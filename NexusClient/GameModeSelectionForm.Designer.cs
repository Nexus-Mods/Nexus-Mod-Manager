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
			this.butOK = new System.Windows.Forms.Button();
			this.lblPrompt = new System.Windows.Forms.Label();
			this.panel1 = new System.Windows.Forms.Panel();
			this.panel2 = new System.Windows.Forms.Panel();
			this.panel3 = new System.Windows.Forms.Panel();
			this.glvGameMode = new Nexus.Client.UI.Controls.GameModeListView();
			this.panel1.SuspendLayout();
			this.panel2.SuspendLayout();
			this.panel3.SuspendLayout();
			this.SuspendLayout();
			// 
			// cbxRemember
			// 
			this.cbxRemember.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.cbxRemember.AutoSize = true;
			this.cbxRemember.Location = new System.Drawing.Point(342, 2);
			this.cbxRemember.Name = "cbxRemember";
			this.cbxRemember.Size = new System.Drawing.Size(136, 17);
			this.cbxRemember.TabIndex = 1;
			this.cbxRemember.Text = "Don\'t ask me next time.";
			this.cbxRemember.UseVisualStyleBackColor = true;
			// 
			// butOK
			// 
			this.butOK.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butOK.Location = new System.Drawing.Point(403, 25);
			this.butOK.Name = "butOK";
			this.butOK.Size = new System.Drawing.Size(75, 23);
			this.butOK.TabIndex = 2;
			this.butOK.Text = "OK";
			this.butOK.UseVisualStyleBackColor = true;
			this.butOK.Click += new System.EventHandler(this.butOK_Click);
			// 
			// lblPrompt
			// 
			this.lblPrompt.AutoSize = true;
			this.lblPrompt.Location = new System.Drawing.Point(12, 9);
			this.lblPrompt.Name = "lblPrompt";
			this.lblPrompt.Size = new System.Drawing.Size(210, 13);
			this.lblPrompt.TabIndex = 3;
			this.lblPrompt.Text = "Select the game you would like to manage:";
			// 
			// panel1
			// 
			this.panel1.AutoSize = true;
			this.panel1.Controls.Add(this.lblPrompt);
			this.panel1.Dock = System.Windows.Forms.DockStyle.Top;
			this.panel1.Location = new System.Drawing.Point(0, 0);
			this.panel1.Name = "panel1";
			this.panel1.Size = new System.Drawing.Size(490, 22);
			this.panel1.TabIndex = 5;
			// 
			// panel2
			// 
			this.panel2.Controls.Add(this.butOK);
			this.panel2.Controls.Add(this.cbxRemember);
			this.panel2.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.panel2.Location = new System.Drawing.Point(0, 305);
			this.panel2.Name = "panel2";
			this.panel2.Size = new System.Drawing.Size(490, 60);
			this.panel2.TabIndex = 6;
			// 
			// panel3
			// 
			this.panel3.AutoSize = true;
			this.panel3.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.panel3.Controls.Add(this.glvGameMode);
			this.panel3.Dock = System.Windows.Forms.DockStyle.Fill;
			this.panel3.Location = new System.Drawing.Point(0, 22);
			this.panel3.Name = "panel3";
			this.panel3.Padding = new System.Windows.Forms.Padding(13, 6, 13, 6);
			this.panel3.Size = new System.Drawing.Size(490, 283);
			this.panel3.TabIndex = 7;
			// 
			// glvGameMode
			// 
			this.glvGameMode.AutoSize = true;
			this.glvGameMode.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.glvGameMode.BackColor = System.Drawing.SystemColors.Window;
			this.glvGameMode.Dock = System.Windows.Forms.DockStyle.Fill;
			this.glvGameMode.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
			this.glvGameMode.Location = new System.Drawing.Point(13, 6);
			this.glvGameMode.Name = "glvGameMode";
			this.glvGameMode.SelectedGameMode = null;
			this.glvGameMode.SelectedItem = null;
			this.glvGameMode.Size = new System.Drawing.Size(464, 271);
			this.glvGameMode.TabIndex = 4;
			this.glvGameMode.SelectedItemChanged += new System.EventHandler<Nexus.Client.UI.Controls.SelectedItemEventArgs>(this.glvGameMode_SelectedItemChanged);
			this.glvGameMode.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(glvGameMode_MouseDoubleClick);
			// 
			// GameModeSelectionForm
			// 
			this.AcceptButton = this.butOK;
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.AutoSize = true;
			this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.ClientSize = new System.Drawing.Size(490, 365);
			this.Controls.Add(this.panel3);
			this.Controls.Add(this.panel2);
			this.Controls.Add(this.panel1);
			this.m_fpdFontProvider.SetFontSet(this, "StandardText");
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
			this.Text = "Game Selection";
			this.panel1.ResumeLayout(false);
			this.panel1.PerformLayout();
			this.panel2.ResumeLayout(false);
			this.panel2.PerformLayout();
			this.panel3.ResumeLayout(false);
			this.panel3.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.CheckBox cbxRemember;
		private System.Windows.Forms.Button butOK;
		private System.Windows.Forms.Label lblPrompt;
		private Nexus.Client.UI.Controls.GameModeListView glvGameMode;
		private System.Windows.Forms.Panel panel1;
		private System.Windows.Forms.Panel panel2;
		private System.Windows.Forms.Panel panel3;
	}
}