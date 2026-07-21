
namespace Nexus.UI.Controls
{
	/// <summary>
	/// Describes the methods and propertied of an XML tree node formatter.
	/// </summary>
	/// <remarks>
	/// An XML Tree Node Formatter sets the format of <see cref="XmlTreeNode"/>s.
	/// </remarks>
	public interface IXmlTreeNodeFormatter
	{
		/// <summary>
		/// Formats the given node.
		/// </summary>
		/// <param name="p_xtnNode">The node to format.</param>
		void FormatNode(XmlTreeNode p_xtnNode);
	}
}
