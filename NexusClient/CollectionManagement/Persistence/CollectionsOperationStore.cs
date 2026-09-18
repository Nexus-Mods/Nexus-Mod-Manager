using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>
	/// Persists collection-level operations and their correlated native child-operation checkpoints.
	/// </summary>
	/// <remarks>
	/// The journal records durable intent, correlation, terminal status and independently verified native durability.
	/// It is not a native ownership log and never substitutes for InstallLog or deployment recovery. Human-readable
	/// native result messages are diagnostic UI text and are deliberately not part of the durable journal contract.
	/// </remarks>
	public sealed class CollectionsOperationStore
	{
		private readonly CollectionsStore _store;

		/// <summary>
		/// Creates an operation journal over an existing Collections feature store.
		/// </summary>
		public CollectionsOperationStore(CollectionsStore store)
		{
			_store = store ?? throw new ArgumentNullException(nameof(store));
		}

		/// <summary>
		/// Persists a complete collection-operation snapshot atomically.
		/// </summary>
		/// <remarks>
		/// Re-saving the same checkpoint is idempotent only when the durable snapshot is unchanged. Later snapshots must
		/// advance the collection checkpoint sequence. Existing child intents cannot disappear or be rebound, and child
		/// safety checkpoints/durability cannot regress.
		/// </remarks>
		public void SaveOperation(CollectionOperation operation)
		{
			if (operation == null)
				throw new ArgumentNullException(nameof(operation));

			_store.ExecuteWrite((connection, transaction) =>
			{
				RequirePersistedCollection(connection, transaction, operation.Collection);
				if (operation.Revision != null)
					RequirePersistedRevision(connection, transaction, operation.Revision);
				if (operation.PlanIdentity != null)
					RequirePersistedPlan(connection, transaction, operation, false);
				foreach (CollectionNativeChildOperation child in operation.NativeChildren)
					RequirePersistedRevision(connection, transaction, child.Member.Revision);

				CollectionOperation existing = ReadOperation(connection, transaction, operation.Identity.OperationId);
				if (existing == null)
				{
					InsertOperation(connection, transaction, operation);
					foreach (CollectionNativeChildOperation child in operation.NativeChildren)
						InsertChild(connection, transaction, operation.Identity.OperationId, child);
					return;
				}

				ValidateOperationProgression(existing, operation);
				if (SnapshotsEqual(existing, operation))
					return;

				if (operation.CheckpointSequence <= existing.CheckpointSequence)
					throw new InvalidOperationException("A changed Collection operation snapshot must advance its durable checkpoint sequence.");

				Dictionary<int, CollectionNativeChildOperation> existingChildren = IndexChildren(existing.NativeChildren);
				Dictionary<int, CollectionNativeChildOperation> incomingChildren = IndexChildren(operation.NativeChildren);
				foreach (KeyValuePair<int, CollectionNativeChildOperation> pair in existingChildren)
				{
					CollectionNativeChildOperation incoming;
					if (!incomingChildren.TryGetValue(pair.Key, out incoming))
						throw new InvalidOperationException("A persisted Collection native-child intent cannot be removed from the operation journal.");
					ValidateChildProgression(pair.Value, incoming);
				}

				int maximumExistingSequence = 0;
				foreach (int sequence in existingChildren.Keys)
					maximumExistingSequence = Math.Max(maximumExistingSequence, sequence);

				foreach (CollectionNativeChildOperation child in operation.NativeChildren)
				{
					CollectionNativeChildOperation oldChild;
					if (existingChildren.TryGetValue(child.Sequence, out oldChild))
						UpdateChild(connection, transaction, operation.Identity.OperationId, child);
					else
					{
						if (child.Sequence <= maximumExistingSequence)
							throw new InvalidOperationException("New Collection native-child intents must append after existing journal sequences.");
						InsertChild(connection, transaction, operation.Identity.OperationId, child);
					}
				}

				UpdateOperation(connection, transaction, operation);
			});
		}

		/// <summary>
		/// Loads one collection operation by durable identity, or <c>null</c> when it is not present.
		/// </summary>
		public CollectionOperation GetOperation(CollectionOperationIdentity identity)
		{
			if (identity == null)
				throw new ArgumentNullException(nameof(identity));

			return _store.ExecuteRead((connection, transaction) =>
				ReadOperation(connection, transaction, identity.OperationId));
		}

		/// <summary>
		/// Loads all non-terminal collection operations in stable identity order for startup/recovery reconciliation.
		/// </summary>
		public IReadOnlyList<CollectionOperation> GetIncompleteOperations()
		{
			return _store.ExecuteRead((connection, transaction) =>
			{
				List<Guid> operationIds = ReadOperationIds(connection, transaction, null);
				var operations = new List<CollectionOperation>(operationIds.Count);
				foreach (Guid operationId in operationIds)
					operations.Add(ReadOperation(connection, transaction, operationId));
				return operations;
			});
		}

		/// <summary>
		/// Loads all non-terminal collection operations for one exact target.
		/// </summary>
		public IReadOnlyList<CollectionOperation> GetIncompleteOperations(CollectionTargetIdentity target)
		{
			if (target == null)
				throw new ArgumentNullException(nameof(target));

			return _store.ExecuteRead((connection, transaction) =>
			{
				List<Guid> operationIds = ReadOperationIds(connection, transaction, target);
				var operations = new List<CollectionOperation>(operationIds.Count);
				foreach (Guid operationId in operationIds)
					operations.Add(ReadOperation(connection, transaction, operationId));
				return operations;
			});
		}

		/// <summary>
		/// Resolves the collection operation currently correlated with one exact native operation attempt.
		/// </summary>
		public CollectionOperation GetOperationForNativeAttempt(Guid nativeOperationId, Guid nativeAttemptId)
		{
			RequireGuid(nativeOperationId, nameof(nativeOperationId));
			RequireGuid(nativeAttemptId, nameof(nativeAttemptId));

			return _store.ExecuteRead((connection, transaction) =>
			{
				Guid? collectionOperationId = null;
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"
SELECT collection_operation_id
FROM native_operation_children
WHERE native_operation_id=@native_operation_id AND native_attempt_id=@native_attempt_id
ORDER BY collection_operation_id;";
					command.Parameters.AddWithValue("@native_operation_id", nativeOperationId.ToString("D"));
					command.Parameters.AddWithValue("@native_attempt_id", nativeAttemptId.ToString("D"));
					using (SQLiteDataReader reader = command.ExecuteReader())
					{
						while (reader.Read())
						{
							Guid current = ReadCanonicalGuid(reader.GetString(0), "Collection operation");
							if (collectionOperationId.HasValue && collectionOperationId.Value != current)
								throw new CollectionsStoreSchemaException("One native operation attempt is correlated with multiple Collection operations.");
							collectionOperationId = current;
						}
					}
				}

				return collectionOperationId.HasValue
					? ReadOperation(connection, transaction, collectionOperationId.Value)
					: null;
			});
		}

		private static void InsertOperation(SQLiteConnection connection, SQLiteTransaction transaction, CollectionOperation operation)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
