using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>
	/// Persists collection target associations, member/native bindings, standalone provenance and deliberate user overrides.
	/// </summary>
	/// <remarks>
	/// These records describe Collections provenance and expected recipe relationships only. Native InstallLog and
	/// deployment stores remain authoritative for actual installed effects and ownership.
	/// </remarks>
	public sealed class CollectionsAssociationStore
	{
		private readonly CollectionsStore _store;

		/// <summary>
		/// Creates an association store over an existing Collections feature store.
		/// </summary>
		public CollectionsAssociationStore(CollectionsStore store)
		{
			_store = store ?? throw new ArgumentNullException(nameof(store));
		}

		/// <summary>
		/// Persists a target association. Its revision/target identity is immutable; only the applied-state snapshot may change.
		/// </summary>
		public void SaveAssociation(CollectionTargetAssociation association)
		{
			if (association == null)
				throw new ArgumentNullException(nameof(association));

			_store.ExecuteWrite((connection, transaction) => SaveAssociation(connection, transaction, association));
		}

		/// <summary>
		/// Loads one target association by durable identity, or <c>null</c> when it is not present.
		/// </summary>
		public CollectionTargetAssociation GetAssociation(Guid associationId)
		{
			RequireGuid(associationId, nameof(associationId));
			return _store.ExecuteRead((connection, transaction) => ReadAssociation(connection, transaction, associationId));
		}

		/// <summary>
		/// Loads every persisted association for one exact native target.
		/// </summary>
		public IReadOnlyList<CollectionTargetAssociation> GetAssociationsForTarget(CollectionTargetIdentity target)
		{
			if (target == null)
				throw new ArgumentNullException(nameof(target));

			return _store.ExecuteRead((connection, transaction) =>
			{
				var associations = new List<CollectionTargetAssociation>();
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = AssociationSelect + @"
WHERE ta.target_fingerprint = @target_fingerprint
ORDER BY ta.association_id;";
					command.Parameters.AddWithValue("@target_fingerprint", target.Fingerprint);
					using (SQLiteDataReader reader = command.ExecuteReader())
					{
						while (reader.Read())
							associations.Add(ReadAssociation(reader));
					}
				}
				return associations;
			});
		}

		/// <summary>
		/// Loads the complete persisted Collection relationship state for one target in a single read transaction.
		/// </summary>
		public CollectionsAssociationTargetSnapshot GetTargetSnapshot(CollectionTargetIdentity target)
		{
			if (target == null)
				throw new ArgumentNullException(nameof(target));

			return _store.ExecuteRead((connection, transaction) =>
			{
				var associations = new List<CollectionTargetAssociation>();
				var associationsById = new Dictionary<Guid, CollectionTargetAssociation>();
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = AssociationSelect + @"
WHERE ta.target_fingerprint = @target_fingerprint
ORDER BY ta.association_id;";
					command.Parameters.AddWithValue("@target_fingerprint", target.Fingerprint);
					using (SQLiteDataReader reader = command.ExecuteReader())
					{
						while (reader.Read())
						{
							CollectionTargetAssociation association = ReadAssociation(reader);
							associations.Add(association);
							associationsById.Add(association.AssociationId, association);
						}
					}
				}

				var bindings = new List<CollectionMemberBinding>();
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"
SELECT mb.association_id, mb.member_key_kind, mb.member_key_value, mb.native_target_fingerprint,
       mb.native_mod_key, mb.verified_recipe_fingerprint, mb.binding_kind
FROM member_bindings mb
JOIN target_associations ta ON ta.association_id = mb.association_id
WHERE ta.target_fingerprint = @target_fingerprint
ORDER BY mb.association_id, mb.member_key_kind, mb.member_key_value;";
					command.Parameters.AddWithValue("@target_fingerprint", target.Fingerprint);
					using (SQLiteDataReader reader = command.ExecuteReader())
					{
						while (reader.Read())
						{
							Guid associationId = ReadCanonicalGuid(reader.GetString(0), "Collection member binding association");
							CollectionTargetAssociation association;
							if (!associationsById.TryGetValue(associationId, out association))
								throw new CollectionsStoreSchemaException("A persisted Collection member binding references a missing target association.");
							bindings.Add(ReadBinding(reader, association));
						}
					}
				}

				var overrides = new List<UserOverride>();
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"
SELECT uo.override_id, uo.association_id, uo.baseline_revision_id, uo.target_fingerprint,
       uo.member_key_kind, uo.member_key_value, uo.aspect, uo.subject_key,
       uo.baseline_state_kind, uo.baseline_state_format_version, uo.baseline_state_fingerprint,
       uo.chosen_state_kind, uo.chosen_state_format_version, uo.chosen_state_fingerprint, uo.note
FROM user_overrides uo
JOIN target_associations ta ON ta.association_id = uo.association_id
WHERE ta.target_fingerprint = @target_fingerprint
ORDER BY uo.association_id, uo.override_id;";
					command.Parameters.AddWithValue("@target_fingerprint", target.Fingerprint);
					using (SQLiteDataReader reader = command.ExecuteReader())
					{
						while (reader.Read())
						{
							Guid associationId = ReadCanonicalGuid(reader.GetString(1), "Collection user override association");
							CollectionTargetAssociation association;
							if (!associationsById.TryGetValue(associationId, out association))
								throw new CollectionsStoreSchemaException("A persisted Collection user override references a missing target association.");
							overrides.Add(ReadOverride(reader, association));
						}
					}
				}

				var drift = new List<CollectionDriftObservation>();
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"
SELECT d.observation_id, d.association_id, d.baseline_revision_id, d.target_fingerprint,
       d.member_key_kind, d.member_key_value, d.aspect, d.subject_key,
       d.expected_state_kind, d.expected_state_format_version, d.expected_state_fingerprint,
       d.observed_state_kind, d.observed_state_format_version, d.observed_state_fingerprint, d.detail
