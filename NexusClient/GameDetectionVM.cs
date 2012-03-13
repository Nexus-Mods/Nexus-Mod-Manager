using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nexus.Client.Games;

namespace Nexus.Client
{
	public class GameDetectionVM
	{
		/// <summary>
		/// Gets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		public IEnvironmentInfo EnvironmentInfo { get; private set; }

		public GameDiscoverer GameDetector { get; private set; }

		public GameModeRegistry SupportedGameModes { get; private set; }

		public GameDetectionVM(IEnvironmentInfo p_eifEnvironmentInfo, GameDiscoverer p_gdvGameDetector, GameModeRegistry p_gmrSupportedGameModes)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;
			GameDetector = p_gdvGameDetector;
			SupportedGameModes = p_gmrSupportedGameModes;
		}

		public void Cancel()
		{
			GameDetector.Cancel();
		}
	}
}
