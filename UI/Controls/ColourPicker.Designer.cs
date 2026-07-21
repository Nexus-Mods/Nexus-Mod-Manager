namespace Nexus.UI.Controls
{
	partial class ColourPicker
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
			this.cldColourPicker = new System.Windows.Forms.ColorDialog();
			this.butSelectColour = new System.Windows.Forms.Button();
			this.lblColour = new System.Windows.Forms.Label();
			this.SuspendLayout();
			// 
			// butSelectColour
			// 
			this.butSelectColour.AutoSize = true;
			this.butSelectColour.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.butSelectColour.Location = new System.Drawing.Point(52, 0);
			this.butSelectColour.Name = "butSelectColour";
			this.butSelectColour.Size = new System.Drawing.Size(26, 23);
			this.butSelectColour.TabIndex = 0;
			this.butSelectColour.Text = "...";
			this.butSelectColour.UseVisualStyleBackColor = true;
			this.butSelectColour.Click += new System.EventHandler(this.butSelectColour_Click);
			// 
			// lblColour
			// 
			this.lblColour.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
			this.lblColour.Location = new System.Drawing.Point(0, 0);
			this.lblColour.Name = "lblColour";
			this.lblColour.Size = new System.Drawing.Size(46, 23);
			this.lblColour.TabIndex = 1;
			// 
			// ColourPicker
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.AutoSize = true;
			this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.Controls.Add(this.lblColour);
			this.Controls.Add(this.butSelectColour);
			this.Margin = new System.Windows.Forms.Padding(0);
			this.Name = "ColourPicker";
			this.Size = new System.Drawing.Size(81, 23);
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.ColorDialog cldColourPicker;
		private System.Windows.Forms.Button butSelectColour;
		private System.Windows.Forms.Label lblColour;
	}
}
