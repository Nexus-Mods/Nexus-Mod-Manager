using System;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>
	/// Immutable persisted metadata and opaque payload for one exact resolved Collection plan snapshot.
	/// </summary>
	/// <remarks>
	/// The payload is feature-owned planning data only. It is not native installation state and does not authorize mutation.
	/// </remarks>
	public sealed class CollectionResolvedPlanRecord
	{
		private readonly byte[] _payload;

		/// <summary>
		/// Creates one immutable persisted-plan record.
		/// </summary>
		public CollectionResolvedPlanRecord(CollectionPlanIdentity identity, CollectionRevisionIdentity revision,
			CollectionTargetIdentity target, CollectionExecutionPolicyKind policyKind,
			CollectionCurrentStateFingerprint currentStateFingerprint, string payloadFormat, byte[] payload)
		{
			Identity = identity ?? throw new ArgumentNullException(nameof(identity));
			Revision = revision ?? throw new ArgumentNullException(nameof(revision));
			Target = target ?? throw new ArgumentNullException(nameof(target));
			if (!Enum.IsDefined(typeof(CollectionExecutionPolicyKind), policyKind) || policyKind == CollectionExecutionPolicyKind.Unknown)
				throw new ArgumentOutOfRangeException(nameof(policyKind));
			PolicyKind = policyKind;
			CurrentStateFingerprint = currentStateFingerprint ?? throw new ArgumentNullException(nameof(currentStateFingerprint));
			PayloadFormat = CollectionIdentityValidation.RequireOpaqueToken(payloadFormat, nameof(payloadFormat));
			if (payload == null)
				throw new ArgumentNullException(nameof(payload));
			if (payload.Length == 0)
				throw new ArgumentException("A resolved Collection plan payload cannot be empty.", nameof(payload));
			_payload = (byte[])payload.Clone();
		}

		public CollectionPlanIdentity Identity { get; }
		public CollectionRevisionIdentity Revision { get; }
		public CollectionTargetIdentity Target { get; }
		public CollectionExecutionPolicyKind PolicyKind { get; }
		public CollectionCurrentStateFingerprint CurrentStateFingerprint { get; }
		public string PayloadFormat { get; }

		/// <summary>
		/// Gets a defensive copy of the persisted immutable plan payload.
		/// </summary>
		public byte[] Payload
		{
			get { return (byte[])_payload.Clone(); }
		}

		internal byte[] UnsafePayload
		{
			get { return _payload; }
		}
	}
}
