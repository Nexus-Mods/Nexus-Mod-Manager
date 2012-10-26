﻿namespace Nexus.Client.PluginManagement.UI
{
	partial class PluginManagerControl
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

		#region Component Designer generated code

		/// <summary> 
		/// Required method for Designer support - do not modify 
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.toolStrip2 = new System.Windows.Forms.ToolStrip();
			this.tsbMoveUp = new System.Windows.Forms.ToolStripButton();
			this.tsbMoveDown = new System.Windows.Forms.ToolStripButton();
			this.tsbDisableAll = new System.Windows.Forms.ToolStripButton();
			this.tsbEnableAll = new System.Windows.Forms.ToolStripButton();
			this.splitContainer1 = new System.Windows.Forms.SplitContainer();
			this.rlvPlugins = new Nexus.UI.Controls.ReorderableListView();
			this.clmName = new System.Windows.Forms.ColumnHeader();
			this.clmIndexHex = new System.Windows.Forms.ColumnHeader();
			this.clmIndex = new System.Windows.Forms.ColumnHeader();
			this.splitContainer2 = new System.Windows.Forms.SplitContainer();
			this.ipbImage = new Nexus.UI.Controls.ImagePreviewBox();
			this.hlbPluginInfo = new Nexus.UI.Controls.HtmlLabel();
			this.toolStrip2.SuspendLayout();
			this.splitContainer1.Panel1.SuspendLayout();
			this.splitContainer1.Panel2.SuspendLayout();
			this.splitContainer1.SuspendLayout();
			this.splitContainer2.Panel1.SuspendLayout();
			this.splitContainer2.Panel2.SuspendLayout();
			this.splitContainer2.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.ipbImage)).BeginInit();
			this.SuspendLayout();
			// 
			// toolStrip2
			// 
			this.toolStrip2.Dock = System.Windows.Forms.DockStyle.Left;
			this.m_fpdFontProvider.SetFontSet(this.toolStrip2, "MenuText");
			this.m_fpdFontProvider.SetFontSize(this.toolStrip2, 9F);
			this.toolStrip2.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
			this.toolStrip2.ImageScalingSize = new System.Drawing.Size(32, 32);
			this.toolStrip2.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsbMoveUp,
            this.tsbMoveDown,
			this.tsbDisableAll,
			this.tsbEnableAll});
			this.toolStrip2.Location = new System.Drawing.Point(0, 0);
			this.toolStrip2.Name = "toolStrip2";
			this.toolStrip2.Size = new System.Drawing.Size(37, 453);
			this.toolStrip2.TabIndex = 2;
			this.toolStrip2.Text = "toolStrip2";
			// 
			// tsbMoveUp
			// 
			this.tsbMoveUp.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbMoveUp.Image = global::Nexus.Client.Properties.Resources.up;
			this.tsbMoveUp.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbMoveUp.Name = "tsbMoveUp";
			this.tsbMoveUp.Size = new System.Drawing.Size(34, 36);
			this.tsbMoveUp.Text = "Move Up";
			// 
			// tsbMoveDown
			// 
			this.tsbMoveDown.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbMoveDown.Image = global::Nexus.Client.Properties.Resources.down;
			this.tsbMoveDown.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbMoveDown.Name = "tsbMoveDown";
			this.tsbMoveDown.Size = new System.Drawing.Size(34, 36);
			this.tsbMoveDown.Text = "Move Down";
			// 
			// tsbDisableAll
			// 
			this.tsbDisableAll.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbDisableAll.Image = global::Nexus.Client.Properties.Resources.edit_delete;
			this.tsbDisableAll.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbDisableAll.Name = "tsbDisableAll";
			this.tsbDisableAll.Size = new System.Drawing.Size(34, 36);
			this.tsbDisableAll.Text = "Disable All Plugins";
			// 
			// tsbEnableAll
			// 
			this.tsbEnableAll.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.tsbEnableAll.Image = global::Nexus.Client.Properties.Resources.dialog_ok_4;
			this.tsbEnableAll.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.tsbEnableAll.Name = "tsbEnableAll";
			this.tsbEnableAll.Size = new System.Drawing.Size(34, 36);
			this.tsbEnableAll.Text = "Enable All Plugins";
			// 
			// splitContainer1
			// 
			this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.splitContainer1.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
			this.splitContainer1.Location = new System.Drawing.Point(37, 0);
			this.splitContainer1.Name = "splitContainer1";
			// 
			// splitContainer1.Panel1
			// 
			this.splitContainer1.Panel1.Controls.Add(this.rlvPlugins);
			// 
			// splitContainer1.Panel2
			// 
			this.splitContainer1.Panel2.Controls.Add(this.splitContainer2);
			this.splitContainer1.Size = new System.Drawing.Size(620, 453);
			this.splitContainer1.SplitterDistance = 263;
			this.splitContainer1.TabIndex = 3;
			// 
			// rlvPlugins
			// 
			this.rlvPlugins.CheckBoxes = true;
			this.rlvPlugins.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.clmName,
            this.clmIndexHex,
            this.clmIndex});
			this.rlvPlugins.Dock = System.Windows.Forms.DockStyle.Fill;
			this.rlvPlugins.Location = new System.Drawing.Point(0, 0);
			this.rlvPlugins.Name = "rlvPlugins";
			this.rlvPlugins.OwnerDraw = true;
			this.rlvPlugins.ShowItemToolTips = true;
			this.rlvPlugins.Size = new System.Drawing.Size(263, 453);
			this.rlvPlugins.TabIndex = 0;
			this.rlvPlugins.UseCompatibleStateImageBehavior = false;
			this.rlvPlugins.Resize += new System.EventHandler(this.rlvPlugins_Resize);
			this.rlvPlugins.SelectedIndexChanged += new System.EventHandler(this.rlvPlugins_SelectedIndexChanged);
			this.rlvPlugins.ItemsReordering += new System.EventHandler<Nexus.UI.Controls.ReorderingItemsEventArgs>(this.rlvPlugins_ItemsReordering);
			this.rlvPlugins.ColumnWidthChanging += new System.Windows.Forms.ColumnWidthChangingEventHandler(this.rlvPlugins_ColumnWidthChanging);
			this.rlvPlugins.ItemsReordered += new System.EventHandler<Nexus.UI.Controls.ReorderedItemsEventArgs>(this.rlvPlugins_ItemsReordered);
			// 
			// clmName
			// 
			this.clmName.Text = "Plugin";
			// 
			// clmIndexHex
			// 
			this.clmIndexHex.Text = "Load Order";
			this.clmIndexHex.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
			this.clmIndexHex.Width = 65;
			// 
			// clmIndex
			// 
			this.clmIndex.Text = "Index";
			this.clmIndex.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
			this.clmIndex.Width = 38;
			// 
			// splitContainer2
			// 
			this.splitContainer2.Dock = System.Windows.Forms.DockStyle.Fill;
			this.splitContainer2.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
			this.splitContainer2.Location = new System.Drawing.Point(0, 0);
			this.splitContainer2.Name = "splitContainer2";
			this.splitContainer2.Orientation = System.Windows.Forms.Orientation.Horizontal;
			// 
			// splitContainer2.Panel1
			// 
			this.splitContainer2.Panel1.Controls.Add(this.ipbImage);
			// 
			// splitContainer2.Panel2
			// 
			this.splitContainer2.Panel2.Controls.Add(this.hlbPluginInfo);
			this.splitContainer2.Size = new System.Drawing.Size(353, 453);
			this.splitContainer2.SplitterDistance = 174;
			this.splitContainer2.TabIndex = 0;
			// 
			// ipbImage
			// 
			this.ipbImage.Dock = System.Windows.Forms.DockStyle.Fill;
			this.ipbImage.Location = new System.Drawing.Point(0, 0);
			this.ipbImage.Name = "ipbImage";
			this.ipbImage.Size = new System.Drawing.Size(353, 174);
			this.ipbImage.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
			this.ipbImage.TabIndex = 0;
			this.ipbImage.TabStop = false;
			// 
			// hlbPluginInfo
			// 
			this.hlbPluginInfo.BackColor = System.Drawing.SystemColors.Control;
			this.hlbPluginInfo.Dock = System.Windows.Forms.DockStyle.Fill;
			this.hlbPluginInfo.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F);
			this.hlbPluginInfo.ForeColor = System.Drawing.SystemColors.ControlText;
			this.hlbPluginInfo.Location = new System.Drawing.Point(0, 0);
			this.hlbPluginInfo.MinimumSize = new System.Drawing.Size(20, 20);
			this.hlbPluginInfo.Name = "hlbPluginInfo";
			this.hlbPluginInfo.ScrollBarsEnabled = false;
			this.hlbPluginInfo.Size = new System.Drawing.Size(353, 275);
			this.hlbPluginInfo.TabIndex = 0;
			this.hlbPluginInfo.Text = null;
			// 
			// PluginManagerControl
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(657, 453);
			this.CloseButton = false;
			this.CloseButtonVisible = false;
			this.Controls.Add(this.splitContainer1);
			this.Controls.Add(this.toolStrip2);
			this.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.Name = "PluginManagerControl";
			this.Text = "Plugins";
			this.toolStrip2.ResumeLayout(false);
			this.toolStrip2.PerformLayout();
			this.splitContainer1.Panel1.ResumeLayout(false);
			this.splitContainer1.Panel2.ResumeLayout(false);
			this.splitContainer1.ResumeLayout(false);
			this.splitContainer2.Panel1.ResumeLayout(false);
			this.splitContainer2.Panel2.ResumeLayout(false);
			this.splitContainer2.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.ipbImage)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.ToolStrip toolStrip2;
		private System.Windows.Forms.SplitContainer splitContainer1;
		private Nexus.UI.Controls.ReorderableListView rlvPlugins;
		private System.Windows.Forms.ColumnHeader clmName;
		private System.Windows.Forms.ColumnHeader clmIndexHex;
		private System.Windows.Forms.SplitContainer splitContainer2;
		private Nexus.UI.Controls.ImagePreviewBox ipbImage;
		private Nexus.UI.Controls.HtmlLabel hlbPluginInfo;
		private System.Windows.Forms.ColumnHeader clmIndex;
		private System.Windows.Forms.ToolStripButton tsbMoveUp;
		private System.Windows.Forms.ToolStripButton tsbMoveDown;
		private System.Windows.Forms.ToolStripButton tsbDisableAll;
		private System.Windows.Forms.ToolStripButton tsbEnableAll;
	}
}
