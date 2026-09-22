using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ChinhDo.Transactions;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.Util.Collections;
using Nexus.Client.Util.Localization;

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
		private readonly HashSet<string> m_hstDeployedPluginPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
		protected ModInstallContext InstallContext { get; private set; }
		protected ModInstallRoot InstallRoot => InstallContext.InstallRoot;
		protected IModDeploymentManager DeploymentManager { get; private set; }
		protected TxFileManager FileManager { get; private set; }
		protected ModDeploymentOverwriteResolver OverwriteResolver { get; private set; }

		/// <summary>
		/// Gets whether this task used the promoted deployment path.
		/// </summary>
		public bool UsedPromotedDeployment { get; private set; }

		/// <summary>
		/// Gets the plugin paths successfully deployed by this basic install.
		/// </summary>
		public IList<string> DeployedPluginPaths
		{
			get { return new List<string>(m_hstDeployedPluginPaths).AsReadOnly(); }
		}

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
			: this(p_modMod, p_gmdGameMode, p_mfiFileInstaller, p_pmgPluginManager, p_ivaVirtualModActivator, p_booSkipReadme, p_rolActiveMods, p_dicInstallFiles, new ModInstallContext(ModInstallMethod.Virtual, p_mirInstallRoot))
		{
		}

		/// <summary>
		/// Initializes a basic install task with the immutable deployment context captured for the operation.
		/// </summary>
		public BasicInstallTask(IMod p_modMod, IGameMode p_gmdGameMode, IModFileInstaller p_mfiFileInstaller, IPluginManager p_pmgPluginManager, IVirtualModActivator p_ivaVirtualModActivator, bool p_booSkipReadme, ReadOnlyObservableList<IMod> p_rolActiveMods, List<KeyValuePair<string, string>> p_dicInstallFiles, ModInstallContext p_micInstallContext)
			: this(p_modMod, p_gmdGameMode, p_mfiFileInstaller, p_pmgPluginManager, p_ivaVirtualModActivator,
				p_booSkipReadme, p_rolActiveMods, p_dicInstallFiles, p_micInstallContext, null, null, null)
		{
		}

		/// <summary>
		/// Initializes a basic install task with optional promoted-deployment services.
		/// </summary>
		public BasicInstallTask(IMod p_modMod, IGameMode p_gmdGameMode, IModFileInstaller p_mfiFileInstaller,
			IPluginManager p_pmgPluginManager, IVirtualModActivator p_ivaVirtualModActivator, bool p_booSkipReadme,
			ReadOnlyObservableList<IMod> p_rolActiveMods, List<KeyValuePair<string, string>> p_dicInstallFiles,
			ModInstallContext p_micInstallContext, IModDeploymentManager p_mdmDeploymentManager,
			TxFileManager p_tfmFileManager, ModDeploymentOverwriteResolver p_dorOverwriteResolver)
		{
			Mod = p_modMod;
			GameMode = p_gmdGameMode;
			FileInstaller = p_mfiFileInstaller;
			PluginManager = p_pmgPluginManager;
			VirtualModActivator = p_ivaVirtualModActivator;
			SkipReadme = p_booSkipReadme;
			ActiveMods = p_rolActiveMods;
			FilesToInstall = p_dicInstallFiles;
			InstallContext = p_micInstallContext ?? throw new ArgumentNullException(nameof(p_micInstallContext));
			DeploymentManager = p_mdmDeploymentManager;
			FileManager = p_tfmFileManager;
			OverwriteResolver = p_dorOverwriteResolver;
		}

		#endregion

		/// <summary>
		/// Runs the basic install task.
		/// </summary>
		/// <returns><c>true</c> if the installation succeed;
		/// <c>false</c> otherwise.</returns>
		public bool Execute()
		{
			OverallMessage = LanguageManager.Get("Tasks.Mod.Installing", "Installing Mod...");
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
			if (InstallContext.Method == ModInstallMethod.Direct)
				return DoDirectWork();

			IModLinkInstaller ModLinkInstaller = VirtualModActivator.GetModLinkInstaller();
			char[] chrDirectorySeperators = new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
			List<KeyValuePair<string, string>> lstFiles = (FilesToInstall == null) ? Mod.GetFileList().Select(x => new KeyValuePair<string, string>(x, null)).ToList() : FilesToInstall;

			if (GameMode.RequiresSpecialFileInstallation && GameMode.IsSpecialFile(Mod.GetFileList()))
			{
				List<KeyValuePair<string, string>> specialFiles = GameMode.SpecialFileInstall(Mod)?.Select(x => new KeyValuePair<string, string>(x, null)).ToList();

				if (specialFiles != null)
					lstFiles = specialFiles;
			}

			if (InstallRoot == ModInstallRoot.GameRoot)
				lstFiles = StripGameRootWrapperFolder(lstFiles);

			lstFiles = lstFiles.Where(x => !ModInstallFileFilter.IsIgnored(x.Key) && !ModInstallFileFilter.IsIgnored(x.Value)).ToList();
			List<KeyValuePair<string, string>> lstFilesToLink = new List<KeyValuePair<string, string>>(lstFiles.Count);
			OverallProgressMaximum = lstFiles.Count * 2;

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
						bool booSkipReadme = BasicInstallPlanBuilder.ShouldSkipReadme(
							SkipReadme, File.Key, strFixedPath, GameMode.PluginDirectory);

						if (!booSkipReadme)
						{
							string strStagedFilePath = (booHardLinkFile) ? strLinkPath : strVirtualPath;
							bool booFileInstalled = FileInstaller.InstallFileFromMod(File.Key, strStagedFilePath);

							// A false result can legitimately mean that the user kept an existing staged file.
							// It must never be treated as success when no staged file actually exists (for example,
							// when the archive stream could not be opened).
							if (!booFileInstalled && !System.IO.File.Exists(strStagedFilePath))
								throw new IOException(string.Format("Failed to stage mod file '{0}' at '{1}'.", File.Key, strStagedFilePath));

							lstFilesToLink.Add(new KeyValuePair<string, string>(strFileTo, strStagedFilePath));
						}
					}
				}
				StepOverallProgress();
			}

			if ((lstFiles.Count > 0) && (lstFilesToLink.Count <= 0))
				throw new InvalidDataException(string.Format("This mod does not have the correct file structure for a {0} mod that NMM can use. It will not work with NMM.", GameMode.Name));

			List<string> deployedPluginPaths = new List<string>();
			bool checkPromotedTargets = DeploymentManager != null && DeploymentManager.HasPromotedTargets;

			if (VirtualModActivator.DisableLinkCreation && lstFilesToLink.Count > 0)
				throw new InvalidOperationException("Mod file deployment is currently disabled. The installation cannot complete safely.");

			using (new VirtualModDeploymentBatch(VirtualModActivator, lstFilesToLink.Count))
			{
				foreach (KeyValuePair<string, string> strLink in lstFilesToLink)
				{
					if (!VirtualModActivator.DisableLinkCreation)
					{
						string strFileLink;
						ModDeploymentTarget target = checkPromotedTargets
							? ModDeploymentTargetResolver.Resolve(GameMode, Mod, strLink.Key, InstallRoot)
							: null;
						if (target != null && DeploymentManager.IsPromoted(target))
						{
							if (FileManager == null || OverwriteResolver == null)
								throw new InvalidOperationException("Promoted Virtual deployment requires transactional deployment services.");

							bool activate = OverwriteResolver.ShouldActivate(target);
							UsedPromotedDeployment = true;
							strFileLink = DeploymentManager.InstallVirtualFile(
								Mod,
								target,
								strLink.Key,
								strLink.Value,
								InstallRoot,
								activate,
								FileManager);
						}
						else
						{
							strFileLink = ModLinkInstaller.AddFileLink(Mod, strLink.Key, strLink.Value, false, false, InstallRoot);
						}

						if (!string.IsNullOrEmpty(strFileLink) &&
							PluginManager != null &&
							PluginManager.IsActivatiblePluginFile(strFileLink))
						{
							deployedPluginPaths.Add(strFileLink);
							m_hstDeployedPluginPaths.Add(strFileLink);
						}
					}
					StepOverallProgress();
				}
			}

			if (PluginManager != null && deployedPluginPaths.Count > 0)
				PluginManager.IntegrateDeployedPlugins(deployedPluginPaths);

			if (!UsedPromotedDeployment)
				VirtualModActivator.SaveList();
			return true;
		}

		/// <summary>
		/// Executes the standalone Direct basic-install path without creating Virtual staging payloads.
		/// </summary>
		private bool DoDirectWork()
		{
			List<KeyValuePair<string, string>> files = (FilesToInstall == null)
				? Mod.GetFileList().Select(x => new KeyValuePair<string, string>(x, null)).ToList()
				: FilesToInstall;

			if (GameMode.RequiresSpecialFileInstallation && GameMode.IsSpecialFile(Mod.GetFileList()))
			{
				List<KeyValuePair<string, string>> specialFiles = GameMode.SpecialFileInstall(Mod)
					?.Select(x => new KeyValuePair<string, string>(x, null)).ToList();
				if (specialFiles != null)
					files = specialFiles;
			}

			if (InstallRoot == ModInstallRoot.GameRoot)
				files = StripGameRootWrapperFolder(files);

			files = files.Where(x => !ModInstallFileFilter.IsIgnored(x.Key) && !ModInstallFileFilter.IsIgnored(x.Value)).ToList();
			OverallProgressMaximum = files.Count;
			if (GameMode.RequiresModFileMerge)
				GameMode.ModFileMerge(ActiveMods, Mod, false);

			int eligibleFiles = 0;
			foreach (KeyValuePair<string, string> file in files)
			{
				if (Status == TaskStatus.Cancelling)
					return false;

				string destination = string.IsNullOrWhiteSpace(file.Value) ? file.Key : file.Value;
				string adjustedPath = GetAdjustedPath(destination, ModPathContext.GameInstall);
				if (string.IsNullOrEmpty(adjustedPath))
				{
					StepOverallProgress();
					continue;
				}
				if (InstallRoot == ModInstallRoot.GameRoot)
					destination = adjustedPath;

				if (!(GameMode.RequiresModFileMerge && Path.GetFileName(file.Key) == GameMode.MergedFileName))
				{
					bool skipReadme = BasicInstallPlanBuilder.ShouldSkipReadme(
						SkipReadme, file.Key, adjustedPath, GameMode.PluginDirectory);

					if (!skipReadme)
					{
						eligibleFiles++;
						bool installed = FileInstaller.InstallFileFromMod(file.Key, destination);
						if (installed)
						{
							ModDeploymentTarget target = ModDeploymentTargetResolver.Resolve(GameMode, Mod, destination, InstallRoot);
							string deployedPath = ModDeploymentTargetResolver.GetPhysicalPath(GameMode, target);
							if (PluginManager != null && PluginManager.IsActivatiblePluginFile(deployedPath))
								m_hstDeployedPluginPaths.Add(deployedPath);
						}
					}
				}
				StepOverallProgress();
			}

			if (files.Count > 0 && eligibleFiles == 0)
				throw new InvalidDataException(string.Format("This mod does not have the correct file structure for a {0} mod that NMM can use. It will not work with NMM.", GameMode.Name));

			return true;
		}


		/// <summary>
		/// If valid the current plugin file will be set as active.
		/// </summary>
		private List<KeyValuePair<string, string>> StripGameRootWrapperFolder(List<KeyValuePair<string, string>> files)
		{
			return BasicInstallPlanBuilder.NormalizeGameRootFileMappings(files);
		}

		private string GetAdjustedPath(string path, ModPathContext context)
		{
			return BasicInstallPlanBuilder.GetAdjustedPath(GameMode, Mod, InstallRoot, path, context);
		}

		protected void ActivatePlugin(string p_strPlugin)
		{
			if (FileInstaller.PluginCheck(p_strPlugin, false))
				if (PluginManager.IsActivatiblePluginFile(p_strPlugin))
					PluginManager.ActivatePlugin(p_strPlugin);
		}
	}
}
