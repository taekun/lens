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
	/// Breaks a tuple expression into list of items.
	/// </summary>
	internal class MatchTupleRule : MatchRuleBase
	{
		#region Constructor

		public MatchTupleRule()
		{
			ElementRules = new List<MatchRuleBase>();
		}

		#endregion

		#region Fields

		/// <summary>
		/// Items of the tuple.
		/// </summary>
		public List<MatchRuleBase> ElementRules;

		/// <summary>
		/// The cached list of tuple item types.
		/// </summary>
		private Type[] ItemTypes;

		#endregion

		#region Resovle

		public override IEnumerable<PatternNameBinding> Resolve(Context ctx, Type expressionType)
		{
			if(ElementRules.Count < 1)
				Error(CompilerMessages.PatternTupleTooFewArgs);

			if (ElementRules.Count > 7)
				Error(CompilerMessages.PatternTupleTooManyArgs);

			if(!expressionType.IsTupleType())
				Error(CompilerMessages.PatternTypeMismatch, expressionType, string.Format("Tuple`{0}", ElementRules.Count));

			ItemTypes = expressionType.GetGenericArguments();
			if (ItemTypes.Length != ElementRules.Count)
				Error(CompilerMessages.PatternTypeMismatch, expressionType, string.Format("Tuple`{0}", ElementRules.Count));

			return ElementRules.Select((t, idx) => t.Resolve(ctx, ItemTypes[idx]))
						.SelectMany(subBindings => subBindings);
		}

		#endregion

		#region Expand

		public override IEnumerable<NodeBase> Expand(Context ctx, NodeBase expression, Label nextStatement)
		{
			for (var idx = 0; idx < ElementRules.Count; idx++)
			{
				var fieldName = string.Format("Item{0}", idx + 1);
				var rules = ElementRules[idx].Expand(ctx, Expr.GetMember(expression, fieldName), nextStatement);

				foreach (var rule in rules)
					yield return rule;
			}
		}

		#endregion
	}
}
