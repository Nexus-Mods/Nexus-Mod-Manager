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
			this.label5 = new System.Windows.Forms.Label();
			this.groupBox2 = new System.Windows.Forms.GroupBox();
			this.cbxConnections = new System.Windows.Forms.ComboBox();
			this.label1 = new System.Windows.Forms.Label();
			this.ckbPremiumOnly = new System.Windows.Forms.CheckBox();
			this.label6 = new System.Windows.Forms.Label();
			this.groupBox1.SuspendLayout();
			this.groupBox2.SuspendLayout();
			this.SuspendLayout();
			// 
			// groupBox1
			// 
			this.groupBox1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.groupBox1.Controls.Add(this.cbxServerLocation);
			this.groupBox1.Controls.Add(this.label5);
			this.groupBox1.Location = new System.Drawing.Point(3, 3);
			this.groupBox1.Name = "groupBox1";
			this.groupBox1.Size = new System.Drawing.Size(414, 64);
			this.groupBox1.TabIndex = 3;
			this.groupBox1.TabStop = false;
			this.groupBox1.Text = "Fileserver preferences";
			// 
			// cbxServerLocation
			// 
			this.cbxServerLocation.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed;
			this.cbxServerLocation.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.cbxServerLocation.FormattingEnabled = true;
			this.cbxServerLocation.Location = new System.Drawing.Point(111, 25);
			this.cbxServerLocation.Name = "cbxServerLocation";
			this.cbxServerLocation.Size = new System.Drawing.Size(186, 21);
			this.cbxServerLocation.TabIndex = 1;
			this.cbxServerLocation.DrawItem += new System.Windows.Forms.DrawItemEventHandler(this.cbxServerLocation_DrawItem);
			// 
			// label5
			// 
			this.label5.AutoSize = true;
			this.label5.Location = new System.Drawing.Point(24, 28);
			this.label5.Name = "label5";
			this.label5.Size = new System.Drawing.Size(85, 13);
			this.label5.TabIndex = 5;
			this.label5.Text = "Server Location:";
			// 
			// groupBox2
			// 
			this.groupBox2.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.groupBox2.Controls.Add(this.cbxConnections);
			this.groupBox2.Controls.Add(this.label1);
			this.groupBox2.Controls.Add(this.ckbPremiumOnly);
			this.groupBox2.Controls.Add(this.label6);
			this.groupBox2.Location = new System.Drawing.Point(3, 80);
			this.groupBox2.Name = "groupBox2";
			this.groupBox2.Size = new System.Drawing.Size(414, 92);
			this.groupBox2.TabIndex = 7;
			this.groupBox2.TabStop = false;
			this.groupBox2.Text = "Premium features";
			// 
			// cbxConnections
			// 
			this.cbxConnections.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.cbxConnections.FormattingEnabled = true;
			this.cbxConnections.Location = new System.Drawing.Point(111, 53);
			this.cbxConnections.Name = "cbxConnections";
			this.cbxConnections.Size = new System.Drawing.Size(186, 21);
			this.cbxConnections.TabIndex = 2;
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(6, 25);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(183, 13);
			this.label1.TabIndex = 4;
			this.label1.Text = "Only download from Premium servers:";
			// 
			// ckbPremiumOnly
			// 
			this.ckbPremiumOnly.AutoSize = true;
			this.ckbPremiumOnly.Location = new System.Drawing.Point(193, 25);
			this.ckbPremiumOnly.Name = "ckbPremiumOnly";
			this.ckbPremiumOnly.Size = new System.Drawing.Size(15, 14);
			this.ckbPremiumOnly.TabIndex = 0;
			this.ckbPremiumOnly.UseVisualStyleBackColor = true;
			// 
			// label6
			// 
			this.label6.AutoSize = true;
			this.label6.Location = new System.Drawing.Point(6, 56);
			this.label6.Name = "label6";
			this.label6.Size = new System.Drawing.Size(103, 13);
			this.label6.TabIndex = 6;
			this.label6.Text = "Connections per file:";
			// 
			// DownloadSettingsPage
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.groupBox1);
			this.Controls.Add(this.groupBox2);
			this.Name = "DownloadSettingsPage";
			this.Size = new System.Drawing.Size(420, 294);
			this.groupBox1.ResumeLayout(false);
			this.groupBox1.PerformLayout();
			this.groupBox2.ResumeLayout(false);
			this.groupBox2.PerformLayout();
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.GroupBox groupBox1;
		private System.Windows.Forms.GroupBox groupBox2;
		private System.Windows.Forms.ComboBox cbxServerLocation;
		private System.Windows.Forms.ComboBox cbxConnections;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.Label label5;
		private System.Windows.Forms.Label label6;
		private System.Windows.Forms.CheckBox ckbPremiumOnly;
	}
}
