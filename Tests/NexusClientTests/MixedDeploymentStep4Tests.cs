namespace NexusClientTests
{
	using ChinhDo.Transactions;
	using Microsoft.VisualStudio.TestPlatform.PlatformAbstractions.Interfaces;
	using Nexus.Client.Games;
	using Nexus.Client.ModManagement;
	using Nexus.Client.ModManagement.InstallationLog;
	using Nexus.Client.Mods;
	using Nexus.Transactions;
	using NUnit.Framework;
	using System;
	using System.Collections.Generic;
	using System.ComponentModel;
	using System.IO;
	using System.Linq;
	using System.Reflection;
	using System.Text;
	using System.Xml.Linq;

	/// <summary>
	/// Verifies mixed Virtual/Direct ownership, restoration, and rollback introduced by Step 4.
	/// </summary>
	[TestFixture]
	public class MixedDeploymentStep4Tests
	{
		[Test]
		public void VirtualToDirect_PromotesEffectiveVirtualOrderAndPreservesSources()
		{
			using (var environment = new MixedTestEnvironment())
			{
				IMod virtualFallback = environment.RegisterMod("VirtualFallback", ModInstallMethod.Virtual);
				IMod virtualWinner = environment.RegisterMod("VirtualWinner", ModInstallMethod.Virtual);
				IMod directWinner = environment.RegisterMod("DirectWinner", ModInstallMethod.Direct);
				ModDeploymentTarget target = environment.Target(@"textures\mixed.dds");
				string fallbackSource = environment.AddVirtualOwner(virtualFallback, target, "fallback", false, 2);
				string winnerSource = environment.AddVirtualOwner(virtualWinner, target, "virtual", true, 0);

				environment.InstallDirect(directWinner, target, "direct");

				CollectionAssert.AreEqual(
					new[]
					{
						environment.Key(virtualFallback),
						environment.Key(virtualWinner),
						environment.Key(directWinner)
					},
					environment.InstallLog.GetDeploymentOwnerKeys(target));
				Assert.AreEqual("direct", environment.ReadTarget(target));
				Assert.AreEqual("fallback", File.ReadAllText(fallbackSource));
				Assert.AreEqual("virtual", File.ReadAllText(winnerSource));
			}
		}

		[Test]
		public void LinkActivationTask_DisableAndReactivatePromotedVirtualWinner_UsesCoordinator()
		{
			using (var environment = new MixedTestEnvironment())
			{
				IMod firstVirtual = environment.RegisterMod("FirstVirtual", ModInstallMethod.Virtual);
				IMod direct = environment.RegisterMod("Direct", ModInstallMethod.Direct);
				IMod lastVirtual = environment.RegisterMod("LastVirtual", ModInstallMethod.Virtual);
				ModDeploymentTarget target = environment.Target(@"meshes\activation.nif");
				environment.AddVirtualOwner(firstVirtual, target, "first", true, 0);
				environment.InstallDirect(direct, target, "direct");
				string lastSource = environment.StageVirtual(lastVirtual, target, "last");
				environment.InstallVirtual(lastVirtual, target, lastSource, true);

				new SynchronousLinkActivationTask(environment, lastVirtual).Execute();

				Assert.AreEqual("direct", environment.ReadTarget(target));
				CollectionAssert.AreEqual(
					new[] { environment.Key(firstVirtual), environment.Key(direct) },
					environment.InstallLog.GetDeploymentOwnerKeys(target));
				Assert.AreEqual(ModInstallMethod.Virtual, environment.InstallLog.GetModInstallMethod(lastVirtual));

				var deploymentService = new VirtualDeploymentService(environment.VirtualState.Activator, environment.Manager);
				VirtualDeploymentResult result = deploymentService.ActivateModLinks(lastVirtual, new VirtualDeploymentOptions
				{
					InstallRoot = ModInstallRoot.Data
				});

				Assert.IsNull(result.Failure);
				Assert.AreEqual(1, result.LinkedFileCount);
				Assert.AreEqual("last", environment.ReadTarget(target));
				CollectionAssert.AreEqual(
					new[] { environment.Key(firstVirtual), environment.Key(direct), environment.Key(lastVirtual) },
					environment.InstallLog.GetDeploymentOwnerKeys(target));
			}
		}

		[Test]
		public void LinkActivationTask_DisablingInactivePromotedVirtualOwner_LeavesWinnerUntouched()
		{
			using (var environment = new MixedTestEnvironment())
			{
				IMod firstVirtual = environment.RegisterMod("FirstVirtual", ModInstallMethod.Virtual);
				IMod direct = environment.RegisterMod("Direct", ModInstallMethod.Direct);
				IMod lastVirtual = environment.RegisterMod("LastVirtual", ModInstallMethod.Virtual);
				ModDeploymentTarget target = environment.Target(@"scripts\inactive.pex");
				environment.AddVirtualOwner(firstVirtual, target, "first", true, 0);
				environment.InstallDirect(direct, target, "direct");
				string lastSource = environment.StageVirtual(lastVirtual, target, "last");
				environment.InstallVirtual(lastVirtual, target, lastSource, true);

				new SynchronousLinkActivationTask(environment, firstVirtual).Execute();

				Assert.AreEqual("last", environment.ReadTarget(target));
				CollectionAssert.AreEqual(
					new[] { environment.Key(direct), environment.Key(lastVirtual) },
					environment.InstallLog.GetDeploymentOwnerKeys(target));
			}
		}

		[Test]
		public void DirectToVirtual_UninstallVirtualWinnerRestoresDirectOwner()
		{
			using (var environment = new MixedTestEnvironment())
			{
				IMod direct = environment.RegisterMod("Direct", ModInstallMethod.Direct);
				IMod virtualMod = environment.RegisterMod("Virtual", ModInstallMethod.Virtual);
				ModDeploymentTarget target = environment.Target(@"meshes\mixed.nif");
				environment.InstallDirect(direct, target, "direct");
				string virtualSource = environment.StageVirtual(virtualMod, target, "virtual");

				environment.InstallVirtual(virtualMod, target, virtualSource, true);
				Assert.AreEqual("virtual", environment.ReadTarget(target));

				environment.Uninstall(virtualMod);

				Assert.AreEqual("direct", environment.ReadTarget(target));
				CollectionAssert.AreEqual(
					new[] { environment.Key(direct) },
					environment.InstallLog.GetDeploymentOwnerKeys(target));
			}
		}

		[Test]
		public void OriginalDirectVirtual_UninstallChainEventuallyRestoresOriginal()
		{
			using (var environment = new MixedTestEnvironment())
			{
				IMod direct = environment.RegisterMod("Direct", ModInstallMethod.Direct);
				IMod virtualMod = environment.RegisterMod("Virtual", ModInstallMethod.Virtual);
				ModDeploymentTarget target = environment.Target(@"config\original-chain.ini");
				string targetPath = environment.Manager.GetDeploymentPath(target);
				Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
				File.WriteAllText(targetPath, "original");
				environment.InstallDirect(direct, target, "direct");
				string virtualSource = environment.StageVirtual(virtualMod, target, "virtual");
				environment.InstallVirtual(virtualMod, target, virtualSource, true);

				environment.Uninstall(virtualMod);
				Assert.AreEqual("direct", environment.ReadTarget(target));
				environment.Uninstall(direct);

				Assert.AreEqual("original", environment.ReadTarget(target));
				Assert.IsFalse(environment.InstallLog.IsDeploymentTargetPromoted(target));
			}
		}

		[Test]
		public void VirtualDirectVirtual_RemovingInactiveOwnersLeavesPhysicalWinnerUntouched()
		{
			using (var environment = new MixedTestEnvironment())
			{
				IMod firstVirtual = environment.RegisterMod("FirstVirtual", ModInstallMethod.Virtual);
				IMod direct = environment.RegisterMod("Direct", ModInstallMethod.Direct);
				IMod lastVirtual = environment.RegisterMod("LastVirtual", ModInstallMethod.Virtual);
				ModDeploymentTarget target = environment.Target(@"scripts\winner.pex");
				environment.AddVirtualOwner(firstVirtual, target, "first", true, 0);
				environment.InstallDirect(direct, target, "direct");
				string lastSource = environment.StageVirtual(lastVirtual, target, "last");
				environment.InstallVirtual(lastVirtual, target, lastSource, true);

				environment.Uninstall(direct);
				Assert.AreEqual("last", environment.ReadTarget(target));
				CollectionAssert.AreEqual(
					new[] { environment.Key(firstVirtual), environment.Key(lastVirtual) },
					environment.InstallLog.GetDeploymentOwnerKeys(target));

				environment.Uninstall(firstVirtual);
				Assert.AreEqual("last", environment.ReadTarget(target));
				CollectionAssert.AreEqual(
					new[] { environment.Key(lastVirtual) },
					environment.InstallLog.GetDeploymentOwnerKeys(target));
				Assert.AreEqual(0, environment.BackupFiles.Length);
			}
		}

		[Test]
		public void VirtualDirectVirtual_RemovingWinnerRestoresEachMixedFallback()
		{
			using (var environment = new MixedTestEnvironment())
			{
				IMod firstVirtual = environment.RegisterMod("FirstVirtual", ModInstallMethod.Virtual);
				IMod direct = environment.RegisterMod("Direct", ModInstallMethod.Direct);
				IMod lastVirtual = environment.RegisterMod("LastVirtual", ModInstallMethod.Virtual);
				ModDeploymentTarget target = environment.Target(@"meshes\restore.nif");
				environment.AddVirtualOwner(firstVirtual, target, "first", true, 0);
				environment.InstallDirect(direct, target, "direct");
				string lastSource = environment.StageVirtual(lastVirtual, target, "last");
				environment.InstallVirtual(lastVirtual, target, lastSource, true);

				environment.Uninstall(lastVirtual);
				Assert.AreEqual("direct", environment.ReadTarget(target));

				environment.Uninstall(direct);
				Assert.AreEqual("first", environment.ReadTarget(target));
				CollectionAssert.AreEqual(
					new[] { environment.Key(firstVirtual) },
					environment.InstallLog.GetDeploymentOwnerKeys(target));
			}
		}

		[Test]
		public void DirectVirtualDirect_RemovingWinnerRestoresEachMixedFallback()
		{
			using (var environment = new MixedTestEnvironment())
			{
				IMod firstDirect = environment.RegisterMod("FirstDirect", ModInstallMethod.Direct);
				IMod virtualMod = environment.RegisterMod("Virtual", ModInstallMethod.Virtual);
				IMod lastDirect = environment.RegisterMod("LastDirect", ModInstallMethod.Direct);
				ModDeploymentTarget target = environment.Target(@"scripts\restore.pex");
				environment.InstallDirect(firstDirect, target, "first");
				string virtualSource = environment.StageVirtual(virtualMod, target, "virtual");
				environment.InstallVirtual(virtualMod, target, virtualSource, true);
				environment.InstallDirect(lastDirect, target, "last");

				environment.Uninstall(lastDirect);
				Assert.AreEqual("virtual", environment.ReadTarget(target));

				environment.Uninstall(virtualMod);
				Assert.AreEqual("first", environment.ReadTarget(target));
				CollectionAssert.AreEqual(
					new[] { environment.Key(firstDirect) },
					environment.InstallLog.GetDeploymentOwnerKeys(target));
			}
		}

		[Test]
		public void UninstallCurrentDirectWinner_RestoresVirtualAndKeepsPromotionSticky()
		{
			using (var environment = new MixedTestEnvironment())
			{
				IMod virtualMod = environment.RegisterMod("Virtual", ModInstallMethod.Virtual);
				IMod direct = environment.RegisterMod("Direct", ModInstallMethod.Direct);
				ModDeploymentTarget target = environment.Target(@"interface\winner.swf");
				environment.AddVirtualOwner(virtualMod, target, "virtual", true, 0);
				environment.InstallDirect(direct, target, "direct");

				environment.Uninstall(direct);

				Assert.AreEqual("virtual", environment.ReadTarget(target));
				Assert.IsTrue(environment.InstallLog.IsDeploymentTargetPromoted(target));
				CollectionAssert.AreEqual(
					new[] { environment.Key(virtualMod) },
					environment.InstallLog.GetDeploymentOwnerKeys(target));
			}
		}

		[Test]
		public void InactiveVirtualInstall_LeavesDirectWinnerUntouched()
		{
			using (var environment = new MixedTestEnvironment())
			{
				IMod direct = environment.RegisterMod("Direct", ModInstallMethod.Direct);
				IMod virtualMod = environment.RegisterMod("Virtual", ModInstallMethod.Virtual);
				ModDeploymentTarget target = environment.Target(@"sound\winner.xwm");
				environment.InstallDirect(direct, target, "direct");
				string virtualSource = environment.StageVirtual(virtualMod, target, "virtual");

				string deployedPath = environment.InstallVirtual(virtualMod, target, virtualSource, false);

				Assert.AreEqual(string.Empty, deployedPath);
				Assert.AreEqual("direct", environment.ReadTarget(target));
				CollectionAssert.AreEqual(
					new[] { environment.Key(virtualMod), environment.Key(direct) },
					environment.InstallLog.GetDeploymentOwnerKeys(target));
			}
		}

		[Test]
		public void LegacyVirtualOriginalBackup_IsClaimedAndRestoredAfterLastManagedOwner()
		{
			using (var environment = new MixedTestEnvironment())
			{
				IMod virtualMod = environment.RegisterMod("Virtual", ModInstallMethod.Virtual);
				IMod direct = environment.RegisterMod("Direct", ModInstallMethod.Direct);
				ModDeploymentTarget target = environment.Target(@"config\original.ini");
				environment.AddVirtualOwner(virtualMod, target, "virtual", true, 0, "original");
				string legacyPath = environment.VirtualState.GetOverwritePath(target, environment.Key(virtualMod));

				environment.InstallDirect(direct, target, "direct");
				Assert.IsFalse(File.Exists(legacyPath));
				Assert.AreEqual(1, environment.BackupFiles.Length);

				environment.Uninstall(direct);
				Assert.AreEqual("virtual", environment.ReadTarget(target));
				environment.Uninstall(virtualMod);

				Assert.AreEqual("original", environment.ReadTarget(target));
				Assert.IsFalse(environment.InstallLog.IsDeploymentTargetPromoted(target));
				Assert.AreEqual(0, environment.BackupFiles.Length);
			}
		}

		[Test]
		public void MixedVirtualUninstall_AlsoRemovesPureVirtualTargetsTransactionally()
		{
			using (var environment = new MixedTestEnvironment())
			{
				IMod virtualMod = environment.RegisterMod("Virtual", ModInstallMethod.Virtual);
				IMod direct = environment.RegisterMod("Direct", ModInstallMethod.Direct);
				ModDeploymentTarget promotedTarget = environment.Target(@"textures\promoted.dds");
				ModDeploymentTarget pureTarget = environment.Target(@"textures\pure.dds");
				environment.AddVirtualOwner(virtualMod, promotedTarget, "promoted-virtual", true, 0);
				environment.InstallDirect(direct, promotedTarget, "direct");
				environment.AddVirtualOwner(virtualMod, pureTarget, "pure-virtual", true, 0, "pure-original");

				environment.Uninstall(virtualMod);

				Assert.AreEqual("direct", environment.ReadTarget(promotedTarget));
				Assert.AreEqual("pure-original", environment.ReadTarget(pureTarget));
				CollectionAssert.AreEqual(
					new[] { environment.Key(direct) },
					environment.InstallLog.GetDeploymentOwnerKeys(promotedTarget));
				Assert.IsFalse(environment.InstallLog.IsDeploymentTargetPromoted(pureTarget));
			}
		}

		[Test]
		public void DirectOverwriteOfHardLinkedVirtualWinner_DoesNotMutateStagedSource()
		{
			using (var environment = new MixedTestEnvironment())
			{
				IMod virtualMod = environment.RegisterMod("Virtual", ModInstallMethod.Virtual);
				IMod direct = environment.RegisterMod("Direct", ModInstallMethod.Direct);
				ModDeploymentTarget target = environment.Target(@"bin\linked.dll");
				string source = environment.AddVirtualOwner(virtualMod, target, "virtual-source", true, 0, null, true);

				environment.InstallDirect(direct, target, "direct");

				Assert.AreEqual("direct", environment.ReadTarget(target));
				Assert.AreEqual("virtual-source", File.ReadAllText(source));
			}
		}

		/// <summary>
		/// Verifies that cancelling a promoted Direct overwrite recreates the original Virtual hard link, not just its bytes.
		/// </summary>
		[Test]
		public void CancelledPromotion_RestoresVirtualHardLinkTopology()
		{
			using (var environment = new MixedTestEnvironment())
			{
				IMod virtualMod = environment.RegisterMod("Virtual", ModInstallMethod.Virtual);
				IMod direct = environment.RegisterMod("Direct", ModInstallMethod.Direct);
				ModDeploymentTarget target = environment.Target(@"bin\rollback-hardlink.dll");
				string source = environment.AddVirtualOwner(virtualMod, target, "virtual", true, 0, null, true);
				string payload = environment.CreatePayload("direct");

				using (var scope = new TransactionScope())
				using (FileStream stream = File.OpenRead(payload))
					environment.Manager.InstallDirectFile(direct, target, stream, new TxFileManager());

				File.WriteAllText(source, "changed-through-source");
				Assert.AreEqual("changed-through-source", environment.ReadTarget(target));
			}
		}

		/// <summary>
		/// Verifies that cancelling a promoted Direct overwrite recreates the original Virtual symbolic link when supported.
		/// </summary>
		[Test]
		public void CancelledPromotion_RestoresVirtualSymbolicLinkTopology()
		{
			using (var environment = new MixedTestEnvironment())
			{
				IMod virtualMod = environment.RegisterMod("Virtual", ModInstallMethod.Virtual);
				IMod direct = environment.RegisterMod("Direct", ModInstallMethod.Direct);
				ModDeploymentTarget target = environment.Target(@"bin\rollback-symlink.dll");
				string source = environment.AddVirtualOwner(virtualMod, target, "virtual", true, 0, null, false, true);
				string payload = environment.CreatePayload("direct");

				using (var scope = new TransactionScope())
				using (FileStream stream = File.OpenRead(payload))
					environment.Manager.InstallDirectFile(direct, target, stream, new TxFileManager());

				string deployedPath = environment.Manager.GetDeploymentPath(target);
				Assert.IsTrue((File.GetAttributes(deployedPath) & FileAttributes.ReparsePoint) != 0,
					"Rollback must restore a symbolic-link reparse point, not only matching bytes.");
				Assert.IsTrue(new TxFileManager { TxEnabled = false }.IsSameFile(deployedPath, source));
				File.WriteAllText(source, "changed-through-source");
				Assert.AreEqual("changed-through-source", environment.ReadTarget(target));
			}
		}

		/// <summary>
		/// Verifies cancelling an in-place promoted Virtual replacement restores the previous symbolic-link source and owner stack.
		/// </summary>
		[Test]
		public void CancelledSameOwnerVirtualReplacement_RestoresOriginalSymbolicLinkSource()
		{
			using (var environment = new MixedTestEnvironment())
			{
				IMod virtualMod = environment.RegisterMod("Virtual", ModInstallMethod.Virtual);
				IMod directMod = environment.RegisterMod("Direct", ModInstallMethod.Direct);
				ModDeploymentTarget target = environment.Target(@"bin\same-owner-symlink.dll");
				string virtualKey = environment.Key(virtualMod);
				string originalSource = environment.AddVirtualOwner(virtualMod, target, "original", true, 0, null, false, true);
				environment.InstallDirect(directMod, target, "direct");
				environment.Uninstall(directMod);

				string replacementSource = Path.Combine(
					environment.VirtualPath,
					Path.GetFileNameWithoutExtension(virtualMod.Filename) + "-Replacement",
					target.RelativePath);
				Directory.CreateDirectory(Path.GetDirectoryName(replacementSource));
				File.WriteAllText(replacementSource, "replacement");
				Assert.AreNotEqual(originalSource, replacementSource,
					"The replacement payload must use a distinct staged source so rollback can distinguish both versions.");

				string deployedPath = environment.Manager.GetDeploymentPath(target);
				var fileManager = new TxFileManager { TxEnabled = false };
				if (File.Exists(deployedPath))
					File.Delete(deployedPath);
				CreateTestSymbolicLink(fileManager, deployedPath, originalSource);
				Assert.AreEqual(FileEntryKind.SymbolicLink, fileManager.GetFileEntryKind(deployedPath, originalSource));
				Assert.IsTrue(fileManager.IsSameFile(deployedPath, originalSource));
				Assert.IsFalse(fileManager.IsSameFile(deployedPath, replacementSource));
				Assert.AreEqual("original", environment.ReadTarget(target));
				Assert.AreEqual(originalSource, environment.VirtualState.Activator.GetVirtualSourceForOwner(target, virtualKey));

				using (var scope = new TransactionScope())
				{
					environment.Manager.InstallVirtualFile(
						virtualMod,
						target,
						target.RelativePath,
						replacementSource,
						ModInstallRoot.Data,
						true,
						new TxFileManager());

					Assert.AreEqual("replacement", environment.ReadTarget(target));
					Assert.AreEqual(replacementSource, environment.VirtualState.Activator.GetVirtualSourceForOwner(target, virtualKey));
				}

				Assert.AreEqual(FileEntryKind.SymbolicLink, fileManager.GetFileEntryKind(deployedPath, originalSource));
				Assert.IsTrue(fileManager.IsSameFile(deployedPath, originalSource));
				Assert.IsFalse(fileManager.IsSameFile(deployedPath, replacementSource));
				Assert.AreEqual("original", environment.ReadTarget(target));
				Assert.AreEqual(originalSource, environment.VirtualState.Activator.GetVirtualSourceForOwner(target, virtualKey));
				CollectionAssert.AreEqual(new[] { virtualKey }, environment.InstallLog.GetDeploymentOwnerKeys(target));
			}
       }

		/// <summary>
		/// Verifies native symbolic-link failure is reported without replacing an existing destination.
		/// </summary>
		[Test]
		public void CreateSymbolicLink_ExistingDestinationThrowsAndPreservesFile()
		{
			string root = Path.Combine(Path.GetTempPath(), "NMM-LinkFailure-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			try
			{
				string source = Path.Combine(root, "source.bin");
				string destination = Path.Combine(root, "existing.bin");
				File.WriteAllText(source, "source");
				File.WriteAllText(destination, "original");
				TxFileManager fileManager = new TxFileManager { TxEnabled = false };

				IOException error = Assert.Throws<IOException>(() => fileManager.CreateSymbolicLink(destination, source));
				Assert.IsInstanceOf<Win32Exception>(error.InnerException);
				Assert.AreNotEqual(0, ((Win32Exception)error.InnerException).NativeErrorCode);
				StringAssert.Contains(destination, error.Message);
				StringAssert.Contains(source, error.Message);
				Assert.AreEqual(FileEntryKind.RegularFile, fileManager.GetFileEntryKind(destination, source));
				Assert.AreEqual("original", File.ReadAllText(destination));
				Assert.AreEqual("source", File.ReadAllText(source));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		/// <summary>
		/// Creates a verified test link, skipping only explicit privilege or filesystem capability failures.
		/// </summary>
		private static void CreateTestSymbolicLink(TxFileManager p_tfmFileManager, string p_strLink, string p_strSource)
		{
			try
			{
				Assert.IsTrue(p_tfmFileManager.CreateSymbolicLink(p_strLink, p_strSource));
			}
			catch (IOException ex)
			{
				Win32Exception nativeError = ex.InnerException as Win32Exception;
				if (nativeError != null && (nativeError.NativeErrorCode == 1314 || nativeError.NativeErrorCode == 50))
					Assert.Ignore("Symbolic-link setup requires Windows symlink privileges or a supported filesystem: " + ex.Message);
				throw;
			}

			Assert.AreEqual(FileEntryKind.SymbolicLink, p_tfmFileManager.GetFileEntryKind(p_strLink, p_strSource),
				"Symbolic-link setup must create an observable link before the operation under test.");
			if (File.Exists(p_strSource))
				Assert.IsTrue(p_tfmFileManager.IsSameFile(p_strLink, p_strSource),
					"Symbolic-link setup must resolve to the expected source.");
		}

		/// <summary>
		/// Verifies that rollback captures and recreates a symbolic link whose source is dangling.
		/// </summary>
		[Test]
		public void CancelledDelete_RestoresDanglingSymbolicLinkTopology()
		{
			string root = Path.Combine(Path.GetTempPath(), "NMM-DanglingLink-" + Guid.NewGuid().ToString("N"));
			string source = Path.Combine(root, "missing-source.bin");
			string link = Path.Combine(root, "dangling-link.bin");
			Directory.CreateDirectory(root);
			try
			{
				using (var setupScope = new TransactionScope())
				{
					CreateTestSymbolicLink(new TxFileManager(), link, source);
					setupScope.Complete();
				}

				var fileManager = new TxFileManager { TxEnabled = false };
				Assert.AreEqual(FileEntryKind.SymbolicLink, fileManager.GetFileEntryKind(link, source),
					"A dangling symbolic link must be observable before the transactional delete.");

				using (var scope = new TransactionScope())
					new TxFileManager().DeleteLink(link, source);

				Assert.AreEqual(FileEntryKind.SymbolicLink, fileManager.GetFileEntryKind(link, source),
					"Rollback must recreate the dangling symbolic-link entry itself.");
				File.WriteAllText(source, "late-source");
				Assert.IsTrue(fileManager.IsSameFile(link, source));
				Assert.AreEqual("late-source", File.ReadAllText(link));
			}
			finally
			{
				if (Directory.Exists(root))
					Directory.Delete(root, true);
			}
		}

		/// <summary>
		/// Verifies startup crash recovery recreates a Virtual symbolic-link winner after restart.
		/// </summary>
		[Test]
		public void PendingDeploymentRecoveryJournal_RestoresVirtualSymbolicLinkWinnerAfterRestart()
		{
			using (var environment = new MixedTestEnvironment())
			{
				IMod virtualMod = environment.RegisterMod("RecoveryVirtual", ModInstallMethod.Virtual);
				ModDeploymentTarget target = environment.Target(@"bin\recovery-symlink.dll");
				string source = environment.AddVirtualOwner(virtualMod, target, "virtual", true, 0, null, false, true);
				string transactionDirectory = environment.CreateVirtualRecoveryJournal(target, environment.Key(virtualMod));
				string deployedPath = environment.Manager.GetDeploymentPath(target);

				File.Delete(deployedPath);
				new ModDeploymentManager(environment.InstallLog, environment.VirtualState.Activator, environment.GameMode);

				Assert.IsTrue((File.GetAttributes(deployedPath) & FileAttributes.ReparsePoint) != 0);
				Assert.IsTrue(new TxFileManager { TxEnabled = false }.IsSameFile(deployedPath, source));
				Assert.IsFalse(Directory.Exists(transactionDirectory));
			}
		}

		/// <summary>
		/// Verifies failed startup recovery retains its durable journal for a later retry.
		/// </summary>
		[Test]
		public void PendingDeploymentRecoveryJournal_FailedVirtualRecoveryRetainsJournal()
		{
			using (var environment = new MixedTestEnvironment())
			{
				IMod virtualMod = environment.RegisterMod("BrokenRecoveryVirtual", ModInstallMethod.Virtual);
				ModDeploymentTarget target = environment.Target(@"bin\recovery-missing-source.dll");
				string source = environment.AddVirtualOwner(virtualMod, target, "virtual", true, 0, null, false, true);
				string transactionDirectory = environment.CreateVirtualRecoveryJournal(target, environment.Key(virtualMod));
				File.Delete(environment.Manager.GetDeploymentPath(target));
				File.Delete(source);

				Assert.Throws<FileNotFoundException>(() =>
					new ModDeploymentManager(environment.InstallLog, environment.VirtualState.Activator, environment.GameMode));
				Assert.IsTrue(Directory.Exists(transactionDirectory));
			}
		}

		[Test]
		public void CancelledPromotion_RestoresVirtualWinnerLegacyBackupAndMetadata()
		{
			using (var environment = new MixedTestEnvironment())
			{
				IMod virtualMod = environment.RegisterMod("Virtual", ModInstallMethod.Virtual);
				IMod direct = environment.RegisterMod("Direct", ModInstallMethod.Direct);
				ModDeploymentTarget target = environment.Target(@"textures\rollback.dds");
				string source = environment.AddVirtualOwner(virtualMod, target, "virtual", true, 0, "original");
				string legacyPath = environment.VirtualState.GetOverwritePath(target, environment.Key(virtualMod));
				string payload = environment.CreatePayload("direct");

				using (var scope = new TransactionScope())
				using (FileStream stream = File.OpenRead(payload))
				{
					environment.Manager.InstallDirectFile(direct, target, stream, new TxFileManager());
				}

				Assert.AreEqual("virtual", environment.ReadTarget(target));
				Assert.AreEqual("virtual", File.ReadAllText(source));
				Assert.AreEqual("original", File.ReadAllText(legacyPath));
				Assert.IsFalse(environment.InstallLog.IsDeploymentTargetPromoted(target));
				Assert.AreEqual(0, environment.BackupFiles.Length);
				Assert.IsTrue(environment.VirtualState.IsActive(target, environment.Key(virtualMod)));
			}
		}

		[Test]
		public void FileManagerOwnerSwitch_DirectOwners_ReordersStackAndRestoresSelectedPayload()
		{
			using (var environment = new MixedTestEnvironment())
			{
				IMod firstDirect = environment.RegisterMod("FirstDirect", ModInstallMethod.Direct);
				IMod secondDirect = environment.RegisterMod("SecondDirect", ModInstallMethod.Direct);
				ModDeploymentTarget target = environment.Target(@"textures\owner-switch.dds");
				environment.InstallDirect(firstDirect, target, "first");
				environment.InstallDirect(secondDirect, target, "second");

				environment.Manager.SwitchPromotedOwner(target, environment.Key(firstDirect));

				Assert.AreEqual("first", environment.ReadTarget(target));
				CollectionAssert.AreEqual(
					new[] { environment.Key(secondDirect), environment.Key(firstDirect) },
					environment.InstallLog.GetDeploymentOwnerKeys(target));

				environment.Manager.SwitchPromotedOwner(target, environment.Key(secondDirect));

				Assert.AreEqual("second", environment.ReadTarget(target));
				CollectionAssert.AreEqual(
					new[] { environment.Key(firstDirect), environment.Key(secondDirect) },
					environment.InstallLog.GetDeploymentOwnerKeys(target));
			}
		}

		[Test]
		public void FileManagerOwnerSwitch_MixedOwners_UsesMethodNeutralCoordinator()
		{
			using (var environment = new MixedTestEnvironment())
			{
				IMod direct = environment.RegisterMod("Direct", ModInstallMethod.Direct);
				IMod virtualMod = environment.RegisterMod("Virtual", ModInstallMethod.Virtual);
				ModDeploymentTarget target = environment.Target(@"meshes\owner-switch.nif");
				environment.InstallDirect(direct, target, "direct");
				string virtualSource = environment.StageVirtual(virtualMod, target, "virtual");
				environment.InstallVirtual(virtualMod, target, virtualSource, true);

				environment.Manager.SwitchPromotedOwner(target, environment.Key(direct));

				Assert.AreEqual("direct", environment.ReadTarget(target));
				Assert.IsFalse(environment.VirtualState.IsActive(target, environment.Key(virtualMod)));
				CollectionAssert.AreEqual(
					new[] { environment.Key(virtualMod), environment.Key(direct) },
					environment.InstallLog.GetDeploymentOwnerKeys(target));

				environment.Manager.SwitchPromotedOwner(target, environment.Key(virtualMod));

				Assert.AreEqual("virtual", environment.ReadTarget(target));
				Assert.IsTrue(environment.VirtualState.IsActive(target, environment.Key(virtualMod)));
				CollectionAssert.AreEqual(
					new[] { environment.Key(direct), environment.Key(virtualMod) },
					environment.InstallLog.GetDeploymentOwnerKeys(target));
			}
		}

		[Test]
		public void FileManagerOwnerSwitch_MissingDirectBackupRollsBackCurrentWinner()
		{
			using (var environment = new MixedTestEnvironment())
			{
				IMod firstDirect = environment.RegisterMod("FirstDirect", ModInstallMethod.Direct);
				IMod secondDirect = environment.RegisterMod("SecondDirect", ModInstallMethod.Direct);
				ModDeploymentTarget target = environment.Target(@"scripts\rollback-switch.pex");
				environment.InstallDirect(firstDirect, target, "first");
				environment.InstallDirect(secondDirect, target, "second");
				File.Delete(environment.Manager.GetOwnerSourcePath(target, environment.Key(firstDirect)));

				Assert.Throws<FileNotFoundException>(() =>
					environment.Manager.SwitchPromotedOwner(target, environment.Key(firstDirect)));

				Assert.AreEqual("second", environment.ReadTarget(target));
				CollectionAssert.AreEqual(
					new[] { environment.Key(firstDirect), environment.Key(secondDirect) },
					environment.InstallLog.GetDeploymentOwnerKeys(target));
			}
		}

		[Test]
		public void ProfileRestorePromotedStack_RestoresExactPersistedOrderAndWinner()
		{
			using (var environment = new MixedTestEnvironment())
			{
				IMod firstDirect = environment.RegisterMod("FirstDirect", ModInstallMethod.Direct);
				IMod secondDirect = environment.RegisterMod("SecondDirect", ModInstallMethod.Direct);
				IMod thirdDirect = environment.RegisterMod("ThirdDirect", ModInstallMethod.Direct);
				ModDeploymentTarget target = environment.Target(@"textures\profile-restore.dds");
				environment.InstallDirect(firstDirect, target, "first");
				environment.InstallDirect(secondDirect, target, "second");
				environment.InstallDirect(thirdDirect, target, "third");

				environment.Manager.RestorePromotedOwnerStack(target, new[]
				{
					environment.Key(thirdDirect),
					environment.Key(firstDirect),
					environment.Key(secondDirect)
				});

				Assert.AreEqual("second", environment.ReadTarget(target));
				CollectionAssert.AreEqual(
					new[] { environment.Key(thirdDirect), environment.Key(firstDirect), environment.Key(secondDirect) },
					environment.InstallLog.GetDeploymentOwnerKeys(target));
			}
		}

		[Test]
		public void ProfileRestorePromotedStack_AmbientRollbackRestoresPreviousWinnerAndOrder()
		{
			using (var environment = new MixedTestEnvironment())
			{
				IMod firstDirect = environment.RegisterMod("FirstDirect", ModInstallMethod.Direct);
				IMod secondDirect = environment.RegisterMod("SecondDirect", ModInstallMethod.Direct);
				IMod thirdDirect = environment.RegisterMod("ThirdDirect", ModInstallMethod.Direct);
				ModDeploymentTarget target = environment.Target(@"meshes\profile-rollback.nif");
				environment.InstallDirect(firstDirect, target, "first");
				environment.InstallDirect(secondDirect, target, "second");
				environment.InstallDirect(thirdDirect, target, "third");

				using (var transaction = new TransactionScope())
				{
					environment.Manager.RestorePromotedOwnerStack(target, new[]
					{
						environment.Key(thirdDirect),
						environment.Key(firstDirect),
						environment.Key(secondDirect)
					});
					Assert.AreEqual("second", environment.ReadTarget(target));
				}

				Assert.AreEqual("third", environment.ReadTarget(target));
				CollectionAssert.AreEqual(
					new[] { environment.Key(firstDirect), environment.Key(secondDirect), environment.Key(thirdDirect) },
					environment.InstallLog.GetDeploymentOwnerKeys(target));
			}
		}

		[Test]
		public void LocalRestoreCapturedOwnerStack_PromotedMixed_RebuildsExactPayloadsOrderAndWinner()
		{
			using (var environment = new MixedTestEnvironment())
			{
				IMod direct = environment.RegisterMod("RestoreDirect", ModInstallMethod.Direct);
				IMod virtualMod = environment.RegisterMod("RestoreVirtual", ModInstallMethod.Virtual);
				ModDeploymentTarget target = environment.Target(@"textures\local-restore-mixed.dds");
				string directPayload = environment.CreatePayload("captured-direct");
				string virtualPayload = environment.CreatePayload("captured-virtual");

				environment.Manager.RestoreCapturedOwnerStack(target, true, new[]
				{
					new ModDeploymentRestoreOwner(environment.Key(direct), ModDeploymentRestoreOwnerKind.Direct,
						direct, ModInstallRoot.Data, directPayload),
					new ModDeploymentRestoreOwner(environment.Key(virtualMod), ModDeploymentRestoreOwnerKind.Virtual,
						virtualMod, ModInstallRoot.Data, virtualPayload)
				});

				Assert.IsTrue(environment.Manager.IsPromoted(target));
				CollectionAssert.AreEqual(new[] { environment.Key(direct), environment.Key(virtualMod) },
					environment.Manager.GetOwnerKeys(target));
				Assert.AreEqual("captured-direct", File.ReadAllText(environment.Manager.GetOwnerSourcePath(target, environment.Key(direct))));
				Assert.AreEqual("captured-virtual", File.ReadAllText(environment.Manager.GetOwnerSourcePath(target, environment.Key(virtualMod))));
				Assert.AreEqual("captured-virtual", environment.ReadTarget(target));
			}
		}

		[Test]
		public void LocalRestoreCapturedOwnerStack_PureVirtual_PreservesUnmanagedFallbackAndExactWinnerOrder()
		{
			using (var environment = new MixedTestEnvironment())
			{
				IMod firstVirtual = environment.RegisterMod("RestoreVirtualA", ModInstallMethod.Virtual);
				IMod secondVirtual = environment.RegisterMod("RestoreVirtualB", ModInstallMethod.Virtual);
				ModDeploymentTarget target = environment.Target(@"meshes\local-restore-virtual.nif");
				string deploymentPath = environment.Manager.GetDeploymentPath(target);
				Directory.CreateDirectory(Path.GetDirectoryName(deploymentPath));
				File.WriteAllText(deploymentPath, "unmanaged-fallback");

				environment.Manager.RestoreCapturedOwnerStack(target, false, new[]
				{
					new ModDeploymentRestoreOwner(environment.Key(firstVirtual), ModDeploymentRestoreOwnerKind.Virtual,
						firstVirtual, ModInstallRoot.Data, environment.CreatePayload("captured-a")),
					new ModDeploymentRestoreOwner(environment.Key(secondVirtual), ModDeploymentRestoreOwnerKind.Virtual,
						secondVirtual, ModInstallRoot.Data, environment.CreatePayload("captured-b"))
				});

				Assert.IsFalse(environment.Manager.IsPromoted(target));
				CollectionAssert.AreEqual(new[] { environment.Key(firstVirtual), environment.Key(secondVirtual) },
					environment.Manager.GetOwnerKeys(target));
				Assert.AreEqual("captured-a", File.ReadAllText(environment.Manager.GetOwnerSourcePath(target, environment.Key(firstVirtual))));
				Assert.AreEqual("captured-b", File.ReadAllText(environment.Manager.GetOwnerSourcePath(target, environment.Key(secondVirtual))));
				Assert.AreEqual("captured-b", environment.ReadTarget(target));
				Assert.AreEqual("unmanaged-fallback", File.ReadAllText(
					environment.VirtualState.GetOverwritePath(target, environment.Key(firstVirtual))));
			}
		}

		[Test]
		public void UninstallLastVirtualWinner_ReturnsAbsentPluginCandidateOnlyWhenNoFallbackExists()
		{
			using (var environment = new MixedTestEnvironment())
			{
				IMod virtualMod = environment.RegisterMod("Virtual", ModInstallMethod.Virtual);
				IMod direct = environment.RegisterMod("Direct", ModInstallMethod.Direct);
				ModDeploymentTarget target = environment.Target("winner.esp");
				environment.AddVirtualOwner(virtualMod, target, "virtual", true, 0);
				environment.InstallDirect(direct, target, "direct");
				environment.Uninstall(direct);

				IReadOnlyCollection<string> absent = environment.Uninstall(virtualMod);

				CollectionAssert.AreEqual(new[] { environment.Manager.GetDeploymentPath(target) }, absent);
				Assert.IsFalse(File.Exists(environment.Manager.GetDeploymentPath(target)));
			}
		}

		/// <summary>
		/// Executes the activation task synchronously for mixed-deployment regression coverage.
		/// </summary>
		private sealed class SynchronousLinkActivationTask : LinkActivationTask
		{
			public SynchronousLinkActivationTask(MixedTestEnvironment environment, IMod mod)
				: base(null, environment.VirtualState.Activator,
					new VirtualDeploymentService(environment.VirtualState.Activator, environment.Manager),
					mod, true, null, ModInstallRoot.Data, environment.Manager)
			{
			}

			/// <summary>
			/// Runs the protected task body on the calling test thread.
			/// </summary>
			public void Execute()
			{
				DoWork(new object[0]);
			}
		}

		private sealed class MixedTestEnvironment : IDisposable
		{
			private readonly string m_strRootPath;
			private int m_intPayloadNumber;

			public MixedTestEnvironment()
			{
				m_strRootPath = Path.Combine(Path.GetTempPath(), "NMM-Step4-" + Guid.NewGuid().ToString("N"));
				DataPath = Path.Combine(m_strRootPath, "Data");
				GameRootPath = Path.Combine(m_strRootPath, "GameRoot");
				SecondaryPath = Path.Combine(m_strRootPath, "Secondary");
				OverwritePath = Path.Combine(m_strRootPath, "InstallInfo", "overwrites");
				VirtualPath = Path.Combine(m_strRootPath, "VirtualInstall");
				ModPath = Path.Combine(m_strRootPath, "Mods");
				Directory.CreateDirectory(DataPath);
				Directory.CreateDirectory(GameRootPath);
				Directory.CreateDirectory(SecondaryPath);
				Directory.CreateDirectory(OverwritePath);
				Directory.CreateDirectory(VirtualPath);
				Directory.CreateDirectory(ModPath);

				IGameModeEnvironmentInfo gameModeInfo = InterfaceStub<IGameModeEnvironmentInfo>.Create((method, args) =>
				{
					if (method.Name == "get_InstallationPath")
						return GameRootPath;
					if (method.Name == "get_SecondaryInstallationPath")
						return SecondaryPath;
					if (method.Name == "get_OverwriteDirectory")
						return OverwritePath;
					return null;
				});
				GameMode = InterfaceStub<IGameMode>.Create((method, args) =>
				{
					if (method.Name == "get_GameModeEnvironmentInfo")
						return gameModeInfo;
					if (method.Name == "get_InstallationPath")
						return GameRootPath;
					if (method.Name == "get_PluginDirectory")
						return DataPath;
					if (method.Name == "get_UsesPlugins")
						return true;
					if (method.Name == "get_SecondaryInstallationPath")
						return SecondaryPath;
					if (method.Name == "GetModFormatAdjustedPath")
						return args[1];
					return null;
				});

				InstallLog = CreateInstallLog(ModPath, Path.Combine(m_strRootPath, "InstallInfo", "InstallLog.xml"));
				VirtualState = new TransactionalVirtualState(InstallLog, VirtualPath, GameMode);
				Manager = new ModDeploymentManager(InstallLog, VirtualState.Activator, GameMode);
				VirtualState.GetDeploymentPath = Manager.GetDeploymentPath;
			}

			public string DataPath { get; private set; }
			public string GameRootPath { get; private set; }
			public string SecondaryPath { get; private set; }
			public string OverwritePath { get; private set; }
			public string VirtualPath { get; private set; }
			public string ModPath { get; private set; }
			public IGameMode GameMode { get; private set; }
			public InstallLog InstallLog { get; private set; }
			public ModDeploymentManager Manager { get; private set; }
			public TransactionalVirtualState VirtualState { get; private set; }
			public string[] BackupFiles => Directory.GetFiles(OverwritePath, "*.nmmbackup", SearchOption.AllDirectories);

			public ModDeploymentTarget Target(string p_strPath)
			{
				return ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, p_strPath);
			}

			public IMod RegisterMod(string p_strName, ModInstallMethod p_mimMethod)
			{
				var mod = new InstallLog.DummyMod(p_strName, Path.Combine(ModPath, p_strName + ".7z"));
				InstallLog.AddActiveMod(mod, ModInstallRoot.Data, p_mimMethod);
				return mod;
			}

			public string Key(IMod p_modMod)
			{
				return InstallLog.GetModKey(p_modMod);
			}

			public string StageVirtual(IMod p_modMod, ModDeploymentTarget p_mdtTarget, string p_strContents)
			{
				string path = Path.Combine(VirtualPath, Path.GetFileNameWithoutExtension(p_modMod.Filename), p_mdtTarget.RelativePath);
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.WriteAllText(path, p_strContents);
				return path;
			}

			public string AddVirtualOwner(IMod p_modMod, ModDeploymentTarget p_mdtTarget, string p_strContents,
				bool p_booActive, int p_intPriority, string p_strOriginal = null, bool p_booHardLink = false, bool p_booSymbolicLink = false)
			{
				string source = StageVirtual(p_modMod, p_mdtTarget, p_strContents);
				VirtualState.Add(p_modMod, Key(p_modMod), p_mdtTarget, source, p_booActive, p_intPriority, p_booHardLink, p_booSymbolicLink);
				if (p_booActive)
				DeployInitial(source, Manager.GetDeploymentPath(p_mdtTarget), p_booHardLink, p_booSymbolicLink);
				if (p_strOriginal != null)
				File.WriteAllText(VirtualState.GetOverwritePath(p_mdtTarget, Key(p_modMod)), p_strOriginal);
				return source;
			}

			public void InstallDirect(IMod p_modMod, ModDeploymentTarget p_mdtTarget, string p_strContents)
			{
				string payload = CreatePayload(p_strContents);
				using (var scope = new TransactionScope())
				{
					using (FileStream stream = File.OpenRead(payload))
						Manager.InstallDirectFile(p_modMod, p_mdtTarget, stream, new TxFileManager());
					scope.Complete();
				}
			}

			public string InstallVirtual(IMod p_modMod, ModDeploymentTarget p_mdtTarget, string p_strSource, bool p_booActivate)
			{
				using (var scope = new TransactionScope())
				{
					string result = Manager.InstallVirtualFile(
						p_modMod,
						p_mdtTarget,
						p_mdtTarget.RelativePath,
						p_strSource,
						ModInstallRoot.Data,
						p_booActivate,
						new TxFileManager());
					scope.Complete();
					return result;
				}
			}

			public IReadOnlyCollection<string> Uninstall(IMod p_modMod)
			{
				using (var scope = new TransactionScope())
				{
					IReadOnlyCollection<string> result = Manager.UninstallMixedMod(p_modMod, new TxFileManager());
					InstallLog.RemoveMod(p_modMod);
					scope.Complete();
					return result;
				}
			}

			public string ReadTarget(ModDeploymentTarget p_mdtTarget)
			{
				return File.ReadAllText(Manager.GetDeploymentPath(p_mdtTarget));
			}

			public string CreatePayload(string p_strContents)
			{
				string path = Path.Combine(m_strRootPath, "payload-" + (++m_intPayloadNumber) + ".bin");
				File.WriteAllText(path, p_strContents);
				return path;
			}

			/// <summary>
			/// Creates a durable pre-commit Virtual deployment journal for startup recovery tests.
			/// </summary>
			public string CreateVirtualRecoveryJournal(ModDeploymentTarget p_mdtTarget, string p_strOwnerKey)
			{
				string transactionDirectory = Path.Combine(OverwritePath, "deployment", "_recovery",
					"test-" + Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(transactionDirectory);
				new XDocument(new XElement("deploymentRecovery",
					new XAttribute("transactionId", "test"),
					new XAttribute("preCommitSequence", InstallLog.DeploymentCommitSequence)))
					.Save(Path.Combine(transactionDirectory, "transaction.xml"));

				string sourcePath = VirtualState.Activator.GetVirtualSourceForOwner(p_mdtTarget, p_strOwnerKey);
				var fileManager = new TxFileManager { TxEnabled = false };
				FileEntryKind entryKind = fileManager.GetFileEntryKind(Manager.GetDeploymentPath(p_mdtTarget), sourcePath);
				Assert.AreNotEqual(FileEntryKind.Absent, entryKind);

				var record = new XElement("target",
					new XAttribute("root", p_mdtTarget.Root),
					new XAttribute("path", p_mdtTarget.RelativePath),
					new XAttribute("deploymentState", "Virtual"),
					new XAttribute("virtualOwnerKey", p_strOwnerKey),
					new XAttribute("virtualDeploymentKind", entryKind),
					new XElement("owners"),
					new XElement("backups"));
				byte[] payload = Encoding.UTF8.GetBytes(record.ToString(SaveOptions.DisableFormatting));
				using (var stream = new FileStream(Path.Combine(transactionDirectory, "targets.bin"), FileMode.Create, FileAccess.Write, FileShare.None))
				using (var writer = new BinaryWriter(stream, Encoding.UTF8))
				{
					writer.Write(payload.Length);
					writer.Write(payload);
				}
				return transactionDirectory;
			}

			public void Dispose()
			{
				InstallLog.Release();
				if (Directory.Exists(m_strRootPath))
					Directory.Delete(m_strRootPath, true);
			}

			private static void DeployInitial(string p_strSource, string p_strTarget, bool p_booHardLink, bool p_booSymbolicLink)
			{
				Directory.CreateDirectory(Path.GetDirectoryName(p_strTarget));
				if (p_booHardLink || p_booSymbolicLink)
				{
					using (var scope = new TransactionScope())
					{
						TxFileManager fileManager = new TxFileManager();
						bool created;
						if (p_booHardLink)
						{
							created = fileManager.CreateHardLink(p_strTarget, p_strSource);
						}
						else
						{
							CreateTestSymbolicLink(fileManager, p_strTarget, p_strSource);
							created = true;
						}

						if (!created)
							Assert.Ignore("Hard links are not available in the current test environment.");
						scope.Complete();
						return;
					}
				}
				File.Copy(p_strSource, p_strTarget, true);
			}

			private static InstallLog CreateInstallLog(string p_strModDirectory, string p_strLogPath)
			{
				Directory.CreateDirectory(Path.GetDirectoryName(p_strLogPath));
				var registry = new ModRegistry(null, null);
				ConstructorInfo constructor = typeof(InstallLog).GetConstructor(
					BindingFlags.Instance | BindingFlags.NonPublic,
					null,
					new[] { typeof(ModRegistry), typeof(IGameMode), typeof(string), typeof(string) },
					null);
				Assert.NotNull(constructor);
				return (InstallLog)constructor.Invoke(new object[] { registry, null, p_strModDirectory, p_strLogPath });
			}
		}

		private sealed class TransactionalVirtualState
		{
			private readonly IInstallLog m_ilgInstallLog;
			private readonly string m_strVirtualPath;
			private readonly IGameMode m_gmdGameMode;
			private List<VirtualOwner> m_lstOwners = new List<VirtualOwner>();
			private string m_strEnlistedTransaction;

			public TransactionalVirtualState(IInstallLog p_ilgInstallLog, string p_strVirtualPath, IGameMode p_gmdGameMode)
			{
				m_ilgInstallLog = p_ilgInstallLog;
				m_strVirtualPath = p_strVirtualPath;
				m_gmdGameMode = p_gmdGameMode;
				Activator = InterfaceStub<IVirtualModActivator>.Create(HandleCall);
			}

			public IVirtualModActivator Activator { get; private set; }
			public Func<ModDeploymentTarget, string> GetDeploymentPath { get; set; }

			public void Add(IMod p_modMod, string p_strModKey, ModDeploymentTarget p_mdtTarget,
				string p_strSource, bool p_booActive, int p_intPriority, bool p_booHardLink = false, bool p_booSymbolicLink = false)
			{
				m_lstOwners.Add(new VirtualOwner
				{
					Mod = p_modMod,
					ModKey = p_strModKey,
					Target = p_mdtTarget,
					Source = p_strSource,
					LogicalPath = p_mdtTarget.RelativePath,
					InstallRoot = ModInstallRoot.Data,
					Active = p_booActive,
					Priority = p_intPriority,
					HardLink = p_booHardLink,
					SymbolicLink = p_booSymbolicLink
				});
			}

			public bool IsActive(ModDeploymentTarget p_mdtTarget, string p_strOwnerKey)
			{
				VirtualOwner owner = Find(p_mdtTarget, p_strOwnerKey);
				return owner != null && owner.Active;
			}

			public string GetOverwritePath(ModDeploymentTarget p_mdtTarget, string p_strOwnerKey)
			{
				VirtualOwner owner = Find(p_mdtTarget, p_strOwnerKey);
				if (owner == null)
					return null;

				string path = Path.Combine(
					m_strVirtualPath,
					"_overwrites",
					Path.GetFileNameWithoutExtension(owner.Mod.Filename),
					p_mdtTarget.RelativePath);
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				return path;
			}

			private object HandleCall(MethodInfo p_mifMethod, object[] p_objArguments)
			{
				switch (p_mifMethod.Name)
				{
					case "get_GameMode":
						return m_gmdGameMode;
					case "get_VirtualPath":
						return m_strVirtualPath;
					case "GetVirtualOwnerKeys":
						return GetOwners((ModDeploymentTarget)p_objArguments[0])
							.Select(x => x.ModKey)
							.ToArray();
					case "GetVirtualTargetsForMod":
						return m_lstOwners
							.Where(x => SameMod(x.Mod, (IMod)p_objArguments[0]))
							.Select(x => x.Target)
							.Distinct()
							.ToArray();
					case "GetVirtualSourceForOwner":
						return Require((ModDeploymentTarget)p_objArguments[0], (string)p_objArguments[1]).Source;
					case "GetVirtualOverwritePath":
						return GetOverwritePath((ModDeploymentTarget)p_objArguments[0], (string)p_objArguments[1]);
					case "RegisterVirtualLink":
						Enlist();
						IMod mod = (IMod)p_objArguments[1];
						Add(
							mod,
							m_ilgInstallLog.GetModKey(mod),
							(ModDeploymentTarget)p_objArguments[0],
							(string)p_objArguments[3],
							false,
							(int)p_objArguments[5]);
						return null;
					case "DetachVirtualLinkWithoutFallback":
						Enlist();
						Detach(
							(ModDeploymentTarget)p_objArguments[0],
							(string)p_objArguments[1],
							(TxFileManager)p_objArguments[2]);
						return null;
					case "DeploySpecificVirtualLink":
						Enlist();
						Deploy(
							(ModDeploymentTarget)p_objArguments[0],
							(string)p_objArguments[1],
							(TxFileManager)p_objArguments[2]);
						return null;
					case "RecoverVirtualDeploymentWinner":
						ModDeploymentTarget recoveryTarget = (ModDeploymentTarget)p_objArguments[0];
						VirtualOwner recoveryOwner = Require(recoveryTarget, (string)p_objArguments[1]);
						FileEntryKind expectedKind = (FileEntryKind)p_objArguments[2];

						string recoveryPath = GetDeploymentPath(recoveryTarget);
						Directory.CreateDirectory(Path.GetDirectoryName(recoveryPath));
						var recoveryFileManager = new TxFileManager { TxEnabled = false };
						if (expectedKind == FileEntryKind.Unknown)
						{
							expectedKind = recoveryOwner.HardLink
								? FileEntryKind.HardLink
								: recoveryOwner.SymbolicLink ? FileEntryKind.SymbolicLink : FileEntryKind.RegularFile;
						}

						if (!File.Exists(recoveryOwner.Source))
							throw new FileNotFoundException("The test Virtual source required for recovery is missing.", recoveryOwner.Source);

						FileEntryKind currentKind = recoveryFileManager.GetFileEntryKind(recoveryPath, recoveryOwner.Source);
						if ((expectedKind == FileEntryKind.HardLink || expectedKind == FileEntryKind.SymbolicLink) &&
							currentKind == expectedKind && recoveryFileManager.IsSameFile(recoveryPath, recoveryOwner.Source))
						{
							recoveryOwner.Active = true;
							return null;
						}

						recoveryFileManager.DeleteFileEntryIfPresent(recoveryPath);
						switch (expectedKind)
						{
							case FileEntryKind.HardLink:
								if (!recoveryFileManager.CreateHardLink(recoveryPath, recoveryOwner.Source))
									throw new IOException("The test Virtual hard-link winner could not be recreated.");
								break;
							case FileEntryKind.SymbolicLink:
								recoveryFileManager.CreateSymbolicLink(recoveryPath, recoveryOwner.Source);
								break;
							case FileEntryKind.RegularFile:
								File.Copy(recoveryOwner.Source, recoveryPath, true);
								break;
							default:
								throw new InvalidOperationException("Unexpected Virtual deployment topology in the test recovery backend.");
						}

						currentKind = recoveryFileManager.GetFileEntryKind(recoveryPath, recoveryOwner.Source);
						if (currentKind != expectedKind ||
							((expectedKind == FileEntryKind.HardLink || expectedKind == FileEntryKind.SymbolicLink) &&
							 !recoveryFileManager.IsSameFile(recoveryPath, recoveryOwner.Source)))
						{
							throw new IOException("The test Virtual winner could not be recovered with its captured topology.");
						}

						recoveryOwner.Active = true;
						return null;
					case "RemoveVirtualLinkRecord":
						Enlist();
						m_lstOwners.Remove(Require(
							(ModDeploymentTarget)p_objArguments[0],
							(string)p_objArguments[1]));
						return null;
					case "SetVirtualLinkActiveState":
						Enlist();
						Require(
							(ModDeploymentTarget)p_objArguments[0],
							(string)p_objArguments[1]).Active = (bool)p_objArguments[2];
						return null;
					case "RemoveVirtualModInfoIfUnused":
						return null;
					case "CheckHasActiveLinks":
						return m_lstOwners.Any(x => SameMod(x.Mod, (IMod)p_objArguments[0]));
					case "SaveList":
						return true;
					default:
						return null;
				}
			}

			private IEnumerable<VirtualOwner> GetOwners(ModDeploymentTarget p_mdtTarget)
			{
				return m_lstOwners
					.Where(x => x.Target.Equals(p_mdtTarget))
					.OrderBy(x => x.Active ? 1 : 0)
					.ThenByDescending(x => x.Priority);
			}

			private VirtualOwner Find(ModDeploymentTarget p_mdtTarget, string p_strOwnerKey)
			{
				return m_lstOwners.LastOrDefault(x =>
					x.Target.Equals(p_mdtTarget) &&
					x.ModKey.Equals(p_strOwnerKey, StringComparison.OrdinalIgnoreCase));
			}

			private VirtualOwner Require(ModDeploymentTarget p_mdtTarget, string p_strOwnerKey)
			{
				VirtualOwner owner = Find(p_mdtTarget, p_strOwnerKey);
				if (owner == null)
					throw new InvalidOperationException("The requested test Virtual owner does not exist.");
				return owner;
			}

			private void Detach(ModDeploymentTarget p_mdtTarget, string p_strOwnerKey, TxFileManager p_tfmFileManager)
			{
				VirtualOwner owner = Require(p_mdtTarget, p_strOwnerKey);
				string targetPath = GetDeploymentPath(p_mdtTarget);
				if (owner.Active && File.Exists(targetPath))
					p_tfmFileManager.DeleteLink(targetPath, owner.Source);
				owner.Active = false;
			}

			private void Deploy(ModDeploymentTarget p_mdtTarget, string p_strOwnerKey, TxFileManager p_tfmFileManager)
			{
				VirtualOwner owner = Require(p_mdtTarget, p_strOwnerKey);
				string targetPath = GetDeploymentPath(p_mdtTarget);
				string directory = Path.GetDirectoryName(targetPath);
				if (!Directory.Exists(directory))
					p_tfmFileManager.CreateDirectory(directory);
				if (File.Exists(targetPath))
					p_tfmFileManager.Delete(targetPath);
				p_tfmFileManager.Copy(owner.Source, targetPath, true);
				owner.Active = true;
			}

			private void Enlist()
			{
				Transaction transaction = Transaction.Current;
				if (transaction == null)
					throw new InvalidOperationException("The fake Virtual backend requires an ambient transaction.");

				string transactionId = transaction.TransactionInformation.LocalIdentifier;
				if (m_strEnlistedTransaction == transactionId)
					return;

				m_strEnlistedTransaction = transactionId;
				transaction.EnlistVolatile(
					new StateEnlistment(this, m_lstOwners.Select(x => x.Clone()).ToList()),
					EnlistmentOptions.None);
			}

			private void CompleteTransaction(List<VirtualOwner> p_lstSnapshot, bool p_booRollback)
			{
				if (p_booRollback)
					m_lstOwners = p_lstSnapshot;
				m_strEnlistedTransaction = null;
			}

			private static bool SameMod(IMod p_modFirst, IMod p_modSecond)
			{
				return ReferenceEquals(p_modFirst, p_modSecond) ||
					Path.GetFileName(p_modFirst.Filename).Equals(
						Path.GetFileName(p_modSecond.Filename),
						StringComparison.OrdinalIgnoreCase);
			}

			private sealed class VirtualOwner
			{
				public IMod Mod { get; set; }
				public string ModKey { get; set; }
				public ModDeploymentTarget Target { get; set; }
				public string Source { get; set; }
				public string LogicalPath { get; set; }
				public ModInstallRoot InstallRoot { get; set; }
				public bool Active { get; set; }
				public int Priority { get; set; }
				public bool HardLink { get; set; }
				public bool SymbolicLink { get; set; }

				public VirtualOwner Clone()
				{
					return (VirtualOwner)MemberwiseClone();
				}
			}

			private sealed class StateEnlistment : IEnlistmentNotification
			{
				private readonly TransactionalVirtualState m_tvsOwner;
				private readonly List<VirtualOwner> m_lstSnapshot;

				public StateEnlistment(TransactionalVirtualState p_tvsOwner, List<VirtualOwner> p_lstSnapshot)
				{
					m_tvsOwner = p_tvsOwner;
					m_lstSnapshot = p_lstSnapshot;
				}

				public void Commit(Enlistment p_enlEnlistment)
				{
					m_tvsOwner.CompleteTransaction(m_lstSnapshot, false);
					p_enlEnlistment.Done();
				}

				public void InDoubt(Enlistment p_enlEnlistment)
				{
					Rollback(p_enlEnlistment);
				}

				public void Prepare(PreparingEnlistment p_prePreparingEnlistment)
				{
					p_prePreparingEnlistment.Prepared();
				}

				public void Rollback(Enlistment p_enlEnlistment)
				{
					m_tvsOwner.CompleteTransaction(m_lstSnapshot, true);
					p_enlEnlistment.Done();
				}
			}
		}
	}
}
