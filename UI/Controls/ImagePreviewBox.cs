using System;
using System.Windows.Forms;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// Previews an image as a thumbnail.
	/// </summary>
	/// <remarks>
	/// The image can be clicked to view the full-size image.
	/// </remarks>
	public class ImagePreviewBox : PictureBox
	{
		private ImageViewer m_ivwViewer = new ImageViewer();

		/// <summary>
		/// Raises the <see cref="Control.Click"/> event of the previewer.
		/// </summary>
		/// <remarks>
		/// This opens the image viewer with the larger version of the image.
		/// </remarks>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnClick(EventArgs e)
		{
			if (Image != null)
			{
				m_ivwViewer.Image = Image;
				m_ivwViewer.ShowDialog(FindForm());
			}
			base.OnClick(e);
		}
	}
}
