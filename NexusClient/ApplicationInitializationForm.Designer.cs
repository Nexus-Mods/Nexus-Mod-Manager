namespace Nexus.Client
{
	partial class ApplicationInitializationForm
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
			this.pbxLogo = new System.Windows.Forms.PictureBox();
			this.lblVersion = new System.Windows.Forms.Label();
			this.label1 = new System.Windows.Forms.Label();
			((System.ComponentModel.ISupportInitialize)(this.pbxLogo)).BeginInit();
			this.SuspendLayout();
			// 
			// pbxLogo
			// 
			this.pbxLogo.Image = global::Nexus.Client.Properties.Resources.tes_logo_full;
			this.pbxLogo.Location = new System.Drawing.Point(0, 0);
			this.pbxLogo.Margin = new System.Windows.Forms.Padding(0);
			this.pbxLogo.Name = "pbxLogo";
			this.pbxLogo.Size = new System.Drawing.Size(520, 178);
			this.pbxLogo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.AutoSize;
			this.pbxLogo.TabIndex = 0;
			this.pbxLogo.TabStop = false;
			// 
			// lblVersion
			// 
			this.lblVersion.AutoSize = true;
			this.lblVersion.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(69)))), ((int)(((byte)(69)))), ((int)(((byte)(69)))));
			this.lblVersion.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.lblVersion.ForeColor = System.Drawing.Color.White;
			this.lblVersion.Location = new System.Drawing.Point(401, 140);
			this.lblVersion.Name = "lblVersion";
			this.lblVersion.Size = new System.Drawing.Size(69, 20);
			this.lblVersion.TabIndex = 1;
			this.lblVersion.Text = "0.10.11";
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.m_fpdFontProvider.SetFontSet(this.label1, "SmallText");
			this.m_fpdFontProvider.SetFontStyle(this.label1, System.Drawing.FontStyle.Bold);
			this.label1.Location = new System.Drawing.Point(344, 178);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(161, 13);
			this.label1.TabIndex = 2;
			this.label1.Text = "© 2011 Black Tree Gaming";
			this.label1.TextAlign = System.Drawing.ContentAlignment.TopRight;
			// 
			// ApplicationInitializationForm
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.AutoSize = true;
			this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.ClientSize = new System.Drawing.Size(522, 295);
			this.ControlBox = false;
			this.Controls.Add(this.label1);
			this.Controls.Add(this.lblVersion);
			this.Controls.Add(this.pbxLogo);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "ApplicationInitializationForm";
			this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
			this.Text = "Nexus Mod Manager";
			this.TransparencyKey = System.Drawing.SystemColors.Control;
			((System.ComponentModel.ISupportInitialize)(this.pbxLogo)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.PictureBox pbxLogo;
		private System.Windows.Forms.Label lblVersion;
		private System.Windows.Forms.Label label1;
	}
}