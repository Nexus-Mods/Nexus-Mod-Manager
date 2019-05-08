namespace Nexus.Client.Settings.UI
{
	partial class DownloadSettingsPage
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
            this.checkBoxMaxConcurrentDownloads = new System.Windows.Forms.ComboBox();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.label1 = new System.Windows.Forms.Label();
            this.checkBoxPremiumOnly = new System.Windows.Forms.CheckBox();
            this.lblWarning = new System.Windows.Forms.Label();
            this.lblMaxConcurrentDownload = new System.Windows.Forms.Label();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.buttonChromeFix = new System.Windows.Forms.Button();
            this.label2 = new System.Windows.Forms.Label();
            this.groupBox2.SuspendLayout();
            this.groupBox3.SuspendLayout();
            this.SuspendLayout();
            // 
            // checkBoxMaxConcurrentDownloads
            // 
            this.checkBoxMaxConcurrentDownloads.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.checkBoxMaxConcurrentDownloads.FormattingEnabled = true;
            this.checkBoxMaxConcurrentDownloads.Location = new System.Drawing.Point(177, 126);
            this.checkBoxMaxConcurrentDownloads.Name = "checkBoxMaxConcurrentDownloads";
            this.checkBoxMaxConcurrentDownloads.Size = new System.Drawing.Size(64, 21);
            this.checkBoxMaxConcurrentDownloads.TabIndex = 1;
            // 
            // groupBox2
            // 
            this.groupBox2.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBox2.Controls.Add(this.label1);
            this.groupBox2.Controls.Add(this.checkBoxPremiumOnly);
            this.groupBox2.Location = new System.Drawing.Point(3, 3);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(414, 53);
            this.groupBox2.TabIndex = 7;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Premium features";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(6, 25);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(149, 13);
            this.label1.TabIndex = 4;
            this.label1.Text = "Use multithreaded downloads:";
            // 
            // checkBoxPremiumOnly
            // 
            this.checkBoxPremiumOnly.AutoSize = true;
            this.checkBoxPremiumOnly.Location = new System.Drawing.Point(193, 25);
            this.checkBoxPremiumOnly.Name = "checkBoxPremiumOnly";
            this.checkBoxPremiumOnly.Size = new System.Drawing.Size(15, 14);
            this.checkBoxPremiumOnly.TabIndex = 0;
            this.checkBoxPremiumOnly.UseVisualStyleBackColor = true;
            // 
            // lblWarning
            // 
            this.lblWarning.AutoSize = true;
            this.lblWarning.Location = new System.Drawing.Point(9, 161);
            this.lblWarning.Name = "lblWarning";
            this.lblWarning.Size = new System.Drawing.Size(330, 13);
            this.lblWarning.TabIndex = 8;
            this.lblWarning.Text = "* Some of the settings are only available for logged in Premium users.";
            // 
            // lblMaxConcurrentDownload
            // 
            this.lblMaxConcurrentDownload.AutoSize = true;
            this.lblMaxConcurrentDownload.Location = new System.Drawing.Point(9, 131);
            this.lblMaxConcurrentDownload.Name = "lblMaxConcurrentDownload";
            this.lblMaxConcurrentDownload.Size = new System.Drawing.Size(162, 13);
            this.lblMaxConcurrentDownload.TabIndex = 4;
            this.lblMaxConcurrentDownload.Text = "Maximum concurrent downloads:";
            // 
            // groupBox3
            // 
            this.groupBox3.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBox3.Controls.Add(this.buttonChromeFix);
            this.groupBox3.Controls.Add(this.label2);
            this.groupBox3.Location = new System.Drawing.Point(3, 62);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.Size = new System.Drawing.Size(414, 53);
            this.groupBox3.TabIndex = 9;
            this.groupBox3.TabStop = false;
            this.groupBox3.Text = "Download Fix/Tweaks";
            // 
            // buttonChromeFix
            // 
            this.buttonChromeFix.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.buttonChromeFix.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.buttonChromeFix.Location = new System.Drawing.Point(316, 19);
            this.buttonChromeFix.Name = "buttonChromeFix";
            this.buttonChromeFix.Size = new System.Drawing.Size(64, 24);
            this.buttonChromeFix.TabIndex = 6;
            this.buttonChromeFix.Text = "Fix It!";
            this.buttonChromeFix.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.buttonChromeFix.UseVisualStyleBackColor = true;
            this.buttonChromeFix.Click += new System.EventHandler(this.butChromeFix_Click);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(6, 25);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(284, 13);
            this.label2.TabIndex = 4;
            this.label2.Text = "Chrome/Windows 8.x - Fix (Could require Admin privileges):";
            // 
            // DownloadSettingsPage
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.groupBox3);
            this.Controls.Add(this.lblWarning);
            this.Controls.Add(this.lblMaxConcurrentDownload);
            this.Controls.Add(this.checkBoxMaxConcurrentDownloads);
            this.Controls.Add(this.groupBox2);
            this.Name = "DownloadSettingsPage";
            this.Size = new System.Drawing.Size(420, 193);
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.groupBox3.ResumeLayout(false);
            this.groupBox3.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

		}

		#endregion
		private System.Windows.Forms.GroupBox groupBox2;
		private System.Windows.Forms.ComboBox checkBoxMaxConcurrentDownloads;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.Label lblMaxConcurrentDownload;
		private System.Windows.Forms.CheckBox checkBoxPremiumOnly;
		private System.Windows.Forms.Label lblWarning;
		private System.Windows.Forms.GroupBox groupBox3;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.Button buttonChromeFix;
	}
}
