using System;
using System.Collections;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// Compares two objects.
	/// </summary>
	public class Comparer<T> : IComparer where T : IComparable<T>
	{
		#region IComparer Members

		/// <summary>
		/// Compares the given objects using their
		/// <see cref="FileSystemTreeNode.CompareTo"/> methods.
		/// </summary>
		/// <param name="x">An object to compare to another object.</param>
		/// <param name="y">An object to compare to another object.</param>
		/// <returns>A value less than 0 if <paramref name="x"/> is less than <paramref name="y"/>.
		/// 0 if this node is equal to the other.
		/// A value greater than 0 if <paramref name="x"/> is greater than <paramref name="y"/>.</returns>
		public int Compare(object x, object y)
		{
			return ((T)x).CompareTo((T)y);
		}

		#endregion
	}
}
