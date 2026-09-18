using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using Nexus.Client.BackgroundTasks;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>
	/// Persists Collection acquisition identity and restart checkpoints without retaining temporary authorization URLs.
	/// </summary>
	public sealed class CollectionsAcquisitionStore
	{
		private readonly CollectionsStore _store;

		/// <summary>Creates an acquisition persistence facade over an existing Collections store.</summary>
		public CollectionsAcquisitionStore(CollectionsStore store)
		{
			_store = store ?? throw new ArgumentNullException(nameof(store));
		}

		/// <summary>Persists a request that is waiting for user-mediated acquisition input.</summary>
		public void TrackPending(CollectionAcquisitionRequest request, CollectionAcquisitionPersistenceMode mode)
		{
			ValidateMode(mode);
			Guid? queueOperationId = null;
			CollectionAcquisitionRecord existing = request == null ? null : Get(request.RequestId);
			if (existing != null)
			{
				if (!existing.Matches(request))
					throw new InvalidOperationException("An acquisition request identifier cannot be rebound to different durable Collection acquisition intent.");
				if (existing.State == CollectionAcquisitionPersistenceState.Verified)
					return;
				if (existing.State == CollectionAcquisitionPersistenceState.Queued ||
					existing.State == CollectionAcquisitionPersistenceState.PendingUserAction)
					queueOperationId = existing.QueueOperationId;
			}
			Upsert(request, mode, CollectionAcquisitionPersistenceState.PendingUserAction, queueOperationId, null);
		}

		/// <summary>Persists correlation between one Collection consumer and an existing native AddMod producer.</summary>
		public void TrackQueued(CollectionAcquisitionRequest request, Guid queueOperationId, CollectionAcquisitionPersistenceMode mode)
		{
			if (queueOperationId == Guid.Empty)
				throw new ArgumentException("A non-empty AddMod queue-operation identifier is required.", nameof(queueOperationId));
			ValidateMode(mode);
			Upsert(request, mode, CollectionAcquisitionPersistenceState.Queued, queueOperationId, null);
		}

		/// <summary>Updates all Collection consumers attached to one native producer after a terminal producer notification.</summary>
		public void MarkProducerState(Guid queueOperationId, TaskStatus status)
		{
			if (queueOperationId == Guid.Empty)
				throw new ArgumentException("A non-empty AddMod queue-operation identifier is required.", nameof(queueOperationId));
			CollectionAcquisitionPersistenceState state;
			switch (status)
			{
				case TaskStatus.Complete:
					state = CollectionAcquisitionPersistenceState.ProducerCompletedUnverified;
					break;
				case TaskStatus.Cancelled:
					state = CollectionAcquisitionPersistenceState.Cancelled;
					break;
				case TaskStatus.Error:
					state = CollectionAcquisitionPersistenceState.Failed;
					break;
				default:
					return;
			}
			_store.ExecuteWrite((connection, transaction) =>
			{
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"UPDATE collection_acquisition_requests
SET state=@state, updated_utc=@updated_utc
WHERE queue_operation_id=@queue_operation_id AND state=@queued;";
					command.Parameters.AddWithValue("@state", (int)state);
					command.Parameters.AddWithValue("@updated_utc", UtcNowText());
					command.Parameters.AddWithValue("@queue_operation_id", queueOperationId.ToString("D"));
					command.Parameters.AddWithValue("@queued", (int)CollectionAcquisitionPersistenceState.Queued);
					command.ExecuteNonQuery();
				}
			});
		}

		/// <summary>Marks one consumer as cancelled without affecting other consumers of the same native producer.</summary>
		public void MarkCancelled(Guid requestId)
		{
			UpdateRequestState(requestId, CollectionAcquisitionPersistenceState.Cancelled);
		}

		/// <summary>Marks exact immutable retained content as the verified result for one request.</summary>
		public void MarkVerified(CollectionAcquisitionRequest request, string artifactId)
		{
			artifactId = CollectionIdentityValidation.RequireOpaqueToken(artifactId, nameof(artifactId));
			CollectionAcquisitionRecord existing = Get(request.RequestId);
			CollectionAcquisitionPersistenceMode mode = existing == null
				? CollectionAcquisitionPersistenceMode.VerifiedReuse
				: existing.Mode;
			Upsert(request, mode, CollectionAcquisitionPersistenceState.Verified,
				existing == null ? (Guid?)null : existing.QueueOperationId, artifactId);
		}

		/// <summary>Loads one acquisition record by request identity, or <c>null</c> when it has never been persisted.</summary>
		public CollectionAcquisitionRecord Get(Guid requestId)
		{
			if (requestId == Guid.Empty)
				throw new ArgumentException("A non-empty acquisition request identifier is required.", nameof(requestId));
			return _store.ExecuteRead((connection, transaction) => ReadOne(connection, transaction, requestId));
		}

		/// <summary>
		/// Returns the previously persisted native producer identity when the request is still pending or queued and may resume it.
		/// </summary>
		public Guid? GetReusableQueueOperationId(CollectionAcquisitionRequest request)
		{
			if (request == null)
				throw new ArgumentNullException(nameof(request));
			CollectionAcquisitionRecord existing = Get(request.RequestId);
			if (existing == null)
				return null;
			if (!existing.Matches(request))
				throw new InvalidOperationException("An acquisition request identifier cannot be rebound to different durable Collection acquisition intent.");
			if (existing.State != CollectionAcquisitionPersistenceState.Queued &&
				existing.State != CollectionAcquisitionPersistenceState.PendingUserAction)
				return null;
			return existing.QueueOperationId;
		}

		/// <summary>Loads acquisition records that may still require restart reconciliation.</summary>
		public IReadOnlyList<CollectionAcquisitionRecord> GetUnresolved()
		{
			return _store.ExecuteRead((connection, transaction) =>
			{
				var result = new List<CollectionAcquisitionRecord>();
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = SelectColumns + @"
WHERE state NOT IN (@verified, @cancelled)
ORDER BY updated_utc, request_id;";
					command.Parameters.AddWithValue("@verified", (int)CollectionAcquisitionPersistenceState.Verified);
					command.Parameters.AddWithValue("@cancelled", (int)CollectionAcquisitionPersistenceState.Cancelled);
					using (SQLiteDataReader reader = command.ExecuteReader())
					{
						while (reader.Read())
							result.Add(ReadRecord(reader));
					}
				}
				return result;
			});
		}

		private void Upsert(CollectionAcquisitionRequest request, CollectionAcquisitionPersistenceMode mode,
			CollectionAcquisitionPersistenceState state, Guid? queueOperationId, string verifiedArtifactId)
		{
			if (request == null)
				throw new ArgumentNullException(nameof(request));
			ValidateMode(mode);
			if (!Enum.IsDefined(typeof(CollectionAcquisitionPersistenceState), state) || state == CollectionAcquisitionPersistenceState.Unknown)
				throw new ArgumentOutOfRangeException(nameof(state));

			_store.ExecuteWrite((connection, transaction) =>
			{
				CollectionAcquisitionRecord existing = ReadOne(connection, transaction, request.RequestId);
				if (existing != null && !existing.Matches(request))
					throw new InvalidOperationException("An acquisition request identifier cannot be rebound to different durable Collection acquisition intent.");

				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"
INSERT OR REPLACE INTO collection_acquisition_requests
    (request_id, plan_id, plan_version, revision_identity, target_fingerprint,
     member_key_kind, member_key_value, requirement, artifact_scheme, artifact_stable_id,
     expected_content_hash, recipe_fingerprint, mode, state, queue_operation_id, verified_artifact_id, updated_utc)
VALUES
    (@request_id, @plan_id, @plan_version, @revision_identity, @target_fingerprint,
     @member_key_kind, @member_key_value, @requirement, @artifact_scheme, @artifact_stable_id,
     @expected_content_hash, @recipe_fingerprint, @mode, @state, @queue_operation_id, @verified_artifact_id, @updated_utc);";
					BindIdentity(command, request);
					command.Parameters.AddWithValue("@mode", (int)mode);
					command.Parameters.AddWithValue("@state", (int)state);
					command.Parameters.AddWithValue("@queue_operation_id", queueOperationId.HasValue ? (object)queueOperationId.Value.ToString("D") : DBNull.Value);
					command.Parameters.AddWithValue("@verified_artifact_id", verifiedArtifactId == null ? (object)DBNull.Value : verifiedArtifactId);
					command.Parameters.AddWithValue("@updated_utc", UtcNowText());
					command.ExecuteNonQuery();
				}
			});
		}

		private void UpdateRequestState(Guid requestId, CollectionAcquisitionPersistenceState state)
		{
			if (requestId == Guid.Empty)
				throw new ArgumentException("A non-empty acquisition request identifier is required.", nameof(requestId));
			_store.ExecuteWrite((connection, transaction) =>
			{
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = "UPDATE collection_acquisition_requests SET state=@state, updated_utc=@updated_utc WHERE request_id=@request_id AND state<>@verified;";
					command.Parameters.AddWithValue("@state", (int)state);
					command.Parameters.AddWithValue("@updated_utc", UtcNowText());
					command.Parameters.AddWithValue("@request_id", requestId.ToString("D"));
					command.Parameters.AddWithValue("@verified", (int)CollectionAcquisitionPersistenceState.Verified);
					command.ExecuteNonQuery();
				}
			});
		}

		private static void BindIdentity(SQLiteCommand command, CollectionAcquisitionRequest request)
		{
			command.Parameters.AddWithValue("@request_id", request.RequestId.ToString("D"));
			command.Parameters.AddWithValue("@plan_id", request.PlanIdentity.PlanId.ToString("D"));
			command.Parameters.AddWithValue("@plan_version", request.PlanIdentity.Version);
			command.Parameters.AddWithValue("@revision_identity", request.Revision.ToString());
			command.Parameters.AddWithValue("@target_fingerprint", request.Target.Fingerprint);
			command.Parameters.AddWithValue("@member_key_kind", (int)request.MemberKey.Kind);
			command.Parameters.AddWithValue("@member_key_value", request.MemberKey.Value);
			command.Parameters.AddWithValue("@requirement", (int)request.Requirement);
			command.Parameters.AddWithValue("@artifact_scheme", request.SelectedArtifact.Scheme);
			command.Parameters.AddWithValue("@artifact_stable_id", request.SelectedArtifact.StableId);
			command.Parameters.AddWithValue("@expected_content_hash", request.SelectedArtifact.ExpectedContentHash == null
				? (object)DBNull.Value
				: request.SelectedArtifact.ExpectedContentHash.ToString());
			command.Parameters.AddWithValue("@recipe_fingerprint", request.RecipeIdentity.Fingerprint);
		}

		private static CollectionAcquisitionRecord ReadOne(SQLiteConnection connection, SQLiteTransaction transaction, Guid requestId)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = SelectColumns + " WHERE request_id=@request_id;";
				command.Parameters.AddWithValue("@request_id", requestId.ToString("D"));
				using (SQLiteDataReader reader = command.ExecuteReader())
					return reader.Read() ? ReadRecord(reader) : null;
			}
		}

		private static CollectionAcquisitionRecord ReadRecord(SQLiteDataReader reader)
		{
			Guid requestId = ParseGuid(reader.GetString(0), "request_id");
			Guid planId = ParseGuid(reader.GetString(1), "plan_id");
			Guid? queueOperationId = reader.IsDBNull(14) ? (Guid?)null : ParseGuid(reader.GetString(14), "queue_operation_id");
			DateTime updatedUtc;
			if (!DateTime.TryParse(reader.GetString(16), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out updatedUtc))
				throw new CollectionsStoreSchemaException("Collections acquisition record has an invalid updated_utc value.");
			CollectionAcquisitionPersistenceMode mode = (CollectionAcquisitionPersistenceMode)reader.GetInt32(12);
			CollectionAcquisitionPersistenceState state = (CollectionAcquisitionPersistenceState)reader.GetInt32(13);
			if (!IsValidMode(mode))
				throw new CollectionsStoreSchemaException("Collections acquisition record has an invalid acquisition mode.");
			if (!Enum.IsDefined(typeof(CollectionAcquisitionPersistenceState), state) || state == CollectionAcquisitionPersistenceState.Unknown)
				throw new CollectionsStoreSchemaException("Collections acquisition record has an invalid durable state.");
			return new CollectionAcquisitionRecord(requestId, planId, reader.GetInt32(2), reader.GetString(3), reader.GetString(4),
				reader.GetInt32(5), reader.GetString(6), reader.GetInt32(7), reader.GetString(8), reader.GetString(9),
				reader.IsDBNull(10) ? null : reader.GetString(10), reader.GetString(11), mode, state, queueOperationId,
				reader.IsDBNull(15) ? null : reader.GetString(15), updatedUtc.ToUniversalTime());
		}

		private static Guid ParseGuid(string value, string columnName)
		{
			Guid parsed;
			if (!Guid.TryParse(value, out parsed) || parsed == Guid.Empty)
				throw new CollectionsStoreSchemaException("Collections acquisition record has an invalid " + columnName + ".");
			return parsed;
		}

		private static void ValidateMode(CollectionAcquisitionPersistenceMode mode)
		{
			if (!IsValidMode(mode))
				throw new ArgumentOutOfRangeException(nameof(mode));
		}

		private static bool IsValidMode(CollectionAcquisitionPersistenceMode mode)
		{
			return Enum.IsDefined(typeof(CollectionAcquisitionPersistenceMode), mode) &&
				mode != CollectionAcquisitionPersistenceMode.Unknown;
		}

		private static string UtcNowText()
		{
			return DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
		}

		private const string SelectColumns = @"SELECT request_id, plan_id, plan_version, revision_identity, target_fingerprint,
       member_key_kind, member_key_value, requirement, artifact_scheme, artifact_stable_id,
       expected_content_hash, recipe_fingerprint, mode, state, queue_operation_id, verified_artifact_id, updated_utc
FROM collection_acquisition_requests";
	}
}
