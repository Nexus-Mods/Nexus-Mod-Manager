namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	partial class OptionTypeResolverEditor
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
			this.gbxConditionalType = new System.Windows.Forms.GroupBox();
			this.cteCondionalEditor = new Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors.ConditionalTypeEditor();
			this.gbxSimpleType = new System.Windows.Forms.GroupBox();
			this.cbxSimpleType = new System.Windows.Forms.ComboBox();
			this.label1 = new System.Windows.Forms.Label();
			this.radConditionalType = new System.Windows.Forms.RadioButton();
			this.radStaticType = new System.Windows.Forms.RadioButton();
			this.gbxConditionalType.SuspendLayout();
			this.gbxSimpleType.SuspendLayout();
			this.SuspendLayout();
			// 
			// gbxConditionalType
			// 
			this.gbxConditionalType.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
						| System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.gbxConditionalType.Controls.Add(this.cteCondionalEditor);
			this.gbxConditionalType.Location = new System.Drawing.Point(22, 118);
			this.gbxConditionalType.Name = "gbxConditionalType";
			this.gbxConditionalType.Size = new System.Drawing.Size(647, 449);
			this.gbxConditionalType.TabIndex = 9;
			this.gbxConditionalType.TabStop = false;
			this.gbxConditionalType.Text = "Conditional Type";
			// 
			// cteCondionalEditor
			// 
			this.cteCondionalEditor.Dock = System.Windows.Forms.DockStyle.Fill;
			this.cteCondionalEditor.Location = new System.Drawing.Point(3, 16);
			this.cteCondionalEditor.Name = "cteCondionalEditor";
			this.cteCondionalEditor.Padding = new System.Windows.Forms.Padding(12);
			this.cteCondionalEditor.Size = new System.Drawing.Size(641, 430);
			this.cteCondionalEditor.TabIndex = 0;
			// 
			// gbxSimpleType
			// 
			this.gbxSimpleType.Controls.Add(this.cbxSimpleType);
			this.gbxSimpleType.Controls.Add(this.label1);
			this.gbxSimpleType.Location = new System.Drawing.Point(22, 35);
			this.gbxSimpleType.Name = "gbxSimpleType";
			this.gbxSimpleType.Size = new System.Drawing.Size(342, 54);
			this.gbxSimpleType.TabIndex = 8;
			this.gbxSimpleType.TabStop = false;
			this.gbxSimpleType.Text = "Simple Type";
			// 
			// cbxSimpleType
			// 
			this.cbxSimpleType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.cbxSimpleType.FormattingEnabled = true;
			this.cbxSimpleType.Location = new System.Drawing.Point(52, 19);
			this.cbxSimpleType.Name = "cbxSimpleType";
			this.cbxSimpleType.Size = new System.Drawing.Size(265, 21);
			this.cbxSimpleType.TabIndex = 3;
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(12, 22);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(34, 13);
			this.label1.TabIndex = 2;
			this.label1.Text = "Type:";
			// 
			// radConditionalType
			// 
			this.radConditionalType.AutoSize = true;
			this.radConditionalType.Location = new System.Drawing.Point(12, 95);
			this.radConditionalType.Name = "radConditionalType";
			this.radConditionalType.Size = new System.Drawing.Size(104, 17);
			this.radConditionalType.TabIndex = 7;
			this.radConditionalType.TabStop = true;
			this.radConditionalType.Text = "Conditional Type";
			this.radConditionalType.UseVisualStyleBackColor = true;
			// 
			// radStaticType
			// 
			this.radStaticType.AutoSize = true;
			this.radStaticType.Checked = true;
			this.radStaticType.Location = new System.Drawing.Point(12, 12);
			this.radStaticType.Name = "radStaticType";
			this.radStaticType.Size = new System.Drawing.Size(83, 17);
			this.radStaticType.TabIndex = 6;
			this.radStaticType.TabStop = true;
			this.radStaticType.Text = "Simple Type";
			this.radStaticType.UseVisualStyleBackColor = true;
			this.radStaticType.CheckedChanged += new System.EventHandler(this.radStaticType_CheckedChanged);
			// 
			// OptionTypeResolverEditor
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.gbxConditionalType);
			this.Controls.Add(this.gbxSimpleType);
			this.Controls.Add(this.radConditionalType);
			this.Controls.Add(this.radStaticType);
			this.Name = "OptionTypeResolverEditor";
			this.Padding = new System.Windows.Forms.Padding(9);
			this.Size = new System.Drawing.Size(693, 592);
			this.gbxConditionalType.ResumeLayout(false);
			this.gbxSimpleType.ResumeLayout(false);
			this.gbxSimpleType.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.GroupBox gbxConditionalType;
		private System.Windows.Forms.GroupBox gbxSimpleType;
		private System.Windows.Forms.ComboBox cbxSimpleType;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.RadioButton radConditionalType;
		private System.Windows.Forms.RadioButton radStaticType;
		private ConditionalTypeEditor cteCondionalEditor;
	}
}
