namespace Nexus.Client
{
    using System;
    using System.ComponentModel;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.Threading;
    using System.Windows.Forms;
    using Nexus.Client.BackgroundTasks;
    using Nexus.Client.BackgroundTasks.UI;
    using Nexus.Client.ModManagement;
    using Nexus.Client.ModManagement.UI;
    using Nexus.Client.SSO;
    using Nexus.Client.UI;
    using Nexus.Client.Util;
    using Nexus.UI.Controls;

    /// <summary>
	/// The view displaying the progress of the application initialization.
	/// </summary>
	/// <remarks>
	/// This view may actually be invisible, and simple act as the UI form that
	/// displays the views the application initialization requests shown.
	/// </remarks>
	public partial class ApplicationInitializationForm : ManagedFontForm
	{
		private ApplicationInitializer m_vmlViewModel = null;
		private DialogResult m_drtLastDialogResult = DialogResult.None;
		private System.Windows.Forms.Timer m_tmrGlow = new System.Windows.Forms.Timer();
		Int32 m_intGlowPosition = 0;
		Int32 m_intProgressWidth = 0;
		
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
				m_vmlViewModel.ConfirmMakeWritable = ConfirmMakeWritable;
				m_vmlViewModel.ShowView = ShowView;
				m_vmlViewModel.ShowMessage = ShowMessage;
				m_vmlViewModel.ConfirmItemOverwrite = ConfirmItemOverwrite;
				ColourImage = pbxLogo.Image;
				GreyscaleImage = GenerateGreyscale(ColourImage);
				pbxLogo.Image = new Bitmap(GreyscaleImage);

				m_tmrGlow.Interval = 2;
				m_tmrGlow.Tick += new EventHandler(m_tmrGlow_Tick);
				m_tmrGlow.Start();
				m_vmlViewModel.TaskStarted += new EventHandler<EventArgs<IBackgroundTask>>(ViewModel_TaskStarted);
				m_vmlViewModel.TaskEnded += new EventHandler<TaskEndedEventArgs>(Task_TaskEnded);
				m_vmlViewModel.PropertyChanged += new PropertyChangedEventHandler(Task_PropertyChanged);

