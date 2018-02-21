using System.Drawing;
using Nexus.Client.Games.Gamebryo;

namespace Nexus.Client.Games.Oblivion
{
	/// <summary>
	/// Provides the basic information about the Oblivion game mode.
	/// </summary>
	public class OblivionGameModeDescriptor : GamebryoGameModeDescriptorBase
	{
		private static string[] EXECUTABLES = { "oblivion.exe" };
		private static string[] CRITICAL_PLUGINS = { "oblivion.esm" };
		private static string[] OFFICIAL_PLUGINS = { "DLCHorseArmor.esp", "DLCMehrunesRazor.esp", "Knights.esp", "DLCShiveringIsles.esp", "DLCVileLair.esp", "DLCFrostcrag.esp", "DLCBattlehornCastle.esp", "DLCSpellTomes.esp", "DLCThievesDen.esp", "DLCOrrery.esp" };
        private static string[] OFFICIAL_UNMANAGED_PLUGINS = { };
        private const string MODE_ID = "Oblivion";

		#region Properties

		/// <summary>
		/// Gets the display name of the game mode.
		/// </summary>
		/// <value>The display name of the game mode.</value>
		public override string Name
		{
			get
			{
				return "Oblivion";
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
				return new Theme(Properties.Resources.oblivion_logo, Color.FromArgb(250, 167, 64), null);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public OblivionGameModeDescriptor(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
		}

		#endregion
	}
}
