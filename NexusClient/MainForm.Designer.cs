namespace Nexus.Client
{
	partial class MainForm
	{
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		/// Cleans up resources used by the main form.
		/// </summary>
		/// <param name="disposing">Whether managed resources should be disposed.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				DisposeMainBarResources();
				components?.Dispose();
			}

			base.Dispose(disposing);
		}

		#region Windows Form Designer generated code

		/// <summary>
		/// Initializes the designer-owned main-form content. Runtime document and
		/// docking surfaces are created by the DevExpress docking initializer.
		/// </summary>
		private void InitializeComponent()
		{
			components = new System.ComponentModel.Container();
			this.SuspendLayout();
			//
			// MainForm
			//
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(1134, 591);
			this.Name = "MainForm";
			this.Text = "MainForm";
			this.ResumeLayout(false);
		}

		#endregion
	}
}
