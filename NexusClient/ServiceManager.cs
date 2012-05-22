using Nexus.Client.ActivityMonitoring;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.PluginManagement;
using Nexus.Client.PluginManagement.InstallationLog;
using Nexus.Client.PluginManagement.OrderLog;
using Nexus.Client.Updating;
using Nexus.Client.ModRepositories;

namespace Nexus.Client
{
	/// <summary>
	/// Contains the services used by the manager.
	/// </summary>
	public class ServiceManager
	{
		#region Properties

		/// <summary>
		/// Gets the install log that tracks mod install info
		/// for the current game mode.
		/// </summary>
		/// <value>The install log that tracks mod install info for the current game mode.</value>
		public IInstallLog ModInstallLog { get; private set; }

		/// <summary>
		/// Gets the <see cref="ActivePluginLog"/> tracking plugin activations for the current game mode.
		/// </summary>
		/// <value>The <see cref="ActivePluginLog"/> tracking plugin activations for the current game mode.</value>
		public ActivePluginLog ActivePluginLog { get; private set; }

		/// <summary>
		/// Gets the <see cref="IPluginOrderLog"/> tracking plugin order for the current game mode.
		/// </summary>
		/// <value>The <see cref="IPluginOrderLog"/> tracking plugin order for the current game mode.</value>
		public IPluginOrderLog PluginOrderLog { get; private set; }

		/// <summary>
		/// Gets the repository we are logging in to.
		/// </summary>
		/// <value>The repository we are logging in to.</value>
		public IModRepository ModRepository { get; private set; }

		/// <summary>
		/// Gets the mod manager to use to manage mods.
		/// </summary>
		/// <value>The mod manager to use to manage mods.</value>
		public ModManager ModManager { get; private set; }

		/// <summary>
		/// Gets manager to use to manage plugins.
		/// </summary>
		/// <value>The manager to use to manage plugins.</value>
		public IPluginManager PluginManager { get; private set; }

		/// <summary>
		/// Gets the activity manager to use to manage the monitored activities.
		/// </summary>
		/// <value>The activity manager to use to manage the monitored activities.</value>
		public ActivityMonitor ActivityMonitor { get; private set; }

		/// <summary>
		/// Gets the update manager to use to perform updates.
		/// </summary>
		/// <value>The update manager to use to perform updates.</value>
		public UpdateManager UpdateManager { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simpell constructor that initializes the object with the given services.
		/// </summary>
		/// <param name="p_ilgModInstallLog">The install log that tracks mod install info for the current game mode.</param>
		/// <param name="p_aplActivePluginLog">The <see cref="ActivePluginLog"/> tracking plugin activations for the current game mode.</param>
		/// <param name="p_polPluginOrderLog">The <see cref="IPluginOrderLog"/> tracking plugin order for the current game mode.</param>
		/// <param name="p_mrpModRepository">The repository we are logging in to.</param>
		/// <param name="p_mmgModManager">The mod manager to use to manage mods.</param>
		/// <param name="p_pmgPluginManager">The manager to use to manage plugins.</param>
		/// <param name="p_amtMonitor">The activity manager to use to manage the monitored activities.</param>
		/// <param name="p_umgUpdateManager">The update manager to use to perform updates.</param>
		public ServiceManager(IInstallLog p_ilgModInstallLog, ActivePluginLog p_aplActivePluginLog, IPluginOrderLog p_polPluginOrderLog, IModRepository p_mrpModRepository, ModManager p_mmgModManager, IPluginManager p_pmgPluginManager, ActivityMonitor p_amtMonitor, UpdateManager p_umgUpdateManager)
		{
			ModInstallLog = p_ilgModInstallLog;
			ActivePluginLog = p_aplActivePluginLog;
			PluginOrderLog = p_polPluginOrderLog;
			ModRepository = p_mrpModRepository;
			ModManager = p_mmgModManager;
			PluginManager = p_pmgPluginManager;
			ActivityMonitor = p_amtMonitor;
			UpdateManager = p_umgUpdateManager;
		}

		#endregion
	}
}
