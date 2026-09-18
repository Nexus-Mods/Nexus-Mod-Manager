using System;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>
	/// Effective schema and connection settings observed while opening a Collections feature store.
	/// </summary>
	public sealed class CollectionsStoreStatus
	{
		internal CollectionsStoreStatus(Guid storeId, int schemaVersion, string journalMode,
			int synchronousMode, bool foreignKeysEnabled, int busyTimeoutMilliseconds)
		{
			StoreId = storeId;
			SchemaVersion = schemaVersion;
			JournalMode = journalMode;
			SynchronousMode = synchronousMode;
			ForeignKeysEnabled = foreignKeysEnabled;
			BusyTimeoutMilliseconds = busyTimeoutMilliseconds;
		}

		public Guid StoreId { get; }
		public int SchemaVersion { get; }
		public string JournalMode { get; }
		public int SynchronousMode { get; }
		public bool ForeignKeysEnabled { get; }
		public int BusyTimeoutMilliseconds { get; }

		/// <summary>
		/// Gets whether the effective writable connection matches the C4 durability baseline.
		/// </summary>
		public bool HasRequiredDurabilitySettings
		{
			get
			{
				return string.Equals(JournalMode, "delete", StringComparison.OrdinalIgnoreCase) &&
					SynchronousMode == 2 &&
					ForeignKeysEnabled &&
					BusyTimeoutMilliseconds == CollectionsStore.BusyTimeoutMilliseconds;
			}
		}
	}
}
