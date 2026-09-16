namespace Nexus.Client.ModRepositories
{
	/// <summary>
	/// Describes the authenticated user information exposed by a mod repository.
	/// </summary>
	public sealed class RepositoryUserStatus
	{
		/// <summary>
		/// Creates repository user status information.
		/// </summary>
		/// <param name="name">The display name of the authenticated user.</param>
		/// <param name="isPremium">Whether the user has premium download privileges.</param>
		/// <param name="isSupporter">Whether the user has supporter status.</param>
		public RepositoryUserStatus(string name, bool isPremium, bool isSupporter)
		{
			Name = name;
			IsPremium = isPremium;
			IsSupporter = isSupporter;
		}

		/// <summary>
		/// Gets the display name of the authenticated user.
		/// </summary>
		public string Name { get; }

		/// <summary>
		/// Gets whether the user has premium download privileges.
		/// </summary>
		public bool IsPremium { get; }

		/// <summary>
		/// Gets whether the user has supporter status.
		/// </summary>
		public bool IsSupporter { get; }
	}
}
