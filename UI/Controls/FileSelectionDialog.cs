using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Forms;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// A file selection dialog that allows the selection of an item in a virtual file system.
	/// </summary>
	public partial class FileSelectionDialog : Form
	{
		#region Properites

		/// <summary>
		/// Gets or sets the selected path.
		/// </summary>
		/// <value>The selected path.</value>
		public string SelectedPath
		{
			get
			{
				if (ftvFiles.SelectedNode == null)
					return null;
				return ftvFiles.SelectedNode.FullPath;
			}
			set
			{
				ftvFiles.SetSelectedNode(value);
				
			}
		}

		/// <summary>
		/// Sets the virtual file system items from which to allow selection of a file.
		/// </summary>
		/// <value>The virtual file system items from which to allow selection of a file.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public IList<VirtualFileSystemItem> FileSystemItems
		{
			set
			{
				ftvFiles.FileSystemItems = value;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public FileSelectionDialog()
		{
			InitializeComponent();
		}

		#endregion
	}
}
