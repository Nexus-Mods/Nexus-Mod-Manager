using System.Collections.Generic;
using Nexus.UI.Controls;
using Nexus.Client.Games.Fallout3.Scripting.XmlScript.CPL;
using Nexus.Client.ModManagement.Scripting.XmlScript;
using Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls;
using Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors;

namespace Nexus.Client.Games.Fallout3.Scripting.XmlScript
{
	/// <summary>
	/// Adapts a version 4.0 <see cref="XmlScript"/> to an XML script editor.
	/// </summary>
	public class XmlScript40NodeAdapter : XmlScript30NodeAdapter
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
		/// Gets the editor to use to edit the <see cref="XmlScript"/>'s install step order.
		/// </summary>
		/// <param name="p_xscScript">The <see cref="XmlScript"/> whose install step order is to be edited.</param>
		/// <param name="p_lstModFiles">The list of files in the mod to which the <see cref="XmlScript"/>
		/// being edited belongs.</param>
		/// <returns>The editor to use to edit the <see cref="XmlScript"/>'s install step order. <c>null</c> is returned if the
		/// current <see cref="XmlScript"/> does not support editing the install step order.</returns>
		public override NodeEditor GetInstallStepOrderEditor(Nexus.Client.ModManagement.Scripting.XmlScript.XmlScript p_xscScript, IList<VirtualFileSystemItem> p_lstModFiles)
		{
			InstallStepsEditorVM vmlStepsEditor = new InstallStepsEditorVM(p_xscScript);
			return new InstallStepsEditor(vmlStepsEditor);
		}

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
			FO3CplConverter cvtConverter = new FO3CplConverter(ScriptType.GetCplParserFactory());
			ConditionEditorVM vmlConditionEditor = CreateConditionEditorVM(p_lstModFiles, cvtConverter);

			InstallStepEditorVM vmlStepEditor = new InstallStepEditorVM(vmlConditionEditor, p_ispStep, InstallStepProperties.Name | InstallStepProperties.GroupSortOrder | InstallStepProperties.Visibility);
			return new InstallStepEditor(vmlStepEditor);
		}

		#endregion
	}
}
