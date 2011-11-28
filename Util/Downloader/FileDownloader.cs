using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Diagnostics;

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
		public event EventHandler<CompletedDownloadEventArgs> DownloadComplete = delegate { };

		private Int32 m_intMaxConnections = 5;
		private Int32 m_intMinBlockSize = 500 * 1024;
		private Int32 m_intWriteBufferSize = 1024;
		private Uri m_uriURL = null;
		private string m_strSavePath = null;
		private string m_strFileMetadataPath = null;
		private FileMetadata m_fmdInfo = null;
		private Int32 m_intInitialDownloadedByteCount = 0;
		private Queue<Range> m_queRequiredBlocks = new Queue<Range>();
		private List<BlockDownloader> m_lstDownloaders = new List<BlockDownloader>();
		private FileWriter m_fwrWriter = null;
		private DateTime m_dteStartTime = DateTime.Now;

		#region Properties

		/// <summary>
		/// Gets the URL of the file being downloaded.
		/// </summary>
		/// <value>The URL of the file being downloaded.</value>
		public Uri URL
		{
			get
			{
				return m_uriURL;
			}
		}

		/// <summary>
		/// Gets the cookies to send with the file request.
		/// </summary>
		/// <value>The cookies to send with the file request.</value>
		protected Dictionary<string, string> Cookies { get; private set; }

		/// <summary>
		/// Gets the number of bytes that have been downloaded.
		/// </summary>
		/// <value>The number of bytes that have been downloaded.</value>
		public Int32 DownloadedByteCount
		{
			get
			{
				return (m_fwrWriter == null) ? m_intInitialDownloadedByteCount : m_fwrWriter.WrittenByteCount;
			}
		}

		/// <summary>
		/// Gets the size of the file to download, in bytes.
		/// </summary>
		/// <value>The size of the file to download, in bytes.</value>
		public Int32 FileSize
		{
			get
			{
				return m_fmdInfo.Length;
			}
		}

		/// <summary>
		/// Gets the path to which to the file will be saved.
		/// </summary>
		/// <value>The path to which to the file will be saved.</value>
		public string SavePath
		{
			get
			{
				return Path.ChangeExtension(m_strSavePath, null);
			}
		}

		/// <summary>
		/// Gets the time that has elapsed downloading the file.
		/// </summary>
		/// <value>The time that has elapsed downloading the file.</value>
		public TimeSpan ElapsedTime
		{
			get
			{
				return DateTime.Now.Subtract(m_dteStartTime);
			}
		}

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
				Int64 lngRemainingData = m_fmdInfo.Length - DownloadedByteCount;
				Int64 lngNanoSecondsLeft = lngRemainingData / DownloadSpeed * 1000000000;
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
				Int64 lngNanoSecondsLeft = (Int64)m_fmdInfo.Length / DownloadSpeed * 1000000000;
				return new TimeSpan(lngNanoSecondsLeft / 100);
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
				if (m_lstDownloaders.Count == 0)
					return 0;
				Int32 intDownloadedThisSession = 0;
				foreach (BlockDownloader bdlDownloader in m_lstDownloaders)
					intDownloadedThisSession += bdlDownloader.DownloadedByteCount;
				double dblSeconds = ElapsedTime.TotalSeconds;
				double dblBytesPerSecond = intDownloadedThisSession / dblSeconds;
				return (Int32)dblBytesPerSecond;
			}
		}

		/// <summary>
		/// Gets whether or not the file to be downloaded exists.
		/// </summary>
		/// <value>Whether or not the file to be downloaded exists.</value>
		public bool FileExists
		{
			get
			{
				return m_fmdInfo.Exists && (m_fmdInfo.Length > 0);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <remarks>
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
		/// <param name="p_intMaxConnections">The maximum number of connections to use to download the file.</param>
		/// <param name="p_intMinBlockSize">The minimum block size that should be downloaded at once. This should
		/// ideally be some mulitple of the available bandwidth.</param>
		public FileDownloader(Uri p_uriURL, Dictionary<string, string> p_dicCookies, string p_strSavePath, bool p_booUseDefaultFileName, Int32 p_intMaxConnections, Int32 p_intMinBlockSize)
		{
			m_uriURL = p_uriURL;
			Cookies = p_dicCookies ?? new Dictionary<string, string>();
			m_intMaxConnections = p_intMaxConnections;
			m_intMinBlockSize = p_intMinBlockSize;
			m_intWriteBufferSize = m_intMinBlockSize * 1;
			Initialize(p_strSavePath, p_booUseDefaultFileName);
		}

		#endregion

		/// <summary>
		/// Sets up the initial values of the downloader.
		/// </summary>
		/// <param name="p_strSavePath">The path to which to save the file.
		/// If <paramref name="p_booUseDefaultFileName"/> is <c>false</c>, this value should be a complete
		/// path, including filename. If <paramref name="p_booUseDefaultFileName"/> is <c>true</c>,
		/// this value should be the directory in which to save the file.</param>
		/// <param name="p_booUseDefaultFileName">Whether to use the file name suggested by the server.</param>
		private void Initialize(string p_strSavePath, bool p_booUseDefaultFileName)
		{
			try
			{
				m_fmdInfo = GetMetadata();
			}
			catch (WebException)
			{
				m_fmdInfo = new FileMetadata();
				return;
			}

			string strFilename = p_booUseDefaultFileName ? m_fmdInfo.SuggestedFileName : Path.GetFileName(p_strSavePath);
			foreach (char chrInvalid in Path.GetInvalidFileNameChars())
				strFilename = strFilename.Replace(chrInvalid, '_');
			p_strSavePath = Path.Combine(p_strSavePath, strFilename);

			m_strSavePath = p_strSavePath + ".partial";
			m_strFileMetadataPath = p_strSavePath + ".parts";

			if (!m_fmdInfo.SupportsResume)
			{
				File.Delete(m_strFileMetadataPath);
				File.Delete(m_strSavePath);
			}

			//get the list of ranges we have already downloaded
			RangeSet rgsRanges = new RangeSet();
			if (File.Exists(m_strFileMetadataPath))
			{
				string[] strRanges = File.ReadAllLines(m_strFileMetadataPath);
				foreach (string strRange in strRanges)
				{
					if (String.IsNullOrEmpty(strRange))
						continue;
					rgsRanges.AddRange(Range.Parse(strRange));
				}
			}
			m_intInitialDownloadedByteCount = rgsRanges.TotalSize;
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

		/// <summary>
		/// Raises the <see cref="DownloadComplete"/> event.
		/// </summary>
		/// <remarks>
		/// A completed download does not mean the entire file has been downloaded; the download
		/// may have been interrupted.
		/// </remarks>
		/// <param name="p_booGotEntireFile">Whether the entire file has been downloaded.</param>
		/// <param name="p_strSavedFileName">The path to the downloaded file.</param>
		protected void OnDownloadComplete(bool p_booGotEntireFile, string p_strSavedFileName)
		{
			OnDownloadComplete(new CompletedDownloadEventArgs(p_booGotEntireFile, p_strSavedFileName));
		}

		#endregion

		#region Download Control

		/// <summary>
		/// Starts the file download.
		/// </summary>
		public void StartDownload()
		{
			Trace.TraceInformation(String.Format("[{0}] Downloading.", m_uriURL.ToString()));
			if (!FileExists)
				throw new FileNotFoundException("The file to download does not exist.", m_uriURL.ToString());

			Int32 intConnectionsToUse = m_fmdInfo.SupportsResume ? m_intMaxConnections : 1;
			if (ServicePointManager.DefaultConnectionLimit < intConnectionsToUse)
				throw new Exception(String.Format("Only {0} connections can be created to the same file; {1} are wanted.", ServicePointManager.DefaultConnectionLimit, intConnectionsToUse));

			//get the list of ranges we have not already downloaded
			RangeSet rgsMissingRanges = new RangeSet();
			rgsMissingRanges.AddRange(new Range(0, m_fmdInfo.Length - 1));
			if (File.Exists(m_strFileMetadataPath))
			{
				string[] strRanges = File.ReadAllLines(m_strFileMetadataPath);
				foreach (string strRange in strRanges)
					rgsMissingRanges.RemoveRange(Range.Parse(strRange));
			}
			else if (File.Exists(m_strSavePath))
				File.Delete(m_strSavePath);

			Int32 intBaseBlockSize = Math.Max(rgsMissingRanges.TotalSize / intConnectionsToUse, m_intMinBlockSize);

			//break the ranges into blocks to be downloaded
			foreach (Range rngNeeded in rgsMissingRanges)
			{
				//find out how many blocks will fit into the range
				Int32 intBlockCount = rngNeeded.Size / intBaseBlockSize;
				if (intBlockCount == 0)
					intBlockCount = 1;
				//there is likely to be some remainder (there are likely a fractional number of blocks
				// in the range), so lets distrubute the remainder amongst all of the blocks
				// we do this by elarging our blocksize
				Int32 intBlockSize = (Int32)Math.Ceiling(rngNeeded.Size / (double)intBlockCount);
				Int32 intBlockStart = rngNeeded.StartByte;
				for (; intBlockStart + intBlockSize < rngNeeded.EndByte; intBlockStart += intBlockSize)
					m_queRequiredBlocks.Enqueue(new Range(intBlockStart, intBlockStart + intBlockSize - 1));
				m_queRequiredBlocks.Enqueue(new Range(intBlockStart, rngNeeded.EndByte));
			}

			m_fwrWriter = new FileWriter(m_strSavePath, m_strFileMetadataPath);

			m_dteStartTime = DateTime.Now;
			//spawn the downloading threads
			lock (m_lstDownloaders)
			{
				for (Int32 i = 0; i < intConnectionsToUse; i++)
				{
					BlockDownloader bdrDownloader = new BlockDownloader(this, m_fmdInfo, m_fwrWriter, m_intWriteBufferSize);
					bdrDownloader.FinishedDownloading += new EventHandler(Downloader_FinishedDownloading);
					bdrDownloader.Start();
					m_lstDownloaders.Add(bdrDownloader);
				}
			}
		}

		/// <summary>
		/// Stops the file download.
		/// </summary>
		public void Stop()
		{
			lock (m_lstDownloaders)
			{
				foreach (BlockDownloader bdrDownloader in m_lstDownloaders)
				{
					bdrDownloader.FinishedDownloading -= Downloader_FinishedDownloading;
					bdrDownloader.Stop();
				}
				m_lstDownloaders.Clear();
			}
			if (m_fwrWriter != null)
				m_fwrWriter.Close();
			bool booGetEntireFile = (m_fmdInfo.Length - DownloadedByteCount == 0);
			if (booGetEntireFile)
			{
				if (!String.IsNullOrEmpty(m_strFileMetadataPath) && File.Exists(m_strFileMetadataPath))
					FileUtil.ForceDelete(m_strFileMetadataPath);
				string strNewPath = Path.ChangeExtension(m_strSavePath, null);
				if (File.Exists(strNewPath))
					FileUtil.ForceDelete(strNewPath);
				File.Move(m_strSavePath, strNewPath);
			}
			OnDownloadComplete(booGetEntireFile, Path.ChangeExtension(m_strSavePath, null));
		}

		/// <summary>
		/// Cleans up the meta files used while downloading.
		/// </summary>
		public void Cleanup()
		{
			if (!String.IsNullOrEmpty(m_strFileMetadataPath) && File.Exists(m_strFileMetadataPath))
				FileUtil.ForceDelete(m_strFileMetadataPath);
			FileUtil.ForceDelete(m_strSavePath);
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
			lock (m_lstDownloaders)
			{
				m_lstDownloaders.Remove((BlockDownloader)sender);
			}
			if (m_lstDownloaders.Count == 0)
				Stop();
		}

		#endregion

		/// <summary>
		/// Gets the next block in the file that needs to be downloaded.
		/// </summary>
		/// <returns>The next block in the file that needs to be downloaded.</returns>
		protected Range GetNextBlock()
		{
			lock (m_queRequiredBlocks)
			{
				if (m_queRequiredBlocks.Count == 0)
					return null;
				return m_queRequiredBlocks.Dequeue();
			}
		}

		/// <summary>
		/// Gets the file's metadata.
		/// </summary>
		/// <returns>The file's metadata.</returns>
		protected FileMetadata GetMetadata()
		{
			Trace.TraceInformation(String.Format("[{0}] Retreiving metadata.", m_uriURL.ToString()));
			HttpWebRequest hwrFileMetadata = (HttpWebRequest)WebRequest.Create(m_uriURL);
			CookieContainer ckcCookies = new CookieContainer();
			foreach (KeyValuePair<string, string> kvpCookie in Cookies)
				ckcCookies.Add(new Cookie(kvpCookie.Key, kvpCookie.Value, "/", m_uriURL.Host));
			hwrFileMetadata.CookieContainer = ckcCookies;
			hwrFileMetadata.Method = "HEAD";
			hwrFileMetadata.AddRange(0, 1);
			hwrFileMetadata.AllowAutoRedirect = true;

			FileMetadata fmiInfo = null;
			using (HttpWebResponse wrpFileMetadata = (HttpWebResponse)hwrFileMetadata.GetResponse())
			{
				if ((wrpFileMetadata.StatusCode == HttpStatusCode.OK) || (wrpFileMetadata.StatusCode == HttpStatusCode.PartialContent))
					fmiInfo = new FileMetadata(wrpFileMetadata.Headers);
			}
			if (fmiInfo == null)
				fmiInfo = new FileMetadata();
			if (String.IsNullOrEmpty(fmiInfo.SuggestedFileName))
				fmiInfo.SuggestedFileName = m_uriURL.Segments[m_uriURL.Segments.Length - 1];
			return fmiInfo;
		}
	}
}
