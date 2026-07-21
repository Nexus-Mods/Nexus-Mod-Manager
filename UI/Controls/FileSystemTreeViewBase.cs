using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows.Forms;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// An event arguments class that indicates that a node has changed.
	/// </summary>
	public class NodesChangedEventArgs : EventArgs
	{
		#region Properties

		/// <summary>
		/// Gets the node that has changed.
		/// </summary>
		/// <value>The node that has changed.</value>
		public FileSystemTreeNode ChangedNode { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_tndNode">The node that has changed.</param>
		public NodesChangedEventArgs(FileSystemTreeNode p_tndNode)
		{
			ChangedNode = p_tndNode;
		}

		#endregion
	}

	/// <summary>
	/// Te delegate definition for node changed events.
	/// </summary>
	/// <param name="sender">The object that raised the event.</param>
	/// <param name="e">A <see cref="NodesChangedEventArgs"/> describing the event arguments.</param>
	public delegate void NodesChangedEventHandler(object sender, NodesChangedEventArgs e);

	/// <summary>
	/// A tree view that allow browsing through a file system.
	/// </summary>
	/// <remarks>
	/// The tree view will browse into archives.
	/// </remarks>
	public class FileSystemTreeViewBase : MultiSelectTreeView
	{
		/// <summary>
		/// Raised whenever a node is added to the tree view.
		/// </summary>
		public event NodesChangedEventHandler NodeAdded = delegate { };

		/// <summary>
		/// Raised whenever a node is removed from the tree view.
		/// </summary>
		public event NodesChangedEventHandler NodeRemoved = delegate { };

		#region Properties

		/// <summary>
		/// Gets or sets whether or not the tree view should display files.
		/// </summary>
		/// <value>Whether or not the tree view should display files.</value>
		[Browsable(true)]
		[Category("Behavior")]
		[DefaultValue(true)]
		public bool ShowFiles { get; set; }

		/// <summary>
		/// Gets or sets whether the tree view will navigate into archives.
		/// </summary>
		/// <value>Whether the tree view will navigate into archives.</value>
		[Browsable(true)]
		[Category("Behavior")]
		[DefaultValue(false)]
		public bool BrowseIntoArchives { get; set; }

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public FileSystemTreeViewBase()
		{
			ShowFiles = true;
			BrowseIntoArchives = false;

			ImageList = new ImageList();
			ImageList.TransparentColor = System.Drawing.Color.Transparent;
			ImageList.Images.Add(Properties.Resources.Folder_Open);
			ImageList.Images.SetKeyName(0, "folder");

			this.ImageIndex = 0;
			this.SelectedImageIndex = 0;

			this.TreeViewNodeSorter = new Comparer<FileSystemTreeNode>();
		}

		#endregion

		#region Path Addition

		/// <summary>
		/// Addes the specified files to the source tree.
		/// </summary>
		/// <param name="p_strFileNames">The paths to add to the source tree.</param>
		public void AddPaths(string[] p_strFileNames)
		{
			Cursor crsOldCursor = Cursor;
			Cursor = Cursors.WaitCursor;
			foreach (string strFile in p_strFileNames)
				AddPath(strFile);
			Cursor = crsOldCursor;
		}

		/// <summary>
		/// Addes the specified files to the source tree.
		/// </summary>
		/// <param name="p_tndRoot">The node to which to add the file/folder.</param>
		/// <param name="p_strFileNames">The paths to add to the source tree.</param>
		public void AddPaths(FileSystemTreeNode p_tndRoot, string[] p_strFileNames)
		{
			Cursor crsOldCursor = Cursor;
			Cursor = Cursors.WaitCursor;
			foreach (string strFile in p_strFileNames)
				AddPath(p_tndRoot, strFile);
			Cursor = crsOldCursor;
		}

		/// <summary>
		/// Adds the given file to the source tree.
		/// </summary>
		/// <param name="p_strFile">The file to add.</param>
		public void AddPath(string p_strFile)
		{
			AddPath(null, p_strFile);
		}

		/// <summary>
		/// This adds a file/folder to the source file structure.
		/// </summary>
		/// <param name="p_tndRoot">The node to which to add the file/folder.</param>
		/// <param name="p_strFile">The path to add to the source file structure.</param>
		public FileSystemTreeNode AddPath(FileSystemTreeNode p_tndRoot, string p_strFile)
		{
			if ((p_tndRoot != null) && !p_tndRoot.IsDirectory && (!BrowseIntoArchives || !p_tndRoot.IsArchive))
				return AddPath(p_tndRoot.Parent, p_strFile);
			Cursor crsOldCursor = Cursor;
			Cursor = Cursors.WaitCursor;
			try
			{
				if (!Archive.IsArchivePath(p_strFile))
				{
					FileSystemInfo fsiInfo = null;
					if (Directory.Exists(p_strFile))
						fsiInfo = new DirectoryInfo(p_strFile);
					else if (ShowFiles && File.Exists(p_strFile) && !".lnk".Equals(Path.GetExtension(p_strFile), StringComparison.OrdinalIgnoreCase))
					{
						fsiInfo = new FileInfo(p_strFile);
					}
					else
						return null;
					if (!FileUtil.IsDrivePath(p_strFile) && ((fsiInfo.Attributes & FileAttributes.System) > 0))
						return null;
				}

				FileSystemTreeNode tndFile = null;
				System.Windows.Forms.TreeNodeCollection tncSiblings = (p_tndRoot == null) ? this.Nodes : p_tndRoot.Nodes;
				string strName = Path.GetFileName(p_strFile);
				if (String.IsNullOrEmpty(strName))
					strName = p_strFile;
				if (tncSiblings.ContainsKey(strName))
				{
					tndFile = (FileSystemTreeNode)tncSiblings[strName];
					tndFile.AddSource(p_strFile, false);
				}
				else
				{
					tndFile = new FileSystemTreeNode(strName, p_strFile);
					tndFile.Name = strName;
					tncSiblings.Add(tndFile);
					OnNodeAdded(new NodesChangedEventArgs(tndFile));
				}
				SetNodeImage(tndFile);

				if (tndFile.IsDirectory)
				{
					if (ShouldLoadChildren(tndFile))
						LoadChildren(tndFile);
				}
				else
				{
					if (BrowseIntoArchives && tndFile.IsArchive)
					{
						if (ShouldLoadChildren(tndFile))
							LoadChildren(tndFile);
					}
					else
						tndFile.Sources[p_strFile].IsLoaded = true;
				}
				return tndFile;
			}
			finally
			{
				Cursor = crsOldCursor;
			}
		}

		/// <summary>
		/// Determines if the children of the given node should be loaded.
		/// </summary>
		/// <param name="p_fsnItem">The node for which it is to be determined if the children should be loaded.</param>
		/// <returns><c>true</c> if teh children should be loaded;
		/// <c>false</c> otherwise.</returns>
		protected virtual bool ShouldLoadChildren(FileSystemTreeNode p_fsnItem)
		{
			return true;
		}

		/// <summary>
		/// Loads the children of the given node.
		/// </summary>
		/// <param name="p_tndFolder">The node whose children are to be loaded.</param>
		protected void LoadChildren(FileSystemTreeNode p_tndFolder)
		{
			if (p_tndFolder.LastSource.IsLoaded || !p_tndFolder.IsDirectory && (!BrowseIntoArchives || !p_tndFolder.IsArchive))
				return;
			Cursor crsOldCursor = Cursor;
			Cursor = Cursors.WaitCursor;
			try
			{
				p_tndFolder.LastSource.IsLoaded = true;
				string strPath = p_tndFolder.LastSource;
				if (BrowseIntoArchives && (Archive.IsArchivePath(strPath) || p_tndFolder.IsArchive))
				{
					KeyValuePair<string, string> kvpPath = new KeyValuePair<string, string>(p_tndFolder.LastSource, Path.DirectorySeparatorChar.ToString());
					if (Archive.IsArchivePath(strPath))
						kvpPath = Archive.ParseArchivePath(strPath);
					Archive arcArchive = new Archive(kvpPath.Key);
					string[] strFolders = arcArchive.GetDirectories(kvpPath.Value);
					for (Int32 i = 0; i < strFolders.Length; i++)
						AddPath(p_tndFolder, Archive.GenerateArchivePath(kvpPath.Key, strFolders[i]));
					if (ShowFiles)
					{
						string[] strFiles = arcArchive.GetFiles(kvpPath.Value, false);
						for (Int32 i = 0; i < strFiles.Length; i++)
							AddPath(p_tndFolder, Archive.GenerateArchivePath(kvpPath.Key, strFiles[i]));
					}
				}
				else
				{
					try
					{
						string[] strFolders = Directory.GetDirectories(strPath);
						for (Int32 i = 0; i < strFolders.Length; i++)
							AddPath(p_tndFolder, strFolders[i]);
						if (ShowFiles)
						{
							string[] strFiles = Directory.GetFiles(strPath);
							for (Int32 i = 0; i < strFiles.Length; i++)
								AddPath(p_tndFolder, strFiles[i]);
						}
					}
					catch (UnauthorizedAccessException)
					{
					}
				}
			}
			finally
			{
				Cursor = crsOldCursor;
			}
		}

		#endregion

		#region Node Removal

		/// <summary>
		/// Removes the specified nodes from the tree view.
		/// </summary>
		/// <remarks>
		/// The user is asked to confirm the removal.
		/// </remarks>
		/// <param name="p_lstNodes">The nodes to remove.</param>
		public void RemoveNodes(IList<TreeNode> p_lstNodes)
		{
			//make a copy in case removing nodes changes the given p_lstNodes
			List<TreeNode> lstNodesToRemove = new List<TreeNode>(p_lstNodes);
			if (lstNodesToRemove.IsNullOrEmpty())
				return;
			string strMessage = null;
			if (lstNodesToRemove.Count == 1)
				strMessage = String.Format("Are you sure you want to removed '{0}?'", lstNodesToRemove[0].Text);
			else
				strMessage = String.Format("Are you sure you want to removed the {0} selected nodes?", lstNodesToRemove.Count);
			if (MessageBox.Show(this.FindForm(), strMessage, "Confirm Delete", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
			{
				FileSystemTreeNode tndNode = null;
				for (Int32 i = lstNodesToRemove.Count - 1; i >= 0; i--)
				{
					tndNode = (FileSystemTreeNode)lstNodesToRemove[i];
					tndNode.Remove();
					OnNodeRemoved(new NodesChangedEventArgs(tndNode));
				}
			}
		}

		/// <summary>
		/// Raises the <see cref="Control.OnKeyDown"/> event of the tree view.
		/// </summary>
		/// <remarks>
		/// This makes the delete key remove the selected nodes.
		/// </remarks>
		/// <param name="e">A <see cref="KeyEventArgs"/> describing the event arguments.</param>
		protected override void OnKeyDown(KeyEventArgs e)
		{
			switch (e.KeyCode)
			{
				case Keys.Delete:
					if (SelectedNodes.Count > 0)
						RemoveNodes(SelectedNodes);
					break;
			}
			base.OnKeyDown(e);
		}

		#endregion

		#region Event Raising

		/// <summary>
		/// Raises the <see cref="NodeAdded"/> event.
		/// </summary>
		/// <param name="e">A <see cref="NodesChangedEventArgs"/> describing the event arguments.</param>
		protected virtual void OnNodeAdded(NodesChangedEventArgs e)
		{
			NodeAdded(this, e);
		}

		/// <summary>
		/// Raises the <see cref="NodeRemoved"/> event.
		/// </summary>
		/// <param name="e">A <see cref="NodesChangedEventArgs"/> describing the event arguments.</param>
		protected virtual void OnNodeRemoved(NodesChangedEventArgs e)
		{
			NodeRemoved(this, e);
		}

		#endregion

		/// <summary>
		/// Sets the image of the given node.
		/// </summary>
		/// <param name="p_fsnItem">The node whose image shuld be set.</param>
		protected virtual void SetNodeImage(FileSystemTreeNode p_fsnItem)
		{
			if (p_fsnItem.IsDirectory)
			{
				p_fsnItem.ImageKey = "folder";
				p_fsnItem.SelectedImageKey = "folder";
			}
			else
			{
				string strExtension = Path.GetExtension(p_fsnItem.LastSource).ToLowerInvariant();
				if (!ImageList.Images.ContainsKey(strExtension))
				{
					//this method should work, and it should be faster; however, it seems to
					// always return the generic file icon - no idea why
					/*if (!Archive.IsArchivePath(p_fsnItem.LastSource))
						ImageList.Images.Add(strExtension, System.Drawing.Icon.ExtractAssociatedIcon(p_fsnItem.LastSource));
					else*/
					{
						string strIconPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()) + strExtension;
						File.CreateText(strIconPath).Close();
						ImageList.Images.Add(strExtension, System.Drawing.Icon.ExtractAssociatedIcon(strIconPath));
						File.Delete(strIconPath);
					}
				}
				p_fsnItem.ImageKey = strExtension;
				p_fsnItem.SelectedImageKey = strExtension;
			}
		}

		/// <summary>
		/// Finds the specified node, or the specified node's nearest ancestor if the specified
		/// node does not exist.
		/// </summary>
		/// <param name="p_tndRoot">The node under which to search for the specified node.</param>
		/// <param name="p_strPath">The path to the node to be found.</param>
		/// <param name="p_queMissingPath">An out parameter that returns the path parts that were not found.</param>
		/// <returns>The specified node, it if exists. If it does not exist, then the specified node's
		/// neasrest ancestor. If no ancestor exists, then <c>null</c> is returned.</returns>
		protected FileSystemTreeNode FindNearestAncestor(FileSystemTreeNode p_tndRoot, string p_strPath, out Queue<string> p_queMissingPath)
		{
			string[] strPaths = p_strPath.Split(new char[] { Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
			if (strPaths[0].EndsWith(Path.VolumeSeparatorChar.ToString(), StringComparison.OrdinalIgnoreCase))
				strPaths[0] = strPaths[0] + Path.DirectorySeparatorChar;
			Queue<string> quePath = new Queue<string>(strPaths);
			FileSystemTreeNode tndSelectedNode = null;
			System.Windows.Forms.TreeNodeCollection tncCurrentLevel = (p_tndRoot == null) ? Nodes : p_tndRoot.Nodes;
			string strCurrentPath = null;
			while (quePath.Count > 0)
			{
				strCurrentPath = quePath.Peek();
				foreach (FileSystemTreeNode tndCurrent in tncCurrentLevel)
				{
					if (tndCurrent.Text.Equals(strCurrentPath, StringComparison.OrdinalIgnoreCase))
					{
						tndSelectedNode = tndCurrent;
						break;
					}
				}
				if ((tndSelectedNode == null) || (tncCurrentLevel == tndSelectedNode.Nodes))
					break;
				tncCurrentLevel = tndSelectedNode.Nodes;
				quePath.Dequeue();
			}
			p_queMissingPath = quePath;
			return tndSelectedNode;
		}

		/// <summary>
		/// Sets the selected node to the one specifide by the given path.
		/// </summary>
		/// <param name="p_strPath">The path to the node to be selected.</param>
		public void SetSelectedNode(string p_strPath)
		{
			if (String.IsNullOrEmpty(p_strPath))
				return;
			string[] strPaths = p_strPath.Split(new char[] { Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
			if (strPaths[0].EndsWith(Path.VolumeSeparatorChar.ToString(), StringComparison.OrdinalIgnoreCase))
				strPaths[0] = strPaths[0] + Path.DirectorySeparatorChar;
			TreeNode tndSelectedNode = null;
			System.Windows.Forms.TreeNodeCollection tncCurrentLevel = Nodes;
			for (Int32 i = 0; i < strPaths.Length; i++)
			{
				foreach (TreeNode tndCurrent in tncCurrentLevel)
				{
					if (tndCurrent.Text.Equals(strPaths[i], StringComparison.OrdinalIgnoreCase))
					{
						tndSelectedNode = tndCurrent;
						break;
					}
				}
				if ((tndSelectedNode == null) || (tncCurrentLevel == tndSelectedNode.Nodes))
					break;
				LoadChildren((FileSystemTreeNode)tndSelectedNode);
				tncCurrentLevel = tndSelectedNode.Nodes;
			}
			SelectedNode = tndSelectedNode;
		}
	}
}
