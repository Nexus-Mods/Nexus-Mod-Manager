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
			WeifenLuo.WinFormsUI.Docking.DockPanelSkin dockPanelSkin1 = new WeifenLuo.WinFormsUI.Docking.DockPanelSkin();
			WeifenLuo.WinFormsUI.Docking.AutoHideStripSkin autoHideStripSkin1 = new WeifenLuo.WinFormsUI.Docking.AutoHideStripSkin();
			WeifenLuo.WinFormsUI.Docking.DockPanelGradient dockPanelGradient1 = new WeifenLuo.WinFormsUI.Docking.DockPanelGradient();
			WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient1 = new WeifenLuo.WinFormsUI.Docking.TabGradient();
			WeifenLuo.WinFormsUI.Docking.DockPaneStripSkin dockPaneStripSkin1 = new WeifenLuo.WinFormsUI.Docking.DockPaneStripSkin();
			WeifenLuo.WinFormsUI.Docking.DockPaneStripGradient dockPaneStripGradient1 = new WeifenLuo.WinFormsUI.Docking.DockPaneStripGradient();
			WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient2 = new WeifenLuo.WinFormsUI.Docking.TabGradient();
			WeifenLuo.WinFormsUI.Docking.DockPanelGradient dockPanelGradient2 = new WeifenLuo.WinFormsUI.Docking.DockPanelGradient();
			WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient3 = new WeifenLuo.WinFormsUI.Docking.TabGradient();
			WeifenLuo.WinFormsUI.Docking.DockPaneStripToolWindowGradient dockPaneStripToolWindowGradient1 = new WeifenLuo.WinFormsUI.Docking.DockPaneStripToolWindowGradient();
			WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient4 = new WeifenLuo.WinFormsUI.Docking.TabGradient();
			WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient5 = new WeifenLuo.WinFormsUI.Docking.TabGradient();
			WeifenLuo.WinFormsUI.Docking.DockPanelGradient dockPanelGradient3 = new WeifenLuo.WinFormsUI.Docking.DockPanelGradient();
			WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient6 = new WeifenLuo.WinFormsUI.Docking.TabGradient();
			WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient7 = new WeifenLuo.WinFormsUI.Docking.TabGradient();

			this.caption = new System.Windows.Forms.TextBox();
			this.content = new System.Windows.Forms.TextBox();
			this.anchor = new System.Windows.Forms.Label();
			this.showForm = new System.Windows.Forms.Button();

			this.toolStrip1 = new System.Windows.Forms.ToolStrip();
			this.spbLaunch = new System.Windows.Forms.ToolStripSplitButton();
			this.spbProfiles = new System.Windows.Forms.ToolStripSplitButton();
			this.spbHelp = new System.Windows.Forms.ToolStripSplitButton();
			this.spbChangeMode = new System.Windows.Forms.ToolStripSplitButton();
			this.spbTools = new System.Windows.Forms.ToolStripSplitButton();
			this.spbFolders = new System.Windows.Forms.ToolStripSplitButton();
			this.tsbSettings = new System.Windows.Forms.ToolStripButton();
			this.tsbTips = new System.Windows.Forms.ToolStripSplitButton();
			this.tsbUpdate = new System.Windows.Forms.ToolStripButton();
			this.dockPanel1 = new WeifenLuo.WinFormsUI.Docking.DockPanel();
			this.tssDownload = new System.Windows.Forms.StatusStrip();
			this.tlbDownloads = new System.Windows.Forms.ToolStripLabel();
			this.tpbDownloadSpeed = new Nexus.Client.UI.Controls.CustomizableToolStripProgressBar();
			this.tsbGoPremium = new System.Windows.Forms.ToolStripButton();
            this.tlbLoginMessage = new System.Windows.Forms.ToolStripLabel();
			this.tsbOnlineStatus = new System.Windows.Forms.ToolStripButton();
			this.tstFind = new System.Windows.Forms.ToolStripTextBox();
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
			this.spbTools,
			this.spbFolders,
			this.tsbSettings,
			this.tsbTips,
			this.tstFind,
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
			this.spbProfiles.Image = ((System.Drawing.Image)(resources.GetObject("endorsed")));
			this.spbProfiles.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.spbProfiles.Name = "spbProfiles";
			this.spbProfiles.Size = new System.Drawing.Size(166, 36);
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
			// spbTools
			// 
			this.spbTools.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.spbTools.Image = global::Nexus.Client.Properties.Resources.preferences_system_4;
			this.spbTools.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.spbTools.Name = "spbTools";
			this.spbTools.Size = new System.Drawing.Size(48, 36);
			this.spbTools.Text = "Tools";
			this.spbTools.ButtonClick += new System.EventHandler(this.spbTools_ButtonClick);
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
			// tsbTips
			// 
			this.tsbTips.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbTips.Image = global::Nexus.Client.Properties.Resources.tipsIcon;
			this.tsbTips.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbTips.Name = "tsbTips";
			this.tsbTips.Size = new System.Drawing.Size(36, 36);
			this.tsbTips.Text = "Tips";
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
			this.dockPanel1.ActiveAutoHideContent = null;
			this.dockPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.dockPanel1.DockBackColor = System.Drawing.SystemColors.Control;
			this.dockPanel1.DocumentStyle = WeifenLuo.WinFormsUI.Docking.DocumentStyle.DockingWindow;
			this.dockPanel1.Location = new System.Drawing.Point(0, 39);
			this.dockPanel1.Name = "dockPanel1";
			this.dockPanel1.Size = new System.Drawing.Size(804, 458);
			dockPanelGradient1.EndColor = System.Drawing.SystemColors.ControlLight;
			dockPanelGradient1.StartColor = System.Drawing.SystemColors.ControlLight;
			autoHideStripSkin1.DockStripGradient = dockPanelGradient1;
			tabGradient1.EndColor = System.Drawing.SystemColors.Control;
			tabGradient1.StartColor = System.Drawing.SystemColors.Control;
			tabGradient1.TextColor = System.Drawing.SystemColors.ControlDarkDark;
			autoHideStripSkin1.TabGradient = tabGradient1;
			autoHideStripSkin1.TextFont = new System.Drawing.Font("Segoe UI", 9F);
			dockPanelSkin1.AutoHideStripSkin = autoHideStripSkin1;
			tabGradient2.EndColor = System.Drawing.SystemColors.ControlLightLight;
			tabGradient2.StartColor = System.Drawing.SystemColors.ControlLightLight;
			tabGradient2.TextColor = System.Drawing.SystemColors.ControlText;
			dockPaneStripGradient1.ActiveTabGradient = tabGradient2;
			dockPanelGradient2.EndColor = System.Drawing.SystemColors.Control;
			dockPanelGradient2.StartColor = System.Drawing.SystemColors.Control;
			dockPaneStripGradient1.DockStripGradient = dockPanelGradient2;
			tabGradient3.EndColor = System.Drawing.SystemColors.ControlLight;
			tabGradient3.StartColor = System.Drawing.SystemColors.ControlLight;
			tabGradient3.TextColor = System.Drawing.SystemColors.ControlText;
			dockPaneStripGradient1.InactiveTabGradient = tabGradient3;
			dockPaneStripSkin1.DocumentGradient = dockPaneStripGradient1;
			dockPaneStripSkin1.TextFont = new System.Drawing.Font("Segoe UI", 9F);
			tabGradient4.EndColor = System.Drawing.SystemColors.ActiveCaption;
			tabGradient4.LinearGradientMode = System.Drawing.Drawing2D.LinearGradientMode.Vertical;
			tabGradient4.StartColor = System.Drawing.SystemColors.GradientActiveCaption;
			tabGradient4.TextColor = System.Drawing.SystemColors.ActiveCaptionText;
			dockPaneStripToolWindowGradient1.ActiveCaptionGradient = tabGradient4;
			tabGradient5.EndColor = System.Drawing.SystemColors.Control;
			tabGradient5.StartColor = System.Drawing.SystemColors.Control;
			tabGradient5.TextColor = System.Drawing.SystemColors.ControlText;
			dockPaneStripToolWindowGradient1.ActiveTabGradient = tabGradient5;
			dockPanelGradient3.EndColor = System.Drawing.SystemColors.ControlLight;
			dockPanelGradient3.StartColor = System.Drawing.SystemColors.ControlLight;
			dockPaneStripToolWindowGradient1.DockStripGradient = dockPanelGradient3;
			tabGradient6.EndColor = System.Drawing.SystemColors.InactiveCaption;
			tabGradient6.LinearGradientMode = System.Drawing.Drawing2D.LinearGradientMode.Vertical;
			tabGradient6.StartColor = System.Drawing.SystemColors.GradientInactiveCaption;
			tabGradient6.TextColor = System.Drawing.SystemColors.InactiveCaptionText;
			dockPaneStripToolWindowGradient1.InactiveCaptionGradient = tabGradient6;
			tabGradient7.EndColor = System.Drawing.Color.Transparent;
			tabGradient7.StartColor = System.Drawing.Color.Transparent;
			tabGradient7.TextColor = System.Drawing.SystemColors.ControlDarkDark;
			dockPaneStripToolWindowGradient1.InactiveTabGradient = tabGradient7;
			dockPaneStripSkin1.ToolWindowGradient = dockPaneStripToolWindowGradient1;
			dockPanelSkin1.DockPaneStripSkin = dockPaneStripSkin1;
			this.dockPanel1.Skin = dockPanelSkin1;
			this.dockPanel1.TabIndex = 3;
			// 
			// tssDownload
			// 
			this.tssDownload.AutoSize = false;
			this.tssDownload.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
			this.tsbOnlineStatus,
			this.tlbDownloads,
			this.tpbDownloadSpeed,
			this.tsbGoPremium,
			this.tlbLoginMessage});
			this.tssDownload.Location = new System.Drawing.Point(0, 497);
			this.tssDownload.Name = "tssDownload";
			this.tssDownload.Size = new System.Drawing.Size(804, 36);
			this.tssDownload.TabIndex = 4;
			// 
			// tlbDownloads
			// 

			this.tlbDownloads.Name = "tlbDownloads";
			this.tlbDownloads.Size = new System.Drawing.Size(0, 34);
			// 
			// tstFind
			// 
			this.tstFind.Visible = false;
			this.tstFind.Name = "tstFind";
			this.tstFind.Size = new System.Drawing.Size(100, 20);
			this.tstFind.KeyUp += new System.Windows.Forms.KeyEventHandler(this.tstFind_KeyUp);
			this.tstFind.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
			// 
			// tpbDownloadSpeed
			// 
			this.tpbDownloadSpeed.AutoSize = false;
			this.tpbDownloadSpeed.Maximum = 100;
			this.tpbDownloadSpeed.Name = "tpbDownloadSpeed";
			this.tpbDownloadSpeed.Size = new System.Drawing.Size(200, 34);
			this.tpbDownloadSpeed.Style = System.Windows.Forms.ProgressBarStyle.Continuous;
			this.tpbDownloadSpeed.Value = 0;
			// 
			// tsbGoPremium
			// 
			this.tsbGoPremium.AutoSize = false;
			this.tsbGoPremium.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbGoPremium.Image = global::Nexus.Client.Properties.Resources.go_premium;
			this.tsbGoPremium.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
			this.tsbGoPremium.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbGoPremium.Name = "tsbGoPremium";
			this.tsbGoPremium.Size = new System.Drawing.Size(36, 34);
			this.tsbGoPremium.Click += new System.EventHandler(this.tsbGoPremium_Click);
			// 
			// tsbOnlineStatus
			// 
			this.tsbOnlineStatus.AutoSize = false;
			this.tsbOnlineStatus.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbOnlineStatus.Image = global::Nexus.Client.Properties.Resources.offline_icon;
			this.tsbOnlineStatus.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
			this.tsbOnlineStatus.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbOnlineStatus.Name = "tsbOnlineStatus";
			this.tsbOnlineStatus.Size = new System.Drawing.Size(36, 34);
			// 
            // tlbLoginMessage
			// 
            this.tlbLoginMessage.Name = "tlbLoginMessage";
            this.tlbLoginMessage.Size = new System.Drawing.Size(0, 34);
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
		private System.Windows.Forms.ToolStripSplitButton spbProfiles;
		private System.Windows.Forms.ToolStripButton tsbSettings;
		public System.Windows.Forms.ToolStripSplitButton tsbTips;
		private System.Windows.Forms.ToolStripSplitButton spbChangeMode;
		private System.Windows.Forms.ToolStripSplitButton spbTools;
		private System.Windows.Forms.ToolStripSplitButton spbFolders;
		private System.Windows.Forms.ToolStripButton tsbUpdate;
		private WeifenLuo.WinFormsUI.Docking.DockPanel dockPanel1;
		private System.Windows.Forms.ToolStripSplitButton spbHelp;
		private System.Windows.Forms.StatusStrip tssDownload;
		private Nexus.Client.UI.Controls.CustomizableToolStripProgressBar tpbDownloadSpeed;
		private System.Windows.Forms.ToolStripLabel tlbDownloads;
        private System.Windows.Forms.ToolStripLabel tlbLoginMessage;
		private System.Windows.Forms.ToolStripButton tsbGoPremium;
		private System.Windows.Forms.ToolStripButton tsbOnlineStatus;
		public System.Windows.Forms.ToolStripTextBox tstFind;
	}
}