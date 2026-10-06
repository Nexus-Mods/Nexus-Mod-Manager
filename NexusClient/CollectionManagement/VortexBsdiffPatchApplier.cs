using System;
using System.IO;
using System.Text;
using SevenZip;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Bounded decoder for the classic BSDIFF40 payloads emitted by Vortex Collections.</summary>
	internal static class VortexBsdiffPatchApplier
	{
		internal const long DefaultMaximumOutputBytes = 1024L * 1024L * 1024L;
		private static readonly byte[] Magic = Encoding.ASCII.GetBytes("BSDIFF40");

		public static byte[] Apply(byte[] source, byte[] patch)
		{
			return Apply(source, patch, DefaultMaximumOutputBytes);
		}

		internal static byte[] Apply(byte[] source, byte[] patch, long maximumOutputBytes)
		{
			if (source == null) throw new ArgumentNullException(nameof(source));
			if (patch == null) throw new ArgumentNullException(nameof(patch));
			if (maximumOutputBytes <= 0 || maximumOutputBytes > Int32.MaxValue) throw new ArgumentOutOfRangeException(nameof(maximumOutputBytes));
			if (patch.Length < 32) throw new InvalidDataException("The Vortex binary patch is truncated before its BSDIFF40 header.");
			for (int i = 0; i < Magic.Length; i++) if (patch[i] != Magic[i]) throw new InvalidDataException("The Vortex binary patch does not use BSDIFF40 format.");

			long controlLength = ReadBsdiffInt64(patch, 8);
			long diffLength = ReadBsdiffInt64(patch, 16);
			long outputLength = ReadBsdiffInt64(patch, 24);
			if (controlLength < 0 || diffLength < 0 || outputLength < 0 || outputLength > maximumOutputBytes || outputLength > Int32.MaxValue)
				throw new InvalidDataException("The BSDIFF40 header contains an invalid or unsafe block/output length.");
			long extraOffset = 32L + controlLength + diffLength;
			if (32L + controlLength > patch.LongLength || extraOffset > patch.LongLength)
				throw new InvalidDataException("The BSDIFF40 compressed block lengths exceed the patch payload.");

			long maximumControlBytes = Math.Min(64L * 1024L * 1024L, Math.Max(24L, checked(outputLength * 24L + 24L)));
			long maximumDataBlockBytes = Math.Max(1L, outputLength);
			byte[] control = DecompressBzip2(patch, 32, checked((int)controlLength), maximumControlBytes);
			byte[] diff = DecompressBzip2(patch, checked((int)(32 + controlLength)), checked((int)diffLength), maximumDataBlockBytes);
			byte[] extra = DecompressBzip2(patch, checked((int)extraOffset), checked((int)(patch.LongLength - extraOffset)), maximumDataBlockBytes);
			byte[] output = new byte[(int)outputLength];
			long oldPosition = 0;
			int newPosition = 0, controlPosition = 0, diffPosition = 0, extraPosition = 0;
			while (newPosition < output.Length)
			{
				if (controlPosition + 24 > control.Length) throw new InvalidDataException("The BSDIFF40 control stream is truncated.");
				long addLength64 = ReadBsdiffInt64(control, controlPosition); controlPosition += 8;
				long copyLength64 = ReadBsdiffInt64(control, controlPosition); controlPosition += 8;
				long seekLength64 = ReadBsdiffInt64(control, controlPosition); controlPosition += 8;
				if (addLength64 < 0 || copyLength64 < 0 || addLength64 > Int32.MaxValue || copyLength64 > Int32.MaxValue)
					throw new InvalidDataException("The BSDIFF40 control stream contains an invalid copy length.");
				int addLength = (int)addLength64, copyLength = (int)copyLength64;
				if (addLength == 0 && copyLength == 0)
					throw new InvalidDataException("The BSDIFF40 control stream does not advance the output position.");
				if ((long)newPosition + addLength > output.Length || (long)diffPosition + addLength > diff.Length)
					throw new InvalidDataException("The BSDIFF40 diff stream exceeds its declared output bounds.");
				for (int i = 0; i < addLength; i++)
				{
					long oldIndex = oldPosition + i;
					int oldByte = oldIndex >= 0 && oldIndex < source.LongLength ? source[(int)oldIndex] : 0;
					output[newPosition + i] = unchecked((byte)(diff[diffPosition + i] + oldByte));
				}
				newPosition += addLength; oldPosition = checked(oldPosition + addLength); diffPosition += addLength;
				if ((long)newPosition + copyLength > output.Length || (long)extraPosition + copyLength > extra.Length)
					throw new InvalidDataException("The BSDIFF40 extra stream exceeds its declared output bounds.");
				Buffer.BlockCopy(extra, extraPosition, output, newPosition, copyLength);
				newPosition += copyLength; extraPosition += copyLength;
				try { oldPosition = checked(oldPosition + seekLength64); }
				catch (OverflowException ex) { throw new InvalidDataException("The BSDIFF40 source seek exceeds supported bounds.", ex); }
			}
			return output;
		}

		private static byte[] DecompressBzip2(byte[] payload, int offset, int count, long maximumDecodedBytes)
		{
			if (count <= 0) throw new InvalidDataException("A BSDIFF40 compressed block is empty.");
			if (maximumDecodedBytes <= 0 || maximumDecodedBytes > Int32.MaxValue) throw new InvalidDataException("A BSDIFF40 decoded-block bound is invalid.");
			using (var compressed = new MemoryStream(payload, offset, count, false))
			using (var extractor = new SevenZipExtractor(compressed, false, InArchiveFormat.BZip2))
			using (var output = new BoundedMemoryStream(maximumDecodedBytes))
			{
				if (extractor.ArchiveFileData == null || extractor.ArchiveFileData.Count != 1)
					throw new InvalidDataException("A BSDIFF40 bzip2 block did not decode as one bounded stream.");
				extractor.ExtractFile(extractor.ArchiveFileData[0].Index, output);
				return output.ToArray();
			}
		}

		private sealed class BoundedMemoryStream : MemoryStream
		{
			private readonly long _maximumLength;
			public BoundedMemoryStream(long maximumLength) { _maximumLength = maximumLength; }
			public override void Write(byte[] buffer, int offset, int count)
			{
				if (count < 0 || Position > _maximumLength - count) throw new InvalidDataException("A BSDIFF40 bzip2 block exceeds its decoded-size bound.");
				base.Write(buffer, offset, count);
			}
			public override void WriteByte(byte value)
			{
				if (Position >= _maximumLength) throw new InvalidDataException("A BSDIFF40 bzip2 block exceeds its decoded-size bound.");
				base.WriteByte(value);
			}
			public override void SetLength(long value)
			{
				if (value < 0 || value > _maximumLength) throw new InvalidDataException("A BSDIFF40 bzip2 block exceeds its decoded-size bound.");
				base.SetLength(value);
			}
		}

		private static long ReadBsdiffInt64(byte[] buffer, int offset)
		{
			if (offset < 0 || offset + 8 > buffer.Length) throw new EndOfStreamException();
			long value = buffer[offset + 7] & 0x7f;
			for (int i = 6; i >= 0; i--) value = value * 256 + buffer[offset + i];
			return (buffer[offset + 7] & 0x80) != 0 ? -value : value;
		}
	}

	/// <summary>Small dependency-free CRC32 used only to validate Vortex patch source bytes.</summary>
	internal static class VortexCrc32
	{
		private static readonly uint[] Table = BuildTable();
		public static string ComputeUpperHex(byte[] bytes)
		{
			if (bytes == null) throw new ArgumentNullException(nameof(bytes));
			uint crc = 0xffffffffu;
			for (int i = 0; i < bytes.Length; i++) crc = Table[(int)((crc ^ bytes[i]) & 0xff)] ^ (crc >> 8);
			return (~crc).ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
		}
		private static uint[] BuildTable()
		{
			var table = new uint[256];
			for (int i = 0; i < table.Length; i++)
			{
				uint value = (uint)i;
				for (int bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xedb88320u ^ (value >> 1) : value >> 1;
				table[i] = value;
			}
			return table;
		}
	}
}
