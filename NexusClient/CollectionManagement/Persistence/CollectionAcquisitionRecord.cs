using System;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>
	/// Durable restart correlation for one immutable Collection acquisition request.
	/// </summary>
	public sealed class CollectionAcquisitionRecord
	{
		internal CollectionAcquisitionRecord(Guid requestId, Guid planId, int planVersion, string revisionIdentity,
			string targetFingerprint, int memberKeyKind, string memberKeyValue, int requirement,
			string artifactScheme, string artifactStableId, string expectedContentHash, string recipeFingerprint,
			CollectionAcquisitionPersistenceMode mode, CollectionAcquisitionPersistenceState state,
			Guid? queueOperationId, string verifiedArtifactId, DateTime updatedUtc)
		{
			RequestId = requestId;
			PlanId = planId;
			PlanVersion = planVersion;
			RevisionIdentity = revisionIdentity;
			TargetFingerprint = targetFingerprint;
			MemberKeyKind = memberKeyKind;
			MemberKeyValue = memberKeyValue;
			Requirement = requirement;
			ArtifactScheme = artifactScheme;
			ArtifactStableId = artifactStableId;
			ExpectedContentHash = expectedContentHash;
			RecipeFingerprint = recipeFingerprint;
			Mode = mode;
			State = state;
			QueueOperationId = queueOperationId;
			VerifiedArtifactId = verifiedArtifactId;
			UpdatedUtc = updatedUtc;
		}

		public Guid RequestId { get; }
		public Guid PlanId { get; }
		public int PlanVersion { get; }
		public string RevisionIdentity { get; }
		public string TargetFingerprint { get; }
		public int MemberKeyKind { get; }
		public string MemberKeyValue { get; }
		public int Requirement { get; }
		public string ArtifactScheme { get; }
		public string ArtifactStableId { get; }
		public string ExpectedContentHash { get; }
		public string RecipeFingerprint { get; }
		public CollectionAcquisitionPersistenceMode Mode { get; }
		public CollectionAcquisitionPersistenceState State { get; }
		public Guid? QueueOperationId { get; }
		public string VerifiedArtifactId { get; }
		public DateTime UpdatedUtc { get; }

		/// <summary>
		/// Returns whether this durable record still describes the exact immutable acquisition request supplied by the caller.
		/// </summary>
		public bool Matches(CollectionAcquisitionRequest request)
		{
			if (request == null)
				return false;
			string expectedHash = request.SelectedArtifact.ExpectedContentHash == null
				? null
				: request.SelectedArtifact.ExpectedContentHash.ToString();
			return RequestId == request.RequestId &&
				PlanId == request.PlanIdentity.PlanId && PlanVersion == request.PlanIdentity.Version &&
				StringComparer.Ordinal.Equals(RevisionIdentity, request.Revision.ToString()) &&
				StringComparer.Ordinal.Equals(TargetFingerprint, request.Target.Fingerprint) &&
				MemberKeyKind == (int)request.MemberKey.Kind &&
				StringComparer.Ordinal.Equals(MemberKeyValue, request.MemberKey.Value) &&
				Requirement == (int)request.Requirement &&
				StringComparer.Ordinal.Equals(ArtifactScheme, request.SelectedArtifact.Scheme) &&
				StringComparer.Ordinal.Equals(ArtifactStableId, request.SelectedArtifact.StableId) &&
				StringComparer.Ordinal.Equals(ExpectedContentHash, expectedHash) &&
				StringComparer.Ordinal.Equals(RecipeFingerprint, request.RecipeIdentity.Fingerprint);
		}
	}
}
