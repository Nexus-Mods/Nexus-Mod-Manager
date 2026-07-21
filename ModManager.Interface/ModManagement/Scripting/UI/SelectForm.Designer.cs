namespace Nexus.Client.ModManagement.Scripting.UI
{
	partial class SelectForm
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
			this.sptPlugins = new System.Windows.Forms.SplitContainer();
			this.lvwOptions = new System.Windows.Forms.ListView();
			this.columnHeader1 = new System.Windows.Forms.ColumnHeader();
			this.sptImage = new System.Windows.Forms.SplitContainer();
			this.groupBox1 = new System.Windows.Forms.GroupBox();
			this.tbxDescription = new System.Windows.Forms.TextBox();
			this.pbxImage = new System.Windows.Forms.PictureBox();
			this.panel1 = new System.Windows.Forms.Panel();
			this.butOK = new System.Windows.Forms.Button();
			this.butCancel = new System.Windows.Forms.Button();
			this.sptPlugins.Panel1.SuspendLayout();
			this.sptPlugins.Panel2.SuspendLayout();
			this.sptPlugins.SuspendLayout();
			this.sptImage.Panel1.SuspendLayout();
			this.sptImage.Panel2.SuspendLayout();
			this.sptImage.SuspendLayout();
			this.groupBox1.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.pbxImage)).BeginInit();
			this.panel1.SuspendLayout();
			this.SuspendLayout();
			// 
			// sptPlugins
			// 
			this.sptPlugins.Dock = System.Windows.Forms.DockStyle.Fill;
			this.sptPlugins.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
			this.sptPlugins.Location = new System.Drawing.Point(0, 0);
			this.sptPlugins.Name = "sptPlugins";
			// 
			// sptPlugins.Panel1
			// 
			this.sptPlugins.Panel1.Controls.Add(this.lvwOptions);
			// 
			// sptPlugins.Panel2
			// 
			this.sptPlugins.Panel2.Controls.Add(this.sptImage);
			this.sptPlugins.Size = new System.Drawing.Size(381, 263);
			this.sptPlugins.SplitterDistance = 123;
			this.sptPlugins.TabIndex = 0;
			this.sptPlugins.TabStop = false;
			// 
			// lvwOptions
			// 
			this.lvwOptions.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnHeader1});
			this.lvwOptions.Dock = System.Windows.Forms.DockStyle.Fill;
			this.lvwOptions.FullRowSelect = true;
			this.lvwOptions.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.None;
			this.lvwOptions.HideSelection = false;
			this.lvwOptions.Location = new System.Drawing.Point(0, 0);
			this.lvwOptions.MultiSelect = false;
			this.lvwOptions.Name = "lvwOptions";
			this.lvwOptions.Size = new System.Drawing.Size(123, 263);
			this.lvwOptions.TabIndex = 0;
			this.lvwOptions.UseCompatibleStateImageBehavior = false;
			this.lvwOptions.View = System.Windows.Forms.View.Details;
			this.lvwOptions.SelectedIndexChanged += new System.EventHandler(this.lvwOptions_SelectedIndexChanged);
			this.lvwOptions.SizeChanged += new System.EventHandler(this.lvwOptions_SizeChanged);
			// 
			// columnHeader1
			// 
			this.columnHeader1.Text = "Name";
			// 
			// sptImage
			// 
			this.sptImage.Dock = System.Windows.Forms.DockStyle.Fill;
			this.sptImage.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
			this.sptImage.Location = new System.Drawing.Point(0, 0);
			this.sptImage.Name = "sptImage";
			this.sptImage.Orientation = System.Windows.Forms.Orientation.Horizontal;
			// 
			// sptImage.Panel1
			// 
			this.sptImage.Panel1.Controls.Add(this.groupBox1);
			// 
			// sptImage.Panel2
			// 
			this.sptImage.Panel2.Controls.Add(this.pbxImage);
			this.sptImage.Size = new System.Drawing.Size(254, 263);
			this.sptImage.SplitterDistance = 129;
			this.sptImage.TabIndex = 101;
			this.sptImage.TabStop = false;
			// 
			// groupBox1
			// 
			this.groupBox1.Controls.Add(this.tbxDescription);
			this.groupBox1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.groupBox1.Location = new System.Drawing.Point(0, 0);
			this.groupBox1.Name = "groupBox1";
			this.groupBox1.Size = new System.Drawing.Size(254, 129);
			this.groupBox1.TabIndex = 1;
			this.groupBox1.TabStop = false;
			this.groupBox1.Text = "Description";
			// 
			// tbxDescription
			// 
			this.tbxDescription.BackColor = System.Drawing.SystemColors.Window;
			this.tbxDescription.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
			this.tbxDescription.Dock = System.Windows.Forms.DockStyle.Fill;
			this.tbxDescription.Location = new System.Drawing.Point(3, 16);
			this.tbxDescription.Multiline = true;
			this.tbxDescription.Name = "tbxDescription";
			this.tbxDescription.ReadOnly = true;
			this.tbxDescription.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
			this.tbxDescription.Size = new System.Drawing.Size(248, 110);
			this.tbxDescription.TabIndex = 0;
			// 
			// pbxImage
			// 
			this.pbxImage.Dock = System.Windows.Forms.DockStyle.Fill;
			this.pbxImage.Location = new System.Drawing.Point(0, 0);
			this.pbxImage.Name = "pbxImage";
			this.pbxImage.Size = new System.Drawing.Size(254, 130);
			this.pbxImage.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
			this.pbxImage.TabIndex = 0;
			this.pbxImage.TabStop = false;
			// 
			// panel1
			// 
			this.panel1.Controls.Add(this.butCancel);
			this.panel1.Controls.Add(this.butOK);
			this.panel1.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.panel1.Location = new System.Drawing.Point(0, 263);
			this.panel1.Name = "panel1";
			this.panel1.Size = new System.Drawing.Size(381, 47);
			this.panel1.TabIndex = 1;
			// 
			// butOK
			// 
			this.butOK.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butOK.Location = new System.Drawing.Point(213, 12);
			this.butOK.Name = "butOK";
			this.butOK.Size = new System.Drawing.Size(75, 23);
			this.butOK.TabIndex = 0;
			this.butOK.Text = "OK";
			this.butOK.UseVisualStyleBackColor = true;
			this.butOK.Click += new System.EventHandler(this.butOK_Click);
			// 
			// butCancel
			// 
			this.butCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this.butCancel.Location = new System.Drawing.Point(294, 12);
			this.butCancel.Name = "butCancel";
			this.butCancel.Size = new System.Drawing.Size(75, 23);
			this.butCancel.TabIndex = 1;
			this.butCancel.Text = "Cancel";
			this.butCancel.UseVisualStyleBackColor = true;
			// 
			// SelectForm
			// 
			this.AcceptButton = this.butOK;
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.CancelButton = this.butCancel;
			this.ClientSize = new System.Drawing.Size(381, 310);
			this.Controls.Add(this.sptPlugins);
			this.Controls.Add(this.panel1);
			this.Name = "SelectForm";
			this.ShowInTaskbar = false;
			this.Text = "SelectForm";
			this.sptPlugins.Panel1.ResumeLayout(false);
			this.sptPlugins.Panel2.ResumeLayout(false);
			this.sptPlugins.ResumeLayout(false);
			this.sptImage.Panel1.ResumeLayout(false);
			this.sptImage.Panel2.ResumeLayout(false);
			this.sptImage.ResumeLayout(false);
			this.groupBox1.ResumeLayout(false);
			this.groupBox1.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this.pbxImage)).EndInit();
			this.panel1.ResumeLayout(false);
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.SplitContainer sptPlugins;
		private System.Windows.Forms.ListView lvwOptions;
		private System.Windows.Forms.ColumnHeader columnHeader1;
		private System.Windows.Forms.SplitContainer sptImage;
		private System.Windows.Forms.GroupBox groupBox1;
		private System.Windows.Forms.TextBox tbxDescription;
		private System.Windows.Forms.PictureBox pbxImage;
		private System.Windows.Forms.Panel panel1;
		private System.Windows.Forms.Button butCancel;
		private System.Windows.Forms.Button butOK;
	}
}