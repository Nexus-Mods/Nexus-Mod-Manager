using System;
using Nexus.Client.Util;
using SevenZip.Sdk;

namespace Nexus.Client.Mods.Formats.OMod
{
	public partial class OMod
	{
		/// <summary>
		/// This class watches to progress of the decompression of 7zip
		/// compressed file blicks in the OMod.
		/// </summary>
		/// <remarks>
		/// In the current implementation of the compression library we are using
		/// (SevenZipSharp), even though the
		/// <see cref="SevenZip.Sdk.Compression.Lzma.Decoder.Code(System.IO.Stream, System.IO.Stream, long, long, ICodeProgress)"/>
		/// method accepts an <see cref="ICodeProgress"/> argument, it is unused. Thus
		/// progress is not reported. We have implemented here anyway so that
		/// if it is ever implemented we'll be ready.
		/// </remarks>
		public class DecoderProgressWatcher : ICodeProgress
		{
			#region Events

			/// <summary>
			/// Raised when the progress of the decoder being watched has been updated.
			/// </summary>
			public event EventHandler<EventArgs<Int32>> ProgressUpdated = delegate { };

			#endregion

			private Int64 m_intTotalSize = 0;

			#region Constructors

			/// <summary>
			/// A simple constructor that initializes the object with the given values.
			/// </summary>
			/// <param name="p_intTotalSize">The total size of the file block being decoded.</param>
			public DecoderProgressWatcher(Int64 p_intTotalSize)
			{
				m_intTotalSize = p_intTotalSize;
			}

			#endregion

			#region ICodeProgress Members

			/// <summary>
			/// Reports the progress of the decoder.
			/// </summary>
			/// <param name="inSize">The number of bytes of the compress file block that have been processed.</param>
			/// <param name="outSize">The number of uncompressed bytes generated.</param>
			public void SetProgress(long inSize, long outSize)
			{
				ProgressUpdated(this, new EventArgs<Int32>((Int32)((float)inSize / m_intTotalSize * 100.0)));
			}

			#endregion
		}
	}
}
