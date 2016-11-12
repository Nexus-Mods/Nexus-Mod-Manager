using System;
using System.Windows.Forms;
using Nexus.Client.UI;

namespace Nexus.Client.ModManagement.UI
{
	/// <summary>
	/// A view that allows editing of mod tags.
	/// </summary>
	public partial class BackupManagerForm : ManagedFontForm
	{
		#region Properties
		/// <summary>
		/// Gets the backup manager to use to backup mod installations.
		/// </summary>
		/// <value>The backup manager to use to backup mod installations.</value>
		private BackupManager BackupManager { get; }
		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_bmBackupManager">The view model that provides the data and operations for this view.</param>
		public BackupManagerForm(BackupManager p_bmBackupManager)
		{
			InitializeComponent();

			BackupManager = p_bmBackupManager;

			lstView.Columns.Add("Category", 160);
			lstView.Columns.Add("Size (MB)", 60);
			lstView.Columns.Add("Total Files", 60);
			lstView.Columns.Add("Est. Backup Size", 120);
			lstView.Columns.Add("Est. Backup Time", 120);

			lstView.Items.Add(new ListViewItem(new string[] { "Base game Files", "-", "-", "-", "-" }));
			lstView.Items.Add(new ListViewItem(new string[] { "Installed mod Files", "-", "-", "-", "-" }));
			lstView.Items.Add(new ListViewItem(new string[] { "Files not managed by NMM", "-", "-", "-", "-" }));
			lstView.Items.Add(new ListViewItem(new string[] { "Mod Archives", "-", "-", "-", "-" }));

			this.FormClosed += new FormClosedEventHandler(BackupManagerForm_FormClosed);
		}

