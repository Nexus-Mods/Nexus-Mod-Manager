using System;
using System.Data.SQLite;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>
	/// Persists immutable resolved Collection plan payloads referenced by the operation journal.
	/// </summary>
	/// <remarks>
	/// The store owns feature metadata only. A persisted plan is a durable planning snapshot, never evidence that native work ran.
	/// </remarks>
	public sealed class CollectionsResolvedPlanStore
	{
		private readonly CollectionsStore _store;

		/// <summary>
		/// Creates a resolved-plan store over an existing Collections feature store.
		/// </summary>
		public CollectionsResolvedPlanStore(CollectionsStore store)
		{
			_store = store ?? throw new ArgumentNullException(nameof(store));
		}

		/// <summary>
		/// Persists one immutable resolved plan snapshot.
		/// </summary>
		/// <remarks>
		/// Re-saving the exact same plan payload is idempotent. Reusing a plan identity/version for different metadata or payload is rejected.
		/// </remarks>
		public void SavePlan(ResolvedCollectionPlan plan, string payloadFormat, byte[] payload)
		{
			if (plan == null)
				throw new ArgumentNullException(nameof(plan));
			payloadFormat = CollectionIdentityValidation.RequireOpaqueToken(payloadFormat, nameof(payloadFormat));
			if (payload == null)
				throw new ArgumentNullException(nameof(payload));
			if (payload.Length == 0)
				throw new ArgumentException("A resolved Collection plan payload cannot be empty.", nameof(payload));

			_store.ExecuteWrite((connection, transaction) =>
			{
				RequirePersistedRevision(connection, transaction, plan.Revision);
				CollectionResolvedPlanRecord existing = ReadPlan(connection, transaction, plan.Identity);
				if (existing != null)
				{
					if (!Matches(existing, plan, payloadFormat, payload))
						throw new InvalidOperationException("A persisted resolved Collection plan is immutable and cannot be replaced with different metadata or payload.");
					return;
				}

				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"
INSERT INTO resolved_plans
    (plan_id, plan_version, origin, collection_id, revision_id, target_fingerprint,
     policy_kind, current_state_format_version, current_state_fingerprint, payload_format, payload)
VALUES
    (@plan_id, @plan_version, @origin, @collection_id, @revision_id, @target_fingerprint,
     @policy_kind, @current_state_format_version, @current_state_fingerprint, @payload_format, @payload);";
					AddPlanParameters(command, plan, payloadFormat, payload);
					command.ExecuteNonQuery();
				}
			});
		}

		/// <summary>
		/// Loads one exact resolved plan snapshot, or <c>null</c> when it is not persisted.
		/// </summary>
		public CollectionResolvedPlanRecord GetPlan(CollectionPlanIdentity identity)
		{
			if (identity == null)
				throw new ArgumentNullException(nameof(identity));

			return _store.ExecuteRead((connection, transaction) => ReadPlan(connection, transaction, identity));
		}

		private static CollectionResolvedPlanRecord ReadPlan(SQLiteConnection connection, SQLiteTransaction transaction,
			CollectionPlanIdentity identity)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT rp.origin, rp.collection_id, rp.revision_id, cr.nexus_revision_number,
       rp.target_fingerprint, rp.policy_kind, rp.current_state_format_version,
       rp.current_state_fingerprint, rp.payload_format, rp.payload
FROM resolved_plans rp
JOIN collection_revisions cr
  ON cr.origin=rp.origin AND cr.collection_id=rp.collection_id AND cr.revision_id=rp.revision_id
