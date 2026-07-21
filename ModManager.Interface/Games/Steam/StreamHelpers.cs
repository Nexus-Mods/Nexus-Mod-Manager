using System;
using System.IO;
using System.Text;

namespace Nexus.Client.Games.Steam
{
	internal static class StreamHelpers
	{
		private static byte[] _data = new byte[8];

		public static Int16 ReadInt16(this Stream stream)
		{
			stream.Read(_data, 0, 2);
			return BitConverter.ToInt16(_data, 0);
		}

		public static UInt16 ReadUInt16(this Stream stream)
		{
			stream.Read(_data, 0, 2);

			return BitConverter.ToUInt16(_data, 0);
		}

		public static Int32 ReadInt32(this Stream stream)
		{
			stream.Read(_data, 0, 4);

			return BitConverter.ToInt32(_data, 0);
		}

		public static UInt32 ReadUInt32(this Stream stream)
		{
			stream.Read(_data, 0, 4);

			return BitConverter.ToUInt32(_data, 0);
		}

		public static UInt64 ReadUInt64(this Stream stream)
		{
			stream.Read(_data, 0, 8);

			return BitConverter.ToUInt64(_data, 0);
		}

		public static float ReadFloat(this Stream stream)
		{
			stream.Read(_data, 0, 4);

			return BitConverter.ToSingle(_data, 0);
		}

		public static string ReadNullTermString(this Stream stream, Encoding encoding)
		{
			var characterSize = encoding.GetByteCount("e");

			using (var ms = new MemoryStream())
			{

				while (true)
				{
					var data = new byte[characterSize];
					stream.Read(data, 0, characterSize);

					if (encoding.GetString(data, 0, characterSize) == "\0")
					{
						break;
					}

					ms.Write(data, 0, data.Length);
				}

				return encoding.GetString(ms.ToArray());
			}
		}

		private static byte[] _bufferCache;

		public static byte[] ReadBytesCached(this Stream stream, int len)
		{
			if (_bufferCache == null || _bufferCache.Length < len)
				_bufferCache = new byte[len];

			stream.Read(_bufferCache, 0, len);

			return _bufferCache;
		}

		static readonly byte[] DiscardBuffer = new byte[2 << 12];

		public static void ReadAndDiscard(this Stream stream, int len)
		{
			while (len > DiscardBuffer.Length)
			{
				stream.Read(DiscardBuffer, 0, DiscardBuffer.Length);
				len -= DiscardBuffer.Length;
			}

			stream.Read(DiscardBuffer, 0, len);
		}
	}
}
