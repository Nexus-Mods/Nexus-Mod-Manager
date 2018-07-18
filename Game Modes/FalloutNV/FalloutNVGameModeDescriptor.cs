using System.Drawing;
using Nexus.Client.Games.Gamebryo;

namespace Nexus.Client.Games.FalloutNV
{
	/// <summary>
	/// Provides the basic information about the Fallout: New Vegas game mode.
	/// </summary>
	public class FalloutNVGameModeDescriptor : GamebryoGameModeDescriptorBase
	{
		private static string[] EXECUTABLES = { "falloutNV.exe", "falloutNVng.exe" };
		private static string[] CRITICAL_PLUGINS = { "falloutnv.esm" };
		private static string[] OFFICIAL_PLUGINS = { "DeadMoney.esm", "HonestHearts.esm", "OldWorldBlues.esm", "LonesomeRoad.esm", "GunRunnersArsenal.esm", "CaravanPack.esm", "ClassicPack.esm", "MercenaryPack.esm", "TribalPack.esm" };
        private static string[] OFFICIAL_UNMANAGED_PLUGINS = { };

        private const string MODE_ID = "FalloutNV";

		#region Properties

		/// <summary>
		/// Gets the display name of the game mode.
		/// </summary>
		/// <value>The display name of the game mode.</value>
		public override string Name
		{
			get
			{
				return "Fallout: New Vegas";
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
				return new Theme(Properties.Resources.fonv_logo, Color.FromArgb(250, 167, 64), null);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public FalloutNVGameModeDescriptor(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
		}

		#endregion
	}
}
