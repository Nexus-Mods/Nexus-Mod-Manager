using System;
using System.Collections.Generic;
using System.IO;
using ChinhDo.Transactions;
using Nexus.Client.Games.Gamebryo.ModManagement;
using Nexus.Client.Games.Gamebryo.PluginManagement;
using Nexus.Client.Games.Gamebryo.PluginManagement.Boss;
using Nexus.Client.Games.Gamebryo.PluginManagement.InstallationLog;
using Nexus.Client.Games.Gamebryo.PluginManagement.OrderLog;
using Nexus.Client.Games.Gamebryo.Settings;
using Nexus.Client.Games.Gamebryo.Settings.UI;
using Nexus.Client.Games.Gamebryo.Updating;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.PluginManagement.InstallationLog;
using Nexus.Client.PluginManagement.OrderLog;
using Nexus.Client.Settings.UI;
using Nexus.Client.Updating;
using Nexus.Client.Util;

namespace Nexus.Client.Games.Gamebryo
{
	/// <summary>
	/// Provides information required for the programme to manage Gamebryo based game plugins and mods.
	/// </summary>
	public abstract class GamebryoGameModeBase : GameModeBase
	{
		private GamebryoPluginFactory m_pgfPluginFactory = null;
		private GamebryoActivePluginLogSerializer m_apsActivePluginLogSerializer = null;
		private GamebryoPluginDiscoverer m_pdvPluginDiscoverer = null;
		private GamebryoPluginOrderLogSerializer m_posPluginOrderSerializer = null;
		private GamebryoPluginOrderValidator m_povPluginOrderValidator = null;
		private const Int32 m_intMaxAllowedPlugins = 255;

		#region Properties

		/// <summary>
		/// Gets the list of possible script extender executable files for the game.
		/// </summary>
		/// <value>The list of possible script extender executable files for the game.</value>
		protected abstract string[] ScriptExtenderExecutables { get; }

		/// <summary>
		/// Gets the version of the installed game.
		/// </summary>
		/// <value>The version of the installed game.</value>
		public override Version GameVersion
		{
			get
			{
				string strFullPath = null;
				foreach (string strExecutable in GameExecutables)
				{
					strFullPath = Path.Combine(GameModeEnvironmentInfo.InstallationPath, strExecutable);
					if (File.Exists(strFullPath))
						return new Version(System.Diagnostics.FileVersionInfo.GetVersionInfo(strFullPath).FileVersion.Replace(", ", "."));
				}
				return null;
			}
		}

		/// <summary>
		/// Gets a list of paths to which the game mode writes.
		/// </summary>
		/// <value>A list of paths to which the game mode writes.</value>
		public override IEnumerable<string> WritablePaths
		{
			get
			{
				foreach (string strPlugin in Directory.GetFiles(PluginDirectory, "*.es?", SearchOption.TopDirectoryOnly))
					yield return strPlugin;
				foreach (string strPath in SettingsFiles)
					yield return strPath;
			}
		}

		/// <summary>
		/// Gets whether the game mode uses plugins.
		/// </summary>
		/// <remarks>
		/// This indicates whether the game mode used plugins that are
		/// installed by mods, or simply used mods, without
		/// plugins.
		/// 
		/// In games that use mods only, the installation of a mods package
		/// is sufficient to add the functionality to the game. The game
		/// will often have no concept of managable game modifications.
		/// 
		/// In games that use plugins, mods can install files that directly
		/// affect the game (similar to the mod-free use case), but can also
		/// install plugins that can be managed (for example activated/reordered)
		/// after the mod is installed.
		/// </remarks>
		/// <value>Whether the game mode uses plugins.</value>
		public override bool UsesPlugins
		{
			get
			{
				return true;
			}
		}

		/// <summary>
		/// Gets the installed version of the script extender.
		/// </summary>
		/// <remarks>
		/// <c>null</c> is returned if the script extender is not installed.
		/// </remarks>
		/// <value>The installed version of the script extender.</value>
		public virtual Version ScriptExtenderVersion
		{
			get
			{
				string strFullPath = null;
				foreach (string strExecutable in ScriptExtenderExecutables)
				{
					strFullPath = Path.Combine(GameModeEnvironmentInfo.InstallationPath, strExecutable);
					if (File.Exists(strFullPath))
						return new Version(System.Diagnostics.FileVersionInfo.GetVersionInfo(strFullPath).FileVersion.Replace(", ", "."));
				}
				return null;
			}
		}

		/// <summary>
		/// Gets the path to the per user game data.
		/// </summary>
		/// <value>The path to the per user game data.</value>
		public abstract string UserGameDataPath { get; }
        		
		/// <summary>
		/// Gets the max allowed number of active plugins.
		/// </summary>
		/// <value>The max allowed number of active plugins (0 if there's no limit).</value>
		public override Int32 MaxAllowedActivePluginsCount
		{
			get
			{
				return m_intMaxAllowedPlugins;
			}
		}

