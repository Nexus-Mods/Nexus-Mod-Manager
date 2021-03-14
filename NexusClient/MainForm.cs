namespace Nexus.Client
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Drawing;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Windows.Forms;

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
    using Nexus.Client.PluginManagement.UI;
    using Nexus.Client.Settings.UI;
    using Nexus.Client.SSO;
    using Nexus.Client.TipsManagement;
    using Nexus.Client.UI;
    using Nexus.Client.Util;
    using Nexus.Client.Util.Collections;
    using Nexus.UI.Controls;

    using WeifenLuo.WinFormsUI.Docking;

    /// <summary>
    /// The main form of the mod manager.
    /// </summary>
    public partial class MainForm : ManagedFontForm
	{
		private MainFormVM _viewModel;
		private FormWindowState _lastWindowState = FormWindowState.Normal;
		private readonly ModManagerControl _modManagerControl;
		private readonly PluginManagerControl _pluginManagerControl;
		private readonly DownloadMonitorControl _downloadMonitorControl;
		private readonly ModActivationMonitorControl _modActivationMonitorControl;
		private double _defaultActivityManagerAutoHidePortion;
		private double _defaultActivationMonitorAutoHidePortion;

        public string OptionalPremiumMessage = string.Empty;

		FormWindowState LastWindowState = FormWindowState.Minimized;
		private bool _showLastBalloon;
		private BalloonManager _balloonManager;
		
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
				_viewModel.CheckingOnlineProfileIntegrity += ViewModel_CheckingOnlineProfileIntegrity;
				_viewModel.ProfileManager.CheckOnlineProfileIntegrityStarted += ViewModel_CheckingOnlineProfileIntegrity;
				_viewModel.ApplyingImportedLoadOrder += ViewModel_ApplyingImportedLoadOrder;
				_viewModel.CreatingBackup += ViewModel_CreatingBackup;
				_viewModel.RestoringBackup += ViewModel_RestoringBackup;
				_viewModel.PurgingLooseFiles += ViewModel_PurgingLooseFiles;
				_viewModel.ConfigFilesFixing += ViewModel_ConfigFilesFixing;
				_modManagerControl.ViewModel = _viewModel.ModManagerVM;

                if (ViewModel.UsesPlugins)
				{
					_pluginManagerControl.ViewModel = _viewModel.PluginManagerVM;
					_viewModel.PluginManager.ActivePlugins.CollectionChanged += ActivePlugins_CollectionChanged;
					_pluginManagerControl.ViewModel.PluginMoved += pmcPluginManager_PluginMoved;
					_pluginManagerControl.ViewModel.ApplyingImportedLoadOrder += ViewModel_ApplyingImportedLoadOrder;
				}

				_modActivationMonitorControl.ViewModel = _viewModel.ModActivationMonitorVM;
				_downloadMonitorControl.ViewModel = _viewModel.DownloadMonitorVM;
				_downloadMonitorControl.ViewModel.ActiveTasks.CollectionChanged += ActiveTasks_CollectionChanged;
				_downloadMonitorControl.ViewModel.Tasks.CollectionChanged += Tasks_CollectionChanged;
				_downloadMonitorControl.ViewModel.PropertyChanged += ActiveTasks_PropertyChanged;

				ViewModel.ModRepository.UserStatusUpdate += ModRepository_UserStatusUpdate;

				ApplyTheme(_viewModel.ModeTheme);

				Text = _viewModel.Title;

				_viewModel.ConfirmUpdaterAction = ConfirmUpdaterAction;

				foreach (HelpInformation.HelpLink hlpLink in _viewModel.HelpInfo.HelpLinks)
				{
                    ToolStripMenuItem tmiHelp = new ToolStripMenuItem
                    {
                        Tag = hlpLink,
                        Text = hlpLink.Name,
                        ToolTipText = hlpLink.Url,
                        ImageScaling = ToolStripItemImageScaling.None
                    };

                    tmiHelp.Click += tmiHelp_Click;
					spbHelp.DropDownItems.Add(tmiHelp);
				}

				_balloonManager = new BalloonManager(ViewModel.UsesPlugins);
				_balloonManager.ShowNextClick += BalloonManagerShowNextClick;
				_balloonManager.ShowPreviousClick += BalloonManagerShowPreviousClick;
				_balloonManager.CloseClick += BalloonManagerCloseClick;

				BindCommands();
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the view with its dependencies.
		/// </summary>
		/// <param name="viewModel">The view model that provides the data and operations for this view.</param>
		public MainForm(MainFormVM viewModel)
		{
            _defaultActivityManagerAutoHidePortion = 0;

            InitializeComponent();

			FormClosing += CheckDownloadsOnClosing;

			ResizeEnd += MainForm_ResizeEnd;
			ResizeBegin += MainForm_ResizeBegin;
			Resize += MainForm_Resize;
			Shown += MainForm_Shown;

			_pluginManagerControl = new PluginManagerControl();
			_modManagerControl = new ModManagerControl();
			_downloadMonitorControl = new DownloadMonitorControl();
			_modActivationMonitorControl = new ModActivationMonitorControl();
			dockPanel1.ActiveContentChanged += dockPanel1_ActiveContentChanged;
			_modManagerControl.SetTextBoxFocus += MmgModManagerControlSetTextBoxFocus;
			_modManagerControl.ResetSearchBox += MmgModManagerControlResetSearchBox;
			_modManagerControl.UpdateModsCount += MmgModManagerControlUpdateModsCount;
			_modManagerControl.UninstallModFromProfiles += ModManagerControlUninstallModFromProfiles;
			_modManagerControl.UninstalledAllMods += MmgModManagerControlUninstalledAllMods;
			_downloadMonitorControl.SetTextBoxFocus += DmcDownloadMonitorControlSetTextBoxFocus;
			_pluginManagerControl.UpdatePluginsCount += PmcPluginManagerControlUpdatePluginsCount;
			_modActivationMonitorControl = new ModActivationMonitorControl();
			_modActivationMonitorControl.UpdateBottomBarFeedback += MacModActivationMonitorControlUpdateBottomBarFeedback;
			viewModel.ModManager.LoginTask.PropertyChanged += LoginTask_PropertyChanged;
            toolStripButtonRateLimit.Click +=  ToolStripButtonRateLimitOnClick;
            viewModel.ModRepository.RateLimitExceeded += (sender, args) => Invoke((Action<RateLimitExceededArgs>)OnRateLimitExceeded, args);

            if (viewModel.GameMode.SupportedToolsLauncher != null)
            {
                viewModel.GameMode.SupportedToolsLauncher.ChangedToolPath += SupportedTools_ChangedToolPath;
            }

            ViewModel = viewModel;

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
            MessageBox.Show(this, $"You've reached your daily and hourly limit. Try again in {Math.Floor((args.RateLimit.HourlyReset - DateTimeOffset.UtcNow).TotalMinutes)} minutes.", "API Rate Limit exceeded", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
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
                MessageBox.Show(this, info, "API Rate Limit status", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(this, "You need to be logged in to view rate limits.", "API Rate Limit status", MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }
        }

        #endregion

		#region Startup Checks

		/// <summary>
		/// Checks whether we need to migrate from the old install method to the new one.
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
				var strMigrationWarning = "This new version of NMM includes a major update to the way we store and install your mods which allows us to accommodate" + Environment.NewLine +
					"mod profiling (different profiles for different playthroughs of your game)." + Environment.NewLine +
					"In order for it to work NMM needs to REINSTALL or UNINSTALL all your currently installed mods." + Environment.NewLine + Environment.NewLine +
					"Choose option 'YES' if you would like NMM to attempt to try and REINSTALL all your currently installed mods using the new method. " + Environment.NewLine +
					"The migration procedure is a lengthy process and it could require several minutes or even hours depending on your PC speed and quantity and size " + Environment.NewLine +
					"of your currently installed mods." + Environment.NewLine + "You may be required to interact with some scripted installers during the reinstall process." + Environment.NewLine +
					"NMM will also backup the current Bashed/Perkus/DualSheat patches if presents, but you should rerun the various patchers should your game crash at startup." + Environment.NewLine + Environment.NewLine +
					"Choose option 'NO' if you want NMM to UNINSTALL all your mods and leave you to activate the ones you use again. " + Environment.NewLine +
					"(this doesn't delete your mods, it simply deactivates them)" + Environment.NewLine + Environment.NewLine +
					"Choose the 'CANCEL' option if you would like to cancel this setup and not proceed with this new version." + Environment.NewLine +
					"and you will need to reinstall the previous version of NMM you were using to be able to use NMM again." + Environment.NewLine;

				var drResult = ExtendedMessageBox.Show(this, strMigrationWarning, "New version setup", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

				if (drResult == DialogResult.Cancel)
                {
                    Environment.Exit(0);
                }
                else
				{
					ViewModel.MigrateMods(_modManagerControl, drResult == DialogResult.Yes);
				}
			}
		}

		/// <summary>
		/// Checks whether to show a game specific disclaimer.
		/// </summary>
		private void ShowGameSpecificDisclaimer()
		{
			if (ViewModel.RequiresStartupWarning())
			{
				var strWarning = "We've detected that you are using Fallout 4 version 1.5 (or later) for the first time with NMM. In version 1.5, " + Environment.NewLine +
					"Bethesda changed the way in which plugins were handled." + Environment.NewLine + Environment.NewLine +
					"Because of this, any plugin you previously had enabled will be disabled in NMM and you will need to reactivate " + Environment.NewLine +
					"them in order for your setup to work again." + Environment.NewLine + Environment.NewLine +
					"Unfortunately this is a side-effect of Bethesda's patching of Fallout 4 and nothing to do with us. Unless " + Environment.NewLine +
					"Bethesda change the modding method again in future patches you will only need to do this reactivation once " + Environment.NewLine + "(e.g. this won't happen every time you start NMM in the future!)." + Environment.NewLine + Environment.NewLine +
					"NOTE: If you are making use of NMM's profile system, simply go to the profile menu and select 'Import Load Order' " + Environment.NewLine +
					"from your profile, NMM will automatically reactivate all your plugins in the correct order for you." + Environment.NewLine + Environment.NewLine +
					"If you are not using the profiling system, you will need to activate all your plugins again manually.";

				ExtendedMessageBox.Show(this, strWarning, "New game version disclaimer", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

		/// <summary>
		/// Initializes the main UI components.
		/// </summary>
		/// <remarks>
		/// If the metrics of the various UI components have been saved, they are loaded. Otherwise,
		/// the default layout is applied.
		/// </remarks>
		protected void InitializeDocuments()
		{
            if (ViewModel.EnvironmentInfo.Settings.DockPanelLayouts.ContainsKey("mainForm") && !string.IsNullOrEmpty(ViewModel.EnvironmentInfo.Settings.DockPanelLayouts["mainForm"]))
			{
				dockPanel1.LoadFromXmlString(ViewModel.EnvironmentInfo.Settings.DockPanelLayouts["mainForm"], LoadDockedContent);
				try
				{
					if (_defaultActivityManagerAutoHidePortion == 0)
                    {
                        _defaultActivityManagerAutoHidePortion = _downloadMonitorControl.AutoHidePortion;
                    }
                }
				catch { }

                if (!ViewModel.UsesPlugins)
                {
                    _pluginManagerControl.Hide();
                }
            }
			else
			{
				if (ViewModel.UsesPlugins)
                {
                    _pluginManagerControl.DockState = DockState.Unknown;
                }

                _modManagerControl.DockState = DockState.Unknown;
				_downloadMonitorControl.DockState = DockState.Unknown;
				_downloadMonitorControl.ShowHint = DockState.DockBottomAutoHide;
				_downloadMonitorControl.Show(dockPanel1, DockState.DockBottomAutoHide);

                if (_defaultActivityManagerAutoHidePortion == 0)
                {
                    _defaultActivityManagerAutoHidePortion = _downloadMonitorControl.Height;
                }

                try
				{
					_downloadMonitorControl.AutoHidePortion = _defaultActivityManagerAutoHidePortion;
				}
				catch { }

				_modActivationMonitorControl.DockState = DockState.Unknown;
				_modActivationMonitorControl.ShowHint = DockState.DockBottom;
				_modActivationMonitorControl.Show(dockPanel1, DockState.DockBottom);

				if (_defaultActivationMonitorAutoHidePortion == 0)
                {
                    _defaultActivationMonitorAutoHidePortion = _modActivationMonitorControl.Height;
                }

                try
				{
					_modActivationMonitorControl.AutoHidePortion = _defaultActivationMonitorAutoHidePortion;
				}
				catch { }

				if (ViewModel.UsesPlugins)
                {
                    _pluginManagerControl.Show(dockPanel1);
                }

                _modManagerControl.Show(dockPanel1);
			}

			var strTab = dockPanel1.ActiveDocument.DockHandler.TabText;

			if (ViewModel.PluginManagerVM != null)
            {
                _pluginManagerControl.Show(dockPanel1);
            }

            if (ViewModel.UsesPlugins && strTab == "Plugins")
            {
                _pluginManagerControl.Show(dockPanel1);
            }
            else
            {
                _modManagerControl.Show(dockPanel1);
            }

            if (_downloadMonitorControl == null || _downloadMonitorControl.VisibleState == DockState.Unknown || _downloadMonitorControl.VisibleState == DockState.Hidden)
			{
				_downloadMonitorControl.Show(dockPanel1, DockState.DockBottom);

                if (_defaultActivityManagerAutoHidePortion == 0)
                {
                    _defaultActivityManagerAutoHidePortion = _downloadMonitorControl.Height;
                }

                try
				{
					_downloadMonitorControl.AutoHidePortion = _defaultActivityManagerAutoHidePortion;
				}
				catch { }
			}

			if (_modActivationMonitorControl == null || _modActivationMonitorControl.VisibleState == DockState.Unknown || _modActivationMonitorControl.VisibleState == DockState.Hidden)
			{
				_modActivationMonitorControl.Show(dockPanel1, DockState.DockBottom);

                if (_defaultActivationMonitorAutoHidePortion == 0)
                {
                    _defaultActivationMonitorAutoHidePortion = _modActivationMonitorControl.Height;
                }

                try
				{
					_modActivationMonitorControl.AutoHidePortion = _defaultActivationMonitorAutoHidePortion;
				}
				catch { }
			}

			_modActivationMonitorControl.DockTo(_downloadMonitorControl.Pane, DockStyle.Right, 1);

			if (ViewModel.UsesPlugins)
			{
				toolStripLabelPluginsCounter.Text = "  Total plugins: " + ViewModel.PluginManagerVM.ManagedPlugins.Count + "   |   Active plugins: ";

				var myFontFamily = new FontFamily(toolStripLabelActivePluginsCounter.Font.Name);

				if (ViewModel.PluginManagerVM.ActivePlugins.Count > ViewModel.PluginManagerVM.MaxAllowedActivePluginsCount)
				{
					var icoIcon = new Icon(SystemIcons.Warning, 16, 16);
					toolStripLabelActivePluginsCounter.Image = icoIcon.ToBitmap();
					toolStripLabelActivePluginsCounter.ForeColor = Color.Red;

					if (myFontFamily.IsStyleAvailable(FontStyle.Bold))
                    {
                        toolStripLabelActivePluginsCounter.Font = new Font(toolStripLabelActivePluginsCounter.Font, FontStyle.Bold);
                    }
                    else if (myFontFamily.IsStyleAvailable(FontStyle.Regular))
                    {
                        toolStripLabelActivePluginsCounter.Font = new Font(toolStripLabelActivePluginsCounter.Font, FontStyle.Regular);
                    }

                    toolStripLabelActivePluginsCounter.Text = ViewModel.PluginManagerVM.ActivePlugins.Count.ToString();
					toolStripLabelActivePluginsCounter.ToolTipText = $"Too many active plugins! {ViewModel.CurrentGameModeName} won't start!";
				}
				else
				{
					toolStripLabelActivePluginsCounter.Image = null;
					toolStripLabelActivePluginsCounter.ForeColor = Color.Black;

                    if (myFontFamily.IsStyleAvailable(FontStyle.Regular))
                    {
                        toolStripLabelActivePluginsCounter.Font = new Font(toolStripLabelActivePluginsCounter.Font, FontStyle.Regular);
                    }
                    else if (myFontFamily.IsStyleAvailable(FontStyle.Bold))
                    {
                        toolStripLabelActivePluginsCounter.Font = new Font(toolStripLabelActivePluginsCounter.Font, FontStyle.Bold);
                    }

                    toolStripLabelActivePluginsCounter.Text = ViewModel.PluginManagerVM.ActivePlugins.Count.ToString();
				}

			}
			else
			{
				toolStripSeparatorPluginSeparator.Visible = false;
				toolStripLabelPluginsCounter.Visible = false;
			}

			UpdateModsFeedback();
			UserStatusFeedback();
		}

		/// <summary>
		/// Shows the tips.
		/// </summary>
		/// <param name="p_strVersion">The version of the DropDownMenu clicked</param>
		public void ShowTips(string p_strVersion)
		{
			if (!string.IsNullOrEmpty(p_strVersion))
            {
                _balloonManager.SetTipList(p_strVersion);
            }

            var strTipSection = string.IsNullOrEmpty(_balloonManager.TipSection) ? "toolStrip1" : _balloonManager.TipSection;
			var strTipObject = string.IsNullOrEmpty(_balloonManager.TipObject) ? "tsbTips" : _balloonManager.TipObject;
			_balloonManager.ShowNextTip(FindControlCoords(strTipSection, strTipObject));
		}

		/// <summary>
		/// The BalloonManager ShowNextClick event.
		/// </summary>
        private void BalloonManagerShowNextClick(object sender, EventArgs e)
        {
            if (_viewModel.EnvironmentInfo.Settings.CheckForTipsOnStartup)
			{
				_viewModel.EnvironmentInfo.Settings.CheckForTipsOnStartup = false;
				_viewModel.EnvironmentInfo.Settings.Save();
			}

            ShowTips(_balloonManager.CurrentTip == null
                ? _viewModel.EnvironmentInfo.ApplicationVersion.ToString()
                : string.Empty);
        }

		/// <summary>
		/// The BalloonManager ShowPreviousClick event.
		/// </summary>
        private void BalloonManagerShowPreviousClick(object sender, EventArgs e)
		{
			ShowTips(string.Empty);
		}

		/// <summary>
		/// The BalloonManager CloseClick event.
		/// </summary>
        private void BalloonManagerCloseClick(object sender, EventArgs e)
		{
			if (_viewModel.EnvironmentInfo.Settings.CheckForTipsOnStartup)
			{
				_viewModel.EnvironmentInfo.Settings.CheckForTipsOnStartup = false;
				_viewModel.EnvironmentInfo.Settings.Save();
			}
		}

		/// <summary>
		/// Sets the UI elements providing feedback on the user online status.
		/// </summary>
		protected void UserStatusFeedback()
		{
            toolStripLabelLoginMessage.Visible = true;

            if (ViewModel.OfflineMode)
			{
				if (toolStripProgressBarDownloadSpeed != null)
                {
                    toolStripProgressBarDownloadSpeed.Visible = false;
                }
                
				toolStripLabelLoginMessage.Text = "You are not logged in.";
				toolStripLabelLoginMessage.Font = new Font(base.Font, FontStyle.Bold);
				toolStripButtonGoPremium.Visible = false;
				toolStripButtonOnlineStatus.Image = new Bitmap(Properties.Resources.loggedout_flat, 32, 30);
				toolStripLabelDownloads.Visible = false;
			}
			else
			{
				toolStripButtonOnlineStatus.Image = new Bitmap(Properties.Resources.loggedin_flat, 32, 30);
				
				if (ViewModel.UserStatus.IsPremium)
				{
                    toolStripButtonGoPremium.Visible = false;
                    OptionalPremiumMessage = string.Empty;
                    toolStripButtonGoPremium.Enabled = false;

                    if (toolStripProgressBarDownloadSpeed != null)
                    {
                        toolStripProgressBarDownloadSpeed.Maximum = 100;
                        toolStripProgressBarDownloadSpeed.Value = 0;
                        toolStripProgressBarDownloadSpeed.ColorFillMode = ProgressLabel.FillType.Ascending;
                        toolStripProgressBarDownloadSpeed.ShowOptionalProgress = true;
                    }
                    toolStripLabelDownloads.Tag = "Download Progress:";
                }
				else
				{
                    toolStripButtonGoPremium.Visible = true;
                    toolStripButtonGoPremium.Enabled = true;
                    OptionalPremiumMessage = " Not a Premium Member.";

                    if (toolStripProgressBarDownloadSpeed != null)
                    {
						// Disabled for the time being since there's currently no way to check whether an user is browsing the Nexus with an active adblocker
                        toolStripProgressBarDownloadSpeed.Maximum = (ViewModel.UserStatus.IsSupporter) ? 2048 : 2048;
                        toolStripProgressBarDownloadSpeed.Value = 0;
                        toolStripProgressBarDownloadSpeed.ColorFillMode = ProgressLabel.FillType.Descending;
                        toolStripProgressBarDownloadSpeed.ShowOptionalProgress = false;
                    }

                    toolStripLabelDownloads.Tag = "Download Speed:";
				}

                if (toolStripProgressBarDownloadSpeed != null && _downloadMonitorControl.ViewModel.ActiveTasks.Count > 0)
                {
                    toolStripProgressBarDownloadSpeed.Visible = true;
                }

                toolStripLabelDownloads.Text = $"{toolStripLabelDownloads.Tag} ({_downloadMonitorControl.ViewModel.ActiveTasks.Count} {(_downloadMonitorControl.ViewModel.ActiveTasks.Count == 1 ? "File" : "Files")}) ";
			}
		}

		/// <summary>
		/// Resets the UI layout to the default.
		/// </summary>
		protected void ResetUI()
		{
			ViewModel.EnvironmentInfo.Settings.DockPanelLayouts.Remove("mainForm");
			InitializeDocuments();

            try
			{
				_modManagerControl.ResetColumns();
			}
			catch { }
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
                MessageBox.Show("Nexus Mod Manager was unable to properly initialize the Automatic Sorting functionality." +
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
			_modManagerControl.DisableAllMods();
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
				MessageBox.Show("Nexus Mod Manager was unable to restore your backup profile." +
					Environment.NewLine + Environment.NewLine + error,
					"Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
			else
			{
				MessageBox.Show(String.Format("{0} has been successfully added to your profile list.", error),
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
                toolStripLabelLoginMessage.Text = $"{authenticationFormTask.OverallMessage}{OptionalPremiumMessage}";
                toolStripButtonOnlineStatus.ToolTipText = "Logout";
            }
            else
            {
                toolStripLabelLoginMessage.Text = authenticationFormTask.OverallMessage;
                toolStripButtonOnlineStatus.ToolTipText = "Login";
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
				var drFormClose = MessageBox.Show($"There is an ongoing download, are you sure you want to close {Application.ProductName}?", "Closing", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);

                if (drFormClose != DialogResult.Yes)
                {
                    e.Cancel = true;
                }
            }

			if (ViewModel.IsInstalling)
			{
				var drFormClose = MessageBox.Show($"There is an ongoing mod install/uninstall, are you sure you want to close {Application.ProductName}?", "Closing", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);

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
			if (ViewModel.EnvironmentInfo.Settings.CheckForTipsOnStartup && _balloonManager.balloonHelp != null)
			{
				_balloonManager.balloonHelp.Close();
			}
			else
			{
				if (_showLastBalloon)
				{
					_showLastBalloon = false;
					ShowTips(string.Empty);
				}
			}
		}

		/// <summary>
		/// The Main Form resizeBegin event.
		/// </summary>
		private void MainForm_ResizeBegin(object sender, EventArgs e)
		{
			if (_balloonManager?.balloonHelp != null)
			{
				if (_balloonManager.balloonHelp.Visible)
				{
					if (_balloonManager.CurrentTip != null)
                    {
                        _balloonManager.SetPreviousTip(true);
                    }

                    _balloonManager.balloonHelp.Close();
					_showLastBalloon = true;
				}
				else
                {
                    _showLastBalloon = false;
                }
            }
		}

		/// <summary>
		/// The Main Form resize event.
		/// </summary>
		private void MainForm_Resize(object sender, EventArgs e)
		{
			if (WindowState != LastWindowState)
			{
				LastWindowState = WindowState;

				if (WindowState == FormWindowState.Maximized || WindowState == FormWindowState.Normal)
				{
					if (_balloonManager?.balloonHelp != null && _balloonManager.balloonHelp.Visible)
					{
						if (_balloonManager.CurrentTip != null)
						{
							_balloonManager.SetPreviousTip(true);
							ShowTips(string.Empty);
						}
						else
						{
							_balloonManager.balloonHelp.Close();
						}
					}
				}
			}
		}

		private void MainForm_Shown(object sender, EventArgs e)
		{
			ModMigrationCheck();
			ShowGameSpecificDisclaimer();
			ConfigFilesCheck();
		}

		/// <summary>
		/// This will check whether the SearchBox should be visible.
		/// </summary>
		private void dockPanel1_ActiveContentChanged(object sender, EventArgs e)
		{
			if (Visible && dockPanel1.ActiveDocument != null)
			{
				toolStripTextBoxFind.Visible = dockPanel1.ActiveDocument.DockHandler.TabText == "Mods";
				toolStripTextBoxFind.Enabled = dockPanel1.ActiveDocument.DockHandler.TabText == "Mods";
			}
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
			tlbModsCounter.Text = "  Total mods: " + ViewModel.ModManagerVM.ManagedMods.Count + "   |   Installed mods: " + ViewModel.ModManager.ActiveMods.Count + "   |   Active mods: " + ViewModel.ModManager.VirtualModActivator.ActiveModList.Count();
 		}

		/// <summary>
		/// Updates the Plugins Counter
		/// </summary>
		private void PmcPluginManagerControlUpdatePluginsCount(object sender, EventArgs e)
		{
			toolStripLabelPluginsCounter.Text = "  Total plugins: " + ViewModel.PluginManagerVM.ManagedPlugins.Count + "   |   Active plugins: ";
			var myFontFamily = new FontFamily(toolStripLabelActivePluginsCounter.Font.Name);

			if (ViewModel.PluginManagerVM.ActivePlugins.Count > ViewModel.PluginManagerVM.MaxAllowedActivePluginsCount)
			{
				var icoIcon = new Icon(SystemIcons.Warning, 16, 16);
				toolStripLabelActivePluginsCounter.Image = icoIcon.ToBitmap();
				toolStripLabelActivePluginsCounter.ForeColor = Color.Red;

                if (myFontFamily.IsStyleAvailable(FontStyle.Bold))
                {
                    toolStripLabelActivePluginsCounter.Font = new Font(toolStripLabelActivePluginsCounter.Font, FontStyle.Bold);
                }
                else if (myFontFamily.IsStyleAvailable(FontStyle.Regular))
                {
                    toolStripLabelActivePluginsCounter.Font = new Font(toolStripLabelActivePluginsCounter.Font, FontStyle.Regular);
                }

                toolStripLabelActivePluginsCounter.Text = ViewModel.PluginManagerVM.ActivePlugins.Count.ToString();
				toolStripLabelActivePluginsCounter.ToolTipText = $"Too many active plugins! {ViewModel.CurrentGameModeName} won't start!"; ;
			}
			else
			{
				toolStripLabelActivePluginsCounter.Image = null;

                if (myFontFamily.IsStyleAvailable(FontStyle.Regular))
                {
                    toolStripLabelActivePluginsCounter.Font = new Font(toolStripLabelActivePluginsCounter.Font, FontStyle.Regular);
                }
                else if (myFontFamily.IsStyleAvailable(FontStyle.Bold))
                {
                    toolStripLabelActivePluginsCounter.Font = new Font(toolStripLabelActivePluginsCounter.Font, FontStyle.Bold);
                }

                toolStripLabelActivePluginsCounter.ForeColor = Color.Black;
				toolStripLabelActivePluginsCounter.Text = ViewModel.PluginManagerVM.ActivePlugins.Count.ToString();
			}
		}

		/// <summary>
		/// Updates the Plugins Counter
		/// </summary>
		private void pmcPluginManager_PluginMoved(object sender, EventArgs e)
		{
            if (ViewModel.ProfileManager.CurrentProfile != null && !ViewModel.IsSwitching)
			{
                if (ViewModel.GameMode.UsesPlugins)
				{
					var bteLoadOrder = ViewModel.PluginManagerVM.ExportLoadOrder();
                    ViewModel.ProfileManager.UpdateProfile(ViewModel.ProfileManager.CurrentProfile, null, bteLoadOrder, null, out var error);

					if (!string.IsNullOrEmpty(error))
					{
						error = error + Environment.NewLine + Environment.NewLine + "Unable to automatically save the profile file, please close the program blocking the reported file and manually click on Save Profile from the profiles context menu";
						MessageBox.Show(error, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					}
				}
			}
		}

		/// <summary>
		/// Set the focus to the Search Textbox.
		/// </summary>
		private void MmgModManagerControlSetTextBoxFocus(object sender, EventArgs e)
		{
			toolStripTextBoxFind.Focus();
		}

		/// <summary>
		/// The Main Form resetSearchBox event.
		/// </summary>
		private void MmgModManagerControlResetSearchBox(object sender, EventArgs e)
		{
			toolStripTextBoxFind.Clear();
		}

		/// <summary>
		/// Handles the <see cref="ModManagerControl.UninstallModFromProfiles"/> of the opening
		/// of the ReaMe file.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="ModEventArgs"/> describing the event arguments.</param>
		private void ModManagerControlUninstallModFromProfiles(object sender, ModEventArgs e)
		{
            var mods = new List<IMod> {e.Mod};

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
			if (_modManagerControl.Visible)
            {
                toolStripTextBoxFind.Focus();
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
					var lwiListViewItem = (ModActivationMonitorListViewItem)sender;

                    if (lwiListViewItem.Task != null)
					{
						toolStripButtonLoader.Visible = true;
						toolStripLabelBottomBarFeedbackCounter.Visible = true;

						if (!lwiListViewItem.Task.IsQueued)
						{
							if (lwiListViewItem.Task.GetType() == typeof(ModInstaller))
                            {
                                toolStripLabelBottomBarFeedback.Text = "Mod Activation: Installing ";
                            }
                            else if (lwiListViewItem.Task.GetType() == typeof(ModUninstaller))
                            {
                                toolStripLabelBottomBarFeedback.Text = "Mod Activation: Uninstalling ";
                            }
                            else if (lwiListViewItem.Task.GetType() == typeof(ModUpgrader))
                            {
                                toolStripLabelBottomBarFeedback.Text = "Mod Activation: Upgrading ";
                            }
                        }
					}
					else
					{
						toolStripLabelBottomBarFeedback.Text = "Idle";
						toolStripButtonLoader.Visible = false;
					}
				}
				else
				{
					toolStripButtonLoader.Visible = false;
					toolStripLabelBottomBarFeedbackCounter.Visible = false;
					toolStripLabelBottomBarFeedback.Text = "Idle";
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
				toolStripLabelBottomBarFeedbackCounter.Text = "";
				toolStripLabelBottomBarFeedback.Text = "";
				toolStripButtonLoader.Visible = false;
			}
			else
            {
                toolStripLabelBottomBarFeedbackCounter.Text = $"({intCompletedTasks}/{_modActivationMonitorControl.ViewModel.Tasks.Count})";
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
		/// The Find KeyUp event.
		/// </summary>
		private void tstFind_KeyUp(object sender, KeyEventArgs e)
		{
			_modManagerControl.FindItemWithText(toolStripTextBoxFind.Text);
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

		#region Binding Helpers

		/// <summary>
		/// Binds the commands to the UI.
		/// </summary>
		protected void BindCommands()
		{
			ViewModel.Updating += ViewModel_Updating;
			new ToolStripItemCommandBinding(tsbUpdate, ViewModel.UpdateCommand);

			ViewModel.ToggleLoginCommand.BeforeExecute += LogoutCommand_BeforeExecute;
			new ToolStripItemCommandBinding(toolStripButtonOnlineStatus, ViewModel.ToggleLoginCommand);

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

			if (ViewModel.ProfileManager.CurrentProfile != null && !ViewModel.IsSwitching)
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

                    var bteLoadOrder = ViewModel.PluginManagerVM.ExportLoadOrder();
                    ViewModel.ProfileManager.UpdateProfile(ViewModel.ProfileManager.CurrentProfile, null, bteLoadOrder, strOptionalFiles, out var strError);

					if (!string.IsNullOrEmpty(strError))
					{
						strError = strError + Environment.NewLine + Environment.NewLine + "Unable to automatically save the profile file, please close the program blocking the reported file and manually click on Save Profile from the profiles context menu";
						MessageBox.Show(strError, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					}
				}
			}
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

			_downloadMonitorControl.Activate();

			if (!ViewModel.OfflineMode)
			{
				toolStripLabelDownloads.Text = String.Format("{0} ({1} {2}) ", toolStripLabelDownloads.Tag, _downloadMonitorControl.ViewModel.ActiveTasks.Count, _downloadMonitorControl.ViewModel.ActiveTasks.Count == 1 ? "File" : "Files");
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

			_downloadMonitorControl.Activate();

			if (!ViewModel.OfflineMode)
			{
				if (e.OldItems != null && e.OldItems.Count > 0)
				{
					foreach (AddModTask Task in e.OldItems)
						if (!String.IsNullOrEmpty(Task.ErrorCode) && Task.ErrorCode == "666" && !(Task.Status == TaskStatus.Cancelling || Task.Status == TaskStatus.Cancelled || Task.Status == TaskStatus.Complete))
						{
							MessageBox.Show(String.Format("The NMM web services have currently been disabled by staff of the sites."
								+ " This is NOT an error with NMM and you DO NOT need to report this error to us."
								+ " This is normally a temporary problem so please try again a bit later on in the day." + Environment.NewLine
								+ "If the staff have provided a reason for this down time we'll display it below: {0}", Environment.NewLine + Environment.NewLine + Task.ErrorInfo), "Warning", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
						}
				}
				toolStripLabelDownloads.Text = String.Format("{0} ({1} {2}) ", toolStripLabelDownloads.Tag, _downloadMonitorControl.ViewModel.ActiveTasks.Count, _downloadMonitorControl.ViewModel.ActiveTasks.Count == 1 ? "File" : "Files");
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

                    if (toolStripProgressBarDownloadSpeed.ColorFillMode == ProgressLabel.FillType.Fixed)
                    {
                        toolStripProgressBarDownloadSpeed.Maximum = 1;
                    }

                    toolStripProgressBarDownloadSpeed.Visible = false;
                }
                else switch (toolStripProgressBarDownloadSpeed.ColorFillMode)
                {
                    case ProgressLabel.FillType.Fixed:
                        toolStripProgressBarDownloadSpeed.Visible = true;
                        toolStripProgressBarDownloadSpeed.Maximum = _downloadMonitorControl.ViewModel.TotalSpeed > 0 ? _downloadMonitorControl.ViewModel.TotalSpeed : 1;
                        toolStripProgressBarDownloadSpeed.Value = toolStripProgressBarDownloadSpeed.Maximum;
                        break;
                    case ProgressLabel.FillType.Ascending:
                    {
                        toolStripProgressBarDownloadSpeed.Visible = true;

                        if (_downloadMonitorControl.ViewModel.TotalMaxProgress > 0)
                        {
                            toolStripProgressBarDownloadSpeed.Value = Convert.ToInt32(Convert.ToSingle(_downloadMonitorControl.ViewModel.TotalProgress) / Convert.ToSingle(_downloadMonitorControl.ViewModel.TotalMaxProgress) * 100);
                            toolStripProgressBarDownloadSpeed.OptionalValue = _downloadMonitorControl.ViewModel.TotalSpeed;
                        }

                        break;
                    }
                    case ProgressLabel.FillType.Descending:
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
			base.OnClosed(e);

            if (!DesignMode)
			{
				ViewModel.EnvironmentInfo.Settings.DockPanelLayouts["mainForm"] = dockPanel1.SaveAsXml();
				ViewModel.EnvironmentInfo.Settings.Save();
			}
		}

		/// <summary>
		/// Returns the UI component being requested when the form's metrics are being loaded.
		/// </summary>
		/// <param name="contentId">The id of the component to return to be positioned.</param>
		/// <returns>The component to return to be positioned.</returns>
		protected IDockContent LoadDockedContent(string contentId)
        {
            if (contentId == typeof(PluginManagerControl).ToString())
            {
                return _pluginManagerControl;
            }

            if (contentId == typeof(ModManagerControl).ToString())
            {
                return _modManagerControl;
            }

            if (contentId == typeof(DownloadMonitorControl).ToString())
            {
                return _downloadMonitorControl;
            }

            if (contentId == typeof(ModActivationMonitorControl).ToString())
            {
                return _modActivationMonitorControl;
            }

            return null;
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
				_modManagerControl.RefreshModList();

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

            return MessageBox.Show(this, p_strMessage, p_strTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK;
		}

		#endregion

		#region Change Game Mode Binding Helpers

		/// <summary>
		/// Binds the change game mode commands to the UI.
		/// </summary>
		protected void BindChangeModeCommands()
        {
            foreach (var changeCommand in ViewModel.ChangeGameModeCommands)
            {
                var isReloadCommand = false;

                if (ViewModel.GameMode.ModeId.Equals(changeCommand?.Id, StringComparison.OrdinalIgnoreCase))
                {
                    changeCommand.Name = $"Reload {changeCommand.Id}";
                    changeCommand.Description = $"Reload {changeCommand.Id}";
                    isReloadCommand = true;
                }

                var toolStripMenuItemChange = new ToolStripMenuItem();
                changeCommand.Executed += ChangeGameModeCommand_Executed;
                new ToolStripItemCommandBinding(toolStripMenuItemChange, changeCommand);

                if (isReloadCommand)
                {
                    spbChangeMode.DropDownItems.Insert(0, toolStripMenuItemChange);
                    spbChangeMode.DropDownItems.Insert(1, new ToolStripSeparator());
                    continue;
                }

                if (changeCommand.Name.Equals("Change Default Game...", StringComparison.OrdinalIgnoreCase))
                {
                    spbChangeMode.DropDownItems.Add(new ToolStripSeparator());
                }

                spbChangeMode.DropDownItems.Add(toolStripMenuItemChange);
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
			spbChangeMode.DropDown.Show();
		}

		#endregion

		#region Tools Binding Helpers

		/// <summary>
		/// Binds the tool launch commands to the UI.
		/// </summary>
		protected void BindToolCommands()
		{
			var resetUiCommand = new Command("Reset UI", "Resets the UI to the default layout.", ResetUI);
            var toolStripMenuItemResetTool = new ToolStripMenuItem {ImageScaling = ToolStripItemImageScaling.None};
            new ToolStripItemCommandBinding(toolStripMenuItemResetTool, resetUiCommand);
			toolStripSplitButtonTools.DropDownItems.Add(toolStripMenuItemResetTool);

			var cmdDisableAllMods = new Command("Disable all active mods", "Disables all active mods.", DisableAllMods);
            var tmiDisableAllMods = new ToolStripMenuItem {Image = Properties.Resources.edit_delete};
            new ToolStripItemCommandBinding(tmiDisableAllMods, cmdDisableAllMods);
			toolStripSplitButtonTools.DropDownItems.Add(tmiDisableAllMods);

			var cmdUninstallAllMods = new Command("Uninstall all active mods", "Uninstalls all active mods.", UninstallAllMods);
            var tmiUninstallAllMods = new ToolStripMenuItem {Image = Properties.Resources.edit_delete_6};
            new ToolStripItemCommandBinding(tmiUninstallAllMods, cmdUninstallAllMods);
			toolStripSplitButtonTools.DropDownItems.Add(tmiUninstallAllMods);

			var cmdPurgeLooseFiles = new Command("Purge Unmanaged Files", "Purge Unmanaged Files.", PurgeLooseFiles);
            var tmiPurgeLooseFiles = new ToolStripMenuItem {Image = Properties.Resources.deleteProfile};
            new ToolStripItemCommandBinding(tmiPurgeLooseFiles, cmdPurgeLooseFiles);
			toolStripSplitButtonTools.DropDownItems.Add(tmiPurgeLooseFiles);

			var cmdCreateBackup = new Command("Create Mod Installation backup.", "Create Mod Installation backup.", CreateBackup);
			var cmdRestoreBackup = new Command("Restore Mod Installation backup", "Restore Mod Installation backup.", RestoreBackup);
			var cmdRestoreBackupProfile = new Command("Restore the backup profile", "Adds the backup profile to the profile list.", RestoreBackupProfile);

			var tmiBackup = new ToolStripMenuItem();
			tmiBackup.Text = "Backup and Restore";
			tmiBackup.Image = Properties.Resources.backup;

            var tmiCreateBackup = new ToolStripMenuItem
            {
                Image = Properties.Resources.createBackup
            };
            new ToolStripItemCommandBinding(tmiCreateBackup, cmdCreateBackup);
			tmiBackup.DropDownItems.AddRange(new ToolStripItem[] { tmiCreateBackup });

            var tmiRestoreBackup = new ToolStripMenuItem {Image = Properties.Resources.restoreBackup};
            new ToolStripItemCommandBinding(tmiRestoreBackup, cmdRestoreBackup);
			tmiBackup.DropDownItems.AddRange(new ToolStripItem[] { tmiRestoreBackup });

            var tmiRestoreBackupProfile = new ToolStripMenuItem {Image = Properties.Resources.change_game_mode};
            new ToolStripItemCommandBinding(tmiRestoreBackupProfile, cmdRestoreBackupProfile);
			tmiBackup.DropDownItems.AddRange(new ToolStripItem[] { tmiRestoreBackupProfile });
			
			toolStripSplitButtonTools.DropDownItems.Add(tmiBackup);
			
			var cmdConfigureVirtualFolders = new Command("Change Virtual folders...", "Virtual folders setup menu.", ChangeVirtualFolders);
            var tmiConfigureVirtualFolders = new ToolStripMenuItem {Image = Properties.Resources.category_folder};
            new ToolStripItemCommandBinding(tmiConfigureVirtualFolders, cmdConfigureVirtualFolders);
			toolStripSplitButtonTools.DropDownItems.Add(tmiConfigureVirtualFolders);

			if (ViewModel.UsesPlugins && ViewModel.SupportsPluginAutoSorting)
			{
				var cmdSortPlugins = new Command("Automatic Plugin Sorting", "Automatically sorts the plugin list.", SortPlugins);
                var tmicmdSortPluginsTool = new ToolStripMenuItem {ImageScaling = ToolStripItemImageScaling.None};
                new ToolStripItemCommandBinding(tmicmdSortPluginsTool, cmdSortPlugins);
				toolStripSplitButtonTools.DropDownItems.Add(tmicmdSortPluginsTool);
			}

            foreach (var tolTool in ViewModel.GameToolLauncher.Tools)
			{
                var tmiTool = new ToolStripMenuItem
                {
                    Tag = tolTool, ImageScaling = ToolStripItemImageScaling.None
                };

                new ToolStripItemCommandBinding(tmiTool, tolTool.LaunchCommand);
				tolTool.DisplayToolView += Tool_DisplayToolView;
				tolTool.CloseToolView += Tool_CloseToolView;
				toolStripSplitButtonTools.DropDownItems.Add(tmiTool);
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
			toolStripSplitButtonTools.DropDown.Show();
		}

		private Point FindControlCoords(string section, string target)
		{
			var coords = new Point(0, 0);
			ToolStripItem rootItem;
			Control root;

			switch (section)
			{
				case "PluginManagerControl":
				case "ModManagerControl":
					root = Controls.Find(section, true)[0];
					if (root.TabIndex == 2)
					{
						if (root.ContainsFocus)
                        {
                            coords.X = root.AccessibilityObject.Bounds.Location.X;
                        }
                        else
                        {
                            coords.X = root.Width + root.AccessibilityObject.Bounds.Location.X;
                        }
                    }
					else
					{
						if (root.ContainsFocus)
                        {
                            coords.X = root.AccessibilityObject.Bounds.Location.X + 60;
                        }
                        else
                        {
                            coords.X = root.Width + root.AccessibilityObject.Bounds.Location.X + 60;
                        }
                    }

					coords.Y = root.AccessibilityObject.Bounds.Location.Y - 60;

					break;

				case "toolStrip1":
					root = Controls.Find(section, true)[0];
					rootItem = ((ToolStrip)root).Items.Find(target, true)[0];
					coords.X = rootItem.AccessibilityObject.Bounds.Location.X - 10;
					coords.Y = rootItem.AccessibilityObject.Bounds.Location.Y - 30;
					break;

				case "tssDownload":
					root = Controls.Find(section, true)[0];
					rootItem = ((StatusStrip)root).Items.Find(target, true)[0];

                    if (rootItem.Visible)
					{
						coords.X = rootItem.AccessibilityObject.Bounds.Location.X - 10;
						coords.Y = rootItem.AccessibilityObject.Bounds.Location.Y - 60;
					}
					break;

				case "ModManager.toolStrip1":
					section = "toolStrip1";
					root = _modManagerControl.Controls.Find(section, true)[0];
					rootItem = ((ToolStrip)root).Items.Find(target, true)[0];
					coords.X = rootItem.AccessibilityObject.Bounds.Location.X - 5;
					coords.Y = rootItem.AccessibilityObject.Bounds.Location.Y - 10;
					break;

				case "DownloadManager.toolStrip1":
					section = "toolStrip1";
					root = _downloadMonitorControl.Controls.Find(section, true)[0];
					rootItem = ((ToolStrip)root).Items.Find(target, true)[0];

					switch (_downloadMonitorControl.DockState)
					{
						case DockState.DockBottomAutoHide:
							_downloadMonitorControl.DockState = DockState.DockBottom;
							break;
						case DockState.DockLeftAutoHide:
							_downloadMonitorControl.DockState = DockState.DockLeft;
							break;
						case DockState.DockRightAutoHide:
							_downloadMonitorControl.DockState = DockState.DockRight;
							break;
						case DockState.DockTopAutoHide:
							_downloadMonitorControl.DockState = DockState.DockTop;
							break;
					}

					if (!_downloadMonitorControl.Visible)
                    {
                        _downloadMonitorControl.Show();
                    }

                    coords.X = rootItem.AccessibilityObject.Bounds.Location.X - 10;
					coords.Y = rootItem.AccessibilityObject.Bounds.Location.Y - 40;
					break;

				case "CLWCategoryListView":
					coords.X = _modManagerControl.clwCategoryView.AccessibilityObject.Bounds.Location.X;
					coords.Y = _modManagerControl.clwCategoryView.AccessibilityObject.Bounds.Location.Y - 40;
					break;
					
					case "ModActivationMonitorListView":
						switch (_modActivationMonitorControl.DockState)
						{
							case DockState.DockBottomAutoHide:
								_modActivationMonitorControl.DockState = DockState.DockBottom;
								break;
							case DockState.DockLeftAutoHide:
								_modActivationMonitorControl.DockState = DockState.DockLeft;
								break;
							case DockState.DockRightAutoHide:
								_modActivationMonitorControl.DockState = DockState.DockRight;
								break;
							case DockState.DockTopAutoHide:
								_modActivationMonitorControl.DockState = DockState.DockTop;
								break;
						}

						if (!_modActivationMonitorControl.Visible)
                        {
                            _modActivationMonitorControl.Show();
                        }

                        coords.X = _modActivationMonitorControl.AccessibilityObject.Bounds.Location.X + 20;
						coords.Y = _modActivationMonitorControl.AccessibilityObject.Bounds.Location.Y - 70;
						break;

					case "ModActivationMonitorControl.toolStrip1":
						section = "toolStrip1";
						root = _modActivationMonitorControl.Controls.Find(section, true)[0];
						rootItem = ((ToolStrip)root).Items.Find(target, true)[0];

						if (rootItem.Visible)
						{
							coords.X = rootItem.AccessibilityObject.Bounds.Location.X - 10;
							coords.Y = rootItem.AccessibilityObject.Bounds.Location.Y - 40;
						}
						break;
			}

			return coords;
		}

		#endregion

		#region Open Folders Helpers

		/// <summary>
		/// Binds the tool launch commands to the UI.
		/// </summary>
		protected void BindFolderCommands()
		{
			var cmdGameFolder = new Command("Open Game Folder", "Open the game's root folder in the explorer window.", OpenGameFolder);
            var tmiGameFolder = new ToolStripMenuItem {ImageScaling = ToolStripItemImageScaling.None};
            new ToolStripItemCommandBinding(tmiGameFolder, cmdGameFolder);
			spbFolders.DropDownItems.Add(tmiGameFolder);

			var cmdModsFolder = new Command("Open NMM's Mods Folder", "Open NMM's mods folder in the explorer window.", OpenModsFolder);
            var tmiModsFolder = new ToolStripMenuItem {ImageScaling = ToolStripItemImageScaling.None};
            new ToolStripItemCommandBinding(tmiModsFolder, cmdModsFolder);
			spbFolders.DropDownItems.Add(tmiModsFolder);

			var cmdInstallFolder = new Command("Open NMM's Install Info Folder", "Open NMM's install info folder in the explorer window.", OpenInstallFolder);
            var tmiInstallFolder = new ToolStripMenuItem {ImageScaling = ToolStripItemImageScaling.None};
            new ToolStripItemCommandBinding(tmiInstallFolder, cmdInstallFolder);
			spbFolders.DropDownItems.Add(tmiInstallFolder);
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
			spbFolders.DropDown.Show();
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
			spbHelp.DropDown.Show();
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the help links.
		/// </summary>
		/// <remarks>
		/// This launches the link in the user's browser.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void tmiHelp_Click(object sender, EventArgs e)
		{
			var helpLink = (HelpInformation.HelpLink)((ToolStripMenuItem)sender).Tag;

            if (helpLink == null)
            {
                return;
            }

            try
			{
				Process.Start(helpLink.Url);
			}
			catch (Win32Exception)
			{
				MessageBox.Show(this, "Cannot find program to open: " + helpLink.Url, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
			ProgressDialog.ShowDialog(this, e.Argument, false);

			if (ViewModel.GameMode.UsesPlugins)
			{
				var dctLoadOrder = ViewModel.ImportProfileLoadOrder();

                if (dctLoadOrder != null && dctLoadOrder.Count > 0)
                {
                    ViewModel.ApplyLoadOrder(dctLoadOrder, false);
                }
            }

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

            if (e.Argument?.ReturnValue is bool)
            {
                if ((bool)e.Argument.ReturnValue)
                {
                    MessageBox.Show("Restore Complete! NMM will restart automatically to apply the changes.", "Restore Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    ViewModel.RequestGameMode(ViewModel.GameMode.ModeId);
                    ChangeGameModeCommand_Executed(sender, new EventArgs());
                }
            }
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
					ViewModel.ExecuteProfileSwitch(this, false);
					return;
				}
			}
			else
			{
				ViewModel.ExecuteProfileSwitch(this, false);
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
				ViewModel.ExecuteProfileSwitch(this, false);
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
					ViewModel.ProfileManager.ModBackedProfiles.Add(new ModProfile(((ModProfile)bpBackedProfile).Id, strResult, ViewModel.ModRepository.GameDomainName.ToString(), ((ModProfile)bpBackedProfile).ModCount, false, ((ModProfile)bpBackedProfile).OnlineID, ((ModProfile)bpBackedProfile).Name, System.DateTime.Now.ToShortDateString(), ((ModProfile)bpBackedProfile).IsShared, ((ModProfile)bpBackedProfile).Version.ToString(), ((ModProfile)bpBackedProfile).Author,((ModProfile)bpBackedProfile).WorksWithSaves, false));
					ViewModel.ProfileManager.SaveOnlineConfig();
				}
			}
			else
			{
				MessageBox.Show(strResult, "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
						MessageBox.Show(error, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_ApplyingImportedLoadOrder, sender, e);
				return;
			}

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
				MessageBox.Show("Unable to create the backup: " + e.Argument.ReturnValue.ToString(), "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
			else
            {
                MessageBox.Show("Backup Complete!", "Backup Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
					MessageBox.Show(error);
				}
				else
				{
					MessageBox.Show("An error occured during the Restore!");
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
                MessageBox.Show("Purge Complete!", "Purge Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

		#endregion

		#region Game Launch Binding Helpers

		/// <summary>
		/// Binds the game launch commands to the UI.
		/// </summary>
		protected void BindLaunchCommands()
		{
            foreach (var cmdLaunch in ViewModel.GameLauncher.LaunchCommands)
			{
                var tmiLaunch = new ToolStripMenuItem {Tag = cmdLaunch};
                new ToolStripItemCommandBinding(tmiLaunch, cmdLaunch);
				spbLaunch.DropDownItems.Add(tmiLaunch);

                if (string.Equals(cmdLaunch.Id, _viewModel.SelectedGameLaunchCommandId))
				{
					spbLaunch.DefaultItem = tmiLaunch;
					spbLaunch.Text = spbLaunch.DefaultItem.Text;
					spbLaunch.Image = spbLaunch.DefaultItem.Image;
				}
			}

			if (spbLaunch.DefaultItem == null)
			{
				if (spbLaunch.DropDownItems.Count > 0)
				{
					spbLaunch.DefaultItem = spbLaunch.DropDownItems[0];
					spbLaunch.Text = spbLaunch.DefaultItem.Text;
					spbLaunch.Image = spbLaunch.DefaultItem.Image;
				}
				else
				{
					spbLaunch.Text = "Launch Game";
					spbLaunch.Image = null;
					spbLaunch.Enabled = false;
				}
			}

			ViewModel.ConfirmCloseAfterGameLaunch = ConfirmCloseAfterGameLaunch;
			ViewModel.GameLauncher.GameLaunched += GameLauncher_GameLaunched;
		}

 		/// <summary>
		/// Binds the game profiles commands to the UI.
		/// </summary>
		protected void BindProfileCommands()
		{
			if (ViewModel.ProfileManager.Initialized)
			{
				spbProfiles.DropDownItems.Clear();
				spbProfiles.DefaultItem = null;
                var tmiMenuItem = new ToolStripMenuItem {Tag = "New", Text = "New Profile"};
                spbProfiles.DropDownItems.Add(tmiMenuItem);
                tmiMenuItem = new ToolStripMenuItem {Tag = "Rename", Text = "Rename Current Profile"};
                spbProfiles.DropDownItems.Add(tmiMenuItem);
                tmiMenuItem = new ToolStripMenuItem {Tag = "Remove", Text = "Remove Current Profile"};
                spbProfiles.DropDownItems.Add(tmiMenuItem);
                tmiMenuItem = new ToolStripMenuItem {Tag = "Save", Text = "Save Current Profile"};
                spbProfiles.DropDownItems.Add(tmiMenuItem);
				tmiMenuItem = new ToolStripMenuItem();
				spbProfiles.DropDownItems.Add(new ToolStripSeparator());

				if (ViewModel.ProfileManager.CurrentProfile != null)
				{
                    var tmiProfile = new ToolStripMenuItem {Tag = ViewModel.ProfileManager.CurrentProfile};
                    var name = ViewModel.ProfileManager.CurrentProfile.Name;

                    if (name.Length > 64)
                    {
                        name = name.Substring(0, 62) + "..";
                    }

                    tmiProfile.Text = $"{name} ({ViewModel.ProfileManager.CurrentProfile.ModCount})";
					spbProfiles.DropDownItems.Add(tmiProfile);

					if (ViewModel.ProfileManager.CurrentProfile.IsDefault)
					{
						spbProfiles.DefaultItem = tmiProfile;
						spbProfiles.Text = name;
						spbProfiles.Image = spbProfiles.Image;
					}

					tmiProfile.Enabled = false;
				}

				foreach (var impProfile in ViewModel.ProfileManager.ModProfiles.OrderBy(x => x.Name))
				{
					if (impProfile == ViewModel.ProfileManager.CurrentProfile)
                    {
                        continue;
                    }

                    var strProfileName = impProfile.Name;

                    if (strProfileName.Length > 64)
                    {
                        strProfileName = strProfileName.Substring(0, 62) + "..";
                    }

                    var tmiProfile = new ToolStripMenuItem
                    {
                        Tag = impProfile, Text = $"{strProfileName} ({impProfile.ModCount})"
                    };
                    spbProfiles.DropDownItems.Add(tmiProfile);

                    var tmiItem = new ToolStripMenuItem
                    {
                        Tag = "RenameProfile", Text = "Rename Profile", Name = impProfile.Name
                    };
                    tmiProfile.DropDownItems.Add(tmiItem);

                    tmiItem = new ToolStripMenuItem
                    {
                        Tag = "RemoveProfile", Text = "Remove Profile", Name = impProfile.Name
                    };
                    tmiProfile.DropDownItems.Add(tmiItem);

					if (ViewModel.GameMode.UsesPlugins)
					{
                        tmiItem = new ToolStripMenuItem
                        {
                            Tag = "ImportLoadorder", Text = "Import Profile's Load Order", Name = impProfile.Id
                        };
                        tmiProfile.DropDownItems.Add(tmiItem);
					}

					if (impProfile.IsDefault)
					{
						spbProfiles.DefaultItem = tmiProfile;
						spbProfiles.Text = strProfileName;
						spbProfiles.Image = spbProfiles.Image;
					}

					tmiProfile.DropDownItemClicked += (o, e) => { tmiItem_DropDownItemClicked(impProfile, e); };
				}

				if (spbProfiles.DefaultItem == null)
				{
					if (spbProfiles.DropDownItems.Count > 0)
					{
						spbProfiles.DefaultItem = spbProfiles.DropDownItems[0];
						spbProfiles.Text = spbProfiles.DefaultItem.Text;
						spbProfiles.Image = spbProfiles.Image;
					}
				}

				spbProfiles.Visible = true;
			}
			else
            {
                spbProfiles.Visible = false;
            }
        }

		/// <summary>
		/// Binds the SupportedTools launch commands to the UI.
		/// </summary>
		protected void BindSupportedToolsCommands()
		{
			spbSupportedTools.DropDownItems.Clear();
			spbSupportedTools.DefaultItem = null;

			if (ViewModel.SupportedToolsLauncher != null)
			{
				foreach (var cmdLaunch in ViewModel.SupportedToolsLauncher.LaunchCommands)
				{
                    var tmiLaunch = new ToolStripMenuItem {Tag = cmdLaunch};

                    if (tmiLaunch.Image == null)
                    {
                        tmiLaunch.Image = ToolStripRenderer.CreateDisabledImage(Properties.Resources.supported_tools_flat);
                    }

                    new ToolStripItemCommandBinding(tmiLaunch, cmdLaunch);
					tmiLaunch.MouseUp += TmiLaunch_Click;
					spbSupportedTools.DropDownItems.Add(tmiLaunch);
				}

				if (spbSupportedTools.DefaultItem == null)
				{
					if (spbSupportedTools.DropDownItems.Count > 0)
					{
						spbSupportedTools.Text = "Supported Tools";
						spbSupportedTools.Image = Properties.Resources.supported_tools_flat;
					}
				}
			}
			else
            {
                spbSupportedTools.Visible = false;
            }
        }

		private void TmiLaunch_Click(object sender, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Right)
			{
                var tmiClicked = (ToolStripMenuItem) sender;

                if (tmiClicked?.Tag != null && tmiClicked.Tag.GetType() == typeof(Command))
                {
                    spbSupportedTools.DropDown.Close();
                    ViewModel.SupportedToolsLauncher.ConfigCommand(((Command)tmiClicked.Tag).Id);
                }
            }
		}

		/// <summary>
		/// Handles the <see cref="ToolStripDropDownItem.DropDownItemClicked"/> of the launch game
		/// split button.
		/// </summary>
		/// <remarks>
		/// This makes the last selected function the new default for the button.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="ToolStripItemClickedEventArgs"/> describing the event arguments.</param>
		private void spbLaunch_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e)
		{
			spbLaunch.DefaultItem = e.ClickedItem;
			spbLaunch.Text = e.ClickedItem.Text;
			toolStrip1.SuspendLayout();
			spbLaunch.Image = e.ClickedItem.Image;
			toolStrip1.ResumeLayout();
			_viewModel.SelectedGameLaunchCommandId = ((Command)e.ClickedItem.Tag).Id;
		}

		/// <summary>
		/// Handles the <see cref="ToolStripSplitButton.ButtonClick"/> of the launch game
		/// split button.
		/// </summary>
		/// <remarks>
		/// This makes the last selected function the new default for the button.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="ToolStripItemClickedEventArgs"/> describing the event arguments.</param>
		private void spbSupportedTools_ButtonClick(object sender, EventArgs e)
		{
			spbSupportedTools.DropDown.Show();
		}

		/// <summary>
		/// Handles the <see cref="ToolStripDropDownItem.DropDownItemClicked"/> of the launch game
		/// split button.
		/// </summary>
		/// <remarks>
		/// This makes the last selected function the new default for the button.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="ToolStripItemClickedEventArgs"/> describing the event arguments.</param>
		private void tmiItem_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e)
		{
			if (e.ClickedItem.Tag is string)
			{
				var strCommand = e.ClickedItem.Tag.ToString();

				var mopProfile = (ModProfile)sender;
				
				switch (strCommand)
				{
					case "RenameProfile":
						if (mopProfile != null)
						{
							var pdDialog = PromptDialog.ShowDialog("Rename Online", this, "Type the new name:", "Rename Local", mopProfile.Name, null, null);

                            if (pdDialog != null)
 							{
								if (!string.IsNullOrEmpty(pdDialog.EnteredText) && !pdDialog.EnteredText.Equals(mopProfile.Name, StringComparison.InvariantCulture))
								{
									if (pdDialog.EnteredText.Length > 64)
									{
										MessageBox.Show("Unable to rename the profile!" + Environment.NewLine + Environment.NewLine + "The new profile name is too long, maximum 64 characters.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
										return;
									}

                                    if (string.IsNullOrWhiteSpace(pdDialog.EnteredText.Replace("|", string.Empty)))
									{
										MessageBox.Show("Unable to rename the profile!" + Environment.NewLine + Environment.NewLine + "The new profile name is empty or contains unsupported special characters (eg. | ).", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
										return;
									}

                                    mopProfile.Name = pdDialog.EnteredText;
									ViewModel.ProfileManager.RenameProfile(mopProfile, mopProfile.Name);
									BindProfileCommands();
								}
							}
						}
						break;
					case "RemoveProfile":
						if (mopProfile != null)
						{
							var pdDialog = PromptDialog.ShowDialog("Remove Online", this, String.Format("Are you sure you want to remove the current profile: {0}", mopProfile.Name), "Remove Local", null, null, null);

                            if (pdDialog != null)
                            {
                                ViewModel.ProfileManager.RemoveProfile(mopProfile);
                            }
                        }
						break;
					case "ImportLoadorder":
						if (!string.IsNullOrEmpty(e.ClickedItem.Name))
						{
							var drResult = ExtendedMessageBox.Show(this, $"Are you sure you want to import this profile's loadorder? '{mopProfile.Name}'", "Import Loadorder", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                            if (drResult == DialogResult.Yes)
							{
								if (mopProfile.LoadOrder == null)
								{
                                    ViewModel.ProfileManager.LoadProfile(mopProfile, out var dicProfile);

                                    if (dicProfile != null && dicProfile.Count > 0 && dicProfile.ContainsKey("loadorder"))
									{
										ViewModel.PluginManagerVM.ImportLoadOrderFromString(dicProfile["loadorder"]);
									}
								}
								else
                                {
                                    ViewModel.PluginManagerVM.ImportLoadOrderFromDictionary(mopProfile.LoadOrder);
                                }
                            }
						}
						break;
                }
			}
		}

 		/// <summary>
		/// Handles the <see cref="ToolStripDropDownItem.DropDownItemClicked"/> of the profiles
		/// split button.
		/// </summary>
		/// <remarks>
		/// This makes the last selected function the new default for the button.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="ToolStripItemClickedEventArgs"/> describing the event arguments.</param>
		private void spbProfiles_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e)
		{
			if (e.ClickedItem.Tag != null)
			{
				if (e.ClickedItem.Tag is string)
				{
					var strCommand = e.ClickedItem.Tag.ToString();

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
											MessageBox.Show(error, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
									MessageBox.Show(error, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

					spbProfiles.DefaultItem = e.ClickedItem;
					spbProfiles.Text = e.ClickedItem.Text;
					toolStrip1.SuspendLayout();
					spbProfiles.Image = e.ClickedItem.Image;
					toolStrip1.ResumeLayout();

					var impProfile = (IModProfile)e.ClickedItem.Tag;

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
					MessageBox.Show(string.Format("There were issues saving the current profile: " + Environment.NewLine + Environment.NewLine + "{0}" + Environment.NewLine, e.Message), "Warning",  MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                MessageBox.Show(this, e.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

			var changeMode = new Bitmap(spbChangeMode.Image);

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

			spbChangeMode.Image = changeMode;
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
            else if (_balloonManager?.balloonHelp != null && _balloonManager.balloonHelp.Visible)
            {
                _balloonManager.balloonHelp.Close();
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
			Process.Start("https://discord.gg/JZ4tZ5KFQX");
		}

		private void tsbPatreon_Click(object sender, EventArgs e)
		{
			Process.Start("https://www.patreon.com/NMMCE");
		}
	}
}
