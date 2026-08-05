using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Plugins;
using Nexus.Client.Util;

namespace Nexus.Client.PluginManagement
{
	public class ApplyLoadOrderTask : ThreadedBackgroundTask
	{
		#region Properties

		/// <summary>
		/// Gets the current ModManager.
		/// </summary>
		/// <value>The current ModManager.</value>
		protected IPluginManager PluginManager { get; private set; }

		protected Dictionary<Plugin, string> RegisteredPlugins { get; private set; }

		protected bool SortingOnly { get; private set; }

		/// <summary>
		/// Gets whether the imported active state replaces the current non-protected active state.
		/// </summary>
		protected bool ReplaceActiveState { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a load-order application task that preserves unspecified active plugins.
		/// </summary>
		/// <param name="p_pmgPluginManager">The plugin manager that will apply the state.</param>
		/// <param name="p_kvpRegisteredPlugins">The ordered plugins and their requested active states.</param>
		/// <param name="p_booSortingOnly">Whether only plugin ordering should be changed.</param>
		public ApplyLoadOrderTask(IPluginManager p_pmgPluginManager, Dictionary<Plugin, string> p_kvpRegisteredPlugins, bool p_booSortingOnly)
			: this(p_pmgPluginManager, p_kvpRegisteredPlugins, p_booSortingOnly, false)
		{
		}

		/// <summary>
		/// Initializes a load-order application task.
		/// </summary>
		/// <param name="p_pmgPluginManager">The plugin manager that will apply the state.</param>
		/// <param name="p_kvpRegisteredPlugins">The ordered plugins and their requested active states.</param>
		/// <param name="p_booSortingOnly">Whether only plugin ordering should be changed.</param>
		/// <param name="p_booReplaceActiveState">Whether unspecified non-protected plugins should be deactivated.</param>
		public ApplyLoadOrderTask(IPluginManager p_pmgPluginManager, Dictionary<Plugin, string> p_kvpRegisteredPlugins, bool p_booSortingOnly, bool p_booReplaceActiveState)
		{
			PluginManager = p_pmgPluginManager;
			RegisteredPlugins = p_kvpRegisteredPlugins;
			SortingOnly = p_booSortingOnly;
			ReplaceActiveState = p_booReplaceActiveState;
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
		public void Update()
		{
			Start();
		}

		/// <summary>
		/// Resumes the task.
		/// </summary>
		/// <exception cref="InvalidOperationException">Thrown if the task is not paused.</exception>
		public override void Resume()
		{
			Update();
		}

		/// <summary>
		/// The method that is called to start the backgound task.
		/// </summary>
		/// <param name="args">Arguments to for the task execution.</param>
		/// <param name="p_strMessage">The validation failure message, when the requested state is invalid.</param>
		/// <returns>The validation exception when the requested state is invalid; otherwise, <c>null</c>.</returns>
		protected override object DoWork(object[] args, out string p_strMessage)
		{
			p_strMessage = null;

			try
			{
				OverallMessage = String.Format("Applying load order...");
				OverallProgress = 0;
				OverallProgressStepSize = 1;
				OverallProgressMaximum = RegisteredPlugins.Count;

				if (SortingOnly)
				{
					PluginManager.SetPluginOrder(RegisteredPlugins.Keys.ToList());
				}
				else
				{
					List<Plugin> activePlugins = ReplaceActiveState
						? new List<Plugin>()
						: new List<Plugin>(PluginManager.ActivePlugins);

					foreach (KeyValuePair<Plugin, string> kvp in RegisteredPlugins)
					{
						if (kvp.Value == "1")
						{
							if (PluginManager.CanChangeActiveState(kvp.Key) && !activePlugins.Contains(kvp.Key))
								activePlugins.Add(kvp.Key);
						}
						else if (kvp.Value == "0")
						{
							if (PluginManager.CanChangeActiveState(kvp.Key))
								activePlugins.Remove(kvp.Key);
						}

						if (OverallProgress < OverallProgressMaximum)
							StepOverallProgress();
					}

					PluginManager.ApplyPluginState(RegisteredPlugins.Keys.ToList(), activePlugins);
				}

				if (OverallProgress < OverallProgressMaximum)
					OverallProgress = OverallProgressMaximum;
			}
			catch (Exception ex)
			{
				Status = TaskStatus.Error;
				p_strMessage = ex.Message;
				return ex;
			}

			return null;
		}

	}
}
