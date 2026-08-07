namespace Nexus.Client.ModManagement.UI
{
	partial class BackupManagerForm
	{
		private System.ComponentModel.IContainer components = null;

		protected override void Dispose(bool disposing)
		{
			if (disposing && components != null)
				components.Dispose();
			base.Dispose(disposing);
		}

		private void InitializeComponent()
		{
			this.gridControl = new DevExpress.XtraGrid.GridControl();
			this.gridView = new DevExpress.XtraGrid.Views.Grid.GridView();
			this.btBackup = new DevExpress.XtraEditors.SimpleButton();
			this.btCancel = new DevExpress.XtraEditors.SimpleButton();
			this.lblBackup = new DevExpress.XtraEditors.LabelControl();
			((System.ComponentModel.ISupportInitialize)(this.gridControl)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.gridView)).BeginInit();
			this.SuspendLayout();
			// gridControl
			this.gridControl.Location = new System.Drawing.Point(16, 58);
			this.gridControl.MainView = this.gridView;
			this.gridControl.Name = "gridControl";
			this.gridControl.Size = new System.Drawing.Size(543, 186);
			this.gridControl.TabIndex = 0;
			this.gridControl.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] { this.gridView });
			// gridView
			this.gridView.GridControl = this.gridControl;
			this.gridView.Name = "gridView";
			this.gridView.OptionsBehavior.AllowAddRows = DevExpress.Utils.DefaultBoolean.False;
			this.gridView.OptionsBehavior.AllowDeleteRows = DevExpress.Utils.DefaultBoolean.False;
			this.gridView.OptionsSelection.EnableAppearanceFocusedCell = false;
			this.gridView.OptionsView.ShowGroupPanel = false;
			this.gridView.Columns.AddVisible("Selected", "Backup").Width = 55;
			this.gridView.Columns.AddVisible("Category", "Category").Width = 160;
			this.gridView.Columns.AddVisible("SizeMb", "Size (MB)").Width = 70;
			this.gridView.Columns.AddVisible("TotalFiles", "Total Files").Width = 70;
			this.gridView.Columns.AddVisible("EstimatedBackupSize", "Est. Backup Size").Width = 100;
			this.gridView.Columns.AddVisible("EstimatedBackupTime", "Est. Backup Time").Width = 110;
			for (int i = 1; i < this.gridView.Columns.Count; i++)
				this.gridView.Columns[i].OptionsColumn.AllowEdit = false;
			this.gridView.CellValueChanged += new DevExpress.XtraGrid.Views.Base.CellValueChangedEventHandler(this.gridView_CellValueChanged);
			this.gridView.ShownEditor += new System.EventHandler(this.gridView_ShownEditor);
			// btBackup
			this.btBackup.Location = new System.Drawing.Point(16, 260);
			this.btBackup.Name = "btBackup";
			this.btBackup.Size = new System.Drawing.Size(75, 28);
			this.btBackup.Text = "Backup";
			this.btBackup.Click += new System.EventHandler(this.btBackup_Click);
			// btCancel
			this.btCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this.btCancel.Location = new System.Drawing.Point(97, 260);
			this.btCancel.Name = "btCancel";
			this.btCancel.Size = new System.Drawing.Size(75, 28);
			this.btCancel.Text = "Cancel";
			this.btCancel.Click += new System.EventHandler(this.btCancel_Click);
			// lblBackup
			this.lblBackup.Location = new System.Drawing.Point(16, 30);
			this.lblBackup.Name = "lblBackup";
			this.lblBackup.Size = new System.Drawing.Size(194, 13);
			this.lblBackup.Text = "Select the files that you want to backup.";
			// BackupManagerForm
			this.AcceptButton = this.btBackup;
			this.CancelButton = this.btCancel;
			this.ClientSize = new System.Drawing.Size(575, 304);
			this.Controls.Add(this.gridControl);
			this.Controls.Add(this.btBackup);
			this.Controls.Add(this.btCancel);
			this.Controls.Add(this.lblBackup);
			this.m_fpdFontProvider.SetFontSet(this, "StandardText");
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "BackupManagerForm";
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
			this.Text = "Nexus Mod Manager Backup";
			((System.ComponentModel.ISupportInitialize)(this.gridControl)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.gridView)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();
		}

		private DevExpress.XtraGrid.GridControl gridControl;
		private DevExpress.XtraGrid.Views.Grid.GridView gridView;
		private DevExpress.XtraEditors.SimpleButton btBackup;
		private DevExpress.XtraEditors.SimpleButton btCancel;
		private DevExpress.XtraEditors.LabelControl lblBackup;
	}
}
