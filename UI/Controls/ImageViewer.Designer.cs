namespace Nexus.UI.Controls
{
	partial class ImageViewer
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
			this.toolStrip1 = new System.Windows.Forms.ToolStrip();
			this.tsbZoomOriginal = new System.Windows.Forms.ToolStripButton();
			this.tsbZoomFit = new System.Windows.Forms.ToolStripButton();
			this.panel1 = new System.Windows.Forms.Panel();
			this.pbxImage = new System.Windows.Forms.PictureBox();
			this.toolStrip1.SuspendLayout();
			this.panel1.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.pbxImage)).BeginInit();
			this.SuspendLayout();
			// 
			// toolStrip1
			// 
			this.toolStrip1.ImageScalingSize = new System.Drawing.Size(32, 32);
			this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsbZoomOriginal,
            this.tsbZoomFit});
			this.toolStrip1.Location = new System.Drawing.Point(0, 0);
			this.toolStrip1.Name = "toolStrip1";
			this.toolStrip1.Size = new System.Drawing.Size(645, 39);
			this.toolStrip1.TabIndex = 0;
			this.toolStrip1.Text = "toolStrip1";
			// 
			// tsbZoomOriginal
			// 
			this.tsbZoomOriginal.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbZoomOriginal.Image = global::Nexus.UI.Properties.Resources.zoom_original;
			this.tsbZoomOriginal.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbZoomOriginal.Name = "tsbZoomOriginal";
			this.tsbZoomOriginal.Size = new System.Drawing.Size(36, 36);
			this.tsbZoomOriginal.Text = "Actual Size";
			this.tsbZoomOriginal.Click += new System.EventHandler(this.Zoom_Click);
			// 
			// tsbZoomFit
			// 
			this.tsbZoomFit.Checked = true;
			this.tsbZoomFit.CheckState = System.Windows.Forms.CheckState.Checked;
			this.tsbZoomFit.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbZoomFit.Image = global::Nexus.UI.Properties.Resources.zoom_fit_best_2;
			this.tsbZoomFit.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbZoomFit.Name = "tsbZoomFit";
			this.tsbZoomFit.Size = new System.Drawing.Size(36, 36);
			this.tsbZoomFit.Text = "Fit to Window";
			this.tsbZoomFit.Click += new System.EventHandler(this.Zoom_Click);
			// 
			// panel1
			// 
			this.panel1.AutoScroll = true;
			this.panel1.Controls.Add(this.pbxImage);
			this.panel1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.panel1.Location = new System.Drawing.Point(0, 39);
			this.panel1.Name = "panel1";
			this.panel1.Size = new System.Drawing.Size(645, 374);
			this.panel1.TabIndex = 1;
			// 
			// pbxImage
			// 
			this.pbxImage.Dock = System.Windows.Forms.DockStyle.Fill;
			this.pbxImage.Location = new System.Drawing.Point(0, 0);
			this.pbxImage.Name = "pbxImage";
			this.pbxImage.Size = new System.Drawing.Size(645, 374);
			this.pbxImage.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
			this.pbxImage.TabIndex = 0;
			this.pbxImage.TabStop = false;
			this.pbxImage.Click += new System.EventHandler(this.pbxImage_Click);
			// 
			// ImageViewer
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(645, 413);
			this.Controls.Add(this.panel1);
			this.Controls.Add(this.toolStrip1);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.SizableToolWindow;
			this.Name = "ImageViewer";
			this.ShowInTaskbar = false;
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
			this.Text = "Image Viewer";
			this.toolStrip1.ResumeLayout(false);
			this.toolStrip1.PerformLayout();
			this.panel1.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.pbxImage)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.ToolStrip toolStrip1;
		private System.Windows.Forms.Panel panel1;
		private System.Windows.Forms.PictureBox pbxImage;
		private System.Windows.Forms.ToolStripButton tsbZoomOriginal;
		private System.Windows.Forms.ToolStripButton tsbZoomFit;
	}
}