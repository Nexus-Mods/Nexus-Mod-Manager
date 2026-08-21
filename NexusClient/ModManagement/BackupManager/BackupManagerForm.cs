using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using Nexus.Client.UI;

namespace Nexus.Client.ModManagement.UI
{
	/// <summary>
	/// A view that allows selecting the categories included in an NMM backup.
	/// </summary>
	public partial class BackupManagerForm : ManagedFontXtraForm
	{
		private readonly BindingList<BackupCategoryRow> _rows = new BindingList<BackupCategoryRow>();

		/// <summary>
		/// Gets the backup manager used to inspect and back up mod installations.
		/// </summary>
		private BackupManager BackupManager { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="BackupManagerForm"/> class.
		/// </summary>
		/// <param name="p_bmBackupManager">The backup manager that supplies the backup data.</param>
		public BackupManagerForm(BackupManager p_bmBackupManager)
		{
			InitializeComponent();
			NmmIconProvider.Bind(btBackup, NmmIconAction.Backup);
			NmmIconProvider.Bind(btCancel, NmmIconAction.Cancel);
			BackupManager = p_bmBackupManager;

			_rows.Add(new BackupCategoryRow(0, "Base game Files"));
			_rows.Add(new BackupCategoryRow(1, "Installed mod Files"));
			_rows.Add(new BackupCategoryRow(2, "Files not managed by NMM"));
			_rows.Add(new BackupCategoryRow(3, "Mod Archives"));
			gridControl.DataSource = _rows;
			FormClosed += BackupManagerForm_FormClosed;
		}

		private void btBackup_Click(object sender, EventArgs e)
		{
			int totalFiles = 0;
			bool anySelected = false;
			foreach (BackupCategoryRow row in _rows)
			{
				if (!row.Selected)
					continue;
				anySelected = true;
				int count;
				if (int.TryParse(row.TotalFiles, out count))
					totalFiles += count;
			}

			if (!anySelected)
			{
				XtraMessageBox.Show(this, "You have to select at least one category!", "Create Backup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			if (totalFiles <= 0)
			{
				XtraMessageBox.Show(this, "You cannot backup ZERO files!", "Create Backup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			DialogResult = DialogResult.OK;
		}

		private void btCancel_Click(object sender, EventArgs e)
		{
			DialogResult = DialogResult.Cancel;
		}

		private void gridView_CellValueChanged(object sender, DevExpress.XtraGrid.Views.Base.CellValueChangedEventArgs e)
		{
			if (e.Column.FieldName != nameof(BackupCategoryRow.Selected))
				return;

			BackupCategoryRow row = gridView.GetRow(e.RowHandle) as BackupCategoryRow;
			if (row == null)
				return;

			CheckCategory(row.Index, row.Selected ? CheckState.Checked : CheckState.Unchecked);
		}

		private void gridView_ShownEditor(object sender, EventArgs e)
		{
			if (gridView.FocusedColumn != null && gridView.FocusedColumn.FieldName == nameof(BackupCategoryRow.Selected))
				gridView.PostEditor();
		}

		private void CheckCategory(int p_intIndex, CheckState p_chkState)
		{
			BackupCategoryRow row = _rows[p_intIndex];
			if (p_chkState == CheckState.Unchecked)
			{
				row.SizeMb = "-";
				row.TotalFiles = "-";
				row.EstimatedBackupSize = "-";
				row.EstimatedBackupTime = "-";
				BackupManager.checkList.Remove(p_intIndex);
				gridView.RefreshData();
				return;
			}

			if (!BackupManager.checkList.Contains(p_intIndex))
				BackupManager.checkList.Add(p_intIndex);

			string estimatedCompression = "-";
			string estimatedTime = "-";

			if (p_intIndex == 0)
			{
				if (BackupManager.lstBaseGameFiles.Count == 0)
					BackupManager.CheckBaseGameFiles();
				SetEstimate(row, BackupManager.BaseGameFilesSize, BackupManager.lstBaseGameFiles.Count, out estimatedCompression, out estimatedTime);
				BackupManager.strBaseGameFilesSize = row.SizeMb;
			}
			else if (p_intIndex == 1)
			{
				if (BackupManager.lstInstalledModFiles.Count == 0)
					BackupManager.CheckModsInstallationFiles();
				long size = BackupManager.InstalledModFileSize + BackupManager.InstalledNMMLINKFileSize;
				SetEstimate(row, size, BackupManager.lstInstalledModFiles.Count + BackupManager.lstInstalledNMMLINKFiles.Count, out estimatedCompression, out estimatedTime);
				BackupManager.strInstalledModFileSize = row.SizeMb;
			}
			else if (p_intIndex == 2)
			{
				if (BackupManager.lstLooseFiles.Count == 0)
					BackupManager.CheckLooseFiles(false);
				SetEstimate(row, BackupManager.LooseFilesSize, BackupManager.lstLooseFiles.Count, out estimatedCompression, out estimatedTime);
				BackupManager.strLooseFilesSize = row.SizeMb;
			}
			else if (p_intIndex == 3)
			{
				if (BackupManager.lstModArchives.Count == 0)
					BackupManager.CheckModArchives();
				SetEstimate(row, BackupManager.ModArchivesSize, BackupManager.lstModArchives.Count, out estimatedCompression, out estimatedTime);
				BackupManager.strModArchivesSize = row.SizeMb;
			}

			row.EstimatedBackupSize = estimatedCompression;
			row.EstimatedBackupTime = estimatedTime;
			gridView.RefreshData();
		}

		/// <summary>
		/// Populates a backup row with size, file-count, and rough compression/time estimates.
		/// </summary>
		private static void SetEstimate(BackupCategoryRow row, long sizeBytes, int fileCount, out string estimatedCompression, out string estimatedTime)
		{
			float sizeMb = (sizeBytes / 1024f) / 1024f;
			row.SizeMb = sizeBytes == 0 ? "-" : sizeMb.ToString("0");
			row.TotalFiles = fileCount.ToString();
			estimatedCompression = sizeMb > 0 ? (sizeMb - (sizeMb / 10)).ToString("0") : "-";
			float minutes = sizeMb > 0 ? ((30 * sizeMb) / 60) / 1024f : 0;
			estimatedTime = minutes > 0 ? (minutes > 1 ? Math.Round(minutes, 0) + " minutes" : "Less than a minute") : "-";
		}

		private void BackupManagerForm_FormClosed(object sender, FormClosedEventArgs e)
		{
			BackupManager.TotalFileSize = BackupManager.InstalledModFileSize + BackupManager.BaseGameFilesSize + BackupManager.LooseFilesSize + BackupManager.ModArchivesSize;
			BackupManager.InstalledModFileSize = 0;
			BackupManager.BaseGameFilesSize = 0;
			BackupManager.LooseFilesSize = 0;
			BackupManager.ModArchivesSize = 0;
		}

		/// <summary>
		/// Represents one selectable category in the backup grid.
		/// </summary>
		private sealed class BackupCategoryRow
		{
			/// <summary>
			/// Initializes a new backup category row.
			/// </summary>
			public BackupCategoryRow(int index, string category)
			{
				Index = index;
				Category = category;
			}

			public int Index { get; private set; }
			public bool Selected { get; set; }
			public string Category { get; private set; }
			public string SizeMb { get; set; } = "-";
			public string TotalFiles { get; set; } = "-";
			public string EstimatedBackupSize { get; set; } = "-";
			public string EstimatedBackupTime { get; set; } = "-";
		}
	}
}