FROM drift_observations d
JOIN target_associations ta ON ta.association_id = d.association_id
WHERE ta.target_fingerprint = @target_fingerprint
ORDER BY d.association_id, d.observation_id;";
					command.Parameters.AddWithValue("@target_fingerprint", target.Fingerprint);
					using (SQLiteDataReader reader = command.ExecuteReader())
					{
						while (reader.Read())
						{
							Guid associationId = ReadCanonicalGuid(reader.GetString(1), "Collection drift association");
							CollectionTargetAssociation association;
							if (!associationsById.TryGetValue(associationId, out association))
								throw new CollectionsStoreSchemaException("A persisted Collection drift observation references a missing target association.");
							drift.Add(ReadDriftObservation(reader, association));
						}
					}
				}

				var provenance = new List<NativeModProvenance>();
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"
SELECT native_mod_key, standalone_use
FROM native_mod_provenance
WHERE target_fingerprint = @target_fingerprint
ORDER BY native_mod_key;";
					command.Parameters.AddWithValue("@target_fingerprint", target.Fingerprint);
					using (SQLiteDataReader reader = command.ExecuteReader())
					{
						while (reader.Read())
						{
							StandaloneModUse standaloneUse = (StandaloneModUse)Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture);
							if (!Enum.IsDefined(typeof(StandaloneModUse), standaloneUse) || standaloneUse == StandaloneModUse.Unknown)
								throw new CollectionsStoreSchemaException("A persisted native-mod standalone provenance row has an invalid state.");
							provenance.Add(new NativeModProvenance(new NativeModInstanceIdentity(target, reader.GetString(0)), standaloneUse));
						}
					}
				}

				return new CollectionsAssociationTargetSnapshot(target, associations, bindings, overrides, drift, provenance);
			});
		}

		/// <summary>
		/// Loads the independently established standalone-use provenance for one native mod instance.
		/// </summary>
		/// <remarks>Absence is represented as <see cref="StandaloneModUse.Unknown"/> and remains conservatively protective.</remarks>
		public NativeModProvenance GetNativeModProvenance(NativeModInstanceIdentity nativeMod)
		{
			if (nativeMod == null)
				throw new ArgumentNullException(nameof(nativeMod));

			return _store.ExecuteRead((connection, transaction) =>
			{
				NativeModProvenance persisted = ReadNativeModProvenance(connection, transaction, nativeMod);
				return persisted ?? new NativeModProvenance(nativeMod, StandaloneModUse.Unknown);
			});
		}

		/// <summary>
		/// Persists an explicitly established standalone-use state for one native mod instance.
		/// </summary>
		/// <remarks>Unknown provenance is represented by no row and therefore cannot be written as positive evidence.</remarks>
		public void SaveNativeModProvenance(NativeModProvenance provenance)
		{
			if (provenance == null)
				throw new ArgumentNullException(nameof(provenance));
			if (provenance.StandaloneUse == StandaloneModUse.Unknown)
				throw new ArgumentOutOfRangeException(nameof(provenance), "Unknown standalone use is absence of evidence and cannot be persisted as positive provenance.");

			_store.ExecuteWrite((connection, transaction) => SaveNativeModProvenance(connection, transaction, provenance));
		}

		/// <summary>
		/// Atomically detaches one Collection association while preserving all bound native mods as explicit standalone use.
		/// </summary>
		/// <remarks>
		/// This is a feature-metadata transaction only. Member bindings, overrides and drift cascade with the association;
		/// native InstallLog/deployment state, saved Collection definitions/revisions and retained content are untouched.
		/// </remarks>
		internal CollectionsAssociationDetachRecord DetachAssociation(Guid associationId, CollectionTargetIdentity target,
			CollectionOperationIdentity operationIdentity)
		{
			RequireGuid(associationId, nameof(associationId));
			if (target == null)
				throw new ArgumentNullException(nameof(target));
			if (operationIdentity == null)
				throw new ArgumentNullException(nameof(operationIdentity));

			CollectionsAssociationDetachRecord result = null;
			_store.ExecuteWrite((connection, transaction) =>
			{
				CollectionTargetAssociation association = ReadAssociation(connection, transaction, associationId);
				if (association == null)
					throw new InvalidOperationException("The Collection association to detach no longer exists.");
				if (!association.Target.Equals(target))
					throw new InvalidOperationException("The Collection association to detach belongs to another native target.");
				if (association.State == CollectionAssociationState.Recovering)
					throw new InvalidOperationException("A recovering Collection association cannot be detached until recovery/reconciliation is complete.");
				if (HasIncompleteOperationForCollection(connection, transaction, association.Revision.Collection, target))
					throw new InvalidOperationException("A Collection association cannot be detached while another operation for that Collection/target is incomplete.");

				List<CollectionMemberBinding> bindings = new List<CollectionMemberBinding>(ReadBindings(connection, transaction, association, null));
				var provenance = new List<NativeModProvenance>();
				var nativeMods = new HashSet<NativeModInstanceIdentity>();
				foreach (CollectionMemberBinding binding in bindings)
				{
					if (!nativeMods.Add(binding.NativeMod))
						continue;
					var standalone = new NativeModProvenance(binding.NativeMod, StandaloneModUse.ExplicitStandaloneUse);
					SaveNativeModProvenance(connection, transaction, standalone);
					provenance.Add(standalone);
				}

				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = "DELETE FROM target_associations WHERE association_id=@association_id;";
					command.Parameters.AddWithValue("@association_id", association.AssociationId.ToString("D"));
					if (command.ExecuteNonQuery() != 1)
						throw new CollectionsStoreSchemaException("The Collection association disappeared while detach tracking was being committed.");
				}

				var operation = new CollectionOperation(operationIdentity, CollectionOperationKind.DetachTracking,
					association.Revision.Collection, association.Target, association.Revision, null, 1,
					CollectionOperationPhase.Completed, CollectionOperationResultState.Committed,
					new CollectionNativeChildOperation[0]);
				CollectionsOperationStore.SaveOperation(connection, transaction, operation);
				result = new CollectionsAssociationDetachRecord(operation, association, bindings, provenance);
			});
			return result;
		}

		/// <summary>
		/// Atomically validates one reviewed C6.14 association baseline and creates its uninstall-effects journal operation.
		/// </summary>
		internal void BeginUninstallEffects(CollectionTargetAssociation expectedAssociation, CollectionOperation operation)
		{
			if (expectedAssociation == null)
				throw new ArgumentNullException(nameof(expectedAssociation));
			if (operation == null)
				throw new ArgumentNullException(nameof(operation));
			if (operation.Kind != CollectionOperationKind.UninstallCollectionEffects ||
				operation.Phase != CollectionOperationPhase.ApplyingNativeChildren ||
				operation.ResultState != CollectionOperationResultState.Pending ||
				operation.NativeChildren.Count != 0 || operation.PlanIdentity != null || operation.Revision == null ||
				!operation.Revision.Equals(expectedAssociation.Revision) || !operation.Collection.Equals(expectedAssociation.Revision.Collection) ||
				!operation.Target.Equals(expectedAssociation.Target))
				throw new ArgumentException("The C6.14 uninstall operation does not match the exact reviewed association baseline.", nameof(operation));

			_store.ExecuteWrite((connection, transaction) =>
			{
				CollectionTargetAssociation current = ReadAssociation(connection, transaction, expectedAssociation.AssociationId);
				RequireSameAssociationSnapshot(current, expectedAssociation,
					"The Collection association changed after the C6.14 uninstall preview and must be reviewed again.");
				if (current.State == CollectionAssociationState.Recovering)
					throw new InvalidOperationException("A recovering Collection association cannot begin uninstall until recovery/reconciliation is complete.");
				if (HasIncompleteOperationForCollection(connection, transaction, current.Revision.Collection, current.Target))
					throw new InvalidOperationException("Another Collection operation for this Collection/target is already incomplete.");
				CollectionsOperationStore.SaveOperation(connection, transaction, operation);
			});
		}

		/// <summary>
		/// Atomically marks an association Incomplete and checkpoints a C6.14 child as NativeSubmitted before worker start.
		/// </summary>
		internal void SaveUninstallChildSubmission(Guid associationId, CollectionOperation operation,
			NativeModInstanceIdentity nativeMod, IEnumerable<CollectionMemberKey> expectedMemberKeys)
		{
			RequireGuid(associationId, nameof(associationId));
			if (operation == null)
				throw new ArgumentNullException(nameof(operation));
			if (nativeMod == null)
				throw new ArgumentNullException(nameof(nativeMod));
			if (expectedMemberKeys == null)
				throw new ArgumentNullException(nameof(expectedMemberKeys));
			var expectedMembers = new HashSet<CollectionMemberKey>(expectedMemberKeys);
			if (expectedMembers.Count == 0)
				throw new ArgumentException("A C6.14 native child must retain at least one reviewed member binding.", nameof(expectedMemberKeys));
			if (operation.Kind != CollectionOperationKind.UninstallCollectionEffects ||
				operation.ResultState != CollectionOperationResultState.Pending ||
				operation.Phase != CollectionOperationPhase.ApplyingNativeChildren)
				throw new ArgumentException("The C6.14 submission checkpoint must belong to an active uninstall-effects operation.", nameof(operation));

			_store.ExecuteWrite((connection, transaction) =>
			{
				CollectionTargetAssociation association = ReadAssociation(connection, transaction, associationId);
				RequireOperationAssociation(association, operation);
				if (!nativeMod.Target.Equals(association.Target))
					throw new InvalidOperationException("The C6.14 native child belongs to another target.");
				if (association.State == CollectionAssociationState.Recovering)
					throw new InvalidOperationException("A recovering Collection association cannot submit new uninstall work.");

				List<CollectionMemberBinding> currentBindings = new List<CollectionMemberBinding>(ReadBindings(connection, transaction, null, nativeMod));
				if (currentBindings.Count == 0 || currentBindings.Any(x => x.Association.AssociationId != associationId) ||
					!expectedMembers.SetEquals(currentBindings.Select(x => x.MemberKey)))
					throw new InvalidOperationException("The native mod gained/lost Collection provenance after the C6.14 preview; native removal is no longer authorized.");
				if (HasCustomizationForMembers(connection, transaction, association, currentBindings.Select(x => x.MemberKey)))
					throw new InvalidOperationException("The native mod gained a deliberate override or detected drift after the C6.14 preview; native removal is no longer authorized.");
				NativeModProvenance provenance = ReadNativeModProvenance(connection, transaction, nativeMod);
				if (provenance == null || provenance.StandaloneUse != StandaloneModUse.NoStandaloneUseVerified)
					throw new InvalidOperationException("Standalone provenance changed after the C6.14 preview; native removal is no longer authorized.");

				SaveAssociation(connection, transaction, association.WithState(CollectionAssociationState.Incomplete));
				CollectionsOperationStore.SaveOperation(connection, transaction, operation);
			});
		}

		/// <summary>
		/// Atomically reconciles one C6.14 child and, when verified removed, drops only this association's bindings to that native mod.
		/// </summary>
		internal void SaveUninstallChildReconciliation(Guid associationId, CollectionOperation operation,
			NativeModInstanceIdentity nativeMod, bool verifiedRemoved)
		{
			RequireGuid(associationId, nameof(associationId));
			if (operation == null)
				throw new ArgumentNullException(nameof(operation));
			if (nativeMod == null)
				throw new ArgumentNullException(nameof(nativeMod));
			bool activeApply = operation.Phase == CollectionOperationPhase.ApplyingNativeChildren &&
				operation.ResultState == CollectionOperationResultState.Pending;
			bool activeRecovery = operation.Phase == CollectionOperationPhase.Recovering &&
				operation.ResultState == CollectionOperationResultState.Pending;
			bool requiredRecovery = operation.Phase == CollectionOperationPhase.RecoveryRequired &&
				operation.ResultState == CollectionOperationResultState.RecoveryRequired;
			if (operation.Kind != CollectionOperationKind.UninstallCollectionEffects ||
				(!activeApply && !activeRecovery && !requiredRecovery) || operation.NativeChildren.Count == 0 ||
				operation.NativeChildren.Any(x => x.HasCrossedNativeBoundary && !x.IsReconciled))
				throw new ArgumentException("The C6.14 child reconciliation must persist an active/recovering operation whose submitted children are reconciled.", nameof(operation));

			_store.ExecuteWrite((connection, transaction) =>
			{
				CollectionTargetAssociation association = ReadAssociation(connection, transaction, associationId);
				RequireOperationAssociation(association, operation);
				if (verifiedRemoved)
				{
					foreach (CollectionTargetAssociation affected in ReadBindings(connection, transaction, null, nativeMod)
						.Where(x => x.Association.AssociationId != associationId).Select(x => x.Association)
						.GroupBy(x => x.AssociationId).Select(x => x.First()))
					{
						if (affected.State != CollectionAssociationState.Recovering)
							SaveAssociation(connection, transaction, affected.WithState(CollectionAssociationState.Incomplete));
					}

					using (SQLiteCommand command = connection.CreateCommand())
					{
						command.Transaction = transaction;
						command.CommandText = @"
DELETE FROM member_bindings
WHERE association_id=@association_id
  AND native_target_fingerprint=@target_fingerprint
  AND native_mod_key=@native_mod_key;";
						command.Parameters.AddWithValue("@association_id", associationId.ToString("D"));
						command.Parameters.AddWithValue("@target_fingerprint", nativeMod.Target.Fingerprint);
						command.Parameters.AddWithValue("@native_mod_key", nativeMod.NativeModKey);
						command.ExecuteNonQuery();
					}
					DeleteNativeModProvenance(connection, transaction, nativeMod);
				}
				SaveAssociation(connection, transaction, association.WithState(CollectionAssociationState.Incomplete));
				CollectionsOperationStore.SaveOperation(connection, transaction, operation);
			});
		}

		/// <summary>Atomically records unresolved C6.14 durability and marks the association Recovering.</summary>
		internal void SaveUninstallRecoveryRequired(Guid associationId, CollectionOperation operation)
		{
			RequireGuid(associationId, nameof(associationId));
			if (operation == null)
				throw new ArgumentNullException(nameof(operation));
			if (operation.Kind != CollectionOperationKind.UninstallCollectionEffects ||
				operation.Phase != CollectionOperationPhase.RecoveryRequired ||
				operation.ResultState != CollectionOperationResultState.RecoveryRequired)
				throw new ArgumentException("C6.14 recovery-required persistence requires the exact RecoveryRequired operation state.", nameof(operation));
			_store.ExecuteWrite((connection, transaction) =>
			{
				CollectionTargetAssociation association = ReadAssociation(connection, transaction, associationId);
				RequireOperationAssociation(association, operation);
				SaveAssociation(connection, transaction, association.WithState(CollectionAssociationState.Recovering));
				CollectionsOperationStore.SaveOperation(connection, transaction, operation);
			});
		}

		/// <summary>Atomically stops a partially executed C6.14 operation while retaining the remaining association as Incomplete.</summary>
		internal void SaveUninstallStoppedPartial(Guid associationId, CollectionOperation operation)
		{
			RequireGuid(associationId, nameof(associationId));
			if (operation == null)
				throw new ArgumentNullException(nameof(operation));
			if (operation.Kind != CollectionOperationKind.UninstallCollectionEffects ||
				operation.Phase != CollectionOperationPhase.Completed ||
				operation.ResultState != CollectionOperationResultState.StoppedPartial)
				throw new ArgumentException("C6.14 partial-stop persistence requires a Completed/StoppedPartial operation.", nameof(operation));
			_store.ExecuteWrite((connection, transaction) =>
			{
				CollectionTargetAssociation association = ReadAssociation(connection, transaction, associationId);
				RequireOperationAssociation(association, operation);
				SaveAssociation(connection, transaction, association.WithState(CollectionAssociationState.Incomplete));
				CollectionsOperationStore.SaveOperation(connection, transaction, operation);
			});
		}

		/// <summary>
		/// Atomically completes C6.14 by establishing standalone provenance for intentionally retained current-only mods,
		/// removing the association tracking, and committing the terminal operation checkpoint.
		/// </summary>
		internal void CompleteUninstallEffects(Guid associationId, CollectionOperation operation,
			IEnumerable<NativeModInstanceIdentity> preserveAsStandalone,
			IEnumerable<NativeModInstanceIdentity> knownAbsentNativeMods)
		{
			RequireGuid(associationId, nameof(associationId));
			if (operation == null)
				throw new ArgumentNullException(nameof(operation));
			if (operation.Kind != CollectionOperationKind.UninstallCollectionEffects ||
				operation.Phase != CollectionOperationPhase.Completed ||
				operation.ResultState != CollectionOperationResultState.Committed || operation.HasUnreconciledNativeChild)
				throw new ArgumentException("C6.14 completion requires a fully reconciled Completed/Committed uninstall operation.", nameof(operation));
			if (preserveAsStandalone == null)
				throw new ArgumentNullException(nameof(preserveAsStandalone));
			if (knownAbsentNativeMods == null)
				throw new ArgumentNullException(nameof(knownAbsentNativeMods));

			List<NativeModInstanceIdentity> standalone = preserveAsStandalone.Distinct().ToList();
			List<NativeModInstanceIdentity> absent = knownAbsentNativeMods.Distinct().ToList();
			if (standalone.Intersect(absent).Any())
				throw new ArgumentException("A C6.14 native mod cannot be both preserved as standalone and verified absent.");
			_store.ExecuteWrite((connection, transaction) =>
			{
				CollectionTargetAssociation association = ReadAssociation(connection, transaction, associationId);
				RequireOperationAssociation(association, operation);
				foreach (NativeModInstanceIdentity nativeMod in standalone)
				{
					if (!nativeMod.Target.Equals(association.Target))
						throw new InvalidOperationException("C6.14 cannot establish standalone provenance for another target.");
					SaveNativeModProvenance(connection, transaction,
						new NativeModProvenance(nativeMod, StandaloneModUse.ExplicitStandaloneUse));
				}
				foreach (NativeModInstanceIdentity nativeMod in absent)
				{
					if (!nativeMod.Target.Equals(association.Target))
						throw new InvalidOperationException("C6.14 cannot clear native provenance for another target.");
					DeleteNativeModProvenance(connection, transaction, nativeMod);
				}

				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = "DELETE FROM target_associations WHERE association_id=@association_id;";
					command.Parameters.AddWithValue("@association_id", associationId.ToString("D"));
					if (command.ExecuteNonQuery() != 1)
						throw new CollectionsStoreSchemaException("The Collection association disappeared while C6.14 completion was being committed.");
				}
				CollectionsOperationStore.SaveOperation(connection, transaction, operation);
			});
		}


		/// <summary>Atomically validates one reviewed C9 installed-member removal baseline and creates its durable operation.</summary>
		internal void BeginMemberRemoval(CollectionTargetAssociation expectedAssociation, CollectionMemberBinding expectedBinding,
			CollectionOperation operation)
		{
			if (expectedAssociation == null) throw new ArgumentNullException(nameof(expectedAssociation));
			if (expectedBinding == null) throw new ArgumentNullException(nameof(expectedBinding));
			if (operation == null) throw new ArgumentNullException(nameof(operation));
			if (expectedBinding.Association.AssociationId != expectedAssociation.AssociationId ||
				operation.Kind != CollectionOperationKind.RemoveCollectionMemberEffects ||
				operation.Phase != CollectionOperationPhase.ApplyingNativeChildren ||
				operation.ResultState != CollectionOperationResultState.Pending || operation.NativeChildren.Count != 1 ||
				operation.NativeChildren[0].Checkpoint != CollectionNativeChildCheckpoint.RecoveryInputsReady ||
				operation.NativeChildren[0].Action != CollectionNativeChildAction.Deactivate || operation.PlanIdentity != null || operation.Revision == null || !operation.Revision.Equals(expectedAssociation.Revision) ||
				!operation.NativeChildren[0].Member.Revision.Equals(expectedAssociation.Revision) ||
				!operation.NativeChildren[0].Member.MemberKey.Equals(CollectionInstalledMemberRemovalCoordinator.CreateRemovalJournalKey(
					expectedAssociation.AssociationId, expectedBinding.MemberKey)) ||
				!operation.Collection.Equals(expectedAssociation.Revision.Collection) || !operation.Target.Equals(expectedAssociation.Target))
				throw new ArgumentException("The installed-member removal operation does not match the exact reviewed association/member baseline.", nameof(operation));

			_store.ExecuteWrite((connection, transaction) =>
			{
				CollectionTargetAssociation current = ReadAssociation(connection, transaction, expectedAssociation.AssociationId);
				RequireSameAssociationSnapshot(current, expectedAssociation,
					"The installed Collection association changed after member-removal review and must be reviewed again.");
				if (current.State == CollectionAssociationState.Recovering || current.State == CollectionAssociationState.Incomplete)
					throw new InvalidOperationException("Only Applied or Modified installed Collections can remove an optional member.");
				CollectionMemberBinding binding = ReadBindings(connection, transaction, current, null)
					.SingleOrDefault(x => x.MemberKey.Equals(expectedBinding.MemberKey));
				if (!BindingsEqual(binding, expectedBinding))
					throw new InvalidOperationException("The installed Collection member binding changed after review.");
				if (HasIncompleteOperationForCollection(connection, transaction, current.Revision.Collection, current.Target))
					throw new InvalidOperationException("Another Collection operation for this Collection/target is already incomplete.");
				CollectionsOperationStore.SaveOperation(connection, transaction, operation);
			});
		}

		/// <summary>Marks the exact reviewed member-removal child NativeSubmitted before the native uninstaller starts.</summary>
		internal void SaveMemberRemovalChildSubmission(Guid associationId, CollectionMemberKey memberKey,
			CollectionOperation operation, NativeModInstanceIdentity nativeMod)
		{
			RequireGuid(associationId, nameof(associationId));
			if (memberKey == null) throw new ArgumentNullException(nameof(memberKey));
			if (operation == null) throw new ArgumentNullException(nameof(operation));
			if (nativeMod == null) throw new ArgumentNullException(nameof(nativeMod));
			if (operation.Kind != CollectionOperationKind.RemoveCollectionMemberEffects ||
				operation.Phase != CollectionOperationPhase.ApplyingNativeChildren ||
				operation.ResultState != CollectionOperationResultState.Pending || operation.NativeChildren.Count != 1 ||
				!operation.NativeChildren[0].HasCrossedNativeBoundary)
				throw new ArgumentException("The installed-member removal submission checkpoint is not an active submitted native child.", nameof(operation));

			_store.ExecuteWrite((connection, transaction) =>
			{
				CollectionTargetAssociation association = ReadAssociation(connection, transaction, associationId);
				RequireMemberRemovalOperationAssociation(association, operation);
				CollectionMemberBinding binding = ReadBindings(connection, transaction, association, null)
					.SingleOrDefault(x => x.MemberKey.Equals(memberKey));
				if (binding == null || !binding.NativeMod.Equals(nativeMod))
					throw new InvalidOperationException("The reviewed installed-member binding changed before native removal submission.");
				IReadOnlyList<CollectionMemberBinding> nativeBindings = ReadBindings(connection, transaction, null, nativeMod);
				if (nativeBindings.Count != 1 || nativeBindings[0].Association.AssociationId != associationId ||
					!nativeBindings[0].MemberKey.Equals(memberKey))
					throw new InvalidOperationException("The native mod gained another Collection binding after member-removal review; native removal is no longer authorized.");
				if (HasCustomizationForMembers(connection, transaction, association, new[] { memberKey }))
					throw new InvalidOperationException("The member gained a deliberate override or detected drift after review; native removal is no longer authorized.");
				NativeModProvenance provenance = ReadNativeModProvenance(connection, transaction, nativeMod);
				if (provenance == null || provenance.StandaloneUse != StandaloneModUse.NoStandaloneUseVerified)
					throw new InvalidOperationException("Standalone provenance changed after member-removal review; native removal is no longer authorized.");

				SaveAssociation(connection, transaction, association.WithState(CollectionAssociationState.Incomplete));
				CollectionsOperationStore.SaveOperation(connection, transaction, operation);
			});
		}

		/// <summary>Persists unresolved native durability for an installed-member removal and marks the association Recovering.</summary>
		internal void SaveMemberRemovalRecoveryRequired(Guid associationId, CollectionOperation operation)
		{
			RequireGuid(associationId, nameof(associationId));
			if (operation == null) throw new ArgumentNullException(nameof(operation));
			if (operation.Kind != CollectionOperationKind.RemoveCollectionMemberEffects ||
				operation.Phase != CollectionOperationPhase.RecoveryRequired ||
				operation.ResultState != CollectionOperationResultState.RecoveryRequired)
				throw new ArgumentException("Installed-member removal recovery requires the exact RecoveryRequired operation state.", nameof(operation));
			_store.ExecuteWrite((connection, transaction) =>
			{
				CollectionTargetAssociation association = ReadAssociation(connection, transaction, associationId);
				RequireMemberRemovalOperationAssociation(association, operation);
				SaveAssociation(connection, transaction, association.WithState(CollectionAssociationState.Recovering));
				CollectionsOperationStore.SaveOperation(connection, transaction, operation);
			});
		}

		/// <summary>Stops a native member-removal attempt after the mutation boundary while keeping the association visibly incomplete.</summary>
		internal void SaveMemberRemovalStoppedPartial(Guid associationId, CollectionOperation operation)
		{
			RequireGuid(associationId, nameof(associationId));
			if (operation == null) throw new ArgumentNullException(nameof(operation));
			if (operation.Kind != CollectionOperationKind.RemoveCollectionMemberEffects ||
				operation.Phase != CollectionOperationPhase.Completed ||
				operation.ResultState != CollectionOperationResultState.StoppedPartial)
				throw new ArgumentException("Installed-member removal partial-stop persistence requires Completed/StoppedPartial.", nameof(operation));
			_store.ExecuteWrite((connection, transaction) =>
			{
				CollectionTargetAssociation association = ReadAssociation(connection, transaction, associationId);
				RequireMemberRemovalOperationAssociation(association, operation);
				SaveAssociation(connection, transaction, association.WithState(CollectionAssociationState.Incomplete));
				CollectionsOperationStore.SaveOperation(connection, transaction, operation);
			});
		}

		/// <summary>Atomically removes one member binding when no native mutation is required.</summary>
		internal void CompleteMemberRemovalWithoutNativeMutation(CollectionTargetAssociation expectedAssociation,
			CollectionMemberBinding expectedBinding, CollectionOperation operation, bool preserveAsStandalone, bool knownAbsent)
		{
			if (expectedAssociation == null) throw new ArgumentNullException(nameof(expectedAssociation));
			if (expectedBinding == null) throw new ArgumentNullException(nameof(expectedBinding));
			if (operation == null) throw new ArgumentNullException(nameof(operation));
			ValidateCompletedMemberRemovalOperation(expectedAssociation, operation, false);
			if (preserveAsStandalone && knownAbsent)
				throw new ArgumentException("A preserved standalone member cannot simultaneously be known absent.");

			_store.ExecuteWrite((connection, transaction) =>
			{
				CollectionTargetAssociation current = ReadAssociation(connection, transaction, expectedAssociation.AssociationId);
				RequireSameAssociationSnapshot(current, expectedAssociation,
					"The installed Collection association changed after member-removal review and must be reviewed again.");
				if (current.State == CollectionAssociationState.Recovering || current.State == CollectionAssociationState.Incomplete)
					throw new InvalidOperationException("Only Applied or Modified installed Collections can remove an optional member.");
				if (HasIncompleteOperationForCollection(connection, transaction, current.Revision.Collection, current.Target))
					throw new InvalidOperationException("Another Collection operation for this Collection/target is already incomplete.");
				CollectionMemberBinding binding = ReadBindings(connection, transaction, current, null)
					.SingleOrDefault(x => x.MemberKey.Equals(expectedBinding.MemberKey));
				if (!BindingsEqual(binding, expectedBinding))
					throw new InvalidOperationException("The installed Collection member binding changed after review.");

				DeleteMemberTracking(connection, transaction, current, binding.MemberKey);
				bool stillBound = ReadBindings(connection, transaction, null, binding.NativeMod).Count != 0;
				if (!stillBound && preserveAsStandalone)
					SaveNativeModProvenance(connection, transaction,
						new NativeModProvenance(binding.NativeMod, StandaloneModUse.ExplicitStandaloneUse));
				else if (!stillBound && knownAbsent)
					DeleteNativeModProvenance(connection, transaction, binding.NativeMod);
				SaveAssociation(connection, transaction, current.WithState(CollectionAssociationState.Modified));
				CollectionsOperationStore.SaveOperation(connection, transaction, operation);
			});
		}

		/// <summary>Atomically publishes one verified native optional-member removal and its Collection metadata change.</summary>
		internal void CompleteMemberRemovalAfterNativeRemoval(Guid associationId, CollectionMemberKey memberKey,
			NativeModInstanceIdentity nativeMod, CollectionOperation operation)
		{
			RequireGuid(associationId, nameof(associationId));
			if (memberKey == null) throw new ArgumentNullException(nameof(memberKey));
			if (nativeMod == null) throw new ArgumentNullException(nameof(nativeMod));
			if (operation == null) throw new ArgumentNullException(nameof(operation));
			if (operation.Kind != CollectionOperationKind.RemoveCollectionMemberEffects ||
				operation.Phase != CollectionOperationPhase.Completed || operation.ResultState != CollectionOperationResultState.Committed ||
				operation.NativeChildren.Count != 1 || !operation.NativeChildren[0].IsReconciled ||
				!operation.NativeChildren[0].HasVerifiedCommittedNativeState)
				throw new ArgumentException("Native installed-member removal completion requires one reconciled verified-committed child.", nameof(operation));

			_store.ExecuteWrite((connection, transaction) =>
			{
				CollectionTargetAssociation association = ReadAssociation(connection, transaction, associationId);
				RequireMemberRemovalOperationAssociation(association, operation);
				CollectionMemberBinding binding = ReadBindings(connection, transaction, association, null)
					.SingleOrDefault(x => x.MemberKey.Equals(memberKey));
				if (binding == null || !binding.NativeMod.Equals(nativeMod))
					throw new InvalidOperationException("The installed Collection member binding changed before final member-removal publication.");
				List<CollectionMemberBinding> survivingBindings = ReadBindings(connection, transaction, null, nativeMod)
					.Where(x => x.Association.AssociationId != associationId || !x.MemberKey.Equals(memberKey)).ToList();
				DeleteMemberTracking(connection, transaction, association, memberKey);
				DeleteNativeModProvenance(connection, transaction, nativeMod);
				bool currentAssociationStillReferencesRemovedNative = survivingBindings.Any(x => x.Association.AssociationId == associationId);
				SaveAssociation(connection, transaction, association.WithState(currentAssociationStillReferencesRemovedNative
					? CollectionAssociationState.Incomplete : CollectionAssociationState.Modified));
				foreach (Guid survivingAssociationId in survivingBindings.Select(x => x.Association.AssociationId)
					.Where(x => x != associationId).Distinct())
				{
					CollectionTargetAssociation survivingAssociation = ReadAssociation(connection, transaction, survivingAssociationId);
					if (survivingAssociation != null && survivingAssociation.State != CollectionAssociationState.Recovering)
						SaveAssociation(connection, transaction, survivingAssociation.WithState(CollectionAssociationState.Incomplete));
				}
				CollectionsOperationStore.SaveOperation(connection, transaction, operation);
			});
		}

		private static void ValidateCompletedMemberRemovalOperation(CollectionTargetAssociation association,
			CollectionOperation operation, bool requireNativeChild)
		{
			if (operation.Kind != CollectionOperationKind.RemoveCollectionMemberEffects ||
				operation.Phase != CollectionOperationPhase.Completed || operation.ResultState != CollectionOperationResultState.Committed ||
				operation.PlanIdentity != null || operation.Revision == null || !operation.Revision.Equals(association.Revision) ||
				!operation.Collection.Equals(association.Revision.Collection) || !operation.Target.Equals(association.Target) ||
				(requireNativeChild ? operation.NativeChildren.Count != 1 : operation.NativeChildren.Count != 0))
				throw new ArgumentException("The terminal installed-member removal operation does not match the exact association baseline.", nameof(operation));
		}

		private static bool BindingsEqual(CollectionMemberBinding left, CollectionMemberBinding right)
		{
			return left != null && right != null && left.Association.AssociationId == right.Association.AssociationId &&
				left.MemberKey.Equals(right.MemberKey) && left.NativeMod.Equals(right.NativeMod) &&
				left.VerifiedRecipe.Equals(right.VerifiedRecipe) && left.BindingKind == right.BindingKind;
		}

		private static void DeleteMemberTracking(SQLiteConnection connection, SQLiteTransaction transaction,
			CollectionTargetAssociation association, CollectionMemberKey memberKey)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"DELETE FROM member_bindings
