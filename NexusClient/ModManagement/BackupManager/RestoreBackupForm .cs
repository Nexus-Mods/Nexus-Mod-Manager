using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Permissions;
using System.Security.Principal;
using System.Windows.Forms;
using Nexus.Client.UI;
using SevenZip;


namespace Nexus.Client.ModManagement.UI
{
	/// <summary>
	/// A view that allows editing of mod tags.
	/// </summary>
	public partial class RestoreBackupForm : ManagedFontForm
	{
		private ModManager ModManager = null;
		private ProfileManager ProfileManager = null;
		public string strText = string.Empty;

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_mmModManager">The view model that provides the data and operations for this view.</param>
		public RestoreBackupForm(ModManager p_mmModManager, ProfileManager p_pfProfileManager)
		{
			InitializeComponent();
			ModManager = p_mmModManager;
			ProfileManager = p_pfProfileManager;

			btYes.Enabled = (new WindowsPrincipal(WindowsIdentity.GetCurrent())).IsInRole(WindowsBuiltInRole.Administrator);

			var permissionSet = new PermissionSet(PermissionState.None);
			var writePermission = new FileIOPermission(FileIOPermissionAccess.Write, Path.Combine(ModManager.GameMode.PluginDirectory, "testfile.test"));
			permissionSet.AddPermission(writePermission);

			if (!permissionSet.IsSubsetOf(AppDomain.CurrentDomain.PermissionSet))
			{
				btYes.Enabled = false;
				btNo.Enabled = false;
				lblYes.Text = "Something is preventing Nexus Mod Manager from interacting with the installation folders!";
				lblNo.Text = "Please run NMM as Administrator if the mod installation folder is located in Program Files, or make sure your antivirus isn't blocking it.";
			}
			else
			{
				lblYes.Text = "Click 'Purge and Restore' if you want to DELETE the Virtual Install / Mod Installation (eg. Data for Skyrim) folders and restore them from the backup.(You must run NMM as administrator to enable this.)";
				lblNo.Text = "Click 'Restore' if you want to restore the Virtual Install / Mod Installation (eg. Data for Skyrim) folders from the backup WITHOUT deleting the previous files. Unpacked mod folders present in the current VirtualInstall will be removed if the same mod is present in the restore archive.";
			}
		}
		#endregion

		private void btYes_Click(object sender, System.EventArgs e)
		{
			if (string.IsNullOrEmpty(tbFile.Text))
			{
				lblEstimated.Visible = true;
				lblEstimated.Text = string.Format("Please, select a Backup File!", ModManager.GameMode.Name);
			}
			else
			{
				lblEstimated.Visible = false;
				strText = tbFile.Text;
				DialogResult = DialogResult.Yes;
			}
		}

		private void btNo_Click(object sender, System.EventArgs e)
		{
			if (string.IsNullOrEmpty(tbFile.Text))
			{
				lblEstimated.Visible = true;
				lblEstimated.Text = string.Format("Please, select a Backup File!", ModManager.GameMode.Name);
			}
			else
			{
				lblEstimated.Visible = false;
				strText = tbFile.Text;
				DialogResult = DialogResult.No;
			}
		}

		private void btCancel_Click(object sender, System.EventArgs e)
		{
			DialogResult = DialogResult.Cancel;
		}
		private void btSelectFile_Click(object sender, System.EventArgs e)
		{
			if (fdFile.ShowDialog() == DialogResult.OK)
			{
				BackupManager bkManager = new BackupManager(ModManager, ProfileManager);
				bkManager.Initialize();
				float TemporarySize = 0;
				
				bkManager.CheckRestoreFiles(fdFile.FileName);

				if (!bkManager.booValidArchive)
				{
					lblEstimated.Visible = true;
					lblEstimated.ForeColor = System.Drawing.Color.Red;
					lblEstimated.Text = "You didn't select a valid Nexus Mod Manager backup archive.";
					btYes.Enabled = false;
					btNo.Enabled = false;
				}
				else
				{

					if (bkManager.GameModeNameCheck.Count() == 0)
					{
						lblEstimated.Visible = true;
						lblEstimated.ForeColor = System.Drawing.Color.Red;
						lblEstimated.Text = string.Format("This is not a {0}'s Backup!", ModManager.GameMode.Name);
						btYes.Enabled = false;
						btNo.Enabled = false;
					}
					else
					{
						btYes.Enabled = true;
						btNo.Enabled = true;
						lblEstimated.ForeColor = System.Drawing.Color.Black;

						if (!bkManager.booPluginPath || !bkManager.booVirtualPath)
							btYes.Enabled = false;
						else
							btYes.Enabled = (new WindowsPrincipal(WindowsIdentity.GetCurrent())).IsInRole(WindowsBuiltInRole.Administrator);

						TemporarySize = ((bkManager.InstalledModFileSize + bkManager.BaseGameFilesSize) / 1024f) / 1024f;

						lblEstimated.Visible = true;
						lblEstimated.Text = string.Format("Estimated Restore Size:" + Environment.NewLine + "- Clicking YES: Temporary backup {0} MB - Restored files {1} MB " + Environment.NewLine + "- Clicking NO: {2} MB", Math.Round(TemporarySize, 0), Math.Round(bkManager.RestoredFiles, 0), Math.Round(bkManager.RestoredFiles, 0));
						tbFile.Text = fdFile.FileName;
					}
				}
			}
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
