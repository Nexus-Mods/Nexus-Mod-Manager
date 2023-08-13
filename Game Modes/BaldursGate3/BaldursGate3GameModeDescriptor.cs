using System;
using System.IO;
using System.Drawing;
using Nexus.Client.Games.BaldursGate3;

namespace Nexus.Client.Games.BaldursGate3
{
	/// <summary>
	/// Provides common information about BaldursGate3 based games.
	/// </summary>
	public class BaldursGate3GameModeDescriptor : GameModeDescriptorBase
	{
		private static string[] EXECUTABLES = { @"Launcher\LariLauncher.exe" };
		private const string MODE_ID = "BaldursGate3";

		#region Properties

		/// <summary>
		/// Gets the directory where BaldursGate3 plugins are installed.
		/// </summary>
		/// <value>The directory where BaldursGate3 plugins are installed.</value>
		public override string PluginDirectory
		{
			get
			{
				string strPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
				strPath = Path.Combine(strPath, @"Larian Studios\Baldur's Gate 3\Mods");
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

				if (string.IsNullOrEmpty(strPath) || strPath.Contains(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)))
				{
					strPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
					strPath = Path.Combine(strPath, @"Larian Studios\Baldur's Gate 3\Mods");
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
				return "Baldur's Gate 3";
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
		/// Gets the theme to use for this game mode.
		/// </summary>
		/// <value>The theme to use for this game mode.</value>
		public override Theme ModeTheme
		{
			get
			{
				return new Theme(Properties.Resources.BaldursGate3_logo, Color.FromArgb(209, 171, 94), null);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public BaldursGate3GameModeDescriptor(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
		}

		#endregion
	}
}
