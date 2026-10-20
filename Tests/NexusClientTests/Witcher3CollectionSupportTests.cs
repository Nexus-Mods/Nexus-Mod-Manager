using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.ModManagement.Witcher3;
using Nexus.Client.Mods;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Exercises Witcher extension data and reversible records without a running game or native installation.</summary>
	[TestFixture]
	public class Witcher3CollectionSupportTests
	{
		private string _root;

		/// <summary>Allocates an isolated file tree for settings and menu documents.</summary>
		[SetUp]
		public void SetUp() { _root = Path.Combine(Path.GetTempPath(), "nmm-witcher-collection-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_root); }

		/// <summary>Removes only this fixture's isolated file tree.</summary>
		[TearDown]
		public void TearDown() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

		/// <summary>Accepts the three reported Vortex types and translates zero-based folder priority.</summary>
		[Test]
		public void Normalize_AcceptsWitcherTypesOrderAndBundledScripts()
		{
			JObject root = Manifest();
			root["mergedData"] = new JObject { ["scriptMergedData"] = Zip(new Dictionary<string, string>
			{
				{ ".w3edition", "remastered" }, { "MergeInventory.xml", "<Inventory />" },
				{ "mod0000_MergedFiles/content/scripts/test.ws", "// fixture script" }
			}) };
			CollectionWitcher3Data data = CollectionWitcher3Data.Parse(root);
			Assert.That(data.Order["modExample"].Priority, Is.EqualTo(1));
			Assert.That(data.Order["modExample"].Enabled, Is.False);
			CollectionAssert.AreEqual(new[] { @"Mods\mod0000_MergedFiles\content\scripts\test.ws" }, data.GeneratedFiles.Keys);
			Assert.That(data.Metadata.Count, Is.EqualTo(2));
			NexusCollectionManifestNormalizationResult result = Normalize(root);
			Assert.That(result.CapabilityReport.Status, Is.EqualTo(CollectionCompatibilityStatus.Supported));
			Assert.That(result.Manifest.Members.All(item => item.InstallRootBehavior == CollectionMemberInstallRootBehavior.Default), Is.True);
		}

		/// <summary>Rejects traversal, drive paths and Windows device names inside curator merge archives.</summary>
		[TestCase("../escape.ws")]
		[TestCase("C:/escape.ws")]
		[TestCase("modMerged/content/../escape.ws")]
		[TestCase("modMerged/content/CON")]
		public void Parse_RejectsUnsafeEmbeddedPaths(string path)
		{
			JObject root = Manifest();
			root["mergedData"] = new JObject { ["scriptMergedData"] = Zip(new Dictionary<string, string> { { path, "x" } }) };
			Assert.Throws<InvalidDataException>(() => CollectionWitcher3Data.Parse(root));
			Assert.That(Normalize(root).CapabilityReport.Status, Is.EqualTo(CollectionCompatibilityStatus.Unsupported));
		}

		/// <summary>Does not silently accept unknown merge formats or duplicate folder identities.</summary>
		[Test]
		public void Parse_RejectsUnknownMergeDataAndDuplicateOrder()
		{
			JObject root = Manifest();
			root["mergedData"] = new JObject { ["futureMergeFormat"] = "00" };
			Assert.Throws<InvalidDataException>(() => CollectionWitcher3Data.Parse(root));
			root.Remove("mergedData");
			((JArray)root["loadOrder"]).Add(root["loadOrder"][0].DeepClone());
			Assert.Throws<InvalidDataException>(() => CollectionWitcher3Data.Parse(root));
		}

		/// <summary>Trims archive wrappers while retaining each explicit Mods, DLC or bin destination.</summary>
		[TestCase(@"wrapper\Mods\modExample\content\file.ws", @"Mods\modExample\content\file.ws")]
		[TestCase(@"dlcExample\content\file.bundle", @"DLC\dlcExample\content\file.bundle")]
		[TestCase(@"wrapper\bin\config\r4game\user_config_matrix\pc\input.xml", @"bin\config\r4game\user_config_matrix\pc\input.xml")]
		public void MapPath_PreservesExplicitGameRoots(string source, string expected)
		{
			Assert.That(CollectionWitcher3InstallPlanner.MapPath(source, "witcher3tl", null, null, null), Is.EqualTo(expected));
		}

		/// <summary>Restores real component names stripped by virtual archive restructuring without changing extraction sources.</summary>
		[TestCase(@"dlcNamed\content\en.w3strings", @"DLC\dlcNamed\content\en.w3strings")]
		[TestCase(@"modZ_Grammar\content\en.w3strings", @"Mods\modZ_Grammar\content\en.w3strings")]
		public void Planner_UsesOriginalComponentPathAndLogicalReadPath(string original, string destination)
		{
			const string logical = @"content\en.w3strings";
			InstallModFileOperation file = PlanFiles(new Dictionary<string, string> { { logical, original } }).OfType<InstallModFileOperation>().Single();
			Assert.That(file.SourcePath, Is.EqualTo(logical));
			Assert.That(file.DestinationPath, Is.EqualTo(destination));
		}

		/// <summary>Preserves presets, repeated separators and menu order while repairing a mismatched UTF-16 declaration.</summary>
		[Test]
		public void Planner_CustomMenuRetainsCompleteStructureAndCorrectsEncoding()
		{
			const string path = @"bin\config\r4game\user_config_matrix\pc\custom.xml";
			const string xml = "<?xml version='1.0' encoding='UTF-16'?><UserConfig><Group id='Custom'><PresetsArray><Preset id='0'><Entry varId='A' value='1'/></Preset></PresetsArray><VisibleVars><Var id='Separator' displayType='SUBTLE_SEPARATOR'/><Var id='A'/><Var id='Separator' displayType='SUBTLE_SEPARATOR'/><Var id='B'/></VisibleVars></Group></UserConfig>";
			string payload = Path.Combine(_root, "menu.xml"); File.WriteAllBytes(payload, Encoding.UTF8.GetBytes(xml));
			IReadOnlyList<ScriptedInstallOperation> operations = PlanFiles(new Dictionary<string, string> { { path, path } }, payload);
			GenerateDataFileOperation file = operations.OfType<GenerateDataFileOperation>().Single();
			Assert.That(operations.OfType<EditGameSpecificValueOperation>(), Is.Empty);
			Assert.That(file.PreparationSourcePath, Is.EqualTo(path));
			Assert.That(file.Data.Take(2), Is.EqualTo(new byte[] { 0xff, 0xfe }));
			using (var input = new MemoryStream(file.Data))
			{
				var document = System.Xml.Linq.XDocument.Load(input);
				Assert.That(document.Descendants("Preset").Count(), Is.EqualTo(1));
				CollectionAssert.AreEqual(new[] { "Separator", "A", "Separator", "B" }, document.Descendants("Var").Select(item => (string)item.Attribute("id")));
			}
			Assert.That(Witcher3CollectionRecordStore.RequiresMenuMerge(path), Is.False);
			Assert.That(Witcher3CollectionRecordStore.RequiresMenuMerge(@"bin\config\r4game\user_config_matrix\pc\input.xml"), Is.True);
		}

		/// <summary>Skips unscoped packaging metadata, keeps runtime JSON, and copies root helpers as ordinary owned files.</summary>
		[Test]
		public void Planner_MetadataDoesNotBlockAndHelpersArePassiveFileOperations()
		{
			var paths = new[] { "RELEASE-ACCEPTANCE.json", "MANIFEST_SHA256.json", "Uninstall-Example.bat", @"Mods\modExample\content\info.json" };
			IReadOnlyList<ScriptedInstallOperation> operations = PlanFiles(paths.ToDictionary(path => path, path => path));
			CollectionAssert.AreEquivalent(new[] { "Uninstall-Example.bat", @"Mods\modExample\content\info.json" },
				operations.OfType<InstallModFileOperation>().Select(item => item.DestinationPath));
			Assert.That(CollectionWitcher3Data.ValidateDestination("Uninstall-Example.bat"), Is.EqualTo("Uninstall-Example.bat"));
			Assert.Throws<InvalidDataException>(() => CollectionWitcher3Data.ValidateDestination("witcher3.exe"));
			Assert.Throws<InvalidDataException>(() => CollectionWitcher3Data.ValidateDestination(@"bin\x64\witcher3.exe"));
		}

		/// <summary>Preserves unrelated bytes, BOM and original key spacing when updating an existing order value.</summary>
		[Test]
		public void RecordStore_OrderUpdateAndRestorePreserveOriginalBytes()
		{
			string path = Path.Combine(_root, "mods.settings");
			File.WriteAllText(path, "; keep\r\n[modExample]\r\n Priority = 7\r\nEnabled=1\r\nVK=external\r\n", new UTF8Encoding(true));
			byte[] original = File.ReadAllBytes(path);
			var store = new Witcher3CollectionRecordStore(_root, path);
			string profile, identity;
			store.ParseKey(Witcher3CollectionRecordStore.Key("mods.settings", "ini", "modExample", "Priority"), out profile, out identity);
			byte[] previous;
			Assert.That(store.TryReadValue(path, identity, out previous), Is.True);
			store.WriteValue(path, identity, Encoding.UTF8.GetBytes("2"));
			Assert.That(File.ReadAllText(path), Does.Contain(" Priority = 2"));
			Assert.That(File.ReadAllText(path), Does.Contain("VK=external"));
			store.WriteValue(path, identity, previous);
			CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
		}

		/// <summary>Uses one owner key for case-insensitive INI identities and retains empty scalar values.</summary>
		[Test]
		public void RecordStore_CanonicalizesIniIdentityAndSupportsEmptyValues()
		{
			Assert.That(Witcher3CollectionRecordStore.Key("USER.SETTINGS", "ini", "Section", "Key"),
				Is.EqualTo(Witcher3CollectionRecordStore.Key("user.settings", "ini", "section", "key")));
			List<KeyValuePair<string, byte[]>> records = Witcher3CollectionRecordStore.PlanSettings("user.settings",
				Encoding.UTF8.GetBytes("[Section]\r\nEnabled=true\r\nPriority=custom\r\nEmpty=\r\n")).ToList();
			Assert.That(records.Count, Is.EqualTo(3));
			Assert.That(records.Single(item => item.Key == Witcher3CollectionRecordStore.Key("user.settings", "ini", "Section", "Empty")).Value, Is.Empty);
			Assert.That(new CollectionReviewedGameSpecificValueSnapshot(records[2].Key, new byte[0]).Value, Is.Empty);
		}

		/// <summary>Merges and restores one XML variable without replacing another mod's variable.</summary>
		[Test]
		public void RecordStore_MenuVariableUpdatePreservesOtherVariables()
		{
			const string profile = @"bin\config\r4game\user_config_matrix\pc\input.xml";
			var store = new Witcher3CollectionRecordStore(_root, Path.Combine(_root, "mods.settings"));
			string path = store.GetManifestPath(profile);
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, "<UserConfig><Group id='PCInput'><VisibleVars><Var id='keep' value='original'/><Var id='change' value='old'/></VisibleVars></Group></UserConfig>");
			KeyValuePair<string, byte[]> edit = Witcher3CollectionRecordStore.PlanMenu(profile,
				Encoding.UTF8.GetBytes("<UserConfig><Group id='PCInput'><VisibleVars><Var id='change' value='new'/></VisibleVars></Group></UserConfig>")).Single();
			string file, identity; store.ParseKey(edit.Key, out file, out identity);
			byte[] previous; Assert.That(store.TryReadValue(path, identity, out previous), Is.True);
			store.WriteValue(path, identity, edit.Value);
			Assert.That(File.ReadAllText(path), Does.Contain("value=\"new\""));
			Assert.That(File.ReadAllText(path), Does.Contain("id=\"keep\" value=\"original\""));
			store.WriteValue(path, identity, previous);
			byte[] restored; Assert.That(store.TryReadValue(path, identity, out restored), Is.True);
			CollectionAssert.AreEqual(previous, restored);
		}

		/// <summary>Admits configuration-only native recipes while requiring an exact consumed path set.</summary>
		[Test]
		public void PreparedAdapter_AcceptsValuesWithoutFilesAndRejectsUnconsumedPaths()
		{
			var context = new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data);
			var fingerprint = new ModOperationFingerprint("target:witcher-fixture", context, "recipe:witcher-fixture");
			var adapter = new ModInstallationSimpleFileRecipeAdapter();
			var edit = new EditGameSpecificValueOperation("fixture-key", new byte[0], true);
			Func<IEnumerable<ModInstallationRecipePath>, ModInstallationRecipeInput> input = paths => new ModInstallationRecipeInput(
				ModOperationIdentity.CreateNew(ModOperationOrigin.Collection, fingerprint),
				new ModInstallationRecipeValidation(ModInstallationSimpleFileRecipeAdapter.AdapterId, ModInstallationSimpleFileRecipeAdapter.AdapterVersion,
					context, new ModInstallationRecipeExpectedContent(new string('a', 64), 1),
					new[] { new ModInstallationRecipeCapability(ModInstallationSimpleFileRecipeAdapter.CapabilityId, ModInstallationSimpleFileRecipeAdapter.CapabilityVersion) }, paths));
			Assert.That(adapter.TranslatePreparedPlan(input(new ModInstallationRecipePath[0]), new ScriptedInstallOperation[] { edit }).NativeOperations.Count, Is.EqualTo(1));
			Assert.Throws<InvalidDataException>(() => adapter.TranslatePreparedPlan(input(new[] { new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, @"Mods\modUnused\file.bin") }), new ScriptedInstallOperation[] { edit }));
		}

		/// <summary>Creates synthetic extension data with every reported Witcher package type.</summary>
		private static JObject Manifest()
		{
			var mods = new JArray(); int index = 0;
			foreach (string type in new[] { "witcher3tl", "witcher3dlc", "witcher3menumodroot" })
				mods.Add(new JObject { ["name"] = "Example " + index, ["version"] = "1.0", ["optional"] = false, ["domainName"] = "witcher3",
					["details"] = new JObject { ["type"] = type }, ["source"] = new JObject { ["type"] = "nexus", ["modId"] = ++index, ["fileId"] = index + 10, ["updatePolicy"] = "exact" } });
			return new JObject { ["info"] = new JObject { ["name"] = "Fixture", ["domainName"] = "witcher3", ["author"] = "Fixture", ["authorUrl"] = "https://example.invalid", ["description"] = "Synthetic fixture" }, ["mods"] = mods, ["modRules"] = new JArray(),
				["loadOrder"] = new JArray(new JObject { ["id"] = "modExample", ["enabled"] = false, ["locked"] = true, ["data"] = new JObject { ["prefix"] = 0 } }) };
		}

		/// <summary>Normalizes synthetic data using ordinary retained-manifest provenance.</summary>
		private static NexusCollectionManifestNormalizationResult Normalize(JObject root)
		{
			var revision = new CollectionRevision(CollectionRevisionIdentity.FromNexus(CollectionIdentity.FromNexus("123"), "456", 1), null, null, 3);
			return new NexusCollectionManifestNormalizer().Normalize(Encoding.UTF8.GetBytes(root.ToString()), revision);
		}

		/// <summary>Models archive restructuring independently of payload extraction for read-only plan fixtures.</summary>
		public interface IMappedArchive : IMod, IModArchiveSourcePathProvider { }

		/// <summary>Plans a synthetic member using logical sources and their original archive layout.</summary>
		private static IReadOnlyList<ScriptedInstallOperation> PlanFiles(IDictionary<string, string> paths, string payload = null)
		{
			JObject root = Manifest(); NexusCollectionManifestNormalizationResult normalized = Normalize(root);
			var members = normalized.Manifest.Members.Select(item => new ResolvedCollectionMemberPlan(item, CollectionResolvedArtifactChoice.Exact(item.Artifact))).ToList();
			var plan = new ResolvedCollectionPlan(CollectionPlanIdentity.From(Guid.NewGuid(), 1), CollectionTargetIdentity.FromFingerprint("target:witcher-fixture"),
				CollectionExecutionPolicy.InstallIntoCurrentSetup(), new CollectionCurrentStateFingerprint("fixture/1", "fixture-state"), normalized.CapabilityReport, members);
			IMappedArchive mod = InterfaceStub<IMappedArchive>.Create((method, args) =>
			{
				if (method.Name == "GetFileList") return paths.Keys.ToList();
				if (method.Name == "GetArchiveSourcePath") return paths[(string)args[0]];
				if (method.Name == "GetFileStream") return File.OpenRead(payload);
				if (method.Name == "get_HasInstallScript") return false;
				return null;
			});
			return CollectionWitcher3InstallPlanner.Build(plan, members[0], mod, new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), root, CollectionWitcher3Data.Parse(root), CancellationToken.None);
		}

		/// <summary>Builds a tiny in-memory hexadecimal ZIP fixture; no curator content is copied into source.</summary>
		private static string Zip(IDictionary<string, string> files)
		{
			using (var stream = new MemoryStream())
			{
				using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
					foreach (KeyValuePair<string, string> file in files)
						using (var writer = new StreamWriter(zip.CreateEntry(file.Key).Open(), new UTF8Encoding(false))) writer.Write(file.Value);
				return BitConverter.ToString(stream.ToArray()).Replace("-", String.Empty);
			}
		}
	}
}
