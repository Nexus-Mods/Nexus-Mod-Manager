namespace Nexus.Client.ModManagement
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.ServiceModel;
    using System.Threading;
	using ChinhDo.Transactions;

    using Nexus.Client.BackgroundTasks;
    using Nexus.Client.DownloadManagement;
    using Nexus.Client.Games;
    using Nexus.Client.ModAuthoring;
    using Nexus.Client.ModRepositories;
    using Nexus.Client.Mods;
    using Nexus.Client.Settings;
    using Nexus.Client.Util;
    using Nexus.Client.Util.Collections;

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
			public long OverallProgress { get; set; }

			/// <summary>
			/// Gets or sets the last recorded progress minimum of the download.
			/// </summary>
			/// <value>The last recorded progress minimum of the download.</value>
			public long OverallProgressMinimum { get; set; }

			/// <summary>
			/// Gets or sets the last recorded progress maximum of the download.
			/// </summary>
			/// <value>The last recorded progress maximum of the download.</value>
			public long OverallProgressMaximum { get; set; }

			/// <summary>
			/// Gets the last recorded progress of the download, adjusted to account for the progress minimum.
			/// </summary>
			/// <value>The last recorded progress of the download, adjusted to account for the progress minimum.</value>
			public long AdjustedProgress => OverallProgress - OverallProgressMinimum;

            /// <summary>
			/// Gets the last recorded progress maximum of the download, adjusted to account for the progress minimum.
			/// </summary>
			/// <value>The last recorded progress maximum of the download, adjusted to account for the progress minimum.</value>
			public long AdjustedProgressMaximum => OverallProgressMaximum - OverallProgressMinimum;

            /// <summary>
			/// Gets or sets the last recorded download speed of the download.
			/// </summary>
			/// <value>The last recorded download speed of the download.</value>
			public int DownloadSpeed { get; private set; }

			/// <summary>
			/// Gets or sets the last recorded remaining time of the download.
			/// </summary>
			/// <value>The last recorded remaining time of the download.</value>
			public TimeSpan TimeRemaining { get; private set; }

			#endregion

			#region Constructors

			/// <summary>
			/// A simple constructor that initializes teh object with the given values.
			/// </summary>
			/// <param name="task">The download task for which to store the progress state.</param>
			public DownloadProgressState(FileDownloadTask task)
			{
				Update(task);
			}

			#endregion

			/// <summary>
			/// Updates the stored state to reflect the current progress of the given download.
			/// </summary>
			/// <param name="task">The download task for which to store the progress state.</param>
			public void Update(FileDownloadTask task)
			{
				OverallProgress = task.OverallProgress;
				OverallProgressMinimum = task.OverallProgressMinimum;
				OverallProgressMaximum = task.OverallProgressMaximum;
				DownloadSpeed = task.DownloadSpeed;
				TimeRemaining = task.TimeRemaining;
			}
		}

		private readonly object _lockObject = new object();
		private readonly IGameMode _gameMode;
		private readonly IEnvironmentInfo _environmentInfo;
		private readonly ModRegistry _modRegistry;
		private readonly ReadMeManager _readMeManager;
		private readonly IModRepository _modRepository;
		private readonly IModFormatRegistry _modFormatRegistry;
		private readonly ConfirmOverwriteCallback _confirmOverwriteCallback;
		private readonly Dictionary<IBackgroundTask, DownloadProgressState> _downloaderProgress = new Dictionary<IBackgroundTask, DownloadProgressState>();
		private readonly List<IBackgroundTask> _runningTasks = new List<IBackgroundTask>();
		private bool _finishedDownloads;
		private int _overallProgressOffset;
		private readonly Uri _downloadPath;
		private readonly DateTime _startTime = DateTime.Now;
		private long _previousProgress;
		private readonly Stack<int> _previousSpeed = new Stack<int> { };
		private readonly Stopwatch _speed = new Stopwatch();
		private List<string> _fileserverCaptions = new List<string>();
		private int _hours;
		private double _minutes;
		private int _seconds;
		private long _downloadProgress;
		private long _downloadMaximum;
		private readonly int _localID;
		private string _fileserver = string.Empty;
		private readonly string _repositoryMessage = string.Empty;

		#region Statics

		private static int _counter;
		private static readonly Dictionary<string, int> _sourceUri = new Dictionary<string, int>();

		#endregion

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
		/// <value>Whether the task supports pausing.</value>
		public override bool SupportsPause { get; } = true;

        /// <summary>
		/// Gets whether the task supports queuing.
		/// </summary>
		/// <value>Whether the task supports queuing.</value>
		public override bool SupportsQueue { get; } = true;

        /// <summary>
		/// Gets the time that has elapsed downloading the file.
		/// </summary>
		/// <value>The time that has elapsed downloading the file.</value>
		protected TimeSpan ElapsedTime => DateTime.Now.Subtract(_startTime);

		/// <summary>
		/// Gets the current task speed.
		/// </summary>
		/// <value>The current task speed.</value>
		public int ETA_Hours
		{
			get => _hours;
			set
			{
				var changed = false;

				lock (_lockObject)
				{
					if (_hours != value)
					{
						changed = true;
						_hours = value;
					}
				}

				if (changed)
				{
					OnPropertyChanged(() => ETA_Hours);
				}
			}
		}

		/// <summary>
		/// Gets the current task speed.
		/// </summary>
		/// <value>The current task speed.</value>
		public double ETA_Minutes
		{
			get => _minutes;
            set
			{
				var changed = false;

                lock (_lockObject)
				{
					if (_minutes != value)
					{
						changed = true;
						_minutes = value;
					}
				}

                if (changed)
                {
                    OnPropertyChanged(() => ETA_Minutes);
                }
            }
		}

		/// <summary>
		/// Gets the current task speed.
		/// </summary>
		/// <value>The current task speed.</value>
		public int ETA_Seconds
		{
			get => _seconds;
            set
			{
				var changed = false;

                lock (_lockObject)
				{
					if (_seconds != value)
					{
						changed = true;
						_seconds = value;
					}
				}

                if (changed)
                {
                    OnPropertyChanged(() => ETA_Seconds);
                }
            }
		}

		/// <summary>
		/// Gets the current task speed.
		/// </summary>
		/// <value>The current task speed.</value>
		public long DownloadProgress
		{
			get => _downloadProgress;
            set
			{
				var changed = false;

                lock (_lockObject)
				{
					if (_downloadProgress != value)
					{
						changed = true;
						_downloadProgress = value;
					}
				}

                if (changed)
                {
                    OnPropertyChanged(() => DownloadProgress);
                }
            }
		}

		/// <summary>
		/// Gets the current task speed.
		/// </summary>
		/// <value>The current task speed.</value>
		public long DownloadMaximum
		{
			get => _downloadMaximum;
            set
			{
				var booChanged = false;

                lock (_lockObject)
				{
					if (_downloadMaximum != value)
					{
						booChanged = true;
						_downloadMaximum = value;
					}
				}

                if (booChanged)
                {
                    OnPropertyChanged(() => DownloadMaximum);
                }
            }
		}

		/// <summary>
		/// Gets the current task speed.
		/// </summary>
		/// <value>The current task speed.</value>
		public string FileServer
		{
			get => _fileserver;
            set
			{
				var changed = false;

                lock (_lockObject)
				{
					if (_fileserver != value)
					{
						changed = true;
						_fileserver = value;
					}
				}

                if (changed)
                {
                    OnPropertyChanged(() => FileServer);
                }
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

		/// <summary>
		/// Gets the current Descriptor source path.
		/// </summary>
		/// <value>The current Descriptor source path.</value>
		public string DescriptorSourcePath => Descriptor?.DefaultSourcePath;

        /// <summary>
		/// Gets the current source Uri.
		/// </summary>
		/// <value>The current source Uri.</value>
		public string SourceUri => _downloadPath != null ? _downloadPath.AbsoluteUri : string.Empty;

        #endregion

        #region Constructors

        /// <summary>
        /// A simple constructor that initializes the object with the given values.
        /// </summary>
        /// <param name="gameMode">The game mode for which mods are being managed.</param>
        /// <param name="readMeManager">The ReadMe Manager info.</param>
        /// <param name="environmentInfo">The application's envrionment info.</param>
        /// <param name="modRegistry">The <see cref="T:Nexus.Client.ModManagement.ModRegistry" /> that contains the list of managed <see cref="T:Nexus.Client.Mods.IMod" />s.</param>
        /// <param name="formatRegistry">The <see cref="T:Nexus.Client.Mods.IModFormatRegistry" /> that contains the list
        /// of supported <see cref="T:Nexus.Client.Mods.IModFormat" />s.</param>
        /// <param name="modRepository">The mod repository from which to get mods and mod metadata.</param>
        /// <param name="downloadPath">The path to the mod to add.</param>
        /// <param name="confirmOverwriteCallback">The delegate to call to resolve conflicts with existing files.</param>
        /// <inheritdoc />
        public AddModTask(IGameMode gameMode, ReadMeManager readMeManager, IEnvironmentInfo environmentInfo, ModRegistry modRegistry, IModFormatRegistry formatRegistry, IModRepository modRepository, Uri downloadPath, ConfirmOverwriteCallback confirmOverwriteCallback)
		{
			_gameMode = gameMode;
			_environmentInfo = environmentInfo;
			_modRegistry = modRegistry;
			_modFormatRegistry = formatRegistry;
			_modRepository = modRepository;
			_downloadPath = downloadPath;
			_confirmOverwriteCallback = confirmOverwriteCallback;
			_readMeManager = readMeManager;
			_localID = _counter++;
		}

		#endregion

		/// <summary>
		/// Starts the mod adding task.
		/// </summary>
		public void AddMod(bool queued)
		{
			var strNexusError = string.Empty;
			var strNexusErrorInfo = string.Empty;

            Trace.TraceInformation($"[{_downloadPath}] Starting Add Mod Task.");
			Status = TaskStatus.Running;
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			ShowItemProgress = true;
			OverallMessage = $"{_downloadPath}...";

			try
			{
				if (Descriptor == null || Descriptor.SourceUri == null || !Descriptor.SourceUri.Equals(_downloadPath))
					Descriptor = BuildDescriptor(_downloadPath);
			}
			catch (CommunicationException e)
			{
				if (((WebException) e.InnerException)?.Response?.Headers != null)
				{
					var whcHeaders = ((WebException)e.InnerException).Response.Headers;

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
				if (_modRepository.IsOffline)
				{
					Pause();
					return;
				}

                ErrorCode = strNexusError;
                ErrorInfo = strNexusErrorInfo;
                Status = TaskStatus.Error;
                
                if (!string.IsNullOrEmpty(strNexusErrorInfo))
                {
                    OverallMessage = strNexusErrorInfo;
                    OnTaskEnded(strNexusErrorInfo, null);
                }
                else
                {
                    OverallMessage = $"Server Unreachable: {_downloadPath}";
                    OnTaskEnded($"Server Unreachable: {_downloadPath}", null);
                }

                return;
            }

			if (ModInfo == null || string.IsNullOrEmpty(ModInfo.FileName) || !ModInfo.FileName.Equals(Descriptor.FileName))
				ModInfo = GetModInfo(Descriptor);

			OverallMessage = $"{GetModDisplayName()}...";

			if (Descriptor.Status == TaskStatus.Error)
            {
                Cancel();
            }
            else if (Descriptor.Status == TaskStatus.Paused)
            {
                Pause();
            }
            else if (Descriptor.Status == TaskStatus.Queued || queued)
            {
                Queue();
            }
            else
			{
				if (Descriptor.DownloadFiles.IsNullOrEmpty())
				{
					OverallProgressMaximum = 5;
					AddModFile(_confirmOverwriteCallback);
				}
				else
				{
					_overallProgressOffset = 1;
					OverallProgressMaximum = 6;
					ItemProgressMaximum = 0;
					ItemMessage = $"Downloading {GetModDisplayName()}...";

					lock (_sourceUri)
                    {
                        if (_sourceUri.ContainsKey(Descriptor.SourceUri.ToString()) && _sourceUri[Descriptor.SourceUri.ToString()] != _localID)
						{
							Status = TaskStatus.Error;
							OverallMessage = $"This mod is already downloading: {GetModDisplayName()}";
							OnTaskEnded($"This mod is already downloading: {GetModDisplayName()}", null);
							return;
						}

                        if (!_sourceUri.ContainsKey(Descriptor.SourceUri.ToString()))
                        {
                            _sourceUri.Add(Descriptor.SourceUri.ToString(), _localID);
                        }
                    }

					try
					{
						DownloadFiles(Descriptor.DownloadFiles, queued);
					}
					catch
					{
						Status = TaskStatus.Error;
						OverallMessage = "There was an error downloading this file.";
						OnTaskEnded(Descriptor.SourceUri);
					}
				}
			}
		}

		/// <summary>
		/// Gets the name of the mod to use for display.
		/// </summary>
		/// <returns>The name of the mod to use for display.</returns>
		private string GetModDisplayName()
		{
			if (ModInfo == null || string.IsNullOrEmpty(ModInfo.ModName))
            {
                return Path.GetFileNameWithoutExtension(Descriptor?.DefaultSourcePath);
            }

            return ModInfo.ModName;
		}

		/// <summary>
		/// Get the repository info for the described mod.
		/// </summary>
		/// <param name="descriptor">The object that describes the mod for which to retrieve the info.</param>
		/// <returns>The repository info for the described mod.</returns>
		private IModInfo GetModInfo(AddModDescriptor descriptor)
		{
			switch (descriptor.SourceUri.Scheme.ToLowerInvariant())
			{
				case "file":
					ModInfo localInfo = null;
					localInfo = new ModInfo(_modRepository.GetModInfoForFile(descriptor.DefaultSourcePath));
					localInfo.FileName = Path.GetFileName(descriptor.DefaultSourcePath);
					return localInfo;
                case "nxm":
					NexusUrl nxuModUrl = new NexusUrl(descriptor.SourceUri);

                    if (string.IsNullOrEmpty(nxuModUrl.ModId) || string.IsNullOrEmpty(nxuModUrl.FileId))
                    {
                        throw new ArgumentException("Invalid Nexus URI: " + descriptor.SourceUri);
                    }

                    ModInfo modInfo = null;

                    if (!_modRepository.IsOffline)
                    {
                        modInfo = (ModInfo)_modRepository.GetModInfo(nxuModUrl.ModId);
                        IMod modMod = _modRegistry.RegisteredMods.Find(x => x.Id == nxuModUrl.ModId);

                        if (modMod != null && modInfo != null)
                        {
                            modInfo.IsEndorsed = modMod.IsEndorsed;
                            modInfo.UpdateWarningEnabled = modMod.UpdateWarningEnabled;
                            modInfo.UpdateChecksEnabled = modMod.UpdateChecksEnabled;
                            modInfo.CustomCategoryId = modMod.CustomCategoryId;
                        }
                        else
                        {
                            modInfo.UpdateWarningEnabled = true;
                            modInfo.UpdateChecksEnabled = true;
                        }

						modInfo.ModName = modInfo.ModName + " - " + Descriptor.ModFileName;
						modInfo.FileName = Descriptor.FileName;
						modInfo.DownloadId = nxuModUrl.FileId;
						// This is redundant we already got the all the info we need
						//var mfiFileInfo = _modRepository.GetFileInfo(nxuModUrl.ModId, nxuModUrl.FileId);
						//ModFileInfo mfiFileInfo = new ModFileInfo(modInfo.Id, modInfo.FileName, modInfo.ModName, modInfo.HumanReadableVersion);

						//if (mfiFileInfo != null)
						//{
						//	modInfo = (ModInfo)AutoTagger.CombineInfo(modInfo, mfiFileInfo);
						//}
                    }

                    return modInfo;
				default:
					Trace.TraceInformation($"[{descriptor.SourceUri}] Can't get mod info.");
					throw new Exception("Unable to retrieve mod info: " + descriptor.SourceUri);
			}
		}

		/// <summary>
		/// Build the object that describes the mod being added.
		/// </summary>
		/// <param name="path">The path of the mod being added.</param>
		/// <returns>The object that describes the mod being added.</returns>
		private AddModDescriptor BuildDescriptor(Uri path)
		{
            if (path == null)
            {
				Trace.TraceError($"{nameof(AddModTask)}.{nameof(BuildDescriptor)}: Argument \"{nameof(path)}\" was null.");
                return null;
            }

			if (!_environmentInfo.Settings.QueuedModsToAdd.ContainsKey(_gameMode.ModeId))
            {
                _environmentInfo.Settings.QueuedModsToAdd[_gameMode.ModeId] = new KeyedSettings<AddModDescriptor>();
            }

            var queuedMods = _environmentInfo.Settings.QueuedModsToAdd[_gameMode.ModeId];
			AddModDescriptor descriptor;

			if (path.Scheme.ToLowerInvariant() == "file")
			{
				if (queuedMods.TryGetValue(path.ToString(), out descriptor))
                {
                    _fileserverCaptions = descriptor.SourceName;
                }
                else
				{
					descriptor = new AddModDescriptor(path, path.LocalPath, null, TaskStatus.Running, new List<string>(), string.Empty, string.Empty);
					queuedMods[path.ToString()] = descriptor;

                    lock (_environmentInfo.Settings)
                    {
                        _environmentInfo.Settings.Save();
                    }
                }
			}
			else if (_modRepository.IsOffline)
			{
				if (queuedMods.TryGetValue(path.ToString(), out descriptor))
                {
                    _fileserverCaptions = descriptor.SourceName;
                }
            }
			else
			{
				if (queuedMods.ContainsKey(path.ToString()))
                {
                    queuedMods.Remove(path.ToString());
                }

                if (_fileserverCaptions.Count > 0)
                {
                    _fileserverCaptions.Clear();
                }

                switch (path.Scheme.ToLowerInvariant())
				{
					case "nxm":
						NexusUrl nxuModUrl = new NexusUrl(path);

						if (string.IsNullOrEmpty(nxuModUrl.ModId))
						{
							Trace.TraceError("Invalid Nexus URI: " + path);
							return null;
						}

						List<Uri> uriFilesToDownload = new List<Uri>();
						List<Pathoschild.FluentNexus.Models.ModFileDownloadLink> downloadLinks;

						IModFileInfo fileInfo = string.IsNullOrEmpty(nxuModUrl.FileId) ? _modRepository.GetDefaultFileInfo(nxuModUrl.ModId) : _modRepository.GetFileInfo(nxuModUrl.ModId, nxuModUrl.FileId);

                        if (fileInfo == null)
                        {
                            Trace.TraceInformation($"[{path}] Can't get the file: no file.");
                            return null;
                        }

						downloadLinks = _modRepository.GetFilePartInfo(nxuModUrl.ModId, fileInfo.Id, nxuModUrl.Key, nxuModUrl.Expiry);

						if (downloadLinks == null)
						{
							Trace.TraceError($"Could not retrieve download links for mod \"{nxuModUrl.ModId}\", file \"{fileInfo.Id}\".");
							return null;
						}

                        if (downloadLinks.Count > 0)
                        {
                            foreach (var link in downloadLinks)
                            {
                                if (link.Uri != null)
                                {
                                    uriFilesToDownload.Add(link.Uri);
                                    _fileserverCaptions.Add(link.CdnShortName);
                                }
                            }
                        }

                        if (uriFilesToDownload.Count <= 0)
                        {
                            return null;
                        }

                        var strSourcePath = Path.Combine(_gameMode.GameModeEnvironmentInfo.ModDownloadCacheDirectory, fileInfo.Filename);
						descriptor = new AddModDescriptor(path, strSourcePath, uriFilesToDownload, TaskStatus.Running, _fileserverCaptions, fileInfo.Name, fileInfo.Filename);
						break;
					default:
						Trace.TraceInformation($"[{path}] Can't get the file.");
						throw new Exception("Unable to retrieve file: " + path);
				}

				queuedMods[path.ToString()] = descriptor;

                lock (_environmentInfo.Settings)
                {
                    _environmentInfo.Settings.Save();
                }
            }

			return descriptor;
		}

		#region Mod Files Download

        /// <summary>
        /// Downloads the given files.
        /// </summary>
        /// <param name="files">The files to download.</param>
        /// <param name="queued"></param>
        protected void DownloadFiles(List<Uri> files, bool queued)
		{
			Trace.TraceInformation($"[{Descriptor.SourceUri}] Downloading Files.");
			Trace.TraceInformation($"[{Descriptor.SourceUri}] Launching downloading of {files[0]}.");
			var intConnections = _environmentInfo.Settings.UseMultithreadedDownloads ? _modRepository.AllowedConnections : 1;

			var downloader = new FileDownloadTask(_modRepository, intConnections, 1024 * 500, _modRepository.UserAgent);
			downloader.TaskEnded += Downloader_TaskEnded;
			downloader.PropertyChanged += Downloader_PropertyChanged;
            
			_runningTasks.Add(downloader);
			downloader.DownloadAsync(files, Path.GetDirectoryName(Descriptor.DefaultSourcePath), true);
		}

		/// <summary>
		/// Handles the <see cref="INotifyPropertyChanged.PropertyChanged"/> event of the file downloader tasks.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="PropertyChangedEventArgs"/> describing the event arguments.</param>
		private void Downloader_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (_finishedDownloads)
            {
                return;
            }

            var downloader = (FileDownloadTask)sender;

            if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.OverallProgress)) ||
				e.PropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.OverallProgressMaximum)))
			{
				long lastProgress = 0;

                if (_downloaderProgress.ContainsKey(downloader))
                {
                    lastProgress = _downloaderProgress[downloader].OverallProgress;
                }
                else if (_downloaderProgress.Count == 0)
				{
					//this means this is the first update, or the first update after
					// the task was resumed, so the current item progress can be assumed
					// to be the last progress
					lastProgress = ItemProgress;
				}
				if (lastProgress <= downloader.OverallProgress)
                {
                    if (!_downloaderProgress.ContainsKey(downloader))
                    {
                        _downloaderProgress[downloader] = new DownloadProgressState(downloader);
                    }
                    else
                    {
                        _downloaderProgress[downloader].Update(downloader);
                    }
                }

                long progress = 0;
				long progressMaximum = 0;
				var speed = 0;
				var timeRemaining = TimeSpan.Zero;

                foreach (var dpsState in _downloaderProgress.Values)
				{
					if (dpsState != null)
					{
						progress += dpsState.AdjustedProgress;
						progressMaximum += dpsState.AdjustedProgressMaximum;
						speed += dpsState.DownloadSpeed;

                        if (timeRemaining < dpsState.TimeRemaining)
                        {
                            timeRemaining = dpsState.TimeRemaining;
                        }
                    }
				}

				ItemProgress = progress;
				ItemProgressMaximum = progressMaximum;

				if (_speed.IsRunning)
				{
					if (_speed.ElapsedMilliseconds >= 1000)
					{
						if (_previousProgress == 0)
                        {
                            _previousProgress = (long)(downloader.ResumedByteCount > 0 ? downloader.ResumedByteCount : 0);
                        }

                        if (_downloaderProgress.ContainsKey(downloader))
                        {
                            TaskSpeed = (int)((double)(_downloaderProgress[downloader].AdjustedProgress - _previousProgress) / (_speed.ElapsedMilliseconds / 1000));
                        }

                        if (TaskSpeed >= 0)
						{
							if (_previousSpeed.Count == 10)
                            {
                                _previousSpeed.Pop();
                            }

                            _previousSpeed.Push(TaskSpeed);
						}

                        if (_previousSpeed.Count > 1)
                        {
                            TaskSpeed = (int)_previousSpeed.Average();
                        }

                        if (_downloaderProgress.ContainsKey(downloader))
                        {
                            _previousProgress = _downloaderProgress[downloader].AdjustedProgress;
                        }

                        _speed.Reset();
						_speed.Start();
					}
					else
                    {
                        TaskSpeed = _previousSpeed.Count > 0 && _previousSpeed.Average() > 0 ? (int)_previousSpeed.Average() : speed;
                    }
                }
				else
				{
					_speed.Start();
				}

				int hours = speed == 0 ? 99 : timeRemaining.Hours;
				double minutes = speed == 0 ? 99 : timeRemaining.Minutes;
				var seconds = speed == 0 ? 99 : timeRemaining.Seconds;

                if (ItemProgress == 0 && speed == 0)
                {
                    ItemMessage = "Starting the download...";
                }
                else if (ItemProgress == lastProgress && speed == 0)
                {
                    ItemMessage = "Resuming the download...";
                }
                else
				{
					ETA_Hours = hours;
					ETA_Minutes = minutes;
					ETA_Seconds = seconds;

					if (_downloaderProgress.ContainsKey(downloader))
					{
						DownloadProgress = _downloaderProgress[downloader].AdjustedProgress;
						DownloadMaximum = _downloaderProgress[downloader].AdjustedProgressMaximum;
					}
				}

				ActiveThreads = downloader.ActiveThreads;
			}
			else if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.OverallMessage)) ||
				     e.PropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.Status)))
            {
                switch (downloader.Status)
                {
                    case TaskStatus.Retrying:
                        OverallMessage = downloader.OverallMessage;
                        break;
                    case TaskStatus.Running:
                        OverallMessage = $"{_repositoryMessage}{GetModDisplayName()}";
                        FileServer = _fileserverCaptions[(int)downloader.ItemProgress];
                        break;
                    case TaskStatus.Paused:
                        OverallMessage = $"Paused: {GetModDisplayName()}";
                        FileServer = string.Empty;
                        break;
                }

                InnerTaskStatus = downloader.Status;
            }
		}

        /// <summary>
        /// Handles the <see cref="IBackgroundTask.TaskEnded"/> event of the file downloader tasks.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">A <see cref="TaskEndedEventArgs"/> describing the event arguments.</param>
        private void Downloader_TaskEnded(object sender, TaskEndedEventArgs e)
        {
            if (sender != null)
            {
                _runningTasks.Remove((IBackgroundTask)sender);
            }

            if (e.Status == TaskStatus.Error)
            {
                Status = e.Status;
                OverallMessage = $"\"{GetModDisplayName()}\" couldn't be downloaded: \"{e.Message}\".";
                OnTaskEnded(e.Message, e.ReturnValue);
            }
            else if (e.Status == TaskStatus.Complete)
			{
                if (e.ReturnValue != null)
                {
                    var downloadInfo = (DownloadedFileInfo)e.ReturnValue;

                    if (string.IsNullOrEmpty(Descriptor.SourcePath) && Descriptor.DownloadFiles.IndexOf(downloadInfo.URL) == 0)
                    {
                        Descriptor.SourcePath = downloadInfo.SavedFilePath;
                    }

                    Descriptor.DownloadFiles.Clear();
                    Descriptor.DownloadedFiles.Add(downloadInfo.SavedFilePath);
                }
                else
                {
                    Descriptor.DownloadedFiles.Clear();
                }

				var queuedMods = _environmentInfo.Settings.QueuedModsToAdd[_gameMode.ModeId];
				queuedMods[Descriptor.SourceUri.ToString()] = Descriptor;

                lock (_environmentInfo.Settings)
                {
                    _environmentInfo.Settings.Save();
                }

				if (Descriptor.DownloadFiles.Count == 0)
				{
					StepOverallProgress();
                    AddModFile(_confirmOverwriteCallback);
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
			else if (e.Status == TaskStatus.Paused || e.Status == TaskStatus.Queued)
			{
				var tempFiles = (string[])e.ReturnValue;
				Descriptor.PausedFiles.AddRange(tempFiles);
			}

            if (_speed.IsRunning)
            {
                _speed.Stop();
            }
		}

		#endregion

		#region Mod Addition

		/// <summary>
		/// Adds the mod file to the mod manager.
		/// </summary>
		/// <param name="p_cocConfirmOverwrite">The delegate to call to resolve conflicts with existing files.</param>
		protected void AddModFile(ConfirmOverwriteCallback p_cocConfirmOverwrite)
		{
			var strPath = string.IsNullOrEmpty(Descriptor.SourcePath) ? Descriptor.DefaultSourcePath : Descriptor.SourcePath;
			var destinationHd = string.Empty;
			_finishedDownloads = true;
			var faAttributes = new FileAttributes();

			try
			{
				CheckReadOnlyFlag(strPath);

				faAttributes = File.GetAttributes(strPath);
			}
			catch (DirectoryNotFoundException)
			{
				OverallMessage = $"Could not find a part of the path: {strPath}";
				ItemMessage = "Path error";
				Status = TaskStatus.Error;
				OnTaskEnded(OverallMessage, null);
			}

			var archive = new FileInfo(strPath);
			var destinationFreeSpace = archive.Length;

			if (Path.GetPathRoot(_gameMode.GameModeEnvironmentInfo.ModDirectory) != Path.DirectorySeparatorChar.ToString())
			{
				var driveInfo = new DriveInfo(Path.GetPathRoot(_gameMode.GameModeEnvironmentInfo.ModDirectory));
				destinationFreeSpace = driveInfo.TotalFreeSpace;
				destinationHd = driveInfo.Name;
			}

			if (destinationFreeSpace < archive.Length)
			{
				OverallMessage = $"Not enough free space on your HD: {destinationHd}";
				ItemMessage = "Not enough free space on your HD.";
				Status = TaskStatus.Error;
				OnTaskEnded(OverallMessage, null);
			}
			else if (faAttributes.ToString().IndexOf(FileAttributes.ReadOnly.ToString()) > -1)
			{
				OverallMessage = $"The archive is read only: {strPath}";
				ItemMessage = "The archive is read only";
				Status = TaskStatus.Error;
				OnTaskEnded(OverallMessage, null);
			}
			else if (!File.Exists(strPath))
			{
				OverallMessage = $"File does not exist: {strPath}";
				ItemMessage = "File does not exist";
				Status = TaskStatus.Error;
				OnTaskEnded(OverallMessage, null);
			}
			else
			{
				var mbrModBuilder = new ModBuilder(_gameMode.GameModeEnvironmentInfo, _environmentInfo, new NexusFileUtil(_environmentInfo));
				mbrModBuilder.PropertyChanged += ModBuilder_PropertyChanged;
				mbrModBuilder.TaskEnded += ModBuilder_TaskEnded;
				mbrModBuilder.BuildFromFile(_modFormatRegistry, strPath, p_cocConfirmOverwrite);
				_runningTasks.Add(mbrModBuilder);
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
            {
                OverallMessage = ((IBackgroundTask)sender).OverallMessage;
            }
            else if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.OverallProgress)))
			{
				if (OverallProgress - _overallProgressOffset < ((IBackgroundTask)sender).OverallProgress)
                {
                    OverallProgress = ((IBackgroundTask)sender).OverallProgress + _overallProgressOffset;
                }
            }
			else if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ItemMessage)))
            {
                ItemMessage = ((IBackgroundTask)sender).ItemMessage;
            }
            else if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.ItemProgress)))
            {
                ItemProgress = ((IBackgroundTask)sender).ItemProgress;
            }
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
			_runningTasks.Remove((IBackgroundTask)sender);

            if (Status == TaskStatus.Running)
			{
				if (e.Status == TaskStatus.Complete)
                {
                    RegisterModFiles((IList<string>)e.ReturnValue);
                }
                else
				{
					OverallMessage = $"{GetModDisplayName()} can't be added.";
					ItemMessage = e.Message;
					
					//if we errored while adding, let's set to incomplete so as to not
					// loose the file we download - the user may wish to do something manual
					Status = e.Status == TaskStatus.Error ? TaskStatus.Incomplete : e.Status;
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
			OverallMessage = "Adding mod to manager...";
			ItemMessage = "Registering Mods...";

            if (p_lstAddedMods != null)
			{
				ItemProgress = 0;
				ItemProgressMaximum = p_lstAddedMods.Count;

				foreach (var strMod in p_lstAddedMods)
				{
					OverallMessage = $"Adding mod: {strMod}";

					try
					{
						if (_modRegistry.RegisteredMods.SingleOrDefault(x => x.Filename == strMod) == null)
						{
							if (_environmentInfo.Settings.AddMissingInfoToMods)
                            {
                                _modRegistry.RegisterMod(strMod, ModInfo, _environmentInfo);
                            }
                            else
                            {
                                _modRegistry.RegisterMod(strMod);
                            }
                        }

						if (_readMeManager != null)
						{
							var tfmFileManager = new TxFileManager();

                            if (_readMeManager.VerifyReadMeFile(tfmFileManager, strMod))
                            {
                                _readMeManager.SaveReadMeConfig();
                            }
                        }
					}
					catch (Exception ex)
					{
						OverallMessage = string.Format("Error registering this mod: {1}" + Environment.NewLine + "Error: {0} ", ex.Message, GetModDisplayName());
						ItemMessage = "Error registering mod.";
						Status = TaskStatus.Error;
						OnTaskEnded(null, null);
						return;
					}

					StepItemProgress();
				}
			}
			StepOverallProgress();
			OverallMessage = $"{GetModDisplayName()} has been added";
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

			for (var i = _runningTasks.Count - 1; i >= 0; i--)
			{
				if (i >= _runningTasks.Count)
                {
                    continue;
                }

                var tskTask = _runningTasks[i];

                if (tskTask.Status == TaskStatus.Running || tskTask.Status == TaskStatus.Paused || tskTask.Status == TaskStatus.Incomplete || tskTask.Status == TaskStatus.Retrying || tskTask.Status == TaskStatus.Queued)
                {
                    tskTask.Cancel();
                }
            }

			OverallMessage = $"Cancelled {GetModDisplayName()}";
			ItemMessage = "Cancelled";
			Status = TaskStatus.Cancelled;

			if (Descriptor == null)
			{
				Descriptor = new AddModDescriptor(_downloadPath, string.Empty, null, Status, null, string.Empty, string.Empty);
				OverallMessage = $"Cancelled: {_downloadPath}";
				OnTaskEnded($"Cancelled: {_downloadPath}", null);
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
			for (var i = _runningTasks.Count - 1; i >= 0; i--)
			{
				if (i >= _runningTasks.Count)
                {
                    continue;
                }

                var tskTask = _runningTasks[i];
				if (tskTask.SupportsPause)
                {
                    tskTask.Pause();
                }
                else
                {
                    tskTask.Cancel();
                }
            }

            if (Descriptor != null)
            {
                OnTaskEnded(Descriptor.SourceUri);
            }
            else
            {
                OnTaskEnded(new TaskEndedEventArgs(TaskStatus.Paused, "Paused", null));
            }
        }

		/// <summary>
		/// Queues the task.
		/// </summary>
		/// <exception cref="InvalidOperationException">Thrown if the task does not support queuing.</exception>
		public override void Queue()
		{
			for (var i = _runningTasks.Count - 1; i >= 0; i--)
			{
				if (i >= _runningTasks.Count)
                {
                    continue;
                }

                var tskTask = _runningTasks[i];

                if (tskTask.SupportsQueue)
                {
                    tskTask.Queue();
                }
                else
                {
                    tskTask.Cancel();
                }
            }

			OnTaskEnded(new TaskEndedEventArgs(TaskStatus.Queued, "Queued", Descriptor?.SourceUri));
			Status = TaskStatus.Queued;
		}

		/// <summary>
		/// Resumes the task.
		/// </summary>
		/// <exception cref="InvalidOperationException">Thrown if the task is not paused.</exception>
		public override void Resume()
		{
			if (Status != TaskStatus.Paused && Status != TaskStatus.Incomplete && Status != TaskStatus.Queued)
            {
                throw new InvalidOperationException("Task is not paused.");
            }

            _runningTasks.Clear();
			_downloaderProgress.Clear();
			AddMod(false);
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
			if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.Status)) && Descriptor != null)
			{
				Descriptor.Status = Status;
				var queuedMods = _environmentInfo.Settings.QueuedModsToAdd[_gameMode.ModeId];
				queuedMods[Descriptor.SourceUri.ToString()] = Descriptor;

                lock (_environmentInfo.Settings)
                {
                    _environmentInfo.Settings.Save();
                }
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
			if (e.Status != TaskStatus.Paused && e.Status != TaskStatus.Incomplete && e.Status != TaskStatus.Queued && Descriptor != null)
			{
				if (e.Status != TaskStatus.Paused && e.Status != TaskStatus.Incomplete && e.Status != TaskStatus.Queued)
                {
                    lock (_sourceUri)
                    {
                        if (_sourceUri.ContainsKey(Descriptor.SourceUri.ToString()) && _sourceUri[Descriptor.SourceUri.ToString()] == _localID)
                        {
                            _sourceUri.Remove(Descriptor.SourceUri.ToString());
                        }
                    }
                }

                Thread.Sleep(1000);

				foreach (var strFile in Descriptor.DownloadedFiles)
                {
                    if (strFile.StartsWith(_gameMode.GameModeEnvironmentInfo.ModDownloadCacheDirectory, StringComparison.OrdinalIgnoreCase))
                    {
                        FileUtil.ForceDelete(strFile);
                    }
                }

                if (e.Status == TaskStatus.Cancelled)
				{
					if (Descriptor != null)
					{
						if (Descriptor.PausedFiles.Count > 0)
						{
							foreach (var strFile in Descriptor.PausedFiles)
                            {
                                if (strFile.StartsWith(_gameMode.GameModeEnvironmentInfo.ModDownloadCacheDirectory, StringComparison.OrdinalIgnoreCase))
                                {
                                    FileUtil.ForceDelete(strFile);
                                }
                            }

                            Descriptor.PausedFiles.Clear();
						}
						else
						{
							if (!string.IsNullOrEmpty(Descriptor.DefaultSourcePath))
							{
								if (File.Exists(Descriptor.DefaultSourcePath + ".parts"))
                                {
                                    FileUtil.ForceDelete(Descriptor.DefaultSourcePath + ".parts");
                                }

                                if (File.Exists(Descriptor.DefaultSourcePath + ".partial"))
                                {
                                    FileUtil.ForceDelete(Descriptor.DefaultSourcePath + ".partial");
                                }
                            }
						}
					}
				}

                var queuedMods = _environmentInfo.Settings.QueuedModsToAdd[_gameMode.ModeId];
				queuedMods.Remove(Descriptor.SourceUri.ToString());

                lock (_environmentInfo.Settings)
                {
                    _environmentInfo.Settings.Save();
                }
            }

			base.OnTaskEnded(e);
		}

		#region I/O

		private void CheckReadOnlyFlag(string fileName)
		{
			int retries = 0;
			while (IsFileReadOnly(fileName) && (retries < 10))
			{

				RemoveFileReadOnly(fileName);
				retries++;
				System.Threading.Tasks.Task.Delay(100);
			}
		}

		/// <summary>
		/// Returns whether a file is read-only.
		/// </summary>
		private bool IsFileReadOnly(string fileName)
		{
			// Create a new FileInfo object.
			FileInfo fiInfo = new FileInfo(fileName);

			// Return the IsReadOnly property value.
			return fiInfo.IsReadOnly;
		}

		/// <summary>
		/// Sets the read-only value of a file.
		/// </summary>
		private void RemoveFileReadOnly(string fileName)
		{
			try
			{
				// Create a new FileInfo object.
				FileInfo fInfo = new FileInfo(fileName);

				// Set the IsReadOnly property.
				fInfo.IsReadOnly = false;
			}
			catch
			{
			}
		}

		#endregion

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
			foreach (IDisposable task in _runningTasks)
            {
                task.Dispose();
            }
        }

		#endregion
	}
}
