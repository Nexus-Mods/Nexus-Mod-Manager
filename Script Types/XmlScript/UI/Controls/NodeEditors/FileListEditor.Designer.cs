namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	partial class FileListEditor
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FileListEditor));
			this.panel1 = new System.Windows.Forms.Panel();
			this.lvwInstallableFiles = new System.Windows.Forms.ListView();
			this.columnHeader1 = new System.Windows.Forms.ColumnHeader();
			this.columnHeader2 = new System.Windows.Forms.ColumnHeader();
			this.columnHeader3 = new System.Windows.Forms.ColumnHeader();
			this.toolStrip1 = new System.Windows.Forms.ToolStrip();
			this.tsbEdit = new System.Windows.Forms.ToolStripButton();
			this.tsbDelete = new System.Windows.Forms.ToolStripButton();
			this.tsbAdd = new System.Windows.Forms.ToolStripButton();
			this.ifeFileEditor = new InstallableFileEditor();
			this.panel1.SuspendLayout();
			this.toolStrip1.SuspendLayout();
			this.SuspendLayout();
			// 
			// panel1
			// 
			this.panel1.Controls.Add(this.lvwInstallableFiles);
			this.panel1.Controls.Add(this.toolStrip1);
			this.panel1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.panel1.Location = new System.Drawing.Point(0, 132);
			this.panel1.Name = "panel1";
			this.panel1.Padding = new System.Windows.Forms.Padding(12);
			this.panel1.Size = new System.Drawing.Size(480, 192);
			this.panel1.TabIndex = 1;
			// 
			// lvwInstallableFiles
			// 
			this.lvwInstallableFiles.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnHeader1,
            this.columnHeader2,
            this.columnHeader3});
			this.lvwInstallableFiles.Dock = System.Windows.Forms.DockStyle.Fill;
			this.lvwInstallableFiles.FullRowSelect = true;
			this.lvwInstallableFiles.HideSelection = false;
			this.lvwInstallableFiles.Location = new System.Drawing.Point(12, 51);
			this.lvwInstallableFiles.MultiSelect = false;
			this.lvwInstallableFiles.Name = "lvwInstallableFiles";
			this.lvwInstallableFiles.Size = new System.Drawing.Size(456, 129);
			this.lvwInstallableFiles.Sorting = System.Windows.Forms.SortOrder.Ascending;
			this.lvwInstallableFiles.TabIndex = 0;
			this.lvwInstallableFiles.UseCompatibleStateImageBehavior = false;
			this.lvwInstallableFiles.View = System.Windows.Forms.View.Details;
			this.lvwInstallableFiles.Resize += new System.EventHandler(this.lvwInstallableFiles_Resize);
			this.lvwInstallableFiles.SelectedIndexChanged += new System.EventHandler(this.lvwInstallableFiles_SelectedIndexChanged);
			// 
			// columnHeader1
			// 
			this.columnHeader1.Text = "Source";
			this.columnHeader1.Width = 178;
			// 
			// columnHeader2
			// 
			this.columnHeader2.Text = "Destination";
			this.columnHeader2.Width = 206;
			// 
			// columnHeader3
			// 
			this.columnHeader3.Text = "Priority";
			// 
			// toolStrip1
			// 
			this.toolStrip1.ImageScalingSize = new System.Drawing.Size(32, 32);
			this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsbEdit,
            this.tsbDelete,
            this.tsbAdd});
			this.toolStrip1.Location = new System.Drawing.Point(12, 12);
			this.toolStrip1.Name = "toolStrip1";
			this.toolStrip1.Size = new System.Drawing.Size(456, 39);
			this.toolStrip1.TabIndex = 1;
			this.toolStrip1.Text = "toolStrip1";
			// 
			// tsbEdit
			// 
			this.tsbEdit.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbEdit.Image = global::Nexus.Client.ModManagement.Scripting.XmlScript.Properties.Resources.Edit;
			this.tsbEdit.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbEdit.Name = "tsbEdit";
			this.tsbEdit.Size = new System.Drawing.Size(36, 36);
			this.tsbEdit.Text = "toolStripButton2";
			// 
			// tsbDelete
			// 
			this.tsbDelete.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbDelete.Image = global::Nexus.Client.ModManagement.Scripting.XmlScript.Properties.Resources.delete;
			this.tsbDelete.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbDelete.Name = "tsbDelete";
			this.tsbDelete.Size = new System.Drawing.Size(36, 36);
			this.tsbDelete.Text = "toolStripButton1";
			// 
			// tsbAdd
			// 
			this.tsbAdd.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbAdd.Image = global::Nexus.Client.ModManagement.Scripting.XmlScript.Properties.Resources.Add;
			this.tsbAdd.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbAdd.Name = "tsbAdd";
			this.tsbAdd.Size = new System.Drawing.Size(36, 36);
			this.tsbAdd.Text = "toolStripButton1";
			// 
			// installableFileEditor1
			// 
			this.ifeFileEditor.AutoValidate = System.Windows.Forms.AutoValidate.EnableAllowFocusChange;
			this.ifeFileEditor.Dock = System.Windows.Forms.DockStyle.Top;
			this.ifeFileEditor.Location = new System.Drawing.Point(0, 0);
			this.ifeFileEditor.Name = "installableFileEditor1";
			this.ifeFileEditor.Padding = new System.Windows.Forms.Padding(9);
			this.ifeFileEditor.Size = new System.Drawing.Size(480, 132);
			this.ifeFileEditor.TabIndex = 0;
			// 
			// InstallFilesEditor
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.panel1);
			this.Controls.Add(this.ifeFileEditor);
			this.Name = "InstallFilesEditor";
			this.Size = new System.Drawing.Size(480, 324);
			this.panel1.ResumeLayout(false);
			this.panel1.PerformLayout();
			this.toolStrip1.ResumeLayout(false);
			this.toolStrip1.PerformLayout();
			this.ResumeLayout(false);

		}

		#endregion

		private InstallableFileEditor ifeFileEditor;
		private System.Windows.Forms.Panel panel1;
		private System.Windows.Forms.ListView lvwInstallableFiles;
		private System.Windows.Forms.ColumnHeader columnHeader1;
		private System.Windows.Forms.ColumnHeader columnHeader2;
		private System.Windows.Forms.ColumnHeader columnHeader3;
		private System.Windows.Forms.ToolStrip toolStrip1;
		private System.Windows.Forms.ToolStripButton tsbDelete;
		private System.Windows.Forms.ToolStripButton tsbEdit;
		private System.Windows.Forms.ToolStripButton tsbAdd;
	}
}
