using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using SevenZip;
using Nexus.Client.Util;

namespace Nexus.Client.ModManagement
{
	public class BackupManager
	{
		private ModManager ModManager = null;
		private ProfileManager ProfileManager = null;

		public List<BackupInfo> lstLooseFiles = new List<BackupInfo>();
		public List<BackupInfo> lstInstalledModFiles = new List<BackupInfo>();
		public List<BackupInfo> lstInstalledNMMLINKFiles = new List<BackupInfo>();
		public List<BackupInfo> lstBaseGameFiles = new List<BackupInfo>();
		public List<BackupInfo> lstModArchives = new List<BackupInfo>();

		public long LooseFilesSize = 0;
		public long InstalledModFileSize = 0;
		public long InstalledNMMLINKFileSize = 0;
		public long BaseGameFilesSize = 0;
		public long ModArchivesSize = 0;
		public long TotalFileSize = 0;

		public string strLooseFilesSize = string.Empty;
		public string strInstalledModFileSize = string.Empty;
		public string strBaseGameFilesSize = string.Empty;
		public string strModArchivesSize = string.Empty;

		public List<int> checkList = null;

		public List<ArchiveFileInfo> GameModeNameCheck = null;
		public bool booPluginPath = false;
		public bool booVirtualPath = false;
		public bool booValidArchive = true;
		public float RestoredFiles = 0;

		private List<string> lstDirectories = null;

		public BackupManager(ModManager p_mmModManager, ProfileManager p_pmProfileManager)
		{
			ModManager = p_mmModManager;
			ProfileManager = p_pmProfileManager;

			checkList = new List<int>();
			lstDirectories = new List<string>();
			lstDirectories.Add(Path.Combine(ModManager.CurrentGameModeModDirectory, "ReadMe"));
			lstDirectories.Add(ModManager.VirtualModActivator.VirtualPath);
			lstDirectories.Add(ProfileManager.ProfileManagerPath);
			lstDirectories.Add(ModManager.GameMode.GameModeEnvironmentInfo.ModDownloadCacheDirectory);
			lstDirectories.Add(ModManager.GameMode.GameModeEnvironmentInfo.ModCacheDirectory);
			lstDirectories.Add(Path.Combine(ModManager.CurrentGameModeModDirectory, "categories"));
			lstDirectories.Add(ModManager.VirtualModActivator.HDLinkFolder); 
		}

		public void Initialize()
		{
			if (lstBaseGameFiles != null)
				lstBaseGameFiles.Clear();
			if (lstInstalledModFiles != null)
				lstInstalledModFiles.Clear();
			if (lstLooseFiles != null)
				lstLooseFiles.Clear();
			if (lstModArchives != null)
				lstModArchives.Clear();

			checkList.Clear();
		}


