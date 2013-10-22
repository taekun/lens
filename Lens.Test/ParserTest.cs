﻿using System.Collections.Generic;
using System.Linq;
using Lens.Compiler;
using Lens.Lexer;
using Lens.Parser;
using Lens.SyntaxTree;
using Lens.SyntaxTree.ControlFlow;
using Lens.SyntaxTree.Expressions;
using Lens.SyntaxTree.Operators;
using NUnit.Framework;

namespace Lens.Test
{
	[TestFixture]
	public class ParserTest
	{
		[Test]
		public void Testy()
		{
			Assert.AreEqual(new TypeSignature("int"), new TypeSignature("int"));
		}

		[Test]
		public void Using()
		{
			Test("using System", new UsingNode { Namespace = "System" });
		}

		[Test]
		public void MultiUsing()
		{
			Test("using Lens.Parser", new UsingNode { Namespace = "Lens.Parser" });
		}

		[Test]
		public void Record()
		{
			var src = @"
record Student
    Name:string
    Age:int";

			var result = Expr.Record(
				"Student",
				Expr.Field("Name", "string"),
				Expr.Field("Age", "int")
			);

			Test(src, result);
		}

		[Test]
		public void Type()
		{
			var src = @"
type Suit
    Hearts
    Clubs
    Spades
    Diamonds";

			var result = Expr.Type(
				"Suit",
				Expr.Label("Hearts"),
				Expr.Label("Clubs"),
				Expr.Label("Spades"),
				Expr.Label("Diamonds")
			);
			
			Test(src, result);
		}

		[Test]
		public void ComplexType()
		{
			var src = @"
type Card
    Ace of Suit
    King of Suit
    Queen of Suit
    Jack of Suit
    ValueCard of Tuple<Suit, int>";

			var result = Expr.Type(
				"Card",
				Expr.Label("Ace", "Suit"),
				Expr.Label("King", "Suit"),
				Expr.Label("Queen", "Suit"),
				Expr.Label("Jack", "Suit"),
				Expr.Label("ValueCard", "Tuple<Suit,int>")
			);

			Test(src, result);
		}

		[Test]
		public void ArrayType()
		{
			var src = @"
type ArrayHolder
    Array of int[][]";

			var result = Expr.Type(
				"ArrayHolder",
				Expr.Label("Array", "int[][]")
			);

			Test(src, result);
		}

		[Test]
		public void SimpleFunction()
		{
			var src = @"fun negate:int (x:int) -> -x";
			var result = Expr.Fun(
				"negate",
				"int",
				new[] {Expr.Arg("x", "int")},
				Expr.Negate(Expr.Get("x"))
			);
			
			Test(src, result);
		}

		[Test]
		public void InvocationTest()
		{
			var result = Expr.Invoke(
				Expr.Get("sqrt"),
				Expr.Add(
					Expr.Get("sq1"),
					Expr.Get("sq2")
				)
			);

			Test("sqrt (sq1 + sq2)", result);
		}

		[Test]
		public void ComplexFunction()
		{
			var src = @"fun hypo:double (a:int b:int) ->
    let sq1 = a * a
    let sq2 = b * b
    sqrt (sq1 + sq2)";

			var result = Expr.Fun(
				"hypo",
				"double",
				new[] { Expr.Arg("a", "int"), Expr.Arg("b", "int") },
				Expr.Let(
					"sq1",
					Expr.Mult(
						Expr.Get("a"),
						Expr.Get("a")
					)
				),
				Expr.Let(
					"sq2",
					Expr.Mult(
						Expr.Get("b"),
						Expr.Get("b")
					)
				),
				Expr.Invoke(
					"sqrt",
					Expr.Add(
						Expr.Get("sq1"),
						Expr.Get("sq2")
					)
				)
			);

			Test(src, result);
		}

		[Test]
		public void VariableDeclaration()
		{
			var src = @"var v = 1 + 1";
			var result = Expr.Var("v", Expr.Add(Expr.Int(1), Expr.Int(1)));

			Test(src, result);
		}

		[Test]
		public void ConstantDeclaration()
		{
			var src = @"let v = 1 + 1";
			var result = Expr.Let("v", Expr.Add(Expr.Int(1), Expr.Int(1)));

			Test(src, result);
		}