INSERT INTO collection_operations
    (operation_id, kind, origin, collection_id, target_fingerprint, revision_id,
     plan_id, plan_version, checkpoint_sequence, phase, result_state)
VALUES
    (@operation_id, @kind, @origin, @collection_id, @target_fingerprint, @revision_id,
     @plan_id, @plan_version, @checkpoint_sequence, @phase, @result_state);";
				AddOperationParameters(command, operation);
				command.ExecuteNonQuery();
			}
		}

		private static void UpdateOperation(SQLiteConnection connection, SQLiteTransaction transaction, CollectionOperation operation)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
UPDATE collection_operations
SET revision_id=@revision_id,
    plan_id=@plan_id,
    plan_version=@plan_version,
    checkpoint_sequence=@checkpoint_sequence,
    phase=@phase,
    result_state=@result_state
WHERE operation_id=@operation_id;";
				AddOperationParameters(command, operation);
				if (command.ExecuteNonQuery() != 1)
					throw new CollectionsStoreSchemaException("The Collection operation disappeared while its journal snapshot was being updated.");
			}
		}

		private static void InsertChild(SQLiteConnection connection, SQLiteTransaction transaction, Guid collectionOperationId,
			CollectionNativeChildOperation child)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
INSERT INTO native_operation_children
    (collection_operation_id, sequence, member_origin, member_collection_id, member_revision_id,
     member_key_kind, member_key_value, action, native_operation_id, native_attempt_id, native_origin,
     native_target_fingerprint, native_context_fingerprint, native_recipe_fingerprint,
     checkpoint, reported_status, durability)
