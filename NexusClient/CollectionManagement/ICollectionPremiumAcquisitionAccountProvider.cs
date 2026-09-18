namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Supplies a point-in-time account snapshot for automated Premium Collection acquisition.
	/// </summary>
	public interface ICollectionPremiumAcquisitionAccountProvider
	{
		/// <summary>
		/// Captures the current repository game/account state without resolving any download URL.
		/// </summary>
		CollectionPremiumAcquisitionAccountState Capture();
	}
}
