namespace Nexus.Client.ModManagement
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.IO;
	using System.Linq;

	using ChinhDo.Transactions;

	using Nexus.Client.Mods;
	using Nexus.Transactions;

	/// <summary>
	/// Coordinator-facing Virtual deployment backend. These operations deliberately avoid fallback selection,
	/// plugin changes, and immediate VMA persistence. Promoted-target state is durably prepared for crash recovery;
	/// pure-Virtual state keeps the existing commit-time persistence path.
	/// </summary>
	public partial class VirtualModActivator
	{
		private readonly object m_objDeploymentTransactionLock = new object();
		private Dictionary<string, VirtualDeploymentTransactionEnlistment> m_dicDeploymentTransactionEnlistments;
		private bool m_booDeploymentPublicationPending;

		/// <inheritdoc />
		public IReadOnlyList<string> GetVirtualOwnerKeys(ModDeploymentTarget p_mdtTarget)
		{
			if (p_mdtTarget == null)
				throw new ArgumentNullException(nameof(p_mdtTarget));

			IEnumerable<IVirtualModLink> links = GetVirtualOwnerLinksForTarget(p_mdtTarget)
				.OrderBy(x => x.Active ? 1 : 0)
				.ThenByDescending(x => x.Priority);
			var ownerKeys = new List<string>();

			foreach (IVirtualModLink link in links)
			{
				string ownerKey = GetVirtualOwnerKey(link);
				if (string.IsNullOrWhiteSpace(ownerKey))
					continue;

				int existingIndex = ownerKeys.FindIndex(x => x.Equals(ownerKey, StringComparison.OrdinalIgnoreCase));
				if (existingIndex >= 0)
					ownerKeys.RemoveAt(existingIndex);
				ownerKeys.Add(ownerKey);
			}

			return ownerKeys;
		}

		/// <inheritdoc />
		public IReadOnlyCollection<ModDeploymentTarget> GetVirtualTargetsForMod(IMod p_modMod)
		{
			if (p_modMod == null)
				throw new ArgumentNullException(nameof(p_modMod));

			string ownerKey = ModInstallLog.GetModKey(p_modMod);
			if (string.IsNullOrWhiteSpace(ownerKey))
				return new ModDeploymentTarget[0];

			string modFileName = Path.GetFileName(p_modMod.Filename);
			var targets = new HashSet<ModDeploymentTarget>();
			foreach (IVirtualModLink link in GetVirtualLinksForOwnerKey(ownerKey))
			{
				if (link == null || !VirtualModLinkMatchesMod(link, p_modMod, modFileName))
					continue;

				targets.Add(ModDeploymentTargetResolver.Resolve(
					GameMode,
					p_modMod,
					link.VirtualModPath,
					link.InstallRoot));
			}

			return targets.ToArray();
		}

		/// <inheritdoc />
		public string GetVirtualSourceForOwner(ModDeploymentTarget p_mdtTarget, string p_strOwnerKey)
		{
			IVirtualModLink link = FindVirtualOwnerLink(p_mdtTarget, p_strOwnerKey);
			return link == null ? null : ResolveVirtualSourcePath(link, p_mdtTarget);
		}

		/// <inheritdoc />
		public string GetVirtualOverwritePath(ModDeploymentTarget p_mdtTarget, string p_strOwnerKey)
		{
			IVirtualModLink link = FindVirtualOwnerLink(p_mdtTarget, p_strOwnerKey);
			if (link == null || link.ModInfo == null || string.IsNullOrWhiteSpace(link.ModInfo.ModFileName))
				return null;

			return Path.Combine(
				m_strVirtualActivatorOverwritePath,
				Path.GetFileNameWithoutExtension(link.ModInfo.ModFileName),
				link.VirtualModPath);
		}

		/// <summary>
		/// Returns promoted targets whose Virtual link still references the pre-upgrade archive after the upgrade
		/// file pass. Targets that were reinstalled already point at the replacement archive and are excluded.
		/// </summary>
		public IReadOnlyList<ModDeploymentTarget> GetStalePromotedVirtualTargetsForUpgrade(IMod p_modOldMod)
		{
			if (p_modOldMod == null)
				throw new ArgumentNullException(nameof(p_modOldMod));

			string ownerKey = ModInstallLog.GetModKey(p_modOldMod);
			if (string.IsNullOrWhiteSpace(ownerKey))
				return new ModDeploymentTarget[0];

			string oldModFileName = Path.GetFileName(p_modOldMod.Filename);
			var staleTargets = new List<ModDeploymentTarget>();
			foreach (ModDeploymentTarget target in ModInstallLog.GetDeploymentTargetsForMod(ownerKey))
			{
				if (!ModInstallLog.IsDeploymentTargetPromoted(target))
					continue;

				IVirtualModLink link = FindVirtualOwnerLink(target, ownerKey);
				if (link != null && link.ModInfo != null &&
					!string.IsNullOrEmpty(link.ModInfo.ModFileName) &&
					link.ModInfo.ModFileName.Equals(oldModFileName, StringComparison.OrdinalIgnoreCase))
				{
					staleTargets.Add(target);
				}
			}

			return staleTargets;
		}

		/// <inheritdoc />
		public void RegisterVirtualLink(ModDeploymentTarget p_mdtTarget, IMod p_modMod, string p_strLogicalPath,
			string p_strStagedSource, ModInstallRoot p_mirInstallRoot, int p_intPriority)
		{
			if (p_mdtTarget == null)
				throw new ArgumentNullException(nameof(p_mdtTarget));
			if (p_modMod == null)
				throw new ArgumentNullException(nameof(p_modMod));
			if (string.IsNullOrWhiteSpace(p_strLogicalPath))
				throw new ArgumentException("A logical Virtual destination is required.", nameof(p_strLogicalPath));
			if (string.IsNullOrWhiteSpace(p_strStagedSource) || !File.Exists(p_strStagedSource))
				throw new FileNotFoundException("The staged Virtual source could not be found.", p_strStagedSource);

			string ownerKey = ModInstallLog.GetModKey(p_modMod);
			if (string.IsNullOrWhiteSpace(ownerKey))
				throw new InvalidOperationException("The Virtual mod must be registered with InstallLog before its link is registered.");
			if (FindVirtualOwnerLink(p_mdtTarget, ownerKey) != null)
				throw new InvalidOperationException(string.Format("Virtual owner '{0}' is already registered for deployment target '{1}'.", ownerKey, p_mdtTarget));

			string realPath = GetRealFilePathFromSource(p_strStagedSource);
			if (string.IsNullOrWhiteSpace(realPath))
				throw new InvalidDataException(string.Format("Staged Virtual source '{0}' is outside VirtualInstall/NMMLink.", p_strStagedSource));

			VirtualDeploymentTransactionEnlistment enlistment = GetVirtualDeploymentEnlistment();
			IVirtualModInfo modInfo = FindVirtualModInfoByFileName(Path.GetFileName(p_modMod.Filename));
			if (modInfo == null)
			{
				modInfo = new VirtualModInfo(p_modMod.Id, p_modMod.DownloadId, p_modMod.ModName, p_modMod.Filename, p_modMod.HumanReadableVersion);
				enlistment.TouchModInfo(modInfo, false);
				AddVirtualModInfo(modInfo);
				enlistment.SetModInfoPresent(modInfo, true);
			}

			var link = new VirtualModLink(realPath, p_strLogicalPath, p_intPriority, false, modInfo, p_mirInstallRoot);
			enlistment.Touch(link, p_mdtTarget, false);
			AddVirtualLink(link, p_modMod);
			enlistment.SetLinkPresent(link, true);
			enlistment.MarkDirty();

			if (FindVirtualOwnerLink(p_mdtTarget, ownerKey) == null)
				throw new InvalidOperationException(string.Format("The registered Virtual link did not resolve to deployment target '{0}'.", p_mdtTarget));
		}

		/// <inheritdoc />
		public void DetachVirtualLinkWithoutFallback(ModDeploymentTarget p_mdtTarget, string p_strOwnerKey, TxFileManager p_tfmFileManager)
		{
			if (p_tfmFileManager == null)
				throw new ArgumentNullException(nameof(p_tfmFileManager));

			IVirtualModLink link = RequireVirtualOwnerLink(p_mdtTarget, p_strOwnerKey);
			VirtualDeploymentTransactionEnlistment enlistment = GetVirtualDeploymentEnlistment();
			enlistment.Touch(link, p_mdtTarget, true);

			string deployedPath = GetDeploymentPathForTarget(p_mdtTarget);
			if (link.Active && File.Exists(deployedPath))
			{
				string sourcePath = ResolveVirtualSourcePath(link, p_mdtTarget);
				p_tfmFileManager.DeleteLink(deployedPath, sourcePath);
			}

			link.Active = false;
			enlistment.MarkDirty();
		}

		/// <inheritdoc />
		public void DeploySpecificVirtualLink(ModDeploymentTarget p_mdtTarget, string p_strOwnerKey, TxFileManager p_tfmFileManager)
		{
			if (p_tfmFileManager == null)
				throw new ArgumentNullException(nameof(p_tfmFileManager));

			IVirtualModLink link = RequireVirtualOwnerLink(p_mdtTarget, p_strOwnerKey);
			string sourcePath = ResolveVirtualSourcePath(link, p_mdtTarget);
			if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
				throw new FileNotFoundException("The staged Virtual source for the requested deployment owner could not be found.", sourcePath);

			VirtualDeploymentTransactionEnlistment enlistment = GetVirtualDeploymentEnlistment();
			enlistment.Touch(link, p_mdtTarget, true);

			string deployedPath = GetDeploymentPathForTarget(p_mdtTarget);
			string deployedDirectory = Path.GetDirectoryName(deployedPath);
			if (!string.IsNullOrWhiteSpace(deployedDirectory) && !Directory.Exists(deployedDirectory))
				p_tfmFileManager.CreateDirectory(deployedDirectory);
			if (File.Exists(deployedPath))
				p_tfmFileManager.Delete(deployedPath);

			DeployVirtualSource(p_tfmFileManager, sourcePath, deployedPath);
			link.Active = true;
			enlistment.MarkDirty();
		}

		/// <inheritdoc />
		public void RecoverVirtualDeploymentWinner(ModDeploymentTarget p_mdtTarget, string p_strOwnerKey, FileEntryKind p_fekExpectedKind)
		{
			if (p_fekExpectedKind == FileEntryKind.Absent)
				throw new ArgumentOutOfRangeException(nameof(p_fekExpectedKind));

			IVirtualModLink link = RequireVirtualOwnerLink(p_mdtTarget, p_strOwnerKey);
			string sourcePath = ResolveVirtualSourcePath(link, p_mdtTarget);
			string deployedPath = GetDeploymentPathForTarget(p_mdtTarget);
			string deployedDirectory = Path.GetDirectoryName(deployedPath);
			if (!String.IsNullOrWhiteSpace(deployedDirectory) && !Directory.Exists(deployedDirectory))
				Directory.CreateDirectory(deployedDirectory);

			if (String.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
				throw new FileNotFoundException("The staged Virtual source required for deployment recovery could not be found.", sourcePath);

			var recoveryFileManager = new TxFileManager { TxEnabled = false };
			if (IsRecoveredVirtualEntry(recoveryFileManager, deployedPath, sourcePath, p_fekExpectedKind))
			{
				link.Active = true;
				return;
			}

			recoveryFileManager.DeleteFileEntryIfPresent(deployedPath);
			RestoreVirtualDeploymentEntry(recoveryFileManager, sourcePath, deployedPath, p_fekExpectedKind);
			VerifyRecoveredVirtualEntry(recoveryFileManager, deployedPath, sourcePath, p_fekExpectedKind);
			link.Active = true;
		}

		/// <summary>
		/// Determines whether crash recovery can leave an already-restored Virtual deployment entry untouched.
		/// </summary>
		private static bool IsRecoveredVirtualEntry(TxFileManager p_tfmFileManager, string p_strDeployedPath,
			string p_strSourcePath, FileEntryKind p_fekExpectedKind)
		{
			if (p_fekExpectedKind == FileEntryKind.Unknown)
				return p_tfmFileManager.IsSameFile(p_strDeployedPath, p_strSourcePath);
			if (p_fekExpectedKind != FileEntryKind.HardLink && p_fekExpectedKind != FileEntryKind.SymbolicLink)
				return false;

			return p_tfmFileManager.GetFileEntryKind(p_strDeployedPath, p_strSourcePath) == p_fekExpectedKind &&
				p_tfmFileManager.IsSameFile(p_strDeployedPath, p_strSourcePath);
		}

		/// <summary>
		/// Recreates a Virtual deployment entry using the topology captured before the interrupted mutation.
		/// </summary>
		private void RestoreVirtualDeploymentEntry(TxFileManager p_tfmFileManager, string p_strSourcePath,
			string p_strDeployedPath, FileEntryKind p_fekExpectedKind)
		{
			switch (p_fekExpectedKind)
			{
				case FileEntryKind.Unknown:
					DeployVirtualSource(p_tfmFileManager, p_strSourcePath, p_strDeployedPath);
					break;
				case FileEntryKind.RegularFile:
					p_tfmFileManager.Copy(p_strSourcePath, p_strDeployedPath, true);
					break;
				case FileEntryKind.HardLink:
					if (!p_tfmFileManager.CreateHardLink(p_strDeployedPath, p_strSourcePath))
						throw new IOException(string.Format("Unable to restore hard link '{0}' -> '{1}' during deployment recovery.",
							p_strDeployedPath, p_strSourcePath));
					break;
				case FileEntryKind.SymbolicLink:
					p_tfmFileManager.CreateSymbolicLink(p_strDeployedPath, p_strSourcePath);
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(p_fekExpectedKind));
			}
		}

		/// <summary>
		/// Verifies the topology and source identity of a recreated Virtual deployment entry.
		/// </summary>
		private static void VerifyRecoveredVirtualEntry(TxFileManager p_tfmFileManager, string p_strDeployedPath,
			string p_strSourcePath, FileEntryKind p_fekExpectedKind)
		{
			FileEntryKind actualKind = p_tfmFileManager.GetFileEntryKind(p_strDeployedPath, p_strSourcePath);
			if (actualKind == FileEntryKind.Absent)
				throw new IOException(string.Format("Deployment recovery did not recreate '{0}' from '{1}'.", p_strDeployedPath, p_strSourcePath));

			if (p_fekExpectedKind == FileEntryKind.Unknown)
				return;
			if (actualKind != p_fekExpectedKind)
			{
				throw new IOException(string.Format(
					"Deployment recovery restored '{0}' with topology '{1}' instead of '{2}'. Source: '{3}'.",
					p_strDeployedPath, actualKind, p_fekExpectedKind, p_strSourcePath));
			}

			if ((p_fekExpectedKind == FileEntryKind.HardLink || p_fekExpectedKind == FileEntryKind.SymbolicLink) &&
				!p_tfmFileManager.IsSameFile(p_strDeployedPath, p_strSourcePath))
			{
				throw new IOException(string.Format(
					"Deployment recovery restored link '{0}', but it does not resolve to the expected source '{1}'.",
					p_strDeployedPath, p_strSourcePath));
			}
		}

		/// <inheritdoc />
		public void RemoveVirtualLinkRecord(ModDeploymentTarget p_mdtTarget, string p_strOwnerKey)
		{
			IVirtualModLink link = RequireVirtualOwnerLink(p_mdtTarget, p_strOwnerKey);
			VirtualDeploymentTransactionEnlistment enlistment = GetVirtualDeploymentEnlistment();
			enlistment.Touch(link, p_mdtTarget, true);

			RemoveVirtualLink(link, FindManagedMod(link.ModInfo));
			enlistment.SetLinkPresent(link, false);
			enlistment.MarkDirty();
		}

		/// <inheritdoc />
		public void SetVirtualLinkActiveState(ModDeploymentTarget p_mdtTarget, string p_strOwnerKey, bool p_booActive)
		{
			IVirtualModLink link = RequireVirtualOwnerLink(p_mdtTarget, p_strOwnerKey);
			if (link.Active == p_booActive)
				return;

			VirtualDeploymentTransactionEnlistment enlistment = GetVirtualDeploymentEnlistment();
			enlistment.Touch(link, p_mdtTarget, true);
			link.Active = p_booActive;
			enlistment.MarkDirty();
		}

		/// <inheritdoc />
		public void RemoveVirtualModInfoIfUnused(IMod p_modMod)
		{
			if (p_modMod == null)
				throw new ArgumentNullException(nameof(p_modMod));

			string ownerKey = ModInstallLog.GetModKey(p_modMod);
			string modFileName = Path.GetFileName(p_modMod.Filename);
			if (!string.IsNullOrWhiteSpace(ownerKey) && GetVirtualLinksForOwnerKey(ownerKey).Length > 0)
				return;
			if (string.IsNullOrWhiteSpace(ownerKey) && m_tslVirtualModList.Any(x => VirtualModLinkMatchesMod(x, p_modMod, modFileName)))
				return;

			IVirtualModInfo[] modInfos = m_tslVirtualModInfo
				.Where(x => VirtualModInfoMatchesMod(x, p_modMod, modFileName))
				.ToArray();
			if (modInfos.Length == 0)
				return;

			VirtualDeploymentTransactionEnlistment enlistment = GetVirtualDeploymentEnlistment();
			foreach (IVirtualModInfo modInfo in modInfos)
			{
				enlistment.TouchModInfo(modInfo, true);
				m_tslVirtualModInfo.Remove(modInfo);
				enlistment.SetModInfoPresent(modInfo, false);
			}
			MarkVirtualModInfoLookupDirty();
			enlistment.MarkDirty();
		}

		private IVirtualModLink[] GetVirtualOwnerLinksForTarget(ModDeploymentTarget p_mdtTarget)
		{
			string deploymentPath = GetDeploymentPathForTarget(p_mdtTarget);
			EnsureVirtualLinkIndex();

			IVirtualModLink[] links;
			lock (m_objVirtualLinkIndexLock)
			{
				VirtualLinkIndexBucket bucket = m_vliVirtualLinkIndex.FindByDeploymentPath(deploymentPath);
				links = bucket == null ? new IVirtualModLink[0] : bucket.ToArray();
			}

			return links
				.Where(x => x != null && string.Equals(GetDeployedFilePath(x), deploymentPath, StringComparison.OrdinalIgnoreCase))
				.ToArray();
		}

		private IVirtualModLink FindVirtualOwnerLink(ModDeploymentTarget p_mdtTarget, string p_strOwnerKey)
		{
			if (p_mdtTarget == null)
				throw new ArgumentNullException(nameof(p_mdtTarget));
			if (string.IsNullOrWhiteSpace(p_strOwnerKey))
				throw new ArgumentException("A Virtual deployment owner key is required.", nameof(p_strOwnerKey));

			return GetVirtualOwnerLinksForTarget(p_mdtTarget)
				.OrderBy(x => x.Active ? 1 : 0)
				.ThenByDescending(x => x.Priority)
				.LastOrDefault(x => string.Equals(GetVirtualOwnerKey(x), p_strOwnerKey, StringComparison.OrdinalIgnoreCase));
		}

		private IVirtualModLink RequireVirtualOwnerLink(ModDeploymentTarget p_mdtTarget, string p_strOwnerKey)
		{
			IVirtualModLink link = FindVirtualOwnerLink(p_mdtTarget, p_strOwnerKey);
			if (link == null)
				throw new InvalidOperationException(string.Format("Virtual owner '{0}' is not registered for deployment target '{1}'.", p_strOwnerKey, p_mdtTarget));
			return link;
		}

		private string GetVirtualOwnerKey(IVirtualModLink p_vmlLink)
		{
			if (p_vmlLink == null || p_vmlLink.ModInfo == null)
				return null;

			IMod mod = FindManagedMod(p_vmlLink.ModInfo);
			return mod == null ? null : ModInstallLog.GetModKey(mod);
		}

		/// <summary>
		/// Gets the Virtual links owned by one managed mod without scanning the complete VMA link list.
		/// </summary>
		private IVirtualModLink[] GetVirtualLinksForOwnerKey(string p_strOwnerKey)
		{
			while (true)
			{
				EnsureVirtualLinkIndex();
				lock (m_objVirtualLinkIndexLock)
				{
					if (m_booVirtualLinkIndexDirty)
						continue;

					VirtualLinkOwnerIndexBucket bucket = m_vliVirtualLinkIndex.FindByOwnerKey(p_strOwnerKey);
					return bucket == null ? new IVirtualModLink[0] : bucket.ToArray();
				}
			}
		}

		private string GetDeploymentPathForTarget(ModDeploymentTarget p_mdtTarget)
		{
			return ModDeploymentTargetResolver.GetPhysicalPath(GameMode, p_mdtTarget);
		}

		private string ResolveVirtualSourcePath(IVirtualModLink p_vmlLink, ModDeploymentTarget p_mdtTarget)
		{
			if (p_vmlLink == null || string.IsNullOrWhiteSpace(p_vmlLink.RealModPath))
				return null;

			string virtualSource = Path.Combine(m_strVirtualActivatorPath, p_vmlLink.RealModPath);
			string linkSource = null;
			if (MultiHDMode)
			{
				try
				{
					if (!string.IsNullOrWhiteSpace(HDLinkFolder))
						linkSource = Path.Combine(HDLinkFolder, p_vmlLink.RealModPath);
				}
				catch
				{
					linkSource = null;
				}
			}

			string deployedPath = GetDeploymentPathForTarget(p_mdtTarget);
			string extension = Path.GetExtension(deployedPath);
			bool hardLinkRequired = GameMode.HardlinkRequiredFilesType(deployedPath) ||
				extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
				extension.Equals(".jar", StringComparison.OrdinalIgnoreCase);

			if (hardLinkRequired && MultiHDMode && !string.IsNullOrWhiteSpace(linkSource) && File.Exists(linkSource))
				return linkSource;
			if (File.Exists(virtualSource))
				return virtualSource;
			if (!string.IsNullOrWhiteSpace(linkSource) && File.Exists(linkSource))
				return linkSource;

			return virtualSource;
		}

		private void DeployVirtualSource(TxFileManager p_tfmFileManager, string p_strSourcePath, string p_strDeployedPath)
		{
			string extension = Path.GetExtension(p_strDeployedPath);
			if (!extension.StartsWith(".", StringComparison.Ordinal))
				extension = "." + extension;

			bool hardLinkRequired = GameMode.HardlinkRequiredFilesType(p_strDeployedPath) ||
				extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
				extension.Equals(".jar", StringComparison.OrdinalIgnoreCase);

			if (GameMode.RealFileRequired(extension))
			{
				p_tfmFileManager.Copy(p_strSourcePath, p_strDeployedPath, true);
				return;
			}

			if (hardLinkRequired)
			{
				if (!p_tfmFileManager.CreateHardLink(p_strDeployedPath, p_strSourcePath))
					p_tfmFileManager.Copy(p_strSourcePath, p_strDeployedPath, true);
				return;
			}

			if (DisableLinkCreation)
				throw new InvalidOperationException("Virtual link creation is disabled for the current game mode.");

			if (!MultiHDMode && p_tfmFileManager.CreateHardLink(p_strDeployedPath, p_strSourcePath))
				return;
			if (p_tfmFileManager.CreateSymbolicLink(p_strDeployedPath, p_strSourcePath))
				return;

			throw new IOException(string.Format("Failed to create a Virtual link for '{0}' at '{1}'.", p_strSourcePath, p_strDeployedPath));
		}

		private VirtualDeploymentTransactionEnlistment GetVirtualDeploymentEnlistment()
		{
			Transaction transaction = Transaction.Current;
			if (transaction == null)
				throw new InvalidOperationException("Coordinator-facing Virtual deployment mutations require an ambient transaction.");

			string transactionId = transaction.TransactionInformation.LocalIdentifier;
			lock (m_objDeploymentTransactionLock)
			{
				if (m_dicDeploymentTransactionEnlistments == null)
					m_dicDeploymentTransactionEnlistments = new Dictionary<string, VirtualDeploymentTransactionEnlistment>();

				VirtualDeploymentTransactionEnlistment enlistment;
				if (!m_dicDeploymentTransactionEnlistments.TryGetValue(transactionId, out enlistment))
				{
					enlistment = new VirtualDeploymentTransactionEnlistment(this, transaction, transactionId);
					m_dicDeploymentTransactionEnlistments.Add(transactionId, enlistment);
				}
				return enlistment;
			}
		}

		private void ReleaseVirtualDeploymentEnlistment(string p_strTransactionId)
		{
			lock (m_objDeploymentTransactionLock)
			{
				if (m_dicDeploymentTransactionEnlistments != null)
					m_dicDeploymentTransactionEnlistments.Remove(p_strTransactionId);
			}
		}

		/// <summary>
		/// Records that a committed deployment transaction has externally visible VMA changes to publish.
		/// </summary>
		private void MarkDeploymentPublicationPending()
		{
			lock (m_objDeploymentTransactionLock)
				m_booDeploymentPublicationPending = true;
		}

		/// <summary>
		/// Clears a deferred deployment publication when another final-state notification supersedes it.
		/// </summary>
		private void ClearDeploymentPublicationPending()
		{
			lock (m_objDeploymentTransactionLock)
				m_booDeploymentPublicationPending = false;
		}

		/// <inheritdoc />
		public void PublishPendingDeploymentChanges()
		{
			EventHandler handler;
			lock (m_objDeploymentTransactionLock)
			{
				if (!m_booDeploymentPublicationPending)
					return;

				m_booDeploymentPublicationPending = false;
				handler = ModActivationChanged;
			}

			try
			{
				if (handler != null)
					handler(null, EventArgs.Empty);
			}
			catch (Exception ex)
			{
				Trace.TraceError("Unable to publish committed Virtual deployment changes: {0}", ex);
			}
		}

		private sealed class VirtualDeploymentTransactionEnlistment : IEnlistmentNotification
		{
			private readonly VirtualModActivator m_vmaOwner;
			private readonly Transaction m_trnTransaction;
			private readonly string m_strTransactionId;
			private readonly List<VirtualLinkSnapshot> m_lstTouchedLinks = new List<VirtualLinkSnapshot>();
			private readonly Dictionary<IVirtualModLink, VirtualLinkSnapshot> m_dicTouchedLinks =
				new Dictionary<IVirtualModLink, VirtualLinkSnapshot>(ReferenceEqualityComparer<IVirtualModLink>.Instance);
			private readonly List<VirtualModInfoSnapshot> m_lstTouchedModInfos = new List<VirtualModInfoSnapshot>();
			private readonly Dictionary<IVirtualModInfo, VirtualModInfoSnapshot> m_dicTouchedModInfos =
				new Dictionary<IVirtualModInfo, VirtualModInfoSnapshot>(ReferenceEqualityComparer<IVirtualModInfo>.Instance);
			private readonly Dictionary<ModDeploymentTarget, string[]> m_dicInitialOwnerStacks = new Dictionary<ModDeploymentTarget, string[]>();
			private bool m_booDirty;
			private bool m_booPreparedDurably;
			private bool m_booPreparePersistenceAttempted;
			private string m_strRecoveryJournalPath;

			public VirtualDeploymentTransactionEnlistment(VirtualModActivator p_vmaOwner, Transaction p_trnTransaction, string p_strTransactionId)
			{
				m_vmaOwner = p_vmaOwner;
				m_trnTransaction = p_trnTransaction;
				m_strTransactionId = p_strTransactionId;
				m_trnTransaction.TransactionCompleted += TransactionCompleted;
				p_trnTransaction.EnlistVolatile(this, EnlistmentOptions.None);
			}

			/// <summary>
			/// Captures the pre-mutation state of one Virtual link and its deployment owner stack.
			/// </summary>
			public void Touch(IVirtualModLink p_vmlLink, ModDeploymentTarget p_mdtTarget, bool p_booWasPresent)
			{
				if (p_vmlLink == null)
					return;

				if (p_mdtTarget != null && !m_dicInitialOwnerStacks.ContainsKey(p_mdtTarget))
				{
					string[] ownerStack = m_vmaOwner.ModInstallLog.GetDeploymentOwnerKeys(p_mdtTarget).ToArray();
					m_dicInitialOwnerStacks.Add(p_mdtTarget, ownerStack);
					if (ownerStack.Length > 0)
						m_vmaOwner.ModInstallLog.EnlistDeploymentRecoveryTransaction();
				}

				if (m_dicTouchedLinks.ContainsKey(p_vmlLink))
					return;

				var snapshot = new VirtualLinkSnapshot(
					p_vmlLink,
					new VirtualModLink(p_vmlLink),
					p_booWasPresent,
					p_mdtTarget);
				m_lstTouchedLinks.Add(snapshot);
				m_dicTouchedLinks.Add(p_vmlLink, snapshot);
			}

			/// <summary>
			/// Updates the transaction-local membership state after a coordinator link add/remove.
			/// </summary>
			public void SetLinkPresent(IVirtualModLink p_vmlLink, bool p_booPresent)
			{
				VirtualLinkSnapshot snapshot;
				if (p_vmlLink != null && m_dicTouchedLinks.TryGetValue(p_vmlLink, out snapshot))
					snapshot.IsPresent = p_booPresent;
			}

			public void MarkDirty()
			{
				m_booDirty = true;
			}

			public void TouchModInfo(IVirtualModInfo p_vmiModInfo, bool p_booWasPresent)
			{
				if (p_vmiModInfo == null || m_dicTouchedModInfos.ContainsKey(p_vmiModInfo))
					return;

				var snapshot = new VirtualModInfoSnapshot(p_vmiModInfo, p_booWasPresent);
				m_lstTouchedModInfos.Add(snapshot);
				m_dicTouchedModInfos.Add(p_vmiModInfo, snapshot);
			}

			/// <summary>
			/// Updates the transaction-local membership state after a coordinator mod-info add/remove.
			/// </summary>
			public void SetModInfoPresent(IVirtualModInfo p_vmiModInfo, bool p_booPresent)
			{
				VirtualModInfoSnapshot snapshot;
				if (p_vmiModInfo != null && m_dicTouchedModInfos.TryGetValue(p_vmiModInfo, out snapshot))
					snapshot.IsPresent = p_booPresent;
			}

			public void Commit(Enlistment p_enlEnlistment)
			{
				try
				{
					if (m_booDirty && !m_booPreparedDurably && !m_vmaOwner.SaveList(false))
						Trace.TraceError("Unable to persist pure-Virtual VMA state during transaction commit.");
				}
				finally
				{
					// The recovery journal is finalized only after the transaction reaches its
					// terminal status, when the durable InstallLog outcome is known.
					Cleanup();
					p_enlEnlistment.Done();
				}
			}

			public void InDoubt(Enlistment p_enlEnlistment)
			{
				Rollback(p_enlEnlistment);
			}

			/// <summary>
			/// Finalizes or reconciles the VMA recovery journal once the transaction reaches a terminal state.
			/// </summary>
			private void TransactionCompleted(object p_objSender, EventArgs p_eaEventArgs)
			{
				m_trnTransaction.TransactionCompleted -= TransactionCompleted;
				if (m_trnTransaction.TransactionInformation.Status == TransactionStatus.Committed && m_booDirty)
				m_vmaOwner.MarkDeploymentPublicationPending();

				if (String.IsNullOrWhiteSpace(m_strRecoveryJournalPath) || !File.Exists(m_strRecoveryJournalPath))
					return;

				try
				{
					if (m_trnTransaction.TransactionInformation.Status == TransactionStatus.Committed)
						m_vmaOwner.DeleteVirtualDeploymentRecoveryJournal(m_strRecoveryJournalPath);
					else if (m_trnTransaction.TransactionInformation.Status == TransactionStatus.Aborted)
						m_vmaOwner.RecoverVirtualDeploymentTransaction(m_strRecoveryJournalPath);
				}
				catch (Exception ex)
				{
					Trace.TraceError("Unable to finalize VMA deployment recovery journal '{0}': {1}", m_strRecoveryJournalPath, ex);
				}
			}

			public void Prepare(PreparingEnlistment p_prePreparingEnlistment)
			{
				if (!m_booDirty || !HasPromotedRecoveryBoundary())
				{
					p_prePreparingEnlistment.Prepared();
					return;
				}

				try
				{
					m_strRecoveryJournalPath = m_vmaOwner.WriteVirtualDeploymentRecoveryJournal(
						m_strTransactionId, m_lstTouchedLinks, m_lstTouchedModInfos, m_dicInitialOwnerStacks);
					m_booPreparePersistenceAttempted = true;
					if (!m_vmaOwner.SaveList(false))
						throw new IOException("VMA persistence did not report a successful write during transaction prepare.");
					m_booPreparedDurably = true;
					p_prePreparingEnlistment.Prepared();
				}
				catch (Exception ex)
				{
					Trace.TraceError("Unable to persist promoted VMA state during transaction prepare: {0}", ex);
					p_prePreparingEnlistment.ForceRollback();
				}
			}

			/// <summary>
			/// Returns whether this transaction touches a target whose pre/post InstallLog owner stack is promoted.
			/// </summary>
			private bool HasPromotedRecoveryBoundary()
			{
				foreach (KeyValuePair<ModDeploymentTarget, string[]> pair in m_dicInitialOwnerStacks)
				{
					if ((pair.Value != null && pair.Value.Length > 0) ||
						m_vmaOwner.ModInstallLog.GetDeploymentOwnerKeys(pair.Key).Count > 0)
						return true;
				}
				return false;
			}

			public void Rollback(Enlistment p_enlEnlistment)
			{
				try
				{
					for (int i = m_lstTouchedLinks.Count - 1; i >= 0; i--)
						RestoreSnapshot(m_lstTouchedLinks[i]);

					for (int i = m_lstTouchedModInfos.Count - 1; i >= 0; i--)
						RestoreModInfoSnapshot(m_lstTouchedModInfos[i]);

					if (m_lstTouchedModInfos.Count > 0)
						m_vmaOwner.MarkVirtualModInfoLookupDirty();

					if (m_booPreparePersistenceAttempted)
					{
						if (!m_vmaOwner.SaveList(false))
							throw new IOException("Unable to persist compensated VMA state during transaction rollback.");
						m_vmaOwner.DeleteVirtualDeploymentRecoveryJournal(m_strRecoveryJournalPath);
					}
				}
				finally
				{
					Cleanup();
					p_enlEnlistment.Done();
				}
			}

			private void RestoreModInfoSnapshot(VirtualModInfoSnapshot p_vmsSnapshot)
			{
				if (p_vmsSnapshot.WasPresent)
				{
					if (!p_vmsSnapshot.IsPresent)
						m_vmaOwner.AddVirtualModInfo(p_vmsSnapshot.ModInfo);
				}
				else if (p_vmsSnapshot.IsPresent)
				{
					m_vmaOwner.m_tslVirtualModInfo.Remove(p_vmsSnapshot.ModInfo);
				}
			}

			private void RestoreSnapshot(VirtualLinkSnapshot p_vlsSnapshot)
			{
				if (!p_vlsSnapshot.WasPresent)
				{
					if (p_vlsSnapshot.IsPresent)
						m_vmaOwner.RemoveVirtualLink(p_vlsSnapshot.Link);
					return;
				}

				bool reindex = p_vlsSnapshot.IsPresent && RequiresReindex(p_vlsSnapshot);
				if (reindex)
				{
					m_vmaOwner.RemoveVirtualLink(p_vlsSnapshot.Link, m_vmaOwner.FindManagedMod(p_vlsSnapshot.Link.ModInfo));
					p_vlsSnapshot.IsPresent = false;
				}

				p_vlsSnapshot.Link.VirtualModPath = p_vlsSnapshot.State.VirtualModPath;
				p_vlsSnapshot.Link.RealModPath = p_vlsSnapshot.State.RealModPath;
				p_vlsSnapshot.Link.Priority = p_vlsSnapshot.State.Priority;
				p_vlsSnapshot.Link.Active = p_vlsSnapshot.State.Active;
				p_vlsSnapshot.Link.ModInfo = p_vlsSnapshot.State.ModInfo;
				p_vlsSnapshot.Link.InstallRoot = p_vlsSnapshot.State.InstallRoot;

				if (!p_vlsSnapshot.IsPresent)
				{
					m_vmaOwner.AddVirtualLink(p_vlsSnapshot.Link, m_vmaOwner.FindManagedMod(p_vlsSnapshot.State.ModInfo));
					p_vlsSnapshot.IsPresent = true;
				}
			}

			/// <summary>
			/// Returns whether restoring the snapshot changes a value used by the Virtual-link indexes.
			/// </summary>
			private static bool RequiresReindex(VirtualLinkSnapshot p_vlsSnapshot)
			{
				return !String.Equals(p_vlsSnapshot.Link.VirtualModPath, p_vlsSnapshot.State.VirtualModPath, StringComparison.OrdinalIgnoreCase) ||
					!String.Equals(p_vlsSnapshot.Link.RealModPath, p_vlsSnapshot.State.RealModPath, StringComparison.OrdinalIgnoreCase) ||
					!ReferenceEquals(p_vlsSnapshot.Link.ModInfo, p_vlsSnapshot.State.ModInfo) ||
					p_vlsSnapshot.Link.InstallRoot != p_vlsSnapshot.State.InstallRoot;
			}

			private void Cleanup()
			{
				m_lstTouchedLinks.Clear();
				m_dicTouchedLinks.Clear();
				m_lstTouchedModInfos.Clear();
				m_dicTouchedModInfos.Clear();
				m_dicInitialOwnerStacks.Clear();
				m_vmaOwner.ReleaseVirtualDeploymentEnlistment(m_strTransactionId);
			}
		}

		private sealed class VirtualModInfoSnapshot
		{
			public VirtualModInfoSnapshot(IVirtualModInfo p_vmiModInfo, bool p_booWasPresent)
			{
				ModInfo = p_vmiModInfo;
				State = p_vmiModInfo == null ? null : new VirtualModInfo(
					p_vmiModInfo.ModId, p_vmiModInfo.DownloadId, p_vmiModInfo.UpdatedDownloadId,
					p_vmiModInfo.ModName, p_vmiModInfo.ModFileName, p_vmiModInfo.NewFileName,
					p_vmiModInfo.ModFilePath, p_vmiModInfo.FileVersion);
				WasPresent = p_booWasPresent;
				IsPresent = p_booWasPresent;
			}

			public IVirtualModInfo ModInfo { get; private set; }
			public IVirtualModInfo State { get; private set; }
			public bool WasPresent { get; private set; }
			public bool IsPresent { get; set; }
		}

		private sealed class VirtualLinkSnapshot
		{
			/// <summary>
			/// Captures one Virtual-link state for rollback and crash recovery.
			/// </summary>
			public VirtualLinkSnapshot(IVirtualModLink p_vmlLink, VirtualModLink p_vmlState, bool p_booWasPresent, ModDeploymentTarget p_mdtTarget)
			{
				Link = p_vmlLink;
				State = p_vmlState;
				WasPresent = p_booWasPresent;
				IsPresent = p_booWasPresent;
				Target = p_mdtTarget;
			}

			public IVirtualModLink Link { get; private set; }
			public VirtualModLink State { get; private set; }
			public bool WasPresent { get; private set; }
			public bool IsPresent { get; set; }
			public ModDeploymentTarget Target { get; private set; }
		}
	}
}
