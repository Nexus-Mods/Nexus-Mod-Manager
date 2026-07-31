using System;
using System.Collections.Generic;
using System.IO;

namespace Nexus.Client.ModManagement
{
	internal sealed class VirtualLinkIndexBucket
	{
		private IVirtualModLink m_vmlFirst;
		private List<IVirtualModLink> m_lstAdditional;

		public int Count
		{
			get
			{
				return m_vmlFirst == null ? 0 : 1 + (m_lstAdditional == null ? 0 : m_lstAdditional.Count);
			}
		}

		public IVirtualModLink this[int p_intIndex]
		{
			get
			{
				if (p_intIndex < 0 || p_intIndex >= Count)
					throw new ArgumentOutOfRangeException("p_intIndex");

				return p_intIndex == 0 ? m_vmlFirst : m_lstAdditional[p_intIndex - 1];
			}
		}

		public void Add(IVirtualModLink p_vmlLink)
		{
			if (p_vmlLink == null)
				return;

			if (m_vmlFirst == null)
			{
				m_vmlFirst = p_vmlLink;
				return;
			}

			if (m_lstAdditional == null)
				m_lstAdditional = new List<IVirtualModLink>(1);

			m_lstAdditional.Add(p_vmlLink);
		}

		public bool Remove(IVirtualModLink p_vmlLink)
		{
			if (p_vmlLink == null || m_vmlFirst == null)
				return false;

			EqualityComparer<IVirtualModLink> comparer = EqualityComparer<IVirtualModLink>.Default;
			if (comparer.Equals(m_vmlFirst, p_vmlLink))
			{
				if (m_lstAdditional == null || m_lstAdditional.Count == 0)
				{
					m_vmlFirst = null;
					return true;
				}

				m_vmlFirst = m_lstAdditional[0];
				m_lstAdditional.RemoveAt(0);
				if (m_lstAdditional.Count == 0)
					m_lstAdditional = null;
				return true;
			}

			if (m_lstAdditional == null || !m_lstAdditional.Remove(p_vmlLink))
				return false;

			if (m_lstAdditional.Count == 0)
				m_lstAdditional = null;
			return true;
		}
	}

	internal class VirtualLinkIndex
	{
		private Dictionary<string, VirtualLinkIndexBucket> m_dicLinksByVirtualPath;
		private Dictionary<string, VirtualLinkIndexBucket> m_dicLinksByFileName;
		private Dictionary<string, VirtualLinkIndexBucket> m_dicLinksByDeploymentPath;
		private int m_intReservedLinkCount;

		public VirtualLinkIndex()
			: this(0)
		{
		}

		public VirtualLinkIndex(int p_intExpectedLinkCount)
		{
			int expectedLinkCount = Math.Max(0, p_intExpectedLinkCount);
			m_dicLinksByVirtualPath = CreateIndex(expectedLinkCount);
			m_dicLinksByFileName = CreateIndex(expectedLinkCount);
			m_dicLinksByDeploymentPath = CreateIndex(GetDeploymentCapacity(expectedLinkCount));
			m_intReservedLinkCount = expectedLinkCount;
		}

		public void EnsureCapacity(int p_intExpectedLinkCount)
		{
			int expectedLinkCount = Math.Max(0, p_intExpectedLinkCount);
			if (expectedLinkCount <= m_intReservedLinkCount)
				return;

			int growth = Math.Max(256, m_intReservedLinkCount / 8);
			int reservedLinkCount = expectedLinkCount;
			if (m_intReservedLinkCount <= Int32.MaxValue - growth)
				reservedLinkCount = Math.Max(expectedLinkCount, m_intReservedLinkCount + growth);

			m_dicLinksByVirtualPath = CopyIndex(m_dicLinksByVirtualPath, reservedLinkCount);
			m_dicLinksByFileName = CopyIndex(m_dicLinksByFileName, reservedLinkCount);
			m_dicLinksByDeploymentPath = CopyIndex(m_dicLinksByDeploymentPath, GetDeploymentCapacity(reservedLinkCount));
			m_intReservedLinkCount = reservedLinkCount;
		}

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

		/// <summary>
		/// Clears all virtual-link lookup indexes.
		/// </summary>
		public void Clear()
		{
			m_dicLinksByVirtualPath.Clear();
			m_dicLinksByFileName.Clear();
			m_dicLinksByDeploymentPath.Clear();
		}

