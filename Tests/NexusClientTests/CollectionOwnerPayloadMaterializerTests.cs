using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.Mods;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Step 2C exact owner-payload materialization characterization.</summary>
	[TestFixture]
	[Category("CollectionsC12Workflow")]
	[Category("CollectionsC12Performance")]
	public class CollectionOwnerPayloadMaterializerTests
	{
		private const long Checkpoint = 29;
		private const string NativeKey = "native-step2c";
		private const string ArchiveEntry = "textures\\step2c.dds";

		[Test]
		public void Materialize_ArchiveBackedPayloadWritesExactVerifiedBytes()
		{
			string root = CreateTemporaryDirectory("nmm-step2c-archive-");
			try
			{
				CollectionsStore store = CreateStore(root);
				var artifactStore = new CollectionsRetainedArtifactStore(store);
				CollectionSealedCaptureSnapshot capture = CreateArchiveBackedCapture(root, artifactStore, out CollectionOwnerPayloadSource source);
				byte[] payload = Encoding.ASCII.GetBytes("archive-backed-exact-payload");
				string entryPath = WriteFile(root, "entry.bin", payload);
				int factoryCalls = 0;
				IMod mod = CreateArchiveMod(entryPath, payloadPath => Assert.AreEqual(ArchiveEntry, payloadPath));
				string output = Path.Combine(root, "materialized.bin");

				using (var materializer = new CollectionOwnerPayloadMaterializer(artifactStore, capture, archivePath =>
				{
					factoryCalls++;
					Assert.IsTrue(File.Exists(archivePath));
					return mod;
				}))
				{
					materializer.Materialize(source, output, CancellationToken.None);
				}

				Assert.AreEqual(1, factoryCalls);
				CollectionAssert.AreEqual(payload, File.ReadAllBytes(output));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Materialize_ArchiveBackedPayloadRejectsWrongEntryBytesAndDeletesPartialOutput()
		{
			string root = CreateTemporaryDirectory("nmm-step2c-mismatch-");
			try
			{
				CollectionsStore store = CreateStore(root);
				var artifactStore = new CollectionsRetainedArtifactStore(store);
				CollectionSealedCaptureSnapshot capture = CreateArchiveBackedCapture(root, artifactStore, out CollectionOwnerPayloadSource source);
				string entryPath = WriteFile(root, "wrong-entry.bin", Encoding.ASCII.GetBytes("wrong-payload-bytes"));
				IMod mod = CreateArchiveMod(entryPath, null);
				string output = Path.Combine(root, "materialized.bin");

				using (var materializer = new CollectionOwnerPayloadMaterializer(artifactStore, capture, archivePath => mod))
				{
					Assert.Throws<InvalidDataException>(() => materializer.Materialize(source, output, CancellationToken.None));
				}

				Assert.IsFalse(File.Exists(output));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Materialize_CorruptRetainedArchiveBlocksBeforeOpeningArchiveEntry()
		{
			string root = CreateTemporaryDirectory("nmm-step2c-corrupt-");
			try
			{
				CollectionsStore store = CreateStore(root);
				var artifactStore = new CollectionsRetainedArtifactStore(store);
				CollectionSealedCaptureSnapshot capture = CreateArchiveBackedCapture(root, artifactStore, out CollectionOwnerPayloadSource source);
				string[] retainedFiles = Directory.GetFiles(store.RetainedContentDirectory, "*.blob", SearchOption.AllDirectories);
				Assert.AreEqual(1, retainedFiles.Length);
				File.WriteAllBytes(retainedFiles[0], Encoding.ASCII.GetBytes("retained-archive-corruption"));
				int factoryCalls = 0;
				string output = Path.Combine(root, "materialized.bin");

				using (var materializer = new CollectionOwnerPayloadMaterializer(artifactStore, capture, archivePath =>
				{
					factoryCalls++;
					return null;
				}))
				{
					Assert.Throws<InvalidDataException>(() => materializer.Materialize(source, output, CancellationToken.None));
				}

				Assert.AreEqual(0, factoryCalls);
				Assert.IsFalse(File.Exists(output));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Materialize_CapturedArtifactPreservesBlobOnlyRestorePath()
		{
			string root = CreateTemporaryDirectory("nmm-step2c-legacy-");
			try
			{
				CollectionsStore store = CreateStore(root);
				var artifactStore = new CollectionsRetainedArtifactStore(store);
				byte[] payload = Encoding.ASCII.GetBytes("legacy-v1-blob-payload");
				string sourcePath = WriteFile(root, "payload.bin", payload);
				CollectionsRetainedArtifact artifact = artifactStore.PublishFile(sourcePath);
				var retainedReference = new RetainedArtifactReference(artifact.ArtifactId, "owner-payload:legacy",
					artifact.ContentHash, artifact.ByteLength);
				var retention = new CollectionOwnerPayloadRetention(retainedReference.StableArtifactId, retainedReference.Role,
					retainedReference.ContentHash, retainedReference.ByteLength);
				CollectionOwnerPayloadSource source = CollectionOwnerPayloadSource.FromCapturedArtifact(retention);
				CollectionSealedCaptureSnapshot capture = CreateMinimalCapture(new[] { retainedReference },
					new CollectionCapturedArchiveArtifact[0], new CollectionInstalledModIdentity[0]);
				string output = Path.Combine(root, "materialized.bin");

				using (var materializer = new CollectionOwnerPayloadMaterializer(artifactStore, capture,
					archivePath => throw new InvalidOperationException("A blob-only payload must not open a retained source archive.")))
				{
					materializer.Materialize(source, output, CancellationToken.None);
				}

				CollectionAssert.AreEqual(payload, File.ReadAllBytes(output));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static CollectionSealedCaptureSnapshot CreateArchiveBackedCapture(string root,
			CollectionsRetainedArtifactStore artifactStore, out CollectionOwnerPayloadSource source)
		{
			byte[] archiveBytes = Encoding.ASCII.GetBytes("retained-source-archive-step2c");
			string sourceArchive = WriteFile(root, "mod.zip", archiveBytes);
			CollectionsRetainedArtifact artifact = artifactStore.PublishFile(sourceArchive);
			var archiveReference = new RetainedArtifactReference(artifact.ArtifactId, "mod-archive:" + NativeKey,
				artifact.ContentHash, artifact.ByteLength);
			var capturedArchive = new CollectionCapturedArchiveArtifact(NativeKey, "mod.zip", null, archiveReference);
			var installContext = new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data);
			var installed = new CollectionInstalledModIdentity(NativeKey,
				new CollectionInstalledArchiveReference(sourceArchive, "mod.zip", true, null), "42", "77", "1.0", "1.0", false,
				installContext, new CollectionInstalledMemberProvenance[0]);
			byte[] payload = Encoding.ASCII.GetBytes("archive-backed-exact-payload");
			CollectionContentHash payloadHash = Hash(payload);
			string proof = CollectionOwnerPayloadArchiveReconstruction.CreateProofIdentity(NativeKey, installContext,
				ArchiveEntry, payloadHash, payload.LongLength);
			source = CollectionOwnerPayloadSource.FromArchiveBacked(new CollectionOwnerPayloadArchiveBackedDescriptor(
				CollectionOwnerPayloadArchiveBackedDescriptor.CurrentFormatVersion,
				CollectionOwnerPayloadArchiveBackedKind.ExactArchiveEntry, NativeKey, ArchiveEntry, proof), payloadHash, payload.LongLength);
			return CreateMinimalCapture(new[] { archiveReference }, new[] { capturedArchive }, new[] { installed });
		}

		private static CollectionSealedCaptureSnapshot CreateMinimalCapture(IEnumerable<RetainedArtifactReference> retainedArtifacts,
			IEnumerable<CollectionCapturedArchiveArtifact> archives, IEnumerable<CollectionInstalledModIdentity> installed)
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-step2c");
			CollectionIdentity collection = CollectionIdentity.FromLocal(Guid.NewGuid());
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromLocal(collection, Guid.NewGuid());
			LocalCaptureIdentity captureIdentity = LocalCaptureIdentity.From(Guid.NewGuid());
			var capture = new LocalCapture(captureIdentity, revision, target,
				new CollectionCurrentStateFingerprint("state-v1", "step2c"),
				new LocalCaptureScope(LocalCaptureScope.CurrentVersion, new[]
				{
					LocalCaptureScopeArea.ManagedModState,
					LocalCaptureScopeArea.ModArchives,
					LocalCaptureScopeArea.FileOwnershipAndFallbackPayloads
				}), LocalCaptureCapability.LocallyRestorableWithinScope, retainedArtifacts,
				new LocalCaptureExclusion[0], new LocalCaptureNativeRecordMapping[0]);
			var identities = new CollectionInstalledIdentitySnapshot(target, Checkpoint, installed,
				NativeStateCaptureCoverage.Complete, new CollectionInstalledIdentityIssue[0]);
			var ownerPayloads = new CollectionOwnerPayloadSnapshot(target, captureIdentity, Checkpoint,
				new CollectionOwnerPayloadTarget[0], NativeStateCaptureCoverage.Complete, new CollectionOwnerPayloadIssue[0]);
			var replay = new CollectionScriptedReplaySnapshot(target, captureIdentity, Checkpoint,
				new CollectionScriptedReplayArtifactSet[0], NativeStateCaptureCoverage.NotApplicable,
				new CollectionScriptedReplayIssue[0]);
			var effects = new CollectionNativeEffectSnapshot(target, Checkpoint,
				new CollectionCapturedIniEffect[0], NativeStateCaptureCoverage.NotApplicable,
				new CollectionCapturedGameValueEffect[0], NativeStateCaptureCoverage.NotApplicable,
				new NativeStateCapturePlugin[0], NativeStateCaptureCoverage.NotApplicable,
				new CollectionNativeEffectIssue[0]);
			var metadata = new CollectionUserMetadataSnapshot(target, captureIdentity, Checkpoint,
				new CollectionCapturedSortAssignment[0], NativeStateCaptureCoverage.NotApplicable,
				new CollectionCapturedScreenshotOverride[0], NativeStateCaptureCoverage.NotApplicable,
				new CollectionUserMetadataIssue[0]);
			return new CollectionSealedCaptureSnapshot(capture, identities, archives, ownerPayloads, replay, effects, metadata);
		}

		private static IMod CreateArchiveMod(string entryPath, Action<string> onOpen)
		{
			return InterfaceStub<IMod>.Create((method, args) =>
			{
				if (method.Name == "GetFileList" && (args == null || args.Length == 0))
					return new List<string> { ArchiveEntry };
				if (method.Name == "GetFileStream")
				{
					string requested = (string)args[0];
					onOpen?.Invoke(requested);
					return new FileStream(entryPath, FileMode.Open, FileAccess.Read, FileShare.Read);
				}
				throw new InvalidOperationException("Unexpected IMod call in Step 2C materializer characterization: " + method.Name);
			});
		}

		private static CollectionContentHash Hash(byte[] value)
		{
			using (SHA256 sha256 = SHA256.Create())
				return CollectionContentHash.FromSha256(BitConverter.ToString(sha256.ComputeHash(value)).Replace("-", String.Empty).ToLowerInvariant());
		}

		private static string WriteFile(string root, string fileName, byte[] bytes)
		{
			string path = Path.Combine(root, fileName);
			File.WriteAllBytes(path, bytes);
			return path;
		}

		private static CollectionsStore CreateStore(string root)
		{
			var store = new CollectionsStore(new Nexus.Client.GameStorage.GameStoragePathSet
			{
				GameId = "TEST",
				GameName = "TEST",
				GameInstallPath = root,
				InstallInfoPath = Path.Combine(root, "info"),
				ModsPath = Path.Combine(root, "mods"),
				VirtualInstallPath = Path.Combine(root, "cache"),
				LinkFolderPath = Path.Combine(root, "overwrite"),
				LinkFolderRequired = false
			});
			store.CreateNew();
			return store;
		}

		private static string CreateTemporaryDirectory(string prefix)
		{
			string path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}
	}
}
