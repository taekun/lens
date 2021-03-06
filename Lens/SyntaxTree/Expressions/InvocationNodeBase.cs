﻿using System;
using System.Collections.Generic;
using System.Linq;

using Lens.Compiler;
using Lens.Resolver;
using Lens.SyntaxTree.Declarations.Functions;
using Lens.SyntaxTree.Expressions.GetSet;
using Lens.Utils;

namespace Lens.SyntaxTree.Expressions
{
	/// <summary>
	/// A base class for various forms of method invocation that stores arguments.
	/// </summary>
	abstract internal class InvocationNodeBase : NodeBase
	{
		#region Constructor

		protected InvocationNodeBase()
		{
			Arguments = new List<NodeBase>();
		}

		#endregion

		#region Fields

		/// <summary>
		/// Passed argument expressions.
		/// </summary>
		public List<NodeBase> Arguments { get; set; }

		/// <summary>
		/// Cached callable entity wrapper.
		/// </summary>
		protected abstract CallableWrapperBase _Wrapper { get; }

		/// <summary>
		/// Cached list of argument expression types.
		/// </summary>
		protected Type[] _ArgTypes;

		#endregion

		#region Resolve

		protected override Type resolve(Context ctx, bool mustReturn)
		{
			Func<NodeBase, Type> typeGetter = arg =>
			{
				var gin = arg as GetIdentifierNode;
				if (gin != null && gin.Identifier == "_")
					return typeof (UnspecifiedType);

				return arg.Resolve(ctx);
			};
				
			_ArgTypes = Arguments.Select(typeGetter).ToArray();

			// discard 'unit' pseudoargument
			if (_ArgTypes.Length == 1 && _ArgTypes[0] == typeof (UnitType))
				_ArgTypes = Type.EmptyTypes;

			// prepares arguments only
			return null;
		}

		#endregion

		#region Transform

		protected override IEnumerable<NodeChild> getChildren()
		{
			for (var idx = 0; idx < Arguments.Count; idx++)
			{
				var id = idx;
				var identifier = Arguments[id] as GetIdentifierNode;
				var isPartialArg = identifier != null && identifier.Identifier == "_";
				if (!isPartialArg)
					yield return new NodeChild(Arguments[id], x => Arguments[id] = x);
			}
		}

		protected override NodeBase expand(Context ctx, bool mustReturn)
		{
			if (_Wrapper.IsPartiallyApplied)
			{
				// (expr) _ a b _
				// is transformed into
				// (pa0:T1 pa1:T2) -> (expr) (pa0) (a) (b) (pa1)
				var argDefs = new List<FunctionArgument>();
				var argExprs = new List<NodeBase>();
				for (var idx = 0; idx < _ArgTypes.Length; idx++)
				{
					if (_ArgTypes[idx] == typeof(UnspecifiedType))
					{
						var argName = ctx.Unique.AnonymousArgName();
						argDefs.Add(Expr.Arg(argName, _Wrapper.ArgumentTypes[idx].FullName));
						argExprs.Add(Expr.Get(argName));
					}
					else
					{
						argExprs.Add(Arguments[idx]);
					}
				}

				return Expr.Lambda(argDefs, recreateSelfWithArgs(argExprs));
			}

			if (_Wrapper.IsVariadic)
			{
				var srcTypes = _ArgTypes;
				var dstTypes = _Wrapper.ArgumentTypes;
				var lastDst = dstTypes[dstTypes.Length - 1];
				var lastSrc = srcTypes[srcTypes.Length - 1];

				// compress items into an array:
				//     fx a b c d
				// becomes
				//     fx a b (new[ c as X; d as X ])
				if (dstTypes.Length > srcTypes.Length || lastDst != lastSrc)
				{
					var elemType = lastDst.GetElementType();
					var simpleArgs = Arguments.Take(dstTypes.Length - 1);
					var combined = Expr.Array(Arguments.Skip(dstTypes.Length - 1).Select(x => Expr.Cast(x, elemType)).ToArray());
					return recreateSelfWithArgs(simpleArgs.Union(new[] { combined }));
				}
			}

			return base.expand(ctx, mustReturn);
		}

		/// <summary>
		/// Creates a similar instance of invocation node descendant with replaced arguments list.
		/// </summary>
		protected abstract InvocationNodeBase recreateSelfWithArgs(IEnumerable<NodeBase> newArgs);

		#endregion

		#region Helpers

		/// <summary>
		/// Resolves the expression type in case of partial application.
		/// </summary>
		protected static Type resolvePartial(CallableWrapperBase wrapper, Type returnType, Type[] argTypes)
		{
			if (!wrapper.IsPartiallyApplied)
				return returnType;

			var lambdaArgTypes = new List<Type>();
			for (var idx = 0; idx < argTypes.Length; idx++)
			{
				if (argTypes[idx] == typeof(UnspecifiedType))
					lambdaArgTypes.Add(wrapper.ArgumentTypes[idx]);
			}

			return FunctionalHelper.CreateDelegateType(returnType, lambdaArgTypes.ToArray());
		}

		/// <summary>
		/// Apply inferred types to untyped lambda arguments.
		/// </summary>
		protected void applyLambdaArgTypes(Context ctx)
		{
			for (var idx = 0; idx < _ArgTypes.Length; idx++)
			{
				if (!_ArgTypes[idx].IsLambdaType())
					continue;

				var lambda = (LambdaNode) Arguments[idx];
				if (lambda.MustInferArgTypes)
				{
					var actualWrapper = ReflectionHelper.WrapDelegate(_Wrapper.ArgumentTypes[idx]);
					lambda.SetInferredArgumentTypes(actualWrapper.ArgumentTypes);
					lambda.Resolve(ctx);
				}
			}
		}

		#endregion

		#region Debug

		protected bool Equals(InvocationNodeBase other)
		{
			return Arguments.SequenceEqual(other.Arguments);
		}

		public override bool Equals(object obj)
		{
			if (ReferenceEquals(null, obj)) return false;
			if (ReferenceEquals(this, obj)) return true;
			if (obj.GetType() != this.GetType()) return false;
			return Equals((InvocationNodeBase)obj);
		}

		public override int GetHashCode()
		{
			return (Arguments != null ? Arguments.GetHashCode() : 0);
		}

		#endregion
	}
}
