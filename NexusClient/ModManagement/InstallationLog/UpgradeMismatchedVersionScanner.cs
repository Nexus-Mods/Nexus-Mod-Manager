using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Mods;
using Nexus.Client.Util;

namespace Nexus.Client.ModManagement.InstallationLog
{
	/// <summary>
	/// Asks the user if the given mod whose version does not its version in the install log
	/// should be upgraded.
	/// </summary>
	/// <remarks>
	/// If the user opts to not upgrade the mod, the verison is changed in the install log, but no
	/// other action is taken.
	/// </remarks>
	/// <param name="p_modOld">The mod info in the install log.</param>
	/// <param name="p_modNew">The mod with the mismatched version.</param>
	/// <returns><c>true</c> if the mod should be upgraded;
	/// <c>false</c> otherwise.</returns>
	public delegate bool ConfirmMismatchedVersionModUpgradeDelegate(IMod p_modOld, IMod p_modNew);

	/// <summary>
	/// Checks to see if any mods' versions are different than the
	/// recorded versions in the install log. If so, the discrepancies
	/// are dealt with.
	/// </summary>
	public class UpgradeMismatchedVersionScanner : IBackgroundTaskSet
	{
		#region Events

		/// <summary>
		/// Raised when a task in the set has started.
		/// </summary>
		/// <remarks>
		/// The argument passed with the event args is the task that
		/// has been started.
		/// </remarks>
		public event EventHandler<EventArgs<IBackgroundTask>> TaskStarted = delegate { };

		/// <summary>
		/// Raised when a task set has completed.
		/// </summary>
		public event EventHandler<TaskSetCompletedEventArgs> TaskSetCompleted = delegate { };

		#endregion

		private ConfirmMismatchedVersionModUpgradeDelegate m_dlgConfirmUpgrade = delegate { return false; };
		private ConfirmItemOverwriteDelegate m_dlgOverwriteConfirmation = delegate { return OverwriteResult.YesToAll; };
		private EventWaitHandle m_ewhSetCompleted = new EventWaitHandle(false, EventResetMode.ManualReset);
		
		#region Properties

		/// <summary>
		/// Gets the install log to use to log file installations.
		/// </summary>
		/// <value>The install log to use to log file installations.</value>
		protected IInstallLog InstallLog { get; private set; }

		/// <summary>
		/// Gets the mod manager to use to manage mods.
		/// </summary>
		/// <value>The mod manager to use to manage mods.</value>
		protected ModManager ModManager { get; private set; }

		/// <summary>
		/// Gets whether the task set has completed.
		/// </summary>
		/// <value>Whether the task set has completed.</value>
		public bool IsCompleted { get; private set; }

		/// <summary>
		/// Gets whether the task set is queued.
		/// </summary>
		/// <value>Whether the task set is queued.</value>
		public bool IsQueued { get; set; }

		#endregion

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_ilgInstallLog">The install log to use to log file installations.</param>
		/// <param name="p_mmgModManager">The mod manager to use to upgrade any replaced mods.</param>
		/// <param name="p_dlgConfirmUpgrade">The delegate to call to confirm that a mismatched version mod should be upgraded.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		public UpgradeMismatchedVersionScanner(IInstallLog p_ilgInstallLog, ModManager p_mmgModManager, ConfirmMismatchedVersionModUpgradeDelegate p_dlgConfirmUpgrade, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			InstallLog = p_ilgInstallLog;
			ModManager = p_mmgModManager;
			m_dlgConfirmUpgrade = p_dlgConfirmUpgrade;
			m_dlgOverwriteConfirmation = p_dlgOverwriteConfirmationDelegate;
		}

		#region Event Raising

		/// <summary>
		/// Raises the <see cref="TaskStarted"/> event.
		/// </summary>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the task that was started.</param>
		protected virtual void OnTaskStarted(EventArgs<IBackgroundTask> e)
		{
			TaskStarted(this, e);
		}

		/// <summary>
		/// Raises the <see cref="TaskSetCompleted"/> event.
		/// </summary>
		/// <param name="e">A <see cref="TaskSetCompletedEventArgs"/> describing the task that was started.</param>
		protected virtual void OnTaskSetCompleted(TaskSetCompletedEventArgs e)
		{
			IsCompleted = true;
			m_ewhSetCompleted.Set();
			TaskSetCompleted(this, e);
		}

		#endregion

		/// <summary>
		/// Scans the mods folder for fomods that have versions that differ from their versions in the install log.
		/// </summary>
		/// <remarks>
		/// If fomods with versions that differ from those in the install log are found, the use is asked whether
		/// to replace or upgrade the fomod. Replacing the fomod merely changes the version in the install log,
		/// but makes no system changes. Upgrading the fomod performs an in-place upgrade.
		/// </remarks>
		public void Scan()
		{
			Trace.TraceInformation("Upgrading replaced Mods...");
			Trace.Indent();

			List<IMod> lstModsToUpgrade = new List<IMod>();
			List<KeyValuePair<IMod, IMod>> lstModsToReplace = new List<KeyValuePair<IMod, IMod>>();
			foreach (KeyValuePair<IMod, IMod> kvpUpgraded in InstallLog.GetMismatchedVersionMods())
			{
				Trace.WriteLine(String.Format("Scanning {0}", kvpUpgraded.Value.Filename));
				if (m_dlgConfirmUpgrade(kvpUpgraded.Key, kvpUpgraded.Value))
					lstModsToUpgrade.Add(kvpUpgraded.Value);
				else
					lstModsToReplace.Add(kvpUpgraded);
			}
			Trace.Unindent();

			Replace(lstModsToReplace);
			Upgrade(lstModsToUpgrade);

			OnTaskSetCompleted(new TaskSetCompletedEventArgs(true));
		}

		/// <summary>
		/// Upgrades the given fomods.
		/// </summary>
		/// <param name="p_lstModsToUpgrade">The list of fomods to upgrade.</param>
		private void Upgrade(object p_lstModsToUpgrade)
		{
			IList<IMod> lstModsToUpgrade = (IList<IMod>)p_lstModsToUpgrade;
			foreach (IMod modUpgrade in lstModsToUpgrade)
			{
				IBackgroundTaskSet btsReactivation = ModManager.ReactivateMod(modUpgrade, m_dlgOverwriteConfirmation);
				btsReactivation.TaskStarted += new EventHandler<EventArgs<IBackgroundTask>>(Reactivation_TaskStarted);
				btsReactivation.Wait();
			}
		}

		private void Reactivation_TaskStarted(object sender, EventArgs<IBackgroundTask> e)
		{
			OnTaskStarted(e);
		}

		/// <summary>
		/// Replaces the given fomods in the install log.
		/// </summary>
		/// <param name="p_lstModsToReplace">The list of fomods to replace.</param>
		protected void Replace(IList<KeyValuePair<IMod, IMod>> p_lstModsToReplace)
		{
			foreach (KeyValuePair<IMod, IMod> kvpReplace in p_lstModsToReplace)
				InstallLog.ReplaceActiveMod(kvpReplace.Key, kvpReplace.Value);
		}

		/// <summary>
		/// Blocks until the task set is completed.
		/// </summary>
		public void Wait()
		{
			m_ewhSetCompleted.WaitOne();
		}
	}
}
