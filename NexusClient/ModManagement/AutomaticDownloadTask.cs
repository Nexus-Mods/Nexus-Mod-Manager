using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModAuthoring;
using Nexus.Client.UI;
using Nexus.Client.Util.Collections;


namespace Nexus.Client.ModManagement
{
	public class AutomaticDownloadTask : ThreadedBackgroundTask
	{

		bool m_booCancel = false;
		
		#region Properties

		/// <summary>
		/// Gets the current ModManager.
		/// </summary>
		/// <value>The current ModManager.</value>
		protected ModManager ModManager { get; private set; }
		
		protected ProfileManager ProfileManager { get; private set; }
		protected ConfirmOverwriteCallback ConfirmOverwriteCallback;
		protected List<ProfileMissingModInfo> MissingInfoList = null;


		ThreadSafeObservableList<string> MissingMods;
		
		/// <summary>
		/// Gets the delegate to call to confirm an action.
		/// </summary>
		/// <value>The delegate to call to confirm an action.</value>
		protected ConfirmActionMethod ConfirmActionMethod { get; private set; }


		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_ModManager">The current ModManager.</param>
		/// <param name="p_lstMods">The mod list.</param>
		/// <param name="p_intNewValue">The new category id.</param>
		public AutomaticDownloadTask(List<string> p_lstMissingMods, ModManager p_mmModManager, ProfileManager p_pmProfileManager, ConfirmOverwriteCallback p_cocConfirmOverwrite)
		{
			MissingMods = new ThreadSafeObservableList<string>(p_lstMissingMods);
			ModManager = p_mmModManager;
			ProfileManager = p_pmProfileManager;
			ConfirmOverwriteCallback = p_cocConfirmOverwrite;
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
		public override void Resume()
		{
			Start(ConfirmActionMethod);
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
			try
			{
				ConfirmActionMethod camConfirm = (ConfirmActionMethod)p_objArgs[0];
				OverallMessage = "Starting missing mods download...";
				OverallProgress = 0;
				OverallProgressStepSize = 1;
				ShowItemProgress = false;
				OverallProgressMaximum = MissingMods.Count();

				Status = TaskStatus.Running;
								
				int i = 1;
				
				if (MissingMods.Count > 0)
				{
					foreach (string URI in MissingMods)
					{
						if (m_booCancel)
							break;
						OverallMessage = "Adding the Mods to download: " + i + "/" + MissingMods.Count();
						StepOverallProgress();
						i++;

						ModManager.AddMod(URI, ConfirmOverwriteCallback);
						Thread.Sleep(100);
					}
				}
			}
			catch (Exception ex)
			{
				this.Status = TaskStatus.Error;
				this.ItemMessage = ("There was a problem communicating with the Nexus server: " + Environment.NewLine + ex.Message);
				return null;
			}

			return null;
		}
	}
}

