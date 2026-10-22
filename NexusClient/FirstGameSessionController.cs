namespace Nexus.Client
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.Linq;
	using System.Threading;
	using System.Threading.Tasks;
	using System.Windows.Forms;
	using DevExpress.XtraEditors;
	using Nexus.Client.BackgroundTasks;
	using InitializationTaskStatus = Nexus.Client.BackgroundTasks.TaskStatus;
	using Nexus.Client.Games;
	using Nexus.Client.Games.UI;
	using Nexus.Client.ModManagement.UI;
	using Nexus.Client.Settings;
	using Nexus.Client.CollectionManagement;
	using Nexus.Client.Util;
	using Nexus.Client.Util.Localization;
	using Nexus.Client.UI;

	/// <summary>
	/// Opt-in persistent-shell game-session controller. One native game at a time;
	/// managed game changes are serialized and fail closed while target work is active.
	/// Uses existing ApplicationInitializer and ALL of its user-prompt callbacks.
	/// </summary>
	internal sealed class FirstGameSessionController : IDisposable
	{
		private readonly Bootstrapper _bootstrapper;
		private readonly MainForm _shell;
		private readonly EnvironmentInfo _environment;
		private readonly NexusFontSetResolver _fonts;
		private readonly List<string> _deletedDll;
		private readonly GameSessionEntryGate _gate = new GameSessionEntryGate();
		private readonly GameDiscoveryStartupIntent _startupIntent;
		private Uri _pendingNxm;
		private bool _startupSelectionAttempted;
		private bool _disposed;
		private bool _teardownFailed;
		private bool _startupLinkForwarded;
		private Mutex _modeMutex;
		private bool _ownsModeMutex;
		private ServiceManager _services;
		private IGameMode _gameMode;
		private IMessager _listener;

		internal FirstGameSessionController(Bootstrapper bootstrapper, MainForm shell,
			EnvironmentInfo environment, NexusFontSetResolver fonts, List<string> deletedDll, GameDiscoveryStartupIntent startupIntent = null)
		{
			_bootstrapper = bootstrapper ?? throw new ArgumentNullException(nameof(bootstrapper));
			_shell = shell ?? throw new ArgumentNullException(nameof(shell));
			_environment = environment ?? throw new ArgumentNullException(nameof(environment));
			_fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));
			_deletedDll = deletedDll;
			_startupIntent = startupIntent ?? GameDiscoveryStartupIntent.Parse(null, null);
			_pendingNxm = _startupIntent.NxmLink;
			_shell.ConfigureGameRequested += OnConfigureRequested;
			_shell.LocateGameRequested += OnLocateRequested;
			_shell.ManageGameRequested += OnManageRequested;
			_shell.StartupDefaultGameRequested += OnStartupDefaultRequested;
			_shell.GameCatalogReady += OnCatalogReady;
			_shell.EnableFirstSessionActions();
		}

		private void OnStartupDefaultRequested(object sender, GameDiscoveryGameRequestedEventArgs e)
		{
			if (_disposed || _teardownFailed || _shell.IsFirstSessionLoading) return;
			var entry = _shell.GameDiscoveryCatalog?.Entries.FirstOrDefault(item =>
				String.Equals(item.ModeId, e.ModeId, StringComparison.OrdinalIgnoreCase));
			bool oldRemember = _environment.Settings.RememberGameMode;
			string oldMode = _environment.Settings.RememberedGameMode;
			if (!GameDiscoveryStartupPreference.Toggle(_environment.Settings, entry)) return;
			try
			{
				_environment.Settings.Save();
			}
			catch (Exception exception) when (!(exception is OutOfMemoryException))
			{
				_environment.Settings.RememberGameMode = oldRemember;
				_environment.Settings.RememberedGameMode = oldMode;
				Trace.TraceWarning("Could not save startup game preference: {0}", exception);
				_shell.SetGameLoadMessage(LanguageManager.Get("GameDiscovery.StartupDefaultSaveFailed",
					"Could not save the startup game preference."));
				return;
			}
			_shell.ReprojectGameDiscoveryCatalog();
			_shell.SetGameLoadMessage(LanguageManager.Get(
				_environment.Settings.RememberGameMode ? "GameDiscovery.StartupDefaultSaved" : "GameDiscovery.StartupDefaultCleared",
				_environment.Settings.RememberGameMode ? "Startup game saved." : "No startup game selected."));
		}

		private async void OnManageRequested(object sender, GameDiscoveryGameRequestedEventArgs e)
		{
			await LoadFirstGameAsync(e.ModeId);
		}

		private async void OnCatalogReady(object sender, EventArgs e)
		{
			if (_startupSelectionAttempted || _disposed) return;
			_startupSelectionAttempted = true;
			GameDiscoveryCatalog catalog = _shell.GameDiscoveryCatalog;
			if (catalog == null) return;
			string selected = _startupIntent.RequestedModeId;
			if (_pendingNxm != null)
			{
				// Resolve through the same Nexus-game domain map used by the live listener.
				// Witcher uses its separate download preference; other shared catalogs retain explicit destination selection.
				var destinations = NexusGameDomain.GetDestinations(_pendingNxm.Host,
					_shell.GameDiscoveryRegistry.RegisteredGameModes);
				bool witcherDestination = NexusGameDomain.IsWitcher3DownloadDomain(_pendingNxm.Host) && NexusGameDomain.IsWitcher3Mode(selected);
				// Legacy game modes do not opt into explicit Nexus-domain routing.
				// Preserve their existing -game/NXM alias selection in that case.
				if (destinations.Count == 0 && String.IsNullOrWhiteSpace(selected))
				{
					_shell.SetGameLoadMessage(LanguageManager.Get("GameDiscovery.UnsupportedLink",
						"No supported game corresponds to this Nexus link."));
					return;
				}
				if (destinations.Count == 1 && !witcherDestination)
					selected = destinations[0].ModeId;
				else if (destinations.Count > 1 && !witcherDestination && !destinations.Any(d => String.Equals(d.ModeId, selected, StringComparison.OrdinalIgnoreCase)))
				{
					_shell.SetGameLoadMessage(LanguageManager.Get("GameDiscovery.ChooseLinkDestination",
						"Select a compatible game for the incoming Nexus link. The link has not been downloaded."));
					return;
				}
			}
			if (String.IsNullOrWhiteSpace(selected) && _environment.Settings.RememberGameMode)
				selected = _environment.Settings.RememberedGameMode;
			if (String.IsNullOrWhiteSpace(selected)) return;
			var entry = catalog.Entries.FirstOrDefault(item =>
				String.Equals(item.ModeId, selected, StringComparison.OrdinalIgnoreCase));
			if (entry == null)
			{
				_shell.SetGameLoadMessage(LanguageManager.Get("GameDiscovery.StartupNotSupported",
					"The requested/default game is no longer supported. Choose a game from the catalog."));
				return;
			}
			_shell.SelectGameInDiscovery(entry.ModeId);
			if (GameSessionEntryGate.CanManage(entry))
			{
				await LoadFirstGameAsync(entry.ModeId);
				// Another process may have finished startup after the pre-window handoff probe.
				if (_startupLinkForwarded && !_disposed && !_shell.IsDisposed) _shell.Close();
			}
			else
				_shell.SetGameLoadMessage(LanguageManager.Format("GameDiscovery.StartupNeedsSetup",
					"{0} needs configuration or review before it can be managed.", entry.Name));
		}

		private void OnLocateRequested(object sender, GameDiscoveryGameRequestedEventArgs e)
		{
			if (_disposed || _teardownFailed || _shell.IsFirstSessionLoading) return;
			var factory = _shell.GameDiscoveryRegistry?.GetGameMode(e.ModeId);
			if (factory == null) return;
			using (var chooser = new XtraFolderBrowserDialog())
			{
				chooser.Description = LanguageManager.Format("GameDiscovery.LocatePrompt",
					"Select the installation folder for {0}.", factory.GameModeDescriptor.Name);
				if (chooser.ShowDialog(_shell) != DialogResult.OK) return;
				if (!QuickGameDiscovery.IsVerifiedGameRoot(factory.GameModeDescriptor, chooser.SelectedPath))
				{
					_shell.SetGameLoadMessage(LanguageManager.Get("GameDiscovery.InvalidLocation",
						"The selected folder is not a valid installation of this game/edition."));
					return;
				}
				ConfigureGame(e.ModeId, chooser.SelectedPath);
			}
		}

		private void OnConfigureRequested(object sender, GameDiscoveryGameRequestedEventArgs e)
		{
			ConfigureGame(e.ModeId, e.CandidatePath);
		}

		private void ConfigureGame(string modeId, string candidatePath)
		{
			if (_disposed || _teardownFailed || _shell.IsFirstSessionLoading ||
				String.IsNullOrWhiteSpace(candidatePath)) return;
			var factory = _shell.GameDiscoveryRegistry?.GetGameMode(modeId);
			if (factory == null) return;
			// Reconfiguring the *active* game requires detaching its native services.
			// A different game is an independent storage target and does not.
			if (_gate.State == GameSessionEntryGate.EntryState.Ready &&
				String.Equals(_gate.ModeId, modeId, StringComparison.OrdinalIgnoreCase))
			{
				_shell.SetGameLoadMessage(LanguageManager.Get("GameDiscovery.ActiveStorageBlocked",
					"Storage for the active game cannot be changed until you switch away from it."));
				return;
			}
			// This is a temporary target-game lock, NOT the mutex owned by the active
			// session. Do not call TryAcquireModeMutex/ReleaseMutex here.
			bool ownsSetupMutex;
			using (var setupMutex = new Mutex(true,
				String.Format("{0}-{1}-GameModeMutex", CommonData.ModManagerName, modeId),
				out ownsSetupMutex))
			{
				if (!ownsSetupMutex)
				{
					_shell.SetGameLoadMessage(LanguageManager.Get("GameDiscovery.GameAlreadyRunning",
						"This game is already managed by another NMM process."));
					return;
				}
				try
				{
					string result;
					bool configured = new GameDiscoveryConfigurationService(_environment)
						.Configure(_shell, factory, candidatePath, out result, () =>
						{
							CollectionTargetMutationLease reservation;
							return CollectionTargetMutationLeaseManager.Shared.TryAcquireIdleSessionTransition(out reservation)
								? reservation : null;
						});
					if (configured)
					{
						_shell.ReprojectGameDiscoveryCatalog();
						_shell.SelectGameInDiscovery(modeId);
					}
					if (!String.IsNullOrWhiteSpace(result)) _shell.SetGameLoadMessage(result);
				}
				catch (Exception exception) when (!(exception is OutOfMemoryException))
				{
					Trace.TraceError("Game configuration failed: {0}", exception);
					_shell.SetGameLoadMessage(LanguageManager.Get("GameDiscovery.SetupFailed",
						"Game Storage could not be configured.") + " " + exception.Message);
				}
				finally { setupMutex.ReleaseMutex(); }
			}
		}

		private async Task LoadFirstGameAsync(string modeId)
		{
			if (_disposed || _teardownFailed || _shell.IsDisposed || _shell.IsFirstSessionLoading) return;
			GameDiscoveryCatalog catalog = _shell.GameDiscoveryCatalog;
			var entry = catalog?.Entries.FirstOrDefault(item =>
				String.Equals(item.ModeId, modeId, StringComparison.OrdinalIgnoreCase));
			if (!GameSessionEntryGate.CanManage(entry)) return;
			if (_gate.State == GameSessionEntryGate.EntryState.Ready &&
				String.Equals(_gate.ModeId, entry.ModeId, StringComparison.OrdinalIgnoreCase))
			{
				_shell.SetGameLoadMessage(LanguageManager.Format("GameDiscovery.GameReady", "Managing {0}", entry.Name));
				return;
			}
			if (_gate.State == GameSessionEntryGate.EntryState.Ready && !TryLeaveCurrentGame()) return;
			if (!_gate.TryBegin(entry)) return;
			var registry = _shell.GameDiscoveryRegistry;
			var factory = registry?.GetGameMode(entry.ModeId);
			if (factory == null)
			{
				_gate.Finish(entry.ModeId, false);
				return;
			}

			_shell.IsFirstSessionLoading = true;
			_shell.SetGameLoadStatus(entry.Name, true);
			ApplicationInitializer initializer = null;
			try
			{
				if (!TryAcquireModeMutex(factory.GameModeDescriptor))
				{
					_gate.Finish(entry.ModeId, false);
					_shell.SetGameLoadMessage(LanguageManager.Get("GameDiscovery.GameAlreadyRunning",
						"This game is already managed by another NMM process."));
					return;
				}

				// A non-modal owned progress window retains legacy ShowView/ShowMessage,
				// login, writability and overwrite delegates. Game Storage remains an
				// explicitly owned modal configuration/recovery prompt when requested.
				do
				{
					initializer = new ApplicationInitializer(_environment, _fonts, _deletedDll);
					_shell.SetGameLoadProgress(0, 18, LanguageManager.Get("GameDiscovery.Phase.Settings", "Preparing game settings"));
					await InitializeWithOwnedPresenterAsync(initializer, factory);
					if (initializer.Status == InitializationTaskStatus.Retrying)
						_bootstrapper.ReleaseSessionServices(initializer.Services);
				} while (initializer.Status == InitializationTaskStatus.Retrying);

				if (initializer.Status != InitializationTaskStatus.Complete)
				{
					bool setupCancelled = initializer.GameStorageSetupWasCancelled;
					_bootstrapper.ReleaseSessionServices(initializer.Services);
					initializer = null;
					_gate.Finish(entry.ModeId, false);
					_shell.SetGameLoadMessage(setupCancelled
						? LanguageManager.Get("GameDiscovery.ConfigurationCancelled", "Game Storage configuration was cancelled.")
						: LanguageManager.Get("GameDiscovery.LoadFailed", "Game initialization failed. Select the game to retry."));
					return;
				}

				_services = initializer.Services;
				_gameMode = initializer.GameMode;
				var installed = BuildManagedRegistry(registry, catalog);
				// Read/migrate category XML on a worker before binding the Mods UI.
				// Profile-dependent archive category remapping stays in ModManagerVM,
				// after ProfileManager has been constructed on the UI/native thread.
				_shell.SetGameLoadProgress(initializer.OverallProgress, initializer.OverallProgressMaximum + 2,
					LanguageManager.Get("GameDiscovery.LoadingCategories", "Preparing mod categories..."));
				ModManagerVM.PreparedModCategories preparedCategories = await Task.Run(
					() => ModManagerVM.PrepareCategories(_services.ModManager));
				_shell.SetGameLoadProgress(initializer.OverallProgress + 1, initializer.OverallProgressMaximum + 2,
					LanguageManager.Get("GameDiscovery.Phase.OpeningUI", "Building the game interface"));
				MainFormVM viewModel = _bootstrapper.CreateSessionViewModel(initializer, installed, preparedCategories);
				_shell.AttachFirstGame(viewModel);
				_shell.SetGameLoadProgress(initializer.OverallProgress + 2, initializer.OverallProgressMaximum + 2,
					LanguageManager.Get("GameDiscovery.Phase.Ready", "Game interface ready"));
				_gate.Finish(entry.ModeId, true);

				// The listener is owned by this session, not by MainForm or a process singleton.
				try
				{
					_listener = MessagerServer.InitializeListener(_environment, _gameMode, _services.ModManager,
						_shell, viewModel.CollectionNxmDispatcher);
				}
				catch (Exception exception)
				{
					Trace.TraceWarning("Game session opened, but IPC listener could not start: {0}", exception);
				}

				_shell.SetGameLoadMessage(LanguageManager.Format("GameDiscovery.GameReady", "Managing {0}", entry.Name));
				_shell.SetActiveManagedGame(entry.ModeId);
				_shell.SetGameSessionReady();
				await _shell.NotifyFirstGameReadyAsync();
				DeliverPendingNxmIfMatching();
			}
			catch (Exception exception) when (!(exception is OutOfMemoryException))
			{
				Trace.TraceError("First game session entry failed: {0}", exception);
				// Once a VM is bound, native services cannot be released while its controls
				// may still contain handles. Keep the shell open for a clean process exit.
				if (_shell.HasBoundGameViewModel)
				{
					// A partial UI attach is not a valid source for another game transition.
					_teardownFailed = true;
					if (_gate.State == GameSessionEntryGate.EntryState.Loading)
						_gate.Finish(entry.ModeId, true);
					_shell.SetGameSessionReady();
					_shell.SetGameLoadMessage(LanguageManager.Get("GameDiscovery.PartialEntry",
						"The game was loaded but the interface reported an error. Close NMM before retrying."));
				}
				else
				{
					if (_gate.State == GameSessionEntryGate.EntryState.Loading)
						_gate.Finish(entry.ModeId, false);
					_shell.SetGameLoadMessage(exception.Message);
				}
			}
			finally
			{
				_shell.IsFirstSessionLoading = false;
				_shell.SetGameLoadStatus(entry.Name, false);
				if (_gate.State != GameSessionEntryGate.EntryState.Ready)
				{
					if (_services == null && initializer != null)
						_bootstrapper.ReleaseSessionServices(initializer.Services);
					ReleaseNativeSession();
				}
			}
		}

		/// <summary>Leave one completely idle managed session before beginning another.
		/// The process-wide mutation barrier spans UI unbinding, IPC shutdown and service release;
		/// it is deliberately released BEFORE the next initializer starts.</summary>
		private bool TryLeaveCurrentGame()
		{
			string reason;
			if (!_shell.CanDetachGame(out reason))
			{
				_shell.SetGameLoadMessage(reason);
				return false;
			}
			CollectionTargetMutationLease lease;
			if (!CollectionTargetMutationLeaseManager.Shared.TryAcquireIdleSessionTransition(out lease))
			{
				_shell.SetGameLoadMessage(LanguageManager.Get("GameDiscovery.SwitchOperationBusy",
					"A native mod or Collection operation is still active. Try again after it finishes."));
				return false;
			}

			using (lease)
			{
				try
				{
					// No new UI actions can start after this point. The per-game IPC endpoint
					// must be gone before the old ModManager and dispatcher are released.
					_shell.IsFirstSessionLoading = true;
					if (_listener != null)
					{
						_listener.Dispose();
						_listener = null;
					}
					_shell.DetachGameToDiscovery();
					string previousModeId = _gate.ModeId;
					ReleaseNativeSession();
					_gate.EndSession(previousModeId);
					_shell.SetGameSessionInactive();
					return true;
				}
				catch (Exception exception) when (!(exception is OutOfMemoryException))
				{
					_teardownFailed = true;
					Trace.TraceError("Game-session teardown failed; refusing to attach another game: {0}", exception);
					_shell.SetGameLoadMessage(LanguageManager.Get("GameDiscovery.SwitchFailed",
						"Could not safely close the current game session. Restart NMM before managing a different game."));
					return false;
				}
				finally
				{
					_shell.IsFirstSessionLoading = false;
				}
			}
		}

		private async Task InitializeWithOwnedPresenterAsync(ApplicationInitializer initializer, IGameModeFactory factory)
		{
			var completion = new TaskCompletionSource<TaskEndedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
			EventHandler<TaskEndedEventArgs> handler = (sender, args) =>
			{
				// The initializer can complete from its background thread.
				// Continuations are ALWAYS marshaled to the existing shell UI context.
				if (_shell.IsDisposed) return;
				try
				{
					_shell.BeginInvoke((MethodInvoker)(() => completion.TrySetResult(args)));
				}
				catch (InvalidOperationException) { }
			};
			initializer.TaskEnded += handler;
			System.ComponentModel.PropertyChangedEventHandler progress = (sender, args) =>
			{
				if ((!String.Equals(args.PropertyName, "OverallProgress", StringComparison.Ordinal) &&
					!String.Equals(args.PropertyName, "OverallProgressMaximum", StringComparison.Ordinal) &&
					!String.Equals(args.PropertyName, "OverallMessage", StringComparison.Ordinal)) ||
					_shell.IsDisposed || !_shell.IsHandleCreated) return;
				try
				{
					_shell.BeginInvoke((MethodInvoker)(() =>
					{
						if (!_shell.IsDisposed)
							_shell.SetGameLoadProgress(initializer.OverallProgress, initializer.OverallProgressMaximum + 2,
								initializer.OverallMessage);
					}));
				}
				catch (InvalidOperationException) { }
			};
			initializer.PropertyChanged += progress;
			try
			{
				using (var presenter = new ApplicationInitializationForm(initializer))
				{
					presenter.ShowInTaskbar = false;
					initializer.Initialize(factory, SynchronizationContext.Current);
					presenter.Show(_shell); // Show, never ShowDialog: the main shell stays usable.
					await completion.Task;
				}
			}
			finally
			{
				initializer.TaskEnded -= handler;
				initializer.PropertyChanged -= progress;
			}
		}

		private GameModeRegistry BuildManagedRegistry(GameModeRegistry registry, GameDiscoveryCatalog catalog)
		{
			var installed = new GameModeRegistry();
			foreach (var entry in catalog.Managed)
			{
				var factory = registry.GetGameMode(entry.ModeId);
				if (factory != null) installed.RegisterGameMode(factory);
			}
			return installed;
		}

		private bool TryAcquireModeMutex(IGameModeDescriptor descriptor)
		{
			if (_modeMutex != null) throw new InvalidOperationException("A session already owns a game mutex.");
			_modeMutex = new Mutex(true,
				String.Format("{0}-{1}-GameModeMutex", CommonData.ModManagerName, descriptor.ModeId),
				out _ownsModeMutex);
			if (_ownsModeMutex) return true;
			try
			{
				using (var existing = MessagerClient.GetMessager(_environment, descriptor))
				{
					if (existing != null)
					{
						if (_pendingNxm != null && String.Equals(descriptor.ModeId,
								_gate.ModeId, StringComparison.OrdinalIgnoreCase))
						{
							try
							{
								NexusGameDomain.RequireMatchingLink(descriptor, _pendingNxm);
								existing.AddMod(_pendingNxm.OriginalString);
								_pendingNxm = null;
								_startupLinkForwarded = true;
							}
							catch (ArgumentException) { existing.BringToFront(); }
						}
						else existing.BringToFront();
					}
				}
			}
			catch (Exception exception)
			{
				Trace.TraceWarning("Existing NMM process could not be contacted: {0}", exception);
			}
			ReleaseMutex();
			return false;
		}

		private void DeliverPendingNxmIfMatching()
		{
			if (_pendingNxm == null || _listener == null || _gameMode == null) return;
			try
			{
				NexusGameDomain.RequireMatchingLink(_gameMode, _pendingNxm);
				_listener.AddMod(_pendingNxm.ToString());
				_pendingNxm = null;
			}
			catch (ArgumentException)
			{
				_shell.SetGameLoadMessage(LanguageManager.Get("GameDiscovery.LinkOtherGame",
					"The pending Nexus link belongs to another game. Choose its matching game in Game Discovery."));
			}
			catch (Exception exception) when (!(exception is OutOfMemoryException))
			{
				Trace.TraceWarning("Could not dispatch startup Nexus link: {0}", exception);
				_shell.SetGameLoadMessage(LanguageManager.Get("GameDiscovery.LinkDispatchFailed",
					"The incoming Nexus link could not be queued. No download was started."));
			}
		}

		private void ReleaseNativeSession()
		{
			try { _listener?.Dispose(); }
			catch (Exception exception) { Trace.TraceWarning("Error stopping game IPC: {0}", exception); }
			_listener = null;
			try
			{
				if (_services != null)
				{
					try { if (_gate.State == GameSessionEntryGate.EntryState.Ready) _services.ModInstallLog.Backup(); }
					finally { _bootstrapper.ReleaseSessionServices(_services); _services = null; }
				}
			}
			finally
			{
				try { _gameMode?.Dispose(); }
				finally { _gameMode = null; ReleaseMutex(); }
			}
		}

		private void ReleaseMutex()
		{
			if (_modeMutex == null) return;
			try { if (_ownsModeMutex) _modeMutex.ReleaseMutex(); }
			finally { _modeMutex.Dispose(); _modeMutex = null; _ownsModeMutex = false; }
		}

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			_shell.ManageGameRequested -= OnManageRequested;
			_shell.StartupDefaultGameRequested -= OnStartupDefaultRequested;
			_shell.ConfigureGameRequested -= OnConfigureRequested;
			_shell.LocateGameRequested -= OnLocateRequested;
			_shell.GameCatalogReady -= OnCatalogReady;
			ReleaseNativeSession();
		}
	}
	/// <summary>Explicit, single-value startup preference. No game discovery or storage changes.</summary>
	internal static class GameDiscoveryStartupPreference
	{
		internal static bool Toggle(ISettings settings, GameDiscoveryEntry entry)
		{
			if (settings == null) throw new ArgumentNullException(nameof(settings));
			if (entry == null || entry.Collection != GameDiscoveryCollection.Managed) return false;
			bool isCurrentDefault = settings.RememberGameMode &&
				String.Equals(settings.RememberedGameMode, entry.ModeId, StringComparison.OrdinalIgnoreCase);
			if (!isCurrentDefault && !GameSessionEntryGate.CanManage(entry)) return false;
			settings.RememberGameMode = !isCurrentDefault;
			settings.RememberedGameMode = isCurrentDefault ? String.Empty : entry.ModeId;
			return true;
		}
	}

}
