using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nexus.Client.Games
{
	public class GameModeDiscoveredEventArgs : EventArgs
	{
		#region Properties

		public IGameModeDescriptor GameMode { get; private set; }

		public string InstallationPath { get; private set; }

		#endregion

		#region Constructors

		public GameModeDiscoveredEventArgs(IGameModeDescriptor p_gmdGameMode, string p_strInstallationPath)
		{
			GameMode = p_gmdGameMode;
			InstallationPath = p_strInstallationPath;
		}

		#endregion
	}
}
