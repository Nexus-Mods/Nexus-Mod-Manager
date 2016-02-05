using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using Nexus.Client.Games.Witcher3;

namespace Nexus.Client.Games.Witcher3
{
	/// <summary>
	/// Provides common information about Witcher3 based games.
	/// </summary>
	public class Witcher3GameModeDescriptor : GameModeDescriptorBase
	{
		private static string[] EXECUTABLES = { "bin\\x64\\Witcher3.exe" };
		private static readonly List<string> STOP_FOLDERS = new List<string>() { };
		private const string MODE_ID = "Witcher3";

		#region Properties

		/// <summary>
		/// Gets the directory where Witcher3 plugins are installed.
		/// </summary>
		/// <value>The directory where Witcher3 plugins are installed.</value>
		public override string PluginDirectory
		{
			get
			{
                string strPath = Path.Combine(InstallationPath, "");
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
				string strPath = null;
				if (EnvironmentInfo.Settings.InstallationPaths.ContainsKey(ModeId))
				{
					strPath = (string)EnvironmentInfo.Settings.InstallationPaths[ModeId];
					if (!strPath.Equals(ExecutablePath, StringComparison.InvariantCultureIgnoreCase))
					{
						strPath = ExecutablePath;
						EnvironmentInfo.Settings.InstallationPaths[ModeId] = strPath;
						EnvironmentInfo.Settings.Save();
					}

					return strPath;
				}

				return null;
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
                return "The Witcher 3";
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
				return new Theme(Properties.Resources.Witcher3_logo, Color.FromArgb(80, 45, 23), null);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public Witcher3GameModeDescriptor(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
		}

		#endregion
	}
}
