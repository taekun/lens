﻿using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using Lens.Compiler;
using Lens.Lexer;
using Lens.SyntaxTree;
using Lens.SyntaxTree.ControlFlow;
using Lens.SyntaxTree.Declarations;
using Lens.SyntaxTree.Declarations.Functions;
using Lens.SyntaxTree.Declarations.Locals;
using Lens.SyntaxTree.Declarations.Types;
using Lens.SyntaxTree.Expressions;
using Lens.SyntaxTree.Expressions.GetSet;
using Lens.SyntaxTree.Expressions.Instantiation;
using Lens.SyntaxTree.Literals;
using Lens.SyntaxTree.Operators.TypeBased;
using Lens.Translations;

namespace Lens.Parser
{
	using System.Data;

	using Lens.SyntaxTree.PatternMatching;
	using Lens.SyntaxTree.PatternMatching.Rules;


	internal partial class LensParser
	{
		#region Constructor

		public LensParser(IEnumerable<Lexem> lexems)
		{
			_Lexems = lexems.ToArray();

			Nodes = parseMain().ToList();
		}

		#endregion

		#region Fields

		/// <summary>
		/// Generated list of nodes.
		/// </summary>
		public List<NodeBase> Nodes { get; private set; }

		/// <summary>
		/// Source list of lexems.
		/// </summary>
		private readonly Lexem[] _Lexems;

		/// <summary>
		/// Current index of the lexem in the stream.
		/// </summary>
		private int _LexemId;

		#endregion

		#region Globals

		/// <summary>
		/// main                                        = stmt { NL stmt } EOF
		/// </summary>
		private IEnumerable<NodeBase> parseMain()
		{
			yield return parseStmt();

			while (!check(LexemType.EOF))
			{
				if (!isStmtSeparator())
				{
					if(peek(LexemType.Assign))
						error(ParserMessages.AssignLvalueExpected);
					else
						error(ParserMessages.NewlineSeparatorExpected);
				}

				yield return parseStmt();
			}
		}

		/// <summary>
		/// stmt                                        = using | record_def | type_def | fun_def | local_stmt
		/// </summary>
		private NodeBase parseStmt()
		{
			return attempt(parseUsing)
				   ?? attempt(parseRecordDef)
				   ?? attempt(parseTypeDef)
				   ?? attempt(parseFunDef)
				   ?? ensure(parseLocalStmt, ParserMessages.UnknownStatement);
		}

		#endregion

		#region Namespace & type signatures

		/// <summary>
		/// namespace                                   = identifier { "." identifier }
		/// </summary>
		private TypeSignature parseNamespace()
		{
			return bind(() =>
				{
					if (!peek(LexemType.Identifier))
						return null;

					var identifier = getValue();
					if (!peek(LexemType.Dot))
						return new TypeSignature(identifier);

					var sb = new StringBuilder(identifier);
					while (check(LexemType.Dot))
					{
						identifier = ensure(LexemType.Identifier, ParserMessages.IdentifierExpected).Value;
						sb.Append(".");
						sb.Append(identifier);
					}

					return new TypeSignature(sb.ToString());
				}
			);
		}

		/// <summary>
		/// type                                        = namespace [ type_args ] { "[]" | "?" | "~" }
		/// </summary>
		private TypeSignature parseType()
		{
			var node = attempt(parseNamespace);
			if (node == null)
				return null;

			var args = attempt(parseTypeArgs);
			if(args != null)
				node = new TypeSignature(node.Name, args.ToArray());

			while (true)
			{
				if (peek(LexemType.SquareOpen, LexemType.SquareClose))
				{
					node = new TypeSignature(null, "[]", node);
					skip(2);
				}

				else if (check(LexemType.Tilde))
					node = new TypeSignature(null, "~", node);

				else if (check(LexemType.QuestionMark))
					node = new TypeSignature(null, "?", node);

				else
					return node;
			}
		}

		/// <summary>
		/// type_args                                   = "<" type { "," type } ">"
		/// </summary>
		private List<TypeSignature> parseTypeArgs()
		{
			if (!check(LexemType.Less))
				return null;

			var arg = attempt(parseType);
			if (arg == null)
				return null;

			if (!peekAny(new[] {LexemType.Comma, LexemType.Greater}))
				return null;

			var list = new List<TypeSignature> {arg};
			while (check(LexemType.Comma))
				list.Add(ensure(parseType, ParserMessages.TypeArgumentExpected));

			ensure(LexemType.Greater, ParserMessages.SymbolExpected, '>');
			return list;
		}

		#endregion

		#region Structures

		/// <summary>
		/// using                                       = "using" namespace NL
		/// </summary>
		private UseNode parseUsing()
		{
			if (!check(LexemType.Use))
				return null;

			var nsp = ensure(parseNamespace, ParserMessages.NamespaceExpected);
			var node = new UseNode {Namespace = nsp.FullSignature};

			return node;
		}

		/// <summary>
		/// record_def                                  = "record" identifier INDENT record_stmt { NL record_stmt } DEDENT
		/// </summary>
		private RecordDefinitionNode parseRecordDef()
		{
			if (!check(LexemType.Record))
				return null;

			var node = new RecordDefinitionNode();

			node.Name = ensure(LexemType.Identifier, ParserMessages.RecordIdentifierExpected).Value;
			ensure(LexemType.Indent, ParserMessages.RecordIndentExpected);
			
			var field = bind(parseRecordStmt);
			node.Entries.Add(field);

			while (!check(LexemType.Dedent))
			{
				ensure(LexemType.NewLine, ParserMessages.RecordSeparatorExpected);
				field = bind(parseRecordStmt);
				node.Entries.Add(field);
			}

			return node;
		}

		/// <summary>
		/// record_stmt                                 = identifier ":" type
		/// </summary>
		private RecordField parseRecordStmt()
		{
			var node = new RecordField();

			node.Name = ensure(LexemType.Identifier, ParserMessages.RecordFieldIdentifierExpected).Value;
			ensure(LexemType.Colon, ParserMessages.SymbolExpected, ':');
			node.Type = ensure(parseType, ParserMessages.RecordFieldTypeExpected);

			return node;
		}

		/// <summary>
		/// type_def                                    = "type" identifier INDENT type_stmt { type_stmt } DEDENT
		/// </summary>
		private TypeDefinitionNode parseTypeDef()
		{
			if (!check(LexemType.Type))
				return null;

			var node = new TypeDefinitionNode();

			node.Name = ensure(LexemType.Identifier, ParserMessages.TypeIdentifierExpected).Value;
			ensure(LexemType.Indent, ParserMessages.TypeIndentExpected);

			var field = bind(parseTypeStmt);
			node.Entries.Add(field);

			while (!check(LexemType.Dedent))
			{
				ensure(LexemType.NewLine, ParserMessages.TypeSeparatorExpected);
				field = bind(parseTypeStmt);
				node.Entries.Add(field);
			}

			return node;
		}

		/// <summary>
		/// type_stmt                                   = identifier [ "of" type ]
		/// </summary>
		private TypeLabel parseTypeStmt()
		{
			var node = new TypeLabel();

			node.Name = ensure(LexemType.Identifier, ParserMessages.TypeLabelIdentifierExpected).Value;
			if (check(LexemType.Of))
				node.TagType = ensure(parseType, ParserMessages.TypeLabelTagTypeExpected);

			return node;
		}

