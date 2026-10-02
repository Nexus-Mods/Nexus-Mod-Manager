using System;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>One durable C9 user decision that resolves an otherwise blocking Collection conflict.</summary>
	public enum CollectionConflictResolutionDecisionKind
	{
		Unknown = 0,
		IncomingCollectionWinsFile = 1,
		KeepExistingManagedFileWinner = 2
	}

	/// <summary>
	/// Binds one explicit file-winner decision to the exact Collection revision, target, member, path and existing owner.
	/// </summary>
	/// <remarks>
	/// The existing owner key is part of the identity so a later ownership change makes the decision stale instead of
	/// silently authorizing a different overwrite. C9.1 records intent only; native ownership remains authoritative.
	/// </remarks>
	public sealed class CollectionConflictResolutionDecision
	{
		public CollectionConflictResolutionDecision(Guid decisionId, CollectionRevisionIdentity revision,
			CollectionTargetIdentity target, CollectionMemberKey memberKey, ModDeploymentRoot deploymentRoot,
			string relativePath, string existingOwnerKey, CollectionConflictResolutionDecisionKind kind, string note)
		{
			if (decisionId == Guid.Empty) throw new ArgumentException("A non-empty conflict decision identifier is required.", nameof(decisionId));
			DecisionId = decisionId;
			Revision = revision ?? throw new ArgumentNullException(nameof(revision));
			Target = target ?? throw new ArgumentNullException(nameof(target));
			MemberKey = memberKey ?? throw new ArgumentNullException(nameof(memberKey));
			if (!Enum.IsDefined(typeof(ModDeploymentRoot), deploymentRoot)) throw new ArgumentOutOfRangeException(nameof(deploymentRoot));
			DeploymentRoot = deploymentRoot;
			if (String.IsNullOrWhiteSpace(relativePath)) throw new ArgumentException("A deployment-relative path is required.", nameof(relativePath));
			RelativePath = relativePath;
			ExistingOwnerKey = CollectionIdentityValidation.RequireOpaqueToken(existingOwnerKey, nameof(existingOwnerKey));
			if (!Enum.IsDefined(typeof(CollectionConflictResolutionDecisionKind), kind) || kind == CollectionConflictResolutionDecisionKind.Unknown)
				throw new ArgumentOutOfRangeException(nameof(kind));
			Kind = kind;
			Note = note;
		}

		public Guid DecisionId { get; }
		public CollectionRevisionIdentity Revision { get; }
		public CollectionTargetIdentity Target { get; }
		public CollectionMemberKey MemberKey { get; }
		public ModDeploymentRoot DeploymentRoot { get; }
		public string RelativePath { get; }
		public string ExistingOwnerKey { get; }
		public CollectionConflictResolutionDecisionKind Kind { get; }
		public string Note { get; }
	}
}
