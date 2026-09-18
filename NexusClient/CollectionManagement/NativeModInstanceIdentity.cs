using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Identifies one native NMM mod registration within one real collection target.
	/// </summary>
	/// <remarks>
	/// InstallLog mod keys are native runtime/storage identifiers, not globally stable collection identities. Scoping
	/// the key to <see cref="Target"/> prevents a regenerated key from another target or restored setup from being
	/// treated as the same installed instance.
	/// </remarks>
	public sealed class NativeModInstanceIdentity : IEquatable<NativeModInstanceIdentity>
	{
		/// <summary>
		/// Creates a target-scoped native mod identity.
		/// </summary>
		public NativeModInstanceIdentity(CollectionTargetIdentity target, string nativeModKey)
		{
			if (target == null)
				throw new ArgumentNullException(nameof(target));

			Target = target;
			NativeModKey = CollectionIdentityValidation.RequireOpaqueToken(nativeModKey, nameof(nativeModKey));
		}

		/// <summary>
		/// Gets the real target that owns this native mod registration.
		/// </summary>
		public CollectionTargetIdentity Target { get; }

		/// <summary>
		/// Gets the InstallLog/native owner key within <see cref="Target"/>.
		/// </summary>
		public string NativeModKey { get; }

		/// <inheritdoc />
		public bool Equals(NativeModInstanceIdentity other)
		{
			return !ReferenceEquals(other, null) &&
				Equals(Target, other.Target) &&
				StringComparer.Ordinal.Equals(NativeModKey, other.NativeModKey);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as NativeModInstanceIdentity);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				return (Target.GetHashCode() * 397) ^ StringComparer.Ordinal.GetHashCode(NativeModKey ?? string.Empty);
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return Target + "/native:" + NativeModKey;
		}
	}
}
