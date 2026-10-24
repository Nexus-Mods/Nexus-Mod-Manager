using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Nexus.Client.Games.DataDriven;
using Nexus.Client.OnlineServices.Wabbajack;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Exercises public catalogue semantics and compiled package reading without installing mods.</summary>
	[TestFixture]
	public sealed class WabbajackGalleryTests
	{
		private const string BaseUrl = "https://raw.githubusercontent.com/wabbajack-tools/mod-lists/master/";
		private string _directory;

		/// <summary>Creates an isolated directory for downloaded and compiled list fixtures.</summary>
		[SetUp]
		public void SetUp() { _directory = Path.Combine(Path.GetTempPath(), "nmm-wabbajack-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_directory); }

		/// <summary>Removes this fixture's temporary files.</summary>
		[TearDown]
		public void TearDown() { Directory.Delete(_directory, true); }

		/// <summary>Broken repositories do not hide valid lists; featured state and availability come from Wabbajack's reports.</summary>
		[Test]
		public async Task CatalogueUsesNamespacedFeaturedStatusAndIsolatesInvalidRepository()
		{
			Dictionary<string, string> feeds = new Dictionary<string, string>
			{
				[BaseUrl + "repositories.json"] = "{\"author\":\"https://example.org/good.json\",\"broken\":\"https://example.org/broken.json\"}",
				[BaseUrl + "featured_lists.json"] = "[\"author/VNV\"]",
				[BaseUrl + "allowed_tags.json"] = "[\"Performance\"]",
				[BaseUrl + "tag_mappings.json"] = "{\"Perf\":\"Performance\"}",
				[BaseUrl + "reports/modListSummary.json"] = "[{\"machineURL\":\"author/VNV\",\"failed\":1}]",
				[BaseUrl + "reports/searchIndex.json"] = "{\"AllMods\":[\"NVTF\"],\"ModsPerList\":{\"VNV\":[\"NVTF\"]}}",
				["https://example.org/good.json"] = "[{\"title\":\"Viva New Vegas\",\"game\":\"falloutnewvegas\",\"tags\":[\"Perf\"],\"links\":{\"machineURL\":\"VNV\"}}]",
				["https://example.org/broken.json"] = "not json"
			};
			using (WabbajackClient client = new WabbajackClient(new FeedHandler(feeds)))
			{
				WabbajackCatalogue catalogue = await client.LoadAsync(CancellationToken.None);
				WabbajackList list = catalogue.Lists.Single();
				Assert.That(list.Id, Is.EqualTo("author/VNV"));
				Assert.That(list.Official, Is.True);
				Assert.That(list.IsUnavailable, Is.True);
				Assert.That(list.Tags, Does.Contain("Performance"));
				Assert.That(catalogue.FailedRepositories, Is.EqualTo(1));
				Assert.That(catalogue.SearchIndex.AllMods, Does.Contain("NVTF"));
			}
		}

		/// <summary>Game, size and selected-mod filters match the upstream all-selected-mods and exclude-any behavior.</summary>
		[Test]
		public void GalleryFilterKeepsOtherGamesOutAndUsesSelectedMods()
		{
			WabbajackList list = new WabbajackList { Title = "Viva New Vegas", Game = "falloutnewvegas", Official = true,
				Repository = "author", Links = new WabbajackLinks { MachineUrl = "VNV" }, DownloadMetadata = new WabbajackDownloadMetadata { TotalSize = 100 } };
			WabbajackSearchIndex index = new WabbajackSearchIndex();
			index.ModsPerList.Add(list.Id, new HashSet<string> { "NVTF", "YUP" });
			WabbajackGalleryFilter filter = new WabbajackGalleryFilter { Game = "falloutnewvegas", Search = "viva", Mods = new[] { "NVTF", "YUP" } };
			Assert.That(filter.Matches(list, index), Is.True);
			filter.Mods = new[] { "NVTF", "missing" };
			Assert.That(filter.Matches(list, index), Is.False);
			filter.ExcludeMods = true;
			Assert.That(filter.Matches(list, index), Is.False);
			filter.Mods = new string[0];
			filter.MaximumSize = 99;
			Assert.That(filter.Matches(list, index), Is.False);
			filter.MaximumSize = long.MaxValue;
			list.Game = "skyrimspecialedition";
			Assert.That(filter.Matches(list, index), Is.False);
		}

		/// <summary>Both manifest entry names preserve source types, exact IDs and unfamiliar directive fields for later conversion.</summary>
		[TestCase("modlist")]
		[TestCase("modlist.json")]
		public void CompiledPackagePreservesOriginalInstructions(string entryName)
		{
			string path = Path.Combine(_directory, "list.wabbajack");
			const string manifest = "{\"Name\":\"Example\",\"GameType\":\"FalloutNewVegas\",\"Archives\":[{\"Name\":\"source.7z\",\"State\":{\"$type\":\"NexusDownloader, Wabbajack.Lib\",\"ModID\":123,\"FileID\":456}}],\"Directives\":[{\"$type\":\"FromArchive\",\"To\":\"mods\\\\Example\\\\plugin.esp\",\"ArchiveHashPath\":[\"AAAAAAAAAAA=\",\"plugin.esp\"]},{\"$type\":\"CreateBSA\",\"To\":\"mods\\\\Example\\\\Example.bsa\",\"TempID\":\"retained\"}]}";
			using (FileStream file = File.Create(path))
			using (ZipArchive zip = new ZipArchive(file, ZipArchiveMode.Create))
			using (StreamWriter writer = new StreamWriter(zip.CreateEntry(entryName).Open())) writer.Write(manifest);
			WabbajackPackage package = WabbajackPackage.Read(path);
			Assert.That(package.Archives.Single().State.Type, Is.EqualTo("NexusDownloader, Wabbajack.Lib"));
			Assert.That(package.Archives.Single().State.FileId, Is.EqualTo(456));
			Assert.That(package.Directives[0].ArchiveHashPath.Length, Is.EqualTo(2));
			Assert.That((string)package.Directives[1].AdditionalData["TempID"], Is.EqualTo("retained"));
			Assert.That(package.RawManifest, Is.EqualTo(manifest));
			Assert.That(Directory.GetFiles(_directory).Length, Is.EqualTo(1));
		}

		/// <summary>A failed download preserves the user's existing list package.</summary>
		[Test]
		public void FailedDownloadDoesNotReplaceExistingPackage()
		{
			string path = Path.Combine(_directory, "existing.wabbajack");
			File.WriteAllText(path, "existing package");
			using (WabbajackClient client = new WabbajackClient(new FeedHandler(new Dictionary<string, string>())))
			{
				WabbajackList list = new WabbajackList { Links = new WabbajackLinks { Download = "https://example.org/missing" } };
				Assert.ThrowsAsync<HttpRequestException>(async () => await client.DownloadAsync(list, path, null, CancellationToken.None));
				Assert.That(File.ReadAllText(path), Is.EqualTo("existing package"));
				Assert.That(Directory.GetFiles(_directory).Length, Is.EqualTo(1));
			}
		}

		/// <summary>Missing configuration keeps the gallery opt-in; an enabled entry requires its upstream game key.</summary>
		[Test]
		public void GameConfigurationEnablesGalleryWithoutHardcodedGameNames()
		{
			GameModeDefinition definition = JsonConvert.DeserializeObject<GameModeDefinition>("{\"wabbajack\":{\"enabled\":true,\"game\":\"anothergame\"}}");
			Assert.That(definition.Wabbajack.Enabled, Is.True);
			Assert.That(definition.Wabbajack.Game, Is.EqualTo("anothergame"));
			Assert.That(JsonConvert.DeserializeObject<GameModeDefinition>("{}").Wabbajack, Is.Null);
			definition.Wabbajack.Game = null;
			Assert.That(new GameModeDefinitionValidator().Validate(definition).Any(issue => issue.PropertyPath == "wabbajack.game"), Is.True);
		}

		/// <summary>Serves deterministic public feeds without accessing the network.</summary>
		private sealed class FeedHandler : HttpMessageHandler
		{
			private readonly IDictionary<string, string> _feeds;

			/// <summary>Supplies the expected public URL-to-content mapping.</summary>
			public FeedHandler(IDictionary<string, string> feeds) { _feeds = feeds; }

			/// <summary>Returns a feed or an ordinary missing-resource response.</summary>
			protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				string content;
				return Task.FromResult(_feeds.TryGetValue(request.RequestUri.AbsoluteUri, out content)
					? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) }
					: new HttpResponseMessage(HttpStatusCode.NotFound));
			}
		}
	}
}
