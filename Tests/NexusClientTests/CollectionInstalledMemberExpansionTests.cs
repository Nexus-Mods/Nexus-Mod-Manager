using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.UI;
using NUnit.Framework;

namespace Nexus.Client.Tests
{
	[TestFixture]
	[Category("CollectionsC12Workflow")]
	[Category("CollectionsGateA")]
	[Category("CollectionsManagement")]
	public class CollectionInstalledMemberExpansionTests
	{
		private const string Sha256A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

		[Test]
		public void BuildManagedMemberExpansionSelections_KeepsInstalledOptionalsAndAddsOnlyRequestedMember()
		{
			NormalizedCollectionMember required = CreateMember(0, "required", CollectionMemberRequirement.Required);
			NormalizedCollectionMember installedOptional = CreateMember(1, "installed-optional", CollectionMemberRequirement.Optional);
			NormalizedCollectionMember requestedOptional = CreateMember(2, "requested-optional", CollectionMemberRequirement.Optional);
			NormalizedCollectionMember otherOptional = CreateMember(3, "other-optional", CollectionMemberRequirement.Optional);
			NormalizedCollectionManifest manifest = CreateManifest(required, installedOptional, requestedOptional, otherOptional);

			IReadOnlyList<CollectionOptionalMemberSelection> result = CollectionsPreviewControl.BuildManagedMemberExpansionSelections(
				manifest, new[] { required.IdentityResolution.Key, installedOptional.IdentityResolution.Key },
				requestedOptional.IdentityResolution.Key);

			Assert.That(result.Single(x => x.MemberKey.Equals(installedOptional.IdentityResolution.Key)).Selection,
				Is.EqualTo(CollectionMemberSelection.Selected));
			Assert.That(result.Single(x => x.MemberKey.Equals(requestedOptional.IdentityResolution.Key)).Selection,
				Is.EqualTo(CollectionMemberSelection.Selected));
			Assert.That(result.Single(x => x.MemberKey.Equals(otherOptional.IdentityResolution.Key)).Selection,
				Is.EqualTo(CollectionMemberSelection.Unselected));
		}

		[Test]
		public void BuildManagedMemberExpansionSelections_RejectsRequiredMemberAsRequestedAddition()
		{
			NormalizedCollectionMember required = CreateMember(0, "required", CollectionMemberRequirement.Required);
			NormalizedCollectionMember optional = CreateMember(1, "optional", CollectionMemberRequirement.Optional);
			NormalizedCollectionManifest manifest = CreateManifest(required, optional);

			Assert.Throws<InvalidOperationException>(() => CollectionsPreviewControl.BuildManagedMemberExpansionSelections(
				manifest, new[] { optional.IdentityResolution.Key }, required.IdentityResolution.Key));
		}

		[Test]
		public void BuildManagedMemberExpansionSelections_RejectsMemberAlreadyBoundToInstalledAssociation()
		{
			NormalizedCollectionMember required = CreateMember(0, "required", CollectionMemberRequirement.Required);
			NormalizedCollectionMember optional = CreateMember(1, "optional", CollectionMemberRequirement.Optional);
			NormalizedCollectionManifest manifest = CreateManifest(required, optional);

			Assert.Throws<InvalidOperationException>(() => CollectionsPreviewControl.BuildManagedMemberExpansionSelections(
				manifest, new[] { required.IdentityResolution.Key, optional.IdentityResolution.Key }, optional.IdentityResolution.Key));
		}

		[Test]
		public void BuildManagedMemberExpansionSelections_RejectsIncompleteInstalledRequiredBaseline()
		{
			NormalizedCollectionMember required = CreateMember(0, "required", CollectionMemberRequirement.Required);
			NormalizedCollectionMember requested = CreateMember(1, "requested", CollectionMemberRequirement.Optional);
			NormalizedCollectionManifest manifest = CreateManifest(required, requested);

			Assert.Throws<InvalidOperationException>(() => CollectionsPreviewControl.BuildManagedMemberExpansionSelections(
				manifest, new CollectionMemberKey[0], requested.IdentityResolution.Key));
		}

		private static NormalizedCollectionMember CreateMember(int ordinal, string key, CollectionMemberRequirement requirement)
		{
			return new NormalizedCollectionMember(ordinal,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider(key)), requirement,
				requirement == CollectionMemberRequirement.Required ? CollectionMemberSelection.Selected : CollectionMemberSelection.Unselected,
				new CollectionArtifactReference("nexus-mod-file", "game/" + (100 + ordinal) + "/" + (200 + ordinal), null),
				CollectionRecipeIdentity.FromFingerprint("recipe-" + key), key);
		}

		private static NormalizedCollectionManifest CreateManifest(params NormalizedCollectionMember[] members)
		{
			CollectionIdentity collection = CollectionIdentity.FromNexus("c9-member-expansion");
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "revision-c9-member-expansion", 1);
			return new NormalizedCollectionManifest(revision,
				new CollectionManifestSourceSnapshot(CollectionContentHash.FromSha256(Sha256A), 123, "test-schema", "test-normalizer"),
				CollectionManifestMemberSetCompleteness.Complete, null, members);
		}
	}
}
