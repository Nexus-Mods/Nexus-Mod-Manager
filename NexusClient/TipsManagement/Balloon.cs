using System;
using System.Collections;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Nexus.Client;

namespace Balloon.NET
{
	public class BalloonHelp : Balloon.NET.BalloonVM
	{
		private Hooks.WindowsHook windowsHook;
		private System.ComponentModel.IContainer components = null;

		public event EventHandler ClickNext;
		public event EventHandler ClickPrevious;
        public event EventHandler ClickClose;

		#region Constants
		const int CLOSEBUTTON_WIDTH = 20;
		const int CLOSEBUTTON_HEIGHT = 20;
		const int NEXTBUTTON_WIDTH = 20;
		const int NEXTBUTTON_HEIGHT = 20;
		const uint SHOWCLOSEBUTTON = 0x1;
		const uint SHOWNEXTBUTTON = 0x3;
		const uint CLOSEONMOUSECLICK = 0x2;
		const uint CLOSEONKEYPRESS = 0x4;
		const uint CLOSEONMOUSEMOVE = 0x10;
		const uint CLOSEONDEACTIVATE = 0x20;
		const uint ENABLETIMEOUT = 0x40;
		const int WM_ACTIVATEAPP = 0x001C;
		#endregion
		
		#region Fields
		private uint flags = CLOSEONMOUSECLICK | CLOSEONKEYPRESS | CLOSEONDEACTIVATE;
		private string content = String.Empty;
		private Font captionFont;
		private System.Windows.Forms.Button closeButton;
		public System.Windows.Forms.Button nextButton;
		public System.Windows.Forms.Button previousButton;
		private Size headerSize;
		
		#endregion

		public BalloonHelp()
		{
			// This call is required by the Windows Form Designer.
			InitializeComponent();

		}
		
		#region Utility functions
		private void SetBoolProp(uint prop, bool b)
		{
			if(b)
				flags |= prop;
			else
				flags &= ~prop;
		}
		
		private bool GetBoolProp(uint prop)
		{
			return (flags & prop) != 0;
		}
		#endregion
		
		#region Public Properties
						
		[Category("Balloon")]
		public bool ShowCloseButton
		{
			get
			{
				return Controls.Contains(closeButton);
			}
			set
			{
				if (!value)
				{
					if (Controls.Contains(closeButton))
						Controls.Remove(closeButton);
				}
				else
				{
					if (!Controls.Contains(closeButton))
						Controls.Add(closeButton);
				}

				SetBoolProp(SHOWCLOSEBUTTON, value);
			}
		}

		[Category("Balloon")]
		public bool ShowNextButton
		{
			get
			{
				return Controls.Contains(nextButton);
			}
			set
			{
				if (!value)
				{
					if (Controls.Contains(nextButton))
						Controls.Remove(nextButton);
				}
				else
				{
					if (!Controls.Contains(nextButton))
						Controls.Add(nextButton);
			}

			SetBoolProp(SHOWNEXTBUTTON, value);
			}
		}

		[Category("Balloon")]
		public bool EnableTimeout
		{
			get
			{
				return GetBoolProp(ENABLETIMEOUT);
			}
			set
			{
				SetBoolProp(ENABLETIMEOUT, value);
			}
		}

		[Category("Balloon")]
		public bool CloseOnMouseClick
		{
			get
			{
				return GetBoolProp(CLOSEONMOUSECLICK);
			}
			set
			{
				SetBoolProp(CLOSEONMOUSECLICK, value);
			}
		}

		[Category("Balloon")]
		public bool CloseOnKeyPress
		{
			get
			{
				return GetBoolProp(CLOSEONKEYPRESS);
			}
			set
			{
				SetBoolProp(CLOSEONKEYPRESS, value);
			}
		}

		[Category("Balloon")]
		public bool CloseOnMouseMove
		{
			get
			{
				return GetBoolProp(CLOSEONMOUSEMOVE);
			}
			set
			{
				SetBoolProp(CLOSEONMOUSEMOVE, value);
			}
		}
		
		[Category("Balloon")]
		public bool CloseOnDeactivate
		{
			get
			{
				return GetBoolProp(CLOSEONDEACTIVATE);
			}
			set
			{
				SetBoolProp(CLOSEONDEACTIVATE, value);
			}
		}

