using System;
using System.IO;
using System.Drawing;
using Nexus.Client.Games.WorldOfTanks;

namespace Nexus.Client.Games.WorldOfTanks
{
	/// <summary>
	/// Provides common information about WorldOfTanks based games.
	/// </summary>
	public class WoTGameModeDescriptor : GameModeDescriptorBase
	{
		private static string[] EXECUTABLES = { "WorldOfTanks.exe" };
		private const string MODE_ID = "WorldOfTanks";

		#region Properties

		/// <summary>
		/// Gets the directory where WorldOfTanks plugins are installed.
		/// </summary>
		/// <value>The directory where WorldOfTanks plugins are installed.</value>
		public override string PluginDirectory
		{
			get
			{
				string strVersion = string.Empty;
				string strPath = Path.Combine(InstallationPath, "res_mods");
				string strFullPath = Path.Combine(InstallationPath, "WorldOfTanks.exe");
				if (File.Exists(strFullPath))
				{
					strVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(strFullPath).FileVersion.Replace(", ", ".");
					if ((strVersion.Split('.').Length - 1) > 2)
						strVersion = strVersion.Substring(0, strVersion.LastIndexOf("."));
				}
				strPath = Path.Combine(strPath, strVersion);
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
				return "World of Tanks";
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
				return new Theme(Properties.Resources.wot_logo, Color.FromArgb(80, 45, 23), null);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public WoTGameModeDescriptor(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
		}

		#endregion
	}
}
