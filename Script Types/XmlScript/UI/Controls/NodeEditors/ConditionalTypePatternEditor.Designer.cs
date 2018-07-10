namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	partial class ConditionalTypePatternEditor
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
			this.panel3 = new System.Windows.Forms.Panel();
			this.butSave = new System.Windows.Forms.Button();
			this.cbxType = new System.Windows.Forms.ComboBox();
			this.label2 = new System.Windows.Forms.Label();
			this.erpErrors = new System.Windows.Forms.ErrorProvider(this.components);
			this.cndConditionEditor = new Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors.ConditionEditor();
			this.panel3.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).BeginInit();
			this.SuspendLayout();
			// 
			// panel3
			// 
			this.panel3.Controls.Add(this.butSave);
			this.panel3.Controls.Add(this.cbxType);
			this.panel3.Controls.Add(this.label2);
			this.panel3.Dock = System.Windows.Forms.DockStyle.Top;
			this.panel3.Location = new System.Drawing.Point(0, 0);
			this.panel3.Name = "panel3";
			this.panel3.Padding = new System.Windows.Forms.Padding(9);
			this.panel3.Size = new System.Drawing.Size(717, 36);
			this.panel3.TabIndex = 0;
			// 
			// butSave
			// 
			this.butSave.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.erpErrors.SetIconAlignment(this.butSave, System.Windows.Forms.ErrorIconAlignment.MiddleLeft);
			this.butSave.Location = new System.Drawing.Point(630, 10);
			this.butSave.Name = "butSave";
			this.butSave.Size = new System.Drawing.Size(75, 23);
			this.butSave.TabIndex = 1;
			this.butSave.Text = "Save";
			this.butSave.UseVisualStyleBackColor = true;
			this.butSave.Click += new System.EventHandler(this.butSave_Click);
			// 
			// cbxType
			// 
			this.cbxType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.cbxType.FormattingEnabled = true;
			this.cbxType.Location = new System.Drawing.Point(52, 12);
			this.cbxType.Name = "cbxType";
			this.cbxType.Size = new System.Drawing.Size(121, 21);
			this.cbxType.TabIndex = 0;
			// 
			// label2
			// 
			this.label2.AutoSize = true;
			this.label2.Location = new System.Drawing.Point(12, 15);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(34, 13);
			this.label2.TabIndex = 0;
			this.label2.Text = "Type:";
			// 
			// erpErrors
			// 
			this.erpErrors.ContainerControl = this;
			// 
			// cndConditionEditor
			// 
			this.cndConditionEditor.Dock = System.Windows.Forms.DockStyle.Fill;
			this.cndConditionEditor.Location = new System.Drawing.Point(0, 36);
			this.cndConditionEditor.Name = "cndConditionEditor";
			this.cndConditionEditor.Padding = new System.Windows.Forms.Padding(0, 0, 30, 0);
			this.cndConditionEditor.Size = new System.Drawing.Size(717, 401);
			this.cndConditionEditor.TabIndex = 1;
			// 
			// ConditionalTypePatternEditor
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.cndConditionEditor);
			this.Controls.Add(this.panel3);
			this.Name = "ConditionalTypePatternEditor";
			this.Size = new System.Drawing.Size(717, 437);
			this.panel3.ResumeLayout(false);
			this.panel3.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).EndInit();
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.Panel panel3;
		private System.Windows.Forms.ComboBox cbxType;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.Button butSave;
		private System.Windows.Forms.ErrorProvider erpErrors;
		private ConditionEditor cndConditionEditor;
	}
}
