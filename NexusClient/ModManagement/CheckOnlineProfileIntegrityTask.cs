using System;
using System.Collections.Generic;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModRepositories;
using Nexus.Client.UI;

namespace Nexus.Client.ModManagement
{
	public class CheckOnlineProfileIntegrityTask : ThreadedBackgroundTask
	{
		Dictionary<string, string> MissingModsList = null;
		string GameModeID = string.Empty;
		bool m_booCancel = false;

		#region Properties

		/// <summary>
		/// Gets the current ModManager.
		/// </summary>
		/// <value>The current ModManager.</value>

		protected IModRepository ModRepository { get; private set; }
		protected IModProfile ModProfile { get; private set; }
		protected IProfileManager ProfileManager { get; private set; }

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
		public CheckOnlineProfileIntegrityTask(IModRepository p_mrRepository, IModProfile p_imProfile, IProfileManager p_pmProfileManager, Dictionary<string, string> p_dicMissingMod, string p_strGameModeID)
		{
			ModRepository = p_mrRepository;
			ModProfile = p_imProfile;
			MissingModsList = p_dicMissingMod;
			GameModeID = p_strGameModeID;
			ProfileManager = p_pmProfileManager;
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
				OverallMessage = "Checking the Profile Online Integrity...";
				OverallProgress = 0;
				OverallProgressStepSize = 1;
				ShowItemProgress = false;
				OverallProgressMaximum = 1;

				List<ProfileMissingModInfo> lstMissingInfo = null;
				Dictionary<string, string> dctMissingMods = new Dictionary<string, string>();
				Dictionary<string, string> dctNewDownloadID = new Dictionary<string, string>();

				if (ModProfile != null)
					lstMissingInfo = ModRepository.GetMissingFiles(ModRepository.RemoteGameId, int.Parse(ModProfile.OnlineID));
				
				foreach(KeyValuePair<string, string> kvp in MissingModsList)
				{
					IVirtualModInfo Mod = ModProfile.ModList.Find(x => x.DownloadId == kvp.Value);

					if (Mod != null)
					{
						ProfileMissingModInfo pmModInfo = lstMissingInfo.Find(x => x.FileId.ToString().Equals(kvp.Value));
						
						if (pmModInfo != null)
						{
							if ((!string.IsNullOrEmpty(pmModInfo.NewFileId.ToString())) && (pmModInfo.NewFileId.ToString() != "-1"))
							{
								dctNewDownloadID.Add(kvp.Key, pmModInfo.NewFileId.ToString());
								dctMissingMods.Add(kvp.Key, @"@nxm://" + GameModeID + "/mods/" + Mod.ModId + "/files/" + pmModInfo.NewFileId.ToString());
							}
							else if (string.IsNullOrEmpty(pmModInfo.ModName))
								dctMissingMods.Add(kvp.Key, null);
							else if (pmModInfo.NewFileId.ToString().Equals("-1", StringComparison.OrdinalIgnoreCase))
								dctMissingMods.Add(kvp.Key, null);
						}
						else
							dctMissingMods.Add(kvp.Key, @"nxm://" + GameModeID + "/mods/" + Mod.ModId + "/files/" + Mod.DownloadId);
					}
				}


				ProfileManager.UpdateProfileDownloadId(ModProfile, dctNewDownloadID);
				
				return dctMissingMods;
			}
			catch (Exception ex)
			{
				return ("ERROR - There was a problem communicating with the Nexus server: " + Environment.NewLine + ex.Message);
			}

			return null;
		}
	}
}

