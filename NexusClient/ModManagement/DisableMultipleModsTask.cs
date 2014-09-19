using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Mods;
using Nexus.Client.UI;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement
{
	public class DisableMultipleModsTask : ThreadedBackgroundTask
	{
		bool m_booCancel = false;

		#region Fields

		private VirtualModActivator m_ivaVirtualModActivator = null;
		private List<IMod> m_rolModList = null;

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		public DisableMultipleModsTask(List<IMod> p_rolModList, VirtualModActivator p_ivaVirtualModActivator)
		{
			m_ivaVirtualModActivator = p_ivaVirtualModActivator;
			m_rolModList = p_rolModList;
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
			OverallMessage = "Disabling all the active mods...";
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			OverallProgressMaximum = m_rolModList.Count;
			ShowItemProgress = false;

			ConfirmActionMethod camConfirm = (ConfirmActionMethod)p_objArgs[0];

			foreach (IMod modMod in m_rolModList)
			{
				m_ivaVirtualModActivator.DisableMod(modMod);
				if (OverallProgress < OverallProgressMaximum)
					StepOverallProgress();

				if (m_booCancel)
					break;					
			}
			return null;
		}
	}
}
