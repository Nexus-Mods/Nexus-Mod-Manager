using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Immutable snapshot of the repository account state relevant to Premium Collection acquisition.
	/// </summary>
	public sealed class CollectionPremiumAcquisitionAccountState
	{
		/// <summary>
		/// Creates one account-state snapshot.
		/// </summary>
		public CollectionPremiumAcquisitionAccountState(string gameDomainName, bool isAuthenticated, bool isPremium)
		{
			GameDomainName = String.IsNullOrWhiteSpace(gameDomainName) ? null : gameDomainName.Trim().ToLowerInvariant();
			IsAuthenticated = isAuthenticated;
			IsPremium = isAuthenticated && isPremium;
		}

		/// <summary>Gets the Nexus game domain owned by the active repository.</summary>
		public string GameDomainName { get; }

		/// <summary>Gets whether an authenticated online Nexus account is currently available.</summary>
		public bool IsAuthenticated { get; }

		/// <summary>Gets whether the authenticated account currently has Premium download privileges.</summary>
		public bool IsPremium { get; }
	}
}
