using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.Games.Witcher3;
using Nexus.Client.ModManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class Witcher3BasicInstallMappingTests
	{
		private static readonly ModInstallContext VirtualData = new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data);
		private static readonly ModInstallContext DirectData = new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data);

		private static IReadOnlyList<KeyValuePair<string, string>> Map(ModInstallContext context, params string[] paths)
		{
			return Witcher3BasicInstallMapping.Map(paths.Select(path => new KeyValuePair<string, string>(path, null)),
				"file:nexus:1234:5678", "Armor Set", Witcher3Edition.Remastered, context);
		}

		[TestCase(ModInstallMethod.Direct)]
		[TestCase(ModInstallMethod.Virtual)]
		public void OneArchive_MultipleModsAndDlc_GetIndependentNativeTargets(ModInstallMethod method)
		{
			var files = Map(new ModInstallContext(method, ModInstallRoot.Data),
				@"pack\Mods\modA\content\a.ws", @"pack\Mods\modB\content\b.ws",
				@"pack\DLC\dlcC\content\c.bundle", @"pack\README.md");
			Assert.That(files.Select(file => file.Key), Is.EquivalentTo(new[]
			{
				@"pack\Mods\modA\content\a.ws", @"pack\Mods\modB\content\b.ws",
				@"pack\DLC\dlcC\content\c.bundle"
			}));
			Assert.That(files.Select(file => file.Value), Is.EquivalentTo(new[]
			{
				@"Mods\modA\content\a.ws", @"Mods\modB\content\b.ws",
				@"DLC\dlcC\content\c.bundle"
			}));
		}

		[Test]
		public void RawContent_GetsDeterministicComponentAndKeepsSourcePaths()
		{
			var first = Map(DirectData, "content/scripts/a.ws", "content/scripts/b.ws");
			var second = Map(VirtualData, "content/scripts/b.ws", "content/scripts/a.ws");
			Assert.That(first[0].Key, Is.EqualTo("content/scripts/a.ws"));
			Assert.That(first[0].Value, Does.StartWith(@"Mods\modArmor_Set_"));
			Assert.That(second.Single(file => file.Key == "content/scripts/a.ws").Value,
				Is.EqualTo(first[0].Value));
		}

		[Test]
		public void ExplicitSelectedDestination_IsHonored()
		{
			var result = Witcher3BasicInstallMapping.Map(new[]
			{
				new KeyValuePair<string, string>("package/file.ws", "Mods/modA/content/script.ws")
			}, "stable", "Mod", Witcher3Edition.Remastered, DirectData);
			Assert.That(result.Single().Key, Is.EqualTo("package/file.ws"));
			Assert.That(result.Single().Value, Is.EqualTo(@"Mods\modA\content\script.ws"));
		}

		[TestCase("fomod/ModuleConfig.xml")]
		[TestCase("Mods/modA/content/../evil")]
		[TestCase("bin/config/r4game/user_config_matrix/pc/hud.xml")]
		[TestCase("Documents/The Witcher 3/mods.settings")]
		[TestCase("Mods/modA/content/a.ws/../../escape")]
		public void UnsupportedOrUnsafeFile_RejectsWholeInstall(string other)
		{
			Assert.Throws<Witcher3InstallException>(() => Map(DirectData, "Mods/modGood/content/ok.ws", other));
		}

		[Test]
		public void DuplicateArchiveSource_IsNotDeployedToTwoDifferentTargets()
		{
			Assert.Throws<InvalidDataException>(() => Witcher3BasicInstallMapping.Map(new[]
			{
				new KeyValuePair<string, string>("same.ws", "Mods/modA/content/a.ws"),
				new KeyValuePair<string, string>("same.ws", "Mods/modB/content/b.ws")
			}, "stable", "Mod", Witcher3Edition.Remastered, DirectData));
		}

		[Test]
		public void DuplicateTarget_RejectsWholeInstall()
		{
			Assert.Throws<Witcher3InstallException>(() => Witcher3BasicInstallMapping.Map(new[]
			{
				new KeyValuePair<string, string>("first.ws", "Mods/modA/content/same.ws"),
				new KeyValuePair<string, string>("second.ws", "mods/moda/content/SAME.ws")
			}, "stable", "Mod", Witcher3Edition.Remastered, DirectData));
		}

		[Test]
		public void UnconfirmedEditionAndWrongRoot_NeverProduceMappings()
		{
			Assert.Throws<InvalidDataException>(() => Witcher3BasicInstallMapping.Map(
				new[] { new KeyValuePair<string, string>("Mods/modA/content/a.ws", null) },
				"stable", "Mod", Witcher3Edition.Ambiguous, DirectData));
			Assert.Throws<InvalidDataException>(() => Witcher3BasicInstallMapping.Map(
				new[] { new KeyValuePair<string, string>("Mods/modA/content/a.ws", null) },
				"stable", "Mod", Witcher3Edition.Remastered,
				new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.GameRoot)));
		}

		[Test]
		public void PathProjection_AcceptsOnlyAlreadyNormalizedWitcherTargets()
		{
			Assert.That(Witcher3BasicInstallMapping.ValidatePlannedPath("Mods/modA/content/a.ws"),
				Is.EqualTo(@"Mods\modA\content\a.ws"));
			Assert.That(Witcher3BasicInstallMapping.ValidatePlannedPath("DLC/dlcA/content/a.bundle"),
				Is.EqualTo(@"DLC\dlcA\content\a.bundle"));
			Assert.Throws<InvalidDataException>(() => Witcher3BasicInstallMapping.ValidatePlannedPath("content/a.ws"));
			Assert.Throws<InvalidDataException>(() => Witcher3BasicInstallMapping.ValidatePlannedPath("../Mods/modA/content/a.ws"));
			Assert.Throws<InvalidDataException>(() => Witcher3BasicInstallMapping.ValidatePlannedPath("bin/evil.dll"));
		}
	}
}
