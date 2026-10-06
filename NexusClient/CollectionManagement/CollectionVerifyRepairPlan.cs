using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Classifies one observed C10 verify/repair difference.</summary>
	public enum CollectionVerifyRepairFindingKind
	{
		Satisfied = 0,
		PreservedExplicitOverride = 1,
		MemberParticipationMismatch = 2,
		DetectedDrift = 3,
		BindingMismatch = 4,
		UncharacterizedModification = 5,
		AssociationRecovering = 6,
		ManifestIncomplete = 7,
		StateCoverageUnavailable = 8,
		ManagedFileEffectMismatch = 9,
		IniEffectMismatch = 10,
		GameValueEffectMismatch = 11,
		PluginEffectMismatch = 12,
		FileContentVerificationUnavailable = 13,
		ExactRecipePreparationUnavailable = 14
	}

	/// <summary>Describes what a later explicit repair command may do with one verified difference.</summary>
	public enum CollectionVerifyRepairDisposition
	{
		None = 0,
		PreserveLocalDecision = 1,
		RestoreExpectedState = 2,
		ActionRequired = 3
	}

	/// <summary>One immutable C10 verify/repair finding.</summary>
	public sealed class CollectionVerifyRepairFinding
	{
		internal CollectionVerifyRepairFinding(CollectionMemberKey memberKey, CollectionRequirementReference requirement,
			CollectionVerifyRepairFindingKind kind, CollectionVerifyRepairDisposition disposition,
			CollectionRequirementState expectedState, CollectionRequirementState observedState, string detail)
		{
			MemberKey = memberKey;
			Requirement = requirement;
			Kind = kind;
			Disposition = disposition;
			ExpectedState = expectedState;
			ObservedState = observedState;
			Detail = detail ?? String.Empty;
		}

		public CollectionMemberKey MemberKey { get; }
		public CollectionRequirementReference Requirement { get; }
		public CollectionVerifyRepairFindingKind Kind { get; }
		public CollectionVerifyRepairDisposition Disposition { get; }
		public CollectionRequirementState ExpectedState { get; }
		public CollectionRequirementState ObservedState { get; }
		public string Detail { get; }
		public bool IsRepairable { get { return Disposition == CollectionVerifyRepairDisposition.RestoreExpectedState; } }
		public bool RequiresAction { get { return Disposition == CollectionVerifyRepairDisposition.ActionRequired; } }
	}

	/// <summary>
	/// Read-only C10 verify/repair result. Exact prepared effects permit only the characterized qualified-repair subset;
	/// unsupported or ambiguous differences remain explicit ActionRequired findings.
	/// </summary>
	public sealed class CollectionVerifyRepairPlan
	{
		private readonly ReadOnlyCollection<CollectionVerifyRepairFinding> _findings;
		private readonly ReadOnlyCollection<PreparedCollectionNativeRecipe> _preparedRecipes;
		private readonly ReadOnlyCollection<CollectionMemberBinding> _bindingUpdates;

		internal CollectionVerifyRepairPlan(CollectionTargetAssociation association, CollectionCurrentStateFingerprint fingerprint,
			IEnumerable<CollectionVerifyRepairFinding> findings, bool exactEffectVerificationAvailable,
			ResolvedCollectionPlan resolvedPlan = null, IEnumerable<PreparedCollectionNativeRecipe> preparedRecipes = null,
			IEnumerable<CollectionMemberBinding> bindingUpdates = null)
		{
			Association = association ?? throw new ArgumentNullException(nameof(association));
			StateFingerprint = fingerprint ?? throw new ArgumentNullException(nameof(fingerprint));
			_findings = new ReadOnlyCollection<CollectionVerifyRepairFinding>((findings ?? throw new ArgumentNullException(nameof(findings))).ToList());
			ExactEffectVerificationAvailable = exactEffectVerificationAvailable;
			ResolvedPlan = resolvedPlan;
			_preparedRecipes = new ReadOnlyCollection<PreparedCollectionNativeRecipe>((preparedRecipes ?? Enumerable.Empty<PreparedCollectionNativeRecipe>()).ToList());
			_bindingUpdates = new ReadOnlyCollection<CollectionMemberBinding>((bindingUpdates ?? Enumerable.Empty<CollectionMemberBinding>()).ToList());
		}

		public CollectionTargetAssociation Association { get; }
		public CollectionCurrentStateFingerprint StateFingerprint { get; }
		public ReadOnlyCollection<CollectionVerifyRepairFinding> Findings { get { return _findings; } }
		public bool ExactEffectVerificationAvailable { get; }
		public ResolvedCollectionPlan ResolvedPlan { get; }
		public ReadOnlyCollection<PreparedCollectionNativeRecipe> PreparedRecipes { get { return _preparedRecipes; } }
		public ReadOnlyCollection<CollectionMemberBinding> BindingUpdates { get { return _bindingUpdates; } }
		public bool HasRepairableDifferences { get { return _findings.Any(x => x.IsRepairable); } }
		public bool HasActionRequired { get { return _findings.Any(x => x.RequiresAction); } }
		public bool IsHealthyAtCurrentCoverage
		{
			get { return !HasRepairableDifferences && !HasActionRequired; }
		}
		public bool CanExecuteQualifiedRepair
		{
			get
			{
				if (!HasRepairableDifferences || HasActionRequired || !ExactEffectVerificationAvailable || _bindingUpdates.Count > 0) return false;
				foreach (CollectionVerifyRepairFinding finding in _findings.Where(x => x.IsRepairable))
				{
					if (finding.Requirement == null) return false;
					if (finding.Requirement.Aspect == CollectionRequirementAspect.MemberEnabledState) continue;
					if (finding.MemberKey == null && finding.Requirement.Aspect == CollectionRequirementAspect.PluginState &&
						finding.Kind == CollectionVerifyRepairFindingKind.PluginEffectMismatch && ResolvedPlan != null &&
						CollectionPluginRelativeOrderApplicator.IsRequirementSubject(finding.Requirement.SubjectKey)) continue;
					if (finding.MemberKey == null || ResolvedPlan == null || !_preparedRecipes.Any(x => x.Member.MemberKey.Equals(finding.MemberKey))) return false;
					if (finding.Kind != CollectionVerifyRepairFindingKind.ManagedFileEffectMismatch &&
						finding.Kind != CollectionVerifyRepairFindingKind.IniEffectMismatch &&
						finding.Kind != CollectionVerifyRepairFindingKind.GameValueEffectMismatch &&
						finding.Kind != CollectionVerifyRepairFindingKind.PluginEffectMismatch) return false;
				}
				return true;
			}
		}
	}
}
