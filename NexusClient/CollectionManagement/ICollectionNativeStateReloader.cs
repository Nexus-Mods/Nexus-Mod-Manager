using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Reloads authoritative native NMM state after cross-process target access has been acquired.
	/// </summary>
	public interface ICollectionNativeStateReloader
	{
		/// <summary>
		/// Reloads the specified InstallLog and all native services that derive ownership/deployment state from it.
		/// </summary>
		void Reload(string installLogPath);
	}

	/// <summary>
	/// Rebinds the production <see cref="ServiceManager"/> to a freshly loaded InstallLog and native deployment state.
	/// </summary>
	public sealed class CollectionServiceNativeStateReloader : ICollectionNativeStateReloader
	{
		private readonly ServiceManager _services;

		/// <summary>
		/// Creates a native-state reloader over the current NMM service graph.
		/// </summary>
		public CollectionServiceNativeStateReloader(ServiceManager services)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
		}

		/// <inheritdoc />
		public void Reload(string installLogPath)
		{
			_services.ReinitializeInstallLog(installLogPath);
		}
	}
}
