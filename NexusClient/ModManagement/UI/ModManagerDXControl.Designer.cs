namespace Nexus.Client.ModManagement.UI
{
    partial class ModManagerDXControl
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposePerformanceResources();
                DisposeToolbarCommandBindings();
                if (components != null)
                    components.Dispose();
            }

            base.Dispose(disposing);
        }

        #region Component Designer generated code

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.barManagerMods = new DevExpress.XtraBars.BarManager(this.components);
            this.barModActions = new DevExpress.XtraBars.Bar();
            this.barDockControlTop = new DevExpress.XtraBars.BarDockControl();
            this.barDockControlBottom = new DevExpress.XtraBars.BarDockControl();
            this.barDockControlLeft = new DevExpress.XtraBars.BarDockControl();
            this.barDockControlRight = new DevExpress.XtraBars.BarDockControl();
            this.popupAddMod = new DevExpress.XtraBars.PopupMenu(this.components);
            this.popupDeactivate = new DevExpress.XtraBars.PopupMenu(this.components);
            this.popupOnlineChecks = new DevExpress.XtraBars.PopupMenu(this.components);
            this.popupCategories = new DevExpress.XtraBars.PopupMenu(this.components);
            this.popupExport = new DevExpress.XtraBars.PopupMenu(this.components);
            this.tsbAddMod = new DevExpress.XtraBars.BarButtonItem();
            this.addModToolStripMenuItem = new DevExpress.XtraBars.BarButtonItem();
            this.addModFromURLToolStripMenuItem = new DevExpress.XtraBars.BarButtonItem();
            this.tsbActivate = new DevExpress.XtraBars.BarButtonItem();
            this.tsbDeactivate = new DevExpress.XtraBars.BarButtonItem();
            this.tsb_SaveModLoadOrder = new DevExpress.XtraBars.BarButtonItem();
            this.tsb_ModUpLoadOrder = new DevExpress.XtraBars.BarButtonItem();
            this.tsb_ModDownLoadOrder = new DevExpress.XtraBars.BarButtonItem();
            this.tsbTagMod = new DevExpress.XtraBars.BarButtonItem();
            this.tsbModOnlineChecks = new DevExpress.XtraBars.BarButtonItem();
            this.checkForModUpdateWithinTheLastDayToolStripMenuItem = new DevExpress.XtraBars.BarSubItem();
            this.withinTheLastDayToolStripMenuItem = new DevExpress.XtraBars.BarButtonItem();
            this.withinTheLastWeekToolStripMenuItem = new DevExpress.XtraBars.BarButtonItem();
            this.withinTheLastMonthToolStripMenuItem = new DevExpress.XtraBars.BarButtonItem();
            this.checkFileDownloadId = new DevExpress.XtraBars.BarButtonItem();
            this.checkMissingDownloadId = new DevExpress.XtraBars.BarButtonItem();
            this.tsbToggleEndorse = new DevExpress.XtraBars.BarButtonItem();
            this.tsbResetCategories = new DevExpress.XtraBars.BarButtonItem();
            this.addNewCategory = new DevExpress.XtraBars.BarButtonItem();
            this.collapseAllCategories = new DevExpress.XtraBars.BarButtonItem();
            this.expandAllCategories = new DevExpress.XtraBars.BarButtonItem();
            this.updateNexusAndCustomCategories = new DevExpress.XtraBars.BarButtonItem();
            this.resetDefaultCategories = new DevExpress.XtraBars.BarButtonItem();
            this.resetUnassignedToDefaultCategories = new DevExpress.XtraBars.BarButtonItem();
            this.resetModsCategory = new DevExpress.XtraBars.BarButtonItem();
            this.removeAllCategories = new DevExpress.XtraBars.BarButtonItem();
            this.toggleHiddenCategories = new DevExpress.XtraBars.BarButtonItem();
            this.tsbSwitchView = new DevExpress.XtraBars.BarButtonItem();
            this.tsbExportModList = new DevExpress.XtraBars.BarButtonItem();
            this.exportToTextFile = new DevExpress.XtraBars.BarButtonItem();
            this.exportToClipboard = new DevExpress.XtraBars.BarButtonItem();
            this.tsbShowUpdatesOnly = new DevExpress.XtraBars.BarButtonItem();
            this.tsbSkyrimDownloads = new DevExpress.XtraBars.BarButtonItem();
            this.toolStripLabelModCount = new DevExpress.XtraBars.BarStaticItem();
            this.viewHost = new System.Windows.Forms.Panel();
            ((System.ComponentModel.ISupportInitialize)(this.barManagerMods)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.popupAddMod)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.popupDeactivate)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.popupOnlineChecks)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.popupCategories)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.popupExport)).BeginInit();
            this.SuspendLayout();
            //
            // barManagerMods
            //
            this.barManagerMods.Bars.AddRange(new DevExpress.XtraBars.Bar[] { this.barModActions });
            this.barManagerMods.DockControls.Add(this.barDockControlTop);
            this.barManagerMods.DockControls.Add(this.barDockControlBottom);
            this.barManagerMods.DockControls.Add(this.barDockControlLeft);
            this.barManagerMods.DockControls.Add(this.barDockControlRight);
            this.barManagerMods.Form = this;
            this.barManagerMods.Items.AddRange(new DevExpress.XtraBars.BarItem[] {
                this.tsbAddMod,
                this.addModToolStripMenuItem,
                this.addModFromURLToolStripMenuItem,
                this.tsbActivate,
                this.tsbDeactivate,
                this.tsb_SaveModLoadOrder,
                this.tsb_ModUpLoadOrder,
                this.tsb_ModDownLoadOrder,
                this.tsbTagMod,
                this.tsbModOnlineChecks,
                this.checkForModUpdateWithinTheLastDayToolStripMenuItem,
                this.withinTheLastDayToolStripMenuItem,
                this.withinTheLastWeekToolStripMenuItem,
                this.withinTheLastMonthToolStripMenuItem,
                this.checkFileDownloadId,
                this.checkMissingDownloadId,
                this.tsbToggleEndorse,
                this.tsbResetCategories,
                this.addNewCategory,
                this.collapseAllCategories,
                this.expandAllCategories,
                this.updateNexusAndCustomCategories,
                this.resetDefaultCategories,
                this.resetUnassignedToDefaultCategories,
                this.resetModsCategory,
                this.removeAllCategories,
                this.toggleHiddenCategories,
                this.tsbSwitchView,
                this.tsbExportModList,
                this.exportToTextFile,
                this.exportToClipboard,
                this.tsbShowUpdatesOnly,
                this.tsbSkyrimDownloads,
                this.toolStripLabelModCount
            });
            this.barManagerMods.MaxItemId = 34;
            //
            // barModActions
            //
            this.barModActions.BarName = "Mod Actions";
            this.barModActions.DockCol = 0;
            this.barModActions.DockRow = 0;
            this.barModActions.DockStyle = DevExpress.XtraBars.BarDockStyle.Top;
            this.barModActions.OptionsBar.AllowQuickCustomization = false;
            this.barModActions.OptionsBar.DisableClose = true;
            this.barModActions.OptionsBar.DisableCustomization = true;
            this.barModActions.OptionsBar.DrawDragBorder = false;
            this.barModActions.OptionsBar.RotateWhenVertical = false;
            this.barModActions.OptionsBar.UseWholeRow = true;
            this.barModActions.Text = "Mod Actions";
            //
            // popupAddMod
            //
            this.popupAddMod.LinksPersistInfo.AddRange(new DevExpress.XtraBars.LinkPersistInfo[] {
                new DevExpress.XtraBars.LinkPersistInfo(this.addModToolStripMenuItem),
                new DevExpress.XtraBars.LinkPersistInfo(this.addModFromURLToolStripMenuItem)
            });
            this.popupAddMod.Manager = this.barManagerMods;
            //
            // popupDeactivate
            //
            this.popupDeactivate.Manager = this.barManagerMods;
            //
            // popupOnlineChecks
            //
            this.popupOnlineChecks.LinksPersistInfo.AddRange(new DevExpress.XtraBars.LinkPersistInfo[] {
                new DevExpress.XtraBars.LinkPersistInfo(this.checkForModUpdateWithinTheLastDayToolStripMenuItem),
                new DevExpress.XtraBars.LinkPersistInfo(this.checkFileDownloadId),
                new DevExpress.XtraBars.LinkPersistInfo(this.checkMissingDownloadId)
            });
            this.popupOnlineChecks.Manager = this.barManagerMods;
            //
            // popupCategories
            //
            this.popupCategories.Manager = this.barManagerMods;
            //
            // popupExport
            //
            this.popupExport.LinksPersistInfo.AddRange(new DevExpress.XtraBars.LinkPersistInfo[] {
                new DevExpress.XtraBars.LinkPersistInfo(this.exportToTextFile),
                new DevExpress.XtraBars.LinkPersistInfo(this.exportToClipboard)
            });
            this.popupExport.Manager = this.barManagerMods;
            //
            // tsbAddMod
            //
            this.tsbAddMod.ActAsDropDown = false;
            this.tsbAddMod.ButtonStyle = DevExpress.XtraBars.BarButtonStyle.DropDown;
            this.tsbAddMod.Caption = "Add Mod";
            this.tsbAddMod.DropDownControl = this.popupAddMod;
            this.tsbAddMod.Hint = "Add a mod from a file";
            this.tsbAddMod.Id = 0;
            this.tsbAddMod.Name = "tsbAddMod";
            this.tsbAddMod.PaintStyle = DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph;
            this.tsbAddMod.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.tsbAddMod_ButtonClick);
            //
            // addModToolStripMenuItem
            //
            this.addModToolStripMenuItem.Caption = "Add Mod from File";
            this.addModToolStripMenuItem.Id = 1;
            this.addModToolStripMenuItem.Name = "addModToolStripMenuItem";
            this.addModToolStripMenuItem.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.addModToolStripMenuItem_Click);
            //
            // addModFromURLToolStripMenuItem
            //
            this.addModFromURLToolStripMenuItem.Caption = "Add Mod from URL";
            this.addModFromURLToolStripMenuItem.Id = 2;
            this.addModFromURLToolStripMenuItem.Name = "addModFromURLToolStripMenuItem";
            this.addModFromURLToolStripMenuItem.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.addModFromURLToolStripMenuItem_Click);
            //
            // tsbActivate
            //
            this.tsbActivate.Caption = "Install / Enable";
            this.tsbActivate.Hint = "Install / enable the selected mod(s)";
            this.tsbActivate.Id = 3;
            this.tsbActivate.Name = "tsbActivate";
            this.tsbActivate.PaintStyle = DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph;
            //
            // tsbDeactivate
            //
            this.tsbDeactivate.ActAsDropDown = false;
            this.tsbDeactivate.ButtonStyle = DevExpress.XtraBars.BarButtonStyle.DropDown;
            this.tsbDeactivate.Caption = "Disable Mod";
            this.tsbDeactivate.DropDownControl = this.popupDeactivate;
            this.tsbDeactivate.Hint = "Disable the selected mod(s)";
            this.tsbDeactivate.Id = 4;
            this.tsbDeactivate.Name = "tsbDeactivate";
            this.tsbDeactivate.PaintStyle = DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph;
            this.tsbDeactivate.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.tsbDeactivate_ButtonClick);
            //
            // load-order items
            //
            this.tsb_SaveModLoadOrder.Caption = "Save mod load order";
            this.tsb_SaveModLoadOrder.Hint = "Save the current mod load order";
            this.tsb_SaveModLoadOrder.Id = 5;
            this.tsb_SaveModLoadOrder.Name = "tsb_SaveModLoadOrder";
            this.tsb_SaveModLoadOrder.PaintStyle = DevExpress.XtraBars.BarItemPaintStyle.Standard;
            this.tsb_SaveModLoadOrder.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.tsb_SaveModLoadOrder_Click);
            this.tsb_ModUpLoadOrder.Caption = "Move mod up";
            this.tsb_ModUpLoadOrder.Hint = "Moves mod up in the load order";
            this.tsb_ModUpLoadOrder.Id = 6;
            this.tsb_ModUpLoadOrder.Name = "tsb_ModUpLoadOrder";
            this.tsb_ModUpLoadOrder.PaintStyle = DevExpress.XtraBars.BarItemPaintStyle.Standard;
            this.tsb_ModUpLoadOrder.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.tsb_ModUpLoadOrder_Click);
            this.tsb_ModDownLoadOrder.Caption = "Move mod down";
            this.tsb_ModDownLoadOrder.Hint = "Moves mod down in the load order";
            this.tsb_ModDownLoadOrder.Id = 7;
            this.tsb_ModDownLoadOrder.Name = "tsb_ModDownLoadOrder";
            this.tsb_ModDownLoadOrder.PaintStyle = DevExpress.XtraBars.BarItemPaintStyle.Standard;
            this.tsb_ModDownLoadOrder.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.tsb_ModDownLoadOrder_Click);
            //
            // tsbTagMod
            //
            this.tsbTagMod.Caption = "Get Mod Info";
            this.tsbTagMod.Hint = "Get missing mod info";
            this.tsbTagMod.Id = 8;
            this.tsbTagMod.Name = "tsbTagMod";
            this.tsbTagMod.PaintStyle = DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph;
            //
            // tsbModOnlineChecks
            //
            this.tsbModOnlineChecks.ActAsDropDown = false;
            this.tsbModOnlineChecks.ButtonStyle = DevExpress.XtraBars.BarButtonStyle.DropDown;
            this.tsbModOnlineChecks.Caption = "Updates";
            this.tsbModOnlineChecks.DropDownControl = this.popupOnlineChecks;
            this.tsbModOnlineChecks.Hint = "Check for mod updates";
            this.tsbModOnlineChecks.Id = 9;
            this.tsbModOnlineChecks.Name = "tsbModOnlineChecks";
            this.tsbModOnlineChecks.PaintStyle = DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph;
            this.tsbModOnlineChecks.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.tsbModOnlineChecks_ButtonClick);
            //
            // update interval submenu
            //
            this.checkForModUpdateWithinTheLastDayToolStripMenuItem.Caption = "Check for Mod Updates Interval ...";
            this.checkForModUpdateWithinTheLastDayToolStripMenuItem.Id = 10;
            this.checkForModUpdateWithinTheLastDayToolStripMenuItem.Name = "checkForModUpdateWithinTheLastDayToolStripMenuItem";
            this.checkForModUpdateWithinTheLastDayToolStripMenuItem.LinksPersistInfo.AddRange(new DevExpress.XtraBars.LinkPersistInfo[] {
                new DevExpress.XtraBars.LinkPersistInfo(this.withinTheLastDayToolStripMenuItem),
                new DevExpress.XtraBars.LinkPersistInfo(this.withinTheLastWeekToolStripMenuItem),
                new DevExpress.XtraBars.LinkPersistInfo(this.withinTheLastMonthToolStripMenuItem)
            });
            this.withinTheLastDayToolStripMenuItem.Caption = "...within the last day";
            this.withinTheLastDayToolStripMenuItem.Id = 11;
            this.withinTheLastDayToolStripMenuItem.Name = "withinTheLastDayToolStripMenuItem";
            this.withinTheLastDayToolStripMenuItem.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.withinTheLastDayToolStripMenuItem_Click);
            this.withinTheLastWeekToolStripMenuItem.Caption = "...within the last week";
            this.withinTheLastWeekToolStripMenuItem.Id = 12;
            this.withinTheLastWeekToolStripMenuItem.Name = "withinTheLastWeekToolStripMenuItem";
            this.withinTheLastWeekToolStripMenuItem.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.withinTheLastWeekToolStripMenuItem_Click);
            this.withinTheLastMonthToolStripMenuItem.Caption = "...within the last month";
            this.withinTheLastMonthToolStripMenuItem.Id = 13;
            this.withinTheLastMonthToolStripMenuItem.Name = "withinTheLastMonthToolStripMenuItem";
            this.withinTheLastMonthToolStripMenuItem.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.withinTheLastMonthToolStripMenuItem_Click);
            this.checkFileDownloadId.Caption = "Fix download IDs and Check for mod updates";
            this.checkFileDownloadId.Id = 14;
            this.checkFileDownloadId.Name = "checkFileDownloadId";
            this.checkFileDownloadId.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.checkFileDownloadId_Click);
            this.checkMissingDownloadId.Caption = "Just check for missing download IDs";
            this.checkMissingDownloadId.Id = 15;
            this.checkMissingDownloadId.Name = "checkMissingDownloadId";
            this.checkMissingDownloadId.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.checkMissingDownloadId_Click);
            //
            // tsbToggleEndorse
            //
            this.tsbToggleEndorse.Caption = "Endorse";
            this.tsbToggleEndorse.Hint = "Toggle mod endorsement";
            this.tsbToggleEndorse.Id = 16;
            this.tsbToggleEndorse.Name = "tsbToggleEndorse";
            this.tsbToggleEndorse.PaintStyle = DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph;
            this.tsbToggleEndorse.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.tsbToggleEndorse_Click);
            //
            // tsbResetCategories and category popup
            //
            this.tsbResetCategories.ActAsDropDown = false;
            this.tsbResetCategories.ButtonStyle = DevExpress.XtraBars.BarButtonStyle.DropDown;
            this.tsbResetCategories.Caption = "Categories";
            this.tsbResetCategories.DropDownControl = this.popupCategories;
            this.tsbResetCategories.Hint = "Categories: add new category - Click the small arrow for more options";
            this.tsbResetCategories.Id = 17;
            this.tsbResetCategories.Name = "tsbResetCategories";
            this.tsbResetCategories.PaintStyle = DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph;
            this.tsbResetCategories.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.addNewCategory_Click);
            this.addNewCategory.Caption = "Categories: add new category";
            this.addNewCategory.Id = 18;
            this.addNewCategory.Name = "addNewCategory";
            this.addNewCategory.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.addNewCategory_Click);
            this.collapseAllCategories.Caption = "Categories: collapse all categories";
            this.collapseAllCategories.Id = 19;
            this.collapseAllCategories.Name = "collapseAllCategories";
            this.collapseAllCategories.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.collapseAllCategories_Click);
            this.expandAllCategories.Caption = "Categories: expand all categories";
            this.expandAllCategories.Id = 20;
            this.expandAllCategories.Name = "expandAllCategories";
            this.expandAllCategories.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.expandAllCategories_Click);
            this.updateNexusAndCustomCategories.Caption = "Categories: Update Nexus and custom categories";
            this.updateNexusAndCustomCategories.Id = 21;
            this.updateNexusAndCustomCategories.Name = "updateNexusAndCustomCategories";
            this.updateNexusAndCustomCategories.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.updateNexusAndCustomCategories_Click);
            this.resetDefaultCategories.Caption = "Categories: Update and reset to Nexus site defaults";
            this.resetDefaultCategories.Id = 22;
            this.resetDefaultCategories.Name = "resetDefaultCategories";
            this.resetDefaultCategories.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.resetDefaultCategories_Click);
            this.resetUnassignedToDefaultCategories.Caption = "Categories: reset Unassigned mods to Nexus site defaults";
            this.resetUnassignedToDefaultCategories.Id = 23;
            this.resetUnassignedToDefaultCategories.Name = "resetUnassignedToDefaultCategories";
            this.resetUnassignedToDefaultCategories.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.resetUnassignedToDefaultCategories_Click);
            this.resetModsCategory.Caption = "Categories: reset all mods to unassigned";
            this.resetModsCategory.Id = 24;
            this.resetModsCategory.Name = "resetModsCategory";
            this.resetModsCategory.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.resetModsCategory_Click);
            this.removeAllCategories.Caption = "Categories: remove all categories";
            this.removeAllCategories.Id = 25;
            this.removeAllCategories.Name = "removeAllCategories";
            this.removeAllCategories.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.removeAllCategories_Click);
            this.toggleHiddenCategories.Caption = "Categories: show empty categories";
            this.toggleHiddenCategories.Id = 26;
            this.toggleHiddenCategories.Name = "toggleHiddenCategories";
            this.toggleHiddenCategories.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.toggleHiddenCategories_Click);
            //
            // tsbSwitchView
            //
            this.tsbSwitchView.Caption = "Switch View";
            this.tsbSwitchView.Hint = "Switches the Mod Manager views";
            this.tsbSwitchView.Id = 27;
            this.tsbSwitchView.Name = "tsbSwitchView";
            this.tsbSwitchView.PaintStyle = DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph;
            this.tsbSwitchView.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.tsbSwitchView_Click);
            //
            // tsbExportModList
            //
            this.tsbExportModList.ActAsDropDown = true;
            this.tsbExportModList.ButtonStyle = DevExpress.XtraBars.BarButtonStyle.DropDown;
            this.tsbExportModList.Caption = "Export";
            this.tsbExportModList.DropDownControl = this.popupExport;
            this.tsbExportModList.Hint = "Export the current mod list";
            this.tsbExportModList.Id = 28;
            this.tsbExportModList.Name = "tsbExportModList";
            this.tsbExportModList.PaintStyle = DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph;
            this.exportToTextFile.Caption = "Text file";
            this.exportToTextFile.Id = 29;
            this.exportToTextFile.Name = "exportToTextFile";
            this.exportToClipboard.Caption = "Copy to clipboard";
            this.exportToClipboard.Id = 30;
            this.exportToClipboard.Name = "exportToClipboard";
            //
            // tsbShowUpdatesOnly
            //
            this.tsbShowUpdatesOnly.ButtonStyle = DevExpress.XtraBars.BarButtonStyle.Check;
            this.tsbShowUpdatesOnly.Caption = "Updates Only";
            this.tsbShowUpdatesOnly.Hint = "Toggles filtering the mod list showing only mods requiring an update";
            this.tsbShowUpdatesOnly.Id = 31;
            this.tsbShowUpdatesOnly.Name = "tsbShowUpdatesOnly";
            this.tsbShowUpdatesOnly.PaintStyle = DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph;
            this.tsbShowUpdatesOnly.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.tsbShowUpdatesOnly_Click);
            //
            // tsbSkyrimDownloads
            //
            this.tsbSkyrimDownloads.Caption = "Download Mode";
            this.tsbSkyrimDownloads.Id = 32;
            this.tsbSkyrimDownloads.Name = "tsbSkyrimDownloads";
            this.tsbSkyrimDownloads.PaintStyle = DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph;
            this.tsbSkyrimDownloads.Visibility = DevExpress.XtraBars.BarItemVisibility.Never;
            this.tsbSkyrimDownloads.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.tsbSkyrimDownloads_Click);
            //
            // toolStripLabelModCount
            //
            this.toolStripLabelModCount.Caption = "Mods: 0";
            this.toolStripLabelModCount.Id = 33;
            this.toolStripLabelModCount.Name = "toolStripLabelModCount";
            this.toolStripLabelModCount.Alignment = DevExpress.XtraBars.BarItemLinkAlignment.Right;
            //
            // barDockControls
            //
            this.barDockControlTop.CausesValidation = false;
            this.barDockControlTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.barDockControlTop.Manager = this.barManagerMods;
            this.barDockControlBottom.CausesValidation = false;
            this.barDockControlBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.barDockControlBottom.Manager = this.barManagerMods;
            this.barDockControlLeft.CausesValidation = false;
            this.barDockControlLeft.Dock = System.Windows.Forms.DockStyle.Left;
            this.barDockControlLeft.Manager = this.barManagerMods;
            this.barDockControlRight.CausesValidation = false;
            this.barDockControlRight.Dock = System.Windows.Forms.DockStyle.Right;
            this.barDockControlRight.Manager = this.barManagerMods;
            //
            // viewHost
            //
            this.viewHost.Dock = System.Windows.Forms.DockStyle.Fill;
            this.viewHost.Location = new System.Drawing.Point(0, 0);
            this.viewHost.Name = "viewHost";
            this.viewHost.Size = new System.Drawing.Size(900, 600);
            this.viewHost.TabIndex = 1;
            //
            // ModManagerDXControl
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.viewHost);
            this.Controls.Add(this.barDockControlLeft);
            this.Controls.Add(this.barDockControlRight);
            this.Controls.Add(this.barDockControlBottom);
            this.Controls.Add(this.barDockControlTop);
            this.Name = "ModManagerDXControl";
            this.Size = new System.Drawing.Size(900, 600);
            ((System.ComponentModel.ISupportInitialize)(this.barManagerMods)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.popupAddMod)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.popupDeactivate)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.popupOnlineChecks)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.popupCategories)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.popupExport)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private DevExpress.XtraBars.BarManager barManagerMods;
        private DevExpress.XtraBars.Bar barModActions;
        private DevExpress.XtraBars.BarDockControl barDockControlTop;
        private DevExpress.XtraBars.BarDockControl barDockControlBottom;
        private DevExpress.XtraBars.BarDockControl barDockControlLeft;
        private DevExpress.XtraBars.BarDockControl barDockControlRight;
        private DevExpress.XtraBars.PopupMenu popupAddMod;
        private DevExpress.XtraBars.PopupMenu popupDeactivate;
        private DevExpress.XtraBars.PopupMenu popupOnlineChecks;
        private DevExpress.XtraBars.PopupMenu popupCategories;
        private DevExpress.XtraBars.PopupMenu popupExport;
        private DevExpress.XtraBars.BarButtonItem tsbAddMod;
        private DevExpress.XtraBars.BarButtonItem addModToolStripMenuItem;
        private DevExpress.XtraBars.BarButtonItem addModFromURLToolStripMenuItem;
        private DevExpress.XtraBars.BarButtonItem tsbActivate;
        private DevExpress.XtraBars.BarButtonItem tsbDeactivate;
        private DevExpress.XtraBars.BarButtonItem tsb_SaveModLoadOrder;
        private DevExpress.XtraBars.BarButtonItem tsb_ModUpLoadOrder;
        private DevExpress.XtraBars.BarButtonItem tsb_ModDownLoadOrder;
        private DevExpress.XtraBars.BarButtonItem tsbTagMod;
        private DevExpress.XtraBars.BarButtonItem tsbModOnlineChecks;
        private DevExpress.XtraBars.BarSubItem checkForModUpdateWithinTheLastDayToolStripMenuItem;
        private DevExpress.XtraBars.BarButtonItem withinTheLastDayToolStripMenuItem;
        private DevExpress.XtraBars.BarButtonItem withinTheLastWeekToolStripMenuItem;
        private DevExpress.XtraBars.BarButtonItem withinTheLastMonthToolStripMenuItem;
        private DevExpress.XtraBars.BarButtonItem checkFileDownloadId;
        private DevExpress.XtraBars.BarButtonItem checkMissingDownloadId;
        private DevExpress.XtraBars.BarButtonItem tsbToggleEndorse;
        private DevExpress.XtraBars.BarButtonItem tsbResetCategories;
        private DevExpress.XtraBars.BarButtonItem addNewCategory;
        private DevExpress.XtraBars.BarButtonItem collapseAllCategories;
        private DevExpress.XtraBars.BarButtonItem expandAllCategories;
        private DevExpress.XtraBars.BarButtonItem updateNexusAndCustomCategories;
        private DevExpress.XtraBars.BarButtonItem resetDefaultCategories;
        private DevExpress.XtraBars.BarButtonItem resetUnassignedToDefaultCategories;
        private DevExpress.XtraBars.BarButtonItem resetModsCategory;
        private DevExpress.XtraBars.BarButtonItem removeAllCategories;
        private DevExpress.XtraBars.BarButtonItem toggleHiddenCategories;
        private DevExpress.XtraBars.BarButtonItem tsbSwitchView;
        private DevExpress.XtraBars.BarButtonItem tsbExportModList;
        private DevExpress.XtraBars.BarButtonItem exportToTextFile;
        private DevExpress.XtraBars.BarButtonItem exportToClipboard;
        private DevExpress.XtraBars.BarButtonItem tsbShowUpdatesOnly;
        private DevExpress.XtraBars.BarButtonItem tsbSkyrimDownloads;
        private DevExpress.XtraBars.BarStaticItem toolStripLabelModCount;
        private System.Windows.Forms.Panel viewHost;
    }
}