		public void Add(IVirtualModLink p_vmlLink)
		{
			Add(p_vmlLink, null, null);
		}

		/// <summary>
		/// Adds a virtual link and its deployment keys to the indexes.
		/// </summary>
		/// <param name="p_vmlLink">The virtual link to add.</param>
		/// <param name="p_enmDeploymentPathKeys">The deployment path keys associated with the link.</param>
		public void Add(IVirtualModLink p_vmlLink, IEnumerable<string> p_enmDeploymentPathKeys)
		{
			if (p_vmlLink == null)
				return;

			Add(m_dicLinksByVirtualPath, p_vmlLink.VirtualModPath, p_vmlLink);
			Add(m_dicLinksByFileName, GetFileNameKey(p_vmlLink.VirtualModPath), p_vmlLink);

			if (p_enmDeploymentPathKeys == null)
				return;

			foreach (string strDeploymentPathKey in p_enmDeploymentPathKeys)
				Add(m_dicLinksByDeploymentPath, strDeploymentPathKey, p_vmlLink);
		}

		/// <summary>
		/// Adds a virtual link and up to two deployment keys to the indexes.
		/// </summary>
		/// <param name="p_vmlLink">The virtual link to add.</param>
		/// <param name="p_strPrimaryDeploymentPathKey">The primary deployment path key.</param>
		/// <param name="p_strSecondaryDeploymentPathKey">The secondary deployment path key.</param>
		public void Add(IVirtualModLink p_vmlLink, string p_strPrimaryDeploymentPathKey, string p_strSecondaryDeploymentPathKey)
		{
			if (p_vmlLink == null)
				return;

			Add(m_dicLinksByVirtualPath, p_vmlLink.VirtualModPath, p_vmlLink);
			Add(m_dicLinksByFileName, GetFileNameKey(p_vmlLink.VirtualModPath), p_vmlLink);
			Add(m_dicLinksByDeploymentPath, p_strPrimaryDeploymentPathKey, p_vmlLink);

			if (!String.Equals(p_strPrimaryDeploymentPathKey, p_strSecondaryDeploymentPathKey, StringComparison.OrdinalIgnoreCase))
				Add(m_dicLinksByDeploymentPath, p_strSecondaryDeploymentPathKey, p_vmlLink);
		}

		public void Remove(IVirtualModLink p_vmlLink)
		{
			Remove(p_vmlLink, null, null);
		}

		/// <summary>
		/// Removes a virtual link and its deployment keys from the indexes.
		/// </summary>
		/// <param name="p_vmlLink">The virtual link to remove.</param>
		/// <param name="p_enmDeploymentPathKeys">The deployment path keys associated with the link.</param>
		public void Remove(IVirtualModLink p_vmlLink, IEnumerable<string> p_enmDeploymentPathKeys)
		{
			if (p_vmlLink == null)
				return;

			Remove(m_dicLinksByVirtualPath, p_vmlLink.VirtualModPath, p_vmlLink);
			Remove(m_dicLinksByFileName, GetFileNameKey(p_vmlLink.VirtualModPath), p_vmlLink);

			if (p_enmDeploymentPathKeys == null)
				return;

			foreach (string strDeploymentPathKey in p_enmDeploymentPathKeys)
				Remove(m_dicLinksByDeploymentPath, strDeploymentPathKey, p_vmlLink);
		}

		/// <summary>
		/// Removes a virtual link and up to two deployment keys from the indexes.
		/// </summary>
		/// <param name="p_vmlLink">The virtual link to remove.</param>
		/// <param name="p_strPrimaryDeploymentPathKey">The primary deployment path key.</param>
		/// <param name="p_strSecondaryDeploymentPathKey">The secondary deployment path key.</param>
		public void Remove(IVirtualModLink p_vmlLink, string p_strPrimaryDeploymentPathKey, string p_strSecondaryDeploymentPathKey)
		{
			if (p_vmlLink == null)
				return;

			Remove(m_dicLinksByVirtualPath, p_vmlLink.VirtualModPath, p_vmlLink);
			Remove(m_dicLinksByFileName, GetFileNameKey(p_vmlLink.VirtualModPath), p_vmlLink);
			Remove(m_dicLinksByDeploymentPath, p_strPrimaryDeploymentPathKey, p_vmlLink);

			if (!String.Equals(p_strPrimaryDeploymentPathKey, p_strSecondaryDeploymentPathKey, StringComparison.OrdinalIgnoreCase))
				Remove(m_dicLinksByDeploymentPath, p_strSecondaryDeploymentPathKey, p_vmlLink);
		}

