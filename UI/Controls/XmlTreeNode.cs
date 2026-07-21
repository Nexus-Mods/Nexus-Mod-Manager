using System.Windows.Forms;
using System.Xml;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// A node in an <see cref="XmlTreeView"/>.
	/// </summary>
	public class XmlTreeNode : TreeNode
	{
		#region Properties

		/// <summary>
		/// Gets or sets whether the node should generate child tree nodes for
		/// the children of the XML node being represented by this tree node.
		/// </summary>
		/// <value>Whether the node should generate child tree nodes for
		/// the children of the XML node being represented by this tree node.</value>
		public bool HideChildren { get; set; }

		/// <summary>
		/// Gets whether or not this node has loaded it's children.
		/// </summary>
		/// <value>Whether or not this node has loaded it's children.</value>
		public bool IsLoaded { get; private set; }

		/// <summary>
		/// Gets the XML node represented by this tree node.
		/// </summary>
		/// <value>The XML node represented by this tree node.</value>
		public XmlNode Node { get; private set; }

		#endregion

		#region Contructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_xndXmlNode">The XML node to represent with this tree node.</param>
		public XmlTreeNode(XmlNode p_xndXmlNode)
			: base(p_xndXmlNode.Name)
		{
			Node = p_xndXmlNode;
			IsLoaded = false;

			p_xndXmlNode.OwnerDocument.NodeChanged += new XmlNodeChangedEventHandler(OwnerDocument_NodeChanged);
			p_xndXmlNode.OwnerDocument.NodeInserted += new XmlNodeChangedEventHandler(OwnerDocument_NodeInserted);
			p_xndXmlNode.OwnerDocument.NodeRemoved += new XmlNodeChangedEventHandler(OwnerDocument_NodeRemoved);
		}

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_xndXmlNode">The XML node to represent with this tree node.</param>
		/// <param name="p_booHideChildren">Whether the node should generate child tree nodes for
		/// the children of the XML node being represented by this tree node.</param>
		public XmlTreeNode(XmlNode p_xndXmlNode, bool p_booHideChildren)
			: this(p_xndXmlNode)
		{
			HideChildren = p_booHideChildren;
		}

		#endregion

		/// <summary>
		/// Creates child tree nodes for each child of the XML node represented by this tree node.
		/// </summary>
		public void LoadChildren()
		{
			if (!HideChildren)
			{
				foreach (XmlNode xndChild in Node.ChildNodes)
				{
					XmlTreeNode xtnTreeNode = new XmlTreeNode(xndChild);
					if ((TreeView != null) && (((XmlTreeView)TreeView).NodeFormatter != null))
						((XmlTreeView)TreeView).NodeFormatter.FormatNode(xtnTreeNode);
					Nodes.Add(xtnTreeNode);
					if (IsExpanded)
						xtnTreeNode.LoadChildren();
				}
			}
			IsLoaded = true;
		}

		/// <summary>
		/// Handles the <see cref="XmlDocument.NodeRemoved"/> event of the document being represented
		/// by the tree view to which this node belongs.
		/// </summary>
		/// <remarks>
		/// This reformats the tree node if the XML node that was removed was a child of
		/// the XML node represented by this node.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="XmlNodeChangedEventArgs"/> describing the event arguments.</param>
		private void OwnerDocument_NodeRemoved(object sender, XmlNodeChangedEventArgs e)
		{
			if (e.OldParent == Node)
				((XmlTreeView)TreeView).NodeFormatter.FormatNode(this);
		}

		/// <summary>
		/// Handles the <see cref="XmlDocument.NodeInserted"/> event of the document being represented
		/// by the tree view to which this node belongs.
		/// </summary>
		/// <remarks>
		/// This reformats the tree node if the XML node that was inserted is a child of
		/// the XML node represented by this node.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="XmlNodeChangedEventArgs"/> describing the event arguments.</param>
		private void OwnerDocument_NodeInserted(object sender, XmlNodeChangedEventArgs e)
		{
			if (e.NewParent == Node)
				((XmlTreeView)TreeView).NodeFormatter.FormatNode(this);
		}

		/// <summary>
		/// Handles the <see cref="XmlDocument.NodeChanged"/> event of the document being represented
		/// by the tree view to which this node belongs.
		/// </summary>
		/// <remarks>
		/// This reformats the tree node to reflect the changes.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="XmlNodeChangedEventArgs"/> describing the event arguments.</param>
		private void OwnerDocument_NodeChanged(object sender, XmlNodeChangedEventArgs e)
		{
			XmlNode xndNode = e.Node;
			while ((xndNode.ParentNode != null) && !(xndNode is XmlElement))
				xndNode = xndNode.ParentNode;
			if (xndNode is XmlAttribute)
				xndNode = ((XmlAttribute)xndNode).OwnerElement;
			if (xndNode == Node)
				((XmlTreeView)TreeView).NodeFormatter.FormatNode(this);
		}
	}
}
