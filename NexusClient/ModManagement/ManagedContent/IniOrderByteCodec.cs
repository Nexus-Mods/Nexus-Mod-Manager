using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Nexus.Client.ModManagement.ManagedContent
{
	/// <summary>Exact original-byte encodings supported by the bounded INI order reader.</summary>
	public enum IniOrderTextEncoding
	{
		Utf8 = 0,
		Utf8Bom = 1,
		Utf16LittleEndianBom = 2,
		Utf16BigEndianBom = 3
	}

	/// <summary>Strict encoding and SHA-256 mechanics shared by read-only observations and exact-byte previews; no file I/O.</summary>
	public static class IniOrderByteCodec
	{
		/// <summary>Reproduces the selected original BOM and encoding without a fallback conversion.</summary>
		public static byte[] Encode(string text, IniOrderTextEncoding kind)
		{
			if (text == null) throw new ArgumentNullException(nameof(text));
			Encoding encoding;
			switch (kind)
			{
				case IniOrderTextEncoding.Utf8: encoding = new UTF8Encoding(false, true); break;
				case IniOrderTextEncoding.Utf8Bom: encoding = new UTF8Encoding(true, true); break;
				case IniOrderTextEncoding.Utf16LittleEndianBom:
					encoding = new UnicodeEncoding(false, true, true); break;
				case IniOrderTextEncoding.Utf16BigEndianBom:
					encoding = new UnicodeEncoding(true, true, true); break;
				default: throw new ArgumentOutOfRangeException(nameof(kind));
			}
			return encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
		}

		/// <summary>Decodes strict UTF-8 or BOM-marked UTF-16, rejecting UTF-32 and NUL-containing/binary input.</summary>
		public static bool TryDecode(byte[] bytes, out string text,
			out IniOrderTextEncoding encoding)
		{
			if (bytes == null) throw new ArgumentNullException(nameof(bytes));
			text = null;
			encoding = IniOrderTextEncoding.Utf8;
			if (bytes.Length >= 4 && ((bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF) ||
				(bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)))
				return false; // UTF-32 is deliberately unsupported; don't misread it as UTF-16.
			int offset = 0;
			Encoding decoder;
			if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
			{
				encoding = IniOrderTextEncoding.Utf8Bom;
				decoder = new UTF8Encoding(false, true);
				offset = 3;
			}
			else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
			{
				encoding = IniOrderTextEncoding.Utf16LittleEndianBom;
				decoder = new UnicodeEncoding(false, true, true);
				offset = 2;
			}
			else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
			{
				encoding = IniOrderTextEncoding.Utf16BigEndianBom;
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

		/// <summary>Hashes every original byte, including the BOM, for a detached preimage identity.</summary>
		public static string Hash(byte[] bytes)
		{
			if (bytes == null) throw new ArgumentNullException(nameof(bytes));
			using (var sha = SHA256.Create())
				return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", String.Empty);
		}
	}
}
