using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Editable state for one resolved member in a Local Collection working copy.</summary>
	public sealed class CollectionLocalWorkingCopyMemberEditState
	{
		internal CollectionLocalWorkingCopyMemberEditState(NormalizedCollectionMember baseMember,
			bool included, CollectionMemberRequirement requirement)
		{
			if (baseMember == null) throw new ArgumentNullException(nameof(baseMember));
			if (!baseMember.IdentityResolution.IsResolved)
				throw new ArgumentException("Only resolved Collection members can be represented as editable working-copy rows.", nameof(baseMember));
			if (!Enum.IsDefined(typeof(CollectionMemberRequirement), requirement) || requirement == CollectionMemberRequirement.Unknown)
				throw new ArgumentOutOfRangeException(nameof(requirement));
			MemberKey = baseMember.IdentityResolution.Key;
			SourceOrdinal = baseMember.SourceOrdinal;
			DisplayName = String.IsNullOrWhiteSpace(baseMember.DisplayName) ? MemberKey.ToString() : baseMember.DisplayName;
			Included = included;
			Requirement = requirement;
		}

		public CollectionMemberKey MemberKey { get; }
		public int SourceOrdinal { get; }
		public string DisplayName { get; }
		public bool Included { get; }
		public CollectionMemberRequirement Requirement { get; }
	}

	/// <summary>One explicit editable-member decision submitted from the Local working-copy editor.</summary>
	public sealed class CollectionLocalWorkingCopyMemberDecision
	{
		public CollectionLocalWorkingCopyMemberDecision(CollectionMemberKey memberKey, bool included,
			CollectionMemberRequirement requirement)
		{
			MemberKey = memberKey ?? throw new ArgumentNullException(nameof(memberKey));
			if (!Enum.IsDefined(typeof(CollectionMemberRequirement), requirement) || requirement == CollectionMemberRequirement.Unknown)
				throw new ArgumentOutOfRangeException(nameof(requirement));
			Included = included;
			Requirement = requirement;
		}

		public CollectionMemberKey MemberKey { get; }
		public bool Included { get; }
		public CollectionMemberRequirement Requirement { get; }
	}

	/// <summary>Read-only editable snapshot of one Local working copy.</summary>
	public sealed class CollectionLocalWorkingCopyEditSnapshot
	{
		internal CollectionLocalWorkingCopyEditSnapshot(CollectionLocalWorkingCopyRecord record, CollectionDefinition definition,
			CollectionRevision sourceRevision, IEnumerable<CollectionLocalWorkingCopyMemberEditState> members,
			int lockedMemberCount, int savedRevisionCount)
		{
			Record = record ?? throw new ArgumentNullException(nameof(record));
			Definition = definition ?? throw new ArgumentNullException(nameof(definition));
			SourceRevision = sourceRevision ?? throw new ArgumentNullException(nameof(sourceRevision));
			if (!Definition.Identity.Equals(Record.Collection))
				throw new ArgumentException("The editable Local definition must belong to the exact working copy.", nameof(definition));
			if (!SourceRevision.Identity.Equals(Record.SourceRevision))
				throw new ArgumentException("The editable source revision must match the working-copy base revision.", nameof(sourceRevision));
			if (lockedMemberCount < 0) throw new ArgumentOutOfRangeException(nameof(lockedMemberCount));
			if (savedRevisionCount < 0) throw new ArgumentOutOfRangeException(nameof(savedRevisionCount));
			Members = new ReadOnlyCollection<CollectionLocalWorkingCopyMemberEditState>(
				(members ?? throw new ArgumentNullException(nameof(members))).ToList());
			LockedMemberCount = lockedMemberCount;
			SavedRevisionCount = savedRevisionCount;
		}

		public CollectionLocalWorkingCopyRecord Record { get; }
		public CollectionDefinition Definition { get; }
		public CollectionRevision SourceRevision { get; }
		public IReadOnlyList<CollectionLocalWorkingCopyMemberEditState> Members { get; }
		public int LockedMemberCount { get; }
		public int SavedRevisionCount { get; }
	}

	/// <summary>
	/// Edits one Local Collection working copy without touching installed/native state and seals immutable Local revisions on demand.
	/// </summary>
	public sealed class CollectionLocalWorkingCopyEditor
	{
		private readonly CollectionsStore _store;
		private readonly CollectionsCatalogStore _catalogStore;
		private readonly CollectionsRevisionSourceStore _revisionSourceStore;
		private readonly CollectionsLocalWorkingCopyStore _workingCopyStore;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly NexusCollectionManifestNormalizer _normalizer;

		public CollectionLocalWorkingCopyEditor(CollectionsStore store)
		{
			_store = store ?? throw new ArgumentNullException(nameof(store));
			_catalogStore = new CollectionsCatalogStore(store);
			_revisionSourceStore = new CollectionsRevisionSourceStore(store);
			_workingCopyStore = new CollectionsLocalWorkingCopyStore(store);
			_artifactStore = new CollectionsRetainedArtifactStore(store);
			_normalizer = new NexusCollectionManifestNormalizer();
		}

		/// <summary>Loads the current mutable recipe and projects resolved base members into editable rows.</summary>
		public CollectionLocalWorkingCopyEditSnapshot GetSnapshot(CollectionIdentity localCollection)
		{
			CollectionLocalWorkingCopyRecord record = RequireWorkingCopy(localCollection);
			CollectionDefinition definition = _catalogStore.GetDefinition(localCollection);
			CollectionRevision sourceRevision = _catalogStore.GetRevision(record.SourceRevision);
			if (definition == null || sourceRevision == null)
				throw new CollectionsStoreSchemaException("The Local working copy is missing required catalog metadata.");

			NexusCollectionManifestNormalizationResult baseNormalized = Normalize(_workingCopyStore.LoadBaseManifest(record), sourceRevision);
			NexusCollectionManifestNormalizationResult draftNormalized = Normalize(_workingCopyStore.LoadDraftManifest(record), sourceRevision);
			var draftByKey = draftNormalized.Manifest.Members
				.Where(x => x.IdentityResolution.IsResolved)
				.ToDictionary(x => x.IdentityResolution.Key);
			var members = new List<CollectionLocalWorkingCopyMemberEditState>();
			int locked = 0;
			foreach (NormalizedCollectionMember member in baseNormalized.Manifest.Members.OrderBy(x => x.SourceOrdinal))
			{
				if (!member.IdentityResolution.IsResolved)
				{
					locked++;
					continue;
				}
				NormalizedCollectionMember current;
				bool included = draftByKey.TryGetValue(member.IdentityResolution.Key, out current);
				members.Add(new CollectionLocalWorkingCopyMemberEditState(member, included,
					included ? current.Requirement : member.Requirement));
			}
			return new CollectionLocalWorkingCopyEditSnapshot(record, definition, sourceRevision, members, locked,
				_catalogStore.GetRevisions(localCollection).Count);
		}

		/// <summary>
		/// Saves mutable metadata/member inclusion/required-vs-optional decisions by regenerating the draft from the immutable base.
		/// </summary>
		public CollectionLocalWorkingCopyEditSnapshot SaveDraft(CollectionLocalWorkingCopyEditSnapshot snapshot,
			string displayName, string summary, IEnumerable<CollectionLocalWorkingCopyMemberDecision> decisions)
		{
			if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
			displayName = CollectionDomainValidation.RequireDisplayValue(displayName, nameof(displayName));
			List<CollectionLocalWorkingCopyMemberDecision> copied = (decisions ?? throw new ArgumentNullException(nameof(decisions))).ToList();
			if (copied.Any(x => x == null)) throw new ArgumentException("A Local working-copy edit cannot contain a null member decision.", nameof(decisions));
			if (copied.GroupBy(x => x.MemberKey).Any(x => x.Count() != 1))
				throw new ArgumentException("A Local working-copy edit cannot contain duplicate member decisions.", nameof(decisions));

			CollectionLocalWorkingCopyRecord current = RequireWorkingCopy(snapshot.Record.Collection);
			if (current.UpdatedUtc != snapshot.Record.UpdatedUtc ||
				!StringComparer.Ordinal.Equals(current.DraftManifestArtifactId, snapshot.Record.DraftManifestArtifactId))
				throw new InvalidOperationException("The Local working copy changed after the editor was opened; reload it before saving.");

			byte[] baseBytes = _workingCopyStore.LoadBaseManifest(current);
			NexusCollectionManifestNormalizationResult baseNormalized = Normalize(baseBytes, snapshot.SourceRevision);
			var baseByKey = baseNormalized.Manifest.Members.Where(x => x.IdentityResolution.IsResolved)
				.ToDictionary(x => x.IdentityResolution.Key);
			foreach (CollectionLocalWorkingCopyMemberDecision decision in copied)
				if (!baseByKey.ContainsKey(decision.MemberKey))
					throw new InvalidOperationException("A submitted Local working-copy member decision no longer belongs to the immutable clone base.");

			var requested = copied.ToDictionary(x => x.MemberKey);
			if (requested.Count != baseByKey.Count || baseByKey.Keys.Any(x => !requested.ContainsKey(x)))
				throw new InvalidOperationException("Saving a Local working-copy edit requires an explicit decision for every resolved member in the immutable clone base.");
			JObject root = ParseRoot(baseBytes);
			JArray mods = root["mods"] as JArray;
			if (mods == null)
				throw new InvalidDataException("The Local working-copy base no longer exposes an editable mods array.");

			var removals = new List<int>();
			foreach (NormalizedCollectionMember baseMember in baseNormalized.Manifest.Members.Where(x => x.IdentityResolution.IsResolved))
			{
				CollectionLocalWorkingCopyMemberDecision decision;
				if (!requested.TryGetValue(baseMember.IdentityResolution.Key, out decision))
					continue;
				if (baseMember.SourceOrdinal < 0 || baseMember.SourceOrdinal >= mods.Count || !(mods[baseMember.SourceOrdinal] is JObject))
					throw new InvalidDataException("A Local working-copy member no longer maps to its immutable source ordinal.");
				if (!decision.Included)
				{
					removals.Add(baseMember.SourceOrdinal);
					continue;
				}
				((JObject)mods[baseMember.SourceOrdinal])["optional"] = decision.Requirement == CollectionMemberRequirement.Optional;
			}
			foreach (int ordinal in removals.Distinct().OrderByDescending(x => x))
				mods.RemoveAt(ordinal);

			JObject info = root["info"] as JObject;
			if (info == null)
				throw new InvalidDataException("The Local working-copy base no longer exposes the required info object.");
			info["name"] = displayName;
			info["description"] = summary ?? String.Empty;

			byte[] editedBytes = new UTF8Encoding(false, true).GetBytes(root.ToString(Formatting.Indented));
			Normalize(editedBytes, snapshot.SourceRevision);
			var definition = new CollectionDefinition(snapshot.Record.Collection, displayName,
				snapshot.Definition.AuthorDisplayName, summary);
			_workingCopyStore.SaveDraft(snapshot.Record, definition, editedBytes);
			return GetSnapshot(snapshot.Record.Collection);
		}

		/// <summary>Seals the current draft as a new immutable Local Collection revision without mutating the installed setup.</summary>
		public CollectionRevision SealRevision(CollectionIdentity localCollection, string revisionLabel, string notes)
		{
			revisionLabel = CollectionDomainValidation.RequireDisplayValue(revisionLabel, nameof(revisionLabel));
			CollectionLocalWorkingCopyRecord record = RequireWorkingCopy(localCollection);
			CollectionDefinition definition = _catalogStore.GetDefinition(localCollection);
			if (definition == null)
				throw new CollectionsStoreSchemaException("The Local working-copy definition is missing from the durable catalog.");
			byte[] draftBytes = _workingCopyStore.LoadDraftManifest(record);
			var identity = CollectionRevisionIdentity.FromLocal(localCollection, Guid.NewGuid());
			var provisional = new CollectionRevision(identity, revisionLabel, notes, null);
			NexusCollectionManifestNormalizationResult normalized = Normalize(draftBytes, provisional);
			var revision = new CollectionRevision(identity, revisionLabel, notes, normalized.Manifest.Members.Count);

			bool requiresOuterBundle = normalized.Manifest.Members.Any(x => x.Artifact != null &&
				StringComparer.Ordinal.Equals(x.Artifact.Scheme, CollectionBundledArtifactIdentity.Scheme));
			CollectionRevisionSourceInputKind inputKind;
			CollectionContentHash outerHash;
			long outerLength;
			if (requiresOuterBundle)
			{
				if (String.IsNullOrEmpty(record.BaseSource.RawBundleArtifactId))
					throw new InvalidOperationException("The edited Local revision still contains embedded Collection members, but the cloned source has no retained outer bundle to inherit.");
				if (!_artifactStore.VerifyArtifact(record.BaseSource.RawBundleArtifactId))
					throw new InvalidDataException("The inherited Local working-copy bundle failed retained-content verification.");
				inputKind = CollectionRevisionSourceInputKind.LocalWorkingCopy;
				outerHash = record.BaseSource.BundleContentHash;
				outerLength = record.BaseSource.BundleByteLength;
			}
			else
			{
				inputKind = CollectionRevisionSourceInputKind.RawManifest;
				outerHash = normalized.Manifest.Source.ContentHash;
				outerLength = normalized.Manifest.Source.ByteLength;
			}

			_catalogStore.SaveRevision(revision);
			_revisionSourceStore.RetainManifest(normalized.Manifest, inputKind, outerHash, outerLength,
				record.BaseSource.ManifestEntryName, draftBytes);
			if (inputKind == CollectionRevisionSourceInputKind.LocalWorkingCopy)
			{
				using (Stream bundle = _artifactStore.OpenRead(record.BaseSource.RawBundleArtifactId))
					_revisionSourceStore.RetainBundle(revision.Identity, bundle);
			}
			return revision;
		}

		private CollectionLocalWorkingCopyRecord RequireWorkingCopy(CollectionIdentity localCollection)
		{
			if (localCollection == null) throw new ArgumentNullException(nameof(localCollection));
			if (localCollection.Origin != CollectionOrigin.Local)
				throw new ArgumentException("A Local Collection identity is required.", nameof(localCollection));
			CollectionLocalWorkingCopyRecord record = _workingCopyStore.Get(localCollection);
			if (record == null) throw new InvalidOperationException("The selected Local Collection working copy no longer exists.");
			if (!StringComparer.Ordinal.Equals(record.BaseSource.ManifestSource.SchemaIdentity, NexusCollectionManifestNormalizer.SchemaIdentity) ||
				!StringComparer.Ordinal.Equals(record.BaseSource.ManifestSource.NormalizerVersion, NexusCollectionManifestNormalizer.NormalizerVersion))
				throw new InvalidOperationException("This Local working copy was cloned with a different Collection manifest schema/normalizer and cannot be edited by this build without explicit migration.");
			return record;
		}

		private NexusCollectionManifestNormalizationResult Normalize(byte[] bytes, CollectionRevision revision)
		{
			NexusCollectionManifestNormalizationResult result = _normalizer.Normalize(bytes, revision);
			if (result == null || result.Manifest == null)
				throw new InvalidDataException("The Local working-copy manifest could not be normalized.");
			return result;
		}

		private static JObject ParseRoot(byte[] bytes)
		{
			string json = new UTF8Encoding(false, true).GetString(bytes ?? throw new ArgumentNullException(nameof(bytes)));
			if (json.Length > 0 && json[0] == '\uFEFF') json = json.Substring(1);
			JObject root = JObject.Parse(json);
			if (root == null) throw new InvalidDataException("The Local working-copy manifest root must be a JSON object.");
			return root;
		}
	}
}
