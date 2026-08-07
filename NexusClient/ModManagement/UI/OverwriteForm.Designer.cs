namespace Nexus.Client.ModManagement.UI
{
	partial class OverwriteForm
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
			this.butYesToAll = new DevExpress.XtraEditors.SimpleButton();
			this.butYesToGroup = new DevExpress.XtraEditors.SimpleButton();
			this.butYesToMod = new DevExpress.XtraEditors.SimpleButton();
			this.butYes = new DevExpress.XtraEditors.SimpleButton();
			this.butNoToAll = new DevExpress.XtraEditors.SimpleButton();
			this.butNoToGroup = new DevExpress.XtraEditors.SimpleButton();
			this.butNoToMod = new DevExpress.XtraEditors.SimpleButton();
			this.butNo = new DevExpress.XtraEditors.SimpleButton();
			this.lblMessage = new DevExpress.XtraEditors.LabelControl();
			this.panel1 = new DevExpress.XtraEditors.PanelControl();
			this.panel2 = new DevExpress.XtraEditors.PanelControl();
			((System.ComponentModel.ISupportInitialize)(this.panel1)).BeginInit(); this.panel1.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.panel2)).BeginInit(); this.panel2.SuspendLayout();
			this.SuspendLayout();
			ConfigureButton(this.butYesToAll, 14, "butYesToAll", "Yes to all");
			ConfigureButton(this.butYesToGroup, 95, "butYesToGroup", "Yes to folder");
			ConfigureButton(this.butYesToMod, 176, "butYesToMod", "Yes to Mod");
			ConfigureButton(this.butYes, 257, "butYes", "Yes");
			ConfigureButton(this.butNoToAll, 338, "butNoToAll", "No to all");
			ConfigureButton(this.butNoToGroup, 419, "butNoToGroup", "No to folder");
			ConfigureButton(this.butNoToMod, 500, "butNoToMod", "No to Mod");
			ConfigureButton(this.butNo, 581, "butNo", "No");
			this.lblMessage.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
			this.lblMessage.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.Vertical;
			this.lblMessage.Location = new System.Drawing.Point(11, 9);
			this.lblMessage.Name = "lblMessage";
			this.lblMessage.Size = new System.Drawing.Size(645, 13);
			this.lblMessage.Text = "label1";
			this.panel1.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
			this.panel1.Controls.AddRange(new System.Windows.Forms.Control[] { this.butYesToAll, this.butYesToGroup, this.butYesToMod, this.butYes, this.butNoToAll, this.butNoToGroup, this.butNoToMod, this.butNo });
			this.panel1.Dock = System.Windows.Forms.DockStyle.Bottom; this.panel1.Location = new System.Drawing.Point(0, 69); this.panel1.Name = "panel1"; this.panel1.Size = new System.Drawing.Size(670, 39);
			this.panel2.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
			this.panel2.Controls.Add(this.lblMessage); this.panel2.Dock = System.Windows.Forms.DockStyle.Fill; this.panel2.Location = new System.Drawing.Point(0, 0); this.panel2.Name = "panel2"; this.panel2.Size = new System.Drawing.Size(670, 69);
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F); this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font; this.ClientSize = new System.Drawing.Size(670, 108); this.ControlBox = false; this.Controls.Add(this.panel2); this.Controls.Add(this.panel1); this.m_fpdFontProvider.SetFontSet(this, "StandardText"); this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog; this.KeyPreview = true; this.MinimumSize = new System.Drawing.Size(670, 108); this.Name = "OverwriteForm"; this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen; this.Text = "Confirm Overwrite";
			((System.ComponentModel.ISupportInitialize)(this.panel1)).EndInit(); this.panel1.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.panel2)).EndInit(); this.panel2.ResumeLayout(false);
			this.ResumeLayout(false);
		}

		/// <summary>Configures one overwrite action button.</summary>
		private void ConfigureButton(DevExpress.XtraEditors.SimpleButton button, int x, string name, string text)
		{
			button.Location = new System.Drawing.Point(x, 4); button.Name = name; button.Size = new System.Drawing.Size(75, 23); button.Text = text; button.Click += new System.EventHandler(this.Button_Click);
		}

		private DevExpress.XtraEditors.SimpleButton butYesToAll;
		private DevExpress.XtraEditors.SimpleButton butYesToGroup;
		private DevExpress.XtraEditors.SimpleButton butYesToMod;
		private DevExpress.XtraEditors.SimpleButton butYes;
		private DevExpress.XtraEditors.SimpleButton butNoToAll;
		private DevExpress.XtraEditors.SimpleButton butNoToGroup;
		private DevExpress.XtraEditors.SimpleButton butNoToMod;
		private DevExpress.XtraEditors.SimpleButton butNo;
		private DevExpress.XtraEditors.LabelControl lblMessage;
		private DevExpress.XtraEditors.PanelControl panel1;
		private DevExpress.XtraEditors.PanelControl panel2;
	}
}
