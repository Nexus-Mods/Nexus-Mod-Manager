using System;
using System.Windows.Forms;
using System.Drawing;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// Displays an image.
	/// </summary>
	public partial class ImageViewer : Form
	{
		#region Properties

		/// <summary>
		/// Sets the image being viewed.
		/// </summary>
		/// <value>The image being viewed.</value>
		public Image Image
		{
			set
			{
				pbxImage.Image = value;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public ImageViewer()
		{
			InitializeComponent();
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the zoom buttons.
		/// </summary>
		/// <remarks>
		/// This changes the size mode of the picture box to match the selected zoom mode.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void Zoom_Click(object sender, EventArgs e)
		{
			tsbZoomOriginal.Checked = !tsbZoomOriginal.Checked;
			tsbZoomFit.Checked = !tsbZoomOriginal.Checked;
			pbxImage.SizeMode = tsbZoomFit.Checked ? PictureBoxSizeMode.Zoom : PictureBoxSizeMode.AutoSize;
			pbxImage.Dock = tsbZoomFit.Checked ? DockStyle.Fill : DockStyle.None;
		}
		
		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the picture box.
		/// </summary>
		/// <remarks>
		/// This passes focus to the scroll panel so that it can recieve mouse wheel
		/// events (and other events that indicated a scrolling action).
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void pbxImage_Click(object sender, EventArgs e)
		{
			panel1.Focus();
		}
	}
}
