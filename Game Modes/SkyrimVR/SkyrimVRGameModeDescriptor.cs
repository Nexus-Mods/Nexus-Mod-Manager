namespace Nexus.Client.Games.SkyrimVR
{
    using System.Drawing;

    using Nexus.Client.Games.Gamebryo;

    /// <summary>
    /// Provides the basic information about the SkyrimSE game mode.
    /// </summary>
    public class SkyrimVRGameModeDescriptor : GamebryoGameModeDescriptorBase
	{
		private static string[] EXECUTABLES = { "SkyrimVR.exe" };
		private static string[] CRITICAL_PLUGINS = { "Skyrim.esm", "Update.esm" , "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm", "SkyrimVR.esm" };
		private static string[] OFFICIAL_PLUGINS = { };
        private static string[] OFFICIAL_UNMANAGED_PLUGINS = {  };
        private const string MODE_ID = "SkyrimVR";

		#region Properties

		/// <summary>
		/// Gets the display name of the game mode.
		/// </summary>
		/// <value>The display name of the game mode.</value>
		public override string Name => "Skyrim VR";

	    /// <summary>
		/// Gets the unique id of the game mode.
		/// </summary>
		/// <value>The unique id of the game mode.</value>
		public override string ModeId => MODE_ID;

	    /// <summary>
		/// Gets the list of possible executable files for the game.
		/// </summary>
		/// <value>The list of possible executable files for the game.</value>
		public override string[] GameExecutables => EXECUTABLES;

	    /// <summary>
		/// Gets the list of critical plugin filenames, ordered by load order.
		/// </summary>
		/// <value>The list of critical plugin filenames, ordered by load order.</value>
		protected override string[] OrderedCriticalPluginFilenames => CRITICAL_PLUGINS;

	    /// <summary>
		/// Gets the list of official plugin names, ordered by load order.
		/// </summary>
		/// <value>The list of official plugin names, ordered by load order.</value>
		protected override string[] OrderedOfficialPluginFilenames => OFFICIAL_PLUGINS;

	    /// <summary>
		/// Gets the list of official unmanageable plugin names, ordered by load order.
		/// </summary>
		/// <value>The list of official unmanageable plugin names, ordered by load order.</value>
		protected override string[] OrderedOfficialUnmanagedPluginFilenames => OFFICIAL_UNMANAGED_PLUGINS;

	    /// <summary>
		/// Gets the theme to use for this game mode.
		/// </summary>
		/// <value>The theme to use for this game mode.</value>
		public override Theme ModeTheme => new Theme(Properties.Resources.SkyrimVR_logo,Color.FromArgb(50, 104, 158),null);

	    #endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public SkyrimVRGameModeDescriptor(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
		}

		#endregion
	}
}
