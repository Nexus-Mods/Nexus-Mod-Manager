using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Performs a standard mod installation.
	/// </summary>
	/// <remarks>
	/// A basic install installs all of the files in the mod to the installation directory,
	/// and activates all plugin files.
	/// </remarks>
	public class BasicInstallTask : ThreadedBackgroundTask
	{
		#region Properties

		/// <summary>
		/// Gets or sets the mod being installed.
		/// </summary>
		/// <value>The mod being installed.</value>
		protected IMod Mod { get; set; }

		/// <summary>
		/// Gets the current game mode.
		/// </summary>
		/// <value>The the current game mode.</value>
		protected IGameMode GameMode { get; private set; }

		/// <summary>
		/// Gets or sets the installer to use to install files.
		/// </summary>
		/// <value>The installer to use to install files.</value>
		protected IModFileInstaller FileInstaller { get; set; }

		/// <summary>
		/// Gets the manager to use to manage plugins.
		/// </summary>
		/// <value>The manager to use to manage plugins.</value>
		protected IPluginManager PluginManager { get; private set; }

		/// <summary>
		/// Gets the virtual mod activator to use.
		/// </summary>
		/// <value>The virtual mod activator to use.</value>
		protected IVirtualModActivator VirtualModActivator { get; private set; }

		/// <summary>
		/// Gets or sets whether the installer should skip readme files.
		/// </summary>
		/// <value>Whether the installer should skip readme files.</value>
		protected bool SkipReadme { get; set; }

		/// <summary>
		/// Gets the list of currently active mods.
		/// </summary>
		/// <value>The list of currently active mods.</value>
		protected ReadOnlyObservableList<IMod> ActiveMods { get; private set; }

		/// <summary>
		/// Gets the optional list of files to install.
		/// </summary>
		/// <value>The optional list of files to install.</value>
		protected List<KeyValuePair<string, string>> FilesToInstall { get; private set; }
		protected ModInstallRoot InstallRoot { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_modMod">The mod being installed.</param>
		/// <param name="p_gmdGameMode">The the current game mode.</param>
		/// <param name="p_mfiFileInstaller">The file installer to use.</param>
		/// <param name="p_pmgPluginManager">The plugin manager.</param>
		/// <param name="p_booSkipReadme">Whether to skip the installation of readme files.</param>
		/// <param name="p_rolActiveMods">The list of active mods.</param>
		/// <param name="p_lstInstallFiles">The list of specific files to install, if null the mod will be installed as usual.</param>
		public BasicInstallTask(IMod p_modMod, IGameMode p_gmdGameMode, IModFileInstaller p_mfiFileInstaller, IPluginManager p_pmgPluginManager, IVirtualModActivator p_ivaVirtualModActivator, bool p_booSkipReadme, ReadOnlyObservableList<IMod> p_rolActiveMods, List<KeyValuePair<string, string>> p_dicInstallFiles)
			: this(p_modMod, p_gmdGameMode, p_mfiFileInstaller, p_pmgPluginManager, p_ivaVirtualModActivator, p_booSkipReadme, p_rolActiveMods, p_dicInstallFiles, ModInstallRoot.Default)
		{
		}

		public BasicInstallTask(IMod p_modMod, IGameMode p_gmdGameMode, IModFileInstaller p_mfiFileInstaller, IPluginManager p_pmgPluginManager, IVirtualModActivator p_ivaVirtualModActivator, bool p_booSkipReadme, ReadOnlyObservableList<IMod> p_rolActiveMods, List<KeyValuePair<string, string>> p_dicInstallFiles, ModInstallRoot p_mirInstallRoot)
		{
			Mod = p_modMod;
			GameMode = p_gmdGameMode;
			FileInstaller = p_mfiFileInstaller;
			PluginManager = p_pmgPluginManager;
			VirtualModActivator = p_ivaVirtualModActivator;
			SkipReadme = p_booSkipReadme;
			ActiveMods = p_rolActiveMods;
			FilesToInstall = p_dicInstallFiles;
			InstallRoot = p_mirInstallRoot;
		}

		#endregion

		/// <summary>
		/// Runs the basic install task.
		/// </summary>
		/// <returns><c>true</c> if the installation succeed;
		/// <c>false</c> otherwise.</returns>
		public bool Execute()
		{
			OverallMessage = "Installing Mod...";
			ShowItemProgress = false;
			OverallProgressStepSize = 1;
			return (bool)StartWait();
		}

		/// <summary>
		/// The method that is called to start the backgound task.
		/// </summary>
		/// <remarks>
		/// This method installs all of the files in the <see cref="IMod"/> being installed.
		/// </remarks>
		/// <param name="args">Arguments to for the task execution.</param>
		/// <returns>A return value.</returns>
		protected override object DoWork(object[] args)
		{
			IModLinkInstaller ModLinkInstaller = VirtualModActivator.GetModLinkInstaller();
			char[] chrDirectorySeperators = new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
			List<KeyValuePair<string, string>> lstFiles = (FilesToInstall == null) ? Mod.GetFileList().Select(x => new KeyValuePair<string, string>(x, null)).ToList() : FilesToInstall;
			List<KeyValuePair<string, string>> lstFilesToLink = new List<KeyValuePair<string, string>>(lstFiles.Count);
			OverallProgressMaximum = lstFiles.Count * 2;

			if (GameMode.RequiresSpecialFileInstallation && GameMode.IsSpecialFile(Mod.GetFileList()))
			{
				List<KeyValuePair<string, string>> specialFiles = GameMode.SpecialFileInstall(Mod)?.Select(x => new KeyValuePair<string, string>(x, null)).ToList();

				if (specialFiles != null)
					lstFiles = specialFiles;
			}

			if (InstallRoot == ModInstallRoot.GameRoot)
				lstFiles = StripGameRootWrapperFolder(lstFiles);

            if (GameMode.RequiresModFileMerge)
				GameMode.ModFileMerge(ActiveMods, Mod, false);

			foreach (KeyValuePair<string, string> File in lstFiles)
			{
				string strFileTo = File.Value;
				if (string.IsNullOrWhiteSpace(strFileTo))
					strFileTo = File.Key;


				if (Status == TaskStatus.Cancelling)
					return false;
				string strFixedPath = GetAdjustedPath(strFileTo, ModPathContext.GameInstall);
				if (string.IsNullOrEmpty(strFixedPath))
					continue;

				if (InstallRoot == ModInstallRoot.GameRoot)
					strFileTo = strFixedPath;

				string strVirtualStoragePath = GetAdjustedPath(strFileTo, ModPathContext.VirtualStorage);
				string strModFilenamePath = Path.Combine(VirtualModActivator.VirtualPath, Path.GetFileNameWithoutExtension(Mod.Filename).Trim(), strVirtualStoragePath);
				string strModDownloadIDPath = (string.IsNullOrWhiteSpace(Mod.DownloadId) || (Mod.DownloadId.Length <= 1) || Mod.DownloadId.Equals("-1", StringComparison.OrdinalIgnoreCase)) ? string.Empty : Path.Combine(VirtualModActivator.VirtualPath, Mod.DownloadId, strVirtualStoragePath);
				string strVirtualPath = strModFilenamePath;

				if (!string.IsNullOrWhiteSpace(strModDownloadIDPath))
					strVirtualPath = strModDownloadIDPath;

				string strLinkPath = string.Empty;
				if (VirtualModActivator.MultiHDMode)
				{
					string strModFilenameLink = Path.Combine(VirtualModActivator.HDLinkFolder, Path.GetFileNameWithoutExtension(Mod.Filename).Trim(), strVirtualStoragePath);
					string strModDownloadIDLink = (string.IsNullOrWhiteSpace(Mod.DownloadId) || (Mod.DownloadId.Length <= 1) || Mod.DownloadId.Equals("-1", StringComparison.OrdinalIgnoreCase)) ? string.Empty : Path.Combine(VirtualModActivator.HDLinkFolder, Mod.DownloadId, strVirtualStoragePath);
					 strLinkPath = strModFilenameLink;

					if (!string.IsNullOrWhiteSpace(strModDownloadIDLink))
						strLinkPath = strModDownloadIDLink;
				}

				string strFileType = Path.GetExtension(File.Key);
				if (!strFileType.StartsWith("."))
					strFileType = "." + strFileType;
				bool booHardLinkFile = (VirtualModActivator.MultiHDMode && (GameMode.HardlinkRequiredFilesType(File.Key) || strFileType.Equals(".exe", StringComparison.InvariantCultureIgnoreCase) || strFileType.Equals(".jar", StringComparison.InvariantCultureIgnoreCase)));

				if (!string.IsNullOrEmpty(strFixedPath))
				{
					if (!(GameMode.RequiresModFileMerge && (Path.GetFileName(File.Key) == GameMode.MergedFileName)))
					{
						if (!(SkipReadme && Readme.IsValidExtension(Path.GetExtension(File.Key).ToLower()) && Path.GetDirectoryName(strFixedPath).Equals(Path.GetFileName(GameMode.PluginDirectory), StringComparison.CurrentCultureIgnoreCase)))
						{
							FileInstaller.InstallFileFromMod(File.Key, ((booHardLinkFile) ? strLinkPath : strVirtualPath));
							lstFilesToLink.Add(new KeyValuePair<string, string>(strFileTo, (booHardLinkFile) ? strLinkPath : strVirtualPath));
						}
					}
				}
				StepOverallProgress();
			}

			if ((lstFiles.Count > 0) && (lstFilesToLink.Count <= 0))
				throw new InvalidDataException(string.Format("This mod does not have the correct file structure for a {0} mod that NMM can use. It will not work with NMM.", GameMode.Name));

			List<string> deployedPluginPaths = new List<string>();

			using (VirtualModActivator.BeginModInfoUpdateBatch())
			using (VirtualModActivator.BeginVirtualLinkUpdateBatch(lstFilesToLink.Count))
			{
				foreach (KeyValuePair<string, string> strLink in lstFilesToLink)
				{
					if (!VirtualModActivator.DisableLinkCreation)
					{
						string strFileLink = ModLinkInstaller.AddFileLink(Mod, strLink.Key, strLink.Value, false, false, InstallRoot);

						if (!string.IsNullOrEmpty(strFileLink) &&
							PluginManager != null &&
							PluginManager.IsActivatiblePluginFile(strFileLink))
						{
							deployedPluginPaths.Add(strFileLink);
						}
					}
					StepOverallProgress();
				}
			}

			if (PluginManager != null && deployedPluginPaths.Count > 0)
				PluginManager.IntegrateDeployedPlugins(deployedPluginPaths);

			VirtualModActivator.SaveList();
			return true;
		}


		/// <summary>
		/// If valid the current plugin file will be set as active.
		/// </summary>
		private List<KeyValuePair<string, string>> StripGameRootWrapperFolder(List<KeyValuePair<string, string>> files)
		{
			if (files == null || files.Count == 0)
				return files;

			string commonTopFolder = null;
			foreach (KeyValuePair<string, string> file in files)
			{
				string sourcePath = file.Key;
				if (IsUnsafeGameRootArchivePath(sourcePath))
					throw new InvalidDataException(string.Format("Game-root install path '{0}' cannot be installed safely.", sourcePath));

				string topFolder = GetTopLevelFolder(sourcePath);
				if (string.IsNullOrEmpty(topFolder))
					return NormalizeGameRootFileMappings(files, false);

				if (commonTopFolder == null)
					commonTopFolder = topFolder;
				else if (!commonTopFolder.Equals(topFolder, StringComparison.OrdinalIgnoreCase))
					return NormalizeGameRootFileMappings(files, false);
			}

			bool hasRecognizableRootContent = files.Any(x => IsRecognizableGameRootContent(StripTopLevelFolder(x.Key)));
			return NormalizeGameRootFileMappings(files, hasRecognizableRootContent);
		}

		private static List<KeyValuePair<string, string>> NormalizeGameRootFileMappings(List<KeyValuePair<string, string>> files, bool stripCommonWrapper)
		{
			return files.Select(x =>
			{
				string destination = stripCommonWrapper ? StripTopLevelFolder(x.Key) : x.Key;
				return new KeyValuePair<string, string>(x.Key, NormalizeGameRootRelativePath(destination));
			}).ToList();
		}

		private static string GetTopLevelFolder(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				return null;

			string normalizedPath = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
			int separatorIndex = normalizedPath.IndexOf(Path.DirectorySeparatorChar);
			return separatorIndex <= 0 ? null : normalizedPath.Substring(0, separatorIndex);
		}

		private static string StripTopLevelFolder(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				return path;

			string normalizedPath = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
			int separatorIndex = normalizedPath.IndexOf(Path.DirectorySeparatorChar);
			return separatorIndex < 0 || separatorIndex + 1 >= normalizedPath.Length ? normalizedPath : normalizedPath.Substring(separatorIndex + 1);
		}

		private static bool IsRecognizableGameRootContent(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				return false;

			string normalizedPath = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
			if (normalizedPath.Equals("Data", StringComparison.OrdinalIgnoreCase) || normalizedPath.StartsWith("Data" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
				return true;

			string fileName = Path.GetFileName(normalizedPath);
			return fileName.Equals("skse64_loader.exe", StringComparison.OrdinalIgnoreCase) ||
				(fileName.StartsWith("skse64_", StringComparison.OrdinalIgnoreCase) && fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
		}

		private static bool IsUnsafeGameRootArchivePath(string path)
		{
			if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
				return true;

			string normalizedPath = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			return normalizedPath.Split(Path.DirectorySeparatorChar).Any(x => x == "..");
		}

		private static string NormalizeGameRootRelativePath(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				return string.Empty;

			if (Path.IsPathRooted(path))
				throw new InvalidDataException(string.Format("Game-root install path '{0}' is rooted and cannot be installed safely.", path));

			List<string> pathParts = new List<string>();
			foreach (string part in path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar))
			{
				if (string.IsNullOrEmpty(part) || part == ".")
					continue;

				if (part == "..")
					throw new InvalidDataException(string.Format("Game-root install path '{0}' escapes the selected game root.", path));

				pathParts.Add(part);
			}

			return string.Join(Path.DirectorySeparatorChar.ToString(), pathParts.ToArray());
		}

		private string GetAdjustedPath(string path, ModPathContext context)
		{
			if (InstallRoot == ModInstallRoot.GameRoot)
				return NormalizeGameRootRelativePath(path);

			if (context == ModPathContext.GameInstall)
				return GameMode.GetModFormatAdjustedPath(Mod.Format, path, Mod, context);

			return GameMode.GetModFormatAdjustedPath(Mod.Format, path, context);
		}

		protected void ActivatePlugin(string p_strPlugin)
		{
			if (FileInstaller.PluginCheck(p_strPlugin, false))
				if (PluginManager.IsActivatiblePluginFile(p_strPlugin))
					PluginManager.ActivatePlugin(p_strPlugin);
		}
	}
}
