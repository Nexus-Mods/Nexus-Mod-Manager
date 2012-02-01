namespace Nexus.Client
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
			this.pbxGameLogo = new System.Windows.Forms.PictureBox();
			this.lblGameModeName = new System.Windows.Forms.Label();
			this.pnlCandidate = new System.Windows.Forms.Panel();
			this.pnlNotFound = new System.Windows.Forms.Panel();
			this.pnlSpecifyPath = new System.Windows.Forms.Panel();
			this.lblPath = new System.Windows.Forms.Label();
			this.label1 = new System.Windows.Forms.Label();
			this.butAccept = new System.Windows.Forms.Button();
			this.butReject = new System.Windows.Forms.Button();
			this.lblNotFoundTitle = new System.Windows.Forms.Label();
			this.lblNotFoundMessage = new System.Windows.Forms.Label();
			((System.ComponentModel.ISupportInitialize)(this.pbxGameLogo)).BeginInit();
			this.pnlCandidate.SuspendLayout();
			this.pnlNotFound.SuspendLayout();
			this.SuspendLayout();
			// 
			// pbxGameLogo
			// 
			this.pbxGameLogo.Dock = System.Windows.Forms.DockStyle.Left;
			this.pbxGameLogo.Location = new System.Drawing.Point(0, 0);
			this.pbxGameLogo.Name = "pbxGameLogo";
			this.pbxGameLogo.Size = new System.Drawing.Size(132, 491);
			this.pbxGameLogo.TabIndex = 0;
			this.pbxGameLogo.TabStop = false;
			// 
			// lblGameModeName
			// 
			this.lblGameModeName.AutoSize = true;
			this.lblGameModeName.Location = new System.Drawing.Point(212, 26);
			this.lblGameModeName.Name = "lblGameModeName";
			this.lblGameModeName.Size = new System.Drawing.Size(35, 13);
			this.lblGameModeName.TabIndex = 1;
			this.lblGameModeName.Text = "label1";
			// 
			// pnlCandidate
			// 
			this.pnlCandidate.Controls.Add(this.butReject);
			this.pnlCandidate.Controls.Add(this.butAccept);
			this.pnlCandidate.Controls.Add(this.label1);
			this.pnlCandidate.Controls.Add(this.lblPath);
			this.pnlCandidate.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.pnlCandidate.Location = new System.Drawing.Point(132, 391);
			this.pnlCandidate.Name = "pnlCandidate";
			this.pnlCandidate.Size = new System.Drawing.Size(384, 100);
			this.pnlCandidate.TabIndex = 2;
			// 
			// pnlNotFound
			// 
			this.pnlNotFound.Controls.Add(this.lblNotFoundMessage);
			this.pnlNotFound.Controls.Add(this.lblNotFoundTitle);
			this.pnlNotFound.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.pnlNotFound.Location = new System.Drawing.Point(132, 291);
			this.pnlNotFound.Name = "pnlNotFound";
			this.pnlNotFound.Size = new System.Drawing.Size(384, 100);
			this.pnlNotFound.TabIndex = 3;
			// 
			// pnlSpecifyPath
			// 
			this.pnlSpecifyPath.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.pnlSpecifyPath.Location = new System.Drawing.Point(132, 191);
			this.pnlSpecifyPath.Name = "pnlSpecifyPath";
			this.pnlSpecifyPath.Size = new System.Drawing.Size(384, 100);
			this.pnlSpecifyPath.TabIndex = 4;
			// 
			// lblPath
			// 
			this.lblPath.AutoSize = true;
			this.lblPath.Location = new System.Drawing.Point(53, 32);
			this.lblPath.Name = "lblPath";
			this.lblPath.Size = new System.Drawing.Size(35, 13);
			this.lblPath.TabIndex = 0;
			this.lblPath.Text = "label1";
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(7, 84);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(47, 13);
			this.label1.TabIndex = 1;
			this.label1.Text = "Correct?";
			// 
			// butAccept
			// 
			this.butAccept.Location = new System.Drawing.Point(92, 63);
			this.butAccept.Name = "butAccept";
			this.butAccept.Size = new System.Drawing.Size(75, 23);
			this.butAccept.TabIndex = 2;
			this.butAccept.Text = "button1";
			this.butAccept.UseVisualStyleBackColor = true;
			// 
			// butReject
			// 
			this.butReject.Location = new System.Drawing.Point(197, 63);
			this.butReject.Name = "butReject";
			this.butReject.Size = new System.Drawing.Size(75, 23);
			this.butReject.TabIndex = 3;
			this.butReject.Text = "button2";
			this.butReject.UseVisualStyleBackColor = true;
			// 
			// lblNotFoundTitle
			// 
			this.lblNotFoundTitle.AutoSize = true;
			this.lblNotFoundTitle.Font = new System.Drawing.Font("Microsoft Sans Serif", 24F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.lblNotFoundTitle.ForeColor = System.Drawing.Color.DarkRed;
			this.lblNotFoundTitle.Location = new System.Drawing.Point(80, 21);
			this.lblNotFoundTitle.Name = "lblNotFoundTitle";
			this.lblNotFoundTitle.Size = new System.Drawing.Size(178, 37);
			this.lblNotFoundTitle.TabIndex = 0;
			this.lblNotFoundTitle.Text = "Not Found!";
			// 
			// lblNotFoundMessage
			// 
			this.lblNotFoundMessage.Location = new System.Drawing.Point(29, 49);
			this.lblNotFoundMessage.Name = "lblNotFoundMessage";
			this.lblNotFoundMessage.Size = new System.Drawing.Size(264, 39);
			this.lblNotFoundMessage.TabIndex = 1;
			this.lblNotFoundMessage.Text = "{0} could not be found.\r\nIf this is not correct, enter the game path:";
			// 
			// GameModeListViewItem
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.pnlSpecifyPath);
			this.Controls.Add(this.pnlNotFound);
			this.Controls.Add(this.pnlCandidate);
			this.Controls.Add(this.lblGameModeName);
			this.Controls.Add(this.pbxGameLogo);
			this.Name = "GameModeListViewItem";
			this.Size = new System.Drawing.Size(516, 491);
			((System.ComponentModel.ISupportInitialize)(this.pbxGameLogo)).EndInit();
			this.pnlCandidate.ResumeLayout(false);
			this.pnlCandidate.PerformLayout();
			this.pnlNotFound.ResumeLayout(false);
			this.pnlNotFound.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.PictureBox pbxGameLogo;
		private System.Windows.Forms.Label lblGameModeName;
		private System.Windows.Forms.Panel pnlCandidate;
		private System.Windows.Forms.Label lblPath;
		private System.Windows.Forms.Panel pnlNotFound;
		private System.Windows.Forms.Panel pnlSpecifyPath;
		private System.Windows.Forms.Button butReject;
		private System.Windows.Forms.Button butAccept;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.Label lblNotFoundTitle;
		private System.Windows.Forms.Label lblNotFoundMessage;
	}
}
