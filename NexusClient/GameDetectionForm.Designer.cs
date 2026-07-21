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
			this.butCancel = new System.Windows.Forms.Button();
			this.butStopSearching = new System.Windows.Forms.Button();
			this.butQuickStartup = new System.Windows.Forms.Button();
			this.butOK = new System.Windows.Forms.Button();
			this.lblInfo = new System.Windows.Forms.Label();
			this.gameModeListView1 = new Nexus.Client.UI.Controls.GameModeListView();
			this.panel1.SuspendLayout();
			this.SuspendLayout();
			// 
			// panel1
			// 
			this.panel1.Controls.Add(this.butQuickStartup);
			this.panel1.Controls.Add(this.butStopSearching);
			this.panel1.Controls.Add(this.butCancel);
			this.panel1.Controls.Add(this.butOK);
			this.panel1.Controls.Add(this.lblInfo);
			this.panel1.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.panel1.Location = new System.Drawing.Point(0, 223);
			this.panel1.Name = "panel1";
			this.panel1.Size = new System.Drawing.Size(560, 80);
			this.panel1.TabIndex = 0;
			// 
			// butQuickStartup
			// 
			this.butQuickStartup.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butQuickStartup.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this.butQuickStartup.Location = new System.Drawing.Point(395, 12);
			this.butQuickStartup.Name = "butQuickStartup";
			this.butQuickStartup.Size = new System.Drawing.Size(120, 23);
			this.butQuickStartup.TabIndex = 3;
			this.butQuickStartup.Text = "Quick Startup";
			this.butQuickStartup.UseVisualStyleBackColor = true;
			this.butQuickStartup.Click += new System.EventHandler(this.butQuickStartup_Click);
			// 
			// butStopSearching
			// 
			this.butStopSearching.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butStopSearching.Location = new System.Drawing.Point(273, 12);
			this.butStopSearching.Name = "butStopSearching";
			this.butStopSearching.Size = new System.Drawing.Size(120, 23);
			this.butStopSearching.TabIndex = 2;
			this.butStopSearching.Text = "Stop Searching";
			this.butStopSearching.UseVisualStyleBackColor = true;
			this.butStopSearching.Click += new System.EventHandler(this.butStopSearching_Click);
			// 
			// butCancel
			// 
			this.butCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this.butCancel.Location = new System.Drawing.Point(195, 12);
			this.butCancel.Name = "butCancel";
			this.butCancel.Size = new System.Drawing.Size(75, 23);
			this.butCancel.TabIndex = 1;
			this.butCancel.Text = "Cancel";
			this.butCancel.UseVisualStyleBackColor = true;
			this.butCancel.Click += new System.EventHandler(this.butCancel_Click);
			// 
			// butOK
			// 
			this.butOK.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butOK.Enabled = false;
			this.butOK.Location = new System.Drawing.Point(116, 12);
			this.butOK.Name = "butOK";
			this.butOK.Size = new System.Drawing.Size(75, 23);
			this.butOK.TabIndex = 0;
			this.butOK.Text = "OK";
			this.butOK.UseVisualStyleBackColor = true;
			this.butOK.Click += new System.EventHandler(this.butOK_Click);
			// 
			// lblInfo
			// 
			this.lblInfo.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this.lblInfo.Location = new System.Drawing.Point(6, 6);
			this.lblInfo.Name = "lblInfo";
			this.lblInfo.Size = new System.Drawing.Size(188, 23);
			this.lblInfo.Visible = true;
			this.lblInfo.TabIndex = 2;
			this.lblInfo.MinimumSize = new System.Drawing.Size(768, 80);
			// 
			// gameModeListView1
			// 
			this.gameModeListView1.AutoScroll = true;
			this.gameModeListView1.BackColor = System.Drawing.SystemColors.Window;
			this.gameModeListView1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.gameModeListView1.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
			this.gameModeListView1.Location = new System.Drawing.Point(0, 0);
			this.gameModeListView1.Name = "gameModeListView1";
			this.gameModeListView1.SelectedGameMode = null;
			this.gameModeListView1.SelectedItem = null;
			this.gameModeListView1.Size = new System.Drawing.Size(284, 223);
			this.gameModeListView1.TabIndex = 1;
			// 
			// GameDetectionForm
			// 
			this.AcceptButton = this.butOK;
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.CancelButton = this.butCancel;
			this.ClientSize = new System.Drawing.Size(284, 264);
			this.ControlBox = false;
			this.Controls.Add(this.gameModeListView1);
			this.Controls.Add(this.panel1);
			this.m_fpdFontProvider.SetFontSet(this, "StandardText");
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
			this.Text = "Game Detection";
			this.panel1.ResumeLayout(false);
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.Panel panel1;
		private Nexus.Client.UI.Controls.GameModeListView gameModeListView1;
		private System.Windows.Forms.Button butCancel;
		private System.Windows.Forms.Button butStopSearching;
		private System.Windows.Forms.Button butQuickStartup;
		private System.Windows.Forms.Button butOK;
		private System.Windows.Forms.Label lblInfo;
	}
}