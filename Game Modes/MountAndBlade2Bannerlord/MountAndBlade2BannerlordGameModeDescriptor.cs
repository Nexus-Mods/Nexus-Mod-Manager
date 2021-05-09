using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using Nexus.Client.Games.MountAndBlade2Bannerlord;

namespace Nexus.Client.Games.MountAndBlade2Bannerlord
{
	/// <summary>
	/// Provides common information about MountAndBlade2Bannerlord based games.
	/// </summary>
	public class MountAndBlade2BannerlordGameModeDescriptor : GameModeDescriptorBase
	{
		private static string[] EXECUTABLES = { @"bin\Win64_Shipping_Client\Bannerlord.exe" };
		private static readonly List<string> STOP_FOLDERS = new List<string>() { "Modules" };
		private const string MODE_ID = "MountAndBlade2Bannerlord";

		#region Properties

		/// <summary>
		/// Gets the directory where MountAndBlade2Bannerlord plugins are installed.
		/// </summary>
		/// <value>The directory where MountAndBlade2Bannerlord plugins are installed.</value>
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
		/// Gets the directory where MountAndBlade2Bannerlord plugins are installed.
		/// </summary>
		/// <value>The directory where MountAndBlade2Bannerlord plugins are installed.</value>
		public override string InstallationPath
		{
			get
			{
				string strPath = null;
				if (!string.IsNullOrEmpty(base.InstallationPath))
					strPath = Path.Combine(base.InstallationPath, "");

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
				return "Mount & Blade II : Bannerlord";
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
				return new Theme(Properties.Resources.MountAndBlade2Bannerlord_logo, Color.FromArgb(91, 73, 51), null);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public MountAndBlade2BannerlordGameModeDescriptor(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
		}

		#endregion
	}
}
