using System;
using System.ComponentModel;
using System.Windows.Forms;
using Nexus.Client.Mods;
using Nexus.Client.Util;
using System.Drawing;
using System.IO;
using Nexus.UI.Controls;


namespace Nexus.Client.ModAuthoring.UI.Controls
{
	/// <summary>
	/// A view that allows editing of an <see cref="IModInfo"/>.
	/// </summary>
	public partial class ModInfoEditor : UserControl
	{
		private ModInfoEditorVM m_vmlViewModel = null;

		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public ModInfoEditorVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				value.PropertyChanged += new PropertyChangedEventHandler(ViewModel_PropertyChanged);
				BindModInfo(value.EditedModInfoVM);
			}
		}

		/// <summary>
		/// Whether or not changes made to the info being edited are commited
		/// automatically.
		/// </summary>
		/// <remarks>
		/// If the changes are to be committed automatically, they are committed
		/// after a control has been validated. For example, if the name is changed,
		/// the change will be committed after focus has left the name textbox.
		/// </remarks>
		/// <value>Whether or not changes made to the info being edited are commited
		/// automatically.</value>
		[Category("Behavior")]
		[DefaultValue(true)]
		public bool AutoCommitChanges { get; set; }

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public ModInfoEditor()
		{
			AutoCommitChanges = true;
			InitializeComponent();
		}

		#endregion

		#region Control Metrics Serialization

		/// <summary>
		/// Raises the <see cref="UserControl.Load"/> event of the control.
		/// </summary>
		/// <remarks>
		/// This loads any saved control metrics.
		/// </remarks>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);

			if (!DesignMode)
			{
				ViewModel.Settings.SplitterSizes.LoadSplitterSizes("modInfoEditor", splitContainer1);

				FindForm().FormClosing += new FormClosingEventHandler(ModTaggerForm_FormClosing);
			}
		}

		/// <summary>
		/// Handles the <see cref="Form.Closing"/> event of the parent form.
		/// </summary>
		/// <remarks>
		/// This save the control's metrics.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="FormClosingEventArgs"/> describing the event arguments.</param>
		private void ModTaggerForm_FormClosing(object sender, FormClosingEventArgs e)
		{
			ViewModel.Settings.SplitterSizes.SaveSplitterSizes("modInfoEditor", splitContainer1);
			ViewModel.Settings.Save();
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="INotifyPropertyChanged.PropertyChanged"/> event of the view model.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="PropertyChangedEventArgs"/> describing the event arguments.</param>
		private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName.Equals(ObjectHelper.GetPropertyName(() => ViewModel.EditedModInfoVM)))
				BindModInfo(ViewModel.EditedModInfoVM);
		}

		/// <summary>
		/// Binds the <see cref="IModInfo"/>'s view model to the control.
		/// </summary>
		/// <param name="p_vmlModInfo">The view model to bind to the control.</param>
		protected void BindModInfo(ModInfoVM p_vmlModInfo)
		{
			tbxAuthor.DataBindings.Clear();
			tbxDescription.DataBindings.Clear();
			tbxName.DataBindings.Clear();
			tbxVersion.DataBindings.Clear();
			tbxWebsite.DataBindings.Clear();

			BindingHelper.CreateFullBinding(tbxAuthor, () => tbxAuthor.Text, p_vmlModInfo, () => p_vmlModInfo.Author);
			BindingHelper.CreateFullBinding(tbxDescription, () => tbxDescription.Text, p_vmlModInfo, () => p_vmlModInfo.Description);
			BindingHelper.CreateFullBinding(tbxName, () => tbxName.Text, p_vmlModInfo, () => p_vmlModInfo.ModName);
			BindingHelper.CreateFullBinding(tbxVersion, () => tbxVersion.Text, p_vmlModInfo, () => p_vmlModInfo.HumanReadableVersion);
			BindingHelper.CreateFullBinding(tbxWebsite, () => tbxWebsite.Text, p_vmlModInfo, () => p_vmlModInfo.Website);
			pbxScreenshot.Image = p_vmlModInfo.Screenshot;

			ckbLockAuthor.Visible = false;
			ckbLockDescription.Visible = false;
			ckbLockName.Visible = false;
			ckbLockScreenshot.Visible = false;
			ckbLockVersion.Visible = false;
			ckbLockWebsite.Visible = false;

			p_vmlModInfo.Errors.ErrorChanged -= ModInfo_ErrorChanged;
			p_vmlModInfo.Errors.ErrorChanged += new EventHandler<ErrorEventArguments>(ModInfo_ErrorChanged);
		}

		#region Validation

		/// <summary>
		/// Handles the <see cref="ErrorContainer.ErrorChanged"/> event of the view model of the
		/// <see cref="IModInfo"/> being edited.
		/// </summary>
		/// <remarks>
		/// This displays any validation errors.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="ErrorEventArguments"/> describing the event arguments.</param>
		private void ModInfo_ErrorChanged(object sender, ErrorEventArguments e)
		{
			if (e.Property.Equals(ObjectHelper.GetPropertyName<IModInfo>(x => x.ModName)))
				erpErrors.SetError(tbxName, e.Error);
			else if (e.Property.Equals(ObjectHelper.GetPropertyName<IModInfo>(x => x.Website)))
				erpErrors.SetError(tbxWebsite, e.Error);
		}

		/// <summary>
		/// Handles the <see cref="Control.Validating"/> event of the editing controls.
		/// </summary>
		/// <remarks>
		/// This saves the changed values, if they are valid.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void Control_Validated(object sender, EventArgs e)
		{
			if (ViewModel.EditedModInfoVM.Validate() && AutoCommitChanges)
				ViewModel.EditedModInfoVM.Commit();
		}

		/// <summary>
		/// Handles the <see cref="Control.KeyDown"/> event of the editing controls.
		/// </summary>
		/// <remarks>
		/// This resets the value of the control to the original bound value.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="KeyEventArgs"/> describing the event arguments.</param>
		private void Control_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Escape)
			{
				Binding bndBinding = ((Control)sender).DataBindings[0];
				ViewModel.EditedModInfoVM.Reset(bndBinding.BindingMemberInfo.BindingField);
				bndBinding.ReadValue();
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
		}

		#endregion

		#region Screenshot

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the set screenshot button.
		/// </summary>
		/// <remarks>
		/// Sets the screenshot for the mod.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butSetScreenshot_Click(object sender, EventArgs e)
		{
			if (ofdScreenshot.ShowDialog() == DialogResult.OK)
			{
				ViewModel.EditedModInfoVM.Screenshot = new ExtendedImage(File.ReadAllBytes(ofdScreenshot.FileName));
				pbxScreenshot.Image = ViewModel.EditedModInfoVM.Screenshot;
				if (AutoCommitChanges)
					ViewModel.EditedModInfoVM.Commit();
			}
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the clear screenshot button.
		/// </summary>
		/// <remarks>
		/// Removes the screenshot from the mod.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butClearScreenshot_Click(object sender, EventArgs e)
		{
			ViewModel.EditedModInfoVM.Screenshot = null;
			pbxScreenshot.Image = null;
			if (AutoCommitChanges)
				ViewModel.EditedModInfoVM.Commit();
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="CheckBox.CheckedChanged"/> event of the lock buttons.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void LockValue_CheckedChanged(object sender, EventArgs e)
		{
			CheckBox ckbLock = (CheckBox)sender;
			ckbLock.ImageIndex = ckbLock.Checked ? 0 : 1;
		}

		/// <summary>
		/// Handles the <see cref="Control.KeyPress"/> event of the description textbox.
		/// </summary>
		/// <remarks>
		/// This selects all text when Ctrl-A is pressed.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="KeyPressEventArgs"/> describing the event arguments.</param>
		private void tbxDescription_KeyPress(object sender, KeyPressEventArgs e)
		{
			//character 1 is equivalent to Ctrl-A
			if (e.KeyChar == '\x01')
			{
				((TextBox)sender).SelectAll();
				e.Handled = true;
			}
		}
	}
}
