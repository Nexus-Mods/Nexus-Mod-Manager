using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Indicates that a canonical target reservation was recovered from an abandoned cross-process lock and must be
	/// reloaded/revalidated before native mutation may continue.
	/// </summary>
	public sealed class CollectionTargetRecoveryRequiredException : InvalidOperationException
	{
		/// <summary>
		/// Creates an abandoned-target recovery requirement for the supplied canonical target fingerprint.
		/// </summary>
		public CollectionTargetRecoveryRequiredException(string targetFingerprint)
			: base("The Collection target lock was abandoned by another thread or process. Reload and reconcile native target state before continuing mutation.")
		{
			TargetFingerprint = CollectionIdentityValidation.RequireOpaqueToken(targetFingerprint, nameof(targetFingerprint));
		}

		/// <summary>
		/// Gets the canonical target fingerprint that requires recovery/revalidation.
		/// </summary>
		public string TargetFingerprint { get; }
	}
}
