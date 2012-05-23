namespace Nexus.Client.ModAuthoring.UI.Controls
{
	partial class ReadmeEditor
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
			this.ddtReadme = new Nexus.UI.Controls.DropDownTabControl();
			this.ddpHTML = new Nexus.UI.Controls.DropDownTabPage();
			this.xedReadme = new Nexus.UI.Controls.XmlEditor();
			this.toolStrip1 = new System.Windows.Forms.ToolStrip();
			this.tsbPreview = new System.Windows.Forms.ToolStripButton();
			this.ddpRichText = new Nexus.UI.Controls.DropDownTabPage();
			this.rteReadme = new Nexus.UI.Controls.RichTextEditor();
			this.ddpPlainText = new Nexus.UI.Controls.DropDownTabPage();
			this.tbxReadme = new System.Windows.Forms.TextBox();
			this.ddtReadme.SuspendLayout();
			this.ddpHTML.SuspendLayout();
			this.toolStrip1.SuspendLayout();
			this.ddpRichText.SuspendLayout();
			this.ddpPlainText.SuspendLayout();
			this.SuspendLayout();
			// 
			// ddtReadme
			// 
			this.ddtReadme.BackColor = System.Drawing.SystemColors.Control;
			this.ddtReadme.Controls.Add(this.ddpPlainText);
			this.ddtReadme.Controls.Add(this.ddpHTML);
			this.ddtReadme.Controls.Add(this.ddpRichText);
			this.ddtReadme.Dock = System.Windows.Forms.DockStyle.Fill;
			this.ddtReadme.Location = new System.Drawing.Point(0, 0);
			this.ddtReadme.Name = "ddtReadme";
			this.ddtReadme.SelectedIndex = 2;
			this.ddtReadme.SelectedTabPage = this.ddpPlainText;
			this.ddtReadme.Size = new System.Drawing.Size(387, 266);
			this.ddtReadme.TabIndex = 2;
			this.ddtReadme.TabWidth = 121;
			this.ddtReadme.Text = "Readme Format:";
			// 
			// ddpHTML
			// 
			this.ddpHTML.Controls.Add(this.xedReadme);
			this.ddpHTML.Controls.Add(this.toolStrip1);
			this.ddpHTML.Dock = System.Windows.Forms.DockStyle.Fill;
			this.ddpHTML.Location = new System.Drawing.Point(0, 45);
			this.ddpHTML.Name = "ddpHTML";
			this.ddpHTML.Padding = new System.Windows.Forms.Padding(3);
			this.ddpHTML.PageIndex = 2;
			this.ddpHTML.Size = new System.Drawing.Size(387, 221);
			this.ddpHTML.TabIndex = 3;
			this.ddpHTML.Text = "HTML";
			// 
			// xedReadme
			// 
			this.xedReadme.Dock = System.Windows.Forms.DockStyle.Fill;
			this.xedReadme.IsReadOnly = false;
			this.xedReadme.Location = new System.Drawing.Point(3, 42);
			this.xedReadme.Name = "xedReadme";
			this.xedReadme.Size = new System.Drawing.Size(381, 176);
			this.xedReadme.TabIndex = 0;
			// 
			// toolStrip1
			// 
			this.toolStrip1.ImageScalingSize = new System.Drawing.Size(32, 32);
			this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsbPreview});
			this.toolStrip1.Location = new System.Drawing.Point(3, 3);
			this.toolStrip1.Name = "toolStrip1";
			this.toolStrip1.Size = new System.Drawing.Size(381, 39);
			this.toolStrip1.TabIndex = 1;
			this.toolStrip1.Text = "toolStrip1";
			// 
			// tsbPreview
			// 
			this.tsbPreview.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbPreview.Image = global::Nexus.Client.Properties.Resources.document_preview;
			this.tsbPreview.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbPreview.Name = "tsbPreview";
			this.tsbPreview.Size = new System.Drawing.Size(36, 36);
			this.tsbPreview.Text = "Preview";
			this.tsbPreview.Click += new System.EventHandler(this.tsbPreview_Click);
			// 
			// ddpRichText
			// 
			this.ddpRichText.BackColor = System.Drawing.SystemColors.Control;
			this.ddpRichText.Controls.Add(this.rteReadme);
			this.ddpRichText.Dock = System.Windows.Forms.DockStyle.Fill;
			this.ddpRichText.Location = new System.Drawing.Point(0, 45);
			this.ddpRichText.Name = "ddpRichText";
			this.ddpRichText.Padding = new System.Windows.Forms.Padding(3);
			this.ddpRichText.PageIndex = 1;
			this.ddpRichText.Size = new System.Drawing.Size(387, 221);
			this.ddpRichText.TabIndex = 2;
			this.ddpRichText.Text = "Rich Text";
			// 
			// rteReadme
			// 
			this.rteReadme.Dock = System.Windows.Forms.DockStyle.Fill;
			this.rteReadme.Location = new System.Drawing.Point(3, 3);
			this.rteReadme.Name = "rteReadme";
			this.rteReadme.Rtf = "{\\rtf1\\ansi\\ansicpg1252\\deff0\\deflang4105{\\fonttbl{\\f0\\fnil\\fcharset0 Microsoft S" +
				"ans Serif;}}\r\n\\viewkind4\\uc1\\pard\\f0\\fs17\\par\r\n}\r\n";
			this.rteReadme.Size = new System.Drawing.Size(381, 215);
			this.rteReadme.TabIndex = 0;
			// 
			// ddpPlainText
			// 
			this.ddpPlainText.BackColor = System.Drawing.SystemColors.Control;
			this.ddpPlainText.Controls.Add(this.tbxReadme);
			this.ddpPlainText.Dock = System.Windows.Forms.DockStyle.Fill;
			this.ddpPlainText.Location = new System.Drawing.Point(0, 45);
			this.ddpPlainText.Name = "ddpPlainText";
			this.ddpPlainText.Padding = new System.Windows.Forms.Padding(3);
			this.ddpPlainText.PageIndex = 0;
			this.ddpPlainText.Size = new System.Drawing.Size(387, 221);
			this.ddpPlainText.TabIndex = 1;
			this.ddpPlainText.Text = "Plain Text";
			// 
			// tbxReadme
			// 
			this.tbxReadme.AcceptsReturn = true;
			this.tbxReadme.AcceptsTab = true;
			this.tbxReadme.Dock = System.Windows.Forms.DockStyle.Fill;
			this.tbxReadme.Location = new System.Drawing.Point(3, 3);
			this.tbxReadme.Multiline = true;
			this.tbxReadme.Name = "tbxReadme";
			this.tbxReadme.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
			this.tbxReadme.Size = new System.Drawing.Size(381, 215);
			this.tbxReadme.TabIndex = 0;
			this.tbxReadme.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.tbxReadme_KeyPress);
			// 
			// ReadmeEditor
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.ddtReadme);
			this.Name = "ReadmeEditor";
			this.Size = new System.Drawing.Size(387, 266);
			this.ddtReadme.ResumeLayout(false);
			this.ddpHTML.ResumeLayout(false);
			this.ddpHTML.PerformLayout();
			this.toolStrip1.ResumeLayout(false);
			this.toolStrip1.PerformLayout();
			this.ddpRichText.ResumeLayout(false);
			this.ddpPlainText.ResumeLayout(false);
			this.ddpPlainText.PerformLayout();
			this.ResumeLayout(false);

		}

		#endregion

		private Nexus.UI.Controls.DropDownTabControl ddtReadme;
		private Nexus.UI.Controls.DropDownTabPage ddpPlainText;
		private System.Windows.Forms.TextBox tbxReadme;
		private Nexus.UI.Controls.DropDownTabPage ddpHTML;
		private Nexus.UI.Controls.XmlEditor xedReadme;
		private Nexus.UI.Controls.DropDownTabPage ddpRichText;
		private Nexus.UI.Controls.RichTextEditor rteReadme;
		private System.Windows.Forms.ToolStrip toolStrip1;
		private System.Windows.Forms.ToolStripButton tsbPreview;
	}
}
