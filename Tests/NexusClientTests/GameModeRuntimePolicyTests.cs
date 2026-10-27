using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.CollectionManagement;
using Nexus.Client.Games;
using Nexus.Client.Games.DataDriven;
using Nexus.Client.ModManagement;
using Nexus.Client.Settings;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Verifies that shared runtime behavior follows configuration rather than a known local game identifier.</summary>
	[TestFixture, NonParallelizable]
	public class GameModeRuntimePolicyTests
	{
		/// <summary>Rejects packaging artifacts before a nested package can lose its enclosing directory.</summary>
		[TestCase(@"__MACOSX\Wrapper\Example.pak", true)]
		[TestCase("Wrapper/__macosx/._Example.pak", true)]
		[TestCase(@"Wrapper\._Example.pak", true)]
		[TestCase(@"Wrapper\Example.pak", false)]
		public void ConfiguredArchiveFilters_RejectPackagingArtifacts(string path, bool excluded)
		{
			var policy = new GameModeModInstallDefinition { ExcludedArchiveDirectories = new[] { "__MACOSX" }, ExcludedFileNamePrefixes = new[] { "._" } };
			Assert.That(DataDrivenGameMode.IsConfiguredArchiveFileExcluded(policy, path), Is.EqualTo(excluded));
		}

		/// <summary>Restricts the primary directory to packages while preserving secondary native and loose payloads.</summary>
		[TestCase("Example.pak", false, "Example.pak")]
		[TestCase("Readme.txt", false, "")]
		[TestCase("order.lsx", false, "")]
		[TestCase(@"Data\Public\texture.dds", true, @"Data\Public\texture.dds")]
		[TestCase(@"bin\NativeMods\loader.dll", true, @"bin\NativeMods\loader.dll")]
		public void ConfiguredPrimaryExtensions_KeepOnlyPackages(string path, bool secondary, string expected)
		{
			var policy = new GameModeModInstallDefinition { PrimaryFileExtensions = new[] { ".pak" } };
			Assert.That(DataDrivenGameMode.FilterConfiguredPrimaryFile(policy, path, secondary), Is.EqualTo(expected));
		}

		/// <summary>Flattens configured packages without changing loose assets, native loaders or recorded absolute paths.</summary>
		[TestCase(@"Mods\Example\Example.pak", "Example.pak")]
		[TestCase("Wrapper/Packages/Example.PAK", "Example.PAK")]
		[TestCase("Example.pak", "Example.pak")]
		[TestCase(@"Data\Generated\Public\texture.dds", @"Data\Generated\Public\texture.dds")]
		[TestCase(@"bin\NativeMods\loader.dll", @"bin\NativeMods\loader.dll")]
		[TestCase(@"C:\Recorded\Mods\Old\Example.pak", @"C:\Recorded\Mods\Old\Example.pak")]
		public void ConfiguredPackageLayout_FlattensOnlyConfiguredRelativeFiles(string source, string expected)
		{
			var policy = new GameModeModInstallDefinition { FlattenFileExtensions = new[] { ".pak" } };
			Assert.That(DataDrivenGameMode.AdjustConfiguredFileDestination(policy, source), Is.EqualTo(expected));
		}

		/// <summary>Maps a flat loader while preserving an already routed path and unrelated DLLs.</summary>
		[TestCase("DWrite.dll", @"bin\DWrite.dll")]
		[TestCase("dwrite.DLL", @"bin\DWrite.dll")]
		[TestCase(@"bin\DWrite.dll", @"bin\DWrite.dll")]
		[TestCase(@"other\DWrite.dll", @"other\DWrite.dll")]
		[TestCase("Other.dll", "Other.dll")]
		public void ConfiguredFileDestination_MapsOnlyTheExactSource(string source, string expected)
		{
			var policy = new GameModeModInstallDefinition
			{
				FileDestinations = new List<GameModeFileDestinationDefinition>
				{
					new GameModeFileDestinationDefinition { SourcePath = "DWrite.dll", DestinationPath = @"bin\DWrite.dll" }
				}
			};
			Assert.That(DataDrivenGameMode.AdjustConfiguredFileDestination(policy, source), Is.EqualTo(expected));
		}

		/// <summary>Ordinary domains retain canonical local IPC names even before settings record a newly opened game.</summary>
		[TestCase("fallout4", "Fallout4")]
		[TestCase("starfield", "Starfield")]
		public void PlainDownloadDomain_UsesCanonicalModeIdWithoutEditionRouting(string domain, string modeId)
		{
			var original = GameModeRuntimePolicies.All.ToArray();
			try
			{
				GameModeRuntimePolicies.Replace(new[] { new KeyValuePair<string, GameModeRuntimePolicy>(modeId, new GameModeRuntimePolicy { SchemaVersion = 1 }) });
				ISettings settings = InterfaceStub<ISettings>.Create((method, args) => null);
				Assert.That(GameModeRuntimeConfiguration.RequestedMode(domain, settings), Is.EqualTo(modeId));
				Assert.That(GameModeRuntimeConfiguration.RequestedMode("unknown-catalog", settings), Is.EqualTo("unknown-catalog"));
			}
			finally { GameModeRuntimePolicies.Replace(original); }
		}

		/// <summary>Applies module preparation and download routing to an arbitrary configured local mode.</summary>
		[Test]
		public void ConfiguredMode_UsesDeclaredPreparationAndDownloadRouting()
		{
			var original = GameModeRuntimePolicies.All.ToArray();
			try
			{
				GameModeRuntimePolicies.Replace(new[]
				{
					new KeyValuePair<string, GameModeRuntimePolicy>("ConfiguredMode", new GameModeRuntimePolicy
					{
						SchemaVersion = 1, CollectionAdapter = "module-settings-v1", CollectionDomain = "configured-catalog",
						CollectionOrderProvidesMetadata = true,
						DownloadRouting = new GameModeDownloadRouting { DefaultMode = "ConfiguredMode", IncomingDomains = new[] { "configured-catalog" } }
					})
				});
				IGameMode mode = InterfaceStub<IGameMode>.Create((method, args) => method.Name == "get_ModeId" ? "ConfiguredMode" : null);
				ISettings settings = InterfaceStub<ISettings>.Create((method, args) => null);
				Assert.That(CollectionGamePreparation.ForMode(mode).ComparablePath(@"Data\Generated\texture.dds"), Is.EqualTo(@"Generated\texture.dds"));
				Assert.That(GameModeRuntimeConfiguration.RequestedMode("configured-catalog", settings), Is.EqualTo("ConfiguredMode"));
				Assert.That(GameModeRuntimeConfiguration.DownloadMode("ConfiguredMode", settings), Is.EqualTo("ConfiguredMode"));
				Assert.That(GameModeRuntimePolicies.For("UnconfiguredMode").CollectionOrderProvidesMetadata, Is.False);
			}
			finally { GameModeRuntimePolicies.Replace(original); }
		}

		/// <summary>Recognizes configured console commands without requiring a particular game ID.</summary>
		[Test]
		public void ConfiguredConsoleBatch_UsesDeclaredCommandsAndRuleIdentity()
		{
			var original = GameModeRuntimePolicies.All.ToArray();
			try
			{
				GameModeRuntimePolicies.Replace(new[]
				{
					new KeyValuePair<string, GameModeRuntimePolicy>("ConfiguredMode", new GameModeRuntimePolicy
					{
						SchemaVersion = 1, ConsoleBatchCommands = new[] { "ConfiguredCommand" },
						ConsoleBatchNoArgumentCommand = "ConfiguredCommand", ConsoleBatchRuleId = "configured-batch"
					})
				});
				IGameMode mode = InterfaceStub<IGameMode>.Create((method, args) => method.Name == "get_ModeId" ? (object)"ConfiguredMode" :
					method.Name == "get_StopFolders" ? new string[0] : null);
				GameRootPackageMatch match = GameRootPackageMatcher.Match(mode, new[] { "settings.txt" },
					path => new MemoryStream(Encoding.UTF8.GetBytes("ConfiguredCommand")));
				Assert.That(match, Is.Not.Null);
				Assert.That(match.Rule.Id, Is.EqualTo("configured-batch"));
			}
			finally { GameModeRuntimePolicies.Replace(original); }
		}
	}
}
