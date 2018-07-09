namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	partial class HeaderEditor
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
			this.components = new System.ComponentModel.Container();
			this.label1 = new System.Windows.Forms.Label();
			this.tbxModName = new System.Windows.Forms.TextBox();
			this.label2 = new System.Windows.Forms.Label();
			this.tbxImagePath = new System.Windows.Forms.TextBox();
			this.label4 = new System.Windows.Forms.Label();
			this.butSelectImage = new System.Windows.Forms.Button();
			this.label5 = new System.Windows.Forms.Label();
			this.ckbShowImage = new System.Windows.Forms.CheckBox();
			this.ckbShowFade = new System.Windows.Forms.CheckBox();
			this.label6 = new System.Windows.Forms.Label();
			this.nudHeight = new System.Windows.Forms.NumericUpDown();
			this.cbxAlignment = new System.Windows.Forms.ComboBox();
			this.cpkColour = new Nexus.UI.Controls.ColourPicker();
			this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
			this.pnlTitle = new System.Windows.Forms.Panel();
			this.pnlColour = new System.Windows.Forms.Panel();
			this.pnlAlignment = new System.Windows.Forms.Panel();
			this.pnlImage = new System.Windows.Forms.Panel();
			this.pnlHeight = new System.Windows.Forms.Panel();
			((System.ComponentModel.ISupportInitialize)(this.nudHeight)).BeginInit();
			this.pnlTitle.SuspendLayout();
			this.pnlColour.SuspendLayout();
			this.pnlAlignment.SuspendLayout();
			this.pnlImage.SuspendLayout();
			this.pnlHeight.SuspendLayout();
			this.SuspendLayout();
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(20, 6);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(62, 13);
			this.label1.TabIndex = 0;
			this.label1.Text = "Mod Name:";
			// 
			// tbxModName
			// 
			this.tbxModName.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.tbxModName.Location = new System.Drawing.Point(88, 3);
			this.tbxModName.Name = "tbxModName";
			this.tbxModName.Size = new System.Drawing.Size(349, 20);
			this.tbxModName.TabIndex = 0;
			this.toolTip1.SetToolTip(this.tbxModName, "The name of the mod. This will be displayed in the install wizard.");
			this.tbxModName.Validated += new System.EventHandler(this.Control_Validated);
			this.tbxModName.KeyDown += new System.Windows.Forms.KeyEventHandler(this.tbxModName_KeyDown);
			// 
			// label2
			// 
			this.label2.AutoSize = true;
			this.label2.Location = new System.Drawing.Point(19, 7);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(63, 13);
			this.label2.TabIndex = 2;
			this.label2.Text = "Title Colour:";
			// 
			// tbxImagePath
			// 
			this.tbxImagePath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.tbxImagePath.Location = new System.Drawing.Point(88, 3);
			this.tbxImagePath.Name = "tbxImagePath";
			this.tbxImagePath.Size = new System.Drawing.Size(317, 20);
			this.tbxImagePath.TabIndex = 0;
			this.toolTip1.SetToolTip(this.tbxImagePath, "The mod logo.");
			this.tbxImagePath.Validated += new System.EventHandler(this.Control_Validated);
			// 
			// label4
			// 
			this.label4.AutoSize = true;
			this.label4.Location = new System.Drawing.Point(20, 6);
			this.label4.Name = "label4";
			this.label4.Size = new System.Drawing.Size(62, 13);
			this.label4.TabIndex = 5;
			this.label4.Text = "Title Image:";
			// 
			// butSelectImage
			// 
			this.butSelectImage.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.butSelectImage.AutoSize = true;
			this.butSelectImage.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.butSelectImage.Location = new System.Drawing.Point(411, 1);
			this.butSelectImage.Name = "butSelectImage";
			this.butSelectImage.Size = new System.Drawing.Size(26, 23);
			this.butSelectImage.TabIndex = 1;
			this.butSelectImage.Text = "...";
			this.butSelectImage.UseVisualStyleBackColor = true;
			this.butSelectImage.Click += new System.EventHandler(this.butSelectImage_Click);
			// 
			// label5
			// 
			this.label5.AutoSize = true;
			this.label5.Location = new System.Drawing.Point(3, 6);
			this.label5.Name = "label5";
			this.label5.Size = new System.Drawing.Size(79, 13);
			this.label5.TabIndex = 2;
			this.label5.Text = "Title Alignment:";
			// 
			// ckbShowImage
			// 
			this.ckbShowImage.AutoSize = true;
			this.ckbShowImage.Location = new System.Drawing.Point(88, 29);
			this.ckbShowImage.Name = "ckbShowImage";
			this.ckbShowImage.Size = new System.Drawing.Size(85, 17);
			this.ckbShowImage.TabIndex = 2;
			this.ckbShowImage.Text = "Show Image";
			this.toolTip1.SetToolTip(this.ckbShowImage, "Whether the logo shuld be displayed in the install wizard.");
			this.ckbShowImage.UseVisualStyleBackColor = true;
			this.ckbShowImage.Validated += new System.EventHandler(this.Control_Validated);
			// 
			// ckbShowFade
			// 
			this.ckbShowFade.AutoSize = true;
			this.ckbShowFade.Location = new System.Drawing.Point(88, 52);
			this.ckbShowFade.Name = "ckbShowFade";
			this.ckbShowFade.Size = new System.Drawing.Size(80, 17);
			this.ckbShowFade.TabIndex = 3;
			this.ckbShowFade.Text = "Show Fade";
			this.toolTip1.SetToolTip(this.ckbShowFade, "Whether or not the fade effect should be displayed. This value is ignored if show" +
					"Image is false.");
			this.ckbShowFade.UseVisualStyleBackColor = true;
			// 
			// label6
			// 
			this.label6.AutoSize = true;
			this.label6.Location = new System.Drawing.Point(41, 5);
			this.label6.Name = "label6";
			this.label6.Size = new System.Drawing.Size(41, 13);
			this.label6.TabIndex = 10;
			this.label6.Text = "Height:";
			// 
			// nudHeight
			// 
			this.nudHeight.Location = new System.Drawing.Point(88, 3);
			this.nudHeight.Maximum = new decimal(new int[] {
            1000,
            0,
            0,
            0});
			this.nudHeight.Name = "nudHeight";
			this.nudHeight.Size = new System.Drawing.Size(148, 20);
			this.nudHeight.TabIndex = 0;
			this.toolTip1.SetToolTip(this.nudHeight, "The height to use for the image. Note that there is a minimum height that is enfo" +
					"rced based on the user\'s settings.");
			this.nudHeight.Validated += new System.EventHandler(this.Control_Validated);
			// 
			// cbxAlignment
			// 
			this.cbxAlignment.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.cbxAlignment.FormattingEnabled = true;
			this.cbxAlignment.Location = new System.Drawing.Point(88, 3);
			this.cbxAlignment.Name = "cbxAlignment";
			this.cbxAlignment.Size = new System.Drawing.Size(148, 21);
			this.cbxAlignment.TabIndex = 0;
			this.toolTip1.SetToolTip(this.cbxAlignment, "The position of the title in the install wizard.");
			this.cbxAlignment.Validated += new System.EventHandler(this.Control_Validated);
			// 
			// cpkColour
			// 
			this.cpkColour.AutoSize = true;
			this.cpkColour.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.cpkColour.Colour = System.Drawing.SystemColors.Control;
			this.cpkColour.Location = new System.Drawing.Point(88, 0);
			this.cpkColour.Margin = new System.Windows.Forms.Padding(0);
			this.cpkColour.Name = "cpkColour";
			this.cpkColour.Size = new System.Drawing.Size(81, 26);
			this.cpkColour.TabIndex = 0;
			this.toolTip1.SetToolTip(this.cpkColour, "The colour to use for the title.");
			this.cpkColour.Validated += new System.EventHandler(this.Control_Validated);
			// 
			// toolTip1
			// 
			this.toolTip1.IsBalloon = true;
			this.toolTip1.ToolTipIcon = System.Windows.Forms.ToolTipIcon.Info;
			// 
			// pnlTitle
			// 
			this.pnlTitle.AutoSize = true;
			this.pnlTitle.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.pnlTitle.Controls.Add(this.label1);
			this.pnlTitle.Controls.Add(this.tbxModName);
			this.pnlTitle.Dock = System.Windows.Forms.DockStyle.Top;
			this.pnlTitle.Location = new System.Drawing.Point(9, 9);
			this.pnlTitle.Name = "pnlTitle";
			this.pnlTitle.Size = new System.Drawing.Size(440, 26);
			this.pnlTitle.TabIndex = 0;
			// 
			// pnlColour
			// 
			this.pnlColour.AutoSize = true;
			this.pnlColour.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.pnlColour.Controls.Add(this.label2);
			this.pnlColour.Controls.Add(this.cpkColour);
			this.pnlColour.Dock = System.Windows.Forms.DockStyle.Top;
			this.pnlColour.Location = new System.Drawing.Point(9, 35);
			this.pnlColour.Name = "pnlColour";
			this.pnlColour.Size = new System.Drawing.Size(440, 26);
			this.pnlColour.TabIndex = 1;
			// 
			// pnlAlignment
			// 
			this.pnlAlignment.AutoSize = true;
			this.pnlAlignment.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.pnlAlignment.Controls.Add(this.label5);
			this.pnlAlignment.Controls.Add(this.cbxAlignment);
			this.pnlAlignment.Dock = System.Windows.Forms.DockStyle.Top;
			this.pnlAlignment.Location = new System.Drawing.Point(9, 61);
			this.pnlAlignment.Name = "pnlAlignment";
			this.pnlAlignment.Size = new System.Drawing.Size(440, 27);
			this.pnlAlignment.TabIndex = 14;
			// 
			// pnlImage
			// 
			this.pnlImage.AutoSize = true;
			this.pnlImage.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.pnlImage.Controls.Add(this.label4);
			this.pnlImage.Controls.Add(this.butSelectImage);
			this.pnlImage.Controls.Add(this.tbxImagePath);
			this.pnlImage.Controls.Add(this.ckbShowImage);
			this.pnlImage.Controls.Add(this.ckbShowFade);
			this.pnlImage.Dock = System.Windows.Forms.DockStyle.Top;
			this.pnlImage.Location = new System.Drawing.Point(9, 88);
			this.pnlImage.Name = "pnlImage";
			this.pnlImage.Size = new System.Drawing.Size(440, 72);
			this.pnlImage.TabIndex = 3;
			// 
			// pnlHeight
			// 
			this.pnlHeight.AutoSize = true;
			this.pnlHeight.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.pnlHeight.Controls.Add(this.label6);
			this.pnlHeight.Controls.Add(this.nudHeight);
			this.pnlHeight.Dock = System.Windows.Forms.DockStyle.Top;
			this.pnlHeight.Location = new System.Drawing.Point(9, 160);
			this.pnlHeight.Name = "pnlHeight";
			this.pnlHeight.Size = new System.Drawing.Size(440, 26);
			this.pnlHeight.TabIndex = 4;
			// 
			// HeaderEditor
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.pnlHeight);
			this.Controls.Add(this.pnlImage);
			this.Controls.Add(this.pnlAlignment);
			this.Controls.Add(this.pnlColour);
			this.Controls.Add(this.pnlTitle);
			this.Name = "HeaderEditor";
			this.Padding = new System.Windows.Forms.Padding(9);
			this.Size = new System.Drawing.Size(458, 379);
			((System.ComponentModel.ISupportInitialize)(this.nudHeight)).EndInit();
			this.pnlTitle.ResumeLayout(false);
			this.pnlTitle.PerformLayout();
			this.pnlColour.ResumeLayout(false);
			this.pnlColour.PerformLayout();
			this.pnlAlignment.ResumeLayout(false);
			this.pnlAlignment.PerformLayout();
			this.pnlImage.ResumeLayout(false);
			this.pnlImage.PerformLayout();
			this.pnlHeight.ResumeLayout(false);
			this.pnlHeight.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.TextBox tbxModName;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.TextBox tbxImagePath;
		private System.Windows.Forms.Label label4;
		private System.Windows.Forms.Button butSelectImage;
		private System.Windows.Forms.Label label5;
		private System.Windows.Forms.CheckBox ckbShowImage;
		private System.Windows.Forms.CheckBox ckbShowFade;
		private System.Windows.Forms.Label label6;
		private System.Windows.Forms.NumericUpDown nudHeight;
		private System.Windows.Forms.ComboBox cbxAlignment;
		private Nexus.UI.Controls.ColourPicker cpkColour;
		private System.Windows.Forms.ToolTip toolTip1;
		private System.Windows.Forms.Panel pnlTitle;
		private System.Windows.Forms.Panel pnlColour;
		private System.Windows.Forms.Panel pnlAlignment;
		private System.Windows.Forms.Panel pnlImage;
		private System.Windows.Forms.Panel pnlHeight;
	}
}
