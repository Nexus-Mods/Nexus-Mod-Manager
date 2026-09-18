using System;
using System.Collections.Generic;
using System.Globalization;
using Nexus.Client.ModManagement;
using Nexus.Client.Mods;

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
				if (mod == null || String.IsNullOrWhiteSpace(mod.ModArchivePath) ||
					!ModFileIdentity.IsSameRepositoryFile(mod.Id, mod.DownloadId, expectedModIdText, expectedFileIdText))
					continue;

				candidates.Add(new CollectionManagedArchiveCandidate(
					NexusCollectionModFileArtifactIdentity.Scheme,
					NexusCollectionModFileArtifactIdentity.Format(expectedDomain, expectedModId, expectedFileId),
					mod.ModArchivePath));
			}

			return candidates;
		}
	}
}
