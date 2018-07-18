using System.Collections.Generic;
using Nexus.UI.Controls;
using Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors;
using Nexus.Client.ModManagement.Scripting.XmlScript.CPL;
using Nexus.Client.ModManagement.Scripting.XmlScript.CPL.Controls;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls
{
	/// <summary>
	/// Adapts a version 5.0 <see cref="XmlScript"/> to an XML script editor.
	/// </summary>
	public class XmlScript50NodeAdapter : IXmlScriptNodeAdapter
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
		public XmlScript50NodeAdapter(XmlScriptType p_xstScripType)
		{
			ScriptType = p_xstScripType;
		}

		#endregion

		#region Helper Methods

		/// <summary>
		/// Creates a <see cref="ConditionEditorVM"/>.
		/// </summary>
		/// <param name="p_lstModFiles">The list of files in the script's mod.</param>
		/// <param name="p_cvtConverter">A converter to use to convert CPL.</param>
		/// <returns>A <see cref="ConditionEditorVM"/>.</returns>
		protected virtual ConditionEditorVM CreateConditionEditorVM(IList<VirtualFileSystemItem> p_lstModFiles, CPLConverter p_cvtConverter)
		{
			CPLEditorVM vmlCplEditor = CreateCPLEditorVM(p_lstModFiles);
			ConditionEditorVM vmlConditionEditor = new ConditionEditorVM(vmlCplEditor, p_cvtConverter, null);
			return vmlConditionEditor;
		}

		/// <summary>
		/// Creates a <see cref="CPLEditorVM"/>.
		/// </summary>
		/// <param name="p_lstModFiles">The list of files in the script's mod.</param>
		/// <returns>A <see cref="CPLEditorVM"/>.</returns>
		protected virtual CPLEditorVM CreateCPLEditorVM(IList<VirtualFileSystemItem> p_lstModFiles)
		{
			CPLTextEditorVM vmlCplTextEditor = new CPLTextEditorVM(new CplHighlightingStrategy(ScriptType.GetCplParserFactory()), ScriptType.GetCplParserFactory());

			List<KeyValuePair<string, string>> lstVersionNames = new List<KeyValuePair<string, string>>();
			lstVersionNames.Add(new KeyValuePair<string, string>("Game Version", "gameVersion"));
			lstVersionNames.Add(new KeyValuePair<string, string>("Mod Manager Version", "managerVersion"));

			List<CplConditionEditor> lstConditionEditors = new List<CplConditionEditor>();
			lstConditionEditors.Add(new CplPluginConditionEditor(p_lstModFiles));
			lstConditionEditors.Add(new CplFlagConditionEditor());
			lstConditionEditors.Add(new CplVersionConditionEditor(lstVersionNames));
			CPLEditorVM vmlCplEditor = new CPLEditorVM(vmlCplTextEditor, lstConditionEditors, ConditionOperator.And | ConditionOperator.Or);
			return vmlCplEditor;
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
		public virtual NodeEditor GetPrerequisitesEditor(ModManagement.Scripting.XmlScript.XmlScript p_xscScript, IList<VirtualFileSystemItem> p_lstModFiles)
		{
			CPLConverter cvtConverter = new CPLConverter(ScriptType.GetCplParserFactory());
			CPLEditorVM vmlCplEditor = CreateCPLEditorVM(p_lstModFiles);
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
		public virtual NodeEditor GetInstallStepOrderEditor(ModManagement.Scripting.XmlScript.XmlScript p_xscScript, IList<VirtualFileSystemItem> p_lstModFiles)
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
		public virtual NodeEditor GetInstallStepEditor(InstallStep p_ispStep, IList<VirtualFileSystemItem> p_lstModFiles)
		{
			CPLConverter cvtConverter = new CPLConverter(ScriptType.GetCplParserFactory());
			ConditionEditorVM vmlConditionEditor = CreateConditionEditorVM(p_lstModFiles, cvtConverter);

			InstallStepEditorVM vmlStepEditor = new InstallStepEditorVM(vmlConditionEditor, p_ispStep, InstallStepProperties.Name | InstallStepProperties.GroupSortOrder | InstallStepProperties.Visibility);
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
		public virtual NodeEditor GetOptionGroupEditor(OptionGroup p_opgGroup, IList<VirtualFileSystemItem> p_lstModFiles)
		{
			OptionGroupEditorVM vmlGroupEditor = new OptionGroupEditorVM(p_opgGroup, OptionGroupProperties.Name | OptionGroupProperties.Type | OptionGroupProperties.OptionSortOrder);
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

			CPLConverter cvtConverter = new CPLConverter(ScriptType.GetCplParserFactory());
			ConditionEditorVM vmlConditionEditor = CreateConditionEditorVM(p_lstModFiles, cvtConverter);

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
			CPLConverter cvtConverter = new CPLConverter(ScriptType.GetCplParserFactory());
			ConditionallyInstalledFileSetOrderEditorVM vmlOrderEditor = new ConditionallyInstalledFileSetOrderEditorVM(p_lstConditionallyInstalledFileSets, cvtConverter);
			return new ConditionallyInstalledFileSetOrderEditor(vmlOrderEditor);
		}

		/// <summary>
		/// The editor to use to edit a <see cref="ConditionallyInstalledFileSet"/>.
		/// </summary>
		/// <param name="p_cisFileSet">The <see cref="ConditionallyInstalledFileSet"/> to edit.</param>
		/// <param name="p_lstModFiles">The list of files in the mod to which the <see cref="XmlScript"/>
		/// being edited belongs.</param>
		/// <returns>The editor to use to edit an <see cref="ConditionallyInstalledFileSet"/>. <c>null</c> is returned if the
		/// current <see cref="XmlScript"/> does not support editing <see cref="ConditionallyInstalledFileSet"/>s.</returns>
		public virtual NodeEditor GetConditionallyInstalledFileSetEditor(ConditionallyInstalledFileSet p_cipPattern, IList<VirtualFileSystemItem> p_lstModFiles)
		{
			CPLConverter cvtConverter = new CPLConverter(ScriptType.GetCplParserFactory());
			ConditionEditorVM vmlConditionEditor = CreateConditionEditorVM(p_lstModFiles, cvtConverter);

			InstallableFileEditorVM vmlInstallableFile = new InstallableFileEditorVM(null, p_lstModFiles);
			FileListEditorVM vmlFileList = new FileListEditorVM(vmlInstallableFile, p_cipPattern.Files);

			ConditionallyInstalledFileSetEditorVM vmlFileInstall = new ConditionallyInstalledFileSetEditorVM(vmlConditionEditor, vmlFileList, p_cipPattern);
			return new ConditionallyInstalledFileSetEditor(vmlFileInstall);
		}

		#endregion
	}
}
