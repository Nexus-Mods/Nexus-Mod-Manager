namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	partial class InstallStepsEditor
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
			this.rlvSteps = new Nexus.UI.Controls.ReorderableListView();
			this.clmStepName = new System.Windows.Forms.ColumnHeader();
			this.label1 = new System.Windows.Forms.Label();
			this.cbxSortOrder = new System.Windows.Forms.ComboBox();
			this.SuspendLayout();
			// 
			// rlvSteps
			// 
			this.rlvSteps.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
						| System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.rlvSteps.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.clmStepName});
			this.rlvSteps.Location = new System.Drawing.Point(109, 39);
			this.rlvSteps.Name = "rlvSteps";
			this.rlvSteps.Size = new System.Drawing.Size(421, 351);
			this.rlvSteps.TabIndex = 1;
			this.rlvSteps.UseCompatibleStateImageBehavior = false;
			this.rlvSteps.Resize += new System.EventHandler(this.rlvSteps_Resize);
			this.rlvSteps.ItemsReordered += new System.EventHandler<Nexus.UI.Controls.ReorderedItemsEventArgs>(this.rlvSteps_ItemsReordered);
			// 
			// clmStepName
			// 
			this.clmStepName.Text = "Install Step Name";
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(12, 15);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(91, 13);
			this.label1.TabIndex = 1;
			this.label1.Text = "Install Step Order:";
			// 
			// cbxSortOrder
			// 
			this.cbxSortOrder.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.cbxSortOrder.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.cbxSortOrder.FormattingEnabled = true;
			this.cbxSortOrder.Location = new System.Drawing.Point(109, 12);
			this.cbxSortOrder.Name = "cbxSortOrder";
			this.cbxSortOrder.Size = new System.Drawing.Size(421, 21);
			this.cbxSortOrder.TabIndex = 0;
			this.cbxSortOrder.SelectedIndexChanged += new System.EventHandler(this.cbxSortOrder_SelectedIndexChanged);
			// 
			// InstallStepsEditor
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.cbxSortOrder);
			this.Controls.Add(this.label1);
			this.Controls.Add(this.rlvSteps);
			this.Name = "InstallStepsEditor";
			this.Size = new System.Drawing.Size(560, 414);
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private Nexus.UI.Controls.ReorderableListView rlvSteps;
		private System.Windows.Forms.ColumnHeader clmStepName;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.ComboBox cbxSortOrder;
	}
}
