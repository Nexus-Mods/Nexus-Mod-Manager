using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ChinhDo.Transactions;
using Nexus.Client.Games.Cyberpunk2077.Settings.UI;
using Nexus.Client.Games.Cyberpunk2077.Tools;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.PluginManagement.InstallationLog;
using Nexus.Client.PluginManagement.OrderLog;
using Nexus.Client.Settings.UI;
using Nexus.Client.Games.Tools;
using Nexus.Client.Updating;
using Nexus.Client.Util;
using System.Diagnostics;

namespace Nexus.Client.Games.Cyberpunk2077
{
	/// <summary>
	/// Provides information required for the program to manage Cyberpunk2077 game's plugins and mods.
	/// </summary>
	public class Cyberpunk2077GameMode : GameModeBase
	{
		private Cyberpunk2077GameModeDescriptor m_gmdGameModeInfo = null;
		private Cyberpunk2077Launcher m_glnGameLauncher = null;
		private Cyberpunk2077ToolLauncher m_gtlToolLauncher = null;
		private bool? _isOldGameVersion = null;

		#region Properties

		private bool IsOldGameVersion
		{
			get
			{
				if (!_isOldGameVersion.HasValue)
				{
					Version gameVersion = GameVersion;
					if (gameVersion != null)
						_isOldGameVersion = gameVersion < new Version(1, 2);
				}

				return (_isOldGameVersion.HasValue ? _isOldGameVersion.Value : false);
			}
		}


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
						return new Version(FileVersionInfo.GetVersionInfo(strFullPath).ProductVersion.Replace(", ", "."));
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
				return null;
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
				return null;
			}
		}

		/// <summary>
		/// Gets the path to the per user Cyberpunk2077 data.
		/// </summary>
		/// <value>The path to the per user Cyberpunk2077 data.</value>
		public string UserGameDataPath
		{
			get
			{
				return GameModeEnvironmentInfo.InstallationPath;
			}
		}

		/// <summary>
		/// Gets the game launcher for the game mode.
		/// </summary>
		/// <value>The game launcher for the game mode.</value>
		public override IGameLauncher GameLauncher
		{
			get
			{
				if (m_glnGameLauncher == null)
					m_glnGameLauncher = new Cyberpunk2077Launcher(this, EnvironmentInfo);
				return m_glnGameLauncher;
			}
		}

		/// <summary>
		/// Gets the tool launcher for the game mode.
		/// </summary>
		/// <value>The tool launcher for the game mode.</value>
		public override IToolLauncher GameToolLauncher
		{
			get
			{
				if (m_gtlToolLauncher == null)
					m_gtlToolLauncher = new Cyberpunk2077ToolLauncher(this, EnvironmentInfo);
				return m_gtlToolLauncher;
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
				return false;
			}
		}

		/// <summary>
		/// Gets the default game categories.
		/// </summary>
		/// <value>The default game categories stored in the resource file.</value>
		public override string GameDefaultCategories
		{
			get
			{
				return Properties.Resources.Categories;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's environment info.</param>
		/// <param name="p_futFileUtility">The file utility class to be used by the game mode.</param>
		public Cyberpunk2077GameMode(IEnvironmentInfo p_eifEnvironmentInfo, FileUtil p_futFileUtility)
			: base(p_eifEnvironmentInfo)
		{
			SettingsGroupViews = new List<ISettingsGroupView>();
			GeneralSettingsGroup gsgGeneralSettings = new GeneralSettingsGroup(p_eifEnvironmentInfo, this);
			((List<ISettingsGroupView>)SettingsGroupViews).Add(new GeneralSettingsPage(gsgGeneralSettings));
		}

		#endregion

		#region Initialization

		#endregion

		#region Plugin Management

		/// <summary>
		/// Gets the factory that builds plugins for this game mode.
		/// </summary>
		/// <returns>The factory that builds plugins for this game mode.</returns>
		public override IPluginFactory GetPluginFactory()
		{
			return null;
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
			return null;
		}

		/// <summary>
		/// Gets the discoverer to use to find the plugins managed by this game mode.
		/// </summary>
		/// <returns>The discoverer to use to find the plugins managed by this game mode.</returns>
		public override IPluginDiscoverer GetPluginDiscoverer()
		{
			return null;
		}

		/// <summary>
		/// Gets the serializer that serializes and deserializes the plugin order
		/// for this game mode.
		/// </summary>
		/// <returns>The serailizer that serializes and deserializes the plugin order
		/// for this game mode.</returns>
		public override IPluginOrderLogSerializer GetPluginOrderLogSerializer()
		{
			return null;
		}

		/// <summary>
		/// Gets the object that validates plugin order for this game mode.
		/// </summary>
		/// <returns>The object that validates plugin order for this game mode.</returns>
		public override IPluginOrderValidator GetPluginOrderValidator()
		{
			return null;
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
			return null;
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
			return null;
		}

		#endregion

		/// <summary>
		/// Gets the updaters used by the game mode.
		/// </summary>
		/// <returns>The updaters used by the game mode.</returns>
		public override IEnumerable<IUpdater> GetUpdaters()
		{
			return null;
		}

		/// <summary>
		/// Creates a game mode descriptor for the current game mode.
		/// </summary>
		/// <returns>A game mode descriptor for the current game mode.</returns>
		protected override IGameModeDescriptor CreateGameModeDescriptor()
		{
			if (m_gmdGameModeInfo == null)
				m_gmdGameModeInfo = new Cyberpunk2077GameModeDescriptor(EnvironmentInfo);
			return m_gmdGameModeInfo;
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
			return p_strPath;
		}

		/// <summary>
		/// Adjusts the given path to be relative to the installation path of the game mode.
		/// </summary>
		/// <remarks>
		/// This is basically a hack to allow older FOMods to work. Older FOMods assumed
		/// the installation path of Fallout games to be &lt;games>/data, but this new manager specifies
		/// the installation path to be &lt;games>. This breaks the older FOMods, so this method can detect
		/// the older FOMods (or other mod formats that needs massaging), and adjusts the given path
		/// to be relative to the new instaalation path to make things work.
		/// </remarks>
		/// <param name="p_mftModFormat">The mod format for which to adjust the path.</param>
		/// <param name="p_strPath">The path to adjust.</param>
		/// <param name="p_modMod">The mod.</param>
		/// <param name="p_booIgnoreIfPresent">Whether to ignore the path if the specific root is already present</param>
		/// <returns>The given path, adjusted to be relative to the installation path of the game mode.</returns>
		public override string GetModFormatAdjustedPath(IModFormat p_mftModFormat, string p_strPath, IMod p_modMod, bool p_booIgnoreIfPresent)
		{
			string strPath = p_strPath;

			if (strPath.StartsWith("bin", StringComparison.InvariantCultureIgnoreCase) || strPath.StartsWith("x64", StringComparison.InvariantCultureIgnoreCase))
			{
				if (strPath.StartsWith("x64", StringComparison.InvariantCultureIgnoreCase))
				{
					strPath = Path.Combine("bin", strPath);
				}
			}
			else if (!strPath.StartsWith("archive", StringComparison.InvariantCultureIgnoreCase) && !strPath.StartsWith("red4ext", StringComparison.InvariantCultureIgnoreCase) 
				&& !strPath.StartsWith("mods", StringComparison.InvariantCultureIgnoreCase))
			{
				string modPath = string.Empty;

				if (!IsOldGameVersion)
				{
					strPath = strPath.Replace("patch", "mod");
					modPath = "mod";
				}
				else
					modPath = "patch";

				if (strPath.StartsWith("pc", StringComparison.InvariantCultureIgnoreCase))
				{
					strPath = Path.Combine("archive", strPath);
				}
				else if (strPath.StartsWith(modPath, StringComparison.InvariantCultureIgnoreCase))
				{
					strPath = Path.Combine("archive", "pc", strPath);
				}
				if (strPath.StartsWith("engine", StringComparison.InvariantCultureIgnoreCase) || strPath.StartsWith("r6", StringComparison.InvariantCultureIgnoreCase))
				{
					// do nothing
				}
				else
				{
					strPath = Path.Combine("archive", "pc", modPath, strPath);
				}
			}
			else
			{
				if (!IsOldGameVersion)
				{
					strPath = strPath.Replace("patch", "mod");
				}
			}

			return strPath;
		}

		static string CleanInput(string p_strIn)
		{
			// Replace invalid characters with empty strings. 
			try
			{
				return Regex.Replace(p_strIn, @"[^\w\.-]", "",
									 RegexOptions.None, TimeSpan.FromSeconds(1.5));
			}
			// If we timeout when replacing invalid characters,
			// we should return Empty. 
			catch (RegexMatchTimeoutException)
			{
				return string.Empty;
			}
		}

		/// <summary>
		/// Checks whether the file's type requires a hardlink for the current game mode.
		/// </summary>
		/// <returns>Whether the file's type requires a hardlink for the current game mode.</returns>
		/// <param name="p_strFileName">The filename.</param>
		public override bool HardlinkRequiredFilesType(string p_strFileName)
		{
			string strFileType = Path.GetExtension(p_strFileName);
			return (strFileType.Equals(".lua", StringComparison.InvariantCultureIgnoreCase) || strFileType.Equals(".asi", StringComparison.InvariantCultureIgnoreCase) || strFileType.Equals(".toml", StringComparison.InvariantCultureIgnoreCase)
				|| strFileType.Equals(".mp3", StringComparison.InvariantCultureIgnoreCase) || strFileType.Equals(".BGSM", StringComparison.InvariantCultureIgnoreCase)
				|| strFileType.Equals(".BGEM", StringComparison.InvariantCultureIgnoreCase) || strFileType.Equals(".wav", StringComparison.InvariantCultureIgnoreCase) || strFileType.Equals(".ogg", StringComparison.InvariantCultureIgnoreCase)
				|| strFileType.Equals(".xwm", StringComparison.InvariantCultureIgnoreCase));
		}

		/// <summary>
		/// Checks whether the file type is not compatible with the virtual install.
		/// </summary>
		/// <param name="fileExtension">The file extension starting with a "."</param>
		/// <returns>True if it requires a real file copy.</returns>
		public override bool RealFileRequired(string fileExtension)
		{
			return (fileExtension.Equals(".exe", StringComparison.InvariantCultureIgnoreCase) || fileExtension.Equals(".jar", StringComparison.InvariantCultureIgnoreCase) || fileExtension.Equals(".dll", StringComparison.InvariantCultureIgnoreCase)
					|| fileExtension.Equals(".asi", StringComparison.InvariantCultureIgnoreCase) || fileExtension.Equals(".lua", StringComparison.InvariantCultureIgnoreCase) || fileExtension.Equals(".toml", StringComparison.InvariantCultureIgnoreCase));
			//|| fileExtension.Equals(".json", StringComparison.InvariantCultureIgnoreCase) || fileExtension.Equals(".sqlite3", StringComparison.InvariantCultureIgnoreCase)
		}

		/// <summary>
		/// Disposes of the unamanged resources.
		/// </summary>
		/// <param name="p_booDisposing">Whether the method is being called from the <see cref="IDisposable.Dispose()"/> method.</param>
		protected override void Dispose(bool p_booDisposing)
		{
		}
	}
}
