namespace NexusClientTests
{
	using System;
	using System.Collections;
	using System.Collections.Generic;
	using System.Data.SQLite;
	using System.IO;
	using System.Reflection;
	using Nexus.Client.ModManagement;
	using Nexus.Client.ModManagement.InstallationLog;
	using Nexus.Client.Mods;
	using Nexus.Client.Mods.Formats.FOMod;
	using NUnit.Framework;

	/// <summary>
	/// Verifies durable identity reconciliation, current-binding lifetime and indexed Sort inheritance behavior.
	/// </summary>
	public class ModSortOrderServiceTests
	{
		/// <summary>
		/// Ensures an explicit numeric value survives ModId and DownloadId arriving as separate identity changes.
		/// </summary>
		[Test]
		public void ExplicitNumericIdentityReconciliationPersistsAcrossReload()
		{
			var storage = CreateStorage();
			var store = storage.CreateStore();
			var service = new ModSortOrderService(store);
			var mod = CreateMod("Local.7z");
			service.RebuildCurrentArchiveInventory(new[] { mod }, null);
			service.Resolve(mod, ModSortOrderAssignmentContext.StartupOrDiscovery, new[] { mod });
			service.SetSortNumber(mod, 12);

			mod.Id = "100";
			service.ReconcileIdentity(mod);
			AssertAssignment(storage, store, "Local.7z", "100", null, 12, ModSortOrderAssignmentState.ExplicitNumeric);

			mod.DownloadId = "200";
			service.ReconcileIdentity(mod);
			AssertAssignment(storage, store, "Local.7z", "100", "200", 12, ModSortOrderAssignmentState.ExplicitNumeric);

			var reloaded = new ModSortOrderService(storage.CreateStore());
			var restoredMod = CreateMod("Local.7z", "100", "200");
			reloaded.RebuildCurrentArchiveInventory(new[] { restoredMod }, null);
			Assert.That(reloaded.Resolve(restoredMod, ModSortOrderAssignmentContext.StartupOrDiscovery, new[] { restoredMod }), Is.EqualTo(12));
		}

		/// <summary>
		/// Ensures an explicit blank remains blank when a previously local archive gains repository identity.
		/// </summary>
		[Test]
		public void ExplicitBlankIdentityReconciliationPersistsAcrossReload()
		{
			var storage = CreateStorage();
			var store = storage.CreateStore();
			var service = new ModSortOrderService(store);
			var mod = CreateMod("Blank.7z");
			service.RebuildCurrentArchiveInventory(new[] { mod }, null);
			service.Resolve(mod, ModSortOrderAssignmentContext.StartupOrDiscovery, new[] { mod });
			service.SetSortNumber(mod, null);

			mod.Id = "101";
			mod.DownloadId = "201";
			service.ReconcileIdentity(mod);
			AssertAssignment(storage, store, "Blank.7z", "101", "201", null, ModSortOrderAssignmentState.ExplicitBlank);

			var reloaded = new ModSortOrderService(storage.CreateStore());
			var restoredMod = CreateMod("Blank.7z", "101", "201");
			reloaded.RebuildCurrentArchiveInventory(new[] { restoredMod }, null);
			Assert.That(reloaded.Resolve(restoredMod, ModSortOrderAssignmentContext.StartupOrDiscovery, new[] { restoredMod }), Is.Null);
			Assert.That(reloaded.TryGetSortNumber(restoredMod, out var restoredValue), Is.True);
			Assert.That(restoredValue, Is.Null);
		}

