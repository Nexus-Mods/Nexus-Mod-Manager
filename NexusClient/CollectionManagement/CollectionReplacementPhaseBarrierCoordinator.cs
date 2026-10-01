using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Identifies one C8.5 replacement consistency barrier.</summary>
	public enum CollectionReplacementBarrierKind
	{
		Unknown = 0,
		PostOutgoingRemoval = 1,
		NativeStateVisibility = 2
	}

	/// <summary>Durable result of comparing authoritative phase state with the exact reviewed replacement evidence.</summary>
	public enum CollectionReplacementBarrierDisposition
	{
		Unknown = 0,
		Verified = 1,
		ExplicitReviewRequired = 2,
		RecoveryRequired = 3
	}

	/// <summary>Stable identity of a post-removal or dependency-visibility barrier within one replacement operation.</summary>
	public sealed class CollectionReplacementBarrierIdentity
	{
		public CollectionReplacementBarrierIdentity(int ordinal, CollectionReplacementBarrierKind kind,
			double? completedPhase, double? nextPhase)
		{
			if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal));
			if (!Enum.IsDefined(typeof(CollectionReplacementBarrierKind), kind) || kind == CollectionReplacementBarrierKind.Unknown)
				throw new ArgumentOutOfRangeException(nameof(kind));
			if (kind == CollectionReplacementBarrierKind.PostOutgoingRemoval)
			{
				if (ordinal != 0 || completedPhase.HasValue || nextPhase.HasValue)
					throw new ArgumentException("The post-outgoing-removal barrier is ordinal zero and has no incoming dependency phase numbers.");
			}
			else
			{
				if (ordinal == 0 || !completedPhase.HasValue || !nextPhase.HasValue || completedPhase.Value >= nextPhase.Value)
					throw new ArgumentException("A native-state visibility barrier requires a positive ordinal and increasing completed/next phase numbers.");
			}
			Ordinal = ordinal;
			Kind = kind;
			CompletedPhase = completedPhase;
			NextPhase = nextPhase;
		}

		public int Ordinal { get; }
		public CollectionReplacementBarrierKind Kind { get; }
		public double? CompletedPhase { get; }
		public double? NextPhase { get; }
	}

	/// <summary>Inputs exposed to the read-only recipe re-preparation callback while the target mutation lease is held.</summary>
	public sealed class CollectionReplacementBarrierPreparationContext
	{
		private readonly ReadOnlyCollection<CollectionReplacementPreparedRecipeApproval> _effectiveReviewedRecipes;
		private readonly ReadOnlyCollection<CollectionMemberKey> _requiredMembers;

		internal CollectionReplacementBarrierPreparationContext(CollectionOperation operation,
			CollectionReplacementBarrierIdentity barrier, CollectionReplacementCurrentSetupSnapshot current,
			CollectionReplacementEnvironmentProjection observedEnvironment,
			IEnumerable<CollectionReplacementPreparedRecipeApproval> effectiveReviewedRecipes,
			IEnumerable<CollectionMemberKey> requiredMembers)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Barrier = barrier ?? throw new ArgumentNullException(nameof(barrier));
			CurrentSetup = current ?? throw new ArgumentNullException(nameof(current));
			ObservedEnvironment = observedEnvironment ?? throw new ArgumentNullException(nameof(observedEnvironment));
			_effectiveReviewedRecipes = new ReadOnlyCollection<CollectionReplacementPreparedRecipeApproval>(
				(effectiveReviewedRecipes ?? throw new ArgumentNullException(nameof(effectiveReviewedRecipes))).OrderBy(x => x.MemberKey.ToString(), StringComparer.Ordinal).ToList());
			_requiredMembers = new ReadOnlyCollection<CollectionMemberKey>((requiredMembers ?? throw new ArgumentNullException(nameof(requiredMembers)))
				.OrderBy(x => x.ToString(), StringComparer.Ordinal).ToList());
		}

		public CollectionOperation Operation { get; }
		public CollectionReplacementBarrierIdentity Barrier { get; }
		public CollectionReplacementCurrentSetupSnapshot CurrentSetup { get; }
		public CollectionReplacementEnvironmentProjection ObservedEnvironment { get; }
		public ReadOnlyCollection<CollectionReplacementPreparedRecipeApproval> EffectiveReviewedRecipes { get { return _effectiveReviewedRecipes; } }
		public ReadOnlyCollection<CollectionMemberKey> RequiredMembers { get { return _requiredMembers; } }
	}

	public delegate Task<IReadOnlyList<PreparedCollectionNativeRecipe>> CollectionReplacementRecipeRevalidationCallback(
		CollectionReplacementBarrierPreparationContext context, CancellationToken cancellationToken);

	/// <summary>One durable C8.5 barrier result returned to product composition/review code.</summary>
	public sealed class CollectionReplacementBarrierResult
	{
		private readonly ReadOnlyCollection<CollectionReplacementPreparedRecipeApproval> _revalidatedRecipes;

		internal CollectionReplacementBarrierResult(CollectionOperation operation, CollectionReplacementBarrierIdentity barrier,
			CollectionReplacementBarrierDisposition disposition, string expectedEnvironmentFingerprint,
			string observedEnvironmentFingerprint, IEnumerable<CollectionReplacementPreparedRecipeApproval> revalidatedRecipes,
			string observationArtifactId, string message)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Barrier = barrier ?? throw new ArgumentNullException(nameof(barrier));
			if (!Enum.IsDefined(typeof(CollectionReplacementBarrierDisposition), disposition) || disposition == CollectionReplacementBarrierDisposition.Unknown)
				throw new ArgumentOutOfRangeException(nameof(disposition));
			Disposition = disposition;
			ExpectedEnvironmentFingerprint = CollectionIdentityValidation.RequireOpaqueToken(expectedEnvironmentFingerprint, nameof(expectedEnvironmentFingerprint));
			ObservedEnvironmentFingerprint = CollectionIdentityValidation.RequireOpaqueToken(observedEnvironmentFingerprint, nameof(observedEnvironmentFingerprint));
			_revalidatedRecipes = new ReadOnlyCollection<CollectionReplacementPreparedRecipeApproval>((revalidatedRecipes ?? Enumerable.Empty<CollectionReplacementPreparedRecipeApproval>())
				.OrderBy(x => x.MemberKey.ToString(), StringComparer.Ordinal).ToList());
			ObservationArtifactId = CollectionIdentityValidation.RequireOpaqueToken(observationArtifactId, nameof(observationArtifactId));
			Message = message ?? String.Empty;
		}

		public CollectionOperation Operation { get; }
		public CollectionReplacementBarrierIdentity Barrier { get; }
		public CollectionReplacementBarrierDisposition Disposition { get; }
		public string ExpectedEnvironmentFingerprint { get; }
		public string ObservedEnvironmentFingerprint { get; }
		public ReadOnlyCollection<CollectionReplacementPreparedRecipeApproval> RevalidatedRecipes { get { return _revalidatedRecipes; } }
		public string ObservationArtifactId { get; }
		public string Message { get; }
		public bool IsVerified { get { return Disposition == CollectionReplacementBarrierDisposition.Verified; } }
		public bool RequiresExplicitReview { get { return Disposition == CollectionReplacementBarrierDisposition.ExplicitReviewRequired; } }
		public bool RequiresRecovery { get { return Disposition == CollectionReplacementBarrierDisposition.RecoveryRequired; } }
	}

	/// <summary>
	/// C8.5 consistency boundary between verified outgoing removal and incoming mutation, and at later native-state visibility barriers.
	/// </summary>
	/// <remarks>
	/// Barrier observations and explicit amendments are immutable operation-owned retained artifacts. The original C8.3 plan identity,
	/// reviewed intent and completed native-child lineage never change. Amendments may approve only changed prepared/effect fingerprints
	/// for the same member/provider/context/adapter contract; they cannot authorize extra removals or change association decisions.
	/// </remarks>
	public sealed class CollectionReplacementPhaseBarrierCoordinator
	{
		private const string ObservationFormat = "nmm-ce.collections.replacement-barrier-observation/1";
		private const string AmendmentFormat = "nmm-ce.collections.replacement-phase-amendment/1";
		private const string ObservationRolePrefix = "replacement-barrier-observation-v1-";
		private const string AmendmentRolePrefix = "replacement-phase-amendment-v1-";
		private const string AmendmentFingerprintFormat = "nmm-ce.collections.replacement-phase-amendment-fingerprint/1";

		private sealed class EffectiveApprovalState
		{
			public Dictionary<string, CollectionReplacementPreparedRecipeApproval> Recipes;
			public string LastAmendmentFingerprint;
			public string LastAmendmentObservationArtifactId;
			public string PostRemovalObservationArtifactId;
		}

		private sealed class ObservationRecord
		{
			public CollectionReplacementBarrierIdentity Barrier;
			public CollectionReplacementBarrierDisposition Disposition;
			public CollectionOperationPhase ResumePhase;
			public string ExpectedEnvironmentFingerprint;
			public CollectionReplacementEnvironmentProjection ObservedEnvironment;
			public List<CollectionReplacementPreparedRecipeApproval> RevalidatedRecipes;
			public List<CollectionMemberKey> RequiredMembers;
			public string Message;
			public string ArtifactId;
			public long SourceCheckpointSequence;
		}

		private sealed class ObservationDto
		{
			public string Format; public string OperationId; public string PlanId; public int PlanVersion; public long SourceCheckpointSequence;
			public int Ordinal; public int Kind; public double? CompletedPhase; public double? NextPhase; public int Disposition; public int ResumePhase;
			public string ExpectedEnvironmentFingerprint; public EnvironmentDto Environment; public List<RecipeDto> Recipes; public List<MemberDto> RequiredMembers; public string Message;
		}
		private sealed class AmendmentDto
		{
			public string Format; public string OperationId; public string PlanId; public int PlanVersion; public int Ordinal; public int Kind;
			public long ObservationCheckpointSequence; public string ObservationArtifactId; public string PreviousAmendmentFingerprint; public string AmendmentFingerprint;
		}
		private sealed class EnvironmentDto { public int PluginCoverage; public List<FileDto> Files; public List<IniDto> Ini; public List<PluginDto> Plugins; public List<string> Issues; }
		private sealed class FileDto { public int Root; public string Path; public int Knowledge; public bool Visible; public string Owner; public string Reason; }
		private sealed class IniDto { public string File; public string Section; public string Key; public int Knowledge; public string Value; public string Owner; public string Reason; }
		private sealed class PluginDto { public string FileName; public bool? Registered; public bool? Active; public string Reason; }
		private sealed class RecipeDto { public int KeyKind; public string KeyValue; public string Provider; public string Prepared; public string Effects; public int Method; public int Root; public string Adapter; public int Version; }
		private sealed class MemberDto { public int Kind; public string Value; }

		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionsRetainedArtifactReferenceStore _referenceStore;
		private readonly CollectionReplacementOperationCoordinator _replacementCoordinator;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;
		private readonly CollectionReplacementEnvironmentProjector _environmentProjector;

		public CollectionReplacementPhaseBarrierCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore, CollectionsAssociationStore associationStore,
			CollectionsRetainedArtifactStore artifactStore, CollectionsRetainedArtifactReferenceStore referenceStore)
			: this(services, gameStorageService, operationStore, associationStore, artifactStore, referenceStore,
				new CollectionReplacementOperationCoordinator(operationStore, planStore, artifactStore, referenceStore),
				CollectionTargetMutationLeaseManager.Shared, new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services),
				new CollectionReplacementEnvironmentProjector())
		{
		}

		internal CollectionReplacementPhaseBarrierCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsAssociationStore associationStore,
			CollectionsRetainedArtifactStore artifactStore, CollectionsRetainedArtifactReferenceStore referenceStore,
			CollectionReplacementOperationCoordinator replacementCoordinator, CollectionTargetMutationLeaseManager mutationLeaseManager,
			CollectionTargetOwnershipAuthorityValidator authorityValidator, CollectionReplacementEnvironmentProjector environmentProjector)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
			_artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
			_referenceStore = referenceStore ?? throw new ArgumentNullException(nameof(referenceStore));
			_replacementCoordinator = replacementCoordinator ?? throw new ArgumentNullException(nameof(replacementCoordinator));
			_mutationLeaseManager = mutationLeaseManager ?? throw new ArgumentNullException(nameof(mutationLeaseManager));
			_authorityValidator = authorityValidator ?? throw new ArgumentNullException(nameof(authorityValidator));
			_environmentProjector = environmentProjector ?? throw new ArgumentNullException(nameof(environmentProjector));
		}

		public Task<CollectionReplacementBarrierResult> RevalidatePostOutgoingRemovalAsync(CollectionOperationIdentity operationIdentity,
			CollectionPlanIdentity expectedPlan, GameStoragePathSet paths, CollectionReplacementEnvironmentProjection reviewedBaseline,
			CollectionReplacementRecipeRevalidationCallback recipeRevalidation, CancellationToken cancellationToken)
		{
			return RevalidateAsync(operationIdentity, expectedPlan, paths,
				new CollectionReplacementBarrierIdentity(0, CollectionReplacementBarrierKind.PostOutgoingRemoval, null, null),
				reviewedBaseline, null, null, recipeRevalidation, cancellationToken);
		}

		/// <summary>
		/// Revalidates one reviewed C6 NativeStateVisibility barrier after earlier incoming phases have durably completed.
		/// The completed recipes must be the exact currently approved recipes whose effects define the expected next environment.
		/// </summary>
		public Task<CollectionReplacementBarrierResult> RevalidateIncomingVisibilityBarrierAsync(CollectionOperationIdentity operationIdentity,
			CollectionPlanIdentity expectedPlan, GameStoragePathSet paths, int barrierOrdinal, double completedPhase, double nextPhase,
			CollectionReplacementEnvironmentProjection reviewedBaseline, IEnumerable<PreparedCollectionNativeRecipe> completedRecipes,
			IEnumerable<CollectionMemberKey> nextPhaseMembers, CollectionReplacementRecipeRevalidationCallback recipeRevalidation,
			CancellationToken cancellationToken)
		{
			return RevalidateAsync(operationIdentity, expectedPlan, paths,
				new CollectionReplacementBarrierIdentity(barrierOrdinal, CollectionReplacementBarrierKind.NativeStateVisibility, completedPhase, nextPhase),
				reviewedBaseline, completedRecipes, nextPhaseMembers, recipeRevalidation, cancellationToken);
		}

		private async Task<CollectionReplacementBarrierResult> RevalidateAsync(CollectionOperationIdentity operationIdentity,
			CollectionPlanIdentity expectedPlan, GameStoragePathSet paths, CollectionReplacementBarrierIdentity barrier,
			CollectionReplacementEnvironmentProjection reviewedBaseline, IEnumerable<PreparedCollectionNativeRecipe> completedRecipes,
			IEnumerable<CollectionMemberKey> requiredMembers, CollectionReplacementRecipeRevalidationCallback recipeRevalidation,
			CancellationToken cancellationToken)
		{
			RequireLiveServices();
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			if (expectedPlan == null) throw new ArgumentNullException(nameof(expectedPlan));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			if (reviewedBaseline == null) throw new ArgumentNullException(nameof(reviewedBaseline));
			cancellationToken.ThrowIfCancellationRequested();

			CollectionOperation operation = RequireBarrierOperation(operationIdentity, barrier);
			CollectionReplacementReviewedIntent intent = _replacementCoordinator.GetReviewedIntent(operationIdentity, expectedPlan);
			if (!reviewedBaseline.Diff.PlanIdentity.Equals(expectedPlan) || !reviewedBaseline.Diff.Target.Equals(operation.Target))
				throw new ArgumentException("The supplied replacement baseline belongs to a different reviewed plan/target.", nameof(reviewedBaseline));
			if (!StringComparer.Ordinal.Equals(CollectionReplacementReviewedIntent.ComputeEnvironmentFingerprint(reviewedBaseline), intent.EnvironmentFingerprint))
				throw new InvalidDataException("The supplied C8.2 baseline does not reproduce the environment fingerprint bound to the original C8.3 review.");

			EffectiveApprovalState effective = LoadEffectiveApprovals(operation, intent);
			if (operation.Phase == CollectionOperationPhase.ReplacementBarrierRevalidationRequired)
				ValidateApprovedAmendmentRevalidation(operation, expectedPlan, barrier, effective);
			CollectionReplacementBarrierResult interruptedObservation = RecoverInterruptedObservationPublication(operation, expectedPlan, barrier, reviewedBaseline.Diff);
			if (interruptedObservation != null) return interruptedObservation;
			CollectionReplacementEnvironmentProjection effectiveBaseline = LoadEffectivePostRemovalBaseline(operation, expectedPlan, reviewedBaseline, effective);
			List<PreparedCollectionNativeRecipe> completed = (completedRecipes ?? Enumerable.Empty<PreparedCollectionNativeRecipe>()).ToList();
			CollectionReplacementEnvironmentProjection expectedEnvironment;
			List<CollectionMemberKey> required;
			if (barrier.Kind == CollectionReplacementBarrierKind.PostOutgoingRemoval)
			{
				if (completed.Count != 0) throw new ArgumentException("The post-removal barrier cannot include completed incoming recipes.", nameof(completedRecipes));
				expectedEnvironment = effectiveBaseline;
				required = effective.Recipes.Values.Select(x => x.MemberKey).ToList();
			}
			else
			{
				ValidateCompletedRecipes(completed, effective.Recipes);
				expectedEnvironment = _environmentProjector.Overlay(effectiveBaseline, completed.Select(x => x.EffectPreview));
				required = (requiredMembers ?? throw new ArgumentNullException(nameof(requiredMembers))).ToList();
			}
			ValidateRequiredMembers(required, effective.Recipes);

			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(operation.Target))
				throw new InvalidOperationException("The live canonical target no longer matches the replacement operation at its phase barrier.");

			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				_authorityValidator.ValidateAndReload(lease, authority, paths);
				CollectionReplacementCurrentSetupSnapshot current = CaptureReloadedCurrentSetup(operation.Target);
				string survivorFailure = ValidateSurvivingNativeDecisions(intent, current);
				CollectionReplacementEnvironmentProjection observed = _environmentProjector.ObserveCurrent(expectedEnvironment, current);
				var context = new CollectionReplacementBarrierPreparationContext(operation, barrier, current, observed,
					effective.Recipes.Values, required);
				IReadOnlyList<PreparedCollectionNativeRecipe> prepared;
				if (required.Count == 0)
					prepared = new PreparedCollectionNativeRecipe[0];
				else
				{
					if (recipeRevalidation == null)
						throw new ArgumentNullException(nameof(recipeRevalidation), "A barrier with incoming reviewed recipes requires explicit read-only recipe re-preparation.");
					prepared = await recipeRevalidation(context, cancellationToken).ConfigureAwait(true);
					if (prepared == null) throw new InvalidDataException("Replacement recipe revalidation returned no result set.");
				}
				List<CollectionReplacementPreparedRecipeApproval> revalidated = prepared.Select(CollectionReplacementPreparedRecipeApproval.FromPreparedRecipe).ToList();
				string expectedFingerprint = CollectionReplacementReviewedIntent.ComputeEnvironmentFingerprint(expectedEnvironment);
				string observedFingerprint = CollectionReplacementReviewedIntent.ComputeEnvironmentFingerprint(observed);
				string message;
				CollectionReplacementBarrierDisposition disposition = ClassifyBarrier(expectedFingerprint, observedFingerprint,
					observed.IsReadyForSupportedConditions, survivorFailure, effective.Recipes, required, revalidated,
					barrier.Kind == CollectionReplacementBarrierKind.PostOutgoingRemoval, out message);
				CollectionOperationPhase resumePhase = barrier.Kind == CollectionReplacementBarrierKind.PostOutgoingRemoval
					? CollectionOperationPhase.ReadyForIncomingNativeChildren : CollectionOperationPhase.InstallingIncomingNativeChildren;
				ObservationRecord observation = new ObservationRecord {
					Barrier=barrier, Disposition=disposition, ResumePhase=resumePhase,
					ExpectedEnvironmentFingerprint=expectedFingerprint, ObservedEnvironment=observed,
					RevalidatedRecipes=revalidated, RequiredMembers=required, Message=message,
					SourceCheckpointSequence=operation.CheckpointSequence
				};
				observation.ArtifactId = PersistObservation(operation, expectedPlan, observation);
				operation = AdvanceAfterObservation(operation, observation);
				return new CollectionReplacementBarrierResult(operation, barrier, disposition, expectedFingerprint,
					observedFingerprint, revalidated, observation.ArtifactId, message);
			}
		}

		/// <summary>
		/// Explicitly approves exactly the pending observed C8.5 delta. It cannot alter removal/association decisions or supply
		/// different recipe evidence from the immutable barrier observation.
		/// </summary>
		public CollectionOperation ApprovePendingAmendment(CollectionOperationIdentity operationIdentity,
			CollectionPlanIdentity expectedPlan, string observationArtifactId, CollectionReplacementEnvironmentProjection reviewedBaseline)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			if (expectedPlan == null) throw new ArgumentNullException(nameof(expectedPlan));
			observationArtifactId = CollectionIdentityValidation.RequireOpaqueToken(observationArtifactId, nameof(observationArtifactId));
			if (reviewedBaseline == null) throw new ArgumentNullException(nameof(reviewedBaseline));
			CollectionOperation operation = _operationStore.GetOperation(operationIdentity);
			if (operation == null || operation.Kind != CollectionOperationKind.ReplaceCurrentManagedSetup || operation.IsTerminal ||
				operation.Phase != CollectionOperationPhase.AwaitingReplacementPhaseAmendment || operation.ResultState != CollectionOperationResultState.Pending)
				throw new InvalidOperationException("The replacement operation is not waiting for an explicit C8.5 phase amendment.");
			CollectionReplacementReviewedIntent intent = _replacementCoordinator.GetReviewedIntent(operationIdentity, expectedPlan);
			if (!StringComparer.Ordinal.Equals(CollectionReplacementReviewedIntent.ComputeEnvironmentFingerprint(reviewedBaseline), intent.EnvironmentFingerprint))
				throw new InvalidDataException("The supplied replacement baseline no longer reproduces the original reviewed environment.");

			ObservationRecord observation = LoadPendingObservation(operation, expectedPlan, observationArtifactId, reviewedBaseline.Diff);
			if (observation.Disposition != CollectionReplacementBarrierDisposition.ExplicitReviewRequired)
				throw new InvalidOperationException("Only the exact pending immutable barrier observation classified for explicit review can become a phase amendment.");
			EffectiveApprovalState effective = LoadEffectiveApprovals(operation, intent);
			ValidateAmendableRecipeChanges(effective.Recipes, observation.RequiredMembers, observation.RevalidatedRecipes);
			string amendmentFingerprint;
			if (StringComparer.Ordinal.Equals(effective.LastAmendmentObservationArtifactId, observation.ArtifactId))
				amendmentFingerprint = effective.LastAmendmentFingerprint; // previous approval crossed the artifact boundary before its phase checkpoint
			else
				amendmentFingerprint = PersistAmendment(operation, expectedPlan, observation, effective.LastAmendmentFingerprint);
			if (String.IsNullOrWhiteSpace(amendmentFingerprint))
				throw new InvalidDataException("The replacement phase amendment did not produce a durable fingerprint.");

			// Approval records consent to the observed delta; it does not prove that the target stayed unchanged while the user reviewed it.
			// Force the exact barrier through one more authoritative reload/revalidation before any incoming mutation can resume.
			operation = WithOperationState(operation, CollectionOperationPhase.ReplacementBarrierRevalidationRequired, CollectionOperationResultState.Pending);
			_operationStore.SaveOperation(operation);
			return operation;
		}

		/// <summary>Returns the exact barrier whose approved amendment must be revalidated after restart.</summary>
		public CollectionReplacementBarrierIdentity GetPendingAmendmentRevalidationBarrier(CollectionOperationIdentity operationIdentity,
			CollectionPlanIdentity expectedPlan)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			if (expectedPlan == null) throw new ArgumentNullException(nameof(expectedPlan));
			CollectionOperation operation = _operationStore.GetOperation(operationIdentity);
			if (operation == null || operation.Kind != CollectionOperationKind.ReplaceCurrentManagedSetup || operation.IsTerminal ||
				operation.Phase != CollectionOperationPhase.ReplacementBarrierRevalidationRequired || operation.ResultState != CollectionOperationResultState.Pending)
				throw new InvalidOperationException("The replacement operation is not waiting to revalidate an approved C8.5 phase amendment.");
			CollectionReplacementReviewedIntent intent = _replacementCoordinator.GetReviewedIntent(operationIdentity, expectedPlan);
			EffectiveApprovalState effective = LoadEffectiveApprovals(operation, intent);
			if (String.IsNullOrWhiteSpace(effective.LastAmendmentObservationArtifactId))
				throw new InvalidDataException("The replacement operation has no durable approved amendment observation to revalidate.");
			ObservationDto dto = ReadObservationDto(effective.LastAmendmentObservationArtifactId, operation, expectedPlan);
			return ReadBarrierIdentity(dto);
		}

		/// <summary>Loads the currently effective reviewed recipe identities after all explicitly approved C8.5 amendments.</summary>
		public IReadOnlyList<CollectionReplacementPreparedRecipeApproval> GetEffectiveReviewedRecipes(CollectionOperationIdentity operationIdentity,
			CollectionPlanIdentity expectedPlan)
		{
			CollectionOperation operation = _operationStore.GetOperation(operationIdentity ?? throw new ArgumentNullException(nameof(operationIdentity)));
			if (operation == null || operation.Kind != CollectionOperationKind.ReplaceCurrentManagedSetup)
				throw new InvalidOperationException("The replacement operation is not present.");
			CollectionReplacementReviewedIntent intent = _replacementCoordinator.GetReviewedIntent(operationIdentity, expectedPlan);
			return new ReadOnlyCollection<CollectionReplacementPreparedRecipeApproval>(LoadEffectiveApprovals(operation, intent).Recipes.Values
				.OrderBy(x => x.MemberKey.ToString(), StringComparer.Ordinal).ToList());
		}

		internal static CollectionReplacementBarrierDisposition ClassifyBarrier(string expectedEnvironmentFingerprint,
			string observedEnvironmentFingerprint, bool observedEnvironmentReady, string survivorFailure,
			IDictionary<string, CollectionReplacementPreparedRecipeApproval> effectiveRecipes, IEnumerable<CollectionMemberKey> requiredMembers,
			IEnumerable<CollectionReplacementPreparedRecipeApproval> revalidatedRecipes, out string message)
		{
			return ClassifyBarrier(expectedEnvironmentFingerprint, observedEnvironmentFingerprint, observedEnvironmentReady, survivorFailure,
				effectiveRecipes, requiredMembers, revalidatedRecipes, true, out message);
		}

		internal static CollectionReplacementBarrierDisposition ClassifyBarrier(string expectedEnvironmentFingerprint,
			string observedEnvironmentFingerprint, bool observedEnvironmentReady, string survivorFailure,
			IDictionary<string, CollectionReplacementPreparedRecipeApproval> effectiveRecipes, IEnumerable<CollectionMemberKey> requiredMembers,
			IEnumerable<CollectionReplacementPreparedRecipeApproval> revalidatedRecipes, bool allowEnvironmentAmendment, out string message)
		{
			if (!String.IsNullOrWhiteSpace(survivorFailure))
			{
				message = survivorFailure;
				return CollectionReplacementBarrierDisposition.RecoveryRequired;
			}
			if (!observedEnvironmentReady)
			{
				message = "The authoritative replacement environment contains unsupported or unprovable condition state after native mutation.";
				return CollectionReplacementBarrierDisposition.RecoveryRequired;
			}
			List<CollectionMemberKey> required = (requiredMembers ?? throw new ArgumentNullException(nameof(requiredMembers))).ToList();
			List<CollectionReplacementPreparedRecipeApproval> actual = (revalidatedRecipes ?? throw new ArgumentNullException(nameof(revalidatedRecipes))).ToList();
			if (actual.Count != required.Count || actual.Select(x => x.MemberKey.ToString()).Distinct(StringComparer.Ordinal).Count() != actual.Count ||
				required.Select(x => x.ToString()).Except(actual.Select(x => x.MemberKey.ToString()), StringComparer.Ordinal).Any())
			{
				message = "The barrier could not reproduce exactly the required reviewed incoming recipe set.";
				return CollectionReplacementBarrierDisposition.RecoveryRequired;
			}

			bool recipeDelta = false;
			foreach (CollectionReplacementPreparedRecipeApproval current in actual)
			{
				CollectionReplacementPreparedRecipeApproval approved;
				if (!effectiveRecipes.TryGetValue(current.MemberKey.ToString(), out approved) || !IsAmendableRecipeContract(approved, current))
				{
					message = "A re-prepared incoming recipe changed member/provider/install-context/adapter identity and cannot be approved as a C8.5 phase amendment.";
					return CollectionReplacementBarrierDisposition.RecoveryRequired;
				}
				if (!StringComparer.Ordinal.Equals(approved.PreparedNativeFingerprint, current.PreparedNativeFingerprint) ||
					!StringComparer.Ordinal.Equals(approved.EffectFingerprint, current.EffectFingerprint))
					recipeDelta = true;
			}
			bool environmentDelta = !StringComparer.Ordinal.Equals(expectedEnvironmentFingerprint, observedEnvironmentFingerprint);
			if (environmentDelta && !allowEnvironmentAmendment)
			{
				message = "Authoritative native state differs from the approved projection after incoming native work has already crossed a visibility barrier; this delta cannot be converted into a new review baseline and requires recovery.";
				return CollectionReplacementBarrierDisposition.RecoveryRequired;
			}
			if (environmentDelta || recipeDelta)
			{
				message = environmentDelta && recipeDelta
					? "Authoritative phase state and re-prepared incoming effects differ from the reviewed replacement evidence; explicit amendment review is required."
					: environmentDelta
						? "Authoritative phase condition state differs from the reviewed replacement projection; explicit amendment review is required."
						: "Re-prepared incoming native/effect identity differs from the reviewed recipe; explicit amendment review is required.";
				return CollectionReplacementBarrierDisposition.ExplicitReviewRequired;
			}
			message = "Authoritative phase state and re-prepared incoming recipe/effects exactly match the current reviewed replacement approval.";
			return CollectionReplacementBarrierDisposition.Verified;
		}

		private static bool IsAmendableRecipeContract(CollectionReplacementPreparedRecipeApproval approved,
			CollectionReplacementPreparedRecipeApproval current)
		{
			return approved != null && current != null && approved.MemberKey.Equals(current.MemberKey) &&
				StringComparer.Ordinal.Equals(approved.ProviderRecipeFingerprint, current.ProviderRecipeFingerprint) &&
				approved.Method == current.Method && approved.Root == current.Root &&
				StringComparer.Ordinal.Equals(approved.AdapterId, current.AdapterId) && approved.AdapterVersion == current.AdapterVersion;
		}

		internal static string ValidateSurvivingNativeDecisions(CollectionReplacementReviewedIntent intent,
			CollectionReplacementCurrentSetupSnapshot current)
		{
			if (intent == null) throw new ArgumentNullException(nameof(intent));
			if (current == null) throw new ArgumentNullException(nameof(current));
			foreach (CollectionReplacementNativeApproval approval in intent.NativeApprovals)
			{
				NativeModInstanceIdentity identity = new NativeModInstanceIdentity(current.Target, approval.NativeModKey);
				bool present = current.NativeState.Mods.ContainsKey(identity);
				bool effectsPresent = current.NativeState.FilesByOwnerKey.ContainsKey(approval.NativeModKey) ||
					current.NativeState.IniEditsByOwnerKey.ContainsKey(approval.NativeModKey) || current.NativeState.GameValuesByOwnerKey.ContainsKey(approval.NativeModKey);
				if (approval.Decision == CollectionReplacementNativeDecision.Remove && (present || effectsPresent))
					return "A reviewed outgoing native instance/effect reappeared after the verified removal barrier.";
				if (approval.Decision != CollectionReplacementNativeDecision.Remove && !present)
					return "A native instance explicitly retained/protected by the replacement review disappeared after outgoing removal.";
			}
			return null;
		}

		private static void ValidateRequiredMembers(IEnumerable<CollectionMemberKey> requiredMembers,
			IDictionary<string, CollectionReplacementPreparedRecipeApproval> effectiveRecipes)
		{
			List<CollectionMemberKey> required = requiredMembers.ToList();
			if (required.Any(x => x == null) || required.Select(x => x.ToString()).Distinct(StringComparer.Ordinal).Count() != required.Count)
				throw new ArgumentException("A replacement barrier requires unique non-null member identities.", nameof(requiredMembers));
			foreach (CollectionMemberKey key in required)
				if (!effectiveRecipes.ContainsKey(key.ToString()))
					throw new InvalidOperationException("A replacement barrier requested revalidation for a member that is not part of the current explicit review/amendment chain.");
		}

		private static void ValidateCompletedRecipes(IEnumerable<PreparedCollectionNativeRecipe> completed,
			IDictionary<string, CollectionReplacementPreparedRecipeApproval> effectiveRecipes)
		{
			foreach (PreparedCollectionNativeRecipe recipe in completed)
			{
				if (recipe == null) throw new ArgumentException("Completed replacement recipe evidence cannot contain null entries.", nameof(completed));
				CollectionReplacementPreparedRecipeApproval actual = CollectionReplacementPreparedRecipeApproval.FromPreparedRecipe(recipe);
				CollectionReplacementPreparedRecipeApproval approved;
				if (!effectiveRecipes.TryGetValue(actual.MemberKey.ToString(), out approved) || !SameRecipeApproval(approved, actual))
					throw new InvalidOperationException("A completed incoming phase does not reproduce the currently approved replacement recipe/effect identity.");
			}
		}

		private static bool SameRecipeApproval(CollectionReplacementPreparedRecipeApproval left,
			CollectionReplacementPreparedRecipeApproval right)
		{
			return IsAmendableRecipeContract(left, right) &&
				StringComparer.Ordinal.Equals(left.PreparedNativeFingerprint, right.PreparedNativeFingerprint) &&
				StringComparer.Ordinal.Equals(left.EffectFingerprint, right.EffectFingerprint);
		}

		private static void ValidateAmendableRecipeChanges(IDictionary<string, CollectionReplacementPreparedRecipeApproval> effectiveRecipes,
			IEnumerable<CollectionMemberKey> requiredMembers, IEnumerable<CollectionReplacementPreparedRecipeApproval> observedRecipes)
		{
			string message;
			CollectionReplacementBarrierDisposition disposition = ClassifyBarrier("expected", "observed", true, null,
				effectiveRecipes, requiredMembers, observedRecipes, out message);
			if (disposition == CollectionReplacementBarrierDisposition.RecoveryRequired)
				throw new InvalidOperationException(message);
		}

		private CollectionReplacementBarrierResult RecoverInterruptedObservationPublication(CollectionOperation operation,
			CollectionPlanIdentity plan, CollectionReplacementBarrierIdentity requestedBarrier, CollectionReplacementDiffPlan diff)
		{
			CollectionsRetainedArtifactReferenceRecord reference = _referenceStore.GetReferenceForOwnerRole(
				CollectionsRetainedArtifactOwnerKind.Operation, operation.Identity.OperationId.ToString("D"), ObservationRole(operation.CheckpointSequence));
			if (reference == null) return null;
			if (!_artifactStore.VerifyArtifact(reference.ArtifactId))
				throw new InvalidDataException("An interrupted replacement barrier observation failed retained-content verification.");
			ObservationRecord observation = ReadObservationArtifact(reference.ArtifactId, operation, plan, diff);
			if (observation.SourceCheckpointSequence != operation.CheckpointSequence || !SameBarrier(observation.Barrier, requestedBarrier))
				throw new InvalidDataException("An interrupted replacement barrier observation is not bound to the current operation checkpoint/barrier.");

			string observedFingerprint = CollectionReplacementReviewedIntent.ComputeEnvironmentFingerprint(observation.ObservedEnvironment);
			if (observation.Disposition == CollectionReplacementBarrierDisposition.Verified)
			{
				// A crash between publishing a verified observation and publishing the next journal phase leaves a time gap in which
				// native state may have changed. Do not turn the old observation into fresh execution authority after restart.
				string message = "A verified replacement barrier observation was durably retained but its journal transition was interrupted; fresh execution authority cannot be inferred after the gap.";
				CollectionOperation recovery = WithOperationState(operation, CollectionOperationPhase.RecoveryRequired, CollectionOperationResultState.RecoveryRequired);
				_operationStore.SaveOperation(recovery);
				return new CollectionReplacementBarrierResult(recovery, requestedBarrier, CollectionReplacementBarrierDisposition.RecoveryRequired,
					observation.ExpectedEnvironmentFingerprint, observedFingerprint, observation.RevalidatedRecipes, reference.ArtifactId, message);
			}

			CollectionOperation advanced = AdvanceAfterObservation(operation, observation);
			return new CollectionReplacementBarrierResult(advanced, requestedBarrier, observation.Disposition,
				observation.ExpectedEnvironmentFingerprint, observedFingerprint, observation.RevalidatedRecipes, reference.ArtifactId, observation.Message);
		}

		private CollectionOperation RequireBarrierOperation(CollectionOperationIdentity identity, CollectionReplacementBarrierIdentity barrier)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			CollectionOperationPhase requiredPhase = barrier.Kind == CollectionReplacementBarrierKind.PostOutgoingRemoval
				? CollectionOperationPhase.OutgoingRemovalVerified : CollectionOperationPhase.InstallingIncomingNativeChildren;
			if (operation == null || operation.Kind != CollectionOperationKind.ReplaceCurrentManagedSetup || operation.IsTerminal ||
				operation.ResultState != CollectionOperationResultState.Pending ||
				(operation.Phase != requiredPhase && operation.Phase != CollectionOperationPhase.ReplacementBarrierRevalidationRequired))
				throw new InvalidOperationException("The replacement operation is not at the required C8.5 phase barrier.");
			return operation;
		}

		private void ValidateApprovedAmendmentRevalidation(CollectionOperation operation, CollectionPlanIdentity plan,
			CollectionReplacementBarrierIdentity requestedBarrier, EffectiveApprovalState effective)
		{
			if (String.IsNullOrWhiteSpace(effective.LastAmendmentObservationArtifactId))
				throw new InvalidDataException("Replacement barrier revalidation has no durable approved amendment observation.");
			ObservationDto dto = ReadObservationDto(effective.LastAmendmentObservationArtifactId, operation, plan);
			CollectionReplacementBarrierIdentity approvedBarrier = ReadBarrierIdentity(dto);
			if (!SameBarrier(approvedBarrier, requestedBarrier))
				throw new InvalidOperationException("The replacement operation is waiting to revalidate a different explicitly approved C8.5 barrier.");
		}

		private static bool SameBarrier(CollectionReplacementBarrierIdentity left, CollectionReplacementBarrierIdentity right)
		{
			return left != null && right != null && left.Ordinal == right.Ordinal && left.Kind == right.Kind &&
				Nullable.Equals(left.CompletedPhase, right.CompletedPhase) && Nullable.Equals(left.NextPhase, right.NextPhase);
		}

		private CollectionOperation AdvanceAfterObservation(CollectionOperation operation, ObservationRecord observation)
		{
			CollectionOperationPhase phase;
			CollectionOperationResultState result = CollectionOperationResultState.Pending;
			switch (observation.Disposition)
			{
				case CollectionReplacementBarrierDisposition.Verified:
					phase = observation.ResumePhase;
					break;
				case CollectionReplacementBarrierDisposition.ExplicitReviewRequired:
					phase = CollectionOperationPhase.AwaitingReplacementPhaseAmendment;
					break;
				default:
					phase = CollectionOperationPhase.RecoveryRequired;
					result = CollectionOperationResultState.RecoveryRequired;
					break;
			}
			operation = WithOperationState(operation, phase, result);
			_operationStore.SaveOperation(operation);
			return operation;
		}

		private CollectionReplacementCurrentSetupSnapshot CaptureReloadedCurrentSetup(CollectionTargetIdentity target)
		{
			ModManager manager = _services.ModManager;
			return new CollectionNativeStateReader(() => _services.ModManager.InstallationLog,
				manager.VirtualModActivator, _services.PluginManager, manager.GameMode, _associationStore)
				.CaptureReplacementCurrentSetup(target);
		}

		private string PersistObservation(CollectionOperation operation, CollectionPlanIdentity plan, ObservationRecord observation)
		{
			if (observation.SourceCheckpointSequence != operation.CheckpointSequence)
				throw new InvalidOperationException("A replacement barrier observation must bind the exact operation checkpoint from which it was produced.");
			ObservationDto dto = ToDto(operation, plan, observation);
			byte[] payload = new UTF8Encoding(false, true).GetBytes(JsonConvert.SerializeObject(dto, Formatting.None));
			CollectionsRetainedArtifact artifact;
			using (var stream = new MemoryStream(payload, false)) artifact = _artifactStore.Publish(stream);
			_referenceStore.AcquireExclusiveRoleReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation,
				operation.Identity.OperationId.ToString("D"), ObservationRole(observation.SourceCheckpointSequence));
			return artifact.ArtifactId;
		}

		private string PersistAmendment(CollectionOperation operation, CollectionPlanIdentity plan, ObservationRecord observation,
			string previousAmendmentFingerprint)
		{
			if (observation.SourceCheckpointSequence != operation.CheckpointSequence - 1)
				throw new InvalidOperationException("Only the barrier observation that produced the current review checkpoint can be amended.");
			string fingerprint = ComputeAmendmentFingerprint(operation.Identity, plan, observation.ArtifactId, observation.Barrier,
				observation.RevalidatedRecipes, previousAmendmentFingerprint);
			var dto = new AmendmentDto {
				Format=AmendmentFormat, OperationId=operation.Identity.OperationId.ToString("D"), PlanId=plan.PlanId.ToString("D"), PlanVersion=plan.Version,
				Ordinal=observation.Barrier.Ordinal, Kind=(int)observation.Barrier.Kind, ObservationCheckpointSequence=observation.SourceCheckpointSequence,
				ObservationArtifactId=observation.ArtifactId, PreviousAmendmentFingerprint=previousAmendmentFingerprint, AmendmentFingerprint=fingerprint
			};
			byte[] payload = new UTF8Encoding(false, true).GetBytes(JsonConvert.SerializeObject(dto, Formatting.None));
			CollectionsRetainedArtifact artifact;
			using (var stream = new MemoryStream(payload, false)) artifact = _artifactStore.Publish(stream);
			_referenceStore.AcquireExclusiveRoleReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation,
				operation.Identity.OperationId.ToString("D"), AmendmentRole(operation.CheckpointSequence));
			return fingerprint;
		}

		private EffectiveApprovalState LoadEffectiveApprovals(CollectionOperation operation, CollectionReplacementReviewedIntent intent)
		{
			var result = new EffectiveApprovalState {
				Recipes=intent.PreparedRecipes.ToDictionary(x => x.MemberKey.ToString(), StringComparer.Ordinal),
				LastAmendmentFingerprint=null, LastAmendmentObservationArtifactId=null, PostRemovalObservationArtifactId=null
			};
			string ownerId = operation.Identity.OperationId.ToString("D");
			IReadOnlyList<CollectionsRetainedArtifactReferenceRecord> refs = _referenceStore.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Operation, ownerId)
				.Where(x => x.Role.StartsWith(AmendmentRolePrefix, StringComparison.Ordinal))
				.OrderBy(x => ParseRoleSequence(x.Role, AmendmentRolePrefix)).ToList();
			long lastAmendmentCheckpoint = -1;
			foreach (CollectionsRetainedArtifactReferenceRecord reference in refs)
			{
				long amendmentCheckpoint = ParseRoleSequence(reference.Role, AmendmentRolePrefix);
				if (amendmentCheckpoint <= lastAmendmentCheckpoint || amendmentCheckpoint > operation.CheckpointSequence)
					throw new InvalidDataException("Replacement phase amendments are not in a valid durable checkpoint order.");
				if (!_artifactStore.VerifyArtifact(reference.ArtifactId))
					throw new InvalidDataException("A replacement phase amendment failed retained-content verification.");
				AmendmentDto dto = ReadArtifact<AmendmentDto>(reference.ArtifactId, "replacement phase amendment");
				ValidateAmendmentDto(dto, operation, intent.PlanIdentity, result.LastAmendmentFingerprint);
				if (dto.ObservationCheckpointSequence + 1 != amendmentCheckpoint)
					throw new InvalidDataException("A persisted replacement amendment is not bound to the review checkpoint immediately following its observation.");

				ObservationDto observationDto = ReadObservationDto(dto.ObservationArtifactId, operation, intent.PlanIdentity);
				CollectionReplacementBarrierIdentity barrier = ReadBarrierIdentity(observationDto);
				if (observationDto.SourceCheckpointSequence != dto.ObservationCheckpointSequence || barrier.Ordinal != dto.Ordinal ||
					(int)barrier.Kind != dto.Kind || (CollectionReplacementBarrierDisposition)observationDto.Disposition != CollectionReplacementBarrierDisposition.ExplicitReviewRequired)
					throw new InvalidDataException("A persisted replacement amendment does not reference the exact explicit-review barrier observation it claims to approve.");

				List<CollectionReplacementPreparedRecipeApproval> recipes = (observationDto.Recipes ?? new List<RecipeDto>()).Select(FromDto).ToList();
				List<CollectionMemberKey> required = (observationDto.RequiredMembers ?? new List<MemberDto>()).Select(FromDto).ToList();
				ValidateAmendableRecipeChanges(result.Recipes, required, recipes);
				string recomputed = ComputeAmendmentFingerprint(operation.Identity, intent.PlanIdentity, dto.ObservationArtifactId, barrier,
					recipes, result.LastAmendmentFingerprint);
				if (!StringComparer.Ordinal.Equals(recomputed, dto.AmendmentFingerprint))
					throw new InvalidDataException("A persisted replacement phase amendment fingerprint does not match its immutable observation chain.");

				foreach (CollectionReplacementPreparedRecipeApproval recipe in recipes)
					result.Recipes[recipe.MemberKey.ToString()] = recipe;
				result.LastAmendmentFingerprint = dto.AmendmentFingerprint;
				result.LastAmendmentObservationArtifactId = dto.ObservationArtifactId;
				if (barrier.Kind == CollectionReplacementBarrierKind.PostOutgoingRemoval)
					result.PostRemovalObservationArtifactId = dto.ObservationArtifactId;
				lastAmendmentCheckpoint = amendmentCheckpoint;
			}
			return result;
		}

		private CollectionReplacementEnvironmentProjection LoadEffectivePostRemovalBaseline(CollectionOperation operation,
			CollectionPlanIdentity plan, CollectionReplacementEnvironmentProjection reviewedBaseline, EffectiveApprovalState effective)
		{
			if (String.IsNullOrWhiteSpace(effective.PostRemovalObservationArtifactId)) return reviewedBaseline;
			ObservationRecord record = ReadObservationArtifact(effective.PostRemovalObservationArtifactId, operation, plan, reviewedBaseline.Diff);
			if (record.Barrier.Kind != CollectionReplacementBarrierKind.PostOutgoingRemoval)
				throw new InvalidDataException("The persisted post-removal replacement amendment references the wrong barrier kind.");
			return record.ObservedEnvironment;
		}

		private ObservationRecord LoadPendingObservation(CollectionOperation operation, CollectionPlanIdentity plan,
			string expectedArtifactId, CollectionReplacementDiffPlan diff)
		{
			long sourceCheckpoint = operation.CheckpointSequence - 1;
			if (sourceCheckpoint < 0) throw new InvalidDataException("The pending replacement barrier review has no valid source checkpoint.");
			CollectionsRetainedArtifactReferenceRecord reference = _referenceStore.GetReferenceForOwnerRole(
				CollectionsRetainedArtifactOwnerKind.Operation, operation.Identity.OperationId.ToString("D"), ObservationRole(sourceCheckpoint));
			if (reference == null || !StringComparer.Ordinal.Equals(reference.ArtifactId, expectedArtifactId) || !_artifactStore.VerifyArtifact(reference.ArtifactId))
				throw new InvalidDataException("The requested replacement barrier observation is not the exact pending review observation or failed retained-content verification.");
			ObservationRecord record = ReadObservationArtifact(reference.ArtifactId, operation, plan, diff);
			if (record.SourceCheckpointSequence != sourceCheckpoint)
				throw new InvalidDataException("The pending replacement barrier observation is not bound to the operation checkpoint that requested review.");
			return record;
		}

		private ObservationRecord ReadObservationArtifact(string artifactId, CollectionOperation operation, CollectionPlanIdentity plan,
			CollectionReplacementDiffPlan diff)
		{
			ObservationDto dto = ReadObservationDto(artifactId, operation, plan);
			CollectionReplacementBarrierIdentity barrier = ReadBarrierIdentity(dto);
			CollectionReplacementBarrierDisposition disposition = (CollectionReplacementBarrierDisposition)dto.Disposition;
			CollectionOperationPhase resumePhase = (CollectionOperationPhase)dto.ResumePhase;
			var record = new ObservationRecord {
				Barrier=barrier, Disposition=disposition, ResumePhase=resumePhase,
				ExpectedEnvironmentFingerprint=dto.ExpectedEnvironmentFingerprint,
				ObservedEnvironment=FromDto(dto.Environment, diff),
				RevalidatedRecipes=(dto.Recipes ?? new List<RecipeDto>()).Select(FromDto).ToList(),
				RequiredMembers=(dto.RequiredMembers ?? new List<MemberDto>()).Select(FromDto).ToList(),
				Message=dto.Message ?? String.Empty, ArtifactId=artifactId, SourceCheckpointSequence=dto.SourceCheckpointSequence
			};
			string observedFingerprint = CollectionReplacementReviewedIntent.ComputeEnvironmentFingerprint(record.ObservedEnvironment);
			if (String.IsNullOrWhiteSpace(record.ExpectedEnvironmentFingerprint) || String.IsNullOrWhiteSpace(observedFingerprint))
				throw new InvalidDataException("A replacement barrier observation is missing its environment identity.");
			return record;
		}

		private ObservationDto ReadObservationDto(string artifactId, CollectionOperation operation, CollectionPlanIdentity plan)
		{
			artifactId = CollectionIdentityValidation.RequireOpaqueToken(artifactId, nameof(artifactId));
			if (!_artifactStore.VerifyArtifact(artifactId))
				throw new InvalidDataException("A replacement barrier observation failed retained-content verification.");
			ObservationDto dto = ReadArtifact<ObservationDto>(artifactId, "replacement barrier observation");
			if (dto == null || !StringComparer.Ordinal.Equals(dto.Format, ObservationFormat) ||
				!StringComparer.Ordinal.Equals(dto.OperationId, operation.Identity.OperationId.ToString("D")) ||
				!StringComparer.Ordinal.Equals(dto.PlanId, plan.PlanId.ToString("D")) || dto.PlanVersion != plan.Version ||
				dto.SourceCheckpointSequence < 0 || dto.SourceCheckpointSequence > operation.CheckpointSequence)
				throw new InvalidDataException("A replacement barrier observation does not belong to the exact operation/plan/checkpoint.");
			CollectionReplacementBarrierDisposition disposition = (CollectionReplacementBarrierDisposition)dto.Disposition;
			CollectionOperationPhase resumePhase = (CollectionOperationPhase)dto.ResumePhase;
			if (!Enum.IsDefined(typeof(CollectionReplacementBarrierDisposition), disposition) || disposition == CollectionReplacementBarrierDisposition.Unknown ||
				!Enum.IsDefined(typeof(CollectionOperationPhase), resumePhase))
				throw new InvalidDataException("A replacement barrier observation contains invalid durable enum values.");
			ReadBarrierIdentity(dto);
			return dto;
		}

		private static CollectionReplacementBarrierIdentity ReadBarrierIdentity(ObservationDto dto)
		{
			if (dto == null) throw new ArgumentNullException(nameof(dto));
			return new CollectionReplacementBarrierIdentity(dto.Ordinal, (CollectionReplacementBarrierKind)dto.Kind, dto.CompletedPhase, dto.NextPhase);
		}

		private static ObservationDto ToDto(CollectionOperation operation, CollectionPlanIdentity plan, ObservationRecord observation)
		{
			return new ObservationDto {
				Format=ObservationFormat, OperationId=operation.Identity.OperationId.ToString("D"), PlanId=plan.PlanId.ToString("D"), PlanVersion=plan.Version,
				SourceCheckpointSequence=observation.SourceCheckpointSequence, Ordinal=observation.Barrier.Ordinal, Kind=(int)observation.Barrier.Kind, CompletedPhase=observation.Barrier.CompletedPhase,
				NextPhase=observation.Barrier.NextPhase, Disposition=(int)observation.Disposition, ResumePhase=(int)observation.ResumePhase,
				ExpectedEnvironmentFingerprint=observation.ExpectedEnvironmentFingerprint, Environment=ToDto(observation.ObservedEnvironment),
				Recipes=observation.RevalidatedRecipes.Select(ToDto).ToList(), RequiredMembers=observation.RequiredMembers.Select(ToDto).ToList(), Message=observation.Message
			};
		}

		private static EnvironmentDto ToDto(CollectionReplacementEnvironmentProjection environment)
		{
			return new EnvironmentDto {
				PluginCoverage=(int)environment.PluginCoverage,
				Files=environment.Files.Values.OrderBy(x => (int)x.Target.Root).ThenBy(x => x.Target.RelativePath, StringComparer.OrdinalIgnoreCase)
					.Select(x => new FileDto { Root=(int)x.Target.Root, Path=x.Target.RelativePath, Knowledge=(int)x.Knowledge, Visible=x.Visible, Owner=x.OwnerKey, Reason=x.Reason }).ToList(),
				Ini=environment.IniValues.Values.OrderBy(x => x.Key.ToString(), StringComparer.OrdinalIgnoreCase)
					.Select(x => new IniDto { File=x.Key.File, Section=x.Key.Section, Key=x.Key.Key, Knowledge=(int)x.Knowledge, Value=x.Value, Owner=x.OwnerKey, Reason=x.Reason }).ToList(),
				Plugins=environment.Plugins.Values.OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
					.Select(x => new PluginDto { FileName=x.FileName, Registered=x.Registered, Active=x.Active, Reason=x.Reason }).ToList(),
				Issues=environment.Issues.ToList()
			};
		}

		private static CollectionReplacementEnvironmentProjection FromDto(EnvironmentDto dto, CollectionReplacementDiffPlan diff)
		{
			if (dto == null) throw new InvalidDataException("A replacement barrier observation is missing its environment snapshot.");
			var files = new Dictionary<ModDeploymentTarget, CollectionReplacementFileEnvironmentState>();
			foreach (FileDto x in dto.Files ?? new List<FileDto>())
			{
				ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical((ModDeploymentRoot)x.Root, x.Path);
				files.Add(target, new CollectionReplacementFileEnvironmentState(target, (CollectionReplacementEnvironmentKnowledge)x.Knowledge, x.Visible, x.Owner, x.Reason));
			}
			var ini = new Dictionary<CollectionNativeIniKey, CollectionReplacementIniEnvironmentState>();
			foreach (IniDto x in dto.Ini ?? new List<IniDto>())
			{
				var key = new CollectionNativeIniKey(x.File, x.Section, x.Key);
				ini.Add(key, new CollectionReplacementIniEnvironmentState(key, (CollectionReplacementEnvironmentKnowledge)x.Knowledge, x.Value, x.Owner, x.Reason));
			}
			var plugins = new Dictionary<string, CollectionReplacementPluginEnvironmentState>(StringComparer.OrdinalIgnoreCase);
			foreach (PluginDto x in dto.Plugins ?? new List<PluginDto>())
				plugins.Add(CollectionReplacementPluginEnvironmentState.NormalizePlugin(x.FileName), new CollectionReplacementPluginEnvironmentState(x.FileName, x.Registered, x.Active, x.Reason));
			CollectionNativeStateCoverage pluginCoverage = (CollectionNativeStateCoverage)dto.PluginCoverage;
			if (!Enum.IsDefined(typeof(CollectionNativeStateCoverage), pluginCoverage))
				throw new InvalidDataException("A replacement barrier observation contains invalid plugin coverage.");
			return new CollectionReplacementEnvironmentProjection(diff, files, ini, plugins, pluginCoverage, dto.Issues ?? new List<string>());
		}

		private static RecipeDto ToDto(CollectionReplacementPreparedRecipeApproval x)
		{
			return new RecipeDto { KeyKind=(int)x.MemberKey.Kind, KeyValue=x.MemberKey.Value, Provider=x.ProviderRecipeFingerprint,
				Prepared=x.PreparedNativeFingerprint, Effects=x.EffectFingerprint, Method=(int)x.Method, Root=(int)x.Root, Adapter=x.AdapterId, Version=x.AdapterVersion };
		}

		private static CollectionReplacementPreparedRecipeApproval FromDto(RecipeDto x)
		{
			if (x == null) throw new InvalidDataException("A replacement barrier recipe record is null.");
			return new CollectionReplacementPreparedRecipeApproval(ReadMemberKey(x.KeyKind, x.KeyValue), x.Provider, x.Prepared, x.Effects,
				(ModInstallMethod)x.Method, (ModInstallRoot)x.Root, x.Adapter, x.Version);
		}

		private static MemberDto ToDto(CollectionMemberKey x) { return new MemberDto { Kind=(int)x.Kind, Value=x.Value }; }
		private static CollectionMemberKey FromDto(MemberDto x) { return ReadMemberKey(x.Kind, x.Value); }

		private static CollectionMemberKey ReadMemberKey(int kind, string value)
		{
			switch ((CollectionMemberKeyKind)kind)
			{
				case CollectionMemberKeyKind.ProviderStable: return CollectionMemberKey.FromProvider(value);
				case CollectionMemberKeyKind.ValidatedMatch: return CollectionMemberKey.FromValidatedMatch(value);
				case CollectionMemberKeyKind.Local: Guid id; if (!Guid.TryParse(value, out id)) throw new InvalidDataException("Invalid Local member key in replacement barrier evidence."); return CollectionMemberKey.FromLocal(id);
				default: throw new InvalidDataException("Unsupported member-key kind in replacement barrier evidence.");
			}
		}

		private static string ComputeAmendmentFingerprint(CollectionOperationIdentity operation, CollectionPlanIdentity plan,
			string observationArtifactId, CollectionReplacementBarrierIdentity barrier,
			IEnumerable<CollectionReplacementPreparedRecipeApproval> recipes, string previous)
		{
			observationArtifactId = CollectionIdentityValidation.RequireOpaqueToken(observationArtifactId, nameof(observationArtifactId));
			if (barrier == null) throw new ArgumentNullException(nameof(barrier));
			var text = new StringBuilder();
			text.Append(AmendmentFingerprintFormat).Append('\n').Append(operation.OperationId.ToString("D")).Append('\n')
				.Append(plan.PlanId.ToString("D")).Append('|').Append(plan.Version).Append('\n').Append(barrier.Ordinal).Append('|')
				.Append((int)barrier.Kind).Append('|').Append(barrier.CompletedPhase.HasValue ? barrier.CompletedPhase.Value.ToString("R", CultureInfo.InvariantCulture) : String.Empty)
				.Append('|').Append(barrier.NextPhase.HasValue ? barrier.NextPhase.Value.ToString("R", CultureInfo.InvariantCulture) : String.Empty).Append('\n')
				.Append(observationArtifactId).Append('\n').Append(previous ?? String.Empty).Append('\n');
			foreach (CollectionReplacementPreparedRecipeApproval recipe in (recipes ?? throw new ArgumentNullException(nameof(recipes)))
				.OrderBy(x => x.MemberKey.ToString(), StringComparer.Ordinal))
				text.Append(recipe.MemberKey).Append('|').Append(recipe.ProviderRecipeFingerprint).Append('|').Append(recipe.PreparedNativeFingerprint).Append('|')
					.Append(recipe.EffectFingerprint).Append('|').Append((int)recipe.Method).Append('|').Append((int)recipe.Root).Append('|')
					.Append(recipe.AdapterId).Append('|').Append(recipe.AdapterVersion).Append('\n');
			using (SHA256 sha = SHA256.Create())
				return "sha256:" + String.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())).Select(x => x.ToString("x2")));
		}

		private static void ValidateAmendmentDto(AmendmentDto dto, CollectionOperation operation, CollectionPlanIdentity plan,
			string expectedPreviousFingerprint)
		{
			CollectionReplacementBarrierKind kind = dto == null ? CollectionReplacementBarrierKind.Unknown : (CollectionReplacementBarrierKind)dto.Kind;
			if (dto == null || !StringComparer.Ordinal.Equals(dto.Format, AmendmentFormat) ||
				!StringComparer.Ordinal.Equals(dto.OperationId, operation.Identity.OperationId.ToString("D")) ||
				!StringComparer.Ordinal.Equals(dto.PlanId, plan.PlanId.ToString("D")) || dto.PlanVersion != plan.Version ||
				dto.Ordinal < 0 || dto.ObservationCheckpointSequence < 0 || !Enum.IsDefined(typeof(CollectionReplacementBarrierKind), kind) || kind == CollectionReplacementBarrierKind.Unknown ||
				!StringComparer.Ordinal.Equals(dto.PreviousAmendmentFingerprint ?? String.Empty, expectedPreviousFingerprint ?? String.Empty) ||
				String.IsNullOrWhiteSpace(dto.ObservationArtifactId) || String.IsNullOrWhiteSpace(dto.AmendmentFingerprint))
				throw new InvalidDataException("A persisted replacement phase amendment is malformed or breaks the immutable amendment chain.");
		}

		private T ReadArtifact<T>(string artifactId, string description)
		{
			try
			{
				using (Stream stream = _artifactStore.OpenRead(artifactId))
				using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true))
					return JsonConvert.DeserializeObject<T>(reader.ReadToEnd());
			}
			catch (Exception ex) when (!(ex is InvalidDataException))
			{
				throw new InvalidDataException("The retained " + description + " payload is malformed.", ex);
			}
		}

		private static string ObservationRole(long checkpointSequence) { return ObservationRolePrefix + checkpointSequence.ToString("D20", CultureInfo.InvariantCulture); }
		private static string AmendmentRole(long checkpointSequence) { return AmendmentRolePrefix + checkpointSequence.ToString("D20", CultureInfo.InvariantCulture); }

		private static long ParseRoleSequence(string role, string prefix)
		{
			if (String.IsNullOrWhiteSpace(role) || String.IsNullOrWhiteSpace(prefix) || !role.StartsWith(prefix, StringComparison.Ordinal))
				throw new InvalidDataException("A replacement barrier retained role is malformed.");
			long value;
			if (!Int64.TryParse(role.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out value) || value < 0)
				throw new InvalidDataException("A replacement barrier retained role does not contain a valid checkpoint sequence.");
			return value;
		}

		private static CollectionOperation WithOperationState(CollectionOperation operation, CollectionOperationPhase phase,
			CollectionOperationResultState result)
		{
			return new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
				operation.Revision, operation.PlanIdentity, checked(operation.CheckpointSequence + 1), phase, result, operation.NativeChildren);
		}

		private void RequireLiveServices()
		{
			if (_services.ModManager == null) throw new InvalidOperationException("C8.5 requires the live ModManager service.");
		}
	}
}
