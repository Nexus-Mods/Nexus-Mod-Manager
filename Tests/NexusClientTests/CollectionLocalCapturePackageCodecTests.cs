using System;
using System.IO;
using System.Text;
using Nexus.Client.CollectionManagement;
using Nexus.Client.ModManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>C7.8 durable sealed-capture package format coverage.</summary>
	[TestFixture]
	public class CollectionLocalCapturePackageCodecTests
	{
		[Test]
		public void SerializeAndInspect_PreservesHeaderAndAllC7SnapshotSections()
		{
			CollectionCaptureSealResult result = CreateSealedResult();

			byte[] bytes = CollectionLocalCapturePackageCodec.Serialize(result);
			CollectionLocalCapturePackageInspection inspection = CollectionLocalCapturePackageCodec.Inspect(bytes);
			string json = Encoding.UTF8.GetString(bytes);

			Assert.AreEqual(CollectionLocalCapturePackageCodec.CurrentFormatVersion, inspection.FormatVersion);
			Assert.AreEqual(result.SealedCapture.Capture.Identity, inspection.CaptureIdentity);
			Assert.AreEqual(result.SealedCapture.Capture.Capability, inspection.Capability);
			Assert.AreEqual(result.SealedCapture.Capture.SchemaVersion, inspection.CaptureSchemaVersion);
			Assert.AreEqual(result.SealedCapture.Capture.CapabilityVersion, inspection.CapabilityVersion);
			Assert.AreEqual(result.SealedCapture.Capture.SourceTarget, inspection.SourceTarget);
			Assert.AreEqual(17, inspection.DeploymentCommitSequence);
			StringAssert.Contains("\"installedIdentities\"", json);
			StringAssert.Contains("\"ownerPayloads\"", json);
			StringAssert.Contains("\"scriptedReplay\"", json);
			StringAssert.Contains("\"nativeEffects\"", json);
			StringAssert.Contains("\"userMetadata\"", json);
			StringAssert.Contains("\"sealingIssues\"", json);
		}

		[Test]
		public void SerializeAndDeserialize_RehydratesTypedSealedCaptureForRestorePlanning()
		{
			CollectionCaptureSealResult result = CreateSealedResult();

			CollectionSealedCaptureSnapshot restored = CollectionLocalCapturePackageCodec.Deserialize(
				CollectionLocalCapturePackageCodec.Serialize(result));

			Assert.AreEqual(result.SealedCapture.Capture.Identity, restored.Capture.Identity);
			Assert.AreEqual(result.SealedCapture.Capture.Revision, restored.Capture.Revision);
			Assert.AreEqual(result.SealedCapture.Capture.SourceTarget, restored.Capture.SourceTarget);
			Assert.AreEqual(result.SealedCapture.Capture.Capability, restored.Capture.Capability);
			Assert.AreEqual(result.SealedCapture.Capture.CapturedStateFingerprint, restored.Capture.CapturedStateFingerprint);
			Assert.AreEqual(17, restored.InstalledIdentities.DeploymentCommitSequence);
			Assert.AreEqual(result.SealedCapture.OwnerPayloads.CaptureIdentity, restored.OwnerPayloads.CaptureIdentity);
			Assert.AreEqual(result.SealedCapture.ScriptedReplay.CaptureIdentity, restored.ScriptedReplay.CaptureIdentity);
			Assert.AreEqual(result.SealedCapture.UserMetadata.CaptureIdentity, restored.UserMetadata.CaptureIdentity);
			Assert.AreEqual("native-a", restored.InstalledIdentities.Mods[0].NativeSnapshotKey);
			Assert.AreEqual(ModInstallMethod.Direct, restored.InstalledIdentities.Mods[0].InstallContext.Method);
			Assert.AreEqual("example.bin", restored.OwnerPayloads.Targets[0].Target.RelativePath);
			Assert.AreEqual(new string('a', 64), restored.Capture.RetainedArtifacts[0].ContentHash.Value);
		}

		[Test]
		public void Inspect_RejectsIncompleteEnvelope()
		{
			byte[] malformed = Encoding.UTF8.GetBytes("{\"format\":\"nmm-ce-local-collection-capture\",\"formatVersion\":1}");
			Assert.Throws<InvalidDataException>(() => CollectionLocalCapturePackageCodec.Inspect(malformed));
		}

		private static CollectionCaptureSealResult CreateSealedResult()
		{
			CollectionIdentity collection = CollectionIdentity.FromLocal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromLocal(collection,
				Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
			LocalCaptureIdentity captureIdentity = LocalCaptureIdentity.From(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c78-package");
			CollectionMemberKey memberKey = CollectionMemberKey.FromLocal(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"));
			CollectionContentHash hash = CollectionContentHash.FromSha256(new string('a', 64));
			var retained = new RetainedArtifactReference("artifact-c78", "owner-payload:native-a", hash, 123);
			var mapping = new LocalCaptureNativeRecordMapping(memberKey, new NativeModInstanceIdentity(target, "native-a"));
			var capture = new LocalCapture(captureIdentity, revision, target,
				new CollectionCurrentStateFingerprint("state-v1", "sealed-state"),
				new LocalCaptureScope(LocalCaptureScope.CurrentVersion, new[]
				{
					LocalCaptureScopeArea.ManagedModState,
					LocalCaptureScopeArea.FileOwnershipAndFallbackPayloads
				}),
				LocalCaptureCapability.RecipeOnly, new[] { retained },
				new LocalCaptureExclusion[0], new[] { mapping });
			var installed = new CollectionInstalledModIdentity("native-a",
				new CollectionInstalledArchiveReference(String.Empty, "mod.zip", false, null), "42", "77", "1.0", "1.0", false,
				new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data), new CollectionInstalledMemberProvenance[0]);
			var identities = new CollectionInstalledIdentitySnapshot(target, 17,
				new[] { installed }, NativeStateCaptureCoverage.Complete, new CollectionInstalledIdentityIssue[0]);
			ModDeploymentTarget deploymentTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "example.bin");
			var retention = new CollectionOwnerPayloadRetention(retained.StableArtifactId, retained.Role, retained.ContentHash, retained.ByteLength);
			var ownerPayloads = new CollectionOwnerPayloadSnapshot(target, captureIdentity, 17,
				new[]
				{
					new CollectionOwnerPayloadTarget(deploymentTarget, true, new[]
					{
						new CollectionOwnerPayloadOwner(0, "native-a", String.Empty,
							NativeStateCaptureDeploymentOwnerKind.Direct, true, retention)
					})
				}, NativeStateCaptureCoverage.Complete, new CollectionOwnerPayloadIssue[0]);
			var replay = new CollectionScriptedReplaySnapshot(target, captureIdentity, 17,
				new CollectionScriptedReplayArtifactSet[0], NativeStateCaptureCoverage.NotApplicable,
				new CollectionScriptedReplayIssue[0]);
			var effects = new CollectionNativeEffectSnapshot(target, 17,
				new CollectionCapturedIniEffect[0], NativeStateCaptureCoverage.NotApplicable,
				new CollectionCapturedGameValueEffect[0], NativeStateCaptureCoverage.NotApplicable,
				new NativeStateCapturePlugin[0], NativeStateCaptureCoverage.NotApplicable,
				new CollectionNativeEffectIssue[0]);
			var metadata = new CollectionUserMetadataSnapshot(target, captureIdentity, 17,
				new CollectionCapturedSortAssignment[0], NativeStateCaptureCoverage.NotApplicable,
				new CollectionCapturedScreenshotOverride[0], NativeStateCaptureCoverage.NotApplicable,
				new CollectionUserMetadataIssue[0]);
			var sealedCapture = new CollectionSealedCaptureSnapshot(capture, identities,
				new CollectionCapturedArchiveArtifact[0], ownerPayloads, replay, effects, metadata);
			return new CollectionCaptureSealResult(LocalCaptureCapability.RecipeOnly, sealedCapture,
				new CollectionCaptureSealIssue[0]);
		}
	}
}
