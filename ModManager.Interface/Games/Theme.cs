using System.Drawing;

namespace Nexus.Client.Games
{
	/// <summary>
	/// Describes visual features of an application.
	/// </summary>
	public class Theme
	{
		/// <summary>
		/// Gets the icon to use for this theme.
		/// </summary>
		/// <value>The icon to use for this theme.</value>
		public Icon Icon { get; private set; }

		/// <summary>
		/// Gets the theme's primary colour.
		/// </summary>
		/// <value>The theme's primary colour.</value>
		public Color PrimaryColour { get; private set; }

		/// <summary>
		/// A simple constructor that initializes the theme.
		/// </summary>
		/// <param name="p_icnIcon">The icon to use for this theme.</param>
		/// <param name="p_clrPrimary">The theme's primary colour.</param>
		public Theme(Icon p_icnIcon, Color p_clrPrimary)
		{
			Icon = p_icnIcon;
			PrimaryColour = p_clrPrimary;
		}
	}
}
