namespace Nexus.Client.ModManagement.UI
{
	using DevExpress.XtraGrid;
	using DevExpress.XtraGrid.Views.Grid;
	using DevExpress.XtraEditors;

	/// <summary>
	/// Hosts the flat/default Mods XtraGrid frontend independently from the
	/// Mod Manager orchestration, toolbar and ViewModel lifecycle.
	/// </summary>
	internal partial class ModGridDXControl : XtraUserControl
	{
		/// <summary>
		/// Initializes the extracted flat Mods grid frontend.
		/// </summary>
		public ModGridDXControl()
		{
			InitializeComponent();
		}

		/// <summary>
		/// Gets the underlying DevExpress grid control.
		/// </summary>
		internal GridControl GridControl => gridControl;

		/// <summary>
		/// Gets the underlying DevExpress grid view.
		/// </summary>
		internal GridView GridView => gridView;
	}
}
