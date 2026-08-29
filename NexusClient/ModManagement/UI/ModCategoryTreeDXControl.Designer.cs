namespace Nexus.Client.ModManagement.UI
{
	partial class ModCategoryTreeDXControl
	{
		private System.ComponentModel.IContainer components = null;

		protected override void Dispose(bool disposing)
		{
			if (disposing && components != null)
				components.Dispose();

			base.Dispose(disposing);
		}

		#region Component Designer generated code

		private void InitializeComponent()
		{
			this.treeList = new DevExpress.XtraTreeList.TreeList();
			((System.ComponentModel.ISupportInitialize)(this.treeList)).BeginInit();
			this.SuspendLayout();
			//
			// treeList
			//
			this.treeList.Dock = System.Windows.Forms.DockStyle.Fill;
			this.treeList.Location = new System.Drawing.Point(0, 0);
			this.treeList.Name = "treeList";
			this.treeList.Size = new System.Drawing.Size(900, 600);
			this.treeList.TabIndex = 0;
			//
			// ModCategoryTreeDXControl
			//
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
			this.Controls.Add(this.treeList);
			this.Name = "ModCategoryTreeDXControl";
			this.Size = new System.Drawing.Size(900, 600);
			this.TabStop = false;
			((System.ComponentModel.ISupportInitialize)(this.treeList)).EndInit();
			this.ResumeLayout(false);
		}

		#endregion

		private DevExpress.XtraTreeList.TreeList treeList;
	}
}
