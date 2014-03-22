﻿using Lens.Compiler;
using Lens.Translations;

namespace Lens.SyntaxTree.Operators
{
	/// <summary>
	/// An operator node that divides one value by another value.
	/// </summary>
	internal class DivideOperatorNode : BinaryOperatorNodeBase
	{
		protected override string OperatorRepresentation
		{
			get { return "/"; }
		}

		protected override string OverloadedMethodName
		{
			get { return "op_Division"; }
		}

		public override NodeBase Expand(Context ctx, bool mustReturn)
		{
			if (RightOperand.IsConstant && RightOperand.ConstantValue == 1)
				return LeftOperand;

			return base.Expand(ctx, mustReturn);
		}

		protected override void compileOperator(Context ctx)
		{
			loadAndConvertNumerics(ctx);
			ctx.CurrentILGenerator.EmitDivide();
		}

		protected override dynamic unrollConstant(dynamic left, dynamic right)
		{
			if(left is int && right is int && right == 0)
				error(CompilerMessages.ConstantDivisionByZero);

			return left/right;
		}
	}
}
