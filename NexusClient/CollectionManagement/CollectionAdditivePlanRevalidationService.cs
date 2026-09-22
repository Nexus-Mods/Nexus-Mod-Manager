using System;
using System.Threading;
using Nexus.Client.GameStorage;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Describes the result of the mandatory C6.15.5 target/native-state recapture after acquisition or user input.
	/// </summary>
	public sealed class CollectionAdditivePlanRevalidationResult
	{
		internal CollectionAdditivePlanRevalidationResult(CollectionPlanIdentity previousPlanIdentity,
			CollectionMemberAcquisitionBatch acquisitionBatch)
		{
			PreviousPlanIdentity = previousPlanIdentity ?? throw new ArgumentNullException(nameof(previousPlanIdentity));
			AcquisitionBatch = acquisitionBatch ?? throw new ArgumentNullException(nameof(acquisitionBatch));
		}

		public CollectionPlanIdentity PreviousPlanIdentity { get; }
		public CollectionMemberAcquisitionBatch AcquisitionBatch { get; }
		public CollectionAdditivePlanBuildResult PlanBuild { get { return AcquisitionBatch.PlanBuild; } }
		public bool NativeStateChanged { get { return !PreviousPlanIdentity.Equals(PlanBuild.Plan.Identity); } }
	}

	/// <summary>
	/// Revalidates one acquisition-ready additive plan after a user/network pause and refreshes its immutable plan version when needed.
	/// </summary>
	/// <remarks>
	/// Production callers use the Game Storage overload to re-resolve canonical target authority and recapture one fresh C6.1 index.
	/// The captured-state overload exists for callers that already own an equivalent stable read boundary and for deterministic tests.
	/// </remarks>
	public sealed class CollectionAdditivePlanRevalidationService
	{
		private readonly CollectionTargetIdentityResolver _targetIdentityResolver;
		private readonly CollectionNativeStateReader _nativeStateReader;
		private readonly CollectionOperationCoordinator _operationCoordinator;
		private readonly CollectionResolvedPlanBuilder _resolvedPlanBuilder;
		private readonly CollectionMemberAcquisitionCoordinator _acquisitionCoordinator;

		/// <summary>Creates a production post-pause revalidation service.</summary>
		public CollectionAdditivePlanRevalidationService(CollectionTargetIdentityResolver targetIdentityResolver,
			CollectionNativeStateReader nativeStateReader, CollectionOperationCoordinator operationCoordinator,
			CollectionResolvedPlanBuilder resolvedPlanBuilder, CollectionMemberAcquisitionCoordinator acquisitionCoordinator)
		{
			_targetIdentityResolver = targetIdentityResolver ?? throw new ArgumentNullException(nameof(targetIdentityResolver));
			_nativeStateReader = nativeStateReader ?? throw new ArgumentNullException(nameof(nativeStateReader));
			_operationCoordinator = operationCoordinator ?? throw new ArgumentNullException(nameof(operationCoordinator));
			_resolvedPlanBuilder = resolvedPlanBuilder ?? throw new ArgumentNullException(nameof(resolvedPlanBuilder));
			_acquisitionCoordinator = acquisitionCoordinator ?? throw new ArgumentNullException(nameof(acquisitionCoordinator));
		}

		/// <summary>
		/// Creates a revalidation service for a caller that supplies an already resolved target and freshly captured C6.1 state.
		/// </summary>
		public CollectionAdditivePlanRevalidationService(CollectionOperationCoordinator operationCoordinator,
			CollectionResolvedPlanBuilder resolvedPlanBuilder, CollectionMemberAcquisitionCoordinator acquisitionCoordinator)
		{
			_operationCoordinator = operationCoordinator ?? throw new ArgumentNullException(nameof(operationCoordinator));
			_resolvedPlanBuilder = resolvedPlanBuilder ?? throw new ArgumentNullException(nameof(resolvedPlanBuilder));
			_acquisitionCoordinator = acquisitionCoordinator ?? throw new ArgumentNullException(nameof(acquisitionCoordinator));
		}

		/// <summary>
		/// Re-resolves canonical target/storage identity, captures one fresh C6.1 state index and revalidates the paused plan.
		/// </summary>
		public CollectionAdditivePlanRevalidationResult RevalidateAfterPause(CollectionMemberAcquisitionBatch acquisition,
			GameStoragePathSet targetPaths)
		{
			return RevalidateAfterPause(acquisition, targetPaths, CancellationToken.None);
		}

		/// <summary>
		/// Re-resolves and revalidates after a pause with cooperative cancellation for retained-content verification.
		/// </summary>
		public CollectionAdditivePlanRevalidationResult RevalidateAfterPause(CollectionMemberAcquisitionBatch acquisition,
			GameStoragePathSet targetPaths, CancellationToken cancellationToken)
		{
			if (_targetIdentityResolver == null || _nativeStateReader == null)
				throw new InvalidOperationException("This revalidation service was not configured with production target/native-state readers.");
			if (targetPaths == null)
				throw new ArgumentNullException(nameof(targetPaths));

			CollectionTargetAuthority authority = _targetIdentityResolver.Resolve(targetPaths);
			CollectionNativeStateIndex state = _nativeStateReader.Capture(authority.Target);
			return RevalidateAfterPause(acquisition, authority.Target, state, cancellationToken);
		}

		/// <summary>
		/// Revalidates against an already resolved canonical target and freshly captured C6.1 state index.
		/// </summary>
		public CollectionAdditivePlanRevalidationResult RevalidateAfterPause(CollectionMemberAcquisitionBatch acquisition,
			CollectionTargetIdentity resolvedTarget, CollectionNativeStateIndex nativeState)
		{
			return RevalidateAfterPause(acquisition, resolvedTarget, nativeState, CancellationToken.None);
		}

		/// <summary>
		/// Revalidates against an already captured state with cooperative cancellation for retained-content verification.
		/// </summary>
		public CollectionAdditivePlanRevalidationResult RevalidateAfterPause(CollectionMemberAcquisitionBatch acquisition,
			CollectionTargetIdentity resolvedTarget, CollectionNativeStateIndex nativeState, CancellationToken cancellationToken)
		{
			if (acquisition == null)
				throw new ArgumentNullException(nameof(acquisition));
			if (resolvedTarget == null)
				throw new ArgumentNullException(nameof(resolvedTarget));
			if (nativeState == null)
				throw new ArgumentNullException(nameof(nativeState));
			if (!acquisition.IsReady)
				throw new InvalidOperationException("Every required member archive must be verified before post-pause plan revalidation.");
			if (!acquisition.IsAwaitingInput)
				throw new InvalidOperationException("Post-pause revalidation requires an AwaitingInput Collection operation.");

			CollectionAdditivePlanBuildResult previous = acquisition.PlanBuild;
			CollectionOperation revalidating = _operationCoordinator.BeginRevalidation(previous.Operation.Identity);
			if (!previous.Plan.Target.Equals(resolvedTarget) || !resolvedTarget.Equals(nativeState.Target))
			{
				_operationCoordinator.CompleteFailedBeforeApply(revalidating.Identity);
				throw new InvalidOperationException("The canonical Collection target changed while acquisition/user input was pending. Prepare a new operation for the new target authority.");
			}

			CollectionOperation preparing = _operationCoordinator.ResumePreparing(revalidating.Identity);
			CollectionAdditivePlanBuildResult refreshed = _resolvedPlanBuilder.Rebuild(preparing, previous.Plan, nativeState);
			CollectionMemberAcquisitionBatch rebound = _acquisitionCoordinator.RebindReadyBatch(acquisition, refreshed, cancellationToken);
			return new CollectionAdditivePlanRevalidationResult(previous.Plan.Identity, rebound);
		}
	}
}
