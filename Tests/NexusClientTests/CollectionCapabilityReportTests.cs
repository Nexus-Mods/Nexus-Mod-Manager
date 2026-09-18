using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.CollectionManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class CollectionCapabilityReportTests
	{
		private const string Sha256A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

		[Test]
		public void CompleteSelectedMembersWithoutIssues_AreSupported()
		{
			NormalizedCollectionMember member = CreateCompleteMember(
				0, "member-a", CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			CollectionCapabilityReport report = CollectionCapabilityReport.Create(CreateManifest(
				CollectionManifestMemberSetCompleteness.Complete, null, member));

			Assert.That(report.Status, Is.EqualTo(CollectionCompatibilityStatus.Supported));
			Assert.That(report.IsSupported, Is.True);
			Assert.That(report.ManifestIssues, Is.Empty);
			Assert.That(report.MemberReports.Single().Status, Is.EqualTo(CollectionCompatibilityStatus.Supported));
			Assert.That(report.AllIssues, Is.Empty);
		}

		[Test]
		public void IncompleteMemberSet_IsActionRequiredAndPreservesReason()
		{
			CollectionCapabilityReport report = CollectionCapabilityReport.Create(CreateManifest(
				CollectionManifestMemberSetCompleteness.Incomplete,
				"Provider pagination did not complete.",
				CreateCompleteMember(0, "member-a", CollectionMemberRequirement.Required, CollectionMemberSelection.Selected)));

			Assert.That(report.Status, Is.EqualTo(CollectionCompatibilityStatus.ActionRequired));
			Assert.That(report.IsSupported, Is.False);
			Assert.That(report.ManifestIssues.Count, Is.EqualTo(1));
			Assert.That(report.ManifestIssues[0].Code, Is.EqualTo("manifest.member-set-incomplete"));
			Assert.That(report.ManifestIssues[0].Reason, Does.Contain("pagination"));
		}

		[Test]
		public void SelectedIncompleteMember_ReportsIdentityArtifactAndRecipePrecisely()
		{
			NormalizedCollectionMember member = new NormalizedCollectionMember(
				4,
				CollectionMemberIdentityResolution.Missing("No stable member identifier was present."),
				CollectionMemberRequirement.Required,
				CollectionMemberSelection.Selected,
				null,
				null,
				"Incomplete member");

			CollectionCapabilityReport report = CollectionCapabilityReport.Create(CreateManifest(
				CollectionManifestMemberSetCompleteness.Complete, null, member));
			CollectionMemberCapabilityReport memberReport = report.MemberReports.Single();

			Assert.That(report.Status, Is.EqualTo(CollectionCompatibilityStatus.ActionRequired));
			Assert.That(memberReport.Status, Is.EqualTo(CollectionCompatibilityStatus.ActionRequired));
			Assert.That(memberReport.Issues.Select(issue => issue.Code), Is.EquivalentTo(new[]
			{
				"member.identity-missing",
				"member.artifact-missing",
				"member.recipe-identity-missing"
			}));
			Assert.That(memberReport.Issues.All(issue => issue.SourceOrdinal == 4), Is.True);
			Assert.That(memberReport.Issues.All(issue => issue.MemberKey == null), Is.True);
		}

		[Test]
		public void AmbiguousMemberIdentity_PreservesNormalizerReasonAndField()
		{
			NormalizedCollectionMember member = new NormalizedCollectionMember(
				3,
				CollectionMemberIdentityResolution.Ambiguous("Two candidates matched the same source record."),
				CollectionMemberRequirement.Optional,
				CollectionMemberSelection.Selected,
				new CollectionArtifactReference("test", "artifact-3", null),
				CollectionRecipeIdentity.FromFingerprint("recipe-3"),
				"Ambiguous member");

			CollectionCapabilityIssue issue = CollectionCapabilityReport.Create(CreateManifest(
				CollectionManifestMemberSetCompleteness.Complete, null, member)).AllIssues.Single();

			Assert.That(issue.Code, Is.EqualTo("member.identity-ambiguous"));
			Assert.That(issue.FieldPath, Is.EqualTo("identity"));
			Assert.That(issue.Reason, Does.Contain("Two candidates"));
		}

		[Test]
		public void RequiredOmission_IsActionRequiredDeviation()
		{
			NormalizedCollectionMember member = CreateCompleteMember(
				0, "required", CollectionMemberRequirement.Required, CollectionMemberSelection.Unselected);
			CollectionCapabilityReport report = CollectionCapabilityReport.Create(CreateManifest(
				CollectionManifestMemberSetCompleteness.Complete, null, member));

			Assert.That(report.Status, Is.EqualTo(CollectionCompatibilityStatus.ActionRequired));
			Assert.That(report.MemberReports.Single().Issues.Any(issue => issue.Code == "member.required-omitted"), Is.True);
		}

		[Test]
		public void UnsupportedUnselectedOptional_RemainsVisibleButDoesNotBlockCurrentSelection()
		{
			NormalizedCollectionMember required = CreateCompleteMember(
				0, "required", CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			NormalizedCollectionMember optional = CreateCompleteMember(
				1, "optional", CollectionMemberRequirement.Optional, CollectionMemberSelection.Unselected);
			NormalizedCollectionManifest manifest = CreateManifest(
				CollectionManifestMemberSetCompleteness.Complete, null, required, optional);
			CollectionCapabilityIssue unsupported = CollectionCapabilityIssue.ForMember(
				CollectionCompatibilityStatus.Unsupported,
				"recipe.unsupported-root-layout",
				"This root layout does not have a tested native adapter.",
				optional,
				"recipe.rootLayout");

			CollectionCapabilityReport report = CollectionCapabilityReport.Create(manifest, new[] { unsupported });

			Assert.That(report.Status, Is.EqualTo(CollectionCompatibilityStatus.Supported));
			Assert.That(report.IsSupported, Is.True);
			Assert.That(report.HasUnselectedUnsupportedOptionals, Is.True);
			Assert.That(report.MemberReports.Single(item => item.Member.SourceOrdinal == 1).Status,
				Is.EqualTo(CollectionCompatibilityStatus.Unsupported));
			Assert.That(report.AllIssues.Any(issue => issue.Code == "recipe.unsupported-root-layout"), Is.True);
		}

		[Test]
		public void SelectingUnsupportedOptional_MakesCurrentSelectionUnsupported()
		{
			NormalizedCollectionMember optional = CreateCompleteMember(
				1, "optional", CollectionMemberRequirement.Optional, CollectionMemberSelection.Selected);
			NormalizedCollectionManifest manifest = CreateManifest(
				CollectionManifestMemberSetCompleteness.Complete, null, optional);
			CollectionCapabilityIssue unsupported = CollectionCapabilityIssue.ForMember(
				CollectionCompatibilityStatus.Unsupported,
				"recipe.unsupported-tool",
				"Arbitrary tool execution is unsupported.",
				optional,
				"recipe.tool");

			CollectionCapabilityReport report = CollectionCapabilityReport.Create(manifest, new[] { unsupported });

			Assert.That(report.Status, Is.EqualTo(CollectionCompatibilityStatus.Unsupported));
			Assert.That(report.IsSupported, Is.False);
			Assert.That(report.HasUnselectedUnsupportedOptionals, Is.False);
		}

		[Test]
		public void WholeManifestUnsupportedIssue_BlocksEvenWhenOnlyOptionalMemberIsUnselected()
		{
			NormalizedCollectionMember optional = CreateCompleteMember(
				0, "optional", CollectionMemberRequirement.Optional, CollectionMemberSelection.Unselected);
			NormalizedCollectionManifest manifest = CreateManifest(
				CollectionManifestMemberSetCompleteness.Complete, null, optional);
			CollectionCapabilityIssue issue = CollectionCapabilityIssue.ForManifest(
				CollectionCompatibilityStatus.Unsupported,
				"manifest.unsafe-path",
				"A manifest path escapes the allowed collection scope.",
				"members[0].path");

			CollectionCapabilityReport report = CollectionCapabilityReport.Create(manifest, new[] { issue });

			Assert.That(report.Status, Is.EqualTo(CollectionCompatibilityStatus.Unsupported));
			Assert.That(report.ManifestIssues.Single().FieldPath, Is.EqualTo("members[0].path"));
		}

		[Test]
		public void MemberIssue_PreservesStableKeyOrdinalAndPreciseFieldReason()
		{
			NormalizedCollectionMember member = CreateCompleteMember(
				7, "member-7", CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			NormalizedCollectionManifest manifest = CreateManifest(
				CollectionManifestMemberSetCompleteness.Complete, null, member);
			CollectionCapabilityIssue issue = CollectionCapabilityIssue.ForMember(
				CollectionCompatibilityStatus.ActionRequired,
				"member.manual-prerequisite",
				"The required external prerequisite has not been acknowledged.",
				member,
				"requirements.external[0]");

			CollectionCapabilityIssue actual = CollectionCapabilityReport.Create(manifest, new[] { issue }).AllIssues.Single();

			Assert.That(actual.Target, Is.EqualTo(CollectionCapabilityIssueTarget.Member));
			Assert.That(actual.SourceOrdinal, Is.EqualTo(7));
			Assert.That(actual.MemberKey, Is.EqualTo(member.IdentityResolution.Key));
			Assert.That(actual.FieldPath, Is.EqualTo("requirements.external[0]"));
			Assert.That(actual.Reason, Does.Contain("external prerequisite"));
		}

		[Test]
		public void DeclaredMemberIssue_FromDifferentMemberIsRejected()
		{
			NormalizedCollectionMember member = CreateCompleteMember(
				0, "member-a", CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			NormalizedCollectionMember otherMember = CreateCompleteMember(
				0, "member-b", CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			NormalizedCollectionManifest manifest = CreateManifest(
				CollectionManifestMemberSetCompleteness.Complete, null, member);
			CollectionCapabilityIssue foreignIssue = CollectionCapabilityIssue.ForMember(
				CollectionCompatibilityStatus.Unsupported,
				"member.foreign",
				"This issue belongs to a different normalized member.",
				otherMember);

			Assert.Throws<ArgumentException>(() => CollectionCapabilityReport.Create(manifest, new[] { foreignIssue }));
		}

		[Test]
		public void IssueOrdering_IsDeterministicRegardlessOfDeclaredInputOrder()
		{
			NormalizedCollectionMember member = CreateCompleteMember(
				2, "member-a", CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			NormalizedCollectionManifest manifest = CreateManifest(
				CollectionManifestMemberSetCompleteness.Complete, null, member);
			CollectionCapabilityIssue z = CollectionCapabilityIssue.ForMember(
				CollectionCompatibilityStatus.Unsupported, "z-code", "Z reason.", member, "recipe.z");
			CollectionCapabilityIssue a = CollectionCapabilityIssue.ForMember(
				CollectionCompatibilityStatus.ActionRequired, "a-code", "A reason.", member, "recipe.a");

			string[] first = CollectionCapabilityReport.Create(manifest, new[] { z, a }).AllIssues.Select(issue => issue.Code).ToArray();
			string[] second = CollectionCapabilityReport.Create(manifest, new[] { a, z }).AllIssues.Select(issue => issue.Code).ToArray();

			Assert.That(first, Is.EqualTo(second));
			Assert.That(first, Is.EqualTo(new[] { "a-code", "z-code" }));
		}

		[Test]
		public void CapabilityIssue_RejectsSupportedOrUnknownIssueSeverity()
		{
			Assert.Throws<ArgumentOutOfRangeException>(() => CollectionCapabilityIssue.ForManifest(
				CollectionCompatibilityStatus.Supported, "not-an-issue", "Supported is not an issue severity."));
			Assert.Throws<ArgumentOutOfRangeException>(() => CollectionCapabilityIssue.ForManifest(
				CollectionCompatibilityStatus.Unknown, "unknown", "Unknown is not a completed issue severity."));
		}

		[Test]
		public void DuplicateSourceOrdinals_AreRejectedAtCapabilityReportingBoundary()
		{
			NormalizedCollectionMember first = CreateCompleteMember(
				0, "member-a", CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			NormalizedCollectionMember second = CreateCompleteMember(
				0, "member-b", CollectionMemberRequirement.Optional, CollectionMemberSelection.Unselected);
			NormalizedCollectionManifest manifest = CreateManifest(
				CollectionManifestMemberSetCompleteness.Complete, null, first, second);

			Assert.Throws<ArgumentException>(() => CollectionCapabilityReport.Create(manifest));
		}

		[Test]
		public void CapabilityModels_AreImmutableAndIssueCollectionsAreReadOnly()
		{
			NormalizedCollectionMember member = CreateCompleteMember(
				0, "member-a", CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			CollectionCapabilityReport report = CollectionCapabilityReport.Create(CreateManifest(
				CollectionManifestMemberSetCompleteness.Complete, null, member));

			AssertNoPublicSetters(typeof(CollectionCapabilityIssue));
			AssertNoPublicSetters(typeof(CollectionMemberCapabilityReport));
			AssertNoPublicSetters(typeof(CollectionCapabilityReport));
			Assert.Throws<NotSupportedException>(() => ((IList<CollectionMemberCapabilityReport>)report.MemberReports).Add(report.MemberReports[0]));
		}

		private static NormalizedCollectionMember CreateCompleteMember(
			int sourceOrdinal,
			string memberKey,
			CollectionMemberRequirement requirement,
			CollectionMemberSelection selection)
		{
			return new NormalizedCollectionMember(
				sourceOrdinal,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider(memberKey)),
				requirement,
				selection,
				new CollectionArtifactReference("nexusmods.file", "artifact-" + memberKey, null),
				CollectionRecipeIdentity.FromFingerprint("recipe-" + memberKey),
				"Member " + memberKey);
		}

		private static NormalizedCollectionManifest CreateManifest(
			CollectionManifestMemberSetCompleteness completeness,
			string incompletenessReason,
			params NormalizedCollectionMember[] members)
		{
			CollectionIdentity collection = CollectionIdentity.FromNexus("collection-1");
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "revision-1", 1);
			CollectionManifestSourceSnapshot source = new CollectionManifestSourceSnapshot(
				CollectionContentHash.FromSha256(Sha256A), 100, "schema-v1", "normalizer-v1");
			return new NormalizedCollectionManifest(revision, source, completeness, incompletenessReason, members);
		}

		private static void AssertNoPublicSetters(Type type)
		{
			foreach (var property in type.GetProperties())
				Assert.That(property.GetSetMethod(), Is.Null, type.Name + "." + property.Name + " must remain immutable.");
		}
	}
}
