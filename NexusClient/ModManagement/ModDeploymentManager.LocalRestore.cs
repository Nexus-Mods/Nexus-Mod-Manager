namespace Nexus.Client.ModManagement
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;

	using ChinhDo.Transactions;
	using Nexus.Client.ModManagement.Scripting;
	using Nexus.Client.Mods;
	using Nexus.Transactions;

	/// <summary>Native exact-owner restoration primitives used by Local Collection recovery.</summary>
	public sealed partial class ModDeploymentManager
	{
		/// <inheritdoc />
		public void RestoreCapturedOwnerStack(ModDeploymentTarget p_mdtTarget, bool p_booPromoted,
			IReadOnlyList<ModDeploymentRestoreOwner> p_lstOwners)
		{
			ValidateCapturedRestoreStack(p_mdtTarget, p_booPromoted, p_lstOwners);

			using (var transaction = new TransactionScope())
			{
				var fileManager = new TxFileManager();
				if (m_ilgInstallLog.IsDeploymentTargetPromoted(p_mdtTarget) || p_booPromoted)
					TouchDeploymentRecoveryTarget(p_mdtTarget);
				ClearCurrentTargetOwnership(p_mdtTarget, fileManager);

				if (p_booPromoted)
					RestorePromotedCapturedStack(p_mdtTarget, p_lstOwners, fileManager);
				else
					RestorePureVirtualCapturedStack(p_mdtTarget, p_lstOwners, fileManager);

				transaction.Complete();
			}

			if (!p_booPromoted && !m_vmaVirtualModActivator.SaveList(false))
				throw new IOException("The restored pure-Virtual owner stack could not be persisted durably.");
			m_vmaVirtualModActivator.PublishPendingDeploymentChanges();
		}

		/// <summary>Rejects malformed or context-inconsistent exact-restore owner inputs before filesystem mutation.</summary>
		private void ValidateCapturedRestoreStack(ModDeploymentTarget target, bool promoted,
			IReadOnlyList<ModDeploymentRestoreOwner> owners)
		{
			if (target == null)
				throw new ArgumentNullException(nameof(target));
			if (m_gmdGameMode == null)
				throw new InvalidOperationException("Captured deployment restoration requires the current game mode.");
			if (owners == null || owners.Count == 0)
				throw new ArgumentException("A captured deployment restore requires at least one owner.", nameof(owners));
			if (owners.Any(x => x == null))
				throw new ArgumentException("A captured deployment restore cannot contain null owners.", nameof(owners));
			if (owners.Select(x => x.OwnerKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() != owners.Count)
				throw new InvalidDataException("A captured deployment restore cannot contain duplicate owner keys.");

			int originalIndex = -1;
			for (int index = 0; index < owners.Count; index++)
			{
				ModDeploymentRestoreOwner owner = owners[index];
				if (!File.Exists(owner.PayloadPath))
					throw new FileNotFoundException("A retained deployment restore payload is missing.", owner.PayloadPath);

				if (owner.Kind == ModDeploymentRestoreOwnerKind.OriginalValue)
				{
					if (!owner.OwnerKey.Equals(m_ilgInstallLog.OriginalValuesKey, StringComparison.OrdinalIgnoreCase))
						throw new InvalidDataException("The captured original-value owner was not remapped to the current InstallLog original key.");
					if (originalIndex >= 0)
						throw new InvalidDataException("A captured deployment restore contains more than one original-value owner.");
					originalIndex = index;
					continue;
				}

				string liveKey = m_ilgInstallLog.GetModKey(owner.Mod);
				if (!owner.OwnerKey.Equals(liveKey, StringComparison.OrdinalIgnoreCase))
					throw new InvalidDataException("A captured deployment owner no longer matches its live native mod key.");
				ModInstallMethod expectedMethod = owner.Kind == ModDeploymentRestoreOwnerKind.Direct
					? ModInstallMethod.Direct : ModInstallMethod.Virtual;
				if (m_ilgInstallLog.GetModInstallMethod(owner.Mod) != expectedMethod ||
					m_ilgInstallLog.GetModInstallRoot(owner.Mod) != owner.InstallRoot)
					throw new InvalidDataException("A captured deployment owner no longer matches its reviewed native install context.");
			}

			if (originalIndex > 0)
				throw new InvalidDataException("The unmanaged original owner must be the bottom fallback of a captured promoted stack.");
			if (!promoted && owners.Any(x => x.Kind != ModDeploymentRestoreOwnerKind.Virtual))
				throw new InvalidDataException("A non-promoted captured target may contain only Virtual managed owners.");
			if (promoted && owners.Count == 1 && owners[0].Kind == ModDeploymentRestoreOwnerKind.OriginalValue)
				throw new InvalidDataException("An original-only target is not a promoted managed deployment stack.");
		}

		/// <summary>Removes the current managed target through existing native owner-removal semantics before exact reconstruction.</summary>
		private void ClearCurrentTargetOwnership(ModDeploymentTarget target, TxFileManager fileManager)
		{
			var absent = new List<string>();
			if (m_ilgInstallLog.IsDeploymentTargetPromoted(target))
			{
				string[] owners = m_ilgInstallLog.GetDeploymentOwnerKeys(target).ToArray();
				for (int index = owners.Length - 1; index >= 0; index--)
				{
					string ownerKey = owners[index];
					if (ownerKey.Equals(m_ilgInstallLog.OriginalValuesKey, StringComparison.OrdinalIgnoreCase))
						continue;
					IMod mod = GetOwnerMod(ownerKey);
					if (mod == null)
						throw new InvalidOperationException("The current promoted deployment target contains an unresolved native owner.");
					RemovePromotedOwner(target, ownerKey, fileManager, absent);
				}
				if (m_ilgInstallLog.IsDeploymentTargetPromoted(target))
					m_ilgInstallLog.RemoveDeploymentTarget(target);
				return;
			}

			string[] virtualOwners = m_vmaVirtualModActivator.GetVirtualOwnerKeys(target).ToArray();
			for (int index = virtualOwners.Length - 1; index >= 0; index--)
			{
				IMod mod = GetOwnerMod(virtualOwners[index]);
				if (mod == null)
					throw new InvalidOperationException("The current Virtual deployment target contains an unresolved native owner.");
				RemovePureVirtualOwner(target, virtualOwners[index], fileManager, absent);
			}
		}

		/// <summary>Recreates exact promoted owner bytes, Virtual link records, owner order and physical winner.</summary>
		private void RestorePromotedCapturedStack(ModDeploymentTarget target,
			IReadOnlyList<ModDeploymentRestoreOwner> owners, TxFileManager fileManager)
		{
			string deploymentPath = GetDeploymentPath(target);
			if (File.Exists(deploymentPath))
				fileManager.Delete(deploymentPath);

			for (int index = 0; index < owners.Count; index++)
			{
				ModDeploymentRestoreOwner owner = owners[index];
				bool winner = index == owners.Count - 1;
				if (owner.Kind == ModDeploymentRestoreOwnerKind.Virtual)
				{
					string stagedPath = StageVirtualRestorePayload(target, owner, fileManager);
					m_vmaVirtualModActivator.RegisterVirtualLink(target, owner.Mod, target.RelativePath,
						stagedPath, owner.InstallRoot, Math.Max(0, owners.Count - index));
					continue;
				}

				string destination = winner ? deploymentPath : GetOwnerBackupPath(target, owner.OwnerKey);
				WriteRestorePayload(owner.PayloadPath, destination, fileManager);
			}

			string[] desiredKeys = owners.Select(x => x.OwnerKey).ToArray();
			m_ilgInstallLog.SetDeploymentOwners(target, desiredKeys);
			ModDeploymentRestoreOwner desiredWinner = owners[owners.Count - 1];
			if (desiredWinner.Kind == ModDeploymentRestoreOwnerKind.Virtual)
				m_vmaVirtualModActivator.DeploySpecificVirtualLink(target, desiredWinner.OwnerKey, fileManager);
		}

		/// <summary>Recreates exact pure-Virtual owner order while preserving the current unmanaged fallback beneath the stack.</summary>
		private void RestorePureVirtualCapturedStack(ModDeploymentTarget target,
			IReadOnlyList<ModDeploymentRestoreOwner> owners, TxFileManager fileManager)
		{
			string deploymentPath = GetDeploymentPath(target);
			bool hasUnmanagedFallback = File.Exists(deploymentPath);
			for (int index = 0; index < owners.Count; index++)
			{
				ModDeploymentRestoreOwner owner = owners[index];
				string stagedPath = StageVirtualRestorePayload(target, owner, fileManager);
				m_vmaVirtualModActivator.RegisterVirtualLink(target, owner.Mod, target.RelativePath,
					stagedPath, owner.InstallRoot, Math.Max(0, owners.Count - index));
			}

			if (hasUnmanagedFallback)
			{
				string overwritePath = m_vmaVirtualModActivator.GetVirtualOverwritePath(target, owners[0].OwnerKey);
				if (String.IsNullOrWhiteSpace(overwritePath))
					throw new InvalidOperationException("The restored Virtual fallback path could not be resolved.");
				string directory = Path.GetDirectoryName(overwritePath);
				if (!Directory.Exists(directory))
					fileManager.CreateDirectory(directory);
				if (File.Exists(overwritePath))
					fileManager.Delete(overwritePath);
				MoveOrCopyDelete(deploymentPath, overwritePath, fileManager);
			}

			ModDeploymentRestoreOwner winner = owners[owners.Count - 1];
			m_vmaVirtualModActivator.DeploySpecificVirtualLink(target, winner.OwnerKey, fileManager);
		}

		/// <summary>Copies one retained Virtual payload into the canonical native staging location.</summary>
		private string StageVirtualRestorePayload(ModDeploymentTarget target, ModDeploymentRestoreOwner owner,
			TxFileManager fileManager)
		{
			string stagedPath = ScriptedInstallStagingPathResolver.GetStagingPath(owner.Mod, m_gmdGameMode,
				m_vmaVirtualModActivator, target.RelativePath, true);
			WriteRestorePayload(owner.PayloadPath, stagedPath, fileManager);
			return stagedPath;
		}

		/// <summary>Writes one retained payload transactionally to a native-owned deployment/staging location.</summary>
		private static void WriteRestorePayload(string sourcePath, string destinationPath, TxFileManager fileManager)
		{
			string directory = Path.GetDirectoryName(destinationPath);
			if (!String.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
				fileManager.CreateDirectory(directory);
			using (var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
				fileManager.WriteFileStream(destinationPath, stream);
		}
	}
}
