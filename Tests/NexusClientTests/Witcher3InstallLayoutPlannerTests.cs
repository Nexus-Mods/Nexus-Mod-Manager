using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.Games.Witcher3;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Pure archive path characterization. No Witcher installation, staging path or registry needed.</summary>
	[TestFixture]
	public class Witcher3InstallLayoutPlannerTests
	{
		private static Witcher3InstallLayoutPlan Plan(params string[] paths)
		{
			return Witcher3InstallLayoutPlanner.Plan(paths.Select(path => new Witcher3LayoutInputFile(path)),
				"nexus:witcher3:1234:5678", "Example Mod", Witcher3Edition.Remastered);
		}

		[Test]
		public void ExplicitModFolder_IsRelativeToGameRootAndNeverDoublePrefixed()
		{
			Witcher3InstallLayoutPlan result = Plan(@"Mods\modArmor\content\scripts\armor.ws");
			Assert.That(result.IsUnambiguous, Is.True);
			Assert.That(result.Wrapper, Is.Empty);
			Assert.That(result.Files.Single().GameRelativeTargetPath,
				Is.EqualTo(@"Mods\modArmor\content\scripts\armor.ws"));
			Assert.That(result.Files.Single().GameRelativeComponentPath, Is.EqualTo(@"Mods\modArmor"));
			Assert.That(result.Files.Single().Kind, Is.EqualTo(Witcher3LayoutKind.ModComponent));
			Assert.That(result.RequiresNativeOwnershipReview, Is.True);
		}

		[Test]
		public void BareModFolder_IsRootedExactlyOnce()
		{
			Witcher3InstallLayoutPlan result = Plan("modCombat/content/scripts/combat.ws");
			Assert.That(result.IsUnambiguous, Is.True);
			Assert.That(result.Files.Single().GameRelativeTargetPath,
				Is.EqualTo(@"Mods\modCombat\content\scripts\combat.ws"));
		}

		[Test]
		public void DlcOnlyAndBareDlc_AreNotModLoadOrderComponents()
		{
			Witcher3InstallLayoutPlan result = Plan("DLC/dlcQuest/content/bundles/quest.bundle",
				"dlcExtras/content/metadata.store");
			Assert.That(result.IsUnambiguous, Is.True);
			Assert.That(result.Files.Select(file => file.Kind).Distinct().Single(),
				Is.EqualTo(Witcher3LayoutKind.DlcComponent));
			Assert.That(result.Files.Select(file => file.GameRelativeComponentPath),
				Is.EquivalentTo(new[] { @"DLC\dlcQuest", @"DLC\dlcExtras" }));
		}

		[Test]
		public void MixedWrappedArchive_MapsEachComponentAndPreservesReadme()
		{
			Witcher3InstallLayoutPlan result = Plan("Witcher Package/Mods/modA/content/a.ws",
				"Witcher Package/Mods/modB/content/b.ws",
				"Witcher Package/DLC/dlcA/content/a.bundle", "Witcher Package/README.md");
			Assert.That(result.IsUnambiguous, Is.True);
			Assert.That(result.Wrapper, Is.EqualTo("Witcher Package"));
			Assert.That(result.Files.Count, Is.EqualTo(3));
			Assert.That(result.Files.Select(file => file.GameRelativeTargetPath), Is.EquivalentTo(new[]
			{
				@"Mods\modA\content\a.ws", @"Mods\modB\content\b.ws", @"DLC\dlcA\content\a.bundle"
			}));
			Assert.That(result.IgnoredDocumentation.Single(), Is.EqualTo("Witcher Package/README.md"));
		}

		[Test]
		public void NestedWrapper_RecognizedWithoutArbitrarySearch()
		{
			Witcher3InstallLayoutPlan result = Plan("Release/Game/Mods/modX/content/a.ws");
			Assert.That(result.Wrapper, Is.EqualTo(@"Release\Game"));
			Assert.That(result.IsUnambiguous, Is.True);
		}

		[Test]
		public void MixedRootAndWrappedPayload_IsBlockedRatherThanPartlyInstalled()
		{
			Witcher3InstallLayoutPlan result = Plan("Mods/modA/content/a.ws",
				"GameRoot/DLC/dlcB/content/b.bundle");
			Assert.That(result.Issues.Select(issue => issue.Kind),
				Does.Contain(Witcher3LayoutIssueKind.InconsistentWrapper));
			Assert.That(result.IsUnambiguous, Is.False);
			Assert.That(result.Files, Is.Empty);
			Assert.That(result.Wrapper, Is.Null);
		}

		[Test]
		public void RawContent_CreatesStableUniqueLengthBoundedFolder()
		{
			string[] archive = { "content/scripts/a.ws", "content/scripts/b.ws" };
			Witcher3InstallLayoutPlan first = Plan(archive);
			Witcher3InstallLayoutPlan reordered = Plan(archive.Reverse().ToArray());
			Witcher3InstallLayoutPlan otherArchive = Witcher3InstallLayoutPlanner.Plan(
				archive.Select(path => new Witcher3LayoutInputFile(path)), "nexus:witcher3:ANOTHER", "Example Mod",
				Witcher3Edition.Remastered);
			Assert.That(first.IsUnambiguous, Is.True);
			string generated = first.Files[0].GameRelativeComponentPath;
			Assert.That(generated, Does.StartWith(@"Mods\modExample_Mod_"));
			Assert.That(generated.Split('\\')[1].Length, Is.LessThanOrEqualTo(63));
			Assert.That(first.Files.Select(file => file.GameRelativeComponentPath).Distinct().Single(), Is.EqualTo(generated));
			Assert.That(reordered.Files[0].GameRelativeComponentPath, Is.EqualTo(generated));
			Assert.That(otherArchive.Files[0].GameRelativeComponentPath, Is.Not.EqualTo(generated));
		}

		[Test]
		public void RawContent_RequiresStablePackageIdentityAndDisplayStem()
		{
			Witcher3InstallLayoutPlan result = Witcher3InstallLayoutPlanner.Plan(
				new[] { new Witcher3LayoutInputFile("content/a.ws") }, null, "Example");
			Assert.That(result.IsUnambiguous, Is.False);
			Assert.That(result.Issues.Select(issue => issue.Kind),
				Does.Contain(Witcher3LayoutIssueKind.MissingPackageIdentity));
			Assert.That(result.Files, Is.Empty);
		}

		[Test]
		public void RemasteredFolderLimit_ExplicitNamesAreNeverSilentlyChanged()
		{
			string longName = "mod" + new string('x', 61);
			string path = "Mods/" + longName + "/content/a.ws";
			Witcher3InstallLayoutPlan remastered = Plan(path);
			Witcher3InstallLayoutPlan legacy = Witcher3InstallLayoutPlanner.Plan(
				new[] { new Witcher3LayoutInputFile(path) }, null, null, Witcher3Edition.Legacy);
			Assert.That(remastered.Issues.Select(issue => issue.Kind),
				Does.Contain(Witcher3LayoutIssueKind.ModFolderTooLong));
			Assert.That(remastered.Files.Single().GameRelativeTargetPath, Does.Contain(longName));
			Assert.That(legacy.IsUnambiguous, Is.True);
		}

		[TestCase("../Mods/modA/content/a.ws")]
		[TestCase("Mods/modA/../../evil")]
		[TestCase("C:/Games/Witcher3/Mods/modA/content/a.ws")]
		[TestCase("/Mods/modA/content/a.ws")]
		[TestCase("Mods//modA/content/a.ws")]
		[TestCase("Mods/modA./content/a.ws")]
		[TestCase("Mods/CON/content/a.ws")]
		[TestCase("Mods/modA/content/file.txt:evil")]
		[TestCase("Mods/modA/content/a.ws/")]
		public void UnsafeOrPlatformAmbiguousPaths_AreRejected(string unsafePath)
		{
			Witcher3InstallLayoutPlan result = Plan(unsafePath);
			Assert.That(result.Issues.Select(issue => issue.Kind),
				Does.Contain(Witcher3LayoutIssueKind.InvalidPath));
			Assert.That(result.IsUnambiguous, Is.False);
			Assert.That(result.Files, Is.Empty);
		}

		[Test]
		public void InvalidSelectedDestination_IsRejectedEvenWhenArchiveSourceIsSafe()
		{
			Witcher3InstallLayoutPlan result = Witcher3InstallLayoutPlanner.Plan(new[]
			{
				new Witcher3LayoutInputFile("payload/clean.ws", @"Mods\modA\content\..\secret.ws")
			}, "stable", "Example");
			Assert.That(result.Issues.Select(issue => issue.Kind),
				Does.Contain(Witcher3LayoutIssueKind.InvalidPath));
		}

		[Test]
		public void SelectedScriptedDestination_IsBasedOnSelectionNotArchivePath()
		{
			Witcher3InstallLayoutPlan result = Witcher3InstallLayoutPlanner.Plan(new[]
			{
				new Witcher3LayoutInputFile("fomod/options/selected.ws", "Mods/modA/content/scripts/selected.ws")
			}, "stable", "Example");
			Assert.That(result.IsUnambiguous, Is.True);
			Assert.That(result.Files.Single().ArchivePath, Is.EqualTo("fomod/options/selected.ws"));
			Assert.That(result.Files.Single().GameRelativeTargetPath,
				Is.EqualTo(@"Mods\modA\content\scripts\selected.ws"));
		}

		[Test]
		public void UnresolvedInstallerSelectionAndFomodMetadata_AreNotTreatedAsGameFiles()
		{
			Witcher3InstallLayoutPlan unresolved = Witcher3InstallLayoutPlanner.Plan(new[]
			{
				new Witcher3LayoutInputFile("fomod/options/option.ws", null)
			}, "stable", "Example");
			Assert.That(unresolved.Issues.Select(issue => issue.Kind),
				Does.Contain(Witcher3LayoutIssueKind.UnresolvedInstallerSelection));
			Witcher3InstallLayoutPlan metadata = Plan("fomod/ModuleConfig.xml", "Mods/modA/content/a.ws");
			Assert.That(metadata.Issues.Select(issue => issue.Kind),
				Does.Contain(Witcher3LayoutIssueKind.UnresolvedInstallerSelection));
		}

		[Test]
		public void DestinationCaseAliasesAndCaseVariantComponentFolders_AreBlocked()
		{
			Witcher3InstallLayoutPlan duplicate = Plan("Mods/modA/content/A.ws", "mods/moda/content/a.ws");
			Assert.That(duplicate.Issues.Select(issue => issue.Kind),
				Does.Contain(Witcher3LayoutIssueKind.DuplicateDestination));
			Assert.That(duplicate.IsUnambiguous, Is.False);
			Assert.That(duplicate.Issues.Select(issue => issue.Kind),
				Does.Contain(Witcher3LayoutIssueKind.InconsistentComponentCase));
			Witcher3InstallLayoutPlan componentCase = Plan("Mods/modA/content/A.ws", "Mods/moda/content/B.ws");
			Assert.That(componentCase.Issues.Select(issue => issue.Kind),
				Does.Contain(Witcher3LayoutIssueKind.InconsistentComponentCase));
		}

		[Test]
		public void FileAndDirectoryAliasing_IsBlocked()
		{
			Witcher3InstallLayoutPlan result = Plan("Mods/modA/content/scripts/a.ws",
				"Mods/modA/content/scripts/a.ws/child");
			Assert.That(result.Issues.Select(issue => issue.Kind),
				Does.Contain(Witcher3LayoutIssueKind.FileDirectoryCollision));
		}

		[Test]
		public void ExistingComponentAndExactTarget_AreBothFlaggedWithoutPresumingOwnership()
		{
			Witcher3InstallLayoutPlan result = Witcher3InstallLayoutPlanner.Plan(new[]
			{
				new Witcher3LayoutInputFile("Mods/modA/content/new.ws"),
				new Witcher3LayoutInputFile("Mods/modB/content/old.ws")
			}, "stable", "Example", Witcher3Edition.Remastered,
				new[] { @"Mods\modA", @"Mods\modB\content\old.ws" });
			Assert.That(result.Issues.Select(issue => issue.Kind),
				Does.Contain(Witcher3LayoutIssueKind.ExistingComponentRequiresOwnershipReview));
			Assert.That(result.Issues.Select(issue => issue.Kind),
				Does.Contain(Witcher3LayoutIssueKind.ExistingTargetCollision));
			Assert.That(result.Issues.Count(issue => issue.Kind == Witcher3LayoutIssueKind.ExistingTargetCollision),
				Is.EqualTo(1)); // Existing component directory is not itself an occupied file.
		}

		[Test]
		public void ExistingModsRootDirectoryByItself_IsNotAnOccupiedFile()
		{
			Witcher3InstallLayoutPlan result = Witcher3InstallLayoutPlanner.Plan(new[]
			{
				new Witcher3LayoutInputFile("Mods/modA/content/new.ws")
			}, "stable", "Example", Witcher3Edition.Remastered,
				new[] { "Mods", "DLC" });
			Assert.That(result.IsUnambiguous, Is.True);
		}

		[Test]
		public void ExistingFileDirectoryCollision_IsBlocked()
		{
			Witcher3InstallLayoutPlan result = Witcher3InstallLayoutPlanner.Plan(new[]
			{
				new Witcher3LayoutInputFile("Mods/modA/content/config")
			}, "stable", "Example", Witcher3Edition.Remastered,
				new[] { "Mods/modA/content/config/example.ws" });
			Assert.That(result.Issues.Select(issue => issue.Kind),
				Does.Contain(Witcher3LayoutIssueKind.ExistingTargetCollision));
		}

		[Test]
		public void BadInventoryPath_FailsClosed()
		{
			Witcher3InstallLayoutPlan result = Witcher3InstallLayoutPlanner.Plan(new[]
			{
				new Witcher3LayoutInputFile("Mods/modA/content/a.ws")
			}, "stable", "Example", Witcher3Edition.Remastered,
				new[] { @"Mods\modA\content\..\unknown" });
			Assert.That(result.Issues.Select(issue => issue.Kind),
				Does.Contain(Witcher3LayoutIssueKind.InvalidOccupiedPath));
		}

		[Test]
		public void MenuMatrixXml_IsVisibleButRequiresMergeAdapter()
		{
			Witcher3InstallLayoutPlan result = Plan("bin/config/r4game/user_config_matrix/pc/hud.xml");
			Assert.That(result.Files.Single().Kind, Is.EqualTo(Witcher3LayoutKind.GameConfiguration));
			Assert.That(result.Files.Single().GameRelativeTargetPath,
				Is.EqualTo(@"bin\config\r4game\user_config_matrix\pc\hud.xml"));
			Assert.That(result.Issues.Select(issue => issue.Kind),
				Does.Contain(Witcher3LayoutIssueKind.ConfigurationMergeRequired));
		}

		[Test]
		public void DocumentsInputAndPartFragments_RequireDedicatedEffects()
		{
			Witcher3InstallLayoutPlan input = Plan("Documents/The Witcher 3/dx12user.settings");
			Assert.That(input.Issues.Select(issue => issue.Kind),
				Does.Contain(Witcher3LayoutIssueKind.DocumentSettingsRequireAdapter));
			Witcher3InstallLayoutPlan fragment = Plan("Mods/modA/content/hud.part.txt");
			Assert.That(fragment.Issues.Select(issue => issue.Kind),
				Does.Contain(Witcher3LayoutIssueKind.ConfigurationMergeRequired));
		}

		[Test]
		public void RemasteredDx11Matrix_IsRecognizedButNeverTreatedAsSupported()
		{
			Witcher3InstallLayoutPlan result = Plan("bin/config/r4game/user_config_matrix/pc/graphicsdx11.xml");
			Assert.That(result.Files.Single().Kind, Is.EqualTo(Witcher3LayoutKind.GameConfiguration));
			Assert.That(result.Issues.Select(issue => issue.Kind), Does.Contain(Witcher3LayoutIssueKind.UnsupportedPayload));
		}

		[Test]
		public void ExternalModSettings_IsNotMappedIntoTheGameRoot()
		{
			Witcher3InstallLayoutPlan result = Plan("mods.settings");
			Assert.That(result.Files, Is.Empty);
			Assert.That(result.Issues.Select(issue => issue.Kind),
				Does.Contain(Witcher3LayoutIssueKind.DocumentSettingsRequireAdapter));
		}

		[Test]
		public void FileOutsideDiscoveredWrapper_IsNotSilentlyDropped()
		{
			Witcher3InstallLayoutPlan result = Plan("Wrapper/Mods/modA/content/a.ws", "Other/data.bin");
			Assert.That(result.Files.Count, Is.EqualTo(1));
			Assert.That(result.Issues.Select(issue => issue.Kind),
				Does.Contain(Witcher3LayoutIssueKind.UnsupportedPayload));
			Assert.That(result.IsUnambiguous, Is.False);
		}

		[Test]
		public void DistinctSelectedDestinations_MayShareTheSameSourceFile()
		{
			Witcher3InstallLayoutPlan result = Witcher3InstallLayoutPlanner.Plan(new[]
			{
				new Witcher3LayoutInputFile("alternatives/common.ws", "Mods/modA/content/common.ws"),
				new Witcher3LayoutInputFile("alternatives/common.ws", "Mods/modB/content/common.ws")
			}, "stable", "Example");
			Assert.That(result.IsUnambiguous, Is.True);
			Assert.That(result.Files.Count, Is.EqualTo(2));
		}

		[Test]
		public void UnknownRootBinariesAndEmptySelection_DoNotGetDestinations()
		{
			Witcher3InstallLayoutPlan unknown = Plan("bin/x64_dx12/witcher3.exe");
			Assert.That(unknown.Issues.Select(issue => issue.Kind),
				Does.Contain(Witcher3LayoutIssueKind.UnsupportedPayload));
			Assert.That(unknown.Files, Is.Empty);
			Witcher3InstallLayoutPlan empty = Plan("README.txt", "LICENSE.md");
			Assert.That(empty.Issues.Select(issue => issue.Kind),
				Does.Contain(Witcher3LayoutIssueKind.MissingPayload));
			Assert.That(empty.IgnoredDocumentation.Count, Is.EqualTo(2));
		}

		[Test]
		public void NoMutationOccurs_EvenWhenInventoryHasConflicts()
		{
			var original = new List<string> { "Mods/modA/content/a.ws" };
			var occupied = new List<string> { "Mods/modA/content/a.ws" };
			string[] sourceBefore = original.ToArray();
			string[] inventoryBefore = occupied.ToArray();
			Witcher3InstallLayoutPlan plan = Witcher3InstallLayoutPlanner.Plan(original.Select(x => new Witcher3LayoutInputFile(x)),
				"stable", "A", Witcher3Edition.Legacy, occupied);
			Assert.That(original, Is.EqualTo(sourceBefore));
			Assert.That(occupied, Is.EqualTo(inventoryBefore));
			Assert.That(plan.IsUnambiguous, Is.False);
		}
	}
}
