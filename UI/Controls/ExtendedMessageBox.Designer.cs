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
			this.pnlButtons = new DevExpress.XtraEditors.PanelControl();
			this.pnlMessage = new DevExpress.XtraEditors.PanelControl();
			this.pnlLabel = new DevExpress.XtraEditors.PanelControl();
			this.albPrompt = new DevExpress.XtraEditors.LabelControl();
			this.pbxIcon = new DevExpress.XtraEditors.PictureEdit();
			this.pnlRemember = new DevExpress.XtraEditors.PanelControl();
			this.cbxRemember = new DevExpress.XtraEditors.CheckEdit();
			this.pnlDetails = new DevExpress.XtraEditors.PanelControl();
			this.hlbDetails = new DevExpress.XtraEditors.HtmlContentControl();
			((System.ComponentModel.ISupportInitialize)(this.pnlButtons)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.pnlMessage)).BeginInit();
			this.pnlMessage.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.pnlLabel)).BeginInit();
			this.pnlLabel.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.pbxIcon.Properties)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.pnlRemember)).BeginInit();
			this.pnlRemember.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.cbxRemember.Properties)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.pnlDetails)).BeginInit();
			this.pnlDetails.SuspendLayout();
			this.SuspendLayout();
			//
			// pnlButtons
			//
			this.pnlButtons.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
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
			this.pnlMessage.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
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
			this.pnlLabel.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
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
			this.albPrompt.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
			this.albPrompt.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.Vertical;
			this.albPrompt.Dock = System.Windows.Forms.DockStyle.Top;
			this.albPrompt.Location = new System.Drawing.Point(0, 24);
			this.albPrompt.Name = "albPrompt";
			this.albPrompt.Size = new System.Drawing.Size(188, 13);
			this.albPrompt.TabIndex = 0;
			this.albPrompt.Text = "Message";
			//
			// pbxIcon
			//
			this.pbxIcon.Dock = System.Windows.Forms.DockStyle.Left;
			this.pbxIcon.Location = new System.Drawing.Point(0, 0);
			this.pbxIcon.Margin = new System.Windows.Forms.Padding(0);
			this.pbxIcon.Name = "pbxIcon";
			this.pbxIcon.Padding = new System.Windows.Forms.Padding(24, 17, 12, 17);
			this.pbxIcon.Properties.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
			this.pbxIcon.Properties.ShowCameraMenuItem = DevExpress.XtraEditors.Controls.CameraMenuItemVisibility.Auto;
			this.pbxIcon.Properties.ShowMenu = false;
			this.pbxIcon.Properties.SizeMode = DevExpress.XtraEditors.Controls.PictureSizeMode.Squeeze;
			this.pbxIcon.Size = new System.Drawing.Size(72, 66);
			this.pbxIcon.TabIndex = 1;
			//
			// pnlRemember
			//
			this.pnlRemember.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
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
			this.cbxRemember.Location = new System.Drawing.Point(130, 7);
			this.cbxRemember.Name = "cbxRemember";
			this.cbxRemember.Properties.Caption = "Remember my selection";
			this.cbxRemember.Size = new System.Drawing.Size(142, 20);
			this.cbxRemember.TabIndex = 0;
			//
			// pnlDetails
			//
			this.pnlDetails.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
			this.pnlDetails.Controls.Add(this.hlbDetails);
			this.pnlDetails.Dock = System.Windows.Forms.DockStyle.Fill;
			this.pnlDetails.Location = new System.Drawing.Point(0, 66);
			this.pnlDetails.Name = "pnlDetails";
			this.pnlDetails.Padding = new System.Windows.Forms.Padding(12, 0, 12, 0);
			this.pnlDetails.Size = new System.Drawing.Size(284, 120);
			this.pnlDetails.TabIndex = 6;
			//
			// hlbDetails
			//
			this.hlbDetails.AutoScroll = true;
			this.hlbDetails.Dock = System.Windows.Forms.DockStyle.Fill;
			this.hlbDetails.Location = new System.Drawing.Point(12, 0);
			this.hlbDetails.Name = "hlbDetails";
			this.hlbDetails.Size = new System.Drawing.Size(260, 120);
			this.hlbDetails.TabIndex = 5;
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
			this.Text = "ExtendedMessageBox";
			this.TopMost = true;
			((System.ComponentModel.ISupportInitialize)(this.pnlButtons)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.pnlMessage)).EndInit();
			this.pnlMessage.ResumeLayout(false);
			this.pnlMessage.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this.pnlLabel)).EndInit();
			this.pnlLabel.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.pbxIcon.Properties)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.pnlRemember)).EndInit();
			this.pnlRemember.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.cbxRemember.Properties)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.pnlDetails)).EndInit();
			this.pnlDetails.ResumeLayout(false);
			this.ResumeLayout(false);
			this.PerformLayout();
		}

		#endregion

		private DevExpress.XtraEditors.LabelControl albPrompt;
		private DevExpress.XtraEditors.PictureEdit pbxIcon;
		private DevExpress.XtraEditors.PanelControl pnlButtons;
		private DevExpress.XtraEditors.PanelControl pnlMessage;
		private DevExpress.XtraEditors.PanelControl pnlLabel;
		private DevExpress.XtraEditors.PanelControl pnlRemember;
		private DevExpress.XtraEditors.CheckEdit cbxRemember;
		private DevExpress.XtraEditors.HtmlContentControl hlbDetails;
		private DevExpress.XtraEditors.PanelControl pnlDetails;
	}
}
