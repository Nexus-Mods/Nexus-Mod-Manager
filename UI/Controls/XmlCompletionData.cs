using System;
using ICSharpCode.TextEditor;
using ICSharpCode.TextEditor.Gui.CompletionWindow;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// Describes a selection in a code completion dialog.
	/// </summary>
	public class XmlCompletionData : DefaultCompletionData
	{
		private AutoCompleteType m_actCompletionType = AutoCompleteType.Element;

		#region Properties

		/// <summary>
		/// Gets the type of the completion.
		/// </summary>
		/// <value>The type of the completion.</value>
		public AutoCompleteType CompletionType
		{
			get
			{
				return m_actCompletionType;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_actType">The type of the completion.</param>
		/// <param name="p_strName">The name of the selection.</param>
		/// <param name="p_strDescription">The description of the selection.</param>
		public XmlCompletionData(AutoCompleteType p_actType, string p_strName, string p_strDescription)
			: base(p_strName, p_strDescription, (p_actType == AutoCompleteType.AttributeValues) ? 2 : ((p_actType == AutoCompleteType.Attribute) ? 1 : 0))
		{
			m_actCompletionType = p_actType;
		}

		#endregion

		/// <summary>
		/// Inserts this selection into the document.
		/// </summary>
		/// <param name="textArea">The text area into which to insert the selection.</param>
		/// <param name="ch">The character that was used to choose this completion selection.</param>
		/// <returns><c>true</c> if the insertion of <paramref name="p_chrKey"/> was handled;
		/// <c>false</c> otherwise.</returns>
		public override bool InsertAction(TextArea textArea, char ch)
		{
			switch (m_actCompletionType)
			{
				case AutoCompleteType.Attribute:
				case AutoCompleteType.AttributeValues:
					textArea.InsertString(Text);
					return false;
				case AutoCompleteType.Element:
					if (Text.EndsWith("["))
					{
						Caret crtCaret = textArea.Caret;
						textArea.InsertString(String.Concat(Text, "]]>"));
						crtCaret.Position = textArea.Document.OffsetToPosition(crtCaret.Offset - 3);
						return false;
					}
					break;
			}
			return base.InsertAction(textArea, ch);
		}
	}
}
