using System;
using System.Collections.Generic;
using System.Globalization;
using Nexus.Client.ModManagement;
using Nexus.Client.Mods;
using Nexus.Client.Mods.Formats.FOMod;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Finds existing NMM-managed Nexus archives by repository mod/file identity rather than filename.
	/// </summary>
	public sealed class ModManagerCollectionManagedArchiveSource : ICollectionManagedArchiveSource
	{
		private readonly ModManager _modManager;

		/// <summary>
		/// Creates an archive source over the live native managed-mod registry.
		/// </summary>
		public ModManagerCollectionManagedArchiveSource(ModManager modManager)
		{
			_modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
		}

		/// <inheritdoc />
		public IReadOnlyList<CollectionManagedArchiveCandidate> FindCandidates(CollectionArtifactReference requestedArtifact)
		{
			string expectedDomain;
			long expectedModId;
			long expectedFileId;
			if (!NexusCollectionModFileArtifactIdentity.TryParse(requestedArtifact, out expectedDomain, out expectedModId, out expectedFileId))
				return new CollectionManagedArchiveCandidate[0];

			string currentDomain = _modManager.ModRepository == null ? null : _modManager.ModRepository.GameDomainName;
			if (!StringComparer.OrdinalIgnoreCase.Equals(currentDomain, expectedDomain))
				return new CollectionManagedArchiveCandidate[0];

			string expectedModIdText = expectedModId.ToString(CultureInfo.InvariantCulture);
			string expectedFileIdText = expectedFileId.ToString(CultureInfo.InvariantCulture);
			var candidates = new List<CollectionManagedArchiveCandidate>();
			foreach (IMod mod in _modManager.ManagedMods)
			{
				if (!MatchesRepositoryFileIdentity(mod, _modManager.SortOrderService, expectedModIdText, expectedFileIdText))
					continue;

				candidates.Add(new CollectionManagedArchiveCandidate(
					NexusCollectionModFileArtifactIdentity.Scheme,
					NexusCollectionModFileArtifactIdentity.Format(expectedDomain, expectedModId, expectedFileId),
					mod.ModArchivePath));
			}

			return candidates;
		}

		/// <summary>
		/// Matches one managed archive against an exact requested repository file identity.
		/// </summary>
		/// <remarks>
		/// Live metadata wins when present. For older metadata-disabled downloads, durable Sort history may supply the missing
		/// provenance, but callers that reuse bytes still perform independent immutable content verification.
		/// </remarks>
		internal static bool MatchesRepositoryFileIdentity(IMod mod, ModSortOrderService sortOrderService,
			string expectedModId, string expectedFileId)
		{
			if (mod == null || String.IsNullOrWhiteSpace(mod.ModArchivePath) ||
				!ModFileIdentity.IsUsableRepositoryId(expectedModId) ||
				!ModFileIdentity.IsUsableRepositoryId(expectedFileId))
				return false;

			bool hasLiveModId = ModFileIdentity.IsUsableRepositoryId(mod.Id);
			bool hasLiveFileId = ModFileIdentity.IsUsableRepositoryId(mod.DownloadId);
			if ((hasLiveModId && !StringComparer.OrdinalIgnoreCase.Equals(mod.Id, expectedModId)) ||
				(hasLiveFileId && !StringComparer.OrdinalIgnoreCase.Equals(mod.DownloadId, expectedFileId)))
				return false;

			if (hasLiveModId && hasLiveFileId)
				return true;

			return sortOrderService != null && sortOrderService.HasDurableRepositoryFileIdentity(
				mod.ModArchivePath, expectedModId, expectedFileId);
		}

		/// <summary>
		/// Resolves the current live/resolved repository identity without consulting historical path rows.
		/// </summary>
		internal static bool TryResolveRepositoryFileIdentity(IMod mod, ModSortOrderService sortOrderService,
			out string modId, out string fileId)
		{
			modId = null;
			fileId = null;
			if (mod == null)
				return false;

			bool hasLiveModId = ModFileIdentity.IsUsableRepositoryId(mod.Id);
			bool hasLiveFileId = ModFileIdentity.IsUsableRepositoryId(mod.DownloadId);
			if (hasLiveModId && hasLiveFileId)
			{
				modId = mod.Id;
				fileId = mod.DownloadId;
				return true;
			}

			if (sortOrderService == null || String.IsNullOrWhiteSpace(mod.ModArchivePath))
				return false;

			ModSortOrderRecord assignment;
			if (!sortOrderService.TryGetResolvedAssignment(mod.ModArchivePath, out assignment) || assignment == null ||
				!ModFileIdentity.IsUsableRepositoryId(assignment.ModId) ||
				!ModFileIdentity.IsUsableRepositoryId(assignment.DownloadId))
				return false;

			if ((hasLiveModId && !StringComparer.OrdinalIgnoreCase.Equals(mod.Id, assignment.ModId)) ||
				(hasLiveFileId && !StringComparer.OrdinalIgnoreCase.Equals(mod.DownloadId, assignment.DownloadId)))
				return false;

			modId = assignment.ModId;
			fileId = assignment.DownloadId;
			return true;
		}
	}
}
