namespace Nexus.Client.DownloadMonitoring.UI
{
	partial class DownloadMonitorControl
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
			this.tsbCancel = new System.Windows.Forms.ToolStripButton();
			this.tsbPurgeDownloads = new System.Windows.Forms.ToolStripButton();
			this.tsbRemove = new System.Windows.Forms.ToolStripButton();
			this.tsbRemoveAll = new System.Windows.Forms.ToolStripButton();
			this.tsbPause = new System.Windows.Forms.ToolStripButton();
			this.tsbResume = new System.Windows.Forms.ToolStripButton();
			this.tsbResumeAll = new System.Windows.Forms.ToolStripButton();
			this.lvwTasks = new Nexus.UI.Controls.DoubleBufferedListView();
			this.clmOverallMessage = new System.Windows.Forms.ColumnHeader();
			this.clmOverallProgress = new System.Windows.Forms.ColumnHeader();
			this.clmItemMessage = new System.Windows.Forms.ColumnHeader();
			this.clmItemProgress = new System.Windows.Forms.ColumnHeader();
			this.clmStatus = new System.Windows.Forms.ColumnHeader();
			this.clmETA = new System.Windows.Forms.ColumnHeader();
			this.clmFileserver = new System.Windows.Forms.ColumnHeader();
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
            this.tsbResume,
            this.tsbCancel,
            this.tsbPause,
            this.tsbRemove,
			this.tsbResumeAll,
			this.tsbRemoveAll,
			this.tsbPurgeDownloads});
			this.toolStrip1.Location = new System.Drawing.Point(0, 0);
			this.toolStrip1.Name = "toolStrip1";
			this.toolStrip1.Size = new System.Drawing.Size(37, 183);
			this.toolStrip1.TabIndex = 0;
			this.toolStrip1.Text = "toolStrip1";
			// 
			// tsbCancel
			// 
			this.tsbCancel.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbCancel.Image = global::Nexus.Client.Properties.Resources.edit_delete;
			this.tsbCancel.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbCancel.Name = "tsbCancel";
			this.tsbCancel.Size = new System.Drawing.Size(34, 36);
			this.tsbCancel.Text = "Cancel";
			this.tsbCancel.ToolTipText = "Cancel";
			// 
			// tsbPurgeDownloads
			// 
			this.tsbPurgeDownloads.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbPurgeDownloads.Image = global::Nexus.Client.Properties.Resources.Actions_edit_clear_list_icon;
			this.tsbPurgeDownloads.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbPurgeDownloads.Name = "tsbPurgeDownloads";
			this.tsbPurgeDownloads.Size = new System.Drawing.Size(34, 36);
			this.tsbPurgeDownloads.Text = "Purge Downloads";
			this.tsbPurgeDownloads.ToolTipText = "Purge Downloads";
			// 
			// tsbRemove
			// 
			this.tsbRemove.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbRemove.Image = global::Nexus.Client.Properties.Resources.edit_delete_6;
			this.tsbRemove.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbRemove.Name = "tsbRemove";
			this.tsbRemove.Size = new System.Drawing.Size(34, 36);
			this.tsbRemove.Text = "Remove";
			// 
			// tsbRemoveAll
			// 
			this.tsbRemoveAll.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbRemoveAll.Image = global::Nexus.Client.Properties.Resources.edit_clear_3;
			this.tsbRemoveAll.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbRemoveAll.Name = "tsbRemoveAll";
			this.tsbRemoveAll.Size = new System.Drawing.Size(34, 36);
			this.tsbRemoveAll.Text = "Remove All";
			// 
			// tsbPause
			// 
			this.tsbPause.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbPause.Image = global::Nexus.Client.Properties.Resources.media_playback_pause_7;
			this.tsbPause.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbPause.Name = "tsbPause";
			this.tsbPause.Size = new System.Drawing.Size(34, 36);
			this.tsbPause.Text = "Pause";
			// 
			// tsbResume
			// 
			this.tsbResume.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbResume.Image = global::Nexus.Client.Properties.Resources.media_playback_start_7;
			this.tsbResume.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbResume.Name = "tsbResume";
			this.tsbResume.Size = new System.Drawing.Size(34, 36);
			this.tsbResume.Text = "Resume";
			// 
			// tsbResumeAll
			// 
			this.tsbResumeAll.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbResumeAll.Image = global::Nexus.Client.Properties.Resources.compilebasic;
			this.tsbResumeAll.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbResumeAll.Name = "tsbResumeAll";
			this.tsbResumeAll.Size = new System.Drawing.Size(34, 36);
			this.tsbResumeAll.Text = "Resume All";
			// 
			// lvwTasks
			// 
			this.lvwTasks.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.clmOverallMessage,
            this.clmOverallProgress,
			this.clmStatus,
            this.clmItemMessage,
			this.clmFileserver,
            this.clmETA,
            this.clmItemProgress});
			this.lvwTasks.Dock = System.Windows.Forms.DockStyle.Fill;
			this.lvwTasks.FullRowSelect = true;
			this.lvwTasks.HideSelection = false;
			this.lvwTasks.Location = new System.Drawing.Point(37, 0);
			this.lvwTasks.Name = "lvwTasks";
			this.lvwTasks.ShowItemToolTips = true;
			this.lvwTasks.Size = new System.Drawing.Size(516, 183);
			this.lvwTasks.TabIndex = 1;
			this.lvwTasks.UseCompatibleStateImageBehavior = false;
			this.lvwTasks.View = System.Windows.Forms.View.Details;
			this.lvwTasks.Resize += new System.EventHandler(this.lvwTasks_Resize);
			this.lvwTasks.SelectedIndexChanged += new System.EventHandler(this.lvwTasks_SelectedIndexChanged);
			this.lvwTasks.ColumnWidthChanging += new System.Windows.Forms.ColumnWidthChangingEventHandler(this.lvwTasks_ColumnWidthChanging);
			this.lvwTasks.MouseClick += new System.Windows.Forms.MouseEventHandler(DownloadMonitorControl_MouseClick);
			this.lvwTasks.KeyUp += new System.Windows.Forms.KeyEventHandler(DownloadMonitorControl_KeyUp);
			// 
			// clmOverallMessage
			// 
			this.clmOverallMessage.Text = "Name";
			this.clmOverallProgress.Width = 200;
			// 
			// clmOverallProgress
			// 
			this.clmOverallProgress.Text = "Size";
			this.clmOverallProgress.Width = 100;
			// 
			// clmItemMessage
			// 
			this.clmItemMessage.Text = "Speed";
			this.clmItemMessage.Width = 70;
			// 
			// clmItemProgress
			// 
			this.clmItemProgress.Text = "Threads";
			this.clmItemProgress.Width = 30;
			// 
			// clmFileserver
			// 
			this.clmFileserver.Text = "Fileserver";
			this.clmFileserver.Width = 60;
			// 
			// clmStatus
			// 
			this.clmStatus.Text = "Status";
			this.clmStatus.Width = 60;
			// 
			// clmETA
			// 
			this.clmETA.Text = "ETA";
			this.clmStatus.Width = 50;
			// 
			// DownloadMonitorControl
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(553, 183);
			this.CloseButton = false;
			this.CloseButtonVisible = false;
			this.Controls.Add(this.lvwTasks);
			this.Controls.Add(this.toolStrip1);
			this.Name = "DownloadMonitorControl";
			this.toolStrip1.ResumeLayout(false);
			this.toolStrip1.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();
		}

		#endregion

		private System.Windows.Forms.ToolStrip toolStrip1;
		private System.Windows.Forms.ToolStripButton tsbCancel;
		private System.Windows.Forms.ToolStripButton tsbPurgeDownloads;
		private Nexus.UI.Controls.DoubleBufferedListView lvwTasks;
		private System.Windows.Forms.ColumnHeader clmOverallMessage;
		private System.Windows.Forms.ColumnHeader clmOverallProgress;
		private System.Windows.Forms.ColumnHeader clmItemMessage;
		private System.Windows.Forms.ColumnHeader clmItemProgress;
		private System.Windows.Forms.ColumnHeader clmStatus;
		private System.Windows.Forms.ColumnHeader clmETA;
		private System.Windows.Forms.ColumnHeader clmFileserver;
		private System.Windows.Forms.ToolStripButton tsbRemove;
		private System.Windows.Forms.ToolStripButton tsbPause;
		private System.Windows.Forms.ToolStripButton tsbResume;
		private System.Windows.Forms.ToolStripButton tsbResumeAll;
		private System.Windows.Forms.ToolStripButton tsbRemoveAll;
	}
}
