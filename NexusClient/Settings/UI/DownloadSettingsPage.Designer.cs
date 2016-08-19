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
			this.groupBox1 = new System.Windows.Forms.GroupBox();
			this.cbxServerLocation = new System.Windows.Forms.ComboBox();
			this.cbxMaxConcurrentDownloads = new System.Windows.Forms.ComboBox();
			this.label5 = new System.Windows.Forms.Label();
			this.groupBox2 = new System.Windows.Forms.GroupBox();
			this.label1 = new System.Windows.Forms.Label();
			this.ckbPremiumOnly = new System.Windows.Forms.CheckBox();
			this.lblWarning = new System.Windows.Forms.Label();
			this.lblMaxConcurrentDownload = new System.Windows.Forms.Label();
			this.groupBox3 = new System.Windows.Forms.GroupBox();
			this.butChromeFix = new System.Windows.Forms.Button();
			this.label2 = new System.Windows.Forms.Label();
			this.groupBox1.SuspendLayout();
			this.groupBox2.SuspendLayout();
			this.groupBox3.SuspendLayout();
			this.SuspendLayout();
			// 
			// groupBox1
			// 
			this.groupBox1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.groupBox1.Controls.Add(this.cbxServerLocation);
			this.groupBox1.Controls.Add(this.label5);
			this.groupBox1.Location = new System.Drawing.Point(3, 3);
			this.groupBox1.Name = "groupBox1";
			this.groupBox1.Size = new System.Drawing.Size(414, 64);
			this.groupBox1.TabIndex = 3;
			this.groupBox1.TabStop = false;
			this.groupBox1.Text = "Fileserver preferences";
			// 
			// cbxServerLocation
			// 
			this.cbxServerLocation.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed;
			this.cbxServerLocation.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.cbxServerLocation.FormattingEnabled = true;
			this.cbxServerLocation.Location = new System.Drawing.Point(111, 25);
			this.cbxServerLocation.Name = "cbxServerLocation";
			this.cbxServerLocation.Size = new System.Drawing.Size(186, 21);
			this.cbxServerLocation.TabIndex = 1;
			this.cbxServerLocation.DrawItem += new System.Windows.Forms.DrawItemEventHandler(this.cbxServerLocation_DrawItem);
			// 
			// cbxMaxConcurrentDownloads
			// 
			this.cbxMaxConcurrentDownloads.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.cbxMaxConcurrentDownloads.FormattingEnabled = true;
			this.cbxMaxConcurrentDownloads.Location = new System.Drawing.Point(180, 220);
			this.cbxMaxConcurrentDownloads.Name = "cbxMaxConcurrentDownloads";
			this.cbxMaxConcurrentDownloads.Size = new System.Drawing.Size(64, 21);
			this.cbxMaxConcurrentDownloads.TabIndex = 1;
			// 
			// label5
			// 
			this.label5.AutoSize = true;
			this.label5.Location = new System.Drawing.Point(24, 28);
			this.label5.Name = "label5";
			this.label5.Size = new System.Drawing.Size(85, 13);
			this.label5.TabIndex = 5;
			this.label5.Text = "Server Location:";
			// 
			// groupBox2
			// 
			this.groupBox2.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.groupBox2.Controls.Add(this.label1);
			this.groupBox2.Controls.Add(this.ckbPremiumOnly);
			this.groupBox2.Location = new System.Drawing.Point(3, 80);
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
			// lblMaxConcurrentDownload
			// 
			this.lblMaxConcurrentDownload.AutoSize = true;
			this.lblMaxConcurrentDownload.Location = new System.Drawing.Point(12, 225);
			this.lblMaxConcurrentDownload.Name = "lblMaxConcurrentDownload";
			this.lblMaxConcurrentDownload.Size = new System.Drawing.Size(330, 13);
			this.lblMaxConcurrentDownload.TabIndex = 4;
			this.lblMaxConcurrentDownload.Text = "Maximum concurrent downloads:";
			// 
			// ckbPremiumOnly
			// 
			this.ckbPremiumOnly.AutoSize = true;
			this.ckbPremiumOnly.Location = new System.Drawing.Point(193, 25);
			this.ckbPremiumOnly.Name = "ckbPremiumOnly";
			this.ckbPremiumOnly.Size = new System.Drawing.Size(15, 14);
			this.ckbPremiumOnly.TabIndex = 0;
			this.ckbPremiumOnly.UseVisualStyleBackColor = true;
			// 
			// lblWarning
			// 
			this.lblWarning.AutoSize = true;
			this.lblWarning.Location = new System.Drawing.Point(12, 255);
			this.lblWarning.Name = "lblWarning";
			this.lblWarning.Size = new System.Drawing.Size(330, 13);
			this.lblWarning.TabIndex = 8;
			this.lblWarning.Text = "* Some of the settings are only available for logged in Premium users.";
			// 
			// groupBox3
			// 
			this.groupBox3.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.groupBox3.Controls.Add(this.butChromeFix);
			this.groupBox3.Controls.Add(this.label2);
			this.groupBox3.Location = new System.Drawing.Point(3, 146);
			this.groupBox3.Name = "groupBox3";
			this.groupBox3.Size = new System.Drawing.Size(414, 53);
			this.groupBox3.TabIndex = 9;
			this.groupBox3.TabStop = false;
			this.groupBox3.Text = "Download Fix/Tweaks";
			// 
			// butChromeFix
			// 
			this.butChromeFix.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.butChromeFix.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this.butChromeFix.Location = new System.Drawing.Point(316, 19);
			this.butChromeFix.Name = "butChromeFix";
			this.butChromeFix.Size = new System.Drawing.Size(64, 24);
			this.butChromeFix.TabIndex = 6;
			this.butChromeFix.Text = "Fix It!";
			this.butChromeFix.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			this.butChromeFix.UseVisualStyleBackColor = true;
			this.butChromeFix.Click += new System.EventHandler(this.butChromeFix_Click);
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
			this.Controls.Add(this.cbxMaxConcurrentDownloads);
			this.Controls.Add(this.groupBox1);
			this.Controls.Add(this.groupBox2);
			this.Name = "DownloadSettingsPage";
			this.Size = new System.Drawing.Size(420, 294);
			this.groupBox1.ResumeLayout(false);
			this.groupBox1.PerformLayout();
			this.groupBox2.ResumeLayout(false);
			this.groupBox2.PerformLayout();
			this.groupBox3.ResumeLayout(false);
			this.groupBox3.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.GroupBox groupBox1;
		private System.Windows.Forms.GroupBox groupBox2;
		private System.Windows.Forms.ComboBox cbxServerLocation;
		private System.Windows.Forms.ComboBox cbxMaxConcurrentDownloads;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.Label label5;
		private System.Windows.Forms.Label lblMaxConcurrentDownload;
		private System.Windows.Forms.CheckBox ckbPremiumOnly;
		private System.Windows.Forms.Label lblWarning;
		private System.Windows.Forms.GroupBox groupBox3;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.Button butChromeFix;
	}
}