		[Test]
		public void ArrayDeclaration()
		{
			var src = @"new [1; 2; 1 + 2]";
			var result = Expr.Array(
				Expr.Int(1),
				Expr.Int(2),
				Expr.Add(Expr.Int(1), Expr.Int(2))
			);

			Test(src, result);
		}

		[Test]
		public void TupleDeclaration()
		{
			var src = @"new (1; 1.2; ""hello world""; true)";
			var result = Expr.Tuple(
				Expr.Int(1),
				Expr.Double(1.2),
				Expr.Str("hello world"),
				Expr.True()
			);

			Test(src, result);
		}

		[Test]
		public void NewObjectDeclaration()
		{
			var src = @"new SomeObject false 13.37";
			var result = Expr.New(
				"SomeObject",
				Expr.False(),
				Expr.Double(13.37)
			);

			Test(src, result);
		}

		[Test]
		public void DictionaryDeclaration()
		{
			var src = @"new { a => b; c => true }";
			var result = new NewDictionaryNode
			{
				{ Expr.Get("a"), Expr.Get("b")},
				{ Expr.Get("c"), Expr.True()}
			};

			Test(src, result);
		}

		[Test]
		public void ListDeclaration()
		{
			var src = "new [[true;true;false]]";
			var result = Expr.List(
				Expr.True(),
				Expr.True(),
				Expr.False()
			);

			Test(src, result);
		}

		[Test]
		public void BareLambda()
		{
			var src = "let getFive = -> 5";
			var result = Expr.Let("getFive", Expr.Lambda(Expr.Int(5)));
			
			Test(src, result);
		}

		[Test]
		public void ParametricLambda()
		{
			var src = "a.Where (x:int -> x < 10)";
			var result = Expr.Invoke(
				Expr.Get("a"),
				"Where",
				Expr.Lambda(
					new [] { Expr.Arg("x", "int") },
					Expr.Less(
						Expr.Get("x"),
						Expr.Int(10)
					)
				)
			);

			Test(src, result);
		}

		[Test]
		public void ParametricLambda2()
		{
			var src = "let div = (a:System.Float b:System.Float) -> a / b";
			var result = Expr.Let(
				"div",
				Expr.Lambda(
					new[] { Expr.Arg("a", "System.Float"), Expr.Arg("b", "System.Float") },
					Expr.Div(
						Expr.Get("a"),
						Expr.Get("b")
					)
				)
			);

			Test(src, result);
		}

		[Test]
		public void AssignVariable()
		{
			var src = "a = b";
			var result = Expr.Set("a", Expr.Get("b"));

			Test(src, result);
		}

		[Test]
		public void GetLiteralMember()
		{
			var src = "1.GetHashCode()";
			var result = Expr.Invoke(
				Expr.Int(1),
				"GetHashCode"
			);

			Test(src, result);
		}

		[Test]
		public void GetDynamicMember1()
		{
			var src = "a = b.someShit";
			var result = Expr.Set(
				"a",
				Expr.GetMember(
					Expr.Get("b"),
					"someShit"
				)
			);

			Test(src, result);
		}

		[Test]
		public void GetDynamicMember2()
		{
			var src = "a = (1 + 2).someShit";
			var result = Expr.Set(
				"a", 
				Expr.GetMember(
					Expr.Add(Expr.Int(1), Expr.Int(2)),
					"someShit"
				)
			);

			Test(src, result);
		}

		[Test]
		public void GetStaticMember()
		{
			var src = "a = Enumerable<int>::Empty";
			var result = Expr.Set(
				"a",
				Expr.GetMember("Enumerable<int>", "Empty")
			);

			Test(src, result);
		}

		[Test]
		public void SetDynamicMember1()
		{
			var src = "a.b.c = false";
			var result = Expr.SetMember(
				Expr.GetMember(Expr.Get("a"), "b"),
				"c",
				Expr.False()
			);

			Test(src, result);
		}

		[Test]
		public void SetDynamicMember2()
		{
			var src = "(1 + 2).someShit = a";
			var result = Expr.SetMember(
				Expr.Add(Expr.Int(1), Expr.Int(2)),
				"someShit",
				Expr.Get("a")
			);

			Test(src, result);
		}

		[Test]
		public void SetStaticMember()
		{
			var src = "Singleton::Instance = null";
			var result = Expr.SetMember("Singleton", "Instance", Expr.Null());

			Test(src, result);
		}

