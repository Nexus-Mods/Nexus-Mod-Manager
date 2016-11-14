using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Timers;
using Nexus.Client.UI;
using Nexus.Client.ModRepositories;
using Nexus.Client.Util.Downloader;
using Nexus.Client.BackgroundTasks;

namespace Nexus.Client.DownloadManagement
{
	/// <summary>
	/// Downloads a file.
	/// </summary>
	public class FileDownloadTask : BackgroundTask, IDisposable
	{
		/// <summary>
		/// Stores the state of the task, so it can be resumed.
		/// </summary>
		private class State
		{
			#region Properties

			/// <summary>
			/// Gets whether the file is being downloaded asynchronously.
			/// </summary>
			/// <value>Whether the file is being downloaded asynchronously.</value>
			public bool IsAsync { get; private set; }

			/// <summary>
			/// Gets the URL of the file to download.
			/// </summary>
			/// <value>The URL of the file to download.</value>
			public Uri URL { get; private set; }

			/// <summary>
			/// Gets the list of cookies that should be sent in the request to download the file.
			/// </summary>
			/// <value>The list of cookies that should be sent in the request to download the file.</value>
			public Dictionary<string, string> Cookies { get; private set; }

			/// <summary>
			/// Gets the path to which to save the file.
			/// </summary>
			/// <remarks>
			/// If <paramref name="UseDefaultFileName"/> is <c>false</c>, this value should be a complete
			/// path, including filename. If <paramref name="UseDefaultFileName"/> is <c>true</c>,
			/// this value should be the directory in which to save the file.
			/// </remarks>
			/// <value>The path to which to save the file.</value>
			public string SavePath { get; private set; }

			/// <summary>
			/// Gets whether to use the file name suggested by the server.
			/// </summary>
			/// <value>Whether to use the file name suggested by the server.</value>
			public bool UseDefaultFileName { get; private set; }

			#endregion

			#region Constructors

			/// <summary>
			/// A simple constructor that initializes the object with the given values.
			/// </summary>
			/// <param name="p_booIsAsync">Whether the file is being downloaded asynchronously.</param>
			/// <param name="p_uriURL">The URL of the file to download.</param>
			/// <param name="p_dicCookies">A list of cookies that should be sent in the request to download the file.</param>
			/// <param name="p_strSavePath">The path to which to save the file.
			/// If <paramref name="p_booUseDefaultFileName"/> is <c>false</c>, this value should be a complete
			/// path, including filename. If <paramref name="p_booUseDefaultFileName"/> is <c>true</c>,
			/// this value should be the directory in which to save the file.</param>
			/// <param name="p_booUseDefaultFileName">Whether to use the file name suggested by the server.</param>
			public State(bool p_booIsAsync, Uri p_uriURL, Dictionary<string, string> p_dicCookies, string p_strSavePath, bool p_booUseDefaultFileName)
			{
			}

			#endregion
		}

		private const string m_strMessageFormat = "Downloading {0} ({1:f0}:{2:d2} left - {3} KB/s)";
		private string m_strUserAgent = "";
		private Int32 m_intMaxConnections = 4;
		private Int32 m_intMinBlockSize = 1000 * 1024;
		private Int32 m_intRetries = 3;
		private Int32 m_intRetryInterval = 10000;
		private System.Timers.Timer m_tmrUpdater = new System.Timers.Timer(1000);
		private FileDownloader m_fdrDownloader = null;
		private AutoResetEvent m_areWaitForDownload = null;
		private State m_steState = null;

		#region Properties

		/// <summary>
		/// Gets the mod repository from which to get mods and mod metadata.
		/// </summary>
		/// <value>The mod repository from which to get mods and mod metadata.</value>
		protected IModRepository ModRepository { get; private set; }

		/// <summary>
		/// Gets whether the task supports pausing.
		/// </summary>
		/// <value>Thether the task supports pausing.</value>
		public override bool SupportsPause
		{
			get
			{
				return true;
			}
		}

