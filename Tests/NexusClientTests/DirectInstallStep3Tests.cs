namespace NexusClientTests
{
	using System;
	using System.IO;
	using System.Linq;
	using System.Reflection;
	using System.Text;
	using System.Xml.Linq;

	using ChinhDo.Transactions;
	using Nexus.Client.Games;
	using Nexus.Client.ModManagement;
	using Nexus.Client.ModManagement.InstallationLog;
	using Nexus.Client.ModManagement.Scripting;
	using Nexus.Client.ModManagement.Scripting.Operations;
	using Nexus.Client.Mods;
	using Nexus.Client.PluginManagement;
	using Nexus.Transactions;

	using NUnit.Framework;

	/// <summary>
	/// Verifies standalone Direct deployment, restoration, and rollback behavior introduced by Step 3.
	/// </summary>
	[TestFixture]
	public class DirectInstallStep3Tests
	{
		[Test]
		public void NonConflictingDirectInstall_WritesOnlyFinalDestinationAndDeploymentRegistry()
		{
			using (var environment = new DirectTestEnvironment())
			{
				IMod mod = environment.RegisterDirectMod("Direct");
				ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, @"textures\foo.dds");

				environment.Install(mod, target, "direct");

				Assert.AreEqual("direct", File.ReadAllText(Path.Combine(environment.DataPath, @"textures\foo.dds")));
				CollectionAssert.AreEqual(new[] { environment.InstallLog.GetModKey(mod) }, environment.InstallLog.GetDeploymentOwnerKeys(target));
				Assert.AreEqual(0, environment.InstallLog.GetInstalledModFiles(mod).Count, "Direct destinations must not be written to dataFiles.");
				Assert.IsFalse(Directory.EnumerateFiles(environment.VirtualPath, "*", SearchOption.AllDirectories).Any());
				Assert.IsFalse(Directory.EnumerateFiles(environment.LinkPath, "*", SearchOption.AllDirectories).Any());
			}
		}

		[Test]
		public void DirectGeneratedFile_WritesFinalDestinationWithoutVirtualStaging()
		{
			using (var environment = new DirectTestEnvironment())
			{
				IMod mod = environment.RegisterDirectMod("Generated");
				var context = new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data);
				var overwriteResolver = new ModDeploymentOverwriteResolver(
					mod, environment.InstallLog, environment.Manager, (message, allowGroup, hasOwner) => OverwriteResult.Yes);
				using (var scope = new TransactionScope())
				{
					var installer = new DirectModFileInstaller(mod, environment.GameMode, environment.Manager, null,
						new TxFileManager(), null, context, overwriteResolver);
					Assert.IsTrue(installer.GenerateDataFile(@"config\generated.ini", new byte[] { 1, 2, 3, 4 }));
					scope.Complete();
				}

				ModDeploymentTarget target = ModDeploymentTargetResolver.Resolve(
					environment.GameMode, mod, @"config\generated.ini", ModInstallRoot.Data);
				CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(environment.Manager.GetDeploymentPath(target)));
				Assert.IsFalse(Directory.EnumerateFiles(environment.VirtualPath, "*", SearchOption.AllDirectories).Any());
				Assert.IsFalse(Directory.EnumerateFiles(environment.LinkPath, "*", SearchOption.AllDirectories).Any());
			}
		}

		/// <summary>
		/// Verifies that a plugin-state flush during a Direct upgrade does not finalize stale-target cleanup before later file operations run.
		/// </summary>
		[Test]
		public void DirectUpgrade_PluginStateFlushBeforeLaterFile_PreservesOwnerPrecedence()
		{
			using (var environment = new DirectTestEnvironment())
			{
				IMod first = environment.RegisterDirectMod("A");
				IMod second = environment.RegisterDirectMod("B");
				ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "later.txt");
				environment.Install(first, target, "A-old");
				environment.Install(second, target, "B-winner");

				IGameMode scriptedGameMode = InterfaceStub<IGameMode>.Create((method, args) =>
				{
					if (method.Name == "get_GameModeEnvironmentInfo")
						return environment.GameMode.GameModeEnvironmentInfo;
					if (method.Name == "get_InstallationPath")
						return environment.GameRootPath;
					if (method.Name == "get_UsesPlugins")
						return true;
					if (method.Name == "GetModFormatAdjustedPath")
						return args[1];
					return null;
				});
				IPluginManager pluginManager = InterfaceStub<IPluginManager>.Create((method, args) => null);
				var context = new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data);
				var fileManager = new TxFileManager();
				var overwriteResolver = new ModDeploymentOverwriteResolver(
					first, environment.InstallLog, environment.Manager, (message, allowGroup, hasOwner) => OverwriteResult.Yes);
				var fileInstaller = new DirectModFileInstaller(
					first, first, scriptedGameMode, environment.InstallLog, environment.Manager, pluginManager, fileManager,
					(message, allowGroup, hasOwner) => OverwriteResult.Yes, null, context, true);
				var installers = new InstallerGroup(
					null, fileInstaller, null, null, pluginManager, context, environment.Manager, fileManager, overwriteResolver);
				var executor = new ImmediateScriptedInstallOperationExecutor(
					first, scriptedGameMode, null, CreateEmptyVirtualActivator(), null, installers, null, null);

				using (var scope = new TransactionScope())
				{
					Assert.IsTrue(executor.Execute(new SetPluginActivationOperation("dummy.esp", true)));
					Assert.IsTrue(executor.Execute(new GenerateDataFileOperation("later.txt", Encoding.UTF8.GetBytes("A-new"))));
					fileInstaller.FinalizeInstall();
					scope.Complete();
				}

				string firstKey = environment.InstallLog.GetModKey(first);
				CollectionAssert.AreEqual(
					new[] { firstKey, environment.InstallLog.GetModKey(second) },
					environment.InstallLog.GetDeploymentOwnerKeys(target));
				Assert.AreEqual("B-winner", File.ReadAllText(environment.Manager.GetDeploymentPath(target)));
				Assert.AreEqual("A-new", File.ReadAllText(environment.Manager.GetOwnerBackupPath(target, firstKey)));
			}
		}

		[Test]
		public void DirectOverwrite_UninstallWinnerRestoresPreviousDirectOwner()
		{
			using (var environment = new DirectTestEnvironment())
			{
				IMod first = environment.RegisterDirectMod("First");
				IMod second = environment.RegisterDirectMod("Second");
				ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, @"meshes\foo.nif");

				environment.Install(first, target, "first");
				environment.Install(second, target, "second");

				CollectionAssert.AreEqual(
					new[] { environment.InstallLog.GetModKey(first), environment.InstallLog.GetModKey(second) },
					environment.InstallLog.GetDeploymentOwnerKeys(target));
				Assert.AreEqual("second", File.ReadAllText(environment.Manager.GetDeploymentPath(target)));

				environment.Uninstall(second);

				Assert.AreEqual("first", File.ReadAllText(environment.Manager.GetDeploymentPath(target)));
				CollectionAssert.AreEqual(
					new[] { environment.InstallLog.GetModKey(first) },
					environment.InstallLog.GetDeploymentOwnerKeys(target));
			}
		}

		[Test]
		public void UninstallInactiveDirectOwner_LeavesPhysicalWinnerUntouched()
		{
			using (var environment = new DirectTestEnvironment())
			{
				IMod first = environment.RegisterDirectMod("First");
				IMod second = environment.RegisterDirectMod("Second");
				ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, @"scripts\winner.pex");
				environment.Install(first, target, "first");
				environment.Install(second, target, "second");

				environment.Uninstall(first);

				Assert.AreEqual("second", File.ReadAllText(environment.Manager.GetDeploymentPath(target)));
				CollectionAssert.AreEqual(
					new[] { environment.InstallLog.GetModKey(second) },
					environment.InstallLog.GetDeploymentOwnerKeys(target));
				Assert.AreEqual(0, environment.GetBackupFiles().Length);
			}
		}

		[Test]
		public void UninstallLastDirectOwner_RestoresOriginalUnmanagedFile()
		{
			using (var environment = new DirectTestEnvironment())
			{
				IMod mod = environment.RegisterDirectMod("Direct");
				ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, @"config\original.ini");
				string deployedPath = environment.Manager.GetDeploymentPath(target);
				Directory.CreateDirectory(Path.GetDirectoryName(deployedPath));
				File.WriteAllText(deployedPath, "original");

				environment.Install(mod, target, "direct");
				CollectionAssert.AreEqual(
					new[] { environment.InstallLog.OriginalValuesKey, environment.InstallLog.GetModKey(mod) },
					environment.InstallLog.GetDeploymentOwnerKeys(target));

				environment.Uninstall(mod);

				Assert.AreEqual("original", File.ReadAllText(deployedPath));
				Assert.IsFalse(environment.InstallLog.IsDeploymentTargetPromoted(target));
				Assert.AreEqual(0, environment.GetBackupFiles().Length);
			}
		}

		[Test]
		public void DataAndGameRootTargets_KeepPhysicalFilesAndBackupsSeparate()
		{
			using (var environment = new DirectTestEnvironment())
			{
				IMod dataMod = environment.RegisterDirectMod("Data");
				IMod rootMod = environment.RegisterDirectMod("Root", ModInstallRoot.GameRoot);
				ModDeploymentTarget dataTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "same.dll");
				ModDeploymentTarget rootTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.GameRoot, "same.dll");
				File.WriteAllText(environment.Manager.GetDeploymentPath(dataTarget), "data-original");
				File.WriteAllText(environment.Manager.GetDeploymentPath(rootTarget), "root-original");

				environment.Install(dataMod, dataTarget, "data-direct");
				environment.Install(rootMod, rootTarget, "root-direct");

				Assert.AreEqual("data-direct", File.ReadAllText(environment.Manager.GetDeploymentPath(dataTarget)));
				Assert.AreEqual("root-direct", File.ReadAllText(environment.Manager.GetDeploymentPath(rootTarget)));
				Assert.AreEqual(2, environment.GetBackupFiles().Length);

				environment.Uninstall(dataMod);
				environment.Uninstall(rootMod);
				Assert.AreEqual("data-original", File.ReadAllText(environment.Manager.GetDeploymentPath(dataTarget)));
				Assert.AreEqual("root-original", File.ReadAllText(environment.Manager.GetDeploymentPath(rootTarget)));
			}
		}

		[Test]
		public void CancelledDirectTransaction_RollsBackFilesBackupsActiveModAndDeploymentMetadata()
		{
			using (var environment = new DirectTestEnvironment())
			{
				var mod = new InstallLog.DummyMod("Cancelled", Path.Combine(environment.ModPath, "Cancelled.7z"));
				ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, @"textures\rollback.dds");
				string deployedPath = environment.Manager.GetDeploymentPath(target);
				Directory.CreateDirectory(Path.GetDirectoryName(deployedPath));
				File.WriteAllText(deployedPath, "original");
				string payloadPath = environment.CreatePayload("direct");

				using (var scope = new TransactionScope())
				{
					environment.InstallLog.AddActiveMod(mod, ModInstallRoot.Data, ModInstallMethod.Direct);
					using (FileStream stream = File.OpenRead(payloadPath))
						environment.Manager.InstallDirectFile(mod, target, stream, new TxFileManager());
				}

				Assert.AreEqual("original", File.ReadAllText(deployedPath));
				Assert.IsFalse(environment.InstallLog.ActiveMods.Contains(mod));
				Assert.IsFalse(environment.InstallLog.IsDeploymentTargetPromoted(target));
				Assert.AreEqual(0, environment.GetBackupFiles().Length);
			}
		}

		/// <summary>
		/// Verifies that a failed transactional stream write restores an existing destination before ambient rollback runs.
		/// </summary>
		[Test]
		public void FailedTransactionalStreamWrite_RestoresExistingDestinationImmediately()
		{
			using (var environment = new DirectTestEnvironment())
			{
				string destination = Path.Combine(environment.DataPath, "failure-atomic-existing.bin");
				File.WriteAllText(destination, "original");
				string payload = environment.CreatePayload("replacement");

				using (FileStream stream = File.OpenRead(payload))
				{
					stream.Dispose();
					using (var scope = new TransactionScope())
					{
						Assert.Throws<Exception>(() => new TxFileManager().WriteFileStream(destination, stream));
						Assert.AreEqual("original", File.ReadAllText(destination));
					}
				}
			}
		}

		/// <summary>
		/// Verifies that a failed transactional stream write removes a newly-created partial destination immediately.
		/// </summary>
		[Test]
		public void FailedTransactionalStreamWrite_RemovesNewPartialDestinationImmediately()
		{
			using (var environment = new DirectTestEnvironment())
			{
				string destination = Path.Combine(environment.DataPath, "failure-atomic-new.bin");
				string payload = environment.CreatePayload("replacement");

				using (FileStream stream = File.OpenRead(payload))
				{
					stream.Dispose();
					using (var scope = new TransactionScope())
					{
						Assert.Throws<Exception>(() => new TxFileManager().WriteFileStream(destination, stream));
						Assert.IsFalse(File.Exists(destination));
					}
				}
			}
		}

		[Test]
		public void FailedDirectOverwrite_RollsBackWinnerBackupAndDeploymentMetadata()
		{
			using (var environment = new DirectTestEnvironment())
			{
				IMod first = environment.RegisterDirectMod("First");
				IMod second = environment.RegisterDirectMod("Second");
				ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, @"textures\failure.dds");
				environment.Install(first, target, "first");
				string payloadPath = environment.CreatePayload("second");

				using (FileStream stream = File.OpenRead(payloadPath))
				{
					stream.Dispose();
					using (var scope = new TransactionScope())
					{
						Exception exception = Assert.Throws<Exception>(() =>
							environment.Manager.InstallDirectFile(second, target, stream, new TxFileManager()));
						Assert.IsInstanceOf<ObjectDisposedException>(exception.InnerException);
					}
				}

				Assert.AreEqual("first", File.ReadAllText(environment.Manager.GetDeploymentPath(target)));
				CollectionAssert.AreEqual(
					new[] { environment.InstallLog.GetModKey(first) },
					environment.InstallLog.GetDeploymentOwnerKeys(target));
				Assert.AreEqual(0, environment.GetBackupFiles().Length);
			}
		}

		[Test]
		public void DeploymentCommitSequence_AdvancesOnlyForCommittedDeploymentTransactions()
		{
			using (var environment = new DirectTestEnvironment())
			{
				IMod mod = environment.RegisterDirectMod("Sequence");
				ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, @"sequence\file.bin");
				Assert.AreEqual(0, environment.InstallLog.DeploymentCommitSequence);

				environment.Install(mod, target, "committed");
				Assert.AreEqual(1, environment.InstallLog.DeploymentCommitSequence);
				Assert.AreEqual("1", (string)XDocument.Load(environment.InstallLogPath).Root.Attribute("deploymentCommitSequence"));

				string payloadPath = environment.CreatePayload("rolled-back");
				using (var scope = new TransactionScope())
				using (FileStream stream = File.OpenRead(payloadPath))
					environment.Manager.UpgradeDirectFile(mod, target, stream, new TxFileManager());

				Assert.AreEqual(1, environment.InstallLog.DeploymentCommitSequence);
				Assert.AreEqual("committed", File.ReadAllText(environment.Manager.GetDeploymentPath(target)));
			}
		}

		[Test]
		public void FailedInstallLogCommit_RestoresDirectWinnerMetadataAndSequence()
		{
			using (var environment = new DirectTestEnvironment())
			{
				IMod first = environment.RegisterDirectMod("CommitFailureFirst");
				IMod second = new InstallLog.DummyMod("CommitFailureSecond", Path.Combine(environment.ModPath, "CommitFailureSecond.7z"));
				ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, @"recovery\commit-failure.bin");
				environment.Install(first, target, "before");

				string firstModKey = environment.InstallLog.GetModKey(first);
				long sequence = environment.InstallLog.DeploymentCommitSequence;
				string deploymentPath = environment.Manager.GetDeploymentPath(target);
				string payloadPath = environment.CreatePayload("after");

				using (var installLogLock = new FileStream(environment.InstallLogPath, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					Assert.Throws<TransactionException>(() =>
					{
						using (var scope = new TransactionScope())
						using (FileStream stream = File.OpenRead(payloadPath))
						{
							environment.InstallLog.AddActiveMod(second, ModInstallRoot.Data, ModInstallMethod.Direct);
							environment.Manager.InstallDirectFile(second, target, stream, new TxFileManager());
							scope.Complete();
						}
					});
				}

				Assert.AreEqual(sequence, environment.InstallLog.DeploymentCommitSequence);
				Assert.IsNull(environment.InstallLog.GetModKey(second));
				CollectionAssert.AreEqual(new[] { firstModKey }, environment.InstallLog.GetDeploymentOwnerKeys(target));
				Assert.AreEqual("before", File.ReadAllText(deploymentPath));
				Assert.AreEqual(0, environment.GetBackupFiles().Length);
				Assert.AreEqual(sequence.ToString(), (string)XDocument.Load(environment.InstallLogPath).Root.Attribute("deploymentCommitSequence"));

				InstallLog reloadedInstallLog = environment.ReloadInstallLog();
				try
				{
					Assert.AreEqual(sequence, reloadedInstallLog.DeploymentCommitSequence);
					Assert.IsNull(reloadedInstallLog.GetModKey(second));
					CollectionAssert.AreEqual(new[] { firstModKey }, reloadedInstallLog.GetDeploymentOwnerKeys(target));
				}
				finally
				{
					reloadedInstallLog.Release();
				}
			}
		}

		[Test]
		public void PendingDeploymentRecoveryJournal_RestoresPreTransactionDirectWinner()
		{
			using (var environment = new DirectTestEnvironment())
			{
				IMod mod = environment.RegisterDirectMod("Recovery");
				ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, @"recovery\winner.bin");
				environment.Install(mod, target, "before-crash");
				long sequence = environment.InstallLog.DeploymentCommitSequence;
				string deploymentPath = environment.Manager.GetDeploymentPath(target);
				string originalBackupPath = environment.Manager.GetOwnerBackupPath(target, environment.InstallLog.OriginalValuesKey);
				string originalBackupDirectory = Path.GetDirectoryName(originalBackupPath);
				string transactionDirectory = CreateRecoveryJournal(environment, target, sequence, "before-crash");

				Assert.IsFalse(Directory.Exists(originalBackupDirectory));
				File.WriteAllText(deploymentPath, "interrupted-write");
				InstallLog reloadedInstallLog = environment.ReloadInstallLog();
				try
				{
					Assert.AreEqual(sequence, reloadedInstallLog.DeploymentCommitSequence);
					new ModDeploymentManager(reloadedInstallLog, CreateEmptyVirtualActivator(), environment.GameMode);
				}
				finally
				{
					reloadedInstallLog.Release();
				}

				Assert.AreEqual("before-crash", File.ReadAllText(deploymentPath));
				Assert.IsFalse(Directory.Exists(originalBackupDirectory));
				Assert.IsFalse(Directory.Exists(transactionDirectory));
			}
		}

		[Test]
		public void CommittedDeploymentRecoveryJournal_IsCleanedWithoutRollingBackWinner()
		{
			using (var environment = new DirectTestEnvironment())
			{
				IMod mod = environment.RegisterDirectMod("CommittedRecovery");
				ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, @"recovery\committed.bin");
				environment.Install(mod, target, "committed");
				string deploymentPath = environment.Manager.GetDeploymentPath(target);
				long committedSequence = environment.InstallLog.DeploymentCommitSequence;
				string transactionDirectory = CreateRecoveryJournal(environment, target, committedSequence - 1, "stale-before");

				InstallLog reloadedInstallLog = environment.ReloadInstallLog();
				try
				{
					Assert.AreEqual(committedSequence, reloadedInstallLog.DeploymentCommitSequence);
					new ModDeploymentManager(reloadedInstallLog, CreateEmptyVirtualActivator(), environment.GameMode);
				}
				finally
				{
					reloadedInstallLog.Release();
				}

				Assert.AreEqual("committed", File.ReadAllText(deploymentPath));
				Assert.IsFalse(Directory.Exists(transactionDirectory));
			}
		}

		private static string CreateRecoveryJournal(DirectTestEnvironment p_dteEnvironment, ModDeploymentTarget p_mdtTarget,
			long p_lngPreCommitSequence, string p_strDeploymentSnapshotContents)
		{
			string transactionDirectory = Path.Combine(
				p_dteEnvironment.OverwritePath, "deployment", "_recovery", "test-" + Guid.NewGuid().ToString("N"));
			string snapshotsDirectory = Path.Combine(transactionDirectory, "snapshots");
			Directory.CreateDirectory(snapshotsDirectory);
			new XDocument(new XElement("deploymentRecovery",
				new XAttribute("transactionId", "test"),
				new XAttribute("preCommitSequence", p_lngPreCommitSequence)))
				.Save(Path.Combine(transactionDirectory, "transaction.xml"));

			const string snapshotName = "deployment.bin";
			File.WriteAllText(Path.Combine(snapshotsDirectory, snapshotName), p_strDeploymentSnapshotContents);
			var record = new XElement("target",
				new XAttribute("root", p_mdtTarget.Root),
				new XAttribute("path", p_mdtTarget.RelativePath),
				new XAttribute("deploymentState", "Snapshot"),
				new XAttribute("deploymentSnapshot", snapshotName),
				new XElement("owners"),
				new XElement("backups",
					new XElement("backup",
						new XAttribute("ownerKey", p_dteEnvironment.InstallLog.OriginalValuesKey),
						new XAttribute("existed", false))));
			byte[] payload = Encoding.UTF8.GetBytes(record.ToString(SaveOptions.DisableFormatting));
			using (var stream = new FileStream(Path.Combine(transactionDirectory, "targets.bin"), FileMode.Create, FileAccess.Write, FileShare.None))
			using (var writer = new BinaryWriter(stream, Encoding.UTF8))
			{
				writer.Write(payload.Length);
				writer.Write(payload);
			}
			return transactionDirectory;
		}

		private static IVirtualModActivator CreateEmptyVirtualActivator()
		{
			return InterfaceStub<IVirtualModActivator>.Create((method, args) =>
				method.Name == "GetVirtualOwnerKeys" ? (object)new string[0] : null);
		}

		private sealed class DirectTestEnvironment : IDisposable
		{
			private readonly string m_strRootPath;
			private int m_intPayloadNumber;

			public DirectTestEnvironment()
			{
				m_strRootPath = Path.Combine(Path.GetTempPath(), "NMM-Step3-" + Guid.NewGuid().ToString("N"));
				DataPath = Path.Combine(m_strRootPath, "Data");
				GameRootPath = Path.Combine(m_strRootPath, "GameRoot");
				SecondaryPath = Path.Combine(m_strRootPath, "Secondary");
				OverwritePath = Path.Combine(m_strRootPath, "InstallInfo", "overwrites");
				VirtualPath = Path.Combine(m_strRootPath, "VirtualInstall");
				LinkPath = Path.Combine(m_strRootPath, "NMMLink");
				ModPath = Path.Combine(m_strRootPath, "Mods");
				InstallLogPath = Path.Combine(m_strRootPath, "InstallInfo", "InstallLog.xml");
				Directory.CreateDirectory(DataPath);
				Directory.CreateDirectory(GameRootPath);
				Directory.CreateDirectory(SecondaryPath);
				Directory.CreateDirectory(OverwritePath);
				Directory.CreateDirectory(VirtualPath);
				Directory.CreateDirectory(LinkPath);
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
				IVirtualModActivator virtualModActivator = InterfaceStub<IVirtualModActivator>.Create((method, args) =>
					method.Name == "GetVirtualOwnerKeys" ? (object)new string[0] : null);

				InstallLog = CreateInstallLog(ModPath, InstallLogPath);
				Manager = new ModDeploymentManager(InstallLog, virtualModActivator, GameMode);
			}

			public IGameMode GameMode { get; }
			public string DataPath { get; }
			public string GameRootPath { get; }
			public string SecondaryPath { get; }
			public string OverwritePath { get; }
			public string VirtualPath { get; }
			public string LinkPath { get; }
			public string ModPath { get; }
			public string InstallLogPath { get; }
			public InstallLog InstallLog { get; }
			public ModDeploymentManager Manager { get; }

			public IMod RegisterDirectMod(string p_strName, ModInstallRoot p_mirInstallRoot = ModInstallRoot.Data)
			{
				var mod = new InstallLog.DummyMod(p_strName, Path.Combine(ModPath, p_strName + ".7z"));
				InstallLog.AddActiveMod(mod, p_mirInstallRoot, ModInstallMethod.Direct);
				return mod;
			}

			public void Install(IMod p_modMod, ModDeploymentTarget p_mdtTarget, string p_strContents)
			{
				string payloadPath = CreatePayload(p_strContents);
				using (var scope = new TransactionScope())
				{
					using (FileStream stream = File.OpenRead(payloadPath))
						Manager.InstallDirectFile(p_modMod, p_mdtTarget, stream, new TxFileManager());
					scope.Complete();
				}
			}

			public void Uninstall(IMod p_modMod)
			{
				using (var scope = new TransactionScope())
				{
					Manager.UninstallDirectMod(p_modMod, new TxFileManager());
					InstallLog.RemoveMod(p_modMod);
					scope.Complete();
				}
			}


			public InstallLog ReloadInstallLog()
			{
				return CreateInstallLog(ModPath, InstallLogPath);
			}

			public string CreatePayload(string p_strContents)
			{
				string path = Path.Combine(m_strRootPath, "payload-" + (++m_intPayloadNumber) + ".bin");
				File.WriteAllText(path, p_strContents);
				return path;
			}

			public string[] GetBackupFiles()
			{
				return Directory.Exists(OverwritePath)
					? Directory.GetFiles(OverwritePath, "*.nmmbackup", SearchOption.AllDirectories)
					: new string[0];
			}

			public void Dispose()
			{
				InstallLog.Release();
				if (Directory.Exists(m_strRootPath))
					Directory.Delete(m_strRootPath, true);
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
	}
}
