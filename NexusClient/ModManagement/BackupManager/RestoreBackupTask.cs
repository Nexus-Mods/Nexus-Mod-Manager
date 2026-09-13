using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using ChinhDo.Transactions;
using Nexus.Client.BackgroundTasks;
using Nexus.Transactions;
using Nexus.Client.UI;
using Nexus.Client.Util;
using Nexus.Client.Util.Localization;
using SevenZip;


namespace Nexus.Client.ModManagement
{
	public class RestoreBackupTask : ThreadedBackgroundTask
	{
		bool m_booAllowCancel = true;

		#region Fields
			
		private VirtualModActivator VirtualModActivator = null;
		private ConfirmActionMethod m_camConfirm = null;
		private readonly string m_strExtractProgressFormat;
		private ModManager ModManager = null;
		private ProfileManager ProfileManager = null;
		private IEnvironmentInfo EnvironmentInfo = null;
		private string BackupFile = string.Empty;
		private bool PurgeFolders = false;

		/// <summary>
		/// Gets or sets the file utility class.
		/// </summary>
		/// <value>The file utility class.</value>
		protected FileUtil FileUtility { get; set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		public RestoreBackupTask(VirtualModActivator p_vmaActivator, ModManager p_ModManager, ProfileManager p_pmProfileManager, IEnvironmentInfo p_EnvironmentInfo, string p_strBackupFile, bool p_booPurgeFolders, ConfirmActionMethod p_camConfirm)
		{
			m_camConfirm = p_camConfirm;
			m_strExtractProgressFormat = LanguageManager.GetFormat("Tools.Restore.Progress.ExtractPercent", "Extracting Archive...{0}%");
			VirtualModActivator = p_vmaActivator;
			ModManager = p_ModManager;
			EnvironmentInfo = p_EnvironmentInfo;
			BackupFile = p_strBackupFile;
			PurgeFolders = p_booPurgeFolders;
			ProfileManager = p_pmProfileManager;
		}

		#endregion

		#region Event Raising

		/// <summary>
		/// Raises the <see cref="IBackgroundTask.TaskEnded"/> event.
		/// </summary>
		/// <param name="e">A <see cref="TaskEndedEventArgs"/> describing the event arguments.</param>
		protected override void OnTaskEnded(TaskEndedEventArgs e)
		{
			base.OnTaskEnded(e);
		}
		#endregion

		/// <summary>
		/// Starts the update.
		/// </summary>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		public void Update(ConfirmActionMethod p_camConfirm)
		{
			Start(p_camConfirm);
		}

		/// <summary>
		/// Resumes the task.
		/// </summary>
		/// <exception cref="InvalidOperationException">Thrown if the task is not paused.</exception>
		public override void Resume()
		{
			Update(m_camConfirm);
		}

		/// <summary>
		/// Cancels the update.
		/// </summary>
		public override void Cancel()
		{
			if (m_booAllowCancel)
				base.Cancel();
		}

		/// <summary>
		/// The method that is called to start the backgound task.
		/// </summary>
		/// <param name="args">Arguments to for the task execution.</param>
		/// <returns>Always <c>null</c>.</returns>
		protected override object DoWork(object[] args)
		{
			OverallMessage = LanguageManager.Get("Tools.Restore.Progress.Starting", "Restoring Nexus Mod Manager Backup...");
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			ShowItemProgress = true;
			OverallProgressMaximum = 5;

			List<BackupInfo> lstLooseFiles = new List<BackupInfo>();
			List<BackupInfo> lstInstalledModFiles = new List<BackupInfo>();
			List<BackupInfo> lstInstalledNMMLINKFiles = new List<BackupInfo>();
			List<BackupInfo> lstBaseGameFiles = new List<BackupInfo>();
			List<BackupInfo> lstProfileFiles = new List<BackupInfo>();
			List<BackupInfo> lstModArchives = new List<BackupInfo>();
			List<BackupInfo> lstModCacheArchives = new List<BackupInfo>();
			List<BackupInfo> lstDirectDeploymentFiles = new List<BackupInfo>();
			List<BackupInfo> lstDeploymentBackupFiles = new List<BackupInfo>();

			ModProfile mprModProfile = null;

			string installLog = string.Empty;
			string BackupDirectory = string.Empty;
			string bkpArchive = BackupFile;
			
			if (!File.Exists(bkpArchive))
			{
				string strMessage = LanguageManager.Format("Tools.Restore.Error.BackupMissing", "The Backup file {0} is missing.", bkpArchive);
				DialogResult drFormClose = MessageBox.Show(strMessage, LanguageManager.Get("Tools.Restore.Dialog.BackupTitle", "NMM Backup"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return null;
			}

			OverallMessage = LanguageManager.Get("Tools.Restore.Progress.CheckArchive", "Checking the backup archive...");
			StepOverallProgress();

			using (SevenZipExtractor szeExtractor = new SevenZipExtractor(bkpArchive))
			{
				ReadOnlyCollection<string> lstArchiveFiles = szeExtractor.ArchiveFileNames;

				if ((!lstArchiveFiles.Contains(Path.GetFileName(ModManager.GameMode.PluginDirectory), StringComparer.OrdinalIgnoreCase)) || (!lstArchiveFiles.Contains("VIRTUAL INSTALL", StringComparer.OrdinalIgnoreCase)))
				{
					string strMessage = LanguageManager.Format("Tools.Restore.Error.BackupWrong", "The Backup file {0} is wrong.", bkpArchive);
					DialogResult drFormClose = MessageBox.Show(strMessage, LanguageManager.Get("Tools.Restore.Dialog.BackupTitle", "NMM Backup"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return null;
				}

				ItemMessage = LanguageManager.Get("Tools.Restore.Progress.ExtractArchive", "Extracting the backup archive...");
				ItemProgress = 0;
				ItemProgressMaximum = szeExtractor.ArchiveFileNames.Count;
				BackupDirectory = Path.Combine(EnvironmentInfo.TemporaryPath, "NMMBACKUP");

				if (Directory.Exists(BackupDirectory))
					FileUtil.ForceDelete(BackupDirectory);

				Directory.CreateDirectory(BackupDirectory);

				szeExtractor.FileExtractionStarted += new EventHandler<FileInfoEventArgs>(Extractor_FileExtractionStarted);
				szeExtractor.FileExtractionFinished += new EventHandler<FileInfoEventArgs>(Extractor_FileExtractionFinished);
				szeExtractor.Extracting += new EventHandler<ProgressEventArgs>(Extractor_Extracting);
				try
				{
					szeExtractor.ExtractArchive(BackupDirectory);
				}
				catch (ExtractionFailedException ex)
				{
					Status = TaskStatus.Error;
					return ex.Message;
				}
				catch (FileNotFoundException ex)
				{
					Status = TaskStatus.Error;
					return ex.Message;
				}
				if (File.Exists(Path.Combine(BackupDirectory, "InstallLog.xml")))
					installLog = Path.Combine(BackupDirectory, "InstallLog.xml");
												
				OverallMessage = LanguageManager.Format("Tools.Restore.Progress.CreateGameFileList", "Creating the {0} Files list.", Path.GetFileName(ModManager.GameMode.PluginDirectory));
				StepOverallProgress();
				string[] DATAfiles = Directory.GetFiles(Path.Combine(BackupDirectory, Path.GetFileName(ModManager.GameMode.PluginDirectory)), "*.*", SearchOption.AllDirectories);
				FileInfo fInfo = null;

				OverallProgressMaximum = DATAfiles.Count();
				ItemProgress = 0;
				foreach (string file in DATAfiles)
				{
					string[] result = file.Split(new string[] { Path.Combine(BackupDirectory, Path.GetFileName(ModManager.GameMode.PluginDirectory)) + Path.DirectorySeparatorChar }, StringSplitOptions.None);
					fInfo = new FileInfo(file);
					lstLooseFiles.Add(new BackupInfo(result[1], file, "", Path.GetFileName(ModManager.GameMode.PluginDirectory), fInfo.Length));

					if (ItemProgress < OverallProgressMaximum)
					{
						ItemMessage = file;
						StepItemProgress();
					}
				}

				OverallMessage = LanguageManager.Get("Tools.Restore.Progress.CreateVirtualInstallList", "Creating the VIRTUAL INSTALL Files list.");
				StepOverallProgress();
				string[] VIRTUALINSTALLfiles = Directory.GetFiles(Path.Combine(BackupDirectory, "VIRTUAL INSTALL"), "*.*", SearchOption.AllDirectories);
				fInfo = null;

				OverallProgressMaximum = VIRTUALINSTALLfiles.Count();
				ItemProgress = 0;
				foreach (string file in VIRTUALINSTALLfiles)
				{
					string[] result = file.Split(new string[] { "VIRTUAL INSTALL" + Path.DirectorySeparatorChar }, StringSplitOptions.None);
					fInfo = new FileInfo(file);
					lstInstalledModFiles.Add(new BackupInfo(result[1], file, "", "VIRTUAL INSTALL", fInfo.Length));

					if (ItemProgress < OverallProgressMaximum)
					{
						ItemMessage = file;
						StepItemProgress();
					}
				}

				OverallMessage = LanguageManager.Get("Tools.Restore.Progress.CreateNmmLinkList", "Creating the NMMLINK Files list.");
				StepOverallProgress();
				
				if (Directory.Exists(Path.Combine(BackupDirectory, "NMMLINK")))
				{
					string[] NMMLINKFiles = Directory.GetFiles(Path.Combine(BackupDirectory, "NMMLINK"), "*.*", SearchOption.AllDirectories);
					fInfo = null;

					foreach (string file in NMMLINKFiles)
					{
						string[] result = file.Split(new string[] { "NMMLINK" + Path.DirectorySeparatorChar }, StringSplitOptions.None);
						fInfo = new FileInfo(file);

						if (ModManager.VirtualModActivator.MultiHDMode)
							lstInstalledNMMLINKFiles.Add(new BackupInfo(result[1], file, "", "NMMLINK", fInfo.Length));
						else
							lstInstalledNMMLINKFiles.Add(new BackupInfo(result[1], file, "", "VIRTUAL INSTALL", fInfo.Length));

						if (ItemProgress < OverallProgressMaximum)
						{
							ItemMessage = file;
							StepItemProgress();
						}
					}
				}

				OverallMessage = LanguageManager.Get("Tools.Restore.Progress.CreateArchivesList", "Creating the MOD ARCHIVES list.");
				StepOverallProgress();
				if (Directory.Exists(Path.Combine(BackupDirectory, "MODS")))
				{
					string[] ModArchives = Directory.GetFiles(Path.Combine(BackupDirectory, "MODS"), "*.*", SearchOption.AllDirectories);
					fInfo = null;

					OverallProgressMaximum = ModArchives.Count();
					ItemProgress = 0;
					foreach (string file in ModArchives)
					{
						string[] result = file.Split(new string[] { "MODS" + Path.DirectorySeparatorChar }, StringSplitOptions.None);
						fInfo = new FileInfo(file);
						lstModArchives.Add(new BackupInfo(result[1], file, "", "MODS", fInfo.Length));

						if (ItemProgress < OverallProgressMaximum)
						{
							ItemMessage = file;
							StepItemProgress();
						}
					}
				}

				OverallMessage = LanguageManager.Get("Tools.Restore.Progress.CreateCache", "Creating the CACHE.");
				StepOverallProgress();
				if (Directory.Exists(Path.Combine(BackupDirectory, "cache")))
				{
					string[] ModCacheArchives = Directory.GetFiles(Path.Combine(BackupDirectory, "cache"), "*.*", SearchOption.AllDirectories);
					fInfo = null;

					OverallProgressMaximum = ModCacheArchives.Count();
					ItemProgress = 0;
					foreach (string file in ModCacheArchives)
					{
						string[] result = file.Split(new string[] { "cache" + Path.DirectorySeparatorChar }, StringSplitOptions.None);
						fInfo = new FileInfo(file);
						lstModCacheArchives.Add(new BackupInfo(result[1], file, "", "cache", fInfo.Length));

						if (ItemProgress < OverallProgressMaximum)
						{
							ItemMessage = file;
							StepItemProgress();
						}
					}
				}

				OverallMessage = LanguageManager.Get("Tools.Restore.Progress.CreateProfileList", "Creating the PROFILE Files list.");
				StepOverallProgress();
				string[] Profilefiles = Directory.GetFiles(Path.Combine(BackupDirectory, "PROFILE"), "*.*", SearchOption.AllDirectories);
				fInfo = null;
				
				OverallProgressMaximum = Profilefiles.Count();
				ItemProgress = 0;
				foreach (string file in Profilefiles)
				{
					string[] result = file.Split(new string[] { "PROFILE" + Path.DirectorySeparatorChar }, StringSplitOptions.None);
					fInfo = new FileInfo(file);
					lstProfileFiles.Add(new BackupInfo(result[1], file, "", "PROFILE", fInfo.Length));

					if (ItemProgress < OverallProgressMaximum)
					{
						ItemMessage = file;
						StepItemProgress();
					}
				}

				string deploymentDirectory = Path.Combine(BackupDirectory, "DEPLOYMENT");
				bool hasDeploymentState = Directory.Exists(deploymentDirectory);
				if (hasDeploymentState)
				{
					AddBackupFiles(Path.Combine(deploymentDirectory, "active"), lstDirectDeploymentFiles, "DEPLOYMENT ACTIVE");
					AddBackupFiles(Path.Combine(deploymentDirectory, "overwrites"), lstDeploymentBackupFiles, "DEPLOYMENT OVERWRITES");
				}

				mprModProfile = RestoreBackupFiles(lstLooseFiles, lstInstalledModFiles, lstInstalledNMMLINKFiles, lstProfileFiles, lstModArchives, lstModCacheArchives,
					lstDirectDeploymentFiles, lstDeploymentBackupFiles, hasDeploymentState, BackupDirectory, installLog);
			}

			StepOverallProgress();
				
			return mprModProfile;
		}

		/// <summary>
		/// The method that is called to restore the Backup files.
		/// </summary>
		private ModProfile RestoreBackupFiles(List<BackupInfo> p_lstLooseFiles, List<BackupInfo> p_lstInstalledModFiles, List<BackupInfo> p_lstInstalledNMMLINKFiles, List<BackupInfo> p_lstProfileFiles, List<BackupInfo> p_lstModArchives, List<BackupInfo> p_lstModCacheArchives,
			List<BackupInfo> p_lstDirectDeploymentFiles, List<BackupInfo> p_lstDeploymentBackupFiles, bool p_booHasDeploymentState, string p_strBackupDirectory, string p_strInstallLog)
		{
			string copyGameFilesFormat = LanguageManager.GetFormat("Tools.Restore.Progress.CopyGameFiles", "Copying the {0} Files...{1}/{2}");
			string copyVirtualInstallFormat = LanguageManager.GetFormat("Tools.Restore.Progress.CopyVirtualInstall", "Copying the VIRTUAL INSTALL Files...{0}/{1}");
			string copyNmmLinkFormat = LanguageManager.GetFormat("Tools.Restore.Progress.CopyNmmLink", "Copying the NMMLINK Files...{0}/{1}");
			string copyArchivesFormat = LanguageManager.GetFormat("Tools.Restore.Progress.CopyArchives", "Copying the MOD ARCHIVES...{0}/{1}");
			string copyCacheFormat = LanguageManager.GetFormat("Tools.Restore.Progress.CopyCache", "Copying the CACHE...{0}/{1}");
			string copyProfileFormat = LanguageManager.GetFormat("Tools.Restore.Progress.CopyProfile", "Copying the PROFILE Files...{0}/{1}");

			string FileTo = string.Empty;
			string FileFrom = string.Empty;
			string VIRTUALINSTALLpath = VirtualModActivator.VirtualFoder;
			string NMMLINKpath = VirtualModActivator.HDLinkFolder;
			if (string.IsNullOrEmpty(NMMLINKpath))
				NMMLINKpath = VIRTUALINSTALLpath;
			bool SeparateNmmLinkRoot = !AreSameDirectoryPath(VIRTUALINSTALLpath, NMMLINKpath);
			string ModArchivesPath = ModManager.GameMode.GameModeEnvironmentInfo.ModDirectory;
			string ModCacheArchivesPath = ModManager.GameMode.GameModeEnvironmentInfo.ModCacheDirectory;
			string dir = string.Empty;
			string ProfileId = string.Empty;
			int counter = 0;
			string ModInstallFolder = ModManager.GameMode.PluginDirectory;
			string ModInstallBackup = ModInstallFolder + "_oldbkp";
			string VirtualInstallBackup = VIRTUALINSTALLpath + "_oldbkp";
			string NmmLinkBackup = NMMLINKpath + "_oldbkp";
			bool PurgedModInstall = false;
			bool PurgedVirtualInstall = false;
			bool PurgedNmmLink = false;
			ModProfile mprModProfile = null;
			TransactionScope deploymentRestoreTransaction = null;
			TxFileManager deploymentFileManager = null;
			string deploymentBackupRollback = null;
			bool deploymentBackupStoreReplaced = false;
			string installLogPath = Path.Combine(ModManager.GameMode.GameModeEnvironmentInfo.InstallInfoDirectory, "InstallLog.xml");
			bool installLogRestoreStarted = false;
			bool deploymentRestoreCommitted = false;

			try
			{
				PrevalidateDeploymentRestore(p_lstDirectDeploymentFiles, p_lstDeploymentBackupFiles, p_booHasDeploymentState, p_strInstallLog);
			}
			catch
			{
				return null;
			}

			try
			{
				if (PurgeFolders)
				{
					if (p_lstLooseFiles.Count > 0)
						if (Directory.Exists(ModInstallFolder))
						{
							FileUtil.RenameDirectory(ModInstallFolder, ModInstallBackup);
							PurgedModInstall = true;
						}
					bool restoreVirtualRoot = p_lstInstalledModFiles.Count > 0 || (!SeparateNmmLinkRoot && p_lstInstalledNMMLINKFiles.Count > 0);
					if (restoreVirtualRoot && Directory.Exists(VIRTUALINSTALLpath))
					{
						FileUtil.RenameDirectory(VIRTUALINSTALLpath, VirtualInstallBackup);
						PurgedVirtualInstall = true;
					}
					if (SeparateNmmLinkRoot && p_lstInstalledNMMLINKFiles.Count > 0 && Directory.Exists(NMMLINKpath))
					{
						FileUtil.RenameDirectory(NMMLINKpath, NmmLinkBackup);
						PurgedNmmLink = true;
					}
				}

				deploymentRestoreTransaction = new TransactionScope();
				deploymentFileManager = new TxFileManager();
				ClearCurrentPromotedDeploymentFiles(deploymentFileManager);

				if (p_lstLooseFiles.Count() > 0)
				{
					OverallProgressMaximum = p_lstLooseFiles.Count();

					if (!Directory.Exists(ModManager.GameMode.PluginDirectory))
						Directory.CreateDirectory(ModManager.GameMode.PluginDirectory);

					foreach (BackupInfo bkInfo in p_lstLooseFiles)
					{
						dir = Path.GetDirectoryName(Path.Combine(bkInfo.ModID, bkInfo.VirtualModPath));
						if (!string.IsNullOrEmpty(dir))
							Directory.CreateDirectory(Path.Combine(ModManager.GameMode.PluginDirectory, dir));

						FileFrom = bkInfo.RealModPath;
						FileTo = Path.Combine(ModManager.GameMode.PluginDirectory, bkInfo.ModID, bkInfo.VirtualModPath);

						File.Copy(FileFrom, FileTo, true);

						if (counter < p_lstLooseFiles.Count())
						{
							ItemMessage = bkInfo.VirtualModPath;
							StepItemProgress();
						}

						OverallMessage = string.Format(copyGameFilesFormat, Path.GetFileName(ModManager.GameMode.PluginDirectory), counter++, p_lstLooseFiles.Count());
						StepOverallProgress();
					}
				}

				OverallMessage = LanguageManager.Get("Tools.Restore.Progress.CopyVirtualInstallStart", "Copying the VIRTUAL INSTALL Files.");
				StepOverallProgress();

				if (p_lstInstalledModFiles.Count() > 0)
				{
					OverallProgressMaximum = p_lstInstalledModFiles.Count();

					counter = 0;
					foreach (BackupInfo bkInfo in p_lstInstalledModFiles)
					{
						FileFrom = bkInfo.RealModPath;
						FileTo = bkInfo.VirtualModPath == "VirtualModConfig.xml" ? Path.Combine(VIRTUALINSTALLpath, bkInfo.VirtualModPath) : Path.Combine(VIRTUALINSTALLpath, bkInfo.ModID, bkInfo.VirtualModPath);

						RestorePayloadFile(deploymentFileManager, FileFrom, FileTo);

						if (counter < p_lstInstalledModFiles.Count())
						{
							ItemMessage = bkInfo.VirtualModPath;
							StepItemProgress();
						}

						OverallMessage = string.Format(copyVirtualInstallFormat, counter++, p_lstInstalledModFiles.Count());
						StepOverallProgress();
					}
				}

				OverallMessage = LanguageManager.Get("Tools.Restore.Progress.CopyNmmLinkStart", "Copying the NMMLINK Files.");
				StepOverallProgress();

				if (p_lstInstalledNMMLINKFiles.Count() > 0)
				{
					OverallProgressMaximum = p_lstInstalledNMMLINKFiles.Count();
					
					counter = 0;
					foreach (BackupInfo bkInfo in p_lstInstalledNMMLINKFiles)
					{
						FileFrom = bkInfo.RealModPath;
						FileTo = Path.Combine(NMMLINKpath, bkInfo.VirtualModPath);

						RestorePayloadFile(deploymentFileManager, FileFrom, FileTo);


						if (counter < p_lstInstalledNMMLINKFiles.Count())
						{
							ItemMessage = bkInfo.VirtualModPath;
							StepItemProgress();
						}

						OverallMessage = string.Format(copyNmmLinkFormat, counter++, p_lstInstalledNMMLINKFiles.Count());
						StepOverallProgress();
					}
				}

				OverallMessage = LanguageManager.Get("Tools.Restore.Progress.CopyArchivesStart", "Copying the MOD ARCHIVES.");
				StepOverallProgress();

				if (p_lstModArchives.Count() > 0)
				{
					OverallProgressMaximum = p_lstModArchives.Count();

					if (!Directory.Exists(ModArchivesPath))
						Directory.CreateDirectory(ModArchivesPath);

					counter = 0;
					foreach (BackupInfo bkInfo in p_lstModArchives)
					{
						dir = Path.GetDirectoryName(Path.Combine(bkInfo.ModID, bkInfo.VirtualModPath));
						if (!string.IsNullOrEmpty(dir))
							Directory.CreateDirectory(Path.Combine(ModArchivesPath, dir));

						FileFrom = bkInfo.RealModPath;
						FileTo = Path.Combine(ModArchivesPath,bkInfo.VirtualModPath);

						File.Copy(FileFrom, FileTo, true);

						if (counter < p_lstModArchives.Count())
						{
							ItemMessage = bkInfo.VirtualModPath;
							StepItemProgress();
						}

						OverallMessage = string.Format(copyArchivesFormat, counter++, p_lstModArchives.Count());
						StepOverallProgress();
					}
				}

				OverallMessage = LanguageManager.Get("Tools.Restore.Progress.CopyCacheStart", "Copying the CACHE.");
				StepOverallProgress();

				if (p_lstModCacheArchives.Count() > 0)
				{
					OverallProgressMaximum = p_lstModCacheArchives.Count();

					if (!Directory.Exists(ModCacheArchivesPath))
						Directory.CreateDirectory(ModCacheArchivesPath);

					counter = 0;
					foreach (BackupInfo bkInfo in p_lstModCacheArchives)
					{
						dir = Path.GetDirectoryName(Path.Combine(bkInfo.ModID, bkInfo.VirtualModPath));
						if (!string.IsNullOrEmpty(dir))
							Directory.CreateDirectory(Path.Combine(ModCacheArchivesPath, dir));

						FileFrom = bkInfo.RealModPath;
						FileTo = Path.Combine(ModCacheArchivesPath, bkInfo.VirtualModPath);

						File.Copy(FileFrom, FileTo, true);

						if (counter < p_lstModCacheArchives.Count())
						{
							ItemMessage = bkInfo.VirtualModPath;
							StepItemProgress();
						}

						OverallMessage = string.Format(copyCacheFormat, counter++, p_lstModCacheArchives.Count());
						StepOverallProgress();
					}
				}

				// Always replace the shared deployment store so stale payloads from the pre-restore state cannot satisfy restored ownership metadata.
				deploymentBackupRollback = RestoreDeploymentBackups(p_lstDeploymentBackupFiles);
				deploymentBackupStoreReplaced = true;
				if (p_booHasDeploymentState)
					RestoreDirectWinners(p_lstDirectDeploymentFiles, deploymentFileManager);

				if (p_lstProfileFiles.Count() > 0)
				{
					OverallProgressMaximum = p_lstProfileFiles.Count();

					if (!Directory.Exists(ProfileManager.ProfileManagerPath))
						Directory.CreateDirectory(ProfileManager.ProfileManagerPath);
					
					counter = 0;
					foreach (BackupInfo bkInfo in p_lstProfileFiles)
					{
						dir = Path.GetDirectoryName(Path.Combine(bkInfo.ModID, bkInfo.VirtualModPath));
						if (!string.IsNullOrEmpty(dir))
							Directory.CreateDirectory(Path.Combine(ProfileManager.ProfileManagerPath, dir));

						if (string.IsNullOrEmpty(ProfileId))
							ProfileId = dir;

						FileFrom = bkInfo.RealModPath;
						FileTo = Path.Combine(ProfileManager.ProfileManagerPath, bkInfo.ModID, bkInfo.VirtualModPath);

						File.Copy(FileFrom, FileTo, true);

						if (counter < p_lstProfileFiles.Count())
						{
							ItemMessage = bkInfo.VirtualModPath;
							StepItemProgress();
						}

						OverallMessage = string.Format(copyProfileFormat, counter++, p_lstProfileFiles.Count());
						StepOverallProgress();
					}
				}
				
				string xmlProfilePath = Path.Combine(ProfileManager.ProfileManagerPath, ProfileId, "profile.xml");
				mprModProfile = CreateProfile(xmlProfilePath);

				if (!string.IsNullOrEmpty(p_strInstallLog))
				{
					installLogRestoreStarted = true;
					deploymentFileManager.Copy(p_strInstallLog, installLogPath, true);
					ModManager.ReinitializeInstallLog(installLogPath);
					RestoreVirtualPromotedWinners();
					ValidateRestoredDeploymentState();
				}

				deploymentRestoreTransaction.Complete();
				deploymentRestoreTransaction.Dispose();
				deploymentRestoreTransaction = null;
				deploymentRestoreCommitted = true;
				DiscardDeploymentBackupRollback(deploymentBackupRollback);
				deploymentBackupRollback = null;
								
				OverallMessage = LanguageManager.Get("Tools.Restore.Progress.DeleteLeftovers", "Deleting the leftovers.");
				StepOverallProgress();


				if (Directory.Exists(p_strBackupDirectory))
					FileUtil.ForceDelete(p_strBackupDirectory);

				if (Directory.Exists(ModInstallBackup))
					FileUtil.ForceDelete(ModInstallBackup);

				if (Directory.Exists(VirtualInstallBackup))
					FileUtil.ForceDelete(VirtualInstallBackup);

				if (PurgedNmmLink && Directory.Exists(NmmLinkBackup))
					FileUtil.ForceDelete(NmmLinkBackup);
			}
			catch (Exception e)
			{
				if (!deploymentRestoreCommitted)
				{
					if (deploymentRestoreTransaction != null)
					{
						try
						{
							deploymentRestoreTransaction.Dispose();
						}
						catch
						{
						}
					}

					if (deploymentBackupStoreReplaced)
					{
						try
						{
							RollbackDeploymentBackups(deploymentBackupRollback);
						}
						catch
						{
						}
					}

					if (installLogRestoreStarted && File.Exists(installLogPath))
					{
						try
						{
							ModManager.ReinitializeInstallLog(installLogPath);
						}
						catch
						{
						}
					}
				}

				if (PurgeFolders && !deploymentRestoreCommitted)
				{
					string ModInstallDelete = ModInstallFolder + "_DELETE";
					string VirtualInstallDelete = VIRTUALINSTALLpath + "_DELETE";
					string NmmLinkDelete = NMMLINKpath + "_DELETE";

					if (PurgedModInstall)
					{
						if (Directory.Exists(ModInstallFolder))
							FileUtil.RenameDirectory(ModInstallFolder, ModInstallDelete);

						if (Directory.Exists(ModInstallBackup))
							FileUtil.RenameDirectory(ModInstallBackup, ModInstallFolder);

						if (Directory.Exists(ModInstallDelete))
							FileUtil.ForceDelete(ModInstallDelete);
					}

					if (PurgedVirtualInstall)
					{
						if (Directory.Exists(VIRTUALINSTALLpath))
							FileUtil.RenameDirectory(VIRTUALINSTALLpath, VirtualInstallDelete);

						if (Directory.Exists(VirtualInstallBackup))
							FileUtil.RenameDirectory(VirtualInstallBackup, VIRTUALINSTALLpath);

						if (Directory.Exists(VirtualInstallDelete))
							FileUtil.ForceDelete(VirtualInstallDelete);
					}

					if (PurgedNmmLink)
					{
						if (Directory.Exists(NMMLINKpath))
							FileUtil.RenameDirectory(NMMLINKpath, NmmLinkDelete);

						if (Directory.Exists(NmmLinkBackup))
							FileUtil.RenameDirectory(NmmLinkBackup, NMMLINKpath);

						if (Directory.Exists(NmmLinkDelete))
							FileUtil.ForceDelete(NmmLinkDelete);
					}
				}

				string ErrorMessage = string.Format("Exception: {0}" + Environment.NewLine + Environment.NewLine + "From: {1}" + Environment.NewLine + "To: {2}", e.Message, FileFrom, FileTo);

				return null;
			}

			return mprModProfile;
		}

		/// <summary>
		/// Restores one Virtual/NMMLINK payload without deleting sibling content and journals the write for rollback.
		/// </summary>
		private static void RestorePayloadFile(TxFileManager p_tfmFileManager, string p_strSourcePath, string p_strDestinationPath)
		{
			string directory = Path.GetDirectoryName(p_strDestinationPath);
			if (!String.IsNullOrEmpty(directory))
				p_tfmFileManager.CreateDirectory(directory);

			p_tfmFileManager.Copy(p_strSourcePath, p_strDestinationPath, true);
		}

		/// <summary>
		/// Compares two restore-root paths using Windows filesystem semantics.
		/// </summary>
		private static bool AreSameDirectoryPath(string p_strFirstPath, string p_strSecondPath)
		{
			if (String.IsNullOrWhiteSpace(p_strFirstPath) || String.IsNullOrWhiteSpace(p_strSecondPath))
				return String.Equals(p_strFirstPath, p_strSecondPath, StringComparison.OrdinalIgnoreCase);

			string firstPath = Path.GetFullPath(p_strFirstPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string secondPath = Path.GetFullPath(p_strSecondPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			return String.Equals(firstPath, secondPath, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Adds every file below a deployment backup directory using a safe relative archive path.
		/// </summary>
		private static void AddBackupFiles(string p_strRootPath, ICollection<BackupInfo> p_colFiles, string p_strDirectory)
		{
			if (!Directory.Exists(p_strRootPath))
				return;

			foreach (string file in Directory.GetFiles(p_strRootPath, "*.*", SearchOption.AllDirectories))
			{
				string relativePath = GetRelativePath(p_strRootPath, file);
				FileInfo fileInfo = new FileInfo(file);
				p_colFiles.Add(new BackupInfo(relativePath, file, String.Empty, p_strDirectory, fileInfo.Length));
			}
		}

		/// <summary>
		/// Validates Direct deployment restore inputs before current promoted state is displaced.
		/// </summary>
		private void PrevalidateDeploymentRestore(IEnumerable<BackupInfo> p_enmDirectFiles, IEnumerable<BackupInfo> p_enmDeploymentBackups, bool p_booHasDeploymentState, string p_strInstallLog)
		{
			string backupRoot = Path.Combine(ModManager.GameMode.GameModeEnvironmentInfo.OverwriteDirectory, "deployment");
			foreach (BackupInfo backupInfo in p_enmDeploymentBackups)
			{
				if (!File.Exists(backupInfo.RealModPath))
					throw new FileNotFoundException("A deployment backup payload required by the restore is missing.", backupInfo.RealModPath);
				ResolveContainedPath(backupRoot, backupInfo.VirtualModPath);
			}

			foreach (BackupInfo backupInfo in p_enmDirectFiles)
			{
				if (!File.Exists(backupInfo.RealModPath))
					throw new FileNotFoundException("A Direct winner payload required by the restore is missing.", backupInfo.RealModPath);
				ModManager.DeploymentManager.GetDeploymentPath(ParseDeploymentTarget(backupInfo.VirtualModPath));
			}

			if (!p_booHasDeploymentState)
				return;
			if (String.IsNullOrEmpty(p_strInstallLog) || !File.Exists(p_strInstallLog))
				throw new FileNotFoundException("The InstallLog required by the deployment restore is missing.", p_strInstallLog);

			XDocument.Load(p_strInstallLog);
		}

		/// <summary>
		/// Removes current promoted physical winners while retaining enough transactional state to restore them on failure.
		/// </summary>
		private void ClearCurrentPromotedDeploymentFiles(TxFileManager p_tfmFileManager)
		{
			if (ModManager.DeploymentManager == null || !ModManager.DeploymentManager.HasPromotedTargets)
				return;

			foreach (ModDeploymentTarget target in ModManager.DeploymentManager.GetPromotedTargets())
			{
				string deploymentPath = ModManager.DeploymentManager.GetDeploymentPath(target);
				if (!File.Exists(deploymentPath))
					continue;

				IReadOnlyList<string> owners = ModManager.DeploymentManager.GetOwnerKeys(target);
				if (owners.Count > 0)
				{
					string winnerKey = owners[owners.Count - 1];
					bool isOriginal = winnerKey.Equals(ModManager.InstallationLog.OriginalValuesKey, StringComparison.OrdinalIgnoreCase);
					if (!isOriginal && ModManager.InstallationLog.GetModInstallMethod(winnerKey) == ModInstallMethod.Virtual)
					{
						string sourcePath = ModManager.DeploymentManager.GetOwnerSourcePath(target, winnerKey);
						if (!String.IsNullOrEmpty(sourcePath))
						{
							p_tfmFileManager.DeleteLink(deploymentPath, sourcePath);
							continue;
						}
					}
				}

				p_tfmFileManager.Delete(deploymentPath);
			}
		}

		/// <summary>
		/// Replaces the shared Direct/original overwrite store while retaining the previous store until restore commit.
		/// </summary>
		private string RestoreDeploymentBackups(IEnumerable<BackupInfo> p_enmFiles)
		{
			string backupRoot = Path.Combine(ModManager.GameMode.GameModeEnvironmentInfo.OverwriteDirectory, "deployment");
			bool hadOriginalStore = Directory.Exists(backupRoot);
			string rollbackRoot = null;

			try
			{
				if (hadOriginalStore)
				{
					string candidateRollbackRoot = backupRoot + ".restore-" + Guid.NewGuid().ToString("N");
					FileUtil.RenameDirectory(backupRoot, candidateRollbackRoot);
					rollbackRoot = candidateRollbackRoot;
				}

				foreach (BackupInfo backupInfo in p_enmFiles)
				{
					string destination = ResolveContainedPath(backupRoot, backupInfo.VirtualModPath);
					string directory = Path.GetDirectoryName(destination);
					if (!String.IsNullOrEmpty(directory) && !Directory.Exists(directory))
						Directory.CreateDirectory(directory);
					File.Copy(backupInfo.RealModPath, destination, true);
				}
			}
			catch
			{
				if (rollbackRoot != null)
					RollbackDeploymentBackups(rollbackRoot);
				else if (!hadOriginalStore && Directory.Exists(backupRoot))
					FileUtil.ForceDelete(backupRoot);
				throw;
			}

			return rollbackRoot;
		}

		/// <summary>
		/// Restores the retained pre-restore deployment backup store.
		/// </summary>
		private void RollbackDeploymentBackups(string p_strRollbackRoot)
		{
			string backupRoot = Path.Combine(ModManager.GameMode.GameModeEnvironmentInfo.OverwriteDirectory, "deployment");
			if (Directory.Exists(backupRoot))
				FileUtil.ForceDelete(backupRoot);
			if (!String.IsNullOrEmpty(p_strRollbackRoot) && Directory.Exists(p_strRollbackRoot))
				FileUtil.RenameDirectory(p_strRollbackRoot, backupRoot);
		}

		/// <summary>
		/// Discards the retained pre-restore deployment backup store after a successful restore commit.
		/// </summary>
		private void DiscardDeploymentBackupRollback(string p_strRollbackRoot)
		{
			if (String.IsNullOrEmpty(p_strRollbackRoot) || !Directory.Exists(p_strRollbackRoot))
				return;

			try
			{
				FileUtil.ForceDelete(p_strRollbackRoot);
			}
			catch
			{
				// Cleanup failure must not invalidate an otherwise committed restore.
			}
		}

		/// <summary>
		/// Restores exact bytes for Direct owners that were physical winners when the backup was created.
		/// </summary>
		private void RestoreDirectWinners(IEnumerable<BackupInfo> p_enmFiles, TxFileManager p_tfmFileManager)
		{
			foreach (BackupInfo backupInfo in p_enmFiles)
			{
				ModDeploymentTarget target = ParseDeploymentTarget(backupInfo.VirtualModPath);
				string destination = ModManager.DeploymentManager.GetDeploymentPath(target);
				string directory = Path.GetDirectoryName(destination);
				if (!String.IsNullOrEmpty(directory) && !Directory.Exists(directory))
					p_tfmFileManager.CreateDirectory(directory);

				// Delete first so a pre-restore hardlink/symlink can never redirect the Direct write into Virtual storage.
				if (String.IsNullOrEmpty(directory) || Directory.Exists(directory))
					p_tfmFileManager.Delete(destination);
				p_tfmFileManager.Copy(backupInfo.RealModPath, destination, false);
			}
		}

		/// <summary>
		/// Re-deploys Virtual winners after the restored VMA model and InstallLog have been reloaded.
		/// </summary>
		private void RestoreVirtualPromotedWinners()
		{
			if (ModManager.DeploymentManager == null || !ModManager.DeploymentManager.HasPromotedTargets)
				return;

			using (TransactionScope transaction = new TransactionScope())
			{
				TxFileManager fileManager = new TxFileManager();
				foreach (ModDeploymentTarget target in ModManager.DeploymentManager.GetPromotedTargets())
				{
					IReadOnlyList<string> owners = ModManager.DeploymentManager.GetOwnerKeys(target);
					if (owners.Count == 0)
						continue;

					string winnerKey = owners[owners.Count - 1];
					if (winnerKey.Equals(ModManager.InstallationLog.OriginalValuesKey, StringComparison.OrdinalIgnoreCase))
						throw new InvalidDataException(String.Format("Promoted target '{0}' has an unmanaged original winner.", target));
					if (ModManager.InstallationLog.GetModInstallMethod(winnerKey) == ModInstallMethod.Virtual)
						ModManager.VirtualModActivator.DeploySpecificVirtualLink(target, winnerKey, fileManager);
				}
				transaction.Complete();
			}
		}

		/// <summary>
		/// Verifies that every persisted Direct/original owner version required by a promoted stack is recoverable.
		/// </summary>
		private void ValidateRestoredDeploymentState()
		{
			if (ModManager.DeploymentManager == null || !ModManager.DeploymentManager.HasPromotedTargets)
				return;

			foreach (ModDeploymentTarget target in ModManager.DeploymentManager.GetPromotedTargets())
			{
				IReadOnlyList<string> owners = ModManager.DeploymentManager.GetOwnerKeys(target);
				if (owners.Count == 0)
					throw new InvalidDataException(String.Format("Promoted target '{0}' has no owners after restore.", target));

				string winnerKey = owners[owners.Count - 1];
				foreach (string ownerKey in owners)
				{
					bool isOriginal = ownerKey.Equals(ModManager.InstallationLog.OriginalValuesKey, StringComparison.OrdinalIgnoreCase);
					if (!isOriginal && ModManager.InstallationLog.GetModInstallMethod(ownerKey) == ModInstallMethod.Virtual)
					{
						if (String.IsNullOrEmpty(ModManager.DeploymentManager.GetOwnerSourcePath(target, ownerKey)))
							throw new FileNotFoundException(String.Format("The staged Virtual payload required to restore '{0}' is missing.", target));
						continue;
					}

					string payloadPath = !isOriginal && ownerKey.Equals(winnerKey, StringComparison.OrdinalIgnoreCase)
						? ModManager.DeploymentManager.GetDeploymentPath(target)
						: ModManager.DeploymentManager.GetOwnerBackupPath(target, ownerKey);
					if (!File.Exists(payloadPath))
						throw new FileNotFoundException(String.Format("The Direct/original deployment payload required to restore '{0}' is missing.", target), payloadPath);
				}
			}
		}

		private static ModDeploymentTarget ParseDeploymentTarget(string p_strArchivePath)
		{
			if (String.IsNullOrWhiteSpace(p_strArchivePath))
				throw new InvalidDataException("A Direct deployment backup entry has no target path.");

			int separatorIndex = p_strArchivePath.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
			if (separatorIndex <= 0 || separatorIndex == p_strArchivePath.Length - 1)
				throw new InvalidDataException(String.Format("Invalid Direct deployment backup target '{0}'.", p_strArchivePath));

			ModDeploymentRoot root;
			if (!Enum.TryParse(p_strArchivePath.Substring(0, separatorIndex), true, out root) || !Enum.IsDefined(typeof(ModDeploymentRoot), root))
				throw new InvalidDataException(String.Format("Invalid Direct deployment root in '{0}'.", p_strArchivePath));

			return ModDeploymentTargetResolver.FromCanonical(root, p_strArchivePath.Substring(separatorIndex + 1));
		}

		private static string ResolveContainedPath(string p_strRootPath, string p_strRelativePath)
		{
			string rootPath = Path.GetFullPath(p_strRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			string path = Path.GetFullPath(Path.Combine(rootPath, p_strRelativePath ?? String.Empty));
			if (!path.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException(String.Format("Backup entry '{0}' escapes its deployment root.", p_strRelativePath));
			return path;
		}

		private static string GetRelativePath(string p_strRootPath, string p_strPath)
		{
			string rootPath = Path.GetFullPath(p_strRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			string path = Path.GetFullPath(p_strPath);
			if (!path.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException(String.Format("Backup path '{0}' is outside '{1}'.", p_strPath, p_strRootPath));
			return path.Substring(rootPath.Length);
		}

		/// <summary>
		/// Creates the new profile.
		/// </summary>
		private ModProfile CreateProfile(string p_strXmlProfilePath)
		{
			Int32 intModCount = 0;
			string strProfileId = string.Empty;
			string strProfileName = string.Empty;
			string strGameModeId = string.Empty;
			
			if (File.Exists(p_strXmlProfilePath))
			{
				XDocument docProfile = XDocument.Load(p_strXmlProfilePath);

				try
				{

					XElement xelProfile = docProfile.Descendants("profile").FirstOrDefault();
					intModCount = Int32.TryParse(xelProfile.Element("modCount").Value, out intModCount) ? intModCount : 0;
					strProfileId = xelProfile.Attribute("profileId").Value;
					strProfileName = xelProfile.Attribute("profileName").Value;
					strGameModeId = xelProfile.Element("gameModeId").Value;
					
				}
				catch { }
			}
			
			ModProfile mprModProfile = new ModProfile(strProfileId, strProfileName, strGameModeId, intModCount);
			mprModProfile.IsDefault = true;
			ProfileManager.ModProfiles.Add(mprModProfile);
			ProfileManager.LoadProfileFileList(mprModProfile);
			ProfileManager.SaveConfig();
			return mprModProfile;
		}
	
		/// <summary>
		/// Handles the <see cref="SevenZipExtractor.FileExtractionFinished"/> event of
		/// the archive extractors.
		/// </summary>
		/// <remarks>
		/// This cancels the extraction if the user has cancelled the task. This also updates
		/// the item progress.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="FileInfoEventArgs"/> describing the event arguments.</param>
		private void Extractor_FileExtractionFinished(object sender, FileInfoEventArgs e)
		{
			e.Cancel = Status == TaskStatus.Cancelling;
			StepItemProgress();
		}

		private Int32 ImportedProfileModCount(string p_strXMLFilePath)
		{
			Int32 intModCount = 0;

			if (File.Exists(p_strXMLFilePath))
			{
				XDocument docProfile = XDocument.Load(p_strXMLFilePath);

				try
				{
					XElement xelProfile = docProfile.Descendants("profile").FirstOrDefault();
					intModCount = Int32.TryParse(xelProfile.Element("modCount").Value, out intModCount) ? intModCount : 0;
				}
				catch { }
			}

			return intModCount;
		}

		/// <summary>
		/// Handles the <see cref="SevenZipExtractor.FileExtractionStarted"/> event of
		/// the archive extractors.
		/// </summary>
		/// <remarks>
		/// This cancels the extraction if the user has cancelled the task.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="FileInfoEventArgs"/> describing the event arguments.</param>
		private void Extractor_FileExtractionStarted(object sender, FileInfoEventArgs e)
		{
			e.Cancel = Status == TaskStatus.Cancelling;
		}

		private void Extractor_Extracting(object sender, ProgressEventArgs e)
		{
			ItemMessage = string.Format(m_strExtractProgressFormat, e.PercentDone);
			StepItemProgress();
		}
	}
}
