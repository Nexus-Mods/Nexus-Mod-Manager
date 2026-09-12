namespace Nexus.Client.ModManagement
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Security.Cryptography;
	using System.Text;

	using ChinhDo.Transactions;
	using Nexus.Client.Games;
	using Nexus.Client.ModManagement.InstallationLog;
	using Nexus.Client.Mods;
	using Nexus.Transactions;

	/// <summary>
	/// Coordinates method-neutral deployment ownership queries while preserving VMA as the authority for pure Virtual targets.
	/// </summary>
	public sealed class ModDeploymentManager : IModDeploymentManager
	{
		private readonly IInstallLog m_ilgInstallLog;
		private readonly IVirtualModActivator m_vmaVirtualModActivator;
		private readonly IGameMode m_gmdGameMode;

		/// <summary>
		/// Initializes the deployment coordinator.
		/// </summary>
		public ModDeploymentManager(IInstallLog p_ilgInstallLog, IVirtualModActivator p_vmaVirtualModActivator)
			: this(p_ilgInstallLog, p_vmaVirtualModActivator, null)
		{
		}

		/// <summary>
		/// Initializes the deployment coordinator with the game paths required for filesystem mutations.
		/// </summary>
		public ModDeploymentManager(IInstallLog p_ilgInstallLog, IVirtualModActivator p_vmaVirtualModActivator, IGameMode p_gmdGameMode)
		{
			if (p_ilgInstallLog == null)
				throw new ArgumentNullException(nameof(p_ilgInstallLog));
			if (p_vmaVirtualModActivator == null)
				throw new ArgumentNullException(nameof(p_vmaVirtualModActivator));

			m_ilgInstallLog = p_ilgInstallLog;
			m_vmaVirtualModActivator = p_vmaVirtualModActivator;
			m_gmdGameMode = p_gmdGameMode;
		}

		/// <inheritdoc />
		public bool HasPromotedTargets => m_ilgInstallLog.HasDeploymentTargets;

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
		public string GetDeploymentPath(ModDeploymentTarget p_mdtTarget)
		{
			if (p_mdtTarget == null)
				throw new ArgumentNullException(nameof(p_mdtTarget));

			string rootPath = GetDeploymentRootPath(p_mdtTarget.Root);
			return GetContainedPath(rootPath, p_mdtTarget.RelativePath, "deployment");
		}

		/// <inheritdoc />
		public string InstallDirectFile(IMod p_modMod, ModDeploymentTarget p_mdtTarget, FileStream p_fstPayload, TxFileManager p_tfmFileManager)
		{
			RequireDirectMutationArguments(p_modMod, p_mdtTarget, p_tfmFileManager);
			if (p_fstPayload == null)
				throw new ArgumentNullException(nameof(p_fstPayload));

			string modKey = RequireDirectModKey(p_modMod);
			List<string> owners = GetOrPromoteOwnerStack(p_mdtTarget, p_tfmFileManager);
			string deploymentPath = GetDeploymentPath(p_mdtTarget);
			int existingOwnerIndex = owners.FindIndex(x => x.Equals(modKey, StringComparison.OrdinalIgnoreCase));

			if (existingOwnerIndex >= 0 && existingOwnerIndex != owners.Count - 1)
			{
				DeleteBackupIfPresent(p_mdtTarget, modKey, p_tfmFileManager);
				owners.RemoveAt(existingOwnerIndex);
			}

			string currentOwnerKey = owners.Count == 0 ? null : owners[owners.Count - 1];
			bool replacingCurrentOwner = modKey.Equals(currentOwnerKey, StringComparison.OrdinalIgnoreCase);
			if (!replacingCurrentOwner)
			{
				if (currentOwnerKey == null && File.Exists(deploymentPath))
				{
					currentOwnerKey = m_ilgInstallLog.OriginalValuesKey;
					owners.Add(currentOwnerKey);
				}

				if (currentOwnerKey != null)
					DisplaceCurrentOwner(p_mdtTarget, currentOwnerKey, deploymentPath, p_tfmFileManager);

				owners.Add(modKey);
			}

			string deploymentDirectory = Path.GetDirectoryName(deploymentPath);
			if (!Directory.Exists(deploymentDirectory))
				p_tfmFileManager.CreateDirectory(deploymentDirectory);

			p_tfmFileManager.WriteFileStream(deploymentPath, p_fstPayload);
			m_ilgInstallLog.SetDeploymentOwners(p_mdtTarget, owners);
			return deploymentPath;
		}

		/// <inheritdoc />
		public string InstallVirtualFile(IMod p_modMod, ModDeploymentTarget p_mdtTarget, string p_strLogicalPath,
			string p_strStagedSource, ModInstallRoot p_mirInstallRoot, bool p_booActivate, TxFileManager p_tfmFileManager)
		{
			RequireMutationArguments(p_modMod, p_mdtTarget, p_tfmFileManager);
			string modKey = RequireVirtualModKey(p_modMod);
			if (!m_ilgInstallLog.IsDeploymentTargetPromoted(p_mdtTarget))
				throw new InvalidOperationException("Only promoted Virtual targets may use the method-neutral deployment path.");

			var owners = new List<string>(m_ilgInstallLog.GetDeploymentOwnerKeys(p_mdtTarget));
			if (owners.Any(x => x.Equals(modKey, StringComparison.OrdinalIgnoreCase)))
				throw new NotSupportedException("Virtual reinstall and conversion are implemented in Step 6.");

			int priority = p_booActivate ? 0 : CountVirtualOwners(owners);
			m_vmaVirtualModActivator.RegisterVirtualLink(
				p_mdtTarget,
				p_modMod,
				p_strLogicalPath,
				p_strStagedSource,
				p_mirInstallRoot,
				priority);

			if (!p_booActivate)
			{
				int insertionIndex = owners.Count > 0 &&
					owners[0].Equals(m_ilgInstallLog.OriginalValuesKey, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
				owners.Insert(insertionIndex, modKey);
				m_ilgInstallLog.SetDeploymentOwners(p_mdtTarget, owners);
				return string.Empty;
			}

			string deploymentPath = GetDeploymentPath(p_mdtTarget);
			string currentOwnerKey = owners.Count == 0 ? null : owners[owners.Count - 1];
			if (currentOwnerKey != null)
				DisplaceCurrentOwner(p_mdtTarget, currentOwnerKey, deploymentPath, p_tfmFileManager);

			owners.Add(modKey);
			m_vmaVirtualModActivator.DeploySpecificVirtualLink(p_mdtTarget, modKey, p_tfmFileManager);
			m_ilgInstallLog.SetDeploymentOwners(p_mdtTarget, owners);
			return deploymentPath;
		}

		/// <inheritdoc />
		public IReadOnlyCollection<string> UninstallDirectMod(IMod p_modMod, TxFileManager p_tfmFileManager)
		{
			RequireDirectModKey(p_modMod);
			return UninstallMixedMod(p_modMod, p_tfmFileManager);
		}

		/// <inheritdoc />
		public IReadOnlyCollection<string> UninstallMixedMod(IMod p_modMod, TxFileManager p_tfmFileManager)
		{
			RequireMutationArguments(p_modMod, null, p_tfmFileManager);
			string modKey = RequireModKey(p_modMod);
			ModInstallMethod installMethod = m_ilgInstallLog.GetModInstallMethod(p_modMod);
			var targets = new HashSet<ModDeploymentTarget>(m_ilgInstallLog.GetDeploymentTargetsForMod(modKey));
			if (installMethod == ModInstallMethod.Virtual)
				targets.UnionWith(m_vmaVirtualModActivator.GetVirtualTargetsForMod(p_modMod));

			var absentPaths = new List<string>();
			foreach (ModDeploymentTarget target in targets)
			{
				if (m_ilgInstallLog.IsDeploymentTargetPromoted(target))
					RemovePromotedOwner(target, modKey, p_tfmFileManager, absentPaths);
				else if (installMethod == ModInstallMethod.Virtual)
					RemovePureVirtualOwner(target, modKey, p_tfmFileManager, absentPaths);
			}

			if (installMethod == ModInstallMethod.Virtual)
				m_vmaVirtualModActivator.RemoveVirtualModInfoIfUnused(p_modMod);
			return absentPaths;
		}

		/// <inheritdoc />
		public bool HasPromotedFiles(IMod p_modMod)
		{
			if (p_modMod == null)
				return false;

			string modKey = m_ilgInstallLog.GetModKey(p_modMod);
			return !string.IsNullOrWhiteSpace(modKey) &&
				m_ilgInstallLog.GetDeploymentTargetsForMod(modKey).Count > 0;
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

		private void RequireDirectMutationArguments(IMod p_modMod, ModDeploymentTarget p_mdtTarget, TxFileManager p_tfmFileManager)
		{
			if (p_mdtTarget == null)
				throw new ArgumentNullException(nameof(p_mdtTarget));
			RequireMutationArguments(p_modMod, p_mdtTarget, p_tfmFileManager);
		}

		private static void RequireMutationArguments(IMod p_modMod, ModDeploymentTarget p_mdtTarget, TxFileManager p_tfmFileManager)
		{
			if (p_modMod == null)
				throw new ArgumentNullException(nameof(p_modMod));
			if (p_tfmFileManager == null)
				throw new ArgumentNullException(nameof(p_tfmFileManager));

			RequireAmbientTransaction();
		}

		private static void RequireAmbientTransaction()
		{
			if (Transaction.Current == null)
				throw new InvalidOperationException("Direct deployment mutations require an ambient transaction.");
		}

		private string RequireDirectModKey(IMod p_modMod)
		{
			string modKey = m_ilgInstallLog.GetModKey(p_modMod);
			if (string.IsNullOrWhiteSpace(modKey))
				throw new InvalidOperationException("The Direct mod must be registered with InstallLog before deploying files.");
			if (m_ilgInstallLog.GetModInstallMethod(p_modMod) != ModInstallMethod.Direct)
				throw new InvalidOperationException("Standalone Direct deployment requires a mod recorded with the Direct install method.");

			return modKey;
		}

		private string RequireVirtualModKey(IMod p_modMod)
		{
			string modKey = RequireModKey(p_modMod);
			if (m_ilgInstallLog.GetModInstallMethod(p_modMod) != ModInstallMethod.Virtual)
				throw new InvalidOperationException("Promoted Virtual deployment requires a mod recorded with the Virtual install method.");
			return modKey;
		}

		private string RequireModKey(IMod p_modMod)
		{
			string modKey = m_ilgInstallLog.GetModKey(p_modMod);
			if (string.IsNullOrWhiteSpace(modKey))
				throw new InvalidOperationException("The mod must be registered with InstallLog before changing deployment ownership.");
			return modKey;
		}

		private List<string> GetOrPromoteOwnerStack(ModDeploymentTarget p_mdtTarget, TxFileManager p_tfmFileManager)
		{
			if (m_ilgInstallLog.IsDeploymentTargetPromoted(p_mdtTarget))
				return new List<string>(m_ilgInstallLog.GetDeploymentOwnerKeys(p_mdtTarget));

			IReadOnlyList<string> virtualOwners = m_vmaVirtualModActivator.GetVirtualOwnerKeys(p_mdtTarget);
			var owners = new List<string>(virtualOwners.Count + 1);
			string legacyOverwritePath = FindExistingVirtualOverwritePath(p_mdtTarget, virtualOwners);
			if (!string.IsNullOrEmpty(legacyOverwritePath))
			{
				ClaimLegacyOriginalBackup(p_mdtTarget, legacyOverwritePath, p_tfmFileManager);
				owners.Add(m_ilgInstallLog.OriginalValuesKey);
			}
			owners.AddRange(virtualOwners);
			return owners;
		}

		private void DisplaceCurrentOwner(ModDeploymentTarget p_mdtTarget, string p_strOwnerKey,
			string p_strDeploymentPath, TxFileManager p_tfmFileManager)
		{
			if (GetOwnerMethod(p_strOwnerKey) == ModInstallMethod.Virtual)
			{
				m_vmaVirtualModActivator.DetachVirtualLinkWithoutFallback(p_mdtTarget, p_strOwnerKey, p_tfmFileManager);
				if (File.Exists(p_strDeploymentPath))
					throw new IOException(string.Format("Virtual deployment target '{0}' remained attached after detachment.", p_mdtTarget));
				return;
			}

			if (!File.Exists(p_strDeploymentPath))
				throw new FileNotFoundException("The current deployment winner is missing and cannot be backed up.", p_strDeploymentPath);
			MoveToBackup(p_mdtTarget, p_strOwnerKey, p_strDeploymentPath, p_tfmFileManager);
		}

		private void RemovePromotedOwner(ModDeploymentTarget p_mdtTarget, string p_strModKey,
			TxFileManager p_tfmFileManager, ICollection<string> p_colAbsentPaths)
		{
			var owners = new List<string>(m_ilgInstallLog.GetDeploymentOwnerKeys(p_mdtTarget));
			int ownerIndex = owners.FindIndex(x => x.Equals(p_strModKey, StringComparison.OrdinalIgnoreCase));
			if (ownerIndex < 0)
				return;

			bool virtualOwner = GetOwnerMethod(p_strModKey) == ModInstallMethod.Virtual;
			bool currentWinner = ownerIndex == owners.Count - 1;
			string deploymentPath = GetDeploymentPath(p_mdtTarget);
			if (currentWinner)
			{
				if (virtualOwner)
					m_vmaVirtualModActivator.DetachVirtualLinkWithoutFallback(p_mdtTarget, p_strModKey, p_tfmFileManager);
				else if (File.Exists(deploymentPath))
					p_tfmFileManager.Delete(deploymentPath);
			}

			if (virtualOwner)
				m_vmaVirtualModActivator.RemoveVirtualLinkRecord(p_mdtTarget, p_strModKey);
			else if (!currentWinner)
				DeleteBackupIfPresent(p_mdtTarget, p_strModKey, p_tfmFileManager);

			owners.RemoveAt(ownerIndex);
			if (!currentWinner)
			{
				m_ilgInstallLog.SetDeploymentOwners(p_mdtTarget, owners);
				return;
			}

			RestorePromotedWinner(p_mdtTarget, deploymentPath, owners, p_tfmFileManager, p_colAbsentPaths);
		}

		private void RestorePromotedWinner(ModDeploymentTarget p_mdtTarget, string p_strDeploymentPath,
			List<string> p_lstOwners, TxFileManager p_tfmFileManager, ICollection<string> p_colAbsentPaths)
		{
			string restoreOwnerKey = p_lstOwners.Count == 0 ? null : p_lstOwners[p_lstOwners.Count - 1];
			if (restoreOwnerKey == null)
			{
				m_ilgInstallLog.RemoveDeploymentTarget(p_mdtTarget);
				p_colAbsentPaths.Add(p_strDeploymentPath);
				return;
			}

			if (restoreOwnerKey.Equals(m_ilgInstallLog.OriginalValuesKey, StringComparison.OrdinalIgnoreCase))
			{
				RestoreBackup(p_mdtTarget, restoreOwnerKey, p_strDeploymentPath, p_tfmFileManager);
				m_ilgInstallLog.RemoveDeploymentTarget(p_mdtTarget);
				return;
			}

			if (GetOwnerMethod(restoreOwnerKey) == ModInstallMethod.Virtual)
				m_vmaVirtualModActivator.DeploySpecificVirtualLink(p_mdtTarget, restoreOwnerKey, p_tfmFileManager);
			else
				RestoreBackup(p_mdtTarget, restoreOwnerKey, p_strDeploymentPath, p_tfmFileManager);
			m_ilgInstallLog.SetDeploymentOwners(p_mdtTarget, p_lstOwners);
		}

		private void RemovePureVirtualOwner(ModDeploymentTarget p_mdtTarget, string p_strModKey,
			TxFileManager p_tfmFileManager, ICollection<string> p_colAbsentPaths)
		{
			var owners = new List<string>(m_vmaVirtualModActivator.GetVirtualOwnerKeys(p_mdtTarget));
			int ownerIndex = owners.FindIndex(x => x.Equals(p_strModKey, StringComparison.OrdinalIgnoreCase));
			if (ownerIndex < 0)
				return;

			string deploymentPath = GetDeploymentPath(p_mdtTarget);
			string legacyOverwritePath = FindExistingVirtualOverwritePath(p_mdtTarget, owners);
			bool currentWinner = ownerIndex == owners.Count - 1;
			if (currentWinner)
				m_vmaVirtualModActivator.DetachVirtualLinkWithoutFallback(p_mdtTarget, p_strModKey, p_tfmFileManager);
			m_vmaVirtualModActivator.RemoveVirtualLinkRecord(p_mdtTarget, p_strModKey);
			owners.RemoveAt(ownerIndex);

			if (owners.Count > 0)
			{
				RelocateLegacyVirtualOverwrite(p_mdtTarget, legacyOverwritePath, owners[0], p_tfmFileManager);
				if (currentWinner)
					m_vmaVirtualModActivator.DeploySpecificVirtualLink(p_mdtTarget, owners[owners.Count - 1], p_tfmFileManager);
				return;
			}

			if (currentWinner && !string.IsNullOrEmpty(legacyOverwritePath) && File.Exists(legacyOverwritePath))
				MoveOrCopyDelete(legacyOverwritePath, deploymentPath, p_tfmFileManager);
			else if (currentWinner)
				p_colAbsentPaths.Add(deploymentPath);
		}

		private void RelocateLegacyVirtualOverwrite(ModDeploymentTarget p_mdtTarget, string p_strSourcePath,
			string p_strDestinationOwnerKey, TxFileManager p_tfmFileManager)
		{
			if (string.IsNullOrEmpty(p_strSourcePath) || !File.Exists(p_strSourcePath))
				return;

			string destinationPath = m_vmaVirtualModActivator.GetVirtualOverwritePath(p_mdtTarget, p_strDestinationOwnerKey);
			if (string.IsNullOrWhiteSpace(destinationPath) ||
				string.Equals(p_strSourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			if (File.Exists(destinationPath))
				throw new IOException(string.Format("Legacy Virtual overwrite destination '{0}' already exists.", destinationPath));

			string directory = Path.GetDirectoryName(destinationPath);
			if (!Directory.Exists(directory))
				p_tfmFileManager.CreateDirectory(directory);
			MoveOrCopyDelete(p_strSourcePath, destinationPath, p_tfmFileManager);
		}

		private string FindExistingVirtualOverwritePath(ModDeploymentTarget p_mdtTarget, IEnumerable<string> p_enmOwnerKeys)
		{
			foreach (string ownerKey in p_enmOwnerKeys)
			{
				string overwritePath = m_vmaVirtualModActivator.GetVirtualOverwritePath(p_mdtTarget, ownerKey);
				if (!string.IsNullOrEmpty(overwritePath) && File.Exists(overwritePath))
					return overwritePath;
			}
			return null;
		}

		private void ClaimLegacyOriginalBackup(ModDeploymentTarget p_mdtTarget, string p_strLegacyPath,
			TxFileManager p_tfmFileManager)
		{
			string backupPath = GetBackupPath(p_mdtTarget, m_ilgInstallLog.OriginalValuesKey);
			if (File.Exists(backupPath))
				throw new IOException(string.Format("A shared original backup already exists for '{0}'.", p_mdtTarget));

			string backupDirectory = Path.GetDirectoryName(backupPath);
			if (!Directory.Exists(backupDirectory))
				p_tfmFileManager.CreateDirectory(backupDirectory);
			MoveOrCopyDelete(p_strLegacyPath, backupPath, p_tfmFileManager);
		}

		private ModInstallMethod GetOwnerMethod(string p_strOwnerKey)
		{
			if (p_strOwnerKey.Equals(m_ilgInstallLog.OriginalValuesKey, StringComparison.OrdinalIgnoreCase))
				return ModInstallMethod.Direct;
			return m_ilgInstallLog.GetModInstallMethod(p_strOwnerKey);
		}

		private int CountVirtualOwners(IEnumerable<string> p_enmOwnerKeys)
		{
			return p_enmOwnerKeys.Count(x =>
				!x.Equals(m_ilgInstallLog.OriginalValuesKey, StringComparison.OrdinalIgnoreCase) &&
				GetOwnerMethod(x) == ModInstallMethod.Virtual);
		}

		private string GetDeploymentRootPath(ModDeploymentRoot p_mdrRoot)
		{
			if (m_gmdGameMode == null)
				throw new InvalidOperationException("This deployment coordinator was not initialized with game paths.");

			string rootPath;
			switch (p_mdrRoot)
			{
				case ModDeploymentRoot.Data:
					rootPath = m_gmdGameMode.GameModeEnvironmentInfo.InstallationPath;
					break;
				case ModDeploymentRoot.GameRoot:
					rootPath = m_gmdGameMode.InstallationPath;
					break;
				case ModDeploymentRoot.Secondary:
					rootPath = m_gmdGameMode.SecondaryInstallationPath;
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(p_mdrRoot));
			}

			if (string.IsNullOrWhiteSpace(rootPath))
				throw new InvalidOperationException(string.Format("Deployment root '{0}' is not configured for the current game mode.", p_mdrRoot));

			return rootPath;
		}

		private string GetBackupPath(ModDeploymentTarget p_mdtTarget, string p_strOwnerKey)
		{
			string overwriteDirectory = m_gmdGameMode.GameModeEnvironmentInfo.OverwriteDirectory;
			if (string.IsNullOrWhiteSpace(overwriteDirectory))
				throw new InvalidOperationException("The overwrite backup directory is not configured for the current game mode.");

			string relativeDirectory = Path.GetDirectoryName(p_mdtTarget.RelativePath);
			string backupDirectory = Path.Combine(overwriteDirectory, "deployment", p_mdtTarget.Root.ToString());
			if (!string.IsNullOrEmpty(relativeDirectory))
				backupDirectory = Path.Combine(backupDirectory, relativeDirectory);

			string backupName = Path.GetFileName(p_mdtTarget.RelativePath) + "." + GetOwnerHash(p_strOwnerKey) + ".nmmbackup";
			return GetContainedPath(backupDirectory, backupName, "overwrite backup");
		}

		private static string GetOwnerHash(string p_strOwnerKey)
		{
			if (string.IsNullOrWhiteSpace(p_strOwnerKey))
				throw new ArgumentException("A deployment owner key is required.", nameof(p_strOwnerKey));

			using (SHA256 hash = SHA256.Create())
			{
				byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(p_strOwnerKey.ToUpperInvariant()));
				var result = new StringBuilder(bytes.Length * 2);
				foreach (byte value in bytes)
					result.Append(value.ToString("x2"));
				return result.ToString();
			}
		}

		private static string GetContainedPath(string p_strRootPath, string p_strRelativePath, string p_strDescription)
		{
			string rootPath = Path.GetFullPath(p_strRootPath);
			string rootPrefix = rootPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
				rootPath.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
					? rootPath
					: rootPath + Path.DirectorySeparatorChar;
			string path = Path.GetFullPath(Path.Combine(rootPath, p_strRelativePath));
			if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException(string.Format("The {0} path '{1}' escapes its configured root.", p_strDescription, p_strRelativePath));

			return path;
		}

		private void MoveToBackup(ModDeploymentTarget p_mdtTarget, string p_strOwnerKey, string p_strDeploymentPath, TxFileManager p_tfmFileManager)
		{
			string backupPath = GetBackupPath(p_mdtTarget, p_strOwnerKey);
			if (File.Exists(backupPath))
				throw new IOException(string.Format("A Direct overwrite backup already exists for '{0}'.", p_mdtTarget));

			string backupDirectory = Path.GetDirectoryName(backupPath);
			if (!Directory.Exists(backupDirectory))
				p_tfmFileManager.CreateDirectory(backupDirectory);

			MoveOrCopyDelete(p_strDeploymentPath, backupPath, p_tfmFileManager);
		}

		private void RestoreBackup(ModDeploymentTarget p_mdtTarget, string p_strOwnerKey, string p_strDeploymentPath, TxFileManager p_tfmFileManager)
		{
			string backupPath = GetBackupPath(p_mdtTarget, p_strOwnerKey);
			if (!File.Exists(backupPath))
				throw new FileNotFoundException("The Direct overwrite backup required for restoration is missing.", backupPath);

			string deploymentDirectory = Path.GetDirectoryName(p_strDeploymentPath);
			if (!Directory.Exists(deploymentDirectory))
				p_tfmFileManager.CreateDirectory(deploymentDirectory);

			MoveOrCopyDelete(backupPath, p_strDeploymentPath, p_tfmFileManager);
		}

		private void DeleteBackupIfPresent(ModDeploymentTarget p_mdtTarget, string p_strOwnerKey, TxFileManager p_tfmFileManager)
		{
			string backupPath = GetBackupPath(p_mdtTarget, p_strOwnerKey);
			if (File.Exists(backupPath))
				p_tfmFileManager.Delete(backupPath);
		}

		private static void MoveOrCopyDelete(string p_strSourcePath, string p_strDestinationPath, TxFileManager p_tfmFileManager)
		{
			string sourceRoot = Path.GetPathRoot(Path.GetFullPath(p_strSourcePath));
			string destinationRoot = Path.GetPathRoot(Path.GetFullPath(p_strDestinationPath));
			if (string.Equals(sourceRoot, destinationRoot, StringComparison.OrdinalIgnoreCase))
			{
				p_tfmFileManager.Move(p_strSourcePath, p_strDestinationPath);
				return;
			}

			p_tfmFileManager.Copy(p_strSourcePath, p_strDestinationPath, true);
			p_tfmFileManager.Delete(p_strSourcePath);
		}
	}
}
