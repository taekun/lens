﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using Lens.Compiler;
using Lens.Resolver;
using Lens.Translations;
using Lens.Utils;

namespace Lens.SyntaxTree.Expressions.GetSet
{
	/// <summary>
	/// A node representing read access to a member of a type, either field or property.
	/// </summary>
	internal class GetMemberNode : MemberNodeBase, IPointerProvider
	{
		#region Constructor

		public GetMemberNode()
		{
			TypeHints = new List<TypeSignature>();
		}

		#endregion

		#region Fields

		private Type _Type;
		private FieldWrapper _Field;
		private MethodWrapper _Method;
		private PropertyWrapper _Property;

		private bool _IsStatic;

		public bool PointerRequired { get; set; }
		public bool RefArgumentRequired { get; set; }

		/// <summary>
		/// The list of type signatures if the given identifier is a method.
		/// </summary>
		public List<TypeSignature> TypeHints { get; set; }

		#endregion

		#region Resolve

		protected override Type resolve(Context ctx, bool mustReturn = true)
		{
			resolveSelf(ctx);

			if (_Type != null)
				checkTypeInSafeMode(ctx, _Type);

			if (Expression != null && Expression.Resolve(ctx).IsArray && MemberName == "Length")
				return typeof (int);

			if (_Field != null)
				return _Field.FieldType;

			if (_Property != null)
				return _Property.PropertyType;

			return _Method.ReturnType.IsVoid()
				? FunctionalHelper.CreateActionType(_Method.ArgumentTypes)
				: FunctionalHelper.CreateFuncType(_Method.ReturnType, _Method.ArgumentTypes);
		}

		/// <summary>
		/// Attempts to resolve current node and sets either of the following fields:
		/// _Field, _Method, _Property
		/// 
		/// The following fields are also set:
		/// _Type, _Static
		/// </summary>
		private void resolveSelf(Context ctx)
		{
			Action check = () =>
			{
				if (Expression == null && !_IsStatic)
					error(CompilerMessages.DynamicMemberFromStaticContext, _Type, MemberName);

				if (_Method == null && TypeHints.Count > 0)
					error(CompilerMessages.TypeArgumentsForNonMethod, _Type, MemberName);
			};

			_Type = StaticType != null
				? ctx.ResolveType(StaticType)
				: Expression.Resolve(ctx);

			// special case: array length
			if (_Type.IsArray && MemberName == "Length")
			{
				check();
				return;
			}

			// check for field
			try
			{
				_Field = ctx.ResolveField(_Type, MemberName);
				_IsStatic = _Field.IsStatic;

				check();
				return;
			}
			catch (KeyNotFoundException) { }

			// check for property
			try
			{
				_Property = ctx.ResolveProperty(_Type, MemberName);

				if(!_Property.CanGet)
					error(CompilerMessages.PropertyNoGetter, _Type, MemberName);

				_IsStatic = _Property.IsStatic;

				check();
				return;
			}
			catch (KeyNotFoundException) { }

			// check for event: events are only allowed at the left side of += and -=
			try
			{
				ctx.ResolveEvent(_Type, MemberName);
				error(CompilerMessages.EventAsExpr);
			}
			catch (KeyNotFoundException) { }

			// find method
			var argTypes = TypeHints.Select(t => t.FullSignature == "_" ? null : ctx.ResolveType(t)).ToArray();
			var methods = ctx.ResolveMethodGroup(_Type, MemberName).Where(m => checkMethodArgs(argTypes, m)).ToArray();

			if (methods.Length == 0)
				error(argTypes.Length == 0 ? CompilerMessages.TypeIdentifierNotFound : CompilerMessages.TypeMethodNotFound, _Type.Name, MemberName);

			if (methods.Length > 1)
				error(CompilerMessages.TypeMethodAmbiguous, _Type.Name, MemberName);

			_Method = methods[0];
			if (_Method.ArgumentTypes.Length > 16)
				error(CompilerMessages.CallableTooManyArguments);

			_IsStatic = _Method.IsStatic;

			check();
		}

		private static bool checkMethodArgs(Type[] argTypes, MethodWrapper method)
		{
			if(argTypes.Length == 0)
				return true;

			if (method.ArgumentTypes.Length != argTypes.Length)
				return false;

			return !method.ArgumentTypes.Where((p, idx) => argTypes[idx] != null && p != argTypes[idx]).Any();
		}

		#endregion

		#region Transform

		protected override IEnumerable<NodeChild> getChildren()
		{
			yield return new NodeChild(Expression, x => Expression = x);
		}

		#endregion

		#region Emit

