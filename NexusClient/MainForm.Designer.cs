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
			this.tsbChangeMode = new System.Windows.Forms.ToolStripButton();
			this.spbTools = new System.Windows.Forms.ToolStripSplitButton();
			this.tsbSettings = new System.Windows.Forms.ToolStripButton();
			this.tsbUpdate = new System.Windows.Forms.ToolStripButton();
			this.dockPanel1 = new WeifenLuo.WinFormsUI.Docking.DockPanel();
			this.tsbLogout = new System.Windows.Forms.ToolStripButton();
			this.toolStrip1.SuspendLayout();
			this.SuspendLayout();
			// 
			// toolStrip1
			// 
			this.toolStrip1.ImageScalingSize = new System.Drawing.Size(32, 32);
			this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.spbLaunch,
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
	}
}