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
		Blocked = 6,

		/// <summary>A deterministic embedded Collection archive is being imported through native AddMod.</summary>
		BundledQueued = 7,

		/// <summary>A characterized Vortex direct source is being downloaded through the Collection-owned HTTPS producer.</summary>
		DirectQueued = 8,

		/// <summary>Acquisition waits while another member has a blocking issue, without claiming this member failed.</summary>
		DeferredByBlockedMember = 9
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
			CollectionMemberMatchSet matchSet, IEnumerable<CollectionMemberAcquisitionState> members,
			CollectionArchiveOverwritePolicy archiveOverwritePolicy)
			: this(planBuild, matchSet, members, archiveOverwritePolicy, false)
		{
		}

		/// <summary>Captures the existing-archive verification preference for this acquisition batch.</summary>
		internal CollectionMemberAcquisitionBatch(CollectionAdditivePlanBuildResult planBuild,
			CollectionMemberMatchSet matchSet, IEnumerable<CollectionMemberAcquisitionState> members,
			CollectionArchiveOverwritePolicy archiveOverwritePolicy, bool skipExistingArchiveMd5)
		{
			PlanBuild = planBuild ?? throw new ArgumentNullException(nameof(planBuild));
			MatchSet = matchSet ?? throw new ArgumentNullException(nameof(matchSet));
			ArchiveOverwritePolicy = archiveOverwritePolicy ?? throw new ArgumentNullException(nameof(archiveOverwritePolicy));
			SkipExistingArchiveMd5 = skipExistingArchiveMd5;
			if (members == null)
				throw new ArgumentNullException(nameof(members));
			List<CollectionMemberAcquisitionState> copied = members.ToList();
			if (copied.Count != planBuild.Plan.SelectedMembers.Count || copied.Any(x => x == null))
				throw new ArgumentException("An acquisition batch must contain one non-null state for every selected member.", nameof(members));
			_members = new ReadOnlyCollection<CollectionMemberAcquisitionState>(copied);
		}

		public CollectionAdditivePlanBuildResult PlanBuild { get; }
		public CollectionMemberMatchSet MatchSet { get; }
		/// <summary>Gets the immutable in-session archive-overwrite policy captured when this preparation batch began.</summary>
		public CollectionArchiveOverwritePolicy ArchiveOverwritePolicy { get; }
		/// <summary>Gets the captured opt-in to trust matching Nexus identities of archives present before downloads start.</summary>
		public bool SkipExistingArchiveMd5 { get; }
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
		private const int MaximumAutomaticAcquisitionRetries = 2;
		private readonly Dictionary<Guid, int> _automaticAcquisitionRetries = new Dictionary<Guid, int>();
		private readonly CollectionMemberMatchEngine _matchEngine;
		private readonly CollectionVerifiedArchiveAdopter _archiveAdopter;
		private readonly CollectionPremiumAcquisitionCoordinator _premiumCoordinator;
		private readonly CollectionManualAcquisitionCoordinator _manualCoordinator;
		private readonly CollectionAcquisitionRestartCoordinator _restartCoordinator;
		private readonly CollectionOperationCoordinator _operationCoordinator;
		private readonly ICollectionBundledMemberAcquisitionCoordinator _bundledCoordinator;
		private readonly CollectionDirectAcquisitionCoordinator _directCoordinator;

		/// <summary>Creates the C6.15.5 member-acquisition composition service.</summary>
		public CollectionMemberAcquisitionCoordinator(CollectionMemberMatchEngine matchEngine,
			CollectionVerifiedArchiveAdopter archiveAdopter, CollectionPremiumAcquisitionCoordinator premiumCoordinator,
			CollectionManualAcquisitionCoordinator manualCoordinator, CollectionAcquisitionRestartCoordinator restartCoordinator,
			CollectionOperationCoordinator operationCoordinator)
			: this(matchEngine, archiveAdopter, premiumCoordinator, manualCoordinator, restartCoordinator, operationCoordinator, null)
		{
		}

		/// <summary>Creates the member-acquisition service with optional embedded Collection bundle support.</summary>
		public CollectionMemberAcquisitionCoordinator(CollectionMemberMatchEngine matchEngine,
			CollectionVerifiedArchiveAdopter archiveAdopter, CollectionPremiumAcquisitionCoordinator premiumCoordinator,
			CollectionManualAcquisitionCoordinator manualCoordinator, CollectionAcquisitionRestartCoordinator restartCoordinator,
			CollectionOperationCoordinator operationCoordinator, ICollectionBundledMemberAcquisitionCoordinator bundledCoordinator)
			: this(matchEngine, archiveAdopter, premiumCoordinator, manualCoordinator, restartCoordinator, operationCoordinator,
				bundledCoordinator, null)
		{
		}

		/// <summary>Creates the member-acquisition service with embedded bundle and characterized direct-source support.</summary>
		public CollectionMemberAcquisitionCoordinator(CollectionMemberMatchEngine matchEngine,
			CollectionVerifiedArchiveAdopter archiveAdopter, CollectionPremiumAcquisitionCoordinator premiumCoordinator,
			CollectionManualAcquisitionCoordinator manualCoordinator, CollectionAcquisitionRestartCoordinator restartCoordinator,
			CollectionOperationCoordinator operationCoordinator, ICollectionBundledMemberAcquisitionCoordinator bundledCoordinator,
			CollectionDirectAcquisitionCoordinator directCoordinator)
		{
			_matchEngine = matchEngine ?? throw new ArgumentNullException(nameof(matchEngine));
			_archiveAdopter = archiveAdopter ?? throw new ArgumentNullException(nameof(archiveAdopter));
			_premiumCoordinator = premiumCoordinator ?? throw new ArgumentNullException(nameof(premiumCoordinator));
			_manualCoordinator = manualCoordinator ?? throw new ArgumentNullException(nameof(manualCoordinator));
			_restartCoordinator = restartCoordinator ?? throw new ArgumentNullException(nameof(restartCoordinator));
			_operationCoordinator = operationCoordinator ?? throw new ArgumentNullException(nameof(operationCoordinator));
			_bundledCoordinator = bundledCoordinator;
			_directCoordinator = directCoordinator;
		}

		/// <summary>
		/// Resolves immediately reusable archives, queues available Premium work and exposes free/manual pending actions.
		/// </summary>
		public CollectionMemberAcquisitionBatch Begin(CollectionAdditivePlanBuildResult planBuild,
			ConfirmOverwriteCallback confirmOverwriteCallback, CancellationToken cancellationToken)
		{
			return Begin(planBuild, CollectionArchiveOverwritePolicy.Prompt, confirmOverwriteCallback, cancellationToken);
		}

		/// <summary>Starts member acquisition with one immutable archive-overwrite policy for the complete preparation batch.</summary>
		public CollectionMemberAcquisitionBatch Begin(CollectionAdditivePlanBuildResult planBuild,
			CollectionArchiveOverwritePolicy archiveOverwritePolicy, ConfirmOverwriteCallback confirmOverwriteCallback,
			CancellationToken cancellationToken)
		{
			return BeginCore(planBuild, archiveOverwritePolicy, confirmOverwriteCallback, cancellationToken, null,
				Properties.Settings.Default.CollectionSkipExistingArchiveMd5 || Properties.Settings.Default.CollectionSkipContentChecks);
		}

		/// <summary>Acquires newly selected prerequisites while reusing verified archives and cached content verification.</summary>
		internal CollectionMemberAcquisitionBatch BeginExpanded(CollectionAdditivePlanBuildResult planBuild,
			CollectionMemberAcquisitionBatch previous, CancellationToken cancellationToken)
		{
			if (!previous.IsReady || !previous.PlanBuild.Plan.Revision.Equals(planBuild.Plan.Revision) ||
				!previous.PlanBuild.Plan.Target.Equals(planBuild.Plan.Target))
				throw new InvalidOperationException("Dependency acquisition must extend a ready batch for the same revision and target.");
			return BeginCore(planBuild, previous.ArchiveOverwritePolicy, null, cancellationToken,
				previous.Members.ToDictionary(x => x.Match.Member.MemberKey), previous.SkipExistingArchiveMd5);
		}

		/// <summary>Starts ordinary or expanded acquisition while retaining verified exact inputs.</summary>
		private CollectionMemberAcquisitionBatch BeginCore(CollectionAdditivePlanBuildResult planBuild,
			CollectionArchiveOverwritePolicy archiveOverwritePolicy, ConfirmOverwriteCallback confirmOverwriteCallback,
			CancellationToken cancellationToken, IDictionary<CollectionMemberKey, CollectionMemberAcquisitionState> previousMembers,
			bool skipExistingArchiveMd5)
		{
			if (archiveOverwritePolicy == null) throw new ArgumentNullException(nameof(archiveOverwritePolicy));
			ValidatePreparingPlan(planBuild);
			ResolvedCollectionPlan plan = planBuild.Plan;
			CollectionNativeStateIndex nativeState = planBuild.NativeState;
			CollectionMemberMatchSet initial = _matchEngine.Match(plan, nativeState);

			var requests = new Dictionary<CollectionMemberKey, CollectionAcquisitionRequest>();
			var verifiedArchives = new List<CollectionVerifiedArchive>();
			int archivePosition = 0;
			foreach (CollectionMemberMatchResult match in initial.Members)
			{
				archivePosition++;
				if (!RequiresArchiveInput(match))
					continue;
				CollectionPreparationProgress.Report(Nexus.Client.Util.Localization.LanguageManager.Format(
					"Collections.Workflow.CheckingMemberArchive", "Checking mod archive ({0}/{1}): {2}",
					archivePosition, initial.Members.Count, match.Member.DisplayName), true);
				CollectionAcquisitionRequest request = CreateRequest(plan, match.Member.MemberKey);
				requests.Add(match.Member.MemberKey, request);
				CollectionMemberAcquisitionState previous;
				CollectionVerifiedArchive verified = previousMembers != null && previousMembers.TryGetValue(match.Member.MemberKey, out previous) && previous.VerifiedArchive != null &&
					CollectionExternalArtifactIdentity.MatchesByteLength(request.SelectedArtifact, previous.VerifiedArchive.Artifact.ByteLength)
					? _archiveAdopter.RebindVerified(previous.VerifiedArchive, request, cancellationToken)
					: IsBundled(request) ? _bundledCoordinator.TryComplete(request, cancellationToken)
						: _archiveAdopter.TryAdopt(request, cancellationToken, skipExistingArchiveMd5, plan.Policy.AutomaticallyInstall);
				if (verified != null)
				{
					if (verified.VerificationBasis == CollectionArchiveVerificationBasis.ManagedArchiveMetadataWithCachedHash)
						CollectionPreparationProgress.Report(Nexus.Client.Util.Localization.LanguageManager.Format(
							"Collections.Workflow.ReusingMemberArchiveHash", "Reusing saved archive hash ({0}/{1}): {2}",
							archivePosition, initial.Members.Count, match.Member.DisplayName), true);
					verifiedArchives.Add(verified);
				}
			}

			CollectionMemberMatchSet rematched = _matchEngine.Match(plan, nativeState, verifiedArchives, cancellationToken);
			if (rematched.HasBlockedMembers)
				return BuildBlockedBatch(planBuild, rematched, archiveOverwritePolicy, skipExistingArchiveMd5);

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
			int acquisitionPosition = 0;
			int acquisitionCount = rematched.Members.Count(x =>
				x.Disposition != CollectionMemberMatchDisposition.InstalledCompatible && x.VerifiedArchive == null);
			foreach (CollectionMemberMatchResult match in rematched.Members)
			{
				CollectionAcquisitionRequest request;
				requests.TryGetValue(match.Member.MemberKey, out request);
				if (match.Disposition == CollectionMemberMatchDisposition.InstalledCompatible)
				{
					states.Add(State(match, CollectionMemberAcquisitionDisposition.ReadyInstalled,
						match.VerifiedArchive == null ? null : match.VerifiedArchive.Request, match.VerifiedArchive, null, null, null, null));
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
				acquisitionPosition++;

				if (request == null)
				{
					request = CreateRequest(plan, match.Member.MemberKey);
					requests[match.Member.MemberKey] = request;
				}

				if (IsBundled(request))
				{
					CollectionPreparationProgress.Report(Nexus.Client.Util.Localization.LanguageManager.Format(
						"Collections.Workflow.QueueingMemberImport", "Preparing bundled mod import ({0}/{1}): {2}",
						acquisitionPosition, acquisitionCount, match.Member.DisplayName), true);
					CollectionBundledMemberAcquisitionResult bundled = _bundledCoordinator.Begin(
						request, archiveOverwritePolicy, confirmOverwriteCallback, cancellationToken);
					if (bundled.IsManagedArchiveReady)
					{
						states.Add(State(match, CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive,
							request, bundled.VerifiedArchive, null, null, null, null));
					}
					else
					{
						// The sealed bytes are intentionally not exposed as ready until AddMod has registered those exact bytes natively.
						states.Add(State(match, CollectionMemberAcquisitionDisposition.BundledQueued,
							request, null, bundled.QueueCorrelation, null, null, null));
					}
					continue;
				}

				CollectionPreparationProgress.Report(Nexus.Client.Util.Localization.LanguageManager.Format(
					"Collections.Workflow.QueueingMemberDownload", "Preparing mod download ({0}/{1}): {2}",
					acquisitionPosition, acquisitionCount, match.Member.DisplayName), true);
				CollectionAcquisitionQueueCorrelation directCorrelation = _directCoordinator == null ? null : _directCoordinator.TryQueue(request);
				if (directCorrelation != null)
				{
					states.Add(State(match, CollectionMemberAcquisitionDisposition.DirectQueued,
						request, null, directCorrelation, null, null, null));
					continue;
				}

				CollectionPremiumAcquisitionAvailability availability = _premiumCoordinator.GetAvailability(request);
				if (availability == CollectionPremiumAcquisitionAvailability.Available)
				{
					CollectionAcquisitionQueueCorrelation correlation = _premiumCoordinator.Queue(
						request, confirmOverwriteCallback, archiveOverwritePolicy);
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

			return new CollectionMemberAcquisitionBatch(updatedBuild, rematched, states, archiveOverwritePolicy, skipExistingArchiveMd5);
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
			int archivePosition = 0;
			foreach (CollectionMemberAcquisitionState state in batch.Members)
			{
				archivePosition++;
				if (state.VerifiedArchive != null)
				{
					verified.Add(state.VerifiedArchive);
					continue;
				}
				if (state.Request == null)
					continue;
				CollectionPreparationProgress.Report(Nexus.Client.Util.Localization.LanguageManager.Format(
					"Collections.Workflow.CheckingMemberArchive", "Checking mod archive ({0}/{1}): {2}",
					archivePosition, batch.Members.Count, state.Match.Member.DisplayName), true);
				CollectionVerifiedArchive archive = IsBundled(state.Request)
					? _bundledCoordinator.TryComplete(state.Request, cancellationToken)
					: _archiveAdopter.TryAdopt(state.Request, cancellationToken, false, batch.PlanBuild.Plan.Policy.AutomaticallyInstall);
				if (archive != null)
					verified.Add(archive);
			}

			CollectionMemberMatchSet rematched = _matchEngine.Match(batch.PlanBuild.Plan, batch.PlanBuild.NativeState, verified, cancellationToken);
			var states = new List<CollectionMemberAcquisitionState>(rematched.Members.Count);
			foreach (CollectionMemberMatchResult match in rematched.Members)
			{
				CollectionMemberAcquisitionState previous = batch.Members.First(x => x.Match.Member.MemberKey.Equals(match.Member.MemberKey));
				if (match.Disposition == CollectionMemberMatchDisposition.InstalledCompatible)
					states.Add(State(match, CollectionMemberAcquisitionDisposition.ReadyInstalled,
						match.VerifiedArchive == null ? null : match.VerifiedArchive.Request, match.VerifiedArchive, null, null, null, null));
				else if (match.VerifiedArchive != null)
					states.Add(State(match, CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive,
						previous.Request ?? match.VerifiedArchive.Request, match.VerifiedArchive, null, null, null, previous.PremiumAvailability));
				else if (match.IsBlocked)
					states.Add(State(match, CollectionMemberAcquisitionDisposition.Blocked, previous.Request, null, null, null, null, previous.PremiumAvailability));
				else if ((previous.Disposition == CollectionMemberAcquisitionDisposition.PremiumQueued ||
					previous.Disposition == CollectionMemberAcquisitionDisposition.BundledQueued ||
					previous.Disposition == CollectionMemberAcquisitionDisposition.DirectQueued) && previous.QueueCorrelation != null &&
					CollectionAcquisitionConsumerTask.IsTerminal(previous.QueueCorrelation.Task.Status))
				{
					CollectionAcquisitionQueueCorrelation retry = TryRetryAutomaticPremiumAcquisition(batch, previous, cancellationToken);
					states.Add(State(match, retry == null ? CollectionMemberAcquisitionDisposition.RestartActionRequired : CollectionMemberAcquisitionDisposition.PremiumQueued,
						previous.Request, null, retry ?? previous.QueueCorrelation, previous.PendingAction, previous.RestartResult, previous.PremiumAvailability));
				}
				else
					states.Add(State(match, previous.Disposition, previous.Request, null, previous.QueueCorrelation,
						previous.PendingAction, previous.RestartResult, previous.PremiumAvailability));
			}
			return new CollectionMemberAcquisitionBatch(batch.PlanBuild, rematched, states, batch.ArchiveOverwritePolicy, batch.SkipExistingArchiveMd5);
		}

		/// <summary>Retries only the selected paused member, preserving the plan and every other member's input.</summary>
		public CollectionMemberAcquisitionBatch RetryMember(CollectionMemberAcquisitionBatch batch, CollectionMemberKey memberKey,
			ConfirmOverwriteCallback confirmOverwriteCallback, CancellationToken cancellationToken)
		{
			if (batch == null) throw new ArgumentNullException(nameof(batch));
			if (!batch.IsAwaitingInput) throw new InvalidOperationException("Only an input-paused acquisition can be retried.");
			CollectionMemberAcquisitionState previous = batch.Members.SingleOrDefault(x => x.Match.Member.MemberKey.Equals(memberKey));
			if (previous == null || previous.Request == null || previous.IsReady ||
				(previous.Disposition != CollectionMemberAcquisitionDisposition.RestartActionRequired &&
				 previous.Disposition != CollectionMemberAcquisitionDisposition.ManualInputRequired) ||
				(previous.QueueCorrelation != null && !CollectionAcquisitionConsumerTask.IsTerminal(previous.QueueCorrelation.Task.Status)))
				throw new InvalidOperationException("The selected member has no failed or pending acquisition to retry.");
			cancellationToken.ThrowIfCancellationRequested();
			CollectionMemberAcquisitionState replacement;
			if (IsBundled(previous.Request))
			{
				CollectionBundledMemberAcquisitionResult bundled = _bundledCoordinator.Begin(previous.Request,
					batch.ArchiveOverwritePolicy, confirmOverwriteCallback, cancellationToken);
				replacement = State(previous.Match, bundled.IsManagedArchiveReady ? CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive :
					CollectionMemberAcquisitionDisposition.BundledQueued, previous.Request, bundled.VerifiedArchive, bundled.QueueCorrelation, null, null, null);
			}
			else
			{
				CollectionAcquisitionQueueCorrelation direct = _directCoordinator == null ? null : _directCoordinator.TryQueue(previous.Request);
				CollectionPremiumAcquisitionAvailability availability = _premiumCoordinator.GetAvailability(previous.Request);
				if (direct != null)
					replacement = State(previous.Match, CollectionMemberAcquisitionDisposition.DirectQueued, previous.Request, null, direct, null, null, null);
				else if (availability == CollectionPremiumAcquisitionAvailability.Available)
					replacement = State(previous.Match, CollectionMemberAcquisitionDisposition.PremiumQueued, previous.Request, null,
						_premiumCoordinator.Queue(previous.Request, confirmOverwriteCallback, batch.ArchiveOverwritePolicy), null, null, availability);
				else
					replacement = State(previous.Match, CollectionMemberAcquisitionDisposition.ManualInputRequired, previous.Request, null, null,
						_manualCoordinator.CreatePendingAction(previous.Request), null, availability);
			}
			_automaticAcquisitionRetries.Remove(previous.Request.RequestId);
			return new CollectionMemberAcquisitionBatch(batch.PlanBuild, batch.MatchSet,
				batch.Members.Select(x => ReferenceEquals(x, previous) ? replacement : x), batch.ArchiveOverwritePolicy, batch.SkipExistingArchiveMd5);
		}

		/// <summary>Retries a failed exact Premium download at most twice under the batch's existing Automatic Mode consent.</summary>
		private CollectionAcquisitionQueueCorrelation TryRetryAutomaticPremiumAcquisition(CollectionMemberAcquisitionBatch batch,
			CollectionMemberAcquisitionState previous, CancellationToken cancellationToken)
		{
			if (!batch.PlanBuild.Plan.Policy.AutomaticallyInstall || !batch.ArchiveOverwritePolicy.AutomaticallyOverwritesExistingArchives ||
				previous.Disposition != CollectionMemberAcquisitionDisposition.PremiumQueued || previous.Request == null ||
				previous.QueueCorrelation.Task.Status != Nexus.Client.BackgroundTasks.TaskStatus.Error ||
				_premiumCoordinator.GetAvailability(previous.Request) != CollectionPremiumAcquisitionAvailability.Available)
				return null;
			cancellationToken.ThrowIfCancellationRequested();
			int attempts;
			_automaticAcquisitionRetries.TryGetValue(previous.Request.RequestId, out attempts);
			if (attempts >= MaximumAutomaticAcquisitionRetries) return null;
			_automaticAcquisitionRetries[previous.Request.RequestId] = attempts + 1;
			CollectionPreparationProgress.Report(Nexus.Client.Util.Localization.LanguageManager.Format(
				"Collections.Workflow.RetryingMemberDownload", "Retrying failed download ({0}/{1}): {2}",
				attempts + 1, MaximumAutomaticAcquisitionRetries, previous.Match.Member.DisplayName), true);
			return _premiumCoordinator.Queue(previous.Request, null, batch.ArchiveOverwritePolicy);
		}

		/// <summary>Verifies a user-selected local file for one exact pending manual acquisition in this batch.</summary>
		public CollectionVerifiedArchive VerifyLocalFile(CollectionMemberAcquisitionBatch batch,
			CollectionManualAcquisitionPendingAction pendingAction, string localFilePath, CancellationToken cancellationToken)
		{
			if (batch == null) throw new ArgumentNullException(nameof(batch));
			if (pendingAction == null) throw new ArgumentNullException(nameof(pendingAction));
			CollectionMemberAcquisitionState state = batch.Members.SingleOrDefault(x =>
				x.PendingAction != null && x.PendingAction.ActionId == pendingAction.ActionId);
			if (state == null || state.Disposition != CollectionMemberAcquisitionDisposition.ManualInputRequired ||
				state.Request == null || state.Request.RequestId != pendingAction.Request.RequestId)
				throw new ArgumentException("The pending manual acquisition action does not belong to this input-paused batch.", nameof(pendingAction));
			return _manualCoordinator.VerifyLocalFile(pendingAction, localFilePath, cancellationToken);
		}

		/// <summary>Previews and temporarily retains a different user-selected external archive without installing it.</summary>
		public CollectionAlternateArchiveCandidate InspectAlternateLocalFile(CollectionMemberAcquisitionBatch batch,
			CollectionManualAcquisitionPendingAction pendingAction, string localFilePath, CancellationToken cancellationToken)
		{
			if (batch == null) throw new ArgumentNullException(nameof(batch));
			if (pendingAction == null) throw new ArgumentNullException(nameof(pendingAction));
			CollectionMemberAcquisitionState state = batch.Members.SingleOrDefault(x =>
				x.PendingAction != null && x.PendingAction.ActionId == pendingAction.ActionId);
			if (state == null || state.Disposition != CollectionMemberAcquisitionDisposition.ManualInputRequired ||
				state.Request == null || state.Request.RequestId != pendingAction.Request.RequestId)
				throw new ArgumentException("The alternate file belongs to no current input-paused request.", nameof(pendingAction));
			return _manualCoordinator.InspectAlternateLocalFile(pendingAction, localFilePath, cancellationToken);
		}

		public void ReleaseAlternateLocalFile(CollectionAlternateArchiveCandidate candidate)
		{
			_manualCoordinator.ReleaseAlternateLocalFile(candidate);
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
			if (refreshedPlanBuild.Plan.SelectedMembers.Any(member => previousByMember.ContainsKey(member.MemberKey) &&
				previousByMember[member.MemberKey].VerifiedArchive != null &&
				!CollectionExternalArtifactIdentity.MatchesByteLength(member.ArtifactChoice.SelectedArtifact,
					previousByMember[member.MemberKey].VerifiedArchive.Artifact.ByteLength)))
				return BeginCore(refreshedPlanBuild, previousBatch.ArchiveOverwritePolicy, null, cancellationToken,
					previousByMember, previousBatch.SkipExistingArchiveMd5);
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

			CollectionMemberMatchSet rematched = _matchEngine.Match(refreshedPlanBuild.Plan, refreshedPlanBuild.NativeState, reboundArchives, cancellationToken);
			var states = new List<CollectionMemberAcquisitionState>(rematched.Members.Count);
			foreach (CollectionMemberMatchResult match in rematched.Members)
			{
				if (match.Disposition == CollectionMemberMatchDisposition.InstalledCompatible)
					states.Add(State(match, CollectionMemberAcquisitionDisposition.ReadyInstalled,
						match.VerifiedArchive == null ? null : match.VerifiedArchive.Request, match.VerifiedArchive, null, null, null, null));
				else if (match.VerifiedArchive != null)
					states.Add(State(match, CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive,
						match.VerifiedArchive.Request, match.VerifiedArchive, null, null, null, null));
				else if (match.IsBlocked)
					states.Add(State(match, CollectionMemberAcquisitionDisposition.Blocked, null, null, null, null, null, null));
				else
					throw new InvalidOperationException("Post-pause revalidation changed a previously acquisition-ready member into a state that requires new content. Prepare a new acquisition pass.");
			}
			return new CollectionMemberAcquisitionBatch(refreshedPlanBuild, rematched, states, previousBatch.ArchiveOverwritePolicy, previousBatch.SkipExistingArchiveMd5);
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
				return BuildBlockedBatch(planBuild, initial, CollectionArchiveOverwritePolicy.Prompt);

			var verified = new List<CollectionVerifiedArchive>();
			var restartByMember = new Dictionary<CollectionMemberKey, CollectionAcquisitionRestartResult>();
			var requests = new Dictionary<CollectionMemberKey, CollectionAcquisitionRequest>();
			foreach (CollectionMemberMatchResult match in initial.Members)
			{
				if (!RequiresArchiveInput(match))
					continue;
				CollectionAcquisitionRequest request = CreateRequest(planBuild.Plan, match.Member.MemberKey);
				requests.Add(match.Member.MemberKey, request);
				CollectionVerifiedArchive adopted = IsBundled(request)
					? _bundledCoordinator.TryComplete(request, cancellationToken)
					: _archiveAdopter.TryAdopt(request, cancellationToken, false, planBuild.Plan.Policy.AutomaticallyInstall);
				if (adopted != null)
				{
					verified.Add(adopted);
					continue;
				}
				if (IsBundled(request))
					continue; // Never auto-replay local AddMod work during restart reconciliation.
				CollectionAcquisitionRestartResult restart = _restartCoordinator.Reconcile(request, cancellationToken);
				restartByMember.Add(match.Member.MemberKey, restart);
				if (restart.VerifiedArchive != null)
					verified.Add(restart.VerifiedArchive);
			}

			CollectionMemberMatchSet rematched = _matchEngine.Match(planBuild.Plan, planBuild.NativeState, verified, cancellationToken);
			var states = new List<CollectionMemberAcquisitionState>(rematched.Members.Count);
			foreach (CollectionMemberMatchResult match in rematched.Members)
			{
				CollectionAcquisitionRequest request;
				requests.TryGetValue(match.Member.MemberKey, out request);
				CollectionAcquisitionRestartResult restart;
				restartByMember.TryGetValue(match.Member.MemberKey, out restart);
				if (match.Disposition == CollectionMemberMatchDisposition.InstalledCompatible)
					states.Add(State(match, CollectionMemberAcquisitionDisposition.ReadyInstalled,
						match.VerifiedArchive == null ? null : match.VerifiedArchive.Request, match.VerifiedArchive, null, null, restart, null));
				else if (match.VerifiedArchive != null)
					states.Add(State(match, CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive,
						request ?? match.VerifiedArchive.Request, match.VerifiedArchive, null, null, restart, restart == null ? (CollectionPremiumAcquisitionAvailability?)null : restart.PremiumAvailability));
				else if (match.IsBlocked)
					states.Add(State(match, CollectionMemberAcquisitionDisposition.Blocked, request, null, null, null, restart, null));
				else
					states.Add(State(match, CollectionMemberAcquisitionDisposition.RestartActionRequired,
						request, null, null, null, restart, restart == null ? (CollectionPremiumAcquisitionAvailability?)null : restart.PremiumAvailability));
			}
			return new CollectionMemberAcquisitionBatch(planBuild, rematched, states, CollectionArchiveOverwritePolicy.Prompt);
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

		private bool IsBundled(CollectionAcquisitionRequest request)
		{
			return request != null && _bundledCoordinator != null && _bundledCoordinator.Supports(request.SelectedArtifact);
		}

		private static bool RequiresArchiveInput(CollectionMemberMatchResult match)
		{
			return match.Disposition == CollectionMemberMatchDisposition.ArchiveOnlyReuse ||
				match.Disposition == CollectionMemberMatchDisposition.ReinstallRequired ||
				match.Disposition == CollectionMemberMatchDisposition.AcquisitionRequired;
		}

		private static CollectionMemberAcquisitionBatch BuildBlockedBatch(CollectionAdditivePlanBuildResult planBuild,
			CollectionMemberMatchSet matches, CollectionArchiveOverwritePolicy archiveOverwritePolicy, bool skipExistingArchiveMd5 = false)
		{
			var states = new List<CollectionMemberAcquisitionState>(matches.Members.Count);
			foreach (CollectionMemberMatchResult match in matches.Members)
			{
				if (match.IsBlocked)
					states.Add(State(match, CollectionMemberAcquisitionDisposition.Blocked, null, match.VerifiedArchive, null, null, null, null));
				else if (match.Disposition == CollectionMemberMatchDisposition.InstalledCompatible)
					states.Add(State(match, CollectionMemberAcquisitionDisposition.ReadyInstalled,
						match.VerifiedArchive == null ? null : match.VerifiedArchive.Request, match.VerifiedArchive, null, null, null, null));
				else if (match.VerifiedArchive != null)
					states.Add(State(match, CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive,
						match.VerifiedArchive.Request, match.VerifiedArchive, null, null, null, null));
				else
					states.Add(State(match, CollectionMemberAcquisitionDisposition.DeferredByBlockedMember, null, null, null, null, null, null));
			}
			return new CollectionMemberAcquisitionBatch(planBuild, matches, states, archiveOverwritePolicy, skipExistingArchiveMd5);
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
