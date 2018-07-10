namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI
{
	partial class OptionFormStep
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
			this.sptPlugins = new System.Windows.Forms.SplitContainer();
			this.lvwPlugins = new System.Windows.Forms.ListView();
			this.columnHeader1 = new System.Windows.Forms.ColumnHeader();
			this.sptImage = new System.Windows.Forms.SplitContainer();
			this.groupBox1 = new System.Windows.Forms.GroupBox();
			this.tbxDescription = new System.Windows.Forms.TextBox();
			this.pbxImage = new System.Windows.Forms.PictureBox();
			this.sptPlugins.Panel1.SuspendLayout();
			this.sptPlugins.Panel2.SuspendLayout();
			this.sptPlugins.SuspendLayout();
			this.sptImage.Panel1.SuspendLayout();
			this.sptImage.Panel2.SuspendLayout();
			this.sptImage.SuspendLayout();
			this.groupBox1.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.pbxImage)).BeginInit();
			this.SuspendLayout();
			// 
			// sptPlugins
			// 
			this.sptPlugins.Dock = System.Windows.Forms.DockStyle.Fill;
			this.sptPlugins.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
			this.sptPlugins.Location = new System.Drawing.Point(0, 47);
			this.sptPlugins.Name = "sptPlugins";
			// 
			// sptPlugins.Panel1
			// 
			this.sptPlugins.Panel1.Controls.Add(this.lvwPlugins);
			// 
			// sptPlugins.Panel2
			// 
			this.sptPlugins.Panel2.Controls.Add(this.sptImage);
			this.sptPlugins.Size = new System.Drawing.Size(587, 396);
			this.sptPlugins.SplitterDistance = 195;
			this.sptPlugins.TabIndex = 0;
			this.sptPlugins.TabStop = false;
			// 
			// lvwPlugins
			// 
			this.lvwPlugins.CheckBoxes = true;
			this.lvwPlugins.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnHeader1});
			this.lvwPlugins.Dock = System.Windows.Forms.DockStyle.Fill;
			this.lvwPlugins.FullRowSelect = true;
			this.lvwPlugins.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.None;
			this.lvwPlugins.HideSelection = false;
			this.lvwPlugins.Location = new System.Drawing.Point(0, 0);
			this.lvwPlugins.MultiSelect = false;
			this.lvwPlugins.Name = "lvwPlugins";
			this.lvwPlugins.Size = new System.Drawing.Size(195, 396);
			this.lvwPlugins.TabIndex = 0;
			this.lvwPlugins.UseCompatibleStateImageBehavior = false;
			this.lvwPlugins.View = System.Windows.Forms.View.Details;
			this.lvwPlugins.ItemChecked += new System.Windows.Forms.ItemCheckedEventHandler(this.lvwPlugins_ItemChecked);
			this.lvwPlugins.SelectedIndexChanged += new System.EventHandler(this.lvwPlugins_SelectedIndexChanged);
			this.lvwPlugins.SizeChanged += new System.EventHandler(this.lvwPlugins_SizeChanged);
			this.lvwPlugins.ItemCheck += new System.Windows.Forms.ItemCheckEventHandler(this.lvwPlugins_ItemCheck);
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
			this.sptImage.Size = new System.Drawing.Size(388, 396);
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
			this.groupBox1.Size = new System.Drawing.Size(388, 129);
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
			this.tbxDescription.Size = new System.Drawing.Size(382, 110);
			this.tbxDescription.TabIndex = 0;
			// 
			// pbxImage
			// 
			this.pbxImage.Dock = System.Windows.Forms.DockStyle.Fill;
			this.pbxImage.Location = new System.Drawing.Point(0, 0);
			this.pbxImage.Name = "pbxImage";
			this.pbxImage.Size = new System.Drawing.Size(388, 263);
			this.pbxImage.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
			this.pbxImage.TabIndex = 0;
			this.pbxImage.TabStop = false;
			// 
			// OptionFormStep
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.sptPlugins);
			this.Name = "OptionFormStep";
			this.Size = new System.Drawing.Size(612, 588);
			this.sptPlugins.Panel1.ResumeLayout(false);
			this.sptPlugins.Panel2.ResumeLayout(false);
			this.sptPlugins.ResumeLayout(false);
			this.sptImage.Panel1.ResumeLayout(false);
			this.sptImage.Panel2.ResumeLayout(false);
			this.sptImage.ResumeLayout(false);
			this.groupBox1.ResumeLayout(false);
			this.groupBox1.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this.pbxImage)).EndInit();
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.SplitContainer sptPlugins;
		private System.Windows.Forms.ListView lvwPlugins;
		private System.Windows.Forms.ColumnHeader columnHeader1;
		private System.Windows.Forms.SplitContainer sptImage;
		private System.Windows.Forms.GroupBox groupBox1;
		private System.Windows.Forms.TextBox tbxDescription;
		private System.Windows.Forms.PictureBox pbxImage;
	}
}
