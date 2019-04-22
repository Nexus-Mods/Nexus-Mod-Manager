using System.Collections.Generic;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;
using Nexus.Client.UI;
using ChinhDo.Transactions;

namespace Nexus.Client.ModManagement
{
	public class ToggleModUpdateChecksTask : ThreadedBackgroundTask
	{
		bool m_booCancel = false;

		#region Properties

		protected HashSet<IMod> m_hashMods = new HashSet<IMod>();
		protected bool? m_booEnable = false;

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		public ToggleModUpdateChecksTask(HashSet<IMod> p_hashMods, bool? p_booEnable, ConfirmActionMethod p_camConfirm)
		{
			m_hashMods = p_hashMods;
			m_booEnable = p_booEnable;
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
		/// <param name="args">Arguments to for the task execution.</param>
		/// <returns>Always <c>null</c>.</returns>
		protected override object DoWork(object[] args)
		{
			OverallMessage = "Toggling mod update checks...";
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			OverallProgressMaximum = m_hashMods.Count;
			ShowItemProgress = false;

			ConfirmActionMethod camConfirm = (ConfirmActionMethod)args[0];

			foreach (IMod modMod in m_hashMods)
			{
				ModInfo mifUpdatedMod = new ModInfo(modMod);
				if (m_booEnable == null)
					mifUpdatedMod.UpdateChecksEnabled = !modMod.UpdateChecksEnabled;
				else
				{
					if (modMod.UpdateChecksEnabled == m_booEnable.Value)
						continue;
					else
						mifUpdatedMod.UpdateChecksEnabled = m_booEnable.Value;
				}
				modMod.UpdateInfo(mifUpdatedMod, false);

				if (OverallProgress < OverallProgressMaximum)
					StepOverallProgress();

				if (m_booCancel)
					break;
			}

			return null;
		}
	}
}
