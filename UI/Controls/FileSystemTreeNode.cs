using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// A tree node that encapsulates a file system item.
	/// </summary>
	/// <remarks>
	/// This tracks the sources of and item.
	/// </remarks>
	public class FileSystemTreeNode : TreeNode, IComparable<FileSystemTreeNode>
	{
		/// <summary>
		/// A set of <see cref="Source"/>s.
		/// </summary>
		public class SourceSet : Set<Source>
		{
			#region Contructors

			/// <summary>
			/// The default constructor.
			/// </summary>
			public SourceSet()
			{
			}

			/// <summary>
			/// The copy constructor.
			/// </summary>
			/// <param name="p_setCopy">The set to copy.</param>
			public SourceSet(Set<Source> p_setCopy)
				: base(p_setCopy)
			{
			}

			#endregion

			/// <summary>
			/// Gets the source specified by the given path.
			/// </summary>
			/// <param name="p_strPath">The path of the source to retrieve.</param>
			/// <returns>The source specified by the given path.</returns>
			public Source this[string p_strPath]
			{
				get
				{
					return this.Find((s) => { return s.Equals(p_strPath); });
				}
			}

			/// <summary>
			/// Adds the specified source.
			/// </summary>
			/// <param name="p_strPath">The path of the source to add.</param>
			/// <param name="p_booIsLoaded">Whether or not the source has been loaded.</param>
			public void Add(string p_strPath, bool p_booIsLoaded)
			{
				this.Add(new Source(p_strPath, p_booIsLoaded));
			}

			/// <summary>
			/// Removes the specified source.
			/// </summary>
			/// <param name="p_strPath">The path of the source to remove.</param>
			/// <returns><c>true</c> if the source was removed;
			/// <c>false</c> otherwise.</returns>
			public bool Remove(string p_strPath)
			{
				for (Int32 i = Count - 1; i >= 0; i--)
					if (this[i].Equals(p_strPath))
					{
						RemoveAt(i);
						return true;
					}
				return false;
			}

			/// <summary>
			/// Determines if the set contains a <see cref="Source"/>
			/// with the given path.
			/// </summary>
			/// <param name="p_strSourcePath">The source path to look for in the set.</param>
			/// <returns><c>true</c> if the set contains a <see cref="Source"/> with
			/// the given path; <c>false</c> otherwise.</returns>
			public bool Contains(string p_strSourcePath)
			{
				for (Int32 i = Count - 1; i >= 0; i--)
					if (this[i].Equals(p_strSourcePath))
						return true;
				return false;
			}
		}

		/// <summary>
		/// A file system item source.
		/// </summary>
		public class Source : IEquatable<Source>, IEquatable<string>
		{
			private string m_strPath = null;
			private bool m_booIsLoaded = false;

			#region Properties

			/// <summary>
			/// Gets or sets the path of the source.
			/// </summary>
			/// <value>The path of the source.</value>
			public string Path
			{
				get
				{
					return m_strPath;
				}
				set
				{
					m_strPath = value;
				}
			}

			/// <summary>
			/// Gets or sets whether the source has been loaded for the
			/// current node.
			/// </summary>
			/// <value>Whether the source has been loaded for the
			/// current node.</value>
			public bool IsLoaded
			{
				get
				{
					return m_booIsLoaded;
				}
				set
				{
					m_booIsLoaded = value;
				}
			}

			#endregion

			#region Constructors

			/// <summary>
			/// A simple constructor that initializes the object with the given values.
			/// </summary>
			/// <param name="p_strPath">The path of the source.</param>
			/// <param name="p_booIsLoaded">Whether the source has been loaded for the
			/// current node.</param>
			public Source(string p_strPath, bool p_booIsLoaded)
			{
				m_strPath = p_strPath;
				m_booIsLoaded = p_booIsLoaded;
			}

			#endregion

			#region IEquatable<Source> Members

			/// <summary>
			/// Determines if this <see cref="Source"/> is equal to the given
			/// <see cref="Source"/>.
			/// </summary>
			/// <remarks>
			/// Two <see cref="Source"/>s are equal if and only if their
			/// <see cref="Source.Path"/>s are case-insensitively equal.
			/// </remarks>
			/// <param name="other">The <see cref="Source"/> to compare to this one.</param>
			/// <returns><c>true</c> if the two <see cref="Source"/>s are equal;
			/// <c>false</c> otherwise.</returns>
			public bool Equals(Source other)
			{
				return Path.Equals(other.Path, StringComparison.InvariantCultureIgnoreCase);
			}

			#endregion

			#region IEquatable<string> Members

			/// <summary>
			/// Determines if this <see cref="Source"/> is equal to the given
			/// string.
			/// </summary>
			/// <remarks>
			/// A <see cref="Source"/> is equal to a string if and only if the
			/// <see cref="Source.Path"/> is case-insensitively equal to the string.
			/// </remarks>
			/// <param name="other">The string to compare to this <see cref="Source"/>.</param>
			/// <returns><c>true</c> if this <see cref="Source"/> is equal
			/// to the given string; <c>false</c> otherwise.</returns>
			public bool Equals(string other)
			{
				return Path.Equals(other);
			}

			#endregion

			/// <summary>
			/// Converts the <see cref="Source"/> to a string.
			/// </summary>
			/// <param name="p_srcSource">The <see cref="Source"/> to convert to a string.</param>
			/// <returns>The string representation of the <see cref="Source"/>.</returns>
			public static implicit operator string(Source p_srcSource)
			{
				return (p_srcSource == null) ? null : p_srcSource.Path;
			}
		}

		private static Dictionary<string, Archive> m_dicArchiveCache = new Dictionary<string, Archive>(StringComparer.InvariantCultureIgnoreCase);

		private SourceSet m_sstSources = new SourceSet();
		private bool? m_booIsAchive = null;
		private bool? m_booIsDirectory = null;

		#region Properties

		/// <summary>
		/// Gets the path to the node in the current tree.
		/// </summary>
		/// <value>The path to the node in the current tree.</value>
		public new string FullPath
		{
			get
			{
				if (TreeView != null)
					return base.FullPath;
				Stack<string> stkPath = new Stack<string>();
				TreeNode tndParent = this;
				do
				{
					stkPath.Push(tndParent.Text);
					tndParent = tndParent.Parent;
				} while (tndParent != null);
				StringBuilder stbPath = new StringBuilder();
				while (stkPath.Count > 0)
				{
					stbPath.Append(stkPath.Pop());
					if (stkPath.Count > 0)
						stbPath.Append(Path.DirectorySeparatorChar);
				}
				return stbPath.ToString();
			}
		}

		/// <summary>
		/// Gets whether or not the node represents a directory.
		/// </summary>
		/// <value>Whether or not the node represents a directory.</value>
		public bool IsDirectory
		{
			get
			{
				if (m_booIsDirectory.HasValue)
					return m_booIsDirectory.Value;

				if (m_sstSources.Count == 0)
					m_booIsDirectory = true;
				else if (Archive.IsArchivePath(LastSource.Path))
				{
					KeyValuePair<string, string> kvpArchive = Archive.ParseArchivePath(LastSource);
					Archive arcArchive = null;
					lock (m_dicArchiveCache)
					{
						if (!m_dicArchiveCache.ContainsKey(kvpArchive.Key))
							m_dicArchiveCache[kvpArchive.Key] = new Archive(kvpArchive.Key);
						arcArchive = m_dicArchiveCache[kvpArchive.Key];
					}
					m_booIsDirectory = arcArchive.IsDirectory(kvpArchive.Value);
				}
				else
					m_booIsDirectory = Directory.Exists(LastSource);
				return m_booIsDirectory.Value;
			}
		}

		/// <summary>
		/// Gets whether or not the node represents a drive.
		/// </summary>
		/// <value>Whether or not the node represents a drive.</value>
		public bool IsDrive
		{
			get
			{
				return FileUtil.IsDrivePath(LastSource.Path);
			}
		}

		/// <summary>
		/// Gets whether or not the node represents an archive.
		/// </summary>
		/// <value>Whether or not the node represents an archive.</value>
		public bool IsArchive
		{
			get
			{
				if (m_booIsAchive.HasValue)
					return m_booIsAchive.Value;

				if (m_sstSources.Count == 0)
					m_booIsAchive = false;
				else
				{
					m_booIsAchive = Archive.IsArchive(LastSource);
				}
				return m_booIsAchive.Value;
			}
		}

		/// <summary>
		/// Gets the node's parent.
		/// </summary>
		/// <remarks>
		/// This casts the parent as a <see cref="FileSystemTreeNode"/>.
		/// </remarks>
		/// <value>The node's parent.</value>
		public new FileSystemTreeNode Parent
		{
			get
			{
				return (FileSystemTreeNode)base.Parent;
			}
		}

		/// <summary>
		/// Gets the sources for the node.
		/// </summary>
		/// <value>The sources for the node.</value>
		public SourceSet Sources
		{
			get
			{
				return m_sstSources;
			}
		}

		/// <summary>
		/// Gets the last source for the node.
		/// </summary>
		/// <remarks>
		/// The last source is the source that will overwrite the other sources. It is the source
		/// last added.
		/// </remarks>
		/// <value>The last source for the node.</value>
		public Source LastSource
		{
			get
			{
				if (m_sstSources.Count > 0)
					return m_sstSources[m_sstSources.Count - 1];
				return null;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The copy constructor.
		/// </summary>
		/// <param name="p_tndCopy">The node to copy.</param>
		public FileSystemTreeNode(FileSystemTreeNode p_tndCopy)
			: base(p_tndCopy.Text)
		{
			this.Name = p_tndCopy.Name;
			this.m_sstSources = new SourceSet(p_tndCopy.m_sstSources);
		}

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_strName">The name of the node.</param>
		/// <param name="p_strPath">The path of the file system item being represented by the node.</param>
		public FileSystemTreeNode(string p_strName, string p_strPath)
			: base(p_strName)
		{
			if (!String.IsNullOrEmpty(p_strPath))
				m_sstSources.Add(p_strPath, false);
		}

		#endregion

		/// <summary>
		/// Adds the specified path as a source for the node.
		/// </summary>
		/// <param name="p_strSource">The path to add as a source for the node.</param>
		/// <param name="p_booIsLoaded">Whether the source has been loaded.</param>
		public void AddSource(string p_strSource, bool p_booIsLoaded)
		{
			if (!String.IsNullOrEmpty(p_strSource))
			{
				m_sstSources.Remove(p_strSource);
				m_sstSources.Add(p_strSource, p_booIsLoaded);
			}
		}

		#region IComparable<FileSystemTreeNode> Members

		/// <summary>
		/// Compares this node to another.
		/// </summary>
		/// <remarks>
		/// A directory is less than a file. If the nodes being compared are
		/// both directories, or both not directories, their display text
		/// is compared.
		/// </remarks> 
		/// <param name="other">The <see cref="FileSystemTreeNode"/> to which to compare this node.</param>
		/// <returns>A value less than 0 if this node is less than the other.
		/// 0 if this node is equal to the other.
		/// A value greater than 0 if this node is greater than the other.</returns>
		public int CompareTo(FileSystemTreeNode other)
		{
			Int32 intResult = other.IsDirectory.CompareTo(this.IsDirectory);
			if (intResult == 0)
				intResult = this.Text.CompareTo(other.Text);
			return intResult;
		}

		#endregion
	}
}
