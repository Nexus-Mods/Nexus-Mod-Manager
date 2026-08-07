namespace Nexus.Client.ModManagement.UI
{
	partial class RestoreBackupForm
	{
		private System.ComponentModel.IContainer components = null;

		protected override void Dispose(bool disposing)
		{
			if (disposing && components != null)
				components.Dispose();
			base.Dispose(disposing);
		}

		private void InitializeComponent()
		{
			this.btYes = new DevExpress.XtraEditors.SimpleButton();
			this.btNo = new DevExpress.XtraEditors.SimpleButton();
			this.btCancel = new DevExpress.XtraEditors.SimpleButton();
			this.btSelectFile = new DevExpress.XtraEditors.SimpleButton();
			this.lblYes = new DevExpress.XtraEditors.LabelControl();
			this.lblNo = new DevExpress.XtraEditors.LabelControl();
			this.lblCancel = new DevExpress.XtraEditors.LabelControl();
			this.lblEstimated = new DevExpress.XtraEditors.LabelControl();
			this.fdFile = new DevExpress.XtraEditors.XtraOpenFileDialog();
			this.tbFile = new DevExpress.XtraEditors.TextEdit();
			((System.ComponentModel.ISupportInitialize)(this.tbFile.Properties)).BeginInit();
			this.SuspendLayout();
			// buttons
			this.btYes.Location = new System.Drawing.Point(478, 198); this.btYes.Name = "btYes"; this.btYes.Size = new System.Drawing.Size(106, 23); this.btYes.Text = "Purge and Restore"; this.btYes.Click += new System.EventHandler(this.btYes_Click);
			this.btNo.Location = new System.Drawing.Point(590, 198); this.btNo.Name = "btNo"; this.btNo.Size = new System.Drawing.Size(54, 23); this.btNo.Text = "Restore"; this.btNo.Click += new System.EventHandler(this.btNo_Click);
			this.btCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel; this.btCancel.Location = new System.Drawing.Point(650, 198); this.btCancel.Name = "btCancel"; this.btCancel.Size = new System.Drawing.Size(50, 23); this.btCancel.Text = "Cancel"; this.btCancel.Click += new System.EventHandler(this.btCancel_Click);
			this.btSelectFile.Location = new System.Drawing.Point(634, 127); this.btSelectFile.Name = "btSelectFile"; this.btSelectFile.Size = new System.Drawing.Size(66, 23); this.btSelectFile.Text = "Select File"; this.btSelectFile.Click += new System.EventHandler(this.btSelectFile_Click);
			// labels
			this.lblYes.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap; this.lblYes.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None; this.lblYes.Location = new System.Drawing.Point(16, 35); this.lblYes.Name = "lblYes"; this.lblYes.Size = new System.Drawing.Size(684, 32);
			this.lblNo.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap; this.lblNo.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None; this.lblNo.Location = new System.Drawing.Point(16, 70); this.lblNo.Name = "lblNo"; this.lblNo.Size = new System.Drawing.Size(684, 31);
			this.lblCancel.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None; this.lblCancel.Location = new System.Drawing.Point(16, 108); this.lblCancel.Name = "lblCancel"; this.lblCancel.Size = new System.Drawing.Size(593, 20); this.lblCancel.Text = "Click CANCEL if you want to abort the operation.";
			this.lblEstimated.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap; this.lblEstimated.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None; this.lblEstimated.Location = new System.Drawing.Point(16, 158); this.lblEstimated.Name = "lblEstimated"; this.lblEstimated.Size = new System.Drawing.Size(456, 63); this.lblEstimated.Text = "Estimated Restore Size: "; this.lblEstimated.Visible = false;
			// tbFile
			this.tbFile.Location = new System.Drawing.Point(16, 130); this.tbFile.Name = "tbFile"; this.tbFile.Size = new System.Drawing.Size(612, 20);
			// RestoreBackupForm
			this.CancelButton = this.btCancel;
			this.ClientSize = new System.Drawing.Size(712, 227);
			this.Controls.Add(this.btYes); this.Controls.Add(this.btNo); this.Controls.Add(this.btCancel); this.Controls.Add(this.btSelectFile); this.Controls.Add(this.tbFile); this.Controls.Add(this.lblYes); this.Controls.Add(this.lblNo); this.Controls.Add(this.lblEstimated); this.Controls.Add(this.lblCancel);
			this.m_fpdFontProvider.SetFontSet(this, "StandardText");
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
			this.MaximizeBox = false; this.MinimizeBox = false; this.Name = "RestoreBackupForm"; this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent; this.Text = "Restore Nexus Mod Manager";
			((System.ComponentModel.ISupportInitialize)(this.tbFile.Properties)).EndInit();
			this.ResumeLayout(false); this.PerformLayout();
		}

		private DevExpress.XtraEditors.SimpleButton btYes;
		private DevExpress.XtraEditors.SimpleButton btNo;
		private DevExpress.XtraEditors.SimpleButton btCancel;
		private DevExpress.XtraEditors.SimpleButton btSelectFile;
		private DevExpress.XtraEditors.LabelControl lblYes;
		private DevExpress.XtraEditors.LabelControl lblCancel;
		private DevExpress.XtraEditors.LabelControl lblNo;
		private DevExpress.XtraEditors.LabelControl lblEstimated;
		private DevExpress.XtraEditors.XtraOpenFileDialog fdFile;
		private DevExpress.XtraEditors.TextEdit tbFile;
	}
}
