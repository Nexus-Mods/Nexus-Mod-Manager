using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// C7.3 owner/fallback payload retention characterization.
	/// </summary>
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

		private static InstallLogReadSnapshot CreateInstall(IEnumerable<InstallLogReadMod> mods,
			IEnumerable<InstallLogReadFile> files, IEnumerable<InstallLogReadDeploymentTarget> deploymentTargets)
		{
			return new InstallLogReadSnapshot("original-values", 23, mods, files,
				new InstallLogReadIniEdit[0], new InstallLogReadGameValue[0], deploymentTargets);
		}

		private static InstallLogReadMod CreateMod(string key, ModInstallMethod method)
		{
			return new InstallLogReadMod(key, key + ".zip", key + ".zip", "1", "1", "1", "1", false,
				ModInstallRoot.Data, method, false);
		}

		private static NativeStateCaptureSnapshot CreateNativeState(InstallLogReadSnapshot install,
			IEnumerable<VirtualModReadLink> links, IEnumerable<NativeStateCaptureDeploymentTarget> deployments,
			NativeStateCaptureCoverage deploymentCoverage,
			IEnumerable<NativeStateCaptureVirtualPayloadSource> virtualPayloadSources = null)
		{
			return new NativeStateCaptureSnapshot(install, new VirtualModReadSnapshot(links),
				virtualPayloadSources ?? new NativeStateCaptureVirtualPayloadSource[0],
				new NativeStateCaptureRoot[0], deployments, deploymentCoverage,
				new NativeStateCaptureReplayReference[0], new NativeStateCapturePlugin[0],
				NativeStateCaptureCoverage.NotApplicable, new NativeStateCaptureIssue[0]);
		}

		private static CollectionOwnerPayloadCaptureService CreateService(string root, out CollectionsStore store)
		{
			store = new CollectionsStore(Path.Combine(root, "Collections"));
			store.CreateNew();
			IInstallLog installLog = InterfaceStub<IInstallLog>.Create((method, args) => null);
			IVirtualModActivator virtualModActivator = InterfaceStub<IVirtualModActivator>.Create((method, args) => null);
			IGameMode gameMode = InterfaceStub<IGameMode>.Create((method, args) => null);
			var nativeReader = new NativeStateCaptureReader(installLog, virtualModActivator, null, null, gameMode);
			return new CollectionOwnerPayloadCaptureService(nativeReader,
				new CollectionsRetainedArtifactStore(store), new CollectionsRetainedArtifactReferenceStore(store));
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