VALUES
    (@collection_operation_id, @sequence, @member_origin, @member_collection_id, @member_revision_id,
     @member_key_kind, @member_key_value, @action, @native_operation_id, @native_attempt_id, @native_origin,
     @native_target_fingerprint, @native_context_fingerprint, @native_recipe_fingerprint,
     @checkpoint, @reported_status, @durability);";
				AddChildParameters(command, collectionOperationId, child);
				command.ExecuteNonQuery();
			}
		}

		private static void UpdateChild(SQLiteConnection connection, SQLiteTransaction transaction, Guid collectionOperationId,
			CollectionNativeChildOperation child)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
UPDATE native_operation_children
SET checkpoint=@checkpoint,
    reported_status=@reported_status,
    durability=@durability
WHERE collection_operation_id=@collection_operation_id AND sequence=@sequence;";
				AddChildParameters(command, collectionOperationId, child);
				if (command.ExecuteNonQuery() != 1)
					throw new CollectionsStoreSchemaException("The Collection native child disappeared while its checkpoint was being updated.");
			}
		}

		private static CollectionOperation ReadOperation(SQLiteConnection connection, SQLiteTransaction transaction, Guid operationId)
		{
			int rawKind;
			int rawOrigin;
			string collectionId;
			string targetFingerprint;
			string revisionId;
			string planId;
			int? planVersion;
			long checkpointSequence;
			int rawPhase;
			int rawResult;

			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT kind, origin, collection_id, target_fingerprint, revision_id,
       plan_id, plan_version, checkpoint_sequence, phase, result_state
FROM collection_operations
WHERE operation_id=@operation_id;";
				command.Parameters.AddWithValue("@operation_id", operationId.ToString("D"));
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					if (!reader.Read())
						return null;

					rawKind = reader.GetInt32(0);
					rawOrigin = reader.GetInt32(1);
					collectionId = reader.GetString(2);
					targetFingerprint = reader.GetString(3);
					revisionId = ReadNullableString(reader, 4);
					planId = ReadNullableString(reader, 5);
					planVersion = ReadNullableInt32(reader, 6);
					checkpointSequence = reader.GetInt64(7);
					rawPhase = reader.GetInt32(8);
					rawResult = reader.GetInt32(9);
				}
			}

			try
			{
				CollectionOperationKind kind = (CollectionOperationKind)rawKind;
				if (!Enum.IsDefined(typeof(CollectionOperationKind), kind) || kind == CollectionOperationKind.Unknown)
					throw new CollectionsStoreSchemaException("A persisted Collection operation has an invalid kind.");
				CollectionIdentity collection = ReadCollectionIdentity(rawOrigin, collectionId);
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint(targetFingerprint);
				CollectionRevisionIdentity revision = revisionId == null
					? null
					: ReadPersistedRevisionIdentity(connection, transaction, collection, revisionId);
				CollectionPlanIdentity planIdentity = ReadPlanIdentity(planId, planVersion);
				CollectionOperationPhase phase = (CollectionOperationPhase)rawPhase;
				CollectionOperationResultState resultState = (CollectionOperationResultState)rawResult;
				if (!Enum.IsDefined(typeof(CollectionOperationPhase), phase) || phase == CollectionOperationPhase.Unknown)
					throw new CollectionsStoreSchemaException("A persisted Collection operation has an invalid phase.");
				if (!Enum.IsDefined(typeof(CollectionOperationResultState), resultState))
					throw new CollectionsStoreSchemaException("A persisted Collection operation has an invalid result state.");

				IReadOnlyList<CollectionNativeChildOperation> children = ReadChildren(connection, transaction, operationId);
				CollectionOperation operation = new CollectionOperation(CollectionOperationIdentity.From(operationId), kind,
					collection, target, revision, planIdentity, checkpointSequence, phase, resultState, children);
				if (planIdentity != null)
					RequirePersistedPlan(connection, transaction, operation, true);
				return operation;
			}
			catch (CollectionsStoreSchemaException)
			{
				throw;
			}
			catch (Exception ex) when (ex is ArgumentException || ex is ArgumentOutOfRangeException)
			{
				throw new CollectionsStoreSchemaException("A persisted Collection operation is inconsistent with the Collection domain model.", ex);
			}
		}

		private static IReadOnlyList<CollectionNativeChildOperation> ReadChildren(SQLiteConnection connection,
			SQLiteTransaction transaction, Guid operationId)
		{
			var children = new List<CollectionNativeChildOperation>();
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT nc.sequence,
       nc.member_origin, nc.member_collection_id, nc.member_revision_id,
       cr.revision_id, cr.nexus_revision_number,
       nc.member_key_kind, nc.member_key_value, nc.action,
       nc.native_operation_id, nc.native_attempt_id, nc.native_origin,
       nc.native_target_fingerprint, nc.native_context_fingerprint, nc.native_recipe_fingerprint,
       nc.checkpoint, nc.reported_status, nc.durability
FROM native_operation_children nc
LEFT JOIN collection_revisions cr
  ON cr.origin=nc.member_origin
 AND cr.collection_id=nc.member_collection_id
 AND cr.revision_id=nc.member_revision_id
WHERE nc.collection_operation_id=@operation_id
ORDER BY nc.sequence;";
				command.Parameters.AddWithValue("@operation_id", operationId.ToString("D"));
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					while (reader.Read())
						children.Add(ReadChild(reader));
				}
			}
			return children;
		}

		private static CollectionNativeChildOperation ReadChild(SQLiteDataReader reader)
		{
			try
			{
				int sequence = reader.GetInt32(0);
				CollectionIdentity memberCollection = ReadCollectionIdentity(reader.GetInt32(1), reader.GetString(2));
				string memberRevisionId = reader.GetString(3);
				if (reader.IsDBNull(4))
					throw new CollectionsStoreSchemaException("A persisted Collection native child references a missing member revision.");
				long? nexusRevisionNumber = ReadNullableInt64(reader, 5);
				CollectionRevisionIdentity memberRevision = ReadRevisionIdentity(memberCollection, memberRevisionId, nexusRevisionNumber);
				CollectionMemberKey memberKey = ReadMemberKey(reader.GetInt32(6), reader.GetString(7));
				CollectionNativeChildAction action = (CollectionNativeChildAction)reader.GetInt32(8);
				if (!Enum.IsDefined(typeof(CollectionNativeChildAction), action) || action == CollectionNativeChildAction.Unknown)
					throw new CollectionsStoreSchemaException("A persisted Collection native child has an invalid action.");

				Guid nativeOperationId = ReadCanonicalGuid(reader.GetString(9), "Native operation");
				Guid nativeAttemptId = ReadCanonicalGuid(reader.GetString(10), "Native operation attempt");
				ModOperationOrigin nativeOrigin = (ModOperationOrigin)reader.GetInt32(11);
				if (!Enum.IsDefined(typeof(ModOperationOrigin), nativeOrigin) || nativeOrigin == ModOperationOrigin.Unknown)
					throw new CollectionsStoreSchemaException("A persisted Collection native child has an invalid native origin.");
				string nativeTarget = reader.GetString(12);
				ModInstallContext installContext = ParseInstallContext(reader.GetString(13));
				string recipeFingerprint = reader.GetString(14);
				if (recipeFingerprint.Length == 0)
					recipeFingerprint = null;
				var nativeFingerprint = new ModOperationFingerprint(nativeTarget, installContext, recipeFingerprint);
				var nativeIdentity = new ModOperationIdentity(nativeOperationId, nativeAttemptId, nativeOrigin, nativeFingerprint);

				CollectionNativeChildCheckpoint checkpoint = (CollectionNativeChildCheckpoint)reader.GetInt32(15);
				if (!Enum.IsDefined(typeof(CollectionNativeChildCheckpoint), checkpoint) || checkpoint == CollectionNativeChildCheckpoint.Unknown)
					throw new CollectionsStoreSchemaException("A persisted Collection native child has an invalid checkpoint.");

				ModOperationResult result = ReadNativeResult(reader, nativeIdentity, 16, 17);
				var member = new CollectionOperationMemberReference(memberRevision, memberKey);
				return new CollectionNativeChildOperation(sequence, member, action, nativeIdentity, checkpoint, result);
			}
			catch (CollectionsStoreSchemaException)
			{
				throw;
			}
			catch (Exception ex) when (ex is ArgumentException || ex is ArgumentOutOfRangeException || ex is FormatException)
			{
				throw new CollectionsStoreSchemaException("A persisted Collection native child is inconsistent with the Collection domain model.", ex);
			}
		}

		private static ModOperationResult ReadNativeResult(SQLiteDataReader reader, ModOperationIdentity identity,
			int statusOrdinal, int durabilityOrdinal)
		{
			bool statusMissing = reader.IsDBNull(statusOrdinal);
			bool durabilityMissing = reader.IsDBNull(durabilityOrdinal);
			if (statusMissing != durabilityMissing)
				throw new CollectionsStoreSchemaException("A persisted Collection native child has an incomplete terminal result.");
			if (statusMissing)
				return null;

			ModOperationReportedStatus status = (ModOperationReportedStatus)reader.GetInt32(statusOrdinal);
			ModOperationDurability durability = (ModOperationDurability)reader.GetInt32(durabilityOrdinal);
			if (!Enum.IsDefined(typeof(ModOperationReportedStatus), status) || status == ModOperationReportedStatus.Unknown)
				throw new CollectionsStoreSchemaException("A persisted Collection native child has an invalid reported status.");
			if (!Enum.IsDefined(typeof(ModOperationDurability), durability))
				throw new CollectionsStoreSchemaException("A persisted Collection native child has an invalid durability state.");
			return new ModOperationResult(identity, status, durability, null);
		}

		private static List<Guid> ReadOperationIds(SQLiteConnection connection, SQLiteTransaction transaction,
			CollectionTargetIdentity target)
		{
			var operationIds = new List<Guid>();
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = target == null
					? @"SELECT operation_id FROM collection_operations WHERE phase<>@completed ORDER BY operation_id;"
					: @"SELECT operation_id FROM collection_operations WHERE phase<>@completed AND target_fingerprint=@target_fingerprint ORDER BY operation_id;";
				command.Parameters.AddWithValue("@completed", (int)CollectionOperationPhase.Completed);
				if (target != null)
					command.Parameters.AddWithValue("@target_fingerprint", target.Fingerprint);
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					while (reader.Read())
						operationIds.Add(ReadCanonicalGuid(reader.GetString(0), "Collection operation"));
				}
			}
			return operationIds;
		}

		private static void ValidateOperationProgression(CollectionOperation existing, CollectionOperation incoming)
		{
			if (existing.Kind != incoming.Kind || !existing.Collection.Equals(incoming.Collection) || !existing.Target.Equals(incoming.Target))
				throw new InvalidOperationException("A persisted Collection operation identity cannot be rebound to another kind, Collection or target.");
			if (incoming.CheckpointSequence < existing.CheckpointSequence)
				throw new InvalidOperationException("A Collection operation checkpoint sequence cannot move backwards.");

			if (existing.Revision != null)
			{
				if (incoming.Revision == null || !existing.Revision.Equals(incoming.Revision))
					throw new InvalidOperationException("A resolved Collection operation revision cannot be removed or rebound.");
			}

			if (existing.PlanIdentity != null && incoming.PlanIdentity == null)
				throw new InvalidOperationException("A resolved Collection operation plan cannot be removed from the journal.");
			if (existing.PlanIdentity != null && incoming.PlanIdentity != null &&
				!existing.PlanIdentity.Equals(incoming.PlanIdentity) &&
				(existing.HasCrossedNativeBoundary || incoming.HasCrossedNativeBoundary))
				throw new InvalidOperationException("A Collection operation plan cannot change after the native mutation boundary has been crossed.");
		}

		private static void ValidateChildProgression(CollectionNativeChildOperation existing, CollectionNativeChildOperation incoming)
		{
			if (!existing.Member.Equals(incoming.Member) || existing.Action != incoming.Action ||
				existing.NativeOperation.OperationId != incoming.NativeOperation.OperationId ||
				existing.NativeOperation.AttemptId != incoming.NativeOperation.AttemptId ||
				existing.NativeOperation.Origin != incoming.NativeOperation.Origin ||
				!existing.NativeOperation.Fingerprint.Equals(incoming.NativeOperation.Fingerprint))
				throw new InvalidOperationException("A persisted Collection native-child intent cannot be rebound to another member, action or native attempt.");
			if (incoming.Checkpoint < existing.Checkpoint)
				throw new InvalidOperationException("A Collection native-child safety checkpoint cannot move backwards.");

			if (existing.NativeResult == null)
				return;
			if (incoming.NativeResult == null)
				throw new InvalidOperationException("A persisted native terminal result cannot be removed from the Collection journal.");
			if (existing.NativeResult.ReportedStatus != incoming.NativeResult.ReportedStatus)
				throw new InvalidOperationException("A native operation's persisted terminal status cannot be rewritten.");
			if (existing.NativeResult.Durability != ModOperationDurability.Unknown &&
				existing.NativeResult.Durability != incoming.NativeResult.Durability)
				throw new InvalidOperationException("A verified native durability result cannot be rewritten.");
		}

		private static bool SnapshotsEqual(CollectionOperation left, CollectionOperation right)
		{
			if (left.Kind != right.Kind || !left.Collection.Equals(right.Collection) || !left.Target.Equals(right.Target) ||
				!Equals(left.Revision, right.Revision) || !Equals(left.PlanIdentity, right.PlanIdentity) ||
				left.CheckpointSequence != right.CheckpointSequence || left.Phase != right.Phase || left.ResultState != right.ResultState ||
				left.NativeChildren.Count != right.NativeChildren.Count)
				return false;

			for (int i = 0; i < left.NativeChildren.Count; i++)
			{
				if (!ChildSnapshotsEqual(left.NativeChildren[i], right.NativeChildren[i]))
					return false;
			}
			return true;
		}

		private static bool ChildSnapshotsEqual(CollectionNativeChildOperation left, CollectionNativeChildOperation right)
		{
			if (left.Sequence != right.Sequence || !left.Member.Equals(right.Member) || left.Action != right.Action ||
				left.NativeOperation.OperationId != right.NativeOperation.OperationId ||
				left.NativeOperation.AttemptId != right.NativeOperation.AttemptId ||
				left.NativeOperation.Origin != right.NativeOperation.Origin ||
				!left.NativeOperation.Fingerprint.Equals(right.NativeOperation.Fingerprint) || left.Checkpoint != right.Checkpoint)
				return false;
			if (left.NativeResult == null || right.NativeResult == null)
				return left.NativeResult == null && right.NativeResult == null;
			return left.NativeResult.ReportedStatus == right.NativeResult.ReportedStatus &&
				left.NativeResult.Durability == right.NativeResult.Durability;
		}

		private static Dictionary<int, CollectionNativeChildOperation> IndexChildren(
			IEnumerable<CollectionNativeChildOperation> children)
		{
			var result = new Dictionary<int, CollectionNativeChildOperation>();
			foreach (CollectionNativeChildOperation child in children)
				result.Add(child.Sequence, child);
			return result;
		}

		private static void AddOperationParameters(SQLiteCommand command, CollectionOperation operation)
		{
			command.Parameters.AddWithValue("@operation_id", operation.Identity.OperationId.ToString("D"));
			command.Parameters.AddWithValue("@kind", (int)operation.Kind);
			command.Parameters.AddWithValue("@origin", (int)operation.Collection.Origin);
			command.Parameters.AddWithValue("@collection_id", operation.Collection.StableId);
			command.Parameters.AddWithValue("@target_fingerprint", operation.Target.Fingerprint);
			command.Parameters.AddWithValue("@revision_id", DbValue(operation.Revision == null ? null : operation.Revision.StableRevisionId));
			command.Parameters.AddWithValue("@plan_id", DbValue(operation.PlanIdentity == null ? null : operation.PlanIdentity.PlanId.ToString("D")));
			command.Parameters.AddWithValue("@plan_version", DbValue(operation.PlanIdentity == null ? (int?)null : operation.PlanIdentity.Version));
			command.Parameters.AddWithValue("@checkpoint_sequence", operation.CheckpointSequence);
			command.Parameters.AddWithValue("@phase", (int)operation.Phase);
			command.Parameters.AddWithValue("@result_state", (int)operation.ResultState);
		}

		private static void AddChildParameters(SQLiteCommand command, Guid collectionOperationId,
			CollectionNativeChildOperation child)
		{
			command.Parameters.AddWithValue("@collection_operation_id", collectionOperationId.ToString("D"));
			command.Parameters.AddWithValue("@sequence", child.Sequence);
			command.Parameters.AddWithValue("@member_origin", (int)child.Member.Revision.Collection.Origin);
			command.Parameters.AddWithValue("@member_collection_id", child.Member.Revision.Collection.StableId);
			command.Parameters.AddWithValue("@member_revision_id", child.Member.Revision.StableRevisionId);
			command.Parameters.AddWithValue("@member_key_kind", (int)child.Member.MemberKey.Kind);
			command.Parameters.AddWithValue("@member_key_value", child.Member.MemberKey.Value);
			command.Parameters.AddWithValue("@action", (int)child.Action);
			command.Parameters.AddWithValue("@native_operation_id", child.NativeOperation.OperationId.ToString("D"));
			command.Parameters.AddWithValue("@native_attempt_id", child.NativeOperation.AttemptId.ToString("D"));
			command.Parameters.AddWithValue("@native_origin", (int)child.NativeOperation.Origin);
			command.Parameters.AddWithValue("@native_target_fingerprint", child.NativeOperation.Fingerprint.TargetFingerprint);
			command.Parameters.AddWithValue("@native_context_fingerprint", SerializeInstallContext(child.NativeOperation.Fingerprint));
			command.Parameters.AddWithValue("@native_recipe_fingerprint", child.NativeOperation.Fingerprint.RecipeFingerprint ?? String.Empty);
			command.Parameters.AddWithValue("@checkpoint", (int)child.Checkpoint);
			command.Parameters.AddWithValue("@reported_status", DbValue(child.NativeResult == null ? (int?)null : (int)child.NativeResult.ReportedStatus));
			command.Parameters.AddWithValue("@durability", DbValue(child.NativeResult == null ? (int?)null : (int)child.NativeResult.Durability));
		}

		private static void RequirePersistedCollection(SQLiteConnection connection, SQLiteTransaction transaction,
			CollectionIdentity collection)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = "SELECT COUNT(*) FROM collections WHERE origin=@origin AND collection_id=@collection_id;";
				command.Parameters.AddWithValue("@origin", (int)collection.Origin);
				command.Parameters.AddWithValue("@collection_id", collection.StableId);
				if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
					throw new InvalidOperationException("The Collection definition must be persisted before its operation journal.");
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
				command.Parameters.AddWithValue("@origin", (int)revision.Collection.Origin);
				command.Parameters.AddWithValue("@collection_id", revision.Collection.StableId);
				command.Parameters.AddWithValue("@revision_id", revision.StableRevisionId);
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					if (!reader.Read())
						throw new InvalidOperationException("The exact Collection revision must be persisted before it can be referenced by an operation journal.");
					long? persistedNumber = ReadNullableInt64(reader, 0);
					if (persistedNumber != revision.NexusRevisionNumber)
						throw new InvalidOperationException("The supplied Collection revision identity does not match the persisted concrete revision.");
				}
			}
		}

		private static void RequirePersistedPlan(SQLiteConnection connection, SQLiteTransaction transaction,
			CollectionOperation operation, bool persistedSnapshot)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT origin, collection_id, revision_id, target_fingerprint
