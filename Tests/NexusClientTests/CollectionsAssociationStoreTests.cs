using System;
using System.IO;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// C4.7 target association, member binding and deliberate override persistence coverage.
	/// </summary>
	public class CollectionsAssociationStoreTests
	{
		[Test]
		public void Association_RoundTripsAndAllowsStateRefreshWithoutIdentityRebinding()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore featureStore = CreateFeatureStore(root);
				CollectionRevisionIdentity revision = SeedNexusRevision(featureStore, "association-roundtrip", "rev-a", 1);
				var associations = new CollectionsAssociationStore(featureStore);
				var association = new CollectionTargetAssociation(Guid.NewGuid(), revision,
					CollectionTargetIdentity.FromFingerprint("game-a|storage-a"), CollectionAssociationState.Applied);

				associations.SaveAssociation(association);
				CollectionTargetAssociation loaded = associations.GetAssociation(association.AssociationId);
				Assert.IsNotNull(loaded);
				Assert.AreEqual(association.AssociationId, loaded.AssociationId);
				Assert.AreEqual(revision, loaded.Revision);
				Assert.AreEqual(association.Target, loaded.Target);
				Assert.AreEqual(CollectionAssociationState.Applied, loaded.State);

				associations.SaveAssociation(association.WithState(CollectionAssociationState.Modified));
				Assert.AreEqual(CollectionAssociationState.Modified, associations.GetAssociation(association.AssociationId).State);

				var rebound = new CollectionTargetAssociation(association.AssociationId, revision,
					CollectionTargetIdentity.FromFingerprint("game-a|different-storage"), CollectionAssociationState.Applied);
				Assert.Throws<InvalidOperationException>(() => associations.SaveAssociation(rebound));
				Assert.AreEqual(association.Target, associations.GetAssociation(association.AssociationId).Target);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Association_RequiresExactPersistedRevision()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore featureStore = CreateFeatureStore(root);
				var associations = new CollectionsAssociationStore(featureStore);
				CollectionIdentity collection = CollectionIdentity.FromNexus("missing-revision");
				CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "rev-a", 1);
				var association = new CollectionTargetAssociation(Guid.NewGuid(), revision,
					CollectionTargetIdentity.FromFingerprint("target-a"), CollectionAssociationState.Applied);

				Assert.Throws<InvalidOperationException>(() => associations.SaveAssociation(association));
				Assert.IsNull(associations.GetAssociation(association.AssociationId));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Binding_RoundTripsAndRefreshesCurrentNativeRecipeWithoutDuplicateRow()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore featureStore = CreateFeatureStore(root);
				CollectionTargetAssociation association = SeedAssociation(featureStore, "binding-roundtrip", "rev-a", 1, "target-a");
				var associations = new CollectionsAssociationStore(featureStore);
				CollectionMemberKey member = CollectionMemberKey.FromProvider("member-a");
				var firstBinding = new CollectionMemberBinding(association, member,
					new NativeModInstanceIdentity(association.Target, "native-mod-1"),
					CollectionRecipeIdentity.FromFingerprint("recipe-v1"), CollectionMemberBindingKind.AdoptedExisting);

				associations.SaveBinding(firstBinding);
				CollectionMemberBinding loaded = associations.GetBindings(association.AssociationId).Single();
				Assert.AreEqual(member, loaded.MemberKey);
				Assert.AreEqual("native-mod-1", loaded.NativeMod.NativeModKey);
				Assert.AreEqual("recipe-v1", loaded.VerifiedRecipe.Fingerprint);
				Assert.AreEqual(CollectionMemberBindingKind.AdoptedExisting, loaded.BindingKind);

				var refreshed = new CollectionMemberBinding(association, member,
					new NativeModInstanceIdentity(association.Target, "native-mod-2"),
					CollectionRecipeIdentity.FromFingerprint("recipe-v2"), CollectionMemberBindingKind.InstalledForCollection);
				associations.SaveBinding(refreshed);

				CollectionMemberBinding[] bindings = associations.GetBindings(association.AssociationId).ToArray();
				Assert.AreEqual(1, bindings.Length);
				Assert.AreEqual("native-mod-2", bindings[0].NativeMod.NativeModKey);
				Assert.AreEqual("recipe-v2", bindings[0].VerifiedRecipe.Fingerprint);
				Assert.AreEqual(CollectionMemberBindingKind.InstalledForCollection, bindings[0].BindingKind);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Binding_ReverseLookupPreservesManyToManySharedNativeInstance()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore featureStore = CreateFeatureStore(root);
				CollectionTargetAssociation first = SeedAssociation(featureStore, "shared-one", "rev-1", 1, "shared-target");
				CollectionTargetAssociation second = SeedAssociation(featureStore, "shared-two", "rev-1", 1, "shared-target");
				var associations = new CollectionsAssociationStore(featureStore);
				var nativeMod = new NativeModInstanceIdentity(first.Target, "native-shared");
				associations.SaveBinding(new CollectionMemberBinding(first, CollectionMemberKey.FromProvider("member-one"), nativeMod,
					CollectionRecipeIdentity.FromFingerprint("shared-recipe"), CollectionMemberBindingKind.AdoptedExisting));
				associations.SaveBinding(new CollectionMemberBinding(second, CollectionMemberKey.FromProvider("member-two"), nativeMod,
					CollectionRecipeIdentity.FromFingerprint("shared-recipe"), CollectionMemberBindingKind.AdoptedExisting));

				CollectionMemberBinding[] reverse = associations.GetBindingsForNativeMod(nativeMod).ToArray();
				Assert.AreEqual(2, reverse.Length);
				CollectionAssert.AreEquivalent(
					new[] { first.AssociationId, second.AssociationId },
					reverse.Select(x => x.Association.AssociationId).ToArray());
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Override_RoundTripsMemberAndSubjectScopedRequirements()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore featureStore = CreateFeatureStore(root);
				CollectionTargetAssociation association = SeedAssociation(featureStore, "override-roundtrip", "rev-a", 2, "target-a");
				var associations = new CollectionsAssociationStore(featureStore);
				var memberRequirement = new CollectionRequirementReference(association, CollectionMemberKey.FromProvider("member-a"),
					CollectionRequirementAspect.MemberEnabledState, null);
				var memberOverride = new UserOverride(Guid.NewGuid(), memberRequirement,
					CollectionRequirementState.Present("bool-v1", "enabled"), CollectionRequirementState.Absent(), "Keep disabled");
				var winnerRequirement = new CollectionRequirementReference(association, null,
					CollectionRequirementAspect.FileWinner, "meshes/example.nif");
				var winnerOverride = new UserOverride(Guid.NewGuid(), winnerRequirement,
					CollectionRequirementState.Present("owner-v1", "curator-owner"),
					CollectionRequirementState.Present("owner-v1", "user-owner"), null);

				associations.SaveOverride(memberOverride);
				associations.SaveOverride(winnerOverride);

				UserOverride[] loaded = associations.GetOverrides(association.AssociationId).ToArray();
				Assert.AreEqual(2, loaded.Length);
				UserOverride loadedMember = loaded.Single(x => x.OverrideId == memberOverride.OverrideId);
				Assert.AreEqual(memberRequirement, loadedMember.Requirement);
				Assert.AreEqual(CollectionRequirementStateKind.Present, loadedMember.BaselineState.Kind);
				Assert.AreEqual(CollectionRequirementStateKind.Absent, loadedMember.UserChosenState.Kind);
				Assert.AreEqual("Keep disabled", loadedMember.Note);
				UserOverride loadedWinner = loaded.Single(x => x.OverrideId == winnerOverride.OverrideId);
				Assert.AreEqual(winnerRequirement, loadedWinner.Requirement);
				Assert.AreEqual("user-owner", loadedWinner.UserChosenState.Fingerprint);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Override_SameIdentityMayRefreshChosenStateButCannotRebindBaseline()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore featureStore = CreateFeatureStore(root);
				CollectionTargetAssociation association = SeedAssociation(featureStore, "override-refresh", "rev-a", 1, "target-a");
				var associations = new CollectionsAssociationStore(featureStore);
				var requirement = new CollectionRequirementReference(association, CollectionMemberKey.FromProvider("member-a"),
					CollectionRequirementAspect.InstallerRecipe, null);
				Guid overrideId = Guid.NewGuid();
				CollectionRequirementState baseline = CollectionRequirementState.Present("recipe-v1", "baseline");
				associations.SaveOverride(new UserOverride(overrideId, requirement, baseline,
					CollectionRequirementState.Present("recipe-v1", "choice-a"), "First choice"));

				associations.SaveOverride(new UserOverride(overrideId, requirement, baseline,
					CollectionRequirementState.Present("recipe-v1", "choice-b"), "Second choice"));

				UserOverride loaded = associations.GetOverrides(association.AssociationId).Single();
				Assert.AreEqual("choice-b", loaded.UserChosenState.Fingerprint);
				Assert.AreEqual("Second choice", loaded.Note);

				Assert.Throws<InvalidOperationException>(() => associations.SaveOverride(new UserOverride(overrideId, requirement,
					CollectionRequirementState.Present("recipe-v1", "different-baseline"),
					CollectionRequirementState.Present("recipe-v1", "choice-c"), null)));
				Assert.AreEqual("choice-b", associations.GetOverrides(association.AssociationId).Single().UserChosenState.Fingerprint);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Override_RejectsSecondActiveIdentityForSameExactRequirement()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore featureStore = CreateFeatureStore(root);
				CollectionTargetAssociation association = SeedAssociation(featureStore, "override-unique", "rev-a", 1, "target-a");
				var associations = new CollectionsAssociationStore(featureStore);
				var requirement = new CollectionRequirementReference(association, null,
					CollectionRequirementAspect.PluginState, "plugin-a.esp");
				CollectionRequirementState baseline = CollectionRequirementState.Present("plugin-v1", "enabled");
				associations.SaveOverride(new UserOverride(Guid.NewGuid(), requirement, baseline,
					CollectionRequirementState.Absent(), "Disable it"));

				Assert.Throws<InvalidOperationException>(() => associations.SaveOverride(new UserOverride(Guid.NewGuid(), requirement, baseline,
					CollectionRequirementState.Present("plugin-v1", "enabled-later"), "Conflicting second decision")));
				Assert.AreEqual(1, associations.GetOverrides(association.AssociationId).Count);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void BindingAndOverride_RejectStaleAssociationBaselineWithSameAssociationId()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore featureStore = CreateFeatureStore(root);
				CollectionRevisionIdentity firstRevision = SeedNexusRevision(featureStore, "stale-baseline", "rev-1", 1);
				CollectionRevisionIdentity secondRevision = SeedAdditionalNexusRevision(featureStore, firstRevision.Collection, "rev-2", 2);
				var associations = new CollectionsAssociationStore(featureStore);
				var target = CollectionTargetIdentity.FromFingerprint("target-a");
				Guid associationId = Guid.NewGuid();
				var persisted = new CollectionTargetAssociation(associationId, firstRevision, target, CollectionAssociationState.Applied);
				associations.SaveAssociation(persisted);
				var stale = new CollectionTargetAssociation(associationId, secondRevision, target, CollectionAssociationState.Applied);

				var staleBinding = new CollectionMemberBinding(stale, CollectionMemberKey.FromProvider("member-a"),
					new NativeModInstanceIdentity(target, "native-a"), CollectionRecipeIdentity.FromFingerprint("recipe-a"),
					CollectionMemberBindingKind.AdoptedExisting);
				Assert.Throws<InvalidOperationException>(() => associations.SaveBinding(staleBinding));

				var staleRequirement = new CollectionRequirementReference(stale, CollectionMemberKey.FromProvider("member-a"),
					CollectionRequirementAspect.MemberParticipation, null);
				var staleOverride = new UserOverride(Guid.NewGuid(), staleRequirement,
					CollectionRequirementState.Present("participation-v1", "required"), CollectionRequirementState.Absent(), null);
				Assert.Throws<InvalidOperationException>(() => associations.SaveOverride(staleOverride));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void TargetSnapshot_LoadsOnlyRequestedTargetRelationships()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore featureStore = CreateFeatureStore(root);
				CollectionTargetAssociation requested = SeedAssociation(featureStore, "target-snapshot-a", "rev-a", 1, "target-a");
				CollectionTargetAssociation other = SeedAssociation(featureStore, "target-snapshot-b", "rev-b", 1, "target-b");
				var associations = new CollectionsAssociationStore(featureStore);
				associations.SaveBinding(new CollectionMemberBinding(requested, CollectionMemberKey.FromProvider("member-a"),
					new NativeModInstanceIdentity(requested.Target, "native-a"), CollectionRecipeIdentity.FromFingerprint("recipe-a"),
					CollectionMemberBindingKind.AdoptedExisting));
				associations.SaveBinding(new CollectionMemberBinding(other, CollectionMemberKey.FromProvider("member-b"),
					new NativeModInstanceIdentity(other.Target, "native-b"), CollectionRecipeIdentity.FromFingerprint("recipe-b"),
					CollectionMemberBindingKind.AdoptedExisting));

				var requirement = new CollectionRequirementReference(requested, CollectionMemberKey.FromProvider("member-a"),
					CollectionRequirementAspect.MemberEnabledState, null);
				associations.SaveOverride(new UserOverride(Guid.NewGuid(), requirement,
					CollectionRequirementState.Present("bool-v1", "enabled"), CollectionRequirementState.Absent(), null));

				CollectionsAssociationTargetSnapshot snapshot = associations.GetTargetSnapshot(requested.Target);

				Assert.AreEqual(requested.Target, snapshot.Target);
				Assert.AreEqual(1, snapshot.Associations.Count);
				Assert.AreEqual(requested.AssociationId, snapshot.Associations[0].AssociationId);
				Assert.AreEqual(1, snapshot.Bindings.Count);
				Assert.AreEqual("native-a", snapshot.Bindings[0].NativeMod.NativeModKey);
				Assert.AreEqual(1, snapshot.Overrides.Count);
				Assert.AreEqual(requested.AssociationId, snapshot.Overrides[0].Requirement.AssociationId);

				CollectionsAssociationTargetSnapshot empty = associations.GetTargetSnapshot(CollectionTargetIdentity.FromFingerprint("target-empty"));
				Assert.AreEqual(0, empty.Associations.Count);
				Assert.AreEqual(0, empty.Bindings.Count);
				Assert.AreEqual(0, empty.Overrides.Count);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void LocalAssociationAndLocalMemberIdentity_RoundTripWithoutNexusIdentityAssumptions()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore featureStore = CreateFeatureStore(root);
				var catalog = new CollectionsCatalogStore(featureStore);
				CollectionIdentity collection = CollectionIdentity.FromLocal(Guid.NewGuid());
				CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromLocal(collection, Guid.NewGuid());
				catalog.SaveDefinitionAndRevision(new CollectionDefinition(collection, "Local", null, null),
					new CollectionRevision(revision, "Local revision", null, 1));
				var associations = new CollectionsAssociationStore(featureStore);
				var association = new CollectionTargetAssociation(Guid.NewGuid(), revision,
					CollectionTargetIdentity.FromFingerprint("local-target"), CollectionAssociationState.Modified);
				associations.SaveAssociation(association);
				CollectionMemberKey member = CollectionMemberKey.FromLocal(Guid.NewGuid());
				associations.SaveBinding(new CollectionMemberBinding(association, member,
					new NativeModInstanceIdentity(association.Target, "local-native"), CollectionRecipeIdentity.FromFingerprint("local-recipe"),
					CollectionMemberBindingKind.InstalledForCollection));

				CollectionTargetAssociation loadedAssociation = associations.GetAssociation(association.AssociationId);
				CollectionMemberBinding loadedBinding = associations.GetBindings(association.AssociationId).Single();
				Assert.AreEqual(CollectionOrigin.Local, loadedAssociation.Revision.Collection.Origin);
				Assert.IsFalse(loadedAssociation.Revision.NexusRevisionNumber.HasValue);
				Assert.AreEqual(CollectionMemberKeyKind.Local, loadedBinding.MemberKey.Kind);
				Assert.AreEqual(member, loadedBinding.MemberKey);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static CollectionsStore CreateFeatureStore(string root)
		{
			var store = new CollectionsStore(root);
			store.CreateNew();
			return store;
		}

		private static CollectionTargetAssociation SeedAssociation(CollectionsStore store, string collectionId,
			string revisionId, long revisionNumber, string targetFingerprint)
		{
			CollectionRevisionIdentity revision = SeedNexusRevision(store, collectionId, revisionId, revisionNumber);
			var association = new CollectionTargetAssociation(Guid.NewGuid(), revision,
				CollectionTargetIdentity.FromFingerprint(targetFingerprint), CollectionAssociationState.Applied);
			new CollectionsAssociationStore(store).SaveAssociation(association);
			return association;
		}

		private static CollectionRevisionIdentity SeedNexusRevision(CollectionsStore store, string collectionId,
			string revisionId, long revisionNumber)
		{
			var catalog = new CollectionsCatalogStore(store);
			CollectionIdentity collection = CollectionIdentity.FromNexus(collectionId);
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, revisionId, revisionNumber);
			catalog.SaveDefinitionAndRevision(new CollectionDefinition(collection, collectionId, null, null),
				new CollectionRevision(revision, "Revision " + revisionNumber, null, null));
			return revision;
		}

		private static CollectionRevisionIdentity SeedAdditionalNexusRevision(CollectionsStore store, CollectionIdentity collection,
			string revisionId, long revisionNumber)
		{
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, revisionId, revisionNumber);
			new CollectionsCatalogStore(store).SaveRevision(new CollectionRevision(revision, "Revision " + revisionNumber, null, null));
			return revision;
		}

		private static string CreateTemporaryDirectory()
		{
			string directory = Path.Combine(Path.GetTempPath(), "nmm-collections-association-tests-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			return directory;
		}
	}
}
