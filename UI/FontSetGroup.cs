using System;
using System.Collections.Generic;

namespace Nexus.UI
{
	/// <summary>
	/// A group of <see cref="FontSet"/>s.
	/// </summary>
	public class FontSetGroup
	{
		private Dictionary<string, FontSet> m_dicFontSets = new Dictionary<string, FontSet>();

		#region Properties

		/// <summary>
		/// Gets the default <see cref="FontSet"/> for the group.
		/// </summary>
		/// <value>The default <see cref="FontSet"/> for the group.</value>
		public FontSet DefaultFontSet { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public FontSetGroup()
		{
		}

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_fstDefault">The default <see cref="FontSet"/> for the group.</param>
		public FontSetGroup(FontSet p_fstDefault)
		{
			DefaultFontSet = p_fstDefault;
		}

		#endregion

		/// <summary>
		/// Adds the given <see cref="FontSet"/> to the group.
		/// </summary>
		/// <remarks>
		/// This replaces any previous <see cref="FontSet"/> with the same name.
		/// </remarks>
		/// <param name="p_strName">The name of the <see cref="FontSet"/>.</param>
		/// <param name="p_fstSet">The <see cref="FontSet"/>.</param>
		public void AddFontSet(string p_strName, FontSet p_fstSet)
		{
			m_dicFontSets[p_strName] = p_fstSet;
		}

		/// <summary>
		/// Removes the specified <see cref="FontSet"/> from the group.
		/// </summary>
		/// <param name="p_strName">The name of the <see cref="FontSet"/> to remove.</param>
		public void RemoveFontSet(string p_strName)
		{
			m_dicFontSets.Remove(p_strName);
		}

		/// <summary>
		/// Determines if the group contains the specified <see cref="FontSet"/>.
		/// </summary>
		/// <param name="p_strName">The name of the <see cref="FontSet"/> whose existence the group is to be checked.</param>
		/// <returns><c>true</c> if the specified <see cref="FontSet"/> is in the group;
		/// <c>false</c> otherwise.</returns>
		public bool HasFontSet(string p_strName)
		{
			if (String.IsNullOrEmpty(p_strName))
				return false;
			return m_dicFontSets.ContainsKey(p_strName);
		}

		/// <summary>
		/// Gets the requested <see cref="FontSet"/>.
		/// </summary>
		/// <param name="p_strName">The name of the <see cref="FontSet"/> to retrieve from the group.</param>
		/// <returns>The requested <see cref="FontSet"/>, or <c>null</c> if the group does not contain
		/// the requested <see cref="FontSet"/>.</returns>
		public FontSet GetFontSet(string p_strName)
		{
			if (m_dicFontSets.ContainsKey(p_strName))
				return m_dicFontSets[p_strName];
			return null;
		}
	}
}
