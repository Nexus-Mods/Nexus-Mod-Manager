using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nexus.UI;
using System.Drawing;

namespace Nexus.Client.UI
{
	/// <summary>
	/// Searches through the available font sets for the desired font.
	/// </summary>
	/// <remarks>
	/// This resolver searches through the available themes for the appropriate fonts.
	/// </remarks>
	public class NexusFontSetResolver : IFontSetResolver
	{
		private List<FontSetGroup> m_lstFontSetGroups = new List<FontSetGroup>();

		#region IFontSetResolver Members

		/// <summary>
		/// Returns a font that best matches the described font set.
		/// </summary>
		/// <param name="p_fsiFontInfo">The information describing the desired font.</param>
		/// <returns>A font that best matches the described font set.</returns>
		public Font RequestFont(FontSetInformation p_fsiFontInfo)
		{
			Font fntFont = null;
			for (Int32 i = m_lstFontSetGroups.Count - 1; i >= 0; i--)
			{
				FontSetGroup fsgGroup = m_lstFontSetGroups[i];
				if (fsgGroup.HasFontSet(p_fsiFontInfo.Set))
					fntFont = fsgGroup.GetFontSet(p_fsiFontInfo.Set).CreateFont(p_fsiFontInfo.Style, p_fsiFontInfo.Size);
				if ((fntFont == null) && (fsgGroup.DefaultFontSet != null))
					fntFont = fsgGroup.DefaultFontSet.CreateFont(p_fsiFontInfo.Style, p_fsiFontInfo.Size);
				if (fntFont != null)
					return fntFont;
			}
			return null;
		}

		#endregion

		/// <summary>
		/// Addes the given <see cref="FontSetGroup"/>s to the resolver.
		/// </summary>
		/// <remarks>
		/// When resolving a font, the groups are examined in the reverse order in which they were added.
		/// </remarks>
		/// <param name="p_fsgFontSets">The font set group to add.</param>
		public void AddFontSets(FontSetGroup p_fsgFontSets)
		{
			m_lstFontSetGroups.Add(p_fsgFontSets);
		}
	}
}
