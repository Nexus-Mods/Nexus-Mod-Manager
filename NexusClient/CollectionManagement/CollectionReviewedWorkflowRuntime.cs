using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.OnlineServices.NexusMods.Collections;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Reconstructed executable C6.15.12 runtime objects for one exact persisted reviewed additive workflow.
	/// </summary>
	/// <remarks>
	/// The runtime is derived only after C6.15.10 rehydration has validated retained inputs and the current safe-boundary
	/// native state. It does not authorize mutation; the application coordinator still owns approval and C6.6-C6.10 sequencing.
	/// </remarks>
	public sealed class CollectionReviewedWorkflowRuntime
	{
		private readonly ReadOnlyCollection<CollectionMemberKey> _remainingMembers;
		private readonly ReadOnlyCollection<PreparedCollectionNativeRecipe> _preparedRecipes;
		private readonly ReadOnlyCollection<CollectionVerifiedArchive> _verifiedArchives;

		internal CollectionReviewedWorkflowRuntime(CollectionReviewedWorkflowSnapshot snapshot, ResolvedCollectionPlan plan,
			CollectionMemberMatchSet matches, CollectionDependencyPhasePlan dependencyPlan, CollectionConflictImpactPlan impactPlan,
			CollectionNativeStateIndex currentState, IEnumerable<CollectionMemberKey> remainingMembers,
			IEnumerable<PreparedCollectionNativeRecipe> preparedRecipes, IEnumerable<CollectionVerifiedArchive> verifiedArchives)
		{
			Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
			Plan = plan ?? throw new ArgumentNullException(nameof(plan));
			Matches = matches ?? throw new ArgumentNullException(nameof(matches));
			DependencyPlan = dependencyPlan ?? throw new ArgumentNullException(nameof(dependencyPlan));
			ImpactPlan = impactPlan ?? throw new ArgumentNullException(nameof(impactPlan));
			CurrentState = currentState ?? throw new ArgumentNullException(nameof(currentState));
			_remainingMembers = new ReadOnlyCollection<CollectionMemberKey>((remainingMembers ?? throw new ArgumentNullException(nameof(remainingMembers))).ToList());
			_preparedRecipes = new ReadOnlyCollection<PreparedCollectionNativeRecipe>((preparedRecipes ?? throw new ArgumentNullException(nameof(preparedRecipes))).ToList());
			_verifiedArchives = new ReadOnlyCollection<CollectionVerifiedArchive>((verifiedArchives ?? throw new ArgumentNullException(nameof(verifiedArchives))).ToList());
		}

		public CollectionReviewedWorkflowSnapshot Snapshot { get; }
		public ResolvedCollectionPlan Plan { get; }
		public CollectionMemberMatchSet Matches { get; }
		public CollectionDependencyPhasePlan DependencyPlan { get; }
		public CollectionConflictImpactPlan ImpactPlan { get; }
		public CollectionNativeStateIndex CurrentState { get; }
		public ReadOnlyCollection<CollectionMemberKey> RemainingMembers { get { return _remainingMembers; } }
		public ReadOnlyCollection<PreparedCollectionNativeRecipe> PreparedRecipes { get { return _preparedRecipes; } }
		public ReadOnlyCollection<CollectionVerifiedArchive> VerifiedArchives { get { return _verifiedArchives; } }

		/// <summary>Gets the exact prepared recipe for one selected member, or null for a reviewed no-op member.</summary>
		public PreparedCollectionNativeRecipe GetPreparedRecipe(CollectionMemberKey memberKey)
		{
			if (memberKey == null) throw new ArgumentNullException(nameof(memberKey));
			return _preparedRecipes.SingleOrDefault(x => x.Member.MemberKey.Equals(memberKey));
		}

		/// <summary>Gets the exact verified immutable archive for one selected member, or null when none was required.</summary>
		public CollectionVerifiedArchive GetVerifiedArchive(CollectionMemberKey memberKey)
		{
			if (memberKey == null) throw new ArgumentNullException(nameof(memberKey));
			return _verifiedArchives.SingleOrDefault(x => x.Request.MemberKey.Equals(memberKey));
		}
	}

	/// <summary>
	/// Reconstructs the exact reviewed C6.2-C6.4 and C6.15.9 runtime objects from retained immutable v2 workflow data.
	/// </summary>
	public sealed class CollectionReviewedWorkflowRuntimeReconstructor
	{
		private readonly CollectionsCatalogStore _catalogStore;
		private readonly CollectionsRevisionSourceStore _revisionSourceStore;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionsRetainedArtifactReferenceStore _referenceStore;
		private readonly NexusCollectionManifestNormalizer _normalizer;
		private readonly ModInstallationSimpleFileRecipeAdapter _simpleFileAdapter;
		private readonly CollectionEffectiveSelectionBuilder _selectionBuilder;

		/// <summary>Creates a runtime reconstructor over the durable Collections stores.</summary>
		public CollectionReviewedWorkflowRuntimeReconstructor(CollectionsStore store)
			: this(new CollectionsCatalogStore(store), new CollectionsRevisionSourceStore(store),
				new CollectionsRetainedArtifactStore(store), new CollectionsRetainedArtifactReferenceStore(store),
				new NexusCollectionManifestNormalizer(), new ModInstallationSimpleFileRecipeAdapter(), new CollectionEffectiveSelectionBuilder())
		{
		}

		internal CollectionReviewedWorkflowRuntimeReconstructor(CollectionsCatalogStore catalogStore,
			CollectionsRevisionSourceStore revisionSourceStore, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore, NexusCollectionManifestNormalizer normalizer,
			ModInstallationSimpleFileRecipeAdapter simpleFileAdapter, CollectionEffectiveSelectionBuilder selectionBuilder)
		{
			_catalogStore = catalogStore ?? throw new ArgumentNullException(nameof(catalogStore));
			_revisionSourceStore = revisionSourceStore ?? throw new ArgumentNullException(nameof(revisionSourceStore));
			_artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
			_referenceStore = referenceStore ?? throw new ArgumentNullException(nameof(referenceStore));
			_normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
			_simpleFileAdapter = simpleFileAdapter ?? throw new ArgumentNullException(nameof(simpleFileAdapter));
			_selectionBuilder = selectionBuilder ?? throw new ArgumentNullException(nameof(selectionBuilder));
		}

		/// <summary>
		/// Reconstructs executable reviewed runtime state after a successful C6.15.10 safe-boundary rehydration.
		/// </summary>
		public CollectionReviewedWorkflowRuntime Reconstruct(CollectionReviewedWorkflowRehydrationResult rehydration)
		{
			if (rehydration == null) throw new ArgumentNullException(nameof(rehydration));
			if (!rehydration.CanResume || rehydration.Snapshot == null || rehydration.CurrentState == null)
				throw new InvalidOperationException("Reviewed workflow runtime can be reconstructed only from a successful safe-boundary rehydration result.");

			CollectionReviewedWorkflowSnapshot snapshot = rehydration.Snapshot;
			ResolvedCollectionPlan plan = ReconstructPlan(snapshot);
			Dictionary<CollectionMemberKey, CollectionVerifiedArchive> archives = ReconstructVerifiedArchives(plan, snapshot);
			CollectionMemberMatchSet matches = ReconstructMatches(plan, snapshot, rehydration.CurrentState, rehydration.RemainingMembers, archives);
			CollectionDependencyPhasePlan dependencyPlan = ReconstructDependencyPlan(plan, matches, snapshot);
			CollectionConflictImpactPlan impactPlan = ReconstructImpactPlan(plan, snapshot, rehydration.CurrentState);
			List<PreparedCollectionNativeRecipe> recipes = ReconstructPreparedRecipes(plan, snapshot);

			return new CollectionReviewedWorkflowRuntime(snapshot, plan, matches, dependencyPlan, impactPlan,
				rehydration.CurrentState, rehydration.RemainingMembers, recipes, archives.Values);
		}

		/// <summary>
		/// Reconstructs the exact immutable reviewed plan from retained v2 workflow inputs without asserting that current native state is resumable.
		/// </summary>
		/// <remarks>
		/// C6.15.12 recovery uses this only after loading the exact persisted reviewed snapshot and before C6.10 reconciles a C6.9 terminal result.
		/// It validates retained revision/source identity and selected closure, but does not authorize mutation or replace safe-boundary rehydration.
		/// </remarks>
		public ResolvedCollectionPlan ReconstructPlan(CollectionReviewedWorkflowSnapshot snapshot)
		{
			if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
			if (snapshot.PolicyKind != CollectionExecutionPolicyKind.InstallIntoCurrentSetup ||
				snapshot.ReplacementBackupChoice != CollectionReplacementBackupChoice.NotApplicable)
				throw new NotSupportedException("C6.15.12 reviewed-runtime reconstruction is additive-only.");

			CollectionRevision revision = _catalogStore.GetRevision(snapshot.Revision);
			if (revision == null)
				throw new InvalidDataException("The exact Collection revision metadata required by the reviewed workflow is missing.");
			byte[] rawManifest = _revisionSourceStore.LoadManifest(snapshot.Revision, snapshot.ManifestSource);
			NexusCollectionManifestNormalizationResult normalized = _normalizer.Normalize(rawManifest, revision);
			if (!normalized.Manifest.Revision.Equals(snapshot.Revision) || !normalized.Manifest.Source.Equals(snapshot.ManifestSource))
				throw new InvalidDataException("The retained Collection manifest no longer reproduces the reviewed revision/source identity.");

			CollectionEffectiveSelection effective = ReconstructEffectiveSelection(normalized.CapabilityReport, snapshot);
			return BuildPlan(effective, snapshot);
		}

		private CollectionEffectiveSelection ReconstructEffectiveSelection(CollectionCapabilityReport normalizedCapability,
			CollectionReviewedWorkflowSnapshot snapshot)
		{
			var selected = new HashSet<CollectionMemberKey>(snapshot.Members.Select(x => x.MemberKey));
			var decisions = new List<CollectionOptionalMemberSelection>();
			foreach (NormalizedCollectionMember member in normalizedCapability.Manifest.Members)
			{
				if (member.Requirement != CollectionMemberRequirement.Optional || !member.IdentityResolution.IsResolved)
					continue;
				decisions.Add(new CollectionOptionalMemberSelection(member.IdentityResolution.Key,
					selected.Contains(member.IdentityResolution.Key) ? CollectionMemberSelection.Selected : CollectionMemberSelection.Unselected));
			}

			CollectionEffectiveSelection effective = _selectionBuilder.Build(normalizedCapability, decisions);
			if (effective.CapabilityReport.Status != CollectionCompatibilityStatus.Supported)
				throw new NotSupportedException("The retained manifest no longer reproduces a supported reviewed selected closure.");
			ValidateManifestReviewIdentity(effective.Manifest, snapshot);
			return effective;
		}

		private static ResolvedCollectionPlan BuildPlan(CollectionEffectiveSelection effective,
			CollectionReviewedWorkflowSnapshot snapshot)
		{
			var normalizedByKey = effective.Manifest.Members.Where(x => x.IsSelected && x.IdentityResolution.IsResolved)
				.ToDictionary(x => x.IdentityResolution.Key);
			var members = new List<ResolvedCollectionMemberPlan>();
			foreach (CollectionReviewedMemberSnapshot persisted in snapshot.Members)
			{
				NormalizedCollectionMember normalized;
				if (!normalizedByKey.TryGetValue(persisted.MemberKey, out normalized))
					throw new InvalidDataException("A reviewed selected member is absent from the retained effective manifest.");
				if (normalized.SourceOrdinal != persisted.SourceOrdinal || normalized.Requirement != persisted.Requirement ||
					normalized.InstallationPhase != persisted.InstallationPhase ||
					!StringComparer.Ordinal.Equals(normalized.RecipeIdentity.Fingerprint, persisted.RecipeFingerprint) ||
					!Equals(normalized.Artifact, persisted.RequestedArtifact))
					throw new InvalidDataException("A retained normalized member no longer matches its reviewed immutable descriptor.");

				CollectionResolvedArtifactChoice choice = persisted.ArtifactChoiceKind == CollectionResolvedArtifactChoiceKind.ExactRequestedArtifact
					? CollectionResolvedArtifactChoice.Exact(persisted.RequestedArtifact)
					: CollectionResolvedArtifactChoice.SupportedSubstitution(persisted.RequestedArtifact, persisted.SelectedArtifact, persisted.SubstitutionRuleId);
				members.Add(new ResolvedCollectionMemberPlan(normalized, choice));
			}

			return new ResolvedCollectionPlan(snapshot.Identity, snapshot.Target, CollectionExecutionPolicy.InstallIntoCurrentSetup(),
				snapshot.ApprovedStateFingerprint, effective.CapabilityReport, members);
		}

		private Dictionary<CollectionMemberKey, CollectionVerifiedArchive> ReconstructVerifiedArchives(ResolvedCollectionPlan plan,
			CollectionReviewedWorkflowSnapshot snapshot)
		{
			var result = new Dictionary<CollectionMemberKey, CollectionVerifiedArchive>();
			foreach (CollectionReviewedPhaseMemberSnapshot phaseMember in snapshot.Phases.SelectMany(x => x.Members))
			{
				if (phaseMember.VerifiedArchive == null)
					continue;
				ResolvedCollectionMemberPlan member = plan.SelectedMembers.Single(x => x.MemberKey.Equals(phaseMember.MemberKey));
				Guid requestId = CollectionMemberAcquisitionCoordinator.CreateStableRequestId(plan.Identity, member.MemberKey);
				CollectionAcquisitionRequest request = CollectionAcquisitionRequest.Create(requestId, plan, member.MemberKey);
				CollectionReviewedVerifiedArchiveSnapshot persisted = phaseMember.VerifiedArchive;
				CollectionsRetainedArtifact artifact = _artifactStore.GetArtifact(persisted.ArtifactId);
				if (artifact == null || !artifact.ContentHash.Equals(persisted.ContentHash) || artifact.ByteLength != persisted.ByteLength ||
					!_artifactStore.VerifyArtifact(artifact.ArtifactId))
					throw new InvalidDataException("A reviewed verified archive is missing, changed or failed retained-content verification.");

				string ownerId = requestId.ToString("D");
				string expectedRole = CollectionVerifiedArchiveAdopter.CreateReferenceRole(request.SelectedArtifact);
				List<CollectionsRetainedArtifactReferenceRecord> verifiedReferences = _referenceStore
					.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Download, ownerId)
					.Where(x => x.Role.StartsWith(CollectionVerifiedArchiveAdopter.VerifiedReferenceRolePrefix, StringComparison.Ordinal)).ToList();
				if (verifiedReferences.Count != 1 || !StringComparer.Ordinal.Equals(verifiedReferences[0].Role, expectedRole) ||
					!StringComparer.Ordinal.Equals(verifiedReferences[0].ArtifactId, artifact.ArtifactId))
					throw new InvalidDataException("The reviewed verified archive no longer has its exact deterministic acquisition reference.");

				result.Add(member.MemberKey, new CollectionVerifiedArchive(request, artifact, verifiedReferences[0],
					persisted.SourceKind, persisted.VerificationBasis));
			}
			return result;
		}

		private static CollectionMemberMatchSet ReconstructMatches(ResolvedCollectionPlan plan,
			CollectionReviewedWorkflowSnapshot snapshot, CollectionNativeStateIndex currentState,
			IEnumerable<CollectionMemberKey> remainingMembers, IDictionary<CollectionMemberKey, CollectionVerifiedArchive> archives)
		{
			var remaining = new HashSet<CollectionMemberKey>(remainingMembers);
			var planMembers = plan.SelectedMembers.ToDictionary(x => x.MemberKey);
			var results = new List<CollectionMemberMatchResult>();
			foreach (CollectionReviewedPhaseMemberSnapshot persisted in snapshot.Phases.OrderBy(x => x.PhaseNumber).SelectMany(x => x.Members))
			{
				ResolvedCollectionMemberPlan member = planMembers[persisted.MemberKey];
				var candidates = new List<CollectionNativeModState>();
				foreach (CollectionReviewedNativeCandidateSnapshot candidate in persisted.NativeCandidates)
				{
					if (!StringComparer.Ordinal.Equals(candidate.TargetFingerprint, plan.Target.Fingerprint))
						throw new InvalidDataException("A reviewed native candidate belongs to a different target.");
					var identity = new NativeModInstanceIdentity(plan.Target, candidate.NativeModKey);
					CollectionNativeModState live;
					if (currentState.Mods.TryGetValue(identity, out live))
					{
						ValidateNativeCandidate(candidate, live);
						candidates.Add(live);
					}
					else if (remaining.Contains(member.MemberKey) || persisted.Disposition == CollectionMemberMatchDisposition.InstalledCompatible)
						throw new InvalidDataException("A reviewed native candidate required by remaining work is no longer present at the verified safe boundary.");
					else
						candidates.Add(new CollectionNativeModState(identity, String.Empty, candidate.NativeModKey,
							candidate.NexusModId, candidate.NexusFileId, String.Empty, String.Empty, candidate.InstallRoot, candidate.InstallMethod));
				}

				var bindings = new List<CollectionMemberBinding>();
				foreach (CollectionReviewedBindingSnapshot binding in persisted.Bindings)
				{
					CollectionTargetAssociation liveAssociation;
					if (!currentState.Associations.TryGetValue(binding.AssociationId, out liveAssociation) || !liveAssociation.Target.Equals(plan.Target))
						throw new InvalidDataException("A reviewed existing Collection association is no longer available at the verified safe boundary.");
					var reviewedAssociation = new CollectionTargetAssociation(binding.AssociationId, liveAssociation.Revision, plan.Target, binding.AssociationState);
					bindings.Add(new CollectionMemberBinding(reviewedAssociation, binding.MemberKey,
						new NativeModInstanceIdentity(plan.Target, binding.NativeModKey),
						CollectionRecipeIdentity.FromFingerprint(binding.VerifiedRecipeFingerprint), binding.BindingKind));
				}

				CollectionVerifiedArchive archive;
				archives.TryGetValue(member.MemberKey, out archive);
				results.Add(new CollectionMemberMatchResult(member, persisted.Disposition, persisted.Reason, candidates, bindings, archive));
			}
			return new CollectionMemberMatchSet(plan, snapshot.ApprovedStateFingerprint, results);
		}

		private static CollectionDependencyPhasePlan ReconstructDependencyPlan(ResolvedCollectionPlan plan,
			CollectionMemberMatchSet matches, CollectionReviewedWorkflowSnapshot snapshot)
		{
			var byKey = matches.MembersByKey;
			var phases = snapshot.Phases.OrderBy(x => x.PhaseNumber).Select(x => new CollectionExecutionPhase(x.PhaseNumber,
				x.Members.Select(y => new CollectionPlannedPhaseMember(byKey[y.MemberKey])))).ToList();
			var barriers = snapshot.Barriers.Select(x => new CollectionPhaseBarrier(x.CompletedPhase, x.NextPhase, x.Kind)).ToList();
			return new CollectionDependencyPhasePlan(plan, matches, phases, barriers, new CollectionDependencyPhaseIssue[0]);
		}

		private static CollectionConflictImpactPlan ReconstructImpactPlan(ResolvedCollectionPlan plan,
			CollectionReviewedWorkflowSnapshot snapshot, CollectionNativeStateIndex currentState)
		{
			var files = snapshot.FileImpacts.Select(x => new CollectionFileImpact(x.Target, x.Writers, x.PlannedWinner,
				x.CurrentOwnerKey, x.AffectedAssociationIds)).ToList();
			var plugins = snapshot.PluginImpacts.Select(x => new CollectionPluginImpact(x.MemberKey, x.Effect, null,
				x.AffectedAssociationIds)).ToList();
			var configs = snapshot.ConfigurationImpacts.Select(x => new CollectionConfigurationImpact(x.MemberKey, x.Kind,
				x.SubjectKey, x.CurrentOwnerKey, x.ChangesRecordedValue, x.AffectedAssociationIds)).ToList();
			var associations = new List<CollectionAssociationImpact>();
			foreach (CollectionReviewedAssociationImpactSnapshot persisted in snapshot.AssociationImpacts)
			{
				CollectionTargetAssociation association;
				if (!currentState.Associations.TryGetValue(persisted.AssociationId, out association))
					throw new InvalidDataException("A reviewed affected Collection association is no longer available at the verified safe boundary.");
				associations.Add(new CollectionAssociationImpact(association, persisted.Kind));
			}
			return new CollectionConflictImpactPlan(plan, snapshot.ApprovedStateFingerprint, files, plugins, configs, associations,
				new CollectionConflictImpactIssue[0]);
		}

		private List<PreparedCollectionNativeRecipe> ReconstructPreparedRecipes(ResolvedCollectionPlan plan,
			CollectionReviewedWorkflowSnapshot snapshot)
		{
			var result = new List<PreparedCollectionNativeRecipe>();
			foreach (CollectionReviewedPreparedRecipeSnapshot persisted in snapshot.PreparedRecipes)
			{
				ResolvedCollectionMemberPlan member = plan.SelectedMembers.Single(x => x.MemberKey.Equals(persisted.MemberKey));
				if (persisted.SourceOrdinal != member.SourceOrdinal ||
					!StringComparer.Ordinal.Equals(persisted.ProviderRecipeFingerprint, member.RecipeIdentity.Fingerprint))
					throw new InvalidDataException("A reviewed prepared recipe no longer matches its reconstructed selected member.");
				if (!StringComparer.Ordinal.Equals(persisted.Validation.AdapterId, ModInstallationSimpleFileRecipeAdapter.AdapterId) ||
					persisted.Validation.AdapterVersion != ModInstallationSimpleFileRecipeAdapter.AdapterVersion || persisted.SimpleFileMappings.Count == 0)
					throw new NotSupportedException("The reviewed native recipe cannot be reproduced by the current characterized simple-file adapter.");

				foreach (string artifactId in persisted.RetainedArtifactIds)
				{
					CollectionsRetainedArtifact artifact = _artifactStore.GetArtifact(artifactId);
					if (artifact == null || !_artifactStore.VerifyArtifact(artifactId))
						throw new InvalidDataException("A retained artifact required to reconstruct the reviewed native recipe is missing or corrupt.");
				}

				var fingerprint = new ModOperationFingerprint(plan.Target.Fingerprint, persisted.Validation.InstallContext,
					member.RecipeIdentity.Fingerprint);
				var input = new ModInstallationRecipeInput(ModOperationIdentity.CreateNew(ModOperationOrigin.Collection, fingerprint), persisted.Validation);
				var recipe = new ModInstallationSimpleFileRecipe(persisted.SimpleFileMappings.Select(x =>
					new ModInstallationSimpleFileMapping(x.SourcePath, x.DestinationPath)));
				ModInstallationRecipeInput translated = _simpleFileAdapter.Translate(input, recipe);
				ValidateTranslatedMappings(translated, persisted.SimpleFileMappings);
				result.Add(new PreparedCollectionNativeRecipe(member,
					PreparedCollectionNativeRecipeIdentity.FromFingerprint(persisted.PreparedNativeFingerprint), translated,
					persisted.EffectPreview, persisted.SkipReadmeFiles, persisted.RetainedArtifactIds));
			}
			return result;
		}

		private static void ValidateManifestReviewIdentity(NormalizedCollectionManifest manifest,
			CollectionReviewedWorkflowSnapshot snapshot)
		{
			var selected = manifest.Members.Where(x => x.IsSelected && x.IdentityResolution.IsResolved).ToList();
			if (selected.Count != snapshot.Members.Count)
				throw new InvalidDataException("The retained effective selected member closure differs from the reviewed workflow.");

			var dependencies = new HashSet<string>(manifest.Dependencies.Select(x => DependencyKey(x.PrerequisiteMemberKey, x.DependentMemberKey, x.Kind)), StringComparer.Ordinal);
			var reviewedDependencies = new HashSet<string>(snapshot.Dependencies.Select(x => DependencyKey(x.Prerequisite, x.Dependent, x.Kind)), StringComparer.Ordinal);
			if (!dependencies.SetEquals(reviewedDependencies))
				throw new InvalidDataException("The retained manifest dependency graph differs from the reviewed workflow.");

			var priorities = new HashSet<string>(manifest.FilePriorityRules.Select(x => PriorityKey(x.LowerPriorityMemberKey, x.HigherPriorityMemberKey)), StringComparer.Ordinal);
			var reviewedPriorities = new HashSet<string>(snapshot.PriorityRules.Select(x => PriorityKey(x.Lower, x.Higher)), StringComparer.Ordinal);
			if (!priorities.SetEquals(reviewedPriorities))
				throw new InvalidDataException("The retained manifest file-priority rules differ from the reviewed workflow.");
		}

		private static void ValidateNativeCandidate(CollectionReviewedNativeCandidateSnapshot expected, CollectionNativeModState actual)
		{
			if (!StringComparer.Ordinal.Equals(expected.NativeModKey, actual.Identity.NativeModKey) ||
				!StringComparer.Ordinal.Equals(expected.NexusModId, actual.NexusModId) ||
				!StringComparer.Ordinal.Equals(expected.NexusFileId, actual.NexusFileId) ||
				expected.InstallRoot != actual.InstallRoot || expected.InstallMethod != actual.InstallMethod)
				throw new InvalidDataException("A native mod candidate differs from the exact reviewed identity/install context.");
		}

		private static void ValidateTranslatedMappings(ModInstallationRecipeInput translated,
			IReadOnlyList<CollectionReviewedSimpleFileMappingSnapshot> expected)
		{
			if (translated.NativeOperations.Count != expected.Count)
				throw new InvalidDataException("Reconstructed simple-file operation count differs from the reviewed descriptor.");
			for (int i = 0; i < expected.Count; i++)
			{
				InstallModFileOperation operation = translated.NativeOperations[i] as InstallModFileOperation;
				if (operation == null || operation.DeploymentDecision != null ||
					!StringComparer.Ordinal.Equals(operation.SourcePath, expected[i].SourcePath) ||
					!StringComparer.Ordinal.Equals(operation.DestinationPath, expected[i].DestinationPath))
					throw new InvalidDataException("Reconstructed simple-file native operations differ from the reviewed executable descriptor.");
			}
		}

		private static string DependencyKey(CollectionMemberKey prerequisite, CollectionMemberKey dependent, CollectionMemberDependencyKind kind)
		{
			return ((int)prerequisite.Kind) + ":" + prerequisite.Value + ">" + ((int)dependent.Kind) + ":" + dependent.Value + ":" + ((int)kind);
		}

		private static string PriorityKey(CollectionMemberKey lower, CollectionMemberKey higher)
		{
			return ((int)lower.Kind) + ":" + lower.Value + ">" + ((int)higher.Kind) + ":" + higher.Value;
		}
	}
}
