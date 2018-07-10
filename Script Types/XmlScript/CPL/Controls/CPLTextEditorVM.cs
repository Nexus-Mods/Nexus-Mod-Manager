using System;
using System.Collections.Generic;
using ICSharpCode.TextEditor.Document;
using Nexus.Client.Util;
using Nexus.Client.Util.Antlr;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.CPL.Controls
{
	public class CPLTextEditorVM : ObservableObject
	{
		public event EventHandler CodeValidated = delegate { };

		private string m_strCode = null;
		private Int32 m_intCaretPosition = 0;

		#region Properties

		public IHighlightingStrategy HighlightingStrategy { get; private set; }

		/// <summary>
		/// Gets or sets the CPL Parser factory to use to create the parser to validate the code.
		/// </summary>
		/// <value>The CPL Parser factory to use to create the parser to validate the code.</value>
		public ICplParserFactory CPLParserFactory { get; private set; }

		public Int32 CaretPosition 
		{
			get
			{
				return m_intCaretPosition;
			}
			set
			{
				SetPropertyIfChanged(ref m_intCaretPosition, value, () => CaretPosition);
			}
		}

		public string Code
		{
			get
			{
				return m_strCode ?? "";
			}
			set
			{
				SetPropertyIfChanged(ref m_strCode, value, () => Code);
			}
		}

		public List<LanguageError> Errors { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public CPLTextEditorVM(IHighlightingStrategy p_hltHighlighter, ICplParserFactory p_pftCPLParserFactory)
		{
			HighlightingStrategy = p_hltHighlighter;
			CPLParserFactory = p_pftCPLParserFactory;
		}

		#endregion

		#region Validation

		/// <summary>
		/// Validates the CPL.
		/// </summary>
		/// <returns><c>true</c> if the CPL is valid; <c>false</c> otherwise.</returns>
		public bool ValidateCPL()
		{
			bool booIsValid = true;
			if (Code.Length > 0)
			{
				ErrorTracker et = new ErrorTracker();
				CPLParserFactory.CreateParser(Code, et).Parse();
				List<LanguageError> lstErrors = new List<LanguageError>(et.ParserErrors);
				lstErrors.AddRange(et.LexerErrors);

				Errors = lstErrors;
				booIsValid = (lstErrors.Count == 0);
			}
			else
				Errors = new List<LanguageError>();
			CodeValidated(this, new EventArgs());
			return booIsValid;
		}

		#endregion
	}
}
