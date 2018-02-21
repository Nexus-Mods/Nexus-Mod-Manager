namespace Nexus.Client.ModManagement.Scripting.XmlScript.CPL.Controls
{
	partial class CplVersionConditionEditor
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
			this.pnlEditVersion = new System.Windows.Forms.Panel();
			this.tbxMinimumVersion = new System.Windows.Forms.TextBox();
			this.label5 = new System.Windows.Forms.Label();
			this.label4 = new System.Windows.Forms.Label();
			this.cbxVersionName = new System.Windows.Forms.ComboBox();
			this.erpErrors = new System.Windows.Forms.ErrorProvider(this.components);
			this.pnlEditVersion.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).BeginInit();
			this.SuspendLayout();
			// 
			// pnlEditVersion
			// 
			this.pnlEditVersion.Controls.Add(this.tbxMinimumVersion);
			this.pnlEditVersion.Controls.Add(this.label5);
			this.pnlEditVersion.Controls.Add(this.label4);
			this.pnlEditVersion.Controls.Add(this.cbxVersionName);
			this.pnlEditVersion.Dock = System.Windows.Forms.DockStyle.Fill;
			this.pnlEditVersion.Location = new System.Drawing.Point(20, 0);
			this.pnlEditVersion.Name = "pnlEditVersion";
			this.pnlEditVersion.Size = new System.Drawing.Size(593, 35);
			this.pnlEditVersion.TabIndex = 1;
			// 
			// tbxMinimumVersion
			// 
			this.tbxMinimumVersion.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.tbxMinimumVersion.Location = new System.Drawing.Point(249, 7);
			this.tbxMinimumVersion.Name = "tbxMinimumVersion";
			this.tbxMinimumVersion.Size = new System.Drawing.Size(314, 20);
			this.tbxMinimumVersion.TabIndex = 1;
			// 
			// label5
			// 
			this.label5.AutoSize = true;
			this.label5.Location = new System.Drawing.Point(162, 10);
			this.label5.Name = "label5";
			this.label5.Size = new System.Drawing.Size(81, 13);
			this.label5.TabIndex = 2;
			this.label5.Text = "must be at least";
			// 
			// label4
			// 
			this.label4.AutoSize = true;
			this.label4.Location = new System.Drawing.Point(3, 10);
			this.label4.Name = "label4";
			this.label4.Size = new System.Drawing.Size(26, 13);
			this.label4.TabIndex = 1;
			this.label4.Text = "The";
			// 
			// cbxVersionName
			// 
			this.cbxVersionName.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.cbxVersionName.FormattingEnabled = true;
			this.cbxVersionName.Location = new System.Drawing.Point(35, 7);
			this.cbxVersionName.Name = "cbxVersionName";
			this.cbxVersionName.Size = new System.Drawing.Size(121, 21);
			this.cbxVersionName.TabIndex = 0;
			// 
			// erpErrors
			// 
			this.erpErrors.ContainerControl = this;
			// 
			// CplVersionConditionEditor
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.pnlEditVersion);
			this.Name = "CplVersionConditionEditor";
			this.Size = new System.Drawing.Size(613, 35);
			this.Controls.SetChildIndex(this.pnlEditVersion, 0);
			this.pnlEditVersion.ResumeLayout(false);
			this.pnlEditVersion.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Panel pnlEditVersion;
		private System.Windows.Forms.TextBox tbxMinimumVersion;
		private System.Windows.Forms.Label label5;
		private System.Windows.Forms.Label label4;
		private System.Windows.Forms.ComboBox cbxVersionName;
		private System.Windows.Forms.ErrorProvider erpErrors;
	}
}
