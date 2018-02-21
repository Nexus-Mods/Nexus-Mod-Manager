using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Forms;
using Nexus.Client.Commands;
using Nexus.Client.Commands.Generic;
using Nexus.Client.Util;
using Nexus.UI.Controls;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls
{
	/// <summary>
	/// A UI element that allows editing of an <see cref="XmlScript"/>.
	/// </summary>
	public partial class XmlScriptTreeEditor : UserControl, IScriptEditor
	{
		private XmlScriptTreeEditorVM m_vmlViewModel = null;
		private List<ICommand> m_lstDeleteCommands = new List<ICommand>();
		private List<ICommandBinding> m_lstCommandBindings = new List<ICommandBinding>();

		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		protected XmlScriptTreeEditorVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				cbxScriptVersion.DataSource = value.ScriptVersions;
				value.PropertyChanged += new PropertyChangedEventHandler(ViewModel_PropertyChanged);
				stvScript.ViewModel = ViewModel;

				RefreshScript();
			}
		}

		/// <summary>
		/// Gets the name of the script type being edited.
		/// </summary>
		/// <value>The name of the script type being edited.</value>
		public string ScriptTypeName { get; private set; }

		/// <summary>
		/// Sets the files in the mod whose script is being edited.
		/// </summary>
		/// <value>The files in the mod whose script is being edited.</value>
		public IList<VirtualFileSystemItem> ModFiles
		{
			set
			{
				ViewModel.ModFiles = value;
			}
		}

		/// <summary>
		/// Gets or sets the script being edited.
		/// </summary>
		/// <value>The script being edited.</value>
		public IScript Script
		{
			get
			{
				return ViewModel.Script;
			}
			set
			{
				if ((value != null) && !(value is XmlScript))
					throw new ArgumentException("The given script is not an XML Script.");
				ViewModel.Script = (XmlScript)value;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default construtor.
		/// </summary>
		/// <param name="p_vmlViewModel">The view model that provides the data and operations for this view.</param>
		public XmlScriptTreeEditor(XmlScriptTreeEditorVM p_vmlViewModel)
		{
			InitializeComponent();
			ViewModel = p_vmlViewModel;
			ScriptTypeName = p_vmlViewModel.ScriptType.TypeName;
		}

		#endregion

		#region Deletion

		/// <summary>
		/// Handles the <see cref="Command{InstallStep}.BeforeExecute"/> event of the delete Install Step command.
		/// </summary>
		/// <remarks>
		/// This asks the user to confirm the deletion.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void DeleteInstallStepCommand_BeforeExecute(object sender, CancelEventArgs<InstallStep> e)
		{
			string strMessage = "Are you sure you want to delete Install Step \"{0}?\"";
			e.Cancel = (MessageBox.Show(this, String.Format(strMessage, e.Argument.Name), "Confirm", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.Cancel);
		}

		/// <summary>
		/// Handles the <see cref="Command{OptionGroup}.BeforeExecute"/> event of the delete Option Group command.
		/// </summary>
		/// <remarks>
		/// This asks the user to confirm the deletion.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void DeleteOptionGroupCommand_BeforeExecute(object sender, CancelEventArgs<OptionGroup> e)
		{
			string strMessage = "Are you sure you want to delete Option Group \"{0}?\"";
			e.Cancel = (MessageBox.Show(this, String.Format(strMessage, e.Argument.Name), "Confirm", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.Cancel);
		}

		/// <summary>
		/// Handles the <see cref="Command{Option}.BeforeExecute"/> event of the delete Option command.
		/// </summary>
		/// <remarks>
		/// This asks the user to confirm the deletion.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void DeleteOptionCommand_BeforeExecute(object sender, CancelEventArgs<Option> e)
		{
			string strMessage = "Are you sure you want to delete Option \"{0}?\"";
			e.Cancel = (MessageBox.Show(this, String.Format(strMessage, e.Argument.Name), "Confirm", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.Cancel);
		}

		/// <summary>
		/// Handles the <see cref="Command{ConditionallyInstalledFileSet}.BeforeExecute"/> event of the delete
		/// Conditionally Installed File Set command.
		/// </summary>
		/// <remarks>
		/// This asks the user to confirm the deletion.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void DeleteConditionallyInstalledFileSetCommand_BeforeExecute(object sender, CancelEventArgs<ConditionallyInstalledFileSet> e)
		{
			string strMessage = "Are you sure you want to delete the selected Conditionally Installed File Set?";
			e.Cancel = (MessageBox.Show(this, strMessage, "Confirm", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.Cancel);
		}

		/// <summary>
		/// Handles the <see cref="Control.EnabledChanged"/> event of the delete button.
		/// </summary>
		/// <remarks>
		/// This enables the button if any of its bound commands can be executed.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void tsbDelete_EnabledChanged(object sender, EventArgs e)
		{
			bool booEnabled = false;
			foreach (ICommand cmdCommand in m_lstDeleteCommands)
				booEnabled |= cmdCommand.CanExecute;
			tsbDelete.Enabled = booEnabled;
		}

		#endregion

		/// <summary>
		/// Returns the currently selected node's associated script object.
		/// </summary>
		/// <typeparam name="T">The type of object to return.</typeparam>
		/// <returns>The currently selected node's associated script object, or
		/// <c>null</c> if the node's script obejct is not of type <typeparamref name="T"/>.</returns>
		private T GetSelectedScriptObject<T>() where T : class
		{
			if (stvScript.SelectedNode == null)
				return null;
			return ((XmlScriptTreeNode)stvScript.SelectedNode).GetObject() as T;
		}

		/// <summary>
		/// Handles the <see cref="INotifyPropertyChanged.PropertyChanged"/> event of the <see cref="ViewModel"/>.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="PropertyChangedEventArgs"/> describing the event arguments.</param>
		private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName.Equals(ObjectHelper.GetPropertyName(() => ViewModel.Script)))
				RefreshScript();
		}

		/// <summary>
		/// Refresh the available commands.
		/// </summary>
		protected void RefreshScript()
		{
			splitContainer1.Panel2.Controls.Clear();
			m_lstDeleteCommands.Clear();
			foreach (ICommandBinding cbdBinding in m_lstCommandBindings)
				cbdBinding.Unbind();
			m_lstCommandBindings.Clear();
			if (ViewModel.IsAddInstallStepCommandSupported)
			{
				m_lstCommandBindings.Add(new ToolStripItemCommandBinding<XmlScript>(tsbAddInstallStep, ViewModel.AddInstallStepCommand, () => ViewModel.Script));

				m_lstDeleteCommands.Add(ViewModel.DeleteInstallStepCommand);
				ViewModel.DeleteInstallStepCommand.BeforeExecute -= DeleteInstallStepCommand_BeforeExecute;
				ViewModel.DeleteInstallStepCommand.BeforeExecute += new EventHandler<CancelEventArgs<InstallStep>>(DeleteInstallStepCommand_BeforeExecute);
				m_lstCommandBindings.Add(new ToolStripItemCommandBinding<InstallStep>(tsbDelete, ViewModel.DeleteInstallStepCommand, GetSelectedScriptObject<InstallStep>));
				tsbAddInstallStep.Visible = true;
			}
			else
				tsbAddInstallStep.Visible = false;

			if (ViewModel.IsAddOptionGroupCommandSupported)
			{
				m_lstCommandBindings.Add(new ToolStripItemCommandBinding<InstallStep>(tsbAddOptionGroup, ViewModel.AddOptionGroupCommand, GetSelectedScriptObject<InstallStep>));

				m_lstDeleteCommands.Add(ViewModel.DeleteOptionGroupCommand);
				ViewModel.DeleteOptionGroupCommand.BeforeExecute -= DeleteOptionGroupCommand_BeforeExecute;
				ViewModel.DeleteOptionGroupCommand.BeforeExecute += new EventHandler<CancelEventArgs<OptionGroup>>(DeleteOptionGroupCommand_BeforeExecute);
				m_lstCommandBindings.Add(new ToolStripItemCommandBinding<OptionGroup>(tsbDelete, ViewModel.DeleteOptionGroupCommand, GetSelectedScriptObject<OptionGroup>));
				tsbAddOptionGroup.Visible = true;
			}
			else
				tsbAddOptionGroup.Visible = false;

			if (ViewModel.IsAddOptionCommandSupported)
			{
				m_lstCommandBindings.Add(new ToolStripItemCommandBinding<OptionGroup>(tspAddOption, ViewModel.AddOptionCommand, GetSelectedScriptObject<OptionGroup>));

				m_lstDeleteCommands.Add(ViewModel.DeleteOptionCommand);
				ViewModel.DeleteOptionCommand.BeforeExecute -= DeleteOptionCommand_BeforeExecute;
				ViewModel.DeleteOptionCommand.BeforeExecute += new EventHandler<CancelEventArgs<Option>>(DeleteOptionCommand_BeforeExecute);
				m_lstCommandBindings.Add(new ToolStripItemCommandBinding<Option>(tsbDelete, ViewModel.DeleteOptionCommand, GetSelectedScriptObject<Option>));
				tspAddOption.Visible = true;
			}
			else
				tspAddOption.Visible = false;

			if (ViewModel.IsAddConditionallyInstalledFileSetCommandSupported)
			{
				m_lstCommandBindings.Add(new ToolStripItemCommandBinding<IList<ConditionallyInstalledFileSet>>(tsbAddConditionallyInstalledFileSet, ViewModel.AddConditionallyInstalledFileSetCommand, GetSelectedScriptObject<IList<ConditionallyInstalledFileSet>>));

				m_lstDeleteCommands.Add(ViewModel.DeleteConditionallyInstalledFileSetCommand);
				ViewModel.DeleteConditionallyInstalledFileSetCommand.BeforeExecute -= DeleteConditionallyInstalledFileSetCommand_BeforeExecute;
				ViewModel.DeleteConditionallyInstalledFileSetCommand.BeforeExecute += new EventHandler<CancelEventArgs<ConditionallyInstalledFileSet>>(DeleteConditionallyInstalledFileSetCommand_BeforeExecute);
				m_lstCommandBindings.Add(new ToolStripItemCommandBinding<ConditionallyInstalledFileSet>(tsbDelete, ViewModel.DeleteConditionallyInstalledFileSetCommand, GetSelectedScriptObject<ConditionallyInstalledFileSet>));
				tsbAddConditionallyInstalledFileSet.Visible = true;
			}
			else
				tsbAddConditionallyInstalledFileSet.Visible = false;

			tsbDelete.Text = "Delete";
			tsbDelete.ToolTipText = "Deletes the selected item.";
			tsbDelete.Enabled = false;
			tsbDelete.EnabledChanged -= tsbDelete_EnabledChanged;
			tsbDelete.EnabledChanged += new EventHandler(tsbDelete_EnabledChanged);

			cbxScriptVersion.DataBindings.Clear();
			if (ViewModel.Script != null)
				BindingHelper.CreateFullBinding(cbxScriptVersion, () => cbxScriptVersion.SelectedItem, ViewModel.Script, () => ViewModel.Script.Version).DataSourceUpdateMode = DataSourceUpdateMode.OnPropertyChanged;
		}

		/// <summary>
		/// Handles the <see cref="TreeView.AfterSelect"/> event of the <see cref="XmlScript"/>
		/// tree view.
		/// </summary>
		/// <remarks>
		/// This retrieves the appropriate editor for the selected node, and displays in the editing
		/// panel.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="TreeViewEventArgs"/> describing the event arguments.</param>
		private void stvScript_AfterSelect(object sender, TreeViewEventArgs e)
		{
			object objObject = ((XmlScriptTreeNode)e.Node).GetObject();
			ViewModel.AddInstallStepCommand.CanExecute = objObject is XmlScript;
			ViewModel.AddOptionGroupCommand.CanExecute = objObject is InstallStep;
			ViewModel.AddOptionCommand.CanExecute = objObject is OptionGroup;
			ViewModel.AddConditionallyInstalledFileSetCommand.CanExecute = objObject is IList<ConditionallyInstalledFileSet>;

			ViewModel.DeleteInstallStepCommand.CanExecute = (objObject is InstallStep) && ViewModel.CanDelete((InstallStep)objObject);
			ViewModel.DeleteOptionGroupCommand.CanExecute = (objObject is OptionGroup);
			ViewModel.DeleteOptionCommand.CanExecute = (objObject is Option);
			ViewModel.DeleteConditionallyInstalledFileSetCommand.CanExecute = (objObject is ConditionallyInstalledFileSet);

			splitContainer1.Panel2.Controls.Clear();
			NodeEditor nedEditor = ((XmlScriptTreeNode)e.Node).CreateEditor(ViewModel.ModFiles);
			if (nedEditor != null)
			{
				nedEditor.Dock = DockStyle.Fill;
				splitContainer1.Panel2.Controls.Add(nedEditor);
			}
		}

		/// <summary>
		/// Handles the <see cref="TreeView.BeforeSelect"/> event of the <see cref="XmlScript"/>
		/// tree view.
		/// </summary>
		/// <remarks>
		/// This prevents node switching if the current editor is not in a valid state.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="TreeViewCancelEventArgs"/> describing the event arguments.</param>
		private void stvScript_BeforeSelect(object sender, TreeViewCancelEventArgs e)
		{
			foreach (Control ctlEditor in splitContainer1.Panel2.Controls)
				if (ctlEditor is NodeEditor)
					e.Cancel = !((NodeEditor)ctlEditor).GetViewModel().Validate();
		}

		/// <summary>
		/// Handles the <see cref="ComboBox.SelectedIndexChanged"/> event.
		/// </summary>
		/// <remarks>
		/// This is reqruied to write the <see cref="ComboBox.SelectedItem"/> to the data binding,
		/// as <see cref="DataSourceUpdateMode.OnPropertyChanged"/> doesn't seem to work with this
		/// property: no idea why.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void cbxScriptVersion_SelectedIndexChanged(object sender, EventArgs e)
		{
			Binding bndBinding = ((ComboBox)sender).DataBindings["SelectedItem"];
			if (bndBinding != null)
				bndBinding.WriteValue();
		}
	}
}
