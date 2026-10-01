using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Immutable C8.1 snapshot of the current managed setup plus Collection decision inputs that are not part of C6 native fingerprinting.
	/// </summary>
	/// <remarks>
	/// The embedded native-state index remains authoritative for observed installed effects. Drift and standalone provenance are
	/// captured separately because the existing C6 fingerprint deliberately does not bind those replacement-specific decisions.
	/// </remarks>
	public sealed class CollectionReplacementCurrentSetupSnapshot
	{
		private readonly ReadOnlyDictionary<Guid, CollectionAssociationCustomization> _customizationsByAssociation;
		private readonly ReadOnlyDictionary<NativeModInstanceIdentity, NativeModProvenance> _provenanceByNativeMod;
		private readonly ReadOnlyCollection<CollectionDriftObservation> _driftObservations;

		/// <summary>Creates one replacement-planning snapshot from one coherent native/feature-state read.</summary>
		public CollectionReplacementCurrentSetupSnapshot(CollectionNativeStateIndex nativeState,
			IEnumerable<CollectionDriftObservation> driftObservations, IEnumerable<NativeModProvenance> provenance)
		{
			NativeState = nativeState ?? throw new ArgumentNullException(nameof(nativeState));
			if (driftObservations == null)
				throw new ArgumentNullException(nameof(driftObservations));
			if (provenance == null)
				throw new ArgumentNullException(nameof(provenance));

			List<CollectionDriftObservation> drift = driftObservations.ToList();
			if (drift.Any(x => x == null))
				throw new ArgumentException("A replacement current-setup snapshot cannot contain a null drift observation.", nameof(driftObservations));
			foreach (CollectionDriftObservation observation in drift)
			{
				CollectionTargetAssociation association;
				if (!NativeState.Associations.TryGetValue(observation.Requirement.AssociationId, out association) ||
					!observation.Requirement.Target.Equals(NativeState.Target) ||
					!observation.Requirement.BaselineRevision.Equals(association.Revision))
					throw new ArgumentException("Every replacement drift observation must belong to an association in the captured native-state index.", nameof(driftObservations));
			}
			_driftObservations = new ReadOnlyCollection<CollectionDriftObservation>(drift
				.OrderBy(x => x.ObservationId).ToList());

			Dictionary<NativeModInstanceIdentity, NativeModProvenance> persistedProvenance =
				new Dictionary<NativeModInstanceIdentity, NativeModProvenance>();
			foreach (NativeModProvenance item in provenance)
			{
				if (item == null)
					throw new ArgumentException("A replacement current-setup snapshot cannot contain null native provenance.", nameof(provenance));
				if (!item.NativeMod.Target.Equals(NativeState.Target))
					throw new ArgumentException("Replacement native provenance belongs to another target.", nameof(provenance));
				if (persistedProvenance.ContainsKey(item.NativeMod))
					throw new ArgumentException("Replacement native provenance cannot contain duplicate native mod identities.", nameof(provenance));
				persistedProvenance.Add(item.NativeMod, item);
			}

			var currentProvenance = new Dictionary<NativeModInstanceIdentity, NativeModProvenance>();
			foreach (CollectionNativeModState mod in NativeState.Mods.Values)
			{
				NativeModProvenance item;
				if (!persistedProvenance.TryGetValue(mod.Identity, out item))
					item = new NativeModProvenance(mod.Identity, StandaloneModUse.Unknown);
				currentProvenance.Add(mod.Identity, item);
			}
			_provenanceByNativeMod = new ReadOnlyDictionary<NativeModInstanceIdentity, NativeModProvenance>(currentProvenance);

			var customizations = new Dictionary<Guid, CollectionAssociationCustomization>();
			foreach (CollectionTargetAssociation association in NativeState.Associations.Values)
			{
				ReadOnlyCollection<UserOverride> overrides;
				NativeState.OverridesByAssociation.TryGetValue(association.AssociationId, out overrides);
				CollectionDriftObservation[] associationDrift = _driftObservations
					.Where(x => x.Requirement.AssociationId == association.AssociationId).ToArray();
				customizations.Add(association.AssociationId, new CollectionAssociationCustomization(association,
					overrides ?? new ReadOnlyCollection<UserOverride>(new List<UserOverride>()), associationDrift));
			}
			_customizationsByAssociation = new ReadOnlyDictionary<Guid, CollectionAssociationCustomization>(customizations);
			DecisionFingerprint = BuildDecisionFingerprint(this);
		}

		/// <summary>Gets the target represented by the snapshot.</summary>
		public CollectionTargetIdentity Target { get { return NativeState.Target; } }

		/// <summary>Gets the detached native-state observation used for all C8.1 managed-effect facts.</summary>
		public CollectionNativeStateIndex NativeState { get; }

		/// <summary>Gets the complete current drift observations captured with the association snapshot.</summary>
		public ReadOnlyCollection<CollectionDriftObservation> DriftObservations { get { return _driftObservations; } }

		/// <summary>Gets replacement-relevant customization/drift grouped by current association.</summary>
		public IReadOnlyDictionary<Guid, CollectionAssociationCustomization> CustomizationsByAssociation
		{
			get { return _customizationsByAssociation; }
		}

		/// <summary>
		/// Gets standalone-use provenance for every currently registered native mod. Missing persisted evidence is materialized as Unknown.
		/// </summary>
		public IReadOnlyDictionary<NativeModInstanceIdentity, NativeModProvenance> ProvenanceByNativeMod
		{
			get { return _provenanceByNativeMod; }
		}

		/// <summary>
		/// Gets a replacement-specific decision fingerprint binding the native fingerprint plus drift and standalone provenance.
		/// </summary>
		public CollectionCurrentStateFingerprint DecisionFingerprint { get; }

		private static CollectionCurrentStateFingerprint BuildDecisionFingerprint(CollectionReplacementCurrentSetupSnapshot snapshot)
		{
			using (var stream = new MemoryStream())
			using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), true))
			{
				Write(writer, snapshot.NativeState.Fingerprint.FormatVersion);
				Write(writer, snapshot.NativeState.Fingerprint.Value);

				writer.Write(snapshot.DriftObservations.Count);
				foreach (CollectionDriftObservation drift in snapshot.DriftObservations)
				{
					Write(writer, drift.ObservationId.ToString("D"));
					WriteRequirement(writer, drift.Requirement);
					WriteState(writer, drift.ExpectedState);
					WriteState(writer, drift.ObservedState);
				}

				writer.Write(snapshot.ProvenanceByNativeMod.Count);
				foreach (NativeModProvenance item in snapshot.ProvenanceByNativeMod.Values
					.OrderBy(x => x.NativeMod.NativeModKey, StringComparer.Ordinal))
				{
					Write(writer, item.NativeMod.NativeModKey);
					writer.Write((int)item.StandaloneUse);
				}
				writer.Flush();

				using (SHA256 sha256 = SHA256.Create())
				{
					byte[] hash = sha256.ComputeHash(stream.ToArray());
					return new CollectionCurrentStateFingerprint("replacement-current-setup-v1",
						String.Concat(hash.Select(x => x.ToString("x2"))));
				}
			}
		}

		private static void WriteRequirement(BinaryWriter writer, CollectionRequirementReference requirement)
		{
			Write(writer, requirement.AssociationId.ToString("D"));
			Write(writer, requirement.BaselineRevision.ToString());
			Write(writer, requirement.MemberKey == null ? null : requirement.MemberKey.ToString());
			writer.Write((int)requirement.Aspect);
			Write(writer, requirement.SubjectKey);
		}

		private static void WriteState(BinaryWriter writer, CollectionRequirementState state)
		{
			writer.Write((int)state.Kind);
			Write(writer, state.FormatVersion);
			Write(writer, state.Fingerprint);
		}

		private static void Write(BinaryWriter writer, string value)
		{
			writer.Write(value != null);
			if (value != null)
				writer.Write(value);
		}
	}
}
