namespace Nexus.Client.Games
{
	public class GameStoreInstallInfo
	{
		public GameStore Store { get; set; }

		public string Id { get; set; }

		public string InstallFolderName { get; set; }

		public string ExecutableName { get; set; }

		public string RegistryKey { get; set; }

		public string RegistryValueName { get; set; }

		public string PathSuffix { get; set; }
	}
}