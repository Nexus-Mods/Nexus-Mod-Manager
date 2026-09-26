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
		/// Gets the currently promoted deployment targets without scanning the complete Virtual link set.
		/// </summary>
		IReadOnlyCollection<ModDeploymentTarget> GetPromotedTargets();

		/// <summary>
		/// Gets the installed mod represented by a real deployment owner key.
		/// </summary>
		IMod GetOwnerMod(string p_strOwnerKey);

		/// <summary>
		/// Gets the canonical payload path used to preview an owner of a deployment target.
		/// </summary>
		string GetOwnerSourcePath(ModDeploymentTarget p_mdtTarget, string p_strOwnerKey);

		/// <summary>
		/// Gets the root/path/owner-aware overwrite backup path for a promoted owner.
		/// </summary>
		string GetOwnerBackupPath(ModDeploymentTarget p_mdtTarget, string p_strOwnerKey);

		/// <summary>
		/// Makes an existing real mod owner the physical winner of a promoted deployment target.
		/// </summary>
		void SwitchPromotedOwner(ModDeploymentTarget p_mdtTarget, string p_strSelectedOwnerKey);

		/// <summary>
		/// Restores the exact persisted owner order for a promoted target while keeping the requested winner physical.
		/// </summary>
		void RestorePromotedOwnerStack(ModDeploymentTarget p_mdtTarget, IReadOnlyList<string> p_lstOwnerKeys);

		/// <summary>
		/// Reconstructs one exact captured deployment owner stack from already-remapped managed owners and retained payload bytes.
		/// </summary>
		void RestoreCapturedOwnerStack(ModDeploymentTarget p_mdtTarget, bool p_booPromoted,
			IReadOnlyList<ModDeploymentRestoreOwner> p_lstOwners);

		/// <summary>
		/// Streams a standalone Direct payload to its final game destination and records its ownership.
		/// </summary>
		string InstallDirectFile(IMod p_modMod, ModDeploymentTarget p_mdtTarget, FileStream p_fstPayload, TxFileManager p_tfmFileManager);

		/// <summary>
		/// Writes a generated Direct payload to its final game destination and records its ownership.
		/// </summary>
		string InstallDirectFile(IMod p_modMod, ModDeploymentTarget p_mdtTarget, byte[] p_btePayload, TxFileManager p_tfmFileManager);

		/// <summary>
		/// Replaces a Direct owner's payload during an upgrade without changing its position in an existing owner stack.
		/// </summary>
		string UpgradeDirectFile(IMod p_modMod, ModDeploymentTarget p_mdtTarget, FileStream p_fstPayload, TxFileManager p_tfmFileManager);

		/// <summary>
		/// Replaces generated Direct payload bytes during an upgrade without changing the owner's stack position.
		/// </summary>
		string UpgradeDirectFile(IMod p_modMod, ModDeploymentTarget p_mdtTarget, byte[] p_btePayload, TxFileManager p_tfmFileManager);

		/// <summary>
		/// Removes one owned deployment target while preserving/restoring the remaining owner stack.
		/// </summary>
		/// <returns>The physical path when the target is left absent; otherwise <c>null</c>.</returns>
		string RemoveOwnedTarget(IMod p_modMod, ModDeploymentTarget p_mdtTarget, TxFileManager p_tfmFileManager);

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
