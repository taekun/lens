﻿using System.Collections.Generic;
using System.Linq;
using Lens.Lexer;
using Lens.Parser;
using Lens.SyntaxTree;
using NUnit.Framework;

namespace Lens.Test
{
	using System.Reflection;


	internal class TestBase
	{
		protected static void Test(string src, object value, bool testConstants = false)
		{
			Assert.AreEqual(value, Compile(src, new LensCompilerOptions { UnrollConstants = true, AllowSave = true }));
			if (testConstants)
				Assert.AreEqual(value, Compile(src));
		}

		protected static void Test(IEnumerable<NodeBase> nodes, object value, bool testConstants = false)
		{
			Assert.AreEqual(value, Compile(nodes, new LensCompilerOptions {UnrollConstants = true}));
			if (testConstants)
				Assert.AreEqual(value, Compile(nodes));
		}

		protected static void TestError(string src, string msg)
		{
			var exception = Assert.Throws<LensCompilerException>(() => Compile(src));
			var srcId = exception.Message.Substring(0, 6);
			var msgId = msg.Substring(0, 6);

			Assert.IsTrue(
				srcId == msgId,
				"Message does not match!\nExpected: {0}\nActual: {1}!",
				msg,
				exception.Message
			);
		}

		protected static void Test(string src, object value, LensCompilerOptions opts)
		{
			Assert.AreEqual(value, Compile(src, opts));
		}

		protected static void Test(IEnumerable<NodeBase> nodes, object value, LensCompilerOptions opts)
		{
			Assert.AreEqual(value, Compile(nodes, opts));
		}

		protected static void TestParser(string source, params NodeBase[] expected)
		{
			Assert.AreEqual(expected, Parse(source).ToArray());
		}

		protected static IEnumerable<NodeBase> Parse(string source)
		{
			var lexer = new LensLexer(source);
			var parser = new LensParser(lexer.Lexems);
			return parser.Nodes;
		}

		protected static object Compile(string src, LensCompilerOptions opts = null)
		{
			return createCompiler(opts).Run(src);
		}

		protected static object Compile(IEnumerable<NodeBase> nodes, LensCompilerOptions opts = null)
		{
			return createCompiler(opts).Run(nodes);
		}

		private static LensCompiler createCompiler(LensCompilerOptions opts)
		{
			var compiler = new LensCompiler(opts ?? new LensCompilerOptions { AllowSave = true });
			compiler.RegisterAssembly(Assembly.Load("System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"));
			return compiler;
		}
	}
}
