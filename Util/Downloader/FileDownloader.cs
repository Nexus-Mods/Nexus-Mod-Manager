using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;

namespace Nexus.Client.Util.Downloader
{
	/// <summary>
	/// Downloads a file.
	/// </summary>
	/// <remarks>
	/// This downloader uses multiple connections to download the file. It also supports resuming
	/// interrupted downloads.
	/// </remarks>
	public partial class FileDownloader
	{
	    private const int MaxBlockSize = 128 * 1024 * 1024;

	    public event EventHandler<CompletedDownloadEventArgs> DownloadComplete = delegate { };

		private readonly int _maxConnections; // = 5;
	    private readonly int _writeBufferSize; // = 1024;
        private readonly int _minBlockSize; // = 1000 * 1024;
	    private readonly string _userAgent;
	    private readonly Queue<Range> _requiredBlocks = new Queue<Range>();
	    private readonly List<BlockDownloader> _downloaders = new List<BlockDownloader>();

        private string _errorCode;
		private string _errorInfo;
		private string _savePath;
		private string _fileMetadataPath;
	    private ulong _initialByteCount;

	    private DateTime _startTime;
        private FileMetadata _metadata;
		private FileWriter _writer;

        #region Properties

        /// <summary>
        /// Gets the URL of the file being downloaded.
        /// </summary>
        /// <value>The URL of the file being downloaded.</value>
        public Uri URL { get; }

        /// <summary>
        /// Gets the cookies to send with the file request.
        /// </summary>
        /// <value>The cookies to send with the file request.</value>
        protected Dictionary<string, string> Cookies { get; }

		/// <summary>
		/// Gets the number of bytes that have been downloaded.
		/// </summary>
		/// <value>The number of bytes that have been downloaded.</value>
		public ulong DownloadedByteCount => _writer?.WrittenByteCount / 1024 ?? ResumedByteCount;

	    /// <summary>
		/// Gets the number of bytes that have been downloaded.
		/// </summary>
		/// <value>The number of bytes that have been downloaded.</value>
		protected ulong ByteCount => _writer?.WrittenByteCount ?? _initialByteCount;

	    /// <summary>
        /// Gets the number of bytes that have been previously downloaded.
        /// </summary>
        /// <value>The number of bytes that have been previously downloaded.</value>
        public ulong ResumedByteCount { get; private set; } = 0;

        /// <summary>
        /// Gets the number of currently active downloaders.
        /// </summary>
        /// <value>The number of currently active downloaders.</value>
        public int NumberOfActiveDownloaders => _downloaders.Count;

	    /// <summary>
		/// Gets the size of the file to download, in bytes.
		/// </summary>
		/// <value>The size of the file to download, in bytes.</value>
		public ulong FileSize => _metadata.Length;

	    /// <summary>
		/// Gets the path to which to the file will be saved.
		/// </summary>
		/// <value>The path to which to the file will be saved.</value>
		public string SavePath => Path.ChangeExtension(_savePath, null);

	    /// <summary>
		/// Gets the path to which to the file will be saved.
		/// </summary>
		/// <value>The path to which to the file will be saved.</value>
		public string[] TempFiles => new[] { _fileMetadataPath, _savePath };

	    /// <summary>
		/// Gets the time that has elapsed downloading the file.
		/// </summary>
		/// <value>The time that has elapsed downloading the file.</value>
		public TimeSpan ElapsedTime => DateTime.Now.Subtract(_startTime);

	    /// <summary>
		/// Gets the time that has elapsed downloading the file.
		/// </summary>
		/// <value>The time that has elapsed downloading the file.</value>
		public TimeSpan TimeRemaining
		{
			get
			{
				if (DownloadSpeed == 0)
					return TimeSpan.MaxValue;

			    ulong lngRemainingData = _metadata.Length - (DownloadedByteCount * 1024);

			    long lngNanoSecondsLeft = 0;

			    if (DownloadSpeed > 0)
					lngNanoSecondsLeft = (long)(lngRemainingData / (ulong)DownloadSpeed * 1000000000);

				return new TimeSpan(lngNanoSecondsLeft / 100);
			}
		}

