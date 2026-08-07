namespace Nexus.Client
{
	partial class ApplicationInitializationForm
	{
		private System.ComponentModel.IContainer components = null;

		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
				components.Dispose();
			base.Dispose(disposing);
		}

		private void InitializeComponent()
		{
			this.pbxLogo = new DevExpress.XtraEditors.PictureEdit();
			this.lblVersion = new DevExpress.XtraEditors.LabelControl();
			((System.ComponentModel.ISupportInitialize)(this.pbxLogo.Properties)).BeginInit();
			this.SuspendLayout();
			// 
			// pbxLogo
			// 
			this.pbxLogo.Dock = System.Windows.Forms.DockStyle.Fill;
			this.pbxLogo.EditValue = global::Nexus.Client.Properties.Resources.NMM_P_Logo_800;
			this.pbxLogo.Location = new System.Drawing.Point(0, 0);
			this.pbxLogo.Name = "pbxLogo";
			this.pbxLogo.Properties.AllowFocused = false;
			this.pbxLogo.Properties.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
			this.pbxLogo.Properties.ShowCameraMenuItem = DevExpress.XtraEditors.Controls.CameraMenuItemVisibility.Auto;
			this.pbxLogo.Properties.ShowMenu = false;
			this.pbxLogo.Properties.SizeMode = DevExpress.XtraEditors.Controls.PictureSizeMode.Stretch;
			this.pbxLogo.Size = new System.Drawing.Size(800, 448);
			this.pbxLogo.TabIndex = 0;
			// 
			// lblVersion
			// 
			this.lblVersion.Appearance.BackColor = System.Drawing.Color.Transparent;
			this.lblVersion.Appearance.Font = new System.Drawing.Font("Calibri", 14F, System.Drawing.FontStyle.Bold);
			this.lblVersion.Appearance.ForeColor = System.Drawing.Color.White;
			this.lblVersion.Appearance.Options.UseBackColor = true;
			this.lblVersion.Appearance.Options.UseFont = true;
			this.lblVersion.Appearance.Options.UseForeColor = true;
			this.lblVersion.Location = new System.Drawing.Point(148, 328);
			this.lblVersion.Name = "lblVersion";
			this.lblVersion.Size = new System.Drawing.Size(56, 23);
			this.lblVersion.TabIndex = 1;
			this.lblVersion.Text = "0.10.11";
			// 
			// ApplicationInitializationForm
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackColor = System.Drawing.Color.FromArgb(38, 38, 38);
			this.ClientSize = new System.Drawing.Size(800, 448);
			this.ControlBox = false;
			this.Controls.Add(this.lblVersion);
			this.Controls.Add(this.pbxLogo);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "ApplicationInitializationForm";
			this.ShowInTaskbar = false;
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
			this.Text = "Nexus Mod Manager";
			((System.ComponentModel.ISupportInitialize)(this.pbxLogo.Properties)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();
		}

		private DevExpress.XtraEditors.PictureEdit pbxLogo;
		private DevExpress.XtraEditors.LabelControl lblVersion;
	}
}
