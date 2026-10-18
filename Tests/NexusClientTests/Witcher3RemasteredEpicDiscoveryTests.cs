using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nexus.Client.Games;
using Nexus.Client.Games.DataDriven;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>W3R.9c: Epic manifest discovery is identity-based and still subject to the edition gate.</summary>
	[TestFixture]
	public class Witcher3RemasteredEpicDiscoveryTests
	{
		private const string EpicId = "725a22e15ed74735bb0d6a19f3cc82d0";
		private const string Dx12Executable = @"bin\x64_dx12\witcher3.exe";

		[Test]
		public void ShippedDefinition_RegistersTheVerifiedEpicIdWithNoNameFallback()
		{
			GameModeDefinition definition = ReadShippedDefinition();
			Assert.That(definition.ModeId, Is.EqualTo("Witcher3Remastered"));
			Assert.That(definition.BehaviorProfile, Is.EqualTo("witcher3remastered"));
			Assert.That(new GameModeDefinitionValidator().Validate(definition)
				.Where(issue => issue.Severity == GameModeDefinitionIssueSeverity.Error), Is.Empty);

			GameStoreInstallInfo[] stores = DataDrivenGameModeFactory.BuildStoreDiscovery(definition.Discovery).ToArray();
			Assert.That(stores.Count(store => store.Store == GameStore.Epic), Is.EqualTo(1));
			Assert.That(stores.Where(store => store.Store == GameStore.Steam).Select(store => store.Id),
				Is.EquivalentTo(new[] { "292030" }));
			Assert.That(stores.Where(store => store.Store == GameStore.Gog).Select(store => store.Id),
				Is.EquivalentTo(new[] { "1495134320", "1207664643" }));

			GameStoreInstallInfo epic = stores.Single(store => store.Store == GameStore.Epic);
			Assert.That(epic.Id, Is.EqualTo(EpicId));
			Assert.That(epic.ExecutableName, Is.EqualTo(Dx12Executable));
			Assert.That(epic.InstallFolderName, Is.Null,
				"Do not allow the detector's DisplayName fallback to accept a different Epic game.");
			Assert.That(epic.PathSuffix, Is.Null);
			Assert.That(epic.RegistryKey, Is.Null);
		}

		[TestCase("AppName")]
		[TestCase("CatalogItemId")]
		[TestCase("MainGameCatalogItemId")]
		public void EpicManifest_RecognizesAnExactIdentifier(string key)
		{
			var manifest = new JObject
			{
				[key] = EpicId,
				["DisplayName"] = "The Witcher 3: Wild Hunt",
				["InstallLocation"] = @"D:\EpicLibrary\Witcher3"
			};
			Assert.That(MatchesManifest(manifest, EpicStore()), Is.True);
		}

		[Test]
		public void EpicManifest_RejectsAnotherGameEvenWithTheWitcherDisplayName()
		{
			var manifest = new JObject
			{
				["AppName"] = "unrelated-epic-game",
				["CatalogItemId"] = "different-item-id",
				["DisplayName"] = "The Witcher 3: Wild Hunt",
				["InstallLocation"] = @"D:\EpicLibrary\Witcher3"
			};
			Assert.That(MatchesManifest(manifest, EpicStore()), Is.False);
		}

		[Test]
		public void EpicManifest_RejectsAnUnidentifiedExecutableOnlyManifest()
		{
			var manifest = new JObject
			{
				["DisplayName"] = "The Witcher 3: Wild Hunt",
				["LaunchExecutable"] = Dx12Executable,
				["InstallLocation"] = @"D:\EpicLibrary\Witcher3"
			};
			Assert.That(MatchesManifest(manifest, EpicStore()), Is.False);
		}

		[Test]
		public void SteamAndGogLegacyCandidates_DoNotHideAConfirmedEpicRemasteredInstallation()
		{
			var stores = DataDrivenGameModeFactory.BuildStoreDiscovery(ReadShippedDefinition().Discovery);
			var inspected = new List<GameStore>();
			string selected = DataDrivenGameModeFactory.SelectEditionValidStorePath(stores,
				store =>
				{
					inspected.Add(store.Store);
					return store.Store == GameStore.Epic ? "epic-confirmed-5x" : store.Id + "-legacy-4x";
				},
				path => path == "epic-confirmed-5x");

			Assert.That(selected, Is.EqualTo("epic-confirmed-5x"));
			Assert.That(inspected, Is.EqualTo(new[] { GameStore.Steam, GameStore.Gog,
				GameStore.Gog, GameStore.Epic }));
		}

		[Test]
		public void EpicWithOnlyDx12OrLegacyVersionEvidence_IsNotAutomaticallyAccepted()
		{
			var epicOnly = new[] { EpicStore() };
			string selected = DataDrivenGameModeFactory.SelectEditionValidStorePath(epicOnly,
				store => "epic-dx12-but-4x", path => false);
			Assert.That(selected, Is.Null);
		}

		[Test]
		public void EpicDiscovery_DoesNotOpenTheNativeActivationGate()
		{
			PropertyInfo gate = typeof(DataDrivenGameModeFactory).GetProperty("RemasteredNativeActivationApproved",
				BindingFlags.Static | BindingFlags.NonPublic);
			Assert.That(gate, Is.Not.Null);
			Assert.That(gate.GetValue(null, null), Is.False);
		}

		private static GameStoreInstallInfo EpicStore()
		{
			return new GameStoreInstallInfo
			{
				Store = GameStore.Epic,
				Id = EpicId,
				ExecutableName = Dx12Executable
			};
		}

		private static bool MatchesManifest(JObject manifest, GameStoreInstallInfo store)
		{
			MethodInfo matcher = typeof(GameStoreInstallationPathDetector).GetMethod("MatchesEpicManifest",
				BindingFlags.Static | BindingFlags.NonPublic);
			Assert.That(matcher, Is.Not.Null, "Existing Epic identity matcher was not found.");
			return (bool)matcher.Invoke(null, new object[] { manifest.ToString(Formatting.None), store });
		}

		private static GameModeDefinition ReadShippedDefinition()
		{
			string path = Path.Combine(TestContext.CurrentContext.TestDirectory,
				"Fixtures", "Witcher3Remastered.game.json");
			Assert.That(File.Exists(path), Is.True, "The test project must copy the real Remastered game definition.");
			return JsonConvert.DeserializeObject<GameModeDefinition>(File.ReadAllText(path));
		}
	}
}
