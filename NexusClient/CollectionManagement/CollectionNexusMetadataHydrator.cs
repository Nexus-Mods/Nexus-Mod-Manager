using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Nexus.Client.ModManagement;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Best-effort post-commit metadata reconciliation for Nexus-backed Collection members.
	/// </summary>
	/// <remarks>
	/// Exact repository identity is recovered from the live mod or the durable Sort identity row established during
	/// Collection archive verification. Descriptive Nexus metadata is populated only after Collection mutation/recovery
	/// boundaries are complete, because these fields participate in the native-state fingerprint used by C6 planning.
	/// </remarks>
	internal sealed class CollectionNexusMetadataHydrator
	{
		private readonly ModManager _modManager;

		public CollectionNexusMetadataHydrator(ModManager modManager)
		{
			_modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
		}

		/// <summary>
		/// Enriches the native mods referenced by the supplied Collection bindings. Non-Nexus/bundled members are skipped.
		/// Failures are traced per member and never change Collection durability state.
		/// </summary>
		public int Enrich(IEnumerable<CollectionMemberBinding> bindings)
		{
			if (bindings == null)
				throw new ArgumentNullException(nameof(bindings));

			var processedNativeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var pageInfoCache = new Dictionary<string, IModInfo>(StringComparer.OrdinalIgnoreCase);
			var fileInfoCache = new Dictionary<string, IModFileInfo>(StringComparer.OrdinalIgnoreCase);
			int enrichedCount = 0;

			foreach (CollectionMemberBinding binding in bindings)
			{
				if (binding == null || !processedNativeKeys.Add(binding.NativeMod.NativeModKey))
					continue;

				List<IMod> nativeMatches = _modManager.InstallationLog.ActiveMods.Where(x => x != null &&
					StringComparer.OrdinalIgnoreCase.Equals(_modManager.InstallationLog.GetModKey(x), binding.NativeMod.NativeModKey)).ToList();
				if (nativeMatches.Count != 1)
				{
					Trace.TraceWarning("Collection metadata enrichment skipped native key '{0}': resolved to {1} active mods.",
						binding.NativeMod.NativeModKey, nativeMatches.Count);
					continue;
				}

				IMod mod = nativeMatches[0];
				string modId;
				string fileId;
				if (!ModManagerCollectionManagedArchiveSource.TryResolveRepositoryFileIdentity(
					mod, _modManager.SortOrderService, out modId, out fileId))
					continue;

				try
				{
					// Repository identity is exact Collection provenance and must not depend on the generic
					// "add missing info" setting. Keep the entire metadata repair best-effort: the Collection
					// association has already been durably finalized before this service is called.
					AddModTask.ApplyTrustedRepositoryIdentity(mod, modId, fileId);
					if (_modManager.SortOrderService != null)
						_modManager.SortOrderService.ConfirmVerifiedRepositoryFileIdentity(mod, modId, fileId);

					if (!NeedsNexusMetadataEnrichment(mod) || _modManager.ModRepository == null || _modManager.ModRepository.IsOffline)
						continue;

					IModInfo pageInfo;
					if (!pageInfoCache.TryGetValue(modId, out pageInfo))
					{
						pageInfo = _modManager.ModRepository.GetModInfo(modId);
						pageInfoCache[modId] = pageInfo;
					}

					string fileKey = modId + "/" + fileId;
					IModFileInfo fileInfo;
					if (!fileInfoCache.TryGetValue(fileKey, out fileInfo))
					{
						fileInfo = _modManager.ModRepository.GetFileInfo(modId, fileId);
						fileInfoCache[fileKey] = fileInfo;
					}

					if (pageInfo == null)
						continue;

					ModInfo enriched;
					if (fileInfo != null)
					{
						IModInfo combined = AutoTagger.CombineInfo(pageInfo, fileInfo);
						if (combined == null)
							continue;
						enriched = new ModInfo(combined);
					}
					else
					{
						// Preserve normal AddMod semantics: mod-page version must not masquerade as exact file version.
						enriched = new ModInfo(pageInfo)
						{
							HumanReadableVersion = null,
							LastKnownVersion = null,
							MachineVersion = null
						};
					}

					enriched.Id = modId;
					enriched.DownloadId = fileId;
					enriched.CustomCategoryId = mod.CustomCategoryId;
					enriched.InstallDate = mod.InstallDate;
					enriched.IsEndorsed = mod.IsEndorsed;
					enriched.UpdateWarningEnabled = mod.UpdateWarningEnabled;
					enriched.UpdateChecksEnabled = mod.UpdateChecksEnabled;
					mod.UpdateInfo(enriched, false);
					enrichedCount++;
				}
				catch (Exception ex)
				{
					Trace.TraceWarning("Collection metadata enrichment failed for Nexus mod/file {0}/{1} (native key '{2}'): {3}",
						modId, fileId, binding.NativeMod.NativeModKey, ex);
				}
			}

			return enrichedCount;
		}

		private static bool NeedsNexusMetadataEnrichment(IMod mod)
		{
			if (mod == null)
				return false;
			string archiveName = String.IsNullOrWhiteSpace(mod.ModArchivePath)
				? String.Empty
				: Path.GetFileNameWithoutExtension(mod.ModArchivePath);
			return String.IsNullOrWhiteSpace(mod.ModName) ||
				(!String.IsNullOrWhiteSpace(archiveName) && StringComparer.OrdinalIgnoreCase.Equals(mod.ModName, archiveName)) ||
				String.IsNullOrWhiteSpace(mod.HumanReadableVersion) ||
				String.IsNullOrWhiteSpace(mod.LastKnownVersion) ||
				String.IsNullOrWhiteSpace(mod.Author) ||
				mod.CategoryId <= 0;
		}
	}
}