WHERE association_id=@association_id AND member_key_kind=@member_key_kind AND member_key_value=@member_key_value;";
				command.Parameters.AddWithValue("@association_id", association.AssociationId.ToString("D"));
				command.Parameters.AddWithValue("@member_key_kind", (int)memberKey.Kind);
				command.Parameters.AddWithValue("@member_key_value", memberKey.Value);
				if (command.ExecuteNonQuery() != 1)
					throw new InvalidOperationException("The installed Collection member binding disappeared during removal publication.");
			}
			string[] tables = { "user_overrides", "drift_observations" };
			foreach (string table in tables)
			{
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = "DELETE FROM " + table +
						" WHERE association_id=@association_id AND member_key_kind=@member_key_kind AND member_key_value=@member_key_value;";
					command.Parameters.AddWithValue("@association_id", association.AssociationId.ToString("D"));
					command.Parameters.AddWithValue("@member_key_kind", (int)memberKey.Kind);
					command.Parameters.AddWithValue("@member_key_value", memberKey.Value);
					command.ExecuteNonQuery();
				}
			}
		}

		/// <summary>
		/// Persists the current native-mod binding for one member of an existing exact association baseline.
		/// </summary>
		public void SaveBinding(CollectionMemberBinding binding)
		{
			if (binding == null)
				throw new ArgumentNullException(nameof(binding));

			_store.ExecuteWrite((connection, transaction) =>
			{
				RequireMatchingAssociation(connection, transaction, binding.Association);
				SaveBinding(connection, transaction, binding);
			});
		}

		/// <summary>
		/// Atomically persists one C6.10 child reconciliation together with its association/binding provenance.
		/// </summary>
		/// <remarks>
		/// A null association/binding is valid for a verified non-commit outcome. Native ownership is never written here.
		/// </remarks>
		internal void SaveChildReconciliation(CollectionOperation operation, CollectionTargetAssociation association,
			CollectionMemberBinding binding)
		{
			if (operation == null)
				throw new ArgumentNullException(nameof(operation));
			if (binding != null && association == null)
				throw new ArgumentException("A member binding requires the association being checkpointed.", nameof(binding));
			if (binding != null && binding.Association.AssociationId != association.AssociationId)
				throw new ArgumentException("The member binding must belong to the association being checkpointed.", nameof(binding));

			_store.ExecuteWrite((connection, transaction) =>
			{
				if (association != null)
					SaveAssociation(connection, transaction, association);
				if (binding != null)
				{
					RequireMatchingAssociation(connection, transaction, binding.Association);
					SaveBinding(connection, transaction, binding);
				}
				CollectionsOperationStore.SaveOperation(connection, transaction, operation);
			});
		}

		/// <summary>
		/// Atomically commits C8.8 feature metadata after aggregate native replacement verification has already succeeded.
		/// </summary>
		/// <remarks>
		/// The reviewed outgoing association set is compare-and-swapped in the same Collections transaction as the incoming
		/// Applied association/bindings and terminal replacement journal result. TransitionOut deletes only feature tracking;
		/// it does not manufacture standalone provenance for native mods which the replacement may already have removed.
		/// </remarks>
		internal void FinalizeReplacementAssociations(CollectionReplacementReviewedIntent intent,
			CollectionTargetAssociation incomingAssociation, IEnumerable<CollectionMemberBinding> incomingBindings,
			CollectionOperation committedOperation)
		{
			if (intent == null) throw new ArgumentNullException(nameof(intent));
			if (incomingAssociation == null) throw new ArgumentNullException(nameof(incomingAssociation));
			if (incomingBindings == null) throw new ArgumentNullException(nameof(incomingBindings));
			if (committedOperation == null) throw new ArgumentNullException(nameof(committedOperation));
			if (incomingAssociation.State != CollectionAssociationState.Applied || !incomingAssociation.Target.Equals(intent.Target) ||
				!incomingAssociation.Revision.Equals(intent.Revision))
				throw new ArgumentException("The C8.8 incoming association must be the exact reviewed revision/target and Applied.", nameof(incomingAssociation));
			if (committedOperation.Kind != CollectionOperationKind.ReplaceCurrentManagedSetup ||
				committedOperation.Phase != CollectionOperationPhase.Completed || committedOperation.ResultState != CollectionOperationResultState.Committed ||
				committedOperation.PlanIdentity == null || !committedOperation.PlanIdentity.Equals(intent.PlanIdentity) ||
				committedOperation.Revision == null || !committedOperation.Revision.Equals(intent.Revision) ||
				!committedOperation.Target.Equals(intent.Target))
				throw new ArgumentException("The C8.8 terminal operation does not match the exact reviewed replacement intent.", nameof(committedOperation));

			List<CollectionMemberBinding> bindings = incomingBindings.ToList();
			if (bindings.Any(x => x == null || x.Association.AssociationId != incomingAssociation.AssociationId ||
				!x.Association.Revision.Equals(incomingAssociation.Revision) || !x.Association.Target.Equals(incomingAssociation.Target)))
				throw new ArgumentException("Every C8.8 incoming binding must belong to the exact incoming association.", nameof(incomingBindings));
			if (bindings.Select(x => x.MemberKey).Distinct().Count() != bindings.Count)
				throw new ArgumentException("The C8.8 incoming association cannot contain duplicate member bindings.", nameof(incomingBindings));

			_store.ExecuteWrite((connection, transaction) =>
			{
				HashSet<Guid> expectedIds = new HashSet<Guid>(intent.AssociationApprovals.Select(x => x.AssociationId));
				HashSet<Guid> currentIds = ReadAssociationIdsForTarget(connection, transaction, intent.Target);
				if (!currentIds.SetEquals(expectedIds))
					throw new InvalidOperationException("The target association set changed after replacement review; C8.8 will not publish stale transitions.");

				foreach (CollectionReplacementAssociationApproval approval in intent.AssociationApprovals.OrderBy(x => x.AssociationId))
				{
					CollectionTargetAssociation current = ReadAssociation(connection, transaction, approval.AssociationId);
					if (current == null || !current.Target.Equals(intent.Target))
						throw new InvalidOperationException("A reviewed outgoing association disappeared or moved before replacement publication.");
					if (approval.Decision == CollectionReplacementAssociationDecision.Preserve)
					{
						if (current.State == CollectionAssociationState.Recovering)
							throw new InvalidOperationException("A reviewed preserved association entered recovery before replacement publication.");
						continue;
					}
					if (approval.Decision != CollectionReplacementAssociationDecision.TransitionOut)
						throw new InvalidOperationException("C8.8 encountered an unsupported reviewed association transition.");
					if (current.AssociationId == incomingAssociation.AssociationId)
						throw new InvalidOperationException("C8.8 cannot transition out the same association it is publishing as incoming.");
					using (SQLiteCommand command = connection.CreateCommand())
					{
						command.Transaction = transaction;
						command.CommandText = "DELETE FROM target_associations WHERE association_id=@association_id;";
						command.Parameters.AddWithValue("@association_id", current.AssociationId.ToString("D"));
						if (command.ExecuteNonQuery() != 1)
							throw new CollectionsStoreSchemaException("A reviewed outgoing association disappeared during C8.8 publication.");
					}
				}

				SaveAssociation(connection, transaction, incomingAssociation);
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = "DELETE FROM member_bindings WHERE association_id=@association_id;";
					command.Parameters.AddWithValue("@association_id", incomingAssociation.AssociationId.ToString("D"));
					command.ExecuteNonQuery();
				}
				foreach (CollectionMemberBinding binding in bindings)
				{
					RequireMatchingAssociation(connection, transaction, binding.Association);
					SaveBinding(connection, transaction, binding);
				}
				CollectionsOperationStore.SaveOperation(connection, transaction, committedOperation);
			});
		}

		/// <summary>
		/// Atomically transitions one reviewed C10 association from its old revision to the verified candidate revision.
		/// </summary>
		/// <remarks>
		/// The old concrete association identity is not rebound. It is compare-and-swapped out and a distinct candidate
		/// association is inserted together with its bindings, rebased deliberate overrides and terminal UpdateRevision
		/// journal result in one Collections transaction. Native state was already verified by C10.8 and is never written here.
		/// </remarks>
		internal void FinalizeRevisionUpdateAssociation(CollectionRevisionUpdateReviewedIntent reviewedIntent,
			CollectionRevisionUpdateAggregateVerificationRecord verification, CollectionTargetAssociation incomingAssociation,
			IEnumerable<CollectionMemberBinding> incomingBindings, IEnumerable<UserOverride> expectedOldOverrides,
			IEnumerable<UserOverride> incomingOverrides, CollectionOperation committedOperation)
		{
			if (reviewedIntent == null) throw new ArgumentNullException(nameof(reviewedIntent));
			if (verification == null) throw new ArgumentNullException(nameof(verification));
			if (incomingAssociation == null) throw new ArgumentNullException(nameof(incomingAssociation));
			if (incomingBindings == null) throw new ArgumentNullException(nameof(incomingBindings));
			if (expectedOldOverrides == null) throw new ArgumentNullException(nameof(expectedOldOverrides));
			if (incomingOverrides == null) throw new ArgumentNullException(nameof(incomingOverrides));
			if (committedOperation == null) throw new ArgumentNullException(nameof(committedOperation));
			if (incomingAssociation.AssociationId == reviewedIntent.AssociationId)
				throw new ArgumentException("C10.9 publishes a distinct concrete-revision association identity instead of rebinding the old association.", nameof(incomingAssociation));
			if (!incomingAssociation.Target.Equals(reviewedIntent.Target) || !incomingAssociation.Revision.Equals(reviewedIntent.CandidateRevision))
				throw new ArgumentException("The C10.9 candidate association must be the exact reviewed candidate revision/target.", nameof(incomingAssociation));
			if (verification.OperationId != committedOperation.Identity.OperationId ||
				!verification.PlanIdentity.Equals(reviewedIntent.CandidatePlanIdentity) ||
				!StringComparer.Ordinal.Equals(verification.ReviewFingerprint, reviewedIntent.ReviewFingerprint))
				throw new ArgumentException("The C10.8 aggregate verification does not belong to the exact terminal revision-update operation/review.", nameof(verification));
			if (committedOperation.Kind != CollectionOperationKind.UpdateRevision || committedOperation.Phase != CollectionOperationPhase.Completed ||
				committedOperation.ResultState != CollectionOperationResultState.Committed || committedOperation.PlanIdentity == null ||
				!committedOperation.PlanIdentity.Equals(reviewedIntent.CandidatePlanIdentity) || committedOperation.Revision == null ||
				!committedOperation.Revision.Equals(reviewedIntent.CandidateRevision) || !committedOperation.Target.Equals(reviewedIntent.Target))
				throw new ArgumentException("The C10.9 terminal operation does not match the exact reviewed candidate revision.", nameof(committedOperation));

			List<CollectionMemberBinding> bindings = incomingBindings.ToList();
			List<UserOverride> oldOverrides = expectedOldOverrides.OrderBy(x => x == null ? Guid.Empty : x.OverrideId).ToList();
			List<UserOverride> newOverrides = incomingOverrides.OrderBy(x => x == null ? Guid.Empty : x.OverrideId).ToList();
			if (bindings.Any(x => x == null || x.Association.AssociationId != incomingAssociation.AssociationId ||
				!x.Association.Revision.Equals(incomingAssociation.Revision) || !x.Association.Target.Equals(incomingAssociation.Target)))
				throw new ArgumentException("Every C10.9 binding must belong to the exact candidate association.", nameof(incomingBindings));
			if (bindings.Select(x => x.MemberKey).Distinct().Count() != bindings.Count)
				throw new ArgumentException("The C10.9 candidate association cannot contain duplicate member bindings.", nameof(incomingBindings));
			if (oldOverrides.Any(x => x == null) || oldOverrides.Select(x => x.OverrideId).Distinct().Count() != oldOverrides.Count ||
				!new HashSet<Guid>(oldOverrides.Select(x => x.OverrideId)).SetEquals(reviewedIntent.PreservedOverrideIds))
				throw new ArgumentException("The C10.9 expected old override set must exactly match the decisions frozen by the review.", nameof(expectedOldOverrides));
			if (newOverrides.Any(x => x == null) || newOverrides.Select(x => x.OverrideId).Distinct().Count() != newOverrides.Count ||
				newOverrides.Any(x => !reviewedIntent.PreservedOverrideIds.Contains(x.OverrideId)))
				throw new ArgumentException("C10.9 may only carry forward override identities frozen by the reviewed update.", nameof(incomingOverrides));
			foreach (UserOverride userOverride in newOverrides)
			{
				if (userOverride.Requirement.AssociationId != incomingAssociation.AssociationId ||
					!userOverride.Requirement.BaselineRevision.Equals(incomingAssociation.Revision) ||
					!userOverride.Requirement.Target.Equals(incomingAssociation.Target))
					throw new ArgumentException("Every C10.9 carried override must be rebased to the exact candidate association baseline.", nameof(incomingOverrides));
			}
			CollectionAssociationState expectedState = newOverrides.Count == 0 ? CollectionAssociationState.Applied : CollectionAssociationState.Modified;
			if (incomingAssociation.State != expectedState)
				throw new ArgumentException("The C10.9 candidate association state must reflect whether deliberate overrides remain after rebasing.", nameof(incomingAssociation));

			_store.ExecuteWrite((connection, transaction) =>
			{
				CollectionTargetAssociation current = ReadAssociation(connection, transaction, reviewedIntent.AssociationId);
				if (current == null || !current.Revision.Equals(reviewedIntent.OldRevision) || !current.Target.Equals(reviewedIntent.Target) ||
					current.State != reviewedIntent.AssociationState)
					throw new InvalidOperationException("The old Collection association changed after C10 review; candidate publication is stale.");
				List<UserOverride> currentOverrides = ReadOverrides(connection, transaction, current).OrderBy(x => x.OverrideId).ToList();
				if (!OverrideSetsEqual(oldOverrides, currentOverrides))
					throw new InvalidOperationException("The old Collection override set changed after C10 review; candidate publication is stale.");
				if (ReadDriftObservations(connection, transaction, current).Count != 0)
					throw new InvalidOperationException("Unaccepted drift appeared before C10.9 publication.");

				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"
SELECT COUNT(*)
FROM target_associations
WHERE target_fingerprint=@target_fingerprint
  AND origin=@origin
  AND collection_id=@collection_id
  AND association_id<>@old_association_id;";
					command.Parameters.AddWithValue("@target_fingerprint", reviewedIntent.Target.Fingerprint);
					command.Parameters.AddWithValue("@origin", (int)reviewedIntent.OldRevision.Collection.Origin);
					command.Parameters.AddWithValue("@collection_id", reviewedIntent.OldRevision.Collection.StableId);
					command.Parameters.AddWithValue("@old_association_id", reviewedIntent.AssociationId.ToString("D"));
					if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
						throw new InvalidOperationException("Another association for the same Collection/target exists; C10.9 will not create mixed revision tracking.");
				}

				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = "DELETE FROM target_associations WHERE association_id=@association_id;";
					command.Parameters.AddWithValue("@association_id", reviewedIntent.AssociationId.ToString("D"));
					if (command.ExecuteNonQuery() != 1)
						throw new CollectionsStoreSchemaException("The reviewed old association disappeared during C10.9 publication.");
				}

				SaveAssociation(connection, transaction, incomingAssociation);
				foreach (CollectionMemberBinding binding in bindings)
				{
					RequireMatchingAssociation(connection, transaction, binding.Association);
					SaveBinding(connection, transaction, binding);
				}
				foreach (UserOverride userOverride in newOverrides)
				{
					RequireAssociationForRequirement(connection, transaction, userOverride.Requirement);
					SaveOverride(connection, transaction, userOverride);
				}
				CollectionsOperationStore.SaveOperation(connection, transaction, committedOperation);
			});
		}

		/// <summary>
		/// Atomically publishes the complete C6.10 applied association/binding set after every selected member is satisfied.
		/// </summary>
		internal void SaveAppliedAssociation(CollectionTargetAssociation association, IEnumerable<CollectionMemberBinding> bindings,
			IEnumerable<NativeModProvenance> provenanceCandidates)
		{
			if (association == null)
				throw new ArgumentNullException(nameof(association));
			if (association.State != CollectionAssociationState.Applied)
				throw new ArgumentException("The final C6.10 association snapshot must be Applied.", nameof(association));
			if (bindings == null)
				throw new ArgumentNullException(nameof(bindings));
			if (provenanceCandidates == null)
				throw new ArgumentNullException(nameof(provenanceCandidates));

			List<CollectionMemberBinding> copied = new List<CollectionMemberBinding>(bindings);
			List<NativeModProvenance> copiedProvenance = new List<NativeModProvenance>(provenanceCandidates);
			var memberKeys = new HashSet<CollectionMemberKey>();
			foreach (CollectionMemberBinding binding in copied)
			{
				if (binding == null)
					throw new ArgumentException("An applied association cannot contain a null member binding.", nameof(bindings));
				if (binding.Association.AssociationId != association.AssociationId ||
					!binding.Association.Revision.Equals(association.Revision) || !binding.Association.Target.Equals(association.Target))
					throw new ArgumentException("Every final binding must belong to the exact applied association baseline.", nameof(bindings));
				if (!memberKeys.Add(binding.MemberKey))
					throw new ArgumentException("The final applied association cannot contain duplicate member bindings.", nameof(bindings));
			}

			_store.ExecuteWrite((connection, transaction) =>
			{
				if (HasIncompleteUninstallOperationForTarget(connection, transaction, association.Target))
					throw new InvalidOperationException("An active Collection uninstall-effects operation owns this target; Applied association provenance cannot be published concurrently.");
				SaveAssociation(connection, transaction, association);
				foreach (CollectionMemberBinding binding in copied)
				{
					RequireMatchingAssociation(connection, transaction, binding.Association);
					SaveBinding(connection, transaction, binding);
				}
				foreach (NativeModProvenance provenance in copiedProvenance)
				{
					if (provenance == null)
						throw new ArgumentException("An applied association cannot contain a null native provenance candidate.", nameof(provenanceCandidates));
					if (!provenance.NativeMod.Target.Equals(association.Target))
						throw new ArgumentException("Every native provenance candidate must belong to the applied association target.", nameof(provenanceCandidates));
					if (provenance.StandaloneUse == StandaloneModUse.Unknown)
						throw new ArgumentOutOfRangeException(nameof(provenanceCandidates), "Unknown standalone use cannot be persisted as positive provenance.");

					NativeModProvenance existing = ReadNativeModProvenance(connection, transaction, provenance.NativeMod);
					if (existing == null)
						SaveNativeModProvenance(connection, transaction, provenance);
					else if (existing.StandaloneUse != provenance.StandaloneUse &&
						provenance.StandaloneUse == StandaloneModUse.ExplicitStandaloneUse)
						SaveNativeModProvenance(connection, transaction, provenance);
				}
			});
		}

		/// <summary>
		/// Loads every member binding belonging to one target association.
		/// </summary>
		public IReadOnlyList<CollectionMemberBinding> GetBindings(Guid associationId)
		{
			RequireGuid(associationId, nameof(associationId));
			return _store.ExecuteRead((connection, transaction) =>
			{
				CollectionTargetAssociation association = ReadAssociation(connection, transaction, associationId);
				if (association == null)
					return (IReadOnlyList<CollectionMemberBinding>)new List<CollectionMemberBinding>();

				return ReadBindings(connection, transaction, association, null);
			});
		}

		/// <summary>
		/// Loads every collection member currently bound to one target-scoped native mod instance.
		/// </summary>
		public IReadOnlyList<CollectionMemberBinding> GetBindingsForNativeMod(NativeModInstanceIdentity nativeMod)
		{
			if (nativeMod == null)
				throw new ArgumentNullException(nameof(nativeMod));

			return _store.ExecuteRead((connection, transaction) =>
				ReadBindings(connection, transaction, null, nativeMod));
		}

		/// <summary>
		/// Persists one deliberate user override for an existing exact association baseline.
		/// </summary>
		/// <remarks>
		/// An override identity may update the user's chosen state/note, but cannot be rebound to another requirement or
		/// baseline. Only one active deliberate override may exist for the same exact requirement.
		/// </remarks>
		public void SaveOverride(UserOverride userOverride)
		{
			if (userOverride == null)
				throw new ArgumentNullException(nameof(userOverride));

			_store.ExecuteWrite((connection, transaction) =>
			{
				CollectionTargetAssociation association = RequireAssociationForRequirement(connection, transaction, userOverride.Requirement);
				EnsureOverrideIdentityAndRequirementAreCompatible(connection, transaction, association, userOverride);
				SaveOverride(connection, transaction, userOverride);
			});
		}

		/// <summary>
		/// Loads every deliberate user override belonging to one target association.
		/// </summary>
		public IReadOnlyList<UserOverride> GetOverrides(Guid associationId)
		{
			RequireGuid(associationId, nameof(associationId));
			return _store.ExecuteRead((connection, transaction) =>
			{
				CollectionTargetAssociation association = ReadAssociation(connection, transaction, associationId);
				if (association == null)
					return (IReadOnlyList<UserOverride>)new List<UserOverride>();

				return ReadOverrides(connection, transaction, association);
			});
		}

		/// <summary>
		/// Loads every current detected-drift observation belonging to one target association.
		/// </summary>
		public IReadOnlyList<CollectionDriftObservation> GetDriftObservations(Guid associationId)
		{
			RequireGuid(associationId, nameof(associationId));
			return _store.ExecuteRead((connection, transaction) =>
			{
				CollectionTargetAssociation association = ReadAssociation(connection, transaction, associationId);
				if (association == null)
					return (IReadOnlyList<CollectionDriftObservation>)new List<CollectionDriftObservation>();

				return ReadDriftObservations(connection, transaction, association);
			});
		}

		/// <summary>Marks the exact outgoing target associations as Recovering before a Local Collection restore can mutate native state.</summary>
		internal void MarkLocalRestoreOutgoingAssociationsRecovering(CollectionTargetIdentity target,
			IReadOnlyDictionary<Guid, CollectionAssociationState> expectedStates)
		{
			if (target == null) throw new ArgumentNullException(nameof(target));
			if (expectedStates == null) throw new ArgumentNullException(nameof(expectedStates));

			_store.ExecuteWrite((connection, transaction) =>
			{
				HashSet<Guid> expectedIds = new HashSet<Guid>(expectedStates.Keys);
				HashSet<Guid> currentIds = ReadAssociationIdsForTarget(connection, transaction, target);
				if (!currentIds.SetEquals(expectedIds))
					throw new InvalidOperationException("The outgoing Collection association set changed after Local restore review and cannot be superseded silently.");

				foreach (KeyValuePair<Guid, CollectionAssociationState> expected in expectedStates.OrderBy(x => x.Key))
				{
					RequireGuid(expected.Key, nameof(expectedStates));
					if (!Enum.IsDefined(typeof(CollectionAssociationState), expected.Value) ||
						expected.Value == CollectionAssociationState.Unknown || expected.Value == CollectionAssociationState.Recovering)
						throw new ArgumentOutOfRangeException(nameof(expectedStates),
							"A Local restore cannot adopt an already-recovering outgoing association boundary.");

					CollectionTargetAssociation current = ReadAssociation(connection, transaction, expected.Key);
					if (current == null || !current.Target.Equals(target))
						throw new InvalidOperationException("An outgoing Collection association disappeared or moved before the Local restore profile boundary was established.");
					if (current.State == CollectionAssociationState.Recovering)
						continue;
					if (current.State != expected.Value)
						throw new InvalidOperationException("An outgoing Collection association changed after Local restore review and cannot be superseded silently.");

					SaveAssociation(connection, transaction, current.WithState(CollectionAssociationState.Recovering));
				}
			});
		}

		/// <summary>Finalizes a verified Local restore by leaving every superseded outgoing association explicitly Incomplete.</summary>
		internal void FinalizeLocalRestoreOutgoingAssociations(CollectionTargetIdentity target, IEnumerable<Guid> associationIds)
		{
			if (target == null) throw new ArgumentNullException(nameof(target));
			if (associationIds == null) throw new ArgumentNullException(nameof(associationIds));
			List<Guid> ids = associationIds.Distinct().OrderBy(x => x).ToList();

			_store.ExecuteWrite((connection, transaction) =>
			{
				HashSet<Guid> currentIds = ReadAssociationIdsForTarget(connection, transaction, target);
				if (!currentIds.SetEquals(ids))
					throw new InvalidOperationException("The outgoing Collection association set changed before Local restore reconciliation completed.");

				foreach (Guid associationId in ids)
				{
					RequireGuid(associationId, nameof(associationIds));
					CollectionTargetAssociation current = ReadAssociation(connection, transaction, associationId);
					if (current == null || !current.Target.Equals(target))
						throw new InvalidOperationException("A superseded Collection association disappeared before Local restore reconciliation completed.");
					if (current.State == CollectionAssociationState.Incomplete)
						continue;
					if (current.State != CollectionAssociationState.Recovering)
						throw new InvalidOperationException("A superseded Collection association no longer owns the expected Local restore recovery boundary.");

					SaveAssociation(connection, transaction, current.WithState(CollectionAssociationState.Incomplete));
				}
			});
		}

		/// <summary>
		/// Atomically updates association drift state and the exact current observations produced by one manual native mutation.
		/// </summary>
		/// <remarks>
		/// This writes Collections feature metadata only. Native InstallLog/deployment state remains authoritative. An observation
		/// replaces any older observation for the same exact requirement so callers never accumulate contradictory current drift.
		/// </remarks>
		internal void SaveManualMutationDrift(IEnumerable<CollectionTargetAssociation> associations,
			IEnumerable<CollectionDriftObservation> driftObservations, IEnumerable<CollectionRequirementReference> clearedRequirements)
		{
			if (associations == null)
				throw new ArgumentNullException(nameof(associations));
			if (driftObservations == null)
				throw new ArgumentNullException(nameof(driftObservations));
			if (clearedRequirements == null)
				throw new ArgumentNullException(nameof(clearedRequirements));

			List<CollectionTargetAssociation> copiedAssociations = new List<CollectionTargetAssociation>(associations);
			List<CollectionDriftObservation> copiedDrift = new List<CollectionDriftObservation>(driftObservations);
			List<CollectionRequirementReference> copiedCleared = new List<CollectionRequirementReference>(clearedRequirements);
			_store.ExecuteWrite((connection, transaction) =>
			{
				foreach (CollectionTargetAssociation association in copiedAssociations)
				{
					if (association == null)
						throw new ArgumentException("A manual-mutation association update cannot contain null records.", nameof(associations));
					SaveAssociation(connection, transaction, association);
				}

				foreach (CollectionRequirementReference requirement in copiedCleared)
				{
					if (requirement == null)
						throw new ArgumentException("A cleared drift requirement cannot be null.", nameof(clearedRequirements));
					RequireAssociationForRequirement(connection, transaction, requirement);
					DeleteDriftObservationForRequirement(connection, transaction, requirement);
				}

				foreach (CollectionDriftObservation drift in copiedDrift)
				{
					if (drift == null)
						throw new ArgumentException("A manual-mutation drift update cannot contain null records.", nameof(driftObservations));
					RequireAssociationForRequirement(connection, transaction, drift.Requirement);
					SaveDriftObservation(connection, transaction, drift);
				}
			});
		}

		/// <summary>
		/// Atomically publishes a verify-proven healthy association state, including exact member rebinding and stale-drift cleanup.
		/// </summary>
		internal void SaveVerifiedHealthyReconciliation(CollectionTargetAssociation expectedAssociation,
			CollectionTargetAssociation finalAssociation, IEnumerable<CollectionMemberBinding> bindingUpdates,
			IEnumerable<CollectionRequirementReference> clearedRequirements)
		{
			if (expectedAssociation == null) throw new ArgumentNullException(nameof(expectedAssociation));
			if (finalAssociation == null) throw new ArgumentNullException(nameof(finalAssociation));
			if (bindingUpdates == null) throw new ArgumentNullException(nameof(bindingUpdates));
			if (clearedRequirements == null) throw new ArgumentNullException(nameof(clearedRequirements));
			if (expectedAssociation.AssociationId != finalAssociation.AssociationId ||
				!expectedAssociation.Revision.Equals(finalAssociation.Revision) ||
				!expectedAssociation.Target.Equals(finalAssociation.Target))
				throw new ArgumentException("Verify reconciliation must preserve the exact association identity/revision/target.", nameof(finalAssociation));

			List<CollectionMemberBinding> copiedBindings = bindingUpdates.ToList();
			List<CollectionRequirementReference> copiedCleared = clearedRequirements.ToList();
			if (copiedBindings.Any(x => x == null || x.Association.AssociationId != expectedAssociation.AssociationId ||
				!x.Association.Revision.Equals(expectedAssociation.Revision) || !x.Association.Target.Equals(expectedAssociation.Target)))
				throw new ArgumentException("Every verify reconciliation binding must belong to the exact association baseline.", nameof(bindingUpdates));
			if (copiedCleared.Any(x => x == null || x.AssociationId != expectedAssociation.AssociationId ||
				!x.BaselineRevision.Equals(expectedAssociation.Revision) || !x.Target.Equals(expectedAssociation.Target)))
				throw new ArgumentException("Every cleared verify requirement must belong to the exact association baseline.", nameof(clearedRequirements));

			_store.ExecuteWrite((connection, transaction) =>
			{
				CollectionTargetAssociation current = ReadAssociation(connection, transaction, expectedAssociation.AssociationId);
				if (current == null || !current.Revision.Equals(expectedAssociation.Revision) ||
					!current.Target.Equals(expectedAssociation.Target) || current.State != expectedAssociation.State)
					throw new InvalidOperationException("The Collection association changed after verify/repair assessment.");

				SaveAssociation(connection, transaction, finalAssociation);
				foreach (CollectionMemberBinding binding in copiedBindings)
					SaveBinding(connection, transaction, binding);
				foreach (CollectionRequirementReference requirement in copiedCleared)
					DeleteDriftObservationForRequirement(connection, transaction, requirement);
			});
		}

		/// <summary>
		/// Atomically records one C6.12 explicit user decision and its remaining observed drift.
		/// </summary>
		/// <remarks>
		/// Empty expected identities mean the caller observed no current record. This compare-and-swap prevents stale UI or
		/// asynchronous native notifications from silently replacing a newer explicit decision. Association recovery state is
		/// never hidden by an override, and clearing an override does not mark a previously Modified association Applied.
		/// </remarks>
		internal void SaveUserOverrideDecision(CollectionRequirementReference requirement,
			CollectionRequirementState baselineState, UserOverride userOverride, CollectionDriftObservation drift,
			Guid expectedOverrideId, Guid expectedDriftObservationId)
		{
			if (requirement == null) throw new ArgumentNullException(nameof(requirement));
			if (baselineState == null) throw new ArgumentNullException(nameof(baselineState));
			if (userOverride != null && (!userOverride.Requirement.Equals(requirement) ||
				!userOverride.BaselineState.Equals(baselineState)))
				throw new ArgumentException("The user override does not match the exact requirement/baseline being recorded.", nameof(userOverride));

			CollectionRequirementState expectedState = userOverride == null ? baselineState : userOverride.UserChosenState;
			if (drift != null && (!drift.Requirement.Equals(requirement) || !drift.ExpectedState.Equals(expectedState)))
				throw new ArgumentException("The remaining drift must be measured from the exact explicit user decision.", nameof(drift));

			_store.ExecuteWrite((connection, transaction) =>
			{
				CollectionTargetAssociation association = RequireAssociationForRequirement(connection, transaction, requirement);
				if (association.State == CollectionAssociationState.Recovering)
					throw new InvalidOperationException("A recovering Collection association must be reconciled before user overrides can be changed.");
				if (HasIncompleteOperationForCollection(connection, transaction, association.Revision.Collection, association.Target))
					throw new InvalidOperationException("Collection user overrides cannot be changed while another operation for this Collection/target is incomplete.");

				UserOverride currentOverride = ReadOverrideForRequirement(connection, transaction, association, requirement);
				CollectionDriftObservation currentDrift = ReadDriftObservationForRequirement(connection, transaction, association, requirement);
				ValidateExpectedIdentity(currentOverride == null ? Guid.Empty : currentOverride.OverrideId,
					expectedOverrideId, "Collection user override");
				ValidateExpectedIdentity(currentDrift == null ? Guid.Empty : currentDrift.ObservationId,
					expectedDriftObservationId, "Collection drift observation");
				if (currentOverride != null && !currentOverride.BaselineState.Equals(baselineState))
					throw new InvalidOperationException("The current Collection override is pinned to a different baseline state.");

				if (userOverride == null)
					DeleteOverrideForRequirement(connection, transaction, requirement);
				else
				{
					EnsureOverrideIdentityAndRequirementAreCompatible(connection, transaction, association, userOverride);
					SaveOverride(connection, transaction, userOverride);
				}

				DeleteDriftObservationForRequirement(connection, transaction, requirement);
				if (drift != null)
					SaveDriftObservation(connection, transaction, drift);

				if (association.State == CollectionAssociationState.Applied && (userOverride != null || drift != null))
					SaveAssociation(connection, transaction, association.WithState(CollectionAssociationState.Modified));
			});
		}

		private const string AssociationSelect = @"
