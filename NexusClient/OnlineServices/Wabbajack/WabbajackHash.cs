using System;
using System.IO;
using System.IO.Hashing;
using System.Threading;

namespace Nexus.Client.OnlineServices.Wabbajack
{
	/// <summary>Wabbajack's zero-seed XXH64 digest, encoded as little-endian bytes in base64.</summary>
	internal static class WabbajackHash
	{
		/// <summary>Hashes a stream without loading an entire game file into memory.</summary>
		internal static string Compute(Stream source, CancellationToken token)
		{
			XxHash64 hash = new XxHash64();
			byte[] buffer = new byte[81920];
			int count;
			while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
			{
				token.ThrowIfCancellationRequested();
				hash.Append(new ReadOnlySpan<byte>(buffer, 0, count));
			}
			return Format(hash);
		}

		/// <summary>Formats the numeric digest as Wabbajack's serialized hash.</summary>
		internal static string Format(XxHash64 hash) { return Convert.ToBase64String(BitConverter.GetBytes(hash.GetCurrentHashAsUInt64())); }
	}

	/// <summary>Hashes exact reconstructed bytes while SevenZip writes them into a Collection bundle.</summary>
	internal sealed class WabbajackOutputStream : Stream
	{
		private readonly Stream _destination;
		private readonly CancellationToken _token;
		private readonly XxHash64 _hash = new XxHash64();
		private long _length;

		/// <summary>Wraps a destination owned by the caller.</summary>
		internal WabbajackOutputStream(Stream destination, CancellationToken token) { _destination = destination; _token = token; }
		internal string Hash => WabbajackHash.Format(_hash);
		public override bool CanRead => false;
		public override bool CanSeek => false;
		public override bool CanWrite => true;
		public override long Length => _length;
		public override long Position { get => _length; set => throw new NotSupportedException(); }
		/// <summary>Copies and hashes one output block.</summary>
		public override void Write(byte[] buffer, int offset, int count)
		{
			_token.ThrowIfCancellationRequested();
			_destination.Write(buffer, offset, count);
			_hash.Append(new ReadOnlySpan<byte>(buffer, offset, count));
			_length += count;
		}
		/// <summary>Flushes the underlying bundle entry.</summary>
		public override void Flush() { _destination.Flush(); }
		/// <summary>Reading is not supported for an output wrapper.</summary>
		public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
		/// <summary>Seeking is not supported for a compressed bundle entry.</summary>
		public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
		/// <summary>Changing a compressed bundle entry's length is not supported.</summary>
		public override void SetLength(long value) { throw new NotSupportedException(); }
	}
}
