using System.Windows.Forms;
using System.Xml;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// A tree view that displays a representation of an XML file.
	/// </summary>
	public class XmlTreeView : TreeView
	{
		#region Properties

		/// <summary>
		/// Gets or sets the formatter to use to display XML nodes as
		/// nodes in the tree view.
		/// </summary>
		/// <value>The formatter to use to display XML nodes as
		/// nodes in the tree view.</value>
		public IXmlTreeNodeFormatter NodeFormatter { get; set; }

		/// <summary>
		/// Sets the XML file that is to be represented by the tree view.
		/// </summary>
		/// <value>The XML file that is to be represented by the tree view.</value>
		public XmlDocument Document
		{
			set
			{
				Nodes.Clear();
				foreach (XmlNode xndChild in value.ChildNodes)
					AddNode(xndChild);
			}
		}

		#endregion

		/// <summary>
		/// Adds the given node to the tree view.
		/// </summary>
		/// <param name="p_xndXmlNode">The node to add.</param>
		protected void AddNode(XmlNode p_xndXmlNode)
		{
			if (p_xndXmlNode.NodeType != XmlNodeType.Element)
				return;
			XmlTreeNode xtnNode = new XmlTreeNode(p_xndXmlNode);
			if (NodeFormatter != null)
				NodeFormatter.FormatNode(xtnNode);
			Nodes.Add(xtnNode);
			xtnNode.LoadChildren();
		}

		/// <summary>
		/// Raises the <see cref="TreeView.BeforeExpand"/> event.
		/// </summary>
		/// <remarks>
		/// This ensures that the children of a node that is about to be exanded are loaded.
		/// </remarks>
		/// <param name="e">A <see cref="TreeViewCancelEventArgs"/> describing the event arguments.</param>
		protected override void OnBeforeExpand(TreeViewCancelEventArgs e)
		{
			foreach (XmlTreeNode xtnChild in e.Node.Nodes)
				if (!xtnChild.IsLoaded)
					xtnChild.LoadChildren();
			base.OnBeforeExpand(e);
		}
	}
}
