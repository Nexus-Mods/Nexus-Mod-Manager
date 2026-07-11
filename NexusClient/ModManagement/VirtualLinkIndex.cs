using System;
using System.Collections.Generic;

namespace Nexus.Client.ModManagement
{
	internal class VirtualLinkIndex
	{
		private readonly Dictionary<string, List<IVirtualModLink>> m_dicLinksByVirtualPath = new Dictionary<string, List<IVirtualModLink>>(StringComparer.OrdinalIgnoreCase);

		public void Rebuild(IEnumerable<IVirtualModLink> p_enmLinks)
		{
			Clear();

			if (p_enmLinks == null)
				return;

			foreach (IVirtualModLink vmlLink in p_enmLinks)
				Add(vmlLink);
		}

		public void Clear()
		{
			m_dicLinksByVirtualPath.Clear();
		}

		public void Add(IVirtualModLink p_vmlLink)
		{
			List<IVirtualModLink> lstLinks;
			if (!m_dicLinksByVirtualPath.TryGetValue(p_vmlLink.VirtualModPath, out lstLinks))
			{
				lstLinks = new List<IVirtualModLink>();
				m_dicLinksByVirtualPath.Add(p_vmlLink.VirtualModPath, lstLinks);
			}

			lstLinks.Add(p_vmlLink);
		}

		public void Remove(IVirtualModLink p_vmlLink)
		{
			List<IVirtualModLink> lstLinks;
			if (!m_dicLinksByVirtualPath.TryGetValue(p_vmlLink.VirtualModPath, out lstLinks))
				return;

			lstLinks.Remove(p_vmlLink);
			if (lstLinks.Count == 0)
				m_dicLinksByVirtualPath.Remove(p_vmlLink.VirtualModPath);
		}

		public List<IVirtualModLink> FindByVirtualPath(string p_strVirtualPath)
		{
			List<IVirtualModLink> lstLinks;
			if (m_dicLinksByVirtualPath.TryGetValue(p_strVirtualPath, out lstLinks))
				return new List<IVirtualModLink>(lstLinks);

			return new List<IVirtualModLink>();
		}
	}
}