using System;
using System.Collections.Generic;
using System.Drawing;
using Antlr.Runtime;
using ICSharpCode.TextEditor;
using ICSharpCode.TextEditor.Document;
using Nexus.Client.Util.Antlr;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.CPL.Controls
{
	/// <summary>
	/// The highlighting strategy for the Conditional Pattern Language.
	/// </summary>
	/// <remarks>
	/// This is used to highilight CPL in the editor.
	/// </remarks>
	public class CplHighlightingStrategy : IHighlightingStrategy
	{
		public enum TokenClass
		{
			Operator,
			String,
			Keyword,
			Variable,
			Default
		}

		Dictionary<string, string> properties = new Dictionary<string, string>();
		DefaultHighlightingStrategy defaultHighlightingStrategy = new DefaultHighlightingStrategy();

		#region IHighlightingStrategy Members

		/// <summary>
		/// Gets the file extensions that this highlighting strategy can
		/// be used for.
		/// </summary>
		/// <remarks>
		/// CPL is not stored in files, and so there are no extensions.
		/// </remarks>
		/// <value>The file extensions that this highlighting strategy can
		/// be used for.</value>
		public string[] Extensions
		{
			get
			{
				return new string[] { };
			}
		}

		/// <summary>
		/// Gets the <see cref="HighlightColor"/> for the specified class of
		/// token.
		/// </summary>
		/// <param name="p_strTokenClassName">The name of the token class for which the
		/// <see cref="HighlightColor"/> is to be returned.</param>
		/// <returns>The <see cref="HighlightColor"/> for the specified class of
		/// token.</returns>
		public HighlightColor GetColorFor(string p_strColourName)
		{
			if (Array.Find(Enum.GetNames(typeof(TokenClass)), (x) => { return x.Equals(p_strColourName, StringComparison.InvariantCultureIgnoreCase); }) == null)
				return defaultHighlightingStrategy.GetColorFor(p_strColourName);
			return GetColorFor((TokenClass)Enum.Parse(typeof(TokenClass), p_strColourName, true));
		}

		/// <summary>
		/// Gets the <see cref="HighlightColor"/> for the specified class of
		/// token.
		/// </summary>
		/// <param name="p_ttpTokenClass">The token class for which the
		/// <see cref="HighlightColor"/> is to be returned.</param>
		/// <returns>The <see cref="HighlightColor"/> for the specified class of
		/// token.</returns>
		public HighlightColor GetColorFor(TokenClass p_ttpTokenClass)
		{
			switch (p_ttpTokenClass)
			{
				case TokenClass.Operator:
					return new HighlightColor(Color.ForestGreen, false, false);
				case TokenClass.String:
					return new HighlightColor(Color.Maroon, false, false);
				case TokenClass.Keyword:
					return new HighlightColor(Color.Red, false, false);
				case TokenClass.Variable:
					return new HighlightColor(Color.Blue, false, false);
				default:
					return defaultHighlightingStrategy.GetColorFor("DefaultColor");
			}
		}

		/// <summary>
		/// Highlights the tokens in the given document.
		/// </summary>
		/// <param name="document">The document being highlighted.</param>
		public void MarkTokens(IDocument document)
		{
			MarkTokens(document, new List<LineSegment>(document.LineSegmentCollection));
		}

		/// <summary>
		/// Highlights the tokens in the given lines in the given document.
		/// </summary>
		/// <param name="document">The document being highlighted.</param>
		/// <param name="lines">The lines to highlight.</param>
		public void MarkTokens(IDocument document, List<LineSegment> lines)
		{
			Dictionary<Int32, List<IToken>> dicTokens = new Dictionary<int, List<IToken>>();
			AntlrLexerBase lex = LexerFactory.CreateLexer(document.TextContent, new ErrorTracker());
			for (IToken tknToken = lex.NextToken(); tknToken.Type > -1; tknToken = lex.NextToken())
			{
				if (!dicTokens.ContainsKey(tknToken.Line - 1))
					dicTokens[tknToken.Line - 1] = new List<IToken>();
				dicTokens[tknToken.Line - 1].Add(tknToken);
			}
			foreach (LineSegment lsgLine in lines)
			{
				lsgLine.Words = new List<TextWord>();
				if (dicTokens.ContainsKey(lsgLine.LineNumber))
					foreach (IToken tknToken in dicTokens[lsgLine.LineNumber])
						HandleToken(document, tknToken);
				document.RequestUpdate(new TextAreaUpdate(TextAreaUpdateType.SingleLine, lsgLine.LineNumber));
			}
		}

		/// <summary>
		/// Gets the name of the language this highlighter is for.
		/// </summary>
		/// <value>The name of the language this highlighter is for.</value>
		public string Name
		{
			get
			{
				return "Conditional Pattern Language (CPL)";
			}
		}

		/// <summary>
		/// Gets the properties of this highlighter.
		/// </summary>
		/// <value>The properties of this highlighter.</value>
		public Dictionary<string, string> Properties
		{
			get
			{
				return properties;
			}
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets or sets the factory to use to create the CPL lexer used to highlight the code.
		/// </summary>
		/// <value>The factory to use to create the CPL lexer used to highlight the code.</value>
		protected ICplParserFactory LexerFactory { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple construstor that sets the <see cref="LexerFactory"/> to use.
		/// </summary>
		/// <param name="p_cpfLexerFactory">The factory to use to create the CPL lexer used to highlight the code.</param>
		public CplHighlightingStrategy(ICplParserFactory p_cpfLexerFactory)
		{
			LexerFactory = p_cpfLexerFactory;
		}

		#endregion

		/// <summary>
		/// Determines the token class of the given token type.
		/// </summary>
		/// <param name="p_intTokenType">The token type to classify.</param>
		/// <returns>The <see cref="TokenClass"/> of the given token type.</returns>
		protected virtual TokenClass GetTokenClass(Int32 p_intTokenType)
		{
			switch (p_intTokenType)
			{
				case CPLLexer.OR:
				case CPLLexer.AND:
				case CPLLexer.IS:
					return TokenClass.Operator;
				case CPLLexer.QUOTED_VALUE:
					return TokenClass.String;
				case CPLLexer.FILESTATE:
				case CPLLexer.GAME_VERSION:
				case CPLLexer.MANAGER_VERSION:
					return TokenClass.Keyword;
				case CPLLexer.FLAGNAME:
				case CPLLexer.VERSION:
					return TokenClass.Variable;
				default:
					return TokenClass.Default;
			}
		}

		/// <summary>
		/// Colourises the given token and adds it to the document.
		/// </summary>
		/// <param name="document">The document being highlighted.</param>
		/// <param name="j">The token to highlight.</param>
		protected void HandleToken(IDocument document, IToken j)
		{
			LineSegment lsgLine = document.GetLineSegment(j.Line - 1);
			HighlightColor hclColour = GetColorFor(GetTokenClass(j.Type));
			TextWord twdWord = new TextWord(document, lsgLine, j.CharPositionInLine, j.Text.Length, hclColour, false);
			Int32 i = 0;
			for (; i < lsgLine.Words.Count; i++)
				if (lsgLine.Words[i].Offset > j.CharPositionInLine)
				{
					lsgLine.Words.Insert(i, twdWord);
					break;
				}
			if (lsgLine.Words.Count == i)
				lsgLine.Words.Add(twdWord);
		}
	}
}
