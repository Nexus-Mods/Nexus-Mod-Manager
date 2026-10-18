using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.Witcher3;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class Witcher3RemasteredComponentHealthInspectorTests
	{
		private const string Merged = "mod0000_MergedFiles";

		private static Witcher3ComponentNativeOwner Owner(string key)
		{
			return new Witcher3ComponentNativeOwner(new InstallLogReadMod(key, key + ".zip",
				key + ".zip", "Archive " + key, "1", "2", "1", "1", false,
				ModInstallRoot.Data, ModInstallMethod.Direct, false), 1, 1);
		}

		private static Witcher3ComponentFile File(string relative,
			Witcher3ComponentFileSource source = Witcher3ComponentFileSource.UnmanagedDiskFile,
			bool? observed = true)
		{
			var keys = source == Witcher3ComponentFileSource.UnmanagedDiskFile
				? new string[0] : new[] { "ownerA" };
			return new Witcher3ComponentFile(relative, source, keys,
				keys.Length == 0 ? null : "ownerA", false, false, observed);
		}

		private static Witcher3NativeComponent Component(string name, bool? observed = true,
			bool dlc = false, IEnumerable<Witcher3ComponentNativeOwner> owners = null,
			IEnumerable<Witcher3ComponentFile> files = null)
		{
			string folder = (dlc ? @"DLC\" : @"Mods\") + name;
			Witcher3ComponentKind kind = dlc ? Witcher3ComponentKind.Dlc
				: name.Equals(Merged, StringComparison.OrdinalIgnoreCase)
					? Witcher3ComponentKind.MergedOutputCandidate : Witcher3ComponentKind.Mod;
			return new Witcher3NativeComponent(folder, kind, observed,
				owners ?? new Witcher3ComponentNativeOwner[0],
				files ?? new Witcher3ComponentFile[0]);
		}

		private static Witcher3RemasteredComponentHealthReport Inspect(bool complete = true,
			params Witcher3NativeComponent[] components)
		{
			return Witcher3RemasteredComponentHealthInspector.Inspect(
				new Witcher3NativeComponentIndex(42, components,
					new Witcher3ComponentIssue[0], complete));
		}

		private static Witcher3RemasteredComponentHealthIssue Find(
			Witcher3RemasteredComponentHealthReport report, Witcher3RemasteredComponentHealthCode code)
		{
			return report.Issues.Single(issue => issue.Code == code);
		}

		[Test]
		public void EmptyCompleteInventory_DoesNotInventHealthWarnings()
		{
			var report = Inspect();
			Assert.That(report.ModFolderCount, Is.Zero);
			Assert.That(report.ObservedModFolderCount, Is.Zero);
			Assert.That(report.HasCompleteDiskInventory, Is.True);
			Assert.That(report.Issues, Is.Empty);
		}

		[Test]
		public void FolderNameBoundary_OnlyObservedModNamesBeyond63AreFlagged()
		{
			var report = Inspect(true, Component("mod" + new string('m', 60)),
				Component("mod" + new string('m', 61)));
			var issue = Find(report, Witcher3RemasteredComponentHealthCode.FolderNameExceedsLimit);
			Assert.That(report.ModFolderCount, Is.EqualTo(2));
			Assert.That(issue.FolderNameLength, Is.EqualTo(64));
			Assert.That(issue.GameRelativeFolderPath, Is.EqualTo(@"Mods\mod" + new string('m', 61)));
			Assert.That(issue.Severity, Is.EqualTo(Witcher3RemasteredHealthSeverity.Warning));
		}

		[Test]
		public void FolderLimitUsesConservativeUtf16CodeUnits_AndExcludesModsPrefix()
		{
			var report = Inspect(true, Component("mod" + new string('x', 58) + "😀"));
			// 3 + 58 + 2 UTF-16 units = 63, not 64.
			Assert.That(report.Issues, Is.Empty);
			var tooLong = Inspect(true, Component("mod" + new string('x', 59) + "😀"));
			Assert.That(Find(tooLong, Witcher3RemasteredComponentHealthCode.FolderNameExceedsLimit)
				.FolderNameLength, Is.EqualTo(64));
		}

		[Test]
		public void DlcIsExcludedFromFolderLengthAndMergeChecks()
		{
			var report = Inspect(true, Component("dlc" + new string('d', 87), dlc: true),
				Component(Merged, dlc: true));
			Assert.That(report.Issues, Is.Empty);
			Assert.That(report.ModFolderCount, Is.Zero);
		}

		[Test]
		public void CompleteDiskScan_MissingLongOrMergedFolderIsNotDeclaredPresent()
		{
			var report = Inspect(true, Component("mod" + new string('m', 69), false),
				Component(Merged, false));
			Assert.That(report.Issues, Is.Empty);
			Assert.That(report.ObservedModFolderCount, Is.Zero);
		}

		[Test]
		public void PartialScan_ReportsUncertaintyInsteadOfInventingDiskPresence()
		{
			var report = Inspect(false, Component("mod" + new string('m', 62), null),
				Component(Merged, null));
			Assert.That(report.Issues.Count, Is.EqualTo(3));
			Assert.That(Find(report, Witcher3RemasteredComponentHealthCode.InventoryIncomplete)
				.Severity, Is.EqualTo(Witcher3RemasteredHealthSeverity.Information));
			Assert.That(Find(report, Witcher3RemasteredComponentHealthCode.FolderNameExceedsLimit)
				.Severity, Is.EqualTo(Witcher3RemasteredHealthSeverity.Information));
			Assert.That(Find(report, Witcher3RemasteredComponentHealthCode.LegacyMergePresenceUnconfirmed)
				.Severity, Is.EqualTo(Witcher3RemasteredHealthSeverity.Information));
			Assert.That(report.Issues.All(issue => issue.Severity != Witcher3RemasteredHealthSeverity.Warning), Is.True);
		}

		[Test]
		public void ObservedEmptyMergeFolderIsCautionNotProofOfIncompatibleScripts()
		{
			var report = Inspect(true, Component(Merged));
			Assert.That(report.Issues.Select(issue => issue.Code),
				Is.EqualTo(new[] { Witcher3RemasteredComponentHealthCode.LegacyMergedFolderObserved }));
			Assert.That(report.Issues.Single().Origin,
				Is.EqualTo(Witcher3RemasteredComponentOrigin.ExternalObserved));
			Assert.That(report.Issues.Single().ObservedScriptCount, Is.Zero);
		}

		[Test]
		public void LegacyMergedScriptWarningRequiresPositivelyObservedScriptFile()
		{
			var report = Inspect(true, Component(Merged, files: new[]
			{
				File(@"Mods\mod0000_MergedFiles\content\scripts\a.ws"),
				File(@"Mods\mod0000_MergedFiles\content\scripts\b.WS"),
				File(@"Mods\mod0000_MergedFiles\content\scripts\stale.ws", observed: false),
				File(@"Mods\mod0000_MergedFiles\content\textures\tex.ws"),
				File(@"Mods\mod0000_MergedFiles\content\scripts\note.txt")
			}));
			Assert.That(Find(report, Witcher3RemasteredComponentHealthCode.LegacyMergedScriptsObserved)
				.ObservedScriptCount, Is.EqualTo(2));
			Assert.That(Find(report, Witcher3RemasteredComponentHealthCode.LegacyMergedFolderObserved)
				.Severity, Is.EqualTo(Witcher3RemasteredHealthSeverity.Warning));
		}

		[Test]
		public void HistoricalManagedScriptPathAloneCannotConfirmPresentMergeScripts()
		{
			var report = Inspect(true, Component(Merged, files: new[]
			{
				File(@"Mods\mod0000_MergedFiles\content\scripts\old.ws",
					Witcher3ComponentFileSource.LegacyInstallLog, false)
			}));
			Assert.That(report.Issues.Any(issue => issue.Code ==
				Witcher3RemasteredComponentHealthCode.LegacyMergedScriptsObserved), Is.False);
		}

		[Test]
		public void CompilerTriggerAndMergerLookalikesAreNotClassifiedAsLegacyOutput()
		{
			var report = Inspect(true, Component("mod0000____CompilationTrigger"),
				Component("mod0000_MergedFilesBackup"), Component("modOtherMerge"));
			Assert.That(report.Issues, Is.Empty);
		}

		[Test]
		public void NativeOwnerAndUnmanagedFileAreReportedAsMixed_NotExternal()
		{
			var report = Inspect(true, Component(Merged, owners: new[] { Owner("ownerA") },
				files: new[]
				{
					File(@"Mods\mod0000_MergedFiles\content\scripts\a.ws",
						Witcher3ComponentFileSource.LegacyInstallLog),
					File(@"Mods\mod0000_MergedFiles\content\scripts\manual.ws")
				}));
			var issue = Find(report, Witcher3RemasteredComponentHealthCode.LegacyMergedFolderObserved);
			Assert.That(issue.Origin, Is.EqualTo(Witcher3RemasteredComponentOrigin.MixedNativeAndExternal));
			Assert.That(issue.NativeContributorCount, Is.EqualTo(1));
		}

		[Test]
		public void MultipleNativeOwnersAreAttributedWithoutClaimingToolOrScriptCompatibility()
		{
			var report = Inspect(true, Component(Merged, owners: new[] { Owner("one"), Owner("two") },
				files: new[] { File(@"Mods\mod0000_MergedFiles\content\scripts\merged.ws",
					Witcher3ComponentFileSource.PromotedDeployment) }));
			var issue = Find(report, Witcher3RemasteredComponentHealthCode.LegacyMergedScriptsObserved);
			Assert.That(issue.Origin, Is.EqualTo(Witcher3RemasteredComponentOrigin.NativeManaged));
			Assert.That(issue.NativeContributorCount, Is.EqualTo(2));
		}

		[Test]
		public void NoRecordedOwnerAndUnobservedFolder_UsesUnconfirmedOrigin()
		{
			var report = Inspect(false, Component(Merged, observed: null));
			Assert.That(Find(report, Witcher3RemasteredComponentHealthCode.LegacyMergePresenceUnconfirmed)
				.Origin, Is.EqualTo(Witcher3RemasteredComponentOrigin.Unconfirmed));
		}

		[Test]
		public void LargeInventoryIsInspectedWithoutNewDiskReadsOrCollectionWideRenames()
		{
			var components = Enumerable.Range(0, 1500)
				.Select(i => Component("mod" + i.ToString("D5"))).ToArray();
			var report = Inspect(true, components);
			Assert.That(report.ModFolderCount, Is.EqualTo(1500));
			Assert.That(report.ObservedModFolderCount, Is.EqualTo(1500));
			Assert.That(report.Issues, Is.Empty);
		}

		[Test]
		public void NullIndexFailsWithoutReadingOrModifyingAnyFiles()
		{
			Assert.Throws<ArgumentNullException>(() => Witcher3RemasteredComponentHealthInspector.Inspect(null));
		}
	}
}