SELECT ta.association_id, cr.origin, cr.collection_id, cr.revision_id, cr.nexus_revision_number,
       ta.target_fingerprint, ta.state
FROM target_associations ta
JOIN collection_revisions cr
  ON cr.origin = ta.origin AND cr.collection_id = ta.collection_id AND cr.revision_id = ta.revision_id";

		private static void SaveAssociation(SQLiteConnection connection, SQLiteTransaction transaction,
			CollectionTargetAssociation association)
		{
			RequirePersistedRevision(connection, transaction, association.Revision);
			CollectionTargetAssociation existing = ReadAssociation(connection, transaction, association.AssociationId);
			if (existing != null)
			{
				if (!existing.Revision.Equals(association.Revision) || !existing.Target.Equals(association.Target))
					throw new InvalidOperationException("A persisted Collection association identity cannot be rebound to another revision or target.");

				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = "UPDATE target_associations SET state=@state WHERE association_id=@association_id;";
					command.Parameters.AddWithValue("@state", (int)association.State);
					command.Parameters.AddWithValue("@association_id", association.AssociationId.ToString("D"));
					command.ExecuteNonQuery();
				}
				return;
			}

			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
INSERT INTO target_associations
    (association_id, origin, collection_id, revision_id, target_fingerprint, state)
