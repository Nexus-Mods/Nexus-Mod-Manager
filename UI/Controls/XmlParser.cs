using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using ICSharpCode.TextEditor;
using ICSharpCode.TextEditor.Document;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// Parses the tags of an XML document.
	/// </summary>
	public class XmlParser
	{
		/// <summary>
		/// The delegate type for the callback methods of <see cref="ParseTags"/>.
		/// </summary>
		/// <param name="p_docDocument">The document being parsed.</param>
		/// <param name="p_strTagName">The name of the complete tag that was parsed.</param>
		/// <param name="p_tlcStart">The location of the start of the tag.</param>
		/// <param name="p_tlcEnd">The location of the end of the tag.</param>
		public delegate void ParsedTag(IDocument p_docDocument, string p_strTagName, TextLocation p_tlcStart, TextLocation p_tlcEnd);

		/// <summary>
		/// A regular expression that extracts the contents of a tag.
		/// </summary>
		private static Regex rgxTagContents = new Regex("<([^!>][^>]*)>?", RegexOptions.Singleline);

		/// <summary>
		/// A regular expression that extracts the name of a tag.
		/// </summary>
		private static Regex rgxTagName = new Regex(@"([^!/\s][^/\s]*)");

		/// <summary>
		/// A stack that holds tags and their positions.
		/// </summary>
		public class TagStack : LinkedList<TagStack.TagPosition>
		{
			/// <summary>
			/// Encapsulates a tag and it's position.
			/// </summary>
			public class TagPosition : IEquatable<string>
			{
				private string m_strName = null;
				private Int32 m_intLineNumber = 0;
				private Int32 m_intColumn = 0;

				#region Properties

				/// <summary>
				/// Gets the name of the tag.
				/// </summary>
				/// <value>The name of the tag.</value>
				public string Name
				{
					get
					{
						return m_strName;
					}
				}

				/// <summary>
				/// Gets the line number of the tag in the document.
				/// </summary>
				/// <value>The line number of the tag in the document.</value>
				public Int32 LineNumber
				{
					get
					{
						return m_intLineNumber;
					}
				}

				/// <summary>
				/// Gets the column of the tag.
				/// </summary>
				/// <value>The column of the tag.</value>
				public Int32 Column
				{
					get
					{
						return m_intColumn;
					}
				}

				#endregion

				#region Constructors

				/// <summary>
				/// A simple constructor that initializes the object with the given values.
				/// </summary>
				/// <param name="p_strName">The name of the tag.</param>
				/// <param name="p_intLineNumber">The line number of the tag in the document.</param>
				/// <param name="p_intColumn">The column of the tag.</param>
				public TagPosition(string p_strName, Int32 p_intLineNumber, Int32 p_intColumn)
				{
					m_strName = p_strName;
					m_intLineNumber = p_intLineNumber;
					m_intColumn = p_intColumn;
				}

				#endregion

				#region IEquatable<string> Members

				/// <summary>
				/// Determines if this object is equal to the given string.
				/// </summary>
				/// <remarks>
				/// This <see cref="TagPosition"/> is equal to the given string if and only if
				/// the string is equal to this <see cref="TagPosition"/>'s <see cref="Name"/>.
				/// </remarks>
				/// <param name="other">The string to compare to this object.</param>
				/// <returns><c>true</c> if the given string is equal to this object;
				/// <c>false</c> otherwise.</returns>
				public bool Equals(string other)
				{
					if (String.IsNullOrEmpty(Name))
					{
						if (String.IsNullOrEmpty(other))
							return true;
						return other.Equals(Name);
					}
					return Name.Equals(other);
				}

				#endregion
			}

			/// <summary>
			/// Determines if the stack contains the specified tag.
			/// </summary>
			/// <param name="p_strTagName">The name of the tag for whose presence is to be determined.</param>
			/// <returns><c>true</c> if the stack contains the specified tag;
			/// <c>false</c> otherwise.</returns>
			public bool Contains(string p_strTagName)
			{
				for (LinkedListNode<TagPosition> llnCurrent = Last; llnCurrent != null; llnCurrent = llnCurrent.Previous)
					if (llnCurrent.Value.Equals(p_strTagName))
						return true;
				return false;
			}

			/// <summary>
			/// Push the specified tag onto the stack.
			/// </summary>
			/// <param name="p_strTagName">The name of the tag to add to the stack.</param>
			/// <param name="p_intLineNumber">The line number of the tag to add to the stack.</param>
			/// <param name="p_intColumn">The column of the tag to add to the stack.</param>
			public void Push(string p_strTagName, Int32 p_intLineNumber, Int32 p_intColumn)
			{
				AddLast(new TagPosition(p_strTagName, p_intLineNumber, p_intColumn));
			}

			/// <summary>
			/// Peek at the top tag on the stack.
			/// </summary>
			/// <returns>A <see cref="TagPosition"/> representing the tag on the top of the stack.</returns>
			public TagPosition Peek()
			{
				return Last.Value;
			}

			/// <summary>
			/// Pop off the top tag off the stack.
			/// </summary>
			/// <returns>A <see cref="TagPosition"/> representing the tag on the top of the stack.</returns>
			public TagPosition Pop()
			{
				TagPosition tgpTag = Last.Value;
				RemoveLast();
				return tgpTag;
			}
		}

		/// <summary>
		/// Parses the given document to find all tags between the beginning of the document and the specified
		/// end line.
		/// </summary>
		/// <remarks>
		/// The <paramref name="p_pctCompleteTagCallback"/> is called whenever a complete tag has been parsed.
		/// A complete tag is a tag whose opening and closing tags have been found (for example, &lt;b>..&lt;/b>).
		/// The stack that is returned contains all the unclosed tags found, and so represents where in the
		/// document heirarchy the line falls.
		/// </remarks>
		/// <param name="p_docDocument">The document to parse.</param>
		/// <param name="p_intEndLine">The line of the document at which to stop parsing.</param>
		/// <param name="p_pctCompleteTagCallback">The method to call whenever a complete tag is parsed.</param>
		/// <param name="p_pctUnclosedTagCallback">The method to call whenever an unclosed tag is parsed.</param>
		/// <returns>A stack containing all the unclosed tags found.</returns>
		/// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="p_intEndLine"/> is greater than
		/// or equal to the <see cref="IDocument.TotalNumberOfLines"/> of <paramref name="p_docDocument"/>.</exception>
		public static TagStack ParseTags(IDocument p_docDocument, Int32 p_intEndLine, ParsedTag p_pctCompleteTagCallback, ParsedTag p_pctUnclosedTagCallback)
		{
			if (p_intEndLine >= p_docDocument.TotalNumberOfLines)
				throw new ArgumentOutOfRangeException("p_intEndLine", p_intEndLine, "The given end line paramater is outside of the range of lines in the given document.");
			//parse the buffer
			TagStack stkTags = new TagStack();
			for (Int32 i = 0; i <= p_intEndLine; i++)
			{
				string strLine = p_docDocument.GetText(p_docDocument.GetLineSegment(i));
				Int32 intLineNum = i;
				Int32 intLastOpenPos = strLine.LastIndexOf('<');
				if (intLastOpenPos < 0)
					continue;
				Int32 intLastClosePos = strLine.LastIndexOf('>');
				if ((intLastClosePos > -1) && (intLastOpenPos > intLastClosePos))
				{
					string strNextLine = null;
					StringBuilder stbLines = new StringBuilder(strLine);
					//there is an open tag on this line - read lines until it is closed.
					for (; i <= p_intEndLine; i++)
					{
						strNextLine = p_docDocument.GetText(p_docDocument.GetLineSegment(i));
						intLastClosePos = strLine.LastIndexOf('>');
						stbLines.Append(strNextLine);
						if (intLastClosePos < 0)
						{
							i--;
							break;
						}
					}
					strLine = stbLines.ToString();
				}

				MatchCollection mclLineTags = rgxTagContents.Matches(strLine);
				foreach (Match mtcTag in mclLineTags)
				{
					string strTag = mtcTag.Groups[1].Value.Trim();
					string strTagName = rgxTagName.Match(strTag).Groups[1].Value;
					if (strTag.StartsWith("/"))
					{
						if (stkTags.Contains(strTagName))
						{
							while (!stkTags.Peek().Equals(strTagName))
							{
								TagStack.TagPosition tpsTag = stkTags.Pop();
								TextLocation tlcStart = new TextLocation(tpsTag.Column, tpsTag.LineNumber);
								TextLocation tlcEnd = new TextLocation(tpsTag.Column + tpsTag.Name.Length, tpsTag.LineNumber);
								if (p_pctUnclosedTagCallback != null)
									p_pctUnclosedTagCallback(p_docDocument, tpsTag.Name, tlcStart, tlcEnd);
							}
							TagStack.TagPosition tpsCompleteTag = stkTags.Pop();
							if (p_pctCompleteTagCallback != null)
							{
								TextLocation tlcStart = new TextLocation(tpsCompleteTag.Column, tpsCompleteTag.LineNumber);
								Int32 intEndFoldPos = mtcTag.Groups[1].Index;
								TextLocation tlcEnd = new TextLocation(intEndFoldPos, intLineNum);
								p_pctCompleteTagCallback(p_docDocument, strTagName, tlcStart, tlcEnd);
							}
						}
					}
					else
					{
						if (!strTag.EndsWith("/"))
							stkTags.Push(strTagName, intLineNum, mtcTag.Groups[1].Index);
					}
				}
			}
			return stkTags;
		}

		/// <summary>
		/// Determins if the caret is inside a tag.
		/// </summary>
		/// <param name="p_txaTextArea">The text area containing the caret.</param>
		/// <returns><c>true</c> if the caret is inside a tag; <c>false</c> otherwise.</returns>
		public static bool IsInsideTag(TextArea p_txaTextArea)
		{
			string strText = p_txaTextArea.Document.TextContent.Substring(0, p_txaTextArea.Caret.Offset);
			return strText.LastIndexOf('<') > strText.LastIndexOf('>');
		}
	}
}
