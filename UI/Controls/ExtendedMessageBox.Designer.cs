namespace Nexus.UI.Controls
{
	partial class ExtendedMessageBox
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

		#region Windows Form Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.pnlButtons = new System.Windows.Forms.Panel();
			this.pnlMessage = new System.Windows.Forms.Panel();
			this.pnlLabel = new System.Windows.Forms.Panel();
			this.albPrompt = new Nexus.UI.Controls.AutosizeLabel();
			this.pbxIcon = new System.Windows.Forms.PictureBox();
			this.pnlRemember = new System.Windows.Forms.Panel();
			this.cbxRemember = new System.Windows.Forms.CheckBox();
			this.pnlDetails = new System.Windows.Forms.Panel();
			this.hlbDetails = new Nexus.UI.Controls.HtmlLabel();
			this.pnlMessage.SuspendLayout();
			this.pnlLabel.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.pbxIcon)).BeginInit();
			this.pnlRemember.SuspendLayout();
			this.pnlDetails.SuspendLayout();
			this.SuspendLayout();
			// 
			// pnlButtons
			// 
			this.pnlButtons.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.pnlButtons.Location = new System.Drawing.Point(0, 215);
			this.pnlButtons.Name = "pnlButtons";
			this.pnlButtons.Size = new System.Drawing.Size(284, 47);
			this.pnlButtons.TabIndex = 2;
			// 
			// pnlMessage
			// 
			this.pnlMessage.AutoSize = true;
			this.pnlMessage.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.pnlMessage.BackColor = System.Drawing.SystemColors.Window;
			this.pnlMessage.Controls.Add(this.pnlLabel);
			this.pnlMessage.Controls.Add(this.pbxIcon);
			this.pnlMessage.Dock = System.Windows.Forms.DockStyle.Top;
			this.pnlMessage.Location = new System.Drawing.Point(0, 0);
			this.pnlMessage.Name = "pnlMessage";
			this.pnlMessage.Size = new System.Drawing.Size(284, 66);
			this.pnlMessage.TabIndex = 3;
			// 
			// pnlLabel
			// 
			this.pnlLabel.AutoSize = true;
			this.pnlLabel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.pnlLabel.Controls.Add(this.albPrompt);
			this.pnlLabel.Dock = System.Windows.Forms.DockStyle.Top;
			this.pnlLabel.Location = new System.Drawing.Point(72, 0);
			this.pnlLabel.Margin = new System.Windows.Forms.Padding(0);
			this.pnlLabel.Name = "pnlLabel";
			this.pnlLabel.Padding = new System.Windows.Forms.Padding(0, 24, 24, 24);
			this.pnlLabel.Size = new System.Drawing.Size(212, 66);
			this.pnlLabel.TabIndex = 2;
			// 
			// albPrompt
			// 
			this.albPrompt.BackColor = System.Drawing.SystemColors.Window;
			this.albPrompt.Cursor = System.Windows.Forms.Cursors.Arrow;
			this.albPrompt.Dock = System.Windows.Forms.DockStyle.Top;
			this.albPrompt.Location = new System.Drawing.Point(0, 24);
			this.albPrompt.Name = "albPrompt";
			this.albPrompt.Size = new System.Drawing.Size(188, 18);
			this.albPrompt.TabIndex = 0;
			this.albPrompt.Text = "autolabel1";
			// 
			// pbxIcon
			// 
			this.pbxIcon.Dock = System.Windows.Forms.DockStyle.Left;
			this.pbxIcon.Location = new System.Drawing.Point(0, 0);
			this.pbxIcon.Margin = new System.Windows.Forms.Padding(0);
			this.pbxIcon.Name = "pbxIcon";
			this.pbxIcon.Padding = new System.Windows.Forms.Padding(24, 24, 12, 24);
			this.pbxIcon.Size = new System.Drawing.Size(72, 66);
			this.pbxIcon.SizeMode = System.Windows.Forms.PictureBoxSizeMode.CenterImage;
			this.pbxIcon.TabIndex = 1;
			this.pbxIcon.TabStop = false;
			// 
			// pnlRemember
			// 
			this.pnlRemember.Controls.Add(this.cbxRemember);
			this.pnlRemember.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.pnlRemember.Location = new System.Drawing.Point(0, 186);
			this.pnlRemember.Name = "pnlRemember";
			this.pnlRemember.Size = new System.Drawing.Size(284, 29);
			this.pnlRemember.TabIndex = 4;
			// 
			// cbxRemember
			// 
			this.cbxRemember.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.cbxRemember.AutoSize = true;
			this.cbxRemember.Location = new System.Drawing.Point(134, 12);
			this.cbxRemember.Name = "cbxRemember";
			this.cbxRemember.Size = new System.Drawing.Size(138, 17);
			this.cbxRemember.TabIndex = 0;
			this.cbxRemember.Text = "Remember my selection";
			this.cbxRemember.UseVisualStyleBackColor = true;
			// 
			// pnlDetails
			// 
			this.pnlDetails.BackColor = System.Drawing.SystemColors.Window;
			this.pnlDetails.Controls.Add(this.hlbDetails);
			this.pnlDetails.Dock = System.Windows.Forms.DockStyle.Fill;
			this.pnlDetails.Location = new System.Drawing.Point(0, 66);
			this.pnlDetails.Name = "pnlDetails";
			this.pnlDetails.Padding = new System.Windows.Forms.Padding(12, 0, 0, 0);
			this.pnlDetails.Size = new System.Drawing.Size(284, 120);
			this.pnlDetails.TabIndex = 6;
			// 
			// hlbDetails
			// 
			this.hlbDetails.BackColor = System.Drawing.SystemColors.Window;
			this.hlbDetails.Dock = System.Windows.Forms.DockStyle.Fill;
			this.hlbDetails.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F);
			this.hlbDetails.ForeColor = System.Drawing.SystemColors.ControlText;
			this.hlbDetails.Location = new System.Drawing.Point(12, 0);
			this.hlbDetails.MinimumSize = new System.Drawing.Size(20, 20);
			this.hlbDetails.Name = "hlbDetails";
			this.hlbDetails.ScrollBarsEnabled = false;
			this.hlbDetails.Size = new System.Drawing.Size(272, 120);
			this.hlbDetails.TabIndex = 5;
			this.hlbDetails.Text = "htmlLabel1";
			// 
			// ExtendedMessageBox
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(284, 262);
			this.Controls.Add(this.pnlDetails);
			this.Controls.Add(this.pnlRemember);
			this.Controls.Add(this.pnlButtons);
			this.Controls.Add(this.pnlMessage);
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.MinimumSize = new System.Drawing.Size(250, 28);
			this.Name = "ExtendedMessageBox";
			this.ShowIcon = false;
			this.ShowInTaskbar = false;
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
			this.Text = "RememberSelectionMessageBox";
			this.pnlMessage.ResumeLayout(false);
			this.pnlMessage.PerformLayout();
			this.pnlLabel.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.pbxIcon)).EndInit();
			this.pnlRemember.ResumeLayout(false);
			this.pnlRemember.PerformLayout();
			this.pnlDetails.ResumeLayout(false);
			this.ResumeLayout(false);
			this.PerformLayout();
			this.TopMost = true;
		}

		#endregion

		private Nexus.UI.Controls.AutosizeLabel albPrompt;
		private System.Windows.Forms.PictureBox pbxIcon;
		private System.Windows.Forms.Panel pnlButtons;
		private System.Windows.Forms.Panel pnlMessage;
		private System.Windows.Forms.Panel pnlLabel;
		private System.Windows.Forms.Panel pnlRemember;
		private System.Windows.Forms.CheckBox cbxRemember;
		private Nexus.UI.Controls.HtmlLabel hlbDetails;
		private System.Windows.Forms.Panel pnlDetails;
	}
}