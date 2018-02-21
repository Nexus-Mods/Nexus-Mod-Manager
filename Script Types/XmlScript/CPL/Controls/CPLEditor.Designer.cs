namespace Nexus.Client.ModManagement.Scripting.XmlScript.CPL.Controls
{
	partial class CPLEditor
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
			this.butAnd = new System.Windows.Forms.Button();
			this.butOr = new System.Windows.Forms.Button();
			this.pnlButtons = new System.Windows.Forms.Panel();
			this.splitContainer1 = new System.Windows.Forms.SplitContainer();
			this.cleCPLEditor = new Nexus.Client.ModManagement.Scripting.XmlScript.CPL.Controls.CPLTextEditor();
			this.pnlButtons.SuspendLayout();
			this.splitContainer1.Panel1.SuspendLayout();
			this.splitContainer1.Panel2.SuspendLayout();
			this.splitContainer1.SuspendLayout();
			this.SuspendLayout();
			// 
			// butAnd
			// 
			this.butAnd.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.butAnd.Location = new System.Drawing.Point(352, 6);
			this.butAnd.Name = "butAnd";
			this.butAnd.Size = new System.Drawing.Size(75, 23);
			this.butAnd.TabIndex = 6;
			this.butAnd.Text = "AND";
			this.butAnd.UseVisualStyleBackColor = true;
			this.butAnd.Click += new System.EventHandler(this.Operator_Click);
			// 
			// butOr
			// 
			this.butOr.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this.butOr.Location = new System.Drawing.Point(433, 6);
			this.butOr.Name = "butOr";
			this.butOr.Size = new System.Drawing.Size(75, 23);
			this.butOr.TabIndex = 7;
			this.butOr.Text = "OR";
			this.butOr.UseVisualStyleBackColor = true;
			this.butOr.Click += new System.EventHandler(this.Operator_Click);
			// 
			// pnlButtons
			// 
			this.pnlButtons.AutoSize = true;
			this.pnlButtons.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.pnlButtons.Controls.Add(this.butOr);
			this.pnlButtons.Controls.Add(this.butAnd);
			this.pnlButtons.Dock = System.Windows.Forms.DockStyle.Top;
			this.pnlButtons.Location = new System.Drawing.Point(0, 0);
			this.pnlButtons.Name = "pnlButtons";
			this.pnlButtons.Size = new System.Drawing.Size(511, 32);
			this.pnlButtons.TabIndex = 8;
			// 
			// splitContainer1
			// 
			this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.splitContainer1.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
			this.splitContainer1.Location = new System.Drawing.Point(12, 12);
			this.splitContainer1.Name = "splitContainer1";
			this.splitContainer1.Orientation = System.Windows.Forms.Orientation.Horizontal;
			// 
			// splitContainer1.Panel1
			// 
			this.splitContainer1.Panel1.Controls.Add(this.cleCPLEditor);
			// 
			// splitContainer1.Panel2
			// 
			this.splitContainer1.Panel2.Controls.Add(this.pnlButtons);
			this.splitContainer1.Size = new System.Drawing.Size(511, 469);
			this.splitContainer1.SplitterDistance = 112;
			this.splitContainer1.TabIndex = 8;
			// 
			// cleCPLEditor
			// 
			this.cleCPLEditor.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
			this.cleCPLEditor.Dock = System.Windows.Forms.DockStyle.Fill;
			this.cleCPLEditor.IsReadOnly = false;
			this.cleCPLEditor.Location = new System.Drawing.Point(0, 0);
			this.cleCPLEditor.Name = "cleCPLEditor";
			this.cleCPLEditor.ShowLineNumbers = false;
			this.cleCPLEditor.Size = new System.Drawing.Size(511, 112);
			this.cleCPLEditor.TabIndex = 0;
			// 
			// CPLEditor
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.splitContainer1);
			this.Name = "CPLEditor";
			this.Padding = new System.Windows.Forms.Padding(12);
			this.Size = new System.Drawing.Size(535, 493);
			this.pnlButtons.ResumeLayout(false);
			this.splitContainer1.Panel1.ResumeLayout(false);
			this.splitContainer1.Panel2.ResumeLayout(false);
			this.splitContainer1.Panel2.PerformLayout();
			this.splitContainer1.ResumeLayout(false);
			this.ResumeLayout(false);

		}

		#endregion

		private Nexus.Client.ModManagement.Scripting.XmlScript.CPL.Controls.CPLTextEditor cleCPLEditor;
		private System.Windows.Forms.Button butOr;
		private System.Windows.Forms.Button butAnd;
		private System.Windows.Forms.Panel pnlButtons;
		private System.Windows.Forms.SplitContainer splitContainer1;
	}
}
