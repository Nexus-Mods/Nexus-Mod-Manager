namespace Nexus.Client.UI.Controls
{
	partial class GameModeListViewItem
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
			this.lblGameModeName = new System.Windows.Forms.Label();
			this.pbxGameLogo = new System.Windows.Forms.PictureBox();
			((System.ComponentModel.ISupportInitialize)(this.pbxGameLogo)).BeginInit();
			this.SuspendLayout();
			// 
			// lblGameModeName
			// 
			this.lblGameModeName.AutoSize = true;
			this.lblGameModeName.Dock = System.Windows.Forms.DockStyle.Fill;
			this.m_fpdFontProvider.SetFontSet(this.lblGameModeName, "HeadingText");
			this.m_fpdFontProvider.SetFontSize(this.lblGameModeName, 14F);
			this.m_fpdFontProvider.SetFontStyle(this.lblGameModeName, System.Drawing.FontStyle.Bold);
			this.lblGameModeName.Location = new System.Drawing.Point(60, 0);
			this.lblGameModeName.MinimumSize = new System.Drawing.Size(100, 45);
			this.lblGameModeName.Name = "lblGameModeName";
			this.lblGameModeName.Size = new System.Drawing.Size(185, 45);
			this.lblGameModeName.TabIndex = 2;
			this.lblGameModeName.Text = "GAME TITLE";
			this.lblGameModeName.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this.lblGameModeName.Click += new System.EventHandler(this.Control_Click);
			this.lblGameModeName.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(lblGameModeName_MouseDoubleClick);
			// 
			// pbxGameLogo
			// 
			this.pbxGameLogo.Dock = System.Windows.Forms.DockStyle.Left;
			this.pbxGameLogo.Location = new System.Drawing.Point(0, 0);
			this.pbxGameLogo.MinimumSize = new System.Drawing.Size(60, 45);
			this.pbxGameLogo.Name = "pbxGameLogo";
			this.pbxGameLogo.Size = new System.Drawing.Size(60, 45);
			this.pbxGameLogo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.CenterImage;
			this.pbxGameLogo.TabIndex = 1;
			this.pbxGameLogo.TabStop = false;
			this.pbxGameLogo.Click += new System.EventHandler(this.Control_Click);
			// 
			// GameModeListViewItem
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.AutoSize = true;
			this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.Controls.Add(this.lblGameModeName);
			this.Controls.Add(this.pbxGameLogo);
			this.Name = "GameModeListViewItem";
			this.Size = new System.Drawing.Size(245, 45);
			((System.ComponentModel.ISupportInitialize)(this.pbxGameLogo)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.PictureBox pbxGameLogo;
		private System.Windows.Forms.Label lblGameModeName;
	}
}
