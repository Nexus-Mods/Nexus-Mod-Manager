using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Identifies one sealed Local Collection capture independently from the Local Collection revision that owns it.
	/// </summary>
	public sealed class LocalCaptureIdentity : IEquatable<LocalCaptureIdentity>
	{
		private LocalCaptureIdentity(Guid captureId)
		{
			CaptureId = captureId;
		}

		/// <summary>
		/// Gets the persistent NMM-owned capture identifier.
		/// </summary>
		public Guid CaptureId { get; }

		/// <summary>
		/// Creates a capture identity from a persistent non-empty identifier.
		/// </summary>
		public static LocalCaptureIdentity From(Guid captureId)
		{
			if (captureId == Guid.Empty)
				throw new ArgumentException("A non-empty Local Collection capture identifier is required.", nameof(captureId));

			return new LocalCaptureIdentity(captureId);
		}

		/// <inheritdoc />
		public bool Equals(LocalCaptureIdentity other)
		{
			return !ReferenceEquals(other, null) && CaptureId == other.CaptureId;
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as LocalCaptureIdentity);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			return CaptureId.GetHashCode();
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return CaptureId.ToString("D");
		}
	}
}
