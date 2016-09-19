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
			this.tlcDownloadDate = new BrightIdeasSoftware.OLVColumn();
			this.tlcEndorsement = new BrightIdeasSoftware.OLVColumn();
			this.tlcDownloadId = new BrightIdeasSoftware.OLVColumn();
			this.tlcWebVersion = new BrightIdeasSoftware.OLVColumn();
			this.tlcAuthor = new BrightIdeasSoftware.OLVColumn();
			this.tlcCategory = new BrightIdeasSoftware.OLVColumn();
            this.tlcLoadOrder = new BrightIdeasSoftware.OLVColumn();

			this.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
			this.tlcModName,
			this.tlcCategory,
            this.tlcLoadOrder,
			this.tlcInstallDate,
			this.tlcDownloadDate,
			this.tlcEndorsement,
			this.tlcDownloadId,
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
			this.tlcModName.IsEditable = false;
            //
            // tlcLoadOrder
            //
            this.tlcLoadOrder.Text = "Load Order";
            this.tlcLoadOrder.IsEditable = true;
            this.tlcLoadOrder.TextAlign = HorizontalAlignment.Center;
			// 
			// tlcInstallDate
			// 
			this.tlcInstallDate.Text = "Install Date";
			this.tlcInstallDate.TextAlign = HorizontalAlignment.Center;
			this.tlcInstallDate.IsEditable = false;
			// 
			// tlcDownloadDate
			// 
			this.tlcDownloadDate.Text = "Download Date";
			this.tlcDownloadDate.TextAlign = HorizontalAlignment.Center;
			this.tlcDownloadDate.IsEditable = false;
			// 
			// tlcEndorsement
			// 
			this.tlcEndorsement.Text = "Endorsement";
			this.tlcEndorsement.TextAlign = HorizontalAlignment.Center;
			this.tlcEndorsement.IsEditable = false;
			// 
			// tlcDownloadId
			// 
			this.tlcDownloadId.Text = "Download Id";
			this.tlcDownloadId.TextAlign = HorizontalAlignment.Center;
			this.tlcDownloadId.IsEditable = false;
			// 
			// tlcWebVersion
			// 
			this.tlcWebVersion.Text = "Mod Version";
			this.tlcWebVersion.TextAlign = HorizontalAlignment.Center;
			this.tlcWebVersion.IsEditable = false;
			// 
			// tlcAuthor
			// 
			this.tlcAuthor.Text = "Author";
			this.tlcAuthor.IsEditable = false;
			// 
			// tlcCategory
			// 
			this.tlcCategory.Text = "Category";
			this.tlcCategory.IsEditable = false;
		}

		#endregion

		private System.Windows.Forms.ContextMenuStrip cmsContextMenu;
		private BrightIdeasSoftware.OLVColumn tlcModName;
		private BrightIdeasSoftware.OLVColumn tlcInstallDate;
		private BrightIdeasSoftware.OLVColumn tlcDownloadDate;
		private BrightIdeasSoftware.OLVColumn tlcEndorsement;
		private BrightIdeasSoftware.OLVColumn tlcDownloadId;
		private BrightIdeasSoftware.OLVColumn tlcWebVersion;
		private BrightIdeasSoftware.OLVColumn tlcAuthor;
		private BrightIdeasSoftware.OLVColumn tlcCategory;
        private BrightIdeasSoftware.OLVColumn tlcLoadOrder;
	}
}
