using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Approved destination roots for a reviewed Collection setup overlay.</summary>
	public enum CollectionSetupRoot { Unmapped = 0, Game = 1, Documents = 2, Skip = 3 }

	/// <summary>Read-only presentation of the latest recorded setup package for one Collection revision.</summary>
	public enum CollectionSetupPackageState { None = 0, Applied = 1, RecoveryRequired = 2, Restored = 3 }

	/// <summary>One archive payload and its editable, root-relative proposed destination.</summary>
	public sealed class CollectionSetupFile
	{
		public bool Selected { get; set; }
		public string Source { get; internal set; }
		public string Group { get; internal set; }
		public CollectionSetupRoot Root { get; set; }
		public string RelativePath { get; set; }
		public string Destination { get; internal set; }
		public string Status { get; internal set; }
		public string Detail { get; internal set; }
		public bool PersonalSettings { get; internal set; }
		internal byte[] Content;
		internal byte[] Before;
		internal bool Reviewed;
	}

	/// <summary>Detached setup archive, README evidence and exact physical preimages for one Collection revision.</summary>
	public sealed class CollectionSetupPackage
	{
		public string ArchiveName { get; internal set; }
		public string ArchiveHash { get; internal set; }
		public string Revision { get; internal set; }
		public string Target { get; internal set; }
		public string GameDirectory { get; internal set; }
		public string DocumentsDirectory { get; set; }
		public List<CollectionSetupFile> Files { get; internal set; }
		public string Readmes { get; internal set; }
	}

	/// <summary>Bounded archive inspection and conservative destination proposals; README prose never executes.</summary>
	internal static class CollectionSetupPackageReader
	{
		internal const int MaximumFiles = 4096;
		internal const int MaximumFileBytes = 64 * 1024 * 1024;
		internal const long MaximumTotalBytes = 128L * 1024 * 1024;
		private static readonly string[] DocumentNames = { "mods.settings", "input.settings", "dx12user.settings", "user.settings" };
		private static readonly string[] TextExtensions = { ".settings", ".ini", ".cfg", ".txt", ".xml", ".json" };

		/// <summary>Reads regular ZIP entries without extraction or accepting author-supplied absolute paths.</summary>
		internal static CollectionSetupPackage Read(string path)
		{
			var files = new List<CollectionSetupFile>();
			var readmes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			long total = 0;
			string archiveHash;
			using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
			{
				if (input.Length > MaximumTotalBytes) throw new InvalidDataException(L("ValidationTheSetupArchiveExceedsTheSupportedSize128MB", "The setup archive exceeds the supported size (128 MB)."));
				using (SHA256 sha = SHA256.Create()) archiveHash = Hex(sha.ComputeHash(input));
				input.Position = 0;
				using (var zip = new ZipArchive(input, ZipArchiveMode.Read, true))
				{
					if (zip.Entries.Count > MaximumFiles) throw new InvalidDataException(L("ValidationTheSetupArchiveContainsTooManyEntries", "The setup archive contains too many entries."));
					foreach (ZipArchiveEntry entry in zip.Entries)
					{
						string name = entry.FullName.Replace('/', '\\');
						bool directory = name.EndsWith("\\", StringComparison.Ordinal);
						name = Relative(directory ? name.TrimEnd('\\') : name);
						if (!seen.Add(name)) throw new InvalidDataException(L("DuplicatePath", "The setup archive contains duplicate paths: ") + name);
						int type = (entry.ExternalAttributes >> 16) & 0xf000;
						if ((type != 0 && type != 0x8000 && type != 0x4000) || (entry.ExternalAttributes & 0x400) != 0)
							throw new InvalidDataException(L("ValidationSetupArchivesCannotContainLinksOrSpecialFiles", "Setup archives cannot contain links or special files."));
						if (directory) { if (entry.Length != 0) throw new InvalidDataException(L("ValidationInvalidSetupDirectory", "Invalid setup directory.")); continue; }
						if (entry.Length > MaximumFileBytes || (total += entry.Length) > MaximumTotalBytes)
							throw new InvalidDataException(L("ValidationTheExpandedSetupArchiveExceedsItsSupportedSize", "The expanded setup archive exceeds its supported size."));
						byte[] bytes;
						using (Stream source = entry.Open()) bytes = ReadBounded(source, entry.Length);
						string leaf = Path.GetFileName(name);
						if (leaf.IndexOf("readme", StringComparison.OrdinalIgnoreCase) >= 0 ||
							leaf.IndexOf("instruction", StringComparison.OrdinalIgnoreCase) >= 0)
						{
							if (bytes.Length > 256 * 1024) throw new InvalidDataException(L("ValidationASetupREADMEExceeds256KB", "A setup README exceeds 256 KB."));
							readmes[name] = Decode(bytes); continue;
						}
						files.Add(new CollectionSetupFile { Source = name, Group = Path.GetDirectoryName(name), Content = bytes,
							Root = CollectionSetupRoot.Unmapped, RelativePath = leaf });
					}
				}
			}
			if (files.Count == 0) throw new InvalidDataException(L("ValidationTheSetupArchiveHasNoPayloadFiles", "The setup archive has no payload files."));
			foreach (CollectionSetupFile file in files) Propose(file, readmes);
			return new CollectionSetupPackage { ArchiveName = Path.GetFileName(path), ArchiveHash = archiveHash, Files = files,
				Readmes = String.Join(Environment.NewLine + Environment.NewLine, readmes.Select(x => x.Key + Environment.NewLine + x.Value)) };
		}

		/// <summary>Recognizes file identities and canonical path anchors, using README text only as corroborating evidence.</summary>
		private static void Propose(CollectionSetupFile file, IDictionary<string, string> readmes)
		{
			string leaf = Path.GetFileName(file.Source);
			string[] parts = file.Source.Split('\\');
			string instructions = String.Join("\n", readmes.Where(x => (String.IsNullOrEmpty(Path.GetDirectoryName(x.Key)) || file.Source.StartsWith(
				Path.GetDirectoryName(x.Key) + "\\", StringComparison.OrdinalIgnoreCase))).Select(x => x.Value));
			if (DocumentNames.Contains(leaf, StringComparer.OrdinalIgnoreCase))
			{
				file.Root = CollectionSetupRoot.Documents; file.RelativePath = leaf.ToLowerInvariant();
				file.PersonalSettings = !leaf.Equals("mods.settings", StringComparison.OrdinalIgnoreCase);
			}
			else if (leaf.Equals("dx12filelist.txt", StringComparison.OrdinalIgnoreCase) || leaf.Equals("dx11filelist.txt", StringComparison.OrdinalIgnoreCase))
			{
				file.Root = CollectionSetupRoot.Game;
				file.RelativePath = @"bin\config\r4game\user_config_matrix\pc\" + leaf;
			}
			else
			{
				int anchor = Array.FindIndex(parts, x => x.Equals("Mods", StringComparison.OrdinalIgnoreCase) ||
					x.Equals("DLC", StringComparison.OrdinalIgnoreCase) || x.Equals("bin", StringComparison.OrdinalIgnoreCase));
				if (anchor >= 0) { file.Root = CollectionSetupRoot.Game; file.RelativePath = String.Join("\\", parts.Skip(anchor)); }
				else
				{
					int mod = Array.FindIndex(parts, x => Regex.IsMatch(x, "^mod[a-z0-9_]", RegexOptions.IgnoreCase));
					if (mod >= 0 && parts.Skip(mod + 1).Any(x => x.Equals("content", StringComparison.OrdinalIgnoreCase)))
					{ file.Root = CollectionSetupRoot.Game; file.RelativePath = "Mods\\" + String.Join("\\", parts.Skip(mod)); }
				}
				if (file.Root == CollectionSetupRoot.Unmapped && instructions.IndexOf("Documents", StringComparison.OrdinalIgnoreCase) >= 0 &&
					TextExtensions.Contains(Path.GetExtension(leaf), StringComparer.OrdinalIgnoreCase))
				{ file.Root = CollectionSetupRoot.Documents; file.PersonalSettings = true; }
			}
			file.Selected = file.Root != CollectionSetupRoot.Unmapped && !file.PersonalSettings;
		}

		/// <summary>Validates a literal relative destination, rejecting Windows aliases and traversal.</summary>
		internal static string Relative(string path)
		{
			if (String.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.IndexOf(':') >= 0)
				throw new InvalidDataException(L("ValidationASetupPathMustBeRelativeToItsSelected", "A setup path must be relative to its selected destination folder."));
			string[] parts = path.Replace('/', '\\').Split('\\');
			if (parts.Any(x => x.Length == 0 || x == "." || x == ".." || x.EndsWith(".", StringComparison.Ordinal) ||
				x.EndsWith(" ", StringComparison.Ordinal) || x.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
				Regex.IsMatch(x, @"^(con|prn|aux|nul|com[0-9]|lpt[0-9])(\.|$)", RegexOptions.IgnoreCase)))
				throw new InvalidDataException(L("UnsafePath", "A setup path is unsafe: ") + path);
			return String.Join("\\", parts);
		}

		/// <summary>Bounds all stream reads, including dishonest ZIP size declarations.</summary>
		internal static byte[] ReadBounded(Stream input, long expected = -1)
		{
			using (var output = new MemoryStream())
			{
				byte[] buffer = new byte[81920]; int count;
				while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
				{
					if (output.Length + count > MaximumFileBytes || (expected >= 0 && output.Length + count > expected))
						throw new InvalidDataException(L("ValidationASetupFileExceedsItsDeclaredSizeOrSupported", "A setup file exceeds its declared size or supported limit."));
					output.Write(buffer, 0, count);
				}
				if (expected >= 0 && output.Length != expected) throw new InvalidDataException(L("ValidationASetupFileIsTruncated", "A setup file is truncated."));
				return output.ToArray();
			}
		}

		/// <summary>Decodes README text, including common Windows text encodings.</summary>
		internal static string Decode(byte[] bytes)
		{
			if (bytes == null) return String.Empty;
			if (bytes.Length >= 2 && ((bytes[0] == 255 && bytes[1] == 254) || (bytes[0] == 254 && bytes[1] == 255)))
				return (bytes[0] == 255 ? Encoding.Unicode : Encoding.BigEndianUnicode).GetString(bytes, 2, bytes.Length - 2);
			try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
			catch (DecoderFallbackException) { return Encoding.GetEncoding(1252).GetString(bytes); }
		}

		/// <summary>Returns an exact byte identity, preserving the distinction between absent and empty files.</summary>
		internal static string Hash(byte[] bytes) { if (bytes == null) return null; using (SHA256 sha = SHA256.Create()) return Hex(sha.ComputeHash(bytes)); }
		/// <summary>Resolves localized archive validation messages.</summary>
		private static string L(string key, string text) { return LanguageManager.Get("Collections.Setup." + key, text); }
		/// <summary>Formats SHA-256 without culture-dependent text.</summary>
		private static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", String.Empty).ToLowerInvariant(); }
	}
}
