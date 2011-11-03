namespace Nexus.Client.Games
{
	partial class WorkingDirectorySelectionForm
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
			this.tbxWorkingDirectory = new System.Windows.Forms.TextBox();
			this.label2 = new System.Windows.Forms.Label();
			this.butSelect = new System.Windows.Forms.Button();
			this.butOK = new System.Windows.Forms.Button();
			this.butCancel = new System.Windows.Forms.Button();
			this.butAutoDetect = new System.Windows.Forms.Button();
			this.panel1 = new System.Windows.Forms.Panel();
			this.panel2 = new System.Windows.Forms.Panel();
			this.fbdWorkingDirectory = new System.Windows.Forms.FolderBrowserDialog();
			this.autosizeLabel1 = new Nexus.Client.Controls.AutosizeLabel();
			this.panel1.SuspendLayout();
			this.panel2.SuspendLayout();
			this.SuspendLayout();
			// 
			// tbxWorkingDirectory
			// 
			this.tbxWorkingDirectory.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.tbxWorkingDirectory.Location = new System.Drawing.Point(24, 19);
			this.tbxWorkingDirectory.Name = "tbxWorkingDirectory";
			this.tbxWorkingDirectory.Size = new System.Drawing.Size(519, 20);
			this.tbxWorkingDirectory.TabIndex = 0;
			// 
			// label2
			// 
			this.label2.AutoSize = true;
			this.label2.Location = new System.Drawing.Point(12, 3);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(35, 13);
			this.label2.TabIndex = 2;
			this.label2.Text = "label2";
			// 
			// butSelect
			// 
			this.butSelect.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.butSelect.AutoSize = true;
			this.butSelect.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.butSelect.Location = new System.Drawing.Point(549, 17);
			this.butSelect.Name = "butSelect";
			this.butSelect.Size = new System.Drawing.Size(26, 23);
			this.butSelect.TabIndex = 1;
			this.butSelect.Text = "...";
			this.butSelect.UseVisualStyleBackColor = true;
			this.butSelect.Click += new System.EventHandler(this.butSelect_Click);
			// 
			// butOK
			// 
			this.butOK.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butOK.Location = new System.Drawing.Point(419, 74);
			this.butOK.Name = "butOK";
			this.butOK.Size = new System.Drawing.Size(75, 23);
			this.butOK.TabIndex = 3;
			this.butOK.Text = "OK";
			this.butOK.UseVisualStyleBackColor = true;
			this.butOK.Click += new System.EventHandler(this.butOK_Click);
			// 
			// butCancel
			// 
			this.butCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this.butCancel.Location = new System.Drawing.Point(500, 74);
			this.butCancel.Name = "butCancel";
			this.butCancel.Size = new System.Drawing.Size(75, 23);
			this.butCancel.TabIndex = 4;
			this.butCancel.Text = "Cancel";
			this.butCancel.UseVisualStyleBackColor = true;
			// 
			// butAutoDetect
			// 
			this.butAutoDetect.Location = new System.Drawing.Point(500, 45);
			this.butAutoDetect.Name = "butAutoDetect";
			this.butAutoDetect.Size = new System.Drawing.Size(75, 23);
			this.butAutoDetect.TabIndex = 2;
			this.butAutoDetect.Text = "Auto Detect";
			this.butAutoDetect.UseVisualStyleBackColor = true;
			this.butAutoDetect.Click += new System.EventHandler(this.butAutoDetect_Click);
			// 
			// panel1
			// 
			this.panel1.AutoSize = true;
			this.panel1.Controls.Add(this.autosizeLabel1);
			this.panel1.Dock = System.Windows.Forms.DockStyle.Top;
			this.panel1.Location = new System.Drawing.Point(0, 0);
			this.panel1.Name = "panel1";
			this.panel1.Padding = new System.Windows.Forms.Padding(12, 9, 12, 3);
			this.panel1.Size = new System.Drawing.Size(587, 30);
			this.panel1.TabIndex = 7;
			// 
			// panel2
			// 
			this.panel2.Controls.Add(this.label2);
			this.panel2.Controls.Add(this.tbxWorkingDirectory);
			this.panel2.Controls.Add(this.butAutoDetect);
			this.panel2.Controls.Add(this.butSelect);
			this.panel2.Controls.Add(this.butCancel);
			this.panel2.Controls.Add(this.butOK);
			this.panel2.Dock = System.Windows.Forms.DockStyle.Top;
			this.panel2.Location = new System.Drawing.Point(0, 30);
			this.panel2.Name = "panel2";
			this.panel2.Size = new System.Drawing.Size(587, 109);
			this.panel2.TabIndex = 8;
			// 
			// autosizeLabel1
			// 
			this.autosizeLabel1.BackColor = System.Drawing.SystemColors.Control;
			this.autosizeLabel1.BorderStyle = System.Windows.Forms.BorderStyle.None;
			this.autosizeLabel1.Dock = System.Windows.Forms.DockStyle.Top;
			this.autosizeLabel1.Enabled = false;
			this.autosizeLabel1.Location = new System.Drawing.Point(12, 9);
			this.autosizeLabel1.Name = "autosizeLabel1";
			this.autosizeLabel1.ReadOnly = true;
			this.autosizeLabel1.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.None;
			this.autosizeLabel1.Size = new System.Drawing.Size(563, 18);
			this.autosizeLabel1.TabIndex = 0;
			this.autosizeLabel1.TabStop = false;
			this.autosizeLabel1.Text = "autosizeLabel1";
			// 
			// WorkingDirectorySelectionForm
			// 
			this.AcceptButton = this.butOK;
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.AutoSize = true;
			this.CancelButton = this.butCancel;
			this.ClientSize = new System.Drawing.Size(587, 139);
			this.Controls.Add(this.panel2);
			this.Controls.Add(this.panel1);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
			this.Name = "WorkingDirectorySelectionForm";
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
			this.Text = "Select Working Directory";
			this.panel1.ResumeLayout(false);
			this.panel2.ResumeLayout(false);
			this.panel2.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.TextBox tbxWorkingDirectory;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.Button butSelect;
		private System.Windows.Forms.Button butOK;
		private System.Windows.Forms.Button butCancel;
		private System.Windows.Forms.Button butAutoDetect;
		private System.Windows.Forms.Panel panel1;
		private Nexus.Client.Controls.AutosizeLabel autosizeLabel1;
		private System.Windows.Forms.Panel panel2;
		private System.Windows.Forms.FolderBrowserDialog fbdWorkingDirectory;
	}
}