		/// <summary>
		/// fun_def                                     = [ "pure" ] "fun" identifier [ ":" type ] fun_args "->" block
		/// </summary>
		private FunctionNode parseFunDef()
		{
			var node = new FunctionNode();
			node.IsPure = check(LexemType.Pure);

			if (!check(LexemType.Fun))
			{
				if (node.IsPure)
					error(ParserMessages.FunctionDefExpected);
				else
					return null;
			}

			node.Name = ensure(LexemType.Identifier, ParserMessages.FunctionIdentifierExpected).Value;
			if (check(LexemType.Colon))
				node.ReturnTypeSignature = ensure(parseType, ParserMessages.FunctionReturnExpected);

			node.Arguments = attempt(() => parseFunArgs(true)) ?? new List<FunctionArgument>();
			ensure(LexemType.Arrow, ParserMessages.SymbolExpected, "->");
			node.Body.LoadFrom(ensure(parseBlock, ParserMessages.FunctionBodyExpected));

			return node;
		}

		/// <summary>
		/// fun_args                                    = fun_single_arg | fun_many_args
		/// </summary>
		private List<FunctionArgument> parseFunArgs(bool required = false)
		{
			var single = attempt(() => parseFunSingleArg(required));
			if (single != null)
				return new List<FunctionArgument> {single};

			return parseFunManyArgs(required);
		}

		/// <summary>
		/// fun_arg                                     = identifier [ ":" [ "ref" ] type [ "... " ] ]
		/// </summary>
		private FunctionArgument parseFunSingleArg(bool required = false)
		{
			if (!peek(LexemType.Identifier))
				return null;

			var node = new FunctionArgument();
			node.Name = getValue();

			if (!check(LexemType.Colon))
			{
				if (required)
					error(ParserMessages.SymbolExpected, ":");

				node.Type = typeof (UnspecifiedType);
				return node;
			}

			node.IsRefArgument = check(LexemType.Ref);
			node.TypeSignature = ensure(parseType, ParserMessages.ArgTypeExpected);

			if (check(LexemType.Ellipsis))
			{
				if(node.IsRefArgument)
					error(ParserMessages.VariadicByRef);

				node.IsVariadic = true;
			}

			return node;
		}

		/// <summary>
		/// fun_arg_list                                = "(" { fun_single_arg } ")"
		/// </summary>
		private List<FunctionArgument> parseFunManyArgs(bool required = false)
		{
			if (!check(LexemType.ParenOpen))
				return null;

			var args = new List<FunctionArgument>();
			while (!check(LexemType.ParenClose))
			{
				var arg = attempt(() => parseFunSingleArg(required));
				if (arg == null)
					return null;

				args.Add(arg);
			}

			return args;
		}

		#endregion

		#region Blocks

		/// <summary>
		/// block                                       = local_stmt_list | local_stmt
		/// </summary>
		private CodeBlockNode parseBlock()
		{
			var many = parseLocalStmtList().ToList();
			if (many.Count > 0)
				return new CodeBlockNode { Statements = many };

			var single = parseLocalStmt();
			if (single != null)
				return new CodeBlockNode { single };

			return null;
		}

		/// <summary>
		/// local_stmt_list                             = INDENT local_stmt { NL local_stmt } DEDENT
		/// </summary>
		private IEnumerable<NodeBase> parseLocalStmtList()
		{
			if (!check(LexemType.Indent))
				yield break;

			yield return ensure(parseLocalStmt, ParserMessages.ExpressionExpected);

			while (!check(LexemType.Dedent))
			{
				if(!isStmtSeparator())
					error(ParserMessages.NewlineSeparatorExpected);
				yield return ensure(parseLocalStmt, ParserMessages.ExpressionExpected);
			}
		}

		/// <summary>
		/// local_stmt                                  = name_def_stmt | set_stmt | expr
		/// </summary>
		private NodeBase parseLocalStmt()
		{
			if(peek(LexemType.PassLeft))
				error(ParserMessages.ArgumentPassIndentExpected);

			if (peek(LexemType.PassRight))
				error(ParserMessages.MethodPassIndentExpected);

			return attempt(parseNameDefStmt)
				   ?? attempt(parseSetStmt)
				   ?? attempt(parseExpr);
		}

		#endregion

		#region Let & var

		/// <summary>
		/// name_def_stmt                               = var_stmt | let_stmt
		/// </summary>
		private NameDeclarationNodeBase parseNameDefStmt()
		{
			return attempt(parseVarStmt)
				   ?? attempt(parseLetStmt) as NameDeclarationNodeBase;
		}

		/// <summary>
		/// var_stmt                                    = "var" identifier ( "=" expr | ":" type )
		/// </summary>
		private VarNode parseVarStmt()
		{
			if (!check(LexemType.Var))
				return null;

			var node = new VarNode();
			node.Name = ensure(LexemType.Identifier, ParserMessages.VarIdentifierExpected).Value;
			if (check(LexemType.Colon))
				node.Type = ensure(parseType, ParserMessages.VarTypeExpected);
			else if(check(LexemType.Assign))
				node.Value = ensure(parseExpr, ParserMessages.InitExpressionExpected);
			else
				error(ParserMessages.InitExpressionOrTypeExpected);

			return node;
		}

		/// <summary>
		/// let_stmt                                    = "let" identifier "=" expr
		/// </summary>
		private LetNode parseLetStmt()
		{
			if (!check(LexemType.Let))
				return null;

			var node = new LetNode();
			node.Name = ensure(LexemType.Identifier, ParserMessages.VarIdentifierExpected).Value;
			ensure(LexemType.Assign, ParserMessages.SymbolExpected, '=');
			node.Value = ensure(parseExpr, ParserMessages.InitExpressionExpected);

			return node;
		}

		#endregion

		#region Assignment

		/// <summary>
		/// set_stmt                                    = set_id_stmt | set_stmbr_stmt | set_any_stmt
		/// </summary>
		private NodeBase parseSetStmt()
		{
			return attempt(parseSetIdStmt)
				   ?? attempt(parseSetStmbrStmt)
				   ?? attempt(parseSetAnyStmt);
		}

		/// <summary>
		/// set_id_stmt                                 = identifier assignment_op expr
		/// </summary>
		private NodeBase parseSetIdStmt()
		{
			if (!peek(LexemType.Identifier))
				return null;

			var node = new SetIdentifierNode();
			node.Identifier = getValue();

			if (check(LexemType.Assign))
			{
				node.Value = ensure(parseExpr, ParserMessages.ExpressionExpected);
				return node;
			}

			if (peekAny(_BinaryOperators) && peek(1, LexemType.Assign))
			{
				var opType = _Lexems[_LexemId].Type;
				skip(2);
				node.Value = ensure(parseExpr, ParserMessages.ExpressionExpected);
				return new ShortAssignmentNode(opType, node);
			}

			return null;
		}

		/// <summary>
		/// set_stmbr_stmt                              = type "::" identifier assignment_op expr
		/// </summary>
		private NodeBase parseSetStmbrStmt()
		{
			var type = attempt(parseType);
			if (type == null)
				return null;

			if (!check(LexemType.DoubleСolon))
				return null;

			var node = new SetMemberNode();
			node.StaticType = type;
			node.MemberName = ensure(LexemType.Identifier, ParserMessages.MemberNameExpected).Value;

			if (check(LexemType.Assign))
			{
				node.Value = ensure(parseExpr, ParserMessages.ExpressionExpected);
				return node;
			}

			if (peekAny(_BinaryOperators) && peek(1, LexemType.Assign))
			{
				var opType = _Lexems[_LexemId].Type;
				skip(2);
				node.Value = ensure(parseExpr, ParserMessages.ExpressionExpected);
				return new ShortAssignmentNode(opType, node);
			}

			return null;
		}

