using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	[Category("CollectionsC12Workflow")]
	[Category("CollectionsManagement")]
	public class CollectionRevisionUpdateObsoleteEffectCoordinatorTests
	{
		private const string Sha256A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
		private const string Sha256B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

		[Test]
		public void QualifiedPlan_RemovedExclusiveMember_AllowsNativeRemoval()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture f = CreateFixture(root, "exclusive", true, false, false);
				CollectionRevisionUpdateObsoleteEffectPlan plan = CollectionRevisionUpdateObsoleteEffectCoordinator.BuildQualifiedPlan(
					f.Associations, f.Intent, f.UpdatePlan, f.State);

				CollectionRevisionUpdateObsoleteMemberAction action = plan.Actions.Single();
				Assert.That(action.Disposition, Is.EqualTo(CollectionRevisionUpdateObsoleteMemberDisposition.RemoveExclusiveNativeMod));
				Assert.That(action.RequiresNativeRemoval, Is.True);
				Assert.That(plan.RequiresNativeMutation, Is.True);
			}
			finally { Directory.Delete(root, true); }
		}

		/// <summary>A reviewed independent installation is kept, and changed provenance invalidates that exact review.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void QualifiedPlan_RemovedIndependentMember_PreservesOnlyTheReviewedInstallation(bool changeProvenance)
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture f = CreateFixture(root, "independent", true, false, false, false, StandaloneModUse.ExplicitStandaloneUse);
				CollectionRevisionUpdateMemberPlan member = f.UpdatePlan.Members.Single();
				Assert.That(member.Disposition, Is.EqualTo(CollectionRevisionUpdateDisposition.PreserveStandalone));
				if (changeProvenance)
				{
					f.Associations.SaveNativeModProvenance(new NativeModProvenance(member.Binding.NativeMod, StandaloneModUse.NoStandaloneUseVerified));
					Assert.Throws<InvalidOperationException>(() => CollectionRevisionUpdateObsoleteEffectCoordinator.BuildQualifiedPlan(
						f.Associations, f.Intent, f.UpdatePlan, f.State));
				}
				else
				{
					CollectionRevisionUpdateObsoleteEffectPlan plan = CollectionRevisionUpdateObsoleteEffectCoordinator.BuildQualifiedPlan(
						f.Associations, f.Intent, f.UpdatePlan, f.State);
					Assert.That(plan.Actions.Single().Disposition, Is.EqualTo(CollectionRevisionUpdateObsoleteMemberDisposition.PreserveStandalone));
					Assert.That(plan.RequiresNativeMutation, Is.False);
				}
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void QualifiedPlan_RemovedMemberSharedByAnotherCollection_PreservesNativeMod()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture f = CreateFixture(root, "shared", true, true, false);
				CollectionRevisionUpdateObsoleteEffectPlan plan = CollectionRevisionUpdateObsoleteEffectCoordinator.BuildQualifiedPlan(
					f.Associations, f.Intent, f.UpdatePlan, f.State);

				Assert.That(plan.Actions.Single().Disposition, Is.EqualTo(CollectionRevisionUpdateObsoleteMemberDisposition.PreserveSharedNativeMod));
				Assert.That(plan.RequiresNativeMutation, Is.False);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void QualifiedPlan_RemovedMemberWithApprovedOverride_DefersToOverridePreservation()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture f = CreateFixture(root, "override", true, false, true);
				CollectionRevisionUpdateObsoleteEffectPlan plan = CollectionRevisionUpdateObsoleteEffectCoordinator.BuildQualifiedPlan(
					f.Associations, f.Intent, f.UpdatePlan, f.State);

				Assert.That(plan.Actions.Single().Disposition, Is.EqualTo(CollectionRevisionUpdateObsoleteMemberDisposition.PreserveForOverride));
				Assert.That(plan.DeferredOverrideMembers.Single(), Is.EqualTo(f.MemberKey));
				Assert.That(plan.RequiresNativeMutation, Is.False);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void QualifiedPlan_RemovedMemberWithApprovedUnscopedOverride_PreservesNativeMod()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture f = CreateFixture(root, "global-override", true, false, true, true);
				Assert.That(f.UpdatePlan.Members.Single().Disposition, Is.EqualTo(CollectionRevisionUpdateDisposition.RemoveFromOldRevision));

				CollectionRevisionUpdateObsoleteEffectPlan plan = CollectionRevisionUpdateObsoleteEffectCoordinator.BuildQualifiedPlan(
					f.Associations, f.Intent, f.UpdatePlan, f.State);

				Assert.That(plan.Actions.Single().Disposition, Is.EqualTo(CollectionRevisionUpdateObsoleteMemberDisposition.PreserveForOverride));
				Assert.That(plan.RequiresNativeMutation, Is.False);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void QualifiedPlan_ChangedMemberWithRemovedEffect_DefersToCandidateReinstall()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture f = CreateChangedFixture(root, "changed");
				CollectionRevisionUpdateObsoleteEffectPlan plan = CollectionRevisionUpdateObsoleteEffectCoordinator.BuildQualifiedPlan(
					f.Associations, f.Intent, f.UpdatePlan, f.State);

				Assert.That(plan.Actions.Single().Disposition, Is.EqualTo(CollectionRevisionUpdateObsoleteMemberDisposition.DeferToCandidateReinstall));
				Assert.That(plan.RequiresNativeMutation, Is.False);
			}
			finally { Directory.Delete(root, true); }
		}

		private static Fixture CreateFixture(string root, string suffix, bool removed, bool shared, bool withOverride, bool unscopedOverride = false,
			StandaloneModUse standaloneUse = StandaloneModUse.NoStandaloneUseVerified)
		{
			var store = new CollectionsStore(root);
			store.CreateNew();
			var associations = new CollectionsAssociationStore(store);
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c10-4-" + suffix);
			CollectionIdentity collection = CollectionIdentity.FromNexus("collection-c10-4-" + suffix);
			CollectionRevisionIdentity oldRevision = CollectionRevisionIdentity.FromNexus(collection, "old", 1);
			CollectionRevisionIdentity newRevision = CollectionRevisionIdentity.FromNexus(collection, "new", 2);
			PersistRevision(store, oldRevision);
			PersistRevision(store, newRevision);
			var association = new CollectionTargetAssociation(Guid.NewGuid(), oldRevision, target,
				withOverride ? CollectionAssociationState.Modified : CollectionAssociationState.Applied);
			associations.SaveAssociation(association);
			CollectionMemberKey key = CollectionMemberKey.FromProvider("member-a");
			NormalizedCollectionMember oldMember = CreateMember(key.Value, "100", "200", "recipe-a");
			NormalizedCollectionMember newMember = removed ? null : CreateMember(key.Value, "100", "200", "recipe-a");
			var nativeIdentity = new NativeModInstanceIdentity(target, "native-a");
			var native = new CollectionNativeModState(nativeIdentity, "C:\\Mods\\a.7z", "a.7z", "100", "200", "1", "1",
				ModInstallRoot.Data, ModInstallMethod.Virtual);
			var binding = new CollectionMemberBinding(association, key, nativeIdentity, oldMember.RecipeIdentity,
				CollectionMemberBindingKind.InstalledForCollection);
			associations.SaveBinding(binding);
			associations.SaveNativeModProvenance(new NativeModProvenance(nativeIdentity, standaloneUse));

			var associationList = new List<CollectionTargetAssociation> { association };
			var bindingList = new List<CollectionMemberBinding> { binding };
			if (shared)
			{
				CollectionIdentity otherCollection = CollectionIdentity.FromNexus("other-" + suffix);
				CollectionRevisionIdentity otherRevision = CollectionRevisionIdentity.FromNexus(otherCollection, "r1", 1);
				PersistRevision(store, otherRevision);
				var other = new CollectionTargetAssociation(Guid.NewGuid(), otherRevision, target, CollectionAssociationState.Applied);
				associations.SaveAssociation(other);
				var otherBinding = new CollectionMemberBinding(other, CollectionMemberKey.FromProvider("other-member"), nativeIdentity,
					oldMember.RecipeIdentity, CollectionMemberBindingKind.AdoptedExisting);
				associations.SaveBinding(otherBinding);
				associationList.Add(other);
				bindingList.Add(otherBinding);
			}

			var overrideList = new List<UserOverride>();
			if (withOverride)
			{
				CollectionRequirementReference requirement = unscopedOverride
					? new CollectionRequirementReference(association, null, CollectionRequirementAspect.FileWinner, "data/global.dds")
					: new CollectionRequirementReference(association, key, CollectionRequirementAspect.MemberEnabledState, null);
				var localOverride = new UserOverride(Guid.NewGuid(), requirement,
					CollectionRequirementState.Present("bool-v1", "enabled"), CollectionRequirementState.Present("bool-v1", "disabled"), "preserve local state");
				associations.SaveOverride(localOverride);
				overrideList.Add(localOverride);
			}

			CollectionNativeStateIndex state = CreateState(target, native, associationList, bindingList, overrideList);
			ResolvedCollectionPlan oldPlan = CreatePlan(oldRevision, target, oldMember, state.Fingerprint, Sha256A);
			ResolvedCollectionPlan newPlan = CreatePlan(newRevision, target, newMember, state.Fingerprint, Sha256B);
			CollectionRevisionUpdatePlan update = new CollectionRevisionUpdatePlanner().Plan(association, oldPlan, newPlan, state,
				overrideList, new CollectionDriftObservation[0], new[] { new NativeModProvenance(nativeIdentity, standaloneUse) });
			CollectionRevisionUpdateReviewedIntent intent = CollectionRevisionUpdateReviewedIntent.Create(update);
			return new Fixture(associations, key, state, update, intent);
		}

		private static Fixture CreateChangedFixture(string root, string suffix)
		{
			var store = new CollectionsStore(root);
			store.CreateNew();
			var associations = new CollectionsAssociationStore(store);
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c10-4-" + suffix);
			CollectionIdentity collection = CollectionIdentity.FromNexus("collection-c10-4-" + suffix);
			CollectionRevisionIdentity oldRevision = CollectionRevisionIdentity.FromNexus(collection, "old", 1);
			CollectionRevisionIdentity newRevision = CollectionRevisionIdentity.FromNexus(collection, "new", 2);
			PersistRevision(store, oldRevision);
			PersistRevision(store, newRevision);
			var association = new CollectionTargetAssociation(Guid.NewGuid(), oldRevision, target, CollectionAssociationState.Applied);
			associations.SaveAssociation(association);
			CollectionMemberKey key = CollectionMemberKey.FromProvider("member-a");
			NormalizedCollectionMember oldMember = CreateMember(key.Value, "100", "200", "recipe-a");
			NormalizedCollectionMember newMember = CreateMember(key.Value, "100", "201", "recipe-b");
			var nativeIdentity = new NativeModInstanceIdentity(target, "native-a");
			var native = new CollectionNativeModState(nativeIdentity, "C:\\Mods\\a.7z", "a.7z", "100", "200", "1", "1",
				ModInstallRoot.Data, ModInstallMethod.Virtual);
			var binding = new CollectionMemberBinding(association, key, nativeIdentity, oldMember.RecipeIdentity,
				CollectionMemberBindingKind.InstalledForCollection);
			associations.SaveBinding(binding);
			associations.SaveNativeModProvenance(new NativeModProvenance(nativeIdentity, StandaloneModUse.NoStandaloneUseVerified));
			CollectionNativeStateIndex state = CreateState(target, native, new[] { association }, new[] { binding }, new UserOverride[0]);
			ResolvedCollectionPlan oldPlan = CreatePlan(oldRevision, target, oldMember, state.Fingerprint, Sha256A);
			ResolvedCollectionPlan newPlan = CreatePlan(newRevision, target, newMember, state.Fingerprint, Sha256B);
			var oldPreview = new CollectionMemberEffectPreview(key, oldMember.RecipeIdentity, ModInstallMethod.Virtual, ModInstallRoot.Data,
				new[] { new CollectionPlannedFileEffect(ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "old.dds")) },
				new CollectionPlannedIniEffect[0], new CollectionPlannedGameValueEffect[0], new CollectionPlannedPluginEffect[0], new CollectionEffectPreviewIssue[0]);
			var newPreview = new CollectionMemberEffectPreview(key, newMember.RecipeIdentity, ModInstallMethod.Virtual, ModInstallRoot.Data,
				new[] { new CollectionPlannedFileEffect(ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "new.dds")) },
				new CollectionPlannedIniEffect[0], new CollectionPlannedGameValueEffect[0], new CollectionPlannedPluginEffect[0], new CollectionEffectPreviewIssue[0]);
			CollectionRevisionUpdatePlan update = new CollectionRevisionUpdatePlanner().Plan(association, oldPlan, newPlan, state,
				new UserOverride[0], new CollectionDriftObservation[0], new[] { new NativeModProvenance(nativeIdentity, StandaloneModUse.NoStandaloneUseVerified) },
				null, null,
				new Dictionary<CollectionMemberKey, CollectionMemberEffectPreview> { { key, oldPreview } },
				new Dictionary<CollectionMemberKey, CollectionMemberEffectPreview> { { key, newPreview } });
			CollectionRevisionUpdateReviewedIntent intent = CollectionRevisionUpdateReviewedIntent.Create(update);
			return new Fixture(associations, key, state, update, intent);
		}

		private static CollectionNativeStateIndex CreateState(CollectionTargetIdentity target, CollectionNativeModState native,
			IEnumerable<CollectionTargetAssociation> associations, IEnumerable<CollectionMemberBinding> bindings,
			IEnumerable<UserOverride> overrides)
		{
			return new CollectionNativeStateIndex(target, new CollectionNativeRootState[0], new[] { native },
				new CollectionNativeFileState[0], new CollectionNativeIniState[0], new CollectionNativeGameValueState[0],
				new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable, associations, bindings, overrides,
				CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
		}

		private static NormalizedCollectionMember CreateMember(string memberKey, string modId, string fileId, string recipe)
		{
			return new NormalizedCollectionMember(0,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider(memberKey)),
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected,
				new CollectionArtifactReference("nexus-mod-file", "skyrimspecialedition/" + modId + "/" + fileId, null),
				CollectionRecipeIdentity.FromFingerprint(recipe), memberKey);
		}

		private static ResolvedCollectionPlan CreatePlan(CollectionRevisionIdentity revision, CollectionTargetIdentity target,
			NormalizedCollectionMember member, CollectionCurrentStateFingerprint fingerprint, string sha256)
		{
			var source = new CollectionManifestSourceSnapshot(CollectionContentHash.FromSha256(sha256), 100, "schema-v1", "normalizer-v1");
			NormalizedCollectionMember[] members = member == null ? new NormalizedCollectionMember[0] : new[] { member };
			var manifest = new NormalizedCollectionManifest(revision, source, CollectionManifestMemberSetCompleteness.Complete, null, members);
			CollectionCapabilityReport report = CollectionCapabilityReport.Create(manifest);
			ResolvedCollectionMemberPlan[] resolved = member == null ? new ResolvedCollectionMemberPlan[0] :
				new[] { new ResolvedCollectionMemberPlan(member, CollectionResolvedArtifactChoice.Exact(member.Artifact)) };
			return new ResolvedCollectionPlan(CollectionPlanIdentity.From(Guid.NewGuid(), 1), target,
				CollectionExecutionPolicy.InstallIntoCurrentSetup(), fingerprint, report, resolved);
		}

		private static void PersistRevision(CollectionsStore store, CollectionRevisionIdentity revision)
		{
			var catalog = new CollectionsCatalogStore(store);
			catalog.SaveDefinitionAndRevision(new CollectionDefinition(revision.Collection, revision.Collection.StableId, null, null),
				new CollectionRevision(revision, revision.StableRevisionId, null, null));
		}

		private static string CreateTemporaryDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "nmm-c10-4-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}

		private sealed class Fixture
		{
			public Fixture(CollectionsAssociationStore associations, CollectionMemberKey memberKey,
				CollectionNativeStateIndex state, CollectionRevisionUpdatePlan updatePlan,
				CollectionRevisionUpdateReviewedIntent intent)
			{
				Associations = associations;
				MemberKey = memberKey;
				State = state;
				UpdatePlan = updatePlan;
				Intent = intent;
			}
			public CollectionsAssociationStore Associations { get; }
			public CollectionMemberKey MemberKey { get; }
			public CollectionNativeStateIndex State { get; }
			public CollectionRevisionUpdatePlan UpdatePlan { get; }
			public CollectionRevisionUpdateReviewedIntent Intent { get; }
		}
	}
}