		[Category("Balloon")]
		public string Content
		{
			get
			{
				return content;
			}
			set
			{
				content = value;
			}
		}

		[Browsable(false)]
		[EditorBrowsable(EditorBrowsableState.Never)]
		public new string Text
		{
			get
			{
				return base.Text;
			}
			set
			{
				base.Text = value;
			}
		}

		[Category("Balloon")]
		public string Caption
		{
			get
			{
				string ret = this.Text;
				return (ret == null) ? String.Empty : ret;
			}
			set
			{
				this.Text = value;
			}
		}
	
		[Category("Balloon")]
		public Font CaptionFont
		{
			get
			{
				if (captionFont == null)
					captionFont = new Font("Arial", 14, FontStyle.Bold);

				return captionFont;
			}
			set
			{
				captionFont = value;
			}
		}
		#endregion

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		protected override void Dispose( bool disposing )
		{
			if( disposing )
			{
				if (components != null) 
				{
					components.Dispose();
				}
			}
			base.Dispose( disposing );
		}

		#region Calculate size
		protected Size CalcClientSize()
		{
			System.Drawing.Size size = Size.Empty;

			using(System.Drawing.Graphics g = this.CreateGraphics())
			{
				if (this.Icon != null)
					size = new Size(16,16);

				if (this.ShowCloseButton)
				{
					if (size.Width != 0)
						size.Width += TIPMARGIN;
					
					size.Width += (CLOSEBUTTON_WIDTH + TIPMARGIN);
					size.Height = Math.Max(size.Height, CLOSEBUTTON_HEIGHT);
				}

				if (this.ShowNextButton)
				{
					if (size.Width != 0)
						size.Width += TIPMARGIN;

					size.Width += (NEXTBUTTON_WIDTH + TIPMARGIN);
					size.Height = Math.Max(size.Height, NEXTBUTTON_HEIGHT);
				}
				
				if (this.Caption.Length != 0)
				{
					if (size.Width != 0)
						size.Width += TIPMARGIN;
					
					System.Drawing.Size captionSize = Size.Ceiling(g.MeasureString(Caption, this.CaptionFont));
					size.Width += captionSize.Width;
					size.Height = Math.Max(captionSize.Height, size.Height);
				}
				
				headerSize = size;

				string text = this.Content;
				
				if ((text != null) && (text.Length != 0))
				{
					size.Height += TIPMARGIN;

					System.Drawing.Size textSize = Size.Ceiling(g.MeasureString(text, this.Font));
					size.Height += textSize.Height;
					size.Width = Math.Max(textSize.Width, headerSize.Width);
					headerSize.Width = size.Width;
				}
			}

			return size;
		}
		#endregion

		#region Client Area Drawing
		
		private void Draw(Graphics g)
		{
			Rectangle drawingRect = this.ClientRectangle;

			string content = this.Content;

			if (content.Length != 0)
			{
				RectangleF textRect = new RectangleF(0, headerSize.Height + TIPMARGIN, drawingRect.Width, drawingRect.Height - headerSize.Height - TIPMARGIN);
				Brush b = new SolidBrush(this.ForeColor);
				g.DrawString(content, this.Font, b, textRect);
				b.Dispose();
			}

			// calc & draw icon
			IntPtr Hicon = Nexus.Client.Properties.Resources.help_book.GetHicon();
			Icon newIcon = Icon.FromHandle(Hicon);

			if(this.Icon != null)
			{
				g.DrawIcon(newIcon, 0, 0);
				drawingRect.X += (newIcon.Width);
				drawingRect.Width -= (newIcon.Width);
			}

			// calc & draw close button / next button
			if(this.ShowCloseButton)
			{
				drawingRect.Width -= (CLOSEBUTTON_WIDTH);
			}

			if (this.ShowNextButton)
			{
				drawingRect.Width -= (NEXTBUTTON_WIDTH);
			}
			
			string caption = this.Caption;

			if (caption.Length != 0)
			{
				drawingRect.X += TIPMARGIN;
				drawingRect.Width -= TIPMARGIN;

				//StringFormat sf = new StringFormat();
				//sf.Alignment = StringAlignment.Center;

				Brush b = new SolidBrush(this.ForeColor);
				
				g.DrawString(caption, this.CaptionFont, b, (RectangleF)drawingRect);//, sf); 

				b.Dispose();
				//sf.Dispose();
			}
		}
		
		protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
		{
			Draw(e.Graphics);
		}

		#endregion

		#region Designer generated code
		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.components = new System.ComponentModel.Container();
			this.windowsHook = new Balloon.NET.Hooks.WindowsHook(this.components);
			this.closeButton = new System.Windows.Forms.Button();
			this.nextButton = new System.Windows.Forms.Button();
			this.previousButton = new System.Windows.Forms.Button();
			this.SuspendLayout();
			// 
			// windowsHook
			// 
			this.windowsHook.ThreadID = 0;
			// 
			// closeButton
			// 
			this.closeButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.closeButton.FlatAppearance.BorderColor = System.Drawing.Color.LightYellow;
			this.closeButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.closeButton.Image = global::Nexus.Client.Properties.Resources.tipsClose;
			this.closeButton.Location = new System.Drawing.Point(198, 0);
			this.closeButton.Name = "closeButton";
			this.closeButton.Size = new System.Drawing.Size(20, 20);
			this.closeButton.TabIndex = 0;
			this.closeButton.Click += new System.EventHandler(this.closeButton_Click);
			// 
			// nextButton
			// 
			this.nextButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.nextButton.FlatAppearance.BorderColor = System.Drawing.Color.LightYellow;
			this.nextButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.nextButton.Image = global::Nexus.Client.Properties.Resources.arrow_state_blue_right;
			this.nextButton.Location = new System.Drawing.Point(182, 2);
			this.nextButton.Name = "nextButton";
			this.nextButton.Size = new System.Drawing.Size(16, 16);
			this.nextButton.TabIndex = 0;
			this.nextButton.Click += new System.EventHandler(this.nextButton_Click);
			// 
			// previousButton
			// 
			this.previousButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.previousButton.FlatAppearance.BorderColor = System.Drawing.Color.LightYellow;
			this.previousButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.previousButton.Image = global::Nexus.Client.Properties.Resources.arrow_state_blue_left;
			this.previousButton.Location = new System.Drawing.Point(164, 2);
			this.previousButton.Name = "previousButton";
			this.previousButton.Size = new System.Drawing.Size(16, 16);
			this.previousButton.TabIndex = 0;
			this.previousButton.Click += new System.EventHandler(this.previousButton_Click);
			// 
			// BalloonHelp
			// 
			this.AutoScaleBaseSize = new System.Drawing.Size(5, 13);
			this.BackColor = System.Drawing.Color.LightYellow;
			this.ClientSize = new System.Drawing.Size(224, 190);
			this.Controls.Add(this.closeButton);
			this.Controls.Add(this.previousButton);
			this.Controls.Add(this.nextButton);
			this.Name = "BalloonHelp";
			this.ResumeLayout(false);

		}
		#endregion
		
		protected override void OnLoad(System.EventArgs e)
		{
			this.ClientSize = CalcClientSize();
					
			base.OnLoad(e);
		}
	
		protected virtual void OnDeactivateApp(System.EventArgs e)
		{
			if (this.CloseOnDeactivate)
				Close();
		}
		
		protected override void WndProc(ref Message m)
		{
			if ((m.Msg == WM_ACTIVATEAPP) && (m.WParam == IntPtr.Zero))
			{
				OnDeactivateApp(EventArgs.Empty);
			}
			else
				base.WndProc(ref m);
		}

		protected override void OnClosed(System.EventArgs e)
		{
			windowsHook.Dispose();
			base.OnClosed(e);
		}

		private void closeButton_Click(object sender, EventArgs e)
		{
            Close();
            ClickClose(this, e);
		}

		private void lostFocus(object sender, EventArgs e)
		{
			Close();
		}

		private void nextButton_Click(object sender, EventArgs e)
		{
			Close();
			ClickNext(this,e);
		}

		private void previousButton_Click(object sender, EventArgs e)
		{
			Close();
			ClickPrevious(this, e);
		}
	}
}
