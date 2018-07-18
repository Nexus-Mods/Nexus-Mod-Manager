namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls
{
	partial class XmlScriptTreeEditor
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
			this.splitContainer1 = new System.Windows.Forms.SplitContainer();
			this.toolStrip1 = new System.Windows.Forms.ToolStrip();
			this.tsbAddInstallStep = new System.Windows.Forms.ToolStripButton();
			this.tsbAddOptionGroup = new System.Windows.Forms.ToolStripButton();
			this.tspAddOption = new System.Windows.Forms.ToolStripButton();
			this.tsbAddConditionallyInstalledFileSet = new System.Windows.Forms.ToolStripButton();
			this.tsbDelete = new System.Windows.Forms.ToolStripButton();
			this.panel1 = new System.Windows.Forms.Panel();
			this.label1 = new System.Windows.Forms.Label();
			this.cbxScriptVersion = new System.Windows.Forms.ComboBox();
			this.stvScript = new Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.XmlScriptTreeView();
			this.splitContainer1.Panel1.SuspendLayout();
			this.splitContainer1.SuspendLayout();
			this.toolStrip1.SuspendLayout();
			this.panel1.SuspendLayout();
			this.SuspendLayout();
			// 
			// splitContainer1
			// 
			this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.splitContainer1.Location = new System.Drawing.Point(0, 82);
			this.splitContainer1.Name = "splitContainer1";
			// 
			// splitContainer1.Panel1
			// 
			this.splitContainer1.Panel1.Controls.Add(this.stvScript);
			this.splitContainer1.Size = new System.Drawing.Size(700, 476);
			this.splitContainer1.SplitterDistance = 233;
			this.splitContainer1.TabIndex = 0;
			// 
			// toolStrip1
			// 
			this.toolStrip1.ImageScalingSize = new System.Drawing.Size(32, 32);
			this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsbAddInstallStep,
            this.tsbAddOptionGroup,
            this.tspAddOption,
            this.tsbAddConditionallyInstalledFileSet,
            this.tsbDelete});
			this.toolStrip1.Location = new System.Drawing.Point(0, 43);
			this.toolStrip1.Name = "toolStrip1";
			this.toolStrip1.Size = new System.Drawing.Size(700, 39);
			this.toolStrip1.TabIndex = 1;
			this.toolStrip1.Text = "toolStrip1";
			// 
			// tsbAddInstallStep
			// 
			this.tsbAddInstallStep.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbAddInstallStep.Image = global::Nexus.Client.ModManagement.Scripting.XmlScript.Properties.Resources.add_install_step;
			this.tsbAddInstallStep.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbAddInstallStep.Name = "tsbAddInstallStep";
			this.tsbAddInstallStep.Size = new System.Drawing.Size(36, 36);
			this.tsbAddInstallStep.Text = "toolStripButton1";
			// 
			// tsbAddOptionGroup
			// 
			this.tsbAddOptionGroup.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbAddOptionGroup.Image = global::Nexus.Client.ModManagement.Scripting.XmlScript.Properties.Resources.add_option_group;
			this.tsbAddOptionGroup.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbAddOptionGroup.Name = "tsbAddOptionGroup";
			this.tsbAddOptionGroup.Size = new System.Drawing.Size(36, 36);
			this.tsbAddOptionGroup.Text = "toolStripButton1";
			// 
			// tspAddOption
			// 
			this.tspAddOption.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tspAddOption.Image = global::Nexus.Client.ModManagement.Scripting.XmlScript.Properties.Resources.add_option;
			this.tspAddOption.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tspAddOption.Name = "tspAddOption";
			this.tspAddOption.Size = new System.Drawing.Size(36, 36);
			this.tspAddOption.Text = "toolStripButton1";
			// 
			// tsbAddConditionallyInstalledFileSet
			// 
			this.tsbAddConditionallyInstalledFileSet.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbAddConditionallyInstalledFileSet.Image = global::Nexus.Client.ModManagement.Scripting.XmlScript.Properties.Resources.add_file_set;
			this.tsbAddConditionallyInstalledFileSet.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbAddConditionallyInstalledFileSet.Name = "tsbAddConditionallyInstalledFileSet";
			this.tsbAddConditionallyInstalledFileSet.Size = new System.Drawing.Size(36, 36);
			this.tsbAddConditionallyInstalledFileSet.Text = "toolStripButton1";
			// 
			// tsbDelete
			// 
			this.tsbDelete.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbDelete.Image = global::Nexus.Client.ModManagement.Scripting.XmlScript.Properties.Resources.delete;
			this.tsbDelete.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbDelete.Name = "tsbDelete";
			this.tsbDelete.Size = new System.Drawing.Size(36, 36);
			this.tsbDelete.Text = "toolStripButton1";
			// 
			// panel1
			// 
			this.panel1.Controls.Add(this.cbxScriptVersion);
			this.panel1.Controls.Add(this.label1);
			this.panel1.Dock = System.Windows.Forms.DockStyle.Top;
			this.panel1.Location = new System.Drawing.Point(0, 0);
			this.panel1.Name = "panel1";
			this.panel1.Size = new System.Drawing.Size(700, 43);
			this.panel1.TabIndex = 2;
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(12, 15);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(100, 13);
			this.label1.TabIndex = 0;
			this.label1.Text = "XML Script Version:";
			// 
			// cbxScriptVersion
			// 
			this.cbxScriptVersion.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.cbxScriptVersion.FormattingEnabled = true;
			this.cbxScriptVersion.Location = new System.Drawing.Point(118, 12);
			this.cbxScriptVersion.Name = "cbxScriptVersion";
			this.cbxScriptVersion.Size = new System.Drawing.Size(121, 21);
			this.cbxScriptVersion.TabIndex = 1;
			this.cbxScriptVersion.SelectedIndexChanged += new System.EventHandler(this.cbxScriptVersion_SelectedIndexChanged);
			// 
			// stvScript
			// 
			this.stvScript.Dock = System.Windows.Forms.DockStyle.Fill;
			this.stvScript.HideSelection = false;
			this.stvScript.Location = new System.Drawing.Point(0, 0);
			this.stvScript.Name = "stvScript";
			this.stvScript.Size = new System.Drawing.Size(233, 476);
			this.stvScript.TabIndex = 0;
			this.stvScript.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.stvScript_AfterSelect);
			this.stvScript.BeforeSelect += new System.Windows.Forms.TreeViewCancelEventHandler(this.stvScript_BeforeSelect);
			// 
			// XmlScriptTreeEditor
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.splitContainer1);
			this.Controls.Add(this.toolStrip1);
			this.Controls.Add(this.panel1);
			this.Name = "XmlScriptTreeEditor";
			this.Size = new System.Drawing.Size(700, 558);
			this.splitContainer1.Panel1.ResumeLayout(false);
			this.splitContainer1.ResumeLayout(false);
			this.toolStrip1.ResumeLayout(false);
			this.toolStrip1.PerformLayout();
			this.panel1.ResumeLayout(false);
			this.panel1.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.SplitContainer splitContainer1;
		private Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.XmlScriptTreeView stvScript;
		private System.Windows.Forms.ToolStrip toolStrip1;
		private System.Windows.Forms.ToolStripButton tsbAddInstallStep;
		private System.Windows.Forms.ToolStripButton tsbAddOptionGroup;
		private System.Windows.Forms.ToolStripButton tspAddOption;
		private System.Windows.Forms.ToolStripButton tsbAddConditionallyInstalledFileSet;
		private System.Windows.Forms.ToolStripButton tsbDelete;
		private System.Windows.Forms.Panel panel1;
		private System.Windows.Forms.ComboBox cbxScriptVersion;
		private System.Windows.Forms.Label label1;
	}
}
