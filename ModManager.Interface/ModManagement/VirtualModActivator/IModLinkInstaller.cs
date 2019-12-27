namespace Nexus.Client.ModManagement
{
    using Nexus.Client.Mods;

    /// <summary>
    /// Interface for Mod Link installation.
    /// </summary>
	public interface IModLinkInstaller
	{
        /// <summary>
        /// Adds a file link.
        /// </summary>
        /// <param name="mod">Mod to activate.</param>
        /// <param name="baseFilePath">Base file path.</param>
        /// <param name="sourceFile">Source file.</param>
        /// <param name="isSwitching">Whether or not we're switching (?).</param>
        /// <returns>Link to file.</returns>
        string AddFileLink(IMod mod, string baseFilePath, string sourceFile, bool isSwitching);

        /// <summary>
        /// Adds a file link.
        /// </summary>
        /// <param name="mod">Mod to activate.</param>
        /// <param name="baseFilePath">Base file path.</param>
        /// <param name="sourceFile">Source file.</param>
        /// <param name="isSwitching">Whether or not we're switching (?).</param>
        /// <param name="handlePlugin">Whether or not we'll handle the plugin.</param>
        /// <returns>Link to file.</returns>
        string AddFileLink(IMod mod, string baseFilePath, string sourceFile, bool isSwitching, bool handlePlugin);
    }
}
