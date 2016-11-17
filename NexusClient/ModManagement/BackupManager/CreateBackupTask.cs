using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.PluginManagement.UI;
using Nexus.Client.PluginManagement;
using Nexus.Client.Util;
using Nexus.Client.UI;
using Nexus.Client.Util.Collections;
using SevenZip;

namespace Nexus.Client.ModManagement
{
	public class CreateBackupTask : ThreadedBackgroundTask
	{
		bool m_booAllowCancel = true;

		#region Fields
			
		private VirtualModActivator VirtualModActivator = null;
		private PluginManagerVM PluginManagerVM = null;
		private IPluginManager PluginManager = null;
		private ProfileManager ProfileManager = null;
		private ConfirmActionMethod m_camConfirm = null;
		private ModManager ModManager = null;
		private BackupManager BackupManager = null;
		private IEnvironmentInfo EnvironmentInfo = null;
		private int FileCounter = 0;
		private int TotalFiles = 0;
		private long TotalFileSize = 0;
		private string SelectedPath = string.Empty;
		private static readonly Object m_objLock = new Object();

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		public CreateBackupTask(VirtualModActivator p_vmaActivator, ModManager p_ModManager, IEnvironmentInfo p_EnvironmentInfo, PluginManagerVM p_pmPluginManagerVM, IPluginManager p_pmPluginManager, ProfileManager p_pmProfileManager, string p_strSelectedPath, BackupManager p_bmBackupManager, ConfirmActionMethod p_camConfirm)
		{
			m_camConfirm = p_camConfirm;
			VirtualModActivator = p_vmaActivator;
			ModManager = p_ModManager;
			EnvironmentInfo = p_EnvironmentInfo;
			SelectedPath = p_strSelectedPath;
			BackupManager = p_bmBackupManager;
			TotalFileSize = BackupManager.TotalFileSize;
			PluginManagerVM = p_pmPluginManagerVM;
			PluginManager = p_pmPluginManager;
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
		/// Creates the next Profile ID.
		/// </summary>
		public string GetNextId
		{
			get
			{
				string RandomName = Path.GetFileNameWithoutExtension(Path.GetRandomFileName());
				while (ProfileManager.ModProfiles.Find(x => x.Id == RandomName) != null)
					RandomName = Path.GetFileNameWithoutExtension(Path.GetRandomFileName());

				return RandomName;
			}
		}

		/// <summary>
		/// Copies the directories.
		/// </summary>
		private static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
		{
			DirectoryInfo dir = new DirectoryInfo(sourceDirName);
			DirectoryInfo[] dirs = dir.GetDirectories();
						
			if (!dir.Exists)
				return;
						
			if (!Directory.Exists(destDirName))
			{
				Directory.CreateDirectory(destDirName);
			}
					
			FileInfo[] files = dir.GetFiles();

			foreach (FileInfo file in files)
			{
				string temppath = Path.Combine(destDirName, file.Name);
				file.CopyTo(temppath, false);
			}
						
			if (copySubDirs)
			{

				foreach (DirectoryInfo subdir in dirs)
				{
					string temppath = Path.Combine(destDirName, subdir.Name);
					DirectoryCopy(subdir.FullName, temppath, copySubDirs);
				}
			}
		}

		/// <summary>
		/// The method that is called to start the backgound task.
		/// </summary>
		/// <param name="p_objArgs">Arguments to for the task execution.</param>
		/// <returns>Always <c>null</c>.</returns>
		protected override object DoWork(object[] p_objArgs)
		{
			OverallMessage = "Backuping Nexus Mod Manager...";
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			ShowItemProgress = true;
			OverallProgressMaximum = 7;
			ItemProgressStepSize = 1;
			FileCounter = 0;

			OverallMessage = "Creating the directories.";
			StepOverallProgress();
			
			DriveInfo drive = new DriveInfo(EnvironmentInfo.TemporaryPath);
			drive = new DriveInfo(drive.Name);

			if (drive.AvailableFreeSpace > (TotalFileSize))
			{
				string BackupDirectory = Path.Combine(EnvironmentInfo.TemporaryPath, "NMMBACKUP");
				if (Directory.Exists(BackupDirectory))
					FileUtil.ForceDelete(BackupDirectory);

				string strPathToCreate = BackupDirectory;
				Directory.CreateDirectory(strPathToCreate);
				strPathToCreate = Path.Combine(BackupDirectory, Path.GetFileName(ModManager.GameMode.PluginDirectory));
				Directory.CreateDirectory(strPathToCreate);
				strPathToCreate = Path.Combine(BackupDirectory, "VIRTUAL INSTALL");
				Directory.CreateDirectory(strPathToCreate);
				strPathToCreate = Path.Combine(BackupDirectory, "PROFILE");
				Directory.CreateDirectory(strPathToCreate);

				bool PathLimit = CheckPathLimit(BackupDirectory);
				if (PathLimit)
				{
					string NewBackupDirectory = Path.Combine(Directory.GetDirectoryRoot(BackupDirectory), "NMMTemp");
					string WarningMessage = "Warning: NMM won't be able to use the default 'temp' folder for this operation, this will cause some file paths to reach the OS limit of 260 characters and prevent files to be copied." + Environment.NewLine + Environment.NewLine;
					WarningMessage = WarningMessage + "Just for this backup NMM will use a " + NewBackupDirectory + " folder. This folder will be removed after the backup completes.";

					MessageBox.Show(WarningMessage, "Create Backup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					BackupDirectory = NewBackupDirectory;
				}

				int i = 1;
				string dir = string.Empty;

				OverallProgressMaximum = BackupManager.lstBaseGameFiles.Count();
				if ((BackupManager.checkList.Contains(0)) && (BackupManager.lstBaseGameFiles.Count > 0))
				{
					i = 1;
					foreach (BackupInfo bkInfo in BackupManager.lstBaseGameFiles)
					{
						if (i < BackupManager.lstBaseGameFiles.Count())
						{
							ItemMessage = bkInfo.VirtualModPath;
							StepItemProgress();
						}

						OverallMessage = string.Format("Copying GAME BASE files...{0}/{1}", i++, BackupManager.lstBaseGameFiles.Count());
						StepOverallProgress();
						dir = Path.GetDirectoryName(Path.Combine(bkInfo.ModID, bkInfo.VirtualModPath));
						if (!string.IsNullOrEmpty(dir))
							Directory.CreateDirectory(Path.Combine(BackupDirectory, bkInfo.Directory, dir));

						try
						{
							File.Copy(bkInfo.RealModPath, Path.Combine(BackupDirectory, bkInfo.Directory, bkInfo.VirtualModPath), true);
						}
						catch (FileNotFoundException)
						{ }
					}

					TotalFiles += BackupManager.lstBaseGameFiles.Count;
				}

				if ((BackupManager.checkList.Contains(1)) && (BackupManager.lstInstalledModFiles.Count > 0))
				{
					OverallProgressMaximum = BackupManager.lstInstalledModFiles.Count();
					foreach (BackupInfo bkInfo in BackupManager.lstInstalledModFiles)
					{
						OverallMessage = string.Format("Copying MODS INSTALLATION files...{0}/{1}", i++, BackupManager.lstInstalledModFiles.Count());
						StepOverallProgress();
						dir = Path.GetDirectoryName(Path.Combine(bkInfo.ModID, bkInfo.VirtualModPath));
						if (!string.IsNullOrEmpty(dir))
							Directory.CreateDirectory(Path.Combine(BackupDirectory, bkInfo.Directory, dir));

						try
						{ 
							File.Copy(bkInfo.RealModPath, Path.Combine(BackupDirectory, bkInfo.Directory, bkInfo.ModID, bkInfo.VirtualModPath), true);
						}
						catch (FileNotFoundException)
						{ }

						if (ItemProgress < ItemProgressMaximum)
						{
							ItemMessage = bkInfo.RealModPath;
							StepItemProgress();
						}
					}

					OverallProgressMaximum = BackupManager.lstInstalledNMMLINKFiles.Count();
					foreach (BackupInfo bkInfo in BackupManager.lstInstalledNMMLINKFiles)
					{
						OverallMessage = string.Format("Copying NMMLINK files...{0}/{1}", i++, BackupManager.lstInstalledNMMLINKFiles.Count());
						StepOverallProgress();
						dir = Path.GetDirectoryName(Path.Combine("NMMLINK", bkInfo.VirtualModPath));
						if (!string.IsNullOrEmpty(dir))
							Directory.CreateDirectory(Path.Combine(BackupDirectory, dir));

						try
						{
							File.Copy(bkInfo.RealModPath, Path.Combine(BackupDirectory, bkInfo.Directory, bkInfo.ModID, bkInfo.VirtualModPath), true);
						}
						catch (FileNotFoundException)
						{ }

						if (ItemProgress < ItemProgressMaximum)
						{
							ItemMessage = bkInfo.RealModPath;
							StepItemProgress();
						}
					}

					TotalFiles += BackupManager.lstInstalledModFiles.Count + BackupManager.lstInstalledNMMLINKFiles.Count;
				}

				OverallProgressMaximum = BackupManager.lstLooseFiles.Count();
				if ((BackupManager.checkList.Contains(2)) && (BackupManager.lstLooseFiles.Count > 0))
				{
					i = 1;
					foreach (BackupInfo bkInfo in BackupManager.lstLooseFiles)
					{
						if (i < BackupManager.lstLooseFiles.Count())
						{
							ItemMessage = bkInfo.VirtualModPath;
							StepItemProgress();
						}

						OverallMessage = string.Format("Copying " + Path.GetFileName(ModManager.GameMode.PluginDirectory) + " files...{0}/{1}", i++, BackupManager.lstLooseFiles.Count());
						StepOverallProgress();
						dir = Path.GetDirectoryName(Path.Combine(bkInfo.ModID, bkInfo.VirtualModPath));
						if (!string.IsNullOrEmpty(dir))
							Directory.CreateDirectory(Path.Combine(BackupDirectory, bkInfo.Directory, dir));

						try
						{
							File.Copy(bkInfo.RealModPath, Path.Combine(BackupDirectory, bkInfo.Directory, bkInfo.VirtualModPath), true);
						}
						catch (FileNotFoundException)
						{ }
					}

					TotalFiles += BackupManager.lstLooseFiles.Count;
				}

				OverallProgressMaximum = BackupManager.lstModArchives.Count();
				if ((BackupManager.checkList.Contains(3)) && (BackupManager.lstModArchives.Count > 0))
				{
					i = 1;
					foreach (BackupInfo bkInfo in BackupManager.lstModArchives)
					{
						if (i < BackupManager.lstModArchives.Count())
						{
							ItemMessage = bkInfo.VirtualModPath;
							StepItemProgress();
						}

						OverallMessage = string.Format("Copying MOD ARCHIVES...{0}/{1}", i++, BackupManager.lstModArchives.Count());
						StepOverallProgress();
						dir = Path.GetDirectoryName(Path.Combine(bkInfo.Directory, bkInfo.VirtualModPath));
						if (!string.IsNullOrEmpty(dir))
							Directory.CreateDirectory(Path.Combine(BackupDirectory, dir));

						try
						{ 
							File.Copy(bkInfo.RealModPath, Path.Combine(BackupDirectory, bkInfo.Directory, bkInfo.VirtualModPath), true);
						}
						catch (FileNotFoundException)
						{ }
					}

					TotalFiles += BackupManager.lstModArchives.Count;
				}

				byte[] bteLoadOrder = null;
				if (ModManager.GameMode.UsesPlugins)
					bteLoadOrder = PluginManagerVM.ExportLoadOrder();

				string[] strOptionalFiles = null;

				if (ModManager.GameMode.RequiresOptionalFilesCheckOnProfileSwitch)
					if ((PluginManager != null) && ((PluginManager.ActivePlugins != null) && (PluginManager.ActivePlugins.Count > 0)))
						strOptionalFiles = ModManager.GameMode.GetOptionalFilesList(PluginManager.ActivePlugins.Select(x => x.Filename).ToArray());

				IModProfile mprModProfile = AddProfile(null, null, bteLoadOrder, ModManager.GameMode.ModeId, -1, strOptionalFiles, Path.Combine(BackupDirectory, "PROFILE"));

				Directory.CreateDirectory(Path.Combine(BackupDirectory, ModManager.GameMode.Name));

				string installLog = Path.Combine(ModManager.GameMode.GameModeEnvironmentInfo.InstallInfoDirectory, "InstallLog.xml");

				if (File.Exists(installLog))
					File.Copy(installLog, Path.Combine(BackupDirectory, "InstallLog.xml"));

				string startPath = BackupDirectory;
				string zipPath = Path.Combine(EnvironmentInfo.ApplicationPersonalDataFolderPath, "NMM_BACKUP.zip");

				if (File.Exists(zipPath))
					File.Delete(zipPath);

				OverallMessage = "Zipping the Archive...";
				StepOverallProgress();

				string strDateTimeStamp = DateTime.Now.ToString(System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.SortableDateTimePattern);
				strDateTimeStamp = strDateTimeStamp.Replace(":", "");
				strDateTimeStamp = strDateTimeStamp.Replace("-", "");
				strDateTimeStamp = strDateTimeStamp.Replace("T", "-");

				SevenZipCompressor szcCompressor = new SevenZipCompressor(startPath);
				szcCompressor.CompressionLevel = SevenZip.CompressionLevel.Normal;
				szcCompressor.ArchiveFormat = OutArchiveFormat.Zip;
				szcCompressor.FastCompression = false;
				szcCompressor.CompressionMethod = CompressionMethod.Default;
				szcCompressor.CompressionMode = SevenZip.CompressionMode.Create;
				szcCompressor.FileCompressionStarted += new EventHandler<FileNameEventArgs>(compressor_FileCompressionStarted);

				szcCompressor.CompressDirectory(startPath, Path.Combine(SelectedPath, ModManager.GameMode.ModeId + "_NMM_BACKUP_" + strDateTimeStamp + ".zip"), true);

				OverallMessage = "Deleting the leftovers.";
				StepOverallProgress();
				FileUtil.ForceDelete(BackupDirectory);
			}
			else
				return (string.Format("Not enough space on drive: {0} - ({1}Mb required)", drive.Name, ((TotalFileSize / 1024)/ 1024).ToString()));

			return null;
		}

		private bool CheckPathLimit(string p_strBackupDirectory)
		{
			int PathLimit = 248;

			if ((BackupManager.checkList.Contains(0)) && (BackupManager.lstBaseGameFiles.Count > 0))
			{
				foreach (BackupInfo bkInfo in BackupManager.lstBaseGameFiles)
				{
					if (Path.Combine(p_strBackupDirectory, bkInfo.Directory, bkInfo.VirtualModPath).Length > PathLimit)
						return true;
				}
			}

			if ((BackupManager.checkList.Contains(1)) && (BackupManager.lstInstalledModFiles.Count > 0))
			{
				foreach (BackupInfo bkInfo in BackupManager.lstInstalledModFiles)
				{
					if (Path.Combine(p_strBackupDirectory, bkInfo.Directory, bkInfo.ModID, bkInfo.VirtualModPath).Length > PathLimit)
						return true;
				}
			}

			if ((BackupManager.checkList.Contains(2)) && (BackupManager.lstLooseFiles.Count > 0))
			{
				foreach (BackupInfo bkInfo in BackupManager.lstLooseFiles)
				{
					if (Path.Combine(p_strBackupDirectory, bkInfo.Directory, bkInfo.VirtualModPath).Length > PathLimit)
						return true;
				}
			}

			if ((BackupManager.checkList.Contains(3)) && (BackupManager.lstModArchives.Count > 0))
			{
				foreach (BackupInfo bkInfo in BackupManager.lstModArchives)
				{
					if (Path.Combine(p_strBackupDirectory, bkInfo.Directory, bkInfo.VirtualModPath).Length > PathLimit)
						return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Adds the new Profile inside the Backup Folder.
		/// </summary>
		public IModProfile AddProfile(byte[] p_bteModList, byte[] p_bteIniList, byte[] p_bteLoadOrder, string p_strGameModeId, Int32 p_intModCount, string[] p_strOptionalFiles, string p_strBackupDirectory)
		{
			string strId = GetNextId;
			int intNewProfile = 1;
			if (ProfileManager.ModProfiles.Count > 0)
			{
				List<IModProfile> lstNewProfile = ProfileManager.ModProfiles.Where(x => x.Name.IndexOf("Profile") == 0).ToList();
				if ((lstNewProfile != null) && (lstNewProfile.Count > 0))
				{
					List<Int32> lstID = new List<Int32>();
					foreach (IModProfile imp in lstNewProfile)
					{
						string n = imp.Name.Substring(8);
						int i = 0;
						if (int.TryParse(n, out i))
							lstID.Add(Convert.ToInt32(i));
					}
					if (lstID.Count > 0)
					{
						intNewProfile = Enumerable.Range(1, lstID.Max() + 1).Except(lstID).Min();
					}
				}
			}

			string strActiveProfileID = string.Empty;
			string profileName = p_strGameModeId + " Restored Backup " + intNewProfile.ToString();

			ModProfile mprModProfile = new ModProfile(strId, profileName, p_strGameModeId, (p_intModCount < 0 ? VirtualModActivator.ModCount : p_intModCount), false, "", "", "", false, "", "", 0, false);
			ProfileManager.SaveProfile(mprModProfile, p_bteModList, p_bteIniList, p_bteLoadOrder, p_strOptionalFiles, p_strBackupDirectory);
						
			string strLogPath = string.IsNullOrEmpty(strActiveProfileID) ? Path.Combine(ModManager.GameMode.GameModeEnvironmentInfo.InstallInfoDirectory, "Scripted") : Path.Combine(p_strBackupDirectory, strActiveProfileID, "Scripted");
			if (Directory.Exists(strLogPath))
				lock (m_objLock)
					DirectoryCopy(strLogPath, Path.Combine(p_strBackupDirectory, mprModProfile.Id, "Scripted"), true);
			return mprModProfile;
		}

		void compressor_FileCompressionStarted(object sender, FileNameEventArgs e)
		{
			double PercentDone = ((double)FileCounter / TotalFiles) * 100;
			OverallMessage = OverallMessage = "Zipping the Archive..." + PercentDone.ToString("0") + "%";
			FileCounter++;
			StepOverallProgress();
		}
		
		static bool FileEquals(string path1, string path2)
		{
			try
			{
				FileInfo fiPath1 = new FileInfo(path1);
				FileInfo fiPath2 = new FileInfo(path2);

				if(fiPath1.Length == fiPath2.Length)
				{
					byte[] file1 = File.ReadAllBytes(path1);
					byte[] file2 = File.ReadAllBytes(path2);

					for (int i = 0; i < file1.Length; i++)
					{
						if (file1[i] != file2[i])
						{
							return false;
						}
					}
					return true;
				}
				return false;
			}
			catch
			{
				return false;
			}
		}

	}
}