		/// <summary>
		/// Ensures a failed durable identity write leaves both SQLite and the active in-memory value unchanged.
		/// </summary>
		[Test]
		public void FailedIdentityReconciliationDoesNotPublishUndurableState()
		{
			var storage = CreateStorage();
			var store = storage.CreateStore();
			CreateIdentityUpdateFailureTrigger(store);
			var service = new ModSortOrderService(store);
			var mod = CreateMod("Failure.7z");
			service.RebuildCurrentArchiveInventory(new[] { mod }, null);
			service.Resolve(mod, ModSortOrderAssignmentContext.StartupOrDiscovery, new[] { mod });
			service.SetSortNumber(mod, 12);

			mod.Id = "102";
			mod.DownloadId = "202";
			Assert.Throws<SQLiteException>(() => service.ReconcileIdentity(mod));
			Assert.That(service.GetSortNumber(mod), Is.EqualTo(12));
			AssertAssignment(storage, store, "Failure.7z", null, null, 12, ModSortOrderAssignmentState.ExplicitNumeric);
		}

		/// <summary>
		/// Ensures trusted download identities remain MIN donors even when the live IMod metadata is empty, including after reload.
		/// </summary>
		[Test]
		public void MetadataDisabledDownloadsUsePersistedEffectiveIdentityForMinimumInheritance()
		{
			var storage = CreateStorage();
			var service = new ModSortOrderService(storage.CreateStore());
			var managed = new List<IMod>();
			service.RebuildCurrentArchiveInventory(managed, null);

			var first = AddTrustedDownload(service, managed, "A.7z", "200", "300");
			service.SetSortNumber(first, 10);
			var second = AddTrustedDownload(service, managed, "B.7z", "200", "301");
			service.SetSortNumber(second, 30);
			var blank = AddTrustedDownload(service, managed, "C.7z", "200", "302");
			service.SetSortNumber(blank, null);

			var reloaded = new ModSortOrderService(storage.CreateStore());
			var restored = new List<IMod>
			{
				CreateMod("A.7z"),
				CreateMod("B.7z"),
				CreateMod("C.7z")
			};
			reloaded.RebuildCurrentArchiveInventory(restored, null);
			foreach (var current in restored)
			{
				reloaded.Resolve(current, ModSortOrderAssignmentContext.StartupOrDiscovery, new ThrowingEnumerable<IMod>());
			}

			var next = CreateMod("D.7z");
			reloaded.TrackManagedMod(next);
			Assert.That(reloaded.ResolveAddOrDownload(next, new ThrowingEnumerable<IMod>(), "200", "303"), Is.EqualTo(10));
		}

		/// <summary>
		/// Ensures removing a live archive unbinds it while preserving exact historical recovery at a new locator.
		/// </summary>
		[Test]
		public void RemovedArchiveCanRestoreExactAssignmentAtDifferentPathWithoutRestart()
		{
			var storage = CreateStorage();
			var service = new ModSortOrderService(storage.CreateStore());
			var original = CreateMod("Original.7z", "300", "400");
			service.RebuildCurrentArchiveInventory(new[] { original }, null);
			service.Resolve(original, ModSortOrderAssignmentContext.AddOrDownload, new[] { original });
			service.SetSortNumber(original, 25);
			service.UntrackManagedMod(original);

			var restored = CreateMod("Restored.7z", "300", "400");
			service.TrackManagedMod(restored);
			Assert.That(service.Resolve(restored, ModSortOrderAssignmentContext.AddOrDownload, new[] { restored }), Is.EqualTo(25));
			Assert.That(service.GetSortNumber(restored), Is.EqualTo(25));
		}

		/// <summary>
		/// Ensures two real current copies keep independent durable rows instead of stealing one another's exact binding.
		/// </summary>
		[Test]
		public void ConcurrentExactCopiesRemainIndependent()
		{
			var storage = CreateStorage();
			var service = new ModSortOrderService(storage.CreateStore());
			var first = CreateMod("First.7z", "310", "410");
			service.RebuildCurrentArchiveInventory(new[] { first }, null);
			service.Resolve(first, ModSortOrderAssignmentContext.AddOrDownload, new[] { first });
			service.SetSortNumber(first, 25);

			var second = CreateMod("Second.7z", "310", "410");
			service.TrackManagedMod(second);
			service.Resolve(second, ModSortOrderAssignmentContext.AddOrDownload, new[] { first, second });
			service.SetSortNumber(second, 40);

			Assert.That(service.GetSortNumber(first), Is.EqualTo(25));
			Assert.That(service.GetSortNumber(second), Is.EqualTo(40));
		}

