namespace Nexus.Client
{
	using System;
	using System.Collections.Generic;
	using System.Collections.Specialized;
	using System.ComponentModel;
	using System.Data.SQLite;
	using System.Diagnostics;
	using System.Drawing;
	using System.IO;
	using System.Linq;
	using System.Text;
	using System.Threading.Tasks;
	using System.Windows.Forms;

	using DevExpress.XtraBars;
	using DevExpress.XtraEditors.Controls;
	using DevExpress.XtraEditors.Repository;
	using DevExpress.XtraSplashScreen;

	using Nexus.Client.BackgroundTasks;
	using Nexus.Client.BackgroundTasks.UI;
	using Nexus.Client.Commands;
	using Nexus.Client.DownloadMonitoring.UI;
	using Nexus.Client.UI.Controls;
	using Nexus.Client.Games;
	using Nexus.Client.Games.Settings;
	using Nexus.Client.Games.Tools;
	using Nexus.Client.ModActivationMonitoring.UI;
	using Nexus.Client.ModManagement;
	using Nexus.Client.ModManagement.UI;
	using Nexus.Client.ModRepositories;
	using Nexus.Client.Mods;
	using Nexus.Client.Mods.Formats.FOMod;
	using Nexus.Client.PluginManagement.UI;
	using Nexus.Client.Settings.UI;
	using Nexus.Client.SSO;
	using Nexus.Client.UI;
	using Nexus.Client.Util;
	using Nexus.Client.Util.Collections;
	using Nexus.UI.Controls;

	using DevExpress.LookAndFeel;
	using DevExpress.Skins;

	using DevExpress.XtraEditors;

	/// <summary>
	/// The main form of the mod manager.
	/// </summary>
	public partial class MainForm : ManagedFontXtraForm
	{
		private MainFormVM _viewModel;
		private FormWindowState _lastWindowState = FormWindowState.Normal;
		private readonly IModManagerView _modManagerControl;
		private readonly PluginManagerDXControl _pluginManagerControl;
		private readonly DownloadMonitorControl _downloadMonitorControl;
		private readonly ModActivationMonitorControl _modActivationMonitorControl;
		private readonly CategoryManagerControl _categoryManagerControl;
		private readonly FileManagerControl _fileManagerControl;
		private readonly Timer _activePluginsProfileSaveTimer = new Timer();
		private bool _activePluginsProfileSavePending;
		private readonly List<ITool> _boundGameTools = new List<ITool>();

		private const string DevExpressSkinSettingsKey = "mainForm.DevExpressSkin";

		private BarStaticItem _devExpressSkinLabel;
		private BarEditItem _devExpressSkinComboBox;
		private RepositoryItemComboBox _devExpressSkinRepository;
		private bool _updatingDevExpressSkinSelector;

		public string OptionalPremiumMessage = string.Empty;

		FormWindowState LastWindowState = FormWindowState.Minimized;


		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		protected MainFormVM ViewModel
		{
			get => _viewModel;
			set
			{
				_viewModel = value;

				_viewModel.ProfileManager.ModProfiles.CollectionChanged += ModProfiles_CollectionChanged;
				_viewModel.ProfileSwitching += ViewModel_ProfileSwitching;
				_viewModel.AbortedProfileSwitch += ViewModel_AbortedProfileSwitch;
				_viewModel.ProfileDownloading += ViewModel_ProfileDownloading;
				_viewModel.ProfileSharing += ViewModel_ProfileSharing;
				_viewModel.MigratingMods += ViewModel_MigratingMods;
				_viewModel.ModManager.VirtualModActivator.ModActivationChanged += VirtualModActivator_ModActivationChanged;
				_viewModel.ModManager.VirtualModActivator.VirtualStoreMutationEnded += VirtualModActivator_VirtualStoreMutationEnded;
				_viewModel.CheckingOnlineProfileIntegrity += ViewModel_CheckingOnlineProfileIntegrity;
				_viewModel.ProfileManager.CheckOnlineProfileIntegrityStarted += ViewModel_CheckingOnlineProfileIntegrity;
				_viewModel.ApplyingImportedLoadOrder += ViewModel_ApplyingImportedLoadOrder;
				_viewModel.CreatingBackup += ViewModel_CreatingBackup;
				_viewModel.RestoringBackup += ViewModel_RestoringBackup;
				_viewModel.PurgingLooseFiles += ViewModel_PurgingLooseFiles;
				_viewModel.ConfigFilesFixing += ViewModel_ConfigFilesFixing;
				_viewModel.ModManagerVM.ProfileSwitchSettingUp += ModManagerVM_ProfileSwitchSettingUp;
				_modManagerControl.ViewModel = _viewModel.ModManagerVM;

				_categoryManagerControl.ViewModel = _viewModel.ModManagerVM;

				if (ViewModel.UsesPlugins)
				{
					_pluginManagerControl.ViewModel = _viewModel.PluginManagerVM;
					_pluginManagerControl.PluginManager = _viewModel.PluginManager;
					_viewModel.PluginManager.ActivePlugins.CollectionChanged += ActivePlugins_CollectionChanged;
					_pluginManagerControl.ViewModel.PluginMoved += pmcPluginManager_PluginMoved;
					_pluginManagerControl.ViewModel.ApplyingImportedLoadOrder += ViewModel_ApplyingImportedLoadOrder;
				}

				_modActivationMonitorControl.ViewModel = _viewModel.ModActivationMonitorVM;
				_fileManagerControl.ViewModel = _viewModel.ModManagerVM;
				_downloadMonitorControl.ViewModel = _viewModel.DownloadMonitorVM;
				_downloadMonitorControl.ViewModel.ActiveTasks.CollectionChanged += ActiveTasks_CollectionChanged;
				_downloadMonitorControl.ViewModel.Tasks.CollectionChanged += Tasks_CollectionChanged;
				_downloadMonitorControl.ViewModel.PropertyChanged += ActiveTasks_PropertyChanged;

				ViewModel.ModRepository.UserStatusUpdate += ModRepository_UserStatusUpdate;

				ApplyTheme(_viewModel.ModeTheme);

				Text = _viewModel.Title;

				_viewModel.ConfirmUpdaterAction = ConfirmUpdaterAction;

				ClearTransientPopupItems(popupHelp);
				foreach (HelpInformation.HelpLink helpLink in _viewModel.HelpInfo.HelpLinks)
				{
					BarButtonItem helpItem = new BarButtonItem(barManagerMain, helpLink.Name)
					{
						Tag = helpLink,
						Hint = helpLink.Url
					};
					helpItem.ItemClick += HelpItem_ItemClick;
					popupHelp.AddItem(helpItem);
				}


				SetBarItemVisible(tsbSkyrimDownloads, _viewModel.ModManagerVM.IsSkyrimSEGameMode);

				if (_viewModel.ModManagerVM.IsSkyrimSEGameMode)
				{
					tsbSkyrimDownloads.Caption = _viewModel.ModManagerVM.SkyrimSEDownloadFeedback;
					_modManagerControl.ViewModel.SwitchingSkyrimDownloadMode += ViewModel_SwitchingSkyrimDownloadMode;
				}

				BindCommands();
			}
		}

		private void ViewModel_SwitchingSkyrimDownloadMode(object sender, EventArgs e)
		{
			tsbSkyrimDownloads.Caption = _viewModel.ModManagerVM.SkyrimSEDownloadFeedback;
		}

		private void ModManagerVM_ProfileSwitchSettingUp(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ModManagerVM_ProfileSwitchSettingUp, sender, e);
				return;
			}
			_modManagerControl.ToggleDisabledSummary(true);
			ProgressDialog.ShowDialog(this, e.Argument);
			_modManagerControl.ToggleDisabledSummary(false);

			ViewModel.ExecuteProfileSwitch(this);
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the view with its dependencies.
		/// </summary>
		/// <param name="viewModel">The view model that provides the data and operations for this view.</param>
		public MainForm(MainFormVM viewModel)
		{

			// Restore the global skin before any DevExpress controls are created.
			InitializeDevExpressLookAndFeel(viewModel);

			InitializeComponent();
			InitializeMainBars();
			InitializeDevExpressSkinSelector();
			InitializeDevExpressDisplaySelector(viewModel);
			BuildMainToolbarLinks();

			FormClosing += CheckDownloadsOnClosing;
			FormClosing += MainForm_FormClosing;

			ResizeEnd += MainForm_ResizeEnd;
			ResizeBegin += MainForm_ResizeBegin;
			Resize += MainForm_Resize;
			Shown += MainForm_Shown;
			_activePluginsProfileSaveTimer.Interval = 250;
			_activePluginsProfileSaveTimer.Tick += ActivePluginsProfileSaveTimer_Tick;

			_pluginManagerControl = new PluginManagerDXControl();
			_modManagerControl = new ModManagerDXControl();
			_downloadMonitorControl = new DownloadMonitorControl();
			_modActivationMonitorControl = new ModActivationMonitorControl();
			_categoryManagerControl = new CategoryManagerControl();
			_categoryManagerControl.CollapseAllCategoriesRequested += CategoryManagerControl_CollapseAllCategoriesRequested;
			_categoryManagerControl.ExpandAllCategoriesRequested += CategoryManagerControl_ExpandAllCategoriesRequested;
			_fileManagerControl = new FileManagerControl();
			InitializeMainDockingInfrastructure();
			_modManagerControl.SetTextBoxFocus += MmgModManagerControlSetTextBoxFocus;
			_modManagerControl.ResetSearchBox += MmgModManagerControlResetSearchBox;
			_modManagerControl.UpdateModsCount += MmgModManagerControlUpdateModsCount;
			_modManagerControl.UninstallModFromProfiles += ModManagerControlUninstallModFromProfiles;
			_modManagerControl.UninstalledAllMods += MmgModManagerControlUninstalledAllMods;
			_downloadMonitorControl.SetTextBoxFocus += DmcDownloadMonitorControlSetTextBoxFocus;
			_pluginManagerControl.UpdatePluginsCount += PmcPluginManagerControlUpdatePluginsCount;
			_pluginManagerControl.PluginMoved += pmcPluginManager_PluginMoved;
			_modActivationMonitorControl.UpdateBottomBarFeedback += MacModActivationMonitorControlUpdateBottomBarFeedback;
			viewModel.ModManager.LoginTask.PropertyChanged += LoginTask_PropertyChanged;
			viewModel.ModRepository.RateLimitExceeded += (sender, args) => Invoke((Action<RateLimitExceededArgs>)OnRateLimitExceeded, args);

			if (viewModel.GameMode.SupportedToolsLauncher != null)
			{
				viewModel.GameMode.SupportedToolsLauncher.ChangedToolPath += SupportedTools_ChangedToolPath;
			}

			ViewModel = viewModel;
			ShowEmbeddedDockContents();
			ApplyDevExpressDisplaySettingsToSurfaces();

			try
			{
				InitializeDocuments();
			}
			catch
			{
				ResetUI();
			}

			viewModel.EnvironmentInfo.Settings.WindowPositions.GetWindowPosition("MainForm", this);
			_lastWindowState = WindowState;
		}

