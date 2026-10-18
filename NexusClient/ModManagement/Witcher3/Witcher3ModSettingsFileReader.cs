using System;
using System.IO;
using Nexus.Client.ModManagement.ManagedContent;

namespace Nexus.Client.ModManagement.Witcher3
{
	public enum Witcher3ModSettingsReadStatus
	{
		Loaded = 0,
		Missing = 1,
		ReadFailed = 2,
		TooLarge = 3,
		UnsupportedEncoding = 4,
		ChangedDuringRead = 5,
		ParseRejected = 6
	}

	public enum Witcher3ModSettingsTextEncoding
	{
		Utf8 = 0,
		Utf8Bom = 1,
		Utf16LittleEndianBom = 2,
		Utf16BigEndianBom = 3
	}

	/// <summary>
	/// Immutable content identity for an observed file. The SHA-256 is over ORIGINAL FILE BYTES,
	/// including the BOM; a future writer must independently re-read and compare that preimage.
	/// A timestamp match alone is never sufficient authorization to overwrite another tool's edits.
	/// </summary>
	public sealed class Witcher3ModSettingsFileReadResult
	{
		internal Witcher3ModSettingsFileReadResult(string path, Witcher3ModSettingsReadStatus status,
			Witcher3ModSettingsDocument document, Witcher3ModSettingsTextEncoding? encoding,
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

		/// <summary>Adapts the same exact-byte observation to the shared codec without another disk read.</summary>
		internal IniOrderFileObservation ToShared() => new IniOrderFileObservation(FilePath, (IniOrderReadStatus)Status,
			Document == null ? null : IniOrderParser.Parse(Document.OriginalText, Witcher3ModSettingsParser.Format),
			Encoding.HasValue ? (IniOrderTextEncoding?)Encoding.Value : null, ByteLength, ContentSha256, LastWriteTimeUtc, Diagnostic);

		public string FilePath { get; }
		public Witcher3ModSettingsReadStatus Status { get; }
		public Witcher3ModSettingsDocument Document { get; }
		public Witcher3ModSettingsTextEncoding? Encoding { get; }
		public long? ByteLength { get; }
		public string ContentSha256 { get; }
		public DateTime? LastWriteTimeUtc { get; }
		public string Diagnostic { get; }
		public bool IsLoaded => Status == Witcher3ModSettingsReadStatus.Loaded;
	}

	/// <summary>
	/// Bounded read-only observer. It NEVER creates Documents folders, synthesizes mods.settings,
	/// converts encodings, normalizes the on-disk file, or holds a file lock for a future write.
	/// The Documents root is supplied by the caller to support redirected/OneDrive profiles.
	/// </summary>
	public static class Witcher3ModSettingsFileReader
	{
		public const int DefaultMaxBytes = 2 * 1024 * 1024;

		public static string GetFilePath(string documentsDirectory)
		{
			if (String.IsNullOrWhiteSpace(documentsDirectory) || !Path.IsPathRooted(documentsDirectory))
				throw new ArgumentException("A fully resolved Documents directory is required.", nameof(documentsDirectory));
			return Path.Combine(Path.GetFullPath(documentsDirectory), "The Witcher 3", "mods.settings");
		}

		/// <summary>Adapts the shared bounded observer while retaining Witcher's public read/status/encoding contracts.</summary>
		public static Witcher3ModSettingsFileReadResult Read(string absoluteFilePath, int maxBytes = DefaultMaxBytes)
		{
			IniOrderFileObservation observed = IniOrderFileReader.Read(absoluteFilePath, Witcher3ModSettingsParser.Format, maxBytes);
			string diagnostic = observed.Diagnostic == "No order settings file exists." ? "No mods.settings file exists." : observed.Diagnostic;
			return new Witcher3ModSettingsFileReadResult(observed.FilePath, (Witcher3ModSettingsReadStatus)observed.Status,
				observed.Document == null ? null : Witcher3ModSettingsParser.FromSharedDocument(observed.Document),
				observed.Encoding.HasValue ? (Witcher3ModSettingsTextEncoding?)observed.Encoding.Value : null,
				observed.ByteLength, observed.ContentSha256, observed.LastWriteTimeUtc, diagnostic);
		}
	}
}
