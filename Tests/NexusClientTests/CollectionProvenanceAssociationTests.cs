using System;
using Nexus.Client.CollectionManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class CollectionProvenanceAssociationTests
	{
		[Test]
		public void TargetIdentity_IsOpaqueOrdinalAndRejectsMissingOrPaddedTokens()
		{
			CollectionTargetIdentity first = CollectionTargetIdentity.FromFingerprint("game-storage-A");
			CollectionTargetIdentity same = CollectionTargetIdentity.FromFingerprint("game-storage-A");
			CollectionTargetIdentity differentCase = CollectionTargetIdentity.FromFingerprint("Game-Storage-A");

			Assert.That(first.Fingerprint, Is.EqualTo("game-storage-A"));
			Assert.That(first, Is.EqualTo(same));
			Assert.That(first.GetHashCode(), Is.EqualTo(same.GetHashCode()));
			Assert.That(first, Is.Not.EqualTo(differentCase));
			Assert.Throws<ArgumentException>(() => CollectionTargetIdentity.FromFingerprint(null));
			Assert.Throws<ArgumentException>(() => CollectionTargetIdentity.FromFingerprint(" target"));
		}

		[Test]
		public void NativeModIdentity_IsScopedToRealTargetNotGlobalNativeKey()
		{
			NativeModInstanceIdentity first = new NativeModInstanceIdentity(
				CollectionTargetIdentity.FromFingerprint("target-A"), "native-key-1");
			NativeModInstanceIdentity same = new NativeModInstanceIdentity(
				CollectionTargetIdentity.FromFingerprint("target-A"), "native-key-1");
			NativeModInstanceIdentity otherTarget = new NativeModInstanceIdentity(
				CollectionTargetIdentity.FromFingerprint("target-B"), "native-key-1");

			Assert.That(first, Is.EqualTo(same));
			Assert.That(first, Is.Not.EqualTo(otherTarget));
			Assert.Throws<ArgumentNullException>(() => new NativeModInstanceIdentity(null, "native-key-1"));
			Assert.Throws<ArgumentException>(() => new NativeModInstanceIdentity(first.Target, " "));
		}

		[Test]
		public void RecipeIdentity_IsOpaqueOrdinalAndIndependentFromDisplayOrMemberIdentity()
		{
			CollectionRecipeIdentity first = CollectionRecipeIdentity.FromFingerprint("recipe-sha256:ABC123");
			CollectionRecipeIdentity same = CollectionRecipeIdentity.FromFingerprint("recipe-sha256:ABC123");
			CollectionRecipeIdentity differentCase = CollectionRecipeIdentity.FromFingerprint("recipe-sha256:abc123");

			Assert.That(first, Is.EqualTo(same));
			Assert.That(first.GetHashCode(), Is.EqualTo(same.GetHashCode()));
			Assert.That(first, Is.Not.EqualTo(differentCase));
			Assert.Throws<ArgumentException>(() => CollectionRecipeIdentity.FromFingerprint(" "));
		}

		[Test]
		public void TargetAssociation_BindsConcreteRevisionToTargetWithoutExclusiveActiveCollectionConcept()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-A");
			CollectionTargetAssociation first = CreateAssociation("2210", "772530", 100, target);
			CollectionTargetAssociation second = CreateAssociation("2211", "800000", 7, target);

			Assert.That(first.Target, Is.EqualTo(target));
			Assert.That(first.Revision.NexusRevisionNumber, Is.EqualTo(100));
			Assert.That(first.State, Is.EqualTo(CollectionAssociationState.Applied));
			Assert.That(second.Target, Is.EqualTo(target));
			Assert.That(second.AssociationId, Is.Not.EqualTo(first.AssociationId));
		}

		[Test]
		public void TargetAssociation_StateTransitionPreservesAssociationRevisionAndTargetIdentity()
		{
			CollectionTargetAssociation applied = CreateAssociation(
				"2210", "772530", 100, CollectionTargetIdentity.FromFingerprint("target-A"));
			CollectionTargetAssociation modified = applied.WithState(CollectionAssociationState.Modified);

			Assert.That(modified.AssociationId, Is.EqualTo(applied.AssociationId));
			Assert.That(modified.Revision, Is.SameAs(applied.Revision));
			Assert.That(modified.Target, Is.SameAs(applied.Target));
			Assert.That(modified.State, Is.EqualTo(CollectionAssociationState.Modified));
			Assert.That(modified, Is.EqualTo(applied));
		}

		[Test]
		public void TargetAssociation_RejectsMissingIdentityRevisionTargetAndUnknownState()
		{
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(
				CollectionIdentity.FromNexus("2210"), "772530", 100);
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-A");

			Assert.Throws<ArgumentException>(() => new CollectionTargetAssociation(Guid.Empty, revision, target, CollectionAssociationState.Applied));
			Assert.Throws<ArgumentNullException>(() => new CollectionTargetAssociation(Guid.NewGuid(), null, target, CollectionAssociationState.Applied));
			Assert.Throws<ArgumentNullException>(() => new CollectionTargetAssociation(Guid.NewGuid(), revision, null, CollectionAssociationState.Applied));
			Assert.Throws<ArgumentOutOfRangeException>(() => new CollectionTargetAssociation(Guid.NewGuid(), revision, target, CollectionAssociationState.Unknown));
			Assert.Throws<ArgumentOutOfRangeException>(() => new CollectionTargetAssociation(Guid.NewGuid(), revision, target, (CollectionAssociationState)999));
		}

		[Test]
		public void MemberBinding_RecordsAdoptedVersusInstalledProvenanceAndVerifiedRecipe()
		{
			CollectionTargetAssociation association = CreateAssociation(
				"2210", "772530", 100, CollectionTargetIdentity.FromFingerprint("target-A"));
			NativeModInstanceIdentity nativeMod = new NativeModInstanceIdentity(association.Target, "native-key-1");

			CollectionMemberBinding adopted = new CollectionMemberBinding(
				association, CollectionMemberKey.FromProvider("member-1"), nativeMod,
				CollectionRecipeIdentity.FromFingerprint("recipe-sha256:aaa"), CollectionMemberBindingKind.AdoptedExisting);
			CollectionMemberBinding installed = new CollectionMemberBinding(
				association, CollectionMemberKey.FromProvider("member-2"), nativeMod,
				CollectionRecipeIdentity.FromFingerprint("recipe-sha256:bbb"), CollectionMemberBindingKind.InstalledForCollection);

			Assert.That(adopted.BindingKind, Is.EqualTo(CollectionMemberBindingKind.AdoptedExisting));
			Assert.That(installed.BindingKind, Is.EqualTo(CollectionMemberBindingKind.InstalledForCollection));
			Assert.That(adopted.NativeMod, Is.SameAs(nativeMod));
			Assert.That(installed.NativeMod, Is.SameAs(nativeMod));
			Assert.That(adopted.VerifiedRecipe.Fingerprint, Is.EqualTo("recipe-sha256:aaa"));
		}

		[Test]
		public void MemberBinding_AllowsManyCollectionsToReferenceSameCompatibleNativeInstance()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-A");
			NativeModInstanceIdentity sharedNativeMod = new NativeModInstanceIdentity(target, "shared-native-key");
			CollectionTargetAssociation firstAssociation = CreateAssociation("2210", "772530", 100, target);
			CollectionTargetAssociation secondAssociation = CreateAssociation("2211", "800000", 7, target);

			CollectionMemberBinding first = new CollectionMemberBinding(
				firstAssociation, CollectionMemberKey.FromProvider("member-A"), sharedNativeMod,
				CollectionRecipeIdentity.FromFingerprint("recipe-sha256:same"), CollectionMemberBindingKind.AdoptedExisting);
			CollectionMemberBinding second = new CollectionMemberBinding(
				secondAssociation, CollectionMemberKey.FromProvider("member-B"), sharedNativeMod,
				CollectionRecipeIdentity.FromFingerprint("recipe-sha256:same"), CollectionMemberBindingKind.AdoptedExisting);

			Assert.That(first.NativeMod, Is.SameAs(second.NativeMod));
			Assert.That(first.Association, Is.Not.EqualTo(second.Association));
			Assert.That(first, Is.Not.EqualTo(second));
		}

		[Test]
		public void MemberBinding_RejectsCrossTargetMappingUnknownKindAndMissingRecipeIdentity()
		{
			CollectionTargetAssociation association = CreateAssociation(
				"2210", "772530", 100, CollectionTargetIdentity.FromFingerprint("target-A"));
			NativeModInstanceIdentity wrongTarget = new NativeModInstanceIdentity(
				CollectionTargetIdentity.FromFingerprint("target-B"), "native-key-1");
			NativeModInstanceIdentity correctTarget = new NativeModInstanceIdentity(association.Target, "native-key-1");
			CollectionMemberKey member = CollectionMemberKey.FromProvider("member-1");

			Assert.Throws<ArgumentException>(() => new CollectionMemberBinding(
				association, member, wrongTarget, CollectionRecipeIdentity.FromFingerprint("recipe-sha256:aaa"), CollectionMemberBindingKind.AdoptedExisting));
			Assert.Throws<ArgumentOutOfRangeException>(() => new CollectionMemberBinding(
				association, member, correctTarget, CollectionRecipeIdentity.FromFingerprint("recipe-sha256:aaa"), CollectionMemberBindingKind.Unknown));
			Assert.Throws<ArgumentNullException>(() => new CollectionMemberBinding(
				association, member, correctTarget, null, CollectionMemberBindingKind.AdoptedExisting));
		}

		[Test]
		public void StandaloneProvenance_UnknownIsConservativelyProtective()
		{
			NativeModInstanceIdentity nativeMod = new NativeModInstanceIdentity(
				CollectionTargetIdentity.FromFingerprint("target-A"), "native-key-1");
			NativeModProvenance unknown = new NativeModProvenance(nativeMod, StandaloneModUse.Unknown);
			NativeModProvenance verifiedCollectionOnly = new NativeModProvenance(nativeMod, StandaloneModUse.NoStandaloneUseVerified);
			NativeModProvenance explicitStandalone = new NativeModProvenance(nativeMod, StandaloneModUse.ExplicitStandaloneUse);

			Assert.That(unknown.StandaloneUseProtectsFromAutomaticRemoval, Is.True);
			Assert.That(verifiedCollectionOnly.StandaloneUseProtectsFromAutomaticRemoval, Is.False);
			Assert.That(explicitStandalone.StandaloneUseProtectsFromAutomaticRemoval, Is.True);
		}

		[Test]
		public void AssociationAndProvenanceModelsExposeNoPublicPropertySetters()
		{
			AssertNoPublicSetters(typeof(CollectionTargetIdentity));
			AssertNoPublicSetters(typeof(NativeModInstanceIdentity));
			AssertNoPublicSetters(typeof(CollectionRecipeIdentity));
			AssertNoPublicSetters(typeof(CollectionTargetAssociation));
			AssertNoPublicSetters(typeof(CollectionMemberBinding));
			AssertNoPublicSetters(typeof(NativeModProvenance));
		}

		private static CollectionTargetAssociation CreateAssociation(string collectionId, string revisionId,
			long revisionNumber, CollectionTargetIdentity target)
		{
			return new CollectionTargetAssociation(Guid.NewGuid(),
				CollectionRevisionIdentity.FromNexus(CollectionIdentity.FromNexus(collectionId), revisionId, revisionNumber),
				target, CollectionAssociationState.Applied);
		}

		private static void AssertNoPublicSetters(Type type)
		{
			foreach (var property in type.GetProperties())
				Assert.That(property.GetSetMethod(), Is.Null, type.Name + "." + property.Name + " must remain immutable.");
		}
	}
}
