using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>
	/// Persists sealed Local Collection capture contracts and their durable package-artifact binding.
	/// </summary>
	/// <remarks>
	/// This store owns Collection feature metadata only. Captured native keys remain historical provenance and retained
	/// artifacts remain immutable blobs protected by capture-scoped lifetime references.
	/// </remarks>
	public sealed class CollectionsLocalCaptureStore
	{
		/// <summary>Gets the retained-artifact role used by the versioned durable C7 capture package.</summary>
		public const string PackageReferenceRole = "capture-package/v1";

		private readonly CollectionsStore _store;

		/// <summary>Creates a Local Collection capture store over an existing Collections feature store.</summary>
		public CollectionsLocalCaptureStore(CollectionsStore store)
		{
			_store = store ?? throw new ArgumentNullException(nameof(store));
		}

		/// <summary>
		/// Atomically publishes the Local Collection definition, immutable revision, sealed capture and package binding.
		/// </summary>
		public void Save(CollectionDefinition definition, CollectionRevision revision, LocalCapture capture,
			int packageFormatVersion, string packageArtifactId)
		{
			if (definition == null)
				throw new ArgumentNullException(nameof(definition));
			if (revision == null)
				throw new ArgumentNullException(nameof(revision));
			if (capture == null)
				throw new ArgumentNullException(nameof(capture));
			if (definition.Origin != CollectionOrigin.Local)
				throw new ArgumentException("A saved current setup must publish a Local Collection definition.", nameof(definition));
			if (!definition.Identity.Equals(revision.Collection) || !revision.Identity.Equals(capture.Revision))
				throw new ArgumentException("Definition, revision and capture identities must describe the same Local Collection snapshot.", nameof(capture));
			if (packageFormatVersion <= 0)
				throw new ArgumentOutOfRangeException(nameof(packageFormatVersion));
			packageArtifactId = CollectionIdentityValidation.RequireOpaqueToken(packageArtifactId, nameof(packageArtifactId));

			string captureId = capture.Identity.ToString();
			_store.ExecuteWrite((connection, transaction) =>
			{
				RequireCaptureReference(connection, transaction, captureId, PackageReferenceRole, packageArtifactId);
				foreach (RetainedArtifactReference retained in capture.RetainedArtifacts)
					RequireCaptureReference(connection, transaction, captureId, retained.Role, retained.StableArtifactId);

				CollectionsCatalogStore.SaveDefinition(connection, transaction, definition);
				CollectionsCatalogStore.SaveRevision(connection, transaction, revision);
				if (CaptureExists(connection, transaction, captureId))
					throw new InvalidOperationException("A sealed Local Collection capture with this identity is already persisted.");

				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"
INSERT INTO local_captures
    (capture_id, origin, collection_id, revision_id, source_target_fingerprint,
     state_fingerprint_format_version, state_fingerprint, scope_version, capability)
VALUES
    (@capture_id, @origin, @collection_id, @revision_id, @source_target_fingerprint,
     @state_fingerprint_format_version, @state_fingerprint, @scope_version, @capability);";
					command.Parameters.AddWithValue("@capture_id", captureId);
					command.Parameters.AddWithValue("@origin", (int)capture.Revision.Collection.Origin);
					command.Parameters.AddWithValue("@collection_id", capture.Revision.Collection.StableId);
					command.Parameters.AddWithValue("@revision_id", capture.Revision.StableRevisionId);
					command.Parameters.AddWithValue("@source_target_fingerprint", capture.SourceTarget.Fingerprint);
					command.Parameters.AddWithValue("@state_fingerprint_format_version", capture.CapturedStateFingerprint.FormatVersion);
					command.Parameters.AddWithValue("@state_fingerprint", capture.CapturedStateFingerprint.Value);
					command.Parameters.AddWithValue("@scope_version", capture.Scope.Version);
					command.Parameters.AddWithValue("@capability", (int)capture.Capability);
					command.ExecuteNonQuery();
				}

				foreach (LocalCaptureScopeArea area in capture.Scope.Areas)
				{
					using (SQLiteCommand command = connection.CreateCommand())
					{
						command.Transaction = transaction;
						command.CommandText = "INSERT INTO local_capture_scope_areas (capture_id, area) VALUES (@capture_id, @area);";
						command.Parameters.AddWithValue("@capture_id", captureId);
						command.Parameters.AddWithValue("@area", (int)area);
						command.ExecuteNonQuery();
					}
				}

				foreach (LocalCaptureExclusion exclusion in capture.Exclusions)
				{
					using (SQLiteCommand command = connection.CreateCommand())
					{
						command.Transaction = transaction;
						command.CommandText = @"
INSERT INTO local_capture_exclusions (capture_id, area, code, reason)
VALUES (@capture_id, @area, @code, @reason);";
						command.Parameters.AddWithValue("@capture_id", captureId);
						command.Parameters.AddWithValue("@area", (int)exclusion.Area);
						command.Parameters.AddWithValue("@code", exclusion.Code);
						command.Parameters.AddWithValue("@reason", exclusion.Reason);
						command.ExecuteNonQuery();
					}
				}

				foreach (LocalCaptureNativeRecordMapping mapping in capture.NativeRecordMappings)
				{
					using (SQLiteCommand command = connection.CreateCommand())
					{
						command.Transaction = transaction;
						command.CommandText = @"
INSERT INTO local_capture_native_mappings
    (capture_id, member_key_kind, member_key_value, source_target_fingerprint, native_mod_key)
VALUES
    (@capture_id, @member_key_kind, @member_key_value, @source_target_fingerprint, @native_mod_key);";
						command.Parameters.AddWithValue("@capture_id", captureId);
						command.Parameters.AddWithValue("@member_key_kind", (int)mapping.SnapshotMemberKey.Kind);
						command.Parameters.AddWithValue("@member_key_value", mapping.SnapshotMemberKey.Value);
						command.Parameters.AddWithValue("@source_target_fingerprint", mapping.SourceNativeInstance.Target.Fingerprint);
						command.Parameters.AddWithValue("@native_mod_key", mapping.SourceNativeInstance.NativeModKey);
						command.ExecuteNonQuery();
					}
				}

				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"
INSERT INTO local_capture_packages
    (capture_id, capture_schema_version, capability_version, package_format_version, package_artifact_id)
VALUES
    (@capture_id, @capture_schema_version, @capability_version, @package_format_version, @package_artifact_id);";
					command.Parameters.AddWithValue("@capture_id", captureId);
					command.Parameters.AddWithValue("@capture_schema_version", capture.SchemaVersion);
					command.Parameters.AddWithValue("@capability_version", capture.CapabilityVersion);
					command.Parameters.AddWithValue("@package_format_version", packageFormatVersion);
					command.Parameters.AddWithValue("@package_artifact_id", packageArtifactId);
					command.ExecuteNonQuery();
				}
			});
		}

		/// <summary>Loads one persisted sealed capture contract, or <c>null</c> when no such capture exists.</summary>
		public LocalCapture GetCapture(LocalCaptureIdentity identity)
		{
			if (identity == null)
				throw new ArgumentNullException(nameof(identity));
			string captureId = identity.ToString();
			return _store.ExecuteRead((connection, transaction) => ReadCapture(connection, transaction, captureId));
		}

		/// <summary>Gets the immutable package artifact identity for one persisted capture, or <c>null</c> when absent.</summary>
		public string GetPackageArtifactId(LocalCaptureIdentity identity)
		{
			if (identity == null)
				throw new ArgumentNullException(nameof(identity));
			return _store.ExecuteRead((connection, transaction) =>
			{
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = "SELECT package_artifact_id FROM local_capture_packages WHERE capture_id=@capture_id;";
					command.Parameters.AddWithValue("@capture_id", identity.ToString());
					object value = command.ExecuteScalar();
					return value == null || value == DBNull.Value ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
				}
			});
		}

		private static LocalCapture ReadCapture(SQLiteConnection connection, SQLiteTransaction transaction, string captureId)
		{
			int origin;
			string collectionId;
			string revisionId;
			string targetFingerprint;
			string fingerprintFormat;
			string fingerprintValue;
			int scopeVersion;
			LocalCaptureCapability capability;
			int captureSchemaVersion;
			int capabilityVersion;

			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT c.origin, c.collection_id, c.revision_id, c.source_target_fingerprint,
       c.state_fingerprint_format_version, c.state_fingerprint, c.scope_version, c.capability,
       p.capture_schema_version, p.capability_version
FROM local_captures c
JOIN local_capture_packages p ON p.capture_id=c.capture_id
WHERE c.capture_id=@capture_id;";
				command.Parameters.AddWithValue("@capture_id", captureId);
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					if (!reader.Read())
						return null;
					origin = Convert.ToInt32(reader[0], CultureInfo.InvariantCulture);
					collectionId = Convert.ToString(reader[1], CultureInfo.InvariantCulture);
					revisionId = Convert.ToString(reader[2], CultureInfo.InvariantCulture);
					targetFingerprint = Convert.ToString(reader[3], CultureInfo.InvariantCulture);
					fingerprintFormat = Convert.ToString(reader[4], CultureInfo.InvariantCulture);
					fingerprintValue = Convert.ToString(reader[5], CultureInfo.InvariantCulture);
					scopeVersion = Convert.ToInt32(reader[6], CultureInfo.InvariantCulture);
					capability = (LocalCaptureCapability)Convert.ToInt32(reader[7], CultureInfo.InvariantCulture);
					captureSchemaVersion = Convert.ToInt32(reader[8], CultureInfo.InvariantCulture);
					capabilityVersion = Convert.ToInt32(reader[9], CultureInfo.InvariantCulture);
				}
			}

			if (origin != (int)CollectionOrigin.Local)
				throw new CollectionsStoreSchemaException("A persisted Local Collection capture refers to a non-local Collection identity.");
			Guid localCollectionId;
			Guid localRevisionId;
			Guid parsedCaptureId;
			if (!Guid.TryParse(collectionId, out localCollectionId) || !Guid.TryParse(revisionId, out localRevisionId) ||
				!Guid.TryParse(captureId, out parsedCaptureId))
				throw new CollectionsStoreSchemaException("A persisted Local Collection capture contains an invalid local GUID identity.");

			CollectionIdentity collection = CollectionIdentity.FromLocal(localCollectionId);
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromLocal(collection, localRevisionId);
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint(targetFingerprint);
			List<LocalCaptureScopeArea> areas = ReadScopeAreas(connection, transaction, captureId);
			List<LocalCaptureExclusion> exclusions = ReadExclusions(connection, transaction, captureId);
			List<LocalCaptureNativeRecordMapping> mappings = ReadMappings(connection, transaction, captureId);
			List<RetainedArtifactReference> retained = ReadRetainedArtifacts(connection, transaction, captureId);

			return new LocalCapture(LocalCaptureIdentity.From(parsedCaptureId), revision, target,
				new CollectionCurrentStateFingerprint(fingerprintFormat, fingerprintValue),
				new LocalCaptureScope(scopeVersion, areas), capability, captureSchemaVersion, capabilityVersion,
				retained, exclusions, mappings);
		}

		private static List<LocalCaptureScopeArea> ReadScopeAreas(SQLiteConnection connection, SQLiteTransaction transaction, string captureId)
		{
			var result = new List<LocalCaptureScopeArea>();
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = "SELECT area FROM local_capture_scope_areas WHERE capture_id=@capture_id ORDER BY area;";
				command.Parameters.AddWithValue("@capture_id", captureId);
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					while (reader.Read())
						result.Add((LocalCaptureScopeArea)Convert.ToInt32(reader[0], CultureInfo.InvariantCulture));
				}
			}
			return result;
		}

		private static List<LocalCaptureExclusion> ReadExclusions(SQLiteConnection connection, SQLiteTransaction transaction, string captureId)
		{
			var result = new List<LocalCaptureExclusion>();
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT area, code, reason FROM local_capture_exclusions
WHERE capture_id=@capture_id ORDER BY area, code;";
				command.Parameters.AddWithValue("@capture_id", captureId);
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					while (reader.Read())
						result.Add(new LocalCaptureExclusion((LocalCaptureScopeArea)Convert.ToInt32(reader[0], CultureInfo.InvariantCulture),
							Convert.ToString(reader[1], CultureInfo.InvariantCulture), Convert.ToString(reader[2], CultureInfo.InvariantCulture)));
				}
			}
			return result;
		}

		private static List<LocalCaptureNativeRecordMapping> ReadMappings(SQLiteConnection connection, SQLiteTransaction transaction, string captureId)
		{
			var result = new List<LocalCaptureNativeRecordMapping>();
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT member_key_kind, member_key_value, source_target_fingerprint, native_mod_key
FROM local_capture_native_mappings
WHERE capture_id=@capture_id ORDER BY member_key_kind, member_key_value;";
				command.Parameters.AddWithValue("@capture_id", captureId);
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					while (reader.Read())
					{
						CollectionMemberKey member = ReadMemberKey((CollectionMemberKeyKind)Convert.ToInt32(reader[0], CultureInfo.InvariantCulture),
							Convert.ToString(reader[1], CultureInfo.InvariantCulture));
						CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint(Convert.ToString(reader[2], CultureInfo.InvariantCulture));
						result.Add(new LocalCaptureNativeRecordMapping(member,
							new NativeModInstanceIdentity(target, Convert.ToString(reader[3], CultureInfo.InvariantCulture))));
					}
				}
			}
			return result;
		}

		private static List<RetainedArtifactReference> ReadRetainedArtifacts(SQLiteConnection connection, SQLiteTransaction transaction,
			string captureId)
		{
			var result = new List<RetainedArtifactReference>();
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT r.artifact_id, r.role, a.hash_algorithm, a.hash_value, a.byte_length
FROM retained_artifact_references r
JOIN retained_artifacts a ON a.artifact_id=r.artifact_id
WHERE r.owner_kind=@owner_kind AND r.owner_id=@owner_id AND r.role<>@package_role
ORDER BY r.role;";
				command.Parameters.AddWithValue("@owner_kind", (int)CollectionsRetainedArtifactOwnerKind.Capture);
				command.Parameters.AddWithValue("@owner_id", captureId);
				command.Parameters.AddWithValue("@package_role", PackageReferenceRole);
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					while (reader.Read())
					{
						string algorithm = Convert.ToString(reader[2], CultureInfo.InvariantCulture);
						if (!String.Equals(algorithm, "sha256", StringComparison.OrdinalIgnoreCase))
							throw new CollectionsStoreSchemaException("A Local Collection capture references an unsupported retained-artifact hash algorithm.");
						result.Add(new RetainedArtifactReference(Convert.ToString(reader[0], CultureInfo.InvariantCulture),
							Convert.ToString(reader[1], CultureInfo.InvariantCulture),
							CollectionContentHash.FromSha256(Convert.ToString(reader[3], CultureInfo.InvariantCulture)),
							Convert.ToInt64(reader[4], CultureInfo.InvariantCulture)));
					}
				}
			}
			return result;
		}

		private static CollectionMemberKey ReadMemberKey(CollectionMemberKeyKind kind, string value)
		{
			switch (kind)
			{
				case CollectionMemberKeyKind.ProviderStable:
					return CollectionMemberKey.FromProvider(value);
				case CollectionMemberKeyKind.Local:
					Guid id;
					if (!Guid.TryParse(value, out id))
						throw new CollectionsStoreSchemaException("A persisted Local Collection member key is not a valid GUID.");
					return CollectionMemberKey.FromLocal(id);
				case CollectionMemberKeyKind.ValidatedMatch:
					return CollectionMemberKey.FromValidatedMatch(value);
				default:
					throw new CollectionsStoreSchemaException("A persisted Local Collection capture contains an unsupported member-key kind.");
			}
		}

		private static bool CaptureExists(SQLiteConnection connection, SQLiteTransaction transaction, string captureId)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = "SELECT 1 FROM local_captures WHERE capture_id=@capture_id LIMIT 1;";
				command.Parameters.AddWithValue("@capture_id", captureId);
				return command.ExecuteScalar() != null;
			}
		}

		private static void RequireCaptureReference(SQLiteConnection connection, SQLiteTransaction transaction,
			string captureId, string role, string artifactId)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT COUNT(*)
FROM retained_artifact_references
WHERE owner_kind=@owner_kind AND owner_id=@owner_id AND role=@role AND artifact_id=@artifact_id;";
				command.Parameters.AddWithValue("@owner_kind", (int)CollectionsRetainedArtifactOwnerKind.Capture);
				command.Parameters.AddWithValue("@owner_id", captureId);
				command.Parameters.AddWithValue("@role", role);
				command.Parameters.AddWithValue("@artifact_id", artifactId);
				if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
					throw new InvalidOperationException("The sealed capture is missing the exact durable retained-artifact reference required for persistence.");
			}
		}
	}
}
