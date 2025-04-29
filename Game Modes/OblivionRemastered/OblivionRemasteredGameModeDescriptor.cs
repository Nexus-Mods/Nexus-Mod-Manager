using System;
using System.Drawing;
using System.IO;
using System.Linq;
using Nexus.Client.Games.Gamebryo;

namespace Nexus.Client.Games.OblivionRemastered
{
	/// <summary>
	/// Provides the basic information about the OblivionRemastered game mode.
	/// </summary>
	public class OblivionRemasteredGameModeDescriptor : GamebryoGameModeDescriptorBase
	{
		private static string[] EXECUTABLES = { "OblivionRemastered.exe" };
		private static string[] CRITICAL_PLUGINS = { "Oblivion.esm" };
		private static string[] OFFICIAL_PLUGINS = { "DLCBattlehornCastle.esp", "DLCFrostcrag.esp", "DLCHorseArmor.esp", "DLCMehrunesRazor.esp", "DLCOrrery.esp"
			, "DLCShiveringIsles.esp", "DLCSpellTomes.esp", "DLCThievesDen.esp", "DLCVileLair.esp", "Knights.esp", "AltarESPMain.esp", "AltarDeluxe.esp", "AltarESPLocal.esp", "AltarGymNavigation.esp", "TamrielLeveledRegion.esp" };
        private static string[] OFFICIAL_UNMANAGED_PLUGINS = { };
        private const string MODE_ID = "OblivionRemastered";
		private string _pluginPath = string.Empty;

		#region Properties

		/// <summary>
		/// Gets the display name of the game mode.
		/// </summary>
		/// <value>The display name of the game mode.</value>
		public override string Name
		{
			get
			{
				return "Oblivion Remastered";
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
		/// Gets the list of official unmanageable plugin names, ordered by load order.
		/// </summary>
		/// <value>The list of official unmanageable plugin names, ordered by load order.</value>
		protected override string[] OrderedOfficialUnmanagedPluginFilenames
		{
			get
			{
                return OFFICIAL_UNMANAGED_PLUGINS;
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
				return new Theme(Properties.Resources.OblivionRemastered_logo,Color.FromArgb(50, 104, 158),null);
			}
		}

		/// <summary>
		/// Gets the directory where Fallout 3 plugins are installed.
		/// </summary>
		/// <value>The directory where Fallout 3 plugins are installed.</value>
		public override string PluginDirectory
		{
			get
			{
				if (!string.IsNullOrEmpty(_pluginPath))
				{
					return _pluginPath;
				}

				var path = string.Empty;

				if (!string.IsNullOrEmpty(InstallationPath))
				{
					path = Path.Combine(InstallationPath, "OblivionRemastered\\Content\\Dev\\ObvData\\Data");

					var pathRoot = Path.GetPathRoot(path);

					if (DriveInfo.GetDrives().Where(x => x.Name.Equals(pathRoot, StringComparison.CurrentCultureIgnoreCase)).ToList().Count <= 0)
					{
						throw new DirectoryNotFoundException("The selected drive is no longer present on the system.");
					}

					if (!Directory.Exists(path))
					{
						Directory.CreateDirectory(path);
					}
				}

				_pluginPath = path;

				return path;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public OblivionRemasteredGameModeDescriptor(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
		}

		#endregion
	}
}