		/// <summary>
		/// set_any_stmt                                = lvalue_expr assignment_op expr
		/// </summary>
		private NodeBase parseSetAnyStmt()
		{
			var node = attempt(parseLvalueExpr);
			if (node == null)
				return null;

			if (check(LexemType.Assign))
			{
				var expr = ensure(parseExpr, ParserMessages.AssignExpressionExpected);
				return makeSetter(node, expr);
			}

			if (peekAny(_BinaryOperators) && peek(1, LexemType.Assign))
			{
				var opType = _Lexems[_LexemId].Type;
				skip(2);
				var expr = ensure(parseExpr, ParserMessages.AssignExpressionExpected);
				return new ShortAssignmentNode(opType, makeSetter(node, expr));
			}

			return null;
		}

		#endregion

		#region Lvalues

		/// <summary>
		/// lvalue_expr                                 = lvalue_name_expr | lvalue_paren_expr
		/// </summary>
		private NodeBase parseLvalueExpr()
		{
			return attempt(parseLvalueNameExpr)
				   ?? attempt(parseLvalueParenExpr);
		}

		/// <summary>
		/// lvalue_name_expr                            = lvalue_name { accessor }
		/// </summary>
		private NodeBase parseLvalueNameExpr()
		{
			var node = attempt(parseLvalueName);
			if (node == null)
				return null;

			while (true)
			{
				var acc = attempt(parseAccessor);
				if (acc == null)
					return node;

				node = attachAccessor(node, acc);
			}
		}

		/// <summary>
		/// lvalue_paren_expr                           = paren_expr accessor { accessor }
		/// </summary>
		private NodeBase parseLvalueParenExpr()
		{
			var node = attempt(parseParenExpr);
			if (node == null)
				return null;

			var acc = attempt(parseAccessor);
			if (acc == null)
				return null;

			node = attachAccessor(node, acc);
			while (true)
			{
				acc = attempt(parseAccessor);
				if (acc == null)
					return node;

				node = attachAccessor(node, acc);
			}
		}

		/// <summary>
		/// lvalue_name                                 = lvalue_stmbr_expr | lvalue_id_expr
		/// </summary>
		private NodeBase parseLvalueName()
		{
			return attempt(parseLvalueStmbrExpr)
				   ?? attempt(parseLvalueIdExpr) as NodeBase;
		}

		/// <summary>
		/// lvalue_stmbr_expr                           = type "::" identifier
		/// </summary>
		private GetMemberNode parseLvalueStmbrExpr()
		{
			var type = attempt(parseType);
			if (type == null || !check(LexemType.DoubleСolon))
				return null;

			var node = new GetMemberNode();
			node.StaticType = type;
			node.MemberName = ensure(LexemType.Identifier, ParserMessages.MemberNameExpected).Value;
			return node;
		}

		/// <summary>
		/// lvalue_id_expr                              = identifier
		/// </summary>
		private GetIdentifierNode parseLvalueIdExpr()
		{
			if (!peek(LexemType.Identifier))
				return null;

			return new GetIdentifierNode(getValue());
		}

		#endregion

		#region Accessors

		/// <summary>
		/// get_expr                                    = atom { accessor }
		/// </summary>
		private NodeBase parseGetExpr()
		{
			var node = attempt(parseAtom);
			if (node == null)
				return null;

			while (true)
			{
				var acc = attempt(parseAccessor);
				if (acc == null)
					return node;

				node = attachAccessor(node, acc);
			}
		}

		/// <summary>
		/// get_id_expr                                 = identifier [ type_args ]
		/// </summary>
		private GetIdentifierNode parseGetIdExpr()
		{
			var node = attempt(parseLvalueIdExpr);
			return node;

			// todo: type args
		}

		/// <summary>
		/// get_stmbr_expr                              = type "::" identifier [ type_args ]
		/// </summary>
		private GetMemberNode parseGetStmbrExpr()
		{
			var node = attempt(parseLvalueStmbrExpr);
			if (node == null)
				return null;

			var hints = attempt(parseTypeArgs);
			if (hints != null)
				node.TypeHints = hints;

			return node;
		}

		/// <summary>
		/// accessor                                    = accessor_idx | accessor_mbr
		/// </summary>
		private NodeBase parseAccessor()
		{
			return attempt(parseAccessorIdx)
				   ?? attempt(parseAccessorMbr) as NodeBase;
		}

		/// <summary>
		/// accessor_idx                                = "[" line_expr "]"
		/// </summary>
		private GetIndexNode parseAccessorIdx()
		{
			if (!check(LexemType.SquareOpen))
				return null;

			var node = new GetIndexNode();
			node.Index = ensure(parseLineExpr, ParserMessages.IndexExpressionExpected);
			ensure(LexemType.SquareClose, ParserMessages.SymbolExpected, ']');
			return node;
		}

		/// <summary>
		/// accessor_mbr                                = "." identifier [ type_args ]
		/// </summary>
		private GetMemberNode parseAccessorMbr()
		{
			if (!check(LexemType.Dot))
				return null;

			var node = new GetMemberNode();
			node.MemberName = ensure(LexemType.Identifier, ParserMessages.MemberNameExpected).Value;

			var args = attempt(parseTypeArgs);
			if (args != null)
				node.TypeHints = args;

			return node;
		}

		#endregion

		#region Expression root

		/// <summary>
		/// expr                                        = block_expr | line_expr
		/// </summary>
		private NodeBase parseExpr()
		{
			return attempt(parseBlockExpr)
				   ?? attempt(parseLineExpr);
		}

		#endregion

		#region Block control structures

		/// <summary>
		/// block_expr                                  = if_block | while_block | for_block | using_block | try_stmt | match_block | new_block_expr | invoke_block_expr | invoke_block_pass_expr | lambda_block_expr
		/// </summary>
		private NodeBase parseBlockExpr()
		{
			return attempt(parseIfBlock)
				   ?? attempt(parseWhileBlock)
				   ?? attempt(parseForBlock)
				   ?? attempt(parseUsingBlock)
				   ?? attempt(parseTryStmt)
				   ?? attempt(parseMatchBlock)
				   ?? attempt(parseNewBlockExpr)
				   ?? attempt(parseInvokeBlockExpr)
				   ?? attempt(parseInvokeBlockPassExpr)
				   ?? attempt(parseLambdaBlockExpr) as NodeBase;
		}

		/// <summary>
		/// if_block                                    = if_header block [ NL "else" block ]
		/// </summary>
		private IfNode parseIfBlock()
		{
			var node = attempt(parseIfHeader);
			if (node == null)
				return null;

			node.TrueAction = ensure(parseBlock, ParserMessages.ConditionBlockExpected);
			if (check(LexemType.Else))
				node.FalseAction = ensure(parseBlock, ParserMessages.CodeBlockExpected);

			return node;
		}

		/// <summary>
		/// while_block                                 = while_header block
		/// </summary>
		private WhileNode parseWhileBlock()
		{
			var node = attempt(parseWhileHeader);
			if (node == null)
				return null;

			node.Body.LoadFrom(ensure(parseBlock, ParserMessages.LoopBodyExpected));
			return node;
		}

		/// <summary>
		/// for_block                                   = for_header block
		/// </summary>
		private ForeachNode parseForBlock()
		{
			var node = attempt(parseForHeader);
			if (node == null)
				return null;

			node.Body = ensure(parseBlock, ParserMessages.LoopBodyExpected);
			return node;
		}

