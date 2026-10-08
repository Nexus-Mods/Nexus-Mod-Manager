using System;
using System.Data.SQLite;
using Nexus.Client.OnlineServices.NexusMods.Collections;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>Public Nexus lookup locator retained separately from stable Collection identity.</summary>
	/// <remarks>
	/// The numeric provider Collection identity remains authoritative. The slug is presentation/navigation metadata only,
	/// but retaining it lets installed/restarted Collection views keep showing the human-facing Nexus locator.
	/// </remarks>
	public sealed class CollectionNexusLocatorRecord
	{
		internal CollectionNexusLocatorRecord(CollectionIdentity collection, string gameDomain, string collectionSlug)
		{
			Collection = collection ?? throw new ArgumentNullException(nameof(collection));
			if (Collection.Origin != CollectionOrigin.NexusMods)
				throw new ArgumentException("A Nexus locator can only belong to a Nexus Collection.", nameof(collection));
			if (String.IsNullOrWhiteSpace(gameDomain) || !StringComparer.Ordinal.Equals(gameDomain, gameDomain.Trim()))
				throw new ArgumentException("A Nexus game domain is required without surrounding whitespace.", nameof(gameDomain));
			GameDomain = gameDomain.ToLowerInvariant();
			CollectionSlug = NexusCollectionRevisionRequest.RequireSlug(collectionSlug);
		}

		public CollectionIdentity Collection { get; }
		public string GameDomain { get; }
		public string CollectionSlug { get; }
	}

	/// <summary>Stores non-authoritative Nexus slug/domain metadata in the existing feature metadata table.</summary>
	public sealed class CollectionsNexusLocatorStore
	{
		private const string KeyPrefix = "nexus-locator-v1:";
		private readonly CollectionsStore _store;

		public CollectionsNexusLocatorStore(CollectionsStore store)
		{
			_store = store ?? throw new ArgumentNullException(nameof(store));
		}

		/// <summary>Records the latest known public locator for one stable Nexus Collection identity.</summary>
		public void Save(CollectionIdentity collection, string gameDomain, string collectionSlug)
		{
			var record = new CollectionNexusLocatorRecord(collection, gameDomain, collectionSlug);
			string value = record.GameDomain + "\n" + record.CollectionSlug;
			_store.ExecuteWrite((connection, transaction) =>
			{
				using (var command = new SQLiteCommand(@"
INSERT OR REPLACE INTO store_metadata (key, value) VALUES (@key, @value);", connection, transaction))
				{
					command.Parameters.AddWithValue("@key", GetKey(collection));
					command.Parameters.AddWithValue("@value", value);
					command.ExecuteNonQuery();
				}
			});
		}

		/// <summary>Gets the last retained public locator, or null when older data never retained one.</summary>
		public CollectionNexusLocatorRecord Get(CollectionIdentity collection)
		{
			if (collection == null) throw new ArgumentNullException(nameof(collection));
			if (collection.Origin != CollectionOrigin.NexusMods || !_store.Exists) return null;
			string value = _store.ExecuteRead((connection, transaction) =>
			{
				using (var command = new SQLiteCommand("SELECT value FROM store_metadata WHERE key=@key;", connection, transaction))
				{
					command.Parameters.AddWithValue("@key", GetKey(collection));
					object raw = command.ExecuteScalar();
					return raw == null || raw == DBNull.Value ? null : Convert.ToString(raw);
				}
			});
			if (String.IsNullOrEmpty(value)) return null;
			int separator = value.IndexOf('\n');
			if (separator <= 0 || separator == value.Length - 1) return null;
			try
			{
				return new CollectionNexusLocatorRecord(collection, value.Substring(0, separator), value.Substring(separator + 1));
			}
			catch (ArgumentException)
			{
				return null;
			}
		}

		private static string GetKey(CollectionIdentity collection)
		{
			return KeyPrefix + collection.StableId;
		}
	}
}
