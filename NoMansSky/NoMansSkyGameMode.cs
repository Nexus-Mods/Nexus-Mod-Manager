using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ChinhDo.Transactions;
using Nexus.Client.Games.NoMansSky.Tools;
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
using Nexus.Client.Util.Collections;
using System.Windows.Forms;

namespace Nexus.Client.Games.NoMansSky
{
    /// <summary>
    /// Provides information required for the program to manage NoMansSky game's plugins and mods.
    /// </summary>
    public class NoMansSkyGameMode : GameModeBase
	{
		private NoMansSkyGameModeDescriptor m_gmdGameModeInfo = null;
		private NoMansSkyLauncher m_glnGameLauncher = null;
		private NoMansSkyToolLauncher m_gtlToolLauncher = null;

		#region Properties

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
						return new Version(FileVersionInfo.GetVersionInfo(strFullPath).FileVersion.Replace(", ", "."));
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
		/// Gets the path to the per user NoMansSky data.
		/// </summary>
		/// <value>The path to the per user NoMansSky data.</value>
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
					m_glnGameLauncher = new NoMansSkyLauncher(this, EnvironmentInfo);
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
					m_gtlToolLauncher = new NoMansSkyToolLauncher(this, EnvironmentInfo);
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

        public override Boolean RequiresSpecialFileInstallation
        {
            get
            {
                return true;
            }
        }

