using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Forms;
using Nexus.Client.Commands.Generic;
using Nexus.UI.Controls;
using Nexus.Client.Util;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	/// <summary>
	/// A view for editing a list of <see cref="InstallableFile"/>s.
	/// </summary>
	public partial class FileListEditor : NodeEditor
	{
		/// <summary>
		/// Compares <see cref="ListViewItem"/>s that represent <see cref="InstallableFile"/>s.
		/// </summary>
		/// <remarks>The underlying <see cref="InstallableFile"/>s of the <see cref="ListViewItem"/>s
		/// are compared.</remarks>
		private class InstallFileComparer : IComparer
		{
			#region IComparer Members

			/// <summary>
			/// Compares the given <see cref="ListViewItem"/>s representing <see cref="InstallableFile"/>s.
			/// </summary>
			/// <remarks>
			/// The underlying <see cref="InstallableFile"/>s are compared.
			/// </remarks>
			/// <param name="x">An object to compare to another object.</param>
			/// <param name="y">An object to compare to another object.</param>
			/// <returns>A value less than 0 if <paramref name="x"/> is less than <paramref name="y"/>.
			/// 0 if this node is equal to the other.
			/// A value greater than 0 if <paramref name="x"/> is greater than <paramref name="y"/>.</returns>
			public int Compare(object x, object y)
			{
				InstallableFile iflX = (InstallableFile)((ListViewItem)x).Tag;
				InstallableFile iflY = (InstallableFile)((ListViewItem)y).Tag;
				return iflX.Priority.CompareTo(iflY.Priority);
			}

			#endregion
		}

		private FileListEditorVM m_vmlViewModel = null;
		private IList<InstallableFile> m_lstInstallabeFiles = null;

		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public FileListEditorVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				ifeFileEditor.ViewModel = value.InstallableFileEditorVM;

				InstallabeFiles = value.InstallableFiles;

				value.FileAdded += new EventHandler<EventArgs<InstallableFile>>(FileAdded);
				value.FileEdited += new EventHandler<EventArgs<InstallableFile>>(FileEdited);
				value.FileDeleted += new EventHandler<EventArgs<InstallableFile>>(FileDeleted);

				value.EditCommand.Executed += new EventHandler<EventArgs<InstallableFile>>(EditCommand_Executed);
				new ToolStripItemCommandBinding<InstallableFile>(tsbEdit, value.EditCommand, GetSelectedInstallableFile);
				new EventCommandBinding<InstallableFile>(lvwInstallableFiles, "ItemActivate", value.EditCommand, GetSelectedInstallableFile);

				value.AddCommand.Executed += new EventHandler<EventArgs<InstallableFile>>(EditCommand_Executed);
				new ToolStripItemCommandBinding<InstallableFile>(tsbAdd, value.AddCommand, () => { return null; });

				new ToolStripItemCommandBinding<InstallableFile>(tsbDelete, value.DeleteCommand, GetSelectedInstallableFile);
				new KeyDownCommandBinding<InstallableFile>(lvwInstallableFiles, value.DeleteCommand, GetSelectedInstallableFile, Keys.Delete);
			}
		}

		/// <summary>
		/// Gets or sets the list of <see cref="InstallableFile"/>s being edited.
		/// </summary>
		/// <value>The list of <see cref="InstallableFile"/>s being edited.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		protected IList<InstallableFile> InstallabeFiles
		{
			get
			{
				return m_lstInstallabeFiles;
			}
			set
			{
				m_lstInstallabeFiles = value;

				lvwInstallableFiles.Items.Clear();
				foreach (InstallableFile iflFile in value)
					AddInstallableFile(iflFile);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public FileListEditor()
		{
			InitializeComponent();

			lvwInstallableFiles.ListViewItemSorter = new InstallFileComparer();
		}

		/// <summary>
		/// A simple constructor the initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_vmlViewModel">The view model that provides the data and operations for this view.</param>
		public FileListEditor(FileListEditorVM p_vmlViewModel)
			: this()
		{
			ViewModel = p_vmlViewModel;
		}

		#endregion

		/// <summary>
		/// Adds the given <see cref="InstallableFile"/> to the list view.
		/// </summary>
		/// <param name="p_iflFile">The <see cref="InstallableFile"/> to add to the list view.</param>
		protected void AddInstallableFile(InstallableFile p_iflFile)
		{
			ListViewItem lviFile = new ListViewItem(p_iflFile.Source);

			lviFile.SubItems[0].Name = "Source";
			lviFile.SubItems.Add(p_iflFile.Destination);
			lviFile.SubItems[1].Name = "Destination";
			lviFile.SubItems.Add(p_iflFile.Priority.ToString());
			lviFile.SubItems[2].Name = "Priority";
			lviFile.Tag = p_iflFile;
			lvwInstallableFiles.Items.Add(lviFile);
		}

		/// <summary>
		/// Handles the <see cref="FileListEditorVM.FileAdded"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This addes the newly added <see cref="InstallableFile"/> to the list view.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{InstallableFile}"/> describing the event arguments.</param>
		private void FileAdded(object sender, EventArgs<InstallableFile> e)
		{
			AddInstallableFile(e.Argument);
		}

		/// <summary>
		/// Handles the <see cref="FileListEditorVM.FileEdited"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This updates the edited <see cref="InstallableFile"/> in the list view.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{InstallableFile}"/> describing the event arguments.</param>
		private void FileEdited(object sender, EventArgs<InstallableFile> e)
		{
			InstallableFile fliFile = e.Argument;
			ListViewItem lviFile = null;
			for (Int32 i = lvwInstallableFiles.Items.Count - 1; i >= 0; i--)
				if (lvwInstallableFiles.Items[i].Tag == fliFile)
				{
					lviFile = lvwInstallableFiles.Items[i];
					break;
				}

			lviFile.SubItems["Source"].Text = fliFile.Source;
			lviFile.SubItems["Destination"].Text = fliFile.Destination;
			lviFile.SubItems["Priority"].Text = fliFile.Priority.ToString();
		}

		/// <summary>
		/// Handles the <see cref="FileListEditorVM.FileDeleted"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This removed the delete <see cref="InstallableFile"/> from the list view.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{InstallableFile}"/> describing the event arguments.</param>
		private void FileDeleted(object sender, EventArgs<InstallableFile> e)
		{
			InstallableFile fliFile = e.Argument;
			for (Int32 i = lvwInstallableFiles.Items.Count - 1; i >= 0; i--)
				if (lvwInstallableFiles.Items[i].Tag == fliFile)
				{
					lvwInstallableFiles.Items.RemoveAt(i);
					if (i < lvwInstallableFiles.Items.Count)
						lvwInstallableFiles.Items[i].Selected = true;
					break;
				}
		}

		/// <summary>
		/// This gets the <see cref="InstallableFile"/> currently selected in the view.
		/// </summary>
		/// <returns>The <see cref="InstallableFile"/> currently selected in the view.</returns>
		private InstallableFile GetSelectedInstallableFile()
		{
			if (lvwInstallableFiles.SelectedItems.Count == 0)
				return null;
			return (InstallableFile)lvwInstallableFiles.SelectedItems[0].Tag;
		}

		/// <summary>
		/// Hanldes the <see cref="Command{T}.Executed"/> event of the edit command.
		/// </summary>
		/// <remarks>This transfer focus to the <see cref="InstallableFile"/> editor.</remarks>
		/// <param name="p_objCommand">The command that executed.</param>
		/// <param name="p_eeaArguments">The command arguments.</param>
		private void EditCommand_Executed(object p_objCommand, EventArgs<InstallableFile> p_eeaArguments)
		{
			ifeFileEditor.Focus();
		}

		/// <summary>
		/// Handles the <see cref="Control.Resize"/> event of the list view.
		/// </summary>
		/// <remarks>This resizes the columns to fill the list view.</remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void lvwInstallableFiles_Resize(object sender, EventArgs e)
		{
			if (lvwInstallableFiles.Columns.Count == 0)
				return;
			Int32 intWidth = (lvwInstallableFiles.ClientSize.Width - lvwInstallableFiles.Columns[lvwInstallableFiles.Columns.Count - 1].Width) / (lvwInstallableFiles.Columns.Count - 1);
			for (Int32 i = 0; i < lvwInstallableFiles.Columns.Count - 1; i++)
				lvwInstallableFiles.Columns[i].Width = intWidth;
		}

		/// <summary>
		/// Handles the <see cref="ListView.SelectedIndexChanged"/> event of the list view.
		/// </summary>
		/// <remarks>This updates the enable states of the commands.</remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void lvwInstallableFiles_SelectedIndexChanged(object sender, EventArgs e)
		{
			ViewModel.EditCommand.CanExecute = (lvwInstallableFiles.SelectedItems.Count > 0);
			ViewModel.DeleteCommand.CanExecute = (lvwInstallableFiles.SelectedItems.Count > 0);
		}

		/// <summary>
		/// Gets the view model of the editor.
		/// </summary>
		/// <returns>The view model of the editor.</returns>
		public override IViewModel GetViewModel()
		{
			return (IViewModel)ViewModel;
		}
	}
}
