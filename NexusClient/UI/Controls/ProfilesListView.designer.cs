using System.Windows.Forms;

namespace Nexus.Client.UI.Controls
{
	/// <summary>
	/// A customizable progress bar.
	/// </summary>
	partial class ProfilesListView
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
			this.tlcVersion = new BrightIdeasSoftware.OLVColumn();
			this.tlcAuthor = new BrightIdeasSoftware.OLVColumn();
			this.tlcShared = new BrightIdeasSoftware.OLVColumn();
			this.tlcEdited = new BrightIdeasSoftware.OLVColumn();
			this.tlcRevert = new BrightIdeasSoftware.OLVColumn();

			//this.Scrollable = true;

			this.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
			this.tlcProfileName,
			this.tlcActiveMods,
			this.tlcLastBackedUp,
			this.tlcVersion,
			this.tlcAuthor,
			this.tlcShared,
			this.tlcEdited,
			this.tlcRevert});
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
			// tlcLastBackedUp
			// 
			this.tlcLastBackedUp.Text = "Last backed up";
			this.tlcLastBackedUp.IsEditable = false;
			this.tlcLastBackedUp.TextAlign = HorizontalAlignment.Center;
			// 
			// tlcVersion
			// 
			this.tlcVersion.Text = "Version";
			this.tlcVersion.IsEditable = false;
			this.tlcVersion.TextAlign = HorizontalAlignment.Center;
			// 
			// tlcAuthor
			// 
			this.tlcAuthor.Text = "Author";
			this.tlcAuthor.IsEditable = false;
			this.tlcAuthor.TextAlign = HorizontalAlignment.Center;
			// 
			// tlcShared
			// 
			this.tlcShared.Text = "Shared";
			this.tlcShared.IsEditable = false;
			this.tlcShared.TextAlign = HorizontalAlignment.Center;
			// 
			// tlcEdited
			// 
			this.tlcEdited.Text = "Edited";
			this.tlcEdited.IsEditable = false;
			this.tlcEdited.TextAlign = HorizontalAlignment.Center;
			// 
			// tlcRevert
			// 
			this.tlcRevert.Text = "R";
			this.tlcRevert.IsEditable = false;
			this.tlcRevert.TextAlign = HorizontalAlignment.Center;
			this.tlcRevert.ToolTipText = "Revert Profile";

		}

		#endregion

		private System.Windows.Forms.ContextMenuStrip cmsContextMenu;
		private BrightIdeasSoftware.OLVColumn tlcProfileName;
		private BrightIdeasSoftware.OLVColumn tlcActiveMods;
		private BrightIdeasSoftware.OLVColumn tlcLastBackedUp;
		private BrightIdeasSoftware.OLVColumn tlcVersion;
		private BrightIdeasSoftware.OLVColumn tlcAuthor;
		private BrightIdeasSoftware.OLVColumn tlcShared;
		private BrightIdeasSoftware.OLVColumn tlcEdited;
		private BrightIdeasSoftware.OLVColumn tlcRevert;
	}
}
