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

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_ModManager">The current ModManager.</param>
		/// <param name="p_lstMods">The mod list.</param>
		/// <param name="p_intNewValue">The new category id.</param>
		public ApplyLoadOrderTask(IPluginManager p_pmgPluginManager, Dictionary<Plugin, string> p_kvpRegisteredPlugins, bool p_booSortingOnly)
		{
			PluginManager = p_pmgPluginManager;
			RegisteredPlugins = p_kvpRegisteredPlugins;
			SortingOnly = p_booSortingOnly;
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
		/// <param name="p_objArgs">Arguments to for the task execution.</param>
		/// <returns>Always <c>null</c>.</returns>
		protected override object DoWork(object[] p_objArgs)
		{
			OverallMessage = String.Format("Applying load order...");
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			OverallProgressMaximum = RegisteredPlugins.Count;

			Transactions.TransactionScope tsTransaction = null;
			try
			{
				tsTransaction = new Transactions.TransactionScope();

				if (!SortingOnly)
				{
					foreach (KeyValuePair<Plugin, string> kvp in RegisteredPlugins)
					{
						if (kvp.Value == "1")
						{
							if (PluginManager.CanChangeActiveState(kvp.Key))
								PluginManager.ActivatePlugin(kvp.Key);
						}
						if (kvp.Value == "0")
						{
							if (PluginManager.CanChangeActiveState(kvp.Key))
								PluginManager.DeactivatePlugin(kvp.Key);
						}

						if (OverallProgress < OverallProgressMaximum)
							StepOverallProgress();
					}
				}

				PluginManager.SetPluginOrder(RegisteredPlugins.Keys.ToList());

				if (OverallProgress < OverallProgressMaximum)
					OverallProgress = OverallProgressMaximum;

				tsTransaction.Complete();
			}
			catch
			{
				throw;
			}
			finally
			{
				if (tsTransaction != null)
					tsTransaction.Dispose();
			}

			return null;
		}
	}
}
