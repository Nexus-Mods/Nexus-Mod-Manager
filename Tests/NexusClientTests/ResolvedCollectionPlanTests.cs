using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.CollectionManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class ResolvedCollectionPlanTests
	{
		private const string Sha256A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

		[Test]
		public void SupportedSelection_ProducesExactImmutablePlanContract()
		{
			NormalizedCollectionMember required = CreateMember(0, "required", CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			NormalizedCollectionMember optional = CreateMember(1, "optional", CollectionMemberRequirement.Optional, CollectionMemberSelection.Unselected);
			CollectionCapabilityReport report = CollectionCapabilityReport.Create(CreateManifest(required, optional));
			ResolvedCollectionPlan plan = CreatePlan(
				report,
				CollectionExecutionPolicy.InstallIntoCurrentSetup(),
				new ResolvedCollectionMemberPlan(required, CollectionResolvedArtifactChoice.Exact(required.Artifact)));

			Assert.That(plan.Revision, Is.EqualTo(report.Manifest.Revision));
			Assert.That(plan.ManifestSource, Is.EqualTo(report.Manifest.Source));
			Assert.That(plan.SelectedMembers.Count, Is.EqualTo(1));
			Assert.That(plan.SelectedMembers[0].MemberKey, Is.EqualTo(required.IdentityResolution.Key));
			Assert.That(plan.SelectedMembers[0].ArtifactChoice.SelectedArtifact, Is.EqualTo(required.Artifact));
			Assert.That(plan.SelectedMembers[0].RecipeIdentity, Is.EqualTo(required.RecipeIdentity));
			Assert.That(plan.HasResolvedPolicyDecisions, Is.True);
		}

		[Test]
		public void UnsupportedOrActionRequiredSelection_CannotBecomeResolvedPlan()
		{
			NormalizedCollectionMember required = CreateMember(0, "required", CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			NormalizedCollectionManifest manifest = CreateManifest(required);
			CollectionCapabilityReport actionRequired = CollectionCapabilityReport.Create(manifest, new[]
			{
				CollectionCapabilityIssue.ForMember(
					CollectionCompatibilityStatus.ActionRequired,
					"member.manual-action",
					"Manual input is required.",
					required)
			});
			CollectionCapabilityReport unsupported = CollectionCapabilityReport.Create(manifest, new[]
			{
				CollectionCapabilityIssue.ForMember(
					CollectionCompatibilityStatus.Unsupported,
					"recipe.unsupported",
					"The recipe is unsupported.",
					required)
			});
			ResolvedCollectionMemberPlan memberPlan = new ResolvedCollectionMemberPlan(required, CollectionResolvedArtifactChoice.Exact(required.Artifact));

			Assert.Throws<ArgumentException>(() => CreatePlan(actionRequired, CollectionExecutionPolicy.InstallIntoCurrentSetup(), memberPlan));
			Assert.Throws<ArgumentException>(() => CreatePlan(unsupported, CollectionExecutionPolicy.InstallIntoCurrentSetup(), memberPlan));
		}

		[Test]
		public void SelectedClosure_MustContainEveryAndOnlySelectedManifestMember()
		{
			NormalizedCollectionMember first = CreateMember(0, "a", CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			NormalizedCollectionMember second = CreateMember(1, "b", CollectionMemberRequirement.Optional, CollectionMemberSelection.Selected);
			NormalizedCollectionMember unselected = CreateMember(2, "c", CollectionMemberRequirement.Optional, CollectionMemberSelection.Unselected);
			CollectionCapabilityReport report = CollectionCapabilityReport.Create(CreateManifest(first, second, unselected));
			ResolvedCollectionMemberPlan firstPlan = ExactPlan(first);
			ResolvedCollectionMemberPlan secondPlan = ExactPlan(second);

			Assert.Throws<ArgumentException>(() => CreatePlan(report, CollectionExecutionPolicy.InstallIntoCurrentSetup(), firstPlan));
			Assert.Throws<ArgumentException>(() => CreatePlan(report, CollectionExecutionPolicy.InstallIntoCurrentSetup(), firstPlan, firstPlan, secondPlan));
			Assert.Throws<ArgumentException>(() => new ResolvedCollectionMemberPlan(
				unselected, CollectionResolvedArtifactChoice.Exact(unselected.Artifact)));
		}

		[Test]
		public void CanonicalMemberStorageOrder_DoesNotInheritSourceOrCallerOrder()
		{
			NormalizedCollectionMember b = CreateMember(0, "b", CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			NormalizedCollectionMember a = CreateMember(1, "a", CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			CollectionCapabilityReport report = CollectionCapabilityReport.Create(CreateManifest(b, a));

			ResolvedCollectionPlan plan = CreatePlan(
				report,
				CollectionExecutionPolicy.InstallIntoCurrentSetup(),
				ExactPlan(b),
				ExactPlan(a));

			Assert.That(plan.SelectedMembers.Select(item => item.MemberKey.Value).ToArray(), Is.EqualTo(new[] { "a", "b" }));
		}

		[Test]
		public void SupportedSubstitution_PersistsRequestedSelectedArtifactAndRule()
		{
			NormalizedCollectionMember member = CreateMember(0, "member", CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			CollectionArtifactReference substitute = new CollectionArtifactReference("nexusmods.file", "artifact-substitute", null);
			CollectionResolvedArtifactChoice choice = CollectionResolvedArtifactChoice.SupportedSubstitution(
				member.Artifact, substitute, "same-content-compatible-file");
			ResolvedCollectionMemberPlan memberPlan = new ResolvedCollectionMemberPlan(member, choice);
			ResolvedCollectionPlan plan = CreatePlan(
				CollectionCapabilityReport.Create(CreateManifest(member)),
				CollectionExecutionPolicy.InstallIntoCurrentSetup(),
				memberPlan);

			Assert.That(plan.SelectedMembers.Single().ArtifactChoice.IsSubstitution, Is.True);
			Assert.That(plan.SelectedMembers.Single().ArtifactChoice.RequestedArtifact, Is.EqualTo(member.Artifact));
			Assert.That(plan.SelectedMembers.Single().ArtifactChoice.SelectedArtifact, Is.EqualTo(substitute));
			Assert.That(plan.SelectedMembers.Single().ArtifactChoice.SubstitutionRuleId, Is.EqualTo("same-content-compatible-file"));
		}

		[Test]
		public void ArtifactChoice_RejectsFakeOrUnidentifiedSubstitution()
		{
			CollectionArtifactReference requested = new CollectionArtifactReference("test", "requested", null);
			CollectionArtifactReference selected = new CollectionArtifactReference("test", "selected", null);

			Assert.Throws<ArgumentException>(() => CollectionResolvedArtifactChoice.SupportedSubstitution(requested, requested, "rule"));
			Assert.Throws<ArgumentException>(() => CollectionResolvedArtifactChoice.SupportedSubstitution(requested, selected, " "));
		}

		[Test]
		public void MemberPlan_RejectsArtifactDecisionFromDifferentManifestMember()
		{
			NormalizedCollectionMember first = CreateMember(0, "a", CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			NormalizedCollectionMember second = CreateMember(1, "b", CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);

			Assert.Throws<ArgumentException>(() => new ResolvedCollectionMemberPlan(
				first, CollectionResolvedArtifactChoice.Exact(second.Artifact)));
		}

		[Test]
		public void VersionedStateFingerprint_IsExactOrdinalValueObject()
		{
			CollectionCurrentStateFingerprint first = new CollectionCurrentStateFingerprint("managed-state-v1", "ABC");
			CollectionCurrentStateFingerprint same = new CollectionCurrentStateFingerprint("managed-state-v1", "ABC");
			CollectionCurrentStateFingerprint differentCase = new CollectionCurrentStateFingerprint("managed-state-v1", "abc");

			Assert.That(first, Is.EqualTo(same));
			Assert.That(first, Is.Not.EqualTo(differentCase));
			Assert.That(first.FormatVersion, Is.EqualTo("managed-state-v1"));
			Assert.That(first.Value, Is.EqualTo("ABC"));
		}

		[Test]
		public void PlanIdentity_RequiresVersionAndExplicitlyAdvances()
		{
			Guid id = Guid.Parse("11111111-2222-3333-4444-555555555555");
			CollectionPlanIdentity first = CollectionPlanIdentity.From(id, 1);
			CollectionPlanIdentity second = first.NextVersion();

			Assert.That(second.PlanId, Is.EqualTo(id));
			Assert.That(second.Version, Is.EqualTo(2));
			Assert.That(second, Is.Not.EqualTo(first));
			Assert.Throws<ArgumentException>(() => CollectionPlanIdentity.From(Guid.Empty, 1));
			Assert.Throws<ArgumentOutOfRangeException>(() => CollectionPlanIdentity.From(id, 0));
		}

		[Test]
		public void ReplacementBackupDecision_RemainsExplicitPolicyGateButDoesNotPreventPlanning()
		{
			NormalizedCollectionMember member = CreateMember(0, "member", CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			CollectionCapabilityReport report = CollectionCapabilityReport.Create(CreateManifest(member));
			ResolvedCollectionPlan undecided = CreatePlan(
				report,
				CollectionExecutionPolicy.ReplaceCurrentManagedSetup(),
				ExactPlan(member));
			ResolvedCollectionPlan decided = CreatePlan(
				report,
				CollectionExecutionPolicy.ReplaceCurrentManagedSetup(CollectionReplacementBackupChoice.ContinueWithoutLocalCollection),
				ExactPlan(member));

			Assert.That(undecided.HasResolvedPolicyDecisions, Is.False);
			Assert.That(decided.HasResolvedPolicyDecisions, Is.True);
		}

		[Test]
		public void PlanModels_AreImmutableAndSelectedMemberCollectionIsReadOnly()
		{
			NormalizedCollectionMember member = CreateMember(0, "member", CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			ResolvedCollectionPlan plan = CreatePlan(
				CollectionCapabilityReport.Create(CreateManifest(member)),
				CollectionExecutionPolicy.InstallIntoCurrentSetup(),
				ExactPlan(member));

			AssertNoPublicSetters(typeof(CollectionPlanIdentity));
			AssertNoPublicSetters(typeof(CollectionCurrentStateFingerprint));
			AssertNoPublicSetters(typeof(CollectionResolvedArtifactChoice));
			AssertNoPublicSetters(typeof(ResolvedCollectionMemberPlan));
			AssertNoPublicSetters(typeof(ResolvedCollectionPlan));
			Assert.Throws<NotSupportedException>(() => ((IList<ResolvedCollectionMemberPlan>)plan.SelectedMembers).Add(plan.SelectedMembers[0]));
		}

		private static ResolvedCollectionMemberPlan ExactPlan(NormalizedCollectionMember member)
		{
			return new ResolvedCollectionMemberPlan(member, CollectionResolvedArtifactChoice.Exact(member.Artifact));
		}

		private static ResolvedCollectionPlan CreatePlan(
			CollectionCapabilityReport report,
			CollectionExecutionPolicy policy,
			params ResolvedCollectionMemberPlan[] members)
		{
			return new ResolvedCollectionPlan(
				CollectionPlanIdentity.From(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), 1),
				CollectionTargetIdentity.FromFingerprint("target-1"),
				policy,
				new CollectionCurrentStateFingerprint("managed-state-v1", "state-1"),
				report,
				members);
		}

		private static NormalizedCollectionMember CreateMember(
			int sourceOrdinal,
			string key,
			CollectionMemberRequirement requirement,
			CollectionMemberSelection selection)
		{
			return new NormalizedCollectionMember(
				sourceOrdinal,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider(key)),
				requirement,
				selection,
				new CollectionArtifactReference("nexusmods.file", "artifact-" + key, null),
				CollectionRecipeIdentity.FromFingerprint("recipe-" + key),
				"Member " + key);
		}

		private static NormalizedCollectionManifest CreateManifest(params NormalizedCollectionMember[] members)
		{
			CollectionIdentity collection = CollectionIdentity.FromNexus("collection-1");
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "revision-1", 1);
			CollectionManifestSourceSnapshot source = new CollectionManifestSourceSnapshot(
				CollectionContentHash.FromSha256(Sha256A), 100, "schema-v1", "normalizer-v1");
			return new NormalizedCollectionManifest(
				revision,
				source,
				CollectionManifestMemberSetCompleteness.Complete,
				null,
				members);
		}

		private static void AssertNoPublicSetters(Type type)
		{
			foreach (var property in type.GetProperties())
				Assert.That(property.GetSetMethod(), Is.Null, type.Name + "." + property.Name + " must remain immutable.");
		}
	}
}
