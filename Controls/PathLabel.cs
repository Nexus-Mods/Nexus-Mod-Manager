using System.Windows.Forms;

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
			if (AutoEllipsis)
			{
				TextFormatFlags tffFormatting = TextFormatFlags.Left | TextFormatFlags.PathEllipsis;
				TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, tffFormatting);
			}
			else
				base.OnPaint(e);
		}

	}
}
