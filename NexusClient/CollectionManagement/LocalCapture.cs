using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Describes the restoration promise made by a sealed Local Collection capture.
	/// </summary>
	public enum LocalCaptureCapability
	{
		Unknown = 0,

		/// <summary>
		/// The snapshot records intended reconstruction, but missing content/effects may require downloads or manual work.
		/// </summary>
		RecipeOnly = 1,

		/// <summary>
		/// Required content and native restoration information for the declared scope have been retained and verified.
		/// </summary>
		LocallyRestorableWithinScope = 2
	}

	/// <summary>
	/// Immutable contract for one sealed Local Collection capture of an actual NMM-managed setup.
	/// </summary>
	/// <remarks>
	/// In-progress capture belongs to the later operation/journal layer. An instance of this class represents only a
	/// published snapshot contract. Restoring it is a replacement operation; this type itself performs no native writes.
	/// </remarks>
	public sealed class LocalCapture
	{
		/// <summary>Gets the current serialized Local Collection capture schema version.</summary>
		public const int CurrentSchemaVersion = 1;

		/// <summary>Gets the current restoration-capability contract version.</summary>
		public const int CurrentCapabilityVersion = 1;

		private readonly ReadOnlyCollection<RetainedArtifactReference> _retainedArtifacts;
		private readonly ReadOnlyCollection<LocalCaptureExclusion> _exclusions;
		private readonly ReadOnlyCollection<LocalCaptureNativeRecordMapping> _nativeRecordMappings;

		/// <summary>
		/// Creates a sealed Local Collection capture contract.
		/// </summary>
		public LocalCapture(
			LocalCaptureIdentity identity,
			CollectionRevisionIdentity revision,
			CollectionTargetIdentity sourceTarget,
			CollectionCurrentStateFingerprint capturedStateFingerprint,
			LocalCaptureScope scope,
			LocalCaptureCapability capability,
			IEnumerable<RetainedArtifactReference> retainedArtifacts,
			IEnumerable<LocalCaptureExclusion> exclusions,
			IEnumerable<LocalCaptureNativeRecordMapping> nativeRecordMappings)
			: this(identity, revision, sourceTarget, capturedStateFingerprint, scope, capability,
				CurrentSchemaVersion, CurrentCapabilityVersion, retainedArtifacts, exclusions, nativeRecordMappings)
		{
		}

		/// <summary>
		/// Creates a sealed Local Collection capture contract with explicit persisted schema/capability versions.
		/// </summary>
		public LocalCapture(
			LocalCaptureIdentity identity,
			CollectionRevisionIdentity revision,
			CollectionTargetIdentity sourceTarget,
			CollectionCurrentStateFingerprint capturedStateFingerprint,
			LocalCaptureScope scope,
			LocalCaptureCapability capability,
			int schemaVersion,
			int capabilityVersion,
			IEnumerable<RetainedArtifactReference> retainedArtifacts,
			IEnumerable<LocalCaptureExclusion> exclusions,
			IEnumerable<LocalCaptureNativeRecordMapping> nativeRecordMappings)
		{
			if (identity == null)
				throw new ArgumentNullException(nameof(identity));
			if (revision == null)
				throw new ArgumentNullException(nameof(revision));
			if (revision.Collection.Origin != CollectionOrigin.Local)
				throw new ArgumentException("A Local Collection capture must belong to a Local Collection revision.", nameof(revision));
			if (sourceTarget == null)
				throw new ArgumentNullException(nameof(sourceTarget));
			if (capturedStateFingerprint == null)
				throw new ArgumentNullException(nameof(capturedStateFingerprint));
			if (scope == null)
				throw new ArgumentNullException(nameof(scope));
			if (!Enum.IsDefined(typeof(LocalCaptureCapability), capability) || capability == LocalCaptureCapability.Unknown)
				throw new ArgumentOutOfRangeException(nameof(capability));
			if (schemaVersion <= 0)
				throw new ArgumentOutOfRangeException(nameof(schemaVersion));
			if (capabilityVersion <= 0)
				throw new ArgumentOutOfRangeException(nameof(capabilityVersion));
			if (retainedArtifacts == null)
				throw new ArgumentNullException(nameof(retainedArtifacts));
			if (exclusions == null)
				throw new ArgumentNullException(nameof(exclusions));
			if (nativeRecordMappings == null)
				throw new ArgumentNullException(nameof(nativeRecordMappings));

			List<RetainedArtifactReference> copiedArtifacts = new List<RetainedArtifactReference>();
			HashSet<string> retainedArtifactRoles = new HashSet<string>(StringComparer.Ordinal);
			Dictionary<string, RetainedArtifactReference> retainedArtifactsById =
				new Dictionary<string, RetainedArtifactReference>(StringComparer.Ordinal);
			foreach (RetainedArtifactReference artifact in retainedArtifacts)
			{
				if (artifact == null)
					throw new ArgumentException("A capture cannot contain a null retained artifact reference.", nameof(retainedArtifacts));
				if (!retainedArtifactRoles.Add(artifact.Role))
					throw new ArgumentException("A capture cannot bind the same retained-artifact role more than once.", nameof(retainedArtifacts));

				RetainedArtifactReference existingArtifact;
				if (retainedArtifactsById.TryGetValue(artifact.StableArtifactId, out existingArtifact))
				{
					if (!existingArtifact.ContentHash.Equals(artifact.ContentHash) || existingArtifact.ByteLength != artifact.ByteLength)
						throw new ArgumentException("References to the same retained artifact identity must agree on SHA-256 and length.", nameof(retainedArtifacts));
				}
				else
					retainedArtifactsById.Add(artifact.StableArtifactId, artifact);

				copiedArtifacts.Add(artifact);
			}

			List<LocalCaptureExclusion> copiedExclusions = new List<LocalCaptureExclusion>();
			HashSet<string> exclusionKeys = new HashSet<string>(StringComparer.Ordinal);
			foreach (LocalCaptureExclusion exclusion in exclusions)
			{
				if (exclusion == null)
					throw new ArgumentException("A capture cannot contain a null scope exclusion.", nameof(exclusions));
				if (!scope.Contains(exclusion.Area))
					throw new ArgumentException("A capture exclusion must refer to an area declared by the capture scope.", nameof(exclusions));

				string key = ((int)exclusion.Area).ToString() + ":" + exclusion.Code;
				if (!exclusionKeys.Add(key))
					throw new ArgumentException("A capture cannot contain duplicate exclusion codes for the same scope area.", nameof(exclusions));

				copiedExclusions.Add(exclusion);
			}

			List<LocalCaptureNativeRecordMapping> copiedMappings = new List<LocalCaptureNativeRecordMapping>();
			HashSet<CollectionMemberKey> snapshotMembers = new HashSet<CollectionMemberKey>();
			HashSet<NativeModInstanceIdentity> nativeInstances = new HashSet<NativeModInstanceIdentity>();
			foreach (LocalCaptureNativeRecordMapping mapping in nativeRecordMappings)
			{
				if (mapping == null)
					throw new ArgumentException("A capture cannot contain a null native record mapping.", nameof(nativeRecordMappings));
				if (!Equals(mapping.SourceNativeInstance.Target, sourceTarget))
					throw new ArgumentException("Every captured native record must belong to the capture source target.", nameof(nativeRecordMappings));
				if (!snapshotMembers.Add(mapping.SnapshotMemberKey))
					throw new ArgumentException("A snapshot member cannot map to more than one source native record.", nameof(nativeRecordMappings));
				if (!nativeInstances.Add(mapping.SourceNativeInstance))
					throw new ArgumentException("A source native record cannot map to more than one snapshot member.", nameof(nativeRecordMappings));

				copiedMappings.Add(mapping);
			}

			Identity = identity;
			Revision = revision;
			SourceTarget = sourceTarget;
			CapturedStateFingerprint = capturedStateFingerprint;
			Scope = scope;
			Capability = capability;
			SchemaVersion = schemaVersion;
			CapabilityVersion = capabilityVersion;
			_retainedArtifacts = new ReadOnlyCollection<RetainedArtifactReference>(copiedArtifacts);
			_exclusions = new ReadOnlyCollection<LocalCaptureExclusion>(copiedExclusions);
			_nativeRecordMappings = new ReadOnlyCollection<LocalCaptureNativeRecordMapping>(copiedMappings);
		}

		/// <summary>
		/// Gets the collection-native capture identity.
		/// </summary>
		public LocalCaptureIdentity Identity { get; }

		/// <summary>
		/// Gets the sealed Local Collection revision that owns this capture.
		/// </summary>
		public CollectionRevisionIdentity Revision { get; }

		/// <summary>
		/// Gets the real target whose managed state was captured.
		/// </summary>
		public CollectionTargetIdentity SourceTarget { get; }

		/// <summary>
		/// Gets the versioned fingerprint of the managed state accepted at capture time.
		/// </summary>
		public CollectionCurrentStateFingerprint CapturedStateFingerprint { get; }

		/// <summary>
		/// Gets the explicit versioned scope of the capture promise.
		/// </summary>
		public LocalCaptureScope Scope { get; }

		/// <summary>
		/// Gets the restoration capability promised by this sealed capture.
		/// </summary>
		public LocalCaptureCapability Capability { get; }

		/// <summary>Gets the persisted Local Collection capture schema version.</summary>
		public int SchemaVersion { get; }

		/// <summary>Gets the persisted restoration-capability contract version.</summary>
		public int CapabilityVersion { get; }

		/// <summary>
		/// Gets immutable content retained for this capture.
		/// </summary>
		public ReadOnlyCollection<RetainedArtifactReference> RetainedArtifacts
		{
			get { return _retainedArtifacts; }
		}

		/// <summary>
		/// Gets explicit limitations accepted as outside the capture promise.
		/// </summary>
		public ReadOnlyCollection<LocalCaptureExclusion> Exclusions
		{
			get { return _exclusions; }
		}

		/// <summary>
		/// Gets capture-time mappings from collection-native snapshot members to source native registrations.
		/// </summary>
		public ReadOnlyCollection<LocalCaptureNativeRecordMapping> NativeRecordMappings
		{
			get { return _nativeRecordMappings; }
		}

		/// <summary>
		/// Gets whether this capture only records reconstruction intent and may require external acquisition/manual work.
		/// </summary>
		public bool IsRecipeOnly
		{
			get { return Capability == LocalCaptureCapability.RecipeOnly; }
		}

		/// <summary>
		/// Gets whether the snapshot has retained and verified the information/content required to restore its declared scope.
		/// </summary>
		public bool IsLocallyRestorableWithinScope
		{
			get { return Capability == LocalCaptureCapability.LocallyRestorableWithinScope; }
		}
	}
}
