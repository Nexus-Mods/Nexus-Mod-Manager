using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.ModManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>C7.8 durable sealed-capture package format coverage.</summary>
	[TestFixture]
	[Category("CollectionsGateL")]
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
			StringAssert.Contains("\"PayloadSource\"", json);
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
			Assert.AreEqual(CollectionOwnerPayloadSourceKind.CapturedArtifact,
				restored.OwnerPayloads.Targets[0].Owners[0].PayloadSource.Kind);
			Assert.AreEqual("artifact-c78", restored.OwnerPayloads.Targets[0].Owners[0].PayloadSource.CapturedArtifact.StableArtifactId);
			Assert.AreEqual(CollectionOwnerPayloadVirtualFallbackState.NotApplicable,
				restored.OwnerPayloads.Targets[0].VirtualFallback.State);
			Assert.AreEqual(new string('a', 64), restored.Capture.RetainedArtifacts[0].ContentHash.Value);
		}

		[Test]
		public void SerializeAndDeserialize_V2ArchiveBackedOwnerPreservesReconstructionDescriptor()
		{
			CollectionSealedCaptureSnapshot restored = CollectionLocalCapturePackageCodec.Deserialize(
				CollectionLocalCapturePackageCodec.Serialize(CreateSealedResult(true)));

			CollectionOwnerPayloadOwner owner = restored.OwnerPayloads.Targets[0].Owners[0];
			Assert.IsNull(owner.RetainedPayload);
			Assert.AreEqual(CollectionOwnerPayloadSourceKind.ArchiveBacked, owner.PayloadSource.Kind);
			Assert.AreEqual(CollectionOwnerPayloadArchiveBackedKind.ExactArchiveEntry,
				owner.PayloadSource.ArchiveBacked.ReconstructionKind);
			Assert.AreEqual("native-a", owner.PayloadSource.ArchiveBacked.NativeSnapshotKey);
			Assert.AreEqual("example.bin", owner.PayloadSource.ArchiveBacked.ArchiveEntryPath);
			Assert.AreEqual("prepared-native-c78", owner.PayloadSource.ArchiveBacked.ReconstructionIdentity);
			Assert.AreEqual(new string('a', 64), owner.PayloadSource.ExpectedContentHash.Value);
			Assert.AreEqual(123, owner.PayloadSource.ExpectedByteLength);
		}

		[Test]
		public void Deserialize_LegacyV1BlobOwnerIsAdaptedToCapturedArtifactSource()
		{
			CollectionCaptureSealResult result = CreateSealedResult();
			JObject root = JObject.Parse(Encoding.UTF8.GetString(CollectionLocalCapturePackageCodec.Serialize(result)));
			root["formatVersion"] = 1;
			JObject owner = (JObject)root["ownerPayloads"]["Targets"][0]["Owners"][0];
			owner.Remove("PayloadSource");

			CollectionSealedCaptureSnapshot restored = CollectionLocalCapturePackageCodec.Deserialize(
				Encoding.UTF8.GetBytes(root.ToString(Formatting.None)));

			CollectionOwnerPayloadOwner restoredOwner = restored.OwnerPayloads.Targets[0].Owners[0];
			Assert.IsNotNull(restoredOwner.RetainedPayload);
			Assert.IsNotNull(restoredOwner.PayloadSource);
			Assert.AreEqual(CollectionOwnerPayloadSourceKind.CapturedArtifact, restoredOwner.PayloadSource.Kind);
			Assert.AreEqual(restoredOwner.RetainedPayload.StableArtifactId,
				restoredOwner.PayloadSource.CapturedArtifact.StableArtifactId);
		}

		[Test]
		public void Inspect_RejectsIncompleteEnvelope()
		{
			byte[] malformed = Encoding.UTF8.GetBytes("{\"format\":\"nmm-ce-local-collection-capture\",\"formatVersion\":1}");
			Assert.Throws<InvalidDataException>(() => CollectionLocalCapturePackageCodec.Inspect(malformed));
		}

		[Test]
		public void Deserialize_PreFallbackPureVirtualShapeIsExplicitlyLegacyUncaptured()
		{
			CollectionCaptureSealResult result = CreateSealedResult();
			JObject root = JObject.Parse(Encoding.UTF8.GetString(CollectionLocalCapturePackageCodec.Serialize(result)));
			JObject target = (JObject)root["ownerPayloads"]["Targets"][0];
			target["Promoted"] = false;
			target.Remove("VirtualFallback");

			CollectionSealedCaptureSnapshot restored = CollectionLocalCapturePackageCodec.Deserialize(
				Encoding.UTF8.GetBytes(root.ToString(Formatting.None)));

			Assert.AreEqual(CollectionOwnerPayloadVirtualFallbackState.LegacyUncaptured,
				restored.OwnerPayloads.Targets[0].VirtualFallback.State);
		}

		private static CollectionCaptureSealResult CreateSealedResult(bool archiveBacked = false)
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
			CollectionOwnerPayloadSource payloadSource = archiveBacked
				? CollectionOwnerPayloadSource.FromArchiveBacked(new CollectionOwnerPayloadArchiveBackedDescriptor(
					CollectionOwnerPayloadArchiveBackedDescriptor.CurrentFormatVersion,
					CollectionOwnerPayloadArchiveBackedKind.ExactArchiveEntry, "native-a", "example.bin", "prepared-native-c78"), hash, 123)
				: CollectionOwnerPayloadSource.FromCapturedArtifact(retention);
			var ownerPayloads = new CollectionOwnerPayloadSnapshot(target, captureIdentity, 17,
				new[]
				{
					new CollectionOwnerPayloadTarget(deploymentTarget, true, new[]
					{
						new CollectionOwnerPayloadOwner(0, "native-a", String.Empty,
							NativeStateCaptureDeploymentOwnerKind.Direct, true, archiveBacked ? null : retention, payloadSource)
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
