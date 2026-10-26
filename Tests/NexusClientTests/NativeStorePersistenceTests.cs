using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ChinhDo.Transactions;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Transactions;
using Nexus.Client.Util.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Regression coverage for one-time XML imports, incremental native writes and explicit restore boundaries.</summary>
	[TestFixture]
	public class NativeStorePersistenceTests
	{
		/// <summary>Retains missing originals, existing empty values and ordinary bytes across commit, restart, export and restore.</summary>
		[Test]
		public void InstallLog_AbsentAndEmptyGameValuesRemainDistinctAcrossPersistenceBoundaries()
		{
			using (var fixture = new StoreFixture())
			{
				InstallLogSqliteStore.CreateFromDocument(fixture.Database, CreateLog(0));
				var store = new InstallLogSqliteStore(fixture.Database);
				byte[] installed = new byte[] { 1, 2, 255 };
				var entry = new XElement("edit", new XAttribute("key", "WitcherSetting"), new XElement("installingMods",
					InstallLog.SerializeGameSpecificValueOwner("ORIGINAL_VALUE", null),
					InstallLog.SerializeGameSpecificValueOwner("first", new byte[0]),
					InstallLog.SerializeGameSpecificValueOwner("second", installed)));
				store.Apply(new XElement[0], new string[0], new[] { new InstallLogSqliteStore.Entry(2, entry) }, 1);
				Assert.AreEqual(1, Convert.ToInt32(fixture.Scalar("SELECT COUNT(*) FROM Owners o JOIN Items i ON i.ItemId=o.ItemId WHERE i.Path='WitcherSetting' AND o.BinaryValue IS NULL")));
				Assert.AreEqual(1, Convert.ToInt32(fixture.Scalar("SELECT COUNT(*) FROM Owners o JOIN Items i ON i.ItemId=o.ItemId WHERE i.Path='WitcherSetting' AND typeof(o.BinaryValue)='blob' AND length(o.BinaryValue)=0")));
				XElement[] owners = new InstallLogSqliteStore(fixture.Database).Read().Root.Element("gameSpecificEdits").Elements("edit")
					.Single(item => (string)item.Attribute("key") == "WitcherSetting").Descendants("mod").ToArray();
				CollectionAssert.AreEqual(new[] { "ORIGINAL_VALUE", "first", "second" }, owners.Select(item => (string)item.Attribute("key")));
				Assert.IsNull(InstallLog.DeserializeGameSpecificValueOwner(owners[0]));
				Assert.That(InstallLog.DeserializeGameSpecificValueOwner(owners[1]), Is.Empty);
				Assert.IsNull(owners[1].Attribute("absent"));
				CollectionAssert.AreEqual(installed, InstallLog.DeserializeGameSpecificValueOwner(owners[2]));

				string export = Path.Combine(fixture.Root, "export.xml");
				InstallLog.ExportSnapshot(fixture.Xml, export);
				string restoredPath = Path.Combine(fixture.Root, "restored.sqlite");
				InstallLogSqliteStore.CreateFromDocument(restoredPath, XDocument.Load(export));
				Assert.IsTrue(XNode.DeepEquals(store.Read(), new InstallLogSqliteStore(restoredPath).Read()));

				// Removing one winner retains the exact fallback, including the absent original sentinel.
				entry.Element("installingMods").Elements("mod").Last().Remove();
				store.Apply(new XElement[0], new string[0], new[] { new InstallLogSqliteStore.Entry(2, entry) }, 2);
				owners = store.Read().Root.Element("gameSpecificEdits").Elements("edit").Single(item => (string)item.Attribute("key") == "WitcherSetting").Descendants("mod").ToArray();
				Assert.IsNull(InstallLog.DeserializeGameSpecificValueOwner(owners[0]));
				Assert.That(InstallLog.DeserializeGameSpecificValueOwner(owners[1]), Is.Empty);
			}
		}

		/// <summary>Legacy empty Base64 still means an existing empty value, and invalid absence markers cannot discard bytes.</summary>
		[Test]
		public void InstallLog_GameValueCodecPreservesLegacyPayloadsAndRejectsConflictingAbsence()
		{
			var empty = new XElement("mod", new XAttribute("key", "first"));
			Assert.That(InstallLog.DeserializeGameSpecificValueOwner(empty), Is.Empty);
			var ordinary = InstallLog.SerializeGameSpecificValueOwner("first", new byte[] { 0, 255 });
			Assert.IsNull(ordinary.Attribute("absent"));
			Assert.AreEqual(Convert.ToBase64String(new byte[] { 0, 255 }), ordinary.Value);
			var conflict = new XElement(ordinary); conflict.SetAttributeValue("absent", true);
			Assert.Throws<InvalidDataException>(() => InstallLog.DeserializeGameSpecificValueOwner(conflict));
			empty.SetAttributeValue("absent", "invalid");
			Assert.Throws<InvalidDataException>(() => InstallLog.DeserializeGameSpecificValueOwner(empty));
		}

		/// <summary>Preserves legacy bytes, ownership order, INI text, binary edits and sparse root metadata through restart.</summary>
		[Test]
		public void InstallLog_MigrationPreservesLegacyValuesAndIsIdempotent()
		{
			using (var fixture = new StoreFixture())
			{
				XDocument legacy = CreateLog(2);
				legacy.Save(fixture.Xml);
				byte[] original = File.ReadAllBytes(fixture.Xml);
				InstallLogSqliteStore.EnsureMigrated(fixture.Xml);
				var store = new InstallLogSqliteStore(fixture.Database);
				XDocument imported = store.Read();
				CollectionAssert.AreEqual(new[] { "first", "second" }, imported.Root.Element("dataFiles").Elements("file").First()
					.Element("installingMods").Elements("mod").Select(x => (string)x.Attribute("key")));
				Assert.AreEqual("<value>&\nnext", imported.Root.Element("iniEdits").Element("ini").Descendants("mod").Single().Value);
				CollectionAssert.AreEqual(new byte[] { 0, 255, 1, 127 }, Convert.FromBase64String(imported.Root.Element("gameSpecificEdits").Descendants("mod").Single().Value));
				Assert.AreEqual("GameRoot", (string)imported.Root.Element("deploymentFiles").Element("file").Attribute("root"));
				XElement replacement = FileEntry("file0", "first");
				store.Apply(new XElement[0], new string[0], new[] { new InstallLogSqliteStore.Entry(0, replacement) }, 9);
				InstallLogSqliteStore.EnsureMigrated(fixture.Xml);
				Assert.AreEqual("9", (string)InstallLogSqliteStore.ReadDocument(fixture.Xml).Root.Attribute("deploymentCommitSequence"));
				CollectionAssert.AreEqual(original, File.ReadAllBytes(fixture.Xml));
				CollectionAssert.AreEqual(original, File.ReadAllBytes(fixture.Xml + ".pre-sqlite"));
			}
		}

		/// <summary>Exercises existing version conversion before import without modifying the user's backup XML.</summary>
		[Test]
		public void InstallLog_RestoreOlderXmlUsesExistingUpgraderAndRollbackRestoresDatabase()
		{
			using (var fixture = new StoreFixture())
			{
				CreateLog(1).Save(fixture.Xml);
				InstallLogSqliteStore.EnsureMigrated(fixture.Xml);
				XDocument before = InstallLogSqliteStore.ReadDocument(fixture.Xml);
				string backup = Path.Combine(fixture.Root, "older.xml");
				XDocument legacy = CreateLog(3);
				legacy.Root.SetAttributeValue("fileVersion", "0.5.0.0");
				legacy.Save(backup);
				byte[] original = File.ReadAllBytes(backup);
				using (var scope = new TransactionScope())
				{
					InstallLog.RestoreSnapshot(backup, fixture.Xml, fixture.Root, new ModRegistry(null, null), new TxFileManager());
					Assert.AreEqual(3, InstallLogSqliteStore.ReadDocument(fixture.Xml).Root.Element("dataFiles").Elements().Count());
				}
				Assert.IsTrue(XNode.DeepEquals(before, InstallLogSqliteStore.ReadDocument(fixture.Xml)));
				using (var scope = new TransactionScope())
				{
					InstallLog.RestoreSnapshot(backup, fixture.Xml, fixture.Root, new ModRegistry(null, null), new TxFileManager());
					scope.Complete();
				}
				Assert.AreEqual(3, InstallLogSqliteStore.ReadDocument(fixture.Xml).Root.Element("dataFiles").Elements().Count());
				CollectionAssert.AreEqual(original, File.ReadAllBytes(backup));
			}
		}

		/// <summary>A missing authoritative database must not reactivate retained XML containing stale ownership.</summary>
		[Test]
		public void InstallLog_MissingDatabaseAfterMigrationRejectsStaleXml()
		{
			using (var fixture = new StoreFixture())
			{
				CreateLog(1).Save(fixture.Xml);
				InstallLogSqliteStore.EnsureMigrated(fixture.Xml);
				File.Delete(fixture.Database);
				Assert.IsTrue(InstallLog.HasPersistedLog(fixture.Xml));
				Assert.Throws<InvalidDataException>(() => InstallLogSqliteStore.EnsureMigrated(fixture.Xml));
				Assert.IsFalse(File.Exists(fixture.Database));
			}
		}

		/// <summary>A failed import never publishes a partial database over the only copy of legacy state.</summary>
		[Test]
		public void InstallLog_InvalidBinaryValueLeavesLegacyXmlAndNoPublishedDatabase()
		{
			using (var fixture = new StoreFixture())
			{
				XDocument legacy = CreateLog(1);
				legacy.Root.Element("gameSpecificEdits").Descendants("mod").Single().Value = "invalid base64";
				legacy.Save(fixture.Xml);
				byte[] original = File.ReadAllBytes(fixture.Xml);
				Assert.Throws<FormatException>(() => InstallLogSqliteStore.EnsureMigrated(fixture.Xml));
				CollectionAssert.AreEqual(original, File.ReadAllBytes(fixture.Xml));
				Assert.IsFalse(File.Exists(fixture.Database));
				Assert.IsFalse(File.Exists(fixture.Database + ".authority"));
			}
		}

		/// <summary>Unrelated rows stay untouched as the installed inventory grows; removal uses the indexed owner references.</summary>
		[TestCase(20)]
		[TestCase(2000)]
		public void InstallLog_DeltaTouchesOnlyChangedOwnershipStack(int fileCount)
		{
			using (var fixture = new StoreFixture())
			{
				InstallLogSqliteStore.CreateFromDocument(fixture.Database, CreateLog(fileCount));
				var store = new InstallLogSqliteStore(fixture.Database);
				store.Read();
				long unaffectedId = Convert.ToInt64(fixture.Scalar("SELECT ItemId FROM Items WHERE Path='file1'"));
				fixture.Execute("CREATE TRIGGER RejectUnrelatedDelete BEFORE DELETE ON Owners WHEN OLD.ItemId IN (SELECT ItemId FROM Items WHERE Path <> 'file0') BEGIN SELECT RAISE(ABORT,'unrelated owner changed'); END;");
				fixture.Execute("CREATE TRIGGER RejectUnrelatedUpdate BEFORE UPDATE ON Mods BEGIN SELECT RAISE(ABORT,'unrelated registration changed'); END;");
				store.Apply(new XElement[0], new string[0], new[] { new InstallLogSqliteStore.Entry(0, FileEntry("file0", "first")) }, 4);
				Assert.AreEqual(unaffectedId, Convert.ToInt64(fixture.Scalar("SELECT ItemId FROM Items WHERE Path='file1'")));
				Assert.AreEqual(fileCount, Convert.ToInt32(fixture.Scalar("SELECT COUNT(*) FROM Items WHERE Kind=0")));
				Assert.IsFalse(store.FindOwnedEntries(new[] { "SECOND" }).Any(x => x.Kind == 0 && (string)x.Value.Attribute("path") == "file0"));
			}
		}

		/// <summary>Recovery sequence and owner edits roll back together when SQLite rejects a commit.</summary>
		[Test]
		public void InstallLog_FailedDeltaRollsBackOwnersAndSequenceAndCanRetry()
		{
			using (var fixture = new StoreFixture())
			{
				InstallLogSqliteStore.CreateFromDocument(fixture.Database, CreateLog(2));
				var store = new InstallLogSqliteStore(fixture.Database);
				XDocument before = store.Read();
				fixture.Execute("CREATE TRIGGER FailSequence BEFORE INSERT ON Metadata WHEN NEW.Key='deployment_commit_sequence' BEGIN SELECT RAISE(ABORT,'injected failure'); END;");
				var entries = new[] { new InstallLogSqliteStore.Entry(0, FileEntry("file0", "first")) };
				Assert.Throws<SQLiteException>(() => store.Apply(new XElement[0], new string[0], entries, 5));
				Assert.IsTrue(XNode.DeepEquals(before, new InstallLogSqliteStore(fixture.Database).Read()));
				fixture.Execute("DROP TRIGGER FailSequence");
				store.Apply(new XElement[0], new string[0], entries, 5);
				Assert.AreEqual("5", (string)store.Read().Root.Attribute("deploymentCommitSequence"));
			}
		}

		/// <summary>Exports are current and read-only legacy input is never rewritten by ordinary SQLite commits.</summary>
		[Test]
		public void InstallLog_ExplicitXmlExportAndDatabaseBackupContainCurrentState()
		{
			using (var fixture = new StoreFixture())
			{
				CreateLog(1).Save(fixture.Xml);
				InstallLogSqliteStore.EnsureMigrated(fixture.Xml);
				var store = new InstallLogSqliteStore(fixture.Database);
				store.Read();
				using (new FileStream(fixture.Xml, FileMode.Open, FileAccess.Read, FileShare.Read))
					store.Apply(new XElement[0], new string[0], new[] { new InstallLogSqliteStore.Entry(0, FileEntry("file0", "second")) }, 6);
				string export = Path.Combine(fixture.Root, "export.xml");
				InstallLog.ExportSnapshot(fixture.Xml, export);
				Assert.AreEqual("6", (string)XDocument.Load(export).Root.Attribute("deploymentCommitSequence"));
				store.BackupTo(fixture.Database + ".bak");
				File.Delete(fixture.Database);
				Assert.IsTrue(InstallLog.Restore(fixture.Xml));
				Assert.AreEqual("6", (string)InstallLogSqliteStore.ReadDocument(fixture.Xml).Root.Attribute("deploymentCommitSequence"));
			}
		}

		/// <summary>In-place edits retain stable SQL identities even after an explicit profile export.</summary>
		[Test]
		public void VirtualStore_DeltaAndExportKeepLiveIdentitiesAndDoNotWriteXml()
		{
			using (var fixture = new StoreFixture())
			{
				SQLiteVirtualModStore store = CreateVirtualStore(fixture);
				VirtualModStoreData data = store.Load(fixture.Xml, VirtualModActivator.CurrentVersion, x => true, (x, y) => "");
				long originalId = Convert.ToInt64(fixture.Scalar("SELECT VirtualLinkId FROM VirtualLinks"));
				store.Copy(fixture.Xml, Path.Combine(fixture.Root, "profile.xml"));
				data.VirtualLinks[0].Priority = 7;
				var delta = new VirtualModStoreDelta();
				delta.Links.Add(data.VirtualLinks[0]);
				store.SaveDelta(VirtualModActivator.CurrentVersion, delta);
				Assert.AreEqual(originalId, Convert.ToInt64(fixture.Scalar("SELECT VirtualLinkId FROM VirtualLinks")));
				Assert.AreEqual(7, Convert.ToInt32(fixture.Scalar("SELECT Priority FROM VirtualLinks")));
				Assert.IsFalse(File.Exists(fixture.Xml));
			}
		}

		/// <summary>Streaming profile exports retain empty registrations, owner/link order, XML escaping and install roots.</summary>
		[Test]
		public void VirtualStore_StreamingExportMatchesLegacyXmlAndPreservesLiveTopology()
		{
			using (var fixture = new StoreFixture())
			{
				SQLiteVirtualModStore store = CreateVirtualStore(fixture);
				var empty = new VirtualModInfo("1", "10", "Empty & <owner>", "Empty.zip", "1.0");
				var data = new VirtualModInfo("2", "20", "Data", "Data.zip", "2.0");
				var root = new VirtualModInfo("3", "30", "Root", "Root.zip", "3.0");
				store.SaveStoreData(VirtualModActivator.CurrentVersion, new VirtualModStoreData(
					new List<IVirtualModInfo> { empty, data, root }, new List<IVirtualModLink> {
						new VirtualModLink("Root\\A", "A.dll", 8, true, root, ModInstallRoot.GameRoot),
						new VirtualModLink("Data\\B", "B & C.txt", 2, false, data, ModInstallRoot.Data),
						new VirtualModLink("Data\\A", "A.txt", 1, true, data, ModInstallRoot.Data) }));
				VirtualModStoreData loaded = store.Load(fixture.Xml, VirtualModActivator.CurrentVersion, x => true, (x, y) => "");
				string expected = Path.Combine(fixture.Root, "expected.xml");
				string actual = Path.Combine(fixture.Root, "actual.xml");
				new XmlVirtualModStore().Save(VirtualModActivator.CurrentVersion, expected, loaded.VirtualMods, loaded.VirtualLinks);

				store.Copy(fixture.Xml, actual);

				Assert.That(XNode.DeepEquals(XDocument.Load(expected), XDocument.Load(actual)), Is.True);
				Assert.That(store.HasLoadedTopology(loaded.VirtualMods, loaded.VirtualLinks), Is.True);
				Assert.That(File.Exists(fixture.Xml), Is.False);
			}
		}

		/// <summary>A streamed XML failure leaves the previous profile and live SQL identities untouched.</summary>
		[Test]
		public void VirtualStore_FailedStreamingExportPreservesPreviousProfile()
		{
			using (var fixture = new StoreFixture())
			{
				SQLiteVirtualModStore store = CreateVirtualStore(fixture);
				var invalid = new VirtualModInfo("1", "10", "Invalid\u0001Name", "Invalid.zip", "1.0");
				store.SaveStoreData(VirtualModActivator.CurrentVersion, new VirtualModStoreData(
					new List<IVirtualModInfo> { invalid }, new List<IVirtualModLink>()));
				VirtualModStoreData loaded = store.Load(fixture.Xml, VirtualModActivator.CurrentVersion, x => true, (x, y) => "");
				string profile = Path.Combine(fixture.Root, "profile.xml");
				File.WriteAllText(profile, "previous profile");

				Assert.Catch(() => store.Copy(fixture.Xml, profile));

				Assert.That(File.ReadAllText(profile), Is.EqualTo("previous profile"));
				Assert.That(store.HasLoadedTopology(loaded.VirtualMods, loaded.VirtualLinks), Is.True);
				Assert.That(Directory.GetFiles(fixture.Root, "*.tmp"), Is.Empty);
			}
		}

		/// <summary>A failed Virtual write does not publish new IDs or consume the retry delta.</summary>
		[Test]
		public void VirtualStore_FailedDeltaRetainsGenerationForRetry()
		{
			using (var fixture = new StoreFixture())
			{
				SQLiteVirtualModStore store = CreateVirtualStore(fixture);
				VirtualModStoreData data = store.Load(fixture.Xml, VirtualModActivator.CurrentVersion, x => true, (x, y) => "");
				var delta = new VirtualModStoreDelta();
				data.VirtualLinks[0].Priority = 12;
				delta.Links.Add(data.VirtualLinks[0]);
				fixture.Execute("CREATE TRIGGER RejectLink BEFORE UPDATE ON VirtualLinks BEGIN SELECT RAISE(ABORT,'injected failure'); END;");
				Assert.Throws<SQLiteException>(() => store.SaveDelta(VirtualModActivator.CurrentVersion, delta));
				Assert.AreEqual(1, Convert.ToInt32(fixture.Scalar("SELECT Priority FROM VirtualLinks")));
				fixture.Execute("DROP TRIGGER RejectLink");
				store.SaveDelta(VirtualModActivator.CurrentVersion, delta);
				Assert.AreEqual(12, Convert.ToInt32(fixture.Scalar("SELECT Priority FROM VirtualLinks")));
			}
		}

		/// <summary>Legacy Virtual XML remains importable, including old root defaults and registrations without links.</summary>
		[Test]
		public void VirtualStore_LegacyXmlMigratesOnceAndMissingDatabaseCannotReimportIt()
		{
			using (var fixture = new StoreFixture())
			{
				File.WriteAllText(fixture.Xml, "<virtualModActivator fileVersion='0.2.0.0'><modList><modInfo modId='1' modName='Example' modFileName='Example.zip' modFilePath='mods'><fileLink realPath='source' virtualPath='target'><linkPriority>3</linkPriority><isActive>true</isActive></fileLink></modInfo><modInfo modId='2' modName='Empty' modFileName='Empty.zip' modFilePath='mods' /></modList></virtualModActivator>");
				byte[] original = File.ReadAllBytes(fixture.Xml);
				var factory = new VirtualModStoreFactory();
				IVirtualModStore store = factory.Create(fixture.Xml, VirtualModActivator.CurrentVersion, x => true, (x, y) => "1.0");
				Assert.IsInstanceOf<SQLiteVirtualModStore>(store);
				VirtualModStoreData data = store.Load(fixture.Xml, VirtualModActivator.CurrentVersion, x => true, (x, y) => "1.0");
				Assert.AreEqual(2, data.VirtualMods.Count);
				Assert.AreEqual(1, data.VirtualLinks.Count);
				Assert.AreEqual(ModInstallRoot.Data, data.VirtualLinks[0].InstallRoot);
				Assert.AreEqual(3, data.VirtualLinks[0].Priority);
				CollectionAssert.AreEqual(original, File.ReadAllBytes(fixture.Xml));
				File.Delete(fixture.Database);
				Assert.Throws<InvalidDataException>(() => factory.Create(fixture.Xml, VirtualModActivator.CurrentVersion, x => true, (x, y) => "1.0"));
			}
		}

		/// <summary>Malformed legacy Virtual state cannot be promoted as a partial successful import.</summary>
		[TestCase("invalid", "true")]
		[TestCase("1", "invalid")]
		public void VirtualStore_InvalidLegacyLinkDoesNotPublishDatabase(string priority, string active)
		{
			using (var fixture = new StoreFixture())
			{
				File.WriteAllText(fixture.Xml, "<virtualModActivator fileVersion='0.3.0.0'><modList><modInfo modId='1' modName='Example' modFileName='Example.zip' modFilePath='mods'><fileLink realPath='a' virtualPath='b'><linkPriority>" + priority + "</linkPriority><isActive>" + active + "</isActive></fileLink></modInfo></modList></virtualModActivator>");
				Assert.Throws<InvalidDataException>(() => new VirtualModStoreFactory().Create(fixture.Xml, VirtualModActivator.CurrentVersion, x => true, (x, y) => ""));
				Assert.IsFalse(File.Exists(fixture.Database));
				Assert.IsTrue(File.Exists(fixture.Xml));
			}
		}

		/// <summary>Ordinary legacy loads retain their historical defaults for malformed optional link metadata.</summary>
		[Test]
		public void VirtualStore_LegacyLoadRetainsOptionalMetadataFallbacks()
		{
			using (var fixture = new StoreFixture())
			{
				File.WriteAllText(fixture.Xml, "<virtualModActivator fileVersion='0.3.0.0'><modList><modInfo modId='1' modName='Example' modFileName='Example.zip' modFilePath='mods'><fileLink realPath='a' virtualPath='b'><linkPriority>invalid</linkPriority><isActive>invalid</isActive></fileLink></modInfo></modList></virtualModActivator>");
				VirtualModStoreData loaded = new XmlVirtualModStore().Load(fixture.Xml, VirtualModActivator.CurrentVersion, x => true, (x, y) => "");
				Assert.AreEqual(1, loaded.VirtualLinks.Count);
				Assert.AreEqual(0, loaded.VirtualLinks[0].Priority);
				Assert.IsFalse(loaded.VirtualLinks[0].Active);
			}
		}

		/// <summary>Changing only case still addresses the same INI record in the native transaction buffers.</summary>
		[Test]
		public void IniIdentity_HashMatchesCaseInsensitiveEquality()
		{
			var first = new IniEdit("Settings.ini", "Display", "Value");
			var second = new IniEdit("settings.INI", "DISPLAY", "value");
			Assert.IsTrue(first.Equals(second));
			Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
		}

		/// <summary>UI batching must not hide the per-file mutations required by incremental persistence.</summary>
		[Test]
		public void BatchedObservableList_PreservesIndividualPersistenceNotifications()
		{
			var list = new ThreadSafeObservableList<string>();
			var actions = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
			int uiNotifications = 0;
			list.MutationOccurred += (sender, args) => actions.Add(args.Action);
			list.CollectionChanged += (sender, args) => uiNotifications++;
			using (list.BeginUpdate())
			{
				list.Add("first");
				list.Add("second");
				list.Remove("first");
			}
			CollectionAssert.AreEqual(new[] { System.Collections.Specialized.NotifyCollectionChangedAction.Add,
				System.Collections.Specialized.NotifyCollectionChangedAction.Add, System.Collections.Specialized.NotifyCollectionChangedAction.Remove }, actions);
			Assert.AreEqual(1, uiNotifications);
		}

		/// <summary>Creates a representative legacy log with ordered winners, fallback edits and both deployment roots.</summary>
		private static XDocument CreateLog(int count)
		{
			return new XDocument(new XElement("installLog", new XAttribute("fileVersion", InstallLog.CurrentVersion),
				new XElement("modList", new[] { "first", "second" }.Select(key => new XElement("mod", new XAttribute("path", key + ".zip"), new XAttribute("key", key),
					new XElement("version", new XAttribute("machineVersion", "1.0"), "1.0"), new XElement("name", key), new XElement("installDate", "date")))),
				new XElement("dataFiles", Enumerable.Range(0, count).Select(i => FileEntry("file" + i, "first", "second"))),
				new XElement("iniEdits", new XElement("ini", new XAttribute("file", "settings.ini"), new XAttribute("section", "Display"), new XAttribute("key", "Value"),
					new XElement("installingMods", new XElement("mod", new XAttribute("key", "first"), "<value>&\nnext")))),
				new XElement("gameSpecificEdits", new XElement("edit", new XAttribute("key", "Binary"), new XElement("installingMods",
					new XElement("mod", new XAttribute("key", "second"), Convert.ToBase64String(new byte[] { 0, 255, 1, 127 }))))),
				new XElement("deploymentFiles", new XElement("file", new XAttribute("path", "loader.dll"), new XAttribute("root", "GameRoot"),
					new XElement("installingMods", new XElement("mod", new XAttribute("key", "second")))))));
		}

		/// <summary>Creates a file ownership stack in fallback-to-winner order.</summary>
		private static XElement FileEntry(string path, params string[] owners)
		{
			return new XElement("file", new XAttribute("path", path), new XElement("installingMods", owners.Select(x => new XElement("mod", new XAttribute("key", x)))));
		}

		/// <summary>Creates a populated Virtual store whose metadata can be changed without a snapshot save.</summary>
		private static SQLiteVirtualModStore CreateVirtualStore(StoreFixture fixture)
		{
			var store = new SQLiteVirtualModStore(fixture.Xml, fixture.Database, VirtualModActivator.CurrentVersion, x => true, new XmlVirtualModStore());
			store.Initialize();
			var mod = new VirtualModInfo("1", "2", "Example", "example.zip", fixture.Root, "1.0");
			store.Save(VirtualModActivator.CurrentVersion, fixture.Xml, new IVirtualModInfo[] { mod }, new[] { new VirtualModLink("source", "target", 1, true, mod, ModInstallRoot.Data) });
			return store;
		}

		/// <summary>Isolates every native persistence scenario from existing user installations.</summary>
		private sealed class StoreFixture : IDisposable
		{
			internal readonly string Root = Path.Combine(Path.GetTempPath(), "nmm-store-" + Guid.NewGuid().ToString("N"));
			internal string Xml { get { return Path.Combine(Root, "InstallLog.xml"); } }
			internal string Database { get { return Path.ChangeExtension(Xml, ".sqlite"); } }
			/// <summary>Creates an isolated store directory.</summary>
			internal StoreFixture() { Directory.CreateDirectory(Root); }
			/// <summary>Executes a fault-injection or inspection statement against only the fixture's database.</summary>
			internal object Scalar(string sql)
			{
				using (var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = Database, Pooling = false, Enlist = false }.ConnectionString))
				{
					connection.Open();
					using (SQLiteCommand command = connection.CreateCommand()) { command.CommandText = sql; return command.ExecuteScalar(); }
				}
			}
			/// <summary>Executes a fixture-only SQL mutation.</summary>
			internal void Execute(string sql) { Scalar(sql); }
			/// <summary>Removes only this fixture's temporary directory.</summary>
			public void Dispose() { Directory.Delete(Root, true); }
		}
	}
}
