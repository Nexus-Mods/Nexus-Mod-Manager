namespace Nexus.Client.SSO
{
	partial class ManualApiKeyEntryForm
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
			this.label1 = new DevExpress.XtraEditors.LabelControl();
			this.label2 = new DevExpress.XtraEditors.LabelControl();
			this.linkLabelManageApiKeys = new DevExpress.XtraEditors.HyperlinkLabelControl();
			this.textBoxApiKey = new DevExpress.XtraEditors.TextEdit();
			this.label3 = new DevExpress.XtraEditors.LabelControl();
			this.buttonOk = new DevExpress.XtraEditors.SimpleButton();
			this.buttonCancel = new DevExpress.XtraEditors.SimpleButton();
			((System.ComponentModel.ISupportInitialize)(this.textBoxApiKey.Properties)).BeginInit();
			this.SuspendLayout();
			// label1
			this.label1.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
			this.label1.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
			this.label1.Location = new System.Drawing.Point(13, 13);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(220, 31);
			this.label1.Text = "For unknown reasons NMM cannot communicate with the Nexus SSO service.";
			// label2
			this.label2.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
			this.label2.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
			this.label2.Location = new System.Drawing.Point(13, 53);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(220, 58);
			this.label2.Text = "Click the link below to get to the API key management page, where you can manually generate an API key and enter it in the field at the bottom.";
			// linkLabelManageApiKeys
			this.linkLabelManageApiKeys.Location = new System.Drawing.Point(43, 127);
			this.linkLabelManageApiKeys.Name = "linkLabelManageApiKeys";
			this.linkLabelManageApiKeys.Size = new System.Drawing.Size(161, 13);
			this.linkLabelManageApiKeys.Text = "<href=api>API key management</href>";
			this.linkLabelManageApiKeys.HyperlinkClick += new DevExpress.Utils.HyperlinkClickEventHandler(this.LinkLabelManageApiKeys_HyperlinkClick);
			// textBoxApiKey
			this.textBoxApiKey.Location = new System.Drawing.Point(16, 192);
			this.textBoxApiKey.Name = "textBoxApiKey";
			this.textBoxApiKey.Size = new System.Drawing.Size(217, 20);
			// label3
			this.label3.Location = new System.Drawing.Point(16, 176);
			this.label3.Name = "label3";
			this.label3.Size = new System.Drawing.Size(43, 13);
			this.label3.Text = "API key:";
			// buttonOk
			this.buttonOk.Location = new System.Drawing.Point(16, 219);
			this.buttonOk.Name = "buttonOk";
			this.buttonOk.Size = new System.Drawing.Size(75, 23);
			this.buttonOk.Text = "OK";
			this.buttonOk.Click += new System.EventHandler(this.ButtonOk_Click);
			// buttonCancel
			this.buttonCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this.buttonCancel.Location = new System.Drawing.Point(158, 219);
			this.buttonCancel.Name = "buttonCancel";
			this.buttonCancel.Size = new System.Drawing.Size(75, 23);
			this.buttonCancel.Text = "Cancel";
			this.buttonCancel.Click += new System.EventHandler(this.ButtonCancel_Click);
			// ManualApiKeyEntryForm
			this.AcceptButton = this.buttonOk;
			this.CancelButton = this.buttonCancel;
			this.ClientSize = new System.Drawing.Size(248, 250);
			this.Controls.Add(this.buttonCancel);
			this.Controls.Add(this.buttonOk);
			this.Controls.Add(this.label3);
			this.Controls.Add(this.textBoxApiKey);
			this.Controls.Add(this.linkLabelManageApiKeys);
			this.Controls.Add(this.label2);
			this.Controls.Add(this.label1);
			this.m_fpdFontProvider.SetFontSet(this, "StandardText");
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "ManualApiKeyEntryForm";
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
			this.Text = "Manual API Key Entry";
			((System.ComponentModel.ISupportInitialize)(this.textBoxApiKey.Properties)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();
		}

		private DevExpress.XtraEditors.LabelControl label1;
		private DevExpress.XtraEditors.LabelControl label2;
		private DevExpress.XtraEditors.HyperlinkLabelControl linkLabelManageApiKeys;
		private DevExpress.XtraEditors.TextEdit textBoxApiKey;
		private DevExpress.XtraEditors.LabelControl label3;
		private DevExpress.XtraEditors.SimpleButton buttonOk;
		private DevExpress.XtraEditors.SimpleButton buttonCancel;
	}
}
