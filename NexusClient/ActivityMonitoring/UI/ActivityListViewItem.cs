using System;
using System.ComponentModel;
using System.Windows.Forms;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Util;

namespace Nexus.Client.ActivityMonitoring.UI
{
	/// <summary>
	/// A list view item that displays the status of a <see cref="IBackgroundTask"/>
	/// </summary>
	public class ActivityListViewItem : ListViewItem
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
		public ActivityListViewItem(IBackgroundTask p_tskTask)
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
				Int64 intDenominator = p_tskTask.OverallProgressMaximum - p_tskTask.OverallProgressMinimum;
				Int64 intPercent = (intDenominator == 0) ? 0 : p_tskTask.OverallProgress / intDenominator;
				lsiSubItem.Text = intPercent.ToString("P0");
			}

			lsiSubItem = SubItems.Add(new ListViewSubItem());
			lsiSubItem.Name = ObjectHelper.GetPropertyName(() => p_tskTask.ItemMessage);
			if (p_tskTask.ShowItemProgress)
				lsiSubItem.Text = p_tskTask.ItemMessage;

			lsiSubItem = SubItems.Add(new ListViewSubItem());
			lsiSubItem.Name = ObjectHelper.GetPropertyName(() => p_tskTask.ItemProgress);
			if (p_tskTask.ShowItemProgress)
			{
				if (p_tskTask.ShowItemProgressAsMarquee)
					lsiSubItem.Text = "Working...";
				else
				{
					Int64 intDenominator = p_tskTask.ItemProgressMaximum - p_tskTask.ItemProgressMinimum;
					Int64 intPercent = (intDenominator == 0) ? 0 : p_tskTask.ItemProgress / intDenominator;
					lsiSubItem.Text = intPercent.ToString("P0");
				}
			}

			p_tskTask.PropertyChanged += new PropertyChangedEventHandler(Task_PropertyChanged);

			lsiSubItem = SubItems.Add(new ListViewSubItem());
			lsiSubItem.Name = ObjectHelper.GetPropertyName(() => p_tskTask.Status);
			lsiSubItem.Text = p_tskTask.Status.ToString();
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
				SubItems[p_strPropertyName].Text = p_tskTask.OverallMessage;
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.OverallProgress))
					|| p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.OverallProgressMaximum))
					|| p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.OverallProgressMinimum))
					|| p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ShowOverallProgressAsMarquee)))
			{
				if (p_tskTask.ShowOverallProgressAsMarquee)
					SubItems[ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.OverallProgress)].Text = "Working...";
				else
				{
					Int64 intDivisor = p_tskTask.OverallProgressMaximum - p_tskTask.OverallProgressMinimum;
					float fltPercentage = (intDivisor == 0) ? 0 : ((float)p_tskTask.OverallProgress) / intDivisor;
					SubItems[ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.OverallProgress)].Text = fltPercentage.ToString("P0");
				}
			}
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ItemMessage)))
				SubItems[p_strPropertyName].Text = p_tskTask.ItemMessage;
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
					else
					{
						Int64 intDivisor = p_tskTask.ItemProgressMaximum - p_tskTask.ItemProgressMinimum;
						float fltPercentage = (intDivisor == 0) ? 0 : ((float)p_tskTask.ItemProgress) / intDivisor;
						SubItems[ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ItemProgress)].Text = fltPercentage.ToString("P0");
					}
				}
				else
				{
					SubItems[ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ItemMessage)].Text = null;
					SubItems[ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ItemProgress)].Text = null;
				}
			}
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.Status)))
				SubItems[p_strPropertyName].Text = p_tskTask.Status.ToString();
		}

		#endregion
	}
}
