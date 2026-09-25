using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Classifies a limitation observed while mapping committed native registrations into a Local Collection identity capture.
	/// </summary>
	public enum CollectionInstalledIdentityIssueKind
	{
		AssociationStateUnavailable = 1,
		StaleAssociationBinding = 2
	}

	/// <summary>
	/// Records one non-fatal C7.2 identity/provenance capture limitation.
	/// </summary>
	public sealed class CollectionInstalledIdentityIssue
	{
		/// <summary>Creates one immutable installed-identity capture issue.</summary>
		public CollectionInstalledIdentityIssue(CollectionInstalledIdentityIssueKind kind, string resourceKey, string message)
		{
			if (!Enum.IsDefined(typeof(CollectionInstalledIdentityIssueKind), kind))
				throw new ArgumentOutOfRangeException(nameof(kind));
			Kind = kind;
			ResourceKey = resourceKey ?? String.Empty;
			Message = message ?? String.Empty;
		}

		public CollectionInstalledIdentityIssueKind Kind { get; }
		public string ResourceKey { get; }
		public string Message { get; }
	}

	/// <summary>
	/// Records a stable source identity reported by native mod metadata without claiming that current archive bytes were verified.
	/// </summary>
	public sealed class CollectionInstalledSourceIdentity
	{
		/// <summary>Creates one immutable recorded source identity.</summary>
		public CollectionInstalledSourceIdentity(string scheme, string stableId)
		{
			if (String.IsNullOrWhiteSpace(scheme))
				throw new ArgumentException("A source identity scheme is required.", nameof(scheme));
			if (String.IsNullOrWhiteSpace(stableId))
				throw new ArgumentException("A stable source identity is required.", nameof(stableId));
			Scheme = scheme;
			StableId = stableId;
		}

		public string Scheme { get; }
		public string StableId { get; }
	}

	/// <summary>
	/// Captures the currently available archive/source reference for one installed native mod.
	/// </summary>
	/// <remarks>
	/// <see cref="LiveArchivePath"/> is only a live source reference. <see cref="SourceIdentity"/> records provider/native
	/// provenance independently from filename, but neither value proves the current bytes or retains them for offline restore.
	/// </remarks>
	public sealed class CollectionInstalledArchiveReference
	{
		/// <summary>Creates one immutable archive/source identity observation.</summary>
		public CollectionInstalledArchiveReference(string liveArchivePath, string fileName, bool archiveCurrentlyAvailable,
			CollectionInstalledSourceIdentity sourceIdentity)
		{
			LiveArchivePath = liveArchivePath ?? String.Empty;
			FileName = fileName ?? String.Empty;
			ArchiveCurrentlyAvailable = archiveCurrentlyAvailable;
			SourceIdentity = sourceIdentity;
		}

		public string LiveArchivePath { get; }
		public string FileName { get; }
		public bool ArchiveCurrentlyAvailable { get; }
		public CollectionInstalledSourceIdentity SourceIdentity { get; }
	}

	/// <summary>
	/// Records trusted Collection provenance that currently maps one captured native registration to an existing Collection member.
	/// </summary>
	public sealed class CollectionInstalledMemberProvenance
	{
		/// <summary>Creates one detached association/member provenance observation.</summary>
		public CollectionInstalledMemberProvenance(Guid associationId, CollectionRevisionIdentity revision,
			CollectionAssociationState associationState, CollectionMemberKey memberKey, CollectionRecipeIdentity verifiedRecipe,
			CollectionMemberBindingKind bindingKind)
		{
			if (associationId == Guid.Empty)
				throw new ArgumentException("A non-empty association identifier is required.", nameof(associationId));
			Revision = revision ?? throw new ArgumentNullException(nameof(revision));
			if (!Enum.IsDefined(typeof(CollectionAssociationState), associationState) || associationState == CollectionAssociationState.Unknown)
				throw new ArgumentOutOfRangeException(nameof(associationState));
			MemberKey = memberKey ?? throw new ArgumentNullException(nameof(memberKey));
			VerifiedRecipe = verifiedRecipe ?? throw new ArgumentNullException(nameof(verifiedRecipe));
			if (!Enum.IsDefined(typeof(CollectionMemberBindingKind), bindingKind) || bindingKind == CollectionMemberBindingKind.Unknown)
				throw new ArgumentOutOfRangeException(nameof(bindingKind));
			AssociationId = associationId;
			AssociationState = associationState;
			BindingKind = bindingKind;
		}

		public Guid AssociationId { get; }
		public CollectionRevisionIdentity Revision { get; }
		public CollectionAssociationState AssociationState { get; }
		public CollectionMemberKey MemberKey { get; }
		public CollectionRecipeIdentity VerifiedRecipe { get; }
		public CollectionMemberBindingKind BindingKind { get; }
	}

	/// <summary>
	/// Identifies one active native registration as it existed when the C7.2 snapshot was captured.
	/// </summary>
	/// <remarks>
	/// <see cref="NativeSnapshotKey"/> is deliberately snapshot-scoped. Restore code must remap recreated native registrations
	/// instead of treating an InstallLog owner key as an eternal Local Collection member identifier.
	/// </remarks>
	public sealed class CollectionInstalledModIdentity
	{
		private readonly ReadOnlyCollection<CollectionInstalledMemberProvenance> _provenance;

		/// <summary>Creates one immutable installed native identity/context record.</summary>
		public CollectionInstalledModIdentity(string nativeSnapshotKey, CollectionInstalledArchiveReference archive,
			string nexusModId, string nexusFileId, string humanReadableVersion, string machineVersion, bool hasInstallScript,
			ModInstallContext installContext, IEnumerable<CollectionInstalledMemberProvenance> provenance)
		{
			if (String.IsNullOrWhiteSpace(nativeSnapshotKey))
				throw new ArgumentException("A native snapshot key is required.", nameof(nativeSnapshotKey));
			NativeSnapshotKey = nativeSnapshotKey;
			Archive = archive ?? throw new ArgumentNullException(nameof(archive));
			NexusModId = nexusModId ?? String.Empty;
			NexusFileId = nexusFileId ?? String.Empty;
			HumanReadableVersion = humanReadableVersion ?? String.Empty;
			MachineVersion = machineVersion ?? String.Empty;
			HasInstallScript = hasInstallScript;
			if (installContext == null)
				throw new ArgumentNullException(nameof(installContext));
			InstallContext = new ModInstallContext(installContext.Method, installContext.InstallRoot);
			List<CollectionInstalledMemberProvenance> copied = (provenance ?? throw new ArgumentNullException(nameof(provenance))).ToList();
			if (copied.Any(x => x == null))
				throw new ArgumentException("Installed-mod provenance cannot contain null records.", nameof(provenance));
			_provenance = new ReadOnlyCollection<CollectionInstalledMemberProvenance>(copied);
		}

		public string NativeSnapshotKey { get; }
		public CollectionInstalledArchiveReference Archive { get; }
		public string NexusModId { get; }
		public string NexusFileId { get; }
		public string HumanReadableVersion { get; }
		public string MachineVersion { get; }
		public bool HasInstallScript { get; }
		public ModInstallContext InstallContext { get; }
		public ReadOnlyCollection<CollectionInstalledMemberProvenance> Provenance { get { return _provenance; } }
	}

	/// <summary>
	/// Immutable target-scoped C7.2 mapping of the active native registrations that were actually installed at capture time.
	/// </summary>
	public sealed class CollectionInstalledIdentitySnapshot
	{
		/// <summary>Creates one immutable installed-identity snapshot.</summary>
		public CollectionInstalledIdentitySnapshot(CollectionTargetIdentity target, long deploymentCommitSequence,
			IEnumerable<CollectionInstalledModIdentity> mods, NativeStateCaptureCoverage provenanceCoverage,
			IEnumerable<CollectionInstalledIdentityIssue> issues)
		{
			Target = target ?? throw new ArgumentNullException(nameof(target));
			if (!Enum.IsDefined(typeof(NativeStateCaptureCoverage), provenanceCoverage))
				throw new ArgumentOutOfRangeException(nameof(provenanceCoverage));
			DeploymentCommitSequence = deploymentCommitSequence;
			Mods = Copy(mods, nameof(mods));
			ProvenanceCoverage = provenanceCoverage;
			Issues = Copy(issues, nameof(issues));
		}

		public CollectionTargetIdentity Target { get; }
		/// <summary>Gets the observed native deployment checkpoint for diagnostics; it is not a universal generation identifier.</summary>
		public long DeploymentCommitSequence { get; }
		public ReadOnlyCollection<CollectionInstalledModIdentity> Mods { get; }
		public NativeStateCaptureCoverage ProvenanceCoverage { get; }
		public ReadOnlyCollection<CollectionInstalledIdentityIssue> Issues { get; }

		private static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values, string parameterName)
		{
			if (values == null)
				throw new ArgumentNullException(parameterName);
			List<T> copied = values.ToList();
			if (copied.Any(x => ReferenceEquals(x, null)))
				throw new ArgumentException("An installed-identity snapshot cannot contain null records.", parameterName);
			return new ReadOnlyCollection<T>(copied);
		}
	}
}
