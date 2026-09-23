using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.Mods;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// Verifies C6.15.9 retained-source to native-recipe preparation for the initial characterized basic/simple capability.
	/// </summary>
	[TestFixture]
	public class CollectionNativeRecipePreparerTests
	{
		[Test]
		public void PrepareBasicSimpleExact_ProducesValidatedTranslatedRecipeAndExactPreview()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "supported", @"meshes\body.nif", @"textures\body.dds");

				PreparedCollectionNativeRecipe prepared = fixture.Preparer.PrepareBasicSimpleExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode,
					fixture.InstallContext, fixture.State, false);

				Assert.That(prepared.ProviderRecipeIdentity, Is.EqualTo(fixture.Member.RecipeIdentity));
				Assert.That(prepared.PreparedNativeIdentity.Fingerprint, Does.StartWith("sha256:"));
				Assert.That(prepared.AdapterId, Is.EqualTo(ModInstallationSimpleFileRecipeAdapter.AdapterId));
				Assert.That(prepared.AdapterVersion, Is.EqualTo(ModInstallationSimpleFileRecipeAdapter.AdapterVersion));
				Assert.That(prepared.InstallContext.Method, Is.EqualTo(ModInstallMethod.Virtual));
				Assert.That(prepared.InstallContext.InstallRoot, Is.EqualTo(ModInstallRoot.Data));
				Assert.That(prepared.RecipeInput.HasNativePlan, Is.True);
				Assert.That(prepared.RecipeInput.NativeOperations.Count, Is.EqualTo(2));
				Assert.That(prepared.RecipeInput.NativeOperations.All(x => x is InstallModFileOperation), Is.True);
				Assert.That(prepared.EffectPreview.IsComplete, Is.True);
				Assert.That(prepared.EffectPreview.Files.Count, Is.EqualTo(2));
				Assert.That(prepared.Validation.ExpectedContent.Sha256, Is.EqualTo(fixture.VerifiedArchive.Artifact.ContentHash.Value));
				Assert.That(prepared.Validation.ExpectedContent.ByteLength, Is.EqualTo(fixture.VerifiedArchive.Artifact.ByteLength));
				CollectionAssert.AreEquivalent(new[]
				{
					fixture.SourceRecord.RawManifestArtifactId,
					fixture.VerifiedArchive.Artifact.ArtifactId
				}, prepared.RetainedArtifactIds);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareBasicSimpleExact_SameInputsProduceSamePreparedNativeIdentity()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "deterministic", @"meshes\body.nif");

				PreparedCollectionNativeRecipe first = fixture.Preparer.PrepareBasicSimpleExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode,
					fixture.InstallContext, fixture.State, false);
				PreparedCollectionNativeRecipe second = fixture.Preparer.PrepareBasicSimpleExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode,
					fixture.InstallContext, fixture.State, false);

				Assert.That(second.PreparedNativeIdentity, Is.EqualTo(first.PreparedNativeIdentity));
				Assert.That(second.RecipeInput.OperationIdentity.OperationId, Is.Not.EqualTo(first.RecipeInput.OperationIdentity.OperationId),
					"Preparation attempt identities are intentionally not part of prepared-native semantic identity.");
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareBasicSimpleExact_ReadmeSettingChangesPreparedNativeIdentity()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "readme-setting", @"meshes\body.nif");

				PreparedCollectionNativeRecipe keepReadmes = fixture.Preparer.PrepareBasicSimpleExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode,
					fixture.InstallContext, fixture.State, false);
				PreparedCollectionNativeRecipe skipReadmes = fixture.Preparer.PrepareBasicSimpleExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode,
					fixture.InstallContext, fixture.State, true);

				Assert.That(keepReadmes.SkipReadmeFiles, Is.False);
				Assert.That(skipReadmes.SkipReadmeFiles, Is.True);
				Assert.That(skipReadmes.PreparedNativeIdentity, Is.Not.EqualTo(keepReadmes.PreparedNativeIdentity));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareBasicSimpleExact_RejectsManagedArchiveDifferentFromVerifiedBytes()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "archive-mismatch", @"meshes\body.nif");
				File.WriteAllBytes(fixture.ModArchivePath, Encoding.UTF8.GetBytes("changed archive bytes"));

				Assert.Throws<InvalidDataException>(() => fixture.Preparer.PrepareBasicSimpleExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode,
					fixture.InstallContext, fixture.State, false));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareBasicSimpleExact_RejectsMissingDurableVerifiedArchiveReference()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "missing-reference", @"meshes\body.nif");
				Assert.That(new CollectionsRetainedArtifactReferenceStore(fixture.Store)
					.ReleaseReference(fixture.VerifiedArchive.Reference.ReferenceId), Is.True);

				Assert.Throws<InvalidDataException>(() => fixture.Preparer.PrepareBasicSimpleExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode,
					fixture.InstallContext, fixture.State, false));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareBasicSimpleExact_RejectsStateDifferentFromResolvedPlan()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "stale-state", @"meshes\body.nif");
				CollectionNativeStateIndex changedState = CreateState(fixture.Target, 1);

				Assert.Throws<InvalidOperationException>(() => fixture.Preparer.PrepareBasicSimpleExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode,
					fixture.InstallContext, changedState, false));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareBasicSimpleExact_SpecialFileBehaviorFailsClosedWithoutMutation()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				bool specialInstallCalled = false;
				Fixture fixture = CreateFixture(root, "special", "special.bin");
				fixture.GameMode = CreateGameMode(true, () => specialInstallCalled = true);

				Assert.Throws<NotSupportedException>(() => fixture.Preparer.PrepareBasicSimpleExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode,
					fixture.InstallContext, fixture.State, false));
				Assert.That(specialInstallCalled, Is.False);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static Fixture CreateFixture(string root, string suffix, params string[] archiveFiles)
		{
			var store = new CollectionsStore(root);
			store.CreateNew();
			CollectionIdentity collection = CollectionIdentity.FromNexus("c6159-" + suffix);
			var revision = new CollectionRevision(
				CollectionRevisionIdentity.FromNexus(collection, "revision-" + suffix, 9), "Revision", null, 1);
			new CollectionsCatalogStore(store).SaveDefinitionAndRevision(
				new CollectionDefinition(collection, suffix, null, null), revision);

			string json = "{" +
				"\"info\":{\"author\":\"Curator\",\"authorUrl\":\"https://example.invalid/author\",\"name\":\"Example\",\"description\":\"Example\",\"domainName\":\"skyrim\"}," +
				"\"mods\":[{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20,\"updatePolicy\":\"exact\"}}]," +
				"\"modRules\":[]}";
			byte[] manifestBytes = Encoding.UTF8.GetBytes(json);
			NexusCollectionManifestNormalizationResult normalization = new NexusCollectionManifestNormalizer().Normalize(manifestBytes, revision);
			Assert.That(normalization.CapabilityReport.Status, Is.EqualTo(CollectionCompatibilityStatus.Supported));
			var sourceStore = new CollectionsRevisionSourceStore(store);
			CollectionRevisionSourceRecord sourceRecord = sourceStore.RetainManifest(normalization.Manifest,
				CollectionRevisionSourceInputKind.RawManifest, normalization.Manifest.Source.ContentHash,
				manifestBytes.LongLength, "collection.json", manifestBytes);

			NormalizedCollectionMember normalizedMember = normalization.Manifest.Members.Single();
			var member = new ResolvedCollectionMemberPlan(normalizedMember, CollectionResolvedArtifactChoice.Exact(normalizedMember.Artifact));
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-sha256:" + new string('a', 64));
			CollectionNativeStateIndex state = CreateState(target, 0);
			var plan = new ResolvedCollectionPlan(CollectionPlanIdentity.From(Guid.NewGuid(), 1), target,
				CollectionExecutionPolicy.InstallIntoCurrentSetup(), state.Fingerprint, normalization.CapabilityReport, new[] { member });

			string modArchivePath = Path.Combine(root, "managed-" + suffix + ".7z");
			byte[] archiveBytes = Encoding.UTF8.GetBytes("verified archive bytes for " + suffix);
			File.WriteAllBytes(modArchivePath, archiveBytes);
			var artifactStore = new CollectionsRetainedArtifactStore(store);
			CollectionsRetainedArtifact artifact = artifactStore.PublishFile(modArchivePath);
			CollectionAcquisitionRequest request = CollectionAcquisitionRequest.Create(Guid.NewGuid(), plan, member.MemberKey);
			CollectionsRetainedArtifactReferenceRecord reference = new CollectionsRetainedArtifactReferenceStore(store).AcquireReference(
				artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Download, request.RequestId.ToString("D"), "verified-test");
			var verifiedArchive = new CollectionVerifiedArchive(request, artifact, reference,
				CollectionVerifiedArchiveSourceKind.ManagedArchive, CollectionArchiveVerificationBasis.ProviderContentIdentity);

			List<string> fileList = archiveFiles.ToList();
			IMod mod = InterfaceStub<IMod>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_Filename": return modArchivePath;
					case "get_FileName": return Path.GetFileName(modArchivePath);
					case "GetFileList": return new List<string>(fileList);
					default: return null;
				}
			});

			return new Fixture(store, target, state, plan, member, sourceRecord, verifiedArchive, mod, modArchivePath,
				CreateGameMode(false, null), new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data));
		}

		private static IGameMode CreateGameMode(bool specialFile, Action onSpecialInstall)
		{
			return InterfaceStub<IGameMode>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_Name": return "Test Game";
					case "get_PluginDirectory": return @"C:\Game\Data";
					case "get_UsesPlugins": return false;
					case "get_RequiresSpecialFileInstallation": return specialFile;
					case "IsSpecialFile": return specialFile;
					case "SpecialFileInstall":
						onSpecialInstall?.Invoke();
						return new[] { "transformed.bin" };
					case "get_RequiresModFileMerge": return false;
					case "get_HasSecondaryInstallPath": return false;
					case "GetModFormatAdjustedPath": return AdjustPath(args);
					default: return null;
				}
			});
		}

		private static string AdjustPath(object[] args)
		{
			string path = (string)args[1];
			if (args.Length == 4 && args[3] is ModPathContext)
				return @"Game\" + path;
			if (args.Length == 3 && args[2] is ModPathContext)
				return path;
			if (args.Length == 4 && args[3] is bool)
				return @"Target\" + path;
			if (args.Length == 3 && args[2] is bool)
				return @"Target\" + path;
			return path;
		}

		private static CollectionNativeStateIndex CreateState(CollectionTargetIdentity target, long deploymentCommitSequence)
		{
			return new CollectionNativeStateIndex(target, new CollectionNativeRootState[0],
				new CollectionNativeModState[0], new CollectionNativeFileState[0], new CollectionNativeIniState[0],
				new CollectionNativeGameValueState[0], new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable,
				new CollectionTargetAssociation[0], new CollectionMemberBinding[0], new UserOverride[0],
				CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], deploymentCommitSequence);
		}

		private static string CreateTemporaryDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "nmm-c6-15-9-native-recipe-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}

		private sealed class Fixture
		{
			public Fixture(CollectionsStore store, CollectionTargetIdentity target, CollectionNativeStateIndex state,
				ResolvedCollectionPlan plan, ResolvedCollectionMemberPlan member, CollectionRevisionSourceRecord sourceRecord,
				CollectionVerifiedArchive verifiedArchive, IMod mod, string modArchivePath, IGameMode gameMode,
				ModInstallContext installContext)
			{
				Store = store;
				Target = target;
				State = state;
				Plan = plan;
				Member = member;
				SourceRecord = sourceRecord;
				VerifiedArchive = verifiedArchive;
				Mod = mod;
				ModArchivePath = modArchivePath;
				GameMode = gameMode;
				InstallContext = installContext;
				Preparer = new CollectionNativeRecipePreparer(store);
			}

			public CollectionsStore Store { get; }
			public CollectionTargetIdentity Target { get; }
			public CollectionNativeStateIndex State { get; }
			public ResolvedCollectionPlan Plan { get; }
			public ResolvedCollectionMemberPlan Member { get; }
			public CollectionRevisionSourceRecord SourceRecord { get; }
			public CollectionVerifiedArchive VerifiedArchive { get; }
			public IMod Mod { get; }
			public string ModArchivePath { get; }
			public IGameMode GameMode { get; set; }
			public ModInstallContext InstallContext { get; }
			public CollectionNativeRecipePreparer Preparer { get; }
		}
	}
}
