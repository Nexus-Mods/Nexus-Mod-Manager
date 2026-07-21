using System.Collections;
using System.Collections.Generic;

namespace Nexus.Client.Plugins
{
	/// <summary>
	/// Provides base funcitonality of classes that compare two <see cref="Plugin"/>s.
	/// </summary>
	/// <remarks>
	/// This class provide static access to some standard <see cref="Plugin"/> comparers.
	/// </remarks>
	public abstract class PluginComparer : IComparer, IEqualityComparer, IComparer<Plugin>, IEqualityComparer<Plugin>
	{
		#region Standard Comparers

		/// <summary>
		/// Gets a comparer that compares <see cref="Plugin"/>s based on their <see cref="Plugin.Filename"/>s.
		/// </summary>
		/// <value>A comparer that compares <see cref="Plugin"/>s based on their <see cref="Plugin.Filename"/>s.</value>
		public static PluginComparer Filename
		{
			get
			{
				return new FilenamePluginComparer();
			}
		}

		#endregion

		#region IEqualityComparer<Plugin> Members

		/// <summary>
		/// Determines if the given <see cref="Plugin"/>s are equal.
		/// </summary>
		/// <param name="x">A <see cref="Plugin"/> whose equality is being checked.</param>
		/// <param name="y">A <see cref="Plugin"/> whose equality is being checked.</param>
		/// <returns><c>true</c> if the two <see cref="Plugin"/>s are equal;
		/// <c>false</c> otherwise.</returns>
		public bool Equals(Plugin x, Plugin y)
		{
			return Compare(x, y) == 0;
		}

		/// <summary>
		/// Gets the hashcode to use for the given <see cref="Plugin"/>.
		/// </summary>
		/// <param name="obj">The <see cref="Plugin"/> whose hashcode is to be determined.</param>
		/// <returns>The hascode to use for the given <see cref="Plugin"/>.</returns>
		public abstract int GetHashCode(Plugin obj);

		#endregion

		#region IComparer<Plugin> Members

		/// <summary>
		/// Compares the given <see cref="Plugin"/>s.
		/// </summary>
		/// <param name="x">An object to compare to another object.</param>
		/// <param name="y">An object to compare to another object.</param>
		/// <returns>A value less than 0 if <paramref name="x"/> is less than <paramref name="y"/>.
		/// 0 if this node is equal to the other.
		/// A value greater than 0 if <paramref name="x"/> is greater than <paramref name="y"/>.</returns>
		public abstract int Compare(Plugin x, Plugin y);

		#endregion

		#region IEqualityComparer Members

		/// <summary>
		/// Determines if the given <see cref="Plugin"/>s are equal.
		/// </summary>
		/// <param name="x">A <see cref="Plugin"/> whose equality is being checked.</param>
		/// <param name="y">A <see cref="Plugin"/> whose equality is being checked.</param>
		/// <returns><c>true</c> if the two <see cref="Plugin"/>s are equal;
		/// <c>false</c> otherwise.</returns>
		bool IEqualityComparer.Equals(object x, object y)
		{
			return Equals(x as Plugin, y as Plugin);
		}

		/// <summary>
		/// Gets the hashcode to use for the given <see cref="Plugin"/>.
		/// </summary>
		/// <param name="obj">The <see cref="Plugin"/> whose hashcode is to be determined.</param>
		/// <returns>The hascode to use for the given <see cref="Plugin"/>.</returns>
		int IEqualityComparer.GetHashCode(object obj)
		{
			return GetHashCode(obj as Plugin);
		}

		#endregion

		#region IComparer Members

		/// <summary>
		/// Compares the given <see cref="Plugin"/>s.
		/// </summary>
		/// <param name="x">An object to compare to another object.</param>
		/// <param name="y">An object to compare to another object.</param>
		/// <returns>A value less than 0 if <paramref name="x"/> is less than <paramref name="y"/>.
		/// 0 if this node is equal to the other.
		/// A value greater than 0 if <paramref name="x"/> is greater than <paramref name="y"/>.</returns>
		int IComparer.Compare(object x, object y)
		{
			return Compare(x as Plugin, y as Plugin);
		}

		#endregion
	}
}
