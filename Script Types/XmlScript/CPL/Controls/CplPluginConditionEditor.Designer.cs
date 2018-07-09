namespace Nexus.Client.ModManagement.Scripting.XmlScript.CPL.Controls
{
	partial class CplPluginConditionEditor
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
			this.label1 = new System.Windows.Forms.Label();
			this.pnlEditPlugin = new System.Windows.Forms.Panel();
			this.cbxPluginState = new System.Windows.Forms.ComboBox();
			this.butSelectPlugin = new System.Windows.Forms.Button();
			this.tbxPlugin = new System.Windows.Forms.TextBox();
			this.erpErrors = new System.Windows.Forms.ErrorProvider(this.components);
			this.pnlEditPlugin.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).BeginInit();
			this.SuspendLayout();
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(3, 11);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(64, 13);
			this.label1.TabIndex = 0;
			this.label1.Text = "Plugin Path:";
			// 
			// pnlEditPlugin
			// 
			this.pnlEditPlugin.Controls.Add(this.cbxPluginState);
			this.pnlEditPlugin.Controls.Add(this.butSelectPlugin);
			this.pnlEditPlugin.Controls.Add(this.tbxPlugin);
			this.pnlEditPlugin.Controls.Add(this.label1);
			this.pnlEditPlugin.Dock = System.Windows.Forms.DockStyle.Fill;
			this.pnlEditPlugin.Location = new System.Drawing.Point(20, 0);
			this.pnlEditPlugin.Name = "pnlEditPlugin";
			this.pnlEditPlugin.Size = new System.Drawing.Size(777, 35);
			this.pnlEditPlugin.TabIndex = 4;
			// 
			// cbxPluginState
			// 
			this.cbxPluginState.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.cbxPluginState.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.cbxPluginState.FormattingEnabled = true;
			this.cbxPluginState.Location = new System.Drawing.Point(653, 8);
			this.cbxPluginState.Name = "cbxPluginState";
			this.cbxPluginState.Size = new System.Drawing.Size(121, 21);
			this.cbxPluginState.TabIndex = 2;
			// 
			// butSelectPlugin
			// 
			this.butSelectPlugin.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.butSelectPlugin.AutoSize = true;
			this.butSelectPlugin.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.butSelectPlugin.Location = new System.Drawing.Point(599, 6);
			this.butSelectPlugin.Name = "butSelectPlugin";
			this.butSelectPlugin.Size = new System.Drawing.Size(26, 23);
			this.butSelectPlugin.TabIndex = 1;
			this.butSelectPlugin.Text = "...";
			this.butSelectPlugin.UseVisualStyleBackColor = true;
			this.butSelectPlugin.Click += new System.EventHandler(this.butSelectPlugin_Click);
			// 
			// tbxPlugin
			// 
			this.tbxPlugin.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.tbxPlugin.Location = new System.Drawing.Point(73, 8);
			this.tbxPlugin.Name = "tbxPlugin";
			this.tbxPlugin.Size = new System.Drawing.Size(520, 20);
			this.tbxPlugin.TabIndex = 0;
			// 
			// erpErrors
			// 
			this.erpErrors.ContainerControl = this;
			// 
			// CplPluginConditionEditor
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.pnlEditPlugin);
			this.Name = "CplPluginConditionEditor";
			this.Size = new System.Drawing.Size(797, 35);
			this.Controls.SetChildIndex(this.pnlEditPlugin, 0);
			this.pnlEditPlugin.ResumeLayout(false);
			this.pnlEditPlugin.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.Panel pnlEditPlugin;
		private System.Windows.Forms.ComboBox cbxPluginState;
		private System.Windows.Forms.Button butSelectPlugin;
		private System.Windows.Forms.TextBox tbxPlugin;
		private System.Windows.Forms.ErrorProvider erpErrors;
	}
}