		[Test]
		public void GetIndex()
		{
			var src = "a = b[2**2]";
			var result = Expr.Set(
				"a",
				Expr.GetIdx(
					Expr.Get("b"),
					Expr.Pow(
						Expr.Int(2),
						Expr.Int(2)
					)
				)
			);

			Test(src, result);
		}

		[Test]
		public void SetIndex()
		{
			var src = "a[2**2] = test 1 2 3";
			var result = Expr.SetIdx(
				Expr.Get("a"),
				Expr.Pow(Expr.Int(2), Expr.Int(2)),
				Expr.Invoke(
					"test",
					Expr.Int(1),
					Expr.Int(2),
					Expr.Int(3)
				)
			);

			Test(src, result);
		}

		[Test]
		public void MultilineLambda()
		{
			var src = @"
(a:double) ->
    logger.log a
    a ** 2";

			var result = Expr.Lambda
			(
				new [] { Expr.Arg("a", "double") },
				Expr.Invoke(
					Expr.Get("logger"),
					"log",
					Expr.Get("a")
				),
				Expr.Pow(
					Expr.Get("a"),
					Expr.Int(2)
				)
			);

			Test(src, result);
		}

		[Test]
		public void MultilineInvocation()
		{
			var src = @"
test
    <| true
    <| (a:double) ->
        logger.log a
        a ** 2
    <| false";

			var result = Expr.Invoke(
				"test",
				Expr.True(),
				Expr.Lambda
				(
					new [] { Expr.Arg("a", "double") },
						Expr.Invoke(
							Expr.Get("logger"),
							"log",
							Expr.Get("a")
						),
						Expr.Pow(
							Expr.Get("a"),
							Expr.Int(2)
						)
				),
				Expr.False()
			);

			Test(src, result);
		}

		[Test]
		public void ConditionSimple()
		{
			var src = @"
if true then
    a = 1
    b = 2";
			var result = Expr.If(
				Expr.True(),
				Expr.Block(
					Expr.Set("a", Expr.Int(1)),
					Expr.Set("b", Expr.Int(2))
				)
			);

			Test(src, result);
		}

		[Test]
		public void ConditionFull()
		{
			var src = "if true then a else b";
			var result = Expr.If(
				Expr.True(),
				Expr.Block(Expr.Get("a")),
				Expr.Block(Expr.Get("b"))
			);

			Test(src, result);
		}

		[Test]
		public void Loop()
		{
			var src = @"while a > 0 do a = a - 1";
			var result = Expr.While(
				Expr.Greater(
					Expr.Get("a"),
					Expr.Int(0)
				),
				Expr.Block(
					Expr.Set(
						"a",
						Expr.Sub(
							Expr.Get("a"),
							Expr.Int(1)
						)
					)
				)
			);

			Test(src, result);
		}

		[Test]
		public void MultilineLoop()
		{
			var src = @"
while a > 0 do
    a
b";
			var result = Expr.While(
				Expr.Greater(
					Expr.Get("a"),
					Expr.Int(0)
				),
				Expr.Block(Expr.Get("a"))
			);

			Test(src, result, Expr.Get("b"));
		}

		[Test]
		public void SingleCatch()
		{
			var src = @"
try
    1 / 0
catch ex:DivisionByZeroException
    log ex
";

			var result = Expr.Try(
				Expr.Block(
					Expr.Div(Expr.Int(1), Expr.Int(0))
				),
				Expr.Catch(
					"DivisionByZeroException",
					"ex",
					Expr.Block(
						Expr.Invoke("log", Expr.Get("ex"))
					)
				)
			);

			Test(src, result);
		}

		[Test]
		public void MultipleCatch()
		{
			var src = @"
try
    1 / 0
catch ex:DivisionByZeroException
    log ex
catch
    doStuff ()
    log ""whoopsie""
finally
    log ""finally""
";

			var result = Expr.Try(
				Expr.Block(
					Expr.Div(Expr.Int(1), Expr.Int(0))
				),
				Expr.Block(
					Expr.Invoke("log", Expr.Str("finally"))
				),
				Expr.Catch(
					"DivisionByZeroException",
					"ex",
					Expr.Block(
						Expr.Invoke("log", Expr.Get("ex"))
					)
				),
				Expr.CatchAll(
					Expr.Invoke("doStuff", Expr.Unit()),
					Expr.Invoke("log", Expr.Str("whoopsie"))
				)
			);

			Test(src, result);
		}

