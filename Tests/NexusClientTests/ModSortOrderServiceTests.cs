namespace NexusClientTests
{
	using System;
	using System.Collections;
	using System.Collections.Generic;
	using System.Collections.Specialized;
	using System.Data.SQLite;
	using System.IO;
	using System.Reflection;
	using System.Runtime.Serialization;
	using Nexus.Client.ModManagement;
	using Nexus.Client.ModManagement.InstallationLog;
	using Nexus.Client.Mods;
	using Nexus.Client.Mods.Formats.FOMod;
	using Nexus.Client.Util.Collections;
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
		/// Ensures logical capture can read the complete already-resolved Sort state without re-resolving or flattening explicit blank.
		/// </summary>
		[Test]
		public void ResolvedAssignmentReadPreservesExplicitState()
		{
			var storage = CreateStorage();
			var service = new ModSortOrderService(storage.CreateStore());
			var mod = CreateMod("Capture.7z", "110", "210");
			service.RebuildCurrentArchiveInventory(new[] { mod }, null);
			service.Resolve(mod, ModSortOrderAssignmentContext.StartupOrDiscovery, new[] { mod });
			service.SetSortNumber(mod, null);

			ModSortOrderRecord assignment;
			Assert.That(service.TryGetResolvedAssignment(mod.ModArchivePath, out assignment), Is.True);
			Assert.That(assignment.AssignmentState, Is.EqualTo(ModSortOrderAssignmentState.ExplicitBlank));
			Assert.That(assignment.SortNumber, Is.Null);
			Assert.That(assignment.ModId, Is.EqualTo("110"));
			Assert.That(assignment.DownloadId, Is.EqualTo("210"));
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
		/// Ensures replacing the install log moves ActiveMods tracking and rebuilds binding protectors, including a second rollback-style replacement.
		/// </summary>
		[Test]
		public void InstallLogReplacementRebindsSortActiveModsTracking()
		{
			var storage = CreateStorage();
			var service = new ModSortOrderService(storage.CreateStore());
			var registry = new ModRegistry(null, null);
			var manager = (ModManager)FormatterServices.GetUninitializedObject(typeof(ModManager));
			SetPrivateField(manager, "<ManagedModRegistry>k__BackingField", registry);
			SetPrivateField(manager, "<SortOrderService>k__BackingField", service);

			var oldItems = new ThreadSafeObservableList<IMod>();
			var newItems = new ThreadSafeObservableList<IMod>();
			var rollbackItems = new ThreadSafeObservableList<IMod>();
			var oldProtector = CreateMod(Path.Combine(storage.ModDirectory, "Old.7z"), "810", "910");
			var newProtector = CreateMod(Path.Combine(storage.ModDirectory, "New.7z"), "811", "911");
			var rollbackProtector = CreateMod(Path.Combine(storage.ModDirectory, "Rollback.7z"), "812", "912");
			oldItems.Add(oldProtector);
			newItems.Add(newProtector);
			rollbackItems.Add(rollbackProtector);
			var oldLog = CreateInstallLog(oldItems);
			var newLog = CreateInstallLog(newItems);
			var rollbackLog = CreateInstallLog(rollbackItems);

			SetPrivateField(manager, "<InstallationLog>k__BackingField", oldLog);
			service.RebuildCurrentArchiveInventory(new IMod[0], new[] { oldProtector });
			service.Resolve(oldProtector, ModSortOrderAssignmentContext.StartupOrDiscovery, new IMod[0]);
			service.SetSortNumber(oldProtector, 25);
			SubscribeActiveModsHandler(manager, oldLog);

			SetPrivateField(manager, "<InstallationLog>k__BackingField", newLog);
			InvokeRebindSortOrderActiveModsTracking(manager, oldLog);
			Assert.That(GetCurrentArchiveCount(service), Is.EqualTo(1));

			var restoredOld = CreateMod(Path.Combine(storage.ModDirectory, "OldRestored.7z"), "810", "910");
			service.TrackManagedMod(restoredOld);
			Assert.That(service.Resolve(restoredOld, ModSortOrderAssignmentContext.AddOrDownload, new[] { restoredOld }), Is.EqualTo(25), "The obsolete protector must not block exact recovery after replacement.");
			service.UntrackManagedMod(restoredOld);

			service.SetSortNumber(newProtector, 30);
			var duplicateNew = CreateMod(Path.Combine(storage.ModDirectory, "NewDuplicate.7z"), "811", "911");
			service.TrackManagedMod(duplicateNew);
			Assert.That(service.Resolve(duplicateNew, ModSortOrderAssignmentContext.AddOrDownload, new[] { duplicateNew }), Is.Null, "The replacement protector must prevent another current copy from stealing its exact assignment.");
			service.UntrackManagedMod(duplicateNew);

			oldItems.Add(CreateMod(Path.Combine(storage.ModDirectory, "OldIgnored.7z"), "813", "913"));
			Assert.That(GetCurrentArchiveCount(service), Is.EqualTo(1), "The replaced ActiveMods collection must be detached.");
			newItems.Add(CreateMod(Path.Combine(storage.ModDirectory, "NewTracked.7z"), "814", "914"));
			Assert.That(GetCurrentArchiveCount(service), Is.EqualTo(2), "The replacement ActiveMods collection must be tracked.");

			SetPrivateField(manager, "<InstallationLog>k__BackingField", rollbackLog);
			InvokeRebindSortOrderActiveModsTracking(manager, newLog);
			Assert.That(GetCurrentArchiveCount(service), Is.EqualTo(1));
			newItems.Add(CreateMod(Path.Combine(storage.ModDirectory, "NewIgnored.7z"), "815", "915"));
			Assert.That(GetCurrentArchiveCount(service), Is.EqualTo(1), "Rollback replacement must detach the intermediate ActiveMods collection.");
			rollbackItems.Add(CreateMod(Path.Combine(storage.ModDirectory, "RollbackTracked.7z"), "816", "916"));
			Assert.That(GetCurrentArchiveCount(service), Is.EqualTo(2), "Rollback replacement must track the restored ActiveMods collection.");
		}

		/// <summary>
		/// Ensures manager startup/reset subscription rebuilds perform one add/remove operation per managed object.
		/// </summary>
		[Test]
		public void ManagerIdentityTrackingBulkRebuildIsLinearInSubscriptions()
		{
			const int modCount = 5000;
			var addCount = 0;
			var removeCount = 0;
			var manager = (ModManager)FormatterServices.GetUninitializedObject(typeof(ModManager));
			var tracked = CreateManagerIdentityTrackingSet();
			SetPrivateField(manager, "m_setSortOrderTrackedManagedMods", tracked);
			var mods = new List<IMod>(modCount);
			for (var index = 0; index < modCount; index++)
			{
				mods.Add(CreateIdentityTrackingMod(() => addCount++, () => removeCount++));
			}

			InvokeRebuildSortOrderManagedIdentityTracking(manager, mods);
			Assert.That(addCount, Is.EqualTo(modCount));
			Assert.That(removeCount, Is.Zero);
			Assert.That(tracked.Count, Is.EqualTo(modCount));

			InvokeRebuildSortOrderManagedIdentityTracking(manager, mods);
			Assert.That(addCount, Is.EqualTo(modCount * 2));
			Assert.That(removeCount, Is.EqualTo(modCount));
			Assert.That(tracked.Count, Is.EqualTo(modCount));

			InvokeClearSortOrderManagedIdentityTracking(manager);
			Assert.That(removeCount, Is.EqualTo(modCount * 2));
			Assert.That(tracked.Count, Is.Zero);
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
		/// Creates the manager's production reference-identity set for an uninitialized manager test shell.
		/// </summary>
		private static HashSet<IMod> CreateManagerIdentityTrackingSet()
		{
			var field = typeof(ModManager).GetField("m_setSortOrderTrackedManagedMods", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(field, Is.Not.Null);
			Assert.That(field.FieldType, Is.EqualTo(typeof(HashSet<IMod>)));

			var comparerType = typeof(ModManager).GetNestedType("ModReferenceEqualityComparer", BindingFlags.NonPublic);
			Assert.That(comparerType, Is.Not.Null);
			var instanceField = comparerType.GetField("Instance", BindingFlags.Static | BindingFlags.Public);
			Assert.That(instanceField, Is.Not.Null);
			return new HashSet<IMod>((IEqualityComparer<IMod>)instanceField.GetValue(null));
		}

		/// <summary>
		/// Creates an IMod proxy that counts manager PropertyChanged subscription operations.
		/// </summary>
		private static IMod CreateIdentityTrackingMod(Action onAdd, Action onRemove)
		{
			return InterfaceStub<IMod>.Create((method, args) =>
			{
				if (method.Name == "add_PropertyChanged")
				{
					onAdd();
				}
				else if (method.Name == "remove_PropertyChanged")
				{
					onRemove();
				}
				return null;
			});
		}

		/// <summary>
		/// Invokes the manager's bulk identity-subscription rebuild used by startup and registry Reset handling.
		/// </summary>
		private static void InvokeRebuildSortOrderManagedIdentityTracking(ModManager manager, IEnumerable<IMod> mods)
		{
			var method = typeof(ModManager).GetMethod("RebuildSortOrderManagedIdentityTracking", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(method, Is.Not.Null);
			method.Invoke(manager, new object[] { mods });
		}

		/// <summary>
		/// Invokes the manager's linear bulk unsubscribe path used during rebuild and release.
		/// </summary>
		private static void InvokeClearSortOrderManagedIdentityTracking(ModManager manager)
		{
			var method = typeof(ModManager).GetMethod("ClearSortOrderManagedIdentityTracking", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(method, Is.Not.Null);
			method.Invoke(manager, null);
		}

		/// <summary>
		/// Creates a lightweight install-log proxy exposing the supplied observable ActiveMods collection.
		/// </summary>
		private static IInstallLog CreateInstallLog(ThreadSafeObservableList<IMod> activeMods)
		{
			var readOnlyActiveMods = new ReadOnlyObservableList<IMod>(activeMods);
			return InterfaceStub<IInstallLog>.Create((method, args) => method.Name == "get_ActiveMods" ? readOnlyActiveMods : null);
		}

		/// <summary>
		/// Subscribes the manager's private ActiveMods handler to reproduce the initial manager state before replacement.
		/// </summary>
		private static void SubscribeActiveModsHandler(ModManager manager, IInstallLog installLog)
		{
			var method = typeof(ModManager).GetMethod("ActiveMods_SortOrderCollectionChanged", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(method, Is.Not.Null);
			var handler = (NotifyCollectionChangedEventHandler)Delegate.CreateDelegate(typeof(NotifyCollectionChangedEventHandler), manager, method);
			installLog.ActiveMods.CollectionChanged += handler;
		}

		/// <summary>
		/// Invokes the manager's install-log Sort rebinding helper without constructing unrelated UI/deployment dependencies.
		/// </summary>
		private static void InvokeRebindSortOrderActiveModsTracking(ModManager manager, IInstallLog previousInstallLog)
		{
			var method = typeof(ModManager).GetMethod("RebindSortOrderActiveModsTracking", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(method, Is.Not.Null);
			method.Invoke(manager, new object[] { previousInstallLog });
		}

		/// <summary>
		/// Reads the service's live current-archive inventory size for event-subscription assertions.
		/// </summary>
		private static int GetCurrentArchiveCount(ModSortOrderService service)
		{
			var field = typeof(ModSortOrderService).GetField("_currentArchivesByLocator", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(field, Is.Not.Null);
			return ((IDictionary)field.GetValue(service)).Count;
		}

		/// <summary>
		/// Assigns a private auto-property backing field on an uninitialized manager test shell.
		/// </summary>
		private static void SetPrivateField(object target, string fieldName, object value)
		{
			var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(field, Is.Not.Null, "Missing field: " + fieldName);
			field.SetValue(target, value);
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
