using System.Drawing;

namespace Nexus.UI
{
	/// <summary>
	/// Describes the methods and properties of a font set resolver.
	/// </summary>
	/// <remarks>
	/// A font set resolver determines which font to use for the <see cref="Nexus.UI.Controls.FontProvider"/>.
	/// </remarks>
	public interface IFontSetResolver
	{		
		/// <summary>
		/// Returns a font that best matches the described font set.
		/// </summary>
		/// <param name="p_fsiFontInfo">The information describing the desired font.</param>
		/// <returns>A font that best matches the described font set.</returns>
		Font RequestFont(FontSetInformation p_fsiFontInfo);
	}
}