		/// <summary>
		/// Gets the total time required to download the file.
		/// </summary>
		/// <value>The total time required to download the file.</value>
		public TimeSpan TotalTime
		{
			get
			{
				if (DownloadSpeed == 0)
					return TimeSpan.MaxValue;

			    long lngNanoSecondsLeft = (long)(_metadata.Length / (ulong)DownloadSpeed * 1000000000);

			    return new TimeSpan(lngNanoSecondsLeft / 100);
			}
		}

		/// <summary>
		/// Gets the download speed, in bytes per second.
		/// </summary>
		/// <value>The download speed, in bytes per second.</value>
		public int DownloadSpeed
		{
			get
			{
				if (_downloaders.Count == 0)
					return 0;
				ulong intDownloadedThisSession = 0;
				foreach (BlockDownloader bdlDownloader in _downloaders)
					if (bdlDownloader.DownloadedByteCount > 0)
						intDownloadedThisSession += bdlDownloader.DownloadedByteCount;
				double dblSeconds = ElapsedTime.TotalSeconds;
				double dblBytesPerSecond = intDownloadedThisSession / dblSeconds;
				return (int)dblBytesPerSecond;
			}
		}

		/// <summary>
		/// Gets whether or not the file to be downloaded exists.
		/// </summary>
		/// <value>Whether or not the file to be downloaded exists.</value>
		public bool FileExists => _metadata.Exists && (_metadata.Length > 0);

	    /// <summary>
		/// Gets whether or not the file exists on the server.
		/// </summary>
		/// <value>Whether or not the file exists on the server.</value>
		public bool FileNotFound => _metadata.NotFound;

	    /// <summary>
		/// Gets the current error code, if anything wrong happened.
		/// </summary>
		/// <value>The current error code, if anything wrong happened.</value>
		public string ErrorCode
		{
			get
			{
				if (string.IsNullOrEmpty(_errorCode))
					return ((_metadata != null) ? _metadata.NexusError : string.Empty);

			    return _errorCode;
			}
		}

