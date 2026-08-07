namespace Nexus.Client.SSO
{
	partial class AuthenticationForm
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
			this.panel2 = new DevExpress.XtraEditors.PanelControl();
			this.buttonCancel = new DevExpress.XtraEditors.SimpleButton();
			this.buttonSingleSignOn = new DevExpress.XtraEditors.SimpleButton();
			this.label1 = new DevExpress.XtraEditors.LabelControl();
			this.label2 = new DevExpress.XtraEditors.LabelControl();
			((System.ComponentModel.ISupportInitialize)(this.panel2)).BeginInit();
			this.SuspendLayout();
			// panel2
			this.panel2.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
			this.panel2.Dock = System.Windows.Forms.DockStyle.Top;
			this.panel2.Location = new System.Drawing.Point(0, 0);
			this.panel2.Name = "panel2";
			this.panel2.Size = new System.Drawing.Size(253, 1);
			this.panel2.TabIndex = 16;
			// buttonCancel
			this.buttonCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this.buttonCancel.Location = new System.Drawing.Point(166, 83);
			this.buttonCancel.Name = "buttonCancel";
			this.buttonCancel.Size = new System.Drawing.Size(75, 33);
			this.buttonCancel.TabIndex = 3;
			this.buttonCancel.Text = "Cancel";
			this.buttonCancel.Click += new System.EventHandler(this.ButtonCancel_Click);
			// buttonSingleSignOn
			this.buttonSingleSignOn.Location = new System.Drawing.Point(12, 83);
			this.buttonSingleSignOn.Name = "buttonSingleSignOn";
			this.buttonSingleSignOn.Size = new System.Drawing.Size(91, 33);
			this.buttonSingleSignOn.TabIndex = 0;
			this.buttonSingleSignOn.Text = "Authorize NMM";
			this.buttonSingleSignOn.Click += new System.EventHandler(this.ButtonSingleSignOn_Click);
			// label1
			this.label1.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
			this.label1.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
			this.label1.Location = new System.Drawing.Point(7, 9);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(239, 18);
			this.label1.TabIndex = 25;
			this.label1.Text = "User authentication is now handled with API keys.";
			// label2
			this.label2.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
			this.label2.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
			this.label2.Location = new System.Drawing.Point(7, 33);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(239, 40);
			this.label2.TabIndex = 26;
			this.label2.Text = "Use the Authorize button below to let NMM access your account details.";
			// AuthenticationForm
			this.AcceptButton = this.buttonSingleSignOn;
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.CancelButton = this.buttonCancel;
			this.ClientSize = new System.Drawing.Size(253, 126);
			this.Controls.Add(this.label2);
			this.Controls.Add(this.label1);
			this.Controls.Add(this.buttonSingleSignOn);
			this.Controls.Add(this.buttonCancel);
			this.Controls.Add(this.panel2);
			this.m_fpdFontProvider.SetFontSet(this, "StandardText");
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
			this.Text = "Authorization";
			((System.ComponentModel.ISupportInitialize)(this.panel2)).EndInit();
			this.ResumeLayout(false);
		}

		private DevExpress.XtraEditors.PanelControl panel2;
		private DevExpress.XtraEditors.SimpleButton buttonCancel;
		private DevExpress.XtraEditors.SimpleButton buttonSingleSignOn;
		private DevExpress.XtraEditors.LabelControl label1;
		private DevExpress.XtraEditors.LabelControl label2;
	}
}
