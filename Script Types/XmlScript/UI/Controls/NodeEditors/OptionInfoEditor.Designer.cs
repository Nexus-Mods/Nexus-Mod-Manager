namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	partial class OptionInfoEditor
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
			this.tbxDescription = new System.Windows.Forms.TextBox();
			this.butSelectImage = new System.Windows.Forms.Button();
			this.tbxImagePath = new System.Windows.Forms.TextBox();
			this.tbxName = new System.Windows.Forms.TextBox();
			this.label3 = new System.Windows.Forms.Label();
			this.label2 = new System.Windows.Forms.Label();
			this.label1 = new System.Windows.Forms.Label();
			this.pbxImage = new System.Windows.Forms.PictureBox();
			this.erpErrors = new System.Windows.Forms.ErrorProvider(this.components);
			((System.ComponentModel.ISupportInitialize)(this.pbxImage)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).BeginInit();
			this.SuspendLayout();
			// 
			// tbxDescription
			// 
			this.tbxDescription.AcceptsReturn = true;
			this.tbxDescription.AcceptsTab = true;
			this.tbxDescription.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.tbxDescription.Location = new System.Drawing.Point(72, 29);
			this.tbxDescription.Multiline = true;
			this.tbxDescription.Name = "tbxDescription";
			this.tbxDescription.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
			this.tbxDescription.Size = new System.Drawing.Size(509, 96);
			this.tbxDescription.TabIndex = 1;
			// 
			// butSelectImage
			// 
			this.butSelectImage.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.butSelectImage.AutoSize = true;
			this.butSelectImage.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.butSelectImage.Location = new System.Drawing.Point(587, 129);
			this.butSelectImage.Name = "butSelectImage";
			this.butSelectImage.Size = new System.Drawing.Size(26, 23);
			this.butSelectImage.TabIndex = 3;
			this.butSelectImage.Text = "...";
			this.butSelectImage.UseVisualStyleBackColor = true;
			this.butSelectImage.Click += new System.EventHandler(this.butSelectImage_Click);
			// 
			// tbxImagePath
			// 
			this.tbxImagePath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.tbxImagePath.Location = new System.Drawing.Point(72, 131);
			this.tbxImagePath.Name = "tbxImagePath";
			this.tbxImagePath.Size = new System.Drawing.Size(509, 20);
			this.tbxImagePath.TabIndex = 2;
			this.tbxImagePath.KeyDown += new System.Windows.Forms.KeyEventHandler(this.Control_KeyDown);
			// 
			// tbxName
			// 
			this.tbxName.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.tbxName.Location = new System.Drawing.Point(72, 3);
			this.tbxName.Name = "tbxName";
			this.tbxName.Size = new System.Drawing.Size(509, 20);
			this.tbxName.TabIndex = 0;
			this.tbxName.KeyDown += new System.Windows.Forms.KeyEventHandler(this.Control_KeyDown);
			this.tbxName.Validated += new System.EventHandler(this.tbxName_Validated);
			// 
			// label3
			// 
			this.label3.AutoSize = true;
			this.label3.Location = new System.Drawing.Point(27, 134);
			this.label3.Name = "label3";
			this.label3.Size = new System.Drawing.Size(39, 13);
			this.label3.TabIndex = 13;
			this.label3.Text = "Image:";
			// 
			// label2
			// 
			this.label2.AutoSize = true;
			this.label2.Location = new System.Drawing.Point(3, 32);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(63, 13);
			this.label2.TabIndex = 10;
			this.label2.Text = "Description:";
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(28, 6);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(38, 13);
			this.label1.TabIndex = 9;
			this.label1.Text = "Name:";
			// 
			// pbxImage
			// 
			this.pbxImage.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
						| System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.pbxImage.Location = new System.Drawing.Point(72, 157);
			this.pbxImage.Name = "pbxImage";
			this.pbxImage.Size = new System.Drawing.Size(509, 376);
			this.pbxImage.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
			this.pbxImage.TabIndex = 15;
			this.pbxImage.TabStop = false;
			// 
			// erpErrors
			// 
			this.erpErrors.ContainerControl = this;
			// 
			// OptionInfoEditor
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackColor = System.Drawing.Color.Transparent;
			this.Controls.Add(this.tbxDescription);
			this.Controls.Add(this.pbxImage);
			this.Controls.Add(this.butSelectImage);
			this.Controls.Add(this.tbxImagePath);
			this.Controls.Add(this.tbxName);
			this.Controls.Add(this.label3);
			this.Controls.Add(this.label2);
			this.Controls.Add(this.label1);
			this.Name = "OptionInfoEditor";
			this.Size = new System.Drawing.Size(641, 536);
			((System.ComponentModel.ISupportInitialize)(this.pbxImage)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.TextBox tbxDescription;
		private System.Windows.Forms.PictureBox pbxImage;
		private System.Windows.Forms.Button butSelectImage;
		private System.Windows.Forms.TextBox tbxImagePath;
		private System.Windows.Forms.TextBox tbxName;
		private System.Windows.Forms.Label label3;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.ErrorProvider erpErrors;

	}
}
