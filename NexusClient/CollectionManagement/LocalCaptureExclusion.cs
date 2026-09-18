using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Records one explicit limitation carved out of a Local Collection capture's declared scope.
	/// </summary>
	public sealed class LocalCaptureExclusion : IEquatable<LocalCaptureExclusion>
	{
		/// <summary>
		/// Creates an explicit capture-scope exclusion.
		/// </summary>
		public LocalCaptureExclusion(LocalCaptureScopeArea area, string code, string reason)
		{
			if (!Enum.IsDefined(typeof(LocalCaptureScopeArea), area) || area == LocalCaptureScopeArea.Unknown)
				throw new ArgumentOutOfRangeException(nameof(area));

			Area = area;
			Code = CollectionIdentityValidation.RequireOpaqueToken(code, nameof(code));
			Reason = CollectionDomainValidation.RequireDisplayValue(reason, nameof(reason));
		}

		/// <summary>
		/// Gets the declared scope area affected by the exclusion.
		/// </summary>
		public LocalCaptureScopeArea Area { get; }

		/// <summary>
		/// Gets the stable machine-readable exclusion code.
		/// </summary>
		public string Code { get; }

		/// <summary>
		/// Gets the user-facing reason the item/effect is outside the capture promise.
		/// </summary>
		public string Reason { get; }

		/// <inheritdoc />
		public bool Equals(LocalCaptureExclusion other)
		{
			return !ReferenceEquals(other, null) &&
				Area == other.Area &&
				StringComparer.Ordinal.Equals(Code, other.Code) &&
				StringComparer.Ordinal.Equals(Reason, other.Reason);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as LocalCaptureExclusion);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				int hashCode = (int)Area;
				hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(Code ?? string.Empty);
				hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(Reason ?? string.Empty);
				return hashCode;
			}
		}
	}
}
