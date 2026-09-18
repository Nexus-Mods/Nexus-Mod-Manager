using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Describes safe user-mediated ways to continue one unresolved Collection acquisition request.
	/// </summary>
	/// <remarks>
	/// The pending action contains only stable request/provider identity. It never retains a signed NXM URI, CDN URL,
	/// selected local path or other temporary authorization/input. Durable restart reconstruction belongs to C4.22.
	/// </remarks>
	public sealed class CollectionManualAcquisitionPendingAction
	{
		internal CollectionManualAcquisitionPendingAction(
			CollectionAcquisitionRequest request,
			CollectionManualAcquisitionActionKind allowedActions,
			Uri browserUri)
		{
			Request = request ?? throw new ArgumentNullException(nameof(request));
			const CollectionManualAcquisitionActionKind knownActions =
				CollectionManualAcquisitionActionKind.Browser |
				CollectionManualAcquisitionActionKind.Nxm |
				CollectionManualAcquisitionActionKind.LocalFile;
			if (allowedActions == CollectionManualAcquisitionActionKind.None ||
				(((int)allowedActions) & ~((int)knownActions)) != 0)
				throw new ArgumentOutOfRangeException(nameof(allowedActions));
			if ((allowedActions & CollectionManualAcquisitionActionKind.Browser) != 0)
			{
				if (browserUri == null || !browserUri.IsAbsoluteUri ||
					(!StringComparer.OrdinalIgnoreCase.Equals(browserUri.Scheme, Uri.UriSchemeHttp) &&
					 !StringComparer.OrdinalIgnoreCase.Equals(browserUri.Scheme, Uri.UriSchemeHttps)))
					throw new ArgumentException("A stable HTTP(S) browser URI is required when browser mediation is enabled.", nameof(browserUri));
			}
			else if (browserUri != null)
			{
				throw new ArgumentException("A browser URI cannot be retained when browser mediation is not enabled.", nameof(browserUri));
			}
			AllowedActions = allowedActions;
			BrowserUri = browserUri;
		}

		/// <summary>Gets the immutable acquisition request waiting for user input.</summary>
		public CollectionAcquisitionRequest Request { get; }

		/// <summary>Gets the stable pending-action identity. It is the acquisition request identity, not a download URL.</summary>
		public Guid ActionId => Request.RequestId;

		/// <summary>Gets the user-mediated actions which may safely continue this request.</summary>
		public CollectionManualAcquisitionActionKind AllowedActions { get; }

		/// <summary>Gets the stable public provider page to open, when browser mediation is supported.</summary>
		public Uri BrowserUri { get; }

		/// <summary>Returns whether the specified manual action is available for this pending request.</summary>
		public bool Supports(CollectionManualAcquisitionActionKind action)
		{
			int actionValue = (int)action;
			if (!Enum.IsDefined(typeof(CollectionManualAcquisitionActionKind), action) ||
				action == CollectionManualAcquisitionActionKind.None || (actionValue & (actionValue - 1)) != 0)
				throw new ArgumentOutOfRangeException(nameof(action));
			return (AllowedActions & action) == action;
		}
	}
}