VALUES
    (@association_id, @origin, @collection_id, @revision_id, @target_fingerprint, @state);";
				command.Parameters.AddWithValue("@association_id", association.AssociationId.ToString("D"));
				AddRevisionKeyParameters(command, association.Revision);
				command.Parameters.AddWithValue("@target_fingerprint", association.Target.Fingerprint);
				command.Parameters.AddWithValue("@state", (int)association.State);
				command.ExecuteNonQuery();
			}
		}

		private static void SaveBinding(SQLiteConnection connection, SQLiteTransaction transaction, CollectionMemberBinding binding)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
UPDATE member_bindings
SET native_target_fingerprint = @native_target_fingerprint,
    native_mod_key = @native_mod_key,
    verified_recipe_fingerprint = @verified_recipe_fingerprint,
    binding_kind = @binding_kind
WHERE association_id = @association_id
  AND member_key_kind = @member_key_kind
  AND member_key_value = @member_key_value;";
				AddBindingParameters(command, binding);
				if (command.ExecuteNonQuery() > 0)
					return;
			}

			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
INSERT INTO member_bindings
    (association_id, member_key_kind, member_key_value, native_target_fingerprint,
     native_mod_key, verified_recipe_fingerprint, binding_kind)
VALUES
    (@association_id, @member_key_kind, @member_key_value, @native_target_fingerprint,
     @native_mod_key, @verified_recipe_fingerprint, @binding_kind);";
				AddBindingParameters(command, binding);
				command.ExecuteNonQuery();
			}
		}

		private static void SaveNativeModProvenance(SQLiteConnection connection, SQLiteTransaction transaction,
			NativeModProvenance provenance)
		{
			if (provenance.StandaloneUse == StandaloneModUse.Unknown)
				throw new ArgumentOutOfRangeException(nameof(provenance), "Unknown standalone use cannot be persisted as positive provenance.");

			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
UPDATE native_mod_provenance
SET standalone_use=@standalone_use
WHERE target_fingerprint=@target_fingerprint AND native_mod_key=@native_mod_key;";
				AddNativeModProvenanceParameters(command, provenance);
				if (command.ExecuteNonQuery() > 0)
					return;
			}

			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
