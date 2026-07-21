using System;
using System.ComponentModel;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Commands;
using Nexus.Client.Commands.Generic;
using Nexus.UI.Controls;
using Nexus.Client.ModAuthoring.UI.Controls;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.Mods;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModAuthoring.UI
{
	/// <summary>
	/// This class encapsulates the data and the operations presented by UI
	/// elements that display a mod packaging <see cref="Project"/> editor.
	/// </summary>
	public class ModPackagingFormVM : ObservableObject
	{
		#region Events

		/// <summary>
		/// Raised when the packaging of a mod has begun.
		/// </summary>
		/// <remarks>
		/// Listeners can use this event to monitor the packaging process.
		/// </remarks>
		public event EventHandler<EventArgs<IBackgroundTask>> ModPackingStarted = delegate { };

		/// <summary>
		/// Raised when the <see cref="Project"/> being edited has been validated.
		/// </summary>
		public event EventHandler ProjectValidated = delegate { };

		#endregion

		#region Delegate Methods

		/// <summary>
		/// Gets whether the current <see cref="Project"/> should be saved.
		/// </summary>
		/// <remarks>
		/// If the delegate returns <c>null</c>, then the user wishes to keep the
		/// current project as the project being edited.
		/// </remarks>
		public Func<bool?> ConfirmSaveCurrentProject = delegate { return true; };

		/// <summary>
		/// Gets the path of the <see cref="Project"/> file to open.
		/// </summary>
		public Func<string> GetOpenPath = delegate { return null; };

		/// <summary>
		/// Gets the file path to which to save the <see cref="Project"/>.
		/// </summary>
		public Func<string> GetProjectSavePath = delegate { return null; };

		/// <summary>
		/// Gets the file path to which to save the new mod.
		/// </summary>
		public Func<string> GetNewModSavePath = delegate { return null; };

		/// <summary>
		/// Determines if the validation warnings should be ignored.
		/// </summary>
		public Func<bool> GetIgnoreWarnings = delegate { return true; };

		#endregion

		private Project m_prjProject = null;

		#region Properties

		#region Commands

		/// <summary>
		/// Gets the command to save the <see cref="ModProject"/>.
		/// </summary>
		/// <remarks>
		/// The commands takes an argument describing the file path to which to save the <see cref="ModProject"/>.
		/// </remarks>
		/// <value>The command to save the <see cref="ModProject"/>.</value>
		public Command<string> SaveCommand { get; private set; }

		/// <summary>
		/// Gets the command to open a <see cref="Project"/>.
		/// </summary>
		/// <remarks>
		/// The commands takes an argument describing the file path from which to load the <see cref="Project"/>.
		/// </remarks>
		/// <value>The command to open a <see cref="Project"/>.</value>
		public Command OpenCommand { get; private set; }

		/// <summary>
		/// Gets the command to create a new <see cref="Project"/>.
		/// </summary>
		/// <value>The command to create a new <see cref="Project"/>.</value>
		public Command NewCommand { get; private set; }

		/// <summary>
		/// Gets the command to package a <see cref="Project"/> into a mod file.
		/// </summary>
		/// <value>The command to package a <see cref="Project"/> into a mod file.</value>
		public Command<string> BuildCommand { get; private set; }

		#endregion

		/// <summary>
		/// Gets the <see cref="InstallScriptEditorVM"/> that encapsulates the data
		/// and operations for diaplying the install script editor.
		/// </summary>
		/// <value>The <see cref="InstallScriptEditorVM"/> that encapsulates the data
		/// and operations for diaplying the install script editor.</value>
		public InstallScriptEditorVM ScriptEditorVM { get; private set; }

		/// <summary>
		/// Gets the <see cref="ModInfoEditorVM"/> that encapsulates the data
		/// and operations for diaplying the <see cref="IModInfo"/> editor.
		/// </summary>
		/// <value>The <see cref="ModInfoEditorVM"/> that encapsulates the data
		/// and operations for diaplying the <see cref="IModInfo"/> editor.</value>
		public ModInfoEditorVM InfoEditorVM { get; private set; }

		/// <summary>
		/// Gets or sets the mod packaging <see cref="Project"/> being edited.
		/// </summary>
		/// <value>The mod packaging <see cref="Project"/> being edited.</value>
		public Project ModProject
		{
			get
			{
				return m_prjProject;
			}
			set
			{
				if (m_prjProject != null)
					m_prjProject.PropertyChanged -= Project_PropertyChanged;
				SetPropertyIfChanged(ref m_prjProject, value, () => ModProject);
				if (m_prjProject != null)
				{
					m_prjProject.PropertyChanged += new PropertyChangedEventHandler(Project_PropertyChanged);
					ScriptEditorVM.EditedMod = m_prjProject;
					ScriptEditorVM.ModFiles = m_prjProject.ModFiles;
					InfoEditorVM.ModInfo = m_prjProject;
				}
			}
		}

		/// <summary>
		/// Gets the <see cref="IScriptTypeRegistry"/> of available <see cref="IScriptType"/>s.
		/// </summary>
		/// <value>The <see cref="IScriptTypeRegistry"/> of available <see cref="IScriptType"/>s.</value>
		protected IScriptTypeRegistry IScriptTypeRegistry { get; set; }

		/// <summary>
		/// Gets the <see cref="ModPackager"/> to use to build mod files
		/// from <see cref="Project"/>s.
		/// </summary>
		/// <value>The <see cref="ModPackager"/> to use to build mod files
		/// from <see cref="Project"/>s.</value>
		protected ModPackager ModBuilder { get; set; }

		/// <summary>
		/// Gets the current validation errors for the current view model.
		/// </summary>
		/// <value>The current validation errors for the current view model.</value>
		public ErrorContainer Errors { get; private set; }

		/// <summary>
		/// Gets the current validation warnings for the current view model.
		/// </summary>
		/// <value>The current validation warnings for the current view model.</value>
		public ErrorContainer Warnings { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_vmlScriptEditorVM">The <see cref="InstallScriptEditorVM"/> that encapsulates the data
		/// and operations for diaplying the <see cref="IScript"/> editor.</param>
		/// <param name="p_vmlInfoEditorVM">The <see cref="ModInfoEditorVM"/> that encapsulates the data
		/// and operations for diaplying the <see cref="IModInfo"/> editor.</param>
		/// <param name="p_prjModProject">The <see cref="Project"/> to edit.</param>
		/// <param name="p_srgScriptTypeRegistry">The <see cref="IScriptTypeRegistry"/> of available <see cref="IScriptType"/>s.</param>
		/// <param name="p_mpkModBuilder">he <see cref="ModPackager"/> to use to build mod files
		/// from <see cref="Project"/>s.</param>
		public ModPackagingFormVM(InstallScriptEditorVM p_vmlScriptEditorVM, ModInfoEditorVM p_vmlInfoEditorVM, Project p_prjModProject, IScriptTypeRegistry p_srgScriptTypeRegistry, ModPackager p_mpkModBuilder)
		{
			Errors = new ErrorContainer();
			Warnings = new ErrorContainer();

			ScriptEditorVM = p_vmlScriptEditorVM;
			InfoEditorVM = p_vmlInfoEditorVM;
			IScriptTypeRegistry = p_srgScriptTypeRegistry;
			ModBuilder = p_mpkModBuilder;
			
			ModProject = p_prjModProject ?? new Project(p_srgScriptTypeRegistry);

			SaveCommand = new Command<string>("Save Project", "Save the project.", SaveProject, ModProject.IsDirty);
			OpenCommand = new Command("Open Project", "Open a project.", OpenProject);
			NewCommand = new Command("New Project", "Create a new project.", NewProject);
			BuildCommand = new Command<string>("Build Mod", "Builds the mod file.", BuildMod);
		}


		#endregion

		#region Commands

		/// <summary>
		/// Determine if we should 
		/// </summary>
		/// <returns></returns>
		protected bool GetAbandonCurrentProject()
		{
			if (!ModProject.IsDirty)
				return true;
			bool? booSave = ConfirmSaveCurrentProject();
			if (!booSave.HasValue)
				return false;
			if (booSave.Value)
			{
				string strPath = GetProjectSavePath();
				if (String.IsNullOrEmpty(strPath))
					return false;
				SaveCommand.Execute(strPath);
			}
			return true;
		}

		/// <summary>
		/// Saves the mod packaging <see cref="Project"/> to the given path.
		/// </summary>
		/// <param name="p_strPath">The file path to which to save the <see cref="Project"/>.</param>
		protected void SaveProject(string p_strPath)
		{
			if (String.IsNullOrEmpty(p_strPath))
				p_strPath = ModProject.FilePath;
			if (String.IsNullOrEmpty(p_strPath))
				p_strPath = GetProjectSavePath();
			if (!String.IsNullOrEmpty(p_strPath))
				ModProject.Save(p_strPath);
		}

		/// <summary>
		/// Opens the mod packaging <see cref="Project"/> from the given path.
		/// </summary>
		protected void OpenProject()
		{
			if (!GetAbandonCurrentProject())
				return;
			string strPath = GetOpenPath();
			if (!String.IsNullOrEmpty(strPath))
				ModProject = new Project(strPath, IScriptTypeRegistry);
		}

		/// <summary>
		/// Creates a new packaging <see cref="Project"/>.
		/// </summary>
		protected void NewProject()
		{
			if (GetAbandonCurrentProject())
				ModProject = new Project(IScriptTypeRegistry);
		}

		/// <summary>
		/// Build a mod file at the given path from the current <see cref="Project"/>.
		/// </summary>
		/// <param name="p_strPath">The file path at which to build the mod.</param>
		protected void BuildMod(string p_strPath)
		{
			if (!Validate())
				return;
			if ((Warnings.Count > 0) && !GetIgnoreWarnings())
				return;
			//save project
			string strProjectPath = ModProject.FilePath;
			if (String.IsNullOrEmpty(strProjectPath))
				strProjectPath = GetProjectSavePath();
			if (String.IsNullOrEmpty(strProjectPath))
				return;
			ModProject.Save(strProjectPath);

			if (String.IsNullOrEmpty(p_strPath))
				p_strPath = GetNewModSavePath();
			if (!String.IsNullOrEmpty(p_strPath))
			{
				ModBuilder.PackageMod(p_strPath, ModProject);
				ModPackingStarted(this, new EventArgs<IBackgroundTask>(ModBuilder));
			}
		}

		#endregion

		#region Validation

		/// <summary>
		/// Validates the mod files.
		/// </summary>
		/// <remarks>
		/// This raises a warning if no files have been selected.
		/// </remarks>
		/// <returns>Always <c>true</c>.</returns>
		protected bool ValidateFiles()
		{
			Warnings.Clear<Project>(x => x.ModFiles);
			if (ModProject.ModFiles.IsNullOrEmpty())
				Warnings.SetError<Project>(x => x.ModFiles, "No files have been selected.");
			return true;
		}

		/// <summary>
		/// This validates the install script.
		/// </summary>
		/// <returns><c>true</c> if the script is valid;
		/// <c>false</c> otherwsie.</returns>
		protected bool ValidateScript()
		{
			Errors.Clear<Project>(x => x.InstallScript);
			if (ModProject.InstallScript == null)
				return true;

			if (!ModProject.InstallScript.Type.ValidateScript(ModProject.InstallScript))
			{
				Errors.SetError<Project>(x => x.InstallScript, "Invalid script.");
				return false;
			}
			return true;
		}

		/// <summary>
		/// This validates the mod info.
		/// </summary>
		/// <returns><c>true</c> if the info is valid;
		/// <c>false</c> otherwsie.</returns>
		protected bool ValidateModInfo()
		{
			Errors.Clear<Project>(x => x.ModName);
			if (!InfoEditorVM.Validate())
			{
				Errors.SetError<Project>(x => x.ModName, "Invalid information.");
				return false;
			}
			return true;
		}

		/// <summary>
		/// Validates the readme.
		/// </summary>
		/// <remarks>
		/// This raises a warning if there is no readme.
		/// </remarks>
		/// <returns>Always <c>true</c>.</returns>
		protected bool ValidateReadme()
		{
			Warnings.Clear<Project>(x => x.ModReadme);
			if ((ModProject.ModReadme == null) || String.IsNullOrEmpty(ModProject.ModReadme.Text))
				Warnings.SetError<Project>(x => x.ModReadme, "Missing readme.");
			return true;
		}

		/// <summary>
		/// Validates the project.
		/// </summary>
		/// <returns><c>true</c> if the project is valid;
		/// <c>false</c> otherwsie.</returns>
		public bool Validate()
		{
			Warnings.Clear();
			Errors.Clear();
			bool booIsValid = ValidateFiles();
			booIsValid &= ValidateScript();
			booIsValid &= ValidateModInfo();
			booIsValid &= ValidateReadme();

			ProjectValidated(this, new EventArgs());
			return booIsValid;
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="INotifyPropertyChanged.PropertyChanged"/> event of the <see cref="Project"/>
		/// being edited.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="PropertyChangedEventArgs"/> describing the event arguments.</param>
		private void Project_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			SaveCommand.CanExecute = ModProject.IsDirty;
			string[] strModInfoProperties = { ObjectHelper.GetPropertyName<IModInfo>(x=>x.Author),
												ObjectHelper.GetPropertyName<IModInfo>(x=>x.Description),												
												ObjectHelper.GetPropertyName<IModInfo>(x=>x.HumanReadableVersion),												
												ObjectHelper.GetPropertyName<IModInfo>(x=>x.MachineVersion),												
												ObjectHelper.GetPropertyName<IModInfo>(x=>x.ModName),												
												ObjectHelper.GetPropertyName<IModInfo>(x=>x.Website) };
			if (strModInfoProperties.Contains(x => x.Equals(e.PropertyName)))
				InfoEditorVM.ModInfo = m_prjProject;
		}
	}
}
