using System.Collections.Generic;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Provides method-neutral deployment ownership queries without changing the existing Virtual deployment path.
	/// </summary>
	public interface IModDeploymentManager
	{
		/// <summary>
		/// Gets whether the specified target is managed by the sparse method-neutral deployment registry.
		/// </summary>
		bool IsPromoted(ModDeploymentTarget p_mdtTarget);

		/// <summary>
		/// Gets the effective owner sequence ordered from restoration fallback to current physical winner.
		/// </summary>
		IReadOnlyList<string> GetOwnerKeys(ModDeploymentTarget p_mdtTarget);

		/// <summary>
		/// Gets the current managed owner key for the target, or <c>null</c> when the target has no managed owner.
		/// </summary>
		string GetCurrentOwnerKey(ModDeploymentTarget p_mdtTarget);

		/// <summary>
		/// Gets whether the mod currently owns managed deployment state in either the sparse registry or VMA.
		/// </summary>
		bool HasManagedFiles(IMod p_modMod);
	}
}
