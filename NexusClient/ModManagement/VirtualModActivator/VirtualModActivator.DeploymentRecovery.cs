namespace Nexus.Client.ModManagement
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.IO;
	using System.Linq;
	using System.Xml.Linq;

	/// <summary>
	/// Crash recovery for transaction-enlisted Virtual state used by promoted Direct/Virtual targets.
	/// </summary>
	public partial class VirtualModActivator
	{
		private const string DeploymentRecoveryFolder = "_deploymentRecovery";

		/// <summary>
		/// Writes the pre/post VMA state needed to reconcile a prepared deployment transaction after a crash.
		/// </summary>
		private string WriteVirtualDeploymentRecoveryJournal(string p_strTransactionId,
			IEnumerable<VirtualLinkSnapshot> p_enmLinks, IEnumerable<VirtualModInfoSnapshot> p_enmModInfos,
			IDictionary<ModDeploymentTarget, string[]> p_dicInitialOwnerStacks)
		{
			VirtualLinkSnapshot[] links = p_enmLinks == null ? new VirtualLinkSnapshot[0] : p_enmLinks.ToArray();
			VirtualModInfoSnapshot[] modInfos = p_enmModInfos == null ? new VirtualModInfoSnapshot[0] : p_enmModInfos.ToArray();
			ModDeploymentTarget[] targets = links
				.Where(x => x.Target != null)
				.Select(x => x.Target)
				.Distinct()
				.ToArray();

			if (targets.Length == 0)
				return null;

			string recoveryDirectory = Path.Combine(m_strVirtualActivatorPath, DeploymentRecoveryFolder);
			Directory.CreateDirectory(recoveryDirectory);
			string journalName = DateTime.UtcNow.Ticks.ToString("D19") + "-" + GetSafeRecoveryFileName(p_strTransactionId) + ".xml";
			string journalPath = Path.Combine(recoveryDirectory, journalName);
			string temporaryPath = journalPath + ".tmp";

			var document = new XDocument(
				new XElement("virtualDeploymentRecovery",
					new XAttribute("transactionId", p_strTransactionId ?? string.Empty),
					new XElement("targets", targets.Select(x => CreateRecoveryTargetElement(x, p_dicInitialOwnerStacks))),
					new XElement("modInfos", modInfos.Select(CreateRecoveryModInfoElement)),
					new XElement("links", links.Select(CreateRecoveryLinkElement))));

			if (File.Exists(temporaryPath))
				File.Delete(temporaryPath);
			document.Save(temporaryPath);
			if (File.Exists(journalPath))
				File.Delete(journalPath);
			File.Move(temporaryPath, journalPath);
			return journalPath;
		}

		/// <summary>
		/// Serializes the InstallLog owner-stack boundary for one touched deployment target.
		/// </summary>
		private XElement CreateRecoveryTargetElement(ModDeploymentTarget p_mdtTarget, IDictionary<ModDeploymentTarget, string[]> p_dicInitialOwnerStacks)
		{
			string[] beforeOwners;
			if (p_dicInitialOwnerStacks == null || !p_dicInitialOwnerStacks.TryGetValue(p_mdtTarget, out beforeOwners))
				beforeOwners = new string[0];
			string[] afterOwners = ModInstallLog.GetDeploymentOwnerKeys(p_mdtTarget).ToArray();

			return new XElement("target",
				new XAttribute("root", p_mdtTarget.Root),
				new XAttribute("path", p_mdtTarget.RelativePath),
				new XElement("beforeOwners", beforeOwners.Select(x => new XElement("owner", new XAttribute("key", x)))),
				new XElement("afterOwners", afterOwners.Select(x => new XElement("owner", new XAttribute("key", x)))));
		}

		/// <summary>
		/// Serializes one touched Virtual mod-info record before and after the transaction.
		/// </summary>
		private XElement CreateRecoveryModInfoElement(VirtualModInfoSnapshot p_vmsSnapshot)
		{
			return new XElement("modInfo",
				CreateRecoveryModInfoState("before", p_vmsSnapshot.State, p_vmsSnapshot.WasPresent),
				CreateRecoveryModInfoState("after", p_vmsSnapshot.ModInfo,
					m_tslVirtualModInfo.Any(x => ReferenceEquals(x, p_vmsSnapshot.ModInfo))));
		}

		/// <summary>
		/// Serializes one touched Virtual link before and after the transaction.
		/// </summary>
		private XElement CreateRecoveryLinkElement(VirtualLinkSnapshot p_vlsSnapshot)
		{
			return new XElement("link",
				p_vlsSnapshot.Target == null ? null : new XAttribute("root", p_vlsSnapshot.Target.Root),
				p_vlsSnapshot.Target == null ? null : new XAttribute("path", p_vlsSnapshot.Target.RelativePath),
				CreateRecoveryLinkState("before", p_vlsSnapshot.State, p_vlsSnapshot.WasPresent),
				CreateRecoveryLinkState("after", p_vlsSnapshot.Link,
					m_tslVirtualModList.Any(x => ReferenceEquals(x, p_vlsSnapshot.Link))));
		}

		/// <summary>
		/// Serializes one side of a Virtual mod-info recovery record.
		/// </summary>
		private static XElement CreateRecoveryModInfoState(string p_strName, IVirtualModInfo p_vmiModInfo, bool p_booPresent)
		{
			var element = new XElement(p_strName, new XAttribute("present", p_booPresent));
			if (!p_booPresent || p_vmiModInfo == null)
				return element;

			element.Add(
				new XAttribute("modId", p_vmiModInfo.ModId ?? string.Empty),
				new XAttribute("downloadId", p_vmiModInfo.DownloadId ?? string.Empty),
				new XAttribute("updatedDownloadId", p_vmiModInfo.UpdatedDownloadId ?? string.Empty),
				new XAttribute("modName", p_vmiModInfo.ModName ?? string.Empty),
				new XAttribute("modFileName", p_vmiModInfo.ModFileName ?? string.Empty),
				new XAttribute("newFileName", p_vmiModInfo.NewFileName ?? string.Empty),
				new XAttribute("modFilePath", p_vmiModInfo.ModFilePath ?? string.Empty),
				new XAttribute("fileVersion", p_vmiModInfo.FileVersion ?? string.Empty));
			return element;
		}

		/// <summary>
		/// Serializes one side of a Virtual-link recovery record.
		/// </summary>
		private static XElement CreateRecoveryLinkState(string p_strName, IVirtualModLink p_vmlLink, bool p_booPresent)
		{
			var element = new XElement(p_strName, new XAttribute("present", p_booPresent));
			if (!p_booPresent || p_vmlLink == null)
				return element;

			element.Add(
				new XAttribute("virtualPath", p_vmlLink.VirtualModPath ?? string.Empty),
				new XAttribute("realPath", p_vmlLink.RealModPath ?? string.Empty),
				new XAttribute("priority", p_vmlLink.Priority),
				new XAttribute("active", p_vmlLink.Active),
				new XAttribute("installRoot", p_vmlLink.InstallRoot),
				new XAttribute("modId", p_vmlLink.ModInfo == null ? string.Empty : p_vmlLink.ModInfo.ModId ?? string.Empty),
				new XAttribute("downloadId", p_vmlLink.ModInfo == null ? string.Empty : p_vmlLink.ModInfo.DownloadId ?? string.Empty),
				new XAttribute("updatedDownloadId", p_vmlLink.ModInfo == null ? string.Empty : p_vmlLink.ModInfo.UpdatedDownloadId ?? string.Empty),
				new XAttribute("modName", p_vmlLink.ModInfo == null ? string.Empty : p_vmlLink.ModInfo.ModName ?? string.Empty),
				new XAttribute("modFileName", p_vmlLink.ModInfo == null ? string.Empty : p_vmlLink.ModInfo.ModFileName ?? string.Empty),
				new XAttribute("newFileName", p_vmlLink.ModInfo == null ? string.Empty : p_vmlLink.ModInfo.NewFileName ?? string.Empty),
				new XAttribute("modFilePath", p_vmlLink.ModInfo == null ? string.Empty : p_vmlLink.ModInfo.ModFilePath ?? string.Empty),
				new XAttribute("fileVersion", p_vmlLink.ModInfo == null ? string.Empty : p_vmlLink.ModInfo.FileVersion ?? string.Empty));
			return element;
		}

		/// <summary>
		/// Reconciles any prepared VMA deployment journals left by an interrupted transaction.
		/// </summary>
		private void RecoverPendingVirtualDeploymentTransactions()
		{
			string recoveryDirectory = Path.Combine(m_strVirtualActivatorPath, DeploymentRecoveryFolder);
			if (!Directory.Exists(recoveryDirectory))
				return;

			foreach (string journalPath in Directory.GetFiles(recoveryDirectory, "*.xml").OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase))
			{
				try
				{
					RecoverVirtualDeploymentTransaction(journalPath);
				}
				catch (Exception ex)
				{
					Trace.TraceError("Unable to reconcile Virtual deployment recovery journal '{0}': {1}", journalPath, ex);
				}
			}
		}

		/// <summary>
		/// Reconciles one pending VMA journal against the durable InstallLog owner state.
		/// </summary>
		private void RecoverVirtualDeploymentTransaction(string p_strJournalPath)
		{
			XDocument document = XDocument.Load(p_strJournalPath);
			XElement root = document.Root;
			if (root == null || !string.Equals(root.Name.LocalName, "virtualDeploymentRecovery", StringComparison.Ordinal))
				throw new InvalidDataException("Invalid Virtual deployment recovery journal.");

			XElement targetsElement = root.Element("targets");
			XElement[] targetRecords = targetsElement == null ? new XElement[0] : targetsElement.Elements("target").ToArray();
			bool usePostState = ResolveRecoveryOutcome(targetRecords);
			string stateName = usePostState ? "after" : "before";

			XElement modInfosElement = root.Element("modInfos");
			XElement[] modInfoRecords = modInfosElement == null ? new XElement[0] : modInfosElement.Elements("modInfo").ToArray();
			foreach (XElement record in modInfoRecords)
				EnsureRecoveryModInfoState(record.Element(stateName));

			XElement linksElement = root.Element("links");
			XElement[] linkRecords = linksElement == null ? new XElement[0] : linksElement.Elements("link").ToArray();
			foreach (XElement record in linkRecords)
				ApplyRecoveryLinkState(record, stateName);

			foreach (XElement record in modInfoRecords)
				RemoveRecoveryModInfoForAbsentState(record.Element(stateName), record);

			MarkVirtualModInfoLookupDirty();
			MarkVirtualLinkIndexDirty();
			RebuildVirtualLinkIndex();
			if (!SaveList(false))
				throw new IOException("Unable to persist reconciled Virtual deployment state.");
			File.Delete(p_strJournalPath);
		}

		/// <summary>
		/// Determines whether a pending recovery journal represents the durable pre-transaction or post-transaction state.
		/// </summary>
		private bool ResolveRecoveryOutcome(IEnumerable<XElement> p_enmTargetRecords)
		{
			bool sawPreState = false;
			bool sawPostState = false;
			foreach (XElement record in p_enmTargetRecords ?? Enumerable.Empty<XElement>())
			{
				ModDeploymentTarget target = ReadRecoveryTarget(record);
				string[] currentOwners = ModInstallLog.GetDeploymentOwnerKeys(target).ToArray();
				string[] beforeOwners = ReadRecoveryOwners(record.Element("beforeOwners"));
				string[] afterOwners = ReadRecoveryOwners(record.Element("afterOwners"));

				if (beforeOwners.SequenceEqual(afterOwners, StringComparer.OrdinalIgnoreCase))
					continue;

				bool matchesBefore = currentOwners.SequenceEqual(beforeOwners, StringComparer.OrdinalIgnoreCase);
				bool matchesAfter = currentOwners.SequenceEqual(afterOwners, StringComparer.OrdinalIgnoreCase);
				if (matchesBefore == matchesAfter)
					throw new InvalidDataException(string.Format("InstallLog deployment state for '{0}' does not match either side of the pending VMA recovery transaction.", target));

				sawPreState |= matchesBefore;
				sawPostState |= matchesAfter;
			}

			if (sawPreState && sawPostState)
				throw new InvalidDataException("InstallLog contains a mixed pre/post state for a pending VMA recovery transaction.");

			// VMA is durably written during Prepare. A promoted reinstall can leave the owner
			// stack unchanged on both sides; in that case retain the prepared VMA state, which
			// reflects the filesystem mutations already issued by the operation.
			return !sawPreState;
		}

		/// <summary>
		/// Reads one persisted ordered owner stack from a recovery journal.
		/// </summary>
		private static string[] ReadRecoveryOwners(XElement p_xelOwners)
		{
			return p_xelOwners == null
				? new string[0]
				: p_xelOwners.Elements("owner").Select(x => (string)x.Attribute("key") ?? string.Empty).ToArray();
		}

		/// <summary>
		/// Reconstructs a canonical deployment target from a recovery journal record.
		/// </summary>
		private static ModDeploymentTarget ReadRecoveryTarget(XElement p_xelTarget)
		{
			ModDeploymentRoot root;
			if (!Enum.TryParse((string)p_xelTarget.Attribute("root"), true, out root))
				throw new InvalidDataException("Invalid deployment root in Virtual recovery journal.");
			return ModDeploymentTargetResolver.FromCanonical(root, (string)p_xelTarget.Attribute("path"));
		}

		/// <summary>
		/// Applies the selected pre/post state for one Virtual link.
		/// </summary>
		private void ApplyRecoveryLinkState(XElement p_xelRecord, string p_strStateName)
		{
			XElement state = p_xelRecord.Element(p_strStateName);
			XElement alternateState = p_xelRecord.Element(p_strStateName == "after" ? "before" : "after");
			string modFileName = RecoveryStatePresent(state)
				? (string)state.Attribute("modFileName")
				: (string)(alternateState == null ? null : alternateState.Attribute("modFileName"));
			ModDeploymentTarget target = ReadRecoveryTarget(p_xelRecord);
			IVirtualModLink current = GetVirtualOwnerLinksForTarget(target)
				.FirstOrDefault(x => x.ModInfo != null && string.Equals(x.ModInfo.ModFileName, modFileName, StringComparison.OrdinalIgnoreCase));

			if (!RecoveryStatePresent(state))
			{
				if (current != null)
					RemoveVirtualLink(current, FindManagedMod(current.ModInfo));
				return;
			}

			IVirtualModInfo modInfo = EnsureRecoveryModInfoByFileName(modFileName, state);
			if (current == null)
			{
				current = new VirtualModLink(
					(string)state.Attribute("realPath"),
					(string)state.Attribute("virtualPath"),
					(int)state.Attribute("priority"),
					(bool)state.Attribute("active"),
					modInfo,
					(ModInstallRoot)Enum.Parse(typeof(ModInstallRoot), (string)state.Attribute("installRoot"), true));
				AddVirtualLink(current, FindManagedMod(modInfo));
				return;
			}

			current.RealModPath = (string)state.Attribute("realPath");
			current.VirtualModPath = (string)state.Attribute("virtualPath");
			current.Priority = (int)state.Attribute("priority");
			current.Active = (bool)state.Attribute("active");
			current.InstallRoot = (ModInstallRoot)Enum.Parse(typeof(ModInstallRoot), (string)state.Attribute("installRoot"), true);
			current.ModInfo = modInfo;
		}

		/// <summary>
		/// Ensures a Virtual mod-info record required by the selected recovery state exists.
		/// </summary>
		private void EnsureRecoveryModInfoState(XElement p_xelState)
		{
			if (!RecoveryStatePresent(p_xelState))
				return;
			EnsureRecoveryModInfoByFileName((string)p_xelState.Attribute("modFileName"), p_xelState);
		}

		/// <summary>
		/// Finds or recreates the Virtual mod-info referenced by a recovery state.
		/// </summary>
		private IVirtualModInfo EnsureRecoveryModInfoByFileName(string p_strModFileName, XElement p_xelState)
		{
			if (String.IsNullOrWhiteSpace(p_strModFileName) || p_xelState == null)
				throw new InvalidDataException("Virtual recovery state is missing its mod filename.");

			IVirtualModInfo existing = FindVirtualModInfoByFileName(p_strModFileName);
			if (existing != null)
			{
				VirtualModInfo mutable = existing as VirtualModInfo;
				if (mutable != null)
				{
					mutable.ModId = (string)p_xelState.Attribute("modId") ?? mutable.ModId;
					mutable.DownloadId = (string)p_xelState.Attribute("downloadId") ?? mutable.DownloadId;
					mutable.UpdatedDownloadId = (string)p_xelState.Attribute("updatedDownloadId") ?? mutable.UpdatedDownloadId;
					mutable.ModName = (string)p_xelState.Attribute("modName") ?? mutable.ModName;
					mutable.NewFileName = (string)p_xelState.Attribute("newFileName") ?? mutable.NewFileName;
					mutable.ModFilePath = (string)p_xelState.Attribute("modFilePath") ?? mutable.ModFilePath;
				}
				return existing;
			}

			var modInfo = new VirtualModInfo(
				(string)p_xelState.Attribute("modId"),
				(string)p_xelState.Attribute("downloadId"),
				(string)p_xelState.Attribute("updatedDownloadId"),
				(string)p_xelState.Attribute("modName"),
				p_strModFileName,
				(string)p_xelState.Attribute("newFileName"),
				(string)p_xelState.Attribute("modFilePath"),
				(string)p_xelState.Attribute("fileVersion"));
			AddVirtualModInfo(modInfo);
			return modInfo;
		}

		/// <summary>
		/// Removes a Virtual mod-info record that must not exist in the selected recovery state.
		/// </summary>
		private void RemoveRecoveryModInfoForAbsentState(XElement p_xelState, XElement p_xelRecord)
		{
			if (RecoveryStatePresent(p_xelState))
				return;

			XElement alternate = p_xelRecord == null ? null : p_xelRecord.Elements().FirstOrDefault(x => x != p_xelState && RecoveryStatePresent(x));
			string modFileName = alternate == null ? null : (string)alternate.Attribute("modFileName");
			if (String.IsNullOrWhiteSpace(modFileName))
				return;

			IVirtualModInfo modInfo = FindVirtualModInfoByFileName(modFileName);
			if (modInfo == null || m_tslVirtualModList.Any(x => ReferenceEquals(x.ModInfo, modInfo)))
				return;
			m_tslVirtualModInfo.Remove(modInfo);
			MarkVirtualModInfoLookupDirty();
		}

		/// <summary>
		/// Gets whether a serialized recovery side represents a present object.
		/// </summary>
		private static bool RecoveryStatePresent(XElement p_xelState)
		{
			return p_xelState != null && ((bool?)p_xelState.Attribute("present") ?? false);
		}

		/// <summary>
		/// Deletes a recovery journal after the transaction outcome has been made durable.
		/// </summary>
		private void DeleteVirtualDeploymentRecoveryJournal(string p_strJournalPath)
		{
			if (!string.IsNullOrWhiteSpace(p_strJournalPath) && File.Exists(p_strJournalPath))
				File.Delete(p_strJournalPath);
		}

		/// <summary>
		/// Converts a transaction identifier into a filesystem-safe journal filename.
		/// </summary>
		private static string GetSafeRecoveryFileName(string p_strTransactionId)
		{
			string value = string.IsNullOrWhiteSpace(p_strTransactionId) ? Guid.NewGuid().ToString("N") : p_strTransactionId;
			foreach (char invalid in Path.GetInvalidFileNameChars())
				value = value.Replace(invalid, '_');
			return value;
		}
	}
}
