namespace Nexus.Client.Settings.UI
{
    partial class OsSettingsPage
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
            this.ckbAssociateURL = new System.Windows.Forms.CheckBox();
            this.groupBoxShellExtensions = new System.Windows.Forms.GroupBox();
            this.checkBoxShell7z = new System.Windows.Forms.CheckBox();
            this.checkBoxShellRar = new System.Windows.Forms.CheckBox();
            this.checkBoxShellZip = new System.Windows.Forms.CheckBox();
            this.ttpTip = new System.Windows.Forms.ToolTip(this.components);
            this.flpGeneral.SuspendLayout();
            this.gbxAssociations.SuspendLayout();
            this.flpFileAssociations.SuspendLayout();
            this.groupBoxShellExtensions.SuspendLayout();
            this.SuspendLayout();
            // 
            // flpGeneral
            // 
            this.flpGeneral.AutoScroll = true;
            this.flpGeneral.Controls.Add(this.gbxAssociations);
            this.flpGeneral.Controls.Add(this.groupBoxShellExtensions);
            this.flpGeneral.Dock = System.Windows.Forms.DockStyle.Fill;
            this.flpGeneral.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.flpGeneral.Location = new System.Drawing.Point(0, 0);
            this.flpGeneral.Name = "flpGeneral";
            this.flpGeneral.Size = new System.Drawing.Size(398, 294);
            this.flpGeneral.TabIndex = 25;
            this.flpGeneral.WrapContents = false;
            this.flpGeneral.MouseHover += new System.EventHandler(this.flpGeneral_MouseHover);
            this.flpGeneral.MouseMove += new System.Windows.Forms.MouseEventHandler(this.flpGeneral_MouseMove);
            // 
            // gbxAssociations
            // 
            this.gbxAssociations.AutoSize = true;
            this.gbxAssociations.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.gbxAssociations.Controls.Add(this.flpFileAssociations);
            this.gbxAssociations.Location = new System.Drawing.Point(3, 3);
            this.gbxAssociations.MinimumSize = new System.Drawing.Size(368, 0);
            this.gbxAssociations.Name = "gbxAssociations";
            this.gbxAssociations.Size = new System.Drawing.Size(368, 48);
            this.gbxAssociations.TabIndex = 22;
            this.gbxAssociations.TabStop = false;
            this.gbxAssociations.Text = "General";
            // 
            // flpFileAssociations
            // 
            this.flpFileAssociations.AutoSize = true;
            this.flpFileAssociations.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.flpFileAssociations.Controls.Add(this.ckbAssociateURL);
            this.flpFileAssociations.Dock = System.Windows.Forms.DockStyle.Fill;
            this.flpFileAssociations.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.flpFileAssociations.Location = new System.Drawing.Point(3, 16);
            this.flpFileAssociations.Name = "flpFileAssociations";
            this.flpFileAssociations.Padding = new System.Windows.Forms.Padding(10, 3, 3, 3);
            this.flpFileAssociations.Size = new System.Drawing.Size(362, 29);
            this.flpFileAssociations.TabIndex = 4;
            // 
            // ckbAssociateURL
            // 
            this.ckbAssociateURL.AutoSize = true;
            this.ckbAssociateURL.Location = new System.Drawing.Point(13, 6);
            this.ckbAssociateURL.Name = "ckbAssociateURL";
            this.ckbAssociateURL.Size = new System.Drawing.Size(151, 17);
            this.ckbAssociateURL.TabIndex = 6;
            this.ckbAssociateURL.Text = "Associate with NXM URLs";
            this.ckbAssociateURL.UseVisualStyleBackColor = true;
            // 
            // groupBoxShellExtensions
            // 
            this.groupBoxShellExtensions.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.groupBoxShellExtensions.Controls.Add(this.checkBoxShell7z);
            this.groupBoxShellExtensions.Controls.Add(this.checkBoxShellRar);
            this.groupBoxShellExtensions.Controls.Add(this.checkBoxShellZip);
            this.groupBoxShellExtensions.Location = new System.Drawing.Point(3, 57);
            this.groupBoxShellExtensions.Name = "groupBoxShellExtensions";
            this.groupBoxShellExtensions.Size = new System.Drawing.Size(365, 100);
            this.groupBoxShellExtensions.TabIndex = 23;
            this.groupBoxShellExtensions.TabStop = false;
            this.groupBoxShellExtensions.Text = "Shell extensions";
            // 
            // checkBoxShell7z
            // 
            this.checkBoxShell7z.AutoSize = true;
            this.checkBoxShell7z.Location = new System.Drawing.Point(16, 66);
            this.checkBoxShell7z.Name = "checkBoxShell7z";
            this.checkBoxShell7z.Size = new System.Drawing.Size(199, 17);
            this.checkBoxShell7z.TabIndex = 2;
            this.checkBoxShell7z.Text = "Add NMM shell extension for .7z files";
            this.checkBoxShell7z.UseVisualStyleBackColor = true;
            // 
            // checkBoxShellRar
            // 
            this.checkBoxShellRar.AutoSize = true;
            this.checkBoxShellRar.Location = new System.Drawing.Point(16, 43);
            this.checkBoxShellRar.Name = "checkBoxShellRar";
            this.checkBoxShellRar.Size = new System.Drawing.Size(200, 17);
            this.checkBoxShellRar.TabIndex = 1;
            this.checkBoxShellRar.Text = "Add NMM shell extension for .rar files";
            this.checkBoxShellRar.UseVisualStyleBackColor = true;
            // 
            // checkBoxShellZip
            // 
            this.checkBoxShellZip.AutoSize = true;
            this.checkBoxShellZip.Location = new System.Drawing.Point(16, 20);
            this.checkBoxShellZip.Name = "checkBoxShellZip";
            this.checkBoxShellZip.Size = new System.Drawing.Size(201, 17);
            this.checkBoxShellZip.TabIndex = 0;
            this.checkBoxShellZip.Text = "Add NMM shell extension for .zip files";
            this.checkBoxShellZip.UseVisualStyleBackColor = true;
            // 
            // OsSettingsPage
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.flpGeneral);
            this.Name = "OsSettingsPage";
            this.Size = new System.Drawing.Size(398, 294);
            this.flpGeneral.ResumeLayout(false);
            this.flpGeneral.PerformLayout();
            this.gbxAssociations.ResumeLayout(false);
            this.gbxAssociations.PerformLayout();
            this.flpFileAssociations.ResumeLayout(false);
            this.flpFileAssociations.PerformLayout();
            this.groupBoxShellExtensions.ResumeLayout(false);
            this.groupBoxShellExtensions.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.FlowLayoutPanel flpGeneral;
        private System.Windows.Forms.GroupBox gbxAssociations;
        private System.Windows.Forms.FlowLayoutPanel flpFileAssociations;
        private System.Windows.Forms.ToolTip ttpTip;
        private System.Windows.Forms.CheckBox ckbAssociateURL;
        private System.Windows.Forms.GroupBox groupBoxShellExtensions;
        private System.Windows.Forms.CheckBox checkBoxShell7z;
        private System.Windows.Forms.CheckBox checkBoxShellRar;
        private System.Windows.Forms.CheckBox checkBoxShellZip;
    }
}
