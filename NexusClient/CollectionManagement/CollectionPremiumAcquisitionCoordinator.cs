using System;
using System.Globalization;
using Nexus.Client.ModAuthoring;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Routes exact Nexus mod-file Collection requests through NMM's existing authenticated Premium AddMod flow.
	/// </summary>
	/// <remarks>
	/// This coordinator never requests or stores CDN/signed URLs. It emits an unsigned exact-file NXM identity only after
	/// confirming the current repository account is Premium; the existing AddMod worker remains the sole owner of authorized
	/// download-link resolution and downloading. Free/manual acquisition remains C4.20.
	/// </remarks>
	public sealed class CollectionPremiumAcquisitionCoordinator
	{
		private readonly CollectionAcquisitionRequestCoordinator _requestCoordinator;
		private readonly ICollectionPremiumAcquisitionAccountProvider _accountProvider;

		/// <summary>
		/// Creates a Premium acquisition coordinator over the existing C4.17 request queue and account-state provider.
		/// </summary>
		public CollectionPremiumAcquisitionCoordinator(
			CollectionAcquisitionRequestCoordinator requestCoordinator,
			ICollectionPremiumAcquisitionAccountProvider accountProvider)
		{
			_requestCoordinator = requestCoordinator ?? throw new ArgumentNullException(nameof(requestCoordinator));
			_accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
		}

		/// <summary>
		/// Reports whether the request can currently enter automated Premium acquisition without performing network work.
		/// </summary>
		public CollectionPremiumAcquisitionAvailability GetAvailability(CollectionAcquisitionRequest request)
		{
			if (request == null)
				throw new ArgumentNullException(nameof(request));

			string gameDomain;
			long modId;
			long fileId;
			if (!TryGetNexusIdentity(request, out gameDomain, out modId, out fileId))
				return CollectionPremiumAcquisitionAvailability.UnsupportedArtifact;

			CollectionPremiumAcquisitionAccountState account = _accountProvider.Capture();
			if (account == null || !account.IsAuthenticated)
				return CollectionPremiumAcquisitionAvailability.NotAuthenticated;
			if (!account.IsPremium)
				return CollectionPremiumAcquisitionAvailability.PremiumRequired;
			if (String.IsNullOrWhiteSpace(account.GameDomainName) ||
				!StringComparer.OrdinalIgnoreCase.Equals(account.GameDomainName, gameDomain))
				return CollectionPremiumAcquisitionAvailability.GameDomainMismatch;

			return CollectionPremiumAcquisitionAvailability.Available;
		}

		/// <summary>
		/// Queues one exact Nexus mod file through the existing AddMod path when the current account has Premium privileges.
		/// </summary>
		public CollectionAcquisitionQueueCorrelation Queue(
			CollectionAcquisitionRequest request,
			ConfirmOverwriteCallback confirmOverwriteCallback)
		{
			if (request == null)
				throw new ArgumentNullException(nameof(request));

			string gameDomain;
			long modId;
			long fileId;
			if (!TryGetNexusIdentity(request, out gameDomain, out modId, out fileId))
				throw new InvalidOperationException("The Collection acquisition request is not an exact Nexus mod-file artifact supported by Premium automation.");

			CollectionPremiumAcquisitionAccountState account = _accountProvider.Capture();
			CollectionPremiumAcquisitionAvailability availability = GetAvailability(account, gameDomain);
			if (availability != CollectionPremiumAcquisitionAvailability.Available)
				throw new InvalidOperationException("Automated Premium Collection acquisition is unavailable: " + availability + ".");

			Uri sourceUri = CreateUnsignedNxmUri(gameDomain, modId, fileId);
			return _requestCoordinator.Queue(request, sourceUri, confirmOverwriteCallback);
		}

		private static CollectionPremiumAcquisitionAvailability GetAvailability(
			CollectionPremiumAcquisitionAccountState account,
			string gameDomain)
		{
			if (account == null || !account.IsAuthenticated)
				return CollectionPremiumAcquisitionAvailability.NotAuthenticated;
			if (!account.IsPremium)
				return CollectionPremiumAcquisitionAvailability.PremiumRequired;
			if (String.IsNullOrWhiteSpace(account.GameDomainName) ||
				!StringComparer.OrdinalIgnoreCase.Equals(account.GameDomainName, gameDomain))
				return CollectionPremiumAcquisitionAvailability.GameDomainMismatch;
			return CollectionPremiumAcquisitionAvailability.Available;
		}

		private static bool TryGetNexusIdentity(CollectionAcquisitionRequest request, out string gameDomain, out long modId, out long fileId)
		{
			if (!NexusCollectionModFileArtifactIdentity.TryParse(request.SelectedArtifact, out gameDomain, out modId, out fileId))
				return false;
			return Uri.CheckHostName(gameDomain) != UriHostNameType.Unknown;
		}

		private static Uri CreateUnsignedNxmUri(string gameDomain, long modId, long fileId)
		{
			string value = String.Format(CultureInfo.InvariantCulture,
				"nxm://{0}/mods/{1}/files/{2}", gameDomain, modId, fileId);
			return new Uri(value, UriKind.Absolute);
		}
	}
}
