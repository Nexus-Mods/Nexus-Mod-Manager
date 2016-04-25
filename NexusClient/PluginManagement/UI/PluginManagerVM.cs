using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.BackgroundTasks.UI;
using Nexus.Client.Commands;
using Nexus.Client.Commands.Generic;
using Nexus.Client.Games;
using Nexus.Client.ModActivationMonitoring;
using Nexus.Client.ModManagement;
using Nexus.Client.Plugins;
using Nexus.Client.Settings;
using Nexus.Client.UI;
using Nexus.Client.Util.Collections;
using Nexus.Client.Util;

namespace Nexus.Client.PluginManagement.UI
{
	/// <summary>
	/// This class encapsulates the data and the operations presented by UI
	/// elements that display plugin management.
	/// </summary>
	public class PluginManagerVM
	{
		#region Events

		/// <summary>
		/// Raised when exporting the current load order fails.
		/// </summary>
		public event EventHandler<ExportFailedEventArgs> ExportFailed = delegate { };

		/// <summary>
		/// Raised when exporting the current load order succeeds.
		/// </summary>
		public event EventHandler<ExportSucceededEventArgs> ExportSucceeded = delegate { };

		/// <summary>
		/// Raised when importing a load order fails.
		/// </summary>
		public event EventHandler<ImportFailedEventArgs> ImportFailed = delegate { };

		/// <summary>
		/// Raised when importing a load order partially succeeds.
		/// </summary>
		public event EventHandler<ImportSucceededEventArgs> ImportPartiallySucceeded = delegate { };

		/// <summary>
		/// Raised when importing a load order succeeds.
		/// </summary>
		public event EventHandler<ImportSucceededEventArgs> ImportSucceeded = delegate { };

		/// <summary>
		/// Raised when switching profiles.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> PluginControlResetting = delegate { };

		/// <summary>
		/// Raised when the mods list is being updated.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> SortingPlugins = delegate { };

		/// <summary>
		/// Raised when a plugin is manually moved in the loadorder.
		/// </summary>
		public event EventHandler PluginMoved = delegate { };

		/// <summary>
		/// Managing multiple plugins.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> ManagingMultiplePlugins = delegate { };

		#endregion

		#region Delegates

		/// <summary>
		/// Called when an updater's action needs to be confirmed.
		/// </summary>
		public ConfirmActionMethod ConfirmUpdaterAction = delegate { return true; };

		#endregion

		#region Properties

		#region Commands

		/// <summary>
		/// Gets the command to activate a plugin.
		/// </summary>
		/// <remarks>
		/// The commands takes an argument describing the plugin to be activated.
		/// </remarks>
		/// <value>The command to activate a plugin.</value>
		public Command<Plugin> ActivatePluginCommand { get; private set; }

		/// <summary>
		/// Gets the command to deactivate a plugin.
		/// </summary>
		/// <remarks>
		/// The commands takes an argument describing the plugin to be deactivated.
		/// </remarks>
		/// <value>The command to deactivate a plugin.</value>
		public Command<Plugin> DeactivatePluginCommand { get; private set; }

		/// <summary>
		/// Gets the command to move plugins up in the load order.
		/// </summary>
		/// <remarks>
		/// The commands takes an argument describing the plugins to be moved. Moving up decreases
		/// the load order.
		/// </remarks>
		/// <value>The command to move plugins up in the load order.</value>
		public Command<IEnumerable<Plugin>> MoveUpCommand { get; private set; }

		/// <summary>
		/// Gets the command to move plugins down in the load order.
		/// </summary>
		/// <remarks>
		/// The commands takes an argument describing the plugins to be moved. Moving down increases
		/// the load order.
		/// </remarks>
		/// <value>The command to move plugins down in the load order.</value>
		public Command<IList<Plugin>> MoveDownCommand { get; private set; }

		/// <summary>
		/// Gets the command to export the current load order to a text file.
		/// </summary>
		/// <remarks>
		/// The command takes an argument specifying the current game mode and the filename to save the load order to.
		/// </remarks>
		/// <value>The command to export the current load order to a text file.</value>
		public Command<string> ExportLoadOrderToFileCommand { get; private set; }

		/// <summary>
		/// Gets the command to export the current load order to the clipboard.
		/// </summary>
		/// <remarks>
		/// The command takes an argument specifying the current game mode.
		/// </remarks>
		/// <value>The command to export the current load order to the clipboard.</value>
		public Command ExportLoadOrderToClipboardCommand { get; private set; }

		/// <summary>
		/// Gets the command to import a load order from a text file.
		/// </summary>
		/// <remarks>
		/// The command takes an argument specifying the current game mode and the filename to import the load order from.
		/// </remarks>
		/// <value>The command to import a load order from a text file.</value>
		public Command<string> ImportLoadOrderFromFileCommand { get; private set; }

		/// <summary>
		/// Gets the command to import a load order from the clipboard.
		/// </summary>
		/// <remarks>
		/// The command takes an argument specifying the current game mode.
		/// </remarks>
		/// <value>The command to import a load order from the clipboard.</value>
		public Command ImportLoadOrderFromClipboardCommand { get; private set; }

		#endregion

		/// <summary>
		/// Gets the plugin manager to use to manage plugins.
		/// </summary>
		/// <value>The plugin manager to use to manage plugins.</value>
		protected IPluginManager PluginManager { get; private set; }

		/// <summary>
		/// Gets the list of plugins being managed by the plugin manager.
		/// </summary>
		/// <value>The list of plugins being managed by the plugin manager.</value>
		public ReadOnlyObservableList<Plugin> ManagedPlugins
		{
			get
			{
				return PluginManager.ManagedPlugins;
			}
		}

		/// <summary>
		/// Gets the list of active plugins.
		/// </summary>
		/// <value>The list of active plugins.</value>
		public ReadOnlyObservableList<Plugin> ActivePlugins
		{
			get
			{
				return PluginManager.ActivePlugins;
			}
		}

