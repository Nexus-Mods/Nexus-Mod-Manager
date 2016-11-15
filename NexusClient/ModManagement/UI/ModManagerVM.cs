using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Commands.Generic;
using Nexus.Client.Games;
using Nexus.UI.Controls;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;
using Nexus.Client.Settings;
using Nexus.Client.UI;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement.UI
{
    /// <summary>
    /// This class encapsulates the data and the operations presented by UI
    /// elements that display mod management.
    /// </summary>
    public class ModManagerVM
	{
		private bool m_booIsCategoryInitialized = false;
		private Control m_ctlParentForm = null;

		#region Events

		/// <summary>
		/// Raised when mods are about to be added to the mod manager.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> AddingMod = delegate { };

		/// <summary>
		/// Raised when mods are about to be deleted from the mod manager.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTaskSet>> DeletingMod = delegate { };

		/// <summary>
		/// Raised when the activation status of a mod is about to be changed.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTaskSet>> ChangingModActivation = delegate { };

		/// <summary>
		/// Raised when a mod is being tagged.
		/// </summary>
		public event EventHandler<EventArgs<ModTaggerVM>> TaggingMod = delegate { };

		/// <summary>
		/// Raised when the category list is being updated.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> UpdatingCategory = delegate { };

		/// <summary>
		/// Raised when the mods list is being updated.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> UpdatingMods = delegate { };

		/// <summary>
		/// Raised when the categories list is being updated.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> UpdatingCategories = delegate { };

		/// <summary>
		/// Raised when the readme manager is being set up.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> ReadMeManagerSetup = delegate { };


		/// <summary>
		/// Raised when the toggle all mods is being set up.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> TogglingAllWarning = delegate { };

		/// <summary>
		/// Raised when disabling multiple mods.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> DisablingMultipleMods = delegate { };

		/// <summary>
		/// Raised when uninstalling multiple mods.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> DeactivatingMultipleMods = delegate { };

		/// <summary>
		/// Raised when uninstalling multiple mods.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> DeletingMultipleMods = delegate { };
		
		/// <summary>
		/// Raised when installing multiple mods.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> ActivatingMultipleMods = delegate { };

		/// <summary>
		/// Raised when activating a single mod.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> ActivatingMod = delegate { };

		/// <summary>
		/// Raised when adding Automatic Downloads.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> AutomaticDownloading = delegate { };

		/// <summary>
		/// Raised when reinstalling a single mod.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> ReinstallingMod = delegate { };

		#endregion

		#region Delegates

		/// <summary>
		/// Called when an updater's action needs to be confirmed.
		/// </summary>
		public ConfirmActionMethod ConfirmUpdaterAction = delegate { return true; };

		#endregion

		/// <summary>
		/// Raised when the deletion of a mod file needs to be confirmed.
		/// </summary>
		public Func<List<IMod>, bool> ConfirmModFileDeletion = delegate { return false; };

		/// <summary>
		/// Raised when the overwriting of a mod file needs to be confirmed.
		/// </summary>
		public Func<string, string, string> ConfirmModFileOverwrite = delegate { return null; };

		/// <summary>
		/// Raised when the overwriting of an item by another item being installed by a mod needs to be confirmed.
		/// </summary>
		public ConfirmItemOverwriteDelegate ConfirmItemOverwrite = delegate { return OverwriteResult.No; };

		/// <summary>
		/// Raised when the overwriting of an item by another item being installed by a mod needs to be confirmed.
		/// </summary>
		public ConfirmModUpgradeDelegate ConfirmModUpgrade = delegate { return ConfirmUpgradeResult.Cancel; };

		#region Properties

		#region Commands

		/// <summary>
		/// Gets the command to add a mod to the manager.
		/// </summary>
		/// <remarks>
		/// The commands takes an argument describing the file path to the mod to be added.
		/// </remarks>
		/// <value>The command to add a mod to the manager.</value>
		public Command<string> AddModCommand { get; private set; }

		/// <summary>
		/// Gets the command to delete a mod from the manager.
		/// </summary>
		/// <remarks>
		/// The commands takes an argument describing the mod to be deleted.
		/// </remarks>
		/// <value>The command to delete a mod from the manager.</value>
		public Command<IMod> DeleteModCommand { get; private set; }

		/// <summary>
		/// Gets the command to activate a mod.
		/// </summary>
		/// <remarks>
		/// The commands takes an argument describing the mod to be activated.
		/// </remarks>
		/// <value>The command to activate a mod.</value>
		public Command<List<IMod>> ActivateModCommand { get; private set; }

		/// <summary>
		/// Gets the command to deactivate a mod.
		/// </summary>
		/// <remarks>
		/// The commands takes an argument describing the mod to be deactivated.
		/// </remarks>
		/// <value>The command to deactivate a mod.</value>
		public Command<IMod> DisableModCommand { get; private set; }

		/// <summary>
		/// Gets the command to tag a mod.
		/// </summary>
		/// <remarks>
		/// The commands takes an argument describing the mod to be tagged.
		/// </remarks>
		/// <value>The command to tag a mod.</value>
		public Command<IMod> TagModCommand { get; private set; }

		#endregion

		/// <summary>
		/// Gets the mod manager to use to manage mods.
		/// </summary>
		/// <value>The mod manager to use to manage mods.</value>
		public ModManager ModManager { get; private set; }

		/// <summary>
		/// Gets the mod repository from which to get mods and mod metadata.
		/// </summary>
		/// <value>The mod repository from which to get mods and mod metadata.</value>
		public IModRepository ModRepository { get; private set; }

		/// <summary>
		/// Gets the current profile manager.
		/// </summary>
		/// <value>The current profile manager.</value>
		public IProfileManager ProfileManager { get; private set; }

		/// <summary>
		/// Gets the category manager to use to manage categories.
		/// </summary>
		/// <value>The category manager to use to manage categories.</value>
		public CategoryManager CategoryManager { get; private set; }

		/// <summary>
		/// Gets the list of mods being managed by the mod manager.
		/// </summary>
		/// <value>The list of mods being managed by the mod manager.</value>
		public ReadOnlyObservableList<IMod> ManagedMods
		{
			get
			{
				return ModManager.ManagedMods;
			}
		}

		/// <summary>
		/// Gets the newest available information about the managed mods.
		/// </summary>
		/// <value>The newest available information about the managed mods.</value>
		public ReadOnlyObservableList<AutoUpdater.UpdateInfo> NewestModInfo
		{
			get
			{
				return ModManager.NewestModInfo;
			}
		}

		/// <summary>
		/// Gets the list of active mods.
		/// </summary>
		/// <value>The list of active mods.</value>
		public ReadOnlyObservableList<IMod> ActiveMods
		{
			get
			{
				return ModManager.ActiveMods;
			}
		}

		/// <summary>
		/// Gets the current virtual mod activator.
		/// </summary>
		/// <value>The current virtual mod activator.</value>
		public IVirtualModActivator VirtualModActivator
		{
			get
			{
				return ModManager.VirtualModActivator;
			}
		}

		/// <summary>
		/// Gets the application and user settings.
		/// </summary>
		/// <value>The application and user settings.</value>
		public ISettings Settings { get; private set; }

		/// <summary>
		/// Gets the theme to use for the UI.
		/// </summary>
		/// <value>The theme to use for the UI.</value>
		public Theme CurrentTheme { get; private set; }

		/// <summary>
		/// Gets whether the manager is in offline mode.
		/// </summary>
		/// <value>Whether the manager is in offline mode.</value>
		public bool OfflineMode
		{
			get
			{
				return ModManager.ModRepository.IsOffline;
			}
		}

		/// <summary>
		/// Gets whether the category file has been initialized.
		/// </summary>
		/// <value>Whether the category file has been initialized.</value>
		public bool IsCategoryInitialized
		{
			get
			{
				return m_booIsCategoryInitialized;
			}
		}

		/// <summary>
		/// Gets the parent form.
		/// </summary>
		/// <value>The parent form.</value>
		public Control ParentForm
		{
			get
			{
				return m_ctlParentForm;
			}
			set
			{
				m_ctlParentForm = value;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_mmdModManager">The mod manager to use to manage mods.</param>
		/// <param name="p_setSettings">The application and user settings.</param>
		/// <param name="p_thmTheme">The current theme to use for the views.</param>
		public ModManagerVM(ModManager p_mmdModManager, IProfileManager p_prmProfileManager, ISettings p_setSettings, Theme p_thmTheme)
		{
			ModManager = p_mmdModManager;
			ModRepository = p_mmdModManager.ModRepository;
			ProfileManager = p_prmProfileManager;
			Settings = p_setSettings;
			CurrentTheme = p_thmTheme;
			CategoryManager = new CategoryManager(ModManager.CurrentGameModeModDirectory, "categories");
			if (this.CategoryManager.IsValidPath)
			{
				this.CategoryManager.LoadCategories(String.Empty);
				m_booIsCategoryInitialized = true;
			}
			else
				this.CategoryManager.Backup();

			AddModCommand = new Command<string>("Add Mod", "Adds a mod to the manager.", AddMod);
			DeleteModCommand = new Command<IMod>("Delete Mod", "Deletes the selected mod.", DeleteMod);
			ActivateModCommand = new Command<List<IMod>>("Install/Enable Mod", "Installs and/or enables the selected mod.", ActivateMods);
			DisableModCommand = new Command<IMod>("Disable Mod", "Disables the selected mod.", DisableMod);
			TagModCommand = new Command<IMod>("Tag Mod", "Gets missing mod info.", TagMod);

			ModManager.UpdateCheckStarted += new EventHandler<EventArgs<IBackgroundTask>>(ModManager_UpdateCheckStarted);
			ModManager.UpdateCategoriesCheckStarted += new EventHandler<EventArgs<IBackgroundTask>>(ModManager_UpdateCategoriesCheckStarted);
			ModManager.AutomaticDownloadStarted += new EventHandler<EventArgs<IBackgroundTask>>(ModManager_AutomaticDownloadStarted);
		}

		#endregion

		#region Readme Management
		/// <summary>
		/// Delete the ReadMe file.
		/// </summary>
		/// <param name="p_modMod">The mod.</param>
		public void DeleteReadMe(IMod p_modMod)
		{
			if (p_modMod == null)
			{
				return;
			}
			ModManager.ReadMeManager.DeleteReadMe(Path.GetFileNameWithoutExtension(p_modMod.Filename));
			ModManager.ReadMeManager.SaveReadMeConfig();
		}

		/// <summary>
		/// Checks the ReadMe file path if exists.
		/// </summary>
		/// <param name="p_modMod">The mod.</param>
		public string[] GetModReadMe(IMod p_modMod)
		{
			if (p_modMod == null)
			{
				return null;
			}
			return ModManager.ReadMeManager.CheckModReadMe(Path.GetFileNameWithoutExtension(p_modMod.Filename));
		}

		/// <summary>
		/// Open the ReadMe file.
		/// </summary>
		/// <param name="p_modMod">The mod.</param>
		/// <param name="p_strFileName">The file to open.</param>
		public bool OpenReadMe(IMod p_modMod, string p_strFileName)
		{
			if (p_modMod == null)
			{
				return false;
			}
			bool booResult = false;
			string strReadMe = ModManager.ReadMeManager.GetModReadMe(Path.GetFileNameWithoutExtension(p_modMod.Filename), p_strFileName);
			if (!String.IsNullOrEmpty(strReadMe))
			{
				try
				{
					System.Diagnostics.Process.Start(strReadMe);
					booResult = true;
				}
				catch
				{
					booResult = false;
				}
			}
			return booResult;
		}

		#endregion

		#region Mod Addition/Deletion

		/// <summary>
		/// Installs the specified mod.
		/// </summary>
		/// <param name="p_strPath">The path to the mod to install.</param>
		protected void AddMod(string p_strPath)
		{
			IBackgroundTask bgtAddingTask = ModManager.AddMod(p_strPath, ConfirmFileOverwrite);
		}

		/// <summary>
		/// The callback that confirm a file overwrite.
		/// </summary>
		/// <param name="p_strOldFilePath">The path to the file that is to be overwritten.</param>
		/// <param name="p_strNewFilePath">An out parameter specifying the file to to which to
		/// write the file.</param>
		/// <returns><c>true</c> if the file should be written;
		/// <c>false</c> otherwise.</returns>
		protected bool ConfirmFileOverwrite(string p_strOldFilePath, out string p_strNewFilePath)
		{
			string strNewFileName = p_strOldFilePath;
			string strExtension = Path.GetExtension(p_strOldFilePath);
			string strDirectory = Path.GetDirectoryName(p_strOldFilePath);
			for (Int32 i = 2; i < Int32.MaxValue && File.Exists(strNewFileName); i++)
				strNewFileName = Path.Combine(strDirectory, String.Format("{0} ({1}){2}", Path.GetFileNameWithoutExtension(p_strOldFilePath), i, strExtension));
			if (File.Exists(strNewFileName))
				throw new Exception("Cannot write file. Unable to find unused file name.");
			p_strNewFilePath = ConfirmModFileOverwrite(p_strOldFilePath, strNewFileName);
			return (p_strNewFilePath != null);
		}

		/// <summary>
		/// Deletes the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod to activate.</param>
		public void DeleteMod(IMod p_modMod)
		{
			if (p_modMod != null)
			{
				IBackgroundTaskSet btsInstall = ModManager.DeleteMod(p_modMod, ModManager.ActiveMods);
				DeletingMod(this, new EventArgs<IBackgroundTaskSet>(btsInstall));
			}
		}

		#endregion

		#region Mod Activation/Deactivation

		/// <summary>
		/// Activates the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod to activate.</param>
		public void ActivateMod(IMod p_modMod)
		{
			if (VirtualModActivator.MultiHDMode && !UacUtil.IsElevated)
			{
				MessageBox.Show("It looks like MultiHD mode is enabled but you're not running NMM as Administrator, you will be unable to install/activate mods or switch profiles." + Environment.NewLine + Environment.NewLine + "Close NMM and run it as Administrator to fix this.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			string strMessage;
			bool booRequiresConfig = ModManager.GameMode.RequiresExternalConfig(out strMessage);

			if (booRequiresConfig)
			{
				ExtendedMessageBox.Show(this.ParentForm, strMessage, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}

			string strErrorMessage = ModManager.RequiredToolErrorMessage;
			if (string.IsNullOrEmpty(strErrorMessage))
			{
				if (!ActiveMods.Contains(p_modMod))
				{
					ModMatcher mmcMatcher = new ModMatcher(ModManager.InstallationLog.ActiveMods, true);
					IMod modOldVersion = mmcMatcher.FindAlternateVersion(p_modMod, true);

					if (modOldVersion != null)
					{
						string strUpgradeMessage = "A different version of {0} has been detected. The installed version is {1}, the new version is {2}. Would you like to upgrade?" + Environment.NewLine + "Selecting No will install the new Mod normally.";
						strUpgradeMessage = String.Format(strUpgradeMessage, modOldVersion.ModName, modOldVersion.HumanReadableVersion, p_modMod.HumanReadableVersion);
						switch (MessageBox.Show(strUpgradeMessage, "Upgrade", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
						{
							case DialogResult.Yes:
								ReinstallMod(modOldVersion, p_modMod);
								break;
							case DialogResult.No:
								IBackgroundTaskSet btsInstall = ModManager.ActivateMod(p_modMod, ConfirmModUpgrade, ConfirmItemOverwrite, ModManager.ActiveMods);
								if (btsInstall != null)
									ModManager.ModActivationMonitor.AddActivity(btsInstall);
								break;
							case DialogResult.Cancel:
								break;
							default:
								break;
						}
					}
					else
					{
						IBackgroundTaskSet btsInstall = ModManager.ActivateMod(p_modMod, ConfirmModUpgrade, ConfirmItemOverwrite, ModManager.ActiveMods);
						if (btsInstall != null)
							ModManager.ModActivationMonitor.AddActivity(btsInstall);
					}

				}
				else
					EnableMod(p_modMod);
			}
			else
			{
				ExtendedMessageBox.Show(ParentForm, strErrorMessage, "Required Tool not present", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
		}

		/// <summary>
		/// Activates the given mod.
		/// </summary>
		/// <param name="p_lstMod">The mods to activate.</param>
		public void ActivateMods(List<IMod> p_lstMod)
		{
			if (VirtualModActivator.MultiHDMode && !UacUtil.IsElevated)
			{
				MessageBox.Show("It looks like MultiHD mode is enabled but you're not running NMM as Administrator, you will be unable to install/activate mods or switch profiles." + Environment.NewLine + Environment.NewLine + "Close NMM and run it as Administrator to fix this.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			string strMessage;
			bool booRequiresConfig = ModManager.GameMode.RequiresExternalConfig(out strMessage);

			if (booRequiresConfig)
			{
				ExtendedMessageBox.Show(this.ParentForm, strMessage, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
			
			string strErrorMessage = ModManager.RequiredToolErrorMessage;
			if (string.IsNullOrEmpty(strErrorMessage))
			{
				foreach (IMod modMod in p_lstMod)
				{
					if (!ActiveMods.Contains(modMod))
					{
						ModMatcher mmcMatcher = new ModMatcher(ModManager.InstallationLog.ActiveMods, true);
						IMod modOldVersion = mmcMatcher.FindAlternateVersion(modMod, true);

						if (modOldVersion != null)
						{
							string strUpgradeMessage = "A different version of {0} has been detected. The installed version is {1}, the new version is {2}. Would you like to upgrade?" + Environment.NewLine + "Selecting No will install the new Mod normally.";
							strUpgradeMessage = String.Format(strUpgradeMessage, modOldVersion.ModName, modOldVersion.HumanReadableVersion, modMod.HumanReadableVersion);
							switch (MessageBox.Show(strUpgradeMessage, "Upgrade", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
							{
								case DialogResult.Yes:
									ReinstallMod(modOldVersion, modMod);
									break;
								case DialogResult.No:
									IBackgroundTaskSet btsInstall = ModManager.ActivateMod(modMod, ConfirmModUpgrade, ConfirmItemOverwrite, ModManager.ActiveMods);
									if (btsInstall != null)
										ModManager.ModActivationMonitor.AddActivity(btsInstall);
									break;
								case DialogResult.Cancel:
									break;
								default:
									break;
							}
						}
						else
						{
							IBackgroundTaskSet btsInstall = ModManager.ActivateMod(modMod, ConfirmModUpgrade, ConfirmItemOverwrite, ModManager.ActiveMods);
							if (btsInstall != null)
								ModManager.ModActivationMonitor.AddActivity(btsInstall);
						}
					}
					else
						EnableMod(modMod);
				}
			}
			else
			{
				ExtendedMessageBox.Show(ParentForm, strErrorMessage, "Required Tool not present", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
		}

		/// <summary>
		/// Deactivates the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod to deactivate.</param>
		public void DeactivateMod(IMod p_modMod)
		{
			VirtualModActivator.DisableMod(p_modMod);

			IBackgroundTaskSet btsUninstall = ModManager.DeactivateMod(p_modMod, ModManager.ActiveMods);
			if (btsUninstall != null)
				ModManager.ModActivationMonitor.AddActivity(btsUninstall);
		}

		/// <summary>
		/// Deactivates the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod to deactivate.</param>
		protected void DeactivateMods(List<IMod> p_lstMod)
		{
			foreach (IMod modMod in p_lstMod)
			{
				IBackgroundTaskSet btsUninstall = ModManager.DeactivateMod(modMod, ModManager.ActiveMods);
				if (btsUninstall != null)
					ModManager.ModActivationMonitor.AddActivity(btsUninstall);
			}
		}

		/// <summary>
		/// Reinstalls the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod to reinstall.</param>
		public void ReinstallMod(IMod p_modMod, IMod p_modUpgrade)
		{
			VirtualModActivator.DisableMod(p_modMod);

			IBackgroundTaskSet btsUninstall = ModManager.DeactivateMod(p_modMod, ModManager.ActiveMods);
			if (btsUninstall != null)
				ModManager.ModActivationMonitor.AddActivity(btsUninstall);
			
			if (VirtualModActivator.MultiHDMode && !UacUtil.IsElevated)
			{
				MessageBox.Show("It looks like MultiHD mode is enabled but you're not running NMM as Administrator, you will be unable to install/activate mods or switch profiles." + Environment.NewLine + Environment.NewLine + "Close NMM and run it as Administrator to fix this.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			string strMessage;
			bool booRequiresConfig = ModManager.GameMode.RequiresExternalConfig(out strMessage);

			if (booRequiresConfig)
			{
				ExtendedMessageBox.Show(this.ParentForm, strMessage, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}

			string strErrorMessage = ModManager.RequiredToolErrorMessage;
			if (String.IsNullOrEmpty(strErrorMessage))
			{
				IBackgroundTaskSet btsReinstall = ModManager.ReinstallMod(p_modUpgrade ?? p_modMod, ConfirmModUpgrade, ConfirmItemOverwrite, ModManager.ActiveMods);
				if (btsReinstall != null)
					ModManager.ModActivationMonitor.AddActivity(btsReinstall);
			}
			else
			{
				ExtendedMessageBox.Show(ParentForm, strErrorMessage, "Required Tool not present", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
		}

		/// <summary>
		/// Disables all the mods.
		/// </summary>
		/// <param name="p_rolModList">The list of mods to disable.</param>
		public void DisableMultipleMods(List<IMod> p_rolModList)
		{
			DialogResult Result = MessageBox.Show("Do you want to disable all the active mods?", "Disable Mods", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

			if (Result == DialogResult.Yes)
			{
				DisablingMultipleMods(this, new EventArgs<IBackgroundTask>(ModManager.DisableMultipleMods(p_rolModList, ConfirmUpdaterAction)));
			}
		}

		/// <summary>
		/// Deletes all the mods.
		/// </summary>
		/// <param name="p_rolModList">The list of mods to disable.</param>
		public void DeleteMultipleMods(ReadOnlyObservableList<IMod> p_rolModList, bool p_booForceUninstall, bool p_booSilent, bool p_booFilesOnly)
		{
			DeletingMultipleMods(p_booSilent, new EventArgs<IBackgroundTask>(ModManager.DeleteMultipleMods(p_rolModList, ConfirmUpdaterAction)));
		}

		/// <summary>
		/// Deactivates all the mods.
		/// </summary>
		/// <param name="p_rolModList">The list of Active Mods.</param>
		public void DeactivateMultipleMods(ReadOnlyObservableList<IMod> p_rolModList, bool p_booForceUninstall, bool p_booSilent, bool p_booFilesOnly)
		{
			DialogResult Result = DialogResult.None;

			if (!p_booForceUninstall)
				Result = MessageBox.Show("Do you want to uninstall all the installed mods?", "Deactivate Mods", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
			
			if (p_booForceUninstall || (Result == DialogResult.Yes))
			{
				DeactivatingMultipleMods(p_booSilent, new EventArgs<IBackgroundTask>(ModManager.DeactivateMultipleMods(p_rolModList, p_booFilesOnly, ConfirmUpdaterAction)));
			}
		}

		/// <summary>
		/// Install all the mods.
		/// </summary>
		/// <param name="p_lstModList">The list of Active Mods.</param>
        /// <param name="p_booAllowCancel">Defines if the user is allowed to cancel</param>
		public void MultiModInstall(List<IMod> p_lstModList, bool p_booAllowCancel)
		{
			ActivatingMultipleMods(this, new EventArgs<IBackgroundTask>(ModManager.ActivateMultipleMods(p_lstModList, p_booAllowCancel, ConfirmUpdaterAction, ConfirmItemOverwrite)));
		}

		#endregion

		#region Mod Tagging

		/// <summary>
		/// Tags the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod to tag.</param>
		protected void TagMod(IMod p_modMod)
		{
			if (!ModManager.ModRepository.IsOffline)
			{
				ModTaggerVM mtgTagger = new ModTaggerVM(ModManager.GetModTagger(), p_modMod, Settings, CurrentTheme);
				TaggingMod(this, new EventArgs<ModTaggerVM>(mtgTagger));
			}
			else
			{
				ModManager.Login();
				ModTaggerVM mtgTagger = new ModTaggerVM(ModManager.GetModTagger(), p_modMod, Settings, CurrentTheme);
				ModManager.AsyncTagMod(this, mtgTagger, TaggingMod);
			}
		}

		/// <summary>
		/// Updates the mod's name.
		/// </summary>
		/// <param name="p_modMod">The mod whose name is to be updated.</param>
		/// <param name="p_strNewModName">The name to which to update the mod's name.</param>
		public void UpdateModName(IMod p_modMod, string p_strNewModName)
		{
			ModInfo mifNewInfo = new ModInfo(p_modMod);
			mifNewInfo.ModName = p_strNewModName;
			p_modMod.UpdateInfo(mifNewInfo, true);
		}

        public void UpdateModLoadOrder(IMod p_modMod, int p_intNewPosition)
        {
            ModInfo mifNewInfo = new ModInfo(p_modMod);
            mifNewInfo.NewPlaceInModLoadOrder = p_intNewPosition;
            p_modMod.UpdateInfo(mifNewInfo, true);
        }

		#endregion

		#region Virtual Mod Activation
		
		public void EnableMod(IMod p_modMod)
		{
			if (VirtualModActivator.MultiHDMode && !UacUtil.IsElevated)
			{
				MessageBox.Show("It looks like MultiHD mode is enabled but you're not running NMM as Administrator, you will be unable to install/activate mods or switch profiles." + Environment.NewLine + Environment.NewLine + "Close NMM and run it as Administrator to fix this.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}
			
			ActivatingMod(p_modMod, new EventArgs<IBackgroundTask>(VirtualModActivator.ActivatingMod(p_modMod, false, ConfirmUpdaterAction)));
		}

		public void DisableMod(IMod p_modMod)
		{
			ActivatingMod(p_modMod, new EventArgs<IBackgroundTask>(VirtualModActivator.ActivatingMod(p_modMod, true, ConfirmUpdaterAction)));
		}

		#endregion

		#region Mod Updating

		/// <summary>
		/// Checks for mod updates.
		/// </summary>
		/// <returns>Message</returns>
		/// <param name="p_booOverrideCategorySetup">Whether to just check for mods missing the Nexus Category.</param>
		public void CheckForUpdates(bool p_booOverrideCategorySetup)
		{
			List<IMod> lstModList = new List<IMod>();

			if (p_booOverrideCategorySetup)
			{
				lstModList.AddRange(from Mod in ManagedMods
									where ((Mod.CategoryId == 0) && (Mod.CustomCategoryId < 0))
									select Mod);
			}
			else
				lstModList.AddRange(ManagedMods);

			if (!ModRepository.IsOffline)
			{
				if (lstModList.Count > 0)
				{
					UpdatingMods(this, new EventArgs<IBackgroundTask>(ModManager.UpdateMods(lstModList, null, ConfirmUpdaterAction, p_booOverrideCategorySetup, false)));
				}
			}
			else
			{
				ModManager.Login();
				ModManager.AsyncUpdateMods(lstModList, null, ConfirmUpdaterAction, p_booOverrideCategorySetup, false);
			}
		}

		/// <summary>
		/// Checks for mod updates.
		/// </summary>
		/// <returns>Message</returns>
		/// <param name="p_booOverrideCategorySetup">Whether to just check for mods missing the Nexus Category.</param>
		public void CheckCategoriesUpdates()
		{
			string strMessage = "Are you sure you want to reset to the Nexus site default categories?";
			strMessage += Environment.NewLine + Environment.NewLine + "Note: The category list will be updated from the Nexus and your downloaded mods will be automatically reassigned to the Nexus categories.";
			DialogResult Result = MessageBox.Show(strMessage, "Category reset", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
			if (Result == DialogResult.Yes)
			{
				if (!ModRepository.IsOffline)
					UpdatingCategories(this, new EventArgs<IBackgroundTask>(ModManager.UpdateCategories(CategoryManager, ProfileManager, ConfirmUpdaterAction)));
				else
				{
					ModManager.Login();
					ModManager.AsyncUpdateCategories(CategoryManager, ProfileManager, ConfirmUpdaterAction);
				}
			}
		}

		/// <summary>
		/// Checks for mod file's missing download id.
		/// </summary>
		/// <returns>Message</returns>
		public void CheckModFileDownloadId(bool? p_booOnlyMissing)
		{
			List<IMod> lstModList = new List<IMod>();

			lstModList.AddRange(from Mod in ManagedMods select Mod);

			if (!ModRepository.IsOffline)
			{
				if (ManagedMods.Count > 0)
				{
					UpdatingMods(this, new EventArgs<IBackgroundTask>(ModManager.UpdateMods(lstModList, ProfileManager, ConfirmUpdaterAction, false, p_booOnlyMissing)));
				}
			}
			else
			{
				ModManager.Login();
				ModManager.AsyncUpdateMods(lstModList, ProfileManager, ConfirmUpdaterAction, false, p_booOnlyMissing);
			}
		}

		/// <summary>
		/// Toggles the endorsement for the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod to endorse/unendorse.</param>
		public void ToggleModEndorsement(IMod p_modMod, HashSet<IMod> p_hashMods, bool? p_booEnable)
		{

			string strResult = string.Empty;
			if (String.IsNullOrEmpty(p_modMod.Id))
				throw new Exception("we couldn't find a proper Nexus ID or the file no longer exists on the Nexus sites.");

			if (!ModManager.ModRepository.IsOffline)
				ModManager.ToggleModEndorsement(p_modMod);
			else
			{
				ModManager.Login();
				ModManager.AsyncEndorseMod(p_modMod);
			}
		}

		/// <summary>
		/// Toggles the mod update warning.
		/// </summary>
		/// <param name="p_hashMods">The mod list.</param>
		/// <param name="p_booEnable">Whether to enable/disable the warning or toggle it if null.</param>
		public void ToggleModUpdateWarning(HashSet<IMod> p_hashMods, bool? p_booEnable)
		{
			TogglingAllWarning(this, new EventArgs<IBackgroundTask>(ModManager.ToggleUpdateWarningTask(p_hashMods, p_booEnable, ConfirmUpdaterAction)));
		}

		/// <summary>
		/// Updates the downlodIds into the Virtual Install config files.
		/// </summary>
		/// <param name="p_dctDownloadId">The updated downloadIds.</param>
		public void UpdateVirtualListDownloadId(Dictionary<string, string> p_dctDownloadId)
		{
			VirtualModActivator.UpdateDownloadId(ProfileManager.GetProfileModListPath(ProfileManager.CurrentProfile), p_dctDownloadId);
		}

		#endregion

		#region Category Updating

		/// <summary>
		/// Switches the mod category.
		/// </summary>
		/// <param name="p_modMod">The mod.</param>
		/// <param name="p_intCategoryId">The new category id.</param>
		public void SwitchModCategory(IMod p_modMod, Int32 p_intCategoryId)
		{
			ModManager.SwitchModCategory(p_modMod, p_intCategoryId);
		}

		/// <summary>
		/// Resets to the repository default categories.
		/// </summary>
		public bool ResetDefaultCategories(bool p_booResetCategories)
		{
			if (p_booResetCategories)
				CategoryManager.ResetCategories(ModManager.CurrentGameModeDefaultCategories);
			SwitchModsToCategory(-1);
			CheckForUpdates(true);
			return true;
		}

		/// <summary>
		/// Resets to the repository default categories.
		/// </summary>
		public bool ResetToUnassigned()
		{
			string strMessage = "Are you sure you want to reset all mods to the Unassigned category?";
			strMessage += Environment.NewLine + Environment.NewLine + "Note: If you're using custom categories you won't be able to revert this operation.";
			DialogResult Result = MessageBox.Show(strMessage, "Category reset", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
			if (Result == DialogResult.Yes)
			{
				SwitchModsToCategory(0);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Removes all categories and set all mods to Unassigned.
		/// </summary>
		public bool RemoveAllCategories()
		{
			string strMessage = "Are you sure you want to remove all the categories and set all mods to Unassigned?";
			strMessage += Environment.NewLine + Environment.NewLine + "Note: If you're using custom categories you won't be able to revert this operation.";
			DialogResult Result = MessageBox.Show(strMessage, "Category remove", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
			if (Result == DialogResult.Yes)
			{
				CategoryManager.ResetCategories(String.Empty);
				SwitchModsToCategory(0);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Sets all managed mods to the given category id.
		/// </summary>
		/// <param name="p_intCategoryId">The unassigned category Id.</param>
		private void SwitchModsToCategory(Int32 p_intCategoryId)
		{
			UpdatingCategory(this, new EventArgs<IBackgroundTask>(CategoryManager.Update(ModManager, ManagedMods, p_intCategoryId, ConfirmUpdaterAction)));
		}

		/// <summary>
		/// Sets the selected mods to the given category id.
		/// </summary>
		/// <param name="p_lstSelectedMods">The list of selected mods.</param>
		/// <param name="p_intCategoryId">The unassigned category Id.</param>
		public void SwitchModsToCategory(List<IMod> p_lstSelectedMods, Int32 p_intCategoryId)
		{
			UpdatingCategory(this, new EventArgs<IBackgroundTask>(CategoryManager.Update(ModManager, p_lstSelectedMods, p_intCategoryId, ConfirmUpdaterAction)));
		}

		/// <summary>
		/// Sets all mods assigned to a removed category to Unassigned.
		/// </summary>
		/// <param name="p_imcCategory">The removed category.</param>
		public void SwitchModsToUnassigned(IModCategory p_imcCategory)
		{
			var CategoryMods = ManagedMods.Where(Mod => (Mod.CustomCategoryId >= 0 ? Mod.CustomCategoryId : Mod.CategoryId) == p_imcCategory.Id).ToList();

			UpdatingCategory(this, new EventArgs<IBackgroundTask>(CategoryManager.Update(ModManager, CategoryMods, 0, ConfirmUpdaterAction)));
		}

		/// <summary>
		/// Checks if the CategoryManager has been properly initialized.
		/// </summary>
		public void CheckCategoryManager()
		{
			if (!this.CategoryManager.IsValidPath)
			{
				string strMessage = "You currently don't have any file categories setup.";
				strMessage += Environment.NewLine + "Would you like NMM to organise your mods based on the categories the Nexus sites use (YES), or would you like to organise your categories yourself (NO)?";
				strMessage += Environment.NewLine + Environment.NewLine + "Note: If you choose to use Nexus categories you can still create your own categories and move your files around them. This initial Nexus setup is just a template for you to use.";

				DialogResult Result = ExtendedMessageBox.Show(null, strMessage, "Category setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
				if (Result == DialogResult.Yes)
				{
					this.CategoryManager.LoadCategories(ModManager.CurrentGameModeDefaultCategories);
					if (!OfflineMode)
						CheckForUpdates(true);
				}
				else
				{
					this.CategoryManager.LoadCategories(String.Empty);
					SwitchModsToCategory(0);
				}
			}
			else
				this.CategoryManager.LoadCategories(String.Empty);
		}

		#endregion

		#region ReadMe Manager

		/// <summary>
		/// Checks if the ReadMe Manager has been properly initialized.
		/// </summary>
		public void CheckReadMeManager()
		{
			if ((this.ModManager.ManagedMods.Count > 0) && (!this.ModManager.ReadMeManager.IsInitialized))
			{
				string strMessage = string.Empty;
				if (ModManager.ReadMeManager.IsXMLCorrupt)
					strMessage = "An error occurred loading the ReadMeManager.xml file." + Environment.NewLine + Environment.NewLine;

				strMessage += "NMM needs to setup the Readme Manager, this could take a few minutes depending on the number of mods and archive sizes.";
				strMessage += Environment.NewLine + "Do you want to perform the Readme Manager startup scan?";
				strMessage += Environment.NewLine + Environment.NewLine + "Note: if choose not to, you will be able to perform a scan by selecting any number of mods, and choosing 'Readme Scan' in the right-click menu.";

				if (ExtendedMessageBox.Show(null, strMessage, "Readme Manager Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
					SetupReadMeManager(ModManager.ManagedMods.ToList<IMod>());
			}
		}

		/// <summary>
		/// Readme Manager setup.
		/// </summary>
		/// <returns>Message</returns>
		/// <param name="p_lstModList">The list of mods.</param>
		public void SetupReadMeManager(List<IMod> p_lstModList)
		{
			ReadMeManagerSetup(this, new EventArgs<IBackgroundTask>(ModManager.SetupReadMeManager(p_lstModList, ConfirmUpdaterAction)));
		}

		#endregion

		/// <summary>
		/// Purges all the scripted installers log files inside the InstallInfo\Scripted folder.
		/// </summary>
		public void PurgeXMLInstalledFile()
		{
			ModManager.PurgeXMLInstalledFile();
		}

		/// <summary>
		/// Gets the list of extensions commonly used for mod files.
		/// </summary>
		/// <returns>The list of extensions commonly used for mod files.</returns>
		public IList<string> GetModFormatExtensions()
		{
			Set<string> setModExtensions = new Set<string>(StringComparer.OrdinalIgnoreCase);
			foreach (IModFormat mftFormat in ModManager.ModFormats)
				setModExtensions.Add(mftFormat.Extension);
			setModExtensions.Add(".7z");
			setModExtensions.Add(".zip");
			setModExtensions.Add(".rar");
			return setModExtensions;
		}

		/// <summary>
		/// Handles the <see cref="ModManager.UpdateCheckStarted"/> event of the ModManager.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ModManager_UpdateCheckStarted(object sender, EventArgs<IBackgroundTask> e)
		{
			UpdatingMods(sender, e);
		}

		/// <summary>
		/// Handles the <see cref="ModManager.UpdateCategoriesCheckStarted"/> event of the ModManager.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ModManager_UpdateCategoriesCheckStarted(object sender, EventArgs<IBackgroundTask> e)
		{
			UpdatingCategories(sender, e);
		}

		

		/// <summary>
		/// The Automatic Download.
		/// </summary>
		public void AutomaticDownload(List<string> p_lstMissingMods, ProfileManager p_pmProfileManager)
		{
			if (!ModRepository.IsOffline)
				AutomaticDownloading(this, new EventArgs<IBackgroundTask>(ModManager.AutomaticDownload(p_lstMissingMods, p_pmProfileManager, ConfirmUpdaterAction, ConfirmFileOverwrite)));
			else
			{
				ModManager.Login();
				ModManager.AsyncAutomaticDownload(p_lstMissingMods, p_pmProfileManager, ConfirmUpdaterAction, ConfirmFileOverwrite);
			}
		}

		private void ModManager_AutomaticDownloadStarted(object sender, EventArgs<IBackgroundTask> e)
		{
			AutomaticDownloading(sender, e);
		}

        public void SaveModLoadOrder()
        {
            ModManager.GameMode.SortMods(ReinstallMod, ActiveMods);
        }
	}
}