		/// <summary>
		/// using_block                                 = using_header block
		/// </summary>
		private UsingNode parseUsingBlock()
		{
			var node = attempt(parseUsingHeader);
			if (node == null)
				return null;

			node.Body = ensure(parseBlock, ParserMessages.UsingBodyExpected);
			return node;
		}

		/// <summary>
		/// try_stmt                                    = "try" block catch_stmt_list [ finally_stmt ]
		/// </summary>
		private TryNode parseTryStmt()
		{
			if (!check(LexemType.Try))
				return null;

			var node = new TryNode();
			node.Code = ensure(parseBlock, ParserMessages.TryBlockExpected);
			node.CatchClauses = parseCatchStmtList().ToList();
			node.Finally = attempt(parseFinallyStmt);

			if (node.Finally == null && node.CatchClauses.Count == 0)
				error(ParserMessages.CatchExpected);

			return node;
		}

		/// <summary>
		/// catch_stmt_list                             = catch_stmt { catch_stmt }
		/// </summary>
		private IEnumerable<CatchNode> parseCatchStmtList()
		{
			while (peek(LexemType.Catch))
				yield return parseCatchStmt();
		}

		/// <summary>
		/// catch_stmt                                  = "catch" [ identifier ":" type ] block
		/// </summary>
		private CatchNode parseCatchStmt()
		{
			if (!check(LexemType.Catch))
				return null;

			var node = new CatchNode();
			if (peek(LexemType.Identifier))
			{
				node.ExceptionVariable = getValue();
				ensure(LexemType.Colon, ParserMessages.SymbolExpected, ':');
				node.ExceptionType = ensure(parseType, ParserMessages.ExceptionTypeExpected);
			}

			node.Code = ensure(parseBlock, ParserMessages.ExceptionHandlerExpected);
			return node;
		}

		/// <summary>
		/// finally_stmt                                = "finally" block
		/// </summary>
		private CodeBlockNode parseFinallyStmt()
		{
			if (!check(LexemType.Finally))
				return null;

			var block = parseBlock();
			return block;
		}

		/// <summary>
		/// lambda_block_expr                           = [ fun_args ] "->" block
		/// </summary>
		private LambdaNode parseLambdaBlockExpr()
		{
			var node = new LambdaNode();
			node.Arguments = attempt(() => parseFunArgs()) ?? new List<FunctionArgument>();
			
			if (!check(LexemType.Arrow))
				return null;

			node.Body.LoadFrom(ensure(parseBlock, ParserMessages.FunctionBodyExpected));
			return node;
		}

		#endregion

		#region Headers

		/// <summary>
		/// if_header                                   = "if" line_expr "then"
		/// </summary>
		private IfNode parseIfHeader()
		{
			if (!check(LexemType.If))
				return null;

			var node = new IfNode();
			node.Condition = ensure(parseLineExpr, ParserMessages.ConditionExpected);
			ensure(LexemType.Then, ParserMessages.SymbolExpected, "then");

			return node;
		}

		/// <summary>
		/// while_header                                = "while" line_expr "do"
		/// </summary>
		private WhileNode parseWhileHeader()
		{
			if (!check(LexemType.While))
				return null;

			var node = new WhileNode();
			node.Condition = ensure(parseLineExpr, ParserMessages.ConditionExpected);
			ensure(LexemType.Do, ParserMessages.SymbolExpected, "do");

			return node;
		}

		/// <summary>
		/// for_block                                   = "for" identifier "in" line_expr [ ".." line_expr ] "do"
		/// </summary>
		private ForeachNode parseForHeader()
		{
			if (!check(LexemType.For))
				return null;

			var node = new ForeachNode();
			node.VariableName = ensure(LexemType.Identifier, ParserMessages.VarIdentifierExpected).Value;
			ensure(LexemType.In, ParserMessages.SymbolExpected, "in");

			var iter = ensure(parseLineExpr, ParserMessages.SequenceExpected);
			if (check(LexemType.DoubleDot))
			{
				node.RangeStart = iter;
				node.RangeEnd = ensure(parseLineExpr, ParserMessages.RangeEndExpected);
			}
			else
			{
				node.IterableExpression = iter;
			}

			ensure(LexemType.Do, ParserMessages.SymbolExpected, "do");
			return node;
		}

		/// <summary>
		/// using_header                                = "using" [ identifier "=" ] line_expr "do"
		/// </summary>
		private UsingNode parseUsingHeader()
		{
			if (!check(LexemType.Using))
				return null;

			var node = new UsingNode();
			if (peek(LexemType.Identifier, LexemType.Assign))
			{
				node.VariableName = getValue();
				skip();
			}

			node.Expression = ensure(parseLineExpr, ParserMessages.ExpressionExpected);
			ensure(LexemType.Do, ParserMessages.SymbolExpected, "do");

			return node;
		}

		#endregion

		#region Block initializers

		/// <summary>
		/// new_block_expr                              = "new" new_tuple_block | new_array_block | new_list_block | new_dict_block | new_object_block
		/// </summary>
		private NodeBase parseNewBlockExpr()
		{
			if (!check(LexemType.New))
				return null;

			return attempt(parseNewTupleBlock)
				   ?? attempt(parseNewListBlock)
				   ?? attempt(parseNewArrayBlock)
				   ?? attempt(parseNewDictBlock)
				   ?? attempt(parseNewObjectBlock) as NodeBase;
		}

		/// <summary>
		/// new_tuple_block                             = "(" init_expr_block ")"
		/// </summary>
		private NewTupleNode parseNewTupleBlock()
		{
			if (!check(LexemType.ParenOpen))
				return null;

			var node = new NewTupleNode();
			node.Expressions = parseInitExprBlock().ToList();
			if(node.Expressions.Count == 0)
				return null;

			ensure(LexemType.ParenClose, ParserMessages.SymbolExpected, ')');

			return node;
		}

		/// <summary>
		/// new_list_block                              = "[" "[" init_expr_block "]" "]"
		/// </summary>
		private NewListNode parseNewListBlock()
		{
			if (!peek(LexemType.SquareOpen, LexemType.SquareOpen))
				return null;

			skip(2);

			var node = new NewListNode();
			node.Expressions = parseInitExprBlock().ToList();
			if (node.Expressions.Count == 0)
				return null;

			if (!peek(LexemType.SquareClose, LexemType.SquareClose))
				error(ParserMessages.SymbolExpected, "]]");

			skip(2);

			return node;
		}

		/// <summary>
		/// new_array_block                             = "[" init_expr_block "]"
		/// </summary>
		private NewArrayNode parseNewArrayBlock()
		{
			if (!check(LexemType.SquareOpen))
				return null;

			var node = new NewArrayNode();
			node.Expressions = parseInitExprBlock().ToList();
			if (node.Expressions.Count == 0)
				return null;

			ensure(LexemType.SquareClose, ParserMessages.SymbolExpected, "]");

			return node;
		}

		/// <summary>
		/// new_dict_block                              = "{" init_dict_expr_block "}"
		/// </summary>
		private NewDictionaryNode parseNewDictBlock()
		{
			if (!check(LexemType.CurlyOpen))
				return null;

			var node = new NewDictionaryNode();
			node.Expressions = parseInitExprDictBlock().ToList();
			if (node.Expressions.Count == 0)
				return null;

			ensure(LexemType.CurlyClose, ParserMessages.SymbolExpected, "}");

			return node;
		}