		/// <summary>
		/// Ensures an active missing-archive placeholder protects exact recovery but never contributes a MIN value.
		/// </summary>
		[Test]
		public void BindingProtectorDoesNotDonateMinimumValue()
		{
			var storage = CreateStorage();
			var service = new ModSortOrderService(storage.CreateStore());
			var installed = CreateMod("Installed.7z", "500", "600");
			service.RebuildCurrentArchiveInventory(new[] { installed }, null);
			service.Resolve(installed, ModSortOrderAssignmentContext.AddOrDownload, new[] { installed });
			service.SetSortNumber(installed, 10);
			service.UntrackManagedMod(installed);
			service.TrackBindingProtector(installed);
			service.Resolve(installed, ModSortOrderAssignmentContext.StartupOrDiscovery, new IMod[0]);

			var next = CreateMod("Next.7z", "500", "601");
			service.TrackManagedMod(next);
			Assert.That(service.Resolve(next, ModSortOrderAssignmentContext.AddOrDownload, new[] { next }), Is.Null);
		}

		/// <summary>
		/// Ensures passive startup resolution uses the prebuilt current-identity indexes rather than rescanning the supplied library.
		/// </summary>
		[Test]
		public void StartupResolutionDoesNotEnumerateManagedLibraryPerArchive()
		{
			var storage = CreateStorage();
			var service = new ModSortOrderService(storage.CreateStore());
			var mods = new[]
			{
				CreateMod("One.7z", "700", "800"),
				CreateMod("Two.7z", "701", "801"),
				CreateMod("Three.7z", "702", "802")
			};
			service.RebuildCurrentArchiveInventory(mods, null);

			foreach (var mod in mods)
			{
				Assert.DoesNotThrow(() => service.Resolve(mod, ModSortOrderAssignmentContext.StartupOrDiscovery, new ThrowingEnumerable<IMod>()));
			}
		}

		/// <summary>
		/// Adds one metadata-disabled download using only the trusted repository identity supplied by the download context.
		/// </summary>
		private static IMod AddTrustedDownload(ModSortOrderService service, IList<IMod> managed, string filename, string modId, string downloadId)
		{
			var mod = CreateMod(filename);
			managed.Add(mod);
			service.TrackManagedMod(mod);
			service.ResolveAddOrDownload(mod, managed, modId, downloadId);
			return mod;
		}

		/// <summary>
		/// Creates one lightweight mod object suitable for Sort-order service tests.
		/// </summary>
		private static IMod CreateMod(string filename, string modId = null, string downloadId = null)
		{
			return new InstallLog.DummyMod(filename, filename)
			{
				Id = modId,
				DownloadId = downloadId
			};
		}

		/// <summary>
		/// Creates isolated cache and Mods directories for one SQLite-backed test.
		/// </summary>
		private static SortTestStorage CreateStorage()
		{
			var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ModSortOrderTests", Guid.NewGuid().ToString("N"));
			var cache = Path.Combine(root, "Cache");
			var mods = Path.Combine(root, "Mods");
			Directory.CreateDirectory(cache);
			Directory.CreateDirectory(mods);
			return new SortTestStorage(cache, mods);
		}

