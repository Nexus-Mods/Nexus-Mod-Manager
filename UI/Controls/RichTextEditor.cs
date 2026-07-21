using System;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// A rict text editor, with a toolbar.
	/// </summary>
	public partial class RichTextEditor : UserControl
	{
		/// <summary>
		/// The default picklist of font sizes.
		/// </summary>
		private readonly Int32[] FONT_SIZES = { 8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72 };
		private ToolStripButton[] m_tsbJustifications = null;
		private RichTextBox rtbTemp = new RichTextBox();
		private Int32 m_intLastFontIndex = -1;
		private float m_fltLastFontSize = -1;

		#region Properties

		/// <summary>
		/// Gets or sets the text of the rich text editor, including RTF formatting codes.
		/// </summary>
		/// <value>The text of the rich text editor, including RTF formatting codes.</value>
		public string Rtf
		{
			get
			{
				return rtbTextbox.Rtf;
			}
			set
			{
				rtbTextbox.Rtf = value;
			}
		}

		/// <summary>
		/// Gets or sets the text of the rich text editor as plain text.
		/// </summary>
		/// <value>The text of the rich text editor as plain text.</value>
		public override string Text
		{
			get
			{
				return rtbTextbox.Text;
			}
			set
			{
				rtbTextbox.Text = value;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public RichTextEditor()
		{
			InitializeComponent();

			m_tsbJustifications = new ToolStripButton[] { tsbJustifyLeft, tsbJustifyCentre, tsbJustifyRight };
			tsbJustifyLeft.Tag = HorizontalAlignment.Left;
			tsbJustifyCentre.Tag = HorizontalAlignment.Center;
			tsbJustifyRight.Tag = HorizontalAlignment.Right;
			tsbJustifyLeft.Checked = true;

			InstalledFontCollection ifcInstalledFonts = new InstalledFontCollection();
			foreach (FontFamily ffmFont in ifcInstalledFonts.Families)
				if (ffmFont.IsStyleAvailable(FontStyle.Regular))
				{
					tscbFont.Items.Add(ffmFont);
					if (ffmFont.Name.Equals(rtbTextbox.SelectionFont.Name))
						tscbFont.SelectedItem = ffmFont;
				}
			tscbFont.ComboBox.DisplayMember = "Name";
			tscbFont.ComboBox.ValueMember = "Name";
			m_intLastFontIndex = tscbFont.SelectedIndex;

			tscbFontSize.ComboBox.DataSource = FONT_SIZES;
			m_fltLastFontSize = float.Parse(tscbFontSize.Text);

			tsbBold.Tag = FontStyle.Bold;
			tsbItalic.Tag = FontStyle.Italic;
			tsbUnderline.Tag = FontStyle.Underline;
			tsbStrikeout.Tag = FontStyle.Strikeout;
		}

		#endregion

		#region Change Font

		/// <summary>
		/// Changes the font of the current selection.
		/// </summary>
		protected void ChangeFont(string p_strFontFamilyName, float p_fltFontSize)
		{
			if (p_fltFontSize <= 0.0)
				throw new ArgumentOutOfRangeException("Invalid font size parameter to ChangeFontSize");

			// setting the font style using the SelectionFont doen't work because
			// a null selection font is returned for a selection with more 
			// than one font style

			Int32 intSelectionStart = rtbTextbox.SelectionStart;
			Int32 intSelectionLength = rtbTextbox.SelectionLength;
			Int32 intTempStart = 0;

			// if there is text selected, and only one style in the selection, change it
			if (intSelectionLength <= 1 && rtbTextbox.SelectionFont != null)
			{
				rtbTextbox.SelectionFont = new Font(p_strFontFamilyName, p_fltFontSize, rtbTextbox.SelectionFont.Style);
				return;
			}

			// walk through the selected text
			rtbTemp.Rtf = rtbTextbox.SelectedRtf;
			for (int i = 0; i < intSelectionLength; ++i)
			{
				rtbTemp.Select(intTempStart + i, 1);
				rtbTemp.SelectionFont = new Font(p_strFontFamilyName, p_fltFontSize, rtbTemp.SelectionFont.Style);
			}

			// replace and reselect
			rtbTemp.Select(intTempStart, intSelectionLength);
			rtbTextbox.SelectedRtf = rtbTemp.SelectedRtf;
			rtbTextbox.Select(intSelectionStart, intSelectionLength);
			return;
		}

		/// <summary>
		/// Handles the <see cref="ComboBox.SelectedIndexChanged"/> event of the font selector.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void tscbFont_SelectedIndexChanged(object sender, EventArgs e)
		{
			m_intLastFontIndex = tscbFont.SelectedIndex;
			float fltFontSize = (rtbTextbox.SelectionFont == null) ? float.Parse(tscbFontSize.Text) : rtbTextbox.SelectionFont.Size;
			ChangeFont(((FontFamily)tscbFont.SelectedItem).Name, fltFontSize);
		}

		/// <summary>
		/// Handles the <see cref="Control.Leave"/> event of the font selector.
		/// </summary>
		/// <remarks>
		/// This selects the previously selected font if the contents of the font selector don't match a
		/// font in the list.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void tscbFont_Leave(object sender, EventArgs e)
		{
			if (tscbFont.SelectedIndex < 0)
				tscbFont.SelectedIndex = m_intLastFontIndex;
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.TextChanged"/> event of the font size selector.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void tscbFontSize_TextChanged(object sender, EventArgs e)
		{
			float fltFontSize = -1;
			if (float.TryParse(tscbFontSize.Text, out fltFontSize))
			{
				m_fltLastFontSize = fltFontSize;
				ChangeFont(((FontFamily)tscbFont.SelectedItem).Name, fltFontSize);
			}
		}

		/// <summary>
		/// Handles the <see cref="Control.Leave"/> event of the font size selector.
		/// </summary>
		/// <remarks>
		/// This selects the previously selected font size if the contents of the font selector is not
		/// a valid number.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void tscbFontSize_Leave(object sender, EventArgs e)
		{
			float fltFontSize = -1;
			if (!float.TryParse(tscbFontSize.Text, out fltFontSize))
				tscbFontSize.Text = m_fltLastFontSize.ToString();
		}

		#endregion

		#region Change font style

		/// <summary>
		/// Changes the font style of the current selection.
		/// </summary>
		public void ChangeFontStyle(FontStyle p_fstStyle, bool p_booAddStyle)
		{
			int rtb1start = rtbTextbox.SelectionStart;
			int len = rtbTextbox.SelectionLength;
			int rtbTempStart = 0;

			// if there is text selected, and only one style in the selection, change it
			if (len <= 1 && rtbTextbox.SelectionFont != null)
			{
				if (p_booAddStyle)
					rtbTextbox.SelectionFont = new Font(rtbTextbox.SelectionFont, rtbTextbox.SelectionFont.Style | p_fstStyle);
				else
					rtbTextbox.SelectionFont = new Font(rtbTextbox.SelectionFont, rtbTextbox.SelectionFont.Style & ~p_fstStyle);
				return;
			}

			// walk through the selected text
			rtbTemp.Rtf = rtbTextbox.SelectedRtf;
			for (int i = 0; i < len; ++i)
			{
				rtbTemp.Select(rtbTempStart + i, 1);
				if (p_booAddStyle)
					rtbTemp.SelectionFont = new Font(rtbTemp.SelectionFont, rtbTemp.SelectionFont.Style | p_fstStyle);
				else
					rtbTemp.SelectionFont = new Font(rtbTemp.SelectionFont, rtbTemp.SelectionFont.Style & ~p_fstStyle);
			}

			// replace and reselect
			rtbTemp.Select(rtbTempStart, len);
			rtbTextbox.SelectedRtf = rtbTemp.SelectedRtf;
			rtbTextbox.Select(rtb1start, len);
			return;
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> events of the font style buttons.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void FontStyleChanged(object sender, EventArgs e)
		{
			ToolStripButton tsbFontStyle = (ToolStripButton)sender;
			ChangeFontStyle((FontStyle)tsbFontStyle.Tag, tsbFontStyle.Checked);
		}

		#endregion

		#region Text Justification

		/// <summary>
		/// Handles the <see cref="Control.Click"/> events of the text justification buttons.
		/// </summary>
		/// <remarks>
		/// The <see cref="RichTextBox"/> doesn't support full justification.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void JustifyText(object sender, EventArgs e)
		{
			foreach (ToolStripButton tsbJustification in m_tsbJustifications)
				tsbJustification.Checked = (tsbJustification == sender);
			rtbTextbox.SelectionAlignment = (HorizontalAlignment)((ToolStripButton)sender).Tag;
		}

		#endregion

		#region Update Toolbar

		/// <summary>
		/// Gets the details of the font at the caret's current position.
		/// </summary>
		/// <returns>A font with all the characteristics of the font at the current caret position.</returns>
		public Font GetFontDetails()
		{
			//This method should handle cases that occur when multiple fonts/styles are selected

			Int32 intSelectionStart = rtbTextbox.SelectionStart;
			Int32 intSelectionLength = rtbTextbox.SelectionLength;
			Int32 intTempStart = 0;

			if (intSelectionLength <= 1)
			{
				// Return the selection or default font
				if (rtbTextbox.SelectionFont != null)
					return rtbTextbox.SelectionFont;
				else
					return rtbTextbox.Font;
			}

			// Step through the selected text one char at a time	
			// after setting defaults from first char
			rtbTemp.Rtf = rtbTextbox.SelectedRtf;

			//Turn everything on so we can turn it off one by one
			FontStyle replystyle =
				FontStyle.Bold | FontStyle.Italic | FontStyle.Strikeout | FontStyle.Underline;

			// Set reply font, size and style to that of first char in selection.
			rtbTemp.Select(intTempStart, 1);
			string replyfont = rtbTemp.SelectionFont.Name;
			float replyfontsize = rtbTemp.SelectionFont.Size;
			replystyle = replystyle & rtbTemp.SelectionFont.Style;

			// Search the rest of the selection
			for (int i = 1; i < intSelectionLength; ++i)
			{
				rtbTemp.Select(intTempStart + i, 1);

				// Check reply for different style
				replystyle = replystyle & rtbTemp.SelectionFont.Style;

				// Check font
				if (replyfont != rtbTemp.SelectionFont.FontFamily.Name)
					replyfont = "";

				// Check font size
				if (replyfontsize != rtbTemp.SelectionFont.Size)
					replyfontsize = (float)0.0;
			}

			// Now set font and size if more than one font or font size was selected
			if (replyfont == "")
				replyfont = rtbTemp.Font.FontFamily.Name;

			if (replyfontsize == 0.0)
				replyfontsize = rtbTemp.Font.Size;

			// generate reply font
			Font reply
				= new Font(replyfont, replyfontsize, replystyle);

			return reply;
		}

		/// <summary>
		/// Update the toolbar button statuses to math the font at the current caret position.
		/// </summary>
		public void UpdateToolbar()
		{
			Font fnt = GetFontDetails();
			FontStyle style = fnt.Style;

			tsbBold.Checked = fnt.Bold;
			tsbItalic.Checked = fnt.Italic;
			tsbUnderline.Checked = fnt.Underline;
			tsbStrikeout.Checked = fnt.Strikeout;
			tsbJustifyLeft.Checked = (rtbTextbox.SelectionAlignment == HorizontalAlignment.Left);
			tsbJustifyCentre.Checked = (rtbTextbox.SelectionAlignment == HorizontalAlignment.Center);
			tsbJustifyRight.Checked = (rtbTextbox.SelectionAlignment == HorizontalAlignment.Right);

			//Check the correct color
			/*foreach (MenuItem mi in cmColors.MenuItems)
				mi.Checked = (rtb1.SelectionColor == Color.FromName(mi.Text));*/

			//Check the correct font
			foreach (FontFamily ffmFont in tscbFont.Items)
				if (ffmFont.Name.Equals(fnt.FontFamily.Name))
				{
					tscbFont.SelectedItem = ffmFont;
					break;
				}

			tscbFontSize.Text = fnt.SizeInPoints.ToString();
		}

		/// <summary>
		/// Handles the <see cref="RichTextBox.SelectionChanged"/> event of the rich text box.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void rtbTextbox_SelectionChanged(object sender, EventArgs e)
		{
			UpdateToolbar();
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="Control.KeyDown"/> event of the rich text box.
		/// </summary>
		/// <remarks>
		/// This handles the processing of shortcut key combinations.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="KeyEventArgs"/> describing the event arguments.</param>
		private void rtbTextbox_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.Control)
				switch (e.KeyCode)
				{
					case Keys.A:
						rtbTextbox.SelectAll();
						e.Handled = true;
						break;
					case Keys.B:
						tsbBold.Checked = !tsbBold.Checked;
						FontStyleChanged(tsbBold, new EventArgs());
						e.Handled = true;
						break;
					case Keys.I:
						tsbItalic.Checked = !tsbItalic.Checked;
						FontStyleChanged(tsbItalic, new EventArgs());
						e.Handled = true;
						e.SuppressKeyPress = true;
						break;
					case Keys.U:
						tsbUnderline.Checked = !tsbUnderline.Checked;
						FontStyleChanged(tsbUnderline, new EventArgs());
						e.Handled = true;
						break;
				}
		}
	}
}
