namespace Nexus.Client.ModManagement
{
	using System;
	using System.Collections.Generic;

	using Nexus.Client.ModManagement.InstallationLog;
	using Nexus.Client.Mods;

	/// <summary>
	/// Coordinates method-neutral deployment ownership queries while preserving VMA as the authority for pure Virtual targets.
	/// </summary>
	public sealed class ModDeploymentManager : IModDeploymentManager
	{
		private readonly IInstallLog m_ilgInstallLog;
		private readonly IVirtualModActivator m_vmaVirtualModActivator;

		/// <summary>
		/// Initializes the deployment coordinator.
		/// </summary>
		public ModDeploymentManager(IInstallLog p_ilgInstallLog, IVirtualModActivator p_vmaVirtualModActivator)
		{
			if (p_ilgInstallLog == null)
				throw new ArgumentNullException(nameof(p_ilgInstallLog));
			if (p_vmaVirtualModActivator == null)
				throw new ArgumentNullException(nameof(p_vmaVirtualModActivator));

			m_ilgInstallLog = p_ilgInstallLog;
			m_vmaVirtualModActivator = p_vmaVirtualModActivator;
		}

		/// <inheritdoc />
		public bool IsPromoted(ModDeploymentTarget p_mdtTarget)
		{
			if (p_mdtTarget == null)
				throw new ArgumentNullException(nameof(p_mdtTarget));

			return m_ilgInstallLog.IsDeploymentTargetPromoted(p_mdtTarget);
		}

		/// <inheritdoc />
		public IReadOnlyList<string> GetOwnerKeys(ModDeploymentTarget p_mdtTarget)
		{
			if (p_mdtTarget == null)
				throw new ArgumentNullException(nameof(p_mdtTarget));

			return m_ilgInstallLog.IsDeploymentTargetPromoted(p_mdtTarget)
				? m_ilgInstallLog.GetDeploymentOwnerKeys(p_mdtTarget)
				: m_vmaVirtualModActivator.GetVirtualOwnerKeys(p_mdtTarget);
		}

		/// <inheritdoc />
		public string GetCurrentOwnerKey(ModDeploymentTarget p_mdtTarget)
		{
			IReadOnlyList<string> owners = GetOwnerKeys(p_mdtTarget);
			return owners.Count == 0 ? null : owners[owners.Count - 1];
		}

		/// <inheritdoc />
		public bool HasManagedFiles(IMod p_modMod)
		{
			if (p_modMod == null)
				return false;

			string modKey = m_ilgInstallLog.GetModKey(p_modMod);
			if (!string.IsNullOrWhiteSpace(modKey) && m_ilgInstallLog.GetDeploymentTargetsForMod(modKey).Count > 0)
				return true;

			return m_vmaVirtualModActivator.CheckHasActiveLinks(p_modMod);
		}
	}
}
