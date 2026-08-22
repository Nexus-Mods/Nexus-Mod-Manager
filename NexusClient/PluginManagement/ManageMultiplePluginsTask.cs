using System;
using System.Collections.Generic;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Plugins;
using Nexus.Client.PluginManagement.InstallationLog;
using Nexus.Client.UI;
using System.Linq;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.PluginManagement
{
	public class ManageMultiplePluginsTask : ThreadedBackgroundTask
	{
		#region Fields

		protected IList<Plugin> PluginList { get; private set; }
		protected IPluginManager PluginManager { get; private set; }
		protected bool EnablePlugins { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		public ManageMultiplePluginsTask(List<Plugin> p_lstPlugins, IPluginManager p_pmgPluginManager, bool p_booEnable)
		{
			PluginList = p_lstPlugins;
			PluginManager = p_pmgPluginManager;
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
		/// <param name="args">Arguments to for the task execution.</param>
		/// <returns>Always <c>null</c>.</returns>
		protected override object DoWork(object[] args)
		{
			OverallMessage = EnablePlugins
				? LanguageManager.Get("Tasks.Plugins.ActivatingManaged", "Activating all the managed plugins...")
				: LanguageManager.Get("Tasks.Plugins.DisablingManaged", "Disabling all the managed plugins...");
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			OverallProgressMaximum = PluginList.Count;
			ShowItemProgress = false;

			List<Plugin> lstPlugins = new List<Plugin>();
			HashSet<Plugin> hstActivePlugins = new HashSet<Plugin>(PluginManager.ActivePlugins.Where(x => x != null), PluginComparer.Filename);

			ConfirmActionMethod camConfirm = (ConfirmActionMethod)args[0];

			foreach (Plugin plugin in PluginList)
			{
				if (plugin != null && EnablePlugins != hstActivePlugins.Contains(plugin))
					lstPlugins.Add(plugin);

				if (OverallProgress < OverallProgressMaximum)
					StepOverallProgress();
			}

			PluginManager.SetPluginActivation(lstPlugins, EnablePlugins);

			return null;
		}
	}
}