		protected override void emitCode(Context ctx, bool mustReturn)
		{
			var gen = ctx.CurrentMethod.Generator;
			
			if (!_IsStatic)
			{
				Expression.EmitNodeForAccess(ctx);

				if (MemberName == "Length" && Expression.Resolve(ctx).IsArray)
				{
					gen.EmitGetArrayLength();
					return;
				}
			}

			if (_Field != null)
				emitField(gen);

			else if (_Property != null)
				emitProperty(ctx, gen);

			if (_Method != null)
				emitMethod(ctx, gen);
		}

		/// <summary>
		/// Emits code for loading a field (possibly constant).
		/// </summary>
		private void emitField(ILGenerator gen)
		{
			if (_Field.IsLiteral)
			{
				var fieldType = _Field.FieldType;
				var dataType = fieldType.IsEnum ? Enum.GetUnderlyingType(fieldType) : fieldType;

				var value = _Field.FieldInfo.GetValue(null);

				if (dataType == typeof(int))
					gen.EmitConstant((int)value);
				else if (dataType == typeof(long))
					gen.EmitConstant((long)value);
				else if (dataType == typeof(double))
					gen.EmitConstant((double)value);
				else if (dataType == typeof(float))
					gen.EmitConstant((float)value);

				else if (dataType == typeof(uint))
					gen.EmitConstant(unchecked((int)(uint)value));
				else if (dataType == typeof(ulong))
					gen.EmitConstant(unchecked((long)(ulong)value));

				else if (dataType == typeof(byte))
					gen.EmitConstant((byte)value);
				else if (dataType == typeof(sbyte))
					gen.EmitConstant((sbyte)value);
				else if (dataType == typeof(short))
					gen.EmitConstant((short)value);
				else if (dataType == typeof(ushort))
					gen.EmitConstant((ushort)value);
				else if (dataType == typeof(string))
					gen.EmitConstant((string)value);
				else
					throw new NotImplementedException("Unknown literal field type!");
			}
			else
			{
				gen.EmitLoadField(_Field.FieldInfo, PointerRequired || RefArgumentRequired);
			}
		}

		/// <summary>
		/// Emits code for loading a property value.
		/// </summary>
		private void emitProperty(Context ctx, ILGenerator gen)
		{
			if (_Property.PropertyType.IsValueType && RefArgumentRequired)
				error(CompilerMessages.PropertyValuetypeRef, _Property.Type, MemberName, _Property.PropertyType);

			gen.EmitCall(_Property.Getter);

			if (PointerRequired)
			{
				var tmpVar = ctx.Scope.DeclareImplicit(ctx, _Property.PropertyType, false);
				gen.EmitSaveLocal(tmpVar.LocalBuilder);
				gen.EmitLoadLocal(tmpVar.LocalBuilder, true);
			}
		}

		/// <summary>
		/// Emits code for getting the method as a delegate instance.
		/// </summary>
		private void emitMethod(Context ctx, ILGenerator gen)
		{
			if (RefArgumentRequired)
				error(CompilerMessages.MethodRef);

			if (_IsStatic)
				gen.EmitNull();

			var retType = _Method.ReturnType;
			var type = retType.IsVoid()
				? FunctionalHelper.CreateActionType(_Method.ArgumentTypes)
				: FunctionalHelper.CreateFuncType(retType, _Method.ArgumentTypes);

			var ctor = ctx.ResolveConstructor(type, new [] { typeof(object), typeof(IntPtr) });
			gen.EmitLoadFunctionPointer(_Method.MethodInfo);
			gen.EmitCreateObject(ctor.ConstructorInfo);
		}

		#endregion

		#region Debug

		protected bool Equals(GetMemberNode other)
		{
			return base.Equals(other)
				   && PointerRequired.Equals(other.PointerRequired)
				   && RefArgumentRequired.Equals(other.RefArgumentRequired)
				   && TypeHints.SequenceEqual(other.TypeHints);
		}

		public override bool Equals(object obj)
		{
			if (ReferenceEquals(null, obj)) return false;
			if (ReferenceEquals(this, obj)) return true;
			if (obj.GetType() != this.GetType()) return false;
			return Equals((GetMemberNode)obj);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				int hashCode = base.GetHashCode();
				hashCode = (hashCode * 397) ^ PointerRequired.GetHashCode();
				hashCode = (hashCode * 397) ^ RefArgumentRequired.GetHashCode();
				hashCode = (hashCode * 397) ^ (TypeHints != null ? TypeHints.GetHashCode() : 0);
				return hashCode;
			}
		}

		public override string ToString()
		{
			var typehints = TypeHints.Any() ? "<" + string.Join(", ", TypeHints) + ">" : string.Empty;
			return StaticType == null
				? string.Format("getmbr({0}{1} of value {2})", MemberName, typehints, Expression)
				: string.Format("getmbr({0}{1} of type {2})", MemberName, typehints, StaticType);
		}

		#endregion
	}
}
