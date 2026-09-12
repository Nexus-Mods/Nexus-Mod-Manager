using System.Collections.Generic;
using System.IO;
using ChinhDo.Transactions;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Provides method-neutral deployment ownership queries without changing the existing Virtual deployment path.
	/// </summary>
	public interface IModDeploymentManager
	{
		/// <summary>
		/// Gets whether any target is currently managed by the sparse method-neutral deployment registry.
		/// </summary>
		bool HasPromotedTargets { get; }

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
		/// Gets the physical game path represented by the specified deployment target.
		/// </summary>
		string GetDeploymentPath(ModDeploymentTarget p_mdtTarget);

		/// <summary>
		/// Streams a standalone Direct payload to its final game destination and records its ownership.
		/// </summary>
		string InstallDirectFile(IMod p_modMod, ModDeploymentTarget p_mdtTarget, FileStream p_fstPayload, TxFileManager p_tfmFileManager);

		/// <summary>
		/// Registers a staged Virtual payload in a promoted stack and optionally makes it the physical winner.
		/// </summary>
		string InstallVirtualFile(IMod p_modMod, ModDeploymentTarget p_mdtTarget, string p_strLogicalPath,
			string p_strStagedSource, ModInstallRoot p_mirInstallRoot, bool p_booActivate, TxFileManager p_tfmFileManager);

		/// <summary>
		/// Removes every target owned by a Direct mod and restores the next managed or original owner.
		/// </summary>
		/// <returns>Physical paths left absent after ownership restoration.</returns>
		IReadOnlyCollection<string> UninstallDirectMod(IMod p_modMod, TxFileManager p_tfmFileManager);

		/// <summary>
		/// Removes the mod from method-neutral ownership stacks and restores each resulting physical winner.
		/// </summary>
		IReadOnlyCollection<string> UninstallMixedMod(IMod p_modMod, TxFileManager p_tfmFileManager);

		/// <summary>
		/// Gets whether the mod owns at least one promoted deployment target.
		/// </summary>
		bool HasPromotedFiles(IMod p_modMod);

		/// <summary>
		/// Gets whether the mod currently owns managed deployment state in either the sparse registry or VMA.
		/// </summary>
		bool HasManagedFiles(IMod p_modMod);
	}
}
