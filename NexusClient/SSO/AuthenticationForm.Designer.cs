namespace Nexus.Client.SSO
{
	partial class AuthenticationForm
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
            this.panel2 = new System.Windows.Forms.Panel();
            this.buttonCancel = new System.Windows.Forms.Button();
            this.linkLabelManageApiKeys = new System.Windows.Forms.LinkLabel();
            this.buttonSingleSignOn = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // panel2
            // 
            this.panel2.AutoSize = true;
            this.panel2.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.panel2.Dock = System.Windows.Forms.DockStyle.Top;
            this.panel2.Location = new System.Drawing.Point(0, 0);
            this.panel2.Name = "panel2";
            this.panel2.Size = new System.Drawing.Size(253, 0);
            this.panel2.TabIndex = 16;
            // 
            // buttonCancel
            // 
            this.buttonCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.buttonCancel.Location = new System.Drawing.Point(166, 101);
            this.buttonCancel.Margin = new System.Windows.Forms.Padding(3, 3, 3, 8);
            this.buttonCancel.Name = "buttonCancel";
            this.buttonCancel.Size = new System.Drawing.Size(75, 33);
            this.buttonCancel.TabIndex = 3;
            this.buttonCancel.Text = "Cancel";
            this.buttonCancel.UseVisualStyleBackColor = true;
            this.buttonCancel.Click += new System.EventHandler(this.ButtonCancel_Click);
            // 
            // linkLabelManageApiKeys
            // 
            this.linkLabelManageApiKeys.AutoSize = true;
            this.linkLabelManageApiKeys.Location = new System.Drawing.Point(33, 74);
            this.linkLabelManageApiKeys.Name = "linkLabelManageApiKeys";
            this.linkLabelManageApiKeys.Size = new System.Drawing.Size(177, 13);
            this.linkLabelManageApiKeys.TabIndex = 2;
            this.linkLabelManageApiKeys.TabStop = true;
            this.linkLabelManageApiKeys.Text = "Manage your Nexus API key(s) here";
            this.linkLabelManageApiKeys.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.linkLabelManageApiKeys_LinkClicked);
            // 
            // buttonSingleSignOn
            // 
            this.buttonSingleSignOn.Location = new System.Drawing.Point(12, 101);
            this.buttonSingleSignOn.Margin = new System.Windows.Forms.Padding(3, 3, 3, 8);
            this.buttonSingleSignOn.Name = "buttonSingleSignOn";
            this.buttonSingleSignOn.Size = new System.Drawing.Size(91, 33);
            this.buttonSingleSignOn.TabIndex = 0;
            this.buttonSingleSignOn.Text = "Authorize NMM";
            this.buttonSingleSignOn.UseVisualStyleBackColor = true;
            this.buttonSingleSignOn.Click += new System.EventHandler(this.ButtonSingleSignOn_Click);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(6, 9);
            this.label1.MaximumSize = new System.Drawing.Size(245, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(243, 13);
            this.label1.TabIndex = 25;
            this.label1.Text = "User authentication is now handled with API keys.";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(6, 33);
            this.label2.MaximumSize = new System.Drawing.Size(245, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(213, 26);
            this.label2.TabIndex = 26;
            this.label2.Text = "Use the Authorize button below to let NMM access your account details.";
            // 
            // AuthenticationForm
            // 
            this.AcceptButton = this.buttonSingleSignOn;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSize = true;
            this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.CancelButton = this.buttonCancel;
            this.ClientSize = new System.Drawing.Size(253, 151);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.buttonSingleSignOn);
            this.Controls.Add(this.linkLabelManageApiKeys);
            this.Controls.Add(this.buttonCancel);
            this.Controls.Add(this.panel2);
            this.m_fpdFontProvider.SetFontSet(this, "StandardText");
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.MinimumSize = new System.Drawing.Size(262, 39);
            this.Name = "AuthenticationForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Authorization";
            this.ResumeLayout(false);
            this.PerformLayout();

		}

		#endregion
		private System.Windows.Forms.Panel panel2;
        private System.Windows.Forms.Button buttonCancel;
        private System.Windows.Forms.LinkLabel linkLabelManageApiKeys;
        private System.Windows.Forms.Button buttonSingleSignOn;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
    }
}