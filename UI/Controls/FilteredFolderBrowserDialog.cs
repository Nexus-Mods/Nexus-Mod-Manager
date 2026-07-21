using System;
using System.Windows.Forms;
using System.IO;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Collections.Generic;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// Alows selection of files from a folder that match a given pattern.
	/// </summary>
	public partial class FilteredFolderBrowserDialog : Form
	{
		/// <summary>
		/// Enumerates the different types of file filters.
		/// </summary>
		public enum FilterType
		{
			/// <summary>
			/// A simple wildcard-based filter.
			/// </summary>
			/// <remarks>
			/// Wildcard filters can match file names using * and ?.
			/// </remarks>
			Wildcard,

			/// <summary>
			/// A regular expression filter.
			/// </summary>
			/// <remarks>
			/// Regular expression filters match file names using a regular expression.
			/// </remarks>
			Regex
		}

		private Environment.SpecialFolder m_sfrRootFolder = Environment.SpecialFolder.Desktop;

		#region Properties

		/// <summary>
		/// Gets the selected file filter type.
		/// </summary>
		/// <value>The selected file filter type.</value>
		[Browsable(true)]
		[Category("Behavior")]
		[DefaultValue(FilterType.Wildcard)]
		public FilterType FileFilterType
		{
			get
			{
				return (FilterType)cbxFilterType.SelectedValue;
			}
		}

		/// <summary>
		/// Gets or sets the file filter.
		/// </summary>
		/// <value>The file filter.</value>
		public string FileFilter
		{
			get
			{
				return tbxFileFilter.Text;
			}
			set
			{
				tbxFileFilter.Text = value;
			}
		}

		/// <summary>
		/// Sets whether the new folder button is visible.
		/// </summary>
		/// <value>Whether the new folder button is visible.</value>
		[Browsable(true)]
		[Category("Behavior")]
		[DefaultValue(true)]
		public bool ShowNewFolderButton
		{
			set
			{
				butNewFolder.Visible = value;
			}
		}

		/// <summary>
		/// Sets whether files are displayed.
		/// </summary>
		/// <value>Whether files are displayed.</value>
		[Browsable(true)]
		[Category("Behavior")]
		[DefaultValue(false)]
		public bool ShowFiles
		{
			set
			{
				ftvFileSystem.ShowFiles = value;
			}
		}

		/// <summary>
		/// Gets or sets the selected path.
		/// </summary>
		/// <value>The selected path.</value>
		public string SelectedPath
		{
			get
			{
				if (ftvFileSystem.SelectedNode == null)
					return null;
				return ftvFileSystem.SelectedNode.FullPath;
			}
			set
			{
				string strPathRoot = null;
				if (RootFolder == Environment.SpecialFolder.MyComputer)
					strPathRoot = "";
				else
					strPathRoot = Path.GetDirectoryName(Environment.GetFolderPath(RootFolder));

				if (!value.StartsWith(strPathRoot, StringComparison.OrdinalIgnoreCase))
					throw new ArgumentException("The given path must be rooted in: " + strPathRoot);
				string[] strPaths = value.Split(new char[] { Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
				string[] strRoot = strPathRoot.Split(new char[] { Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
				string strPath = String.Join(Path.DirectorySeparatorChar.ToString(), strPaths, strRoot.Length, strPaths.Length - strRoot.Length);
				ftvFileSystem.SetSelectedNode(strPath);
			}
		}

		/// <summary>
		/// Gets or sets whether to match files in subfolders of the <see cref="SelectedPath"/>.
		/// </summary>
		public bool SearchSubfolders
		{
			get
			{
				return ckbRecurse.Checked;
			}
			set
			{
				ckbRecurse.Checked = value;
			}
		}

		/// <summary>
		/// Gets the list of files in the <see cref="SelectedPath"/> that match the filter.
		/// </summary>
		/// <remarks>
		/// The list of matched fiels will only include files in subfolders if <see cref="SearchSubfolders"/>
		/// is <c>true</c>.
		/// </remarks>
		/// <value>The list of files in the <see cref="SelectedPath"/> that match the filter.</value>
		public List<string> MatchedFiles
		{
			get
			{
				return MatchFiles(SelectedPath, SearchSubfolders, FileFilter, FileFilterType);
			}
		}

		/// <summary>
		/// Gets or sets the message displayed at the top of the window.
		/// </summary>
		/// <value>The message displayed at the top of the window.</value>
		[Browsable(true)]
		[Category("Appearance")]
		public string Description
		{
			get
			{
				return autosizeLabel1.Text;
			}
			set
			{
				autosizeLabel1.Text = value;
				pnlDescription.Visible = !String.IsNullOrEmpty(autosizeLabel1.Text);
			}
		}

		/// <summary>
		/// Gets or sets the root folder of the browser dialog.
		/// </summary>
		/// <value>The root folder of the browser dialog.</value>
		[Browsable(true)]
		[Category("Behavior")]
		[DefaultValue(System.Environment.SpecialFolder.Desktop)]
		public Environment.SpecialFolder RootFolder
		{
			get
			{
				return m_sfrRootFolder;
			}
			set
			{
				if (!value.Equals(m_sfrRootFolder))
				{
					m_sfrRootFolder = value;
					ftvFileSystem.Nodes.Clear();
					if (value != Environment.SpecialFolder.MyComputer)
					{
						ftvFileSystem.AddPath(Path.GetDirectoryName(Environment.GetFolderPath(value)));
						ftvFileSystem.Nodes[0].Expand();
						ftvFileSystem.ShowRootLines = false;
					}
					else
					{
						DriveInfo[] difDrives = DriveInfo.GetDrives();
						foreach (DriveInfo difDrive in difDrives)
							ftvFileSystem.AddPath(difDrive.RootDirectory.FullName);
						ftvFileSystem.ShowRootLines = true;
					}
				}
			}
		}

		#endregion

		/// <summary>
		/// The default constructor.
		/// </summary>
		public FilteredFolderBrowserDialog()
		{
			InitializeComponent();
			pnlDescription.Visible = !String.IsNullOrEmpty(autosizeLabel1.Text);
			
			cbxFilterType.DataSource = new[] {new { Name = "Wildcard", Type = FilterType.Wildcard },
											new { Name = "Regular Expression", Type = FilterType.Regex }};
			cbxFilterType.DisplayMember = "Name";
			cbxFilterType.ValueMember = "Type";
			cbxFilterType.SelectedIndex = 0;
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the new folder button.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event properties.</param>
		private void butNewFolder_Click(object sender, EventArgs e)
		{
			FileSystemTreeNode tndRoot = (FileSystemTreeNode)ftvFileSystem.SelectedNode;
			if (tndRoot == null)
				tndRoot = (FileSystemTreeNode)ftvFileSystem.Nodes[0];
			string strNewFolderName = Path.Combine(tndRoot.LastSource, "New Folder");
			for (Int32 i = 0; i < Int32.MaxValue && Directory.Exists(strNewFolderName); i++)
				strNewFolderName = Path.Combine(tndRoot.LastSource, "New Folder " + i);
			Directory.CreateDirectory(strNewFolderName);
			FileSystemTreeNode tndNewNode = ftvFileSystem.AddPath(tndRoot, strNewFolderName);
			if (tndRoot != null)
				tndRoot.Expand();
			//make sure the node being edited is the only one selected
			ftvFileSystem.SelectedNode = tndNewNode;
			tndNewNode.BeginEdit();
		}

		/// <summary>
		/// Finds all files matching the filter in the specified directory, and optionally subdirectories.
		/// </summary>
		/// <param name="p_strSelectedPath">The path in which to match files.</param>
		/// <param name="p_booSearchSubfolders">Whether to match files in subdirectories of <paramref name="p_strSelectedPath"/>.</param>
		/// <param name="p_strFileFilter">The filter to use to match files.</param>
		/// <param name="p_ftpFileFilterType">The type of file filter used to match files.</param>
		/// <returns>All files matching the filter in the specified directory, and optionally subdirectories.</returns>
		protected List<string> MatchFiles(string p_strSelectedPath, bool p_booSearchSubfolders, string p_strFileFilter, FilterType p_ftpFileFilterType)
		{
			List<string> lstFiles = new List<string>();
			switch (p_ftpFileFilterType)
			{
				case FilterType.Wildcard:
					string strFilter = String.IsNullOrEmpty(p_strFileFilter) ? "*" : p_strFileFilter;
					lstFiles.AddRange(Directory.GetFiles(p_strSelectedPath, strFilter, p_booSearchSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly));
					break;
				case FilterType.Regex:
					string[] strFiles = Directory.GetFiles(p_strSelectedPath, "*", p_booSearchSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
					Regex rgxFilter = new Regex(String.IsNullOrEmpty(p_strFileFilter) ? ".*" : p_strFileFilter, RegexOptions.IgnoreCase);
					foreach (string strFile in strFiles)
						if (rgxFilter.IsMatch(Path.GetFileName(strFile)))
							lstFiles.Add(strFile);
					break;
				default:
					throw new Exception("Invalid value for FilterType.");
			}
			return lstFiles;
		}
	}
}
