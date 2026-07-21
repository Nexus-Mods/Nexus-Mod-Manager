using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// A label that runcates the displayed string as if it were a file system path.
	/// </summary>
	public class PathLabel : Label
	{
		#region Properties

		/// <summary>
		/// Gets or sets whether the label's font size will shrnik to fit the entire
		/// text into the label's area.
		/// </summary>
		/// <value>Whether the label's font size will shrnik to fit the entire
		/// text into the label's area.</value>
		[Category("Behavior")]
		[Browsable(true)]
		[DefaultValue(true)]
		public bool ShrinkFontToFit { get; set; }

		/// <summary>
		/// Gets or sets the minimum allowed font size when shrinking the font to fit the label's text.
		/// </summary>
		/// <value>The minimum allowed font size when shrinking the font to fit the label's text.</value>
		[Category("Behavior")]
		[Browsable(true)]
		[DefaultValue(8.0f)]
		public float MinimumFontSize { get; set; }

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public PathLabel()
		{
			ShrinkFontToFit = true;
			MinimumFontSize = 8.0f;
		}

		#endregion

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
			
			Font fntFont = Font;
			bool booAllFit = false;
			string strText = null;
			Int32 intLastHighGuess = Convert.ToInt32(Math.Floor(Font.SizeInPoints * 10f)) + 1;
			Int32 intLastLowGuess = Convert.ToInt32(Math.Floor(MinimumFontSize * 10f));
			Int32 intGuess = (intLastHighGuess + intLastLowGuess) / 2;
			while (intGuess > 0)
			{
				fntFont = new Font(Font.FontFamily, (float)intGuess / 10f, Font.Style, Font.Unit, Font.GdiCharSet, Font.GdiVerticalFont);
				Int32 intMaxLines = ClientSize.Height / TextRenderer.MeasureText(e.Graphics, " ", fntFont).Height;
				strText = SplitText(e.Graphics, Text, intMaxLines, fntFont, ClientSize, out booAllFit);
				if (!ShrinkFontToFit)
					break;
				Int32 intNewGuess = 0;
				if (booAllFit)
				{
					intNewGuess = (intLastHighGuess + intGuess) / 2;
					intLastLowGuess = intGuess;
				}
				else
				{
					intNewGuess = (intGuess + intLastLowGuess) / 2;
					intLastHighGuess = intGuess;
				}
				if (intNewGuess == intGuess)
					break;
				intGuess = intNewGuess;
			}

			if (AutoEllipsis && !booAllFit)
			{
				string[] strTextLines = strText.Split(new string[] { Environment.NewLine }, StringSplitOptions.None);
				string strLastLine = String.Copy(strTextLines[strTextLines.Length - 1]);
				TextRenderer.MeasureText(e.Graphics, strLastLine, fntFont, ClientSize, TextFormatFlags.PathEllipsis | TextFormatFlags.ModifyString);
				Int32 intNullPos = strLastLine.IndexOf('\0');
				strTextLines[strTextLines.Length - 1] = (intNullPos > -1) ? strLastLine.Substring(0, intNullPos) : strLastLine;
				strText = String.Join(Environment.NewLine, strTextLines);
			}
			TextRenderer.DrawText(e.Graphics, strText, fntFont, ClientRectangle, ForeColor, BackColor, tffFormatting);
		}

		/// <summary>
		/// Splits the given text into multiples lines, factoring in the given information.
		/// </summary>
		/// <remarks>
		/// If the given text will not fit in the given area, the last line of the split text
		/// will contain the extra characters.
		/// </remarks>
		/// <param name="p_grpGraphics">The graphics object that will be used to render the text.</param>
		/// <param name="p_strText">The text to split.</param>
		/// <param name="p_intMaxLines">The maximum number of lines into which to split the text.</param>
		/// <param name="p_fntFont">The font in which the text will be rendered.</param>
		/// <param name="p_szeArea">The area in which the text will be rendered.</param>
		/// <param name="p_booAllTextFit">An out parameter indicating whether all of the given text fit in the given area.</param>
		/// <returns>The given text into multiples lines, factoring in the given information.</returns>
		private string SplitText(Graphics p_grpGraphics, string p_strText, Int32 p_intMaxLines, Font p_fntFont, Size p_szeArea, out bool p_booAllTextFit)
		{
			string strText = p_strText;
			Int32 intLineStart = 0;
			Int32 intLinesFilled = 1;
			Int32 intBreakPosition = 0;
			while (intBreakPosition > -1)
			{
				string strLine = String.Copy(strText.Substring(intLineStart));
				TextRenderer.MeasureText(p_grpGraphics, strLine, p_fntFont, p_szeArea, TextFormatFlags.ModifyString | TextFormatFlags.EndEllipsis);
				intBreakPosition = strLine.IndexOf("...\0");
				if (intLinesFilled == p_intMaxLines)
					break;
				if (intBreakPosition > -1)
				{
					intLineStart = intLineStart + intBreakPosition;
					strText = strText.Insert(intLineStart, Environment.NewLine);
					intLineStart = intLineStart + Environment.NewLine.Length;
					intLinesFilled++;
				}
			}
			p_booAllTextFit = (intLinesFilled <= p_intMaxLines) && (intBreakPosition < 0);
			return strText;
		}

	}
}
