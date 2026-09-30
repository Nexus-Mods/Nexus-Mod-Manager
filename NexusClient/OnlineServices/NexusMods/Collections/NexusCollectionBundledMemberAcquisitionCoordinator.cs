using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Nexus.Client.CollectionManagement;
using Nexus.Client.ModAuthoring;
using Nexus.Client.ModManagement;
using Nexus.Client.Mods;

namespace Nexus.Client.OnlineServices.NexusMods.Collections
{
	/// <summary>
	/// Imports deterministic embedded Collection member archives through NMM's ordinary AddMod registry before native recipe preparation.
	/// </summary>
	public sealed class NexusCollectionBundledMemberAcquisitionCoordinator : ICollectionBundledMemberAcquisitionCoordinator
	{
		private readonly NexusCollectionBundledArtifactMaterializer _materializer;
		private readonly CollectionVerifiedArchiveAdopter _archiveAdopter;
		private readonly CollectionAcquisitionRequestCoordinator _requestCoordinator;
		private readonly ModManager _modManager;

		public NexusCollectionBundledMemberAcquisitionCoordinator(NexusCollectionBundledArtifactMaterializer materializer,
			CollectionVerifiedArchiveAdopter archiveAdopter, CollectionAcquisitionRequestCoordinator requestCoordinator,
			ModManager modManager)
		{
			_materializer = materializer ?? throw new ArgumentNullException(nameof(materializer));
			_archiveAdopter = archiveAdopter ?? throw new ArgumentNullException(nameof(archiveAdopter));
			_requestCoordinator = requestCoordinator ?? throw new ArgumentNullException(nameof(requestCoordinator));
			_modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
		}

		public bool Supports(CollectionArtifactReference artifact)
		{
			return CollectionBundledArtifactIdentity.IsBundle(artifact);
		}

		public CollectionBundledMemberAcquisitionResult Begin(CollectionAcquisitionRequest request,
			CollectionArchiveOverwritePolicy archiveOverwritePolicy, ConfirmOverwriteCallback confirmOverwriteCallback,
			CancellationToken cancellationToken)
		{
			ValidateRequest(request);
			if (archiveOverwritePolicy == null) throw new ArgumentNullException(nameof(archiveOverwritePolicy));
			NexusCollectionBundledArtifactMaterialization materialized = _materializer.Materialize(request, cancellationToken);
			CollectionVerifiedArchive verified = _archiveAdopter.AdoptRevisionBundleMaterialization(
				request, materialized.Artifact, cancellationToken);

			List<IMod> exactManaged = CollectionArchiveContentMatcher.FindExactManagedMods(_modManager, verified.Artifact, cancellationToken);
			if (exactManaged.Count > 1)
				throw new InvalidDataException("Multiple native managed archives contain the exact materialized bytes for one Collection bundle member; review is required before preparation.");
			if (exactManaged.Count == 1)
			{
				_materializer.CleanupStaging(request);
				return new CollectionBundledMemberAcquisitionResult(verified, null);
			}

			CollectionAcquisitionQueueCorrelation correlation = _requestCoordinator.QueueMaterializedLocal(
				request, materialized.StagingPath, confirmOverwriteCallback, archiveOverwritePolicy);
			return new CollectionBundledMemberAcquisitionResult(verified, correlation);
		}

		public CollectionVerifiedArchive TryComplete(CollectionAcquisitionRequest request, CancellationToken cancellationToken)
		{
			ValidateRequest(request);
			CollectionVerifiedArchive verified = _archiveAdopter.TryAdopt(request, cancellationToken);
			if (verified == null)
			{
				NexusCollectionBundledArtifactMaterialization materialized = _materializer.Materialize(request, cancellationToken);
				verified = _archiveAdopter.AdoptRevisionBundleMaterialization(request, materialized.Artifact, cancellationToken);
			}

			List<IMod> exactManaged = CollectionArchiveContentMatcher.FindExactManagedMods(_modManager, verified.Artifact, cancellationToken);
			if (exactManaged.Count > 1)
				throw new InvalidDataException("Multiple native managed archives contain the exact materialized bytes for one Collection bundle member; review is required before preparation.");
			if (exactManaged.Count == 0)
				return null;

			_materializer.CleanupStaging(request);
			return verified;
		}

		private void ValidateRequest(CollectionAcquisitionRequest request)
		{
			if (request == null) throw new ArgumentNullException(nameof(request));
			if (!Supports(request.SelectedArtifact))
				throw new ArgumentException("The acquisition request is not a characterized Collection bundle artifact.", nameof(request));
		}

	}
}
