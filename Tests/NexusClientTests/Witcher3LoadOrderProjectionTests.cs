using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.Games.Witcher3;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.Witcher3;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class Witcher3LoadOrderProjectionTests
	{
		private const string Original = "original-values";
		private static string Root => Path.Combine(Path.GetTempPath(), "nmm-w3r-loadorder-fixture");

		private static InstallLogReadMod Mod(string name) => new InstallLogReadMod(name, name + ".zip",
			name + ".zip", "NMM " + name, "1", "2", "1.0", "1.0",
			false, ModInstallRoot.Data, ModInstallMethod.Direct, false);

		private static InstallLogReadFile NativeFile(string relative, params string[] owners) =>
			new InstallLogReadFile(relative, ModDeploymentTargetResolver.FromCanonical(
				ModDeploymentRoot.Data, relative), owners);

		private static Witcher3NativeComponentIndex Index(IEnumerable<InstallLogReadMod> mods = null,
			IEnumerable<InstallLogReadFile> files = null,
			IEnumerable<string> diskFiles = null, IEnumerable<string> diskFolders = null,
			bool? diskComplete = null)
		{
			var log = new InstallLogReadSnapshot(Original, 41,
				mods ?? new InstallLogReadMod[0], files ?? new InstallLogReadFile[0],
				new InstallLogReadIniEdit[0], new InstallLogReadGameValue[0],
				new InstallLogReadDeploymentTarget[0]);
			var state = new NativeStateCaptureSnapshot(log,
				new VirtualModReadSnapshot(new VirtualModReadLink[0]), new[]
				{
					new NativeStateCaptureRoot(ModDeploymentRoot.Data, Root),
					new NativeStateCaptureRoot(ModDeploymentRoot.GameRoot, Root)
				}, new NativeStateCaptureDeploymentTarget[0],
				NativeStateCaptureCoverage.NotApplicable, new NativeStateCaptureReplayReference[0],
				new NativeStateCapturePlugin[0], NativeStateCaptureCoverage.NotApplicable,
				new NativeStateCaptureIssue[0]);
			Witcher3ComponentDiskObservation disk = diskComplete.HasValue
				? new Witcher3ComponentDiskObservation(Root, diskFiles ?? new string[0],
					diskFolders ?? new string[0], diskComplete.Value)
				: null;
			return Witcher3NativeComponentIndexer.Build(state, Root, disk);
		}

		private static Witcher3LoadOrderProjection Project(Witcher3NativeComponentIndex index,
			string settings, Witcher3Edition edition = Witcher3Edition.Legacy)
		{
			return Witcher3LoadOrderProjector.Project(index,
				Witcher3ModSettingsParser.Parse(settings), edition);
		}

		[Test]
		public void OneNativeArchiveWithTwoFolders_ProducesSeparatelyConfiguredRows()
		{
			var index = Index(new[] { Mod("one") }, new[]
			{
				NativeFile(@"Mods\modAlpha\content\a.ws", "one"),
				NativeFile(@"Mods\modBeta\content\b.ws", "one")
			});
			var result = Project(index, "[modAlpha]\nPriority=8\nEnabled=1\n[modBeta]\nPriority=2\nEnabled=0\n");
			Assert.That(result.Rows.Select(x => x.FolderName), Is.EqualTo(new[] { "modBeta", "modAlpha" }));
			Assert.That(result.Rows.Select(x => x.ConfiguredPriority), Is.EqualTo(new int?[] { 2, 8 }));
			Assert.That(result.Rows.Select(x => x.ConfiguredEnabled), Is.EqualTo(new bool?[] { false, true }));
			Assert.That(result.Rows.All(x => x.NativeContributors.Single().NativeModKey == "one"), Is.True);
			Assert.That(result.Rows.All(x => x.Origin == Witcher3LoadOrderOrigin.NativeManaged), Is.True);
			Assert.That(result.NativeDeploymentCommitSequence, Is.EqualTo(41));
		}

		[Test]
		public void SharedComponent_PreservesAllNativeOwnersWithoutCreatingDuplicateRows()
		{
			var index = Index(new[] { Mod("one"), Mod("two") }, new[]
			{
				NativeFile(@"Mods\modShared\content\a.ws", "one"),
				NativeFile(@"Mods\modShared\content\b.ws", "two")
			});
			var result = Project(index, "[modShared]\nPriority=3\n");
			Assert.That(result.Rows.Count, Is.EqualTo(1));
			Assert.That(result.Rows.Single().NativeContributors.Select(x => x.NativeModKey),
				Is.EquivalentTo(new[] { "one", "two" }));
		}

		[Test]
		public void DlcComponents_DoNotAppearInWitcherModLoadOrder()
		{
			var index = Index(new[] { Mod("combined") }, new[]
			{
				NativeFile(@"Mods\modA\content\a.ws", "combined"),
				NativeFile(@"DLC\dlcA\content\file.bundle", "combined")
			});
			var result = Project(index, "[modA]\nPriority=1\n");
			Assert.That(result.Rows.Select(x => x.FolderName), Is.EqualTo(new[] { "modA" }));
		}

		[Test]
		public void ManualDiskComponent_RemainsExternalAndDoesNotGainANativeOwner()
		{
			var index = Index(diskFolders: new[] { @"Mods\modHandInstalled" }, diskComplete: true);
			var result = Project(index, "[modHandInstalled]\nPriority=9\nVK=fake-native-key\n");
			var row = result.Rows.Single();
			Assert.That(row.Origin, Is.EqualTo(Witcher3LoadOrderOrigin.ExternalObserved));
			Assert.That(row.Presence, Is.EqualTo(Witcher3LoadOrderPresence.Observed));
			Assert.That(row.NativeContributors, Is.Empty);
			Assert.That(row.SettingsSections.Single().VortexKey, Is.EqualTo("fake-native-key"));
		}

		[Test]
		public void MixedManagedAndUnmanagedFiles_ReportedWithoutReassigningFileOwnership()
		{
			string owned = @"Mods\modMixed\content\a.ws";
			string manual = @"Mods\modMixed\content\b.ws";
			var index = Index(new[] { Mod("owned") }, new[] { NativeFile(owned, "owned") },
				diskFiles: new[] { owned, manual }, diskFolders: new[] { @"Mods\modMixed" }, diskComplete: true);
			var row = Project(index, "[modMixed]\nPriority=4\n").Rows.Single();
			Assert.That(row.Origin, Is.EqualTo(Witcher3LoadOrderOrigin.MixedNativeAndExternal));
			Assert.That(row.NativeContributors.Select(x => x.NativeModKey), Is.EqualTo(new[] { "owned" }));
		}

		[Test]
		public void UnknownSectionInCompleteScan_IsMarkedMissingNotAutomaticallyManaged()
		{
			var result = Project(Index(diskComplete: true), "[modGhost]\nPriority=1\n");
			var row = result.Rows.Single();
			Assert.That(row.Presence, Is.EqualTo(Witcher3LoadOrderPresence.MissingFromCompleteInventory));
			Assert.That(row.Origin, Is.EqualTo(Witcher3LoadOrderOrigin.Undetermined));
			Assert.That(result.Issues.Select(x => x.Kind), Does.Contain(Witcher3LoadOrderIssueKind.ConfiguredComponentMissing));
			Assert.That(result.Issues.Select(x => x.Kind), Does.Contain(Witcher3LoadOrderIssueKind.UnmatchedConfiguredComponent));
		}

		[Test]
		public void UnknownSectionWithoutDiskScan_IsNotAssumedMissingOrUnmanaged()
		{
			var result = Project(Index(), "[modGhost]\nPriority=1\n");
			Assert.That(result.Rows.Single().Presence, Is.EqualTo(
				Witcher3LoadOrderPresence.NotObservedOrInventoryIncomplete));
			Assert.That(result.Issues.Select(x => x.Kind),
				Does.Not.Contain(Witcher3LoadOrderIssueKind.ConfiguredComponentMissing));
		}

		[Test]
		public void NativeInventoryWithoutDiskScan_DoesNotClaimFilePresent()
		{
			var index = Index(new[] { Mod("one") }, new[] { NativeFile(@"Mods\modA\content\one.ws", "one") });
			var row = Project(index, "[modA]\nPriority=2\n").Rows.Single();
			Assert.That(row.Origin, Is.EqualTo(Witcher3LoadOrderOrigin.NativeManaged));
			Assert.That(row.Presence, Is.EqualTo(Witcher3LoadOrderPresence.NotObservedOrInventoryIncomplete));
		}

		[Test]
		public void NativeComponentMissingFromCompleteScan_IsNotTreatedAsDeployed()
		{
			var index = Index(new[] { Mod("one") }, new[] { NativeFile(@"Mods\modA\content\one.ws", "one") },
				diskComplete: true);
			var result = Project(index, "[modA]\nPriority=2\n");
			Assert.That(result.Rows.Single().Presence, Is.EqualTo(Witcher3LoadOrderPresence.MissingFromCompleteInventory));
			Assert.That(result.Issues.Select(x => x.Kind), Does.Contain(Witcher3LoadOrderIssueKind.NativeInventoryHasIssues));
		}

		[Test]
		public void NotListedComponent_UsesGameDefaultLabelWithoutInventedPriorityOrEnabled()
		{
			var index = Index(new[] { Mod("one") }, new[]
			{
				NativeFile(@"Mods\modUnlisted\content\one.ws", "one")
			});
			var row = Project(index, "[OtherTool]\nSomething=else\n").Rows.Single();
			Assert.That(row.ValueSource, Is.EqualTo(Witcher3LoadOrderValueSource.NotConfigured));
			Assert.That(row.ConfiguredPriority, Is.Null);
			Assert.That(row.ConfiguredEnabled, Is.Null);
			Assert.That(row.IsListedInSettings, Is.False);
		}

		[Test]
		public void DuplicateSections_InvalidateEffectiveOrderClaimsEvenWhenTheNumbersAreReadable()
		{
			var result = Project(Index(), "[modA]\nPriority=1\n[modA]\nPriority=2\n");
			Assert.That(result.Rows.All(x => x.ValueSource == Witcher3LoadOrderValueSource.ConfiguredButAmbiguous), Is.True);
			Assert.That(result.Issues.Select(x => x.Kind), Does.Contain(Witcher3LoadOrderIssueKind.SettingsHaveIssues));
			Assert.That(result.IsGameEffectiveOrderRuntimeVerified, Is.False);
		}

		[Test]
		public void CaseVariantSettingsMatchWindowsFolder_WithoutInventingSecondComponent()
		{
			var index = Index(new[] { Mod("one") }, new[] { NativeFile(@"Mods\modABC\content\one.ws", "one") });
			var result = Project(index, "[MODabc]\nPriority=7\n");
			Assert.That(result.Rows.Count, Is.EqualTo(1));
			Assert.That(result.Rows.Single().FolderName, Is.EqualTo("modABC"));
			Assert.That(result.Issues.Select(x => x.Kind),
				Does.Contain(Witcher3LoadOrderIssueKind.SettingsFolderNameCaseDiffers));
		}

		[Test]
		public void DuplicateCaseInsensitiveSettingsSections_DoNotExposeAFakeUniquePriority()
		{
			var result = Project(Index(), "[modA]\nPriority=1\n[MODA]\nPriority=2\n");
			Assert.That(result.Rows.Count, Is.EqualTo(1));
			Assert.That(result.Rows.Single().SettingsSections.Count, Is.EqualTo(2));
			Assert.That(result.Rows.Single().ConfiguredPriority, Is.Null);
			Assert.That(result.Rows.Single().ValueSource, Is.EqualTo(Witcher3LoadOrderValueSource.ConfiguredButAmbiguous));
		}

		[Test]
		public void RemasteredEdition_ExplicitPrioritiesRemainNotRuntimeVerified()
		{
			var result = Project(Index(), "[modA]\nPriority=1\nEnabled=0\n", Witcher3Edition.Remastered);
			Assert.That(result.ConfigurationMode, Is.EqualTo(Witcher3OrderConfigurationMode.ExternalConfigurationPresent));
			Assert.That(result.EffectEvidence, Is.EqualTo(Witcher3OrderEffectEvidence.RemasteredNotVerified));
			Assert.That(result.Rows.Single().ConfiguredEnabled, Is.False);
			Assert.That(result.IsGameEffectiveOrderRuntimeVerified, Is.False);
			Assert.That(result.IsNmmManagedOrder, Is.False);
			Assert.That(result.AuthorizesModifyingSettings, Is.False);
		}

		[Test]
		public void ReadResultMissing_MeansDefaultWithoutPretendingGameDisabledMods()
		{
			string path = Path.Combine(Path.GetTempPath(), "nmm-w3r-missing-" + Guid.NewGuid().ToString("N"), "mods.settings");
			var read = Witcher3ModSettingsFileReader.Read(path);
			var index = Index(new[] { Mod("one") }, new[] { NativeFile(@"Mods\modA\content\one.ws", "one") });
			var result = Witcher3LoadOrderProjector.Project(index, read);
			Assert.That(result.SettingsReadStatus, Is.EqualTo(Witcher3ModSettingsReadStatus.Missing));
			Assert.That(result.ConfigurationMode, Is.EqualTo(Witcher3OrderConfigurationMode.DefaultNoModOverrides));
			Assert.That(result.Rows.Single().ValueSource, Is.EqualTo(Witcher3LoadOrderValueSource.NotConfigured));
			Assert.That(result.Rows.Single().ConfiguredEnabled, Is.Null);
		}

		[Test]
		public void ReadResultFailed_DoesNotMasqueradeAsDefault()
		{
			string dir = Path.Combine(Path.GetTempPath(), "nmm-w3r-readerror-" + Guid.NewGuid().ToString("N"));
			try
			{
				Directory.CreateDirectory(dir);
				string path = Path.Combine(dir, "mods.settings");
				File.WriteAllBytes(path, Encoding.UTF8.GetBytes("[modA]\nPriority=1\n"));
				var read = Witcher3ModSettingsFileReader.Read(path, maxBytes: 1);
				var result = Witcher3LoadOrderProjector.Project(Index(), read);
				Assert.That(result.ConfigurationMode, Is.EqualTo(Witcher3OrderConfigurationMode.SettingsUnavailable));
				Assert.That(result.SettingsReadStatus, Is.EqualTo(Witcher3ModSettingsReadStatus.TooLarge));
				Assert.That(result.Issues.Select(x => x.Kind), Does.Contain(Witcher3LoadOrderIssueKind.SettingsCouldNotBeRead));
			}
			finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
		}

		[Test]
		public void UnknownEdition_DoesNotBorrowLegacyDocumentationAsRuntimeProof()
		{
			var result = Project(Index(), "[modA]\nPriority=3\n", Witcher3Edition.Ambiguous);
			Assert.That(result.EffectEvidence, Is.EqualTo(Witcher3OrderEffectEvidence.UnknownEdition));
		}

		[Test]
		public void UnknownNonModSectionsStayInDocument_ButNotInLoadOrderView()
		{
			var result = Project(Index(), "[OtherApp]\nPriority=2\n[dlcQuest]\nPriority=3\n");
			Assert.That(result.Rows, Is.Empty);
			Assert.That(result.SettingsDocument.Sections.Count, Is.EqualTo(2));
			Assert.That(result.ConfigurationMode, Is.EqualTo(Witcher3OrderConfigurationMode.DefaultNoModOverrides));
		}
	}
}
