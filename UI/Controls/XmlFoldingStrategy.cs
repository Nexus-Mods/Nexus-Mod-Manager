using System;
using System.Collections.Generic;
using ICSharpCode.TextEditor;
using ICSharpCode.TextEditor.Document;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// Handles the folding of XML.
	/// </summary>
	public class XmlFoldingStrategy : IFoldingStrategy
	{
		private List<FoldMarker> m_lstFolds = new List<FoldMarker>();

		#region IFoldingStrategy Members

		/// <summary>
		/// Generates the list of markers indicating where the XML should be folded.
		/// </summary>
		/// <param name="document">The document to fold.</param>
		/// <param name="fileName">The file name of the document to fold.</param>
		/// <param name="parseInformation">User-supplied parse information.</param>
		/// <returns>The list of markers indicating where the XML should be folded.</returns>
		public List<FoldMarker> GenerateFoldMarkers(IDocument document, string fileName, object parseInformation)
		{
			m_lstFolds.Clear();

			XmlParser.ParseTags(document, document.TotalNumberOfLines - 1, AddFold, null);

			return m_lstFolds;
		}

		/// <summary>
		/// Adds a fold for the specified tag spanning the specified lines.
		/// </summary>
		/// <remarks>
		/// This method is called by the <see cref="XmlParser"/> whenever a complete tag is found.
		/// </remarks>
		/// <param name="p_docDocument">The document in which to make the fold.</param>
		/// <param name="p_strTagName">The name of the tag being folded.</param>
		/// <param name="p_tlcStart">The location of the start of the tag.</param>
		/// <param name="p_tlcEnd">The location of the closing tag.</param>
		protected void AddFold(IDocument p_docDocument, string p_strTagName, TextLocation p_tlcStart, TextLocation p_tlcEnd)
		{
			if (p_tlcStart.Line == p_tlcEnd.Line)
				return;
			string strStartLine = p_docDocument.GetText(p_docDocument.GetLineSegment(p_tlcStart.Line));
			Int32 intStartFoldPos = strStartLine.IndexOf(">", p_tlcStart.Column);
			if (intStartFoldPos < 0)
				intStartFoldPos = strStartLine.Length;
			else
				intStartFoldPos++;
			m_lstFolds.Add(new FoldMarker(p_docDocument, p_tlcStart.Line, intStartFoldPos, p_tlcEnd.Line, p_tlcEnd.Column - 1));
		}

		#endregion
	}
}
