namespace NexusClientTests
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Reflection;
	using System.Runtime.Serialization;

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
		/// Creates the minimal game-mode contract required by the real VMA deployment backend.
		/// </summary>
		private static IGameMode CreateGameMode(string p_strModeId, string p_strGamePath, string p_strDataPath)
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
						return true;
					default:
						return null;
				}
			});
		}

		/// <summary>
		/// Creates isolated Virtual-storage settings for the test game mode.
		/// </summary>
		private static IEnvironmentInfo CreateEnvironmentInfo(string p_strModeId, string p_strVirtualRoot)
		{
			var virtualFolders = new PerGameModeSettings<string>();
			virtualFolders[p_strModeId] = p_strVirtualRoot;
			var linkFolders = new PerGameModeSettings<string>();
			linkFolders[p_strModeId] = string.Empty;
			var multiHd = new PerGameModeSettings<bool>();
			multiHd[p_strModeId] = false;
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
