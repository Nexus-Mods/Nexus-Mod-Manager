using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.DownloadManagement;
using Nexus.Client.Games;
using Nexus.Client.ModAuthoring;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;
using Nexus.Client.Settings;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;
using ChinhDo.Transactions;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Adds, and downloads if required, a mod to the mod manager.
	/// </summary>
	public class AddModTask : BackgroundTask, IDisposable
	{
		/// <summary>
		/// Stores the state of a file being downloaded.
		/// </summary>
		private class DownloadProgressState
		{
			#region Properties

			/// <summary>
			/// Gets or sets the last recorded progress of the download.
			/// </summary>
			/// <value>The last recorded progress of the download.</value>
			public Int32 OverallProgress { get; set; }

			/// <summary>
			/// Gets or sets the last recorded progress minimum of the download.
			/// </summary>
			/// <value>The last recorded progress minimum of the download.</value>
			public Int32 OverallProgressMinimum { get; set; }

			/// <summary>
			/// Gets or sets the last recorded progress maximum of the download.
			/// </summary>
			/// <value>The last recorded progress maximum of the download.</value>
			public Int32 OverallProgressMaximum { get; set; }

			/// <summary>
			/// Gets the last recorded progress of the download, adjusted to account for the progress minimum.
			/// </summary>
			/// <value>The last recorded progress of the download, adjusted to account for the progress minimum.</value>
			public Int32 AdjustedProgress
			{
				get
				{
					return OverallProgress - OverallProgressMinimum;
				}
			}

			/// <summary>
			/// Gets the last recorded progress maximum of the download, adjusted to account for the progress minimum.
			/// </summary>
			/// <value>The last recorded progress maximum of the download, adjusted to account for the progress minimum.</value>
			public Int32 AdjustedProgressMaximum
			{
				get
				{
					return OverallProgressMaximum - OverallProgressMinimum;
				}
			}

			/// <summary>
			/// Gets or sets the last recorded download speed of the download.
			/// </summary>
			/// <value>The last recorded download speed of the download.</value>
			public Int32 DownloadSpeed { get; set; }

			/// <summary>
			/// Gets or sets the last recorded remaining time of the download.
			/// </summary>
			/// <value>The last recorded remaining time of the download.</value>
			public TimeSpan TimeRemaining { get; set; }

			#endregion

			#region Constructors

			/// <summary>
			/// A simple constructor that initializes teh object with the given values.
			/// </summary>
			/// <param name="p_fdtTask">The download task for which to store the progress state.</param>
			public DownloadProgressState(FileDownloadTask p_fdtTask)
			{
				Update(p_fdtTask);
			}

			#endregion

			/// <summary>
			/// Updates the stored state to reflect the current progress of the given download.
			/// </summary>
			/// <param name="p_fdtTask">The download task for which to store the progress state.</param>
			public void Update(FileDownloadTask p_fdtTask)
			{
				OverallProgress = p_fdtTask.OverallProgress;
				OverallProgressMinimum = p_fdtTask.OverallProgressMinimum;
				OverallProgressMaximum = p_fdtTask.OverallProgressMaximum;
				DownloadSpeed = p_fdtTask.DownloadSpeed;
				TimeRemaining = p_fdtTask.TimeRemaining;
			}
		}

		private readonly object LOCK_OBJECT = new object();
		private IGameMode m_gmdGameMode = null;
		private IEnvironmentInfo m_eifEnvironmentInfo = null;
		private ModRegistry m_mrgModRegistry = null;
		private ReadMeManager m_rmmReadMeManager = null;
		private IModRepository m_mrpModRepository = null;
		private IModFormatRegistry m_mfrModFormatRegistry = null;
		private ConfirmOverwriteCallback m_cocConfirmOverwrite = null;
		private Dictionary<IBackgroundTask, DownloadProgressState> m_dicDownloaderProgress = new Dictionary<IBackgroundTask, DownloadProgressState>();
		private List<IBackgroundTask> m_lstRunningTasks = new List<IBackgroundTask>();
		private bool m_booFinishedDownloads = false;
		private Int32 m_intOverallProgressOffset = 0;
		private Uri m_uriPath = null;
		private DateTime m_dteStartTime = DateTime.Now;
		private Int32 m_intPreviousProgress = 0;
		private Stack<Int32> m_lstPreviousSpeed = new Stack<Int32> { };
		private Stopwatch swtSpeed = new Stopwatch();
		private List<string> m_strFileserverCaptions = new List<string>();
		private Double m_dblMinutes = 0;
		private Int32 m_intSeconds = 0;
		private Int32 m_intDownloadProgress = 0;
		private Int32 m_intDownloadMaximum = 0;
		private string m_strFileserver = String.Empty;

		#region Properties

		/// <summary>
		/// Gets the descriptor describing the mod being added.
		/// </summary>
		/// <value>The descriptor describing the mod being added.</value>
		protected AddModDescriptor Descriptor { get; private set; }

		/// <summary>
		/// Gets the metadata about the mod we are adding.
		/// </summary>
		/// <value>The metadata about the mod we are adding.</value>
		public IModInfo ModInfo { get; private set; }

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
		/// Gets the time that has elapsed downloading the file.
		/// </summary>
		/// <value>The time that has elapsed downloading the file.</value>
		protected TimeSpan ElapsedTime
		{
			get
			{
				return DateTime.Now.Subtract(m_dteStartTime);
			}
		}

		/// <summary>
		/// Gets the current task speed.
		/// </summary>
		/// <value>The current task speed.</value>
		public Double ETA_Minutes
		{
			get
			{
				return m_dblMinutes;
			}
			set
			{
				bool booChanged = false;
				lock (LOCK_OBJECT)
				{
					if (m_dblMinutes != value)
					{
						booChanged = true;
						m_dblMinutes = value;
					}
				}
				if (booChanged)
					OnPropertyChanged(() => ETA_Minutes);
			}
		}

		/// <summary>
		/// Gets the current task speed.
		/// </summary>
		/// <value>The current task speed.</value>
		public Int32 ETA_Seconds
		{
			get
			{
				return m_intSeconds;
			}
			set
			{
				bool booChanged = false;
				lock (LOCK_OBJECT)
				{
					if (m_intSeconds != value)
					{
						booChanged = true;
						m_intSeconds = value;
					}
				}
				if (booChanged)
					OnPropertyChanged(() => ETA_Seconds);
			}
		}

		/// <summary>
		/// Gets the current task speed.
		/// </summary>
		/// <value>The current task speed.</value>
		public Int32 DownloadProgress
		{
			get
			{
				return m_intDownloadProgress;
			}
			set
			{
				bool booChanged = false;
				lock (LOCK_OBJECT)
				{
					if (m_intDownloadProgress != value)
					{
						booChanged = true;
						m_intDownloadProgress = value;
					}
				}
				if (booChanged)
					OnPropertyChanged(() => DownloadProgress);
			}
		}

		/// <summary>
		/// Gets the current task speed.
		/// </summary>
		/// <value>The current task speed.</value>
		public Int32 DownloadMaximum
		{
			get
			{
				return m_intDownloadMaximum;
			}
			set
			{
				bool booChanged = false;
				lock (LOCK_OBJECT)
				{
					if (m_intDownloadMaximum != value)
					{
						booChanged = true;
						m_intDownloadMaximum = value;
					}
				}
				if (booChanged)
					OnPropertyChanged(() => DownloadMaximum);
			}
		}

		/// <summary>
		/// Gets the current task speed.
		/// </summary>
		/// <value>The current task speed.</value>
		public string FileServer
		{
			get
			{
				return m_strFileserver;
			}
			set
			{
				bool booChanged = false;
				lock (LOCK_OBJECT)
				{
					if (m_strFileserver != value)
					{
						booChanged = true;
						m_strFileserver = value;
					}
				}
				if (booChanged)
					OnPropertyChanged(() => FileServer);
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
		/// <param name="p_gmdGameMode">The game mode for which mods are being managed.</param>
		/// <param name="p_rmmReadMeManager">The ReadMe Manager info.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_mrgModRegistry">The <see cref="ModRegistry"/> that contains the list of managed <see cref="IMod"/>s.</param>
		/// <param name="p_frgFormatRegistry">The <see cref="IModFormatRegistry"/> that contains the list
		/// of supported <see cref="IModFormat"/>s.</param>
		/// <param name="p_mrpModRepository">The mod repository from which to get mods and mod metadata.</param>
		/// <param name="p_uriPath">The path to the mod to add.</param>
		/// <param name="p_cocConfirmOverwrite">The delegate to call to resolve conflicts with existing files.</param>
		public AddModTask(IGameMode p_gmdGameMode, ReadMeManager p_rmmReadMeManager, IEnvironmentInfo p_eifEnvironmentInfo, ModRegistry p_mrgModRegistry, IModFormatRegistry p_frgFormatRegistry, IModRepository p_mrpModRepository, Uri p_uriPath, ConfirmOverwriteCallback p_cocConfirmOverwrite)
		{
			m_gmdGameMode = p_gmdGameMode;
			m_eifEnvironmentInfo = p_eifEnvironmentInfo;
			m_mrgModRegistry = p_mrgModRegistry;
			m_mfrModFormatRegistry = p_frgFormatRegistry;
			m_mrpModRepository = p_mrpModRepository;
			m_uriPath = p_uriPath;
			m_cocConfirmOverwrite = p_cocConfirmOverwrite;
			m_rmmReadMeManager = p_rmmReadMeManager;
		}

		#endregion

		/// <summary>
		/// Starts the mod adding task.
		/// </summary>
		public void AddMod()
		{
			string strNexusError = String.Empty;
			string strNexusErrorInfo = String.Empty;
			Trace.TraceInformation(String.Format("[{0}] Starting Add Mod Task.", m_uriPath));
			Status = TaskStatus.Running;
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			ShowItemProgress = true;
			OverallMessage = String.Format("{0}...", m_uriPath);

			try
			{
				Descriptor = BuildDescriptor(m_uriPath);
			}
			catch (System.ServiceModel.CommunicationException e)
			{
				if ((((System.Exception)(e)).InnerException != null) && (((System.Net.WebException)(((System.Exception)(e)).InnerException)).Response != null) && (((System.Net.WebException)(((System.Exception)(e)).InnerException)).Response.Headers != null))
				{
					WebHeaderCollection whcHeaders = ((System.Net.WebException)(((System.Exception)(e)).InnerException)).Response.Headers;
					foreach (string Header in whcHeaders.Keys)
					{
						switch (Header)
						{
							case "NexusError":
								strNexusError = whcHeaders.GetValues(Header)[0];
								break;
							case "NexusErrorInfo":
								strNexusErrorInfo = whcHeaders.GetValues(Header)[0];
								break;
						}
					}
				}
			}
			if (Descriptor == null)
			{
				if (m_mrpModRepository.IsOffline)
				{
					Status = TaskStatus.Error;
					OverallMessage = "Unable to download in Offline Mode!";
					OnTaskEnded("Unable to download in Offline Mode!", null);
					return;
				}
				else
				{
					ErrorCode = strNexusError;
					ErrorInfo = strNexusErrorInfo;
					Status = TaskStatus.Error;
					if (!String.IsNullOrEmpty(strNexusErrorInfo))
					{
						OverallMessage = strNexusErrorInfo;
						OnTaskEnded(strNexusErrorInfo, null);
					}
					else
					{
						OverallMessage = String.Format("Server Unreachable: {0}", m_uriPath.ToString());
						OnTaskEnded(String.Format("Server Unreachable: {0}", m_uriPath.ToString()), null);
					}
					return;
				}
			}

			ModInfo = GetModInfo(Descriptor);

			OverallMessage = String.Format("{0}...", GetModDisplayName());

			if (Descriptor.Status == TaskStatus.Error)
				Cancel();
			else if (Descriptor.Status == TaskStatus.Paused)
				Pause();
			else
			{
				if (Descriptor.DownloadFiles.IsNullOrEmpty())
				{
					OverallProgressMaximum = 5;
					AddModFile(m_cocConfirmOverwrite);
				}
				else
				{
					m_intOverallProgressOffset = 1;
					OverallProgressMaximum = 6;
					ItemProgressMaximum = 0;
					ItemMessage = String.Format("Downloading {0}...", GetModDisplayName());
					DownloadFiles(Descriptor.DownloadFiles);
				}
			}
		}

		/// <summary>
		/// Gets the name of the mod to use for display.
		/// </summary>
		/// <returns>The name of the mod to use for display.</returns>
		private string GetModDisplayName()
		{
			if ((ModInfo == null) || String.IsNullOrEmpty(ModInfo.ModName))
				if (Descriptor == null)
					return null;
				else
					return Path.GetFileNameWithoutExtension(Descriptor.DefaultSourcePath);
			return ModInfo.ModName;
		}

		/// <summary>
		/// Get the reposiroty info for the described mod.
		/// </summary>
		/// <param name="p_amdDescriptor">The obejct that describes the mod for which to retrieve the info.</param>
		/// <returns>The repository info for the described mod.</returns>
		private IModInfo GetModInfo(AddModDescriptor p_amdDescriptor)
		{
			switch (p_amdDescriptor.SourceUri.Scheme.ToLowerInvariant())
			{
				case "file":
					try
					{
						return new ModInfo(m_mrpModRepository.GetModInfoForFile(Path.GetFileName(p_amdDescriptor.DefaultSourcePath)));
					}
					catch (RepositoryUnavailableException e)
					{
						TraceUtil.TraceException(e);
						return null;
					}
				case "nxm":
					NexusUrl nxuModUrl = new NexusUrl(p_amdDescriptor.SourceUri);
					if ((String.IsNullOrEmpty(nxuModUrl.ModId)) || (string.IsNullOrEmpty(nxuModUrl.FileId)))
						throw new ArgumentException("Invalid Nexus URI: " + p_amdDescriptor.SourceUri.ToString());
					ModInfo modInfo = null;

					try
					{
						if (!m_mrpModRepository.IsOffline)
						{
							modInfo = (ModInfo)m_mrpModRepository.GetModInfo(nxuModUrl.ModId);

							IMod modMod = m_mrgModRegistry.RegisteredMods.Find(x => x.Id == nxuModUrl.ModId);
							if ((modMod != null) && (modInfo != null))
							{
								modInfo.IsEndorsed = modMod.IsEndorsed;
								modInfo.UpdateWarningEnabled = modMod.UpdateWarningEnabled;
								modInfo.CustomCategoryId = modMod.CustomCategoryId;
							}

							IModFileInfo mfiFileInfo = m_mrpModRepository.GetFileInfo(nxuModUrl.ModId, nxuModUrl.FileId);
							if ((modInfo != null) && (mfiFileInfo != null))
								modInfo = (ModInfo)AutoTagger.CombineInfo(modInfo, mfiFileInfo);
						}
					}
					catch (RepositoryUnavailableException e)
					{
						TraceUtil.TraceException(e);
					}
					return modInfo;
				default:
					Trace.TraceInformation(String.Format("[{0}] Can't get mod info.", p_amdDescriptor.SourceUri.ToString()));
					throw new Exception("Unable to retrieve mod info: " + p_amdDescriptor.SourceUri.ToString());
			}
		}

		/// <summary>
		/// Build the obejct that describes the mod being added.
		/// </summary>
		/// <param name="p_uriPath">The path of the mod being added.</param>
		/// <returns>The obejct that describes the mod being added.</returns>
		private AddModDescriptor BuildDescriptor(Uri p_uriPath)
		{
			if (!m_eifEnvironmentInfo.Settings.QueuedModsToAdd.ContainsKey(m_gmdGameMode.ModeId))
				m_eifEnvironmentInfo.Settings.QueuedModsToAdd[m_gmdGameMode.ModeId] = new KeyedSettings<AddModDescriptor>();
			KeyedSettings<AddModDescriptor> dicQueuedMods = m_eifEnvironmentInfo.Settings.QueuedModsToAdd[m_gmdGameMode.ModeId];
			AddModDescriptor amdDescriptor = null;
			if (!dicQueuedMods.TryGetValue(p_uriPath.ToString(), out amdDescriptor))
			{
				switch (p_uriPath.Scheme.ToLowerInvariant())
				{
					case "file":
						amdDescriptor = new AddModDescriptor(p_uriPath, p_uriPath.LocalPath, null, TaskStatus.Running, new List<string>());
						break;
					case "nxm":
						NexusUrl nxuModUrl = new NexusUrl(p_uriPath);

						if (String.IsNullOrEmpty(nxuModUrl.ModId))
						{
							Trace.TraceError("Invalid Nexus URI: " + p_uriPath.ToString());
							return null;
						}

						IModFileInfo mfiFile = null;
						List<FileserverInfo> lstFileServerInfo = new List<FileserverInfo>();
						List<Uri> uriFilesToDownload = new List<Uri>();
						try
						{
							if (String.IsNullOrEmpty(nxuModUrl.FileId))
								mfiFile = m_mrpModRepository.GetDefaultFileInfo(nxuModUrl.ModId);
							else
								mfiFile = m_mrpModRepository.GetFileInfo(nxuModUrl.ModId, nxuModUrl.FileId);
							if (mfiFile == null)
							{
								Trace.TraceInformation(String.Format("[{0}] Can't get the file: no file.", p_uriPath.ToString()));
								return null;
							}
							lstFileServerInfo = m_mrpModRepository.GetFilePartInfo(nxuModUrl.ModId, mfiFile.Id.ToString(), m_eifEnvironmentInfo.Settings.PremiumOnly, m_eifEnvironmentInfo.Settings.UserLocation);
							if (lstFileServerInfo.Count > 0)
							{
								foreach (FileserverInfo fsiFileServer in lstFileServerInfo)
									if (!String.IsNullOrEmpty(fsiFileServer.DownloadLink))
									{
										uriFilesToDownload.Add(new Uri(fsiFileServer.DownloadLink));
										m_strFileserverCaptions.Add(fsiFileServer.Name);
									}
							}
						}
						catch (RepositoryUnavailableException e)
						{
							TraceUtil.TraceException(e);
							return null;
						}

						if ((uriFilesToDownload == null) || (uriFilesToDownload.Count <= 0))
							return null;
						string strSourcePath = Path.Combine(m_gmdGameMode.GameModeEnvironmentInfo.ModDownloadCacheDirectory, mfiFile.Filename);
						amdDescriptor = new AddModDescriptor(p_uriPath, strSourcePath, uriFilesToDownload, TaskStatus.Running, m_strFileserverCaptions);
						break;
					default:
						Trace.TraceInformation(String.Format("[{0}] Can't get the file.", p_uriPath.ToString()));
						throw new Exception("Unable to retrieve file: " + p_uriPath.ToString());
				}
				dicQueuedMods[p_uriPath.ToString()] = amdDescriptor;
				m_eifEnvironmentInfo.Settings.Save();
			}
			else
				m_strFileserverCaptions = amdDescriptor.SourceName;
			return amdDescriptor;
		}

		#region Mod Files Download

		/// <summary>
		/// Downloads the given files.
		/// </summary>
		/// <param name="p_lstFiles">The files to download.</param>
		protected void DownloadFiles(List<Uri> p_lstFiles)
		{
			Trace.TraceInformation(String.Format("[{0}] Downloading Files.", Descriptor.SourceUri.ToString()));
			Trace.TraceInformation(String.Format("[{0}] Launching downloading of {1}.", Descriptor.SourceUri.ToString(), p_lstFiles[0].ToString()));
			Dictionary<string, string> dicAuthenticationTokens = m_eifEnvironmentInfo.Settings.RepositoryAuthenticationTokens[m_mrpModRepository.Id];
			//TODO get the max connection and block size from settings
			Int32 intConnections = m_eifEnvironmentInfo.Settings.NumberOfConnections <= m_mrpModRepository.AllowedConnections.Max() ? m_eifEnvironmentInfo.Settings.NumberOfConnections : m_mrpModRepository.AllowedConnections.Max();

			FileDownloadTask fdtDownloader = new FileDownloadTask(intConnections, 1024 * 500, m_mrpModRepository.UserAgent);
			fdtDownloader.TaskEnded += new EventHandler<TaskEndedEventArgs>(Downloader_TaskEnded);
			fdtDownloader.PropertyChanged += new PropertyChangedEventHandler(Downloader_PropertyChanged);
			m_lstRunningTasks.Add(fdtDownloader);
			fdtDownloader.DownloadAsync(p_lstFiles, dicAuthenticationTokens, Path.GetDirectoryName(Descriptor.DefaultSourcePath), true);
		}

		/// <summary>
		/// Handles the <see cref="INotifyPropertyChanged.PropertyChanged"/> event of the file downloader tasks.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="PropertyChangedEventArgs"/> describing the event arguments.</param>
		private void Downloader_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (m_booFinishedDownloads)
				return;
			FileDownloadTask fdtDownloader = (FileDownloadTask)sender;
			if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.OverallProgress)) ||
				e.PropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.OverallProgressMaximum)))
			{
				Int32 intLastProgress = 0;
				if (m_dicDownloaderProgress.ContainsKey(fdtDownloader))
					intLastProgress = m_dicDownloaderProgress[fdtDownloader].OverallProgress;
				else if (m_dicDownloaderProgress.Count == 0)
				{
					//this means this is the first update, or the first update after
					// the task was resumed, so the current item progress can be assumed
					// to be the last progress
					intLastProgress = ItemProgress;
				}
				if (intLastProgress <= fdtDownloader.OverallProgress)
					if (!m_dicDownloaderProgress.ContainsKey(fdtDownloader))
						m_dicDownloaderProgress[fdtDownloader] = new DownloadProgressState(fdtDownloader);
					else
						m_dicDownloaderProgress[fdtDownloader].Update(fdtDownloader);
				Int32 intProgress = 0;
				Int32 intProgressMaximum = 0;
				Int32 intSpeed = 0;
				TimeSpan tspTimeRemaining = TimeSpan.Zero;
				foreach (DownloadProgressState dpsState in m_dicDownloaderProgress.Values)
				{
					intProgress += dpsState.AdjustedProgress;
					intProgressMaximum += dpsState.AdjustedProgressMaximum;
					intSpeed += dpsState.DownloadSpeed;
					if (tspTimeRemaining < dpsState.TimeRemaining)
						tspTimeRemaining = dpsState.TimeRemaining;
				}
				ItemProgress = intProgress;
				ItemProgressMaximum = intProgressMaximum;

				if (swtSpeed.IsRunning)
				{
					if (swtSpeed.ElapsedMilliseconds >= 1000)
					{
						if (m_intPreviousProgress == 0)
							m_intPreviousProgress = fdtDownloader.ResumedByteCount > 0 ? fdtDownloader.ResumedByteCount : 0;
						if (m_dicDownloaderProgress.ContainsKey(fdtDownloader))
							TaskSpeed = (int)(((double)(m_dicDownloaderProgress[fdtDownloader].AdjustedProgress - m_intPreviousProgress) / (swtSpeed.ElapsedMilliseconds / 1000)) / 1024);
						if (TaskSpeed >= 0)
						{
							if (m_lstPreviousSpeed.Count == 10)
								m_lstPreviousSpeed.Pop();
							m_lstPreviousSpeed.Push(TaskSpeed);
						}
						if (m_lstPreviousSpeed.Count > 1)
							TaskSpeed = (int)m_lstPreviousSpeed.Average();
						if (m_dicDownloaderProgress.ContainsKey(fdtDownloader))
							m_intPreviousProgress = m_dicDownloaderProgress[fdtDownloader].AdjustedProgress;
						swtSpeed.Reset();
						swtSpeed.Start();
					}
					else
						TaskSpeed = (m_lstPreviousSpeed.Count > 0) && (m_lstPreviousSpeed.Average() > 0) ? (int)m_lstPreviousSpeed.Average() : (intSpeed / 1024);
				}
				else
				{
					swtSpeed.Start();
				}

				double dblMinutes = (intSpeed == 0) ? 99 : tspTimeRemaining.TotalMinutes;
				Int32 intSeconds = (intSpeed == 0) ? 99 : tspTimeRemaining.Seconds;
				if ((ItemProgress == 0) && (intSpeed == 0))
					ItemMessage = "Starting the download...";
				else if ((ItemProgress == intLastProgress) && (intSpeed == 0))
					ItemMessage = "Resuming the download...";
				else
				{
					ETA_Minutes = dblMinutes;
					ETA_Seconds = intSeconds;

					if (m_dicDownloaderProgress.ContainsKey(fdtDownloader))
					{
						DownloadProgress = (m_dicDownloaderProgress[fdtDownloader].AdjustedProgress / 1024);
						DownloadMaximum = (m_dicDownloaderProgress[fdtDownloader].AdjustedProgressMaximum / 1024);
					}
				}

				ActiveThreads = fdtDownloader.ActiveThreads;
			}
			else if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.OverallMessage)) ||
				e.PropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.Status)))
			{
				if (fdtDownloader.Status == TaskStatus.Retrying)
				{
					OverallMessage = fdtDownloader.OverallMessage;
				}
				else if (fdtDownloader.Status == TaskStatus.Running)
				{
					OverallMessage = String.Format("{0}", GetModDisplayName());
					if (m_strFileserverCaptions.ContainsKey(fdtDownloader.ItemProgress))
						FileServer = m_strFileserverCaptions[fdtDownloader.ItemProgress];
				}
				InnerTaskStatus = fdtDownloader.Status;
			}
		}

		/// <summary>
		/// Handles the <see cref="IBackgroundTask.TaskEnded"/> event of the file downloader tasks.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="TaskEndedEventArgs"/> describing the event arguments.</param>
		private void Downloader_TaskEnded(object sender, TaskEndedEventArgs e)
		{
			m_lstRunningTasks.Remove((IBackgroundTask)sender);
			if (e.Status == TaskStatus.Complete)
			{
				DownloadedFileInfo dfiDownloadInfo = (DownloadedFileInfo)e.ReturnValue;
				if (String.IsNullOrEmpty(Descriptor.SourcePath) && (Descriptor.DownloadFiles.IndexOf(dfiDownloadInfo.URL) == 0))
					Descriptor.SourcePath = dfiDownloadInfo.SavedFilePath;

				Descriptor.DownloadFiles.Clear();
				Descriptor.DownloadedFiles.Add(dfiDownloadInfo.SavedFilePath);

				KeyedSettings<AddModDescriptor> dicQueuedMods = m_eifEnvironmentInfo.Settings.QueuedModsToAdd[m_gmdGameMode.ModeId];
				dicQueuedMods[Descriptor.SourceUri.ToString()] = Descriptor;
				m_eifEnvironmentInfo.Settings.Save();

				if (Descriptor.DownloadFiles.Count == 0)
				{
					StepOverallProgress();
					AddModFile(m_cocConfirmOverwrite);
				}
			}
			else if (IsActive)
			{
				if ((FileDownloadTask)sender != null)
				{
					ErrorCode = ((FileDownloadTask)sender).ErrorCode;
					ErrorInfo = ((FileDownloadTask)sender).ErrorInfo;
				}

				Status = e.Status;
				OverallMessage = e.Message;
				OnTaskEnded(e.Message, e.ReturnValue);
			}
			else if (e.Status == TaskStatus.Paused)
			{
				string[] TempFiles = (string[])e.ReturnValue;
				Descriptor.PausedFiles.AddRange(TempFiles);
			}

			if (swtSpeed.IsRunning)
				swtSpeed.Stop();
		}

		#endregion

		#region Mod Addition

		/// <summary>
		/// Adds the mod file to the mod manager.
		/// </summary>
		/// <param name="p_cocConfirmOverwrite">The delegate to call to resolve conflicts with existing files.</param>
		protected void AddModFile(ConfirmOverwriteCallback p_cocConfirmOverwrite)
		{
			string strPath = String.IsNullOrEmpty(Descriptor.SourcePath) ? Descriptor.DefaultSourcePath : Descriptor.SourcePath;
			string strDestinationHD = String.Empty;
			m_booFinishedDownloads = true;
			FileAttributes faAttributes = File.GetAttributes(strPath);
			FileInfo fiArchive = new FileInfo(strPath);
			long lngDestinationFreeSpace = fiArchive.Length;

			if (Path.GetPathRoot(m_gmdGameMode.GameModeEnvironmentInfo.ModDirectory) != Path.DirectorySeparatorChar.ToString())
			{
				DriveInfo diDestinationHD = new DriveInfo(Path.GetPathRoot(m_gmdGameMode.GameModeEnvironmentInfo.ModDirectory));
				lngDestinationFreeSpace = diDestinationHD.TotalFreeSpace;
				strDestinationHD = diDestinationHD.Name;
			}

			if (lngDestinationFreeSpace < fiArchive.Length)
			{
				OverallMessage = String.Format("Not enough free space on your HD: {0}", strDestinationHD);
				ItemMessage = "Not enough free space on your HD.";
				Status = TaskStatus.Error;
				OnTaskEnded(OverallMessage, null);
			}
			else if (faAttributes.ToString().IndexOf(FileAttributes.ReadOnly.ToString()) > -1)
			{
				OverallMessage = String.Format("The archive is read only: {0}", strPath);
				ItemMessage = "The archive is read only";
				Status = TaskStatus.Error;
				OnTaskEnded(OverallMessage, null);
			}
			else if (!File.Exists(strPath))
			{
				OverallMessage = String.Format("File does not exist: {0}", strPath);
				ItemMessage = "File does not exist";
				Status = TaskStatus.Error;
				OnTaskEnded(OverallMessage, null);
			}
			else
			{
				ModBuilder mbrModBuilder = new ModBuilder(m_gmdGameMode.GameModeEnvironmentInfo, m_eifEnvironmentInfo, new NexusFileUtil(m_eifEnvironmentInfo));
				mbrModBuilder.PropertyChanged += new PropertyChangedEventHandler(ModBuilder_PropertyChanged);
				mbrModBuilder.TaskEnded += new EventHandler<TaskEndedEventArgs>(ModBuilder_TaskEnded);
				mbrModBuilder.BuildFromFile(m_mfrModFormatRegistry, strPath, p_cocConfirmOverwrite);
				m_lstRunningTasks.Add(mbrModBuilder);
			}
		}

		/// <summary>
		/// Handles the <see cref="INotifyPropertyChanged.PropertyChanged"/> event of the mod builder task.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="PropertyChangedEventArgs"/> describing the event arguments.</param>
		private void ModBuilder_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.OverallMessage)))
				OverallMessage = ((IBackgroundTask)sender).OverallMessage;
			else if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.OverallProgress)))
			{
				if (OverallProgress - m_intOverallProgressOffset < ((IBackgroundTask)sender).OverallProgress)
					OverallProgress = ((IBackgroundTask)sender).OverallProgress + m_intOverallProgressOffset;
			}
			else if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ItemMessage)))
				ItemMessage = ((IBackgroundTask)sender).ItemMessage;
			else if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ItemProgress)))
				ItemProgress = ((IBackgroundTask)sender).ItemProgress;
			else if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ItemProgressMaximum)))
			{
				ItemProgressMaximum = ((IBackgroundTask)sender).ItemProgressMaximum;
				ItemProgress = 0;
			}
			else if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ItemProgressMinimum)))
			{
				ItemProgressMinimum = ((IBackgroundTask)sender).ItemProgressMinimum;
				ItemProgress = 0;
			}
		}

		/// <summary>
		/// Handles the <see cref="IBackgroundTask.TaskEnded"/> event of the mod builder task.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="TaskEndedEventArgs"/> describing the event arguments.</param>
		private void ModBuilder_TaskEnded(object sender, TaskEndedEventArgs e)
		{
			m_lstRunningTasks.Remove((IBackgroundTask)sender);
			if (Status == TaskStatus.Running)
			{
				if (e.Status == TaskStatus.Complete)
					RegisterModFiles((IList<string>)e.ReturnValue);
				else
				{
					OverallMessage = String.Format("{0} can't be added.", GetModDisplayName());
					ItemMessage = e.Message;
					//if we errored while adding, let's set to imcomplete so as to not
					// loose the file we download - the user may wish to do somethinng manual
					Status = (e.Status == TaskStatus.Error) ? TaskStatus.Incomplete : e.Status;
					OnTaskEnded(e.Message, e.ReturnValue);
				}
			}
		}

		#endregion

		#region Mod Registration

		/// <summary>
		/// Registers the given mods with the registry.
		/// </summary>
		/// <param name="p_lstAddedMods">The mods that have been added and need to be registered with the manager.</param>
		protected void RegisterModFiles(IList<string> p_lstAddedMods)
		{
			OverallMessage = "Adding mods to manager...";
			ItemMessage = "Registering Mods...";
			if (p_lstAddedMods != null)
			{
				ItemProgress = 0;
				ItemProgressMaximum = p_lstAddedMods.Count;

				foreach (string strMod in p_lstAddedMods)
				{
					try
					{
						if (m_mrgModRegistry.RegisteredMods.SingleOrDefault(x => x.Filename == strMod) == null)
						{
							if (m_eifEnvironmentInfo.Settings.AddMissingInfoToMods)
								m_mrgModRegistry.RegisterMod(strMod, ModInfo);
							else
								m_mrgModRegistry.RegisterMod(strMod);
						}

						if (m_rmmReadMeManager != null)
						{
							TxFileManager tfmFileManager = new TxFileManager();
							string strModFolderPath = m_gmdGameMode.GameModeEnvironmentInfo.ModDirectory;
							if (m_rmmReadMeManager.VerifyReadMeFile(tfmFileManager, strMod))
								m_rmmReadMeManager.SaveReadMeConfig();
						}
					}
					catch (Exception ex)
					{
						OverallMessage = String.Format("There was an error registering this mod: {1}" + Environment.NewLine + "Error: {0} ", ex.Message, GetModDisplayName());
						ItemMessage = "Error registering mod.";
						Status = TaskStatus.Error;
						OnTaskEnded(null, null);
						return;
					}
					StepItemProgress();
				}
			}
			StepOverallProgress();
			OverallMessage = String.Format("{0} has been added", GetModDisplayName());
			ItemMessage = "Finished adding mod.";
			Status = TaskStatus.Complete;
			OnTaskEnded(null, null);
		}

		#endregion

		#region Task Control

		/// <summary>
		/// Cancels the task.
		/// </summary>
		public override void Cancel()
		{
			base.Cancel();
			foreach (IBackgroundTask tskTask in m_lstRunningTasks)
				if ((tskTask.Status == TaskStatus.Running) || (tskTask.Status == TaskStatus.Paused) || (tskTask.Status == TaskStatus.Incomplete) || (tskTask.Status == TaskStatus.Retrying))
					tskTask.Cancel();
			OverallMessage = String.Format("Cancelled {0}", GetModDisplayName());
			ItemMessage = "Cancelled";
			Status = TaskStatus.Cancelled;

			if (Descriptor == null)
			{
				Status = TaskStatus.Error;
				Descriptor = BuildDescriptor(m_uriPath);
				Descriptor.Status = Status;
				OverallMessage = String.Format("An error occurred while cancelling: {0}", m_uriPath.ToString());
				OnTaskEnded(String.Format("An error occurred while cancelling: {0}", m_uriPath.ToString()), null);
				return;
			}

			OnTaskEnded(Descriptor.SourceUri);

		}

		/// <summary>
		/// Pauses the task.
		/// </summary>
		/// <exception cref="InvalidOperationException">Thrown if the task does not support pausing.</exception>
		public override void Pause()
		{
			Status = TaskStatus.Paused;
			for (Int32 i = m_lstRunningTasks.Count - 1; i >= 0; i--)
			{
				if (i >= m_lstRunningTasks.Count)
					continue;
				IBackgroundTask tskTask = m_lstRunningTasks[i];
				if (tskTask.SupportsPause)
					tskTask.Pause();
				else
					tskTask.Cancel();
			}
			OnTaskEnded(Descriptor.SourceUri);
		}

		/// <summary>
		/// Resumes the task.
		/// </summary>
		/// <exception cref="InvalidOperationException">Thrown if the task is not paused.</exception>
		public override void Resume()
		{
			if ((Status != TaskStatus.Paused) && (Status != TaskStatus.Incomplete))
				throw new InvalidOperationException("Task is not paused.");
			m_lstRunningTasks.Clear();
			m_dicDownloaderProgress.Clear();
			AddMod();
		}

		#endregion

		/// <summary>
		/// Raises the <see cref="INotifyPropertyChanged.PropertyChanged"/> event.
		/// </summary>
		/// <remarks>
		/// This persists the task state to storage, so it can be resumed on client restart.
		/// </remarks>
		/// <param name="e">A <see cref="PropertyChangedEventArgs"/> describing the event's arguments.</param>
		protected override void OnPropertyChanged(PropertyChangedEventArgs e)
		{
			if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.Status)) && (Descriptor != null))
			{
				Descriptor.Status = Status;
				KeyedSettings<AddModDescriptor> dicQueuedMods = m_eifEnvironmentInfo.Settings.QueuedModsToAdd[m_gmdGameMode.ModeId];
				dicQueuedMods[Descriptor.SourceUri.ToString()] = Descriptor;
				m_eifEnvironmentInfo.Settings.Save();
			}
			base.OnPropertyChanged(e);
		}

		/// <summary>
		/// Raises the <see cref="IBackgroundTask.TaskEnded"/> event.
		/// </summary>
		/// <remarks>
		/// This removes the task state from storage if it failed.
		/// </remarks>
		/// <param name="e">A <see cref="TaskEndedEventArgs"/> describing the event's arguments.</param>
		protected override void OnTaskEnded(TaskEndedEventArgs e)
		{
			if ((e.Status != TaskStatus.Paused) && (e.Status != TaskStatus.Incomplete) && (Descriptor != null))
			{
				foreach (string strFile in Descriptor.DownloadedFiles)
					if (strFile.StartsWith(m_gmdGameMode.GameModeEnvironmentInfo.ModDownloadCacheDirectory, StringComparison.OrdinalIgnoreCase))
						FileUtil.ForceDelete(strFile);
				if (e.Status == TaskStatus.Cancelled)
				{
					foreach (string strFile in Descriptor.PausedFiles)
						if (strFile.StartsWith(m_gmdGameMode.GameModeEnvironmentInfo.ModDownloadCacheDirectory, StringComparison.OrdinalIgnoreCase))
						{
							FileUtil.ForceDelete(strFile);
						}
					Descriptor.PausedFiles.Clear();
				}
				KeyedSettings<AddModDescriptor> dicQueuedMods = m_eifEnvironmentInfo.Settings.QueuedModsToAdd[m_gmdGameMode.ModeId];
				dicQueuedMods.Remove(Descriptor.SourceUri.ToString());
				m_eifEnvironmentInfo.Settings.Save();
			}
			base.OnTaskEnded(e);
		}

		#region IDisposable Members

		/// <summary>
		/// Terminates all tasks started by this task.
		/// </summary>
		/// <remarks>
		/// After being disposed, that is no guarantee that the task's status will be correct. Further
		/// interaction with the object is undefined.
		/// </remarks>
		public void Dispose()
		{
			foreach (IDisposable tskTask in m_lstRunningTasks)
				tskTask.Dispose();
		}

		#endregion
	}
}
