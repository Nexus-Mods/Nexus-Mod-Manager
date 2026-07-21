namespace Nexus.Client.ModAuthoring.Controls
{
	partial class ModFilesTreeView
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
			this.components = new System.ComponentModel.Container();
			this.panel3 = new System.Windows.Forms.Panel();
			this.label1 = new System.Windows.Forms.Label();
			this.ofdFileChooser = new System.Windows.Forms.OpenFileDialog();
			this.fbdFolderChooser = new System.Windows.Forms.FolderBrowserDialog();
			this.toolStrip1 = new System.Windows.Forms.ToolStrip();
			this.tspAddFiles = new System.Windows.Forms.ToolStripButton();
			this.tspAddFilteredFiles = new System.Windows.Forms.ToolStripButton();
			this.tsbAddFolder = new System.Windows.Forms.ToolStripButton();
			this.ftvSource = new Nexus.Client.Controls.VirtualFileSystemTreeView();
			this.panel3.SuspendLayout();
			this.toolStrip1.SuspendLayout();
			this.SuspendLayout();
			// 
			// panel3
			// 
			this.panel3.Controls.Add(this.label1);
			this.panel3.Dock = System.Windows.Forms.DockStyle.Top;
			this.panel3.Location = new System.Drawing.Point(0, 0);
			this.panel3.Name = "panel3";
			this.panel3.Size = new System.Drawing.Size(229, 20);
			this.panel3.TabIndex = 2;
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.label1.Location = new System.Drawing.Point(3, 3);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(77, 13);
			this.label1.TabIndex = 0;
			this.label1.Text = "Source Files";
			// 
			// ofdFileChooser
			// 
			this.ofdFileChooser.Multiselect = true;
			this.ofdFileChooser.RestoreDirectory = true;
			// 
			// fbdFolderChooser
			// 
			this.fbdFolderChooser.RootFolder = System.Environment.SpecialFolder.MyComputer;
			// 
			// toolStrip1
			// 
			this.toolStrip1.ImageScalingSize = new System.Drawing.Size(32, 32);
			this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tspAddFiles,
            this.tspAddFilteredFiles,
            this.tsbAddFolder});
			this.toolStrip1.Location = new System.Drawing.Point(0, 20);
			this.toolStrip1.Name = "toolStrip1";
			this.toolStrip1.Size = new System.Drawing.Size(229, 39);
			this.toolStrip1.TabIndex = 3;
			this.toolStrip1.Text = "toolStrip1";
			// 
			// tspAddFiles
			// 
			this.tspAddFiles.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tspAddFiles.Image = global::Nexus.Client.Properties.Resources.AddFile_48x48;
			this.tspAddFiles.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tspAddFiles.Name = "tspAddFiles";
			this.tspAddFiles.Size = new System.Drawing.Size(36, 36);
			this.tspAddFiles.Text = "Add Files...";
			this.tspAddFiles.Click += new System.EventHandler(this.tspAddFiles_Click);
			// 
			// tspAddFilteredFiles
			// 
			this.tspAddFilteredFiles.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tspAddFilteredFiles.Image = global::Nexus.Client.Properties.Resources.add_filtered_file_48x48;
			this.tspAddFilteredFiles.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tspAddFilteredFiles.Name = "tspAddFilteredFiles";
			this.tspAddFilteredFiles.Size = new System.Drawing.Size(36, 36);
			this.tspAddFilteredFiles.Text = "Add Filtered Files...";
			this.tspAddFilteredFiles.Click += new System.EventHandler(this.tspAddFilteredFiles_Click);
			// 
			// tsbAddFolder
			// 
			this.tsbAddFolder.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbAddFolder.Image = global::Nexus.Client.Properties.Resources.AddFolder_48x48;
			this.tsbAddFolder.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbAddFolder.Name = "tsbAddFolder";
			this.tsbAddFolder.Size = new System.Drawing.Size(36, 36);
			this.tsbAddFolder.Text = "Add Folder...";
			this.tsbAddFolder.Click += new System.EventHandler(this.tsbAddFolder_Click);
			// 
			// ftvSource
			// 
			this.ftvSource.AllowDrop = true;
			this.ftvSource.Cursor = System.Windows.Forms.Cursors.Default;
			this.ftvSource.Dock = System.Windows.Forms.DockStyle.Fill;
			this.ftvSource.HideSelection = false;
			this.ftvSource.ImageIndex = 0;
			this.ftvSource.LabelEdit = true;
			this.ftvSource.Location = new System.Drawing.Point(0, 59);
			this.ftvSource.Name = "ftvSource";
			this.ftvSource.SelectedImageIndex = 0;
			this.ftvSource.Size = new System.Drawing.Size(229, 158);
			this.ftvSource.Sorted = true;
			this.ftvSource.TabIndex = 0;
			// 
			// ModFilesTreeView
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.ftvSource);
			this.Controls.Add(this.toolStrip1);
			this.Controls.Add(this.panel3);
			this.Name = "ModFilesTreeView";
			this.Size = new System.Drawing.Size(229, 217);
			this.panel3.ResumeLayout(false);
			this.panel3.PerformLayout();
			this.toolStrip1.ResumeLayout(false);
			this.toolStrip1.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.Panel panel3;
		private System.Windows.Forms.OpenFileDialog ofdFileChooser;
		private System.Windows.Forms.FolderBrowserDialog fbdFolderChooser;
		private Nexus.Client.Controls.VirtualFileSystemTreeView ftvSource;
		private System.Windows.Forms.ToolStrip toolStrip1;
		private System.Windows.Forms.ToolStripButton tspAddFiles;
		private System.Windows.Forms.ToolStripButton tspAddFilteredFiles;
		private System.Windows.Forms.ToolStripButton tsbAddFolder;
	}
}
