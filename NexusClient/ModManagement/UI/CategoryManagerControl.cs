namespace Nexus.Client.ModManagement.UI
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Windows.Forms;

    using Nexus.Client.BackgroundTasks;
    using Nexus.Client.BackgroundTasks.UI;
    using Nexus.Client.Mods;
    using Nexus.Client.UI;
    using Nexus.Client.Util;

    /// <summary>
    /// A dock-content panel that shows the mod category list and exposes
    /// category management actions.  Shown as the "Categories" tab alongside
    /// the Mods and Plugins dock documents.
    /// </summary>
    public partial class CategoryManagerControl : ManagedFontDockContent
    {
        private ModManagerVM _viewModel;

        // ── Events ───────────────────────────────────────────────────────────────

        /// <summary>Raised when the user clicks Collapse All; MainForm routes this to ModManagerDXControl.</summary>
        public event EventHandler CollapseAllCategoriesRequested;

        /// <summary>Raised when the user clicks Expand All; MainForm routes this to ModManagerDXControl.</summary>
        public event EventHandler ExpandAllCategoriesRequested;

        // ── Constructor ───────────────────────────────────────────────────────────

        public CategoryManagerControl()
        {
            InitializeComponent();
            Text         = "Categories";
            HideOnClose  = true;
        }

        internal void ApplyDisplaySettings(DevExpressDisplaySettings settings)
        {
            if (settings == null) return;

            DevExpressDisplaySettingsApplier.ApplyToControlTree(this, settings);
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
                }
            }
        }

        private void HookViewModel()
        {
            _viewModel.UpdatingCategory   += VM_UpdatingCategory;
            _viewModel.UpdatingCategories += VM_UpdatingCategories;
            if (_viewModel.CategoryManager?.Categories != null)
                _viewModel.CategoryManager.Categories.CollectionChanged += Categories_CollectionChanged;
        }

        private void UnhookViewModel()
        {
            _viewModel.UpdatingCategory   -= VM_UpdatingCategory;
            _viewModel.UpdatingCategories -= VM_UpdatingCategories;
            if (_viewModel.CategoryManager?.Categories != null)
                _viewModel.CategoryManager.Categories.CollectionChanged -= Categories_CollectionChanged;
        }

        // ── Refresh ───────────────────────────────────────────────────────────────

        /// <summary>Reloads the category grid. Safe to call from a background thread.</summary>
        public void RefreshCategoryList()
        {
            if (InvokeRequired) { Invoke((MethodInvoker)RefreshCategoryList); return; }
            if (_viewModel?.CategoryManager == null) return;

            gridControl.DataSource = null;
            gridControl.DataSource = _viewModel.CategoryManager.Categories;
            gridView.RefreshData();
        }

        // ── Collection changed ────────────────────────────────────────────────────

        private void Categories_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RefreshCategoryList();
        }

        // ── Toolbar action handlers ───────────────────────────────────────────────

        private void tsbAddCategory_Click(object sender, EventArgs e)
        {
            if (_viewModel == null) return;
            _viewModel.CategoryManager.AddCategory();
        }

        private void tsbRenameCategory_Click(object sender, EventArgs e)
        {
            IModCategory selected = GetSelectedCategory();
            if (selected == null || _viewModel == null) return;

            string newName = ShowInputDialog("Rename Category", "Enter new name:", selected.CategoryName);
            if (string.IsNullOrWhiteSpace(newName) || newName == selected.CategoryName) return;

            _viewModel.CategoryManager.RenameCategory(selected.Id, newName);
            RefreshCategoryList();
        }

        private void tsbRemoveCategory_Click(object sender, EventArgs e)
        {
            IModCategory selected = GetSelectedCategory();
            if (selected == null || _viewModel == null) return;

            if (selected.Id == 0)
            {
                MessageBox.Show("The Unassigned category cannot be removed.",
                    "Remove Category", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (MessageBox.Show(
                    $"Remove category \"{selected.CategoryName}\"?\nMods in this category will be moved to Unassigned.",
                    "Remove Category", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

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

        private void tsbUpdateFromNexus_Click(object sender, EventArgs e)
        {
            if (_viewModel == null) return;
            try
            {
                _viewModel.CheckCategoriesUpdates();
            }
            catch (Exception ex)
            {
                if (ex.Message != "Login required")
                    MessageBox.Show(
                        $"Couldn't perform the update check, retry later.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                        "Update check", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void tsbResetUnassigned_Click(object sender, EventArgs e)
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

        private void tsbResetAllToUnassigned_Click(object sender, EventArgs e)
        {
            if (_viewModel == null) return;
            _viewModel.ResetToUnassigned();
        }

        private void tsbRemoveAllCategories_Click(object sender, EventArgs e)
        {
            if (_viewModel == null) return;
            _viewModel.RemoveAllCategories();
        }

        // ── Grid inline rename ────────────────────────────────────────────────────

        private void gridView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.F2) return;
            e.Handled = true;
            tsbRenameCategory_Click(sender, EventArgs.Empty);
        }



        private void VM_UpdatingCategory(object sender, EventArgs<IBackgroundTask> e)
        {
            if (InvokeRequired) { Invoke((Action<object, EventArgs<IBackgroundTask>>)VM_UpdatingCategory, sender, e); return; }
            ProgressDialog.ShowDialog(this, e.Argument);
            RefreshCategoryList();
        }

        private void VM_UpdatingCategories(object sender, EventArgs<IBackgroundTask> e)
        {
            if (InvokeRequired) { Invoke((Action<object, EventArgs<IBackgroundTask>>)VM_UpdatingCategories, sender, e); return; }
            ProgressDialog.ShowDialog(this, e.Argument);
            RefreshCategoryList();
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

        private static string ShowInputDialog(string title, string prompt, string defaultValue)
        {
            using (Form form = new Form())
            {
                form.Text            = title;
                form.Size            = new System.Drawing.Size(380, 130);
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition   = FormStartPosition.CenterParent;
                form.MaximizeBox     = false;
                form.MinimizeBox     = false;

                Label   label     = new Label   { Text = prompt,        Left = 10, Top = 12, Width = 340 };
                TextBox textBox   = new TextBox { Text = defaultValue,  Left = 10, Top = 32, Width = 340 };
                Button  btnOk     = new Button  { Text = "OK",     Left = 195, Top = 60, Width = 75, DialogResult = DialogResult.OK };
                Button  btnCancel = new Button  { Text = "Cancel", Left = 275, Top = 60, Width = 75, DialogResult = DialogResult.Cancel };

                form.Controls.AddRange(new Control[] { label, textBox, btnOk, btnCancel });
                form.AcceptButton = btnOk;
                form.CancelButton = btnCancel;

                return form.ShowDialog() == DialogResult.OK ? textBox.Text.Trim() : null;
            }
        }
    }
}
