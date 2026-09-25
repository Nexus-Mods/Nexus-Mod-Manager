using System;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.PluginManagement;
using Nexus.Client.Plugins;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// C7.5 INI/game-specific/plugin native effect capture characterization.
	/// </summary>
	public class CollectionNativeEffectCaptureReaderTests
	{
		[Test]
		public void Capture_PreservesIniAndGameValueOwnerHistoriesAndCurrentWinners()
		{
			byte[] originalBytes = { 1, 2 };
			byte[] firstBytes = { 3, 4 };
			byte[] winningBytes = { 5, 6 };
			InstallLogReadSnapshot install = CreateInstall(
				new[]
				{
					CreateMod("mod-a", false),
					CreateMod("mod-b", false)
				},
				new[]
				{
					new InstallLogReadIniEdit("settings.ini", "Display", "Mode", new[]
					{
						new InstallLogReadStringValue("original-values", "base"),
						new InstallLogReadStringValue("mod-a", "first"),
						new InstallLogReadStringValue("mod-b", "winner")
					})
				},
				new[]
				{
					new InstallLogReadGameValue("BinarySetting", new[]
					{
						new InstallLogReadBinaryValue("original-values", originalBytes),
						new InstallLogReadBinaryValue("mod-a", firstBytes),
						new InstallLogReadBinaryValue("mod-b", winningBytes)
					})
				});
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c75-effects");
			CollectionNativeEffectCaptureReader reader = CreateReader();

			CollectionNativeEffectSnapshot snapshot = reader.Capture(target, CreateNativeState(install,
				new NativeStateCapturePlugin[0], NativeStateCaptureCoverage.NotApplicable));

			Assert.AreEqual(NativeStateCaptureCoverage.Complete, snapshot.IniCoverage);
			Assert.AreEqual(NativeStateCaptureCoverage.Complete, snapshot.GameValueCoverage);
			Assert.AreEqual(NativeStateCaptureCoverage.NotApplicable, snapshot.PluginCoverage);
			Assert.AreEqual(NativeStateCaptureCoverage.Complete, snapshot.Coverage);
			CollectionCapturedIniEffect ini = snapshot.IniEdits.Single();
			CollectionAssert.AreEqual(new[] { "original-values", "mod-a", "mod-b" }, ini.Values.Select(x => x.OwnerKey).ToArray());
			Assert.AreEqual(CollectionNativeEffectOwnerKind.OriginalValue, ini.Values[0].OwnerKind);
			Assert.AreEqual(CollectionNativeEffectOwnerKind.NativeMod, ini.Values[1].OwnerKind);
			Assert.IsFalse(ini.Values[1].CurrentWinner);
			Assert.AreEqual("mod-b", ini.CurrentValue.OwnerKey);
			Assert.AreEqual("winner", ini.CurrentValue.Value);

			CollectionCapturedGameValueEffect gameValue = snapshot.GameValues.Single();
			Assert.AreEqual(CollectionNativeEffectOwnerKind.OriginalValue, gameValue.Values[0].OwnerKind);
			Assert.AreEqual(CollectionNativeEffectOwnerKind.NativeMod, gameValue.CurrentValue.OwnerKind);
			CollectionAssert.AreEqual(new byte[] { 5, 6 }, gameValue.CurrentValue.Value);
			winningBytes[0] = 99;
			byte[] exposed = gameValue.CurrentValue.Value;
			exposed[1] = 99;
			CollectionAssert.AreEqual(new byte[] { 5, 6 }, gameValue.CurrentValue.Value);
		}

		[Test]
		public void Capture_UnresolvedNativeEffectOwnersFailClosedWithoutInventingIdentity()
		{
			InstallLogReadSnapshot install = CreateInstall(new[] { CreateMod("active-mod", false) },
				new[]
				{
					new InstallLogReadIniEdit("settings.ini", "General", "Flag",
						new[] { new InstallLogReadStringValue("removed-owner", "1") })
				},
				new[]
				{
					new InstallLogReadGameValue("GameFlag",
						new[] { new InstallLogReadBinaryValue("removed-owner", new byte[] { 7 }) })
				});
			CollectionNativeEffectCaptureReader reader = CreateReader();

			CollectionNativeEffectSnapshot snapshot = reader.Capture(CollectionTargetIdentity.FromFingerprint("target-c75-unresolved"),
				CreateNativeState(install, new NativeStateCapturePlugin[0], NativeStateCaptureCoverage.NotApplicable));

			Assert.AreEqual(NativeStateCaptureCoverage.Partial, snapshot.IniCoverage);
			Assert.AreEqual(NativeStateCaptureCoverage.Partial, snapshot.GameValueCoverage);
			Assert.AreEqual(NativeStateCaptureCoverage.Partial, snapshot.Coverage);
			Assert.AreEqual(CollectionNativeEffectOwnerKind.Unresolved, snapshot.IniEdits.Single().CurrentValue.OwnerKind);
			Assert.AreEqual(CollectionNativeEffectOwnerKind.Unresolved, snapshot.GameValues.Single().CurrentValue.OwnerKind);
			Assert.IsTrue(snapshot.Issues.Any(x => x.Kind == CollectionNativeEffectIssueKind.UnresolvedIniOwner));
			Assert.IsTrue(snapshot.Issues.Any(x => x.Kind == CollectionNativeEffectIssueKind.UnresolvedGameValueOwner));
		}

		[Test]
		public void Capture_PreservesAmbiguousRecordsButMarksCaseAliasAndDuplicateKeysPartial()
		{
			InstallLogReadSnapshot install = CreateInstall(new InstallLogReadMod[0],
				new[]
				{
					new InstallLogReadIniEdit("Config.ini", "General", "Mode",
						new[] { new InstallLogReadStringValue("original-values", "A") }),
					new InstallLogReadIniEdit("config.ini", "general", "mode",
						new[] { new InstallLogReadStringValue("original-values", "B") })
				},
				new[]
				{
					new InstallLogReadGameValue("Duplicate", new[] { new InstallLogReadBinaryValue("original-values", new byte[] { 1 }) }),
					new InstallLogReadGameValue("Duplicate", new[] { new InstallLogReadBinaryValue("original-values", new byte[] { 2 }) })
				});
			CollectionNativeEffectCaptureReader reader = CreateReader();

			CollectionNativeEffectSnapshot snapshot = reader.Capture(CollectionTargetIdentity.FromFingerprint("target-c75-alias"),
				CreateNativeState(install, new NativeStateCapturePlugin[0], NativeStateCaptureCoverage.NotApplicable));

			Assert.AreEqual(2, snapshot.IniEdits.Count);
			Assert.AreEqual(2, snapshot.GameValues.Count);
			Assert.AreEqual(NativeStateCaptureCoverage.Partial, snapshot.IniCoverage);
			Assert.AreEqual(NativeStateCaptureCoverage.Partial, snapshot.GameValueCoverage);
			Assert.IsTrue(snapshot.Issues.Any(x => x.Kind == CollectionNativeEffectIssueKind.AmbiguousIniIdentity));
			Assert.IsTrue(snapshot.Issues.Any(x => x.Kind == CollectionNativeEffectIssueKind.DuplicateGameValueIdentity));
		}

		[Test]
		public void Capture_PreservesCompletePluginFinalStateInNativePriorityOrder()
		{
			InstallLogReadSnapshot install = CreateInstall(new InstallLogReadMod[0],
				new InstallLogReadIniEdit[0], new InstallLogReadGameValue[0]);
			var plugins = new[]
			{
				new NativeStateCapturePlugin("Late.esp", false, 1, null, String.Empty, PluginParseStatus.Parsed,
					PluginAddressClass.Full, PluginHeaderFlags.None, PluginSpecialFlags.None, false, 44,
					new[] { "Master.esm" }, new[]
					{
						new NativeStateCapturePluginDiagnostic(PluginValidationIssueKind.MissingMaster, PluginValidationSeverity.Warning)
					}),
				new NativeStateCapturePlugin("Master.esm", true, 0, 0, "00", PluginParseStatus.Parsed,
					PluginAddressClass.Full, PluginHeaderFlags.Master, PluginSpecialFlags.None, true, 44,
					new string[0], new NativeStateCapturePluginDiagnostic[0])
			};
			CollectionNativeEffectCaptureReader reader = CreateReader();

			CollectionNativeEffectSnapshot snapshot = reader.Capture(CollectionTargetIdentity.FromFingerprint("target-c75-plugins"),
				CreateNativeState(install, plugins, NativeStateCaptureCoverage.Complete));

			Assert.AreEqual(NativeStateCaptureCoverage.NotApplicable, snapshot.IniCoverage);
			Assert.AreEqual(NativeStateCaptureCoverage.NotApplicable, snapshot.GameValueCoverage);
			Assert.AreEqual(NativeStateCaptureCoverage.Complete, snapshot.PluginCoverage);
			Assert.AreEqual(NativeStateCaptureCoverage.Complete, snapshot.Coverage);
			CollectionAssert.AreEqual(new[] { "Master.esm", "Late.esp" }, snapshot.Plugins.Select(x => x.FileName).ToArray());
			Assert.IsTrue(snapshot.Plugins[0].Active);
			Assert.IsFalse(snapshot.Plugins[1].Active);
			Assert.AreEqual("Master.esm", snapshot.Plugins[1].Masters.Single());
			Assert.AreEqual(PluginValidationIssueKind.MissingMaster, snapshot.Plugins[1].Diagnostics.Single().Kind);
		}

		[Test]
		public void Capture_UnavailablePluginSurfacePropagatesNativeFailureInsteadOfClaimingCompleteness()
		{
			InstallLogReadSnapshot install = CreateInstall(new InstallLogReadMod[0],
				new InstallLogReadIniEdit[0], new InstallLogReadGameValue[0]);
			NativeStateCaptureSnapshot nativeState = CreateNativeState(install, new NativeStateCapturePlugin[0],
				NativeStateCaptureCoverage.Unavailable, new[]
				{
					new NativeStateCaptureIssue(NativeStateCaptureIssueKind.PluginStateUnavailable, "plugins", "plugin manager unavailable")
				});
			CollectionNativeEffectCaptureReader reader = CreateReader();

			CollectionNativeEffectSnapshot snapshot = reader.Capture(CollectionTargetIdentity.FromFingerprint("target-c75-plugin-unavailable"),
				nativeState);

			Assert.AreEqual(NativeStateCaptureCoverage.Unavailable, snapshot.PluginCoverage);
			Assert.AreEqual(NativeStateCaptureCoverage.Unavailable, snapshot.Coverage);
			CollectionNativeEffectIssue issue = snapshot.Issues.Single(x => x.Kind == CollectionNativeEffectIssueKind.PluginStateIncomplete);
			Assert.AreEqual("plugin manager unavailable", issue.Message);
		}

		[Test]
		public void Capture_AmbiguousPluginIdentityOrPriorityMarksPluginSurfacePartial()
		{
			InstallLogReadSnapshot install = CreateInstall(new InstallLogReadMod[0],
				new InstallLogReadIniEdit[0], new InstallLogReadGameValue[0]);
			var plugins = new[]
			{
				CreatePlugin("Same.esp", 0),
				CreatePlugin("same.ESP", 0)
			};
			CollectionNativeEffectCaptureReader reader = CreateReader();

			CollectionNativeEffectSnapshot snapshot = reader.Capture(CollectionTargetIdentity.FromFingerprint("target-c75-plugin-ambiguous"),
				CreateNativeState(install, plugins, NativeStateCaptureCoverage.Complete));

			Assert.AreEqual(NativeStateCaptureCoverage.Partial, snapshot.PluginCoverage);
			Assert.AreEqual(NativeStateCaptureCoverage.Partial, snapshot.Coverage);
			Assert.IsTrue(snapshot.Issues.Any(x => x.Kind == CollectionNativeEffectIssueKind.DuplicatePluginIdentity));
			Assert.IsTrue(snapshot.Issues.Any(x => x.Kind == CollectionNativeEffectIssueKind.AmbiguousPluginOrder));
		}

		private static InstallLogReadSnapshot CreateInstall(InstallLogReadMod[] mods, InstallLogReadIniEdit[] iniEdits,
			InstallLogReadGameValue[] gameValues)
		{
			return new InstallLogReadSnapshot("original-values", 41, mods, new InstallLogReadFile[0], iniEdits,
				gameValues, new InstallLogReadDeploymentTarget[0]);
		}

		private static InstallLogReadMod CreateMod(string key, bool hidden)
		{
			return new InstallLogReadMod(key, key + ".zip", key + ".zip", "1", "2", "1", "1", false,
				ModInstallRoot.Data, ModInstallMethod.Virtual, hidden);
		}

		private static NativeStateCapturePlugin CreatePlugin(string fileName, int priority)
		{
			return new NativeStateCapturePlugin(fileName, true, priority, priority, priority.ToString("X2"),
				PluginParseStatus.Parsed, PluginAddressClass.Full, PluginHeaderFlags.None, PluginSpecialFlags.None,
				false, 44, new string[0], new NativeStateCapturePluginDiagnostic[0]);
		}

		private static NativeStateCaptureSnapshot CreateNativeState(InstallLogReadSnapshot install,
			NativeStateCapturePlugin[] plugins, NativeStateCaptureCoverage pluginCoverage)
		{
			return CreateNativeState(install, plugins, pluginCoverage, new NativeStateCaptureIssue[0]);
		}

		private static NativeStateCaptureSnapshot CreateNativeState(InstallLogReadSnapshot install,
			NativeStateCapturePlugin[] plugins, NativeStateCaptureCoverage pluginCoverage, NativeStateCaptureIssue[] issues)
		{
			return new NativeStateCaptureSnapshot(install, new VirtualModReadSnapshot(new VirtualModReadLink[0]),
				new NativeStateCaptureVirtualPayloadSource[0], new NativeStateCaptureRoot[0],
				new NativeStateCaptureDeploymentTarget[0], NativeStateCaptureCoverage.NotApplicable,
				new NativeStateCaptureReplayReference[0], plugins, pluginCoverage, issues);
		}

		private static CollectionNativeEffectCaptureReader CreateReader()
		{
			IInstallLog installLog = InterfaceStub<IInstallLog>.Create((method, args) => null);
			IVirtualModActivator virtualModActivator = InterfaceStub<IVirtualModActivator>.Create((method, args) => null);
			IGameMode gameMode = InterfaceStub<IGameMode>.Create((method, args) => null);
			return new CollectionNativeEffectCaptureReader(new NativeStateCaptureReader(installLog, virtualModActivator,
				null, null, gameMode));
		}
	}
}