		/// <summary>
		/// init_expr_block                             = INDENT line_expr { NL line_expr } DEDENT
		/// </summary>
		private IEnumerable<NodeBase> parseInitExprBlock()
		{
			if(peek(LexemType.NewLine))
				error(ParserMessages.InitializerIndentExprected);

			if (!check(LexemType.Indent))
				yield break;

			yield return ensure(parseLineExpr, ParserMessages.InitExpressionExpected);

			while (!check(LexemType.Dedent))
			{
				if(peekAny(LexemType.CurlyClose, LexemType.SquareClose, LexemType.ParenClose))
					error(ParserMessages.ClosingBraceNewLine);

				ensure(LexemType.NewLine, ParserMessages.InitExpressionSeparatorExpected);
				yield return ensure(parseLineExpr, ParserMessages.InitExpressionExpected);
			}
		}

		/// <summary>
		/// init_expr_dict_block                        = INDENT init_dict_expr { NL init_dict_expr } DEDENT
		/// </summary>
		private IEnumerable<KeyValuePair<NodeBase, NodeBase>> parseInitExprDictBlock()
		{
			if (!check(LexemType.Indent))
				yield break;

			yield return parseInitDictExpr();

			while (!check(LexemType.Dedent))
			{
				ensure(LexemType.NewLine, ParserMessages.InitExpressionSeparatorExpected);
				yield return parseInitDictExpr();
			}
		}

		/// <summary>
		/// init_dict_expr                              = line_expr "=>" line_expr
		/// </summary>
		private KeyValuePair<NodeBase, NodeBase> parseInitDictExpr()
		{
			var key = ensure(parseLineExpr, ParserMessages.DictionaryKeyExpected);
			ensure(LexemType.FatArrow, ParserMessages.SymbolExpected, "=>");
			var value = ensure(parseLineExpr, ParserMessages.DictionaryValueExpected);

			return new KeyValuePair<NodeBase, NodeBase>(key, value);
		}

		/// <summary>
		/// new_object_block                            = type invoke_block_args
		/// </summary>
		private NewObjectNode parseNewObjectBlock()
		{
			var type = attempt(parseType);
			if (type == null)
				return null;

			var args = parseInvokeBlockArgs().ToList();
			if (args.Count == 0)
				return null;

			var node = new NewObjectNode();
			node.TypeSignature = type;
			node.Arguments = args;
			return node;
		}

		#endregion

		#region Block invocations

