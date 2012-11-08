using System;
using System.ComponentModel;
using System.Windows.Forms;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Util;

namespace Nexus.Client.DownloadMonitoring.UI
{
	/// <summary>
	/// A list view item that displays the status of a <see cref="IBackgroundTask"/>
	/// </summary>
	public class DownloadListViewItem : ListViewItem
	{
		#region Properties

		/// <summary>
		/// Gets the <see cref="IBackgroundTask"/> whose status is being displayed by this list view item.
		/// </summary>
		/// <value>The <see cref="IBackgroundTask"/> whose status is being displayed by this list view item.</value>
		public IBackgroundTask Task { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_tskTask">The task whose status is to be displayed by this list
		/// view item.</param>
		public DownloadListViewItem(IBackgroundTask p_tskTask)
		{
			Task = p_tskTask;

			ListViewSubItem lsiSubItem = SubItems[0];
			lsiSubItem.Name = ObjectHelper.GetPropertyName(() => p_tskTask.OverallMessage);
			lsiSubItem.Text = p_tskTask.OverallMessage;

			lsiSubItem = SubItems.Add(new ListViewSubItem());
			lsiSubItem.Name = ObjectHelper.GetPropertyName(() => p_tskTask.OverallProgress);
			if (p_tskTask.ShowOverallProgressAsMarquee)
				lsiSubItem.Text = "Working...";
			else
			{
				lsiSubItem.Text = "";
			}

			lsiSubItem = SubItems.Add(new ListViewSubItem());
			lsiSubItem.Name = ObjectHelper.GetPropertyName(() => p_tskTask.Status);
			if (p_tskTask.Status == TaskStatus.Running)
				lsiSubItem.Text = "Downloading";
			else
				lsiSubItem.Text = p_tskTask.Status.ToString();

			lsiSubItem = SubItems.Add(new ListViewSubItem());
			lsiSubItem.Name = ObjectHelper.GetPropertyName(() => p_tskTask.ItemMessage);
			if (p_tskTask.ShowItemProgress)
				lsiSubItem.Text = p_tskTask.ItemMessage;
			else
				lsiSubItem.Text = "";

			lsiSubItem = SubItems.Add(new ListViewSubItem());
			lsiSubItem.Name = "Fileserver";
			lsiSubItem.Text = "";

			lsiSubItem = SubItems.Add(new ListViewSubItem());
			lsiSubItem.Name = "ETA";
			lsiSubItem.Text = "";

			lsiSubItem = SubItems.Add(new ListViewSubItem());
			lsiSubItem.Name = ObjectHelper.GetPropertyName(() => p_tskTask.ItemProgress);
			if (p_tskTask.ShowItemProgress)
			{
				if (p_tskTask.ShowItemProgressAsMarquee)
					lsiSubItem.Text = "Working...";
				else
				{
					lsiSubItem.Text = "";
				}
			}

			lsiSubItem = SubItems.Add(new ListViewSubItem());
			lsiSubItem.Name = ObjectHelper.GetPropertyName(() => p_tskTask.InnerTaskStatus);

			p_tskTask.PropertyChanged += new PropertyChangedEventHandler(Task_PropertyChanged);
		}

		#endregion

		#region Task Property Change Handling

		/// <summary>
		/// Handles the <see cref="INotifyPropertyChanged.PropertyChanged"/> event of the task.
		/// </summary>
		/// <remarks>
		/// This updates the progress message and other text in the list view item.
		/// </remarks>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="e">A <see cref="PropertyChangedEventArgs"/> that describes the event arguments.</param>
		private void Task_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			string strPropertyName = e.PropertyName;
			if ((ListView != null) && ListView.InvokeRequired)
			{
				ListView.Invoke((Action<IBackgroundTask, string>)HandleChangedTaskProperty, sender, e.PropertyName);
				return;
			}
			HandleChangedTaskProperty((IBackgroundTask)sender, e.PropertyName);
		}

		/// <summary>
		/// Updates the list view item to display the changed property.
		/// </summary>
		/// <param name="p_tskTask">The task whose property has changed.</param>
		/// <param name="p_strPropertyName">The name of the propety that has changed.</param>
		private void HandleChangedTaskProperty(IBackgroundTask p_tskTask, string p_strPropertyName)
		{
			if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.OverallMessage)))
			{
				SubItems[ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.OverallMessage)].Text = p_tskTask.OverallMessage;
			}
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ItemMessage)))
			{
				string Message = p_tskTask.ItemMessage;
				if (Message.IndexOf("ETA:") > 0)
				{
					try
					{
						SubItems[ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.OverallProgress)].Text = Message.Substring(Message.IndexOf("(") + 1, Message.IndexOf(")") - Message.IndexOf("(") - 1);
						SubItems[ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ItemMessage)].Text = Message.Substring(Message.LastIndexOf("(") + 1, Message.LastIndexOf(")") - Message.LastIndexOf("(") - 1);
						SubItems["ETA"].Text = Message.Substring(Message.IndexOf("ETA:") + 5, Message.LastIndexOf("(") - Message.IndexOf("ETA:") - 6);
						if ((p_tskTask.Status.ToString() == "Running") && (p_tskTask.Status.ToString() == SubItems[ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.Status)].Text))
							SubItems[ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.Status)].Text = "Downloading";
						SubItems[ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ItemProgress)].Text = p_tskTask.ActiveThreads.ToString();
					}
					catch
					{
					}
				}
				else if (Message.IndexOf("Fileserver:") >= 0)
					SubItems["Fileserver"].Text = Message.Substring(Message.IndexOf(":") + 1);
			}
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ItemProgress))
					|| p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ItemProgressMaximum))
					|| p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ItemProgressMinimum))
					|| p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ShowItemProgress))
					|| p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ShowItemProgressAsMarquee)))
			{
				if (p_tskTask.ShowItemProgress)
				{
					if (p_tskTask.ShowItemProgressAsMarquee)
						SubItems[ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ItemProgress)].Text = "Working...";
				}
				else
				{
					SubItems[ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ItemMessage)].Text = null;
					SubItems[ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ItemProgress)].Text = null;
				}
			}
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.Status)))
			{
				if (p_tskTask.Status == TaskStatus.Running)
				{
					SubItems[p_strPropertyName].Text = "Downloading";
				}
				else
				{
					SubItems[p_strPropertyName].Text = p_tskTask.Status.ToString();
					SubItems[ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ItemMessage)].Text = "";
					SubItems[ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ItemProgress)].Text = "";
					if (!(p_tskTask.Status.ToString() == "Paused"))
						SubItems[ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.OverallProgress)].Text = "";
					SubItems["ETA"].Text = "";
				}
			}
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.InnerTaskStatus)))
			{
				if (p_tskTask.InnerTaskStatus.ToString() == "Retrying")
					SubItems[ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.Status)].Text = p_tskTask.InnerTaskStatus.ToString();
				else if (p_tskTask.InnerTaskStatus.ToString() == "Running")
					SubItems[ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.Status)].Text = "Downloading";

			}
		}

		#endregion
	}
}
