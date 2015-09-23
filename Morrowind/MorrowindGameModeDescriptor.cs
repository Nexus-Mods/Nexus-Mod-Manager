using System;
using System.Drawing;
using System.IO;
using Nexus.Client.Games.Gamebryo;

namespace Nexus.Client.Games.Morrowind
{
	/// <summary>
	/// Provides the basic information about the Morrowind game mode.
	/// </summary>
	public class MorrowindGameModeDescriptor : GamebryoGameModeDescriptorBase
	{
		private static string[] EXECUTABLES = { "Morrowind.exe" };
		private static string[] CRITICAL_PLUGINS = { "Morrowind.esm" };
		private static string[] OFFICIAL_PLUGINS = { "Tribunal.esm", "Bloodmoon.esm" };
		private const string MODE_ID = "Morrowind";

		#region Properties

		/// <summary>
		/// Gets the directory where Morrowind plugins are installed.
		/// </summary>
		/// <value>The directory where Morrowind plugins are installed.</value>
		public override string PluginDirectory
		{
			get
			{
				string strPath = Path.Combine(InstallationPath, "Data Files");
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
				return "Morrowind";
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
		/// Gets the list of critical plugin filenames, ordered by load order.
		/// </summary>
		/// <value>The list of critical plugin filenames, ordered by load order.</value>
		protected override string[] OrderedCriticalPluginFilenames
		{
			get
			{
				return CRITICAL_PLUGINS;
			}
		}

		/// <summary>
		/// Gets the list of official plugin names, ordered by load order.
		/// </summary>
		/// <value>The list of official plugin names, ordered by load order.</value>
		protected override string[] OrderedOfficialPluginFilenames
		{
			get
			{
				return OFFICIAL_PLUGINS;
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
				return new Theme(Properties.Resources.morrowind_logo, Color.FromArgb(80, 45, 23), null);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public MorrowindGameModeDescriptor(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
		}

		#endregion
	}
}
