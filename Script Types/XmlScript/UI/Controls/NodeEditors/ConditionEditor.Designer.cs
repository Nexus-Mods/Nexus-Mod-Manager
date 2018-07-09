namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	partial class ConditionEditor
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
			this.cpePatternEditor = new Nexus.Client.ModManagement.Scripting.XmlScript.CPL.Controls.CPLEditor();
			this.erpErrors = new System.Windows.Forms.ErrorProvider(this.components);
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).BeginInit();
			this.SuspendLayout();
			// 
			// cpePatternEditor
			// 
			this.cpePatternEditor.Dock = System.Windows.Forms.DockStyle.Fill;
			this.erpErrors.SetIconAlignment(this.cpePatternEditor, System.Windows.Forms.ErrorIconAlignment.TopRight);
			this.cpePatternEditor.Location = new System.Drawing.Point(0, 0);
			this.cpePatternEditor.Name = "cpePatternEditor";
			this.cpePatternEditor.Padding = new System.Windows.Forms.Padding(12, 12, 0, 12);
			this.cpePatternEditor.Size = new System.Drawing.Size(642, 483);
			this.cpePatternEditor.TabIndex = 2;
			this.cpePatternEditor.Validated += new System.EventHandler(this.cpePatternEditor_Validated);
			// 
			// erpErrors
			// 
			this.erpErrors.ContainerControl = this;
			// 
			// ConditionEditor
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.cpePatternEditor);
			this.Name = "ConditionEditor";
			this.Padding = new System.Windows.Forms.Padding(0, 0, 30, 0);
			this.Size = new System.Drawing.Size(672, 483);
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).EndInit();
			this.ResumeLayout(false);

		}

		#endregion

		private Nexus.Client.ModManagement.Scripting.XmlScript.CPL.Controls.CPLEditor cpePatternEditor;
		private System.Windows.Forms.ErrorProvider erpErrors;
	}
}
