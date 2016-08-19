using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ChinhDo.Transactions;
using Nexus.Client.Games;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// This installs mod files when a mod is being upgraded.
	/// </summary>
	/// <remarks>
	/// This differs from the regular <see cref="ModFileInstaller"/> in that
	/// installed files are installed overtop of their current location. If a file being
	/// installed has been previously installed, this installer will write the file to
	/// the same location. Thus, if the file is in the overwrite folder, this installer
	/// will write the file to the overwrite folder; if the file is in the main
	/// installation folder, this installer will write the file in the main
	/// installation folder. If a file being installed has not been previously
	/// installed, it is installed as usual.
	/// </remarks>
	public class ModFileUpgradeInstaller : ModFileInstaller
	{
		#region Properties

		/// <summary>
		/// Gets the list of files that were already installed by the current mod
		/// before the upgrade, but not yet reinstalled during the upgrade.
		/// </summary>
		protected Set<string> OriginallyInstalledFiles { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_gmiGameModeInfo">The environment info of the current game mode.</param>
		/// <param name="p_modMod">The mod being installed.</param>
		/// <param name="p_ilgInstallLog">The install log to use to log file installations.</param>
		/// <param name="p_pmgPluginManager">The plugin manager.</param>
		/// <param name="p_dfuDataFileUtility">The utility class to use to work with data files.</param>
		/// <param name="p_tfmFileManager">The transactional file manager to use to interact with the file system.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		/// <param name="p_UsesPlugins">Game using plugin or mods (True for plugins).</param>
		public ModFileUpgradeInstaller(IGameModeEnvironmentInfo p_gmiGameModeInfo, IMod p_modMod, IInstallLog p_ilgInstallLog, IPluginManager p_pmgPluginManager, IDataFileUtil p_dfuDataFileUtility, TxFileManager p_tfmFileManager, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate, bool p_UsesPlugins)
			:base(p_gmiGameModeInfo, p_modMod, p_ilgInstallLog, p_pmgPluginManager, p_dfuDataFileUtility, p_tfmFileManager, p_dlgOverwriteConfirmationDelegate, p_UsesPlugins)
		{
			OriginallyInstalledFiles = new Set<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string strFile in InstallLog.GetInstalledModFiles(Mod))
				OriginallyInstalledFiles.Add(strFile.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
		}

		#endregion

		/// <summary>
		/// Writes the file represented by the given byte array to the given path.
		/// </summary>
		/// <remarks>
		/// This method writes the given data as a file at the given path, if it is owned
		/// by the mod being upgraded. If the specified data file is not owned by the mod
		/// being upgraded, the file is instead written to the overwrites directory.
		/// 
		/// If the file was not previously installed by the mod, then the normal install rules apply,
		/// including confirming overwrite if applicable.
		/// </remarks>
		/// <param name="p_strPath">The path where the file is to be created.</param>
		/// <param name="p_bteData">The data that is to make up the file.</param>
		/// <returns><c>true</c> if the file was written; <c>false</c> if the user chose
		/// not to overwrite an existing file.</returns>
		/// <exception cref="IllegalFilePathException">Thrown if <paramref name="p_strPath"/> is
		/// not safe.</exception>
		public override bool GenerateDataFile(string p_strPath, byte[] p_bteData)
		{
			DataFileUtility.AssertFilePathIsSafe(p_strPath);
			string strInstallFilePath = Path.Combine(GameModeInfo.InstallationPath, p_strPath);

			IList<IMod> lstInstallers = InstallLog.GetFileInstallers(p_strPath);
			if (lstInstallers.Contains(Mod, ModComparer.Filename))
			{
				string strWritePath = null;
				if (!ModComparer.Filename.Equals(lstInstallers[lstInstallers.Count - 1], Mod))
				{
					string strDirectory = Path.GetDirectoryName(p_strPath);
					string strBackupPath = Path.Combine(GameModeInfo.OverwriteDirectory, strDirectory);
					string strOldModKey = InstallLog.GetModKey(Mod);
					string strFile = strOldModKey + "_" + Path.GetFileName(p_strPath);
					strWritePath = Path.Combine(strBackupPath, strFile);
				}
				else
					strWritePath = strInstallFilePath;
				TransactionalFileManager.WriteAllBytes(strWritePath, p_bteData);
				OriginallyInstalledFiles.Remove(p_strPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
				return true;
			}

			return base.GenerateDataFile(p_strPath, p_bteData);
		}

		/// <summary>
		/// Finalizes the installation of the files.
		/// </summary>
		/// <remarks>
		/// This removes all of the file that weren't reinstalled during the upgrade.
		/// </remarks>
		public override void FinalizeInstall()
		{
			foreach (string strFile in OriginallyInstalledFiles)
				UninstallDataFile(strFile);
		}
	}
}
