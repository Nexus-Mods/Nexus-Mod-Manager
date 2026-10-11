using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Nexus.Client.Util.Collections;
using Nexus.Client.Util.Threading;

namespace Nexus.Client.Util.Downloader
{
	/// <summary>
	/// Writes data to a file.
	/// </summary>
	public class FileWriter : IDisposable
	{
		/// <summary>
		/// Describes a block of data that is to be written to a file.
		/// </summary>
		protected class FileBlock : IComparable<FileBlock>
		{
			#region Properties

			/// <summary>
			/// Gets the start position in the file at which to write
			/// this block's data.
			/// </summary>
			/// <value>The start position in the file at which to write
			/// this block's data.</value>
			public Int64 StartPosition { get; private set; }

			/// <summary>
			/// Gets the data to write to the file.
			/// </summary>
			/// <value>The data to write to the file.</value>
			public byte[] Data { get; private set; }

			#endregion

			#region Constructors

			/// <summary>
			/// A simple constructor that initializes the object with the given data.
			/// </summary>
			/// <param name="p_intStartPosition">The start position in the file at which to write
			/// this block's data.</param>
			/// <param name="p_bteData">The data to write to the file.</param>
			public FileBlock(Int64 p_intStartPosition, byte[] p_bteData)
			{
				StartPosition = p_intStartPosition;
				Data = p_bteData;
			}

			#endregion

			#region IComparable<FileBlock> Members

			/// <summary>
			/// Compares this <see cref="FileBlock"/> to another.
			/// </summary>
			/// <remarks>
			/// <see cref="FileBlock"/>s are strictly ordered by their <see cref="FileBlock.StartPosition"/>s.
			/// </remarks>
			/// <param name="other">The <see cref="FileBlock"/> to which to compare this <see cref="FileBlock"/>.</param>
			/// <returns>A value less than 0 if this <see cref="FileBlock"/> is less than the other.
			/// 0 if this <see cref="FileBlock"/> is equal to the other.
			/// A value greater than 0 if this <see cref="FileBlock"/> is greater than the other.</returns>
			public int CompareTo(FileBlock other)
			{
				return StartPosition.CompareTo(other.StartPosition);
			}

			#endregion
		}

		private const int WriteBufferSize = 1024 * 1024;
		private const long MaximumQueuedBytes = 32 * 1024 * 1024;
		private const long MetadataCheckpointBytes = 8 * 1024 * 1024;
		private long m_lngQueuedBytes;
		private string m_strFilePath = null;
		private string m_strFileMetadataPath = null;
		private SortedList<FileBlock> m_sltBlocksToWrite = new SortedList<FileBlock>();
		private RangeSet m_rgsWrittenRanges = new RangeSet();
		private EventWaitHandle m_ewhProcessQueue = new EventWaitHandle(false, EventResetMode.ManualReset);
		private bool m_booIsClosing = false;
		private TrackedThread m_thdWrite = null;

		public event EventHandler UnableToWrite = delegate { };

		#region Properties

