﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;

using Lens.Compiler;
using Lens.Resolver;
using Lens.Translations;
using Lens.Utils;

namespace Lens.SyntaxTree.PatternMatching.Rules
{
	/// <summary>
	/// Key-value pair pattern.
	/// </summary>
	internal class MatchKeyValueRule : MatchRuleBase
	{
		#region Fields

		/// <summary>
		/// Key pattern.
		/// </summary>
		public MatchRuleBase KeyRule;

		/// <summary>
		/// Value pattern.
		/// </summary>
		public MatchRuleBase ValueRule;

		/// <summary>
		/// The cached types of key and value.
		/// </summary>
		private Type[] Types;

		#endregion

		#region Resolve

		public override IEnumerable<PatternNameBinding> Resolve(Context ctx, Type expressionType)
		{
			if(!expressionType.IsAppliedVersionOf(typeof(KeyValuePair<,>)))
				Error(CompilerMessages.PatternTypeMismatch, expressionType, typeof(KeyValuePair<,>));

			Types = expressionType.GetGenericArguments();

			return KeyRule.Resolve(ctx, Types[0])
					  .Concat(ValueRule.Resolve(ctx, Types[1]));
		}

		#endregion

		#region Expand

		public override IEnumerable<NodeBase> Expand(Context ctx, NodeBase expression, Label nextStatement)
		{
			foreach (var rule in KeyRule.Expand(ctx, Expr.GetMember(expression, "Key"), nextStatement))
				yield return rule;

			foreach (var rule in ValueRule.Expand(ctx, Expr.GetMember(expression, "Value"), nextStatement))
				yield return rule;
		}

		#endregion
	}
}
