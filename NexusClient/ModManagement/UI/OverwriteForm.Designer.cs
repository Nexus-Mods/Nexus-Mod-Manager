namespace Nexus.Client.ModManagement.UI
{
	partial class OverwriteForm
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
			this.butYesToAll = new System.Windows.Forms.Button();
			this.butYesToGroup = new System.Windows.Forms.Button();
			this.butYesToMod = new System.Windows.Forms.Button();
			this.butYes = new System.Windows.Forms.Button();
			this.butNoToAll = new System.Windows.Forms.Button();
			this.butNoToGroup = new System.Windows.Forms.Button();
			this.butNoToMod = new System.Windows.Forms.Button();
			this.butNo = new System.Windows.Forms.Button();
			this.lblMessage = new System.Windows.Forms.Label();
			this.panel1 = new System.Windows.Forms.Panel();
			this.panel2 = new System.Windows.Forms.Panel();
			this.panel1.SuspendLayout();
			this.panel2.SuspendLayout();
			this.SuspendLayout();
			// 
			// butYesToAll
			// 
			this.butYesToAll.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butYesToAll.Location = new System.Drawing.Point(14, 4);
			this.butYesToAll.Name = "butYesToAll";
			this.butYesToAll.Size = new System.Drawing.Size(75, 23);
			this.butYesToAll.TabIndex = 1;
			this.butYesToAll.Text = "Yes to all";
			this.butYesToAll.UseVisualStyleBackColor = true;
			this.butYesToAll.Click += new System.EventHandler(this.Button_Click);
			// 
			// butYesToGroup
			// 
			this.butYesToGroup.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butYesToGroup.Location = new System.Drawing.Point(95, 4);
			this.butYesToGroup.Name = "butYesToGroup";
			this.butYesToGroup.Size = new System.Drawing.Size(75, 23);
			this.butYesToGroup.TabIndex = 2;
			this.butYesToGroup.Text = "Yes to folder";
			this.butYesToGroup.UseVisualStyleBackColor = true;
			this.butYesToGroup.Click += new System.EventHandler(this.Button_Click);
			// 
			// butYesToMod
			// 
			this.butYesToMod.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butYesToMod.Location = new System.Drawing.Point(176, 4);
			this.butYesToMod.Name = "butYesToMod";
			this.butYesToMod.Size = new System.Drawing.Size(75, 23);
			this.butYesToMod.TabIndex = 3;
			this.butYesToMod.Text = "Yes to Mod";
			this.butYesToMod.UseVisualStyleBackColor = true;
			this.butYesToMod.Click += new System.EventHandler(this.Button_Click);
			// 
			// butYes
			// 
			this.butYes.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butYes.Location = new System.Drawing.Point(257, 4);
			this.butYes.Name = "butYes";
			this.butYes.Size = new System.Drawing.Size(75, 23);
			this.butYes.TabIndex = 4;
			this.butYes.Text = "Yes";
			this.butYes.UseVisualStyleBackColor = true;
			this.butYes.Click += new System.EventHandler(this.Button_Click);
			// 
			// butNoToAll
			// 
			this.butNoToAll.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butNoToAll.Location = new System.Drawing.Point(338, 4);
			this.butNoToAll.Name = "butNoToAll";
			this.butNoToAll.Size = new System.Drawing.Size(75, 23);
			this.butNoToAll.TabIndex = 5;
			this.butNoToAll.Text = "No to all";
			this.butNoToAll.UseVisualStyleBackColor = true;
			this.butNoToAll.Click += new System.EventHandler(this.Button_Click);
			// 
			// butNoToGroup
			// 
			this.butNoToGroup.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butNoToGroup.Location = new System.Drawing.Point(419, 4);
			this.butNoToGroup.Name = "butNoToGroup";
			this.butNoToGroup.Size = new System.Drawing.Size(75, 23);
			this.butNoToGroup.TabIndex = 6;
			this.butNoToGroup.Text = "No to folder";
			this.butNoToGroup.UseVisualStyleBackColor = true;
			this.butNoToGroup.Click += new System.EventHandler(this.Button_Click);
			// 
			// butNoToMod
			// 
			this.butNoToMod.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butNoToMod.Location = new System.Drawing.Point(500, 4);
			this.butNoToMod.Name = "butNoToMod";
			this.butNoToMod.Size = new System.Drawing.Size(75, 23);
			this.butNoToMod.TabIndex = 7;
			this.butNoToMod.Text = "No to Mod";
			this.butNoToMod.UseVisualStyleBackColor = true;
			this.butNoToMod.Click += new System.EventHandler(this.Button_Click);
			// 
			// butNo
			// 
			this.butNo.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butNo.Location = new System.Drawing.Point(581, 4);
			this.butNo.Name = "butNo";
			this.butNo.Size = new System.Drawing.Size(75, 23);
			this.butNo.TabIndex = 8;
			this.butNo.Text = "No";
			this.butNo.UseVisualStyleBackColor = true;
			this.butNo.Click += new System.EventHandler(this.Button_Click);
			// 
			// lblMessage
			// 
			this.lblMessage.AutoSize = true;
			this.lblMessage.Location = new System.Drawing.Point(11, 9);
			this.lblMessage.MinimumSize = new System.Drawing.Size(70, 27);
			this.lblMessage.Name = "lblMessage";
			this.lblMessage.Size = new System.Drawing.Size(70, 27);
			this.lblMessage.TabIndex = 9;
			this.lblMessage.Text = "label1";
			// 
			// panel1
			// 
			this.panel1.AutoSize = true;
			this.panel1.Controls.Add(this.butNo);
			this.panel1.Controls.Add(this.butYesToAll);
			this.panel1.Controls.Add(this.butNoToGroup);
			this.panel1.Controls.Add(this.butYesToGroup);
			this.panel1.Controls.Add(this.butNoToMod);
			this.panel1.Controls.Add(this.butYesToMod);
			this.panel1.Controls.Add(this.butNoToAll);
			this.panel1.Controls.Add(this.butYes);
			this.panel1.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.panel1.Location = new System.Drawing.Point(0, 69);
			this.panel1.Name = "panel1";
			this.panel1.Size = new System.Drawing.Size(670, 39);
			this.panel1.TabIndex = 10;
			// 
			// panel2
			// 
			this.panel2.AutoSize = true;
			this.panel2.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.panel2.Controls.Add(this.lblMessage);
			this.panel2.Dock = System.Windows.Forms.DockStyle.Fill;
			this.panel2.Location = new System.Drawing.Point(0, 0);
			this.panel2.Name = "panel2";
			this.panel2.Size = new System.Drawing.Size(670, 69);
			this.panel2.TabIndex = 11;
			// 
			// OverwriteForm
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.AutoSize = true;
			this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.ClientSize = new System.Drawing.Size(670, 108);
			this.ControlBox = false;
			this.Controls.Add(this.panel2);
			this.Controls.Add(this.panel1);
			this.m_fpdFontProvider.SetFontSet(this, "StandardText");
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
			this.KeyPreview = true;
			this.MinimumSize = new System.Drawing.Size(670, 28);
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
			this.Text = "Confirm Overwrite";
			this.panel1.ResumeLayout(false);
			this.panel2.ResumeLayout(false);
			this.panel2.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Button butYesToAll;
		private System.Windows.Forms.Button butYesToGroup;
		private System.Windows.Forms.Button butYesToMod;
		private System.Windows.Forms.Button butYes;
		private System.Windows.Forms.Button butNoToAll;
		private System.Windows.Forms.Button butNoToGroup;
		private System.Windows.Forms.Button butNoToMod;
		private System.Windows.Forms.Button butNo;
		private System.Windows.Forms.Label lblMessage;
		private System.Windows.Forms.Panel panel1;
		private System.Windows.Forms.Panel panel2;
	}
}