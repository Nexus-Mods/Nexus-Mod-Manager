using System;
using System.IO;
using System.Linq;
using Nexus.Transactions;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// C7.1 generic read-only native capture characterization.
	/// </summary>
	public class NativeStateCaptureReaderTests
	{
		[Test]
		public void Capture_ProducesDeterministicDeploymentReferencesAndPreservesPromotedOwnerOrder()
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-c71-" + Guid.NewGuid().ToString("N"));
			string installInfo = Path.Combine(root, "InstallInfo");
			ModDeploymentTarget targetA = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "meshes\\a.bin");
			ModDeploymentTarget targetB = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\b.bin");
			InstallLogReadSnapshot install = new InstallLogReadSnapshot("original-values", 41,
				new[]
				{
					new InstallLogReadMod("virtual-b", "b.zip", "b.zip", "2", "20", "1", "1", false,
						ModInstallRoot.Data, ModInstallMethod.Virtual, false),
					new InstallLogReadMod("direct-a", "a.zip", "a.zip", "1", "10", "1", "1", false,
						ModInstallRoot.Data, ModInstallMethod.Direct, false)
				},
				new[]
				{
					new InstallLogReadFile("textures\\b.bin", targetB, new[] { "direct-a" }),
					new InstallLogReadFile("meshes\\a.bin", targetA, new[] { "virtual-b" })
				},
				new InstallLogReadIniEdit[0], new InstallLogReadGameValue[0],
				new[]
				{
					new InstallLogReadDeploymentTarget(targetB, new[] { "direct-a" }),
					new InstallLogReadDeploymentTarget(targetA, new[] { "original-values", "direct-a", "virtual-b" })
				});
			VirtualModReadSnapshot virtualState = new VirtualModReadSnapshot(new[]
			{
				new VirtualModReadLink(targetB, "virtual-b", "b", true, 0, "relative-b.bin", Path.Combine(root, "virtual-b.bin")),
				new VirtualModReadLink(targetA, "virtual-b", "a", true, 0, "relative-a.bin", Path.Combine(root, "virtual-a.bin"))
			});

			NativeStateCaptureSnapshot snapshot = CreateReader(root, installInfo, install, virtualState,
				CreateDeploymentManager(root)).Capture();

			Assert.AreSame(install, snapshot.InstallLog);
			Assert.AreSame(virtualState, snapshot.VirtualState);
			Assert.AreEqual(2, snapshot.ActiveVirtualPayloadSources.Count);
			CollectionAssert.AreEqual(new[] { targetA, targetB }, snapshot.ActiveVirtualPayloadSources.Select(x => x.Target).ToArray());
			Assert.AreEqual(Path.Combine(root, "virtual-a.bin"), snapshot.ActiveVirtualPayloadSources[0].PayloadSourcePath);
			CollectionAssert.AreEqual(new[] { targetA, targetB }, snapshot.DeploymentTargets.Select(x => x.Target).ToArray());
			Assert.AreEqual(NativeStateCaptureCoverage.Complete, snapshot.DeploymentCoverage);

			NativeStateCaptureDeploymentTarget promotedA = snapshot.DeploymentTargets[0];
			CollectionAssert.AreEqual(new[] { "original-values", "direct-a", "virtual-b" }, promotedA.Owners.Select(x => x.OwnerKey).ToArray());
			Assert.AreEqual("virtual-b", promotedA.CurrentOwnerKey);
			Assert.AreEqual(NativeStateCaptureDeploymentOwnerKind.OriginalValue, promotedA.Owners[0].Kind);
			Assert.AreEqual(NativeStateCaptureDeploymentOwnerKind.Direct, promotedA.Owners[1].Kind);
			Assert.AreEqual(NativeStateCaptureDeploymentOwnerKind.Virtual, promotedA.Owners[2].Kind);
			Assert.IsFalse(promotedA.Owners[1].CurrentWinner);
			Assert.IsTrue(promotedA.Owners[2].CurrentWinner);
			StringAssert.Contains("backup-original-values", promotedA.Owners[0].PayloadSourcePath);
			StringAssert.Contains("backup-direct-a", promotedA.Owners[1].PayloadSourcePath);
			StringAssert.Contains("virtual-virtual-b", promotedA.Owners[2].PayloadSourcePath);

			NativeStateCaptureDeploymentTarget promotedB = snapshot.DeploymentTargets[1];
			Assert.AreEqual(promotedB.DeploymentPath, promotedB.Owners.Single().PayloadSourcePath);
		}

		[Test]
		public void Capture_ScriptedReplayReferencesDoNotCopyOrCreateArtifacts()
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-c71-replay-" + Guid.NewGuid().ToString("N"));
			string installInfo = Path.Combine(root, "InstallInfo");
			InstallLogReadSnapshot install = new InstallLogReadSnapshot("original-values", 0,
				new[]
				{
					new InstallLogReadMod("scripted", "scripted.zip", "scripted.zip", "1", "2", "1", "1", true,
						ModInstallRoot.Data, ModInstallMethod.Virtual, false)
				},
				new InstallLogReadFile[0], new InstallLogReadIniEdit[0], new InstallLogReadGameValue[0],
				new InstallLogReadDeploymentTarget[0]);

			NativeStateCaptureSnapshot snapshot = CreateReader(root, installInfo, install,
				new VirtualModReadSnapshot(new VirtualModReadLink[0]), null).Capture();

			Assert.AreEqual(1, snapshot.ReplayReferences.Count);
			NativeStateCaptureReplayReference replay = snapshot.ReplayReferences[0];
			Assert.AreEqual("scripted", replay.ModKey);
			Assert.AreEqual(Path.Combine(installInfo, "Scripted", "scripted.xml"), replay.CachePath);
			Assert.AreEqual(Path.Combine(installInfo, "Scripted", "scripted.payload"), replay.PayloadDirectoryPath);
			Assert.IsFalse(File.Exists(replay.CachePath));
			Assert.IsFalse(Directory.Exists(replay.PayloadDirectoryPath));
			Assert.AreEqual(NativeStateCaptureCoverage.NotApplicable, snapshot.DeploymentCoverage);
		}

		[Test]
		public void Capture_WithoutDeploymentManagerLeavesTopologyInInstallLogAndMarksReferencesUnavailable()
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-c71-partial-" + Guid.NewGuid().ToString("N"));
			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "file.bin");
			InstallLogReadSnapshot install = new InstallLogReadSnapshot("original-values", 5,
				new[]
				{
					new InstallLogReadMod("direct-a", "a.zip", "a.zip", "1", "2", "1", "1", false,
						ModInstallRoot.Data, ModInstallMethod.Direct, false)
				},
				new InstallLogReadFile[0], new InstallLogReadIniEdit[0], new InstallLogReadGameValue[0],
				new[] { new InstallLogReadDeploymentTarget(target, new[] { "original-values", "direct-a" }) });

			NativeStateCaptureSnapshot snapshot = CreateReader(root, Path.Combine(root, "InstallInfo"), install,
				new VirtualModReadSnapshot(new VirtualModReadLink[0]), null).Capture();

			Assert.AreEqual(NativeStateCaptureCoverage.Unavailable, snapshot.DeploymentCoverage);
			Assert.AreEqual(0, snapshot.DeploymentTargets.Count);
			CollectionAssert.AreEqual(new[] { "original-values", "direct-a" }, snapshot.InstallLog.DeploymentTargets[0].OwnerKeys.ToArray());
			Assert.IsTrue(snapshot.Issues.Any(x => x.Kind == NativeStateCaptureIssueKind.DeploymentStateUnavailable));
		}

		[Test]
		public void Capture_RejectsAmbientNativeTransaction()
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-c71-tx-" + Guid.NewGuid().ToString("N"));
			InstallLogReadSnapshot install = new InstallLogReadSnapshot("original-values", 0,
				new InstallLogReadMod[0], new InstallLogReadFile[0], new InstallLogReadIniEdit[0],
				new InstallLogReadGameValue[0], new InstallLogReadDeploymentTarget[0]);
			NativeStateCaptureReader reader = CreateReader(root, Path.Combine(root, "InstallInfo"), install,
				new VirtualModReadSnapshot(new VirtualModReadLink[0]), null);

			using (var transaction = new TransactionScope())
			{
				InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => reader.Capture());
				StringAssert.Contains("ambient native transaction", error.Message);
			}
		}

		private static NativeStateCaptureReader CreateReader(string root, string installInfo,
			InstallLogReadSnapshot install, VirtualModReadSnapshot virtualState, IModDeploymentManager deploymentManager)
		{
			IInstallLog installLog = InterfaceStub<IInstallLog>.Create((method, args) =>
				method.Name == "GetCommittedStateSnapshot" ? install : null);
			IVirtualModActivator virtualModActivator = InterfaceStub<IVirtualModActivator>.Create((method, args) =>
				method.Name == "GetReadSnapshot" ? virtualState : null);
			IGameModeEnvironmentInfo environment = InterfaceStub<IGameModeEnvironmentInfo>.Create((method, args) =>
			{
				if (method.Name == "get_InstallInfoDirectory") return installInfo;
				if (method.Name == "get_InstallationPath") return root;
				return null;
			});
			IGameMode gameMode = InterfaceStub<IGameMode>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_InstallationPath": return root;
					case "get_PluginDirectory": return root;
					case "get_SecondaryInstallationPath": return null;
					case "get_HasSecondaryInstallPath": return false;
					case "get_UsesPlugins": return false;
					case "get_GameModeEnvironmentInfo": return environment;
					default: return null;
				}
			});
			return new NativeStateCaptureReader(installLog, virtualModActivator, deploymentManager, null, gameMode);
		}

		private static IModDeploymentManager CreateDeploymentManager(string root)
		{
			return InterfaceStub<IModDeploymentManager>.Create((method, args) =>
			{
				ModDeploymentTarget target = args != null && args.Length > 0 ? args[0] as ModDeploymentTarget : null;
				string ownerKey = args != null && args.Length > 1 ? args[1] as string : null;
				switch (method.Name)
				{
					case "GetDeploymentPath":
						return Path.Combine(root, (target == null ? String.Empty : target.RelativePath).Replace('\\', Path.DirectorySeparatorChar));
					case "GetOwnerBackupPath":
						return Path.Combine(root, "backup-" + ownerKey + "-" + Path.GetFileName(target.RelativePath));
					case "GetOwnerSourcePath":
						return Path.Combine(root, "virtual-" + ownerKey + "-" + Path.GetFileName(target.RelativePath));
					default:
						return null;
				}
			});
		}
	}
}
