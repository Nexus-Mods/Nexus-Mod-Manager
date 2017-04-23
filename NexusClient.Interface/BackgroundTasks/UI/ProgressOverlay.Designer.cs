﻿namespace Nexus.Client.BackgroundTasks.UI
{
    partial class ProgressOverlay
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
            this.pbrItemProgress = new System.Windows.Forms.ProgressBar();
            this.lblItemMessage = new System.Windows.Forms.Label();
            this.pnlItemProgress = new System.Windows.Forms.Panel();
            this.pbrTotalProgress = new System.Windows.Forms.ProgressBar();
            this.pnlTotalProgress = new System.Windows.Forms.Panel();
            this.lblTotalMessage = new System.Windows.Forms.Label();
            this.butCancel = new System.Windows.Forms.Button();
            this.panel3 = new System.Windows.Forms.Panel();
            this.pnlItemProgress.SuspendLayout();
            this.pnlTotalProgress.SuspendLayout();
            this.panel3.SuspendLayout();
            this.SuspendLayout();
            // 
            // pbrItemProgress
            // 
            this.pbrItemProgress.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.pbrItemProgress.Location = new System.Drawing.Point(12, 25);
            this.pbrItemProgress.Name = "pbrItemProgress";
            this.pbrItemProgress.Size = new System.Drawing.Size(441, 23);
            this.pbrItemProgress.TabIndex = 0;
            // 
            // lblItemMessage
            // 
            this.lblItemMessage.AutoEllipsis = true;
            this.lblItemMessage.AutoSize = true;
            this.lblItemMessage.Location = new System.Drawing.Point(12, 9);
            this.lblItemMessage.MaximumSize = new System.Drawing.Size(435, 13);
            this.lblItemMessage.Name = "lblItemMessage";
            this.lblItemMessage.Size = new System.Drawing.Size(35, 13);
            this.lblItemMessage.TabIndex = 1;
            this.lblItemMessage.Text = "label1";
            // 
            // pnlItemProgress
            // 
            this.pnlItemProgress.Controls.Add(this.pbrItemProgress);
            this.pnlItemProgress.Controls.Add(this.lblItemMessage);
            this.pnlItemProgress.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlItemProgress.Location = new System.Drawing.Point(0, 0);
            this.pnlItemProgress.Name = "pnlItemProgress";
            this.pnlItemProgress.Size = new System.Drawing.Size(465, 54);
            this.pnlItemProgress.TabIndex = 6;
            // 
            // pbrTotalProgress
            // 
            this.pbrTotalProgress.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.pbrTotalProgress.Location = new System.Drawing.Point(12, 25);
            this.pbrTotalProgress.Name = "pbrTotalProgress";
            this.pbrTotalProgress.Size = new System.Drawing.Size(441, 23);
            this.pbrTotalProgress.TabIndex = 0;
            // 
            // pnlTotalProgress
            // 
            this.pnlTotalProgress.Controls.Add(this.pbrTotalProgress);
            this.pnlTotalProgress.Controls.Add(this.lblTotalMessage);
            this.pnlTotalProgress.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlTotalProgress.Location = new System.Drawing.Point(0, 54);
            this.pnlTotalProgress.Name = "pnlTotalProgress";
            this.pnlTotalProgress.Size = new System.Drawing.Size(465, 54);
            this.pnlTotalProgress.TabIndex = 7;
            // 
            // lblTotalMessage
            // 
            this.lblTotalMessage.AutoEllipsis = true;
            this.lblTotalMessage.AutoSize = true;
            this.lblTotalMessage.Location = new System.Drawing.Point(12, 9);
            this.lblTotalMessage.MaximumSize = new System.Drawing.Size(435, 13);
            this.lblTotalMessage.Name = "lblTotalMessage";
            this.lblTotalMessage.Size = new System.Drawing.Size(35, 13);
            this.lblTotalMessage.TabIndex = 1;
            this.lblTotalMessage.Text = "label1";
            // 
            // butCancel
            // 
            this.butCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.butCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.butCancel.Location = new System.Drawing.Point(378, 6);
            this.butCancel.Name = "butCancel";
            this.butCancel.Size = new System.Drawing.Size(75, 23);
            this.butCancel.TabIndex = 2;
            this.butCancel.Text = "Cancel";
            this.butCancel.UseVisualStyleBackColor = true;
            // 
            // panel3
            // 
            this.panel3.Controls.Add(this.butCancel);
            this.panel3.Dock = System.Windows.Forms.DockStyle.Top;
            this.panel3.Location = new System.Drawing.Point(0, 108);
            this.panel3.Name = "panel3";
            this.panel3.Size = new System.Drawing.Size(465, 41);
            this.panel3.TabIndex = 8;
            // 
            // ProgressOverlay
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.panel3);
            this.Controls.Add(this.pnlTotalProgress);
            this.Controls.Add(this.pnlItemProgress);
            this.Name = "ProgressOverlay";
            this.Size = new System.Drawing.Size(465, 151);
            this.pnlItemProgress.ResumeLayout(false);
            this.pnlItemProgress.PerformLayout();
            this.pnlTotalProgress.ResumeLayout(false);
            this.pnlTotalProgress.PerformLayout();
            this.panel3.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ProgressBar pbrItemProgress;
        private System.Windows.Forms.Label lblItemMessage;
        private System.Windows.Forms.Panel pnlItemProgress;
        private System.Windows.Forms.ProgressBar pbrTotalProgress;
        private System.Windows.Forms.Panel pnlTotalProgress;
        private System.Windows.Forms.Label lblTotalMessage;
        private System.Windows.Forms.Button butCancel;
        private System.Windows.Forms.Panel panel3;
    }
}
