using System.Collections.Generic;
using Nexus.Client.Games.Tools;

namespace Nexus.Client.Games.DataDriven
{
	public class DataDrivenToolLauncher : IToolLauncher
	{
		private readonly List<ITool> _tools = new List<ITool>();

		public DataDrivenToolLauncher(IGameMode gameMode, IEnvironmentInfo environmentInfo, GameModeDefinition definition)
		{
		}

		public IEnumerable<ITool> Tools => _tools;
	}
}
