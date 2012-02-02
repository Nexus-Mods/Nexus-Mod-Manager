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
			this.components = new System.ComponentModel.Container();
			this.lblGameModeName = new System.Windows.Forms.Label();
			this.pnlCandidate = new System.Windows.Forms.Panel();
			this.lblFoundTitle = new System.Windows.Forms.Label();
			this.butReject = new System.Windows.Forms.Button();
			this.butAccept = new System.Windows.Forms.Button();
			this.lblCorrect = new System.Windows.Forms.Label();
			this.lblPath = new System.Windows.Forms.Label();
			this.pnlNotFound = new System.Windows.Forms.Panel();
			this.butSelectPath = new System.Windows.Forms.Button();
			this.tbxInstallPath = new System.Windows.Forms.TextBox();
			this.lblNotFoundMessage = new System.Windows.Forms.Label();
			this.lblNotFoundTitle = new System.Windows.Forms.Label();
			this.fbdSelectPath = new System.Windows.Forms.FolderBrowserDialog();
			this.pbxGameLogo = new System.Windows.Forms.PictureBox();
			this.erpErrors = new System.Windows.Forms.ErrorProvider(this.components);
			this.panel1 = new System.Windows.Forms.Panel();
			this.pnlCandidate.SuspendLayout();
			this.pnlNotFound.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.pbxGameLogo)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).BeginInit();
			this.panel1.SuspendLayout();
			this.SuspendLayout();
			// 
			// lblGameModeName
			// 
			this.lblGameModeName.AutoSize = true;
			this.lblGameModeName.Location = new System.Drawing.Point(3, 4);
			this.lblGameModeName.Name = "lblGameModeName";
			this.lblGameModeName.Size = new System.Drawing.Size(35, 13);
			this.lblGameModeName.TabIndex = 1;
			this.lblGameModeName.Text = "label1";
			// 
			// pnlCandidate
			// 
			this.pnlCandidate.Controls.Add(this.lblFoundTitle);
			this.pnlCandidate.Controls.Add(this.butReject);
			this.pnlCandidate.Controls.Add(this.butAccept);
			this.pnlCandidate.Controls.Add(this.lblCorrect);
			this.pnlCandidate.Controls.Add(this.lblPath);
			this.pnlCandidate.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.pnlCandidate.Location = new System.Drawing.Point(132, 100);
			this.pnlCandidate.Name = "pnlCandidate";
			this.pnlCandidate.Size = new System.Drawing.Size(384, 72);
			this.pnlCandidate.TabIndex = 2;
			// 
			// lblFoundTitle
			// 
			this.lblFoundTitle.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.lblFoundTitle.AutoSize = true;
			this.lblFoundTitle.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Italic, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.lblFoundTitle.ForeColor = System.Drawing.Color.Green;
			this.lblFoundTitle.Location = new System.Drawing.Point(163, 3);
			this.lblFoundTitle.Name = "lblFoundTitle";
			this.lblFoundTitle.Size = new System.Drawing.Size(59, 20);
			this.lblFoundTitle.TabIndex = 4;
			this.lblFoundTitle.Text = "Found!";
			// 
			// butReject
			// 
			this.butReject.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.butReject.AutoSize = true;
			this.butReject.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.butReject.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
			this.butReject.Image = global::Nexus.Client.Properties.Resources.dialog_cancel_4_24;
			this.butReject.Location = new System.Drawing.Point(233, 39);
			this.butReject.Name = "butReject";
			this.butReject.Size = new System.Drawing.Size(30, 30);
			this.butReject.TabIndex = 3;
			this.butReject.UseVisualStyleBackColor = true;
			// 
			// butAccept
			// 
			this.butAccept.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.butAccept.AutoSize = true;
			this.butAccept.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.butAccept.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
			this.butAccept.Image = global::Nexus.Client.Properties.Resources.dialog_ok_4_22;
			this.butAccept.Location = new System.Drawing.Point(197, 41);
			this.butAccept.Name = "butAccept";
			this.butAccept.Size = new System.Drawing.Size(28, 28);
			this.butAccept.TabIndex = 2;
			this.butAccept.UseVisualStyleBackColor = true;
			// 
			// lblCorrect
			// 
			this.lblCorrect.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.lblCorrect.AutoSize = true;
			this.lblCorrect.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.lblCorrect.Location = new System.Drawing.Point(121, 43);
			this.lblCorrect.Name = "lblCorrect";
			this.lblCorrect.Size = new System.Drawing.Size(70, 20);
			this.lblCorrect.TabIndex = 1;
			this.lblCorrect.Text = "Correct?";
			// 
			// lblPath
			// 
			this.lblPath.AutoSize = true;
			this.lblPath.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.lblPath.Location = new System.Drawing.Point(6, 40);
			this.lblPath.Name = "lblPath";
			this.lblPath.Size = new System.Drawing.Size(46, 17);
			this.lblPath.TabIndex = 0;
			this.lblPath.Text = "label1";
			// 
			// pnlNotFound
			// 
			this.pnlNotFound.Controls.Add(this.butSelectPath);
			this.pnlNotFound.Controls.Add(this.tbxInstallPath);
			this.pnlNotFound.Controls.Add(this.lblNotFoundMessage);
			this.pnlNotFound.Controls.Add(this.lblNotFoundTitle);
			this.pnlNotFound.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.pnlNotFound.Location = new System.Drawing.Point(132, 34);
			this.pnlNotFound.Name = "pnlNotFound";
			this.pnlNotFound.Size = new System.Drawing.Size(384, 66);
			this.pnlNotFound.TabIndex = 3;
			// 
			// butSelectPath
			// 
			this.butSelectPath.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butSelectPath.AutoSize = true;
			this.butSelectPath.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.butSelectPath.Location = new System.Drawing.Point(335, 37);
			this.butSelectPath.Name = "butSelectPath";
			this.butSelectPath.Size = new System.Drawing.Size(26, 23);
			this.butSelectPath.TabIndex = 3;
			this.butSelectPath.Text = "...";
			this.butSelectPath.UseVisualStyleBackColor = true;
			this.butSelectPath.Click += new System.EventHandler(this.butSelectPath_Click);
			// 
			// tbxInstallPath
			// 
			this.tbxInstallPath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.tbxInstallPath.Location = new System.Drawing.Point(23, 39);
			this.tbxInstallPath.Name = "tbxInstallPath";
			this.tbxInstallPath.Size = new System.Drawing.Size(306, 20);
			this.tbxInstallPath.TabIndex = 2;
			// 
			// lblNotFoundMessage
			// 
			this.lblNotFoundMessage.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this.lblNotFoundMessage.AutoSize = true;
			this.lblNotFoundMessage.Location = new System.Drawing.Point(20, 23);
			this.lblNotFoundMessage.Name = "lblNotFoundMessage";
			this.lblNotFoundMessage.Size = new System.Drawing.Size(200, 13);
			this.lblNotFoundMessage.TabIndex = 1;
			this.lblNotFoundMessage.Text = "If this is not correct, enter the game path:";
			// 
			// lblNotFoundTitle
			// 
			this.lblNotFoundTitle.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.lblNotFoundTitle.AutoSize = true;
			this.lblNotFoundTitle.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Italic, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.lblNotFoundTitle.ForeColor = System.Drawing.Color.DarkRed;
			this.lblNotFoundTitle.Location = new System.Drawing.Point(143, 3);
			this.lblNotFoundTitle.Name = "lblNotFoundTitle";
			this.lblNotFoundTitle.Size = new System.Drawing.Size(88, 20);
			this.lblNotFoundTitle.TabIndex = 0;
			this.lblNotFoundTitle.Text = "Not Found!";
			// 
			// fbdSelectPath
			// 
			this.fbdSelectPath.ShowNewFolderButton = false;
			// 
			// pbxGameLogo
			// 
			this.pbxGameLogo.Dock = System.Windows.Forms.DockStyle.Left;
			this.pbxGameLogo.Location = new System.Drawing.Point(0, 0);
			this.pbxGameLogo.Name = "pbxGameLogo";
			this.pbxGameLogo.Size = new System.Drawing.Size(132, 172);
			this.pbxGameLogo.TabIndex = 0;
			this.pbxGameLogo.TabStop = false;
			// 
			// erpErrors
			// 
			this.erpErrors.ContainerControl = this;
			// 
			// panel1
			// 
			this.panel1.Controls.Add(this.lblGameModeName);
			this.panel1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.panel1.Location = new System.Drawing.Point(132, 0);
			this.panel1.Name = "panel1";
			this.panel1.Size = new System.Drawing.Size(384, 34);
			this.panel1.TabIndex = 4;
			// 
			// GameModeListViewItem
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.panel1);
			this.Controls.Add(this.pnlNotFound);
			this.Controls.Add(this.pnlCandidate);
			this.Controls.Add(this.pbxGameLogo);
			this.Name = "GameModeListViewItem";
			this.Size = new System.Drawing.Size(516, 172);
			this.pnlCandidate.ResumeLayout(false);
			this.pnlCandidate.PerformLayout();
			this.pnlNotFound.ResumeLayout(false);
			this.pnlNotFound.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this.pbxGameLogo)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).EndInit();
			this.panel1.ResumeLayout(false);
			this.panel1.PerformLayout();
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.PictureBox pbxGameLogo;
		private System.Windows.Forms.Label lblGameModeName;
		private System.Windows.Forms.Panel pnlCandidate;
		private System.Windows.Forms.Label lblPath;
		private System.Windows.Forms.Panel pnlNotFound;
		private System.Windows.Forms.Button butReject;
		private System.Windows.Forms.Button butAccept;
		private System.Windows.Forms.Label lblCorrect;
		private System.Windows.Forms.Label lblNotFoundTitle;
		private System.Windows.Forms.Label lblNotFoundMessage;
		private System.Windows.Forms.TextBox tbxInstallPath;
		private System.Windows.Forms.Button butSelectPath;
		private System.Windows.Forms.FolderBrowserDialog fbdSelectPath;
		private System.Windows.Forms.ErrorProvider erpErrors;
		private System.Windows.Forms.Label lblFoundTitle;
		private System.Windows.Forms.Panel panel1;
	}
}
