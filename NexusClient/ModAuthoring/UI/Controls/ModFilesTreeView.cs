using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Forms;
using Nexus.UI.Controls;
using Nexus.Client.Util;

namespace Nexus.Client.ModAuthoring.UI.Controls
{
	/// <summary>
	/// The file structure of a mod.
	/// </summary>
	public partial class ModFilesTreeView : UserControl, INotifyPropertyChanged
	{
		#region INotifyPropertyChanged Members

		/// <summary>
		/// Raised whenever a property of the class changes.
		/// </summary>
		public event PropertyChangedEventHandler PropertyChanged = delegate { };

		#endregion

		private FilteredFolderBrowserDialog m_ffbFileChooser = new FilteredFolderBrowserDialog();

		/// <summary>
		/// Gets or sets the sources listed in the control.
		/// </summary>
		/// <value>The sources listed in the control.</value>
		[Bindable(true)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public IList<VirtualFileSystemItem> Sources
		{
			get
			{
				return ftvSource.FileSystemItems;
			}
			set
			{
				ftvSource.FileSystemItems = value;
			}
		}

		/// <summary>
		/// The default constructor.
		/// </summary>
		public ModFilesTreeView()
		{
			InitializeComponent();
			m_ffbFileChooser.ShowNewFolderButton = false;
			m_ffbFileChooser.Description = "Select the folder containing the files you would like to select. Then, specify a filter that will match the files you wish to select." + Environment.NewLine +
											"For example, a  filter of mymod*.dds will select alltexture files whose name start with mymod.";
			m_ffbFileChooser.RootFolder = Environment.SpecialFolder.MyComputer;
			m_ffbFileChooser.SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
			ftvSource.NodeAdded += new NodesChangedEventHandler(ftvSource_NodesChanged);
			ftvSource.NodeRemoved += new NodesChangedEventHandler(ftvSource_NodesChanged);
		}

		/// <summary>
		/// Handles the <see cref="FileSystemTreeViewBase.NodeAdded"/> and 
		/// <see cref="FileSystemTreeViewBase.NodeRemoved"/> events of the sources tree view.
		/// </summary>
		/// <remarks>
		/// This raised the <see cref="PropertyChanged"/> event on the <see cref="Sources"/> property.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="NodesChangedEventArgs"/> describing the event arguments.</param>
		private void ftvSource_NodesChanged(object sender, NodesChangedEventArgs e)
		{
			PropertyChanged(this, new PropertyChangedEventArgs(ObjectHelper.GetPropertyName(() => Sources)));
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the add files button.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void tspAddFiles_Click(object sender, EventArgs e)
		{
			TreeNode tndRoot = ftvSource.SelectedNode;
			if (ofdFileChooser.ShowDialog(this) == DialogResult.OK)
				ftvSource.AddPaths((FileSystemTreeNode)tndRoot, ofdFileChooser.FileNames);
			if (tndRoot != null)
				tndRoot.Expand();
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the add filtered files button.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void tspAddFilteredFiles_Click(object sender, EventArgs e)
		{
			TreeNode tndRoot = ftvSource.SelectedNode;
			if (m_ffbFileChooser.ShowDialog(this) == DialogResult.OK)
			{
				string strPathPrefix = m_ffbFileChooser.SelectedPath;
				List<VirtualFileSystemItem> lstVFSItems = new List<VirtualFileSystemItem>();
				foreach (string strPath in m_ffbFileChooser.MatchedFiles)
					lstVFSItems.Add(new VirtualFileSystemItem(strPath, strPath.Substring(strPathPrefix.Length), false));
				ftvSource.AddVirtualPaths((FileSystemTreeNode)tndRoot, lstVFSItems.ToArray());
			}
			if (tndRoot != null)
				tndRoot.Expand();
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the add folder button.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void tsbAddFolder_Click(object sender, EventArgs e)
		{
			TreeNode tndRoot = ftvSource.SelectedNode;
			if (fbdFolderChooser.ShowDialog(this) == DialogResult.OK)
				ftvSource.AddPath((FileSystemTreeNode)tndRoot, fbdFolderChooser.SelectedPath);
			if (tndRoot != null)
				tndRoot.Expand();
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the delete button.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void tsbDelete_Click(object sender, EventArgs e)
		{
			ftvSource.RemoveNodes(ftvSource.SelectedNodes);
		}
	}
}
