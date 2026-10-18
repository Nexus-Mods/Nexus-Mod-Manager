using Nexus.Client.Games.Witcher3;

namespace Nexus.Client.Games.DataDriven
{
	/// <summary>
	/// Edition-aware launcher for the separately registered Witcher3Remastered ModeId.
	/// Does not change generic launch behavior or enable Remastered native installation.
	/// </summary>
	public sealed class DataDrivenWitcher3RemasteredGameLauncher : DataDrivenGameLauncher
	{
		public DataDrivenWitcher3RemasteredGameLauncher(IGameMode gameMode,
			IEnvironmentInfo environmentInfo, GameModeDefinition definition)
			: base(gameMode, environmentInfo, definition)
		{
		}

		protected override string GetLaunchBlockMessage()
		{
			Witcher3LaunchDecision decision = Witcher3LaunchPlanner.ReviewCurrentInstallation(
				GameMode.GameModeEnvironmentInfo.InstallationPath,
				GameMode.GameModeEnvironmentInfo.ExecutablePath,
				Witcher3Edition.Remastered);
			return decision.CanLaunch ? null : decision.GetUserMessage();
		}
	}
}
