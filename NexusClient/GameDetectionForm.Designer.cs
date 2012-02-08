namespace Nexus.Client
{
	partial class GameDetectionForm
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
			this.panel1 = new System.Windows.Forms.Panel();
			this.gameModeListView1 = new Nexus.Client.GameModeListView();
			this.SuspendLayout();
			// 
			// panel1
			// 
			this.panel1.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.panel1.Location = new System.Drawing.Point(0, 212);
			this.panel1.Name = "panel1";
			this.panel1.Size = new System.Drawing.Size(284, 52);
			this.panel1.TabIndex = 0;
			// 
			// gameModeListView1
			// 
			this.gameModeListView1.AutoScroll = true;
			this.gameModeListView1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.gameModeListView1.Location = new System.Drawing.Point(0, 0);
			this.gameModeListView1.Name = "gameModeListView1";
			this.gameModeListView1.Size = new System.Drawing.Size(284, 212);
			this.gameModeListView1.TabIndex = 1;
			// 
			// GameDetectionForm
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(284, 264);
			this.Controls.Add(this.gameModeListView1);
			this.Controls.Add(this.panel1);
			this.Name = "GameDetectionForm";
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
			this.Text = "GameDetectionForm";
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.Panel panel1;
		private GameModeListView gameModeListView1;
	}
}