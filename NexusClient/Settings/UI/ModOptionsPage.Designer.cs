namespace Nexus.Client.Settings.UI
{
	partial class ModOptionsPage
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
			this.label9 = new System.Windows.Forms.Label();
			this.cbxModFormat = new System.Windows.Forms.ComboBox();
			this.cbxModCompression = new System.Windows.Forms.ComboBox();
			this.label5 = new System.Windows.Forms.Label();
			this.label6 = new System.Windows.Forms.Label();
			this.groupBox1.SuspendLayout();
			this.SuspendLayout();
			// 
			// groupBox1
			// 
			this.groupBox1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.groupBox1.Controls.Add(this.label9);
			this.groupBox1.Controls.Add(this.cbxModFormat);
			this.groupBox1.Controls.Add(this.cbxModCompression);
			this.groupBox1.Controls.Add(this.label5);
			this.groupBox1.Controls.Add(this.label6);
			this.groupBox1.Location = new System.Drawing.Point(3, 3);
			this.groupBox1.Name = "groupBox1";
			this.groupBox1.Size = new System.Drawing.Size(414, 112);
			this.groupBox1.TabIndex = 2;
			this.groupBox1.TabStop = false;
			this.groupBox1.Text = "FOMod Compression";
			// 
			// label9
			// 
			this.label9.Location = new System.Drawing.Point(22, 16);
			this.label9.Name = "label9";
			this.label9.Size = new System.Drawing.Size(354, 31);
			this.label9.TabIndex = 3;
			this.label9.Text = "NOTE: Using a format other than Zip can make the Package Manager respond slowly.";
			// 
			// cbxFomodFormat
			// 
			this.cbxModFormat.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.cbxModFormat.FormattingEnabled = true;
			this.cbxModFormat.Location = new System.Drawing.Point(111, 50);
			this.cbxModFormat.Name = "cbxFomodFormat";
			this.cbxModFormat.Size = new System.Drawing.Size(186, 21);
			this.cbxModFormat.TabIndex = 0;
			// 
			// cbxFomodCompression
			// 
			this.cbxModCompression.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.cbxModCompression.FormattingEnabled = true;
			this.cbxModCompression.Location = new System.Drawing.Point(111, 77);
			this.cbxModCompression.Name = "cbxFomodCompression";
			this.cbxModCompression.Size = new System.Drawing.Size(186, 21);
			this.cbxModCompression.TabIndex = 1;
			// 
			// label5
			// 
			this.label5.AutoSize = true;
			this.label5.Location = new System.Drawing.Point(63, 53);
			this.label5.Name = "label5";
			this.label5.Size = new System.Drawing.Size(42, 13);
			this.label5.TabIndex = 0;
			this.label5.Text = "Format:";
			// 
			// label6
			// 
			this.label6.AutoSize = true;
			this.label6.Location = new System.Drawing.Point(6, 80);
			this.label6.Name = "label6";
			this.label6.Size = new System.Drawing.Size(99, 13);
			this.label6.TabIndex = 2;
			this.label6.Text = "Compression Level:";
			// 
			// ModOptionsPage
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.groupBox1);
			this.Name = "ModOptionsPage";
			this.Size = new System.Drawing.Size(420, 294);
			this.groupBox1.ResumeLayout(false);
			this.groupBox1.PerformLayout();
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.GroupBox groupBox1;
		private System.Windows.Forms.Label label9;
		private System.Windows.Forms.ComboBox cbxModFormat;
		private System.Windows.Forms.ComboBox cbxModCompression;
		private System.Windows.Forms.Label label5;
		private System.Windows.Forms.Label label6;
	}
}
