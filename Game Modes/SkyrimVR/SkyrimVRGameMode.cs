using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Nexus.Client.Games.Fallout3;
using Nexus.Client.Games.Gamebryo;
using Nexus.Client.Games.SkyrimSE;
using Nexus.Client.Games.SkyrimSE.Tools;
using Nexus.Client.Games.Tools;
using Nexus.Client.Util;

namespace Nexus.Client.Games.SkyrimVR
{
	/// <summary>
	/// Provides information required for the program to manage SkyrimVR plugins and mods.
	/// </summary>
	public class SkyrimVRGameMode : SkyrimSEGameMode
	{
		private static string[] SCRIPT_EXTENDER_EXECUTABLES = { "skse_loader.exe" };
		private SkyrimVRGameModeDescriptor m_gmdGameModeInfo;
		private SkyrimVRLauncher m_glnGameLauncher;
		private SkyrimSEToolLauncher m_gtlToolLauncher;
		private SkyrimVRSupportedTools m_stlSupportedTools;

		#region Properties

		/// <summary>
		/// Gets the list of possible script extender executable files for the game.
		/// </summary>
		/// <value>The list of possible script extender executable files for the game.</value>
		protected override string[] ScriptExtenderExecutables => SCRIPT_EXTENDER_EXECUTABLES;

		/// <summary>
		/// Gets the path to the per user SkyrimVR data.
		/// </summary>
		/// <value>The path to the per user SkyrimVR data.</value>
		public override string UserGameDataPath => Path.Combine(EnvironmentInfo.PersonalDataFolderPath, @"My games\Skyrim VR");

		/// <summary>
		/// Gets the game launcher for the game mode.
		/// </summary>
		/// <value>The game launcher for the game mode.</value>
		public override IGameLauncher GameLauncher => m_glnGameLauncher ?? (m_glnGameLauncher = new SkyrimVRLauncher(this, EnvironmentInfo));

		/// <summary>
		/// Gets the tool launcher for the game mode.
		/// </summary>
		/// <value>The tool launcher for the game mode.</value>
		public override IToolLauncher GameToolLauncher => m_gtlToolLauncher ?? (m_gtlToolLauncher = new SkyrimSEToolLauncher(this, EnvironmentInfo));