		[Test]
		public void Finally()
		{
			var src = @"
try
    1 / 0
finally
    log ""finally""
";

			var result = Expr.Try(
				Expr.Block(
					Expr.Div(Expr.Int(1), Expr.Int(0))
				),
				Expr.Block(
					Expr.Invoke("log", Expr.Str("finally"))
				)
			);

			Test(src, result);
		}

		[Test]
		public void Typeof()
		{
			var src = "let a = typeof int";
			var result = Expr.Let("a", Expr.Typeof("int"));
			Test(src, result);
		}

		[Test]
		public void DefaultOf()
		{
			var src = "let b = default System.Collections.Generic.List<int>";
			var result = Expr.Let("b", Expr.Default("System.Collections.Generic.List<int>"));
			Test(src, result);
		}

		[Test]
		public void Not()
		{
			var src = "not a && b";
			var result = Expr.And(
				Expr.Not(Expr.Get("a")),
				Expr.Get("b")
			);

			Test(src, result);
		}

		[Test]
		public void Cast()
		{
			var src = "let a = (b as List<int>).Count";
			var result = Expr.Let(
				"a",
				Expr.GetMember(
					Expr.Cast(
						Expr.Get("b"),
						"List<int>"
					),
					"Count"
				)
			);

			Test(src, result);
		}

		[Test]
		public void IsOperator()
		{
			var src = "1 is double";
			var result = Expr.Is(Expr.Int(1), new TypeSignature("double"));
			Test(src, result);
		}

		[Test]
		public void ManyParameters()
		{
			var src = @"test 1337 true ""hello"" (new(13.37; new [1; 2]))";
			var result = Expr.Invoke(
				"test",
				Expr.Int(1337),
				Expr.True(),
				Expr.Str("hello"),
				Expr.Tuple(
					Expr.Double(13.37),
					Expr.Array(
						Expr.Int(1),
						Expr.Int(2)
					)
				)
			);

			Test(src, result);
		}

		[Test]
		public void OperatorPriority1()
		{
			var src = "test a b > 10";
			var result = Expr.Greater(
				Expr.Invoke(
					"test",
					Expr.Get("a"),
					Expr.Get("b")
				),
				Expr.Int(10)
			);

			Test(src, result);
		}

		[Test]
		public void OperatorPriority2()
		{
			var src = "1 + 2 - 3 * 4 / 5 % 6 ** 7";
			var result = Expr.Sub(
				Expr.Add(
					Expr.Int(1),
					Expr.Int(2)
				),
				Expr.Mod(
					Expr.Div(
						Expr.Mult(
							Expr.Int(3),
							Expr.Int(4)
						),
						Expr.Int(5)
					),
					Expr.Pow(
						Expr.Int(6),
						Expr.Int(7)
					)
				)
			);

			Test(src, result);
		}

		[Test]
		public void TrailingWhitespace()
		{
			var src = "1 ";
			var result = Expr.Int(1);
			Test(src, result);
		}

		[Test]
		public void Unit()
		{
			var src = "()";
			var result = Expr.Unit();
			Test(src, result);
		}

		[Test]
		public void DefinitionAndInvocation()
		{
			var src = @"fun test:int -> 10
test ()";
			var definition = Expr.Fun("test", "int", Expr.Int(10));
			var invocation = Expr.Invoke("test", Expr.Unit());

			Test(src, definition, invocation);
		}

		[Test]
		public void ThrowExpression()
		{
			var src = "throw new NotImplementedException ()";
			var result = Expr.Throw("NotImplementedException");

			Test(src, result);
		}

		[Test]
		public void Rethrow()
		{
			var src = @"
try
    1 / 0
catch ex:DivisionByZeroException
    throw";

			var result = Expr.Try(
				Expr.Block(
					Expr.Div(Expr.Int(1), Expr.Int(0))
				),
				Expr.Catch(
					"DivisionByZeroException",
					"ex",
					Expr.Block(Expr.Throw())
				)
			);

			Test(src, result);
		}

		[Test]
		public void LinqCall()
		{
			var src = @"(new [1; 2]).Where (x:int -> x > 1)";
			var result = Expr.Invoke(
				Expr.Array(Expr.Int(1), Expr.Int(2)),
				"Where",
				new LambdaNode
					{
						Arguments = new List<FunctionArgument> { new FunctionArgument("x", "int") },
						Body = Expr.Block(
							Expr.Greater(
								Expr.Get("x"),
								Expr.Int(1)
							)
						)
					}
				);

			Test(src, result);
		}

