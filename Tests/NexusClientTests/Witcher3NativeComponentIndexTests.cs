using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.Witcher3;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>W3R.3: Native ownership projection is read-only; no live installer, Witcher game, or archive is required.</summary>
	[TestFixture]
	public class Witcher3NativeComponentIndexTests
	{
		private const string Original = "original-values";
		private static string Root => Path.Combine(Path.GetTempPath(), "nmm-w3r-native-index-fixture");

		private static ModDeploymentTarget Target(string relative, ModDeploymentRoot root = ModDeploymentRoot.Data)
		{
			return ModDeploymentTargetResolver.FromCanonical(root, relative);
		}

		private static InstallLogReadMod Mod(string key, ModInstallMethod method = ModInstallMethod.Direct, bool hidden = false)
		{
			return new InstallLogReadMod(key, key + ".zip", key + ".zip", "NMM " + key,
				"123", "456", "1.0", "1.0", false, ModInstallRoot.Data, method, hidden);
		}

		private static InstallLogReadFile Legacy(string path, params string[] owners)
		{
			return new InstallLogReadFile(path, Target(path), owners);
		}

		private static InstallLogReadDeploymentTarget Promoted(string path, params string[] owners)
		{
			return new InstallLogReadDeploymentTarget(Target(path), owners);
		}

		private static NativeStateCaptureSnapshot State(
			IEnumerable<InstallLogReadMod> mods = null, IEnumerable<InstallLogReadFile> files = null,
			IEnumerable<InstallLogReadDeploymentTarget> promoted = null,
			IEnumerable<VirtualModReadLink> virtualLinks = null,
			IEnumerable<NativeStateCaptureRoot> roots = null,
			NativeStateCaptureCoverage coverage = NativeStateCaptureCoverage.NotApplicable,
			IEnumerable<NativeStateCaptureIssue> issues = null)
		{
			var registrations = (mods ?? new InstallLogReadMod[0]).ToList();
			var promotions = (promoted ?? new InstallLogReadDeploymentTarget[0]).ToList();
			var log = new InstallLogReadSnapshot(Original, 37,
				registrations, files ?? new InstallLogReadFile[0],
				new InstallLogReadIniEdit[0], new InstallLogReadGameValue[0], promotions);
			var promotedCapture = promotions.Select(record => new NativeStateCaptureDeploymentTarget(
				record.Target, String.Empty, record.OwnerKeys.Select((key, index) =>
				{
					InstallLogReadMod mod = registrations.FirstOrDefault(x => x.ModKey == key);
					var kind = key == Original ? NativeStateCaptureDeploymentOwnerKind.OriginalValue
						: mod == null ? NativeStateCaptureDeploymentOwnerKind.Unresolved
						: mod.InstallMethod == ModInstallMethod.Virtual ? NativeStateCaptureDeploymentOwnerKind.Virtual
						: NativeStateCaptureDeploymentOwnerKind.Direct;
					return new NativeStateCaptureDeploymentOwner(key, kind,
						index == record.OwnerKeys.Count - 1, String.Empty);
				}))).ToArray();
			if (promotions.Count > 0 && coverage == NativeStateCaptureCoverage.NotApplicable)
				coverage = NativeStateCaptureCoverage.Complete;
			return new NativeStateCaptureSnapshot(log, new VirtualModReadSnapshot(virtualLinks ?? new VirtualModReadLink[0]),
				roots ?? new[]
				{
					new NativeStateCaptureRoot(ModDeploymentRoot.Data, Root),
					new NativeStateCaptureRoot(ModDeploymentRoot.GameRoot, Root)
				},
				promotedCapture, coverage, new NativeStateCaptureReplayReference[0],
				new NativeStateCapturePlugin[0], NativeStateCaptureCoverage.NotApplicable,
				issues ?? new NativeStateCaptureIssue[0]);
		}

		private static Witcher3NativeComponentIndex Index(NativeStateCaptureSnapshot state,
			Witcher3ComponentDiskObservation disk = null)
		{
			return Witcher3NativeComponentIndexer.Build(state, Root, disk);
		}

		private static Witcher3ComponentDiskObservation Disk(IEnumerable<string> files = null,
			IEnumerable<string> directories = null, bool complete = true)
		{
			return new Witcher3ComponentDiskObservation(Root, files ?? new string[0],
				directories ?? new string[0], complete);
		}

		[Test]
		public void EmptyCommittedState_NeverCreatesGameDirectoryOrInventsMods()
		{
			Witcher3NativeComponentIndex result = Index(State());
			Assert.That(result.Components, Is.Empty);
			Assert.That(result.Issues, Is.Empty);
			Assert.That(result.HasCompleteDiskInventory, Is.False);
			Assert.That(result.NativeDeploymentCommitSequence, Is.EqualTo(37));
		}

		[Test]
		public void OneNativeArchiveWithTwoModFolders_ProducesIndependentComponents()
		{
			var result = Index(State(new[] { Mod("one") }, new[]
			{
				Legacy(@"Mods\modAlpha\content\one.ws", "one"),
				Legacy(@"Mods\modBeta\content\two.ws", "one")
			}));
			Assert.That(result.Components.Select(x => x.GameRelativeFolderPath), Is.EqualTo(new[]
			{
				@"Mods\modAlpha", @"Mods\modBeta"
			}));
			Assert.That(result.Components.All(x => x.Owners.Single().NativeModKey == "one"), Is.True);
			Assert.That(result.Components.All(x => x.IsModLoadOrderCandidate), Is.True);
		}

		[Test]
		public void DifferentArchivesContributeSeparateFilesToOneFolder()
		{
			var result = Index(State(new[] { Mod("one"), Mod("two") }, new[]
			{
				Legacy(@"Mods\modShared\content\one.ws", "one"),
				Legacy(@"Mods\modShared\content\two.ws", "two")
			}));
			var folder = result.Components.Single();
			Assert.That(folder.Owners.Select(x => x.NativeModKey), Is.EquivalentTo(new[] { "one", "two" }));
			Assert.That(folder.Files.Count, Is.EqualTo(2));
			Assert.That(folder.Owners.All(x => x.WinningFileCount == 1), Is.True);
		}

		[Test]
		public void TwoArchivesInSameFileOwnerStack_PreserveShadowedAndWinningOwners()
		{
			var result = Index(State(new[] { Mod("one"), Mod("two") }, new[]
			{
				Legacy(@"Mods\modShared\content\same.ws", Original, "one", "two")
			}));
			var folder = result.Components.Single();
			var file = folder.Files.Single();
			Assert.That(file.NativeOwnerKeys, Is.EqualTo(new[] { "one", "two" }));
			Assert.That(file.IntendedWinnerKey, Is.EqualTo("two"));
			Assert.That(file.HasOriginalFallback, Is.True);
			Assert.That(folder.Owners.Single(x => x.NativeModKey == "one").WinningFileCount, Is.Zero);
			Assert.That(folder.Owners.Single(x => x.NativeModKey == "two").WinningFileCount, Is.EqualTo(1));
		}

		[Test]
		public void DlcFoldersRemainSeparateAndNeverBecomeLoadOrderEntries()
		{
			var result = Index(State(new[] { Mod("mixed") }, new[]
			{
				Legacy(@"Mods\modX\content\x.ws", "mixed"),
				Legacy(@"DLC\dlcY\content\y.bundle", "mixed")
			}));
			Assert.That(result.Components.Count, Is.EqualTo(2));
			Assert.That(result.Components.Single(x => x.Kind == Witcher3ComponentKind.Dlc).IsModLoadOrderCandidate, Is.False);
			Assert.That(result.Components.Single(x => x.Kind == Witcher3ComponentKind.Mod).IsModLoadOrderCandidate, Is.True);
		}

		[Test]
		public void PureVirtualActiveLink_IsIndexedWithoutInstallLogFileRecord()
		{
			var target = Target(@"Mods\modVirtual\content\one.ws");
			var result = Index(State(new[] { Mod("virtual", ModInstallMethod.Virtual) }, virtualLinks: new[]
			{
				new VirtualModReadLink(target, "virtual", "internal", true, 3, "source", "live")
			}));
			Assert.That(result.Components.Single().Files.Single().Source, Is.EqualTo(Witcher3ComponentFileSource.VirtualLink));
			Assert.That(result.Components.Single().Owners.Single().InstallMethod, Is.EqualTo(ModInstallMethod.Virtual));
			Assert.That(result.Components.Single().Files.Single().IntendedWinnerKey, Is.EqualTo("virtual"));
		}

		[Test]
		public void InactiveVirtualLinksAreNotMistakenForDeployedFiles()
		{
			var target = Target(@"Mods\modDormant\content\dormant.ws");
			var result = Index(State(new[] { Mod("virtual", ModInstallMethod.Virtual) }, virtualLinks: new[]
			{
				new VirtualModReadLink(target, "virtual", "internal", false, 4, "source")
			}));
			Assert.That(result.Components, Is.Empty);
		}

		[Test]
		public void AmbiguousActiveVirtualLinksNeverInventAWinner()
		{
			var target = Target(@"Mods\modV\content\same.ws");
			var result = Index(State(new[] { Mod("one", ModInstallMethod.Virtual), Mod("two", ModInstallMethod.Virtual) },
				virtualLinks: new[]
				{
					new VirtualModReadLink(target, "one", "one", true, 1, "one"),
					new VirtualModReadLink(target, "two", "two", true, 2, "two")
				}));
			Assert.That(result.Issues.Select(x => x.Kind), Does.Contain(Witcher3ComponentIssueKind.AmbiguousVirtualWinner));
			Assert.That(result.Components.Single().Files.Single().WinnerAmbiguous, Is.True);
			Assert.That(result.Components.Single().Files.Single().IntendedWinnerKey, Is.Null);
			Assert.That(result.Components.Single().Owners.Count, Is.EqualTo(2));
		}

		[Test]
		public void PromotedOwnershipSupersedesOldInstallLogAndVirtualEntries()
		{
			string path = @"Mods\modPromoted\content\one.ws";
			var target = Target(path);
			var result = Index(State(new[] { Mod("direct"), Mod("virtual", ModInstallMethod.Virtual) },
				files: new[] { Legacy(path, "direct") },
				promoted: new[] { Promoted(path, Original, "direct", "virtual") },
				virtualLinks: new[] { new VirtualModReadLink(target, "virtual", "v", true, 5, "file") }));
			var file = result.Components.Single().Files.Single();
			Assert.That(file.Source, Is.EqualTo(Witcher3ComponentFileSource.PromotedDeployment));
			Assert.That(file.NativeOwnerKeys, Is.EqualTo(new[] { "direct", "virtual" }));
			Assert.That(file.IntendedWinnerKey, Is.EqualTo("virtual"));
			Assert.That(file.HasOriginalFallback, Is.True);
			Assert.That(result.Issues, Is.Empty);
		}

		[Test]
		public void PromotedCaptureWithDisagreeingLiveOwners_IsFlagged()
		{
			string path = @"Mods\modPromoted\content\one.ws";
			var registered = State(new[] { Mod("direct"), Mod("virtual", ModInstallMethod.Virtual) },
				promoted: new[] { Promoted(path, "direct", "virtual") });
			var mismatched = new NativeStateCaptureSnapshot(registered.InstallLog, registered.VirtualState,
				registered.Roots,
				new[] { new NativeStateCaptureDeploymentTarget(Target(path), "", new[]
				{
					new NativeStateCaptureDeploymentOwner("virtual", NativeStateCaptureDeploymentOwnerKind.Virtual, false, ""),
					new NativeStateCaptureDeploymentOwner("direct", NativeStateCaptureDeploymentOwnerKind.Direct, true, "")
				}) }, NativeStateCaptureCoverage.Complete, registered.ReplayReferences, registered.Plugins,
				NativeStateCaptureCoverage.NotApplicable, registered.Issues);
			var result = Index(mismatched);
			Assert.That(result.Issues.Select(x => x.Kind), Does.Contain(Witcher3ComponentIssueKind.ConflictingNativeTarget));
			Assert.That(result.Components.Single().Files.Single().IntendedWinnerKey, Is.EqualTo("virtual"),
					"The committed registry remains the display reference; the disagreement blocks write authority.");
		}

		[Test]
		public void LegacyVirtualRecordWithoutActiveVmaLink_IsNotDeclaredDeployed()
		{
			string path = @"Mods\modStale\content\one.ws";
			var result = Index(State(new[] { Mod("virtual", ModInstallMethod.Virtual) },
				files: new[] { Legacy(path, "virtual") }));
			Assert.That(result.Components, Is.Empty);
			Assert.That(result.Issues.Select(x => x.Kind), Does.Contain(Witcher3ComponentIssueKind.UnverifiedVirtualTarget));
		}

		[Test]
		public void LegacyAndVirtualSameTargetWithoutPromotion_IsFlaggedRatherThanSilentlyMerged()
		{
			string path = @"Mods\modMixed\content\one.ws";
			var result = Index(State(new[] { Mod("direct"), Mod("virtual", ModInstallMethod.Virtual) },
				files: new[] { Legacy(path, "direct") }, virtualLinks: new[]
				{
					new VirtualModReadLink(Target(path), "virtual", "virtual", true, 0, "source")
				}));
			Assert.That(result.Issues.Select(x => x.Kind), Does.Contain(Witcher3ComponentIssueKind.ConflictingNativeTarget));
			Assert.That(result.Components.Single().Files.Single().IntendedWinnerKey, Is.EqualTo("virtual"));
		}

		[Test]
		public void ExternalAndMixedFoldersRequireActualDiskObservation()
		{
			string managed = @"Mods\modShared\content\managed.ws";
			var result = Index(State(new[] { Mod("nmm") }, files: new[] { Legacy(managed, "nmm") }),
				Disk(new[] { managed, @"Mods\modShared\content\manual.ws", @"Mods\modManual\content\manual.ws" },
					new[] { @"Mods\modShared", @"Mods\modManual" }));
			var shared = result.Components.Single(x => x.FolderName == "modShared");
			var manual = result.Components.Single(x => x.FolderName == "modManual");
			Assert.That(shared.HasMixedManagedAndUnmanagedFiles, Is.True);
			Assert.That(shared.UnmanagedFileCount, Is.EqualTo(1));
			Assert.That(manual.IsEntirelyExternal, Is.True);
			Assert.That(manual.Owners, Is.Empty);
			Assert.That(manual.Files.Single().Source, Is.EqualTo(Witcher3ComponentFileSource.UnmanagedDiskFile));
			Assert.That(result.HasCompleteDiskInventory, Is.True);
		}

		[Test]
		public void DiskInventoryCanPreserveManualEmptyFolderWithoutInventingAnOwner()
		{
			var result = Index(State(), Disk(directories: new[] { @"Mods\modEmpty" }));
			Assert.That(result.Components.Single().IsEntirelyExternal, Is.True);
			Assert.That(result.Components.Single().ObservedOnDisk, Is.True);
			Assert.That(result.Components.Single().Files, Is.Empty);
		}

		[Test]
		public void GeneratedMergerFolderIsOnlyAHeuristicNotNativeOwnership()
		{
			var result = Index(State(), Disk(new[] { @"Mods\mod0000_MergedFiles\content\merged.ws" }));
			Assert.That(result.Components.Single().IsMergeCandidate, Is.True);
			Assert.That(result.Components.Single().IsEntirelyExternal, Is.True);
			Assert.That(result.Components.Single().IsModLoadOrderCandidate, Is.True);
		}

		[Test]
		public void CompleteScanReportsMissingNativeTargetButRetainsOwnership()
		{
			string path = @"Mods\modMissing\content\one.ws";
			var result = Index(State(new[] { Mod("nmm") }, files: new[] { Legacy(path, "nmm") }), Disk());
			Assert.That(result.Components.Single().Files.Single().ObservedOnDisk, Is.False);
			Assert.That(result.Components.Single().Owners.Single().NativeModKey, Is.EqualTo("nmm"));
			Assert.That(result.Issues.Select(x => x.Kind), Does.Contain(Witcher3ComponentIssueKind.MissingObservedNativeFile));
		}

		[Test]
		public void PartialDiskScanNeverClaimsAnUnobservedNativeFileIsMissing()
		{
			string path = @"Mods\modMissing\content\one.ws";
			var result = Index(State(new[] { Mod("nmm") }, files: new[] { Legacy(path, "nmm") }),
				Disk(complete: false));
			Assert.That(result.Components.Single().Files.Single().ObservedOnDisk, Is.Null);
			Assert.That(result.Issues.Select(x => x.Kind), Does.Contain(Witcher3ComponentIssueKind.DiskScanIncomplete));
			Assert.That(result.Issues.Any(x => x.Kind == Witcher3ComponentIssueKind.MissingObservedNativeFile), Is.False);
		}

		[Test]
		public void SlashSeparatedDiskFileMatchesBackslashSeparatedNativeTarget()
		{
			string path = @"Mods\modA\content\one.ws";
			var result = Index(State(new[] { Mod("nmm") }, files: new[] { Legacy(path, "nmm") }),
				Disk(new[] { "Mods/modA/content/one.ws" }));
			Assert.That(result.Components.Single().Files.Count, Is.EqualTo(1));
			Assert.That(result.Components.Single().Files.Single().ObservedOnDisk, Is.True);
			Assert.That(result.Components.Single().UnmanagedFileCount, Is.Zero);
		}

		[Test]
		public void DiskEvidenceCannotBeAppliedToAnotherInstallRoot()
		{
			var wrongDisk = new Witcher3ComponentDiskObservation(Path.GetTempPath(),
				new string[0], new string[0], true);
			Assert.Throws<ArgumentException>(() => Index(State(), wrongDisk));
		}

		[Test]
		public void MissingPhysicalRootRecordNeverFallsBackToAssumedGameRoot()
		{
			var result = Index(State(new[] { Mod("nmm") }, files: new[]
			{
				Legacy(@"Mods\modA\content\one.ws", "nmm")
			}, roots: new NativeStateCaptureRoot[0]));
			Assert.That(result.Components, Is.Empty);
			Assert.That(result.Issues.Select(x => x.Kind), Does.Contain(Witcher3ComponentIssueKind.PhysicalRootUnavailable));
		}

		[Test]
		public void ProvenNestedNativeRootMapsToGameRelativeComponentWithoutGuessing()
		{
			string path = @"modA\content\one.ws";
			var file = new InstallLogReadFile(path, Target(path, ModDeploymentRoot.Secondary), new[] { "nmm" });
			var result = Index(State(new[] { Mod("nmm") }, files: new[] { file },
				roots: new[] { new NativeStateCaptureRoot(ModDeploymentRoot.Secondary, Path.Combine(Root, "Mods")) }));
			Assert.That(result.Components.Single().GameRelativeFolderPath, Is.EqualTo(@"Mods\modA"));
			Assert.That(result.Components.Single().Files.Single().GameRelativePath,
				Is.EqualTo(@"Mods\modA\content\one.ws"));
		}

		[Test]
		public void SiblingGameRootIsNeverTreatedAsThisInstall()
		{
			string sibling = Path.Combine(Path.GetTempPath(), "nmm-w3r-other-install");
			var result = Index(State(new[] { Mod("nmm") }, files: new[]
			{
				Legacy(@"Mods\modA\content\one.ws", "nmm")
			}, roots: new[] { new NativeStateCaptureRoot(ModDeploymentRoot.Data, sibling) }));
			Assert.That(result.Components, Is.Empty);
			Assert.That(result.Issues.Select(x => x.Kind), Does.Contain(Witcher3ComponentIssueKind.TargetOutsideGameRoot));
		}

		[Test]
		public void DuplicatePhysicalPathThroughNativeRootsCannotChooseAWinner()
		{
			var samePath = @"Mods\modA\content\one.ws";
			var byGameRoot = new InstallLogReadFile(samePath,
				Target(samePath, ModDeploymentRoot.GameRoot), new[] { "two" });
			var result = Index(State(new[] { Mod("one"), Mod("two") }, files: new[]
			{
				Legacy(samePath, "one"), byGameRoot
			}));
			Assert.That(result.Components.Single().Files.Single().WinnerAmbiguous, Is.True);
			Assert.That(result.Components.Single().Files.Single().NativeOwnerKeys, Is.EquivalentTo(new[] { "one", "two" }));
			Assert.That(result.Issues.Select(x => x.Kind), Does.Contain(Witcher3ComponentIssueKind.ConflictingNativeTarget));
		}

		[Test]
		public void MissingAndHiddenRegistrationsCannotBeReportedAsManagedOwners()
		{
			var result = Index(State(new[] { Mod("hidden", hidden: true) }, files: new[]
			{
				Legacy(@"Mods\modA\content\one.ws", Original, "hidden", "missing")
			}));
			Assert.That(result.Components.Single().Owners, Is.Empty);
			Assert.That(result.Components.Single().Files.Single().WinnerAmbiguous, Is.True);
			Assert.That(result.Issues.Count(x => x.Kind == Witcher3ComponentIssueKind.UnresolvedNativeOwner), Is.GreaterThanOrEqualTo(2));
		}

		[Test]
		public void UnresolvedNativeOwnershipIsNotMislabeledAsExternalManualMod()
		{
			string path = @"Mods\modUnknown\content\one.ws";
			var result = Index(State(files: new[] { Legacy(path, "unknown") }),
				Disk(new[] { path }));
			Assert.That(result.Components.Single().Owners, Is.Empty);
			Assert.That(result.Components.Single().IsEntirelyExternal, Is.False);
			Assert.That(result.Issues.Select(x => x.Kind), Does.Contain(Witcher3ComponentIssueKind.UnresolvedNativeOwner));
		}

		[Test]
		public void NativeCaptureIssuesArePropagatedAsIncompleteStatus()
		{
			var result = Index(State(coverage: NativeStateCaptureCoverage.Partial, issues: new[]
			{
				new NativeStateCaptureIssue(NativeStateCaptureIssueKind.DeploymentOwnerUnresolved,
					"Mods\\modA", "missing owner")
			}));
			Assert.That(result.HasNoReportedIssues, Is.False);
			Assert.That(result.Issues.Select(x => x.Kind), Does.Contain(Witcher3ComponentIssueKind.NativeCaptureIncomplete));
		}

		[Test]
		public void UnrecognizedModFolderIsNotPromotedToManagedLoadOrderEntry()
		{
			var result = Index(State(new[] { Mod("nmm") }, files: new[]
			{
				Legacy(@"Mods\Textures\content\one.ws", "nmm")
			}));
			Assert.That(result.Components, Is.Empty);
			Assert.That(result.Issues.Select(x => x.Kind), Does.Contain(Witcher3ComponentIssueKind.UnrecognizedComponentFolder));
		}

		[Test]
		public void AbsolutePathsAndTraversalInDiskInventoryAreRejected()
		{
			var result = Index(State(), Disk(new[] { @"Mods\modA\..\modB\content\x.ws", "C:/escape.ws" }));
			Assert.That(result.Components, Is.Empty);
			Assert.That(result.Issues.Select(x => x.Kind), Does.Contain(Witcher3ComponentIssueKind.InvalidComponentPath));
		}

		[Test]
		public void MalformedDiskEvidenceCannotProveRecordedFileIsAbsent()
		{
			string path = @"Mods\modA\content\native.ws";
			var result = Index(State(new[] { Mod("nmm") }, files: new[] { Legacy(path, "nmm") }),
				Disk(files: new[] { @"Mods\modA\..\bad.ws" }, complete: true));
			Assert.That(result.HasCompleteDiskInventory, Is.False);
			Assert.That(result.Components.Single().Files.Single().ObservedOnDisk, Is.Null);
			Assert.That(result.Issues.Select(x => x.Kind), Does.Contain(Witcher3ComponentIssueKind.InvalidComponentPath));
			Assert.That(result.Issues.Any(x => x.Kind == Witcher3ComponentIssueKind.MissingObservedNativeFile), Is.False);
		}

		[Test]
		public void CaseAliasIsReportedRatherThanCreatingTwoWindowsComponents()
		{
			var result = Index(State(new[] { Mod("one"), Mod("two") }, files: new[]
			{
				Legacy(@"Mods\modAlpha\content\one.ws", "one"),
				Legacy(@"Mods\MODALPHA\content\two.ws", "two")
			}));
			Assert.That(result.Components.Count, Is.EqualTo(1));
			Assert.That(result.Issues.Select(x => x.Kind), Does.Contain(Witcher3ComponentIssueKind.InconsistentComponentCase));
		}
	}

}
