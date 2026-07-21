using System;
using System.Collections.Generic;
using System.Drawing;
using Nexus.UI;

namespace Nexus.Client.Games
{
	/// <summary>
	/// Describes visual features of an application.
	/// </summary>
	public class Theme
	{
		#region Properties

		/// <summary>
		/// Gets the icon to use for this theme.
		/// </summary>
		/// <value>The icon to use for this theme.</value>
		public Icon Icon
		{
			get;
			private set;
		}

		/// <summary>
		/// Gets the theme's font sets.
		/// </summary>
		/// <value>The theme's font sets.</value>
		public FontSetGroup FontSets
		{
			get;
			private set;
		}

		/// <summary>
		/// Gets the theme's primary colour.
		/// </summary>
		/// <value>The theme's primary colour.</value>
		public Color PrimaryColour
		{
			get;
			private set;
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the theme.
		/// </summary>
		/// <param name="p_icnIcon">The icon to use for this theme.</param>
		/// <param name="p_clrPrimary">The theme's primary colour.</param>
		/// <param name="p_fsgFontSets">The theme's font sets.</param>
		public Theme(Icon p_icnIcon, Color p_clrPrimary, FontSetGroup p_fsgFontSets)
		{
			Icon = p_icnIcon;
			PrimaryColour = p_clrPrimary;
			FontSets = p_fsgFontSets ?? new FontSetGroup();
		}

		#endregion
	}
}