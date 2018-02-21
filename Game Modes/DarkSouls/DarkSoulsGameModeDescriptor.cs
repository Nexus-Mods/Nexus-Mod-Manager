using System;
using System.IO;
using System.Drawing;
using Nexus.Client.Games.DarkSouls;

namespace Nexus.Client.Games.DarkSouls
{
	/// <summary>
	/// Provides common information about DarkSouls based games.
	/// </summary>
	public class DarkSoulsGameModeDescriptor : GameModeDescriptorBase
	{
		private static string[] EXECUTABLES = { "DARKSOULS.exe" };
		private static string[] CRITICAL_PLUGINS = { "DINPUT8.dll" , "DSfix.ini" };
		private const string MODE_ID = "DarkSouls";

		#region Properties

		/// <summary>
		/// Gets the directory where DarkSouls plugins are installed.
		/// </summary>
		/// <value>The directory where DarkSouls plugins are installed.</value>
		public override string PluginDirectory
		{
			get
			{
				string strPath = InstallationPath;
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
				return "Dark Souls";
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
				return new Theme(Properties.Resources.darksouls_logo, Color.FromArgb(80, 45, 23), null);
			}
		}

		/// <summary>
		/// Gets the custom message for missing critical files.
		/// </summary>
		/// <value>The custom message for missing critical files.</value>
		public override string CriticalFilesErrorMessage
		{
			get
			{
				
				return ("DSfix by Durante is required for Dark Souls modding, make sure it's properly installed and up to date." + Environment.NewLine + "Link: http://darksouls.nexusmods.com/mods/19/" + Environment.NewLine + "Install it, then restart NMM.");
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public DarkSoulsGameModeDescriptor(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
		}

		#endregion
	}
}
