namespace Nexus.Client.ModManagement.UI
{
    partial class ModManagerDXControl
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
            this.toolStrip1 = new System.Windows.Forms.ToolStrip();

            // Add Mod split button
            this.tsbAddMod = new System.Windows.Forms.ToolStripSplitButton();
            this.addModToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.addModFromURLToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();

            // Core mod actions
            this.tsbActivate   = new System.Windows.Forms.ToolStripButton();
            this.tsbDeactivate = new System.Windows.Forms.ToolStripButton();

            // Load order
            this.tsb_SaveModLoadOrder = new System.Windows.Forms.ToolStripButton();
            this.tsb_ModUpLoadOrder   = new System.Windows.Forms.ToolStripButton();
            this.tsb_ModDownLoadOrder = new System.Windows.Forms.ToolStripButton();

            // Tag
            this.tsbTagMod = new System.Windows.Forms.ToolStripButton();

            // Online checks split button
            this.tsbModOnlineChecks = new System.Windows.Forms.ToolStripSplitButton();
            this.withinTheLastDayToolStripMenuItem   = new System.Windows.Forms.ToolStripMenuItem();
            this.withinTheLastWeekToolStripMenuItem  = new System.Windows.Forms.ToolStripMenuItem();
            this.withinTheLastMonthToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.checkForModUpdateWithinTheLastDayToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.checkFileDownloadId    = new System.Windows.Forms.ToolStripMenuItem();
            this.checkMissingDownloadId = new System.Windows.Forms.ToolStripMenuItem();

            // Endorse
            this.tsbToggleEndorse = new System.Windows.Forms.ToolStripButton();

            // Categories split button
            this.tsbResetCategories                       = new System.Windows.Forms.ToolStripSplitButton();
            this.addNewCategory                           = new System.Windows.Forms.ToolStripMenuItem();
            this.collapseAllCategories                    = new System.Windows.Forms.ToolStripMenuItem();
            this.expandAllCategories                      = new System.Windows.Forms.ToolStripMenuItem();
            this.resetDefaultCategories                   = new System.Windows.Forms.ToolStripMenuItem();
            this.resetUnassignedToDefaultCategories       = new System.Windows.Forms.ToolStripMenuItem();
            this.resetModsCategory                        = new System.Windows.Forms.ToolStripMenuItem();
            this.removeAllCategories                      = new System.Windows.Forms.ToolStripMenuItem();
            this.toggleHiddenCategories                   = new System.Windows.Forms.ToolStripMenuItem();

            // Switch view
            this.tsbSwitchView = new System.Windows.Forms.ToolStripButton();

            // Export
            this.tsbExportModList   = new System.Windows.Forms.ToolStripDropDownButton();
            this.exportToTextFile   = new System.Windows.Forms.ToolStripMenuItem();
            this.exportToClipboard  = new System.Windows.Forms.ToolStripMenuItem();

            // Show updates only
            this.tsbShowUpdatesOnly = new System.Windows.Forms.ToolStripButton();

            // Skyrim downloads
            this.tsbSkyrimDownloads = new System.Windows.Forms.ToolStripButton();

            // Mod count label
            this.toolStripLabelModCount = new System.Windows.Forms.ToolStripLabel();

            // Grid controls
            this.gridControl = new DevExpress.XtraGrid.GridControl();
            this.gridView    = new DevExpress.XtraGrid.Views.Grid.GridView();

