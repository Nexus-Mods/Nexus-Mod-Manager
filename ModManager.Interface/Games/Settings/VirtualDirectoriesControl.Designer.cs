namespace Nexus.Client.Games.Settings
{
	partial class VirtualDirectoriesControl
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
			this.erpErrors = new System.Windows.Forms.ErrorProvider(this.components);
			this.fbdDirectory = new System.Windows.Forms.FolderBrowserDialog();
			this.grbInfo = new System.Windows.Forms.GroupBox();
			this.grbMulti = new System.Windows.Forms.GroupBox();
			this.ckbUseMultiHDInstall = new System.Windows.Forms.CheckBox();
			this.lblLinkDirectoryLabel = new System.Windows.Forms.Label();
			this.tbxLinkDirectory = new System.Windows.Forms.TextBox();
			this.lblLinkPrompt = new System.Windows.Forms.Label();
			this.butSelectLinkDirectory = new System.Windows.Forms.Button();
			this.butSelectVirtualDirectory = new System.Windows.Forms.Button();
			this.lblVirtualPrompt = new System.Windows.Forms.Label();
			this.tbxVirtualDirectory = new System.Windows.Forms.TextBox();
			this.lblVirtualDirectoryLabel = new System.Windows.Forms.Label();
			this.lbInfo = new System.Windows.Forms.Label();
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).BeginInit();
			this.grbInfo.SuspendLayout();
			this.grbMulti.SuspendLayout();
			this.SuspendLayout();
			// 
			// erpErrors
			// 
			this.erpErrors.ContainerControl = this;
			// 
			// grbInfo
			// 
			this.grbInfo.Anchor = System.Windows.Forms.AnchorStyles.Top;
			this.grbInfo.Controls.Add(this.grbMulti);
			this.grbInfo.Controls.Add(this.butSelectVirtualDirectory);
			this.grbInfo.Controls.Add(this.lblVirtualPrompt);
			this.grbInfo.Controls.Add(this.tbxVirtualDirectory);
			this.grbInfo.Controls.Add(this.lblVirtualDirectoryLabel);
			this.grbInfo.Controls.Add(this.lbInfo);
			this.grbInfo.Location = new System.Drawing.Point(3, 9);
			this.grbInfo.Name = "grbInfo";
			this.grbInfo.Size = new System.Drawing.Size(890, 347);
			this.grbInfo.TabIndex = 22;
			this.grbInfo.TabStop = false;
			this.grbInfo.Text = "Virtual mod install setup:";
			// 
			// grbMulti
			// 
			this.grbMulti.Controls.Add(this.ckbUseMultiHDInstall);
			this.grbMulti.Controls.Add(this.lblLinkDirectoryLabel);
			this.grbMulti.Controls.Add(this.tbxLinkDirectory);
			this.grbMulti.Controls.Add(this.lblLinkPrompt);
			this.grbMulti.Controls.Add(this.butSelectLinkDirectory);
			this.grbMulti.Location = new System.Drawing.Point(6, 143);
			this.grbMulti.Name = "grbMulti";
			this.grbMulti.Size = new System.Drawing.Size(525, 166);
			this.grbMulti.TabIndex = 26;
			this.grbMulti.TabStop = false;
			this.grbMulti.Text = "Multi-HD install - You MUST run the program as Administrator to enable this:";
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
			// lblLinkDirectoryLabel
			// 
			this.lblLinkDirectoryLabel.AutoSize = true;
			this.lblLinkDirectoryLabel.Location = new System.Drawing.Point(11, 130);
			this.lblLinkDirectoryLabel.Name = "lblLinkDirectoryLabel";
			this.lblLinkDirectoryLabel.Size = new System.Drawing.Size(76, 13);
			this.lblLinkDirectoryLabel.TabIndex = 16;
			this.lblLinkDirectoryLabel.Text = "Required Link:";
			// 
			// tbxLinkDirectory
			// 
			this.tbxLinkDirectory.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.tbxLinkDirectory.Location = new System.Drawing.Point(90, 127);
			this.tbxLinkDirectory.Name = "tbxLinkDirectory";
			this.tbxLinkDirectory.Size = new System.Drawing.Size(391, 20);
			this.tbxLinkDirectory.TabIndex = 15;
			// 
			// lblLinkPrompt
			// 
			this.lblLinkPrompt.AutoEllipsis = true;
			this.lblLinkPrompt.AutoSize = true;
			this.lblLinkPrompt.Location = new System.Drawing.Point(6, 94);
			this.lblLinkPrompt.Name = "lblLinkPrompt";
			this.lblLinkPrompt.Size = new System.Drawing.Size(283, 13);
			this.lblLinkPrompt.TabIndex = 18;
			this.lblLinkPrompt.Text = "Select where you want to install Multi-HD incompatible files";
			// 
			// butSelectLinkDirectory
			// 
			this.butSelectLinkDirectory.Anchor = System.Windows.Forms.AnchorStyles.Left;
			this.butSelectLinkDirectory.AutoSize = true;
			this.butSelectLinkDirectory.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.butSelectLinkDirectory.Location = new System.Drawing.Point(487, 125);
			this.butSelectLinkDirectory.Name = "butSelectLinkDirectory";
			this.butSelectLinkDirectory.Size = new System.Drawing.Size(26, 23);
			this.butSelectLinkDirectory.TabIndex = 17;
			this.butSelectLinkDirectory.Text = "...";
			this.butSelectLinkDirectory.UseVisualStyleBackColor = true;
			this.butSelectLinkDirectory.Click += new System.EventHandler(this.butSelectLinkDirectory_Click);
			// 
			// butSelectVirtualDirectory
			// 
			this.butSelectVirtualDirectory.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.butSelectVirtualDirectory.AutoSize = true;
			this.butSelectVirtualDirectory.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.butSelectVirtualDirectory.Location = new System.Drawing.Point(505, 50);
			this.butSelectVirtualDirectory.Name = "butSelectVirtualDirectory";
			this.butSelectVirtualDirectory.Size = new System.Drawing.Size(26, 23);
			this.butSelectVirtualDirectory.TabIndex = 24;
			this.butSelectVirtualDirectory.Text = "...";
			this.butSelectVirtualDirectory.UseVisualStyleBackColor = true;
			this.butSelectVirtualDirectory.Click += new System.EventHandler(this.butSelectVirtualDirectory_Click);
			// 
			// lblVirtualPrompt
			// 
			this.lblVirtualPrompt.AutoEllipsis = true;
			this.lblVirtualPrompt.AutoSize = true;
			this.lblVirtualPrompt.Location = new System.Drawing.Point(6, 16);
			this.lblVirtualPrompt.Name = "lblVirtualPrompt";
			this.lblVirtualPrompt.Size = new System.Drawing.Size(273, 13);
			this.lblVirtualPrompt.TabIndex = 25;
			this.lblVirtualPrompt.Text = "Select the path where you want NMM to install the mods";
			// 
			// tbxVirtualDirectory
			// 
			this.tbxVirtualDirectory.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.tbxVirtualDirectory.Location = new System.Drawing.Point(96, 53);
			this.tbxVirtualDirectory.Name = "tbxVirtualDirectory";
			this.tbxVirtualDirectory.Size = new System.Drawing.Size(403, 20);
			this.tbxVirtualDirectory.TabIndex = 22;
			// 
			// lblVirtualDirectoryLabel
			// 
			this.lblVirtualDirectoryLabel.AutoSize = true;
			this.lblVirtualDirectoryLabel.Location = new System.Drawing.Point(21, 56);
			this.lblVirtualDirectoryLabel.Name = "lblVirtualDirectoryLabel";
			this.lblVirtualDirectoryLabel.Size = new System.Drawing.Size(69, 13);
			this.lblVirtualDirectoryLabel.TabIndex = 23;
			this.lblVirtualDirectoryLabel.Text = "Virtual Install:";
			// 
			// lbInfo
			// 
			this.lbInfo.Anchor = System.Windows.Forms.AnchorStyles.Top;
			this.lbInfo.AutoEllipsis = true;
			this.lbInfo.ForeColor = System.Drawing.Color.MidnightBlue;
			this.lbInfo.Location = new System.Drawing.Point(537, 16);
			this.lbInfo.Name = "lbInfo";
			this.lbInfo.Size = new System.Drawing.Size(347, 319);
			this.lbInfo.TabIndex = 21;
			this.lbInfo.Text = "Warning";
			// 
			// VirtualDirectoriesControl
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.AutoSize = true;
			this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.Controls.Add(this.grbInfo);
			this.m_fpdFontProvider.SetFontSet(this, "StandardText");
			this.MinimumSize = new System.Drawing.Size(900, 300);
			this.Name = "VirtualDirectoriesControl";
			this.Size = new System.Drawing.Size(900, 300);
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).EndInit();
			this.grbInfo.ResumeLayout(false);
			this.grbInfo.PerformLayout();
			this.grbMulti.ResumeLayout(false);
			this.grbMulti.PerformLayout();
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.ErrorProvider erpErrors;
		private System.Windows.Forms.FolderBrowserDialog fbdDirectory;
		private System.Windows.Forms.GroupBox grbInfo;
		private System.Windows.Forms.Label lbInfo;
		private System.Windows.Forms.GroupBox grbMulti;
		private System.Windows.Forms.CheckBox ckbUseMultiHDInstall;
		private System.Windows.Forms.Label lblLinkDirectoryLabel;
		private System.Windows.Forms.TextBox tbxLinkDirectory;
		private System.Windows.Forms.Label lblLinkPrompt;
		private System.Windows.Forms.Button butSelectLinkDirectory;
		private System.Windows.Forms.Button butSelectVirtualDirectory;
		private System.Windows.Forms.Label lblVirtualPrompt;
		private System.Windows.Forms.TextBox tbxVirtualDirectory;
		private System.Windows.Forms.Label lblVirtualDirectoryLabel;
	}
}
