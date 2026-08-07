namespace Nexus.Client.ActivityMonitoring.UI
{
	partial class ActivityMonitorControl
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
			this.tsbCancel = new DevExpress.XtraBars.BarButtonItem();
			this.tsbRemove = new DevExpress.XtraBars.BarButtonItem();
			this.tsbPause = new DevExpress.XtraBars.BarButtonItem();
			this.tsbResume = new DevExpress.XtraBars.BarButtonItem();
			this.gridControl = new DevExpress.XtraGrid.GridControl();
			this.gridView = new DevExpress.XtraGrid.Views.Grid.GridView();
			((System.ComponentModel.ISupportInitialize)(this.barManager)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.gridControl)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.gridView)).BeginInit();
			this.SuspendLayout();

			this.barManager.Bars.AddRange(new DevExpress.XtraBars.Bar[] { this.barActions });
			this.barManager.DockControls.Add(this.barDockControlTop);
			this.barManager.DockControls.Add(this.barDockControlBottom);
			this.barManager.DockControls.Add(this.barDockControlLeft);
			this.barManager.DockControls.Add(this.barDockControlRight);
			this.barManager.Form = this;
			this.barManager.Items.AddRange(new DevExpress.XtraBars.BarItem[] { this.tsbCancel, this.tsbRemove, this.tsbPause, this.tsbResume });
			this.barManager.MaxItemId = 4;

			this.barActions.BarName = "Activity Actions";
			this.barActions.DockStyle = DevExpress.XtraBars.BarDockStyle.Left;
			this.barActions.LinksPersistInfo.AddRange(new DevExpress.XtraBars.LinkPersistInfo[] {
				new DevExpress.XtraBars.LinkPersistInfo(this.tsbCancel),
				new DevExpress.XtraBars.LinkPersistInfo(this.tsbRemove),
				new DevExpress.XtraBars.LinkPersistInfo(this.tsbPause),
				new DevExpress.XtraBars.LinkPersistInfo(this.tsbResume)
			});
			this.barActions.OptionsBar.AllowQuickCustomization = false;
			this.barActions.OptionsBar.DisableClose = true;
			this.barActions.OptionsBar.DisableCustomization = true;
			this.barActions.OptionsBar.DrawDragBorder = false;

			ConfigureBarButton(this.tsbCancel, 0, "tsbCancel", "Cancel", "Cancel", global::Nexus.Client.Properties.Resources.edit_delete);
			ConfigureBarButton(this.tsbRemove, 1, "tsbRemove", "Remove", "Remove", global::Nexus.Client.Properties.Resources.edit_delete_6);
			ConfigureBarButton(this.tsbPause, 2, "tsbPause", "Pause", "Pause", global::Nexus.Client.Properties.Resources.media_playback_pause_7);
			ConfigureBarButton(this.tsbResume, 3, "tsbResume", "Resume", "Resume", global::Nexus.Client.Properties.Resources.media_playback_start_7);

			this.barDockControlTop.CausesValidation = false; this.barDockControlTop.Dock = System.Windows.Forms.DockStyle.Top; this.barDockControlTop.Manager = this.barManager;
			this.barDockControlBottom.CausesValidation = false; this.barDockControlBottom.Dock = System.Windows.Forms.DockStyle.Bottom; this.barDockControlBottom.Manager = this.barManager;
			this.barDockControlLeft.CausesValidation = false; this.barDockControlLeft.Dock = System.Windows.Forms.DockStyle.Left; this.barDockControlLeft.Manager = this.barManager;
			this.barDockControlRight.CausesValidation = false; this.barDockControlRight.Dock = System.Windows.Forms.DockStyle.Right; this.barDockControlRight.Manager = this.barManager;

			this.gridControl.Dock = System.Windows.Forms.DockStyle.Fill;
			this.gridControl.Location = new System.Drawing.Point(40, 0);
			this.gridControl.MainView = this.gridView;
			this.gridControl.Name = "gridControl";
			this.gridControl.Size = new System.Drawing.Size(513, 163);
			this.gridControl.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] { this.gridView });

			this.gridView.GridControl = this.gridControl;
			this.gridView.Name = "gridView";
			this.gridView.OptionsBehavior.Editable = false;
			this.gridView.OptionsSelection.EnableAppearanceFocusedCell = false;
			this.gridView.OptionsView.ShowGroupPanel = false;
			this.gridView.Columns.AddVisible("OverallMessage", "Overall Message").Width = 170;
			this.gridView.Columns.AddVisible("OverallProgress", "Overall Progress").Width = 90;
			this.gridView.Columns.AddVisible("ItemMessage", "Step Message").Width = 150;
			this.gridView.Columns.AddVisible("ItemProgress", "Step Progress").Width = 80;
			this.gridView.Columns.AddVisible("Status", "Status").Width = 70;
			this.gridView.FocusedRowChanged += new DevExpress.XtraGrid.Views.Base.FocusedRowChangedEventHandler(this.gridView_FocusedRowChanged);

			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(553, 163);
			this.CloseButton = false;
			this.CloseButtonVisible = false;
			this.Controls.Add(this.gridControl);
			this.Controls.Add(this.barDockControlLeft);
			this.Controls.Add(this.barDockControlRight);
			this.Controls.Add(this.barDockControlBottom);
			this.Controls.Add(this.barDockControlTop);
			this.Name = "ActivityMonitorControl";
			((System.ComponentModel.ISupportInitialize)(this.barManager)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.gridControl)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.gridView)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();
		}

		/// <summary>Configures an activity-toolbar button.</summary>
		private static void ConfigureBarButton(DevExpress.XtraBars.BarButtonItem button, int id, string name, string caption, string hint, System.Drawing.Image image)
		{
			button.Id = id;
			button.Name = name;
			button.Caption = caption;
			button.Hint = hint;
			button.ImageOptions.Image = image;
			button.PaintStyle = DevExpress.XtraBars.BarItemPaintStyle.Standard;
		}

		private DevExpress.XtraBars.BarManager barManager;
		private DevExpress.XtraBars.Bar barActions;
		private DevExpress.XtraBars.BarDockControl barDockControlTop;
		private DevExpress.XtraBars.BarDockControl barDockControlBottom;
		private DevExpress.XtraBars.BarDockControl barDockControlLeft;
		private DevExpress.XtraBars.BarDockControl barDockControlRight;
		private DevExpress.XtraBars.BarButtonItem tsbCancel;
		private DevExpress.XtraBars.BarButtonItem tsbRemove;
		private DevExpress.XtraBars.BarButtonItem tsbPause;
		private DevExpress.XtraBars.BarButtonItem tsbResume;
		private DevExpress.XtraGrid.GridControl gridControl;
		private DevExpress.XtraGrid.Views.Grid.GridView gridView;
	}
}
