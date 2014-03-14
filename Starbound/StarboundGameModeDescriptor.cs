using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using Nexus.Client.Games.Starbound;

namespace Nexus.Client.Games.Starbound
{
	/// <summary>
	/// Provides common information about Starbound based games.
	/// </summary>
	public class StarboundGameModeDescriptor : GameModeDescriptorBase
	{
		private static string[] EXECUTABLES = { "win32\\starbound.exe", "win32\\starbound_opengl.exe" };
		private static readonly List<string> STOP_FOLDERS = new List<string>() { "mods" };
		private static string[] CRITICAL_PLUGINS = null;
		private const string MODE_ID = "Starbound";

		#region Properties

		/// <summary>
		/// Gets the directory where Starbound plugins are installed.
		/// </summary>
		/// <value>The directory where Starbound plugins are installed.</value>
		public override string PluginDirectory
		{
			get
			{
                string strPath = Path.Combine(InstallationPath, "mods");
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
                return "Starbound";
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
				return new Theme(Properties.Resources.starbound_logo, Color.FromArgb(80, 45, 23), null);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public StarboundGameModeDescriptor(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
		}

		#endregion
	}
}
