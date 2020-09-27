namespace Nexus.Client
{
	partial class MainForm
	{
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		#region Windows Form Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
			WeifenLuo.WinFormsUI.Docking.DockPanelSkin dockPanelSkin2 = new WeifenLuo.WinFormsUI.Docking.DockPanelSkin();
			WeifenLuo.WinFormsUI.Docking.AutoHideStripSkin autoHideStripSkin2 = new WeifenLuo.WinFormsUI.Docking.AutoHideStripSkin();
			WeifenLuo.WinFormsUI.Docking.DockPanelGradient dockPanelGradient4 = new WeifenLuo.WinFormsUI.Docking.DockPanelGradient();
			WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient8 = new WeifenLuo.WinFormsUI.Docking.TabGradient();
			WeifenLuo.WinFormsUI.Docking.DockPaneStripSkin dockPaneStripSkin2 = new WeifenLuo.WinFormsUI.Docking.DockPaneStripSkin();
			WeifenLuo.WinFormsUI.Docking.DockPaneStripGradient dockPaneStripGradient2 = new WeifenLuo.WinFormsUI.Docking.DockPaneStripGradient();
			WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient9 = new WeifenLuo.WinFormsUI.Docking.TabGradient();
			WeifenLuo.WinFormsUI.Docking.DockPanelGradient dockPanelGradient5 = new WeifenLuo.WinFormsUI.Docking.DockPanelGradient();
			WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient10 = new WeifenLuo.WinFormsUI.Docking.TabGradient();
			WeifenLuo.WinFormsUI.Docking.DockPaneStripToolWindowGradient dockPaneStripToolWindowGradient2 = new WeifenLuo.WinFormsUI.Docking.DockPaneStripToolWindowGradient();
			WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient11 = new WeifenLuo.WinFormsUI.Docking.TabGradient();
			WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient12 = new WeifenLuo.WinFormsUI.Docking.TabGradient();
			WeifenLuo.WinFormsUI.Docking.DockPanelGradient dockPanelGradient6 = new WeifenLuo.WinFormsUI.Docking.DockPanelGradient();
			WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient13 = new WeifenLuo.WinFormsUI.Docking.TabGradient();
			WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient14 = new WeifenLuo.WinFormsUI.Docking.TabGradient();
			this.toolStrip1 = new System.Windows.Forms.ToolStrip();
			this.spbLaunch = new System.Windows.Forms.ToolStripSplitButton();
			this.spbProfiles = new System.Windows.Forms.ToolStripSplitButton();
			this.spbHelp = new System.Windows.Forms.ToolStripSplitButton();
			this.spbChangeMode = new System.Windows.Forms.ToolStripSplitButton();
			this.toolStripSplitButtonTools = new System.Windows.Forms.ToolStripSplitButton();
			this.spbFolders = new System.Windows.Forms.ToolStripSplitButton();
			this.tsbSettings = new System.Windows.Forms.ToolStripButton();
			this.toolStripTextBoxFind = new System.Windows.Forms.ToolStripTextBox();
			this.spbSupportedTools = new System.Windows.Forms.ToolStripSplitButton();
			this.tsbUpdate = new System.Windows.Forms.ToolStripButton();
			this.dockPanel1 = new WeifenLuo.WinFormsUI.Docking.DockPanel();
			this.tssDownload = new System.Windows.Forms.StatusStrip();
			this.toolStripButtonOnlineStatus = new System.Windows.Forms.ToolStripButton();
			this.toolStripLabelDownloads = new System.Windows.Forms.ToolStripLabel();
			this.toolStripProgressBarDownloadSpeed = new Nexus.Client.UI.Controls.CustomizableToolStripProgressBar();
			this.toolStripButtonGoPremium = new System.Windows.Forms.ToolStripButton();
			this.toolStripLabelLoginMessage = new System.Windows.Forms.ToolStripLabel();
			this.toolStripButtonRateLimit = new System.Windows.Forms.ToolStripButton();
			this.tlbStatusFiller = new System.Windows.Forms.ToolStripStatusLabel();
			this.toolStripLabelBottomBarFeedback = new System.Windows.Forms.ToolStripLabel();
			this.toolStripLabelBottomBarFeedbackCounter = new System.Windows.Forms.ToolStripLabel();
			this.toolStripButtonLoader = new System.Windows.Forms.ToolStripButton();
			this.tlbModSeparator = new System.Windows.Forms.ToolStripSeparator();
			this.toolStripLabelPluginsCounter = new System.Windows.Forms.ToolStripLabel();
			this.toolStripLabelActivePluginsCounter = new System.Windows.Forms.ToolStripLabel();
			this.toolStripSeparatorPluginSeparator = new System.Windows.Forms.ToolStripSeparator();
			this.tlbModsCounter = new System.Windows.Forms.ToolStripLabel();
			this.toolStrip1.SuspendLayout();
			this.tssDownload.SuspendLayout();
			this.SuspendLayout();
			// 
			// toolStrip1
			// 
			this.m_fpdFontProvider.SetFontSet(this.toolStrip1, "MenuText");
			this.m_fpdFontProvider.SetFontSize(this.toolStrip1, 9F);
			this.toolStrip1.ImageScalingSize = new System.Drawing.Size(32, 32);
			this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.spbLaunch,
            this.spbProfiles,
            this.spbHelp,
            this.spbChangeMode,
            this.toolStripSplitButtonTools,
            this.spbFolders,
            this.tsbSettings,
            this.toolStripTextBoxFind,
            this.spbSupportedTools,
            this.tsbUpdate});
			this.toolStrip1.Location = new System.Drawing.Point(0, 0);
			this.toolStrip1.Name = "toolStrip1";
			this.toolStrip1.Size = new System.Drawing.Size(804, 39);
			this.toolStrip1.TabIndex = 0;
			this.toolStrip1.Text = "toolStrip1";
			// 
			// spbLaunch
			// 
			this.spbLaunch.Image = ((System.Drawing.Image)(resources.GetObject("spbLaunch.Image")));
			this.spbLaunch.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.spbLaunch.Name = "spbLaunch";
			this.spbLaunch.Size = new System.Drawing.Size(166, 36);
			this.spbLaunch.Text = "toolStripSplitButton1";
			this.spbLaunch.DropDownItemClicked += new System.Windows.Forms.ToolStripItemClickedEventHandler(this.spbLaunch_DropDownItemClicked);
			// 
			// spbProfiles
			// 
			this.spbProfiles.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.spbProfiles.Name = "spbProfiles";
			this.spbProfiles.Size = new System.Drawing.Size(64, 36);
			this.spbProfiles.Text = "Profiles";
			this.spbProfiles.DropDownItemClicked += new System.Windows.Forms.ToolStripItemClickedEventHandler(this.spbProfiles_DropDownItemClicked);
			// 
			// spbHelp
			// 
			this.spbHelp.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
			this.spbHelp.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.spbHelp.Image = global::Nexus.Client.Properties.Resources.help_3;
			this.spbHelp.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.spbHelp.Name = "spbHelp";
			this.spbHelp.Size = new System.Drawing.Size(48, 36);
			this.spbHelp.Text = "Help";
			this.spbHelp.ButtonClick += new System.EventHandler(this.spbHelp_ButtonClick);
			// 
			// spbChangeMode
			// 
			this.spbChangeMode.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
			this.spbChangeMode.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.spbChangeMode.Image = global::Nexus.Client.Properties.Resources.change_game_mode;
			this.spbChangeMode.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.spbChangeMode.Name = "spbChangeMode";
			this.spbChangeMode.Size = new System.Drawing.Size(48, 36);
			this.spbChangeMode.Text = "Change Game Mode";
			this.spbChangeMode.ButtonClick += new System.EventHandler(this.spbChangeMode_ButtonClick);
			// 
			// toolStripSplitButtonTools
			// 
			this.toolStripSplitButtonTools.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.toolStripSplitButtonTools.Image = global::Nexus.Client.Properties.Resources.preferences_system_4;
			this.toolStripSplitButtonTools.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.toolStripSplitButtonTools.Name = "toolStripSplitButtonTools";
			this.toolStripSplitButtonTools.Size = new System.Drawing.Size(48, 36);
			this.toolStripSplitButtonTools.Text = "Tools";
			this.toolStripSplitButtonTools.ButtonClick += new System.EventHandler(this.spbTools_ButtonClick);
			// 
			// spbFolders
			// 
			this.spbFolders.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.spbFolders.Image = global::Nexus.Client.Properties.Resources.folders_open;
			this.spbFolders.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.spbFolders.Name = "spbFolders";
			this.spbFolders.Size = new System.Drawing.Size(48, 36);
			this.spbFolders.Text = "Open folders";
			this.spbFolders.ButtonClick += new System.EventHandler(this.spbFolders_ButtonClick);
			// 
			// tsbSettings
			// 
			this.tsbSettings.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbSettings.Image = global::Nexus.Client.Properties.Resources.system_settings;
			this.tsbSettings.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbSettings.Name = "tsbSettings";
			this.tsbSettings.Size = new System.Drawing.Size(36, 36);
			this.tsbSettings.Text = "Settings";
			this.tsbSettings.Click += new System.EventHandler(this.tsbSettings_Click);
			// 
			// toolStripTextBoxFind
			// 
			this.toolStripTextBoxFind.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
			this.toolStripTextBoxFind.Font = new System.Drawing.Font("Segoe UI", 9F);
			this.toolStripTextBoxFind.Name = "toolStripTextBoxFind";
			this.toolStripTextBoxFind.Size = new System.Drawing.Size(100, 39);
			this.toolStripTextBoxFind.Visible = false;
			this.toolStripTextBoxFind.KeyUp += new System.Windows.Forms.KeyEventHandler(this.tstFind_KeyUp);
			// 
			// spbSupportedTools
			// 
			this.spbSupportedTools.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.spbSupportedTools.Image = global::Nexus.Client.Properties.Resources.supported_tools;
			this.spbSupportedTools.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.spbSupportedTools.Name = "spbSupportedTools";
			this.spbSupportedTools.Size = new System.Drawing.Size(48, 36);
			this.spbSupportedTools.Text = "Supported Tools";
			this.spbSupportedTools.ButtonClick += new System.EventHandler(this.spbSupportedTools_ButtonClick);
			// 
			// tsbUpdate
			// 
			this.tsbUpdate.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
			this.tsbUpdate.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbUpdate.Image = global::Nexus.Client.Properties.Resources.system_software_update_2;
			this.tsbUpdate.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbUpdate.Name = "tsbUpdate";
			this.tsbUpdate.Size = new System.Drawing.Size(36, 36);
			this.tsbUpdate.Text = "toolStripButton1";
			// 
			// dockPanel1
			// 
			this.dockPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.dockPanel1.DockBackColor = System.Drawing.SystemColors.Control;
			this.dockPanel1.DocumentStyle = WeifenLuo.WinFormsUI.Docking.DocumentStyle.DockingWindow;
			this.dockPanel1.Location = new System.Drawing.Point(0, 39);
			this.dockPanel1.Name = "dockPanel1";
			this.dockPanel1.Size = new System.Drawing.Size(804, 458);
			dockPanelGradient4.EndColor = System.Drawing.SystemColors.ControlLight;
			dockPanelGradient4.StartColor = System.Drawing.SystemColors.ControlLight;
			autoHideStripSkin2.DockStripGradient = dockPanelGradient4;
			tabGradient8.EndColor = System.Drawing.SystemColors.Control;
			tabGradient8.StartColor = System.Drawing.SystemColors.Control;
			tabGradient8.TextColor = System.Drawing.SystemColors.ControlDarkDark;
			autoHideStripSkin2.TabGradient = tabGradient8;
			autoHideStripSkin2.TextFont = new System.Drawing.Font("Segoe UI", 9F);
			dockPanelSkin2.AutoHideStripSkin = autoHideStripSkin2;
			tabGradient9.EndColor = System.Drawing.SystemColors.ControlLightLight;
			tabGradient9.StartColor = System.Drawing.SystemColors.ControlLightLight;
			tabGradient9.TextColor = System.Drawing.SystemColors.ControlText;
			dockPaneStripGradient2.ActiveTabGradient = tabGradient9;
			dockPanelGradient5.EndColor = System.Drawing.SystemColors.Control;
			dockPanelGradient5.StartColor = System.Drawing.SystemColors.Control;
			dockPaneStripGradient2.DockStripGradient = dockPanelGradient5;
			tabGradient10.EndColor = System.Drawing.SystemColors.ControlLight;
			tabGradient10.StartColor = System.Drawing.SystemColors.ControlLight;
			tabGradient10.TextColor = System.Drawing.SystemColors.ControlText;
			dockPaneStripGradient2.InactiveTabGradient = tabGradient10;
			dockPaneStripSkin2.DocumentGradient = dockPaneStripGradient2;
			dockPaneStripSkin2.TextFont = new System.Drawing.Font("Segoe UI", 9F);
			tabGradient11.EndColor = System.Drawing.SystemColors.ActiveCaption;
			tabGradient11.LinearGradientMode = System.Drawing.Drawing2D.LinearGradientMode.Vertical;
			tabGradient11.StartColor = System.Drawing.SystemColors.GradientActiveCaption;
			tabGradient11.TextColor = System.Drawing.SystemColors.ActiveCaptionText;
			dockPaneStripToolWindowGradient2.ActiveCaptionGradient = tabGradient11;
			tabGradient12.EndColor = System.Drawing.SystemColors.Control;
			tabGradient12.StartColor = System.Drawing.SystemColors.Control;
			tabGradient12.TextColor = System.Drawing.SystemColors.ControlText;
			dockPaneStripToolWindowGradient2.ActiveTabGradient = tabGradient12;
			dockPanelGradient6.EndColor = System.Drawing.SystemColors.ControlLight;
			dockPanelGradient6.StartColor = System.Drawing.SystemColors.ControlLight;
			dockPaneStripToolWindowGradient2.DockStripGradient = dockPanelGradient6;
			tabGradient13.EndColor = System.Drawing.SystemColors.InactiveCaption;
			tabGradient13.LinearGradientMode = System.Drawing.Drawing2D.LinearGradientMode.Vertical;
			tabGradient13.StartColor = System.Drawing.SystemColors.GradientInactiveCaption;
			tabGradient13.TextColor = System.Drawing.SystemColors.InactiveCaptionText;
			dockPaneStripToolWindowGradient2.InactiveCaptionGradient = tabGradient13;
			tabGradient14.EndColor = System.Drawing.Color.Transparent;
			tabGradient14.StartColor = System.Drawing.Color.Transparent;
			tabGradient14.TextColor = System.Drawing.SystemColors.ControlDarkDark;
			dockPaneStripToolWindowGradient2.InactiveTabGradient = tabGradient14;
			dockPaneStripSkin2.ToolWindowGradient = dockPaneStripToolWindowGradient2;
			dockPanelSkin2.DockPaneStripSkin = dockPaneStripSkin2;
			this.dockPanel1.Skin = dockPanelSkin2;
			this.dockPanel1.TabIndex = 3;
			// 
			// tssDownload
			// 
			this.tssDownload.AutoSize = false;
			this.tssDownload.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripButtonOnlineStatus,
            this.toolStripLabelDownloads,
            this.toolStripProgressBarDownloadSpeed,
            this.toolStripButtonGoPremium,
            this.toolStripLabelLoginMessage,
            this.toolStripButtonRateLimit,
            this.tlbStatusFiller,
            this.toolStripLabelBottomBarFeedback,
            this.toolStripLabelBottomBarFeedbackCounter,
            this.toolStripButtonLoader,
            this.tlbModSeparator,
            this.toolStripLabelPluginsCounter,
            this.toolStripLabelActivePluginsCounter,
            this.toolStripSeparatorPluginSeparator,
            this.tlbModsCounter});
			this.tssDownload.Location = new System.Drawing.Point(0, 497);
			this.tssDownload.Name = "tssDownload";
			this.tssDownload.ShowItemToolTips = true;
			this.tssDownload.Size = new System.Drawing.Size(804, 36);
			this.tssDownload.TabIndex = 4;
			// 
			// toolStripButtonOnlineStatus
			// 
			this.toolStripButtonOnlineStatus.AutoSize = false;
			this.toolStripButtonOnlineStatus.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.toolStripButtonOnlineStatus.Image = global::Nexus.Client.Properties.Resources.offline_icon;
			this.toolStripButtonOnlineStatus.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
			this.toolStripButtonOnlineStatus.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.toolStripButtonOnlineStatus.Name = "toolStripButtonOnlineStatus";
			this.toolStripButtonOnlineStatus.Size = new System.Drawing.Size(36, 34);
			this.toolStripButtonOnlineStatus.ToolTipText = "Login";
			// 
			// toolStripLabelDownloads
			// 
			this.toolStripLabelDownloads.Name = "toolStripLabelDownloads";
			this.toolStripLabelDownloads.Size = new System.Drawing.Size(0, 34);
			// 
			// toolStripProgressBarDownloadSpeed
			// 
			this.toolStripProgressBarDownloadSpeed.AutoSize = false;
			this.toolStripProgressBarDownloadSpeed.BaseColor = System.Drawing.Color.Green;
			this.toolStripProgressBarDownloadSpeed.ColorFillMode = Nexus.Client.UI.Controls.ProgressLabel.FillType.Fixed;
			this.toolStripProgressBarDownloadSpeed.Maximum = 100;
			this.toolStripProgressBarDownloadSpeed.Name = "toolStripProgressBarDownloadSpeed";
			this.toolStripProgressBarDownloadSpeed.OptionalValue = 0;
			this.toolStripProgressBarDownloadSpeed.ShowOptionalProgress = true;
			this.toolStripProgressBarDownloadSpeed.Size = new System.Drawing.Size(200, 34);
			this.toolStripProgressBarDownloadSpeed.Style = System.Windows.Forms.ProgressBarStyle.Continuous;
			this.toolStripProgressBarDownloadSpeed.Value = 0;
			// 
			// toolStripButtonGoPremium
			// 
			this.toolStripButtonGoPremium.Name = "toolStripButtonGoPremium";
			this.toolStripButtonGoPremium.Size = new System.Drawing.Size(23, 34);
			// 
			// toolStripLabelLoginMessage
			// 
			this.toolStripLabelLoginMessage.Name = "toolStripLabelLoginMessage";
			this.toolStripLabelLoginMessage.Size = new System.Drawing.Size(0, 34);
			// 
			// toolStripButtonRateLimit
			// 
			this.toolStripButtonRateLimit.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.toolStripButtonRateLimit.Image = global::Nexus.Client.Properties.Resources.info;
			this.toolStripButtonRateLimit.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.toolStripButtonRateLimit.Name = "toolStripButtonRateLimit";
			this.toolStripButtonRateLimit.Size = new System.Drawing.Size(23, 34);
			this.toolStripButtonRateLimit.Text = "Rate Limit";
			// 
			// tlbStatusFiller
			// 
			this.tlbStatusFiller.Name = "tlbStatusFiller";
			this.tlbStatusFiller.Size = new System.Drawing.Size(115, 31);
			this.tlbStatusFiller.Spring = true;
			// 
			// toolStripLabelBottomBarFeedback
			// 
			this.toolStripLabelBottomBarFeedback.Name = "toolStripLabelBottomBarFeedback";
			this.toolStripLabelBottomBarFeedback.Size = new System.Drawing.Size(0, 34);
			this.toolStripLabelBottomBarFeedback.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			// 
			// toolStripLabelBottomBarFeedbackCounter
			// 
			this.toolStripLabelBottomBarFeedbackCounter.Name = "toolStripLabelBottomBarFeedbackCounter";
			this.toolStripLabelBottomBarFeedbackCounter.Size = new System.Drawing.Size(0, 34);
			this.toolStripLabelBottomBarFeedbackCounter.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			// 
			// toolStripButtonLoader
			// 
			this.toolStripButtonLoader.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.toolStripButtonLoader.Image = global::Nexus.Client.Properties.Resources.round_loading;
			this.toolStripButtonLoader.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.toolStripButtonLoader.Name = "toolStripButtonLoader";
			this.toolStripButtonLoader.Size = new System.Drawing.Size(23, 34);
			this.toolStripButtonLoader.Text = "Settings";
			this.toolStripButtonLoader.Visible = false;
			// 
			// tlbModSeparator
			// 
			this.tlbModSeparator.Name = "tlbModSeparator";
			this.tlbModSeparator.Size = new System.Drawing.Size(6, 36);
			// 
			// toolStripLabelPluginsCounter
			// 
			this.toolStripLabelPluginsCounter.Name = "toolStripLabelPluginsCounter";
			this.toolStripLabelPluginsCounter.Size = new System.Drawing.Size(172, 34);
			this.toolStripLabelPluginsCounter.Text = " | Total plugins / Active plugins ";
			this.toolStripLabelPluginsCounter.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			// 
			// toolStripLabelActivePluginsCounter
			// 
			this.toolStripLabelActivePluginsCounter.Name = "toolStripLabelActivePluginsCounter";
			this.toolStripLabelActivePluginsCounter.Size = new System.Drawing.Size(0, 34);
			this.toolStripLabelActivePluginsCounter.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			// 
			// toolStripSeparatorPluginSeparator
			// 
			this.toolStripSeparatorPluginSeparator.Name = "toolStripSeparatorPluginSeparator";
			this.toolStripSeparatorPluginSeparator.Size = new System.Drawing.Size(6, 36);
			// 
			// tlbModsCounter
			// 
			this.tlbModsCounter.Name = "tlbModsCounter";
			this.tlbModsCounter.Size = new System.Drawing.Size(154, 34);
			this.tlbModsCounter.Text = " | Total mods / Active mods ";
			this.tlbModsCounter.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			// 
			// MainForm
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(804, 533);
			this.Controls.Add(this.dockPanel1);
			this.Controls.Add(this.toolStrip1);
			this.Controls.Add(this.tssDownload);
			this.m_fpdFontProvider.SetFontSet(this, "StandardText");
			this.Name = "MainForm";
			this.Text = "MainForm";
			this.toolStrip1.ResumeLayout(false);
			this.toolStrip1.PerformLayout();
			this.tssDownload.ResumeLayout(false);
			this.tssDownload.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.ToolStrip toolStrip1;
		private System.Windows.Forms.ToolStripSplitButton spbLaunch;
		private System.Windows.Forms.ToolStripSplitButton spbSupportedTools;
		private System.Windows.Forms.ToolStripSplitButton spbProfiles;
		private System.Windows.Forms.ToolStripButton tsbSettings;
		private System.Windows.Forms.ToolStripSplitButton spbChangeMode;
		private System.Windows.Forms.ToolStripSplitButton toolStripSplitButtonTools;
		private System.Windows.Forms.ToolStripSplitButton spbFolders;
		private System.Windows.Forms.ToolStripButton tsbUpdate;
		private WeifenLuo.WinFormsUI.Docking.DockPanel dockPanel1;
		private System.Windows.Forms.ToolStripSplitButton spbHelp;
		private System.Windows.Forms.StatusStrip tssDownload;
		private UI.Controls.CustomizableToolStripProgressBar toolStripProgressBarDownloadSpeed;
		private System.Windows.Forms.ToolStripLabel toolStripLabelDownloads;
		private System.Windows.Forms.ToolStripLabel toolStripLabelLoginMessage;
		private System.Windows.Forms.ToolStripButton toolStripButtonGoPremium;
		private System.Windows.Forms.ToolStripButton toolStripButtonOnlineStatus;
		private System.Windows.Forms.ToolStripLabel tlbModsCounter;
		private System.Windows.Forms.ToolStripLabel toolStripLabelPluginsCounter;
		private System.Windows.Forms.ToolStripLabel toolStripLabelActivePluginsCounter;
		private System.Windows.Forms.ToolStripSeparator toolStripSeparatorPluginSeparator;
		private System.Windows.Forms.ToolStripSeparator tlbModSeparator;
		private System.Windows.Forms.ToolStripLabel toolStripLabelBottomBarFeedback;
		private System.Windows.Forms.ToolStripLabel toolStripLabelBottomBarFeedbackCounter;
		private System.Windows.Forms.ToolStripButton toolStripButtonLoader;
		private System.Windows.Forms.ToolStripStatusLabel tlbStatusFiller;
		private System.Windows.Forms.ToolStripTextBox toolStripTextBoxFind;
        private System.Windows.Forms.ToolStripButton toolStripButtonRateLimit;
    }
}