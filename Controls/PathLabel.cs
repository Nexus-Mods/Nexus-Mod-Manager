using System.Windows.Forms;
using System.Drawing;
using System;

namespace Nexus.Client.Controls
{
	/// <summary>
	/// A label that runcates the displayed string as if it were a file system path.
	/// </summary>
	public class PathLabel : Label
	{
		/// <summary>
		/// Paints the label's string.
		/// </summary>
		/// <param name="e">A <see cref="PaintEventArgs"/> describing the event arguments.</param>
		protected override void OnPaint(PaintEventArgs e)
		{
			TextFormatFlags tffFormatting = 0;
			switch (TextAlign)
			{
				case ContentAlignment.BottomCenter:
					tffFormatting |= TextFormatFlags.Bottom | TextFormatFlags.HorizontalCenter;
					break;
				case ContentAlignment.BottomLeft:
					tffFormatting |= TextFormatFlags.Bottom | TextFormatFlags.Left;
					break;
				case ContentAlignment.BottomRight:
					tffFormatting |= TextFormatFlags.Bottom | TextFormatFlags.Right;
					break;
				case ContentAlignment.MiddleCenter:
					tffFormatting |= TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter;
					break;
				case ContentAlignment.MiddleLeft:
					tffFormatting |= TextFormatFlags.VerticalCenter | TextFormatFlags.Left;
					break;
				case ContentAlignment.MiddleRight:
					tffFormatting |= TextFormatFlags.VerticalCenter | TextFormatFlags.Right;
					break;
				case ContentAlignment.TopCenter:
					tffFormatting |= TextFormatFlags.Top | TextFormatFlags.HorizontalCenter;
					break;
				case ContentAlignment.TopLeft:
					tffFormatting |= TextFormatFlags.Top | TextFormatFlags.Left;
					break;
				case ContentAlignment.TopRight:
					tffFormatting |= TextFormatFlags.Top | TextFormatFlags.Right;
					break;
			}
			bool booShrinkToFit = true;

			Int32 intMaxLines = 0;
			Int32 intCharsFitted = 0;
			Int32 intLinesFilled = 0;
			SizeF szeArea = new Size(ClientSize.Width-3, ClientSize.Height-3);
			Font fntFont = Font;
			StringFormat sftNoWrap = new StringFormat();
			sftNoWrap.FormatFlags = StringFormatFlags.LineLimit;
			fntFont = new Font(Font.FontFamily, 9.4f, Font.Style, Font.Unit, Font.GdiCharSet, Font.GdiVerticalFont);
			do
			{
				e.Graphics.MeasureString(Text, fntFont, szeArea, sftNoWrap, out intCharsFitted, out intMaxLines);
				if ((intCharsFitted < Text.Length) && booShrinkToFit && (fntFont.SizeInPoints > 7))
					fntFont = new Font(Font.FontFamily, fntFont.SizeInPoints - 0.1f, Font.Style, Font.Unit, Font.GdiCharSet, Font.GdiVerticalFont);
				else
					break;
			} while (true);
			
			intCharsFitted = 0;
			intLinesFilled = 0;
			string strText = Text;
			sftNoWrap.FormatFlags |= StringFormatFlags.NoWrap;
			while ((intCharsFitted < strText.Length) && (intLinesFilled < intMaxLines))
			{
				e.Graphics.MeasureString(strText, fntFont, szeArea, sftNoWrap, out intCharsFitted, out intLinesFilled);
				if ((intCharsFitted < strText.Length) && (intLinesFilled < intMaxLines))
					strText = strText.Insert(intCharsFitted, Environment.NewLine);
			}
			if (AutoEllipsis && (intCharsFitted < strText.Length))
			{
				string[] strTextLines = strText.Split(new string[] { Environment.NewLine }, StringSplitOptions.None);
				string strLastLine = String.Copy(strTextLines[strTextLines.Length - 1]);
				TextRenderer.MeasureText(e.Graphics, strLastLine, fntFont, ClientSize, TextFormatFlags.PathEllipsis | TextFormatFlags.ModifyString);
				Int32 intNullPos = strLastLine.IndexOf('\0');
				strTextLines[strTextLines.Length - 1] = (intNullPos > -1) ? strLastLine.Substring(0, intNullPos) : strLastLine;
				strText = String.Join(Environment.NewLine, strTextLines);

			}
			e.Graphics.DrawString(strText, fntFont, new SolidBrush(ForeColor), ClientRectangle);
			//TextRenderer.DrawText(e.Graphics, strText, fntFont, ClientRectangle, ForeColor, BackColor, tffFormatting);
		}

	}
}
