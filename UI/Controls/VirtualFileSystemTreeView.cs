using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// A tree view that represnts, and allow for the management of, a virtual file system.
	/// </summary>
	/// <remarks>
	/// The file system can be comprised of a mix real and virtual files and directories, all
	/// arranged in a virtual file structure.
	/// </remarks>
	public class VirtualFileSystemTreeView : FileSystemTreeViewBase
	{
		#region Properties

		/// <summary>
		/// Gets or sets the file system items listed in the control.
		/// </summary>
		/// <value>The file system items listed in the control.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public IList<VirtualFileSystemItem> FileSystemItems
		{
			get
			{
				List<VirtualFileSystemItem> lstItems = new List<VirtualFileSystemItem>();
				Stack<FileSystemTreeNode> stkNode = new Stack<FileSystemTreeNode>();
				foreach (FileSystemTreeNode tndChild in this.Nodes)
					stkNode.Push(tndChild);
				FileSystemTreeNode tndCurrent = null;
				while (stkNode.Count > 0)
				{
					tndCurrent = stkNode.Pop();
					lstItems.Add(new VirtualFileSystemItem(tndCurrent.LastSource, tndCurrent.FullPath, tndCurrent.IsDirectory));
					foreach (FileSystemTreeNode tndChild in tndCurrent.Nodes)
						stkNode.Push(tndChild);
				}
				return lstItems;
			}
			set
			{
				this.Nodes.Clear();
				if (value != null)
				{
					VirtualFileSystemItem[] vfiItems = new VirtualFileSystemItem[value.Count];
					value.CopyTo(vfiItems, 0);
					AddVirtualPaths(null, vfiItems);
				}
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public VirtualFileSystemTreeView()
		{
		}

		#endregion

		#region Path Addition

		/// <summary>
		/// Adds a virtual file system item to the tree view.
		/// </summary>
		/// <param name="p_tndRoot">The node to which to add the new item.</param>
		/// <param name="p_strName">The name of the new virtual item to add.</param>
		/// <returns>The new tree node representing the new virtual file system item.</returns>
		public FileSystemTreeNode AddVirtualNode(FileSystemTreeNode p_tndRoot, string p_strName)
		{
			if ((p_tndRoot != null) && !p_tndRoot.IsDirectory)
				return AddVirtualNode(p_tndRoot.Parent, p_strName);
			FileSystemTreeNode tndFile = null;
			System.Windows.Forms.TreeNodeCollection tncSiblings = (p_tndRoot == null) ? this.Nodes : p_tndRoot.Nodes;
			if (!tncSiblings.ContainsKey(p_strName))
				tndFile = (FileSystemTreeNode)tncSiblings[p_strName];
			else
			{
				string strName = Path.GetFileName(p_strName);
				if (String.IsNullOrEmpty(strName))
					strName = p_strName;
				tndFile = new FileSystemTreeNode(strName, null);
				tndFile.Name = p_strName;
				tncSiblings.Add(tndFile);
				OnNodeAdded(new NodesChangedEventArgs(tndFile));
			}
			return tndFile;
		}

		/// <summary>
		/// Addes the specified virtual paths to the source tree.
		/// </summary>
		/// <param name="p_tndRoot">The node to which to add the paths.</param>
		/// <param name="p_vfiPaths">The paths to add to the source tree.</param>
		public void AddVirtualPaths(FileSystemTreeNode p_tndRoot, VirtualFileSystemItem[] p_vfiPaths)
		{
			Cursor crsOldCursor = Cursor;
			Cursor = Cursors.WaitCursor;
			foreach (VirtualFileSystemItem vfiPath in p_vfiPaths)
				AddVirtualPath(p_tndRoot, vfiPath);
			Cursor = crsOldCursor;
		}

		/// <summary>
		/// Addes the specified virtual path to the source tree.
		/// </summary>
		/// <param name="p_tndRoot">The node to which to add the path.</param>
		/// <param name="p_vfiPath">The path to add to the source tree.</param>
		public void AddVirtualPath(FileSystemTreeNode p_tndRoot, VirtualFileSystemItem p_vfiPath)
		{
			Cursor crsOldCursor = Cursor;
			Cursor = Cursors.WaitCursor;

			Queue<string> queRemainingPath = null;
			FileSystemTreeNode tndNode = FindNearestAncestor(p_tndRoot, p_vfiPath.Path, out queRemainingPath);
			string strCurrentPath = null;
			while (queRemainingPath.Count > 0)
			{
				strCurrentPath = queRemainingPath.Dequeue();
				if ((queRemainingPath.Count > 0) || p_vfiPath.IsDirectory)
				{
					FileSystemTreeNode tndChild = new FileSystemTreeNode(strCurrentPath, null);
					((tndNode == null) ? Nodes : tndNode.Nodes).Add(tndChild);
					OnNodeAdded(new NodesChangedEventArgs(tndChild));
					tndNode = tndChild;
				}
				else
				{
					tndNode = AddPath(tndNode, p_vfiPath.Source);
				}
			}
			tndNode.AddSource(p_vfiPath.Source, true);

			Cursor = crsOldCursor;
		}

		#endregion

		#region Drag/Drop - Control as Destination

		/// <summary>
		/// Raises the <see cref="Control.DragEnter"/>.
		/// </summary>
		/// <remarks>
		/// This determines if the item being dragged can be dropped at the current location.
		/// </remarks>
		/// <param name="e">A <see cref="DragEventArgs"/> that describes the event arguments.</param>
		protected override void OnDragOver(DragEventArgs e)
		{
			base.OnDragOver(e);
			e.Effect = DragDropEffects.None;
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				string[] strFileNames = (string[])e.Data.GetData(DataFormats.FileDrop);
				foreach (string strFile in strFileNames)
					if ((Directory.Exists(strFile) || (File.Exists(strFile) && !".lnk".Equals(Path.GetExtension(strFile).ToLowerInvariant()))))
					{
						e.Effect = DragDropEffects.Copy;
						break;
					}
			}
			else if (e.Data.GetDataPresent(typeof(List<FileSystemTreeNode>)))
			{
				e.Effect = DragDropEffects.Move;
			}

			if (e.Effect != DragDropEffects.None)
			{
				FileSystemTreeNode tndFolder = (FileSystemTreeNode)GetNodeAt(PointToClient(new Point(e.X, e.Y)));
				if ((tndFolder != null) && !tndFolder.IsDirectory)
					tndFolder = tndFolder.Parent;
				SelectedNode = tndFolder;
			}
		}

		/// <summary>
		/// Raises the <see cref="Control.DragDrop"/>.
		/// </summary>
		/// <remarks>
		/// This handles adding the dropped file/folder to the source tree.
		/// </remarks>
		/// <param name="e">A <see cref="DragEventArgs"/> that describes the event arguments.</param>
		protected override void OnDragDrop(DragEventArgs e)
		{
			base.OnDragDrop(e);

			Cursor crsOldCursor = Cursor;
			Cursor = Cursors.WaitCursor;
			BeginUpdate();
			FileSystemTreeNode tndFolder = (FileSystemTreeNode)GetNodeAt(PointToClient(new Point(e.X, e.Y)));
			if ((tndFolder != null) && !tndFolder.IsDirectory)
				tndFolder = tndFolder.Parent;
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				string[] strFileNames = (string[])e.Data.GetData(DataFormats.FileDrop);
				if (strFileNames != null)
				{
					if (tndFolder != null)
					{
						AddPaths(tndFolder, strFileNames);
						tndFolder.Expand();
					}
					else
						AddPaths(null, strFileNames);
				}
			}
			else if (e.Data.GetDataPresent(typeof(List<FileSystemTreeNode>)))
			{
				List<FileSystemTreeNode> lstNodes = ((List<FileSystemTreeNode>)e.Data.GetData(typeof(List<FileSystemTreeNode>)));
				System.Windows.Forms.TreeNodeCollection tncSiblings = null;
				if (tndFolder == null)
					tncSiblings = Nodes;
				else
				{
					tncSiblings = tndFolder.Nodes;
					tndFolder.Expand();
				}
				for (Int32 i = 0; i < lstNodes.Count; i++)
				{
					lstNodes[i].Remove();
					tncSiblings.Add(lstNodes[i]);
				}
			}
			EndUpdate();
			Cursor = crsOldCursor;
		}

		/// <summary>
		/// Raises the <see cref="Control.QueryContinueDrag"/> event.
		/// </summary>
		/// <remarks>
		/// This aborts the drag operation of an item from the source tree view if the action is interrupted
		/// or stopped over something other than the virtual file system tree view.
		/// </remarks>
		/// <param name="e">A <see cref="QueryContinueDragEventArgs"/> that describes the event arguments.</param>
		protected override void OnQueryContinueDrag(QueryContinueDragEventArgs e)
		{
			base.OnQueryContinueDrag(e);
			if ((e.Action != DragAction.Drop) && ((Control.MouseButtons & MouseButtons.Left) != MouseButtons.Left))
				e.Action = DragAction.Cancel;
		}

		#endregion

		#region Drag/Drop - Control as Source

		/// <summary>
		/// Raises the <see cref="TreeView.ItemDrag"/> event.
		/// </summary>
		/// <remarks>
		/// This starts the drag operation of item in the tree view.
		/// </remarks>
		/// <param name="e">A <see cref="ItemDragEventArgs"/> that describes the event arguments.</param>
		protected override void OnItemDrag(ItemDragEventArgs e)
		{
			base.OnItemDrag(e);
			List<FileSystemTreeNode> lstAffectedNodes = new List<FileSystemTreeNode>();

			if (SelectedNodes.Contains((FileSystemTreeNode)e.Item))
				foreach (FileSystemTreeNode tndNode in SelectedNodes)
					lstAffectedNodes.Add(tndNode);
			else
			{
				lstAffectedNodes.Add((FileSystemTreeNode)e.Item);
			}
			DoDragDrop(lstAffectedNodes, DragDropEffects.Move);
		}

		#endregion
	}
}
