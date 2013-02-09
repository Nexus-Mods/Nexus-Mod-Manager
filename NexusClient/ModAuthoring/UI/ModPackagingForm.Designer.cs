namespace Nexus.Client.ModAuthoring.UI
{
	partial class ModPackagingForm
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ModPackagingForm));
			this.verticalTabControl1 = new Nexus.UI.Controls.VerticalTabControl();
			this.vtpModFiles = new Nexus.UI.Controls.VerticalTabPage();
			this.vtpModInfo = new Nexus.UI.Controls.VerticalTabPage();
			this.vtpScript = new Nexus.UI.Controls.VerticalTabPage();
			this.vtpReadme = new Nexus.UI.Controls.VerticalTabPage();
			this.toolStrip1 = new System.Windows.Forms.ToolStrip();
			this.tsbNew = new System.Windows.Forms.ToolStripButton();
			this.tsbSave = new System.Windows.Forms.ToolStripButton();
			this.tsbOpen = new System.Windows.Forms.ToolStripButton();
			this.tsbMakeMod = new System.Windows.Forms.ToolStripButton();
			this.sfdProject = new System.Windows.Forms.SaveFileDialog();
			this.ofdProject = new System.Windows.Forms.OpenFileDialog();
			this.sfdNewMod = new System.Windows.Forms.SaveFileDialog();
			this.sspWarnings = new Nexus.UI.Controls.SiteStatusProvider();
			this.sspErrors = new Nexus.UI.Controls.SiteStatusProvider();
			this.ftvModFilesEditor = new Nexus.Client.ModAuthoring.UI.Controls.ModFilesTreeView();
			this.mieModInfo = new Nexus.Client.ModAuthoring.UI.Controls.ModInfoEditor();
			this.sedScriptEditor = new Nexus.Client.ModAuthoring.UI.Controls.ScriptEditor();
			this.redReadme = new Nexus.Client.ModAuthoring.UI.Controls.ReadmeEditor();
			this.verticalTabControl1.SuspendLayout();
			this.vtpModFiles.SuspendLayout();
			this.vtpModInfo.SuspendLayout();
			this.vtpScript.SuspendLayout();
			this.vtpReadme.SuspendLayout();
			this.toolStrip1.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.sspWarnings)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.sspErrors)).BeginInit();
			this.SuspendLayout();
			// 
			// verticalTabControl1
			// 
			this.verticalTabControl1.BackColor = System.Drawing.SystemColors.Window;
			this.verticalTabControl1.Controls.Add(this.vtpModFiles);
			this.verticalTabControl1.Controls.Add(this.vtpModInfo);
			this.verticalTabControl1.Controls.Add(this.vtpScript);
			this.verticalTabControl1.Controls.Add(this.vtpReadme);
			this.verticalTabControl1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.verticalTabControl1.Location = new System.Drawing.Point(0, 39);
			this.verticalTabControl1.Name = "verticalTabControl1";
			this.verticalTabControl1.SelectedIndex = 0;
			this.verticalTabControl1.SelectedTabPage = this.vtpModFiles;
			this.verticalTabControl1.Size = new System.Drawing.Size(648, 559);
			this.verticalTabControl1.TabIndex = 0;
			this.verticalTabControl1.Text = "verticalTabControl1";
			this.verticalTabControl1.SelectedTabPageChanged += new System.EventHandler<Nexus.UI.Controls.VerticalTabControl.TabPageEventArgs>(this.verticalTabControl1_SelectedTabPageChanged);
			// 
			// vtpModFiles
			// 
			this.vtpModFiles.BackColor = System.Drawing.SystemColors.Control;
			this.vtpModFiles.Controls.Add(this.ftvModFilesEditor);
			this.vtpModFiles.Dock = System.Windows.Forms.DockStyle.Fill;
			this.vtpModFiles.Location = new System.Drawing.Point(150, 0);
			this.vtpModFiles.Name = "vtpModFiles";
			this.vtpModFiles.PageIndex = 0;
			this.vtpModFiles.Size = new System.Drawing.Size(498, 559);
			this.vtpModFiles.TabIndex = 1;
			this.vtpModFiles.Text = "Files";
			// 
			// vtpModInfo
			// 
			this.vtpModInfo.BackColor = System.Drawing.SystemColors.Control;
			this.vtpModInfo.Controls.Add(this.mieModInfo);
			this.vtpModInfo.Dock = System.Windows.Forms.DockStyle.Fill;
			this.vtpModInfo.Location = new System.Drawing.Point(0, 0);
			this.vtpModInfo.Name = "vtpModInfo";
			this.vtpModInfo.PageIndex = 2;
			this.vtpModInfo.Size = new System.Drawing.Size(648, 559);
			this.vtpModInfo.TabIndex = 3;
			this.vtpModInfo.Text = "Mod Info";
			// 
			// vtpScript
			// 
			this.vtpScript.BackColor = System.Drawing.SystemColors.Control;
			this.vtpScript.Controls.Add(this.sedScriptEditor);
			this.vtpScript.Dock = System.Windows.Forms.DockStyle.Fill;
			this.vtpScript.Location = new System.Drawing.Point(0, 0);
			this.vtpScript.Name = "vtpScript";
			this.vtpScript.PageIndex = 1;
			this.vtpScript.Size = new System.Drawing.Size(648, 559);
			this.vtpScript.TabIndex = 2;
			this.vtpScript.Text = "Install Script";
			// 
			// vtpReadme
			// 
			this.vtpReadme.BackColor = System.Drawing.SystemColors.Control;
			this.vtpReadme.Controls.Add(this.redReadme);
			this.vtpReadme.Dock = System.Windows.Forms.DockStyle.Fill;
			this.vtpReadme.Location = new System.Drawing.Point(0, 0);
			this.vtpReadme.Name = "vtpReadme";
			this.vtpReadme.PageIndex = 3;
			this.vtpReadme.Size = new System.Drawing.Size(648, 559);
			this.vtpReadme.TabIndex = 4;
			this.vtpReadme.Text = "Readme";
			// 
			// toolStrip1
			// 
			this.toolStrip1.ImageScalingSize = new System.Drawing.Size(32, 32);
			this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsbNew,
            this.tsbSave,
            this.tsbOpen,
            this.tsbMakeMod});
			this.toolStrip1.Location = new System.Drawing.Point(0, 0);
			this.toolStrip1.Name = "toolStrip1";
			this.toolStrip1.Size = new System.Drawing.Size(648, 39);
			this.toolStrip1.TabIndex = 1;
			this.toolStrip1.Text = "toolStrip1";
			// 
			// tsbNew
			// 
			this.tsbNew.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbNew.Image = global::Nexus.Client.Properties.Resources.document_new_4;
			this.tsbNew.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbNew.Name = "tsbNew";
			this.tsbNew.Size = new System.Drawing.Size(36, 36);
			this.tsbNew.Text = "toolStripButton1";
			// 
			// tsbSave
			// 
			this.tsbSave.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbSave.Image = global::Nexus.Client.Properties.Resources.document_save;
			this.tsbSave.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbSave.Name = "tsbSave";
			this.tsbSave.Size = new System.Drawing.Size(36, 36);
			this.tsbSave.Text = "toolStripButton1";
			// 
			// tsbOpen
			// 
			this.tsbOpen.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbOpen.Image = global::Nexus.Client.Properties.Resources.folders_open;
			this.tsbOpen.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbOpen.Name = "tsbOpen";
			this.tsbOpen.Size = new System.Drawing.Size(36, 36);
			this.tsbOpen.Text = "toolStripButton1";
			// 
			// tsbMakeMod
			// 
			this.tsbMakeMod.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbMakeMod.Image = global::Nexus.Client.Properties.Resources.compilebasic;
			this.tsbMakeMod.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbMakeMod.Name = "tsbMakeMod";
			this.tsbMakeMod.Size = new System.Drawing.Size(36, 36);
			this.tsbMakeMod.Text = "tsbMakeMod";
			// 
			// sfdProject
			// 
			this.sfdProject.DefaultExt = "prj";
			this.sfdProject.Filter = "Mod Packaging Projects | *.prj";
			this.sfdProject.Title = "Save Project";
			// 
			// ofdProject
			// 
			this.ofdProject.DefaultExt = "prj";
			this.ofdProject.Filter = "Mod Packaging Projects | *.prj";
			this.ofdProject.Title = "Open Project";
			// 
			// sfdNewMod
			// 
			this.sfdNewMod.DefaultExt = "nxm";
			this.sfdNewMod.Filter = "Nexus Mod | *.nxm";
			this.sfdNewMod.Title = "New Mod File";
			// 
			// sspWarnings
			// 
			this.sspWarnings.ContainerControl = this;
			this.sspWarnings.Icon = ((System.Drawing.Icon)(resources.GetObject("sspWarnings.Icon")));
			// 
			// sspErrors
			// 
			this.sspErrors.ContainerControl = this;
			// 
			// ftvModFilesEditor
			// 
			this.ftvModFilesEditor.Dock = System.Windows.Forms.DockStyle.Fill;
			this.ftvModFilesEditor.Location = new System.Drawing.Point(0, 0);
			this.ftvModFilesEditor.Name = "ftvModFilesEditor";
			this.ftvModFilesEditor.Size = new System.Drawing.Size(498, 559);
			this.ftvModFilesEditor.TabIndex = 1;
			// 
			// mieModInfo
			// 
			this.mieModInfo.AutoScroll = true;
			this.mieModInfo.Dock = System.Windows.Forms.DockStyle.Fill;
			this.mieModInfo.Location = new System.Drawing.Point(0, 0);
			this.mieModInfo.Name = "mieModInfo";
			this.mieModInfo.Padding = new System.Windows.Forms.Padding(9);
			this.mieModInfo.Size = new System.Drawing.Size(648, 559);
			this.mieModInfo.TabIndex = 0;
			// 
			// sedScriptEditor
			// 
			this.sedScriptEditor.Dock = System.Windows.Forms.DockStyle.Fill;
			this.sedScriptEditor.Location = new System.Drawing.Point(0, 0);
			this.sedScriptEditor.Name = "sedScriptEditor";
			this.sedScriptEditor.Size = new System.Drawing.Size(648, 559);
			this.sedScriptEditor.TabIndex = 0;
			// 
			// redReadme
			// 
			this.redReadme.Dock = System.Windows.Forms.DockStyle.Fill;
			this.redReadme.Location = new System.Drawing.Point(0, 0);
			this.redReadme.Name = "redReadme";
			this.redReadme.Padding = new System.Windows.Forms.Padding(9);
			this.redReadme.Readme = null;
			this.redReadme.Size = new System.Drawing.Size(648, 559);
			this.redReadme.TabIndex = 0;
			// 
			// ModPackagingForm
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(648, 598);
			this.Controls.Add(this.verticalTabControl1);
			this.Controls.Add(this.toolStrip1);
			this.Name = "ModPackagingForm";
			this.Text = "Mod Packager";
			this.verticalTabControl1.ResumeLayout(false);
			this.vtpModFiles.ResumeLayout(false);
			this.vtpModInfo.ResumeLayout(false);
			this.vtpScript.ResumeLayout(false);
			this.vtpReadme.ResumeLayout(false);
			this.toolStrip1.ResumeLayout(false);
			this.toolStrip1.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this.sspWarnings)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.sspErrors)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private Nexus.UI.Controls.VerticalTabControl verticalTabControl1;
		private Nexus.UI.Controls.VerticalTabPage vtpModFiles;
		private Nexus.UI.Controls.VerticalTabPage vtpScript;
		private Nexus.Client.ModAuthoring.UI.Controls.ModFilesTreeView ftvModFilesEditor;
		private Nexus.UI.Controls.VerticalTabPage vtpModInfo;
		private Nexus.Client.ModAuthoring.UI.Controls.ModInfoEditor mieModInfo;
		private Nexus.UI.Controls.VerticalTabPage vtpReadme;
		private Nexus.Client.ModAuthoring.UI.Controls.ReadmeEditor redReadme;
		private System.Windows.Forms.ToolStrip toolStrip1;
		private System.Windows.Forms.ToolStripButton tsbSave;
		private System.Windows.Forms.ToolStripButton tsbNew;
		private System.Windows.Forms.ToolStripButton tsbOpen;
		private System.Windows.Forms.SaveFileDialog sfdProject;
		private System.Windows.Forms.OpenFileDialog ofdProject;
		private System.Windows.Forms.ToolStripButton tsbMakeMod;
		private System.Windows.Forms.SaveFileDialog sfdNewMod;
		private Nexus.Client.ModAuthoring.UI.Controls.ScriptEditor sedScriptEditor;
		private Nexus.UI.Controls.SiteStatusProvider sspWarnings;
		private Nexus.UI.Controls.SiteStatusProvider sspErrors;
	}
}