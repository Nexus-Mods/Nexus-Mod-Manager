using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Nexus.Client.Util;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// A tree view that allow browsing through a file system.
	/// </summary>
	/// <remarks>
	/// The tree view will browse into archives.
	/// </remarks>
	public class FileSystemTreeView : FileSystemTreeViewBase
	{
		#region Properties

		/// <summary>
		/// Gets or sets the sources listed in the control.
		/// </summary>
		/// <value>The sources listed in the control.</value>
		public string[] Sources
		{
			get
			{
				List<string> lstSource = new List<string>();
				foreach (FileSystemTreeNode tndSource in this.Nodes)
					lstSource.Add(tndSource.LastSource);
				return lstSource.ToArray();
			}
			set
			{
				this.Nodes.Clear();
				AddPaths(value);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public FileSystemTreeView()
		{
			ImageList.Images.Add(Properties.Resources.Hard_Drive);
			ImageList.Images.SetKeyName(1, "drive");
		}

		#endregion

		#region Path Addition

		/// <summary>
		/// Determines if the children of the given node should be loaded.
		/// </summary>
		/// <param name="p_fsnItem">The node for which it is to be determined if the children should be loaded.</param>
		/// <returns><c>true</c> if teh children should be loaded;
		/// <c>false</c> otherwise.</returns>
		protected override bool ShouldLoadChildren(FileSystemTreeNode p_fsnItem)
		{
			return (p_fsnItem.Parent == null) || (p_fsnItem.Parent.IsExpanded);
		}

		/// <summary>
		/// Loads the grand children of the given node.
		/// </summary>
		/// <param name="p_tndFolder">The node whose grand children are to be loaded.</param>
		protected void LoadGrandChildren(FileSystemTreeNode p_tndFolder)
		{
			Cursor crsOldCursor = Cursor;
			Cursor = Cursors.WaitCursor;
			try
			{
				foreach (FileSystemTreeNode tndFolder in p_tndFolder.Nodes)
				{
					LoadChildren(tndFolder);
				}
			}
			finally
			{
				Cursor = crsOldCursor;
			}
		}

		/// <summary>
		/// Raises the <see cref="TreeView.BeforeExpand"/> event of the tree view.
		/// </summary>
		/// <remarks>
		/// This handles retrieving the sub-files and sub-folders to display in the tree view.
		/// </remarks>
		/// <param name="e">A <see cref="TreeViewCancelEventArgs"/> that describes the event arguments.</param>
		protected override void OnBeforeExpand(TreeViewCancelEventArgs e)
		{
			LoadGrandChildren((FileSystemTreeNode)e.Node);
			base.OnBeforeExpand(e);
		}

		#endregion

		/// <summary>
		/// Sets the image of the given node.
		/// </summary>
		/// <param name="p_fsnItem">The node whose image shuld be set.</param>
		protected override void SetNodeImage(FileSystemTreeNode p_fsnItem)
		{
			if (p_fsnItem.IsDrive)
			{
				p_fsnItem.ImageKey = "drive";
				p_fsnItem.SelectedImageKey = "drive";
			}
			else
				base.SetNodeImage(p_fsnItem);
		}

		#region Node Renaming

		/// <summary>
		/// Raise the <see cref="TreeView.BeforeLabelEdit"/> event.
		/// </summary>
		/// <remarks>
		/// This prevents drive nodes from being renamed.
		/// </remarks>
		/// <param name="e">A <see cref="NodeLabelEditEventArgs"/> describing the event arguments.</param>
		protected override void OnBeforeLabelEdit(NodeLabelEditEventArgs e)
		{
			if (((FileSystemTreeNode)e.Node).IsDrive)
				e.CancelEdit = true;
			base.OnBeforeLabelEdit(e);
		}

		/// <summary>
		/// Raise the <see cref="TreeView.AfterLabelEdit"/> event.
		/// </summary>
		/// <remarks>
		/// This sorts the newly renamed node.
		/// </remarks>
		/// <param name="e">A <see cref="NodeLabelEditEventArgs"/> describing the event arguments.</param>
		protected override void OnAfterLabelEdit(NodeLabelEditEventArgs e)
		{
			if (e.Label == null)
				e.CancelEdit = true;
			else
			{
				string strOldPath = ((FileSystemTreeNode)e.Node).LastSource;
				string strNewPath = Path.Combine(Path.GetDirectoryName(((FileSystemTreeNode)e.Node).LastSource), e.Label);
				if (!Directory.Exists(strOldPath) && !strOldPath.Equals(strNewPath))
				{
					Directory.Move(strOldPath, strNewPath);
					((FileSystemTreeNode)e.Node).LastSource.Path = strNewPath;
					e.Node.Name = e.Label;
				}
				this.BeginInvoke((MethodInvoker)(() => { this.Sort(); }));
			}
			base.OnAfterLabelEdit(e);
		}

		#endregion
	}
}
