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
		/// <summary>
		/// Encapsulates an action that the thread safe extractor is asked to execute.
		/// </summary>
		private class ActionPackage
		{
			#region Properties

			/// <summary>
			/// Gets the event that notifies the requester when the action has completed.
			/// </summary>
			/// <value>The event that notifies the requester when the action has completed.</value>
			public ManualResetEvent DoneEvent { get; private set; }

			/// <summary>
			/// Gets the action to execute.
			/// </summary>
			/// <value>The action to execute.</value>
			public Action Action { get; private set; }

			/// <summary>
			/// Gets or sets the exception that was caught while executing the action.
			/// </summary>
			/// <value>The exception that was caught while executing the action.</value>
			public Exception Exception { get; set; }

			#endregion

			#region Constructors

			/// <summary>
			/// A simple constructor that initializes the object with the given values.
			/// </summary>
			/// <param name="p_actAction">The action to execute.</param>
			public ActionPackage(Action p_actAction)
			{
				Action = p_actAction;
				DoneEvent = new ManualResetEvent(false);
			}

			#endregion.s
		}

		private TrackedThread m_thdExtractor = null;
		private Queue<ActionPackage> m_queEvents = null;
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
				ExecuteAction(() => { rocFileInfo = m_szeExtractor.ArchiveFileData; });
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
				ExecuteAction(() => { booIsSolid = m_szeExtractor.IsSolid; });
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
			m_queEvents = new Queue<ActionPackage>();
			m_mreEvent = new ManualResetEvent(false);
			m_thdExtractor = new TrackedThread(RunThread);
			m_thdExtractor.Thread.IsBackground = false;
			m_thdExtractor.Thread.Name = "Seven Zip Extractor";

			ActionPackage apkStart = new ActionPackage(null);
			m_queEvents.Enqueue(apkStart);
			m_thdExtractor.Start();
			apkStart.DoneEvent.WaitOne();
		}

		#endregion

		/// <summary>
		/// Executes the given action.
		/// </summary>
		/// <remarks>
		/// This method:
		/// 1) Enqueues the action in the work queue.
		/// 2) Notifies the extraction thread that there is work to do.
		/// 3) Waits to be notified that the action has completed.
		/// 4) Throws any exception that was raised while executing the action.
		/// </remarks>
		/// <param name="p_actAction">The action to get the extractor to execute.</param>
		protected void ExecuteAction(Action p_actAction)
		{
			ActionPackage apkAction = new ActionPackage(p_actAction);
			m_queEvents.Enqueue(apkAction);
			m_mreEvent.Set();
			apkAction.DoneEvent.WaitOne();
			if (apkAction.Exception != null)
				throw apkAction.Exception;
		}

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
				ActionPackage apkStartEvent = m_queEvents.Dequeue();
				apkStartEvent.DoneEvent.Set();
				while (true)
				{
					m_mreEvent.WaitOne();
					ActionPackage apkEvent = m_queEvents.Dequeue();
					if (apkEvent.Action == null)
						break;
					try
					{
						apkEvent.Action();
					}
					catch (Exception e)
					{
						apkEvent.Exception = e;
					}
					m_mreEvent.Reset();
					apkEvent.DoneEvent.Set();
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
			ExecuteAction(() => { m_szeExtractor.ExtractFile(p_intIndex, p_stmFile); });
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
			m_queEvents.Enqueue(new ActionPackage(null));
			m_mreEvent.Set();
			m_thdExtractor.Thread.Join();
		}

		#endregion
	}
}