FROM resolved_plans
WHERE plan_id=@plan_id AND plan_version=@plan_version;";
				command.Parameters.AddWithValue("@plan_id", operation.PlanIdentity.PlanId.ToString("D"));
				command.Parameters.AddWithValue("@plan_version", operation.PlanIdentity.Version);
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					if (!reader.Read())
					{
						if (persistedSnapshot)
							throw new CollectionsStoreSchemaException("A persisted Collection operation references a missing resolved plan.");
						throw new InvalidOperationException("The exact resolved Collection plan must be persisted before an operation can reference it.");
					}
					if (operation.Revision == null || reader.GetInt32(0) != (int)operation.Collection.Origin ||
						!StringComparer.Ordinal.Equals(reader.GetString(1), operation.Collection.StableId) ||
						!StringComparer.Ordinal.Equals(reader.GetString(2), operation.Revision.StableRevisionId) ||
						!StringComparer.Ordinal.Equals(reader.GetString(3), operation.Target.Fingerprint))
					{
						if (persistedSnapshot)
							throw new CollectionsStoreSchemaException("A persisted Collection operation references a resolved plan for another Collection, revision or target.");
						throw new InvalidOperationException("The resolved Collection plan does not match the operation's Collection, revision and target.");
					}
				}
			}
		}

		private static CollectionRevisionIdentity ReadPersistedRevisionIdentity(SQLiteConnection connection,
			SQLiteTransaction transaction, CollectionIdentity collection, string revisionId)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT nexus_revision_number
