namespace Nexus.Client.ModRepositories
{
	using System;

	/// <summary>
	/// Describes one mod reported as updated by a repository together with provider freshness timestamps.
	/// </summary>
	public sealed class RepositoryModUpdate
	{
		/// <summary>
		/// Creates one repository updated-mod record.
		/// </summary>
		/// <param name="modId">The repository mod ID.</param>
		/// <param name="latestFileUpdateUtc">The provider timestamp of the latest file update.</param>
		/// <param name="latestModActivityUtc">The provider timestamp of the latest mod activity.</param>
		public RepositoryModUpdate(string modId, DateTimeOffset latestFileUpdateUtc, DateTimeOffset latestModActivityUtc)
		{
			ModId = modId;
			LatestFileUpdateUtc = latestFileUpdateUtc.ToUniversalTime();
			LatestModActivityUtc = latestModActivityUtc.ToUniversalTime();
		}

		/// <summary>
		/// Gets the repository mod ID.
		/// </summary>
		public string ModId { get; }

		/// <summary>
		/// Gets the provider timestamp of the latest file update in UTC.
		/// </summary>
		public DateTimeOffset LatestFileUpdateUtc { get; }

		/// <summary>
		/// Gets the provider timestamp of the latest mod activity in UTC.
		/// </summary>
		public DateTimeOffset LatestModActivityUtc { get; }
	}
}