INSERT INTO native_mod_provenance (target_fingerprint, native_mod_key, standalone_use)
VALUES (@target_fingerprint, @native_mod_key, @standalone_use);";
				AddNativeModProvenanceParameters(command, provenance);
				command.ExecuteNonQuery();
			}
		}

		private static void DeleteNativeModProvenance(SQLiteConnection connection, SQLiteTransaction transaction,
			NativeModInstanceIdentity nativeMod)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
DELETE FROM native_mod_provenance
WHERE target_fingerprint=@target_fingerprint AND native_mod_key=@native_mod_key;";
				command.Parameters.AddWithValue("@target_fingerprint", nativeMod.Target.Fingerprint);
				command.Parameters.AddWithValue("@native_mod_key", nativeMod.NativeModKey);
				command.ExecuteNonQuery();
			}
		}

		private static NativeModProvenance ReadNativeModProvenance(SQLiteConnection connection, SQLiteTransaction transaction,
			NativeModInstanceIdentity nativeMod)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT standalone_use
FROM native_mod_provenance
WHERE target_fingerprint=@target_fingerprint AND native_mod_key=@native_mod_key;";
				command.Parameters.AddWithValue("@target_fingerprint", nativeMod.Target.Fingerprint);
				command.Parameters.AddWithValue("@native_mod_key", nativeMod.NativeModKey);
				object value = command.ExecuteScalar();
				if (value == null || value == DBNull.Value)
					return null;

				StandaloneModUse standaloneUse = (StandaloneModUse)Convert.ToInt32(value, CultureInfo.InvariantCulture);
				if (!Enum.IsDefined(typeof(StandaloneModUse), standaloneUse) || standaloneUse == StandaloneModUse.Unknown)
					throw new CollectionsStoreSchemaException("A persisted native-mod standalone provenance row has an invalid state.");
				return new NativeModProvenance(nativeMod, standaloneUse);
			}
		}

		private static void RequireSameAssociationSnapshot(CollectionTargetAssociation current,
			CollectionTargetAssociation expected, string message)
		{
			if (current == null || expected == null || current.AssociationId != expected.AssociationId ||
				!current.Revision.Equals(expected.Revision) || !current.Target.Equals(expected.Target) || current.State != expected.State)
				throw new InvalidOperationException(message);
		}

		private static void RequireMemberRemovalOperationAssociation(CollectionTargetAssociation association, CollectionOperation operation)
		{
			if (association == null)
				throw new InvalidOperationException("The Collection association required by the installed-member removal no longer exists.");
			if (operation == null || operation.Kind != CollectionOperationKind.RemoveCollectionMemberEffects || operation.Revision == null ||
				!association.Revision.Equals(operation.Revision) || !association.Target.Equals(operation.Target) ||
				!association.Revision.Collection.Equals(operation.Collection))
				throw new InvalidOperationException("The installed-member removal operation no longer matches the persisted Collection association.");
		}

		private static void RequireOperationAssociation(CollectionTargetAssociation association, CollectionOperation operation)
		{
			if (association == null)
				throw new InvalidOperationException("The Collection association required by the uninstall operation no longer exists.");
			if (operation == null || operation.Kind != CollectionOperationKind.UninstallCollectionEffects || operation.Revision == null ||
				!association.Revision.Equals(operation.Revision) || !association.Target.Equals(operation.Target) ||
				!association.Revision.Collection.Equals(operation.Collection))
				throw new InvalidOperationException("The C6.14 operation no longer matches the persisted Collection association.");
		}

		private static void AddNativeModProvenanceParameters(SQLiteCommand command, NativeModProvenance provenance)
		{
			command.Parameters.AddWithValue("@target_fingerprint", provenance.NativeMod.Target.Fingerprint);
			command.Parameters.AddWithValue("@native_mod_key", provenance.NativeMod.NativeModKey);
			command.Parameters.AddWithValue("@standalone_use", (int)provenance.StandaloneUse);
		}

		private static bool HasCustomizationForMembers(SQLiteConnection connection, SQLiteTransaction transaction,
			CollectionTargetAssociation association, IEnumerable<CollectionMemberKey> memberKeys)
		{
			var keys = new HashSet<CollectionMemberKey>(memberKeys ?? Enumerable.Empty<CollectionMemberKey>());
			string[] tables = { "user_overrides", "drift_observations" };
			foreach (string table in tables)
			{
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = "SELECT member_key_kind, member_key_value FROM " + table +
						" WHERE association_id=@association_id;";
					command.Parameters.AddWithValue("@association_id", association.AssociationId.ToString("D"));
					using (SQLiteDataReader reader = command.ExecuteReader())
					{
						while (reader.Read())
						{
							CollectionMemberKey memberKey = ReadNullableMemberKey(reader, 0, 1);
							if (memberKey == null || keys.Contains(memberKey))
								return true;
						}
					}
				}
			}
			return false;
		}

		private static bool HasIncompleteUninstallOperationForTarget(SQLiteConnection connection, SQLiteTransaction transaction,
			CollectionTargetIdentity target)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT 1
FROM collection_operations
WHERE target_fingerprint=@target_fingerprint AND kind=@kind AND phase<>@completed_phase
LIMIT 1;";
				command.Parameters.AddWithValue("@target_fingerprint", target.Fingerprint);
				command.Parameters.AddWithValue("@kind", (int)CollectionOperationKind.UninstallCollectionEffects);
				command.Parameters.AddWithValue("@completed_phase", (int)CollectionOperationPhase.Completed);
				return command.ExecuteScalar() != null;
			}
		}

		private static bool HasIncompleteOperationForCollection(SQLiteConnection connection, SQLiteTransaction transaction,
			CollectionIdentity collection, CollectionTargetIdentity target)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT 1
FROM collection_operations
WHERE origin=@origin AND collection_id=@collection_id AND target_fingerprint=@target_fingerprint
  AND phase<>@completed_phase
LIMIT 1;";
				command.Parameters.AddWithValue("@origin", (int)collection.Origin);
				command.Parameters.AddWithValue("@collection_id", collection.StableId);
				command.Parameters.AddWithValue("@target_fingerprint", target.Fingerprint);
				command.Parameters.AddWithValue("@completed_phase", (int)CollectionOperationPhase.Completed);
				return command.ExecuteScalar() != null;
			}
		}

		private static void DeleteDriftObservationForRequirement(SQLiteConnection connection, SQLiteTransaction transaction,
			CollectionRequirementReference requirement)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
DELETE FROM drift_observations
WHERE association_id=@association_id
  AND baseline_revision_id=@baseline_revision_id
  AND target_fingerprint=@target_fingerprint
  AND ((member_key_kind=@member_key_kind) OR (member_key_kind IS NULL AND @member_key_kind IS NULL))
  AND ((member_key_value=@member_key_value) OR (member_key_value IS NULL AND @member_key_value IS NULL))
  AND aspect=@aspect
  AND ((subject_key=@subject_key) OR (subject_key IS NULL AND @subject_key IS NULL));";
				AddRequirementParameters(command, requirement);
				command.ExecuteNonQuery();
			}
		}

		private static void SaveDriftObservation(SQLiteConnection connection, SQLiteTransaction transaction, CollectionDriftObservation drift)
		{
			DeleteDriftObservationForRequirement(connection, transaction, drift.Requirement);

			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
INSERT INTO drift_observations
    (observation_id, association_id, baseline_revision_id, target_fingerprint,
     member_key_kind, member_key_value, aspect, subject_key,
     expected_state_kind, expected_state_format_version, expected_state_fingerprint,
     observed_state_kind, observed_state_format_version, observed_state_fingerprint, detail)
VALUES
    (@observation_id, @association_id, @baseline_revision_id, @target_fingerprint,
     @member_key_kind, @member_key_value, @aspect, @subject_key,
     @expected_state_kind, @expected_state_format_version, @expected_state_fingerprint,
     @observed_state_kind, @observed_state_format_version, @observed_state_fingerprint, @detail);";
				command.Parameters.AddWithValue("@observation_id", drift.ObservationId.ToString("D"));
				AddRequirementParameters(command, drift.Requirement);
				AddRequirementStateParameters(command, "expected", drift.ExpectedState);
				AddRequirementStateParameters(command, "observed", drift.ObservedState);
				command.Parameters.AddWithValue("@detail", DbValue(drift.Detail));
				command.ExecuteNonQuery();
			}
		}

		private static IReadOnlyList<CollectionDriftObservation> ReadDriftObservations(SQLiteConnection connection,
			SQLiteTransaction transaction, CollectionTargetAssociation association)
		{
			var drift = new List<CollectionDriftObservation>();
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT observation_id, association_id, baseline_revision_id, target_fingerprint,
       member_key_kind, member_key_value, aspect, subject_key,
       expected_state_kind, expected_state_format_version, expected_state_fingerprint,
       observed_state_kind, observed_state_format_version, observed_state_fingerprint, detail
FROM drift_observations
WHERE association_id=@association_id
ORDER BY observation_id;";
				command.Parameters.AddWithValue("@association_id", association.AssociationId.ToString("D"));
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					while (reader.Read())
						drift.Add(ReadDriftObservation(reader, association));
				}
			}
			return drift;
		}

		private static CollectionDriftObservation ReadDriftObservation(SQLiteDataReader reader, CollectionTargetAssociation association)
		{
			Guid observationId = ReadCanonicalGuid(reader.GetString(0), "Collection drift observation");
			Guid associationId = ReadCanonicalGuid(reader.GetString(1), "Collection drift association");
			if (associationId != association.AssociationId ||
				!StringComparer.Ordinal.Equals(reader.GetString(2), association.Revision.StableRevisionId) ||
				!StringComparer.Ordinal.Equals(reader.GetString(3), association.Target.Fingerprint))
				throw new CollectionsStoreSchemaException("A persisted drift observation references the wrong association baseline.");

			CollectionMemberKey memberKey = ReadNullableMemberKey(reader, 4, 5);
			CollectionRequirementAspect aspect = (CollectionRequirementAspect)reader.GetInt32(6);
			if (!Enum.IsDefined(typeof(CollectionRequirementAspect), aspect) || aspect == CollectionRequirementAspect.Unknown)
				throw new CollectionsStoreSchemaException("A persisted drift observation has an invalid requirement aspect.");

			CollectionRequirementReference requirement;
			try
			{
				requirement = new CollectionRequirementReference(association, memberKey, aspect, ReadNullableString(reader, 7));
				return new CollectionDriftObservation(observationId, requirement,
					ReadRequirementState(reader, 8, 9, 10, "expected"),
					ReadRequirementState(reader, 11, 12, 13, "observed"), ReadNullableString(reader, 14));
			}
			catch (ArgumentException ex)
			{
				throw new CollectionsStoreSchemaException("A persisted drift observation is inconsistent with the Collection domain model.", ex);
			}
		}


		private static UserOverride ReadOverrideForRequirement(SQLiteConnection connection, SQLiteTransaction transaction,
			CollectionTargetAssociation association, CollectionRequirementReference requirement)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT override_id, association_id, baseline_revision_id, target_fingerprint,
       member_key_kind, member_key_value, aspect, subject_key,
       baseline_state_kind, baseline_state_format_version, baseline_state_fingerprint,
       chosen_state_kind, chosen_state_format_version, chosen_state_fingerprint, note
