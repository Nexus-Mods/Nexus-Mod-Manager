using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Nexus.UI.Controls;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.Mods;
using Nexus.Client.Util;

namespace Nexus.Client.ModAuthoring.UI.Controls
{
	/// <summary>
	/// This class encapsulates the data and the operations presented by UI
	/// elements that display a mod script editor.
	/// </summary>
	/// <remarks>
	/// This is an abstract base class that be be subclassed to provide view models
	/// for each type of mod script, such as <see cref="IScriptedMod.InstallScript"/>.
	/// </remarks>
	public abstract class ModScriptEditorVM : ObservableObject
	{
		private IScriptedMod m_modEditedMod = null;
		private Dictionary<IScriptType, IScriptEditor> m_dicScriptEditors = new Dictionary<IScriptType, IScriptEditor>();
		private IList<VirtualFileSystemItem> m_lstModFiles = null;
		private IScriptEditor m_sedCurrentEditor = null;

		#region Properties

		/// <summary>
		/// Gets or sets whether the script being edited is to be used.
		/// </summary>
		/// <remarks>
		/// If a script is not used, the edited script is set to <c>null</c>.
		/// </remarks>
		/// <value>Whether the script being edited is to be used.</value>
		public bool UseScript
		{
			get
			{
				return (EditedScript != null);
			}
			set
			{
				EditedScript = value ? m_sedCurrentEditor.Script : null;
			}
		}

		/// <summary>
		/// Gets or sets the <see cref="IScriptedMod"/> whose script is being edited.
		/// </summary>
		/// <value>The <see cref="IScriptedMod"/> whose script is being edited.</value>
		public IScriptedMod EditedMod
		{
			get
			{
				return m_modEditedMod;
			}
			set
			{
				if (m_modEditedMod != null)
					m_modEditedMod.PropertyChanged -= EditedMod_PropertyChanged;
				m_modEditedMod = value;
				if (m_modEditedMod != null)
					m_modEditedMod.PropertyChanged += new PropertyChangedEventHandler(EditedMod_PropertyChanged);
				foreach (IScriptEditor sedEditor in m_dicScriptEditors.Values)
					sedEditor.Script = null;
				if (EditedScript != null)
					CurrentEditor = m_dicScriptEditors[EditedScript.Type];
				else
					CurrentEditor = m_dicScriptEditors.Values.First();
				RefreshScript();
			}
		}

		/// <summary>
		/// Gets the <see cref="IScriptTypeRegistry"/> of available <see cref="IScriptType"/>s.
		/// </summary>
		/// <value>The <see cref="IScriptTypeRegistry"/> of available <see cref="IScriptType"/>s.</value>
		protected IScriptTypeRegistry IScriptTypeRegistry { get; private set; }

		/// <summary>
		/// Gets or sets the list of files that are in the script's mod.
		/// </summary>
		/// <value>The list of files that are in the script's mod.</value>
		public IList<VirtualFileSystemItem> ModFiles
		{
			get
			{
				return m_lstModFiles;
			}
			set
			{
				if (SetPropertyIfChanged(ref m_lstModFiles, value, () => ModFiles))
					foreach (IScriptEditor sedEditor in m_dicScriptEditors.Values)
						sedEditor.ModFiles = value;
			}
		}

		/// <summary>
		/// Gets the list of available script editors.
		/// </summary>
		/// <value>The list of available script editors.</value>
		public IEnumerable<IScriptEditor> Editors
		{
			get
			{
				return m_dicScriptEditors.Values;
			}
		}

		/// <summary>
		/// Gets or sets the currently selected script editor.
		/// </summary>
		/// <value>The currently selected script editor.</value>
		public IScriptEditor CurrentEditor
		{
			get
			{
				return m_sedCurrentEditor;
			}
			set
			{
				SetPropertyIfChanged(ref m_sedCurrentEditor, value, () => CurrentEditor);
			}
		}

		/// <summary>
		/// Gets or sets the script being edited.
		/// </summary>
		/// <remarks>
		/// This is abstract so that subclass can determine which of the mod's scripts
		/// is being edited. For example, the install script, or the uninstall script,
		/// or the reactivation script.
		/// </remarks>
		/// <value>The script being edited.</value>
		protected abstract IScript EditedScript { get; set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A siple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_stgScriptTypeRegistry">The <see cref="IScriptTypeRegistry"/> of available <see cref="IScriptType"/>s.</param>
		public ModScriptEditorVM(IScriptTypeRegistry p_stgScriptTypeRegistry)
		{
			IScriptTypeRegistry = p_stgScriptTypeRegistry;

			foreach (IScriptType stpScriptType in IScriptTypeRegistry.Types)
				m_dicScriptEditors[stpScriptType] = stpScriptType.CreateEditor(null);
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="INotifyPropertyChanged.PropertyChanged"/> event of the mod being edited.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="PropertyChangedEventArgs"/> describing the event arguments.</param>
		private void EditedMod_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			RefreshScript();
		}

		/// <summary>
		/// Loads the script from the mod being edited.
		/// </summary>
		protected void RefreshScript()
		{
			m_sedCurrentEditor.Script = EditedScript;
			OnPropertyChanged(() => UseScript);
		}
	}
}
