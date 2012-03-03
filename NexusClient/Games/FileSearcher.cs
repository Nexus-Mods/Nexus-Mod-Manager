using System;
using System.IO;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Util.Collections;
using System.Collections;
using System.Collections.Generic;
using Nexus.Client.Util;
using System.Text.RegularExpressions;

namespace Nexus.Client.Games
{
	/// <summary>
	/// This task searched the user's computer for the specified installation path.
	/// </summary>
	/// <remarks>
	/// The task is given specific file names for which to search that indicate the
	/// installation path being sought.
	/// </remarks>
	public class FileSearcher : ThreadedBackgroundTask
	{
		#region Events

		public event EventHandler<EventArgs<string>> FileFound = delegate { };

		#endregion

		private DateTime m_dteLastUpdate = DateTime.MinValue;
		
		#region Properties

		protected bool EndTaskRequested
		{
			get
			{
				return (Status == TaskStatus.Complete) || (Status == TaskStatus.Cancelling);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		public FileSearcher()
		{
		}

		#endregion

		#region Event Raising

		/// <summary>
		/// Raises the <see cref="FileFound"/> event.
		/// </summary>
		/// <param name="e">An <see cref="EventArgs{string}"/> describing the task that was started.</param>
		protected virtual void OnFileFound(EventArgs<string> e)
		{
			FileFound(this, e);
		}

		/// <summary>
		/// Raises the <see cref="FileFound"/> event.
		/// </summary>
		/// <param name="p_strFoundPath">The path of the file that was found.</param>
		protected void OnFileFound(string p_strFoundPath)
		{
			OnFileFound(new EventArgs<string>(p_strFoundPath));
		}

		#endregion

		/// <summary>
		/// Finds all files matching the given patterns.
		/// </summary>
		/// <param name="p_strSearchFiles">The file patterns to search for.</param>
		public void Find(string[] p_strSearchFiles)
		{
			ShowItemProgress = false;
			ShowOverallProgressAsMarquee = true;

			Start(p_strSearchFiles);
		}

		/// <summary>
		/// The delegate that is called to start the backgound task.
		/// </summary>
		/// <param name="p_objArgs">the file patterns to search for.</param>
		/// <returns>Always <c>null</c>.</returns>
		protected override object DoWork(object[] p_objArgs)
		{
			string[] strSearchFiles = (string[])p_objArgs;
			Regex[] rgxPatterns = new Regex[strSearchFiles.Length];
			string strSeparatorChar = Path.DirectorySeparatorChar.Equals('\\') ? @"\\" : Path.DirectorySeparatorChar.ToString();
			for (Int32 i = 0; i < strSearchFiles.Length; i++)
				rgxPatterns[i] = new Regex(strSearchFiles[i].Replace(".", "\\.").Replace("*", ".*").Replace(Path.AltDirectorySeparatorChar.ToString(), strSeparatorChar), RegexOptions.IgnoreCase);
			DriveInfo[] difDrives = DriveInfo.GetDrives();

			Queue<string> queSearchPaths = new Queue<string>();
			foreach (DriveInfo difDrive in difDrives)
				if ((difDrive.DriveType != DriveType.CDRom) && difDrive.IsReady)
					queSearchPaths.Enqueue(difDrive.Name);
			Int32 intFOlderCnt = 0;
			while (queSearchPaths.Count > 0)
			{
				if (EndTaskRequested)
					return null;
				string strSearchPath = queSearchPaths.Dequeue();
				intFOlderCnt++;
				Search(strSearchPath, rgxPatterns);
				if (EndTaskRequested)
					return null;
				try
				{
					foreach (string strSubdirectory in Directory.GetDirectories(strSearchPath))
					{
						if (Path.GetFileName(strSubdirectory).StartsWith("$"))
							continue;
						queSearchPaths.Enqueue(strSubdirectory);
					}
				}
				catch (UnauthorizedAccessException)
				{
					//we don't have access to the path we are trying to search, so do nothing
				}
			}
			return null;
		}

		/// <summary>
		/// This recursively searches the specified directory for the search files.
		/// </summary>
		/// <param name="p_strPath">The path of the direcotry to recursively search.</param>
		/// <param name="p_rgxPatterns">The file patterns to search for.</param>
		protected void Search(string p_strPath, Regex[] p_rgxPatterns)
		{
			if ((DateTime.Now - m_dteLastUpdate).TotalMilliseconds > 100)
			{
				OverallMessage = p_strPath;
				m_dteLastUpdate = DateTime.Now;
			}
			string[] strHaystackFiles = null;
			try
			{
				strHaystackFiles = Directory.GetFiles(p_strPath);
			}
			catch (UnauthorizedAccessException)
			{
				//we don't have access to the path we are trying to search, so let's bail
				return;
			}
			for (Int32 i = 0; i < strHaystackFiles.Length; i++)
			{
				if (EndTaskRequested)
					return;
				for (Int32 j = 0; j < p_rgxPatterns.Length; j++)
				{
					if (p_rgxPatterns[j].IsMatch(strHaystackFiles[i]))
						OnFileFound(strHaystackFiles[i]);
				}
			}
		}
	}
}
