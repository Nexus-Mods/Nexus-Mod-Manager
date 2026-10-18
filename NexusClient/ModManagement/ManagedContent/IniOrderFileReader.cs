using System;
using System.IO;
using System.Security;

namespace Nexus.Client.ModManagement.ManagedContent
{
	/// <summary>Explicit outcomes of a bounded, read-only INI order-file observation.</summary>
	public enum IniOrderReadStatus
	{
		Loaded = 0,
		Missing = 1,
		ReadFailed = 2,
		TooLarge = 3,
		UnsupportedEncoding = 4,
		ChangedDuringRead = 5,
		ParseRejected = 6
	}

	/// <summary>
	/// Immutable content identity for an observed file. The SHA-256 is over ORIGINAL FILE BYTES,
	/// including the BOM; a future writer must independently re-read and compare that preimage.
	/// A timestamp match alone is never sufficient authorization to overwrite another tool's edits.
	/// </summary>
	public sealed class IniOrderFileObservation
	{
		/// <summary>Freezes the decoded observation and exact-byte identity without granting write permission.</summary>
		internal IniOrderFileObservation(string path, IniOrderReadStatus status,
			IniOrderDocument document, IniOrderTextEncoding? encoding,
			long? byteLength, string contentSha256, DateTime? lastWriteUtc, string diagnostic)
		{
			FilePath = path;
			Status = status;
			Document = document;
			Encoding = encoding;
			ByteLength = byteLength;
			ContentSha256 = contentSha256;
			LastWriteTimeUtc = lastWriteUtc;
			Diagnostic = diagnostic;
		}

		public string FilePath { get; }
		public IniOrderReadStatus Status { get; }
		public IniOrderDocument Document { get; }
		public IniOrderTextEncoding? Encoding { get; }
		public long? ByteLength { get; }
		public string ContentSha256 { get; }
		public DateTime? LastWriteTimeUtc { get; }
		public string Diagnostic { get; }
		public bool IsLoaded => Status == IniOrderReadStatus.Loaded;
	}

	/// <summary>Shared bounded read-only observer; never creates, normalizes, repairs or publishes an order file.</summary>
	public static class IniOrderFileReader
	{
		public const int DefaultMaxBytes = 2 * 1024 * 1024;

		/// <summary>Observes an existing file under bounded read limits, preserving encoding, ambiguity and change-during-read status.</summary>
		public static IniOrderFileObservation Read(string absoluteFilePath, IniOrderFormat format, int maxBytes = DefaultMaxBytes)
		{
			if (format == null) throw new ArgumentNullException(nameof(format));
			if (String.IsNullOrWhiteSpace(absoluteFilePath) || !Path.IsPathRooted(absoluteFilePath))
				throw new ArgumentException("A fully resolved settings file path is required.", nameof(absoluteFilePath));
			if (maxBytes < 1 || maxBytes == Int32.MaxValue)
				throw new ArgumentOutOfRangeException(nameof(maxBytes));
			string fullPath = Path.GetFullPath(absoluteFilePath);
			try
			{
				var before = new FileInfo(fullPath);
				before.Refresh();
				long beforeLength = before.Exists ? before.Length : -1;
				DateTime beforeWrite = before.Exists ? before.LastWriteTimeUtc : DateTime.MinValue;
				byte[] bytes;
				using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read,
					FileShare.ReadWrite | FileShare.Delete))
				{
					if (stream.Length > maxBytes)
						return Result(fullPath, IniOrderReadStatus.TooLarge,
							"The settings file exceeds the bounded read limit.");
					using (var memory = new MemoryStream())
					{
						var buffer = new byte[8192];
						int count;
						while ((count = stream.Read(buffer, 0, Math.Min(buffer.Length,
							maxBytes - (int)memory.Length + 1))) > 0)
						{
							memory.Write(buffer, 0, count);
							if (memory.Length > maxBytes)
								return Result(fullPath, IniOrderReadStatus.TooLarge,
									"The settings file grew beyond the bounded read limit.");
						}
						bytes = memory.ToArray();
					}
				}
				var after = new FileInfo(fullPath);
				after.Refresh();
				if (!before.Exists || !after.Exists || beforeLength != bytes.Length ||
					after.Length != bytes.Length || beforeWrite != after.LastWriteTimeUtc)
					return Result(fullPath, IniOrderReadStatus.ChangedDuringRead,
						"The settings file changed during observation. Refresh before interpreting it.");

				IniOrderTextEncoding encoding;
				string text;
				if (!IniOrderByteCodec.TryDecode(bytes, out text, out encoding))
					return Result(fullPath, IniOrderReadStatus.UnsupportedEncoding,
						"Unsupported or malformed text encoding; original bytes were left untouched.");
				IniOrderDocument document = IniOrderParser.Parse(text, format);
				if (!document.IsComplete)
					return new IniOrderFileObservation(fullPath,
						IniOrderReadStatus.ParseRejected, document, encoding, bytes.Length,
						IniOrderByteCodec.Hash(bytes), after.LastWriteTimeUtc, "The parser limit was reached; no order was inferred.");
				return new IniOrderFileObservation(fullPath,
						IniOrderReadStatus.Loaded, document, encoding, bytes.Length,
						IniOrderByteCodec.Hash(bytes), after.LastWriteTimeUtc, null);
			}
			catch (FileNotFoundException)
			{
				return Result(fullPath, IniOrderReadStatus.Missing, "No order settings file exists.");
			}
			catch (DirectoryNotFoundException)
			{
				return Result(fullPath, IniOrderReadStatus.Missing, "The settings directory does not exist.");
			}
			catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
				error is SecurityException || error is NotSupportedException)
			{
				return Result(fullPath, IniOrderReadStatus.ReadFailed, error.Message);
			}
		}

		/// <summary>Captures a read failure without synthesizing settings or guessing values.</summary>
		private static IniOrderFileObservation Result(string path,
			IniOrderReadStatus status, string diagnostic)
		{
			return new IniOrderFileObservation(path, status, null, null,
				null, null, null, diagnostic);
		}

	}
}
