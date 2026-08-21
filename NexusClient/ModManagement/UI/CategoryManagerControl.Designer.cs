namespace Nexus.Client.ModManagement.UI
{
    partial class CategoryManagerControl
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.barManagerCategory = new DevExpress.XtraBars.BarManager(this.components);
            this.barCategoryActions = new DevExpress.XtraBars.Bar();
            this.barDockControlTop = new DevExpress.XtraBars.BarDockControl();
            this.barDockControlBottom = new DevExpress.XtraBars.BarDockControl();
            this.barDockControlLeft = new DevExpress.XtraBars.BarDockControl();
            this.barDockControlRight = new DevExpress.XtraBars.BarDockControl();
            this.tsbAddCategory = new DevExpress.XtraBars.BarButtonItem();
            this.tsbRenameCategory = new DevExpress.XtraBars.BarButtonItem();
            this.tsbRemoveCategory = new DevExpress.XtraBars.BarButtonItem();
            this.tsbUpdateFromNexus = new DevExpress.XtraBars.BarButtonItem();
            this.tsbResetUnassigned = new DevExpress.XtraBars.BarButtonItem();
            this.tsbResetAllToUnassigned = new DevExpress.XtraBars.BarButtonItem();
            this.tsbRemoveAllCategories = new DevExpress.XtraBars.BarButtonItem();
            this.gridControl = new DevExpress.XtraGrid.GridControl();
            this.gridView = new DevExpress.XtraGrid.Views.Grid.GridView();
            ((System.ComponentModel.ISupportInitialize)(this.barManagerCategory)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridControl)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridView)).BeginInit();
            this.SuspendLayout();
            //
            // barManagerCategory
            //
            this.barManagerCategory.Bars.AddRange(new DevExpress.XtraBars.Bar[] { this.barCategoryActions });
            this.barManagerCategory.DockControls.Add(this.barDockControlTop);
            this.barManagerCategory.DockControls.Add(this.barDockControlBottom);
            this.barManagerCategory.DockControls.Add(this.barDockControlLeft);
            this.barManagerCategory.DockControls.Add(this.barDockControlRight);
            this.barManagerCategory.Form = this;
            this.barManagerCategory.Items.AddRange(new DevExpress.XtraBars.BarItem[] {
                this.tsbAddCategory,
                this.tsbRenameCategory,
                this.tsbRemoveCategory,
                this.tsbUpdateFromNexus,
                this.tsbResetUnassigned,
                this.tsbResetAllToUnassigned,
                this.tsbRemoveAllCategories
            });
            this.barManagerCategory.MaxItemId = 7;
            //
            // barCategoryActions
            //
            this.barCategoryActions.BarName = "Category Actions";
            this.barCategoryActions.DockCol = 0;
            this.barCategoryActions.DockRow = 0;
            this.barCategoryActions.DockStyle = DevExpress.XtraBars.BarDockStyle.Left;
            this.barCategoryActions.LinksPersistInfo.AddRange(new DevExpress.XtraBars.LinkPersistInfo[] {
                new DevExpress.XtraBars.LinkPersistInfo(this.tsbAddCategory),
                new DevExpress.XtraBars.LinkPersistInfo(this.tsbRenameCategory),
                new DevExpress.XtraBars.LinkPersistInfo(this.tsbRemoveCategory),
                new DevExpress.XtraBars.LinkPersistInfo(this.tsbUpdateFromNexus, true),
                new DevExpress.XtraBars.LinkPersistInfo(this.tsbResetUnassigned),
                new DevExpress.XtraBars.LinkPersistInfo(this.tsbResetAllToUnassigned),
                new DevExpress.XtraBars.LinkPersistInfo(this.tsbRemoveAllCategories)
            });
            this.barCategoryActions.OptionsBar.AllowQuickCustomization = false;
            this.barCategoryActions.OptionsBar.DisableClose = true;
            this.barCategoryActions.OptionsBar.DisableCustomization = true;
            this.barCategoryActions.OptionsBar.DrawDragBorder = false;
            this.barCategoryActions.OptionsBar.RotateWhenVertical = false;
            this.barCategoryActions.OptionsBar.UseWholeRow = true;
            this.barCategoryActions.Text = "Category Actions";
            //
            // tsbAddCategory
            //
            this.tsbAddCategory.Caption = "Add Category";
            this.tsbAddCategory.Hint = "Add a new category";
            this.tsbAddCategory.Id = 0;
            this.tsbAddCategory.ImageOptions.Image = global::Nexus.Client.Properties.Resources.categories_add_new;
            this.tsbAddCategory.Name = "tsbAddCategory";
            this.tsbAddCategory.PaintStyle = DevExpress.XtraBars.BarItemPaintStyle.Standard;
            this.tsbAddCategory.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.tsbAddCategory_Click);
            //
            // tsbRenameCategory
            //
            this.tsbRenameCategory.Caption = "Rename Category";
            this.tsbRenameCategory.Hint = "Rename the selected category (F2)";
            this.tsbRenameCategory.Id = 1;
            this.tsbRenameCategory.ImageOptions.Image = global::Nexus.Client.Properties.Resources.categories_rename;
            this.tsbRenameCategory.Name = "tsbRenameCategory";
            this.tsbRenameCategory.PaintStyle = DevExpress.XtraBars.BarItemPaintStyle.Standard;
            this.tsbRenameCategory.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.tsbRenameCategory_Click);
            //
            // tsbRemoveCategory
            //
            this.tsbRemoveCategory.Caption = "Remove Category";
            this.tsbRemoveCategory.Hint = "Remove the selected category";
            this.tsbRemoveCategory.Id = 2;
            this.tsbRemoveCategory.ImageOptions.Image = global::Nexus.Client.Properties.Resources.categories_remove;
            this.tsbRemoveCategory.Name = "tsbRemoveCategory";
            this.tsbRemoveCategory.PaintStyle = DevExpress.XtraBars.BarItemPaintStyle.Standard;
            this.tsbRemoveCategory.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.tsbRemoveCategory_Click);
            //
            // tsbUpdateFromNexus
            //
            this.tsbUpdateFromNexus.Caption = "Update from Nexus";
            this.tsbUpdateFromNexus.Hint = "Update and reset categories to Nexus site defaults";
            this.tsbUpdateFromNexus.Id = 3;
            this.tsbUpdateFromNexus.ImageOptions.Image = global::Nexus.Client.Properties.Resources.categories_update_reset_nexus;
            this.tsbUpdateFromNexus.Name = "tsbUpdateFromNexus";
            this.tsbUpdateFromNexus.PaintStyle = DevExpress.XtraBars.BarItemPaintStyle.Standard;
            this.tsbUpdateFromNexus.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.tsbUpdateFromNexus_Click);
            //
            // tsbResetUnassigned
            //
            this.tsbResetUnassigned.Caption = "Reset Unassigned to Nexus Defaults";
            this.tsbResetUnassigned.Hint = "Reset unassigned mods to Nexus site default categories";
            this.tsbResetUnassigned.Id = 4;
            this.tsbResetUnassigned.ImageOptions.Image = global::Nexus.Client.Properties.Resources.categories_reset_unassigned_nexus;
            this.tsbResetUnassigned.Name = "tsbResetUnassigned";
            this.tsbResetUnassigned.PaintStyle = DevExpress.XtraBars.BarItemPaintStyle.Standard;
            this.tsbResetUnassigned.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.tsbResetUnassigned_Click);
            //
            // tsbResetAllToUnassigned
            //
            this.tsbResetAllToUnassigned.Caption = "Reset All Mods to Unassigned";
            this.tsbResetAllToUnassigned.Hint = "Reset all mods to the Unassigned category";
            this.tsbResetAllToUnassigned.Id = 5;
            this.tsbResetAllToUnassigned.ImageOptions.Image = global::Nexus.Client.Properties.Resources.categories_reset_unassigned;
            this.tsbResetAllToUnassigned.Name = "tsbResetAllToUnassigned";
            this.tsbResetAllToUnassigned.PaintStyle = DevExpress.XtraBars.BarItemPaintStyle.Standard;
            this.tsbResetAllToUnassigned.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.tsbResetAllToUnassigned_Click);
            //
            // tsbRemoveAllCategories
            //
            this.tsbRemoveAllCategories.Caption = "Remove All Categories";
            this.tsbRemoveAllCategories.Hint = "Remove all categories and reset all mods to Unassigned";
            this.tsbRemoveAllCategories.Id = 6;
            this.tsbRemoveAllCategories.ImageOptions.Image = global::Nexus.Client.Properties.Resources.categories_delete_reset_unassigned;
            this.tsbRemoveAllCategories.Name = "tsbRemoveAllCategories";
            this.tsbRemoveAllCategories.PaintStyle = DevExpress.XtraBars.BarItemPaintStyle.Standard;
            this.tsbRemoveAllCategories.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.tsbRemoveAllCategories_Click);
            //
            // barDockControlTop
            //
            this.barDockControlTop.CausesValidation = false;
            this.barDockControlTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.barDockControlTop.Manager = this.barManagerCategory;
            //
            // barDockControlBottom
            //
            this.barDockControlBottom.CausesValidation = false;
            this.barDockControlBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.barDockControlBottom.Manager = this.barManagerCategory;
            //
            // barDockControlLeft
            //
            this.barDockControlLeft.CausesValidation = false;
            this.barDockControlLeft.Dock = System.Windows.Forms.DockStyle.Left;
            this.barDockControlLeft.Manager = this.barManagerCategory;
            //
            // barDockControlRight
            //
            this.barDockControlRight.CausesValidation = false;
            this.barDockControlRight.Dock = System.Windows.Forms.DockStyle.Right;
            this.barDockControlRight.Manager = this.barManagerCategory;
            //
            // gridControl
            //
            this.gridControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridControl.MainView = this.gridView;
            this.gridControl.Name = "gridControl";
            this.gridControl.TabIndex = 1;
            this.gridControl.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] { this.gridView });
            //
            // gridView
            //
            this.gridView.GridControl = this.gridControl;
            this.gridView.Name = "gridView";
            this.gridView.OptionsView.ShowGroupPanel = false;
            this.gridView.OptionsBehavior.Editable = false;

            DevExpress.XtraGrid.Columns.GridColumn colId = new DevExpress.XtraGrid.Columns.GridColumn
            {
                FieldName = "Id",
                Caption = "ID",
                Width = 40,
                VisibleIndex = 0
            };
            colId.OptionsColumn.AllowEdit = false;

            DevExpress.XtraGrid.Columns.GridColumn colName = new DevExpress.XtraGrid.Columns.GridColumn
            {
                FieldName = "CategoryName",
                Caption = "Category Name",
                Width = 300,
                VisibleIndex = 1
            };
            colName.OptionsColumn.AllowEdit = true;

            this.gridView.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] { colId, colName });
            this.gridView.KeyDown += new System.Windows.Forms.KeyEventHandler(this.gridView_KeyDown);
            //
            // CategoryManagerControl
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.gridControl);
            this.Controls.Add(this.barDockControlLeft);
            this.Controls.Add(this.barDockControlRight);
            this.Controls.Add(this.barDockControlBottom);
            this.Controls.Add(this.barDockControlTop);
            this.Name = "CategoryManagerControl";
            this.Size = new System.Drawing.Size(600, 400);
            ((System.ComponentModel.ISupportInitialize)(this.barManagerCategory)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridControl)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridView)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private DevExpress.XtraBars.BarManager barManagerCategory;
        private DevExpress.XtraBars.Bar barCategoryActions;
        private DevExpress.XtraBars.BarDockControl barDockControlTop;
        private DevExpress.XtraBars.BarDockControl barDockControlBottom;
        private DevExpress.XtraBars.BarDockControl barDockControlLeft;
        private DevExpress.XtraBars.BarDockControl barDockControlRight;
        private DevExpress.XtraBars.BarButtonItem tsbAddCategory;
        private DevExpress.XtraBars.BarButtonItem tsbRenameCategory;
        private DevExpress.XtraBars.BarButtonItem tsbRemoveCategory;
        private DevExpress.XtraBars.BarButtonItem tsbUpdateFromNexus;
        private DevExpress.XtraBars.BarButtonItem tsbResetUnassigned;
        private DevExpress.XtraBars.BarButtonItem tsbResetAllToUnassigned;
        private DevExpress.XtraBars.BarButtonItem tsbRemoveAllCategories;
        private DevExpress.XtraGrid.GridControl gridControl;
        private DevExpress.XtraGrid.Views.Grid.GridView gridView;
    }
}
