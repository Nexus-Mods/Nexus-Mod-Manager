namespace NexusClientTests
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Reflection;

	using ChinhDo.Transactions;
	using Nexus.Client.Games;
	using Nexus.Client.ModManagement;
	using Nexus.Client.ModManagement.InstallationLog;
	using Nexus.Client.Mods;
	using Nexus.Client.PluginManagement;

	using NUnit.Framework;

	/// <summary>
	/// Verifies that basic Virtual installation preserves its hot path and branches only for promoted targets.
	/// </summary>
	[TestFixture]
	public class BasicInstallPromotedRoutingTests
	{
		[Test]
		public void PureVirtualFastPath_DoesNotPerformPerTargetRegistryQueries()
		{
			string root = CreateRoot();
			try
			{
				int promotedQueries = 0;
				int linkCalls = 0;
				int saveCalls = 0;
				IMod mod = CreateMod("pure.dds");
				IGameMode gameMode = CreateGameMode(root, false);
				IModLinkInstaller linkInstaller = InterfaceStub<IModLinkInstaller>.Create((method, args) =>
				{
					if (method.Name == "AddFileLink")
					{
						linkCalls++;
						return Path.Combine(root, "Data", (string)args[1]);
					}
					return null;
				});
				IVirtualModActivator activator = CreateActivator(root, linkInstaller, () => saveCalls++);
				IModDeploymentManager deploymentManager = InterfaceStub<IModDeploymentManager>.Create((method, args) =>
				{
					if (method.Name == "get_HasPromotedTargets")
						return false;
					if (method.Name == "IsPromoted")
						promotedQueries++;
					return null;
				});
				var task = new BasicInstallTask(
					mod,
					gameMode,
					new StagingFileInstaller(),
					null,
					activator,
					false,
					null,
					null,
					new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data),
					deploymentManager,
					new TxFileManager(),
					null);

				Assert.IsTrue(InvokeDoWork(task));
				Assert.AreEqual(1, linkCalls);
				Assert.AreEqual(0, promotedQueries);
				Assert.AreEqual(1, saveCalls);
				Assert.IsFalse(task.UsedPromotedDeployment);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PromotedVirtualTarget_UsesCoordinatorAndBatchesWinningPluginOnce()
		{
			string root = CreateRoot();
			try
			{
				int linkCalls = 0;
				int coordinatorCalls = 0;
				int saveCalls = 0;
				int pluginBatches = 0;
				string deployedPath = Path.Combine(root, "Data", "winner.esp");
				IMod mod = CreateMod("winner.esp");
				IGameMode gameMode = CreateGameMode(root, true);
				IModLinkInstaller linkInstaller = InterfaceStub<IModLinkInstaller>.Create((method, args) =>
				{
					if (method.Name == "AddFileLink")
						linkCalls++;
					return null;
				});
				IVirtualModActivator activator = CreateActivator(root, linkInstaller, () => saveCalls++);
				IModDeploymentManager deploymentManager = InterfaceStub<IModDeploymentManager>.Create((method, args) =>
				{
					switch (method.Name)
					{
						case "get_HasPromotedTargets":
							return true;
						case "IsPromoted":
							return true;
						case "GetDeploymentPath":
							return deployedPath;
						case "GetCurrentOwnerKey":
							return "DirectOwner";
						case "InstallVirtualFile":
							coordinatorCalls++;
							Assert.IsTrue((bool)args[5]);
							return deployedPath;
						default:
							return null;
					}
				});
				IInstallLog installLog = InterfaceStub<IInstallLog>.Create((method, args) =>
				{
					if (method.Name == "GetModKey")
						return "VirtualOwner";
					if (method.Name == "get_OriginalValuesKey")
						return "ORIGINAL_VALUE";
					return null;
				});
				IPluginManager pluginManager = InterfaceStub<IPluginManager>.Create((method, args) =>
				{
					if (method.Name == "IsActivatiblePluginFile")
						return true;
					if (method.Name == "IntegrateDeployedPlugins")
					{
						pluginBatches++;
						CollectionAssert.AreEqual(new[] { deployedPath }, (IEnumerable<string>)args[0]);
					}
					return null;
				});
				var overwriteResolver = new ModDeploymentOverwriteResolver(
					mod,
					installLog,
					deploymentManager,
					(message, allowGroup, hasOwner) => OverwriteResult.Yes);
				var task = new BasicInstallTask(
					mod,
					gameMode,
					new StagingFileInstaller(),
					pluginManager,
					activator,
					false,
					null,
					null,
					new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data),
					deploymentManager,
					new TxFileManager(),
					overwriteResolver);

				Assert.IsTrue(InvokeDoWork(task));
				Assert.AreEqual(1, coordinatorCalls);
				Assert.AreEqual(0, linkCalls);
				Assert.AreEqual(0, saveCalls);
				Assert.AreEqual(1, pluginBatches);
				Assert.IsTrue(task.UsedPromotedDeployment);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static string CreateRoot()
		{
			string root = Path.Combine(Path.GetTempPath(), "NMM-Step4-Basic-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Path.Combine(root, "VirtualInstall"));
			Directory.CreateDirectory(Path.Combine(root, "Data"));
			return root;
		}

		private static IMod CreateMod(string p_strPath)
		{
			return InterfaceStub<IMod>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_Filename":
						return @"C:\Mods\Virtual.7z";
					case "get_ModName":
						return "Virtual";
					case "get_DownloadId":
						return string.Empty;
					case "GetFileList":
						return new List<string> { p_strPath };
					default:
						return null;
				}
			});
		}

		private static IGameMode CreateGameMode(string p_strRoot, bool p_booUsesPlugins)
		{
			return InterfaceStub<IGameMode>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_Name":
						return "Test Game";
					case "get_InstallationPath":
						return Path.Combine(p_strRoot, "Game");
					case "get_PluginDirectory":
						return Path.Combine(p_strRoot, "Data");
					case "get_UsesPlugins":
						return p_booUsesPlugins;
					case "GetModFormatAdjustedPath":
						return args[1];
					case "HardlinkRequiredFilesType":
						return false;
					case "IsSpecialFile":
						return false;
					default:
						return null;
				}
			});
		}

		private static IVirtualModActivator CreateActivator(
			string p_strRoot,
			IModLinkInstaller p_mliLinkInstaller,
			Action p_actSave)
		{
			return InterfaceStub<IVirtualModActivator>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_VirtualPath":
						return Path.Combine(p_strRoot, "VirtualInstall");
					case "GetModLinkInstaller":
						return p_mliLinkInstaller;
					case "BeginModInfoUpdateBatch":
					case "BeginVirtualLinkUpdateBatch":
						return new EmptyDisposable();
					case "SaveList":
						p_actSave();
						return true;
					default:
						return null;
				}
			});
		}

		private static bool InvokeDoWork(BasicInstallTask p_bitTask)
		{
			MethodInfo method = typeof(BasicInstallTask).GetMethod(
				"DoWork",
				BindingFlags.Instance | BindingFlags.NonPublic,
				null,
				new[] { typeof(object[]) },
				null);
			return (bool)method.Invoke(p_bitTask, new object[] { new object[0] });
		}

		private sealed class StagingFileInstaller : IModFileInstaller
		{
			public List<string> InstallErrors { get; } = new List<string>();

			public bool InstallFileFromMod(string p_strModFilePath, string p_strInstallPath)
			{
				Directory.CreateDirectory(Path.GetDirectoryName(p_strInstallPath));
				File.WriteAllText(p_strInstallPath, "staged");
				return true;
			}

			public bool GenerateDataFile(string p_strPath, byte[] p_bteData)
			{
				throw new NotSupportedException();
			}

			public bool PluginCheck(string p_strPath, bool p_booRemove)
			{
				return false;
			}

			public bool UninstallDataFile(string p_strPath)
			{
				throw new NotSupportedException();
			}

			public void FinalizeInstall()
			{
			}
		}

		private sealed class EmptyDisposable : IDisposable
		{
			public void Dispose()
			{
			}
		}
	}
}
