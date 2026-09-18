using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Describes the machine-wide native ownership authority bound to one physical Collection target.
	/// </summary>
	/// <remarks>
	/// The binding deliberately stores opaque identities rather than native owner records. InstallLog remains the live
	/// ownership authority; this object only prevents two different InstallLogs from being treated as one writable target.
	/// </remarks>
	public sealed class CollectionTargetOwnershipAuthorityBinding
	{
		internal CollectionTargetOwnershipAuthorityBinding(string targetFingerprint, string storageId, string installInfoPhysicalKey)
		{
			TargetFingerprint = CollectionIdentityValidation.RequireOpaqueToken(targetFingerprint, nameof(targetFingerprint));
			StorageId = CollectionIdentityValidation.RequireOpaqueToken(storageId, nameof(storageId));
			InstallInfoPhysicalKey = CollectionIdentityValidation.RequireOpaqueToken(installInfoPhysicalKey, nameof(installInfoPhysicalKey));
		}

		/// <summary>
		/// Gets the canonical Collection target fingerprint that established this ownership authority.
		/// </summary>
		public string TargetFingerprint { get; }

		/// <summary>
		/// Gets the persisted Game Storage identity that owns native state for the target.
		/// </summary>
		public string StorageId { get; }

		/// <summary>
		/// Gets the physical identity of the InstallInfo directory containing the authoritative InstallLog.
		/// </summary>
		public string InstallInfoPhysicalKey { get; }
	}
}
