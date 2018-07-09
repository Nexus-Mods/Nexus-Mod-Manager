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
	/// Adapts a version 2.0 <see cref="XmlScript"/> to an XML script editor.
	/// </summary>
	public class XmlScript20NodeAdapter : XmlScript10NodeAdapter
	{
		#region Construtors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_xstScripType">The current xml script type.</param>
		public XmlScript20NodeAdapter(XmlScriptType p_xstScripType)
			: base(p_xstScripType)
		{
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
			CPLTextEditorVM vmlCplTextEditor = new CPLTextEditorVM(new FO3CplHighlightingStrategy(ScriptType.GetCplParserFactory()), ScriptType.GetCplParserFactory());
			
			List<CplConditionEditor> lstConditionEditors = new List<CplConditionEditor>();
			lstConditionEditors.Add(new CplPluginConditionEditor(p_lstModFiles));
			lstConditionEditors.Add(new CplFlagConditionEditor());
			CPLEditorVM vmlCplEditor = new CPLEditorVM(vmlCplTextEditor, lstConditionEditors, ConditionOperator.And | ConditionOperator.Or);


			ConditionEditorVM vmlConditionEditor = new ConditionEditorVM(vmlCplEditor, p_cvtConverter, null);
			return vmlConditionEditor;
		}

		#endregion

		#region IXmlScriptNodeAdapter Members

		/// <summary>
		/// The editor to use to edit an <see cref="Option"/>.
		/// </summary>
		/// <param name="p_optOption">The <see cref="Option"/> to edit.</param>
		/// <param name="p_lstModFiles">The list of files in the mod to which the <see cref="XmlScript"/>
		/// being edited belongs.</param>
		/// <returns>The editor to use to edit an <see cref="Option"/>. <c>null</c> is returned if the
		/// current <see cref="XmlScript"/> does not support editing <see cref="Option"/>s.</returns>
		public override NodeEditor GetOptionEditor(Option p_optOption, IList<VirtualFileSystemItem> p_lstModFiles)
		{
			OptionInfoEditorVM vmlOptionInfo = new OptionInfoEditorVM(p_optOption, p_lstModFiles);

			InstallableFileEditorVM vmlInstallableFile = new InstallableFileEditorVM(null, p_lstModFiles);
			FileListEditorVM vmlFileList = new FileListEditorVM(vmlInstallableFile, p_optOption.Files);

			FO3CplConverter cvtConverter = new FO3CplConverter(ScriptType.GetCplParserFactory());
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
		public override NodeEditor GetConditionallyInstalledFileSetOrderEditor(IList<ConditionallyInstalledFileSet> p_lstConditionallyInstalledFileSets, IList<VirtualFileSystemItem> p_lstModFiles)
		{
			FO3CplConverter cvtConverter = new FO3CplConverter(ScriptType.GetCplParserFactory());
			ConditionallyInstalledFileSetOrderEditorVM vmlOrderEditor = new ConditionallyInstalledFileSetOrderEditorVM(p_lstConditionallyInstalledFileSets, cvtConverter);
			return new ConditionallyInstalledFileSetOrderEditor(vmlOrderEditor);
		}

		/// <summary>
		/// The editor to use to edit a <see cref="ConditionallyInstalledFileSet"/>.
		/// </summary>
		/// <param name="p_cipPattern">The <see cref="ConditionallyInstalledFileSet"/> to edit.</param>
		/// <param name="p_lstModFiles">The list of files in the mod to which the <see cref="XmlScript"/>
		/// being edited belongs.</param>
		/// <returns>The editor to use to edit an <see cref="ConditionallyInstalledFileSet"/>. <c>null</c> is returned if the
		/// current <see cref="XmlScript"/> does not support editing <see cref="ConditionallyInstalledFileSet"/>s.</returns>
		public override NodeEditor GetConditionallyInstalledFileSetEditor(ConditionallyInstalledFileSet p_cipPattern, IList<VirtualFileSystemItem> p_lstModFiles)
		{
			FO3CplConverter cvtConverter = new FO3CplConverter(ScriptType.GetCplParserFactory());
			ConditionEditorVM vmlConditionEditor = CreateConditionEditorVM(p_lstModFiles, cvtConverter);			

			InstallableFileEditorVM vmlInstallableFile = new InstallableFileEditorVM(null, p_lstModFiles);
			FileListEditorVM vmlFileList = new FileListEditorVM(vmlInstallableFile, p_cipPattern.Files);

			ConditionallyInstalledFileSetEditorVM vmlFileInstall = new ConditionallyInstalledFileSetEditorVM(vmlConditionEditor, vmlFileList, p_cipPattern);
			return new ConditionallyInstalledFileSetEditor(vmlFileInstall);
		}

		#endregion
	}
}
