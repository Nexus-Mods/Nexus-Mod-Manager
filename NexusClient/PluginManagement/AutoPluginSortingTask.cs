using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.Plugins;
using Nexus.Client.UI;

namespace Nexus.Client.PluginManagement
{
	public class AutoPluginSortingTask : ThreadedBackgroundTask
	{
		#region Properties

		protected IGameMode GameMode { get; set; }
		protected IList<Plugin> Plugins { get; set; }
		
		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		public AutoPluginSortingTask(IGameMode p_gmdCurrentGameMode, IList<Plugin> p_lstPlugins, ConfirmActionMethod p_camConfirm)
		{
			GameMode = p_gmdCurrentGameMode;
			Plugins = p_lstPlugins;
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
		}

		/// <summary>
		/// The method that is called to start the backgound task.
		/// </summary>
		/// <param name="p_objArgs">Arguments to for the task execution.</param>
		/// <returns>Always <c>null</c>.</returns>
		protected override object DoWork(object[] p_objArgs)
		{
			ShowOverallProgressAsMarquee = true;
			OverallMessage = "Sorting, please wait...";
			string[] SortedPlugins = GameMode.SortPlugins(Plugins);
			return SortedPlugins;
		}
	}
}
