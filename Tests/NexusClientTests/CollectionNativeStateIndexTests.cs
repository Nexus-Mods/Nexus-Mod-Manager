using System;
using System.Collections.Generic;
using Nexus.Client.CollectionManagement;
using Nexus.Client.ModManagement;
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
