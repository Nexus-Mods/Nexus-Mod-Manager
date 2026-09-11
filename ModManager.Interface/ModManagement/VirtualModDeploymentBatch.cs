using System;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Groups virtual-mod metadata and link-list updates so a deployment can defer repeated maintenance work until the batch completes.
	/// </summary>
	public sealed class VirtualModDeploymentBatch : IDisposable
	{
		private IDisposable m_dspModInfoBatch;
		private IDisposable m_dspVirtualLinkBatch;
		private bool m_booDisposed;

		/// <summary>
		/// Begins a deployment batch for the specified virtual mod activator.
		/// </summary>
		/// <param name="p_ivaVirtualModActivator">The virtual mod activator whose updates are being grouped.</param>
		/// <param name="p_intExpectedAdditionalLinks">The expected number of virtual links that may be added during the batch.</param>
		public VirtualModDeploymentBatch(IVirtualModActivator p_ivaVirtualModActivator, int p_intExpectedAdditionalLinks)
		{
			if (p_ivaVirtualModActivator == null)
				throw new ArgumentNullException(nameof(p_ivaVirtualModActivator));

			m_dspModInfoBatch = p_ivaVirtualModActivator.BeginModInfoUpdateBatch();
			try
			{
				m_dspVirtualLinkBatch = p_ivaVirtualModActivator.BeginVirtualLinkUpdateBatch(Math.Max(0, p_intExpectedAdditionalLinks));
			}
			catch
			{
				m_dspModInfoBatch.Dispose();
				m_dspModInfoBatch = null;
				throw;
			}
		}

		/// <summary>
		/// Completes the deployment batch and flushes deferred virtual-link and mod-information updates.
		/// </summary>
		public void Dispose()
		{
			if (m_booDisposed)
				return;

			m_booDisposed = true;
			try
			{
				if (m_dspVirtualLinkBatch != null)
					m_dspVirtualLinkBatch.Dispose();
			}
			finally
			{
				m_dspVirtualLinkBatch = null;
				if (m_dspModInfoBatch != null)
					m_dspModInfoBatch.Dispose();
				m_dspModInfoBatch = null;
			}
		}
	}
}
