using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.Games.DataDriven;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using Nexus.Client.OnlineServices.Wabbajack;
using Nexus.Client.Wabbajack;
using NUnit.Framework;
using Octodiff.Core;
using Octodiff.Diagnostics;

namespace NexusClientTests
{
	/// <summary>Regression coverage for the real CDN protocol and compiled-profile-to-Collection conversion.</summary>
	[TestFixture]
	public sealed class WabbajackConversionTests
	{
		private string _directory;

		/// <summary>Creates one isolated preparation workspace.</summary>
		[SetUp]
		public void SetUp() { _directory = Path.Combine(Path.GetTempPath(), "nmm-wj-convert-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_directory); }

		/// <summary>Removes only this fixture's workspace.</summary>
		[TearDown]
		public void TearDown() { Directory.Delete(_directory, true); }

		/// <summary>Wabbajack serializes the numeric digest little-endian, unlike Vortex's canonical byte order.</summary>
		[Test]
		public void HashUsesWabbajackLittleEndianEncoding()
		{
			Assert.That(Hash(new byte[0]), Is.EqualTo(Convert.ToBase64String(BitConverter.GetBytes(0xEF46DB3751D8E999UL))));
		}

		/// <summary>Authored list URLs are directories containing a gzip definition and indexed parts, not direct ZIP files.</summary>
		[Test]
		public async Task DownloadAssemblesCdnDefinitionAndPartsInOffsetOrder()
		{
			byte[] first = Encoding.UTF8.GetBytes("first part "); byte[] second = Encoding.UTF8.GetBytes("second part");
			byte[] complete = first.Concat(second).ToArray();
			const string root = "https://authored-files.wabbajack.org/list.wabbajack_example";
			JObject definition = new JObject { ["Size"] = complete.Length, ["Parts"] = new JArray(
				new JObject { ["Index"] = 1, ["Offset"] = first.Length, ["Size"] = second.Length, ["Hash"] = Hash(second) },
				new JObject { ["Index"] = 0, ["Offset"] = 0, ["Size"] = first.Length, ["Hash"] = Hash(first) }) };
			byte[] compressed;
			using (MemoryStream stream = new MemoryStream())
			{
				using (GZipStream gzip = new GZipStream(stream, CompressionMode.Compress, true))
				using (StreamWriter writer = new StreamWriter(gzip)) writer.Write(definition.ToString());
				compressed = stream.ToArray();
			}
			using (WabbajackClient client = new WabbajackClient(new BinaryHandler(new Dictionary<string, byte[]> {
				{ root + "/definition.json.gz", compressed }, { root + "/parts/0", first }, { root + "/parts/1", second } })))
			{
				string path = Path.Combine(_directory, "list.wabbajack");
				await client.DownloadFileAsync(root, path, Hash(complete), null, CancellationToken.None);
				CollectionAssert.AreEqual(complete, File.ReadAllBytes(path));
			}
		}

		/// <summary>Rebuilds a selected profile, applies an Octodiff patch, preserves order and retains vanilla inputs for repeat imports.</summary>
		[Test]
		public void ConversionProducesRepeatableNativeCollectionAndSetupBundles()
		{
			byte[] original = Encoding.UTF8.GetBytes("original game input");
			byte[] patched = Encoding.UTF8.GetBytes("patched game output with extra data");
			string game = Path.Combine(_directory, "game"); Directory.CreateDirectory(Path.Combine(game, "Data"));
			string gameFile = Path.Combine(game, "Data", "base.bin"); File.WriteAllBytes(gameFile, original);
			string downloads = Path.Combine(_directory, "downloads"); Directory.CreateDirectory(downloads);
			string output = Path.Combine(_directory, "prepared");
			WabbajackPackage package = CreatePackage(original, patched);
			GameModeWabbajackDefinition configuration = new GameModeWabbajackDefinition { Enabled = true, Game = "examplegame", GameRootFolders = new[] { "Root files" } };
			WabbajackProfile profile = WabbajackProfile.ReadAll(package, configuration).Single();
			CollectionAssert.AreEqual(new[] { "Base", "Override" }, profile.Mods);
			Assert.That(package.SourceId, Is.EqualTo("author/list")); Assert.That(package.DefaultProfile, Is.EqualTo("Main"));
			WabbajackCollectionConverter converter = new WabbajackCollectionConverter(package, profile, configuration, game, "newvegas");
			Assert.That(converter.FindMissingSources(new[] { downloads }, null, CancellationToken.None), Is.Empty);
			WabbajackConvertedCollection result = converter.Convert(output, null, null, CancellationToken.None);
			byte[] firstBundle = File.ReadAllBytes(result.BundlePath);
			using (ZipArchive bundle = ZipFile.OpenRead(result.BundlePath))
			{
				JObject manifest = JObject.Parse(Read(bundle.GetEntry("collection.json")));
				CollectionAssert.AreEqual(new[] { "[Wabbajack root files]", "Base", "Override" }, manifest["mods"].Select(member => (string)member["name"]));
				Assert.That((string)manifest["mods"][0]["details"]["type"], Is.EqualTo("enb"));
				Assert.That(manifest["modRules"].Count(), Is.EqualTo(2));
				string expression = (string)manifest["mods"][2]["source"]["fileExpression"];
				Assert.That(Read(bundle.GetEntry("bundled/" + expression + "/data.bin")), Is.EqualTo(Encoding.UTF8.GetString(patched)));
				Assert.That(bundle.Entries.Any(entry => entry.FullName.Contains("mohidden") || entry.FullName.EndsWith("meta.ini")), Is.False);
				NexusCollectionManifestNormalizationResult normalized = new NexusCollectionBundleImporter().ImportManifest(Encoding.UTF8.GetBytes(manifest.ToString()), result.Revision);
				Assert.That(normalized.CapabilityReport.Status, Is.EqualTo(CollectionCompatibilityStatus.Supported));
				Assert.That(result.Definition.Origin, Is.EqualTo(CollectionOrigin.Local));
			}
			using (ZipArchive setup = ZipFile.OpenRead(result.SetupPath)) Assert.That(Read(setup.GetEntry("Documents/game.ini")), Is.EqualTo("setting=1"));
			File.WriteAllText(gameFile, "installed replacement");
			WabbajackCollectionConverter again = new WabbajackCollectionConverter(package, profile, configuration, game, "newvegas");
			Assert.That(again.FindMissingSources(new[] { downloads, Path.Combine(output, "Wabbajack Sources") }, null, CancellationToken.None), Is.Empty);
			WabbajackConvertedCollection second = again.Convert(output, null, null, CancellationToken.None);
			Assert.That(second.Revision.Identity, Is.EqualTo(result.Revision.Identity));
			CollectionAssert.AreEqual(firstBundle, File.ReadAllBytes(second.BundlePath));
		}

		/// <summary>Creates a compact compiled package with real Octodiff bytes and a disabled unsupported member.</summary>
		private WabbajackPackage CreatePackage(byte[] original, byte[] patched)
		{
			Dictionary<string, byte[]> embedded = new Dictionary<string, byte[]>(); JArray directives = new JArray();
			Action<string, string, string> inline = (path, id, text) => {
				byte[] bytes = Encoding.UTF8.GetBytes(text); embedded.Add(id, bytes);
				directives.Add(new JObject { ["$type"] = "InlineFile", ["To"] = path, ["SourceDataID"] = id, ["Size"] = bytes.Length, ["Hash"] = Hash(bytes) }); };
			inline("profiles/Main/modlist.txt", "mods", "+Override\n+Base\n-Ignored\n");
			inline("profiles/Main/plugins.txt", "plugins", "a.esm\nb.esp\n");
			inline("profiles/Main/loadorder.txt", "order", "a.esm\nb.esp\n");
			inline("profiles/Main/game.ini", "ini", "setting=1");
			inline("Example.compiler_settings", "compiler", "{\"MachineUrl\":\"author/list\",\"Profile\":\"Main\"}");
			inline("Root files/loader.dll", "loader", "root-loader");
			inline("mods/Base/meta.ini", "metadata", "ignored metadata");
			inline("mods/Base/hidden.esp.mohidden", "hidden", "disabled file");
			directives.Add(new JObject { ["$type"] = "CreateBSA", ["To"] = "mods/Ignored/unused.bsa" });
			directives.Add(new JObject { ["$type"] = "FromArchive", ["To"] = "mods/Base/data.bin", ["ArchiveHashPath"] = new JArray(Hash(original)), ["Hash"] = Hash(original), ["Size"] = original.Length });
			directives.Add(new JObject { ["$type"] = "PatchedFromArchive", ["To"] = "mods/Override/data.bin", ["ArchiveHashPath"] = new JArray(Hash(original)),
				["FromHash"] = Hash(original), ["Hash"] = Hash(patched), ["Size"] = patched.Length, ["PatchID"] = "patch" });
			using (MemoryStream signature = new MemoryStream())
			using (MemoryStream input = new MemoryStream(original))
			using (MemoryStream target = new MemoryStream(patched))
			using (MemoryStream delta = new MemoryStream())
			{
				new SignatureBuilder().Build(input, new SignatureWriter(signature)); signature.Position = 0;
				new DeltaBuilder().BuildDelta(target, new SignatureReader(signature, new NullProgressReporter()), new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(delta)));
				embedded.Add("patch", delta.ToArray());
			}
			JObject manifest = new JObject { ["Name"] = "Example", ["Author"] = "Author", ["GameType"] = "ExampleGame", ["Version"] = "1.0",
				["Directives"] = directives, ["Archives"] = new JArray(new JObject { ["Name"] = "base.bin", ["Size"] = original.Length, ["Hash"] = Hash(original),
					["State"] = new JObject { ["$type"] = "GameFileSourceDownloader, Wabbajack.Lib", ["Game"] = "ExampleGame", ["GameFile"] = "Data/base.bin" } }) };
			string file = Path.Combine(_directory, "example.wabbajack");
			using (ZipArchive zip = ZipFile.Open(file, ZipArchiveMode.Create))
			{
				embedded.Add("modlist", Encoding.UTF8.GetBytes(manifest.ToString()));
				foreach (KeyValuePair<string, byte[]> entry in embedded) using (Stream stream = zip.CreateEntry(entry.Key).Open()) stream.Write(entry.Value, 0, entry.Value.Length);
			}
			return WabbajackPackage.Read(file);
		}

		/// <summary>Calculates the fixture's upstream digest.</summary>
		private static string Hash(byte[] bytes) { using (MemoryStream stream = new MemoryStream(bytes)) return WabbajackHash.Compute(stream, CancellationToken.None); }

		/// <summary>Reads a generated bundle's text entry.</summary>
		private static string Read(ZipArchiveEntry entry) { using (StreamReader reader = new StreamReader(entry.Open())) return reader.ReadToEnd(); }

		/// <summary>Serves binary CDN parts without network access.</summary>
		private sealed class BinaryHandler : HttpMessageHandler
		{
			private readonly IDictionary<string, byte[]> _responses;
			/// <summary>Supplies the upstream URL-to-bytes mapping.</summary>
			internal BinaryHandler(IDictionary<string, byte[]> responses) { _responses = responses; }
			/// <summary>Returns only the fixture's defined CDN resources.</summary>
			protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				byte[] bytes;
				return Task.FromResult(_responses.TryGetValue(request.RequestUri.AbsoluteUri, out bytes)
					? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) } : new HttpResponseMessage(HttpStatusCode.NotFound));
			}
		}
	}
}