		/// <summary>
		/// Reads one assignment through a fresh SQLite connection and compares its durable fields.
		/// </summary>
		private static void AssertAssignment(SortTestStorage storage, ModSortOrderStore store, string archivePath, string expectedModId, string expectedDownloadId, int? expectedSortNumber, ModSortOrderAssignmentState expectedState)
		{
			using (var connection = OpenFreshConnection(storage.DatabasePath))
			using (var command = connection.CreateCommand())
			{
				command.CommandText = @"SELECT mod_id, download_id, sort_number, assignment_state
FROM mod_sort_assignments
WHERE archive_path = @archive_path
ORDER BY assignment_id DESC
LIMIT 1;";
				command.Parameters.AddWithValue("@archive_path", store.GetArchiveLocator(archivePath));
				using (var reader = command.ExecuteReader())
				{
					Assert.That(reader.Read(), Is.True);
					Assert.That(reader.IsDBNull(0) ? null : reader.GetString(0), Is.EqualTo(expectedModId));
					Assert.That(reader.IsDBNull(1) ? null : reader.GetString(1), Is.EqualTo(expectedDownloadId));
					Assert.That(reader.IsDBNull(2) ? (int?)null : Convert.ToInt32(reader.GetValue(2)), Is.EqualTo(expectedSortNumber));
					Assert.That((ModSortOrderAssignmentState)Convert.ToInt32(reader.GetValue(3)), Is.EqualTo(expectedState));
				}
			}
		}

		/// <summary>
		/// Creates a trigger inside the store's shared transaction so the next identity update fails deterministically.
		/// </summary>
		private static void CreateIdentityUpdateFailureTrigger(ModSortOrderStore store)
		{
			var databaseField = typeof(ModSortOrderStore).GetField("_database", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(databaseField, Is.Not.Null);
			var database = databaseField.GetValue(store);
			var databaseType = database.GetType();
			var connectionField = databaseType.GetField("Connection", BindingFlags.Instance | BindingFlags.Public);
			var transactionField = databaseType.GetField("Transaction", BindingFlags.Instance | BindingFlags.Public);
			Assert.That(connectionField, Is.Not.Null);
			Assert.That(transactionField, Is.Not.Null);

			var connection = (SQLiteConnection)connectionField.GetValue(database);
			var transaction = (SQLiteTransaction)transactionField.GetValue(database);
			using (var command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"CREATE TRIGGER fail_sort_identity_update
BEFORE UPDATE OF mod_id, download_id ON mod_sort_assignments
BEGIN
    SELECT RAISE(ABORT, 'test identity write failure');
END;";
				command.ExecuteNonQuery();
			}
		}

		/// <summary>
		/// Opens an independent connection so assertions observe only committed SQLite state.
		/// </summary>
		private static SQLiteConnection OpenFreshConnection(string databasePath)
		{
			var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder
			{
				DataSource = databasePath,
				ForeignKeys = true,
				JournalMode = SQLiteJournalModeEnum.Delete,
				Pooling = false
			}.ConnectionString);
			connection.Open();
			return connection;
		}

		/// <summary>
		/// Holds the isolated storage paths used by one SQLite-backed Sort-order test.
		/// </summary>
		private sealed class SortTestStorage
		{
			/// <summary>
			/// Initializes one isolated test-storage descriptor.
			/// </summary>
			public SortTestStorage(string cacheDirectory, string modDirectory)
			{
				CacheDirectory = cacheDirectory;
				ModDirectory = modDirectory;
			}

			public string CacheDirectory { get; }
			public string ModDirectory { get; }
			public string DatabasePath => Path.Combine(CacheDirectory, "fomodArchiveMetadata.sqlite");

			/// <summary>
			/// Creates a store bound to this isolated cache and Mods root.
			/// </summary>
			public ModSortOrderStore CreateStore()
			{
				return new ModSortOrderStore(CacheDirectory, ModDirectory);
			}
		}

		/// <summary>
		/// Fails if production code attempts to enumerate the full managed library after current indexes have been built.
		/// </summary>
		private sealed class ThrowingEnumerable<T> : IEnumerable<T>
		{
			/// <summary>
			/// Throws because indexed resolution must not enumerate this sequence.
			/// </summary>
			public IEnumerator<T> GetEnumerator()
			{
				throw new InvalidOperationException("The full managed library was enumerated after current indexes were built.");
			}

			/// <summary>
			/// Returns the generic enumerator implementation and therefore throws identically.
			/// </summary>
			IEnumerator IEnumerable.GetEnumerator()
			{
				return GetEnumerator();
			}
		}
	}
}
