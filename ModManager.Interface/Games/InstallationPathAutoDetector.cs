using System;
using System.IO;
using Nexus.Client.Util;
using Nexus.Client.BackgroundTasks;

namespace Nexus.Client.Games
{
	/// <summary>
	/// This task searched the user's computer for the specified installation path.
	/// </summary>
	/// <remarks>
	/// The task is given specific file names for which to search that indicate the
	/// installation path being sought.
	/// </remarks>
	public class InstallationPathAutoDetector : ThreadedBackgroundTask
	{
		private System.Func<string, bool> ConfirmFoundInstallationPath;
		private Set<string> m_setSearchedFolders = new Set<string>(StringComparer.OrdinalIgnoreCase);
		private Set<string> m_setSkipFolders = new Set<string>(StringComparer.OrdinalIgnoreCase);
		
		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_fncConfirmFoundInstallationPath">The delegate to call to confirm if a found path
		/// is correct.</param>
		public InstallationPathAutoDetector(System.Func<string, bool> p_fncConfirmFoundInstallationPath)
		{
			ConfirmFoundInstallationPath = p_fncConfirmFoundInstallationPath ?? (x => true);
		}

		#endregion

		/// <summary>
		/// Finds the installation path.
		/// </summary>
		/// <param name="p_strSearchFiles">The files to search for when auto-detecting.</param>
		public void Detect(string[] p_strSearchFiles)
		{
			OverallProgressStepSize = 1;
			ShowItemProgress = true;
			ShowItemProgressAsMarquee = true;

			Start(p_strSearchFiles);
		}

		/// <summary>
		/// The delegate that is called to start the backgound task.
		/// </summary>
		/// <param name="p_objArgs">the files to search for.</param>
		/// <returns>The auto-detected installation path.</returns>
		protected override object DoWork(object[] p_objArgs)
		{
			string[] strSearchFiles = (string[])p_objArgs;
			DriveInfo[] difDrives = DriveInfo.GetDrives();

			OverallProgressMaximum = difDrives.Length * 2;
			string strFound = null;
			string strSystemRoot = Environment.ExpandEnvironmentVariables("%SYSTEMROOT%");
			for (Int32 i = 0; i < 2; i++)
			{
				if (!"%SYSTEMROOT%".Equals(strSystemRoot))
				{
					if (i == 0)
						m_setSkipFolders.Add(strSystemRoot);
					else
						m_setSkipFolders.Remove(strSystemRoot);
				}
				foreach (DriveInfo difDrive in difDrives)
				{
					if (Status == TaskStatus.Cancelling)
						return null;
					OverallMessage = String.Format("Searching {0} ({1})...", difDrive.Name, (i == 0) ? "Quick Scan" : "Deep Search");
					if (difDrive.DriveType != DriveType.CDRom)
						strFound = SearchToDepth(difDrive.Name, strSearchFiles, (i == 0) ? 3 : -1, 0);
					StepOverallProgress();
					if (!String.IsNullOrEmpty(strFound))
						return strFound;
				}
			}
			return strFound;
		}

		/// <summary>
		/// This recursively searches the specified directory for the search files.
		/// </summary>
		/// <param name="p_strPath">The path of the direcotry to recursively search.</param>
		/// <param name="p_strSearchFiles">The files to search for when auto-detecting.</param>
		/// <param name="p_intMaxDepth">The depth to hich the search for the search files.</param>
		/// <param name="p_intDepth">The current search depth.</param>
		protected string SearchToDepth(string p_strPath, string[] p_strSearchFiles, Int32 p_intMaxDepth, Int32 p_intDepth)
		{
			if ((p_intMaxDepth > -1) && (p_intDepth > p_intMaxDepth) || m_setSkipFolders.Contains(p_strPath))
				return null;
			ItemMessage = p_strPath;
			if (!m_setSearchedFolders.Contains(p_strPath))
			{
				m_setSearchedFolders.Add(p_strPath);
				foreach (string strSearchFile in p_strSearchFiles)
				{
					if (Status == TaskStatus.Cancelling)
						return null;
					try
					{
						string[] strFoundFiles = Directory.GetFiles(p_strPath, strSearchFile, SearchOption.TopDirectoryOnly);
						foreach (string strFoundFile in strFoundFiles)
							if (ConfirmFoundInstallationPath(Path.GetDirectoryName(strFoundFile)))
								return Path.GetDirectoryName(strFoundFile);
					}
					catch (UnauthorizedAccessException)
					{
						//we don't have access to the path we are trying to search, so let's bail
						return null;
					}
				}
			}
			try
			{
				string[] strDirectories = Directory.GetDirectories(p_strPath);
				foreach (string strDirectory in strDirectories)
				{
					if (Status == TaskStatus.Cancelling)
						return null;
					if (Path.GetFileName(strDirectory).StartsWith("$"))
						continue;
					string strFound = SearchToDepth(strDirectory, p_strSearchFiles, p_intMaxDepth, p_intDepth + 1);
					if (!String.IsNullOrEmpty(strFound))
						return strFound;
				}
			}
			catch (UnauthorizedAccessException)
			{
				//we don't have access to the path we are trying to search, so let's bail
				return null;
			}
			return null;
		}
	}
}
