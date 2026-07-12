namespace Nexus.Client.ModManagement
{
	using System.Collections.Generic;

	public enum VirtualInstallReconciliationIssueKind
	{
		MissingModInfo,
		ModInfoWithoutLinks,
		DuplicateVirtualPath,
		MissingSourceFile,
		MissingDeployedActiveFile,
		MissingInactiveSourceFile,
		EmptyRealPath,
		EmptyVirtualPath,
		NullVirtualLink,
		MetadataInconsistency
	}

	public sealed class VirtualInstallReconciliationIssue
	{
		internal VirtualInstallReconciliationIssue(VirtualInstallReconciliationIssueKind kind, string message, string modFileName, string realPath, string virtualPath, int? priority, bool? active, bool canRepairAutomatically)
		{
			Kind = kind;
			Message = message;
			ModFileName = modFileName;
			RealPath = realPath;
			VirtualPath = virtualPath;
			Priority = priority;
			Active = active;
			CanRepairAutomatically = canRepairAutomatically;
		}

		public VirtualInstallReconciliationIssueKind Kind { get; private set; }

		public string Message { get; private set; }

		public string ModFileName { get; private set; }

		public string RealPath { get; private set; }

		public string VirtualPath { get; private set; }

		public int? Priority { get; private set; }

		public bool? Active { get; private set; }

		public bool CanRepairAutomatically { get; private set; }
	}

	public sealed class VirtualInstallReconciliationReport
	{
		private readonly List<VirtualInstallReconciliationIssue> m_lstIssues = new List<VirtualInstallReconciliationIssue>();
		private readonly List<string> m_lstRepairsApplied = new List<string>();

		public IList<VirtualInstallReconciliationIssue> Issues
		{
			get
			{
				return m_lstIssues.AsReadOnly();
			}
		}

		public IList<string> RepairsApplied
		{
			get
			{
				return m_lstRepairsApplied.AsReadOnly();
			}
		}

		public bool HasIssues
		{
			get
			{
				return m_lstIssues.Count > 0;
			}
		}

		public bool HasRepairs
		{
			get
			{
				return m_lstRepairsApplied.Count > 0;
			}
		}

		internal void AddIssue(VirtualInstallReconciliationIssueKind kind, string message, IVirtualModLink link, bool canRepairAutomatically)
		{
			AddIssue(kind, message, GetModFileName(link), GetRealPath(link), GetVirtualPath(link), GetPriority(link), GetActive(link), canRepairAutomatically);
		}

		internal void AddIssue(VirtualInstallReconciliationIssueKind kind, string message, IVirtualModInfo modInfo, bool canRepairAutomatically)
		{
			AddIssue(kind, message, GetModFileName(modInfo), null, null, null, null, canRepairAutomatically);
		}

		internal void AddIssue(VirtualInstallReconciliationIssueKind kind, string message, string modFileName, string realPath, string virtualPath, int? priority, bool? active, bool canRepairAutomatically)
		{
			m_lstIssues.Add(new VirtualInstallReconciliationIssue(kind, message, modFileName, realPath, virtualPath, priority, active, canRepairAutomatically));
		}

		internal void AddRepair(string message)
		{
			m_lstRepairsApplied.Add(message);
		}

		private static string GetModFileName(IVirtualModLink link)
		{
			return link == null || link.ModInfo == null ? null : link.ModInfo.ModFileName;
		}

		private static string GetModFileName(IVirtualModInfo modInfo)
		{
			return modInfo == null ? null : modInfo.ModFileName;
		}

		private static string GetRealPath(IVirtualModLink link)
		{
			return link == null ? null : link.RealModPath;
		}

		private static string GetVirtualPath(IVirtualModLink link)
		{
			return link == null ? null : link.VirtualModPath;
		}

		private static int? GetPriority(IVirtualModLink link)
		{
			return link == null ? (int?)null : link.Priority;
		}

		private static bool? GetActive(IVirtualModLink link)
		{
			return link == null ? (bool?)null : link.Active;
		}
	}
}