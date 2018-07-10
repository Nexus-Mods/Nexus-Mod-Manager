namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	partial class OptionGroupEditor
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
			this.erpErrors = new System.Windows.Forms.ErrorProvider(this.components);
			this.label1 = new System.Windows.Forms.Label();
			this.label2 = new System.Windows.Forms.Label();
			this.tbxName = new System.Windows.Forms.TextBox();
			this.cbxType = new System.Windows.Forms.ComboBox();
			this.pnlName = new System.Windows.Forms.Panel();
			this.pnlSortOrder = new System.Windows.Forms.Panel();
			this.rlvOptions = new Nexus.UI.Controls.ReorderableListView();
			this.clmOptionName = new System.Windows.Forms.ColumnHeader();
			this.cbxSortOrder = new System.Windows.Forms.ComboBox();
			this.label3 = new System.Windows.Forms.Label();
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).BeginInit();
			this.pnlName.SuspendLayout();
			this.pnlSortOrder.SuspendLayout();
			this.SuspendLayout();
			// 
			// erpErrors
			// 
			this.erpErrors.ContainerControl = this;
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(12, 15);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(70, 13);
			this.label1.TabIndex = 0;
			this.label1.Text = "Group Name:";
			// 
			// label2
			// 
			this.label2.AutoSize = true;
			this.label2.Location = new System.Drawing.Point(48, 41);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(34, 13);
			this.label2.TabIndex = 1;
			this.label2.Text = "Type:";
			// 
			// tbxName
			// 
			this.tbxName.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.tbxName.Location = new System.Drawing.Point(88, 12);
			this.tbxName.Name = "tbxName";
			this.tbxName.Size = new System.Drawing.Size(518, 20);
			this.tbxName.TabIndex = 0;
			this.tbxName.Validated += new System.EventHandler(this.Control_Validated);
			this.tbxName.KeyDown += new System.Windows.Forms.KeyEventHandler(this.tbxName_KeyDown);
			// 
			// cbxType
			// 
			this.cbxType.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.cbxType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.cbxType.FormattingEnabled = true;
			this.cbxType.Location = new System.Drawing.Point(88, 38);
			this.cbxType.Name = "cbxType";
			this.cbxType.Size = new System.Drawing.Size(518, 21);
			this.cbxType.TabIndex = 1;
			this.cbxType.Validated += new System.EventHandler(this.Control_Validated);
			// 
			// pnlName
			// 
			this.pnlName.AutoSize = true;
			this.pnlName.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.pnlName.Controls.Add(this.label1);
			this.pnlName.Controls.Add(this.cbxType);
			this.pnlName.Controls.Add(this.label2);
			this.pnlName.Controls.Add(this.tbxName);
			this.pnlName.Dock = System.Windows.Forms.DockStyle.Top;
			this.pnlName.Location = new System.Drawing.Point(0, 0);
			this.pnlName.Name = "pnlName";
			this.pnlName.Size = new System.Drawing.Size(636, 62);
			this.pnlName.TabIndex = 2;
			// 
			// pnlSortOrder
			// 
			this.pnlSortOrder.Controls.Add(this.rlvOptions);
			this.pnlSortOrder.Controls.Add(this.cbxSortOrder);
			this.pnlSortOrder.Controls.Add(this.label3);
			this.pnlSortOrder.Dock = System.Windows.Forms.DockStyle.Fill;
			this.pnlSortOrder.Location = new System.Drawing.Point(0, 62);
			this.pnlSortOrder.Name = "pnlSortOrder";
			this.pnlSortOrder.Size = new System.Drawing.Size(636, 339);
			this.pnlSortOrder.TabIndex = 3;
			// 
			// rlvOptions
			// 
			this.rlvOptions.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
						| System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.rlvOptions.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.clmOptionName});
			this.rlvOptions.Location = new System.Drawing.Point(88, 30);
			this.rlvOptions.Name = "rlvOptions";
			this.rlvOptions.Size = new System.Drawing.Size(518, 280);
			this.rlvOptions.TabIndex = 2;
			this.rlvOptions.UseCompatibleStateImageBehavior = false;
			this.rlvOptions.Resize += new System.EventHandler(this.rlvOptions_Resize);
			this.rlvOptions.ItemsReordered += new System.EventHandler<Nexus.UI.Controls.ReorderedItemsEventArgs>(this.rlvOptions_ItemsReordered);
			// 
			// clmOptionName
			// 
			this.clmOptionName.Text = "Option Name";
			this.clmOptionName.Width = 123;
			// 
			// cbxSortOrder
			// 
			this.cbxSortOrder.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.cbxSortOrder.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.cbxSortOrder.FormattingEnabled = true;
			this.cbxSortOrder.Location = new System.Drawing.Point(88, 3);
			this.cbxSortOrder.Name = "cbxSortOrder";
			this.cbxSortOrder.Size = new System.Drawing.Size(518, 21);
			this.cbxSortOrder.TabIndex = 1;
			this.cbxSortOrder.SelectedIndexChanged += new System.EventHandler(this.cbxSortOrder_SelectedIndexChanged);
			// 
			// label3
			// 
			this.label3.AutoSize = true;
			this.label3.Location = new System.Drawing.Point(24, 6);
			this.label3.Name = "label3";
			this.label3.Size = new System.Drawing.Size(58, 13);
			this.label3.TabIndex = 0;
			this.label3.Text = "Sort Order:";
			// 
			// OptionGroupEditor
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.AutoValidate = System.Windows.Forms.AutoValidate.EnableAllowFocusChange;
			this.Controls.Add(this.pnlSortOrder);
			this.Controls.Add(this.pnlName);
			this.Name = "OptionGroupEditor";
			this.Size = new System.Drawing.Size(636, 401);
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).EndInit();
			this.pnlName.ResumeLayout(false);
			this.pnlName.PerformLayout();
			this.pnlSortOrder.ResumeLayout(false);
			this.pnlSortOrder.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.ErrorProvider erpErrors;
		private System.Windows.Forms.ComboBox cbxType;
		private System.Windows.Forms.TextBox tbxName;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.Panel pnlSortOrder;
		private System.Windows.Forms.ComboBox cbxSortOrder;
		private System.Windows.Forms.Label label3;
		private System.Windows.Forms.Panel pnlName;
		private Nexus.UI.Controls.ReorderableListView rlvOptions;
		private System.Windows.Forms.ColumnHeader clmOptionName;
	}
}
