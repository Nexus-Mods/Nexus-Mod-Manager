using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Result of C8.8 aggregate replacement verification and atomic association publication.</summary>
	public sealed class CollectionReplacementFinalizationResult
	{
		internal CollectionReplacementFinalizationResult(CollectionOperation operation,
			CollectionTargetAssociation association, IEnumerable<CollectionMemberBinding> bindings,
			CollectionNativeStateIndex verifiedNativeState)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Association = association ?? throw new ArgumentNullException(nameof(association));
			Bindings = new ReadOnlyCollection<CollectionMemberBinding>((bindings ?? throw new ArgumentNullException(nameof(bindings))).ToList());
			VerifiedNativeState = verifiedNativeState ?? throw new ArgumentNullException(nameof(verifiedNativeState));
		}

		public CollectionOperation Operation { get; }
		public CollectionTargetAssociation Association { get; }
		public ReadOnlyCollection<CollectionMemberBinding> Bindings { get; }
		public CollectionNativeStateIndex VerifiedNativeState { get; }
	}

	/// <summary>
	/// C8.8 verifies the complete reviewed replacement result from one fresh authoritative target observation, then atomically
	/// publishes the incoming association, reviewed outgoing-association transitions and the terminal replacement operation.
	/// </summary>
	public sealed class CollectionReplacementAggregateVerificationCoordinator
	{
		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionsNativeChildRecoveryManifestStore _manifestStore;
		private readonly CollectionReplacementOperationCoordinator _replacementCoordinator;
		private readonly CollectionNativeStateReader _nativeStateReader;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;
		private readonly CollectionReplacementRecoveryCoordinator _recoveryCoordinator;

		public CollectionReplacementAggregateVerificationCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore,
			CollectionsAssociationStore associationStore, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore, CollectionsNativeChildRecoveryManifestStore manifestStore)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
			_manifestStore = manifestStore ?? throw new ArgumentNullException(nameof(manifestStore));
			if (planStore == null) throw new ArgumentNullException(nameof(planStore));
			if (artifactStore == null) throw new ArgumentNullException(nameof(artifactStore));
			if (referenceStore == null) throw new ArgumentNullException(nameof(referenceStore));
			_replacementCoordinator = new CollectionReplacementOperationCoordinator(operationStore, planStore, artifactStore, referenceStore);
			_nativeStateReader = new CollectionNativeStateReader(() => _services.ModManager.InstallationLog,
				_services.ModManager.VirtualModActivator, _services.PluginManager, _services.ModManager.GameMode, associationStore);
			_mutationLeaseManager = CollectionTargetMutationLeaseManager.Shared;
			_authorityValidator = new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services);
			_recoveryCoordinator = new CollectionReplacementRecoveryCoordinator(services, gameStorageService, operationStore,
				planStore, associationStore, artifactStore, referenceStore, manifestStore);
		}

		public async Task<CollectionReplacementFinalizationResult> VerifyAndFinalizeAsync(CollectionOperationIdentity operationIdentity,
			ResolvedCollectionPlan reviewedPlan, IEnumerable<PreparedCollectionNativeRecipe> effectivePreparedRecipes,
			GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			if (reviewedPlan == null) throw new ArgumentNullException(nameof(reviewedPlan));
			if (effectivePreparedRecipes == null) throw new ArgumentNullException(nameof(effectivePreparedRecipes));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			if (reviewedPlan.Policy.Kind != CollectionExecutionPolicyKind.ReplaceCurrentManagedSetup)
				throw new ArgumentException("C8.8 requires the exact reviewed replacement plan.", nameof(reviewedPlan));
			cancellationToken.ThrowIfCancellationRequested();

			CollectionOperation operation = RequireReadyOperation(operationIdentity, reviewedPlan);
			if (operation.IsSuccessful)
				return LoadCompleted(operation, reviewedPlan);

			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(reviewedPlan.Target))
				throw new InvalidOperationException("The live canonical target changed before aggregate replacement verification.");

			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(false))
			{
				cancellationToken.ThrowIfCancellationRequested();
				_authorityValidator.ValidateAndReload(lease, authority, paths);
				operation = RequireReadyOperation(operationIdentity, reviewedPlan);
				CollectionReplacementReviewedIntent intent = _replacementCoordinator.GetReviewedIntent(operationIdentity, reviewedPlan.Identity);
				_replacementCoordinator.GetRecoveryBoundary(operationIdentity, reviewedPlan.Identity);
				CollectionNativeStateIndex finalState = _nativeStateReader.Capture(reviewedPlan.Target);
				Dictionary<CollectionMemberKey, PreparedCollectionNativeRecipe> recipes = ValidatePreparedRecipes(intent, effectivePreparedRecipes);
				Dictionary<CollectionMemberKey, CollectionNativeModState> nativeByMember = ResolveIncomingMembers(operation, reviewedPlan, finalState);

				VerifyReviewedNativeDecisions(intent, finalState);
				VerifyIncomingEffects(intent, recipes, nativeByMember, finalState);
				VerifyReviewedWinners(intent, nativeByMember, finalState);
				VerifySubmittedChildren(operation);

				CollectionTargetAssociation incomingAssociation = ResolveIncomingAssociation(operation, reviewedPlan, intent);
				List<CollectionMemberBinding> bindings = BuildBindings(operation, reviewedPlan, incomingAssociation, nativeByMember);
				CollectionOperation committed = CreateCommittedOperation(operation);
				_associationStore.FinalizeReplacementAssociations(intent, incomingAssociation, bindings, committed);
				committed = _operationStore.GetOperation(operation.Identity);
				if (committed == null || !committed.IsSuccessful)
					throw new InvalidOperationException("The replacement association transaction did not publish a committed operation.");

				_recoveryCoordinator.ReleaseTerminalRecoveryReferences(operation.Identity);
				return new CollectionReplacementFinalizationResult(committed, incomingAssociation, bindings, finalState);
			}
		}

		private CollectionOperation RequireReadyOperation(CollectionOperationIdentity identity, ResolvedCollectionPlan plan)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Kind != CollectionOperationKind.ReplaceCurrentManagedSetup || operation.PlanIdentity == null ||
				!operation.PlanIdentity.Equals(plan.Identity) || operation.Revision == null || !operation.Revision.Equals(plan.Revision) ||
				!operation.Target.Equals(plan.Target))
				throw new InvalidOperationException("C8.8 requires the exact replacement operation and reviewed plan.");
			if (!operation.IsSuccessful && operation.Phase != CollectionOperationPhase.IncomingNativeChildrenVerified)
				throw new InvalidOperationException("C8.8 can finalize only after C8.6 verified all incoming native work.");
			return operation;
		}

		private CollectionReplacementFinalizationResult LoadCompleted(CollectionOperation operation, ResolvedCollectionPlan plan)
		{
			CollectionTargetAssociation association = _associationStore.GetAssociationsForTarget(plan.Target)
				.SingleOrDefault(x => x.Revision.Equals(plan.Revision) && x.State == CollectionAssociationState.Applied);
			if (association == null)
				throw new InvalidOperationException("A committed replacement operation is missing its applied incoming association.");
			return new CollectionReplacementFinalizationResult(operation, association,
				_associationStore.GetBindings(association.AssociationId), _nativeStateReader.Capture(plan.Target));
		}

		private static Dictionary<CollectionMemberKey, PreparedCollectionNativeRecipe> ValidatePreparedRecipes(
			CollectionReplacementReviewedIntent intent, IEnumerable<PreparedCollectionNativeRecipe> supplied)
		{
			var result = new Dictionary<CollectionMemberKey, PreparedCollectionNativeRecipe>();
			Dictionary<CollectionMemberKey, CollectionReplacementPreparedRecipeApproval> approved = intent.PreparedRecipes.ToDictionary(x => x.MemberKey);
			foreach (PreparedCollectionNativeRecipe recipe in supplied)
			{
				if (recipe == null) throw new ArgumentException("Prepared replacement recipes cannot contain null entries.", nameof(supplied));
				CollectionReplacementPreparedRecipeApproval approval;
				if (!approved.TryGetValue(recipe.Member.MemberKey, out approval))
					throw new InvalidOperationException("A supplied prepared recipe was not part of the exact replacement review.");
				CollectionReplacementPreparedRecipeApproval observed = CollectionReplacementPreparedRecipeApproval.FromPreparedRecipe(recipe);
				if (!StringComparer.Ordinal.Equals(approval.ProviderRecipeFingerprint, observed.ProviderRecipeFingerprint) ||
					!StringComparer.Ordinal.Equals(approval.PreparedNativeFingerprint, observed.PreparedNativeFingerprint) ||
					!StringComparer.Ordinal.Equals(approval.EffectFingerprint, observed.EffectFingerprint) || approval.Method != observed.Method ||
					approval.Root != observed.Root || approval.AdapterVersion != observed.AdapterVersion ||
					!StringComparer.Ordinal.Equals(approval.AdapterId, observed.AdapterId))
					throw new InvalidOperationException("A prepared replacement recipe changed after explicit review.");
				if (result.ContainsKey(recipe.Member.MemberKey)) throw new InvalidOperationException("Duplicate prepared replacement member.");
				result.Add(recipe.Member.MemberKey, recipe);
			}
			foreach (CollectionReplacementPreparedRecipeApproval approval in approved.Values)
				if (!result.ContainsKey(approval.MemberKey))
					throw new InvalidOperationException("Aggregate replacement verification is missing one reviewed prepared recipe.");
			return result;
		}

		private Dictionary<CollectionMemberKey, CollectionNativeModState> ResolveIncomingMembers(CollectionOperation operation,
			ResolvedCollectionPlan plan, CollectionNativeStateIndex state)
		{
			var result = new Dictionary<CollectionMemberKey, CollectionNativeModState>();
			foreach (ResolvedCollectionMemberPlan member in plan.SelectedMembers)
			{
				List<CollectionNativeChildOperation> children = operation.NativeChildren.Where(x => x.Action == CollectionNativeChildAction.ActivateOrReinstall &&
					x.Member.MemberKey.Equals(member.MemberKey)).ToList();
				CollectionNativeModState native = children.Count == 1
					? ResolveMutatingMember(operation, children[0], state)
					: ResolveRetainedMember(member, state);
				if (native == null) throw new InvalidOperationException("A selected incoming replacement member cannot be resolved to one authoritative native instance.");
				result.Add(member.MemberKey, native);
			}
			return result;
		}

		private CollectionNativeModState ResolveMutatingMember(CollectionOperation operation, CollectionNativeChildOperation child,
			CollectionNativeStateIndex state)
		{
			if (!child.IsReconciled || !child.HasVerifiedCommittedNativeState)
				throw new InvalidOperationException("A mutating incoming member is not reconciled as VerifiedCommitted.");
			CollectionNativeChildRecoveryManifest manifest = _manifestStore.GetManifest(operation, child);
			if (manifest == null || manifest.ExecutionEvidence == null || manifest.SafeBoundaryStateFingerprint == null)
				throw new InvalidDataException("A committed incoming replacement child is missing durable execution/safe-boundary evidence.");
			CollectionNativeChildExecutionEvidence evidence = manifest.ExecutionEvidence;
			IEnumerable<CollectionNativeModState> candidates = state.Mods.Values.Where(x => x.InstallMethod == child.NativeOperation.Fingerprint.InstallMethod &&
				x.InstallRoot == child.NativeOperation.Fingerprint.InstallRoot);
			string domain;
			long modId;
			long fileId;
			if (NexusCollectionModFileArtifactIdentity.TryParse(evidence.SelectedArtifact, out domain, out modId, out fileId))
				candidates = candidates.Where(x => ParseId(x.NexusModId) == modId && ParseId(x.NexusFileId) == fileId);
			else
				candidates = candidates.Where(x => StringComparer.OrdinalIgnoreCase.Equals(x.FileName, evidence.IncomingFileName));
			List<CollectionNativeModState> resolved = candidates.ToList();
			if (resolved.Count != 1) throw new InvalidOperationException("A committed incoming replacement child no longer resolves to one exact native instance.");
			return resolved[0];
		}

		private static CollectionNativeModState ResolveRetainedMember(ResolvedCollectionMemberPlan member, CollectionNativeStateIndex state)
		{
			string domain;
			long modId;
			long fileId;
			if (NexusCollectionModFileArtifactIdentity.TryParse(member.ArtifactChoice.SelectedArtifact, out domain, out modId, out fileId))
			{
				List<CollectionNativeModState> matches = state.Mods.Values.Where(x => ParseId(x.NexusModId) == modId && ParseId(x.NexusFileId) == fileId).ToList();
				return matches.Count == 1 ? matches[0] : null;
			}
			List<CollectionNativeModState> bound = state.BindingsByNativeMod.Where(x => x.Value.Any(b => b.VerifiedRecipe.Equals(member.RecipeIdentity)))
				.Select(x => state.Mods.ContainsKey(x.Key) ? state.Mods[x.Key] : null).Where(x => x != null).Distinct().ToList();
			return bound.Count == 1 ? bound[0] : null;
		}

		private static long ParseId(string value)
		{
			long parsed;
			return Int64.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) ? parsed : -1;
		}

		private static void VerifyReviewedNativeDecisions(CollectionReplacementReviewedIntent intent, CollectionNativeStateIndex state)
		{
			Dictionary<string, CollectionNativeModState> mods = state.Mods.Values.ToDictionary(x => x.Identity.NativeModKey, StringComparer.OrdinalIgnoreCase);
			foreach (CollectionReplacementNativeApproval approval in intent.NativeApprovals)
			{
				bool exists = mods.ContainsKey(approval.NativeModKey);
				if (approval.Decision == CollectionReplacementNativeDecision.Remove && exists)
					throw new InvalidOperationException("A reviewed outgoing native mod still exists at aggregate replacement verification.");
				if ((approval.Decision == CollectionReplacementNativeDecision.Retain || approval.Decision == CollectionReplacementNativeDecision.Protected) && !exists)
					throw new InvalidOperationException("A reviewed retained/protected native mod disappeared before replacement commit.");
			}
		}

		private static void VerifyIncomingEffects(CollectionReplacementReviewedIntent intent,
			IDictionary<CollectionMemberKey, PreparedCollectionNativeRecipe> recipes,
			IDictionary<CollectionMemberKey, CollectionNativeModState> nativeByMember, CollectionNativeStateIndex state)
		{
			foreach (PreparedCollectionNativeRecipe recipe in recipes.Values)
			{
				CollectionNativeModState native = nativeByMember[recipe.Member.MemberKey];
				if (native.InstallMethod != recipe.InstallContext.Method || native.InstallRoot != recipe.InstallContext.InstallRoot)
					throw new InvalidOperationException("A reviewed incoming member no longer has its approved native install context.");
				string ownerKey = native.Identity.NativeModKey;
				foreach (CollectionPlannedFileEffect effect in recipe.EffectPreview.Files)
				{
					CollectionNativeFileState file;
					if (!state.Files.TryGetValue(effect.Target, out file) || String.IsNullOrWhiteSpace(file.PhysicalPath) || !File.Exists(file.PhysicalPath) ||
						!ContainsOwner(file, ownerKey))
						throw new InvalidOperationException("A reviewed incoming file effect is missing from aggregate replacement state.");
				}
				foreach (CollectionPlannedIniEffect effect in recipe.EffectPreview.IniEdits)
				{
					CollectionNativeIniState ini;
					if (!state.IniEdits.TryGetValue(effect.Key, out ini) || ini.Values.Count == 0 ||
						!StringComparer.OrdinalIgnoreCase.Equals(ini.Values[ini.Values.Count - 1].OwnerKey, ownerKey) ||
						!StringComparer.Ordinal.Equals(ini.Values[ini.Values.Count - 1].Value, effect.Value))
						throw new InvalidOperationException("A reviewed incoming INI effect no longer matches aggregate replacement state.");
				}
				foreach (CollectionPlannedGameValueEffect effect in recipe.EffectPreview.GameValues)
				{
					CollectionNativeGameValueState value;
					if (!state.GameValues.TryGetValue(effect.Key, out value) || value.Values.Count == 0 ||
						!StringComparer.OrdinalIgnoreCase.Equals(value.Values[value.Values.Count - 1].OwnerKey, ownerKey) ||
						!BytesEqual(value.Values[value.Values.Count - 1].UnsafeValue, effect.UnsafeValue))
						throw new InvalidOperationException("A reviewed incoming game-specific effect no longer matches aggregate replacement state.");
				}
				foreach (CollectionPlannedPluginEffect effect in recipe.EffectPreview.PluginEffects)
					if (!CollectionNativeChildVerificationCoordinator.VerifyPluginEffect(state, effect))
						throw new InvalidOperationException("A reviewed incoming plugin effect no longer matches aggregate replacement state.");
			}
		}

		private static void VerifyReviewedWinners(CollectionReplacementReviewedIntent intent,
			IDictionary<CollectionMemberKey, CollectionNativeModState> nativeByMember, CollectionNativeStateIndex state)
		{
			foreach (CollectionReplacementFileWinnerApproval approval in intent.FileWinnerApprovals)
			{
				CollectionNativeModState winner;
				CollectionNativeFileState file;
				if (!nativeByMember.TryGetValue(approval.WinnerMemberKey, out winner) || !state.Files.TryGetValue(approval.Target, out file) ||
					!StringComparer.OrdinalIgnoreCase.Equals(file.EffectiveOwnerKey, winner.Identity.NativeModKey))
					throw new InvalidOperationException("A reviewed replacement file winner no longer matches authoritative final state.");
			}
		}

		private static bool ContainsOwner(CollectionNativeFileState file, string ownerKey)
		{
			return file.InstallLogOwners.Concat(file.DeploymentOwners).Concat(file.VirtualOwners)
				.Any(x => x.Kind == CollectionNativeOwnerKind.NativeMod && StringComparer.OrdinalIgnoreCase.Equals(x.OwnerKey, ownerKey));
		}

		private static bool BytesEqual(byte[] left, byte[] right)
		{
			if (ReferenceEquals(left, right)) return true;
			if (left == null || right == null || left.Length != right.Length) return false;
			for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
			return true;
		}

		private static void VerifySubmittedChildren(CollectionOperation operation)
		{
			foreach (CollectionNativeChildOperation child in operation.NativeChildren)
				if (child.HasCrossedNativeBoundary && (!child.IsReconciled || !child.HasVerifiedCommittedNativeState))
					throw new InvalidOperationException("Replacement cannot commit while any submitted native child lacks a verified committed reconciliation.");
		}

		private CollectionTargetAssociation ResolveIncomingAssociation(CollectionOperation operation, ResolvedCollectionPlan plan,
			CollectionReplacementReviewedIntent intent)
		{
			List<CollectionTargetAssociation> exact = _associationStore.GetAssociationsForTarget(plan.Target).Where(x => x.Revision.Equals(plan.Revision)).ToList();
			if (exact.Count > 1) throw new InvalidOperationException("Multiple exact incoming Collection associations exist for the replacement target.");
			if (exact.Count == 1)
			{
				CollectionReplacementAssociationApproval approval = intent.AssociationApprovals.SingleOrDefault(x => x.AssociationId == exact[0].AssociationId);
				if (approval != null && approval.Decision == CollectionReplacementAssociationDecision.TransitionOut)
					throw new InvalidOperationException("The reviewed replacement cannot transition out the same association it is about to publish as incoming.");
				if (_associationStore.GetOverrides(exact[0].AssociationId).Count > 0 || _associationStore.GetDriftObservations(exact[0].AssociationId).Count > 0)
					throw new InvalidOperationException("An existing incoming association still has customization/drift and cannot be silently reset to Applied.");
				return exact[0].WithState(CollectionAssociationState.Applied);
			}
			return new CollectionTargetAssociation(operation.Identity.OperationId, plan.Revision, plan.Target, CollectionAssociationState.Applied);
		}

		private static List<CollectionMemberBinding> BuildBindings(CollectionOperation operation, ResolvedCollectionPlan plan,
			CollectionTargetAssociation association, IDictionary<CollectionMemberKey, CollectionNativeModState> nativeByMember)
		{
			var mutated = new HashSet<CollectionMemberKey>(operation.NativeChildren.Where(x => x.Action == CollectionNativeChildAction.ActivateOrReinstall &&
				x.IsReconciled && x.HasVerifiedCommittedNativeState).Select(x => x.Member.MemberKey));
			return plan.SelectedMembers.Select(member => new CollectionMemberBinding(association, member.MemberKey,
				nativeByMember[member.MemberKey].Identity, member.RecipeIdentity,
				mutated.Contains(member.MemberKey) ? CollectionMemberBindingKind.InstalledForCollection : CollectionMemberBindingKind.AdoptedExisting)).ToList();
		}

		private static CollectionOperation CreateCommittedOperation(CollectionOperation current)
		{
			if (current.CheckpointSequence == Int64.MaxValue) throw new InvalidOperationException("The replacement checkpoint sequence cannot advance further.");
			return new CollectionOperation(current.Identity, current.Kind, current.Collection, current.Target, current.Revision,
				current.PlanIdentity, current.CheckpointSequence + 1, CollectionOperationPhase.Completed,
				CollectionOperationResultState.Committed, current.NativeChildren);
		}
	}
}
