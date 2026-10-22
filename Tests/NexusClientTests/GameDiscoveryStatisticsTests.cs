using System;
using System.IO;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraGrid.Views.Grid.ViewInfo;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nexus.Client.Games;
using Nexus.Client.Games.UI;
using Nexus.Client.UI;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Verifies that optional catalog summaries cannot change authoritative game data or invent counts.</summary>
	[TestFixture]
	public class GameDiscoveryStatisticsTests
	{
		private string _root;

		/// <summary>Creates an isolated directory owned by this fixture.</summary>
		[SetUp]
		public void SetUp()
		{
			_root = Path.Combine(Path.GetTempPath(), "nmm-game-summary-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_root);
		}

		/// <summary>Removes only the directory created for the current test.</summary>
		[TearDown]
		public void TearDown() => Directory.Delete(_root, true);

		/// <summary>A missing cache remains unknown and does not create files at startup.</summary>
		[Test]
		public void MissingCacheDoesNotWriteOrInventCounts()
		{
			GameDiscoveryStatisticsCache cache = new GameDiscoveryStatisticsCache(_root);
			Assert.That(cache.Get(Entry()), Is.Null);
			cache.Save();
			Assert.That(Directory.GetFiles(_root), Is.Empty);
		}

		/// <summary>Retains zero, unknown optional counts, profile context and UTC time through an atomic round trip.</summary>
		[Test]
		public void SnapshotRoundTripPreservesUnknownAndKnownZero()
		{
			GameDiscoveryStatisticsCache cache = new GameDiscoveryStatisticsCache(_root);
			GameDiscoveryStatistics snapshot = Snapshot();
			snapshot.AppliedCollections = 0;
			snapshot.ManagedCollections = 0;
			cache.Set("FalloutNV", snapshot);
			cache.Save();
			GameDiscoveryStatistics loaded = new GameDiscoveryStatisticsCache(_root).Get(Entry());
			Assert.That(loaded.ActiveMods, Is.EqualTo(2));
			Assert.That(loaded.ManagedMods, Is.EqualTo(8));
			Assert.That(loaded.AppliedCollections, Is.EqualTo(0));
			Assert.That(loaded.ActivePlugins, Is.Null);
			Assert.That(loaded.ProfileName, Is.EqualTo("Test profile"));
			Assert.That(loaded.UpdatedUtc, Is.EqualTo(snapshot.UpdatedUtc));
			Assert.That(loaded.UpdatedUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
			Assert.That(Directory.GetFiles(_root, "*.tmp"), Is.Empty);
		}

		/// <summary>One malformed entry cannot suppress valid summaries for other games.</summary>
		[Test]
		public void InvalidEntriesAreIsolated()
		{
			JObject games = new JObject
			{
				["FalloutNV"] = JObject.FromObject(Snapshot()),
				["BadCount"] = new JObject { ["ActiveMods"] = "broken" },
				["Partial"] = new JObject { ["InstallationPath"] = "game" },
				["Negative"] = JObject.FromObject(Snapshot())
			};
			games["Negative"]["ManagedMods"] = -1;
			WriteDocument(1, games);
			GameDiscoveryStatisticsCache cache = new GameDiscoveryStatisticsCache(_root);
			Assert.That(cache.Get(Entry()), Is.Not.Null);
			Assert.That(cache.Get(Entry("BadCount")), Is.Null);
			Assert.That(cache.Get(Entry("Partial")), Is.Null);
			Assert.That(cache.Get(Entry("Negative")), Is.Null);
		}

		/// <summary>Display counts from a previous installation or storage location cannot follow a reconfiguration.</summary>
		[Test]
		public void ReconfigurationInvalidatesOldCounts()
		{
			GameDiscoveryStatisticsCache cache = new GameDiscoveryStatisticsCache(_root);
			cache.Set("FalloutNV", Snapshot());
			Assert.That(cache.Get(Entry(installation: "other game")), Is.Null);
			cache.ValidateStorage("FalloutNV", "other storage");
			Assert.That(cache.Get(Entry()), Is.Null);
		}

		/// <summary>A future cache format is ignored and preserved even when the current session produces counts.</summary>
		[Test]
		public void FutureVersionIsNotOverwritten()
		{
			WriteDocument(2, new JObject { ["future"] = new JObject { ["extra"] = true } });
			string before = File.ReadAllText(CachePath);
			GameDiscoveryStatisticsCache cache = new GameDiscoveryStatisticsCache(_root);
			cache.Set("FalloutNV", Snapshot());
			cache.Save();
			Assert.That(File.ReadAllText(CachePath), Is.EqualTo(before));
		}

		/// <summary>Malformed JSON remains a nonfatal missing display hint.</summary>
		[Test]
		public void CorruptCacheDoesNotPreventDiscovery()
		{
			File.WriteAllText(CachePath, "{broken");
			Assert.That(new GameDiscoveryStatisticsCache(_root).Get(Entry()), Is.Null);
		}

		/// <summary>Expands only managed rows and refreshes bound counts without collapsing their details.</summary>
		[Test]
		[Apartment(ApartmentState.STA)]
		public void ManagedDetailsExpandAndRefreshWhileSupportedRowsStayFlat()
		{
			GameDiscoveryStatistics snapshot = Snapshot();
			using (GameDiscoveryDocument document = new GameDiscoveryDocument())
			{
				document.ConfigureRows(id => SystemIcons.Application, entry => snapshot);
				document.SetCatalog(new GameDiscoveryCatalog(new[] { Entry(), new GameDiscoveryEntry("Supported", "Supported",
					GameDiscoveryCollection.Supported, null, null, false, false, false, false, new GameDiscoveryInstallationCandidate[0]) }, new string[0]));
				document.StartPosition = FormStartPosition.Manual;
				document.Location = new Point(-20000, -20000);
				document.Size = new Size(1200, 900);
				document.ShowInTaskbar = false;
				document.Show();
				Application.DoEvents();
				GridControl grid = (GridControl)typeof(GameDiscoveryDocument).GetField("_grid", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(document);
				GridView view = (GridView)grid.MainView;
				grid.ForceInitialize();
				view.SetMasterRowExpanded(0, true);
				Application.DoEvents();
				Assert.That(view.GetMasterRowExpanded(0), Is.True);
				GridView detail = (GridView)view.GetDetailView(0, 0);
				Assert.That(detail.RowCount, Is.EqualTo(4));
				snapshot.ActiveMods = 5;
				document.RefreshStatistics("FalloutNV");
				Assert.That(view.GetMasterRowExpanded(0), Is.True);
				Assert.That(detail.GetRowCellValue(0, "Active"), Is.EqualTo("5"));
				document.SelectMode("Supported");
				view.SetMasterRowExpanded(0, true);
				Assert.That(view.GetRelationCount(0), Is.EqualTo(0));
				Assert.That(view.GetMasterRowExpanded(0), Is.False);
				document.Close();
			}
		}

		/// <summary>Catalog-wide missing-mode diagnostics must not appear as problems with each selected game.</summary>
		[Test]
		[Apartment(ApartmentState.STA)]
		public void UnavailableSavedModes_AreNotAppendedToUnrelatedGameDetails()
		{
			using (GameDiscoveryDocument document = new GameDiscoveryDocument())
			{
				string[] unavailable = { "Witcher3Remastered" };
				document.SetCatalog(new GameDiscoveryCatalog(new[] { Entry(),
					new GameDiscoveryEntry("Configure", "Configure", GameDiscoveryCollection.ToConfigure,
						"game", "executable", false, false, false, false, new GameDiscoveryInstallationCandidate[0]),
					new GameDiscoveryEntry("Supported", "Supported", GameDiscoveryCollection.Supported,
						null, null, false, false, false, false, new GameDiscoveryInstallationCandidate[0]) }, unavailable));
				MemoEdit details = (MemoEdit)typeof(GameDiscoveryDocument).GetField("_details", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(document);
				foreach (string modeId in new[] { "FalloutNV", "Configure", "Supported" })
				{
					document.SelectMode(modeId);
					LabelControl title = (LabelControl)typeof(GameDiscoveryDocument).GetField("_gameTitle", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(document);
					Assert.That(title.Text, Is.EqualTo(modeId));
					Assert.That(details.Text, Does.Not.Contain("Witcher3Remastered"), modeId);
				}
				document.SetCatalog(new GameDiscoveryCatalog(new GameDiscoveryEntry[0], unavailable));
				Assert.That(details.Text, Does.Contain("Witcher3Remastered"), "Keep catalog diagnostics available when no game is selected.");
			}
		}

		/// <summary>Measures rendered logo rows after the same density updates used by the main window.</summary>
		[TestCase("Compact")]
		[TestCase("Comfortable")]
		[TestCase("Spacious")]
		[Apartment(ApartmentState.STA)]
		public void LogoRowsSurviveDisplaySettingsAndHistoryIsManagedOnly(string density)
		{
			using (GameDiscoveryDocument document = new GameDiscoveryDocument())
			using (DevExpressDisplaySettings settings = new DevExpressDisplaySettings("Segoe UI", 9f, density,
				DevExpressDisplaySettings.DefaultIconStyle, 20, DevExpressDisplaySettings.DefaultIconColorProfile,
				DevExpressDisplaySettings.DefaultButtonPresentationProfile))
			{
				document.ConfigureRows(id => SystemIcons.Application, entry => Snapshot());
				document.SetCatalog(new GameDiscoveryCatalog(new[] { Entry(),
					new GameDiscoveryEntry("Configure", "Configure", GameDiscoveryCollection.ToConfigure,
						"game", "executable", false, false, false, false, new GameDiscoveryInstallationCandidate[0]),
					new GameDiscoveryEntry("Supported", "Supported", GameDiscoveryCollection.Supported,
						null, null, false, false, false, false, new GameDiscoveryInstallationCandidate[0]) }, new string[0]));
				document.StartPosition = FormStartPosition.Manual;
				document.Location = new Point(-20000, -20000);
				document.Size = new Size(1200, 900);
				document.ShowInTaskbar = false;
				document.Show();
				GridControl grid = (GridControl)typeof(GameDiscoveryDocument).GetField("_grid", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(document);
				GridView view = (GridView)grid.MainView;
				foreach (string modeId in new[] { "FalloutNV", "Configure", "Supported", "FalloutNV" })
				{
					document.SelectMode(modeId);
					DevExpressDisplaySettingsApplier.ApplyToControlTree(document, settings);
					DevExpressDisplaySettingsApplier.ApplyToControlTree(document, settings);
					grid.ForceInitialize();
					Application.DoEvents();
					GridViewInfo info = (GridViewInfo)view.GetViewInfo();
					Assert.That(info.GetGridRowInfo(0).Bounds.Height, Is.GreaterThanOrEqualTo(64), modeId);
					Assert.That(view.Columns["LastManaged"].Visible, Is.EqualTo(modeId == "FalloutNV"), modeId);
				}
				view.SetMasterRowExpanded(0, true);
				Application.DoEvents();
				GridView detail = (GridView)view.GetDetailView(0, 0);
				Assert.That(((GridViewInfo)detail.GetViewInfo()).GetGridRowInfo(0).Bounds.Height, Is.LessThan(64),
					"Summary rows must retain normal density.");
				document.Close();
			}
		}

		private string CachePath => Path.Combine(_root, "game-discovery-statistics.json");

		/// <summary>Writes a versioned test document.</summary>
		private void WriteDocument(int version, JObject games) => File.WriteAllText(CachePath,
			new JObject { ["schemaVersion"] = version, ["games"] = games }.ToString(Formatting.None));

		/// <summary>Creates a valid display snapshot without touching a game or native stores.</summary>
		private static GameDiscoveryStatistics Snapshot() => new GameDiscoveryStatistics
		{
			InstallationPath = "game", InstallInfoPath = "storage", ProfileName = "Test profile",
			UpdatedUtc = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc), ActiveMods = 2, ManagedMods = 8
		};

		/// <summary>Creates the minimal managed catalog entry used for cache identity checks.</summary>
		private static GameDiscoveryEntry Entry(string modeId = "FalloutNV", string installation = "game") =>
			new GameDiscoveryEntry(modeId, modeId, GameDiscoveryCollection.Managed, installation, "executable", true, true,
				false, false, new GameDiscoveryInstallationCandidate[0]);
	}
}
