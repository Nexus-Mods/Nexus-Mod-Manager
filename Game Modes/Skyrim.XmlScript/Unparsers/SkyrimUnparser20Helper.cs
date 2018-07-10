using System.Xml.Linq;
using Nexus.Client.ModManagement.Scripting.XmlScript;

namespace Nexus.Client.Games.Skyrim.Scripting.XmlScript.Unparsers
{
	/// <summary>
	/// A helper class that encapsulates unparsing of Skyrim specific XML Script features.
	/// </summary>
	/// <remarks>
	/// This class encapsulates functionality that is used by several unparsers, so as to prevent
	/// code duplication.
	/// </remarks>
	public static class SkyrimUnparser20Helper
	{
		/// <summary>
		/// Unparses the given <see cref="ICondition"/>.
		/// </summary>
		/// <remarks>
		/// This method provides Skyrim-specific unparsing of the condition. If the given condition
		/// is not Skyrim-specific, nothing in done.
		/// </remarks>
		/// <param name="p_cndCondition">The <see cref="ICondition"/> for which to generate XML.</param>
		/// <returns>The XML representation of the given <see cref="ICondition"/> if the condition
		/// is Fallout 3-specific; <c>null</c> otherwise.</returns>
		public static XElement UnparseCondition(ICondition p_cndCondition)
		{
			if (p_cndCondition is SkseCondition)
			{
				XElement xelFoseVersion = new XElement("skseDependency",
												new XAttribute("version", ((SkseCondition)p_cndCondition).MinimumVersion.ToString()));
				return xelFoseVersion;
			}
			return null;
		}
	}
}
