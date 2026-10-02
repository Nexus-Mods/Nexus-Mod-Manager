using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Identifies the native service used to apply one reviewed C6.4 file winner.</summary>
	public enum CollectionReviewedFileWinnerDispatchKind
	{
		Unknown = 0,
		Promoted = 1,
		Virtual = 2
	}

	/// <summary>Describes how one reviewed file winner reached its verified native state.</summary>
	public enum CollectionReviewedFileWinnerOutcome
	{
		Unknown = 0,
		AlreadySatisfied = 1,
		SwitchedAndVerified = 2,
		RecoveredCommitted = 3
	}

	/// <summary>One verified C6.15.11 file-winner reconciliation result.</summary>
	public sealed class CollectionReviewedFileWinnerResult
	{
		internal CollectionReviewedFileWinnerResult(ModDeploymentTarget target, string desiredOwnerKey,
			CollectionReviewedFileWinnerDispatchKind dispatchKind, CollectionReviewedFileWinnerOutcome outcome)
		{
			Target = target ?? throw new ArgumentNullException(nameof(target));
			DesiredOwnerKey = CollectionIdentityValidation.RequireOpaqueToken(desiredOwnerKey, nameof(desiredOwnerKey));
			if (!Enum.IsDefined(typeof(CollectionReviewedFileWinnerDispatchKind), dispatchKind) || dispatchKind == CollectionReviewedFileWinnerDispatchKind.Unknown)
				throw new ArgumentOutOfRangeException(nameof(dispatchKind));
			if (!Enum.IsDefined(typeof(CollectionReviewedFileWinnerOutcome), outcome) || outcome == CollectionReviewedFileWinnerOutcome.Unknown)
				throw new ArgumentOutOfRangeException(nameof(outcome));
			DispatchKind = dispatchKind;
			Outcome = outcome;
		}

		public ModDeploymentTarget Target { get; }
		public string DesiredOwnerKey { get; }
		public CollectionReviewedFileWinnerDispatchKind DispatchKind { get; }
		public CollectionReviewedFileWinnerOutcome Outcome { get; }
	}

	/// <summary>Result of reconciling all executable reviewed C6.4 file winners.</summary>
	public sealed class CollectionReviewedFileWinnerReconciliationResult
	{
		private readonly ReadOnlyCollection<CollectionReviewedFileWinnerResult> _winners;

		internal CollectionReviewedFileWinnerReconciliationResult(IEnumerable<CollectionReviewedFileWinnerResult> winners)
		{
			_winners = new ReadOnlyCollection<CollectionReviewedFileWinnerResult>((winners ?? throw new ArgumentNullException(nameof(winners))).ToList());
		}

		public ReadOnlyCollection<CollectionReviewedFileWinnerResult> Winners { get { return _winners; } }
	}

	/// <summary>Raised when a persisted winner intent cannot be reduced to its reviewed preimage or reviewed winner.</summary>
	public sealed class CollectionReviewedFileWinnerRecoveryRequiredException : InvalidOperationException
	{
		public CollectionReviewedFileWinnerRecoveryRequiredException(string message) : base(message) { }
	}

	/// <summary>
	/// Executes the reviewed C6.4 per-path winner after all required writer owners exist, using native ownership services only.
	/// </summary>
	/// <remarks>
	/// Intent/preimage bytes are retained before mutation. A verified marker is published only after authoritative native state
	/// reports the reviewed owner. The coordinator never uses install order as winner policy and never invokes manual-drift hooks.
	/// </remarks>
	public sealed class CollectionReviewedFileWinnerReconciliationCoordinator
	{
		private const string IntentFormat = "nmm-ce.collections.file-winner-intent/2";
		private const string LegacyIntentFormat = "nmm-ce.collections.file-winner-intent/1";
		private const string IntentRolePrefix = "file-winner-intent-";
		private const string VerifiedRolePrefix = "file-winner-verified-";

		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsResolvedPlanStore _planStore;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionsRetainedArtifactReferenceStore _referenceStore;
		private readonly IProfileManager _profileManager;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;
		private readonly IModDeploymentManager _fixedDeploymentManager;
		private readonly IVirtualDeploymentService _fixedVirtualDeploymentService;
		private readonly Func<CollectionTargetIdentity, CollectionNativeStateIndex> _captureState;
		private readonly Action _updateProfileDeployment;

		/// <summary>Creates the production reviewed-winner reconciler over existing C4/native ownership services.</summary>
		public CollectionReviewedFileWinnerReconciliationCoordinator(ServiceManager services, IProfileManager profileManager,
			GameStorageService gameStorageService, CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore,
			CollectionsAssociationStore associationStore, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore)
			: this(services, profileManager, gameStorageService, operationStore, planStore, associationStore, artifactStore, referenceStore,
				CollectionTargetMutationLeaseManager.Shared,
				new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services))
		{
		}

		internal CollectionReviewedFileWinnerReconciliationCoordinator(ServiceManager services, IProfileManager profileManager,
			GameStorageService gameStorageService, CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore,
			CollectionsAssociationStore associationStore, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore, CollectionTargetMutationLeaseManager mutationLeaseManager,
			CollectionTargetOwnershipAuthorityValidator authorityValidator)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_planStore = planStore ?? throw new ArgumentNullException(nameof(planStore));
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
			_artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
			_referenceStore = referenceStore ?? throw new ArgumentNullException(nameof(referenceStore));
			_mutationLeaseManager = mutationLeaseManager ?? throw new ArgumentNullException(nameof(mutationLeaseManager));
			_authorityValidator = authorityValidator ?? throw new ArgumentNullException(nameof(authorityValidator));
			if (_services.ModManager == null) throw new InvalidOperationException("C6.15.11 requires the live ModManager service.");
			if (_services.ModManager.DeploymentManager == null) throw new InvalidOperationException("C6.15.11 requires the native deployment manager.");
			_fixedDeploymentManager = null;
			_fixedVirtualDeploymentService = null;
			_captureState = target => new CollectionNativeStateReader(() => _services.ModManager.InstallationLog,
				_services.ModManager.VirtualModActivator, _services.PluginManager, _services.ModManager.GameMode, _associationStore).Capture(target);
			_updateProfileDeployment = () => _profileManager.UpdateCurrentDeploymentManifest();
		}

		/// <summary>Test seam over already validated native authority; production callers use <see cref="ReconcileAsync"/>.</summary>
		internal CollectionReviewedFileWinnerReconciliationCoordinator(CollectionsOperationStore operationStore,
			CollectionsResolvedPlanStore planStore, CollectionsAssociationStore associationStore,
			CollectionsRetainedArtifactStore artifactStore, CollectionsRetainedArtifactReferenceStore referenceStore,
			IModDeploymentManager deploymentManager, IVirtualDeploymentService virtualDeploymentService,
			Func<CollectionTargetIdentity, CollectionNativeStateIndex> captureState, Action updateProfileDeployment)
		{
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_planStore = planStore ?? throw new ArgumentNullException(nameof(planStore));
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
			_artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
			_referenceStore = referenceStore ?? throw new ArgumentNullException(nameof(referenceStore));
			_fixedDeploymentManager = deploymentManager ?? throw new ArgumentNullException(nameof(deploymentManager));
			_fixedVirtualDeploymentService = virtualDeploymentService ?? throw new ArgumentNullException(nameof(virtualDeploymentService));
			_captureState = captureState ?? throw new ArgumentNullException(nameof(captureState));
			_updateProfileDeployment = updateProfileDeployment ?? throw new ArgumentNullException(nameof(updateProfileDeployment));
		}

		/// <summary>
		/// Reconciles every executable reviewed file winner while holding the canonical target mutation reservation.
		/// </summary>
		public async Task<CollectionReviewedFileWinnerReconciliationResult> ReconcileAsync(CollectionOperationIdentity operationIdentity,
			ResolvedCollectionPlan plan, CollectionMemberMatchSet matches, CollectionConflictImpactPlan impactPlan,
			GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			if (_services == null || _gameStorageService == null || _mutationLeaseManager == null || _authorityValidator == null)
				throw new InvalidOperationException("The production C6.15.11 entry point is unavailable on the test-only coordinator.");
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			ValidateInputs(operationIdentity, plan, matches, impactPlan, false);
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(plan.Target)) throw new InvalidOperationException("The live canonical target no longer matches the reviewed Collection plan.");
			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				cancellationToken.ThrowIfCancellationRequested();
				_authorityValidator.ValidateAndReload(lease, authority, paths);
				return ReconcileValidated(operationIdentity, plan, matches, impactPlan, cancellationToken, false);
			}
		}

		/// <summary>Reconciles reviewed winners without cancellation.</summary>
		public Task<CollectionReviewedFileWinnerReconciliationResult> ReconcileAsync(CollectionOperationIdentity operationIdentity,
			ResolvedCollectionPlan plan, CollectionMemberMatchSet matches, CollectionConflictImpactPlan impactPlan,
			GameStoragePathSet paths)
		{
			return ReconcileAsync(operationIdentity, plan, matches, impactPlan, paths, CancellationToken.None);
		}

		/// <summary>Reconciles C8.6 reviewed replacement winners after all required incoming owners exist.</summary>
		internal async Task<CollectionReviewedFileWinnerReconciliationResult> ReconcileReplacementAsync(
			CollectionOperationIdentity operationIdentity, ResolvedCollectionPlan executionPlan, CollectionMemberMatchSet liveMatches,
			CollectionConflictImpactPlan impactPlan, GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			if (_services == null || _gameStorageService == null || _mutationLeaseManager == null || _authorityValidator == null)
				throw new InvalidOperationException("The production reviewed-winner entry point is unavailable on the test-only coordinator.");
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			ValidateInputs(operationIdentity, executionPlan, liveMatches, impactPlan, true);
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(executionPlan.Target)) throw new InvalidOperationException("The live canonical target no longer matches the replacement execution plan.");
			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				cancellationToken.ThrowIfCancellationRequested();
				_authorityValidator.ValidateAndReload(lease, authority, paths);
				return ReconcileValidated(operationIdentity, executionPlan, liveMatches, impactPlan, cancellationToken, true);
			}
		}

		internal CollectionReviewedFileWinnerReconciliationResult ReconcileValidated(CollectionOperationIdentity operationIdentity,
			ResolvedCollectionPlan plan, CollectionMemberMatchSet matches, CollectionConflictImpactPlan impactPlan,
			CancellationToken cancellationToken)
		{
			return ReconcileValidated(operationIdentity, plan, matches, impactPlan, cancellationToken, false);
		}

		private CollectionReviewedFileWinnerReconciliationResult ReconcileValidated(CollectionOperationIdentity operationIdentity,
			ResolvedCollectionPlan plan, CollectionMemberMatchSet matches, CollectionConflictImpactPlan impactPlan,
			CancellationToken cancellationToken, bool replacementExecution)
		{
			ValidateInputs(operationIdentity, plan, matches, impactPlan, replacementExecution);
			IModDeploymentManager deploymentManager = GetCurrentDeploymentManager();
			IVirtualDeploymentService virtualDeploymentService = GetCurrentVirtualDeploymentService(deploymentManager);
			var results = new List<CollectionReviewedFileWinnerResult>();
			foreach (CollectionFileImpact impact in impactPlan.FileImpacts
				.Where(x => x.PreserveCurrentOwner || (x.PlannedWinner != null && x.Writers.Count > 1))
				.OrderBy(x => (int)x.Target.Root).ThenBy(x => x.Target.RelativePath, StringComparer.OrdinalIgnoreCase))
			{
				cancellationToken.ThrowIfCancellationRequested();
				CollectionNativeStateIndex state = _captureState(plan.Target);
				CollectionNativeFileState file = RequireLiveFile(state, impact.Target);
				Dictionary<CollectionMemberKey, string> writerOwners = ResolveWriterOwners(plan, matches, impact, file);
				string desiredOwner;
				string fallbackOwner = null;
				if (impact.PreserveCurrentOwner)
				{
					desiredOwner = CollectionIdentityValidation.RequireOpaqueToken(impact.CurrentOwnerKey, nameof(impact.CurrentOwnerKey));
					if (!GetRealOwnerKeys(file).Contains(desiredOwner, StringComparer.OrdinalIgnoreCase))
						throw new CollectionReviewedFileWinnerRecoveryRequiredException("The reviewed existing managed file winner is no longer present in the native owner stack.");
					if (impact.Writers.Count > 1)
						fallbackOwner = writerOwners[impact.PlannedWinner];
				}
				else
				{
					desiredOwner = writerOwners[impact.PlannedWinner];
				}
				CollectionReviewedFileWinnerDispatchKind dispatch = ResolveDispatch(file);
				List<string> currentOwners = GetOwnerPreimage(file);
				CollectionReviewedFileWinnerIntent intent = LoadIntent(operationIdentity, plan, impact.Target);
				if (intent != null)
				{
					ValidateIntent(intent, operationIdentity, plan, impact.Target, desiredOwner, fallbackOwner, dispatch);
					if (OwnerStacksEqual(currentOwners, GetExpectedPostimage(intent)))
					{
						if (!StringComparer.OrdinalIgnoreCase.Equals(file.EffectiveOwnerKey, desiredOwner))
							throw new CollectionReviewedFileWinnerRecoveryRequiredException("The reviewed-winner postimage does not expose the reviewed final owner.");
						if (!IsVerified(operationIdentity, intent))
						{
							_updateProfileDeployment();
							MarkVerified(operationIdentity, intent);
						}
						results.Add(new CollectionReviewedFileWinnerResult(impact.Target, desiredOwner, dispatch,
							CollectionReviewedFileWinnerOutcome.RecoveredCommitted));
						continue;
					}
					if (IsVerified(operationIdentity, intent))
						throw new CollectionReviewedFileWinnerRecoveryRequiredException("The durable winner checkpoint says the switch was verified but native state no longer matches its exact postimage.");
					List<string> intermediate = GetIntermediatePostimage(intent);
					if (!OwnerStacksEqual(currentOwners, intent.PreimageOwners) &&
						(intermediate == null || !OwnerStacksEqual(currentOwners, intermediate)))
						throw new CollectionReviewedFileWinnerRecoveryRequiredException("The native owner stack no longer matches the durable reviewed-winner preimage, intermediate fallback state, or final postimage.");
				}
				else
				{
					bool alreadyExact = StringComparer.OrdinalIgnoreCase.Equals(file.EffectiveOwnerKey, desiredOwner) &&
						(fallbackOwner == null || (currentOwners.Count >= 2 &&
						StringComparer.OrdinalIgnoreCase.Equals(currentOwners[currentOwners.Count - 2], fallbackOwner)));
					if (alreadyExact)
					{
						results.Add(new CollectionReviewedFileWinnerResult(impact.Target, desiredOwner, dispatch,
							CollectionReviewedFileWinnerOutcome.AlreadySatisfied));
						continue;
					}
					if (!StringComparer.OrdinalIgnoreCase.Equals(file.EffectiveOwnerKey, desiredOwner) &&
						!writerOwners.Values.Contains(file.EffectiveOwnerKey, StringComparer.OrdinalIgnoreCase))
						throw new CollectionReviewedFileWinnerRecoveryRequiredException(impact.PreserveCurrentOwner
							? "The current native file winner is neither the reviewed preserved owner nor one of the reviewed incoming writer owners."
							: "The current native file winner is no longer one of the reviewed incoming writer owners.");
					intent = new CollectionReviewedFileWinnerIntent(operationIdentity, plan.Identity, impact.Target, dispatch,
						file.EffectiveOwnerKey, desiredOwner, fallbackOwner, currentOwners);
					SaveIntent(intent);
				}

				ApplyWinner(intent, deploymentManager, virtualDeploymentService, currentOwners);
				CollectionNativeStateIndex after = _captureState(plan.Target);
				CollectionNativeFileState verified = RequireLiveFile(after, impact.Target);
				if (!StringComparer.OrdinalIgnoreCase.Equals(verified.EffectiveOwnerKey, desiredOwner) ||
					!OwnerStacksEqual(GetOwnerPreimage(verified), GetExpectedPostimage(intent)))
					throw new CollectionReviewedFileWinnerRecoveryRequiredException("The native owner switch returned without establishing the exact reviewed file-winner postimage.");
				_updateProfileDeployment();
				MarkVerified(operationIdentity, intent);
				results.Add(new CollectionReviewedFileWinnerResult(impact.Target, desiredOwner, dispatch,
					CollectionReviewedFileWinnerOutcome.SwitchedAndVerified));
			}
			return new CollectionReviewedFileWinnerReconciliationResult(results);
		}

		private void ValidateInputs(CollectionOperationIdentity operationIdentity, ResolvedCollectionPlan plan,
			CollectionMemberMatchSet matches, CollectionConflictImpactPlan impactPlan, bool replacementExecution)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			if (plan == null) throw new ArgumentNullException(nameof(plan));
			if (matches == null) throw new ArgumentNullException(nameof(matches));
			if (impactPlan == null) throw new ArgumentNullException(nameof(impactPlan));
			CollectionExecutionPolicyKind requiredPolicy = replacementExecution
				? CollectionExecutionPolicyKind.ReplaceCurrentManagedSetup : CollectionExecutionPolicyKind.InstallIntoCurrentSetup;
			if (plan.Policy.Kind != requiredPolicy)
				throw new ArgumentException(replacementExecution ? "C8.6 requires an explicit replacement execution plan." : "C6.15.11 only reconciles additive Collection plans.", nameof(plan));
			if (!matches.PlanIdentity.Equals(plan.Identity) || !matches.Target.Equals(plan.Target) ||
				!impactPlan.PlanIdentity.Equals(plan.Identity) || !impactPlan.Target.Equals(plan.Target))
				throw new ArgumentException("Winner reconciliation inputs do not belong to the exact reviewed Collection plan.");
			if (!impactPlan.IsReady) throw new InvalidOperationException("Only a Ready C6.4 impact plan has executable reviewed winners.");
			if (matches.HasBlockedMembers || matches.HasAcquisitionRequired)
				throw new InvalidOperationException("C6.15.11 requires the complete selected member closure to be resolved.");

			CollectionOperation operation = _operationStore.GetOperation(operationIdentity);
			bool correctOperation = replacementExecution
				? operation != null && operation.Kind == CollectionOperationKind.ReplaceCurrentManagedSetup && operation.Phase == CollectionOperationPhase.InstallingIncomingNativeChildren
				: operation != null && operation.Kind == CollectionOperationKind.ApplyResolvedPlan &&
					(operation.Phase == CollectionOperationPhase.ApplyingNativeChildren || operation.Phase == CollectionOperationPhase.PausedAtSafeBoundary || operation.Phase == CollectionOperationPhase.Recovering);
			if (!correctOperation || operation.ResultState != CollectionOperationResultState.Pending || operation.PlanIdentity == null || !operation.PlanIdentity.Equals(plan.Identity) ||
				operation.Revision == null || !operation.Revision.Equals(plan.Revision) || !operation.Target.Equals(plan.Target))
				throw new InvalidOperationException(replacementExecution ? "C8.6 requires the active replacement incoming phase." : "C6.15.11 requires the active operation for the exact reviewed additive plan.");
			if (operation.NativeChildren.Any(x => !x.IsReconciled))
				throw new InvalidOperationException("Reviewed file winners may be reconciled only after every submitted native child reached a reconciled safe boundary.");
			CollectionResolvedPlanRecord persisted = _planStore.GetPlan(plan.Identity);
			if (persisted == null || !persisted.Revision.Equals(plan.Revision) || !persisted.Target.Equals(plan.Target) || persisted.PolicyKind != requiredPolicy)
				throw new InvalidOperationException("The exact reviewed Collection plan is not durably persisted.");
			if (!replacementExecution)
			{
				if (!StringComparer.Ordinal.Equals(persisted.PayloadFormat, CollectionReviewedWorkflowSnapshotCodec.PayloadFormat))
					throw new InvalidOperationException("C6.15.11 requires the exact durable reviewed-workflow v2 snapshot.");
				CollectionReviewedWorkflowSnapshot reviewed = CollectionReviewedWorkflowSnapshotCodec.Deserialize(persisted.Payload);
				ValidateReviewedWinners(reviewed, plan, impactPlan);
			}
			else
			{
				if (!StringComparer.Ordinal.Equals(persisted.PayloadFormat, CollectionReplacementReviewedIntentCodec.PayloadFormat))
					throw new InvalidOperationException("C8.6 requires the exact durable replacement reviewed intent with explicit file-winner approvals.");
				ValidateReplacementReviewedWinners(CollectionReplacementReviewedIntentCodec.Deserialize(persisted.Payload), impactPlan);
			}
		}

		private static void ValidateReplacementReviewedWinners(CollectionReplacementReviewedIntent intent,
			CollectionConflictImpactPlan impactPlan)
		{
			Dictionary<ModDeploymentTarget, CollectionReplacementFileWinnerApproval> approved = intent.FileWinnerApprovals
				.ToDictionary(x => x.Target);
			List<CollectionFileImpact> multiWriter = impactPlan.FileImpacts.Where(x => x.Writers.Count > 1).ToList();
			foreach (CollectionFileImpact impact in multiWriter)
			{
				CollectionReplacementFileWinnerApproval winner;
				if (impact.PlannedWinner == null || !approved.TryGetValue(impact.Target, out winner) ||
					!winner.WinnerMemberKey.Equals(impact.PlannedWinner))
					throw new InvalidOperationException("C8.6 will not reconcile a multi-writer file target unless its exact planned winner was durably approved by C8.3 review.");
			}
			if (approved.Keys.Any(target => multiWriter.All(x => !x.Target.Equals(target))))
				throw new InvalidOperationException("The durable replacement review contains a file-winner approval that is not present in the current reviewed impact plan.");
		}

		private static void ValidateReviewedWinners(CollectionReviewedWorkflowSnapshot reviewed, ResolvedCollectionPlan plan,
			CollectionConflictImpactPlan impactPlan)
		{
			if (reviewed == null || !reviewed.Identity.Equals(plan.Identity) || !reviewed.Revision.Equals(plan.Revision) || !reviewed.Target.Equals(plan.Target))
				throw new InvalidDataException("The durable reviewed-workflow snapshot does not belong to the exact Collection plan.");
			if (reviewed.FileImpacts.Count != impactPlan.FileImpacts.Count)
				throw new InvalidDataException("The supplied C6.4 file impacts no longer match the durable reviewed workflow.");
			foreach (CollectionFileImpact impact in impactPlan.FileImpacts)
			{
				CollectionReviewedFileImpactSnapshot persisted = reviewed.FileImpacts.SingleOrDefault(x => x.Target.Equals(impact.Target));
				if (persisted == null || !Equals(persisted.PlannedWinner, impact.PlannedWinner) ||
					persisted.PreserveCurrentOwner != impact.PreserveCurrentOwner ||
					!StringComparer.OrdinalIgnoreCase.Equals(persisted.CurrentOwnerKey ?? String.Empty, impact.CurrentOwnerKey ?? String.Empty) ||
					persisted.Writers.Count != impact.Writers.Count ||
					!persisted.Writers.OrderBy(x => (int)x.Kind).ThenBy(x => x.Value, StringComparer.Ordinal)
						.SequenceEqual(impact.Writers.OrderBy(x => (int)x.Kind).ThenBy(x => x.Value, StringComparer.Ordinal)))
					throw new InvalidDataException("A C6.4 file-winner decision differs from the exact durable reviewed workflow.");
			}
		}

		private Dictionary<CollectionMemberKey, string> ResolveWriterOwners(ResolvedCollectionPlan plan,
			CollectionMemberMatchSet matches, CollectionFileImpact impact, CollectionNativeFileState file)
		{
			var actualOwners = new HashSet<string>(GetRealOwnerKeys(file), StringComparer.OrdinalIgnoreCase);
			CollectionsAssociationTargetSnapshot snapshot = _associationStore.GetTargetSnapshot(plan.Target);
			var result = new Dictionary<CollectionMemberKey, string>();
			foreach (CollectionMemberKey writer in impact.Writers)
			{
				ResolvedCollectionMemberPlan member = plan.SelectedMembers.Single(x => x.MemberKey.Equals(writer));
				var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				CollectionMemberMatchResult match;
				if (matches.MembersByKey.TryGetValue(writer, out match) && match.MatchedNativeMod != null)
					candidates.Add(match.MatchedNativeMod.Identity.NativeModKey);
				foreach (CollectionMemberBinding binding in snapshot.Bindings.Where(x => x.MemberKey.Equals(writer) && x.VerifiedRecipe.Equals(member.RecipeIdentity)))
					candidates.Add(binding.NativeMod.NativeModKey);
				candidates.IntersectWith(actualOwners);
				if (candidates.Count != 1)
					throw new InvalidOperationException("The reviewed Collection writer does not resolve to exactly one live native owner for the target.");
				result.Add(writer, candidates.Single());
			}
			return result;
		}

		private static CollectionNativeFileState RequireLiveFile(CollectionNativeStateIndex state, ModDeploymentTarget target)
		{
			CollectionNativeFileState file;
			if (state == null || !state.Files.TryGetValue(target, out file) || file == null || String.IsNullOrWhiteSpace(file.EffectiveOwnerKey))
				throw new CollectionReviewedFileWinnerRecoveryRequiredException("The reviewed native file target no longer has one authoritative managed winner.");
			return file;
		}

		private static CollectionReviewedFileWinnerDispatchKind ResolveDispatch(CollectionNativeFileState file)
		{
			if (file.Promoted) return CollectionReviewedFileWinnerDispatchKind.Promoted;
			if (file.RecordedByVirtualState && file.Target.Root == ModDeploymentRoot.Data)
				return CollectionReviewedFileWinnerDispatchKind.Virtual;
			throw new InvalidOperationException("The reviewed native file target cannot be reconciled by the supported promoted/Virtual owner-switch services.");
		}

		private static IEnumerable<string> GetRealOwnerKeys(CollectionNativeFileState file)
		{
			IEnumerable<CollectionNativeOwnerState> owners = file.Promoted ? file.DeploymentOwners : file.VirtualOwners;
			return owners.Where(x => x.Kind == CollectionNativeOwnerKind.NativeMod && !String.IsNullOrWhiteSpace(x.OwnerKey))
				.Select(x => x.OwnerKey).Distinct(StringComparer.Ordinal);
		}

		private static List<string> GetOwnerPreimage(CollectionNativeFileState file)
		{
			IEnumerable<CollectionNativeOwnerState> owners = file.Promoted ? file.DeploymentOwners : file.VirtualOwners;
			return owners.Where(x => x != null && !String.IsNullOrWhiteSpace(x.OwnerKey))
				.Select(x => x.OwnerKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}

		private static List<string> GetIntermediatePostimage(CollectionReviewedFileWinnerIntent intent)
		{
			if (String.IsNullOrWhiteSpace(intent.FallbackOwnerKey)) return null;
			return MoveOwnerToEnd(intent.PreimageOwners, intent.FallbackOwnerKey);
		}

		private static List<string> GetExpectedPostimage(CollectionReviewedFileWinnerIntent intent)
		{
			List<string> owners = String.IsNullOrWhiteSpace(intent.FallbackOwnerKey)
				? intent.PreimageOwners.ToList()
				: MoveOwnerToEnd(intent.PreimageOwners, intent.FallbackOwnerKey);
			return MoveOwnerToEnd(owners, intent.DesiredOwnerKey);
		}

		private static List<string> MoveOwnerToEnd(IEnumerable<string> source, string ownerKey)
		{
			var owners = source.Where(x => !StringComparer.OrdinalIgnoreCase.Equals(x, ownerKey)).ToList();
			owners.Add(ownerKey);
			return owners;
		}

		private static bool OwnerStacksEqual(IEnumerable<string> left, IEnumerable<string> right)
		{
			if (left == null || right == null) return false;
			return left.SequenceEqual(right, StringComparer.OrdinalIgnoreCase);
		}

		private void ApplyWinner(CollectionReviewedFileWinnerIntent intent, IModDeploymentManager deploymentManager,
			IVirtualDeploymentService virtualDeploymentService, IReadOnlyList<string> currentOwners)
		{
			List<string> intermediate = GetIntermediatePostimage(intent);
			if (intermediate != null && !OwnerStacksEqual(currentOwners, intermediate))
				ApplyOwner(intent, intent.FallbackOwnerKey, deploymentManager, virtualDeploymentService);
			ApplyOwner(intent, intent.DesiredOwnerKey, deploymentManager, virtualDeploymentService);
		}

		private static void ApplyOwner(CollectionReviewedFileWinnerIntent intent, string ownerKey,
			IModDeploymentManager deploymentManager, IVirtualDeploymentService virtualDeploymentService)
		{
			if (intent.DispatchKind == CollectionReviewedFileWinnerDispatchKind.Promoted)
			{
				deploymentManager.SwitchPromotedOwner(intent.Target, ownerKey);
				return;
			}
			VirtualFileOwnerSwitchResult result = virtualDeploymentService.SwitchFileOwner(intent.Target.RelativePath, ownerKey);
			if (result == null || !result.Success || !StringComparer.OrdinalIgnoreCase.Equals(result.SelectedOwnerKey, ownerKey))
				throw new InvalidOperationException("The native Virtual owner switch did not report the reviewed owner as selected.", result == null ? null : result.Failure);
		}

		/// <summary>Resolves the deployment manager from the current post-reload native service graph.</summary>
		private IModDeploymentManager GetCurrentDeploymentManager()
		{
			if (_services == null)
				return _fixedDeploymentManager;
			if (_services.ModManager == null || _services.ModManager.DeploymentManager == null)
				throw new InvalidOperationException("The current native deployment manager is unavailable after authority reload.");
			return _services.ModManager.DeploymentManager;
		}

		/// <summary>Creates the Virtual owner-switch service against the same current deployment manager.</summary>
		private IVirtualDeploymentService GetCurrentVirtualDeploymentService(IModDeploymentManager deploymentManager)
		{
			if (_services == null)
				return _fixedVirtualDeploymentService;
			return new VirtualDeploymentService(_services.ModManager.VirtualModActivator, deploymentManager);
		}

		private CollectionReviewedFileWinnerIntent LoadIntent(CollectionOperationIdentity operationIdentity, ResolvedCollectionPlan plan, ModDeploymentTarget target)
		{
			string role = GetIntentRole(target);
			CollectionsRetainedArtifactReferenceRecord reference = _referenceStore.GetReferenceForOwnerRole(CollectionsRetainedArtifactOwnerKind.Operation,
				operationIdentity.ToString(), role);
			if (reference == null) return null;
			if (!_artifactStore.VerifyArtifact(reference.ArtifactId)) throw new InvalidDataException("The retained reviewed-winner intent failed its integrity check.");
			using (Stream stream = _artifactStore.OpenRead(reference.ArtifactId))
			using (var reader = new BinaryReader(stream, Encoding.UTF8, false))
			{
				CollectionReviewedFileWinnerIntent intent = CollectionReviewedFileWinnerIntent.Deserialize(reader);
				if (stream.Position != stream.Length) throw new InvalidDataException("The retained reviewed-winner intent contains trailing data.");
				if (!intent.OperationIdentity.Equals(operationIdentity) || !intent.PlanIdentity.Equals(plan.Identity))
					throw new InvalidDataException("The retained reviewed-winner intent does not belong to the exact Collection operation/plan.");
				return intent;
			}
		}

		private void SaveIntent(CollectionReviewedFileWinnerIntent intent)
		{
			byte[] bytes;
			using (var stream = new MemoryStream())
			{
				using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) intent.Serialize(writer);
				bytes = stream.ToArray();
			}
			CollectionsRetainedArtifact artifact;
			using (var stream = new MemoryStream(bytes, false)) artifact = _artifactStore.Publish(stream);
			_referenceStore.AcquireExclusiveRoleReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation,
				intent.OperationIdentity.ToString(), GetIntentRole(intent.Target));
		}

		private bool IsVerified(CollectionOperationIdentity operationIdentity, CollectionReviewedFileWinnerIntent intent)
		{
			CollectionsRetainedArtifactReferenceRecord reference = _referenceStore.GetReferenceForOwnerRole(CollectionsRetainedArtifactOwnerKind.Operation,
				operationIdentity.ToString(), GetVerifiedRole(intent.Target));
			if (reference == null) return false;
			CollectionsRetainedArtifactReferenceRecord intentReference = _referenceStore.GetReferenceForOwnerRole(CollectionsRetainedArtifactOwnerKind.Operation,
				operationIdentity.ToString(), GetIntentRole(intent.Target));
			if (intentReference == null || !StringComparer.Ordinal.Equals(reference.ArtifactId, intentReference.ArtifactId))
				throw new InvalidDataException("The reviewed-winner verified checkpoint does not protect the exact persisted intent bytes.");
			return true;
		}

		private void MarkVerified(CollectionOperationIdentity operationIdentity, CollectionReviewedFileWinnerIntent intent)
		{
			CollectionsRetainedArtifactReferenceRecord reference = _referenceStore.GetReferenceForOwnerRole(CollectionsRetainedArtifactOwnerKind.Operation,
				operationIdentity.ToString(), GetIntentRole(intent.Target));
			if (reference == null) throw new InvalidDataException("A reviewed-winner intent must be durable before its verified checkpoint.");
			_referenceStore.AcquireExclusiveRoleReference(reference.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation,
				operationIdentity.ToString(), GetVerifiedRole(intent.Target));
		}

		private static void ValidateIntent(CollectionReviewedFileWinnerIntent intent, CollectionOperationIdentity operationIdentity,
			ResolvedCollectionPlan plan, ModDeploymentTarget target, string desiredOwner, string fallbackOwner,
			CollectionReviewedFileWinnerDispatchKind dispatch)
		{
			if (!intent.OperationIdentity.Equals(operationIdentity) || !intent.PlanIdentity.Equals(plan.Identity) || !intent.Target.Equals(target) ||
				intent.DispatchKind != dispatch || !StringComparer.OrdinalIgnoreCase.Equals(intent.DesiredOwnerKey, desiredOwner) ||
				!StringComparer.OrdinalIgnoreCase.Equals(intent.FallbackOwnerKey ?? String.Empty, fallbackOwner ?? String.Empty))
				throw new InvalidDataException("The durable reviewed-winner intent contradicts the exact approved winner/fallback decision.");
		}

		private static string GetIntentRole(ModDeploymentTarget target) { return IntentRolePrefix + GetTargetToken(target); }
		private static string GetVerifiedRole(ModDeploymentTarget target) { return VerifiedRolePrefix + GetTargetToken(target); }
		private static string GetTargetToken(ModDeploymentTarget target)
		{
			using (SHA256 sha = SHA256.Create())
			{
				byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(target.ToString()));
				var builder = new StringBuilder(hash.Length * 2);
				foreach (byte value in hash) builder.Append(value.ToString("x2"));
				return builder.ToString();
			}
		}

		private sealed class CollectionReviewedFileWinnerIntent
		{
			private readonly ReadOnlyCollection<string> _preimageOwners;

			public CollectionReviewedFileWinnerIntent(CollectionOperationIdentity operationIdentity, CollectionPlanIdentity planIdentity,
				ModDeploymentTarget target, CollectionReviewedFileWinnerDispatchKind dispatchKind, string previousOwnerKey,
				string desiredOwnerKey, string fallbackOwnerKey, IEnumerable<string> preimageOwners)
			{
				OperationIdentity = operationIdentity ?? throw new ArgumentNullException(nameof(operationIdentity));
				PlanIdentity = planIdentity ?? throw new ArgumentNullException(nameof(planIdentity));
				Target = target ?? throw new ArgumentNullException(nameof(target));
				if (!Enum.IsDefined(typeof(CollectionReviewedFileWinnerDispatchKind), dispatchKind) || dispatchKind == CollectionReviewedFileWinnerDispatchKind.Unknown)
					throw new ArgumentOutOfRangeException(nameof(dispatchKind));
				DispatchKind = dispatchKind;
				PreviousOwnerKey = CollectionIdentityValidation.RequireOpaqueToken(previousOwnerKey, nameof(previousOwnerKey));
				DesiredOwnerKey = CollectionIdentityValidation.RequireOpaqueToken(desiredOwnerKey, nameof(desiredOwnerKey));
				FallbackOwnerKey = String.IsNullOrWhiteSpace(fallbackOwnerKey) ? null :
					CollectionIdentityValidation.RequireOpaqueToken(fallbackOwnerKey, nameof(fallbackOwnerKey));
				if (FallbackOwnerKey != null && StringComparer.OrdinalIgnoreCase.Equals(FallbackOwnerKey, DesiredOwnerKey))
					throw new ArgumentException("The reviewed fallback owner must differ from the final reviewed owner.", nameof(fallbackOwnerKey));
				_preimageOwners = new ReadOnlyCollection<string>((preimageOwners ?? throw new ArgumentNullException(nameof(preimageOwners)))
					.Select(x => CollectionIdentityValidation.RequireOpaqueToken(x, nameof(preimageOwners))).ToList());
				if (_preimageOwners.Count == 0 || !_preimageOwners.Contains(PreviousOwnerKey, StringComparer.OrdinalIgnoreCase) ||
					!_preimageOwners.Contains(DesiredOwnerKey, StringComparer.OrdinalIgnoreCase) ||
					(FallbackOwnerKey != null && !_preimageOwners.Contains(FallbackOwnerKey, StringComparer.OrdinalIgnoreCase)) ||
					!StringComparer.OrdinalIgnoreCase.Equals(_preimageOwners[_preimageOwners.Count - 1], PreviousOwnerKey))
					throw new ArgumentException("The winner preimage must contain the reviewed managed owners and end with the previous effective owner.", nameof(preimageOwners));
			}

			public CollectionOperationIdentity OperationIdentity { get; }
			public CollectionPlanIdentity PlanIdentity { get; }
			public ModDeploymentTarget Target { get; }
			public CollectionReviewedFileWinnerDispatchKind DispatchKind { get; }
			public string PreviousOwnerKey { get; }
			public string DesiredOwnerKey { get; }
			public string FallbackOwnerKey { get; }
			public ReadOnlyCollection<string> PreimageOwners { get { return _preimageOwners; } }

			public void Serialize(BinaryWriter writer)
			{
				writer.Write(IntentFormat);
				writer.Write(OperationIdentity.OperationId.ToString("D"));
				writer.Write((int)ModOperationOrigin.Collection);
				writer.Write(PlanIdentity.PlanId.ToString("D"));
				writer.Write(PlanIdentity.Version);
				writer.Write((int)Target.Root);
				writer.Write(Target.RelativePath);
				writer.Write((int)DispatchKind);
				writer.Write(PreviousOwnerKey);
				writer.Write(DesiredOwnerKey);
				writer.Write(FallbackOwnerKey != null);
				if (FallbackOwnerKey != null) writer.Write(FallbackOwnerKey);
				writer.Write(_preimageOwners.Count);
				foreach (string owner in _preimageOwners) writer.Write(owner);
			}

			public static CollectionReviewedFileWinnerIntent Deserialize(BinaryReader reader)
			{
				string format = reader.ReadString();
				bool legacy = StringComparer.Ordinal.Equals(format, LegacyIntentFormat);
				if (!legacy && !StringComparer.Ordinal.Equals(format, IntentFormat)) throw new InvalidDataException("Unsupported reviewed-winner intent format.");
				Guid operationId = Guid.Parse(reader.ReadString());
				ModOperationOrigin origin = (ModOperationOrigin)reader.ReadInt32();
				if (origin != ModOperationOrigin.Collection) throw new InvalidDataException("Reviewed-winner intents must record Collection native origin.");
				CollectionOperationIdentity operation = CollectionOperationIdentity.From(operationId);
				CollectionPlanIdentity plan = CollectionPlanIdentity.From(Guid.Parse(reader.ReadString()), reader.ReadInt32());
				ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical((ModDeploymentRoot)reader.ReadInt32(), reader.ReadString());
				var dispatch = (CollectionReviewedFileWinnerDispatchKind)reader.ReadInt32();
				string previous = reader.ReadString();
				string desired = reader.ReadString();
				string fallback = !legacy && reader.ReadBoolean() ? reader.ReadString() : null;
				int count = reader.ReadInt32();
				if (count <= 0 || count > 4096) throw new InvalidDataException("Invalid reviewed-winner preimage owner count.");
				var owners = new List<string>(count);
				for (int i = 0; i < count; i++) owners.Add(reader.ReadString());
				return new CollectionReviewedFileWinnerIntent(operation, plan, target, dispatch, previous, desired, fallback, owners);
			}
		}
	}
}
