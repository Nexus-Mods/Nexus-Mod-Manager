namespace Nexus.UI.Controls
{
	partial class FilteredFolderBrowserDialog
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
			this.components = new System.ComponentModel.Container();
			this.panel1 = new System.Windows.Forms.Panel();
			this.ckbRecurse = new System.Windows.Forms.CheckBox();
			this.tbxFileFilter = new System.Windows.Forms.TextBox();
			this.label2 = new System.Windows.Forms.Label();
			this.cbxFilterType = new System.Windows.Forms.ComboBox();
			this.label1 = new System.Windows.Forms.Label();
			this.butNewFolder = new System.Windows.Forms.Button();
			this.butOK = new System.Windows.Forms.Button();
			this.butCancel = new System.Windows.Forms.Button();
			this.pnlDescription = new System.Windows.Forms.Panel();
			this.autosizeLabel1 = new Nexus.UI.Controls.AutosizeLabel();
			this.panel3 = new System.Windows.Forms.Panel();
			this.ftvFileSystem = new Nexus.UI.Controls.FileSystemTreeView();
			this.panel1.SuspendLayout();
			this.pnlDescription.SuspendLayout();
			this.panel3.SuspendLayout();
			this.SuspendLayout();
			// 
			// panel1
			// 
			this.panel1.BackColor = System.Drawing.Color.Transparent;
			this.panel1.Controls.Add(this.ckbRecurse);
			this.panel1.Controls.Add(this.tbxFileFilter);
			this.panel1.Controls.Add(this.label2);
			this.panel1.Controls.Add(this.cbxFilterType);
			this.panel1.Controls.Add(this.label1);
			this.panel1.Controls.Add(this.butNewFolder);
			this.panel1.Controls.Add(this.butOK);
			this.panel1.Controls.Add(this.butCancel);
			this.panel1.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.panel1.Location = new System.Drawing.Point(0, 381);
			this.panel1.Name = "panel1";
			this.panel1.Size = new System.Drawing.Size(387, 100);
			this.panel1.TabIndex = 0;
			// 
			// ckbRecurse
			// 
			this.ckbRecurse.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.ckbRecurse.AutoSize = true;
			this.ckbRecurse.Checked = true;
			this.ckbRecurse.CheckState = System.Windows.Forms.CheckState.Checked;
			this.ckbRecurse.Location = new System.Drawing.Point(262, 39);
			this.ckbRecurse.Name = "ckbRecurse";
			this.ckbRecurse.Size = new System.Drawing.Size(113, 17);
			this.ckbRecurse.TabIndex = 3;
			this.ckbRecurse.Text = "Search Subfolders";
			this.ckbRecurse.UseVisualStyleBackColor = true;
			// 
			// tbxFileFilter
			// 
			this.tbxFileFilter.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.tbxFileFilter.Location = new System.Drawing.Point(69, 13);
			this.tbxFileFilter.Name = "tbxFileFilter";
			this.tbxFileFilter.Size = new System.Drawing.Size(122, 20);
			this.tbxFileFilter.TabIndex = 0;
			// 
			// label2
			// 
			this.label2.AutoSize = true;
			this.label2.Location = new System.Drawing.Point(12, 15);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(51, 13);
			this.label2.TabIndex = 5;
			this.label2.Text = "File Filter:";
			// 
			// cbxFilterType
			// 
			this.cbxFilterType.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.cbxFilterType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.cbxFilterType.FormattingEnabled = true;
			this.cbxFilterType.Location = new System.Drawing.Point(262, 12);
			this.cbxFilterType.Name = "cbxFilterType";
			this.cbxFilterType.Size = new System.Drawing.Size(113, 21);
			this.cbxFilterType.TabIndex = 1;
			// 
			// label1
			// 
			this.label1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(197, 15);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(59, 13);
			this.label1.TabIndex = 3;
			this.label1.Text = "Filter Type:";
			// 
			// butNewFolder
			// 
			this.butNewFolder.Location = new System.Drawing.Point(12, 65);
			this.butNewFolder.Name = "butNewFolder";
			this.butNewFolder.Size = new System.Drawing.Size(101, 23);
			this.butNewFolder.TabIndex = 6;
			this.butNewFolder.Text = "Make New Folder";
			this.butNewFolder.UseVisualStyleBackColor = true;
			this.butNewFolder.Click += new System.EventHandler(this.butNewFolder_Click);
			// 
			// butOK
			// 
			this.butOK.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butOK.DialogResult = System.Windows.Forms.DialogResult.OK;
			this.butOK.Location = new System.Drawing.Point(219, 65);
			this.butOK.Name = "butOK";
			this.butOK.Size = new System.Drawing.Size(75, 23);
			this.butOK.TabIndex = 4;
			this.butOK.Text = "OK";
			this.butOK.UseVisualStyleBackColor = true;
			// 
			// butCancel
			// 
			this.butCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this.butCancel.Location = new System.Drawing.Point(300, 65);
			this.butCancel.Name = "butCancel";
			this.butCancel.Size = new System.Drawing.Size(75, 23);
			this.butCancel.TabIndex = 5;
			this.butCancel.Text = "Cancel";
			this.butCancel.UseVisualStyleBackColor = true;
			// 
			// pnlDescription
			// 
			this.pnlDescription.AutoSize = true;
			this.pnlDescription.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.pnlDescription.BackColor = System.Drawing.SystemColors.Control;
			this.pnlDescription.Controls.Add(this.autosizeLabel1);
			this.pnlDescription.Dock = System.Windows.Forms.DockStyle.Top;
			this.pnlDescription.Location = new System.Drawing.Point(0, 0);
			this.pnlDescription.Name = "pnlDescription";
			this.pnlDescription.Size = new System.Drawing.Size(387, 30);
			this.pnlDescription.TabIndex = 1;
			// 
			// autosizeLabel1
			// 
			this.autosizeLabel1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.autosizeLabel1.BackColor = System.Drawing.SystemColors.Control;
			this.autosizeLabel1.BorderStyle = System.Windows.Forms.BorderStyle.None;
			this.autosizeLabel1.Cursor = System.Windows.Forms.Cursors.Arrow;
			this.autosizeLabel1.Location = new System.Drawing.Point(12, 12);
			this.autosizeLabel1.Margin = new System.Windows.Forms.Padding(0);
			this.autosizeLabel1.Name = "autosizeLabel1";
			this.autosizeLabel1.ReadOnly = true;
			this.autosizeLabel1.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.None;
			this.autosizeLabel1.Size = new System.Drawing.Size(363, 18);
			this.autosizeLabel1.TabIndex = 0;
			this.autosizeLabel1.TabStop = false;
			this.autosizeLabel1.Text = "";
			// 
			// panel3
			// 
			this.panel3.Controls.Add(this.ftvFileSystem);
			this.panel3.Dock = System.Windows.Forms.DockStyle.Fill;
			this.panel3.Location = new System.Drawing.Point(0, 30);
			this.panel3.Name = "panel3";
			this.panel3.Padding = new System.Windows.Forms.Padding(12, 12, 12, 0);
			this.panel3.Size = new System.Drawing.Size(387, 351);
			this.panel3.TabIndex = 2;
			// 
			// ftvFileSystem
			// 
			this.ftvFileSystem.Cursor = System.Windows.Forms.Cursors.Default;
			this.ftvFileSystem.Dock = System.Windows.Forms.DockStyle.Fill;
			this.ftvFileSystem.HideSelection = false;
			this.ftvFileSystem.HotTracking = true;
			this.ftvFileSystem.ImageIndex = 0;
			this.ftvFileSystem.Indent = 19;
			this.ftvFileSystem.ItemHeight = 16;
			this.ftvFileSystem.LabelEdit = true;
			this.ftvFileSystem.Location = new System.Drawing.Point(12, 12);
			this.ftvFileSystem.MultiSelect = false;
			this.ftvFileSystem.Name = "ftvFileSystem";
			this.ftvFileSystem.SelectedImageIndex = 0;
			this.ftvFileSystem.ShowFiles = false;
			this.ftvFileSystem.ShowLines = false;
			this.ftvFileSystem.ShowRootLines = false;
			this.ftvFileSystem.Size = new System.Drawing.Size(363, 339);
			this.ftvFileSystem.Sorted = true;
			this.ftvFileSystem.Sources = new string[0];
			this.ftvFileSystem.TabIndex = 0;
			// 
			// FilteredFolderBrowserDialog
			// 
			this.AcceptButton = this.butOK;
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.CancelButton = this.butCancel;
			this.ClientSize = new System.Drawing.Size(387, 481);
			this.Controls.Add(this.panel3);
			this.Controls.Add(this.pnlDescription);
			this.Controls.Add(this.panel1);
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "FilteredFolderBrowserDialog";
			this.ShowIcon = false;
			this.ShowInTaskbar = false;
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
			this.Text = "Filtered File Selection";
			this.panel1.ResumeLayout(false);
			this.panel1.PerformLayout();
			this.pnlDescription.ResumeLayout(false);
			this.panel3.ResumeLayout(false);
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Panel panel1;
		private System.Windows.Forms.Panel pnlDescription;
		private System.Windows.Forms.Button butCancel;
		private AutosizeLabel autosizeLabel1;
		private System.Windows.Forms.Panel panel3;
		private FileSystemTreeView ftvFileSystem;
		private System.Windows.Forms.Button butNewFolder;
		private System.Windows.Forms.Button butOK;
		private System.Windows.Forms.TextBox tbxFileFilter;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.ComboBox cbxFilterType;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.CheckBox ckbRecurse;
	}
}