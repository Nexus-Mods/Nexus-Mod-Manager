namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Exposes data-file overwrite decisions separately from approved file mutations.
	/// </summary>
	public interface IModFileInstallDecisionSupport
	{
		/// <summary>
		/// Resolves whether an existing data file may be overwritten.
		/// </summary>
		/// <param name="p_strPath">The path relative to the installer's data root.</param>
		/// <returns><c>true</c> if the write is approved; otherwise, <c>false</c>.</returns>
		bool ResolveDataFileOverwrite(string p_strPath);

		/// <summary>
		/// Installs a file from the mod archive without performing overwrite-decision processing.
		/// </summary>
		/// <param name="p_strModFilePath">The path of the source file in the mod archive.</param>
		/// <param name="p_strInstallPath">The destination path relative to the installer's data root.</param>
		/// <returns><c>true</c> if the source file is written; otherwise, <c>false</c>.</returns>
		bool InstallFileFromModWithResolvedOverwrite(string p_strModFilePath, string p_strInstallPath);

		/// <summary>
		/// Installs a file after overwrite processing while explicitly controlling legacy plugin handling.
		/// </summary>
		/// <param name="p_strModFilePath">The path of the source file in the mod archive.</param>
		/// <param name="p_strInstallPath">The destination path relative to the installer's data root.</param>
		/// <param name="p_booHandlePlugin">Whether the deployed file should be handled as an automatically activated plugin.</param>
		/// <returns><c>true</c> if the source file is written; otherwise, <c>false</c>.</returns>
		bool InstallFileFromModWithResolvedOverwrite(string p_strModFilePath, string p_strInstallPath, bool p_booHandlePlugin);

		/// <summary>
		/// Writes generated data without performing overwrite-decision processing.
		/// </summary>
		/// <param name="p_strPath">The destination path relative to the installer's data root.</param>
		/// <param name="p_bteData">The data to write.</param>
		/// <returns><c>true</c> when the file is written.</returns>
		bool GenerateDataFileWithResolvedOverwrite(string p_strPath, byte[] p_bteData);
	}
}