FROM user_overrides
WHERE association_id=@association_id
  AND baseline_revision_id=@baseline_revision_id
  AND target_fingerprint=@target_fingerprint
  AND ((member_key_kind=@member_key_kind) OR (member_key_kind IS NULL AND @member_key_kind IS NULL))
  AND ((member_key_value=@member_key_value) OR (member_key_value IS NULL AND @member_key_value IS NULL))
  AND aspect=@aspect
  AND ((subject_key=@subject_key) OR (subject_key IS NULL AND @subject_key IS NULL));";
				AddRequirementParameters(command, requirement);
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					if (!reader.Read())
						return null;
					UserOverride value = ReadOverride(reader, association);
					if (reader.Read())
						throw new CollectionsStoreSchemaException("Multiple active user overrides exist for the same exact Collection requirement.");
					return value;
				}
			}
		}

		private static CollectionDriftObservation ReadDriftObservationForRequirement(SQLiteConnection connection,
			SQLiteTransaction transaction, CollectionTargetAssociation association, CollectionRequirementReference requirement)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT observation_id, association_id, baseline_revision_id, target_fingerprint,
       member_key_kind, member_key_value, aspect, subject_key,
       expected_state_kind, expected_state_format_version, expected_state_fingerprint,
       observed_state_kind, observed_state_format_version, observed_state_fingerprint, detail
FROM drift_observations
WHERE association_id=@association_id
  AND baseline_revision_id=@baseline_revision_id
  AND target_fingerprint=@target_fingerprint
  AND ((member_key_kind=@member_key_kind) OR (member_key_kind IS NULL AND @member_key_kind IS NULL))
  AND ((member_key_value=@member_key_value) OR (member_key_value IS NULL AND @member_key_value IS NULL))
  AND aspect=@aspect
  AND ((subject_key=@subject_key) OR (subject_key IS NULL AND @subject_key IS NULL));";
				AddRequirementParameters(command, requirement);
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					if (!reader.Read())
						return null;
					CollectionDriftObservation value = ReadDriftObservation(reader, association);
					if (reader.Read())
						throw new CollectionsStoreSchemaException("Multiple current drift observations exist for the same exact Collection requirement.");
					return value;
				}
			}
		}

		private static void DeleteOverrideForRequirement(SQLiteConnection connection, SQLiteTransaction transaction,
			CollectionRequirementReference requirement)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
DELETE FROM user_overrides
WHERE association_id=@association_id
  AND baseline_revision_id=@baseline_revision_id
  AND target_fingerprint=@target_fingerprint
  AND ((member_key_kind=@member_key_kind) OR (member_key_kind IS NULL AND @member_key_kind IS NULL))
  AND ((member_key_value=@member_key_value) OR (member_key_value IS NULL AND @member_key_value IS NULL))
  AND aspect=@aspect
  AND ((subject_key=@subject_key) OR (subject_key IS NULL AND @subject_key IS NULL));";
				AddRequirementParameters(command, requirement);
				command.ExecuteNonQuery();
			}
		}

		private static void ValidateExpectedIdentity(Guid currentId, Guid expectedId, string description)
		{
			if (currentId != expectedId)
				throw new InvalidOperationException(description + " changed since the user decision was prepared; refresh Collection state and retry.");
		}

		private static void SaveOverride(SQLiteConnection connection, SQLiteTransaction transaction, UserOverride userOverride)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
UPDATE user_overrides
SET chosen_state_kind = @chosen_state_kind,
    chosen_state_format_version = @chosen_state_format_version,
    chosen_state_fingerprint = @chosen_state_fingerprint,
    note = @note
WHERE override_id = @override_id;";
				AddOverrideParameters(command, userOverride);
				if (command.ExecuteNonQuery() > 0)
					return;
			}

			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
INSERT INTO user_overrides
    (override_id, association_id, baseline_revision_id, target_fingerprint,
     member_key_kind, member_key_value, aspect, subject_key,
     baseline_state_kind, baseline_state_format_version, baseline_state_fingerprint,
     chosen_state_kind, chosen_state_format_version, chosen_state_fingerprint, note)
VALUES
    (@override_id, @association_id, @baseline_revision_id, @target_fingerprint,
     @member_key_kind, @member_key_value, @aspect, @subject_key,
     @baseline_state_kind, @baseline_state_format_version, @baseline_state_fingerprint,
     @chosen_state_kind, @chosen_state_format_version, @chosen_state_fingerprint, @note);";
				AddOverrideParameters(command, userOverride);
				command.ExecuteNonQuery();
			}
		}

		private static HashSet<Guid> ReadAssociationIdsForTarget(SQLiteConnection connection, SQLiteTransaction transaction,
			CollectionTargetIdentity target)
		{
			var result = new HashSet<Guid>();
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = "SELECT association_id FROM target_associations WHERE target_fingerprint = @target_fingerprint;";
				command.Parameters.AddWithValue("@target_fingerprint", target.Fingerprint);
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					while (reader.Read())
						result.Add(ReadCanonicalGuid(reader.GetString(0), "Collection target association"));
				}
			}
			return result;
		}

		private static CollectionTargetAssociation ReadAssociation(SQLiteConnection connection, SQLiteTransaction transaction, Guid associationId)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = AssociationSelect + " WHERE ta.association_id = @association_id;";
				command.Parameters.AddWithValue("@association_id", associationId.ToString("D"));
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					return reader.Read() ? ReadAssociation(reader) : null;
				}
			}
		}

		private static CollectionTargetAssociation ReadAssociation(SQLiteDataReader reader)
		{
			Guid associationId = ReadCanonicalGuid(reader.GetString(0), "Collection association");
			CollectionRevisionIdentity revision = ReadRevisionIdentity(reader.GetInt32(1), reader.GetString(2),
				reader.GetString(3), ReadNullableInt64(reader, 4));
			CollectionTargetIdentity target = ReadTarget(reader.GetString(5));
			CollectionAssociationState state = (CollectionAssociationState)reader.GetInt32(6);
			if (!Enum.IsDefined(typeof(CollectionAssociationState), state) || state == CollectionAssociationState.Unknown)
				throw new CollectionsStoreSchemaException("A persisted Collection association has an invalid state.");

			return new CollectionTargetAssociation(associationId, revision, target, state);
		}

		private static IReadOnlyList<CollectionMemberBinding> ReadBindings(SQLiteConnection connection, SQLiteTransaction transaction,
			CollectionTargetAssociation association, NativeModInstanceIdentity nativeMod)
		{
			var bindings = new List<CollectionMemberBinding>();
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				if (association != null)
				{
					command.CommandText = @"
SELECT association_id, member_key_kind, member_key_value, native_target_fingerprint,
       native_mod_key, verified_recipe_fingerprint, binding_kind
FROM member_bindings
WHERE association_id = @association_id
ORDER BY member_key_kind, member_key_value;";
					command.Parameters.AddWithValue("@association_id", association.AssociationId.ToString("D"));
					using (SQLiteDataReader reader = command.ExecuteReader())
					{
						while (reader.Read())
							bindings.Add(ReadBinding(reader, association));
					}
				}
				else
				{
					command.CommandText = @"
SELECT mb.association_id, mb.member_key_kind, mb.member_key_value, mb.native_target_fingerprint,
       mb.native_mod_key, mb.verified_recipe_fingerprint, mb.binding_kind,
       cr.origin, cr.collection_id, cr.revision_id, cr.nexus_revision_number, ta.target_fingerprint, ta.state
FROM member_bindings mb
JOIN target_associations ta ON ta.association_id = mb.association_id
JOIN collection_revisions cr
  ON cr.origin = ta.origin AND cr.collection_id = ta.collection_id AND cr.revision_id = ta.revision_id
WHERE mb.native_target_fingerprint = @native_target_fingerprint AND mb.native_mod_key = @native_mod_key
ORDER BY mb.association_id, mb.member_key_kind, mb.member_key_value;";
					command.Parameters.AddWithValue("@native_target_fingerprint", nativeMod.Target.Fingerprint);
					command.Parameters.AddWithValue("@native_mod_key", nativeMod.NativeModKey);
					using (SQLiteDataReader reader = command.ExecuteReader())
					{
						while (reader.Read())
						{
							Guid associationId = ReadCanonicalGuid(reader.GetString(0), "Collection member binding association");
							CollectionRevisionIdentity revision = ReadRevisionIdentity(reader.GetInt32(7), reader.GetString(8),
								reader.GetString(9), ReadNullableInt64(reader, 10));
							CollectionTargetIdentity target = ReadTarget(reader.GetString(11));
							CollectionAssociationState state = (CollectionAssociationState)reader.GetInt32(12);
							if (!Enum.IsDefined(typeof(CollectionAssociationState), state) || state == CollectionAssociationState.Unknown)
								throw new CollectionsStoreSchemaException("A persisted Collection association has an invalid state.");
							CollectionTargetAssociation bindingAssociation = new CollectionTargetAssociation(associationId, revision, target, state);
							bindings.Add(ReadBinding(reader, bindingAssociation));
						}
					}
				}
			}
			return bindings;
		}

		private static CollectionMemberBinding ReadBinding(SQLiteDataReader reader, CollectionTargetAssociation association)
		{
			CollectionMemberKey memberKey = ReadMemberKey(reader.GetInt32(1), reader.GetString(2));
			CollectionTargetIdentity nativeTarget = ReadTarget(reader.GetString(3));
			if (!association.Target.Equals(nativeTarget))
				throw new CollectionsStoreSchemaException("A persisted Collection member binding targets a different native target than its association.");

			NativeModInstanceIdentity nativeMod;
			CollectionRecipeIdentity recipe;
			try
			{
				nativeMod = new NativeModInstanceIdentity(nativeTarget, reader.GetString(4));
				recipe = CollectionRecipeIdentity.FromFingerprint(reader.GetString(5));
			}
			catch (ArgumentException ex)
			{
				throw new CollectionsStoreSchemaException("A persisted Collection member binding contains an invalid identity.", ex);
			}

			CollectionMemberBindingKind kind = (CollectionMemberBindingKind)reader.GetInt32(6);
			if (!Enum.IsDefined(typeof(CollectionMemberBindingKind), kind) || kind == CollectionMemberBindingKind.Unknown)
				throw new CollectionsStoreSchemaException("A persisted Collection member binding has an invalid binding kind.");

			return new CollectionMemberBinding(association, memberKey, nativeMod, recipe, kind);
		}

		private static List<UserOverride> ReadOverrides(SQLiteConnection connection, SQLiteTransaction transaction,
			CollectionTargetAssociation association)
		{
			var overrides = new List<UserOverride>();
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT override_id, association_id, baseline_revision_id, target_fingerprint,
       member_key_kind, member_key_value, aspect, subject_key,
       baseline_state_kind, baseline_state_format_version, baseline_state_fingerprint,
       chosen_state_kind, chosen_state_format_version, chosen_state_fingerprint, note
FROM user_overrides
WHERE association_id = @association_id
ORDER BY override_id;";
				command.Parameters.AddWithValue("@association_id", association.AssociationId.ToString("D"));
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					while (reader.Read())
						overrides.Add(ReadOverride(reader, association));
				}
			}
			return overrides;
		}

		private static bool OverrideSetsEqual(IList<UserOverride> left, IList<UserOverride> right)
		{
			if (left == null || right == null || left.Count != right.Count) return false;
			Dictionary<Guid, UserOverride> byId = right.ToDictionary(x => x.OverrideId);
			foreach (UserOverride expected in left)
			{
				UserOverride actual;
				if (!byId.TryGetValue(expected.OverrideId, out actual) || !expected.Requirement.Equals(actual.Requirement) ||
					!expected.BaselineState.Equals(actual.BaselineState) || !expected.UserChosenState.Equals(actual.UserChosenState) ||
					!StringComparer.Ordinal.Equals(expected.Note, actual.Note))
					return false;
			}
			return true;
		}

		private static UserOverride ReadOverride(SQLiteDataReader reader, CollectionTargetAssociation association)
		{
			Guid overrideId = ReadCanonicalGuid(reader.GetString(0), "Collection user override");
			Guid associationId = ReadCanonicalGuid(reader.GetString(1), "Collection user override association");
			if (associationId != association.AssociationId)
				throw new CollectionsStoreSchemaException("A persisted user override references the wrong association.");
			if (!StringComparer.Ordinal.Equals(reader.GetString(2), association.Revision.StableRevisionId))
				throw new CollectionsStoreSchemaException("A persisted user override references the wrong baseline revision.");
			if (!StringComparer.Ordinal.Equals(reader.GetString(3), association.Target.Fingerprint))
				throw new CollectionsStoreSchemaException("A persisted user override references the wrong target.");

			CollectionMemberKey memberKey = ReadNullableMemberKey(reader, 4, 5);
			CollectionRequirementAspect aspect = (CollectionRequirementAspect)reader.GetInt32(6);
			if (!Enum.IsDefined(typeof(CollectionRequirementAspect), aspect) || aspect == CollectionRequirementAspect.Unknown)
				throw new CollectionsStoreSchemaException("A persisted user override has an invalid requirement aspect.");

			CollectionRequirementReference requirement;
			try
			{
				requirement = new CollectionRequirementReference(association, memberKey, aspect, ReadNullableString(reader, 7));
			}
			catch (ArgumentException ex)
			{
				throw new CollectionsStoreSchemaException("A persisted user override contains an invalid requirement reference.", ex);
			}

			CollectionRequirementState baselineState = ReadRequirementState(reader, 8, 9, 10, "baseline");
			CollectionRequirementState chosenState = ReadRequirementState(reader, 11, 12, 13, "chosen");
			try
			{
				return new UserOverride(overrideId, requirement, baselineState, chosenState, ReadNullableString(reader, 14));
			}
			catch (ArgumentException ex)
			{
				throw new CollectionsStoreSchemaException("A persisted user override is inconsistent with the Collection domain model.", ex);
			}
		}

		private static void RequirePersistedRevision(SQLiteConnection connection, SQLiteTransaction transaction,
			CollectionRevisionIdentity revision)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT nexus_revision_number
