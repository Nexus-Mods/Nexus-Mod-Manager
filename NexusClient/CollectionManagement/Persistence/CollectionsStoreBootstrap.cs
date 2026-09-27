using System;
using System.IO;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>
	/// Centralizes application-level opening/creation policy for the Collections feature store.
	/// </summary>
	/// <remarks>
	/// Store construction itself remains side-effect free. Explicit feature work may create a genuinely missing store, while
	/// read/recovery probes can distinguish a never-created store from an existing store that must validate successfully.
	/// Existing incompatible, corrupt, busy or read-only stores are never replaced.
	/// </remarks>
	public static class CollectionsStoreBootstrap
	{
		/// <summary>
		/// Opens the current store, creating it only when no database exists yet.
		/// </summary>
		public static CollectionsStoreStatus OpenOrCreateForFeatureUse(CollectionsStore store)
		{
			if (store == null)
				throw new ArgumentNullException(nameof(store));

			if (!store.Exists)
			{
				try
				{
					store.CreateNew();
				}
				catch (IOException)
				{
					// Another process may have won the create race. Only continue when a store now exists;
					// OpenExisting below remains the authority for schema/access validation.
					if (!store.Exists)
						throw;
				}
			}

			return store.OpenExisting();
		}

		/// <summary>
		/// Opens and validates an existing store, or returns <c>null</c> when the feature has never created one.
		/// </summary>
		public static CollectionsStoreStatus OpenExistingIfPresent(CollectionsStore store)
		{
			if (store == null)
				throw new ArgumentNullException(nameof(store));
			return store.Exists ? store.OpenExisting() : null;
		}
	}
}