		/// <summary>
		/// Gets the paths of the INI files that can be edited while managing the game.
		/// </summary>
		/// <value>The paths of the INI files that can be edited while managing the game.</value>
		public GamebryoSettingsFiles SettingsFiles { get; private set; }

		/// <summary>
		/// Gets the BOSS plugin sorter.
		/// </summary>
		/// <value>The BOSS plugin sorter.</value>
		protected BossSorter BossSorter { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's environment info.</param>
		/// <param name="p_futFileUtility">The file utility class to be used by the game mode.</param>
		public GamebryoGameModeBase(IEnvironmentInfo p_eifEnvironmentInfo, FileUtil p_futFileUtility)
			: base(p_eifEnvironmentInfo)
		{
			SettingsFiles = CreateSettingsFileContainer();
			SetupSettingsFiles();
			SettingsGroupViews = new List<ISettingsGroupView>();
			GeneralSettingsGroup gsgGeneralSettings = new GeneralSettingsGroup(p_eifEnvironmentInfo, this);
			((List<ISettingsGroupView>)SettingsGroupViews).Add(new GeneralSettingsPage(gsgGeneralSettings));

			string strPath = p_eifEnvironmentInfo.ApplicationPersonalDataFolderPath;
			strPath = Path.Combine(Path.Combine(strPath, "boss"), "masterlist.txt");
			BossSorter = new BossSorter(p_eifEnvironmentInfo, this, p_futFileUtility, strPath);
		}

		#endregion

		#region Initialization

		/// <summary>
		/// Instantiates the container to use to store the list of settings files.
		/// </summary>
		/// <returns>The container to use to store the list of settings files.</returns>
		protected abstract GamebryoSettingsFiles CreateSettingsFileContainer();

		/// <summary>
		/// Adds the settings files to the game mode's list.
		/// </summary>
		protected virtual void SetupSettingsFiles()
		{
			SettingsFiles.RendererFilePath = Path.Combine(UserGameDataPath, "RendererInfo.txt");
			SettingsFiles.PluginsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), String.Format("{0}/plugins.txt", ModeId));
			if (!File.Exists(SettingsFiles.PluginsFilePath))
			{
				string strDirectory = Path.GetDirectoryName(SettingsFiles.PluginsFilePath);
				if (!Directory.Exists(strDirectory))
					Directory.CreateDirectory(strDirectory);
				File.Create(SettingsFiles.PluginsFilePath).Close();
			}
		}

		#endregion

		#region Plugin Management

		/// <summary>
		/// Gets the factory that builds plugins for this game mode.
		/// </summary>
		/// <returns>The factory that builds plugins for this game mode.</returns>
		public override IPluginFactory GetPluginFactory()
		{
			if (m_pgfPluginFactory == null)
				m_pgfPluginFactory = new GamebryoPluginFactory(PluginDirectory, BossSorter);
			return m_pgfPluginFactory;
		}

		/// <summary>
		/// Gets the serailizer that serializes and deserializes the list of active plugins
		/// for this game mode.
		/// </summary>
		/// <param name="p_polPluginOrderLog">The <see cref="IPluginOrderLog"/> tracking plugin order for the current game mode.</param>
		/// <returns>The serailizer that serializes and deserializes the list of active plugins
		/// for this game mode.</returns>
		public override IActivePluginLogSerializer GetActivePluginLogSerializer(IPluginOrderLog p_polPluginOrderLog)
		{
			if (m_apsActivePluginLogSerializer == null)
				m_apsActivePluginLogSerializer = new GamebryoActivePluginLogSerializer(this, p_polPluginOrderLog, BossSorter);
			return m_apsActivePluginLogSerializer;
		}

		/// <summary>
		/// Gets the discoverer to use to find the plugins managed by this game mode.
		/// </summary>
		/// <returns>The discoverer to use to find the plugins managed by this game mode.</returns>
		public override IPluginDiscoverer GetPluginDiscoverer()
		{
			if (m_pdvPluginDiscoverer == null)
				m_pdvPluginDiscoverer = new GamebryoPluginDiscoverer(PluginDirectory);
			return m_pdvPluginDiscoverer;
		}

		/// <summary>
		/// Gets the serializer that serializes and deserializes the plugin order
		/// for this game mode.
		/// </summary>
		/// <returns>The serailizer that serializes and deserializes the plugin order
		/// for this game mode.</returns>
		public override IPluginOrderLogSerializer GetPluginOrderLogSerializer()
		{
			if (m_posPluginOrderSerializer == null)
				m_posPluginOrderSerializer = new GamebryoPluginOrderLogSerializer(BossSorter);
			return m_posPluginOrderSerializer;
		}

		/// <summary>
		/// Gets the object that validates plugin order for this game mode.
		/// </summary>
		/// <returns>The object that validates plugin order for this game mode.</returns>
		public override IPluginOrderValidator GetPluginOrderValidator()
		{
			if (m_povPluginOrderValidator == null)
				m_povPluginOrderValidator = new GamebryoPluginOrderValidator(OrderedCriticalPluginNames);
			return m_povPluginOrderValidator;
		}

