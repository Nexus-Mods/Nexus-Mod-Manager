namespace NexusClientTests
{
	using System;
	using System.Collections.Generic;
	using System.Data.SQLite;
	using System.IO;
	using System.Threading;

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
			IVirtualModInfo mod = new VirtualModInfo("1", "1", name, name + ".7z", @"C:\NMM\Mods", "1.0");
			var mods = new List<IVirtualModInfo> { mod };
			var links = new List<IVirtualModLink>
			{
				new VirtualModLink(@"C:\NMM\Virtual\" + name + @"\file.dds", @"textures\" + name + ".dds", 0, true, mod)
			};
			return new VirtualModStoreData(mods, links);
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
