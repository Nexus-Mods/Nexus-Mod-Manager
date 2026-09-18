using System;
using Nexus.Client.CollectionManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class CollectionIdentityValueTests
	{
		[Test]
		public void NexusCollectionIdentity_PreservesOpaqueProviderIdentityWithOrdinalEquality()
		{
			CollectionIdentity first = CollectionIdentity.FromNexus("2210");
			CollectionIdentity same = CollectionIdentity.FromNexus("2210");
			CollectionIdentity differentCase = CollectionIdentity.FromNexus("A1b2");
			CollectionIdentity otherCase = CollectionIdentity.FromNexus("a1b2");

			Assert.That(first.Origin, Is.EqualTo(CollectionOrigin.NexusMods));
			Assert.That(first.StableId, Is.EqualTo("2210"));
			Assert.That(first, Is.EqualTo(same));
			Assert.That(first.GetHashCode(), Is.EqualTo(same.GetHashCode()));
			Assert.That(differentCase, Is.Not.EqualTo(otherCase));
		}

		[Test]
		public void NexusCollectionIdentity_RejectsMissingOrPaddedIdentityTokens()
		{
			Assert.Throws<ArgumentException>(() => CollectionIdentity.FromNexus(null));
			Assert.Throws<ArgumentException>(() => CollectionIdentity.FromNexus("   "));
			Assert.Throws<ArgumentException>(() => CollectionIdentity.FromNexus(" 2210"));
			Assert.Throws<ArgumentException>(() => CollectionIdentity.FromNexus("2210 "));
		}

		[Test]
		public void LocalCollectionIdentity_UsesCanonicalGuidAndSeparatesOrigin()
		{
			Guid id = Guid.Parse("197BBEA1-41B3-4A06-9209-CC0792A5334A");
			CollectionIdentity local = CollectionIdentity.FromLocal(id);
			CollectionIdentity nexus = CollectionIdentity.FromNexus(id.ToString("D"));

			Assert.That(local.Origin, Is.EqualTo(CollectionOrigin.Local));
			Assert.That(local.StableId, Is.EqualTo("197bbea1-41b3-4a06-9209-cc0792a5334a"));
			Assert.That(local, Is.Not.EqualTo(nexus));
			Assert.Throws<ArgumentException>(() => CollectionIdentity.FromLocal(Guid.Empty));
		}

		[Test]
		public void NexusRevisionIdentity_SeparatesProviderIdFromConcreteRevisionNumber()
		{
			CollectionIdentity collection = CollectionIdentity.FromNexus("2210");
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "772530", 100);

			Assert.That(revision.Collection, Is.SameAs(collection));
			Assert.That(revision.StableRevisionId, Is.EqualTo("772530"));
			Assert.That(revision.NexusRevisionNumber, Is.EqualTo(100));
			Assert.That(revision.StableRevisionId, Is.Not.EqualTo(revision.NexusRevisionNumber.ToString()));
		}

		[Test]
		public void NexusRevisionIdentity_RequiresConcretePositiveRevisionAndNexusCollection()
		{
			CollectionIdentity nexus = CollectionIdentity.FromNexus("2210");
			CollectionIdentity local = CollectionIdentity.FromLocal(Guid.NewGuid());

			Assert.Throws<ArgumentNullException>(() => CollectionRevisionIdentity.FromNexus(null, "772530", 100));
			Assert.Throws<ArgumentException>(() => CollectionRevisionIdentity.FromNexus(local, "772530", 100));
			Assert.Throws<ArgumentException>(() => CollectionRevisionIdentity.FromNexus(nexus, " ", 100));
			Assert.Throws<ArgumentOutOfRangeException>(() => CollectionRevisionIdentity.FromNexus(nexus, "772530", 0));
			Assert.Throws<ArgumentOutOfRangeException>(() => CollectionRevisionIdentity.FromNexus(nexus, "772530", -1));
		}

		[Test]
		public void LocalRevisionIdentity_HasNoNexusRevisionNumberAndRequiresLocalCollection()
		{
			CollectionIdentity local = CollectionIdentity.FromLocal(Guid.NewGuid());
			Guid revisionId = Guid.Parse("4D7D24AE-1AA9-4C46-AB59-B90BBBD0A6EF");
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromLocal(local, revisionId);

			Assert.That(revision.StableRevisionId, Is.EqualTo("4d7d24ae-1aa9-4c46-ab59-b90bbbd0a6ef"));
			Assert.That(revision.NexusRevisionNumber, Is.Null);
			Assert.Throws<ArgumentException>(() => CollectionRevisionIdentity.FromLocal(CollectionIdentity.FromNexus("2210"), revisionId));
			Assert.Throws<ArgumentException>(() => CollectionRevisionIdentity.FromLocal(local, Guid.Empty));
		}

		[Test]
		public void RevisionEquality_IncludesCollectionRevisionIdAndRevisionNumber()
		{
			CollectionRevisionIdentity first = CollectionRevisionIdentity.FromNexus(CollectionIdentity.FromNexus("2210"), "772530", 100);
			CollectionRevisionIdentity same = CollectionRevisionIdentity.FromNexus(CollectionIdentity.FromNexus("2210"), "772530", 100);
			CollectionRevisionIdentity differentNumber = CollectionRevisionIdentity.FromNexus(CollectionIdentity.FromNexus("2210"), "772530", 101);
			CollectionRevisionIdentity differentId = CollectionRevisionIdentity.FromNexus(CollectionIdentity.FromNexus("2210"), "772531", 100);
			CollectionRevisionIdentity differentCollection = CollectionRevisionIdentity.FromNexus(CollectionIdentity.FromNexus("2211"), "772530", 100);

			Assert.That(first, Is.EqualTo(same));
			Assert.That(first.GetHashCode(), Is.EqualTo(same.GetHashCode()));
			Assert.That(first, Is.Not.EqualTo(differentNumber));
			Assert.That(first, Is.Not.EqualTo(differentId));
			Assert.That(first, Is.Not.EqualTo(differentCollection));
		}

		[Test]
		public void MemberKeys_DistinguishProviderLocalAndValidatedMatchingIdentities()
		{
			CollectionMemberKey provider = CollectionMemberKey.FromProvider("member-42");
			CollectionMemberKey providerAgain = CollectionMemberKey.FromProvider("member-42");
			CollectionMemberKey matched = CollectionMemberKey.FromValidatedMatch("member-42");
			CollectionMemberKey local = CollectionMemberKey.FromLocal(Guid.Parse("31AA8E7B-E5AE-4771-9CC7-D25F24A250E0"));

			Assert.That(provider.Kind, Is.EqualTo(CollectionMemberKeyKind.ProviderStable));
			Assert.That(provider, Is.EqualTo(providerAgain));
			Assert.That(provider.GetHashCode(), Is.EqualTo(providerAgain.GetHashCode()));
			Assert.That(provider, Is.Not.EqualTo(matched));
			Assert.That(matched.Kind, Is.EqualTo(CollectionMemberKeyKind.ValidatedMatch));
			Assert.That(local.Kind, Is.EqualTo(CollectionMemberKeyKind.Local));
			Assert.That(local.Value, Is.EqualTo("31aa8e7b-e5ae-4771-9cc7-d25f24a250e0"));
		}

		[Test]
		public void MemberKeys_RejectMissingPaddedAndEmptyLocalIdentities()
		{
			Assert.Throws<ArgumentException>(() => CollectionMemberKey.FromProvider(null));
			Assert.Throws<ArgumentException>(() => CollectionMemberKey.FromProvider(" member-42"));
			Assert.Throws<ArgumentException>(() => CollectionMemberKey.FromValidatedMatch(" "));
			Assert.Throws<ArgumentException>(() => CollectionMemberKey.FromLocal(Guid.Empty));
		}
	}
}
