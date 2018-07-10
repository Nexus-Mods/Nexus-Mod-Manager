using System;
using System.ComponentModel;
using System.Windows.Forms;
using ICSharpCode.TextEditor;
using ICSharpCode.TextEditor.Document;
using Nexus.Client.Util;
using Nexus.Client.Util.Antlr;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.CPL.Controls
{
	/// <summary>
	/// And editor for the Conditional Pattern Language.
	/// </summary>
	public class CPLTextEditor : TextEditorControl
	{
		private CPLTextEditorVM m_vmlViewModel = null;
		private Timer m_tmrValidator = new Timer();
		private bool m_booUpdatingCode = false;

		#region Properties

		public CPLTextEditorVM ViewModel
		{
			private get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				Document.TextContent = ViewModel.Code;
				value.PropertyChanged += new PropertyChangedEventHandler(ViewModel_PropertyChanged);
				Document.HighlightingStrategy = value.HighlightingStrategy;
				value.CodeValidated += new EventHandler(CodeValidated);
				ValidateCPL();
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public CPLTextEditor()
		{
			ActiveTextAreaControl.Caret.PositionChanged += new EventHandler(Caret_PositionChanged);
			m_tmrValidator.Tick += ValidateOnTimer;
			m_tmrValidator.Interval = 2000;
		}

		#endregion

		private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (!m_booUpdatingCode)
			{
				if (e.PropertyName.Equals(ObjectHelper.GetPropertyName(() => ViewModel.Code)))
				{
					Document.TextContent = ViewModel.Code;
					ValidateCPL();
				}
				else if (e.PropertyName.Equals(ObjectHelper.GetPropertyName(() => ViewModel.CaretPosition)))
				{
					ActiveTextAreaControl.Caret.Position = Document.OffsetToPosition(ViewModel.CaretPosition);
				}
			}
		}

		private void Caret_PositionChanged(object sender, EventArgs e)
		{
			m_booUpdatingCode = true;
			ViewModel.CaretPosition = ActiveTextAreaControl.Caret.Offset;
			m_booUpdatingCode = false;
		}

		#region Validation

		/// <summary>
		/// Validates the CPL, after a given period has elapsed.
		/// </summary>
		/// <remarks>
		/// This method is called by a timer after a set span after the text in the editor was last changed.
		/// </remarks>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		void ValidateOnTimer(object sender, EventArgs e)
		{
			ValidateCPL();
		}

		/// <summary>
		/// Validates the CPL.
		/// </summary>
		public void ValidateCPL()
		{
			m_tmrValidator.Stop();
			ViewModel.ValidateCPL();
		}

		private void CodeValidated(object sender, EventArgs e)
		{
			IDocument docDocument = ActiveTextAreaControl.TextArea.Document;
			docDocument.MarkerStrategy.RemoveAll(x => { return (x.TextMarkerType == TextMarkerType.WaveLine); });

			foreach (LanguageError errError in ViewModel.Errors)
			{
				LineSegment lsgLine = docDocument.GetLineSegment(errError.Line);
				TextWord twdWord = lsgLine.GetWord(errError.Column);
				Int32 intWordOffest = docDocument.PositionToOffset(new TextLocation(errError.Column, errError.Line));
				TextMarker tmkError = new TextMarker(intWordOffest, (twdWord == null) ? 1 : twdWord.Length, TextMarkerType.WaveLine);
				tmkError.ToolTip = errError.Message;
				docDocument.MarkerStrategy.AddMarker(tmkError);
			}
			ActiveTextAreaControl.TextArea.Invalidate();
		}

		/// <summary>
		/// Starts the timers to validate the CPL.
		/// </summary>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnTextChanged(EventArgs e)
		{
			m_tmrValidator.Stop();
			m_tmrValidator.Start();

			base.OnTextChanged(e);
			m_booUpdatingCode = true;
			ViewModel.Code = Document.TextContent;
			m_booUpdatingCode = false;
		}

		#endregion
	}
}
