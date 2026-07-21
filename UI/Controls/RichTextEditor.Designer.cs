namespace Nexus.UI.Controls
{
	partial class RichTextEditor
	{
		/// <summary> 
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary> 
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		#region Component Designer generated code

		/// <summary> 
		/// Required method for Designer support - do not modify 
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.rtbTextbox = new System.Windows.Forms.RichTextBox();
			this.toolStrip1 = new System.Windows.Forms.ToolStrip();
			this.tscbFont = new System.Windows.Forms.ToolStripComboBox();
			this.tscbFontSize = new System.Windows.Forms.ToolStripComboBox();
			this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
			this.tsbBold = new System.Windows.Forms.ToolStripButton();
			this.tsbItalic = new System.Windows.Forms.ToolStripButton();
			this.tsbUnderline = new System.Windows.Forms.ToolStripButton();
			this.tsbStrikeout = new System.Windows.Forms.ToolStripButton();
			this.tsbJustifyLeft = new System.Windows.Forms.ToolStripButton();
			this.tsbJustifyCentre = new System.Windows.Forms.ToolStripButton();
			this.tsbJustifyRight = new System.Windows.Forms.ToolStripButton();
			this.toolStrip1.SuspendLayout();
			this.SuspendLayout();
			// 
			// rtbTextbox
			// 
			this.rtbTextbox.AcceptsTab = true;
			this.rtbTextbox.Dock = System.Windows.Forms.DockStyle.Fill;
			this.rtbTextbox.Location = new System.Drawing.Point(0, 25);
			this.rtbTextbox.Name = "rtbTextbox";
			this.rtbTextbox.Size = new System.Drawing.Size(694, 351);
			this.rtbTextbox.TabIndex = 0;
			this.rtbTextbox.Text = "";
			this.rtbTextbox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.rtbTextbox_KeyDown);
			this.rtbTextbox.SelectionChanged += new System.EventHandler(this.rtbTextbox_SelectionChanged);
			// 
			// toolStrip1
			// 
			this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tscbFont,
            this.tscbFontSize,
            this.tsbBold,
            this.tsbItalic,
            this.tsbUnderline,
            this.tsbStrikeout,
            this.toolStripSeparator1,
            this.tsbJustifyLeft,
            this.tsbJustifyCentre,
            this.tsbJustifyRight});
			this.toolStrip1.Location = new System.Drawing.Point(0, 0);
			this.toolStrip1.Name = "toolStrip1";
			this.toolStrip1.Size = new System.Drawing.Size(694, 25);
			this.toolStrip1.TabIndex = 1;
			this.toolStrip1.Text = "toolStrip1";
			// 
			// tscbFont
			// 
			this.tscbFont.DropDownWidth = 175;
			this.tscbFont.Name = "tscbFont";
			this.tscbFont.Size = new System.Drawing.Size(150, 25);
			this.tscbFont.SelectedIndexChanged += new System.EventHandler(this.tscbFont_SelectedIndexChanged);
			this.tscbFont.Leave += new System.EventHandler(this.tscbFont_Leave);
			// 
			// tscbFontSize
			// 
			this.tscbFontSize.AutoSize = false;
			this.tscbFontSize.DropDownWidth = 35;
			this.tscbFontSize.Name = "tscbFontSize";
			this.tscbFontSize.Size = new System.Drawing.Size(35, 23);
			this.tscbFontSize.Leave += new System.EventHandler(this.tscbFontSize_Leave);
			this.tscbFontSize.TextChanged += new System.EventHandler(this.tscbFontSize_TextChanged);
			// 
			// toolStripSeparator1
			// 
			this.toolStripSeparator1.Name = "toolStripSeparator1";
			this.toolStripSeparator1.Size = new System.Drawing.Size(6, 25);
			// 
			// tsbBold
			// 
			this.tsbBold.CheckOnClick = true;
			this.tsbBold.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbBold.Image = global::Nexus.UI.Properties.Resources.boldhs;
			this.tsbBold.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbBold.Name = "tsbBold";
			this.tsbBold.Size = new System.Drawing.Size(23, 22);
			this.tsbBold.Text = "Bold";
			this.tsbBold.CheckedChanged += new System.EventHandler(this.FontStyleChanged);
			// 
			// tsbItalic
			// 
			this.tsbItalic.CheckOnClick = true;
			this.tsbItalic.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbItalic.Image = global::Nexus.UI.Properties.Resources.ItalicHS;
			this.tsbItalic.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbItalic.Name = "tsbItalic";
			this.tsbItalic.Size = new System.Drawing.Size(23, 22);
			this.tsbItalic.Text = "Italic";
			this.tsbItalic.CheckedChanged += new System.EventHandler(this.FontStyleChanged);
			// 
			// tsbUnderline
			// 
			this.tsbUnderline.CheckOnClick = true;
			this.tsbUnderline.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbUnderline.Image = global::Nexus.UI.Properties.Resources.underline;
			this.tsbUnderline.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbUnderline.Name = "tsbUnderline";
			this.tsbUnderline.Size = new System.Drawing.Size(23, 22);
			this.tsbUnderline.Text = "Underline";
			this.tsbUnderline.CheckedChanged += new System.EventHandler(this.FontStyleChanged);
			// 
			// tsbStrikeout
			// 
			this.tsbStrikeout.CheckOnClick = true;
			this.tsbStrikeout.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbStrikeout.Image = global::Nexus.UI.Properties.Resources.strikeout;
			this.tsbStrikeout.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbStrikeout.Name = "tsbStrikeout";
			this.tsbStrikeout.Size = new System.Drawing.Size(23, 22);
			this.tsbStrikeout.Text = "Strikeout";
			this.tsbStrikeout.CheckedChanged += new System.EventHandler(this.FontStyleChanged);
			// 
			// tsbJustifyLeft
			// 
			this.tsbJustifyLeft.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbJustifyLeft.Image = global::Nexus.UI.Properties.Resources.justify_left;
			this.tsbJustifyLeft.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbJustifyLeft.Name = "tsbJustifyLeft";
			this.tsbJustifyLeft.Size = new System.Drawing.Size(23, 22);
			this.tsbJustifyLeft.Text = "Left Justify";
			this.tsbJustifyLeft.Click += new System.EventHandler(this.JustifyText);
			// 
			// tsbJustifyCentre
			// 
			this.tsbJustifyCentre.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbJustifyCentre.Image = global::Nexus.UI.Properties.Resources.justify_centre;
			this.tsbJustifyCentre.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbJustifyCentre.Name = "tsbJustifyCentre";
			this.tsbJustifyCentre.Size = new System.Drawing.Size(23, 22);
			this.tsbJustifyCentre.Text = "Centre Justify";
			this.tsbJustifyCentre.Click += new System.EventHandler(this.JustifyText);
			// 
			// tsbJustifyRight
			// 
			this.tsbJustifyRight.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbJustifyRight.Image = global::Nexus.UI.Properties.Resources.justify_right;
			this.tsbJustifyRight.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbJustifyRight.Name = "tsbJustifyRight";
			this.tsbJustifyRight.Size = new System.Drawing.Size(23, 22);
			this.tsbJustifyRight.Text = "Right Justify";
			this.tsbJustifyRight.Click += new System.EventHandler(this.JustifyText);
			// 
			// RichTextEditor
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.rtbTextbox);
			this.Controls.Add(this.toolStrip1);
			this.Name = "RichTextEditor";
			this.Size = new System.Drawing.Size(694, 376);
			this.toolStrip1.ResumeLayout(false);
			this.toolStrip1.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.RichTextBox rtbTextbox;
		private System.Windows.Forms.ToolStrip toolStrip1;
		private System.Windows.Forms.ToolStripComboBox tscbFont;
		private System.Windows.Forms.ToolStripComboBox tscbFontSize;
		private System.Windows.Forms.ToolStripButton tsbBold;
		private System.Windows.Forms.ToolStripButton tsbItalic;
		private System.Windows.Forms.ToolStripButton tsbUnderline;
		private System.Windows.Forms.ToolStripButton tsbStrikeout;
		private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
		private System.Windows.Forms.ToolStripButton tsbJustifyLeft;
		private System.Windows.Forms.ToolStripButton tsbJustifyCentre;
		private System.Windows.Forms.ToolStripButton tsbJustifyRight;
	}
}
