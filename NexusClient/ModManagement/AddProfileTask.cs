using System;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModRepositories;
using Nexus.Client.UI;


namespace Nexus.Client.ModManagement
{
	public class AddProfileTask : ThreadedBackgroundTask
	{
		bool m_booCancel = false;
		bool m_booStartupMigration = false;
		string Label = string.Empty;


		#region Properties

		/// <summary>
		/// Gets the current ModManager.
		/// </summary>
		/// <value>The current ModManager.</value>
		protected ModManager ModManager { get; private set; }
		protected IEnvironmentInfo EnvironmentInfo { get; private set; }
		Uri m_Uri = null;
		protected IModRepository ModRepository { get; private set; }
		protected ProfileManager ProfileManager { get; private set; }

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
		public AddProfileTask(ModManager p_mmgModManager, IModRepository p_mrModRepository, IEnvironmentInfo p_eiEnvironmentInfo, ProfileManager p_pmProfileManager, Uri p_uUri, ConfirmActionMethod p_camConfirm, string p_strLabel)
		{
			ModManager = p_mmgModManager;
			ModRepository = p_mrModRepository;
			EnvironmentInfo = p_eiEnvironmentInfo;
			ProfileManager = p_pmProfileManager;
			m_Uri = p_uUri;
			ConfirmActionMethod = p_camConfirm;
			Label = p_strLabel;
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
			return null;
		}
	}
}
