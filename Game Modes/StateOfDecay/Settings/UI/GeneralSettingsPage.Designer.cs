namespace Nexus.Client.Games.StateOfDecay.Settings.UI
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
            this.label3 = new System.Windows.Forms.Label();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.tbxCommandArguments = new System.Windows.Forms.TextBox();
            this.tbxCommand = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.fbdWorkingDirectory = new System.Windows.Forms.FolderBrowserDialog();
            this.rdcDirectories = new Nexus.Client.Games.Settings.RequiredDirectoriesControl();
            this.erpErrors = new System.Windows.Forms.ErrorProvider(this.components);
            this.label1 = new System.Windows.Forms.Label();
            this.groupBox1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.erpErrors)).BeginInit();
            this.SuspendLayout();
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(16, 22);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(57, 13);
            this.label3.TabIndex = 6;
            this.label3.Text = "Command:";
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.tbxCommandArguments);
            this.groupBox1.Controls.Add(this.tbxCommand);
            this.groupBox1.Controls.Add(this.label4);
            this.groupBox1.Controls.Add(this.label3);
            this.groupBox1.Location = new System.Drawing.Point(24, 133);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(346, 78);
            this.groupBox1.TabIndex = 3;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Custom Launch Command";
            // 
            // tbxCommandArguments
            // 
            this.tbxCommandArguments.Location = new System.Drawing.Point(79, 45);
            this.tbxCommandArguments.Name = "tbxCommandArguments";
            this.tbxCommandArguments.Size = new System.Drawing.Size(257, 20);
            this.tbxCommandArguments.TabIndex = 5;
            // 
            // tbxCommand
            // 
            this.tbxCommand.Location = new System.Drawing.Point(79, 19);
            this.tbxCommand.Name = "tbxCommand";
            this.tbxCommand.Size = new System.Drawing.Size(257, 20);
            this.tbxCommand.TabIndex = 4;
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(13, 48);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(60, 13);
            this.label4.TabIndex = 7;
            this.label4.Text = "Arguments:";
            // 
            // rdcDirectories
            // 
            this.m_fpdFontProvider.SetFontSet(this.rdcDirectories, "StandardText");
            this.rdcDirectories.InstallInfoLabel = "Install Info*:";
            this.rdcDirectories.Location = new System.Drawing.Point(0, 3);
            this.rdcDirectories.ModDirectoryLabel = "Mod Directory*:";
            this.rdcDirectories.Name = "rdcDirectories";
            this.rdcDirectories.Size = new System.Drawing.Size(393, 85);
            this.rdcDirectories.TabIndex = 0;
            // 
            // erpErrors
            // 
            this.erpErrors.ContainerControl = this;
            // 
            // label1
            // 
            this.label1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.label1.AutoSize = true;
            this.m_fpdFontProvider.SetFontSet(this.label1, "StandardText");
            this.m_fpdFontProvider.SetFontStyle(this.label1, System.Drawing.FontStyle.Italic);
            this.label1.Location = new System.Drawing.Point(233, 272);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(137, 13);
            this.label1.TabIndex = 5;
            this.label1.Text = "* requires application restart";
            // 
            // GeneralSettingsPage
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.Transparent;
            this.Controls.Add(this.label1);
            this.Controls.Add(this.rdcDirectories);
            this.Controls.Add(this.groupBox1);
            this.Name = "GeneralSettingsPage";
            this.Size = new System.Drawing.Size(403, 307);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.erpErrors)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.TextBox tbxCommandArguments;
        private System.Windows.Forms.TextBox tbxCommand;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.FolderBrowserDialog fbdWorkingDirectory;
        private Nexus.Client.Games.Settings.RequiredDirectoriesControl rdcDirectories;
        private System.Windows.Forms.ErrorProvider erpErrors;
        private System.Windows.Forms.Label label1;

    }
}
