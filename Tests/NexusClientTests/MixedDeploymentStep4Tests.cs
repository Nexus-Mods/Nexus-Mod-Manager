namespace NexusClientTests
{
	using System;
	using System.Collections.Generic;
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
					if (method.Name == "GetModFormatAdjustedPath")
						return args[1];
					return null;
				});

				InstallLog = CreateInstallLog(ModPath, Path.Combine(m_strRootPath, "InstallInfo", "InstallLog.xml"));
				VirtualState = new TransactionalVirtualState(InstallLog, VirtualPath);
				Manager = new ModDeploymentManager(InstallLog, VirtualState.Activator, gameMode);
				VirtualState.GetDeploymentPath = Manager.GetDeploymentPath;
			}

			public string DataPath { get; private set; }
			public string GameRootPath { get; private set; }
			public string SecondaryPath { get; private set; }
			public string OverwritePath { get; private set; }
			public string VirtualPath { get; private set; }
			public string ModPath { get; private set; }
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
				bool p_booActive, int p_intPriority, string p_strOriginal = null, bool p_booHardLink = false)
			{
				string source = StageVirtual(p_modMod, p_mdtTarget, p_strContents);
				VirtualState.Add(p_modMod, Key(p_modMod), p_mdtTarget, source, p_booActive, p_intPriority);
				if (p_booActive)
				DeployInitial(source, Manager.GetDeploymentPath(p_mdtTarget), p_booHardLink);
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

			public void Dispose()
			{
				InstallLog.Release();
				if (Directory.Exists(m_strRootPath))
					Directory.Delete(m_strRootPath, true);
			}

			private static void DeployInitial(string p_strSource, string p_strTarget, bool p_booHardLink)
			{
				Directory.CreateDirectory(Path.GetDirectoryName(p_strTarget));
				if (p_booHardLink)
				{
					using (var scope = new TransactionScope())
					{
						if (new TxFileManager().CreateHardLink(p_strTarget, p_strSource))
						{
							scope.Complete();
							return;
						}
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
			private List<VirtualOwner> m_lstOwners = new List<VirtualOwner>();
			private string m_strEnlistedTransaction;

			public TransactionalVirtualState(IInstallLog p_ilgInstallLog, string p_strVirtualPath)
			{
				m_ilgInstallLog = p_ilgInstallLog;
				m_strVirtualPath = p_strVirtualPath;
				Activator = InterfaceStub<IVirtualModActivator>.Create(HandleCall);
			}

			public IVirtualModActivator Activator { get; private set; }
			public Func<ModDeploymentTarget, string> GetDeploymentPath { get; set; }

			public void Add(IMod p_modMod, string p_strModKey, ModDeploymentTarget p_mdtTarget,
				string p_strSource, bool p_booActive, int p_intPriority)
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
					Priority = p_intPriority
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
					p_tfmFileManager.Delete(targetPath);
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
