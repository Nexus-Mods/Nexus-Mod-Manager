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
			this.lblError = new System.Windows.Forms.Label();
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
			this.label2.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.label2.AutoSize = true;
			this.label2.Location = new System.Drawing.Point(12, 52);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(58, 13);
			this.label2.TabIndex = 1;
			this.label2.Text = "Username:";
			// 
			// label3
			// 
			this.label3.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.label3.AutoSize = true;
			this.label3.Location = new System.Drawing.Point(14, 78);
			this.label3.Name = "label3";
			this.label3.Size = new System.Drawing.Size(56, 13);
			this.label3.TabIndex = 2;
			this.label3.Text = "Password:";
			// 
			// butLogin
			// 
			this.butLogin.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butLogin.Location = new System.Drawing.Point(88, 156);
			this.butLogin.Name = "butLogin";
			this.butLogin.Size = new System.Drawing.Size(75, 23);
			this.butLogin.TabIndex = 3;
			this.butLogin.Text = "Login";
			this.butLogin.UseVisualStyleBackColor = true;
			this.butLogin.Click += new System.EventHandler(this.butLogin_Click);
			// 
			// butCancel
			// 
			this.butCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this.butCancel.Location = new System.Drawing.Point(169, 156);
			this.butCancel.Name = "butCancel";
			this.butCancel.Size = new System.Drawing.Size(75, 23);
			this.butCancel.TabIndex = 4;
			this.butCancel.Text = "Cancel";
			this.butCancel.UseVisualStyleBackColor = true;
			this.butCancel.Click += new System.EventHandler(this.butCancel_Click);
			// 
			// tbxUsername
			// 
			this.tbxUsername.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.tbxUsername.Location = new System.Drawing.Point(76, 49);
			this.tbxUsername.Name = "tbxUsername";
			this.tbxUsername.Size = new System.Drawing.Size(168, 20);
			this.tbxUsername.TabIndex = 0;
			// 
			// tbxPassword
			// 
			this.tbxPassword.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.tbxPassword.Location = new System.Drawing.Point(76, 75);
			this.tbxPassword.Name = "tbxPassword";
			this.tbxPassword.Size = new System.Drawing.Size(168, 20);
			this.tbxPassword.TabIndex = 1;
			this.tbxPassword.UseSystemPasswordChar = true;
			// 
			// ckbStayLoggedIn
			// 
			this.ckbStayLoggedIn.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.ckbStayLoggedIn.AutoSize = true;
			this.ckbStayLoggedIn.Location = new System.Drawing.Point(76, 101);
			this.ckbStayLoggedIn.Name = "ckbStayLoggedIn";
			this.ckbStayLoggedIn.Size = new System.Drawing.Size(98, 17);
			this.ckbStayLoggedIn.TabIndex = 2;
			this.ckbStayLoggedIn.Text = "Stay Logged In";
			this.ckbStayLoggedIn.UseVisualStyleBackColor = true;
			// 
			// label4
			// 
			this.label4.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.label4.Location = new System.Drawing.Point(73, 121);
			this.label4.Name = "label4";
			this.label4.Size = new System.Drawing.Size(171, 26);
			this.label4.TabIndex = 8;
			this.label4.Text = "Staying logged in will NOT store your password.";
			// 
			// lblError
			// 
			this.lblError.AutoSize = true;
			this.lblError.ForeColor = System.Drawing.Color.Red;
			this.lblError.Location = new System.Drawing.Point(12, 26);
			this.lblError.Name = "lblError";
			this.lblError.Size = new System.Drawing.Size(180, 13);
			this.lblError.TabIndex = 9;
			this.lblError.Text = "The given login information is invalid.";
			// 
			// LoginForm
			// 
			this.AcceptButton = this.butLogin;
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.CancelButton = this.butCancel;
			this.ClientSize = new System.Drawing.Size(256, 191);
			this.Controls.Add(this.lblError);
			this.Controls.Add(this.label4);
			this.Controls.Add(this.ckbStayLoggedIn);
			this.Controls.Add(this.tbxPassword);
			this.Controls.Add(this.tbxUsername);
			this.Controls.Add(this.butCancel);
			this.Controls.Add(this.butLogin);
			this.Controls.Add(this.label3);
			this.Controls.Add(this.label2);
			this.Controls.Add(this.lblPrompt);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "LoginForm";
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
			this.Text = "Login";
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
		private System.Windows.Forms.Label lblError;
	}
}