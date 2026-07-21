using System;
using System.Drawing;
using System.Collections.Generic;

namespace Nexus.UI
{
	/// <summary>Used to represent a list of fonts.</summary>
	public class FontSet
	{
		private Dictionary<FontStyle, FontFamily> m_dicFamilyCache = new Dictionary<FontStyle, FontFamily>();

		#region Properties

		/// <summary>Gets the families.</summary>
		public string[] Families { get; private set; }

		#endregion

		#region Constructors

		/// <summary>Creates a new instance of the <see cref="FontSet"/> class.</summary>
		/// <param name="families">The list of font familes this set uses. The familes will try and be used in the order they were passed, e.g. index 0 to n.</param>
		/// <exception cref="ArgumentNullException">If the <paramref name="families"/> or <paramref name="styles"/> parameters are null.</exception>
		/// <exception cref="ArgumentException">If the <paramref name="families"/> or <paramref name="styles"/> parameters are zero length, or the <paramref name="families"/> parameters contains null, or empty string entires.</exception>
		public FontSet(string[] families)
		{
			if (families == null)
				throw new ArgumentNullException("families");

			if (families.Length == 0)
				throw new ArgumentException("Invalid argument passed, the families parameter can't be zero length.");

			if (Array.Exists(families, delegate(string family) { return string.IsNullOrEmpty(family); }))
				throw new ArgumentException("Invalid argument passed, the families parameter contains a null or empty value.");

			this.Families = families;
		}

		#endregion

		#region Public Methods

		/// <summary>Used to create the font with the style and size requested.</summary>
		/// <param name="p_fstStyle">The style of font to create.</param>
		/// <param name="p_fltSize">The size of font to create.</param>
		/// <returns>The font created.</returns>
		public Font CreateFont(FontStyle p_fstStyle, float p_fltSize)
		{
			if (m_dicFamilyCache.ContainsKey(p_fstStyle))
				return new Font(m_dicFamilyCache[p_fstStyle], p_fltSize, p_fstStyle);

			Font fntFont = null;
			foreach (string strFontFamily in Families)
			{
				try
				{
					fntFont = FontManager.GetFont(strFontFamily, p_fltSize, p_fstStyle);
					if (fntFont == null)
						fntFont = new Font(strFontFamily, p_fltSize, p_fstStyle);
				}
				catch
				{
					//the font doesn't work for some reason - keep looking
				}
				if (fntFont != null)
				{
					m_dicFamilyCache[p_fstStyle] = fntFont.FontFamily;
					return fntFont;
				}
			}
			return null;
		}

		#endregion
	}
}