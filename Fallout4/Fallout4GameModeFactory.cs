using Nexus.Client.Games.Gamebryo;
using Nexus.Client.UI;
using Nexus.Client.Util;
using Nexus.Client.Games.Steam;
using System.Linq;
using System.IO;
using System;
using Microsoft.Win32;
using System.Diagnostics;

namespace Nexus.Client.Games.Fallout4
{
	/// <summary>
	/// The game mode factory that builds <see cref="Fallout4GameMode"/>s.
	/// </summary>
	public class Fallout4GameModeFactory : GamebryoGameModeFactory
	{
		private readonly IGameModeDescriptor m_gmdGameModeDescriptor = null;

		#region Properties

		/// <summary>
		/// Gets the descriptor of the game mode that this factory builds.
		/// </summary>
		/// <value>The descriptor of the game mode that this factory builds.</value>
		public override IGameModeDescriptor GameModeDescriptor
		{
			get
			{
				return m_gmdGameModeDescriptor;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple consturctor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's environement info.</param>
		public Fallout4GameModeFactory(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
			m_gmdGameModeDescriptor = new Fallout4GameModeDescriptor(p_eifEnvironmentInfo);
		}

		#endregion

		/// <summary>
		/// Instantiates the game mode.
		/// </summary>
		/// <param name="p_futFileUtility">The file utility class to be used by the game mode.</param>
		/// <returns>The game mode for which this is a factory.</returns>
		protected override GamebryoGameModeBase InstantiateGameMode(FileUtil p_futFileUtility)
		{
			return new Fallout4GameMode(EnvironmentInfo, p_futFileUtility);
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
            var strValue = SteamInstallationPathDetector.Instance.GetSteamInstallationPath("377160", "Fallout4", "Fallout4Launcher.exe");

			if (string.IsNullOrEmpty(strValue))
				strValue = base.GetInstallationPath();

			return strValue;
		}
	}
}
