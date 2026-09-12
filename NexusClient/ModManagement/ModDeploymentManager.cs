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

			EnsureStandaloneDirectTarget(p_mdtTarget);
			string modKey = RequireDirectModKey(p_modMod);
			var owners = new List<string>(m_ilgInstallLog.GetDeploymentOwnerKeys(p_mdtTarget));
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
				{
					if (!File.Exists(deploymentPath))
						throw new FileNotFoundException("The current Direct deployment winner is missing and cannot be backed up.", deploymentPath);

					MoveToBackup(p_mdtTarget, currentOwnerKey, deploymentPath, p_tfmFileManager);
				}

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
		public IReadOnlyCollection<string> UninstallDirectMod(IMod p_modMod, TxFileManager p_tfmFileManager)
		{
			if (p_modMod == null)
				throw new ArgumentNullException(nameof(p_modMod));
			if (p_tfmFileManager == null)
				throw new ArgumentNullException(nameof(p_tfmFileManager));
			RequireAmbientTransaction();

			string modKey = RequireDirectModKey(p_modMod);
			ModDeploymentTarget[] targets = m_ilgInstallLog.GetDeploymentTargetsForMod(modKey).ToArray();
			var absentPaths = new List<string>();
			foreach (ModDeploymentTarget target in targets)
			{
				EnsureStandaloneDirectTarget(target);
				var owners = new List<string>(m_ilgInstallLog.GetDeploymentOwnerKeys(target));
				int ownerIndex = owners.FindIndex(x => x.Equals(modKey, StringComparison.OrdinalIgnoreCase));
				if (ownerIndex < 0)
					continue;

				string deploymentPath = GetDeploymentPath(target);
				bool isCurrentWinner = ownerIndex == owners.Count - 1;
				owners.RemoveAt(ownerIndex);

				if (!isCurrentWinner)
				{
					DeleteBackupIfPresent(target, modKey, p_tfmFileManager);
					m_ilgInstallLog.SetDeploymentOwners(target, owners);
					continue;
				}

				if (File.Exists(deploymentPath))
					p_tfmFileManager.Delete(deploymentPath);

				string restoreOwnerKey = owners.Count == 0 ? null : owners[owners.Count - 1];
				if (restoreOwnerKey == null)
				{
					m_ilgInstallLog.RemoveDeploymentTarget(target);
					absentPaths.Add(deploymentPath);
					continue;
				}

				RestoreBackup(target, restoreOwnerKey, deploymentPath, p_tfmFileManager);
				if (restoreOwnerKey.Equals(m_ilgInstallLog.OriginalValuesKey, StringComparison.OrdinalIgnoreCase))
				{
					owners.RemoveAt(owners.Count - 1);
					m_ilgInstallLog.RemoveDeploymentTarget(target);
				}
				else
				{
					m_ilgInstallLog.SetDeploymentOwners(target, owners);
				}
			}

			return absentPaths;
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
			if (p_modMod == null)
				throw new ArgumentNullException(nameof(p_modMod));
			if (p_mdtTarget == null)
				throw new ArgumentNullException(nameof(p_mdtTarget));
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

		private void EnsureStandaloneDirectTarget(ModDeploymentTarget p_mdtTarget)
		{
			if (m_vmaVirtualModActivator.GetVirtualOwnerKeys(p_mdtTarget).Count > 0)
				throw new NotSupportedException("Mixed Virtual/Direct ownership is implemented in Step 4 and is not available yet.");
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
