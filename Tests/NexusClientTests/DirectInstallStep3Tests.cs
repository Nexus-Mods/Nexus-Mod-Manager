namespace NexusClientTests
{
	using System;
	using System.IO;
	using System.Linq;
	using System.Reflection;

	using ChinhDo.Transactions;
	using Nexus.Client.Games;
	using Nexus.Client.ModManagement;
	using Nexus.Client.ModManagement.InstallationLog;
	using Nexus.Client.Mods;
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
						return DataPath;
					if (method.Name == "get_SecondaryInstallationPath")
						return SecondaryPath;
					if (method.Name == "get_OverwriteDirectory")
						return OverwritePath;
					return null;
				});
				IGameMode gameMode = InterfaceStub<IGameMode>.Create((method, args) =>
				{
					if (method.Name == "get_GameModeEnvironmentInfo")
						return gameModeInfo;
					if (method.Name == "get_InstallationPath")
						return GameRootPath;
					if (method.Name == "get_SecondaryInstallationPath")
						return SecondaryPath;
					return null;
				});
				IVirtualModActivator virtualModActivator = InterfaceStub<IVirtualModActivator>.Create((method, args) =>
					method.Name == "GetVirtualOwnerKeys" ? (object)new string[0] : null);

				InstallLog = CreateInstallLog(ModPath, Path.Combine(m_strRootPath, "InstallInfo", "InstallLog.xml"));
				Manager = new ModDeploymentManager(InstallLog, virtualModActivator, gameMode);
			}

			public string DataPath { get; }
			public string GameRootPath { get; }
			public string SecondaryPath { get; }
			public string OverwritePath { get; }
			public string VirtualPath { get; }
			public string LinkPath { get; }
			public string ModPath { get; }
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
