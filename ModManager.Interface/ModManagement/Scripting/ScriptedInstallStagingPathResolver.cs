using System;
using System.IO;
using Nexus.Client.Games;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement.Scripting
{
	/// <summary>
	/// Resolves the physical staging location used by scripted file operations.
	/// </summary>
	public static class ScriptedInstallStagingPathResolver
	{
		/// <summary>
		/// Resolves the virtual or HD-link staging path for a logical scripted-install destination.
		/// </summary>
		/// <param name="p_modMod">The mod being installed.</param>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		/// <param name="p_ivaVirtualModActivator">The virtual mod activator providing staging roots.</param>
		/// <param name="p_strDestinationPath">The logical destination path of the file.</param>
		/// <param name="p_booIgnoreSentinelDownloadId">Whether the legacy <c>-1</c> download identifier should fall back to filename-based staging.</param>
		/// <returns>The physical path used to stage the scripted file.</returns>
		public static string GetStagingPath(IMod p_modMod, IGameMode p_gmdGameMode, IVirtualModActivator p_ivaVirtualModActivator, string p_strDestinationPath, bool p_booIgnoreSentinelDownloadId)
		{
			if (p_modMod == null)
				throw new ArgumentNullException(nameof(p_modMod));
			if (p_gmdGameMode == null)
				throw new ArgumentNullException(nameof(p_gmdGameMode));
			if (p_ivaVirtualModActivator == null)
				throw new ArgumentNullException(nameof(p_ivaVirtualModActivator));

			return GetStagingPathCore(p_modMod, p_gmdGameMode, p_ivaVirtualModActivator, p_strDestinationPath, p_booIgnoreSentinelDownloadId);
		}

		/// <summary>
		/// Resolves the native Virtual staging location for a logical install destination and an explicit install root.
		/// </summary>
		/// <remarks>
		/// Data-root installs must apply the game mode's VirtualStorage path projection before choosing the staging file.
		/// Game-root installs already carry game-root-relative destinations and therefore bypass that projection.
		/// </remarks>
		public static string GetStagingPath(IMod p_modMod, IGameMode p_gmdGameMode, IVirtualModActivator p_ivaVirtualModActivator,
			string p_strDestinationPath, ModInstallRoot p_mirInstallRoot, bool p_booIgnoreSentinelDownloadId)
		{
			if (p_modMod == null)
				throw new ArgumentNullException(nameof(p_modMod));
			if (p_gmdGameMode == null)
				throw new ArgumentNullException(nameof(p_gmdGameMode));
			if (p_ivaVirtualModActivator == null)
				throw new ArgumentNullException(nameof(p_ivaVirtualModActivator));

			string strVirtualStoragePath = Nexus.Client.ModManagement.BasicInstallPlanBuilder.GetAdjustedPath(
				p_gmdGameMode, p_modMod, p_mirInstallRoot, p_strDestinationPath, ModPathContext.VirtualStorage);

			if (String.IsNullOrWhiteSpace(strVirtualStoragePath))
				throw new InvalidDataException(String.Format(
					"The logical destination '{0}' does not resolve to a usable Virtual staging path.", p_strDestinationPath));

			return GetStagingPathCore(p_modMod, p_gmdGameMode, p_ivaVirtualModActivator, strVirtualStoragePath, p_booIgnoreSentinelDownloadId);
		}

		private static string GetStagingPathCore(IMod p_modMod, IGameMode p_gmdGameMode, IVirtualModActivator p_ivaVirtualModActivator,
			string p_strDestinationPath, bool p_booIgnoreSentinelDownloadId)
		{
			string strFileType = Path.GetExtension(p_strDestinationPath);
			if (!strFileType.StartsWith("."))
				strFileType = "." + strFileType;

			bool booHardLinkFile = p_ivaVirtualModActivator.MultiHDMode &&
				(p_gmdGameMode.HardlinkRequiredFilesType(p_strDestinationPath) ||
				 strFileType.Equals(".exe", StringComparison.InvariantCultureIgnoreCase) ||
				 strFileType.Equals(".jar", StringComparison.InvariantCultureIgnoreCase));
			string strStagingRoot = booHardLinkFile ? p_ivaVirtualModActivator.HDLinkFolder : p_ivaVirtualModActivator.VirtualPath;
			string strFilenamePath = Path.Combine(strStagingRoot, Path.GetFileNameWithoutExtension(p_modMod.Filename), p_strDestinationPath);

			bool booUseDownloadId = !String.IsNullOrWhiteSpace(p_modMod.DownloadId) && (p_modMod.DownloadId.Length > 1) &&
				(!p_booIgnoreSentinelDownloadId || !p_modMod.DownloadId.Equals("-1", StringComparison.OrdinalIgnoreCase));
			return booUseDownloadId ? Path.Combine(strStagingRoot, p_modMod.DownloadId, p_strDestinationPath) : strFilenamePath;
		}
	}
}
