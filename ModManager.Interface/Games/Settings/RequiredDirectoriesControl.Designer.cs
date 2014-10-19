namespace Nexus.Client.Games.Settings
{
	partial class RequiredDirectoriesControl
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
			this.butSelectInfoDirectory = new System.Windows.Forms.Button();
			this.lblInstallInfoPrompt = new System.Windows.Forms.Label();
			this.tbxInstallInfo = new System.Windows.Forms.TextBox();
			this.lblInstallInfoLabel = new System.Windows.Forms.Label();
			this.butSelectModDirectory = new System.Windows.Forms.Button();
			this.lblModPrompt = new System.Windows.Forms.Label();
			this.tbxModDirectory = new System.Windows.Forms.TextBox();
			this.lblModDirectoryLabel = new System.Windows.Forms.Label();
			this.butSelectToolDirectory = new System.Windows.Forms.Button();
			this.lblToolPrompt = new System.Windows.Forms.Label();
			this.tbxToolDirectory = new System.Windows.Forms.TextBox();
			this.lblToolDirectoryLabel = new System.Windows.Forms.Label();
			this.butSelectVirtualDirectory = new System.Windows.Forms.Button();
			this.lblVirtualPrompt = new System.Windows.Forms.Label();
			this.tbxVirtualDirectory = new System.Windows.Forms.TextBox();
			this.lblVirtualDirectoryLabel = new System.Windows.Forms.Label();
			this.butSelectLinkDirectory = new System.Windows.Forms.Button();
			this.lblLinkPrompt = new System.Windows.Forms.Label();
			this.tbxLinkDirectory = new System.Windows.Forms.TextBox();
			this.lblLinkDirectoryLabel = new System.Windows.Forms.Label();
			this.ckbUseMultiHDInstall = new System.Windows.Forms.CheckBox();
			this.erpErrors = new System.Windows.Forms.ErrorProvider(this.components);
			this.fbdDirectory = new System.Windows.Forms.FolderBrowserDialog();
			this.lbWarning = new System.Windows.Forms.Label();
			this.grbMulti = new System.Windows.Forms.GroupBox();
			this.grbInfo = new System.Windows.Forms.GroupBox();
			this.lbInfo = new System.Windows.Forms.Label();
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).BeginInit();
			this.grbMulti.SuspendLayout();
			this.grbInfo.SuspendLayout();
			this.SuspendLayout();
			// 
			// butSelectInfoDirectory
			// 
			this.butSelectInfoDirectory.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.butSelectInfoDirectory.AutoSize = true;
			this.butSelectInfoDirectory.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.butSelectInfoDirectory.Location = new System.Drawing.Point(751, 57);
			this.butSelectInfoDirectory.Name = "butSelectInfoDirectory";
			this.butSelectInfoDirectory.Size = new System.Drawing.Size(26, 23);
			this.butSelectInfoDirectory.TabIndex = 12;
			this.butSelectInfoDirectory.Text = "...";
			this.butSelectInfoDirectory.UseVisualStyleBackColor = true;
			this.butSelectInfoDirectory.Click += new System.EventHandler(this.butSelectInfoDirectory_Click);
			// 
			// lblInstallInfoPrompt
			// 
			this.lblInstallInfoPrompt.AutoSize = true;
			this.lblInstallInfoPrompt.Location = new System.Drawing.Point(3, 43);
			this.lblInstallInfoPrompt.Name = "lblInstallInfoPrompt";
			this.lblInstallInfoPrompt.Size = new System.Drawing.Size(336, 13);
			this.lblInstallInfoPrompt.TabIndex = 14;
			this.lblInstallInfoPrompt.Text = "Choose the directory where you would like to store your {0} install info.";
			// 
			// tbxInstallInfo
			// 
			this.tbxInstallInfo.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.tbxInstallInfo.Location = new System.Drawing.Point(102, 59);
			this.tbxInstallInfo.Name = "tbxInstallInfo";
			this.tbxInstallInfo.Size = new System.Drawing.Size(643, 20);
			this.tbxInstallInfo.TabIndex = 10;
			// 
			// lblInstallInfoLabel
			// 
			this.lblInstallInfoLabel.AutoSize = true;
			this.lblInstallInfoLabel.Location = new System.Drawing.Point(38, 62);
			this.lblInstallInfoLabel.Name = "lblInstallInfoLabel";
			this.lblInstallInfoLabel.Size = new System.Drawing.Size(58, 13);
			this.lblInstallInfoLabel.TabIndex = 13;
			this.lblInstallInfoLabel.Text = "Install Info:";
			// 
			// butSelectModDirectory
			// 
			this.butSelectModDirectory.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.butSelectModDirectory.AutoSize = true;
			this.butSelectModDirectory.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.butSelectModDirectory.Location = new System.Drawing.Point(751, 14);
			this.butSelectModDirectory.Name = "butSelectModDirectory";
			this.butSelectModDirectory.Size = new System.Drawing.Size(26, 23);
			this.butSelectModDirectory.TabIndex = 9;
			this.butSelectModDirectory.Text = "...";
			this.butSelectModDirectory.UseVisualStyleBackColor = true;
			this.butSelectModDirectory.Click += new System.EventHandler(this.butSelectModDirectory_Click);
			// 
			// lblModPrompt
			// 
			this.lblModPrompt.AutoSize = true;
			this.lblModPrompt.Location = new System.Drawing.Point(3, 0);
			this.lblModPrompt.Name = "lblModPrompt";
			this.lblModPrompt.Size = new System.Drawing.Size(316, 13);
			this.lblModPrompt.TabIndex = 11;
			this.lblModPrompt.Text = "Choose the directory where you would like to store your {0} Mods.";
			// 
			// tbxModDirectory
			// 
			this.tbxModDirectory.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.tbxModDirectory.Location = new System.Drawing.Point(102, 16);
			this.tbxModDirectory.Name = "tbxModDirectory";
			this.tbxModDirectory.Size = new System.Drawing.Size(643, 20);
			this.tbxModDirectory.TabIndex = 7;
			// 
			// lblModDirectoryLabel
			// 
			this.lblModDirectoryLabel.AutoSize = true;
			this.lblModDirectoryLabel.Location = new System.Drawing.Point(20, 19);
			this.lblModDirectoryLabel.Name = "lblModDirectoryLabel";
			this.lblModDirectoryLabel.Size = new System.Drawing.Size(76, 13);
			this.lblModDirectoryLabel.TabIndex = 8;
			this.lblModDirectoryLabel.Text = "Mod Directory:";
			// 
			// butSelectToolDirectory
			// 
			this.butSelectToolDirectory.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.butSelectToolDirectory.AutoSize = true;
			this.butSelectToolDirectory.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.butSelectToolDirectory.Location = new System.Drawing.Point(751, 104);
			this.butSelectToolDirectory.Name = "butSelectToolDirectory";
			this.butSelectToolDirectory.Size = new System.Drawing.Size(26, 23);
			this.butSelectToolDirectory.TabIndex = 17;
			this.butSelectToolDirectory.Text = "...";
			this.butSelectToolDirectory.UseVisualStyleBackColor = true;
			this.butSelectToolDirectory.Click += new System.EventHandler(this.butSelectToolDirectory_Click);
			// 
			// lblToolPrompt
			// 
			this.lblToolPrompt.AutoSize = true;
			this.lblToolPrompt.Location = new System.Drawing.Point(3, 90);
			this.lblToolPrompt.Name = "lblToolPrompt";
			this.lblToolPrompt.Size = new System.Drawing.Size(309, 13);
			this.lblToolPrompt.TabIndex = 18;
			this.lblToolPrompt.Text = "(Optional) Select the path where the required {0} tool is installed.";
			// 
			// tbxToolDirectory
			// 
			this.tbxToolDirectory.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.tbxToolDirectory.Location = new System.Drawing.Point(102, 106);
			this.tbxToolDirectory.Name = "tbxToolDirectory";
			this.tbxToolDirectory.Size = new System.Drawing.Size(643, 20);
			this.tbxToolDirectory.TabIndex = 15;
			// 
			// lblToolDirectoryLabel
			// 
			this.lblToolDirectoryLabel.AutoSize = true;
			this.lblToolDirectoryLabel.Location = new System.Drawing.Point(20, 109);
			this.lblToolDirectoryLabel.Name = "lblToolDirectoryLabel";
			this.lblToolDirectoryLabel.Size = new System.Drawing.Size(77, 13);
			this.lblToolDirectoryLabel.TabIndex = 16;
			this.lblToolDirectoryLabel.Text = "Required Tool:";
			// 
			// butSelectVirtualDirectory
			// 
			this.butSelectVirtualDirectory.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.butSelectVirtualDirectory.AutoSize = true;
			this.butSelectVirtualDirectory.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.butSelectVirtualDirectory.Location = new System.Drawing.Point(435, 177);
			this.butSelectVirtualDirectory.Name = "butSelectVirtualDirectory";
			this.butSelectVirtualDirectory.Size = new System.Drawing.Size(26, 23);
			this.butSelectVirtualDirectory.TabIndex = 17;
			this.butSelectVirtualDirectory.Text = "...";
			this.butSelectVirtualDirectory.UseVisualStyleBackColor = true;
			this.butSelectVirtualDirectory.Click += new System.EventHandler(this.butSelectVirtualDirectory_Click);
			// 
			// lblVirtualPrompt
			// 
			this.lblVirtualPrompt.AutoEllipsis = true;
			this.lblVirtualPrompt.AutoSize = true;
			this.lblVirtualPrompt.Location = new System.Drawing.Point(3, 148);
			this.lblVirtualPrompt.Name = "lblVirtualPrompt";
			this.lblVirtualPrompt.Size = new System.Drawing.Size(273, 13);
			this.lblVirtualPrompt.TabIndex = 18;
			this.lblVirtualPrompt.Text = "Select the path where you want NMM to install the mods";
			// 
			// tbxVirtualDirectory
			// 
			this.tbxVirtualDirectory.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.tbxVirtualDirectory.Location = new System.Drawing.Point(102, 179);
			this.tbxVirtualDirectory.Name = "tbxVirtualDirectory";
			this.tbxVirtualDirectory.Size = new System.Drawing.Size(327, 20);
			this.tbxVirtualDirectory.TabIndex = 15;
			// 
			// lblVirtualDirectoryLabel
			// 
			this.lblVirtualDirectoryLabel.AutoSize = true;
			this.lblVirtualDirectoryLabel.Location = new System.Drawing.Point(27, 182);
			this.lblVirtualDirectoryLabel.Name = "lblVirtualDirectoryLabel";
			this.lblVirtualDirectoryLabel.Size = new System.Drawing.Size(69, 13);
			this.lblVirtualDirectoryLabel.TabIndex = 16;
			this.lblVirtualDirectoryLabel.Text = "Virtual Install:";
			// 
			// butSelectLinkDirectory
			// 
			this.butSelectLinkDirectory.Anchor = System.Windows.Forms.AnchorStyles.Left;
			this.butSelectLinkDirectory.AutoSize = true;
			this.butSelectLinkDirectory.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.butSelectLinkDirectory.Location = new System.Drawing.Point(429, 80);
			this.butSelectLinkDirectory.Name = "butSelectLinkDirectory";
			this.butSelectLinkDirectory.Size = new System.Drawing.Size(26, 23);
			this.butSelectLinkDirectory.TabIndex = 17;
			this.butSelectLinkDirectory.Text = "...";
			this.butSelectLinkDirectory.UseVisualStyleBackColor = true;
			this.butSelectLinkDirectory.Click += new System.EventHandler(this.butSelectLinkDirectory_Click);
			// 
			// lblLinkPrompt
			// 
			this.lblLinkPrompt.AutoEllipsis = true;
			this.lblLinkPrompt.AutoSize = true;
			this.lblLinkPrompt.Location = new System.Drawing.Point(6, 46);
			this.lblLinkPrompt.Name = "lblLinkPrompt";
			this.lblLinkPrompt.Size = new System.Drawing.Size(273, 13);
			this.lblLinkPrompt.TabIndex = 18;
			this.lblLinkPrompt.Text = "Select the path where you want NMM to install the mods";
			// 
			// tbxLinkDirectory
			// 
			this.tbxLinkDirectory.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.tbxLinkDirectory.Location = new System.Drawing.Point(96, 83);
			this.tbxLinkDirectory.Name = "tbxLinkDirectory";
			this.tbxLinkDirectory.Size = new System.Drawing.Size(327, 20);
			this.tbxLinkDirectory.TabIndex = 15;
			// 
			// lblLinkDirectoryLabel
			// 
			this.lblLinkDirectoryLabel.AutoSize = true;
			this.lblLinkDirectoryLabel.Location = new System.Drawing.Point(15, 86);
			this.lblLinkDirectoryLabel.Name = "lblLinkDirectoryLabel";
			this.lblLinkDirectoryLabel.Size = new System.Drawing.Size(76, 13);
			this.lblLinkDirectoryLabel.TabIndex = 16;
			this.lblLinkDirectoryLabel.Text = "Required Link:";
			// 
			// ckbUseMultiHDInstall
			// 
			this.ckbUseMultiHDInstall.AutoSize = true;
			this.ckbUseMultiHDInstall.Location = new System.Drawing.Point(26, 23);
			this.ckbUseMultiHDInstall.Name = "ckbUseMultiHDInstall";
			this.ckbUseMultiHDInstall.Size = new System.Drawing.Size(242, 17);
			this.ckbUseMultiHDInstall.TabIndex = 18;
			this.ckbUseMultiHDInstall.Text = "Check this to enable the multi HD install mode";
			this.ckbUseMultiHDInstall.CheckedChanged += new System.EventHandler(this.ckbUseMultiHDInstall_CheckedChanged);
			// 
			// erpErrors
			// 
			this.erpErrors.ContainerControl = this;
			// 
			// lbWarning
			// 
			this.lbWarning.AutoSize = true;
			this.lbWarning.ForeColor = System.Drawing.Color.Red;
			this.lbWarning.Location = new System.Drawing.Point(12, 316);
			this.lbWarning.MaximumSize = new System.Drawing.Size(400, 0);
			this.lbWarning.Name = "lbWarning";
			this.lbWarning.Size = new System.Drawing.Size(50, 13);
			this.lbWarning.TabIndex = 20;
			this.lbWarning.Text = "Warning:";
			this.lbWarning.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
			this.lbWarning.Visible = false;
			// 
			// grbMulti
			// 
			this.grbMulti.Controls.Add(this.ckbUseMultiHDInstall);
			this.grbMulti.Controls.Add(this.lblLinkDirectoryLabel);
			this.grbMulti.Controls.Add(this.tbxLinkDirectory);
			this.grbMulti.Controls.Add(this.lblLinkPrompt);
			this.grbMulti.Controls.Add(this.butSelectLinkDirectory);
			this.grbMulti.Location = new System.Drawing.Point(6, 202);
			this.grbMulti.Name = "grbMulti";
			this.grbMulti.Size = new System.Drawing.Size(461, 111);
			this.grbMulti.TabIndex = 21;
			this.grbMulti.TabStop = false;
			this.grbMulti.Text = "You MUST run the program as Administrator to enable this:";
			// 
			// grbInfo
			// 
			this.grbInfo.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.grbInfo.Controls.Add(this.lbInfo);
			this.grbInfo.Location = new System.Drawing.Point(476, 132);
			this.grbInfo.Name = "grbInfo";
			this.grbInfo.Size = new System.Drawing.Size(318, 464);
			this.grbInfo.TabIndex = 22;
			this.grbInfo.TabStop = false;
			this.grbInfo.Text = "READ ME";
			// 
			// lbInfo
			// 
			this.lbInfo.AutoEllipsis = true;
			this.lbInfo.AutoSize = true;
			this.lbInfo.ForeColor = System.Drawing.Color.MidnightBlue;
			this.lbInfo.Location = new System.Drawing.Point(6, 16);
			this.lbInfo.Name = "lbInfo";
			this.lbInfo.Size = new System.Drawing.Size(47, 13);
			this.lbInfo.TabIndex = 21;
			this.lbInfo.Text = "Warning";
			this.lbInfo.Visible = false;
			// 
			// RequiredDirectoriesControl
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.AutoSize = true;
			this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.Controls.Add(this.grbInfo);
			this.Controls.Add(this.grbMulti);
			this.Controls.Add(this.lbWarning);
			this.Controls.Add(this.butSelectInfoDirectory);
			this.Controls.Add(this.lblInstallInfoPrompt);
			this.Controls.Add(this.tbxInstallInfo);
			this.Controls.Add(this.lblInstallInfoLabel);
			this.Controls.Add(this.butSelectModDirectory);
			this.Controls.Add(this.lblModPrompt);
			this.Controls.Add(this.tbxModDirectory);
			this.Controls.Add(this.lblModDirectoryLabel);
			this.Controls.Add(this.butSelectToolDirectory);
			this.Controls.Add(this.lblToolPrompt);
			this.Controls.Add(this.tbxToolDirectory);
			this.Controls.Add(this.lblToolDirectoryLabel);
			this.Controls.Add(this.butSelectVirtualDirectory);
			this.Controls.Add(this.lblVirtualPrompt);
			this.Controls.Add(this.tbxVirtualDirectory);
			this.Controls.Add(this.lblVirtualDirectoryLabel);
			this.m_fpdFontProvider.SetFontSet(this, "StandardText");
			this.MinimumSize = new System.Drawing.Size(800, 600);
			this.Name = "RequiredDirectoriesControl";
			this.Size = new System.Drawing.Size(800, 600);
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).EndInit();
			this.grbMulti.ResumeLayout(false);
			this.grbMulti.PerformLayout();
			this.grbInfo.ResumeLayout(false);
			this.grbInfo.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Button butSelectInfoDirectory;
		private System.Windows.Forms.Label lblInstallInfoPrompt;
		private System.Windows.Forms.TextBox tbxInstallInfo;
		private System.Windows.Forms.Label lblInstallInfoLabel;
		private System.Windows.Forms.Button butSelectModDirectory;
		private System.Windows.Forms.Label lblModPrompt;
		private System.Windows.Forms.TextBox tbxModDirectory;
		private System.Windows.Forms.Label lblModDirectoryLabel;
		private System.Windows.Forms.Button butSelectToolDirectory;
		private System.Windows.Forms.Label lblToolPrompt;
		private System.Windows.Forms.TextBox tbxToolDirectory;
		private System.Windows.Forms.Label lblToolDirectoryLabel;
		private System.Windows.Forms.Button butSelectVirtualDirectory;
		private System.Windows.Forms.Label lblVirtualPrompt;
		private System.Windows.Forms.TextBox tbxVirtualDirectory;
		private System.Windows.Forms.Label lblVirtualDirectoryLabel;
		private System.Windows.Forms.Button butSelectLinkDirectory;
		private System.Windows.Forms.Label lblLinkPrompt;
		private System.Windows.Forms.TextBox tbxLinkDirectory;
		private System.Windows.Forms.Label lblLinkDirectoryLabel;
		private System.Windows.Forms.CheckBox ckbUseMultiHDInstall;
		private System.Windows.Forms.ErrorProvider erpErrors;
		private System.Windows.Forms.FolderBrowserDialog fbdDirectory;
		private System.Windows.Forms.Label lbWarning;
		private System.Windows.Forms.GroupBox grbMulti;
		private System.Windows.Forms.GroupBox grbInfo;
		private System.Windows.Forms.Label lbInfo;
	}
}
