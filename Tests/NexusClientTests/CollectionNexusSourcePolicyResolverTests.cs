using System;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModRepositories;
using Nexus.Client.ModManagement;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using Nexus.Client.OnlineServices.NexusMods.V1;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	[Category("CollectionsC12Compatibility")]
	public class CollectionNexusSourcePolicyResolverTests
	{
		[Test]
		public void Resolve_PreferExactAvailableBecomesSupportedWithExactChoice()
		{
			string root = NewRoot();
			try
			{
				Fixture fixture = CreateFixture(root, "prefer");
				int calls = 0;
				var resolver = new CollectionNexusSourcePolicyResolver(fixture.SourceStore,
					(domain, modId, fileId, policy, requestedVersion) =>
					{
						calls++;
						Assert.AreEqual("fallout4", domain);
						Assert.AreEqual(66566, modId);
						Assert.AreEqual(259831, fileId);
						Assert.AreEqual("prefer", policy);
						Assert.AreEqual("1.2", requestedVersion);
						return CollectionNexusSourcePolicyFileResolution.Resolved(fileId);
					});

				CollectionNexusSourcePolicyResolution result = resolver.Resolve(fixture.Selection);
				NormalizedCollectionMember member = fixture.Selection.Manifest.Members.Single();
				CollectionResolvedArtifactChoice choice = result.ArtifactChoices[member.IdentityResolution.Key];

				Assert.AreEqual(1, calls);
				Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.Selection.CapabilityReport.Status);
				Assert.IsFalse(result.Selection.CapabilityReport.AllIssues.Any(x => x.Code == CollectionNexusSourcePolicyResolver.PreferIssueCode));
				Assert.AreEqual(CollectionResolvedArtifactChoiceKind.ExactRequestedArtifact, choice.Kind);
				Assert.AreEqual(member.Artifact, choice.SelectedArtifact);
				Assert.AreSame(fixture.Selection.Manifest, result.Selection.Manifest);
				Assert.AreEqual(fixture.Selection.SelectionFingerprint, result.Selection.SelectionFingerprint);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Resolve_PreferFallbackFreezesSupportedSubstitution()
		{
			string root = NewRoot();
			try
			{
				Fixture fixture = CreateFixture(root, "prefer");
				var resolver = new CollectionNexusSourcePolicyResolver(fixture.SourceStore,
					(domain, modId, fileId, policy, requestedVersion) => CollectionNexusSourcePolicyFileResolution.Resolved(300001));

				CollectionNexusSourcePolicyResolution result = resolver.Resolve(fixture.Selection);
				NormalizedCollectionMember member = fixture.Selection.Manifest.Members.Single();
				CollectionResolvedArtifactChoice choice = result.ArtifactChoices[member.IdentityResolution.Key];
				string parsedDomain;
				long parsedModId;
				long parsedFileId;

				Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.Selection.CapabilityReport.Status);
				Assert.AreEqual(CollectionResolvedArtifactChoiceKind.SupportedSubstitution, choice.Kind);
				Assert.AreEqual(CollectionNexusSourcePolicyResolver.PreferFallbackSubstitutionRuleId, choice.SubstitutionRuleId);
				Assert.IsTrue(NexusCollectionModFileArtifactIdentity.TryParse(choice.SelectedArtifact, out parsedDomain, out parsedModId, out parsedFileId));
				Assert.AreEqual("fallout4", parsedDomain);
				Assert.AreEqual(66566, parsedModId);
				Assert.AreEqual(300001, parsedFileId);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Resolve_LatestFreezesSupportedSubstitution()
		{
			string root = NewRoot();
			try
			{
				Fixture fixture = CreateFixture(root, "latest");
				var resolver = new CollectionNexusSourcePolicyResolver(fixture.SourceStore,
					(domain, modId, fileId, policy, requestedVersion) =>
					{
						Assert.AreEqual("latest", policy);
						return CollectionNexusSourcePolicyFileResolution.Resolved(300002);
					});

				CollectionNexusSourcePolicyResolution result = resolver.Resolve(fixture.Selection);
				CollectionResolvedArtifactChoice choice = result.ArtifactChoices[fixture.Selection.Manifest.Members.Single().IdentityResolution.Key];

				Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.Selection.CapabilityReport.Status);
				Assert.IsFalse(result.Selection.CapabilityReport.AllIssues.Any(x => x.Code == CollectionNexusSourcePolicyResolver.LatestIssueCode));
				Assert.AreEqual(CollectionResolvedArtifactChoiceKind.SupportedSubstitution, choice.Kind);
				Assert.AreEqual(CollectionNexusSourcePolicyResolver.LatestSubstitutionRuleId, choice.SubstitutionRuleId);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Resolve_UnresolvedPolicyRemainsActionRequired()
		{
			string root = NewRoot();
			try
			{
				Fixture fixture = CreateFixture(root, "prefer");
				var resolver = new CollectionNexusSourcePolicyResolver(fixture.SourceStore,
					(domain, modId, fileId, policy, requestedVersion) => CollectionNexusSourcePolicyFileResolution.Unresolved());

				CollectionNexusSourcePolicyResolution result = resolver.Resolve(fixture.Selection);

				Assert.AreEqual(CollectionCompatibilityStatus.ActionRequired, result.Selection.CapabilityReport.Status);
				Assert.AreEqual(0, result.ArtifactChoices.Count);
				Assert.IsTrue(result.Selection.CapabilityReport.AllIssues.Any(x => x.Code == CollectionNexusSourcePolicyResolver.PreferIssueCode));
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void ReviewedExactPreferChoiceRehydratesWithoutProviderCall()
		{
			string root = NewRoot();
			try
			{
				Fixture fixture = CreateFixture(root, "prefer");
				NormalizedCollectionMember member = fixture.Selection.Manifest.Members.Single();
				var reviewed = new CollectionReviewedMemberSnapshot(member.IdentityResolution.Key, member.SourceOrdinal,
					member.Requirement, member.InstallationPhase, member.RecipeIdentity.Fingerprint,
					CollectionResolvedArtifactChoiceKind.ExactRequestedArtifact, member.Artifact, member.Artifact, null);

				CollectionEffectiveSelection result = CollectionNexusSourcePolicyResolver.ReapplyReviewedChoices(
					fixture.Selection, fixture.RawManifest, new[] { reviewed });

				Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
				Assert.IsFalse(result.CapabilityReport.AllIssues.Any(x => x.Code == CollectionNexusSourcePolicyResolver.PreferIssueCode));
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void ReviewedLatestSubstitutionRehydratesWithStableRuleAndSameNexusMod()
		{
			string root = NewRoot();
			try
			{
				Fixture fixture = CreateFixture(root, "latest");
				NormalizedCollectionMember member = fixture.Selection.Manifest.Members.Single();
				var selected = new CollectionArtifactReference(NexusCollectionModFileArtifactIdentity.Scheme,
					NexusCollectionModFileArtifactIdentity.Format("fallout4", 66566, 300003), null);
				var reviewed = new CollectionReviewedMemberSnapshot(member.IdentityResolution.Key, member.SourceOrdinal,
					member.Requirement, member.InstallationPhase, member.RecipeIdentity.Fingerprint,
					CollectionResolvedArtifactChoiceKind.SupportedSubstitution, member.Artifact, selected,
					CollectionNexusSourcePolicyResolver.LatestSubstitutionRuleId);

				CollectionEffectiveSelection result = CollectionNexusSourcePolicyResolver.ReapplyReviewedChoices(
					fixture.Selection, fixture.RawManifest, new[] { reviewed });

				Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
				Assert.IsFalse(result.CapabilityReport.AllIssues.Any(x => x.Code == CollectionNexusSourcePolicyResolver.LatestIssueCode));
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void ReviewedLatestSubstitutionWithUnknownRuleRemainsActionRequired()
		{
			string root = NewRoot();
			try
			{
				Fixture fixture = CreateFixture(root, "latest");
				NormalizedCollectionMember member = fixture.Selection.Manifest.Members.Single();
				var selected = new CollectionArtifactReference(NexusCollectionModFileArtifactIdentity.Scheme,
					NexusCollectionModFileArtifactIdentity.Format("fallout4", 66566, 300003), null);
				var reviewed = new CollectionReviewedMemberSnapshot(member.IdentityResolution.Key, member.SourceOrdinal,
					member.Requirement, member.InstallationPhase, member.RecipeIdentity.Fingerprint,
					CollectionResolvedArtifactChoiceKind.SupportedSubstitution, member.Artifact, selected, "unknown-rule");

				CollectionEffectiveSelection result = CollectionNexusSourcePolicyResolver.ReapplyReviewedChoices(
					fixture.Selection, fixture.RawManifest, new[] { reviewed });

				Assert.AreEqual(CollectionCompatibilityStatus.ActionRequired, result.CapabilityReport.Status);
				Assert.IsTrue(result.CapabilityReport.AllIssues.Any(x => x.Code == CollectionNexusSourcePolicyResolver.LatestIssueCode));
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void ResolveInstalled_LatestUsesBoundNativeFileWithoutProviderLookup()
		{
			string root = NewRoot();
			try
			{
				Fixture fixture = CreateFixture(root, "latest");
				NormalizedCollectionMember member = fixture.Selection.Manifest.Members.Single();
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-installed-policy");
				var association = new CollectionTargetAssociation(Guid.NewGuid(), fixture.Selection.Manifest.Revision, target, CollectionAssociationState.Applied);
				var native = new CollectionNativeModState(new NativeModInstanceIdentity(target, "native-policy"), "C:\\Mods\\policy.7z", "policy.7z",
					"66566", "300003", "1.3", "1.3.0.0", ModInstallRoot.Data, ModInstallMethod.Virtual);
				var binding = new CollectionMemberBinding(association, member.IdentityResolution.Key, native.Identity, member.RecipeIdentity,
					CollectionMemberBindingKind.InstalledForCollection);
				var state = new CollectionNativeStateIndex(target, new CollectionNativeRootState[0], new[] { native }, new CollectionNativeFileState[0],
					new CollectionNativeIniState[0], new CollectionNativeGameValueState[0], new CollectionNativePluginState[0],
					CollectionNativeStateCoverage.NotApplicable, new[] { association }, new[] { binding }, new UserOverride[0],
					CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
				var resolver = new CollectionNexusSourcePolicyResolver(fixture.SourceStore,
					(domain, modId, fileId, policy, requestedVersion) => { Assert.Fail("Installed policy reconstruction must not query the provider."); return null; });

				CollectionNexusSourcePolicyResolution result = resolver.ResolveInstalled(fixture.Selection, new[] { binding }, state);
				CollectionResolvedArtifactChoice choice = result.ArtifactChoices[member.IdentityResolution.Key];
				string parsedDomain; long parsedModId; long parsedFileId;
				Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.Selection.CapabilityReport.Status);
				Assert.AreEqual(CollectionResolvedArtifactChoiceKind.SupportedSubstitution, choice.Kind);
				Assert.AreEqual(CollectionNexusSourcePolicyResolver.LatestSubstitutionRuleId, choice.SubstitutionRuleId);
				Assert.IsTrue(NexusCollectionModFileArtifactIdentity.TryParse(choice.SelectedArtifact, out parsedDomain, out parsedModId, out parsedFileId));
				Assert.AreEqual(300003, parsedFileId);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void ReviewedChoiceDictionary_RehydratesLatestWithoutManifestProviderLookup()
		{
			string root = NewRoot();
			try
			{
				Fixture fixture = CreateFixture(root, "latest");
				NormalizedCollectionMember member = fixture.Selection.Manifest.Members.Single();
				var selected = new CollectionArtifactReference(NexusCollectionModFileArtifactIdentity.Scheme,
					NexusCollectionModFileArtifactIdentity.Format("fallout4", 66566, 300004), null);
				var choice = CollectionResolvedArtifactChoice.SupportedSubstitution(member.Artifact, selected,
					CollectionNexusSourcePolicyResolver.LatestSubstitutionRuleId);

				CollectionEffectiveSelection result = CollectionNexusSourcePolicyResolver.ReapplyReviewedChoices(fixture.Selection,
					new System.Collections.Generic.Dictionary<CollectionMemberKey, CollectionResolvedArtifactChoice> { { member.IdentityResolution.Key, choice } });

				Assert.AreEqual(CollectionCompatibilityStatus.Supported, result.CapabilityReport.Status);
				Assert.IsFalse(result.CapabilityReport.AllIssues.Any(x => x.Code == CollectionNexusSourcePolicyResolver.LatestIssueCode));
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Candidate_PreferUncoercibleCuratorVersionFailsClosed()
		{
			NexusV1ModFileList files = FileList(
				new[]
				{
					File(200, "banana", NexusV1FileCategory.Archived, 1),
					File(201, "2.0", NexusV1FileCategory.Update, 2)
				},
				new[] { Update(200, 201, 2) });

			Assert.AreEqual(0, NexusModsApiRepository.ResolveCollectionSourcePolicyCandidate(files, 200, "prefer", "banana"));
		}

		[Test]
		public void Candidate_LatestFollowsReachableUpdateChainAndUsesNewestTimestamp()
		{
			NexusV1ModFileList files = FileList(
				new[]
				{
					File(200, "1.0", NexusV1FileCategory.Main, 1),
					File(201, "1.1", NexusV1FileCategory.Update, 3),
					File(202, "1.2", NexusV1FileCategory.Update, 2)
				},
				new[] { Update(200, 201, 3), Update(201, 202, 2) });

			Assert.AreEqual(201, NexusModsApiRepository.ResolveCollectionSourcePolicyCandidate(files, 200, "latest", "1.0"));
		}

		[Test]
		public void Candidate_PreferUsesOnlyUpdatesAtOrAboveCuratorVersion()
		{
			NexusV1ModFileList files = FileList(
				new[]
				{
					File(200, "1.2", NexusV1FileCategory.Archived, 1),
					File(201, "1.1", NexusV1FileCategory.Update, 3),
					File(202, "v1.3-beta", NexusV1FileCategory.Update, 2)
				},
				new[] { Update(200, 201, 3), Update(201, 202, 2) });

			Assert.AreEqual(202, NexusModsApiRepository.ResolveCollectionSourcePolicyCandidate(files, 200, "prefer", "release-1.2"));
		}

		[Test]
		public void Candidate_LatestUsesSingleCurrentFileWhenNoUpdateChainExists()
		{
			NexusV1ModFileList files = FileList(
				new[] { File(300, "2.0", NexusV1FileCategory.Main, 1) },
				new NexusV1ModFileUpdate[0]);

			Assert.AreEqual(300, NexusModsApiRepository.ResolveCollectionSourcePolicyCandidate(files, 200, "latest", "1.0"));
		}

		[Test]
		public void Candidate_AmbiguousCurrentFilesFallBackToRequestedFileLikeVortex()
		{
			NexusV1ModFileList files = FileList(
				new[]
				{
					File(200, "1.0", NexusV1FileCategory.Main, 1),
					File(300, "2.0", NexusV1FileCategory.Main, 2)
				},
				new NexusV1ModFileUpdate[0]);

			Assert.AreEqual(200, NexusModsApiRepository.ResolveCollectionSourcePolicyCandidate(files, 200, "latest", "1.0"));
		}

		[Test]
		public void Candidate_UpdateCycleTerminatesAndSelectsNewestReachableCandidate()
		{
			NexusV1ModFileList files = FileList(
				new[]
				{
					File(200, "1.0", NexusV1FileCategory.Main, 1),
					File(201, "1.1", NexusV1FileCategory.Update, 2)
				},
				new[] { Update(200, 201, 2), Update(201, 200, 1) });

			Assert.AreEqual(201, NexusModsApiRepository.ResolveCollectionSourcePolicyCandidate(files, 200, "latest", "1.0"));
		}

		private static NexusV1ModFileList FileList(NexusV1ModFile[] files, NexusV1ModFileUpdate[] updates)
		{
			return new NexusV1ModFileList { Files = files, FileUpdates = updates };
		}

		private static NexusV1ModFile File(int id, string version, NexusV1FileCategory category, int day)
		{
			return new NexusV1ModFile
			{
				FileId = id,
				FileVersion = version,
				Category = category,
				UploadedTimestamp = new DateTimeOffset(2026, 1, day, 0, 0, 0, TimeSpan.Zero)
			};
		}

		private static NexusV1ModFileUpdate Update(int oldFileId, int newFileId, int day)
		{
			return new NexusV1ModFileUpdate
			{
				OldFileId = oldFileId,
				NewFileId = newFileId,
				UploadedTimestamp = new DateTimeOffset(2026, 1, day, 0, 0, 0, TimeSpan.Zero)
			};
		}

		private static Fixture CreateFixture(string root, string updatePolicy)
		{
			var store = new CollectionsStore(root);
			store.CreateNew();
			CollectionIdentity collection = CollectionIdentity.FromNexus("55233");
			var revision = new CollectionRevision(CollectionRevisionIdentity.FromNexus(collection, "791455", 11),
				"Module 02 - HUD", null, 1);
			new CollectionsCatalogStore(store).SaveDefinitionAndRevision(
				new CollectionDefinition(collection, "Module 02 - HUD", "Sephrajin", null), revision);

			string json = "{" +
				"\"info\":{\"author\":\"Sephrajin\",\"authorUrl\":\"https://example.invalid\",\"name\":\"Module 02 - HUD\",\"description\":\"HUD\",\"domainName\":\"fallout4\"}," +
				"\"mods\":[{\"name\":\"JHUD - FallUI HUD Preset\",\"version\":\"1.2\",\"optional\":false,\"domainName\":\"fallout4\",\"source\":{\"type\":\"nexus\",\"modId\":66566,\"fileId\":259831,\"updatePolicy\":\"" + updatePolicy + "\"}}]," +
				"\"modRules\":[]}";
			byte[] bytes = Encoding.UTF8.GetBytes(json);
			NexusCollectionManifestNormalizationResult normalized = new NexusCollectionManifestNormalizer().Normalize(bytes, revision);
			CollectionEffectiveSelection selection = new CollectionEffectiveSelectionBuilder().Build(normalized.CapabilityReport,
				new CollectionOptionalMemberSelection[0]);
			var sourceStore = new CollectionsRevisionSourceStore(store);
			sourceStore.RetainManifest(normalized.Manifest, CollectionRevisionSourceInputKind.RawManifest,
				normalized.Manifest.Source.ContentHash, bytes.LongLength, "collection.json", bytes);
			return new Fixture(sourceStore, selection, bytes);
		}

		private static string NewRoot()
		{
			return Path.Combine(Path.GetTempPath(), "nmm-c11-source-policy-" + Guid.NewGuid().ToString("N"));
		}

		private sealed class Fixture
		{
			public Fixture(CollectionsRevisionSourceStore sourceStore, CollectionEffectiveSelection selection, byte[] rawManifest)
			{
				SourceStore = sourceStore;
				Selection = selection;
				RawManifest = rawManifest;
			}
			public CollectionsRevisionSourceStore SourceStore { get; }
			public CollectionEffectiveSelection Selection { get; }
			public byte[] RawManifest { get; }
		}
	}
}
