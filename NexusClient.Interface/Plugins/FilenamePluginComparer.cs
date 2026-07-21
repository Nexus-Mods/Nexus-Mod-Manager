using System;

namespace Nexus.Client.Plugins
{
	/// <summary>
	/// Compares <see cref="Plugin"/>s based on their <see cref="Plugin.Filename"/>s.
	/// </summary>
	/// <remarks>
	/// In the constext of this comparer, <see cref="Plugin"/>s are strictly ordered
	/// by their ordinally case-insensitive <see cref="Plugin.Filename"/>s.
	/// </remarks>
	public class FilenamePluginComparer : PluginComparer
	{
		/// <summary>
		/// Gets the hashcode to use for the given <see cref="Plugin"/>.
		/// </summary>
		/// <param name="obj">The <see cref="Plugin"/> whose hashcode is to be determined.</param>
		/// <returns>The hascode to use for the given <see cref="Plugin"/>.</returns>
		public override int GetHashCode(Plugin obj)
		{
			if ((obj == null) || (obj.Filename == null))
				return 53;
			return obj.Filename.GetHashCode();
		}

		/// <summary>
		/// Compares the given <see cref="Plugin"/>s.
		/// </summary>
		/// <param name="x">An object to compare to another object.</param>
		/// <param name="y">An object to compare to another object.</param>
		/// <returns>A value less than 0 if <paramref name="x"/> is less than <paramref name="y"/>.
		/// 0 if this node is equal to the other.
		/// A value greater than 0 if <paramref name="x"/> is greater than <paramref name="y"/>.</returns>
		public override int Compare(Plugin x, Plugin y)
		{
			if (x == null)
				return (y == null) ? 0 : -1;
			if (y == null)
				return 1;
			return StringComparer.OrdinalIgnoreCase.Compare(x.Filename, y.Filename);
		}
	}
}
