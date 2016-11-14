using System;
using System.ComponentModel;
using System.Windows.Forms;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModManagement;
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
		public AddModTask Task { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_tskTask">The task whose status is to be displayed by this list
		/// view item.</param>
		public DownloadListViewItem(AddModTask p_tskTask)
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
			{
				if (p_tskTask.IsRemote)
					lsiSubItem.Text = "Downloading";
				else
					lsiSubItem.Text = "Moving";
			}
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
			try
			{
				string strPropertyName = e.PropertyName;
				if ((ListView != null) && ListView.InvokeRequired)
				{
					ListView.Invoke((Action<AddModTask, string>)HandleChangedTaskProperty, sender, e.PropertyName);
					return;
				}
				HandleChangedTaskProperty((AddModTask)sender, e.PropertyName);
			}
			catch {}
		}

		/// <summary>
		/// Updates the list view item to display the changed property.
		/// </summary>
		/// <param name="p_tskTask">The task whose property has changed.</param>
		/// <param name="p_strPropertyName">The name of the propety that has changed.</param>
		private void HandleChangedTaskProperty(AddModTask p_tskTask, string p_strPropertyName)
		{
			if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<AddModTask>(x => x.OverallMessage)))
			{
				SubItems[ObjectHelper.GetPropertyName<AddModTask>(x => x.OverallMessage)].Text = p_tskTask.OverallMessage;
			}
			else if ((p_strPropertyName.Equals(ObjectHelper.GetPropertyName<AddModTask>(x => x.ETA_Seconds))) || (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<AddModTask>(x => x.ETA_Minutes))))
			{
				if (Task.Status == TaskStatus.Running)
					SubItems["ETA"].Text = String.Format("{0:00}:{1:00}", p_tskTask.ETA_Minutes, p_tskTask.ETA_Seconds);
				else
					SubItems["ETA"].Text = String.Empty;
			}
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<AddModTask>(x => x.FileServer)))
			{
				if (Task.Status == TaskStatus.Running)
					SubItems["Fileserver"].Text = p_tskTask.FileServer;
				else
					SubItems["Fileserver"].Text = String.Empty;
			}
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<AddModTask>(x => x.TaskSpeed)))
			{
				SubItems[ObjectHelper.GetPropertyName<AddModTask>(x => x.ItemMessage)].Text = String.Format("{0:} KB/s", p_tskTask.TaskSpeed.ToString());
			}
			else if ((p_strPropertyName.Equals(ObjectHelper.GetPropertyName<AddModTask>(x => x.DownloadProgress))) || (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<AddModTask>(x => x.DownloadMaximum))))
			{
				if(p_tskTask.DownloadMaximum < 1024)
					SubItems[ObjectHelper.GetPropertyName<AddModTask>(x => x.OverallProgress)].Text = String.Format("{0}KB / {1}KB", p_tskTask.DownloadProgress, p_tskTask.DownloadMaximum);
				else
					SubItems[ObjectHelper.GetPropertyName<AddModTask>(x => x.OverallProgress)].Text = String.Format("{0}MB / {1}MB", (p_tskTask.DownloadProgress / 1024), (p_tskTask.DownloadMaximum / 1024));
			}
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<AddModTask>(x => x.ActiveThreads)))
			{
				if (Task.Status == TaskStatus.Running)
					SubItems[ObjectHelper.GetPropertyName<AddModTask>(x => x.ItemProgress)].Text = p_tskTask.ActiveThreads.ToString();
				else
					SubItems[ObjectHelper.GetPropertyName<AddModTask>(x => x.ItemProgress)].Text = String.Empty;
			}
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<AddModTask>(x => x.ItemProgress))
					|| p_strPropertyName.Equals(ObjectHelper.GetPropertyName<AddModTask>(x => x.ItemProgressMaximum))
					|| p_strPropertyName.Equals(ObjectHelper.GetPropertyName<AddModTask>(x => x.ItemProgressMinimum))
					|| p_strPropertyName.Equals(ObjectHelper.GetPropertyName<AddModTask>(x => x.ShowItemProgress))
					|| p_strPropertyName.Equals(ObjectHelper.GetPropertyName<AddModTask>(x => x.ShowItemProgressAsMarquee)))
			{
				if (p_tskTask.ShowItemProgress)
				{
					if (p_tskTask.ShowItemProgressAsMarquee)
						SubItems[ObjectHelper.GetPropertyName<AddModTask>(x => x.ItemProgress)].Text = "Working...";
				}
				else
				{
					SubItems[ObjectHelper.GetPropertyName<AddModTask>(x => x.ItemMessage)].Text = null;
					SubItems[ObjectHelper.GetPropertyName<AddModTask>(x => x.ItemProgress)].Text = null;
				}
			}
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<AddModTask>(x => x.Status)))
			{
				if (p_tskTask.Status == TaskStatus.Running)
				{
					if (p_tskTask.IsRemote)
						SubItems[p_strPropertyName].Text = "Downloading";
					else
						SubItems[p_strPropertyName].Text = "Moving";
				}
				else
				{
					SubItems[p_strPropertyName].Text = p_tskTask.Status.ToString();
					SubItems[ObjectHelper.GetPropertyName<AddModTask>(x => x.ItemMessage)].Text = "";
					SubItems[ObjectHelper.GetPropertyName<AddModTask>(x => x.ItemProgress)].Text = "";
					if (!(p_tskTask.Status.ToString() == "Paused"))
						SubItems[ObjectHelper.GetPropertyName<AddModTask>(x => x.OverallProgress)].Text = "";
					SubItems["ETA"].Text = "";
				}
			}
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<AddModTask>(x => x.InnerTaskStatus)))
			{
				if ((p_tskTask.InnerTaskStatus.ToString() == "Retrying") && ((p_tskTask.Status != TaskStatus.Paused) && (p_tskTask.Status != TaskStatus.Queued)))
					SubItems[ObjectHelper.GetPropertyName<AddModTask>(x => x.Status)].Text = p_tskTask.InnerTaskStatus.ToString();
				else if (p_tskTask.InnerTaskStatus.ToString() == "Running")
				{
					if (p_tskTask.IsRemote)
						SubItems[ObjectHelper.GetPropertyName<AddModTask>(x => x.Status)].Text = "Downloading";
					else
						SubItems[ObjectHelper.GetPropertyName<AddModTask>(x => x.Status)].Text = "Moving";
				}
				else
				{
					SubItems["ETA"].Text = "";
					SubItems[ObjectHelper.GetPropertyName<AddModTask>(x => x.ItemMessage)].Text = "";
					SubItems[ObjectHelper.GetPropertyName<AddModTask>(x => x.ItemProgress)].Text = "";
				}
			}
		}

		#endregion
	}
}
