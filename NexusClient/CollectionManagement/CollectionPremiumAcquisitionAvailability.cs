using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Describes whether one Collection artifact may use the automated Nexus Premium acquisition path.
	/// </summary>
	public enum CollectionPremiumAcquisitionAvailability
	{
		/// <summary>The request can be queued through the existing authorized Nexus AddMod path.</summary>
		Available = 0,

		/// <summary>The selected artifact is not an exact Nexus mod-file identity supported by this path.</summary>
		UnsupportedArtifact = 1,

		/// <summary>No authenticated online Nexus account is currently available.</summary>
		NotAuthenticated = 2,

		/// <summary>The current Nexus account does not have Premium automated-download privileges.</summary>
		PremiumRequired = 3,

		/// <summary>The active repository is for a different Nexus game domain than the requested artifact.</summary>
		GameDomainMismatch = 4
	}
}