            ((System.ComponentModel.ISupportInitialize)(this.gridControl)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridView)).BeginInit();
            this.toolStrip1.SuspendLayout();
            this.SuspendLayout();

            // ── toolStrip1 ───────────────────────────────────────────────────
            this.toolStrip1.Dock             = System.Windows.Forms.DockStyle.Top;
            this.toolStrip1.GripStyle        = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.toolStrip1.ImageScalingSize = new System.Drawing.Size(16, 16);
            this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.tsbAddMod,
                this.tsbActivate,
                this.tsbDeactivate,
                this.tsbTagMod,
                new System.Windows.Forms.ToolStripSeparator(),
                this.tsbModOnlineChecks,
                this.tsbToggleEndorse,
                new System.Windows.Forms.ToolStripSeparator(),
                this.tsbResetCategories,
                this.tsbSwitchView,
                this.tsbExportModList,
                new System.Windows.Forms.ToolStripSeparator(),
                this.tsbShowUpdatesOnly,
                this.tsbSkyrimDownloads
            });
            this.toolStrip1.Name     = "toolStrip1";
            this.toolStrip1.TabIndex = 0;
            this.toolStrip1.Font     = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.toolStrip1.Padding  = new System.Windows.Forms.Padding(3, 1, 3, 1);

            // ── tsbAddMod ────────────────────────────────────────────────────
            this.tsbAddMod.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsbAddMod.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.addModToolStripMenuItem,
                this.addModFromURLToolStripMenuItem
            });
            this.tsbAddMod.Image       = global::Nexus.Client.Properties.Resources.add_mod_file_flat;
            this.tsbAddMod.Name        = "tsbAddMod";
            this.tsbAddMod.Text        = "Add Mod";
            this.tsbAddMod.ToolTipText = "Add a mod from a file";
            this.tsbAddMod.ButtonClick += new System.EventHandler(this.tsbAddMod_ButtonClick);

            // ── addModToolStripMenuItem ───────────────────────────────────────
            this.addModToolStripMenuItem.Image = global::Nexus.Client.Properties.Resources.add_mod_file_flat;
            this.addModToolStripMenuItem.Name  = "addModToolStripMenuItem";
            this.addModToolStripMenuItem.Text  = "Add Mod from File";
            this.addModToolStripMenuItem.Click += new System.EventHandler(this.addModToolStripMenuItem_Click);

            // ── addModFromURLToolStripMenuItem ────────────────────────────────
            this.addModFromURLToolStripMenuItem.Image = global::Nexus.Client.Properties.Resources.add_mod_url_flat;
            this.addModFromURLToolStripMenuItem.Name  = "addModFromURLToolStripMenuItem";
            this.addModFromURLToolStripMenuItem.Text  = "Add Mod from URL";
            this.addModFromURLToolStripMenuItem.Click += new System.EventHandler(this.addModFromURLToolStripMenuItem_Click);

            // ── tsbActivate ──────────────────────────────────────────────────
            this.tsbActivate.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsbActivate.Image        = global::Nexus.Client.Properties.Resources.obsidianshade_checkmark;
            this.tsbActivate.Name         = "tsbActivate";
            this.tsbActivate.Text         = "\u2713 Install / Enable";
            this.tsbActivate.ToolTipText  = "Install / enable the selected mod(s)";

            // ── tsbDeactivate ────────────────────────────────────────────────
            this.tsbDeactivate.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsbDeactivate.Image        = global::Nexus.Client.Properties.Resources.remove_download_flat;
            this.tsbDeactivate.Name         = "tsbDeactivate";
            this.tsbDeactivate.Text         = "\u2715 Disable";
            this.tsbDeactivate.ToolTipText  = "Disable the selected mod(s)";

            // ── tsb_SaveModLoadOrder ─────────────────────────────────────────
            this.tsb_SaveModLoadOrder.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.tsb_SaveModLoadOrder.Image        = global::Nexus.Client.Properties.Resources.save_mod_loadorder_flat;
            this.tsb_SaveModLoadOrder.Name         = "tsb_SaveModLoadOrder";
            this.tsb_SaveModLoadOrder.Text         = "Save mod load order";
            this.tsb_SaveModLoadOrder.ToolTipText  = "Save the current mod load order";
            this.tsb_SaveModLoadOrder.Click       += new System.EventHandler(this.tsb_SaveModLoadOrder_Click);

            // ── tsb_ModUpLoadOrder ───────────────────────────────────────────
            this.tsb_ModUpLoadOrder.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.tsb_ModUpLoadOrder.Image        = global::Nexus.Client.Properties.Resources.move_up_flat;
            this.tsb_ModUpLoadOrder.Name         = "tsb_ModUpLoadOrder";
            this.tsb_ModUpLoadOrder.Text         = "Move mod up";
            this.tsb_ModUpLoadOrder.ToolTipText  = "Moves mod up in the load order";
            this.tsb_ModUpLoadOrder.Click       += new System.EventHandler(this.tsb_ModUpLoadOrder_Click);

            // ── tsb_ModDownLoadOrder ─────────────────────────────────────────
            this.tsb_ModDownLoadOrder.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.tsb_ModDownLoadOrder.Image        = global::Nexus.Client.Properties.Resources.move_down_flat;
            this.tsb_ModDownLoadOrder.Name         = "tsb_ModDownLoadOrder";
            this.tsb_ModDownLoadOrder.Text         = "Move mod down";
            this.tsb_ModDownLoadOrder.ToolTipText  = "Moves mod down in the load order";
            this.tsb_ModDownLoadOrder.Click       += new System.EventHandler(this.tsb_ModDownLoadOrder_Click);

            // ── tsbTagMod ────────────────────────────────────────────────────
            this.tsbTagMod.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsbTagMod.Image        = global::Nexus.Client.Properties.Resources.mad_tagger_flat;
            this.tsbTagMod.Name         = "tsbTagMod";
            this.tsbTagMod.Text         = "Tag";
            this.tsbTagMod.ToolTipText  = "Get missing mod info";

            // ── tsbModOnlineChecks ───────────────────────────────────────────
            this.tsbModOnlineChecks.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsbModOnlineChecks.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.checkForModUpdateWithinTheLastDayToolStripMenuItem,
                this.checkFileDownloadId,
                this.checkMissingDownloadId
            });
            this.tsbModOnlineChecks.Image        = global::Nexus.Client.Properties.Resources.check_mod_updates_flat;
            this.tsbModOnlineChecks.Name         = "tsbModOnlineChecks";
            this.tsbModOnlineChecks.Text         = "Updates";
            this.tsbModOnlineChecks.ToolTipText  = "Check for mod updates";
            this.tsbModOnlineChecks.ButtonClick += new System.EventHandler(this.tsbModOnlineChecks_ButtonClick);

            // ── checkForModUpdateWithinTheLastDayToolStripMenuItem ────────────
            this.checkForModUpdateWithinTheLastDayToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.withinTheLastDayToolStripMenuItem,
                this.withinTheLastWeekToolStripMenuItem,
                this.withinTheLastMonthToolStripMenuItem
            });
            this.checkForModUpdateWithinTheLastDayToolStripMenuItem.Image = global::Nexus.Client.Properties.Resources.check_updates_interval_flat;
            this.checkForModUpdateWithinTheLastDayToolStripMenuItem.Name  = "checkForModUpdateWithinTheLastDayToolStripMenuItem";
            this.checkForModUpdateWithinTheLastDayToolStripMenuItem.Text  = "Check for Mod Updates Interval ...";

            this.withinTheLastDayToolStripMenuItem.Name   = "withinTheLastDayToolStripMenuItem";
            this.withinTheLastDayToolStripMenuItem.Text   = "...within the last day";
            this.withinTheLastDayToolStripMenuItem.Click += new System.EventHandler(this.withinTheLastDayToolStripMenuItem_Click);

            this.withinTheLastWeekToolStripMenuItem.Name   = "withinTheLastWeekToolStripMenuItem";
            this.withinTheLastWeekToolStripMenuItem.Text   = "...within the last week";
            this.withinTheLastWeekToolStripMenuItem.Click += new System.EventHandler(this.withinTheLastWeekToolStripMenuItem_Click);

            this.withinTheLastMonthToolStripMenuItem.Name   = "withinTheLastMonthToolStripMenuItem";
            this.withinTheLastMonthToolStripMenuItem.Text   = "...within the last month";
            this.withinTheLastMonthToolStripMenuItem.Click += new System.EventHandler(this.withinTheLastMonthToolStripMenuItem_Click);

            // ── checkFileDownloadId ───────────────────────────────────────────
            this.checkFileDownloadId.Image = global::Nexus.Client.Properties.Resources.check_updates_id_fix_flat;
            this.checkFileDownloadId.Name  = "checkFileDownloadId";
            this.checkFileDownloadId.Text  = "Fix download IDs and Check for mod updates";
            this.checkFileDownloadId.Click += new System.EventHandler(this.checkFileDownloadId_Click);

            // ── checkMissingDownloadId ────────────────────────────────────────
            this.checkMissingDownloadId.Image = global::Nexus.Client.Properties.Resources.check_updates_id_fix_flat;
            this.checkMissingDownloadId.Name  = "checkMissingDownloadId";
            this.checkMissingDownloadId.Text  = "Just check for missing download IDs";
            this.checkMissingDownloadId.Click += new System.EventHandler(this.checkMissingDownloadId_Click);

            // ── tsbToggleEndorse ─────────────────────────────────────────────
            this.tsbToggleEndorse.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsbToggleEndorse.Image        = global::Nexus.Client.Properties.Resources.endorse_flat;
            this.tsbToggleEndorse.Name         = "tsbToggleEndorse";
            this.tsbToggleEndorse.Text         = "Endorse";
            this.tsbToggleEndorse.ToolTipText  = "Toggle mod endorsement";
            this.tsbToggleEndorse.Click       += new System.EventHandler(this.tsbToggleEndorse_Click);

            // ── tsbResetCategories ───────────────────────────────────────────
            this.tsbResetCategories.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsbResetCategories.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.addNewCategory,
                this.collapseAllCategories,
                this.expandAllCategories,
                this.resetDefaultCategories,
                this.resetUnassignedToDefaultCategories,
                this.resetModsCategory,
                this.removeAllCategories,
                this.toggleHiddenCategories
            });
            this.tsbResetCategories.Image       = global::Nexus.Client.Properties.Resources.categories_flat;
            this.tsbResetCategories.Name        = "tsbResetCategories";
            this.tsbResetCategories.Text        = "Categories";
            this.tsbResetCategories.ToolTipText = "Categories: add new category - Click the small arrow for more options";
            this.tsbResetCategories.ButtonClick += new System.EventHandler(this.addNewCategory_Click);

            this.addNewCategory.Image = global::Nexus.Client.Properties.Resources.categories_flat;
            this.addNewCategory.Name  = "addNewCategory";
            this.addNewCategory.Text  = "Categories: add new category";
            this.addNewCategory.Click += new System.EventHandler(this.addNewCategory_Click);

            this.collapseAllCategories.Image = global::Nexus.Client.Properties.Resources.collapse_all;
            this.collapseAllCategories.Name  = "collapseAllCategories";
            this.collapseAllCategories.Text  = "Categories: collapse all categories";
            this.collapseAllCategories.Click += new System.EventHandler(this.collapseAllCategories_Click);

            this.expandAllCategories.Image = global::Nexus.Client.Properties.Resources.expand_all;
            this.expandAllCategories.Name  = "expandAllCategories";
            this.expandAllCategories.Text  = "Categories: expand all categories";
            this.expandAllCategories.Click += new System.EventHandler(this.expandAllCategories_Click);

            this.resetDefaultCategories.Image = global::Nexus.Client.Properties.Resources.reset_default;
            this.resetDefaultCategories.Name  = "resetDefaultCategories";
            this.resetDefaultCategories.Text  = "Categories: Update and reset to Nexus site defaults";
            this.resetDefaultCategories.Click += new System.EventHandler(this.resetDefaultCategories_Click);

            this.resetUnassignedToDefaultCategories.Image = global::Nexus.Client.Properties.Resources.reset_default;
            this.resetUnassignedToDefaultCategories.Name  = "resetUnassignedToDefaultCategories";
            this.resetUnassignedToDefaultCategories.Text  = "Categories: reset Unassigned mods to Nexus site defaults";
            this.resetUnassignedToDefaultCategories.Click += new System.EventHandler(this.resetUnassignedToDefaultCategories_Click);

            this.resetModsCategory.Image = global::Nexus.Client.Properties.Resources.reset_unassigned;
            this.resetModsCategory.Name  = "resetModsCategory";
            this.resetModsCategory.Text  = "Categories: reset all mods to unassigned";
            this.resetModsCategory.Click += new System.EventHandler(this.resetModsCategory_Click);

            this.removeAllCategories.Image = global::Nexus.Client.Properties.Resources.remove_all_categories;
            this.removeAllCategories.Name  = "removeAllCategories";
            this.removeAllCategories.Text  = "Categories: remove all categories";
            this.removeAllCategories.Click += new System.EventHandler(this.removeAllCategories_Click);

            this.toggleHiddenCategories.Image = global::Nexus.Client.Properties.Resources.reset_categories;
            this.toggleHiddenCategories.Name  = "toggleHiddenCategories";
            this.toggleHiddenCategories.Text  = "Categories: toggle hidden categories";
            this.toggleHiddenCategories.Click += new System.EventHandler(this.toggleHiddenCategories_Click);

            // ── tsbSwitchView ────────────────────────────────────────────────
            this.tsbSwitchView.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsbSwitchView.Image        = global::Nexus.Client.Properties.Resources.switch_view_flat;
            this.tsbSwitchView.Name         = "tsbSwitchView";
            this.tsbSwitchView.Text         = "Switch View";
            this.tsbSwitchView.ToolTipText  = "Switches the Mod Manager views";
            this.tsbSwitchView.Click       += new System.EventHandler(this.tsbSwitchView_Click);

            // ── tsbExportModList ─────────────────────────────────────────────
            this.tsbExportModList.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsbExportModList.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.exportToTextFile,
                this.exportToClipboard
            });
            this.tsbExportModList.Image       = global::Nexus.Client.Properties.Resources.export_mod_list_flat;
            this.tsbExportModList.Name        = "tsbExportModList";
            this.tsbExportModList.Text        = "Export";
            this.tsbExportModList.ToolTipText = "Export the current mod list";

            this.exportToTextFile.Image = global::Nexus.Client.Properties.Resources.export_text_file_flat;
            this.exportToTextFile.Name  = "exportToTextFile";
            this.exportToTextFile.Text  = "Text file";

            this.exportToClipboard.Image = global::Nexus.Client.Properties.Resources.export_clipboard_flat;
            this.exportToClipboard.Name  = "exportToClipboard";
            this.exportToClipboard.Text  = "Copy to clipboard";

            // ── tsbShowUpdatesOnly ────────────────────────────────────────────
            this.tsbShowUpdatesOnly.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsbShowUpdatesOnly.Image        = global::Nexus.Client.Properties.Resources.update_warning_disabled;
            this.tsbShowUpdatesOnly.Name         = "tsbShowUpdatesOnly";
            this.tsbShowUpdatesOnly.CheckOnClick = false;
            this.tsbShowUpdatesOnly.Text         = "Updates Only";
            this.tsbShowUpdatesOnly.ToolTipText  = "Toggles filtering the mod list showing only mods requiring an update";
            this.tsbShowUpdatesOnly.Click       += new System.EventHandler(this.tsbShowUpdatesOnly_Click);

            // ── tsbSkyrimDownloads ───────────────────────────────────────────
            this.tsbSkyrimDownloads.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.tsbSkyrimDownloads.Name         = "tsbSkyrimDownloads";
            this.tsbSkyrimDownloads.Visible      = false;
            this.tsbSkyrimDownloads.Click       += new System.EventHandler(this.tsbSkyrimDownloads_Click);

            // ── toolStripLabelModCount ───────────────────────────────────────
            this.toolStripLabelModCount.Name = "toolStripLabelModCount";
            this.toolStripLabelModCount.Text = "Mods: 0";

            // ── gridControl ──────────────────────────────────────────────────
            this.gridControl.Dock     = System.Windows.Forms.DockStyle.Fill;
            this.gridControl.Location = new System.Drawing.Point(0, 0);
            this.gridControl.MainView = this.gridView;
            this.gridControl.Name     = "gridControl";
            this.gridControl.TabIndex = 1;
            this.gridControl.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] { this.gridView });

            // ── gridView ─────────────────────────────────────────────────────
            this.gridView.GridControl     = this.gridControl;
            this.gridView.Name            = "gridView";
            this.gridView.PopupMenuShowing += new DevExpress.XtraGrid.Views.Grid.PopupMenuShowingEventHandler(this.gridView_PopupMenuShowing);

            // ── ModManagerDXControl ──────────────────────────────────────────
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode       = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.gridControl);
            this.Controls.Add(this.toolStrip1);
            this.Name = "ModManagerDXControl";
            this.Size = new System.Drawing.Size(900, 600);

            ((System.ComponentModel.ISupportInitialize)(this.gridControl)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridView)).EndInit();
            this.toolStrip1.ResumeLayout(false);
            this.toolStrip1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.ToolStrip toolStrip1;
        private System.Windows.Forms.ToolStripSplitButton tsbAddMod;
        private System.Windows.Forms.ToolStripMenuItem addModToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem addModFromURLToolStripMenuItem;
        private System.Windows.Forms.ToolStripButton tsbActivate;
        private System.Windows.Forms.ToolStripButton tsbDeactivate;
        private System.Windows.Forms.ToolStripButton tsb_SaveModLoadOrder;
        private System.Windows.Forms.ToolStripButton tsb_ModUpLoadOrder;
        private System.Windows.Forms.ToolStripButton tsb_ModDownLoadOrder;
        private System.Windows.Forms.ToolStripButton tsbTagMod;
        private System.Windows.Forms.ToolStripSplitButton tsbModOnlineChecks;
        private System.Windows.Forms.ToolStripMenuItem checkForModUpdateWithinTheLastDayToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem withinTheLastDayToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem withinTheLastWeekToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem withinTheLastMonthToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem checkFileDownloadId;
        private System.Windows.Forms.ToolStripMenuItem checkMissingDownloadId;
        private System.Windows.Forms.ToolStripButton tsbToggleEndorse;
        private System.Windows.Forms.ToolStripSplitButton tsbResetCategories;
        private System.Windows.Forms.ToolStripMenuItem addNewCategory;
        private System.Windows.Forms.ToolStripMenuItem collapseAllCategories;
        private System.Windows.Forms.ToolStripMenuItem expandAllCategories;
        private System.Windows.Forms.ToolStripMenuItem resetDefaultCategories;
        private System.Windows.Forms.ToolStripMenuItem resetUnassignedToDefaultCategories;
        private System.Windows.Forms.ToolStripMenuItem resetModsCategory;
        private System.Windows.Forms.ToolStripMenuItem removeAllCategories;
        private System.Windows.Forms.ToolStripMenuItem toggleHiddenCategories;
        private System.Windows.Forms.ToolStripButton tsbSwitchView;
        private System.Windows.Forms.ToolStripDropDownButton tsbExportModList;
        private System.Windows.Forms.ToolStripMenuItem exportToTextFile;
        private System.Windows.Forms.ToolStripMenuItem exportToClipboard;
        private System.Windows.Forms.ToolStripButton tsbShowUpdatesOnly;
        private System.Windows.Forms.ToolStripButton tsbSkyrimDownloads;
        private System.Windows.Forms.ToolStripLabel toolStripLabelModCount;
        private DevExpress.XtraGrid.GridControl gridControl;
        private DevExpress.XtraGrid.Views.Grid.GridView gridView;
    }
}