		/// <summary>
		/// Gets the total numbers of bytes that have beend written to the file.
		/// </summary>
		/// <value>The total numbers of bytes that have beend written to the file.</value>
		public UInt64 WrittenByteCount
		{
			get
			{
				lock (m_rgsWrittenRanges)
					return m_rgsWrittenRanges.TotalSize;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple consturctor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_strFilePath">The path of the file to which to write. If the
		/// file does not exist, is will be created.</param>
		/// <param name="p_strFileMetadataPath">The path of the file containing the metadata
		/// describing which parts of the file are already written.</param>
		public FileWriter(string p_strFilePath, string p_strFileMetadataPath)
		{
			m_strFilePath = p_strFilePath;
			m_strFileMetadataPath = p_strFileMetadataPath;

			if (File.Exists(p_strFileMetadataPath))
			{
				string[] strRanges = File.ReadAllLines(p_strFileMetadataPath);
				foreach (string strRange in strRanges)
				{
					string strCleanRange = strRange.Trim().Trim('\0');
					if (String.IsNullOrEmpty(strCleanRange))
						continue;
					m_rgsWrittenRanges.AddRange(Range.Parse(strCleanRange));
				}
			}

			m_thdWrite = new TrackedThread(WaitForData);
			m_thdWrite.Start();
		}

		#endregion

		#region Writing

		/// <summary>
		/// Writes blocks of data to the file being written.
		/// </summary>
		/// <remarks>
		/// This method is run on a separate thread. It waits to be signalled indicating that
		/// there is data queued to be written, writes the data, and waits for the next signal.
		///
		/// Resume metadata is checkpointed after a bounded amount of data or time, and on close.
		/// </remarks>
		protected void WaitForData()
		{
			int intRetries = 0;
			string strFolder = Path.GetDirectoryName(m_strFilePath);
			FileBlock pendingBlock = null;
			long uncheckpointedBytes = 0;
			Stopwatch checkpointTimer = Stopwatch.StartNew();
			try
			{
				if (!Directory.Exists(strFolder))
					Directory.CreateDirectory(strFolder);
				using (FileStream fsmFile = new FileStream(m_strFilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, WriteBufferSize))
				using (FileStream fsmMetaFile = File.OpenWrite(m_strFileMetadataPath))
				using (StreamWriter fsmMetaWriter = new StreamWriter(fsmMetaFile))
				{
					while (true)
					{
						try
						{
							bool closing;
							lock (m_sltBlocksToWrite)
							{
								if (pendingBlock == null && m_sltBlocksToWrite.Count > 0)
								{
									// Prefer continuing the current extent before seeking to another download thread's extent.
									int index = m_sltBlocksToWrite.IndexOf(new FileBlock(fsmFile.Position, null));
									if (index < 0)
										index = 0;
									pendingBlock = m_sltBlocksToWrite[index];
									m_sltBlocksToWrite.RemoveAt(index);
									m_lngQueuedBytes -= pendingBlock.Data.Length;
									Monitor.PulseAll(m_sltBlocksToWrite);
								}
								closing = m_booIsClosing && pendingBlock == null && m_sltBlocksToWrite.Count == 0;
								if (pendingBlock == null)
									m_ewhProcessQueue.Reset();
							}
							if (pendingBlock != null)
							{
								if (fsmFile.Position != pendingBlock.StartPosition)
									fsmFile.Seek(pendingBlock.StartPosition, SeekOrigin.Begin);
								fsmFile.Write(pendingBlock.Data, 0, pendingBlock.Data.Length);
								lock (m_rgsWrittenRanges)
									m_rgsWrittenRanges.AddRange(new Range((UInt64)pendingBlock.StartPosition, (UInt64)(pendingBlock.StartPosition + pendingBlock.Data.Length - 1)));
								uncheckpointedBytes += pendingBlock.Data.Length;
								pendingBlock = null;
							}
							if (uncheckpointedBytes > 0 && (closing || uncheckpointedBytes >= MetadataCheckpointBytes || checkpointTimer.ElapsedMilliseconds >= 1000))
							{
								CheckpointMetadata(fsmFile, fsmMetaFile, fsmMetaWriter);
								uncheckpointedBytes = 0;
								checkpointTimer.Restart();
							}
							if (closing)
								return;
							intRetries = 0;
							lock (m_sltBlocksToWrite)
							{
								if (m_sltBlocksToWrite.Count > 0 || m_booIsClosing)
									continue;
							}
							m_ewhProcessQueue.WaitOne(uncheckpointedBytes > 0 ? (int)Math.Max(1, 1000 - checkpointTimer.ElapsedMilliseconds) : Timeout.Infinite);
						}
						catch (IOException ex)
						{
							int errorCode = System.Runtime.InteropServices.Marshal.GetHRForException(ex) & 0xffff;
							if (errorCode != 32 && errorCode != 33)
								throw;
							if (++intRetries > 600)
							{
								UnableToWrite(this, EventArgs.Empty);
								return;
							}
							// Retain the failed block while waiting for the file lock to clear.
							Thread.Sleep(100);
						}
					}
				}
			}
			catch (UnauthorizedAccessException)
			{
				Trace.TraceError("Unable to write to file \"{0}\", it's already opened.", m_strFilePath);
			}
			finally
			{
				lock (m_sltBlocksToWrite)
				{
					m_booIsClosing = true;
					Monitor.PulseAll(m_sltBlocksToWrite);
				}
			}
		}

		/// <summary>
		/// Flushes downloaded data before publishing its resumable ranges using the existing metadata format.
		/// </summary>
		private void CheckpointMetadata(FileStream file, FileStream metadata, StreamWriter writer)
		{
			file.Flush();
			writer.Flush();
			metadata.Position = 0;
			metadata.SetLength(0);
			lock (m_rgsWrittenRanges)
				foreach (Range range in m_rgsWrittenRanges)
					writer.WriteLine(range.ToString());
			writer.Flush();
		}

		#endregion

		/// <summary>
		/// Closes the file writer.
		/// </summary>
		public void Close()
		{
			lock (m_sltBlocksToWrite)
			{
				m_booIsClosing = true;
				m_ewhProcessQueue.Set();
				Monitor.PulseAll(m_sltBlocksToWrite);
			}
			if (Thread.CurrentThread != m_thdWrite.Thread)
				m_thdWrite.Thread.Join();
		}

		/// <summary>
		/// Enqueues a block of data to write to the file.
		/// </summary>
		/// <param name="p_intStartPosition">The start position in the file at which to write the data.</param>
		/// <param name="p_bteData">The data to write.</param>
		public void EnqueueBlock(UInt64 p_intStartPosition, byte[] p_bteData)
		{
			lock (m_sltBlocksToWrite)
			{
				while (!m_booIsClosing && m_lngQueuedBytes > 0 && m_lngQueuedBytes + p_bteData.Length > MaximumQueuedBytes)
					Monitor.Wait(m_sltBlocksToWrite);
				if (m_booIsClosing)
					return;
				m_sltBlocksToWrite.Add(new FileBlock((Int64)p_intStartPosition, p_bteData));
				m_lngQueuedBytes += p_bteData.Length;
				m_ewhProcessQueue.Set();
			}
		}

		#region IDisposable Members

		/// <summary>
		/// Disposes of the resources used by the class.
		/// </summary>
		public void Dispose()
		{
			Close();
		}

		#endregion
	}
}
