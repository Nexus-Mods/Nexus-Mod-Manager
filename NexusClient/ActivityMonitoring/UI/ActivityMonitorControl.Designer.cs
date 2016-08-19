namespace Nexus.Client.ActivityMonitoring.UI
{
	partial class ActivityMonitorControl
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
			this.tsbRemove = new System.Windows.Forms.ToolStripButton();
			this.tsbPause = new System.Windows.Forms.ToolStripButton();
			this.tsbResume = new System.Windows.Forms.ToolStripButton();
			this.lvwTasks = new Nexus.UI.Controls.DoubleBufferedListView();
			this.clmOverallMessage = new System.Windows.Forms.ColumnHeader();
			this.clmOverallProgress = new System.Windows.Forms.ColumnHeader();
			this.clmItemMessage = new System.Windows.Forms.ColumnHeader();
			this.clmItemProgress = new System.Windows.Forms.ColumnHeader();
			this.clmStatus = new System.Windows.Forms.ColumnHeader();
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
			this.tsbRemove,
			this.tsbPause,
			this.tsbResume});
			this.toolStrip1.Location = new System.Drawing.Point(0, 0);
			this.toolStrip1.Name = "toolStrip1";
			this.toolStrip1.Size = new System.Drawing.Size(37, 163);
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
			// tsbRemove
			// 
			this.tsbRemove.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbRemove.Image = global::Nexus.Client.Properties.Resources.edit_delete_6;
			this.tsbRemove.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbRemove.Name = "tsbRemove";
			this.tsbRemove.Size = new System.Drawing.Size(34, 36);
			this.tsbRemove.Text = "Remove";
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
			// lvwTasks
			// 
			this.lvwTasks.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
			this.clmOverallMessage,
			this.clmOverallProgress,
			this.clmItemMessage,
			this.clmItemProgress,
			this.clmStatus});
			this.lvwTasks.Dock = System.Windows.Forms.DockStyle.Fill;
			this.lvwTasks.FullRowSelect = true;
			this.lvwTasks.HideSelection = false;
			this.lvwTasks.Location = new System.Drawing.Point(37, 0);
			this.lvwTasks.Name = "lvwTasks";
			this.lvwTasks.ShowItemToolTips = true;
			this.lvwTasks.Size = new System.Drawing.Size(516, 163);
			this.lvwTasks.TabIndex = 1;
			this.lvwTasks.UseCompatibleStateImageBehavior = false;
			this.lvwTasks.View = System.Windows.Forms.View.Details;
			this.lvwTasks.Resize += new System.EventHandler(this.lvwTasks_Resize);
			this.lvwTasks.SelectedIndexChanged += new System.EventHandler(this.lvwTasks_SelectedIndexChanged);
			this.lvwTasks.ColumnWidthChanging += new System.Windows.Forms.ColumnWidthChangingEventHandler(this.lvwTasks_ColumnWidthChanging);
			// 
			// clmOverallMessage
			// 
			this.clmOverallMessage.Text = "Overall Message";
			// 
			// clmOverallProgress
			// 
			this.clmOverallProgress.Text = "Overall Progress";
			this.clmOverallProgress.Width = 92;
			// 
			// clmItemMessage
			// 
			this.clmItemMessage.Text = "Step Message";
			// 
			// clmItemProgress
			// 
			this.clmItemProgress.Text = "Step Progress";
			this.clmItemProgress.Width = 81;
			// 
			// clmStatus
			// 
			this.clmStatus.Text = "Status";
			this.clmStatus.Width = 68;
			// 
			// ActivityMonitorControl
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(553, 163);
			this.CloseButton = false;
			this.CloseButtonVisible = false;
			this.Controls.Add(this.lvwTasks);
			this.Controls.Add(this.toolStrip1);
			this.Name = "ActivityMonitorControl";
			this.toolStrip1.ResumeLayout(false);
			this.toolStrip1.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.ToolStrip toolStrip1;
		private System.Windows.Forms.ToolStripButton tsbCancel;
		private Nexus.UI.Controls.DoubleBufferedListView lvwTasks;
		private System.Windows.Forms.ColumnHeader clmOverallMessage;
		private System.Windows.Forms.ColumnHeader clmOverallProgress;
		private System.Windows.Forms.ColumnHeader clmItemMessage;
		private System.Windows.Forms.ColumnHeader clmItemProgress;
		private System.Windows.Forms.ColumnHeader clmStatus;
		private System.Windows.Forms.ToolStripButton tsbRemove;
		private System.Windows.Forms.ToolStripButton tsbPause;
		private System.Windows.Forms.ToolStripButton tsbResume;
	}
}
