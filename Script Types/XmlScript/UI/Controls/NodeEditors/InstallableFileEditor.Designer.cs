namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	partial class InstallableFileEditor
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(InstallableFileEditor));
			this.label1 = new System.Windows.Forms.Label();
			this.tbxSource = new System.Windows.Forms.TextBox();
			this.butSource = new System.Windows.Forms.Button();
			this.label2 = new System.Windows.Forms.Label();
			this.tbxDestination = new System.Windows.Forms.TextBox();
			this.ckbAlwaysInstall = new System.Windows.Forms.CheckBox();
			this.ckbInstallIfUsable = new System.Windows.Forms.CheckBox();
			this.nudPriority = new System.Windows.Forms.NumericUpDown();
			this.label3 = new System.Windows.Forms.Label();
			this.butSave = new System.Windows.Forms.Button();
			this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
			this.erpErrors = new System.Windows.Forms.ErrorProvider(this.components);
			((System.ComponentModel.ISupportInitialize)(this.nudPriority)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).BeginInit();
			this.SuspendLayout();
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(31, 17);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(44, 13);
			this.label1.TabIndex = 0;
			this.label1.Text = "Source:";
			// 
			// tbxSource
			// 
			this.tbxSource.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.tbxSource.Location = new System.Drawing.Point(81, 14);
			this.tbxSource.Name = "tbxSource";
			this.tbxSource.Size = new System.Drawing.Size(480, 20);
			this.tbxSource.TabIndex = 0;
			this.toolTip1.SetToolTip(this.tbxSource, "The path to the file or folder in the mod.");
			this.tbxSource.Validated += new System.EventHandler(this.tbxSource_Validated);
			this.tbxSource.KeyDown += new System.Windows.Forms.KeyEventHandler(this.EditControl_KeyDown);
			// 
			// butSource
			// 
			this.butSource.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.butSource.AutoSize = true;
			this.butSource.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.butSource.Location = new System.Drawing.Point(567, 11);
			this.butSource.Name = "butSource";
			this.butSource.Size = new System.Drawing.Size(26, 23);
			this.butSource.TabIndex = 2;
			this.butSource.Text = "...";
			this.butSource.UseVisualStyleBackColor = true;
			this.butSource.Click += new System.EventHandler(this.butSource_Click);
			// 
			// label2
			// 
			this.label2.AutoSize = true;
			this.label2.Location = new System.Drawing.Point(12, 43);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(63, 13);
			this.label2.TabIndex = 3;
			this.label2.Text = "Destination:";
			// 
			// tbxDestination
			// 
			this.tbxDestination.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.tbxDestination.Location = new System.Drawing.Point(81, 40);
			this.tbxDestination.Name = "tbxDestination";
			this.tbxDestination.Size = new System.Drawing.Size(480, 20);
			this.tbxDestination.TabIndex = 1;
			this.toolTip1.SetToolTip(this.tbxDestination, "The path to which the file or folder should be installed. If omitted, the destina" +
					"tion is the same as the source.");
			this.tbxDestination.KeyDown += new System.Windows.Forms.KeyEventHandler(this.EditControl_KeyDown);
			// 
			// ckbAlwaysInstall
			// 
			this.ckbAlwaysInstall.AutoSize = true;
			this.ckbAlwaysInstall.Location = new System.Drawing.Point(81, 66);
			this.ckbAlwaysInstall.Name = "ckbAlwaysInstall";
			this.ckbAlwaysInstall.Size = new System.Drawing.Size(89, 17);
			this.ckbAlwaysInstall.TabIndex = 2;
			this.ckbAlwaysInstall.Text = "Always Install";
			this.toolTip1.SetToolTip(this.ckbAlwaysInstall, "Indicates that the file or folder should always be installed, regardless of wheth" +
					"er or not the plugin has been selected.");
			this.ckbAlwaysInstall.UseVisualStyleBackColor = true;
			this.ckbAlwaysInstall.KeyDown += new System.Windows.Forms.KeyEventHandler(this.EditControl_KeyDown);
			// 
			// ckbInstallIfUsable
			// 
			this.ckbInstallIfUsable.AutoSize = true;
			this.ckbInstallIfUsable.Location = new System.Drawing.Point(176, 66);
			this.ckbInstallIfUsable.Name = "ckbInstallIfUsable";
			this.ckbInstallIfUsable.Size = new System.Drawing.Size(97, 17);
			this.ckbInstallIfUsable.TabIndex = 3;
			this.ckbInstallIfUsable.Text = "Install if Usable";
			this.toolTip1.SetToolTip(this.ckbInstallIfUsable, "Indicates that the file or folder should always be installed if the plugin is not" +
					" NotUsable, regardless of whether or not the plugin has been selected.");
			this.ckbInstallIfUsable.UseVisualStyleBackColor = true;
			this.ckbInstallIfUsable.KeyDown += new System.Windows.Forms.KeyEventHandler(this.EditControl_KeyDown);
			// 
			// nudPriority
			// 
			this.nudPriority.Location = new System.Drawing.Point(81, 89);
			this.nudPriority.Name = "nudPriority";
			this.nudPriority.Size = new System.Drawing.Size(120, 20);
			this.nudPriority.TabIndex = 4;
			this.toolTip1.SetToolTip(this.nudPriority, resources.GetString("nudPriority.ToolTip"));
			this.nudPriority.KeyDown += new System.Windows.Forms.KeyEventHandler(this.EditControl_KeyDown);
			// 
			// label3
			// 
			this.label3.AutoSize = true;
			this.label3.Location = new System.Drawing.Point(34, 91);
			this.label3.Name = "label3";
			this.label3.Size = new System.Drawing.Size(41, 13);
			this.label3.TabIndex = 8;
			this.label3.Text = "Priority:";
			// 
			// butSave
			// 
			this.butSave.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butSave.Location = new System.Drawing.Point(533, 232);
			this.butSave.Name = "butSave";
			this.butSave.Size = new System.Drawing.Size(75, 23);
			this.butSave.TabIndex = 5;
			this.butSave.Text = "Add";
			this.butSave.UseVisualStyleBackColor = true;
			this.butSave.Click += new System.EventHandler(this.butSave_Click);
			// 
			// toolTip1
			// 
			this.toolTip1.IsBalloon = true;
			this.toolTip1.ToolTipIcon = System.Windows.Forms.ToolTipIcon.Info;
			// 
			// erpErrors
			// 
			this.erpErrors.ContainerControl = this;
			// 
			// InstallableFileEditor
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.AutoValidate = System.Windows.Forms.AutoValidate.EnableAllowFocusChange;
			this.Controls.Add(this.butSave);
			this.Controls.Add(this.label3);
			this.Controls.Add(this.nudPriority);
			this.Controls.Add(this.ckbInstallIfUsable);
			this.Controls.Add(this.ckbAlwaysInstall);
			this.Controls.Add(this.tbxDestination);
			this.Controls.Add(this.label2);
			this.Controls.Add(this.butSource);
			this.Controls.Add(this.tbxSource);
			this.Controls.Add(this.label1);
			this.Name = "InstallableFileEditor";
			this.Padding = new System.Windows.Forms.Padding(9);
			this.Size = new System.Drawing.Size(620, 267);
			((System.ComponentModel.ISupportInitialize)(this.nudPriority)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.TextBox tbxSource;
		private System.Windows.Forms.Button butSource;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.TextBox tbxDestination;
		private System.Windows.Forms.CheckBox ckbAlwaysInstall;
		private System.Windows.Forms.CheckBox ckbInstallIfUsable;
		private System.Windows.Forms.NumericUpDown nudPriority;
		private System.Windows.Forms.Label label3;
		private System.Windows.Forms.Button butSave;
		private System.Windows.Forms.ToolTip toolTip1;
		private System.Windows.Forms.ErrorProvider erpErrors;
	}
}
