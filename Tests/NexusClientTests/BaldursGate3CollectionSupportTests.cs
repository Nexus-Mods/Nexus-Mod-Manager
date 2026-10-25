using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using Newtonsoft.Json.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.ManagedContent;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Exercises BG3 Collection deployment and reversible ordering without a game installation.</summary>
	[TestFixture]
	public class BaldursGate3CollectionSupportTests
	{
		/// <summary>Admits the extension types only for BG3 and retains the ordinary native install context.</summary>
		[TestCase("bg3-loose")]
		[TestCase("bg3-replacer")]
		[TestCase("bg3-bg3se")]
		[TestCase("dinput")]
		public void Normalize_AcceptsBg3SecondaryTypes(string type)
		{
			JObject root = Manifest(type);
			NexusCollectionManifestNormalizationResult result = Normalize(root);
			Assert.That(result.CapabilityReport.AllIssues.Any(issue => issue.Status == CollectionCompatibilityStatus.Unsupported), Is.False);
			Assert.That(result.Manifest.Members.Single().InstallRootBehavior, Is.EqualTo(CollectionMemberInstallRootBehavior.Default));
		}

		/// <summary>Keeps BG3-specific types rejected for unrelated games.</summary>
		[Test]
		public void Normalize_DoesNotAcceptBg3TypesForOtherGames()
		{
			JObject root = Manifest("bg3-loose");
			root["info"]["domainName"] = "fallout4";
			root["mods"][0]["domainName"] = "fallout4";
			Assert.That(Normalize(root).CapabilityReport.AllIssues.Any(issue => issue.Code == "member.mod-type-unsupported"), Is.True);
		}

		/// <summary>Curator order changes invalidate the owning member's retained recipe.</summary>
		[Test]
		public void Normalize_OrderIsPartOfOwnerRecipeIdentity()
		{
			JObject root = Manifest("");
			root["loadOrder"] = new JArray(Entry("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "One"), Entry("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "Two"));
			string before = Normalize(root).Manifest.Members.Single().RecipeIdentity.Fingerprint;
			root["loadOrder"] = new JArray(((JArray)root["loadOrder"]).Reverse().Select(item => item.DeepClone()));
			Assert.That(Normalize(root).Manifest.Members.Single().RecipeIdentity.Fingerprint, Is.Not.EqualTo(before));
		}

		/// <summary>Never routes loose data or native DLLs into the user's ordinary pak directory.</summary>
		[TestCase("bg3-loose", @"Public\Example\file.dds", @"Data\Public\Example\file.dds")]
		[TestCase("bg3-loose", @"wrapper\Data\Public\Example\file.dds", @"Data\Public\Example\file.dds")]
		[TestCase("bg3-replacer", @"Generated\Public\file.gr2", @"Data\Generated\Public\file.gr2")]
		[TestCase("bg3-bg3se", "DWrite.dll", @"bin\DWrite.dll")]
		[TestCase("dinput", @"wrapper\bin\NativeMods\camera.dll", @"bin\NativeMods\camera.dll")]
		public void MapDestination_UsesSecondaryGameFolders(string type, string source, string expected)
		{
			Assert.That(CollectionBaldursGate3Data.MapDestination(source, type, false), Is.EqualTo(expected));
		}

		/// <summary>Matches the automatic BG3SE installer's DLL selection rather than copying bundled executables.</summary>
		[Test]
		public void MapFiles_ExtenderInstallerFlattensDllsOnly()
		{
			var files = new[] { @"release\DWrite.dll", @"release\readme.txt" };
			var recipe = new ModInstallationSimpleFileRecipe(files.Select(path => new ModInstallationSimpleFileMapping(path, path)));
			ModInstallationSimpleFileRecipe mapped = CollectionBaldursGate3Data.MapFiles(recipe, "bg3-bg3se", files);
			Assert.That(mapped.Mappings.Single().SourcePath, Is.EqualTo(files[0]));
			Assert.That(mapped.Mappings.Single().DestinationPath, Is.EqualTo(@"bin\DWrite.dll"));
		}

		/// <summary>Preserves game-owned modules and unrelated settings, skips listed/disabled packages, and restores the original order.</summary>
		[TestCase(false, false)]
		[TestCase(true, false)]
		[TestCase(false, true)]
		[TestCase(true, true)]
		public void RecordStore_AppliesCuratorOrderAndRestoresOriginal(bool partialSelection, bool legacyOrder)
		{
			string directory = Path.Combine(Path.GetTempPath(), "nmm-bg3-collection-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Path.Combine(directory, "Public"));
			try
			{
				string path = Path.Combine(directory, "Public", "modsettings.lsx");
				File.WriteAllText(path, "<save><version major='4' minor='8'/><region id='ModuleSettings'><node id='root'><children><node id='Mods'><children><node id='ModuleShortDesc'><attribute id='Name' value='GustavDev'/><attribute id='UUID' value='cccccccc-cccc-cccc-cccc-cccccccccccc'/></node></children></node><node id='Unrelated'><attribute id='keep' value='yes'/></node></children></node></region></save>");
				var rule = new StructuredXmlRecordRule(1, StructuredXmlRecordRule.ProviderId, 1, "info.json", "Mods", "Ascii", "Fixture", "modsettings.lsx",
					new[] { "default" }, "bg3-modsettings-v1|", "Mods", "ModuleShortDesc", "UUID",
					new[] { new StructuredXmlRecordField("UUID", "guid", new[] { "UUID" }, null, null) });
				var store = new BaldursGate3CollectionRecordStore(rule, directory);
				JObject listed = Entry("dddddddd-dddd-dddd-dddd-dddddddddddd", "Listed"); listed["data"]["isListed"] = true;
				JObject disabled = Entry("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee", "Disabled"); disabled["enabled"] = false;
				var order = new JArray(Entry("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "Two"), Entry("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "One"), listed, disabled);
				if (partialSelection)
				{
					var existing = new XmlDocument(); existing.Load(path);
					XmlElement previous = existing.CreateElement("node"); previous.SetAttribute("id", "ModuleShortDesc");
					previous.InnerXml = "<attribute id='Name' value='Previous One'/><attribute id='UUID' value='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'/>";
					existing.SelectSingleNode("//node[@id='Mods']/children").AppendChild(previous); existing.Save(path);
				}
				if (legacyOrder)
				{
					var legacy = new XmlDocument(); legacy.Load(path);
					XmlElement node = legacy.CreateElement("node"); node.SetAttribute("id", "ModOrder"); node.AppendChild(legacy.CreateElement("children"));
					legacy.SelectSingleNode("//node[@id='Mods']").ParentNode.AppendChild(node); legacy.Save(path);
				}
				ISet<string> selected = partialSelection ? new HashSet<string>(new[] { "tWo.PAK" }, StringComparer.OrdinalIgnoreCase) : null;
				BasicInstallGameSpecificValue planned = store.BuildPlan(order, selected).Single();
				string profile, identity; store.ParseKey(planned.Key, out profile, out identity);
				byte[] original; Assert.That(store.TryReadValue(path, identity, out original), Is.True);
				store.WriteValue(path, identity, planned.Value);
				var document = new XmlDocument(); document.Load(path);
				string[] names = document.SelectNodes("//node[@id='ModuleShortDesc']/attribute[@id='Name']").Cast<XmlElement>().Select(node => node.GetAttribute("value")).ToArray();
				CollectionAssert.AreEqual(partialSelection ? new[] { "GustavDev", "Two" } : new[] { "GustavDev", "Two", "One" }, names);
				if (legacyOrder)
					Assert.That(document.SelectNodes("//node[@id='ModOrder']/children/node").Count, Is.EqualTo(partialSelection ? 1 : 2));
				else Assert.That(document.SelectSingleNode("//node[@id='ModOrder']"), Is.Null);
				Assert.That(document.SelectSingleNode("//attribute[@id='keep']").Attributes["value"].Value, Is.EqualTo("yes"));
				byte[] installed; store.TryReadValue(path, identity, out installed); CollectionAssert.AreEqual(planned.Value, installed);
				store.WriteValue(path, identity, original);
				byte[] restored; store.TryReadValue(path, identity, out restored); CollectionAssert.AreEqual(original, restored);
			}
			finally { Directory.Delete(directory, true); }
		}

		/// <summary>Creates synthetic package metadata in the extension's actual export shape.</summary>
		private static JObject Entry(string uuid, string name) => new JObject
		{
			["id"] = name + ".pak", ["modId"] = "fixture", ["enabled"] = true,
			["data"] = new JObject { ["uuid"] = uuid, ["name"] = name, ["folder"] = name, ["version"] = "1", ["isListed"] = false }
		};

		/// <summary>Creates a minimal retained Collection manifest for normalization.</summary>
		private static JObject Manifest(string type) => new JObject
		{
			["info"] = new JObject { ["name"] = "Fixture", ["domainName"] = "baldursgate3", ["author"] = "Fixture", ["authorUrl"] = "https://example.invalid", ["description"] = "Synthetic fixture" },
			["mods"] = new JArray(new JObject { ["name"] = "Example", ["version"] = "1", ["optional"] = false, ["domainName"] = "baldursgate3",
				["details"] = new JObject { ["type"] = type }, ["source"] = new JObject { ["type"] = "nexus", ["modId"] = 1, ["fileId"] = 10, ["updatePolicy"] = "exact" } }),
			["modRules"] = new JArray(), ["loadOrder"] = new JArray()
		};

		/// <summary>Runs the production normalizer over synthetic retained bytes.</summary>
		private static NexusCollectionManifestNormalizationResult Normalize(JObject root)
		{
			var revision = new CollectionRevision(CollectionRevisionIdentity.FromNexus(CollectionIdentity.FromNexus("123"), "456", 1), null, null, 1);
			return new NexusCollectionManifestNormalizer().Normalize(Encoding.UTF8.GetBytes(root.ToString()), revision);
		}
	}
}