		/// <summary>
		/// Gets whether the task supports retrying.
		/// </summary>
		/// <value>Thether the task supports retrying.</value>
		public override bool SupportsRetry
		{
			get
			{
				return true;
			}
		}

		/// <summary>
		/// Gets whether the task supports queuing.
		/// </summary>
		/// <value>Thether the task supports queuing.</value>
		public override bool SupportsQueue
		{
			get
			{
				return true;
			}
		}

		/// <summary>
		/// Gets the download speed, in bytes per second.
		/// </summary>
		/// <value>The download speed, in bytes per second.</value>
		public Int32 DownloadSpeed
		{
			get
			{
				if (m_fdrDownloader == null)
					return 0;
				return m_fdrDownloader.DownloadSpeed;
			}
		}

		/// <summary>
		/// Gets the number of currently active download threads.
		/// </summary>
		/// <value>The number of currently active download threads.</value>
		public override Int32 ActiveThreads
		{
			get
			{
				if (m_fdrDownloader == null)
					return 0;
				return m_fdrDownloader.NumberOfActiveDownloaders;
			}
		}

		/// <summary>
		/// Gets the time remaining to download the file.
		/// </summary>
		/// <value>The time remaining to download the file.</value>
		public TimeSpan TimeRemaining
		{
			get
			{
				if (m_fdrDownloader == null)
					return TimeSpan.MinValue;
				return m_fdrDownloader.TimeRemaining;
			}
		}

		/// <summary>
		/// Gets the number of bytes that have been previously downloaded.
		/// </summary>
		/// <value>The number of bytes that have been previously downloaded.</value>
		public UInt64 ResumedByteCount
		{
			get
			{
				if (m_fdrDownloader == null)
					return 0;
				return m_fdrDownloader.ResumedByteCount;
			}
		}

		/// <summary>
		/// Gets the current error code, if anything wrong happened.
		/// </summary>
		/// <value>The current error code, if anything wrong happened.</value>
		public string ErrorCode { get; private set; }