		/// <summary>
		/// The Mods Installation Files check.
		/// </summary>
		public void CheckModsInstallationFiles()
		{
			FileInfo fInfo = null;
			BackupInfo bInfo = null;

			if((ModManager.VirtualModActivator != null) && (ModManager.VirtualModActivator.Initialized))
			{
				if (ModManager.VirtualModActivator.VirtualLinks.Count > 0)
				{
					foreach (IVirtualModLink link in ModManager.VirtualModActivator.VirtualLinks)
					{
						if (File.Exists(Path.Combine(ModManager.VirtualModActivator.VirtualFoder, link.RealModPath)))
						{
							fInfo = new FileInfo(Path.Combine(ModManager.VirtualModActivator.VirtualFoder, link.RealModPath));
							InstalledModFileSize = InstalledModFileSize + fInfo.Length;
							bInfo = new BackupInfo(link.VirtualModPath, Path.Combine(ModManager.VirtualModActivator.VirtualFoder, link.RealModPath), !string.IsNullOrEmpty(link.ModInfo.DownloadId) ? link.ModInfo.DownloadId : Path.GetFileNameWithoutExtension(link.ModInfo.ModFileName), "VIRTUAL INSTALL", fInfo.Length);
							lstInstalledModFiles.Add(bInfo);
						}
					}

					if (File.Exists(Path.Combine(ModManager.VirtualModActivator.VirtualFoder, "VirtualModConfig.xml")))
					{
						fInfo = new FileInfo(Path.Combine(ModManager.VirtualModActivator.VirtualFoder, "VirtualModConfig.xml"));
						InstalledModFileSize = InstalledModFileSize + fInfo.Length;
						bInfo = new BackupInfo("VirtualModConfig.xml", Path.Combine(ModManager.VirtualModActivator.VirtualFoder, "VirtualModConfig.xml"), "", "VIRTUAL INSTALL", fInfo.Length);
						lstInstalledModFiles.Add(bInfo);
					}

					string OverwriteSource = Path.Combine(ModManager.VirtualModActivator.VirtualPath, "_overwrites");
					if (Directory.Exists(OverwriteSource))
					{
						string[] OverwriteFiles = Directory.GetFiles(OverwriteSource, "*.*", SearchOption.AllDirectories);
						string fileName = string.Empty;
						string destFile = string.Empty;

						if (OverwriteFiles.Count() > 0)
						{
							foreach (string file in OverwriteFiles)
							{
								string[] result = file.Split(new string[] { ModManager.VirtualModActivator.VirtualFoder + Path.DirectorySeparatorChar }, StringSplitOptions.None);
								fInfo = new FileInfo(file);
								InstalledModFileSize = InstalledModFileSize + fInfo.Length;
								lstInstalledModFiles.Add(new BackupInfo(result[1], file, "", "VIRTUAL INSTALL", fInfo.Length));
							}
						}
					}
				}
			}
			else
			{
				foreach (Mods.IMod mod in ModManager.InstallationLog.ActiveMods)
				{
					IList<string> fileList = ModManager.InstallationLog.GetInstalledModFiles(mod);

					foreach (string file in fileList)
					{
						fInfo = new FileInfo(Path.Combine(ModManager.GameMode.InstallationPath, file));
						InstalledModFileSize = InstalledModFileSize + fInfo.Length;
						lstInstalledModFiles.Add(new BackupInfo(file, Path.Combine(ModManager.GameMode.InstallationPath, file), "", Path.GetFileName(ModManager.GameMode.PluginDirectory), fInfo.Length));
					}
				}
			}

			if (ModManager.VirtualModActivator.MultiHDMode)
			{
				string[] fileList = Directory.GetFiles(ModManager.VirtualModActivator.HDLinkFolder, "*.*", SearchOption.AllDirectories);

				foreach (string file in fileList)
				{
					string[] result = file.Split(new string[] { ModManager.VirtualModActivator.HDLinkFolder + Path.DirectorySeparatorChar }, StringSplitOptions.None);
					fInfo = new FileInfo(file);
					InstalledNMMLINKFileSize = InstalledNMMLINKFileSize + fInfo.Length;
					lstInstalledNMMLINKFiles.Add(new BackupInfo(result[1], file, "", Path.GetFileName(ModManager.VirtualModActivator.HDLinkFolder), fInfo.Length));
				}
			}
		}

		public void CheckRestoreFiles(string p_strFileName)
		{
			if (Path.GetExtension(p_strFileName) != ".zip")
				booValidArchive = false;
			else
			{

				using (SevenZipExtractor szeExtractor = new SevenZipExtractor(p_strFileName))
				{
					ReadOnlyCollection<ArchiveFileInfo> lstArchiveFiles = szeExtractor.ArchiveFileData;
					GameModeNameCheck = lstArchiveFiles.Where(x => x.FileName.Equals(ModManager.GameMode.Name, StringComparison.OrdinalIgnoreCase)).ToList();

					if (GameModeNameCheck.Count() > 0)
					{
						string checkPluginPath = Path.GetFileName(ModManager.GameMode.PluginDirectory) + Path.DirectorySeparatorChar;
						string checkVirtualPath = "VIRTUAL INSTALL" + Path.DirectorySeparatorChar;

						foreach (ArchiveFileInfo ArchiveFile in lstArchiveFiles)
						{
							if (ArchiveFile.FileName.StartsWith(checkPluginPath))
								booPluginPath = true;
							if (ArchiveFile.FileName.StartsWith(checkVirtualPath))
								booVirtualPath = true;

							RestoredFiles = RestoredFiles + (ArchiveFile.Size / 1024f) / 1024f;
						}
					}
				}
			}
		}