		private void btBackup_Click(object sender, System.EventArgs e)
		{
			if (this.lstView.CheckedItems.Count > 0)
			{
				int TotalFiles = 0;
				foreach (ListViewItem item in lstView.CheckedItems)
				{
					TotalFiles = TotalFiles + int.Parse(item.SubItems[2].Text);
				}
				if(TotalFiles > 0)
					DialogResult = DialogResult.OK;
				else
					MessageBox.Show("You cannot backup ZERO files!", "Create Backup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
			else
			{
				MessageBox.Show("You have to select at least one category!", "Create Backup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
		}

		private void btCancel_Click(object sender, System.EventArgs e)
		{
			DialogResult = DialogResult.Cancel;
		}
		
		private void ListViewItem_ItemCheck(object sender, ItemCheckEventArgs e)
		{
			CheckCategory(e.Index, e.NewValue);
		}

		#endregion

		private void CheckCategory(int p_intIndex, CheckState p_chkState)
		{
			if (p_chkState == CheckState.Unchecked)
			{
				lstView.Items[p_intIndex].SubItems[1].Text = "-";
				lstView.Items[p_intIndex].SubItems[2].Text = "-";
				lstView.Items[p_intIndex].SubItems[3].Text = "-";
				lstView.Items[p_intIndex].SubItems[4].Text = "-";
				BackupManager.checkList.Remove(p_intIndex);
			}
			else
			{
				string strEstBaseCompression = string.Empty;
				string strEstBaseTime = string.Empty;

				BackupManager.checkList.Add(p_intIndex);

				if (p_intIndex == 0)
				{
					if (BackupManager.lstBaseGameFiles.Count == 0)
						BackupManager.CheckBaseGameFiles();

					float BaseGameFileSize = ((BackupManager.BaseGameFilesSize / 1024f) / 1024f);

					if (BackupManager.BaseGameFilesSize != 0)
					{
						BackupManager.strBaseGameFilesSize = BaseGameFileSize.ToString("0");
						strEstBaseCompression = BaseGameFileSize > 0 ? (BaseGameFileSize - (BaseGameFileSize / 10)).ToString("0") : "-";
						float EstBaseTime = BaseGameFileSize > 0 ? ((30 * BaseGameFileSize) / 60) / 1024f : 0;
						strEstBaseTime = EstBaseTime > 0 ? (EstBaseTime > 1 ? Math.Round(EstBaseTime, 0).ToString() + " minutes" : "Less than a minute") : "-";
					}
					else
					{
						BackupManager.strBaseGameFilesSize = "-";
						strEstBaseCompression = "-";
						strEstBaseTime = "-";
					}
					
					lstView.Items[p_intIndex].SubItems[1].Text = BackupManager.strBaseGameFilesSize;
					lstView.Items[p_intIndex].SubItems[2].Text = BackupManager.lstBaseGameFiles.Count.ToString();
					lstView.Items[p_intIndex].SubItems[3].Text = strEstBaseCompression;
					lstView.Items[p_intIndex].SubItems[4].Text = strEstBaseTime;
				}

				if (p_intIndex == 1)
				{
					if (BackupManager.lstInstalledModFiles.Count == 0)
						BackupManager.CheckModsInstallationFiles();

					float InstalledModFileSize = (((BackupManager.InstalledModFileSize + BackupManager.InstalledNMMLINKFileSize) / 1024f) / 1024f);

					if (BackupManager.InstalledModFileSize != 0)
					{
						BackupManager.strInstalledModFileSize = InstalledModFileSize.ToString("0");
						strEstBaseCompression = InstalledModFileSize > 0 ? (InstalledModFileSize - (InstalledModFileSize / 10)).ToString("0") : "-";
						float EstBaseTime = InstalledModFileSize > 0 ? ((30 * InstalledModFileSize) / 60) / 1024f : 0;
						strEstBaseTime = EstBaseTime > 0 ? (EstBaseTime > 1 ? Math.Round(EstBaseTime, 0).ToString() + " minutes" : "Less than a minute") : "-";
					}
					else
					{
						BackupManager.strInstalledModFileSize = "-";
						strEstBaseCompression = "-";
						strEstBaseTime = "-";
					}
					

					lstView.Items[p_intIndex].SubItems[1].Text = BackupManager.strInstalledModFileSize;
					lstView.Items[p_intIndex].SubItems[2].Text = (BackupManager.lstInstalledModFiles.Count + BackupManager.lstInstalledNMMLINKFiles.Count).ToString();
					lstView.Items[p_intIndex].SubItems[3].Text = strEstBaseCompression;
					lstView.Items[p_intIndex].SubItems[4].Text = strEstBaseTime;
				}

				if (p_intIndex == 2)
				{
					if (BackupManager.lstLooseFiles.Count == 0)
						BackupManager.CheckLooseFiles(false);

					float LooseFilesSize = ((BackupManager.LooseFilesSize / 1024f) / 1024f);

					if (BackupManager.LooseFilesSize != 0)
					{
						BackupManager.strLooseFilesSize = LooseFilesSize.ToString("0");
						strEstBaseCompression = LooseFilesSize > 0 ? (LooseFilesSize - (LooseFilesSize / 10)).ToString("0") : "-";
						float EstBaseTime = LooseFilesSize > 0 ? ((30 * LooseFilesSize) / 60) / 1024f : 0;
						strEstBaseTime = EstBaseTime > 0 ? (EstBaseTime > 1 ? Math.Round(EstBaseTime, 0).ToString() + " minutes" : "Less than a minute") : "-";
					}
					else
					{
						BackupManager.strLooseFilesSize = "-";
						strEstBaseCompression = "-";
						strEstBaseTime = "-";
					}
			
					lstView.Items[p_intIndex].SubItems[1].Text = BackupManager.strLooseFilesSize;
					lstView.Items[p_intIndex].SubItems[2].Text = BackupManager.lstLooseFiles.Count.ToString();
					lstView.Items[p_intIndex].SubItems[3].Text = strEstBaseCompression;
					lstView.Items[p_intIndex].SubItems[4].Text = strEstBaseTime;
				}
				if (p_intIndex == 3)
				{
					if (BackupManager.lstModArchives.Count == 0)
						BackupManager.CheckModArchives();

					float ModArchivesSize = ((BackupManager.ModArchivesSize / 1024f) / 1024f);

					if (BackupManager.ModArchivesSize != 0)
					{
						BackupManager.strModArchivesSize = ModArchivesSize.ToString("0");
						strEstBaseCompression = ModArchivesSize > 0 ? (ModArchivesSize - (ModArchivesSize / 10)).ToString("0") : "-";
						float EstBaseTime = ModArchivesSize > 0 ? ((30 * ModArchivesSize) / 60) / 1024f : 0;
						strEstBaseTime = EstBaseTime > 0 ? (EstBaseTime > 1 ? Math.Round(EstBaseTime, 0).ToString() + " minutes" : "Less than a minute") : "-";
					}
					else
					{
						BackupManager.strModArchivesSize = "-";
						strEstBaseCompression = "-";
						strEstBaseTime = "-";
					}

					lstView.Items[p_intIndex].SubItems[1].Text = BackupManager.strModArchivesSize;
					lstView.Items[p_intIndex].SubItems[2].Text = BackupManager.lstModArchives.Count.ToString();
					lstView.Items[p_intIndex].SubItems[3].Text = strEstBaseCompression;
					lstView.Items[p_intIndex].SubItems[4].Text = strEstBaseTime;
				}
			}
		}

		private void BackupManagerForm_FormClosed(object sender, FormClosedEventArgs e)
		{
			BackupManager.TotalFileSize = BackupManager.InstalledModFileSize + BackupManager.BaseGameFilesSize + BackupManager.LooseFilesSize + BackupManager.ModArchivesSize;
			BackupManager.InstalledModFileSize = 0;
			BackupManager.BaseGameFilesSize = 0;
			BackupManager.LooseFilesSize = 0;
			BackupManager.ModArchivesSize = 0;
		}


		#region Control Metrics Serialization

		/// <summary>
		/// Raises the <see cref="UserControl.Load"/> event of the control.
		/// </summary>
		/// <remarks>
		/// This loads any saved control metrics.
		/// </remarks>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
		}
		
		#endregion
		
		#region Column Resizing
		
		#endregion
	}
}
