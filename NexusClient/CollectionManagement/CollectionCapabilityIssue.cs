using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Describes where a collection capability issue applies.
	/// </summary>
	public enum CollectionCapabilityIssueTarget
	{
		Unknown = 0,
		Manifest = 1,
		Member = 2
	}

	/// <summary>
	/// Immutable precise reason why collection input needs action or is unsupported.
	/// </summary>
	public sealed class CollectionCapabilityIssue
	{
		private CollectionCapabilityIssue(
			CollectionCapabilityIssueTarget target,
			CollectionCompatibilityStatus status,
			string code,
			string reason,
			int? sourceOrdinal,
			CollectionMemberKey memberKey,
			string fieldPath)
		{
			if (!Enum.IsDefined(typeof(CollectionCapabilityIssueTarget), target) || target == CollectionCapabilityIssueTarget.Unknown)
				throw new ArgumentOutOfRangeException(nameof(target));
			if (status != CollectionCompatibilityStatus.ActionRequired && status != CollectionCompatibilityStatus.Unsupported)
				throw new ArgumentOutOfRangeException(nameof(status), "Capability issues must require action or describe unsupported behavior.");

			code = CollectionIdentityValidation.RequireOpaqueToken(code, nameof(code));
			reason = CollectionDomainValidation.RequireDisplayValue(reason, nameof(reason));
			fieldPath = CollectionDomainValidation.OptionalDisplayValue(fieldPath, nameof(fieldPath));

			if (target == CollectionCapabilityIssueTarget.Manifest)
			{
				if (sourceOrdinal.HasValue || memberKey != null)
					throw new ArgumentException("Manifest capability issues cannot reference a member.");
			}
			else
			{
				if (!sourceOrdinal.HasValue || sourceOrdinal.Value < 0)
					throw new ArgumentOutOfRangeException(nameof(sourceOrdinal), "Member capability issues require a non-negative source ordinal.");
			}

			Target = target;
			Status = status;
			Code = code;
			Reason = reason;
			SourceOrdinal = sourceOrdinal;
			MemberKey = memberKey;
			FieldPath = fieldPath;
		}

		/// <summary>
		/// Gets whether the issue applies to the whole manifest or one normalized member.
		/// </summary>
		public CollectionCapabilityIssueTarget Target { get; }

		/// <summary>
		/// Gets the compatibility effect of this issue.
		/// </summary>
		public CollectionCompatibilityStatus Status { get; }

		/// <summary>
		/// Gets the stable machine-readable issue code.
		/// </summary>
		public string Code { get; }

		/// <summary>
		/// Gets the precise user/debug-facing reason.
		/// </summary>
		public string Reason { get; }

		/// <summary>
		/// Gets the retained raw-source ordinal for a member issue, or null for a manifest issue.
		/// </summary>
		public int? SourceOrdinal { get; }

		/// <summary>
		/// Gets the stable member key when that identity was resolved, or null otherwise.
		/// </summary>
		public CollectionMemberKey MemberKey { get; }

		/// <summary>
		/// Gets the optional normalized field path that produced the issue.
		/// </summary>
		public string FieldPath { get; }

		/// <summary>
		/// Creates a whole-manifest capability issue.
		/// </summary>
		public static CollectionCapabilityIssue ForManifest(
			CollectionCompatibilityStatus status,
			string code,
			string reason,
			string fieldPath = null)
		{
			return new CollectionCapabilityIssue(CollectionCapabilityIssueTarget.Manifest, status, code, reason, null, null, fieldPath);
		}

		/// <summary>
		/// Creates a capability issue tied to one normalized member.
		/// </summary>
		public static CollectionCapabilityIssue ForMember(
			CollectionCompatibilityStatus status,
			string code,
			string reason,
			NormalizedCollectionMember member,
			string fieldPath = null)
		{
			if (member == null)
				throw new ArgumentNullException(nameof(member));

			CollectionMemberKey key = member.IdentityResolution.IsResolved ? member.IdentityResolution.Key : null;
			return new CollectionCapabilityIssue(
				CollectionCapabilityIssueTarget.Member,
				status,
				code,
				reason,
				member.SourceOrdinal,
				key,
				fieldPath);
		}
	}
}
