using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nexus.Client.Games;

namespace Nexus.Client
{
	public class GameDetectionVM
	{
		public GameDiscoverer GameDetector { get; private set; }

		public GameModeRegistry SupportedGameModes { get; private set; }

		public GameDetectionVM(GameDiscoverer p_gdvGameDetector, GameModeRegistry p_gmrSupportedGameModes)
		{
			GameDetector = p_gdvGameDetector;
			SupportedGameModes = p_gmrSupportedGameModes;
		}
	}
}
