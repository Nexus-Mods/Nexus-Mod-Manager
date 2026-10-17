using System;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;

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

		public static Witcher3ModSettingsFileReadResult Read(string absoluteFilePath, int maxBytes = DefaultMaxBytes)
		{
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
						return Result(fullPath, Witcher3ModSettingsReadStatus.TooLarge,
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
								return Result(fullPath, Witcher3ModSettingsReadStatus.TooLarge,
									"The settings file grew beyond the bounded read limit.");
						}
						bytes = memory.ToArray();
					}
				}
				var after = new FileInfo(fullPath);
				after.Refresh();
				if (!before.Exists || !after.Exists || beforeLength != bytes.Length ||
					after.Length != bytes.Length || beforeWrite != after.LastWriteTimeUtc)
					return Result(fullPath, Witcher3ModSettingsReadStatus.ChangedDuringRead,
						"The settings file changed during observation. Refresh before interpreting it.");

				Witcher3ModSettingsTextEncoding encoding;
				string text;
				if (!TryDecode(bytes, out text, out encoding))
					return Result(fullPath, Witcher3ModSettingsReadStatus.UnsupportedEncoding,
						"Unsupported or malformed text encoding; original bytes were left untouched.");
				Witcher3ModSettingsDocument document = Witcher3ModSettingsParser.Parse(text);
				if (!document.IsComplete)
					return new Witcher3ModSettingsFileReadResult(fullPath,
						Witcher3ModSettingsReadStatus.ParseRejected, document, encoding, bytes.Length,
						Hash(bytes), after.LastWriteTimeUtc, "The parser limit was reached; no order was inferred.");
				return new Witcher3ModSettingsFileReadResult(fullPath,
						Witcher3ModSettingsReadStatus.Loaded, document, encoding, bytes.Length,
						Hash(bytes), after.LastWriteTimeUtc, null);
			}
			catch (FileNotFoundException)
			{
				return Result(fullPath, Witcher3ModSettingsReadStatus.Missing, "No mods.settings file exists.");
			}
			catch (DirectoryNotFoundException)
			{
				return Result(fullPath, Witcher3ModSettingsReadStatus.Missing, "The settings directory does not exist.");
			}
			catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
				error is SecurityException || error is NotSupportedException)
			{
				return Result(fullPath, Witcher3ModSettingsReadStatus.ReadFailed, error.Message);
			}
		}

		private static Witcher3ModSettingsFileReadResult Result(string path,
			Witcher3ModSettingsReadStatus status, string diagnostic)
		{
			return new Witcher3ModSettingsFileReadResult(path, status, null, null,
				null, null, null, diagnostic);
		}

		private static bool TryDecode(byte[] bytes, out string text,
			out Witcher3ModSettingsTextEncoding encoding)
		{
			text = null;
			encoding = Witcher3ModSettingsTextEncoding.Utf8;
			if (bytes.Length >= 4 && ((bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF) ||
				(bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)))
				return false; // UTF-32 is deliberately unsupported; don't misread it as UTF-16.
			int offset = 0;
			Encoding decoder;
			if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
			{
				encoding = Witcher3ModSettingsTextEncoding.Utf8Bom;
				decoder = new UTF8Encoding(false, true);
				offset = 3;
			}
			else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
			{
				encoding = Witcher3ModSettingsTextEncoding.Utf16LittleEndianBom;
				decoder = new UnicodeEncoding(false, true, true);
				offset = 2;
			}
			else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
			{
				encoding = Witcher3ModSettingsTextEncoding.Utf16BigEndianBom;
				decoder = new UnicodeEncoding(true, true, true);
				offset = 2;
			}
			else decoder = new UTF8Encoding(false, true);
			try
			{
				text = decoder.GetString(bytes, offset, bytes.Length - offset);
				return text.IndexOf('\0') < 0; // Refuse binary or BOM-less UTF-16 disguised as UTF-8.
			}
			catch (DecoderFallbackException) { return false; }
		}

		private static string Hash(byte[] bytes)
		{
			using (var sha = SHA256.Create())
				return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", String.Empty);
		}
	}
}
