namespace Nexus.Client.ModActivationMonitoring.UI
{
	partial class ModActivationMonitorControl
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
			this.toolStrip1 = new System.Windows.Forms.ToolStrip();
			this.lvwActiveTasks = new Nexus.UI.Controls.DoubleBufferedListView();
			this.clmOverallMessage = new System.Windows.Forms.ColumnHeader();
			this.clmOverallProgress = new System.Windows.Forms.ColumnHeader();
			this.clmOperation = new System.Windows.Forms.ColumnHeader();
			this.clmProgress = new System.Windows.Forms.ColumnHeader();
			this.clmErrorInfo = new System.Windows.Forms.ColumnHeader();
			this.tsbCancel = new System.Windows.Forms.ToolStripButton();
			this.tsbRemoveQueued = new System.Windows.Forms.ToolStripButton();
			this.tsbRemoveAll = new System.Windows.Forms.ToolStripButton();
			System.Resources.ResourceManager resources = new System.Resources.ResourceManager(typeof(MainForm));
			this.toolStrip1.SuspendLayout();
			this.SuspendLayout();
			// 
			// toolStrip1
			// 
			this.toolStrip1.Dock = System.Windows.Forms.DockStyle.Left;
			this.m_fpdFontProvider.SetFontSet(this.toolStrip1, "MenuText");
			this.m_fpdFontProvider.SetFontSize(this.toolStrip1, 9F);
			this.toolStrip1.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
			this.toolStrip1.ImageScalingSize = new System.Drawing.Size(32, 32);
			this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
			this.tsbCancel,
			this.tsbRemoveQueued,
			this.tsbRemoveAll});
			this.toolStrip1.Location = new System.Drawing.Point(0, 0);
			this.toolStrip1.Name = "toolStrip1";
			this.toolStrip1.Size = new System.Drawing.Size(37, 183);
			this.toolStrip1.TabIndex = 0;
			this.toolStrip1.Text = "toolStrip1";
			// 
			// tsbCancel
			// 
			this.tsbCancel.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbCancel.Image = global::Nexus.Client.Properties.Resources.cancel_download_flat;
			this.tsbCancel.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbCancel.Name = "tsbCancel";
			this.tsbCancel.Size = new System.Drawing.Size(34, 36);
			this.tsbCancel.Text = "Cancel";
			this.tsbCancel.ToolTipText = "Cancel";
			// 
			// tsbRemoveAll
			// 
			this.tsbRemoveAll.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbRemoveAll.Image = global::Nexus.Client.Properties.Resources.list_cleanup_flat;
			this.tsbRemoveAll.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbRemoveAll.Name = "tsbRemoveAll";
			this.tsbRemoveAll.Size = new System.Drawing.Size(34, 36);
			this.tsbRemoveAll.Text = "Remove all";
			// 
			// tsbRemoveQueued
			// 
			this.tsbRemoveQueued.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbRemoveQueued.Image = global::Nexus.Client.Properties.Resources.remove_download_flat;
			this.tsbRemoveQueued.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbRemoveQueued.Name = "tsbRemove";
			this.tsbRemoveQueued.Size = new System.Drawing.Size(34, 36);
			this.tsbRemoveQueued.Text = "Remove";
			// 
			// lvwActiveTasks
			// 
			this.lvwActiveTasks.OwnerDraw = true;
			this.lvwActiveTasks.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
			this.clmOverallMessage,
			this.clmOverallProgress,
			this.clmOperation,
			this.clmProgress,
			this.clmErrorInfo});
			//clmIcon});
			this.lvwActiveTasks.Dock = System.Windows.Forms.DockStyle.Fill;
			this.lvwActiveTasks.FullRowSelect = true;
			this.lvwActiveTasks.HideSelection = false;
			this.lvwActiveTasks.Location = new System.Drawing.Point(37, 0);
			this.lvwActiveTasks.Name = "lvwActiveTasks";
			this.lvwActiveTasks.ShowItemToolTips = true;
			this.lvwActiveTasks.Size = new System.Drawing.Size(516, 183);
			this.lvwActiveTasks.TabIndex = 1;
			this.lvwActiveTasks.UseCompatibleStateImageBehavior = false;
			this.lvwActiveTasks.View = System.Windows.Forms.View.Details;
			this.lvwActiveTasks.SelectedIndexChanged += new System.EventHandler(this.lvwTasks_SelectedIndexChanged);
			this.lvwActiveTasks.Resize += new System.EventHandler(this.lvwTasks_Resize);
			this.lvwActiveTasks.ColumnWidthChanging += new System.Windows.Forms.ColumnWidthChangingEventHandler(this.lvwTasks_ColumnWidthChanging);
			this.lvwActiveTasks.MouseClick += new System.Windows.Forms.MouseEventHandler(ModActivationMonitorControl_MouseClick);
			this.lvwActiveTasks.KeyUp += new System.Windows.Forms.KeyEventHandler(ModActivationMonitorControl_KeyUp);
			this.lvwActiveTasks.ColumnWidthChanging += new System.Windows.Forms.ColumnWidthChangingEventHandler(ModActivationMonitorControl_ColumnWidthChanging);
			this.lvwActiveTasks.DrawSubItem += new System.Windows.Forms.DrawListViewSubItemEventHandler(ModActivationMonitorControl_DrawSubItem);
			this.lvwActiveTasks.DrawColumnHeader += new System.Windows.Forms.DrawListViewColumnHeaderEventHandler(ModActivationMonitorControl_DrawColumnHeader);
			// 
			// clmOverallMessage
			// 
			this.clmOverallMessage.Text = "Name";
			this.clmOverallMessage.Width = 288;
			// 
			// clmOverallProgress
			// 
			this.clmOverallProgress.Text = "Status";
			this.clmOverallProgress.Width = 60;
			// 
			// clmOperation
			// 
			this.clmOperation.Text = "Operation";
			this.clmOperation.Width = 80;
			//
			// clmProgress
			// 
			this.clmProgress.Text = "Progress";
			this.clmProgress.Width = 190;
			//
			// clmErrorInfo
			// 
			this.clmErrorInfo.Text = "?";
			this.clmErrorInfo.Width = 20;
			// 
			// ModActivationMonitorControl
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(180, 453);
			this.CloseButton = false;
			this.CloseButtonVisible = false;
			this.Controls.Add(this.lvwActiveTasks);
			this.Controls.Add(this.toolStrip1);
			this.Name = "ActiveModsMonitorControl";
			this.toolStrip1.ResumeLayout(false);
			this.toolStrip1.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();
		}

		#endregion

		private System.Windows.Forms.ToolStrip toolStrip1;
		public Nexus.UI.Controls.DoubleBufferedListView lvwActiveTasks;
		private System.Windows.Forms.ColumnHeader clmOverallMessage;
		private System.Windows.Forms.ColumnHeader clmOverallProgress;
		private System.Windows.Forms.ColumnHeader clmOperation;
		private System.Windows.Forms.ColumnHeader clmProgress;
		private System.Windows.Forms.ColumnHeader clmErrorInfo;
		private System.Windows.Forms.ToolStripButton tsbCancel;
		private System.Windows.Forms.ToolStripButton tsbRemoveAll;
		private System.Windows.Forms.ToolStripButton tsbRemoveQueued;
	}
}
