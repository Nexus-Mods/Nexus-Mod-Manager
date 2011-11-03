namespace Nexus.Client.Settings.UI
{
	partial class GeneralSettingsPage
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
			this.flpGeneral = new System.Windows.Forms.FlowLayoutPanel();
			this.gbxAssociations = new System.Windows.Forms.GroupBox();
			this.flpFileAssociations = new System.Windows.Forms.FlowLayoutPanel();
			this.ckbShellExtensions = new System.Windows.Forms.CheckBox();
			this.ckbAssociateURL = new System.Windows.Forms.CheckBox();
			this.groupBox5 = new System.Windows.Forms.GroupBox();
			this.ckbAddMissingInfo = new System.Windows.Forms.CheckBox();
			this.ckbCheckModVersions = new System.Windows.Forms.CheckBox();
			this.ckbCheckForUpdates = new System.Windows.Forms.CheckBox();
			this.ttpTip = new System.Windows.Forms.ToolTip(this.components);
			this.ckbScanSubfolders = new System.Windows.Forms.CheckBox();
			this.flpGeneral.SuspendLayout();
			this.gbxAssociations.SuspendLayout();
			this.flpFileAssociations.SuspendLayout();
			this.groupBox5.SuspendLayout();
			this.SuspendLayout();
			// 
			// flpGeneral
			// 
			this.flpGeneral.AutoScroll = true;
			this.flpGeneral.Controls.Add(this.gbxAssociations);
			this.flpGeneral.Controls.Add(this.groupBox5);
			this.flpGeneral.Dock = System.Windows.Forms.DockStyle.Fill;
			this.flpGeneral.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
			this.flpGeneral.Location = new System.Drawing.Point(0, 0);
			this.flpGeneral.Name = "flpGeneral";
			this.flpGeneral.Size = new System.Drawing.Size(398, 294);
			this.flpGeneral.TabIndex = 25;
			this.flpGeneral.WrapContents = false;
			this.flpGeneral.MouseMove += new System.Windows.Forms.MouseEventHandler(this.flpGeneral_MouseMove);
			this.flpGeneral.MouseHover += new System.EventHandler(this.flpGeneral_MouseHover);
			// 
			// gbxAssociations
			// 
			this.gbxAssociations.AutoSize = true;
			this.gbxAssociations.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.gbxAssociations.Controls.Add(this.flpFileAssociations);
			this.gbxAssociations.Location = new System.Drawing.Point(3, 3);
			this.gbxAssociations.MinimumSize = new System.Drawing.Size(368, 0);
			this.gbxAssociations.Name = "gbxAssociations";
			this.gbxAssociations.Size = new System.Drawing.Size(368, 71);
			this.gbxAssociations.TabIndex = 22;
			this.gbxAssociations.TabStop = false;
			this.gbxAssociations.Text = "Associations";
			// 
			// flpFileAssociations
			// 
			this.flpFileAssociations.AutoSize = true;
			this.flpFileAssociations.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.flpFileAssociations.Controls.Add(this.ckbShellExtensions);
			this.flpFileAssociations.Controls.Add(this.ckbAssociateURL);
			this.flpFileAssociations.Dock = System.Windows.Forms.DockStyle.Fill;
			this.flpFileAssociations.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
			this.flpFileAssociations.Location = new System.Drawing.Point(3, 16);
			this.flpFileAssociations.Name = "flpFileAssociations";
			this.flpFileAssociations.Padding = new System.Windows.Forms.Padding(10, 3, 3, 3);
			this.flpFileAssociations.Size = new System.Drawing.Size(362, 52);
			this.flpFileAssociations.TabIndex = 4;
			// 
			// ckbShellExtensions
			// 
			this.ckbShellExtensions.AutoSize = true;
			this.ckbShellExtensions.Location = new System.Drawing.Point(13, 6);
			this.ckbShellExtensions.Name = "ckbShellExtensions";
			this.ckbShellExtensions.Size = new System.Drawing.Size(231, 17);
			this.ckbShellExtensions.TabIndex = 3;
			this.ckbShellExtensions.Text = "Add shell extensions for supported file types";
			this.ckbShellExtensions.UseVisualStyleBackColor = true;
			// 
			// ckbAssociateURL
			// 
			this.ckbAssociateURL.AutoSize = true;
			this.ckbAssociateURL.Location = new System.Drawing.Point(13, 29);
			this.ckbAssociateURL.Name = "ckbAssociateURL";
			this.ckbAssociateURL.Size = new System.Drawing.Size(151, 17);
			this.ckbAssociateURL.TabIndex = 4;
			this.ckbAssociateURL.Text = "Associate with NXM URLs";
			this.ckbAssociateURL.UseVisualStyleBackColor = true;
			// 
			// groupBox5
			// 
			this.groupBox5.Controls.Add(this.ckbScanSubfolders);
			this.groupBox5.Controls.Add(this.ckbAddMissingInfo);
			this.groupBox5.Controls.Add(this.ckbCheckModVersions);
			this.groupBox5.Controls.Add(this.ckbCheckForUpdates);
			this.groupBox5.Location = new System.Drawing.Point(3, 80);
			this.groupBox5.Name = "groupBox5";
			this.groupBox5.Size = new System.Drawing.Size(368, 112);
			this.groupBox5.TabIndex = 23;
			this.groupBox5.TabStop = false;
			this.groupBox5.Text = "Options";
			// 
			// ckbAddMissingInfo
			// 
			this.ckbAddMissingInfo.AutoSize = true;
			this.ckbAddMissingInfo.Location = new System.Drawing.Point(16, 65);
			this.ckbAddMissingInfo.Name = "ckbAddMissingInfo";
			this.ckbAddMissingInfo.Size = new System.Drawing.Size(143, 17);
			this.ckbAddMissingInfo.TabIndex = 2;
			this.ckbAddMissingInfo.Text = "Add missing info to Mods";
			this.ckbAddMissingInfo.UseVisualStyleBackColor = true;
			// 
			// ckbCheckModVersions
			// 
			this.ckbCheckModVersions.AutoSize = true;
			this.ckbCheckModVersions.Location = new System.Drawing.Point(16, 42);
			this.ckbCheckModVersions.Name = "ckbCheckModVersions";
			this.ckbCheckModVersions.Size = new System.Drawing.Size(161, 17);
			this.ckbCheckModVersions.TabIndex = 1;
			this.ckbCheckModVersions.Text = "Check for new Mod versions";
			this.ckbCheckModVersions.UseVisualStyleBackColor = true;
			// 
			// ckbCheckForUpdates
			// 
			this.ckbCheckForUpdates.AutoSize = true;
			this.ckbCheckForUpdates.Location = new System.Drawing.Point(16, 19);
			this.ckbCheckForUpdates.Name = "ckbCheckForUpdates";
			this.ckbCheckForUpdates.Size = new System.Drawing.Size(163, 17);
			this.ckbCheckForUpdates.TabIndex = 0;
			this.ckbCheckForUpdates.Text = "Check for updates on startup";
			this.ckbCheckForUpdates.UseVisualStyleBackColor = true;
			// 
			// ckbScanSubfolders
			// 
			this.ckbScanSubfolders.AutoSize = true;
			this.ckbScanSubfolders.Location = new System.Drawing.Point(16, 88);
			this.ckbScanSubfolders.Name = "ckbScanSubfolders";
			this.ckbScanSubfolders.Size = new System.Drawing.Size(217, 17);
			this.ckbScanSubfolders.TabIndex = 3;
			this.ckbScanSubfolders.Text = "Scan Mods directory subfolders for mods";
			this.ckbScanSubfolders.UseVisualStyleBackColor = true;
			// 
			// GeneralSettingsPage
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.flpGeneral);
			this.Name = "GeneralSettingsPage";
			this.Size = new System.Drawing.Size(398, 294);
			this.flpGeneral.ResumeLayout(false);
			this.flpGeneral.PerformLayout();
			this.gbxAssociations.ResumeLayout(false);
			this.gbxAssociations.PerformLayout();
			this.flpFileAssociations.ResumeLayout(false);
			this.flpFileAssociations.PerformLayout();
			this.groupBox5.ResumeLayout(false);
			this.groupBox5.PerformLayout();
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.FlowLayoutPanel flpGeneral;
		private System.Windows.Forms.GroupBox gbxAssociations;
		private System.Windows.Forms.FlowLayoutPanel flpFileAssociations;
		private System.Windows.Forms.CheckBox ckbShellExtensions;
		private System.Windows.Forms.GroupBox groupBox5;
		private System.Windows.Forms.CheckBox ckbAddMissingInfo;
		private System.Windows.Forms.CheckBox ckbCheckModVersions;
		private System.Windows.Forms.ToolTip ttpTip;
		private System.Windows.Forms.CheckBox ckbAssociateURL;
		private System.Windows.Forms.CheckBox ckbCheckForUpdates;
		private System.Windows.Forms.CheckBox ckbScanSubfolders;

	}
}
