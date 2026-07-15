using System;
using System.Collections.Generic;

namespace Nexus.Client.ModManagement
{
	internal class VirtualLinkIndex
	{
		private readonly Dictionary<string, List<IVirtualModLink>> m_dicLinksByVirtualPath = new Dictionary<string, List<IVirtualModLink>>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, List<IVirtualModLink>> m_dicLinksByDeploymentPath = new Dictionary<string, List<IVirtualModLink>>(StringComparer.OrdinalIgnoreCase);

		public void Rebuild(IEnumerable<IVirtualModLink> p_enmLinks)
		{
			Rebuild(p_enmLinks, null);
		}

		public void Rebuild(IEnumerable<IVirtualModLink> p_enmLinks, Func<IVirtualModLink, IEnumerable<string>> p_dlgDeploymentPathKeyFactory)
		{
			Clear();

			if (p_enmLinks == null)
				return;

			foreach (IVirtualModLink vmlLink in p_enmLinks)
				Add(vmlLink, GetDeploymentPathKeys(p_dlgDeploymentPathKeyFactory, vmlLink));
		}

		public void Clear()
		{
			m_dicLinksByVirtualPath.Clear();
			m_dicLinksByDeploymentPath.Clear();
		}

		public void Add(IVirtualModLink p_vmlLink)
		{
			Add(p_vmlLink, null);
		}

		public void Add(IVirtualModLink p_vmlLink, IEnumerable<string> p_enmDeploymentPathKeys)
		{
			if (p_vmlLink == null)
				return;

			Add(m_dicLinksByVirtualPath, p_vmlLink.VirtualModPath, p_vmlLink);

			if (p_enmDeploymentPathKeys == null)
				return;

			foreach (string strDeploymentPathKey in p_enmDeploymentPathKeys)
				Add(m_dicLinksByDeploymentPath, strDeploymentPathKey, p_vmlLink);
		}

		public void Remove(IVirtualModLink p_vmlLink)
		{
			Remove(p_vmlLink, null);
		}

		public void Remove(IVirtualModLink p_vmlLink, IEnumerable<string> p_enmDeploymentPathKeys)
		{
			if (p_vmlLink == null)
				return;

			Remove(m_dicLinksByVirtualPath, p_vmlLink.VirtualModPath, p_vmlLink);

			if (p_enmDeploymentPathKeys == null)
				return;

			foreach (string strDeploymentPathKey in p_enmDeploymentPathKeys)
				Remove(m_dicLinksByDeploymentPath, strDeploymentPathKey, p_vmlLink);
		}

		public List<IVirtualModLink> FindByVirtualPath(string p_strVirtualPath)
		{
			return Find(m_dicLinksByVirtualPath, p_strVirtualPath);
		}

		public List<IVirtualModLink> FindByDeploymentPath(string p_strDeploymentPathKey)
		{
			return Find(m_dicLinksByDeploymentPath, p_strDeploymentPathKey);
		}

		private static IEnumerable<string> GetDeploymentPathKeys(Func<IVirtualModLink, IEnumerable<string>> p_dlgDeploymentPathKeyFactory, IVirtualModLink p_vmlLink)
		{
			return p_dlgDeploymentPathKeyFactory == null ? null : p_dlgDeploymentPathKeyFactory(p_vmlLink);
		}

		private static void Add(Dictionary<string, List<IVirtualModLink>> p_dicIndex, string p_strKey, IVirtualModLink p_vmlLink)
		{
			if (string.IsNullOrEmpty(p_strKey))
				return;

			List<IVirtualModLink> lstLinks;
			if (!p_dicIndex.TryGetValue(p_strKey, out lstLinks))
			{
				lstLinks = new List<IVirtualModLink>();
				p_dicIndex.Add(p_strKey, lstLinks);
			}

			lstLinks.Add(p_vmlLink);
		}

		private static void Remove(Dictionary<string, List<IVirtualModLink>> p_dicIndex, string p_strKey, IVirtualModLink p_vmlLink)
		{
			if (string.IsNullOrEmpty(p_strKey))
				return;

			List<IVirtualModLink> lstLinks;
			if (!p_dicIndex.TryGetValue(p_strKey, out lstLinks))
				return;

			lstLinks.Remove(p_vmlLink);
			if (lstLinks.Count == 0)
				p_dicIndex.Remove(p_strKey);
		}

		private static List<IVirtualModLink> Find(Dictionary<string, List<IVirtualModLink>> p_dicIndex, string p_strKey)
		{
			List<IVirtualModLink> lstLinks;
			if (!string.IsNullOrEmpty(p_strKey) && p_dicIndex.TryGetValue(p_strKey, out lstLinks))
				return new List<IVirtualModLink>(lstLinks);

			return new List<IVirtualModLink>();
		}
	}
}