FROM collection_revisions
WHERE origin=@origin AND collection_id=@collection_id AND revision_id=@revision_id;";
				AddRevisionKeyParameters(command, revision);
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					if (!reader.Read())
						throw new InvalidOperationException("The exact Collection revision must be persisted before it can be associated with a target.");

					long? persistedNexusNumber = reader.IsDBNull(0) ? (long?)null : reader.GetInt64(0);
					if (persistedNexusNumber != revision.NexusRevisionNumber)
						throw new InvalidOperationException("The supplied Collection revision identity does not match the persisted concrete revision.");
				}
			}
		}

		private static void RequireMatchingAssociation(SQLiteConnection connection, SQLiteTransaction transaction,
			CollectionTargetAssociation association)
		{
			CollectionTargetAssociation persisted = ReadAssociation(connection, transaction, association.AssociationId);
			if (persisted == null)
				throw new InvalidOperationException("The Collection target association must be persisted before member bindings.");
			if (!persisted.Revision.Equals(association.Revision) || !persisted.Target.Equals(association.Target))
				throw new InvalidOperationException("The supplied Collection target association does not match the persisted baseline.");
		}

		private static CollectionTargetAssociation RequireAssociationForRequirement(SQLiteConnection connection,
			SQLiteTransaction transaction, CollectionRequirementReference requirement)
		{
			CollectionTargetAssociation association = ReadAssociation(connection, transaction, requirement.AssociationId);
			if (association == null)
				throw new InvalidOperationException("The Collection target association must be persisted before requirement customization or drift.");
			if (!association.Revision.Equals(requirement.BaselineRevision) || !association.Target.Equals(requirement.Target))
				throw new InvalidOperationException("The Collection requirement does not match the persisted association baseline.");
			return association;
		}

		private static void EnsureOverrideIdentityAndRequirementAreCompatible(SQLiteConnection connection,
			SQLiteTransaction transaction, CollectionTargetAssociation association, UserOverride userOverride)
		{
			UserOverride existingById = ReadOverrideById(connection, transaction, association, userOverride.OverrideId);
			if (existingById != null)
			{
				if (!existingById.Requirement.Equals(userOverride.Requirement) ||
					!existingById.BaselineState.Equals(userOverride.BaselineState))
					throw new InvalidOperationException("A persisted user override identity cannot be rebound to another requirement or baseline state.");
				return;
			}

			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT override_id
FROM user_overrides
WHERE association_id=@association_id
  AND baseline_revision_id=@baseline_revision_id
  AND target_fingerprint=@target_fingerprint
  AND ((member_key_kind=@member_key_kind) OR (member_key_kind IS NULL AND @member_key_kind IS NULL))
  AND ((member_key_value=@member_key_value) OR (member_key_value IS NULL AND @member_key_value IS NULL))
  AND aspect=@aspect
  AND ((subject_key=@subject_key) OR (subject_key IS NULL AND @subject_key IS NULL));";
				AddRequirementParameters(command, userOverride.Requirement);
				object value = command.ExecuteScalar();
				if (value != null && value != DBNull.Value)
					throw new InvalidOperationException("Only one active deliberate user override may exist for the same exact Collection requirement.");
			}
		}

		private static UserOverride ReadOverrideById(SQLiteConnection connection, SQLiteTransaction transaction,
			CollectionTargetAssociation association, Guid overrideId)
		{
			string persistedAssociationId;
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = "SELECT association_id FROM user_overrides WHERE override_id=@override_id;";
				command.Parameters.AddWithValue("@override_id", overrideId.ToString("D"));
				object value = command.ExecuteScalar();
				if (value == null || value == DBNull.Value)
					return null;
				persistedAssociationId = Convert.ToString(value, CultureInfo.InvariantCulture);
			}

			Guid persistedId = ReadCanonicalGuid(persistedAssociationId, "Collection user override association");
			CollectionTargetAssociation persistedAssociation = association;
			if (persistedAssociation.AssociationId != persistedId)
			{
				persistedAssociation = ReadAssociation(connection, transaction, persistedId);
				if (persistedAssociation == null)
					throw new CollectionsStoreSchemaException("A persisted user override references a missing association.");
			}

			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT override_id, association_id, baseline_revision_id, target_fingerprint,
       member_key_kind, member_key_value, aspect, subject_key,
       baseline_state_kind, baseline_state_format_version, baseline_state_fingerprint,
       chosen_state_kind, chosen_state_format_version, chosen_state_fingerprint, note
FROM user_overrides
WHERE override_id=@override_id;";
				command.Parameters.AddWithValue("@override_id", overrideId.ToString("D"));
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					return reader.Read() ? ReadOverride(reader, persistedAssociation) : null;
				}
			}
		}

		private static void AddBindingParameters(SQLiteCommand command, CollectionMemberBinding binding)
		{
			command.Parameters.AddWithValue("@association_id", binding.Association.AssociationId.ToString("D"));
			command.Parameters.AddWithValue("@member_key_kind", (int)binding.MemberKey.Kind);
			command.Parameters.AddWithValue("@member_key_value", binding.MemberKey.Value);
			command.Parameters.AddWithValue("@native_target_fingerprint", binding.NativeMod.Target.Fingerprint);
			command.Parameters.AddWithValue("@native_mod_key", binding.NativeMod.NativeModKey);
			command.Parameters.AddWithValue("@verified_recipe_fingerprint", binding.VerifiedRecipe.Fingerprint);
			command.Parameters.AddWithValue("@binding_kind", (int)binding.BindingKind);
		}

		private static void AddOverrideParameters(SQLiteCommand command, UserOverride userOverride)
		{
			command.Parameters.AddWithValue("@override_id", userOverride.OverrideId.ToString("D"));
			AddRequirementParameters(command, userOverride.Requirement);
			AddRequirementStateParameters(command, "baseline", userOverride.BaselineState);
			AddRequirementStateParameters(command, "chosen", userOverride.UserChosenState);
			command.Parameters.AddWithValue("@note", DbValue(userOverride.Note));
		}

		private static void AddRequirementParameters(SQLiteCommand command, CollectionRequirementReference requirement)
		{
			command.Parameters.AddWithValue("@association_id", requirement.AssociationId.ToString("D"));
			command.Parameters.AddWithValue("@baseline_revision_id", requirement.BaselineRevision.StableRevisionId);
			command.Parameters.AddWithValue("@target_fingerprint", requirement.Target.Fingerprint);
			command.Parameters.AddWithValue("@member_key_kind", DbValue(requirement.MemberKey == null ? (int?)null : (int)requirement.MemberKey.Kind));
			command.Parameters.AddWithValue("@member_key_value", DbValue(requirement.MemberKey == null ? null : requirement.MemberKey.Value));
			command.Parameters.AddWithValue("@aspect", (int)requirement.Aspect);
			command.Parameters.AddWithValue("@subject_key", DbValue(requirement.SubjectKey));
		}

		private static void AddRequirementStateParameters(SQLiteCommand command, string prefix, CollectionRequirementState state)
		{
			command.Parameters.AddWithValue("@" + prefix + "_state_kind", (int)state.Kind);
			command.Parameters.AddWithValue("@" + prefix + "_state_format_version", DbValue(state.FormatVersion));
			command.Parameters.AddWithValue("@" + prefix + "_state_fingerprint", DbValue(state.Fingerprint));
		}

		private static void AddRevisionKeyParameters(SQLiteCommand command, CollectionRevisionIdentity revision)
		{
			command.Parameters.AddWithValue("@origin", (int)revision.Collection.Origin);
			command.Parameters.AddWithValue("@collection_id", revision.Collection.StableId);
			command.Parameters.AddWithValue("@revision_id", revision.StableRevisionId);
		}

		private static CollectionRevisionIdentity ReadRevisionIdentity(int rawOrigin, string collectionId,
			string revisionId, long? nexusRevisionNumber)
		{
			CollectionIdentity collection;
			try
			{
				CollectionOrigin origin = (CollectionOrigin)rawOrigin;
				switch (origin)
				{
					case CollectionOrigin.NexusMods:
						collection = CollectionIdentity.FromNexus(collectionId);
						if (!nexusRevisionNumber.HasValue || nexusRevisionNumber.Value <= 0)
							throw new CollectionsStoreSchemaException("A persisted Nexus Collection revision is missing its concrete revision number.");
						return CollectionRevisionIdentity.FromNexus(collection, revisionId, nexusRevisionNumber.Value);
					case CollectionOrigin.Local:
						collection = CollectionIdentity.FromLocal(ReadCanonicalGuid(collectionId, "Local Collection"));
						if (nexusRevisionNumber.HasValue)
							throw new CollectionsStoreSchemaException("A persisted Local Collection revision cannot contain a Nexus revision number.");
						return CollectionRevisionIdentity.FromLocal(collection, ReadCanonicalGuid(revisionId, "Local Collection revision"));
					default:
						throw new CollectionsStoreSchemaException("A persisted Collection association has an unsupported origin value.");
				}
			}
			catch (ArgumentException ex)
			{
				throw new CollectionsStoreSchemaException("A persisted Collection association contains an invalid revision identity.", ex);
			}
		}

		private static CollectionTargetIdentity ReadTarget(string fingerprint)
		{
			try
			{
				return CollectionTargetIdentity.FromFingerprint(fingerprint);
			}
			catch (ArgumentException ex)
			{
				throw new CollectionsStoreSchemaException("A persisted Collection association contains an invalid target identity.", ex);
			}
		}

		private static CollectionMemberKey ReadNullableMemberKey(SQLiteDataReader reader, int kindOrdinal, int valueOrdinal)
		{
			bool kindMissing = reader.IsDBNull(kindOrdinal);
			bool valueMissing = reader.IsDBNull(valueOrdinal);
			if (kindMissing != valueMissing)
				throw new CollectionsStoreSchemaException("A persisted Collection requirement has an incomplete member key.");
			return kindMissing ? null : ReadMemberKey(reader.GetInt32(kindOrdinal), reader.GetString(valueOrdinal));
		}

		private static CollectionMemberKey ReadMemberKey(int rawKind, string value)
		{
			try
			{
				CollectionMemberKeyKind kind = (CollectionMemberKeyKind)rawKind;
				switch (kind)
				{
					case CollectionMemberKeyKind.ProviderStable:
						return CollectionMemberKey.FromProvider(value);
					case CollectionMemberKeyKind.Local:
						return CollectionMemberKey.FromLocal(ReadCanonicalGuid(value, "Local Collection member"));
					case CollectionMemberKeyKind.ValidatedMatch:
						return CollectionMemberKey.FromValidatedMatch(value);
					default:
						throw new CollectionsStoreSchemaException("A persisted Collection member has an invalid key kind.");
				}
			}
			catch (ArgumentException ex)
			{
				throw new CollectionsStoreSchemaException("A persisted Collection member has an invalid key.", ex);
			}
		}

		private static CollectionRequirementState ReadRequirementState(SQLiteDataReader reader, int kindOrdinal,
			int formatOrdinal, int fingerprintOrdinal, string label)
		{
			CollectionRequirementStateKind kind = (CollectionRequirementStateKind)reader.GetInt32(kindOrdinal);
			string formatVersion = ReadNullableString(reader, formatOrdinal);
			string fingerprint = ReadNullableString(reader, fingerprintOrdinal);
			try
			{
				switch (kind)
				{
					case CollectionRequirementStateKind.Absent:
						if (formatVersion != null || fingerprint != null)
							throw new CollectionsStoreSchemaException("A persisted absent " + label + " requirement state contains present-state data.");
						return CollectionRequirementState.Absent();
					case CollectionRequirementStateKind.Present:
						if (formatVersion == null || fingerprint == null)
							throw new CollectionsStoreSchemaException("A persisted present " + label + " requirement state is incomplete.");
						return CollectionRequirementState.Present(formatVersion, fingerprint);
					default:
						throw new CollectionsStoreSchemaException("A persisted " + label + " requirement state has an invalid kind.");
				}
			}
			catch (ArgumentException ex)
			{
				throw new CollectionsStoreSchemaException("A persisted " + label + " requirement state contains invalid identity data.", ex);
			}
		}

		private static long? ReadNullableInt64(SQLiteDataReader reader, int ordinal)
		{
			return reader.IsDBNull(ordinal) ? (long?)null : reader.GetInt64(ordinal);
		}

		private static string ReadNullableString(SQLiteDataReader reader, int ordinal)
		{
			return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
		}

		private static Guid ReadCanonicalGuid(string value, string label)
		{
			Guid parsed;
			if (!Guid.TryParseExact(value, "D", out parsed) || parsed == Guid.Empty ||
				!StringComparer.Ordinal.Equals(value, parsed.ToString("D")))
				throw new CollectionsStoreSchemaException(label + " has an invalid persisted GUID identity.");
			return parsed;
		}

		private static Guid RequireGuid(Guid value, string parameterName)
		{
			if (value == Guid.Empty)
				throw new ArgumentException("A non-empty identifier is required.", parameterName);
			return value;
		}

		private static object DbValue(object value)
		{
			return value ?? DBNull.Value;
		}
	}

	/// <summary>Immutable result of one atomic C6.13 association-detach persistence transaction.</summary>
	internal sealed class CollectionsAssociationDetachRecord
	{
		internal CollectionsAssociationDetachRecord(CollectionOperation operation, CollectionTargetAssociation association,
			IEnumerable<CollectionMemberBinding> bindings, IEnumerable<NativeModProvenance> standaloneProvenance)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Association = association ?? throw new ArgumentNullException(nameof(association));
			Bindings = new List<CollectionMemberBinding>(bindings ?? throw new ArgumentNullException(nameof(bindings)));
			StandaloneProvenance = new List<NativeModProvenance>(standaloneProvenance ?? throw new ArgumentNullException(nameof(standaloneProvenance)));
		}

		internal CollectionOperation Operation { get; }
		internal CollectionTargetAssociation Association { get; }
		internal IReadOnlyList<CollectionMemberBinding> Bindings { get; }
		internal IReadOnlyList<NativeModProvenance> StandaloneProvenance { get; }
	}
}
