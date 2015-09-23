using System;
using System.Windows.Forms;
using System.Drawing;
using BrightIdeasSoftware.Design;

namespace Nexus.Client.UI.Controls
{
	/// <summary>
	/// A customizable progress bar.
	/// </summary>
	partial class ProfileListView
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
			this.tlcModName = new BrightIdeasSoftware.OLVColumn();
			this.tlcFileName = new BrightIdeasSoftware.OLVColumn();
			this.tlcModId = new BrightIdeasSoftware.OLVColumn();
			this.tlcFileCount = new BrightIdeasSoftware.OLVColumn();

			this.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
			this.tlcModName,
			this.tlcFileName,
			this.tlcModId,
			this.tlcFileCount});
			// 
			// cmsContextMenu
			// 
			this.cmsContextMenu.Name = "cmsContextMenu";
			this.cmsContextMenu.Size = new System.Drawing.Size(61, 4);
			// 
			// tlcModName
			// 
			this.tlcModName.Text = "Mod Name";
			this.tlcModName.IsEditable = false;
			// 
			// tlcInstallDate
			// 
			this.tlcFileName.Text = "Mod File";
			this.tlcFileName.IsEditable = false;
			// 
			// tlcEndorsement
			// 
			this.tlcModId.Text = "Mod Id";
			this.tlcModId.IsEditable = false;
			// 
			// tlcEndorsement
			// 
			this.tlcFileCount.Text = "Files";
			this.tlcFileCount.IsEditable = false;
		}

		#endregion

		private System.Windows.Forms.ContextMenuStrip cmsContextMenu;
		private BrightIdeasSoftware.OLVColumn tlcModName;
		private BrightIdeasSoftware.OLVColumn tlcFileName;
		private BrightIdeasSoftware.OLVColumn tlcModId;
		private BrightIdeasSoftware.OLVColumn tlcFileCount;
	}
}
