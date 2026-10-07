using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Nexus.Client;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.ModManagement.Scripting.XmlScript;
using Nexus.Client.Mods;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using Nexus.Client.PluginManagement;
using Nexus.Client.Plugins;
using Nexus.Client.Util.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// Verifies C6.15.9 retained-source to native-recipe preparation for characterized basic/simple and bounded FOMOD capabilities.
	/// </summary>
	[TestFixture]
	[Category("CollectionsC12Compatibility")]
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
		public void PrepareBasicSimpleExact_ReviewedPreferExactDecisionSurvivesRetainedSourceRevalidation()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixtureCore(root, "prefer-exact-reviewed", null,
					new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), false,
					choicesJson: null, installScript: null, pluginsJson: null, updatePolicy: "prefer",
					archiveFiles: new[] { @"textures\body.dds" });

				PreparedCollectionNativeRecipe prepared = fixture.Preparer.PrepareBasicSimpleExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode,
					fixture.InstallContext, fixture.State, false);

				Assert.That(prepared.RecipeInput.HasNativePlan, Is.True);
				Assert.That(fixture.Plan.CapabilityReport.Status, Is.EqualTo(CollectionCompatibilityStatus.Supported));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareBasicSimpleExact_ReviewedPreferSubstitutionIsPreparedAndBoundIntoRepairReview()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixtureCore(root, "prefer-substitution-reviewed", null,
					new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), false,
					choicesJson: null, installScript: null, pluginsJson: null, updatePolicy: "prefer",
					archiveFiles: new[] { @"textures\body.dds" });

				PreparedCollectionNativeRecipe exactPrepared = fixture.Preparer.PrepareBasicSimpleExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode,
					fixture.InstallContext, fixture.State, false);

				NormalizedCollectionMember normalized = fixture.Plan.CapabilityReport.Manifest.Members.Single();
				var selectedArtifact = new CollectionArtifactReference(NexusCollectionModFileArtifactIdentity.Scheme,
					NexusCollectionModFileArtifactIdentity.Format("skyrim", 10, 30), null);
				var choice = CollectionResolvedArtifactChoice.SupportedSubstitution(normalized.Artifact, selectedArtifact,
					CollectionNexusSourcePolicyResolver.PreferFallbackSubstitutionRuleId);
				var substitutedMember = new ResolvedCollectionMemberPlan(normalized, choice);
				var substitutedPlan = new ResolvedCollectionPlan(CollectionPlanIdentity.From(Guid.NewGuid(), 1), fixture.Target,
					CollectionExecutionPolicy.InstallIntoCurrentSetup(), fixture.State.Fingerprint, fixture.Plan.CapabilityReport,
					new[] { substitutedMember });
				CollectionAcquisitionRequest request = CollectionAcquisitionRequest.Create(Guid.NewGuid(), substitutedPlan, substitutedMember.MemberKey);
				CollectionsRetainedArtifactReferenceRecord reference = new CollectionsRetainedArtifactReferenceStore(fixture.Store).AcquireReference(
					fixture.VerifiedArchive.Artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Download, request.RequestId.ToString("D"), "verified-policy-test");
				var substitutedArchive = new CollectionVerifiedArchive(request, fixture.VerifiedArchive.Artifact, reference,
					CollectionVerifiedArchiveSourceKind.ManagedArchive, CollectionArchiveVerificationBasis.ProviderContentIdentity);

				PreparedCollectionNativeRecipe substitutedPrepared = fixture.Preparer.PrepareBasicSimpleExact(
					substitutedPlan, substitutedMember, substitutedArchive, fixture.Mod, fixture.GameMode,
					fixture.InstallContext, fixture.State, false);
				CollectionVerifyRepairPreparedRecipeReview exactReview = CollectionVerifyRepairPreparedRecipeReview.From(exactPrepared);
				CollectionVerifyRepairPreparedRecipeReview substitutedReview = CollectionVerifyRepairPreparedRecipeReview.From(substitutedPrepared);

				var otherSelectedArtifact = new CollectionArtifactReference(NexusCollectionModFileArtifactIdentity.Scheme,
					NexusCollectionModFileArtifactIdentity.Format("skyrim", 10, 31), null);
				var otherChoice = CollectionResolvedArtifactChoice.SupportedSubstitution(normalized.Artifact, otherSelectedArtifact,
					CollectionNexusSourcePolicyResolver.PreferFallbackSubstitutionRuleId);
				var otherMember = new ResolvedCollectionMemberPlan(normalized, otherChoice);
				var otherPlan = new ResolvedCollectionPlan(CollectionPlanIdentity.From(Guid.NewGuid(), 1), fixture.Target,
					CollectionExecutionPolicy.InstallIntoCurrentSetup(), fixture.State.Fingerprint, fixture.Plan.CapabilityReport, new[] { otherMember });
				CollectionAcquisitionRequest otherRequest = CollectionAcquisitionRequest.Create(Guid.NewGuid(), otherPlan, otherMember.MemberKey);
				CollectionsRetainedArtifactReferenceRecord otherReference = new CollectionsRetainedArtifactReferenceStore(fixture.Store).AcquireReference(
					fixture.VerifiedArchive.Artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Download, otherRequest.RequestId.ToString("D"), "verified-policy-other-test");
				var otherArchive = new CollectionVerifiedArchive(otherRequest, fixture.VerifiedArchive.Artifact, otherReference,
					CollectionVerifiedArchiveSourceKind.ManagedArchive, CollectionArchiveVerificationBasis.ProviderContentIdentity);
				PreparedCollectionNativeRecipe otherPrepared = fixture.Preparer.PrepareBasicSimpleExact(
					otherPlan, otherMember, otherArchive, fixture.Mod, fixture.GameMode, fixture.InstallContext, fixture.State, false);

				Assert.That(substitutedPrepared.Member.ArtifactChoice, Is.EqualTo(choice));
				Assert.That(substitutedReview.Matches(substitutedPrepared), Is.True);
				Assert.That(exactReview.Matches(substitutedPrepared), Is.False,
					"A restart review created for the curator file must not silently accept a source-policy substitution.");
				Assert.That(substitutedReview.Matches(otherPrepared), Is.False,
					"A reviewed source-policy substitution must remain bound to its exact selected Nexus file across restart.");
				Assert.That(substitutedReview.ExecutableDescriptorFingerprint, Is.Not.EqualTo(exactReview.ExecutableDescriptorFingerprint));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareBasicSimpleExact_UncharacterizedSubstitutionRuleFailsClosed()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixtureCore(root, "prefer-unknown-substitution", null,
					new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), false,
					choicesJson: null, installScript: null, pluginsJson: null, updatePolicy: "prefer",
					archiveFiles: new[] { @"textures\body.dds" });
				NormalizedCollectionMember normalized = fixture.Plan.CapabilityReport.Manifest.Members.Single();
				var selectedArtifact = new CollectionArtifactReference(NexusCollectionModFileArtifactIdentity.Scheme,
					NexusCollectionModFileArtifactIdentity.Format("skyrim", 10, 30), null);
				var member = new ResolvedCollectionMemberPlan(normalized,
					CollectionResolvedArtifactChoice.SupportedSubstitution(normalized.Artifact, selectedArtifact, "unknown-source-policy-rule"));
				var plan = new ResolvedCollectionPlan(CollectionPlanIdentity.From(Guid.NewGuid(), 1), fixture.Target,
					CollectionExecutionPolicy.InstallIntoCurrentSetup(), fixture.State.Fingerprint, fixture.Plan.CapabilityReport, new[] { member });
				CollectionAcquisitionRequest request = CollectionAcquisitionRequest.Create(Guid.NewGuid(), plan, member.MemberKey);
				CollectionsRetainedArtifactReferenceRecord reference = new CollectionsRetainedArtifactReferenceStore(fixture.Store).AcquireReference(
					fixture.VerifiedArchive.Artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Download, request.RequestId.ToString("D"), "unknown-policy-test");
				var archive = new CollectionVerifiedArchive(request, fixture.VerifiedArchive.Artifact, reference,
					CollectionVerifiedArchiveSourceKind.ManagedArchive, CollectionArchiveVerificationBasis.ProviderContentIdentity);

				Assert.Throws<NotSupportedException>(() => fixture.Preparer.PrepareBasicSimpleExact(
					plan, member, archive, fixture.Mod, fixture.GameMode, fixture.InstallContext, fixture.State, false));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareExact_VortexMd5FileListResolvesContentAndFreezesSimpleRecipe()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				byte[] wanted = Encoding.UTF8.GetBytes("wanted file bytes");
				byte[] ignored = Encoding.UTF8.GetBytes("ignored file bytes");
				string wantedMd5 = ComputeMd5(wanted);
				var contents = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
				{
					{ @"payload\opaque.bin", wanted },
					{ @"extras\ignored.txt", ignored }
				};
				Fixture fixture = CreateFileListFixture(root, "file-list", contents,
					"[{\"path\":\"textures/body.dds\",\"md5\":\"" + wantedMd5 + "\"}]");

				PreparedCollectionNativeRecipe prepared = fixture.Preparer.PrepareExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode, CreateEnvironmentInfo(),
					fixture.InstallContext, fixture.State, false);

				Assert.That(prepared.AdapterId, Is.EqualTo(ModInstallationSimpleFileRecipeAdapter.AdapterId));
				InstallModFileOperation operation = prepared.RecipeInput.NativeOperations.OfType<InstallModFileOperation>().Single();
				Assert.That(operation.SourcePath, Is.EqualTo(@"payload\opaque.bin"));
				Assert.That(operation.DestinationPath, Is.EqualTo(@"textures\body.dds"));
				Assert.That(prepared.RecipeInput.NativeOperations.Count, Is.EqualTo(1));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareExact_VortexXxh64FileListResolvesContentAndFreezesSimpleRecipe()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				byte[] wanted = Encoding.ASCII.GetBytes("abc");
				var contents = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
				{
					{ @"payload\opaque.bin", wanted },
					{ @"extras\ignored.txt", Encoding.UTF8.GetBytes("ignored") }
				};
				Fixture fixture = CreateFileListFixture(root, "file-list-xxh64", contents,
					"[{\"path\":\"first.bin\",\"xxh64\":\"RLws9a13CZk=\"},{\"path\":\"second.bin\",\"xxh64\":\"RLws9a13CZk=\"}]");

				PreparedCollectionNativeRecipe prepared = fixture.Preparer.PrepareExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode, CreateEnvironmentInfo(),
					fixture.InstallContext, fixture.State, false);

				InstallModFileOperation[] operations = prepared.RecipeInput.NativeOperations.OfType<InstallModFileOperation>().ToArray();
				Assert.That(operations.Length, Is.EqualTo(2));
				Assert.That(operations.Select(x => x.SourcePath), Is.All.EqualTo(@"payload\opaque.bin"));
				CollectionAssert.AreEquivalent(new[] { "first.bin", "second.bin" }, operations.Select(x => x.DestinationPath));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareExact_VortexMd5FileListAmbiguousArchiveContentFailsClosed()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				byte[] bytes = Encoding.UTF8.GetBytes("same bytes");
				string md5 = ComputeMd5(bytes);
				var contents = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
				{
					{ @"a\one.bin", bytes },
					{ @"b\two.bin", bytes }
				};
				Fixture fixture = CreateFileListFixture(root, "file-list-ambiguous", contents,
					"[{\"path\":\"target.bin\",\"md5\":\"" + md5 + "\"}]");

				Assert.Throws<InvalidDataException>(() => fixture.Preparer.PrepareExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode, CreateEnvironmentInfo(),
					fixture.InstallContext, fixture.State, false));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareExact_VortexMd5FileListReplicatesOneSourceToSeveralDestinations()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				byte[] bytes = Encoding.UTF8.GetBytes("replicated bytes");
				string md5 = ComputeMd5(bytes);
				var contents = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
				{
					{ @"payload\one.bin", bytes }
				};
				Fixture fixture = CreateFileListFixture(root, "file-list-replicate", contents,
					"[{\"path\":\"first.bin\",\"md5\":\"" + md5 + "\"},{\"path\":\"second.bin\",\"md5\":\"" + md5 + "\"}]");

				PreparedCollectionNativeRecipe prepared = fixture.Preparer.PrepareExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode, CreateEnvironmentInfo(),
					fixture.InstallContext, fixture.State, false);

				InstallModFileOperation[] operations = prepared.RecipeInput.NativeOperations.OfType<InstallModFileOperation>().ToArray();
				Assert.That(operations.Length, Is.EqualTo(2));
				Assert.That(operations.Select(x => x.SourcePath), Is.All.EqualTo(@"payload\one.bin"));
				CollectionAssert.AreEquivalent(new[] { "first.bin", "second.bin" }, operations.Select(x => x.DestinationPath));
				Assert.That(prepared.EffectPreview.Files.Count, Is.EqualTo(2));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareExact_VortexFileOverridesSuppressExactTranslatedOutputBeforeEffectPreview()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFileOverrideFixture(root, "file-overrides",
					"[\"C:/Curator/Skyrim/Target/textures/one.dds\"]",
					@"textures\one.dds", @"textures\two.dds");

				PreparedCollectionNativeRecipe prepared = fixture.Preparer.PrepareExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode, CreateEnvironmentInfo(),
					fixture.InstallContext, fixture.State, false);

				InstallModFileOperation[] operations = prepared.RecipeInput.NativeOperations.OfType<InstallModFileOperation>().ToArray();
				Assert.That(operations.Length, Is.EqualTo(1));
				Assert.That(operations[0].SourcePath, Is.EqualTo(@"textures\two.dds"));
				Assert.That(prepared.EffectPreview.Files.Count, Is.EqualTo(1));
				Assert.That(prepared.EffectPreview.Files.Single().Target.RelativePath, Does.EndWith(@"textures\two.dds"));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareExact_VortexFileOverridesFilterFrozenFileListRecipe()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				byte[] one = Encoding.UTF8.GetBytes("one");
				byte[] two = Encoding.UTF8.GetBytes("two");
				string hashesJson = "[{\"path\":\"first.dds\",\"md5\":\"" + ComputeMd5(one) +
					"\"},{\"path\":\"second.dds\",\"md5\":\"" + ComputeMd5(two) + "\"}]";
				var contents = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
				{
					{ @"payload\one.bin", one },
					{ @"payload\two.bin", two }
				};
				Fixture fixture = CreateFileListOverrideFixture(root, "file-list-overrides", contents, hashesJson,
					"[\"second.dds\"]");

				PreparedCollectionNativeRecipe prepared = fixture.Preparer.PrepareExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode, CreateEnvironmentInfo(),
					fixture.InstallContext, fixture.State, false);

				InstallModFileOperation operation = prepared.RecipeInput.NativeOperations.OfType<InstallModFileOperation>().Single();
				Assert.That(operation.SourcePath, Is.EqualTo(@"payload\one.bin"));
				Assert.That(operation.DestinationPath, Is.EqualTo("first.dds"));
				Assert.That(prepared.EffectPreview.Files.Single().Target.RelativePath, Does.EndWith("first.dds"));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareExact_VortexFileOverridesSupportPortableRelativeTargetPaths()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFileOverrideFixture(root, "file-overrides-relative",
					"[\"Target/textures/one.dds\"]", @"textures\one.dds", @"textures\two.dds");

				PreparedCollectionNativeRecipe prepared = fixture.Preparer.PrepareExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode, CreateEnvironmentInfo(),
					fixture.InstallContext, fixture.State, false);

				Assert.That(prepared.RecipeInput.NativeOperations.OfType<InstallModFileOperation>().Single().SourcePath,
					Is.EqualTo(@"textures\two.dds"));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareExact_UnresolvedVortexFileOverrideFailsClosed()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFileOverrideFixture(root, "file-overrides-stale",
					"[\"C:/Curator/Skyrim/Data/stale/missing.dds\"]", @"textures\one.dds");

				Assert.Throws<NotSupportedException>(() => fixture.Preparer.PrepareExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode, CreateEnvironmentInfo(),
					fixture.InstallContext, fixture.State, false));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareExact_VortexFileOverridesSuppressingEveryOutputFailsClosed()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFileOverrideFixture(root, "file-overrides-empty",
					"[\"Target/textures/one.dds\"]", @"textures\one.dds");

				Assert.Throws<NotSupportedException>(() => fixture.Preparer.PrepareExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode, CreateEnvironmentInfo(),
					fixture.InstallContext, fixture.State, false));
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

		/// <summary>Verified revision removals may change state without changing the approved basic, file-list or FOMOD output.</summary>
		[TestCase("basic")]
		[TestCase("file-list")]
		[TestCase("fomod")]
		public void PrepareRevisionUpdateExact_ChangedBoundaryRetainsApprovedIdentityAndOrdinaryPreparationRemainsStrict(string kind)
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture;
				if (kind == "fomod")
					fixture = CreateFomodFixture(root, "revision-boundary-fomod", false);
				else if (kind == "file-list")
				{
					byte[] bytes = Encoding.UTF8.GetBytes("revision boundary bytes");
					fixture = CreateFileListFixture(root, "revision-boundary-file-list",
						new Dictionary<string, byte[]> { { @"payload\one.bin", bytes } },
						"[{\"path\":\"first.bin\",\"md5\":\"" + ComputeMd5(bytes) + "\"}]");
				}
				else
					fixture = CreateFixture(root, "revision-boundary-basic", @"meshes\body.nif");
				PreparedCollectionNativeRecipe approved = fixture.Preparer.PrepareExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode, CreateEnvironmentInfo(),
					fixture.InstallContext, fixture.State, false);
				CollectionNativeStateIndex changedState = CreateState(fixture.Target, 1);
				Assert.That(changedState.Fingerprint, Is.Not.EqualTo(fixture.Plan.CurrentStateFingerprint));

				Assert.Throws<InvalidOperationException>(() => fixture.Preparer.PrepareExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode, CreateEnvironmentInfo(),
					fixture.InstallContext, changedState, false));
				PreparedCollectionNativeRecipe resumed = fixture.Preparer.PrepareRevisionUpdateExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode, CreateEnvironmentInfo(),
					fixture.InstallContext, changedState, false, null, null, CancellationToken.None);
				Assert.That(resumed.PreparedNativeIdentity, Is.EqualTo(approved.PreparedNativeIdentity));
				Assert.That(fixture.Plan.CurrentStateFingerprint, Is.EqualTo(fixture.State.Fingerprint));

				PreparedCollectionNativeRecipe changedSetting = fixture.Preparer.PrepareRevisionUpdateExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode, CreateEnvironmentInfo(),
					fixture.InstallContext, changedState, true, null, null, CancellationToken.None);
				Assert.That(changedSetting.PreparedNativeIdentity, Is.Not.EqualTo(approved.PreparedNativeIdentity),
					"Re-preparation must still expose a changed recipe to the revision coordinator's identity check.");
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

		[Test]
		public void PrepareBasicSimpleExact_DeterministicSpecialFileBehaviorUsesExactNativeFilePlan()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				bool specialInstallCalled = false;
				Fixture fixture = CreateFixture(root, "special-deterministic", @"Legacy\old.pak", @"Current\payload.bin");
				fixture.GameMode = CreateGameMode(true, () => specialInstallCalled = true, false, null,
					new[] { @"Current\payload.bin" });

				PreparedCollectionNativeRecipe prepared = fixture.Preparer.PrepareBasicSimpleExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode,
					fixture.InstallContext, fixture.State, false);

				Assert.That(prepared.EffectPreview.Files.Count, Is.EqualTo(1));
				Assert.That(prepared.EffectPreview.Files.Single().Target.RelativePath, Is.EqualTo(@"Target\Current\payload.bin"));
				Assert.That(specialInstallCalled, Is.False);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareBasicSimpleExact_DeterministicSpecialFileGameValueBecomesReviewedNativeEffect()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "special-game-value", "info.json", @"Mods\payload.pak");
				byte[] expected = new byte[] { 4, 3, 2, 1 };
				fixture.GameMode = CreateGameMode(true, null, false, null, new[] { @"Mods\payload.pak" },
					false, null, false, null, new[] { new BasicInstallGameSpecificValue("bg3|profile|module", expected) });

				PreparedCollectionNativeRecipe prepared = fixture.Preparer.PrepareBasicSimpleExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode,
					fixture.InstallContext, fixture.State, false);

				EditGameSpecificValueOperation operation = prepared.RecipeInput.NativeOperations.OfType<EditGameSpecificValueOperation>().Single();
				Assert.That(operation.Key, Is.EqualTo("bg3|profile|module"));
				Assert.That(operation.HasResolvedOverwriteDecision, Is.True);
				CollectionAssert.AreEqual(expected, operation.Value);
				CollectionPlannedGameValueEffect effect = prepared.EffectPreview.GameValues.Single();
				Assert.That(effect.Key, Is.EqualTo(operation.Key));
				CollectionAssert.AreEqual(expected, effect.Value);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareExact_DeterministicModFileMergeBecomesRetainedGeneratedNativeEffect()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "merge-deterministic", "chargenmorphcfg.xml", @"textures\body.dds");
				byte[] merged = new byte[] { 10, 20, 30, 40, 50 };
				fixture.GameMode = CreateGameMode(false, null, false, null, null, true,
					new DeterministicModFileMergePlan("chargenmorphcfg.xml", @"NMM_chargenmorphcfg\chargenmorphcfg.xml", merged));

				PreparedCollectionNativeRecipe prepared = fixture.Preparer.PrepareExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode, CreateEnvironmentInfo(),
					fixture.InstallContext, fixture.State, false, CreateEmptyPluginManager(), new List<IMod>(), CancellationToken.None);

				GenerateDataFileOperation generated = prepared.RecipeInput.NativeOperations.OfType<GenerateDataFileOperation>().Single();
				Assert.That(generated.PreparationSourcePath, Is.EqualTo("chargenmorphcfg.xml"));
				Assert.That(generated.DestinationPath, Is.EqualTo(@"NMM_chargenmorphcfg\chargenmorphcfg.xml"));
				CollectionAssert.AreEqual(merged, generated.Data);
				Assert.That(prepared.EffectPreview.Files.Any(x => x.Target.RelativePath.EndsWith(@"NMM_chargenmorphcfg\chargenmorphcfg.xml", StringComparison.OrdinalIgnoreCase)), Is.True);
				Assert.That(prepared.RetainedArtifactIds.Count, Is.GreaterThanOrEqualTo(3));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareExact_DazipStyleMergeRoutesArchiveToSecondaryAndGeneratedOutputToPrimaryRoot()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "merge-secondary-root", "chargenmorphcfg.xml", @"textures\body.dds");
				const string mergedPath = @"NMM_chargenmorphcfg\chargenmorphcfg.xml";
				fixture.GameMode = CreateGameMode(false, null, false, null, null, true,
					new DeterministicModFileMergePlan("chargenmorphcfg.xml", mergedPath, new byte[] { 2, 4, 6, 8 }),
					true, (mod, path) => !path.EndsWith(mergedPath, StringComparison.OrdinalIgnoreCase));

				PreparedCollectionNativeRecipe prepared = fixture.Preparer.PrepareExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode, CreateEnvironmentInfo(),
					fixture.InstallContext, fixture.State, false, CreateEmptyPluginManager(), new List<IMod>(), CancellationToken.None);

				CollectionPlannedFileEffect archiveEffect = prepared.EffectPreview.Files.Single(x => x.Target.RelativePath.EndsWith(@"textures\body.dds", StringComparison.OrdinalIgnoreCase));
				CollectionPlannedFileEffect mergeEffect = prepared.EffectPreview.Files.Single(x => x.Target.RelativePath.EndsWith(mergedPath, StringComparison.OrdinalIgnoreCase));
				Assert.That(archiveEffect.Target.Root, Is.EqualTo(ModDeploymentRoot.Secondary));
				Assert.That(mergeEffect.Target.Root, Is.EqualTo(ModDeploymentRoot.Data));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareExact_DeferredDeterministicMergeContributorKeepsOnlyIndependentEffects()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, "merge-deferred", "chargenmorphcfg.xml", @"textures\body.dds");
				fixture.GameMode = CreateGameMode(false, null, false, null, null, true,
					new DeterministicModFileMergePlan("chargenmorphcfg.xml", @"NMM_chargenmorphcfg\chargenmorphcfg.xml", new byte[] { 1, 3, 5, 7 }));

				PreparedCollectionNativeRecipe prepared = fixture.Preparer.PrepareExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode, CreateEnvironmentInfo(),
					fixture.InstallContext, fixture.State, false, CreateEmptyPluginManager(), new List<IMod>(), false, CancellationToken.None);

				Assert.That(prepared.RecipeInput.NativeOperations.OfType<GenerateDataFileOperation>(), Is.Empty);
				InstallModFileOperation file = prepared.RecipeInput.NativeOperations.OfType<InstallModFileOperation>().Single();
				Assert.That(file.SourcePath, Is.EqualTo(@"textures\body.dds"));
				Assert.That(prepared.EffectPreview.Files.Any(x => x.Target.RelativePath.EndsWith(@"NMM_chargenmorphcfg\chargenmorphcfg.xml", StringComparison.OrdinalIgnoreCase)), Is.False);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareBasicSimpleExact_DinputAtArchiveRootUsesNativeGameRootPlan()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateGameRootFixture(root, "dinput-root", "dinput", "dinput8.dll", "preset.ini");

				PreparedCollectionNativeRecipe prepared = fixture.Preparer.PrepareBasicSimpleExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode,
					fixture.InstallContext, fixture.State, false);

				Assert.That(prepared.InstallContext.InstallRoot, Is.EqualTo(ModInstallRoot.GameRoot));
				CollectionAssert.AreEquivalent(new[] { "dinput8.dll", "preset.ini" },
					prepared.EffectPreview.Files.Select(x => x.Target.RelativePath).ToArray());
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareBasicSimpleExact_DinputInSingleWrapperUsesNativeWrapperNormalization()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateGameRootFixture(root, "dinput-wrapper", "dinput",
					@"Injector\dinput8.dll", @"Injector\preset.ini");

				PreparedCollectionNativeRecipe prepared = fixture.Preparer.PrepareBasicSimpleExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode,
					fixture.InstallContext, fixture.State, false);

				CollectionAssert.AreEquivalent(new[] { "dinput8.dll", "preset.ini" },
					prepared.EffectPreview.Files.Select(x => x.Target.RelativePath).ToArray());
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareBasicSimpleExact_EnbUsesNativeGameRootPlan()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateGameRootFixture(root, "enb-root", "enb",
					@"ENBWrapper\enbseries.ini", @"ENBWrapper\d3d11.dll");

				PreparedCollectionNativeRecipe prepared = fixture.Preparer.PrepareBasicSimpleExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode,
					fixture.InstallContext, fixture.State, false);

				Assert.That(fixture.Member.InstallRootBehavior, Is.EqualTo(CollectionMemberInstallRootBehavior.VortexEnbGameRoot));
				Assert.That(prepared.InstallContext.InstallRoot, Is.EqualTo(ModInstallRoot.GameRoot));
				CollectionAssert.AreEquivalent(new[] { "enbseries.ini", "d3d11.dll" },
					prepared.EffectPreview.Files.Select(x => x.Target.RelativePath).ToArray());
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[TestCase(@"Wrapper\Bin\dinput8.dll", @"Wrapper\Bin\preset.ini", TestName = "Prepare_DinputNestedBase_FailsClosed")]
		[TestCase(@"Wrapper\dinput8.dll", "readme.txt", TestName = "Prepare_DinputFilesOutsideBase_FailsClosed")]
		[TestCase("dinput8.dll", @"Other\dinput8.dll", TestName = "Prepare_DinputMultipleMarkers_FailsClosed")]
		public void PrepareBasicSimpleExact_UnrepresentableDinputLayoutFailsClosed(string first, string second)
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateGameRootFixture(root, "dinput-unsupported-" + Guid.NewGuid().ToString("N"), "dinput", first, second);

				Assert.Throws<NotSupportedException>(() => fixture.Preparer.PrepareBasicSimpleExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode,
					fixture.InstallContext, fixture.State, false));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareBasicSimpleExact_DinputRequiresGameRootContextAtPreparationBoundary()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateGameRootFixture(root, "dinput-wrong-root", "dinput", "f4se_loader.exe");
				var wrongContext = new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data);

				Assert.Throws<ArgumentException>(() => fixture.Preparer.PrepareBasicSimpleExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode,
					wrongContext, fixture.State, false));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareExact_EmptyVortexFomodPresetFreezesDefaultOnlyFiles()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateEmptyPresetFomodFixture(root, "fomod-default-only", false);

				PreparedCollectionNativeRecipe prepared = fixture.Preparer.PrepareExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode, CreateEnvironmentInfo(),
					fixture.InstallContext, fixture.State, false, CreateEmptyPluginManager());

				Assert.That(fixture.Member.HasVortexFomodSelection, Is.True);
				Assert.That(fixture.Member.VortexFomodSelection.Steps.Count, Is.EqualTo(0));
				InstallModFileOperation file = prepared.RecipeInput.NativeOperations.OfType<InstallModFileOperation>().Single();
				Assert.That(file.SourcePath, Is.EqualTo(@"required\core.bin"));
				Assert.That(file.DestinationPath, Is.EqualTo("core.bin"));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareExact_EmptyVortexFomodPresetAgainstSelectableDefinitionFailsClosed()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateEmptyPresetFomodFixture(root, "fomod-empty-stale", true);

				Assert.Throws<InvalidDataException>(() => fixture.Preparer.PrepareExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode, CreateEnvironmentInfo(),
					fixture.InstallContext, fixture.State, false, CreateEmptyPluginManager()));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareExact_VortexFomodChoiceUsesNativeSelectionAndFreezesExactFilePlan()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFomodFixture(root, "fomod-map", false);

				PreparedCollectionNativeRecipe prepared = fixture.Preparer.PrepareExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode, CreateEnvironmentInfo(),
					fixture.InstallContext, fixture.State, false, CreateEmptyPluginManager());

				Assert.That(fixture.Member.HasVortexFomodSelection, Is.True);
				Assert.That(prepared.AdapterId, Is.EqualTo(ModInstallationSimpleFileRecipeAdapter.AdapterId),
					"Reviewed persistence stays on the existing durable simple-file recipe after native FOMOD validation.");
				InstallModFileOperation file = prepared.RecipeInput.NativeOperations.OfType<InstallModFileOperation>().Single();
				Assert.That(file.SourcePath, Is.EqualTo(@"textures\map-4k.dds"));
				Assert.That(file.DestinationPath, Is.EqualTo(@"textures\map.dds"));
				Assert.That(prepared.EffectPreview.Files.Count, Is.EqualTo(1));
				Assert.That(prepared.PreparedNativeIdentity.Fingerprint, Does.StartWith("sha256:"));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareExact_VortexFomodDinputFreezesSelectedOutputsIntoNativeGameRootRecipe()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFomodDinputFixture(root, "fomod-dinput", "dinput8.dll");

				PreparedCollectionNativeRecipe prepared = fixture.Preparer.PrepareExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode, CreateEnvironmentInfo(),
					fixture.InstallContext, fixture.State, false, CreateEmptyPluginManager());

				Assert.That(fixture.Member.InstallRootBehavior, Is.EqualTo(CollectionMemberInstallRootBehavior.VortexDInputGameRoot));
				Assert.That(prepared.InstallContext.InstallRoot, Is.EqualTo(ModInstallRoot.GameRoot));
				CollectionAssert.AreEquivalent(new[] { "dinput8.dll", "preset.ini" },
					prepared.RecipeInput.NativeOperations.OfType<InstallModFileOperation>().Select(x => x.DestinationPath).ToArray());
				Assert.That(prepared.EffectPreview.Files.All(x => x.Target.Root == ModDeploymentRoot.GameRoot), Is.True);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareExact_VortexFomodDinputWithoutRootMarkerRemainsFailClosed()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFomodDinputFixture(root, "fomod-dinput-nested", @"bin\dinput8.dll");

				Assert.Throws<NotSupportedException>(() => fixture.Preparer.PrepareExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode, CreateEnvironmentInfo(),
					fixture.InstallContext, fixture.State, false, CreateEmptyPluginManager()));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareExact_VortexFomodPluginFileWithoutCollectionPluginStateRemainsBlocked()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFomodPluginFixture(root, "fomod-plugin-blocked", false);

				Assert.Throws<NotSupportedException>(() => fixture.Preparer.PrepareExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode, CreateEnvironmentInfo(),
					fixture.InstallContext, fixture.State, false, CreateEmptyPluginManager()));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareExact_VortexFomodPluginFileWithExplicitCollectionStateFreezesReviewedSimpleFilePlan()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFomodPluginFixture(root, "fomod-plugin-supported", true);

				PreparedCollectionNativeRecipe prepared = fixture.Preparer.PrepareExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode, CreateEnvironmentInfo(),
					fixture.InstallContext, fixture.State, false, CreateEmptyPluginManager());

				Assert.That(prepared.AdapterId, Is.EqualTo(ModInstallationSimpleFileRecipeAdapter.AdapterId));
				InstallModFileOperation file = prepared.RecipeInput.NativeOperations.OfType<InstallModFileOperation>().Single();
				Assert.That(file.SourcePath, Is.EqualTo(@"plugin\choice.esp"));
				Assert.That(file.DestinationPath, Is.EqualTo(@"choice.esp"));
				Assert.That(prepared.EffectPreview.PluginEffects.Count, Is.EqualTo(1));
				Assert.That(prepared.EffectPreview.PluginEffects.Single().Active, Is.True);
				Assert.That(fixture.Plan.CapabilityReport.Manifest.HasPluginStateSection, Is.True);
				Assert.That(fixture.Plan.CapabilityReport.Manifest.PluginStates.Single().PluginName, Is.EqualTo("choice.esp"));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PrepareExact_VortexFomodChoiceNameMismatchAgainstActualArchiveFailsClosed()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFomodFixture(root, "fomod-stale", true);

				Assert.Throws<InvalidDataException>(() => fixture.Preparer.PrepareExact(
					fixture.Plan, fixture.Member, fixture.VerifiedArchive, fixture.Mod, fixture.GameMode, CreateEnvironmentInfo(),
					fixture.InstallContext, fixture.State, false));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static Fixture CreateFixture(string root, string suffix, params string[] archiveFiles)
		{
			return CreateFixtureCore(root, suffix, null, new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), false, archiveFiles);
		}

		private static Fixture CreateGameRootFixture(string root, string suffix, string modType, params string[] archiveFiles)
		{
			return CreateFixtureCore(root, suffix, modType, new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.GameRoot), true, archiveFiles);
		}

		private static Fixture CreateEmptyPresetFomodFixture(string root, string suffix, bool includeSelectableStep)
		{
			var scriptType = new XmlScriptType();
			var script = new XmlScript(scriptType, new Version(5, 0));
			script.RequiredInstallFiles.Add(new InstallableFile(@"required\core.bin", "core.bin", false, 0, false, false));
			if (includeSelectableStep)
			{
				var step = new InstallStep("Optional Step", null, SortOrder.Explicit);
				var group = new OptionGroup("Optional Group", OptionGroupType.SelectAny, SortOrder.Explicit);
				group.Options.Add(CreateFomodOption("Optional File", @"optional\extra.bin", "extra.bin"));
				step.OptionGroups.Add(group);
				script.InstallSteps.Add(step);
			}

			string choicesJson = "{\"type\":\"fomod\",\"options\":[]}";
			return CreateFixtureCore(root, suffix, null, new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), false,
				choicesJson, script, includeSelectableStep
					? new[] { @"required\core.bin", @"optional\extra.bin" }
					: new[] { @"required\core.bin" });
		}

		private static Fixture CreateFomodFixture(string root, string suffix, bool staleActualOptionName)
		{
			var scriptType = new XmlScriptType();
			var script = new XmlScript(scriptType, new Version(5, 0));
			var step = new InstallStep("Install Map with Locations", null, SortOrder.Explicit);
			var group = new OptionGroup("Map with All Locations", OptionGroupType.SelectExactlyOne, SortOrder.Explicit);
			group.Options.Add(CreateFomodOption(staleActualOptionName ? "Renamed 4k Option" : "4k With All Locations",
				@"textures\map-4k.dds", @"textures\map.dds"));
			group.Options.Add(CreateFomodOption("2k With All Locations", @"textures\map-2k.dds", @"textures\map.dds"));
			step.OptionGroups.Add(group);
			script.InstallSteps.Add(step);

			string choicesJson = "{\"type\":\"fomod\",\"options\":[{\"name\":\"Install Map with Locations\",\"groups\":[" +
				"{\"name\":\"Map with All Locations\",\"choices\":[{\"name\":\"4k With All Locations\",\"idx\":0}]}]}]}";
			return CreateFixtureCore(root, suffix, null, new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), false,
				choicesJson, script, null, "exact", (string)null, (IDictionary<string, byte[]>)null, @"textures\map-4k.dds", @"textures\map-2k.dds");
		}

		private static Fixture CreateFomodDinputFixture(string root, string suffix, string dinputDestination)
		{
			var scriptType = new XmlScriptType();
			var script = new XmlScript(scriptType, new Version(5, 0));
			var step = new InstallStep("Injector Step", null, SortOrder.Explicit);
			var group = new OptionGroup("Injector Group", OptionGroupType.SelectExactlyOne, SortOrder.Explicit);
			var option = new Option("Install Injector", String.Empty, null, new StaticOptionTypeResolver(OptionType.Optional));
			option.Files.Add(new InstallableFile(@"payload\dinput8.dll", dinputDestination, false, 0, false, false));
			option.Files.Add(new InstallableFile(@"payload\preset.ini", "preset.ini", false, 0, false, false));
			group.Options.Add(option);
			step.OptionGroups.Add(group);
			script.InstallSteps.Add(step);

			string choicesJson = "{\"type\":\"fomod\",\"options\":[{\"name\":\"Injector Step\",\"groups\":[" +
				"{\"name\":\"Injector Group\",\"choices\":[{\"name\":\"Install Injector\",\"idx\":0}]}]}]}";
			return CreateFixtureCore(root, suffix, "dinput", new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.GameRoot), true,
				choicesJson, script, null, "exact", (string)null, (IDictionary<string, byte[]>)null, @"payload\dinput8.dll", @"payload\preset.ini");
		}

		private static Fixture CreateFomodPluginFixture(string root, string suffix, bool declarePluginState)
		{
			var scriptType = new XmlScriptType();
			var script = new XmlScript(scriptType, new Version(5, 0));
			var step = new InstallStep("Plugin Step", null, SortOrder.Explicit);
			var group = new OptionGroup("Plugin Group", OptionGroupType.SelectExactlyOne, SortOrder.Explicit);
			group.Options.Add(CreateFomodOption("Plugin", @"plugin\choice.esp", @"choice.esp"));
			step.OptionGroups.Add(group);
			script.InstallSteps.Add(step);

			string choicesJson = "{\"type\":\"fomod\",\"options\":[{\"name\":\"Plugin Step\",\"groups\":[" +
				"{\"name\":\"Plugin Group\",\"choices\":[{\"name\":\"Plugin\",\"idx\":0}]}]}]}";
			string pluginsJson = declarePluginState ? "[{\"name\":\"choice.esp\",\"enabled\":true}]" : null;
			Fixture fixture = CreateFixtureCore(root, suffix, null, new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), false,
				choicesJson, script, pluginsJson, "exact", (string)null, (IDictionary<string, byte[]>)null, @"plugin\choice.esp");
			fixture.GameMode = CreateGameMode(false, null, false, new[] { ".esp", ".esm", ".esl" });
			return fixture;
		}

		private static Option CreateFomodOption(string name, string source, string destination)
		{
			var option = new Option(name, String.Empty, null, new StaticOptionTypeResolver(OptionType.Optional));
			option.Files.Add(new InstallableFile(source, destination, false, 0, false, false));
			return option;
		}

		private static Fixture CreateFixtureCore(string root, string suffix, string modType, ModInstallContext installContext,
			bool supportsGameRootInstall, params string[] archiveFiles)
		{
			return CreateFixtureCore(root, suffix, modType, installContext, supportsGameRootInstall, null, null, null, "exact", archiveFiles);
		}

		private static Fixture CreateFixtureCore(string root, string suffix, string modType, ModInstallContext installContext,
			bool supportsGameRootInstall, string choicesJson, IScript installScript, params string[] archiveFiles)
		{
			return CreateFixtureCore(root, suffix, modType, installContext, supportsGameRootInstall, choicesJson, installScript, null, "exact", archiveFiles);
		}

		private static Fixture CreateFixtureCore(string root, string suffix, string modType, ModInstallContext installContext,
			bool supportsGameRootInstall, string choicesJson, IScript installScript, string pluginsJson, params string[] archiveFiles)
		{
			return CreateFixtureCore(root, suffix, modType, installContext, supportsGameRootInstall, choicesJson, installScript, pluginsJson, "exact", archiveFiles);
		}

		private static Fixture CreateFixtureCore(string root, string suffix, string modType, ModInstallContext installContext,
			bool supportsGameRootInstall, string choicesJson, IScript installScript, string pluginsJson, string updatePolicy, params string[] archiveFiles)
		{
			return CreateFixtureCore(root, suffix, modType, installContext, supportsGameRootInstall, choicesJson, installScript,
				pluginsJson, updatePolicy, null, null, archiveFiles);
		}

		private static Fixture CreateFixtureCore(string root, string suffix, string modType, ModInstallContext installContext,
			bool supportsGameRootInstall, string choicesJson, IScript installScript, string pluginsJson, string updatePolicy,
			string hashesJson, IDictionary<string, byte[]> archiveContents, params string[] archiveFiles)
		{
			return CreateFixtureCoreWithOverrides(root, suffix, modType, installContext, supportsGameRootInstall, choicesJson, installScript,
				pluginsJson, updatePolicy, hashesJson, archiveContents, null, archiveFiles);
		}

		private static Fixture CreateFixtureCoreWithOverrides(string root, string suffix, string modType, ModInstallContext installContext,
			bool supportsGameRootInstall, string choicesJson, IScript installScript, string pluginsJson, string updatePolicy,
			string hashesJson, IDictionary<string, byte[]> archiveContents, string fileOverridesJson, params string[] archiveFiles)
		{
			var store = new CollectionsStore(root);
			store.CreateNew();
			CollectionIdentity collection = CollectionIdentity.FromNexus("c6159-" + suffix);
			var revision = new CollectionRevision(
				CollectionRevisionIdentity.FromNexus(collection, "revision-" + suffix, 9), "Revision", null, 1);
			new CollectionsCatalogStore(store).SaveDefinitionAndRevision(
				new CollectionDefinition(collection, suffix, null, null), revision);

			string details = String.IsNullOrEmpty(modType) ? String.Empty : ",\"details\":{\"type\":\"" + modType + "\"}";
			string choices = String.IsNullOrEmpty(choicesJson) ? String.Empty : ",\"choices\":" + choicesJson;
			string hashes = String.IsNullOrEmpty(hashesJson) ? String.Empty : ",\"hashes\":" + hashesJson;
			string fileOverrides = String.IsNullOrEmpty(fileOverridesJson) ? String.Empty : ",\"fileOverrides\":" + fileOverridesJson;
			string plugins = String.IsNullOrEmpty(pluginsJson) ? String.Empty : ",\"plugins\":" + pluginsJson;
			string json = "{" +
				"\"info\":{\"author\":\"Curator\",\"authorUrl\":\"https://example.invalid/author\",\"name\":\"Example\",\"description\":\"Example\",\"domainName\":\"skyrim\"}," +
				"\"mods\":[{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20,\"updatePolicy\":\"" + updatePolicy + "\"}" + details + hashes + choices + fileOverrides + "}]," +
				"\"modRules\":[]" + plugins + "}";
			byte[] manifestBytes = Encoding.UTF8.GetBytes(json);
			NexusCollectionManifestNormalizationResult normalization = new NexusCollectionManifestNormalizer().Normalize(manifestBytes, revision);
			CollectionCapabilityReport reviewedCapability = normalization.CapabilityReport;
			if (StringComparer.Ordinal.Equals(updatePolicy, "prefer"))
			{
				Assert.That(normalization.CapabilityReport.Status, Is.EqualTo(CollectionCompatibilityStatus.ActionRequired));
				reviewedCapability = normalization.CapabilityReport.FilterDeclaredIssues(issue =>
					!StringComparer.Ordinal.Equals(issue.Code, CollectionNexusSourcePolicyResolver.PreferIssueCode));
			}
			Assert.That(reviewedCapability.Status, Is.EqualTo(CollectionCompatibilityStatus.Supported));
			var sourceStore = new CollectionsRevisionSourceStore(store);
			CollectionRevisionSourceRecord sourceRecord = sourceStore.RetainManifest(normalization.Manifest,
				CollectionRevisionSourceInputKind.RawManifest, normalization.Manifest.Source.ContentHash,
				manifestBytes.LongLength, "collection.json", manifestBytes);

			NormalizedCollectionMember normalizedMember = normalization.Manifest.Members.Single();
			var member = new ResolvedCollectionMemberPlan(normalizedMember, CollectionResolvedArtifactChoice.Exact(normalizedMember.Artifact));
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-sha256:" + new string('a', 64));
			CollectionNativeStateIndex state = CreateState(target, 0);
			var plan = new ResolvedCollectionPlan(CollectionPlanIdentity.From(Guid.NewGuid(), 1), target,
				CollectionExecutionPolicy.InstallIntoCurrentSetup(), state.Fingerprint, reviewedCapability, new[] { member });

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
					case "get_HasInstallScript": return installScript != null;
					case "get_InstallScript": return installScript;
					case "GetFileList": return new List<string>(fileList);
					case "GetFile":
						byte[] bytes;
						return archiveContents != null && archiveContents.TryGetValue((string)args[0], out bytes) ? bytes : null;
					case "GetFileStream":
						string requestedPath = (string)args[0];
						if (!fileList.Any(path => StringComparer.OrdinalIgnoreCase.Equals(path, requestedPath))) return null;
						byte[] streamBytes;
						if (archiveContents == null || !archiveContents.TryGetValue(requestedPath, out streamBytes))
							streamBytes = Encoding.UTF8.GetBytes("fixture:" + requestedPath);
						return CreateFixtureFileStream(streamBytes);
					default: return null;
				}
			});

			return new Fixture(store, target, state, plan, member, sourceRecord, verifiedArchive, mod, modArchivePath,
				CreateGameMode(false, null, supportsGameRootInstall), installContext);
		}


		private static Fixture CreateFileListFixture(string root, string suffix, IDictionary<string, byte[]> archiveContents, string hashesJson)
		{
			if (archiveContents == null) throw new ArgumentNullException(nameof(archiveContents));
			return CreateFixtureCore(root, suffix, null, new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), false,
				null, null, null, "exact", hashesJson, archiveContents, archiveContents.Keys.ToArray());
		}

		private static Fixture CreateFileListOverrideFixture(string root, string suffix, IDictionary<string, byte[]> archiveContents,
			string hashesJson, string fileOverridesJson)
		{
			if (archiveContents == null) throw new ArgumentNullException(nameof(archiveContents));
			return CreateFixtureCoreWithOverrides(root, suffix, null, new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), false,
				null, null, null, "exact", hashesJson, archiveContents, fileOverridesJson, archiveContents.Keys.ToArray());
		}

		private static Fixture CreateFileOverrideFixture(string root, string suffix, string fileOverridesJson, params string[] archiveFiles)
		{
			return CreateFixtureCoreWithOverrides(root, suffix, null, new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), false,
				null, null, null, "exact", null, null, fileOverridesJson, archiveFiles);
		}

		private static string ComputeMd5(byte[] bytes)
		{
			using (MD5 md5 = MD5.Create())
				return BitConverter.ToString(md5.ComputeHash(bytes)).Replace("-", String.Empty).ToLowerInvariant();
		}

		private interface ITestGameMode : IGameMode, IDeterministicSpecialFileInstallPlanProvider,
			IDeterministicSpecialFileGameValuePlanProvider, IDeterministicModFileMergePlanProvider
		{
		}

		private static IGameMode CreateGameMode(bool specialFile, Action onSpecialInstall, bool supportsGameRootInstall = false,
			IEnumerable<string> pluginExtensions = null, IEnumerable<string> deterministicSpecialFiles = null,
			bool requiresMerge = false, DeterministicModFileMergePlan deterministicMergePlan = null,
			bool hasSecondaryInstallPath = false, Func<IMod, string, bool> checkSecondaryInstall = null,
			IEnumerable<BasicInstallGameSpecificValue> deterministicSpecialGameValues = null)
		{
			return InterfaceStub<ITestGameMode>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_Name": return "Test Game";
					case "get_GameModeEnvironmentInfo": return CreateGameModeEnvironmentInfo(hasSecondaryInstallPath);
					case "get_InstallationPath": return @"C:\Game";
					case "get_SecondaryInstallationPath": return hasSecondaryInstallPath ? @"C:\GameSecondary" : null;
					case "get_PluginDirectory": return @"C:\Game\Data";
					case "get_UsesPlugins": return pluginExtensions != null;
					case "get_PluginExtensions": return pluginExtensions;
					case "get_SupportsGameRootModInstall": return supportsGameRootInstall;
					case "get_RequiresSpecialFileInstallation": return specialFile;
					case "IsSpecialFile": return specialFile;
					case "SpecialFileInstall":
						onSpecialInstall?.Invoke();
						return new[] { "transformed.bin" };
					case "GetDeterministicSpecialFileInstallPlan": return deterministicSpecialFiles;
					case "GetDeterministicSpecialFileGameValues": return deterministicSpecialGameValues ?? new BasicInstallGameSpecificValue[0];
					case "IsDeterministicSpecialFileGameValueKey": return args[0] is string;
					case "get_RequiresModFileMerge": return requiresMerge;
					case "GetDeterministicModFileMergePlan": return deterministicMergePlan;
					case "get_HasSecondaryInstallPath": return hasSecondaryInstallPath;
					case "CheckSecondaryInstall": return checkSecondaryInstall != null && checkSecondaryInstall((IMod)args[0], (string)args[1]);
					case "GetModFormatAdjustedPath": return pluginExtensions != null ? (string)args[1] : AdjustPath(args);
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


		private static IGameModeEnvironmentInfo CreateGameModeEnvironmentInfo(bool hasSecondaryInstallPath = false)
		{
			return InterfaceStub<IGameModeEnvironmentInfo>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_InstallationPath": return @"C:\Game";
					case "get_SecondaryInstallationPath": return hasSecondaryInstallPath ? @"C:\GameSecondary" : null;
					case "get_ExecutablePath": return @"C:\Game\Game.exe";
					case "get_InstallInfoDirectory": return @"C:\Game\NMM\InstallInfo";
					case "get_OverwriteDirectory": return @"C:\Game\NMM\Overwrite";
					case "get_ModDirectory": return @"C:\Game\NMM\Mods";
					case "get_ModCacheDirectory": return @"C:\Game\NMM\Cache";
					case "get_ModDownloadCacheDirectory": return @"C:\Game\NMM\Downloads";
					case "get_ModReadMeDirectory": return @"C:\Game\NMM\ReadMe";
					case "get_CategoryDirectory": return @"C:\Game\NMM\Categories";
					default: return null;
				}
			});
		}

		private static IPluginManager CreateEmptyPluginManager()
		{
			var managed = new ReadOnlyObservableList<Plugin>(new ThreadSafeObservableList<Plugin>());
			var active = new ReadOnlyObservableList<Plugin>(new ThreadSafeObservableList<Plugin>());
			return InterfaceStub<IPluginManager>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_ManagedPlugins": return managed;
					case "get_ActivePlugins": return active;
					case "CanChangeActiveState":
					case "CanChangePluginOrder": return true;
					default: return null;
				}
			});
		}

		private static IEnvironmentInfo CreateEnvironmentInfo()
		{
			return InterfaceStub<IEnvironmentInfo>.Create((method, args) => null);
		}

		private static CollectionNativeStateIndex CreateState(CollectionTargetIdentity target, long deploymentCommitSequence)
		{
			return new CollectionNativeStateIndex(target, new CollectionNativeRootState[0],
				new CollectionNativeModState[0], new CollectionNativeFileState[0], new CollectionNativeIniState[0],
				new CollectionNativeGameValueState[0], new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable,
				new CollectionTargetAssociation[0], new CollectionMemberBinding[0], new UserOverride[0],
				CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], deploymentCommitSequence);
		}

		private static FileStream CreateFixtureFileStream(byte[] bytes)
		{
			string path = Path.Combine(Path.GetTempPath(), "nmm-collection-fixture-stream-" + Guid.NewGuid().ToString("N") + ".bin");
			File.WriteAllBytes(path, bytes ?? new byte[0]);
			return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, FileOptions.DeleteOnClose);
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
