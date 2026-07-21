namespace Nexus.Client.ModAuthoring.UI.Controls
{
	partial class ModInfoEditor
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
			DoDispose();
			base.Dispose(disposing);
		}

		/// <summary>
		/// Allows extension of the dispose method.
		/// </summary>
		partial void DoDispose();

		#region Component Designer generated code

		/// <summary> 
		/// Required method for Designer support - do not modify 
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.components = new System.ComponentModel.Container();
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ModInfoEditor));
			this.butClearScreenshot = new System.Windows.Forms.Button();
			this.butSetScreenshot = new System.Windows.Forms.Button();
			this.ckbLockScreenshot = new System.Windows.Forms.CheckBox();
			this.imlChecks = new System.Windows.Forms.ImageList(this.components);
			this.label2 = new System.Windows.Forms.Label();
			this.pbxScreenshot = new System.Windows.Forms.PictureBox();
			this.ckbLockDescription = new System.Windows.Forms.CheckBox();
			this.ckbLockWebsite = new System.Windows.Forms.CheckBox();
			this.ckbLockAuthor = new System.Windows.Forms.CheckBox();
			this.ckbLockVersion = new System.Windows.Forms.CheckBox();
			this.ckbLockName = new System.Windows.Forms.CheckBox();
			this.tbxWebsite = new System.Windows.Forms.TextBox();
			this.tbxDescription = new System.Windows.Forms.TextBox();
			this.tbxAuthor = new System.Windows.Forms.TextBox();
			this.tbxVersion = new System.Windows.Forms.TextBox();
			this.tbxName = new System.Windows.Forms.TextBox();
			this.label7 = new System.Windows.Forms.Label();
			this.label6 = new System.Windows.Forms.Label();
			this.label5 = new System.Windows.Forms.Label();
			this.label4 = new System.Windows.Forms.Label();
			this.label3 = new System.Windows.Forms.Label();
			this.erpErrors = new System.Windows.Forms.ErrorProvider(this.components);
			this.ofdScreenshot = new System.Windows.Forms.OpenFileDialog();
			this.ttpToolTips = new System.Windows.Forms.ToolTip(this.components);
			this.splitContainer1 = new System.Windows.Forms.SplitContainer();
			((System.ComponentModel.ISupportInitialize)(this.pbxScreenshot)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).BeginInit();
			this.splitContainer1.Panel1.SuspendLayout();
			this.splitContainer1.Panel2.SuspendLayout();
			this.splitContainer1.SuspendLayout();
			this.SuspendLayout();
			// 
			// butClearScreenshot
			// 
			this.butClearScreenshot.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.butClearScreenshot.AutoSize = true;
			this.butClearScreenshot.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.butClearScreenshot.Image = ((System.Drawing.Image)(resources.GetObject("butClearScreenshot.Image")));
			this.butClearScreenshot.Location = new System.Drawing.Point(287, 8);
			this.butClearScreenshot.Name = "butClearScreenshot";
			this.butClearScreenshot.Size = new System.Drawing.Size(38, 38);
			this.butClearScreenshot.TabIndex = 2;
			this.ttpToolTips.SetToolTip(this.butClearScreenshot, "Clear Screenshot");
			this.butClearScreenshot.UseVisualStyleBackColor = true;
			this.butClearScreenshot.Click += new System.EventHandler(this.butClearScreenshot_Click);
			// 
			// butSetScreenshot
			// 
			this.butSetScreenshot.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.butSetScreenshot.AutoSize = true;
			this.butSetScreenshot.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.butSetScreenshot.Image = global::Nexus.Client.Properties.Resources.folders_open;
			this.butSetScreenshot.Location = new System.Drawing.Point(243, 8);
			this.butSetScreenshot.Name = "butSetScreenshot";
			this.butSetScreenshot.Size = new System.Drawing.Size(38, 38);
			this.butSetScreenshot.TabIndex = 1;
			this.ttpToolTips.SetToolTip(this.butSetScreenshot, "Set Screenshot");
			this.butSetScreenshot.UseVisualStyleBackColor = true;
			this.butSetScreenshot.Click += new System.EventHandler(this.butSetScreenshot_Click);
			// 
			// ckbLockScreenshot
			// 
			this.ckbLockScreenshot.Appearance = System.Windows.Forms.Appearance.Button;
			this.ckbLockScreenshot.AutoSize = true;
			this.ckbLockScreenshot.FlatAppearance.BorderSize = 0;
			this.ckbLockScreenshot.FlatAppearance.CheckedBackColor = System.Drawing.Color.Transparent;
			this.ckbLockScreenshot.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.ckbLockScreenshot.ImageIndex = 1;
			this.ckbLockScreenshot.ImageList = this.imlChecks;
			this.ckbLockScreenshot.Location = new System.Drawing.Point(73, 14);
			this.ckbLockScreenshot.Name = "ckbLockScreenshot";
			this.ckbLockScreenshot.Size = new System.Drawing.Size(22, 22);
			this.ckbLockScreenshot.TabIndex = 0;
			this.ckbLockScreenshot.UseVisualStyleBackColor = true;
			this.ckbLockScreenshot.CheckedChanged += new System.EventHandler(this.LockValue_CheckedChanged);
			// 
			// imlChecks
			// 
			this.imlChecks.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("imlChecks.ImageStream")));
			this.imlChecks.TransparentColor = System.Drawing.Color.Transparent;
			this.imlChecks.Images.SetKeyName(0, "locked");
			this.imlChecks.Images.SetKeyName(1, "unlocked");
			// 
			// label2
			// 
			this.label2.AutoSize = true;
			this.label2.Location = new System.Drawing.Point(3, 21);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(64, 13);
			this.label2.TabIndex = 1;
			this.label2.Text = "Screenshot:";
			// 
			// pbxScreenshot
			// 
			this.pbxScreenshot.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
						| System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.pbxScreenshot.Location = new System.Drawing.Point(3, 52);
			this.pbxScreenshot.Name = "pbxScreenshot";
			this.pbxScreenshot.Size = new System.Drawing.Size(322, 242);
			this.pbxScreenshot.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
			this.pbxScreenshot.TabIndex = 0;
			this.pbxScreenshot.TabStop = false;
			// 
			// ckbLockDescription
			// 
			this.ckbLockDescription.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.ckbLockDescription.Appearance = System.Windows.Forms.Appearance.Button;
			this.ckbLockDescription.AutoSize = true;
			this.ckbLockDescription.FlatAppearance.BorderSize = 0;
			this.ckbLockDescription.FlatAppearance.CheckedBackColor = System.Drawing.Color.Transparent;
			this.ckbLockDescription.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.ckbLockDescription.ImageIndex = 1;
			this.ckbLockDescription.ImageList = this.imlChecks;
			this.ckbLockDescription.Location = new System.Drawing.Point(248, 120);
			this.ckbLockDescription.Name = "ckbLockDescription";
			this.ckbLockDescription.Size = new System.Drawing.Size(22, 22);
			this.ckbLockDescription.TabIndex = 9;
			this.ckbLockDescription.UseVisualStyleBackColor = true;
			this.ckbLockDescription.CheckedChanged += new System.EventHandler(this.LockValue_CheckedChanged);
			// 
			// ckbLockWebsite
			// 
			this.ckbLockWebsite.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.ckbLockWebsite.Appearance = System.Windows.Forms.Appearance.Button;
			this.ckbLockWebsite.AutoSize = true;
			this.ckbLockWebsite.FlatAppearance.BorderSize = 0;
			this.ckbLockWebsite.FlatAppearance.CheckedBackColor = System.Drawing.Color.Transparent;
			this.ckbLockWebsite.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.ckbLockWebsite.ImageIndex = 1;
			this.ckbLockWebsite.ImageList = this.imlChecks;
			this.ckbLockWebsite.Location = new System.Drawing.Point(248, 94);
			this.ckbLockWebsite.Name = "ckbLockWebsite";
			this.ckbLockWebsite.Size = new System.Drawing.Size(22, 22);
			this.ckbLockWebsite.TabIndex = 7;
			this.ckbLockWebsite.UseVisualStyleBackColor = true;
			this.ckbLockWebsite.CheckedChanged += new System.EventHandler(this.LockValue_CheckedChanged);
			// 
			// ckbLockAuthor
			// 
			this.ckbLockAuthor.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.ckbLockAuthor.Appearance = System.Windows.Forms.Appearance.Button;
			this.ckbLockAuthor.AutoSize = true;
			this.ckbLockAuthor.FlatAppearance.BorderSize = 0;
			this.ckbLockAuthor.FlatAppearance.CheckedBackColor = System.Drawing.Color.Transparent;
			this.ckbLockAuthor.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.ckbLockAuthor.ImageIndex = 1;
			this.ckbLockAuthor.ImageList = this.imlChecks;
			this.ckbLockAuthor.Location = new System.Drawing.Point(248, 68);
			this.ckbLockAuthor.Name = "ckbLockAuthor";
			this.ckbLockAuthor.Size = new System.Drawing.Size(22, 22);
			this.ckbLockAuthor.TabIndex = 5;
			this.ckbLockAuthor.UseVisualStyleBackColor = true;
			this.ckbLockAuthor.CheckedChanged += new System.EventHandler(this.LockValue_CheckedChanged);
			// 
			// ckbLockVersion
			// 
			this.ckbLockVersion.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.ckbLockVersion.Appearance = System.Windows.Forms.Appearance.Button;
			this.ckbLockVersion.AutoSize = true;
			this.ckbLockVersion.FlatAppearance.BorderSize = 0;
			this.ckbLockVersion.FlatAppearance.CheckedBackColor = System.Drawing.Color.Transparent;
			this.ckbLockVersion.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.ckbLockVersion.ImageIndex = 1;
			this.ckbLockVersion.ImageList = this.imlChecks;
			this.ckbLockVersion.Location = new System.Drawing.Point(248, 42);
			this.ckbLockVersion.Name = "ckbLockVersion";
			this.ckbLockVersion.Size = new System.Drawing.Size(22, 22);
			this.ckbLockVersion.TabIndex = 3;
			this.ckbLockVersion.UseVisualStyleBackColor = true;
			this.ckbLockVersion.CheckedChanged += new System.EventHandler(this.LockValue_CheckedChanged);
			// 
			// ckbLockName
			// 
			this.ckbLockName.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.ckbLockName.Appearance = System.Windows.Forms.Appearance.Button;
			this.ckbLockName.AutoSize = true;
			this.ckbLockName.FlatAppearance.BorderSize = 0;
			this.ckbLockName.FlatAppearance.CheckedBackColor = System.Drawing.Color.Transparent;
			this.ckbLockName.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.ckbLockName.ImageIndex = 1;
			this.ckbLockName.ImageList = this.imlChecks;
			this.ckbLockName.Location = new System.Drawing.Point(248, 16);
			this.ckbLockName.Name = "ckbLockName";
			this.ckbLockName.Size = new System.Drawing.Size(22, 22);
			this.ckbLockName.TabIndex = 1;
			this.ckbLockName.UseVisualStyleBackColor = true;
			this.ckbLockName.CheckedChanged += new System.EventHandler(this.LockValue_CheckedChanged);
			// 
			// tbxWebsite
			// 
			this.tbxWebsite.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.tbxWebsite.Location = new System.Drawing.Point(58, 96);
			this.tbxWebsite.Name = "tbxWebsite";
			this.tbxWebsite.Size = new System.Drawing.Size(184, 20);
			this.tbxWebsite.TabIndex = 6;
			this.tbxWebsite.Validated += new System.EventHandler(this.Control_Validated);
			// 
			// tbxDescription
			// 
			this.tbxDescription.AcceptsReturn = true;
			this.tbxDescription.AcceptsTab = true;
			this.tbxDescription.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
						| System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.erpErrors.SetIconAlignment(this.tbxDescription, System.Windows.Forms.ErrorIconAlignment.TopRight);
			this.tbxDescription.Location = new System.Drawing.Point(20, 147);
			this.tbxDescription.MaxLength = 2147483647;
			this.tbxDescription.Multiline = true;
			this.tbxDescription.Name = "tbxDescription";
			this.tbxDescription.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
			this.tbxDescription.Size = new System.Drawing.Size(250, 147);
			this.tbxDescription.TabIndex = 8;
			this.tbxDescription.Validated += new System.EventHandler(this.Control_Validated);
			this.tbxDescription.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.tbxDescription_KeyPress);
			// 
			// tbxAuthor
			// 
			this.tbxAuthor.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.tbxAuthor.Location = new System.Drawing.Point(58, 70);
			this.tbxAuthor.Name = "tbxAuthor";
			this.tbxAuthor.Size = new System.Drawing.Size(184, 20);
			this.tbxAuthor.TabIndex = 4;
			this.tbxAuthor.Validated += new System.EventHandler(this.Control_Validated);
			// 
			// tbxVersion
			// 
			this.tbxVersion.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.tbxVersion.Location = new System.Drawing.Point(58, 44);
			this.tbxVersion.Name = "tbxVersion";
			this.tbxVersion.Size = new System.Drawing.Size(184, 20);
			this.tbxVersion.TabIndex = 2;
			this.tbxVersion.Validated += new System.EventHandler(this.Control_Validated);
			// 
			// tbxName
			// 
			this.tbxName.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.tbxName.Location = new System.Drawing.Point(58, 18);
			this.tbxName.Name = "tbxName";
			this.tbxName.Size = new System.Drawing.Size(184, 20);
			this.tbxName.TabIndex = 0;
			this.tbxName.Validated += new System.EventHandler(this.Control_Validated);
			// 
			// label7
			// 
			this.label7.AutoSize = true;
			this.label7.Location = new System.Drawing.Point(3, 99);
			this.label7.Name = "label7";
			this.label7.Size = new System.Drawing.Size(49, 13);
			this.label7.TabIndex = 4;
			this.label7.Text = "Website:";
			// 
			// label6
			// 
			this.label6.AutoSize = true;
			this.label6.Location = new System.Drawing.Point(3, 125);
			this.label6.Name = "label6";
			this.label6.Size = new System.Drawing.Size(63, 13);
			this.label6.TabIndex = 3;
			this.label6.Text = "Description:";
			// 
			// label5
			// 
			this.label5.AutoSize = true;
			this.label5.Location = new System.Drawing.Point(11, 73);
			this.label5.Name = "label5";
			this.label5.Size = new System.Drawing.Size(41, 13);
			this.label5.TabIndex = 2;
			this.label5.Text = "Author:";
			// 
			// label4
			// 
			this.label4.AutoSize = true;
			this.label4.Location = new System.Drawing.Point(7, 47);
			this.label4.Name = "label4";
			this.label4.Size = new System.Drawing.Size(45, 13);
			this.label4.TabIndex = 1;
			this.label4.Text = "Version:";
			// 
			// label3
			// 
			this.label3.AutoSize = true;
			this.label3.Location = new System.Drawing.Point(14, 21);
			this.label3.Name = "label3";
			this.label3.Size = new System.Drawing.Size(38, 13);
			this.label3.TabIndex = 0;
			this.label3.Text = "Name:";
			// 
			// erpErrors
			// 
			this.erpErrors.ContainerControl = this;
			// 
			// ofdScreenshot
			// 
			this.ofdScreenshot.Filter = "Image files|*.png;*.jpg;*.bmp";
			this.ofdScreenshot.RestoreDirectory = true;
			// 
			// splitContainer1
			// 
			this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.splitContainer1.Location = new System.Drawing.Point(0, 0);
			this.splitContainer1.Name = "splitContainer1";
			// 
			// splitContainer1.Panel1
			// 
			this.splitContainer1.Panel1.Controls.Add(this.ckbLockDescription);
			this.splitContainer1.Panel1.Controls.Add(this.tbxDescription);
			this.splitContainer1.Panel1.Controls.Add(this.ckbLockWebsite);
			this.splitContainer1.Panel1.Controls.Add(this.label3);
			this.splitContainer1.Panel1.Controls.Add(this.ckbLockAuthor);
			this.splitContainer1.Panel1.Controls.Add(this.label4);
			this.splitContainer1.Panel1.Controls.Add(this.ckbLockVersion);
			this.splitContainer1.Panel1.Controls.Add(this.label5);
			this.splitContainer1.Panel1.Controls.Add(this.ckbLockName);
			this.splitContainer1.Panel1.Controls.Add(this.label6);
			this.splitContainer1.Panel1.Controls.Add(this.tbxWebsite);
			this.splitContainer1.Panel1.Controls.Add(this.label7);
			this.splitContainer1.Panel1.Controls.Add(this.tbxName);
			this.splitContainer1.Panel1.Controls.Add(this.tbxAuthor);
			this.splitContainer1.Panel1.Controls.Add(this.tbxVersion);
			// 
			// splitContainer1.Panel2
			// 
			this.splitContainer1.Panel2.Controls.Add(this.butClearScreenshot);
			this.splitContainer1.Panel2.Controls.Add(this.pbxScreenshot);
			this.splitContainer1.Panel2.Controls.Add(this.butSetScreenshot);
			this.splitContainer1.Panel2.Controls.Add(this.label2);
			this.splitContainer1.Panel2.Controls.Add(this.ckbLockScreenshot);
			this.splitContainer1.Size = new System.Drawing.Size(625, 297);
			this.splitContainer1.SplitterDistance = 293;
			this.splitContainer1.TabIndex = 7;
			// 
			// ModInfoEditor
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.splitContainer1);
			this.Name = "ModInfoEditor";
			this.Size = new System.Drawing.Size(625, 297);
			((System.ComponentModel.ISupportInitialize)(this.pbxScreenshot)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).EndInit();
			this.splitContainer1.Panel1.ResumeLayout(false);
			this.splitContainer1.Panel1.PerformLayout();
			this.splitContainer1.Panel2.ResumeLayout(false);
			this.splitContainer1.Panel2.PerformLayout();
			this.splitContainer1.ResumeLayout(false);
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.CheckBox ckbLockScreenshot;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.PictureBox pbxScreenshot;
		private System.Windows.Forms.TextBox tbxWebsite;
		private System.Windows.Forms.TextBox tbxDescription;
		private System.Windows.Forms.TextBox tbxAuthor;
		private System.Windows.Forms.TextBox tbxVersion;
		private System.Windows.Forms.Label label7;
		private System.Windows.Forms.Label label6;
		private System.Windows.Forms.Label label5;
		private System.Windows.Forms.Label label4;
		private System.Windows.Forms.Label label3;
		private System.Windows.Forms.ImageList imlChecks;
		private System.Windows.Forms.Button butSetScreenshot;
		private System.Windows.Forms.Button butClearScreenshot;
		private System.Windows.Forms.CheckBox ckbLockDescription;
		private System.Windows.Forms.CheckBox ckbLockWebsite;
		private System.Windows.Forms.CheckBox ckbLockAuthor;
		private System.Windows.Forms.CheckBox ckbLockVersion;
		private System.Windows.Forms.CheckBox ckbLockName;
		private System.Windows.Forms.ErrorProvider erpErrors;
		private System.Windows.Forms.TextBox tbxName;
		private System.Windows.Forms.OpenFileDialog ofdScreenshot;
		private System.Windows.Forms.ToolTip ttpToolTips;
		private System.Windows.Forms.SplitContainer splitContainer1;
	}
}
