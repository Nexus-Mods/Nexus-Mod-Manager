using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Describes the read-only C6.2 disposition for one selected Collection member.
	/// </summary>
	public enum CollectionMemberMatchDisposition
	{
		Unknown = 0,
		InstalledCompatible = 1,
		ArchiveOnlyReuse = 2,
		ReinstallRequired = 3,
		AcquisitionRequired = 4,
		Blocked = 5
	}

	/// <summary>
	/// Explains why C6.2 classified one selected member into its current matching disposition.
	/// </summary>
	public enum CollectionMemberMatchReason
	{
		Unknown = 0,
		ExistingVerifiedBinding = 1,
		CompatibleSharedVerifiedBinding = 2,
		VerifiedArchiveAvailable = 3,
		ExactArtifactRecipeUnverified = 4,
		AlternateArtifactInstalled = 5,
		NoReusableInput = 6,
		CurrentStateChanged = 7,
		AssociationStateUnavailable = 8,
		UnsupportedArtifactIdentity = 9,
		AmbiguousInstalledCandidates = 10,
		MissingBoundNativeMod = 11,
		ConflictingVerifiedRecipe = 12,
		BoundNativeArtifactMismatch = 13,
		AssociationNotApplied = 14,
		AssociationRequiresRecovery = 15,
		UnsupportedExecutionPolicy = 16
	}

	/// <summary>
	/// Immutable C6.2 matching result for one selected Collection member.
	/// </summary>
	/// <remarks>
	/// Matching is planning evidence only. It does not mutate native state, create Collection bindings, choose file winners,
	/// order dependencies or authorize installation. Later C6 phases revalidate state before any native mutation.
	/// </remarks>
	public sealed class CollectionMemberMatchResult
	{
		private readonly ReadOnlyCollection<CollectionNativeModState> _nativeCandidates;
		private readonly ReadOnlyCollection<CollectionMemberBinding> _existingBindings;

		internal CollectionMemberMatchResult(ResolvedCollectionMemberPlan member,
			CollectionMemberMatchDisposition disposition, CollectionMemberMatchReason reason,
			IEnumerable<CollectionNativeModState> nativeCandidates, IEnumerable<CollectionMemberBinding> existingBindings,
			CollectionVerifiedArchive verifiedArchive)
		{
			Member = member ?? throw new ArgumentNullException(nameof(member));
			if (!Enum.IsDefined(typeof(CollectionMemberMatchDisposition), disposition) || disposition == CollectionMemberMatchDisposition.Unknown)
				throw new ArgumentOutOfRangeException(nameof(disposition));
			if (!Enum.IsDefined(typeof(CollectionMemberMatchReason), reason) || reason == CollectionMemberMatchReason.Unknown)
				throw new ArgumentOutOfRangeException(nameof(reason));

			Disposition = disposition;
			Reason = reason;
			_nativeCandidates = Copy(nativeCandidates, nameof(nativeCandidates));
			_existingBindings = Copy(existingBindings, nameof(existingBindings));
			VerifiedArchive = verifiedArchive;
		}

		public ResolvedCollectionMemberPlan Member { get; }
		public CollectionMemberMatchDisposition Disposition { get; }
		public CollectionMemberMatchReason Reason { get; }
		public ReadOnlyCollection<CollectionNativeModState> NativeCandidates { get { return _nativeCandidates; } }
		public ReadOnlyCollection<CollectionMemberBinding> ExistingBindings { get { return _existingBindings; } }
		public CollectionVerifiedArchive VerifiedArchive { get; }

		/// <summary>Gets the single matched native instance when the result has exactly one candidate.</summary>
		public CollectionNativeModState MatchedNativeMod
		{
			get { return _nativeCandidates.Count == 1 ? _nativeCandidates[0] : null; }
		}

		/// <summary>Gets whether this result prevents later planning from treating the member as actionable.</summary>
		public bool IsBlocked
		{
			get { return Disposition == CollectionMemberMatchDisposition.Blocked; }
		}

		private static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values, string parameterName) where T : class
		{
			if (values == null)
				throw new ArgumentNullException(parameterName);
			List<T> copied = values.ToList();
			if (copied.Any(x => x == null))
				throw new ArgumentException("A member-match result cannot contain null records.", parameterName);
			return new ReadOnlyCollection<T>(copied);
		}
	}

	/// <summary>
	/// Immutable C6.2 matching output for one exact resolved plan and native-state observation.
	/// </summary>
	public sealed class CollectionMemberMatchSet
	{
		private readonly ReadOnlyCollection<CollectionMemberMatchResult> _members;
		private readonly ReadOnlyDictionary<CollectionMemberKey, CollectionMemberMatchResult> _membersByKey;

		internal CollectionMemberMatchSet(ResolvedCollectionPlan plan, CollectionNativeStateIndex nativeState,
			IEnumerable<CollectionMemberMatchResult> members)
		{
			if (plan == null)
				throw new ArgumentNullException(nameof(plan));
			if (nativeState == null)
				throw new ArgumentNullException(nameof(nativeState));
			if (members == null)
				throw new ArgumentNullException(nameof(members));

			List<CollectionMemberMatchResult> copied = members.ToList();
			if (copied.Any(x => x == null))
				throw new ArgumentException("A member-match set cannot contain null member results.", nameof(members));
			if (copied.Count != plan.SelectedMembers.Count)
				throw new ArgumentException("A member-match set must cover the complete selected member closure.", nameof(members));

			Dictionary<CollectionMemberKey, CollectionMemberMatchResult> byKey = new Dictionary<CollectionMemberKey, CollectionMemberMatchResult>();
			foreach (CollectionMemberMatchResult result in copied)
			{
				if (byKey.ContainsKey(result.Member.MemberKey))
					throw new ArgumentException("A member-match set cannot contain duplicate member keys.", nameof(members));
				byKey.Add(result.Member.MemberKey, result);
			}

			foreach (ResolvedCollectionMemberPlan selected in plan.SelectedMembers)
				if (!byKey.ContainsKey(selected.MemberKey))
					throw new ArgumentException("A member-match set must contain every selected member.", nameof(members));

			PlanIdentity = plan.Identity;
			Target = plan.Target;
			StateFingerprint = nativeState.Fingerprint;
			_members = new ReadOnlyCollection<CollectionMemberMatchResult>(copied);
			_membersByKey = new ReadOnlyDictionary<CollectionMemberKey, CollectionMemberMatchResult>(byKey);
		}

		public CollectionPlanIdentity PlanIdentity { get; }
		public CollectionTargetIdentity Target { get; }
		public CollectionCurrentStateFingerprint StateFingerprint { get; }
		public ReadOnlyCollection<CollectionMemberMatchResult> Members { get { return _members; } }
		public IReadOnlyDictionary<CollectionMemberKey, CollectionMemberMatchResult> MembersByKey { get { return _membersByKey; } }

		public bool HasBlockedMembers
		{
			get { return _members.Any(x => x.Disposition == CollectionMemberMatchDisposition.Blocked); }
		}

		public bool HasAcquisitionRequired
		{
			get { return _members.Any(x => x.Disposition == CollectionMemberMatchDisposition.AcquisitionRequired); }
		}

		public bool HasReinstallRequired
		{
			get { return _members.Any(x => x.Disposition == CollectionMemberMatchDisposition.ReinstallRequired); }
		}
	}
}