		[Test]
		public void FieldWithTypeParameters()
		{
			var src = "Enumerable::Empty<int>";
			var result = new GetMemberNode
			{
				StaticType = new TypeSignature("Enumerable"),
				MemberName = "Empty",
				TypeHints = { new TypeSignature("int") }
			};

			Test(src, result);
		}

		[Test]
		public void FluentCall()
		{
			var src = @"Enumerable::Range 1 100
    |> Where (i:int -> i % 2 == 0)
    |> Select (i:int -> i * 2)";

			var result = Expr.Invoke(
				Expr.Invoke(
					Expr.Invoke(Expr.GetMember(new TypeSignature("Enumerable"), "Range"), Expr.Int(1), Expr.Int(100)),
					"Where",
					Expr.Lambda(
						new [] { Expr.Arg("i", "int") },
						Expr.Equal(Expr.Mod(Expr.Get("i"), Expr.Int(2)), Expr.Int())
					)
				),
				"Select",
				Expr.Lambda(
					new [] {Expr.Arg("i", "int") },
					Expr.Mult(Expr.Get("i"), Expr.Int(2))
				)
			);

			Test(src, result);
		}

		[Test]
		public void TypeHints1()
		{
			var src = "Enumerable::Empty<int> ()";
			var result = Expr.Invoke(
				Expr.GetMember("Enumerable", "Empty", "int"),
				Expr.Unit()
			);

			Test(src, result);
		}

		[Test]
		public void TypeHints2()
		{
			var src = @"expr.CallStuff<int, TestType, _> 1 ""hello""";
			var result = Expr.Invoke(
				Expr.GetMember(
					Expr.Get("expr"),
					"CallStuff",
					"int", "TestType", "_"
				),
				Expr.Int(1),
				Expr.Str("hello")
			);

			Test(src, result);
		}

		[Test]
		public void TypeHints3()
		{
			var src = @"let a = SomeType::Method<int, _, System.Uri>";
			var result = Expr.Let(
				"a",
				Expr.GetMember(
					"SomeType",
					"Method",
					"int", "_", "System.Uri"
				)
			);

			Test(src, result);
		}

		[Test]
		public void TypeHints4()
		{
			var src = @"let a = b.Method<int, _, System.Uri>";
			var result = Expr.Let(
				"a",
				Expr.GetMember(
					Expr.Get("b"),
					"Method",
					"int", "_", "System.Uri"
				)
			);

			Test(src, result);
		}

		[Test]
		public void RefArgDeclaration()
		{
			var src = "fun test:object (x:int y:ref int) -> y = x";
			var result = Expr.Fun
			(
				"test",
				new TypeSignature("object"),
				new [] { Expr.Arg("x", "int"), Expr.Arg("y", "int", true) },
				Expr.Set("y", Expr.Get("x"))
			);

			Test(src, result);
		}

		[Test]
		public void RefArgUsage()
		{
			var src = "test x (ref a) (ref b.Field) (ref Type::Field)";
			var result = Expr.Invoke(
				"test",
				Expr.Get("x"),
				Expr.Ref(Expr.Get("a")),
				Expr.Ref(Expr.GetMember(Expr.Get("b"), "Field")),
				Expr.Ref(Expr.GetMember("Type", "Field"))
			);

			Test(src, result);
		}

		[Test]
		public void Algebraic1()
		{
			var src = @"
type TestType
    Value of int
(new Value 1).Tag";

			var result = new NodeBase[]
			{
				Expr.Type(
					"TestType",
					Expr.Label("Value", "int")
				),

				Expr.GetMember(
					Expr.New("Value", Expr.Int(1)),
					"Tag"
				)
			};

			Test(src, result);
		}

		[Test]
		public void Algebraic2()
		{
			var src = @"
type TestType
    Small of int
    Large of int
var a = new Small 1
var b = new Large 100
a.Tag + b.Tag";

			var result = new NodeBase[]
			{
				Expr.Type(
					"TestType",
					Expr.Label("Small", "int"),
					Expr.Label("Large", "int")
				),

				Expr.Var("a", Expr.New("Small", Expr.Int(1))),
				Expr.Var("b", Expr.New("Large", Expr.Int(100))),
				Expr.Add(
					Expr.GetMember(Expr.Get("a"), "Tag"),
					Expr.GetMember(Expr.Get("b"), "Tag")
				)
			};

			Test(src, result);
		}

