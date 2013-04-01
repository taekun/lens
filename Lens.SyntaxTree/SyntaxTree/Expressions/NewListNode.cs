﻿using System;
using System.Collections.Generic;
using Lens.SyntaxTree.Compiler;
using Lens.SyntaxTree.Translations;
using Lens.SyntaxTree.Utils;

namespace Lens.SyntaxTree.SyntaxTree.Expressions
{
	/// <summary>
	/// A node representing a new List declaration.
	/// </summary>
	public class NewListNode : ValueListNodeBase<NodeBase>
	{
		private Type m_ItemType;

		protected override Type resolveExpressionType(Context ctx, bool mustReturn = true)
		{
			if(Expressions.Count == 0)
				Error(Messages.ListEmpty);

			m_ItemType = resolveItemType(Expressions, ctx);
			if (m_ItemType == null)
				Error(Expressions[0], Messages.ListTypeUnknown);

			return typeof(List<>).MakeGenericType(m_ItemType);
		}

		public override IEnumerable<NodeBase> GetChildNodes()
		{
			return Expressions;
		}

		public override void Compile(Context ctx, bool mustReturn)
		{
			var gen = ctx.CurrentILGenerator;
			var tmpVar = ctx.CurrentScope.DeclareImplicitName(ctx, GetExpressionType(ctx), true);
			
			var listType = GetExpressionType(ctx);
			var ctor = ctx.ResolveConstructor(listType, new[] {typeof (int)});
			var addMethod = ctx.ResolveMethod(listType, "Add", new[] { m_ItemType });

			var count = Expressions.Count;
			gen.EmitConstant(count);
			gen.EmitCreateObject(ctor.ConstructorInfo);
			gen.EmitSaveLocal(tmpVar);

			foreach (var curr in Expressions)
			{
				var currType = curr.GetExpressionType(ctx);

				ctx.CheckTypedExpression(curr, currType, true);

				if (!m_ItemType.IsExtendablyAssignableFrom(currType))
					Error(curr, Messages.ListElementTypeMismatch, currType, m_ItemType);

				gen.EmitLoadLocal(tmpVar);
				
				Expr.Cast(curr, addMethod.ArgumentTypes[0]).Compile(ctx, true);
				gen.EmitCall(addMethod.MethodInfo);
			}

			gen.EmitLoadLocal(tmpVar);
		}

		public override string ToString()
		{
			return string.Format("list({0})", string.Join(";", Expressions));
		}
	}
}
