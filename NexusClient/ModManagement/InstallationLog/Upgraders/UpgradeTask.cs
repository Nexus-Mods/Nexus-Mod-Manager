using System.Diagnostics;
using ChinhDo.Transactions;
using Nexus.Transactions;

namespace Nexus.Client.ModManagement.InstallationLog.Upgraders
{
	/// <summary>
	/// Upgrades the Install Log from a specific version to the latest version.
	/// </summary>
	/// <remarks>
	/// This base class handles setting up the common resources and transaction required for all
	/// log upgrades.
	/// </remarks>
	public abstract class UpgradeTask : ThreadedBackgroundTask
	{
		private TxFileManager m_tfmFileManager = null;

		#region Properties

		/// <summary>
		/// Gets the transactional file manager to be used in the upgrade.
		/// </summary>
		protected TxFileManager FileManager
		{
			get
			{
				return m_tfmFileManager;
			}
		}

		#endregion

		/// <summary>
		/// Called to perform the upgrade.
		/// </summary>
		/// <remarks>
		/// Sets up the resources required to upgrade the install log.
		/// </remarks>
		/// <param name="p_mdrManagedModRegistry">The <see cref="ModRegistry"/> that contains the list
		/// of managed mods.</param>
		/// <param name="p_strModInstallDirectory">The path of the directory where all of the mods are installed.</param>
		/// <param name="p_strLogPath">The path from which to load the install log information.</param>
		public void Upgrade(string p_strLogPath, string p_strModInstallDirectory, ModRegistry p_mdrManagedModRegistry)
		{
			Trace.WriteLine("Beginning Install Log Upgrade.");

			m_tfmFileManager = new TxFileManager();
			using (TransactionScope tsTransaction = new TransactionScope())
			{
				m_tfmFileManager.Snapshot(p_strLogPath);
				Start(p_strLogPath, p_strModInstallDirectory, p_mdrManagedModRegistry);
				tsTransaction.Complete();
				m_tfmFileManager = null;
			}
		}
	}
}
