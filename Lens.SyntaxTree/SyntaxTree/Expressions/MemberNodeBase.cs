﻿using Lens.SyntaxTree.Utils;

namespace Lens.SyntaxTree.SyntaxTree.Expressions
{
	/// <summary>
	/// The base node for 
	/// </summary>
	abstract public class MemberNodeBase : NodeBase
	{
		/// <summary>
		/// Expression to access a dynamic member.
		/// </summary>
		public NodeBase Expression { get; set; }

		/// <summary>
		/// Type signature to access a static type.
		/// </summary>
		public TypeSignature StaticType { get; set; }

		/// <summary>
		/// For indeterminate cases.
		/// (eg. A.SomeMember - A may be either a type or a local variable)
		/// </summary>
		public string Identifier { get; set; }

		/// <summary>
		/// The name of the member to access.
		/// </summary>
		public string MemberName { get; set; }

		#region Equality members

		protected bool Equals(MemberNodeBase other)
		{
			return Equals(Expression, other.Expression)
				&& string.Equals(Identifier, other.Identifier)
				&& string.Equals(MemberName, other.MemberName)
				&& Equals(StaticType, other.StaticType);
		}

		public override bool Equals(object obj)
		{
			if (ReferenceEquals(null, obj)) return false;
			if (ReferenceEquals(this, obj)) return true;
			if (obj.GetType() != this.GetType()) return false;
			return Equals((MemberNodeBase)obj);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				int hashCode = (Expression != null ? Expression.GetHashCode() : 0);
				hashCode = (hashCode * 397) ^ (Identifier != null ? Identifier.GetHashCode() : 0);
				hashCode = (hashCode * 397) ^ (MemberName != null ? MemberName.GetHashCode() : 0);
				hashCode = (hashCode * 397) ^ (StaticType != null ? StaticType.GetHashCode() : 0);
				return hashCode;
			}
		}

		#endregion
	}
}
