using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using Nexus.Client.Games.XCOM2;

namespace Nexus.Client.Games.XCOM2
{
	/// <summary>
	/// Provides common information about XCOM2 based games.
	/// </summary>
	public class XCOM2GameModeDescriptor : GameModeDescriptorBase
	{
		private static string[] EXECUTABLES = { "Binaries\\Win64\\XCOM2.exe" };
		private static readonly List<string> STOP_FOLDERS = new List<string>() { "Mods" };
		private const string MODE_ID = "XCOM2";

		#region Properties

		/// <summary>
		/// Gets the directory where XCOM2 plugins are installed.
		/// </summary>
		/// <value>The directory where XCOM2 plugins are installed.</value>
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
		/// Gets the directory where XCOM2 plugins are installed.
		/// </summary>
		/// <value>The directory where XCOM2 plugins are installed.</value>
		public override string InstallationPath
		{
			get
			{
				string strPath = null;
				if (!string.IsNullOrEmpty(base.InstallationPath))
					strPath = Path.Combine(base.InstallationPath, @"XComGame\Mods");

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
                return "XCOM 2";
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
				return new Theme(Properties.Resources.XCOM2_logo, Color.FromArgb(80, 45, 23), null);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public XCOM2GameModeDescriptor(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
		}

		#endregion
	}
}
