namespace Nexus.Client.GameStorage
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using Nexus.Client.Games;
	using Nexus.Client.Games.DataDriven;

	/// <summary>The same per-role path defaults used by game initialization and explicit setup.
	/// This method reads settings; it never creates a directory or saves any settings.</summary>
	internal static class GameStorageInitialPaths
	{
		internal static GameStoragePathSet Create(IEnvironmentInfo environment, GameStorageService service,
			IGameModeFactory factory, string gameInstallPath)
		{
			if (environment == null) throw new ArgumentNullException(nameof(environment));
			if (service == null) throw new ArgumentNullException(nameof(service));
			if (factory == null) throw new ArgumentNullException(nameof(factory));
			string id = factory.GameModeDescriptor.ModeId;
			string root = GetInitialRoot(environment, id, gameInstallPath);
			string savedVirtual = Read(environment.Settings.VirtualFolder, id);
			string virtualPath = service.NormalizeVirtualInstallDirectory(
				String.IsNullOrWhiteSpace(savedVirtual) ? root : savedVirtual);
			string linkPath = Read(environment.Settings.HDLinkFolder, id) ?? Path.Combine(root, "LinkFolder");
			bool multiHd = Read(environment.Settings.MultiHDInstall, id);
			bool linkRequired = multiHd || service.IsLinkFolderRequired(virtualPath, gameInstallPath);
			return new GameStoragePathSet
			{
				GameId = id,
				GameName = factory.GameModeDescriptor.Name,
				GameInstallPath = gameInstallPath,
				InstallInfoPath = Read(environment.Settings.InstallInfoFolder, id) ?? Path.Combine(root, "InstallInfo"),
				ModsPath = Read(environment.Settings.ModFolder, id) ?? Path.Combine(root, "Mods"),
				VirtualInstallPath = virtualPath,
				LinkFolderPath = linkRequired ? linkPath : null,
				LinkFolderRequired = linkRequired,
				CompatibleSharedModsGameIds = GameModeStorageSharingRegistry.GetMutuallyCompatibleModsStorageModeIds(id)
			};
		}

		private static string GetInitialRoot(IEnvironmentInfo environment, string id, string installPath)
		{
			try
			{
				string drive = String.IsNullOrWhiteSpace(installPath) ? null : Path.GetPathRoot(installPath);
				if (!String.IsNullOrWhiteSpace(drive) && Directory.Exists(drive))
					return Path.Combine(drive, "Games", "NMMCE", id);
			}
			catch (ArgumentException) { }
			catch (NotSupportedException) { }
			return Path.Combine(environment.ApplicationPersonalDataFolderPath, "Game Storage", id);
		}

		private static string Read(IDictionary<string, string> values, string id)
		{
			string value;
			return values != null && values.TryGetValue(id, out value) && !String.IsNullOrWhiteSpace(value) ? value : null;
		}

		private static bool Read(IDictionary<string, bool> values, string id)
		{
			bool value;
			return values != null && values.TryGetValue(id, out value) && value;
		}
	}
}