FROM collection_revisions
WHERE origin=@origin AND collection_id=@collection_id AND revision_id=@revision_id;";
				command.Parameters.AddWithValue("@origin", (int)collection.Origin);
				command.Parameters.AddWithValue("@collection_id", collection.StableId);
				command.Parameters.AddWithValue("@revision_id", revisionId);
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					if (!reader.Read())
						throw new CollectionsStoreSchemaException("A persisted Collection operation references a missing revision.");
					return ReadRevisionIdentity(collection, revisionId, ReadNullableInt64(reader, 0));
				}
			}
		}

		private static CollectionIdentity ReadCollectionIdentity(int rawOrigin, string stableId)
		{
			CollectionOrigin origin = (CollectionOrigin)rawOrigin;
			switch (origin)
			{
				case CollectionOrigin.NexusMods:
					return CollectionIdentity.FromNexus(stableId);
				case CollectionOrigin.Local:
					return CollectionIdentity.FromLocal(ReadCanonicalGuid(stableId, "Local Collection"));
				default:
					throw new CollectionsStoreSchemaException("A persisted Collection operation has an unsupported Collection origin.");
			}
		}

		private static CollectionRevisionIdentity ReadRevisionIdentity(CollectionIdentity collection, string revisionId,
			long? nexusRevisionNumber)
		{
			if (collection.Origin == CollectionOrigin.NexusMods)
			{
				if (!nexusRevisionNumber.HasValue || nexusRevisionNumber.Value <= 0)
					throw new CollectionsStoreSchemaException("A persisted Nexus Collection revision is missing its concrete revision number.");
				return CollectionRevisionIdentity.FromNexus(collection, revisionId, nexusRevisionNumber.Value);
			}

			if (nexusRevisionNumber.HasValue)
				throw new CollectionsStoreSchemaException("A persisted Local Collection revision cannot contain a Nexus revision number.");
			return CollectionRevisionIdentity.FromLocal(collection, ReadCanonicalGuid(revisionId, "Local Collection revision"));
		}

		private static CollectionPlanIdentity ReadPlanIdentity(string planId, int? planVersion)
		{
			if ((planId == null && planVersion.HasValue) || (planId != null && !planVersion.HasValue))
				throw new CollectionsStoreSchemaException("A persisted Collection operation has an incomplete resolved-plan identity.");
			if (planId == null)
				return null;
			return CollectionPlanIdentity.From(ReadCanonicalGuid(planId, "Collection plan"), planVersion.Value);
		}

		private static CollectionMemberKey ReadMemberKey(int rawKind, string value)
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
					throw new CollectionsStoreSchemaException("A persisted Collection native child has an unsupported member-key kind.");
			}
		}

		private static string SerializeInstallContext(ModOperationFingerprint fingerprint)
		{
			return ((int)fingerprint.InstallMethod).ToString(CultureInfo.InvariantCulture) + ":" +
				((int)fingerprint.InstallRoot).ToString(CultureInfo.InvariantCulture);
		}

		private static ModInstallContext ParseInstallContext(string value)
		{
			if (String.IsNullOrEmpty(value))
				throw new CollectionsStoreSchemaException("A persisted Collection native child is missing its native install context.");
			string[] parts = value.Split(':');
			int rawMethod;
			int rawRoot;
			if (parts.Length != 2 || !Int32.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out rawMethod) ||
				!Int32.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out rawRoot))
				throw new CollectionsStoreSchemaException("A persisted Collection native child has an invalid native install-context fingerprint.");
			try
			{
				return new ModInstallContext((ModInstallMethod)rawMethod, (ModInstallRoot)rawRoot);
			}
			catch (ArgumentOutOfRangeException ex)
			{
				throw new CollectionsStoreSchemaException("A persisted Collection native child has an unsupported native install context.", ex);
			}
		}

		private static Guid ReadCanonicalGuid(string value, string description)
		{
			Guid parsed;
			if (!Guid.TryParseExact(value, "D", out parsed) || parsed == Guid.Empty ||
				!StringComparer.Ordinal.Equals(value, parsed.ToString("D")))
				throw new CollectionsStoreSchemaException(description + " has an invalid persisted GUID identity.");
			return parsed;
		}

		private static void RequireGuid(Guid value, string parameterName)
		{
			if (value == Guid.Empty)
				throw new ArgumentException("A non-empty identifier is required.", parameterName);
		}

		private static string ReadNullableString(SQLiteDataReader reader, int ordinal)
		{
			return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
		}

		private static int? ReadNullableInt32(SQLiteDataReader reader, int ordinal)
		{
			return reader.IsDBNull(ordinal) ? (int?)null : reader.GetInt32(ordinal);
		}

		private static long? ReadNullableInt64(SQLiteDataReader reader, int ordinal)
		{
			return reader.IsDBNull(ordinal) ? (long?)null : reader.GetInt64(ordinal);
		}

		private static object DbValue(object value)
		{
			return value ?? DBNull.Value;
		}
	}
}
