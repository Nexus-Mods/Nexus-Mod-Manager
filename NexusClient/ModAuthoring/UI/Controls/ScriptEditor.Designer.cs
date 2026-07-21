namespace Nexus.Client.ModAuthoring.UI.Controls
{
	partial class ScriptEditor
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
			this.panel1 = new System.Windows.Forms.Panel();
			this.ckbUseScript = new System.Windows.Forms.CheckBox();
			this.dtcScriptEditors = new Nexus.UI.Controls.DropDownTabControl();
			this.panel1.SuspendLayout();
			this.SuspendLayout();
			// 
			// panel1
			// 
			this.panel1.Controls.Add(this.ckbUseScript);
			this.panel1.Dock = System.Windows.Forms.DockStyle.Top;
			this.panel1.Location = new System.Drawing.Point(0, 0);
			this.panel1.Name = "panel1";
			this.panel1.Size = new System.Drawing.Size(654, 32);
			this.panel1.TabIndex = 0;
			// 
			// ckbUseScript
			// 
			this.ckbUseScript.AutoSize = true;
			this.ckbUseScript.Location = new System.Drawing.Point(13, 12);
			this.ckbUseScript.Name = "ckbUseScript";
			this.ckbUseScript.Size = new System.Drawing.Size(105, 17);
			this.ckbUseScript.TabIndex = 0;
			this.ckbUseScript.Text = "Use Install Script";
			this.ckbUseScript.UseVisualStyleBackColor = true;
			// 
			// dtcScriptEditors
			// 
			this.dtcScriptEditors.BackColor = System.Drawing.SystemColors.Control;
			this.dtcScriptEditors.Dock = System.Windows.Forms.DockStyle.Fill;
			this.dtcScriptEditors.Location = new System.Drawing.Point(0, 32);
			this.dtcScriptEditors.Name = "dtcScriptEditors";
			this.dtcScriptEditors.SelectedIndex = -1;
			this.dtcScriptEditors.SelectedTabPage = null;
			this.dtcScriptEditors.Size = new System.Drawing.Size(654, 529);
			this.dtcScriptEditors.TabIndex = 1;
			this.dtcScriptEditors.TabWidth = 121;
			this.dtcScriptEditors.Text = "Script Type:";
			this.dtcScriptEditors.SelectedIndexChanged += new System.EventHandler(this.dtcScriptEditors_SelectedIndexChanged);
			// 
			// ScriptEditor
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.dtcScriptEditors);
			this.Controls.Add(this.panel1);
			this.Name = "ScriptEditor";
			this.Size = new System.Drawing.Size(654, 561);
			this.panel1.ResumeLayout(false);
			this.panel1.PerformLayout();
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.Panel panel1;
		private System.Windows.Forms.CheckBox ckbUseScript;
		private Nexus.UI.Controls.DropDownTabControl dtcScriptEditors;
	}
}