		public void CheckModArchives()
		{
			string[] modCacheArchives = null;
			string[] modRootArchives = null;

			FileInfo fInfo = null;
			if (Directory.Exists(ModManager.GameMode.GameModeEnvironmentInfo.ModDirectory))
			{
				var DirectoriesList = Directory.EnumerateDirectories(ModManager.GameMode.GameModeEnvironmentInfo.ModDirectory, "*.*", SearchOption.AllDirectories).ToList();
				
				foreach (string DirRemoved in lstDirectories)
				{
					if(!string.IsNullOrEmpty(DirRemoved))
						DirectoriesList.RemoveAll(x=> x.StartsWith(DirRemoved, StringComparison.InvariantCultureIgnoreCase));
				}

				foreach (string dir in DirectoriesList)
				{
					if(!lstDirectories.Contains(dir))
					{
						var modArchives = Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly);

						foreach (string archive in modArchives)
						{
							if (Archive.IsArchive(archive))
							{
								string[] result = archive.Split(new string[] { ModManager.GameMode.GameModeEnvironmentInfo.ModDirectory + Path.DirectorySeparatorChar }, StringSplitOptions.None);
								fInfo = new FileInfo(archive);
								ModArchivesSize = ModArchivesSize + fInfo.Length;
								lstModArchives.Add(new BackupInfo(result[1], archive, "", Path.GetFileName(ModManager.GameMode.GameModeEnvironmentInfo.ModDirectory), fInfo.Length));
							}
						}
					}
				}

				if (Directory.Exists(ModManager.GameMode.GameModeEnvironmentInfo.ModDirectory))
					modRootArchives = Directory.GetFiles(ModManager.GameMode.GameModeEnvironmentInfo.ModDirectory, "*.*", SearchOption.TopDirectoryOnly);

				if (modRootArchives != null)
				{
					foreach (string archive in modRootArchives)
					{
						if (Archive.IsArchive(archive))
						{
							fInfo = new FileInfo(Path.Combine(ModManager.GameMode.GameModeEnvironmentInfo.ModDirectory, archive));
							ModArchivesSize = ModArchivesSize + fInfo.Length;
							lstModArchives.Add(new BackupInfo(Path.GetFileName(archive), Path.Combine(ModManager.GameMode.GameModeEnvironmentInfo.ModDirectory, archive), "", Path.GetFileName(ModManager.GameMode.GameModeEnvironmentInfo.ModDirectory), fInfo.Length));
						}
					}
				}
				if (Directory.Exists(ModManager.GameMode.GameModeEnvironmentInfo.ModCacheDirectory))
					modCacheArchives = Directory.GetFiles(ModManager.GameMode.GameModeEnvironmentInfo.ModCacheDirectory, "*.*", SearchOption.AllDirectories);

				if (modCacheArchives != null)
				{
					foreach (string file in modCacheArchives)
					{
						string[] result = file.Split(new string[] { ModManager.GameMode.GameModeEnvironmentInfo.ModCacheDirectory + Path.DirectorySeparatorChar }, StringSplitOptions.None);
						fInfo = new FileInfo(Path.Combine(ModManager.GameMode.GameModeEnvironmentInfo.ModCacheDirectory, file));
						ModArchivesSize = ModArchivesSize + fInfo.Length;
						lstModArchives.Add(new BackupInfo(result[1], file, "", Path.GetFileName(ModManager.GameMode.GameModeEnvironmentInfo.ModCacheDirectory), fInfo.Length));
					}
				}
			}
		}

