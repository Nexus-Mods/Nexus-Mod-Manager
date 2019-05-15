using System.Drawing;
using Nexus.Client.Games.Gamebryo;

namespace Nexus.Client.Games.Enderal
{
	/// <summary>
	/// Provides the basic information about the Enderal game mode.
	/// </summary>
	public class EnderalGameModeDescriptor : GamebryoGameModeDescriptorBase
	{
		private static string[] EXECUTABLES = { "Enderal Launcher.exe" };
		private static string[] CRITICAL_PLUGINS = { "Skyrim.esm", "Update.esm" };
		private static string[] OFFICIAL_PLUGINS = { "Enderal - Forgotten Stories.esm" };
        private static string[] OFFICIAL_UNMANAGED_PLUGINS = { };
        private const string MODE_ID = "Enderal";

		#region Properties

		/// <summary>
		/// Gets the display name of the game mode.
		/// </summary>
		/// <value>The display name of the game mode.</value>
		public override string Name
		{
			get
			{
				return "Enderal";
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
				return new Theme(Properties.Resources.enderal_logo,Color.FromArgb(50, 104, 158),null);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public EnderalGameModeDescriptor(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
		}

		#endregion
	}
}
