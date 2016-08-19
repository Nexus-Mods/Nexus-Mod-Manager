using System;
using System.Collections.Generic;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Plugins;
using Nexus.Client.PluginManagement.InstallationLog;
using Nexus.Client.UI;

namespace Nexus.Client.PluginManagement
{
	public class ManageMultiplePluginsTask : ThreadedBackgroundTask
	{
		#region Fields

		protected IList<Plugin> PluginList { get; private set; }
		protected ActivePluginLog APL { get; private set; }
		protected bool EnablePlugins { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		public ManageMultiplePluginsTask(List<Plugin> p_lstPlugins, ActivePluginLog p_aplLog, bool p_booEnable)
		{
			PluginList = p_lstPlugins;
			APL = p_aplLog;
			EnablePlugins = p_booEnable;
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
		}

		/// <summary>
		/// The method that is called to start the backgound task.
		/// </summary>
		/// <param name="p_objArgs">Arguments to for the task execution.</param>
		/// <returns>Always <c>null</c>.</returns>
		protected override object DoWork(object[] p_objArgs)
		{
			OverallMessage = String.Format("{0} all the managed plugins...", EnablePlugins ? "Activating" : "Disabling");
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			OverallProgressMaximum = PluginList.Count;
			ShowItemProgress = false;

			List<Plugin> lstPlugins = new List<Plugin>();

			ConfirmActionMethod camConfirm = (ConfirmActionMethod)p_objArgs[0];

			foreach (Plugin plugin in PluginList)
			{
				if (EnablePlugins)
				{
					if (!APL.IsPluginActive(plugin))
						lstPlugins.Add(plugin);
				}
				else
				{
					if (APL.IsPluginActive(plugin))
						lstPlugins.Add(plugin);
				}

				if (OverallProgress < OverallProgressMaximum)
					StepOverallProgress();
			}

			if (EnablePlugins)
				APL.ActivatePlugins(lstPlugins);
			else
				APL.DeactivatePlugins(lstPlugins);

			return null;
		}
	}
}
