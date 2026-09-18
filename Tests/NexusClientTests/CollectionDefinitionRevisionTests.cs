using System;
using Nexus.Client.CollectionManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class CollectionDefinitionRevisionTests
	{
		[Test]
		public void Definition_PreservesIdentityAndDisplayMetadataWithoutOwningRevisionState()
		{
			CollectionIdentity identity = CollectionIdentity.FromNexus("2210");
			string summary = "Line one.\r\nLine two.";
			CollectionDefinition definition = new CollectionDefinition(identity, "Constellation", "Curator", summary);

			Assert.That(definition.Identity, Is.SameAs(identity));
			Assert.That(definition.Origin, Is.EqualTo(CollectionOrigin.NexusMods));
			Assert.That(definition.DisplayName, Is.EqualTo("Constellation"));
			Assert.That(definition.AuthorDisplayName, Is.EqualTo("Curator"));
			Assert.That(definition.Summary, Is.EqualTo(summary));
		}

		[Test]
		public void Definition_IdentityEqualityDoesNotDependOnRefreshableDisplayMetadata()
		{
			CollectionIdentity identity = CollectionIdentity.FromNexus("2210");
			CollectionDefinition oldMetadata = new CollectionDefinition(identity, "Old Name", "Old Author", "Old summary");
			CollectionDefinition refreshedMetadata = new CollectionDefinition(CollectionIdentity.FromNexus("2210"), "New Name", "New Author", "New summary");
			CollectionDefinition other = new CollectionDefinition(CollectionIdentity.FromNexus("2211"), "Old Name", "Old Author", "Old summary");

			Assert.That(oldMetadata, Is.EqualTo(refreshedMetadata));
			Assert.That(oldMetadata.GetHashCode(), Is.EqualTo(refreshedMetadata.GetHashCode()));
			Assert.That(oldMetadata, Is.Not.EqualTo(other));
		}

		[Test]
		public void Definition_AllowsMissingDecorativeMetadataButRejectsMalformedSuppliedValues()
		{
			CollectionIdentity identity = CollectionIdentity.FromNexus("2210");
			CollectionDefinition identityOnly = new CollectionDefinition(identity, null, null, null);

			Assert.That(identityOnly.Identity, Is.EqualTo(identity));
			Assert.That(identityOnly.DisplayName, Is.Null);
			Assert.That(identityOnly.AuthorDisplayName, Is.Null);
			Assert.That(identityOnly.Summary, Is.Null);
			Assert.Throws<ArgumentNullException>(() => new CollectionDefinition(null, "Name", null, null));
			Assert.Throws<ArgumentException>(() => new CollectionDefinition(identity, "   ", null, null));
			Assert.Throws<ArgumentException>(() => new CollectionDefinition(identity, " Name", null, null));
			Assert.Throws<ArgumentException>(() => new CollectionDefinition(identity, "Name", " Author", null));
		}

		[Test]
		public void NexusRevision_PreservesExactIdentityAndDescriptiveMemberCount()
		{
			CollectionRevisionIdentity identity = CollectionRevisionIdentity.FromNexus(
				CollectionIdentity.FromNexus("2210"), "772530", 100);
			string notes = "Provider notes\r\nare preserved.";
			CollectionRevision revision = new CollectionRevision(identity, "Revision 100", notes, 567);

			Assert.That(revision.Identity, Is.SameAs(identity));
			Assert.That(revision.Collection, Is.SameAs(identity.Collection));
			Assert.That(revision.IsRemoteBaseline, Is.True);
			Assert.That(revision.IsSealedLocalRevision, Is.False);
			Assert.That(revision.RevisionLabel, Is.EqualTo("Revision 100"));
			Assert.That(revision.Notes, Is.EqualTo(notes));
			Assert.That(revision.DeclaredMemberCount, Is.EqualTo(567));
		}

		[Test]
		public void LocalRevision_IsExplicitlyASealedLocalRevision()
		{
			CollectionIdentity collection = CollectionIdentity.FromLocal(Guid.Parse("197BBEA1-41B3-4A06-9209-CC0792A5334A"));
			CollectionRevisionIdentity identity = CollectionRevisionIdentity.FromLocal(
				collection, Guid.Parse("4D7D24AE-1AA9-4C46-AB59-B90BBBD0A6EF"));
			CollectionRevision revision = new CollectionRevision(identity, "Before replacement", null, 42);

			Assert.That(revision.IsRemoteBaseline, Is.False);
			Assert.That(revision.IsSealedLocalRevision, Is.True);
			Assert.That(revision.Collection, Is.EqualTo(collection));
		}

		[Test]
		public void Revision_RejectsInvalidMetadataWithoutInventingManifestCompleteness()
		{
			CollectionRevisionIdentity identity = CollectionRevisionIdentity.FromNexus(
				CollectionIdentity.FromNexus("2210"), "772530", 100);

			Assert.Throws<ArgumentNullException>(() => new CollectionRevision(null, null, null, null));
			Assert.Throws<ArgumentException>(() => new CollectionRevision(identity, " ", null, null));
			Assert.Throws<ArgumentException>(() => new CollectionRevision(identity, " Revision 100", null, null));
			Assert.Throws<ArgumentOutOfRangeException>(() => new CollectionRevision(identity, null, null, -1));

			CollectionRevision unknownCount = new CollectionRevision(identity, null, null, null);
			Assert.That(unknownCount.DeclaredMemberCount, Is.Null);
		}

		[Test]
		public void Revision_EqualityIsRevisionIdentityNotDecorativeMetadata()
		{
			CollectionRevision first = new CollectionRevision(
				CollectionRevisionIdentity.FromNexus(CollectionIdentity.FromNexus("2210"), "772530", 100),
				"Published", "Notes A", 567);
			CollectionRevision sameIdentity = new CollectionRevision(
				CollectionRevisionIdentity.FromNexus(CollectionIdentity.FromNexus("2210"), "772530", 100),
				"Refreshed label", "Notes B", 567);
			CollectionRevision otherRevision = new CollectionRevision(
				CollectionRevisionIdentity.FromNexus(CollectionIdentity.FromNexus("2210"), "772531", 101),
				"Published", "Notes A", 567);

			Assert.That(first, Is.EqualTo(sameIdentity));
			Assert.That(first.GetHashCode(), Is.EqualTo(sameIdentity.GetHashCode()));
			Assert.That(first, Is.Not.EqualTo(otherRevision));
		}

		[Test]
		public void ModelsExposeNoPublicPropertySetters()
		{
			AssertNoPublicSetters(typeof(CollectionDefinition));
			AssertNoPublicSetters(typeof(CollectionRevision));
		}

		private static void AssertNoPublicSetters(Type type)
		{
			foreach (var property in type.GetProperties())
			{
				Assert.That(property.GetSetMethod(), Is.Null, type.Name + "." + property.Name + " must remain immutable.");
			}
		}
	}
}
