using System;
using System.IO;
using System.Drawing;
using Nexus.Client.Games.Witcher2;
using System.Collections.Generic;

namespace Nexus.Client.Games.Witcher2
{
	/// <summary>
    /// Provides common information about Witcher2 based games.
	/// </summary>
	public class Witcher2GameModeDescriptor : GameModeDescriptorBase
	{
		private static readonly List<string> PLUGIN_EXTENSIONS = new List<string>() { ".dzip" };
		private static readonly List<string> STOP_FOLDERS = new List<string>() { "abilities", "characters", "combat", "cutscenes",
																					"engine", "environment", "environment_levels", "fx",
																					"game", "globals", "items", "junk",
																					"levels", "reactions", "speedtree", "templates",
																					"tests" };
		private static string[] EXECUTABLES = { "userContentManager.exe", "witcher2.exe" };
		private static string[] CRITICAL_PLUGINS = { "userContentManager.exe" };
        private const string MODE_ID = "Witcher2";

		#region Properties

		/// <summary>
		/// Gets the extensions that are used by the game mode for plugin files.
		/// </summary>
		/// <value>The extensions that are used by the game mode for plugin files.</value>
		public override IEnumerable<string> PluginExtensions
		{
			get
			{
				return PLUGIN_EXTENSIONS;
			}
		}

		/// <summary>
		/// Gets a list of possible folders that should be looked for in mod archives to determine
		/// file structure.
		/// </summary>
		/// <value>A list of possible folders that should be looked for in mod archives to determine
		/// file structure.</value>
		public override IEnumerable<string> StopFolders
		{
			get
			{
				return STOP_FOLDERS;
			}
		}

		/// <summary>
		/// Gets the directory where The Witcher 2 plugins are installed.
		/// </summary>
		/// <value>The directory where The Witcher 2 plugins are installed.</value>
		public override string PluginDirectory
		{
			get
			{
				string strPath = Path.Combine(Path.GetDirectoryName(ExecutablePath), "CookedPC");
				if (!Directory.Exists(strPath))
					Directory.CreateDirectory(strPath);
				return strPath;
			}
		}

		/// <summary>
		/// Gets the secondary path to which mod files should be installed.
		/// </summary>
		/// <value>The secondary path to which mod files should be installed.</value>
		public override string SecondaryInstallationPath
		{
			get
			{
				string strPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
				strPath = Path.Combine(strPath, @"Witcher 2\UserContent");
				if (!Directory.Exists(strPath))
					Directory.CreateDirectory(strPath);
				return strPath;
			}
		}

		/// <summary>
		/// Gets the display name of the game mode.
		/// </summary>
		/// <value>The display name of the game mode.</value>
		public override string Name
		{
			get
			{
				return "The Witcher 2";
			}
		}

		/// <summary>
		/// Gets the unique id of the game mode.
		/// </summary>
		/// <value>The unique id of the game mode.</value>
		public override string ModeId
		{
			get
			{
				return MODE_ID;
			}
		}

		/// <summary>
		/// Gets the list of possible executable files for the game.
		/// </summary>
		/// <value>The list of possible executable files for the game.</value>
		public override string[] GameExecutables
		{
			get
			{
				return EXECUTABLES;
			}
		}

		/// <summary>
		/// Gets the list of critical plugin names, ordered by load order.
		/// </summary>
		/// <value>The list of critical plugin names, ordered by load order.</value>
		public override string[] OrderedCriticalPluginNames
		{
			get
			{
				if (!String.IsNullOrEmpty(ExecutablePath))
					for (int i = 0; i < CRITICAL_PLUGINS.Length; i++)
						CRITICAL_PLUGINS[i] = Path.Combine(ExecutablePath, CRITICAL_PLUGINS[i]);
				return CRITICAL_PLUGINS;
			}
		}

		/// <summary>
		/// Gets the theme to use for this game mode.
		/// </summary>
		/// <value>The theme to use for this game mode.</value>
		public override Theme ModeTheme
		{
			get
			{
				return new Theme(Properties.Resources.witcher2_logo, Color.FromArgb(80, 45, 23), null);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public Witcher2GameModeDescriptor(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
		}

		#endregion
	}
}
