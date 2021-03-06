﻿using System;
using System.Collections.Generic;
using Lens.Compiler;
using Lens.Compiler.Entities;
using Lens.Resolver;
using Lens.Translations;
using Lens.Utils;

namespace Lens.SyntaxTree.Expressions.GetSet
{
	/// <summary>
	/// A node representing read access to a local variable or a function.
	/// </summary>
	internal class SetIdentifierNode : IdentifierNodeBase
	{
		#region Constructor

		public SetIdentifierNode(string identifier = null)
		{
			Identifier = identifier;
		}

		#endregion

		#region Fields

		/// <summary>
		/// A flag indicating that assignment to a constant variable is legal
		/// because it's being instantiated.
		/// </summary>
		public bool IsInitialization { get; set; }

		/// <summary>
		/// Value to be assigned.
		/// </summary>
		public NodeBase Value { get; set; }
		
		/// <summary>
		/// Global property reference (if resolved).
		/// </summary>
		private GlobalPropertyInfo _Property;

		#endregion

		#region Resolve

		protected override Type resolve(Context ctx, bool mustReturn)
		{
			if (Identifier == "_")
				error(CompilerMessages.UnderscoreNameUsed);

			var nameInfo = Local ?? ctx.Scope.FindLocal(Identifier);
			if (nameInfo != null)
			{
				if (nameInfo.IsImmutable && !IsInitialization)
					error(CompilerMessages.IdentifierIsConstant, Identifier);
			}
			else
			{
				try
				{
					_Property = ctx.ResolveGlobalProperty(Identifier);

					if (!_Property.HasSetter)
						error(CompilerMessages.GlobalPropertyNoSetter, Identifier);
				}
				catch (KeyNotFoundException)
				{
					error(CompilerMessages.VariableNotFound, Identifier);
				}
			}

			var destType = nameInfo != null ? nameInfo.Type : _Property.PropertyType;
			ensureLambdaInferred(ctx, Value, destType);

			var exprType = Value.Resolve(ctx);
			ctx.CheckTypedExpression(Value, exprType, true);

			if (!destType.IsExtendablyAssignableFrom(exprType))
			{
				error(
					nameInfo != null ? CompilerMessages.IdentifierTypeMismatch : CompilerMessages.GlobalPropertyTypeMismatch,
					exprType,
					destType
				);	
			}

			return base.resolve(ctx, mustReturn);
		}

		#endregion

		#region Transform

		protected override IEnumerable<NodeChild> getChildren()
		{
			yield return new NodeChild(Value, x => Value = x);
		}

		#endregion

		#region Emit

		protected override void emitCode(Context ctx, bool mustReturn)
		{
			var gen = ctx.CurrentMethod.Generator;
			var type = Value.Resolve(ctx);

			if (_Property != null)
			{
				var cast = Expr.Cast(Value, type);
				if (_Property.SetterMethod != null)
				{
					cast.Emit(ctx, true);
					gen.EmitCall(_Property.SetterMethod.MethodInfo);
				}
				else
				{
					var method = typeof (GlobalPropertyHelper).GetMethod("Set").MakeGenericMethod(type);

					gen.EmitConstant(ctx.ContextId);
					gen.EmitConstant(_Property.PropertyId);
					Expr.Cast(Value, type).Emit(ctx, true);
					gen.EmitCall(method);
				}
			}
			else
			{
				var nameInfo = Local ?? ctx.Scope.FindLocal(Identifier);
				if (nameInfo != null)
				{
					if (nameInfo.IsClosured)
					{
						if (nameInfo.ClosureDistance == 0)
							emitSetClosuredLocal(ctx, nameInfo);
						else
							emitSetClosuredRemote(ctx, nameInfo);
					}
					else
					{
						emitSetLocal(ctx, nameInfo);
					}
				}
			}
		}

		/// <summary>
		/// Assigns an ordinary local variable.
		/// </summary>
		private void emitSetLocal(Context ctx, Local name)
		{
			var gen = ctx.CurrentMethod.Generator;

			var castNode = Expr.Cast(Value, name.Type);

			if (!name.IsRefArgument)
			{
				castNode.Emit(ctx, true);
				if(name.ArgumentId.HasValue)
					gen.EmitSaveArgument(name.ArgumentId.Value);
				else
					gen.EmitSaveLocal(name.LocalBuilder);
			}
			else
			{
				gen.EmitLoadArgument(name.ArgumentId.Value);
				castNode.Emit(ctx, true);
				gen.EmitSaveObject(name.Type);
			}
		}

		/// <summary>
		/// Assigns a closured variable that is declared in current scope.
		/// </summary>
		private void emitSetClosuredLocal(Context ctx, Local name)
		{
			var gen = ctx.CurrentMethod.Generator;

			gen.EmitLoadLocal(ctx.Scope.ActiveClosure.ClosureVariable);
			
			Expr.Cast(Value, name.Type).Emit(ctx, true);

			var clsType = ctx.Scope.ActiveClosure.ClosureType.TypeInfo;
			var clsField = ctx.ResolveField(clsType, name.ClosureFieldName);
			gen.EmitSaveField(clsField.FieldInfo);
		}

		/// <summary>
		/// Assigns a closured variable that has been imported from outer scopes.
		/// </summary>
		private void emitSetClosuredRemote(Context ctx, Local name)
		{
			var gen = ctx.CurrentMethod.Generator;

			gen.EmitLoadArgument(0);

			var dist = name.ClosureDistance;
			var type = (Type)ctx.CurrentType.TypeBuilder;
			while (dist > 1)
			{
				var rootField = ctx.ResolveField(type, EntityNames.ParentScopeFieldName);
				gen.EmitLoadField(rootField.FieldInfo);

				type = rootField.FieldType;
				dist--;
			}

			Expr.Cast(Value, name.Type).Emit(ctx, true);

			var clsField = ctx.ResolveField(type, name.ClosureFieldName);
			gen.EmitSaveField(clsField.FieldInfo);
		}

		#endregion

		#region Debug

		protected bool Equals(SetIdentifierNode other)
		{
			return base.Equals(other) && Equals(Value, other.Value);
		}

		public override bool Equals(object obj)
		{
			if (ReferenceEquals(null, obj)) return false;
			if (ReferenceEquals(this, obj)) return true;
			if (obj.GetType() != this.GetType()) return false;
			return Equals((SetIdentifierNode)obj);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				return (base.GetHashCode() * 397) ^ (Value != null ? Value.GetHashCode() : 0);
			}
		}

		public override string ToString()
		{
			return string.Format("set({0} = {1})", Identifier, Value);
		}

		#endregion
	}
}
