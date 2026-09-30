using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
				if (!IsRepositoryFileCandidate(mod, _modManager.SortOrderService, expectedModIdText, expectedFileIdText))
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
		/// Determines whether one managed archive is a bounded candidate for immutable-byte verification.
		/// </summary>
		/// <remarks>
		/// Exact repository identity remains preferred. The legacy fallback deliberately accepts only a matching live ModId with
		/// a missing FileId; known contradictory metadata still rejects the archive. This keeps discovery bounded to one Nexus mod
		/// while the adopter performs the provider-backed exact-file verification before any identity is trusted.
		/// </remarks>
		internal static bool IsRepositoryFileCandidate(IMod mod, ModSortOrderService sortOrderService,
			string expectedModId, string expectedFileId)
		{
			if (MatchesRepositoryFileIdentity(mod, sortOrderService, expectedModId, expectedFileId))
				return true;
			if (mod == null || String.IsNullOrWhiteSpace(mod.ModArchivePath) ||
				!ModFileIdentity.IsUsableRepositoryId(expectedModId) ||
				!ModFileIdentity.IsUsableRepositoryId(expectedFileId))
				return false;

			bool hasLiveModId = ModFileIdentity.IsUsableRepositoryId(mod.Id);
			bool hasLiveFileId = ModFileIdentity.IsUsableRepositoryId(mod.DownloadId);
			if ((hasLiveModId && !StringComparer.OrdinalIgnoreCase.Equals(mod.Id, expectedModId)) ||
				(hasLiveFileId && !StringComparer.OrdinalIgnoreCase.Equals(mod.DownloadId, expectedFileId)))
				return false;

			return hasLiveModId && !hasLiveFileId;
		}

		/// <summary>
		/// Returns whether the repository metadata currently known for a managed archive does not contradict an exact
		/// Nexus mod/file identity. Callers must still independently verify immutable archive bytes before trusting it.
		/// </summary>
		internal static bool IsMetadataCompatibleForVerifiedContent(IMod mod, string expectedModId, string expectedFileId)
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

			return true;
		}

		/// <summary>
		/// Persists exact repository identity after immutable-byte verification without mutating live IMod metadata.
		/// </summary>
		internal static void ConfirmVerifiedRepositoryFileIdentity(ModManager modManager, IMod mod,
			string expectedModId, string expectedFileId)
		{
			if (modManager == null) throw new ArgumentNullException(nameof(modManager));
			if (!IsMetadataCompatibleForVerifiedContent(mod, expectedModId, expectedFileId))
				throw new InvalidDataException("The verified managed archive has contradictory repository metadata.");
			if (modManager.SortOrderService == null)
				throw new InvalidOperationException("Verified Collection archive reuse requires the native durable repository-identity store.");

			modManager.SortOrderService.ConfirmVerifiedRepositoryFileIdentity(mod, expectedModId, expectedFileId);
		}

		/// <summary>Chooses a stable native record when several records refer to byte-identical installation input.</summary>
		internal static IMod SelectDeterministicEquivalentManagedMod(IEnumerable<IMod> candidates)
		{
			if (candidates == null)
				throw new ArgumentNullException(nameof(candidates));
			IMod selected = candidates.Where(x => x != null).OrderBy(
				x => x.ModArchivePath ?? String.Empty, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
			if (selected == null)
				throw new InvalidOperationException("No managed archive candidate is available.");
			return selected;
		}

		/// <summary>Records exact repository identity after the adopter has verified this candidate's immutable bytes.</summary>
		internal void ConfirmVerifiedCandidate(CollectionManagedArchiveCandidate candidate, CollectionArtifactReference requestedArtifact)
		{
			if (candidate == null)
				throw new ArgumentNullException(nameof(candidate));
			if (requestedArtifact == null)
				throw new ArgumentNullException(nameof(requestedArtifact));

			string expectedDomain;
			long expectedModId;
			long expectedFileId;
			if (!NexusCollectionModFileArtifactIdentity.TryParse(requestedArtifact, out expectedDomain, out expectedModId, out expectedFileId))
				throw new InvalidDataException("The verified managed archive does not use a Nexus mod/file artifact identity.");

			string currentDomain = _modManager.ModRepository == null ? null : _modManager.ModRepository.GameDomainName;
			if (!StringComparer.OrdinalIgnoreCase.Equals(currentDomain, expectedDomain))
				throw new InvalidDataException("The verified managed archive belongs to a different Nexus game domain.");

			string candidatePath = Path.GetFullPath(candidate.ArchivePath);
			List<IMod> matches = _modManager.ManagedMods.Where(x => x != null && !String.IsNullOrWhiteSpace(x.ModArchivePath) &&
				StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(x.ModArchivePath), candidatePath)).ToList();
			if (matches.Count != 1)
				throw new InvalidDataException("The verified managed archive can no longer be resolved to exactly one native managed mod.");

			string modId = expectedModId.ToString(CultureInfo.InvariantCulture);
			string fileId = expectedFileId.ToString(CultureInfo.InvariantCulture);
			IMod mod = matches[0];
			if (!IsRepositoryFileCandidate(mod, _modManager.SortOrderService, modId, fileId))
				throw new InvalidDataException("The managed archive repository metadata changed while exact Collection verification was running.");
			if (_modManager.SortOrderService == null)
				throw new InvalidOperationException("Verified Collection archive reuse requires the native durable repository-identity store.");

			// Do not mutate live IMod metadata here: preparation fingerprints include those fields. The durable Sort identity row is
			// sufficient for later exact managed-archive resolution and does not make a read-only Collection plan stale.
			_modManager.SortOrderService.ConfirmVerifiedRepositoryFileIdentity(mod, modId, fileId);
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
