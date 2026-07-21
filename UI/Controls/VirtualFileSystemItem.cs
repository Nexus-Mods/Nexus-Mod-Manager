using System;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// This is an item that is displayed in a <see cref="VirtualFileSystemTreeView"/>.
	/// </summary>
	public class VirtualFileSystemItem : IComparable<VirtualFileSystemItem>
	{
		#region Properties

		/// <summary>
		/// Gets the path of the file system item that is being represented.
		/// </summary>
		/// <value>The path of the file system item that is being represented.</value>
		public string Source { get; private set; }

		/// <summary>
		/// Gets the path of the virtual file system item in the virutal file system
		/// represented by the <see cref="VirtualFileSystemTreeView"/> in which this item
		/// is being used.
		/// </summary>
		/// <value>The path of the virtual file system item in the virutal file system
		/// represented by the <see cref="VirtualFileSystemTreeView"/> in which this item
		/// is being used.</value>
		public string Path { get; private set; }

		/// <summary>
		/// Gets whether the item is a directory.
		/// </summary>
		/// <value>Whether the item is a directory.</value>
		public bool IsDirectory { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_strSource">The path of the file system item that is being represented.</param>
		/// <param name="p_strPath">The path of the virtual file system item in the virutal file system
		/// represented by the <see cref="VirtualFileSystemTreeView"/> in which this item
		/// is being used.</param>
		/// <param name="p_booIsDirectory">Whether the item is a directory.</param>
		public VirtualFileSystemItem(string p_strSource, string p_strPath, bool p_booIsDirectory)
		{
			Source = p_strSource;
			Path = p_strPath;
			IsDirectory = p_booIsDirectory;
		}

		#endregion

		#region IComparable<VirtualFileSystemItem> Members

		/// <summary>
		/// Determines whether this <see cref="VirtualFileSystemItem"/> is less than, equal to,
		/// or greater than the given <see cref="VirtualFileSystemItem"/>.
		/// </summary>
		/// <param name="other">The <see cref="VirtualFileSystemItem"/> to which to compare this <see cref="VirtualFileSystemItem"/>.</param>
		/// <returns>A value less than 0 if this <see cref="VirtualFileSystemItem"/> is less than the given <see cref="VirtualFileSystemItem"/>,
		/// or 0 if this <see cref="VirtualFileSystemItem"/> is equal to the given <see cref="VirtualFileSystemItem"/>,
		///or a value greater than 0 if this <see cref="VirtualFileSystemItem"/> is greater than the given <see cref="VirtualFileSystemItem"/>.</returns>
		public int CompareTo(VirtualFileSystemItem other)
		{
			Int32 intResult = IsDirectory.CompareTo(other.IsDirectory);
			if (intResult == 0)
			{
				intResult = String.Compare(Source, other.Source, StringComparison.OrdinalIgnoreCase);
				if (intResult == 0)
					intResult = String.Compare(Path, other.Path, StringComparison.OrdinalIgnoreCase);
			}
			return intResult;
		}

		#endregion
	}
}
