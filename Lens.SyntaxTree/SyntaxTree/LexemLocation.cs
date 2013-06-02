﻿namespace Lens.SyntaxTree.SyntaxTree
{
	/// <summary>
	/// The position of a caret in the text.
	/// </summary>
	public struct LexemLocation
	{
		public int Line;
		public int Offset;

		public bool IsEmpty
		{
			get { return Line == 0 && Offset == 0; }
		}
	}
}
