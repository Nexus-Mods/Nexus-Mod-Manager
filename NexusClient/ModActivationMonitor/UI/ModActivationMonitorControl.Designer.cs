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
			this.components = new System.ComponentModel.Container();
			this.barManager = new DevExpress.XtraBars.BarManager(this.components);
			this.barActions = new DevExpress.XtraBars.Bar();
			this.tsbCancel = new DevExpress.XtraBars.BarButtonItem();
			this.tsbRemoveQueued = new DevExpress.XtraBars.BarButtonItem();
			this.tsbRemoveAll = new DevExpress.XtraBars.BarButtonItem();
			this.copyItem = new DevExpress.XtraBars.BarButtonItem();
			this.barDockControlTop = new DevExpress.XtraBars.BarDockControl();
			this.barDockControlBottom = new DevExpress.XtraBars.BarDockControl();
			this.barDockControlLeft = new DevExpress.XtraBars.BarDockControl();
			this.barDockControlRight = new DevExpress.XtraBars.BarDockControl();
			this.popupMenu = new DevExpress.XtraBars.PopupMenu(this.components);
			this.gridControl = new DevExpress.XtraGrid.GridControl();
			this.gridView = new DevExpress.XtraGrid.Views.Grid.GridView();
			((System.ComponentModel.ISupportInitialize)(this.barManager)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.popupMenu)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.gridControl)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.gridView)).BeginInit();
			this.SuspendLayout();
			//
			// barManager
			//
			this.barManager.Bars.AddRange(new DevExpress.XtraBars.Bar[] {
            this.barActions});
			this.barManager.DockControls.Add(this.barDockControlTop);
			this.barManager.DockControls.Add(this.barDockControlBottom);
			this.barManager.DockControls.Add(this.barDockControlLeft);
			this.barManager.DockControls.Add(this.barDockControlRight);
			this.barManager.Form = this;
			this.barManager.Items.AddRange(new DevExpress.XtraBars.BarItem[] {
            this.tsbCancel,
            this.tsbRemoveQueued,
            this.tsbRemoveAll,
            this.copyItem});
			this.barManager.MaxItemId = 4;
			//
			// barActions
			//
			this.barActions.BarName = "Activation Actions";
			this.barActions.DockStyle = DevExpress.XtraBars.BarDockStyle.Left;
			this.barActions.LinksPersistInfo.AddRange(new DevExpress.XtraBars.LinkPersistInfo[] {
            new DevExpress.XtraBars.LinkPersistInfo(this.tsbCancel),
            new DevExpress.XtraBars.LinkPersistInfo(this.tsbRemoveQueued),
            new DevExpress.XtraBars.LinkPersistInfo(this.tsbRemoveAll)});
			this.barActions.OptionsBar.AllowQuickCustomization = false;
			this.barActions.OptionsBar.DisableClose = true;
			this.barActions.OptionsBar.DisableCustomization = true;
			this.barActions.OptionsBar.DrawDragBorder = false;
			this.barActions.OptionsBar.RotateWhenVertical = false;
			//
			// tsbCancel
			//
			ConfigureBarButton(this.tsbCancel, 0, "tsbCancel", "Cancel");
			//
			// tsbRemoveQueued
			//
			ConfigureBarButton(this.tsbRemoveQueued, 1, "tsbRemoveQueued", "Remove queued");
			//
			// tsbRemoveAll
			//
			ConfigureBarButton(this.tsbRemoveAll, 2, "tsbRemoveAll", "Remove all");
			//
			// copyItem
			//
			this.copyItem.Caption = "Copy to clipboard";
			this.copyItem.Id = 3;
			this.copyItem.Name = "copyItem";
			this.copyItem.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.copyItem_ItemClick);
			//
			// barDockControlTop
			//
			this.barDockControlTop.CausesValidation = false;
			this.barDockControlTop.Dock = System.Windows.Forms.DockStyle.Top;
			this.barDockControlTop.Location = new System.Drawing.Point(0, 0);
			this.barDockControlTop.Manager = this.barManager;
			this.barDockControlTop.Size = new System.Drawing.Size(553, 0);
			//
			// barDockControlBottom
			//
			this.barDockControlBottom.CausesValidation = false;
			this.barDockControlBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.barDockControlBottom.Location = new System.Drawing.Point(0, 183);
			this.barDockControlBottom.Manager = this.barManager;
			this.barDockControlBottom.Size = new System.Drawing.Size(553, 0);
			//
			// barDockControlLeft
			//
			this.barDockControlLeft.CausesValidation = false;
			this.barDockControlLeft.Dock = System.Windows.Forms.DockStyle.Left;
			this.barDockControlLeft.Location = new System.Drawing.Point(0, 0);
			this.barDockControlLeft.Manager = this.barManager;
			this.barDockControlLeft.Size = new System.Drawing.Size(32, 183);
			//
			// barDockControlRight
			//
			this.barDockControlRight.CausesValidation = false;
			this.barDockControlRight.Dock = System.Windows.Forms.DockStyle.Right;
			this.barDockControlRight.Location = new System.Drawing.Point(553, 0);
			this.barDockControlRight.Manager = this.barManager;
			this.barDockControlRight.Size = new System.Drawing.Size(0, 183);
			//
			// popupMenu
			//
			this.popupMenu.LinksPersistInfo.AddRange(new DevExpress.XtraBars.LinkPersistInfo[] {
            new DevExpress.XtraBars.LinkPersistInfo(this.copyItem)});
			this.popupMenu.Manager = this.barManager;
			//
			// gridControl
			//
			this.gridControl.Dock = System.Windows.Forms.DockStyle.Fill;
			this.gridControl.Location = new System.Drawing.Point(32, 0);
			this.gridControl.MainView = this.gridView;
			this.gridControl.MenuManager = this.barManager;
			this.gridControl.Name = "gridControl";
			this.gridControl.Size = new System.Drawing.Size(521, 183);
			this.gridControl.TabIndex = 4;
			this.gridControl.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] {
            this.gridView});
			this.gridControl.KeyUp += new System.Windows.Forms.KeyEventHandler(this.gridControl_KeyUp);
			this.gridControl.MouseUp += new System.Windows.Forms.MouseEventHandler(this.gridControl_MouseUp);
			//
			// gridView
			//
			this.gridView.GridControl = this.gridControl;
			this.gridView.Name = "gridView";
			this.gridView.OptionsBehavior.Editable = false;
			this.gridView.OptionsCustomization.AllowColumnMoving = false;
			this.gridView.OptionsSelection.EnableAppearanceFocusedCell = false;
			this.gridView.OptionsView.ShowGroupPanel = false;
			this.gridView.OptionsView.ColumnAutoWidth = true;
			this.gridView.Columns.AddVisible("ModName", "Name").Width = 180;
			this.gridView.Columns.AddVisible("Status", "Status").Width = 90;
			this.gridView.Columns.AddVisible("Operation", "Operation").Width = 90;
			this.gridView.Columns.AddVisible("Progress", "Progress").Width = 170;
			this.gridView.Columns.AddVisible("ErrorInfo", "?").Width = 30;
			this.gridView.FocusedRowChanged += new DevExpress.XtraGrid.Views.Base.FocusedRowChangedEventHandler(this.gridView_FocusedRowChanged);
			this.gridView.RowCellClick += new DevExpress.XtraGrid.Views.Grid.RowCellClickEventHandler(this.gridView_RowCellClick);
			//
			// ModActivationMonitorControl
			//
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(553, 183);
			this.Controls.Add(this.gridControl);
			this.Controls.Add(this.barDockControlLeft);
			this.Controls.Add(this.barDockControlRight);
			this.Controls.Add(this.barDockControlBottom);
			this.Controls.Add(this.barDockControlTop);
			this.Name = "ActiveModsMonitorControl";
			((System.ComponentModel.ISupportInitialize)(this.barManager)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.popupMenu)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.gridControl)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.gridView)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		/// <summary>Configures an activation-monitor toolbar button.</summary>
		private static void ConfigureBarButton(DevExpress.XtraBars.BarButtonItem button, int id, string name, string caption)
		{
			button.Id = id; button.Name = name; button.Caption = caption; button.Hint = caption; button.PaintStyle = DevExpress.XtraBars.BarItemPaintStyle.Standard;
		}

		#endregion

		private DevExpress.XtraBars.BarManager barManager;
		private DevExpress.XtraBars.Bar barActions;
		private DevExpress.XtraBars.BarButtonItem tsbCancel;
		private DevExpress.XtraBars.BarButtonItem tsbRemoveQueued;
		private DevExpress.XtraBars.BarButtonItem tsbRemoveAll;
		private DevExpress.XtraBars.BarButtonItem copyItem;
		private DevExpress.XtraBars.BarDockControl barDockControlTop;
		private DevExpress.XtraBars.BarDockControl barDockControlBottom;
		private DevExpress.XtraBars.BarDockControl barDockControlLeft;
		private DevExpress.XtraBars.BarDockControl barDockControlRight;
		private DevExpress.XtraBars.PopupMenu popupMenu;
		private DevExpress.XtraGrid.GridControl gridControl;
		private DevExpress.XtraGrid.Views.Grid.GridView gridView;
	}
}
