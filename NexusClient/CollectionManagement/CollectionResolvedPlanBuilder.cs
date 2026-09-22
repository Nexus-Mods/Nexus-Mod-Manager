using System;
using System.Collections.Generic;
using Nexus.Client.CollectionManagement.Persistence;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Carries one newly prepared additive resolved plan together with the exact C6.1 native-state snapshot and durable operation.
	/// </summary>
	/// <remarks>
	/// The operation remains in <see cref="CollectionOperationPhase.Preparing"/>. The plan is not yet a reviewed/approved plan
	/// and is not persisted in <c>resolved_plans</c> until the existing C6.5 review boundary is reached.
	/// </remarks>
	public sealed class CollectionAdditivePlanBuildResult
	{
		internal CollectionAdditivePlanBuildResult(CollectionOperation operation, ResolvedCollectionPlan plan,
			CollectionNativeStateIndex nativeState)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Plan = plan ?? throw new ArgumentNullException(nameof(plan));
			NativeState = nativeState ?? throw new ArgumentNullException(nameof(nativeState));
		}

		/// <summary>Gets the durable C6.5 operation left at the pre-review Preparing boundary.</summary>
		public CollectionOperation Operation { get; }

		/// <summary>Gets the immutable additive resolved-plan snapshot produced from the exact selected closure.</summary>
		public ResolvedCollectionPlan Plan { get; }

		/// <summary>Gets the single C6.1 native-state index whose fingerprint is bound into <see cref="Plan"/>.</summary>
		public CollectionNativeStateIndex NativeState { get; }
	}

	/// <summary>
	/// Builds the first production <see cref="ResolvedCollectionPlan"/> from a retained revision, effective selection and one C6.1 state snapshot.
	/// </summary>
	/// <remarks>
	/// This bridge is additive-only. It chooses the exact artifact requested by every selected Gate-A member, creates no downloads,
	/// translates no recipes and performs no native mutation. The exact retained collection.json source is verified before the durable
	/// C6.5 operation is created so missing/corrupt recipe source cannot leave a new preparation journal entry behind.
	/// </remarks>
	public sealed class CollectionResolvedPlanBuilder
	{
		private readonly CollectionsCatalogStore _catalogStore;
		private readonly CollectionsRevisionSourceStore _revisionSourceStore;
		private readonly CollectionOperationCoordinator _operationCoordinator;

		/// <summary>
		/// Creates a production resolved-plan builder over the existing C4 catalog/source stores and C6.5 operation coordinator.
		/// </summary>
		public CollectionResolvedPlanBuilder(CollectionsCatalogStore catalogStore,
			CollectionsRevisionSourceStore revisionSourceStore, CollectionOperationCoordinator operationCoordinator)
		{
			_catalogStore = catalogStore ?? throw new ArgumentNullException(nameof(catalogStore));
			_revisionSourceStore = revisionSourceStore ?? throw new ArgumentNullException(nameof(revisionSourceStore));
			_operationCoordinator = operationCoordinator ?? throw new ArgumentNullException(nameof(operationCoordinator));
		}

		/// <summary>
		/// Creates one additive resolved plan from an already-resolved canonical target and one matching C6.1 state capture.
		/// </summary>
		/// <remarks>
		/// The caller owns target resolution/state capture. This method verifies retained revision source, creates exact artifact
		/// decisions, advances a new C6.5 operation through Created/Resolving/Preparing, and creates the first plan identity/version.
		/// </remarks>
		public CollectionAdditivePlanBuildResult Build(CollectionEffectiveSelection effectiveSelection,
			CollectionTargetIdentity target, CollectionNativeStateIndex nativeState)
		{
			if (effectiveSelection == null)
				throw new ArgumentNullException(nameof(effectiveSelection));
			if (target == null)
				throw new ArgumentNullException(nameof(target));
			if (nativeState == null)
				throw new ArgumentNullException(nameof(nativeState));
			if (!target.IsCanonical)
				throw new ArgumentException("Production additive planning requires a canonical Collection target authority.", nameof(target));
			if (!target.Equals(nativeState.Target))
				throw new ArgumentException("The C6.1 native-state snapshot must belong to the exact resolved Collection target.", nameof(nativeState));
			if (effectiveSelection.CapabilityReport.Status != CollectionCompatibilityStatus.Supported)
				throw new InvalidOperationException("Only a fully supported effective Collection selection can produce an additive resolved plan.");

			NormalizedCollectionManifest manifest = effectiveSelection.Manifest;
			CollectionRevision persistedRevision = _catalogStore.GetRevision(manifest.Revision);
			if (persistedRevision == null)
				throw new InvalidOperationException("The exact Collection revision must be persisted before additive planning.");

			// C6.15.1 is the durable recipe-source authority. Load verifies revision ownership, source provenance and full blob integrity.
			_revisionSourceStore.LoadManifest(manifest.Revision, manifest.Source);

			List<ResolvedCollectionMemberPlan> members = CreateExactMemberPlans(effectiveSelection);

			CollectionOperation operation = _operationCoordinator.CreateApplyOperation(manifest.Revision.Collection, target);
			operation = _operationCoordinator.BeginResolving(operation.Identity);
			operation = _operationCoordinator.BeginPreparing(operation.Identity, manifest.Revision);
			CollectionPlanIdentity planIdentity = _operationCoordinator.CreateNextPlanIdentity(operation.Identity);

			var plan = new ResolvedCollectionPlan(planIdentity, target,
				CollectionExecutionPolicy.InstallIntoCurrentSetup(), nativeState.Fingerprint,
				effectiveSelection.CapabilityReport, members);

			return new CollectionAdditivePlanBuildResult(operation, plan, nativeState);
		}

		/// <summary>
		/// Rebuilds one pre-review additive plan after an acquisition/user pause against a newly captured C6.1 state snapshot.
		/// </summary>
		/// <remarks>
		/// The concrete revision, target, selected closure and exact artifact decisions remain unchanged. When relevant native state
		/// changed, the same logical plan ID receives the next immutable version; an unchanged fingerprint preserves the identity.
		/// </remarks>
		public CollectionAdditivePlanBuildResult Rebuild(CollectionOperation operation, ResolvedCollectionPlan previousPlan,
			CollectionNativeStateIndex nativeState)
		{
			if (operation == null)
				throw new ArgumentNullException(nameof(operation));
			if (previousPlan == null)
				throw new ArgumentNullException(nameof(previousPlan));
			if (nativeState == null)
				throw new ArgumentNullException(nameof(nativeState));
			if (operation.Kind != CollectionOperationKind.ApplyResolvedPlan || operation.Phase != CollectionOperationPhase.Preparing ||
				operation.HasCrossedNativeBoundary)
				throw new InvalidOperationException("An additive plan may be rebuilt only at the pre-review Preparing boundary.");
			if (operation.Revision == null || !operation.Revision.Equals(previousPlan.Revision) ||
				!operation.Collection.Equals(previousPlan.Revision.Collection) || !operation.Target.Equals(previousPlan.Target))
				throw new ArgumentException("The Collection operation must describe the same exact revision and target as the previous plan.", nameof(operation));
			if (!previousPlan.Target.Equals(nativeState.Target))
				throw new ArgumentException("The refreshed C6.1 native-state snapshot must belong to the exact resolved Collection target.", nameof(nativeState));

			_revisionSourceStore.LoadManifest(previousPlan.Revision, previousPlan.ManifestSource);

			CollectionPlanIdentity identity = previousPlan.CurrentStateFingerprint.Equals(nativeState.Fingerprint)
				? previousPlan.Identity
				: previousPlan.Identity.NextVersion();
			var plan = new ResolvedCollectionPlan(identity, previousPlan.Target, previousPlan.Policy, nativeState.Fingerprint,
				previousPlan.CapabilityReport, previousPlan.SelectedMembers);
			return new CollectionAdditivePlanBuildResult(operation, plan, nativeState);
		}

		private static List<ResolvedCollectionMemberPlan> CreateExactMemberPlans(CollectionEffectiveSelection effectiveSelection)
		{
			var members = new List<ResolvedCollectionMemberPlan>();
			foreach (NormalizedCollectionMember member in effectiveSelection.Manifest.Members)
			{
				if (!member.IsSelected)
					continue;

				members.Add(new ResolvedCollectionMemberPlan(member, CollectionResolvedArtifactChoice.Exact(member.Artifact)));
			}
			return members;
		}
	}
}
