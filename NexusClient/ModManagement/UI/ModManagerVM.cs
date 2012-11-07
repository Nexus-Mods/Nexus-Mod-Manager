using System;
using System.Collections.Generic;
using System.IO;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Commands.Generic;
using Nexus.Client.Games;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;
using Nexus.Client.Settings;
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

		#endregion

		/// <summary>
		/// Raised when the deletion of a mod file needs to be confirmed.
		/// </summary>
		public Func<IMod, bool> ConfirmModFileDeletion = delegate { return false; };

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
		public Command<IMod> ActivateModCommand { get; private set; }

		/// <summary>
		/// Gets the command to deactivate a mod.
		/// </summary>
		/// <remarks>
		/// The commands takes an argument describing the mod to be deactivated.
		/// </remarks>
		/// <value>The command to deactivate a mod.</value>
		public Command<IMod> DeactivateModCommand { get; private set; }

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
		protected ModManager ModManager { get; private set; }

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
				return ModManager.RepositoryOfflineMode;
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
		public ModManagerVM(ModManager p_mmdModManager, ISettings p_setSettings, Theme p_thmTheme)
		{
			ModManager = p_mmdModManager;
			Settings = p_setSettings;
			CurrentTheme = p_thmTheme;

			AddModCommand = new Command<string>("Add Mod", "Adds a mod to the manager.", AddMod);
			DeleteModCommand = new Command<IMod>("Delete Mod", "Deletes the selected mod.", DeleteMod);
			ActivateModCommand = new Command<IMod>("Activate Mod", "Activates the selected mod.", ActivateMod);
			DeactivateModCommand = new Command<IMod>("Deactivate Mod", "Deactivates the selected mod.", DeactivateMod);
			TagModCommand = new Command<IMod>("Tag Mod", "Gets missing mod info.", TagMod);
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
		protected void DeleteMod(IMod p_modMod)
		{
			if (ConfirmModFileDeletion(p_modMod))
			{
				IBackgroundTaskSet btsInstall = ModManager.DeleteMod(p_modMod);
				DeletingMod(this, new EventArgs<IBackgroundTaskSet>(btsInstall));
			}
		}

		#endregion

		#region Mod Activation/Deactivation

		/// <summary>
		/// Activates the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod to activate.</param>
		protected void ActivateMod(IMod p_modMod)
		{
			p_modMod.InstallDate = DateTime.Now.ToString();
			IBackgroundTaskSet btsInstall = ModManager.ActivateMod(p_modMod, ConfirmModUpgrade, ConfirmItemOverwrite);
			if (btsInstall != null)
				ChangingModActivation(this, new EventArgs<IBackgroundTaskSet>(btsInstall));
		}

		/// <summary>
		/// Deactivates the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod to deactivate.</param>
		protected void DeactivateMod(IMod p_modMod)
		{
			p_modMod.InstallDate = null;
			IBackgroundTaskSet btsUninstall = ModManager.DeactivateMod(p_modMod);
			ChangingModActivation(this, new EventArgs<IBackgroundTaskSet>(btsUninstall));
		}

		#endregion

		#region Mod Tagging

		/// <summary>
		/// Tags the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod to tag.</param>
		protected void TagMod(IMod p_modMod)
		{
			ModTaggerVM mtgTagger = new ModTaggerVM(ModManager.GetModTagger(), p_modMod, Settings, CurrentTheme);
			TaggingMod(this, new EventArgs<ModTaggerVM>(mtgTagger));
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

		/// <summary>
		/// Updates the mod's last known version.
		/// </summary>
		/// <param name="p_modMod">The mod whose last known version is to be updated.</param>
		/// <param name="p_strModLastVersion">The last known version.</param>
		public void UpdateModLastVersion(IMod p_modMod, string p_strModLastVersion)
		{
			ModInfo mifNewInfo = new ModInfo(p_modMod);
			mifNewInfo.LastKnownVersion = p_strModLastVersion;
			p_modMod.UpdateInfo(mifNewInfo, true);
		}

		#endregion

		#region Mod Updating

		public string CheckForUpdates()
		{
			return ModManager.CheckForUpdates();
		}

		#endregion

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
	}
}