		private void OnRateLimitExceeded(RateLimitExceededArgs args)
		{
			XtraMessageBox.Show(this, $"You've reached your daily and hourly limit. Try again in {Math.Floor((args.RateLimit.HourlyReset - DateTimeOffset.UtcNow).TotalMinutes)} minutes.", "API Rate Limit exceeded", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}

		private void ToolStripButtonRateLimitOnClick(object sender, EventArgs e)
		{
			if (ViewModel.UserStatus != null)
			{
				var rateLimit = ViewModel.ModRepository.RateLimit;
				var dailyReset = rateLimit.DailyReset - DateTimeOffset.UtcNow;

				var info =
					$"Daily: {rateLimit.DailyRemaining}/{rateLimit.DailyLimit} requests left (resets in {dailyReset.Hours}h {dailyReset.Minutes} m)\n" +
					$"Hourly: {rateLimit.HourlyRemaining}/{rateLimit.HourlyLimit} requests left (resets in {Math.Floor((rateLimit.HourlyReset - DateTimeOffset.UtcNow).TotalMinutes)} m)";
				XtraMessageBox.Show(this, info, "API Rate Limit status", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			else
			{
				XtraMessageBox.Show(this, "You need to be logged in to view rate limits.", "API Rate Limit status", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
		}

		#endregion

		#region Startup Checks

		/// <summary>
		/// Checks whether legacy install-log state needs user attention.
		/// </summary>
		private void ModMigrationCheck()
		{
			if (ViewModel.ProfileManager?.CurrentProfile != null)
			{
				ViewModel.ModManager.VirtualModActivator.Initialize();

				if (!ViewModel.ModManager.VirtualModActivator.Initialized)
				{
					ViewModel.ModManager.VirtualModActivator.Setup();
				}

				return;
			}

			if (ViewModel.RequiresModMigration())
			{
				var strMigrationWarning = "NMM found an old install-log setup for this game mode." + Environment.NewLine + Environment.NewLine +
					"The legacy migration tool used to reinstall or uninstall every active mod automatically. That process is no longer run at startup because it can remove working files without enough user control." + Environment.NewLine + Environment.NewLine +
					"Detected active install-log entries: " + ViewModel.ModManager.InstallationLog.ActiveMods.Count + Environment.NewLine +
					"Virtual install folder: " + ViewModel.ModManager.VirtualModActivator.VirtualPath + Environment.NewLine + Environment.NewLine +
					"NMM will keep the existing files in place. Some profile and virtual-install features may not work correctly until this setup is repaired manually." + Environment.NewLine + Environment.NewLine +
					"Recommended recovery path:" + Environment.NewLine +
					"1. Back up the game folder, NMM config folder, and mod archives." + Environment.NewLine +
					"2. Verify that the Mods folder and Virtual Install folder in Settings point to the correct locations." + Environment.NewLine +
					"3. Reinstall or reactivate the affected mods manually once the folders are correct.";

				ExtendedMessageBox.Show(this, strMigrationWarning, "Legacy install setup detected", MessageBoxButtons.OK, MessageBoxIcon.Warning);

				if (!ViewModel.ModManager.VirtualModActivator.Initialized)
				{
					ViewModel.ModManager.VirtualModActivator.Setup();
				}
			}
		}

		/// <summary>
		/// Checks whether to show a game specific disclaimer.
		/// </summary>
		private void ShowGameSpecificDisclaimer()
		{
			string warning = ViewModel.RequiresStartupWarning();
			if (!string.IsNullOrEmpty(warning))
			{
				ExtendedMessageBox.Show(this, warning, "New game version disclaimer", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}

		private void ConfigFilesCheck()
		{
			var lstConfigFiles = new List<string>();

			var strVirtualConfigFile = ViewModel.VirtualModActivator.RequiresFixing();

			if (!string.IsNullOrEmpty(strVirtualConfigFile))
			{
				lstConfigFiles.Add(strVirtualConfigFile);
			}

			var strCurrentProfile = ViewModel.VirtualModActivator.RequiresFixing(ViewModel.ProfileManager.GetProfileModListPath(ViewModel.ProfileManager.CurrentProfile));

			if (!string.IsNullOrEmpty(strCurrentProfile))
			{
				lstConfigFiles.Add(strCurrentProfile);
			}

			if (lstConfigFiles.Count > 0)
			{
				ViewModel.FixConfigFiles(lstConfigFiles, null);
			}
		}

		#endregion

		#region DevExpress Skin

		private static void InitializeDevExpressLookAndFeel(MainFormVM viewModel)
		{
			SkinManager.EnableFormSkins();

			if (viewModel?.EnvironmentInfo?.Settings?.DockPanelLayouts == null)
				return;

			if (!viewModel.EnvironmentInfo.Settings.DockPanelLayouts.ContainsKey(
					DevExpressSkinSettingsKey))
			{
				return;
			}

			string savedSkin =
				viewModel.EnvironmentInfo.Settings.DockPanelLayouts[
					DevExpressSkinSettingsKey];

			if (String.IsNullOrWhiteSpace(savedSkin))
				return;

			bool skinExists = SkinManager.Default.Skins
				.Cast<SkinContainer>()
				.Any(
					skin => String.Equals(
						skin.SkinName,
						savedSkin,
						StringComparison.OrdinalIgnoreCase));

			if (skinExists)
				UserLookAndFeel.Default.SetSkinStyle(savedSkin);
		}

		/// <summary>
		/// Creates the native DevExpress skin selector used by the main toolbar.
		/// </summary>
		private void InitializeDevExpressSkinSelector()
		{
			_devExpressSkinLabel = new BarStaticItem
			{
				Manager = barManagerMain,
				Caption = "UI Skin:"
			};

			_devExpressSkinRepository = new RepositoryItemComboBox
			{
				TextEditStyle = TextEditStyles.DisableTextEditor
			};
			barManagerMain.RepositoryItems.Add(_devExpressSkinRepository);

			_devExpressSkinComboBox = new BarEditItem(barManagerMain, _devExpressSkinRepository)
			{
				EditWidth = 165,
				Hint = "Select the appearance of DevExpress controls"
			};

			_updatingDevExpressSkinSelector = true;
			try
			{
				IEnumerable<string> availableSkins = SkinManager.Default.Skins.Cast<SkinContainer>()
					.Select(skin => skin.SkinName)
					.Where(name => !String.IsNullOrWhiteSpace(name))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase);

				foreach (string skinName in availableSkins)
					_devExpressSkinRepository.Items.Add(skinName);

				string currentSkin = UserLookAndFeel.Default.SkinName;
				string selectedSkin = _devExpressSkinRepository.Items.Cast<object>()
					.Select(Convert.ToString)
					.FirstOrDefault(item => String.Equals(item, currentSkin, StringComparison.OrdinalIgnoreCase));

				if (selectedSkin == null && _devExpressSkinRepository.Items.Count > 0)
					selectedSkin = Convert.ToString(_devExpressSkinRepository.Items[0]);

				_devExpressSkinComboBox.EditValue = selectedSkin;
			}
			finally
			{
				_updatingDevExpressSkinSelector = false;
			}

			_devExpressSkinComboBox.EditValueChanged += DevExpressSkinComboBox_EditValueChanged;
		}

		/// <summary>
		/// Applies and persists a skin selected from the DevExpress toolbar editor.
		/// </summary>
		private void DevExpressSkinComboBox_EditValueChanged(object sender, EventArgs e)
		{
			if (_updatingDevExpressSkinSelector)
				return;

			string skinName = Convert.ToString(_devExpressSkinComboBox.EditValue);
			if (String.IsNullOrWhiteSpace(skinName) || String.Equals(UserLookAndFeel.Default.SkinName, skinName, StringComparison.OrdinalIgnoreCase))
				return;

			UserLookAndFeel.Default.SetSkinStyle(skinName);
			_modManagerControl?.ForceListRefresh();
			ApplyDevExpressDisplaySettingsToSurfaces();

			if (ViewModel?.EnvironmentInfo?.Settings?.DockPanelLayouts != null)
			{
				ViewModel.EnvironmentInfo.Settings.DockPanelLayouts[DevExpressSkinSettingsKey] = skinName;
				ViewModel.EnvironmentInfo.Settings.Save();
			}

			_devExpressSkinComboBox.Hint = "Current skin: " + skinName;
		}

		#endregion

		/// <summary>
		/// Initializes the main UI components.
		/// </summary>
		/// <remarks>
		/// If the metrics of the various UI components have been saved, they are loaded. Otherwise,
		/// the default layout is applied.
		/// </remarks>
		protected void InitializeDocuments()
		{
			EnsureMainDocuments();
			RestoreMainDockingLayout();

			if (ViewModel.UsesPlugins)
			{
				toolStripLabelPluginsCounter.Caption = "  Total plugins: " + ViewModel.PluginManagerVM.ManagedPlugins.Count + "   |   Active plugins: ";

				var myFontFamily = new FontFamily(GetBarItemFont(toolStripLabelActivePluginsCounter).Name);

				int limitedPluginsCount = ViewModel.PluginManagerVM.ActivePlugins.Count(x => x != null && !x.IgnoreIndexing);

				if (limitedPluginsCount > ViewModel.PluginManagerVM.MaxAllowedActivePluginsCount)
				{
					var icoIcon = new Icon(SystemIcons.Warning, 16, 16);
					toolStripLabelActivePluginsCounter.ImageOptions.Image = icoIcon.ToBitmap();
					SetBarItemForeColor(toolStripLabelActivePluginsCounter, Color.Red);

					if (myFontFamily.IsStyleAvailable(FontStyle.Bold))
					{
						SetBarItemFontStyle(toolStripLabelActivePluginsCounter, FontStyle.Bold);
					}
					else if (myFontFamily.IsStyleAvailable(FontStyle.Regular))
					{
						SetBarItemFontStyle(toolStripLabelActivePluginsCounter, FontStyle.Regular);
					}

					toolStripLabelActivePluginsCounter.Caption = limitedPluginsCount.ToString() + " (" + ViewModel.PluginManagerVM.ActivePlugins.Count(x => x != null).ToString() + ")";
					toolStripLabelActivePluginsCounter.Hint = $"There may be too many active plugins. {ViewModel.CurrentGameModeName} might not start!";
				}
				else
				{
					toolStripLabelActivePluginsCounter.ImageOptions.Image = null;
					SetBarItemForeColor(toolStripLabelActivePluginsCounter, Color.Empty);

					if (myFontFamily.IsStyleAvailable(FontStyle.Regular))
					{
						SetBarItemFontStyle(toolStripLabelActivePluginsCounter, FontStyle.Regular);
					}
					else if (myFontFamily.IsStyleAvailable(FontStyle.Bold))
					{
						SetBarItemFontStyle(toolStripLabelActivePluginsCounter, FontStyle.Bold);
					}

					toolStripLabelActivePluginsCounter.Caption = limitedPluginsCount.ToString() + " (" + ViewModel.PluginManagerVM.ActivePlugins.Count(x => x != null).ToString() + ")";
				}

			}
			else
			{
				SetBarItemVisible(toolStripLabelPluginsCounter, false);
			}

			UpdateModsFeedback();
			UserStatusFeedback();
		}

		/// <summary>
		/// Sets the UI elements providing feedback on the user online status.
		/// </summary>
		protected void UserStatusFeedback()
		{
			SetBarItemVisible(toolStripLabelLoginMessage, true);

			if (ViewModel.OfflineMode)
			{
				if (toolStripProgressBarDownloadSpeed != null)
				{
					toolStripProgressBarDownloadSpeed.Visible = false;
				}

				toolStripLabelLoginMessage.Caption = "You are not logged in.";
				SetBarItemFontStyle(toolStripLabelLoginMessage, FontStyle.Bold);
				SetBarItemVisible(toolStripButtonGoPremium, false);
				toolStripButtonOnlineStatus.ImageOptions.Image = new Bitmap(Properties.Resources.loggedout_flat, 32, 30);
				SetBarItemVisible(toolStripLabelDownloads, false);
			}
			else
			{
				toolStripButtonOnlineStatus.ImageOptions.Image = new Bitmap(Properties.Resources.loggedin_flat, 32, 30);

				// We no longer give a damn about a user's Nexus status
				//if (ViewModel.UserStatus.IsPremium)
				//{
				SetBarItemVisible(toolStripButtonGoPremium, false);
				OptionalPremiumMessage = string.Empty;
				toolStripButtonGoPremium.Enabled = false;

				if (toolStripProgressBarDownloadSpeed != null)
				{
					toolStripProgressBarDownloadSpeed.Maximum = 100;
					toolStripProgressBarDownloadSpeed.Value = 0;
					toolStripProgressBarDownloadSpeed.ColorFillMode = DownloadProgressBarItem.FillType.Ascending;
					toolStripProgressBarDownloadSpeed.ShowOptionalProgress = true;
				}
				toolStripLabelDownloads.Tag = "Download Progress:";
				//}
				//else
				//{
				//                toolStripButtonGoPremium.Visible = true;
				//                toolStripButtonGoPremium.Enabled = true;
				//                OptionalPremiumMessage = " Not a Premium Member.";

				//                if (toolStripProgressBarDownloadSpeed != null)
				//                {
				//		// Disabled for the time being since there's currently no way to check whether an user is browsing the Nexus with an active adblocker
				//                    toolStripProgressBarDownloadSpeed.Maximum = (ViewModel.UserStatus.IsSupporter) ? 2048 : 2048;
				//                    toolStripProgressBarDownloadSpeed.Value = 0;
				//                    toolStripProgressBarDownloadSpeed.ColorFillMode = DownloadProgressBarItem.FillType.Descending;
				//                    toolStripProgressBarDownloadSpeed.ShowOptionalProgress = false;
				//                }

				//                toolStripLabelDownloads.Tag = "Download Speed:";
				//}

				if (toolStripProgressBarDownloadSpeed != null && _downloadMonitorControl.ViewModel.ActiveTasks.Count > 0)
				{
					toolStripProgressBarDownloadSpeed.Visible = true;
				}

				toolStripLabelDownloads.Caption = $"{toolStripLabelDownloads.Tag} ({_downloadMonitorControl.ViewModel.ActiveTasks.Count} {(_downloadMonitorControl.ViewModel.ActiveTasks.Count == 1 ? "File" : "Files")}) ";
			}
		}

		/// <summary>
		/// Resets the UI layout to the default.
		/// </summary>
		protected void ResetUI()
		{
			ResetMainDockingLayout();

			try
			{
				_modManagerControl.ResetColumns();
			}
			catch { }
		}

		/// <summary>
		/// Repairs the cached FOMOD info.xml (name/version/description) for mods that
		/// currently show up as uncategorized, by restoring it from each mod's legacy
		/// loose cache folder when one is still present on disk.
		/// </summary>
		protected async void RepairFomodInfoCache()
		{
			var targetMods = ViewModel.ModManagerVM.ManagedMods
				.Where(mod => (mod.CategoryId == 0) && (mod.CustomCategoryId == -1))
				.ToList();

			if (targetMods.Count == 0)
			{
				XtraMessageBox.Show(this, "No uncategorized mods were found - nothing to repair.",
					"Repair FOMOD Info Cache", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			// Snapshot everything the background thread needs before leaving the UI
			// thread - ManagedMods is UI-bound and must not be touched off-thread.
			var cacheDirectory = ViewModel.GameMode.GameModeEnvironmentInfo.ModCacheDirectory;
			var archivePaths = targetMods.Select(mod => mod.ModArchivePath).ToList();

			SplashScreenManager.ShowDefaultWaitForm("Repair FOMOD Info Cache",
				string.Format("Checking {0} uncategorized mod(s)...", targetMods.Count));

			FOModCacheRepairTool.RepairResult result;
			try
			{
				result = await Task.Run(() => FOModCacheRepairTool.RepairFromLegacyCache(cacheDirectory, archivePaths));
			}
			catch (Exception e)
			{
				TraceUtil.TraceException(e);
				XtraMessageBox.Show(this, "The repair could not be completed: " + e.Message,
					"Repair FOMOD Info Cache", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}
			finally
			{
				SplashScreenManager.CloseDefaultWaitForm();
			}

			foreach (var error in result.Errors)
			{
				Trace.TraceWarning("RepairFomodInfoCache: " + error);
			}

			var message = string.Format("Checked {0} uncategorized mod(s).{1}Restored info for {2} mod(s) from the legacy cache. If the mod cache was restored the program will automatically restart.",
				targetMods.Count, Environment.NewLine, result.FixedCount);

			if (result.Errors.Count > 0)
			{
				message += string.Format("{0}{0}{1} issue(s) were logged to the trace log.", Environment.NewLine, result.Errors.Count);
			}

			XtraMessageBox.Show(this, message, "Repair FOMOD Info Cache", MessageBoxButtons.OK, MessageBoxIcon.Information);

			if (result.FixedCount > 0)
			{
				var reloadGameModeCommand = ViewModel.ChangeGameModeCommands
					.FirstOrDefault(cmd => ViewModel.GameMode.ModeId.Equals(cmd?.Id, StringComparison.OrdinalIgnoreCase));

				if (reloadGameModeCommand != null)
				{
					reloadGameModeCommand.Execute();
				}
				else
					XtraMessageBox.Show(this, "Unable to restart. Please close and restart manually to complete the cache restore process.", "Repair FOMOD Info Cache", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
		}

		/// <summary>
		/// Automatically sorts the plugin list.
		/// </summary>
		protected void SortPlugins()
		{
			if (ViewModel.SupportsPluginAutoSorting && ViewModel.PluginSorterInitialized)
			{
				ViewModel.SortPlugins();
			}
			else
			{
				XtraMessageBox.Show("Nexus Mod Manager was unable to properly initialize the Automatic Sorting functionality." +
								Environment.NewLine + Environment.NewLine + "This game is not supported or something is wrong with your loadorder.txt or plugins.txt files," +
								Environment.NewLine + "or one or more plugins are corrupt/broken.",
					"Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
		}

		/// <summary>
		/// Disable all active mods.
		/// </summary>
		protected void DisableAllMods()
		{
			_modManagerControl.DisableAllMods(false);
		}

		/// <summary>
		/// Uninstall all active mods.
		/// </summary>
		protected void UninstallAllMods()
		{
			UninstallAllMods(false, false);
		}

		/// <summary>
		/// Purge Loose Files.
		/// </summary>
		protected void PurgeLooseFiles()
		{
			if (ViewModel.UsesPlugins)
			{
				var drPurgeLooseFiles = ExtendedMessageBox.Show(this, "USE THIS FUNCTION AT YOUR OWN RISK: Would you like to clean your game folder from unmanaged files (not installed by NMM and not official game files)? Legit files may be lost if the mod manager doesn't recognize them as official game files.", "Purge Unmanaged Files", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);

				if (drPurgeLooseFiles == DialogResult.Yes)
				{
					ViewModel.PurgeLooseFiles();
				}
			}
		}

		/// <summary>
		/// Adds the backup profile to the profile list.
		/// </summary>
		protected void RestoreBackupProfile()
		{
			if (ViewModel.ProfileManager.RestoreBackupProfile(ViewModel.GameMode.ModeId, out var error) == false)
			{
				XtraMessageBox.Show("Nexus Mod Manager was unable to restore your backup profile." +
					Environment.NewLine + Environment.NewLine + error,
					"Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
			else
			{
				XtraMessageBox.Show(String.Format("{0} has been successfully added to your profile list.", error),
					"Restored", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}

		protected void CreateBackup()
		{
			ViewModel.CreateBackup(this);
		}

		protected void RestoreBackup()
		{
			ViewModel.RestoreBackup(_modManagerControl);
		}

		/// <summary>
		/// Uninstall all active mods.
		/// </summary>
		protected void UninstallAllMods(bool forceUninstall, bool silent)
		{
			_modManagerControl.DeactivateAllMods(forceUninstall, silent);
		}

		/// <summary>
		/// This will show the Virtual folders settings.
		/// </summary>
		protected void ChangeVirtualFolders()
		{
			var vmlSetup = new VirtualDirectoriesSetupVM(ViewModel.EnvironmentInfo, ViewModel.GameMode, ViewModel.ModManager.VirtualModActivator);
			var frmSetup = new VirtualDirectoriesSetupForm(vmlSetup);

			if (frmSetup.ShowDialog(this) == DialogResult.OK)
			{
				if (ViewModel.ProfileManager.CurrentProfile == null)
				{
					byte[] bteLoadOrder = null;

					if (ViewModel.GameMode.UsesPlugins)
					{
						bteLoadOrder = ViewModel.PluginManagerVM.ExportLoadOrder();
					}

					var bteModList = ViewModel.ModManager.InstallationLog.GetXmlModList();
					var bteIniList = ViewModel.ModManager.InstallationLog.GetXmlIniList();
					var intModCount = ViewModel.ModManager.ActiveMods.Count;
					AddNewProfile(bteModList, bteIniList, bteLoadOrder, intModCount, true);

					UninstallAllMods(true, true);

					ViewModel.ModManager.VirtualModActivator.Reset();

					AddNewProfile(bteModList, bteIniList, bteLoadOrder, intModCount, false);
					ViewModel.SwitchProfile(this, ViewModel.ProfileManager.CurrentProfile, true, false);
				}
				else
				{
					var impCurrentProfile = ViewModel.ProfileManager.CurrentProfile;
					ViewModel.ProfileManager.SetCurrentProfile(null);

					UninstallAllMods(true, true);

					ViewModel.ModManager.VirtualModActivator.Reset();

					ViewModel.SwitchProfile(this, impCurrentProfile, true, false);
				}
			}
		}

		private void LoginTask_PropertyChanged(object sender, EventArgs e)
		{
			var authenticationFormTask = (AuthenticationFormTask)sender;

			if (authenticationFormTask.OverallMessage != null && authenticationFormTask.OverallMessage.Contains("Logged in"))
			{
				toolStripLabelLoginMessage.Caption = $"{authenticationFormTask.OverallMessage}{OptionalPremiumMessage}";
				toolStripButtonOnlineStatus.Hint = "Logout";
			}
			else
			{
				toolStripLabelLoginMessage.Caption = authenticationFormTask.OverallMessage;
				toolStripButtonOnlineStatus.Hint = "Login";
			}
		}

		/// <summary>
		/// Opens the selected game folder.
		/// </summary>
		protected void OpenGameFolder()
		{
			if (FileUtil.IsValidPath(ViewModel.GamePath))
			{
				Process.Start(ViewModel.GamePath);
			}
		}

		/// <summary>
		/// Checks if there are any active downloads before closing the mod manager.
		/// </summary>
		/// <remarks>
		/// If there's an active download, the program will ask the user if he really wants to close it.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="FormClosingEventArgs"/> describing the event arguments.</param>
		private void CheckDownloadsOnClosing(object sender, FormClosingEventArgs e)
		{
			if (ViewModel.DownloadMonitorVM.ActiveTasks.Count > 0)
			{
				var drFormClose = XtraMessageBox.Show($"There is an ongoing download, are you sure you want to close {Application.ProductName}?", "Closing", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);

				if (drFormClose != DialogResult.Yes)
				{
					e.Cancel = true;
				}
			}

			if (ViewModel.IsInstalling)
			{
				var drFormClose = XtraMessageBox.Show($"There is an ongoing mod install/uninstall, are you sure you want to close {Application.ProductName}?", "Closing", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);

				if (drFormClose != DialogResult.Yes)
				{
					e.Cancel = true;
				}
			}
		}

		/// <summary>
		/// The Main Form resizeEnd event.
		/// </summary>
		private void MainForm_ResizeEnd(object sender, EventArgs e)
		{
		}

		/// <summary>
		/// The Main Form resizeBegin event.
		/// </summary>
		private void MainForm_ResizeBegin(object sender, EventArgs e)
		{
		}

		/// <summary>
		/// The Main Form resize event.
		/// </summary>
		private void MainForm_Resize(object sender, EventArgs e)
		{
			if (WindowState != LastWindowState)
			{
				LastWindowState = WindowState;
			}
		}

		private async void MainForm_Shown(object sender, EventArgs e)
		{
			ShowEmbeddedDockContents();
			ApplyDefaultMonitorPanelSizes();
			BeginInvoke((MethodInvoker)ActivateModsDocument);
			ModMigrationCheck();
			ShowGameSpecificDisclaimer();
			ConfigFilesCheck();

			if (IsMainDocumentActive(_fileManagerControl))
				await _fileManagerControl.EnsureInitialLoadAsync().ConfigureAwait(true);
		}

		/// <summary>
		/// Updates the Mods Counter
		/// </summary>
		private void MmgModManagerControlUpdateModsCount(object sender, EventArgs e)
		{
			UpdateModsFeedback();
		}

		/// <summary>
		/// Updates the Mods Counter
		/// </summary>
		private void UpdateModsFeedback()
		{
			tlbModsCounter.Caption = "  Total mods: " + ViewModel.ModManagerVM.ManagedMods.Count + "   |   Installed mods: " + ViewModel.ModManager.ActiveMods.Count + "   |   Active mods: " + ViewModel.ModManager.VirtualModActivator.ActiveModList.Count();
		}

		/// <summary>
		/// Updates the Plugins Counter
		/// </summary>
		private void PmcPluginManagerControlUpdatePluginsCount(object sender, EventArgs e)
		{
			toolStripLabelPluginsCounter.Caption = "  Total plugins: " + ViewModel.PluginManagerVM.ManagedPlugins.Count + "   |   Active plugins: ";
			var myFontFamily = new FontFamily(GetBarItemFont(toolStripLabelActivePluginsCounter).Name);

			int limitedPluginsCount = ViewModel.PluginManagerVM.ActivePlugins.Count(x => x != null && !x.IgnoreIndexing);

			if (limitedPluginsCount > ViewModel.PluginManagerVM.MaxAllowedActivePluginsCount)
			{
				var icoIcon = new Icon(SystemIcons.Warning, 16, 16);
				toolStripLabelActivePluginsCounter.ImageOptions.Image = icoIcon.ToBitmap();
				SetBarItemForeColor(toolStripLabelActivePluginsCounter, Color.Red);

				if (myFontFamily.IsStyleAvailable(FontStyle.Bold))
				{
					SetBarItemFontStyle(toolStripLabelActivePluginsCounter, FontStyle.Bold);
				}
				else if (myFontFamily.IsStyleAvailable(FontStyle.Regular))
				{
					SetBarItemFontStyle(toolStripLabelActivePluginsCounter, FontStyle.Regular);
				}

				toolStripLabelActivePluginsCounter.Caption = limitedPluginsCount.ToString() + " (" + ViewModel.PluginManagerVM.ActivePlugins.Count(x => x != null).ToString() + ")";
				toolStripLabelActivePluginsCounter.Hint = $"There may be too many active plugins. {ViewModel.CurrentGameModeName} might not start!"; ;
			}
			else
			{
				toolStripLabelActivePluginsCounter.ImageOptions.Image = null;

				if (myFontFamily.IsStyleAvailable(FontStyle.Regular))
				{
					SetBarItemFontStyle(toolStripLabelActivePluginsCounter, FontStyle.Regular);
				}
				else if (myFontFamily.IsStyleAvailable(FontStyle.Bold))
				{
					SetBarItemFontStyle(toolStripLabelActivePluginsCounter, FontStyle.Bold);
				}

				SetBarItemForeColor(toolStripLabelActivePluginsCounter, Color.Empty);
				toolStripLabelActivePluginsCounter.Caption = limitedPluginsCount.ToString() + " (" + ViewModel.PluginManagerVM.ActivePlugins.Count(x => x != null).ToString() + ")";
			}
		}

		/// <summary>
		/// Schedules the current plugin load order to be saved to the active profile after a plugin ordering change.
		/// </summary>
		/// <param name="sender">The event sender.</param>
		/// <param name="e">The event arguments.</param>
		private void pmcPluginManager_PluginMoved(object sender, EventArgs e)
		{
			if (ViewModel == null ||
				ViewModel.IsSwitching ||
				!ViewModel.GameMode.UsesPlugins ||
				ViewModel.ProfileManager.CurrentProfile == null)
			{
				return;
			}

			ScheduleActivePluginsProfileSave();
		}

		/// <summary>
		/// Set the focus to the Search Textbox.
		/// </summary>
		private void MmgModManagerControlSetTextBoxFocus(object sender, EventArgs e)
		{
			FocusMainFindEditor();
		}

		/// <summary>
		/// The Main Form resetSearchBox event.
		/// </summary>
		private void MmgModManagerControlResetSearchBox(object sender, EventArgs e)
		{
			toolStripTextBoxFind.EditValue = String.Empty;
		}

		/// <summary>
		/// Handles the <see cref="ModManagerControl.UninstallModFromProfiles"/> of the opening
		/// of the ReaMe file.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="ModEventArgs"/> describing the event arguments.</param>
		private void ModManagerControlUninstallModFromProfiles(object sender, ModEventArgs e)
		{
			var mods = new List<IMod> { e.Mod };

			if (ViewModel.ProfileManager != null && ViewModel.ProfileManager.Initialized)
			{
				ViewModel.ProfileManager.PurgeModsFromProfiles(mods);
			}
		}

		/// <summary>
		/// Handles the <see cref="ModManagerControl.UninstalledAllMods"/> of the opening
		/// of the ReaMe file.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="EventArgs"/> describing the event arguments.</param>
		private void MmgModManagerControlUninstalledAllMods(object sender, EventArgs e)
		{
			if (ViewModel.ProfileManager?.CurrentProfile != null)
			{
				ViewModel.ProfileManager.PurgeProfileXMLInstalledFile();
			}
		}

		/// <summary>
		/// Set the focus to the Search Textbox.
		/// </summary>
		private void DmcDownloadMonitorControlSetTextBoxFocus(object sender, EventArgs e)
		{
			if (IsMainDocumentActive((Control)_modManagerControl))
			{
				FocusMainFindEditor();
			}
		}

		/// <summary>
		/// Updates the Bottom Bar Queue Feedback
		/// </summary>
		private void MacModActivationMonitorControlUpdateBottomBarFeedback(object sender, EventArgs e)
		{
			UpgradeBottomBarFeedbackCounter();

			if (sender != null)
			{
				if (ViewModel.IsInstalling)
				{
					IBackgroundTaskSet task = null;
					if (sender is ModActivationMonitorRow row)
						task = row.Task;
					else if (sender is ModActivationMonitorListViewItem listViewItem)
						task = listViewItem.Task;

					if (task != null)
					{
						SetBarItemVisible(toolStripButtonLoader, true);
						SetBarItemVisible(toolStripLabelBottomBarFeedbackCounter, true);

						if (!task.IsQueued)
						{
							if (task.GetType() == typeof(ModInstaller))
							{
								toolStripLabelBottomBarFeedback.Caption = "Mod Activation: Installing ";
							}
							else if (task.GetType() == typeof(ModUninstaller))
							{
								toolStripLabelBottomBarFeedback.Caption = "Mod Activation: Uninstalling ";
							}
							else if (task.GetType() == typeof(ModUpgrader))
							{
								toolStripLabelBottomBarFeedback.Caption = "Mod Activation: Upgrading ";
							}
						}
					}
					else
					{
						toolStripLabelBottomBarFeedback.Caption = "Idle";
						SetBarItemVisible(toolStripButtonLoader, false);
					}
				}
				else
				{
					SetBarItemVisible(toolStripButtonLoader, false);
					SetBarItemVisible(toolStripLabelBottomBarFeedbackCounter, false);
					toolStripLabelBottomBarFeedback.Caption = "Idle";
				}
			}
		}

		/// <summary>
		/// Updates the Bottom Bar Queue Counter
		/// </summary>
		private void UpgradeBottomBarFeedbackCounter()
		{
			var intCompletedTasks = _modActivationMonitorControl.ViewModel.Tasks.Count(x => x.IsCompleted);

			if (_modActivationMonitorControl.ViewModel.Tasks.Count == 0)
			{
				toolStripLabelBottomBarFeedbackCounter.Caption = "";
				toolStripLabelBottomBarFeedback.Caption = "";
				SetBarItemVisible(toolStripButtonLoader, false);
			}
			else
			{
				toolStripLabelBottomBarFeedbackCounter.Caption = $"({intCompletedTasks}/{_modActivationMonitorControl.ViewModel.Tasks.Count})";
			}
		}

		/// <summary>
		/// Opens NMM's mods folder for the current game.
		/// </summary>
		protected void OpenModsFolder()
		{
			if (FileUtil.IsValidPath(ViewModel.ModsPath))
			{
				Process.Start(ViewModel.ModsPath);
			}
		}

		/// <summary>
		/// Opens NMM's cache folder for the current game.
		/// </summary>
		protected void OpenCacheFolder()
		{
			if (FileUtil.IsValidPath(ViewModel.CachePath))
			{
				Process.Start(ViewModel.CachePath);
			}
		}

		/// <summary>
		/// The Find KeyUp event.
		/// </summary>
		private void tstFind_KeyUp(object sender, KeyEventArgs e)
		{
			_modManagerControl.FindItemWithText(MainFindText);
		}

		/// <summary>
		/// Opens NMM's install info folder for the current game.
		/// </summary>
		protected void OpenInstallFolder()
		{
			if (FileUtil.IsValidPath(ViewModel.InstallInfoPath))
			{
				Process.Start(ViewModel.InstallInfoPath);
			}
		}

		/// <summary>
		/// Opens NMM's config folder.
		/// </summary>
		protected void OpenConfigFolder()
		{
			if (FileUtil.IsValidPath(ViewModel.ConfigPath))
			{
				Process.Start(ViewModel.ConfigPath);
			}
		}

		#region Binding Helpers

		/// <summary>
		/// Binds the commands to the UI.
		/// </summary>
		protected void BindCommands()
		{
			ViewModel.Updating -= ViewModel_Updating;
			ViewModel.Updating += ViewModel_Updating;
			BindExistingBarItem(tsbUpdate, ViewModel.UpdateCommand);

			ViewModel.ToggleLoginCommand.BeforeExecute -= LogoutCommand_BeforeExecute;
			ViewModel.ToggleLoginCommand.BeforeExecute += LogoutCommand_BeforeExecute;
			BindExistingBarItem(toolStripButtonOnlineStatus, ViewModel.ToggleLoginCommand);

			BindLaunchCommands();
			BindProfileCommands();
			BindSupportedToolsCommands();
			BindToolCommands();
			BindFolderCommands();
			BindChangeModeCommands();
		}

		#region Logout

		/// <summary>
		/// Handles the <see cref="Command.BeforeExecute"/> event of the logout command.
		/// </summary>
		/// <remarks>
		/// This confirms whether the user wants to logout.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="CancelEventArgs"/> describing the event arguments.</param>
		private void LogoutCommand_BeforeExecute(object sender, CancelEventArgs e)
		{
			if (!ViewModel.OfflineMode)
			{
				if (ExtendedMessageBox.Show(this, "Do you want to logout? This will require you to authorize NMM again the next time you try to log in.", "Logout", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
				{
					e.Cancel = true;
				}
			}
		}

		#endregion

		#region Change Game Mode

		/// <summary>
		/// Handles the <see cref="Command.Executed"/> event of the change game mode command.
		/// </summary>
		/// <remarks>
		/// This closes the application.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="EventArgs"/> describing the event arguments.</param>
		private void ChangeGameModeCommand_Executed(object sender, EventArgs e)
		{
			Close();
		}

		#endregion

		#region Tasks

		/// <summary>
		/// Handles the <see cref="INotifyCollectionChanged.CollectionChanged"/> event of the view model's
		/// active mod list.
		/// </summary>
		/// <remarks>
		/// This updates the list of mods to refelct changes to which mods are active.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="NotifyCollectionChangedEventArgs"/> describing the event arguments.</param>
		private void ActivePlugins_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, NotifyCollectionChangedEventArgs>)ActivePlugins_CollectionChanged, sender, e);
				return;
			}

			ScheduleActivePluginsProfileSave();
		}

		private void ScheduleActivePluginsProfileSave()
		{
			DeferActivePluginsProfileSave();
		}

		private void DeferActivePluginsProfileSave()
		{
			_activePluginsProfileSavePending = true;
			_activePluginsProfileSaveTimer.Stop();
			_activePluginsProfileSaveTimer.Start();
		}

		private void VirtualModActivator_VirtualStoreMutationEnded(object sender, EventArgs e)
		{
			if (InvokeRequired)
			{
				BeginInvoke((MethodInvoker)(() => VirtualModActivator_VirtualStoreMutationEnded(sender, e)));
				return;
			}

			if (_activePluginsProfileSavePending)
				DeferActivePluginsProfileSave();
		}

		private void ActivePluginsProfileSaveTimer_Tick(object sender, EventArgs e)
		{
			_activePluginsProfileSaveTimer.Stop();
			SaveActivePluginsToCurrentProfile();
		}

		private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
		{
			_activePluginsProfileSaveTimer.Stop();
			SaveActivePluginsToCurrentProfile();
		}

		private void SaveActivePluginsToCurrentProfile()
		{
			if (!_activePluginsProfileSavePending)
				return;

			if (ShouldDeferActivePluginsProfileSave())
			{
				DeferActivePluginsProfileSave();
				return;
			}

			_activePluginsProfileSavePending = false;

			if (ViewModel.ProfileManager.CurrentProfile != null)
			{
				string[] strOptionalFiles = null;

				if (ViewModel.GameMode.UsesPlugins)
				{
					if (ViewModel.GameMode.RequiresOptionalFilesCheckOnProfileSwitch)
					{
						if (ViewModel.PluginManager?.ActivePlugins != null && ViewModel.PluginManager.ActivePlugins.Count > 0)
						{
							strOptionalFiles = ViewModel.GameMode.GetOptionalFilesList(ViewModel.PluginManager.ActivePlugins.Where(p => p != null).Select(x => x.Filename).ToArray());
						}
					}

					try
					{
						var bteLoadOrder = ViewModel.PluginManagerVM.ExportLoadOrder();
						ViewModel.ProfileManager.UpdateProfile(ViewModel.ProfileManager.CurrentProfile, null, bteLoadOrder, strOptionalFiles, out var strError);

						if (!string.IsNullOrEmpty(strError))
						{
							strError = strError + Environment.NewLine + Environment.NewLine + "Unable to automatically save the profile file, please close the program blocking the reported file and manually click on Save Profile from the profiles context menu";
							XtraMessageBox.Show(strError, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
						}
					}
					catch (SQLiteException ex)
					{
						if (!IsTransientSQLiteBusy(ex))
							throw;

						Trace.TraceWarning("Deferred automatic profile save because the virtual mod SQLite store is busy: {0}", ex.Message);
						DeferActivePluginsProfileSave();
					}
					catch (IOException ex)
					{
						string strError = ex.Message + Environment.NewLine + Environment.NewLine + "Unable to automatically save the profile file, please close the program blocking the reported file and manually click on Save Profile from the profiles context menu";
						XtraMessageBox.Show(strError, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					}
				}
			}
		}

		private bool ShouldDeferActivePluginsProfileSave()
		{
			if (ViewModel == null)
				return false;

			if (ViewModel.IsInstalling || ViewModel.IsSwitching)
				return true;

			return ViewModel.VirtualModActivator != null && ViewModel.VirtualModActivator.IsVirtualStoreMutationInProgress;
		}

		private static bool IsTransientSQLiteBusy(SQLiteException ex)
		{
			return ex.ResultCode == SQLiteErrorCode.Busy || ex.ResultCode == SQLiteErrorCode.Locked;
		}

		/// <summary>
		/// Handles the <see cref="ModRepository.UserStatusUpdate"/> event of the tasks list.
		/// </summary>
		/// <remarks>
		/// Updates the UI elements.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void ModRepository_UserStatusUpdate(object sender, EventArgs e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs>)ModRepository_UserStatusUpdate, sender, e);
				return;
			}

			UserStatusFeedback();
		}

		/// <summary>
		/// Handles the <see cref="INotifyCollectionChanged.CollectionChanged"/> event of the tasks list.
		/// </summary>
		/// <remarks>
		/// Displays the activity monitor.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="NotifyCollectionChangedEventArgs"/> describing the event arguments.</param>
		private void Tasks_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, NotifyCollectionChangedEventArgs>)Tasks_CollectionChanged, sender, e);
				return;
			}

			ShowDownloadMonitorPanel();

			if (!ViewModel.OfflineMode)
			{
				toolStripLabelDownloads.Caption = String.Format("{0} ({1} {2}) ", toolStripLabelDownloads.Tag, _downloadMonitorControl.ViewModel.ActiveTasks.Count, _downloadMonitorControl.ViewModel.ActiveTasks.Count == 1 ? "File" : "Files");
				if (_downloadMonitorControl.ViewModel.ActiveTasks.Count <= 0)
				{
					UpdateProgressBarSpeed("TotalSpeed", true);
				}
			}
		}

		/// <summary>
		/// Handles the <see cref="INotifyCollectionChanged.CollectionChanged"/> event of the active tasks list.
		/// </summary>
		/// <remarks>
		/// Displays the activity monitor.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="NotifyCollectionChangedEventArgs"/> describing the event arguments.</param>
		private void ActiveTasks_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, NotifyCollectionChangedEventArgs>)ActiveTasks_CollectionChanged, sender, e);
				return;
			}

			ShowDownloadMonitorPanel();

			if (!ViewModel.OfflineMode)
			{
				if (e.OldItems != null && e.OldItems.Count > 0)
				{
					foreach (AddModTask Task in e.OldItems)
						if (!String.IsNullOrEmpty(Task.ErrorCode) && Task.ErrorCode == "666" && !(Task.Status == BackgroundTasks.TaskStatus.Cancelling || Task.Status == BackgroundTasks.TaskStatus.Cancelled || Task.Status == BackgroundTasks.TaskStatus.Complete))
						{
							XtraMessageBox.Show(String.Format("The NMM web services have currently been disabled by staff of the sites."
								+ " This is NOT an error with NMM and you DO NOT need to report this error to us."
								+ " This is normally a temporary problem so please try again a bit later on in the day." + Environment.NewLine
								+ "If the staff have provided a reason for this down time we'll display it below: {0}", Environment.NewLine + Environment.NewLine + Task.ErrorInfo), "Warning", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
						}
				}
				toolStripLabelDownloads.Caption = String.Format("{0} ({1} {2}) ", toolStripLabelDownloads.Tag, _downloadMonitorControl.ViewModel.ActiveTasks.Count, _downloadMonitorControl.ViewModel.ActiveTasks.Count == 1 ? "File" : "Files");
				if (_downloadMonitorControl.ViewModel.ActiveTasks.Count <= 0)
					UpdateProgressBarSpeed("TotalSpeed", true);
			}
		}

		/// <summary>
		/// Handles the <see cref="System.ComponentModel.ProgressChangedEventHandler"/> event of the active tasks list.
		/// </summary>
		/// <remarks>
		/// Checks the current downloading speed.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="System.ComponentModel.PropertyChangedEventArgs"/> describing the event arguments.</param>
		private void ActiveTasks_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, System.ComponentModel.PropertyChangedEventArgs>)ActiveTasks_PropertyChanged, sender, e);
				return;
			}

			UpdateProgressBarSpeed(e.PropertyName, false);
		}

		/// <summary>
		/// Checks if the downloading speed progress bar needs to be updated.
		/// </summary>
		/// <param name="PropertyName">The property name.</param>
		/// <param name="OverrideSpeed">If true the speed value is overridden with a 0.</param>
		private void UpdateProgressBarSpeed(string PropertyName, bool OverrideSpeed)
		{
			if (toolStripProgressBarDownloadSpeed?.IsValid == true && (PropertyName == "TotalSpeed" || PropertyName == "TotalProgress"))
			{
				if (OverrideSpeed)
				{
					toolStripProgressBarDownloadSpeed.Value = 0;

					if (toolStripProgressBarDownloadSpeed.ColorFillMode == DownloadProgressBarItem.FillType.Fixed)
					{
						toolStripProgressBarDownloadSpeed.Maximum = 1;
					}

					toolStripProgressBarDownloadSpeed.Visible = false;
				}
				else switch (toolStripProgressBarDownloadSpeed.ColorFillMode)
					{
						case DownloadProgressBarItem.FillType.Fixed:
							toolStripProgressBarDownloadSpeed.Visible = true;
							toolStripProgressBarDownloadSpeed.Maximum = _downloadMonitorControl.ViewModel.TotalSpeed > 0 ? _downloadMonitorControl.ViewModel.TotalSpeed : 1;
							toolStripProgressBarDownloadSpeed.Value = toolStripProgressBarDownloadSpeed.Maximum;
							break;
						case DownloadProgressBarItem.FillType.Ascending:
							{
								toolStripProgressBarDownloadSpeed.Visible = true;

								if (_downloadMonitorControl.ViewModel.TotalMaxProgress > 0)
								{
									toolStripProgressBarDownloadSpeed.Value = Convert.ToInt32(Convert.ToSingle(_downloadMonitorControl.ViewModel.TotalProgress) / Convert.ToSingle(_downloadMonitorControl.ViewModel.TotalMaxProgress) * 100);
									toolStripProgressBarDownloadSpeed.OptionalValue = _downloadMonitorControl.ViewModel.TotalSpeed;
								}

								break;
							}
						case DownloadProgressBarItem.FillType.Descending:
							{
								toolStripProgressBarDownloadSpeed.Visible = true;
								// Disabled for the time being since there's currently no way to check whether an user is browsing the Nexus with an active adblocker
								toolStripProgressBarDownloadSpeed.Value = _downloadMonitorControl.ViewModel.TotalSpeed <= 1024 ? _downloadMonitorControl.ViewModel.TotalSpeed : (ViewModel.UserStatus.IsSupporter ? 2048 : 2048);
								break;
							}
					}
			}
		}

		#endregion

		#endregion

		#region Control Metrics Serialization

		/// <summary>
		/// Raises the <see cref="Form.Closed"/> event of the form.
		/// </summary>
		/// <remarks>
		/// This saves the form's metrics.
		/// </remarks>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnClosed(EventArgs e)
		{
			if (!DesignMode)
			{
				SaveMainDockingLayout();
				ViewModel.EnvironmentInfo.Settings.Save();
			}

			base.OnClosed(e);
		}

		#endregion

		#region Maintenance Binding Helpers

		/// <summary>
		/// Handles the <see cref="MainFormVM.Updating"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays the progress dialog.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ViewModel_Updating(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_Updating, sender, e);
				return;
			}

			ProgressDialog.ShowDialog(this, e.Argument);
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the setting button.
		/// </summary>
		/// <remarks>Displays the settings form.</remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void tsbSettings_Click(object sender, EventArgs e)
		{
			var frmSettings = new SettingsForm(ViewModel.SettingsFormVM);

			if (frmSettings.ShowDialog(this) == DialogResult.OK)
			{
				_modManagerControl.ForceListRefresh();

				if (ViewModel.SupportedToolsLauncher != null)
				{
					ViewModel.SupportedToolsLauncher.SetupCommands();
					BindSupportedToolsCommands();
				}
			}
		}

		/// <summary>
		/// This asks the user to confirm an updater action.
		/// </summary>
		/// <param name="p_strMessage">The message describing the action to confirm.</param>
		/// <param name="p_strTitle">The title of the action to confirm.</param>
		/// <returns><c>true</c> if the action has been confirmed;
		/// <c>false</c> otherwise.</returns>
		private bool ConfirmUpdaterAction(string p_strMessage, string p_strTitle)
		{
			if (InvokeRequired)
			{
				return (bool)Invoke((ConfirmActionMethod)ConfirmUpdaterAction, p_strMessage, p_strTitle);
			}

			return XtraMessageBox.Show(this, p_strMessage, p_strTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK;
		}

		#endregion

		#region Change Game Mode Binding Helpers

		/// <summary>
		/// Binds the change game mode commands to the UI.
		/// </summary>
		protected void BindChangeModeCommands()
		{
			ClearTransientPopupItems(popupChangeMode);
			foreach (Command previouslyBoundCommand in _changeModeCommandsWithExecutedHandler)
				previouslyBoundCommand.Executed -= ChangeGameModeCommand_Executed;
			_changeModeCommandsWithExecutedHandler.Clear();

			IEnumerable<Command> commands = ViewModel.ChangeGameModeCommands
				.OrderByDescending(command => ViewModel.GameMode.ModeId.Equals(command?.Id, StringComparison.OrdinalIgnoreCase));
			bool addedReloadCommand = false;

			foreach (Command changeCommand in commands)
			{
				bool isReloadCommand = ViewModel.GameMode.ModeId.Equals(changeCommand?.Id, StringComparison.OrdinalIgnoreCase);
				changeCommand.Executed += ChangeGameModeCommand_Executed;
				_changeModeCommandsWithExecutedHandler.Add(changeCommand);
				BarButtonItem item = CreateCommandBarButton(changeCommand);
				if (isReloadCommand)
				{
					item.Caption = $"Reload {changeCommand.Name}";
					item.Hint = $"Reload {changeCommand.Name}";
				}
				BarItemLink link = popupChangeMode.AddItem(item);

				if (!isReloadCommand && addedReloadCommand)
				{
					link.BeginGroup = true;
					addedReloadCommand = false;
				}
				else if (changeCommand.Name.Equals("Change Default Game...", StringComparison.OrdinalIgnoreCase))
				{
					link.BeginGroup = true;
				}

				if (isReloadCommand)
					addedReloadCommand = true;
			}
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the change game mode button.
		/// </summary>
		/// <remarks>
		/// This displays the list of game modes when the button is clicked.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void spbChangeMode_ButtonClick(object sender, EventArgs e)
		{
			popupChangeMode.ShowPopup(Control.MousePosition);
		}

		#endregion

		#region Tools Binding Helpers

		/// <summary>
		/// Binds the tool launch commands to the UI.
		/// </summary>
		protected void BindToolCommands()
		{
			ClearTransientPopupItems(popupTools);
			foreach (ITool previouslyBoundTool in _boundGameTools)
			{
				previouslyBoundTool.DisplayToolView -= Tool_DisplayToolView;
				previouslyBoundTool.CloseToolView -= Tool_CloseToolView;
			}
			_boundGameTools.Clear();

			Command resetUiCommand = new Command("Reset UI", "Resets the UI to the default layout.", ResetUI);
			popupTools.AddItem(CreateCommandBarButton(resetUiCommand));

			Command repairFomodInfoCacheCommand = new Command("Repair FOMOD Info Cache", "Restores mod info (name, version, description) for uncategorized mods from the legacy FOMOD cache, where available.", RepairFomodInfoCache);
			popupTools.AddItem(CreateCommandBarButton(repairFomodInfoCacheCommand));

			Command disableAllModsCommand = new Command("Disable all active mods", "Disables all active mods.", DisableAllMods);
			popupTools.AddItem(CreateCommandBarButton(disableAllModsCommand, Properties.Resources.edit_delete));

			Command uninstallAllModsCommand = new Command("Uninstall all active mods", "Uninstalls all active mods.", UninstallAllMods);
			popupTools.AddItem(CreateCommandBarButton(uninstallAllModsCommand, Properties.Resources.edit_delete_6));

			Command purgeLooseFilesCommand = new Command("Purge Unmanaged Files", "Purge Unmanaged Files.", PurgeLooseFiles);
			popupTools.AddItem(CreateCommandBarButton(purgeLooseFilesCommand, Properties.Resources.deleteProfile));

			BarSubItem backupMenu = new BarSubItem(barManagerMain, "Backup and Restore");
			backupMenu.ImageOptions.Image = ScaleBarImage(Properties.Resources.backup, StatusBarImageSize);
			Command createBackupCommand = new Command("Create Mod Installation backup.", "Create Mod Installation backup.", CreateBackup);
			Command restoreBackupCommand = new Command("Restore Mod Installation backup", "Restore Mod Installation backup.", RestoreBackup);
			Command restoreBackupProfileCommand = new Command("Restore the backup profile", "Adds the backup profile to the profile list.", RestoreBackupProfile);
			backupMenu.AddItem(CreateCommandBarButton(createBackupCommand, Properties.Resources.createBackup));
			backupMenu.AddItem(CreateCommandBarButton(restoreBackupCommand, Properties.Resources.restoreBackup));
			backupMenu.AddItem(CreateCommandBarButton(restoreBackupProfileCommand, Properties.Resources.change_game_mode));
			popupTools.AddItem(backupMenu);

			Command configureVirtualFoldersCommand = new Command("Change Virtual folders...", "Virtual folders setup menu.", ChangeVirtualFolders);
			popupTools.AddItem(CreateCommandBarButton(configureVirtualFoldersCommand, Properties.Resources.category_folder));

			if (ViewModel.UsesPlugins && ViewModel.SupportsPluginAutoSorting)
			{
				Command sortPluginsCommand = new Command("Automatic Plugin Sorting", "Automatically sorts the plugin list.", SortPlugins);
				popupTools.AddItem(CreateCommandBarButton(sortPluginsCommand));
			}

			foreach (ITool tool in ViewModel.GameToolLauncher.Tools)
			{
				BarButtonItem toolItem = CreateCommandBarButton(tool.LaunchCommand);
				toolItem.Tag = tool;
				tool.DisplayToolView += Tool_DisplayToolView;
				tool.CloseToolView += Tool_CloseToolView;
				_boundGameTools.Add(tool);
				popupTools.AddItem(toolItem);
			}
		}

		private void SupportedTools_ChangedToolPath(object sender, EventArgs e)
		{
			ViewModel.SupportedToolsLauncher.SetupCommands();
			BindSupportedToolsCommands();
		}

		/// <summary>
		/// Handles the <see cref="ITool.CloseToolView"/> event of a tool.
		/// </summary>
		/// <remarks>
		/// This closes the tool's view.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="DisplayToolViewEventArgs"/> describing the event arguments.</param>
		private void Tool_CloseToolView(object sender, DisplayToolViewEventArgs e)
		{
			((Form)e.ToolView).Close();
		}

		/// <summary>
		/// Handles the <see cref="ITool.DisplayToolView"/> event of a tool.
		/// </summary>
		/// <remarks>
		/// This shows the tool's view.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="DisplayToolViewEventArgs"/> describing the event arguments.</param>
		private void Tool_DisplayToolView(object sender, DisplayToolViewEventArgs e)
		{
			if (e.IsModal)
			{
				((Form)e.ToolView).ShowDialog(this);
			}
			else
			{
				((Form)e.ToolView).Show(this);
			}
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the tools button.
		/// </summary>
		/// <remarks>
		/// This displays the list of tools when the button is clicked.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void spbTools_ButtonClick(object sender, EventArgs e)
		{
			popupTools.ShowPopup(Control.MousePosition);
		}

		#endregion

		#region Open Folders Helpers

		/// <summary>
		/// Binds the tool launch commands to the UI.
		/// </summary>
		protected void BindFolderCommands()
		{
			ClearTransientPopupItems(popupFolders);

			Command cmdGameFolder = new Command("Open Game Folder", "Open the game's root folder in the explorer window.", OpenGameFolder);
			Command cmdModsFolder = new Command("Open NMM's Mods Folder", "Open NMM's mods folder in the explorer window.", OpenModsFolder);
			Command cmdCacheFolder = new Command("Open NMM's Cache Folder", "Open NMM's cache folder in the explorer window.", OpenCacheFolder);
			Command cmdInstallFolder = new Command("Open NMM's Install Info Folder", "Open NMM's install info folder in the explorer window.", OpenInstallFolder);
			Command cmdConfigFolder = new Command("Open NMM's Config Folder", "Open NMM's config in the explorer window.", OpenConfigFolder);

			popupFolders.AddItem(CreateCommandBarButton(cmdGameFolder));
			popupFolders.AddItem(CreateCommandBarButton(cmdModsFolder));
			popupFolders.AddItem(CreateCommandBarButton(cmdCacheFolder));
			popupFolders.AddItem(CreateCommandBarButton(cmdInstallFolder));
			popupFolders.AddItem(CreateCommandBarButton(cmdConfigFolder));
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the tools button.
		/// </summary>
		/// <remarks>
		/// This displays the list of tools when the button is clicked.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void spbFolders_ButtonClick(object sender, EventArgs e)
		{
			popupFolders.ShowPopup(Control.MousePosition);
		}

		#endregion

		#region Help Links Binding Helpers

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the help button.
		/// </summary>
		/// <remarks>
		/// This displays the list of help items when the button is clicked.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void spbHelp_ButtonClick(object sender, EventArgs e)
		{
			popupHelp.ShowPopup(Control.MousePosition);
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the help links.
		/// </summary>
		/// <remarks>
		/// This launches the link in the user's browser.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void HelpItem_ItemClick(object sender, ItemClickEventArgs e)
		{
			HelpInformation.HelpLink helpLink = e.Item.Tag as HelpInformation.HelpLink;
			if (helpLink == null)
				return;

			try
			{
				Process.Start(helpLink.Url);
			}
			catch (Win32Exception)
			{
				XtraMessageBox.Show(this, "Cannot find program to open: " + helpLink.Url, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				Trace.WriteLine("Cannot find program to open: " + helpLink.Url);
			}
		}

		#endregion

		#region Task Set Handling

		/// <summary>
		/// Handles the <see cref="MainFormVM.ProfileSwitching"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays the progress dialog.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ViewModel_ProfileSwitching(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_ProfileSwitching, sender, e);
				return;
			}

			DialogResult drProfileSwitch = ProgressDialog.ShowDialog(this, e.Argument, false);
			if (drProfileSwitch != DialogResult.OK || e.Argument.Status != BackgroundTasks.TaskStatus.Complete)
			{
				HandleFailedProfileSwitch(GetBackgroundTaskError(e.Argument, "The selected profile could not be activated."));
				return;
			}

			if (!ViewModel.WaitForPendingLoadOrderWrites(out string strWriteError))
			{
				HandleFailedProfileSwitch(strWriteError);
				return;
			}

			if (ViewModel.GameMode.UsesPlugins)
			{
				IBackgroundTask bgtLoadOrder = ViewModel.ApplyPendingProfileLoadOrder();
				if (bgtLoadOrder != null)
				{
					DialogResult drLoadOrder = ProgressDialog.ShowDialog(this, bgtLoadOrder, false);
					if (drLoadOrder != DialogResult.OK || bgtLoadOrder.Status != BackgroundTasks.TaskStatus.Complete)
					{
						HandleFailedProfileSwitch(GetBackgroundTaskError(bgtLoadOrder, "The profile plugin state is invalid and could not be applied."));
						return;
					}

					if (!ViewModel.WaitForPendingLoadOrderWrites(out strWriteError))
					{
						HandleFailedProfileSwitch(strWriteError);
						return;
					}
				}
			}

			ViewModel.CommitProfileSwitch();
			ViewModel.ModManager.VirtualModActivator.RestoreIniEdits();

			var strOptionalToolPath = ViewModel.GameMode.PostProfileSwitchTool(out var message);

			if (!string.IsNullOrEmpty(strOptionalToolPath) && File.Exists(strOptionalToolPath) && ExtendedMessageBox.Show(this, message, "Optional tool detected", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
			{
				ViewModel.GameMode.SupportedToolsLauncher.LaunchDefaultCommand();
			}

			ViewModel.IsSwitching = false;
			ViewModel.ProfileManager.UpdateProfile(ViewModel.ProfileManager.CurrentProfile, null, null, null, out _);
			ViewModel.ProfileManager.SetDefaultProfile(ViewModel.ProfileManager.CurrentProfile);
			ViewModel.ProfileManager.SaveConfig();
			_modManagerControl.ForceListRefresh();
			BindProfileCommands();
			UpdateModsFeedback();

			if (e.Argument?.ReturnValue is bool && (bool)e.Argument.ReturnValue)
			{
				XtraMessageBox.Show("Restore Complete! NMM will restart automatically to apply the changes.", "Restore Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
				ViewModel.RequestGameMode(ViewModel.GameMode.ModeId);
				ChangeGameModeCommand_Executed(sender, new EventArgs());
			}
		}

		/// <summary>
		/// Rolls back a failed profile switch and reports whether the previous state was restored completely.
		/// </summary>
		/// <param name="p_strFailureMessage">The failure that caused the profile switch to abort.</param>
		private void HandleFailedProfileSwitch(string p_strFailureMessage)
		{
			List<string> lstRollbackErrors = new List<string>();
			IBackgroundTask bgtRollback = ViewModel.RollbackProfileSwitch();

			if (bgtRollback != null)
			{
				DialogResult drRollback = ProgressDialog.ShowDialog(this, bgtRollback, false);
				if (drRollback != DialogResult.OK || bgtRollback.Status != BackgroundTasks.TaskStatus.Complete)
					lstRollbackErrors.Add(GetBackgroundTaskError(bgtRollback, "The previous profile's deployed files could not be fully restored."));
			}

			if (!ViewModel.WaitForPendingLoadOrderWrites(out string strRollbackWriteError))
				lstRollbackErrors.Add(strRollbackWriteError);

			if (ViewModel.GameMode.UsesPlugins)
			{
				IBackgroundTask bgtPreviousLoadOrder = ViewModel.ApplyPreviousProfileLoadOrder();
				if (bgtPreviousLoadOrder != null)
				{
					DialogResult drPreviousLoadOrder = ProgressDialog.ShowDialog(this, bgtPreviousLoadOrder, false);
					if (drPreviousLoadOrder != DialogResult.OK || bgtPreviousLoadOrder.Status != BackgroundTasks.TaskStatus.Complete)
						lstRollbackErrors.Add(GetBackgroundTaskError(bgtPreviousLoadOrder, "The previous plugin state could not be fully restored."));
				}

				if (!ViewModel.WaitForPendingLoadOrderWrites(out strRollbackWriteError))
					lstRollbackErrors.Add(strRollbackWriteError);
			}

			ViewModel.CompleteProfileRollback();
			_modManagerControl.ForceListRefresh();
			BindProfileCommands();
			UpdateModsFeedback();

			bool booRollbackSucceeded = lstRollbackErrors.Count == 0;
			string strResult = booRollbackSucceeded
				? "The previous profile was restored."
				: "NMM could not fully restore the previous profile. Review the active mods and plugins before launching the game.";

			if (!booRollbackSucceeded)
				strResult += Environment.NewLine + Environment.NewLine + String.Join(Environment.NewLine, lstRollbackErrors.Where(x => !String.IsNullOrWhiteSpace(x)).Distinct());

			XtraMessageBox.Show(
				(String.IsNullOrWhiteSpace(p_strFailureMessage) ? "The profile switch failed." : p_strFailureMessage) + Environment.NewLine + Environment.NewLine + strResult,
				"Profile switch failed",
				MessageBoxButtons.OK,
				MessageBoxIcon.Warning);
		}

		/// <summary>
		/// Gets a useful error message from a failed background task.
		/// </summary>
		/// <param name="p_bgtTask">The failed task.</param>
		/// <param name="p_strFallback">The fallback message.</param>
		/// <returns>The task error message or the fallback text.</returns>
		private static string GetBackgroundTaskError(IBackgroundTask p_bgtTask, string p_strFallback)
		{
			Exception expFailure = p_bgtTask == null ? null : p_bgtTask.ReturnValue as Exception;
			return expFailure == null || String.IsNullOrWhiteSpace(expFailure.Message)
				? p_strFallback
				: expFailure.Message;
		}

		/// <summary>
		/// Handles the <see cref="MainFormVM.ProfileDownloading"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays the progress dialog.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ViewModel_ProfileDownloading(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_ProfileDownloading, sender, e);
				return;
			}

			ProgressDialog.ShowDialog(this, e.Argument, true);
		}

		/// <summary>
		/// Handles the <see cref="MainFormVM.ConfigFilesFixing"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays the progress dialog.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ViewModel_ConfigFilesFixing(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_ConfigFilesFixing, sender, e);
				return;
			}

			ProgressDialog.ShowDialog(this, e.Argument, true);

			if (e.Argument.ReturnValue != null)
			{
				if (e.Argument.ReturnValue.GetType() == typeof(ModProfile))
				{
					IModProfile ModProfile = (IModProfile)e.Argument.ReturnValue;
					ViewModel.SwitchProfile(this, ModProfile, false, false);
				}
			}
		}

		#region DEPRECATED: Profile Sharing is no longer supported

		/// <summary>
		/// Handles the <see cref="MainFormVM.ProfileDownloading"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays the progress dialog.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ViewModel_CheckingOnlineProfileIntegrity(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_CheckingOnlineProfileIntegrity, sender, e);
				return;
			}

			ProgressDialog.ShowDialog(this, e.Argument, true);

			Dictionary<string, string> missingInfoDictionary;

			if (e.Argument.ReturnValue != null)
			{
				var error = e.Argument.ReturnValue.ToString();

				if (e.Argument.ReturnValue is string)
				{
					ExtendedMessageBox.Show(this, error, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}

				missingInfoDictionary = (Dictionary<string, string>)e.Argument.ReturnValue;

				if (!missingInfoDictionary.Any())
				{
					ViewModel.ExecuteProfileSwitch(this);
					return;
				}
			}
			else
			{
				ViewModel.ExecuteProfileSwitch(this);
				return;
			}

			var sbMessage = new StringBuilder();
			var sbDetails = new StringBuilder();

			sbMessage.AppendLine("Some mods required by the profile are missing: ");

			var tslMissingMods = new ThreadSafeObservableList<string>();
			var intNewVersions = 0;
			var intMissing = 0;

			foreach (var kvp in missingInfoDictionary)
			{
				var value = kvp.Value;

				if (!string.IsNullOrEmpty(value) && value.Contains("@"))
				{
					intNewVersions++;
					tslMissingMods.Add(value.Substring(1));
					sbDetails.AppendLine($"MISMATCHED: {value}#{kvp.Key}");
				}
				else if (string.IsNullOrEmpty(value))
				{
					intMissing++;
					sbDetails.AppendLine($"MISSING: {kvp.Key}");
				}
				else
				{
					tslMissingMods.Add(value);
				}
			}

			var lstMissingMods = new List<string>();
			var lstIncompleteMods = new List<string>();

			var strKey = string.Empty;

			foreach (var url in tslMissingMods)
			{
				if (missingInfoDictionary.ContainsValue(url))
				{
					strKey = missingInfoDictionary.FirstOrDefault(x => x.Value == url).Key;
				}

				var booCheck = ViewModel.CheckAlreadyDownloading(url, strKey);

				if (booCheck == false)
				{
					lstMissingMods.Add(url);
				}
				else if (booCheck == null)
				{
					lstIncompleteMods.Add(url);
				}
			}

			if (lstMissingMods.Count <= 0 && lstIncompleteMods.Count <= 0)
			{
				ExtendedMessageBox.Show(this, "The mod files required by this profile are still being downloaded, please wait for the downloads to complete before activating this profile.", "Please wait..", null, MessageBoxButtons.OK, MessageBoxIcon.Information);

				if (ViewModel.ProfileManager.CurrentProfile != null)
				{
					ViewModel.ProfileManager.SetCurrentProfile(ViewModel.ProfileManager.CurrentProfile);
				}

				BindProfileCommands();
				return;
			}
			else if (lstMissingMods.Count <= 0 && lstIncompleteMods.Count > 0)
			{
				var sbIncomplete = new StringBuilder();

				foreach (var File in lstIncompleteMods)
				{
					sbIncomplete.AppendLine(File);
				}

				var strIncomplete = sbIncomplete.ToString();
				var drIncomplete = ExtendedMessageBox.Show(this, "Some mods required by this profile were not completely downloaded or the download was paused, Nexus Mod Manager will now try to resume their download.", CommonData.ModManagerName, strIncomplete, MessageBoxButtons.OK, MessageBoxIcon.Information);

				if (drIncomplete == DialogResult.OK)
				{
					ViewModel.ResumeIncompleteDownloads(lstIncompleteMods);
				}

				return;
			}

			if (intNewVersions > 0)
			{
				sbMessage.AppendLine($"- {intNewVersions.ToString()} only got a new version of the file.");
			}

			if (intMissing > 0)
			{
				sbMessage.AppendLine($"- {intMissing.ToString()} are no longer present on the Nexus.");
			}

			sbMessage.AppendLine().AppendLine("This may cause the resulting profile installation to be broken or requiring some tweaks to work.");
			sbMessage.AppendLine("How would you like to proceed?").AppendLine().AppendLine();
			sbMessage.AppendLine("Click YES if you want to automatically download the mods missing from your PC (you will have to manually switch profile when all the downloads completes).");
			sbMessage.AppendLine("Click NO if you want to switch to the new profile without these mods, your game will most likely be unable to start without these mods or heavy tweaking.");
			sbMessage.AppendLine("Click CANCEL if you want to abort the profile switch.");

			var details = sbDetails.ToString();

			var drResult = ExtendedMessageBox.Show(this, sbMessage.ToString(), CommonData.ModManagerName, details, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);

			if (drResult == DialogResult.Yes)
			{
				if (ViewModel.ProfileManager.CurrentProfile != null)
				{
					ViewModel.ProfileManager.SetCurrentProfile(ViewModel.ProfileManager.CurrentProfile);
				}

				BindProfileCommands();

				if (lstIncompleteMods.Count > 0)
				{
					ViewModel.ResumeIncompleteDownloads(lstIncompleteMods);
				}

				ViewModel.AutomaticDownload(lstMissingMods, ViewModel.ProfileManager);

				return;
			}

			if (drResult == DialogResult.No)
			{
				ViewModel.ExecuteProfileSwitch(this);
			}

			if (drResult == DialogResult.Cancel)
			{
				if (ViewModel.ProfileManager.CurrentProfile != null)
				{
					ViewModel.ProfileManager.SetCurrentProfile(ViewModel.ProfileManager.CurrentProfile);
				}

				BindProfileCommands();
			}

		}

		#endregion

		/// <summary>
		/// Handles the <see cref="MainFormVM.ProfileSharing"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays the progress dialog.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ViewModel_ProfileSharing(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_ProfileSharing, sender, e);
				return;
			}

			ProgressDialog.ShowDialog(this, e.Argument, true);
		}

		/// <summary>
		/// Handles the <see cref="MainFormVM.AbortedProfileSwitch"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays the progress dialog.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ViewModel_AbortedProfileSwitch(object sender, EventArgs e)
		{
			BindProfileCommands();
		}

		/// <summary>
		/// Handles the <see cref="MainFormVM.ProfileRenaming"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays the progress dialog.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ViewModel_RenamingBackedProfile(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_RenamingBackedProfile, sender, e);
				return;
			}

			ProgressDialog.ShowDialog(this, e.Argument, true);

			var strResult = e.Argument.ReturnValue.ToString();

			if (!strResult.Contains("ERROR"))
			{
				var bpBackedProfile = ViewModel.ProfileManager.ModBackedProfiles.FirstOrDefault(x => String.Equals(Path.GetFileName(x.OnlineID), ((ModProfile)sender).OnlineID, StringComparison.InvariantCultureIgnoreCase));

				if (bpBackedProfile != null)
				{
					ViewModel.ProfileManager.ModBackedProfiles.Remove(bpBackedProfile);
					ViewModel.ProfileManager.ModBackedProfiles.Add(new ModProfile(((ModProfile)bpBackedProfile).Id, strResult, ViewModel.ModRepository.GameDomainName.ToString(), ((ModProfile)bpBackedProfile).ModCount, false, ((ModProfile)bpBackedProfile).OnlineID, ((ModProfile)bpBackedProfile).Name, System.DateTime.Now.ToShortDateString(), ((ModProfile)bpBackedProfile).IsShared, ((ModProfile)bpBackedProfile).Version.ToString(), ((ModProfile)bpBackedProfile).Author, ((ModProfile)bpBackedProfile).WorksWithSaves, false));
					ViewModel.ProfileManager.SaveOnlineConfig();
				}
			}
			else
			{
				XtraMessageBox.Show(strResult, "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}

		/// <summary>
		/// Handles the <see cref="MainFormVM.MigratingMods"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays the progress dialog.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ViewModel_MigratingMods(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_MigratingMods, sender, e);
				return;
			}

			ProgressDialog.ShowDialog(this, e.Argument, false);

			if (ViewModel.ProfileManager.CurrentProfile != null)
			{
				ViewModel.SwitchProfile(this, ViewModel.ProfileManager.CurrentProfile, true, false);
			}
			else
			{
				BindProfileCommands();
			}
		}

		/// <summary>
		/// Handles the <see cref="VirtualModActivator.ModActivationChanged"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays the progress dialog.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void VirtualModActivator_ModActivationChanged(object sender, EventArgs e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs>)VirtualModActivator_ModActivationChanged, sender, e);
				return;
			}

			if (!ViewModel.IsSwitching)
			{
				UpdateModsFeedback();

				if (ViewModel.ProfileManager.CurrentProfile != null)
				{
					var bteIniEdits = ViewModel.ModManager.InstallationLog.GetXmlIniList();

					ViewModel.ProfileManager.UpdateProfile(ViewModel.ProfileManager.CurrentProfile, bteIniEdits, null, null, out var error);

					if (!string.IsNullOrEmpty(error))
					{
						error = error + Environment.NewLine + Environment.NewLine + "Unable to automatically save the profile file, please close the program blocking the reported file and manually click on Save Profile from the profiles context menu";
						XtraMessageBox.Show(error, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					}

					_modManagerControl.SetCommandExecutableStatus();
					BindProfileCommands();
				}
			}
		}

		/// <summary>
		/// Handles the <see cref="PluginManagerVM.ApplyingImportedLoadOrder"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays the progress dialog.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ViewModel_ApplyingImportedLoadOrder(object sender, EventArgs<IBackgroundTask> e)
		{
			if (IsDisposed || Disposing || e == null || e.Argument == null)
				return;

			if (InvokeRequired)
			{
				try
				{
					Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_ApplyingImportedLoadOrder, sender, e);
				}
				catch (ObjectDisposedException)
				{
				}
				catch (InvalidOperationException)
				{
				}

				return;
			}

			if (IsDisposed || Disposing || !IsHandleCreated)
				return;

			ProgressDialog.ShowDialog(this, e.Argument, false);
		}

