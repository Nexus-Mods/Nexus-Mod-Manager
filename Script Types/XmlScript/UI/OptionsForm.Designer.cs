namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI
{
	partial class OptionsForm
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
			this.pnlBottom = new System.Windows.Forms.Panel();
			this.butBack = new System.Windows.Forms.Button();
			this.butNext = new System.Windows.Forms.Button();
			this.butCancel = new System.Windows.Forms.Button();
			this.pnlWizardSteps = new System.Windows.Forms.Panel();
			this.hplTitle = new Nexus.Client.ModManagement.Scripting.XmlScript.UI.HeaderPanel();
			this.pnlBottom.SuspendLayout();
			this.SuspendLayout();
			// 
			// pnlBottom
			// 
			this.pnlBottom.Controls.Add(this.butBack);
			this.pnlBottom.Controls.Add(this.butNext);
			this.pnlBottom.Controls.Add(this.butCancel);
			this.pnlBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.pnlBottom.Location = new System.Drawing.Point(0, 443);
			this.pnlBottom.Name = "pnlBottom";
			this.pnlBottom.Size = new System.Drawing.Size(587, 39);
			this.pnlBottom.TabIndex = 1;
			// 
			// butBack
			// 
			this.butBack.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butBack.Location = new System.Drawing.Point(342, 4);
			this.butBack.Name = "butBack";
			this.butBack.Size = new System.Drawing.Size(75, 23);
			this.butBack.TabIndex = 2;
			this.butBack.Text = "< Back";
			this.butBack.UseVisualStyleBackColor = true;
			this.butBack.Click += new System.EventHandler(this.butBack_Click);
			// 
			// butNext
			// 
			this.butNext.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butNext.Location = new System.Drawing.Point(419, 4);
			this.butNext.Name = "butNext";
			this.butNext.Size = new System.Drawing.Size(75, 23);
			this.butNext.TabIndex = 0;
			this.butNext.Text = "Next >";
			this.butNext.UseVisualStyleBackColor = true;
			this.butNext.Click += new System.EventHandler(this.butNext_Click);
			// 
			// butCancel
			// 
			this.butCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this.butCancel.Location = new System.Drawing.Point(500, 4);
			this.butCancel.Name = "butCancel";
			this.butCancel.Size = new System.Drawing.Size(75, 23);
			this.butCancel.TabIndex = 1;
			this.butCancel.Text = "Cancel";
			this.butCancel.UseVisualStyleBackColor = true;
			this.butCancel.Click += new System.EventHandler(this.butCancel_Click);
			// 
			// pnlWizardSteps
			// 
			this.pnlWizardSteps.Dock = System.Windows.Forms.DockStyle.Fill;
			this.pnlWizardSteps.Location = new System.Drawing.Point(0, 47);
			this.pnlWizardSteps.Name = "pnlWizardSteps";
			this.pnlWizardSteps.Size = new System.Drawing.Size(587, 396);
			this.pnlWizardSteps.TabIndex = 22;
			// 
			// hplTitle
			// 
			this.hplTitle.Dock = System.Windows.Forms.DockStyle.Top;
			this.m_fpdFontProvider.SetFontSet(this.hplTitle, "StandardText");
			this.m_fpdFontProvider.SetFontSize(this.hplTitle, 14F);
			this.m_fpdFontProvider.SetFontStyle(this.hplTitle, System.Drawing.FontStyle.Bold);
			this.hplTitle.ImageLocation = null;
			this.hplTitle.Location = new System.Drawing.Point(0, 0);
			this.hplTitle.Name = "hplTitle";
			this.hplTitle.ShowFade = false;
			this.hplTitle.Size = new System.Drawing.Size(587, 47);
			this.hplTitle.TabIndex = 21;
			this.hplTitle.Text = "Generic Options Form";
			this.hplTitle.TextPosition = Nexus.Client.ModManagement.Scripting.XmlScript.TextPosition.Left;
			// 
			// OptionsForm
			// 
			this.AcceptButton = this.butNext;
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.CancelButton = this.butCancel;
			this.ClientSize = new System.Drawing.Size(587, 482);
			this.Controls.Add(this.pnlWizardSteps);
			this.Controls.Add(this.hplTitle);
			this.Controls.Add(this.pnlBottom);
			this.m_fpdFontProvider.SetFontSet(this, "StandardText");
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
			this.Text = "Options Form";
			this.pnlBottom.ResumeLayout(false);
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.Panel pnlBottom;
		private System.Windows.Forms.Button butCancel;
		private HeaderPanel hplTitle;
		private System.Windows.Forms.Button butBack;
		private System.Windows.Forms.Button butNext;
		private System.Windows.Forms.Panel pnlWizardSteps;
	}
}