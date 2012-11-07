namespace Nexus.Client.Settings.UI
{
	partial class DownloadSettingsPage
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
			this.groupBox1 = new System.Windows.Forms.GroupBox();
			this.cbxServerLocation = new System.Windows.Forms.ComboBox();
			this.cbxConnections = new System.Windows.Forms.ComboBox();
			this.label1 = new System.Windows.Forms.Label();
			this.label5 = new System.Windows.Forms.Label();
			this.label6 = new System.Windows.Forms.Label();
			this.ckbPremiumOnly = new System.Windows.Forms.CheckBox();
			this.groupBox1.SuspendLayout();
			this.SuspendLayout();
			// 
			// groupBox1
			// 
			this.groupBox1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.groupBox1.Controls.Add(this.cbxServerLocation);
			this.groupBox1.Controls.Add(this.cbxConnections);
			this.groupBox1.Controls.Add(this.label1);
			this.groupBox1.Controls.Add(this.ckbPremiumOnly);
			this.groupBox1.Controls.Add(this.label5);
			this.groupBox1.Controls.Add(this.label6);
			this.groupBox1.Location = new System.Drawing.Point(3, 3);
			this.groupBox1.Name = "groupBox1";
			this.groupBox1.Size = new System.Drawing.Size(414, 112);
			this.groupBox1.TabIndex = 3;
			this.groupBox1.TabStop = false;
			this.groupBox1.Text = "Fileserver preferences";
			// 
			// cbxServerLocation
			// 
			this.cbxServerLocation.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.cbxServerLocation.FormattingEnabled = true;
			this.cbxServerLocation.Location = new System.Drawing.Point(111, 50);
			this.cbxServerLocation.Name = "cbxServerLocation";
			this.cbxServerLocation.Size = new System.Drawing.Size(186, 21);
			this.cbxServerLocation.TabIndex = 1;
			// 
			// cbxConnections
			// 
			this.cbxConnections.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.cbxConnections.FormattingEnabled = true;
			this.cbxConnections.Location = new System.Drawing.Point(111, 77);
			this.cbxConnections.Name = "cbxConnections";
			this.cbxConnections.Size = new System.Drawing.Size(186, 21);
			this.cbxConnections.TabIndex = 2;
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(27, 26);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(77, 13);
			this.label1.TabIndex = 4;
			this.label1.Text = "Premium Only:";
			//
			// ckbPremiumOnly
			//
			this.ckbPremiumOnly.AutoSize = true;
			this.ckbPremiumOnly.Location = new System.Drawing.Point(111, 26);
			this.ckbPremiumOnly.Name = "ckbPremiumOnly";
			this.ckbPremiumOnly.Size = new System.Drawing.Size(163, 17);
			this.ckbPremiumOnly.TabIndex = 0;
			this.ckbPremiumOnly.Text = "";
			this.ckbPremiumOnly.UseVisualStyleBackColor = true;
			// 
			// label5
			// 
			this.label5.AutoSize = true;
			this.label5.Location = new System.Drawing.Point(24, 53);
			this.label5.Name = "label5";
			this.label5.Size = new System.Drawing.Size(77, 13);
			this.label5.TabIndex = 5;
			this.label5.Text = "Server Location:";
			// 
			// label6
			// 
			this.label6.AutoSize = true;
			this.label6.Location = new System.Drawing.Point(6, 80);
			this.label6.Name = "label6";
			this.label6.Size = new System.Drawing.Size(99, 13);
			this.label6.TabIndex = 6;
			this.label6.Text = "Connections per file:";
			// 
			// ModOptionsPage
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.groupBox1);
			this.Name = "DownloadSettingsPage";
			this.Size = new System.Drawing.Size(420, 294);
			this.groupBox1.ResumeLayout(false);
			this.groupBox1.PerformLayout();
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.GroupBox groupBox1;
		private System.Windows.Forms.ComboBox cbxServerLocation;
		private System.Windows.Forms.ComboBox cbxConnections;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.Label label5;
		private System.Windows.Forms.Label label6;
		private System.Windows.Forms.CheckBox ckbPremiumOnly;
	}
}
