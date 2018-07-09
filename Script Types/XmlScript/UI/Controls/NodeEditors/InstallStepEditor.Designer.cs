namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	partial class InstallStepEditor
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
			this.tclInstallStep = new System.Windows.Forms.TabControl();
			this.tpgGeneral = new System.Windows.Forms.TabPage();
			this.pnlSortOrder = new System.Windows.Forms.Panel();
			this.rlvGroups = new Nexus.UI.Controls.ReorderableListView();
			this.clmGroupName = new System.Windows.Forms.ColumnHeader();
			this.cbxSortOrder = new System.Windows.Forms.ComboBox();
			this.label2 = new System.Windows.Forms.Label();
			this.pnlName = new System.Windows.Forms.Panel();
			this.tbxName = new System.Windows.Forms.TextBox();
			this.label1 = new System.Windows.Forms.Label();
			this.tpgVisibility = new System.Windows.Forms.TabPage();
			this.cedVisibilityCondition = new Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors.ConditionEditor();
			this.erpErrors = new System.Windows.Forms.ErrorProvider(this.components);
			this.tclInstallStep.SuspendLayout();
			this.tpgGeneral.SuspendLayout();
			this.pnlSortOrder.SuspendLayout();
			this.pnlName.SuspendLayout();
			this.tpgVisibility.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).BeginInit();
			this.SuspendLayout();
			// 
			// tclInstallStep
			// 
			this.tclInstallStep.Controls.Add(this.tpgGeneral);
			this.tclInstallStep.Controls.Add(this.tpgVisibility);
			this.tclInstallStep.Dock = System.Windows.Forms.DockStyle.Fill;
			this.tclInstallStep.Location = new System.Drawing.Point(12, 12);
			this.tclInstallStep.Name = "tclInstallStep";
			this.tclInstallStep.SelectedIndex = 0;
			this.tclInstallStep.Size = new System.Drawing.Size(519, 480);
			this.tclInstallStep.TabIndex = 0;
			this.tclInstallStep.Deselecting += new System.Windows.Forms.TabControlCancelEventHandler(this.tclInstallStep_Deselecting);
			// 
			// tpgGeneral
			// 
			this.tpgGeneral.Controls.Add(this.pnlSortOrder);
			this.tpgGeneral.Controls.Add(this.pnlName);
			this.tpgGeneral.Location = new System.Drawing.Point(4, 22);
			this.tpgGeneral.Name = "tpgGeneral";
			this.tpgGeneral.Padding = new System.Windows.Forms.Padding(3);
			this.tpgGeneral.Size = new System.Drawing.Size(511, 454);
			this.tpgGeneral.TabIndex = 0;
			this.tpgGeneral.Text = "General";
			this.tpgGeneral.UseVisualStyleBackColor = true;
			// 
			// pnlSortOrder
			// 
			this.pnlSortOrder.Controls.Add(this.rlvGroups);
			this.pnlSortOrder.Controls.Add(this.cbxSortOrder);
			this.pnlSortOrder.Controls.Add(this.label2);
			this.pnlSortOrder.Dock = System.Windows.Forms.DockStyle.Fill;
			this.pnlSortOrder.Location = new System.Drawing.Point(3, 29);
			this.pnlSortOrder.Name = "pnlSortOrder";
			this.pnlSortOrder.Size = new System.Drawing.Size(505, 422);
			this.pnlSortOrder.TabIndex = 1;
			// 
			// rlvGroups
			// 
			this.rlvGroups.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
						| System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.rlvGroups.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.clmGroupName});
			this.rlvGroups.Location = new System.Drawing.Point(72, 30);
			this.rlvGroups.Name = "rlvGroups";
			this.rlvGroups.Size = new System.Drawing.Size(401, 364);
			this.rlvGroups.TabIndex = 1;
			this.rlvGroups.UseCompatibleStateImageBehavior = false;
			this.rlvGroups.Resize += new System.EventHandler(this.rlvGroups_Resize);
			this.rlvGroups.ItemsReordered += new System.EventHandler<Nexus.UI.Controls.ReorderedItemsEventArgs>(this.rlvGroups_ItemsReordered);
			// 
			// clmGroupName
			// 
			this.clmGroupName.Text = "Group Name";
			this.clmGroupName.Width = 123;
			// 
			// cbxSortOrder
			// 
			this.cbxSortOrder.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.cbxSortOrder.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.cbxSortOrder.FormattingEnabled = true;
			this.cbxSortOrder.Location = new System.Drawing.Point(72, 3);
			this.cbxSortOrder.Name = "cbxSortOrder";
			this.cbxSortOrder.Size = new System.Drawing.Size(401, 21);
			this.cbxSortOrder.TabIndex = 0;
			this.cbxSortOrder.SelectedIndexChanged += new System.EventHandler(this.cbxSortOrder_SelectedIndexChanged);
			// 
			// label2
			// 
			this.label2.AutoSize = true;
			this.label2.Location = new System.Drawing.Point(8, 6);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(58, 13);
			this.label2.TabIndex = 0;
			this.label2.Text = "Sort Order:";
			// 
			// pnlName
			// 
			this.pnlName.AutoSize = true;
			this.pnlName.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.pnlName.Controls.Add(this.tbxName);
			this.pnlName.Controls.Add(this.label1);
			this.pnlName.Dock = System.Windows.Forms.DockStyle.Top;
			this.pnlName.Location = new System.Drawing.Point(3, 3);
			this.pnlName.Name = "pnlName";
			this.pnlName.Size = new System.Drawing.Size(505, 26);
			this.pnlName.TabIndex = 0;
			// 
			// tbxName
			// 
			this.tbxName.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.tbxName.Location = new System.Drawing.Point(72, 3);
			this.tbxName.Name = "tbxName";
			this.tbxName.Size = new System.Drawing.Size(401, 20);
			this.tbxName.TabIndex = 0;
			this.tbxName.Validated += new System.EventHandler(this.Control_Validated);
			this.tbxName.KeyDown += new System.Windows.Forms.KeyEventHandler(this.tbxName_KeyDown);
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(3, 6);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(63, 13);
			this.label1.TabIndex = 0;
			this.label1.Text = "Step Name:";
			// 
			// tpgVisibility
			// 
			this.tpgVisibility.Controls.Add(this.cedVisibilityCondition);
			this.tpgVisibility.Location = new System.Drawing.Point(4, 22);
			this.tpgVisibility.Name = "tpgVisibility";
			this.tpgVisibility.Padding = new System.Windows.Forms.Padding(3);
			this.tpgVisibility.Size = new System.Drawing.Size(511, 454);
			this.tpgVisibility.TabIndex = 1;
			this.tpgVisibility.Text = "Visibility";
			this.tpgVisibility.UseVisualStyleBackColor = true;
			// 
			// cedVisibilityCondition
			// 
			this.cedVisibilityCondition.Dock = System.Windows.Forms.DockStyle.Fill;
			this.cedVisibilityCondition.Location = new System.Drawing.Point(3, 3);
			this.cedVisibilityCondition.Name = "cedVisibilityCondition";
			this.cedVisibilityCondition.Padding = new System.Windows.Forms.Padding(0, 0, 30, 0);
			this.cedVisibilityCondition.Size = new System.Drawing.Size(505, 448);
			this.cedVisibilityCondition.TabIndex = 0;
			// 
			// erpErrors
			// 
			this.erpErrors.ContainerControl = this;
			// 
			// InstallStepEditor
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.tclInstallStep);
			this.Name = "InstallStepEditor";
			this.Padding = new System.Windows.Forms.Padding(12);
			this.Size = new System.Drawing.Size(543, 504);
			this.tclInstallStep.ResumeLayout(false);
			this.tpgGeneral.ResumeLayout(false);
			this.tpgGeneral.PerformLayout();
			this.pnlSortOrder.ResumeLayout(false);
			this.pnlSortOrder.PerformLayout();
			this.pnlName.ResumeLayout(false);
			this.pnlName.PerformLayout();
			this.tpgVisibility.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).EndInit();
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.TabControl tclInstallStep;
		private System.Windows.Forms.TabPage tpgGeneral;
		private System.Windows.Forms.TabPage tpgVisibility;
		private System.Windows.Forms.Panel pnlName;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.TextBox tbxName;
		private System.Windows.Forms.Panel pnlSortOrder;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.ComboBox cbxSortOrder;
		private Nexus.UI.Controls.ReorderableListView rlvGroups;
		private System.Windows.Forms.ColumnHeader clmGroupName;
		private System.Windows.Forms.ErrorProvider erpErrors;
		private ConditionEditor cedVisibilityCondition;
	}
}
