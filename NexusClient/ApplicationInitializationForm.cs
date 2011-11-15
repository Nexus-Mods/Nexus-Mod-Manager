using System;
using System.ComponentModel;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.BackgroundTasks.UI;
using Nexus.Client.Controls;
using Nexus.Client.Games;
using Nexus.Client.UI;
using Nexus.Client.Util;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.UI;

namespace Nexus.Client
{
	/// <summary>
	/// The view displaying the progress of the application initialization.
	/// </summary>
	/// <remarks>
	/// This view may actually be invisible, and simple act as the UI form that
	/// displays the views the application initialization requests shown.
	/// </remarks>
	public partial class ApplicationInitializationForm : Form
	{
		private ApplicationInitializer m_vmlViewModel = null;
		private DialogResult m_drtLastDialogResult = DialogResult.None;

		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		protected ApplicationInitializer ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				m_vmlViewModel.LoginUser = Login;
				m_vmlViewModel.GetInstallationPathCandidate = GetInstallationPathCandidate;
				m_vmlViewModel.ConfirmMakeWritable = ConfirmMakeWritable;
				m_vmlViewModel.ShowView = ShowView;
				m_vmlViewModel.ShowMessage = ShowMessage;
				m_vmlViewModel.ConfirmItemOverwrite = ConfirmItemOverwrite;

				m_vmlViewModel.TaskStarted += new EventHandler<EventArgs<IBackgroundTask>>(ViewModel_TaskStarted);
				m_vmlViewModel.TaskEnded += new EventHandler<TaskEndedEventArgs>(ApplicationInitializer_TaskEnded);

				lblVersion.Text = m_vmlViewModel.EnvironmentInfo.ApplicationVersion.ToString();
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_iniApplicationInitializer">The application initializer.</param>
		public ApplicationInitializationForm(ApplicationInitializer p_iniApplicationInitializer)
		{
			InitializeComponent();
			ViewModel = p_iniApplicationInitializer;			
		}

		#endregion

		#region Form Events

		/// <summary>
		/// Raises the <see cref="Form.Shown"/> event.
		/// </summary>
		/// <remarks>
		/// This immediately closes the form if the task has already completed.
		/// </remarks>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnShown(EventArgs e)
		{
			base.OnShown(e);
			if (!ViewModel.IsActive)
			{
				//we need to set this again, as it gets reset once the form is shown,
				// and if we are here, we set it before the form was shown
				DialogResult = m_drtLastDialogResult;
				Close();
			}
		}

		/// <summary>
		/// Raises the <see cref="Form.Closing"/> event.
		/// </summary>
		/// <remarks>
		/// This prevents the form from closing until the task has completed.
		/// </remarks>
		/// <param name="e">A <see cref="CancelEventArgs"/> describing the event arguments.</param>
		protected override void OnClosing(CancelEventArgs e)
		{
			e.Cancel = ViewModel.IsActive;
			if (!e.Cancel)
			{
				ViewModel.TaskEnded -= ApplicationInitializer_TaskEnded;
			}
			base.OnClosing(e);
		}

		#endregion

		void ApplicationInitializer_TaskEnded(object sender, TaskEndedEventArgs e)
		{
			if (Disposing || IsDisposed)
				return;
			try
			{
				//if the form hasn't been created, there is no point in updating
				// the UI
				//further, if the form handle hasn't been created, there is no way
				// to safely update the form from another thread
				if (IsHandleCreated)
				{

					if (InvokeRequired)
					{
						Invoke((Action<object, TaskEndedEventArgs>)ApplicationInitializer_TaskEnded, sender, e);
						return;
					}
					else if ((Owner != null) && Owner.InvokeRequired)
					{
						Owner.Invoke((Action<object, TaskEndedEventArgs>)ApplicationInitializer_TaskEnded, sender, e);
						return;
					}
				}
			}
			catch (ObjectDisposedException)
			{
				//if the control is disposed, we don't need to do anything
				return;
			}

			if (e.Status == TaskStatus.Complete)
				DialogResult = DialogResult.OK;
			else
				DialogResult = DialogResult.Cancel;
			m_drtLastDialogResult = DialogResult;
			if (!String.IsNullOrEmpty(e.Message))
				MessageBox.Show(this, e.Message, "Message", MessageBoxButtons.OK, MessageBoxIcon.Information);
			if (e.ReturnValue != null)
				ShowMessage((ViewMessage)e.ReturnValue);
			//only try to close the form if it has been created
			// otherwise Close() can get called before the form is shown,
			// and then once Show() or ShowDialog() is called an
			// System.ObjectDisposedException is thrown
			if (IsHandleCreated)
			{
				for (Int32 i = 0; i < 10; i++)
				{
					try
					{
						this.Close();
						break;
					}
					catch (InvalidOperationException)
					{
						//we are trying to close the form at the exact moment it is being shown
						// wait a moment and try again
						Thread.Sleep(100);
					}
				}
			}
		}

