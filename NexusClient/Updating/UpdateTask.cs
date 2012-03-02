using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.UI;
using Nexus.Client.Util;

namespace Nexus.Client.Updating
{
	/// <summary>
	/// Updates the mod manager, and the currently running game mode.
	/// </summary>
	public class UpdateTask : ThreadedBackgroundTask
	{
		private List<IUpdater> m_lstUpdaters = new List<IUpdater>();
		private List<IUpdater> m_lstFailedUpdaters = new List<IUpdater>();

		#region Properties

		/// <summary>
		/// Gets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		protected IEnvironmentInfo EnvironmentInfo { get; private set; }

		/// <summary>
		/// Gets the current game mode.
		/// </summary>
		/// <value>The current game mode.</value>
		protected IGameMode GameMode { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public UpdateTask(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo)
		{
			GameMode = p_gmdGameMode;
			EnvironmentInfo = p_eifEnvironmentInfo;
		}

		#endregion

		#region Event Raising

		/// <summary>
		/// Raises the <see cref="IBackgroundTask.TaskEnded"/> event.
		/// </summary>
		/// <param name="e">A <see cref="TaskEndedEventArgs"/> describing the event arguments.</param>
		protected override void OnTaskEnded(TaskEndedEventArgs e)
		{
			if (m_lstFailedUpdaters.Count > 0)
			{
				StringBuilder stbMessage = new StringBuilder();
				for (Int32 i = 0; i < m_lstFailedUpdaters.Count; i++)
				{
					IUpdater updUpdater = m_lstFailedUpdaters[i];
					stbMessage.AppendFormat("{0}: {1}", updUpdater.Name, updUpdater.Message);
					if (i < m_lstFailedUpdaters.Count - 1)
						stbMessage.AppendLine();
				}
				base.OnTaskEnded(new TaskEndedEventArgs(e.Status, stbMessage.ToString(), e.ReturnValue));
			}
			else
				base.OnTaskEnded(e);
		}
		#endregion

		/// <summary>
		/// Starts the update.
		/// </summary>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		public void Update(ConfirmActionMethod p_camConfirm)
		{
			Start(p_camConfirm);
		}

		/// <summary>
		/// Cancels the update.
		/// </summary>
		public override void Cancel()
		{
			base.Cancel();
			foreach (IUpdater updUpdater in m_lstUpdaters)
				updUpdater.Cancel();
		}

		/// <summary>
		/// The method that is called to start the backgound task.
		/// </summary>
		/// <param name="p_objArgs">Arguments to for the task execution.</param>
		/// <returns>Always <c>null</c>.</returns>
		protected override object DoWork(object[] p_objArgs)
		{
			ConfirmActionMethod camConfirm = (ConfirmActionMethod)p_objArgs[0];

			//TODO add game mode updaters
			OverallMessage = "Updating...";
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			ShowItemProgress = true;

			m_lstUpdaters.Clear();
			m_lstUpdaters.Add(new ProgrammeUpdater(EnvironmentInfo));
			m_lstUpdaters.AddRange(GameMode.GetUpdaters());
			OverallProgressMaximum = m_lstUpdaters.Count;

			foreach (IUpdater updUpdater in m_lstUpdaters)
			{
				ItemMessage = updUpdater.Message;
				ItemProgress = updUpdater.Progress;
				ItemProgressMaximum = updUpdater.ProgressMaximum;
				updUpdater.PropertyChanged += new PropertyChangedEventHandler(Updater_PropertyChanged);
				updUpdater.Confirm = camConfirm;
				if (!updUpdater.Update())
					m_lstFailedUpdaters.Add(updUpdater);
				StepOverallProgress();
				updUpdater.PropertyChanged -= new PropertyChangedEventHandler(Updater_PropertyChanged);
			}
			return null;
		}

		private void Updater_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			IUpdater updUpdater = (IUpdater)sender;
			if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<IUpdater>(x => x.Message)))
				ItemMessage = updUpdater.Message;
			else if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<IUpdater>(x => x.Progress)))
				ItemProgress = updUpdater.Progress;
			else if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<IUpdater>(x => x.ProgressMaximum)))
				ItemProgressMaximum = updUpdater.ProgressMaximum;
		}
	}
}
