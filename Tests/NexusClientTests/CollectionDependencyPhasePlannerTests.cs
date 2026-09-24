using System;
using System.Linq;
using Nexus.Client.CollectionManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class CollectionDependencyPhasePlannerTests
	{
		private const string Sha256A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

		[Test]
		public void Plan_SparsePhasesUseCanonicalIndependentOrderingAndVisibilityBarriers()
		{
			NormalizedCollectionMember z = CreateMember(0, "z", 0, CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			NormalizedCollectionMember a = CreateMember(1, "a", 0, CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			NormalizedCollectionMember middle = CreateMember(2, "middle", 666d, CollectionMemberRequirement.Optional, CollectionMemberSelection.Selected);
			NormalizedCollectionMember late = CreateMember(3, "late", 20d, CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			Fixture fixture = CreateFixture(new[] { z, a, middle, late }, null);

			CollectionDependencyPhasePlan result = new CollectionDependencyPhasePlanner().Plan(fixture.Plan, fixture.Matches);

			Assert.IsTrue(result.IsReady);
			Assert.AreEqual(new[] { 0d, 20d, 666d }, result.Phases.Select(x => x.PhaseNumber).ToArray());
			Assert.AreEqual(new[] { "a", "z" }, result.Phases[0].Members.Select(x => x.MemberKey.Value).ToArray(),
				"Independent members must use stable member identity rather than source/API order.");
			Assert.AreEqual("late", result.Phases[1].Members[0].MemberKey.Value);
			Assert.AreEqual("middle", result.Phases[2].Members[0].MemberKey.Value);
			Assert.AreEqual(2, result.Barriers.Count);
			Assert.AreEqual(0d, result.Barriers[0].CompletedPhase);
			Assert.AreEqual(20d, result.Barriers[0].NextPhase);
			Assert.AreEqual(CollectionPhaseBarrierKind.NativeStateVisibility, result.Barriers[0].Kind);
			Assert.AreEqual(20d, result.Barriers[1].CompletedPhase);
			Assert.AreEqual(666d, result.Barriers[1].NextPhase);
		}

		[Test]
		public void Plan_SamePhasePrerequisiteOverridesCanonicalTieBreak()
		{
			NormalizedCollectionMember dependent = CreateMember(0, "a-dependent", 5, CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			NormalizedCollectionMember prerequisite = CreateMember(1, "z-prerequisite", 5, CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			CollectionMemberDependency edge = new CollectionMemberDependency(prerequisite.IdentityResolution.Key,
				dependent.IdentityResolution.Key, CollectionMemberDependencyKind.InstallerPrerequisite);
			Fixture fixture = CreateFixture(new[] { dependent, prerequisite }, new[] { edge });

			CollectionDependencyPhasePlan result = new CollectionDependencyPhasePlanner().Plan(fixture.Plan, fixture.Matches);

			Assert.IsTrue(result.IsReady);
			Assert.AreEqual(new[] { "z-prerequisite", "a-dependent" },
				result.Phases.Single().Members.Select(x => x.MemberKey.Value).ToArray());
		}

		[Test]
		public void Plan_EarlierPhasePrerequisiteIsSatisfiedByVisibilityBarrier()
		{
			NormalizedCollectionMember prerequisite = CreateMember(0, "prerequisite", -2.5d,
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			NormalizedCollectionMember dependent = CreateMember(1, "dependent", 4.25d,
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			Fixture fixture = CreateFixture(new[] { dependent, prerequisite }, new[]
			{
				new CollectionMemberDependency(prerequisite.IdentityResolution.Key, dependent.IdentityResolution.Key,
					CollectionMemberDependencyKind.InstallerPrerequisite)
			});

			CollectionDependencyPhasePlan result = new CollectionDependencyPhasePlanner().Plan(fixture.Plan, fixture.Matches);

			Assert.IsTrue(result.IsReady);
			Assert.AreEqual(new[] { -2.5d, 4.25d }, result.Phases.Select(x => x.PhaseNumber).ToArray());
			Assert.AreEqual(1, result.Barriers.Count);
			Assert.AreEqual(-2.5d, result.Barriers[0].CompletedPhase);
			Assert.AreEqual(4.25d, result.Barriers[0].NextPhase);
		}

		[Test]
		public void Capability_SelectedMemberWithUnselectedPrerequisiteRequiresSelectionReview()
		{
			NormalizedCollectionMember prerequisite = CreateMember(0, "optional-prerequisite", 0,
				CollectionMemberRequirement.Optional, CollectionMemberSelection.Unselected);
			NormalizedCollectionMember dependent = CreateMember(1, "dependent", 0,
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			CollectionMemberDependency edge = new CollectionMemberDependency(prerequisite.IdentityResolution.Key,
				dependent.IdentityResolution.Key, CollectionMemberDependencyKind.InstallerPrerequisite);
			NormalizedCollectionManifest manifest = CreateManifest(new[] { prerequisite, dependent }, new[] { edge });

			CollectionCapabilityReport report = CollectionCapabilityReport.Create(manifest);

			Assert.AreEqual(CollectionCompatibilityStatus.ActionRequired, report.Status);
			Assert.IsTrue(report.AllIssues.Any(x => x.Code == "member.prerequisite-unselected"));
		}

		[Test]
		public void Plan_PrerequisiteInLaterPhaseBlocksWithoutProducingExecutablePhases()
		{
			NormalizedCollectionMember dependent = CreateMember(0, "dependent", 0, CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			NormalizedCollectionMember prerequisite = CreateMember(1, "prerequisite", 10, CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			Fixture fixture = CreateFixture(new[] { dependent, prerequisite }, new[]
			{
				new CollectionMemberDependency(prerequisite.IdentityResolution.Key, dependent.IdentityResolution.Key,
					CollectionMemberDependencyKind.InstallerPrerequisite)
			});

			CollectionDependencyPhasePlan result = new CollectionDependencyPhasePlanner().Plan(fixture.Plan, fixture.Matches);

			Assert.IsTrue(result.IsBlocked);
			Assert.AreEqual(0, result.Phases.Count);
			Assert.AreEqual(CollectionDependencyPhaseIssueKind.PhaseOrderingConflict, result.Issues.Single().Kind);
		}

		[Test]
		public void Plan_SamePhaseDependencyCycleBlocksDeterministically()
		{
			NormalizedCollectionMember a = CreateMember(0, "a", 3, CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			NormalizedCollectionMember b = CreateMember(1, "b", 3, CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			Fixture fixture = CreateFixture(new[] { b, a }, new[]
			{
				new CollectionMemberDependency(a.IdentityResolution.Key, b.IdentityResolution.Key, CollectionMemberDependencyKind.InstallerPrerequisite),
				new CollectionMemberDependency(b.IdentityResolution.Key, a.IdentityResolution.Key, CollectionMemberDependencyKind.InstallerPrerequisite)
			});

			CollectionDependencyPhasePlan result = new CollectionDependencyPhasePlanner().Plan(fixture.Plan, fixture.Matches);

			Assert.IsTrue(result.IsBlocked);
			Assert.AreEqual(0, result.Phases.Count);
			Assert.AreEqual(CollectionDependencyPhaseIssueKind.DependencyCycle, result.Issues.Single().Kind);
			Assert.AreEqual(3d, result.Issues.Single().Phase);
		}

		[Test]
		public void Plan_C6Point2BlockedMemberBlocksWholeDependencyPlan()
		{
			NormalizedCollectionMember member = CreateMember(0, "replacement", 0,
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			Fixture fixture = CreateFixture(new[] { member }, null,
				CollectionExecutionPolicy.ReplaceCurrentManagedSetup(CollectionReplacementBackupChoice.ContinueWithoutLocalCollection));

			Assert.IsTrue(fixture.Matches.HasBlockedMembers);
			CollectionDependencyPhasePlan result = new CollectionDependencyPhasePlanner().Plan(fixture.Plan, fixture.Matches);
			Assert.IsTrue(result.IsBlocked);
			Assert.AreEqual(CollectionDependencyPhaseIssueKind.BlockedMemberMatch, result.Issues.Single().Kind);
			Assert.AreEqual(0, result.Phases.Count);
		}

		[Test]
		public void Plan_MatchSetFromDifferentPlanIsRejected()
		{
			NormalizedCollectionMember member = CreateMember(0, "member", 0,
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			Fixture first = CreateFixture(new[] { member }, null, null, Guid.Parse("11111111-1111-1111-1111-111111111111"));
			Fixture second = CreateFixture(new[] { member }, null, null, Guid.Parse("22222222-2222-2222-2222-222222222222"));

			Assert.Throws<ArgumentException>(() => new CollectionDependencyPhasePlanner().Plan(first.Plan, second.Matches));
		}

		[Test]
		public void Manifest_DependencyEdgesAreImmutableValidatedAndDeduplicated()
		{
			NormalizedCollectionMember a = CreateMember(0, "a", 0, CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			NormalizedCollectionMember b = CreateMember(1, "b", 0, CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			CollectionMemberDependency edge = new CollectionMemberDependency(a.IdentityResolution.Key, b.IdentityResolution.Key,
				CollectionMemberDependencyKind.InstallerPrerequisite);
			NormalizedCollectionManifest manifest = CreateManifest(new[] { a, b }, new[] { edge });

			Assert.AreEqual(1, manifest.Dependencies.Count);
			Assert.Throws<NotSupportedException>(() => ((System.Collections.Generic.IList<CollectionMemberDependency>)manifest.Dependencies).Add(edge));
			Assert.Throws<ArgumentException>(() => CreateManifest(new[] { a, b }, new[] { edge, edge }));
			Assert.Throws<ArgumentException>(() => new CollectionMemberDependency(a.IdentityResolution.Key, a.IdentityResolution.Key,
				CollectionMemberDependencyKind.InstallerPrerequisite));
		}

		private static Fixture CreateFixture(NormalizedCollectionMember[] members, CollectionMemberDependency[] dependencies,
			CollectionExecutionPolicy policy = null, Guid? planId = null)
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c6-3-" + Guid.NewGuid().ToString("N"));
			CollectionNativeStateIndex state = CreateState(target);
			NormalizedCollectionManifest manifest = CreateManifest(members, dependencies);
			CollectionCapabilityReport report = CollectionCapabilityReport.Create(manifest);
			Assert.AreEqual(CollectionCompatibilityStatus.Supported, report.Status, "The test fixture must produce a resolvable selection.");
			ResolvedCollectionMemberPlan[] selected = members.Where(x => x.IsSelected)
				.Select(x => new ResolvedCollectionMemberPlan(x, CollectionResolvedArtifactChoice.Exact(x.Artifact))).ToArray();
			ResolvedCollectionPlan plan = new ResolvedCollectionPlan(
				CollectionPlanIdentity.From(planId ?? Guid.Parse("aaaaaaaa-3333-4444-5555-bbbbbbbbbbbb"), 1),
				target, policy ?? CollectionExecutionPolicy.InstallIntoCurrentSetup(), state.Fingerprint, report, selected);
			CollectionMemberMatchSet matches = new CollectionMemberMatchEngine().Match(plan, state);
			return new Fixture(plan, matches);
		}

		private static CollectionNativeStateIndex CreateState(CollectionTargetIdentity target)
		{
			return new CollectionNativeStateIndex(target,
				new CollectionNativeRootState[0], new CollectionNativeModState[0],
				new CollectionNativeFileState[0], new CollectionNativeIniState[0], new CollectionNativeGameValueState[0],
				new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable,
				new CollectionTargetAssociation[0], new CollectionMemberBinding[0], new UserOverride[0],
				CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
		}

		private static NormalizedCollectionMember CreateMember(int sourceOrdinal, string key, double phase,
			CollectionMemberRequirement requirement, CollectionMemberSelection selection)
		{
			return new NormalizedCollectionMember(sourceOrdinal,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider(key)),
				requirement, selection,
				new CollectionArtifactReference("nexus-mod-file", "skyrim/" + (100 + sourceOrdinal) + "/" + (200 + sourceOrdinal), null),
				CollectionRecipeIdentity.FromFingerprint("recipe-" + key), "Member " + key, phase);
		}

		private static NormalizedCollectionManifest CreateManifest(NormalizedCollectionMember[] members,
			CollectionMemberDependency[] dependencies)
		{
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(
				CollectionIdentity.FromNexus("collection-c6-3"), "revision-c6-3", 1);
			CollectionManifestSourceSnapshot source = new CollectionManifestSourceSnapshot(
				CollectionContentHash.FromSha256(Sha256A), 100, "schema-v1", "normalizer-v2");
			return new NormalizedCollectionManifest(revision, source, CollectionManifestMemberSetCompleteness.Complete,
				null, members, dependencies);
		}

		private sealed class Fixture
		{
			public Fixture(ResolvedCollectionPlan plan, CollectionMemberMatchSet matches)
			{
				Plan = plan;
				Matches = matches;
			}

			public ResolvedCollectionPlan Plan { get; }
			public CollectionMemberMatchSet Matches { get; }
		}
	}
}
