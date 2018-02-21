namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	partial class ConditionallyInstalledFileSetEditor
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
			this.tclConditionalInstall = new System.Windows.Forms.TabControl();
			this.tabPage1 = new System.Windows.Forms.TabPage();
			this.tabPage2 = new System.Windows.Forms.TabPage();
			this.cedCondition = new Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors.ConditionEditor();
			this.fleFiles = new Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors.FileListEditor();
			this.tclConditionalInstall.SuspendLayout();
			this.tabPage1.SuspendLayout();
			this.tabPage2.SuspendLayout();
			this.SuspendLayout();
			// 
			// tclConditionalInstall
			// 
			this.tclConditionalInstall.Controls.Add(this.tabPage1);
			this.tclConditionalInstall.Controls.Add(this.tabPage2);
			this.tclConditionalInstall.Dock = System.Windows.Forms.DockStyle.Fill;
			this.tclConditionalInstall.Location = new System.Drawing.Point(12, 12);
			this.tclConditionalInstall.Name = "tclConditionalInstall";
			this.tclConditionalInstall.SelectedIndex = 0;
			this.tclConditionalInstall.Size = new System.Drawing.Size(599, 517);
			this.tclConditionalInstall.TabIndex = 0;
			// 
			// tabPage1
			// 
			this.tabPage1.Controls.Add(this.cedCondition);
			this.tabPage1.Location = new System.Drawing.Point(4, 22);
			this.tabPage1.Name = "tabPage1";
			this.tabPage1.Padding = new System.Windows.Forms.Padding(3);
			this.tabPage1.Size = new System.Drawing.Size(591, 491);
			this.tabPage1.TabIndex = 0;
			this.tabPage1.Text = "Conditions";
			this.tabPage1.UseVisualStyleBackColor = true;
			// 
			// tabPage2
			// 
			this.tabPage2.Controls.Add(this.fleFiles);
			this.tabPage2.Location = new System.Drawing.Point(4, 22);
			this.tabPage2.Name = "tabPage2";
			this.tabPage2.Padding = new System.Windows.Forms.Padding(3);
			this.tabPage2.Size = new System.Drawing.Size(591, 491);
			this.tabPage2.TabIndex = 1;
			this.tabPage2.Text = "Files";
			this.tabPage2.UseVisualStyleBackColor = true;
			// 
			// cedCondition
			// 
			this.cedCondition.Dock = System.Windows.Forms.DockStyle.Fill;
			this.cedCondition.Location = new System.Drawing.Point(3, 3);
			this.cedCondition.Name = "cedCondition";
			this.cedCondition.Padding = new System.Windows.Forms.Padding(0, 0, 30, 0);
			this.cedCondition.Size = new System.Drawing.Size(585, 485);
			this.cedCondition.TabIndex = 0;
			// 
			// fleFiles
			// 
			this.fleFiles.Dock = System.Windows.Forms.DockStyle.Fill;
			this.fleFiles.Location = new System.Drawing.Point(3, 3);
			this.fleFiles.Name = "fleFiles";
			this.fleFiles.Size = new System.Drawing.Size(585, 485);
			this.fleFiles.TabIndex = 0;
			// 
			// ConditionalFileInstallEditor
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.tclConditionalInstall);
			this.Name = "ConditionalFileInstallEditor";
			this.Padding = new System.Windows.Forms.Padding(12);
			this.Size = new System.Drawing.Size(623, 541);
			this.tclConditionalInstall.ResumeLayout(false);
			this.tabPage1.ResumeLayout(false);
			this.tabPage2.ResumeLayout(false);
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.TabControl tclConditionalInstall;
		private System.Windows.Forms.TabPage tabPage1;
		private System.Windows.Forms.TabPage tabPage2;
		private ConditionEditor cedCondition;
		private FileListEditor fleFiles;
	}
}
