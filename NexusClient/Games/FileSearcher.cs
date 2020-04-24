namespace Nexus.Client.Games
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text.RegularExpressions;
    
    using Nexus.Client.BackgroundTasks;
    using Nexus.Client.Util;

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

		private DateTime _lastUpdate = DateTime.MinValue;

		#region Properties

		/// <summary>
		/// Gets whether or not the task has been asked to terminate.
		/// </summary>
		/// <value>Whether or not the task has been asked to terminate.</value>
		protected bool EndTaskRequested => Status == TaskStatus.Complete || Status == TaskStatus.Cancelling;

        #endregion

		#region Event Raising

		/// <summary>
		/// Raises the <see cref="FileFound"/> event.
		/// </summary>
		/// <param name="args">An <see cref="EventArgs{String}"/> describing the task that was started.</param>
		protected virtual void OnFileFound(EventArgs<string> args)
		{
			FileFound(this, args);
		}

		/// <summary>
		/// Raises the <see cref="FileFound"/> event.
		/// </summary>
		/// <param name="foundPath">The path of the file that was found.</param>
		protected void OnFileFound(string foundPath)
		{
			OnFileFound(new EventArgs<string>(foundPath));
		}

		#endregion

		/// <summary>
		/// Finds all files matching the given patterns.
		/// </summary>
		/// <param name="searchFiles">The file patterns to search for.</param>
		public void Find(string[] searchFiles)
		{
			ShowItemProgress = false;
			ShowOverallProgressAsMarquee = true;

			Start(searchFiles);
		}

		/// <summary>
		/// The delegate that is called to start the background task.
		/// </summary>
		/// <param name="args">the file patterns to search for.</param>
		/// <returns>Always <c>null</c>.</returns>
		protected override object DoWork(object[] args)
		{
			var strSearchFiles = (string[])args;
			var rgxPatterns = new Regex[strSearchFiles.Length];
			var strSeparatorChar = Path.DirectorySeparatorChar.Equals('\\') ? @"\\" : Path.DirectorySeparatorChar.ToString();
			
            for (var i = 0; i < strSearchFiles.Length; i++)
            {
                rgxPatterns[i] = new Regex(strSearchFiles[i].Replace(@"\", @"\\").Replace(".", "\\.").Replace("*", ".*").Replace(Path.AltDirectorySeparatorChar.ToString(), strSeparatorChar), RegexOptions.IgnoreCase);
            }

            var difDrives = DriveInfo.GetDrives();

			var queSearchPaths = new Queue<string>();
			
            foreach (var difDrive in difDrives)
            {
                if (difDrive.DriveType == DriveType.Fixed && difDrive.IsReady)
                {
                    queSearchPaths.Enqueue(difDrive.Name);
                }
            }

            while (queSearchPaths.Count > 0)
			{
				if (EndTaskRequested)
                {
                    return null;
                }

                var searchPath = queSearchPaths.Dequeue();
                
                if (!FileUtil.IsValidPath(searchPath))
                {
                    continue;
                }

                Search(searchPath, rgxPatterns);
				
                if (EndTaskRequested)
                {
                    return null;
                }

                try
				{
					foreach (var subDirectory in Directory.GetDirectories(searchPath))
					{
						if (!string.IsNullOrEmpty(subDirectory) && Path.GetFileName(subDirectory).StartsWith("$"))
                        {
                            continue;
                        }

                        queSearchPaths.Enqueue(subDirectory);
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
					Trace.TraceInformation("Path too long when getting directories: {0}", searchPath);
				}
				catch (DirectoryNotFoundException)
				{
					//doesn't exist so we don't care
					// though I have no idea why we were searching it to begin with
					// possibly the user was manipulating the file system while the search is being executed?
				}
				catch (ArgumentException e)
				{
					Trace.TraceInformation("Argument exception when getting subdirectories: {0}", searchPath);
					TraceUtil.TraceException(e);
					Trace.TraceInformation("Ignoring.");
				}
				catch (IOException)
				{
					//not sure what goings on here
					// it seems this can happen when a drive has an unrecognized format
					// there are likely some other unusual cases
					Trace.TraceInformation("IOException while getting subdirectories for: {0}", searchPath);
				}
				catch (NotSupportedException)
				{
		 			// not sure what goings on here
					// it seems this can happen when a drive has folders created some special way under linux
					// for example PlayOnLinux. F:\PlayOnLinux\logs\Diablo II : Lord Of Destruction_1318695546
					Trace.TraceInformation("NotSupportedException while getting subdirectories for: {0}", searchPath);
				}
			}

			return null;
		}

		/// <summary>
		/// This recursively searches the specified directory for the search files.
		/// </summary>
		/// <param name="path">The path of the directory to recursively search.</param>
		/// <param name="patterns">The file patterns to search for.</param>
		protected void Search(string path, Regex[] patterns)
		{
			if ((DateTime.Now - _lastUpdate).TotalMilliseconds > 100)
			{
				OverallMessage = path;
				_lastUpdate = DateTime.Now;
			}
			
            string[] haystackFiles = null;
			
            try
			{
				//we need this check, as a drive may have become un-ready whilst we were searching
				if (!string.IsNullOrEmpty(path) && new DriveInfo(Path.GetPathRoot(path)).IsReady)
                {
                    haystackFiles = Directory.GetFiles(path);
                }
                else
                {
                    return;
                }
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
				Trace.TraceInformation("Path too long: {0}", path);
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
				Trace.TraceInformation("IOException while getting files from: {0}", path);
			}
			catch (ArgumentException)
			{
				//There's something wrong with the path, looks like a drive or UNC name , so let's bail
				return;
			}
			catch (NotSupportedException)
			{
				//There's something wrong with the path, like an absolute path appended to another absolute path.
				Trace.TraceInformation("NotSupportedException while getting files from: {0}", path);
				return;
			}
			
            if (haystackFiles == null)
            {
                return;
            }

            foreach (var file in haystackFiles)
            {
                if (EndTaskRequested)
                {
                    return;
                }

                foreach (var pattern in patterns)
                {
                    if (pattern.IsMatch(file))
                    {
                        OnFileFound(file);
                    }
                }
            }
		}
	}
}
	