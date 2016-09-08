using System.Windows.Forms;

namespace Nexus.Client.ModManagement.UI
{
	partial class RestoreBackupForm
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(RestoreBackupForm));
			this.btYes = new System.Windows.Forms.Button();
			this.btNo = new System.Windows.Forms.Button();
			this.btCancel = new System.Windows.Forms.Button();
			this.btSelectFile = new System.Windows.Forms.Button();
			this.lblYes = new System.Windows.Forms.Label();
			this.lblNo = new System.Windows.Forms.Label();
			this.lblCancel = new System.Windows.Forms.Label();
			this.lblEstimated = new System.Windows.Forms.Label();
			this.fdFile = new System.Windows.Forms.OpenFileDialog();
			this.tbFile = new System.Windows.Forms.TextBox();
			this.SuspendLayout();
			// 
			// btYes
			// 
			this.btYes.AutoSize = true;
			this.btYes.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.btYes.Location = new System.Drawing.Point(478, 198);
			this.btYes.Name = "btYes";
			this.btYes.Size = new System.Drawing.Size(106, 23);
			this.btYes.TabIndex = 2;
			this.btYes.Text = "Purge and Restore";
			this.btYes.UseVisualStyleBackColor = true;
			this.btYes.Click += new System.EventHandler(this.btYes_Click);
			// 
			// btNo
			// 
			this.btNo.AutoSize = true;
			this.btNo.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.btNo.Location = new System.Drawing.Point(590, 198);
			this.btNo.Name = "btNo";
			this.btNo.Size = new System.Drawing.Size(54, 23);
			this.btNo.TabIndex = 2;
			this.btNo.Text = "Restore";
			this.btNo.UseVisualStyleBackColor = true;
			this.btNo.Click += new System.EventHandler(this.btNo_Click);
			// 
			// btCancel
			// 
			this.btCancel.AutoSize = true;
			this.btCancel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.btCancel.Location = new System.Drawing.Point(650, 198);
			this.btCancel.Name = "btCancel";
			this.btCancel.Size = new System.Drawing.Size(50, 23);
			this.btCancel.TabIndex = 2;
			this.btCancel.Text = "Cancel";
			this.btCancel.UseVisualStyleBackColor = true;
			this.btCancel.Click += new System.EventHandler(this.btCancel_Click);
			// 
			// btSelectFile
			// 
			this.btSelectFile.AutoSize = true;
			this.btSelectFile.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.btSelectFile.Location = new System.Drawing.Point(634, 127);
			this.btSelectFile.Name = "btSelectFile";
			this.btSelectFile.Size = new System.Drawing.Size(66, 23);
			this.btSelectFile.TabIndex = 2;
			this.btSelectFile.Text = "Select File";
			this.btSelectFile.UseVisualStyleBackColor = true;
			this.btSelectFile.Click += new System.EventHandler(this.btSelectFile_Click);
			// 
			// lblYes
			// 
			this.lblYes.Location = new System.Drawing.Point(16, 35);
			this.lblYes.Name = "lblYes";
			this.lblYes.Size = new System.Drawing.Size(684, 32);
			this.lblYes.TabIndex = 3;
			this.lblYes.Text = resources.GetString("lblYes.Text");
			// 
			// lblNo
			// 
			this.lblNo.Location = new System.Drawing.Point(16, 70);
			this.lblNo.Name = "lblNo";
			this.lblNo.Size = new System.Drawing.Size(684, 31);
			this.lblNo.TabIndex = 4;
			this.lblNo.Text = "Click \'Restore\' if you want to restore the Virtual Install / Mod Installation (eg" +
    ". Data for Skyrim) folders from the backup WITHOUT deleting the previous files.";
			// 
			// lblCancel
			// 
			this.lblCancel.Location = new System.Drawing.Point(16, 108);
			this.lblCancel.Name = "lblCancel";
			this.lblCancel.Size = new System.Drawing.Size(593, 20);
			this.lblCancel.TabIndex = 6;
			this.lblCancel.Text = "Click CANCEL if you want to abort the operation.";
			// 
			// lblEstimated
			// 
			this.lblEstimated.Location = new System.Drawing.Point(16, 158);
			this.lblEstimated.Name = "lblEstimated";
			this.lblEstimated.Size = new System.Drawing.Size(456, 63);
			this.lblEstimated.TabIndex = 5;
			this.lblEstimated.Text = "Estimated Restore Size: ";
			this.lblEstimated.Visible = false;
			// 
			// tbFile
			// 
			this.tbFile.Location = new System.Drawing.Point(16, 130);
			this.tbFile.Name = "tbFile";
			this.tbFile.Size = new System.Drawing.Size(612, 20);
			this.tbFile.TabIndex = 2;
			// 
			// RestoreBackupForm
			// 
			this.ClientSize = new System.Drawing.Size(712, 227);
			this.Controls.Add(this.btYes);
			this.Controls.Add(this.btNo);
			this.Controls.Add(this.btCancel);
			this.Controls.Add(this.btSelectFile);
			this.Controls.Add(this.tbFile);
			this.Controls.Add(this.lblYes);
			this.Controls.Add(this.lblNo);
			this.Controls.Add(this.lblEstimated);
			this.Controls.Add(this.lblCancel);
			this.Name = "RestoreBackupForm";
			this.Text = "Restore Nexus Mod Manager";
			this.ResumeLayout(false);
			this.PerformLayout();

		}
						
		#endregion

		private Button btYes;
		private Button btNo;
		private Button btCancel;
		private Button btSelectFile;
		private Label lblAdminMode;
		private Label lblYes;
		private Label lblCancel;
		private Label lblNo;
		private Label lblEstimated;
		private OpenFileDialog fdFile;
		private TextBox tbFile;
	}
}