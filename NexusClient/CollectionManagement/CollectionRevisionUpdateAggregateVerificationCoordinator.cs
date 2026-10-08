using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.Mods;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Durable evidence that C10.8 verified the complete supported candidate-update boundary.</summary>
	public sealed class CollectionRevisionUpdateAggregateVerificationRecord
	{
		internal CollectionRevisionUpdateAggregateVerificationRecord(Guid operationId, CollectionPlanIdentity planIdentity,
			string reviewFingerprint, CollectionCurrentStateFingerprint stateFingerprint)
		{
			if (operationId == Guid.Empty) throw new ArgumentException("An aggregate verification operation id is required.", nameof(operationId));
			OperationId = operationId;
			PlanIdentity = planIdentity ?? throw new ArgumentNullException(nameof(planIdentity));
			ReviewFingerprint = CollectionIdentityValidation.RequireOpaqueToken(reviewFingerprint, nameof(reviewFingerprint));
			StateFingerprint = stateFingerprint ?? throw new ArgumentNullException(nameof(stateFingerprint));
		}

		public Guid OperationId { get; }
		public CollectionPlanIdentity PlanIdentity { get; }
		public string ReviewFingerprint { get; }
		public CollectionCurrentStateFingerprint StateFingerprint { get; }
	}

	/// <summary>Result of C10.8 aggregate verification. Revision/association publication remains a later boundary.</summary>
	public sealed class CollectionRevisionUpdateAggregateVerificationResult
	{
		internal CollectionRevisionUpdateAggregateVerificationResult(CollectionOperation operation,
			CollectionNativeStateIndex verifiedState, CollectionRevisionUpdateAggregateVerificationRecord verification)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			VerifiedState = verifiedState ?? throw new ArgumentNullException(nameof(verifiedState));
			Verification = verification ?? throw new ArgumentNullException(nameof(verification));
		}

		public CollectionOperation Operation { get; }
		public CollectionNativeStateIndex VerifiedState { get; }
		public CollectionRevisionUpdateAggregateVerificationRecord Verification { get; }
	}

	/// <summary>
	/// Pure aggregate checks over the final authoritative state after C10.4-C10.7. Exact mutating child evidence is
	/// verified by the coordinator before entering this verifier; this class then proves the complete candidate member
	/// closure and the absence of stale managed file/config ownership for removed effects inside updated members.
	/// </summary>
	public sealed class CollectionRevisionUpdateAggregateVerifier
	{
		public void Verify(CollectionRevisionUpdatePlan updatePlan, CollectionRevisionUpdateOverridePreservationPlan preservation,
			CollectionOperation operation, CollectionNativeStateIndex state,
			IReadOnlyDictionary<CollectionMemberKey, CollectionNativeModState> verifiedCandidateNative)
		{
			if (updatePlan == null) throw new ArgumentNullException(nameof(updatePlan));
			if (preservation == null) throw new ArgumentNullException(nameof(preservation));
			if (operation == null) throw new ArgumentNullException(nameof(operation));
			if (state == null) throw new ArgumentNullException(nameof(state));
			if (verifiedCandidateNative == null) throw new ArgumentNullException(nameof(verifiedCandidateNative));
			if (!preservation.IsQualified) throw new InvalidOperationException("C10.8 requires a fully qualified C10.5 preservation plan.");
			if (operation.Kind != CollectionOperationKind.UpdateRevision || operation.PlanIdentity == null || !operation.PlanIdentity.Equals(updatePlan.NewPlan.Identity) ||
				operation.Revision == null || !operation.Revision.Equals(updatePlan.NewPlan.Revision) || !operation.Target.Equals(updatePlan.NewPlan.Target))
				throw new InvalidOperationException("C10.8 aggregate inputs do not belong to the exact active revision-update operation.");
			preservation.ReviewedIntent.ValidateCurrentPlan(updatePlan);
			if (!state.Target.Equals(updatePlan.NewPlan.Target) || state.AssociationCoverage != CollectionNativeStateCoverage.Complete)
				throw new InvalidOperationException("C10.8 requires one complete authoritative observation of the reviewed target.");

			var suppressed = new HashSet<CollectionMemberKey>(preservation.MembersWhoseCandidateMutationIsSuppressed);
			var expectedMutations = new HashSet<CollectionMemberKey>(updatePlan.Members.Where(x => RequiresCandidateMutation(x) && !suppressed.Contains(x.MemberKey)).Select(x => x.MemberKey));
			if (!expectedMutations.SetEquals(verifiedCandidateNative.Keys))
				throw new InvalidOperationException("The exact set of verified candidate native instances does not match the approved C10 mutation set.");

			foreach (CollectionRevisionUpdateMemberPlan member in updatePlan.Members)
			{
				if (member.NewMember == null) continue;
				if (suppressed.Contains(member.MemberKey))
				{
					VerifySuppressedMember(member, preservation, state);
					continue;
				}
				if (RequiresCandidateMutation(member))
				{
					CollectionNativeModState native = verifiedCandidateNative[member.MemberKey];
					CollectionNativeModState observed;
					if (!state.Mods.TryGetValue(native.Identity, out observed))
						throw new InvalidOperationException("A verified candidate member disappeared before aggregate verification.");
					continue;
				}
				if (member.Binding == null || !member.Binding.VerifiedRecipe.Equals(member.NewMember.RecipeIdentity) || !state.Mods.ContainsKey(member.Binding.NativeMod))
					throw new InvalidOperationException("An unchanged candidate member no longer has its exact verified old-revision native binding.");
			}

			VerifyRemovedMembers(updatePlan, operation, state, verifiedCandidateNative);
			VerifyRemovedEffectsInsideUpdatedMembers(updatePlan, suppressed, verifiedCandidateNative, state);
		}

		private static void VerifyRemovedMembers(CollectionRevisionUpdatePlan updatePlan, CollectionOperation operation,
			CollectionNativeStateIndex state, IReadOnlyDictionary<CollectionMemberKey, CollectionNativeModState> verifiedCandidateNative)
		{
			foreach (CollectionRevisionUpdateMemberPlan member in updatePlan.Members.Where(x => x.NewMember == null))
			{
				if (member.Binding == null)
					throw new InvalidOperationException("A removed old-revision member reached aggregate verification without its exact reviewed native binding.");
				List<CollectionNativeChildOperation> children = operation.NativeChildren.Where(x => x.Action == CollectionNativeChildAction.Deactivate &&
					x.Member.MemberKey.Equals(member.MemberKey)).ToList();
				if (children.Count > 1)
					throw new InvalidOperationException("More than one obsolete-revision deactivation child exists for the same removed member.");

				bool nativePresent = state.Mods.ContainsKey(member.Binding.NativeMod);
				bool identityReusedByCandidate = verifiedCandidateNative.Values.Any(x => x.Identity.Equals(member.Binding.NativeMod));
				if (member.Disposition == CollectionRevisionUpdateDisposition.PreserveStandalone)
				{
					bool nativeDeactivated = operation.NativeChildren.Any(x => x.Action == CollectionNativeChildAction.Deactivate &&
						updatePlan.Members.Any(m => m.Binding != null && m.MemberKey.Equals(x.Member.MemberKey) &&
							m.Binding.NativeMod.Equals(member.Binding.NativeMod)));

					// A validated Nexus file-id change is represented by an old removed key plus a new added key, even though
					// revision correlation proves they are one logical mod transition. PreserveStandalone protects this native
					// from disappearing while the old key leaves the revision; it must not reject the reviewed candidate reinstall
					// when that reinstall durably resolves to the exact same native identity (and therefore preserves standalone
					// provenance). A different native identity remains fail-closed.
					CollectionMemberKey correlatedCandidateKey;
					CollectionNativeModState correlatedCandidateNative;
					if (updatePlan.MemberCorrelations.TryGetNewMemberKey(member.MemberKey, out correlatedCandidateKey) &&
						verifiedCandidateNative.TryGetValue(correlatedCandidateKey, out correlatedCandidateNative))
					{
						if (!nativePresent || nativeDeactivated || !correlatedCandidateNative.Identity.Equals(member.Binding.NativeMod))
							throw new InvalidOperationException("A correlated independently used predecessor was not updated through its exact reviewed native identity.");
						continue;
					}

					if (!nativePresent || nativeDeactivated || identityReusedByCandidate)
						throw new InvalidOperationException("An independently used installation was removed or repurposed despite the approved preservation decision.");
					continue;
				}
				if (children.Count == 1)
				{
					if (!children[0].IsReconciled || !children[0].HasVerifiedCommittedNativeState)
						throw new InvalidOperationException("A removed old-revision member has an unverified deactivation child at aggregate verification.");
					if (nativePresent && !identityReusedByCandidate)
						throw new InvalidOperationException("A durably removed old-revision native member is still present without verified candidate reuse of that exact identity.");
				}
				else if (!nativePresent)
				{
					throw new InvalidOperationException("A removed old-revision native member disappeared without the reviewed C10 deactivation lineage; aggregate verification cannot treat an external or unexplained absence as qualified reversion.");
				}
			}
		}

		internal static bool RequiresCandidateMutation(CollectionRevisionUpdateMemberPlan member)
		{
			if (member == null || member.NewMember == null || member.ChangeKind == CollectionRevisionUpdateChangeKind.Removed) return false;
			if (member.ChangeKind != CollectionRevisionUpdateChangeKind.Unchanged) return true;
			return member.PreparationKind == CollectionRevisionUpdatePreparationKind.PreparedForCandidate ||
				member.PreparationKind == CollectionRevisionUpdatePreparationKind.CandidateChanged ||
				member.PreparationKind == CollectionRevisionUpdatePreparationKind.ReprepareRequired;
		}

		private static void VerifySuppressedMember(CollectionRevisionUpdateMemberPlan member,
			CollectionRevisionUpdateOverridePreservationPlan preservation, CollectionNativeStateIndex state)
		{
			List<CollectionRevisionUpdateOverridePreservationAction> actions = preservation.Actions.Where(x =>
				x.SuppressesCandidateMemberMutation && x.UserOverride.Requirement.MemberKey != null &&
				preservation.UpdatePlan.MemberCorrelations.ResolveCandidateMemberKey(x.UserOverride.Requirement.MemberKey).Equals(member.MemberKey)).ToList();
			if (actions.Count == 0) throw new InvalidOperationException("A suppressed candidate member has no exact qualified preservation action.");
			bool omitted = actions.Any(x => x.UserOverride.Requirement.Aspect == CollectionRequirementAspect.MemberParticipation &&
				x.UserOverride.UserChosenState.Kind == CollectionRequirementStateKind.Absent);
			if (member.Binding == null)
			{
				if (!omitted) throw new InvalidOperationException("A suppressed candidate member without an old native binding is not an intentional omission.");
				return;
			}
			if (!state.Mods.ContainsKey(member.Binding.NativeMod))
				throw new InvalidOperationException("A qualified preserve-existing-native-state override lost its reviewed native member.");
		}

		private static void VerifyRemovedEffectsInsideUpdatedMembers(CollectionRevisionUpdatePlan updatePlan,
			ISet<CollectionMemberKey> suppressed, IReadOnlyDictionary<CollectionMemberKey, CollectionNativeModState> nativeByMember,
			CollectionNativeStateIndex state)
		{
			foreach (CollectionRevisionUpdateEffectPlan effect in updatePlan.Effects.Where(x => x.ChangeKind == CollectionRevisionUpdateEffectChangeKind.Removed))
			{
				CollectionRevisionUpdateMemberPlan member = updatePlan.Members.SingleOrDefault(x => x.MemberKey.Equals(effect.MemberKey));
				if (member == null || member.NewMember == null || suppressed.Contains(member.MemberKey) || !RequiresCandidateMutation(member)) continue;
				CollectionNativeModState native;
				if (!nativeByMember.TryGetValue(member.MemberKey, out native))
					throw new InvalidOperationException("An updated member is missing from the aggregate native mapping while checking obsolete effects.");
				switch (effect.Kind)
				{
					case CollectionRevisionUpdateEffectKind.File:
						VerifyFileOwnerAbsent(effect.ResourceKey, native.Identity.NativeModKey, state);
						break;
					case CollectionRevisionUpdateEffectKind.Ini:
						VerifyIniOwnerAbsent(effect.ResourceKey, native.Identity.NativeModKey, state);
						break;
					case CollectionRevisionUpdateEffectKind.GameValue:
						VerifyGameValueOwnerAbsent(effect.ResourceKey, native.Identity.NativeModKey, state);
						break;
					case CollectionRevisionUpdateEffectKind.Plugin:
						// Plugin activation/order is global rather than member-owned. Exact candidate child evidence verifies the new
						// supported plugin state; native member ownership cannot prove a per-member negative plugin effect here.
						break;
					default:
						throw new InvalidOperationException("C10.8 encountered an unknown revision-update effect kind.");
				}
			}
		}

		private static void VerifyFileOwnerAbsent(string resourceKey, string ownerKey, CollectionNativeStateIndex state)
		{
			const string prefix = "file:";
			if (String.IsNullOrWhiteSpace(resourceKey) || !resourceKey.StartsWith(prefix, StringComparison.Ordinal))
				throw new InvalidDataException("A removed file-effect identity is not in the canonical C10 format.");
			int separator = resourceKey.IndexOf(':', prefix.Length);
			int rootValue;
			if (separator <= prefix.Length || !Int32.TryParse(resourceKey.Substring(prefix.Length, separator - prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out rootValue) ||
				!Enum.IsDefined(typeof(ModDeploymentRoot), rootValue))
				throw new InvalidDataException("A removed file-effect identity contains an invalid deployment root.");
			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical((ModDeploymentRoot)rootValue, resourceKey.Substring(separator + 1));
			CollectionNativeFileState file;
			if (state.Files.TryGetValue(target, out file) &&
				(ContainsOwner(file.InstallLogOwners, ownerKey) || ContainsOwner(file.DeploymentOwners, ownerKey) || ContainsOwner(file.VirtualOwners, ownerKey)))
				throw new InvalidOperationException("An updated member still owns a file effect that the candidate revision removed.");
		}

		private static bool ContainsOwner(IEnumerable<CollectionNativeOwnerState> owners, string ownerKey)
		{
			return owners.Any(x => x.Kind == CollectionNativeOwnerKind.NativeMod && StringComparer.OrdinalIgnoreCase.Equals(x.OwnerKey, ownerKey));
		}

		private static void VerifyIniOwnerAbsent(string resourceKey, string ownerKey, CollectionNativeStateIndex state)
		{
			const string prefix = "ini:";
			string value = resourceKey != null && resourceKey.StartsWith(prefix, StringComparison.Ordinal) ? resourceKey.Substring(prefix.Length) : null;
			string[] parts = value == null ? new string[0] : value.Split(new[] { ':' }, 3);
			if (parts.Length != 3) throw new InvalidDataException("A removed INI-effect identity is not in the canonical C10 format.");
			CollectionNativeIniState ini;
			if (state.IniEdits.TryGetValue(new CollectionNativeIniKey(parts[0], parts[1], parts[2]), out ini) &&
				ini.Values.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x.OwnerKey, ownerKey)))
				throw new InvalidOperationException("An updated member still owns an INI effect that the candidate revision removed.");
		}

		private static void VerifyGameValueOwnerAbsent(string resourceKey, string ownerKey, CollectionNativeStateIndex state)
		{
			const string prefix = "game:";
			if (String.IsNullOrWhiteSpace(resourceKey) || !resourceKey.StartsWith(prefix, StringComparison.Ordinal))
				throw new InvalidDataException("A removed game-value effect identity is not in the canonical C10 format.");
			CollectionNativeGameValueState value;
			if (state.GameValues.TryGetValue(resourceKey.Substring(prefix.Length), out value) &&
				value.Values.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x.OwnerKey, ownerKey)))
				throw new InvalidOperationException("An updated member still owns a game-specific value that the candidate revision removed.");
		}
	}

	/// <summary>
	/// C10.8 obtains one fresh authoritative observation, verifies every supported candidate member/effect plus retained C9
	/// customization, and durably seals the resulting native-state fingerprint for C10.9 publication. It does not publish
	/// the candidate association or mark the update committed.
	/// </summary>
	public sealed class CollectionRevisionUpdateAggregateVerificationCoordinator
	{
		private const string VerificationRole = "revision-update-aggregate-verification-v1";
		private const string VerificationFormat = "nmm-ce.collections.revision-update-aggregate-verification/1";

		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionsNativeChildRecoveryManifestStore _manifestStore;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionsRetainedArtifactReferenceStore _referenceStore;
		private readonly CollectionRevisionUpdateReviewCoordinator _reviewCoordinator;
		private readonly CollectionNativeStateReader _nativeStateReader;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;
		private readonly CollectionRevisionUpdateAggregateVerifier _verifier;

		private sealed class VerificationDto
		{
			public string Format { get; set; }
			public string OperationId { get; set; }
			public string PlanId { get; set; }
			public int PlanVersion { get; set; }
			public string ReviewFingerprint { get; set; }
			public string StateFingerprintFormatVersion { get; set; }
			public string StateFingerprintValue { get; set; }
		}

		public CollectionRevisionUpdateAggregateVerificationCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore, CollectionsAssociationStore associationStore,
			CollectionsNativeChildRecoveryManifestStore manifestStore, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
			_manifestStore = manifestStore ?? throw new ArgumentNullException(nameof(manifestStore));
			_artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
			_referenceStore = referenceStore ?? throw new ArgumentNullException(nameof(referenceStore));
			if (planStore == null) throw new ArgumentNullException(nameof(planStore));
			_reviewCoordinator = new CollectionRevisionUpdateReviewCoordinator(operationStore, planStore, associationStore);
			_nativeStateReader = new CollectionNativeStateReader(() => _services.ModManager.InstallationLog,
				_services.ModManager.VirtualModActivator, _services.PluginManager, _services.ModManager.GameMode, associationStore);
			_mutationLeaseManager = CollectionTargetMutationLeaseManager.Shared;
			_authorityValidator = new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services);
			_verifier = new CollectionRevisionUpdateAggregateVerifier();
		}

		public async Task<CollectionRevisionUpdateAggregateVerificationResult> VerifyAsync(CollectionOperationIdentity operationIdentity,
			CollectionRevisionUpdatePlan updatePlan, CollectionRevisionUpdateOverridePreservationPlan preservation,
			GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			if (updatePlan == null) throw new ArgumentNullException(nameof(updatePlan));
			if (preservation == null) throw new ArgumentNullException(nameof(preservation));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			if (!preservation.IsQualified) throw new InvalidOperationException("C10.8 cannot verify an update with unresolved override preservation decisions.");
			CollectionRevisionUpdateReviewedIntent reviewed = _reviewCoordinator.LoadReviewedIntent(operationIdentity);
			if (!StringComparer.Ordinal.Equals(reviewed.ReviewFingerprint, preservation.ReviewedIntent.ReviewFingerprint))
				throw new InvalidOperationException("The C10.8 preservation plan does not belong to the exact durable revision-update review.");
			reviewed.ValidateCurrentPlan(updatePlan);

			CollectionOperation operation = RequireOperation(operationIdentity, updatePlan.NewPlan);
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(updatePlan.NewPlan.Target))
				throw new InvalidOperationException("The canonical target changed before C10.8 aggregate verification.");

			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(false))
			{
				cancellationToken.ThrowIfCancellationRequested();
				_authorityValidator.ValidateAndReload(lease, authority, paths);
				operation = RequireOperation(operationIdentity, updatePlan.NewPlan);
				ValidatePersistedCustomization(reviewed, preservation);
				if (operation.Phase == CollectionOperationPhase.QualifiedRevisionOverridesVerified)
					operation = SavePhase(operation, CollectionOperationPhase.VerifyingCandidateRevisionAggregate, CollectionOperationResultState.Pending);
				else if (operation.Phase != CollectionOperationPhase.VerifyingCandidateRevisionAggregate &&
					operation.Phase != CollectionOperationPhase.CandidateRevisionAggregateVerified)
					throw new InvalidOperationException("C10.8 requires the verified override boundary before aggregate verification.");

				CollectionNativeStateIndex state = _nativeStateReader.Capture(updatePlan.NewPlan.Target);
				ValidatePersistedCustomization(reviewed, preservation);
				Dictionary<CollectionMemberKey, CollectionNativeModState> candidateNative = VerifyCandidateChildren(operation, updatePlan, preservation, state, paths);
				VerifyDeactivationChildren(operation, updatePlan, candidateNative, state);
				_verifier.Verify(updatePlan, preservation, operation, state, candidateNative);
				VerifyTypedOverrides(operation, updatePlan, preservation, state);

				CollectionRevisionUpdateAggregateVerificationRecord verification = PersistOrValidateVerification(operation,
					updatePlan.NewPlan.Identity, reviewed.ReviewFingerprint, state.Fingerprint);
				if (operation.Phase != CollectionOperationPhase.CandidateRevisionAggregateVerified)
					operation = SavePhase(operation, CollectionOperationPhase.CandidateRevisionAggregateVerified, CollectionOperationResultState.Pending);
				return new CollectionRevisionUpdateAggregateVerificationResult(operation, state, verification);
			}
		}

		public CollectionRevisionUpdateAggregateVerificationRecord LoadVerification(CollectionOperationIdentity operationIdentity,
			CollectionPlanIdentity expectedPlan)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			if (expectedPlan == null) throw new ArgumentNullException(nameof(expectedPlan));
			CollectionOperation operation = _operationStore.GetOperation(operationIdentity);
			if (operation == null || operation.Kind != CollectionOperationKind.UpdateRevision || operation.PlanIdentity == null ||
				!operation.PlanIdentity.Equals(expectedPlan) || operation.Phase != CollectionOperationPhase.CandidateRevisionAggregateVerified)
				throw new InvalidOperationException("The revision-update operation has no completed C10.8 aggregate verification boundary.");
			return LoadPersistedVerification(operation, expectedPlan);
		}

		private Dictionary<CollectionMemberKey, CollectionNativeModState> VerifyCandidateChildren(CollectionOperation operation,
			CollectionRevisionUpdatePlan updatePlan, CollectionRevisionUpdateOverridePreservationPlan preservation,
			CollectionNativeStateIndex state, GameStoragePathSet paths)
		{
			var result = new Dictionary<CollectionMemberKey, CollectionNativeModState>();
			var suppressed = new HashSet<CollectionMemberKey>(preservation.MembersWhoseCandidateMutationIsSuppressed);
			foreach (CollectionRevisionUpdateMemberPlan member in updatePlan.Members.Where(x =>
				CollectionRevisionUpdateAggregateVerifier.RequiresCandidateMutation(x) && !suppressed.Contains(x.MemberKey)))
			{
				CollectionNativeChildOperation child = RequireCommittedCandidateChild(operation,
					new CollectionOperationMemberReference(updatePlan.NewPlan.Revision, member.MemberKey));
				CollectionNativeChildRecoveryManifest manifest = _manifestStore.GetManifest(operation, child);
				if (manifest == null || manifest.ExecutionEvidence == null ||
					!manifest.ExecutionEvidence.ReviewedEffects.MemberKey.Equals(member.MemberKey) ||
					!manifest.ExecutionEvidence.ReviewedEffects.RecipeIdentity.Equals(member.NewMember.RecipeIdentity))
					throw new InvalidDataException("A committed C10 candidate child is missing exact reviewed execution evidence.");
				CollectionNativeModState native;
				string failure;
				if (!CollectionNativeChildRestartReconciliationCoordinator.TryVerifyCommittedState(manifest, manifest.ExecutionEvidence, state,
					_services.ModManager.ModRepository == null ? null : _services.ModManager.ModRepository.GameDomainName,
					paths.InstallInfoPath, _services.ModManager.GameMode, out native, out failure))
					throw new InvalidOperationException("Aggregate verification failed for candidate member '" + member.MemberKey + "': " + (failure ?? "unknown native mismatch"));
				result.Add(member.MemberKey, native);
			}

			foreach (CollectionNativeChildOperation child in operation.NativeChildren.Where(x => x.Action == CollectionNativeChildAction.ActivateOrReinstall))
				if (!child.Member.Revision.Equals(updatePlan.NewPlan.Revision) || !result.ContainsKey(child.Member.MemberKey))
					throw new InvalidOperationException("The revision-update journal contains an unexpected candidate activation/reinstall child outside the approved mutation set.");
			return result;
		}

		/// <summary>Requires one final committed candidate attempt, permitting only earlier reconciled rollback or not-started attempts of that exact member.</summary>
		internal static CollectionNativeChildOperation RequireCommittedCandidateChild(CollectionOperation operation,
			CollectionOperationMemberReference member)
		{
			if (operation.Kind != CollectionOperationKind.UpdateRevision || !member.Revision.Equals(operation.Revision))
				throw new InvalidOperationException("Candidate child verification requires the exact revision-update operation and member revision.");
			List<CollectionNativeChildOperation> children = operation.NativeChildren.Where(x =>
				x.Action == CollectionNativeChildAction.ActivateOrReinstall && x.Member.Equals(member)).OrderBy(x => x.Sequence).ToList();
			CollectionNativeChildOperation committed = children.LastOrDefault();
			if (committed == null || !committed.IsReconciled || !committed.HasVerifiedCommittedNativeState ||
				children.Take(children.Count - 1).Any(x => !CollectionNativeChildPreparationCoordinator.IsRetryableRevisionUpdateAttempt(
					x, CollectionNativeChildWorkflowMode.RevisionUpdate)))
				throw new InvalidOperationException("One required candidate member lacks a final verified installation after safely reconciled retry attempts.");
			return committed;
		}

		private static void VerifyDeactivationChildren(CollectionOperation operation, CollectionRevisionUpdatePlan updatePlan,
			IReadOnlyDictionary<CollectionMemberKey, CollectionNativeModState> candidateNative, CollectionNativeStateIndex state)
		{
			foreach (CollectionNativeChildOperation child in operation.NativeChildren.Where(x => x.Action == CollectionNativeChildAction.Deactivate))
			{
				if (!child.IsReconciled || !child.HasVerifiedCommittedNativeState)
					throw new InvalidOperationException("An obsolete-revision deactivation child is not durably reconciled at aggregate verification.");
				CollectionRevisionUpdateMemberPlan member = updatePlan.Members.SingleOrDefault(x => x.MemberKey.Equals(child.Member.MemberKey));
				if (member == null || member.NewMember != null || member.Binding == null)
					throw new InvalidOperationException("The revision-update journal contains a deactivation child outside the reviewed removed-member set.");
				bool identityReusedByCandidate = candidateNative.Values.Any(x => x.Identity.Equals(member.Binding.NativeMod));
				if (!identityReusedByCandidate && state.Mods.ContainsKey(member.Binding.NativeMod))
					throw new InvalidOperationException("A durably removed old-revision native member is present again without a verified candidate child reusing that identity.");
			}
		}

		private void ValidatePersistedCustomization(CollectionRevisionUpdateReviewedIntent reviewed,
			CollectionRevisionUpdateOverridePreservationPlan preservation)
		{
			CollectionTargetAssociation association = _associationStore.GetAssociation(reviewed.AssociationId);
			if (association == null || !association.Revision.Equals(reviewed.OldRevision) || !association.Target.Equals(reviewed.Target) ||
				association.State != reviewed.AssociationState)
				throw new InvalidOperationException("The old Collection association changed before C10.8 aggregate verification.");
			Dictionary<Guid, UserOverride> expected = preservation.Actions.ToDictionary(x => x.UserOverride.OverrideId, x => x.UserOverride);
			Dictionary<Guid, UserOverride> current = _associationStore.GetOverrides(reviewed.AssociationId).ToDictionary(x => x.OverrideId);
			if (!new HashSet<Guid>(expected.Keys).SetEquals(current.Keys))
				throw new InvalidOperationException("The persisted C9 override set changed before C10.8 aggregate verification.");
			foreach (Guid id in expected.Keys)
				if (!OverrideEquals(expected[id], current[id]))
					throw new InvalidOperationException("A persisted C9 override changed before C10.8 aggregate verification.");
		}

		private void VerifyTypedOverrides(CollectionOperation operation, CollectionRevisionUpdatePlan updatePlan,
			CollectionRevisionUpdateOverridePreservationPlan preservation, CollectionNativeStateIndex state)
		{
			foreach (CollectionRevisionUpdateOverridePreservationAction action in preservation.Actions.Where(x => x.RequiresPostCandidateReapply))
			{
				if (action.UserOverride.Requirement.Aspect != CollectionRequirementAspect.MemberEnabledState || action.UserOverride.Requirement.MemberKey == null)
					throw new InvalidOperationException("C10.8 encountered a replayable override without a typed verification adapter.");
				bool desired;
				if (!CollectionMemberRequirementStates.TryGetEnabled(action.UserOverride.UserChosenState, out desired))
					throw new InvalidOperationException("C10.8 encountered a non-canonical member enabled-state override.");
				CollectionNativeModState native = ResolveCurrentNativeMember(operation, updatePlan,
					action.UserOverride.Requirement.MemberKey, state);
				List<IMod> live = _services.ModManager.InstallationLog.ActiveMods.Where(x => x != null &&
					StringComparer.OrdinalIgnoreCase.Equals(_services.ModManager.InstallationLog.GetModKey(x), native.Identity.NativeModKey)).ToList();
				if (live.Count != 1 || _services.ModManager.InstallationLog.GetModInstallMethod(live[0]) != ModInstallMethod.Virtual)
					throw new InvalidOperationException("The reviewed enabled-state override no longer resolves to one Virtual native member.");
				IVirtualModActivator activator = _services.ModManager.VirtualModActivator;
				if (activator == null) throw new InvalidOperationException("The Virtual Mod Activator is unavailable during C10.8 override verification.");
				bool enabled = activator.ActiveModList.Contains((Path.GetFileName(live[0].Filename) ?? String.Empty).ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);
				if (enabled != desired) throw new InvalidOperationException("The qualified C9 enabled-state override no longer matches authoritative Virtual state.");
			}
		}

		private CollectionNativeModState ResolveCurrentNativeMember(CollectionOperation operation, CollectionRevisionUpdatePlan updatePlan,
			CollectionMemberKey memberKey, CollectionNativeStateIndex state)
		{
			CollectionMemberKey candidateMemberKey = updatePlan.MemberCorrelations.ResolveCandidateMemberKey(memberKey);
			CollectionRevisionUpdateMemberPlan member = updatePlan.Members.Single(x => x.MemberKey.Equals(candidateMemberKey));
			List<CollectionNativeChildOperation> children = operation.NativeChildren.Where(x => x.Action == CollectionNativeChildAction.ActivateOrReinstall &&
				x.Member.MemberKey.Equals(candidateMemberKey) && x.IsReconciled && x.HasVerifiedCommittedNativeState).ToList();
			if (children.Count > 1) throw new InvalidOperationException("Multiple committed candidate children exist for one C10.8 member.");
			if (children.Count == 0)
			{
				if (member.Binding == null) throw new InvalidOperationException("A preserved C10.8 override member has no reviewed native binding.");
				CollectionNativeModState existing;
				if (!state.Mods.TryGetValue(member.Binding.NativeMod, out existing)) throw new InvalidOperationException("The preserved override member is missing from native state.");
				return existing;
			}
			CollectionNativeChildRecoveryManifest manifest = _manifestStore.GetManifest(operation, children[0]);
			if (manifest == null || manifest.ExecutionEvidence == null) throw new InvalidDataException("The C10.8 override target child is missing execution evidence.");
			CollectionNativeModState resolved;
			string failure;
			if (!CollectionNativeChildRestartReconciliationCoordinator.TryVerifyCommittedState(manifest, manifest.ExecutionEvidence, state,
				_services.ModManager.ModRepository == null ? null : _services.ModManager.ModRepository.GameDomainName,
				_services.ModManager.GameMode.GameModeEnvironmentInfo.InstallInfoDirectory, _services.ModManager.GameMode, out resolved, out failure, false))
				throw new InvalidOperationException("The C10.8 override target no longer matches its committed candidate evidence: " + (failure ?? "unknown mismatch"));
			return resolved;
		}

		private CollectionRevisionUpdateAggregateVerificationRecord PersistOrValidateVerification(CollectionOperation operation,
			CollectionPlanIdentity planIdentity, string reviewFingerprint, CollectionCurrentStateFingerprint stateFingerprint)
		{
			CollectionRevisionUpdateAggregateVerificationRecord existing = TryLoadPersistedVerification(operation, planIdentity);
			if (existing != null)
			{
				if (!StringComparer.Ordinal.Equals(existing.ReviewFingerprint, reviewFingerprint) || !existing.StateFingerprint.Equals(stateFingerprint))
					throw new InvalidOperationException("The authoritative native state changed after the durable C10.8 verification boundary was sealed.");
				return existing;
			}
			var dto = new VerificationDto
			{
				Format = VerificationFormat,
				OperationId = operation.Identity.OperationId.ToString("D"),
				PlanId = planIdentity.PlanId.ToString("D"),
				PlanVersion = planIdentity.Version,
				ReviewFingerprint = reviewFingerprint,
				StateFingerprintFormatVersion = stateFingerprint.FormatVersion,
				StateFingerprintValue = stateFingerprint.Value
			};
			byte[] payload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(dto, Formatting.None));
			CollectionsRetainedArtifact artifact;
			using (var stream = new MemoryStream(payload, false)) artifact = _artifactStore.Publish(stream);
			_referenceStore.AcquireExclusiveRoleReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation,
				operation.Identity.OperationId.ToString("D"), VerificationRole);
			return new CollectionRevisionUpdateAggregateVerificationRecord(operation.Identity.OperationId, planIdentity,
				reviewFingerprint, stateFingerprint);
		}

		private CollectionRevisionUpdateAggregateVerificationRecord LoadPersistedVerification(CollectionOperation operation,
			CollectionPlanIdentity expectedPlan)
		{
			CollectionRevisionUpdateAggregateVerificationRecord value = TryLoadPersistedVerification(operation, expectedPlan);
			if (value == null) throw new InvalidDataException("The C10.8 aggregate verification record is missing.");
			return value;
		}

		private CollectionRevisionUpdateAggregateVerificationRecord TryLoadPersistedVerification(CollectionOperation operation,
			CollectionPlanIdentity expectedPlan)
		{
			CollectionsRetainedArtifactReferenceRecord reference = _referenceStore.GetReferenceForOwnerRole(CollectionsRetainedArtifactOwnerKind.Operation,
				operation.Identity.OperationId.ToString("D"), VerificationRole);
			if (reference == null) return null;
			if (!_artifactStore.VerifyArtifact(reference.ArtifactId)) throw new InvalidDataException("The C10.8 aggregate verification artifact is missing or corrupted.");
			VerificationDto dto;
			using (Stream stream = _artifactStore.OpenRead(reference.ArtifactId))
			using (var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, false))
				dto = JsonConvert.DeserializeObject<VerificationDto>(reader.ReadToEnd());
			Guid operationId;
			Guid planId;
			if (dto == null || !StringComparer.Ordinal.Equals(dto.Format, VerificationFormat) || !Guid.TryParse(dto.OperationId, out operationId) ||
				operationId != operation.Identity.OperationId || !Guid.TryParse(dto.PlanId, out planId) || planId != expectedPlan.PlanId ||
				dto.PlanVersion != expectedPlan.Version || String.IsNullOrWhiteSpace(dto.ReviewFingerprint) || String.IsNullOrWhiteSpace(dto.StateFingerprintValue))
				throw new InvalidDataException("The C10.8 aggregate verification artifact does not match the exact operation/plan identity.");
			return new CollectionRevisionUpdateAggregateVerificationRecord(operationId, expectedPlan, dto.ReviewFingerprint,
				new CollectionCurrentStateFingerprint(dto.StateFingerprintFormatVersion, dto.StateFingerprintValue));
		}

		private CollectionOperation RequireOperation(CollectionOperationIdentity identity, ResolvedCollectionPlan candidatePlan)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Kind != CollectionOperationKind.UpdateRevision || operation.IsTerminal || operation.PlanIdentity == null ||
				!operation.PlanIdentity.Equals(candidatePlan.Identity) || operation.Revision == null || !operation.Revision.Equals(candidatePlan.Revision) ||
				!operation.Target.Equals(candidatePlan.Target))
				throw new InvalidOperationException("The C10.8 request does not belong to the active exact revision-update operation.");
			if (operation.Phase != CollectionOperationPhase.QualifiedRevisionOverridesVerified &&
				operation.Phase != CollectionOperationPhase.VerifyingCandidateRevisionAggregate &&
				operation.Phase != CollectionOperationPhase.CandidateRevisionAggregateVerified)
				throw new InvalidOperationException("C10.8 requires the verified override boundary before aggregate verification.");
			return operation;
		}

		private CollectionOperation SavePhase(CollectionOperation operation, CollectionOperationPhase phase, CollectionOperationResultState result)
		{
			if (operation.CheckpointSequence == Int64.MaxValue) throw new InvalidOperationException("The Collection operation checkpoint sequence cannot be incremented further.");
			var updated = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target, operation.Revision,
				operation.PlanIdentity, operation.CheckpointSequence + 1, phase, result, operation.NativeChildren);
			_operationStore.SaveOperation(updated);
			return _operationStore.GetOperation(operation.Identity);
		}

		private static bool OverrideEquals(UserOverride left, UserOverride right)
		{
			return left != null && right != null && left.OverrideId == right.OverrideId && left.Requirement.Equals(right.Requirement) &&
				left.BaselineState.Equals(right.BaselineState) && left.UserChosenState.Equals(right.UserChosenState) && StringComparer.Ordinal.Equals(left.Note, right.Note);
		}
	}
}
