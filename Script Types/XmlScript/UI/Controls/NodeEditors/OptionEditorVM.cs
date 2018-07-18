using Nexus.UI.Controls;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	public class OptionEditorVM : IViewModel
	{
		public OptionInfoEditorVM OptionInfoEditorVM { get; private set; }
		public FileListEditorVM FileListEditorVM { get; private set; }
		public OptionTypeResolverEditorVM OptionTypeResolverEditorVM { get; private set; }

		public OptionEditorVM(OptionInfoEditorVM p_vmlOptionInfo, FileListEditorVM p_vmlFileList, OptionTypeResolverEditorVM p_vmlTypeResolverEditor)
		{
			OptionInfoEditorVM = p_vmlOptionInfo;
			FileListEditorVM = p_vmlFileList;
			OptionTypeResolverEditorVM = p_vmlTypeResolverEditor;
		}

		#region IViewModel Members

		public bool Validate()
		{
			return OptionInfoEditorVM.Validate();
		}

		#endregion
	}
}
