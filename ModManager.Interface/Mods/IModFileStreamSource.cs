using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Nexus.Client.Mods
{
	/// <summary>Optional archive source that writes an entry directly to a caller-owned stream without temporary extraction.</summary>
	public interface IModFileStreamSource
	{
		/// <summary>Copies the exact logical file bytes to a writable stream without closing that stream.</summary>
		void CopyFileTo(string file, Stream destination);
	}

	/// <summary>Optional source that extracts selected logical files together without repeatedly decoding solid archives.</summary>
	public interface IModFileBatchSource
	{
		/// <summary>Copies logical source files to caller-owned paths, reporting extraction progress and honoring cancellation.</summary>
		void CopyFilesTo(IReadOnlyDictionary<string, string> destinations, CancellationToken cancellationToken, Action<float> progress);
	}
}
