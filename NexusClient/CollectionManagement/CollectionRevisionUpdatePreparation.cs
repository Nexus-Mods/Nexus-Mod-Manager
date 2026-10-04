using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModAuthoring;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Prepares one candidate member of an approved C10 revision update without mutating installed native state.</summary>
	public sealed class CollectionRevisionUpdatePreparationMemberState
	{
		internal CollectionRevisionUpdatePreparationMemberState(CollectionRevisionUpdateMemberPlan updateMember,
			CollectionMemberAcquisitionDisposition disposition, CollectionAcquisitionRequest request,
			CollectionVerifiedArchive verifiedArchive, CollectionAcquisitionQueueCorrelation queueCorrelation,
			CollectionManualAcquisitionPendingAction pendingAction, CollectionAcquisitionRestartResult restartResult,
			CollectionPremiumAcquisitionAvailability? premiumAvailability, PreparedCollectionNativeRecipe preparedRecipe)
		{
			UpdateMember = updateMember ?? throw new ArgumentNullException(nameof(updateMember));
			if (updateMember.NewMember == null)
				throw new ArgumentException("Revision-update preparation cannot target a member removed by the candidate revision.", nameof(updateMember));
			if (!Enum.IsDefined(typeof(CollectionMemberAcquisitionDisposition), disposition))
				throw new ArgumentOutOfRangeException(nameof(disposition));
			Disposition = disposition;
			Request = request;
			VerifiedArchive = verifiedArchive;
			QueueCorrelation = queueCorrelation;
			PendingAction = pendingAction;
			RestartResult = restartResult;
			PremiumAvailability = premiumAvailability;
			PreparedRecipe = preparedRecipe;
			if (preparedRecipe != null && !preparedRecipe.Member.MemberKey.Equals(updateMember.MemberKey))
				throw new ArgumentException("A prepared native recipe must belong to the exact revision-update member.", nameof(preparedRecipe));
		}

		public CollectionRevisionUpdateMemberPlan UpdateMember { get; }
		public CollectionMemberAcquisitionDisposition Disposition { get; }
		public CollectionAcquisitionRequest Request { get; }
		public CollectionVerifiedArchive VerifiedArchive { get; }
		public CollectionAcquisitionQueueCorrelation QueueCorrelation { get; }
		public CollectionManualAcquisitionPendingAction PendingAction { get; }
		public CollectionAcquisitionRestartResult RestartResult { get; }
		public CollectionPremiumAcquisitionAvailability? PremiumAvailability { get; }
		public PreparedCollectionNativeRecipe PreparedRecipe { get; }
		public bool IsPrepared { get { return PreparedRecipe != null; } }
	}

	/// <summary>One exact pre-mutation C10.3 acquisition/native-preparation pass for an approved update.</summary>
	public sealed class CollectionRevisionUpdatePreparationBatch
	{
		private readonly ReadOnlyCollection<CollectionRevisionUpdatePreparationMemberState> _members;

		internal CollectionRevisionUpdatePreparationBatch(CollectionOperation operation,
			CollectionRevisionUpdateReviewedIntent reviewedIntent, CollectionRevisionUpdatePlan currentPlan,
			IEnumerable<CollectionRevisionUpdatePreparationMemberState> members,
			CollectionArchiveOverwritePolicy archiveOverwritePolicy)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			ReviewedIntent = reviewedIntent ?? throw new ArgumentNullException(nameof(reviewedIntent));
			CurrentPlan = currentPlan ?? throw new ArgumentNullException(nameof(currentPlan));
			ArchiveOverwritePolicy = archiveOverwritePolicy ?? throw new ArgumentNullException(nameof(archiveOverwritePolicy));
			List<CollectionRevisionUpdatePreparationMemberState> copied = (members ?? throw new ArgumentNullException(nameof(members))).ToList();
			if (copied.Any(x => x == null) || copied.Select(x => x.UpdateMember.MemberKey).Distinct().Count() != copied.Count)
				throw new ArgumentException("A revision-update preparation batch cannot contain null or duplicate member rows.", nameof(members));
			_members = new ReadOnlyCollection<CollectionRevisionUpdatePreparationMemberState>(copied
				.OrderBy(x => x.UpdateMember.MemberKey.Kind).ThenBy(x => x.UpdateMember.MemberKey.Value, StringComparer.Ordinal).ToList());
		}

		public CollectionOperation Operation { get; }
		public CollectionRevisionUpdateReviewedIntent ReviewedIntent { get; }
		public CollectionRevisionUpdatePlan CurrentPlan { get; }
		public CollectionArchiveOverwritePolicy ArchiveOverwritePolicy { get; }
		public ReadOnlyCollection<CollectionRevisionUpdatePreparationMemberState> Members { get { return _members; } }
		public bool IsAwaitingInput { get { return Operation.Phase == CollectionOperationPhase.AwaitingInput; } }
		public bool IsReady { get { return Operation.Phase == CollectionOperationPhase.ReadyToApply && _members.All(x => x.IsPrepared); } }
		public bool HasBlockedMembers { get { return _members.Any(x => x.Disposition == CollectionMemberAcquisitionDisposition.Blocked); } }
		public ReadOnlyCollection<PreparedCollectionNativeRecipe> PreparedRecipes
		{
			get { return new ReadOnlyCollection<PreparedCollectionNativeRecipe>(_members.Where(x => x.PreparedRecipe != null).Select(x => x.PreparedRecipe).ToList()); }
		}
	}

	/// <summary>Native-recipe preparation boundary used by the C10.3 coordinator after exact archive acquisition.</summary>
	public interface ICollectionRevisionUpdateNativeRecipePreparationService
	{
		PreparedCollectionNativeRecipe Prepare(CollectionRevisionUpdatePlan updatePlan,
			CollectionRevisionUpdateMemberPlan updateMember, CollectionVerifiedArchive verifiedArchive,
			CollectionNativeStateIndex currentState, CancellationToken cancellationToken);
	}

	/// <summary>
	/// C10.3 coordinator for exact archive acquisition and candidate native-recipe preparation after C10.2 approval.
	/// Installed native state and Collection association/revision state remain untouched.
	/// </summary>
	public sealed class CollectionRevisionUpdatePreparationCoordinator
	{
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionRevisionUpdateReviewCoordinator _reviewCoordinator;
		private readonly CollectionVerifiedArchiveAdopter _archiveAdopter;
		private readonly CollectionPremiumAcquisitionCoordinator _premiumCoordinator;
		private readonly CollectionManualAcquisitionCoordinator _manualCoordinator;
		private readonly CollectionAcquisitionRestartCoordinator _restartCoordinator;
		private readonly ICollectionBundledMemberAcquisitionCoordinator _bundledCoordinator;
		private readonly CollectionDirectAcquisitionCoordinator _directCoordinator;
		private readonly ICollectionRevisionUpdateNativeRecipePreparationService _recipePreparation;

		public CollectionRevisionUpdatePreparationCoordinator(CollectionsOperationStore operationStore,
			CollectionRevisionUpdateReviewCoordinator reviewCoordinator, CollectionVerifiedArchiveAdopter archiveAdopter,
			CollectionPremiumAcquisitionCoordinator premiumCoordinator, CollectionManualAcquisitionCoordinator manualCoordinator,
			CollectionAcquisitionRestartCoordinator restartCoordinator,
			ICollectionRevisionUpdateNativeRecipePreparationService recipePreparation,
			ICollectionBundledMemberAcquisitionCoordinator bundledCoordinator = null,
			CollectionDirectAcquisitionCoordinator directCoordinator = null)
		{
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_reviewCoordinator = reviewCoordinator ?? throw new ArgumentNullException(nameof(reviewCoordinator));
			_archiveAdopter = archiveAdopter ?? throw new ArgumentNullException(nameof(archiveAdopter));
			_premiumCoordinator = premiumCoordinator ?? throw new ArgumentNullException(nameof(premiumCoordinator));
			_manualCoordinator = manualCoordinator ?? throw new ArgumentNullException(nameof(manualCoordinator));
			_restartCoordinator = restartCoordinator ?? throw new ArgumentNullException(nameof(restartCoordinator));
			_recipePreparation = recipePreparation ?? throw new ArgumentNullException(nameof(recipePreparation));
			_bundledCoordinator = bundledCoordinator;
			_directCoordinator = directCoordinator;
		}

		/// <summary>
		/// Reuses already verified bytes, or starts the existing C4 Premium/manual/bundled acquisition path for candidate members
		/// that need a new native recipe. A durable AwaitingInput checkpoint is written before external acquisition starts.
		/// </summary>
		public CollectionRevisionUpdatePreparationBatch Begin(CollectionOperationIdentity operationIdentity,
			CollectionRevisionUpdatePlan currentPlan, CollectionNativeStateIndex currentState,
			CollectionArchiveOverwritePolicy archiveOverwritePolicy, ConfirmOverwriteCallback confirmOverwriteCallback,
			CancellationToken cancellationToken)
		{
			if (archiveOverwritePolicy == null) throw new ArgumentNullException(nameof(archiveOverwritePolicy));
			ValidateState(currentPlan, currentState);
			CollectionRevisionUpdateReviewedIntent intent = _reviewCoordinator.ValidateApprovedPlan(operationIdentity,
				currentPlan.NewPlan.Identity, currentPlan);
			CollectionOperation operation = RequireOperation(operationIdentity, CollectionOperationPhase.ReadyToApply);
			List<CollectionRevisionUpdateMemberPlan> required = GetPreparationMembers(currentPlan);
			if (required.Count == 0)
				return new CollectionRevisionUpdatePreparationBatch(operation, intent, currentPlan,
					new CollectionRevisionUpdatePreparationMemberState[0], archiveOverwritePolicy);

			var requests = new Dictionary<CollectionMemberKey, CollectionAcquisitionRequest>();
			var verified = new Dictionary<CollectionMemberKey, CollectionVerifiedArchive>();
			foreach (CollectionRevisionUpdateMemberPlan member in required)
			{
				cancellationToken.ThrowIfCancellationRequested();
				CollectionAcquisitionRequest request = CreateRequest(currentPlan.NewPlan, member.MemberKey);
				requests.Add(member.MemberKey, request);
				CollectionVerifiedArchive archive = IsBundled(request)
					? _bundledCoordinator.TryComplete(request, cancellationToken)
					: _archiveAdopter.TryAdopt(request, cancellationToken);
				if (archive != null) verified.Add(member.MemberKey, archive);
			}

			if (verified.Count == required.Count)
				return PrepareReady(operation, intent, currentPlan, currentState, required, requests, verified,
					archiveOverwritePolicy, cancellationToken);

			operation = _reviewCoordinator.AwaitPreparationInput(operationIdentity, currentPlan.NewPlan.Identity, currentPlan);
			var states = new List<CollectionRevisionUpdatePreparationMemberState>(required.Count);
			foreach (CollectionRevisionUpdateMemberPlan member in required)
			{
				CollectionAcquisitionRequest request = requests[member.MemberKey];
				CollectionVerifiedArchive archive;
				if (verified.TryGetValue(member.MemberKey, out archive))
				{
					states.Add(State(member, CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive, request,
						archive, null, null, null, null, null));
					continue;
				}

				if (CollectionBundledArtifactIdentity.IsBundle(request.SelectedArtifact))
				{
					if (!IsBundled(request))
					{
						states.Add(State(member, CollectionMemberAcquisitionDisposition.Blocked, request,
							null, null, null, null, null, null));
						continue;
					}
					CollectionBundledMemberAcquisitionResult bundled = _bundledCoordinator.Begin(request,
						archiveOverwritePolicy, confirmOverwriteCallback, cancellationToken);
					if (bundled.IsManagedArchiveReady)
						states.Add(State(member, CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive, request,
							bundled.VerifiedArchive, null, null, null, null, null));
					else
						states.Add(State(member, CollectionMemberAcquisitionDisposition.BundledQueued, request,
							null, bundled.QueueCorrelation, null, null, null, null));
					continue;
				}

				CollectionAcquisitionQueueCorrelation directCorrelation = _directCoordinator == null ? null : _directCoordinator.TryQueue(request);
				if (directCorrelation != null)
				{
					states.Add(State(member, CollectionMemberAcquisitionDisposition.DirectQueued, request,
						null, directCorrelation, null, null, null, null));
					continue;
				}

				CollectionPremiumAcquisitionAvailability availability = _premiumCoordinator.GetAvailability(request);
				if (availability == CollectionPremiumAcquisitionAvailability.Available)
				{
					CollectionAcquisitionQueueCorrelation correlation = _premiumCoordinator.Queue(request,
						confirmOverwriteCallback, archiveOverwritePolicy);
					states.Add(State(member, CollectionMemberAcquisitionDisposition.PremiumQueued, request,
						null, correlation, null, null, availability, null));
				}
				else
				{
					CollectionManualAcquisitionPendingAction pending = _manualCoordinator.CreatePendingAction(request);
					states.Add(State(member, CollectionMemberAcquisitionDisposition.ManualInputRequired, request,
						null, null, pending, null, availability, null));
				}
			}
			return new CollectionRevisionUpdatePreparationBatch(operation, intent, currentPlan, states, archiveOverwritePolicy);
		}

		/// <summary>Verifies a user-selected local file for one exact pending candidate acquisition.</summary>
		public CollectionVerifiedArchive VerifyLocalFile(CollectionRevisionUpdatePreparationBatch batch,
			CollectionManualAcquisitionPendingAction pendingAction, string localFilePath, CancellationToken cancellationToken)
		{
			if (batch == null) throw new ArgumentNullException(nameof(batch));
			if (pendingAction == null) throw new ArgumentNullException(nameof(pendingAction));
			CollectionRevisionUpdatePreparationMemberState state = batch.Members.SingleOrDefault(x =>
				x.PendingAction != null && x.PendingAction.ActionId == pendingAction.ActionId);
			if (state == null || state.Disposition != CollectionMemberAcquisitionDisposition.ManualInputRequired ||
				state.Request == null || state.Request.RequestId != pendingAction.Request.RequestId)
				throw new ArgumentException("The pending manual acquisition action does not belong to this revision-update preparation.", nameof(pendingAction));
			return _manualCoordinator.VerifyLocalFile(pendingAction, localFilePath, cancellationToken);
		}

		/// <summary>
		/// Re-probes queued/manual acquisition and, only after all required bytes are exact, revalidates the approved three-way
		/// plan against fresh current state before returning to ReadyToApply and preparing candidate native recipes.
		/// </summary>
		public CollectionRevisionUpdatePreparationBatch ProbeCompletedInput(CollectionRevisionUpdatePreparationBatch previous,
			CollectionRevisionUpdatePlan currentPlan, CollectionNativeStateIndex currentState, CancellationToken cancellationToken)
		{
			if (previous == null) throw new ArgumentNullException(nameof(previous));
			if (!previous.IsAwaitingInput)
				throw new InvalidOperationException("Revision-update acquisition can be probed only while the operation is awaiting input.");
			ValidateSameLogicalPlan(previous, currentPlan);
			ValidateState(currentPlan, currentState);

			var states = new List<CollectionRevisionUpdatePreparationMemberState>(previous.Members.Count);
			bool allReady = true;
			foreach (CollectionRevisionUpdatePreparationMemberState prior in previous.Members)
			{
				cancellationToken.ThrowIfCancellationRequested();
				CollectionVerifiedArchive archive = prior.VerifiedArchive;
				if (archive == null && prior.Request != null)
					archive = IsBundled(prior.Request)
						? _bundledCoordinator.TryComplete(prior.Request, cancellationToken)
						: _archiveAdopter.TryAdopt(prior.Request, cancellationToken);
				if (archive != null)
				{
					states.Add(State(prior.UpdateMember, CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive,
						prior.Request, archive, null, null, null, prior.PremiumAvailability, null));
					continue;
				}

				allReady = false;
				CollectionMemberAcquisitionDisposition disposition = prior.Disposition;
				if ((prior.Disposition == CollectionMemberAcquisitionDisposition.PremiumQueued ||
					prior.Disposition == CollectionMemberAcquisitionDisposition.BundledQueued ||
					prior.Disposition == CollectionMemberAcquisitionDisposition.DirectQueued) && prior.QueueCorrelation != null &&
					CollectionAcquisitionConsumerTask.IsTerminal(prior.QueueCorrelation.Task.Status))
					disposition = CollectionMemberAcquisitionDisposition.RestartActionRequired;
				states.Add(State(prior.UpdateMember, disposition, prior.Request, null, prior.QueueCorrelation,
					prior.PendingAction, prior.RestartResult, prior.PremiumAvailability, null));
			}
			if (!allReady)
				return new CollectionRevisionUpdatePreparationBatch(previous.Operation, previous.ReviewedIntent,
					currentPlan, states, previous.ArchiveOverwritePolicy);

			CollectionRevisionUpdateReviewedIntent intent = _reviewCoordinator.ResumeApprovedPreparationInput(
				previous.Operation.Identity, currentPlan.NewPlan.Identity, currentPlan);
			CollectionOperation operation = RequireOperation(previous.Operation.Identity, CollectionOperationPhase.ReadyToApply);
			var requests = states.ToDictionary(x => x.UpdateMember.MemberKey, x => x.Request);
			var verified = states.ToDictionary(x => x.UpdateMember.MemberKey, x => x.VerifiedArchive);
			return PrepareReady(operation, intent, currentPlan, currentState, states.Select(x => x.UpdateMember).ToList(),
				requests, verified, previous.ArchiveOverwritePolicy, cancellationToken);
		}

		/// <summary>Reconstructs C4 acquisition disposition after restart without replaying signed URLs or auto-starting downloads.</summary>
		public CollectionRevisionUpdatePreparationBatch ReconcileRestart(CollectionOperationIdentity operationIdentity,
			CollectionRevisionUpdatePlan currentPlan, CollectionNativeStateIndex currentState, CancellationToken cancellationToken)
		{
			ValidateState(currentPlan, currentState);
			CollectionOperation operation = RequireOperation(operationIdentity, CollectionOperationPhase.AwaitingInput);
			CollectionRevisionUpdateReviewedIntent intent = _reviewCoordinator.LoadReviewedIntent(operationIdentity);
			intent.ValidateCurrentPlan(currentPlan);
			List<CollectionRevisionUpdateMemberPlan> required = GetPreparationMembers(currentPlan);
			var states = new List<CollectionRevisionUpdatePreparationMemberState>(required.Count);
			bool allReady = true;
			foreach (CollectionRevisionUpdateMemberPlan member in required)
			{
				CollectionAcquisitionRequest request = CreateRequest(currentPlan.NewPlan, member.MemberKey);
				CollectionAcquisitionRestartResult restart = _restartCoordinator.Reconcile(request, cancellationToken);
				if (restart.Disposition == CollectionAcquisitionRestartDisposition.VerifiedInputReady && restart.VerifiedArchive != null)
				{
					states.Add(State(member, CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive, request,
						restart.VerifiedArchive, null, null, restart, restart.PremiumAvailability, null));
				}
				else
				{
					allReady = false;
					states.Add(State(member, CollectionMemberAcquisitionDisposition.RestartActionRequired, request,
						null, null, null, restart, restart.PremiumAvailability, null));
				}
			}
			if (!allReady)
				return new CollectionRevisionUpdatePreparationBatch(operation, intent, currentPlan, states, CollectionArchiveOverwritePolicy.Prompt);

			intent = _reviewCoordinator.ResumeApprovedPreparationInput(operationIdentity, currentPlan.NewPlan.Identity, currentPlan);
			operation = RequireOperation(operationIdentity, CollectionOperationPhase.ReadyToApply);
			return PrepareReady(operation, intent, currentPlan, currentState, required,
				states.ToDictionary(x => x.UpdateMember.MemberKey, x => x.Request),
				states.ToDictionary(x => x.UpdateMember.MemberKey, x => x.VerifiedArchive),
				CollectionArchiveOverwritePolicy.Prompt, cancellationToken);
		}

		private CollectionRevisionUpdatePreparationBatch PrepareReady(CollectionOperation operation,
			CollectionRevisionUpdateReviewedIntent intent, CollectionRevisionUpdatePlan currentPlan,
			CollectionNativeStateIndex currentState, IList<CollectionRevisionUpdateMemberPlan> required,
			IDictionary<CollectionMemberKey, CollectionAcquisitionRequest> requests,
			IDictionary<CollectionMemberKey, CollectionVerifiedArchive> verified,
			CollectionArchiveOverwritePolicy archiveOverwritePolicy, CancellationToken cancellationToken)
		{
			var states = new List<CollectionRevisionUpdatePreparationMemberState>(required.Count);
			foreach (CollectionRevisionUpdateMemberPlan member in required)
			{
				cancellationToken.ThrowIfCancellationRequested();
				CollectionVerifiedArchive archive;
				if (!verified.TryGetValue(member.MemberKey, out archive) || archive == null)
					throw new InvalidOperationException("Revision-update preparation lost the exact verified candidate archive for " + member.MemberKey + ".");
				PreparedCollectionNativeRecipe prepared = _recipePreparation.Prepare(currentPlan, member, archive, currentState, cancellationToken);
				ValidatePreparedRecipe(intent, currentPlan, member, prepared);
				states.Add(State(member, CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive,
					requests[member.MemberKey], archive, null, null, null, null, prepared));
			}
			return new CollectionRevisionUpdatePreparationBatch(operation, intent, currentPlan, states, archiveOverwritePolicy);
		}

		private static void ValidatePreparedRecipe(CollectionRevisionUpdateReviewedIntent intent,
			CollectionRevisionUpdatePlan currentPlan, CollectionRevisionUpdateMemberPlan member,
			PreparedCollectionNativeRecipe prepared)
		{
			if (prepared == null) throw new InvalidOperationException("Revision-update native preparation returned no recipe.");
			if (!prepared.Member.MemberKey.Equals(member.MemberKey) || !prepared.Member.RecipeIdentity.Equals(member.NewMember.RecipeIdentity) ||
				!prepared.Member.ArtifactChoice.Equals(member.NewMember.ArtifactChoice))
				throw new InvalidOperationException("Prepared native output does not describe the exact candidate member reviewed for update.");
			CollectionRevisionUpdateReviewEntry reviewed = intent.Members.Single(x => x.MemberKey.Equals(member.MemberKey));
			if (!String.IsNullOrWhiteSpace(reviewed.NewPreparedFingerprint) &&
				!StringComparer.Ordinal.Equals(reviewed.NewPreparedFingerprint, prepared.PreparedNativeIdentity.Fingerprint))
				throw new InvalidOperationException("Candidate native preparation changed after revision-update review; a new three-way review is required before mutation.");
			if (!StringComparer.Ordinal.Equals(prepared.RecipeInput.OperationIdentity.Fingerprint.RecipeFingerprint, member.NewMember.RecipeIdentity.Fingerprint))
				throw new InvalidOperationException("Prepared native recipe operation identity no longer matches the candidate provider recipe identity.");
			if (!StringComparer.Ordinal.Equals(prepared.RecipeInput.OperationIdentity.Fingerprint.TargetFingerprint, currentPlan.NewPlan.Target.Fingerprint))
				throw new InvalidOperationException("Prepared native recipe operation identity no longer matches the reviewed target.");
		}

		private static List<CollectionRevisionUpdateMemberPlan> GetPreparationMembers(CollectionRevisionUpdatePlan plan)
		{
			return plan.Members.Where(RequiresCandidatePreparation)
				.OrderBy(x => x.MemberKey.Kind).ThenBy(x => x.MemberKey.Value, StringComparer.Ordinal).ToList();
		}

		private static bool RequiresCandidatePreparation(CollectionRevisionUpdateMemberPlan member)
		{
			if (member == null || member.NewMember == null || member.ChangeKind == CollectionRevisionUpdateChangeKind.Removed)
				return false;
			if (member.ChangeKind != CollectionRevisionUpdateChangeKind.Unchanged)
				return true;
			return member.PreparationKind == CollectionRevisionUpdatePreparationKind.PreparedForCandidate ||
				member.PreparationKind == CollectionRevisionUpdatePreparationKind.CandidateChanged ||
				member.PreparationKind == CollectionRevisionUpdatePreparationKind.ReprepareRequired;
		}

		private static CollectionAcquisitionRequest CreateRequest(ResolvedCollectionPlan candidatePlan, CollectionMemberKey memberKey)
		{
			return CollectionAcquisitionRequest.Create(CollectionMemberAcquisitionCoordinator.CreateStableRequestId(candidatePlan.Identity, memberKey),
				candidatePlan, memberKey);
		}

		private bool IsBundled(CollectionAcquisitionRequest request)
		{
			return request != null && _bundledCoordinator != null && _bundledCoordinator.Supports(request.SelectedArtifact);
		}

		private CollectionOperation RequireOperation(CollectionOperationIdentity identity, CollectionOperationPhase phase)
		{
			if (identity == null) throw new ArgumentNullException(nameof(identity));
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Kind != CollectionOperationKind.UpdateRevision)
				throw new InvalidOperationException("C10.3 requires an existing UpdateRevision operation.");
			if (operation.Phase != phase || operation.HasCrossedNativeBoundary)
				throw new InvalidOperationException("C10.3 revision-update preparation is valid only before native mutation at phase " + phase + ".");
			return operation;
		}

		private static void ValidateState(CollectionRevisionUpdatePlan plan, CollectionNativeStateIndex state)
		{
			if (plan == null) throw new ArgumentNullException(nameof(plan));
			if (state == null) throw new ArgumentNullException(nameof(state));
			if (!plan.NewPlan.Target.Equals(state.Target) || !plan.ObservedStateFingerprint.Equals(state.Fingerprint) ||
				!plan.NewPlan.CurrentStateFingerprint.Equals(state.Fingerprint))
				throw new InvalidOperationException("Revision-update preparation requires the exact authoritative native-state snapshot approved by the three-way review.");
		}

		private static void ValidateSameLogicalPlan(CollectionRevisionUpdatePreparationBatch previous, CollectionRevisionUpdatePlan current)
		{
			if (current == null) throw new ArgumentNullException(nameof(current));
			if (!previous.CurrentPlan.NewPlan.Identity.Equals(current.NewPlan.Identity) ||
				!previous.CurrentPlan.OldPlan.Identity.Equals(current.OldPlan.Identity) ||
				previous.CurrentPlan.Association.AssociationId != current.Association.AssociationId)
				throw new InvalidOperationException("Revision-update acquisition cannot resume against a different reviewed old/new plan.");
		}

		private static CollectionRevisionUpdatePreparationMemberState State(CollectionRevisionUpdateMemberPlan member,
			CollectionMemberAcquisitionDisposition disposition, CollectionAcquisitionRequest request,
			CollectionVerifiedArchive archive, CollectionAcquisitionQueueCorrelation correlation,
			CollectionManualAcquisitionPendingAction pendingAction, CollectionAcquisitionRestartResult restartResult,
			CollectionPremiumAcquisitionAvailability? availability, PreparedCollectionNativeRecipe prepared)
		{
			return new CollectionRevisionUpdatePreparationMemberState(member, disposition, request, archive,
				correlation, pendingAction, restartResult, availability, prepared);
		}
	}
}
