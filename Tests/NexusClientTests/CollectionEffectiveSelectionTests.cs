using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nexus.Client.CollectionManagement;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class CollectionEffectiveSelectionTests
	{
		private const string Sha256A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

		[Test]
		public void Build_SelectsOptionalWithoutMutatingNormalizedManifestAndKeepsRequiredSelected()
		{
			NormalizedCollectionMember required = CreateMember(
				0, "required", CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			NormalizedCollectionMember optional = CreateMember(
				1, "optional", CollectionMemberRequirement.Optional, CollectionMemberSelection.Unselected);
			NormalizedCollectionManifest manifest = CreateManifest(new[] { required, optional }, null);
			CollectionCapabilityReport normalizedReport = CollectionCapabilityReport.Create(manifest);

			CollectionEffectiveSelection effective = new CollectionEffectiveSelectionBuilder().Build(
				normalizedReport,
				new[] { new CollectionOptionalMemberSelection(optional.IdentityResolution.Key, CollectionMemberSelection.Selected) });

			Assert.That(manifest.Members[0].IsSelected, Is.True);
			Assert.That(manifest.Members[1].IsSelected, Is.False, "The provider-normalized manifest must remain immutable.");
			Assert.That(effective.Manifest, Is.Not.SameAs(manifest));
			Assert.That(effective.Manifest.Members[0].IsSelected, Is.True);
			Assert.That(effective.Manifest.Members[1].IsSelected, Is.True);
			Assert.That(effective.CapabilityReport.Status, Is.EqualTo(CollectionCompatibilityStatus.Supported));
			Assert.That(effective.Manifest.Source, Is.SameAs(manifest.Source));
			Assert.That(effective.Manifest.Revision, Is.SameAs(manifest.Revision));
		}

		[Test]
		public void Build_SelectedOptionalStartsContributingDeclaredCapabilityIssues()
		{
			NormalizedCollectionMember required = CreateMember(
				0, "required", CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			NormalizedCollectionMember optional = CreateMember(
				1, "optional", CollectionMemberRequirement.Optional, CollectionMemberSelection.Unselected);
			NormalizedCollectionManifest manifest = CreateManifest(new[] { required, optional }, null);
			CollectionCapabilityIssue unsupported = CollectionCapabilityIssue.ForMember(
				CollectionCompatibilityStatus.Unsupported,
				"member.test-unsupported",
				"The optional member uses behavior which is not executable.",
				optional,
				"recipe.test");
			CollectionCapabilityReport normalizedReport = CollectionCapabilityReport.Create(manifest, new[] { unsupported });

			Assert.That(normalizedReport.Status, Is.EqualTo(CollectionCompatibilityStatus.Supported));
			CollectionEffectiveSelection effective = new CollectionEffectiveSelectionBuilder().Build(
				normalizedReport,
				new[] { new CollectionOptionalMemberSelection(optional.IdentityResolution.Key, CollectionMemberSelection.Selected) });

			Assert.That(effective.CapabilityReport.Status, Is.EqualTo(CollectionCompatibilityStatus.Unsupported));
			Assert.That(effective.CapabilityReport.AllIssues.Count(x => x.Code == "member.test-unsupported"), Is.EqualTo(1));
		}

		[Test]
		public void Build_RecalculatesSelectedDependencyClosure()
		{
			NormalizedCollectionMember prerequisite = CreateMember(
				0, "prerequisite", CollectionMemberRequirement.Optional, CollectionMemberSelection.Unselected);
			NormalizedCollectionMember dependent = CreateMember(
				1, "dependent", CollectionMemberRequirement.Optional, CollectionMemberSelection.Unselected);
			var dependency = new CollectionMemberDependency(
				prerequisite.IdentityResolution.Key,
				dependent.IdentityResolution.Key,
				CollectionMemberDependencyKind.InstallerPrerequisite);
			NormalizedCollectionManifest manifest = CreateManifest(new[] { prerequisite, dependent }, new[] { dependency });
			CollectionCapabilityReport normalizedReport = CollectionCapabilityReport.Create(manifest);
			var builder = new CollectionEffectiveSelectionBuilder();

			CollectionEffectiveSelection missingPrerequisite = builder.Build(
				normalizedReport,
				new[] { new CollectionOptionalMemberSelection(dependent.IdentityResolution.Key, CollectionMemberSelection.Selected) });
			Assert.That(missingPrerequisite.CapabilityReport.Status, Is.EqualTo(CollectionCompatibilityStatus.ActionRequired));
			Assert.That(missingPrerequisite.CapabilityReport.AllIssues.Any(x => x.Code == "member.prerequisite-unselected"), Is.True);

			CollectionEffectiveSelection completeClosure = builder.Build(
				normalizedReport,
				new[]
				{
					new CollectionOptionalMemberSelection(prerequisite.IdentityResolution.Key, CollectionMemberSelection.Selected),
					new CollectionOptionalMemberSelection(dependent.IdentityResolution.Key, CollectionMemberSelection.Selected)
				});
			Assert.That(completeClosure.CapabilityReport.Status, Is.EqualTo(CollectionCompatibilityStatus.Supported));
			Assert.That(completeClosure.CapabilityReport.AllIssues.Any(x => x.Code == "member.prerequisite-unselected"), Is.False);
		}

		[Test]
		public void Build_RejectsRequiredMemberToggle()
		{
			NormalizedCollectionMember required = CreateMember(
				0, "required", CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			CollectionCapabilityReport normalizedReport = CollectionCapabilityReport.Create(CreateManifest(new[] { required }, null));

			Assert.Throws<ArgumentException>(() => new CollectionEffectiveSelectionBuilder().Build(
				normalizedReport,
				new[] { new CollectionOptionalMemberSelection(required.IdentityResolution.Key, CollectionMemberSelection.Unselected) }));
		}

		[Test]
		public void Build_RejectsUnknownAndDuplicateOptionalDecisions()
		{
			NormalizedCollectionMember optional = CreateMember(
				0, "optional", CollectionMemberRequirement.Optional, CollectionMemberSelection.Unselected);
			CollectionCapabilityReport normalizedReport = CollectionCapabilityReport.Create(CreateManifest(new[] { optional }, null));
			var builder = new CollectionEffectiveSelectionBuilder();

			Assert.Throws<ArgumentException>(() => builder.Build(
				normalizedReport,
				new[] { new CollectionOptionalMemberSelection(CollectionMemberKey.FromProvider("unknown"), CollectionMemberSelection.Selected) }));
			Assert.Throws<ArgumentException>(() => builder.Build(
				normalizedReport,
				new[]
				{
					new CollectionOptionalMemberSelection(optional.IdentityResolution.Key, CollectionMemberSelection.Selected),
					new CollectionOptionalMemberSelection(optional.IdentityResolution.Key, CollectionMemberSelection.Unselected)
				}));
		}

		[Test]
		public void Build_SelectionFingerprintIsStableAndChangesWithOptionalChoices()
		{
			NormalizedCollectionMember first = CreateMember(
				0, "first", CollectionMemberRequirement.Optional, CollectionMemberSelection.Unselected);
			NormalizedCollectionMember second = CreateMember(
				1, "second", CollectionMemberRequirement.Optional, CollectionMemberSelection.Unselected);
			CollectionCapabilityReport normalizedReport = CollectionCapabilityReport.Create(CreateManifest(new[] { first, second }, null));
			var builder = new CollectionEffectiveSelectionBuilder();

			CollectionEffectiveSelection selectedBoth = builder.Build(normalizedReport, new[]
			{
				new CollectionOptionalMemberSelection(first.IdentityResolution.Key, CollectionMemberSelection.Selected),
				new CollectionOptionalMemberSelection(second.IdentityResolution.Key, CollectionMemberSelection.Selected)
			});
			CollectionEffectiveSelection reversedDecisionOrder = builder.Build(normalizedReport, new[]
			{
				new CollectionOptionalMemberSelection(second.IdentityResolution.Key, CollectionMemberSelection.Selected),
				new CollectionOptionalMemberSelection(first.IdentityResolution.Key, CollectionMemberSelection.Selected)
			});
			CollectionEffectiveSelection selectedFirstOnly = builder.Build(normalizedReport, new[]
			{
				new CollectionOptionalMemberSelection(first.IdentityResolution.Key, CollectionMemberSelection.Selected)
			});

			Assert.That(selectedBoth.SelectionFingerprintFormatVersion, Is.EqualTo("nmm-ce.collections.effective-selection/1"));
			Assert.That(selectedBoth.SelectionFingerprint, Does.StartWith("sha256:"));
			Assert.That(selectedBoth.SelectionFingerprint, Is.EqualTo(reversedDecisionOrder.SelectionFingerprint));
			Assert.That(selectedBoth.SelectionFingerprint, Is.Not.EqualTo(selectedFirstOnly.SelectionFingerprint));
		}

		[TestCase("")]
		[TestCase("exact")]
		public void NexusGateA_AbsentOrExactOptionalPolicyBecomesSupportedWhenSelected(string updatePolicy)
		{
			NexusCollectionManifestNormalizationResult normalized = NormalizeNexusManifest(updatePolicy);
			NormalizedCollectionMember optional = normalized.Manifest.Members[1];

			CollectionEffectiveSelection effective = new CollectionEffectiveSelectionBuilder().Build(
				normalized.CapabilityReport,
				new[] { new CollectionOptionalMemberSelection(optional.IdentityResolution.Key, CollectionMemberSelection.Selected) });

			Assert.That(effective.CapabilityReport.Status, Is.EqualTo(CollectionCompatibilityStatus.Supported));
			Assert.That(effective.CapabilityReport.AllIssues.Any(x => x.Code == "member.source-policy-needs-resolution"), Is.False);
		}

		[TestCase("latest")]
		[TestCase("prefer")]
		public void NexusGateA_LatestOrPreferOptionalPolicyRemainsActionRequiredWhenSelected(string updatePolicy)
		{
			NexusCollectionManifestNormalizationResult normalized = NormalizeNexusManifest(updatePolicy);
			NormalizedCollectionMember optional = normalized.Manifest.Members[1];
			Assert.That(normalized.CapabilityReport.Status, Is.EqualTo(CollectionCompatibilityStatus.Supported),
				"An unresolved source policy on an unselected optional must remain visible without blocking the current closure.");
			Assert.That(normalized.CapabilityReport.AllIssues.Any(x => x.Code == "member.source-policy-needs-resolution"), Is.True);

			CollectionEffectiveSelection effective = new CollectionEffectiveSelectionBuilder().Build(
				normalized.CapabilityReport,
				new[] { new CollectionOptionalMemberSelection(optional.IdentityResolution.Key, CollectionMemberSelection.Selected) });

			Assert.That(effective.CapabilityReport.Status, Is.EqualTo(CollectionCompatibilityStatus.ActionRequired));
			Assert.That(effective.CapabilityReport.AllIssues.Any(x => x.Code == "member.source-policy-needs-resolution"), Is.True);
		}

		private static NexusCollectionManifestNormalizationResult NormalizeNexusManifest(string optionalUpdatePolicy)
		{
			string policyJson = String.IsNullOrEmpty(optionalUpdatePolicy) ? String.Empty : ",\"updatePolicy\":\"" + optionalUpdatePolicy + "\"";
			string json = "{" +
				"\"info\":{\"author\":\"Curator\",\"authorUrl\":\"https://example.invalid/author\",\"name\":\"Example\",\"description\":\"Example\",\"domainName\":\"skyrim\"}," +
				"\"mods\":[" +
				"{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20,\"updatePolicy\":\"exact\"}}," +
				"{\"name\":\"Optional\",\"version\":\"1\",\"optional\":true,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":11,\"fileId\":21" + policyJson + "}}]," +
				"\"modRules\":[]}";
			CollectionIdentity collection = CollectionIdentity.FromNexus("c6153");
			CollectionRevision revision = new CollectionRevision(
				CollectionRevisionIdentity.FromNexus(collection, "revision-c6153", 3), null, null, 2);
			return new NexusCollectionManifestNormalizer().Normalize(Encoding.UTF8.GetBytes(json), revision);
		}

		private static NormalizedCollectionMember CreateMember(
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
				new CollectionArtifactReference("nexus-mod-file", "game/" + (100 + sourceOrdinal) + "/" + (200 + sourceOrdinal), null),
				CollectionRecipeIdentity.FromFingerprint("recipe-" + memberKey),
				memberKey);
		}

		private static NormalizedCollectionManifest CreateManifest(
			IEnumerable<NormalizedCollectionMember> members,
			IEnumerable<CollectionMemberDependency> dependencies)
		{
			CollectionIdentity collection = CollectionIdentity.FromNexus("c6153-domain");
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "revision-domain", 3);
			return new NormalizedCollectionManifest(
				revision,
				new CollectionManifestSourceSnapshot(CollectionContentHash.FromSha256(Sha256A), 123, "test-schema", "test-normalizer"),
				CollectionManifestMemberSetCompleteness.Complete,
				null,
				members,
				dependencies);
		}
	}
}