        public override bool UsesModLoadOrder
        {
            get
            {
                return true;
            }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// A simple constructor that initializes the object with the given values.
        /// </summary>
        /// <param name="p_eifEnvironmentInfo">The application's environment info.</param>
        /// <param name="p_futFileUtility">The file utility class to be used by the game mode.</param>
        public NoMansSkyGameMode(IEnvironmentInfo p_eifEnvironmentInfo, FileUtil p_futFileUtility)
			: base(p_eifEnvironmentInfo)
		{
			SettingsGroupViews = new List<ISettingsGroupView>();
			GeneralSettingsGroup gsgGeneralSettings = new GeneralSettingsGroup(p_eifEnvironmentInfo, this);
			((List<ISettingsGroupView>)SettingsGroupViews).Add(new Settings.UI.GeneralSettingsPage(gsgGeneralSettings));
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
				m_gmdGameModeInfo = new NoMansSkyGameModeDescriptor(EnvironmentInfo);
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
		/// to be relative to the new installation path to make things work.
		/// </remarks>
		/// <param name="p_mftModFormat">The mod format for which to adjust the path.</param>
		/// <param name="p_strPath">The path to adjust</param>
		/// <param name="p_booIgnoreIfPresent">Whether to ignore the path if the specific root is already present</param>
		/// <returns>The given path, adjusted to be relative to the installation path of the game mode.</returns>
		public override string GetModFormatAdjustedPath(IModFormat p_mftModFormat, string p_strPath, bool p_booIgnoreIfPresent)
		{
			string strPath = p_strPath;
			string strFileType = Path.GetExtension(strPath);
            string[] strSpecialFiles = new[] { "opengl32.dll", "NMSE_steam.dll", "NMSE_Core_1_0.dll" };
            
            if (strFileType.Equals(".pak", StringComparison.InvariantCultureIgnoreCase))
            {
				if (strPath.StartsWith("PCBANKS", StringComparison.InvariantCultureIgnoreCase))
				{
					if (strPath.StartsWith("PCBANKS" + Path.DirectorySeparatorChar + "MODS", StringComparison.InvariantCultureIgnoreCase))
						strPath = Path.Combine("GAMEDATA", strPath);
					else
						strPath = strPath.Replace("PCBANKS", Path.Combine("GAMEDATA", "PCBANKS", "MODS"));
				}             
				else if (strPath.StartsWith("MODS", StringComparison.InvariantCultureIgnoreCase))
					strPath = Path.Combine("GAMEDATA", "PCBANKS", strPath);
				else
					strPath = Path.Combine("GAMEDATA", "PCBANKS", "MODS", Path.GetFileName(strPath));
            }
            else if(strSpecialFiles.Any((s) => Path.GetFileName(strPath).Equals(s, StringComparison.InvariantCultureIgnoreCase) || strFileType.Equals(".exe", StringComparison.InvariantCultureIgnoreCase)))
            {
                if (!strPath.StartsWith("Binaries"))
                    strPath = Path.Combine("Binaries", strPath);
            }
            
            // the other mods should be handled by special mod install, this just handles the major ones

			return strPath;
		}

		/// <summary>
		/// Adjusts the given path to be relative to the installation path of the game mode.
		/// </summary>
		/// <remarks>
		/// This is basically a hack to allow older FOMods to work. Older FOMods assumed
		/// the installation path of Fallout games to be &lt;games>/data, but this new manager specifies
		/// the installation path to be &lt;games>. This breaks the older FOMods, so this method can detect
		/// the older FOMods (or other mod formats that needs massaging), and adjusts the given path
		/// to be relative to the new installation path to make things work.
		/// </remarks>
		/// <param name="p_mftModFormat">The mod format for which to adjust the path.</param>
		/// <param name="p_strPath">The path to adjust.</param>
		/// <param name="p_modMod">The mod.</param>
		/// <param name="p_booIgnoreIfPresent">Whether to ignore the path if the specific root is already present</param>
		/// <returns>The given path, adjusted to be relative to the installation path of the game mode.</returns>
		public override string GetModFormatAdjustedPath(IModFormat p_mftModFormat, string p_strPath, IMod p_modMod, bool p_booIgnoreIfPresent)
		{
            string strPath = p_strPath;
            string strFileType = Path.GetExtension(strPath);
            string[] strSpecialFiles = new[] { "opengl32.dll", "NMSE_steam.dll", "NMSE_Core_1_0.dll" };

            // do normal stuff to the files
            if (strFileType.Equals(".pak", StringComparison.InvariantCultureIgnoreCase))
            {
				if (strPath.StartsWith("PCBANKS", StringComparison.InvariantCultureIgnoreCase))
				{
					if (strPath.StartsWith("PCBANKS" + Path.DirectorySeparatorChar + "MODS", StringComparison.InvariantCultureIgnoreCase))
						strPath = Path.Combine("GAMEDATA", strPath);
					else
						strPath = strPath.Replace("PCBANKS", Path.Combine("GAMEDATA", "PCBANKS", "MODS"));
				}
				else if (strPath.StartsWith("MODS", StringComparison.InvariantCultureIgnoreCase))
					strPath = Path.Combine("GAMEDATA", "PCBANKS", strPath);
				else
					strPath = Path.Combine("GAMEDATA", "PCBANKS", "MODS", Path.GetFileName(strPath));
			}
            else if (strSpecialFiles.Any((s) => Path.GetFileName(strPath).Equals(s, StringComparison.InvariantCultureIgnoreCase) || strFileType.Equals(".exe", StringComparison.InvariantCultureIgnoreCase)))
            {
                if (!strPath.StartsWith("Binaries"))
                    strPath = Path.Combine("Binaries", strPath);
            }
            else if (strFileType.Equals(".dll", StringComparison.InvariantCultureIgnoreCase))
            {
                if (!strPath.StartsWith("Binaries"))
                {
                    if (!strPath.StartsWith("NMSE"))
                        strPath = Path.Combine("Binaries", "NMSE", strPath);
                    else
                        strPath = Path.Combine("Binaries", strPath);
                }
            }

            // edit files for load order

            if (strFileType.Equals(".pak", StringComparison.InvariantCultureIgnoreCase))
            {
                string strFileName = Path.GetFileName(strPath);

                if (!strFileName.Contains("_MOD"))
                    strFileName = strFileName.Insert(0, "_MOD");

                bool booIsUninstall = false;
                // check if this is an uninstall
                {
                    string strPossibleOldFile;
                    if (p_modMod.PlaceInModLoadOrder != -1)
                        strPossibleOldFile = Path.Combine(Path.GetDirectoryName(strPath), strFileName.Insert(1, p_modMod.PlaceInModLoadOrder.ToString()));
                    else
                        strPossibleOldFile = Path.Combine(Path.GetDirectoryName(strPath), strFileName);

                    if (File.Exists(Path.Combine(InstallationPath, strPossibleOldFile)))
                    {
                        strPath = strPossibleOldFile;
                        booIsUninstall = true;
                    }
                }

                if (!booIsUninstall)
                {
                    if (p_modMod.NewPlaceInModLoadOrder != -1)
                        strFileName = strFileName.Insert(1, p_modMod.NewPlaceInModLoadOrder.ToString());
                    strPath = Path.Combine(Path.GetDirectoryName(strPath), strFileName);
                }
            }

            // the other mods should be handled by special mod install, this just handles the major ones

            return strPath;
        }

		/// <summary>
		/// Checks whether the file's type requires a hardlink for the current game mode.
		/// </summary>
		/// <returns>Whether the file's type requires a hardlink for the current game mode.</returns>
		/// <param name="p_strFileName">The filename.</param>
		public override bool HardlinkRequiredFilesType(string p_strFileName)
		{
			string strFileType = Path.GetExtension(p_strFileName);
			return (strFileType.Equals(".mp3", StringComparison.InvariantCultureIgnoreCase) || strFileType.Equals(".dll", StringComparison.InvariantCultureIgnoreCase));
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
		/// Disposes of the unamanged resources.
		/// </summary>
		/// <param name="p_booDisposing">Whether the method is being called from the <see cref="IDisposable.Dispose()"/> method.</param>
		protected override void Dispose(bool p_booDisposing)
		{
		}

        /// <summary>
        /// Checks whether the current game mode requires external config steps to be taken before installing mods.
        /// </summary>
        /// <param name="p_strMessage">The message to show to the user</param>
        /// <returns>Whether the current game mode requires external config steps to be taken before installing mods.</returns>
        public override bool RequiresExternalConfig(out string p_strMessage)
        {
            p_strMessage = string.Empty;

            bool booBanksExist = Directory.Exists(Path.Combine(InstallationPath, "GAMEDATA", "PCBANKS", "MODS"));

            if(!booBanksExist)
            {
                Trace.TraceWarning(@"PCBANKS\MODS doesn't exist!");
                p_strMessage = @"Could not find PCBANKS\MODS folder. The PCBANKS\MODS folder is required for mods to run.";
            }

            return !booBanksExist;
        }

        public override IEnumerable<string> SpecialFileInstall(IMod p_modSelectedMod)
        {
            MessageBox.Show("The following mod is in an invalid state and can't be installed properly. The mod manager will attempt to install it anyway.");
            return p_modSelectedMod.GetFileList();
        }

        /// <summary>
        /// Checks if any files in the list require special installation
        /// </summary>
        /// <param name="p_strFiles">The list of files that need to be checked</param>
        /// <returns><c>true</c> if there are special files</returns>
        public override Boolean IsSpecialFile(IEnumerable<String> p_strFiles)
        {
            if (p_strFiles.Select(s => Path.GetExtension(s)).Any(s => s.Equals(".pak", StringComparison.InvariantCultureIgnoreCase) || s.Equals(".dll", StringComparison.InvariantCultureIgnoreCase)))
                return false;

            return p_strFiles.Select(s => Path.GetExtension(s))
                             .Intersect(new[] { ".wem", ".bnk", ".xml", ".txt", ".fnt", ".dds", ".mbin", ".pc", ".bin", ".h", ".glsl", ".TTC", ".TTF" }, StringComparer.InvariantCultureIgnoreCase)
                             .Any();
        }

        public override void SortMods(Action<IMod, IMod> p_actReinstallMod, ReadOnlyObservableList<IMod> p_lstActiveMods)
        {
            foreach (IMod modMod in p_lstActiveMods) p_actReinstallMod(modMod, null);
        }
    }
}
