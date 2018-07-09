namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	partial class ConditionalTypeEditor
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
			this.label1 = new System.Windows.Forms.Label();
			this.cbxDefaultType = new System.Windows.Forms.ComboBox();
			this.lvwConditionalTypes = new System.Windows.Forms.ListView();
			this.clmType = new System.Windows.Forms.ColumnHeader();
			this.clmCondition = new System.Windows.Forms.ColumnHeader();
			this.panel1 = new System.Windows.Forms.Panel();
			this.toolStrip1 = new System.Windows.Forms.ToolStrip();
			this.tsbEdit = new System.Windows.Forms.ToolStripButton();
			this.tsbDelete = new System.Windows.Forms.ToolStripButton();
			this.tsbAdd = new System.Windows.Forms.ToolStripButton();
			this.splitContainer1 = new System.Windows.Forms.SplitContainer();
			this.cpePatternEditor = new Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors.ConditionalTypePatternEditor();
			this.panel2 = new System.Windows.Forms.Panel();
			this.panel1.SuspendLayout();
			this.toolStrip1.SuspendLayout();
			this.splitContainer1.Panel1.SuspendLayout();
			this.splitContainer1.Panel2.SuspendLayout();
			this.splitContainer1.SuspendLayout();
			this.panel2.SuspendLayout();
			this.SuspendLayout();
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(0, 3);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(71, 13);
			this.label1.TabIndex = 0;
			this.label1.Text = "Default Type:";
			// 
			// cbxDefaultType
			// 
			this.cbxDefaultType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.cbxDefaultType.FormattingEnabled = true;
			this.cbxDefaultType.Location = new System.Drawing.Point(77, 0);
			this.cbxDefaultType.Name = "cbxDefaultType";
			this.cbxDefaultType.Size = new System.Drawing.Size(121, 21);
			this.cbxDefaultType.TabIndex = 1;
			// 
			// lvwConditionalTypes
			// 
			this.lvwConditionalTypes.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.clmType,
            this.clmCondition});
			this.lvwConditionalTypes.Dock = System.Windows.Forms.DockStyle.Fill;
			this.lvwConditionalTypes.FullRowSelect = true;
			this.lvwConditionalTypes.HideSelection = false;
			this.lvwConditionalTypes.Location = new System.Drawing.Point(0, 39);
			this.lvwConditionalTypes.Name = "lvwConditionalTypes";
			this.lvwConditionalTypes.Size = new System.Drawing.Size(777, 165);
			this.lvwConditionalTypes.TabIndex = 2;
			this.lvwConditionalTypes.UseCompatibleStateImageBehavior = false;
			this.lvwConditionalTypes.View = System.Windows.Forms.View.Details;
			this.lvwConditionalTypes.Resize += new System.EventHandler(this.lvwConditionalTypes_Resize);
			this.lvwConditionalTypes.SelectedIndexChanged += new System.EventHandler(this.lvwConditionalTypes_SelectedIndexChanged);
			// 
			// clmType
			// 
			this.clmType.Text = "Type";
			// 
			// clmCondition
			// 
			this.clmCondition.Text = "Condition";
			this.clmCondition.Width = 363;
			// 
			// panel1
			// 
			this.panel1.Controls.Add(this.lvwConditionalTypes);
			this.panel1.Controls.Add(this.toolStrip1);
			this.panel1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.panel1.Location = new System.Drawing.Point(0, 0);
			this.panel1.Name = "panel1";
			this.panel1.Size = new System.Drawing.Size(777, 204);
			this.panel1.TabIndex = 3;
			// 
			// toolStrip1
			// 
			this.toolStrip1.ImageScalingSize = new System.Drawing.Size(32, 32);
			this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsbEdit,
            this.tsbDelete,
            this.tsbAdd});
			this.toolStrip1.Location = new System.Drawing.Point(0, 0);
			this.toolStrip1.Name = "toolStrip1";
			this.toolStrip1.Size = new System.Drawing.Size(777, 39);
			this.toolStrip1.TabIndex = 0;
			this.toolStrip1.Text = "toolStrip1";
			// 
			// tsbEdit
			// 
			this.tsbEdit.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbEdit.Enabled = false;
			this.tsbEdit.Image = global::Nexus.Client.ModManagement.Scripting.XmlScript.Properties.Resources.Edit;
			this.tsbEdit.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbEdit.Name = "tsbEdit";
			this.tsbEdit.Size = new System.Drawing.Size(36, 36);
			this.tsbEdit.Text = "toolStripButton1";
			// 
			// tsbDelete
			// 
			this.tsbDelete.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbDelete.Enabled = false;
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
			// splitContainer1
			// 
			this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.splitContainer1.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
			this.splitContainer1.Location = new System.Drawing.Point(12, 50);
			this.splitContainer1.Name = "splitContainer1";
			this.splitContainer1.Orientation = System.Windows.Forms.Orientation.Horizontal;
			// 
			// splitContainer1.Panel1
			// 
			this.splitContainer1.Panel1.Controls.Add(this.panel1);
			// 
			// splitContainer1.Panel2
			// 
			this.splitContainer1.Panel2.Controls.Add(this.cpePatternEditor);
			this.splitContainer1.Size = new System.Drawing.Size(777, 509);
			this.splitContainer1.SplitterDistance = 204;
			this.splitContainer1.TabIndex = 4;
			// 
			// cpePatternEditor
			// 
			this.cpePatternEditor.Dock = System.Windows.Forms.DockStyle.Fill;
			this.cpePatternEditor.Location = new System.Drawing.Point(0, 0);
			this.cpePatternEditor.Name = "cpePatternEditor";
			this.cpePatternEditor.Size = new System.Drawing.Size(777, 301);
			this.cpePatternEditor.TabIndex = 0;
			// 
			// panel2
			// 
			this.panel2.Controls.Add(this.cbxDefaultType);
			this.panel2.Controls.Add(this.label1);
			this.panel2.Dock = System.Windows.Forms.DockStyle.Top;
			this.panel2.Location = new System.Drawing.Point(12, 12);
			this.panel2.Name = "panel2";
			this.panel2.Size = new System.Drawing.Size(777, 38);
			this.panel2.TabIndex = 5;
			// 
			// ConditionalTypeEditor
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.splitContainer1);
			this.Controls.Add(this.panel2);
			this.Name = "ConditionalTypeEditor";
			this.Padding = new System.Windows.Forms.Padding(12);
			this.Size = new System.Drawing.Size(801, 571);
			this.panel1.ResumeLayout(false);
			this.panel1.PerformLayout();
			this.toolStrip1.ResumeLayout(false);
			this.toolStrip1.PerformLayout();
			this.splitContainer1.Panel1.ResumeLayout(false);
			this.splitContainer1.Panel2.ResumeLayout(false);
			this.splitContainer1.ResumeLayout(false);
			this.panel2.ResumeLayout(false);
			this.panel2.PerformLayout();
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.ComboBox cbxDefaultType;
		private System.Windows.Forms.ListView lvwConditionalTypes;
		private System.Windows.Forms.ColumnHeader clmType;
		private System.Windows.Forms.ColumnHeader clmCondition;
		private System.Windows.Forms.Panel panel1;
		private System.Windows.Forms.ToolStrip toolStrip1;
		private System.Windows.Forms.ToolStripButton tsbEdit;
		private System.Windows.Forms.ToolStripButton tsbDelete;
		private System.Windows.Forms.ToolStripButton tsbAdd;
		private System.Windows.Forms.SplitContainer splitContainer1;
		private System.Windows.Forms.Panel panel2;
		private ConditionalTypePatternEditor cpePatternEditor;
	}
}
