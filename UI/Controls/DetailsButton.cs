using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// A button with an arrow that alternates between pointing up and down when clicked.
	/// </summary>
	public class DetailsButton : Button
	{
		private Image[] m_imgImages = new Image[4];
		private string[] m_strText = new string[2];
		private Int32 m_intTextIndex = 0;
		private Int32 m_intTextImageSpacing = 3;

		#region Properties

		/// <summary>
		/// Gets or sets the text to display for the open state.
		/// </summary>
		/// <value>The text to display for the open state.</value>
		[DefaultValue("Show details")]
		[Category("Appearance")]
		public string OpenText
		{
			get
			{
				return m_strText[0];
			}
			set
			{
				m_strText[0] = value;
				Text = m_strText[m_intTextIndex];
			}
		}

		/// <summary>
		/// Gets or sets the text to display for the close state.
		/// </summary>
		/// <value>The text to display for the close state.</value>
		[DefaultValue("Hide details")]
		[Category("Appearance")]
		public string CloseText
		{
			get
			{
				return m_strText[1];
			}
			set
			{
				m_strText[1] = value;
				Text = m_strText[m_intTextIndex];
			}
		}

		/// <summary>
		/// Gets or set the text displayed on the button.
		/// </summary>
		/// <remarks>
		/// Only the <see cref="OpenText"/> or <see cref="CloseText"/> can be
		/// displayed.
		/// </remarks>
		/// <value>The text displayed on the button.</value>
		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public override string Text
		{
			get
			{
				return base.Text;
			}
			set
			{
				if (Array.IndexOf(m_strText, value) < 0)
					base.Text = m_strText[0];
				else
					base.Text = value;
			}
		}

		/// <summary>
		/// Gets or sets the background colour.
		/// </summary>
		/// <value>The background colour.</value>
		public override Color BackColor
		{
			get
			{
				return base.BackColor;
			}
			set
			{
				base.BackColor = value;
				FlatAppearance.MouseDownBackColor = value;
				FlatAppearance.MouseOverBackColor = value;
			}
		}

		/// <summary>
		/// Gets or sets the flat style.
		/// </summary>
		/// <value>The flat style.</value>
		[DefaultValue(typeof(FlatStyle), "Flat")]
		public new FlatStyle FlatStyle
		{
			get
			{
				return base.FlatStyle;
			}
			set
			{
				base.FlatStyle = value;
			}
		}

		/// <summary>
		/// Gets or sets where the image is displayed on the button.
		/// </summary>
		/// <value>Where the image is displayed on the button.</value>
		[DefaultValue(typeof(ContentAlignment), "MiddleLeft")]
		public new ContentAlignment ImageAlign
		{
			get
			{
				return base.ImageAlign;
			}
			set
			{
				base.ImageAlign = value;
			}
		}

		/// <summary>
		/// Gets or sets where the text is displayed on the button relative to the image.
		/// </summary>
		/// <value>Where the text is displayed on the button relative to the image.</value>
		[DefaultValue(typeof(TextImageRelation), "ImageBeforeText")]
		public new TextImageRelation TextImageRelation
		{
			get
			{
				return base.TextImageRelation;
			}
			set
			{
				base.TextImageRelation = value;
				SetImages();
			}
		}

		/// <summary>
		/// Gets or sets the spacing between the image and the text.
		/// </summary>
		/// <value>The spacing between the image and the text.</value>
		[Browsable(true)]
		[Category("Appearance")]
		[DefaultValue(3)]
		public Int32 TextImageSpacing
		{
			get
			{
				return m_intTextImageSpacing;
			}
			set
			{
				m_intTextImageSpacing = value;
				SetImages();
			}
		}

		/// <summary>
		/// Gets or sets the index of the image that is first displayed on the button.
		/// </summary>
		/// <value>The index of the image that is first displayed on the button.</value>
		[DefaultValue(0)]
		public new Int32 ImageIndex
		{
			get
			{
				return base.ImageIndex;
			}
			set
			{
				base.ImageIndex = value;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public DetailsButton()
		{
			m_imgImages[0] = Properties.Resources.open;
			m_imgImages[1] = Properties.Resources.open_hot;
			m_imgImages[2] = Properties.Resources.close;
			m_imgImages[3] = Properties.Resources.close_hot;

			ImageList iltImages = new ImageList();
			iltImages.ColorDepth = ColorDepth.Depth32Bit;
			ImageList = iltImages;

			FlatAppearance.BorderSize = 0;
			FlatAppearance.MouseDownBackColor = System.Drawing.SystemColors.Control;
			FlatAppearance.MouseOverBackColor = System.Drawing.SystemColors.Control;

			FlatStyle = FlatStyle.Flat;
			TextImageSpacing = 3;
			ImageAlign = ContentAlignment.MiddleLeft;
			TextImageRelation = TextImageRelation.ImageBeforeText;

			ImageIndex = 0;

			m_strText[0] = "Show details";
			m_strText[1] = "Hide details";
			Text = m_strText[m_intTextIndex];
			Size = new Size(PreferredSize.Width + 28, PreferredSize.Height);
		}

		#endregion

		#region Image Handling

		/// <summary>
		/// This fills the image list with the padded images.
		/// </summary>
		private void SetImages()
		{
			Int32 intImageIndex = ImageIndex;
			ImageList.Images.Clear();
			Image imgReference = PadImage(m_imgImages[0]);
			ImageList.ImageSize = new Size(imgReference.Width, imgReference.Height);
			for (Int32 i = 0; i < m_imgImages.Length; i++)
				ImageList.Images.Add(PadImage(m_imgImages[i]));
			ImageIndex = intImageIndex;
		}

		/// <summary>
		/// Adds the <see cref="TextImageSpacing"/> to the image.
		/// </summary>
		/// <param name="p_imgImage">The image to pad.</param>
		/// <returns>The given  image with the padding added to the appropriate side, based on
		/// <see cref="TextImageRelation"/>.</returns>
		private Bitmap PadImage(Image p_imgImage)
		{
			Int32 intWidth = 0;
			Int32 intHeight = 0;
			Int32 intXOffset = 0;
			Int32 intYOffset = 0;
			switch (TextImageRelation)
			{
				case TextImageRelation.ImageAboveText:
					intWidth = p_imgImage.Width;
					intHeight = p_imgImage.Height + TextImageSpacing;
					intXOffset = 0;
					intYOffset = 0;
					break;
				case TextImageRelation.ImageBeforeText:
					intWidth = p_imgImage.Width + TextImageSpacing;
					intHeight = p_imgImage.Height;
					intXOffset = 0;
					intYOffset = 0;
					break;
				case TextImageRelation.Overlay:
					intWidth = p_imgImage.Width;
					intHeight = p_imgImage.Height;
					intXOffset = 0;
					intYOffset = 0;
					break;
				case TextImageRelation.TextAboveImage:
					intWidth = p_imgImage.Width;
					intHeight = p_imgImage.Height + TextImageSpacing;
					intXOffset = 0;
					intYOffset = TextImageSpacing;
					break;
				case TextImageRelation.TextBeforeImage:
					intWidth = p_imgImage.Width + TextImageSpacing;
					intHeight = p_imgImage.Height;
					intXOffset = TextImageSpacing;
					intYOffset = 0;
					break;
			}
			Bitmap bmpPadded = new Bitmap(intWidth, intHeight, p_imgImage.PixelFormat);
			using (Graphics g = Graphics.FromImage(bmpPadded))
				g.DrawImageUnscaled(p_imgImage, intXOffset, intYOffset);
			return bmpPadded;
		}

		#endregion

		/// <summary>
		/// Raises the <see cref="Control.MouseEnter"/> event.
		/// </summary>
		/// <remarks>
		/// This switches the image to the hot version of the current image.
		/// </remarks>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnMouseEnter(EventArgs e)
		{
			ImageIndex += 1 - (ImageIndex % 2);
			base.OnMouseEnter(e);
		}

		/// <summary>
		/// Raises the <see cref="Control.MouseLeave"/> event.
		/// </summary>
		/// <remarks>
		/// This switches the image to the normal version of the current image.
		/// </remarks>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnMouseLeave(EventArgs e)
		{
			ImageIndex -= ImageIndex % 2;
			base.OnMouseLeave(e);
		}

		/// <summary>
		/// Raises the <see cref="Control.Click"/> event.
		/// </summary>
		/// <remarks>
		/// This alternates the image between the up arrow and the down arrow.
		/// </remarks>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnClick(EventArgs e)
		{
			ImageIndex = (ImageIndex + 2) % 4;
			m_intTextIndex = (m_intTextIndex + 1) % 2;
			Text = m_strText[m_intTextIndex];
			base.OnClick(e);
		}
	}
}
