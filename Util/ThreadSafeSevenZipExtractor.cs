using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using Nexus.Client.Util.Threading;
using SevenZip;

namespace Nexus.Client.Util
{
	/// <summary>
	/// A wrapper for the <see cref="SevenZipExtractor"/> class that makes using it
	/// thread safe.
	/// </summary>
	public class ThreadSafeSevenZipExtractor : IDisposable
	{
		private TrackedThread m_thdExtractor = null;
		private Queue<KeyValuePair<Action<object>, ManualResetEvent>> m_queEvents = null;
		private ManualResetEvent m_mreEvent = null;
		private SevenZipExtractor m_szeExtractor = null;
		private string m_strPath = null;
		private Stream m_stmArchive = null;

		#region Properties

		/// <summary>
		/// Gets the <see cref="SevenZipExtractor"/> that is being made thread safe.
		/// </summary>
		/// <value>The <see cref="SevenZipExtractor"/> that is being made thread safe.</value>
		public SevenZipExtractor Extractor
		{
			get
			{
				return m_szeExtractor;
			}
		}

		/// <summary>
		/// Gets the list of data describing the files in the archive.
		/// </summary>
		/// <remarks>
		/// This wrapper property ensures the operation executes on the same thread in which the
		/// <see cref="SevenZipExtractor"/> was created.
		/// </remarks>
		/// <value>The list of data describing the files in the archive.</value>
		/// <seealso cref="SevenZipExtractor.ArchiveFileData"/>
		public ReadOnlyCollection<ArchiveFileInfo> ArchiveFileData
		{
			get
			{
				ReadOnlyCollection<ArchiveFileInfo> rocFileInfo = null;
				ManualResetEvent mreDoneEvent = new ManualResetEvent(false);
				m_queEvents.Enqueue(new KeyValuePair<Action<object>, ManualResetEvent>((o) => { rocFileInfo = m_szeExtractor.ArchiveFileData; }, mreDoneEvent));
				m_mreEvent.Set();
				mreDoneEvent.WaitOne();
				return rocFileInfo;
			}
		}

		/// <summary>
		/// Gets whether the archive is solid.
		/// </summary>
		/// <remarks>
		/// This wrapper property ensures the operation executes on the same thread in which the
		/// <see cref="SevenZipExtractor"/> was created.
		/// </remarks>
		/// <value><c>true</c> if the archive is solid; <c>false</c> otherwise.</value>
		/// <seealso cref="SevenZipExtractor.IsSolid"/>
		public bool IsSolid
		{
			get
			{
				bool booIsSolid = false;
				ManualResetEvent mreDoneEvent = new ManualResetEvent(false);
				m_queEvents.Enqueue(new KeyValuePair<Action<object>, ManualResetEvent>((o) => { booIsSolid = m_szeExtractor.IsSolid; }, mreDoneEvent));
				m_mreEvent.Set();
				mreDoneEvent.WaitOne();
				return booIsSolid;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// Creates a thread safe extractor for the file at the given path.
		/// </summary>
		/// <param name="p_strPath">The path to the file for which to create an extractor.</param>
		public ThreadSafeSevenZipExtractor(string p_strPath)
		{
			m_strPath = p_strPath;
			Init();
		}

		/// <summary>
		/// Creates a thread safe extractor for the given stream.
		/// </summary>
		/// <param name="p_stmArchive">The stream for which to create an extractor.</param>
		public ThreadSafeSevenZipExtractor(Stream p_stmArchive)
		{
			m_stmArchive = p_stmArchive;
			Init();
		}

		/// <summary>
		/// Initializes the thread safe extractor.
		/// </summary>
		protected void Init()
		{
			m_queEvents = new Queue<KeyValuePair<Action<object>, ManualResetEvent>>();
			m_mreEvent = new ManualResetEvent(false);
			m_thdExtractor = new TrackedThread(RunThread);
			m_thdExtractor.Thread.IsBackground = false;
			m_thdExtractor.Thread.Name = "Seven Zip Extractor";

			ManualResetEvent mreDoneStart = new ManualResetEvent(false);
			m_queEvents.Enqueue(new KeyValuePair<Action<object>, ManualResetEvent>(null, mreDoneStart));
			m_thdExtractor.Start();
			mreDoneStart.WaitOne();
		}

		#endregion

		/// <summary>
		/// The run method of the thread on which the <see cref="SevenZipExtractor"/> is created.
		/// </summary>
		/// <remarks>
		/// This method creates a <see cref="SevenZipExtractor"/> and then watches for events to execute.
		/// Other methods signal the thread that an action needs to be taken, and this thread executes said
		/// actions.
		/// </remarks>
		protected void RunThread()
		{
			m_szeExtractor = String.IsNullOrEmpty(m_strPath) ? new SevenZipExtractor(m_stmArchive) : new SevenZipExtractor(m_strPath);
			try
			{
				KeyValuePair<Action<object>, ManualResetEvent> kvpStartEvent = m_queEvents.Dequeue();
				kvpStartEvent.Value.Set();
				while (true)
				{
					m_mreEvent.WaitOne();
					KeyValuePair<Action<object>, ManualResetEvent> kvpEvent = m_queEvents.Dequeue();
					if (kvpEvent.Key == null)
						break;
					kvpEvent.Key(null);
					m_mreEvent.Reset();
					kvpEvent.Value.Set();
				}
			}
			finally
			{
				m_szeExtractor.Dispose();
			}
		}

		/// <summary>
		/// Extracts the specified file to the given stream.
		/// </summary>
		/// <remarks>
		/// This wrapper property ensures the operation executes on the same thread in which the
		/// <see cref="SevenZipExtractor"/> was created.
		/// </remarks>
		/// <param name="p_intIndex">The index of the file to extract from the archive.</param>
		/// <param name="p_stmFile">The stream to which to extract the file.</param>
		public void ExtractFile(Int32 p_intIndex, Stream p_stmFile)
		{
			ManualResetEvent mreDoneEvent = new ManualResetEvent(false);
			m_queEvents.Enqueue(new KeyValuePair<Action<object>, ManualResetEvent>((o) => { m_szeExtractor.ExtractFile(p_intIndex, p_stmFile); }, mreDoneEvent));
			m_mreEvent.Set();
			mreDoneEvent.WaitOne();
		}

		#region IDisposable Members

		/// <summary>
		/// Ensures all used resources are released.
		/// </summary>
		/// <remarks>
		/// This terminates the thread upon which the <see cref="SevenZipExtractor"/> was created.
		/// </remarks>
		public void Dispose()
		{
			m_queEvents.Enqueue(new KeyValuePair<Action<object>, ManualResetEvent>(null, null));
			m_mreEvent.Set();
			m_thdExtractor.Thread.Join();
		}

		#endregion
	}
}
