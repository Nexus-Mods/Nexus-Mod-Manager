using System;
using System.IO;
using System.Reflection;
using Nexus.Client.CollectionManagement;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>C6.8 authoritative durability and effect-verification coverage.</summary>
	[TestFixture]
	public class CollectionNativeChildVerificationCoordinatorTests
	{
		[Test]
		public void DetermineVerifiedDurability_CommittedRealityWinsOverReportedFailure()
		{
			ModOperationResult reported = CreateResult(ModOperationReportedStatus.Failed, ModOperationDurability.Unknown);
			Assert.AreEqual(ModOperationDurability.VerifiedCommitted,
				InvokeDurability(reported, true, false));
		}

		[Test]
		public void DetermineVerifiedDurability_VerifiedRollbackEvidenceDistinguishesNotStartedFromRollback()
		{
			Assert.AreEqual(ModOperationDurability.NotStarted,
				InvokeDurability(CreateResult(ModOperationReportedStatus.Cancelled, ModOperationDurability.NotStarted), false, true));
			Assert.AreEqual(ModOperationDurability.VerifiedRolledBack,
				InvokeDurability(CreateResult(ModOperationReportedStatus.Failed, ModOperationDurability.Unknown), false, true));
		}

		[Test]
		public void DetermineVerifiedDurability_UnverifiedRealityRemainsUnknown()
		{
			Assert.AreEqual(ModOperationDurability.Unknown,
				InvokeDurability(CreateResult(ModOperationReportedStatus.Succeeded, ModOperationDurability.VerifiedCommitted), false, false));
		}

		[Test]
		public void DetermineVerifiedDurability_NativeCommittedBoundaryCannotBeReclassifiedAsRollback()
		{
			Assert.AreEqual(ModOperationDurability.Unknown,
				InvokeDurability(CreateResult(ModOperationReportedStatus.Failed, ModOperationDurability.VerifiedCommitted), false, true));
		}

		[Test]
		public void FileContentIdentity_RejectsChangedBytesWithSameLength()
		{
			string temp = Path.GetTempFileName();
			try
			{
				byte[] expectedBytes = { 1, 2, 3, 4 };
				File.WriteAllBytes(temp, expectedBytes);
				Type contentType = typeof(CollectionNativeChildVerificationCoordinator).GetNestedType(
					"FileContentIdentity", BindingFlags.NonPublic);
				Assert.IsNotNull(contentType);
				MethodInfo fromBytes = contentType.GetMethod("FromBytes", BindingFlags.Public | BindingFlags.Static);
				MethodInfo matches = contentType.GetMethod("Matches", BindingFlags.Public | BindingFlags.Instance);
				Assert.IsNotNull(fromBytes);
				Assert.IsNotNull(matches);
				object expected = fromBytes.Invoke(null, new object[] { expectedBytes });
				Assert.IsTrue((bool)matches.Invoke(expected, new object[] { temp }));

				File.WriteAllBytes(temp, new byte[] { 1, 9, 3, 4 });
				Assert.IsFalse((bool)matches.Invoke(expected, new object[] { temp }));
			}
			finally
			{
				File.Delete(temp);
			}
		}

		[Test]
		public void VerifyMemberEffects_RequiresExactNativeOwnerAndConfigurationValues()
		{
			string temp = Path.GetTempFileName();
			try
			{
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c68-test");
				var nativeMod = new CollectionNativeModState(new NativeModInstanceIdentity(target, "owner-a"),
					"archive.7z", "archive.7z", "10", "20", "1.0", "1.0", false,
					ModInstallRoot.Data, ModInstallMethod.Direct);
				ModDeploymentTarget deploymentTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\a.dds");
				var owner = new CollectionNativeOwnerState("owner-a", null, CollectionNativeOwnerKind.NativeMod, null, null, null);
				var file = new CollectionNativeFileState(deploymentTarget, temp, false, true, false, "owner-a",
					new[] { owner }, new CollectionNativeOwnerState[0], new CollectionNativeOwnerState[0]);
				var iniKey = new CollectionNativeIniKey("settings.ini", "Display", "Mode");
				var ini = new CollectionNativeIniState(iniKey, new[] { new CollectionNativeTextOwnerValue("owner-a", "High") });
				var game = new CollectionNativeGameValueState("setting", new[] { new CollectionNativeBinaryOwnerValue("owner-a", new byte[] { 1, 2 }) });
				var state = new CollectionNativeStateIndex(target, new CollectionNativeRootState[0], new[] { nativeMod }, new[] { file },
					new[] { ini }, new[] { game }, new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable,
					new CollectionTargetAssociation[0], new CollectionMemberBinding[0], new UserOverride[0],
					CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
				var preview = new CollectionMemberEffectPreview(CollectionMemberKey.FromProvider("member-a"),
					CollectionRecipeIdentity.FromFingerprint("recipe-a"), ModInstallMethod.Direct, ModInstallRoot.Data,
					new[] { new CollectionPlannedFileEffect(deploymentTarget) },
					new[] { new CollectionPlannedIniEffect(iniKey, "High") },
					new[] { new CollectionPlannedGameValueEffect("setting", new byte[] { 1, 2 }) },
					new CollectionPlannedPluginEffect[0], new CollectionEffectPreviewIssue[0]);

				Assert.IsTrue(InvokeVerifyMemberEffects(state, nativeMod, preview));

				var wrongFile = new CollectionNativeFileState(deploymentTarget, temp, false, true, false, "owner-b",
					new[] { owner }, new CollectionNativeOwnerState[0], new CollectionNativeOwnerState[0]);
				var wrongState = new CollectionNativeStateIndex(target, new CollectionNativeRootState[0], new[] { nativeMod }, new[] { wrongFile },
					new[] { ini }, new[] { game }, new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable,
					new CollectionTargetAssociation[0], new CollectionMemberBinding[0], new UserOverride[0],
					CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
				Assert.IsFalse(InvokeVerifyMemberEffects(wrongState, nativeMod, preview));
			}
			finally
			{
				File.Delete(temp);
			}
		}

		[Test]
		public void ReviewedFileContentMatches_UsesPreviewHashWithoutIncomingArchive()
		{
			string temp = Path.GetTempFileName();
			try
			{
				byte[] bytes = { 1, 2, 3, 4 };
				File.WriteAllBytes(temp, bytes);
				ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\a.dds");
				var effect = new CollectionPlannedFileEffect(target,
					CollectionContentHash.FromSha256("9f64a747e1b97f131fabb6b447296c9b6f0201e79fb3c5356e6c77e89b6a806a"), bytes.Length);

				Assert.IsTrue(CollectionNativeChildVerificationCoordinator.ReviewedFileContentMatches(effect, temp));

				File.WriteAllBytes(temp, new byte[] { 1, 9, 3, 4 });
				Assert.IsFalse(CollectionNativeChildVerificationCoordinator.ReviewedFileContentMatches(effect, temp));
			}
			finally
			{
				File.Delete(temp);
			}
		}

		[Test]
		public void ReviewedFileContentMatches_RejectsPreviewWithoutExactContentIdentity()
		{
			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\a.dds");
			Assert.IsFalse(CollectionNativeChildVerificationCoordinator.ReviewedFileContentMatches(
				new CollectionPlannedFileEffect(target), "unused"));
		}

		/// <summary>Uses exact retained file and replay preimages for a failed new activation in both deployment methods.</summary>
		[TestCase(ModInstallMethod.Virtual, "unchanged", true)]
		[TestCase(ModInstallMethod.Direct, "unchanged", true)]
		[TestCase(ModInstallMethod.Virtual, "absent", true)]
		[TestCase(ModInstallMethod.Direct, "absent", true)]
		[TestCase(ModInstallMethod.Virtual, "changed-bytes", false)]
		[TestCase(ModInstallMethod.Virtual, "unexpected-file", false)]
		[TestCase(ModInstallMethod.Virtual, "changed-replay", false)]
		[TestCase(ModInstallMethod.Virtual, "changed-fingerprint", false)]
		[TestCase(ModInstallMethod.Virtual, "legacy", false)]
		[TestCase(ModInstallMethod.Virtual, "reported-commit", false)]
		public void LiveRollback_RequiresExactRetainedPreState(ModInstallMethod method, string scenario, bool accepted)
		{
			string directory = Path.Combine(Path.GetTempPath(), "collection-live-rollback-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			try
			{
				string path = Path.Combine(directory, "mod.dll");
				byte[] before = { 1, 2, 3, 4 };
				bool existed = scenario != "absent" && scenario != "unexpected-file";
				if (existed) File.WriteAllBytes(path, before);
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c68-rollback");
				ModDeploymentTarget deployment = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "mod.dll");
				var file = new CollectionNativeFileState(deployment, path, false, false, false, null,
					new CollectionNativeOwnerState[0], new CollectionNativeOwnerState[0], new CollectionNativeOwnerState[0]);
				var state = new CollectionNativeStateIndex(target, new CollectionNativeRootState[0], new CollectionNativeModState[0],
					new[] { file }, new CollectionNativeIniState[0], new CollectionNativeGameValueState[0], new CollectionNativePluginState[0],
					CollectionNativeStateCoverage.NotApplicable, new CollectionTargetAssociation[0], new CollectionMemberBinding[0],
					new UserOverride[0], CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
				CollectionContentHash hash;
				using (var sha = System.Security.Cryptography.SHA256.Create())
					hash = CollectionContentHash.FromSha256(BitConverter.ToString(sha.ComputeHash(before)).Replace("-", "").ToLowerInvariant());
				var preview = new CollectionMemberEffectPreview(CollectionMemberKey.FromProvider("rollback-member"),
					CollectionRecipeIdentity.FromFingerprint("rollback-recipe"), method, ModInstallRoot.Data,
					new[] { new CollectionPlannedFileEffect(deployment, hash, before.Length) }, new CollectionPlannedIniEffect[0],
					new CollectionPlannedGameValueEffect[0], new CollectionPlannedPluginEffect[0], new CollectionEffectPreviewIssue[0]);
				var evidence = new CollectionNativeChildExecutionEvidence("game", 10, 20, "incoming.7z", preview,
					new[] { new CollectionNativeFileContentEvidence(deployment, existed, existed ? hash : null, existed ? before.Length : 0) },
					new[] { new CollectionNativeFileContentEvidence(deployment, true, hash, before.Length) },
					new CollectionReplayContentEvidence(false, null, 0, false, new CollectionReplayPayloadContentEvidence[0]),
					new CollectionExpectedReplayOperation[0]);
				CollectionIdentity collection = CollectionIdentity.FromNexus("rollback-collection");
				CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "rollback-revision", 1);
				ModOperationIdentity native = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
					new ModOperationFingerprint(target.Fingerprint, new ModInstallContext(method, ModInstallRoot.Data), "rollback-recipe"));
				var manifest = new CollectionNativeChildRecoveryManifest(CollectionOperationIdentity.CreateNew(), 1,
					CollectionPlanIdentity.From(Guid.NewGuid(), 1), new CollectionOperationMemberReference(revision, preview.MemberKey),
					CollectionNativeChildAction.ActivateOrReinstall, native,
					scenario == "changed-fingerprint" ? new CollectionCurrentStateFingerprint(state.Fingerprint.FormatVersion, "changed") : state.Fingerprint,
					new CollectionRecoveryArtifact("incoming", hash, before.Length), null, null,
					new CollectionScriptedReplayRecoverySnapshot(false, null, false, new CollectionReplayRecoveryPayload[0]),
					scenario == "legacy" ? null : evidence);
				if (scenario == "changed-bytes" || scenario == "unexpected-file")
					File.WriteAllBytes(path, new byte[] { 9, 2, 3, 4 });
				if (scenario == "changed-replay")
				{
					string replay = ScriptedFileSelectionCache.GetDefaultFilePath("incoming.7z", directory);
					Directory.CreateDirectory(Path.GetDirectoryName(replay));
					File.WriteAllText(replay, "changed");
				}
				IGameMode game = InterfaceStub<IGameMode>.Create((member, args) => null);
				var reported = new ModOperationResult(native, ModOperationReportedStatus.Failed,
					scenario == "reported-commit" ? ModOperationDurability.VerifiedCommitted : ModOperationDurability.Unknown, "overwrite rejected");
				Assert.That(CollectionNativeChildVerificationCoordinator.TryVerifyRolledBackState(reported, preview, manifest, state,
					directory, game), Is.EqualTo(accepted));
			}
			finally { Directory.Delete(directory, true); }
		}

		private static ModOperationResult CreateResult(ModOperationReportedStatus status, ModOperationDurability durability)
		{
			ModOperationIdentity identity = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint("target-c68-test", new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data), "recipe-a"));
			return new ModOperationResult(identity, status, durability, null);
		}

		private static ModOperationDurability InvokeDurability(ModOperationResult reported, bool committed, bool preState)
		{
			MethodInfo method = typeof(CollectionNativeChildVerificationCoordinator).GetMethod(
				"DetermineVerifiedDurability", BindingFlags.Static | BindingFlags.NonPublic);
			Assert.IsNotNull(method);
			return (ModOperationDurability)method.Invoke(null, new object[] { reported, committed, preState });
		}

		private static bool InvokeVerifyMemberEffects(CollectionNativeStateIndex state, CollectionNativeModState nativeMod,
			CollectionMemberEffectPreview preview)
		{
			MethodInfo method = typeof(CollectionNativeChildVerificationCoordinator).GetMethod(
				"VerifyMemberEffects", BindingFlags.Static | BindingFlags.NonPublic);
			Assert.IsNotNull(method);
			return (bool)method.Invoke(null, new object[] { state, nativeMod, preview });
		}
	}
}
