using System;
using System.Collections.Generic;
using Nexus.Client.Commands.Generic;
using Nexus.UI.Controls;
using Nexus.Client.Util;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	/// <summary>
	/// This class encapsulates the data and the operations presented by UI
	/// elements that enable editing of a list of <see cref="InstallableFile"/>s.
	/// </summary>
	public class FileListEditorVM : IViewModel
	{
		#region Events

		/// <summary>
		/// Raised when an <see cref="InstallableFile"/> has been added.
		/// </summary>
		public event EventHandler<EventArgs<InstallableFile>> FileAdded = delegate { };

		/// <summary>
		/// Raised when an <see cref="InstallableFile"/> has been edited.
		/// </summary>
		public event EventHandler<EventArgs<InstallableFile>> FileEdited = delegate { };

		/// <summary>
		/// Raised when an <see cref="InstallableFile"/> has been deleted.
		/// </summary>
		public event EventHandler<EventArgs<InstallableFile>> FileDeleted = delegate { };

		#endregion

		private InstallableFileEditorVM m_femInstallableFileEditorVM = null;

		#region Properties

		#region Commands

		/// <summary>
		/// Gets the command to edit an <see cref="InstallableFile"/>.
		/// </summary>
		/// <value>The command to edit an <see cref="InstallableFile"/>.</value>
		public Command<InstallableFile> EditCommand { get; set; }

		/// <summary>
		/// Gets the command to add an <see cref="InstallableFile"/>.
		/// </summary>
		/// <value>The command to add an <see cref="InstallableFile"/>.</value>
		public Command<InstallableFile> AddCommand { get; set; }
		
		/// <summary>
		/// Gets the command to delete an <see cref="InstallableFile"/>.
		/// </summary>
		/// <value>The command to delete an <see cref="InstallableFile"/>.</value>
		public Command<InstallableFile> DeleteCommand { get; set; }

		#endregion

		/// <summary>
		/// Gets the <see cref="InstallableFileEditorVM"/> that encapsulates the data
		/// and operations for editing an <see cref="InstallableFile"/>.
		/// </summary>
		/// <value>The <see cref="InstallableFileEditorVM"/> that encapsulates the data
		/// and operations for editing an <see cref="InstallableFile"/>.</value>
		public InstallableFileEditorVM InstallableFileEditorVM
		{
			get
			{
				return m_femInstallableFileEditorVM;
			}
			private set
			{
				m_femInstallableFileEditorVM = value;
				value.InstallableFileSaved += new EventHandler<EventArgs<InstallableFile>>(InstallableFileSaved);
			}
		}

		/// <summary>
		/// Gets the list of <see cref="InstallableFile"/>s that is being edited.
		/// </summary>
		/// <value>The list of <see cref="InstallableFile"/>s that is being edited.</value>
		public IList<InstallableFile> InstallableFiles { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the view model with its dependencies.
		/// </summary>
		/// <param name="p_femInstallableFileEditorVM">The <see cref="InstallableFileEditorVM"/> that encapsulates the data
		/// and operations for editing an <see cref="InstallableFile"/>.</param>
		/// <param name="p_lstFiles">The list of <see cref="InstallableFile"/>s that is being edited.</param>
		public FileListEditorVM(InstallableFileEditorVM p_femInstallableFileEditorVM, IList<InstallableFile> p_lstFiles)
		{
			InstallableFileEditorVM=p_femInstallableFileEditorVM;
			InstallableFiles = p_lstFiles;

			EditCommand = new Command<InstallableFile>("Edit", "Edit the selected installable file.", EditInstallableFile, false);
			AddCommand = new Command<InstallableFile>("Add", "Add an installable file.", AddInstallableFile);
			DeleteCommand = new Command<InstallableFile>("Delete", "Delete the selected installable file.", DeleteInstallableFile, false);
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="InstallableFileEditorVM.InstallableFileSaved"/> event of the
		/// <see cref="InstallableFile"/> editor view model.
		/// </summary>
		/// <remarks>This raises the <see cref="FileAdded"/> or <see cref="FileEdited"/> event,
		/// as appropriate.</remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{InstallableFile}"/> describing the event arguments.</param>
		private void InstallableFileSaved(object sender, EventArgs<InstallableFile> e)
		{
			if (!InstallableFiles.Contains(e.Argument))
			{
				InstallableFiles.Add(e.Argument);
				FileAdded(this, new EventArgs<InstallableFile>(e.Argument));
			}
			else
				FileEdited(this, new EventArgs<InstallableFile>(e.Argument));
		}

		/// <summary>
		/// Passes the given <see cref="InstallableFile"/> to the editor view model
		/// to be edited.
		/// </summary>
		/// <param name="p_iflArgument">The <see cref="InstallableFile"/> to be edited.</param>
		private void EditInstallableFile(InstallableFile p_iflArgument)
		{
			InstallableFileEditorVM.InstallableFile = p_iflArgument;
		}

		/// <summary>
		/// Passes the a new <see cref="InstallableFile"/> to the editor view model
		/// to be edited.
		/// </summary>
		/// <param name="p_iflArgument">Ignored.</param>
		private void AddInstallableFile(InstallableFile p_iflArgument)
		{
			InstallableFileEditorVM.InstallableFile = null;
		}

		/// <summary>
		/// Deletes the given <see cref="InstallableFile"/> from the list.
		/// </summary>
		/// <param name="p_iflArgument">The <see cref="InstallableFile"/> to delete.</param>
		private void DeleteInstallableFile(InstallableFile p_iflArgument)
		{
			InstallableFiles.Remove(p_iflArgument);
			FileDeleted(this, new EventArgs<InstallableFile>(p_iflArgument));
		}

		#region IViewModel Members

		/// <summary>
		/// Validates the current state edited list.
		/// </summary>
		/// <returns>Always <c>true</c>.</returns>
		public bool Validate()
		{
			return true;
		}

		#endregion
	}
}