		/// <summary>
		/// Gets the current error info, if anything wrong happened.
		/// </summary>
		/// <value>The current error info, if anything wrong happened.</value>
		public string ErrorInfo
		{
			get
			{
				if (string.IsNullOrEmpty(_errorInfo))
					return ((_metadata != null) ? _metadata.NexusErrorInfo : string.Empty);

			    return _errorInfo;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <remarks>
		/// If the given <paramref name="savePath"/> already exists, an attempt will be made to
		/// resume the download. If the pre-existing file is not a partial download, or the download
		/// cannot be resumed, the file will be overwritten.
		/// </remarks>
		/// <param name="url">The URL of the file to download.</param>
		/// <param name="cookies">A list of cookies that should be sent in the request to download the file.</param>
		/// <param name="savePath">The path to which to save the file.
		/// If <paramref name="useDefaultFileName"/> is <c>false</c>, this value should be a complete
		/// path, including filename. If <paramref name="useDefaultFileName"/> is <c>true</c>,
		/// this value should be the directory in which to save the file.</param>
		/// <param name="useDefaultFileName">Whether to use the file name suggested by the server.</param>
		/// <param name="maxConnections">The maximum number of connections to use to download the file.</param>
		/// <param name="minBlockSize">The minimum block size that should be downloaded at once. This should
		/// ideally be some mulitple of the available bandwidth.</param>
		/// <param name="userAgent">The current User Agent.</param>
		public FileDownloader(Uri url, Dictionary<string, string> cookies, string savePath, bool useDefaultFileName, int maxConnections, int minBlockSize, string userAgent)
		{
			_maxConnections = maxConnections;
			_minBlockSize = minBlockSize;
			_writeBufferSize = _minBlockSize * 1;
			_userAgent = userAgent;
		    _startTime = DateTime.Now;

		    URL = url;

            Cookies = cookies ?? new Dictionary<string, string>();

            Initialize(savePath, useDefaultFileName);
		}

		#endregion

		/// <summary>
		/// Sets up the initial values of the downloader.
		/// </summary>
		/// <param name="savePath">The path to which to save the file.
		/// If <paramref name="useDefaultFileName"/> is <c>false</c>, this value should be a complete
		/// path, including filename. If <paramref name="useDefaultFileName"/> is <c>true</c>,
		/// this value should be the directory in which to save the file.</param>
		/// <param name="useDefaultFileName">Whether to use the file name suggested by the server.</param>
		private void Initialize(string savePath, bool useDefaultFileName)
		{
			_metadata = GetMetadata();

			var strFilename = useDefaultFileName ? _metadata.SuggestedFileName : Path.GetFileName(savePath);

		    strFilename = Uri.UnescapeDataString(strFilename);

		    foreach (var chrInvalid in Path.GetInvalidFileNameChars())
				strFilename = strFilename.Replace(chrInvalid, '_');

		    savePath = Path.Combine(savePath, strFilename);

			_savePath = savePath + ".partial";
			_fileMetadataPath = savePath + ".parts";

		    var metaDataPathExists = File.Exists(_fileMetadataPath);

            if (!_metadata.SupportsResume)
			{
                if(metaDataPathExists)
				    File.Delete(_fileMetadataPath);

                if(File.Exists(_savePath))
			        File.Delete(_savePath);
			}

			//get the list of ranges we have already downloaded
			var rgsRanges = new RangeSet();

		    if (metaDataPathExists)
			{
				var strRanges = File.ReadAllLines(_fileMetadataPath);

			    foreach (var strRange in strRanges)
				{
					var strCleanRange = strRange.Trim().Trim('\0');

				    if (string.IsNullOrEmpty(strCleanRange))
						continue;

				    rgsRanges.AddRange(Range.Parse(strCleanRange));
				}
			}

			_initialByteCount = rgsRanges.TotalSize;

			ResumedByteCount = rgsRanges.TotalSize / 1024;
		}

		#region Event Raising

		/// <summary>
		/// Raises the <see cref="DownloadComplete"/> event.
		/// </summary>
		/// <remarks>
		/// A completed download does not mean the entire file has been downloaded; the download
		/// may have been interrupted.
		/// </remarks>
		/// <param name="e">A <see cref="CompletedDownloadEventArgs"/> describing the event arguments.</param>
		protected virtual void OnDownloadComplete(CompletedDownloadEventArgs e)
		{
			DownloadComplete(this, e);
		}

		#endregion

		#region Download Control

		/// <summary>
		/// Starts the file download.
		/// </summary>
		public void StartDownload()
		{
			Trace.TraceInformation($"[{URL}] Downloading.");

		    if (!FileExists)
				throw new FileNotFoundException("The file to download does not exist.", URL.ToString());

			int intConnectionsToUse = _metadata.SupportsResume ? _maxConnections : 1;

		    if (ServicePointManager.DefaultConnectionLimit < 1)
				throw new Exception(string.Format("Only {0} connections can be created to the same file; {1} are wanted.", ServicePointManager.DefaultConnectionLimit, 1));

		    if (ServicePointManager.DefaultConnectionLimit < intConnectionsToUse)
				intConnectionsToUse = ServicePointManager.DefaultConnectionLimit;

			//get the list of ranges we have not already downloaded
			RangeSet rgsMissingRanges = new RangeSet();

		    rgsMissingRanges.AddRange(new Range(0, _metadata.Length - 1));

		    if (File.Exists(_fileMetadataPath))
			{
				string[] strRanges = File.ReadAllLines(_fileMetadataPath);
				foreach (string strRange in strRanges)
				{
					string strCleanRange = strRange.Trim().Trim('\0');
					if (string.IsNullOrEmpty(strCleanRange))
						continue;
					rgsMissingRanges.RemoveRange(Range.Parse(strCleanRange));
				}
			}
			else if (File.Exists(_savePath))
				File.Delete(_savePath);

			int intMinBlockSize = (int)Math.Min((ulong)_minBlockSize, rgsMissingRanges.TotalSize);
			int intBaseBlockSize = (int)Math.Max(rgsMissingRanges.TotalSize / (ulong)intConnectionsToUse, (ulong)intMinBlockSize);

		    if (intConnectionsToUse > 1)
				intBaseBlockSize = Math.Min(intBaseBlockSize, MaxBlockSize);

			//break the ranges into blocks to be downloaded
			foreach (Range rngNeeded in rgsMissingRanges)
			{
				//find out how many blocks will fit into the range
				int intBlockCount = (int)(rngNeeded.Size / (ulong)intBaseBlockSize);

			    if (intBlockCount == 0)
					intBlockCount = 1;
				
			    //there is likely to be some remainder (there are likely a fractional number of blocks
				// in the range), so lets distrubute the remainder amongst all of the blocks
				// we do this by elarging our blocksize
				ulong intBlockSize = (ulong)Math.Ceiling(rngNeeded.Size / (double)intBlockCount);
				ulong intBlockStart = rngNeeded.StartByte;

			    for (; intBlockStart + intBlockSize < rngNeeded.EndByte; intBlockStart += intBlockSize)
					_requiredBlocks.Enqueue(new Range(intBlockStart, intBlockStart + intBlockSize - 1));

			    _requiredBlocks.Enqueue(new Range(intBlockStart, rngNeeded.EndByte));
			}

			_writer = new FileWriter(_savePath, _fileMetadataPath);

			_startTime = DateTime.Now;
			
		    //spawn the downloading threads
			int intRequiredBlocks = _requiredBlocks.Count;

		    lock (_downloaders)
			{
				for (int i = 0; i < (intRequiredBlocks < intConnectionsToUse ? intRequiredBlocks : intConnectionsToUse); i++)
				{
					BlockDownloader bdrDownloader = new BlockDownloader(this, _metadata, _writer, _writeBufferSize, _userAgent);
					bdrDownloader.FinishedDownloading += new EventHandler(Downloader_FinishedDownloading);
					bdrDownloader.Start();

				    _downloaders.Add(bdrDownloader);
				}
			}
		}

		/// <summary>
		/// Stops the file download.
		/// </summary>
		public void Stop()
		{
			lock (_downloaders)
			{
				foreach (BlockDownloader bdrDownloader in _downloaders)
				{
					bdrDownloader.FinishedDownloading -= Downloader_FinishedDownloading;
					bdrDownloader.Stop();
				}
				_downloaders.Clear();
			}
			if (_writer != null)
			{
				_writer.Close();
				_writer.Dispose();
			}

            bool booGetEntireFile = _metadata.Length > 0 && (_metadata.Length - ByteCount == 0);

		    string failureMessage = string.Empty;

            if (booGetEntireFile)
			{
                if (!string.IsNullOrEmpty(_fileMetadataPath) && File.Exists(_fileMetadataPath))
                {
                    FileUtil.ForceDelete(_fileMetadataPath);
                }

				string strNewPath = Path.ChangeExtension(_savePath, null);

                try
				{
                    if (File.Exists(strNewPath))
                    {
                        FileUtil.ForceDelete(strNewPath);
                    }
				}
				finally
				{
                    try
                    {
                        File.Move(_savePath, strNewPath);
                    }
                    catch (IOException e)
                    {
                        Trace.TraceError($"[{URL}] Could not move downloaded file to its destination. Error message: \"{e.Message}\"");

                        failureMessage = e.Message;

                        booGetEntireFile = false;
                    }
				}
			}

			OnDownloadComplete(new CompletedDownloadEventArgs(booGetEntireFile, Path.ChangeExtension(_savePath, null), failureMessage));
		}

		/// <summary>
		/// Cleans up the meta files used while downloading.
		/// </summary>
		public void Cleanup()
		{
			if (!string.IsNullOrEmpty(_fileMetadataPath) && File.Exists(_fileMetadataPath))
				FileUtil.ForceDelete(_fileMetadataPath);

		    if (!string.IsNullOrEmpty(_savePath) && File.Exists(_savePath))
				FileUtil.ForceDelete(_savePath);
		}

		/// <summary>
		/// Handles the <see cref="BlockDownloader.FinishedDownloading"/> events of the
		/// <see cref="BlockDownloader"/>s being used.
		/// </summary>
		/// <remarks>
		/// This stops the file download once all the <see cref="BlockDownloader"/>s have
		/// finished.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> desciribing the event arguments.</param>
		private void Downloader_FinishedDownloading(object sender, EventArgs e)
		{
			lock (_downloaders)
			{
				if (((BlockDownloader)sender).ErrorCode == "666")
				{
					_errorCode = ((BlockDownloader)sender).ErrorCode;
					_errorInfo = ((BlockDownloader)sender).ErrorInfo;
				}

			    _downloaders.Remove((BlockDownloader)sender);
			}

			if (_downloaders.Count == 0)
				Stop();
		}

		#endregion

		/// <summary>
		/// Gets the next block in the file that needs to be downloaded.
		/// </summary>
		/// <returns>The next block in the file that needs to be downloaded.</returns>
		protected Range GetNextBlock()
		{
			lock (_requiredBlocks)
			{
			    var r = _requiredBlocks.Count == 0 ? null : _requiredBlocks.Dequeue();

			    return r;
			}
		}

		/// <summary>
		/// Gets the file's metadata.
		/// </summary>
		/// <returns>The file's metadata.</returns>
		protected FileMetadata GetMetadata()
		{
			Trace.TraceInformation($"[{URL}] Retrieving meta data.");

		    var ckcCookies = new CookieContainer();

		    foreach (var kvp in Cookies)
				ckcCookies.Add(new Cookie(kvp.Key, kvp.Value, "/", URL.Host));

		    var metaData = (HttpWebRequest)WebRequest.Create(URL);

		    metaData.CookieContainer = ckcCookies;
			metaData.Method = "HEAD";
			metaData.AddRange(0, 1);
			metaData.AllowAutoRedirect = true;
			metaData.UserAgent = _userAgent;

			FileMetadata fmiInfo = null;

			try
			{
				using (var wrpFileMetadata = (HttpWebResponse)metaData.GetResponse())
				{
				    if (wrpFileMetadata.StatusCode == HttpStatusCode.OK ||
				        wrpFileMetadata.StatusCode == HttpStatusCode.PartialContent)
				    {
				        fmiInfo = new FileMetadata(wrpFileMetadata.Headers);
				    }
				}
			}
			catch (WebException e)
			{
				using (var wrpDownload = (HttpWebResponse)e.Response)
				{
				    if (wrpDownload != null)
				    {
				        switch (wrpDownload.StatusCode)
				        {
				            case HttpStatusCode.ServiceUnavailable:
				                fmiInfo = new FileMetadata(e.Response.Headers);
				                break;
				            case HttpStatusCode.NotFound:
				                fmiInfo = new FileMetadata();
				                fmiInfo.NotFound = true;
				                break;
                            default:
                                //On an off chance there are other enums not being handled
                                Trace.TraceWarning($"[{wrpDownload.StatusCode}] wasn't handled. 0x201807081159");
                                break;
				        }
				    }
				}
			}

			if (fmiInfo == null)
				fmiInfo = new FileMetadata();

			if (string.IsNullOrEmpty(fmiInfo.SuggestedFileName))
				fmiInfo.SuggestedFileName = Uri.UnescapeDataString(URL.Segments[URL.Segments.Length - 1]);

		    return fmiInfo;
		}
	}
}
