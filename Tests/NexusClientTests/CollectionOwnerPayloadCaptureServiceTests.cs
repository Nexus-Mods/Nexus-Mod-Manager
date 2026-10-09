using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;
using Nexus.Client.Util.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// C7.3 owner/fallback payload retention characterization.
	/// </summary>
	[Category("CollectionsGateL")]
	public class CollectionOwnerPayloadCaptureServiceTests
	{
		[Test]
		public void Capture_PromotedStackRetainsOriginalDirectAndVirtualPayloadsInExactOrder()
		{
			string root = CreateTemporaryDirectory("nmm-c73-promoted-");
			try
			{
				string originalPath = WritePayload(root, "original.bin", "original");
				string directPath = WritePayload(root, "direct.bin", "direct");
				string virtualPath = WritePayload(root, "virtual.bin", "virtual");
				ModDeploymentTarget deploymentTarget = ModDeploymentTargetResolver.FromCanonical(
					ModDeploymentRoot.Data, "meshes\\shared.nif");
				InstallLogReadSnapshot install = CreateInstall(
					new[]
					{
						CreateMod("direct-a", ModInstallMethod.Direct),
						CreateMod("virtual-b", ModInstallMethod.Virtual)
					},
					new InstallLogReadFile[0],
					new[] { new InstallLogReadDeploymentTarget(deploymentTarget, new[] { "original-values", "direct-a", "virtual-b" }) });
				NativeStateCaptureDeploymentTarget observed = new NativeStateCaptureDeploymentTarget(deploymentTarget, virtualPath,
					new[]
					{
						new NativeStateCaptureDeploymentOwner("original-values", NativeStateCaptureDeploymentOwnerKind.OriginalValue, false, originalPath),
						new NativeStateCaptureDeploymentOwner("direct-a", NativeStateCaptureDeploymentOwnerKind.Direct, false, directPath),
						new NativeStateCaptureDeploymentOwner("virtual-b", NativeStateCaptureDeploymentOwnerKind.Virtual, true, virtualPath)
					});
				NativeStateCaptureSnapshot nativeState = CreateNativeState(install, new VirtualModReadLink[0], new[] { observed },
					NativeStateCaptureCoverage.Complete);
				CollectionsStore store;
				CollectionOwnerPayloadCaptureService service = CreateService(root, out store);
				LocalCaptureIdentity captureIdentity = LocalCaptureIdentity.From(Guid.Parse("10000000-0000-0000-0000-000000000001"));

				CollectionOwnerPayloadSnapshot snapshot = service.Capture(
					CollectionTargetIdentity.FromFingerprint("target-c73-promoted"), captureIdentity, nativeState, CancellationToken.None);

				Assert.AreEqual(NativeStateCaptureCoverage.Complete, snapshot.Coverage);
				Assert.AreEqual(1, snapshot.Targets.Count);
				CollectionOwnerPayloadTarget target = snapshot.Targets[0];
				Assert.IsTrue(target.Promoted);
				CollectionAssert.AreEqual(new[] { "original-values", "direct-a", "virtual-b" },
					target.Owners.Select(x => x.OwnerKey).ToArray());
				Assert.AreEqual("virtual-b", target.CurrentWinner.OwnerKey);
				Assert.IsTrue(target.Owners.All(x => x.RetainedPayload != null));
				Assert.AreEqual(3, new CollectionsRetainedArtifactReferenceStore(store)
					.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Capture, captureIdentity.ToString()).Count);
				AssertRetainedText(store, target.Owners[0].RetainedPayload, "original");
				AssertRetainedText(store, target.Owners[1].RetainedPayload, "direct");
				AssertRetainedText(store, target.Owners[2].RetainedPayload, "virtual");
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Capture_NonPromotedVirtualStackRetainsLowerPriorityPayloadsAndCurrentWinner()
		{
			string root = CreateTemporaryDirectory("nmm-c73-virtual-");
			try
			{
				string firstPath = WritePayload(root, "virtual-high.bin", "high");
				string secondPath = WritePayload(root, "virtual-low.bin", "low");
				string fallbackPath = WritePayload(root, "virtual-fallback.bin", "unmanaged-fallback");
				ModDeploymentTarget deploymentTarget = ModDeploymentTargetResolver.FromCanonical(
					ModDeploymentRoot.GameRoot, "textures\\shared.dds");
				InstallLogReadSnapshot install = CreateInstall(
					new[]
					{
						CreateMod("virtual-high", ModInstallMethod.Virtual),
						CreateMod("virtual-low", ModInstallMethod.Virtual)
					},
					new[] { new InstallLogReadFile("textures\\shared.dds", deploymentTarget, new[] { "virtual-high", "virtual-low" }) },
					new InstallLogReadDeploymentTarget[0]);
				var links = new[]
				{
					new VirtualModReadLink(deploymentTarget, "virtual-low", "low-ref", true, 5, secondPath),
					new VirtualModReadLink(deploymentTarget, "virtual-high", "high-ref", true, 20, firstPath)
				};
				NativeStateCaptureSnapshot nativeState = CreateNativeState(install, links,
					new NativeStateCaptureDeploymentTarget[0], NativeStateCaptureCoverage.NotApplicable,
					new[]
					{
						new NativeStateCaptureVirtualPayloadSource(deploymentTarget, "virtual-high", "high-ref", firstPath),
						new NativeStateCaptureVirtualPayloadSource(deploymentTarget, "virtual-low", "low-ref", secondPath)
					}, new[]
					{
						new NativeStateCaptureVirtualFallback(deploymentTarget,
							NativeStateCaptureVirtualFallbackState.Present, fallbackPath)
					});
				CollectionsStore store;
				CollectionOwnerPayloadCaptureService service = CreateService(root, out store);

				CollectionOwnerPayloadSnapshot snapshot = service.Capture(
					CollectionTargetIdentity.FromFingerprint("target-c73-virtual"),
					LocalCaptureIdentity.From(Guid.Parse("10000000-0000-0000-0000-000000000002")), nativeState, CancellationToken.None);

				Assert.AreEqual(NativeStateCaptureCoverage.Complete, snapshot.Coverage);
				CollectionOwnerPayloadTarget captured = snapshot.Targets.Single();
				Assert.IsFalse(captured.Promoted);
				CollectionAssert.AreEqual(new[] { "virtual-high", "virtual-low" },
					captured.Owners.Select(x => x.OwnerKey).ToArray());
				Assert.AreEqual("virtual-low", captured.CurrentWinner.OwnerKey);
				Assert.AreEqual(NativeStateCaptureDeploymentOwnerKind.Virtual, captured.Owners[0].Kind);
				Assert.IsTrue(captured.Owners.All(x => x.RetainedPayload != null));
				AssertRetainedText(store, captured.Owners[0].RetainedPayload, "high");
				AssertRetainedText(store, captured.Owners[1].RetainedPayload, "low");
				Assert.AreEqual(CollectionOwnerPayloadVirtualFallbackState.Retained, captured.VirtualFallback.State);
				AssertRetainedText(store, captured.VirtualFallback.RetainedPayload, "unmanaged-fallback");
				Assert.AreEqual(3, new CollectionsRetainedArtifactReferenceStore(store)
					.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Capture, snapshot.CaptureIdentity.ToString()).Count);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Capture_NonPromotedVirtualStackRecordsExplicitlyAbsentFallbackWithoutInventingContent()
		{
			string root = CreateTemporaryDirectory("nmm-c73-virtual-absent-");
			try
			{
				string payloadPath = WritePayload(root, "virtual.bin", "managed");
				ModDeploymentTarget deploymentTarget = ModDeploymentTargetResolver.FromCanonical(
					ModDeploymentRoot.Data, "textures\\no-fallback.dds");
				InstallLogReadSnapshot install = CreateInstall(
					new[] { CreateMod("virtual-a", ModInstallMethod.Virtual) },
					new[] { new InstallLogReadFile("textures\\no-fallback.dds", deploymentTarget, new[] { "virtual-a" }) },
					new InstallLogReadDeploymentTarget[0]);
				var links = new[] { new VirtualModReadLink(deploymentTarget, "virtual-a", "a-ref", true, 0, payloadPath) };
				NativeStateCaptureSnapshot nativeState = CreateNativeState(install, links,
					new NativeStateCaptureDeploymentTarget[0], NativeStateCaptureCoverage.NotApplicable,
					new[] { new NativeStateCaptureVirtualPayloadSource(deploymentTarget, "virtual-a", "a-ref", payloadPath) },
					new[] { new NativeStateCaptureVirtualFallback(deploymentTarget,
						NativeStateCaptureVirtualFallbackState.ExplicitlyAbsent, String.Empty) });
				CollectionsStore store;
				CollectionOwnerPayloadCaptureService service = CreateService(root, out store);

				CollectionOwnerPayloadSnapshot snapshot = service.Capture(
					CollectionTargetIdentity.FromFingerprint("target-c73-virtual-absent"),
					LocalCaptureIdentity.From(Guid.Parse("10000000-0000-0000-0000-000000000006")), nativeState, CancellationToken.None);

				CollectionOwnerPayloadTarget captured = snapshot.Targets.Single();
				Assert.AreEqual(NativeStateCaptureCoverage.Complete, snapshot.Coverage);
				Assert.AreEqual(CollectionOwnerPayloadVirtualFallbackState.ExplicitlyAbsent, captured.VirtualFallback.State);
				Assert.IsNull(captured.VirtualFallback.RetainedPayload);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Capture_MissingPromotedPayloadIsExplicitlyPartialAndDoesNotInventArtifact()
		{
			string root = CreateTemporaryDirectory("nmm-c73-missing-");
			try
			{
				string missingPath = Path.Combine(root, "does-not-exist.bin");
				ModDeploymentTarget deploymentTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "missing.bin");
				InstallLogReadSnapshot install = CreateInstall(new[] { CreateMod("direct-a", ModInstallMethod.Direct) },
					new InstallLogReadFile[0], new[] { new InstallLogReadDeploymentTarget(deploymentTarget, new[] { "direct-a" }) });
				NativeStateCaptureDeploymentTarget observed = new NativeStateCaptureDeploymentTarget(deploymentTarget, missingPath,
					new[] { new NativeStateCaptureDeploymentOwner("direct-a", NativeStateCaptureDeploymentOwnerKind.Direct, true, missingPath) });
				NativeStateCaptureSnapshot nativeState = CreateNativeState(install, new VirtualModReadLink[0], new[] { observed },
					NativeStateCaptureCoverage.Complete);
				CollectionsStore store;
				CollectionOwnerPayloadCaptureService service = CreateService(root, out store);

				CollectionOwnerPayloadSnapshot snapshot = service.Capture(
					CollectionTargetIdentity.FromFingerprint("target-c73-missing"),
					LocalCaptureIdentity.From(Guid.Parse("10000000-0000-0000-0000-000000000003")), nativeState, CancellationToken.None);

				Assert.AreEqual(NativeStateCaptureCoverage.Partial, snapshot.Coverage);
				Assert.IsNull(snapshot.Targets.Single().CurrentWinner.RetainedPayload);
				Assert.IsTrue(snapshot.Issues.Any(x => x.Kind == CollectionOwnerPayloadIssueKind.PayloadSourceMissing));
				Assert.AreEqual(0, new CollectionsRetainedArtifactReferenceStore(store)
					.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Capture, snapshot.CaptureIdentity.ToString()).Count);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Capture_LegacyInstallLogOnlyTopologyIsPreservedButNotPretendedRestorable()
		{
			string root = CreateTemporaryDirectory("nmm-c73-legacy-");
			try
			{
				ModDeploymentTarget deploymentTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "legacy.bin");
				InstallLogReadSnapshot install = CreateInstall(new[] { CreateMod("virtual-a", ModInstallMethod.Virtual) },
					new[] { new InstallLogReadFile("legacy.bin", deploymentTarget, new[] { "virtual-a" }) },
					new InstallLogReadDeploymentTarget[0]);
				NativeStateCaptureSnapshot nativeState = CreateNativeState(install, new VirtualModReadLink[0],
					new NativeStateCaptureDeploymentTarget[0], NativeStateCaptureCoverage.NotApplicable);
				CollectionsStore store;
				CollectionOwnerPayloadCaptureService service = CreateService(root, out store);

				CollectionOwnerPayloadSnapshot snapshot = service.Capture(
					CollectionTargetIdentity.FromFingerprint("target-c73-legacy"),
					LocalCaptureIdentity.From(Guid.Parse("10000000-0000-0000-0000-000000000004")), nativeState, CancellationToken.None);

				Assert.AreEqual(NativeStateCaptureCoverage.Partial, snapshot.Coverage);
				Assert.AreEqual("virtual-a", snapshot.Targets.Single().CurrentWinner.OwnerKey);
				Assert.IsNull(snapshot.Targets.Single().CurrentWinner.RetainedPayload);
				Assert.IsTrue(snapshot.Issues.Any(x => x.Kind == CollectionOwnerPayloadIssueKind.LegacyPayloadSourceUnavailable));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Capture_SameCaptureRoleRejectsChangedBytesInsteadOfSilentlyRebinding()
		{
			string root = CreateTemporaryDirectory("nmm-c73-rebind-");
			try
			{
				string payloadPath = WritePayload(root, "winner.bin", "first");
				ModDeploymentTarget deploymentTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "winner.bin");
				InstallLogReadSnapshot install = CreateInstall(new[] { CreateMod("direct-a", ModInstallMethod.Direct) },
					new InstallLogReadFile[0], new[] { new InstallLogReadDeploymentTarget(deploymentTarget, new[] { "direct-a" }) });
				NativeStateCaptureSnapshot nativeState = CreateNativeState(install, new VirtualModReadLink[0],
					new[]
					{
						new NativeStateCaptureDeploymentTarget(deploymentTarget, payloadPath,
							new[] { new NativeStateCaptureDeploymentOwner("direct-a", NativeStateCaptureDeploymentOwnerKind.Direct, true, payloadPath) })
					}, NativeStateCaptureCoverage.Complete);
				CollectionsStore store;
				CollectionOwnerPayloadCaptureService service = CreateService(root, out store);
				LocalCaptureIdentity captureIdentity = LocalCaptureIdentity.From(Guid.Parse("10000000-0000-0000-0000-000000000005"));
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c73-rebind");

				service.Capture(target, captureIdentity, nativeState, CancellationToken.None);
				File.WriteAllText(payloadPath, "second", Encoding.UTF8);

				InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
					service.Capture(target, captureIdentity, nativeState, CancellationToken.None));
				StringAssert.Contains("already bound to different immutable bytes", error.Message);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Capture_EligibleManagedPayloadUsesArchiveBackedSourceWithoutPublishingStandaloneBlob()
		{
			string root = CreateTemporaryDirectory("nmm-c12-step2b-archive-");
			try
			{
				string payloadPath = WritePayload(root, "managed.bin", "managed");
				ModDeploymentTarget deploymentTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "managed.bin");
				InstallLogReadSnapshot install = CreateInstall(new[] { CreateMod("direct-a", ModInstallMethod.Direct) },
					new InstallLogReadFile[0], new[] { new InstallLogReadDeploymentTarget(deploymentTarget, new[] { "direct-a" }) });
				NativeStateCaptureSnapshot nativeState = CreateNativeState(install, new VirtualModReadLink[0],
					new[] { new NativeStateCaptureDeploymentTarget(deploymentTarget, payloadPath,
						new[] { new NativeStateCaptureDeploymentOwner("direct-a", NativeStateCaptureDeploymentOwnerKind.Direct, true, payloadPath) }) },
					NativeStateCaptureCoverage.Complete);
				CollectionsStore store;
				CollectionOwnerPayloadCaptureService service = CreateService(root, out store,
					new StubArchiveEvidenceSource(true));

				CollectionOwnerPayloadSnapshot snapshot = service.Capture(CollectionTargetIdentity.FromFingerprint("target-step2b-archive"),
					LocalCaptureIdentity.From(Guid.Parse("10000000-0000-0000-0000-000000000010")), nativeState, CancellationToken.None);

				CollectionOwnerPayloadOwner owner = snapshot.Targets.Single().CurrentWinner;
				Assert.IsNull(owner.RetainedPayload);
				Assert.IsNotNull(owner.PayloadSource);
				Assert.AreEqual(CollectionOwnerPayloadSourceKind.ArchiveBacked, owner.PayloadSource.Kind);
				Assert.AreEqual("direct-a", owner.PayloadSource.ArchiveBacked.NativeSnapshotKey);
				Assert.AreEqual(0, new CollectionsRetainedArtifactReferenceStore(store)
					.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Capture, snapshot.CaptureIdentity.ToString()).Count);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Capture_ChangedManagedPayloadFallsBackToExistingByteRetention()
		{
			string root = CreateTemporaryDirectory("nmm-c12-step2b-fallback-");
			try
			{
				string payloadPath = WritePayload(root, "managed.bin", "changed");
				ModDeploymentTarget deploymentTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "managed.bin");
				InstallLogReadSnapshot install = CreateInstall(new[] { CreateMod("direct-a", ModInstallMethod.Direct) },
					new InstallLogReadFile[0], new[] { new InstallLogReadDeploymentTarget(deploymentTarget, new[] { "direct-a" }) });
				NativeStateCaptureSnapshot nativeState = CreateNativeState(install, new VirtualModReadLink[0],
					new[] { new NativeStateCaptureDeploymentTarget(deploymentTarget, payloadPath,
						new[] { new NativeStateCaptureDeploymentOwner("direct-a", NativeStateCaptureDeploymentOwnerKind.Direct, true, payloadPath) }) },
					NativeStateCaptureCoverage.Complete);
				CollectionsStore store;
				CollectionOwnerPayloadCaptureService service = CreateService(root, out store,
					new StubArchiveEvidenceSource(false));

				CollectionOwnerPayloadSnapshot snapshot = service.Capture(CollectionTargetIdentity.FromFingerprint("target-step2b-fallback"),
					LocalCaptureIdentity.From(Guid.Parse("10000000-0000-0000-0000-000000000011")), nativeState, CancellationToken.None);

				CollectionOwnerPayloadOwner owner = snapshot.Targets.Single().CurrentWinner;
				Assert.IsNotNull(owner.RetainedPayload);
				Assert.AreEqual(CollectionOwnerPayloadSourceKind.CapturedArtifact, owner.PayloadSource.Kind);
				Assert.AreEqual(1, new CollectionsRetainedArtifactReferenceStore(store)
					.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Capture, snapshot.CaptureIdentity.ToString()).Count);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		/// <summary>Production evidence proves both payloads with the same complete SHA-256 identity, including empty and multi-block files.</summary>
		[TestCase(0, ModInstallMethod.Direct)]
		[TestCase(33, ModInstallMethod.Virtual)]
		[TestCase(1048587, ModInstallMethod.Direct)]
		public void ArchiveEvidence_EqualPayloadsProduceTheExactReconstructionProof(int byteCount, ModInstallMethod method)
		{
			string root = CreateTemporaryDirectory("nmm-capture-evidence-equal-");
			try
			{
				byte[] bytes = CreateEvidenceBytes(byteCount);
				string entryPath = WriteEvidenceBytes(root, "entry.bin", bytes);
				string deployedPath = WriteEvidenceBytes(root, "deployed.bin", bytes);
				var evidence = CreateArchiveEvidence(root, () => new FileStream(entryPath, FileMode.Open, FileAccess.Read, FileShare.Read), method);
				NativeStateCaptureDeploymentOwnerKind kind = method == ModInstallMethod.Direct
					? NativeStateCaptureDeploymentOwnerKind.Direct : NativeStateCaptureDeploymentOwnerKind.Virtual;

				CollectionOwnerPayloadReconstructionCandidate candidate = evidence.CreateCandidate(kind, "direct-a",
					ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "managed.bin"), deployedPath, CancellationToken.None);

				Assert.IsNotNull(candidate);
				Assert.AreEqual(HashEvidenceBytes(bytes), candidate.ExpectedContentHash);
				Assert.AreEqual(candidate.ExpectedContentHash, candidate.ObservedContentHash);
				Assert.AreEqual(bytes.LongLength, candidate.ExpectedByteLength);
				Assert.AreEqual(bytes.LongLength, candidate.ObservedByteLength);
				Assert.AreEqual(CollectionOwnerPayloadArchiveReconstruction.CreateProofIdentity("direct-a",
					new ModInstallContext(method, ModInstallRoot.Default), "managed.bin", HashEvidenceBytes(bytes), bytes.LongLength),
					candidate.ReconstructionIdentity);
				Assert.AreEqual(CollectionOwnerPayloadSourceKind.ArchiveBacked,
					CollectionOwnerPayloadReconstructionClassifier.Classify(candidate).SourceKind);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		/// <summary>A changed file stops classification before reading the rest of a large archive entry.</summary>
		[TestCase(true)]
		[TestCase(false)]
		public void ArchiveEvidence_DifferingSizeOrFirstBlockStopsBeforeAFullRead(bool differentSize)
		{
			string root = CreateTemporaryDirectory("nmm-capture-evidence-mismatch-");
			try
			{
				byte[] archiveBytes = CreateEvidenceBytes(2097163);
				byte[] deployedBytes = differentSize ? CreateEvidenceBytes(archiveBytes.Length - 1) : (byte[])archiveBytes.Clone();
				if (!differentSize) deployedBytes[0] ^= 1;
				string entryPath = WriteEvidenceBytes(root, "entry.bin", archiveBytes);
				string deployedPath = WriteEvidenceBytes(root, "deployed.bin", deployedBytes);
				EvidenceFileStream opened = null;
				var evidence = CreateArchiveEvidence(root, () => opened = new EvidenceFileStream(entryPath));

				CollectionOwnerPayloadReconstructionCandidate candidate = evidence.CreateCandidate(NativeStateCaptureDeploymentOwnerKind.Direct,
					"direct-a", ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "managed.bin"), deployedPath, CancellationToken.None);

				Assert.IsNull(candidate);
				Assert.IsNotNull(opened);
				Assert.AreEqual(differentSize ? 0 : 1048576, opened.BytesRead);
				Assert.IsTrue(opened.WasDisposed);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		/// <summary>Short stream reads and reused buffers cannot change the decision for a later, smaller payload.</summary>
		[Test]
		public void ArchiveEvidence_ShortReadsAndReusedBuffersPreserveExactIdentity()
		{
			string root = CreateTemporaryDirectory("nmm-capture-evidence-short-read-");
			try
			{
				string entryPath = Path.Combine(root, "entry.bin");
				string deployedPath = Path.Combine(root, "deployed.bin");
				var evidence = CreateArchiveEvidence(root, () => new EvidenceFileStream(entryPath, 17));
				ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "managed.bin");
				foreach (int byteCount in new[] { 16391, 33 })
				{
					byte[] bytes = CreateEvidenceBytes(byteCount);
					File.WriteAllBytes(entryPath, bytes);
					File.WriteAllBytes(deployedPath, bytes);

					CollectionOwnerPayloadReconstructionCandidate candidate = evidence.CreateCandidate(NativeStateCaptureDeploymentOwnerKind.Direct,
						"direct-a", target, deployedPath, CancellationToken.None);

					Assert.IsNotNull(candidate);
					Assert.AreEqual(HashEvidenceBytes(bytes), candidate.ExpectedContentHash);
					Assert.AreEqual(bytes.LongLength, candidate.ObservedByteLength);
				}
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		/// <summary>Cancellation during comparison must escape classification and release the source handle.</summary>
		[Test]
		public void ArchiveEvidence_CancellationDuringReadReleasesTheArchiveStream()
		{
			string root = CreateTemporaryDirectory("nmm-capture-evidence-cancel-");
			try
			{
				byte[] bytes = CreateEvidenceBytes(4096);
				string entryPath = WriteEvidenceBytes(root, "entry.bin", bytes);
				string deployedPath = WriteEvidenceBytes(root, "deployed.bin", bytes);
				EvidenceFileStream opened = null;
				using (var cancellation = new CancellationTokenSource())
				{
					var evidence = CreateArchiveEvidence(root, () => opened = new EvidenceFileStream(entryPath, 17, cancellation.Cancel));

					Assert.Throws<OperationCanceledException>(() => evidence.CreateCandidate(NativeStateCaptureDeploymentOwnerKind.Direct,
						"direct-a", ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "managed.bin"), deployedPath, cancellation.Token));
					Assert.IsNotNull(opened);
					Assert.IsTrue(opened.WasDisposed);
				}
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		/// <summary>The production comparison must retain all current bytes when a difference occurs even at the end of a large file.</summary>
		[Test]
		public void Capture_ProductionEvidenceRetainsAChangedFinalBlockExactly()
		{
			string root = CreateTemporaryDirectory("nmm-capture-evidence-retention-");
			try
			{
				byte[] archiveBytes = CreateEvidenceBytes(1048587);
				byte[] deployedBytes = (byte[])archiveBytes.Clone();
				deployedBytes[deployedBytes.Length - 1] ^= 1;
				string entryPath = WriteEvidenceBytes(root, "entry.bin", archiveBytes);
				string deployedPath = WriteEvidenceBytes(root, "deployed.bin", deployedBytes);
				var evidence = CreateArchiveEvidence(root, () => new FileStream(entryPath, FileMode.Open, FileAccess.Read, FileShare.Read));
				ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "managed.bin");
				InstallLogReadSnapshot install = CreateInstall(new[] { CreateMod("direct-a", ModInstallMethod.Direct, ModInstallRoot.Default) },
					new InstallLogReadFile[0], new[] { new InstallLogReadDeploymentTarget(target, new[] { "direct-a" }) });
				NativeStateCaptureSnapshot native = CreateNativeState(install, new VirtualModReadLink[0], new[]
				{
					new NativeStateCaptureDeploymentTarget(target, deployedPath, new[]
					{
						new NativeStateCaptureDeploymentOwner("direct-a", NativeStateCaptureDeploymentOwnerKind.Direct, true, deployedPath)
					})
				}, NativeStateCaptureCoverage.Complete);
				CollectionsStore store;
				CollectionOwnerPayloadCaptureService service = CreateService(root, out store, evidence);

				CollectionOwnerPayloadSnapshot snapshot = service.Capture(CollectionTargetIdentity.FromFingerprint("target-capture-evidence"),
					LocalCaptureIdentity.From(Guid.NewGuid()), native, CancellationToken.None);

				Assert.AreEqual(NativeStateCaptureCoverage.Complete, snapshot.Coverage);
				CollectionOwnerPayloadSource source = snapshot.Targets.Single().CurrentWinner.PayloadSource;
				Assert.AreEqual(CollectionOwnerPayloadSourceKind.CapturedArtifact, source.Kind);
				Assert.AreEqual(HashEvidenceBytes(deployedBytes), source.ExpectedContentHash);
				var artifacts = new CollectionsRetainedArtifactStore(store);
				Assert.IsTrue(artifacts.VerifyArtifact(source.CapturedArtifact.StableArtifactId));
				using (Stream retained = artifacts.OpenRead(source.CapturedArtifact.StableArtifactId))
				using (var copy = new MemoryStream())
				{
					retained.CopyTo(copy);
					CollectionAssert.AreEqual(deployedBytes, copy.ToArray());
				}
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static InstallLogReadSnapshot CreateInstall(IEnumerable<InstallLogReadMod> mods,
			IEnumerable<InstallLogReadFile> files, IEnumerable<InstallLogReadDeploymentTarget> deploymentTargets)
		{
			return new InstallLogReadSnapshot("original-values", 23, mods, files,
				new InstallLogReadIniEdit[0], new InstallLogReadGameValue[0], deploymentTargets);
		}

		private static InstallLogReadMod CreateMod(string key, ModInstallMethod method, ModInstallRoot installRoot = ModInstallRoot.Data)
		{
			return new InstallLogReadMod(key, key + ".zip", key + ".zip", "1", "1", "1", "1", false,
				installRoot, method, false);
		}

		private static NativeStateCaptureSnapshot CreateNativeState(InstallLogReadSnapshot install,
			IEnumerable<VirtualModReadLink> links, IEnumerable<NativeStateCaptureDeploymentTarget> deployments,
			NativeStateCaptureCoverage deploymentCoverage,
			IEnumerable<NativeStateCaptureVirtualPayloadSource> virtualPayloadSources = null,
			IEnumerable<NativeStateCaptureVirtualFallback> virtualFallbacks = null)
		{
			return new NativeStateCaptureSnapshot(install, new VirtualModReadSnapshot(links),
				virtualPayloadSources ?? new NativeStateCaptureVirtualPayloadSource[0],
				virtualFallbacks ?? new NativeStateCaptureVirtualFallback[0],
				new NativeStateCaptureRoot[0], deployments, deploymentCoverage,
				new NativeStateCaptureReplayReference[0], new NativeStateCapturePlugin[0],
				NativeStateCaptureCoverage.NotApplicable, new NativeStateCaptureIssue[0]);
		}

		private static CollectionOwnerPayloadCaptureService CreateService(string root, out CollectionsStore store)
		{
			return CreateService(root, out store, null);
		}

		private static CollectionOwnerPayloadCaptureService CreateService(string root, out CollectionsStore store,
			ICollectionOwnerPayloadArchiveEvidenceSource archiveEvidenceSource)
		{
			store = new CollectionsStore(Path.Combine(root, "Collections"));
			store.CreateNew();
			IInstallLog installLog = InterfaceStub<IInstallLog>.Create((method, args) => null);
			IVirtualModActivator virtualModActivator = InterfaceStub<IVirtualModActivator>.Create((method, args) => null);
			IGameMode gameMode = InterfaceStub<IGameMode>.Create((method, args) => null);
			var nativeReader = new NativeStateCaptureReader(installLog, virtualModActivator, null, null, gameMode);
			return new CollectionOwnerPayloadCaptureService(nativeReader,
				new CollectionsRetainedArtifactStore(store), new CollectionsRetainedArtifactReferenceStore(store), archiveEvidenceSource);
		}

		/// <summary>Composes the real archive evidence reader using an isolated registered-mod stub.</summary>
		private static CollectionOwnerPayloadArchiveEvidenceSource CreateArchiveEvidence(string root, Func<FileStream> openEntry,
			ModInstallMethod method = ModInstallMethod.Direct)
		{
			string archivePath = WriteEvidenceBytes(root, "mod.zip", Encoding.ASCII.GetBytes("capture source archive"));
			IMod mod = InterfaceStub<IMod>.Create((call, args) =>
			{
				if (call.Name == "GetFileList") return new List<string> { "managed.bin" };
				if (call.Name == "GetFileStream") return openEntry();
				return null;
			});
			var active = new ReadOnlyObservableList<IMod>(new ThreadSafeObservableList<IMod>(new[] { mod }));
			IInstallLog installLog = InterfaceStub<IInstallLog>.Create((call, args) =>
			{
				if (call.Name == "get_ActiveMods") return active;
				if (call.Name == "GetModKey") return "direct-a";
				return null;
			});
			var installed = new CollectionInstalledModIdentity("direct-a",
				new CollectionInstalledArchiveReference(archivePath, "mod.zip", true, null), "1", "1", "1", "1", false,
				new ModInstallContext(method, ModInstallRoot.Default), new CollectionInstalledMemberProvenance[0]);
			var identities = new CollectionInstalledIdentitySnapshot(CollectionTargetIdentity.FromFingerprint("target-capture-evidence"),
				23, new[] { installed }, NativeStateCaptureCoverage.Complete, new CollectionInstalledIdentityIssue[0]);
			IGameMode gameMode = InterfaceStub<IGameMode>.Create((call, args) => null);
			return new CollectionOwnerPayloadArchiveEvidenceSource(installLog, identities, gameMode);
		}

		/// <summary>Creates deterministic payloads for multi-block capture comparison.</summary>
		private static byte[] CreateEvidenceBytes(int byteCount)
		{
			var bytes = new byte[byteCount];
			for (int index = 0; index < bytes.Length; index++) bytes[index] = (byte)(index * 17 + 3);
			return bytes;
		}

		/// <summary>Writes only isolated test payloads, preserving their exact bytes.</summary>
		private static string WriteEvidenceBytes(string root, string name, byte[] bytes)
		{
			string path = Path.Combine(root, name);
			File.WriteAllBytes(path, bytes);
			return path;
		}

		/// <summary>Calculates the independent expected digest used by capture evidence assertions.</summary>
		private static CollectionContentHash HashEvidenceBytes(byte[] bytes)
		{
			using (SHA256 sha256 = SHA256.Create())
				return CollectionContentHash.FromSha256(BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", String.Empty).ToLowerInvariant());
		}

		/// <summary>Counts archive-entry reads and can simulate short reads or cancellation without a native archive dependency.</summary>
		private sealed class EvidenceFileStream : FileStream
		{
			private readonly int _maximumRead;
			private readonly Action _afterRead;

			/// <summary>Opens one isolated archive entry with configurable read behavior.</summary>
			internal EvidenceFileStream(string path, int maximumRead = Int32.MaxValue, Action afterRead = null)
				: base(path, FileMode.Open, FileAccess.Read, FileShare.Read)
			{
				_maximumRead = maximumRead;
				_afterRead = afterRead;
			}

			internal long BytesRead { get; private set; }
			internal bool WasDisposed { get; private set; }

			/// <summary>Reads a bounded chunk and records the physical bytes requested by classification.</summary>
			public override int Read(byte[] buffer, int offset, int count)
			{
				int read = base.Read(buffer, offset, Math.Min(count, _maximumRead));
				BytesRead += read;
				_afterRead?.Invoke();
				return read;
			}

			/// <summary>Records release of the archive entry after success, mismatch, failure, or cancellation.</summary>
			protected override void Dispose(bool disposing)
			{
				WasDisposed = true;
				base.Dispose(disposing);
			}
		}

		private sealed class StubArchiveEvidenceSource : ICollectionOwnerPayloadArchiveEvidenceSource
		{
			private readonly bool _matches;

			internal StubArchiveEvidenceSource(bool matches)
			{
				_matches = matches;
			}

			public CollectionOwnerPayloadReconstructionCandidate CreateCandidate(NativeStateCaptureDeploymentOwnerKind ownerKind,
				string ownerKey, ModDeploymentTarget target, string payloadSourcePath, CancellationToken cancellationToken)
			{
				CollectionContentHash expected = CollectionContentHash.FromSha256(new string('a', 64));
				CollectionContentHash observed = _matches ? expected : CollectionContentHash.FromSha256(new string('b', 64));
				return new CollectionOwnerPayloadReconstructionCandidate(ownerKind,
					CollectionOwnerPayloadReconstructionMappingKind.ExactArchiveEntry, true, false, ownerKey,
					target.RelativePath, "step2b-test-proof", expected, 7, observed, 7);
			}
		}

		private static string CreateTemporaryDirectory(string prefix)
		{
			string path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}

		private static string WritePayload(string root, string fileName, string content)
		{
			string path = Path.Combine(root, fileName);
			File.WriteAllText(path, content, Encoding.UTF8);
			return path;
		}

		private static void AssertRetainedText(CollectionsStore store, CollectionOwnerPayloadRetention retained, string expected)
		{
			var artifacts = new CollectionsRetainedArtifactStore(store);
			Assert.IsTrue(artifacts.VerifyArtifact(retained.StableArtifactId));
			using (var reader = new StreamReader(artifacts.OpenRead(retained.StableArtifactId), Encoding.UTF8))
				Assert.AreEqual(expected, reader.ReadToEnd());
		}
	}
}
