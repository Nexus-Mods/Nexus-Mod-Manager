namespace Nexus.Client
{
	using System;
	using System.Linq;
	using System.Threading.Tasks;
	using System.Windows.Forms;
	using Nexus.Client.BackgroundTasks;
	using Nexus.Client.Games;
	using Nexus.Client.Util;
	using Nexus.Client.Util.Localization;

	/// <summary>Attach and detach game-specific UI beneath the persistent main-window shell.</summary>
	public partial class MainForm
	{
		/// <summary>
		/// Call only on the UI thread after the initializer has completed and the
		/// previous session (if any) has been fully detached and disposed.
		/// </summary>
		internal void AttachFirstGame(MainFormVM viewModel)
		{
			if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));
			if (ViewModel != null || !IsHandleCreated || IsDisposed)
				throw new InvalidOperationException("This MainForm cannot attach another live game session.");
			if (InvokeRequired)
				throw new InvalidOperationException("Game controls must be created on the UI thread.");

			_gameDiscoveryCancellation?.Cancel();
			SuspendLayout();
			_mainTabbedView.BeginUpdate();
			try
			{
				// Placeholder documents never own game state or persist game layouts.
				ClosePlaceholder(_emptyModsDocument);
				ClosePlaceholder(_emptyCategoriesDocument);
				ClosePlaceholder(_emptyCollectionsDocument);
				_emptyModsDocument = null;
				_emptyCategoriesDocument = null;
				_emptyCollectionsDocument = null;

				InitializeGameSurfaceControls(viewModel, false);
				_activePluginsProfileSaveTimer.Interval = 250;
				_activePluginsProfileSaveTimer.Tick += ActivePluginsProfileSaveTimer_Tick;
				FormClosing -= MainForm_FormClosing;
				FormClosing += CheckDownloadsOnClosing;
				FormClosing += MainForm_FormClosing;
				ResizeEnd += MainForm_ResizeEnd;
				ResizeBegin += MainForm_ResizeBegin;
				Resize += MainForm_Resize;

				RestoreGameDependentBars();
				ViewModel = viewModel;
				ShowEmbeddedDockContents();
				ApplyDevExpressDisplaySettingsToSurfaces();
				try { InitializeDocuments(); }
				catch { ResetUI(); }
			}
			finally
			{
				_mainTabbedView.EndUpdate();
				ResumeLayout(true);
			}

			ApplyMainDocumentOrder(_mainTabbedView, GetDefaultMainDocumentOrder(),
				GetLayoutSetting(DevExpressDocumentOrderSettingsKey));
			ActivateModsDocument();
			ApplyDefaultMonitorPanelSizes();
			RestoreMonitorColumnWidths();
			_mainDocumentPersistenceEnabled = true;
			spbChangeMode.Enabled = true;
		}

		/// <summary>Read-only switch-admission check. No in-flight native or UX-owned work may lose its services.</summary>
		internal bool CanDetachGame(out string reason)
		{
			reason = null;
			if (ViewModel == null) return true;
			if (!_persistentSessionShell || IsFirstSessionLoading)
			{ reason = L("GameDiscovery.SwitchBusy", "A game is already loading or switching."); return false; }
			if (ViewModel.ModActivationMonitor.HasPendingNativeOperations || ViewModel.IsInstalling)
			{ reason = L("GameDiscovery.SwitchNativeBusy", "Finish the queued mod installation/removal before switching games."); return false; }
			if (_loadOrderControl?.HasActiveSessionWork == true)
			{ reason = L("LoadOrder.UI.LeaveBusy", "Finish or cancel the operation on the Load Order page before switching games."); return false; }
			// Paused, errored and incomplete downloads remain bound to the old game/repository.
			// They cannot be silently discarded until durable cross-session routing exists.
			if (ViewModel.DownloadMonitorVM.ActiveTasks.Count > 0 ||
				ViewModel.DownloadMonitorVM.Tasks.Any(task => task != null &&
					task.Status != BackgroundTasks.TaskStatus.Complete &&
					task.Status != BackgroundTasks.TaskStatus.Cancelled))
			{ reason = L("GameDiscovery.SwitchDownloadBusy", "Finish or remove pending, paused or incomplete downloads before switching games."); return false; }
			if (_collectionsPreviewControl?.HasActiveSessionWork == true || _fileManagerControl?.HasActiveSessionWork == true ||
				ViewModel.IsSwitching)
			{ reason = L("GameDiscovery.SwitchWorkflowBusy", "Finish the current Collection, File Manager or profile operation before switching games."); return false; }
			if (_activePluginsProfileSavePending)
			{
				_activePluginsProfileSaveTimer.Stop();
				SaveActivePluginsToCurrentProfile();
				if (_activePluginsProfileSavePending)
				{ reason = L("GameDiscovery.SwitchProfileBusy", "Profile state is still being saved. Try switching again shortly."); return false; }
			}
			return true;
		}

		/// <summary>Protects a detached load-order draft before profile changes, game teardown or application close.</summary>
		internal bool ConfirmLoadOrderLeave()
		{
			if (_loadOrderControl != null && (_loadOrderControl.HasActiveSessionWork || _loadOrderControl.HasUnsavedChanges) && FindMainDocument(_loadOrderControl) != null)
				_mainTabbedView.ActivateDocument(_loadOrderControl);
			return _loadOrderControl?.ConfirmLeave() ?? true;
		}

		/// <summary>UI-thread-only symmetric detach. The game services MUST remain alive until this returns.</summary>
		internal void DetachGameToDiscovery()
		{
			if (InvokeRequired || !IsHandleCreated || IsDisposed || !_persistentSessionShell || ViewModel == null)
				throw new InvalidOperationException("Cannot detach the current game on this window/thread.");

			// Persist per-game state before clearing its document and monitor references.
			SaveGameStatistics();
			SaveMainDocumentOrder();
			SaveMainDockingLayout();
			(_modManagerControl as ModManagement.UI.ModManagerDXControl)?.SavePersistedLayout();
			_downloadMonitorControl.DetachFromSessionHost(this);
			_modActivationMonitorControl.DetachFromSessionHost(this);
			_shellEnvironmentInfo.Settings.Save();
			_activePluginsProfileSaveTimer.Stop();
			_activePluginsProfileSavePending = false;

			_mainDocumentPersistenceEnabled = false;
			_mainTabbedView.BeginUpdate();
			_mainDockManager.BeginUpdate();
			SuspendLayout();
			try
			{
				_mainTabbedView.ActivateDocument(_gameDiscoveryDocument);
				UnbindCurrentGameEvents();
				UnbindCurrentGameBars();

				ClosePlaceholder((Form)_modManagerControl);
				ClosePlaceholder(_pluginManagerControl);
				ClosePlaceholder(_categoryManagerControl);
				ClosePlaceholder(_fileManagerControl);
				ClosePlaceholder(_collectionsPreviewControl);
				ClosePlaceholder(_wabbajackGalleryControl);
				ClosePlaceholder(_witcher3LoadOrderControl);
				ClosePlaceholder(_managedContentComponentsControl);
				ClosePlaceholder(_loadOrderControl);

				_downloadMonitorControl.TextChanged -= DownloadMonitor_TextChanged;
				_modActivationMonitorControl.TextChanged -= ModActivationMonitor_TextChanged;
				if (_downloadMonitorDockPanel?.ControlContainer != null)
					_downloadMonitorDockPanel.ControlContainer.Controls.Remove(_downloadMonitorControl);
				if (_modActivationMonitorDockPanel?.ControlContainer != null)
					_modActivationMonitorDockPanel.ControlContainer.Controls.Remove(_modActivationMonitorControl);
				_downloadMonitorControl.Dispose();
				_modActivationMonitorControl.Dispose();
				_downloadMonitorDockPanel.Visibility = DevExpress.XtraBars.Docking.DockVisibility.Hidden;
				_modActivationMonitorDockPanel.Visibility = DevExpress.XtraBars.Docking.DockVisibility.Hidden;

				_viewModel = null;
				_modManagerControl = null;
				_pluginManagerControl = null;
				_categoryManagerControl = null;
				_fileManagerControl = null;
				_collectionsPreviewControl = null;
				_wabbajackGalleryControl = null;
				_witcher3LoadOrderControl = null;
				_managedContentComponentsControl = null;
				_loadOrderControl = null;
				_downloadMonitorControl = null;
				_modActivationMonitorControl = null;

				InitializeNoGameDocuments();
				EnsureMainDocuments();
				DisableGameDependentBars();
				Text = MainFormVM.FormatTitle(_shellEnvironmentInfo.ApplicationVersion);
			}
			finally
			{
				ResumeLayout(true);
				_mainDockManager.EndUpdate();
				_mainTabbedView.EndUpdate();
			}
			ApplyMainDocumentOrder(_mainTabbedView, GetDefaultMainDocumentOrder(), null);
			_mainTabbedView.ActivateDocument(_gameDiscoveryDocument);
		}

		private void UnbindCurrentGameEvents()
		{
			MainFormVM vm = ViewModel;
			vm.ConfirmLoadOrderLeave = null;
			vm.ProfileManager.ModProfiles.CollectionChanged -= ModProfiles_CollectionChanged;
			vm.ProfileSwitching -= ViewModel_ProfileSwitching;
			vm.AbortedProfileSwitch -= ViewModel_AbortedProfileSwitch;
			vm.ProfileDownloading -= ViewModel_ProfileDownloading;
			vm.ProfileSharing -= ViewModel_ProfileSharing;
			vm.MigratingMods -= ViewModel_MigratingMods;
			vm.ModManager.VirtualModActivator.ModActivationChanged -= VirtualModActivator_ModActivationChanged;
			vm.ModManager.VirtualModActivator.VirtualStoreMutationEnded -= VirtualModActivator_VirtualStoreMutationEnded;
			vm.CheckingOnlineProfileIntegrity -= ViewModel_CheckingOnlineProfileIntegrity;
			vm.ProfileManager.CheckOnlineProfileIntegrityStarted -= ViewModel_CheckingOnlineProfileIntegrity;
			vm.ApplyingImportedLoadOrder -= ViewModel_ApplyingImportedLoadOrder;
			vm.CreatingBackup -= ViewModel_CreatingBackup;
			vm.RestoringBackup -= ViewModel_RestoringBackup;
			vm.PurgingLooseFiles -= ViewModel_PurgingLooseFiles;
			vm.ConfigFilesFixing -= ViewModel_ConfigFilesFixing;
			vm.ModManagerVM.ProfileSwitchSettingUp -= ModManagerVM_ProfileSwitchSettingUp;
			vm.ModManagerVM.DownloadModeChanged -= ViewModel_SwitchingSkyrimDownloadMode;
			vm.ModManager.LoginTask.PropertyChanged -= LoginTask_PropertyChanged;
			vm.ModRepository.RateLimitExceeded -= ModRepository_RateLimitExceeded;
			vm.ModRepository.UserStatusUpdate -= ModRepository_UserStatusUpdate;
			vm.Updating -= ViewModel_Updating;
			vm.ToggleLoginCommand.BeforeExecute -= LogoutCommand_BeforeExecute;
			vm.GameLauncher.GameLaunched -= GameLauncher_GameLaunched;
			if (vm.GameMode.SupportedToolsLauncher != null)
				vm.GameMode.SupportedToolsLauncher.ChangedToolPath -= SupportedTools_ChangedToolPath;
			if (vm.UsesPlugins)
			{
				vm.PluginManager.ActivePlugins.CollectionChanged -= ActivePlugins_CollectionChanged;
				vm.PluginManagerVM.PluginMoved -= pmcPluginManager_PluginMoved;
				vm.PluginManagerVM.ApplyingImportedLoadOrder -= ViewModel_ApplyingImportedLoadOrder;
			}
			vm.DownloadMonitorVM.ActiveTasks.CollectionChanged -= ActiveTasks_CollectionChanged;
			vm.DownloadMonitorVM.Tasks.CollectionChanged -= Tasks_CollectionChanged;
			vm.DownloadMonitorVM.PropertyChanged -= ActiveTasks_PropertyChanged;
			_modManagerControl.SetTextBoxFocus -= MmgModManagerControlSetTextBoxFocus;
			_modManagerControl.ResetSearchBox -= MmgModManagerControlResetSearchBox;
			_modManagerControl.UpdateModsCount -= MmgModManagerControlUpdateModsCount;
			_modManagerControl.UninstallModFromProfiles -= ModManagerControlUninstallModFromProfiles;
			_modManagerControl.UninstalledAllMods -= MmgModManagerControlUninstalledAllMods;
			_downloadMonitorControl.SetTextBoxFocus -= DmcDownloadMonitorControlSetTextBoxFocus;
			_pluginManagerControl.UpdatePluginsCount -= PmcPluginManagerControlUpdatePluginsCount;
			_pluginManagerControl.PluginMoved -= pmcPluginManager_PluginMoved;
			_modActivationMonitorControl.UpdateBottomBarFeedback -= MacModActivationMonitorControlUpdateBottomBarFeedback;
			_categoryManagerControl.CollapseAllCategoriesRequested -= CategoryManagerControl_CollapseAllCategoriesRequested;
			_categoryManagerControl.ExpandAllCategoriesRequested -= CategoryManagerControl_ExpandAllCategoriesRequested;
			_collectionsPreviewControl.PreviewActivated -= CollectionsPreviewControl_PreviewActivated;
			_collectionsPreviewControl.ManagedModRequested -= CollectionsPreviewControl_ManagedModRequested;
			_collectionsPreviewControl.ManagedAssociationsChanged -= CollectionsPreviewControl_ManagedAssociationsChanged;
			FormClosing -= CheckDownloadsOnClosing;
			ResizeEnd -= MainForm_ResizeEnd;
			ResizeBegin -= MainForm_ResizeBegin;
			Resize -= MainForm_Resize;
			_activePluginsProfileSaveTimer.Tick -= ActivePluginsProfileSaveTimer_Tick;
		}

		private void UnbindCurrentGameBars()
		{
			foreach (var command in _changeModeCommandsWithExecutedHandler)
				command.Executed -= ChangeGameModeCommand_Executed;
			_changeModeCommandsWithExecutedHandler.Clear();
			foreach (var tool in _boundGameTools)
			{
				tool.DisplayToolView -= Tool_DisplayToolView;
				tool.CloseToolView -= Tool_CloseToolView;
			}
			_boundGameTools.Clear();
			foreach (var binding in _mainBarCommandBindings.Values)
				binding.Unbind();
			_mainBarCommandBindings.Clear();
			ClearTransientPopupItems(popupLaunch);
			ClearTransientPopupItems(popupProfiles);
			ClearTransientPopupItems(popupChangeMode);
			ClearTransientPopupItems(popupSupportedTools);
			ClearTransientPopupItems(popupTools);
			ClearTransientPopupItems(popupFolders);
			ClearTransientPopupItems(popupHelp);
			_launchDefaultItem = null;
			_profileDefaultItem = null;
		}

		private static void ClosePlaceholder(Form form)
		{
			if (form == null) return;
			form.Close();
			form.Dispose();
		}

		/// <summary>Normally invoked from Shown; the shell was already shown before attachment.</summary>
		internal async Task NotifyFirstGameReadyAsync()
		{
			if (ViewModel == null || IsDisposed) return;
			ViewModel.ViewIsShown();
			if (ViewModel.ModRepository.IsOffline && !ViewModel.ModManager.LoginTask.LoggingIn)
				ViewModel.ModManager.LoginTask.TokenLogin();

			ModMigrationCheck();
			ShowGameSpecificDisclaimer();
			ConfigFilesCheck();
			if (IsMainDocumentActive(_fileManagerControl))
				await _fileManagerControl.EnsureInitialLoadAsync().ConfigureAwait(true);
			if (ViewModel.Witcher3ModSettingsLifecycle != null && IsMainDocumentActive(_witcher3LoadOrderControl))
				await _witcher3LoadOrderControl.RefreshOnActivateAsync().ConfigureAwait(true);
			if (ViewModel.GameMode is Games.ManagedContent.IManagedContentComponentProvider &&
				IsMainDocumentActive(_managedContentComponentsControl))
				await _managedContentComponentsControl.RefreshOnActivateAsync().ConfigureAwait(true);
			if (ViewModel.NativeLoadOrder != null && IsMainDocumentActive(_loadOrderControl))
				await _loadOrderControl.RefreshOnActivateAsync().ConfigureAwait(true);
		}
	}
}
