using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.Games;
using Nexus.Client.GameStorage;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement
{
	/// <summary>Builds a scan-scoped Collection provenance index without inspecting deployed files.</summary>
	internal static class FileManagerCollectionSourceLookup
	{
		/// <summary>Copies native-to-Virtual owner identities on the UI thread before background scanning begins.</summary>
		internal static IDictionary<string, string> CaptureRegistrationKeys(ModManager manager)
		{
			var keys = new Dictionary<string, string>(StringComparer.Ordinal);
			try
			{
				foreach (IMod mod in manager.InstallationLog.ActiveMods)
				{
					string nativeKey = manager.InstallationLog.GetModKey(mod);
					if (!String.IsNullOrEmpty(nativeKey))
						keys[nativeKey] = FileManagerQueryService.CreateOwnerKey(Path.GetFileName(mod.Filename), mod.DownloadId);
				}
			}
			catch (Exception ex)
			{
				keys.Clear();
				Trace.TraceWarning("File Manager Collection registration snapshot unavailable: {0}", ex.Message);
			}
			return keys;
		}

		/// <summary>Reads existing target provenance once; missing or unavailable tracking leaves normal NMM classification intact.</summary>
		internal static ISet<string> Load(IGameMode gameMode, IEnvironmentInfo environmentInfo, IDictionary<string, string> registrationKeys)
		{
			try
			{
				if (registrationKeys.Count == 0)
					return null;
				var storageService = new GameStorageService(environmentInfo);
				GameStoragePathSet paths = storageService.FromGameMode(gameMode);
				var store = new CollectionsStore(paths);
				if (!store.Exists)
					return null;
				CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(storageService).Resolve(paths);
				IReadOnlyList<string> installedKeys = new CollectionsAssociationStore(store).GetInstalledForCollectionModKeys(authority.Target);
				return BuildOwnerKeys(installedKeys, registrationKeys);
			}
			catch (Exception ex)
			{
				Trace.TraceWarning("File Manager Collection provenance unavailable: {0}", ex.Message);
				return null;
			}
		}

		/// <summary>Maps exact native registrations to both deployment key formats, excluding ambiguous Virtual aliases.</summary>
		internal static ISet<string> BuildOwnerKeys(IEnumerable<string> installedKeys, IDictionary<string, string> registrationKeys)
		{
			var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var ambiguousAliases = new HashSet<string>(registrationKeys.Values.GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
				.Where(x => x.Count() > 1).Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
			foreach (string nativeKey in installedKeys)
			{
				string virtualKey;
				if (!registrationKeys.TryGetValue(nativeKey, out virtualKey))
					continue;
				result.Add(nativeKey);
				if (!String.IsNullOrEmpty(virtualKey) && !ambiguousAliases.Contains(virtualKey))
					result.Add(virtualKey);
			}
			return result;
		}
	}
}