		/// <summary>
		/// invoke_block_expr                           = line_expr [ INDENT invoke_pass { NL invoke_pass } DEDENT ]
		/// </summary>
		private NodeBase parseInvokeBlockExpr()
		{
			var expr = attempt(parseLineExpr);
			if (expr == null)
				return null;

			if (!check(LexemType.Indent))
				return null;
			
			var pass = attempt(parseInvokePass);
			if (pass == null)
				return null;

			(pass.Expression as GetMemberNode).Expression = expr;
			expr = pass;

			while (!check(LexemType.Dedent))
			{
				ensure(LexemType.NewLine, ParserMessages.InvokePassSeparatorExpected);
				pass = attempt(parseInvokePass);
				if (pass == null)
					return expr;

				(pass.Expression as GetMemberNode).Expression = expr;
				expr = pass;
			}
			
			return expr;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <returns></returns>
		private InvocationNode parseInvokeBlockPassExpr()
		{
			var expr = attempt(parseLineExpr);
			if (expr == null)
				return null;

			var args = parseInvokeBlockArgs().ToList();
			if (args.Count == 0)
				return null;

			return new InvocationNode {Expression = expr, Arguments = args};
		}
		
		/// <summary>
		/// invoke_pass                                 = "|>" identifier [ type_args ] ( invoke_block_args | invoke_line_args )
		/// </summary>
		private InvocationNode parseInvokePass()
		{
			if (!check(LexemType.PassRight))
				return null;

			var getter = new GetMemberNode();
			var invoker = new InvocationNode { Expression = getter };

			getter.MemberName = ensure(LexemType.Identifier, ParserMessages.MemberNameExpected).Value;

			var hints = attempt(parseTypeArgs);
			if(hints != null)
				getter.TypeHints = hints;

			var lambda = attempt(parseLambdaLineExpr);
			if (lambda != null)
			{
				invoker.Arguments = new List<NodeBase> { lambda };
			}
			else
			{
				invoker.Arguments = parseInvokeBlockArgs().ToList();
				if (invoker.Arguments.Count == 0)
					invoker.Arguments = parseInvokeLineArgs().ToList();

				if (invoker.Arguments.Count == 0)
					error(ParserMessages.ArgumentsExpected);
			}

			return invoker;
		}

		/// <summary>
		/// invoke_block_args                           = INDENT invoke_block_arg { NL invoke_block_arg } DEDENT
		/// </summary>
		private IEnumerable<NodeBase> parseInvokeBlockArgs()
		{
			if (!check(LexemType.Indent))
				yield break;

			var node = attempt(parseInvokeBlockArg);
			if (node == null)
				yield break;

			yield return node;

			while (!check(LexemType.Dedent))
			{
				if(!isStmtSeparator())
					error("Argument passes must be separated by newlines!");

				node = attempt(parseInvokeBlockArg);
				if (node == null)
					yield break;

				yield return node;
			}
		}

		/// <summary>
		/// invoke_block_arg                            = "<|" ( ref_arg | expr )
		/// </summary>
		private NodeBase parseInvokeBlockArg()
		{
			if (!check(LexemType.PassLeft))
				return null;

			return attempt(parseRefArg)
				   ?? ensure(parseExpr, ParserMessages.ExpressionExpected);
		}
		
		/// <summary>
		/// invoke_line_args                            = { invoke_line_arg }
		/// </summary>
		private IEnumerable<NodeBase> parseInvokeLineArgs()
		{
			while (true)
			{
				var curr = attempt(parseInvokeLineArg);
				if (curr == null)
					yield break;

				yield return curr;
			}
		}

		/// <summary>
		/// invoke_line_arg                             = ref_arg | get_expr
		/// </summary>
		private NodeBase parseInvokeLineArg()
		{
			return attempt(parseRefArg)
				   ?? attempt(parseGetExpr);
		}

		/// <summary>
		/// ref_arg                                     = "ref" lvalue_expr | "(" "ref" lvalue_expr ")"
		/// </summary>
		private NodeBase parseRefArg()
		{
			var paren = check(LexemType.ParenOpen);

			if (!check(LexemType.Ref))
				return null;

			var node = ensure(parseLvalueExpr, ParserMessages.RefLvalueExpected);
			(node as IPointerProvider).RefArgumentRequired = true;

			if (paren)
				ensure(LexemType.ParenClose, ParserMessages.SymbolExpected, ')');

			return node;
		}

		#endregion

		#region Pattern matching

		/// <summary>
		/// match_block                                 = "match" line_expr "with" INDENT { match_stmt } DEDENT
		/// </summary>
		private MatchNode parseMatchBlock()
		{
			if (!check(LexemType.Match))
				return null;

			var node = new MatchNode { Expression = ensure(parseLineExpr, ParserMessages.MatchExpressionExpected) };

			ensure(LexemType.With, ParserMessages.SymbolExpected, "with");
			ensure(LexemType.Indent, ParserMessages.MatchIndentExpected);

			node.MatchStatements.Add(ensure(parseMatchStmt, ParserMessages.SymbolExpected, "case"));

			while (!check(LexemType.Dedent))
			{
				ensure(LexemType.NewLine, ParserMessages.NewlineSeparatorExpected);
				node.MatchStatements.Add(ensure(parseMatchStmt, ParserMessages.SymbolExpected, "case"));
			}

			return node;
		}

		/// <summary>
		/// match_stmt                                  = "case" match_rules [ "when" line_expr ] "then" block
		/// </summary>
		private MatchStatementNode parseMatchStmt()
		{
			ensure(LexemType.Case, ParserMessages.SymbolExpected, "case");

			var node = new MatchStatementNode { MatchRules = parseMatchRules().ToList() };

			if (check(LexemType.When))
				node.Condition = ensure(parseLineExpr, ParserMessages.WhenGuardExpressionExpected);

			ensure(LexemType.Then, ParserMessages.SymbolExpected, "then");

			node.Expression = ensure(parseBlock, ParserMessages.CodeBlockExpected);

			return node;
		}

		/// <summary>
		/// match_rules                                 = match_rule { [ NL ] "|" match_rule }
		/// </summary>
		private IEnumerable<MatchRuleBase> parseMatchRules()
		{
			yield return ensure(parseMatchRule, ParserMessages.MatchRuleExpected);

			while (true)
			{
				check(LexemType.NewLine);
				if (!check(LexemType.BitOr))
					yield break;

				yield return ensure(parseMatchRule, ParserMessages.MatchRuleExpected);
			}
		}

		/// <summary>
		/// match_rule                                  = rule_generic [ rule_keyvalue ]
		/// </summary>
		private MatchRuleBase parseMatchRule()
		{
			var rule = parseRuleGeneric();
			var keyValue = attempt(parseRuleKeyValue);

			if (keyValue == null)
				return rule;

			keyValue.KeyRule = rule;
			return keyValue;
		}

		#endregion

		#region Match rules

		/// <summary>
		/// rule_generic                                = rule_tuple | rule_array | rule_regex | rule_record | rule_type | rule_range | rule_literal | rule_name
		/// </summary>
		private MatchRuleBase parseRuleGeneric()
		{
			return attempt(parseRuleTuple)
			       ?? attempt(parseRuleArray)
				   ?? attempt(parseRegex)
			       ?? attempt(parseRuleRecord)
			       ?? attempt(parseRuleType)
			       ?? attempt(parseRuleRange)
			       ?? attempt(parseRuleLiteral)
				   ?? attempt(parseRuleName) as MatchRuleBase;
		}

		/// <summary>
		/// rule_tuple                                  = "(" match_rule { ";" match_rule } ")"
		/// </summary>
		private MatchTupleRule parseRuleTuple()
		{
			if (!check(LexemType.ParenOpen))
				return null;

			var node = new MatchTupleRule();
			node.ElementRules.Add(ensure(parseMatchRule, ParserMessages.MatchRuleExpected));
			while(check(LexemType.Semicolon))
				node.ElementRules.Add(ensure(parseMatchRule, ParserMessages.MatchRuleExpected));

			ensure(LexemType.ParenClose, ParserMessages.SymbolExpected, ")");

			return node;
		}

		/// <summary>
		/// rule_array                                  = "[" [ rule_array_item { ";" rule_array_item } ] "]"
		/// </summary>
		private MatchRuleBase parseRuleArray()
		{
			if (!check(LexemType.SquareOpen))
				return null;

			var node = new MatchArrayRule();
			if (!check(LexemType.SquareClose))
			{
				node.ElementRules.Add(ensure(parseRuleArrayItem, ParserMessages.MatchRuleExpected));

				while (check(LexemType.Semicolon))
					node.ElementRules.Add(ensure(parseRuleArrayItem, ParserMessages.MatchRuleExpected));

				ensure(LexemType.SquareClose, ParserMessages.SymbolExpected, "]");
			}

			return node;
		}

		/// <summary>
		/// rule_regex                                 = regex
		/// </summary>
		private MatchRegexNode parseRegex()
		{
			if (!peek(LexemType.Regex))
				return null;

			var raw = getValue();
			var trailPos = raw.LastIndexOf('#');
			var value = raw.Substring(1, trailPos - 1);
			var mods = raw.Substring(trailPos + 1);
			return new MatchRegexNode {Value = value, Modifiers = mods};
		}

		/// <summary>
		/// rule_array_item                             = rule_subsequence | match_rule
		/// </summary>
		private MatchRuleBase parseRuleArrayItem()
		{
			return attempt(parseRuleSubsequence)
			       ?? attempt(parseMatchRule);
		}

		/// <summary>
		/// rule_record                                 = identifier "(" rule_record_var { ";" rule_record_var } ")"
		/// </summary>
		private MatchRecordRule parseRuleRecord()
		{
			if (!peek(LexemType.Identifier, LexemType.ParenOpen))
				return null;

			var identifier = parseType();
			skip();

			var node = new MatchRecordRule { Identifier = identifier };
			node.FieldRules.Add(ensure(parseRuleRecordVar, ParserMessages.MatchRuleExpected));
			while(check(LexemType.Semicolon))
				node.FieldRules.Add(ensure(parseRuleRecordVar, ParserMessages.MatchRuleExpected));

			ensure(LexemType.ParenClose, ParserMessages.SymbolExpected, ")");

			return node;
		}

		/// <summary>
		/// rule_record_var                             = identifier "=" match_rule
		/// </summary>
		private MatchRecordField parseRuleRecordVar()
		{
			if (!peek(LexemType.Identifier, LexemType.Assign))
				return null;

			var identifier = parseType();
			skip();

			return new MatchRecordField
			{
				Name = identifier,
				Rule = ensure(parseMatchRule, ParserMessages.MatchRuleExpected)
			};
		}

		/// <summary>
		/// rule_type                                   = identifier "of" match_rule
		/// </summary>
		private MatchTypeRule parseRuleType()
		{
			if (!peek(LexemType.Identifier, LexemType.Of))
				return null;

			var identifier = parseType();
			skip();

			return new MatchTypeRule
			{
				Identifier = identifier,
				LabelRule = ensure(parseMatchRule, ParserMessages.MatchRuleExpected)
			};
		}

		/// <summary>
		/// rule_range                                  = rule_literal ".." rule_literal
		/// </summary>
		private MatchRangeRule parseRuleRange()
		{
			var start = attempt(parseRuleLiteral);
			if (start == null)
				return null;

			if (!check(LexemType.DoubleDot))
				return null;

			return new MatchRangeRule
			{
				RangeStartRule = start,
				RangeEndRule = ensure(parseRuleLiteral, ParserMessages.MatchRuleExpected)
			};
		}

		/// <summary>
		/// rule_literal                                = literal
		/// </summary>
		private MatchLiteralRule parseRuleLiteral()
		{
			var literal = attempt(parseLiteral);
			if (literal == null)
				return null;

			return new MatchLiteralRule { Literal = (ILiteralNode)literal };
		}

		/// <summary>
		/// rule_name                                   = identifier [ ":" type ]
		/// </summary>
		private MatchNameRule parseRuleName()
		{
			if (!peek(LexemType.Identifier))
				return null;

			var node = new MatchNameRule { Name = getValue() };
			if (check(LexemType.Colon))
				node.Type = ensure(parseType, ParserMessages.TypeSignatureExpected);

			return node;
		}

		/// <summary>
		/// rule_subsequence                            = "..." identifier
		/// </summary>
		private MatchNameRule parseRuleSubsequence()
		{
			if (!check(LexemType.Ellipsis))
				return null;

			return new MatchNameRule
			{
				Name = getValue(),
				IsArraySubsequence = true
			};
		}

		/// <summary>
		/// rule_keyvalue                               = "=>" rule_generic
		/// </summary>
		private MatchKeyValueRule parseRuleKeyValue()
		{
			if (!check(LexemType.FatArrow))
				return null;

			// key is substituted in match_rule
			return new MatchKeyValueRule { ValueRule = ensure(parseRuleGeneric, ParserMessages.MatchRuleExpected) };
		}

		#endregion

		#region Line expressions

		/// <summary>
		/// line_stmt                                   = set_stmt | line_expr
		/// </summary>
		private NodeBase parseLineStmt()
		{
			return attempt(parseSetStmt)
				   ?? attempt(parseLineExpr);
		}

		/// <summary>
		/// line_expr                                   = if_line | while_line | for_line | using_line | throw_stmt | new_object_line | typeop_expr | line_typecheck_expr
		/// </summary>
		private NodeBase parseLineExpr()
		{
			return attempt(parseIfLine)
				   ?? attempt(parseWhileLine)
				   ?? attempt(parseForLine)
				   ?? attempt(parseUsingLine)
				   ?? attempt(parseThrowStmt)
				   ?? attempt(parseNewObjectExpr)
				   ?? attempt(parseTypeopExpr)
				   ?? attempt(parseLineTypecheckExpr);
		}

		/// <summary>
		/// typeop_expr                                 = default_expr | typeof_expr
		/// </summary>
		private NodeBase parseTypeopExpr()
		{
			return attempt(parseDefaultExpr)
				   ?? attempt(parseTypeofExpr) as NodeBase;
		}

		/// <summary>
		/// default_expr                                = "default" type
		/// </summary>
		private DefaultOperatorNode parseDefaultExpr()
		{
			if (!check(LexemType.Default))
				return null;

			return new DefaultOperatorNode { TypeSignature = ensure(parseType, ParserMessages.TypeSignatureExpected) };
		}

		/// <summary>
		/// typeof_expr                                 = "typeof" type
		/// </summary>
		private TypeofOperatorNode parseTypeofExpr()
		{
			if (!check(LexemType.Typeof))
				return null;

			return new TypeofOperatorNode {TypeSignature = ensure(parseType, ParserMessages.TypeSignatureExpected) };
		}

		/// <summary>
		/// line_typecheck_expr                         = line_op_expr [ typecheck_op_expr ]
		/// </summary>
		private NodeBase parseLineTypecheckExpr()
		{
			var node = attempt(parseLineOpExpr);
			if (node == null)
				return null;

			var typeop = attempt(parseTypecheckOpExpr);

			var cast = typeop as CastOperatorNode;
			if (cast != null)
			{
				cast.Expression = node;
				return cast;
			}

			var check = typeop as IsOperatorNode;
			if (check != null)
			{
				check.Expression = node;
				return check;
			}

			return node;
		}

		/// <summary>
		/// line_op_expr                                = [ unary_op ] line_base_expr { binary_op line_base_expr }
		/// </summary>
		private NodeBase parseLineOpExpr()
		{
			// parser magic
			return processOperator(parseLineBaseExpr);
		}

		/// <summary>
		/// typecheck_op_expr                           = "as" type | "is" type
		/// </summary>
		private NodeBase parseTypecheckOpExpr()
		{
			if (check(LexemType.Is))
				return new IsOperatorNode {TypeSignature = ensure(parseType, ParserMessages.TypeSignatureExpected)};

			if (check(LexemType.As))
				return new CastOperatorNode { TypeSignature = ensure(parseType, ParserMessages.TypeSignatureExpected) };

			return null;
		}

		/// <summary>
		/// line_base_expr                              = line_invoke_base_expr | get_expr
		/// </summary>
		private NodeBase parseLineBaseExpr()
		{
			return attempt(parseLineInvokeBaseExpr)
				   ?? attempt(parseGetExpr);
		}

		/// <summary>
		/// line_invoke_base_expr                       = get_expr [ invoke_line_args ]
		/// </summary>
		private NodeBase parseLineInvokeBaseExpr()
		{
			var expr = attempt(parseGetExpr);
			if (expr == null)
				return null;

			var args = parseInvokeLineArgs().ToList();
			if (args.Count == 0)
				return expr;

			var node = new InvocationNode();
			node.Expression = expr;
			node.Arguments = args;
			return node;
		}


		/// <summary>
		/// atom                                        = literal | get_id_expr | get_stmbr_expr | new_collection_expr | paren_expr
		/// </summary>
		private NodeBase parseAtom()
		{
			return attempt(parseLiteral)
				   ?? attempt(parseGetStmbrExpr)
				   ?? attempt(parseGetIdExpr)
				   ?? attempt(parseNewCollectionExpr)
				   ?? attempt(parseParenExpr);
		}

		/// <summary>
		/// paren_expr                                  = "(" ( lambda_line_expr | line_expr ) ")"
		/// </summary>
		private NodeBase parseParenExpr()
		{
			if (!check(LexemType.ParenOpen))
				return null;

			var expr = attempt(parseLambdaLineExpr)
					   ?? ensure(parseLineStmt, ParserMessages.ExpressionExpected);

			if (expr != null)
			{
				if (peek(LexemType.Colon))
					return null;

				ensure(LexemType.ParenClose, ParserMessages.SymbolExpected, ')');
			}

			return expr;
		}

		/// <summary>
		/// lambda_line_expr                            = [ fun_args ] "->" line_expr
		/// </summary>
		private LambdaNode parseLambdaLineExpr()
		{
			var node = new LambdaNode();
			node.Arguments = attempt(() => parseFunArgs()) ?? new List<FunctionArgument>();
			
			if (!check(LexemType.Arrow))
				return null;

			node.Body.Add(ensure(parseLineStmt, ParserMessages.FunctionBodyExpected));
			return node;
		}

		#endregion

		#region Line control structures

		/// <summary>
		/// if_line                                     = if_header line_stmt [ "else" line_stmt ]
		/// </summary>
		private IfNode parseIfLine()
		{
			var node = attempt(parseIfHeader);
			if (node == null)
				return null;

			node.TrueAction.Add(ensure(parseLineStmt, ParserMessages.ConditionExpressionExpected));
			if ( check(LexemType.Else))
				node.FalseAction = new CodeBlockNode { ensure(parseLineStmt, ParserMessages.ExpressionExpected) };

			return node;
		}

		/// <summary>
		/// while_line                                  = while_header line_stmt
		/// </summary>
		private WhileNode parseWhileLine()
		{
			var node = attempt(parseWhileHeader);
			if (node == null)
				return null;

			node.Body.Add(ensure(parseLineStmt, ParserMessages.LoopExpressionExpected));
			return node;
		}

		/// <summary>
		/// for_line                                    = for_header line_stmt
		/// </summary>
		private ForeachNode parseForLine()
		{
			var node = attempt(parseForHeader);
			if (node == null)
				return null;

			node.Body.Add(ensure(parseLineStmt, ParserMessages.LoopExpressionExpected));
			return node;
		}

		/// <summary>
		/// using_line                                  = using_header line_stmt
		/// </summary>
		private UsingNode parseUsingLine()
		{
			var node = attempt(parseUsingHeader);
			if (node == null)
				return null;

			node.Body.Add(ensure(parseLineStmt, ParserMessages.UsingExpressionExpected));
			return node;
		}

		/// <summary>
		/// throw_stmt                                  = "throw" [ line_expr ]
		/// </summary>
		private ThrowNode parseThrowStmt()
		{
			if (!check(LexemType.Throw))
				return null;

			var node = new ThrowNode();
			node.Expression = attempt(parseLineExpr);
			return node;
		}
		
		#endregion

		#region Line initializers

		/// <summary>
		/// new_line_expr                               = "new" ( new_objarray_line | new_object_block )
		/// </summary>
		private NodeBase parseNewObjectExpr()
		{
			if (!check(LexemType.New))
				return null;

			return attempt(parseNewObjArrayLine)
				   ?? attempt(parseNewObjectLine) as NodeBase;
		}

		/// <summary>
		/// new_collection_expr                        = "new" ( new_tuple_line | new_list_line | new_array_line | new_dict_line )
		/// </summary>
		private NodeBase parseNewCollectionExpr()
		{
			if (!check(LexemType.New))
				return null;

			return attempt(parseNewTupleLine)
				   ?? attempt(parseNewListLine)
				   ?? attempt(parseNewArrayLine)
				   ?? attempt(parseNewDictLine) as NodeBase;
		}

		/// <summary>
		/// new_tuple_line                             = "(" init_expr_line ")"
		/// </summary>
		private NewTupleNode parseNewTupleLine()
		{
			// todo: revise grammar, possibly eliminating the unit literal
			if(check(LexemType.Unit))
				error(ParserMessages.TupleItem);

			if (!check(LexemType.ParenOpen))
				return null;

			var node = new NewTupleNode();
			node.Expressions = parseInitExprLine().ToList();

			if (node.Expressions.Count == 0)
				error(ParserMessages.TupleItem);

			ensure(LexemType.ParenClose, ParserMessages.SymbolExpected, ")");

			return node;
		}

		/// <summary>
		/// new_list_line                               = "[" "[" init_expr_block "]" "]"
		/// </summary>
		private NewListNode parseNewListLine()
		{
			if (!peek(LexemType.SquareOpen, LexemType.SquareOpen))
				return null;

			skip(2);

			var node = new NewListNode();
			node.Expressions = parseInitExprLine().ToList();

			if (node.Expressions.Count == 0)
				error(ParserMessages.ListItem);

			if (!peek(LexemType.SquareClose, LexemType.SquareClose))
				error(ParserMessages.SymbolExpected, "]]");

			skip(2);

			return node;
		}

		/// <summary>
		/// new_array_line                              = "[" init_expr_block "]"
		/// </summary>
		private NewArrayNode parseNewArrayLine()
		{
			if (!check(LexemType.SquareOpen))
				return null;

			var node = new NewArrayNode();
			node.Expressions = parseInitExprLine().ToList();

			if (node.Expressions.Count == 0)
				error(ParserMessages.ArrayItem);

			ensure(LexemType.SquareClose, ParserMessages.SymbolExpected, "]");

			return node;
		}

		/// <summary>
		/// new_dict_line                               = "{" init_dict_expr_block "}"
		/// </summary>
		private NewDictionaryNode parseNewDictLine()
		{
			if (!check(LexemType.CurlyOpen))
				return null;

			var node = new NewDictionaryNode();
			node.Expressions = parseInitExprDictLine().ToList();

			if (node.Expressions.Count == 0)
				error(ParserMessages.DictionaryItem);

			ensure(LexemType.CurlyClose, ParserMessages.SymbolExpected, "}");

			return node;
		}

		/// <summary>
		/// init_expr_line                              = line_expr { ";" line_expr }
		/// </summary>
		private IEnumerable<NodeBase> parseInitExprLine()
		{
			var node = attempt(parseLineExpr);
			if(node == null)
				yield break;

			yield return node;
			while (check(LexemType.Semicolon))
				yield return ensure(parseLineExpr, ParserMessages.ExpressionExpected);
		}

		/// <summary>
		/// init_expr_dict_line                         = init_dict_expr { ";" init_dict_expr }
		/// </summary>
		private IEnumerable<KeyValuePair<NodeBase, NodeBase>> parseInitExprDictLine()
		{
			if (check(LexemType.CurlyClose))
				yield break;

			yield return parseInitDictExpr();

			while (check(LexemType.Semicolon))
				yield return parseInitDictExpr();
		}

		/// <summary>
		/// new_objarray_line                          = type "[" line_expr "]"
		/// </summary>
		private NewObjectArrayNode parseNewObjArrayLine()
		{
			var type = attempt(parseType);
			if (type == null)
				return null;

			if (!check(LexemType.SquareOpen))
				return null;

			var node = new NewObjectArrayNode();
			node.TypeSignature = type;
			node.Size = ensure(parseLineExpr, ParserMessages.ExpressionExpected);

			ensure(LexemType.SquareClose, ParserMessages.SymbolExpected, "]");

			return node;
		}

		/// <summary>
		/// new_object_line                             = type invoke_line_args
		/// </summary>
		private NewObjectNode parseNewObjectLine()
		{
			var type = attempt(parseType);
			if (type == null)
				return null;

			var args = parseInvokeLineArgs().ToList();
			if (args.Count == 0)
				return null;

			var node = new NewObjectNode();
			node.TypeSignature = type;
			node.Arguments = args;
			return node;
		}

		#endregion

		#region Literals

		/// <summary>
		/// literal                                     = unit | null | bool | int | long | float | double | decimal | char | string
		/// </summary>
		private NodeBase parseLiteral()
		{
			return attempt(parseUnit)
				   ?? attempt(parseNull)
				   ?? attempt(parseBool)
				   ?? attempt(parseInt)
				   ?? attempt(parseLong)
				   ?? attempt(parseFloat)
				   ?? attempt(parseDouble)
				   ?? attempt(parseDecimal)
				   ?? attempt(parseChar)
				   ?? attempt(parseString) as NodeBase;
		}

		private UnitNode parseUnit()
		{
			return check(LexemType.Unit) ? new UnitNode() : null;
		}

		private NullNode parseNull()
		{
			return check(LexemType.Null) ? new NullNode() : null;
		}

		private BooleanNode parseBool()
		{
			if(check(LexemType.True))
				return new BooleanNode(true);

			if (check(LexemType.False))
				return new BooleanNode();

			return null;
		}

		private StringNode parseString()
		{
			if (!peek(LexemType.String))
				return null;

			return new StringNode(getValue());
		}

		private IntNode parseInt()
		{
			if (!peek(LexemType.Int))
				return null;

			var value = getValue();
			try
			{
				return new IntNode(int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture));
			}
			catch
			{
				error(ParserMessages.InvalidInteger, value);
				return null;
			}
		}

