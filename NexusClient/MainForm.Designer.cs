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
			this.toolStrip1 = new System.Windows.Forms.ToolStrip();
			this.spbLaunch = new System.Windows.Forms.ToolStripSplitButton();
			this.tsbChangeMode = new System.Windows.Forms.ToolStripButton();
			this.spbTools = new System.Windows.Forms.ToolStripSplitButton();
			this.tsbSettings = new System.Windows.Forms.ToolStripButton();
			this.tsbUpdate = new System.Windows.Forms.ToolStripButton();
			this.tabControl1 = new System.Windows.Forms.TabControl();
			this.tpgPlugins = new System.Windows.Forms.TabPage();
			this.pmcPluginManager = new Nexus.Client.PluginManagement.UI.PluginManagerControl();
			this.tpgMods = new System.Windows.Forms.TabPage();
			this.mmgModManager = new Nexus.Client.ModManagement.UI.ModManagerControl();
			this.tpgActivityMonitor = new System.Windows.Forms.TabPage();
			this.amcActivityMonitor = new Nexus.Client.ActivityMonitoring.UI.ActivityMonitorControl();
			this.toolStrip1.SuspendLayout();
			this.tabControl1.SuspendLayout();
			this.tpgPlugins.SuspendLayout();
			this.tpgMods.SuspendLayout();
			this.tpgActivityMonitor.SuspendLayout();
			this.SuspendLayout();
			// 
			// toolStrip1
			// 
			this.toolStrip1.ImageScalingSize = new System.Drawing.Size(32, 32);
			this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.spbLaunch,
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
			this.tsbChangeMode.Click += new System.EventHandler(this.tsbChangeMode_Click);
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
			// tabControl1
			// 
			this.tabControl1.Controls.Add(this.tpgPlugins);
			this.tabControl1.Controls.Add(this.tpgMods);
			this.tabControl1.Controls.Add(this.tpgActivityMonitor);
			this.tabControl1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.tabControl1.Location = new System.Drawing.Point(0, 39);
			this.tabControl1.Name = "tabControl1";
			this.tabControl1.SelectedIndex = 0;
			this.tabControl1.Size = new System.Drawing.Size(804, 494);
			this.tabControl1.TabIndex = 2;
			// 
			// tpgPlugins
			// 
			this.tpgPlugins.Controls.Add(this.pmcPluginManager);
			this.tpgPlugins.Location = new System.Drawing.Point(4, 22);
			this.tpgPlugins.Name = "tpgPlugins";
			this.tpgPlugins.Padding = new System.Windows.Forms.Padding(3);
			this.tpgPlugins.Size = new System.Drawing.Size(796, 468);
			this.tpgPlugins.TabIndex = 0;
			this.tpgPlugins.Text = "Plugins";
			this.tpgPlugins.UseVisualStyleBackColor = true;
			// 
			// pmcPluginManager
			// 
			this.pmcPluginManager.Dock = System.Windows.Forms.DockStyle.Fill;
			this.pmcPluginManager.Location = new System.Drawing.Point(3, 3);
			this.pmcPluginManager.Name = "pmcPluginManager";
			this.pmcPluginManager.Size = new System.Drawing.Size(790, 462);
			this.pmcPluginManager.TabIndex = 0;
			// 
			// tpgMods
			// 
			this.tpgMods.Controls.Add(this.mmgModManager);
			this.tpgMods.Location = new System.Drawing.Point(4, 22);
			this.tpgMods.Name = "tpgMods";
			this.tpgMods.Padding = new System.Windows.Forms.Padding(3);
			this.tpgMods.Size = new System.Drawing.Size(796, 468);
			this.tpgMods.TabIndex = 1;
			this.tpgMods.Text = "Mods";
			this.tpgMods.UseVisualStyleBackColor = true;
			// 
			// mmgModManager
			// 
			this.mmgModManager.Dock = System.Windows.Forms.DockStyle.Fill;
			this.mmgModManager.Location = new System.Drawing.Point(3, 3);
			this.mmgModManager.Name = "mmgModManager";
			this.mmgModManager.Size = new System.Drawing.Size(790, 462);
			this.mmgModManager.TabIndex = 0;
			// 
			// tpgActivityMonitor
			// 
			this.tpgActivityMonitor.Controls.Add(this.amcActivityMonitor);
			this.tpgActivityMonitor.Location = new System.Drawing.Point(4, 22);
			this.tpgActivityMonitor.Name = "tpgActivityMonitor";
			this.tpgActivityMonitor.Padding = new System.Windows.Forms.Padding(3);
			this.tpgActivityMonitor.Size = new System.Drawing.Size(796, 468);
			this.tpgActivityMonitor.TabIndex = 2;
			this.tpgActivityMonitor.Text = "Download Manager";
			this.tpgActivityMonitor.UseVisualStyleBackColor = true;
			// 
			// amcActivityMonitor
			// 
			this.amcActivityMonitor.Dock = System.Windows.Forms.DockStyle.Fill;
			this.amcActivityMonitor.Location = new System.Drawing.Point(3, 3);
			this.amcActivityMonitor.Name = "amcActivityMonitor";
			this.amcActivityMonitor.Size = new System.Drawing.Size(790, 462);
			this.amcActivityMonitor.TabIndex = 0;
			// 
			// MainForm
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(804, 533);
			this.Controls.Add(this.tabControl1);
			this.Controls.Add(this.toolStrip1);
			this.Name = "MainForm";
			this.Text = "MainForm";
			this.toolStrip1.ResumeLayout(false);
			this.toolStrip1.PerformLayout();
			this.tabControl1.ResumeLayout(false);
			this.tpgPlugins.ResumeLayout(false);
			this.tpgMods.ResumeLayout(false);
			this.tpgActivityMonitor.ResumeLayout(false);
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.ToolStrip toolStrip1;
		private System.Windows.Forms.TabControl tabControl1;
		private System.Windows.Forms.TabPage tpgPlugins;
		private System.Windows.Forms.TabPage tpgMods;
		private System.Windows.Forms.ToolStripSplitButton spbLaunch;
		private Nexus.Client.ModManagement.UI.ModManagerControl mmgModManager;
		private Nexus.Client.PluginManagement.UI.PluginManagerControl pmcPluginManager;
		private System.Windows.Forms.ToolStripButton tsbSettings;
		private System.Windows.Forms.TabPage tpgActivityMonitor;
		private Nexus.Client.ActivityMonitoring.UI.ActivityMonitorControl amcActivityMonitor;
		private System.Windows.Forms.ToolStripButton tsbChangeMode;
		private System.Windows.Forms.ToolStripSplitButton spbTools;
		private System.Windows.Forms.ToolStripButton tsbUpdate;
	}
}