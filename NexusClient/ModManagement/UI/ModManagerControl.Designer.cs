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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ModManagerControl));
			this.toolStrip1 = new System.Windows.Forms.ToolStrip();
			this.tsbAddMod = new System.Windows.Forms.ToolStripSplitButton();
			this.addModToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.addModFromURLToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.tsbResetCategories = new System.Windows.Forms.ToolStripSplitButton();
			this.addNewCategory = new System.Windows.Forms.ToolStripMenuItem();
			this.resetDefaultCategories = new System.Windows.Forms.ToolStripMenuItem();
			this.resetModsCategory = new System.Windows.Forms.ToolStripMenuItem();
			this.removeAllCategories = new System.Windows.Forms.ToolStripMenuItem();
			this.collapseAllCategories = new System.Windows.Forms.ToolStripMenuItem();
			this.expandAllCategories = new System.Windows.Forms.ToolStripMenuItem();
			this.toggleHiddenCategories = new System.Windows.Forms.ToolStripMenuItem();
			this.tsbActivate = new System.Windows.Forms.ToolStripButton();
			this.tsbDeactivate = new System.Windows.Forms.ToolStripButton();
			this.tsbDeleteMod = new System.Windows.Forms.ToolStripButton();
			this.tsbTagMod = new System.Windows.Forms.ToolStripButton();
			this.tsbCheckModVersions = new System.Windows.Forms.ToolStripButton();
			this.tsbToggleEndorse = new System.Windows.Forms.ToolStripButton();
			this.tsbSwitchView = new System.Windows.Forms.ToolStripButton();
			this.sptMods = new System.Windows.Forms.SplitContainer();
			this.lvwMods = new Nexus.UI.Controls.IconListView();
			this.clmModName = new System.Windows.Forms.ColumnHeader();
			this.clmCategory = new System.Windows.Forms.ColumnHeader();
			this.clmInstallDate = new System.Windows.Forms.ColumnHeader();
			this.clmVersion = new System.Windows.Forms.ColumnHeader();
			this.clmWebVersion = new System.Windows.Forms.ColumnHeader();
			this.clmAuthor = new System.Windows.Forms.ColumnHeader();
			this.clmEndorsement = new System.Windows.Forms.ColumnHeader();
			this.sptSummaryInfo = new System.Windows.Forms.SplitContainer();
			this.ipbScreenShot = new Nexus.UI.Controls.ImagePreviewBox();
			this.flbInfo = new Nexus.UI.Controls.FormattedLabel();
			this.clwCategoryView = new Nexus.Client.UI.Controls.CategoryListView();
			
			this.ofdChooseMod = new System.Windows.Forms.OpenFileDialog();
			this.toolStrip1.SuspendLayout();
			this.sptMods.Panel1.SuspendLayout();
			this.sptMods.Panel2.SuspendLayout();
			this.sptMods.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.clwCategoryView)).BeginInit();
			this.sptSummaryInfo.Panel1.SuspendLayout();
			this.sptSummaryInfo.Panel2.SuspendLayout();
			this.sptSummaryInfo.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.ipbScreenShot)).BeginInit();
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
            this.tsbAddMod,
            this.tsbActivate,
            this.tsbDeactivate,
            this.tsbDeleteMod,
            this.tsbTagMod,
			this.tsbCheckModVersions,
			this.tsbToggleEndorse,
			this.tsbResetCategories,
			this.tsbSwitchView});
			this.toolStrip1.Location = new System.Drawing.Point(0, 0);
			this.toolStrip1.Name = "toolStrip1";
			this.toolStrip1.Size = new System.Drawing.Size(49, 453);
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
			this.addModToolStripMenuItem.Image = global::Nexus.Client.Properties.Resources.add_mod;
			this.addModToolStripMenuItem.Name = "addModToolStripMenuItem";
			this.addModToolStripMenuItem.Size = new System.Drawing.Size(195, 38);
			this.addModToolStripMenuItem.Text = "Add Mod from File";
			this.addModToolStripMenuItem.Click += new System.EventHandler(this.addModToolStripMenuItem_Click);
			// 
			// addModFromURLToolStripMenuItem
			// 
			this.addModFromURLToolStripMenuItem.Image = global::Nexus.Client.Properties.Resources.add_mod_url;
			this.addModFromURLToolStripMenuItem.Name = "addModFromURLToolStripMenuItem";
			this.addModFromURLToolStripMenuItem.Size = new System.Drawing.Size(195, 38);
			this.addModFromURLToolStripMenuItem.Text = "Add Mod from URL";
			this.addModFromURLToolStripMenuItem.Click += new System.EventHandler(this.addModFromURLToolStripMenuItem_Click);
			// 
			// tsbResetCategories
			// 
			this.tsbResetCategories.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbResetCategories.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.addNewCategory,
			this.collapseAllCategories,
			this.expandAllCategories,
			this.resetDefaultCategories,
            this.resetModsCategory,
			this.removeAllCategories,
			this.toggleHiddenCategories});
			this.tsbResetCategories.Image = global::Nexus.Client.Properties.Resources.reset_categories;
			this.tsbResetCategories.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbResetCategories.Name = "tsbResetCategories";
			this.tsbResetCategories.Size = new System.Drawing.Size(46, 36);
			this.tsbResetCategories.Text = "Categories: add new category";
			// 
			// addNewCategory
			// 
			this.addNewCategory.Image = global::Nexus.Client.Properties.Resources.reset_categories;
			this.addNewCategory.Name = "addNewCategory";
			this.addNewCategory.Size = new System.Drawing.Size(195, 38);
			this.addNewCategory.Text = "Categories: add new category";
			this.addNewCategory.Click += new System.EventHandler(this.addNewCategory_Click);
			// 
			// resetDefaultCategories
			// 
			this.resetDefaultCategories.Image = global::Nexus.Client.Properties.Resources.reset_default;
			this.resetDefaultCategories.Name = "resetDefaultCategories";
			this.resetDefaultCategories.Size = new System.Drawing.Size(195, 38);
			this.resetDefaultCategories.Text = "Categories: reset to Nexus site defaults";
			this.resetDefaultCategories.Click += new System.EventHandler(this.resetDefaultCategories_Click);
			// 
			// resetModsCategory
			// 
			this.resetModsCategory.Image = global::Nexus.Client.Properties.Resources.reset_unassigned;
			this.resetModsCategory.Name = "resetModsCategory";
			this.resetModsCategory.Size = new System.Drawing.Size(195, 38);
			this.resetModsCategory.Text = "Categories: reset all mods to unassigned";
			this.resetModsCategory.Click += new System.EventHandler(this.resetModsCategory_Click);
			// 
			// removeAllCategories
			// 
			this.removeAllCategories.Image = global::Nexus.Client.Properties.Resources.remove_all_categories;
			this.removeAllCategories.Name = "removeAllCategories";
			this.removeAllCategories.Size = new System.Drawing.Size(195, 38);
			this.removeAllCategories.Text = "Categories: remove all categories";
			this.removeAllCategories.Click += new System.EventHandler(this.removeAllCategories_Click);
			// 
			// collapseAllCategories
			// 
			this.collapseAllCategories.Image = global::Nexus.Client.Properties.Resources.collapse_all;
			this.collapseAllCategories.Name = "collapseAllCategories";
			this.collapseAllCategories.Size = new System.Drawing.Size(195, 38);
			this.collapseAllCategories.Text = "Categories: collapse all categories";
			this.collapseAllCategories.Click += new System.EventHandler(this.collapseAllCategories_Click);
			// 
			// expandAllCategories
			// 
			this.expandAllCategories.Image = global::Nexus.Client.Properties.Resources.expand_all;
			this.expandAllCategories.Name = "expandAllCategories";
			this.expandAllCategories.Size = new System.Drawing.Size(195, 38);
			this.expandAllCategories.Text = "Categories: expand all categories";
			this.expandAllCategories.Click += new System.EventHandler(this.expandAllCategories_Click);
			// 
			// toggleHiddenCategories
			// 
			this.toggleHiddenCategories.Image = global::Nexus.Client.Properties.Resources.reset_categories;
			this.toggleHiddenCategories.Name = "toggleHiddenCategories";
			this.toggleHiddenCategories.Size = new System.Drawing.Size(195, 38);
			this.toggleHiddenCategories.Text = "Categories: toggle hidden categories";
			this.toggleHiddenCategories.Click += new System.EventHandler(this.toggleHiddenCategories_Click);
			// 
			// tsbActivate
			// 
			this.tsbActivate.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbActivate.Image = global::Nexus.Client.Properties.Resources.activate_mod;
			this.tsbActivate.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbActivate.Name = "tsbActivate";
			this.tsbActivate.Size = new System.Drawing.Size(46, 36);
			this.tsbActivate.Text = "toolStripButton1";
			// 
			// tsbDeactivate
			// 
			this.tsbDeactivate.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbDeactivate.Image = global::Nexus.Client.Properties.Resources.deactivate_mod;
			this.tsbDeactivate.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbDeactivate.Name = "tsbDeactivate";
			this.tsbDeactivate.Size = new System.Drawing.Size(46, 36);
			this.tsbDeactivate.Text = "toolStripButton1";
			// 
			// tsbDeleteMod
			// 
			this.tsbDeleteMod.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbDeleteMod.Image = global::Nexus.Client.Properties.Resources.edit_delete_6;
			this.tsbDeleteMod.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbDeleteMod.Name = "tsbDeleteMod";
			this.tsbDeleteMod.Size = new System.Drawing.Size(46, 36);
			this.tsbDeleteMod.Text = "toolStripButton1";
			// 
			// tsbTagMod
			// 
			this.tsbTagMod.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbTagMod.Image = global::Nexus.Client.Properties.Resources.info_add;
			this.tsbTagMod.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbTagMod.Name = "tsbTagMod";
			this.tsbTagMod.Size = new System.Drawing.Size(46, 36);
			this.tsbTagMod.Text = "toolStripButton1";
			// 
			// tsbCheckModVersions
			// 
			this.tsbCheckModVersions.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbCheckModVersions.Image = global::Nexus.Client.Properties.Resources.change_game_mode;
			this.tsbCheckModVersions.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbCheckModVersions.Name = "tsbCheckModVersions";
			this.tsbCheckModVersions.Size = new System.Drawing.Size(46, 36);
			this.tsbCheckModVersions.Text = "toolStripButton1";
			// 
			// tsbToggleEndorse
			// 
			this.tsbToggleEndorse.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbToggleEndorse.Image = global::Nexus.Client.Properties.Resources.unendorsed;
			this.tsbToggleEndorse.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbToggleEndorse.Name = "tsbToggleEndorse";
			this.tsbToggleEndorse.Size = new System.Drawing.Size(46, 36);
			this.tsbToggleEndorse.Text = "toolStripButton1";
			this.tsbToggleEndorse.Enabled = false;
			// 
			// tsbSwitchView
			// 
			this.tsbSwitchView.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbSwitchView.Image = global::Nexus.Client.Properties.Resources.switch_view;
			this.tsbSwitchView.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbSwitchView.Name = "tsbSwitchView";
			this.tsbSwitchView.Size = new System.Drawing.Size(46, 36);
			this.tsbSwitchView.Text = "Switches the Mod Manager views";
			this.tsbSwitchView.Click += new System.EventHandler(tsbSwitchCategory_Click);
			// 
			// sptMods
			// 
			this.sptMods.Dock = System.Windows.Forms.DockStyle.Fill;
			this.sptMods.FixedPanel = System.Windows.Forms.FixedPanel.Panel2;
			this.sptMods.Location = new System.Drawing.Point(49, 0);
			this.sptMods.Name = "sptMods";
			// 
			// sptMods.Panel1
			// 
			this.sptMods.Panel1.Controls.Add(this.lvwMods);
			this.sptMods.Panel1.Controls.Add(this.clwCategoryView);
			// 
			// sptMods.Panel2
			// 
			this.sptMods.Panel2.Controls.Add(this.sptSummaryInfo);
			this.sptMods.Size = new System.Drawing.Size(608, 453);
			this.sptMods.SplitterDistance = 315;
			this.sptMods.TabIndex = 1;
			// 
			// lvwMods
			// 
			this.lvwMods.CheckBoxes = true;
			this.lvwMods.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.clmModName,
			this.clmCategory,
			this.clmInstallDate,
            this.clmVersion,
            this.clmWebVersion,
			this.clmEndorsement,
            this.clmAuthor});
			this.lvwMods.Dock = System.Windows.Forms.DockStyle.Fill;
			this.lvwMods.FullRowSelect = true;
			this.lvwMods.HideSelection = false;
			this.lvwMods.LabelEdit = true;
			this.lvwMods.Location = new System.Drawing.Point(0, 0);
			this.lvwMods.MultiSelect = false;
			this.lvwMods.Name = "lvwMods";
			this.lvwMods.OwnerDraw = true;
			this.lvwMods.ShowItemToolTips = true;
			this.lvwMods.Size = new System.Drawing.Size(315, 453);
			this.lvwMods.Sorting = System.Windows.Forms.SortOrder.Ascending;
			this.lvwMods.TabIndex = 0;
			this.lvwMods.UseCompatibleStateImageBehavior = false;
			this.lvwMods.View = System.Windows.Forms.View.Details;
			this.lvwMods.Resize += new System.EventHandler(this.lvwMods_Resize);
			this.lvwMods.AfterLabelEdit += new System.Windows.Forms.LabelEditEventHandler(this.lvwMods_AfterLabelEdit);
			this.lvwMods.SelectedIndexChanged += new System.EventHandler(this.lvwMods_SelectedIndexChanged);
			this.lvwMods.MouseMove += new System.Windows.Forms.MouseEventHandler(this.lvwMods_MouseMove);
			this.lvwMods.MouseDown += new System.Windows.Forms.MouseEventHandler(this.lvwMods_MouseDown);
			this.lvwMods.ColumnWidthChanging += new System.Windows.Forms.ColumnWidthChangingEventHandler(this.lvwMods_ColumnWidthChanging);
			this.lvwMods.Visible = false;
			// 
			// clmModName
			// 
			this.clmModName.Text = "Name";
			// 
			// clmCategory
			// 
			this.clmCategory.Text = "Category";
			// 
			// clmInstallDate
			// 
			this.clmInstallDate.Text = "Install Date";
			this.clmInstallDate.Width = 125;
			// 
			// clmVersion
			// 
			this.clmVersion.Text = "Version";
			// 
			// clmWebVersion
			// 
			this.clmWebVersion.Text = "Latest Version";
			// 
			// clmAuthor
			// 
			this.clmAuthor.Text = "Author";
			// 
			// clmEndorsement
			// 
			this.clmEndorsement.Text = "Endorsement";
			// 
			// clwCategoryView
			// 
			this.clwCategoryView.CheckBoxes = true;
			this.clwCategoryView.Dock = System.Windows.Forms.DockStyle.Fill;
			this.clwCategoryView.FullRowSelect = true;
			this.clwCategoryView.HideSelection = false;
			this.clwCategoryView.LabelEdit = true;
			this.clwCategoryView.Location = new System.Drawing.Point(0, 0);
			this.clwCategoryView.MultiSelect = false;
			this.clwCategoryView.Name = "clwCategoryView";
			this.clwCategoryView.OwnerDraw = true;
			this.clwCategoryView.ShowGroups = false;
			this.clwCategoryView.ShowImagesOnSubItems = true;
			this.clwCategoryView.ShowItemToolTips = true;
			this.clwCategoryView.Size = new System.Drawing.Size(315, 453);
			this.clwCategoryView.Sorting = System.Windows.Forms.SortOrder.Ascending;
			this.clwCategoryView.TabIndex = 0;
			this.clwCategoryView.UseCompatibleStateImageBehavior = false;
			this.clwCategoryView.UseHyperlinks = true;
			this.clwCategoryView.View = System.Windows.Forms.View.Details;
			this.clwCategoryView.VirtualMode = true;
			this.clwCategoryView.Resize += new System.EventHandler(this.lvwMods_Resize);
			this.clwCategoryView.AfterLabelEdit += new System.Windows.Forms.LabelEditEventHandler(this.clwCategoryView_AfterLabelEdit);
			this.clwCategoryView.ColumnWidthChanging += new System.Windows.Forms.ColumnWidthChangingEventHandler(this.clwCategoryView_ColumnWidthChanging);
			this.clwCategoryView.Visible = true;
			// 
			// sptSummaryInfo
			// 
			this.sptSummaryInfo.Dock = System.Windows.Forms.DockStyle.Fill;
			this.sptSummaryInfo.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
			this.sptSummaryInfo.Location = new System.Drawing.Point(0, 0);
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
			this.sptSummaryInfo.Size = new System.Drawing.Size(289, 453);
			this.sptSummaryInfo.SplitterDistance = 142;
			this.sptSummaryInfo.TabIndex = 0;
			// 
			// ipbScreenShot
			// 
			this.ipbScreenShot.Dock = System.Windows.Forms.DockStyle.Fill;
			this.ipbScreenShot.Location = new System.Drawing.Point(0, 0);
			this.ipbScreenShot.Name = "ipbScreenShot";
			this.ipbScreenShot.Size = new System.Drawing.Size(289, 142);
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
			this.flbInfo.MinimumSize = new System.Drawing.Size(20, 20);
			this.flbInfo.Name = "flbInfo";
			this.flbInfo.Size = new System.Drawing.Size(289, 307);
			this.flbInfo.TabIndex = 0;
			this.flbInfo.Text = null;
			// 
			// ModManagerControl
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(657, 453);
			this.CloseButton = false;
			this.CloseButtonVisible = false;
			this.Controls.Add(this.sptMods);
			this.Controls.Add(this.toolStrip1);
			this.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.Name = "ModManagerControl";
			this.Text = "Mods";
			this.toolStrip1.ResumeLayout(false);
			this.toolStrip1.PerformLayout();
			this.sptMods.Panel1.ResumeLayout(false);
			this.sptMods.Panel2.ResumeLayout(false);
			this.sptMods.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.clwCategoryView)).EndInit();
			this.sptSummaryInfo.Panel1.ResumeLayout(false);
			this.sptSummaryInfo.Panel2.ResumeLayout(false);
			this.sptSummaryInfo.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.ipbScreenShot)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();
		}

		#endregion

		private System.Windows.Forms.ToolStrip toolStrip1;
		private System.Windows.Forms.SplitContainer sptMods;
		private Nexus.UI.Controls.IconListView lvwMods;
		private System.Windows.Forms.SplitContainer sptSummaryInfo;
		private Nexus.UI.Controls.ImagePreviewBox ipbScreenShot;
		private Nexus.UI.Controls.FormattedLabel flbInfo;
		private Nexus.Client.UI.Controls.CategoryListView clwCategoryView;
		private System.Windows.Forms.ToolStripSplitButton tsbAddMod;
		private System.Windows.Forms.ToolStripMenuItem addModToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem addModFromURLToolStripMenuItem;
		private System.Windows.Forms.ToolStripSplitButton tsbResetCategories;
		private System.Windows.Forms.ToolStripMenuItem addNewCategory;
		private System.Windows.Forms.ToolStripMenuItem resetDefaultCategories;
		private System.Windows.Forms.ToolStripMenuItem resetModsCategory;
		private System.Windows.Forms.ToolStripMenuItem removeAllCategories;
		private System.Windows.Forms.ToolStripMenuItem collapseAllCategories;
		private System.Windows.Forms.ToolStripMenuItem expandAllCategories;
		private System.Windows.Forms.ToolStripMenuItem toggleHiddenCategories;
		private System.Windows.Forms.OpenFileDialog ofdChooseMod;
		private System.Windows.Forms.ColumnHeader clmModName;
		private System.Windows.Forms.ColumnHeader clmCategory;
		private System.Windows.Forms.ColumnHeader clmInstallDate;
		private System.Windows.Forms.ColumnHeader clmVersion;
		private System.Windows.Forms.ColumnHeader clmWebVersion;
		private System.Windows.Forms.ColumnHeader clmAuthor;
		private System.Windows.Forms.ColumnHeader clmEndorsement;
		private System.Windows.Forms.ToolStripButton tsbActivate;
		private System.Windows.Forms.ToolStripButton tsbDeactivate;
		private System.Windows.Forms.ToolStripButton tsbDeleteMod;
		private System.Windows.Forms.ToolStripButton tsbTagMod;
		private System.Windows.Forms.ToolStripButton tsbCheckModVersions;
		private System.Windows.Forms.ToolStripButton tsbToggleEndorse;
		private System.Windows.Forms.ToolStripButton tsbSwitchView;
	}
}
