using System;
using System.IO;
using System.Drawing;
using System.Collections.Generic;

namespace Nexus.Client.Games.Sims4
{
	/// <summary>
	/// Provides common information about Sims4 based games.
	/// </summary>
	public class Sims4GameModeDescriptor : GameModeDescriptorBase
	{
		private static string[] EXECUTABLES = { @"Game\Bin\TS4_x64.exe" };
		private const string MODE_ID = "TheSims4";
		private static readonly List<string> STOP_FOLDERS = new List<string> { "Mods", "Tray" };

		#region Properties

		/// <summary>
		/// Gets the directory where Sims4 plugins are installed.
		/// </summary>
		/// <value>The directory where Sims4 plugins are installed.</value>
		public override string PluginDirectory
		{
			get
			{
				string strPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
				strPath = Path.Combine(strPath, @"Electronic Arts\The Sims 4");
				if (!Directory.Exists(strPath))
					Directory.CreateDirectory(strPath);
				return strPath;
			}
		}

		/// <summary>
		/// Gets the path to which mod files should be installed.
		/// </summary>
		/// <value>The path to which mod files should be installed.</value>
		public override string InstallationPath
		{
			get
			{
				string strPath = string.Empty;

				if (EnvironmentInfo.Settings.InstallationPaths.ContainsKey(ModeId))
				{
					strPath = EnvironmentInfo.Settings.InstallationPaths[ModeId];
				}

				if (string.IsNullOrEmpty(strPath))
				{
					strPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
					strPath = Path.Combine(strPath, @"Electronic Arts\The Sims 4");
					EnvironmentInfo.Settings.InstallationPaths[ModeId] = strPath;
					EnvironmentInfo.Settings.Save();
				}

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
				return "The Sims 4";
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
		/// Gets the theme to use for this game mode.
		/// </summary>
		/// <value>The theme to use for this game mode.</value>
		public override Theme ModeTheme
		{
			get
			{
				return new Theme(Properties.Resources.Sims4_logo, Color.FromArgb(59, 145, 16), null);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public Sims4GameModeDescriptor(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
		}

		#endregion
	}
}
