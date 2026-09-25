using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Maps the generic C7.1 native observation into target-scoped installed identities and trusted Collection provenance.
	/// </summary>
	/// <remarks>
	/// This C7.2 reader records recipe-level identity/context only. It does not retain archive bytes, replay payloads,
	/// deployment fallbacks or non-file effects and therefore does not by itself make a Local Collection restorable offline.
	/// </remarks>
	public sealed class CollectionInstalledIdentityCaptureReader
	{
		private readonly NativeStateCaptureReader _nativeStateReader;
		private readonly Func<CollectionTargetIdentity, CollectionsAssociationTargetSnapshot> _associationSnapshotReader;
		private readonly string _nexusGameDomainName;

		/// <summary>Creates a C7.2 capture reader over generic native state and the feature-owned provenance store.</summary>
		public CollectionInstalledIdentityCaptureReader(NativeStateCaptureReader nativeStateReader,
			CollectionsAssociationStore associationStore, string nexusGameDomainName)
			: this(nativeStateReader, associationStore == null ? null : new Func<CollectionTargetIdentity, CollectionsAssociationTargetSnapshot>(associationStore.GetTargetSnapshot), nexusGameDomainName)
		{
		}

		internal CollectionInstalledIdentityCaptureReader(NativeStateCaptureReader nativeStateReader,
			Func<CollectionTargetIdentity, CollectionsAssociationTargetSnapshot> associationSnapshotReader, string nexusGameDomainName)
		{
			_nativeStateReader = nativeStateReader ?? throw new ArgumentNullException(nameof(nativeStateReader));
			_associationSnapshotReader = associationSnapshotReader;
			_nexusGameDomainName = String.IsNullOrWhiteSpace(nexusGameDomainName) ? null : nexusGameDomainName.Trim().ToLowerInvariant();
		}

		/// <summary>
		/// Captures the active native registrations and their exact current install contexts for one Collection target.
		/// </summary>
		public CollectionInstalledIdentitySnapshot Capture(CollectionTargetIdentity target)
		{
			if (target == null)
				throw new ArgumentNullException(nameof(target));
			return Capture(target, _nativeStateReader.Capture());
		}

		internal CollectionInstalledIdentitySnapshot Capture(CollectionTargetIdentity target, NativeStateCaptureSnapshot nativeState)
		{
			if (target == null)
				throw new ArgumentNullException(nameof(target));
			if (nativeState == null)
				throw new ArgumentNullException(nameof(nativeState));

			List<InstallLogReadMod> activeMods = nativeState.InstallLog.Mods.Where(x => !x.Hidden)
				.OrderBy(x => x.ModKey, StringComparer.Ordinal).ToList();
			if (activeMods.GroupBy(x => x.ModKey, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() != 1))
				throw new InvalidOperationException("Committed native state contains duplicate active mod keys and cannot be captured unambiguously.");

			var issues = new List<CollectionInstalledIdentityIssue>();
			NativeStateCaptureCoverage provenanceCoverage;
			CollectionsAssociationTargetSnapshot associations = CaptureAssociations(target, issues, out provenanceCoverage);
			Dictionary<string, List<CollectionInstalledMemberProvenance>> provenanceByNativeKey =
				BuildProvenance(target, activeMods, associations, issues);

			var mods = new List<CollectionInstalledModIdentity>(activeMods.Count);
			foreach (InstallLogReadMod mod in activeMods)
			{
				List<CollectionInstalledMemberProvenance> provenance;
				if (!provenanceByNativeKey.TryGetValue(mod.ModKey, out provenance))
					provenance = new List<CollectionInstalledMemberProvenance>();

				mods.Add(new CollectionInstalledModIdentity(mod.ModKey, CreateArchiveReference(mod), mod.NexusModId,
					mod.NexusFileId, mod.HumanReadableVersion, mod.MachineVersion, mod.HasInstallScript,
					new ModInstallContext(mod.InstallMethod, mod.InstallRoot), provenance));
			}

			return new CollectionInstalledIdentitySnapshot(target, nativeState.InstallLog.DeploymentCommitSequence,
				mods, provenanceCoverage, issues);
		}

		private CollectionsAssociationTargetSnapshot CaptureAssociations(CollectionTargetIdentity target,
			List<CollectionInstalledIdentityIssue> issues, out NativeStateCaptureCoverage coverage)
		{
			if (_associationSnapshotReader == null)
			{
				coverage = NativeStateCaptureCoverage.Unavailable;
				issues.Add(new CollectionInstalledIdentityIssue(CollectionInstalledIdentityIssueKind.AssociationStateUnavailable,
					"collections", "The Collections association store is unavailable; native identities remain captured without Collection provenance."));
				return null;
			}

			try
			{
				CollectionsAssociationTargetSnapshot snapshot = _associationSnapshotReader(target);
				if (snapshot == null || !target.Equals(snapshot.Target))
					throw new InvalidOperationException("The Collections association snapshot does not identify the requested target.");
				coverage = NativeStateCaptureCoverage.Complete;
				return snapshot;
			}
			catch (Exception exception) when (exception is FileNotFoundException ||
				exception is CollectionsStoreAccessException || exception is CollectionsStoreSchemaException)
			{
				coverage = NativeStateCaptureCoverage.Unavailable;
				issues.Add(new CollectionInstalledIdentityIssue(CollectionInstalledIdentityIssueKind.AssociationStateUnavailable,
					"collections", exception.Message));
				return null;
			}
		}

		private static Dictionary<string, List<CollectionInstalledMemberProvenance>> BuildProvenance(CollectionTargetIdentity target,
			IEnumerable<InstallLogReadMod> activeMods, CollectionsAssociationTargetSnapshot associations,
			List<CollectionInstalledIdentityIssue> issues)
		{
			var result = new Dictionary<string, List<CollectionInstalledMemberProvenance>>(StringComparer.OrdinalIgnoreCase);
			if (associations == null)
				return result;

			var activeKeys = new HashSet<string>(activeMods.Select(x => x.ModKey), StringComparer.OrdinalIgnoreCase);
			Dictionary<Guid, CollectionTargetAssociation> associationsById = associations.Associations
				.ToDictionary(x => x.AssociationId);
			foreach (CollectionMemberBinding binding in associations.Bindings
				.OrderBy(x => x.Association.AssociationId).ThenBy(x => (int)x.MemberKey.Kind).ThenBy(x => x.MemberKey.Value, StringComparer.Ordinal))
			{
				if (!target.Equals(binding.NativeMod.Target))
					continue;
				string nativeKey = binding.NativeMod.NativeModKey;
				if (!activeKeys.Contains(nativeKey))
				{
					issues.Add(new CollectionInstalledIdentityIssue(CollectionInstalledIdentityIssueKind.StaleAssociationBinding,
						nativeKey, "A persisted Collection member binding refers to a native mod registration that is not active in the captured InstallLog state."));
					continue;
				}

				CollectionTargetAssociation association;
				if (!associationsById.TryGetValue(binding.Association.AssociationId, out association))
					association = binding.Association;
				List<CollectionInstalledMemberProvenance> list;
				if (!result.TryGetValue(nativeKey, out list))
				{
					list = new List<CollectionInstalledMemberProvenance>();
					result.Add(nativeKey, list);
				}
				list.Add(new CollectionInstalledMemberProvenance(association.AssociationId, association.Revision,
					association.State, binding.MemberKey, binding.VerifiedRecipe, binding.BindingKind));
			}
			return result;
		}

		private CollectionInstalledArchiveReference CreateArchiveReference(InstallLogReadMod mod)
		{
			CollectionInstalledSourceIdentity sourceIdentity = CreateStableSourceIdentity(mod);
			bool available = !String.IsNullOrWhiteSpace(mod.ArchivePath) && File.Exists(mod.ArchivePath);
			return new CollectionInstalledArchiveReference(mod.ArchivePath, mod.FileName, available, sourceIdentity);
		}

		private CollectionInstalledSourceIdentity CreateStableSourceIdentity(InstallLogReadMod mod)
		{
			if (String.IsNullOrWhiteSpace(_nexusGameDomainName))
				return null;
			long modId;
			long fileId;
			if (!Int64.TryParse(mod.NexusModId, NumberStyles.None, CultureInfo.InvariantCulture, out modId) || modId <= 0 ||
				!Int64.TryParse(mod.NexusFileId, NumberStyles.None, CultureInfo.InvariantCulture, out fileId) || fileId <= 0)
				return null;

			return new CollectionInstalledSourceIdentity(NexusCollectionModFileArtifactIdentity.Scheme,
				NexusCollectionModFileArtifactIdentity.Format(_nexusGameDomainName, modId, fileId));
		}
	}
}
