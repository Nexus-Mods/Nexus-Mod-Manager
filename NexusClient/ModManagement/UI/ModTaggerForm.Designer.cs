namespace Nexus.Client.ModManagement.UI
{
	partial class ModTaggerForm
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
			this.lvwTagCandidates = new System.Windows.Forms.ListView();
			this.clmName = new System.Windows.Forms.ColumnHeader();
			this.clmVersion = new System.Windows.Forms.ColumnHeader();
			this.panel4 = new System.Windows.Forms.Panel();
			this.butCancel = new System.Windows.Forms.Button();
			this.butOK = new System.Windows.Forms.Button();
			this.splitContainer1 = new System.Windows.Forms.SplitContainer();
			this.label1 = new System.Windows.Forms.Label();
			this.mieTagEditor = new Nexus.Client.ModAuthoring.UI.Controls.ModInfoEditor();
			this.panel4.SuspendLayout();
			this.splitContainer1.Panel1.SuspendLayout();
			this.splitContainer1.Panel2.SuspendLayout();
			this.splitContainer1.SuspendLayout();
			this.SuspendLayout();
			// 
			// lvwTagCandidates
			// 
			this.lvwTagCandidates.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
						| System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.lvwTagCandidates.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.clmName,
            this.clmVersion});
			this.lvwTagCandidates.FullRowSelect = true;
			this.lvwTagCandidates.HideSelection = false;
			this.lvwTagCandidates.Location = new System.Drawing.Point(12, 25);
			this.lvwTagCandidates.Name = "lvwTagCandidates";
			this.lvwTagCandidates.ShowItemToolTips = true;
			this.lvwTagCandidates.Size = new System.Drawing.Size(224, 325);
			this.lvwTagCandidates.TabIndex = 0;
			this.lvwTagCandidates.UseCompatibleStateImageBehavior = false;
			this.lvwTagCandidates.View = System.Windows.Forms.View.Details;
			this.lvwTagCandidates.Resize += new System.EventHandler(this.lvwTagCandidates_Resize);
			this.lvwTagCandidates.SelectedIndexChanged += new System.EventHandler(this.lvwTagCandidates_SelectedIndexChanged);
			this.lvwTagCandidates.ColumnWidthChanging += new System.Windows.Forms.ColumnWidthChangingEventHandler(this.lvwTagCandidates_ColumnWidthChanging);
			// 
			// clmName
			// 
			this.clmName.Text = "Name";
			// 
			// clmVersion
			// 
			this.clmVersion.Text = "Version";
			// 
			// panel4
			// 
			this.panel4.Controls.Add(this.butCancel);
			this.panel4.Controls.Add(this.butOK);
			this.panel4.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.panel4.Location = new System.Drawing.Point(0, 363);
			this.panel4.Name = "panel4";
			this.panel4.Size = new System.Drawing.Size(717, 47);
			this.panel4.TabIndex = 5;
			// 
			// butCancel
			// 
			this.butCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this.butCancel.Location = new System.Drawing.Point(630, 12);
			this.butCancel.Name = "butCancel";
			this.butCancel.Size = new System.Drawing.Size(75, 23);
			this.butCancel.TabIndex = 1;
			this.butCancel.Text = "Cancel";
			this.butCancel.UseVisualStyleBackColor = true;
			// 
			// butOK
			// 
			this.butOK.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.butOK.Location = new System.Drawing.Point(549, 12);
			this.butOK.Name = "butOK";
			this.butOK.Size = new System.Drawing.Size(75, 23);
			this.butOK.TabIndex = 0;
			this.butOK.Text = "OK";
			this.butOK.UseVisualStyleBackColor = true;
			this.butOK.Click += new System.EventHandler(this.butOK_Click);
			// 
			// splitContainer1
			// 
			this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.splitContainer1.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
			this.splitContainer1.Location = new System.Drawing.Point(0, 0);
			this.splitContainer1.Name = "splitContainer1";
			// 
			// splitContainer1.Panel1
			// 
			this.splitContainer1.Panel1.Controls.Add(this.label1);
			this.splitContainer1.Panel1.Controls.Add(this.lvwTagCandidates);
			// 
			// splitContainer1.Panel2
			// 
			this.splitContainer1.Panel2.Controls.Add(this.mieTagEditor);
			this.splitContainer1.Size = new System.Drawing.Size(717, 363);
			this.splitContainer1.SplitterDistance = 239;
			this.splitContainer1.TabIndex = 7;
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(3, 9);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(83, 13);
			this.label1.TabIndex = 1;
			this.label1.Text = "Matching Mods:";
			// 
			// mieTagEditor
			// 
			this.mieTagEditor.AutoCommitChanges = false;
			this.mieTagEditor.Dock = System.Windows.Forms.DockStyle.Fill;
			this.mieTagEditor.Location = new System.Drawing.Point(0, 0);
			this.mieTagEditor.Name = "mieTagEditor";
			this.mieTagEditor.Padding = new System.Windows.Forms.Padding(9);
			this.mieTagEditor.Size = new System.Drawing.Size(474, 363);
			this.mieTagEditor.TabIndex = 6;
			// 
			// ModTaggerForm
			// 
			this.AcceptButton = this.butOK;
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.CancelButton = this.butCancel;
			this.ClientSize = new System.Drawing.Size(717, 410);
			this.Controls.Add(this.splitContainer1);
			this.Controls.Add(this.panel4);
			this.Name = "ModTaggerForm";
			this.ShowInTaskbar = false;
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
			this.Text = "Edit Mod Information";
			this.panel4.ResumeLayout(false);
			this.splitContainer1.Panel1.ResumeLayout(false);
			this.splitContainer1.Panel1.PerformLayout();
			this.splitContainer1.Panel2.ResumeLayout(false);
			this.splitContainer1.ResumeLayout(false);
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.ListView lvwTagCandidates;
		private System.Windows.Forms.Panel panel4;
		private System.Windows.Forms.Button butCancel;
		private System.Windows.Forms.Button butOK;
		private Nexus.Client.ModAuthoring.UI.Controls.ModInfoEditor mieTagEditor;
		private System.Windows.Forms.ColumnHeader clmName;
		private System.Windows.Forms.ColumnHeader clmVersion;
		private System.Windows.Forms.SplitContainer splitContainer1;
		private System.Windows.Forms.Label label1;
	}
}