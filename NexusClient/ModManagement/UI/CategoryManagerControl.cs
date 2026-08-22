namespace Nexus.Client.ModManagement.UI
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Text;
    using System.Windows.Forms;

    using DevExpress.XtraBars;
    using DevExpress.XtraEditors;

    using Nexus.Client.BackgroundTasks;
    using Nexus.Client.BackgroundTasks.UI;
    using Nexus.Client.Mods;
    using Nexus.Client.UI;
    using Nexus.Client.Util;
    using Nexus.Client.Util.Localization;

    /// <summary>
    /// A dock-content panel that shows the mod category list and exposes
    /// category management actions.  Shown as the "Categories" tab alongside
    /// the Mods and Plugins dock documents.
    /// </summary>
    public partial class CategoryManagerControl : ManagedFontDockContent
    {
        private const string GridLayoutKey = "categoryManagerGrid";
        private const string GridColumnWidthsKey = GridLayoutKey + ".ColumnWidths";
        private const int GridLayoutSaveDelayMs = 400;

        private ModManagerVM _viewModel;
        private DevExpressDisplaySettings _displaySettings;
        private bool _restoringGridLayout;
        private readonly Timer _gridLayoutSaveTimer;

        // ── Events ───────────────────────────────────────────────────────────────

        /// <summary>Raised when the user clicks Collapse All; MainForm routes this to ModManagerDXControl.</summary>
        public event EventHandler CollapseAllCategoriesRequested;

        /// <summary>Raised when the user clicks Expand All; MainForm routes this to ModManagerDXControl.</summary>
        public event EventHandler ExpandAllCategoriesRequested;

        // ── Constructor ───────────────────────────────────────────────────────────

        public CategoryManagerControl()
        {
            InitializeComponent();
            ApplyLocalization();
            NmmIconProvider.Bind(tsbAddCategory, NmmIconAction.Add);
            NmmIconProvider.Bind(tsbRenameCategory, NmmIconAction.Rename);
            NmmIconProvider.Bind(tsbRemoveCategory, NmmIconAction.Delete);
			NmmIconProvider.Bind(tsbUpdateFromNexus, NmmIconAction.UpdateResetCategories);
			NmmIconProvider.Bind(tsbResetUnassigned, NmmIconAction.ResetUnassigned);
			NmmIconProvider.Bind(tsbResetAllToUnassigned, NmmIconAction.ResetAll);
            NmmIconProvider.Bind(tsbRemoveAllCategories, NmmIconAction.RemoveAll);
			NmmIconProvider.BindBar(barCategoryActions, NmmButtonPresentationScope.Categories, true);
            DevExpressDisplaySettingsApplier.NormalizeBarItemImages(barManagerCategory, new System.Drawing.Size(32, 32));
            Text        = LanguageManager.Get("Categories.Title", "Categories");
            HideOnClose = true;

            _gridLayoutSaveTimer = new Timer
            {
                Interval = GridLayoutSaveDelayMs
            };
            _gridLayoutSaveTimer.Tick += GridLayoutSaveTimer_Tick;
            DevExpressGridLayoutPersistence.ConfigureSessionOnlyFilters(gridView);

            gridView.ColumnWidthChanged +=
                (sender, args) => QueueGridLayoutSave();
            gridView.ColumnPositionChanged +=
                (sender, args) => QueueGridLayoutSave();
            gridView.EndSorting +=
                (sender, args) => QueueGridLayoutSave();
        }

        private void ApplyLocalization()
        {
            barCategoryActions.Text = LanguageManager.Get("Categories.Toolbar.Title", "Category Actions");
            tsbAddCategory.Caption = LanguageManager.Get("Categories.Actions.Add.Name", "Add Category");
            tsbAddCategory.Hint = LanguageManager.Get("Categories.Actions.Add.Tooltip", "Add a new category");
            tsbRenameCategory.Caption = LanguageManager.Get("Categories.Actions.Rename.Name", "Rename Category");
            tsbRenameCategory.Hint = LanguageManager.Get("Categories.Actions.Rename.Tooltip", "Rename the selected category (F2)");
            tsbRemoveCategory.Caption = LanguageManager.Get("Categories.Actions.Remove.Name", "Remove Category");
            tsbRemoveCategory.Hint = LanguageManager.Get("Categories.Actions.Remove.Tooltip", "Remove the selected category");
            tsbUpdateFromNexus.Caption = LanguageManager.Get("Categories.Actions.UpdateFromNexus.Name", "Update from Nexus");
            tsbUpdateFromNexus.Hint = LanguageManager.Get("Categories.Actions.UpdateFromNexus.Tooltip", "Update and reset categories to Nexus site defaults");
            tsbResetUnassigned.Caption = LanguageManager.Get("Categories.Actions.ResetUnassigned.Name", "Reset Unassigned to Nexus Defaults");
            tsbResetUnassigned.Hint = LanguageManager.Get("Categories.Actions.ResetUnassigned.Tooltip", "Reset unassigned mods to Nexus site default categories");
            tsbResetAllToUnassigned.Caption = LanguageManager.Get("Categories.Actions.ResetAll.Name", "Reset All Mods to Unassigned");
            tsbResetAllToUnassigned.Hint = LanguageManager.Get("Categories.Actions.ResetAll.Tooltip", "Reset all mods to the Unassigned category");
            tsbRemoveAllCategories.Caption = LanguageManager.Get("Categories.Actions.RemoveAll.Name", "Remove All Categories");
            tsbRemoveAllCategories.Hint = LanguageManager.Get("Categories.Actions.RemoveAll.Tooltip", "Remove all categories and reset all mods to Unassigned");

            DevExpress.XtraGrid.Columns.GridColumn idColumn = gridView.Columns["Id"];
            if (idColumn != null) idColumn.Caption = LanguageManager.Get("Categories.Columns.Id.Header", "ID");
            DevExpress.XtraGrid.Columns.GridColumn nameColumn = gridView.Columns["CategoryName"];
            if (nameColumn != null) nameColumn.Caption = LanguageManager.Get("Categories.Columns.Name.Header", "Category Name");
        }

        internal void ApplyDisplaySettings(DevExpressDisplaySettings settings)
        {
            if (settings == null) return;

            _displaySettings = settings;
            DevExpressDisplaySettingsApplier.ApplyToControlTree(this, settings);
            DevExpressDisplaySettingsApplier.ApplyToBarManager(barManagerCategory, settings);
            gridControl.Invalidate();
        }

        // ── ViewModel ─────────────────────────────────────────────────────────────

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ModManagerVM ViewModel
        {
            get => _viewModel;
            set
            {
                if (_viewModel != null)
                    UnhookViewModel();

                _viewModel = value;

                if (_viewModel != null)
                {
                    HookViewModel();
                    RefreshCategoryList();
                    RestoreGridLayout();
                }
            }
        }

        private void HookViewModel()
        {
            _viewModel.UpdatingCategory   += VM_UpdatingCategory;
            _viewModel.UpdatingCategories += VM_UpdatingCategories;
            if (_viewModel.CategoryManager != null)
                _viewModel.CategoryManager.CategoriesChanged += CategoryManager_CategoriesChanged;
        }

        private void UnhookViewModel()
        {
            _viewModel.UpdatingCategory   -= VM_UpdatingCategory;
            _viewModel.UpdatingCategories -= VM_UpdatingCategories;
            if (_viewModel.CategoryManager != null)
                _viewModel.CategoryManager.CategoriesChanged -= CategoryManager_CategoriesChanged;
        }

        // ── Refresh ───────────────────────────────────────────────────────────────

        /// <summary>Reloads the category grid. Safe to call from a background thread.</summary>
        public void RefreshCategoryList()
        {
            if (InvokeRequired)
            {
                Invoke((MethodInvoker)RefreshCategoryList);
                return;
            }

            if (_viewModel?.CategoryManager == null)
                return;

            gridControl.DataSource = null;
            gridControl.DataSource = _viewModel.CategoryManager.Categories;
            gridView.RefreshData();
        }

        // ── Category changes ─────────────────────────────────────────────────────

        /// <summary>
        /// Refreshes the category grid after category definitions change.
        /// </summary>
        private void CategoryManager_CategoriesChanged(object sender, EventArgs e)
        {
            RefreshCategoryList();
        }

        // ── Toolbar action handlers ───────────────────────────────────────────────

        private void tsbAddCategory_Click(object sender, ItemClickEventArgs e)
        {
            if (_viewModel == null) return;
            _viewModel.CategoryManager.AddCategory();
        }

        private void tsbRenameCategory_Click(object sender, ItemClickEventArgs e)
        {
            IModCategory selected = GetSelectedCategory();
            if (selected == null || _viewModel == null) return;

            string newName = ShowInputDialog(this, LanguageManager.Get("Categories.Rename.Title", "Rename Category"), LanguageManager.Get("Categories.Rename.Prompt", "Enter new name:"), selected.CategoryName);
            if (string.IsNullOrWhiteSpace(newName) || newName == selected.CategoryName) return;

            _viewModel.CategoryManager.RenameCategory(selected.Id, newName);
            RefreshCategoryList();
        }

        private void tsbRemoveCategory_Click(object sender, ItemClickEventArgs e)
        {
            IModCategory selected = GetSelectedCategory();
            if (selected == null || _viewModel == null) return;

            if (selected.Id == 0)
            {
                XtraMessageBox.Show(
                    this,
                    LanguageManager.Get("Categories.Remove.UnassignedBlocked.Message", "The Unassigned category cannot be removed."),
                    LanguageManager.Get("Categories.Actions.Remove.Name", "Remove Category"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (XtraMessageBox.Show(
                    this,
                    LanguageManager.Format("Categories.Remove.Confirm.Message", "Remove category \"{0}\"?\nMods in this category will be moved to Unassigned.", selected.CategoryName),
                    LanguageManager.Get("Categories.Actions.Remove.Name", "Remove Category"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            _viewModel.SwitchModsToUnassigned(selected);
            _viewModel.CategoryManager.RemoveCategory(selected);
        }

        private void tsbCollapseAll_Click(object sender, EventArgs e)
        {
            CollapseAllCategoriesRequested?.Invoke(this, EventArgs.Empty);
        }

        private void tsbExpandAll_Click(object sender, EventArgs e)
        {
            ExpandAllCategoriesRequested?.Invoke(this, EventArgs.Empty);
        }

        private void tsbUpdateFromNexus_Click(object sender, ItemClickEventArgs e)
        {
            if (_viewModel == null) return;

            try
            {
                _viewModel.CheckCategoriesUpdates();
            }
            catch (Exception ex)
            {
                if (ex.Message != "Login required")
                {
                    XtraMessageBox.Show(
                        this,
                        LanguageManager.Get("Categories.UpdateCheck.Failed.Message", "Couldn't perform the update check, retry later.") + Environment.NewLine + Environment.NewLine + ex.Message,
                        LanguageManager.Get("Categories.UpdateCheck.Title", "Update check"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
        }

        private void tsbResetUnassigned_Click(object sender, ItemClickEventArgs e)
        {
            if (_viewModel == null) return;

            List<IMod> mods = new List<IMod>();
            foreach (IMod mod in _viewModel.ManagedMods)
            {
                if (mod.CategoryId > 0 && mod.CustomCategoryId == 0)
                    mods.Add(mod);
            }

            if (mods.Count > 0)
                _viewModel.SwitchModsToCategory(mods, -1);

            _viewModel.CheckForUpdates(true);
        }

        private void tsbResetAllToUnassigned_Click(object sender, ItemClickEventArgs e)
        {
            if (_viewModel == null) return;
            _viewModel.ResetToUnassigned();
        }

        private void tsbRemoveAllCategories_Click(object sender, ItemClickEventArgs e)
        {
            if (_viewModel == null) return;
            _viewModel.RemoveAllCategories();
        }

        // ── Grid inline rename ────────────────────────────────────────────────────

        private void gridView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.F2) return;

            e.Handled = true;
            tsbRenameCategory_Click(sender, null);
        }

        private void VM_UpdatingCategory(object sender, EventArgs<IBackgroundTask> e)
        {
            if (InvokeRequired)
            {
                Invoke(
                    (Action<object, EventArgs<IBackgroundTask>>)VM_UpdatingCategory,
                    sender,
                    e);
                return;
            }

            ProgressDialog.ShowDialog(this, e.Argument);
            RefreshCategoryList();
        }

        private void VM_UpdatingCategories(object sender, EventArgs<IBackgroundTask> e)
        {
            if (InvokeRequired)
            {
                Invoke(
                    (Action<object, EventArgs<IBackgroundTask>>)VM_UpdatingCategories,
                    sender,
                    e);
                return;
            }

            ProgressDialog.ShowDialog(this, e.Argument);
            RefreshCategoryList();
        }

        // ── Grid persistence ──────────────────────────────────────────────────────

        private void QueueGridLayoutSave()
        {
            if (_restoringGridLayout ||
                _viewModel?.Settings == null ||
                _gridLayoutSaveTimer == null ||
                IsDisposed)
            {
                return;
            }

            _gridLayoutSaveTimer.Stop();
            _gridLayoutSaveTimer.Start();
        }

        private void GridLayoutSaveTimer_Tick(object sender, EventArgs e)
        {
            _gridLayoutSaveTimer.Stop();
            SaveGridLayout();
        }

        private void RestoreGridLayout()
        {
            if (_viewModel?.Settings == null)
                return;

            _restoringGridLayout = true;
            try
            {
                try
                {
                    if (_viewModel.Settings.DockPanelLayouts.ContainsKey(GridLayoutKey))
                    {
                        string layout = _viewModel.Settings.DockPanelLayouts[GridLayoutKey];
                        if (!string.IsNullOrWhiteSpace(layout))
                        {
                            byte[] bytes = Encoding.UTF8.GetBytes(layout);
                            using (MemoryStream stream = new MemoryStream(bytes))
                            {
                                gridView.RestoreLayoutFromStream(stream);
                            }
                        }
                    }
                }
                catch
                {
                    _viewModel.Settings.DockPanelLayouts.Remove(GridLayoutKey);
                }

                DevExpressGridLayoutPersistence.ClearTransientFilters(gridView);
                if (_viewModel.Settings.DockPanelLayouts.ContainsKey(GridColumnWidthsKey))
                    DevExpressGridLayoutPersistence.RestoreColumnWidths(gridView, _viewModel.Settings.DockPanelLayouts[GridColumnWidthsKey]);
            }
            finally
            {
                _restoringGridLayout = false;
            }
        }

        private void SaveGridLayout()
        {
            if (_restoringGridLayout || _viewModel?.Settings == null)
                return;

            _gridLayoutSaveTimer?.Stop();

            try
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    gridView.SaveLayoutToStream(stream);
                    _viewModel.Settings.DockPanelLayouts[GridLayoutKey] =
                        Encoding.UTF8.GetString(stream.ToArray());
                }

                _viewModel.Settings.DockPanelLayouts[GridColumnWidthsKey] =
                    DevExpressGridLayoutPersistence.SerializeColumnWidths(gridView);
            }
            catch
            {
                _viewModel.Settings.DockPanelLayouts.Remove(GridLayoutKey);
                _viewModel.Settings.DockPanelLayouts.Remove(GridColumnWidthsKey);
            }

            _viewModel.Settings.Save();
        }

        protected override void OnClosed(EventArgs e)
        {
            _gridLayoutSaveTimer?.Stop();
            SaveGridLayout();

            if (_gridLayoutSaveTimer != null)
            {
                _gridLayoutSaveTimer.Tick -= GridLayoutSaveTimer_Tick;
                _gridLayoutSaveTimer.Dispose();
            }

            base.OnClosed(e);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private IModCategory GetSelectedCategory()
        {
            return GetCategoryAtRow(gridView.FocusedRowHandle);
        }

        private IModCategory GetCategoryAtRow(int rowHandle)
        {
            if (rowHandle < 0) return null;
            return gridView.GetRow(rowHandle) as IModCategory;
        }

        /// <summary>
        /// Displays a skin-aware DevExpress text prompt and returns the trimmed value when accepted.
        /// </summary>
        /// <param name="owner">Window that owns the prompt.</param>
        /// <param name="title">Dialog title.</param>
        /// <param name="prompt">Prompt displayed above the editor.</param>
        /// <param name="defaultValue">Initial editor value.</param>
        /// <returns>The entered text, or <c>null</c> when the dialog is cancelled.</returns>
        private string ShowInputDialog(IWin32Window owner, string title, string prompt, string defaultValue)
        {
            using (ManagedFontXtraForm form = new ManagedFontXtraForm())
            using (LabelControl label = new LabelControl())
            using (TextEdit textBox = new TextEdit())
            using (SimpleButton btnOk = new SimpleButton())
            using (SimpleButton btnCancel = new SimpleButton())
            {
                form.Text = title;
                form.ClientSize = new System.Drawing.Size(380, 112);
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.ShowInTaskbar = false;

                label.Text = prompt;
                label.Location = new System.Drawing.Point(12, 12);

                textBox.Text = defaultValue ?? string.Empty;
                textBox.Location = new System.Drawing.Point(12, 34);
                textBox.Size = new System.Drawing.Size(356, 20);
                textBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

                btnOk.Text = LanguageManager.Get("Common.Action.Ok", "OK");
                btnOk.Location = new System.Drawing.Point(212, 70);
                btnOk.Size = new System.Drawing.Size(75, 26);
                btnOk.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                btnOk.DialogResult = DialogResult.OK;
                NmmIconProvider.Bind(btnOk, NmmIconAction.Apply);

                btnCancel.Text = LanguageManager.Get("Common.Action.Cancel", "Cancel");
                btnCancel.Location = new System.Drawing.Point(293, 70);
                btnCancel.Size = new System.Drawing.Size(75, 26);
                btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                btnCancel.DialogResult = DialogResult.Cancel;
                NmmIconProvider.Bind(btnCancel, NmmIconAction.Cancel);

                form.Controls.Add(label);
                form.Controls.Add(textBox);
                form.Controls.Add(btnOk);
                form.Controls.Add(btnCancel);
                form.AcceptButton = btnOk;
                form.CancelButton = btnCancel;

                if (_displaySettings != null)
                    DevExpressDisplaySettingsApplier.ApplyToControlTree(form, _displaySettings);

                form.Shown += (sender, args) =>
                {
                    textBox.Focus();
                    textBox.SelectAll();
                };

                return form.ShowDialog(owner) == DialogResult.OK
                    ? textBox.Text.Trim()
                    : null;
            }
        }
    }
}
