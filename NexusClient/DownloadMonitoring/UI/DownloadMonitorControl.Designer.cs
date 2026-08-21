namespace Nexus.Client.DownloadMonitoring.UI
{
	partial class DownloadMonitorControl
	{
		private System.ComponentModel.IContainer components = null;

		protected override void Dispose(bool disposing)
		{
			if (disposing && components != null)
				components.Dispose();
			base.Dispose(disposing);
		}

		private void InitializeComponent()
		{
			this.components = new System.ComponentModel.Container();
			this.barManager = new DevExpress.XtraBars.BarManager(this.components);
			this.barActions = new DevExpress.XtraBars.Bar();
			this.barDockControlTop = new DevExpress.XtraBars.BarDockControl();
			this.barDockControlBottom = new DevExpress.XtraBars.BarDockControl();
			this.barDockControlLeft = new DevExpress.XtraBars.BarDockControl();
			this.barDockControlRight = new DevExpress.XtraBars.BarDockControl();
			this.tsbResume = new DevExpress.XtraBars.BarButtonItem();
			this.tsbCancel = new DevExpress.XtraBars.BarButtonItem();
			this.tsbPause = new DevExpress.XtraBars.BarButtonItem();
			this.tsbRemove = new DevExpress.XtraBars.BarButtonItem();
			this.tsbResumeAll = new DevExpress.XtraBars.BarButtonItem();
			this.tsbRemoveAll = new DevExpress.XtraBars.BarButtonItem();
			this.tsbPurgeDownloads = new DevExpress.XtraBars.BarButtonItem();
			this.copyItem = new DevExpress.XtraBars.BarButtonItem();
			this.popupMenu = new DevExpress.XtraBars.PopupMenu(this.components);
			this.gridControl = new DevExpress.XtraGrid.GridControl();
			this.gridView = new DevExpress.XtraGrid.Views.Grid.GridView();
			((System.ComponentModel.ISupportInitialize)(this.barManager)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.popupMenu)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.gridControl)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.gridView)).BeginInit();
			this.SuspendLayout();

			this.barManager.Bars.AddRange(new DevExpress.XtraBars.Bar[] { this.barActions });
			this.barManager.DockControls.Add(this.barDockControlTop);
			this.barManager.DockControls.Add(this.barDockControlBottom);
			this.barManager.DockControls.Add(this.barDockControlLeft);
			this.barManager.DockControls.Add(this.barDockControlRight);
			this.barManager.Form = this;
			this.barManager.Items.AddRange(new DevExpress.XtraBars.BarItem[] { this.tsbResume, this.tsbCancel, this.tsbPause, this.tsbRemove, this.tsbResumeAll, this.tsbRemoveAll, this.tsbPurgeDownloads, this.copyItem });
			this.barManager.MaxItemId = 8;

			this.barActions.BarName = "Download Actions";
			this.barActions.DockStyle = DevExpress.XtraBars.BarDockStyle.Left;
			this.barActions.LinksPersistInfo.AddRange(new DevExpress.XtraBars.LinkPersistInfo[] {
				new DevExpress.XtraBars.LinkPersistInfo(this.tsbResume), new DevExpress.XtraBars.LinkPersistInfo(this.tsbCancel),
				new DevExpress.XtraBars.LinkPersistInfo(this.tsbPause), new DevExpress.XtraBars.LinkPersistInfo(this.tsbRemove),
				new DevExpress.XtraBars.LinkPersistInfo(this.tsbResumeAll, true), new DevExpress.XtraBars.LinkPersistInfo(this.tsbRemoveAll),
				new DevExpress.XtraBars.LinkPersistInfo(this.tsbPurgeDownloads)
			});
			this.barActions.OptionsBar.AllowQuickCustomization = false;
			this.barActions.OptionsBar.DisableClose = true;
			this.barActions.OptionsBar.DisableCustomization = true;
			this.barActions.OptionsBar.DrawDragBorder = false;
			this.barActions.OptionsBar.RotateWhenVertical = false;

			ConfigureBarButton(this.tsbResume, 0, "tsbResume", "Resume", global::Nexus.Client.Properties.Resources.resume_download_flat);
			ConfigureBarButton(this.tsbCancel, 1, "tsbCancel", "Cancel", global::Nexus.Client.Properties.Resources.cancel_download_flat);
			ConfigureBarButton(this.tsbPause, 2, "tsbPause", "Pause", global::Nexus.Client.Properties.Resources.pause_download_flat);
			ConfigureBarButton(this.tsbRemove, 3, "tsbRemove", "Remove", global::Nexus.Client.Properties.Resources.remove_download_flat);
			ConfigureBarButton(this.tsbResumeAll, 4, "tsbResumeAll", "Resume All", global::Nexus.Client.Properties.Resources.playlist);
			ConfigureBarButton(this.tsbRemoveAll, 5, "tsbRemoveAll", "Remove All", global::Nexus.Client.Properties.Resources.list_cleanup_flat);
			ConfigureBarButton(this.tsbPurgeDownloads, 6, "tsbPurgeDownloads", "Purge Downloads", global::Nexus.Client.Properties.Resources.delete_file);
			this.copyItem.Caption = "Copy to clipboard"; this.copyItem.Id = 7; this.copyItem.Name = "copyItem"; this.copyItem.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.copyItem_ItemClick);

			this.popupMenu.Manager = this.barManager;
			this.popupMenu.LinksPersistInfo.Add(new DevExpress.XtraBars.LinkPersistInfo(this.copyItem));

			this.barDockControlTop.CausesValidation = false; this.barDockControlTop.Dock = System.Windows.Forms.DockStyle.Top; this.barDockControlTop.Manager = this.barManager;
			this.barDockControlBottom.CausesValidation = false; this.barDockControlBottom.Dock = System.Windows.Forms.DockStyle.Bottom; this.barDockControlBottom.Manager = this.barManager;
			this.barDockControlLeft.CausesValidation = false; this.barDockControlLeft.Dock = System.Windows.Forms.DockStyle.Left; this.barDockControlLeft.Manager = this.barManager;
			this.barDockControlRight.CausesValidation = false; this.barDockControlRight.Dock = System.Windows.Forms.DockStyle.Right; this.barDockControlRight.Manager = this.barManager;

			this.gridControl.Dock = System.Windows.Forms.DockStyle.Fill;
			this.gridControl.MainView = this.gridView;
			this.gridControl.Name = "gridControl";
			this.gridControl.KeyUp += new System.Windows.Forms.KeyEventHandler(this.gridControl_KeyUp);
			this.gridControl.MouseUp += new System.Windows.Forms.MouseEventHandler(this.gridControl_MouseUp);
			this.gridControl.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] { this.gridView });

			this.gridView.GridControl = this.gridControl;
			this.gridView.Name = "gridView";
			this.gridView.OptionsBehavior.Editable = false;
			this.gridView.OptionsSelection.EnableAppearanceFocusedCell = false;
			this.gridView.OptionsView.ShowGroupPanel = false;
			this.gridView.Columns.AddVisible("OverallMessage", "Name").Width = 180;
			this.gridView.Columns.AddVisible("OverallProgress", "Progress").Width = 90;
			this.gridView.Columns.AddVisible("Status", "Status").Width = 80;
			this.gridView.Columns.AddVisible("ItemMessage", "Speed / Step").Width = 90;
			this.gridView.Columns.AddVisible("FileServer", "Fileserver").Width = 90;
			this.gridView.Columns.AddVisible("ETA", "ETA").Width = 70;
			this.gridView.Columns.AddVisible("ItemProgress", "Threads / Step").Width = 80;
			this.gridView.FocusedRowChanged += new DevExpress.XtraGrid.Views.Base.FocusedRowChangedEventHandler(this.gridView_FocusedRowChanged);

			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(680, 183);
			this.Controls.Add(this.gridControl);
			this.Controls.Add(this.barDockControlLeft);
			this.Controls.Add(this.barDockControlRight);
			this.Controls.Add(this.barDockControlBottom);
			this.Controls.Add(this.barDockControlTop);
			this.Name = "DownloadMonitorControl";
			((System.ComponentModel.ISupportInitialize)(this.barManager)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.popupMenu)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.gridControl)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.gridView)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();
		}

		/// <summary>Configures a download-toolbar button.</summary>
		private static void ConfigureBarButton(DevExpress.XtraBars.BarButtonItem button, int id, string name, string caption, System.Drawing.Image image)
		{
			button.Id = id; button.Name = name; button.Caption = caption; button.Hint = caption; button.ImageOptions.Image = image; button.PaintStyle = DevExpress.XtraBars.BarItemPaintStyle.Standard;
		}

		private DevExpress.XtraBars.BarManager barManager;
		private DevExpress.XtraBars.Bar barActions;
		private DevExpress.XtraBars.BarDockControl barDockControlTop;
		private DevExpress.XtraBars.BarDockControl barDockControlBottom;
		private DevExpress.XtraBars.BarDockControl barDockControlLeft;
		private DevExpress.XtraBars.BarDockControl barDockControlRight;
		private DevExpress.XtraBars.BarButtonItem tsbResume;
		private DevExpress.XtraBars.BarButtonItem tsbCancel;
		private DevExpress.XtraBars.BarButtonItem tsbPause;
		private DevExpress.XtraBars.BarButtonItem tsbRemove;
		private DevExpress.XtraBars.BarButtonItem tsbResumeAll;
		private DevExpress.XtraBars.BarButtonItem tsbRemoveAll;
		private DevExpress.XtraBars.BarButtonItem tsbPurgeDownloads;
		private DevExpress.XtraBars.BarButtonItem copyItem;
		private DevExpress.XtraBars.PopupMenu popupMenu;
		private DevExpress.XtraGrid.GridControl gridControl;
		private DevExpress.XtraGrid.Views.Grid.GridView gridView;
	}
}
