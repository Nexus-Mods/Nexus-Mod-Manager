using System;
using System.Collections.Generic;
using Nexus.Client.CollectionManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class CollectionManifestNormalizationModelTests
	{
		private const string Sha256A = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
		private const string Sha256B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

		[Test]
		public void ContentHash_NormalizesSha256AndRejectsMalformedValues()
		{
			CollectionContentHash hash = CollectionContentHash.FromSha256(Sha256A);

			Assert.That(hash.Algorithm, Is.EqualTo(CollectionContentHashAlgorithm.Sha256));
			Assert.That(hash.Value, Is.EqualTo(Sha256A.ToLowerInvariant()));
			Assert.Throws<ArgumentException>(() => CollectionContentHash.FromSha256("abc"));
			Assert.Throws<ArgumentException>(() => CollectionContentHash.FromSha256(new string('g', 64)));
			Assert.Throws<ArgumentException>(() => CollectionContentHash.FromSha256(" " + Sha256B));
		}

		[Test]
		public void ManifestSourceSnapshot_BindsRawHashSchemaAndNormalizerWithoutAPathOrUrl()
		{
			CollectionManifestSourceSnapshot snapshot = new CollectionManifestSourceSnapshot(
				CollectionContentHash.FromSha256(Sha256A), 1234, "nexus-collection-manifest-v1", "nmm-normalizer-v1");

			Assert.That(snapshot.ContentHash.Value, Is.EqualTo(Sha256A.ToLowerInvariant()));
			Assert.That(snapshot.ByteLength, Is.EqualTo(1234));
			Assert.That(snapshot.SchemaIdentity, Is.EqualTo("nexus-collection-manifest-v1"));
			Assert.That(snapshot.NormalizerVersion, Is.EqualTo("nmm-normalizer-v1"));
			Assert.That(typeof(CollectionManifestSourceSnapshot).GetProperty("Path"), Is.Null);
			Assert.That(typeof(CollectionManifestSourceSnapshot).GetProperty("Url"), Is.Null);
			Assert.That(typeof(CollectionManifestSourceSnapshot).GetProperty("DownloadUrl"), Is.Null);
		}

		[Test]
		public void ArtifactReference_KeepsStableSourceIdentitySeparateFromDownloadAuthorization()
		{
			CollectionArtifactReference artifact = new CollectionArtifactReference(
				"nexusmods.file", "skyrimse:123:456", CollectionContentHash.FromSha256(Sha256B));

			Assert.That(artifact.Scheme, Is.EqualTo("nexusmods.file"));
			Assert.That(artifact.StableId, Is.EqualTo("skyrimse:123:456"));
			Assert.That(artifact.ExpectedContentHash.Value, Is.EqualTo(Sha256B));
			Assert.That(typeof(CollectionArtifactReference).GetProperty("Url"), Is.Null);
			Assert.That(typeof(CollectionArtifactReference).GetProperty("DownloadUrl"), Is.Null);
			Assert.Throws<ArgumentException>(() => new CollectionArtifactReference(
				"nexusmods.file", "https://cdn.example.test/signed?token=secret", null));
		}

		[Test]
		public void MemberIdentityResolution_PreservesAmbiguityInsteadOfInventingAKey()
		{
			CollectionMemberIdentityResolution ambiguous = CollectionMemberIdentityResolution.Ambiguous(
				"Two source members match the same validated artifact identity.");

			Assert.That(ambiguous.Status, Is.EqualTo(CollectionMemberIdentityResolutionStatus.Ambiguous));
			Assert.That(ambiguous.IsResolved, Is.False);
			Assert.That(ambiguous.Key, Is.Null);
			Assert.That(ambiguous.Reason, Does.Contain("Two source members"));
		}

		[Test]
		public void ResolvedMemberKey_RemainsIdentityWhenSourceOrderChanges()
		{
			CollectionMemberKey key = CollectionMemberKey.FromProvider("member-42");
			NormalizedCollectionMember firstOrder = CreateMember(0, CollectionMemberIdentityResolution.Resolved(key),
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			NormalizedCollectionMember laterOrder = CreateMember(89, CollectionMemberIdentityResolution.Resolved(key),
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);

			Assert.That(firstOrder.SourceOrdinal, Is.Not.EqualTo(laterOrder.SourceOrdinal));
			Assert.That(firstOrder.IdentityResolution.Key, Is.EqualTo(laterOrder.IdentityResolution.Key));
		}

		[Test]
		public void RequiredMemberMayBeExplicitlyOmittedButIsMarkedAsDeviation()
		{
			NormalizedCollectionMember required = CreateMember(0,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider("required")),
				CollectionMemberRequirement.Required, CollectionMemberSelection.Unselected);
			NormalizedCollectionMember optional = CreateMember(1,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider("optional")),
				CollectionMemberRequirement.Optional, CollectionMemberSelection.Unselected);

			Assert.That(required.IsRequiredOmission, Is.True);
			Assert.That(optional.IsRequiredOmission, Is.False);
		}

		[Test]
		public void Member_AllowsIncompleteArtifactOrRecipeForReadOnlyPreview()
		{
			NormalizedCollectionMember member = new NormalizedCollectionMember(
				0,
				CollectionMemberIdentityResolution.Missing("Stable identity was not present in this source page."),
				CollectionMemberRequirement.Required,
				CollectionMemberSelection.Selected,
				null,
				null,
				"Preview-only member");

			Assert.That(member.Artifact, Is.Null);
			Assert.That(member.RecipeIdentity, Is.Null);
			Assert.That(member.IdentityResolution.IsResolved, Is.False);
		}

		[Test]
		public void Manifest_CopiesInputAndRejectsDuplicateResolvedMemberKeys()
		{
			NormalizedCollectionMember member = CreateMember(0,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider("member-a")),
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			List<NormalizedCollectionMember> sourceMembers = new List<NormalizedCollectionMember> { member };
			NormalizedCollectionManifest manifest = CreateManifest(CollectionManifestMemberSetCompleteness.Complete, null, sourceMembers);

			sourceMembers.Clear();
			Assert.That(manifest.Members.Count, Is.EqualTo(1));
			Assert.Throws<NotSupportedException>(() => ((IList<NormalizedCollectionMember>)manifest.Members).Add(member));

			NormalizedCollectionMember duplicate = CreateMember(1,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider("member-a")),
				CollectionMemberRequirement.Optional, CollectionMemberSelection.Selected);
			Assert.Throws<ArgumentException>(() => CreateManifest(CollectionManifestMemberSetCompleteness.Complete, null,
				new[] { member, duplicate }));
		}

		[Test]
		public void Manifest_IncompleteMemberSetRequiresExplicitReason()
		{
			NormalizedCollectionManifest incomplete = CreateManifest(
				CollectionManifestMemberSetCompleteness.Incomplete,
				"Provider pagination did not complete.",
				new NormalizedCollectionMember[0]);

			Assert.That(incomplete.IsMemberSetComplete, Is.False);
			Assert.That(incomplete.IncompletenessReason, Does.Contain("pagination"));
			Assert.Throws<ArgumentException>(() => CreateManifest(
				CollectionManifestMemberSetCompleteness.Incomplete, null, new NormalizedCollectionMember[0]));
			Assert.Throws<ArgumentException>(() => CreateManifest(
				CollectionManifestMemberSetCompleteness.Complete, "unexpected", new NormalizedCollectionMember[0]));
		}

		[Test]
		public void NormalizedModels_ExposeNoPublicPropertySetters()
		{
			AssertNoPublicSetters(typeof(CollectionContentHash));
			AssertNoPublicSetters(typeof(CollectionManifestSourceSnapshot));
			AssertNoPublicSetters(typeof(CollectionArtifactReference));
			AssertNoPublicSetters(typeof(CollectionMemberIdentityResolution));
			AssertNoPublicSetters(typeof(NormalizedCollectionMember));
			AssertNoPublicSetters(typeof(NormalizedCollectionManifest));
		}

		private static NormalizedCollectionMember CreateMember(
			int sourceOrdinal,
			CollectionMemberIdentityResolution identity,
			CollectionMemberRequirement requirement,
			CollectionMemberSelection selection)
		{
			return new NormalizedCollectionMember(
				sourceOrdinal,
				identity,
				requirement,
				selection,
				new CollectionArtifactReference("test-artifact", "artifact-" + sourceOrdinal, null),
				CollectionRecipeIdentity.FromFingerprint("recipe-" + sourceOrdinal),
				"Member " + sourceOrdinal);
		}

		private static NormalizedCollectionManifest CreateManifest(
			CollectionManifestMemberSetCompleteness completeness,
			string incompletenessReason,
			IEnumerable<NormalizedCollectionMember> members)
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
