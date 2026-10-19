namespace Nexus.Client
{
	using System;
	using System.Diagnostics;
	using System.Threading;
	using System.Threading.Tasks;
	using DevExpress.XtraBars;
	using Nexus.Client.Games;
	using Nexus.Client.Games.UI;
	using Nexus.Client.Util.Localization;

	/// <summary>Game-independent discovery and placeholder surfaces owned by MainForm.</summary>
	public partial class MainForm
	{
		private GameDiscoveryDocument _gameDiscoveryDocument;
		private GameRequiredDocument _emptyModsDocument;
		private GameRequiredDocument _emptyCategoriesDocument;
		private GameRequiredDocument _emptyCollectionsDocument;
		private GameModeRegistry _gameDiscoveryRegistry;
		private System.Collections.Generic.IReadOnlyList<GameDiscoveryInstallationCandidate> _gameDiscoveryCandidates;
		private CancellationTokenSource _gameDiscoveryCancellation;
		private CancellationTokenSource _gameDiscoveryScanCancellation;
		internal event EventHandler<GameDiscoveryGameRequestedEventArgs> ManageGameRequested;
		internal event EventHandler<GameDiscoveryGameRequestedEventArgs> ConfigureGameRequested;
		internal event EventHandler<GameDiscoveryGameRequestedEventArgs> LocateGameRequested;
		internal event EventHandler GameCatalogReady;
		internal GameModeRegistry GameDiscoveryRegistry => _gameDiscoveryRegistry;
		internal GameDiscoveryCatalog GameDiscoveryCatalog => _gameDiscoveryDocument?.Catalog;
		internal void SelectGameInDiscovery(string modeId)
		{
			_gameDiscoveryDocument.SelectMode(modeId);
			_mainTabbedView?.ActivateDocument(_gameDiscoveryDocument);
		}

		/// <summary>Refresh only the read-only projection after explicit configuration.
		/// No launcher enumeration or settings changes occur here.</summary>
		internal void ReprojectGameDiscoveryCatalog()
		{
			if (_gameDiscoveryRegistry == null || IsDisposed) return;
			_gameDiscoveryDocument.SetCatalog(GameDiscoveryCatalogBuilder.Build(
				_gameDiscoveryRegistry.RegisteredGameModes, _shellEnvironmentInfo.Settings, _gameDiscoveryCandidates));
		}
		internal bool IsFirstSessionLoading { get; set; }
		internal bool HasBoundGameViewModel => ViewModel != null;
		internal void EnableFirstSessionActions() => _gameDiscoveryDocument.EnableFirstSessionActions();
		internal void SetGameSessionReady() => _gameDiscoveryDocument.SetSessionReady();
		internal void SetGameSessionInactive() => _gameDiscoveryDocument.SetSessionInactive();

		internal void SetGameLoadStatus(string name, bool loading)
		{
			_gameDiscoveryDocument.SetSessionLoading(name, loading);
		}

		internal void SetGameLoadMessage(string message)
		{
			_gameDiscoveryDocument.SetSessionMessage(message);
		}

		private void InitializeGameDiscoveryDocument()
		{
			_gameDiscoveryDocument = new GameDiscoveryDocument();
			_gameDiscoveryDocument.RefreshRequested += async (sender, args) => await RefreshGameDiscoveryAsync();
			_gameDiscoveryDocument.ScanRequested += async (sender, args) => await ScanGameDiscoveryAsync();
			_gameDiscoveryDocument.StopScanRequested += (sender, args) => StopGameDiscoveryScan();
			_gameDiscoveryDocument.ManageRequested += (sender, args) => ManageGameRequested?.Invoke(this, args);
			_gameDiscoveryDocument.ConfigureRequested += (sender, args) => ConfigureGameRequested?.Invoke(this, args);
			_gameDiscoveryDocument.LocateRequested += (sender, args) => LocateGameRequested?.Invoke(this, args);
		}

		private void InitializeNoGameDocuments()
		{
			Action navigate = () => _mainTabbedView?.ActivateDocument(_gameDiscoveryDocument);
			_emptyModsDocument = new GameRequiredDocument(LanguageManager.Get("MainForm.Tabs.Mods", "Mods"), navigate);
			_emptyCategoriesDocument = new GameRequiredDocument(LanguageManager.Get("MainForm.Tabs.Categories", "Categories"), navigate);
			_emptyCollectionsDocument = new GameRequiredDocument(LanguageManager.Get("MainForm.Tabs.Collections", "Collections"), navigate);
		}

		private void DisableGameDependentBars()
		{
			foreach (BarItem item in new BarItem[] { spbLaunch, spbProfiles, _installMethodSelector,
				toolStripSplitButtonTools, spbFolders, tsbSettings, spbSupportedTools,
				toolStripTextBoxFind, tsbUpdate, spbChangeMode, toolStripButtonOnlineStatus,
				toolStripButtonRateLimit })
				if (item != null) item.Enabled = false;
			_installMethodLabel.Visibility = BarItemVisibility.Never;
			toolStripLabelDownloads.Visibility = BarItemVisibility.Never;
			tlbModsCounter.Visibility = BarItemVisibility.Never;
			toolStripLabelPluginsCounter.Visibility = BarItemVisibility.Never;
			toolStripLabelActivePluginsCounter.Visibility = BarItemVisibility.Never;
			tsbSkyrimDownloads.Visibility = BarItemVisibility.Never;
			toolStripLabelLoginMessage.Caption = LanguageManager.Get("GameDiscovery.SelectGame", "Select a game to manage");
		}

		/// <summary>Restore ordinary game toolbar availability before binding native commands.
		/// Per-game command bindings and view activation may disable items again.
		/// Change-game remains disabled in the opt-in shell; Game Discovery owns switching.
		/// </summary>
		private void RestoreGameDependentBars()
		{
			foreach (BarItem item in new BarItem[] { spbLaunch, spbProfiles, _installMethodSelector,
				toolStripSplitButtonTools, spbFolders, tsbSettings, spbSupportedTools,
				toolStripTextBoxFind, tsbUpdate, toolStripButtonOnlineStatus,
				toolStripButtonRateLimit })
				if (item != null) item.Enabled = true;
			_installMethodLabel.Visibility = BarItemVisibility.Always;
			toolStripLabelDownloads.Visibility = BarItemVisibility.Always;
			tlbModsCounter.Visibility = BarItemVisibility.Always;
			toolStripLabelPluginsCounter.Visibility = BarItemVisibility.Always;
			toolStripLabelActivePluginsCounter.Visibility = BarItemVisibility.Always;
		}

		/// <summary>
		/// Does not update game settings. Catalog projection runs on the UI thread after
		/// the background launcher/factory lookup has completed, so legacy settings are
		/// never read simultaneously by this task and a game-owned UI mutation.
		/// </summary>
		private async Task RefreshGameDiscoveryAsync()
		{
			if (IsDisposed || Disposing || _gameDiscoveryScanCancellation != null) return;
			_gameDiscoveryCancellation?.Cancel();
			var current = new CancellationTokenSource();
			_gameDiscoveryCancellation = current;
			_gameDiscoveryDocument.SetBusy(true);
			try
			{
				GameModeRegistry registry = _gameDiscoveryRegistry;
				if (registry == null)
					registry = await Task.Run(() => GameModeRegistry.DiscoverSupportedGameModes(_shellEnvironmentInfo), current.Token);
				current.Token.ThrowIfCancellationRequested();
				var discovered = await Task.Run(() => new QuickGameDiscovery().Find(registry.RegisteredGameModeFactories, current.Token), current.Token);
				current.Token.ThrowIfCancellationRequested();
				if (!Object.ReferenceEquals(_gameDiscoveryCancellation, current) || IsDisposed || Disposing) return;
				_gameDiscoveryRegistry = registry;
				_gameDiscoveryCandidates = GameDiscoveryCandidateMerger.Merge(_gameDiscoveryCandidates, discovered);
				ReprojectGameDiscoveryCatalog();
				_gameDiscoveryDocument.SetBusy(false);
				GameCatalogReady?.Invoke(this, EventArgs.Empty);
			}
			catch (OperationCanceledException) { }
			catch (Exception exception) when (!(exception is OutOfMemoryException))
			{
				Trace.TraceWarning("Game Discovery refresh failed: {0}", exception);
				if (Object.ReferenceEquals(_gameDiscoveryCancellation, current) && !IsDisposed && !Disposing)
					_gameDiscoveryDocument.SetError(exception.Message);
			}
			finally
			{
				if (Object.ReferenceEquals(_gameDiscoveryCancellation, current))
				{
					_gameDiscoveryCancellation = null;
					if (!IsDisposed && !Disposing) _gameDiscoveryDocument.SetRefreshFinished();
				}
				current.Dispose();
			}
		}

		/// <summary>A full filesystem scan is never started except by the Scan button.</summary>
		private async Task ScanGameDiscoveryAsync()
		{
			if (IsDisposed || Disposing || IsFirstSessionLoading || _gameDiscoveryScanCancellation != null ||
				_gameDiscoveryCancellation != null || _gameDiscoveryRegistry == null) return;

			var current = new CancellationTokenSource();
			_gameDiscoveryScanCancellation = current;
			_gameDiscoveryDocument.SetScanning(true);
			try
			{
				var progress = new Progress<string>(path =>
				{
					if (Object.ReferenceEquals(_gameDiscoveryScanCancellation, current) && !IsDisposed && !Disposing &&
						!current.IsCancellationRequested)
						_gameDiscoveryDocument.SetScanProgress(path);
				});
				GameDiscoveryScanResult result = await new GameDiscoveryRecursiveScanner().ScanAsync(
					_gameDiscoveryRegistry.RegisteredGameModes, progress, current.Token);
				if (!Object.ReferenceEquals(_gameDiscoveryScanCancellation, current) || IsDisposed || Disposing) return;
				_gameDiscoveryCandidates = GameDiscoveryCandidateMerger.Merge(_gameDiscoveryCandidates, result.Candidates);
				ReprojectGameDiscoveryCatalog();
				_gameDiscoveryDocument.SetScanResult(result.Candidates.Count, result.Cancelled);
			}
			catch (OperationCanceledException)
			{
				if (!IsDisposed && !Disposing)
					_gameDiscoveryDocument.SetScanResult(0, true);
			}
			catch (Exception exception) when (!(exception is OutOfMemoryException))
			{
				Trace.TraceWarning("Game Discovery scan failed: {0}", exception);
				if (!IsDisposed && !Disposing)
					_gameDiscoveryDocument.SetScanError(exception.Message);
			}
			finally
			{
				if (Object.ReferenceEquals(_gameDiscoveryScanCancellation, current))
				{
					_gameDiscoveryScanCancellation = null;
					if (!IsDisposed && !Disposing) _gameDiscoveryDocument.SetScanning(false);
				}
				current.Dispose();
			}
		}

		private void StopGameDiscoveryScan()
		{
			if (_gameDiscoveryScanCancellation == null || _gameDiscoveryScanCancellation.IsCancellationRequested) return;
			_gameDiscoveryDocument.SetScanStopping();
			_gameDiscoveryScanCancellation.Cancel();
		}
	}
}
