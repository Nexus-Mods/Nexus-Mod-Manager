using System;
using System.Xml.Linq;
using Nexus.Client.ModManagement.Scripting.XmlScript;
using Nexus.Client.ModManagement.Scripting.XmlScript.Parsers;

namespace Nexus.Client.Games.Skyrim.Scripting.XmlScript.Parsers
{
	/// <summary>
	/// A helper class that encapsulates parsing of Skyrim specific XML Script features.
	/// </summary>
	/// <remarks>
	/// This class encapsulates functionality that is used by several parsers, so as to prevent
	/// code duplication.
	/// </remarks>
	public static class SkyrimParser20Helper
	{
		/// <summary>
		/// Reads the condition from the given node.
		/// </summary>
		/// <param name="p_xelCondition">The node from which to load the condition.</param>
		/// <returns>An <see cref="ICondition"/> representing the condition described in the given node.</returns>
		public static ICondition ParseCondition(XElement p_xelCondition)
		{
			if (p_xelCondition == null)
				return null;
			switch (p_xelCondition.Name.LocalName)
			{
				case "skseDependency":
					Version verMinFoseVersion = Parser.ParseVersion(p_xelCondition.Attribute("version").Value);
					return new SkseCondition(verMinFoseVersion);
				default:
					return null;
			}
		}
	}
}
