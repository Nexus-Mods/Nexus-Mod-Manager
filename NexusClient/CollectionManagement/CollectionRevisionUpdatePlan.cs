using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Describes how one stable member changes between the installed and candidate Collection revisions.</summary>
	public enum CollectionRevisionUpdateChangeKind
	{
		Unchanged = 0,
		Added = 1,
		Removed = 2,
		ArtifactChanged = 3,
		RecipeChanged = 4,
		ArtifactAndRecipeChanged = 5
	}

	/// <summary>Describes what C10.1 can prove about the currently observed state for one old-revision member.</summary>
	public enum CollectionRevisionUpdateCurrentStateKind
	{
		NotApplicable = 0,
		MatchesOldBaseline = 1,
		IntentionalOverride = 2,
		UnacceptedDrift = 3,
		MissingNativeBinding = 4,
		Ambiguous = 5
	}

	/// <summary>Describes the read-only disposition produced by the three-way C10.1 planner.</summary>
	public enum CollectionRevisionUpdateDisposition
	{
		NoChange = 0,
		FollowNewRevision = 1,
		AddFromNewRevision = 2,
		RemoveFromOldRevision = 3,
		PreserveOverrideForReview = 4,
		DriftRequiresReview = 5,
		ActionRequired = 6,
		PreserveStandalone = 7
	}

	/// <summary>Describes whether concrete NMM-native preparation remains reusable across the revision transition.</summary>
	public enum CollectionRevisionUpdatePreparationKind
	{
		NotAvailable = 0,
		Unchanged = 1,
		PreparedForCandidate = 2,
		CandidateChanged = 3,
		ReprepareRequired = 4
	}

	/// <summary>Identifies one exact prepared native effect domain compared across revisions.</summary>
	public enum CollectionRevisionUpdateEffectKind
	{
		File = 1,
		Ini = 2,
		GameValue = 3,
		Plugin = 4
	}

	/// <summary>Describes whether one exact prepared native effect is retained, added, removed or changed.</summary>
	public enum CollectionRevisionUpdateEffectChangeKind
	{
		Unchanged = 0,
		Added = 1,
		Removed = 2,
		Changed = 3
	}

	/// <summary>One deterministic effect-level comparison when exact old/new effect previews are available.</summary>
	public sealed class CollectionRevisionUpdateEffectPlan
	{
		internal CollectionRevisionUpdateEffectPlan(CollectionMemberKey memberKey, CollectionRevisionUpdateEffectKind kind,
			string resourceKey, CollectionRevisionUpdateEffectChangeKind changeKind)
		{
			MemberKey = memberKey ?? throw new ArgumentNullException(nameof(memberKey));
			Kind = kind;
			ResourceKey = CollectionIdentityValidation.RequireOpaqueToken(resourceKey, nameof(resourceKey));
			ChangeKind = changeKind;
		}

		public CollectionMemberKey MemberKey { get; }
		public CollectionRevisionUpdateEffectKind Kind { get; }
		public string ResourceKey { get; }
		public CollectionRevisionUpdateEffectChangeKind ChangeKind { get; }
	}

	/// <summary>One immutable old/current/new member classification.</summary>
	public sealed class CollectionRevisionUpdateMemberPlan
	{
		private readonly ReadOnlyCollection<UserOverride> _overrides;
		private readonly ReadOnlyCollection<CollectionDriftObservation> _drift;

		internal CollectionRevisionUpdateMemberPlan(CollectionMemberKey memberKey, ResolvedCollectionMemberPlan oldMember,
			ResolvedCollectionMemberPlan newMember, CollectionMemberBinding binding, CollectionRevisionUpdateChangeKind changeKind,
			CollectionRevisionUpdateCurrentStateKind currentStateKind, CollectionRevisionUpdateDisposition disposition,
			CollectionRevisionUpdatePreparationKind preparationKind, PreparedCollectionNativeRecipeIdentity oldPreparedIdentity,
			PreparedCollectionNativeRecipeIdentity newPreparedIdentity, IEnumerable<UserOverride> overrides,
			IEnumerable<CollectionDriftObservation> drift, bool standaloneProtected, string detail)
		{
			MemberKey = memberKey ?? throw new ArgumentNullException(nameof(memberKey));
			OldMember = oldMember;
			NewMember = newMember;
			Binding = binding;
			ChangeKind = changeKind;
			CurrentStateKind = currentStateKind;
			Disposition = disposition;
			PreparationKind = preparationKind;
			OldPreparedIdentity = oldPreparedIdentity;
			NewPreparedIdentity = newPreparedIdentity;
			_overrides = new ReadOnlyCollection<UserOverride>((overrides ?? Enumerable.Empty<UserOverride>()).ToList());
			_drift = new ReadOnlyCollection<CollectionDriftObservation>((drift ?? Enumerable.Empty<CollectionDriftObservation>()).ToList());
			StandaloneProtected = standaloneProtected;
			Detail = detail ?? String.Empty;
		}

		public CollectionMemberKey MemberKey { get; }
		public ResolvedCollectionMemberPlan OldMember { get; }
		public ResolvedCollectionMemberPlan NewMember { get; }
		public CollectionMemberBinding Binding { get; }
		public CollectionRevisionUpdateChangeKind ChangeKind { get; }
		public CollectionRevisionUpdateCurrentStateKind CurrentStateKind { get; }
		public CollectionRevisionUpdateDisposition Disposition { get; }
		public CollectionRevisionUpdatePreparationKind PreparationKind { get; }
		public PreparedCollectionNativeRecipeIdentity OldPreparedIdentity { get; }
		public PreparedCollectionNativeRecipeIdentity NewPreparedIdentity { get; }
		public ReadOnlyCollection<UserOverride> Overrides { get { return _overrides; } }
		public ReadOnlyCollection<CollectionDriftObservation> Drift { get { return _drift; } }
		public bool StandaloneProtected { get; }
		public string Detail { get; }

		/// <summary>Gets whether an otherwise matching removed member is blocked only by protective standalone-use history.</summary>
		public bool RequiresStandaloneUseConfirmation
		{
			get
			{
				return ChangeKind == CollectionRevisionUpdateChangeKind.Removed && StandaloneProtected && Binding != null &&
					CurrentStateKind == CollectionRevisionUpdateCurrentStateKind.MatchesOldBaseline &&
					Disposition == CollectionRevisionUpdateDisposition.ActionRequired;
			}
		}

		public bool RequiresExplicitReview
		{
			get
			{
				return Disposition == CollectionRevisionUpdateDisposition.PreserveOverrideForReview ||
					Disposition == CollectionRevisionUpdateDisposition.DriftRequiresReview ||
					Disposition == CollectionRevisionUpdateDisposition.ActionRequired;
			}
		}
	}

	/// <summary>Immutable read-only result of comparing old resolved recipe, actual customized state and a candidate new recipe.</summary>
	public sealed class CollectionRevisionUpdatePlan
	{
		private readonly ReadOnlyCollection<CollectionRevisionUpdateMemberPlan> _members;
		private readonly ReadOnlyCollection<CollectionRevisionUpdateEffectPlan> _effects;
		private readonly CollectionRevisionUpdateMemberCorrelationMap _memberCorrelations;

		internal CollectionRevisionUpdatePlan(CollectionTargetAssociation association, ResolvedCollectionPlan oldPlan,
			ResolvedCollectionPlan newPlan, CollectionCurrentStateFingerprint observedStateFingerprint,
			IEnumerable<CollectionRevisionUpdateMemberPlan> members, IEnumerable<CollectionRevisionUpdateEffectPlan> effects, IEnumerable<UserOverride> unscopedOverrides,
			IEnumerable<CollectionDriftObservation> unscopedDrift)
		{
			Association = association ?? throw new ArgumentNullException(nameof(association));
			OldPlan = oldPlan ?? throw new ArgumentNullException(nameof(oldPlan));
			NewPlan = newPlan ?? throw new ArgumentNullException(nameof(newPlan));
			ObservedStateFingerprint = observedStateFingerprint ?? throw new ArgumentNullException(nameof(observedStateFingerprint));
			_memberCorrelations = CollectionRevisionUpdateMemberCorrelationMap.Build(OldPlan, NewPlan);
			_members = new ReadOnlyCollection<CollectionRevisionUpdateMemberPlan>((members ?? throw new ArgumentNullException(nameof(members))).ToList());
			_effects = new ReadOnlyCollection<CollectionRevisionUpdateEffectPlan>((effects ?? throw new ArgumentNullException(nameof(effects))).ToList());
			UnscopedOverrides = new ReadOnlyCollection<UserOverride>((unscopedOverrides ?? throw new ArgumentNullException(nameof(unscopedOverrides))).ToList());
			UnscopedDrift = new ReadOnlyCollection<CollectionDriftObservation>((unscopedDrift ?? throw new ArgumentNullException(nameof(unscopedDrift))).ToList());
		}

		public CollectionTargetAssociation Association { get; }
		public ResolvedCollectionPlan OldPlan { get; }
		public ResolvedCollectionPlan NewPlan { get; }
		public CollectionCurrentStateFingerprint ObservedStateFingerprint { get; }
		public ReadOnlyCollection<CollectionRevisionUpdateMemberPlan> Members { get { return _members; } }
		public ReadOnlyCollection<CollectionRevisionUpdateEffectPlan> Effects { get { return _effects; } }
		internal CollectionRevisionUpdateMemberCorrelationMap MemberCorrelations { get { return _memberCorrelations; } }
		public ReadOnlyCollection<UserOverride> UnscopedOverrides { get; }
		public ReadOnlyCollection<CollectionDriftObservation> UnscopedDrift { get; }
		public bool HasAssociationBlocker { get { return Association.State == CollectionAssociationState.Incomplete || Association.State == CollectionAssociationState.Recovering; } }
		public bool HasActionRequired { get { return HasAssociationBlocker || UnscopedOverrides.Count > 0 || UnscopedDrift.Count > 0 || _members.Any(x => x.RequiresExplicitReview); } }

		/// <summary>Gets whether unresolved drift/ambiguity prevents an update approval rather than merely requiring explicit override review.</summary>
		public bool HasBlockingActionRequired
		{
			get
			{
				return HasAssociationBlocker || UnscopedDrift.Count > 0 || _members.Any(x =>
					x.Disposition == CollectionRevisionUpdateDisposition.DriftRequiresReview ||
					x.Disposition == CollectionRevisionUpdateDisposition.ActionRequired);
			}
		}

		/// <summary>Gets whether the review contains deliberate C9 overrides that an update approval must explicitly preserve.</summary>
		public bool HasReviewableOverrides
		{
			get { return UnscopedOverrides.Count > 0 || _members.Any(x => x.Overrides.Count > 0); }
		}

		public bool PreparedNativeOutputChanged { get { return _members.Any(x => x.PreparationKind == CollectionRevisionUpdatePreparationKind.CandidateChanged); } }
		public bool RequiresNativeRepreparation { get { return _members.Any(x => x.PreparationKind == CollectionRevisionUpdatePreparationKind.ReprepareRequired); } }
	}
}
