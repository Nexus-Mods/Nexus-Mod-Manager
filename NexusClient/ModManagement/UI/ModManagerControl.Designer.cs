namespace Nexus.Client.ModManagement.UI
{
	partial class ModManagerControl
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
			DoDispose();
			base.Dispose(disposing);
		}

		/// <summary>
		/// Allows extension of the dispose method.
		/// </summary>
		partial void DoDispose();

		#region Component Designer generated code

		/// <summary> 
		/// Required method for Designer support - do not modify 
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.components = new System.ComponentModel.Container();
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ModManagerControl));
			this.toolStrip1 = new System.Windows.Forms.ToolStrip();
			this.tsbAddMod = new System.Windows.Forms.ToolStripSplitButton();
			this.addModToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.addModFromURLToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.tsbActivate = new System.Windows.Forms.ToolStripButton();
			this.tsbDeactivate = new System.Windows.Forms.ToolStripButton();
			this.tsb_SaveModLoadOrder = new System.Windows.Forms.ToolStripButton();
			this.tsb_ModUpLoadOrder = new System.Windows.Forms.ToolStripButton();
			this.tsb_ModDownLoadOrder = new System.Windows.Forms.ToolStripButton();
			this.tsbTagMod = new System.Windows.Forms.ToolStripButton();
			this.tsbModOnlineChecks = new System.Windows.Forms.ToolStripSplitButton();
			this.checkForModUpdateWithinTheLastDayToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.withinTheLastDayToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.withinTheLastWeekToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.withinTheLastMonthToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.checkFileDownloadId = new System.Windows.Forms.ToolStripMenuItem();
			this.checkMissingDownloadId = new System.Windows.Forms.ToolStripMenuItem();
			this.tsbToggleEndorse = new System.Windows.Forms.ToolStripButton();
			this.tsbResetCategories = new System.Windows.Forms.ToolStripSplitButton();
			this.addNewCategory = new System.Windows.Forms.ToolStripMenuItem();
			this.collapseAllCategories = new System.Windows.Forms.ToolStripMenuItem();
			this.expandAllCategories = new System.Windows.Forms.ToolStripMenuItem();
			this.resetDefaultCategories = new System.Windows.Forms.ToolStripMenuItem();
			this.resetUnassignedToDefaultCategories = new System.Windows.Forms.ToolStripMenuItem();
			this.resetModsCategory = new System.Windows.Forms.ToolStripMenuItem();
			this.removeAllCategories = new System.Windows.Forms.ToolStripMenuItem();
			this.toggleHiddenCategories = new System.Windows.Forms.ToolStripMenuItem();
			this.tsbSwitchView = new System.Windows.Forms.ToolStripButton();
			this.tsbExportModList = new System.Windows.Forms.ToolStripDropDownButton();
			this.exportToTextFile = new System.Windows.Forms.ToolStripMenuItem();
			this.exportToClipboard = new System.Windows.Forms.ToolStripMenuItem();
			this.checkModUpdates = new System.Windows.Forms.ToolStripMenuItem();
			this.sptMods = new System.Windows.Forms.SplitContainer();
			this.clwCategoryView = new Nexus.Client.UI.Controls.CategoryListView();
			this.sptSummaryInfo = new System.Windows.Forms.SplitContainer();
			this.ipbScreenShot = new Nexus.UI.Controls.ImagePreviewBox();
			this.flbInfo = new Nexus.UI.Controls.FormattedLabel();
			this.ofdChooseMod = new System.Windows.Forms.OpenFileDialog();
			this.sfdChooseExport = new System.Windows.Forms.SaveFileDialog();
			this.toolStrip1.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.sptMods)).BeginInit();
			this.sptMods.Panel1.SuspendLayout();
			this.sptMods.Panel2.SuspendLayout();
			this.sptMods.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.clwCategoryView)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.sptSummaryInfo)).BeginInit();
			this.sptSummaryInfo.Panel1.SuspendLayout();
			this.sptSummaryInfo.Panel2.SuspendLayout();
			this.sptSummaryInfo.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.ipbScreenShot)).BeginInit();
			this.SuspendLayout();
			// 
			// toolStrip1
			// 
			this.toolStrip1.Dock = System.Windows.Forms.DockStyle.Left;
			this.toolStrip1.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
			this.toolStrip1.ImageScalingSize = new System.Drawing.Size(32, 32);
			this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsbAddMod,
            this.tsbActivate,
            this.tsbDeactivate,
            this.tsb_SaveModLoadOrder,
            this.tsb_ModUpLoadOrder,
            this.tsb_ModDownLoadOrder,
            this.tsbTagMod,
            this.tsbModOnlineChecks,
            this.tsbToggleEndorse,
            this.tsbResetCategories,
            this.tsbSwitchView,
            this.tsbExportModList});
			this.toolStrip1.Location = new System.Drawing.Point(0, 0);
			this.toolStrip1.Name = "toolStrip1";
			this.toolStrip1.Size = new System.Drawing.Size(49, 642);
			this.toolStrip1.TabIndex = 0;
			this.toolStrip1.Text = "toolStrip1";
			// 
			// tsbAddMod
			// 
			this.tsbAddMod.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbAddMod.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.addModToolStripMenuItem,
            this.addModFromURLToolStripMenuItem});
			this.tsbAddMod.Image = ((System.Drawing.Image)(resources.GetObject("tsbAddMod.Image")));
			this.tsbAddMod.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbAddMod.Name = "tsbAddMod";
			this.tsbAddMod.Size = new System.Drawing.Size(46, 36);
			this.tsbAddMod.Text = "Add Mod";
			// 
			// addModToolStripMenuItem
			// 
			this.addModToolStripMenuItem.Image = global::Nexus.Client.Properties.Resources.add_mod_file_flat;
			this.addModToolStripMenuItem.Name = "addModToolStripMenuItem";
			this.addModToolStripMenuItem.Size = new System.Drawing.Size(196, 38);
			this.addModToolStripMenuItem.Text = "Add Mod from File";
			this.addModToolStripMenuItem.Click += new System.EventHandler(this.addModToolStripMenuItem_Click);
			// 
			// addModFromURLToolStripMenuItem
			// 
			this.addModFromURLToolStripMenuItem.Image = global::Nexus.Client.Properties.Resources.add_mod_url_flat;
			this.addModFromURLToolStripMenuItem.Name = "addModFromURLToolStripMenuItem";
			this.addModFromURLToolStripMenuItem.Size = new System.Drawing.Size(196, 38);
			this.addModFromURLToolStripMenuItem.Text = "Add Mod from URL";
			this.addModFromURLToolStripMenuItem.Click += new System.EventHandler(this.addModFromURLToolStripMenuItem_Click);
			// 
			// tsbActivate
			// 
			this.tsbActivate.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbActivate.Image = global::Nexus.Client.Properties.Resources.obsidianshade_checkmark;
			this.tsbActivate.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbActivate.Name = "tsbActivate";
			this.tsbActivate.Size = new System.Drawing.Size(46, 36);
			this.tsbActivate.Text = "toolStripButton1";
			this.tsbActivate.Click += new System.EventHandler(this.tsbActivate_Click);
			// 
			// tsbDeactivate
			// 
			this.tsbDeactivate.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbDeactivate.Image = global::Nexus.Client.Properties.Resources.remove_download_flat;
			this.tsbDeactivate.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbDeactivate.Name = "tsbDeactivate";
			this.tsbDeactivate.Size = new System.Drawing.Size(46, 36);
			this.tsbDeactivate.Text = "toolStripButton1";
			// 
			// tsb_SaveModLoadOrder
			// 
			this.tsb_SaveModLoadOrder.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsb_SaveModLoadOrder.Image = global::Nexus.Client.Properties.Resources.save_mod_loadorder_flat;
			this.tsb_SaveModLoadOrder.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsb_SaveModLoadOrder.Name = "tsb_SaveModLoadOrder";
			this.tsb_SaveModLoadOrder.Size = new System.Drawing.Size(46, 36);
			this.tsb_SaveModLoadOrder.Text = "Save mod load order";
			this.tsb_SaveModLoadOrder.Click += new System.EventHandler(this.tsb_SaveModLoadOrder_Click);
			// 
			// tsb_ModUpLoadOrder
			// 
			this.tsb_ModUpLoadOrder.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsb_ModUpLoadOrder.Image = global::Nexus.Client.Properties.Resources.move_up_flat;
			this.tsb_ModUpLoadOrder.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsb_ModUpLoadOrder.Name = "tsb_ModUpLoadOrder";
			this.tsb_ModUpLoadOrder.Size = new System.Drawing.Size(46, 36);
			this.tsb_ModUpLoadOrder.Text = "toolStripButton1";
			this.tsb_ModUpLoadOrder.ToolTipText = "Moves mod up in the load order";
			this.tsb_ModUpLoadOrder.Click += new System.EventHandler(this.tsb_ModUpLoadOrder_Click);
			// 
			// tsb_ModDownLoadOrder
			// 
			this.tsb_ModDownLoadOrder.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsb_ModDownLoadOrder.Image = global::Nexus.Client.Properties.Resources.move_down_flat;
			this.tsb_ModDownLoadOrder.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsb_ModDownLoadOrder.Name = "tsb_ModDownLoadOrder";
			this.tsb_ModDownLoadOrder.Size = new System.Drawing.Size(46, 36);
			this.tsb_ModDownLoadOrder.Text = "toolStripButton2";
			this.tsb_ModDownLoadOrder.ToolTipText = "Moves mod down in the load order";
			this.tsb_ModDownLoadOrder.Click += new System.EventHandler(this.tsb_ModDownLoadOrder_Click);
			// 
			// tsbTagMod
			// 
			this.tsbTagMod.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbTagMod.Image = global::Nexus.Client.Properties.Resources.mad_tagger_flat;
			this.tsbTagMod.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbTagMod.Name = "tsbTagMod";
			this.tsbTagMod.Size = new System.Drawing.Size(46, 36);
			this.tsbTagMod.Text = "toolStripButton1";
			// 
			// tsbModOnlineChecks
			// 
			this.tsbModOnlineChecks.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbModOnlineChecks.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.checkForModUpdateWithinTheLastDayToolStripMenuItem,
            this.checkFileDownloadId,
            this.checkMissingDownloadId});
			this.tsbModOnlineChecks.Image = global::Nexus.Client.Properties.Resources.check_mod_updates_flat;
			this.tsbModOnlineChecks.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbModOnlineChecks.Name = "tsbModOnlineChecks";
			this.tsbModOnlineChecks.Size = new System.Drawing.Size(46, 36);
			this.tsbModOnlineChecks.Text = "Check for mod updates";
			this.tsbModOnlineChecks.ButtonClick += new System.EventHandler(this.tsbModOnlineChecks_ButtonClick);
			// 
			// checkForModUpdateWithinTheLastDayToolStripMenuItem
			// 
			this.checkForModUpdateWithinTheLastDayToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.withinTheLastDayToolStripMenuItem,
            this.withinTheLastWeekToolStripMenuItem,
            this.withinTheLastMonthToolStripMenuItem});
			this.checkForModUpdateWithinTheLastDayToolStripMenuItem.Image = global::Nexus.Client.Properties.Resources.check_updates_interval_flat;
			this.checkForModUpdateWithinTheLastDayToolStripMenuItem.Name = "checkForModUpdateWithinTheLastDayToolStripMenuItem";
			this.checkForModUpdateWithinTheLastDayToolStripMenuItem.Size = new System.Drawing.Size(330, 38);
			this.checkForModUpdateWithinTheLastDayToolStripMenuItem.Text = "Check for Mod Updates Interval ...";
			// 
			// withinTheLastDayToolStripMenuItem
			// 
			this.withinTheLastDayToolStripMenuItem.Name = "withinTheLastDayToolStripMenuItem";
			this.withinTheLastDayToolStripMenuItem.Size = new System.Drawing.Size(196, 22);
			this.withinTheLastDayToolStripMenuItem.Text = "...within the last day";
			this.withinTheLastDayToolStripMenuItem.Click += new System.EventHandler(this.withinTheLastDayToolStripMenuItem_Click);
			// 
			// withinTheLastWeekToolStripMenuItem
			// 
			this.withinTheLastWeekToolStripMenuItem.Name = "withinTheLastWeekToolStripMenuItem";
			this.withinTheLastWeekToolStripMenuItem.Size = new System.Drawing.Size(196, 22);
			this.withinTheLastWeekToolStripMenuItem.Text = "...within the last week";
			this.withinTheLastWeekToolStripMenuItem.Click += new System.EventHandler(this.withinTheLastWeekToolStripMenuItem_Click);
			// 
			// withinTheLastMonthToolStripMenuItem
			// 
			this.withinTheLastMonthToolStripMenuItem.Name = "withinTheLastMonthToolStripMenuItem";
			this.withinTheLastMonthToolStripMenuItem.Size = new System.Drawing.Size(196, 22);
			this.withinTheLastMonthToolStripMenuItem.Text = "...within the last month";
			this.withinTheLastMonthToolStripMenuItem.Click += new System.EventHandler(this.withinTheLastMonthToolStripMenuItem_Click);
			// 
			// checkFileDownloadId
			// 
			this.checkFileDownloadId.Image = global::Nexus.Client.Properties.Resources.check_updates_id_fix_flat;
			this.checkFileDownloadId.Name = "checkFileDownloadId";
			this.checkFileDownloadId.Size = new System.Drawing.Size(330, 38);
			this.checkFileDownloadId.Text = "Fix download IDs and Check for mod updates";
			this.checkFileDownloadId.Click += new System.EventHandler(this.checkFileDownloadId_Click);
			// 
			// checkMissingDownloadId
			// 
			this.checkMissingDownloadId.Image = global::Nexus.Client.Properties.Resources.check_updates_id_fix_flat;
			this.checkMissingDownloadId.Name = "checkMissingDownloadId";
			this.checkMissingDownloadId.Size = new System.Drawing.Size(330, 38);
			this.checkMissingDownloadId.Text = "Just check for missing download IDs";
			this.checkMissingDownloadId.Click += new System.EventHandler(this.checkMissingDownloadId_Click);
			// 
			// tsbToggleEndorse
			// 
			this.tsbToggleEndorse.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbToggleEndorse.Image = global::Nexus.Client.Properties.Resources.endorse_flat;
			this.tsbToggleEndorse.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbToggleEndorse.Name = "tsbToggleEndorse";
			this.tsbToggleEndorse.Size = new System.Drawing.Size(46, 36);
			this.tsbToggleEndorse.Text = "toolStripButton1";
			// 
			// tsbResetCategories
			// 
			this.tsbResetCategories.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbResetCategories.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.addNewCategory,
            this.collapseAllCategories,
            this.expandAllCategories,
            this.resetDefaultCategories,
            this.resetUnassignedToDefaultCategories,
            this.resetModsCategory,
            this.removeAllCategories,
            this.toggleHiddenCategories});
			this.tsbResetCategories.Image = global::Nexus.Client.Properties.Resources.categories_flat;
			this.tsbResetCategories.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbResetCategories.Name = "tsbResetCategories";
			this.tsbResetCategories.Size = new System.Drawing.Size(46, 36);
			this.tsbResetCategories.Text = "Categories: add new category - Click the small arrow for more options";
			// 
			// addNewCategory
			// 
			this.addNewCategory.Image = global::Nexus.Client.Properties.Resources.categories_flat;
			this.addNewCategory.Name = "addNewCategory";
			this.addNewCategory.Size = new System.Drawing.Size(390, 38);
			this.addNewCategory.Text = "Categories: add new category";
			this.addNewCategory.Click += new System.EventHandler(this.addNewCategory_Click);
			// 
			// collapseAllCategories
			// 
			this.collapseAllCategories.Image = global::Nexus.Client.Properties.Resources.collapse_all;
			this.collapseAllCategories.Name = "collapseAllCategories";
			this.collapseAllCategories.Size = new System.Drawing.Size(390, 38);
			this.collapseAllCategories.Text = "Categories: collapse all categories";
			this.collapseAllCategories.Click += new System.EventHandler(this.collapseAllCategories_Click);
			// 
			// expandAllCategories
			// 
			this.expandAllCategories.Image = global::Nexus.Client.Properties.Resources.expand_all;
			this.expandAllCategories.Name = "expandAllCategories";
			this.expandAllCategories.Size = new System.Drawing.Size(390, 38);
			this.expandAllCategories.Text = "Categories: expand all categories";
			this.expandAllCategories.Click += new System.EventHandler(this.expandAllCategories_Click);
			// 
			// resetDefaultCategories
			// 
			this.resetDefaultCategories.Image = global::Nexus.Client.Properties.Resources.reset_default;
			this.resetDefaultCategories.Name = "resetDefaultCategories";
			this.resetDefaultCategories.Size = new System.Drawing.Size(390, 38);
			this.resetDefaultCategories.Text = "Categories: Update and reset to Nexus site defaults";
			this.resetDefaultCategories.Click += new System.EventHandler(this.resetDefaultCategories_Click);
			// 
			// resetUnassignedToDefaultCategories
			// 
			this.resetUnassignedToDefaultCategories.Image = global::Nexus.Client.Properties.Resources.reset_default;
			this.resetUnassignedToDefaultCategories.Name = "resetUnassignedToDefaultCategories";
			this.resetUnassignedToDefaultCategories.Size = new System.Drawing.Size(390, 38);
			this.resetUnassignedToDefaultCategories.Text = "Categories: reset Unassigned mods to Nexus site defaults";
			this.resetUnassignedToDefaultCategories.Click += new System.EventHandler(this.resetUnassignedToDefaultCategories_Click);
			// 
			// resetModsCategory
			// 
			this.resetModsCategory.Image = global::Nexus.Client.Properties.Resources.reset_unassigned;
			this.resetModsCategory.Name = "resetModsCategory";
			this.resetModsCategory.Size = new System.Drawing.Size(390, 38);
			this.resetModsCategory.Text = "Categories: reset all mods to unassigned";
			this.resetModsCategory.Click += new System.EventHandler(this.resetModsCategory_Click);
			// 
			// removeAllCategories
			// 
			this.removeAllCategories.Image = global::Nexus.Client.Properties.Resources.remove_all_categories;
			this.removeAllCategories.Name = "removeAllCategories";
			this.removeAllCategories.Size = new System.Drawing.Size(390, 38);
			this.removeAllCategories.Text = "Categories: remove all categories";
			this.removeAllCategories.Click += new System.EventHandler(this.removeAllCategories_Click);
			// 
			// toggleHiddenCategories
			// 
			this.toggleHiddenCategories.Image = global::Nexus.Client.Properties.Resources.reset_categories;
			this.toggleHiddenCategories.Name = "toggleHiddenCategories";
			this.toggleHiddenCategories.Size = new System.Drawing.Size(390, 38);
			this.toggleHiddenCategories.Text = "Categories: toggle hidden categories";
			this.toggleHiddenCategories.Click += new System.EventHandler(this.toggleHiddenCategories_Click);
			// 
			// tsbSwitchView
			// 
			this.tsbSwitchView.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbSwitchView.Image = global::Nexus.Client.Properties.Resources.switch_view_flat;
			this.tsbSwitchView.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbSwitchView.Name = "tsbSwitchView";
			this.tsbSwitchView.Size = new System.Drawing.Size(46, 36);
			this.tsbSwitchView.Text = "Switches the Mod Manager views";
			this.tsbSwitchView.Click += new System.EventHandler(this.tsbSwitchCategory_Click);
			// 
			// tsbExportModList
			// 
			this.tsbExportModList.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbExportModList.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.exportToTextFile,
            this.exportToClipboard});
			this.tsbExportModList.Image = global::Nexus.Client.Properties.Resources.export_mod_list_flat;
			this.tsbExportModList.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbExportModList.Name = "tsbExportModList";
			this.tsbExportModList.Size = new System.Drawing.Size(46, 36);
			this.tsbExportModList.Text = "Export the current mod list";
			// 
			// exportToTextFile
			// 
			this.exportToTextFile.Image = global::Nexus.Client.Properties.Resources.export_text_file_flat;
			this.exportToTextFile.Name = "exportToTextFile";
			this.exportToTextFile.Size = new System.Drawing.Size(196, 38);
			this.exportToTextFile.Text = "Text file";
			// 
			// exportToClipboard
			// 
			this.exportToClipboard.Image = global::Nexus.Client.Properties.Resources.export_clipboard_flat;
			this.exportToClipboard.Name = "exportToClipboard";
			this.exportToClipboard.Size = new System.Drawing.Size(196, 38);
			this.exportToClipboard.Text = "Copy to clipboard";
			// 
			// checkModUpdates
			// 
			this.checkModUpdates.Image = global::Nexus.Client.Properties.Resources.change_game_mode;
			this.checkModUpdates.Name = "checkModUpdates";
			this.checkModUpdates.Size = new System.Drawing.Size(295, 38);
			this.checkModUpdates.Text = "Check for mod updates";
			this.checkModUpdates.Click += new System.EventHandler(this.checkModUpdates_Click);
			// 
			// sptMods
			// 
			this.sptMods.Dock = System.Windows.Forms.DockStyle.Fill;
			this.sptMods.FixedPanel = System.Windows.Forms.FixedPanel.Panel2;
			this.sptMods.Location = new System.Drawing.Point(49, 0);
			this.sptMods.Margin = new System.Windows.Forms.Padding(2);
			this.sptMods.Name = "sptMods";
			// 
			// sptMods.Panel1
			// 
			this.sptMods.Panel1.Controls.Add(this.clwCategoryView);
			// 
			// sptMods.Panel2
			// 
			this.sptMods.Panel2.Controls.Add(this.sptSummaryInfo);
			this.sptMods.Size = new System.Drawing.Size(922, 642);
			this.sptMods.SplitterDistance = 650;
			this.sptMods.SplitterWidth = 3;
			this.sptMods.TabIndex = 1;
			// 
			// clwCategoryView
			// 
			this.clwCategoryView.CategoryModeEnabled = true;
			this.clwCategoryView.CellEditUseWholeCell = false;
			this.clwCategoryView.CheckBoxes = true;
			this.clwCategoryView.Cursor = System.Windows.Forms.Cursors.Default;
			this.clwCategoryView.Dock = System.Windows.Forms.DockStyle.Fill;
			this.clwCategoryView.FullRowSelect = true;
			this.clwCategoryView.HideSelection = false;
			this.clwCategoryView.LabelEdit = true;
			this.clwCategoryView.Location = new System.Drawing.Point(0, 0);
			this.clwCategoryView.Margin = new System.Windows.Forms.Padding(2);
			this.clwCategoryView.MultiSelect = false;
			this.clwCategoryView.Name = "clwCategoryView";
			this.clwCategoryView.ShowGroups = false;
			this.clwCategoryView.ShowHiddenCategories = false;
			this.clwCategoryView.ShowImagesOnSubItems = true;
			this.clwCategoryView.ShowItemToolTips = true;
			this.clwCategoryView.Size = new System.Drawing.Size(650, 642);
			this.clwCategoryView.Sorting = System.Windows.Forms.SortOrder.Ascending;
			this.clwCategoryView.TabIndex = 0;
			this.clwCategoryView.UseCompatibleStateImageBehavior = false;
			this.clwCategoryView.UseHyperlinks = true;
			this.clwCategoryView.View = System.Windows.Forms.View.Details;
			this.clwCategoryView.VirtualMode = true;
			this.clwCategoryView.Expanding += new System.EventHandler<BrightIdeasSoftware.TreeBranchExpandingEventArgs>(this.clwCategoryView_TreeBranchExpanding);
			this.clwCategoryView.Collapsing += new System.EventHandler<BrightIdeasSoftware.TreeBranchCollapsingEventArgs>(this.clwCategoryView_TreeBranchCollapsing);
			this.clwCategoryView.AfterLabelEdit += new System.Windows.Forms.LabelEditEventHandler(this.clwCategoryView_AfterLabelEdit);
			this.clwCategoryView.ColumnWidthChanged += new System.Windows.Forms.ColumnWidthChangedEventHandler(this.clwCategoryView_ColumnWidthChanged);
			this.clwCategoryView.ColumnWidthChanging += new System.Windows.Forms.ColumnWidthChangingEventHandler(this.clwCategoryView_ColumnWidthChanging);
			this.clwCategoryView.Resize += new System.EventHandler(this.clwCategoryView_Resize);
			// 
			// sptSummaryInfo
			// 
			this.sptSummaryInfo.Dock = System.Windows.Forms.DockStyle.Fill;
			this.sptSummaryInfo.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
			this.sptSummaryInfo.Location = new System.Drawing.Point(0, 0);
			this.sptSummaryInfo.Margin = new System.Windows.Forms.Padding(2);
			this.sptSummaryInfo.Name = "sptSummaryInfo";
			this.sptSummaryInfo.Orientation = System.Windows.Forms.Orientation.Horizontal;
			// 
			// sptSummaryInfo.Panel1
			// 
			this.sptSummaryInfo.Panel1.Controls.Add(this.ipbScreenShot);
			// 
			// sptSummaryInfo.Panel2
			// 
			this.sptSummaryInfo.Panel2.Controls.Add(this.flbInfo);
			this.sptSummaryInfo.Size = new System.Drawing.Size(269, 642);
			this.sptSummaryInfo.SplitterDistance = 142;
			this.sptSummaryInfo.SplitterWidth = 3;
			this.sptSummaryInfo.TabIndex = 0;
			// 
			// ipbScreenShot
			// 
			this.ipbScreenShot.Dock = System.Windows.Forms.DockStyle.Fill;
			this.ipbScreenShot.Location = new System.Drawing.Point(0, 0);
			this.ipbScreenShot.Margin = new System.Windows.Forms.Padding(2);
			this.ipbScreenShot.Name = "ipbScreenShot";
			this.ipbScreenShot.Size = new System.Drawing.Size(269, 142);
			this.ipbScreenShot.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
			this.ipbScreenShot.TabIndex = 0;
			this.ipbScreenShot.TabStop = false;
			// 
			// flbInfo
			// 
			this.flbInfo.BackColor = System.Drawing.SystemColors.Control;
			this.flbInfo.Dock = System.Windows.Forms.DockStyle.Fill;
			this.flbInfo.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F);
			this.flbInfo.ForeColor = System.Drawing.SystemColors.ControlText;
			this.flbInfo.Location = new System.Drawing.Point(0, 0);
			this.flbInfo.Margin = new System.Windows.Forms.Padding(2);
			this.flbInfo.MinimumSize = new System.Drawing.Size(15, 15);
			this.flbInfo.Name = "flbInfo";
			this.flbInfo.Size = new System.Drawing.Size(269, 497);
			this.flbInfo.TabIndex = 0;
			this.flbInfo.Text = null;
			// 
			// sfdChooseExport
			// 
			this.sfdChooseExport.Filter = "Text files (*.txt)|*.txt";
			this.sfdChooseExport.Title = "Save current mod list to:";
			// 
			// ModManagerControl
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(971, 642);
			this.CloseButton = false;
			this.CloseButtonVisible = false;
			this.Controls.Add(this.sptMods);
			this.Controls.Add(this.toolStrip1);
			this.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.Margin = new System.Windows.Forms.Padding(2);
			this.Name = "ModManagerControl";
			this.Text = "Mods";
			this.toolStrip1.ResumeLayout(false);
			this.toolStrip1.PerformLayout();
			this.sptMods.Panel1.ResumeLayout(false);
			this.sptMods.Panel2.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.sptMods)).EndInit();
			this.sptMods.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.clwCategoryView)).EndInit();
			this.sptSummaryInfo.Panel1.ResumeLayout(false);
			this.sptSummaryInfo.Panel2.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.sptSummaryInfo)).EndInit();
			this.sptSummaryInfo.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.ipbScreenShot)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		public System.Windows.Forms.ToolStrip toolStrip1;
		private System.Windows.Forms.SplitContainer sptMods;
		private System.Windows.Forms.SplitContainer sptSummaryInfo;
		private Nexus.UI.Controls.ImagePreviewBox ipbScreenShot;
		private Nexus.UI.Controls.FormattedLabel flbInfo;
		private System.Windows.Forms.ToolStripSplitButton tsbAddMod;
		private System.Windows.Forms.ToolStripMenuItem addModToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem addModFromURLToolStripMenuItem;
		private System.Windows.Forms.ToolStripSplitButton tsbResetCategories;
		private System.Windows.Forms.ToolStripMenuItem addNewCategory;
		private System.Windows.Forms.ToolStripMenuItem resetDefaultCategories;
		private System.Windows.Forms.ToolStripMenuItem resetUnassignedToDefaultCategories;
		private System.Windows.Forms.ToolStripMenuItem resetModsCategory;
		private System.Windows.Forms.ToolStripMenuItem removeAllCategories;
		private System.Windows.Forms.ToolStripMenuItem collapseAllCategories;
		private System.Windows.Forms.ToolStripMenuItem expandAllCategories;
		private System.Windows.Forms.ToolStripMenuItem toggleHiddenCategories;
		private System.Windows.Forms.OpenFileDialog ofdChooseMod;
		private System.Windows.Forms.ToolStripButton tsbActivate;
		private System.Windows.Forms.ToolStripButton tsbDeactivate;
		private System.Windows.Forms.ToolStripButton tsbTagMod;
		private System.Windows.Forms.ToolStripMenuItem checkModUpdates;
		private System.Windows.Forms.ToolStripMenuItem checkFileDownloadId;
		private System.Windows.Forms.ToolStripMenuItem checkMissingDownloadId;
		private System.Windows.Forms.ToolStripSplitButton tsbModOnlineChecks;
		private System.Windows.Forms.ToolStripButton tsbSwitchView;
		private System.Windows.Forms.ToolStripButton tsbToggleEndorse;
        private System.Windows.Forms.ToolStripButton tsb_SaveModLoadOrder;
        public Client.UI.Controls.CategoryListView clwCategoryView;
        private System.Windows.Forms.ToolStripButton tsb_ModUpLoadOrder;
        private System.Windows.Forms.ToolStripButton tsb_ModDownLoadOrder;
		private System.Windows.Forms.ToolStripMenuItem checkForModUpdateWithinTheLastDayToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem withinTheLastDayToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem withinTheLastWeekToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem withinTheLastMonthToolStripMenuItem;
        private System.Windows.Forms.ToolStripDropDownButton tsbExportModList;
        private System.Windows.Forms.ToolStripMenuItem exportToTextFile;
        private System.Windows.Forms.ToolStripMenuItem exportToClipboard;
        private System.Windows.Forms.SaveFileDialog sfdChooseExport;
    }
}