		#endregion

		#region Game Specific Value Management

		/// <summary>
		/// Gets the installer to use to install game specific values.
		/// </summary>
		/// <param name="p_modMod">The mod being installed.</param>
		/// <param name="p_ilgInstallLog">The install log to use to log the installation of the game specific values.</param>
		/// <param name="p_tfmFileManager">The transactional file manager to use to interact with the file system.</param>
		/// <returns>The installer to use to manage game specific values, or <c>null</c> if the game mode does not
		/// install any game specific values.</returns>
		/// <param name="p_futFileUtility">The file utility class.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		public override IGameSpecificValueInstaller GetGameSpecificValueInstaller(IMod p_modMod, IInstallLog p_ilgInstallLog, TxFileManager p_tfmFileManager, FileUtil p_futFileUtility, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			return new GamebryoGameSpecificValueInstaller(p_modMod, GameModeEnvironmentInfo, p_ilgInstallLog, p_tfmFileManager, p_futFileUtility, p_dlgOverwriteConfirmationDelegate);
		}

		/// <summary>
		/// Gets the installer to use to upgrade game specific values.
		/// </summary>
		/// <param name="p_modMod">The mod being upgraded.</param>
		/// <param name="p_ilgInstallLog">The install log to use to log the installation of the game specific values.</param>
		/// <param name="p_tfmFileManager">The transactional file manager to use to interact with the file system.</param>
		/// <returns>The installer to use to manage game specific values, or <c>null</c> if the game mode does not
		/// install any game specific values.</returns>
		/// <param name="p_futFileUtility">The file utility class.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		public override IGameSpecificValueInstaller GetGameSpecificValueUpgradeInstaller(IMod p_modMod, IInstallLog p_ilgInstallLog, TxFileManager p_tfmFileManager, FileUtil p_futFileUtility, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			return new GamebryoGameSpecificValueInstaller(p_modMod, GameModeEnvironmentInfo, p_ilgInstallLog, p_tfmFileManager, p_futFileUtility, p_dlgOverwriteConfirmationDelegate);
		}

		#endregion

		/// <summary>
		/// Gets the updaters used by the game mode.
		/// </summary>
		/// <returns>The updaters used by the game mode.</returns>
		public override IEnumerable<IUpdater> GetUpdaters()
		{
			BossUpdater bupUpdater = new BossUpdater(EnvironmentInfo, BossSorter);
			yield return bupUpdater;
		}

		/// <summary>
		/// Adjusts the given path to be relative to the installation path of the game mode.
		/// </summary>
		/// <remarks>
		/// This is basically a hack to allow older FOMod/OMods to work. Older FOMods assumed
		/// the installation path of Fallout games to be &lt;games>/data, but this new manager specifies
		/// the installation path to be &lt;games>. This breaks the older FOMods, so this method can detect
		/// the older FOMods (or other mod formats that needs massaging), and adjusts the given path
		/// to be relative to the new instaalation path to make things work.
		/// </remarks>
		/// <param name="p_mftModFormat">The mod format for which to adjust the path.</param>
		/// <param name="p_strPath">The path to adjust</param>
		/// <param name="p_booIgnoreIfPresent">Whether to ignore the path if the specific root is already present</param>
		/// <returns>The given path, adjusted to be relative to the installation path of the game mode.</returns>
		public override string GetModFormatAdjustedPath(IModFormat p_mftModFormat, string p_strPath, bool p_booIgnoreIfPresent)
		{
			if ((p_mftModFormat != null) && (p_mftModFormat.Id.Equals("FOMod") || p_mftModFormat.Id.Equals("OMod")))
			{
				if (p_booIgnoreIfPresent && !String.IsNullOrEmpty(p_strPath) && p_strPath.StartsWith("Data" + Path.DirectorySeparatorChar, System.StringComparison.InvariantCultureIgnoreCase))
					return p_strPath.Substring(5);
				else if (!p_booIgnoreIfPresent)
					return Path.Combine("Data", p_strPath ?? "");
			}
			else if (p_mftModFormat == null)
			{
				if (p_booIgnoreIfPresent && !String.IsNullOrEmpty(p_strPath) && p_strPath.StartsWith("Data" + Path.DirectorySeparatorChar, System.StringComparison.InvariantCultureIgnoreCase))
					return p_strPath.Substring(5);
				else if (!p_booIgnoreIfPresent)
					return Path.Combine("Data", p_strPath ?? "");
			}
			return p_strPath;
		}

		/// <summary>
		/// Disposes of the unamanged resources.
		/// </summary>
		/// <param name="p_booDisposing">Whether the method is being called from the <see cref="IDisposable.Dispose()"/> method.</param>
		protected override void Dispose(bool p_booDisposing)
		{
			if (BossSorter != null)
				BossSorter.Dispose();
		}
	}
}
