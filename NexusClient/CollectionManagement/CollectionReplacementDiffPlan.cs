using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>High-level C8.1 difference classification shared by incoming members and current native instances.</summary>
	public enum CollectionReplacementDiffDisposition
	{
		Unknown = 0,
		OutgoingOnly = 1,
		IncomingOnly = 2,
		RetainedReused = 3,
		ReinstallOrChange = 4,
		SharedProtected = 5,
		BlockedAmbiguous = 6
	}

	/// <summary>Describes what C8.1 facts establish about later removal of one current native mod.</summary>
	public enum CollectionReplacementRemovalDecision
	{
		Unknown = 0,
		NotApplicable = 1,
		Protected = 2,
		EligibleForReviewedRemoval = 3,
		RequiresExplicitReview = 4,
		Blocked = 5
	}

	/// <summary>Classifies how one current association relates to the proposed replacement result.</summary>
	public enum CollectionReplacementAssociationDisposition
	{
		Unknown = 0,
		SurvivesCompatible = 1,
		OutgoingTransition = 2,
		RequiresExplicitReview = 3,
		Blocked = 4
	}

	/// <summary>Identifies one observed native effect domain included in the read-only replacement difference.</summary>
	public enum CollectionReplacementEffectKind
	{
		Unknown = 0,
		File = 1,
		Ini = 2,
		GameValue = 3,
		Plugin = 4
	}

	/// <summary>Classifies the current observed effect after removing only the outgoing managed owners represented by C8.1 facts.</summary>
	public enum CollectionReplacementEffectDisposition
	{
		Unknown = 0,
		Retained = 1,
		OutgoingOnly = 2,
		WinnerChange = 3,
		ReinstallOrChange = 4,
		PreservedUnknownOrUnmanaged = 5,
		BlockedAmbiguous = 6
	}

	/// <summary>Read-only C8.1 classification for one selected incoming member.</summary>
	public sealed class CollectionReplacementIncomingMemberImpact
	{
		private readonly ReadOnlyCollection<CollectionNativeModState> _nativeCandidates;

		internal CollectionReplacementIncomingMemberImpact(ResolvedCollectionMemberPlan member,
			CollectionReplacementDiffDisposition disposition, CollectionMemberMatchDisposition matchDisposition,
			CollectionMemberMatchReason matchReason, IEnumerable<CollectionNativeModState> nativeCandidates, string reason)
		{
			Member = member ?? throw new ArgumentNullException(nameof(member));
			if (!Enum.IsDefined(typeof(CollectionReplacementDiffDisposition), disposition) || disposition == CollectionReplacementDiffDisposition.Unknown)
				throw new ArgumentOutOfRangeException(nameof(disposition));
			if (!Enum.IsDefined(typeof(CollectionMemberMatchDisposition), matchDisposition) || matchDisposition == CollectionMemberMatchDisposition.Unknown)
				throw new ArgumentOutOfRangeException(nameof(matchDisposition));
			if (!Enum.IsDefined(typeof(CollectionMemberMatchReason), matchReason) || matchReason == CollectionMemberMatchReason.Unknown)
				throw new ArgumentOutOfRangeException(nameof(matchReason));
			List<CollectionNativeModState> candidates = (nativeCandidates ?? throw new ArgumentNullException(nameof(nativeCandidates))).ToList();
			if (candidates.Any(x => x == null))
				throw new ArgumentException("An incoming replacement impact cannot contain a null native candidate.", nameof(nativeCandidates));
			Disposition = disposition;
			MatchDisposition = matchDisposition;
			MatchReason = matchReason;
			_nativeCandidates = new ReadOnlyCollection<CollectionNativeModState>(candidates
				.OrderBy(x => x.Identity.NativeModKey, StringComparer.Ordinal).ToList());
			Reason = reason ?? String.Empty;
		}

		public ResolvedCollectionMemberPlan Member { get; }
		public CollectionReplacementDiffDisposition Disposition { get; }
		public CollectionMemberMatchDisposition MatchDisposition { get; }
		public CollectionMemberMatchReason MatchReason { get; }
		public ReadOnlyCollection<CollectionNativeModState> NativeCandidates { get { return _nativeCandidates; } }
		public string Reason { get; }
		public bool BlocksPlanning { get { return Disposition == CollectionReplacementDiffDisposition.BlockedAmbiguous; } }
	}

	/// <summary>Read-only C8.1 classification for one currently registered native mod.</summary>
	public sealed class CollectionReplacementNativeModImpact
	{
		private readonly ReadOnlyCollection<CollectionMemberBinding> _bindings;
		private readonly ReadOnlyCollection<Guid> _survivingAssociationIds;

		internal CollectionReplacementNativeModImpact(CollectionNativeModState nativeMod,
			CollectionReplacementDiffDisposition disposition, CollectionReplacementRemovalDecision removalDecision,
			StandaloneModUse standaloneUse, bool hasCustomization, IEnumerable<CollectionMemberBinding> bindings,
			IEnumerable<Guid> survivingAssociationIds, string reason)
		{
			NativeMod = nativeMod ?? throw new ArgumentNullException(nameof(nativeMod));
			if (!Enum.IsDefined(typeof(CollectionReplacementDiffDisposition), disposition) || disposition == CollectionReplacementDiffDisposition.Unknown)
				throw new ArgumentOutOfRangeException(nameof(disposition));
			if (!Enum.IsDefined(typeof(CollectionReplacementRemovalDecision), removalDecision) || removalDecision == CollectionReplacementRemovalDecision.Unknown)
				throw new ArgumentOutOfRangeException(nameof(removalDecision));
			if (!Enum.IsDefined(typeof(StandaloneModUse), standaloneUse))
				throw new ArgumentOutOfRangeException(nameof(standaloneUse));
			List<CollectionMemberBinding> copiedBindings = (bindings ?? throw new ArgumentNullException(nameof(bindings))).ToList();
			if (copiedBindings.Any(x => x == null))
				throw new ArgumentException("A replacement native-mod impact cannot contain a null Collection binding.", nameof(bindings));
			Disposition = disposition;
			RemovalDecision = removalDecision;
			StandaloneUse = standaloneUse;
			HasCustomization = hasCustomization;
			_bindings = new ReadOnlyCollection<CollectionMemberBinding>(copiedBindings
				.OrderBy(x => x.Association.AssociationId).ThenBy(x => x.MemberKey.ToString(), StringComparer.Ordinal).ToList());
			_survivingAssociationIds = new ReadOnlyCollection<Guid>((survivingAssociationIds ?? throw new ArgumentNullException(nameof(survivingAssociationIds)))
				.Distinct().OrderBy(x => x).ToList());
			Reason = reason ?? String.Empty;
		}

		public CollectionNativeModState NativeMod { get; }
		public CollectionReplacementDiffDisposition Disposition { get; }
		public CollectionReplacementRemovalDecision RemovalDecision { get; }
		public StandaloneModUse StandaloneUse { get; }
		public bool HasCustomization { get; }
		public ReadOnlyCollection<CollectionMemberBinding> Bindings { get { return _bindings; } }
		public ReadOnlyCollection<Guid> SurvivingAssociationIds { get { return _survivingAssociationIds; } }
		public string Reason { get; }
		public bool BlocksPlanning { get { return RemovalDecision == CollectionReplacementRemovalDecision.Blocked; } }
		public bool RequiresExplicitReview { get { return RemovalDecision == CollectionReplacementRemovalDecision.RequiresExplicitReview; } }
	}

	/// <summary>Read-only C8.1 classification for one existing Collection association.</summary>
	public sealed class CollectionReplacementAssociationImpact
	{
		internal CollectionReplacementAssociationImpact(CollectionTargetAssociation association,
			CollectionReplacementAssociationDisposition disposition, bool hasCustomization, string reason)
		{
			Association = association ?? throw new ArgumentNullException(nameof(association));
			if (!Enum.IsDefined(typeof(CollectionReplacementAssociationDisposition), disposition) || disposition == CollectionReplacementAssociationDisposition.Unknown)
				throw new ArgumentOutOfRangeException(nameof(disposition));
			Disposition = disposition;
			HasCustomization = hasCustomization;
			Reason = reason ?? String.Empty;
		}

		public CollectionTargetAssociation Association { get; }
		public CollectionReplacementAssociationDisposition Disposition { get; }
		public bool HasCustomization { get; }
		public string Reason { get; }
		public bool BlocksPlanning { get { return Disposition == CollectionReplacementAssociationDisposition.Blocked; } }
		public bool RequiresExplicitReview { get { return Disposition == CollectionReplacementAssociationDisposition.RequiresExplicitReview; } }
	}

	/// <summary>Read-only C8.1 classification for one observed native effect.</summary>
	public sealed class CollectionReplacementEffectImpact
	{
		private readonly ReadOnlyCollection<string> _ownerKeys;

		internal CollectionReplacementEffectImpact(CollectionReplacementEffectKind kind, string resourceKey,
			CollectionReplacementEffectDisposition disposition, string currentOwnerKey, string projectedOwnerKey,
			IEnumerable<string> ownerKeys, string reason)
		{
			if (!Enum.IsDefined(typeof(CollectionReplacementEffectKind), kind) || kind == CollectionReplacementEffectKind.Unknown)
				throw new ArgumentOutOfRangeException(nameof(kind));
			if (String.IsNullOrWhiteSpace(resourceKey))
				throw new ArgumentException("A replacement effect impact requires a stable resource key.", nameof(resourceKey));
			if (!Enum.IsDefined(typeof(CollectionReplacementEffectDisposition), disposition) || disposition == CollectionReplacementEffectDisposition.Unknown)
				throw new ArgumentOutOfRangeException(nameof(disposition));
			Kind = kind;
			ResourceKey = resourceKey;
			Disposition = disposition;
			CurrentOwnerKey = currentOwnerKey;
			ProjectedOwnerKey = projectedOwnerKey;
			_ownerKeys = new ReadOnlyCollection<string>((ownerKeys ?? throw new ArgumentNullException(nameof(ownerKeys)))
				.Where(x => !String.IsNullOrWhiteSpace(x)).ToList());
			Reason = reason ?? String.Empty;
		}

		public CollectionReplacementEffectKind Kind { get; }
		public string ResourceKey { get; }
		public CollectionReplacementEffectDisposition Disposition { get; }
		public string CurrentOwnerKey { get; }
		public string ProjectedOwnerKey { get; }
		public ReadOnlyCollection<string> OwnerKeys { get { return _ownerKeys; } }
		public string Reason { get; }
		public bool BlocksPlanning { get { return Disposition == CollectionReplacementEffectDisposition.BlockedAmbiguous; } }
	}

	/// <summary>
	/// Immutable C8.1 read-only difference between the current managed setup and one resolved incoming replacement selection.
	/// </summary>
	/// <remarks>
	/// This type is intentionally non-executable. It does not contain native tasks, removal authorization, persisted consent,
	/// recovery inputs or replacement-environment projections. Unknown/unmanaged content outside the observed native index is preserved.
	/// </remarks>
	public sealed class CollectionReplacementDiffPlan
	{
		private readonly ReadOnlyCollection<CollectionReplacementIncomingMemberImpact> _incomingMembers;
		private readonly ReadOnlyCollection<CollectionReplacementNativeModImpact> _nativeMods;
		private readonly ReadOnlyCollection<CollectionReplacementAssociationImpact> _associations;
		private readonly ReadOnlyCollection<CollectionReplacementEffectImpact> _effects;

		internal CollectionReplacementDiffPlan(ResolvedCollectionPlan incomingPlan,
			CollectionReplacementCurrentSetupSnapshot currentSetup,
			IEnumerable<CollectionReplacementIncomingMemberImpact> incomingMembers,
			IEnumerable<CollectionReplacementNativeModImpact> nativeMods,
			IEnumerable<CollectionReplacementAssociationImpact> associations,
			IEnumerable<CollectionReplacementEffectImpact> effects)
		{
			IncomingPlan = incomingPlan ?? throw new ArgumentNullException(nameof(incomingPlan));
			CurrentSetup = currentSetup ?? throw new ArgumentNullException(nameof(currentSetup));
			_incomingMembers = Copy(incomingMembers, nameof(incomingMembers), x => x.Member.MemberKey.ToString());
			_nativeMods = Copy(nativeMods, nameof(nativeMods), x => x.NativeMod.Identity.NativeModKey);
			_associations = new ReadOnlyCollection<CollectionReplacementAssociationImpact>((associations ?? throw new ArgumentNullException(nameof(associations)))
				.OrderBy(x => x.Association.AssociationId).ToList());
			_effects = new ReadOnlyCollection<CollectionReplacementEffectImpact>((effects ?? throw new ArgumentNullException(nameof(effects)))
				.OrderBy(x => (int)x.Kind).ThenBy(x => x.ResourceKey, StringComparer.OrdinalIgnoreCase).ToList());
		}

		public ResolvedCollectionPlan IncomingPlan { get; }
		public CollectionReplacementCurrentSetupSnapshot CurrentSetup { get; }
		public CollectionTargetIdentity Target { get { return IncomingPlan.Target; } }
		public CollectionPlanIdentity PlanIdentity { get { return IncomingPlan.Identity; } }
		public CollectionRevisionIdentity IncomingRevision { get { return IncomingPlan.Revision; } }
		public CollectionCurrentStateFingerprint NativeStateFingerprint { get { return CurrentSetup.NativeState.Fingerprint; } }
		public CollectionCurrentStateFingerprint DecisionInputFingerprint { get { return CurrentSetup.DecisionFingerprint; } }
		public ReadOnlyCollection<CollectionReplacementIncomingMemberImpact> IncomingMembers { get { return _incomingMembers; } }
		public ReadOnlyCollection<CollectionReplacementNativeModImpact> NativeMods { get { return _nativeMods; } }
		public ReadOnlyCollection<CollectionReplacementAssociationImpact> Associations { get { return _associations; } }
		public ReadOnlyCollection<CollectionReplacementEffectImpact> Effects { get { return _effects; } }
		public bool CurrentStateMatchesPlan { get { return IncomingPlan.CurrentStateFingerprint.Equals(CurrentSetup.NativeState.Fingerprint); } }
		public bool PreservesUnknownOrUnmanagedContent { get { return true; } }
		public bool IsCompletePhysicalGameDirectoryInventory { get { return false; } }
		public bool HasBlockers
		{
			get { return _incomingMembers.Any(x => x.BlocksPlanning) || _nativeMods.Any(x => x.BlocksPlanning) ||
				_associations.Any(x => x.BlocksPlanning) || _effects.Any(x => x.BlocksPlanning); }
		}
		public bool HasUnresolvedReviewDecisions
		{
			get { return _nativeMods.Any(x => x.RequiresExplicitReview) || _associations.Any(x => x.RequiresExplicitReview); }
		}

		private static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values, string parameterName, Func<T, string> orderKey) where T : class
		{
			List<T> copied = (values ?? throw new ArgumentNullException(parameterName)).ToList();
			if (copied.Any(x => x == null))
				throw new ArgumentException("A replacement diff plan cannot contain null impacts.", parameterName);
			return new ReadOnlyCollection<T>(copied.OrderBy(orderKey, StringComparer.Ordinal).ToList());
		}
	}
}
