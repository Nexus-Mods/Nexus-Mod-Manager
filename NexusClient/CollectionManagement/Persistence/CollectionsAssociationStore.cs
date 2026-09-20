using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>
	/// Persists collection target associations, member/native bindings and deliberate user overrides.
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

				return new CollectionsAssociationTargetSnapshot(target, associations, bindings, overrides);
			});
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
					command.Parameters.AddWithValue("@association_id", associationId.ToString("D"));
					using (SQLiteDataReader reader = command.ExecuteReader())
					{
						while (reader.Read())
							overrides.Add(ReadOverride(reader, association));
					}
				}
				return overrides;
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
				throw new InvalidOperationException("The Collection target association must be persisted before deliberate overrides.");
			if (!association.Revision.Equals(requirement.BaselineRevision) || !association.Target.Equals(requirement.Target))
				throw new InvalidOperationException("The deliberate override does not match the persisted association baseline.");
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
}
