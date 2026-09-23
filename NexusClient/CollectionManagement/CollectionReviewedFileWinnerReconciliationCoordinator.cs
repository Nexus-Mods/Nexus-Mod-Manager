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
		private const string IntentFormat = "nmm-ce.collections.file-winner-intent/1";
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
		private readonly IModDeploymentManager _deploymentManager;
		private readonly IVirtualDeploymentService _virtualDeploymentService;
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
			_deploymentManager = _services.ModManager.DeploymentManager ?? throw new InvalidOperationException("C6.15.11 requires the native deployment manager.");
			_virtualDeploymentService = new VirtualDeploymentService(_services.ModManager.VirtualModActivator, _deploymentManager);
			_captureState = target => new CollectionNativeStateReader(_services.ModManager.InstallationLog,
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
			_deploymentManager = deploymentManager ?? throw new ArgumentNullException(nameof(deploymentManager));
			_virtualDeploymentService = virtualDeploymentService ?? throw new ArgumentNullException(nameof(virtualDeploymentService));
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
			ValidateInputs(operationIdentity, plan, matches, impactPlan);
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(plan.Target)) throw new InvalidOperationException("The live canonical target no longer matches the reviewed Collection plan.");
			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				cancellationToken.ThrowIfCancellationRequested();
				_authorityValidator.ValidateAndReload(lease, authority, paths);
				return ReconcileValidated(operationIdentity, plan, matches, impactPlan, cancellationToken);
			}
		}

		/// <summary>Reconciles reviewed winners without cancellation.</summary>
		public Task<CollectionReviewedFileWinnerReconciliationResult> ReconcileAsync(CollectionOperationIdentity operationIdentity,
			ResolvedCollectionPlan plan, CollectionMemberMatchSet matches, CollectionConflictImpactPlan impactPlan,
			GameStoragePathSet paths)
		{
			return ReconcileAsync(operationIdentity, plan, matches, impactPlan, paths, CancellationToken.None);
		}

		internal CollectionReviewedFileWinnerReconciliationResult ReconcileValidated(CollectionOperationIdentity operationIdentity,
			ResolvedCollectionPlan plan, CollectionMemberMatchSet matches, CollectionConflictImpactPlan impactPlan,
			CancellationToken cancellationToken)
		{
			ValidateInputs(operationIdentity, plan, matches, impactPlan);
			var results = new List<CollectionReviewedFileWinnerResult>();
			foreach (CollectionFileImpact impact in impactPlan.FileImpacts
				.Where(x => x.PlannedWinner != null && x.Writers.Count > 1)
				.OrderBy(x => (int)x.Target.Root).ThenBy(x => x.Target.RelativePath, StringComparer.OrdinalIgnoreCase))
			{
				cancellationToken.ThrowIfCancellationRequested();
				CollectionNativeStateIndex state = _captureState(plan.Target);
				CollectionNativeFileState file = RequireLiveFile(state, impact.Target);
				Dictionary<CollectionMemberKey, string> writerOwners = ResolveWriterOwners(plan, matches, impact, file);
				string desiredOwner = writerOwners[impact.PlannedWinner];
				CollectionReviewedFileWinnerDispatchKind dispatch = ResolveDispatch(file);
				if (StringComparer.OrdinalIgnoreCase.Equals(file.EffectiveOwnerKey, desiredOwner))
				{
					CollectionReviewedFileWinnerIntent existing = LoadIntent(operationIdentity, plan, impact.Target);
					if (existing != null)
					{
						ValidateIntent(existing, operationIdentity, plan, impact.Target, desiredOwner, dispatch);
						if (!OwnerStacksEqual(GetOwnerPreimage(file), GetExpectedPostimage(existing)))
							throw new CollectionReviewedFileWinnerRecoveryRequiredException("The native owner stack no longer matches the durable reviewed-winner postimage.");
						if (!IsVerified(operationIdentity, existing))
						{
							_updateProfileDeployment();
							MarkVerified(operationIdentity, existing);
						}
						results.Add(new CollectionReviewedFileWinnerResult(impact.Target, desiredOwner, dispatch,
							CollectionReviewedFileWinnerOutcome.RecoveredCommitted));
					}
					else
						results.Add(new CollectionReviewedFileWinnerResult(impact.Target, desiredOwner, dispatch,
							CollectionReviewedFileWinnerOutcome.AlreadySatisfied));
					continue;
				}

				if (!writerOwners.Values.Contains(file.EffectiveOwnerKey, StringComparer.Ordinal))
					throw new CollectionReviewedFileWinnerRecoveryRequiredException("The current native file winner is no longer one of the reviewed incoming writer owners.");

				CollectionReviewedFileWinnerIntent intent = LoadIntent(operationIdentity, plan, impact.Target);
				if (intent == null)
				{
					intent = new CollectionReviewedFileWinnerIntent(operationIdentity, plan.Identity, impact.Target, dispatch,
						file.EffectiveOwnerKey, desiredOwner, GetOwnerPreimage(file));
					SaveIntent(intent);
				}
				else
				{
					ValidateIntent(intent, operationIdentity, plan, impact.Target, desiredOwner, dispatch);
					if (IsVerified(operationIdentity, intent))
						throw new CollectionReviewedFileWinnerRecoveryRequiredException("The durable winner checkpoint says the switch was verified but native state reports another owner.");
					if (!StringComparer.OrdinalIgnoreCase.Equals(file.EffectiveOwnerKey, intent.PreviousOwnerKey))
						throw new CollectionReviewedFileWinnerRecoveryRequiredException("The native file winner no longer matches either the durable preimage or the reviewed winner.");
					if (!OwnerStacksEqual(GetOwnerPreimage(file), intent.PreimageOwners))
						throw new CollectionReviewedFileWinnerRecoveryRequiredException("The native owner stack changed after the reviewed-winner intent was persisted.");
				}

				ApplyWinner(intent);
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
			CollectionMemberMatchSet matches, CollectionConflictImpactPlan impactPlan)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			if (plan == null) throw new ArgumentNullException(nameof(plan));
			if (matches == null) throw new ArgumentNullException(nameof(matches));
			if (impactPlan == null) throw new ArgumentNullException(nameof(impactPlan));
			if (plan.Policy.Kind != CollectionExecutionPolicyKind.InstallIntoCurrentSetup)
				throw new ArgumentException("C6.15.11 only reconciles additive Collection plans.", nameof(plan));
			if (!matches.PlanIdentity.Equals(plan.Identity) || !matches.Target.Equals(plan.Target) ||
				!impactPlan.PlanIdentity.Equals(plan.Identity) || !impactPlan.Target.Equals(plan.Target))
				throw new ArgumentException("Winner reconciliation inputs do not belong to the exact reviewed Collection plan.");
			if (!impactPlan.IsReady) throw new InvalidOperationException("Only a Ready C6.4 impact plan has executable reviewed winners.");
			if (matches.HasBlockedMembers || matches.HasAcquisitionRequired)
				throw new InvalidOperationException("C6.15.11 requires the complete selected member closure to be resolved.");

			CollectionOperation operation = _operationStore.GetOperation(operationIdentity);
			if (operation == null || operation.Kind != CollectionOperationKind.ApplyResolvedPlan || operation.ResultState != CollectionOperationResultState.Pending ||
				(operation.Phase != CollectionOperationPhase.ApplyingNativeChildren && operation.Phase != CollectionOperationPhase.PausedAtSafeBoundary && operation.Phase != CollectionOperationPhase.Recovering) ||
				operation.PlanIdentity == null || !operation.PlanIdentity.Equals(plan.Identity) || operation.Revision == null || !operation.Revision.Equals(plan.Revision) || !operation.Target.Equals(plan.Target))
				throw new InvalidOperationException("C6.15.11 requires the active operation for the exact reviewed additive plan.");
			if (operation.NativeChildren.Any(x => !x.IsReconciled))
				throw new InvalidOperationException("Reviewed file winners may be reconciled only after every submitted native child reached a reconciled safe boundary.");
			CollectionResolvedPlanRecord persisted = _planStore.GetPlan(plan.Identity);
			if (persisted == null || !persisted.Revision.Equals(plan.Revision) || !persisted.Target.Equals(plan.Target))
				throw new InvalidOperationException("The exact reviewed Collection plan is not durably persisted.");
			if (!StringComparer.Ordinal.Equals(persisted.PayloadFormat, CollectionReviewedWorkflowSnapshotCodec.PayloadFormat))
				throw new InvalidOperationException("C6.15.11 requires the exact durable reviewed-workflow v2 snapshot.");
			CollectionReviewedWorkflowSnapshot reviewed = CollectionReviewedWorkflowSnapshotCodec.Deserialize(persisted.Payload);
			ValidateReviewedWinners(reviewed, plan, impactPlan);
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

		private static List<string> GetExpectedPostimage(CollectionReviewedFileWinnerIntent intent)
		{
			var owners = intent.PreimageOwners
				.Where(x => !StringComparer.OrdinalIgnoreCase.Equals(x, intent.DesiredOwnerKey)).ToList();
			owners.Add(intent.DesiredOwnerKey);
			return owners;
		}

		private static bool OwnerStacksEqual(IEnumerable<string> left, IEnumerable<string> right)
		{
			if (left == null || right == null) return false;
			return left.SequenceEqual(right, StringComparer.OrdinalIgnoreCase);
		}

		private void ApplyWinner(CollectionReviewedFileWinnerIntent intent)
		{
			if (intent.DispatchKind == CollectionReviewedFileWinnerDispatchKind.Promoted)
			{
				_deploymentManager.SwitchPromotedOwner(intent.Target, intent.DesiredOwnerKey);
				return;
			}
			VirtualFileOwnerSwitchResult result = _virtualDeploymentService.SwitchFileOwner(intent.Target.RelativePath, intent.DesiredOwnerKey);
			if (result == null || !result.Success || !StringComparer.OrdinalIgnoreCase.Equals(result.SelectedOwnerKey, intent.DesiredOwnerKey))
				throw new InvalidOperationException("The native Virtual owner switch did not report the reviewed owner as selected.", result == null ? null : result.Failure);
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
			ResolvedCollectionPlan plan, ModDeploymentTarget target, string desiredOwner, CollectionReviewedFileWinnerDispatchKind dispatch)
		{
			if (!intent.OperationIdentity.Equals(operationIdentity) || !intent.PlanIdentity.Equals(plan.Identity) || !intent.Target.Equals(target) ||
				intent.DispatchKind != dispatch || !StringComparer.OrdinalIgnoreCase.Equals(intent.DesiredOwnerKey, desiredOwner))
				throw new InvalidDataException("The durable reviewed-winner intent contradicts the exact approved winner decision.");
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
				string desiredOwnerKey, IEnumerable<string> preimageOwners)
			{
				OperationIdentity = operationIdentity ?? throw new ArgumentNullException(nameof(operationIdentity));
				PlanIdentity = planIdentity ?? throw new ArgumentNullException(nameof(planIdentity));
				Target = target ?? throw new ArgumentNullException(nameof(target));
				if (!Enum.IsDefined(typeof(CollectionReviewedFileWinnerDispatchKind), dispatchKind) || dispatchKind == CollectionReviewedFileWinnerDispatchKind.Unknown)
					throw new ArgumentOutOfRangeException(nameof(dispatchKind));
				DispatchKind = dispatchKind;
				PreviousOwnerKey = CollectionIdentityValidation.RequireOpaqueToken(previousOwnerKey, nameof(previousOwnerKey));
				DesiredOwnerKey = CollectionIdentityValidation.RequireOpaqueToken(desiredOwnerKey, nameof(desiredOwnerKey));
				_preimageOwners = new ReadOnlyCollection<string>((preimageOwners ?? throw new ArgumentNullException(nameof(preimageOwners)))
					.Select(x => CollectionIdentityValidation.RequireOpaqueToken(x, nameof(preimageOwners))).ToList());
				if (_preimageOwners.Count == 0 || !_preimageOwners.Contains(PreviousOwnerKey, StringComparer.OrdinalIgnoreCase) ||
					!_preimageOwners.Contains(DesiredOwnerKey, StringComparer.OrdinalIgnoreCase) ||
					!StringComparer.OrdinalIgnoreCase.Equals(_preimageOwners[_preimageOwners.Count - 1], PreviousOwnerKey))
					throw new ArgumentException("The winner preimage must contain both managed owners and end with the previous effective owner.", nameof(preimageOwners));
			}

			public CollectionOperationIdentity OperationIdentity { get; }
			public CollectionPlanIdentity PlanIdentity { get; }
			public ModDeploymentTarget Target { get; }
			public CollectionReviewedFileWinnerDispatchKind DispatchKind { get; }
			public string PreviousOwnerKey { get; }
			public string DesiredOwnerKey { get; }
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
				writer.Write(_preimageOwners.Count);
				foreach (string owner in _preimageOwners) writer.Write(owner);
			}

			public static CollectionReviewedFileWinnerIntent Deserialize(BinaryReader reader)
			{
				if (!StringComparer.Ordinal.Equals(reader.ReadString(), IntentFormat)) throw new InvalidDataException("Unsupported reviewed-winner intent format.");
				Guid operationId = Guid.Parse(reader.ReadString());
				ModOperationOrigin origin = (ModOperationOrigin)reader.ReadInt32();
				if (origin != ModOperationOrigin.Collection) throw new InvalidDataException("Reviewed-winner intents must record Collection native origin.");
				CollectionOperationIdentity operation = CollectionOperationIdentity.From(operationId);
				CollectionPlanIdentity plan = CollectionPlanIdentity.From(Guid.Parse(reader.ReadString()), reader.ReadInt32());
				ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical((ModDeploymentRoot)reader.ReadInt32(), reader.ReadString());
				var dispatch = (CollectionReviewedFileWinnerDispatchKind)reader.ReadInt32();
				string previous = reader.ReadString();
				string desired = reader.ReadString();
				int count = reader.ReadInt32();
				if (count <= 0 || count > 4096) throw new InvalidDataException("Invalid reviewed-winner preimage owner count.");
				var owners = new List<string>(count);
				for (int i = 0; i < count; i++) owners.Add(reader.ReadString());
				return new CollectionReviewedFileWinnerIntent(operation, plan, target, dispatch, previous, desired, owners);
			}
		}
	}
}
