using System;
using System.Collections.Generic;
using System.IO;
using Nexus.Client.CollectionManagement;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.PluginManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// C6.1 immutable native-state index and fingerprint coverage.
	/// </summary>
	public class CollectionNativeStateIndexTests
	{
		[Test]
		public void NativeStateReader_InstallLogProviderResolvesCurrentAuthorityForEveryCapture()
		{
			IInstallLog currentInstallLog = CreateInstallLog(1);
			IVirtualModActivator virtualModActivator = InterfaceStub<IVirtualModActivator>.Create((method, args) =>
				method.Name == "GetReadSnapshot" ? new VirtualModReadSnapshot(new VirtualModReadLink[0]) : null);
			IGameMode gameMode = InterfaceStub<IGameMode>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_InstallationPath": return Path.GetTempPath();
					case "get_HasSecondaryInstallPath": return false;
					case "get_UsesPlugins": return false;
					default: return null;
				}
			});
			var reader = new CollectionNativeStateReader(() => currentInstallLog, virtualModActivator, null, gameMode, null);
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-live-authority");

			CollectionNativeStateIndex first = reader.Capture(target);
			currentInstallLog = CreateInstallLog(2);
			CollectionNativeStateIndex second = reader.Capture(target);

			Assert.That(first.DeploymentCommitSequence, Is.EqualTo(1));
			Assert.That(second.DeploymentCommitSequence, Is.EqualTo(2));
			Assert.That(second.Fingerprint, Is.Not.EqualTo(first.Fingerprint));
		}

		[Test]
		public void NativeStateReader_PreservesCommittedModDisplayNameForCompatibilityMatching()
		{
			InstallLogReadSnapshot snapshot = new InstallLogReadSnapshot("ORIGINAL", 1,
				new[]
				{
					new InstallLogReadMod("native-f4se", "C:\\Mods\\f4se.7z", "f4se.7z",
						"Fallout 4 Script Extender (F4SE)", "42147", "407709", "0.7.9", "0.7.9", false,
						ModInstallRoot.GameRoot, ModInstallMethod.Virtual, false)
				},
				new InstallLogReadFile[0], new InstallLogReadIniEdit[0], new InstallLogReadGameValue[0],
				new InstallLogReadDeploymentTarget[0]);
			IInstallLog installLog = InterfaceStub<IInstallLog>.Create((method, args) =>
				method.Name == "GetCommittedStateSnapshot" ? snapshot : null);
			IVirtualModActivator virtualModActivator = InterfaceStub<IVirtualModActivator>.Create((method, args) =>
				method.Name == "GetReadSnapshot" ? new VirtualModReadSnapshot(new VirtualModReadLink[0]) : null);
			IGameMode gameMode = InterfaceStub<IGameMode>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_InstallationPath": return Path.GetTempPath();
					case "get_HasSecondaryInstallPath": return false;
					case "get_UsesPlugins": return false;
					default: return null;
				}
			});

			CollectionNativeStateIndex state = new CollectionNativeStateReader(installLog, virtualModActivator, null, gameMode, null)
				.Capture(CollectionTargetIdentity.FromFingerprint("target-native-mod-name"));

			Assert.AreEqual("Fallout 4 Script Extender (F4SE)", state.ModsByNativeKey["native-f4se"].ModName);
		}

		private static IInstallLog CreateInstallLog(long deploymentCommitSequence)
		{
			InstallLogReadSnapshot snapshot = new InstallLogReadSnapshot("ORIGINAL", deploymentCommitSequence,
				new InstallLogReadMod[0], new InstallLogReadFile[0], new InstallLogReadIniEdit[0],
				new InstallLogReadGameValue[0], new InstallLogReadDeploymentTarget[0]);
			return InterfaceStub<IInstallLog>.Create((method, args) =>
				method.Name == "GetCommittedStateSnapshot" ? snapshot : null);
		}

		[Test]
		public void IniKey_UsesCaseInsensitiveEqualityAndMatchingHashCode()
		{
			var first = new CollectionNativeIniKey("Skyrim.ini", "Display", "fShadowDistance");
			var same = new CollectionNativeIniKey("SKYRIM.INI", "display", "FSHADOWDISTANCE");

			Assert.AreEqual(first, same);
			Assert.AreEqual(first.GetHashCode(), same.GetHashCode());
		}

		[Test]
		public void Fingerprint_IsStableWhenUnorderedInputsAreReordered()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c6-index");
			CollectionNativeModState firstMod = CreateMod(target, "mod-b", "B.7z");
			CollectionNativeModState secondMod = CreateMod(target, "mod-a", "A.7z");
			CollectionNativeFileState firstFile = CreateFile("textures\\b.dds", "mod-b");
			CollectionNativeFileState secondFile = CreateFile("meshes\\a.nif", "mod-a");
			CollectionNativeIniState firstIni = new CollectionNativeIniState(
				new CollectionNativeIniKey("Skyrim.ini", "Display", "bBorderless"),
				new[] { new CollectionNativeTextOwnerValue("mod-a", "1") });
			CollectionNativeIniState secondIni = new CollectionNativeIniState(
				new CollectionNativeIniKey("SkyrimPrefs.ini", "Display", "iSize W"),
				new[] { new CollectionNativeTextOwnerValue("mod-b", "1920") });

			CollectionNativeStateIndex first = CreateIndex(target,
				new[] { firstMod, secondMod }, new[] { firstFile, secondFile }, new[] { firstIni, secondIni });
			CollectionNativeStateIndex reordered = CreateIndex(target,
				new[] { secondMod, firstMod }, new[] { secondFile, firstFile }, new[] { secondIni, firstIni });

			Assert.AreEqual(first.Fingerprint, reordered.Fingerprint);
			Assert.AreSame(first.Mods[new NativeModInstanceIdentity(target, "mod-a")], first.ModsByNativeKey["MOD-A"]);
			Assert.AreEqual(1, first.FilesByOwnerKey["MOD-A"].Count);
			Assert.AreEqual(1, first.IniEditsByOwnerKey["MOD-A"].Count);
		}

		[Test]
		public void Fingerprint_ChangesWhenOwnerHistoryOrderChanges()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c6-owner-order");
			CollectionNativeModState firstMod = CreateMod(target, "mod-a", "A.7z");
			CollectionNativeModState secondMod = CreateMod(target, "mod-b", "B.7z");
			ModDeploymentTarget deploymentTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\shared.dds");
			var firstOrder = new CollectionNativeFileState(deploymentTarget, "C:\\Game\\Data\\textures\\shared.dds", true, true, false,
				"mod-b", new CollectionNativeOwnerState[0], new[]
				{
					new CollectionNativeOwnerState("mod-a", null, CollectionNativeOwnerKind.NativeMod, null, null, null),
					new CollectionNativeOwnerState("mod-b", null, CollectionNativeOwnerKind.NativeMod, null, null, null)
				}, new CollectionNativeOwnerState[0]);
			var secondOrder = new CollectionNativeFileState(deploymentTarget, "C:\\Game\\Data\\textures\\shared.dds", true, true, false,
				"mod-a", new CollectionNativeOwnerState[0], new[]
				{
					new CollectionNativeOwnerState("mod-b", null, CollectionNativeOwnerKind.NativeMod, null, null, null),
					new CollectionNativeOwnerState("mod-a", null, CollectionNativeOwnerKind.NativeMod, null, null, null)
				}, new CollectionNativeOwnerState[0]);

			CollectionNativeStateIndex first = CreateIndex(target, new[] { firstMod, secondMod }, new[] { firstOrder }, null);
			CollectionNativeStateIndex second = CreateIndex(target, new[] { firstMod, secondMod }, new[] { secondOrder }, null);

			Assert.AreNotEqual(first.Fingerprint, second.Fingerprint);
		}

		[Test]
		public void Fingerprint_ChangesWhenCommittedModDisplayNameChanges()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c6-mod-name");
			var firstMod = new CollectionNativeModState(new NativeModInstanceIdentity(target, "mod-a"), "C:\\Mods\\A.7z",
				"A.7z", "Name A", "100", "200", "1.0", "1.0", false, ModInstallRoot.Data, ModInstallMethod.Virtual);
			var renamed = new CollectionNativeModState(new NativeModInstanceIdentity(target, "mod-a"), "C:\\Mods\\A.7z",
				"A.7z", "Name B", "100", "200", "1.0", "1.0", false, ModInstallRoot.Data, ModInstallMethod.Virtual);

			CollectionNativeStateIndex first = CreateIndex(target, new[] { firstMod }, new CollectionNativeFileState[0], null);
			CollectionNativeStateIndex second = CreateIndex(target, new[] { renamed }, new CollectionNativeFileState[0], null);

			Assert.AreNotEqual(first.Fingerprint, second.Fingerprint);
		}

		[Test]
		public void Fingerprint_ChangesWhenScriptedInstallerCapabilityChanges()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c6-scripted");
			var plain = new CollectionNativeModState(new NativeModInstanceIdentity(target, "mod-a"), "C:\\Mods\\A.7z",
				"A.7z", "100", "200", "1.0", "1.0.0.0", false, ModInstallRoot.Data, ModInstallMethod.Virtual);
			var scripted = new CollectionNativeModState(new NativeModInstanceIdentity(target, "mod-a"), "C:\\Mods\\A.7z",
				"A.7z", "100", "200", "1.0", "1.0.0.0", true, ModInstallRoot.Data, ModInstallMethod.Virtual);

			CollectionNativeStateIndex first = CreateIndex(target, new[] { plain }, new CollectionNativeFileState[0], null);
			CollectionNativeStateIndex second = CreateIndex(target, new[] { scripted }, new CollectionNativeFileState[0], null);

			Assert.AreNotEqual(first.Fingerprint, second.Fingerprint);
		}

		[Test]
		public void BinaryAndPluginState_DefensivelyCopyMutableInputs()
		{
			byte[] payload = { 1, 2, 3 };
			var binary = new CollectionNativeBinaryOwnerValue("mod-a", payload);
			var masters = new List<string> { "Master.esm" };
			var diagnostics = new List<CollectionNativePluginDiagnostic>
			{
				new CollectionNativePluginDiagnostic(PluginValidationIssueKind.MissingMaster, PluginValidationSeverity.Error)
			};
			var plugin = new CollectionNativePluginState("Example.esp", true, 1, 0, "00", PluginParseStatus.Parsed,
				PluginAddressClass.Full, PluginHeaderFlags.None, PluginSpecialFlags.None, false, 44, masters, diagnostics);

			payload[0] = 9;
			masters[0] = "Changed.esm";
			diagnostics.Clear();
			byte[] exposed = binary.Value;
			exposed[1] = 8;

			CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, binary.Value);
			CollectionAssert.AreEqual(new[] { "Master.esm" }, plugin.Masters);
			Assert.AreEqual(1, plugin.Diagnostics.Count);
		}

		private static CollectionNativeModState CreateMod(CollectionTargetIdentity target, string key, string fileName)
		{
			return new CollectionNativeModState(new NativeModInstanceIdentity(target, key), "C:\\Mods\\" + fileName,
				fileName, "100", "200", "1.0", "1.0.0.0", ModInstallRoot.Data, ModInstallMethod.Virtual);
		}

		private static CollectionNativeFileState CreateFile(string relativePath, string ownerKey)
		{
			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, relativePath);
			return new CollectionNativeFileState(target, "C:\\Game\\Data\\" + relativePath, false, false, true, ownerKey,
				new CollectionNativeOwnerState[0], new CollectionNativeOwnerState[0],
				new[] { new CollectionNativeOwnerState(ownerKey, null, CollectionNativeOwnerKind.NativeMod, true, 0, "C:\\Virtual\\" + relativePath) });
		}

		private static CollectionNativeStateIndex CreateIndex(CollectionTargetIdentity target,
			IEnumerable<CollectionNativeModState> mods, IEnumerable<CollectionNativeFileState> files,
			IEnumerable<CollectionNativeIniState> iniEdits)
		{
			return new CollectionNativeStateIndex(target,
				new[] { new CollectionNativeRootState(ModDeploymentRoot.Data, "C:\\Game\\Data") },
				mods, files, iniEdits ?? new CollectionNativeIniState[0], new CollectionNativeGameValueState[0],
				new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable,
				new CollectionTargetAssociation[0], new CollectionMemberBinding[0], new UserOverride[0],
				CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 7);
		}
	}
}
