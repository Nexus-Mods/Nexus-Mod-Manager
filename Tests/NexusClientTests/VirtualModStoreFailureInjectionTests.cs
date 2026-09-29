namespace NexusClientTests
{
	using System;
	using System.Collections.Generic;
	using System.Data.SQLite;
	using System.IO;
	using System.Threading;
	using System.Transactions;

	using Nexus.Client.ModManagement;

	using NUnit.Framework;

	/// <summary>
	/// C6.16-D failure coverage for the SQLite Virtual primary store and its XML compatibility shadow.
	/// </summary>
	[TestFixture]
	[Category("CollectionsGateA")]
	public class VirtualModStoreFailureInjectionTests
	{
		private static readonly Version CurrentVersion = new Version("0.3.0.0");

		/// <summary>
		/// Verifies that a failed XML shadow update cannot replace a successfully committed SQLite primary on restart.
		/// </summary>
		[Test]
		public void Save_PrimaryCommittedShadowFailure_RestartLoadsPrimaryWithoutStaleFallback()
		{
			string root = CreateTempDirectory();
			try
			{
				string xmlPath = Path.Combine(root, "VirtualModConfig.xml");
				string databasePath = Path.Combine(root, "VirtualModConfig.sqlite");
				XmlVirtualModStore xmlStore = new XmlVirtualModStore();
				VirtualModStoreData stale = CreateStoreData("stale-shadow");
				xmlStore.Save(CurrentVersion, xmlPath, stale.VirtualMods, stale.VirtualLinks);

				SQLiteVirtualModStore store = CreateSqliteStore(xmlPath, databasePath, xmlStore);
				VirtualModStoreData committed = CreateStoreData("committed-primary");
				using (new FileStream(xmlPath, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					Assert.DoesNotThrow(() => store.Save(CurrentVersion, xmlPath, committed.VirtualMods, committed.VirtualLinks));
				}

				AssertSingleModName(xmlStore.Load(xmlPath, CurrentVersion, IsValidVersion, MissingFileVersion), "stale-shadow");

				SQLiteVirtualModStore restartedStore = CreateSqliteStore(xmlPath, databasePath, new XmlVirtualModStore());
				Assert.IsTrue(restartedStore.CanLoadExistingStore(), "The committed SQLite primary must remain a valid restart authority.");
				AssertSingleModName(restartedStore.Load(xmlPath, CurrentVersion, IsValidVersion, MissingFileVersion), "committed-primary");
			}
			finally
			{
				DeleteDirectory(root);
			}
		}

		/// <summary>
		/// Verifies that a short-lived external reader does not make the primary Virtual SQLite write fail immediately.
		/// </summary>
		[Test]
		public void Save_TransientExternalReaderLock_WaitsForReaderAndCommitsPrimary()
		{
			string root = CreateTempDirectory();
			try
			{
				string xmlPath = Path.Combine(root, "VirtualModConfig.xml");
				string databasePath = Path.Combine(root, "VirtualModConfig.sqlite");
				SQLiteVirtualModStore store = CreateSqliteStore(xmlPath, databasePath, new XmlVirtualModStore());
				VirtualModStoreData initial = CreateStoreData("initial");
				store.Save(CurrentVersion, xmlPath, initial.VirtualMods, initial.VirtualLinks);

				var blockerBuilder = new SQLiteConnectionStringBuilder
				{
					DataSource = databasePath,
					Pooling = false,
					ReadOnly = true,
					FailIfMissing = true
				};

				using (var blocker = new SQLiteConnection(blockerBuilder.ConnectionString))
				{
					blocker.Open();
					using (SQLiteTransaction blockerTransaction = blocker.BeginTransaction())
					using (SQLiteCommand blockerCommand = blocker.CreateCommand())
					{
						blockerCommand.Transaction = blockerTransaction;
						blockerCommand.CommandText = "SELECT COUNT(*) FROM VirtualMods;";
						Assert.GreaterOrEqual(Convert.ToInt32(blockerCommand.ExecuteScalar()), 1);

						Exception saveFailure = null;
						using (var started = new ManualResetEvent(false))
						using (var completed = new ManualResetEvent(false))
						{
							var worker = new Thread(() =>
							{
								started.Set();
								try
								{
									VirtualModStoreData replacement = CreateStoreData("replacement");
									store.Save(CurrentVersion, xmlPath, replacement.VirtualMods, replacement.VirtualLinks);
								}
								catch (Exception e)
								{
									saveFailure = e;
								}
								finally
								{
									completed.Set();
								}
							});
							worker.IsBackground = true;
							worker.Start();

							Assert.IsTrue(started.WaitOne(2000), "The Virtual store writer did not start.");
							Thread.Sleep(200);
							Assert.IsFalse(completed.WaitOne(0), "The writer should still be waiting for the short-lived reader lock.");

							blockerTransaction.Commit();
							Assert.IsTrue(completed.WaitOne(5000), "The Virtual store writer did not resume after the reader released its lock.");
							worker.Join();
						}

						Assert.IsNull(saveFailure);
					}
				}

				AssertSingleModName(store.Load(xmlPath, CurrentVersion, IsValidVersion, MissingFileVersion), "replacement");
			}
			finally
			{
				DeleteDirectory(root);
			}
		}

		/// <summary>
		/// Verifies that Virtual store connections do not enlist in the installer's ambient TransactionScope.
		/// The VMA owns its own transaction enlistment/persistence boundary; SQLite auto-enlistment can otherwise
		/// retain a write/read lock until the ambient transaction completes and deadlock a later SaveList call.
		/// </summary>
		[Test]
		public void Save_InsideAmbientTransaction_DoesNotRetainSqliteLockUntilScopeCompletion()
		{
			string root = CreateTempDirectory();
			try
			{
				string xmlPath = Path.Combine(root, "VirtualModConfig.xml");
				string databasePath = Path.Combine(root, "VirtualModConfig.sqlite");
				SQLiteVirtualModStore store = CreateSqliteStore(xmlPath, databasePath, new XmlVirtualModStore());

				using (var scope = new TransactionScope())
				{
					VirtualModStoreData first = CreateStoreData("ambient-first");
					Assert.DoesNotThrow(() => store.Save(CurrentVersion, xmlPath, first.VirtualMods, first.VirtualLinks));

					VirtualModStoreData second = CreateStoreData("ambient-second");
					Assert.DoesNotThrow(() => store.Save(CurrentVersion, xmlPath, second.VirtualMods, second.VirtualLinks));
					scope.Complete();
				}

				AssertSingleModName(store.Load(xmlPath, CurrentVersion, IsValidVersion, MissingFileVersion), "ambient-second");
			}
			finally
			{
				DeleteDirectory(root);
			}
		}

		/// <summary>
		/// Verifies that the per-link install root survives both SQLite restart and XML compatibility-shadow reload.
		/// GameRoot links are otherwise reinterpreted as Data links after restart and disappear from authoritative state.
		/// </summary>
		[Test]
		public void Save_GameRootLink_RestartPreservesInstallRoot()
		{
			string root = CreateTempDirectory();
			try
			{
				string xmlPath = Path.Combine(root, "VirtualModConfig.xml");
				string databasePath = Path.Combine(root, "VirtualModConfig.sqlite");
				XmlVirtualModStore xmlStore = new XmlVirtualModStore();
				SQLiteVirtualModStore store = CreateSqliteStore(xmlPath, databasePath, xmlStore);
				VirtualModStoreData data = CreateStoreData("game-root", ModInstallRoot.GameRoot);

				store.Save(CurrentVersion, xmlPath, data.VirtualMods, data.VirtualLinks);

				SQLiteVirtualModStore restartedStore = CreateSqliteStore(xmlPath, databasePath, new XmlVirtualModStore());
				Assert.IsTrue(restartedStore.CanLoadExistingStore());
				AssertSingleLinkRoot(restartedStore.Load(xmlPath, CurrentVersion, IsValidVersion, MissingFileVersion), ModInstallRoot.GameRoot);
				AssertSingleLinkRoot(xmlStore.Load(xmlPath, CurrentVersion, IsValidVersion, MissingFileVersion), ModInstallRoot.GameRoot);
			}
			finally
			{
				DeleteDirectory(root);
			}
		}

		/// <summary>
		/// Verifies that the pre-InstallRoot SQLite schema is upgraded in place instead of being rejected and
		/// silently forcing the application back to the equally root-less legacy XML representation.
		/// </summary>
		[Test]
		public void CanLoadExistingStore_LegacySchemaV1_MigratesInstallRootColumn()
		{
			string root = CreateTempDirectory();
			try
			{
				string xmlPath = Path.Combine(root, "VirtualModConfig.xml");
				string databasePath = Path.Combine(root, "VirtualModConfig.sqlite");
				CreateLegacySchemaV1Database(databasePath);

				SQLiteVirtualModStore store = CreateSqliteStore(xmlPath, databasePath, new XmlVirtualModStore());
				Assert.IsTrue(store.CanLoadExistingStore(), "Schema v1 should migrate to the root-aware schema in place.");
				AssertSingleLinkRoot(store.Load(xmlPath, CurrentVersion, IsValidVersion, MissingFileVersion), ModInstallRoot.Data);

				using (var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ConnectionString))
				{
					connection.Open();
					using (SQLiteCommand command = connection.CreateCommand())
					{
						command.CommandText = "SELECT Value FROM StoreMetadata WHERE Key = 'schema_version';";
						Assert.AreEqual("2", Convert.ToString(command.ExecuteScalar()));
					}
				}
			}
			finally
			{
				DeleteDirectory(root);
			}
		}

		/// <summary>
		/// Verifies that a primary SQLite failure aborts before publishing newer compatibility-shadow state.
		/// </summary>
		[Test]
		public void Save_PrimaryFailure_DoesNotAdvanceCompatibilityShadow()
		{
			string root = CreateTempDirectory();
			try
			{
				string xmlPath = Path.Combine(root, "VirtualModConfig.xml");
				string databasePath = Path.Combine(root, "database-as-directory");
				Directory.CreateDirectory(databasePath);
				XmlVirtualModStore xmlStore = new XmlVirtualModStore();
				VirtualModStoreData original = CreateStoreData("original-shadow");
				xmlStore.Save(CurrentVersion, xmlPath, original.VirtualMods, original.VirtualLinks);

				SQLiteVirtualModStore store = CreateSqliteStore(xmlPath, databasePath, xmlStore);
				VirtualModStoreData attempted = CreateStoreData("must-not-publish");
				Assert.Catch<Exception>(() => store.Save(CurrentVersion, xmlPath, attempted.VirtualMods, attempted.VirtualLinks));

				AssertSingleModName(xmlStore.Load(xmlPath, CurrentVersion, IsValidVersion, MissingFileVersion), "original-shadow");
			}
			finally
			{
				DeleteDirectory(root);
			}
		}

		private static SQLiteVirtualModStore CreateSqliteStore(string xmlPath, string databasePath, XmlVirtualModStore xmlStore)
		{
			return new SQLiteVirtualModStore(xmlPath, databasePath, CurrentVersion, IsValidVersion, xmlStore);
		}

		private static VirtualModStoreData CreateStoreData(string name)
		{
			return CreateStoreData(name, ModInstallRoot.Data);
		}

		private static VirtualModStoreData CreateStoreData(string name, ModInstallRoot installRoot)
		{
			IVirtualModInfo mod = new VirtualModInfo("1", "1", name, name + ".7z", @"C:\NMM\Mods", "1.0");
			var mods = new List<IVirtualModInfo> { mod };
			var links = new List<IVirtualModLink>
			{
				new VirtualModLink(@"C:\NMM\Virtual\" + name + @"\file.dds", @"textures\" + name + ".dds", 0, true, mod, installRoot)
			};
			return new VirtualModStoreData(mods, links);
		}

		private static void AssertSingleLinkRoot(VirtualModStoreData data, ModInstallRoot expectedRoot)
		{
			Assert.IsNotNull(data);
			Assert.AreEqual(1, data.VirtualLinks.Count);
			Assert.AreEqual(expectedRoot, data.VirtualLinks[0].InstallRoot);
		}

		private static void CreateLegacySchemaV1Database(string databasePath)
		{
			using (var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ConnectionString))
			{
				connection.Open();
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.CommandText =
						"CREATE TABLE StoreMetadata (Key TEXT NOT NULL PRIMARY KEY, Value TEXT NOT NULL);" +
						"CREATE TABLE VirtualMods (VirtualModId INTEGER PRIMARY KEY AUTOINCREMENT, ModOrder INTEGER NOT NULL, ModId TEXT NOT NULL, DownloadId TEXT NOT NULL, UpdatedDownloadId TEXT NOT NULL, ModName TEXT NOT NULL, ModFileName TEXT NOT NULL, ModNewFileName TEXT NOT NULL, ModFilePath TEXT NOT NULL, FileVersion TEXT NOT NULL);" +
						"CREATE TABLE VirtualLinks (VirtualLinkId INTEGER PRIMARY KEY AUTOINCREMENT, VirtualModId INTEGER NOT NULL, LinkOrder INTEGER NOT NULL, RealPath TEXT NOT NULL, VirtualPath TEXT NOT NULL, Priority INTEGER NOT NULL, IsActive INTEGER NOT NULL, FOREIGN KEY(VirtualModId) REFERENCES VirtualMods(VirtualModId) ON DELETE CASCADE);" +
						"INSERT INTO StoreMetadata(Key, Value) VALUES ('schema_version', '1');" +
						"INSERT INTO StoreMetadata(Key, Value) VALUES ('file_version', '0.3.0.0');" +
						"INSERT INTO VirtualMods(VirtualModId, ModOrder, ModId, DownloadId, UpdatedDownloadId, ModName, ModFileName, ModNewFileName, ModFilePath, FileVersion) VALUES (1, 0, '1', '1', '', 'legacy', 'legacy.7z', '', 'C:\\NMM\\Mods', '1.0');" +
						"INSERT INTO VirtualLinks(VirtualModId, LinkOrder, RealPath, VirtualPath, Priority, IsActive) VALUES (1, 0, 'legacy-file', 'legacy-file', 0, 1);";
					command.ExecuteNonQuery();
				}
			}
		}

		private static void AssertSingleModName(VirtualModStoreData data, string expectedName)
		{
			Assert.IsNotNull(data);
			Assert.AreEqual(1, data.VirtualMods.Count);
			Assert.AreEqual(expectedName, data.VirtualMods[0].ModName);
		}

		private static bool IsValidVersion(Version version)
		{
			return version == CurrentVersion;
		}

		private static string MissingFileVersion(string modFileName, string modFilePath)
		{
			return String.Empty;
		}

		private static string CreateTempDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "nmm-c616d-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}

		private static void DeleteDirectory(string path)
		{
			try
			{
				if (Directory.Exists(path))
					Directory.Delete(path, true);
			}
			catch
			{
			}
		}
	}
}
