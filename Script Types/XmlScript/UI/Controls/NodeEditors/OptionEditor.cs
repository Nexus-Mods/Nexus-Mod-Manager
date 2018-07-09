using System.Windows.Forms;
using Nexus.UI.Controls;
using System.ComponentModel;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	public partial class OptionEditor : NodeEditor
	{
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public OptionEditorVM ViewModel { get; set; }

		public OptionEditor(OptionEditorVM p_vmlViewModel)
		{
			ViewModel = p_vmlViewModel;
			InitializeComponent();

			oieInfo.ViewModel = p_vmlViewModel.OptionInfoEditorVM;
			fleInstallFiles.ViewModel = p_vmlViewModel.FileListEditorVM;
			treTypeResolverEditor.ViewModel = p_vmlViewModel.OptionTypeResolverEditorVM;			
		}

		private void tblPlugin_Deselecting(object sender, TabControlCancelEventArgs e)
		{
			if (e.TabPage == tpgInfo)
				e.Cancel = !ViewModel.Validate();
		}

		/// <summary>
		/// Gets the view model of the editor.
		/// </summary>
		/// <returns>The view model of the editor.</returns>
		public override IViewModel GetViewModel()
		{
			return (IViewModel)ViewModel;
		}
	}
}
