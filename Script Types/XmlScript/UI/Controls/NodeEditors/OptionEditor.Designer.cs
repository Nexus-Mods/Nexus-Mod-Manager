namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	partial class OptionEditor
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
			this.tclPlugin = new System.Windows.Forms.TabControl();
			this.tpgInfo = new System.Windows.Forms.TabPage();
			this.oieInfo = new Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors.OptionInfoEditor();
			this.tpgFiles = new System.Windows.Forms.TabPage();
			this.fleInstallFiles = new Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors.FileListEditor();
			this.tpgType = new System.Windows.Forms.TabPage();
			this.treTypeResolverEditor = new Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors.OptionTypeResolverEditor();
			this.tclPlugin.SuspendLayout();
			this.tpgInfo.SuspendLayout();
			this.tpgFiles.SuspendLayout();
			this.tpgType.SuspendLayout();
			this.SuspendLayout();
			// 
			// tclPlugin
			// 
			this.tclPlugin.Controls.Add(this.tpgInfo);
			this.tclPlugin.Controls.Add(this.tpgFiles);
			this.tclPlugin.Controls.Add(this.tpgType);
			this.tclPlugin.Dock = System.Windows.Forms.DockStyle.Fill;
			this.tclPlugin.Location = new System.Drawing.Point(12, 12);
			this.tclPlugin.Name = "tclPlugin";
			this.tclPlugin.SelectedIndex = 0;
			this.tclPlugin.Size = new System.Drawing.Size(664, 565);
			this.tclPlugin.TabIndex = 0;
			this.tclPlugin.Deselecting += new System.Windows.Forms.TabControlCancelEventHandler(this.tblPlugin_Deselecting);
			// 
			// tpgInfo
			// 
			this.tpgInfo.Controls.Add(this.oieInfo);
			this.tpgInfo.Location = new System.Drawing.Point(4, 22);
			this.tpgInfo.Name = "tpgInfo";
			this.tpgInfo.Padding = new System.Windows.Forms.Padding(3);
			this.tpgInfo.Size = new System.Drawing.Size(656, 539);
			this.tpgInfo.TabIndex = 0;
			this.tpgInfo.Text = "Info";
			this.tpgInfo.UseVisualStyleBackColor = true;
			// 
			// oieInfo
			// 
			this.oieInfo.BackColor = System.Drawing.Color.Transparent;
			this.oieInfo.Dock = System.Windows.Forms.DockStyle.Fill;
			this.oieInfo.Location = new System.Drawing.Point(3, 3);
			this.oieInfo.Name = "oieInfo";
			this.oieInfo.Size = new System.Drawing.Size(650, 533);
			this.oieInfo.TabIndex = 0;
			// 
			// tpgFiles
			// 
			this.tpgFiles.Controls.Add(this.fleInstallFiles);
			this.tpgFiles.Location = new System.Drawing.Point(4, 22);
			this.tpgFiles.Name = "tpgFiles";
			this.tpgFiles.Padding = new System.Windows.Forms.Padding(3);
			this.tpgFiles.Size = new System.Drawing.Size(656, 539);
			this.tpgFiles.TabIndex = 1;
			this.tpgFiles.Text = "Files to Install";
			this.tpgFiles.UseVisualStyleBackColor = true;
			// 
			// fleInstallFiles
			// 
			this.fleInstallFiles.Dock = System.Windows.Forms.DockStyle.Fill;
			this.fleInstallFiles.Location = new System.Drawing.Point(3, 3);
			this.fleInstallFiles.Name = "fleInstallFiles";
			this.fleInstallFiles.Size = new System.Drawing.Size(650, 533);
			this.fleInstallFiles.TabIndex = 0;
			// 
			// tpgType
			// 
			this.tpgType.Controls.Add(this.treTypeResolverEditor);
			this.tpgType.Location = new System.Drawing.Point(4, 22);
			this.tpgType.Name = "tpgType";
			this.tpgType.Padding = new System.Windows.Forms.Padding(3);
			this.tpgType.Size = new System.Drawing.Size(656, 539);
			this.tpgType.TabIndex = 2;
			this.tpgType.Text = "Type";
			this.tpgType.UseVisualStyleBackColor = true;
			// 
			// treTypeResolverEditor
			// 
			this.treTypeResolverEditor.Dock = System.Windows.Forms.DockStyle.Fill;
			this.treTypeResolverEditor.Location = new System.Drawing.Point(3, 3);
			this.treTypeResolverEditor.Name = "treTypeResolverEditor";
			this.treTypeResolverEditor.Padding = new System.Windows.Forms.Padding(9);
			this.treTypeResolverEditor.Size = new System.Drawing.Size(650, 533);
			this.treTypeResolverEditor.TabIndex = 0;
			// 
			// OptionEditor
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.AutoValidate = System.Windows.Forms.AutoValidate.EnableAllowFocusChange;
			this.Controls.Add(this.tclPlugin);
			this.Name = "OptionEditor";
			this.Padding = new System.Windows.Forms.Padding(12);
			this.Size = new System.Drawing.Size(688, 589);
			this.tclPlugin.ResumeLayout(false);
			this.tpgInfo.ResumeLayout(false);
			this.tpgFiles.ResumeLayout(false);
			this.tpgType.ResumeLayout(false);
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.TabControl tclPlugin;
		private System.Windows.Forms.TabPage tpgInfo;
		private System.Windows.Forms.TabPage tpgFiles;
		private OptionInfoEditor oieInfo;
		private Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors.FileListEditor fleInstallFiles;
		private System.Windows.Forms.TabPage tpgType;
		private OptionTypeResolverEditor treTypeResolverEditor;
	}
}