		/// <summary>
		/// Gets the application and user settings.
		/// </summary>
		/// <value>The application and user settings.</value>
		public ISettings Settings { get; private set; }

		/// <summary>
		/// Gets the game mode currently being managed.
		/// </summary>
		/// <value>The game mode currently being managed.</value>
		protected IGameMode CurrentGameMode { get; private set; }

		/// <summary>
		/// Gets the mod activation monitor.
		/// </summary>
		/// <value>The mod activation monitor.</value>
		public ModActivationMonitor ModActivationMonitor { get; private set; }

		/// <summary>
		/// Gets the current virtual mod activator.
		/// </summary>
		/// <value>The current virtual mod activator.</value>
		protected IVirtualModActivator VirtualModActivator { get; private set; }

		/// <summary>
		/// Gets the max allowed number of active plugins.
		/// </summary>
		/// <value>The max allowed number of active plugins (0 if there's no limit).</value>
		public Int32 MaxAllowedActivePluginsCount
		{
			get
			{
				return PluginManager.MaxAllowedActivePluginsCount;
			}
		}

		/// <summary>
		/// Gets the list of critical plugin names, ordered by load order.
		/// </summary>
		/// <value>The list of critical plugin names, ordered by load order.</value>
		public string[] OrderedCriticalPluginNames
		{
			get
			{
				return CurrentGameMode.OrderedCriticalPluginNames;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_pmgPluginManager">The plugin manager to use to manage plugins.</param>
		/// <param name="p_setSettings">The application and user settings.</param>
		/// <param name="p_gmdGameMode">The game mode that is currently being managed.</param>
		/// <param name="p_mamMonitor">The mod activation monitor.</param>
		public PluginManagerVM(IPluginManager p_pmgPluginManager, ISettings p_setSettings, IGameMode p_gmdGameMode, ModActivationMonitor p_mamMonitor, IVirtualModActivator p_ivaVirtualModActivator)
		{
			PluginManager = p_pmgPluginManager;
			Settings = p_setSettings;
			CurrentGameMode = p_gmdGameMode;
			ModActivationMonitor = p_mamMonitor;
			VirtualModActivator = p_ivaVirtualModActivator;

			CurrentGameMode.LoadOrderManager.ActivePluginUpdate += new EventHandler(LoadOrderManager_ActivePluginUpdate);
			CurrentGameMode.LoadOrderManager.LoadOrderUpdate += new EventHandler(LoadOrderManager_LoadOrderUpdate);
			CurrentGameMode.LoadOrderManager.ExternalPluginAdded += new EventHandler(LoadOrderManager_ExternalPluginAdded);
			CurrentGameMode.LoadOrderManager.ExternalPluginRemoved += new EventHandler(LoadOrderManager_ExternalPluginRemoved);

			ActivatePluginCommand = new Command<Plugin>("Activate Plugin", "Activates the selected plugin.", ActivatePlugin);
			DeactivatePluginCommand = new Command<Plugin>("Deactivate Plugin", "Deactivates the selected plugin.", DeactivatePlugin);
			MoveUpCommand = new Command<IEnumerable<Plugin>>("Move Plugin Up", "Moves the plugin up in the load order.", MovePluginsUp);
			MoveDownCommand = new Command<IList<Plugin>>("Move Plugin Down", "Moves the plugin down in the load order.", MovePluginsDown);

			ExportLoadOrderToFileCommand = new Command<string>("Export to a text file", "Exports the current load order to a text file.", ExportLoadOrderToFile);
			ExportLoadOrderToClipboardCommand = new Command("Export to the clipboard", "Exports the current load order to the clipboard.", ExportLoadOrderToClipboard);
			ImportLoadOrderFromFileCommand = new Command<string>("Import from a text file", "Imports a load order from a text file", ImportLoadOrderFromFile);
			ImportLoadOrderFromClipboardCommand = new Command("Import from the clipboard", "Imports a load order from the clipboard", ImportLoadOrderFromClipboard);
		}

		#endregion

		#region External Plugin Update

		/// <summary>
		/// Handles changes to the plugin activation state made by external programs (or manually)
		/// </summary>
		private void LoadOrderManager_ActivePluginUpdate(object sender, EventArgs e)
		{
			if (ModActivationMonitor.IsInstalling)
				return;

			List<string> lstNewActiveList;
			List<string> lstActivatedPlugins = new List<string>();
			List<string> lstDisabledPlugins = new List<string>();
			List<string> lstActivePlugins;

			if (sender != null)
			{
				try
				{
					lstNewActiveList = ((string[])sender).ToList();
				}
				catch
				{
					return;
				}

				if (ActivePlugins.Count > 0)
				{
					lstActivePlugins = ActivePlugins.Where(x => x != null).Where(x => !string.IsNullOrEmpty(x.Filename)).Select(x => x.Filename).ToList();
					
					var ActivatedPlugins = lstNewActiveList.Except(lstActivePlugins, StringComparer.InvariantCultureIgnoreCase);
					if (ActivatedPlugins != null)
						lstActivatedPlugins = ActivatedPlugins.ToList();

					var DisabledPlugins = lstActivePlugins.Except(lstNewActiveList, StringComparer.InvariantCultureIgnoreCase);
					if (DisabledPlugins != null)
						lstDisabledPlugins = DisabledPlugins.ToList();
				}
				else
					lstActivatedPlugins = lstNewActiveList;

				foreach (string plugin in lstActivatedPlugins)
				{
					if (!PluginManager.IsPluginRegistered(plugin))
						PluginManager.AddPlugin(plugin);

					if (PluginManager.IsPluginRegistered(plugin))
						PluginManager.ActivatePlugin(plugin);
				}
				foreach (string plugin in lstDisabledPlugins)
				{
					if (PluginManager.IsPluginRegistered(plugin))
						PluginManager.DeactivatePlugin(plugin);
				}
			}
		}

		/// <summary>
		/// Handles changes to the plugin load order made by external programs (or manually)
		/// </summary>
		private void LoadOrderManager_LoadOrderUpdate(object sender, EventArgs e)
		{
			if (ModActivationMonitor.IsInstalling)
				return;

			if (sender != null)
				RefreshPluginSorting((string[])sender);
		}

		/// <summary>
		/// Handles new plugins added by external programs (or manually)
		/// </summary>
		private void LoadOrderManager_ExternalPluginAdded(object sender, EventArgs e)
		{
			if (ModActivationMonitor.IsInstalling)
				return;

			if (sender != null)
			{
				PluginManager.AddPlugin(sender.ToString());
			}
		}

		/// <summary>
		/// Handles plugins removed by external programs (or manually)
		/// </summary>
		private void LoadOrderManager_ExternalPluginRemoved(object sender, EventArgs e)
		{
			if (ModActivationMonitor.IsInstalling)
				return;

			if (sender != null)
			{
				if (PluginManager.IsPluginRegistered(sender.ToString()))
				{
					if (PluginManager.IsPluginActive(sender.ToString()))
						PluginManager.DeactivatePlugin(sender.ToString());
					PluginManager.RemovePlugin(sender.ToString());
				}
			}
		}

		#endregion

		#region Mod Activation/Deactivation

		/// <summary>
		/// Activates the given plugin.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin to activate.</param>
		public void ActivatePlugin(Plugin p_plgPlugin)
		{
			PluginManager.ActivatePlugin(p_plgPlugin);
		}

		/// <summary>
		/// deactivates the given plugin.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin to deactivate.</param>
		public void DeactivatePlugin(Plugin p_plgPlugin)
		{
			PluginManager.DeactivatePlugin(p_plgPlugin);
		}

		/// <summary>
		/// Determines if the active state of the given plugin can be changed.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin for which it is to be determined if the active state can be changed.</param>
		/// <returns><c>true</c> if the given plugin's active state can be changed;
		/// <c>false</c> otherwise.</returns>
		public bool CanChangeActiveState(Plugin p_plgPlugin)
		{
			return PluginManager.CanChangeActiveState(p_plgPlugin);
		}

		/// <summary>
		/// Deactivates all the enabled plugins.
		/// </summary>
		public void PluginsDisableAll()
		{
			List<Plugin> lstActivePlugins = ActivePlugins.ToList();

			foreach (string criticalPlugin in OrderedCriticalPluginNames)
			{
				Plugin plgCritical = PluginManager.GetRegisteredPlugin(criticalPlugin);
				if (plgCritical != null)
					lstActivePlugins.Remove(plgCritical);
			}

			ManagingMultiplePlugins(this, new EventArgs<IBackgroundTask>(PluginManager.ManageMultiplePluginsTask(lstActivePlugins, false, ConfirmUpdaterAction)));
		}

		/// <summary>
		/// Activates all the plugins.
		/// </summary>
		public void PluginsEnableAll()
		{
			List<Plugin> lstNotActive = new List<Plugin>();
			lstNotActive = ManagedPlugins.Except(ActivePlugins).ToList();

			ManagingMultiplePlugins(this, new EventArgs<IBackgroundTask>(PluginManager.ManageMultiplePluginsTask(lstNotActive, true, ConfirmUpdaterAction)));
		}

		#endregion

		#region Plugin Ordering

		/// <summary>
		/// Sets the load order of the specifid plugin.
		/// </summary>
		/// <param name="p_plgPlugin">The full path to the plugin file whose load order is to be set.</param>
		/// <param name="p_intNewIndex">The new load order index of the plugin.</param>
		public void SetPluginOrderIndex(Plugin p_plgPlugin, int p_intNewIndex)
		{
			PluginManager.SetPluginOrderIndex(p_plgPlugin, p_intNewIndex);
			if (PluginMoved != null)
				this.PluginMoved(p_plgPlugin, new EventArgs());
		}

		/// <summary>
		/// Determines if the specified plugin order is valid.
		/// </summary>
		/// <param name="p_lstPlugins">The plugins whose order is to be validated.</param>
		/// <returns><c>true</c> if the given plugins are in a valid order;
		/// <c>false</c> otherwise.</returns>
		public bool ValidatePluginOrder(IList<Plugin> p_lstPlugins)
		{
			return PluginManager.ValidateOrder(p_lstPlugins);
		}

		/// <summary>
		/// Moves the given plugins up in the load order.
		/// </summary>
		/// <remarks>
		/// Moving plugins up in the load order decreases the load order, meaning it is loaded sooner.
		/// </remarks>
		/// <param name="p_lstPlugins">The plugins whose load order is to be changed.</param>
		protected void MovePluginsUp(IEnumerable<Plugin> p_lstPlugins)
		{
			Int32 intIndex = -1;
			foreach (Plugin plgPlugin in p_lstPlugins)
			{
				if (intIndex == -1)
					intIndex = PluginManager.ManagedPlugins.IndexOf(plgPlugin) - 1;
				if (intIndex < 0)
					intIndex++;
				PluginManager.SetPluginOrderIndex(plgPlugin, intIndex++);
				if (PluginMoved != null)
					this.PluginMoved(plgPlugin, new EventArgs());
			}
		}

		/// <summary>
		/// Moves the given plugins down in the load order.
		/// </summary>
		/// <remarks>
		/// Moving plugins down in the load order increases the load order, meaning it is loaded later.
		/// </remarks>
		/// <param name="p_lstPlugins">The plugins whose load order is to be changed.</param>
		protected void MovePluginsDown(IList<Plugin> p_lstPlugins)
		{
			Int32 intIndex = -1;
			foreach (Plugin plgPlugin in p_lstPlugins)
			{
				if (intIndex == -1)
				{
					intIndex = PluginManager.ManagedPlugins.IndexOf(plgPlugin) + 1;
					while ((intIndex < PluginManager.ManagedPlugins.Count) && p_lstPlugins.Contains(PluginManager.ManagedPlugins[intIndex]))
						intIndex++;
				}
				if (intIndex >= PluginManager.ManagedPlugins.Count)
					intIndex--;
				PluginManager.SetPluginOrderIndex(plgPlugin, intIndex);
				if (PluginMoved != null)
					this.PluginMoved(plgPlugin, new EventArgs());
			}
		}

		/// <summary>
		/// Determines if the given plugin can be moved up in the load order.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin for which it is to be determined if the load order can be decreased.</param>
		/// <returns><c>true</c> if the given plugin's load order can be decreased;
		/// <c>false</c> otherwise.</returns>
		public bool CanMovePluginUp(Plugin p_plgPlugin)
		{
			Int32 intOldIndex = PluginManager.ManagedPlugins.IndexOf(p_plgPlugin);
			if (intOldIndex < 1)
				return false;
			List<Plugin> lstPlugins = new List<Plugin>(PluginManager.ManagedPlugins);
			lstPlugins.Swap(intOldIndex, intOldIndex - 1);
			return PluginManager.ValidateOrder(lstPlugins);
		}

		/// <summary>
		/// Determines if the given plugins can be moved down in the load order.
		/// </summary>
		/// <param name="p_lstPlugins">The plugins for which it is to be determined if the load order can be increased.</param>
		/// <returns><c>true</c> if the given plugins' load orders can be increased;
		/// <c>false</c> otherwise.</returns>
		public bool CanMovePluginsDown(IList<Plugin> p_lstPlugins)
		{
			Int32 intNewIndex = -1;
			List<Plugin> lstNewOrder = new List<Plugin>(PluginManager.ManagedPlugins);
			foreach (Plugin plgPlugin in p_lstPlugins)
			{
				if (intNewIndex == -1)
				{
					Int32 intOldIndex = PluginManager.ManagedPlugins.IndexOf(plgPlugin);
					if ((intOldIndex < 0) || (intOldIndex > PluginManager.ManagedPlugins.Count - 2))
						return false;
					intNewIndex = intOldIndex + 1;
					while ((intNewIndex < PluginManager.ManagedPlugins.Count) && p_lstPlugins.Contains(PluginManager.ManagedPlugins[intNewIndex]))
						intNewIndex++;
				}
				if (intNewIndex >= PluginManager.ManagedPlugins.Count)
					intNewIndex--;
				lstNewOrder.Remove(plgPlugin);
				lstNewOrder.Insert(intNewIndex, plgPlugin);
			}
			return PluginManager.ValidateOrder(lstNewOrder);
		}

		#endregion

		#region Load Order IO

		#region Automatic Sorting

		/// <summary>
		/// Automatically sorts the plugins.
		/// </summary>
		public void SortPlugins()
		{
			SortingPlugins(this, new EventArgs<IBackgroundTask>(PluginManager.AutoPluginSorting(ConfirmUpdaterAction)));
		}

		/// <summary>
		/// Automatically sorts the plugins.
		/// </summary>
		public void RefreshPluginSorting(string[] p_strPluginList)
		{
			Dictionary<Plugin, string> kvpSortedPlugins;

			GetRegisteredPlugins(p_strPluginList, out kvpSortedPlugins);
			if ((kvpSortedPlugins != null) && (kvpSortedPlugins.Count > 0))
				ApplyLoadOrder(kvpSortedPlugins, true);
		}

		#endregion

		#region Export

		/// <summary>
		/// Determines whether or not the load order can currently be exported for the current game mode.
		/// </summary>
		/// <returns><c>true</c> if the load order can be exported; <c>false</c> otherwise.</returns>
		public bool CanExecuteExportCommands()
		{
			return CurrentGameMode.UsesPlugins && (ManagedPlugins.Count > 0);
		}

		/// <summary>
		/// Writes the current load order and the id of the specified game mode to the specified 
		/// <see cref="TextWriter"/> and returns the number of plugins written.
		/// </summary>
		/// <param name="p_twWriter">The TextWriter to export the current load order to.</param>
		/// <returns>The number of plugins exported.</returns>
		private int ExportLoadOrder(TextWriter p_twWriter)
		{
			if (p_twWriter == null)
				throw new ArgumentNullException("p_twWriter");

			p_twWriter.WriteLine("GameMode={0}", CurrentGameMode.ModeId);
			p_twWriter.WriteLine();

			int intNumPluginsExported = 0;

			foreach (Plugin p in ManagedPlugins)
			{
				p_twWriter.WriteLine(Path.GetFileName(p.Filename) + "=" + (ActivePlugins.Contains(p) ? "1" : "0"));
				intNumPluginsExported++;
			}

			return intNumPluginsExported;
		}

		/// <summary>
		/// Writes the current load order and the id of the specified game mode to the specified 
		/// <see cref="TextWriter"/> and returns the stream.
		/// </summary>
		/// <param name="p_twWriter">The TextWriter to export the current load order to.</param>
		/// <returns>The stream.</returns>
		public byte[] ExportLoadOrder()
		{
			System.Text.StringBuilder sbLoadOrder = new System.Text.StringBuilder();
			sbLoadOrder.AppendLine("GameMode=" + CurrentGameMode.ModeId);

			foreach (Plugin p in ManagedPlugins)
			{
				sbLoadOrder.AppendLine(Path.GetFileName(p.Filename) + "=" + (ActivePlugins.Contains(p) ? "1" : "0"));
			}

			return System.Text.Encoding.UTF8.GetBytes(sbLoadOrder.ToString());
		}

		/// <summary>
		/// Exports the current load order and game mode to a text file.
		/// </summary>
		/// <param name="p_strFilename">The filename to export the load order to.</param>
		protected void ExportLoadOrderToFile(string p_strFilename)
		{
			if (string.IsNullOrEmpty(p_strFilename) || !CurrentGameMode.UsesPlugins)
				return;

			StreamWriter swWriter = null;
			try
			{
				swWriter = new StreamWriter(p_strFilename, false);

				int intExportedPluginCount = ExportLoadOrder(swWriter);

				OnExportSucceeded(p_strFilename, intExportedPluginCount);
			}
			catch (Exception ex)
			{
				OnExportFailed(p_strFilename, ex);
			}
			finally
			{
				if (swWriter != null)
					swWriter.Dispose();
			}
		}

		/// <summary>
		/// Exports the current load order to the clipboard.
		/// </summary>
		protected void ExportLoadOrderToClipboard()
		{
			if (!CurrentGameMode.UsesPlugins)
				return;

			using (StringWriter writer = new StringWriter())
			{
				try
				{
					int intExportedPluginCount = ExportLoadOrder(writer);

					System.Windows.Forms.Clipboard.SetText(writer.ToString());

					OnExportSucceeded(intExportedPluginCount);
				}
				catch (Exception ex)
				{
					OnExportFailed(ex);
				}
			}
		}

		/// <summary>
		/// Returns a default filename for the current game mode when exporting the current load order.
		/// </summary>
		/// <returns>A default filename for the current game mode when exporting the current load order.</returns>
		public string GetDefaultExportFilename()
		{
			// Get a sortable date/time string
			string strDateTimeStamp =
				DateTime.Now
				.ToString(System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.SortableDateTimePattern);

			// The SortableDateTimePattern property uses the ':' character, which can't be used in file names, so use '-' instead.
			strDateTimeStamp = strDateTimeStamp.Replace(':', '-');

			return string.Format("LoadOrder_{0}_{1}.txt", CurrentGameMode.ModeId, strDateTimeStamp);
		}

		/// <summary>
		/// Returns the filter string used when exporting the current load order.
		/// </summary>
		/// <returns>The filter string used when exporting the current load order.</returns>
		public string GetExportFilterString()
		{
			return "Text files (*.txt)|*.txt";
		}

		/// <summary>
		/// Raises the <see cref="ExportFailed"/> event.
		/// </summary>
		/// <param name="p_exError">The error that occurred.</param>
		protected void OnExportFailed(Exception p_exError)
		{
			if (ExportFailed != null)
				ExportFailed(this, new ExportFailedEventArgs(p_exError));
		}

		/// <summary>
		/// Raises the <see cref="ExportFailed"/> event.
		/// </summary>
		/// <param name="p_strFilename">The filename that the load order failed to export to.</param>
		/// <param name="p_exError">The error that occurred.</param>
		protected void OnExportFailed(string p_strFilename, Exception p_exError)
		{
			if (ExportFailed != null)
				ExportFailed(this, new ExportFailedEventArgs(p_strFilename, p_exError));
		}

		/// <summary>
		/// Raises the <see cref="ExportSucceeded"/> event.
		/// </summary>
		/// <param name="p_intExportedPluginCount">The number of plaugins that were exported.</param>
		protected void OnExportSucceeded(int p_intExportedPluginCount)
		{
			if (ExportSucceeded != null)
				ExportSucceeded(this, new ExportSucceededEventArgs(p_intExportedPluginCount));
		}

		/// <summary>
		/// Raises the <see cref="ExportSucceeded"/> event.
		/// </summary>
		/// <param name="p_strFilename">The filename that the load order was exported to.</param>
		/// <param name="p_intExportedPluginCount">The number of plaugins that were exported.</param>
		protected void OnExportSucceeded(string p_strFilename, int p_intExportedPluginCount)
		{
			if (ExportSucceeded != null)
				ExportSucceeded(this, new ExportSucceededEventArgs(p_strFilename, p_intExportedPluginCount));
		}

		#endregion

		#region Import

		/// <summary>
		/// Applies the load order specified by the given list of registered plugins
		/// </summary>
		/// <param name="p_lstRegisteredPlugins">The list of registered plugins.</param>
		/// <param name="p_booSortingOnly">Whether we just want to apply the sorting.</param>
		public void ApplyLoadOrder(Dictionary<Plugin, string> p_kvpRegisteredPlugins, bool p_booSortingOnly)
		{
			ApplyLoadOrderTask altApplyLoadOrder = new ApplyLoadOrderTask(PluginManager, p_kvpRegisteredPlugins, p_booSortingOnly);
			if (CurrentGameMode.LoadOrderManager != null)
				CurrentGameMode.LoadOrderManager.MonitorExternalTask(altApplyLoadOrder);
			else
				altApplyLoadOrder.Update();
		}

		/// <summary>
		/// Determines whether or not a load order can currently be imported for the current game mode.
		/// </summary>
		/// <returns><c>true</c> if a load order can be imported; <c>false</c> otherwise.</returns>
		public bool CanExecuteImportCommands()
		{
			return CurrentGameMode.UsesPlugins
				&& !string.IsNullOrEmpty(CurrentGameMode.PluginDirectory)
				&& (ManagedPlugins.Count > 0);
		}

		/// <summary>
		/// Returns the filter string used when importing a load order.
		/// </summary>
		/// <returns>The filter string used when importing a load order.</returns>
		public string GetImportFilterString()
		{
			return "Text files (*.txt)|*.txt";
		}

		/// <summary>
		/// Gets a list of registered plugins and unregistered plugin filenames for the specified game mode and list of plugin filenames.
		/// </summary>
		/// <param name="p_dPluginFilenames">The dictionary of plugin filenames.</param>
		/// <param name="p_lstRegisteredPlugins">The return list of registered plugins.</param>
		/// <param name="p_lstUnregisteredPlugins">The return list of unregistered plugin filenames.</param>
		/// <exception cref="InvalidImportSourceException">The value of PluginDirectory for the current game mode is empty.</exception>
		public void GetRegisteredPlugins(Dictionary<string, string> p_dctPluginFilenames, out Dictionary<Plugin, string> p_kvpRegisteredPlugins, out List<string> p_lstUnregisteredPlugins)
		{
			string strPluginDirectory = CurrentGameMode.PluginDirectory;

			if (string.IsNullOrEmpty(strPluginDirectory))
				throw new InvalidImportSourceException(string.Format("The PluginDirectory path of the specified import source game mode, {0}, is empty.", CurrentGameMode));

			p_kvpRegisteredPlugins = new Dictionary<Plugin, string>();
			p_lstUnregisteredPlugins = new List<string>();

			foreach (KeyValuePair<string, string> kvp in p_dctPluginFilenames)
			{
				string strPluginPath = Path.Combine(strPluginDirectory, kvp.Key);
				Plugin plgPlugin = PluginManager.GetRegisteredPlugin(strPluginPath);
				if (plgPlugin != null)
					p_kvpRegisteredPlugins.Add(plgPlugin, kvp.Value);
				else
					p_lstUnregisteredPlugins.Add(kvp.Key);
			}
		}

		/// <summary>
		/// Gets a list of registered plugins and unregistered plugin filenames for the specified game mode and list of plugin filenames.
		/// </summary>
		/// <param name="p_lstPluginFilenames">The dictionary of plugin filenames.</param>
		/// <param name="p_kvpOrderedPlugins">The return list of registered plugins.</param>
		/// <exception cref="InvalidImportSourceException">The value of PluginDirectory for the current game mode is empty.</exception>
		public void GetRegisteredPlugins(string[] p_lstPluginFilenames, out Dictionary<Plugin, string> p_kvpOrderedPlugins)
		{
			string strPluginDirectory = CurrentGameMode.PluginDirectory;

			if (string.IsNullOrEmpty(strPluginDirectory))
				throw new InvalidImportSourceException(string.Format("The PluginDirectory path of the specified import source game mode, {0}, is empty.", CurrentGameMode));

			p_kvpOrderedPlugins = new Dictionary<Plugin, string>();

			foreach (string filename in p_lstPluginFilenames)
			{
				string strPluginPath = Path.Combine(strPluginDirectory, filename);
				Plugin plgPlugin = PluginManager.GetRegisteredPlugin(strPluginPath);
				if (plgPlugin != null)
					p_kvpOrderedPlugins.Add(plgPlugin, "0");
			}
		}

		/// <summary>
		/// Read the load order from the specified <see cref="TextReader"/> and returns the number of
		/// plugins imported as well as the number of plugins not found.
		/// </summary>
		/// <param name="p_trReader">The TextReader to read the load order from.</param>
		/// <param name="p_intTotalPluginCount">The total number of plugins that were found in the specified import source.</param>
		/// <param name="p_intImportedCount">The number of plugins imported.</param>
		/// <param name="p_lstUnregisteredPlugins">The list of plugins that are not registered with the current <see cref="T:PluginManager"/>.</param>
		private void ImportLoadOrder(TextReader p_trReader, out int p_intTotalPluginCount, out int p_intImportedCount, out List<string> p_lstUnregisteredPlugins)
		{
			if (p_trReader == null)
				throw new ArgumentNullException("p_trReader");

			Dictionary<string, string> kvpPluginFilenames = ReadImportSource(p_trReader);
			p_intTotalPluginCount = kvpPluginFilenames.Count;

			if (p_intTotalPluginCount == 0)
			{
				p_intImportedCount = 0;
				p_lstUnregisteredPlugins = null;
				return;
			}

			Dictionary<Plugin, string> kvpRegisteredPlugins;

			GetRegisteredPlugins(kvpPluginFilenames, out kvpRegisteredPlugins, out p_lstUnregisteredPlugins);

			if (kvpRegisteredPlugins.Count == 0)
			{
				p_intImportedCount = 0;
				return;
			}

			ApplyLoadOrder(kvpRegisteredPlugins, false);

			p_intImportedCount = kvpRegisteredPlugins.Count;
		}

		/// <summary>
		/// Read the load order from the specified <see cref="Dictionary<string, string>"/> and returns the number of
		/// plugins imported as well as the number of plugins not found.
		/// </summary>
		/// <param name="p_dicDictionary">The Dictionary to read the load order from.</param>
		/// <param name="p_intTotalPluginCount">The total number of plugins that were found in the specified import source.</param>
		/// <param name="p_intImportedCount">The number of plugins imported.</param>
		/// <param name="p_lstUnregisteredPlugins">The list of plugins that are not registered with the current <see cref="T:PluginManager"/>.</param>
		private void ImportLoadOrder(Dictionary<string, string> p_dicDictionary, out int p_intTotalPluginCount, out int p_intImportedCount, out List<string> p_lstUnregisteredPlugins)
		{
			p_intTotalPluginCount = p_dicDictionary.Count;

			if (p_intTotalPluginCount == 0)
			{
				p_intImportedCount = 0;
				p_lstUnregisteredPlugins = null;
				return;
			}

			Dictionary<Plugin, string> kvpRegisteredPlugins;

			GetRegisteredPlugins(p_dicDictionary, out kvpRegisteredPlugins, out p_lstUnregisteredPlugins);

			if (kvpRegisteredPlugins.Count == 0)
			{
				p_intImportedCount = 0;
				return;
			}

			ApplyLoadOrder(kvpRegisteredPlugins, false);

			p_intImportedCount = kvpRegisteredPlugins.Count;
		}

		/// <summary>
		/// Imports a load order from the specified file.
		/// </summary>
		/// <param name="p_strFilename">The filename to import a load order from.</param>
		protected void ImportLoadOrderFromFile(string p_strFilename)
		{
			if (string.IsNullOrEmpty(p_strFilename) || !CurrentGameMode.UsesPlugins)
				return;

			using (StreamReader strReader = new StreamReader(p_strFilename))
			{
				try
				{
					int intTotalPluginCount;
					int intImportedPluginCount;
					List<string> lstPluginsNotImported;

					ImportLoadOrder(strReader, out intTotalPluginCount, out intImportedPluginCount, out lstPluginsNotImported);

					if (intTotalPluginCount == 0)
						OnImportFailed(p_strFilename, "No plugins were found in the specified source file.");
					else if (intImportedPluginCount == intTotalPluginCount)
						OnImportSucceeded(p_strFilename, intTotalPluginCount, intImportedPluginCount, lstPluginsNotImported);
					else if (lstPluginsNotImported.Count == intTotalPluginCount)
						OnImportFailed(p_strFilename, "None of the plugins found in the specified source file were recognized as managed plugins.");
					else
						OnImportPartiallySucceeded(p_strFilename, intTotalPluginCount, intImportedPluginCount, lstPluginsNotImported);
				}
				catch (Exception ex)
				{
					OnImportFailed(p_strFilename, ex);
				}
			}
		}

		/// <summary>
		/// Imports a load order from the specified stream.
		/// </summary>
		/// <param name="p_strStream">The stream to import a load order from.</param>
		public void ImportLoadOrderFromString(string p_strLoadOrder)
		{
			if (String.IsNullOrEmpty(p_strLoadOrder) || !CurrentGameMode.UsesPlugins)
				return;

			using (StringReader strReader = new StringReader(p_strLoadOrder))
			{
				try
				{
					int intTotalPluginCount;
					int intImportedPluginCount;
					List<string> lstPluginsNotImported;

					ImportLoadOrder(strReader, out intTotalPluginCount, out intImportedPluginCount, out lstPluginsNotImported);
				}
				catch (Exception ex)
				{
					OnImportFailed("Profile Manager", ex);
				}
			}
		}

		/// <summary>
		/// Imports a load order from the specified Dictionary.
		/// </summary>
		/// <param name="p_dicDictionary">The Dictionary to import a load order from.</param>
		public void ImportLoadOrderFromDictionary(Dictionary<string, string> p_dicDictionary)
		{
			if ((p_dicDictionary == null) || !(p_dicDictionary.Count > 0) || !CurrentGameMode.UsesPlugins)
				return;

			try
			{
				int intTotalPluginCount;
				int intImportedPluginCount;
				List<string> lstPluginsNotImported;

				ImportLoadOrder(p_dicDictionary, out intTotalPluginCount, out intImportedPluginCount, out lstPluginsNotImported);
			}
			catch (Exception ex)
			{
				OnImportFailed("Profile Manager", ex);
			}
		}

		/// <summary>
		/// Imports a load order from the clipboard.
		/// </summary>
		protected void ImportLoadOrderFromClipboard()
		{
			if (!CurrentGameMode.UsesPlugins)
				return;

			if (!System.Windows.Forms.Clipboard.ContainsText())
			{
				OnImportFailed(null, "The clipboard does not contain any text.");
				return;
			}

			string strClipboardText = System.Windows.Forms.Clipboard.GetText();
			using (StringReader strReader = new StringReader(strClipboardText))
			{
				try
				{
					int intTotalPluginCount;
					int intImportedPluginCount;
					List<string> lstPluginsNotImported;

					ImportLoadOrder(strReader, out intTotalPluginCount, out intImportedPluginCount, out lstPluginsNotImported);

					if (intTotalPluginCount == 0)
						OnImportFailed("No plugins were found on the clipboard.");
					else if (intImportedPluginCount == intTotalPluginCount)
						OnImportSucceeded(intTotalPluginCount, intImportedPluginCount, lstPluginsNotImported);
					else if (lstPluginsNotImported.Count == intTotalPluginCount)
						OnImportFailed("None of the plugins found on the clipboard were recognized as managed plugins.");
					else
						OnImportPartiallySucceeded(intTotalPluginCount, intImportedPluginCount, lstPluginsNotImported);
				}
				catch (Exception ex)
				{
					OnImportFailed(ex);
				}
			}
		}

		/// <summary>
		/// Raises the <see cref="ImportFailed"/> event.
		/// </summary>
		/// <param name="p_exError">The error that occurred.</param>
		protected void OnImportFailed(Exception p_exError)
		{
			if (ImportFailed != null)
				ImportFailed(this, new ImportFailedEventArgs(p_exError));
		}

		/// <summary>
		/// Raises the <see cref="ImportFailed"/> event.
		/// </summary>
		/// <param name="p_strMessage">A message describing why the import failed.</param>
		protected void OnImportFailed(string p_strMessage)
		{
			if (ImportFailed != null)
				ImportFailed(this, new ImportFailedEventArgs(p_strMessage));
		}

		/// <summary>
		/// Raises the <see cref="ImportFailed"/> event.
		/// </summary>
		/// <param name="p_strFilename">The filename that the load order failed to import from.</param>
		/// <param name="p_exError">The error that occurred.</param>
		protected void OnImportFailed(string p_strFilename, Exception p_exError)
		{
			if (ImportFailed != null)
				ImportFailed(this, new ImportFailedEventArgs(p_strFilename, p_exError));
		}

		/// <summary>
		/// Raises the <see cref="ImportFailed"/> event.
		/// </summary>
		/// <param name="p_strFilename">The filename that the load order failed to import from.</param>
		/// <param name="p_strMessage">A message describing why the import failed.</param>
		protected void OnImportFailed(string p_strFilename, string p_strMessage)
		{
			if (ImportFailed != null)
				ImportFailed(this, new ImportFailedEventArgs(p_strFilename, p_strMessage));
		}

		/// <summary>
		/// Raises the <see cref="ImportPartiallySucceeded"/> event.
		/// </summary>
		/// <param name="p_intTotalPluginCount">The total number of plugins that were found in the specified import source.</param>
		/// <param name="p_intImportedPluginCount">The number of plugins that were imported.</param>
		/// <param name="p_lstPluginsNotImported">The list of plugins that were not found in the <see cref="ManagedPlugins"/> collection.</param>
		protected void OnImportPartiallySucceeded(int p_intTotalPluginCount, int p_intImportedPluginCount, List<string> p_lstPluginsNotImported)
		{
			if (ImportPartiallySucceeded != null)
				ImportPartiallySucceeded(this, new ImportSucceededEventArgs(true, p_intTotalPluginCount, p_intImportedPluginCount, p_lstPluginsNotImported));
		}

		/// <summary>
		/// Raises the <see cref="ImportPartiallySucceeded"/> event.
		/// </summary>
		/// <param name="p_strFilename">The filename that the load order was imported from.</param>
		/// <param name="p_intTotalPluginCount">The total number of plugins that were found in the specified import source.</param>
		/// <param name="p_intImportedPluginCount">The number of plugins that were imported.</param>
		/// <param name="p_lstPluginsNotImported">The list of plugins that were not found in the <see cref="ManagedPlugins"/> collection.</param>
		protected void OnImportPartiallySucceeded(string p_strFilename, int p_intTotalPluginCount, int p_intImportedPluginCount, List<string> p_lstPluginsNotImported)
		{
			if (ImportPartiallySucceeded != null)
				ImportPartiallySucceeded(this, new ImportSucceededEventArgs(p_strFilename, true, p_intTotalPluginCount, p_intImportedPluginCount, p_lstPluginsNotImported));
		}

		/// <summary>
		/// Raises the <see cref="ImportSucceeded"/> event.
		/// </summary>
		/// <param name="p_intTotalPluginCount">The total number of plugins that were found in the specified import source.</param>
		/// <param name="p_intImportedPluginCount">The number of plugins that were imported.</param>
		/// <param name="p_lstPluginsNotImported">The list of plugins that were not found in the <see cref="ManagedPlugins"/> collection.</param>
		protected void OnImportSucceeded(int p_intTotalPluginCount, int p_intImportedPluginCount, List<string> p_lstPluginsNotImported)
		{
			if (ImportSucceeded != null)
				ImportSucceeded(this, new ImportSucceededEventArgs(p_intTotalPluginCount, p_intImportedPluginCount, p_lstPluginsNotImported));
		}

		/// <summary>
		/// Raises the <see cref="ImportSucceeded"/> event.
		/// </summary>
		/// <param name="p_strFilename">The filename that the load order was imported from.</param>
		/// <param name="p_intTotalPluginCount">The total number of plugins that were found in the specified import source.</param>
		/// <param name="p_intImportedPluginCount">The number of plugins that were imported.</param>
		/// <param name="p_lstPluginsNotImported">The list of plugins that were not found in the <see cref="ManagedPlugins"/> collection.</param>
		protected void OnImportSucceeded(string p_strFilename, int p_intTotalPluginCount, int p_intImportedPluginCount, List<string> p_lstPluginsNotImported)
		{
			if (ImportSucceeded != null)
				ImportSucceeded(this, new ImportSucceededEventArgs(p_strFilename, p_intTotalPluginCount, p_intImportedPluginCount, p_lstPluginsNotImported));
		}

		/// <summary>
		/// Reads the list of plugin filenames from the specified import source, making sure it is for the current game mode.
		/// </summary>
		/// <param name="p_trReader">The import source.</param>
		/// <exception cref="InvalidImportSourceException">A game mode is not defined in the specified import source.</exception>
		/// <exception cref="InvalidImportSourceException">More than one game mode is defined in the specified import source.</exception>
		/// <exception cref="InvalidImportSourceException">The game mode if defined in the specified import source does not match the id for the current game mode.</exception>
		private Dictionary<string, string> ReadImportSource(TextReader p_trReader)
		{
			Dictionary<string, string> kvpLoadOrder = new Dictionary<string, string>();

			string strLine;
			string strGameModeId = null;
			while ((strLine = p_trReader.ReadLine()) != null)
			{
				Match mchGameModeID = Regex.Match(strLine, @"^GameMode=(?<GameModeId>\w+)$");
				if (mchGameModeID.Success)
				{
					if (strGameModeId == null)
					{
						strGameModeId = mchGameModeID.Groups["GameModeId"].Value;
						continue;
					}
					else
						throw new InvalidImportSourceException("The specified import source has more than one game mode defined.");
				}

				Match mchPlugin = Regex.Match(strLine, @"^(.+\.es(?:p|m))=([0:1])$");
				if (mchPlugin.Success)
					kvpLoadOrder.Add(strLine.Split('=')[0], strLine.Split('=')[1]);
				else
				{
					mchPlugin = Regex.Match(strLine, @"^(.+\.es(?:p|m))$");
					if (mchPlugin.Success)
						kvpLoadOrder.Add(strLine, String.Empty);
				}

			}

			if (string.IsNullOrEmpty(strGameModeId))
				throw new InvalidImportSourceException("The specified import source does not include a game mode.");
			else if (strGameModeId != CurrentGameMode.ModeId)
				throw new InvalidImportSourceException(string.Format("The game mode of the specified import source, {0}, does not match the current game mode, {1}.", strGameModeId, CurrentGameMode.ModeId));

			return kvpLoadOrder;
		}

		#endregion

		#endregion

		/// <summary>
		/// Gets the plugin description.
		/// </summary>
		public string GetPluginDescription(string p_strPlugin)
		{
			return PluginManager.GetPluginDescription(p_strPlugin);
		}

		public List<Plugin> GetOrphanedPlugins(string p_strMasterName)
		{
			return PluginManager.GetOrphanedPlugins(p_strMasterName);
		}

		public string GetPluginOwner(Plugin p_plgPlugin)
		{
			return VirtualModActivator.GetCurrentFileOwner(p_plgPlugin.Filename);
		}

		public bool PluginExists(string p_strPlugin)
		{
			return PluginManager.IsPluginRegistered(p_strPlugin);
		}

		public bool PluginIsActive(string p_strPlugin)
		{
			return PluginManager.IsPluginActive(p_strPlugin);
		}
	}
}
