namespace Nexus.Client
{
	using System;
	using System.Diagnostics;
	using System.Linq;
	using System.Windows.Forms;
	using Nexus.Client.Games;
	using Nexus.Client.ModManagement.UI;

	/// <summary>Captures inexpensive session counts for the game catalog without loading inactive games.</summary>
	public partial class MainForm
	{
		private GameDiscoveryStatisticsCache _gameStatistics;
		private Timer _gameStatisticsTimer;
		private string _gameStatisticsModeId;
		private int? _appliedCollectionCount;
		private int? _managedCollectionCount;
		private bool _collectionStatisticsDirty;

		/// <summary>Coalesces operation notifications; snapshots wait for the native queue and profile switch to settle.</summary>
		private void ScheduleGameStatistics(bool collectionsChanged = false)
		{
			if (_gameStatisticsTimer == null || ViewModel == null || _gameStatisticsModeId == null) return;
			_collectionStatisticsDirty |= collectionsChanged;
			_gameStatisticsTimer.Stop();
			_gameStatisticsTimer.Start();
		}

		/// <summary>Refreshes counts from loaded services only. Collection metadata uses one bounded association query.</summary>
		private void CaptureGameStatistics()
		{
			if (ViewModel == null || _gameStatistics == null || _gameStatisticsModeId == null ||
				!String.Equals(ViewModel.GameMode.ModeId, _gameStatisticsModeId, StringComparison.OrdinalIgnoreCase)) return;
			if (ViewModel.IsSwitching || ViewModel.ModActivationMonitor.HasPendingNativeOperations) return;
			try
			{
				if (_collectionStatisticsDirty)
				{
					_appliedCollectionCount = null;
					_managedCollectionCount = null;
					try
					{
						int applied;
						int managed;
						if (ViewModel.CollectionManagementWorkflow != null)
						{
							ViewModel.CollectionManagementWorkflow.GetAssociationCounts(out applied, out managed);
							_appliedCollectionCount = applied;
							_managedCollectionCount = managed;
						}
					}
					catch (Exception error) when (!(error is OutOfMemoryException)) { Trace.TraceWarning("Could not summarize Collections: {0}", error.Message); }
					_collectionStatisticsDirty = false;
				}
				ModListPresentationState state = new ModListPresentationState();
				state.Attach(ViewModel.ModManagerVM);
				state.RebuildActivationState();
				var mods = ViewModel.ModManagerVM.ManagedMods.ToList();
				_gameStatistics.Set(_gameStatisticsModeId, new GameDiscoveryStatistics
				{
					InstallationPath = ViewModel.GameMode.GameModeEnvironmentInfo.InstallationPath,
					InstallInfoPath = ViewModel.GameMode.GameModeEnvironmentInfo.InstallInfoDirectory,
					ProfileName = ViewModel.ProfileManager.CurrentProfile?.Name,
					UpdatedUtc = DateTime.UtcNow,
					ManagedMods = mods.Count,
					ActiveMods = mods.Count(state.IsModActive),
					AppliedCollections = _appliedCollectionCount,
					ManagedCollections = _managedCollectionCount,
					ActivePlugins = ViewModel.UsesPlugins ? ViewModel.PluginManagerVM.ActivePlugins.Count : (int?)null,
					ManagedPlugins = ViewModel.UsesPlugins ? ViewModel.PluginManagerVM.ManagedPlugins.Count : (int?)null
				});
				_gameDiscoveryDocument.RefreshStatistics(_gameStatisticsModeId);
			}
			catch (Exception error) when (!(error is OutOfMemoryException)) { Trace.TraceWarning("Could not summarize managed game: {0}", error.Message); }
		}

		/// <summary>Flushes the latest successful snapshot before detaching the game services or closing the shell.</summary>
		private void SaveGameStatistics()
		{
			_gameStatisticsTimer?.Stop();
			CaptureGameStatistics();
			_gameStatistics?.Save();
		}
	}
}
