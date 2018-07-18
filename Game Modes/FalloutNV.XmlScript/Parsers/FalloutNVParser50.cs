using System.Xml.Linq;
using Nexus.Client.ModManagement.Scripting.XmlScript;
using Nexus.Client.ModManagement.Scripting.XmlScript.Parsers;

namespace Nexus.Client.Games.FalloutNV.Scripting.XmlScript.Parsers
{
	/// <summary>
	/// Provides Fallout: New Vegas specific parsing for version 5.0 xml script files.
	/// </summary>
	public class FalloutNVParser50 : Parser50
	{
		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_xelScript">The xmlscript file.</param>
		/// <param name="p_xstXmlScriptType">The <see cref="XmlScriptType"/> that describes
		/// XML script type metadata.</param>
		public FalloutNVParser50(XElement p_xelScript, XmlScriptType p_xstXmlScriptType)
			: base(p_xelScript, p_xstXmlScriptType)
		{
		}

		#endregion

		/// <summary>
		/// Reads the condition from the given node.
		/// </summary>
		/// <param name="p_xelCondition">The node from which to load the condition.</param>
		/// <returns>An <see cref="ICondition"/> representing the condition described in the given node.</returns>
		protected override ICondition LoadCondition(XElement p_xelCondition)
		{
			return FONVParser20Helper.ParseCondition(p_xelCondition) ?? base.LoadCondition(p_xelCondition);
		}
	}
}
