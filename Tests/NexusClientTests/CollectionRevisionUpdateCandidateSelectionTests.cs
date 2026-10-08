using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.CollectionManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	[Category("CollectionsC12Workflow")]
	public class CollectionRevisionUpdateCandidateSelectionTests
	{
		private const string Sha256A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
		private const string Sha256B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

		[Test]
		public void Build_ExactOptionalMember_PreservesInstalledSelectionInsteadOfCandidateDefault()
		{
			NormalizedCollectionMember oldMember = Member(0, 72402, 285676, CollectionMemberRequirement.Optional, CollectionMemberSelection.Selected);
			NormalizedCollectionMember candidate = Member(0, 72402, 285676, CollectionMemberRequirement.Optional, CollectionMemberSelection.Unselected);
			Fixture f = CreateFixture(new[] { oldMember }, new[] { candidate });

			CollectionEffectiveSelection result = f.Build();

			Assert.That(result.Manifest.Members.Single().Selection, Is.EqualTo(CollectionMemberSelection.Selected));
		}

		[Test]
		public void Build_ExplicitUncheck_WinsOverInheritedSelectedOptional()
		{
			NormalizedCollectionMember oldMember = Member(0, 72402, 285676, CollectionMemberRequirement.Optional, CollectionMemberSelection.Selected);
			NormalizedCollectionMember candidate = Member(0, 72402, 285676, CollectionMemberRequirement.Optional, CollectionMemberSelection.Unselected);
			Fixture f = CreateFixture(new[] { oldMember }, new[] { candidate });

			Assert.That(f.Build().Manifest.Members.Single().Selection, Is.EqualTo(CollectionMemberSelection.Selected));
			var explicitSelection = new CollectionOptionalMemberSelection(candidate.IdentityResolution.Key, CollectionMemberSelection.Unselected);
			Assert.That(f.Build(new[] { explicitSelection }).Manifest.Members.Single().Selection, Is.EqualTo(CollectionMemberSelection.Unselected));
		}

		[Test]
		public void Build_CrossKeyNexusFileChange_PreservesInstalledOptionalSelection()
		{
			NormalizedCollectionMember oldMember = Member(0, 4598, 407774, CollectionMemberRequirement.Optional, CollectionMemberSelection.Selected);
			NormalizedCollectionMember candidate = Member(0, 4598, 376040, CollectionMemberRequirement.Optional, CollectionMemberSelection.Unselected);
			Fixture f = CreateFixture(new[] { oldMember }, new[] { candidate });

			CollectionEffectiveSelection result = f.Build();

			Assert.That(result.Manifest.Members.Single().IdentityResolution.Key, Is.EqualTo(candidate.IdentityResolution.Key));
			Assert.That(result.Manifest.Members.Single().Selection, Is.EqualTo(CollectionMemberSelection.Selected));
		}

		[Test]
		public void Build_InstalledUnselectedOptional_RemainsUnselectedWhenCandidateDefaultChanges()
		{
			NormalizedCollectionMember oldMember = Member(0, 72904, 283994, CollectionMemberRequirement.Optional, CollectionMemberSelection.Unselected);
			NormalizedCollectionMember candidate = Member(0, 72904, 283994, CollectionMemberRequirement.Optional, CollectionMemberSelection.Selected);
			Fixture f = CreateFixture(new[] { oldMember }, new[] { candidate });

			CollectionEffectiveSelection result = f.Build();

			Assert.That(result.Manifest.Members.Single().Selection, Is.EqualTo(CollectionMemberSelection.Unselected));
		}

		[Test]
		public void Build_ExplicitCandidateDecision_WinsOverPreservedCrossKeyParticipation()
		{
			NormalizedCollectionMember oldMember = Member(0, 4598, 407774, CollectionMemberRequirement.Optional, CollectionMemberSelection.Unselected);
			NormalizedCollectionMember candidate = Member(0, 4598, 376040, CollectionMemberRequirement.Optional, CollectionMemberSelection.Unselected);
			Fixture f = CreateFixture(new[] { oldMember }, new[] { candidate });
			var explicitSelection = new CollectionOptionalMemberSelection(candidate.IdentityResolution.Key, CollectionMemberSelection.Selected);

			CollectionEffectiveSelection result = f.Build(new[] { explicitSelection });

			Assert.That(result.Manifest.Members.Single().Selection, Is.EqualTo(CollectionMemberSelection.Selected));
		}

		[Test]
		public void Build_AmbiguousSameModMultiFileChange_DoesNotGuessParticipation()
		{
			NormalizedCollectionMember oldA = Member(0, 5000, 100, CollectionMemberRequirement.Optional, CollectionMemberSelection.Selected);
			NormalizedCollectionMember oldB = Member(1, 5000, 101, CollectionMemberRequirement.Optional, CollectionMemberSelection.Selected);
			NormalizedCollectionMember candidate = Member(0, 5000, 102, CollectionMemberRequirement.Optional, CollectionMemberSelection.Unselected);
			Fixture f = CreateFixture(new[] { oldA, oldB }, new[] { candidate });

			CollectionEffectiveSelection result = f.Build();

			Assert.That(result.Manifest.Members.Single().Selection, Is.EqualTo(CollectionMemberSelection.Unselected),
				"Multiple old files for the same Nexus mod are not enough evidence to inherit one candidate optional choice.");
		}

		[Test]
		public void Build_NewOptionalWithoutOldCounterpart_UsesCandidateDefault()
		{
			NormalizedCollectionMember oldMember = Member(0, 1000, 100, CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			NormalizedCollectionMember candidateRequired = Member(0, 1000, 100, CollectionMemberRequirement.Required, CollectionMemberSelection.Selected);
			NormalizedCollectionMember candidateNew = Member(1, 2000, 200, CollectionMemberRequirement.Optional, CollectionMemberSelection.Selected);
			Fixture f = CreateFixture(new[] { oldMember }, new[] { candidateRequired, candidateNew });

			CollectionEffectiveSelection result = f.Build();

			Assert.That(result.Manifest.Members.Single(x => x.IdentityResolution.Key.Equals(candidateNew.IdentityResolution.Key)).Selection,
				Is.EqualTo(CollectionMemberSelection.Selected));
		}

		private static Fixture CreateFixture(IEnumerable<NormalizedCollectionMember> installedMembers,
			IEnumerable<NormalizedCollectionMember> candidateMembers)
		{
			CollectionIdentity collection = CollectionIdentity.FromNexus("revision-selection-test");
			NormalizedCollectionManifest installedManifest = Manifest(collection, "old", 6, Sha256A, installedMembers);
			NormalizedCollectionManifest candidateManifest = Manifest(collection, "candidate", 5, Sha256B, candidateMembers);
			CollectionEffectiveSelection installed = new CollectionEffectiveSelectionBuilder().Build(
				CollectionCapabilityReport.Create(installedManifest), new CollectionOptionalMemberSelection[0]);
			return new Fixture(installed, CollectionCapabilityReport.Create(candidateManifest));
		}

		private static NormalizedCollectionManifest Manifest(CollectionIdentity collection, string revisionId, int revisionNumber,
			string sha256, IEnumerable<NormalizedCollectionMember> members)
		{
			return new NormalizedCollectionManifest(CollectionRevisionIdentity.FromNexus(collection, revisionId, revisionNumber),
				new CollectionManifestSourceSnapshot(CollectionContentHash.FromSha256(sha256), 100, "schema-v1", "normalizer-v1"),
				CollectionManifestMemberSetCompleteness.Complete, null, members);
		}

		private static NormalizedCollectionMember Member(int ordinal, long modId, long fileId,
			CollectionMemberRequirement requirement, CollectionMemberSelection selection)
		{
			string stableId = "fallout4/" + modId + "/" + fileId;
			return new NormalizedCollectionMember(ordinal,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromValidatedMatch("nexus-mod-file:" + stableId)),
				requirement, selection, new CollectionArtifactReference("nexus-mod-file", stableId, null),
				CollectionRecipeIdentity.FromFingerprint("recipe-" + modId + "-" + fileId), "mod-" + modId);
		}

		private sealed class Fixture
		{
			private readonly CollectionEffectiveSelection _installed;
			private readonly CollectionCapabilityReport _candidate;
			public Fixture(CollectionEffectiveSelection installed, CollectionCapabilityReport candidate)
			{
				_installed = installed;
				_candidate = candidate;
			}
			public CollectionEffectiveSelection Build(IEnumerable<CollectionOptionalMemberSelection> explicitSelections = null)
			{
				return new CollectionRevisionUpdateCandidateSelectionBuilder().Build(_installed, _candidate,
					explicitSelections ?? new CollectionOptionalMemberSelection[0]);
			}
		}
	}
}
