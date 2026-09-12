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
	/// plugin changes, and immediate VMA persistence; touched VMA state is persisted once at transaction commit.
	/// </summary>
	public partial class VirtualModActivator
	{
		private readonly object m_objDeploymentTransactionLock = new object();
		private Dictionary<string, VirtualDeploymentTransactionEnlistment> m_dicDeploymentTransactionEnlistments;

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
				enlistment.TouchModInfo(modInfo);
				AddVirtualModInfo(modInfo);
			}

			var link = new VirtualModLink(realPath, p_strLogicalPath, p_intPriority, false, modInfo, p_mirInstallRoot);
			enlistment.Touch(link);
			AddVirtualLink(link, p_modMod);
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
			enlistment.Touch(link);

			string deployedPath = GetDeploymentPathForTarget(p_mdtTarget);
			if (link.Active && File.Exists(deployedPath))
				p_tfmFileManager.Delete(deployedPath);

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
			enlistment.Touch(link);

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
		public void RemoveVirtualLinkRecord(ModDeploymentTarget p_mdtTarget, string p_strOwnerKey)
		{
			IVirtualModLink link = RequireVirtualOwnerLink(p_mdtTarget, p_strOwnerKey);
			VirtualDeploymentTransactionEnlistment enlistment = GetVirtualDeploymentEnlistment();
			enlistment.Touch(link);

			RemoveVirtualLink(link, FindManagedMod(link.ModInfo));
			enlistment.MarkDirty();
		}

		/// <inheritdoc />
		public void SetVirtualLinkActiveState(ModDeploymentTarget p_mdtTarget, string p_strOwnerKey, bool p_booActive)
		{
			IVirtualModLink link = RequireVirtualOwnerLink(p_mdtTarget, p_strOwnerKey);
			if (link.Active == p_booActive)
				return;

			VirtualDeploymentTransactionEnlistment enlistment = GetVirtualDeploymentEnlistment();
			enlistment.Touch(link);
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
				enlistment.TouchModInfo(modInfo);
				m_tslVirtualModInfo.Remove(modInfo);
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

					VirtualLinkIndexBucket bucket = m_vliVirtualLinkIndex.FindByOwnerKey(p_strOwnerKey);
					return bucket == null ? new IVirtualModLink[0] : bucket.ToArray();
				}
			}
		}

		private string GetDeploymentPathForTarget(ModDeploymentTarget p_mdtTarget)
		{
			if (p_mdtTarget == null)
				throw new ArgumentNullException(nameof(p_mdtTarget));

			string rootPath;
			switch (p_mdtTarget.Root)
			{
				case ModDeploymentRoot.Data:
					rootPath = m_strGameDataPath;
					break;
				case ModDeploymentRoot.GameRoot:
					rootPath = GameMode.InstallationPath;
					break;
				case ModDeploymentRoot.Secondary:
					rootPath = GameMode.SecondaryInstallationPath;
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(p_mdtTarget));
			}

			if (string.IsNullOrWhiteSpace(rootPath))
				throw new InvalidOperationException(string.Format("Deployment root '{0}' is not configured for the current game mode.", p_mdtTarget.Root));

			return Path.GetFullPath(Path.Combine(rootPath, p_mdtTarget.RelativePath));
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

		private sealed class VirtualDeploymentTransactionEnlistment : IEnlistmentNotification
		{
			private readonly VirtualModActivator m_vmaOwner;
			private readonly string m_strTransactionId;
			private readonly List<VirtualLinkSnapshot> m_lstTouchedLinks = new List<VirtualLinkSnapshot>();
			private readonly List<VirtualModInfoSnapshot> m_lstTouchedModInfos = new List<VirtualModInfoSnapshot>();
			private bool m_booDirty;

			public VirtualDeploymentTransactionEnlistment(VirtualModActivator p_vmaOwner, Transaction p_trnTransaction, string p_strTransactionId)
			{
				m_vmaOwner = p_vmaOwner;
				m_strTransactionId = p_strTransactionId;
				p_trnTransaction.EnlistVolatile(this, EnlistmentOptions.None);
			}

			public void Touch(IVirtualModLink p_vmlLink)
			{
				if (p_vmlLink == null || m_lstTouchedLinks.Any(x => ReferenceEquals(x.Link, p_vmlLink)))
					return;

				m_lstTouchedLinks.Add(new VirtualLinkSnapshot(
					p_vmlLink,
					new VirtualModLink(p_vmlLink),
					m_vmaOwner.m_tslVirtualModList.Any(x => ReferenceEquals(x, p_vmlLink))));
			}

			public void MarkDirty()
			{
				m_booDirty = true;
			}

			public void TouchModInfo(IVirtualModInfo p_vmiModInfo)
			{
				if (p_vmiModInfo == null || m_lstTouchedModInfos.Any(x => ReferenceEquals(x.ModInfo, p_vmiModInfo)))
					return;

				m_lstTouchedModInfos.Add(new VirtualModInfoSnapshot(
					p_vmiModInfo,
					m_vmaOwner.m_tslVirtualModInfo.Any(x => ReferenceEquals(x, p_vmiModInfo))));
			}

			public void Commit(Enlistment p_enlEnlistment)
			{
				try
				{
					if (m_booDirty && !m_vmaOwner.SaveList(true))
						Trace.TraceError("The Virtual deployment transaction committed in memory but VMA persistence did not report a successful write.");
				}
				finally
				{
					Cleanup();
					p_enlEnlistment.Done();
				}
			}

			public void InDoubt(Enlistment p_enlEnlistment)
			{
				Rollback(p_enlEnlistment);
			}

			public void Prepare(PreparingEnlistment p_prePreparingEnlistment)
			{
				p_prePreparingEnlistment.Prepared();
			}

			public void Rollback(Enlistment p_enlEnlistment)
			{
				try
				{
					for (int i = m_lstTouchedLinks.Count - 1; i >= 0; i--)
						RestoreSnapshot(m_lstTouchedLinks[i]);

					for (int i = m_lstTouchedModInfos.Count - 1; i >= 0; i--)
						RestoreModInfoSnapshot(m_lstTouchedModInfos[i]);

					if (m_lstTouchedLinks.Count > 0 || m_lstTouchedModInfos.Count > 0)
					{
						m_vmaOwner.MarkVirtualModInfoLookupDirty();
						m_vmaOwner.MarkVirtualLinkIndexDirty();
						m_vmaOwner.RebuildVirtualLinkIndex();
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
				bool currentlyPresent = m_vmaOwner.m_tslVirtualModInfo.Any(x => ReferenceEquals(x, p_vmsSnapshot.ModInfo));
				if (p_vmsSnapshot.WasPresent)
				{
					if (!currentlyPresent)
						m_vmaOwner.AddVirtualModInfo(p_vmsSnapshot.ModInfo);
				}
				else if (currentlyPresent)
				{
					m_vmaOwner.m_tslVirtualModInfo.Remove(p_vmsSnapshot.ModInfo);
				}
			}

			private void RestoreSnapshot(VirtualLinkSnapshot p_vlsSnapshot)
			{
				bool currentlyPresent = m_vmaOwner.m_tslVirtualModList.Any(x => ReferenceEquals(x, p_vlsSnapshot.Link));
				if (!p_vlsSnapshot.WasPresent)
				{
					if (currentlyPresent)
						m_vmaOwner.RemoveVirtualLink(p_vlsSnapshot.Link);
					return;
				}

				if (!currentlyPresent)
					m_vmaOwner.AddVirtualLink(p_vlsSnapshot.Link, m_vmaOwner.FindManagedMod(p_vlsSnapshot.State.ModInfo));

				p_vlsSnapshot.Link.VirtualModPath = p_vlsSnapshot.State.VirtualModPath;
				p_vlsSnapshot.Link.RealModPath = p_vlsSnapshot.State.RealModPath;
				p_vlsSnapshot.Link.Priority = p_vlsSnapshot.State.Priority;
				p_vlsSnapshot.Link.Active = p_vlsSnapshot.State.Active;
				p_vlsSnapshot.Link.ModInfo = p_vlsSnapshot.State.ModInfo;
				p_vlsSnapshot.Link.InstallRoot = p_vlsSnapshot.State.InstallRoot;
			}

			private void Cleanup()
			{
				m_lstTouchedLinks.Clear();
				m_lstTouchedModInfos.Clear();
				m_vmaOwner.ReleaseVirtualDeploymentEnlistment(m_strTransactionId);
			}
		}

		private sealed class VirtualModInfoSnapshot
		{
			public VirtualModInfoSnapshot(IVirtualModInfo p_vmiModInfo, bool p_booWasPresent)
			{
				ModInfo = p_vmiModInfo;
				WasPresent = p_booWasPresent;
			}

			public IVirtualModInfo ModInfo { get; private set; }
			public bool WasPresent { get; private set; }
		}

		private sealed class VirtualLinkSnapshot
		{
			public VirtualLinkSnapshot(IVirtualModLink p_vmlLink, VirtualModLink p_vmlState, bool p_booWasPresent)
			{
				Link = p_vmlLink;
				State = p_vmlState;
				WasPresent = p_booWasPresent;
			}

			public IVirtualModLink Link { get; private set; }
			public VirtualModLink State { get; private set; }
			public bool WasPresent { get; private set; }
		}
	}
}
