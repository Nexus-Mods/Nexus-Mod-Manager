using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;

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

		/// <summary>
		/// Copies the links in this bucket into a compact array.
		/// </summary>
		/// <returns>An array containing the bucket links in index order.</returns>
		public IVirtualModLink[] ToArray()
		{
			int count = Count;
			if (count == 0)
				return new IVirtualModLink[0];

			IVirtualModLink[] links = new IVirtualModLink[count];
			links[0] = m_vmlFirst;
			if (m_lstAdditional != null)
				m_lstAdditional.CopyTo(links, 1);
			return links;
		}
	}

	/// <summary>
	/// Stores the links for one managed owner with constant-time reference removals.
	/// </summary>
	internal sealed class VirtualLinkOwnerIndexBucket
	{
		private readonly List<IVirtualModLink> m_lstLinks = new List<IVirtualModLink>();
		private readonly Dictionary<IVirtualModLink, int> m_dicPositions =
			new Dictionary<IVirtualModLink, int>(VirtualLinkReferenceComparer.Instance);

		public int Count
		{
			get { return m_lstLinks.Count; }
		}

		/// <summary>
		/// Adds a link to the owner bucket.
		/// </summary>
		public void Add(IVirtualModLink p_vmlLink)
		{
			if (p_vmlLink == null)
				return;

			m_dicPositions[p_vmlLink] = m_lstLinks.Count;
			m_lstLinks.Add(p_vmlLink);
		}

		/// <summary>
		/// Removes a link without shifting the remaining owner links.
		/// </summary>
		public bool Remove(IVirtualModLink p_vmlLink)
		{
			if (p_vmlLink == null || m_lstLinks.Count == 0)
				return false;

			int index;
			if (!m_dicPositions.TryGetValue(p_vmlLink, out index))
			{
				EqualityComparer<IVirtualModLink> comparer = EqualityComparer<IVirtualModLink>.Default;
				index = m_lstLinks.FindIndex(x => comparer.Equals(x, p_vmlLink));
				if (index < 0)
					return false;
			}

			int lastIndex = m_lstLinks.Count - 1;
			IVirtualModLink removedLink = m_lstLinks[index];
			if (index != lastIndex)
			{
				IVirtualModLink lastLink = m_lstLinks[lastIndex];
				m_lstLinks[index] = lastLink;
				m_dicPositions[lastLink] = index;
			}

			m_lstLinks.RemoveAt(lastIndex);
			m_dicPositions.Remove(removedLink);
			return true;
		}

		/// <summary>
		/// Copies the owner links into a compact array.
		/// </summary>
		public IVirtualModLink[] ToArray()
		{
			return m_lstLinks.ToArray();
		}

		private sealed class VirtualLinkReferenceComparer : IEqualityComparer<IVirtualModLink>
		{
			public static readonly VirtualLinkReferenceComparer Instance = new VirtualLinkReferenceComparer();

			/// <inheritdoc />
			public bool Equals(IVirtualModLink p_vmlLeft, IVirtualModLink p_vmlRight)
			{
				return ReferenceEquals(p_vmlLeft, p_vmlRight);
			}

			/// <inheritdoc />
			public int GetHashCode(IVirtualModLink p_vmlLink)
			{
				return p_vmlLink == null ? 0 : RuntimeHelpers.GetHashCode(p_vmlLink);
			}
		}
	}

	/// <summary>
	/// Represents one immutable virtual-link index entry.
	/// </summary>
	internal sealed class VirtualLinkIndexSnapshotEntry
	{
		/// <summary>
		/// Initializes an immutable virtual-link index entry.
		/// </summary>
		/// <param name="p_strKey">The indexed path key.</param>
		/// <param name="p_lstLinks">The links associated with the key.</param>
		public VirtualLinkIndexSnapshotEntry(string p_strKey, IList<IVirtualModLink> p_lstLinks)
		{
			Key = p_strKey ?? String.Empty;
			Links = new ReadOnlyCollection<IVirtualModLink>(p_lstLinks ?? new List<IVirtualModLink>());
		}

		public string Key { get; private set; }
		public IList<IVirtualModLink> Links { get; private set; }
	}

	/// <summary>
	/// Provides an immutable snapshot of the virtual-path and deployment-path indexes.
	/// </summary>
	internal sealed class VirtualLinkIndexSnapshot
	{
		/// <summary>
		/// Initializes a virtual-link index snapshot.
		/// </summary>
		/// <param name="p_lstVirtualPathEntries">The virtual-path index entries.</param>
		/// <param name="p_lstDeploymentPathEntries">The deployment-path index entries.</param>
		public VirtualLinkIndexSnapshot(IList<VirtualLinkIndexSnapshotEntry> p_lstVirtualPathEntries, IList<VirtualLinkIndexSnapshotEntry> p_lstDeploymentPathEntries)
		{
			VirtualPathEntries = p_lstVirtualPathEntries ?? new ReadOnlyCollection<VirtualLinkIndexSnapshotEntry>(new List<VirtualLinkIndexSnapshotEntry>());
			DeploymentPathEntries = p_lstDeploymentPathEntries ?? new ReadOnlyCollection<VirtualLinkIndexSnapshotEntry>(new List<VirtualLinkIndexSnapshotEntry>());
		}

		public IList<VirtualLinkIndexSnapshotEntry> VirtualPathEntries { get; private set; }
		public IList<VirtualLinkIndexSnapshotEntry> DeploymentPathEntries { get; private set; }
	}

	internal class VirtualLinkIndex
	{
		private Dictionary<string, VirtualLinkIndexBucket> m_dicLinksByVirtualPath;
		private Dictionary<string, VirtualLinkIndexBucket> m_dicLinksByFileName;
		private Dictionary<string, VirtualLinkIndexBucket> m_dicLinksByDeploymentPath;
		private Dictionary<string, VirtualLinkOwnerIndexBucket> m_dicLinksByOwnerKey;
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
			m_dicLinksByOwnerKey = CreateOwnerIndex(expectedLinkCount);
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
			m_dicLinksByOwnerKey = CopyOwnerIndex(m_dicLinksByOwnerKey, reservedLinkCount);
			m_intReservedLinkCount = reservedLinkCount;
		}

		public void Rebuild(IEnumerable<IVirtualModLink> p_enmLinks)
		{
			Rebuild(p_enmLinks, null);
		}

		public void Rebuild(IEnumerable<IVirtualModLink> p_enmLinks, Func<IVirtualModLink, IEnumerable<string>> p_dlgDeploymentPathKeyFactory)
		{
			Rebuild(p_enmLinks, p_dlgDeploymentPathKeyFactory, null);
		}

		/// <summary>
		/// Rebuilds the indexes, including the runtime owner-to-links inverse index.
		/// </summary>
		public void Rebuild(IEnumerable<IVirtualModLink> p_enmLinks, Func<IVirtualModLink, IEnumerable<string>> p_dlgDeploymentPathKeyFactory,
			Func<IVirtualModLink, string> p_dlgOwnerKeyFactory)
		{
			Clear();

			if (p_enmLinks == null)
				return;

			foreach (IVirtualModLink vmlLink in p_enmLinks)
				Add(vmlLink, GetDeploymentPathKeys(p_dlgDeploymentPathKeyFactory, vmlLink), GetOwnerKey(p_dlgOwnerKeyFactory, vmlLink));
		}

		/// <summary>
		/// Clears all virtual-link lookup indexes.
		/// </summary>
		public void Clear()
		{
			m_dicLinksByVirtualPath.Clear();
			m_dicLinksByFileName.Clear();
			m_dicLinksByDeploymentPath.Clear();
			m_dicLinksByOwnerKey.Clear();
		}

		public void Add(IVirtualModLink p_vmlLink)
		{
			Add(p_vmlLink, null, null, null);
		}

		/// <summary>
		/// Adds a virtual link and its deployment keys to the indexes.
		/// </summary>
		/// <param name="p_vmlLink">The virtual link to add.</param>
		/// <param name="p_enmDeploymentPathKeys">The deployment path keys associated with the link.</param>
		public void Add(IVirtualModLink p_vmlLink, IEnumerable<string> p_enmDeploymentPathKeys)
		{
			Add(p_vmlLink, p_enmDeploymentPathKeys, null);
		}

		/// <summary>
		/// Adds a virtual link, its deployment keys, and its managed owner key to the indexes.
		/// </summary>
		public void Add(IVirtualModLink p_vmlLink, IEnumerable<string> p_enmDeploymentPathKeys, string p_strOwnerKey)
		{
			if (p_vmlLink == null)
				return;

			Add(m_dicLinksByVirtualPath, p_vmlLink.VirtualModPath, p_vmlLink);
			Add(m_dicLinksByFileName, GetFileNameKey(p_vmlLink.VirtualModPath), p_vmlLink);
			Add(m_dicLinksByOwnerKey, p_strOwnerKey, p_vmlLink);

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
			Add(p_vmlLink, p_strPrimaryDeploymentPathKey, p_strSecondaryDeploymentPathKey, null);
		}

		/// <summary>
		/// Adds a virtual link, up to two deployment keys, and its managed owner key to the indexes.
		/// </summary>
		public void Add(IVirtualModLink p_vmlLink, string p_strPrimaryDeploymentPathKey, string p_strSecondaryDeploymentPathKey, string p_strOwnerKey)
		{
			if (p_vmlLink == null)
				return;

			Add(m_dicLinksByVirtualPath, p_vmlLink.VirtualModPath, p_vmlLink);
			Add(m_dicLinksByFileName, GetFileNameKey(p_vmlLink.VirtualModPath), p_vmlLink);
			Add(m_dicLinksByDeploymentPath, p_strPrimaryDeploymentPathKey, p_vmlLink);
			Add(m_dicLinksByOwnerKey, p_strOwnerKey, p_vmlLink);

			if (!String.Equals(p_strPrimaryDeploymentPathKey, p_strSecondaryDeploymentPathKey, StringComparison.OrdinalIgnoreCase))
				Add(m_dicLinksByDeploymentPath, p_strSecondaryDeploymentPathKey, p_vmlLink);
		}

		public void Remove(IVirtualModLink p_vmlLink)
		{
			Remove(p_vmlLink, null, null, null);
		}

		/// <summary>
		/// Removes a virtual link and its deployment keys from the indexes.
		/// </summary>
		/// <param name="p_vmlLink">The virtual link to remove.</param>
		/// <param name="p_enmDeploymentPathKeys">The deployment path keys associated with the link.</param>
		public void Remove(IVirtualModLink p_vmlLink, IEnumerable<string> p_enmDeploymentPathKeys)
		{
			Remove(p_vmlLink, p_enmDeploymentPathKeys, null);
		}

		/// <summary>
		/// Removes a virtual link, its deployment keys, and its managed owner key from the indexes.
		/// </summary>
		public void Remove(IVirtualModLink p_vmlLink, IEnumerable<string> p_enmDeploymentPathKeys, string p_strOwnerKey)
		{
			if (p_vmlLink == null)
				return;

			Remove(m_dicLinksByVirtualPath, p_vmlLink.VirtualModPath, p_vmlLink);
			Remove(m_dicLinksByFileName, GetFileNameKey(p_vmlLink.VirtualModPath), p_vmlLink);
			Remove(m_dicLinksByOwnerKey, p_strOwnerKey, p_vmlLink);

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
			Remove(p_vmlLink, p_strPrimaryDeploymentPathKey, p_strSecondaryDeploymentPathKey, null);
		}

		/// <summary>
		/// Removes a virtual link, up to two deployment keys, and its managed owner key from the indexes.
		/// </summary>
		public void Remove(IVirtualModLink p_vmlLink, string p_strPrimaryDeploymentPathKey, string p_strSecondaryDeploymentPathKey, string p_strOwnerKey)
		{
			if (p_vmlLink == null)
				return;

			Remove(m_dicLinksByVirtualPath, p_vmlLink.VirtualModPath, p_vmlLink);
			Remove(m_dicLinksByFileName, GetFileNameKey(p_vmlLink.VirtualModPath), p_vmlLink);
			Remove(m_dicLinksByDeploymentPath, p_strPrimaryDeploymentPathKey, p_vmlLink);
			Remove(m_dicLinksByOwnerKey, p_strOwnerKey, p_vmlLink);

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

		/// <summary>
		/// Finds the virtual links registered to a managed mod owner.
		/// </summary>
		public VirtualLinkOwnerIndexBucket FindByOwnerKey(string p_strOwnerKey)
		{
			return Find(m_dicLinksByOwnerKey, p_strOwnerKey);
		}

		/// <summary>
		/// Creates an immutable copy of the path indexes for read-only consumers.
		/// </summary>
		/// <returns>A snapshot that can be used without holding the index lock.</returns>
		public VirtualLinkIndexSnapshot CreateSnapshot()
		{
			return new VirtualLinkIndexSnapshot(
				CreateSnapshotEntries(m_dicLinksByVirtualPath),
				CreateSnapshotEntries(m_dicLinksByDeploymentPath));
		}

		private static IEnumerable<string> GetDeploymentPathKeys(Func<IVirtualModLink, IEnumerable<string>> p_dlgDeploymentPathKeyFactory, IVirtualModLink p_vmlLink)
		{
			return p_dlgDeploymentPathKeyFactory == null ? null : p_dlgDeploymentPathKeyFactory(p_vmlLink);
		}

		private static string GetOwnerKey(Func<IVirtualModLink, string> p_dlgOwnerKeyFactory, IVirtualModLink p_vmlLink)
		{
			return p_dlgOwnerKeyFactory == null ? null : p_dlgOwnerKeyFactory(p_vmlLink);
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

		/// <summary>
		/// Adds a link to the owner index.
		/// </summary>
		private static void Add(Dictionary<string, VirtualLinkOwnerIndexBucket> p_dicIndex, string p_strKey, IVirtualModLink p_vmlLink)
		{
			if (String.IsNullOrEmpty(p_strKey))
				return;

			VirtualLinkOwnerIndexBucket bucket;
			if (!p_dicIndex.TryGetValue(p_strKey, out bucket))
			{
				bucket = new VirtualLinkOwnerIndexBucket();
				p_dicIndex.Add(p_strKey, bucket);
			}

			bucket.Add(p_vmlLink);
		}

		/// <summary>
		/// Removes a link from the owner index.
		/// </summary>
		private static void Remove(Dictionary<string, VirtualLinkOwnerIndexBucket> p_dicIndex, string p_strKey, IVirtualModLink p_vmlLink)
		{
			if (String.IsNullOrEmpty(p_strKey))
				return;

			VirtualLinkOwnerIndexBucket bucket;
			if (!p_dicIndex.TryGetValue(p_strKey, out bucket))
				return;

			if (bucket.Remove(p_vmlLink) && bucket.Count == 0)
				p_dicIndex.Remove(p_strKey);
		}

		/// <summary>
		/// Finds the bucket for one managed owner.
		/// </summary>
		private static VirtualLinkOwnerIndexBucket Find(Dictionary<string, VirtualLinkOwnerIndexBucket> p_dicIndex, string p_strKey)
		{
			VirtualLinkOwnerIndexBucket bucket;
			return !String.IsNullOrEmpty(p_strKey) && p_dicIndex.TryGetValue(p_strKey, out bucket) ? bucket : null;
		}

		private static VirtualLinkIndexBucket Find(Dictionary<string, VirtualLinkIndexBucket> p_dicIndex, string p_strKey)
		{
			VirtualLinkIndexBucket bucket;
			return !String.IsNullOrEmpty(p_strKey) && p_dicIndex.TryGetValue(p_strKey, out bucket) ? bucket : null;
		}

		/// <summary>
		/// Copies one index into immutable snapshot entries.
		/// </summary>
		/// <param name="p_dicIndex">The index to copy.</param>
		/// <returns>A read-only list containing the copied entries.</returns>
		private static IList<VirtualLinkIndexSnapshotEntry> CreateSnapshotEntries(Dictionary<string, VirtualLinkIndexBucket> p_dicIndex)
		{
			List<VirtualLinkIndexSnapshotEntry> entries = new List<VirtualLinkIndexSnapshotEntry>(p_dicIndex.Count);
			foreach (KeyValuePair<string, VirtualLinkIndexBucket> pair in p_dicIndex)
				entries.Add(new VirtualLinkIndexSnapshotEntry(pair.Key, pair.Value == null ? null : pair.Value.ToArray()));
			return new ReadOnlyCollection<VirtualLinkIndexSnapshotEntry>(entries);
		}

		/// <summary>
		/// Creates an owner index with the requested initial capacity.
		/// </summary>
		private static Dictionary<string, VirtualLinkOwnerIndexBucket> CreateOwnerIndex(int p_intCapacity)
		{
			return p_intCapacity > 0
				? new Dictionary<string, VirtualLinkOwnerIndexBucket>(p_intCapacity, StringComparer.OrdinalIgnoreCase)
				: new Dictionary<string, VirtualLinkOwnerIndexBucket>(StringComparer.OrdinalIgnoreCase);
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

		/// <summary>
		/// Copies the owner index into a dictionary with the requested capacity.
		/// </summary>
		private static Dictionary<string, VirtualLinkOwnerIndexBucket> CopyOwnerIndex(Dictionary<string, VirtualLinkOwnerIndexBucket> p_dicSource, int p_intCapacity)
		{
			Dictionary<string, VirtualLinkOwnerIndexBucket> copy = CreateOwnerIndex(Math.Max(p_intCapacity, p_dicSource.Count));
			foreach (KeyValuePair<string, VirtualLinkOwnerIndexBucket> pair in p_dicSource)
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