		/// <summary>
		/// Logins the user into the current mod repository.
		/// </summary>
		/// <param name="p_vmlViewModel">The view model that provides the data and operations for this view.</param>
		/// <returns><c>true</c> if the user was successfully logged in;
		/// <c>false</c> otherwise</returns>
		protected bool Login(LoginFormVM p_vmlViewModel)
		{
			if (InvokeRequired)
				return (bool)Invoke((Func<LoginFormVM, bool>)Login, p_vmlViewModel);
			LoginForm frmLogin = new LoginForm(p_vmlViewModel);
			return frmLogin.ShowDialog(this) == DialogResult.OK;
		}

		/// <summary>
		/// Gets a possible candidate for the installation path for the game mode represented by the given game mode descriptor.
		/// </summary>
		/// <param name="p_gmdGameModeInfo">The descriptor for the game mode for which the installation path is to be found.</param>
		/// <param name="p_strDefaultPath">The default installation path.</param>
		/// <param name="p_strPath">The installation path for the game mode represented by the given game mode descriptor.</param>
		/// <returns><c>true</c> if we got a candidate; <c>fasle</c> otherwise.</returns>
		private bool GetInstallationPathCandidate(IGameModeDescriptor p_gmdGameModeInfo, string p_strDefaultPath, out string p_strPath)
		{
			if (InvokeRequired)
			{
				string strPath = null;
				bool booResult = false;
				Invoke((MethodInvoker)(() => booResult = GetInstallationPathCandidate(p_gmdGameModeInfo, p_strDefaultPath, out strPath)));
				p_strPath = strPath;
				return booResult;
			}
			StringBuilder stbMessage = new StringBuilder();
			stbMessage.AppendFormat("Could not find {0} directory.", p_gmdGameModeInfo.Name).AppendLine();
			stbMessage.AppendFormat("{0}'s registry entry appears to be missing or incorrect.", p_gmdGameModeInfo.Name).AppendLine();
			stbMessage.AppendFormat("Please enter the path to your {0} game file, or click \"Auto Detect\" to search", p_gmdGameModeInfo.Name);
			stbMessage.AppendFormat(" for the install directory. Note that Auto Detection can take several minutes.");
			string strLabel = String.Format("{0} Game Directory:", p_gmdGameModeInfo.Name);
			string strTitle = String.Format("{0} Location", p_gmdGameModeInfo.Name);

			using (WorkingDirectorySelectionForm wdfForm = new WorkingDirectorySelectionForm(strTitle, p_gmdGameModeInfo.ModeTheme.Icon, stbMessage.ToString(), strLabel, p_gmdGameModeInfo.GameExecutables))
			{
				wdfForm.WorkingDirectory = p_strDefaultPath;
				if (wdfForm.ShowDialog(this) == DialogResult.Cancel)
				{
					p_strPath = null;
					return false;
				}
				p_strPath = wdfForm.WorkingDirectory;
			}
			return true;
		}

		/// <summary>
		/// Confirms whether a file system item should be made writable.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_strFileSystemItemPath">The path of the file system item to be made writable</param>
		/// <param name="p_booRememberSelection">Whether to remember the selection.</param>
		/// <returns><c>true</c> if the file system item should be made writable;
		/// <c>false</c> otherwise.</returns>
		private bool ConfirmMakeWritable(IEnvironmentInfo p_eifEnvironmentInfo, string p_strFileSystemItemPath, out bool p_booRememberSelection)
		{
			if (InvokeRequired)
			{
				bool booRemember = false;
				bool booResult = false;
				Invoke((MethodInvoker)(() => booResult = ConfirmMakeWritable(p_eifEnvironmentInfo, p_strFileSystemItemPath, out booRemember)));
				p_booRememberSelection = booRemember;
				return booResult;
			}
			return (ExtendedMessageBox.Show(this, String.Format("'{0}' is read-only, so it can't be managed by {1}. Would you like to make it not read-only?", p_strFileSystemItemPath, p_eifEnvironmentInfo.Settings.ModManagerName), "Read Only", MessageBoxButtons.YesNo, MessageBoxIcon.Question, out p_booRememberSelection) == DialogResult.Yes);
		}