		[Test]
		public void FunWithIfThenElse()
		{
			var src = @"
fun part (x:int) ->
    if x > 100 then
        (new Large x) as TestType
    else
        new Small x";
			var result = Expr.Fun(
				"part",
				new[]
				{
					Expr.Arg("x", "int")
				},
				Expr.If(
					Expr.Greater(Expr.Get("x"), Expr.Int(100)),
					Expr.Block(
						Expr.Cast(
							Expr.New("Large", Expr.Get("x")),
							"TestType"
							)
						),
					Expr.Block(
						Expr.New("Small", Expr.Get("x"))
						)
					)
				);
			Test(src, result);
		}

		[Test]
		public void Algebraic3()
		{
				var src = @"
type TestType
    Small of int
    Large of int
fun part:TestType (x:int) ->
    if x > 100 then
        (new Large x) as TestType
    else
        new Small x


var a = part 10
new [ a is TestType; a is Small; a is Large ]";

			var result = new NodeBase[]
			{
				Expr.Type(
					"TestType",
					Expr.Label("Small", "int"),
					Expr.Label("Large", "int")
				),
				
				Expr.Fun(
					"part",
					"TestType",
					new [] { Expr.Arg("x", "int") },
					Expr.If(
						Expr.Greater(Expr.Get("x"), Expr.Int(100)),
						Expr.Block(
							Expr.Cast(
								Expr.New("Large", Expr.Get("x")),
								"TestType"
							)
						),
						Expr.Block(
							Expr.New("Small", Expr.Get("x"))
						)
					)
				),
				
				Expr.Var("a", Expr.Invoke("part", Expr.Int(10))),
				Expr.Array(
					Expr.Is(Expr.Get("a"), "TestType"),
					Expr.Is(Expr.Get("a"), "Small"),
					Expr.Is(Expr.Get("a"), "Large")
				)
			};

			Test(src, result);
		}

		[Test]
		public void Records1()
		{
			var src = @"
record Holder
    A : int
    B : int
var a = new Holder 2 3
a.A * a.B
";
			var result = new NodeBase[]
			{
				Expr.Record(
					"Holder",
					Expr.Field("A", "int"),
					Expr.Field("B", "int")
				),

				Expr.Var(
					"a",
					Expr.New("Holder", Expr.Int(2), Expr.Int(3))
				),
				Expr.Mult(
					Expr.GetMember(Expr.Get("a"), "A"),
					Expr.GetMember(Expr.Get("a"), "B")
				)
			};
			Test(src, result);
		}

		[Test]
		public void Records2()
		{
			var src = @"
record First
    A : int
record Second
    B : int
var a = new First 2
var b = new Second 3
a.A * b.B
";
			var result = new NodeBase[]
			{
				Expr.Record("First", Expr.Field("A", "int")),
				Expr.Record("Second", Expr.Field("B", "int")),

				Expr.Var("a", Expr.New("First", Expr.Int(2))),
				Expr.Var("b", Expr.New("Second", Expr.Int(3))),
				Expr.Mult(
					Expr.GetMember(Expr.Get("a"), "A"),
					Expr.GetMember(Expr.Get("b"), "B")
				)
			};

			Test(src, result);
		}

		[Test]
		public void RefFunction1()
		{
			var src = @"
var x = 0
int::TryParse ""100"" (ref x)
x";
			var result = new NodeBase[]
			{
				Expr.Var("x", Expr.Int(0)),
				Expr.Invoke(
					"int", "TryParse",
					Expr.Str("100"),
					Expr.Ref(Expr.Get("x"))
				),
				Expr.Get("x")
			};

			Test(src, result);
		}

		[Test]
		public void RefFunction2()
		{
			var src = @"
fun test (a:int x:ref int) -> x = a * 2
var result = 0
test 21 (ref result)
result
";
			var result = new NodeBase[]
			{
				Expr.Fun(
					"test",
					new [] { Expr.Arg("a", "int"), Expr.Arg("x", "int", true) },
					Expr.Set(
						"x",
						Expr.Mult(Expr.Get("a"), Expr.Int(2))
					)
				),
				Expr.Var("result", Expr.Int(0)),
				Expr.Invoke("test", Expr.Int(21), Expr.Ref(Expr.Get("result"))),
				Expr.Get("result")
			};

			Test(src, result);
		}

