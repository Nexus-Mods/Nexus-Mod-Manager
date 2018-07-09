﻿namespace Nexus.Client.Games.SkyrimVR
{
    using Nexus.Client.Games.Gamebryo;
    using Nexus.Client.UI;
    using Nexus.Client.Util;

    /// <summary>
    /// The game mode factory that builds <see cref="SkyrimVRGameMode"/>s.
    /// </summary>
    public class SkyrimVRGameModeFactory : GamebryoGameModeFactory
	{
		private readonly IGameModeDescriptor m_gmdGameModeDescriptor;

		#region Properties

		/// <summary>
		/// Gets the descriptor of the game mode that this factory builds.
		/// </summary>
		/// <value>The descriptor of the game mode that this factory builds.</value>
		public override IGameModeDescriptor GameModeDescriptor => m_gmdGameModeDescriptor;

	    #endregion

		#region Constructors

		/// <summary>
		/// A simple consturctor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's environement info.</param>
		public SkyrimVRGameModeFactory(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
			m_gmdGameModeDescriptor = new SkyrimVRGameModeDescriptor(p_eifEnvironmentInfo);
		}

		#endregion

		/// <summary>
		/// Instantiates the game mode.
		/// </summary>
		/// <param name="p_futFileUtility">The file utility class to be used by the game mode.</param>
		/// <returns>The game mode for which this is a factory.</returns>
		protected override GamebryoGameModeBase InstantiateGameMode(FileUtil p_futFileUtility)
		{
			return new SkyrimVRGameMode(EnvironmentInfo, p_futFileUtility);
		}

		/// <summary>
		/// Performs the initializtion for the game mode being created.
		/// </summary>
		/// <param name="p_dlgShowView">The delegate to use to display a view.</param>
		/// <param name="p_dlgShowMessage">The delegate to use to display a message.</param>
		/// <returns><c>true</c> if the setup completed successfully;
		/// <c>false</c> otherwise.</returns>
		public override bool PerformInitialization(ShowViewDelegate p_dlgShowView, ShowMessageDelegate p_dlgShowMessage)
		{
			return true;
		}

		/// <summary>
		/// Gets the path where mod files should be installed.
		/// </summary>
		/// <returns>The path where mod files should be installed, or
		/// <c>null</c> if the path could not be determined.</returns>
		public override string GetInstallationPath()
		{
            var strValue = SteamInstallationPathDetector.Instance.GetSteamInstallationPath("611670", "SkyrimVR", "SkyrimVR.exe");

		    if (string.IsNullOrEmpty(strValue))
		    {
		        strValue = base.GetInstallationPath();
		    }

			return strValue;
		}
	}
}
