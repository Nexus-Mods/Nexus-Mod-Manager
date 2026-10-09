namespace NexusClientTests
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Reflection;
	using System.Runtime.Serialization;
	using ChinhDo.Transactions;
	using Nexus.Client;
	using Nexus.Client.Games;
	using Nexus.Client.ModManagement;
	using Nexus.Client.ModManagement.InstallationLog;
	using Nexus.Client.ModManagement.Scripting;
	using Nexus.Client.ModManagement.Scripting.Operations;
	using Nexus.Client.Mods;
	using Nexus.Client.PluginManagement;
	using Nexus.Client.Settings;
	using Nexus.Client.Util.Collections;
	using Nexus.Transactions;
	using NUnit.Framework;

	/// <summary>
	/// Exercises real Virtual ownership and legacy file removal across revision replacements and native uninstall.
	/// </summary>
	[TestFixture]
	public class VirtualModRemovalRegressionTests
	{
		/// <summary>Exercises the real Virtual switch using Collection-native keys and the persisted staged source.</summary>
		[TestCase(ModInstallRoot.Data)]
		[TestCase(ModInstallRoot.GameRoot)]
		public void ReviewedWinner_UsesInstallLogKeysAndRetainsBothOwners(ModInstallRoot root)
		{
			using (var fixture = new RemovalFixture(root))
			{
				IMod settings = fixture.CreateMod("BundledSettings");
				IMod package = fixture.CreateMod("PreloaderPackage");
				fixture.Install(settings, "xSE PluginPreloader.xml", "settings");
				fixture.Install(package, "xSE PluginPreloader.xml", "defaults");
				string settingsKey = fixture.InstallLog.GetModKey(settings);
				string packageKey = fixture.InstallLog.GetModKey(package);
				ModDeploymentTarget target = ModDeploymentTargetResolver.Resolve(fixture.GameMode, settings, "xSE PluginPreloader.xml", root);
				IVirtualModLink settingsLink = fixture.Activator.VirtualLinks.Single(x => x.ModInfo.ModFileName == Path.GetFileName(settings.Filename));
				Assert.AreNotEqual(FileManagerQueryService.CreateOwnerKey(settingsLink.ModInfo), settingsKey);
				var service = new VirtualDeploymentService(fixture.Activator, fixture.Deployment);

				VirtualFileOwnerSwitchResult result = service.SwitchFileOwner(target, settingsKey);

				Assert.IsTrue(result.Success, result.FailureMessage);
				Assert.AreEqual(settingsKey, result.SelectedOwnerKey);
				Assert.AreEqual("settings", File.ReadAllText(fixture.Deployed("xSE PluginPreloader.xml")));
				CollectionAssert.AreEqual(new[] { packageKey, settingsKey }, fixture.Activator.GetVirtualOwnerKeys(target));
				Assert.IsTrue(service.SwitchFileOwner(target, settingsKey).Success);
				Assert.IsTrue(fixture.Uninstall(settings).Succeeded);
				Assert.AreEqual("defaults", File.ReadAllText(fixture.Deployed("xSE PluginPreloader.xml")));
				Assert.IsTrue(fixture.Uninstall(package).Succeeded);
				Assert.IsFalse(File.Exists(fixture.Deployed("xSE PluginPreloader.xml")));
			}
		}

		/// <summary>Retains File Manager archive/download keys at the legacy Data-only entry point.</summary>
		[Test]
		public void FileManagerWinner_StillAcceptsAndReturnsLegacyOwnerKey()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod first = fixture.CreateMod("FirstOwner");
				IMod second = fixture.CreateMod("SecondOwner");
				fixture.Install(first, "Shared.xml", "first");
				fixture.Install(second, "Shared.xml", "second");
				IVirtualModLink selected = fixture.Activator.VirtualLinks.Single(x => x.ModInfo.ModFileName == Path.GetFileName(first.Filename));
				string legacyKey = FileManagerQueryService.CreateOwnerKey(selected.ModInfo);

				VirtualFileOwnerSwitchResult result = fixture.Activator.SwitchFileOwner("Shared.xml", legacyKey);

				Assert.IsTrue(result.Success, result.FailureMessage);
				Assert.AreEqual(legacyKey, result.SelectedOwnerKey);
				Assert.AreEqual("first", File.ReadAllText(fixture.Deployed("Shared.xml")));
			}
		}

		/// <summary>Uninstalls an existing Direct mod through the native task without requiring a Virtual link target.</summary>
		[TestCase(ModInstallRoot.Data, false)]
		[TestCase(ModInstallRoot.Data, true)]
		[TestCase(ModInstallRoot.GameRoot, false)]
		[TestCase(ModInstallRoot.GameRoot, true)]
		public void ExistingDirectMod_NativeUninstallRemovesFilesAndRestoresOriginal(ModInstallRoot root, bool original)
		{
			using (var fixture = new RemovalFixture(root))
			{
				IMod mod = fixture.CreateMod("ExistingDirect");
				fixture.InstallLog.AddActiveMod(mod, root, ModInstallMethod.Direct);
				string key = fixture.InstallLog.GetModKey(mod);
				ModDeploymentTarget target = ModDeploymentTargetResolver.Resolve(fixture.GameMode, mod, "Existing.esp", root);
				string destination = fixture.Deployed("Existing.esp");
				if (original) File.WriteAllText(destination, "original");
				using (var scope = new TransactionScope())
				{
					fixture.Deployment.InstallDirectFile(mod, target, System.Text.Encoding.UTF8.GetBytes("direct-mod"), new TxFileManager());
					scope.Complete();
				}
				Assert.IsEmpty(fixture.Activator.VirtualLinks);
				Assert.AreEqual(ModInstallMethod.Direct, fixture.InstallLog.GetModInstallMethod(mod));

				Assert.IsTrue(fixture.Uninstall(mod).Succeeded);

				Assert.IsNull(fixture.InstallLog.GetModKey(mod));
				Assert.IsEmpty(fixture.InstallLog.GetDeploymentTargetsForMod(key));
				if (original) Assert.AreEqual("original", File.ReadAllText(destination));
				else Assert.IsFalse(File.Exists(destination));
			}
		}

		/// <summary>Preserves the previous Virtual source when an overwrite is rolled back after winner deactivation.</summary>
		[TestCase(ModInstallRoot.Data)]
		[TestCase(ModInstallRoot.GameRoot)]
		public void VirtualOverwrite_RollbackRestoresPreviousSourceAfterOwnerIsDeactivated(ModInstallRoot root)
		{
			using (var fixture = new RemovalFixture(root))
			{
				IMod previous = fixture.CreateMod("PreviousOwner");
				IMod incoming = fixture.CreateMod("IncomingOwner");
				fixture.Install(previous, "Shared.esp", "previous");
				fixture.InstallLog.AddActiveMod(incoming, root, ModInstallMethod.Virtual);
				string source = fixture.Staged(incoming, "Shared.esp");
				Directory.CreateDirectory(Path.GetDirectoryName(source));
				File.WriteAllText(source, "incoming");
				ModDeploymentTarget target = ModDeploymentTargetResolver.Resolve(fixture.GameMode, previous, "Shared.esp", root);
				using (var scope = new TransactionScope())
				{
					fixture.Activator.DetachVirtualLinkWithoutFallback(target, fixture.InstallLog.GetModKey(previous), new TxFileManager());
					fixture.Activator.DeploySpecificVirtualLink(target, fixture.InstallLog.GetModKey(previous), new TxFileManager());
					fixture.Activator.UpdateLinkListPriority(fixture.Activator.VirtualLinks.ToList());
					fixture.Activator.AddFileLink(incoming, "Shared.esp", source, false, false, false, 0, root);
					Assert.AreEqual("incoming", File.ReadAllText(fixture.Deployed("Shared.esp")));
				}

				Assert.AreEqual("previous", File.ReadAllText(fixture.Deployed("Shared.esp")));
				Assert.IsTrue(new TxFileManager { TxEnabled = false }.IsSameFile(
					fixture.Deployed("Shared.esp"), fixture.Staged(previous, "Shared.esp")));
				Assert.AreEqual(1, fixture.Activator.VirtualLinks.Count);
				Assert.IsTrue(fixture.Uninstall(previous).Succeeded);
				Assert.IsFalse(File.Exists(fixture.Deployed("Shared.esp")));
			}
		}

		[TestCase(ModInstallRoot.Data, false)]
		[TestCase(ModInstallRoot.Data, true)]
		[TestCase(ModInstallRoot.GameRoot, false)]
		[TestCase(ModInstallRoot.GameRoot, true)]
		public void RevisionRoundTrip_ThenNativeUninstall_RemovesCollectionFilesAndRestoresOnlyOriginal(ModInstallRoot root, bool original)
		{
			using (var fixture = new RemovalFixture(root))
			{
				IMod first = fixture.CreateMod("Revision5");
				IMod second = fixture.CreateMod("Revision6");
				string destination = fixture.Deployed("Collection.esp");
				if (original) File.WriteAllText(destination, "pre-existing");
				fixture.Install(first, "Collection.esp", "rev5");
				fixture.Upgrade(first, second, "Collection.esp", "rev6");
				fixture.Upgrade(second, first, "Collection.esp", "rev5-again");

				Assert.AreEqual(1, fixture.Activator.VirtualLinks.Count, "The mod must not retain its own predecessors as fallback owners.");
				Assert.AreEqual("rev5-again", File.ReadAllText(destination));
				Assert.IsTrue(fixture.Uninstall(first).Succeeded);
				Assert.IsEmpty(fixture.Activator.VirtualLinks);
				Assert.IsNull(fixture.InstallLog.GetModKey(first));
				if (original) Assert.AreEqual("pre-existing", File.ReadAllText(destination));
				else Assert.IsFalse(File.Exists(destination));
			}
		}

		/// <summary>Removes the obsolete Data deployment and restores originals when uninstalling from the new root.</summary>
		[TestCase(false, false)]
		[TestCase(false, true)]
		[TestCase(true, false)]
		[TestCase(true, true)]
		public void SameArchiveFolderCorrection_CleansDataAndUninstallsFromRecordedGameRoot(bool dataOriginal, bool gameOriginal)
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod mod = fixture.CreateMod("Preloader");
				string data = fixture.Deployed("WinHTTP.dll");
				string game = Path.Combine(fixture.GameMode.InstallationPath, "WinHTTP.dll");
				if (dataOriginal) File.WriteAllText(data, "data-original");
				if (gameOriginal) File.WriteAllText(game, "game-original");
				fixture.Install(mod, "WinHTTP.dll", "preloader");
				string key = fixture.InstallLog.GetModKey(mod);
				fixture.Upgrade(mod, mod, "WinHTTP.dll", "preloader", replacementRoot: ModInstallRoot.GameRoot);
				Assert.AreEqual(key, fixture.InstallLog.GetModKey(mod));
				Assert.AreEqual(ModInstallRoot.GameRoot, fixture.InstallLog.GetModInstallRoot(mod));
				Assert.AreEqual(ModInstallMethod.Virtual, fixture.InstallLog.GetModInstallMethod(mod));
				Assert.AreEqual("preloader", File.ReadAllText(game));
				Assert.AreEqual(1, fixture.Activator.VirtualLinks.Count);
				Assert.IsTrue(fixture.Activator.VirtualLinks.All(x => x.InstallRoot == ModInstallRoot.GameRoot));
				if (dataOriginal) Assert.AreEqual("data-original", File.ReadAllText(data));
				else Assert.IsFalse(File.Exists(data));
				Assert.IsTrue(fixture.Uninstall(mod).Succeeded);
				if (gameOriginal) Assert.AreEqual("game-original", File.ReadAllText(game));
				else Assert.IsFalse(File.Exists(game));
				if (dataOriginal) Assert.AreEqual("data-original", File.ReadAllText(data));
				else Assert.IsFalse(File.Exists(data));
			}
		}

		/// <summary>Restores the old native root, file bytes and ownership when the folder correction rolls back.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void SameArchiveFolderCorrection_RollbackRestoresDataRootFilesAndOwnership(bool gameOriginal)
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod mod = fixture.CreateMod("PreloaderRollback");
				fixture.Install(mod, "WinHTTP.dll", "old-preloader");
				string key = fixture.InstallLog.GetModKey(mod);
				string game = Path.Combine(fixture.GameMode.InstallationPath, "WinHTTP.dll");
				if (gameOriginal) File.WriteAllText(game, "game-original");
				fixture.Upgrade(mod, mod, "WinHTTP.dll", "new-preloader", false, replacementRoot: ModInstallRoot.GameRoot);
				Assert.AreEqual(key, fixture.InstallLog.GetModKey(mod));
				Assert.AreEqual(ModInstallRoot.Data, fixture.InstallLog.GetModInstallRoot(mod));
				Assert.AreEqual("old-preloader", File.ReadAllText(fixture.Deployed("WinHTTP.dll")));
				Assert.AreEqual(1, fixture.Activator.VirtualLinks.Count);
				Assert.IsTrue(fixture.Activator.VirtualLinks.All(x => x.InstallRoot == ModInstallRoot.Data));
				if (gameOriginal) Assert.AreEqual("game-original", File.ReadAllText(game));
				else Assert.IsFalse(File.Exists(game));
			}
		}

		[Test]
		public void SameArchiveReinstall_KeepsReinstalledStreamAndRemovesObsoletePlugin()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod mod = fixture.CreateMod("SameArchive");
				fixture.Install(mod, "Keep.esp", "old");
				fixture.Install(mod, "Removed.esp", "obsolete");
				fixture.Upgrade(mod, mod, "Keep.esp", "new");

				Assert.IsTrue(File.Exists(fixture.Staged(mod, "Keep.esp")));
				Assert.AreEqual("new", File.ReadAllText(fixture.Deployed("Keep.esp")));
				Assert.IsFalse(File.Exists(fixture.Deployed("Removed.esp")));
				Assert.AreEqual(1, fixture.Activator.VirtualLinks.Count);
				CollectionAssert.Contains(fixture.RemovedPlugins, fixture.Deployed("Removed.esp"));
			}
		}

		[TestCase(false)]
		[TestCase(true)]
		public void PreparedRecipe_ReusedStagingSurvivesWhileObsoleteFilesAreRemoved(bool generated)
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod mod = fixture.CreateMod("ReusedRecipe");
				fixture.Install(mod, "Reused.esp", "cached");
				fixture.Install(mod, "Obsolete.esp", "obsolete");
				fixture.Upgrade(mod, mod, "Reused.esp", "cached", true, generated);
				Assert.AreEqual("cached", File.ReadAllText(fixture.Staged(mod, "Reused.esp")));
				Assert.AreEqual("cached", File.ReadAllText(fixture.Deployed("Reused.esp")));
				Assert.IsFalse(File.Exists(fixture.Deployed("Obsolete.esp")));
				Assert.IsTrue(fixture.Uninstall(mod).Succeeded);
				Assert.IsFalse(File.Exists(fixture.Deployed("Reused.esp")));
			}
		}

		[Test]
		public void ChangedArchiveUpgrade_RemovesObsoleteDeploymentBeforeRebindingOwner()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod oldMod = fixture.CreateMod("OldArchive");
				IMod newMod = fixture.CreateMod("NewArchive");
				fixture.Install(oldMod, "Keep.esp", "old");
				fixture.Install(oldMod, "Removed.esp", "obsolete");
				fixture.Upgrade(oldMod, newMod, "Keep.esp", "new");

				Assert.IsFalse(File.Exists(fixture.Deployed("Removed.esp")));
				Assert.AreEqual(1, fixture.Activator.VirtualLinks.Count);
				Assert.IsTrue(fixture.Uninstall(newMod).Succeeded);
				Assert.IsFalse(File.Exists(fixture.Deployed("Keep.esp")));
			}
		}

		[Test]
		public void ExistingDuplicateOwnerRecords_NativeUninstallRemovesAllRecordsAndPhysicalFile()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod mod = fixture.CreateMod("LegacyDuplicates");
				fixture.Install(mod, "Duplicate.esp", "mod");
				fixture.Activator.VirtualLinks[0].Active = false;
				fixture.Activator.AddFileLink(mod, "Duplicate.esp", fixture.Staged(mod, "Duplicate.esp"), false, false, false, 0, ModInstallRoot.Data);
				Assert.AreEqual(2, fixture.Activator.VirtualLinks.Count);

				Assert.IsTrue(fixture.Uninstall(mod).Succeeded);
				Assert.IsEmpty(fixture.Activator.VirtualLinks);
				Assert.IsFalse(File.Exists(fixture.Deployed("Duplicate.esp")));
			}
		}

		[Test]
		public void DanglingVirtualLink_NativeUninstallRemovesTheFilesystemEntry()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod mod = fixture.CreateMod("Dangling");
				fixture.Install(mod, "Dangling.esp", "mod");
				string destination = fixture.Deployed("Dangling.esp");
				string source = fixture.Staged(mod, "Dangling.esp");
				File.Delete(destination);
				var files = new TxFileManager { TxEnabled = false };
				if (!files.CreateSymbolicLink(destination, source))
					Assert.Ignore("Symbolic links are not available in this test environment.");
				File.Delete(source);
				Assert.IsFalse(File.Exists(destination));
				Assert.AreEqual(FileEntryKind.SymbolicLink, files.GetFileEntryKind(destination, source));
				Assert.IsTrue(fixture.Uninstall(mod).Succeeded);
				Assert.AreEqual(FileEntryKind.Absent, files.GetFileEntryKind(destination, source));
			}
		}

		[Test]
		public void AbortedReplacement_RestoresPreviousOwnerContentAndOriginalBackup()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod oldMod = fixture.CreateMod("BeforeAbort");
				IMod newMod = fixture.CreateMod("AfterAbort");
				File.WriteAllText(fixture.Deployed("Rollback.esp"), "original");
				fixture.Install(oldMod, "Rollback.esp", "old");
				fixture.Upgrade(oldMod, newMod, "Rollback.esp", "new", false);

				Assert.AreEqual("old", File.ReadAllText(fixture.Deployed("Rollback.esp")));
				Assert.AreEqual(1, fixture.Activator.VirtualLinks.Count);
				Assert.AreEqual(Path.GetFileName(oldMod.Filename), fixture.Activator.VirtualLinks[0].ModInfo.ModFileName);
				Assert.IsTrue(fixture.Uninstall(oldMod).Succeeded);
				Assert.AreEqual("original", File.ReadAllText(fixture.Deployed("Rollback.esp")));
			}
		}

		[Test]
		public void AbortedSameArchiveHardlinkReplacement_RestoresOriginalBytesAndTopology()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod mod = fixture.CreateMod("HardlinkRollback");
				fixture.Install(mod, "Rollback.esp", "old");
				string destination = fixture.Deployed("Rollback.esp");
				string source = fixture.Staged(mod, "Rollback.esp");
				File.Delete(destination);
				var files = new TxFileManager { TxEnabled = false };
				if (!files.CreateHardLink(destination, source))
					Assert.Ignore("Hardlinks are not available in this test environment.");
				fixture.Upgrade(mod, mod, "Rollback.esp", "new", false);
				Assert.AreEqual("old", File.ReadAllText(source));
				Assert.AreEqual("old", File.ReadAllText(destination));
				Assert.AreEqual(FileEntryKind.HardLink, files.GetFileEntryKind(destination, source));
				Assert.AreEqual(1, fixture.Activator.VirtualLinks.Count);
				Assert.IsTrue(fixture.Uninstall(mod).Succeeded);
				Assert.IsFalse(File.Exists(destination));
			}
		}

		[TestCase(false)]
		[TestCase(true)]
		public void LegacyFileRemoval_ProtectedFileFailsWithoutDroppingOwnership(bool readOnly)
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod mod = fixture.CreateMod("Protected");
				fixture.InstallLog.AddActiveMod(mod, ModInstallRoot.Data, ModInstallMethod.Virtual);
				fixture.InstallLog.AddDataFile(mod, "Protected.esp");
				string path = fixture.Deployed("Protected.esp");
				File.WriteAllText(path, "keep tracking");
				try
				{
					if (readOnly) File.SetAttributes(path, FileAttributes.ReadOnly);
					using (FileStream locked = readOnly ? null : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
					using (var scope = new TransactionScope())
						Assert.Throws<IOException>(() => fixture.LegacyInstaller(mod).UninstallDataFile("Protected.esp"));
					CollectionAssert.Contains(fixture.InstallLog.GetInstalledModFiles(mod), "Protected.esp");
					Assert.IsTrue(File.Exists(path));
				}
				finally { File.SetAttributes(path, FileAttributes.Normal); }
			}
		}

		[Test]
		public void LegacyBackupRestoreFailure_RollsBackRemovedFileAndPreservesBackupAndOwnership()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod mod = fixture.CreateMod("RestoreFailure");
				fixture.InstallLog.AddActiveMod(mod, ModInstallRoot.Data, ModInstallMethod.Virtual);
				fixture.InstallLog.LogOriginalDataFile("Restore.esp");
				fixture.InstallLog.AddDataFile(mod, "Restore.esp");
				string path = fixture.Deployed("Restore.esp");
				string backup = Path.Combine(fixture.OverwritePath, fixture.InstallLog.OriginalValuesKey + "_Restore.esp");
				File.WriteAllText(path, "mod");
				File.WriteAllText(backup, "original");
				using (var locked = new FileStream(backup, FileMode.Open, FileAccess.Read, FileShare.None))
				using (var scope = new TransactionScope())
					Assert.Catch<Exception>(() => fixture.LegacyInstaller(mod).UninstallDataFile("Restore.esp"));

				Assert.AreEqual("mod", File.ReadAllText(path));
				Assert.AreEqual("original", File.ReadAllText(backup));
				CollectionAssert.Contains(fixture.InstallLog.GetInstalledModFiles(mod), "Restore.esp");
			}
		}

		[Test]
		public void NativeUninstall_ReportsLockedFileAndKeepsRegistrationForRetry()
		{
			using (var fixture = new RemovalFixture(ModInstallRoot.Data))
			{
				IMod mod = fixture.CreateMod("NativeLocked");
				fixture.InstallLog.AddActiveMod(mod, ModInstallRoot.Data, ModInstallMethod.Virtual);
				fixture.InstallLog.AddDataFile(mod, "Locked.esp");
				string path = fixture.Deployed("Locked.esp");
				File.WriteAllText(path, "locked");
				using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
				{
					SynchronousUninstaller result = fixture.Uninstall(mod);
					Assert.IsFalse(result.Succeeded);
					StringAssert.Contains("Locked.esp", result.CompletionMessage);
					Assert.IsNotNull(fixture.InstallLog.GetModKey(mod));
					CollectionAssert.Contains(fixture.InstallLog.GetInstalledModFiles(mod), "Locked.esp");
				}
				Assert.IsTrue(fixture.Uninstall(mod).Succeeded);
				Assert.IsFalse(File.Exists(path));
			}
		}

		/// <summary>
		/// Provides isolated filesystem roots and real native deployment services without a running main form.
		/// </summary>
		private sealed class RemovalFixture : IDisposable
		{
			private readonly string m_root = Path.Combine(Path.GetTempPath(), "NMM-removal-regression-" + Guid.NewGuid().ToString("N"));
			private readonly ThreadSafeObservableList<IMod> m_mods;
			private readonly ModInstallRoot m_installRoot;
			private readonly IGameModeEnvironmentInfo m_gameInfo;
			/// <summary>
			/// Creates an isolated native deployment environment for the requested install root.
			/// </summary>
			public RemovalFixture(ModInstallRoot root)
			{
				m_installRoot = root;
				DataPath = Path.Combine(m_root, "Game", "Data");
				string gamePath = Path.Combine(m_root, "Game");
				string installInfo = Path.Combine(m_root, "InstallInfo");
				OverwritePath = Path.Combine(installInfo, "overwrites");
				ModPath = Path.Combine(m_root, "Mods");
				foreach (string path in new[] { DataPath, installInfo, OverwritePath, ModPath }) Directory.CreateDirectory(path);
				m_gameInfo = InterfaceStub<IGameModeEnvironmentInfo>.Create((method, args) =>
				{
					switch (method.Name)
					{
						case "get_InstallationPath": return DataPath;
						case "get_InstallInfoDirectory": return installInfo;
						case "get_OverwriteDirectory": return OverwritePath;
						default: return null;
					}
				});
				GameMode = InterfaceStub<IGameMode>.Create((method, args) =>
				{
					switch (method.Name)
					{
						case "get_ModeId": return "fallout4";
						case "get_GameModeEnvironmentInfo": return m_gameInfo;
						case "get_InstallationPath": return gamePath;
						case "get_PluginDirectory": return DataPath;
						case "get_UsesPlugins":
						case "RealFileRequired": return true;
						case "GetModFormatAdjustedPath": return args[1];
						default: return null;
					}
				});
				var folders = new PerGameModeSettings<string>(); folders["fallout4"] = Path.Combine(m_root, "Virtual");
				var linkFolders = new PerGameModeSettings<string>(); linkFolders["fallout4"] = String.Empty;
				var multiHd = new PerGameModeSettings<bool>(); multiHd["fallout4"] = false;
				ISettings settings = InterfaceStub<ISettings>.Create((method, args) =>
				{
					switch (method.Name)
					{
						case "get_VirtualFolder": return folders;
						case "get_HDLinkFolder": return linkFolders;
						case "get_MultiHDInstall": return multiHd;
						default: return null;
					}
				});
				Environment = InterfaceStub<IEnvironmentInfo>.Create((method, args) => method.Name == "get_Settings" ? settings : null);
				Plugins = InterfaceStub<IPluginManager>.Create((method, args) =>
				{
					if (method.Name == "IsActivatiblePluginFile") return Path.GetExtension((string)args[0]) == ".esp";
					if (method.Name == "RemovePlugins") RemovedPlugins.AddRange((IEnumerable<string>)args[0]);
					return null;
				});
				var registry = new ModRegistry(null, GameMode);
				m_mods = (ThreadSafeObservableList<IMod>)typeof(ModRegistry).GetField("m_oclRegisteredMods", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(registry);
				ConstructorInfo constructor = typeof(InstallLog).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
					new[] { typeof(ModRegistry), typeof(IGameMode), typeof(string), typeof(string) }, null);
				InstallLog = (InstallLog)constructor.Invoke(new object[] { registry, null, ModPath, Path.Combine(installInfo, "InstallLog.xml") });
				var manager = (ModManager)FormatterServices.GetUninitializedObject(typeof(ModManager));
				SetField(manager, "<GameMode>k__BackingField", GameMode);
				SetField(manager, "<InstallationLog>k__BackingField", InstallLog);
				SetField(manager, "<ManagedModRegistry>k__BackingField", registry);
				SetField(manager, "<EnvironmentInfo>k__BackingField", Environment);
				Activator = new VirtualModActivator(manager, Plugins, GameMode, InstallLog, Environment, ModPath);
				SetField(manager, "m_vmaVirtualModActivator", Activator);
				Deployment = new ModDeploymentManager(InstallLog, Activator, GameMode);
				SetField(manager, "m_mdmDeploymentManager", Deployment);
			}

			public string DataPath { get; private set; }
			public string ModPath { get; private set; }
			public string OverwritePath { get; private set; }
			public IGameMode GameMode { get; private set; }
			public IEnvironmentInfo Environment { get; private set; }
			public IPluginManager Plugins { get; private set; }
			public InstallLog InstallLog { get; private set; }
			public VirtualModActivator Activator { get; private set; }
			public ModDeploymentManager Deployment { get; private set; }
			public List<string> RemovedPlugins { get; private set; } = new List<string>();
			/// <summary>
			/// Registers a library archive that can participate in native replacement.
			/// </summary>
			public IMod CreateMod(string name)
			{
				var mod = new InstallLog.DummyMod(name, Path.Combine(ModPath, name + ".7z"));
				m_mods.Add(mod);
				return mod;
			}
			/// <summary>
			/// Resolves the archive staging path inside this fixture.
			/// </summary>
			public string Staged(IMod mod, string file) { return Path.Combine(Activator.VirtualPath, Path.GetFileNameWithoutExtension(mod.Filename), file); }
			/// <summary>
			/// Resolves the game destination for the captured install root.
			/// </summary>
			public string Deployed(string file) { return Path.Combine(m_installRoot == ModInstallRoot.GameRoot ? GameMode.InstallationPath : DataPath, file); }
			/// <summary>
			/// Creates the legacy file remover with the fixture ownership log.
			/// </summary>
			public ModFileInstaller LegacyInstaller(IMod mod)
			{
				return new ModFileInstaller(m_gameInfo, mod, InstallLog, Plugins, InterfaceStub<IDataFileUtil>.Create((method, args) => null),
					new TxFileManager(), null, true, Environment, DataPath);
			}
			/// <summary>
			/// Stages and deploys one recorded Virtual file through the production link installer.
			/// </summary>
			public void Install(IMod mod, string file, string content)
			{
				InstallLog.AddActiveMod(mod, m_installRoot, ModInstallMethod.Virtual);
				string source = Staged(mod, file); Directory.CreateDirectory(Path.GetDirectoryName(source)); File.WriteAllText(source, content);
				InstallLog.AddDataFile(mod, source);
				Activator.GetModLinkInstaller().AddFileLink(mod, file, source, true, false, m_installRoot);
				Activator.SaveList(false);
			}
			/// <summary>
			/// Runs stream staging, file finalization, and deployment finalization in one native transaction.
			/// </summary>
			public void Upgrade(IMod oldMod, IMod newMod, string file, string content, bool commit = true, bool? reuseGenerated = null, ModInstallRoot? replacementRoot = null)
			{
				ModInstallRoot destinationRoot = replacementRoot ?? m_installRoot;
				var finalizer = new UpgradeFinalizer(oldMod, newMod, this, destinationRoot);
				using (var scope = new TransactionScope())
				{
					InstallLog.ReplaceActiveMod(oldMod, newMod, destinationRoot, ModInstallMethod.Virtual);
					var installer = new StreamUpgradeInstaller(newMod, this);
					string source = Staged(newMod, file); Directory.CreateDirectory(Path.GetDirectoryName(source));
					if (reuseGenerated.HasValue)
					{
						ScriptedInstallOperation operation = reuseGenerated.Value
							? (ScriptedInstallOperation)new GenerateDataFileOperation(file, System.Text.Encoding.UTF8.GetBytes(content), source, false, new ModLinkInstallDecision(true))
							: new InstallModFileOperation(file, file, source, false, new ModLinkInstallDecision(true));
						installer.RetainPreparedStagingFiles(new[] { operation });
					}
					else
						installer.WriteStream(source, content);
					Activator.GetModLinkInstaller().AddFileLink(newMod, file, source, true, false, destinationRoot);
					installer.FinalizeInstall();
					finalizer.Finish(new TxFileManager());
					if (commit) scope.Complete();
				}
			}
			/// <summary>
			/// Runs the production native uninstall without a background worker.
			/// </summary>
			public SynchronousUninstaller Uninstall(IMod mod)
			{
				var uninstaller = new SynchronousUninstaller(mod, this); uninstaller.Execute(); return uninstaller;
			}
			/// <summary>
			/// Removes the fixture files after the test.
			/// </summary>
			public void Dispose() { if (Directory.Exists(m_root)) Directory.Delete(m_root, true); }
			/// <summary>
			/// Binds the uninitialized manager shell to the native fixture services.
			/// </summary>
			private static void SetField(object target, string name, object value) { typeof(ModManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value); }

			/// <summary>Exposes streamed staging writes through the production upgrade installer.</summary>
			private sealed class StreamUpgradeInstaller : ModFileUpgradeInstaller
			{
				/// <summary>
				/// Creates the upgrade file installer for the fixture native owner.
				/// </summary>
				public StreamUpgradeInstaller(IMod mod, RemovalFixture fixture)
					: base(fixture.m_gameInfo, mod, fixture.InstallLog, fixture.Plugins,
						InterfaceStub<IDataFileUtil>.Create((method, args) => null), new TxFileManager(), null, true, fixture.Environment)
				{ UpgradeDeploymentBackend = fixture.Activator; }
				/// <summary>
				/// Writes an archive stream through the production upgrade file path.
				/// </summary>
				public void WriteStream(string path, string content)
				{
					string payload = Path.Combine(Path.GetDirectoryName(path), Guid.NewGuid().ToString("N") + ".payload");
					File.WriteAllText(payload, content);
					try { using (FileStream stream = File.OpenRead(payload)) Assert.IsTrue(GenerateDataFileWithResolvedOverwrite(path, stream)); }
					finally { File.Delete(payload); }
				}
			}
			/// <summary>Exposes native upgrade deployment finalization without starting its worker.</summary>
			private sealed class UpgradeFinalizer : ModUpgrader
			{
				/// <summary>
				/// Creates the production upgrade deployment finalizer.
				/// </summary>
				public UpgradeFinalizer(IMod oldMod, IMod newMod, RemovalFixture fixture, ModInstallRoot root)
					: base(oldMod, newMod, fixture.GameMode, fixture.Environment, null, null, fixture.InstallLog, fixture.Plugins,
						fixture.Activator, fixture.Deployment, null, null, new ModInstallContext(ModInstallMethod.Virtual, root)) { }
				/// <summary>
				/// Runs the production obsolete deployment cleanup and owner rebind.
				/// </summary>
				public void Finish(TxFileManager files) { FinalizeDeploymentAfterInstall(files); }
			}
		}

		/// <summary>
		/// Executes the native uninstall body on the test thread without starting a background worker.
		/// </summary>
		private sealed class SynchronousUninstaller : ModUninstaller
		{
			/// <summary>
			/// Creates the native uninstall task using the fixture services.
			/// </summary>
			public SynchronousUninstaller(IMod mod, RemovalFixture fixture)
				: base(mod, fixture.GameMode, fixture.Environment, fixture.Activator, fixture.Deployment,
					fixture.InstallLog, fixture.Plugins, fixture.InstallLog.ActiveMods) { }
			/// <summary>
			/// Runs the protected native uninstall body on the current test thread.
			/// </summary>
			public void Execute() { RunTasks(); }
		}
	}
}
