using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Records how one collection-native snapshot member related to one native NMM registration at capture time.
	/// </summary>
	/// <remarks>
	/// The source native key is provenance for the captured setup, not a globally reusable restore identity. C7 restore
	/// logic may recreate native registrations with different keys and must remap them through the collection member key.
	/// </remarks>
	public sealed class LocalCaptureNativeRecordMapping : IEquatable<LocalCaptureNativeRecordMapping>
	{
		/// <summary>
		/// Creates a capture-time member-to-native-record mapping.
		/// </summary>
		public LocalCaptureNativeRecordMapping(CollectionMemberKey snapshotMemberKey, NativeModInstanceIdentity sourceNativeInstance)
		{
			if (snapshotMemberKey == null)
				throw new ArgumentNullException(nameof(snapshotMemberKey));
			if (sourceNativeInstance == null)
				throw new ArgumentNullException(nameof(sourceNativeInstance));

			SnapshotMemberKey = snapshotMemberKey;
			SourceNativeInstance = sourceNativeInstance;
		}

		/// <summary>
		/// Gets the stable member key inside the Local Collection snapshot.
		/// </summary>
		public CollectionMemberKey SnapshotMemberKey { get; }

		/// <summary>
		/// Gets the target-scoped native registration observed when the snapshot was captured.
		/// </summary>
		public NativeModInstanceIdentity SourceNativeInstance { get; }

		/// <inheritdoc />
		public bool Equals(LocalCaptureNativeRecordMapping other)
		{
			return !ReferenceEquals(other, null) &&
				Equals(SnapshotMemberKey, other.SnapshotMemberKey) &&
				Equals(SourceNativeInstance, other.SourceNativeInstance);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as LocalCaptureNativeRecordMapping);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				return (SnapshotMemberKey.GetHashCode() * 397) ^ SourceNativeInstance.GetHashCode();
			}
		}
	}
}
