using System;
using System.Windows.Forms;
using System.Collections.Generic;
using System.IO;
using Nexus.Client.Util;
using Nexus.Client.Controls;

namespace Nexus.Client.ModAuthoring.Controls
{
	/// <summary>
	/// The file structure of a mod.
	/// </summary>
	public partial class ModFilesTreeView : UserControl
	{
		private FilteredFolderBrowserDialog m_ffbFileChooser = new FilteredFolderBrowserDialog();

		/// <summary>
		/// Gets or sets the sources listed in the control.
		/// </summary>
		/// <value>The sources listed in the control.</value>
		public List<VirtualFileSystemItem> Sources
		{
			get
			{
				return ftvSource.FileSystemItems;
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
		/// Handles the <see cref="Control.Click"/> event of the add older button.
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
	}
}
