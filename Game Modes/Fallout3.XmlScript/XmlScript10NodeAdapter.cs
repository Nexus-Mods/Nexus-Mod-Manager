using System.Collections.Generic;
using Nexus.Client.Games.Fallout3.Scripting.XmlScript.CPL;
using Nexus.Client.Games.Fallout3.Scripting.XmlScript.CPL.Controls;
using Nexus.Client.ModManagement.Scripting.XmlScript;
using Nexus.Client.ModManagement.Scripting.XmlScript.CPL.Controls;
using Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls;
using Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors;
using Nexus.UI.Controls;

namespace Nexus.Client.Games.Fallout3.Scripting.XmlScript
{
	/// <summary>
	/// Adapts a version 1.0 <see cref="XmlScript"/> to an XML script editor.
	/// </summary>
	public class XmlScript10NodeAdapter : IXmlScriptNodeAdapter
	{
		#region Properties

		/// <summary>
		/// Gets the current xml script type.
		/// </summary>
		/// <value>The current xml script type.</value>
		protected XmlScriptType ScriptType { get; private set; }

		#endregion

		#region Construtors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_xstScripType">The current xml script type.</param>
		public XmlScript10NodeAdapter(XmlScriptType p_xstScripType)
		{
			ScriptType = p_xstScripType;
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
		public virtual NodeEditor GetHeaderEditor(HeaderInfo p_hdrHeader, IList<VirtualFileSystemItem> p_lstModFiles)
		{
			HeaderEditorVM vmlHeaderEditor = new HeaderEditorVM(p_hdrHeader, p_lstModFiles, HeaderProperties.Title);
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
		public virtual NodeEditor GetPrerequisitesEditor(Nexus.Client.ModManagement.Scripting.XmlScript.XmlScript p_xscScript, IList<VirtualFileSystemItem> p_lstModFiles)
		{
			CPLTextEditorVM vmlCplTextEditor = new CPLTextEditorVM(new FO3CplHighlightingStrategy(ScriptType.GetCplParserFactory()), ScriptType.GetCplParserFactory());

			List<KeyValuePair<string, string>> lstVersionNames = new List<KeyValuePair<string, string>>();
			lstVersionNames.Add(new KeyValuePair<string, string>("FOSE Version", "foseVersion"));
			lstVersionNames.Add(new KeyValuePair<string, string>("Game Version", "gameVersion"));
			lstVersionNames.Add(new KeyValuePair<string, string>("Mod Manager Version", "managerVersion"));

			List<CplConditionEditor> lstConditionEditors = new List<CplConditionEditor>();
			lstConditionEditors.Add(new CplPluginConditionEditor(p_lstModFiles));
			lstConditionEditors.Add(new CplVersionConditionEditor(lstVersionNames));
			CPLEditorVM vmlCplEditor = new CPLEditorVM(vmlCplTextEditor, lstConditionEditors, ConditionOperator.And);

			FO3CplConverter cvtConverter = new FO3CplConverter(ScriptType.GetCplParserFactory());
			PrerequisitesEditorVM vmlPrerequisitesEditor = new PrerequisitesEditorVM(vmlCplEditor, cvtConverter, p_xscScript);
			return new ConditionEditor(vmlPrerequisitesEditor);
		}

		/// <summary>
		/// Gets the editor to use to edit <see cref="XmlScript"/>'s required install files.
		/// </summary>
		/// <param name="p_lstFiles">The list of required install files to edit.</param>
		/// <param name="p_lstModFiles">The list of files in the mod to which the <see cref="XmlScript"/>
		/// being edited belongs.</param>
		/// <returns>The editor to use to edit <see cref="XmlScript"/>'s required install files. <c>null</c> is returned if the
		/// current <see cref="XmlScript"/> does not support editing the required install files.</returns>
		public virtual NodeEditor GetRequiredInstallFilesEditor(IList<InstallableFile> p_lstFiles, IList<VirtualFileSystemItem> p_lstModFiles)
		{
			InstallableFileEditorVM vmlInstallableFile = new InstallableFileEditorVM(null, p_lstModFiles);
			FileListEditorVM vmlFileList = new FileListEditorVM(vmlInstallableFile, p_lstFiles);
			return new FileListEditor(vmlFileList);
		}

		/// <summary>
		/// Gets the editor to use to edit the <see cref="XmlScript"/>'s install step order.
		/// </summary>
		/// <param name="p_xscScript">The <see cref="XmlScript"/> whose install step order is to be edited.</param>
		/// <param name="p_lstModFiles">The list of files in the mod to which the <see cref="XmlScript"/>
		/// being edited belongs.</param>
		/// <returns>The editor to use to edit the <see cref="XmlScript"/>'s install step order. <c>null</c> is returned if the
		/// current <see cref="XmlScript"/> does not support editing the install step order.</returns>
		public virtual NodeEditor GetInstallStepOrderEditor(Nexus.Client.ModManagement.Scripting.XmlScript.XmlScript p_xscScript, IList<VirtualFileSystemItem> p_lstModFiles)
		{
			return null;
		}

		/// <summary>
		/// Gets the editor to use to edit an <see cref="InstallStep"/>.
		/// </summary>
		/// <param name="p_ispStep">The <see cref="InstallStep"/> to edit.</param>
		/// <param name="p_lstModFiles">The list of files in the mod to which the <see cref="XmlScript"/>
		/// being edited belongs.</param>
		/// <returns>The editor to use to edit an <see cref="InstallStep"/>. <c>null</c> is returned if the
		/// current <see cref="XmlScript"/> does not support editing <see cref="InstallStep"/>s.</returns>
		public virtual NodeEditor GetInstallStepEditor(InstallStep p_ispStep, IList<VirtualFileSystemItem> p_lstModFiles)
		{
			return null;
		}

		/// <summary>
		/// The editor to use to edit an <see cref="OptionGroup"/>.
		/// </summary>
		/// <param name="p_opgGroup">The <see cref="OptionGroup"/> to edit.</param>
		/// <param name="p_lstModFiles">The list of files in the mod to which the <see cref="XmlScript"/>
		/// being edited belongs.</param>
		/// <returns>The editor to use to edit an <see cref="OptionGroup"/>. <c>null</c> is returned if the
		/// current <see cref="XmlScript"/> does not support editing <see cref="OptionGroup"/>s.</returns>
		public virtual NodeEditor GetOptionGroupEditor(OptionGroup p_opgGroup, IList<VirtualFileSystemItem> p_lstModFiles)
		{
			OptionGroupEditorVM vmlGroupEditor = new OptionGroupEditorVM(p_opgGroup, OptionGroupProperties.Name | OptionGroupProperties.Type);
			return new OptionGroupEditor(vmlGroupEditor);
		}

		/// <summary>
		/// The editor to use to edit an <see cref="Option"/>.
		/// </summary>
		/// <param name="p_optOption">The <see cref="Option"/> to edit.</param>
		/// <param name="p_lstModFiles">The list of files in the mod to which the <see cref="XmlScript"/>
		/// being edited belongs.</param>
		/// <returns>The editor to use to edit an <see cref="Option"/>. <c>null</c> is returned if the
		/// current <see cref="XmlScript"/> does not support editing <see cref="Option"/>s.</returns>
		public virtual NodeEditor GetOptionEditor(Option p_optOption, IList<VirtualFileSystemItem> p_lstModFiles)
		{
			OptionInfoEditorVM vmlOptionInfo = new OptionInfoEditorVM(p_optOption, p_lstModFiles);
			
			InstallableFileEditorVM vmlInstallableFile = new InstallableFileEditorVM(null, p_lstModFiles);
			FileListEditorVM vmlFileList = new FileListEditorVM(vmlInstallableFile, p_optOption.Files);

			CPLTextEditorVM vmlCplTextEditor = new CPLTextEditorVM(new FO3CplHighlightingStrategy(ScriptType.GetCplParserFactory()), ScriptType.GetCplParserFactory());

			List<CplConditionEditor> lstConditionEditors = new List<CplConditionEditor>();
			lstConditionEditors.Add(new CplPluginConditionEditor(p_lstModFiles));			
			CPLEditorVM vmlCplEditor = new CPLEditorVM(vmlCplTextEditor, lstConditionEditors, ConditionOperator.And | ConditionOperator.Or);

			FO3CplConverter cvtConverter = new FO3CplConverter(ScriptType.GetCplParserFactory());
			ConditionEditorVM vmlConditionEditor = new ConditionEditorVM(vmlCplEditor, cvtConverter, null);
			ConditionalTypePatternEditorVM vmlPatternEditor = new ConditionalTypePatternEditorVM(vmlConditionEditor, null);
			ConditionalTypeEditorVM vmlTypeEditor = new ConditionalTypeEditorVM(vmlPatternEditor, cvtConverter, null);
			OptionTypeResolverEditorVM vmlTypeResolverEditor = new OptionTypeResolverEditorVM(vmlTypeEditor, p_optOption);

			OptionEditorVM vmlOptionEditor = new OptionEditorVM(vmlOptionInfo, vmlFileList, vmlTypeResolverEditor);

			return new OptionEditor(vmlOptionEditor);
		}

		/// <summary>
		/// Gets the editor to use to edit the <see cref="XmlScript"/>'s conditionally installed file set order.
		/// </summary>
		/// <param name="p_lstConditionallyInstalledFileSets">The conditionally installed file sets whose order is to be edited.</param>
		/// <param name="p_lstModFiles">The list of files in the mod to which the <see cref="XmlScript"/>
		/// being edited belongs.</param>
		/// <returns>The editor to use to the the <see cref="XmlScript"/>'s conditionally installed file set order. <c>null</c> is returned if the
		/// current <see cref="XmlScript"/> does not support editing the conditionally installed file set order.</returns>
		public virtual NodeEditor GetConditionallyInstalledFileSetOrderEditor(IList<ConditionallyInstalledFileSet> p_lstConditionallyInstalledFileSets, IList<VirtualFileSystemItem> p_lstModFiles)
		{
			return null;
		}

		/// <summary>
		/// The editor to use to edit a <see cref="ConditionallyInstalledFileSet"/>.
		/// </summary>
		/// <param name="p_cipPattern">The <see cref="ConditionallyInstalledFileSet"/> to edit.</param>
		/// <param name="p_lstModFiles">The list of files in the mod to which the <see cref="XmlScript"/>
		/// being edited belongs.</param>
		/// <returns>The editor to use to edit an <see cref="ConditionallyInstalledFileSet"/>. <c>null</c> is returned if the
		/// current <see cref="XmlScript"/> does not support editing <see cref="ConditionallyInstalledFileSet"/>s.</returns>
		public virtual NodeEditor GetConditionallyInstalledFileSetEditor(ConditionallyInstalledFileSet p_cipPattern, IList<VirtualFileSystemItem> p_lstModFiles)
		{
			return null;
		}

		#endregion
	}
}
