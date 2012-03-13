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
			WeifenLuo.WinFormsUI.Docking.DockPanelSkin dockPanelSkin5 = new WeifenLuo.WinFormsUI.Docking.DockPanelSkin();
			WeifenLuo.WinFormsUI.Docking.AutoHideStripSkin autoHideStripSkin5 = new WeifenLuo.WinFormsUI.Docking.AutoHideStripSkin();
			WeifenLuo.WinFormsUI.Docking.DockPanelGradient dockPanelGradient13 = new WeifenLuo.WinFormsUI.Docking.DockPanelGradient();
			WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient29 = new WeifenLuo.WinFormsUI.Docking.TabGradient();
			WeifenLuo.WinFormsUI.Docking.DockPaneStripSkin dockPaneStripSkin5 = new WeifenLuo.WinFormsUI.Docking.DockPaneStripSkin();
			WeifenLuo.WinFormsUI.Docking.DockPaneStripGradient dockPaneStripGradient5 = new WeifenLuo.WinFormsUI.Docking.DockPaneStripGradient();
			WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient30 = new WeifenLuo.WinFormsUI.Docking.TabGradient();
			WeifenLuo.WinFormsUI.Docking.DockPanelGradient dockPanelGradient14 = new WeifenLuo.WinFormsUI.Docking.DockPanelGradient();
			WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient31 = new WeifenLuo.WinFormsUI.Docking.TabGradient();
			WeifenLuo.WinFormsUI.Docking.DockPaneStripToolWindowGradient dockPaneStripToolWindowGradient5 = new WeifenLuo.WinFormsUI.Docking.DockPaneStripToolWindowGradient();
			WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient32 = new WeifenLuo.WinFormsUI.Docking.TabGradient();
			WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient33 = new WeifenLuo.WinFormsUI.Docking.TabGradient();
			WeifenLuo.WinFormsUI.Docking.DockPanelGradient dockPanelGradient15 = new WeifenLuo.WinFormsUI.Docking.DockPanelGradient();
			WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient34 = new WeifenLuo.WinFormsUI.Docking.TabGradient();
			WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient35 = new WeifenLuo.WinFormsUI.Docking.TabGradient();
			this.toolStrip1 = new System.Windows.Forms.ToolStrip();
			this.spbLaunch = new System.Windows.Forms.ToolStripSplitButton();
			this.tsbLogout = new System.Windows.Forms.ToolStripButton();
			this.tsbChangeMode = new System.Windows.Forms.ToolStripButton();
			this.spbTools = new System.Windows.Forms.ToolStripSplitButton();
			this.tsbSettings = new System.Windows.Forms.ToolStripButton();
			this.tsbUpdate = new System.Windows.Forms.ToolStripButton();
			this.dockPanel1 = new WeifenLuo.WinFormsUI.Docking.DockPanel();
			this.spbHelp = new System.Windows.Forms.ToolStripSplitButton();
			this.toolStrip1.SuspendLayout();
			this.SuspendLayout();
			// 
			// toolStrip1
			// 
			this.toolStrip1.ImageScalingSize = new System.Drawing.Size(32, 32);
			this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.spbLaunch,
            this.spbHelp,
            this.tsbLogout,
            this.tsbChangeMode,
            this.spbTools,
            this.tsbSettings,
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
			this.spbLaunch.Size = new System.Drawing.Size(165, 36);
			this.spbLaunch.Text = "toolStripSplitButton1";
			this.spbLaunch.DropDownItemClicked += new System.Windows.Forms.ToolStripItemClickedEventHandler(this.spbLaunch_DropDownItemClicked);
			// 
			// tsbLogout
			// 
			this.tsbLogout.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
			this.tsbLogout.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbLogout.Image = global::Nexus.Client.Properties.Resources.application_exit_2;
			this.tsbLogout.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbLogout.Name = "tsbLogout";
			this.tsbLogout.Size = new System.Drawing.Size(36, 36);
			this.tsbLogout.Text = "toolStripButton1";
			// 
			// tsbChangeMode
			// 
			this.tsbChangeMode.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
			this.tsbChangeMode.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbChangeMode.Image = global::Nexus.Client.Properties.Resources.change_game_mode;
			this.tsbChangeMode.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbChangeMode.Name = "tsbChangeMode";
			this.tsbChangeMode.Size = new System.Drawing.Size(36, 36);
			this.tsbChangeMode.Text = "Change Game Mode";
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
			this.dockPanel1.Size = new System.Drawing.Size(804, 494);
			dockPanelGradient13.EndColor = System.Drawing.SystemColors.ControlLight;
			dockPanelGradient13.StartColor = System.Drawing.SystemColors.ControlLight;
			autoHideStripSkin5.DockStripGradient = dockPanelGradient13;
			tabGradient29.EndColor = System.Drawing.SystemColors.Control;
			tabGradient29.StartColor = System.Drawing.SystemColors.Control;
			tabGradient29.TextColor = System.Drawing.SystemColors.ControlDarkDark;
			autoHideStripSkin5.TabGradient = tabGradient29;
			autoHideStripSkin5.TextFont = new System.Drawing.Font("Segoe UI", 9F);
			dockPanelSkin5.AutoHideStripSkin = autoHideStripSkin5;
			tabGradient30.EndColor = System.Drawing.SystemColors.ControlLightLight;
			tabGradient30.StartColor = System.Drawing.SystemColors.ControlLightLight;
			tabGradient30.TextColor = System.Drawing.SystemColors.ControlText;
			dockPaneStripGradient5.ActiveTabGradient = tabGradient30;
			dockPanelGradient14.EndColor = System.Drawing.SystemColors.Control;
			dockPanelGradient14.StartColor = System.Drawing.SystemColors.Control;
			dockPaneStripGradient5.DockStripGradient = dockPanelGradient14;
			tabGradient31.EndColor = System.Drawing.SystemColors.ControlLight;
			tabGradient31.StartColor = System.Drawing.SystemColors.ControlLight;
			tabGradient31.TextColor = System.Drawing.SystemColors.ControlText;
			dockPaneStripGradient5.InactiveTabGradient = tabGradient31;
			dockPaneStripSkin5.DocumentGradient = dockPaneStripGradient5;
			dockPaneStripSkin5.TextFont = new System.Drawing.Font("Segoe UI", 9F);
			tabGradient32.EndColor = System.Drawing.SystemColors.ActiveCaption;
			tabGradient32.LinearGradientMode = System.Drawing.Drawing2D.LinearGradientMode.Vertical;
			tabGradient32.StartColor = System.Drawing.SystemColors.GradientActiveCaption;
			tabGradient32.TextColor = System.Drawing.SystemColors.ActiveCaptionText;
			dockPaneStripToolWindowGradient5.ActiveCaptionGradient = tabGradient32;
			tabGradient33.EndColor = System.Drawing.SystemColors.Control;
			tabGradient33.StartColor = System.Drawing.SystemColors.Control;
			tabGradient33.TextColor = System.Drawing.SystemColors.ControlText;
			dockPaneStripToolWindowGradient5.ActiveTabGradient = tabGradient33;
			dockPanelGradient15.EndColor = System.Drawing.SystemColors.ControlLight;
			dockPanelGradient15.StartColor = System.Drawing.SystemColors.ControlLight;
			dockPaneStripToolWindowGradient5.DockStripGradient = dockPanelGradient15;
			tabGradient34.EndColor = System.Drawing.SystemColors.InactiveCaption;
			tabGradient34.LinearGradientMode = System.Drawing.Drawing2D.LinearGradientMode.Vertical;
			tabGradient34.StartColor = System.Drawing.SystemColors.GradientInactiveCaption;
			tabGradient34.TextColor = System.Drawing.SystemColors.InactiveCaptionText;
			dockPaneStripToolWindowGradient5.InactiveCaptionGradient = tabGradient34;
			tabGradient35.EndColor = System.Drawing.Color.Transparent;
			tabGradient35.StartColor = System.Drawing.Color.Transparent;
			tabGradient35.TextColor = System.Drawing.SystemColors.ControlDarkDark;
			dockPaneStripToolWindowGradient5.InactiveTabGradient = tabGradient35;
			dockPaneStripSkin5.ToolWindowGradient = dockPaneStripToolWindowGradient5;
			dockPanelSkin5.DockPaneStripSkin = dockPaneStripSkin5;
			this.dockPanel1.Skin = dockPanelSkin5;
			this.dockPanel1.TabIndex = 3;
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
			// MainForm
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(804, 533);
			this.Controls.Add(this.dockPanel1);
			this.Controls.Add(this.toolStrip1);
			this.Name = "MainForm";
			this.Text = "MainForm";
			this.toolStrip1.ResumeLayout(false);
			this.toolStrip1.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.ToolStrip toolStrip1;
		private System.Windows.Forms.ToolStripSplitButton spbLaunch;
		private System.Windows.Forms.ToolStripButton tsbSettings;
		private System.Windows.Forms.ToolStripButton tsbChangeMode;
		private System.Windows.Forms.ToolStripSplitButton spbTools;
		private System.Windows.Forms.ToolStripButton tsbUpdate;
		private WeifenLuo.WinFormsUI.Docking.DockPanel dockPanel1;
		private System.Windows.Forms.ToolStripButton tsbLogout;
		private System.Windows.Forms.ToolStripSplitButton spbHelp;
	}
}