using System.ComponentModel;
using Nexus.UI.Controls;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	/// <summary>
	/// Edits a <see cref="ConditionallyInstalledFileSet"/>.
	/// </summary>
	public partial class ConditionallyInstalledFileSetEditor : NodeEditor
	{
		private ConditionallyInstalledFileSetEditorVM m_vmlViewModel = null;
		
		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public ConditionallyInstalledFileSetEditorVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				cedCondition.ViewModel = value.ConditionEditorVM;
				fleFiles.ViewModel = value.FileListEditorVM;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the view with its dependencies.
		/// </summary>
		/// <param name="p_vmlViewModel">The view model that provides the data and operations for this view.</param>
		public ConditionallyInstalledFileSetEditor(ConditionallyInstalledFileSetEditorVM p_vmlViewModel)
		{
			InitializeComponent();
			ViewModel = p_vmlViewModel;
		}

		#endregion

		/// <summary>
		/// Gets the view model of the editor.
		/// </summary>
		/// <returns>The view model of the editor.</returns>
		public override IViewModel GetViewModel()
		{
			return ViewModel;
		}
	}
}
