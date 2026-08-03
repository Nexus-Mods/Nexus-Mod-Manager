namespace Nexus.Client.ModManagement.UI
{
	partial class ModTaggerForm
	{
		private System.ComponentModel.IContainer components = null;
		private DevExpress.XtraEditors.SplitContainerControl splitMain;
		private DevExpress.XtraEditors.GroupControl grpCandidates;
		private DevExpress.XtraGrid.GridControl grdCandidates;
		private DevExpress.XtraGrid.Views.Grid.GridView grvCandidates;
		private DevExpress.XtraGrid.Columns.GridColumn colCandidateName;
		private DevExpress.XtraGrid.Columns.GridColumn colCandidateVersion;
		private DevExpress.XtraGrid.Columns.GridColumn colCandidateFileId;
		private DevExpress.XtraEditors.LabelControl lblCandidateHint;
		private DevExpress.XtraLayout.LayoutControl editorLayout;
		private DevExpress.XtraLayout.LayoutControlGroup editorRoot;
		private DevExpress.XtraEditors.TextEdit txtName;
		private DevExpress.XtraEditors.TextEdit txtVersion;
		private DevExpress.XtraEditors.TextEdit txtAuthor;
		private DevExpress.XtraEditors.TextEdit txtWebsite;
		private DevExpress.XtraEditors.TextEdit txtModId;
		private DevExpress.XtraEditors.TextEdit txtFileId;
		private DevExpress.XtraEditors.PanelControl pnlDescription;
		private DevExpress.XtraEditors.PanelControl pnlDescriptionToolbar;
		private DevExpress.XtraEditors.CheckButton btnEditDescription;
		private DevExpress.XtraEditors.HtmlContentControl htmlDescription;
		private DevExpress.XtraEditors.MemoEdit txtDescription;
		private DevExpress.XtraEditors.PictureEdit picScreenshot;
		private DevExpress.XtraEditors.SimpleButton btnOpenWebsite;
		private DevExpress.XtraEditors.SimpleButton btnSetScreenshot;
		private DevExpress.XtraEditors.SimpleButton btnClearScreenshot;
		private DevExpress.XtraEditors.PanelControl buttonPanel;
		private DevExpress.XtraEditors.SimpleButton btnRestoreCurrent;
		private DevExpress.XtraEditors.SimpleButton btnOK;
		private DevExpress.XtraEditors.SimpleButton btnCancel;
		private DevExpress.XtraEditors.DXErrorProvider.DXErrorProvider errorProvider;

		/// <summary>
		/// Releases resources used by the dialog.
		/// </summary>
		/// <param name="disposing">Whether managed resources should be released.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && components != null)
				components.Dispose();
			base.Dispose(disposing);
		}

		private void InitializeComponent()
		{
			this.components = new System.ComponentModel.Container();
			this.splitMain = new DevExpress.XtraEditors.SplitContainerControl();
			this.grpCandidates = new DevExpress.XtraEditors.GroupControl();
			this.grdCandidates = new DevExpress.XtraGrid.GridControl();
			this.grvCandidates = new DevExpress.XtraGrid.Views.Grid.GridView();
			this.colCandidateName = new DevExpress.XtraGrid.Columns.GridColumn();
			this.colCandidateVersion = new DevExpress.XtraGrid.Columns.GridColumn();
			this.colCandidateFileId = new DevExpress.XtraGrid.Columns.GridColumn();
			this.lblCandidateHint = new DevExpress.XtraEditors.LabelControl();
			this.editorLayout = new DevExpress.XtraLayout.LayoutControl();
			this.txtName = new DevExpress.XtraEditors.TextEdit();
			this.txtVersion = new DevExpress.XtraEditors.TextEdit();
			this.txtAuthor = new DevExpress.XtraEditors.TextEdit();
			this.txtWebsite = new DevExpress.XtraEditors.TextEdit();
			this.txtModId = new DevExpress.XtraEditors.TextEdit();
			this.txtFileId = new DevExpress.XtraEditors.TextEdit();
			this.pnlDescription = new DevExpress.XtraEditors.PanelControl();
			this.pnlDescriptionToolbar = new DevExpress.XtraEditors.PanelControl();
			this.btnEditDescription = new DevExpress.XtraEditors.CheckButton();
			this.htmlDescription = new DevExpress.XtraEditors.HtmlContentControl();
			this.txtDescription = new DevExpress.XtraEditors.MemoEdit();
			this.picScreenshot = new DevExpress.XtraEditors.PictureEdit();
			this.btnOpenWebsite = new DevExpress.XtraEditors.SimpleButton();
			this.btnSetScreenshot = new DevExpress.XtraEditors.SimpleButton();
			this.btnClearScreenshot = new DevExpress.XtraEditors.SimpleButton();
			this.editorRoot = new DevExpress.XtraLayout.LayoutControlGroup();
			this.buttonPanel = new DevExpress.XtraEditors.PanelControl();
			this.btnRestoreCurrent = new DevExpress.XtraEditors.SimpleButton();
			this.btnOK = new DevExpress.XtraEditors.SimpleButton();
			this.btnCancel = new DevExpress.XtraEditors.SimpleButton();
			this.errorProvider = new DevExpress.XtraEditors.DXErrorProvider.DXErrorProvider(this.components);
			((System.ComponentModel.ISupportInitialize)(this.splitMain)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.splitMain.Panel1)).BeginInit();
			this.splitMain.Panel1.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.splitMain.Panel2)).BeginInit();
			this.splitMain.Panel2.SuspendLayout();
			this.splitMain.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.grpCandidates)).BeginInit();
			this.grpCandidates.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.grdCandidates)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.grvCandidates)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.editorLayout)).BeginInit();
			this.editorLayout.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.txtName.Properties)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.txtVersion.Properties)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.txtAuthor.Properties)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.txtWebsite.Properties)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.txtModId.Properties)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.txtFileId.Properties)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.pnlDescription)).BeginInit();
			this.pnlDescription.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.pnlDescriptionToolbar)).BeginInit();
			this.pnlDescriptionToolbar.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.txtDescription.Properties)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.picScreenshot.Properties)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.editorRoot)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.buttonPanel)).BeginInit();
			this.buttonPanel.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.errorProvider)).BeginInit();
			this.SuspendLayout();
			//
			// splitMain
			//
			this.splitMain.Dock = System.Windows.Forms.DockStyle.Fill;
			this.splitMain.Location = new System.Drawing.Point(0, 0);
			this.splitMain.Name = "splitMain";
			this.splitMain.Panel1.Controls.Add(this.grpCandidates);
			this.splitMain.Panel2.Controls.Add(this.editorLayout);
			this.splitMain.Size = new System.Drawing.Size(1000, 612);
			this.splitMain.SplitterPosition = 320;
			this.splitMain.TabIndex = 0;
			//
			// grpCandidates
			//
			this.grpCandidates.Controls.Add(this.grdCandidates);
			this.grpCandidates.Controls.Add(this.lblCandidateHint);
			this.grpCandidates.Dock = System.Windows.Forms.DockStyle.Fill;
			this.grpCandidates.Location = new System.Drawing.Point(0, 0);
			this.grpCandidates.Name = "grpCandidates";
			this.grpCandidates.Padding = new System.Windows.Forms.Padding(8);
			this.grpCandidates.Size = new System.Drawing.Size(320, 612);
			this.grpCandidates.TabIndex = 0;
			this.grpCandidates.Text = "Nexus matches";
			//
			// lblCandidateHint
			//
			this.lblCandidateHint.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.Vertical;
			this.lblCandidateHint.Dock = System.Windows.Forms.DockStyle.Top;
			this.lblCandidateHint.Location = new System.Drawing.Point(10, 31);
			this.lblCandidateHint.Name = "lblCandidateHint";
			this.lblCandidateHint.Padding = new System.Windows.Forms.Padding(0, 0, 0, 8);
			this.lblCandidateHint.Size = new System.Drawing.Size(300, 36);
			this.lblCandidateHint.TabIndex = 0;
			this.lblCandidateHint.Text = "Select the exact Nexus file when one is available, or edit the metadata manually.";
			//
			// grdCandidates
			//
			this.grdCandidates.Dock = System.Windows.Forms.DockStyle.Fill;
			this.grdCandidates.Location = new System.Drawing.Point(10, 67);
			this.grdCandidates.MainView = this.grvCandidates;
			this.grdCandidates.Name = "grdCandidates";
			this.grdCandidates.Size = new System.Drawing.Size(300, 535);
			this.grdCandidates.TabIndex = 1;
			this.grdCandidates.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] { this.grvCandidates });
			//
			// grvCandidates
			//
			this.grvCandidates.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] { this.colCandidateName, this.colCandidateVersion, this.colCandidateFileId });
			this.grvCandidates.FocusRectStyle = DevExpress.XtraGrid.Views.Grid.DrawFocusRectStyle.RowFocus;
			this.grvCandidates.GridControl = this.grdCandidates;
			this.grvCandidates.Name = "grvCandidates";
			this.grvCandidates.OptionsBehavior.Editable = false;
			this.grvCandidates.OptionsBehavior.ReadOnly = true;
			this.grvCandidates.OptionsSelection.EnableAppearanceFocusedCell = false;
			this.grvCandidates.OptionsView.ShowGroupPanel = false;
			this.grvCandidates.OptionsView.ShowIndicator = false;
			this.grvCandidates.OptionsView.ShowAutoFilterRow = true;
			this.grvCandidates.FocusedRowChanged += new DevExpress.XtraGrid.Views.Base.FocusedRowChangedEventHandler(this.grvCandidates_FocusedRowChanged);
			//
			// colCandidateName
			//
			this.colCandidateName.Caption = "FILE / MOD";
			this.colCandidateName.FieldName = "ModName";
			this.colCandidateName.Name = "colCandidateName";
			this.colCandidateName.Visible = true;
			this.colCandidateName.VisibleIndex = 0;
			this.colCandidateName.Width = 170;
			//
			// colCandidateVersion
			//
			this.colCandidateVersion.Caption = "VERSION";
			this.colCandidateVersion.FieldName = "HumanReadableVersion";
			this.colCandidateVersion.Name = "colCandidateVersion";
			this.colCandidateVersion.Visible = true;
			this.colCandidateVersion.VisibleIndex = 1;
			this.colCandidateVersion.Width = 70;
			//
			// colCandidateFileId
			//
			this.colCandidateFileId.Caption = "FILE ID";
			this.colCandidateFileId.FieldName = "DownloadId";
			this.colCandidateFileId.Name = "colCandidateFileId";
			this.colCandidateFileId.Visible = true;
			this.colCandidateFileId.VisibleIndex = 2;
			this.colCandidateFileId.Width = 60;
			//
			// editorLayout
			//
			this.editorLayout.AllowCustomization = false;
			this.editorLayout.Controls.Add(this.txtName);
			this.editorLayout.Controls.Add(this.txtVersion);
			this.editorLayout.Controls.Add(this.txtAuthor);
			this.editorLayout.Controls.Add(this.txtWebsite);
			this.editorLayout.Controls.Add(this.txtModId);
			this.editorLayout.Controls.Add(this.txtFileId);
			this.editorLayout.Controls.Add(this.pnlDescription);
			this.editorLayout.Controls.Add(this.picScreenshot);
			this.editorLayout.Controls.Add(this.btnOpenWebsite);
			this.editorLayout.Controls.Add(this.btnSetScreenshot);
			this.editorLayout.Controls.Add(this.btnClearScreenshot);
			this.editorLayout.Dock = System.Windows.Forms.DockStyle.Fill;
			this.editorLayout.Location = new System.Drawing.Point(0, 0);
			this.editorLayout.Name = "editorLayout";
			this.editorLayout.Root = this.editorRoot;
			this.editorLayout.Size = new System.Drawing.Size(670, 612);
			this.editorLayout.TabIndex = 0;
			this.editorLayout.Text = "editorLayout";
			//
			// txtName
			//
			this.txtName.Location = new System.Drawing.Point(118, 12);
			this.txtName.Name = "txtName";
			this.txtName.Size = new System.Drawing.Size(540, 22);
			this.txtName.StyleController = this.editorLayout;
			this.txtName.TabIndex = 0;
			//
			// txtVersion
			//
			this.txtVersion.Location = new System.Drawing.Point(118, 38);
			this.txtVersion.Name = "txtVersion";
			this.txtVersion.Size = new System.Drawing.Size(540, 22);
			this.txtVersion.StyleController = this.editorLayout;
			this.txtVersion.TabIndex = 1;
			//
			// txtAuthor
			//
			this.txtAuthor.Location = new System.Drawing.Point(118, 64);
			this.txtAuthor.Name = "txtAuthor";
			this.txtAuthor.Size = new System.Drawing.Size(540, 22);
			this.txtAuthor.StyleController = this.editorLayout;
			this.txtAuthor.TabIndex = 2;
			//
			// txtWebsite
			//
			this.txtWebsite.Location = new System.Drawing.Point(118, 90);
			this.txtWebsite.Name = "txtWebsite";
			this.txtWebsite.Size = new System.Drawing.Size(540, 22);
			this.txtWebsite.StyleController = this.editorLayout;
			this.txtWebsite.TabIndex = 3;
			this.txtWebsite.Validated += new System.EventHandler(this.txtWebsite_Validated);
			//
			// txtModId
			//
			this.txtModId.Location = new System.Drawing.Point(118, 116);
			this.txtModId.Name = "txtModId";
			this.txtModId.Properties.Mask.EditMask = "d";
			this.txtModId.Properties.Mask.MaskType = DevExpress.XtraEditors.Mask.MaskType.Numeric;
			this.txtModId.Properties.Mask.UseMaskAsDisplayFormat = false;
			this.txtModId.Size = new System.Drawing.Size(540, 22);
			this.txtModId.StyleController = this.editorLayout;
			this.txtModId.TabIndex = 4;
			//
			// txtFileId
			//
			this.txtFileId.Location = new System.Drawing.Point(118, 142);
			this.txtFileId.Name = "txtFileId";
			this.txtFileId.Properties.Mask.EditMask = "d";
			this.txtFileId.Properties.Mask.MaskType = DevExpress.XtraEditors.Mask.MaskType.Numeric;
			this.txtFileId.Properties.Mask.UseMaskAsDisplayFormat = false;
			this.txtFileId.Size = new System.Drawing.Size(540, 22);
			this.txtFileId.StyleController = this.editorLayout;
			this.txtFileId.TabIndex = 5;
			//
			// pnlDescription
			//
			this.pnlDescription.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
			this.pnlDescription.Controls.Add(this.htmlDescription);
			this.pnlDescription.Controls.Add(this.txtDescription);
			this.pnlDescription.Controls.Add(this.pnlDescriptionToolbar);
			this.pnlDescription.Location = new System.Drawing.Point(118, 168);
			this.pnlDescription.Name = "pnlDescription";
			this.pnlDescription.Size = new System.Drawing.Size(540, 166);
			this.pnlDescription.TabIndex = 6;
			//
			// pnlDescriptionToolbar
			//
			this.pnlDescriptionToolbar.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
			this.pnlDescriptionToolbar.Controls.Add(this.btnEditDescription);
			this.pnlDescriptionToolbar.Dock = System.Windows.Forms.DockStyle.Top;
			this.pnlDescriptionToolbar.Location = new System.Drawing.Point(0, 0);
			this.pnlDescriptionToolbar.Name = "pnlDescriptionToolbar";
			this.pnlDescriptionToolbar.Size = new System.Drawing.Size(540, 28);
			this.pnlDescriptionToolbar.TabIndex = 0;
			//
			// btnEditDescription
			//
			this.btnEditDescription.Dock = System.Windows.Forms.DockStyle.Right;
			this.btnEditDescription.Location = new System.Drawing.Point(430, 0);
			this.btnEditDescription.Name = "btnEditDescription";
			this.btnEditDescription.Size = new System.Drawing.Size(110, 28);
			this.btnEditDescription.TabIndex = 0;
			this.btnEditDescription.Text = "Edit source";
			this.btnEditDescription.CheckedChanged += new System.EventHandler(this.btnEditDescription_CheckedChanged);
			//
			// htmlDescription
			//
			this.htmlDescription.AutoScroll = true;
			this.htmlDescription.Dock = System.Windows.Forms.DockStyle.Fill;
			this.htmlDescription.Location = new System.Drawing.Point(0, 28);
			this.htmlDescription.Name = "htmlDescription";
			this.htmlDescription.Size = new System.Drawing.Size(540, 138);
			this.htmlDescription.TabIndex = 1;
			//
			// txtDescription
			//
			this.txtDescription.Dock = System.Windows.Forms.DockStyle.Fill;
			this.txtDescription.Location = new System.Drawing.Point(0, 28);
			this.txtDescription.Name = "txtDescription";
			this.txtDescription.Properties.AcceptsReturn = true;
			this.txtDescription.Properties.AcceptsTab = true;
			this.txtDescription.Properties.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
			this.txtDescription.Size = new System.Drawing.Size(540, 138);
			this.txtDescription.TabIndex = 2;
			this.txtDescription.Visible = false;
			//
			// picScreenshot
			//
			this.picScreenshot.Location = new System.Drawing.Point(118, 338);
			this.picScreenshot.Name = "picScreenshot";
			this.picScreenshot.Properties.AllowFocused = false;
			this.picScreenshot.Properties.NullText = "No screenshot";
			this.picScreenshot.Properties.ShowCameraMenuItem = DevExpress.XtraEditors.Controls.CameraMenuItemVisibility.Auto;
			this.picScreenshot.Properties.ShowMenu = false;
			this.picScreenshot.Properties.SizeMode = DevExpress.XtraEditors.Controls.PictureSizeMode.Zoom;
			this.picScreenshot.Size = new System.Drawing.Size(540, 184);
			this.picScreenshot.StyleController = this.editorLayout;
			this.picScreenshot.TabIndex = 7;
			//
			// btnOpenWebsite
			//
			this.btnOpenWebsite.Location = new System.Drawing.Point(12, 526);
			this.btnOpenWebsite.Name = "btnOpenWebsite";
			this.btnOpenWebsite.Size = new System.Drawing.Size(210, 27);
			this.btnOpenWebsite.StyleController = this.editorLayout;
			this.btnOpenWebsite.TabIndex = 8;
			this.btnOpenWebsite.Text = "Open Nexus page";
			this.btnOpenWebsite.Click += new System.EventHandler(this.btnOpenWebsite_Click);
			//
			// btnSetScreenshot
			//
			this.btnSetScreenshot.Location = new System.Drawing.Point(226, 526);
			this.btnSetScreenshot.Name = "btnSetScreenshot";
			this.btnSetScreenshot.Size = new System.Drawing.Size(210, 27);
			this.btnSetScreenshot.StyleController = this.editorLayout;
			this.btnSetScreenshot.TabIndex = 9;
			this.btnSetScreenshot.Text = "Set screenshot";
			this.btnSetScreenshot.Click += new System.EventHandler(this.btnSetScreenshot_Click);
			//
			// btnClearScreenshot
			//
			this.btnClearScreenshot.Location = new System.Drawing.Point(440, 526);
			this.btnClearScreenshot.Name = "btnClearScreenshot";
			this.btnClearScreenshot.Size = new System.Drawing.Size(218, 27);
			this.btnClearScreenshot.StyleController = this.editorLayout;
			this.btnClearScreenshot.TabIndex = 10;
			this.btnClearScreenshot.Text = "Clear screenshot";
			this.btnClearScreenshot.Click += new System.EventHandler(this.btnClearScreenshot_Click);
			//
			// editorRoot
			//
			this.editorRoot.EnableIndentsWithoutBorders = DevExpress.Utils.DefaultBoolean.True;
			this.editorRoot.GroupBordersVisible = false;
			this.editorRoot.LayoutMode = DevExpress.XtraLayout.Utils.LayoutMode.Table;
			this.editorRoot.Name = "editorRoot";
			this.editorRoot.Size = new System.Drawing.Size(670, 612);
			this.editorRoot.TextVisible = false;
			this.editorRoot.OptionsTableLayoutGroup.ColumnDefinitions.Clear();
			this.editorRoot.OptionsTableLayoutGroup.RowDefinitions.Clear();
			this.editorRoot.OptionsTableLayoutGroup.ColumnDefinitions.Add(new DevExpress.XtraLayout.ColumnDefinition(this.editorRoot, 100D, System.Windows.Forms.SizeType.Percent));
			for (System.Int32 rowIndex = 0; rowIndex < 6; rowIndex++)
				this.editorRoot.OptionsTableLayoutGroup.RowDefinitions.Add(new DevExpress.XtraLayout.RowDefinition(this.editorRoot, 100D, System.Windows.Forms.SizeType.AutoSize));
			this.editorRoot.OptionsTableLayoutGroup.RowDefinitions.Add(new DevExpress.XtraLayout.RowDefinition(this.editorRoot, 45D, System.Windows.Forms.SizeType.Percent));
			this.editorRoot.OptionsTableLayoutGroup.RowDefinitions.Add(new DevExpress.XtraLayout.RowDefinition(this.editorRoot, 55D, System.Windows.Forms.SizeType.Percent));
			this.editorRoot.OptionsTableLayoutGroup.RowDefinitions.Add(new DevExpress.XtraLayout.RowDefinition(this.editorRoot, 100D, System.Windows.Forms.SizeType.AutoSize));
			DevExpress.XtraLayout.LayoutControlItem nameItem = this.editorRoot.AddItem("Mod name", this.txtName);
			nameItem.OptionsTableLayoutItem.RowIndex = 0;
			DevExpress.XtraLayout.LayoutControlItem versionItem = this.editorRoot.AddItem("File version", this.txtVersion);
			versionItem.OptionsTableLayoutItem.RowIndex = 1;
			DevExpress.XtraLayout.LayoutControlItem authorItem = this.editorRoot.AddItem("Author", this.txtAuthor);
			authorItem.OptionsTableLayoutItem.RowIndex = 2;
			DevExpress.XtraLayout.LayoutControlItem websiteItem = this.editorRoot.AddItem("Nexus / website", this.txtWebsite);
			websiteItem.OptionsTableLayoutItem.RowIndex = 3;
			DevExpress.XtraLayout.LayoutControlItem modIdItem = this.editorRoot.AddItem("Nexus mod ID", this.txtModId);
			modIdItem.OptionsTableLayoutItem.RowIndex = 4;
			DevExpress.XtraLayout.LayoutControlItem fileIdItem = this.editorRoot.AddItem("Nexus file ID", this.txtFileId);
			fileIdItem.OptionsTableLayoutItem.RowIndex = 5;
			DevExpress.XtraLayout.LayoutControlItem descriptionItem = this.editorRoot.AddItem("Description", this.pnlDescription);
			descriptionItem.OptionsTableLayoutItem.RowIndex = 6;
			descriptionItem.TextLocation = DevExpress.Utils.Locations.Top;
			DevExpress.XtraLayout.LayoutControlItem screenshotItem = this.editorRoot.AddItem("Screenshot", this.picScreenshot);
			screenshotItem.OptionsTableLayoutItem.RowIndex = 7;
			screenshotItem.TextLocation = DevExpress.Utils.Locations.Top;
			DevExpress.XtraLayout.LayoutControlGroup actionGroup = this.editorRoot.AddGroup();
			actionGroup.OptionsTableLayoutItem.RowIndex = 8;
			actionGroup.GroupBordersVisible = false;
			actionGroup.LayoutMode = DevExpress.XtraLayout.Utils.LayoutMode.Table;
			actionGroup.OptionsTableLayoutGroup.ColumnDefinitions.Clear();
			actionGroup.OptionsTableLayoutGroup.RowDefinitions.Clear();
			actionGroup.OptionsTableLayoutGroup.ColumnDefinitions.Add(new DevExpress.XtraLayout.ColumnDefinition(actionGroup, 33.33D, System.Windows.Forms.SizeType.Percent));
			actionGroup.OptionsTableLayoutGroup.ColumnDefinitions.Add(new DevExpress.XtraLayout.ColumnDefinition(actionGroup, 33.33D, System.Windows.Forms.SizeType.Percent));
			actionGroup.OptionsTableLayoutGroup.ColumnDefinitions.Add(new DevExpress.XtraLayout.ColumnDefinition(actionGroup, 33.34D, System.Windows.Forms.SizeType.Percent));
			actionGroup.OptionsTableLayoutGroup.RowDefinitions.Add(new DevExpress.XtraLayout.RowDefinition(actionGroup, 100D, System.Windows.Forms.SizeType.AutoSize));
			DevExpress.XtraLayout.LayoutControlItem openItem = actionGroup.AddItem("", this.btnOpenWebsite);
			openItem.TextVisible = false;
			openItem.OptionsTableLayoutItem.ColumnIndex = 0;
			DevExpress.XtraLayout.LayoutControlItem setItem = actionGroup.AddItem("", this.btnSetScreenshot);
			setItem.TextVisible = false;
			setItem.OptionsTableLayoutItem.ColumnIndex = 1;
			DevExpress.XtraLayout.LayoutControlItem clearItem = actionGroup.AddItem("", this.btnClearScreenshot);
			clearItem.TextVisible = false;
			clearItem.OptionsTableLayoutItem.ColumnIndex = 2;
			//
			// buttonPanel
			//
			this.buttonPanel.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
			this.buttonPanel.Controls.Add(this.btnRestoreCurrent);
			this.buttonPanel.Controls.Add(this.btnOK);
			this.buttonPanel.Controls.Add(this.btnCancel);
			this.buttonPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.buttonPanel.Location = new System.Drawing.Point(0, 612);
			this.buttonPanel.Name = "buttonPanel";
			this.buttonPanel.Size = new System.Drawing.Size(1000, 48);
			this.buttonPanel.TabIndex = 1;
			this.buttonPanel.Resize += new System.EventHandler(this.buttonPanel_Resize);
			//
			// btnRestoreCurrent
			//
			this.btnRestoreCurrent.Location = new System.Drawing.Point(12, 10);
			this.btnRestoreCurrent.Name = "btnRestoreCurrent";
			this.btnRestoreCurrent.Size = new System.Drawing.Size(140, 28);
			this.btnRestoreCurrent.TabIndex = 0;
			this.btnRestoreCurrent.Text = "Restore current info";
			this.btnRestoreCurrent.Click += new System.EventHandler(this.btnRestoreCurrent_Click);
			//
			// btnOK
			//
			this.btnOK.Location = new System.Drawing.Point(802, 10);
			this.btnOK.Name = "btnOK";
			this.btnOK.Size = new System.Drawing.Size(90, 28);
			this.btnOK.TabIndex = 1;
			this.btnOK.Text = "Save";
			this.btnOK.Click += new System.EventHandler(this.btnOK_Click);
			//
			// btnCancel
			//
			this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this.btnCancel.Location = new System.Drawing.Point(898, 10);
			this.btnCancel.Name = "btnCancel";
			this.btnCancel.Size = new System.Drawing.Size(90, 28);
			this.btnCancel.TabIndex = 2;
			this.btnCancel.Text = "Cancel";
			//
			// errorProvider
			//
			this.errorProvider.ContainerControl = this;
			//
			// ModTaggerForm
			//
			this.AcceptButton = this.btnOK;
			this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.CancelButton = this.btnCancel;
			this.ClientSize = new System.Drawing.Size(1000, 660);
			this.Controls.Add(this.splitMain);
			this.Controls.Add(this.buttonPanel);
			this.m_fpdFontProvider.SetFontSet(this, "StandardText");
			this.MinimumSize = new System.Drawing.Size(840, 560);
			this.Name = "ModTaggerForm";
			this.ShowInTaskbar = false;
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
			this.Text = "Get Mod Info";
			((System.ComponentModel.ISupportInitialize)(this.splitMain.Panel1)).EndInit();
			this.splitMain.Panel1.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.splitMain.Panel2)).EndInit();
			this.splitMain.Panel2.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.splitMain)).EndInit();
			this.splitMain.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.grpCandidates)).EndInit();
			this.grpCandidates.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.grdCandidates)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.grvCandidates)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.editorLayout)).EndInit();
			this.editorLayout.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.txtName.Properties)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.txtVersion.Properties)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.txtAuthor.Properties)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.txtWebsite.Properties)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.txtModId.Properties)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.txtFileId.Properties)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.pnlDescription)).EndInit();
			this.pnlDescription.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.pnlDescriptionToolbar)).EndInit();
			this.pnlDescriptionToolbar.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.txtDescription.Properties)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.picScreenshot.Properties)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.editorRoot)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.buttonPanel)).EndInit();
			this.buttonPanel.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.errorProvider)).EndInit();
			this.ResumeLayout(false);
		}
	}
}