		private void ViewModel_CreatingBackup(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_CreatingBackup, sender, e);
				return;
			}

			ProgressDialog.ShowDialog(this, e.Argument, false);

			if (e.Argument.ReturnValue != null)
			{
				XtraMessageBox.Show("Unable to create the backup: " + e.Argument.ReturnValue.ToString(), "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
			else
			{
				XtraMessageBox.Show("Backup Complete!", "Backup Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}

		private void ViewModel_RestoringBackup(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_RestoringBackup, sender, e);
				return;
			}

			ProgressDialog.ShowDialog(this, e.Argument, false);

			if ((e.Argument.ReturnValue != null) && (e.Argument.ReturnValue is ModProfile modProfile))
			{
				ViewModel.SwitchProfile(this, modProfile, true, true);
			}
			else
			{
				if ((e.Argument.ReturnValue != null) && (e.Argument.ReturnValue is string error))
				{
					XtraMessageBox.Show(error);
				}
				else
				{
					XtraMessageBox.Show("An error occured during the Restore!");
				}
			}
		}

		private void ViewModel_PurgingLooseFiles(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_PurgingLooseFiles, sender, e);
				return;
			}

			ProgressDialog.ShowDialog(this, e.Argument, false);

			if (e.Argument.ReturnValue != null)
			{
				XtraMessageBox.Show("Purge Complete!", "Purge Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}

		#endregion

		#region Game Launch Binding Helpers

		/// <summary>
		/// Binds the game launch commands to the UI.
		/// </summary>
		protected void BindLaunchCommands()
		{
			ClearTransientPopupItems(popupLaunch);
			_launchDefaultItem = null;

			foreach (Command launchCommand in ViewModel.GameLauncher.LaunchCommands)
			{
				BarButtonItem launchItem = CreateCommandBarButton(launchCommand);
				launchItem.ItemClick += LaunchMenuItem_ItemClick;
				popupLaunch.AddItem(launchItem);

				if (String.Equals(launchCommand.Id, _viewModel.SelectedGameLaunchCommandId, StringComparison.OrdinalIgnoreCase))
					SetDefaultLaunchItem(launchItem);
			}

			if (_launchDefaultItem == null && popupLaunch.ItemLinks.Count > 0)
				SetDefaultLaunchItem(popupLaunch.ItemLinks[0].Item as BarButtonItem);

			if (_launchDefaultItem == null)
			{
				spbLaunch.Caption = "Launch Game";
				spbLaunch.ImageOptions.Image = null;
				spbLaunch.Enabled = false;
			}
			else
			{
				spbLaunch.Enabled = true;
			}

			ViewModel.ConfirmCloseAfterGameLaunch = ConfirmCloseAfterGameLaunch;
			ViewModel.GameLauncher.GameLaunched -= GameLauncher_GameLaunched;
			ViewModel.GameLauncher.GameLaunched += GameLauncher_GameLaunched;
		}

		/// <summary>
		/// Binds the game profiles commands to the UI.
		/// </summary>
		protected void BindProfileCommands()
		{
			ClearTransientPopupItems(popupProfiles);
			_profileDefaultItem = null;

			if (!ViewModel.ProfileManager.Initialized)
			{
				SetBarItemVisible(spbProfiles, false);
				return;
			}

			popupProfiles.AddItem(CreateProfileCommandItem("New", "New Profile"));
			popupProfiles.AddItem(CreateProfileCommandItem("Rename", "Rename Current Profile"));
			popupProfiles.AddItem(CreateProfileCommandItem("Remove", "Remove Current Profile"));
			popupProfiles.AddItem(CreateProfileCommandItem("Save", "Save Current Profile"));

			bool beginProfileGroup = true;
			if (ViewModel.ProfileManager.CurrentProfile != null)
			{
				IModProfile currentProfile = ViewModel.ProfileManager.CurrentProfile;
				string currentName = GetCompactProfileName(currentProfile.Name);
				BarButtonItem currentItem = new BarButtonItem(barManagerMain, $"{currentName} ({currentProfile.ModCount})")
				{
					Tag = currentProfile,
					Enabled = false
				};
				popupProfiles.AddItem(currentItem).BeginGroup = beginProfileGroup;
				beginProfileGroup = false;

				if (currentProfile.IsDefault)
				{
					_profileDefaultItem = currentItem;
					spbProfiles.Caption = currentName;
				}
			}

			foreach (ModProfile profile in ViewModel.ProfileManager.ModProfiles.OrderBy(item => item.Name))
			{
				if (profile == ViewModel.ProfileManager.CurrentProfile)
					continue;

				string profileName = GetCompactProfileName(profile.Name);
				PopupMenu profileActions = new PopupMenu(barManagerMain);
				BarButtonItem profileItem = new BarButtonItem(barManagerMain, $"{profileName} ({profile.ModCount})")
				{
					Tag = profile,
					ButtonStyle = BarButtonStyle.DropDown,
					DropDownControl = profileActions,
					ActAsDropDown = false
				};
				profileItem.ItemClick += ProfileMenuItem_ItemClick;
				popupProfiles.AddItem(profileItem).BeginGroup = beginProfileGroup;
				beginProfileGroup = false;

				AddProfileAction(profileActions, profile, "RenameProfile", "Rename Profile");
				AddProfileAction(profileActions, profile, "RemoveProfile", "Remove Profile");
				if (ViewModel.GameMode.UsesPlugins)
					AddProfileAction(profileActions, profile, "ImportLoadorder", "Import Profile's Load Order");

				if (profile.IsDefault)
				{
					_profileDefaultItem = profileItem;
					spbProfiles.Caption = profileName;
				}
			}

			if (_profileDefaultItem == null && popupProfiles.ItemLinks.Count > 0)
			{
				_profileDefaultItem = popupProfiles.ItemLinks[0].Item;
				spbProfiles.Caption = _profileDefaultItem.Caption;
			}

			SetBarItemVisible(spbProfiles, true);
		}

		/// <summary>
		/// Sets the default launch action represented by the main Launch button.
		/// </summary>
		private void SetDefaultLaunchItem(BarButtonItem item)
		{
			if (item == null)
				return;

			_launchDefaultItem = item;
			spbLaunch.Caption = item.Caption;
			spbLaunch.ImageOptions.Image = ScaleBarImage(item.ImageOptions.Image, MainToolbarImageSize);
		}

		/// <summary>
		/// Creates one of the fixed profile-management commands.
		/// </summary>
		private BarButtonItem CreateProfileCommandItem(string command, string caption)
		{
			BarButtonItem item = new BarButtonItem(barManagerMain, caption)
			{
				Tag = command
			};
			item.ItemClick += ProfileMenuItem_ItemClick;
			return item;
		}

		/// <summary>
		/// Adds a profile-specific action to a profile's drop-down menu.
		/// </summary>
		private void AddProfileAction(PopupMenu menu, ModProfile profile, string command, string caption)
		{
			BarButtonItem item = new BarButtonItem(barManagerMain, caption)
			{
				Tag = command
			};
			item.ItemClick += (sender, args) => HandleProfileSubItemClick(profile, Convert.ToString(args.Item.Tag));
			menu.AddItem(item);
		}

		/// <summary>
		/// Returns a toolbar-safe profile caption without changing the underlying profile name.
		/// </summary>
		private static string GetCompactProfileName(string profileName)
		{
			if (String.IsNullOrEmpty(profileName) || profileName.Length <= 64)
				return profileName ?? String.Empty;

			return profileName.Substring(0, 62) + "..";
		}

		/// <summary>
		/// Routes a DevExpress profile-menu click through the existing profile workflow.
		/// </summary>
		private void ProfileMenuItem_ItemClick(object sender, ItemClickEventArgs e)
		{
			HandleProfileItemClick(e.Item);
		}

		/// <summary>
		/// Binds the SupportedTools launch commands to the UI.
		/// </summary>
		protected void BindSupportedToolsCommands()
		{
			ClearTransientPopupItems(popupSupportedTools);

			if (ViewModel.SupportedToolsLauncher == null)
			{
				SetBarItemVisible(spbSupportedTools, false);
				return;
			}

			foreach (Command launchCommand in ViewModel.SupportedToolsLauncher.LaunchCommands)
			{
				BarButtonItem launchItem = CreateCommandBarButton(launchCommand, Properties.Resources.supported_tools_flat);
				launchItem.ItemRightClick += SupportedToolItem_ItemRightClick;
				popupSupportedTools.AddItem(launchItem);
			}

			spbSupportedTools.Caption = "Supported Tools";
			spbSupportedTools.ImageOptions.Image = ScaleBarImage(Properties.Resources.supported_tools_flat, MainToolbarImageSize);
			SetBarItemVisible(spbSupportedTools, popupSupportedTools.ItemLinks.Count > 0);
		}

		/// <summary>
		/// Configures a supported tool when its DevExpress menu item is right-clicked.
		/// </summary>
		/// <param name="sender">The bar item that raised the event.</param>
		/// <param name="e">The item-click event data.</param>
		private void SupportedToolItem_ItemRightClick(object sender, ItemClickEventArgs e)
		{
			Command command = e.Item.Tag as Command;
			if (command == null)
				return;

			popupSupportedTools.HidePopup();
			ViewModel.SupportedToolsLauncher.ConfigCommand(command.Id);
		}

		/// <summary>
		/// Selects a launch command as the main Launch action after its popup item is clicked.
		/// </summary>
		/// <param name="sender">The bar item that raised the event.</param>
		/// <param name="e">The item-click event data.</param>
		private void LaunchMenuItem_ItemClick(object sender, ItemClickEventArgs e)
		{
			BarButtonItem item = e.Item as BarButtonItem;
			Command command = item?.Tag as Command;
			if (item == null || command == null)
				return;

			SetDefaultLaunchItem(item);
			_viewModel.SelectedGameLaunchCommandId = command.Id;
		}

		/// <summary>
		/// Opens the Supported Tools popup.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">The event data.</param>
		private void spbSupportedTools_ButtonClick(object sender, EventArgs e)
		{
			popupSupportedTools.ShowPopup(Control.MousePosition);
		}

		/// <summary>
		/// Executes a profile-specific popup action.
		/// </summary>
		/// <param name="profile">The profile targeted by the action.</param>
		/// <param name="command">The profile action identifier.</param>
		private void HandleProfileSubItemClick(ModProfile profile, string command)
		{
			if (profile == null || String.IsNullOrWhiteSpace(command))
				return;

			switch (command)
			{
				case "RenameProfile":
					PromptDialog renameDialog = PromptDialog.ShowDialog("Rename Online", this, "Type the new name:", "Rename Local", profile.Name, null, null);
					if (renameDialog == null || String.IsNullOrEmpty(renameDialog.EnteredText) || renameDialog.EnteredText.Equals(profile.Name, StringComparison.InvariantCulture))
						return;

					if (renameDialog.EnteredText.Length > 64)
					{
						XtraMessageBox.Show("Unable to rename the profile!" + Environment.NewLine + Environment.NewLine + "The new profile name is too long, maximum 64 characters.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
						return;
					}

					if (String.IsNullOrWhiteSpace(renameDialog.EnteredText.Replace("|", String.Empty)))
					{
						XtraMessageBox.Show("Unable to rename the profile!" + Environment.NewLine + Environment.NewLine + "The new profile name is empty or contains unsupported special characters (eg. | ).", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
						return;
					}

					profile.Name = renameDialog.EnteredText;
					ViewModel.ProfileManager.RenameProfile(profile, profile.Name);
					BindProfileCommands();
					break;

				case "RemoveProfile":
					PromptDialog removeDialog = PromptDialog.ShowDialog("Remove Online", this, String.Format("Are you sure you want to remove the current profile: {0}", profile.Name), "Remove Local", null, null, null);
					if (removeDialog != null)
						ViewModel.ProfileManager.RemoveProfile(profile);
					break;

				case "ImportLoadorder":
					if (String.IsNullOrEmpty(profile.Id))
						return;

					DialogResult result = ExtendedMessageBox.Show(this, $"Are you sure you want to import this profile's loadorder? '{profile.Name}'", "Import Loadorder", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
					if (result != DialogResult.Yes)
						return;

					if (profile.LoadOrder == null)
					{
						ViewModel.ProfileManager.LoadProfile(profile, out var profileData);
						if (profileData != null && profileData.Count > 0 && profileData.ContainsKey("loadorder"))
							ViewModel.PluginManagerVM.ImportLoadOrderFromString(profileData["loadorder"]);
					}
					else
					{
						ViewModel.PluginManagerVM.ImportLoadOrderFromDictionary(profile.LoadOrder);
					}
					break;
			}
		}

		/// <summary>
		/// Handles a main profile command or switches to a selected profile.
		/// </summary>
		/// <param name="clickedItem">The DevExpress menu item selected by the user.</param>
		private void HandleProfileItemClick(BarItem clickedItem)
		{
			if (clickedItem == null)
				return;

			if (clickedItem.Tag != null)
			{
				if (clickedItem.Tag is string)
				{
					var strCommand = clickedItem.Tag.ToString();

					switch (strCommand)
					{
						case "New":
							byte[] bteLoadOrder = null;

							if (ViewModel.GameMode.UsesPlugins)
							{
								bteLoadOrder = ViewModel.PluginManagerVM.ExportLoadOrder();
							}

							AddNewProfile(bteLoadOrder);
							var mopCurrentProfile = (ModProfile)ViewModel.ProfileManager.CurrentProfile;

							if (mopCurrentProfile != null)
							{
								var pdDialog = PromptDialog.ShowDialog("", this, "Type the profile name:", "Set the Profile name", mopCurrentProfile.Name, null, null);

								if (pdDialog != null)
								{
									if (!string.IsNullOrEmpty(pdDialog.EnteredText) && !pdDialog.EnteredText.Equals(mopCurrentProfile.Name, StringComparison.InvariantCulture))
									{
										if (pdDialog.EnteredText.Length > 64)
										{
											ExtendedMessageBox.Show(this, "Unable to set the profile name!" + Environment.NewLine + Environment.NewLine + "The profile name is too long, maximum 64 characters.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
											return;
										}

										if (string.IsNullOrWhiteSpace(pdDialog.EnteredText.Replace("|", string.Empty)))
										{
											ExtendedMessageBox.Show(this, "Unable to set the profile name!" + Environment.NewLine + Environment.NewLine + "The profile name is empty or contains unsupported special characters (eg. | ).", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
											return;
										}

										mopCurrentProfile.Name = pdDialog.EnteredText;
										ViewModel.ProfileManager.UpdateProfile(mopCurrentProfile, null, null, null, out var error);
									}
								}
							}
							break;
						case "Rename":
							var mopCurrent = (ModProfile)ViewModel.ProfileManager.CurrentProfile;

							if (mopCurrent != null)
							{
								var pdDialog = PromptDialog.ShowDialog("Rename Online", this, "Type the new name:", "Rename Local", mopCurrent.Name, null, null);

								if (pdDialog != null)
								{
									if (!string.IsNullOrEmpty(pdDialog.EnteredText) && !pdDialog.EnteredText.Equals(mopCurrent.Name, StringComparison.InvariantCulture))
									{
										if (pdDialog.EnteredText.Length > 64)
										{
											ExtendedMessageBox.Show(this, "Unable to rename the profile!" + Environment.NewLine + Environment.NewLine + "The new profile name is too long, maximum 64 characters.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
											return;
										}

										if (string.IsNullOrWhiteSpace(pdDialog.EnteredText.Replace("|", string.Empty)))
										{
											ExtendedMessageBox.Show(this, "Unable to rename the profile!" + Environment.NewLine + Environment.NewLine + "The new profile name is empty or contains unsupported special characters (eg. | ).", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
											return;
										}

										mopCurrent.Name = pdDialog.EnteredText;
										ViewModel.ProfileManager.UpdateProfile(mopCurrent, null, null, null, out var error);

										if (!string.IsNullOrEmpty(error))
										{
											error = error + Environment.NewLine + Environment.NewLine + "Unable to automatically save the profile file, please close the program blocking the reported file and manually click on Save Profile from the profiles context menu";
											XtraMessageBox.Show(error, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
										}

										BindProfileCommands();
									}
								}
							}
							break;
						case "Remove":
							var mopProfile = (ModProfile)ViewModel.ProfileManager.CurrentProfile;

							if (mopProfile != null)
							{
								var pdDialog = PromptDialog.ShowDialog("Remove Online", this, $"Are you sure you want to remove the current profile: {mopProfile.Name}", "Remove Local", null, null, null);

								if (pdDialog != null)
								{
									ViewModel.ProfileManager.RemoveProfile(mopProfile);
								}
							}
							break;
						case "Save":
							var mopUpdate = (ModProfile)ViewModel.ProfileManager.CurrentProfile;

							if (mopUpdate != null)
							{
								byte[] bteNewLoadOrder = null;

								if (ViewModel.GameMode.UsesPlugins)
								{
									bteNewLoadOrder = ViewModel.PluginManagerVM.ExportLoadOrder();
								}

								var bteIniEdits = ViewModel.ModManager.InstallationLog.GetXmlIniList();

								string[] optionalFiles = null;

								if (ViewModel.GameMode.RequiresOptionalFilesCheckOnProfileSwitch)
								{
									if (ViewModel.PluginManager?.ActivePlugins != null && ViewModel.PluginManager.ActivePlugins.Count > 0)
									{
										optionalFiles = ViewModel.GameMode.GetOptionalFilesList(ViewModel.PluginManager.ActivePlugins.Select(x => x.Filename).ToArray());
									}
								}

								ViewModel.ProfileManager.UpdateProfile(mopUpdate, bteIniEdits, bteNewLoadOrder, optionalFiles, out var error);

								if (!string.IsNullOrEmpty(error))
								{
									error = error + Environment.NewLine + Environment.NewLine + "Unable to automatically save the profile file, please close the program blocking the reported file and manually click on Save Profile from the profiles context menu";
									XtraMessageBox.Show(error, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
								}

								BindProfileCommands();
							}
							break;
					}
				}
				else
				{
					if (ViewModel.ModManager.VirtualModActivator.MultiHDMode && !UacUtil.IsElevated)
					{
						ExtendedMessageBox.Show(this, "It looks like MultiHD mode is enabled but you're not running NMM as Administrator, you will be unable to install/activate mods or switch profiles." + Environment.NewLine + Environment.NewLine + "Close NMM and run it as Administrator to fix this.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
						return;
					}

					_profileDefaultItem = clickedItem;
					spbProfiles.Caption = clickedItem.Caption;
					spbProfiles.ImageOptions.Image = ScaleBarImage(clickedItem.ImageOptions.Image, MainToolbarImageSize);

					var impProfile = (IModProfile)clickedItem.Tag;

					if (impProfile != null)
					{
						var lstConfigFiles = new List<string>();

						var strProfilePath = ViewModel.VirtualModActivator.RequiresFixing(ViewModel.ProfileManager.GetProfileModListPath(impProfile));

						if (!string.IsNullOrEmpty(strProfilePath))
						{
							lstConfigFiles.Add(strProfilePath);
							ViewModel.FixConfigFiles(lstConfigFiles, impProfile);
						}
						else
						{
							ViewModel.SwitchProfile(this, impProfile, false, false);
							BindProfileCommands();
						}
					}
				}
			}
		}

		/// <summary>
		/// Handle the ModProfiles_CollectionChanged event
		/// </summary>
		private void ModProfiles_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, NotifyCollectionChangedEventArgs>)ModProfiles_CollectionChanged, sender, e);
				return;
			}

			BindProfileCommands();
		}

		/// <summary>
		/// The Add New Profile function in the Main Form.
		/// </summary>
		private void AddNewProfile(byte[] p_bteLoadOrder)
		{
			AddNewProfile(null, null, p_bteLoadOrder, -1, false);
		}

		/// <summary>
		/// The Add New Profile function in the Main Form.
		/// </summary>
		private void AddNewProfile(byte[] modList, byte[] iniList, byte[] loadOrder, int modCount, bool backup)
		{
			string[] optionalFiles = null;

			if (ViewModel.GameMode.RequiresOptionalFilesCheckOnProfileSwitch && ViewModel.PluginManager?.ActivePlugins != null && ViewModel.PluginManager.ActivePlugins.Count > 0)
			{
				optionalFiles = ViewModel.GameMode.GetOptionalFilesList(ViewModel.PluginManager.ActivePlugins.Select(x => x.Filename).ToArray());
			}

			if (backup)
			{
				ViewModel.ProfileManager.BackupProfile(modList, iniList, loadOrder, ViewModel.GameMode.ModeId, modCount, optionalFiles);
			}
			else
			{
				try
				{
					ViewModel.ProfileManager.AddProfile(modList, iniList, loadOrder, ViewModel.GameMode.ModeId, modCount, optionalFiles);
				}
				catch (Exception e)
				{
					XtraMessageBox.Show(string.Format("There were issues saving the current profile: " + Environment.NewLine + Environment.NewLine + "{0}" + Environment.NewLine, e.Message), "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				}
			}

			BindProfileCommands();
		}

		/// <summary>
		/// Confirms if the manager should close after launching the game.
		/// </summary>
		/// <param name="rememberSelection">Whether the selected response should be remembered.</param>
		/// <returns><c>true</c> if the manager should close after game launch;
		/// <c>false</c> otherwise.</returns>
		private bool ConfirmCloseAfterGameLaunch(out bool rememberSelection)
		{
			var close = ExtendedMessageBox.Show(this, $"Would you like {CommonData.ModManagerName} to close after launching the game?", "Close", "Details", MessageBoxButtons.YesNo, MessageBoxIcon.Question, out var remember) == DialogResult.Yes;
			rememberSelection = remember;

			return close;
		}

		/// <summary>
		/// Handles the <see cref="IGameLauncher.GameLaunched"/> event of the game launcher.
		/// </summary>
		/// <remarks>This displays any message resulting from the game launch. If the launch was successful, the
		/// form is closed.</remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="GameLaunchEventArgs"/> describing the event arguments.</param>
		private void GameLauncher_GameLaunched(object sender, GameLaunchEventArgs e)
		{
			if (!e.Launched)
			{
				XtraMessageBox.Show(this, e.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			else if (ViewModel.EnvironmentInfo.Settings.CloseModManagerAfterGameLaunch)
			{
				Close();
			}
		}

		#endregion

		/// <summary>
		/// Applies the given theme to the form.
		/// </summary>
		/// <param name="theme">The theme to apply.</param>
		protected void ApplyTheme(Theme theme)
		{
			Icon = Properties.Resources.NMM_CE_P_Logo;

			var changeMode = new Bitmap(spbChangeMode.ImageOptions.Image);

			for (var y = 0; y < changeMode.Height; y++)
			{
				for (var x = 0; x < changeMode.Width; x++)
				{
					var old = changeMode.GetPixel(x, y);

					var r = old.R;
					var g = old.G;
					var b = old.B;

					r = g = b = (byte)(0.21 * r + 0.72 * g + 0.07 * b);

					r = (byte)(r / 255.0 * theme.PrimaryColour.R);
					g = (byte)(g / 255.0 * theme.PrimaryColour.G);
					b = (byte)(b / 255.0 * theme.PrimaryColour.B);

					changeMode.SetPixel(x, y, Color.FromArgb(old.A, r, g, b));
				}
			}

			spbChangeMode.ImageOptions.Image = changeMode;
		}

		#region Form Events

		/// <summary>
		/// Raises the <see cref="Form.Closing"/> event.
		/// </summary>
		/// <remarks>
		/// This saves the current window position.
		/// </remarks>
		/// <param name="e">A <see cref="CancelEventArgs"/> describing the event arguments.</param>
		protected override void OnClosing(CancelEventArgs e)
		{
			base.OnClosing(e);
			ViewModel.EnvironmentInfo.Settings.WindowPositions.SetWindowPosition("MainForm", this);
		}

		/// <summary>
		/// Raises the <see cref="Control.Resize"/> event.
		/// </summary>
		/// <remarks>
		/// This saves the last window state before the form was minimized.
		/// </remarks>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);

			if (WindowState != FormWindowState.Minimized)
			{
				_lastWindowState = WindowState;
			}
		}

		/// <summary>
		/// Raises the <see cref="Form.Shown"/> event.
		/// </summary>
		/// <remarks>
		/// This notifies the view model the view is visible.
		/// </remarks>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnShown(EventArgs e)
		{
			base.OnShown(e);
			ShowStartupMessage();
			ViewModel.ViewIsShown();

			if (ViewModel.ModRepository.IsOffline)
			{
				ViewModel.ModManager.LoginTask.TokenLogin();
			}
		}

		#endregion

		/// <summary>
		/// Shows a startup message if needed.
		/// </summary>
		private void ShowStartupMessage()
		{
		}

		/// <summary>
		/// Restores focus to the form.
		/// </summary>
		public void RestoreFocus()
		{
			WindowState = _lastWindowState;
			Activate();
		}

		private void toolStripButtonOnlineStatus_Click(object sender, EventArgs e)
		{

		}

		private void tsbDiscord_Click(object sender, EventArgs e)
		{
			XtraMessageBox.Show(this, "NMM will open the official NMM Discord server invitation in your default browser.", "NMM Official Discord", MessageBoxButtons.OK, MessageBoxIcon.Information);
			Process.Start("https://discord.gg/JZ4tZ5KFQX");
		}

		private void tsbiPatreon_Click(object sender, EventArgs e)
		{
			XtraMessageBox.Show(this, "NMM will open the official NMM Patreon page in your default browser.", "NMM Official Patreon", MessageBoxButtons.OK, MessageBoxIcon.Information);
			Process.Start("https://www.patreon.com/NMMCE");
		}

		private void tsbiKofi_Click(object sender, EventArgs e)
		{
			XtraMessageBox.Show(this, "NMM will open the official Ko-fi page in your default browser.", "NMM Official Ko-fi", MessageBoxButtons.OK, MessageBoxIcon.Information);
			Process.Start("https://ko-fi.com/duskdweller");
		}

		private void spbSupportNMM_ButtonClick(object sender, EventArgs e)
		{
			XtraMessageBox.Show(this, "NMM will open the official Ko-fi page in your default browser.", "NMM Official Ko-fi", MessageBoxButtons.OK, MessageBoxIcon.Information);
			Process.Start("https://ko-fi.com/duskdweller");
		}

		private bool IsFileManagerAvailable()
		{
			Type type = ViewModel == null || ViewModel.GameMode == null ? null : ViewModel.GameMode.GetType();
			while (type != null)
			{
				if (String.Equals(type.Name, "GamebryoGameModeBase", StringComparison.OrdinalIgnoreCase))
					return true;

				type = type.BaseType;
			}

			return false;
		}

		private void tsbYouTube_Click(object sender, EventArgs e)
		{
			Process.Start(
				"https://www.youtube.com/channel/UCguaVgGHs4Xeknas--3YUsQ/videos");
		}

		private void CategoryManagerControl_CollapseAllCategoriesRequested(object sender, EventArgs e)
		{
			(_modManagerControl as ModManagerDXControl)?.CollapseAllCategories();
		}

		private void CategoryManagerControl_ExpandAllCategoriesRequested(object sender, EventArgs e)
		{
			(_modManagerControl as ModManagerDXControl)?.ExpandAllCategories();
		}
	}
}
