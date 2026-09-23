using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModAuthoring;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Describes the C6.15.5 acquisition state for one selected Collection member.
	/// </summary>
	public enum CollectionMemberAcquisitionDisposition
	{
		ReadyInstalled = 1,
		ReadyVerifiedArchive = 2,
		PremiumQueued = 3,
		ManualInputRequired = 4,
		RestartActionRequired = 5,
		Blocked = 6
	}

	/// <summary>
	/// Immutable acquisition/preparation state for one selected member of an additive plan.
	/// </summary>
	public sealed class CollectionMemberAcquisitionState
	{
		internal CollectionMemberAcquisitionState(CollectionMemberMatchResult match,
			CollectionMemberAcquisitionDisposition disposition, CollectionAcquisitionRequest request,
			CollectionVerifiedArchive verifiedArchive, CollectionAcquisitionQueueCorrelation queueCorrelation,
			CollectionManualAcquisitionPendingAction pendingAction, CollectionAcquisitionRestartResult restartResult,
			CollectionPremiumAcquisitionAvailability? premiumAvailability)
		{
			Match = match ?? throw new ArgumentNullException(nameof(match));
			if (!Enum.IsDefined(typeof(CollectionMemberAcquisitionDisposition), disposition))
				throw new ArgumentOutOfRangeException(nameof(disposition));
			Disposition = disposition;
			Request = request;
			VerifiedArchive = verifiedArchive;
			QueueCorrelation = queueCorrelation;
			PendingAction = pendingAction;
			RestartResult = restartResult;
			PremiumAvailability = premiumAvailability;
		}

		public CollectionMemberMatchResult Match { get; }
		public CollectionMemberAcquisitionDisposition Disposition { get; }
		public CollectionAcquisitionRequest Request { get; }
		public CollectionVerifiedArchive VerifiedArchive { get; }
		public CollectionAcquisitionQueueCorrelation QueueCorrelation { get; }
		public CollectionManualAcquisitionPendingAction PendingAction { get; }
		public CollectionAcquisitionRestartResult RestartResult { get; }
		public CollectionPremiumAcquisitionAvailability? PremiumAvailability { get; }

		/// <summary>Gets whether the member currently has all archive input required for later recipe preparation.</summary>
		public bool IsReady
		{
			get
			{
				return Disposition == CollectionMemberAcquisitionDisposition.ReadyInstalled ||
					Disposition == CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive;
			}
		}
	}

	/// <summary>
	/// Immutable C6.15.5 acquisition snapshot for one exact additive plan.
	/// </summary>
	public sealed class CollectionMemberAcquisitionBatch
	{
		private readonly ReadOnlyCollection<CollectionMemberAcquisitionState> _members;

		internal CollectionMemberAcquisitionBatch(CollectionAdditivePlanBuildResult planBuild,
			CollectionMemberMatchSet matchSet, IEnumerable<CollectionMemberAcquisitionState> members)
		{
			PlanBuild = planBuild ?? throw new ArgumentNullException(nameof(planBuild));
			MatchSet = matchSet ?? throw new ArgumentNullException(nameof(matchSet));
			if (members == null)
				throw new ArgumentNullException(nameof(members));
			List<CollectionMemberAcquisitionState> copied = members.ToList();
			if (copied.Count != planBuild.Plan.SelectedMembers.Count || copied.Any(x => x == null))
				throw new ArgumentException("An acquisition batch must contain one non-null state for every selected member.", nameof(members));
			_members = new ReadOnlyCollection<CollectionMemberAcquisitionState>(copied);
		}

		public CollectionAdditivePlanBuildResult PlanBuild { get; }
		public CollectionMemberMatchSet MatchSet { get; }
		public ReadOnlyCollection<CollectionMemberAcquisitionState> Members { get { return _members; } }
		public bool IsReady { get { return _members.All(x => x.IsReady); } }
		public bool HasBlockedMembers { get { return _members.Any(x => x.Disposition == CollectionMemberAcquisitionDisposition.Blocked); } }
		public bool IsAwaitingInput { get { return PlanBuild.Operation.Phase == CollectionOperationPhase.AwaitingInput; } }
	}

	/// <summary>
	/// Composes C6.2 matching with the existing C4 verified-reuse, Premium, manual and restart acquisition primitives.
	/// </summary>
	/// <remarks>
	/// This coordinator never downloads outside the existing AddMod path and never acquires a Collection target mutation lease.
	/// It performs no native installation. Network/user pauses are represented by the existing C6.5 AwaitingInput boundary.
	/// </remarks>
	public sealed class CollectionMemberAcquisitionCoordinator
	{
		private const string RequestIdentityFormat = "nmm-ce.collections.member-acquisition/1";
		private readonly CollectionMemberMatchEngine _matchEngine;
		private readonly CollectionVerifiedArchiveAdopter _archiveAdopter;
		private readonly CollectionPremiumAcquisitionCoordinator _premiumCoordinator;
		private readonly CollectionManualAcquisitionCoordinator _manualCoordinator;
		private readonly CollectionAcquisitionRestartCoordinator _restartCoordinator;
		private readonly CollectionOperationCoordinator _operationCoordinator;

		/// <summary>Creates the C6.15.5 member-acquisition composition service.</summary>
		public CollectionMemberAcquisitionCoordinator(CollectionMemberMatchEngine matchEngine,
			CollectionVerifiedArchiveAdopter archiveAdopter, CollectionPremiumAcquisitionCoordinator premiumCoordinator,
			CollectionManualAcquisitionCoordinator manualCoordinator, CollectionAcquisitionRestartCoordinator restartCoordinator,
			CollectionOperationCoordinator operationCoordinator)
		{
			_matchEngine = matchEngine ?? throw new ArgumentNullException(nameof(matchEngine));
			_archiveAdopter = archiveAdopter ?? throw new ArgumentNullException(nameof(archiveAdopter));
			_premiumCoordinator = premiumCoordinator ?? throw new ArgumentNullException(nameof(premiumCoordinator));
			_manualCoordinator = manualCoordinator ?? throw new ArgumentNullException(nameof(manualCoordinator));
			_restartCoordinator = restartCoordinator ?? throw new ArgumentNullException(nameof(restartCoordinator));
			_operationCoordinator = operationCoordinator ?? throw new ArgumentNullException(nameof(operationCoordinator));
		}

		/// <summary>
		/// Resolves immediately reusable archives, queues available Premium work and exposes free/manual pending actions.
		/// </summary>
		public CollectionMemberAcquisitionBatch Begin(CollectionAdditivePlanBuildResult planBuild,
			ConfirmOverwriteCallback confirmOverwriteCallback, CancellationToken cancellationToken)
		{
			ValidatePreparingPlan(planBuild);
			ResolvedCollectionPlan plan = planBuild.Plan;
			CollectionNativeStateIndex nativeState = planBuild.NativeState;
			CollectionMemberMatchSet initial = _matchEngine.Match(plan, nativeState);
			if (initial.HasBlockedMembers)
				return BuildBlockedBatch(planBuild, initial);

			var requests = new Dictionary<CollectionMemberKey, CollectionAcquisitionRequest>();
			var verifiedArchives = new List<CollectionVerifiedArchive>();
			foreach (CollectionMemberMatchResult match in initial.Members)
			{
				if (!RequiresArchiveInput(match))
					continue;
				CollectionAcquisitionRequest request = CreateRequest(plan, match.Member.MemberKey);
				requests.Add(match.Member.MemberKey, request);
				CollectionVerifiedArchive verified = _archiveAdopter.TryAdopt(request, cancellationToken);
				if (verified != null)
					verifiedArchives.Add(verified);
			}

			CollectionMemberMatchSet rematched = _matchEngine.Match(plan, nativeState, verifiedArchives);
			if (rematched.HasBlockedMembers)
				return BuildBlockedBatch(planBuild, rematched);

			bool awaitingInput = rematched.Members.Any(x =>
				x.Disposition == CollectionMemberMatchDisposition.AcquisitionRequired ||
				x.Disposition == CollectionMemberMatchDisposition.ReinstallRequired);
			CollectionAdditivePlanBuildResult updatedBuild = planBuild;
			if (awaitingInput)
			{
				// Make the durable pause visible before any Premium queue/user-mediated acquisition side effect begins.
				CollectionOperation operation = _operationCoordinator.AwaitInput(planBuild.Operation.Identity);
				updatedBuild = new CollectionAdditivePlanBuildResult(operation, planBuild.Plan, planBuild.NativeState);
			}

			var states = new List<CollectionMemberAcquisitionState>(rematched.Members.Count);
			foreach (CollectionMemberMatchResult match in rematched.Members)
			{
				CollectionAcquisitionRequest request;
				requests.TryGetValue(match.Member.MemberKey, out request);
				if (match.Disposition == CollectionMemberMatchDisposition.InstalledCompatible)
				{
					states.Add(State(match, CollectionMemberAcquisitionDisposition.ReadyInstalled, null, null, null, null, null, null));
					continue;
				}
				if (match.VerifiedArchive != null)
				{
					states.Add(State(match, CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive,
						request ?? match.VerifiedArchive.Request, match.VerifiedArchive, null, null, null, null));
					continue;
				}
				if (match.Disposition != CollectionMemberMatchDisposition.AcquisitionRequired &&
					match.Disposition != CollectionMemberMatchDisposition.ReinstallRequired)
					throw new InvalidOperationException("C6.15.5 encountered an acquisition state that cannot be routed safely.");

				if (request == null)
				{
					request = CreateRequest(plan, match.Member.MemberKey);
					requests[match.Member.MemberKey] = request;
				}

				CollectionPremiumAcquisitionAvailability availability = _premiumCoordinator.GetAvailability(request);
				if (availability == CollectionPremiumAcquisitionAvailability.Available)
				{
					CollectionAcquisitionQueueCorrelation correlation = _premiumCoordinator.Queue(request, confirmOverwriteCallback);
					states.Add(State(match, CollectionMemberAcquisitionDisposition.PremiumQueued,
						request, null, correlation, null, null, availability));
				}
				else
				{
					CollectionManualAcquisitionPendingAction pending = _manualCoordinator.CreatePendingAction(request);
					states.Add(State(match, CollectionMemberAcquisitionDisposition.ManualInputRequired,
						request, null, null, pending, null, availability));
				}
			}

			return new CollectionMemberAcquisitionBatch(updatedBuild, rematched, states);
		}

		/// <summary>
		/// Rechecks immutable archive availability after queued or user-mediated work without changing the native target snapshot.
		/// </summary>
		/// <remarks>
		/// When this returns a ready batch, the caller must run post-pause target/native-state revalidation before later planning.
		/// </remarks>
		public CollectionMemberAcquisitionBatch ProbeCompletedInput(CollectionMemberAcquisitionBatch batch,
			CancellationToken cancellationToken)
		{
			if (batch == null)
				throw new ArgumentNullException(nameof(batch));
			if (!batch.IsAwaitingInput)
				throw new InvalidOperationException("Acquisition input can be probed only while the Collection operation is awaiting input.");

			var verified = new List<CollectionVerifiedArchive>();
			foreach (CollectionMemberAcquisitionState state in batch.Members)
			{
				if (state.VerifiedArchive != null)
				{
					verified.Add(state.VerifiedArchive);
					continue;
				}
				if (state.Request == null)
					continue;
				CollectionVerifiedArchive archive = _archiveAdopter.TryAdopt(state.Request, cancellationToken);
				if (archive != null)
					verified.Add(archive);
			}

			CollectionMemberMatchSet rematched = _matchEngine.Match(batch.PlanBuild.Plan, batch.PlanBuild.NativeState, verified);
			var states = new List<CollectionMemberAcquisitionState>(rematched.Members.Count);
			foreach (CollectionMemberMatchResult match in rematched.Members)
			{
				CollectionMemberAcquisitionState previous = batch.Members.First(x => x.Match.Member.MemberKey.Equals(match.Member.MemberKey));
				if (match.Disposition == CollectionMemberMatchDisposition.InstalledCompatible)
					states.Add(State(match, CollectionMemberAcquisitionDisposition.ReadyInstalled, null, null, null, null, null, null));
				else if (match.VerifiedArchive != null)
					states.Add(State(match, CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive,
						previous.Request ?? match.VerifiedArchive.Request, match.VerifiedArchive, null, null, null, previous.PremiumAvailability));
				else if (match.IsBlocked)
					states.Add(State(match, CollectionMemberAcquisitionDisposition.Blocked, previous.Request, null, null, null, null, previous.PremiumAvailability));
				else
					states.Add(State(match, previous.Disposition, previous.Request, null, previous.QueueCorrelation,
						previous.PendingAction, previous.RestartResult, previous.PremiumAvailability));
			}
			return new CollectionMemberAcquisitionBatch(batch.PlanBuild, rematched, states);
		}

		/// <summary>
		/// Rebinds a fully ready acquisition batch to a post-pause refreshed plan and reruns C6.2 against the new native-state snapshot.
		/// </summary>
		/// <remarks>
		/// When a fingerprint change increments the plan version, verified C4 bytes are protected by a new request reference rather
		/// than downloaded again. The exact revision, target, member artifact and recipe are required to remain unchanged.
		/// </remarks>
		public CollectionMemberAcquisitionBatch RebindReadyBatch(CollectionMemberAcquisitionBatch previousBatch,
			CollectionAdditivePlanBuildResult refreshedPlanBuild, CancellationToken cancellationToken)
		{
			if (previousBatch == null)
				throw new ArgumentNullException(nameof(previousBatch));
			if (refreshedPlanBuild == null)
				throw new ArgumentNullException(nameof(refreshedPlanBuild));
			if (!previousBatch.IsReady)
				throw new InvalidOperationException("Only a fully verified acquisition batch may be rebound after pause revalidation.");
			ValidatePreparingPlan(refreshedPlanBuild);
			if (!previousBatch.PlanBuild.Plan.Revision.Equals(refreshedPlanBuild.Plan.Revision) ||
				!previousBatch.PlanBuild.Plan.Target.Equals(refreshedPlanBuild.Plan.Target))
				throw new ArgumentException("Post-pause acquisition rebinding cannot change the Collection revision or target.", nameof(refreshedPlanBuild));

			var previousByMember = previousBatch.Members.ToDictionary(x => x.Match.Member.MemberKey);
			var reboundArchives = new List<CollectionVerifiedArchive>();
			foreach (ResolvedCollectionMemberPlan member in refreshedPlanBuild.Plan.SelectedMembers)
			{
				CollectionMemberAcquisitionState previous;
				if (!previousByMember.TryGetValue(member.MemberKey, out previous))
					throw new InvalidOperationException("The refreshed plan selected closure differs from the acquisition-ready closure.");
				if (previous.VerifiedArchive == null)
					continue;

				CollectionAcquisitionRequest request = CreateRequest(refreshedPlanBuild.Plan, member.MemberKey);
				reboundArchives.Add(previous.VerifiedArchive.Request.PlanIdentity.Equals(request.PlanIdentity)
					? previous.VerifiedArchive
					: _archiveAdopter.RebindVerified(previous.VerifiedArchive, request, cancellationToken));
			}

			CollectionMemberMatchSet rematched = _matchEngine.Match(refreshedPlanBuild.Plan, refreshedPlanBuild.NativeState, reboundArchives);
			var states = new List<CollectionMemberAcquisitionState>(rematched.Members.Count);
			foreach (CollectionMemberMatchResult match in rematched.Members)
			{
				if (match.Disposition == CollectionMemberMatchDisposition.InstalledCompatible)
					states.Add(State(match, CollectionMemberAcquisitionDisposition.ReadyInstalled, null, null, null, null, null, null));
				else if (match.VerifiedArchive != null)
					states.Add(State(match, CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive,
						match.VerifiedArchive.Request, match.VerifiedArchive, null, null, null, null));
				else if (match.IsBlocked)
					states.Add(State(match, CollectionMemberAcquisitionDisposition.Blocked, null, null, null, null, null, null));
				else
					throw new InvalidOperationException("Post-pause revalidation changed a previously acquisition-ready member into a state that requires new content. Prepare a new acquisition pass.");
			}
			return new CollectionMemberAcquisitionBatch(refreshedPlanBuild, rematched, states);
		}

		/// <summary>
		/// Reconciles persisted C4 acquisition state after restart without automatically starting new network work.
		/// </summary>
		public CollectionMemberAcquisitionBatch ReconcileRestart(CollectionAdditivePlanBuildResult planBuild,
			CancellationToken cancellationToken)
		{
			if (planBuild == null)
				throw new ArgumentNullException(nameof(planBuild));
			if (planBuild.Operation.Phase != CollectionOperationPhase.AwaitingInput)
				throw new InvalidOperationException("Restart acquisition reconciliation requires an AwaitingInput Collection operation.");

			CollectionMemberMatchSet initial = _matchEngine.Match(planBuild.Plan, planBuild.NativeState);
			if (initial.HasBlockedMembers)
				return BuildBlockedBatch(planBuild, initial);

			var verified = new List<CollectionVerifiedArchive>();
			var restartByMember = new Dictionary<CollectionMemberKey, CollectionAcquisitionRestartResult>();
			var requests = new Dictionary<CollectionMemberKey, CollectionAcquisitionRequest>();
			foreach (CollectionMemberMatchResult match in initial.Members)
			{
				if (!RequiresArchiveInput(match))
					continue;
				CollectionAcquisitionRequest request = CreateRequest(planBuild.Plan, match.Member.MemberKey);
				requests.Add(match.Member.MemberKey, request);
				CollectionVerifiedArchive adopted = _archiveAdopter.TryAdopt(request, cancellationToken);
				if (adopted != null)
				{
					verified.Add(adopted);
					continue;
				}
				CollectionAcquisitionRestartResult restart = _restartCoordinator.Reconcile(request, cancellationToken);
				restartByMember.Add(match.Member.MemberKey, restart);
				if (restart.VerifiedArchive != null)
					verified.Add(restart.VerifiedArchive);
			}

			CollectionMemberMatchSet rematched = _matchEngine.Match(planBuild.Plan, planBuild.NativeState, verified);
			var states = new List<CollectionMemberAcquisitionState>(rematched.Members.Count);
			foreach (CollectionMemberMatchResult match in rematched.Members)
			{
				CollectionAcquisitionRequest request;
				requests.TryGetValue(match.Member.MemberKey, out request);
				CollectionAcquisitionRestartResult restart;
				restartByMember.TryGetValue(match.Member.MemberKey, out restart);
				if (match.Disposition == CollectionMemberMatchDisposition.InstalledCompatible)
					states.Add(State(match, CollectionMemberAcquisitionDisposition.ReadyInstalled, null, null, null, null, restart, null));
				else if (match.VerifiedArchive != null)
					states.Add(State(match, CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive,
						request ?? match.VerifiedArchive.Request, match.VerifiedArchive, null, null, restart, restart == null ? (CollectionPremiumAcquisitionAvailability?)null : restart.PremiumAvailability));
				else if (match.IsBlocked)
					states.Add(State(match, CollectionMemberAcquisitionDisposition.Blocked, request, null, null, null, restart, null));
				else
					states.Add(State(match, CollectionMemberAcquisitionDisposition.RestartActionRequired,
						request, null, null, null, restart, restart == null ? (CollectionPremiumAcquisitionAvailability?)null : restart.PremiumAvailability));
			}
			return new CollectionMemberAcquisitionBatch(planBuild, rematched, states);
		}

		private static void ValidatePreparingPlan(CollectionAdditivePlanBuildResult planBuild)
		{
			if (planBuild == null)
				throw new ArgumentNullException(nameof(planBuild));
			if (planBuild.Operation.Phase != CollectionOperationPhase.Preparing || planBuild.Operation.HasCrossedNativeBoundary)
				throw new InvalidOperationException("Member acquisition may begin only at the pre-review Preparing boundary.");
			if (!planBuild.Operation.Target.Equals(planBuild.Plan.Target) ||
				planBuild.Operation.Revision == null || !planBuild.Operation.Revision.Equals(planBuild.Plan.Revision))
				throw new ArgumentException("The additive plan build result is not bound to its durable Collection operation.", nameof(planBuild));
		}

		private static bool RequiresArchiveInput(CollectionMemberMatchResult match)
		{
			return match.Disposition == CollectionMemberMatchDisposition.ArchiveOnlyReuse ||
				match.Disposition == CollectionMemberMatchDisposition.ReinstallRequired ||
				match.Disposition == CollectionMemberMatchDisposition.AcquisitionRequired;
		}

		private static CollectionMemberAcquisitionBatch BuildBlockedBatch(CollectionAdditivePlanBuildResult planBuild,
			CollectionMemberMatchSet matches)
		{
			var states = new List<CollectionMemberAcquisitionState>(matches.Members.Count);
			foreach (CollectionMemberMatchResult match in matches.Members)
			{
				if (match.IsBlocked)
					states.Add(State(match, CollectionMemberAcquisitionDisposition.Blocked, null, match.VerifiedArchive, null, null, null, null));
				else if (match.Disposition == CollectionMemberMatchDisposition.InstalledCompatible)
					states.Add(State(match, CollectionMemberAcquisitionDisposition.ReadyInstalled, null, null, null, null, null, null));
				else if (match.VerifiedArchive != null)
					states.Add(State(match, CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive,
						match.VerifiedArchive.Request, match.VerifiedArchive, null, null, null, null));
				else
					states.Add(State(match, CollectionMemberAcquisitionDisposition.Blocked, null, null, null, null, null, null));
			}
			return new CollectionMemberAcquisitionBatch(planBuild, matches, states);
		}

		private static CollectionAcquisitionRequest CreateRequest(ResolvedCollectionPlan plan, CollectionMemberKey memberKey)
		{
			return CollectionAcquisitionRequest.Create(CreateStableRequestId(plan.Identity, memberKey), plan, memberKey);
		}

		/// <summary>Creates the deterministic C4 acquisition request identity used for one exact plan/member pair.</summary>
		internal static Guid CreateStableRequestId(CollectionPlanIdentity planIdentity, CollectionMemberKey memberKey)
		{
			using (var stream = new MemoryStream())
			using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), true))
			{
				writer.Write(RequestIdentityFormat);
				writer.Write(planIdentity.PlanId.ToByteArray());
				writer.Write(planIdentity.Version);
				writer.Write((int)memberKey.Kind);
				writer.Write(memberKey.Value);
				writer.Flush();
				using (SHA256 sha256 = SHA256.Create())
				{
					byte[] digest = sha256.ComputeHash(stream.ToArray());
					byte[] guidBytes = new byte[16];
					Buffer.BlockCopy(digest, 0, guidBytes, 0, guidBytes.Length);
					Guid result = new Guid(guidBytes);
					if (result == Guid.Empty)
						throw new InvalidOperationException("The deterministic Collection acquisition request identity is invalid.");
					return result;
				}
			}
		}

		private static CollectionMemberAcquisitionState State(CollectionMemberMatchResult match,
			CollectionMemberAcquisitionDisposition disposition, CollectionAcquisitionRequest request,
			CollectionVerifiedArchive archive, CollectionAcquisitionQueueCorrelation correlation,
			CollectionManualAcquisitionPendingAction pendingAction, CollectionAcquisitionRestartResult restartResult,
			CollectionPremiumAcquisitionAvailability? premiumAvailability)
		{
			return new CollectionMemberAcquisitionState(match, disposition, request, archive, correlation,
				pendingAction, restartResult, premiumAvailability);
		}
	}
}
