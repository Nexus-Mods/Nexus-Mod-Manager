using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Exposes virtual-link conflict decisions separately from link deployment.
	/// </summary>
	public interface IModLinkInstallDecisionSupport
	{
		/// <summary>
		/// Resolves any user-visible overwrite choice required for the specified virtual link without deploying it.
		/// </summary>
		/// <param name="p_modMod">The mod whose file will be linked.</param>
		/// <param name="p_strBaseFilePath">The logical destination path of the file.</param>
		/// <param name="p_mirInstallRoot">The installation root containing the destination.</param>
		/// <returns>The resolved overwrite choice, or an unresolved decision when no prompt was required.</returns>
		ModLinkInstallDecision ResolveFileLinkDecision(IMod p_modMod, string p_strBaseFilePath, ModInstallRoot p_mirInstallRoot);

		/// <summary>
		/// Deploys a virtual link using an overwrite choice that was resolved during planning.
		/// </summary>
		/// <param name="p_modMod">The mod whose file will be linked.</param>
		/// <param name="p_strBaseFilePath">The logical destination path of the file.</param>
		/// <param name="p_strSourceFile">The staged source path.</param>
		/// <param name="p_booIsSwitching">Whether the operation is part of a mod switch.</param>
		/// <param name="p_booHandlePlugin">Whether plugin handling should be performed.</param>
		/// <param name="p_mirInstallRoot">The installation root containing the destination.</param>
		/// <param name="p_midDecision">The overwrite choice resolved before deployment.</param>
		/// <returns>The linked file path, or an empty string when the incoming file is not activated.</returns>
		string AddFileLinkWithResolvedDecision(IMod p_modMod, string p_strBaseFilePath, string p_strSourceFile, bool p_booIsSwitching, bool p_booHandlePlugin, ModInstallRoot p_mirInstallRoot, ModLinkInstallDecision p_midDecision);
	}
}
