using System;
using System.Diagnostics;
using System.IO;
using Nexus.Client.Util;

namespace Nexus.Client
{
	/// <summary>
	/// A trace listener that doesn't print the header info.
	/// </summary>
	public class HeaderlessTextWriterTraceListener : TextWriterTraceListener
	{
		private bool m_booSaveToFile = false;
		private Int64 m_intLastStreamPosition = 0;

		#region Properties

		/// <summary>
		/// Gets the path of the file to which the listener writes.
		/// </summary>
		/// <value>The path of the file to which the listener writes.</value>
		public string FilePath { get; private set; }

		/// <summary>
		/// Gets the stream to which the tracec info has been written.
		/// </summary>
		/// <value>The stream to which the tracec info has been written.</value>
		protected MemoryStream TraceStream { get; private set; }

		/// <summary>
		/// Gets whether the trace is being forced.
		/// </summary>
		/// <remarks>
		/// If it's not being forced, it should only be written to file in the case of a crash.
		/// </remarks>
		/// <value>Whether the trace is being forced.</value>
		public bool TraceIsForced
		{
			get
			{
				return TraceStream == null;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the obejct with the given values.
		/// </summary>
		/// <param name="p_strTraceFilePath">The path of the file to which to write trace info.</param>
		public HeaderlessTextWriterTraceListener(string p_strTraceFilePath)
			: base(p_strTraceFilePath)
		{
			FilePath = p_strTraceFilePath;
		}

		/// <summary>
		/// A simple constructor that initializes the obejct with the given values.
		/// </summary>
		/// <remarks>
		/// When this constuctor is used, the trace data will only be written to the given path
		/// when <see cref="SaveToFile()"/> is called.
		/// </remarks>
		/// <param name="p_msmTraceInfo">The stream to which to send trace info.</param>
		/// <param name="p_strTraceFilePath">The path of the file to which to write trace info.</param>
		public HeaderlessTextWriterTraceListener(MemoryStream p_msmTraceInfo, string p_strTraceFilePath)
			: base(p_msmTraceInfo)
		{
			TraceStream = p_msmTraceInfo;
			FilePath = p_strTraceFilePath;
		}

		#endregion

		/// <summary>
		/// Writes the given messsage, if it isn't the header info.
		/// </summary>
		/// <param name="message">The message to write.</param>
		public override void Write(string message)
		{
			if (message.StartsWith(Path.GetFileName(Environment.GetCommandLineArgs()[0])))
				return;
			base.Write(message);
		}

		/// <summary>
		/// Saves the trace info to the file.
		/// </summary>
		public void SaveToFile()
		{
			if (TraceIsForced)
				throw new InvalidOperationException("This mehtod can olny be called if the HeaderlessTextWriterTraceListener(MemoryStream, string) constructor was used.");
			m_booSaveToFile = true;
			Flush();
		}

		/// <summary>
		/// Changes the file where the trace is saved.
		/// </summary>
		/// <param name="p_strNewFileName">The new filename.</param>
		public void ChangeFilePath(string p_strNewFileName)
		{
			if (TraceIsForced || m_booSaveToFile)
				throw new InvalidOperationException("The trace has already been written. Cannot change file path.");
			FilePath = p_strNewFileName;
		}

		/// <summary>
		/// Writes all remaining data to the underlying listener.
		/// </summary>
		public override void Flush()
		{
			base.Flush();
			if (!TraceIsForced && m_booSaveToFile)
			{
				Int64 intPosition = TraceStream.Position;
				byte[] bteData = new byte[intPosition - m_intLastStreamPosition];
				Array.Copy(TraceStream.GetBuffer(), m_intLastStreamPosition, bteData, 0, bteData.Length);
				File.AppendAllText(FilePath, TextUtil.ByteToString(bteData));
				m_intLastStreamPosition = intPosition;
			}
		}
	}
}