				lblVersion.Text = Application.ProductVersion;
				Text = CommonData.ModManagerName;
			}
		}

		/// <summary>
		/// Gets the splash image used to display progress.
		/// </summary>
		/// <value>The splash image used to display progress.</value>
		protected Image GreyscaleImage { get; private set; }

		/// <summary>
		/// Gets the colour image that is incrementally displayed to show progress.
		/// </summary>
		/// <value>The colour image that is incrementally displayed to show progress.</value>
		protected Image ColourImage { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_iniApplicationInitializer">The application initializer.</param>
		public ApplicationInitializationForm(ApplicationInitializer p_iniApplicationInitializer)
		{
			InitializeComponent();

			var pos = PointToScreen(lblVersion.Location);
			pos = pbxLogo.PointToClient(pos);
			lblVersion.Parent = pbxLogo;
			lblVersion.Location = pos;
			lblVersion.BackColor = Color.Transparent;

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
				ViewModel.TaskEnded -= Task_TaskEnded;
			}
			base.OnClosing(e);
		}

		#endregion

		#region Progress

		/// <summary>
		/// Generates a greyscale image from the given image.
		/// </summary>
		/// <param name="p_imgImage">The image for which to create a greyscale version.</param>
		/// <returns>A greyscale image based on the given image.</returns>
		private Image GenerateGreyscale(Image p_imgImage)
		{
			Bitmap imgGrey = new Bitmap(p_imgImage);
			Color clrOld = Color.Fuchsia;
			for (Int32 y = 0; y < imgGrey.Height; y++)
			{
				for (Int32 x = 0; x < imgGrey.Width; x++)
				{
					clrOld = imgGrey.GetPixel(x, y);

					byte r = clrOld.R;
					byte g = clrOld.G;
					byte b = clrOld.B;

					//r = g = b = (byte)(0.21 * r + 0.72 * g + 0.07 * b);
					r = g = b = (byte)(0.299 * r + 0.587 * g + 0.114 * b);

					imgGrey.SetPixel(x, y, Color.FromArgb(clrOld.A, (Int32)r, (Int32)g, (Int32)b));
				}
			}
			return imgGrey;
		}

		/// <summary>
		/// Handles the <see cref="System.Windows.Forms.Timer.Tick"/> event of the glow effect timer.
		/// </summary>
		/// <remarks>
		/// This updates the progress image, and renders the moving glow effect.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void m_tmrGlow_Tick(object sender, EventArgs e)
		{
			Int32 intDrawPosition = m_intGlowPosition;
			Int32 intStartDrawPosition = intDrawPosition + 900000;
			m_intGlowPosition += 3;
			if (m_intGlowPosition >= m_intProgressWidth)
				m_intGlowPosition = 0;
			using (Graphics grpGraphics = Graphics.FromImage(pbxLogo.Image))
			{
				Rectangle rctCopyArea = new Rectangle(0, 0, m_intProgressWidth, ColourImage.Height);
				grpGraphics.CompositingMode = CompositingMode.SourceCopy;
				grpGraphics.DrawImage(ColourImage, rctCopyArea, rctCopyArea, GraphicsUnit.Pixel);
			}
		
			Int32 intHalfWidth = 20;
			float fltLuminence = 80;
			byte bteR = 0;
			byte bteG = 0;
			byte bteB = 0;
			Bitmap bmpGlow = (Bitmap)pbxLogo.Image;
			for (Int32 i = 1; i <= intHalfWidth; i++)
			{
				for (Int32 j = 0; j < bmpGlow.Height; j++)
				{
					Color clrPixel = bmpGlow.GetPixel(intDrawPosition, j);
					bteR = (byte)Math.Min(255, clrPixel.R + (fltLuminence / intHalfWidth) * i);
					bteG = (byte)Math.Min(255, clrPixel.G + (fltLuminence / intHalfWidth) * i);
					bteB = (byte)Math.Min(255, clrPixel.B + (fltLuminence / intHalfWidth) * i);
					bmpGlow.SetPixel(intDrawPosition, j, Color.FromArgb(clrPixel.A, bteR, bteG, bteB));
				}
				if (++intDrawPosition >= m_intProgressWidth)
				{
					intDrawPosition = 0;
					intStartDrawPosition -= 900000;
				}
				if (intDrawPosition >= intStartDrawPosition)
					return;
			}
			for (Int32 i = intHalfWidth; i >= 1; i--)
			{
				for (Int32 j = 0; j < bmpGlow.Height; j++)
				{
					Color clrPixel = bmpGlow.GetPixel(intDrawPosition, j);
					bteR = (byte)Math.Min(255, clrPixel.R + (fltLuminence / intHalfWidth) * i);
					bteG = (byte)Math.Min(255, clrPixel.G + (fltLuminence / intHalfWidth) * i);
					bteB = (byte)Math.Min(255, clrPixel.B + (fltLuminence / intHalfWidth) * i);
					bmpGlow.SetPixel(intDrawPosition, j, Color.FromArgb(clrPixel.A, bteR, bteG, bteB));
				}
				if (++intDrawPosition >= m_intProgressWidth)
				{
					intDrawPosition = 0;
					intStartDrawPosition -= 900000;
				}
				if (intDrawPosition >= intStartDrawPosition)
					return;
			}
			pbxLogo.Refresh();
		}

		#endregion

		#region Task Event Handling

		/// <summary>
		/// Handles the <see cref="INotifyPropertyChanged.PropertyChanged"/> event of the task.
		/// </summary>
		/// <remarks>
		/// This updates the progress bars and messages.
		/// </remarks>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="e">A <see cref="PropertyChangedEventArgs"/> that describes the event arguments.</param>
		private void Task_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			try
			{
				string strPropertyName = e.PropertyName;
				//if the form hasn't been created, there is no point in updating
				// the UI
				//further, if the form handle hasn't been created, there is no way
				// to safely update the form from another thread
				if (IsHandleCreated)
				{
					if (InvokeRequired)
					{
						Invoke((Action<string>)HandleChangedProperty, e.PropertyName);
						return;
					}
					else if ((Owner != null) && Owner.InvokeRequired)
					{
						Owner.Invoke((Action<string>)HandleChangedProperty, e.PropertyName);
						return;
					}
					HandleChangedProperty(e.PropertyName);
				}
			}
			catch (ObjectDisposedException)
			{
				throw;
			}
		}

		/// <summary>
		/// Updates the form to display the changed property.
		/// </summary>
		/// <param name="p_strPropertyName">The name of the propety that has changed.</param>
		private void HandleChangedProperty(string p_strPropertyName)
		{
			try
			{
				Int32 intMax = (Int32)ViewModel.OverallProgressMaximum;
				Int32 intMin = (Int32)ViewModel.OverallProgressMinimum;
				Int32 intProgress = (Int32)ViewModel.OverallProgress;
				Int32 intDivisor = intMax - intMin;
				float fltPercentage = (intDivisor > 0) ? ((float)intProgress) / intDivisor : 0;

				if (fltPercentage <= 1.0)
					m_intProgressWidth = (Int32)(fltPercentage * ColourImage.Width);
			}
			catch (NullReferenceException)
			{
				//this can happen if we try to update the form before its handle has been created
				// we should never get here, but if we do, we don't need to care
			}
		}

		/// <summary>
		/// Handles the <see cref="IBackgroundTask.TaskEnded"/> event of the task.
		/// </summary>
		/// <remarks>
		/// This sets the <see cref="Form.DialogResult"/>, dependant upon whether or not the
		/// task was cancelled.
		/// </remarks>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="e">A <see cref="TaskEndedEventArgs"/> that describes the event arguments.</param>
		private void Task_TaskEnded(object sender, TaskEndedEventArgs e)
		{
			m_tmrGlow.Stop();
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
						Invoke((Action<object, TaskEndedEventArgs>)Task_TaskEnded, sender, e);
						return;
					}
					else if ((Owner != null) && Owner.InvokeRequired)
					{
						Owner.Invoke((Action<object, TaskEndedEventArgs>)Task_TaskEnded, sender, e);
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
			if (e.ReturnValue != null)
				ShowMessage((ViewMessage)e.ReturnValue);
			else if (!String.IsNullOrEmpty(e.Message))
				MessageBox.Show(this, e.Message, "Message", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

		#endregion

		#region UI Interaction

		/// <summary>
		/// Logins the user into the current mod repository.
		/// </summary>
		/// <param name="p_vmlViewModel">The view model that provides the data and operations for this view.</param>
		/// <returns><c>true</c> if the user was successfully logged in;
		/// <c>false</c> otherwise</returns>
		protected bool Login(AuthenticationFormViewModel p_vmlViewModel)
		{
			if (InvokeRequired)
            {
                return (bool)Invoke((Func<AuthenticationFormViewModel, bool>)Login, p_vmlViewModel);
            }

            var frmLogin = new AuthenticationForm(p_vmlViewModel, null);
			return frmLogin.ShowDialog(this) == DialogResult.OK;
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
			return (ExtendedMessageBox.Show(this, String.Format("'{0}' is read-only, so it can't be managed by {1}. Would you like to make it not read-only?", p_strFileSystemItemPath, CommonData.ModManagerName), "Read Only", MessageBoxButtons.YesNo, MessageBoxIcon.Question, out p_booRememberSelection) == DialogResult.Yes);
		}

		/// <summary>
		/// This asks the user to confirm the overwriting of the specified item.
		/// </summary>
		/// <param name="p_strItemMessage">The message describing the item being overwritten..</param>
		/// <param name="p_booAllowPerGroupChoice">Whether to allow the user to make the decision to amke the selection for all items in the current item's group.</param>
		/// <param name="p_booAllowPerModChoice">Whether to allow the user to make the decision to make the selection for all items in the current Mod.</param>
		/// <returns>The user's choice.</returns>
		private OverwriteResult ConfirmItemOverwrite(string p_strItemMessage, bool p_booAllowPerGroupChoice, bool p_booAllowPerModChoice)
		{
			if (InvokeRequired)
				return (OverwriteResult)Invoke((ConfirmItemOverwriteDelegate)ConfirmItemOverwrite, p_strItemMessage, p_booAllowPerGroupChoice, p_booAllowPerModChoice);
			return OverwriteForm.ShowDialog(this, p_strItemMessage, p_booAllowPerGroupChoice, p_booAllowPerModChoice);
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

		#endregion

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
