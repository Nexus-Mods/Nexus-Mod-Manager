using System.Collections.Generic;
using Nexus.UI.Controls;
using Nexus.Client.Games.Skyrim.Scripting.XmlScript.CPL;
using Nexus.Client.ModManagement.Scripting.XmlScript;
using Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls;
using Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors;

namespace Nexus.Client.Games.Skyrim.Scripting.XmlScript
{
	/// <summary>
	/// Adapts a version 4.0 <see cref="XmlScript"/> to an XML script editor.
	/// </summary>
	public class XmlScript40NodeAdapter : Nexus.Client.Games.Fallout3.Scripting.XmlScript.XmlScript40NodeAdapter
	{
		#region Construtors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_xstScripType">The current xml script type.</param>
		public XmlScript40NodeAdapter(XmlScriptType p_xstScripType)
			: base(p_xstScripType)
		{
		}

		#endregion

		#region IXmlScriptNodeAdapter Members

		/// <summary>
		/// Gets the editor to use to edit an <see cref="InstallStep"/>.
		/// </summary>
		/// <param name="p_ispStep">The <see cref="InstallStep"/> to edit.</param>
		/// <param name="p_lstModFiles">The list of files in the mod to which the <see cref="XmlScript"/>
		/// being edited belongs.</param>
		/// <returns>The editor to use to edit an <see cref="InstallStep"/>. <c>null</c> is returned if the
		/// current <see cref="XmlScript"/> does not support editing <see cref="InstallStep"/>s.</returns>
		public override NodeEditor GetInstallStepEditor(InstallStep p_ispStep, IList<VirtualFileSystemItem> p_lstModFiles)
		{
			SkyrimCplConverter cvtConverter = new SkyrimCplConverter(ScriptType.GetCplParserFactory());
			ConditionEditorVM vmlConditionEditor = CreateConditionEditorVM(p_lstModFiles, cvtConverter);

			InstallStepEditorVM vmlStepEditor = new InstallStepEditorVM(vmlConditionEditor, p_ispStep, InstallStepProperties.Name | InstallStepProperties.GroupSortOrder | InstallStepProperties.Visibility);
			return new InstallStepEditor(vmlStepEditor);
		}

		#endregion
	}
}
