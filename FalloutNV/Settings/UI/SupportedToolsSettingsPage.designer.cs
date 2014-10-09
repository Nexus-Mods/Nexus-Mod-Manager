namespace Nexus.Client.Games.FalloutNV.Settings.UI
{
	partial class SupportedToolsSettingsPage
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
			this.butSelectBOSSDirectory = new System.Windows.Forms.Button();
			this.lblBOSSPrompt = new System.Windows.Forms.Label();
			this.tbxBOSS = new System.Windows.Forms.TextBox();
			this.lblBOSSLabel = new System.Windows.Forms.Label();
			this.label1 = new System.Windows.Forms.Label();
			this.erpErrors = new System.Windows.Forms.ErrorProvider(this.components);
			this.fbdDirectory = new System.Windows.Forms.FolderBrowserDialog();
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).BeginInit();
			this.SuspendLayout();
			// 
			// butSelectBOSSDirectory
			// 
			this.butSelectBOSSDirectory.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.butSelectBOSSDirectory.AutoSize = true;
			this.butSelectBOSSDirectory.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.butSelectBOSSDirectory.Location = new System.Drawing.Point(394, 14);
			this.butSelectBOSSDirectory.Name = "butSelectBOSSDirectory";
			this.butSelectBOSSDirectory.Size = new System.Drawing.Size(26, 23);
			this.butSelectBOSSDirectory.TabIndex = 12;
			this.butSelectBOSSDirectory.Text = "...";
			this.butSelectBOSSDirectory.UseVisualStyleBackColor = true;
			this.butSelectBOSSDirectory.Click += new System.EventHandler(this.butSelectBOSSDirectory_Click);
			// 
			// lblBOSSPrompt
			// 
			this.lblBOSSPrompt.AutoSize = true;
			this.lblBOSSPrompt.Location = new System.Drawing.Point(3, 0);
			this.lblBOSSPrompt.Name = "lblBOSSPrompt";
			this.lblBOSSPrompt.Size = new System.Drawing.Size(222, 13);
			this.lblBOSSPrompt.TabIndex = 14;
			this.lblBOSSPrompt.Text = "Select the directory where BOSS is installed:";
			// 
			// tbxBOSS
			// 
			this.tbxBOSS.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.tbxBOSS.Location = new System.Drawing.Point(130, 16);
			this.tbxBOSS.Name = "tbxBOSS";
			this.tbxBOSS.Size = new System.Drawing.Size(250, 20);
			this.tbxBOSS.TabIndex = 10;
			// 
			// lblBOSSLabel
			// 
			this.lblBOSSLabel.AutoSize = true;
			this.lblBOSSLabel.Location = new System.Drawing.Point(20, 19);
			this.lblBOSSLabel.Name = "lblBOSSLabel";
			this.lblBOSSLabel.Size = new System.Drawing.Size(82, 13);
			this.lblBOSSLabel.TabIndex = 13;
			this.lblBOSSLabel.Text = "BOSS directory:";
			// 
			// erpErrors
			// 
			this.erpErrors.ContainerControl = this;
			// 
			// label1
			// 
			this.label1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(70, 372);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(137, 13);
			this.label1.TabIndex = 5;
			this.label1.Text = "";
			//
			// SupportedToolsSettingsPage
			//
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.label1);
			this.Controls.Add(this.butSelectBOSSDirectory);
			this.Controls.Add(this.lblBOSSPrompt);
			this.Controls.Add(this.tbxBOSS);
			this.Controls.Add(this.lblBOSSLabel);
			this.Name = "SupportedToolsSettingsPage";
			this.Size = new System.Drawing.Size(443, 405);
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Button butSelectBOSSDirectory;
		private System.Windows.Forms.Label lblBOSSPrompt;
		private System.Windows.Forms.TextBox tbxBOSS;
		private System.Windows.Forms.Label lblBOSSLabel;
		private System.Windows.Forms.ErrorProvider erpErrors;
		private System.Windows.Forms.FolderBrowserDialog fbdDirectory;
		private System.Windows.Forms.Label label1;
	}
}
