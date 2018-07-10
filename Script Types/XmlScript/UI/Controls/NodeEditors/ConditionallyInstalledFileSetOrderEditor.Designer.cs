namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	partial class ConditionallyInstalledFileSetOrderEditor
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ConditionallyInstalledFileSetOrderEditor));
			this.label1 = new System.Windows.Forms.Label();
			this.autosizeLabel1 = new Nexus.UI.Controls.AutosizeLabel();
			this.rlvConditionalInstalls = new Nexus.UI.Controls.ReorderableListView();
			this.clmCondition = new System.Windows.Forms.ColumnHeader();
			this.clmFiles = new System.Windows.Forms.ColumnHeader();
			this.SuspendLayout();
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(12, 12);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(178, 13);
			this.label1.TabIndex = 0;
			this.label1.Text = "Conditionally Installed File Set Order:";
			// 
			// autosizeLabel1
			// 
			this.autosizeLabel1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.autosizeLabel1.BackColor = System.Drawing.SystemColors.Control;
			this.autosizeLabel1.BorderStyle = System.Windows.Forms.BorderStyle.None;
			this.autosizeLabel1.Cursor = System.Windows.Forms.Cursors.Arrow;
			this.autosizeLabel1.Location = new System.Drawing.Point(24, 28);
			this.autosizeLabel1.Name = "autosizeLabel1";
			this.autosizeLabel1.ReadOnly = true;
			this.autosizeLabel1.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.None;
			this.autosizeLabel1.Size = new System.Drawing.Size(593, 31);
			this.autosizeLabel1.TabIndex = 2;
			this.autosizeLabel1.TabStop = false;
			this.autosizeLabel1.Text = resources.GetString("autosizeLabel1.Text");
			// 
			// rlvConditionalInstalls
			// 
			this.rlvConditionalInstalls.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
						| System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.rlvConditionalInstalls.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.clmCondition,
            this.clmFiles});
			this.rlvConditionalInstalls.Location = new System.Drawing.Point(24, 65);
			this.rlvConditionalInstalls.Name = "rlvConditionalInstalls";
			this.rlvConditionalInstalls.Size = new System.Drawing.Size(593, 444);
			this.rlvConditionalInstalls.TabIndex = 3;
			this.rlvConditionalInstalls.UseCompatibleStateImageBehavior = false;
			this.rlvConditionalInstalls.Resize += new System.EventHandler(this.rlvConditionalInstalls_Resize);
			this.rlvConditionalInstalls.ItemsReordered += new System.EventHandler<Nexus.UI.Controls.ReorderedItemsEventArgs>(this.rlvConditionalInstalls_ItemsReordered);
			// 
			// clmCondition
			// 
			this.clmCondition.Text = "Condition";
			// 
			// clmFiles
			// 
			this.clmFiles.Text = "Files";
			// 
			// ConditionalFileInstallOrderEditor
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.rlvConditionalInstalls);
			this.Controls.Add(this.autosizeLabel1);
			this.Controls.Add(this.label1);
			this.Name = "ConditionalFileInstallOrderEditor";
			this.Size = new System.Drawing.Size(647, 534);
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Label label1;
		private Nexus.UI.Controls.AutosizeLabel autosizeLabel1;
		private Nexus.UI.Controls.ReorderableListView rlvConditionalInstalls;
		private System.Windows.Forms.ColumnHeader clmCondition;
		private System.Windows.Forms.ColumnHeader clmFiles;
	}
}