		private LongNode parseLong()
		{
			if (!peek(LexemType.Long))
				return null;

			var value = getValue();
			try
			{
				return new LongNode(long.Parse(value.Substring(0, value.Length - 1), NumberStyles.Integer, CultureInfo.InvariantCulture));
			}
			catch
			{
				error(ParserMessages.InvalidLong, value);
				return null;
			}
		}

		private FloatNode parseFloat()
		{
			if (!peek(LexemType.Float))
				return null;

			var value = getValue();
			try
			{
				return new FloatNode(float.Parse(value.Substring(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture));
			}
			catch
			{
				error(ParserMessages.InvalidFloat, value);
				return null;
			}
		}
 
		private DoubleNode parseDouble()
		{
			if (!peek(LexemType.Double))
				return null;

			var value = getValue();
			try
			{
				return new DoubleNode(double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture));
			}
			catch
			{
				error(ParserMessages.InvalidDouble, value);
				return null;
			}
		}

		private DecimalNode parseDecimal()
		{
			if (!peek(LexemType.Decimal))
				return null;

			var value = getValue();
			try
			{
				return new DecimalNode(decimal.Parse(value.Substring(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture));
			}
			catch
			{
				error(ParserMessages.InvalidDecimal, value);
				return null;
			}
		}

		private CharNode parseChar()
		{
			return peek(LexemType.Char) ? new CharNode(getValue()[0]) : null;
		}

		#endregion
	}
}
