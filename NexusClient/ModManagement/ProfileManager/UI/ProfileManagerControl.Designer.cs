namespace Nexus.Client.ModManagement.UI
{
	partial class ProfileManagerControl
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
			DoDispose();
			base.Dispose(disposing);
		}

		/// <summary>
		/// Allows extension of the dispose method.
		/// </summary>
		partial void DoDispose();

		#region Component Designer generated code

		/// <summary> 
		/// Required method for Designer support - do not modify 
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.components = new System.ComponentModel.Container();
			this.sptProfiles = new System.Windows.Forms.SplitContainer();
			this.toolStrip1 = new System.Windows.Forms.ToolStrip();
			this.tsbImport = new System.Windows.Forms.ToolStripButton();
			this.tsbExport = new System.Windows.Forms.ToolStripButton();
			this.lsbProfiles = new System.Windows.Forms.ListBox();
			this.lbProfiles = new System.Windows.Forms.Label();
			this.plwProfiles = new Nexus.Client.UI.Controls.ProfileListView();
			this.ofdChooseProfile = new System.Windows.Forms.OpenFileDialog();
			this.sfdChooseProfile = new System.Windows.Forms.SaveFileDialog();
			this.ListBoxContextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
			((System.ComponentModel.ISupportInitialize)(this.sptProfiles)).BeginInit();
			this.sptProfiles.Panel1.SuspendLayout();
			this.sptProfiles.Panel2.SuspendLayout();
			this.sptProfiles.SuspendLayout();
			this.toolStrip1.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.plwProfiles)).BeginInit();
			this.SuspendLayout();
			// 
			// sptProfiles
			// 
			this.sptProfiles.Dock = System.Windows.Forms.DockStyle.Fill;
			this.sptProfiles.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
			this.sptProfiles.Location = new System.Drawing.Point(0, 0);
			this.sptProfiles.Name = "sptProfiles";
			// 
			// sptProfiles.Panel1
			// 
			this.sptProfiles.Panel1.Controls.Add(this.toolStrip1);
			this.sptProfiles.Panel1.Controls.Add(this.lsbProfiles);
			this.sptProfiles.Panel1.Controls.Add(this.lbProfiles);
			// 
			// sptProfiles.Panel2
			// 
			this.sptProfiles.Panel2.Controls.Add(this.plwProfiles);
			this.sptProfiles.Size = new System.Drawing.Size(657, 453);
			this.sptProfiles.SplitterDistance = 200;
			this.sptProfiles.TabIndex = 1;
			// 
			// toolStrip1
			// 
			this.toolStrip1.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsbImport,
            this.tsbExport});
			this.toolStrip1.Location = new System.Drawing.Point(0, 428);
			this.toolStrip1.Name = "toolStrip1";
			this.toolStrip1.Size = new System.Drawing.Size(200, 25);
			this.toolStrip1.TabIndex = 2;
			this.toolStrip1.Text = "toolStrip1";
			// 
			// tsbImport
			// 
			this.tsbImport.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbImport.Image = global::Nexus.Client.Properties.Resources.document_import_2;
			this.tsbImport.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbImport.Name = "tsbImport";
			this.tsbImport.Size = new System.Drawing.Size(23, 22);
			this.tsbImport.Text = "toolStripButton1";
			this.tsbImport.Click += new System.EventHandler(this.tsbImport_Click);
			// 
			// tsbExport
			// 
			this.tsbExport.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbExport.Image = global::Nexus.Client.Properties.Resources.document_export_4;
			this.tsbExport.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbExport.Name = "tsbExport";
			this.tsbExport.Size = new System.Drawing.Size(23, 22);
			this.tsbExport.Text = "toolStripButton2";
			this.tsbExport.Click += new System.EventHandler(this.tsbExport_Click);
			// 
			// lsbProfiles
			// 
			this.lsbProfiles.FormattingEnabled = true;
			this.lsbProfiles.Location = new System.Drawing.Point(12, 29);
			this.lsbProfiles.Name = "lsbProfiles";
			this.lsbProfiles.Size = new System.Drawing.Size(177, 186);
			this.lsbProfiles.TabIndex = 1;
			this.lsbProfiles.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.lsbProfiles_MouseDoubleClick);
			this.lsbProfiles.MouseDown += new System.Windows.Forms.MouseEventHandler(this.lsbProfiles_MouseDown);
			// 
			// lbProfiles
			// 
			this.lbProfiles.AutoSize = true;
			this.lbProfiles.Location = new System.Drawing.Point(13, 13);
			this.lbProfiles.Name = "lbProfiles";
			this.lbProfiles.Size = new System.Drawing.Size(41, 13);
			this.lbProfiles.TabIndex = 0;
			this.lbProfiles.Text = "Profiles";
			// 
			// plwProfiles
			// 
			this.plwProfiles.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.plwProfiles.Location = new System.Drawing.Point(0, 0);
			this.plwProfiles.Name = "plwProfiles";
			this.plwProfiles.OwnerDraw = true;
			this.plwProfiles.SelectedProfile = null;
			this.plwProfiles.ShowGroups = false;
			this.plwProfiles.ShowMissingMods = false;
			this.plwProfiles.Size = new System.Drawing.Size(450, 450);
			this.plwProfiles.TabIndex = 0;
			this.plwProfiles.UseCompatibleStateImageBehavior = false;
			this.plwProfiles.View = System.Windows.Forms.View.Details;
			this.plwProfiles.VirtualMode = true;
			// 
			// ListBoxContextMenu
			// 
			this.ListBoxContextMenu.Name = "ListBoxContextMenu";
			this.ListBoxContextMenu.Size = new System.Drawing.Size(61, 4);
			// 
			// ProfileManagerControl
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(657, 453);
			this.CloseButton = false;
			this.CloseButtonVisible = false;
			this.Controls.Add(this.sptProfiles);
			this.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.Name = "ProfileManagerControl";
			this.Text = "Profiles";
			this.sptProfiles.Panel1.ResumeLayout(false);
			this.sptProfiles.Panel1.PerformLayout();
			this.sptProfiles.Panel2.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.sptProfiles)).EndInit();
			this.sptProfiles.ResumeLayout(false);
			this.toolStrip1.ResumeLayout(false);
			this.toolStrip1.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this.plwProfiles)).EndInit();
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.SplitContainer sptProfiles;
		private Nexus.Client.UI.Controls.ProfileListView plwProfiles;
		private System.Windows.Forms.ListBox lsbProfiles;
		private System.Windows.Forms.Label lbProfiles;
		private System.Windows.Forms.ToolStrip toolStrip1;
		private System.Windows.Forms.ToolStripButton tsbImport;
		private System.Windows.Forms.ToolStripButton tsbExport;
		private System.Windows.Forms.OpenFileDialog ofdChooseProfile;
		private System.Windows.Forms.SaveFileDialog sfdChooseProfile;
		private System.Windows.Forms.ContextMenuStrip ListBoxContextMenu;
	}
}
