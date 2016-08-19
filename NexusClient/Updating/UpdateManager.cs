using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.UI;
using System;

namespace Nexus.Client.Updating
{
	/// <summary>
	/// Manages the programme's updaters.
	/// </summary>
	public class UpdateManager
	{
		#region Properties

		/// <summary>
		/// Gets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		protected IEnvironmentInfo EnvironmentInfo { get; private set; }

		/// <summary>
		/// Gets the current game mode.
		/// </summary>
		/// <value>The current game mode.</value>
		protected IGameMode GameMode { get; private set; }

		#endregion

		#region event

		public event EventHandler BackupRequest = delegate { };

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public UpdateManager(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo)
		{
			GameMode = p_gmdGameMode;
			EnvironmentInfo = p_eifEnvironmentInfo;
		}

		#endregion

		/// <summary>
		/// Runs the managed updaters.
		/// </summary>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <param name="p_booIsAutoCheck">Whether the check is automatic or user requested.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public IBackgroundTask Update(ConfirmActionMethod p_camConfirm, bool p_booIsAutoCheck)
		{
			UpdateTask utkUpdaters = new UpdateTask(this, GameMode, EnvironmentInfo, p_booIsAutoCheck);
			utkUpdaters.Update(p_camConfirm);
			return utkUpdaters;
		}

		public void CreateBackup()
		{
			BackupRequest(this, new EventArgs());
		}
	}
}
