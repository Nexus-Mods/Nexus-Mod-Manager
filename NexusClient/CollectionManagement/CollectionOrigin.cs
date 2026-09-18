namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Identifies where a collection definition is owned.
	/// </summary>
	public enum CollectionOrigin
	{
		/// <summary>
		/// No trusted origin has been established. Persisted identities must not use this value.
		/// </summary>
		Unknown = 0,

		/// <summary>
		/// The collection belongs to a remote Nexus Mods collection lineage.
		/// </summary>
		NexusMods = 1,

		/// <summary>
		/// The collection is owned locally by NMM.
		/// </summary>
		Local = 2
	}
}
