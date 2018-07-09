using System.Collections.Generic;
using Nexus.UI.Controls;
using Nexus.Client.Games.Fallout3.Scripting.XmlScript.CPL;
using Nexus.Client.Games.Fallout3.Scripting.XmlScript.CPL.Controls;
using Nexus.Client.ModManagement.Scripting.XmlScript;
using Nexus.Client.ModManagement.Scripting.XmlScript.CPL;
using Nexus.Client.ModManagement.Scripting.XmlScript.CPL.Controls;
using Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls;
using Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors;

namespace Nexus.Client.Games.Fallout3.Scripting.XmlScript
{
	/// <summary>
	/// Adapts a version 3.0 <see cref="XmlScript"/> to an XML script editor.
	/// </summary>
	public class XmlScript30NodeAdapter : XmlScript20NodeAdapter
	{
		#region Construtors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_xstScripType">The current xml script type.</param>
		public XmlScript30NodeAdapter(XmlScriptType p_xstScripType)
			: base(p_xstScripType)
		{
		}

		#endregion

		#region Helper Methods

		/// <summary>
		/// Creates a <see cref="CPLEditorVM"/>.
		/// </summary>
		/// <param name="p_lstModFiles">The list of files in the script's mod.</param>
		/// <returns>A <see cref="CPLEditorVM"/>.</returns>
		protected virtual CPLEditorVM CreateCPLEditorVM(IList<VirtualFileSystemItem> p_lstModFiles)
		{
			CPLTextEditorVM vmlCplTextEditor = new CPLTextEditorVM(new FO3CplHighlightingStrategy(ScriptType.GetCplParserFactory()), ScriptType.GetCplParserFactory());

			List<KeyValuePair<string, string>> lstVersionNames = new List<KeyValuePair<string, string>>();
			lstVersionNames.Add(new KeyValuePair<string, string>("FOSE Version", "foseVersion"));
			lstVersionNames.Add(new KeyValuePair<string, string>("Game Version", "gameVersion"));
			lstVersionNames.Add(new KeyValuePair<string, string>("Mod Manager Version", "managerVersion"));

			List<CplConditionEditor> lstConditionEditors = new List<CplConditionEditor>();
			lstConditionEditors.Add(new CplPluginConditionEditor(p_lstModFiles));
			lstConditionEditors.Add(new CplFlagConditionEditor());
			lstConditionEditors.Add(new CplVersionConditionEditor(lstVersionNames));
			CPLEditorVM vmlCplEditor = new CPLEditorVM(vmlCplTextEditor, lstConditionEditors, ConditionOperator.And | ConditionOperator.Or);
			return vmlCplEditor;
		}

		/// <summary>
		/// Creates a <see cref="ConditionEditorVM"/>.
		/// </summary>
		/// <param name="p_lstModFiles">The list of files in the script's mod.</param>
		/// <param name="p_cvtConverter">A converter to use to convert CPL.</param>
		/// <returns>A <see cref="ConditionEditorVM"/>.</returns>
		protected override ConditionEditorVM CreateConditionEditorVM(IList<VirtualFileSystemItem> p_lstModFiles, CPLConverter p_cvtConverter)
		{
			CPLEditorVM vmlCplEditor = CreateCPLEditorVM(p_lstModFiles);
			ConditionEditorVM vmlConditionEditor = new ConditionEditorVM(vmlCplEditor, p_cvtConverter, null);
			return vmlConditionEditor;
		}

		#endregion

		#region IXmlScriptNodeAdapter Members

		/// <summary>
		/// Gets the editor to use to edit <see cref="HeaderInfo"/>.
		/// </summary>
		/// <param name="p_hdrHeader">The header to edit.</param>
		/// <param name="p_lstModFiles">The list of files in the mod to which the <see cref="XmlScript"/>
		/// being edited belongs.</param>
		/// <returns>The editor to use to edit <see cref="HeaderInfo"/>. <c>null</c> is returned if the
		/// current <see cref="XmlScript"/> does not support editing the <see cref="HeaderInfo"/>.</returns>
		public override NodeEditor GetHeaderEditor(HeaderInfo p_hdrHeader, IList<VirtualFileSystemItem> p_lstModFiles)
		{
			HeaderEditorVM vmlHeaderEditor = new HeaderEditorVM(p_hdrHeader, p_lstModFiles, HeaderProperties.Title | HeaderProperties.TextColour | HeaderProperties.TextPosition | HeaderProperties.Image | HeaderProperties.Height);
			return new HeaderEditor(vmlHeaderEditor);
		}

		/// <summary>
		/// Gets the editor to use to edit the <see cref="XmlScript"/> prerequisites.
		/// </summary>
		/// <param name="p_xscScript">The <see cref="XmlScript"/> whose prerequisites are to be edited.</param>
		/// <param name="p_lstModFiles">The list of files in the mod to which the <see cref="XmlScript"/>
		/// being edited belongs.</param>
		/// <returns>The editor to use to edit <see cref="XmlScript"/> prerequisites. <c>null</c> is returned if the
		/// current <see cref="XmlScript"/> does not support editing the prerequisites.</returns>
		public override NodeEditor GetPrerequisitesEditor(Nexus.Client.ModManagement.Scripting.XmlScript.XmlScript p_xscScript, IList<VirtualFileSystemItem> p_lstModFiles)
		{
			FO3CplConverter cvtConverter = new FO3CplConverter(ScriptType.GetCplParserFactory());
			CPLEditorVM vmlCplEditor = CreateCPLEditorVM(p_lstModFiles);
			PrerequisitesEditorVM vmlPrerequisitesEditor = new PrerequisitesEditorVM(vmlCplEditor, cvtConverter, p_xscScript);
			return new ConditionEditor(vmlPrerequisitesEditor);
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
			InstallStepEditorVM vmlStepEditor = new InstallStepEditorVM(null, p_ispStep, InstallStepProperties.GroupSortOrder);
			return new InstallStepEditor(vmlStepEditor);
		}

		/// <summary>
		/// The editor to use to edit an <see cref="OptionGroup"/>.
		/// </summary>
		/// <param name="p_opgGroup">The <see cref="OptionGroup"/> to edit.</param>
		/// <param name="p_lstModFiles">The list of files in the mod to which the <see cref="XmlScript"/>
		/// being edited belongs.</param>
		/// <returns>The editor to use to edit an <see cref="OptionGroup"/>. <c>null</c> is returned if the
		/// current <see cref="XmlScript"/> does not support editing <see cref="OptionGroup"/>s.</returns>
		public override NodeEditor GetOptionGroupEditor(OptionGroup p_opgGroup, IList<VirtualFileSystemItem> p_lstModFiles)
		{
			OptionGroupEditorVM vmlGroupEditor = new OptionGroupEditorVM(p_opgGroup, OptionGroupProperties.Name | OptionGroupProperties.Type | OptionGroupProperties.OptionSortOrder);
			return new OptionGroupEditor(vmlGroupEditor);
		}

		#endregion
	}
}