		public VirtualLinkIndexBucket FindByVirtualPath(string p_strVirtualPath)
		{
			return Find(m_dicLinksByVirtualPath, p_strVirtualPath);
		}

		/// <summary>
		/// Finds the virtual links whose virtual path has the specified file name.
		/// </summary>
		/// <param name="p_strFileName">The file name to find.</param>
		/// <returns>The indexed link bucket, or <c>null</c> when no matching link exists.</returns>
		public VirtualLinkIndexBucket FindByFileName(string p_strFileName)
		{
			return Find(m_dicLinksByFileName, GetFileNameKey(p_strFileName));
		}

		public VirtualLinkIndexBucket FindByDeploymentPath(string p_strDeploymentPathKey)
		{
			return Find(m_dicLinksByDeploymentPath, p_strDeploymentPathKey);
		}

		private static IEnumerable<string> GetDeploymentPathKeys(Func<IVirtualModLink, IEnumerable<string>> p_dlgDeploymentPathKeyFactory, IVirtualModLink p_vmlLink)
		{
			return p_dlgDeploymentPathKeyFactory == null ? null : p_dlgDeploymentPathKeyFactory(p_vmlLink);
		}

		/// <summary>
		/// Gets the normalized file-name key used by the file-name index.
		/// </summary>
		/// <param name="p_strPath">The path from which to obtain the file name.</param>
		/// <returns>The normalized file name, or an empty string when the path is empty.</returns>
		private static string GetFileNameKey(string p_strPath)
		{
			return String.IsNullOrWhiteSpace(p_strPath)
				? String.Empty
				: Path.GetFileName(p_strPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
		}

		private static void Add(Dictionary<string, VirtualLinkIndexBucket> p_dicIndex, string p_strKey, IVirtualModLink p_vmlLink)
		{
			if (String.IsNullOrEmpty(p_strKey))
				return;

			VirtualLinkIndexBucket bucket;
			if (!p_dicIndex.TryGetValue(p_strKey, out bucket))
			{
				bucket = new VirtualLinkIndexBucket();
				p_dicIndex.Add(p_strKey, bucket);
			}

			bucket.Add(p_vmlLink);
		}

		private static void Remove(Dictionary<string, VirtualLinkIndexBucket> p_dicIndex, string p_strKey, IVirtualModLink p_vmlLink)
		{
			if (String.IsNullOrEmpty(p_strKey))
				return;

			VirtualLinkIndexBucket bucket;
			if (!p_dicIndex.TryGetValue(p_strKey, out bucket))
				return;

			if (bucket.Remove(p_vmlLink) && bucket.Count == 0)
				p_dicIndex.Remove(p_strKey);
		}

		private static VirtualLinkIndexBucket Find(Dictionary<string, VirtualLinkIndexBucket> p_dicIndex, string p_strKey)
		{
			VirtualLinkIndexBucket bucket;
			return !String.IsNullOrEmpty(p_strKey) && p_dicIndex.TryGetValue(p_strKey, out bucket) ? bucket : null;
		}

		private static Dictionary<string, VirtualLinkIndexBucket> CreateIndex(int p_intCapacity)
		{
			return p_intCapacity > 0
				? new Dictionary<string, VirtualLinkIndexBucket>(p_intCapacity, StringComparer.OrdinalIgnoreCase)
				: new Dictionary<string, VirtualLinkIndexBucket>(StringComparer.OrdinalIgnoreCase);
		}

		private static Dictionary<string, VirtualLinkIndexBucket> CopyIndex(Dictionary<string, VirtualLinkIndexBucket> p_dicSource, int p_intCapacity)
		{
			Dictionary<string, VirtualLinkIndexBucket> copy = CreateIndex(Math.Max(p_intCapacity, p_dicSource.Count));
			foreach (KeyValuePair<string, VirtualLinkIndexBucket> pair in p_dicSource)
				copy.Add(pair.Key, pair.Value);
			return copy;
		}

		private static int GetDeploymentCapacity(int p_intExpectedLinkCount)
		{
			if (p_intExpectedLinkCount <= 0)
				return 0;

			return p_intExpectedLinkCount > Int32.MaxValue / 2
				? Int32.MaxValue
				: p_intExpectedLinkCount * 2;
		}
	}
}
