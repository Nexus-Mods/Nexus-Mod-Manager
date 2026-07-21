using System;
using System.Text;
using ICSharpCode.TextEditor;
using ICSharpCode.TextEditor.Document;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// Handles the smart indenting of XML.
	/// </summary>
	public class XmlFormattingStrategy : DefaultFormattingStrategy
	{
		/// <summary>
		/// Indents the specified line based on the current depth of the XML hierarchy.
		/// </summary>
		/// <param name="p_txaTextArea">The text area containing the line to indent.</param>
		/// <param name="p_intLineNumber">The line number of the line to indent.</param>
		/// <returns>The indent depth of the specified line.</returns>
		protected override int AutoIndentLine(TextArea p_txaTextArea, int p_intLineNumber)
		{
			XmlParser.TagStack stkTags = XmlParser.ParseTags(p_txaTextArea.Document, p_intLineNumber, null, null);
			Int32 intDepth = 0;
			Int32 intLastLineNum = -1;
			while (stkTags.Count > 0)
			{
				if (stkTags.Peek().LineNumber != intLastLineNum)
				{
					intLastLineNum = stkTags.Peek().LineNumber;
					intDepth++;
				}
				stkTags.Pop();
			}

			StringBuilder stbLineWithIndent = new StringBuilder();
			for (Int32 i = 0; i < intDepth; i++)
				stbLineWithIndent.Append("\t");
			stbLineWithIndent.Append(TextUtilities.GetLineAsString(p_txaTextArea.Document, p_intLineNumber).Trim());
			LineSegment oldLine = p_txaTextArea.Document.GetLineSegment(p_intLineNumber);
			Int32 intCaretOffset = stbLineWithIndent.Length - oldLine.Length;
			SmartReplaceLine(p_txaTextArea.Document, oldLine, stbLineWithIndent.ToString());
			p_txaTextArea.Caret.Column += intCaretOffset;

			return intDepth;
		}
	}
}
