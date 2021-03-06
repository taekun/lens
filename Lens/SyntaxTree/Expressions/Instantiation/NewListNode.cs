﻿using System;
using System.Collections.Generic;
using System.Linq;
using Lens.Compiler;
using Lens.Resolver;
using Lens.Translations;
using Lens.Utils;

namespace Lens.SyntaxTree.Expressions.Instantiation
{
	/// <summary>
	/// A node representing a new List declaration.
	/// </summary>
	internal class NewListNode : CollectionNodeBase<NodeBase>
	{
		#region Fields

		private Type _ItemType;

		#endregion

		#region Resolve

		protected override Type resolve(Context ctx, bool mustReturn)
		{
			if(Expressions.Count == 0)
				error(CompilerMessages.ListEmpty);

			_ItemType = resolveItemType(Expressions, ctx);
			if(_ItemType == typeof(NullType))
				error(CompilerMessages.ListTypeUnknown);

			return typeof(List<>).MakeGenericType(_ItemType);
		}

		#endregion

		#region Transform

		protected override IEnumerable<NodeChild> getChildren()
		{
			return Expressions.Select((expr, i) => new NodeChild(expr, x => Expressions[i] = x));
		}

		#endregion

		#region Emit

		protected override void emitCode(Context ctx, bool mustReturn)
		{
			var gen = ctx.CurrentMethod.Generator;
			var tmpVar = ctx.Scope.DeclareImplicit(ctx, Resolve(ctx), true);
			
			var listType = Resolve(ctx);
			var ctor = ctx.ResolveConstructor(listType, new[] {typeof (int)});
			var addMethod = ctx.ResolveMethod(listType, "Add", new[] { _ItemType });

			var count = Expressions.Count;
			gen.EmitConstant(count);
			gen.EmitCreateObject(ctor.ConstructorInfo);
			gen.EmitSaveLocal(tmpVar.LocalBuilder);

			foreach (var curr in Expressions)
			{
				var currType = curr.Resolve(ctx);

				ctx.CheckTypedExpression(curr, currType, true);

				if (!_ItemType.IsExtendablyAssignableFrom(currType))
					error(curr, CompilerMessages.ListElementTypeMismatch, currType, _ItemType);

				gen.EmitLoadLocal(tmpVar.LocalBuilder);
				
				Expr.Cast(curr, addMethod.ArgumentTypes[0]).Emit(ctx, true);
				gen.EmitCall(addMethod.MethodInfo);
			}

			gen.EmitLoadLocal(tmpVar.LocalBuilder);
		}

		#endregion

		#region Debug

		public override string ToString()
		{
			return string.Format("list({0})", string.Join(";", Expressions));
		}

		#endregion
	}
}