		/// <summary>
		/// This asks the user to confirm the overwriting of the specified item.
		/// </summary>
		/// <param name="p_strItemMessage">The message describing the item being overwritten..</param>
		/// <param name="p_booAllowPerGroupChoice">Whether to allow the user to make the decision to amke the selection for all items in the current item's group.</param>
		/// <returns>The user's choice.</returns>
		private OverwriteResult ConfirmItemOverwrite(string p_strItemMessage, bool p_booAllowPerGroupChoice)
		{
			if (InvokeRequired)
				return (OverwriteResult)Invoke((ConfirmItemOverwriteDelegate)ConfirmItemOverwrite, p_strItemMessage, p_booAllowPerGroupChoice);
			return OverwriteForm.ShowDialog(this, p_strItemMessage, p_booAllowPerGroupChoice);
		}

		/// <summary>
		/// Displays a view.
		/// </summary>
		/// <param name="p_ivwView">The view to display.</param>
		/// <param name="p_booModal">Wheher the view should be modal.</param>
		/// <returns>The return value of the displayed view.</returns>
		private object ShowView(IView p_ivwView, bool p_booModal)
		{
			if (InvokeRequired)
				return Invoke((ShowViewDelegate)ShowView, p_ivwView, p_booModal);
			Form frmView = p_ivwView as Form;
			if (frmView == null)
				throw new ArgumentException(String.Format("The given view must be of type {0}. Type {1} found.", typeof(Form).FullName, p_ivwView.GetType().FullName), "p_ivwView");
			return frmView.ShowDialog(this);
		}

		/// <summary>
		/// Displays a message.
		/// </summary>
		/// <param name="p_vwmMessage">The properties of the message to dislpay.</param>
		/// <returns>The return value of the displayed message.</returns>
		private object ShowMessage(ViewMessage p_vwmMessage)
		{
			if (InvokeRequired)
				return Invoke((ShowMessageDelegate)ShowMessage, p_vwmMessage);
			if (String.IsNullOrEmpty(p_vwmMessage.Details))
			{
				bool booFound = true;
				MessageBoxButtons mbbOptions = MessageBoxButtons.OK;
				switch (p_vwmMessage.Options)
				{
					case ExtendedMessageBoxButtons.Abort | ExtendedMessageBoxButtons.Retry | ExtendedMessageBoxButtons.Ignore:
						mbbOptions = MessageBoxButtons.AbortRetryIgnore;
						break;
					case ExtendedMessageBoxButtons.OK:
						mbbOptions = MessageBoxButtons.OK;
						break;
					case ExtendedMessageBoxButtons.OK | ExtendedMessageBoxButtons.Cancel:
						mbbOptions = MessageBoxButtons.OKCancel;
						break;
					case ExtendedMessageBoxButtons.Retry | ExtendedMessageBoxButtons.Cancel:
						mbbOptions = MessageBoxButtons.RetryCancel;
						break;
					case ExtendedMessageBoxButtons.Yes | ExtendedMessageBoxButtons.No:
						mbbOptions = MessageBoxButtons.YesNo;
						break;
					case ExtendedMessageBoxButtons.Yes | ExtendedMessageBoxButtons.No | ExtendedMessageBoxButtons.Cancel:
						mbbOptions = MessageBoxButtons.YesNoCancel;
						break;
					default:
						booFound = false;
						break;
				}
				if (booFound)
					return MessageBox.Show(this, p_vwmMessage.Message, p_vwmMessage.Title, mbbOptions, p_vwmMessage.MessageType);
			}
			return ExtendedMessageBox.Show(this, p_vwmMessage.Message, p_vwmMessage.Title, p_vwmMessage.Details, p_vwmMessage.Options, p_vwmMessage.MessageType);
		}

		/// <summary>
		/// Handles the <see cref="ApplicationInitializer.TaskStarted"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This display a progress window for the started task.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ViewModel_TaskStarted(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Func<IWin32Window, IBackgroundTask, DialogResult>)ProgressDialog.ShowDialog, this, e.Argument);
				return;
			}
			ProgressDialog.ShowDialog(this, e.Argument);
		}
	}
}
