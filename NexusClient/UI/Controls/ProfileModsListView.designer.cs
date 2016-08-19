using System;
using System.Windows.Forms;
using System.Drawing;
using BrightIdeasSoftware.Design;

namespace Nexus.Client.UI.Controls
{
	/// <summary>
	/// A customizable progress bar.
	/// </summary>
	partial class ProfileModsListView
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
			this.tlcProfileModName = new BrightIdeasSoftware.OLVColumn();
			this.tlcProfileModStatus = new BrightIdeasSoftware.OLVColumn();
			this.tlcProfileModDownloadID = new BrightIdeasSoftware.OLVColumn();
			
			//this.Scrollable. = true;

			this.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
			this.tlcProfileModName,
			this.tlcProfileModDownloadID,
			this.tlcProfileModStatus});
			// 
			// cmsContextMenu
			// 
			this.cmsContextMenu.Name = "cmsContextMenu";
			// 
			// tlcModProfileName
			// 
			this.tlcProfileModName.Text = "Mods";
			this.tlcProfileModName.IsEditable = false;
			// 
			// tlcProfileModDownloadID
			// 
			this.tlcProfileModDownloadID.Text = "DownloadID";
			this.tlcProfileModDownloadID.IsEditable = false;
			// 
			// tlcProfileModStatus
			// 
			this.tlcProfileModStatus.Text = "Status";
			this.tlcProfileModStatus.IsEditable = false;
		}

		#endregion

		private System.Windows.Forms.ContextMenuStrip cmsContextMenu;
		private BrightIdeasSoftware.OLVColumn tlcProfileModName;
		private BrightIdeasSoftware.OLVColumn tlcProfileModDownloadID;
		private BrightIdeasSoftware.OLVColumn tlcProfileModStatus;



	}
}
