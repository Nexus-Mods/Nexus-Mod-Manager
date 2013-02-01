using System;
using System.Windows.Forms;
using System.Drawing;
using BrightIdeasSoftware.Design;

namespace Nexus.Client.UI.Controls
{
	/// <summary>
	/// A customizable progress bar.
	/// </summary>
	partial class CategoryListView
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
			this.tlcInstallDate = new BrightIdeasSoftware.OLVColumn();
			this.tlcEndorsement = new BrightIdeasSoftware.OLVColumn();
			this.tlcVersion = new BrightIdeasSoftware.OLVColumn();
			this.tlcWebVersion = new BrightIdeasSoftware.OLVColumn();
			this.tlcAuthor = new BrightIdeasSoftware.OLVColumn();

			this.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.tlcModName,
            this.tlcInstallDate,
			this.tlcEndorsement,
            this.tlcVersion,
            this.tlcWebVersion,
            this.tlcAuthor});
			// 
			// cmsContextMenu
			// 
			this.cmsContextMenu.Name = "cmsContextMenu";
			this.cmsContextMenu.Size = new System.Drawing.Size(61, 4);
			// 
			// tlcModName
			// 
			this.tlcModName.Text = "Name";
			this.tlcModName.Width = 250;
			this.tlcModName.IsEditable = false;
			// 
			// tlcInstallDate
			// 
			this.tlcInstallDate.Text = "Install Date";
			this.tlcInstallDate.Width = 80;
			this.tlcInstallDate.IsEditable = false;
			// 
			// tlcEndorsement
			// 
			this.tlcEndorsement.Text = "Endorsement";
			this.tlcEndorsement.TextAlign = HorizontalAlignment.Center;
			this.tlcEndorsement.Width = 50;
			this.tlcEndorsement.IsEditable = false;
			// 
			// tlcVersion
			// 
			this.tlcVersion.Text = "Version";
			this.tlcVersion.TextAlign = HorizontalAlignment.Center;
			this.tlcVersion.IsEditable = false;
			// 
			// tlcWebVersion
			// 
			this.tlcWebVersion.Text = "Latest Version";
			this.tlcWebVersion.TextAlign = HorizontalAlignment.Center;
			this.tlcWebVersion.IsEditable = false;
			// 
			// tlcAuthor
			// 
			this.tlcAuthor.Text = "Author";
			this.tlcAuthor.Width = 60;
			this.tlcAuthor.IsEditable = false;
		}

		#endregion

		private System.Windows.Forms.ContextMenuStrip cmsContextMenu;
		private BrightIdeasSoftware.OLVColumn tlcModName;
		private BrightIdeasSoftware.OLVColumn tlcInstallDate;
		private BrightIdeasSoftware.OLVColumn tlcEndorsement;
		private BrightIdeasSoftware.OLVColumn tlcVersion;
		private BrightIdeasSoftware.OLVColumn tlcWebVersion;
		private BrightIdeasSoftware.OLVColumn tlcAuthor;
	}
}
