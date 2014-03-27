using System;
using System.ComponentModel;
using System.Threading;
using System.Windows.Forms;

namespace Nexus.Client.BackgroundTasks.UI
{
	/// <summary>
	/// This is a window that displays the progress of a <see cref="IBackgroundTask"/>
	/// </summary>
	public partial class ProgressDialog : Form
	{
		private Int32 m_intCreatedThreadId = -1;
		private DialogResult m_drtLastDialogResult = DialogResult.None;

		/// <summary>
		/// Shows the progress dialog as a modal window.
		/// </summary>
		/// <param name="p_bgtTask">The <see cref="IBackgroundTask"/> whose progress is to be displayed.</param>
		/// <returns>The <see cref="Form.DialogResult"/> of the prigress dialog.</returns>
		public static DialogResult ShowDialog(IBackgroundTask p_bgtTask)
		{
			return new ProgressDialog(p_bgtTask).ShowDialog();
		}

		/// <summary>
		/// Shows the progress dialog as a modal window that is a child of the given owner.
		/// </summary>
		/// <param name="p_winOwner">The owner of the dialog.</param>
		/// <param name="p_bgtTask">The <see cref="IBackgroundTask"/> whose progress is to be displayed.</param>
		/// <returns>The <see cref="Form.DialogResult"/> of the prigress dialog.</returns>
		public static DialogResult ShowDialog(IWin32Window p_winOwner, IBackgroundTask p_bgtTask)
		{
			return new ProgressDialog(p_bgtTask).ShowDialog(p_winOwner);
		}

		#region Properties

