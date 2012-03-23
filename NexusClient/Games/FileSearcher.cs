using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Util;
using System.Diagnostics;

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

		/// <summary>
		/// Raise when a file has been found.
		/// </summary>
		public event EventHandler<EventArgs<string>> FileFound = delegate { };

		#endregion

		private DateTime m_dteLastUpdate = DateTime.MinValue;

		#region Properties

		/// <summary>
		/// Gets whether or not the task has been asked to terminate.
		/// </summary>
		/// <value>Whether or not the task has been asked to terminate.</value>
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
		/// <param name="e">An <see cref="EventArgs{String}"/> describing the task that was started.</param>
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
				catch (PathTooLongException)
				{
					//how the user has paths that are too long is a bit of a mystery,
					// but given that .NET, and Windows in general, can't handle them,
					// we're going to ignore them
					Trace.TraceInformation("Path too long when getting directories: {0}", strSearchPath);
				}
				catch (DirectoryNotFoundException)
				{
					//doesn't exist so we don't care
					// though I have no idea why we were searching it to begin with
					// possibly the user was manipulating the file system while the search is being executed?
				}
				catch (ArgumentException e)
				{
					Trace.TraceInformation("Argument exception when getting subdirectories: {0}", strSearchPath);
					TraceUtil.TraceException(e);
					Trace.TraceInformation("Ignoring.");
				}
				catch (IOException)
				{
					//not sure what goings on here
					// it seems this can happen when a drive has an unrecognized format
					// there are likely some other unusual cases
					Trace.TraceInformation("IOException while getting subdirectories for: {0}", strSearchPath);
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
				//we need this check, as a drive may have become un-ready whilst we were searching
				if (new DriveInfo(Path.GetPathRoot(p_strPath)).IsReady)
					strHaystackFiles = Directory.GetFiles(p_strPath);
				else
					return;
			}
			catch (UnauthorizedAccessException)
			{
				//we don't have access to the path we are trying to search, so let's bail
				return;
			}
			catch (PathTooLongException)
			{
				//how the user has paths that are too long is a bit of a mystery,
				// but given that .NET, and Windows in general, can't handle them,
				// we're going to ignore them
				Trace.TraceInformation("Path too long: {0}", p_strPath);
				return;
			}
			catch (DirectoryNotFoundException)
			{
				//doesn't exist so we don't care
				// though I have no idea why we were searching it to begin with
				// possibly the user was manipulating the file system while the search is being executed?
				return;
			}
			catch (IOException)
			{
				//not sure what goings on here
				// it seems this can happen when a drive has an unrecognized format
				// there are likely some other unusual cases
				Trace.TraceInformation("IOException while getting files from: {0}", p_strPath);
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
