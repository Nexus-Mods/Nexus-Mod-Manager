using System;
using System.IO;
using System.Linq;
using Nexus.Client.Games.DataDriven;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Verifies that shipped LOOT commands select the exact game identifier expected by LOOT.</summary>
	[TestFixture]
	public class LootSupportedToolsTests
	{
		/// <summary>Loads the actual packaged definitions so missing tools and incorrect edition mappings are detected.</summary>
		[TestCase("Enderal", "Enderal")]
		[TestCase("EnderalSE", "Enderal Special Edition")]
		[TestCase("Fallout3", "Fallout3")]
		[TestCase("Fallout4", "Fallout4")]
		[TestCase("Fallout4VR", "Fallout4VR")]
		[TestCase("FalloutNV", "FalloutNV")]
		[TestCase("Oblivion", "Oblivion")]
		[TestCase("OblivionRemastered", "Oblivion Remastered")]
		[TestCase("Skyrim", "Skyrim")]
		[TestCase("SkyrimGOG", "Skyrim Special Edition")]
		[TestCase("SkyrimSE", "Skyrim Special Edition")]
		[TestCase("SkyrimVR", "Skyrim VR")]
		[TestCase("Starfield", "Starfield")]
		public void LootCommand_SelectsCurrentGame(string directory, string gameIdentifier)
		{
			string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameModes", "Definitions", directory, "game.json");
			GameModeDefinitionLoadResult result = new GameModeDefinitionLoader().LoadFromPath(path);
			Assert.That(result.HasErrors, Is.False, string.Join(Environment.NewLine, result.Issues));
			Assert.That(result.Definitions.Count, Is.EqualTo(1), path);
			GameModeToolDefinition tool = result.Definitions.Single().SupportedTools.Single(item => item.Id == "LOOT");
			Assert.That(tool.ExecutableName, Is.EqualTo("LOOT.exe"));
			CollectionAssert.AreEqual(new[] { "--game=" + gameIdentifier },
				tool.ArgumentTokens.Where(token => token.StartsWith("--game=", StringComparison.Ordinal)));
			Assert.That(tool.DiscoveryRules, Is.Not.Empty);
			Assert.That(tool.ArgumentTokens.Any(token => token.StartsWith("--auto-sort", StringComparison.Ordinal)), Is.False);
			string customIdentifier = gameIdentifier + " (Steam) (1)";
			string settings = "[[games]]\nfolder = '" + gameIdentifier + "'\ngameId = '" + gameIdentifier + "'\npath = 'C:\\Games\\Old'\n" +
				"[[games]]\nfolder = '" + customIdentifier + "'\ngameId = '" + gameIdentifier + "'\npath = 'C:\\Games\\Current'\n";
			Assert.That(Resolve(gameIdentifier, @"C:\Games\Current", settings), Is.EqualTo(customIdentifier));
		}

		/// <summary>Preserves existing installation-path selection for games that already configure it.</summary>
		[TestCase("SkyrimGOG")]
		[TestCase("Starfield")]
		public void LootCommand_PreservesConfiguredGamePath(string directory)
		{
			string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameModes", "Definitions", directory, "game.json");
			GameModeDefinition definition = new GameModeDefinitionLoader().LoadFromPath(path).Definitions.Single();
			GameModeToolDefinition tool = definition.SupportedTools.Single(item => item.Id == "LOOT");
			Assert.That(tool.ArgumentTokens, Does.Contain("--game-path={GamePath}"));
		}

		/// <summary>Supports literal Windows paths, comments, case differences, and trailing directory separators.</summary>
		[Test]
		public void InstallationMatching_SelectsSkyrimSteamInstanceByPath()
		{
			string settings = "# folder = 'Wrong'\n[[games]] # installed Steam game\n" +
				"folder = 'Skyrim Special Edition (Steam) (1)' # keep user metadata\n" +
				"gameId = 'Skyrim Special Edition'\npath = 'I:\\Games\\Skyrim SE\\'\n";
			Assert.That(Resolve("Skyrim Special Edition", @"i:\games\Skyrim SE", settings),
				Is.EqualTo("Skyrim Special Edition (Steam) (1)"));
		}

		/// <summary>Preserves exact identifier spelling and supports escaped TOML basic strings.</summary>
		[Test]
		public void InstallationMatching_DecodesQuotedNamesAndPaths()
		{
			string settings = "[[games]]\nfolder = \"NV \\\"custom\\\" #1\"\ngameId = \"FalloutNV\"\npath = \"C:\\\\Games\\\\New Vegas\"\n";
			Assert.That(Resolve("FalloutNV", @"C:\Games\New Vegas\", settings), Is.EqualTo("NV \"custom\" #1"));
		}

		/// <summary>Never selects another game's configuration merely because it shares an installation path.</summary>
		[Test]
		public void InstallationMatching_RejectsDifferentGameType()
		{
			string settings = "[[games]]\nfolder = 'Fallout3'\ngameId = 'Fallout3'\npath = 'C:\\Games\\Current'\n";
			Assert.That(Resolve("FalloutNV", @"C:\Games\Current", settings), Is.EqualTo("FalloutNV"));
		}

		/// <summary>Retains legacy LOOT entries without gameId when their installation path matches.</summary>
		[Test]
		public void InstallationMatching_AcceptsOlderSettingsWithoutGameId()
		{
			string settings = "[[games]]\nfolder = 'My Skyrim'\npath = 'C:\\Games\\Current'\n";
			Assert.That(Resolve("Skyrim Special Edition", @"C:\Games\Current", settings), Is.EqualTo("My Skyrim"));
		}

		/// <summary>Missing, partial, unsupported, duplicate, or ambiguous entries preserve the shipped fallback.</summary>
		[TestCase("")]
		[TestCase("[[games]]\nfolder = 'Custom'\n")]
		[TestCase("[[games]]\nfolder = 'Custom'\npath = 'C:\\Games\\Other'\n")]
		[TestCase("[[games]]\nfolder = 'Custom'\npath = 'relative/path'\n")]
		[TestCase("[[games]]\nfolder = 'Custom'\npath = '''C:\\Games\\Current'''\n")]
		[TestCase("[[games]]\nfolder = 'Custom'\npath = \"C:\\Games\\Current\"\n")]
		[TestCase("[[games]]\nfolder = 'Custom'\npath = 'C:\\Games\\Current'\nfolder = 'Duplicate'\n")]
		[TestCase("[[games]]\nfolder = 'Custom'\n[other]\npath = 'C:\\Games\\Current'\n")]
		[TestCase("[[games]]\nfolder = 'One'\npath = 'C:\\Games\\Current'\n[[games]]\nfolder = 'Two'\npath = 'C:\\Games\\Current'\n")]
		public void InstallationMatching_InvalidOrAmbiguousSettingsKeepDefault(string settings)
		{
			Assert.That(Resolve("FalloutNV", @"C:\Games\Current", settings), Is.EqualTo("FalloutNV"));
		}

		/// <summary>Missing files and invalid current paths do not prevent an ordinary LOOT launch.</summary>
		[Test]
		public void InstallationMatching_UnavailableSettingsKeepDefault()
		{
			string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.toml");
			Assert.That(LootGameSelection.ResolveGameIdentifier("FalloutNV", @"C:\Games\Current", missing), Is.EqualTo("FalloutNV"));
			Assert.That(LootGameSelection.ResolveGameIdentifier("FalloutNV", "C:\\Invalid\0Path", missing), Is.EqualTo("FalloutNV"));
		}

		/// <summary>Prefers the existing default entry when several aliases refer to the same installed game.</summary>
		[Test]
		public void InstallationMatching_PrefersMatchingDefaultEntry()
		{
			string settings = "[[games]]\nfolder = 'Custom'\ngameId = 'FalloutNV'\npath = 'C:\\Games\\Current'\n" +
				"[[games]]\nfolder = 'FalloutNV'\ngameId = 'FalloutNV'\npath = 'C:\\Games\\Current'\n";
			Assert.That(Resolve("FalloutNV", @"C:\Games\Current", settings), Is.EqualTo("FalloutNV"));
		}

		/// <summary>Uses isolated settings without reading or modifying the developer's LOOT configuration.</summary>
		private static string Resolve(string identifier, string gamePath, string settings)
		{
			string path = Path.Combine(Path.GetTempPath(), "nmm-loot-" + Guid.NewGuid().ToString("N") + ".toml");
			try
			{
				File.WriteAllText(path, settings);
				return LootGameSelection.ResolveGameIdentifier(identifier, gamePath, path);
			}
			finally { File.Delete(path); }
		}
	}
}
