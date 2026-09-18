using System;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>
	/// Describes whether an existing Collections feature store can be used safely by this NMM version.
	/// </summary>
	public enum CollectionsStoreAvailability
	{
		Ready = 1,
		Missing = 2,
		MigrationRequired = 3,
		UnsupportedOlderSchema = 4,
		UnsupportedNewerSchema = 5,
		InvalidOrCorrupt = 6,
		ReadOnly = 7,
		Busy = 8,
		Unavailable = 9
	}

	/// <summary>
	/// Non-destructive diagnostic result for an existing Collections feature store.
	/// </summary>
	public sealed class CollectionsStoreInspection
	{
		internal CollectionsStoreInspection(CollectionsStoreAvailability availability, Guid? storeId,
			int? schemaVersion, string detail)
		{
			Availability = availability;
			StoreId = storeId;
			SchemaVersion = schemaVersion;
			Detail = detail;
		}

		public CollectionsStoreAvailability Availability { get; }
		public Guid? StoreId { get; }
		public int? SchemaVersion { get; }
		public string Detail { get; }

		/// <summary>
		/// Gets whether current-schema feature state can be read without inventing a replacement authority.
		/// </summary>
		public bool CanReadFeatureState
		{
			get
			{
				return (Availability == CollectionsStoreAvailability.Ready || Availability == CollectionsStoreAvailability.ReadOnly) &&
					StoreId.HasValue && SchemaVersion == CollectionsStore.CurrentSchemaVersion;
			}
		}

		/// <summary>
		/// Gets whether the store is currently safe for new Collections feature writes.
		/// </summary>
		public bool CanWriteFeatureState
		{
			get { return Availability == CollectionsStoreAvailability.Ready && CanReadFeatureState; }
		}
	}
}
