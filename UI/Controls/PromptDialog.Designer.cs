namespace Nexus.UI.Controls
{
	partial class PromptDialog
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

		#region Windows Form Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.components = new System.ComponentModel.Container();
			this.tbxPath = new System.Windows.Forms.TextBox();
			this.label1 = new System.Windows.Forms.Label();
			this.butOK = new System.Windows.Forms.Button();
			this.butCancel = new System.Windows.Forms.Button();
			this.erpErrors = new System.Windows.Forms.ErrorProvider(this.components);
			this.cbShared = new System.Windows.Forms.CheckBox();
			this.lbShared = new System.Windows.Forms.Label();
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).BeginInit();
			this.SuspendLayout();
			// 
			// tbxPath
			// 
			this.tbxPath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.tbxPath.Location = new System.Drawing.Point(12, 25);
			this.tbxPath.Name = "tbxPath";
			this.tbxPath.Size = new System.Drawing.Size(318, 20);
			this.tbxPath.TabIndex = 0;
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(12, 9);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(35, 13);
			this.label1.TabIndex = 1;
			this.label1.Text = "label1";
			// 
			// butOK
			// 
			this.butOK.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butOK.Location = new System.Drawing.Point(183, 51);
			this.butOK.Name = "butOK";
			this.butOK.Size = new System.Drawing.Size(75, 23);
			this.butOK.TabIndex = 2;
			this.butOK.Text = "OK";
			this.butOK.UseVisualStyleBackColor = true;
			this.butOK.Click += new System.EventHandler(this.butOK_Click);
			// 
			// butCancel
			// 
			this.butCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this.butCancel.Location = new System.Drawing.Point(264, 51);
			this.butCancel.Name = "butCancel";
			this.butCancel.Size = new System.Drawing.Size(75, 23);
			this.butCancel.TabIndex = 3;
			this.butCancel.Text = "Cancel";
			this.butCancel.UseVisualStyleBackColor = true;
			// 
			// erpErrors
			// 
			this.erpErrors.ContainerControl = this;
			// 
			// cbShared
			// 
			this.cbShared.AutoSize = true;
			this.cbShared.Checked = false;
			this.cbShared.CheckState = System.Windows.Forms.CheckState.Checked;
			this.cbShared.Location = new System.Drawing.Point(15, 52);
			this.cbShared.Name = "cbShared";
			this.cbShared.Size = new System.Drawing.Size(15, 14);
			this.cbShared.TabIndex = 4;
			this.cbShared.UseVisualStyleBackColor = true;
			// 
			// lbShared
			// 
			this.lbShared.AutoSize = true;
			this.lbShared.Location = new System.Drawing.Point(37, 52);
			this.lbShared.Name = "lbShared";
			this.lbShared.Size = new System.Drawing.Size(41, 13);
			this.lbShared.TabIndex = 5;
			this.lbShared.Text = "";
			// 
			// PromptDialog
			// 
			this.AcceptButton = this.butOK;
			this.AutoSize = true;
			this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowOnly;
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.CancelButton = this.butCancel;
			this.ClientSize = new System.Drawing.Size(351, 86);
			this.Controls.Add(this.lbShared);
			this.Controls.Add(this.cbShared);
			this.Controls.Add(this.butCancel);
			this.Controls.Add(this.butOK);
			this.Controls.Add(this.label1);
			this.Controls.Add(this.tbxPath);
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "PromptDialog";
			this.ShowInTaskbar = false;
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		public System.Windows.Forms.TextBox tbxPath;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.Button butOK;
		private System.Windows.Forms.Button butCancel;
		private System.Windows.Forms.ErrorProvider erpErrors;
		public System.Windows.Forms.Label lbShared;
		public System.Windows.Forms.CheckBox cbShared;
	}
}