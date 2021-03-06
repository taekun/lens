﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;

using Lens.Compiler;
using Lens.Resolver;
using Lens.SyntaxTree.Expressions.GetSet;
using Lens.SyntaxTree.Literals;
using Lens.SyntaxTree.PatternMatching.Rules;
using Lens.Translations;
using Lens.Utils;

namespace Lens.SyntaxTree.PatternMatching
{
	/// <summary>
	/// The pattern matching base expression.
	/// </summary>
	internal class MatchNode : NodeBase
	{
		#region Constructor

		public MatchNode()
		{
			MatchStatements = new List<MatchStatementNode>();
			_RuleLabels = new List<Tuple<MatchRuleBase, Label>>();
			_ExpressionLabels = new Dictionary<MatchStatementNode, Label>();
		}

		#endregion

		#region Fields

		/// <summary>
		/// The expression to match against rules.
		/// </summary>
		public NodeBase Expression;

		/// <summary>
		/// Match statements to test the expression against.
		/// </summary>
		public List<MatchStatementNode> MatchStatements;

		/// <summary>
		/// The label after all blocks to jump to when a successful match has been found.
		/// </summary>
		public Label EndLabel { get; private set; }

		/// <summary>
		/// The label for default value block (if there is supposed to be one).
		/// </summary>
		private Label _DefaultLabel;

		/// A lookup for rule labels.
		/// </summary>
		private List<Tuple<MatchRuleBase, Label>> _RuleLabels;

		/// <summary>
		/// A lookup for expression labels.
		/// </summary>
		private Dictionary<MatchStatementNode, Label> _ExpressionLabels;

		/// <summary>
		/// Checks if the current node has been resolved as a value-returning context.
		/// </summary>
		private bool _MustReturnValue
		{
			get { return !_CachedExpressionType.IsVoid(); }
		}

		#endregion

		#region Resolve

		protected override Type resolve(Context ctx, bool mustReturn)
		{
			ctx.CheckTypedExpression(Expression, allowNull: true);

			var stmtTypes = new List<Type>(MatchStatements.Count);
			Type commonType = null;
			foreach (var stmt in MatchStatements)
			{
				stmt.ParentNode = this;
				stmtTypes.Add(stmt.Resolve(ctx, mustReturn));

				foreach (var rule in stmt.MatchRules)
				{
					var nameRule = rule as MatchNameRule;
					// detect catch-all expression for a type
					if (nameRule != null && stmt.Condition == null)
					{
						var nameType = nameRule.Type == null ? typeof (object) : ctx.ResolveType(nameRule.Type);
						if (commonType != null && commonType.IsExtendablyAssignableFrom(nameType))
							error(CompilerMessages.PatternUnreachable);

						commonType = nameType;
					}
				}
			}

			return stmtTypes.Any(x => x.IsVoid())
				? typeof(UnitType) 
				: stmtTypes.ToArray().GetMostCommonType();
		}

		#endregion

		#region Transform

		protected override IEnumerable<NodeChild> getChildren()
		{
			yield return new NodeChild(Expression, x => Expression = x);
			foreach(var stmt in MatchStatements)
				yield return new NodeChild(stmt, null);
		}

		protected override NodeBase expand(Context ctx, bool mustReturn)
		{
			defineLabels(ctx);

			var exprType = Expression.Resolve(ctx);

			var block = Expr.Block(ScopeKind.MatchRoot);
			NodeBase exprGetter;
			if (Expression is ILiteralNode || Expression is GetIdentifierNode)
			{
				exprGetter = Expression;
			}
			else
			{
				var tmpVar = ctx.Scope.DeclareImplicit(ctx, exprType, false);
				exprGetter = Expr.Get(tmpVar);
				block.Add(Expr.Set(tmpVar, Expression));
			}

			foreach(var stmt in MatchStatements)
				block.Add(stmt.ExpandRules(ctx, exprGetter, _ExpressionLabels[stmt]));

			if (_MustReturnValue)
			{
				block.Add(
					Expr.Block(
						Expr.JumpLabel(_DefaultLabel),
						Expr.Default(_CachedExpressionType)
					)
				);
			}

			block.Add(Expr.JumpLabel(EndLabel));

			return block;
		}

		#endregion

		#region Label handling

		private void defineLabels(Context ctx)
		{
			foreach (var stmt in MatchStatements)
			{
				_ExpressionLabels.Add(stmt, ctx.CurrentMethod.Generator.DefineLabel());

				foreach (var rule in stmt.MatchRules)
					_RuleLabels.Add(Tuple.Create(rule, ctx.CurrentMethod.Generator.DefineLabel()));
			}

			EndLabel = ctx.CurrentMethod.Generator.DefineLabel();

			if (_MustReturnValue)
				_DefaultLabel = ctx.CurrentMethod.Generator.DefineLabel();
		}

		/// <summary>
		/// Returns the current and the next labels for a match rule.
		/// </summary>
		public MatchRuleLabelSet GetRuleLabels(MatchRuleBase rule)
		{
			var currIndex = _RuleLabels.FindIndex(x => x.Item1 == rule);
			if(currIndex == -1)
				throw new ArgumentException("Rule does not belong to current match block!");

			var next = currIndex < _RuleLabels.Count - 1
				? _RuleLabels[currIndex + 1].Item2
				: (_MustReturnValue ? _DefaultLabel : EndLabel);


			return new MatchRuleLabelSet
			{
				CurrentRule = _RuleLabels[currIndex].Item2,
				NextRule = next
			};
		}

		#endregion
	}

	/// <summary>
	/// The set of labels required for each rule.
	/// </summary>
	internal class MatchRuleLabelSet
	{
		/// <summary>
		/// Label that is emitted before first check so that other rules can jump to it.
		/// </summary>
		public Label CurrentRule;

		/// <summary>
		/// Label to jump to if any of the current rule's checks fail.
		/// </summary>
		public Label NextRule;
	}
}
