namespace Nexus.Client
{
	partial class LoginForm
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
			this.lblPrompt = new System.Windows.Forms.Label();
			this.label2 = new System.Windows.Forms.Label();
			this.label3 = new System.Windows.Forms.Label();
			this.butLogin = new System.Windows.Forms.Button();
			this.butCancel = new System.Windows.Forms.Button();
			this.tbxUsername = new System.Windows.Forms.TextBox();
			this.tbxPassword = new System.Windows.Forms.TextBox();
			this.ckbStayLoggedIn = new System.Windows.Forms.CheckBox();
			this.label4 = new System.Windows.Forms.Label();
			this.panel1 = new System.Windows.Forms.Panel();
			this.panel2 = new System.Windows.Forms.Panel();
			this.lblError = new Nexus.UI.Controls.AutosizeLabel();
			this.panel3 = new System.Windows.Forms.Panel();
			this.panel1.SuspendLayout();
			this.panel2.SuspendLayout();
			this.panel3.SuspendLayout();
			this.SuspendLayout();
			// 
			// lblPrompt
			// 
			this.lblPrompt.AutoSize = true;
			this.lblPrompt.Location = new System.Drawing.Point(12, 9);
			this.lblPrompt.Name = "lblPrompt";
			this.lblPrompt.Size = new System.Drawing.Size(181, 13);
			this.lblPrompt.TabIndex = 0;
			this.lblPrompt.Text = "You must login to the Nexus website.";
			// 
			// label2
			// 
			this.label2.AutoSize = true;
			this.label2.Location = new System.Drawing.Point(12, 9);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(58, 13);
			this.label2.TabIndex = 1;
			this.label2.Text = "Username:";
			// 
			// label3
			// 
			this.label3.AutoSize = true;
			this.label3.Location = new System.Drawing.Point(14, 35);
			this.label3.Name = "label3";
			this.label3.Size = new System.Drawing.Size(56, 13);
			this.label3.TabIndex = 2;
			this.label3.Text = "Password:";
			// 
			// butLogin
			// 
			this.butLogin.Location = new System.Drawing.Point(88, 113);
			this.butLogin.Name = "butLogin";
			this.butLogin.Size = new System.Drawing.Size(75, 23);
			this.butLogin.TabIndex = 3;
			this.butLogin.Text = "Login";
			this.butLogin.UseVisualStyleBackColor = true;
			this.butLogin.Click += new System.EventHandler(this.butLogin_Click);
			// 
			// butCancel
			// 
			this.butCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this.butCancel.Location = new System.Drawing.Point(169, 113);
			this.butCancel.Name = "butCancel";
			this.butCancel.Size = new System.Drawing.Size(75, 23);
			this.butCancel.TabIndex = 4;
			this.butCancel.Text = "Cancel";
			this.butCancel.UseVisualStyleBackColor = true;
			this.butCancel.Click += new System.EventHandler(this.butCancel_Click);
			// 
			// tbxUsername
			// 
			this.tbxUsername.Location = new System.Drawing.Point(76, 6);
			this.tbxUsername.Name = "tbxUsername";
			this.tbxUsername.Size = new System.Drawing.Size(168, 20);
			this.tbxUsername.TabIndex = 0;
			// 
			// tbxPassword
			// 
			this.tbxPassword.Location = new System.Drawing.Point(76, 32);
			this.tbxPassword.Name = "tbxPassword";
			this.tbxPassword.Size = new System.Drawing.Size(168, 20);
			this.tbxPassword.TabIndex = 1;
			this.tbxPassword.UseSystemPasswordChar = true;
			// 
			// ckbStayLoggedIn
			// 
			this.ckbStayLoggedIn.AutoSize = true;
			this.ckbStayLoggedIn.Location = new System.Drawing.Point(76, 58);
			this.ckbStayLoggedIn.Name = "ckbStayLoggedIn";
			this.ckbStayLoggedIn.Size = new System.Drawing.Size(98, 17);
			this.ckbStayLoggedIn.TabIndex = 2;
			this.ckbStayLoggedIn.Text = "Stay Logged In";
			this.ckbStayLoggedIn.UseVisualStyleBackColor = true;
			// 
			// label4
			// 
			this.label4.Location = new System.Drawing.Point(73, 78);
			this.label4.Name = "label4";
			this.label4.Size = new System.Drawing.Size(171, 26);
			this.label4.TabIndex = 8;
			this.label4.Text = "Staying logged in will NOT store your password.";
			// 
			// panel1
			// 
			this.panel1.Controls.Add(this.lblPrompt);
			this.panel1.Dock = System.Windows.Forms.DockStyle.Top;
			this.panel1.Location = new System.Drawing.Point(0, 0);
			this.panel1.Name = "panel1";
			this.panel1.Size = new System.Drawing.Size(256, 23);
			this.panel1.TabIndex = 10;
			// 
			// panel2
			// 
			this.panel2.AutoSize = true;
			this.panel2.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.panel2.Controls.Add(this.lblError);
			this.panel2.Dock = System.Windows.Forms.DockStyle.Top;
			this.panel2.Location = new System.Drawing.Point(0, 23);
			this.panel2.Name = "panel2";
			this.panel2.Size = new System.Drawing.Size(256, 24);
			this.panel2.TabIndex = 16;
			// 
			// lblError
			// 
			this.lblError.AllowSelection = true;
			this.lblError.AutoScroll = true;
			this.lblError.BackColor = System.Drawing.SystemColors.Control;
			this.lblError.Cursor = System.Windows.Forms.Cursors.IBeam;
			this.lblError.ForeColor = System.Drawing.Color.Red;
			this.lblError.Location = new System.Drawing.Point(14, 3);
			this.lblError.Name = "lblError";
			this.lblError.Size = new System.Drawing.Size(230, 18);
			this.lblError.TabIndex = 0;
			this.lblError.Text = "The given login information is invalid.";
			// 
			// panel3
			// 
			this.panel3.Controls.Add(this.label2);
			this.panel3.Controls.Add(this.label3);
			this.panel3.Controls.Add(this.butLogin);
			this.panel3.Controls.Add(this.label4);
			this.panel3.Controls.Add(this.butCancel);
			this.panel3.Controls.Add(this.ckbStayLoggedIn);
			this.panel3.Controls.Add(this.tbxUsername);
			this.panel3.Controls.Add(this.tbxPassword);
			this.panel3.Dock = System.Windows.Forms.DockStyle.Top;
			this.panel3.Location = new System.Drawing.Point(0, 47);
			this.panel3.Name = "panel3";
			this.panel3.Size = new System.Drawing.Size(256, 144);
			this.panel3.TabIndex = 17;
			// 
			// LoginForm
			// 
			this.AcceptButton = this.butLogin;
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.AutoSize = true;
			this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.CancelButton = this.butCancel;
			this.ClientSize = new System.Drawing.Size(256, 191);
			this.Controls.Add(this.panel3);
			this.Controls.Add(this.panel2);
			this.Controls.Add(this.panel1);
			this.m_fpdFontProvider.SetFontSet(this, "StandardText");
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.MinimumSize = new System.Drawing.Size(262, 28);
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
			this.Text = "Login";
			this.panel1.ResumeLayout(false);
			this.panel1.PerformLayout();
			this.panel2.ResumeLayout(false);
			this.panel3.ResumeLayout(false);
			this.panel3.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Label lblPrompt;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.Label label3;
		private System.Windows.Forms.Button butLogin;
		private System.Windows.Forms.Button butCancel;
		private System.Windows.Forms.TextBox tbxUsername;
		private System.Windows.Forms.TextBox tbxPassword;
		private System.Windows.Forms.CheckBox ckbStayLoggedIn;
		private System.Windows.Forms.Label label4;
		private System.Windows.Forms.Panel panel1;
		private System.Windows.Forms.Panel panel2;
		private Nexus.UI.Controls.AutosizeLabel lblError;
		private System.Windows.Forms.Panel panel3;
	}
}