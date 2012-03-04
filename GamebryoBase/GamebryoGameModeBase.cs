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
using Nexus.Client.Games.Tools;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.PluginManagement.InstallationLog;
using Nexus.Client.PluginManagement.OrderLog;
using Nexus.Client.Settings.UI;
using Nexus.Client.Updating;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.Games.Gamebryo
{
	/// <summary>
	/// Provides information required for the programme to manage Gamebryo based game plugins and mods.
	/// </summary>
	public abstract class GamebryoGameModeBase : GameModeBase
	{
		private string[] m_strCriticalPlugins = null;
		private GamebryoPluginFactory m_pgfPluginFactory = null;
		private GamebryoActivePluginLogSerializer m_apsActivePluginLogSerializer = null;
		private GamebryoPluginDiscoverer m_pdvPluginDiscoverer = null;
		private GamebryoPluginOrderLogSerializer m_posPluginOrderSerializer = null;
		private GamebryoPluginOrderValidator m_povPluginOrderValidator = null;

		#region Properties

		/// <summary>
		/// Gets the list of possible script extender executable files for the game.
		/// </summary>
		/// <value>The list of possible script extender executable files for the game.</value>
		protected abstract string[] ScriptExtenderExecutables { get; }

		/// <summary>
		/// Gets the display name of the game mode.
		/// </summary>
		/// <value>The display name of the game mode.</value>
		public override abstract string Name { get; }

		/// <summary>
		/// Gets the unique id of the game mode.
		/// </summary>
		/// <value>The unique id of the game mode.</value>
		public override abstract string ModeId { get; }

		/// <summary>
		/// Gets the theme to use for this game mode.
		/// </summary>
		/// <value>The theme to use for this game mode.</value>
		public override abstract Theme ModeTheme { get; }

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
		/// Gets the path to the per user Fallout 3 data.
		/// </summary>
		/// <value>The path to the per user Fallout 3 data.</value>
		public abstract string UserGameDataPath { get; }

		/// <summary>
		/// Gets the directory where Fallout 3 plugins are installed.
		/// </summary>
		/// <value>The directory where Fallout 3 plugins are installed.</value>
		public string PluginDirectory
		{
			get
			{
				string strPath = Path.Combine(GameModeEnvironmentInfo.InstallationPath, "Data");
				if (!Directory.Exists(strPath))
					Directory.CreateDirectory(strPath);
				return strPath;
			}
		}

		/// <summary>
		/// Gets the paths of the INI files that can be edited while managing Fallout 3.
		/// </summary>
		/// <value>The paths of the INI files that can be edited while managing Fallout 3.</value>
		public GamebryoSettingsFiles SettingsFiles { get; private set; }

		/// <summary>
		/// Gets the game launcher for the game mode.
		/// </summary>
		/// <value>The game launcher for the game mode.</value>
		public override abstract IGameLauncher GameLauncher { get; }

		/// <summary>
		/// Gets the tool launcher for the game mode.
		/// </summary>
		/// <value>The tool launcher for the game mode.</value>
		public override abstract IToolLauncher GameToolLauncher { get; }

		/// <summary>
		/// Gets the BOSS plugin sorter.
		/// </summary>
		/// <value>The BOSS plugin sorter.</value>
		protected BossSorter BossSorter { get; private set; }

		/// <summary>
		/// Gets the list of critical plugin filenames, ordered by load order.
		/// </summary>
		/// <value>The list of critical plugin filenames, ordered by load order.</value>
		protected abstract string[] OrderedCriticalPluginFilenames { get; }

		/// <summary>
		/// Gets the list of critical plugin names, ordered by load order.
		/// </summary>
		/// <value>The list of critical plugin names, ordered by load order.</value>
		public override string[] OrderedCriticalPluginNames
		{
			get
			{
				if (m_strCriticalPlugins == null)
				{
					m_strCriticalPlugins = new string[OrderedCriticalPluginFilenames.Length];
					for (Int32 i = OrderedCriticalPluginFilenames.Length - 1; i >= 0; i--)
						m_strCriticalPlugins[i] = Path.Combine(PluginDirectory, OrderedCriticalPluginFilenames[i]).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
				}
				return m_strCriticalPlugins;
			}
		}

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
				m_pgfPluginFactory = new GamebryoPluginFactory(PluginDirectory);
			return m_pgfPluginFactory;
		}

		/// <summary>
		/// Gets the serailizer that serializes and deserializes the list of active plugins
		/// for this game mode.
		/// </summary>
		/// <returns>The serailizer that serializes and deserializes the list of active plugins
		/// for this game mode.</returns>
		public override IActivePluginLogSerializer GetActivePluginLogSerializer()
		{
			if (m_apsActivePluginLogSerializer == null)
				m_apsActivePluginLogSerializer = new GamebryoActivePluginLogSerializer(BossSorter);
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
		/// <returns>The given path, adjusted to be relative to the installation path of the game mode.</returns>
		public override string GetModFormatAdjustedPath(IModFormat p_mftModFormat, string p_strPath)
		{
			if (p_mftModFormat.Id.Equals("FOMod") || p_mftModFormat.Id.Equals("OMod"))
				return Path.Combine("Data", p_strPath ?? "");
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
