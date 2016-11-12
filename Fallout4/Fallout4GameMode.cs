using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nexus.Client.Games.Fallout3;
using Nexus.Client.Games.Gamebryo;
using Nexus.Client.Games.Fallout4.Tools;
using Nexus.Client.Games.Tools;
using Nexus.Client.Util;
using Nexus.Client.Games.Fallout4.Settings;
using Nexus.Client.Games.Fallout4.Settings.UI;
using Nexus.Client.Settings.UI;

namespace Nexus.Client.Games.Fallout4
{
	/// <summary>
	/// Provides information required for the programme to manage Fallout4 plugins and mods.
	/// </summary>
	public class Fallout4GameMode : Fallout3GameMode
	{
		private static string[] SCRIPT_EXTENDER_EXECUTABLES = { "f4se_loader.exe" };
		private static bool m_booOldEditsWarning = false;
		private Fallout4GameModeDescriptor m_gmdGameModeInfo = null;
		private Fallout4Launcher m_glnGameLauncher = null;
		private Fallout4ToolLauncher m_gtlToolLauncher = null;
		private Fallout4SupportedTools m_stlSupportedTools = null;
		private string m_strFallout4Ini = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"My Games\Fallout4\Fallout4.ini");
		private string m_strFallout4Prefs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"My Games\Fallout4\Fallout4Prefs.ini");
		private string m_strFallout4Custom = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"My Games\Fallout4\Fallout4Custom.ini");
		private string m_strLooseDefaultValue = @"STRINGS\";
		private string m_strPluginsDefaultValue = @"0";
		private string m_strInvalidateRequiredValue = @"1";
		private string m_strGuideLink = @"http://wiki.nexusmods.com/index.php/Fallout_4_Mod_Installation";

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
					strFullPath = Path.Combine(GameModeEnvironmentInfo.InstallationPath, "Fallout4.exe");
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
		/// Gets the path to the per user Fallout4 data.
		/// </summary>
		/// <value>The path to the per user Fallout4 data.</value>
		public override string UserGameDataPath
		{
			get
			{
				return Path.Combine(EnvironmentInfo.PersonalDataFolderPath, "My games\\Fallout4");
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
					m_glnGameLauncher = new Fallout4Launcher(this, EnvironmentInfo);
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
					m_gtlToolLauncher = new Fallout4ToolLauncher(this, EnvironmentInfo);
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
					m_stlSupportedTools = new Fallout4SupportedTools(this, EnvironmentInfo);
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
				return Properties.Resources.fallout4_base;
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
		public Fallout4GameMode(IEnvironmentInfo p_eifEnvironmentInfo, FileUtil p_futFileUtility)
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
			return new Fallout4SettingsFiles();
		}

		/// <summary>
		/// Adds the settings files to the game mode's list.
		/// </summary>
		protected override void SetupSettingsFiles()
		{
			base.SetupSettingsFiles();
			SettingsFiles.IniPath = Path.Combine(UserGameDataPath, "Fallout4Custom.ini");
			((FalloutSettingsFiles)SettingsFiles).FOPrefsIniPath = Path.Combine(UserGameDataPath, "Fallout4Prefs.ini");
		}

		#endregion

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
				m_gmdGameModeInfo = new Fallout4GameModeDescriptor(EnvironmentInfo);
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
			bool booPlugins = false;
			bool booNewLoose = false;
			p_strMessage = string.Empty;

			if (m_booOldEditsWarning)
				return false;

			if (!File.Exists(m_strFallout4Ini))
				return false;

			GamebryoIniReader girIniReader = new GamebryoIniReader(m_strFallout4Ini);
			string strLoose = girIniReader.GetValue("Archive", "sResourceDataDirsFinal", m_strLooseDefaultValue);

			if (!File.Exists(m_strFallout4Prefs))
				return false;

			girIniReader = new GamebryoIniReader(m_strFallout4Prefs);
			string strPlugins = girIniReader.GetValue("Launcher", "bEnableFileSelection", null);

			if (!File.Exists(m_strFallout4Custom))
				return false;

			girIniReader = new GamebryoIniReader(m_strFallout4Custom);
			string strCustomLoose = girIniReader.GetValue("Archive", "sResourceDataDirsFinal", m_strLooseDefaultValue);
			string strCustomInvalidate = girIniReader.GetValue("Archive", "bInvalidateOlderFiles", null);

			if (!string.IsNullOrEmpty(strLoose))
				if (!strLoose.Equals(m_strLooseDefaultValue, StringComparison.OrdinalIgnoreCase))
					booLoose = true;

			if (string.IsNullOrEmpty(strPlugins))
				booPlugins = true;
			else if (strPlugins.Equals(m_strPluginsDefaultValue, StringComparison.OrdinalIgnoreCase))
				booPlugins = true;

			if (string.IsNullOrEmpty(strCustomInvalidate))
				booNewLoose = true;
			else if (!strCustomInvalidate.Equals(m_strInvalidateRequiredValue, StringComparison.OrdinalIgnoreCase))
				booNewLoose = true;
			else if (strCustomLoose.Equals(m_strLooseDefaultValue, StringComparison.OrdinalIgnoreCase))
				booNewLoose = true;

			if (booNewLoose)
			{
				if (booLoose)
					p_strMessage = string.Format("The old Fallout 4 ini edits are deprecated (the long string in the Fallout4.ini caused long loading times). Please remove the old edits and follow this updated guide:" + Environment.NewLine + Environment.NewLine + "{0}", m_strGuideLink);
				else
					p_strMessage = string.Format("To use Fallout 4 mods you are REQUIRED to make some necessary ini edits ({0}{1}{2}), please follow this guide:" + Environment.NewLine + Environment.NewLine + "{3}", booNewLoose ? "Fallout4Custom.ini" : "", (booNewLoose && booPlugins) ? " and " : "", booPlugins ? "Fallout4Prefs.ini" : "", m_strGuideLink);
			}
			else
			{
				if (booLoose)
				{
					p_strMessage = string.Format("The old Fallout4.ini edit is causing long loading times, please remove it from your .ini file, set it as:" + Environment.NewLine + "sResourceDataDirsFinal=" + Environment.NewLine + "(nothing after the '=')");
					m_booOldEditsWarning = true;
					return true;
				}
			}

			m_booOldEditsWarning = true;
			return (booNewLoose);
		}
	}
}
