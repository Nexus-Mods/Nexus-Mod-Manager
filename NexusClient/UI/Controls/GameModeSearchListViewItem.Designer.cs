namespace Nexus.Client.UI.Controls
{
	partial class GameModeSearchListViewItem
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
			this.label1 = new System.Windows.Forms.Label();
			this.lblFoundTitle = new System.Windows.Forms.Label();
			this.butReject = new System.Windows.Forms.Button();
			this.butAccept = new System.Windows.Forms.Button();
			this.lblPath = new Nexus.Client.Controls.PathLabel();
			this.pnlNotFound = new System.Windows.Forms.Panel();
			this.butOverride = new System.Windows.Forms.Button();
			this.butSelectPath = new System.Windows.Forms.Button();
			this.tbxInstallPath = new System.Windows.Forms.TextBox();
			this.lblNotFoundMessage = new System.Windows.Forms.Label();
			this.lblNotFoundTitle = new System.Windows.Forms.Label();
			this.fbdSelectPath = new System.Windows.Forms.FolderBrowserDialog();
			this.pbxGameLogo = new System.Windows.Forms.PictureBox();
			this.erpErrors = new System.Windows.Forms.ErrorProvider(this.components);
			this.panel1 = new System.Windows.Forms.Panel();
			this.pnlSearching = new System.Windows.Forms.Panel();
			this.butCancel = new System.Windows.Forms.Button();
			this.pbrProgress = new System.Windows.Forms.ProgressBar();
			this.lblSearchingTitle = new System.Windows.Forms.Label();
			this.lblProgressMessage = new Nexus.Client.Controls.PathLabel();
			this.pnlSet = new System.Windows.Forms.Panel();
			this.lblFinalPath = new Nexus.Client.Controls.PathLabel();
			this.pnlCandidate.SuspendLayout();
			this.pnlNotFound.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.pbxGameLogo)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).BeginInit();
			this.panel1.SuspendLayout();
			this.pnlSearching.SuspendLayout();
			this.pnlSet.SuspendLayout();
			this.SuspendLayout();
			// 
			// lblGameModeName
			// 
			this.lblGameModeName.Dock = System.Windows.Forms.DockStyle.Top;
			this.lblGameModeName.Font = new System.Drawing.Font("Microsoft Sans Serif", 20F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.lblGameModeName.Location = new System.Drawing.Point(0, 0);
			this.lblGameModeName.Name = "lblGameModeName";
			this.lblGameModeName.Size = new System.Drawing.Size(388, 31);
			this.lblGameModeName.TabIndex = 1;
			this.lblGameModeName.Text = "GAME TITLE";
			this.lblGameModeName.TextAlign = System.Drawing.ContentAlignment.BottomRight;
			// 
			// pnlCandidate
			// 
			this.pnlCandidate.Controls.Add(this.label1);
			this.pnlCandidate.Controls.Add(this.lblFoundTitle);
			this.pnlCandidate.Controls.Add(this.butReject);
			this.pnlCandidate.Controls.Add(this.butAccept);
			this.pnlCandidate.Controls.Add(this.lblPath);
			this.pnlCandidate.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.pnlCandidate.Location = new System.Drawing.Point(128, 453);
			this.pnlCandidate.Name = "pnlCandidate";
			this.pnlCandidate.Size = new System.Drawing.Size(388, 66);
			this.pnlCandidate.TabIndex = 2;
			// 
			// label1
			// 
			this.label1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(7, 20);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(195, 13);
			this.label1.TabIndex = 5;
			this.label1.Text = "Please verify that the location is correct.";
			// 
			// lblFoundTitle
			// 
			this.lblFoundTitle.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.lblFoundTitle.AutoSize = true;
			this.lblFoundTitle.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Italic, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.lblFoundTitle.ForeColor = System.Drawing.Color.Green;
			this.lblFoundTitle.Location = new System.Drawing.Point(6, 0);
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
			this.butReject.FlatAppearance.BorderSize = 0;
			this.butReject.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.butReject.Image = global::Nexus.Client.Properties.Resources.dialog_cancel_4_16;
			this.butReject.Location = new System.Drawing.Point(363, 3);
			this.butReject.Name = "butReject";
			this.butReject.Size = new System.Drawing.Size(22, 22);
			this.butReject.TabIndex = 3;
			this.butReject.UseVisualStyleBackColor = true;
			this.butReject.Click += new System.EventHandler(this.butReject_Click);
			// 
			// butAccept
			// 
			this.butAccept.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.butAccept.AutoSize = true;
			this.butAccept.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.butAccept.FlatAppearance.BorderSize = 0;
			this.butAccept.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.butAccept.Image = global::Nexus.Client.Properties.Resources.dialog_ok_4_16;
			this.butAccept.Location = new System.Drawing.Point(335, 3);
			this.butAccept.Name = "butAccept";
			this.butAccept.Size = new System.Drawing.Size(22, 22);
			this.butAccept.TabIndex = 2;
			this.butAccept.UseVisualStyleBackColor = true;
			this.butAccept.Click += new System.EventHandler(this.butAccept_Click);
			// 
			// lblPath
			// 
			this.lblPath.AutoEllipsis = true;
			this.lblPath.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.lblPath.Location = new System.Drawing.Point(7, 33);
			this.lblPath.Name = "lblPath";
			this.lblPath.Size = new System.Drawing.Size(378, 33);
			this.lblPath.TabIndex = 0;
			this.lblPath.Text = "C:\\Program Files\\Interim Path\\Steam\\steamapps\\common\\Skyrim";
			this.lblPath.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			// 
			// pnlNotFound
			// 
			this.pnlNotFound.Controls.Add(this.butOverride);
			this.pnlNotFound.Controls.Add(this.butSelectPath);
			this.pnlNotFound.Controls.Add(this.tbxInstallPath);
			this.pnlNotFound.Controls.Add(this.lblNotFoundMessage);
			this.pnlNotFound.Controls.Add(this.lblNotFoundTitle);
			this.pnlNotFound.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.pnlNotFound.Location = new System.Drawing.Point(128, 321);
			this.pnlNotFound.Name = "pnlNotFound";
			this.pnlNotFound.Size = new System.Drawing.Size(388, 66);
			this.pnlNotFound.TabIndex = 3;
			// 
			// butOverride
			// 
			this.butOverride.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.butOverride.AutoSize = true;
			this.butOverride.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.butOverride.FlatAppearance.BorderSize = 0;
			this.butOverride.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.butOverride.Image = global::Nexus.Client.Properties.Resources.dialog_ok_4_16;
			this.butOverride.Location = new System.Drawing.Point(363, 6);
			this.butOverride.Name = "butOverride";
			this.butOverride.Size = new System.Drawing.Size(22, 22);
			this.butOverride.TabIndex = 4;
			this.butOverride.UseVisualStyleBackColor = true;
			this.butOverride.Click += new System.EventHandler(this.butOverride_Click);
			// 
			// butSelectPath
			// 
			this.butSelectPath.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butSelectPath.AutoSize = true;
			this.butSelectPath.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.butSelectPath.Location = new System.Drawing.Point(339, 34);
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
			this.tbxInstallPath.Location = new System.Drawing.Point(10, 36);
			this.tbxInstallPath.Name = "tbxInstallPath";
			this.tbxInstallPath.Size = new System.Drawing.Size(323, 20);
			this.tbxInstallPath.TabIndex = 2;
			this.tbxInstallPath.TextChanged += new System.EventHandler(this.tbxInstallPath_TextChanged);
			// 
			// lblNotFoundMessage
			// 
			this.lblNotFoundMessage.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this.lblNotFoundMessage.AutoSize = true;
			this.lblNotFoundMessage.Location = new System.Drawing.Point(7, 20);
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
			this.lblNotFoundTitle.Location = new System.Drawing.Point(6, 0);
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
			this.pbxGameLogo.Size = new System.Drawing.Size(128, 519);
			this.pbxGameLogo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.CenterImage;
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
			this.panel1.Location = new System.Drawing.Point(128, 0);
			this.panel1.Name = "panel1";
			this.panel1.Size = new System.Drawing.Size(388, 255);
			this.panel1.TabIndex = 4;
			// 
			// pnlSearching
			// 
			this.pnlSearching.Controls.Add(this.butCancel);
			this.pnlSearching.Controls.Add(this.pbrProgress);
			this.pnlSearching.Controls.Add(this.lblSearchingTitle);
			this.pnlSearching.Controls.Add(this.lblProgressMessage);
			this.pnlSearching.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.pnlSearching.Location = new System.Drawing.Point(128, 387);
			this.pnlSearching.Name = "pnlSearching";
			this.pnlSearching.Size = new System.Drawing.Size(388, 66);
			this.pnlSearching.TabIndex = 2;
			// 
			// butCancel
			// 
			this.butCancel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.butCancel.AutoSize = true;
			this.butCancel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.butCancel.FlatAppearance.BorderSize = 0;
			this.butCancel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.butCancel.Image = global::Nexus.Client.Properties.Resources.dialog_cancel_4_16;
			this.butCancel.Location = new System.Drawing.Point(363, 6);
			this.butCancel.Name = "butCancel";
			this.butCancel.Size = new System.Drawing.Size(22, 22);
			this.butCancel.TabIndex = 6;
			this.butCancel.UseVisualStyleBackColor = true;
			this.butCancel.Click += new System.EventHandler(this.butCancel_Click);
			// 
			// pbrProgress
			// 
			this.pbrProgress.Location = new System.Drawing.Point(6, 36);
			this.pbrProgress.Name = "pbrProgress";
			this.pbrProgress.Size = new System.Drawing.Size(379, 23);
			this.pbrProgress.Style = System.Windows.Forms.ProgressBarStyle.Marquee;
			this.pbrProgress.TabIndex = 5;
			// 
			// lblSearchingTitle
			// 
			this.lblSearchingTitle.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.lblSearchingTitle.AutoSize = true;
			this.lblSearchingTitle.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Italic, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.lblSearchingTitle.ForeColor = System.Drawing.Color.Blue;
			this.lblSearchingTitle.Location = new System.Drawing.Point(6, 0);
			this.lblSearchingTitle.Name = "lblSearchingTitle";
			this.lblSearchingTitle.Size = new System.Drawing.Size(89, 20);
			this.lblSearchingTitle.TabIndex = 4;
			this.lblSearchingTitle.Text = "Searching..";
			// 
			// lblProgressMessage
			// 
			this.lblProgressMessage.AutoEllipsis = true;
			this.lblProgressMessage.Location = new System.Drawing.Point(7, 20);
			this.lblProgressMessage.Name = "lblProgressMessage";
			this.lblProgressMessage.Size = new System.Drawing.Size(358, 13);
			this.lblProgressMessage.TabIndex = 1;
			this.lblProgressMessage.Text = "label3";
			// 
			// pnlSet
			// 
			this.pnlSet.Controls.Add(this.lblFinalPath);
			this.pnlSet.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.pnlSet.Location = new System.Drawing.Point(128, 255);
			this.pnlSet.Name = "pnlSet";
			this.pnlSet.Size = new System.Drawing.Size(388, 66);
			this.pnlSet.TabIndex = 6;
			// 
			// lblFinalPath
			// 
			this.lblFinalPath.AutoEllipsis = true;
			this.lblFinalPath.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.lblFinalPath.Location = new System.Drawing.Point(7, 3);
			this.lblFinalPath.Name = "lblFinalPath";
			this.lblFinalPath.Size = new System.Drawing.Size(378, 60);
			this.lblFinalPath.TabIndex = 0;
			this.lblFinalPath.Text = "C:\\Program Files\\Interim Path\\Steam\\steamapps\\common\\Skyrim";
			this.lblFinalPath.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			// 
			// GameModeSearchListViewItem
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.panel1);
			this.Controls.Add(this.pnlSet);
			this.Controls.Add(this.pnlNotFound);
			this.Controls.Add(this.pnlSearching);
			this.Controls.Add(this.pnlCandidate);
			this.Controls.Add(this.pbxGameLogo);
			this.MinimumSize = new System.Drawing.Size(516, 97);
			this.Name = "GameModeSearchListViewItem";
			this.Size = new System.Drawing.Size(516, 519);
			this.pnlCandidate.ResumeLayout(false);
			this.pnlCandidate.PerformLayout();
			this.pnlNotFound.ResumeLayout(false);
			this.pnlNotFound.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this.pbxGameLogo)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).EndInit();
			this.panel1.ResumeLayout(false);
			this.pnlSearching.ResumeLayout(false);
			this.pnlSearching.PerformLayout();
			this.pnlSet.ResumeLayout(false);
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.PictureBox pbxGameLogo;
		private System.Windows.Forms.Label lblGameModeName;
		private System.Windows.Forms.Panel pnlCandidate;
		private Nexus.Client.Controls.PathLabel lblPath;
		private System.Windows.Forms.Panel pnlNotFound;
		private System.Windows.Forms.Button butReject;
		private System.Windows.Forms.Button butAccept;
		private System.Windows.Forms.Label lblNotFoundTitle;
		private System.Windows.Forms.Label lblNotFoundMessage;
		private System.Windows.Forms.TextBox tbxInstallPath;
		private System.Windows.Forms.Button butSelectPath;
		private System.Windows.Forms.FolderBrowserDialog fbdSelectPath;
		private System.Windows.Forms.ErrorProvider erpErrors;
		private System.Windows.Forms.Label lblFoundTitle;
		private System.Windows.Forms.Panel panel1;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.Panel pnlSearching;
		private System.Windows.Forms.ProgressBar pbrProgress;
		private System.Windows.Forms.Label lblSearchingTitle;
		private Nexus.Client.Controls.PathLabel lblProgressMessage;
		private System.Windows.Forms.Button butOverride;
		private System.Windows.Forms.Panel pnlSet;
		private Nexus.Client.Controls.PathLabel lblFinalPath;
		private System.Windows.Forms.Button butCancel;
	}
}
