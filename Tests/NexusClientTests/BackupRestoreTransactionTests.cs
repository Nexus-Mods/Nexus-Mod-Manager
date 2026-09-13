namespace NexusClientTests
{
	using System;
	using System.Collections.Generic;
	using System.ComponentModel;
	using System.IO;
	using System.Reflection;
	using System.Runtime.Serialization;

	using ChinhDo.Transactions;
	using Nexus.Client;
	using Nexus.Client.Games;
	using Nexus.Client.ModManagement;
	using Nexus.Client.ModManagement.InstallationLog;
	using Nexus.Client.Mods;
	using Nexus.Client.PluginManagement;
	using Nexus.Client.Settings;
	using Nexus.Client.Util.Collections;
	using Nexus.Transactions;

	using NUnit.Framework;

	/// <summary>
	/// Verifies that backup restore joins the transaction system required by coordinator-facing Virtual deployment operations.
	/// </summary>
	[TestFixture]
	public class BackupRestoreTransactionTests
	{
		/// <summary>
		/// Restores a promoted Virtual winner through the real VMA backend and requires the NMM ambient transaction.
		/// </summary>
		[Test]
		public void RestoreVirtualPromotedWinners_UsesNexusTransactionWithRealVmaBackend()
		{
			string root = Path.Combine(Path.GetTempPath(), "NMM-BackupRestoreTransaction-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			try
			{
				const string modeId = "TestMode";
				const string ownerKey = "VirtualOwner";
				string dataPath = Path.Combine(root, "Data");
				string gamePath = Path.Combine(root, "Game");
				string modFolder = Path.Combine(root, "Mods");
				string virtualRoot = Path.Combine(root, "VirtualRoot");
				Directory.CreateDirectory(dataPath);
				Directory.CreateDirectory(gamePath);
				Directory.CreateDirectory(modFolder);

				IGameMode gameMode = CreateGameMode(modeId, gamePath, dataPath);
				IEnvironmentInfo environmentInfo = CreateEnvironmentInfo(modeId, virtualRoot);
				IInstallLog installLog = CreateInstallLog(ownerKey);
				IMod mod = CreateMod(Path.Combine(modFolder, "Virtual.7z"));
				ModManager modManager = CreateModManagerShell(gameMode, installLog, mod);
				var virtualModActivator = new VirtualModActivator(
					modManager,
					InterfaceStub<IPluginManager>.Create((method, args) => null),
					gameMode,
					installLog,
					environmentInfo,
					modFolder);

				ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "winner.txt");
				string stagedSource = Path.Combine(virtualRoot, VirtualModActivator.ACTIVATOR_FOLDER, "Virtual", "winner.txt");
				Directory.CreateDirectory(Path.GetDirectoryName(stagedSource));
				File.WriteAllText(stagedSource, "virtual-winner");

				using (TransactionScope transaction = new TransactionScope())
				{
					virtualModActivator.RegisterVirtualLink(target, mod, "winner.txt", stagedSource, ModInstallRoot.Data, 0);
					transaction.Complete();
				}

				IModDeploymentManager deploymentManager = InterfaceStub<IModDeploymentManager>.Create((method, args) =>
				{
					switch (method.Name)
					{
						case "get_HasPromotedTargets":
							return true;
						case "GetPromotedTargets":
							return (IReadOnlyCollection<ModDeploymentTarget>)new[] { target };
						case "GetOwnerKeys":
							return (IReadOnlyList<string>)new[] { ownerKey };
						default:
							return null;
					}
				});

				SetField(modManager, "m_vmaVirtualModActivator", virtualModActivator);
				SetField(modManager, "m_mdmDeploymentManager", deploymentManager);

				var restoreTask = (RestoreBackupTask)FormatterServices.GetUninitializedObject(typeof(RestoreBackupTask));
				SetField(restoreTask, "ModManager", modManager);
				MethodInfo restore = typeof(RestoreBackupTask).GetMethod(
					"RestoreVirtualPromotedWinners",
					BindingFlags.Instance | BindingFlags.NonPublic);

				Assert.NotNull(restore);
				Assert.DoesNotThrow(() => restore.Invoke(restoreTask, null));
				Assert.AreEqual("virtual-winner", File.ReadAllText(Path.Combine(dataPath, "winner.txt")));
			}
			finally
			{
				if (Directory.Exists(root))
					Directory.Delete(root, true);
			}
		}

		/// <summary>
		/// Cancels an in-place Virtual winner replacement through the real VMA backend and restores the original symbolic-link target.
		/// </summary>
		[Test]
		public void SameOwnerVirtualReplacement_Cancelled_RestoresOriginalSymbolicLinkTarget()
		{
			string root = Path.Combine(Path.GetTempPath(), "NMM-VirtualReplacementRollback-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			try
			{
				const string modeId = "TestMode";
				const string ownerKey = "VirtualOwner";
				string dataPath = Path.Combine(root, "Data");
				string gamePath = Path.Combine(root, "Game");
				string modFolder = Path.Combine(root, "Mods");
				string virtualRoot = Path.Combine(root, "VirtualRoot");
				Directory.CreateDirectory(dataPath);
				Directory.CreateDirectory(gamePath);
				Directory.CreateDirectory(modFolder);

				IGameMode gameMode = CreateGameMode(modeId, gamePath, dataPath, false);
				IEnvironmentInfo environmentInfo = CreateEnvironmentInfo(modeId, virtualRoot, true);
				IInstallLog installLog = CreateInstallLog(ownerKey);
				IMod mod = CreateMod(Path.Combine(modFolder, "Virtual.7z"));
				ModManager modManager = CreateModManagerShell(gameMode, installLog, mod);
				var virtualModActivator = new VirtualModActivator(
					modManager,
					InterfaceStub<IPluginManager>.Create((method, args) => null),
					gameMode,
					installLog,
					environmentInfo,
					modFolder);

				ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "winner.txt");
				string originalSource = Path.Combine(virtualRoot, VirtualModActivator.ACTIVATOR_FOLDER, "Virtual", "winner.txt");
				string replacementSource = Path.Combine(virtualRoot, VirtualModActivator.ACTIVATOR_FOLDER, "VirtualReplacement", "winner.txt");
				Directory.CreateDirectory(Path.GetDirectoryName(originalSource));
				Directory.CreateDirectory(Path.GetDirectoryName(replacementSource));
				File.WriteAllText(originalSource, "original");
				File.WriteAllText(replacementSource, "replacement");

				try
				{
					using (TransactionScope transaction = new TransactionScope())
					{
						virtualModActivator.RegisterVirtualLink(target, mod, "winner.txt", originalSource, ModInstallRoot.Data, 0);
						virtualModActivator.DeploySpecificVirtualLink(target, ownerKey, new TxFileManager());
						transaction.Complete();
					}
				}
				catch (IOException ex)
				{
					Win32Exception nativeError = ex.InnerException as Win32Exception;
					if (nativeError != null && (nativeError.NativeErrorCode == 1314 || nativeError.NativeErrorCode == 50))
						Assert.Ignore("Symbolic-link setup requires Windows symlink privileges or a supported filesystem: " + ex.Message);
					throw;
				}

				string deployedPath = Path.Combine(dataPath, "winner.txt");
				var topology = new TxFileManager { TxEnabled = false };
				Assert.AreEqual(FileEntryKind.SymbolicLink, topology.GetFileEntryKind(deployedPath, originalSource));
				Assert.IsTrue(topology.IsSameFile(deployedPath, originalSource));

				using (var transaction = new TransactionScope())
				{
					var fileManager = new TxFileManager();
					virtualModActivator.DetachVirtualLinkWithoutFallback(target, ownerKey, fileManager);
					virtualModActivator.RemoveVirtualLinkRecord(target, ownerKey);
					virtualModActivator.RegisterVirtualLink(target, mod, "winner.txt", replacementSource, ModInstallRoot.Data, 0);
					virtualModActivator.DeploySpecificVirtualLink(target, ownerKey, fileManager);

					Assert.AreEqual(FileEntryKind.SymbolicLink, topology.GetFileEntryKind(deployedPath, replacementSource));
					Assert.IsTrue(topology.IsSameFile(deployedPath, replacementSource));
				}

				Assert.AreEqual(FileEntryKind.SymbolicLink, topology.GetFileEntryKind(deployedPath, originalSource));
				Assert.IsTrue(topology.IsSameFile(deployedPath, originalSource));
				Assert.IsFalse(topology.IsSameFile(deployedPath, replacementSource));
				Assert.AreEqual("original", File.ReadAllText(deployedPath));
			}
			finally
			{
				if (Directory.Exists(root))
					Directory.Delete(root, true);
			}
		}

		/// <summary>
		/// Restoring sibling and nested Virtual payloads must not delete files restored earlier in the same directory tree.
		/// </summary>
		[Test]
		public void RestorePayloadFile_PreservesSiblingAndNestedFiles()
		{
			string root = Path.Combine(Path.GetTempPath(), "NMM-BackupPayloadSiblings-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			try
			{
				string sourceRoot = Path.Combine(root, "Backup");
				string destinationRoot = Path.Combine(root, "VirtualInstall");
				Directory.CreateDirectory(sourceRoot);
				Directory.CreateDirectory(Path.Combine(destinationRoot, "ModA", "textures"));

				string sourceA = Path.Combine(sourceRoot, "a.dds");
				string sourceB = Path.Combine(sourceRoot, "b.dds");
				string sourceNested = Path.Combine(sourceRoot, "nested.dds");
				File.WriteAllText(sourceA, "a");
				File.WriteAllText(sourceB, "b");
				File.WriteAllText(sourceNested, "nested");

				string sibling = Path.Combine(destinationRoot, "ModA", "textures", "existing.dds");
				File.WriteAllText(sibling, "existing");

				using (TransactionScope transaction = new TransactionScope())
				{
					var fileManager = new TxFileManager();
					InvokeRestorePayloadFile(fileManager, sourceA, Path.Combine(destinationRoot, "ModA", "textures", "a.dds"));
					InvokeRestorePayloadFile(fileManager, sourceB, Path.Combine(destinationRoot, "ModA", "textures", "b.dds"));
					InvokeRestorePayloadFile(fileManager, sourceNested, Path.Combine(destinationRoot, "ModA", "textures", "nested", "c.dds"));
					transaction.Complete();
				}

				Assert.AreEqual("a", File.ReadAllText(Path.Combine(destinationRoot, "ModA", "textures", "a.dds")));
				Assert.AreEqual("b", File.ReadAllText(Path.Combine(destinationRoot, "ModA", "textures", "b.dds")));
				Assert.AreEqual("nested", File.ReadAllText(Path.Combine(destinationRoot, "ModA", "textures", "nested", "c.dds")));
				Assert.AreEqual("existing", File.ReadAllText(sibling));
			}
			finally
			{
				if (Directory.Exists(root))
					Directory.Delete(root, true);
			}
		}

		/// <summary>
		/// A failed non-purge restore must restore overwritten payloads and remove payloads newly created by that restore.
		/// </summary>
		[Test]
		public void RestorePayloadFile_AbortedTransactionRestoresOverlayState()
		{
			string root = Path.Combine(Path.GetTempPath(), "NMM-BackupPayloadRollback-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			try
			{
				string sourceRoot = Path.Combine(root, "Backup");
				string destinationRoot = Path.Combine(root, "VirtualInstall");
				Directory.CreateDirectory(sourceRoot);
				Directory.CreateDirectory(destinationRoot);

				string replacementSource = Path.Combine(sourceRoot, "replacement.bin");
				string newSource = Path.Combine(sourceRoot, "new.bin");
				string existingDestination = Path.Combine(destinationRoot, "existing.bin");
				string newDestination = Path.Combine(destinationRoot, "nested", "new.bin");
				File.WriteAllText(replacementSource, "replacement");
				File.WriteAllText(newSource, "new");
				File.WriteAllText(existingDestination, "original");

				using (new TransactionScope())
				{
					var fileManager = new TxFileManager();
					InvokeRestorePayloadFile(fileManager, replacementSource, existingDestination);
					InvokeRestorePayloadFile(fileManager, newSource, newDestination);
					Assert.AreEqual("replacement", File.ReadAllText(existingDestination));
					Assert.AreEqual("new", File.ReadAllText(newDestination));
				}

				Assert.AreEqual("original", File.ReadAllText(existingDestination));
				Assert.IsFalse(File.Exists(newDestination));
				Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(newDestination)));
			}
			finally
			{
				if (Directory.Exists(root))
					Directory.Delete(root, true);
			}
		}

		/// <summary>
		/// A required Direct/mixed deployment payload that disappears after enumeration must fail backup creation explicitly.
		/// </summary>
		[Test]
		public void CopyInstalledBackupFile_RequiredDeploymentPayloadMissing_Throws()
		{
			string root = Path.Combine(Path.GetTempPath(), "NMM-RequiredBackupPayload-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			try
			{
				string source = Path.Combine(root, "missing.bin");
				string destination = Path.Combine(root, "backup.bin");
				var backupInfo = new BackupInfo("Data\\required.bin", source, String.Empty, Path.Combine("DEPLOYMENT", "active"), 4, true);

				TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() => InvokeCopyInstalledBackupFile(backupInfo, destination));
				Assert.IsInstanceOf<FileNotFoundException>(exception.InnerException);
				Assert.AreEqual(source, ((FileNotFoundException)exception.InnerException).FileName);
			}
			finally
			{
				if (Directory.Exists(root))
					Directory.Delete(root, true);
			}
		}

		/// <summary>
		/// Missing legacy installed-file payloads retain the existing best-effort backup behavior.
		/// </summary>
		[Test]
		public void CopyInstalledBackupFile_OptionalLegacyPayloadMissing_IsIgnored()
		{
			string root = Path.Combine(Path.GetTempPath(), "NMM-OptionalBackupPayload-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			try
			{
				string destination = Path.Combine(root, "backup.bin");
				var backupInfo = new BackupInfo("optional.bin", Path.Combine(root, "missing.bin"), String.Empty, "VIRTUAL INSTALL", 4);

				Assert.DoesNotThrow(() => InvokeCopyInstalledBackupFile(backupInfo, destination));
				Assert.IsFalse(File.Exists(destination));
			}
			finally
			{
				if (Directory.Exists(root))
					Directory.Delete(root, true);
			}
		}

		/// <summary>
		/// Required deployment payload validation rejects a temporary backup whose copied bytes do not match the captured size.
		/// </summary>
		[Test]
		public void ValidateRequiredDeploymentPayloads_SizeMismatch_Throws()
		{
			string root = Path.Combine(Path.GetTempPath(), "NMM-BackupPayloadValidation-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			try
			{
				var backupInfo = new BackupInfo("Data\\required.bin", Path.Combine(root, "source.bin"), String.Empty, Path.Combine("DEPLOYMENT", "active"), 8, true);
				string destination = Path.Combine(root, backupInfo.Directory, backupInfo.VirtualModPath);
				Directory.CreateDirectory(Path.GetDirectoryName(destination));
				File.WriteAllBytes(destination, new byte[4]);

				var backupManager = (BackupManager)FormatterServices.GetUninitializedObject(typeof(BackupManager));
				backupManager.lstInstalledModFiles = new List<BackupInfo> { backupInfo };
				var createTask = (CreateBackupTask)FormatterServices.GetUninitializedObject(typeof(CreateBackupTask));
				SetField(createTask, "BackupManager", backupManager);

				TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() => InvokeValidateRequiredDeploymentPayloads(createTask, root));
				Assert.IsInstanceOf<InvalidDataException>(exception.InnerException);
			}
			finally
			{
				if (Directory.Exists(root))
					Directory.Delete(root, true);
			}
		}

		/// <summary>
		/// Invokes the installed-file backup copy primitive while keeping it private to CreateBackupTask.
		/// </summary>
		private static void InvokeCopyInstalledBackupFile(BackupInfo p_bifBackupInfo, string p_strDestinationPath)
		{
			MethodInfo copyPayload = typeof(CreateBackupTask).GetMethod(
				"CopyInstalledBackupFile",
				BindingFlags.Static | BindingFlags.NonPublic);
			Assert.NotNull(copyPayload);
			copyPayload.Invoke(null, new object[] { p_bifBackupInfo, p_strDestinationPath });
		}

		/// <summary>
		/// Invokes required deployment-payload validation while keeping it private to CreateBackupTask.
		/// </summary>
		private static void InvokeValidateRequiredDeploymentPayloads(CreateBackupTask p_cbtCreateTask, string p_strBackupDirectory)
		{
			MethodInfo validate = typeof(CreateBackupTask).GetMethod(
				"ValidateRequiredDeploymentPayloads",
				BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.NotNull(validate);
			validate.Invoke(p_cbtCreateTask, new object[] { p_strBackupDirectory });
		}

		/// <summary>
		/// Invokes the production payload restore primitive while keeping it private to RestoreBackupTask.
		/// </summary>
		private static void InvokeRestorePayloadFile(TxFileManager p_tfmFileManager, string p_strSourcePath, string p_strDestinationPath)
		{
			MethodInfo restorePayload = typeof(RestoreBackupTask).GetMethod(
				"RestorePayloadFile",
				BindingFlags.Static | BindingFlags.NonPublic);
			Assert.NotNull(restorePayload);
			restorePayload.Invoke(null, new object[] { p_tfmFileManager, p_strSourcePath, p_strDestinationPath });
		}

		/// <summary>
		/// Creates the minimal game-mode contract required by the real VMA deployment backend.
		/// </summary>
		private static IGameMode CreateGameMode(string p_strModeId, string p_strGamePath, string p_strDataPath, bool p_booRealFileRequired = true)
		{
			return InterfaceStub<IGameMode>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_ModeId":
						return p_strModeId;
					case "get_InstallationPath":
						return p_strGamePath;
					case "get_PluginDirectory":
						return p_strDataPath;
					case "get_UsesPlugins":
						return true;
					case "GetModFormatAdjustedPath":
						return args[1];
					case "HardlinkRequiredFilesType":
						return false;
					case "RealFileRequired":
						return p_booRealFileRequired;
					default:
						return null;
				}
			});
		}

		/// <summary>
		/// Creates isolated Virtual-storage settings for the test game mode.
		/// </summary>
		private static IEnvironmentInfo CreateEnvironmentInfo(string p_strModeId, string p_strVirtualRoot, bool p_booMultiHd = false)
		{
			var virtualFolders = new PerGameModeSettings<string>();
			virtualFolders[p_strModeId] = p_strVirtualRoot;
			var linkFolders = new PerGameModeSettings<string>();
			linkFolders[p_strModeId] = string.Empty;
			var multiHd = new PerGameModeSettings<bool>();
			multiHd[p_strModeId] = p_booMultiHd;
			ISettings settings = InterfaceStub<ISettings>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_VirtualFolder":
						return virtualFolders;
					case "get_HDLinkFolder":
						return linkFolders;
					case "get_MultiHDInstall":
						return multiHd;
					default:
						return null;
				}
			});
			return InterfaceStub<IEnvironmentInfo>.Create((method, args) => method.Name == "get_Settings" ? settings : null);
		}

		/// <summary>
		/// Creates the InstallLog contract used by the Virtual owner under test.
		/// </summary>
		private static IInstallLog CreateInstallLog(string p_strOwnerKey)
		{
			return InterfaceStub<IInstallLog>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_OriginalValuesKey":
						return "ORIGINAL_VALUE";
					case "GetModKey":
						return p_strOwnerKey;
					case "GetModInstallMethod":
						return ModInstallMethod.Virtual;
					case "GetDeploymentOwnerKeys":
						return (IReadOnlyList<string>)new string[0];
					default:
						return null;
				}
			});
		}

		/// <summary>
		/// Creates the managed Virtual mod represented by the test link.
		/// </summary>
		private static IMod CreateMod(string p_strFilename)
		{
			return InterfaceStub<IMod>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_Id":
						return "123";
					case "get_DownloadId":
						return "123";
					case "get_ModName":
						return "Virtual";
					case "get_Filename":
						return p_strFilename;
					case "get_FileName":
						return Path.GetFileName(p_strFilename);
					case "get_HumanReadableVersion":
						return "1.0";
					default:
						return null;
				}
			});
		}

		/// <summary>
		/// Creates a minimal ModManager shell while retaining its real registry lookup behavior.
		/// </summary>
		private static ModManager CreateModManagerShell(IGameMode p_gmdGameMode, IInstallLog p_ilgInstallLog, IMod p_modMod)
		{
			var registry = new ModRegistry(InterfaceStub<IModFormatRegistry>.Create((method, args) => null), p_gmdGameMode);
			FieldInfo registeredModsField = typeof(ModRegistry).GetField("m_oclRegisteredMods", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.NotNull(registeredModsField);
			var registeredMods = (ThreadSafeObservableList<IMod>)registeredModsField.GetValue(registry);
			registeredMods.Add(p_modMod);

			var manager = (ModManager)FormatterServices.GetUninitializedObject(typeof(ModManager));
			SetField(manager, "<GameMode>k__BackingField", p_gmdGameMode);
			SetField(manager, "<InstallationLog>k__BackingField", p_ilgInstallLog);
			SetField(manager, "<ManagedModRegistry>k__BackingField", registry);
			return manager;
		}

		/// <summary>
		/// Assigns one private test dependency without invoking the full application bootstrap.
		/// </summary>
		private static void SetField(object p_objTarget, string p_strFieldName, object p_objValue)
		{
			FieldInfo field = p_objTarget.GetType().GetField(p_strFieldName, BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.NotNull(field, "Missing field: " + p_strFieldName);
			field.SetValue(p_objTarget, p_objValue);
		}
	}
}
