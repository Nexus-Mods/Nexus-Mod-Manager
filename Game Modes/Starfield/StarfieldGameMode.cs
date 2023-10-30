using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nexus.Client.Games.Fallout3;
using Nexus.Client.Games.Gamebryo;
using Nexus.Client.Games.Starfield.Tools;
using Nexus.Client.Games.Tools;
using Nexus.Client.Util;
using Nexus.Client.Games.Starfield.Settings;
using Nexus.Client.Games.Starfield.Settings.UI;
using Nexus.Client.Settings.UI;
using Nexus.Client.Mods;

namespace Nexus.Client.Games.Starfield
{
	/// <summary>
	/// Provides information required for the programme to manage Starfield plugins and mods.
	/// </summary>
	public class StarfieldGameMode : Fallout3GameMode
	{
		private static string[] SCRIPT_EXTENDER_EXECUTABLES = { "sfse_loader.exe" };
		private static bool m_booOldEditsWarning = false;
		private StarfieldGameModeDescriptor m_gmdGameModeInfo = null;
		private StarfieldLauncher m_glnGameLauncher = null;
		private StarfieldToolLauncher m_gtlToolLauncher = null;
		private StarfieldSupportedTools m_stlSupportedTools = null;
		private string m_strStarfieldIni = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"My Games\Starfield\Starfield.ini");
		private string m_strStarfieldPrefs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"My Games\Starfield\StarfieldPrefs.ini");
		private string m_strStarfieldCustom = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"My Games\Starfield\StarfieldCustom.ini");
		private string m_strLooseDefaultValue = @"STRINGS\";
		private string m_strInvalidateRequiredValue = @"1";
		private string m_strGuideLink = @"https://wiki.nexusmods.com/index.php/Starfield_Mod_Installation";

		#region Properties

		/// <summary>
		/// Gets the version of the installed game.
		/// </summary>
		/// <value>The version of the installed game.</value>
		public override Version GameVersion
		{
			get
			{
				Version FO4Version = new Version("0.0.0.0");
				try
				{
					string strFullPath = null;
					strFullPath = Path.Combine(GameModeEnvironmentInfo.InstallationPath, "Starfield.exe");
					if (File.Exists(strFullPath))
					{
						FO4Version = new Version(System.Diagnostics.FileVersionInfo.GetVersionInfo(strFullPath).FileVersion.Replace(", ", "."));
						return FO4Version;
					}
					else
						return null;
				}
				catch { }
				
				return FO4Version;
			}
		}

		/// <summary>
		/// Gets the list of possible script extender executable files for the game.
		/// </summary>
		/// <value>The list of possible script extender executable files for the game.</value>
		protected override string[] ScriptExtenderExecutables
		{
			get
			{
				return SCRIPT_EXTENDER_EXECUTABLES;
			}
		}

		/// <summary>
		/// Gets the path to the per user Starfield data.
		/// </summary>
		/// <value>The path to the per user Starfield data.</value>
		public override string UserGameDataPath
		{
			get
			{
				return Path.Combine(EnvironmentInfo.PersonalDataFolderPath, "My games\\Starfield");
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
					m_glnGameLauncher = new StarfieldLauncher(this, EnvironmentInfo);
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
					m_gtlToolLauncher = new StarfieldToolLauncher(this, EnvironmentInfo);
				return m_gtlToolLauncher;
			}
		}

		/// <summary>
		/// Gets the supported tool launcher for the game mode.
		/// </summary>
		/// <value>The supported tool launcher for the game mode.</value>
		public override ISupportedToolsLauncher SupportedToolsLauncher
		{
			get
			{
				if (m_stlSupportedTools == null)
					m_stlSupportedTools = new StarfieldSupportedTools(this, EnvironmentInfo);
				return m_stlSupportedTools;
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
		
		/// <summary>
		/// Gets the game Base files.
		/// </summary>
		/// <value>The default game categories stored in the resource file.</value>
		public override string BaseGameFiles
		{
			get
			{
				return Properties.Resources.Starfield_base;
			}
		}

		/// <summary>
		/// Whether the game requires the profile manager to save optional files.
		/// </summary>
		public override bool RequiresOptionalFilesCheckOnProfileSwitch
		{
			get
			{
				return true;
			}
		}

		/// <summary>
		/// Gets whether the game mode supports the automatic sorting
		/// functionality for plugins.
		/// </summary>
		public override bool SupportsPluginAutoSorting
		{
			get
			{
				return false;
			}
		}

		/// <summary>
		/// Whether the plugin sorter is properly initialized.
		/// </summary>
		public override bool PluginSorterInitialized
		{
			get
			{
				return false;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_futFileUtility">The file utility class to be used by the game mode.</param>
		/// <param name="p_eifEnvironmentInfo">The application's environment info.</param>
		public StarfieldGameMode(IEnvironmentInfo p_eifEnvironmentInfo, FileUtil p_futFileUtility)
			: base(p_eifEnvironmentInfo, p_futFileUtility)
		{

		}

		#endregion

		#region Initialization

		/// <summary>
		/// Instantiates the container to use to store the list of settings files.
		/// </summary>
		/// <returns>The container to use to store the list of settings files.</returns>
		protected override GamebryoSettingsFiles CreateSettingsFileContainer()
		{
			return new StarfieldSettingsFiles();
		}

		/// <summary>
		/// Adds the settings files to the game mode's list.
		/// </summary>
		protected override void SetupSettingsFiles()
		{
			base.SetupSettingsFiles();
			SettingsFiles.IniPath = Path.Combine(UserGameDataPath, "StarfieldCustom.ini");
			((FalloutSettingsFiles)SettingsFiles).FOPrefsIniPath = Path.Combine(UserGameDataPath, "StarfieldPrefs.ini");
		}

		#endregion

		public override string GetModFormatAdjustedPath(IModFormat p_mftModFormat, string p_strPath, bool p_booIgnoreIfPresent)
		{
			if (!string.IsNullOrEmpty(p_strPath) && p_strPath.Contains(Path.DirectorySeparatorChar) && p_strPath.Count(x => x == Path.DirectorySeparatorChar) == 1)
			{
				if (Array.IndexOf(m_gmdGameModeInfo.PluginExtensions.ToArray(), Path.GetExtension(p_strPath).ToLowerInvariant()) > -1)
				{
					string root = Path.GetDirectoryName(p_strPath);

					if (!string.IsNullOrEmpty(root) && !root.Equals("Data", StringComparison.OrdinalIgnoreCase))
					{
						p_strPath = p_strPath.Replace(root, "Data");
					}
				}
			}

			return base.GetModFormatAdjustedPath(p_mftModFormat, p_strPath, p_booIgnoreIfPresent);
		}

		/// <summary>
		/// Whether the profile manager should save extra files for the current game mode.
		/// </summary>
		/// <returns>The list of optional files to save (if present) in a profile.</returns>
		/// <param name="p_strMessage">The list of files/plugins/mods to check.</param>
		public override string[] GetOptionalFilesList(string[] p_strList)
		{
			List<string> strOptionalFiles = new List<string>();

			return strOptionalFiles.ToArray();
		}

		/// <summary>
		/// Creates a game mode descriptor for the current game mode.
		/// </summary>
		/// <returns>A game mode descriptor for the current game mode.</returns>
		protected override IGameModeDescriptor CreateGameModeDescriptor()
		{
			if (m_gmdGameModeInfo == null)
				m_gmdGameModeInfo = new StarfieldGameModeDescriptor(EnvironmentInfo);
			return m_gmdGameModeInfo;
		}

		/// <summary>
		/// Whether the profile manager should load extra files for the current game mode.
		/// </summary>
		/// <returns>The list of optional files to load (if present) in a profile.</returns>
		/// <param name="p_strMessage">The list of files/plugins/mods to load.</param>
		public override void SetOptionalFilesList(string[] p_strList)
		{
			if ((p_strList != null) && (p_strList.Length > 0))
				foreach (string strFile in p_strList)
					File.Copy(strFile, Path.Combine(PluginDirectory, Path.GetFileName(strFile)), true);
		}

		/// <summary>
		/// Checks whether the current game mode requires external config steps to be taken before installing mods.
		/// </summary>
		/// <returns>Whether the current game mode requires external config steps to be taken before installing mods.</returns>
		/// <param name="p_strMessage">The message to show to the user.</param>
		public override bool RequiresExternalConfig(out string p_strMessage)
		{
			bool booLoose = false;
			bool booNewLoose = false;
			p_strMessage = string.Empty;

			// Currently unsupported
			return false;

			if (m_booOldEditsWarning)
				return false;

			if (!File.Exists(m_strStarfieldIni))
				return false;

			GamebryoIniReader girIniReader = new GamebryoIniReader(m_strStarfieldIni);
			string strLoose = girIniReader.GetValue("Archive", "sResourceDataDirsFinal", m_strLooseDefaultValue);

			if (!File.Exists(m_strStarfieldPrefs))
				return false;

			if (!File.Exists(m_strStarfieldCustom))
				return false;

			girIniReader = new GamebryoIniReader(m_strStarfieldCustom);
			string strCustomLoose = girIniReader.GetValue("Archive", "sResourceDataDirsFinal", m_strLooseDefaultValue);
			string strCustomInvalidate = girIniReader.GetValue("Archive", "bInvalidateOlderFiles", null);

			if (!string.IsNullOrEmpty(strLoose))
				if (!strLoose.Equals(m_strLooseDefaultValue, StringComparison.OrdinalIgnoreCase))
					booLoose = true;

			if (string.IsNullOrEmpty(strCustomInvalidate))
				booNewLoose = true;
			else if (!strCustomInvalidate.Equals(m_strInvalidateRequiredValue, StringComparison.OrdinalIgnoreCase))
				booNewLoose = true;
			else if (strCustomLoose.Equals(m_strLooseDefaultValue, StringComparison.OrdinalIgnoreCase))
				booNewLoose = true;

            // Implement backup and autofix.  If that fails, warn the user

            if (booNewLoose)
            {
                try
                {
                    string DesiredContent = @"[Display]
iLocation X=0
iLocation Y=0
[Archive]
bInvalidateOlderFiles=1
sResourceDataDirsFinal=
";
                    // back up the current file and then make the corrections
                    System.IO.File.Move(m_strStarfieldCustom, m_strStarfieldCustom.Replace(".ini", ".nmm_backup"));
                    System.IO.File.WriteAllText(m_strStarfieldCustom, DesiredContent);
                }
                catch
                {
                    p_strMessage = string.Format("Your StarfieldCustom.ini is not configured correctly.  Nexus Mod Manager was unable to make the necessary changes automatically.  Please refer to the documentation located at https://wiki.tesnexus.com/index.php/Fallout_4_Mod_Installation#How_To_Enable_Fallout_4_Mods");
                    m_booOldEditsWarning = true;
                    return true;
                }
            }

			// m_booOldEditsWarning = true;
			return false;
		}
	}
}
