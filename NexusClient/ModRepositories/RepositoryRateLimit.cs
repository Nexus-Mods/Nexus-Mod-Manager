namespace Nexus.Client.ModRepositories
{
	using System;

	/// <summary>
	/// Describes the current request limits reported by a mod repository.
	/// </summary>
	public sealed class RepositoryRateLimit
	{
		/// <summary>
		/// Creates a repository rate-limit snapshot.
		/// </summary>
		public RepositoryRateLimit(int dailyLimit, int dailyRemaining, DateTimeOffset dailyReset, int hourlyLimit, int hourlyRemaining, DateTimeOffset hourlyReset)
		{
			DailyLimit = dailyLimit;
			DailyRemaining = dailyRemaining;
			DailyReset = dailyReset;
			HourlyLimit = hourlyLimit;
			HourlyRemaining = hourlyRemaining;
			HourlyReset = hourlyReset;
		}

		/// <summary>
		/// Gets the daily request limit.
		/// </summary>
		public int DailyLimit { get; }

		/// <summary>
		/// Gets the remaining daily requests.
		/// </summary>
		public int DailyRemaining { get; }

		/// <summary>
		/// Gets when the daily request limit resets.
		/// </summary>
		public DateTimeOffset DailyReset { get; }

		/// <summary>
		/// Gets the hourly request limit.
		/// </summary>
		public int HourlyLimit { get; }

		/// <summary>
		/// Gets the remaining hourly requests.
		/// </summary>
		public int HourlyRemaining { get; }

		/// <summary>
		/// Gets when the hourly request limit resets.
		/// </summary>
		public DateTimeOffset HourlyReset { get; }
	}
}
