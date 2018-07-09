using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI
{
	/// <summary>
	/// A panel that displays an image and title.
	/// </summary>
	public class HeaderPanel : UserControl
	{
		/// <summary>
		/// A label that has a transparent backgroun.
		/// </summary>
		private class TransparentLabel : UserControl
		{
			private bool m_booPaintOnce = false;

			#region Properties

			/// <summary>
			/// Gets or sets the text of the label.
			/// </summary>
			/// <value>The text of the label.</value>
			[DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
			[Browsable(true)]
			[Category("Appearance")]
			public override string Text
			{
				get
				{
					return base.Text;
				}
				set
				{
					base.Text = value;
					updateLayout();
				}
			}

			/// <summary>
			/// Gets or sets the font of the label.
			/// </summary>
			/// <value>The font of the label.</value>
			[DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
			public override Font Font
			{
				get
				{
					return base.Font;
				}
				set
				{
					base.Font = value;
					updateLayout();
				}
			}

			#endregion

			#region Constructors

			/// <summary>
			/// The default constructor.
			/// </summary>
			public TransparentLabel()
			{
				BackColor = Color.Transparent;
			}

			#endregion

			/// <summary>
			/// Raises the <see cref="Control.OnPaintBackground"/> event.
			/// </summary>
			/// <remarks>
			/// We don't want a backgroun, so this method does nothing.
			/// </remarks>
			/// <param name="e">A <see cref="PaintEventArgs"/> describing the event arguments.</param>
			protected override void OnPaintBackground(PaintEventArgs e) { }

			/// <summary>
			/// Adjusts the size of the label whenever properties affecting size change.
			/// </summary>
			private void updateLayout()
			{
				using (Graphics g = this.CreateGraphics())
					ClientSize = TextRenderer.MeasureText(g, Text, Font, ClientSize, TextFormatFlags.NoClipping);
			}

			/// <summary>
			/// Raises the <see cref="Control.OnPaint"/> event.
			/// </summary>
			/// <remarks>
			/// This paints what is behind the label, and then oerlays that with the label's text.
			/// </remarks>
			/// <param name="e">A <see cref="PaintEventArgs"/> describing the event arguments.</param>
			protected override void OnPaint(PaintEventArgs e)
			{
				if (!m_booPaintOnce)
				{
					m_booPaintOnce = true;
					this.Visible = false;
					this.Parent.Invalidate(this.Bounds);
					this.Parent.Update();
					this.Visible = true;
					return;
				}
				else
				{
					m_booPaintOnce = false;
					using (Graphics g = e.Graphics)
						TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, BackColor);
				}
			}
		}

		private const Int32 GRADIENT_SIZE_MULT = 5;

		private PictureBox m_pbxImage = new PictureBox();
		private PictureBox m_pbxGradient = new PictureBox();
		private TextPosition m_tpsPosition = TextPosition.RightOfImage;
		private TransparentLabel m_tlbLabel = new TransparentLabel();
		private string m_strImageLocation = null;
		private Bitmap m_bmpOriginalImage = null;
		private bool m_booShowFade = true;

		#region Properties

		/// <summary>
		/// Gets or sets whether to show the fade effect.
		/// </summary>
		/// <value>Whether to show the fade effect.</value>
		[Browsable(true)]
		[Category("Appearance")]
		[DefaultValue(true)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
		public bool ShowFade
		{
			get
			{
				return m_booShowFade;
			}
			set
			{
				if (m_booShowFade != value)
				{
					m_booShowFade = value;
					updateLayout();
				}
			}
		}

		/// <summary>
		/// Gets or sets where to position the title text.
		/// </summary>
		/// <value>Where to position the title text.</value>
		[Browsable(true)]
		[Category("Appearance")]
		[DefaultValue(TextPosition.RightOfImage)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
		public TextPosition TextPosition
		{
			get
			{
				return m_tpsPosition;
			}
			set
			{
				if (m_tpsPosition != value)
				{
					m_tpsPosition = value;
					updateLayout();
				}
			}
		}

		/// <summary>
		/// Gets or sets the title text.
		/// </summary>
		/// <value>The title text.</value>
		[Browsable(true)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
		public override string Text
		{
			get
			{
				return m_tlbLabel.Text;
			}
			set
			{
				if (m_tlbLabel.Text != value)
				{
					m_tlbLabel.Text = value;
					updateLayout();
				}
			}
		}

		/// <summary>
		/// Gets or sets the source location of the image to display.
		/// </summary>
		/// <value>The source location of the image to display.</value>
		[Browsable(true)]
		[Category("Appearance")]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
		public string ImageLocation
		{
			get
			{
				return m_strImageLocation;
			}
			set
			{
				if (m_strImageLocation != value)
				{
					m_strImageLocation = value;
					m_bmpOriginalImage = String.IsNullOrEmpty(m_strImageLocation) ? null : new Bitmap(m_strImageLocation);
					m_booShowFade = !String.IsNullOrEmpty(m_strImageLocation);
					updateLayout();
				}
			}
		}

		/// <summary>
		/// Gets or sets the image to display.
		/// </summary>
		/// <value>The image to display.</value>
		[Browsable(true)]
		[Category("Appearance")]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
		public Image Image
		{
			get
			{
				return m_bmpOriginalImage;
			}
			set
			{
				Bitmap bmpValue = null;
				if (value != null)
					bmpValue = new Bitmap(value);
				if (m_bmpOriginalImage != bmpValue)
				{
					m_bmpOriginalImage = bmpValue;
					m_strImageLocation = null;
					m_booShowFade = (m_bmpOriginalImage != null);
					updateLayout();
				}
			}
		}

		/// <summary>
		/// Gets or sets the height of the header panel.
		/// </summary>
		/// <value>The height of the header panel.</value>
		public new Int32 Height
		{
			get
			{
				return base.Height;
			}
			set
			{
				if (base.Height != value)
				{
					base.Height = value;
					updateLayout();
				}
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public HeaderPanel()
		{
			this.DoubleBuffered = true;
			this.SuspendLayout();
			Controls.Add(m_pbxImage);
			Controls.Add(m_pbxGradient);
			Controls.Add(m_tlbLabel);
			m_pbxImage.SizeMode = PictureBoxSizeMode.StretchImage;
			m_pbxGradient.Dock = DockStyle.Fill;
			m_pbxGradient.SizeMode = PictureBoxSizeMode.StretchImage;
			m_booShowFade = !String.IsNullOrEmpty(m_strImageLocation) || (m_bmpOriginalImage != null);
			this.ResumeLayout();
			updateLayout();
		}

		#endregion

		/// <summary>
		/// Positions the controls per the properties.
		/// </summary>
		private void updateLayout()
		{
			this.SuspendLayout();
			m_pbxImage.Dock = (m_tpsPosition == TextPosition.Left) ? DockStyle.Right : DockStyle.Left;
			m_pbxGradient.BringToFront();
			loadImage();
			m_tlbLabel.Top = (this.ClientSize.Height - m_tlbLabel.Height) / 2;
			switch (m_tpsPosition)
			{
				case TextPosition.Left:
					m_tlbLabel.Left = 15;
					m_tlbLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
					break;
				case TextPosition.RightOfImage:
					m_tlbLabel.Left = m_pbxImage.Width + 15;
					m_tlbLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
					break;
				default:
					m_tlbLabel.Left = this.ClientSize.Width - m_tlbLabel.Width - 15;
					m_tlbLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
					break;
			}
			m_tlbLabel.BringToFront();
			this.ResumeLayout();

			Fade();
		}

		/// <summary>
		/// Raises the <see cref="Control.FontChanged"/> event.
		/// </summary>
		/// <remarks>
		/// This method updates the font of the label.
		/// </remarks>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnFontChanged(EventArgs e)
		{
			base.OnFontChanged(e);
			m_tlbLabel.Font = this.Font;
			updateLayout();
		}

		/// <summary>
		/// Raises the <see cref="Control.Reszie"/> event.
		/// </summary>
		/// <remarks>
		/// This forces the label to refresh if it hasn't already done so.
		/// </remarks>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);
			if (((m_tlbLabel.Anchor & AnchorStyles.Left) == AnchorStyles.Left) && m_booShowFade)
				m_tlbLabel.Refresh();
		}

		/// <summary>
		/// Resizes the source image to fit into the header.
		/// </summary>
		/// <remarks>
		/// The piture box automatically sizes the image to fit; however, resizing ourselves gives us
		/// a significant performance advantage when creating the fade effect.
		/// </remarks>
		/// <param name="p_imgSource">The image to resize.</param>
		/// <param name="p_szeSize">The size to which to resize the image.</param>
		/// <returns>The resized image.</returns>
		protected Bitmap resize(Image p_imgSource, Size p_szeSize)
		{
			Bitmap bmpSmallPicture = new Bitmap(p_szeSize.Width, p_szeSize.Height, PixelFormat.Format32bppArgb);
			Graphics grpPhoto = Graphics.FromImage(bmpSmallPicture);
			grpPhoto.DrawImage(p_imgSource, new Rectangle(0, 0, p_szeSize.Width, p_szeSize.Height), new Rectangle(0, 0, p_imgSource.Width, p_imgSource.Height), GraphicsUnit.Pixel);
			return bmpSmallPicture;
		}

		/// <summary>
		/// Loads the image into the header, and adjust the picture box to display the image correctly.
		/// </summary>
		private void loadImage()
		{
			PictureBox pbxImage = m_pbxImage;

			if (m_bmpOriginalImage == null)
			{
				m_bmpOriginalImage = new Bitmap(120, 90);
				using (Graphics g = Graphics.FromImage(m_bmpOriginalImage))
				{
					g.FillRectangle(new SolidBrush(this.BackColor), 0, 0, 120, 90);
				}
			}
			float fltScale = (float)pbxImage.ClientSize.Height / (float)m_bmpOriginalImage.Height;
			pbxImage.ClientSize = new Size((Int32)(Math.Round(fltScale * m_bmpOriginalImage.Width)), pbxImage.ClientSize.Height);
			Bitmap bmpImage = resize(m_bmpOriginalImage, pbxImage.ClientSize);
			pbxImage.Image = bmpImage;
		}

		/// <summary>
		/// Generates the fade effect.
		/// </summary>
		private void Fade()
		{
			if (!m_booShowFade)
			{
				m_pbxGradient.Image = null;
				return;
			}

			PictureBox pbxImage = m_pbxImage;
			PictureBox pbxGradient = m_pbxGradient;
			Bitmap bmpImage = (Bitmap)pbxImage.Image;

			//set background of picture box to average colour of the image
			Int32 intR = 0;
			Int32 intG = 0;
			Int32 intB = 0;
			Int32 intCounter = 0;
			for (Int32 i = 0; i < bmpImage.Width; i += 10)
			{
				for (Int32 j = 0; j < bmpImage.Height; j += 10)
				{
					Color clrPixel = bmpImage.GetPixel(i, j);
					intR += clrPixel.R;
					intG += clrPixel.G;
					intB += clrPixel.B;
					intCounter++;
				}
			}
			intR /= intCounter;
			intG /= intCounter;
			intB /= intCounter;
			pbxImage.BackColor = Color.FromArgb(255, intR, intG, intB);

			//fade out the edge of the image
			Int32 intRange = bmpImage.Width / 4;
			double dblA = 0;
			for (Int32 i = 0; i < bmpImage.Height; i++)
			{
				Color clrPixel = bmpImage.GetPixel(bmpImage.Width - intRange, i);
				dblA = (double)clrPixel.A;
				double dblADelta = dblA / (double)intRange;
				for (Int32 j = intRange; j > 0; j--)
				{
					Int32 intX = (m_tpsPosition == TextPosition.Left) ? j - 1 : bmpImage.Width - j;
					clrPixel = bmpImage.GetPixel(intX, i);
					intR = clrPixel.R;
					intG = clrPixel.G;
					intB = clrPixel.B;
					dblA -= dblADelta;
					Color clrTmp = Color.FromArgb((Int32)dblA, intR, intG, intB);
					bmpImage.SetPixel(intX, i, clrTmp);
				}
			}

			//create a gradient fading out the average image colour
			Color clrBackColour = pbxImage.BackColor;
			intR = clrBackColour.R;
			intG = clrBackColour.G;
			intB = clrBackColour.B;
			Bitmap bmpGradient = new Bitmap(256 * GRADIENT_SIZE_MULT, bmpImage.Height);
			if (m_tpsPosition == TextPosition.Left)
				for (Int32 i = 0; i < bmpImage.Height; i++)
					for (Int32 j = 0; j < 256 * GRADIENT_SIZE_MULT; j += GRADIENT_SIZE_MULT)
						for (Int32 n = 0; n < GRADIENT_SIZE_MULT; n++)
							bmpGradient.SetPixel(j + n, i, Color.FromArgb(j / GRADIENT_SIZE_MULT, intR, intG, intB));
			else
				for (Int32 i = 0; i < bmpImage.Height; i++)
					for (Int32 j = 0; j < 256 * GRADIENT_SIZE_MULT; j += GRADIENT_SIZE_MULT)
						for (Int32 n = 0; n < GRADIENT_SIZE_MULT; n++)
							bmpGradient.SetPixel(j + n, i, Color.FromArgb(255 - j / GRADIENT_SIZE_MULT, intR, intG, intB));
			pbxGradient.Image = bmpGradient;
			m_tlbLabel.Refresh();
		}
	}
}