		/// <summary>
		/// The Loose Files check.
		/// </summary>
		public void CheckLooseFiles(bool booPurging)
		{
			FileInfo fInfo = null;
			string[] result = null;

			if (Directory.Exists(ModManager.GameMode.PluginDirectory))
			{
				string[] DATAfiles = Directory.GetFiles(ModManager.GameMode.PluginDirectory, "*.*", SearchOption.AllDirectories);

				if (lstBaseGameFiles.Count == 0)
					CheckBaseGameFiles();

				if (lstInstalledModFiles.Count == 0)
					CheckModsInstallationFiles();
				
				foreach (string file in DATAfiles)
				{
					if (!".lnk".Equals(Path.GetExtension(file).ToLowerInvariant()) || booPurging)
					{
						if ((lstBaseGameFiles.Count > 0) || (lstInstalledModFiles.Count > 0))
						{
							var checkedBaseGameFiles = lstBaseGameFiles.Where(x => x.RealModPath.Equals(file, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();

							if (checkedBaseGameFiles == null)
							{
								if ((ModManager.VirtualModActivator != null) && (ModManager.VirtualModActivator.Initialized))
									result = file.Split(new string[] { ModManager.GameMode.PluginDirectory + Path.DirectorySeparatorChar }, StringSplitOptions.None);
								else
									result = file.Split(new string[] { ModManager.GameMode.InstallationPath + Path.DirectorySeparatorChar }, StringSplitOptions.None);

								var checkedModsInstallationFiles = lstInstalledModFiles.Where(x => x.VirtualModPath.Equals(result[1], StringComparison.OrdinalIgnoreCase)).FirstOrDefault();

								if (checkedModsInstallationFiles == null)
								{
									fInfo = new FileInfo(file);
									LooseFilesSize = LooseFilesSize + fInfo.Length;
									lstLooseFiles.Add(new BackupInfo(result[1], file, "", Path.GetFileName(ModManager.GameMode.PluginDirectory), fInfo.Length));
								}
							}
						}
						else
						{
							if ((ModManager.VirtualModActivator != null) && (ModManager.VirtualModActivator.Initialized))
								result = file.Split(new string[] { ModManager.GameMode.PluginDirectory + Path.DirectorySeparatorChar }, StringSplitOptions.None);
							else
								result = file.Split(new string[] { ModManager.GameMode.InstallationPath + Path.DirectorySeparatorChar }, StringSplitOptions.None);

							fInfo = new FileInfo(file);
							LooseFilesSize = LooseFilesSize + fInfo.Length;
							lstLooseFiles.Add(new BackupInfo(result[1], file, "", Path.GetFileName(ModManager.GameMode.PluginDirectory), fInfo.Length));
						}
					}
				}
			}
		}
		
		/// <summary>
		/// The Base Game Files check.
		/// </summary>
		public void CheckBaseGameFiles()
		{
			FileInfo fInfo = null;
			if (ModManager.GameMode.BaseGameFiles != null)
			{
				string[] fileList = ModManager.GameMode.BaseGameFiles.Split(new string[] { Environment.NewLine }, StringSplitOptions.None);
				foreach (string file in fileList)
				{
					if (File.Exists(Path.Combine(ModManager.GameMode.PluginDirectory, file)))
					{
						fInfo = new FileInfo(Path.Combine(ModManager.GameMode.PluginDirectory, file));
						BaseGameFilesSize = BaseGameFilesSize + fInfo.Length;
						lstBaseGameFiles.Add(new BackupInfo(file, Path.Combine(ModManager.GameMode.PluginDirectory, file), "", Path.GetFileName(ModManager.GameMode.PluginDirectory), fInfo.Length));
					}
				}
			}
		}
	}
}
