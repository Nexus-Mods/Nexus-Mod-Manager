using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Forms;
using Nexus.UI.Controls;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls
{
	public class XmlScriptTreeNode : TreeNode
	{
		public XmlScriptTreeNode(string p_strName)
			: base(p_strName)
		{
		}

		public virtual NodeEditor CreateEditor(IList<VirtualFileSystemItem> p_lstModFiles)
		{
			return null;
		}

		public XmlScriptTreeNode<T> AddNode<T>(T p_tObject, XmlScriptTreeNode<T>.EditorFactory<T> p_edfEditorFactory, XmlScriptTreeNode<T>.NodeFormatter<T> p_ftrFormatter, XmlScriptTreeNode<T>.PropertyChangedHandler<T> p_pchPropertyChangedHandler) where T : INotifyPropertyChanged
		{
			XmlScriptTreeNode<T> tndNode = new XmlScriptTreeNode<T>(p_tObject, p_edfEditorFactory, p_ftrFormatter, p_pchPropertyChangedHandler);
			Nodes.Add(tndNode);
			return tndNode;
		}
		
		public XmlScriptTreeNode<T> AddNode<T>(T p_tObject, XmlScriptTreeNode<T>.EditorFactory<T> p_edfEditorFactory, XmlScriptTreeNode<T>.NodeFormatter<T> p_ftrFormatter) where T : INotifyPropertyChanged
		{
			XmlScriptTreeNode<T> tndNode = new XmlScriptTreeNode<T>(p_tObject, p_edfEditorFactory, p_ftrFormatter);
			Nodes.Add(tndNode);
			return tndNode;
		}

		public XmlScriptTreeNode<T> AddNode<T>(T p_tObject, XmlScriptTreeNode<T>.NodeFormatter<T> p_ftrFormatter) where T : INotifyPropertyChanged
		{
			XmlScriptTreeNode<T> tndNode = new XmlScriptTreeNode<T>(p_tObject, p_ftrFormatter);
			Nodes.Add(tndNode);
			return tndNode;
		}

		public XmlScriptTreeNode AddNode(string p_strName)
		{
			XmlScriptTreeNode tndNode = new XmlScriptTreeNode(p_strName);
			Nodes.Add(tndNode);
			return tndNode;
		}

		/// <summary>
		/// Gets the object the node is representing.
		/// </summary>
		/// <returns>The object the node is representing, or <c>null</c> if the
		/// node is not representing a script object.</returns>
		public virtual object GetObject()
		{
			return null;
		}
	}

	public class XmlScriptTreeNode<T> : XmlScriptTreeNode where T : INotifyPropertyChanged
	{
		public delegate NodeEditor EditorFactory<K>(K p_kObject, IList<VirtualFileSystemItem> p_lstModFiles);
		public delegate void NodeFormatter<K>(XmlScriptTreeNode p_stnNode, K p_kObject);
		public delegate void PropertyChangedHandler<K>(XmlScriptTreeNode p_stnNode, K p_kObject, string p_strPropertyName);

		private NodeFormatter<T> m_ftrFormatter = null;
		private EditorFactory<T> m_edfEditorFactory = delegate { return null; };
		private PropertyChangedHandler<T> m_pchPropertyChangedHandler = delegate { };
		private T m_tObject = default(T);

		public T Object
		{
			get
			{
				return m_tObject;
			}
		}

		public XmlScriptTreeNode(T p_tObject, EditorFactory<T> p_edfEditorFactory, NodeFormatter<T> p_ftrFormatter, PropertyChangedHandler<T> p_pchPropertyChangedHandler)
			: this(p_tObject, p_edfEditorFactory, p_ftrFormatter)
		{
			m_pchPropertyChangedHandler += p_pchPropertyChangedHandler;
		}

		public XmlScriptTreeNode(T p_tObject, EditorFactory<T> p_edfEditorFactory, NodeFormatter<T> p_ftrFormatter)
			: this(p_tObject, p_ftrFormatter)
		{
			if (p_edfEditorFactory != null)
				m_edfEditorFactory = p_edfEditorFactory;
		}

		public XmlScriptTreeNode(T p_tObject, NodeFormatter<T> p_ftrFormatter)
			: base(null)
		{
			m_tObject = p_tObject;
			p_tObject.PropertyChanged += new PropertyChangedEventHandler(p_tObject_PropertyChanged);
			m_ftrFormatter = p_ftrFormatter ?? DefaultFormatter;
			m_ftrFormatter(this, m_tObject);
		}

		public override NodeEditor CreateEditor(IList<VirtualFileSystemItem> p_lstModFiles)
		{
			return m_edfEditorFactory(m_tObject, p_lstModFiles);
		}

		/// <summary>
		/// Gets the object the node is representing.
		/// </summary>
		/// <returns>The object the node is representing, or <c>null</c> if the
		/// node is not representing a script object.</returns>
		public override object GetObject()
		{
			return Object;
		}

		private void DefaultFormatter<K>(XmlScriptTreeNode p_stnNode, K p_kObject)
		{
			p_stnNode.Text = p_kObject.ToString();
		}

		private void p_tObject_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			m_ftrFormatter(this, m_tObject);
			m_pchPropertyChangedHandler(this, m_tObject, e.PropertyName);
		}
	}
}