		/// <summary>
		/// Gets the supported tool launcher for the game mode.
		/// </summary>
		/// <value>The supported tool launcher for the game mode.</value>
		public override ISupportedToolsLauncher SupportedToolsLauncher => m_stlSupportedTools ?? (m_stlSupportedTools = new SkyrimVRSupportedTools(this, EnvironmentInfo));

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
				return Properties.Resources.SkyrimSE_base;
			}
		}

		/// <summary>
		/// Whether the game requires the profile manager to save optional files.
		/// </summary>
		public override bool RequiresOptionalFilesCheckOnProfileSwitch => true;

		/// <summary>
		/// Gets whether the game mode supports the automatic sorting
		/// functionality for plugins.
		/// </summary>
		public override bool SupportsPluginAutoSorting => false;

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_futFileUtility">The file utility class to be used by the game mode.</param>
		/// <param name="p_eifEnvironmentInfo">The application's environment info.</param>
		public SkyrimVRGameMode(IEnvironmentInfo p_eifEnvironmentInfo, FileUtil p_futFileUtility)
			: base(p_eifEnvironmentInfo, p_futFileUtility)
		{
			if (!File.Exists(SettingsFiles.IniPath))
			{
				File.Create(SettingsFiles.IniPath);
			}
		}

		#endregion

		#region Initialization

		/// <summary>
		/// Instantiates the container to use to store the list of settings files.
		/// </summary>
		/// <returns>The container to use to store the list of settings files.</returns>
		protected override GamebryoSettingsFiles CreateSettingsFileContainer()
		{
			return new SkyrimVRSettingsFiles();
		}

		/// <summary>
		/// Adds the settings files to the game mode's list.
		/// </summary>
		protected override void SetupSettingsFiles()
		{
			SettingsFiles.RendererFilePath = Path.Combine(UserGameDataPath, "RendererInfo.txt");
			SettingsFiles.PluginsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Skyrim VR", "plugins.txt");

			if (!File.Exists(SettingsFiles.PluginsFilePath))
			{
				var strDirectory = Path.GetDirectoryName(SettingsFiles.PluginsFilePath);

				if (!Directory.Exists(strDirectory))
				{
					Directory.CreateDirectory(strDirectory);
				}

				File.Create(SettingsFiles.PluginsFilePath).Close();
			}

			SettingsFiles.IniPath = Path.Combine(UserGameDataPath, "SkyrimVR.ini");
			((FalloutSettingsFiles)SettingsFiles).FOPrefsIniPath = Path.Combine(UserGameDataPath, "SkyrimPrefs.ini");
		}

		#endregion

		/// <summary>
		/// Whether to run a secondary tools if present.
		/// </summary>
		/// <returns>The path to the optional tool to run.</returns>
		/// <param name="p_strMessage">The message to show to the user.</param>
		public override string PostProfileSwitchTool(out string p_strMessage)
		{
			p_strMessage = "It seems that FNIS is installed: switching mod profiles while using FNIS could require you to run it to prevent broken ingame animations, do you want to run it?";
			return Path.Combine(PluginDirectory, @"tools\GenerateFNIS_for_Users\GenerateFNISforUsers.exe");
		}

		/// <summary>
		/// Whether the profile manager should save extra files for the current game mode.
		/// </summary>
		/// <returns>The list of optional files to save (if present) in a profile.</returns>
		/// <param name="p_strList">The list of files/plugins/mods to check.</param>
		public override string[] GetOptionalFilesList(string[] p_strList)
		{
			var regEx = new Regex("^bashed patch");
			List<string> strOptionalFiles = new List<string>();

			if ((p_strList != null) && (p_strList.Length > 0))
			{
				strOptionalFiles.AddRange(p_strList.Where(x => regEx.IsMatch(Path.GetFileName(x.ToLower()))).ToList());

				regEx = new Regex("^patchusmaximus");
				strOptionalFiles.AddRange(p_strList.Where(x => regEx.IsMatch(Path.GetFileName(x.ToLower()))).ToList());

				regEx = new Regex("^dual sheath redux patch");
				strOptionalFiles.AddRange(p_strList.Where(x => regEx.IsMatch(Path.GetFileName(x.ToLower()))).ToList());
			}

			return strOptionalFiles.ToArray();
		}

		/// <summary>
		/// Creates a game mode descriptor for the current game mode.
		/// </summary>
		/// <returns>A game mode descriptor for the current game mode.</returns>
		protected override IGameModeDescriptor CreateGameModeDescriptor()
		{
			return m_gmdGameModeInfo ?? (m_gmdGameModeInfo = new SkyrimVRGameModeDescriptor(EnvironmentInfo));
		}

		/// <summary>
		/// Whether the profile manager should load extra files for the current game mode.
		/// </summary>
		/// <returns>The list of optional files to load (if present) in a profile.</returns>
		/// <param name="p_strMessage">The list of files/plugins/mods to load.</param>
		public override void SetOptionalFilesList(string[] p_strList)
		{
			if ((p_strList != null) && (p_strList.Length > 0))
			{
				foreach (var strFile in p_strList)
				{
					File.Copy(strFile, Path.Combine(PluginDirectory, Path.GetFileName(strFile)), true);
				}
			}
		}

		/// <summary>
		/// Checks whether the current game mode requires external config steps to be taken before installing mods.
		/// </summary>
		/// <returns>Whether the current game mode requires external config steps to be taken before installing mods.</returns>
		/// <param name="p_strMessage">The message to show to the user.</param>
		public override bool RequiresExternalConfig(out string p_strMessage)
		{
			/* This hacks into the intended use of this functionality to check whether this is the first time the user
			tries to install a mod for Skyrim Special Edition. */
			p_strMessage = string.Empty;

			if (!EnvironmentInfo.Settings.SkyrimSEFirstInstallWarning)
			{
				EnvironmentInfo.Settings.SkyrimSEFirstInstallWarning = true;
				EnvironmentInfo.Settings.Save();

				p_strMessage = "If you're installing a mod for the vanilla Skyrim containing a .BSA file please be aware that it won't work with the Skyrim VR. You will need to download a mod version made specifically for Skyrim VR/SE.";
				return true;
			}

			return false;
		}
	}
}
