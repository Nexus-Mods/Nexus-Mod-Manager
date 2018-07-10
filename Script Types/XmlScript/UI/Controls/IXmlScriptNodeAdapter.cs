using System.Collections.Generic;
using Nexus.UI.Controls;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls
{
	/// <summary>
	/// Defines the methods required to adapt an <see cref="XmlScript"/> to an XML script editor.
	/// </summary>
	public interface IXmlScriptNodeAdapter
	{
		/// <summary>
		/// Gets the editor to use to edit <see cref="HeaderInfo"/>.
		/// </summary>
		/// <param name="p_hdrHeader">The header to edit.</param>
		/// <param name="p_lstModFiles">The list of files in the mod to which the <see cref="XmlScript"/>
		/// being edited belongs.</param>
		/// <returns>The editor to use to edit <see cref="HeaderInfo"/>. <c>null</c> is returned if the
		/// current <see cref="XmlScript"/> does not support editing the <see cref="HeaderInfo"/>.</returns>
		NodeEditor GetHeaderEditor(HeaderInfo p_hdrHeader, IList<VirtualFileSystemItem> p_lstModFiles);

		/// <summary>
		/// Gets the editor to use to edit the <see cref="XmlScript"/> prerequisites.
		/// </summary>
		/// <param name="p_xscScript">The <see cref="XmlScript"/> whose prerequisites are to be edited.</param>
		/// <param name="p_lstModFiles">The list of files in the mod to which the <see cref="XmlScript"/>
		/// being edited belongs.</param>
		/// <returns>The editor to use to edit <see cref="XmlScript"/> prerequisites. <c>null</c> is returned if the
		/// current <see cref="XmlScript"/> does not support editing the prerequisites.</returns>
		NodeEditor GetPrerequisitesEditor(XmlScript p_xscScript, IList<VirtualFileSystemItem> p_lstModFiles);

		/// <summary>
		/// Gets the editor to use to edit <see cref="XmlScript"/>'s required install files.
		/// </summary>
		/// <param name="p_lstFiles">The list of required install files to edit.</param>
		/// <param name="p_lstModFiles">The list of files in the mod to which the <see cref="XmlScript"/>
		/// being edited belongs.</param>
		/// <returns>The editor to use to edit <see cref="XmlScript"/>'s required install files. <c>null</c> is returned if the
		/// current <see cref="XmlScript"/> does not support editing the required install files.</returns>
		NodeEditor GetRequiredInstallFilesEditor(IList<InstallableFile> p_lstFiles, IList<VirtualFileSystemItem> p_lstModFiles);
		
		/// <summary>
		/// Gets the editor to use to edit the <see cref="XmlScript"/>'s install step order.
		/// </summary>
		/// <param name="p_xscScript">The <see cref="XmlScript"/> whose install step order is to be edited.</param>
		/// <param name="p_lstModFiles">The list of files in the mod to which the <see cref="XmlScript"/>
		/// being edited belongs.</param>
		/// <returns>The editor to use to edit the <see cref="XmlScript"/>'s install step order. <c>null</c> is returned if the
		/// current <see cref="XmlScript"/> does not support editing the install step order.</returns>
		NodeEditor GetInstallStepOrderEditor(XmlScript p_xscScript, IList<VirtualFileSystemItem> p_lstModFiles);

		/// <summary>
		/// Gets the editor to use to edit an <see cref="InstallStep"/>.
		/// </summary>
		/// <param name="p_ispStep">The <see cref="InstallStep"/> to edit.</param>
		/// <param name="p_lstModFiles">The list of files in the mod to which the <see cref="XmlScript"/>
		/// being edited belongs.</param>
		/// <returns>The editor to use to edit an <see cref="InstallStep"/>. <c>null</c> is returned if the
		/// current <see cref="XmlScript"/> does not support editing <see cref="InstallStep"/>s.</returns>
		NodeEditor GetInstallStepEditor(InstallStep p_ispStep, IList<VirtualFileSystemItem> p_lstModFiles);

		/// <summary>
		/// The editor to use to edit an <see cref="OptionGroup"/>.
		/// </summary>
		/// <param name="p_opgGroup">The <see cref="OptionGroup"/> to edit.</param>
		/// <param name="p_lstModFiles">The list of files in the mod to which the <see cref="XmlScript"/>
		/// being edited belongs.</param>
		/// <returns>The editor to use to edit an <see cref="OptionGroup"/>. <c>null</c> is returned if the
		/// current <see cref="XmlScript"/> does not support editing <see cref="OptionGroup"/>s.</returns>
		NodeEditor GetOptionGroupEditor(OptionGroup p_opgGroup, IList<VirtualFileSystemItem> p_lstModFiles);

		/// <summary>
		/// The editor to use to edit an <see cref="Option"/>.
		/// </summary>
		/// <param name="p_optOption">The <see cref="Option"/> to edit.</param>
		/// <param name="p_lstModFiles">The list of files in the mod to which the <see cref="XmlScript"/>
		/// being edited belongs.</param>
		/// <returns>The editor to use to edit an <see cref="Option"/>. <c>null</c> is returned if the
		/// current <see cref="XmlScript"/> does not support editing <see cref="Option"/>s.</returns>
		NodeEditor GetOptionEditor(Option p_optOption, IList<VirtualFileSystemItem> p_lstModFiles);

		/// <summary>
		/// Gets the editor to use to edit the <see cref="XmlScript"/>'s conditionally installed file set order.
		/// </summary>
		/// <param name="p_lstConditionallyInstalledFileSets">The conditionally installed file sets whose order is to be edited.</param>
		/// <param name="p_lstModFiles">The list of files in the mod to which the <see cref="XmlScript"/>
		/// being edited belongs.</param>
		/// <returns>The editor to use to the the <see cref="XmlScript"/>'s conditionally installed file set order. <c>null</c> is returned if the
		/// current <see cref="XmlScript"/> does not support editing the conditionally installed file set order.</returns>
		NodeEditor GetConditionallyInstalledFileSetOrderEditor(IList<ConditionallyInstalledFileSet> p_lstConditionallyInstalledFileSets, IList<VirtualFileSystemItem> p_lstModFiles);

		/// <summary>
		/// The editor to use to edit a <see cref="ConditionallyInstalledFileSet"/>.
		/// </summary>
		/// <param name="p_cisFileSet">The <see cref="ConditionallyInstalledFileSet"/> to edit.</param>
		/// <param name="p_lstModFiles">The list of files in the mod to which the <see cref="XmlScript"/>
		/// being edited belongs.</param>
		/// <returns>The editor to use to edit an <see cref="ConditionallyInstalledFileSet"/>. <c>null</c> is returned if the
		/// current <see cref="XmlScript"/> does not support editing <see cref="ConditionallyInstalledFileSet"/>s.</returns>
		NodeEditor GetConditionallyInstalledFileSetEditor(ConditionallyInstalledFileSet p_cisFileSet, IList<VirtualFileSystemItem> p_lstModFiles);
	}
}