		/// <summary>
		/// Gets the current error info, if anything wrong happened.
		/// </summary>
		/// <value>The current error info, if anything wrong happened.</value>
		public string ErrorInfo { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_intMaxConnections">The maximum number of connections to use to download the file.</param>
		/// <param name="p_intMinBlockSize">The minimum block size that should be downloaded at once. This should
		/// ideally be some mulitple of the available bandwidth.</param>
		/// <param name="p_strUserAgent">The current User Agent.</param>
		public FileDownloadTask(IModRepository p_mmrModRepository, Int32 p_intMaxConnections, Int32 p_intMinBlockSize, string p_strUserAgent)
		{
			m_intMaxConnections = p_intMaxConnections;
			m_intMinBlockSize = p_intMinBlockSize;
			m_strUserAgent = p_strUserAgent;
			m_tmrUpdater.Elapsed += new ElapsedEventHandler(Updater_Elapsed);
			ModRepository = p_mmrModRepository;
		}

		#endregion

		/// <summary>
		/// Downloads the speificed file to the specified location.
		/// </summary>
		/// <remarks>
		/// This method starts the download, and waits for it to end.
		/// 
		/// If the given <paramref name="p_strSavePath"/> already exists, an attempt will be made to
		/// resume the download. If the pre-existing file is not a partial download, or the download
		/// cannot be resumed, the file will be overwritten.
		/// </remarks>
		/// <param name="p_uriURL">The URL of the file to download.</param>
		/// <param name="p_dicCookies">A list of cookies that should be sent in the request to download the file.</param>
		/// <param name="p_strSavePath">The path to which to save the file.
		/// If <paramref name="p_booUseDefaultFileName"/> is <c>false</c>, this value should be a complete
		/// path, including filename. If <paramref name="p_booUseDefaultFileName"/> is <c>true</c>,
		/// this value should be the directory in which to save the file.</param>
		/// <param name="p_booUseDefaultFileName">Whether to use the file name suggested by the server.</param>
		public void Download(Uri p_uriURL, Dictionary<string, string> p_dicCookies, string p_strSavePath, bool p_booUseDefaultFileName)
		{
			m_steState = new State(false, p_uriURL, p_dicCookies, p_strSavePath, p_booUseDefaultFileName);
			m_areWaitForDownload = new AutoResetEvent(false);
			DownloadAsync(p_uriURL, p_dicCookies, p_strSavePath, p_booUseDefaultFileName);
			m_areWaitForDownload.WaitOne();
		}

		/// <summary>
		/// Downloads the speificed file to the specified location.
		/// </summary>
		/// <remarks>
		/// This method starts the download and returns.
		/// 
		/// If the given <paramref name="p_strSavePath"/> already exists, an attempt will be made to
		/// resume the download. If the pre-existing file is not a partial download, or the download
		/// cannot be resumed, the file will be overwritten.
		/// </remarks>
		/// <param name="p_uriURL">The URL of the file to download.</param>
		/// <param name="p_dicCookies">A list of cookies that should be sent in the request to download the file.</param>
		/// <param name="p_strSavePath">The path to which to save the file.
		/// If <paramref name="p_booUseDefaultFileName"/> is <c>false</c>, this value should be a complete
		/// path, including filename. If <paramref name="p_booUseDefaultFileName"/> is <c>true</c>,
		/// this value should be the directory in which to save the file.</param>
		/// <param name="p_booUseDefaultFileName">Whether to use the file name suggested by the server.</param>
		public void DownloadAsync(Uri p_uriURL, Dictionary<string, string> p_dicCookies, string p_strSavePath, bool p_booUseDefaultFileName)
		{
			DownloadAsync(new List<Uri>() { p_uriURL }, p_dicCookies, p_strSavePath, p_booUseDefaultFileName);
		}

		/// <summary>
		/// Downloads the speificed file to the specified location.
		/// </summary>
		/// <remarks>
		/// This method starts the download and returns.
		/// 
		/// If the given <paramref name="p_strSavePath"/> already exists, an attempt will be made to
		/// resume the download. If the pre-existing file is not a partial download, or the download
		/// cannot be resumed, the file will be overwritten.
		/// </remarks>
		/// <param name="p_uriURL">The URL list of the file to download.</param>
		/// <param name="p_dicCookies">A list of cookies that should be sent in the request to download the file.</param>
		/// <param name="p_strSavePath">The path to which to save the file.
		/// If <paramref name="p_booUseDefaultFileName"/> is <c>false</c>, this value should be a complete
		/// path, including filename. If <paramref name="p_booUseDefaultFileName"/> is <c>true</c>,
		/// this value should be the directory in which to save the file.</param>
		/// <param name="p_booUseDefaultFileName">Whether to use the file name suggested by the server.</param>
		public void DownloadAsync(List<Uri> p_uriURL, Dictionary<string, string> p_dicCookies, string p_strSavePath, bool p_booUseDefaultFileName)
		{
			System.Diagnostics.Stopwatch swRetry = new System.Diagnostics.Stopwatch();
			int retries = 1;
			int i = 0;
			Uri uriURL = p_uriURL[i];
			ItemProgress = i;
			Status = TaskStatus.Running;

			while ((retries <= m_intRetries) || (Status != TaskStatus.Paused) || (Status != TaskStatus.Queued))
			{
				if ((Status == TaskStatus.Paused) || (Status == TaskStatus.Queued))
					break;
				else if (Status == TaskStatus.Retrying)
					Status = TaskStatus.Running;

				m_fdrDownloader = new FileDownloader(uriURL, p_dicCookies, p_strSavePath, p_booUseDefaultFileName, m_intMaxConnections, m_intMinBlockSize, m_strUserAgent);
				m_steState = new State(true, uriURL, p_dicCookies, p_strSavePath, p_booUseDefaultFileName);
				m_fdrDownloader.DownloadComplete += new EventHandler<CompletedDownloadEventArgs>(Downloader_DownloadComplete);
				ShowItemProgress = false;
				OverallProgressMaximum = (Int64)(m_fdrDownloader.FileSize / 1024);
				OverallProgressMinimum = 0;
				OverallProgressStepSize = 1;
				OverallProgress = (Int64)m_fdrDownloader.DownloadedByteCount;

				if (Status == TaskStatus.Cancelling)
					retries = m_intRetries;
				else if (Status == TaskStatus.Paused)
					break;

				if (!m_fdrDownloader.FileExists)
				{
					if (m_fdrDownloader.FileNotFound)
					{
						swRetry.Start();
						retries = 1;
						OverallMessage = String.Format("File not found on this server, retrying.. ({0}/{1})", retries, m_intRetries);
						Status = TaskStatus.Retrying;

						if (i++ == p_uriURL.Count)
						{
							Status = TaskStatus.Error;
							OnTaskEnded(String.Format("File does not exist: {0}", uriURL.ToString()), null);
							return;
						}

						ItemProgress = i;
						uriURL = p_uriURL[i];

						while ((swRetry.ElapsedMilliseconds < m_intRetryInterval) && swRetry.IsRunning)
						{
							if ((Status == TaskStatus.Cancelling) || (Status == TaskStatus.Paused) || (Status == TaskStatus.Queued))
								break;
						}
						swRetry.Stop();
						swRetry.Reset();
					}
					else if (m_fdrDownloader.ErrorCode == "666")
					{
						ErrorCode = m_fdrDownloader.ErrorCode;
						ErrorInfo = m_fdrDownloader.ErrorInfo;
						Status = TaskStatus.Error;
						OnTaskEnded(m_fdrDownloader.ErrorInfo, null);
						return;
					}
					else if (++retries <= m_intRetries)
					{
						swRetry.Start();
						OverallMessage = String.Format("Server busy or unavailable, retrying.. ({0}/{1})", retries, m_intRetries);
						Status = TaskStatus.Retrying;

						if ((retries == m_intRetries) && (++i < p_uriURL.Count))
						{
							ItemProgress = i;
							retries = 1;
							uriURL = p_uriURL[i];
						}

						while ((swRetry.ElapsedMilliseconds < m_intRetryInterval) && swRetry.IsRunning)
						{
							if ((Status == TaskStatus.Cancelling) || (Status == TaskStatus.Paused) || (Status == TaskStatus.Queued))
								break;
						}
						swRetry.Stop();
						swRetry.Reset();
					}
					else
					{
						Status = TaskStatus.Error;
						OnTaskEnded(String.Format("Error trying to get the file: {0}", uriURL.ToString()), null);
						return;
					}
				}
				else
				{
					break;
				}
			}

			if (ModRepository.IsOffline)
				this.Pause();
			else if (Status == TaskStatus.Running)
			{
				m_fdrDownloader.StartDownload();
				m_tmrUpdater.Start();
			}
		}

		/// <summary>
		/// Handles the <see cref="FileDownloader.DownloadComplete"/> event of the file downloader.
		/// </summary>
		/// <remarks>
		/// This stops the updating of the progress properties.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="CompletedDownloadEventArgs"/> describing the event arguments.</param>
		private void Downloader_DownloadComplete(object sender, CompletedDownloadEventArgs e)
		{
			m_tmrUpdater.Stop();
			if (m_areWaitForDownload != null)
				m_areWaitForDownload.Set();
			if (Status == TaskStatus.Paused)
				OnTaskEnded(String.Format("Paused: {0}", ((FileDownloader)sender).URL), ((FileDownloader)sender).TempFiles);
			else if (!e.GotEntireFile)
			{
				if (Status == TaskStatus.Cancelling)
				{
					m_fdrDownloader.Cleanup();
					Status = TaskStatus.Cancelled;
				}
				else
					Status = TaskStatus.Incomplete;
				ErrorCode = ((FileDownloader)sender).ErrorCode;
				ErrorInfo = ((FileDownloader)sender).ErrorInfo;
				if (ErrorCode == "666")
					OnTaskEnded(String.Format("{1}: {0} , ", ((FileDownloader)sender).URL, ErrorInfo), ((FileDownloader)sender).URL);
				else
					OnTaskEnded(String.Format("Error: {0} , unable to finish the download.", ((FileDownloader)sender).URL), ((FileDownloader)sender).URL);
			}
			else
			{
				Status = TaskStatus.Complete;
				OnTaskEnded(new DownloadedFileInfo(((FileDownloader)sender).URL, e.SavedFileName));
			}
		}

		#region Task Control

		/// <summary>
		/// Cancels the task.
		/// </summary>
		public override void Cancel()
		{
			if (Status == TaskStatus.Retrying)
			{
				Status = TaskStatus.Cancelling;
				base.Cancel();
			}
			else
			{
				Status = TaskStatus.Cancelled;
				if (m_fdrDownloader != null)
					m_fdrDownloader.Cleanup();
				OnTaskEnded("Download cancelled.", (m_fdrDownloader != null ? m_fdrDownloader.URL : new Uri("http://www.nexusmods.com")));
			}
		}

		/// <summary>
		/// Pauses the task.
		/// </summary>
		/// <exception cref="InvalidOperationException">Thrown if the task does not support pausing.</exception>
		public override void Pause()
		{
			Status = TaskStatus.Paused;
			if (m_fdrDownloader != null)
				m_fdrDownloader.Stop();
			else
			{
				base.Cancel();
			}
		}

		/// <summary>
		/// Queues the task.
		/// </summary>
		/// <exception cref="InvalidOperationException">Thrown if the task does not support queuing.</exception>
		public override void Queue()
		{
			Status = TaskStatus.Queued;
			if (m_fdrDownloader != null)
				m_fdrDownloader.Stop();
			else
			{
				base.Cancel();
			}
		}

		/// <summary>
		/// Resumes the task.
		/// </summary>
		/// <exception cref="InvalidOperationException">Thrown if the task is not paused.</exception>
		public override void Resume()
		{
			if ((Status != TaskStatus.Paused) && (Status != TaskStatus.Incomplete) && (Status != TaskStatus.Queued))
				throw new InvalidOperationException("Task is not paused.");
			if (m_steState.IsAsync)
				DownloadAsync(m_steState.URL, m_steState.Cookies, m_steState.SavePath, m_steState.UseDefaultFileName);
			else
				Download(m_steState.URL, m_steState.Cookies, m_steState.SavePath, m_steState.UseDefaultFileName);
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="System.Timers.Timer.Elapsed"/> event of the time that controls the updating of the progress properties.
		/// </summary>
		/// <remarks>
		/// This updates the progress properties.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="ElapsedEventArgs"/> describing the event arguments.</param>
		private void Updater_Elapsed(object sender, ElapsedEventArgs e)
		{
			if (m_fdrDownloader != null)
			{
				OverallMessage = String.Format(m_strMessageFormat, Path.GetFileName(m_fdrDownloader.SavePath), m_fdrDownloader.TimeRemaining.TotalMinutes, m_fdrDownloader.TimeRemaining.Seconds, m_fdrDownloader.DownloadSpeed / 1024);
				OverallProgress = (Int64)m_fdrDownloader.DownloadedByteCount;
				if (Status == TaskStatus.Cancelling)
				{
					m_fdrDownloader.Stop();
					m_fdrDownloader.Cleanup();
				}
			}
		}

		#region IDisposable Members

		/// <summary>
		/// Halts the file download.
		/// </summary>
		/// <remarks>
		/// After being disposed, that is no guarantee that the task's status will be correct. Further
		/// interaction with the object is undefined.
		/// </remarks>
		public void Dispose()
		{
			if (m_fdrDownloader != null)
			{
				m_fdrDownloader.DownloadComplete -= new EventHandler<CompletedDownloadEventArgs>(Downloader_DownloadComplete);
				m_fdrDownloader.Stop();
				m_fdrDownloader = null;
			}
		}

		#endregion
	}
}
