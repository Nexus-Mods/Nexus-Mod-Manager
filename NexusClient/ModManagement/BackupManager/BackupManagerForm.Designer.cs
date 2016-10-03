namespace Nexus.Client.ModManagement.UI
{
	partial class BackupManagerForm
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
			if (disposing)
			{
				if (components != null)
				{
					components.Dispose();
				}
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
			this.lstView = new System.Windows.Forms.ListView();
			this.components = new System.ComponentModel.Container();
			lstView.View = System.Windows.Forms.View.Details;
			this.btBackup = new System.Windows.Forms.Button();
			this.btCancel = new System.Windows.Forms.Button();
			this.lblBackup = new System.Windows.Forms.Label();
			this.lstView.Location = new System.Drawing.Point(16, 64);
			this.lstView.Size = new System.Drawing.Size(543, 180);
			this.lstView.TabIndex = 0;
			lstView.CheckBoxes = true;
			this.lstView.CheckBoxes = true;
			this.lstView.ItemCheck += new System.Windows.Forms.ItemCheckEventHandler(ListViewItem_ItemCheck);
			
			this.btBackup.Location = new System.Drawing.Point(16, 260);
			this.btBackup.Size = new System.Drawing.Size(60, 32);
			this.btBackup.TabIndex = 2;
			this.btBackup.Text = "Backup";
			this.btBackup.AutoSize = true;
			this.btBackup.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.btBackup.UseVisualStyleBackColor = true;
			this.btBackup.Click += new System.EventHandler(this.btBackup_Click);

			this.btCancel.Location = new System.Drawing.Point(90, 260);
			this.btCancel.Size = new System.Drawing.Size(60, 32);
			this.btCancel.TabIndex = 2;
			this.btCancel.Text = "Cancel";
			this.btCancel.AutoSize = true;
			this.btCancel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.btCancel.UseVisualStyleBackColor = true;
			this.btCancel.Click += new System.EventHandler(this.btCancel_Click);

			this.lblBackup.Text = "Select the files that you want to backup.";
			this.lblBackup.Location = new System.Drawing.Point(16, 34);
			this.lblBackup.Size = new System.Drawing.Size(300, 32);

			this.ClientSize = new System.Drawing.Size(563, 300);
			this.Controls.AddRange(new System.Windows.Forms.Control[] {this.lstView,
																		this.btBackup,
																		this.btCancel,
																		this.lblBackup});
			this.Text = "Nexus Mod Manager Backup";

		}
				
		#endregion

		private System.Windows.Forms.Button btBackup;
		private System.Windows.Forms.Button btCancel;
		private System.Windows.Forms.Label lblBackup;
		private System.Windows.Forms.ListView lstView;
	}
}