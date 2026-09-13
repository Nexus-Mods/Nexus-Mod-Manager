namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Exposes plugin-registration flushing separately from end-of-install file cleanup.
	/// </summary>
	public interface IModFilePluginRegistrationSupport
	{
		/// <summary>
		/// Makes pending plugin registrations visible without finalizing the file installation.
		/// </summary>
		void FlushPendingPluginRegistrations();
	}
}
