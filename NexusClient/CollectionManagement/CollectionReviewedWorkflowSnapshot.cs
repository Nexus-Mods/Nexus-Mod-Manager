using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.OnlineServices.NexusMods.Collections;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Immutable, non-executable C6.15.10 snapshot of one exact reviewed additive workflow.
	/// </summary>
	/// <remarks>
	/// The snapshot contains only durable identities, deterministic review descriptors and retained-content references.
	/// It intentionally contains no signed URL, API credential, live installer handle, background task or mutable IMod instance.
	/// </remarks>
	public sealed class CollectionReviewedWorkflowSnapshot
	{
		private readonly ReadOnlyCollection<CollectionReviewedMemberSnapshot> _members;
		private readonly ReadOnlyCollection<CollectionReviewedDependencySnapshot> _dependencies;
		private readonly ReadOnlyCollection<CollectionReviewedPrioritySnapshot> _priorityRules;
		private readonly ReadOnlyCollection<CollectionReviewedPhaseSnapshot> _phases;
		private readonly ReadOnlyCollection<CollectionReviewedBarrierSnapshot> _barriers;
		private readonly ReadOnlyCollection<CollectionReviewedFileImpactSnapshot> _fileImpacts;
		private readonly ReadOnlyCollection<CollectionReviewedPluginImpactSnapshot> _pluginImpacts;
		private readonly ReadOnlyCollection<CollectionReviewedConfigurationImpactSnapshot> _configurationImpacts;
		private readonly ReadOnlyCollection<CollectionReviewedAssociationImpactSnapshot> _associationImpacts;
		private readonly ReadOnlyCollection<CollectionReviewedPreparedRecipeSnapshot> _preparedRecipes;

		internal CollectionReviewedWorkflowSnapshot(CollectionPlanIdentity identity, CollectionRevisionIdentity revision,
			CollectionTargetIdentity target, CollectionExecutionPolicyKind policyKind,
			CollectionReplacementBackupChoice replacementBackupChoice, CollectionCurrentStateFingerprint approvedStateFingerprint,
			CollectionManifestSourceSnapshot manifestSource, IEnumerable<CollectionReviewedMemberSnapshot> members,
			IEnumerable<CollectionReviewedDependencySnapshot> dependencies, IEnumerable<CollectionReviewedPrioritySnapshot> priorityRules,
			IEnumerable<CollectionReviewedPhaseSnapshot> phases, IEnumerable<CollectionReviewedBarrierSnapshot> barriers,
			IEnumerable<CollectionReviewedFileImpactSnapshot> fileImpacts, IEnumerable<CollectionReviewedPluginImpactSnapshot> pluginImpacts,
			IEnumerable<CollectionReviewedConfigurationImpactSnapshot> configurationImpacts,
			IEnumerable<CollectionReviewedAssociationImpactSnapshot> associationImpacts,
			IEnumerable<CollectionReviewedPreparedRecipeSnapshot> preparedRecipes)
		{
			Identity = identity ?? throw new ArgumentNullException(nameof(identity));
			Revision = revision ?? throw new ArgumentNullException(nameof(revision));
			Target = target ?? throw new ArgumentNullException(nameof(target));
			if (!Enum.IsDefined(typeof(CollectionExecutionPolicyKind), policyKind) || policyKind == CollectionExecutionPolicyKind.Unknown)
				throw new ArgumentOutOfRangeException(nameof(policyKind));
			if (!Enum.IsDefined(typeof(CollectionReplacementBackupChoice), replacementBackupChoice))
				throw new ArgumentOutOfRangeException(nameof(replacementBackupChoice));
			ApprovedStateFingerprint = approvedStateFingerprint ?? throw new ArgumentNullException(nameof(approvedStateFingerprint));
			ManifestSource = manifestSource ?? throw new ArgumentNullException(nameof(manifestSource));
			if (policyKind == CollectionExecutionPolicyKind.InstallIntoCurrentSetup &&
				replacementBackupChoice != CollectionReplacementBackupChoice.NotApplicable)
				throw new ArgumentException("Additive reviewed workflow cannot carry a replacement backup choice.", nameof(replacementBackupChoice));
			PolicyKind = policyKind;
			ReplacementBackupChoice = replacementBackupChoice;
			_members = Copy(members, nameof(members));
			_dependencies = Copy(dependencies, nameof(dependencies));
			_priorityRules = Copy(priorityRules, nameof(priorityRules));
			_phases = Copy(phases, nameof(phases));
			_barriers = Copy(barriers, nameof(barriers));
			_fileImpacts = Copy(fileImpacts, nameof(fileImpacts));
			_pluginImpacts = Copy(pluginImpacts, nameof(pluginImpacts));
			_configurationImpacts = Copy(configurationImpacts, nameof(configurationImpacts));
			_associationImpacts = Copy(associationImpacts, nameof(associationImpacts));
			_preparedRecipes = Copy(preparedRecipes, nameof(preparedRecipes));
			ValidateClosure();
		}

		public CollectionPlanIdentity Identity { get; }
		public CollectionRevisionIdentity Revision { get; }
		public CollectionTargetIdentity Target { get; }
		public CollectionExecutionPolicyKind PolicyKind { get; }
		public CollectionReplacementBackupChoice ReplacementBackupChoice { get; }
		public CollectionCurrentStateFingerprint ApprovedStateFingerprint { get; }
		public CollectionManifestSourceSnapshot ManifestSource { get; }
		public ReadOnlyCollection<CollectionReviewedMemberSnapshot> Members { get { return _members; } }
		public ReadOnlyCollection<CollectionReviewedDependencySnapshot> Dependencies { get { return _dependencies; } }
		public ReadOnlyCollection<CollectionReviewedPrioritySnapshot> PriorityRules { get { return _priorityRules; } }
		public ReadOnlyCollection<CollectionReviewedPhaseSnapshot> Phases { get { return _phases; } }
		public ReadOnlyCollection<CollectionReviewedBarrierSnapshot> Barriers { get { return _barriers; } }
		public ReadOnlyCollection<CollectionReviewedFileImpactSnapshot> FileImpacts { get { return _fileImpacts; } }
		public ReadOnlyCollection<CollectionReviewedPluginImpactSnapshot> PluginImpacts { get { return _pluginImpacts; } }
		public ReadOnlyCollection<CollectionReviewedConfigurationImpactSnapshot> ConfigurationImpacts { get { return _configurationImpacts; } }
		public ReadOnlyCollection<CollectionReviewedAssociationImpactSnapshot> AssociationImpacts { get { return _associationImpacts; } }
		public ReadOnlyCollection<CollectionReviewedPreparedRecipeSnapshot> PreparedRecipes { get { return _preparedRecipes; } }

		/// <summary>Builds the exact v2 snapshot from already validated C6.1-C6.4 review inputs and optional C6.15.9 prepared recipes.</summary>
		public static CollectionReviewedWorkflowSnapshot Create(ResolvedCollectionPlan plan,
			CollectionDependencyPhasePlan dependencyPlan, CollectionConflictImpactPlan impactPlan,
			IEnumerable<PreparedCollectionNativeRecipe> preparedRecipes)
		{
			if (plan == null) throw new ArgumentNullException(nameof(plan));
			if (dependencyPlan == null) throw new ArgumentNullException(nameof(dependencyPlan));
			if (impactPlan == null) throw new ArgumentNullException(nameof(impactPlan));
			if (preparedRecipes == null) throw new ArgumentNullException(nameof(preparedRecipes));
			if (!dependencyPlan.PlanIdentity.Equals(plan.Identity) || !impactPlan.PlanIdentity.Equals(plan.Identity) ||
				!dependencyPlan.Target.Equals(plan.Target) || !impactPlan.Target.Equals(plan.Target) ||
				!dependencyPlan.StateFingerprint.Equals(plan.CurrentStateFingerprint) ||
				!impactPlan.StateFingerprint.Equals(plan.CurrentStateFingerprint))
				throw new ArgumentException("Reviewed workflow inputs must belong to the exact same plan and native-state fingerprint.");
			if (!dependencyPlan.IsReady || !impactPlan.IsReady)
				throw new InvalidOperationException("Only a fully ready C6.3/C6.4 review can be persisted as reviewed workflow v2.");

			var members = plan.SelectedMembers.Select(x => new CollectionReviewedMemberSnapshot(x.MemberKey, x.SourceOrdinal,
				x.Requirement, x.InstallationPhase, x.RecipeIdentity.Fingerprint, x.ArtifactChoice.Kind,
				x.ArtifactChoice.RequestedArtifact, x.ArtifactChoice.SelectedArtifact, x.ArtifactChoice.SubstitutionRuleId)).ToList();
			var dependencies = plan.CapabilityReport.Manifest.Dependencies.Select(x =>
				new CollectionReviewedDependencySnapshot(x.PrerequisiteMemberKey, x.DependentMemberKey, x.Kind)).ToList();
			var priorities = plan.CapabilityReport.Manifest.FilePriorityRules.Select(x =>
				new CollectionReviewedPrioritySnapshot(x.LowerPriorityMemberKey, x.HigherPriorityMemberKey)).ToList();

			var phases = new List<CollectionReviewedPhaseSnapshot>();
			var requiredPrepared = new HashSet<CollectionMemberKey>();
			foreach (CollectionExecutionPhase phase in dependencyPlan.Phases)
			{
				var phaseMembers = new List<CollectionReviewedPhaseMemberSnapshot>();
				foreach (CollectionPlannedPhaseMember phaseMember in phase.Members)
				{
					CollectionMemberMatchResult match = phaseMember.Match;
					if (match.Disposition == CollectionMemberMatchDisposition.ArchiveOnlyReuse ||
						match.Disposition == CollectionMemberMatchDisposition.ReinstallRequired)
						requiredPrepared.Add(phaseMember.MemberKey);
					else if (match.Disposition != CollectionMemberMatchDisposition.InstalledCompatible)
						throw new InvalidOperationException("A reviewed workflow cannot persist a member that still requires acquisition or is blocked.");

					phaseMembers.Add(CollectionReviewedPhaseMemberSnapshot.From(match));
				}
				phases.Add(new CollectionReviewedPhaseSnapshot(phase.PhaseNumber, phaseMembers));
			}

			var recipes = new List<CollectionReviewedPreparedRecipeSnapshot>();
			var recipeKeys = new HashSet<CollectionMemberKey>();
			foreach (PreparedCollectionNativeRecipe recipe in preparedRecipes)
			{
				if (recipe == null) throw new ArgumentException("Prepared recipes cannot contain null values.", nameof(preparedRecipes));
				ResolvedCollectionMemberPlan planMember = plan.SelectedMembers.SingleOrDefault(x => x.MemberKey.Equals(recipe.Member.MemberKey));
				if (planMember == null || !planMember.RecipeIdentity.Equals(recipe.ProviderRecipeIdentity) ||
					planMember.SourceOrdinal != recipe.Member.SourceOrdinal)
					throw new ArgumentException("Prepared recipes must belong to the exact selected plan member.", nameof(preparedRecipes));
				if (!recipeKeys.Add(recipe.Member.MemberKey))
					throw new ArgumentException("Prepared recipes cannot contain duplicate member keys.", nameof(preparedRecipes));
				recipes.Add(CollectionReviewedPreparedRecipeSnapshot.From(recipe));
			}
			foreach (CollectionMemberKey requiredKey in requiredPrepared)
				if (!recipeKeys.Contains(requiredKey))
					throw new InvalidOperationException("Every reviewed member that will require native installation must have one exact prepared native recipe descriptor.");

			var fileImpacts = impactPlan.FileImpacts.Select(x => new CollectionReviewedFileImpactSnapshot(x.Target, x.Writers,
				x.PlannedWinner, x.CurrentOwnerKey, x.AffectedAssociationIds)).ToList();
			var pluginImpacts = impactPlan.PluginImpacts.Select(x => new CollectionReviewedPluginImpactSnapshot(x.MemberKey,
				x.Effect, x.AffectedAssociationIds)).ToList();
			var configurationImpacts = impactPlan.ConfigurationImpacts.Select(x => new CollectionReviewedConfigurationImpactSnapshot(
				x.MemberKey, x.Kind, x.SubjectKey, x.CurrentOwnerKey, x.ChangesRecordedValue, x.AffectedAssociationIds)).ToList();
			var associationImpacts = impactPlan.AssociationImpacts.Select(x => new CollectionReviewedAssociationImpactSnapshot(
				x.Association.AssociationId, x.Kind)).ToList();
			var barriers = dependencyPlan.Barriers.Select(x => new CollectionReviewedBarrierSnapshot(x.CompletedPhase, x.NextPhase, x.Kind)).ToList();

			return new CollectionReviewedWorkflowSnapshot(plan.Identity, plan.Revision, plan.Target, plan.Policy.Kind,
				plan.Policy.ReplacementBackupChoice, plan.CurrentStateFingerprint, plan.ManifestSource, members, dependencies,
				priorities, phases, barriers, fileImpacts, pluginImpacts, configurationImpacts, associationImpacts, recipes);
		}

		private void ValidateClosure()
		{
			var keys = new HashSet<CollectionMemberKey>();
			foreach (CollectionReviewedMemberSnapshot member in _members)
				if (!keys.Add(member.MemberKey)) throw new InvalidDataException("Reviewed workflow contains duplicate selected members.");
			var phaseKeys = new HashSet<CollectionMemberKey>();
			foreach (CollectionReviewedPhaseSnapshot phase in _phases)
				foreach (CollectionReviewedPhaseMemberSnapshot member in phase.Members)
				{
					if (!keys.Contains(member.MemberKey) || !phaseKeys.Add(member.MemberKey))
						throw new InvalidDataException("Reviewed workflow phases must cover selected members exactly once.");
				}
			if (phaseKeys.Count != keys.Count)
				throw new InvalidDataException("Reviewed workflow phases do not cover the complete selected closure.");
			var membersByKey = _members.ToDictionary(x => x.MemberKey);
			var recipeKeys = new HashSet<CollectionMemberKey>();
			foreach (CollectionReviewedPreparedRecipeSnapshot recipe in _preparedRecipes)
			{
				CollectionReviewedMemberSnapshot member;
				if (!membersByKey.TryGetValue(recipe.MemberKey, out member) || !recipeKeys.Add(recipe.MemberKey))
					throw new InvalidDataException("Reviewed workflow contains a duplicate or unknown prepared-recipe member.");
				if (member.SourceOrdinal != recipe.SourceOrdinal || !StringComparer.Ordinal.Equals(member.RecipeFingerprint, recipe.ProviderRecipeFingerprint))
					throw new InvalidDataException("Prepared-recipe descriptor no longer matches its selected member provenance.");
			}
			foreach (CollectionReviewedPhaseSnapshot phase in _phases)
				foreach (CollectionReviewedPhaseMemberSnapshot member in phase.Members)
				{
					bool requiresRecipe = member.Disposition == CollectionMemberMatchDisposition.ArchiveOnlyReuse ||
						member.Disposition == CollectionMemberMatchDisposition.ReinstallRequired;
					if (requiresRecipe && !recipeKeys.Contains(member.MemberKey))
						throw new InvalidDataException("A mutating reviewed member is missing its reproducible prepared-native recipe descriptor.");
					if (!requiresRecipe && member.Disposition != CollectionMemberMatchDisposition.InstalledCompatible)
						throw new InvalidDataException("A reviewed workflow contains an unresolved member match disposition.");
				}
		}

		private static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values, string parameterName) where T : class
		{
			if (values == null) throw new ArgumentNullException(parameterName);
			List<T> copied = values.ToList();
			if (copied.Any(x => x == null)) throw new ArgumentException("Snapshot collections cannot contain null values.", parameterName);
			return new ReadOnlyCollection<T>(copied);
		}
	}

	/// <summary>Exact selected-member descriptor persisted in reviewed workflow v2.</summary>
	public sealed class CollectionReviewedMemberSnapshot
	{
		internal CollectionReviewedMemberSnapshot(CollectionMemberKey memberKey, int sourceOrdinal,
			CollectionMemberRequirement requirement, double installationPhase, string recipeFingerprint,
			CollectionResolvedArtifactChoiceKind artifactChoiceKind, CollectionArtifactReference requestedArtifact,
			CollectionArtifactReference selectedArtifact, string substitutionRuleId)
		{
			MemberKey = memberKey ?? throw new ArgumentNullException(nameof(memberKey));
			if (sourceOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(sourceOrdinal));
			SourceOrdinal = sourceOrdinal;
			Requirement = requirement;
			InstallationPhase = installationPhase;
			RecipeFingerprint = CollectionIdentityValidation.RequireOpaqueToken(recipeFingerprint, nameof(recipeFingerprint));
			ArtifactChoiceKind = artifactChoiceKind;
			RequestedArtifact = requestedArtifact ?? throw new ArgumentNullException(nameof(requestedArtifact));
			SelectedArtifact = selectedArtifact ?? throw new ArgumentNullException(nameof(selectedArtifact));
			SubstitutionRuleId = substitutionRuleId;
		}
		public CollectionMemberKey MemberKey { get; }
		public int SourceOrdinal { get; }
		public CollectionMemberRequirement Requirement { get; }
		public double InstallationPhase { get; }
		public string RecipeFingerprint { get; }
		public CollectionResolvedArtifactChoiceKind ArtifactChoiceKind { get; }
		public CollectionArtifactReference RequestedArtifact { get; }
		public CollectionArtifactReference SelectedArtifact { get; }
		public string SubstitutionRuleId { get; }
	}

	/// <summary>Exact dependency edge persisted in reviewed workflow v2.</summary>
	public sealed class CollectionReviewedDependencySnapshot
	{
		internal CollectionReviewedDependencySnapshot(CollectionMemberKey prerequisite, CollectionMemberKey dependent,
			CollectionMemberDependencyKind kind)
		{
			Prerequisite = prerequisite ?? throw new ArgumentNullException(nameof(prerequisite));
			Dependent = dependent ?? throw new ArgumentNullException(nameof(dependent));
			Kind = kind;
		}
		public CollectionMemberKey Prerequisite { get; }
		public CollectionMemberKey Dependent { get; }
		public CollectionMemberDependencyKind Kind { get; }
	}

	/// <summary>Exact relative file-priority rule persisted in reviewed workflow v2.</summary>
	public sealed class CollectionReviewedPrioritySnapshot
	{
		internal CollectionReviewedPrioritySnapshot(CollectionMemberKey lower, CollectionMemberKey higher)
		{
			Lower = lower ?? throw new ArgumentNullException(nameof(lower));
			Higher = higher ?? throw new ArgumentNullException(nameof(higher));
		}
		public CollectionMemberKey Lower { get; }
		public CollectionMemberKey Higher { get; }
	}

	/// <summary>One persisted C6.2 match/native-candidate descriptor inside a C6.3 phase.</summary>
	public sealed class CollectionReviewedPhaseMemberSnapshot
	{
		private readonly ReadOnlyCollection<CollectionReviewedNativeCandidateSnapshot> _nativeCandidates;
		private readonly ReadOnlyCollection<CollectionReviewedBindingSnapshot> _bindings;
		internal CollectionReviewedPhaseMemberSnapshot(CollectionMemberKey memberKey, CollectionMemberMatchDisposition disposition,
			CollectionMemberMatchReason reason, IEnumerable<CollectionReviewedNativeCandidateSnapshot> nativeCandidates,
			IEnumerable<CollectionReviewedBindingSnapshot> bindings, CollectionReviewedVerifiedArchiveSnapshot verifiedArchive)
		{
			MemberKey = memberKey ?? throw new ArgumentNullException(nameof(memberKey));
			Disposition = disposition;
			Reason = reason;
			_nativeCandidates = new ReadOnlyCollection<CollectionReviewedNativeCandidateSnapshot>((nativeCandidates ?? throw new ArgumentNullException(nameof(nativeCandidates))).ToList());
			_bindings = new ReadOnlyCollection<CollectionReviewedBindingSnapshot>((bindings ?? throw new ArgumentNullException(nameof(bindings))).ToList());
			VerifiedArchive = verifiedArchive;
		}
		public CollectionMemberKey MemberKey { get; }
		public CollectionMemberMatchDisposition Disposition { get; }
		public CollectionMemberMatchReason Reason { get; }
		public ReadOnlyCollection<CollectionReviewedNativeCandidateSnapshot> NativeCandidates { get { return _nativeCandidates; } }
		public ReadOnlyCollection<CollectionReviewedBindingSnapshot> Bindings { get { return _bindings; } }
		public CollectionReviewedVerifiedArchiveSnapshot VerifiedArchive { get; }

		internal static CollectionReviewedPhaseMemberSnapshot From(CollectionMemberMatchResult match)
		{
			var candidates = match.NativeCandidates.Select(x => new CollectionReviewedNativeCandidateSnapshot(x.Identity.Target.Fingerprint,
				x.Identity.NativeModKey, x.NexusModId, x.NexusFileId, x.InstallRoot, x.InstallMethod)).ToList();
			var bindings = match.ExistingBindings.Select(x => new CollectionReviewedBindingSnapshot(x.Association.AssociationId,
				x.Association.State, x.MemberKey, x.NativeMod.Target.Fingerprint, x.NativeMod.NativeModKey,
				x.VerifiedRecipe.Fingerprint, x.BindingKind)).ToList();
			CollectionReviewedVerifiedArchiveSnapshot archive = null;
			if (match.VerifiedArchive != null)
				archive = new CollectionReviewedVerifiedArchiveSnapshot(match.VerifiedArchive.Artifact.ArtifactId,
					match.VerifiedArchive.Artifact.ContentHash, match.VerifiedArchive.Artifact.ByteLength,
					match.VerifiedArchive.SourceKind, match.VerifiedArchive.VerificationBasis);
			return new CollectionReviewedPhaseMemberSnapshot(match.Member.MemberKey, match.Disposition, match.Reason, candidates, bindings, archive);
		}
	}

	/// <summary>Durable identity/install-context descriptor for one C6.2 native-mod candidate.</summary>
	public sealed class CollectionReviewedNativeCandidateSnapshot
	{
		internal CollectionReviewedNativeCandidateSnapshot(string targetFingerprint, string nativeModKey, string nexusModId,
			string nexusFileId, ModInstallRoot installRoot, ModInstallMethod installMethod)
		{
			TargetFingerprint = CollectionIdentityValidation.RequireOpaqueToken(targetFingerprint, nameof(targetFingerprint));
			NativeModKey = CollectionIdentityValidation.RequireOpaqueToken(nativeModKey, nameof(nativeModKey));
			NexusModId = nexusModId ?? String.Empty;
			NexusFileId = nexusFileId ?? String.Empty;
			InstallRoot = installRoot;
			InstallMethod = installMethod;
		}
		public string TargetFingerprint { get; }
		public string NativeModKey { get; }
		public string NexusModId { get; }
		public string NexusFileId { get; }
		public ModInstallRoot InstallRoot { get; }
		public ModInstallMethod InstallMethod { get; }
	}

	/// <summary>Durable descriptor for one existing Collection member binding considered during review.</summary>
	public sealed class CollectionReviewedBindingSnapshot
	{
		internal CollectionReviewedBindingSnapshot(Guid associationId, CollectionAssociationState associationState,
			CollectionMemberKey memberKey, string targetFingerprint, string nativeModKey, string verifiedRecipeFingerprint,
			CollectionMemberBindingKind bindingKind)
		{
			if (associationId == Guid.Empty) throw new ArgumentException("Association id is required.", nameof(associationId));
			AssociationId = associationId;
			AssociationState = associationState;
			MemberKey = memberKey ?? throw new ArgumentNullException(nameof(memberKey));
			TargetFingerprint = CollectionIdentityValidation.RequireOpaqueToken(targetFingerprint, nameof(targetFingerprint));
			NativeModKey = CollectionIdentityValidation.RequireOpaqueToken(nativeModKey, nameof(nativeModKey));
			VerifiedRecipeFingerprint = CollectionIdentityValidation.RequireOpaqueToken(verifiedRecipeFingerprint, nameof(verifiedRecipeFingerprint));
			BindingKind = bindingKind;
		}
		public Guid AssociationId { get; }
		public CollectionAssociationState AssociationState { get; }
		public CollectionMemberKey MemberKey { get; }
		public string TargetFingerprint { get; }
		public string NativeModKey { get; }
		public string VerifiedRecipeFingerprint { get; }
		public CollectionMemberBindingKind BindingKind { get; }
	}

	/// <summary>Durable exact-content descriptor for one verified reusable archive.</summary>
	public sealed class CollectionReviewedVerifiedArchiveSnapshot
	{
		internal CollectionReviewedVerifiedArchiveSnapshot(string artifactId, CollectionContentHash contentHash, long byteLength,
			CollectionVerifiedArchiveSourceKind sourceKind, CollectionArchiveVerificationBasis verificationBasis)
		{
			ArtifactId = CollectionIdentityValidation.RequireOpaqueToken(artifactId, nameof(artifactId));
			ContentHash = contentHash ?? throw new ArgumentNullException(nameof(contentHash));
			if (byteLength < 0) throw new ArgumentOutOfRangeException(nameof(byteLength));
			ByteLength = byteLength;
			SourceKind = sourceKind;
			VerificationBasis = verificationBasis;
		}
		public string ArtifactId { get; }
		public CollectionContentHash ContentHash { get; }
		public long ByteLength { get; }
		public CollectionVerifiedArchiveSourceKind SourceKind { get; }
		public CollectionArchiveVerificationBasis VerificationBasis { get; }
	}

	/// <summary>Durable C6.3 execution-phase descriptor for reviewed members.</summary>
	public sealed class CollectionReviewedPhaseSnapshot
	{
		private readonly ReadOnlyCollection<CollectionReviewedPhaseMemberSnapshot> _members;
		internal CollectionReviewedPhaseSnapshot(double phaseNumber, IEnumerable<CollectionReviewedPhaseMemberSnapshot> members)
		{
			PhaseNumber = phaseNumber;
			_members = new ReadOnlyCollection<CollectionReviewedPhaseMemberSnapshot>((members ?? throw new ArgumentNullException(nameof(members))).ToList());
			if (_members.Count == 0) throw new ArgumentException("A reviewed phase cannot be empty.", nameof(members));
		}
		public double PhaseNumber { get; }
		public ReadOnlyCollection<CollectionReviewedPhaseMemberSnapshot> Members { get { return _members; } }
	}

	/// <summary>Durable C6.3 phase-barrier descriptor.</summary>
	public sealed class CollectionReviewedBarrierSnapshot
	{
		internal CollectionReviewedBarrierSnapshot(double completedPhase, double nextPhase, CollectionPhaseBarrierKind kind)
		{
			CompletedPhase = completedPhase;
			NextPhase = nextPhase;
			Kind = kind;
		}
		public double CompletedPhase { get; }
		public double NextPhase { get; }
		public CollectionPhaseBarrierKind Kind { get; }
	}

	/// <summary>Durable C6.4 file-impact and reviewed-winner descriptor.</summary>
	public sealed class CollectionReviewedFileImpactSnapshot
	{
		private readonly ReadOnlyCollection<CollectionMemberKey> _writers;
		private readonly ReadOnlyCollection<Guid> _affectedAssociationIds;
		internal CollectionReviewedFileImpactSnapshot(ModDeploymentTarget target, IEnumerable<CollectionMemberKey> writers,
			CollectionMemberKey plannedWinner, string currentOwnerKey, IEnumerable<Guid> affectedAssociationIds)
		{
			Target = target ?? throw new ArgumentNullException(nameof(target));
			_writers = new ReadOnlyCollection<CollectionMemberKey>((writers ?? throw new ArgumentNullException(nameof(writers))).ToList());
			PlannedWinner = plannedWinner;
			CurrentOwnerKey = currentOwnerKey;
			_affectedAssociationIds = new ReadOnlyCollection<Guid>((affectedAssociationIds ?? throw new ArgumentNullException(nameof(affectedAssociationIds))).OrderBy(x => x).ToList());
		}
		public ModDeploymentTarget Target { get; }
		public ReadOnlyCollection<CollectionMemberKey> Writers { get { return _writers; } }
		public CollectionMemberKey PlannedWinner { get; }
		public string CurrentOwnerKey { get; }
		public ReadOnlyCollection<Guid> AffectedAssociationIds { get { return _affectedAssociationIds; } }
	}

	/// <summary>Durable C6.4 plugin-impact descriptor.</summary>
	public sealed class CollectionReviewedPluginImpactSnapshot
	{
		private readonly ReadOnlyCollection<Guid> _affectedAssociationIds;
		internal CollectionReviewedPluginImpactSnapshot(CollectionMemberKey memberKey, CollectionPlannedPluginEffect effect,
			IEnumerable<Guid> affectedAssociationIds)
		{
			MemberKey = memberKey ?? throw new ArgumentNullException(nameof(memberKey));
			Effect = effect ?? throw new ArgumentNullException(nameof(effect));
			_affectedAssociationIds = new ReadOnlyCollection<Guid>((affectedAssociationIds ?? throw new ArgumentNullException(nameof(affectedAssociationIds))).OrderBy(x => x).ToList());
		}
		public CollectionMemberKey MemberKey { get; }
		public CollectionPlannedPluginEffect Effect { get; }
		public ReadOnlyCollection<Guid> AffectedAssociationIds { get { return _affectedAssociationIds; } }
	}

	/// <summary>Durable C6.4 configuration-impact descriptor.</summary>
	public sealed class CollectionReviewedConfigurationImpactSnapshot
	{
		private readonly ReadOnlyCollection<Guid> _affectedAssociationIds;
		internal CollectionReviewedConfigurationImpactSnapshot(CollectionMemberKey memberKey, CollectionConfigurationImpactKind kind,
			string subjectKey, string currentOwnerKey, bool changesRecordedValue, IEnumerable<Guid> affectedAssociationIds)
		{
			MemberKey = memberKey ?? throw new ArgumentNullException(nameof(memberKey));
			Kind = kind;
			SubjectKey = subjectKey ?? String.Empty;
			CurrentOwnerKey = currentOwnerKey;
			ChangesRecordedValue = changesRecordedValue;
			_affectedAssociationIds = new ReadOnlyCollection<Guid>((affectedAssociationIds ?? throw new ArgumentNullException(nameof(affectedAssociationIds))).OrderBy(x => x).ToList());
		}
		public CollectionMemberKey MemberKey { get; }
		public CollectionConfigurationImpactKind Kind { get; }
		public string SubjectKey { get; }
		public string CurrentOwnerKey { get; }
		public bool ChangesRecordedValue { get; }
		public ReadOnlyCollection<Guid> AffectedAssociationIds { get { return _affectedAssociationIds; } }
	}

	/// <summary>Durable C6.4 existing-association impact descriptor.</summary>
	public sealed class CollectionReviewedAssociationImpactSnapshot
	{
		internal CollectionReviewedAssociationImpactSnapshot(Guid associationId, CollectionAssociationImpactKind kind)
		{
			if (associationId == Guid.Empty) throw new ArgumentException("Association id is required.", nameof(associationId));
			AssociationId = associationId;
			Kind = kind;
		}
		public Guid AssociationId { get; }
		public CollectionAssociationImpactKind Kind { get; }
	}

	/// <summary>One exact source/destination mapping required to reproduce a reviewed simple-file native recipe.</summary>
	public sealed class CollectionReviewedSimpleFileMappingSnapshot
	{
		internal CollectionReviewedSimpleFileMappingSnapshot(string sourcePath, string destinationPath)
		{
			var mapping = new ModInstallationSimpleFileMapping(sourcePath, destinationPath);
			SourcePath = mapping.SourcePath;
			DestinationPath = mapping.DestinationPath;
		}

		public string SourcePath { get; }
		public string DestinationPath { get; }
	}

	/// <summary>Reproducible descriptor for one exact C6.15.9 prepared native recipe.</summary>
	public sealed class CollectionReviewedPreparedRecipeSnapshot
	{
		private readonly ReadOnlyCollection<string> _retainedArtifactIds;
		private readonly ReadOnlyCollection<CollectionReviewedSimpleFileMappingSnapshot> _simpleFileMappings;
		internal CollectionReviewedPreparedRecipeSnapshot(CollectionMemberKey memberKey, int sourceOrdinal,
			string providerRecipeFingerprint, string preparedNativeFingerprint, bool skipReadmeFiles,
			ModInstallationRecipeValidation validation, CollectionMemberEffectPreview effectPreview,
			IEnumerable<string> retainedArtifactIds, IEnumerable<CollectionReviewedSimpleFileMappingSnapshot> simpleFileMappings = null)
		{
			MemberKey = memberKey ?? throw new ArgumentNullException(nameof(memberKey));
			if (sourceOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(sourceOrdinal));
			SourceOrdinal = sourceOrdinal;
			ProviderRecipeFingerprint = CollectionIdentityValidation.RequireOpaqueToken(providerRecipeFingerprint, nameof(providerRecipeFingerprint));
			PreparedNativeFingerprint = CollectionIdentityValidation.RequireOpaqueToken(preparedNativeFingerprint, nameof(preparedNativeFingerprint));
			SkipReadmeFiles = skipReadmeFiles;
			Validation = validation ?? throw new ArgumentNullException(nameof(validation));
			EffectPreview = effectPreview ?? throw new ArgumentNullException(nameof(effectPreview));
			if (!effectPreview.MemberKey.Equals(MemberKey) ||
				!StringComparer.Ordinal.Equals(effectPreview.RecipeIdentity.Fingerprint, ProviderRecipeFingerprint) ||
				effectPreview.InstallMethod != validation.InstallContext.Method ||
				effectPreview.InstallRoot != validation.InstallContext.InstallRoot)
				throw new ArgumentException("Prepared recipe effect preview must match the exact member, recipe and install context.", nameof(effectPreview));
			List<string> ids = (retainedArtifactIds ?? throw new ArgumentNullException(nameof(retainedArtifactIds))).Select(x => CollectionIdentityValidation.RequireOpaqueToken(x, nameof(retainedArtifactIds))).ToList();
			if (ids.Count == 0 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
				throw new ArgumentException("Prepared recipe retained artifact ids must be non-empty and unique.", nameof(retainedArtifactIds));
			_retainedArtifactIds = new ReadOnlyCollection<string>(ids);
			List<CollectionReviewedSimpleFileMappingSnapshot> mappings = (simpleFileMappings ?? Enumerable.Empty<CollectionReviewedSimpleFileMappingSnapshot>()).ToList();
			if (mappings.Any(x => x == null))
				throw new ArgumentException("Prepared recipe simple-file mappings cannot contain null values.", nameof(simpleFileMappings));
			_simpleFileMappings = new ReadOnlyCollection<CollectionReviewedSimpleFileMappingSnapshot>(mappings);
		}
		public CollectionMemberKey MemberKey { get; }
		public int SourceOrdinal { get; }
		public string ProviderRecipeFingerprint { get; }
		public string PreparedNativeFingerprint { get; }
		public bool SkipReadmeFiles { get; }
		public ModInstallationRecipeValidation Validation { get; }
		public CollectionMemberEffectPreview EffectPreview { get; }
		public ReadOnlyCollection<string> RetainedArtifactIds { get { return _retainedArtifactIds; } }
		public ReadOnlyCollection<CollectionReviewedSimpleFileMappingSnapshot> SimpleFileMappings { get { return _simpleFileMappings; } }

		internal static CollectionReviewedPreparedRecipeSnapshot From(PreparedCollectionNativeRecipe recipe)
		{
			var mappings = new List<CollectionReviewedSimpleFileMappingSnapshot>();
			if (!StringComparer.Ordinal.Equals(recipe.AdapterId, ModInstallationSimpleFileRecipeAdapter.AdapterId))
				throw new NotSupportedException("The reviewed-workflow v2 executable descriptor currently supports only the characterized simple-file adapter.");
			foreach (var operation in recipe.RecipeInput.NativeOperations)
			{
				InstallModFileOperation file = operation as InstallModFileOperation;
				if (file == null || file.DeploymentDecision != null)
					throw new NotSupportedException("A reviewed simple-file recipe can persist only unresolved exact InstallModFile operations.");
				mappings.Add(new CollectionReviewedSimpleFileMappingSnapshot(file.SourcePath, file.DestinationPath));
			}
			if (mappings.Count == 0)
				throw new InvalidDataException("A reviewed simple-file recipe requires at least one reproducible source/destination mapping.");

			return new CollectionReviewedPreparedRecipeSnapshot(recipe.Member.MemberKey, recipe.Member.SourceOrdinal,
				recipe.ProviderRecipeIdentity.Fingerprint, recipe.PreparedNativeIdentity.Fingerprint, recipe.SkipReadmeFiles,
				recipe.Validation, recipe.EffectPreview, recipe.RetainedArtifactIds, mappings);
		}
	}

	/// <summary>Versioned binary codec for the durable C6.15.10 reviewed-workflow snapshot.</summary>
	public static class CollectionReviewedWorkflowSnapshotCodec
	{
		public const string PayloadFormat = "nmm-ce.collections.reviewed-workflow/2";
		public const string LegacyPayloadFormat = "nmm-ce.collections.coordinator-plan/1";
		private const int BinaryVersion = 3;
		private const int LegacyBinaryVersion = 2;
		private const int MaxCount = 100000;
		private const int MaxPayloadLength = 64 * 1024 * 1024;

		/// <summary>Serializes one immutable reviewed workflow using the v2 deterministic binary format.</summary>
		public static byte[] Serialize(CollectionReviewedWorkflowSnapshot snapshot)
		{
			if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
			using (var stream = new MemoryStream())
			using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), true))
			{
				writer.Write(BinaryVersion);
				WritePlanIdentity(writer, snapshot.Identity);
				WriteRevision(writer, snapshot.Revision);
				writer.Write(snapshot.Target.Fingerprint);
				writer.Write((int)snapshot.PolicyKind);
				writer.Write((int)snapshot.ReplacementBackupChoice);
				WriteStateFingerprint(writer, snapshot.ApprovedStateFingerprint);
				WriteManifestSource(writer, snapshot.ManifestSource);
				WriteMembers(writer, snapshot.Members);
				WriteDependencies(writer, snapshot.Dependencies);
				WritePriorities(writer, snapshot.PriorityRules);
				WritePhases(writer, snapshot.Phases);
				WriteBarriers(writer, snapshot.Barriers);
				WriteFileImpacts(writer, snapshot.FileImpacts);
				WritePluginImpacts(writer, snapshot.PluginImpacts);
				WriteConfigurationImpacts(writer, snapshot.ConfigurationImpacts);
				WriteAssociationImpacts(writer, snapshot.AssociationImpacts);
				WritePreparedRecipes(writer, snapshot.PreparedRecipes);
				writer.Flush();
				byte[] payload = stream.ToArray();
				if (payload.Length > MaxPayloadLength) throw new InvalidOperationException("Reviewed workflow payload exceeds the bounded v2 persistence size.");
				return payload;
			}
		}

		/// <summary>Decodes and validates one immutable v2 reviewed-workflow payload.</summary>
		public static CollectionReviewedWorkflowSnapshot Deserialize(byte[] payload)
		{
			if (payload == null) throw new ArgumentNullException(nameof(payload));
			if (payload.Length == 0 || payload.Length > MaxPayloadLength) throw new InvalidDataException("Reviewed workflow payload length is invalid.");
			try
			{
				using (var stream = new MemoryStream(payload, false))
				using (var reader = new BinaryReader(stream, new UTF8Encoding(false), true))
				{
					int binaryVersion = reader.ReadInt32();
					if (binaryVersion != BinaryVersion && binaryVersion != LegacyBinaryVersion) throw new InvalidDataException("Unsupported reviewed workflow binary version.");
					CollectionPlanIdentity identity = ReadPlanIdentity(reader);
					CollectionRevisionIdentity revision = ReadRevision(reader);
					CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint(ReadRequiredString(reader));
					CollectionExecutionPolicyKind policy = ReadEnum<CollectionExecutionPolicyKind>(reader, false);
					CollectionReplacementBackupChoice backup = ReadEnum<CollectionReplacementBackupChoice>(reader, true);
					CollectionCurrentStateFingerprint state = ReadStateFingerprint(reader);
					CollectionManifestSourceSnapshot manifestSource = ReadManifestSource(reader);
					List<CollectionReviewedMemberSnapshot> members = ReadMembers(reader);
					List<CollectionReviewedDependencySnapshot> dependencies = ReadDependencies(reader);
					List<CollectionReviewedPrioritySnapshot> priorities = ReadPriorities(reader);
					List<CollectionReviewedPhaseSnapshot> phases = ReadPhases(reader);
					List<CollectionReviewedBarrierSnapshot> barriers = ReadBarriers(reader);
					List<CollectionReviewedFileImpactSnapshot> files = ReadFileImpacts(reader);
					List<CollectionReviewedPluginImpactSnapshot> plugins = ReadPluginImpacts(reader);
					List<CollectionReviewedConfigurationImpactSnapshot> configs = ReadConfigurationImpacts(reader);
					List<CollectionReviewedAssociationImpactSnapshot> associations = ReadAssociationImpacts(reader);
					List<CollectionReviewedPreparedRecipeSnapshot> recipes = ReadPreparedRecipes(reader, binaryVersion);
					if (stream.Position != stream.Length) throw new InvalidDataException("Reviewed workflow payload contains trailing bytes.");
					return new CollectionReviewedWorkflowSnapshot(identity, revision, target, policy, backup, state, manifestSource,
						members, dependencies, priorities, phases, barriers, files, plugins, configs, associations, recipes);
				}
			}
			catch (EndOfStreamException ex) { throw new InvalidDataException("Reviewed workflow payload is truncated.", ex); }
			catch (ArgumentException ex) { throw new InvalidDataException("Reviewed workflow payload contains invalid immutable data.", ex); }
		}

		private static void WriteMembers(BinaryWriter writer, IEnumerable<CollectionReviewedMemberSnapshot> values)
		{
			List<CollectionReviewedMemberSnapshot> list = values.OrderBy(x => x.MemberKey.Kind).ThenBy(x => x.MemberKey.Value, StringComparer.Ordinal).ToList();
			writer.Write(list.Count);
			foreach (CollectionReviewedMemberSnapshot x in list)
			{
				WriteMemberKey(writer, x.MemberKey); writer.Write(x.SourceOrdinal); writer.Write((int)x.Requirement); writer.Write(x.InstallationPhase);
				writer.Write(x.RecipeFingerprint); writer.Write((int)x.ArtifactChoiceKind); WriteArtifact(writer, x.RequestedArtifact); WriteArtifact(writer, x.SelectedArtifact);
				WriteNullableString(writer, x.SubstitutionRuleId);
			}
		}
		private static List<CollectionReviewedMemberSnapshot> ReadMembers(BinaryReader reader)
		{
			var result = new List<CollectionReviewedMemberSnapshot>();
			for (int i = 0, count = ReadCount(reader); i < count; i++)
				result.Add(new CollectionReviewedMemberSnapshot(ReadMemberKey(reader), reader.ReadInt32(), ReadEnum<CollectionMemberRequirement>(reader, true),
					reader.ReadDouble(), ReadRequiredString(reader), ReadEnum<CollectionResolvedArtifactChoiceKind>(reader, false), ReadArtifact(reader), ReadArtifact(reader), ReadNullableString(reader)));
			return result;
		}
		private static void WriteDependencies(BinaryWriter writer, IEnumerable<CollectionReviewedDependencySnapshot> values)
		{
			List<CollectionReviewedDependencySnapshot> list = values.OrderBy(x => x.Prerequisite.Kind).ThenBy(x => x.Prerequisite.Value, StringComparer.Ordinal)
				.ThenBy(x => x.Dependent.Kind).ThenBy(x => x.Dependent.Value, StringComparer.Ordinal).ThenBy(x => x.Kind).ToList();
			writer.Write(list.Count); foreach (var x in list) { WriteMemberKey(writer, x.Prerequisite); WriteMemberKey(writer, x.Dependent); writer.Write((int)x.Kind); }
		}
		private static List<CollectionReviewedDependencySnapshot> ReadDependencies(BinaryReader reader)
		{
			var result = new List<CollectionReviewedDependencySnapshot>();
			for (int i = 0, count = ReadCount(reader); i < count; i++) result.Add(new CollectionReviewedDependencySnapshot(ReadMemberKey(reader), ReadMemberKey(reader), ReadEnum<CollectionMemberDependencyKind>(reader, false)));
			return result;
		}
		private static void WritePriorities(BinaryWriter writer, IEnumerable<CollectionReviewedPrioritySnapshot> values)
		{
			List<CollectionReviewedPrioritySnapshot> list = values.OrderBy(x => x.Lower.Kind).ThenBy(x => x.Lower.Value, StringComparer.Ordinal)
				.ThenBy(x => x.Higher.Kind).ThenBy(x => x.Higher.Value, StringComparer.Ordinal).ToList();
			writer.Write(list.Count); foreach (var x in list) { WriteMemberKey(writer, x.Lower); WriteMemberKey(writer, x.Higher); }
		}
		private static List<CollectionReviewedPrioritySnapshot> ReadPriorities(BinaryReader reader)
		{
			var result = new List<CollectionReviewedPrioritySnapshot>();
			for (int i = 0, count = ReadCount(reader); i < count; i++) result.Add(new CollectionReviewedPrioritySnapshot(ReadMemberKey(reader), ReadMemberKey(reader)));
			return result;
		}
		private static void WritePhases(BinaryWriter writer, IEnumerable<CollectionReviewedPhaseSnapshot> values)
		{
			List<CollectionReviewedPhaseSnapshot> list = values.OrderBy(x => x.PhaseNumber).ToList(); writer.Write(list.Count);
			foreach (var phase in list)
			{
				writer.Write(phase.PhaseNumber); writer.Write(phase.Members.Count);
				foreach (var x in phase.Members)
				{
					WriteMemberKey(writer, x.MemberKey); writer.Write((int)x.Disposition); writer.Write((int)x.Reason);
					writer.Write(x.NativeCandidates.Count); foreach (var c in x.NativeCandidates) { writer.Write(c.TargetFingerprint); writer.Write(c.NativeModKey); writer.Write(c.NexusModId); writer.Write(c.NexusFileId); writer.Write((int)c.InstallRoot); writer.Write((int)c.InstallMethod); }
					writer.Write(x.Bindings.Count); foreach (var b in x.Bindings) { writer.Write(b.AssociationId.ToByteArray()); writer.Write((int)b.AssociationState); WriteMemberKey(writer, b.MemberKey); writer.Write(b.TargetFingerprint); writer.Write(b.NativeModKey); writer.Write(b.VerifiedRecipeFingerprint); writer.Write((int)b.BindingKind); }
					writer.Write(x.VerifiedArchive != null); if (x.VerifiedArchive != null) { writer.Write(x.VerifiedArchive.ArtifactId); WriteHash(writer, x.VerifiedArchive.ContentHash); writer.Write(x.VerifiedArchive.ByteLength); writer.Write((int)x.VerifiedArchive.SourceKind); writer.Write((int)x.VerifiedArchive.VerificationBasis); }
				}
			}
		}
		private static List<CollectionReviewedPhaseSnapshot> ReadPhases(BinaryReader reader)
		{
			var phases = new List<CollectionReviewedPhaseSnapshot>();
			for (int p = 0, pc = ReadCount(reader); p < pc; p++)
			{
				double phaseNumber = reader.ReadDouble(); var members = new List<CollectionReviewedPhaseMemberSnapshot>();
				for (int m = 0, mc = ReadCount(reader); m < mc; m++)
				{
					CollectionMemberKey key = ReadMemberKey(reader); var disposition = ReadEnum<CollectionMemberMatchDisposition>(reader, false); var reason = ReadEnum<CollectionMemberMatchReason>(reader, false);
					var candidates = new List<CollectionReviewedNativeCandidateSnapshot>();
					for (int i = 0, count = ReadCount(reader); i < count; i++) candidates.Add(new CollectionReviewedNativeCandidateSnapshot(ReadRequiredString(reader), ReadRequiredString(reader), reader.ReadString(), reader.ReadString(), ReadEnum<ModInstallRoot>(reader, true), ReadEnum<ModInstallMethod>(reader, true)));
					var bindings = new List<CollectionReviewedBindingSnapshot>();
					for (int i = 0, count = ReadCount(reader); i < count; i++) bindings.Add(new CollectionReviewedBindingSnapshot(ReadGuid(reader), ReadEnum<CollectionAssociationState>(reader, false), ReadMemberKey(reader), ReadRequiredString(reader), ReadRequiredString(reader), ReadRequiredString(reader), ReadEnum<CollectionMemberBindingKind>(reader, false)));
					CollectionReviewedVerifiedArchiveSnapshot archive = null;
					if (reader.ReadBoolean()) archive = new CollectionReviewedVerifiedArchiveSnapshot(ReadRequiredString(reader), ReadHash(reader), reader.ReadInt64(), ReadEnum<CollectionVerifiedArchiveSourceKind>(reader, false), ReadEnum<CollectionArchiveVerificationBasis>(reader, false));
					members.Add(new CollectionReviewedPhaseMemberSnapshot(key, disposition, reason, candidates, bindings, archive));
				}
				phases.Add(new CollectionReviewedPhaseSnapshot(phaseNumber, members));
			}
			return phases;
		}
		private static void WriteBarriers(BinaryWriter writer, IEnumerable<CollectionReviewedBarrierSnapshot> values)
		{
			List<CollectionReviewedBarrierSnapshot> list = values.OrderBy(x => x.CompletedPhase).ThenBy(x => x.NextPhase).ToList(); writer.Write(list.Count); foreach (var x in list) { writer.Write(x.CompletedPhase); writer.Write(x.NextPhase); writer.Write((int)x.Kind); }
		}
		private static List<CollectionReviewedBarrierSnapshot> ReadBarriers(BinaryReader reader)
		{
			var result = new List<CollectionReviewedBarrierSnapshot>(); for (int i = 0, count = ReadCount(reader); i < count; i++) result.Add(new CollectionReviewedBarrierSnapshot(reader.ReadDouble(), reader.ReadDouble(), ReadEnum<CollectionPhaseBarrierKind>(reader, false))); return result;
		}
		private static void WriteFileImpacts(BinaryWriter writer, IEnumerable<CollectionReviewedFileImpactSnapshot> values)
		{
			List<CollectionReviewedFileImpactSnapshot> list = values.OrderBy(x => x.Target.Root).ThenBy(x => x.Target.RelativePath, StringComparer.OrdinalIgnoreCase).ToList(); writer.Write(list.Count);
			foreach (var x in list) { WriteDeploymentTarget(writer, x.Target); writer.Write(x.Writers.Count); foreach (var key in x.Writers) WriteMemberKey(writer, key); WriteNullableMemberKey(writer, x.PlannedWinner); WriteNullableString(writer, x.CurrentOwnerKey); WriteGuids(writer, x.AffectedAssociationIds); }
		}
		private static List<CollectionReviewedFileImpactSnapshot> ReadFileImpacts(BinaryReader reader)
		{
			var result = new List<CollectionReviewedFileImpactSnapshot>();
			for (int i = 0, count = ReadCount(reader); i < count; i++) { ModDeploymentTarget target = ReadDeploymentTarget(reader); var writers = new List<CollectionMemberKey>(); for (int j = 0, wc = ReadCount(reader); j < wc; j++) writers.Add(ReadMemberKey(reader)); result.Add(new CollectionReviewedFileImpactSnapshot(target, writers, ReadNullableMemberKey(reader), ReadNullableString(reader), ReadGuids(reader))); }
			return result;
		}
		private static void WritePluginImpacts(BinaryWriter writer, IEnumerable<CollectionReviewedPluginImpactSnapshot> values)
		{
			List<CollectionReviewedPluginImpactSnapshot> list = values.OrderBy(x => x.MemberKey.Kind).ThenBy(x => x.MemberKey.Value, StringComparer.Ordinal).ThenBy(x => x.Effect.Kind).ToList(); writer.Write(list.Count); foreach (var x in list) { WriteMemberKey(writer, x.MemberKey); WritePluginEffect(writer, x.Effect); WriteGuids(writer, x.AffectedAssociationIds); }
		}
		private static List<CollectionReviewedPluginImpactSnapshot> ReadPluginImpacts(BinaryReader reader)
		{
			var result = new List<CollectionReviewedPluginImpactSnapshot>(); for (int i = 0, count = ReadCount(reader); i < count; i++) result.Add(new CollectionReviewedPluginImpactSnapshot(ReadMemberKey(reader), ReadPluginEffect(reader), ReadGuids(reader))); return result;
		}
		private static void WriteConfigurationImpacts(BinaryWriter writer, IEnumerable<CollectionReviewedConfigurationImpactSnapshot> values)
		{
			List<CollectionReviewedConfigurationImpactSnapshot> list = values.OrderBy(x => x.Kind).ThenBy(x => x.SubjectKey, StringComparer.Ordinal).ThenBy(x => x.MemberKey.Kind).ThenBy(x => x.MemberKey.Value, StringComparer.Ordinal).ToList(); writer.Write(list.Count); foreach (var x in list) { WriteMemberKey(writer, x.MemberKey); writer.Write((int)x.Kind); writer.Write(x.SubjectKey); WriteNullableString(writer, x.CurrentOwnerKey); writer.Write(x.ChangesRecordedValue); WriteGuids(writer, x.AffectedAssociationIds); }
		}
		private static List<CollectionReviewedConfigurationImpactSnapshot> ReadConfigurationImpacts(BinaryReader reader)
		{
			var result = new List<CollectionReviewedConfigurationImpactSnapshot>(); for (int i = 0, count = ReadCount(reader); i < count; i++) result.Add(new CollectionReviewedConfigurationImpactSnapshot(ReadMemberKey(reader), ReadEnum<CollectionConfigurationImpactKind>(reader, false), reader.ReadString(), ReadNullableString(reader), reader.ReadBoolean(), ReadGuids(reader))); return result;
		}
		private static void WriteAssociationImpacts(BinaryWriter writer, IEnumerable<CollectionReviewedAssociationImpactSnapshot> values)
		{
			List<CollectionReviewedAssociationImpactSnapshot> list = values.OrderBy(x => x.AssociationId).ToList(); writer.Write(list.Count); foreach (var x in list) { writer.Write(x.AssociationId.ToByteArray()); writer.Write((int)x.Kind); }
		}
		private static List<CollectionReviewedAssociationImpactSnapshot> ReadAssociationImpacts(BinaryReader reader)
		{
			var result = new List<CollectionReviewedAssociationImpactSnapshot>(); for (int i = 0, count = ReadCount(reader); i < count; i++) result.Add(new CollectionReviewedAssociationImpactSnapshot(ReadGuid(reader), ReadAssociationImpactKind(reader))); return result;
		}
		private static void WritePreparedRecipes(BinaryWriter writer, IEnumerable<CollectionReviewedPreparedRecipeSnapshot> values)
		{
			List<CollectionReviewedPreparedRecipeSnapshot> list = values.OrderBy(x => x.MemberKey.Kind).ThenBy(x => x.MemberKey.Value, StringComparer.Ordinal).ToList(); writer.Write(list.Count);
			foreach (var x in list)
			{
				WriteMemberKey(writer, x.MemberKey); writer.Write(x.SourceOrdinal); writer.Write(x.ProviderRecipeFingerprint); writer.Write(x.PreparedNativeFingerprint); writer.Write(x.SkipReadmeFiles);
				WriteValidation(writer, x.Validation); WriteEffectPreview(writer, x.EffectPreview); writer.Write(x.RetainedArtifactIds.Count); foreach (string id in x.RetainedArtifactIds.OrderBy(y => y, StringComparer.Ordinal)) writer.Write(id);
				writer.Write(x.SimpleFileMappings.Count);
				foreach (CollectionReviewedSimpleFileMappingSnapshot mapping in x.SimpleFileMappings) { writer.Write(mapping.SourcePath); writer.Write(mapping.DestinationPath); }
			}
		}
		private static List<CollectionReviewedPreparedRecipeSnapshot> ReadPreparedRecipes(BinaryReader reader, int binaryVersion)
		{
			var result = new List<CollectionReviewedPreparedRecipeSnapshot>();
			for (int i = 0, count = ReadCount(reader); i < count; i++)
			{
				CollectionMemberKey key = ReadMemberKey(reader); int ordinal = reader.ReadInt32(); string provider = ReadRequiredString(reader); string prepared = ReadRequiredString(reader); bool skip = reader.ReadBoolean();
				ModInstallationRecipeValidation validation = ReadValidation(reader); CollectionMemberEffectPreview preview = ReadEffectPreview(reader); var ids = new List<string>(); for (int j = 0, c = ReadCount(reader); j < c; j++) ids.Add(ReadRequiredString(reader));
				var mappings = new List<CollectionReviewedSimpleFileMappingSnapshot>();
				if (binaryVersion >= 3)
					for (int j = 0, c = ReadCount(reader); j < c; j++) mappings.Add(new CollectionReviewedSimpleFileMappingSnapshot(ReadRequiredString(reader), ReadRequiredString(reader)));
				result.Add(new CollectionReviewedPreparedRecipeSnapshot(key, ordinal, provider, prepared, skip, validation, preview, ids, mappings));
			}
			return result;
		}

		private static void WriteValidation(BinaryWriter writer, ModInstallationRecipeValidation validation)
		{
			writer.Write(validation.AdapterId); writer.Write(validation.AdapterVersion); writer.Write((int)validation.InstallContext.Method); writer.Write((int)validation.InstallContext.InstallRoot); writer.Write(validation.ExpectedContent.Sha256); writer.Write(validation.ExpectedContent.ByteLength);
			writer.Write(validation.Capabilities.Count); foreach (var c in validation.Capabilities.OrderBy(x => x.CapabilityId, StringComparer.Ordinal)) { writer.Write(c.CapabilityId); writer.Write(c.Version); }
			writer.Write(validation.Paths.Count); foreach (var p in validation.Paths) { writer.Write((int)p.Kind); writer.Write(p.Path); }
		}
		private static ModInstallationRecipeValidation ReadValidation(BinaryReader reader)
		{
			string adapter = ReadRequiredString(reader); int adapterVersion = reader.ReadInt32(); var context = new ModInstallContext(ReadEnum<ModInstallMethod>(reader, true), ReadEnum<ModInstallRoot>(reader, true)); var expected = new ModInstallationRecipeExpectedContent(ReadRequiredString(reader), reader.ReadInt64());
			var capabilities = new List<ModInstallationRecipeCapability>(); for (int i = 0, count = ReadCount(reader); i < count; i++) capabilities.Add(new ModInstallationRecipeCapability(ReadRequiredString(reader), reader.ReadInt32()));
			var paths = new List<ModInstallationRecipePath>(); for (int i = 0, count = ReadCount(reader); i < count; i++) paths.Add(new ModInstallationRecipePath(ReadEnum<ModInstallationRecipePathKind>(reader, false), ReadRequiredString(reader)));
			return new ModInstallationRecipeValidation(adapter, adapterVersion, context, expected, capabilities, paths);
		}
		private static void WriteEffectPreview(BinaryWriter writer, CollectionMemberEffectPreview preview)
		{
			WriteMemberKey(writer, preview.MemberKey); writer.Write(preview.RecipeIdentity.Fingerprint); writer.Write((int)preview.InstallMethod); writer.Write((int)preview.InstallRoot);
			writer.Write(preview.Files.Count); foreach (var f in preview.Files) WriteDeploymentTarget(writer, f.Target);
			writer.Write(preview.IniEdits.Count); foreach (var x in preview.IniEdits) { writer.Write(x.Key.File); writer.Write(x.Key.Section); writer.Write(x.Key.Key); WriteNullableString(writer, x.Value); }
			writer.Write(preview.GameValues.Count); foreach (var x in preview.GameValues) { writer.Write(x.Key); byte[] value = x.Value; writer.Write(value != null); if (value != null) { writer.Write(value.Length); writer.Write(value); } }
			writer.Write(preview.PluginEffects.Count); foreach (var x in preview.PluginEffects) WritePluginEffect(writer, x);
			writer.Write(preview.Issues.Count); foreach (var x in preview.Issues) { writer.Write((int)x.Kind); writer.Write(x.OperationType); writer.Write(x.Message); }
		}
		private static CollectionMemberEffectPreview ReadEffectPreview(BinaryReader reader)
		{
			CollectionMemberKey key = ReadMemberKey(reader); CollectionRecipeIdentity recipe = CollectionRecipeIdentity.FromFingerprint(ReadRequiredString(reader)); ModInstallMethod method = ReadEnum<ModInstallMethod>(reader, true); ModInstallRoot root = ReadEnum<ModInstallRoot>(reader, true);
			var files = new List<CollectionPlannedFileEffect>(); for (int i = 0, count = ReadCount(reader); i < count; i++) files.Add(new CollectionPlannedFileEffect(ReadDeploymentTarget(reader)));
			var ini = new List<CollectionPlannedIniEffect>(); for (int i = 0, count = ReadCount(reader); i < count; i++) ini.Add(new CollectionPlannedIniEffect(new CollectionNativeIniKey(reader.ReadString(), reader.ReadString(), reader.ReadString()), ReadNullableString(reader)));
			var game = new List<CollectionPlannedGameValueEffect>(); for (int i = 0, count = ReadCount(reader); i < count; i++) { string k = ReadRequiredString(reader); byte[] value = null; if (reader.ReadBoolean()) { int length = ReadCount(reader); value = reader.ReadBytes(length); if (value.Length != length) throw new EndOfStreamException(); } game.Add(new CollectionPlannedGameValueEffect(k, value)); }
			var plugins = new List<CollectionPlannedPluginEffect>(); for (int i = 0, count = ReadCount(reader); i < count; i++) plugins.Add(ReadPluginEffect(reader));
			var issues = new List<CollectionEffectPreviewIssue>(); for (int i = 0, count = ReadCount(reader); i < count; i++) issues.Add(new CollectionEffectPreviewIssue(ReadEnum<CollectionEffectPreviewIssueKind>(reader, false), reader.ReadString(), reader.ReadString()));
			return new CollectionMemberEffectPreview(key, recipe, method, root, files, ini, game, plugins, issues);
		}
		private static void WritePluginEffect(BinaryWriter writer, CollectionPlannedPluginEffect effect)
		{
			writer.Write((int)effect.Kind); writer.Write(effect.PluginPaths.Count); foreach (string path in effect.PluginPaths) writer.Write(path); writer.Write(effect.Active.HasValue); if (effect.Active.HasValue) writer.Write(effect.Active.Value); writer.Write(effect.AbsoluteIndex.HasValue); if (effect.AbsoluteIndex.HasValue) writer.Write(effect.AbsoluteIndex.Value);
		}
		private static CollectionPlannedPluginEffect ReadPluginEffect(BinaryReader reader)
		{
			CollectionPlannedPluginEffectKind kind = ReadEnum<CollectionPlannedPluginEffectKind>(reader, false); var paths = new List<string>(); for (int i = 0, count = ReadCount(reader); i < count; i++) paths.Add(ReadRequiredString(reader)); bool? active = reader.ReadBoolean() ? (bool?)reader.ReadBoolean() : null; int? index = reader.ReadBoolean() ? (int?)reader.ReadInt32() : null;
			switch (kind) { case CollectionPlannedPluginEffectKind.Activation: if (paths.Count != 1 || !active.HasValue) throw new InvalidDataException("Invalid activation effect descriptor."); return CollectionPlannedPluginEffect.Activation(paths[0], active.Value); case CollectionPlannedPluginEffectKind.AbsoluteOrderIndex: if (paths.Count != 1 || !index.HasValue) throw new InvalidDataException("Invalid absolute-order effect descriptor."); return CollectionPlannedPluginEffect.AbsoluteOrder(paths[0], index.Value); case CollectionPlannedPluginEffectKind.RelativeOrder: return CollectionPlannedPluginEffect.RelativeOrder(paths); default: throw new InvalidDataException("Invalid plugin effect kind."); }
		}
		private static void WritePlanIdentity(BinaryWriter writer, CollectionPlanIdentity identity) { writer.Write(identity.PlanId.ToByteArray()); writer.Write(identity.Version); }
		private static CollectionPlanIdentity ReadPlanIdentity(BinaryReader reader) { return CollectionPlanIdentity.From(ReadGuid(reader), reader.ReadInt32()); }
		private static void WriteRevision(BinaryWriter writer, CollectionRevisionIdentity revision) { writer.Write((int)revision.Collection.Origin); writer.Write(revision.Collection.StableId); writer.Write(revision.StableRevisionId); writer.Write(revision.NexusRevisionNumber.HasValue); if (revision.NexusRevisionNumber.HasValue) writer.Write(revision.NexusRevisionNumber.Value); }
		private static CollectionRevisionIdentity ReadRevision(BinaryReader reader)
		{
			CollectionOrigin origin = ReadEnum<CollectionOrigin>(reader, false); string collectionId = ReadRequiredString(reader); string revisionId = ReadRequiredString(reader); bool hasNumber = reader.ReadBoolean(); long number = hasNumber ? reader.ReadInt64() : 0;
			if (origin == CollectionOrigin.NexusMods) { if (!hasNumber) throw new InvalidDataException("Nexus revision number is missing."); return CollectionRevisionIdentity.FromNexus(CollectionIdentity.FromNexus(collectionId), revisionId, number); }
			if (origin == CollectionOrigin.Local) { Guid c; Guid r; if (hasNumber || !Guid.TryParse(collectionId, out c) || !Guid.TryParse(revisionId, out r)) throw new InvalidDataException("Local revision identity is invalid."); return CollectionRevisionIdentity.FromLocal(CollectionIdentity.FromLocal(c), r); }
			throw new InvalidDataException("Unsupported Collection origin.");
		}
		private static void WriteStateFingerprint(BinaryWriter writer, CollectionCurrentStateFingerprint value) { writer.Write(value.FormatVersion); writer.Write(value.Value); }
		private static CollectionCurrentStateFingerprint ReadStateFingerprint(BinaryReader reader) { return new CollectionCurrentStateFingerprint(ReadRequiredString(reader), ReadRequiredString(reader)); }
		private static void WriteManifestSource(BinaryWriter writer, CollectionManifestSourceSnapshot source) { WriteHash(writer, source.ContentHash); writer.Write(source.ByteLength); writer.Write(source.SchemaIdentity); writer.Write(source.NormalizerVersion); }
		private static CollectionManifestSourceSnapshot ReadManifestSource(BinaryReader reader) { return new CollectionManifestSourceSnapshot(ReadHash(reader), reader.ReadInt64(), ReadRequiredString(reader), ReadRequiredString(reader)); }
		private static void WriteHash(BinaryWriter writer, CollectionContentHash hash) { writer.Write((int)hash.Algorithm); writer.Write(hash.Value); }
		private static CollectionContentHash ReadHash(BinaryReader reader) { CollectionContentHashAlgorithm algorithm = ReadEnum<CollectionContentHashAlgorithm>(reader, false); if (algorithm != CollectionContentHashAlgorithm.Sha256) throw new InvalidDataException("Unsupported retained-content digest algorithm."); return CollectionContentHash.FromSha256(ReadRequiredString(reader)); }
		private static void WriteMemberKey(BinaryWriter writer, CollectionMemberKey key) { writer.Write((int)key.Kind); writer.Write(key.Value); }
		private static CollectionMemberKey ReadMemberKey(BinaryReader reader)
		{
			CollectionMemberKeyKind kind = ReadEnum<CollectionMemberKeyKind>(reader, false); string value = ReadRequiredString(reader); if (kind == CollectionMemberKeyKind.ProviderStable) return CollectionMemberKey.FromProvider(value); if (kind == CollectionMemberKeyKind.ValidatedMatch) return CollectionMemberKey.FromValidatedMatch(value); if (kind == CollectionMemberKeyKind.Local) { Guid id; if (!Guid.TryParse(value, out id)) throw new InvalidDataException("Local member id is invalid."); return CollectionMemberKey.FromLocal(id); } throw new InvalidDataException("Unsupported member key kind.");
		}
		private static void WriteNullableMemberKey(BinaryWriter writer, CollectionMemberKey key) { writer.Write(key != null); if (key != null) WriteMemberKey(writer, key); }
		private static CollectionMemberKey ReadNullableMemberKey(BinaryReader reader) { return reader.ReadBoolean() ? ReadMemberKey(reader) : null; }
		private static void WriteArtifact(BinaryWriter writer, CollectionArtifactReference artifact) { writer.Write(artifact.Scheme); writer.Write(artifact.StableId); writer.Write(artifact.ExpectedContentHash != null); if (artifact.ExpectedContentHash != null) WriteHash(writer, artifact.ExpectedContentHash); }
		private static CollectionArtifactReference ReadArtifact(BinaryReader reader) { string scheme = ReadRequiredString(reader); string id = ReadRequiredString(reader); return new CollectionArtifactReference(scheme, id, reader.ReadBoolean() ? ReadHash(reader) : null); }
		private static void WriteDeploymentTarget(BinaryWriter writer, ModDeploymentTarget target) { writer.Write((int)target.Root); writer.Write(target.RelativePath); }
		private static ModDeploymentTarget ReadDeploymentTarget(BinaryReader reader) { return ModDeploymentTargetResolver.FromCanonical(ReadEnum<ModDeploymentRoot>(reader, true), ReadRequiredString(reader)); }
		private static void WriteNullableString(BinaryWriter writer, string value) { writer.Write(value != null); if (value != null) writer.Write(value); }
		private static string ReadNullableString(BinaryReader reader) { return reader.ReadBoolean() ? reader.ReadString() : null; }
		private static void WriteGuids(BinaryWriter writer, IEnumerable<Guid> values) { List<Guid> list = values.OrderBy(x => x).ToList(); writer.Write(list.Count); foreach (Guid value in list) writer.Write(value.ToByteArray()); }
		private static List<Guid> ReadGuids(BinaryReader reader) { var result = new List<Guid>(); for (int i = 0, count = ReadCount(reader); i < count; i++) result.Add(ReadGuid(reader)); return result; }
		private static Guid ReadGuid(BinaryReader reader) { byte[] bytes = reader.ReadBytes(16); if (bytes.Length != 16) throw new EndOfStreamException(); Guid value = new Guid(bytes); if (value == Guid.Empty) throw new InvalidDataException("Persisted GUID cannot be empty."); return value; }
		private static int ReadCount(BinaryReader reader) { int count = reader.ReadInt32(); if (count < 0 || count > MaxCount) throw new InvalidDataException("Persisted collection count is outside the supported bound."); return count; }
		private static string ReadRequiredString(BinaryReader reader) { string value = reader.ReadString(); if (String.IsNullOrWhiteSpace(value)) throw new InvalidDataException("Persisted required string is empty."); return value; }

		private static CollectionAssociationImpactKind ReadAssociationImpactKind(BinaryReader reader)
		{
			int raw = reader.ReadInt32();
			int known = (int)(CollectionAssociationImpactKind.SharedNativeInstance | CollectionAssociationImpactKind.FileWinner |
				CollectionAssociationImpactKind.PluginState | CollectionAssociationImpactKind.ConfigurationState |
				CollectionAssociationImpactKind.UserOverride);
			if (raw < 0 || (raw & ~known) != 0)
				throw new InvalidDataException("Persisted association-impact flags contain unsupported bits.");
			return (CollectionAssociationImpactKind)raw;
		}
		private static T ReadEnum<T>(BinaryReader reader, bool allowZero) where T : struct { int raw = reader.ReadInt32(); T value = (T)Enum.ToObject(typeof(T), raw); if (!Enum.IsDefined(typeof(T), value) || (!allowZero && raw == 0)) throw new InvalidDataException("Persisted enum value is invalid for " + typeof(T).Name + "."); return value; }
	}

	/// <summary>Result classification for fail-closed reviewed-workflow rehydration.</summary>
	public enum CollectionReviewedWorkflowRehydrationStatus
	{
		Ready = 1,
		RepreparationRequired = 2,
		RecoveryRequired = 3,
		CurrentStateChanged = 4,
		RetainedInputInvalid = 5
	}

	/// <summary>Read-only output of validating one persisted reviewed workflow for safe continuation.</summary>
	public sealed class CollectionReviewedWorkflowRehydrationResult
	{
		private readonly ReadOnlyCollection<CollectionMemberKey> _remainingMembers;
		internal CollectionReviewedWorkflowRehydrationResult(CollectionReviewedWorkflowRehydrationStatus status,
			CollectionReviewedWorkflowSnapshot snapshot, CollectionNativeStateIndex currentState,
			IEnumerable<CollectionMemberKey> remainingMembers, string message)
		{
			Status = status; Snapshot = snapshot; CurrentState = currentState; Message = message ?? String.Empty;
			_remainingMembers = new ReadOnlyCollection<CollectionMemberKey>((remainingMembers ?? Enumerable.Empty<CollectionMemberKey>()).ToList());
		}
		public CollectionReviewedWorkflowRehydrationStatus Status { get; }
		public CollectionReviewedWorkflowSnapshot Snapshot { get; }
		public CollectionNativeStateIndex CurrentState { get; }
		public ReadOnlyCollection<CollectionMemberKey> RemainingMembers { get { return _remainingMembers; } }
		public string Message { get; }
		public bool CanResume { get { return Status == CollectionReviewedWorkflowRehydrationStatus.Ready; } }
	}

	/// <summary>
	/// Loads reviewed workflow v2, verifies immutable retained inputs and recaptures native state before any new mutation.
	/// </summary>
	/// <remarks>
	/// Application-level startup ordering remains C6.15.12: native recovery and C6.9 reconciliation run before this service is
	/// allowed to return resumable work. Ambiguous crossed-native-boundary children are therefore returned as RecoveryRequired.
	/// </remarks>
	public sealed class CollectionReviewedWorkflowRehydrator
	{
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsResolvedPlanStore _planStore;
		private readonly Func<CollectionRevisionIdentity, CollectionRevisionSourceRecord> _sourceRecordLoader;
		private readonly Func<CollectionRevisionIdentity, CollectionManifestSourceSnapshot, byte[]> _manifestLoader;
		private readonly Func<string, CollectionsRetainedArtifact> _artifactLoader;
		private readonly Func<CollectionTargetIdentity, CollectionNativeStateIndex> _stateCapture;
		private readonly Func<CollectionOperation, CollectionNativeChildOperation, CollectionNativeChildRecoveryManifest> _recoveryManifestLoader;

		/// <summary>Creates a rehydrator over the durable Collections stores and the authoritative C6.1 native-state reader.</summary>
		public CollectionReviewedWorkflowRehydrator(CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore,
			CollectionsRevisionSourceStore revisionSourceStore, CollectionsRetainedArtifactStore artifactStore,
			CollectionNativeStateReader nativeStateReader)
			: this(operationStore, planStore, revisionSourceStore, artifactStore, nativeStateReader, null)
		{
		}

		/// <summary>Creates a rehydrator that can continue across already reconciled committed native children.</summary>
		public CollectionReviewedWorkflowRehydrator(CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore,
			CollectionsRevisionSourceStore revisionSourceStore, CollectionsRetainedArtifactStore artifactStore,
			CollectionNativeStateReader nativeStateReader, CollectionsNativeChildRecoveryManifestStore manifestStore)
			: this(operationStore, planStore,
				revisionSourceStore == null ? null : new Func<CollectionRevisionIdentity, CollectionRevisionSourceRecord>(revisionSourceStore.GetSource),
				revisionSourceStore == null ? null : new Func<CollectionRevisionIdentity, CollectionManifestSourceSnapshot, byte[]>(revisionSourceStore.LoadManifest),
				artifactStore == null ? null : new Func<string, CollectionsRetainedArtifact>(artifactId => LoadVerifiedArtifact(artifactStore, artifactId)),
				nativeStateReader == null ? null : new Func<CollectionTargetIdentity, CollectionNativeStateIndex>(nativeStateReader.Capture),
				manifestStore == null ? null : new Func<CollectionOperation, CollectionNativeChildOperation, CollectionNativeChildRecoveryManifest>(manifestStore.GetManifest))
		{
		}

		/// <summary>Creates a read-only rehydrator over explicit durable-input and native-state loaders.</summary>
		public CollectionReviewedWorkflowRehydrator(CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore,
			Func<CollectionRevisionIdentity, CollectionRevisionSourceRecord> sourceRecordLoader,
			Func<CollectionRevisionIdentity, CollectionManifestSourceSnapshot, byte[]> manifestLoader,
			Func<string, CollectionsRetainedArtifact> artifactLoader,
			Func<CollectionTargetIdentity, CollectionNativeStateIndex> stateCapture)
			: this(operationStore, planStore, sourceRecordLoader, manifestLoader, artifactLoader, stateCapture, null)
		{
		}

		/// <summary>Creates a read-only rehydrator with an explicit committed-child safe-boundary manifest loader.</summary>
		public CollectionReviewedWorkflowRehydrator(CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore,
			Func<CollectionRevisionIdentity, CollectionRevisionSourceRecord> sourceRecordLoader,
			Func<CollectionRevisionIdentity, CollectionManifestSourceSnapshot, byte[]> manifestLoader,
			Func<string, CollectionsRetainedArtifact> artifactLoader,
			Func<CollectionTargetIdentity, CollectionNativeStateIndex> stateCapture,
			Func<CollectionOperation, CollectionNativeChildOperation, CollectionNativeChildRecoveryManifest> recoveryManifestLoader)
		{
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_planStore = planStore ?? throw new ArgumentNullException(nameof(planStore));
			_sourceRecordLoader = sourceRecordLoader ?? throw new ArgumentNullException(nameof(sourceRecordLoader));
			_manifestLoader = manifestLoader ?? throw new ArgumentNullException(nameof(manifestLoader));
			_artifactLoader = artifactLoader ?? throw new ArgumentNullException(nameof(artifactLoader));
			_stateCapture = stateCapture ?? throw new ArgumentNullException(nameof(stateCapture));
			_recoveryManifestLoader = recoveryManifestLoader;
		}

		/// <summary>Loads and validates one exact reviewed operation without performing native or Collection mutation.</summary>
		public CollectionReviewedWorkflowRehydrationResult Rehydrate(CollectionOperationIdentity operationIdentity)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			CollectionOperation operation = _operationStore.GetOperation(operationIdentity);
			if (operation == null || operation.PlanIdentity == null)
				return Result(CollectionReviewedWorkflowRehydrationStatus.RepreparationRequired, null, null, null, "The operation has no persisted reviewed plan identity.");
			if (operation.HasUnreconciledNativeChild || operation.HasUnknownNativeDurability || operation.RequiresRecovery)
				return Result(CollectionReviewedWorkflowRehydrationStatus.RecoveryRequired, null, null, null, "Native recovery/C6.9 reconciliation must complete before reviewed work can be rehydrated.");
			if (!IsSafeLoadPhase(operation.Phase))
				return Result(CollectionReviewedWorkflowRehydrationStatus.RepreparationRequired, null, null, null, "The operation is not at a reviewed or verified safe continuation boundary.");

			CollectionResolvedPlanRecord record = _planStore.GetPlan(operation.PlanIdentity);
			if (record == null)
				return Result(CollectionReviewedWorkflowRehydrationStatus.RepreparationRequired, null, null, null, "The exact reviewed-plan payload is missing.");
			if (StringComparer.Ordinal.Equals(record.PayloadFormat, CollectionReviewedWorkflowSnapshotCodec.LegacyPayloadFormat))
				return Result(CollectionReviewedWorkflowRehydrationStatus.RepreparationRequired, null, null, null, "Legacy reviewed-plan v1 is write-only and must be re-prepared; it is never silently interpreted as v2.");
			if (!StringComparer.Ordinal.Equals(record.PayloadFormat, CollectionReviewedWorkflowSnapshotCodec.PayloadFormat))
				return Result(CollectionReviewedWorkflowRehydrationStatus.RepreparationRequired, null, null, null, "The persisted reviewed-plan payload format is unsupported.");

			CollectionReviewedWorkflowSnapshot snapshot;
			try { snapshot = CollectionReviewedWorkflowSnapshotCodec.Deserialize(record.Payload); }
			catch (InvalidDataException ex) { return Result(CollectionReviewedWorkflowRehydrationStatus.RetainedInputInvalid, null, null, null, ex.Message); }
			if (!MatchesRecord(snapshot, record, operation))
				return Result(CollectionReviewedWorkflowRehydrationStatus.RetainedInputInvalid, snapshot, null, null, "Persisted reviewed-workflow metadata does not match the immutable resolved-plan/operation record.");
			if (!StringComparer.Ordinal.Equals(snapshot.ManifestSource.SchemaIdentity, NexusCollectionManifestNormalizer.SchemaIdentity) ||
				!StringComparer.Ordinal.Equals(snapshot.ManifestSource.NormalizerVersion, NexusCollectionManifestNormalizer.NormalizerVersion))
				return Result(CollectionReviewedWorkflowRehydrationStatus.RepreparationRequired, snapshot, null, null, "The Collection manifest schema/normalizer contract changed since review.");

			try
			{
				CollectionRevisionSourceRecord sourceRecord = _sourceRecordLoader(snapshot.Revision);
				if (sourceRecord == null || String.IsNullOrWhiteSpace(sourceRecord.RawManifestArtifactId) || !sourceRecord.ManifestSource.Equals(snapshot.ManifestSource))
					throw new InvalidDataException("The exact retained Collection revision source binding is missing or changed.");
				_manifestLoader(snapshot.Revision, snapshot.ManifestSource);
				_artifactLoader(sourceRecord.RawManifestArtifactId);
				ValidatePreparedDescriptors(snapshot);
			}
			catch (NotSupportedException ex)
			{
				return Result(CollectionReviewedWorkflowRehydrationStatus.RepreparationRequired, snapshot, null, null, ex.Message);
			}
			catch (Exception ex) when (ex is InvalidDataException || ex is InvalidOperationException || ex is FileNotFoundException)
			{
				return Result(CollectionReviewedWorkflowRehydrationStatus.RetainedInputInvalid, snapshot, null, null, ex.Message);
			}

			var remaining = new HashSet<CollectionMemberKey>(snapshot.Members.Select(x => x.MemberKey));
			CollectionCurrentStateFingerprint expectedStateFingerprint = snapshot.ApprovedStateFingerprint;
			foreach (CollectionNativeChildOperation child in operation.NativeChildren.OrderBy(x => x.Sequence))
			{
				if (!child.Member.Revision.Equals(snapshot.Revision) || !remaining.Contains(child.Member.MemberKey))
					return Result(CollectionReviewedWorkflowRehydrationStatus.RetainedInputInvalid, snapshot, null, null, "A persisted native child does not belong to the reviewed workflow closure.");

				// Persisted intent/recovery inputs are still before the native mutation boundary. They remain reviewed
				// pending work and must not be routed through C6.9 merely because they are not reconciled yet.
				if (!child.HasCrossedNativeBoundary)
					continue;

				if (!child.IsReconciled)
					return Result(CollectionReviewedWorkflowRehydrationStatus.RecoveryRequired, snapshot, null, null, "A submitted native child has not been reconciled through C6.9/C6.10.");
				if (child.NativeResult == null || child.NativeResult.Durability != ModOperationDurability.VerifiedCommitted)
					return Result(CollectionReviewedWorkflowRehydrationStatus.RepreparationRequired, snapshot, null, null, "A reconciled child is not a verified committed result; remaining work must be re-prepared.");
				if (_recoveryManifestLoader == null)
					return Result(CollectionReviewedWorkflowRehydrationStatus.RepreparationRequired, snapshot, null, null, "Committed-child safe-boundary recovery data is unavailable; the workflow must be re-prepared.");
				CollectionNativeChildRecoveryManifest recovery = _recoveryManifestLoader(operation, child);
				if (recovery == null || recovery.SafeBoundaryStateFingerprint == null)
					return Result(CollectionReviewedWorkflowRehydrationStatus.RetainedInputInvalid, snapshot, null, null, "A verified committed child is missing its durable C6.10 safe-boundary fingerprint.");
				expectedStateFingerprint = recovery.SafeBoundaryStateFingerprint;
				remaining.Remove(child.Member.MemberKey);
			}

			CollectionNativeStateIndex currentState = _stateCapture(snapshot.Target);
			if (currentState == null || !currentState.Target.Equals(snapshot.Target))
				return Result(CollectionReviewedWorkflowRehydrationStatus.RetainedInputInvalid, snapshot, currentState, null, "Native-state recapture did not return the reviewed target.");
			if (!currentState.Fingerprint.Equals(expectedStateFingerprint))
				return Result(CollectionReviewedWorkflowRehydrationStatus.CurrentStateChanged, snapshot, currentState, null, "Relevant native state differs from the latest verified Collection safe boundary; the workflow must be re-prepared before any new mutation.");
			List<CollectionMemberKey> ordered = snapshot.Phases.OrderBy(x => x.PhaseNumber)
				.SelectMany(x => x.Members).Select(x => x.MemberKey).Where(remaining.Contains).ToList();
			return Result(CollectionReviewedWorkflowRehydrationStatus.Ready, snapshot, currentState, ordered, "The exact reviewed workflow is safe to continue from the verified boundary.");
		}

		private void ValidatePreparedDescriptors(CollectionReviewedWorkflowSnapshot snapshot)
		{
			foreach (CollectionReviewedPreparedRecipeSnapshot recipe in snapshot.PreparedRecipes)
			{
				if (!StringComparer.Ordinal.Equals(recipe.Validation.AdapterId, ModInstallationSimpleFileRecipeAdapter.AdapterId) ||
					recipe.Validation.AdapterVersion != ModInstallationSimpleFileRecipeAdapter.AdapterVersion)
					throw new NotSupportedException("The reviewed native recipe adapter contract changed and must be re-prepared.");
				if (recipe.Validation.Capabilities.Count != 1 ||
					!StringComparer.Ordinal.Equals(recipe.Validation.Capabilities[0].CapabilityId, ModInstallationSimpleFileRecipeAdapter.CapabilityId) ||
					recipe.Validation.Capabilities[0].Version != ModInstallationSimpleFileRecipeAdapter.CapabilityVersion)
					throw new NotSupportedException("The reviewed native recipe capability contract changed and must be re-prepared.");
				if (recipe.SimpleFileMappings.Count == 0)
					throw new NotSupportedException("The reviewed native recipe predates executable simple-file mapping persistence and must be re-prepared.");
				bool foundExpectedArchive = false;
				foreach (string artifactId in recipe.RetainedArtifactIds)
				{
					CollectionsRetainedArtifact artifact = _artifactLoader(artifactId);
					if (artifact.ContentHash.Algorithm == CollectionContentHashAlgorithm.Sha256 &&
						StringComparer.Ordinal.Equals(artifact.ContentHash.Value, recipe.Validation.ExpectedContent.Sha256) &&
						artifact.ByteLength == recipe.Validation.ExpectedContent.ByteLength)
						foundExpectedArchive = true;
				}
				if (!foundExpectedArchive)
					throw new InvalidDataException("A reviewed prepared recipe no longer references its exact immutable source archive.");
			}
		}

		private static CollectionsRetainedArtifact LoadVerifiedArtifact(CollectionsRetainedArtifactStore store, string artifactId)
		{
			CollectionsRetainedArtifact artifact = store.GetArtifact(artifactId);
			if (artifact == null || !store.VerifyArtifact(artifactId))
				throw new InvalidDataException("A retained artifact required by the reviewed workflow is missing or failed integrity verification.");
			return artifact;
		}

		private static bool MatchesRecord(CollectionReviewedWorkflowSnapshot snapshot, CollectionResolvedPlanRecord record, CollectionOperation operation)
		{
			return snapshot.Identity.Equals(record.Identity) && snapshot.Identity.Equals(operation.PlanIdentity) &&
				snapshot.Revision.Equals(record.Revision) && snapshot.Revision.Equals(operation.Revision) &&
				snapshot.Target.Equals(record.Target) && snapshot.Target.Equals(operation.Target) &&
				snapshot.PolicyKind == record.PolicyKind && snapshot.ApprovedStateFingerprint.Equals(record.CurrentStateFingerprint) &&
				operation.Collection.Equals(snapshot.Revision.Collection);
		}

		private static bool IsSafeLoadPhase(CollectionOperationPhase phase)
		{
			return phase == CollectionOperationPhase.ReadyForReview || phase == CollectionOperationPhase.ReadyToApply ||
				phase == CollectionOperationPhase.ApplyingNativeChildren || phase == CollectionOperationPhase.PausedAtSafeBoundary ||
				phase == CollectionOperationPhase.Verifying;
		}

		private static CollectionReviewedWorkflowRehydrationResult Result(CollectionReviewedWorkflowRehydrationStatus status,
			CollectionReviewedWorkflowSnapshot snapshot, CollectionNativeStateIndex state, IEnumerable<CollectionMemberKey> remaining,
			string message)
		{
			return new CollectionReviewedWorkflowRehydrationResult(status, snapshot, state, remaining, message);
		}
	}
}