		[Test]
		public void IfThenElse()
		{
			var src = @"
if x then
    1
else
    2
";
			var result = Expr.If(Expr.Get("x"), Expr.Block(Expr.Int(1)), Expr.Block(Expr.Int(2)));
			Test(src, result);
		}

		[Test]
		public void VariableLocation()
		{
			var script = @"x  + 1";
			var result = Parse(script);
			var node = (AddOperatorNode)result.Single();
			var variable = node.LeftOperand;

			Assert.AreEqual(new LexemLocation { Line = 1, Offset = 1 }, variable.StartLocation);
			Assert.AreEqual(new LexemLocation { Line = 1, Offset = 2 }, variable.EndLocation);
		}

		[Test]
		public void ArgumentLocation()
		{
			var script = "int::TryParse (ref x)";
			var result = Parse(script);
			var node = (InvocationNodeBase)result.Single();
			var argument = node.Arguments.Single();

			Assert.AreEqual(new LexemLocation { Line = 1, Offset = 15 }, argument.StartLocation);
		}

		[Test]
		public void TypeSignatureLocation()
		{
			var script = "new SomeType ()";
			var result = Parse(script);
			var node = (NewObjectNode)result.Single();
			var typeSignature = node.TypeSignature;

			Assert.AreEqual(new LexemLocation { Line = 1, Offset = 5 }, typeSignature.StartLocation);
			Assert.AreEqual(new LexemLocation { Line = 1, Offset = 13 }, typeSignature.EndLocation);
		}

		[Test]
		public void ComplexWhileTest()
		{
			var src = @"using System.Net

let listener = new HttpListener ()
listener.Prefixes.Add ""http://127.0.0.1:8080/""
listener.Prefixes.Add ""http://localhost:8080/""

var count = 1

while true do
    listener.Start ()

    let ctx = listener.GetContext ()
    let rq = ctx.Request
    let resp = ctx.Response

    let respStr = fmt ""Hello from LENS! This page has been viewed {0} times."" count
    let buf = Encoding::UTF8.GetBytes respStr

    resp.ContentLength64 = buf.Length
    let output = resp.OutputStream
    output.Write buf 0 (buf.Length)
    output.Close ()

    listener.Stop ()

    count = count + 1
";
			Parse(src);
		}

		[Test]
		public void NonInitializedVariable()
		{
			var src = @"
var a : int
a = 1";
			var result = new NodeBase[]
			{
				Expr.Var("a", "int"),
				Expr.Set("a", Expr.Int(1))
			};

			Test(src, result);
		}

		[Test]
		public void For1()
		{
			var src = "for x in y do test ()";
			var result = new NodeBase[]
				{
					Expr.For(
						"x",
						Expr.Get("y"),
						Expr.Block(
							Expr.Invoke("get")
						)
					)
				};

			Test(src, result);
		}

		[Test]
		public void For2()
		{
			var src = @"
for a in x..y do
    test ()";
			var result = new NodeBase[]
				{
					Expr.For(
						"a",
						Expr.Get("x"),
						Expr.Get("y"),
						Expr.Block(
							Expr.Invoke("get")
						)
					)
				};

			Test(src, result);
		}

		[Test]
		public void PureFunction()
		{
			var src = @"pure fun add:int (x:int y:int) -> x + y";

			var result = Expr.Fun(
				"add",
				"int",
				true,
				new[] { Expr.Arg("x", "int"), Expr.Arg("y", "int") },
				Expr.Add(
					Expr.Get("x"),
					Expr.Get("y")
				)
			);

			Test(src, result);
		}

		[Test]
		public void AssignmentLocation()
		{
			var script = @"let x = 10
x = 20";
			var result = Parse(script);
			var node2 = result.Skip(1).Single();
			Assert.AreEqual(2, node2.StartLocation.Line);
		}

		[Test]
		public void MemberAssignmentLocation()
		{
			var script = @"a.b = 1";
			var result = Parse(script);
			var node2 = result.Single();
			Assert.AreEqual(1, node2.StartLocation.Line);
		}

		private static void Test(string source, params NodeBase[] expected)
		{
			Assert.AreEqual(expected, Parse(source).ToArray());
		}

		private static IEnumerable<NodeBase> Parse(string source)
		{
			var lexer = new LensLexer(source);
			var parser = new LensParser(lexer.Lexems);
			return parser.Nodes;
		}
	}
}
