using System;
using System.Windows.Forms;
using System.Drawing;
using BrightIdeasSoftware.Design;

namespace Nexus.Client.UI.Controls
{
	/// <summary>
	/// A customizable progress bar.
	/// </summary>
	partial class BackedProfilesListView
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
			components = new System.ComponentModel.Container();
			this.cmsContextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
			this.tlcProfileName = new BrightIdeasSoftware.OLVColumn();
			this.tlcActiveMods = new BrightIdeasSoftware.OLVColumn();
			this.tlcLastBackedUp = new BrightIdeasSoftware.OLVColumn();
			this.tlcShared = new BrightIdeasSoftware.OLVColumn();
			this.tlcVersion = new BrightIdeasSoftware.OLVColumn();
			this.tlcWorksWithSaves = new BrightIdeasSoftware.OLVColumn();
						

			this.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
			this.tlcProfileName,
			this.tlcActiveMods,
			this.tlcLastBackedUp,
			this.tlcShared,
			this.tlcWorksWithSaves,
			this.tlcVersion});
			// 
			// cmsContextMenu
			// 
			this.cmsContextMenu.Name = "cmsContextMenu";
			this.cmsContextMenu.Size = new System.Drawing.Size(61, 4);
			// 
			// tlcProfileName
			// 
			this.tlcProfileName.Text = "Profile Name";
			this.tlcProfileName.IsEditable = false;
			// 
			// tlcActiveMods
			// 
			this.tlcActiveMods.Text = "Active Mods";
			this.tlcActiveMods.IsEditable = false;
			this.tlcActiveMods.TextAlign = HorizontalAlignment.Center;
			// 
			// tlcWorksWithSaves
			// 
			this.tlcWorksWithSaves.Text = "Works With Saves";
			this.tlcWorksWithSaves.IsEditable = false;
			this.tlcWorksWithSaves.TextAlign = HorizontalAlignment.Center;
			// 
			// tlcLastBackedUp
			// 
			this.tlcLastBackedUp.Text = "Last backed up";
			this.tlcLastBackedUp.IsEditable = false;
			this.tlcLastBackedUp.TextAlign = HorizontalAlignment.Center;
			// 
			// tlcShared
			// 
			this.tlcShared.Text = "Shared";
			this.tlcShared.IsEditable = false;
			this.tlcShared.TextAlign = HorizontalAlignment.Center;
			// 
			// tlcVersion
			// 
			this.tlcVersion.Text = "Version";
			this.tlcVersion.IsEditable = false;
			this.tlcVersion.TextAlign = HorizontalAlignment.Center;
		}

		#endregion

		private System.Windows.Forms.ContextMenuStrip cmsContextMenu;
		private BrightIdeasSoftware.OLVColumn tlcProfileName;
		private BrightIdeasSoftware.OLVColumn tlcActiveMods;
		private BrightIdeasSoftware.OLVColumn tlcWorksWithSaves;
		private BrightIdeasSoftware.OLVColumn tlcLastBackedUp;
		private BrightIdeasSoftware.OLVColumn tlcVersion;
		private BrightIdeasSoftware.OLVColumn tlcShared;
	}
}