		/// <summary>
		/// Gets or sets the <see cref="IBackgroundTask"/> whose progress is to be displayed.
		/// </summary>
		/// <value>The <see cref="IBackgroundTask"/> whose progress is to be displayed.</value>
		protected IBackgroundTask Task { get; set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_bgtTask">The <see cref="IBackgroundTask"/> whose progress is to be displayed.</param>
		protected ProgressDialog(IBackgroundTask p_bgtTask)
		{
			m_intCreatedThreadId = Thread.CurrentThread.ManagedThreadId;
			Task = p_bgtTask;
			Task.TaskEnded += new EventHandler<TaskEndedEventArgs>(Task_TaskEnded);
			Task.PropertyChanged += new PropertyChangedEventHandler(Task_PropertyChanged);
			InitializeComponent();

			pbrItemProgress.Value = (Int32)Task.ItemProgress;
			lblItemMessage.Text = Task.ItemMessage;
			pbrTotalProgress.Value = (Int32)Task.OverallProgress;
			lblTotalMessage.Text = Task.OverallMessage;
			pbrItemProgress.Maximum = (Int32)Task.ItemProgressMaximum;
			pbrItemProgress.Minimum = (Int32)Task.ItemProgressMinimum;
			pbrItemProgress.Step = Task.ItemProgressStepSize;
			pbrTotalProgress.Maximum = (Int32)Task.OverallProgressMaximum;
			pbrTotalProgress.Minimum = (Int32)Task.OverallProgressMinimum;
			pbrTotalProgress.Step = Task.OverallProgressStepSize;
			pnlItemProgress.Visible = Task.ShowItemProgress;
			pbrTotalProgress.Style = Task.ShowOverallProgressAsMarquee ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
			pbrItemProgress.Style = Task.ShowItemProgressAsMarquee ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
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
				pnlItemProgress.Visible = Task.ShowItemProgress;
				if (pnlItemProgress.Visible)
				{
					if ((Task.ItemProgress <= pbrItemProgress.Maximum) && (Task.ItemProgress <= Task.ItemProgressMaximum))
						pbrItemProgress.Value = (Int32)Task.ItemProgress;
					else
						pbrItemProgress.Value = (pbrItemProgress.Maximum > (Int32)Task.ItemProgressMaximum) ? (Int32)Task.ItemProgressMaximum : pbrItemProgress.Maximum;
					lblItemMessage.Text = Task.ItemMessage;
					pbrItemProgress.Maximum = (Int32)Task.ItemProgressMaximum;
					pbrItemProgress.Minimum = (Int32)Task.ItemProgressMinimum;
					pbrItemProgress.Step = Task.ItemProgressStepSize;
					pbrItemProgress.Style = Task.ShowItemProgressAsMarquee ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
				}

				if ((Task.OverallProgress <= pbrTotalProgress.Maximum) && (Task.OverallProgress <= Task.OverallProgressMaximum))
					pbrTotalProgress.Value = (Int32)Task.OverallProgress;
				else
					pbrTotalProgress.Value = (pbrTotalProgress.Maximum > (Int32)Task.OverallProgressMaximum) ? (Int32)Task.OverallProgressMaximum : pbrTotalProgress.Maximum;
				lblTotalMessage.Text = Task.OverallMessage;
				pbrTotalProgress.Maximum = (Int32)Task.OverallProgressMaximum;
				pbrTotalProgress.Minimum = (Int32)Task.OverallProgressMinimum;
				pbrTotalProgress.Step = Task.OverallProgressStepSize;
				pbrTotalProgress.Style = Task.ShowOverallProgressAsMarquee ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
				/*
				if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ItemProgress)))
					pbrItemProgress.Value = Task.ItemProgress;
				else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ItemMessage)))
					lblItemMessage.Text = Task.ItemMessage;
				else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.OverallProgress)))
					pbrTotalProgress.Value = Task.OverallProgress;
				else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.OverallMessage)))
					lblTotalMessage.Text = Task.OverallMessage;
				else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ItemProgressMaximum)))
					pbrItemProgress.Maximum = Task.ItemProgressMaximum;
				else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ItemProgressMinimum)))
					pbrItemProgress.Minimum = Task.ItemProgressMinimum;
				else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ItemProgressStepSize)))
					pbrItemProgress.Step = Task.ItemProgressStepSize;
				else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.OverallProgressMaximum)))
					pbrTotalProgress.Maximum = Task.OverallProgressMaximum;
				else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.OverallProgressMinimum)))
					pbrTotalProgress.Minimum = Task.OverallProgressMinimum;
				else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.OverallProgressStepSize)))
					pbrTotalProgress.Step = Task.OverallProgressStepSize;
				else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ShowItemProgress)))
					pnlItemProgress.Visible = Task.ShowItemProgress;
				else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ShowOverallProgressAsMarquee)))
					pbrTotalProgress.Style = Task.ShowOverallProgressAsMarquee ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
				else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ShowItemProgressAsMarquee)))
					pbrItemProgress.Style = Task.ShowItemProgressAsMarquee ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;*/
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
			if (!String.IsNullOrEmpty(e.Message))
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
			if (!Task.IsActive)
			{
				//we need to set this again, as it gets reset once the form is shown,
				// and if we are here, we set it before the form was shown
				DialogResult = m_drtLastDialogResult;
				Close();
			}
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the cancel button.
		/// </summary>
		/// <remarks>
		/// This asks the <see cref="BackgroundWorker"/> to cancel. It also disables the
		/// cancel button to let the user know the process is cancelling.
		/// </remarks>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> that describes the event arguments.</param>
		private void butCancel_Click(object sender, EventArgs e)
		{
			butCancel.Enabled = false;
			butCancel.Text = "Cancelling";
			DialogResult = DialogResult.Cancel;
			Task.Cancel();
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
			e.Cancel = Task.IsActive;
			if (!e.Cancel)
			{
				Task.TaskEnded -= Task_TaskEnded;
				Task.PropertyChanged -= Task_PropertyChanged;
			}
			base.OnClosing(e);
		}

		#endregion

		/// <summary>
		/// Allows extension of the dispose method.
		/// </summary>
		/// <remarks>
		/// This unwires listeners that are wired to object on other threads. This is
		/// because if the form is closed before the threads are finished the threads may
		/// raise events to which we are listening, which will cause access to the control
		/// after it has been disposed (which will raise an exception).
		/// </remarks>
		partial void DoDispose()
		{
			Task.TaskEnded -= Task_TaskEnded;
			Task.PropertyChanged -= Task_PropertyChanged;
		}
	}
}
