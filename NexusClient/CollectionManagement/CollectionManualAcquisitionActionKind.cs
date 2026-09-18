using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Identifies user-mediated actions that may satisfy one pending Collection acquisition request.
	/// </summary>
	[Flags]
	public enum CollectionManualAcquisitionActionKind
	{
		/// <summary>No safe manual action is available.</summary>
		None = 0,

		/// <summary>Open a stable provider page in the user's browser.</summary>
		Browser = 1,

		/// <summary>Accept a provider-returned NXM callback carrying temporary user-mediated authorization.</summary>
		Nxm = 2,

		/// <summary>Accept a user-selected local archive candidate and verify its bytes.</summary>
		LocalFile = 4
	}
}
