using System;
using System.Collections.Generic;
using System.ComponentModel;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Mods;
using Nexus.Client.UI;

namespace Nexus.Client.ModManagement
{
	public class ModMigrationTask : ThreadedBackgroundTask
	{
		bool m_booCancel = false;

		#region Properties


		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_ModManager">The current ModManager.</param>
		/// <param name="p_lstMods">The mod list.</param>
		/// <param name="p_intNewValue">The new category id.</param>
		public ModMigrationTask()
		{
			ShowItemProgress = false;
			OverallMessage = "Initializing migration...";
			OverallProgress = 0;
			OverallProgressMaximum = 8;
		}

		#endregion

		#region Event Raising

		/// <summary>
		/// Raises the <see cref="IBackgroundTask.TaskEnded"/> event.
		/// </summary>
		/// <param name="e">A <see cref="TaskEndedEventArgs"/> describing the event arguments.</param>
		protected override void OnTaskEnded(TaskEndedEventArgs e)
		{
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
			m_booCancel = true;
		}

		/// <summary>
		/// The method that is called to start the backgound task.
		/// </summary>
		/// <param name="p_objArgs">Arguments to for the task execution.</param>
		/// <returns>Always <c>null</c>.</returns>
		protected override object DoWork(object[] p_objArgs)
		{
			return null;
		}

		public void SetStatus(string p_strOverallMessage, Int32 p_intOverallProgress, TaskStatus p_tstStatus)
		{
			if (!p_strOverallMessage.Equals(OverallMessage))
				OverallMessage = p_strOverallMessage;
			if (!(p_intOverallProgress == OverallProgress))
				OverallProgress = p_intOverallProgress;
			if (!(p_tstStatus == Status))
			{
				Status = p_tstStatus;
			}
		}
	}
}