WHERE rp.plan_id=@plan_id AND rp.plan_version=@plan_version;";
				command.Parameters.AddWithValue("@plan_id", identity.PlanId.ToString("D"));
				command.Parameters.AddWithValue("@plan_version", identity.Version);
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					if (!reader.Read())
						return null;

					try
					{
						CollectionIdentity collection = ReadCollectionIdentity(reader.GetInt32(0), reader.GetString(1));
						long? revisionNumber = reader.IsDBNull(3) ? (long?)null : reader.GetInt64(3);
						CollectionRevisionIdentity revision = ReadRevisionIdentity(collection, reader.GetString(2), revisionNumber);
						CollectionExecutionPolicyKind policyKind = (CollectionExecutionPolicyKind)reader.GetInt32(5);
						if (!Enum.IsDefined(typeof(CollectionExecutionPolicyKind), policyKind) || policyKind == CollectionExecutionPolicyKind.Unknown)
							throw new CollectionsStoreSchemaException("A persisted resolved Collection plan has an invalid execution policy.");

						byte[] payload = reader.GetValue(9) as byte[];
						if (payload == null)
							throw new ArgumentException("The persisted resolved Collection plan payload is not a binary value.");
						return new CollectionResolvedPlanRecord(identity, revision,
							CollectionTargetIdentity.FromFingerprint(reader.GetString(4)), policyKind,
							new CollectionCurrentStateFingerprint(reader.GetString(6), reader.GetString(7)),
							reader.GetString(8), payload);
					}
					catch (CollectionsStoreSchemaException)
					{
						throw;
					}
					catch (Exception ex) when (ex is ArgumentException || ex is ArgumentOutOfRangeException || ex is InvalidCastException)
					{
						throw new CollectionsStoreSchemaException("A persisted resolved Collection plan contains invalid immutable metadata.", ex);
					}
				}
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
						throw new InvalidOperationException("The exact Collection revision must be persisted before its resolved plan.");
					long? persistedNumber = reader.IsDBNull(0) ? (long?)null : reader.GetInt64(0);
					if (persistedNumber != revision.NexusRevisionNumber)
						throw new InvalidOperationException("The supplied Collection revision identity does not match the persisted concrete revision.");
				}
			}
		}

		private static bool Matches(CollectionResolvedPlanRecord existing, ResolvedCollectionPlan plan,
			string payloadFormat, byte[] payload)
		{
			if (!existing.Revision.Equals(plan.Revision) || !existing.Target.Equals(plan.Target) ||
				existing.PolicyKind != plan.Policy.Kind || !existing.CurrentStateFingerprint.Equals(plan.CurrentStateFingerprint) ||
				!StringComparer.Ordinal.Equals(existing.PayloadFormat, payloadFormat))
				return false;

			byte[] existingPayload = existing.UnsafePayload;
			if (existingPayload.Length != payload.Length)
				return false;
			for (int i = 0; i < payload.Length; i++)
				if (existingPayload[i] != payload[i])
					return false;
			return true;
		}

		private static void AddPlanParameters(SQLiteCommand command, ResolvedCollectionPlan plan,
			string payloadFormat, byte[] payload)
		{
			command.Parameters.AddWithValue("@plan_id", plan.Identity.PlanId.ToString("D"));
			command.Parameters.AddWithValue("@plan_version", plan.Identity.Version);
			command.Parameters.AddWithValue("@origin", (int)plan.Revision.Collection.Origin);
			command.Parameters.AddWithValue("@collection_id", plan.Revision.Collection.StableId);
			command.Parameters.AddWithValue("@revision_id", plan.Revision.StableRevisionId);
			command.Parameters.AddWithValue("@target_fingerprint", plan.Target.Fingerprint);
			command.Parameters.AddWithValue("@policy_kind", (int)plan.Policy.Kind);
			command.Parameters.AddWithValue("@current_state_format_version", plan.CurrentStateFingerprint.FormatVersion);
			command.Parameters.AddWithValue("@current_state_fingerprint", plan.CurrentStateFingerprint.Value);
			command.Parameters.AddWithValue("@payload_format", payloadFormat);
			command.Parameters.AddWithValue("@payload", payload);
		}

		private static CollectionIdentity ReadCollectionIdentity(int rawOrigin, string stableId)
		{
			CollectionOrigin origin = (CollectionOrigin)rawOrigin;
			switch (origin)
			{
				case CollectionOrigin.NexusMods:
					return CollectionIdentity.FromNexus(stableId);
				case CollectionOrigin.Local:
					Guid localId;
					if (!Guid.TryParse(stableId, out localId) || localId == Guid.Empty)
						throw new CollectionsStoreSchemaException("A persisted Local Collection plan has an invalid Collection identity.");
					return CollectionIdentity.FromLocal(localId);
				default:
					throw new CollectionsStoreSchemaException("A persisted resolved Collection plan has an unsupported Collection origin.");
			}
		}

		private static CollectionRevisionIdentity ReadRevisionIdentity(CollectionIdentity collection,
			string stableRevisionId, long? nexusRevisionNumber)
		{
			if (collection.Origin == CollectionOrigin.NexusMods)
			{
				if (!nexusRevisionNumber.HasValue || nexusRevisionNumber.Value <= 0)
					throw new CollectionsStoreSchemaException("A persisted Nexus Collection plan has an invalid revision number.");
				return CollectionRevisionIdentity.FromNexus(collection, stableRevisionId, nexusRevisionNumber.Value);
			}

			Guid localRevisionId;
			if (nexusRevisionNumber.HasValue || !Guid.TryParse(stableRevisionId, out localRevisionId) || localRevisionId == Guid.Empty)
				throw new CollectionsStoreSchemaException("A persisted Local Collection plan has an invalid revision identity.");
			return CollectionRevisionIdentity.FromLocal(collection, localRevisionId);
		}
	}
}
