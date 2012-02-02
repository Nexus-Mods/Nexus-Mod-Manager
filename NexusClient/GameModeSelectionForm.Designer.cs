namespace Nexus.Client
{
	partial class GameModeSelectionForm
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
			this.cbxRemember = new System.Windows.Forms.CheckBox();
			this.groupBox1 = new System.Windows.Forms.GroupBox();
			this.cbxGameMode = new System.Windows.Forms.ComboBox();
			this.butOK = new System.Windows.Forms.Button();
			this.groupBox1.SuspendLayout();
			this.SuspendLayout();
			// 
			// cbxRemember
			// 
			this.cbxRemember.AutoSize = true;
			this.cbxRemember.Location = new System.Drawing.Point(102, 65);
			this.cbxRemember.Name = "cbxRemember";
			this.cbxRemember.Size = new System.Drawing.Size(136, 17);
			this.cbxRemember.TabIndex = 1;
			this.cbxRemember.Text = "Don\'t ask me next time.";
			this.cbxRemember.UseVisualStyleBackColor = true;
			// 
			// groupBox1
			// 
			this.groupBox1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.groupBox1.Controls.Add(this.cbxGameMode);
			this.groupBox1.Location = new System.Drawing.Point(12, 12);
			this.groupBox1.Name = "groupBox1";
			this.groupBox1.Size = new System.Drawing.Size(466, 47);
			this.groupBox1.TabIndex = 0;
			this.groupBox1.TabStop = false;
			this.groupBox1.Text = "Select the game you would like to manage:";
			// 
			// cbxGameMode
			// 
			this.cbxGameMode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.cbxGameMode.FormattingEnabled = true;
			this.cbxGameMode.Location = new System.Drawing.Point(6, 19);
			this.cbxGameMode.Name = "cbxGameMode";
			this.cbxGameMode.Size = new System.Drawing.Size(214, 21);
			this.cbxGameMode.TabIndex = 0;
			this.cbxGameMode.SelectedIndexChanged += new System.EventHandler(this.cbxGameMode_SelectedIndexChanged);
			// 
			// butOK
			// 
			this.butOK.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butOK.Location = new System.Drawing.Point(403, 330);
			this.butOK.Name = "butOK";
			this.butOK.Size = new System.Drawing.Size(75, 23);
			this.butOK.TabIndex = 2;
			this.butOK.Text = "OK";
			this.butOK.UseVisualStyleBackColor = true;
			this.butOK.Click += new System.EventHandler(this.butOK_Click);
			// 
			// GameModeSelectionForm
			// 
			this.AcceptButton = this.butOK;
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(490, 365);
			this.Controls.Add(this.butOK);
			this.Controls.Add(this.groupBox1);
			this.Controls.Add(this.cbxRemember);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "GameModeSelectionForm";
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
			this.Text = "Game Selection";
			this.groupBox1.ResumeLayout(false);
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.CheckBox cbxRemember;
		private System.Windows.Forms.GroupBox groupBox1;
		private System.Windows.Forms.Button butOK;
		private System.Windows.Forms.ComboBox cbxGameMode;
	}
}