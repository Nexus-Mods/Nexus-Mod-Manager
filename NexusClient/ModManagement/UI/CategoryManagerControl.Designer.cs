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
            this.components              = new System.ComponentModel.Container();
            this.toolStrip1              = new System.Windows.Forms.ToolStrip();
            this.tsbAddCategory          = new System.Windows.Forms.ToolStripButton();
            this.tsbRenameCategory       = new System.Windows.Forms.ToolStripButton();
            this.tsbRemoveCategory       = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator1     = new System.Windows.Forms.ToolStripSeparator();
            this.toolStripSeparator2     = new System.Windows.Forms.ToolStripSeparator();
            this.tsbUpdateFromNexus      = new System.Windows.Forms.ToolStripButton();
            this.tsbResetUnassigned      = new System.Windows.Forms.ToolStripButton();
            this.tsbResetAllToUnassigned = new System.Windows.Forms.ToolStripButton();
            this.tsbRemoveAllCategories  = new System.Windows.Forms.ToolStripButton();
            this.gridControl             = new DevExpress.XtraGrid.GridControl();
            this.gridView                = new DevExpress.XtraGrid.Views.Grid.GridView();

            ((System.ComponentModel.ISupportInitialize)(this.gridControl)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridView)).BeginInit();
            this.toolStrip1.SuspendLayout();
            this.SuspendLayout();

            // ── toolStrip1 ──────────────────────────────────────────────────────
            this.toolStrip1.Dock             = System.Windows.Forms.DockStyle.Left;
            this.toolStrip1.LayoutStyle      = System.Windows.Forms.ToolStripLayoutStyle.VerticalStackWithOverflow;
            this.toolStrip1.GripStyle        = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.toolStrip1.ImageScalingSize = new System.Drawing.Size(32, 32);
            this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.tsbAddCategory,
                this.tsbRenameCategory,
                this.tsbRemoveCategory,
                this.toolStripSeparator1,
                this.toolStripSeparator2,
                this.tsbUpdateFromNexus,
                this.tsbResetUnassigned,
                this.tsbResetAllToUnassigned,
                this.tsbRemoveAllCategories
            });
            this.toolStrip1.Name     = "toolStrip1";
            this.toolStrip1.TabIndex = 0;

            // ── tsbAddCategory ──────────────────────────────────────────────────
            this.tsbAddCategory.Image        = global::Nexus.Client.Properties.Resources.categories_add_new;
            this.tsbAddCategory.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.tsbAddCategory.Name         = "tsbAddCategory";
            this.tsbAddCategory.Text         = "Add Category";
            this.tsbAddCategory.ToolTipText  = "Add a new category";
            this.tsbAddCategory.Click       += new System.EventHandler(this.tsbAddCategory_Click);

            // ── tsbRenameCategory ───────────────────────────────────────────────
            this.tsbRenameCategory.Image        = global::Nexus.Client.Properties.Resources.categories_rename;
            this.tsbRenameCategory.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.tsbRenameCategory.Name         = "tsbRenameCategory";
            this.tsbRenameCategory.Text         = "Rename Category";
            this.tsbRenameCategory.ToolTipText  = "Rename the selected category (F2)";
            this.tsbRenameCategory.Click       += new System.EventHandler(this.tsbRenameCategory_Click);

            // ── tsbRemoveCategory ───────────────────────────────────────────────
            this.tsbRemoveCategory.Image        = global::Nexus.Client.Properties.Resources.categories_remove;
            this.tsbRemoveCategory.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.tsbRemoveCategory.Name         = "tsbRemoveCategory";
            this.tsbRemoveCategory.Text         = "Remove Category";
            this.tsbRemoveCategory.ToolTipText  = "Remove the selected category";
            this.tsbRemoveCategory.Click       += new System.EventHandler(this.tsbRemoveCategory_Click);

            // ── toolStripSeparator1 ─────────────────────────────────────────────
            this.toolStripSeparator1.Name = "toolStripSeparator1";

            // ── toolStripSeparator2 ─────────────────────────────────────────────
            this.toolStripSeparator2.Name = "toolStripSeparator2";

            // ── tsbUpdateFromNexus ──────────────────────────────────────────────
            this.tsbUpdateFromNexus.Image        = global::Nexus.Client.Properties.Resources.categories_update_reset_nexus;
            this.tsbUpdateFromNexus.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.tsbUpdateFromNexus.Name         = "tsbUpdateFromNexus";
            this.tsbUpdateFromNexus.Text         = "Update from Nexus";
            this.tsbUpdateFromNexus.ToolTipText  = "Update and reset categories to Nexus site defaults";
            this.tsbUpdateFromNexus.Click       += new System.EventHandler(this.tsbUpdateFromNexus_Click);

            // ── tsbResetUnassigned ──────────────────────────────────────────────
            this.tsbResetUnassigned.Image        = global::Nexus.Client.Properties.Resources.categories_reset_unassigned_nexus;
            this.tsbResetUnassigned.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.tsbResetUnassigned.Name         = "tsbResetUnassigned";
            this.tsbResetUnassigned.Text         = "Reset Unassigned to Nexus Defaults";
            this.tsbResetUnassigned.ToolTipText  = "Reset unassigned mods to Nexus site default categories";
            this.tsbResetUnassigned.Click       += new System.EventHandler(this.tsbResetUnassigned_Click);

            // ── tsbResetAllToUnassigned ─────────────────────────────────────────
            this.tsbResetAllToUnassigned.Image        = global::Nexus.Client.Properties.Resources.categories_reset_unassigned;
            this.tsbResetAllToUnassigned.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.tsbResetAllToUnassigned.Name         = "tsbResetAllToUnassigned";
            this.tsbResetAllToUnassigned.Text         = "Reset All Mods to Unassigned";
            this.tsbResetAllToUnassigned.ToolTipText  = "Reset all mods to the Unassigned category";
            this.tsbResetAllToUnassigned.Click       += new System.EventHandler(this.tsbResetAllToUnassigned_Click);

            // ── tsbRemoveAllCategories ──────────────────────────────────────────
            this.tsbRemoveAllCategories.Image        = global::Nexus.Client.Properties.Resources.categories_delete_reset_unassigned;
            this.tsbRemoveAllCategories.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.tsbRemoveAllCategories.Name         = "tsbRemoveAllCategories";
            this.tsbRemoveAllCategories.Text         = "Remove All Categories";
            this.tsbRemoveAllCategories.ToolTipText  = "Remove all categories and reset all mods to Unassigned";
            this.tsbRemoveAllCategories.Click       += new System.EventHandler(this.tsbRemoveAllCategories_Click);

            // ── gridControl ─────────────────────────────────────────────────────
            this.gridControl.Dock     = System.Windows.Forms.DockStyle.Fill;
            this.gridControl.MainView = this.gridView;
            this.gridControl.Name     = "gridControl";
            this.gridControl.TabIndex = 1;
            this.gridControl.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] { this.gridView });

            // ── gridView ────────────────────────────────────────────────────────
            this.gridView.GridControl                    = this.gridControl;
            this.gridView.Name                           = "gridView";
            this.gridView.OptionsView.ShowGroupPanel     = false;
            this.gridView.OptionsBehavior.Editable       = false;

            DevExpress.XtraGrid.Columns.GridColumn colId = new DevExpress.XtraGrid.Columns.GridColumn
            {
                FieldName = "Id",
                Caption   = "ID",
                Width     = 40,
                VisibleIndex = 0
            };
            colId.OptionsColumn.AllowEdit = false;

            DevExpress.XtraGrid.Columns.GridColumn colName = new DevExpress.XtraGrid.Columns.GridColumn
            {
                FieldName    = "CategoryName",
                Caption      = "Category Name",
                Width        = 300,
                VisibleIndex = 1
            };
            colName.OptionsColumn.AllowEdit = true;

            this.gridView.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] { colId, colName });

            this.gridView.KeyDown           += new System.Windows.Forms.KeyEventHandler(this.gridView_KeyDown);

            // ── CategoryManagerControl ──────────────────────────────────────────
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode       = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.gridControl);
            this.Controls.Add(this.toolStrip1);
            this.Name = "CategoryManagerControl";
            this.Size = new System.Drawing.Size(600, 400);

            ((System.ComponentModel.ISupportInitialize)(this.gridControl)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridView)).EndInit();
            this.toolStrip1.ResumeLayout(false);
            this.toolStrip1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.ToolStrip             toolStrip1;
        private System.Windows.Forms.ToolStripButton       tsbAddCategory;
        private System.Windows.Forms.ToolStripButton       tsbRenameCategory;
        private System.Windows.Forms.ToolStripButton       tsbRemoveCategory;
        private System.Windows.Forms.ToolStripSeparator    toolStripSeparator1;
        private System.Windows.Forms.ToolStripSeparator    toolStripSeparator2;
        private System.Windows.Forms.ToolStripButton       tsbUpdateFromNexus;
        private System.Windows.Forms.ToolStripButton       tsbResetUnassigned;
        private System.Windows.Forms.ToolStripButton       tsbResetAllToUnassigned;
        private System.Windows.Forms.ToolStripButton       tsbRemoveAllCategories;
        private DevExpress.XtraGrid.GridControl            gridControl;
        private DevExpress.XtraGrid.Views.Grid.GridView    gridView;
    }
}
