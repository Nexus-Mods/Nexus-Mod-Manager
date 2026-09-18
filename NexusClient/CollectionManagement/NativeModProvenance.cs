using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Records whether a native mod instance has independently established standalone user provenance.
	/// </summary>
	public enum StandaloneModUse
	{
		/// <summary>
		/// Standalone use is unknown or ambiguous. Automatic collection removal must treat this conservatively.
		/// </summary>
		Unknown = 0,

		/// <summary>
		/// The owning layer has positively established that no standalone use currently protects the instance.
		/// </summary>
		NoStandaloneUseVerified = 1,

		/// <summary>
		/// The user/native history explicitly requires the instance independently from collection associations.
		/// </summary>
		ExplicitStandaloneUse = 2
	}

	/// <summary>
	/// Stores target-scoped standalone provenance for one native mod instance.
	/// </summary>
	public sealed class NativeModProvenance : IEquatable<NativeModProvenance>
	{
		/// <summary>
		/// Creates an immutable native provenance snapshot.
		/// </summary>
		public NativeModProvenance(NativeModInstanceIdentity nativeMod, StandaloneModUse standaloneUse)
		{
			if (nativeMod == null)
				throw new ArgumentNullException(nameof(nativeMod));
			if (!Enum.IsDefined(typeof(StandaloneModUse), standaloneUse))
				throw new ArgumentOutOfRangeException(nameof(standaloneUse));

			NativeMod = nativeMod;
			StandaloneUse = standaloneUse;
		}

		/// <summary>
		/// Gets the target-scoped native mod registration.
		/// </summary>
		public NativeModInstanceIdentity NativeMod { get; }

		/// <summary>
		/// Gets the independently established standalone-use state.
		/// </summary>
		public StandaloneModUse StandaloneUse { get; }

		/// <summary>
		/// Gets whether standalone provenance alone prevents automatic removal of the native instance.
		/// </summary>
		/// <remarks>
		/// Unknown is deliberately protective. Collection-removal planning must additionally consider surviving
		/// collection bindings; this property represents only the standalone-provenance side of that decision.
		/// </remarks>
		public bool StandaloneUseProtectsFromAutomaticRemoval
		{
			get { return StandaloneUse != StandaloneModUse.NoStandaloneUseVerified; }
		}

		/// <inheritdoc />
		public bool Equals(NativeModProvenance other)
		{
			return !ReferenceEquals(other, null) && Equals(NativeMod, other.NativeMod);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as NativeModProvenance);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			return NativeMod.GetHashCode();
		}
	}
}
