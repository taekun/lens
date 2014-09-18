﻿using System;
using Lens.Compiler;

namespace Lens.SyntaxTree.Literals
{
	/// <summary>
	/// A node to represent the null literal.
	/// </summary>
	internal class NullNode : NodeBase
	{
		#region Resolve

		protected override Type resolve(Context ctx, bool mustReturn)
		{
			return typeof (NullType);
		}

		#endregion

		#region Emit

		protected override void emitCode(Context ctx, bool mustReturn)
		{
			var gen = ctx.CurrentMethod.Generator;
			gen.EmitNull();
		}

		#endregion

		#region Debug

		public override bool Equals(object obj)
		{
			if (ReferenceEquals(null, obj)) return false;
			if (ReferenceEquals(this, obj)) return true;
			return obj.GetType() == GetType();
		}

		public override int GetHashCode()
		{
			return 0;
		}

		public override string ToString()
		{
			return "(null)";
		}

		#endregion
	}
}
