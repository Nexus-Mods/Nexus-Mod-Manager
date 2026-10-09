using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.Mods;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Classifies what C6.14 may safely do with one native mod referenced by the association being removed.</summary>
	public enum CollectionUninstallNativeDisposition
	{
		Unknown = 0,
		RemoveNativeMod = 1,
		AlreadyAbsent = 2,
		PreserveSharedCollection = 3,
		PreserveStandalone = 4,
		PreserveUnknownProvenance = 5,
		PreserveCustomized = 6,
		BlockedSharedMissing = 7,
		BlockedNativeState = 8
	}

	/// <summary>One reviewed C6.14 native-instance impact shared by one or more members of the association.</summary>
	public sealed class CollectionUninstallNativeImpact
	{
		private readonly ReadOnlyCollection<CollectionMemberKey> _memberKeys;
		private readonly ReadOnlyCollection<Guid> _survivingAssociationIds;

		internal CollectionUninstallNativeImpact(NativeModInstanceIdentity nativeMod,
			IEnumerable<CollectionMemberKey> memberKeys, CollectionUninstallNativeDisposition disposition,
			StandaloneModUse standaloneUse, IEnumerable<Guid> survivingAssociationIds,
			ModInstallMethod? installMethod, ModInstallRoot? installRoot, string reason, string nativeDisplayName = null)
		{
			NativeMod = nativeMod ?? throw new ArgumentNullException(nameof(nativeMod));
			if (memberKeys == null) throw new ArgumentNullException(nameof(memberKeys));
			if (!Enum.IsDefined(typeof(CollectionUninstallNativeDisposition), disposition) || disposition == CollectionUninstallNativeDisposition.Unknown)
				throw new ArgumentOutOfRangeException(nameof(disposition));
			if (!Enum.IsDefined(typeof(StandaloneModUse), standaloneUse))
				throw new ArgumentOutOfRangeException(nameof(standaloneUse));
			_memberKeys = new ReadOnlyCollection<CollectionMemberKey>(memberKeys.OrderBy(x => (int)x.Kind)
				.ThenBy(x => x.Value, StringComparer.Ordinal).ToList());
			_survivingAssociationIds = new ReadOnlyCollection<Guid>((survivingAssociationIds ?? Enumerable.Empty<Guid>())
				.Distinct().OrderBy(x => x).ToList());
			Disposition = disposition;
			StandaloneUse = standaloneUse;
			InstallMethod = installMethod;
			InstallRoot = installRoot;
			Reason = reason ?? String.Empty;
			NativeDisplayName = nativeDisplayName ?? String.Empty;
		}

		public NativeModInstanceIdentity NativeMod { get; }
		public ReadOnlyCollection<CollectionMemberKey> MemberKeys { get { return _memberKeys; } }
		public CollectionUninstallNativeDisposition Disposition { get; }
		public StandaloneModUse StandaloneUse { get; }
		public ReadOnlyCollection<Guid> SurvivingAssociationIds { get { return _survivingAssociationIds; } }
		public ModInstallMethod? InstallMethod { get; }
		public ModInstallRoot? InstallRoot { get; }
		public string Reason { get; }
		internal string NativeDisplayName { get; }
		public bool RequiresNativeRemoval { get { return Disposition == CollectionUninstallNativeDisposition.RemoveNativeMod; } }
		public bool BlocksExecution
		{
			get
			{
				return Disposition == CollectionUninstallNativeDisposition.BlockedSharedMissing ||
					Disposition == CollectionUninstallNativeDisposition.BlockedNativeState;
			}
		}
	}

	/// <summary>Immutable reviewed C6.14 impact plan for removing one Collection association's dispensable effects.</summary>
	public sealed class CollectionUninstallEffectsPlan
	{
		private readonly ReadOnlyCollection<CollectionUninstallNativeImpact> _impacts;

		internal CollectionUninstallEffectsPlan(CollectionTargetAssociation association,
			CollectionCurrentStateFingerprint stateFingerprint, IEnumerable<CollectionUninstallNativeImpact> impacts,
			IEnumerable<KeyValuePair<CollectionMemberKey, CollectionArtifactReference>> memberArtifacts = null,
			IEnumerable<KeyValuePair<CollectionMemberKey, string>> nativeFileNames = null,
			IEnumerable<CollectionOperation> supersededOperations = null, IEnumerable<NativeModInstanceIdentity> keptMods = null)
		{
			Association = association ?? throw new ArgumentNullException(nameof(association));
			StateFingerprint = stateFingerprint ?? throw new ArgumentNullException(nameof(stateFingerprint));
			ExplicitMemberRemoval = memberArtifacts != null;
			MemberArtifacts = new ReadOnlyCollection<KeyValuePair<CollectionMemberKey, CollectionArtifactReference>>(
				(memberArtifacts ?? Enumerable.Empty<KeyValuePair<CollectionMemberKey, CollectionArtifactReference>>()).ToList());
			NativeFileNames = new ReadOnlyCollection<KeyValuePair<CollectionMemberKey, string>>(
				(nativeFileNames ?? Enumerable.Empty<KeyValuePair<CollectionMemberKey, string>>()).ToList());
			SupersededOperations = new ReadOnlyCollection<CollectionOperation>((supersededOperations ?? Enumerable.Empty<CollectionOperation>()).ToList());
			KeptMods = new ReadOnlyCollection<NativeModInstanceIdentity>((keptMods ?? Enumerable.Empty<NativeModInstanceIdentity>()).Distinct().ToList());
			_impacts = new ReadOnlyCollection<CollectionUninstallNativeImpact>((impacts ?? throw new ArgumentNullException(nameof(impacts)))
				.OrderBy(x => x.NativeMod.NativeModKey, StringComparer.Ordinal).ToList());
		}

		public CollectionTargetAssociation Association { get; }
		public CollectionCurrentStateFingerprint StateFingerprint { get; }
		public ReadOnlyCollection<CollectionUninstallNativeImpact> Impacts { get { return _impacts; } }
		public bool RequiresNativeMutation { get { return _impacts.Any(x => x.RequiresNativeRemoval); } }
		public bool HasBlockedImpacts { get { return _impacts.Any(x => x.BlocksExecution); } }
		internal bool ExplicitMemberRemoval { get; }
		internal ReadOnlyCollection<KeyValuePair<CollectionMemberKey, CollectionArtifactReference>> MemberArtifacts { get; }
		internal ReadOnlyCollection<KeyValuePair<CollectionMemberKey, string>> NativeFileNames { get; }
		internal ReadOnlyCollection<CollectionOperation> SupersededOperations { get; }
		internal ReadOnlyCollection<NativeModInstanceIdentity> KeptMods { get; }

		/// <summary>Retains the user's explicit choices to keep individual listed mods without changing the removal baseline.</summary>
		internal CollectionUninstallEffectsPlan WithKeptMods(IEnumerable<NativeModInstanceIdentity> keptMods)
		{
			if (!ExplicitMemberRemoval) return this;
			var kept = new HashSet<NativeModInstanceIdentity>(keptMods);
			if (kept.Any(x => !_impacts.Any(y => y.NativeMod.Equals(x))))
				throw new ArgumentException("A kept mod must belong to the reviewed Collection removal list.", nameof(keptMods));
			return new CollectionUninstallEffectsPlan(Association, StateFingerprint, _impacts.Select(x => kept.Contains(x.NativeMod) &&
				x.Disposition != CollectionUninstallNativeDisposition.AlreadyAbsent
				? new CollectionUninstallNativeImpact(x.NativeMod, x.MemberKeys, CollectionUninstallNativeDisposition.PreserveStandalone,
					x.StandaloneUse, x.SurvivingAssociationIds, x.InstallMethod, x.InstallRoot, "Kept by your choice in the uninstall review.", x.NativeDisplayName) : x),
				MemberArtifacts, NativeFileNames, SupersededOperations, kept);
		}
	}

	/// <summary>Result of one C6.14 uninstall-effects attempt.</summary>
	public sealed class CollectionUninstallEffectsResult
	{
		private readonly ReadOnlyCollection<CollectionUninstallNativeImpact> _impacts;

		internal CollectionUninstallEffectsResult(CollectionOperation operation, CollectionTargetAssociation association,
			bool associationRemoved, IEnumerable<CollectionUninstallNativeImpact> impacts)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Association = association;
			AssociationRemoved = associationRemoved;
			_impacts = new ReadOnlyCollection<CollectionUninstallNativeImpact>((impacts ?? throw new ArgumentNullException(nameof(impacts))).ToList());
		}

		public CollectionOperation Operation { get; }
		public CollectionTargetAssociation Association { get; }
		public bool AssociationRemoved { get; }
		public ReadOnlyCollection<CollectionUninstallNativeImpact> Impacts { get { return _impacts; } }
		public bool IsSuccessful { get { return Operation.IsSuccessful && AssociationRemoved; } }
		public int RemovedCount { get { return _impacts.Count(WasRemoved); } }
		public IEnumerable<CollectionUninstallNativeImpact> FailedRemovals { get { return _impacts.Where(x => x.RequiresNativeRemoval && !WasRemoved(x)); } }

		/// <summary>Counts only native removals whose actual terminal outcome was verified, including unbound interrupted members.</summary>
		private bool WasRemoved(CollectionUninstallNativeImpact impact)
		{
			return impact.RequiresNativeRemoval && Operation.NativeChildren.Any(x => x.HasVerifiedCommittedNativeState &&
				(impact.NativeMod.Equals(CollectionUninstallEffectsCoordinator.ResolveRemovalJournalMod(Operation.Target, x.Member.MemberKey)) ||
				 impact.MemberKeys.Contains(x.Member.MemberKey)));
		}
	}

	/// <summary>
	/// Implements C6.14 safe uninstall: remove only native instances proven dispensable, preserve shared/standalone/ambiguous
	/// instances, and reconcile Collection tracking around the existing native uninstaller.
	/// </summary>
	/// <remarks>
	/// Native NMM remains authoritative. C6.14 never deletes files or ownership records directly and never calls DeleteMod.
	/// Every destructive child is submitted through the C3 monitor under the C4 target reservation, then authoritative state
	/// is reloaded before the Collection journal/bindings advance. Restart reconciliation never blindly resubmits a child.
	/// </remarks>
	public sealed class CollectionUninstallEffectsCoordinator
	{
		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;

		/// <summary>Creates a production C6.14 coordinator over existing native/Collections services.</summary>
		public CollectionUninstallEffectsCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsAssociationStore associationStore)
			: this(services, gameStorageService, operationStore, associationStore, CollectionTargetMutationLeaseManager.Shared,
				new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services))
		{
		}

		internal CollectionUninstallEffectsCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsAssociationStore associationStore,
			CollectionTargetMutationLeaseManager mutationLeaseManager,
			CollectionTargetOwnershipAuthorityValidator authorityValidator)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
			_mutationLeaseManager = mutationLeaseManager ?? throw new ArgumentNullException(nameof(mutationLeaseManager));
			_authorityValidator = authorityValidator ?? throw new ArgumentNullException(nameof(authorityValidator));
		}

		/// <summary>Builds the reviewed C6.14 removal/preservation impact plan without mutating native or Collection state.</summary>
		public Task<CollectionUninstallEffectsPlan> PreviewAsync(Guid associationId, GameStoragePathSet paths)
		{
			return PreviewAsync(associationId, paths, CancellationToken.None);
		}

		/// <summary>Builds the reviewed C6.14 impact plan while honoring cancellation around target-state inspection.</summary>
		public async Task<CollectionUninstallEffectsPlan> PreviewAsync(Guid associationId, GameStoragePathSet paths,
			CancellationToken cancellationToken)
		{
			RequireLiveServices();
			if (associationId == Guid.Empty) throw new ArgumentException("A non-empty Collection association identifier is required.", nameof(associationId));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			CollectionTargetAssociation association = RequireAssociation(associationId);
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(association.Target))
				throw new InvalidOperationException("The live canonical game/storage target no longer matches the Collection association.");

			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				cancellationToken.ThrowIfCancellationRequested();
				CollectionNativeStateIndex state = await ReloadAndCaptureStateAsync(lease, authority, paths).ConfigureAwait(true);
				association = RequireAssociation(associationId);
				RequireNoIncompleteOperation(association.Target);
				return BuildPlan(association, state);
			}
		}

		/// <summary>Reviews matching Collection mods independently of interrupted installation verification.</summary>
		internal async Task<CollectionUninstallEffectsPlan> PreviewMembersAsync(Guid associationId, GameStoragePathSet paths,
			IEnumerable<KeyValuePair<CollectionMemberKey, CollectionArtifactReference>> memberArtifacts,
			IEnumerable<KeyValuePair<CollectionMemberKey, string>> nativeFileNames, CancellationToken cancellationToken)
		{
			RequireLiveServices();
			CollectionTargetAssociation association = RequireAssociation(associationId);
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(association.Target)) throw new InvalidOperationException("The selected Collection belongs to another game setup.");
			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				cancellationToken.ThrowIfCancellationRequested();
				CollectionNativeStateIndex state = await ReloadAndCaptureStateAsync(lease, authority, paths).ConfigureAwait(true);
				association = RequireAssociation(associationId);
				return BuildExplicitMemberPlan(_associationStore, association, state, memberArtifacts, nativeFileNames,
					GetSupersededOperations(association));
			}
		}

		/// <summary>
		/// Executes an already reviewed C6.14 plan after exact target/state revalidation.
		/// </summary>
		public Task<CollectionUninstallEffectsResult> ExecuteAsync(CollectionUninstallEffectsPlan reviewedPlan, GameStoragePathSet paths)
		{
			return ExecuteAsync(reviewedPlan, paths, CancellationToken.None);
		}

		/// <summary>Executes a reviewed removal on a worker so durable checkpoints do not block the UI between native tasks.</summary>
		public Task<CollectionUninstallEffectsResult> ExecuteAsync(CollectionUninstallEffectsPlan reviewedPlan,
			GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			// The activation monitor marshals its UI notifications. Keep journal I/O, native preparation and
			// the acceptance checkpoint off the caller's UI context while retaining the serial target lease.
			return Task.Run(() => ExecuteCoreAsync(reviewedPlan, paths, cancellationToken));
		}

		/// <summary>Runs serial removal and reconciliation, honoring cancellation only before each native worker start.</summary>
		private async Task<CollectionUninstallEffectsResult> ExecuteCoreAsync(CollectionUninstallEffectsPlan reviewedPlan,
			GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			RequireLiveServices();
			if (reviewedPlan == null) throw new ArgumentNullException(nameof(reviewedPlan));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			if (reviewedPlan.HasBlockedImpacts)
				throw new InvalidOperationException("The reviewed C6.14 plan contains a native-state conflict that blocks safe automatic removal.");

			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(reviewedPlan.Association.Target))
				throw new InvalidOperationException("The live canonical game/storage target no longer matches the reviewed C6.14 plan.");

			using (CollectionTargetMutationLease rootLease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(false))
			{
				cancellationToken.ThrowIfCancellationRequested();
				CollectionNativeStateIndex state = await ReloadAndCaptureStateAsync(rootLease, authority, paths).ConfigureAwait(false);
				CollectionTargetAssociation association = RequireAssociation(reviewedPlan.Association.AssociationId);
				if (!reviewedPlan.ExplicitMemberRemoval) RequireNoIncompleteOperation(association.Target);
				CollectionUninstallEffectsPlan livePlan = reviewedPlan.ExplicitMemberRemoval
					? BuildExplicitMemberPlan(_associationStore, association, state, reviewedPlan.MemberArtifacts,
						reviewedPlan.NativeFileNames, GetSupersededOperations(association)).WithKeptMods(reviewedPlan.KeptMods)
					: BuildPlan(association, state);
				if (!PlansEqual(reviewedPlan, livePlan))
					throw new InvalidOperationException("Collection/native state changed after the C6.14 uninstall preview; review the removal impact again before mutation.");

				CollectionOperation operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(),
					CollectionOperationKind.UninstallCollectionEffects, association.Revision.Collection, association.Target,
					association.Revision, null, 1, CollectionOperationPhase.ApplyingNativeChildren,
					CollectionOperationResultState.Pending, new CollectionNativeChildOperation[0]);
				_associationStore.BeginUninstallEffects(association, operation,
					livePlan.ExplicitMemberRemoval ? livePlan.SupersededOperations : null);

				int nextSequence = 1;
				foreach (CollectionUninstallNativeImpact impact in livePlan.Impacts.Where(x => x.RequiresNativeRemoval))
				{
					if (cancellationToken.IsCancellationRequested)
					{
						operation = CompleteCancellation(operation, association.AssociationId);
						return BuildResult(operation, association.AssociationId, livePlan);
					}
					operation = _operationStore.GetOperation(operation.Identity);
					CollectionNativeChildOperation child = CreatePreparedChild(operation, impact, nextSequence++, livePlan.ExplicitMemberRemoval);
					operation = SaveChild(operation, child);

					IBackgroundTaskSet nativeTask;
					try
					{
						IMod nativeMod = ResolveLiveMod(impact.NativeMod);
						nativeTask = _services.ModManager.CreateCollectionDeactivationOperation(nativeMod,
							_services.ModManager.ActiveMods, child.NativeOperation);
						if (nativeTask == null)
						{
							if (livePlan.ExplicitMemberRemoval) continue;
							operation = CompleteStopped(operation, association.AssociationId, false);
							return BuildResult(operation, association.AssociationId, livePlan);
						}
						ModInstallerBase installerTask = nativeTask as ModInstallerBase;
						if (installerTask == null || installerTask.OperationIdentity == null ||
							!Matches(installerTask.OperationIdentity, child.NativeOperation))
							throw new InvalidOperationException("The native uninstaller did not retain the exact C6.14 operation identity.");
						installerTask.AssignParentMutationLease(rootLease);
					}
					catch (Exception exception) when (livePlan.ExplicitMemberRemoval && !(exception is OperationCanceledException))
					{
						// No worker was submitted. Leave this prepared intent intact and attempt the remaining mods.
						System.Diagnostics.Trace.TraceError("Collection mod could not start uninstall: " + exception);
						continue;
					}
					catch
					{
						CollectionOperation persisted = _operationStore.GetOperation(operation.Identity) ?? operation;
						if (persisted.HasCrossedNativeBoundary)
							MarkRecoveryRequired(persisted, association.AssociationId);
						else
							CompleteStopped(persisted, association.AssociationId, false);
						throw;
					}

					bool accepted;
					try
					{
						accepted = _services.ModActivationMonitor.SubmitWhenIdle(nativeTask, () =>
						{
							cancellationToken.ThrowIfCancellationRequested();
							CollectionOperation current = RequireActiveOperation(operation.Identity, association.AssociationId);
							CollectionNativeChildOperation currentChild = RequireChild(current, child.Sequence,
								CollectionNativeChildCheckpoint.RecoveryInputsReady);
							var submitted = new CollectionNativeChildOperation(currentChild.Sequence, currentChild.Member,
								currentChild.Action, currentChild.NativeOperation, CollectionNativeChildCheckpoint.NativeSubmitted, null);
							current = ReplaceChild(current, submitted);
							_associationStore.SaveUninstallChildSubmission(association.AssociationId, current, impact.NativeMod, impact.MemberKeys,
								livePlan.ExplicitMemberRemoval);
						});
					}
					catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
					{
						operation = CompleteCancellation(operation, association.AssociationId);
						return BuildResult(operation, association.AssociationId, livePlan);
					}
					catch
					{
						CollectionOperation persisted = _operationStore.GetOperation(operation.Identity) ?? operation;
						if (persisted.HasCrossedNativeBoundary)
							MarkRecoveryRequired(persisted, association.AssociationId);
						else
							CompleteStopped(persisted, association.AssociationId, false);
						throw;
					}
					if (!accepted)
					{
						operation = _operationStore.GetOperation(operation.Identity);
						if (livePlan.ExplicitMemberRemoval) continue;
						operation = CompleteStopped(operation, association.AssociationId, false);
						return BuildResult(operation, association.AssociationId, livePlan);
					}

					bool removed;
					try
					{
						await WaitForCompletionAsync(nativeTask).ConfigureAwait(false);
						bool exactResult;
						ModOperationResult reported = CaptureReportedResult(nativeTask, child.NativeOperation, out exactResult);
						state = await ReloadAndCaptureStateAsync(rootLease, authority, paths).ConfigureAwait(false);
						ModOperationDurability durability = DetermineDurability(reported, exactResult,
							IsNativeModFullyAbsent(state, impact.NativeMod), IsNativeModPresent(state, impact.NativeMod));
						var verifiedResult = new ModOperationResult(child.NativeOperation, reported.ReportedStatus, durability, reported.Message);

						operation = _operationStore.GetOperation(operation.Identity);
						CollectionNativeChildOperation submittedChild = RequireChild(operation, child.Sequence,
							CollectionNativeChildCheckpoint.NativeSubmitted);
						var terminal = new CollectionNativeChildOperation(submittedChild.Sequence, submittedChild.Member,
							submittedChild.Action, submittedChild.NativeOperation, CollectionNativeChildCheckpoint.NativeTerminalObserved,
							verifiedResult);
						operation = ReplaceChild(operation, terminal);
						_operationStore.SaveOperation(operation);

						if (durability == ModOperationDurability.Unknown)
						{
							if (livePlan.ExplicitMemberRemoval) continue;
							operation = MarkRecoveryRequired(operation, association.AssociationId);
							return BuildResult(operation, association.AssociationId, livePlan);
						}

						var reconciled = new CollectionNativeChildOperation(terminal.Sequence, terminal.Member, terminal.Action,
							terminal.NativeOperation, CollectionNativeChildCheckpoint.Reconciled, terminal.NativeResult);
						operation = ReplaceChild(operation, reconciled);
						removed = durability == ModOperationDurability.VerifiedCommitted;
						_associationStore.SaveUninstallChildReconciliation(association.AssociationId, operation, impact.NativeMod, removed,
							livePlan.ExplicitMemberRemoval);
					}
					catch
					{
						CollectionOperation persisted = _operationStore.GetOperation(operation.Identity) ?? operation;
						MarkRecoveryRequired(persisted, association.AssociationId);
						throw;
					}
					if (!removed)
					{
						if (livePlan.ExplicitMemberRemoval) continue;
						operation = CompleteStopped(operation, association.AssociationId, true);
						return BuildResult(operation, association.AssociationId, livePlan);
					}
				}

				try
				{
					state = await ReloadAndCaptureStateAsync(rootLease, authority, paths).ConfigureAwait(false);
					operation = _operationStore.GetOperation(operation.Identity);
					if (livePlan.ExplicitMemberRemoval && (operation.HasUnreconciledNativeChild ||
						livePlan.Impacts.Any(x => x.RequiresNativeRemoval && !IsNativeModFullyAbsent(state, x.NativeMod))))
					{
						operation = CompleteStopped(operation, association.AssociationId, operation.HasCrossedNativeBoundary);
						return BuildResult(operation, association.AssociationId, livePlan);
					}
					foreach (CollectionUninstallNativeImpact impact in livePlan.Impacts.Where(x => x.RequiresNativeRemoval))
						if (!IsNativeModFullyAbsent(state, impact.NativeMod))
							throw new InvalidOperationException("A C6.14 native child was reconciled as removed but authoritative native state no longer proves that removal.");

					operation = _operationStore.GetOperation(operation.Identity);
					operation = WithOperationState(operation, CollectionOperationPhase.Completed, CollectionOperationResultState.Committed);
					IEnumerable<NativeModInstanceIdentity> standalone = livePlan.Impacts
						.Where(RequiresStandalonePreservation).Select(x => x.NativeMod);
					IEnumerable<NativeModInstanceIdentity> absent = livePlan.Impacts
						.Where(x => x.Disposition == CollectionUninstallNativeDisposition.AlreadyAbsent || x.RequiresNativeRemoval)
						.Select(x => x.NativeMod);
					_associationStore.CompleteUninstallEffects(association.AssociationId, operation, standalone, absent);
					return new CollectionUninstallEffectsResult(operation, association, true, livePlan.Impacts);
				}
				catch
				{
					CollectionOperation persisted = _operationStore.GetOperation(operation.Identity) ?? operation;
					if (persisted.Phase != CollectionOperationPhase.Completed)
						MarkRecoveryRequired(persisted, association.AssociationId);
					throw;
				}
			}
		}

		/// <summary>
		/// Reconciles an interrupted C6.14 operation from native reality without automatically replaying an uninstaller.
		/// </summary>
		public Task<CollectionUninstallEffectsResult> ReconcileInterruptedAsync(CollectionOperationIdentity operationIdentity,
			GameStoragePathSet paths)
		{
			return ReconcileInterruptedAsync(operationIdentity, paths, CancellationToken.None);
		}

		/// <summary>Reconciles an interrupted C6.14 operation while honoring cancellation before native-state inspection.</summary>
		public async Task<CollectionUninstallEffectsResult> ReconcileInterruptedAsync(CollectionOperationIdentity operationIdentity,
			GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			RequireLiveServices();
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			CollectionOperation operation = RequireRecoverableOperation(operationIdentity);
			CollectionTargetAssociation association = RequireAssociation(operation.Revision, operation.Target);
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(operation.Target))
				throw new InvalidOperationException("The live canonical game/storage target no longer matches the interrupted C6.14 operation.");

			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				cancellationToken.ThrowIfCancellationRequested();
				CollectionNativeStateIndex state = await ReloadAndCaptureStateAsync(lease, authority, paths).ConfigureAwait(true);
				operation = RequireRecoverableOperation(operationIdentity);
				association = RequireAssociation(operation.Revision, operation.Target);
				List<CollectionNativeChildOperation> pending = operation.NativeChildren
					.Where(x => x.HasCrossedNativeBoundary && !x.IsReconciled).ToList();
				if (pending.Count > 1)
					throw new InvalidOperationException("C6.14 serializes native removals and cannot reconcile multiple simultaneously submitted children.");

				if (pending.Count == 1)
				{
					CollectionNativeChildOperation child = pending[0];
					CollectionMemberBinding binding = _associationStore.GetBindings(association.AssociationId)
						.FirstOrDefault(x => x.MemberKey.Equals(child.Member.MemberKey));
					NativeModInstanceIdentity recoveryMod = ResolveRemovalJournalMod(operation.Target, child.Member.MemberKey);
					if (recoveryMod == null) recoveryMod = binding == null ? null : binding.NativeMod;
					if (recoveryMod == null)
						throw new InvalidOperationException("The interrupted uninstall lost its native mod identity.");

					bool absent = IsNativeModFullyAbsent(state, recoveryMod);
					ModOperationResult prior = child.NativeResult;
					ModOperationDurability durability;
					if (absent)
						durability = ModOperationDurability.VerifiedCommitted;
					else if (prior != null && (prior.Durability == ModOperationDurability.NotStarted ||
						prior.Durability == ModOperationDurability.VerifiedRolledBack))
						durability = prior.Durability;
					else
						durability = ModOperationDurability.Unknown;
					ModOperationReportedStatus status = prior == null ? ModOperationReportedStatus.Failed : prior.ReportedStatus;
					string message = prior == null
						? "The process restarted after C6.14 native submission; durability was reconstructed from authoritative native state."
						: prior.Message;
					var result = new ModOperationResult(child.NativeOperation, status, durability, message);
					var terminal = new CollectionNativeChildOperation(child.Sequence, child.Member, child.Action,
						child.NativeOperation, CollectionNativeChildCheckpoint.NativeTerminalObserved, result);
					operation = ReplaceChild(operation, terminal);
					_operationStore.SaveOperation(operation);

					if (durability == ModOperationDurability.Unknown)
					{
						operation = WithOperationState(operation, CollectionOperationPhase.RecoveryRequired,
							CollectionOperationResultState.RecoveryRequired);
						_associationStore.SaveUninstallRecoveryRequired(association.AssociationId, operation);
						return new CollectionUninstallEffectsResult(operation,
							_associationStore.GetAssociation(association.AssociationId), false, new CollectionUninstallNativeImpact[0]);
					}

					var reconciled = new CollectionNativeChildOperation(terminal.Sequence, terminal.Member, terminal.Action,
						terminal.NativeOperation, CollectionNativeChildCheckpoint.Reconciled, terminal.NativeResult);
					operation = ReplaceChild(operation, reconciled);
					_associationStore.SaveUninstallChildReconciliation(association.AssociationId, operation, recoveryMod,
						durability == ModOperationDurability.VerifiedCommitted);
				}

				operation = _operationStore.GetOperation(operation.Identity);
				operation = CompleteStopped(operation, association.AssociationId, operation.HasCrossedNativeBoundary);
				return new CollectionUninstallEffectsResult(operation,
					_associationStore.GetAssociation(association.AssociationId), false, new CollectionUninstallNativeImpact[0]);
			}
		}

		/// <summary>Recovers the native instance identity for a removal that did not have a completed installation binding.</summary>
		internal static NativeModInstanceIdentity ResolveRemovalJournalMod(CollectionTargetIdentity target, CollectionMemberKey member)
		{
			const string prefix = "uninstall-native:";
			return member.Kind == CollectionMemberKeyKind.ProviderStable && member.Value.StartsWith(prefix, StringComparison.Ordinal) &&
				member.Value.Length > prefix.Length ? new NativeModInstanceIdentity(target, member.Value.Substring(prefix.Length)) : null;
		}

		/// <summary>Finds unfinished intents for this Collection that an approved uninstall will replace without inventing native outcomes.</summary>
		private IReadOnlyList<CollectionOperation> GetSupersededOperations(CollectionTargetAssociation association)
		{
			return _operationStore.GetIncompleteOperations(association.Target)
				.Where(x => x.Collection.Equals(association.Revision.Collection)).OrderBy(x => x.Identity.ToString(), StringComparer.Ordinal).ToList();
		}

		/// <summary>Matches the Collection member list to installed native instances without requiring a successful installation journal.</summary>
		internal static CollectionUninstallEffectsPlan BuildExplicitMemberPlan(CollectionsAssociationStore store,
			CollectionTargetAssociation association, CollectionNativeStateIndex state,
			IEnumerable<KeyValuePair<CollectionMemberKey, CollectionArtifactReference>> memberArtifacts,
			IEnumerable<KeyValuePair<CollectionMemberKey, string>> nativeFileNames, IEnumerable<CollectionOperation> supersededOperations)
		{
			var artifacts = (memberArtifacts ?? Enumerable.Empty<KeyValuePair<CollectionMemberKey, CollectionArtifactReference>>()).ToList();
			var names = (nativeFileNames ?? Enumerable.Empty<KeyValuePair<CollectionMemberKey, string>>()).ToList();
			IReadOnlyList<CollectionMemberBinding> bindings = store.GetBindings(association.AssociationId);
			var membersByMod = new Dictionary<NativeModInstanceIdentity, HashSet<CollectionMemberKey>>();
			foreach (CollectionMemberBinding binding in bindings)
			{
				HashSet<CollectionMemberKey> members;
				if (!membersByMod.TryGetValue(binding.NativeMod, out members)) membersByMod[binding.NativeMod] = members = new HashSet<CollectionMemberKey>();
				members.Add(binding.MemberKey);
			}
			foreach (CollectionNativeModState mod in state.Mods.Values)
			{
				var members = new HashSet<CollectionMemberKey>();
				foreach (KeyValuePair<CollectionMemberKey, CollectionArtifactReference> source in artifacts)
				{
					string domain; long modId; long fileId;
					if (NexusCollectionModFileArtifactIdentity.TryParse(source.Value, out domain, out modId, out fileId) &&
						StringComparer.Ordinal.Equals(mod.NexusModId, modId.ToString(System.Globalization.CultureInfo.InvariantCulture)) &&
						StringComparer.Ordinal.Equals(mod.NexusFileId, fileId.ToString(System.Globalization.CultureInfo.InvariantCulture)))
						members.Add(source.Key);
				}
				foreach (KeyValuePair<CollectionMemberKey, string> source in names)
					if (StringComparer.OrdinalIgnoreCase.Equals(mod.FileName, source.Value)) members.Add(source.Key);
				if (members.Count == 0) continue;
				HashSet<CollectionMemberKey> boundMembers;
				if (!membersByMod.TryGetValue(mod.Identity, out boundMembers)) membersByMod[mod.Identity] = boundMembers = new HashSet<CollectionMemberKey>();
				boundMembers.UnionWith(members);
			}
			var impacts = new List<CollectionUninstallNativeImpact>();
			foreach (KeyValuePair<NativeModInstanceIdentity, HashSet<CollectionMemberKey>> pair in membersByMod)
			{
				CollectionNativeModState mod;
				state.Mods.TryGetValue(pair.Key, out mod);
				NativeModProvenance provenance = store.GetNativeModProvenance(pair.Key);
				Guid[] shared = store.GetBindingsForNativeMod(pair.Key).Where(x => x.Association.AssociationId != association.AssociationId)
					.Select(x => x.Association.AssociationId).Distinct().ToArray();
				string reason = shared.Length > 0 ? "Another Collection also uses this mod. Removing it will leave that Collection incomplete."
					: provenance.StandaloneUse == StandaloneModUse.ExplicitStandaloneUse ? "You also use this mod separately. Uncheck it to keep it installed."
					: "Matches this Collection's mod list. Uncheck it to keep it installed.";
				impacts.Add(new CollectionUninstallNativeImpact(pair.Key, pair.Value,
					mod == null ? CollectionUninstallNativeDisposition.AlreadyAbsent : CollectionUninstallNativeDisposition.RemoveNativeMod,
					provenance.StandaloneUse, shared, mod == null ? (ModInstallMethod?)null : mod.InstallMethod,
					mod == null ? (ModInstallRoot?)null : mod.InstallRoot, reason,
					mod == null ? null : (String.IsNullOrWhiteSpace(mod.ModName) ? mod.FileName : mod.ModName) + " " + mod.HumanReadableVersion));
			}
			return new CollectionUninstallEffectsPlan(association, state.Fingerprint, impacts, artifacts, names, supersededOperations);
		}

		private CollectionUninstallEffectsPlan BuildPlan(CollectionTargetAssociation association, CollectionNativeStateIndex state)
		{
			return BuildPlanForState(_associationStore, association, state);
		}

		/// <summary>Builds one deterministic C6.14 plan from an exact feature-store/native-state snapshot.</summary>
		internal static CollectionUninstallEffectsPlan BuildPlanForState(CollectionsAssociationStore associationStore,
			CollectionTargetAssociation association, CollectionNativeStateIndex state)
		{
			if (associationStore == null) throw new ArgumentNullException(nameof(associationStore));
			if (association == null) throw new ArgumentNullException(nameof(association));
			if (state == null) throw new ArgumentNullException(nameof(state));
			if (association.State == CollectionAssociationState.Recovering)
				throw new InvalidOperationException("A recovering Collection association cannot be uninstalled until recovery/reconciliation is complete.");
			IReadOnlyList<CollectionMemberBinding> bindings = associationStore.GetBindings(association.AssociationId);
			IReadOnlyList<UserOverride> overrides = associationStore.GetOverrides(association.AssociationId);
			IReadOnlyList<CollectionDriftObservation> drift = associationStore.GetDriftObservations(association.AssociationId);
			bool modifiedWithoutDetail = association.State == CollectionAssociationState.Modified && overrides.Count == 0 && drift.Count == 0;
			var impacts = new List<CollectionUninstallNativeImpact>();

			foreach (IGrouping<NativeModInstanceIdentity, CollectionMemberBinding> group in bindings.GroupBy(x => x.NativeMod)
				.OrderBy(x => x.Key.NativeModKey, StringComparer.Ordinal))
			{
				NativeModInstanceIdentity nativeMod = group.Key;
				List<CollectionMemberBinding> allBindings = associationStore.GetBindingsForNativeMod(nativeMod).ToList();
				Guid[] survivingAssociations = allBindings.Where(x => x.Association.AssociationId != association.AssociationId)
					.Select(x => x.Association.AssociationId).Distinct().OrderBy(x => x).ToArray();
				NativeModProvenance provenance = associationStore.GetNativeModProvenance(nativeMod);
				bool present = IsNativeModPresent(state, nativeMod);
				bool hasReferences = HasNativeOwnerReferences(state, nativeMod);
				bool customized = modifiedWithoutDetail || RequirementSetTouchesMembers(overrides.Select(x => x.Requirement), group.Select(x => x.MemberKey)) ||
					RequirementSetTouchesMembers(drift.Select(x => x.Requirement), group.Select(x => x.MemberKey));
				CollectionNativeModState nativeState;
				state.Mods.TryGetValue(nativeMod, out nativeState);

				CollectionUninstallNativeDisposition disposition;
				string reason;
				string coverageFailure = GetAuthoritativeRemovalCoverageFailure(state, nativeMod);
				if (coverageFailure != null)
				{
					disposition = CollectionUninstallNativeDisposition.BlockedNativeState;
					reason = coverageFailure;
				}
				else if (!present && hasReferences)
				{
					disposition = CollectionUninstallNativeDisposition.BlockedNativeState;
					reason = "Native ownership/effect records still reference the bound mod key, but no active native mod registration can drive a safe uninstall.";
				}
				else if (!present && survivingAssociations.Length > 0)
				{
					disposition = CollectionUninstallNativeDisposition.BlockedSharedMissing;
					reason = "Another Collection still binds this native mod, but authoritative native state no longer contains the instance it requires.";
				}
				else if (!present)
				{
					disposition = CollectionUninstallNativeDisposition.AlreadyAbsent;
					reason = "The native mod and its owned effects are already absent from authoritative native state.";
				}
				else if (customized)
				{
					disposition = CollectionUninstallNativeDisposition.PreserveCustomized;
					reason = survivingAssociations.Length > 0
						? "Deliberate override/detected drift requires standalone preservation even though another Collection also references the native mod."
						: "Deliberate override/detected drift makes automatic removal unsafe; preserve the native mod as standalone instead.";
				}
				else if (survivingAssociations.Length > 0)
				{
					disposition = CollectionUninstallNativeDisposition.PreserveSharedCollection;
					reason = "Another applied/tracked Collection association still requires the same native mod instance.";
				}
				else if (provenance.StandaloneUse == StandaloneModUse.ExplicitStandaloneUse)
				{
					disposition = CollectionUninstallNativeDisposition.PreserveStandalone;
					reason = "Explicit standalone provenance requires the native mod independently from Collection tracking.";
				}
				else if (provenance.StandaloneUse == StandaloneModUse.Unknown)
				{
					disposition = CollectionUninstallNativeDisposition.PreserveUnknownProvenance;
					reason = "Standalone provenance is unknown; C6.14 conservatively preserves the native mod rather than guessing sole Collection ownership.";
				}
				else
				{
					disposition = CollectionUninstallNativeDisposition.RemoveNativeMod;
					reason = "No surviving Collection binding or standalone provenance protects this native mod, so native uninstallation is permitted.";
				}

				impacts.Add(new CollectionUninstallNativeImpact(nativeMod, group.Select(x => x.MemberKey), disposition,
					provenance.StandaloneUse, survivingAssociations,
					nativeState == null ? (ModInstallMethod?)null : nativeState.InstallMethod,
					nativeState == null ? (ModInstallRoot?)null : nativeState.InstallRoot, reason));
			}
			return new CollectionUninstallEffectsPlan(association, state.Fingerprint, impacts);
		}

		private static string GetAuthoritativeRemovalCoverageFailure(CollectionNativeStateIndex state,
			NativeModInstanceIdentity nativeMod)
		{
			if (state.AssociationCoverage != CollectionNativeStateCoverage.Complete)
				return "Collection association coverage is unavailable, so C6.14 cannot prove this member is dispensable.";
			if (state.PluginCoverage == CollectionNativeStateCoverage.Unavailable)
				return "Native plugin-state coverage is unavailable, so C6.14 cannot prove complete removal safely.";

			ReadOnlyCollection<CollectionNativeFileState> ownedFiles;
			ReadOnlyCollection<CollectionNativeIniState> ownedIni;
			state.FilesByOwnerKey.TryGetValue(nativeMod.NativeModKey, out ownedFiles);
			state.IniEditsByOwnerKey.TryGetValue(nativeMod.NativeModKey, out ownedIni);
			IEnumerable<CollectionNativeFileState> relevantFiles = ownedFiles ?? Enumerable.Empty<CollectionNativeFileState>();
			IEnumerable<CollectionNativeIniState> relevantIni = ownedIni ?? Enumerable.Empty<CollectionNativeIniState>();
			var fileResources = new HashSet<string>(relevantFiles.Select(x => x.Target.ToString()), StringComparer.OrdinalIgnoreCase);
			var fileRoots = new HashSet<string>(relevantFiles.Select(x => x.Target.Root.ToString()), StringComparer.OrdinalIgnoreCase);
			var iniResources = new HashSet<string>(relevantIni.Select(x => x.Key.ToString()), StringComparer.OrdinalIgnoreCase);

			foreach (CollectionNativeStateIssue issue in state.Issues)
			{
				bool relevant;
				switch (issue.Kind)
				{
					case CollectionNativeStateIssueKind.AssociationStateUnavailable:
					case CollectionNativeStateIssueKind.PluginStateUnavailable:
						relevant = true;
						break;
					case CollectionNativeStateIssueKind.UnresolvedOwner:
						relevant = fileResources.Contains(issue.ResourceKey);
						break;
					case CollectionNativeStateIssueKind.PhysicalRootUnavailable:
						relevant = fileResources.Contains(issue.ResourceKey) || fileRoots.Contains(issue.ResourceKey);
						break;
					case CollectionNativeStateIssueKind.AmbiguousIniIdentity:
						relevant = iniResources.Contains(issue.ResourceKey);
						break;
					default:
						relevant = true;
						break;
				}

				if (relevant)
					return String.Format("Native-state coverage for this mod is incomplete at '{0}': {1}",
						issue.ResourceKey, issue.Message);
			}

			return null;
		}

		private static bool RequirementSetTouchesMembers(IEnumerable<CollectionRequirementReference> requirements,
			IEnumerable<CollectionMemberKey> memberKeys)
		{
			var keys = new HashSet<CollectionMemberKey>(memberKeys);
			foreach (CollectionRequirementReference requirement in requirements)
				if (requirement.MemberKey == null || keys.Contains(requirement.MemberKey))
					return true;
			return false;
		}

		private static bool PlansEqual(CollectionUninstallEffectsPlan left, CollectionUninstallEffectsPlan right)
		{
			if (left == null || right == null || left.Association.AssociationId != right.Association.AssociationId ||
				!left.Association.Revision.Equals(right.Association.Revision) || !left.Association.Target.Equals(right.Association.Target) ||
				left.Association.State != right.Association.State || !left.StateFingerprint.Equals(right.StateFingerprint) ||
				left.Impacts.Count != right.Impacts.Count || left.ExplicitMemberRemoval != right.ExplicitMemberRemoval ||
				left.SupersededOperations.Count != right.SupersededOperations.Count ||
				left.SupersededOperations.Any(x => !right.SupersededOperations.Any(y => y.Identity.Equals(x.Identity) &&
					y.CheckpointSequence == x.CheckpointSequence)))
				return false;
			for (int i = 0; i < left.Impacts.Count; i++)
			{
				CollectionUninstallNativeImpact a = left.Impacts[i];
				CollectionUninstallNativeImpact b = right.Impacts[i];
				if (!a.NativeMod.Equals(b.NativeMod) || a.Disposition != b.Disposition || a.StandaloneUse != b.StandaloneUse ||
					a.InstallMethod != b.InstallMethod || a.InstallRoot != b.InstallRoot ||
					!a.MemberKeys.SequenceEqual(b.MemberKeys) || !a.SurvivingAssociationIds.SequenceEqual(b.SurvivingAssociationIds))
					return false;
			}
			return true;
		}

		private static bool RequiresStandalonePreservation(CollectionUninstallNativeImpact impact)
		{
			return impact.Disposition == CollectionUninstallNativeDisposition.PreserveStandalone ||
				impact.Disposition == CollectionUninstallNativeDisposition.PreserveUnknownProvenance ||
				impact.Disposition == CollectionUninstallNativeDisposition.PreserveCustomized;
		}

		private CollectionNativeChildOperation CreatePreparedChild(CollectionOperation operation,
			CollectionUninstallNativeImpact impact, int sequence, bool explicitMemberRemoval = false)
		{
			if (!impact.InstallMethod.HasValue || !impact.InstallRoot.HasValue || impact.MemberKeys.Count == 0)
				throw new InvalidOperationException("A removable C6.14 native mod is missing installed context/member correlation.");
			var identity = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint(operation.Target.Fingerprint,
					new ModInstallContext(impact.InstallMethod.Value, impact.InstallRoot.Value), null));
			var member = new CollectionOperationMemberReference(operation.Revision, explicitMemberRemoval
				? CollectionMemberKey.FromProvider("uninstall-native:" + impact.NativeMod.NativeModKey) : impact.MemberKeys[0]);
			return new CollectionNativeChildOperation(sequence, member, CollectionNativeChildAction.Deactivate, identity,
				CollectionNativeChildCheckpoint.RecoveryInputsReady, null);
		}

		private IMod ResolveLiveMod(NativeModInstanceIdentity nativeMod)
		{
			Func<IMod, bool> matchesKey = mod =>
			{
				string key;
				try { key = _services.ModManager.InstallationLog.GetModKey(mod); }
				catch { return false; }
				return StringComparer.Ordinal.Equals(key, nativeMod.NativeModKey);
			};
			// Reload can replace the active object while the archive registry still holds an older instance.
			List<IMod> matches = _services.ModManager.ActiveMods.Where(matchesKey).ToList();
			if (matches.Count == 0) matches = _services.ModManager.ManagedMods.Where(matchesKey).ToList();
			if (matches.Count != 1)
				throw new InvalidOperationException("The reviewed C6.14 native mod cannot be resolved to exactly one live active mod instance.");
			return matches[0];
		}

		/// <summary>Reloads and captures authoritative removal state on a worker while the caller retains the target lease.</summary>
		private Task<CollectionNativeStateIndex> ReloadAndCaptureStateAsync(CollectionTargetMutationLease lease,
			CollectionTargetAuthority authority, GameStoragePathSet paths)
		{
			// Reconciliation must finish even when cancellation arrives after a native child committed.
			// Await each reload before publishing its result or submitting the next uninstaller.
			return Task.Run(() =>
			{
				_authorityValidator.ValidateAndReload(lease, authority, paths);
				return CaptureReloadedState(authority.Target);
			});
		}

		private CollectionNativeStateIndex CaptureReloadedState(CollectionTargetIdentity target)
		{
			ModManager modManager = _services.ModManager;
			return new CollectionNativeStateReader(modManager.InstallationLog, modManager.VirtualModActivator,
				_services.PluginManager, modManager.GameMode, _associationStore).Capture(target);
		}

		/// <summary>Applies the same incomplete-operation gate to removal review and execution before approval or mutation.</summary>
		private void RequireNoIncompleteOperation(CollectionTargetIdentity target)
		{
			if (_operationStore.GetIncompleteOperations(target).Count != 0)
				throw new InvalidOperationException("Collection removal cannot begin while another Collection operation for this game is incomplete. Finish or reconcile that operation first.");
		}

		private CollectionTargetAssociation RequireAssociation(Guid associationId)
		{
			CollectionTargetAssociation association = _associationStore.GetAssociation(associationId);
			if (association == null)
				throw new InvalidOperationException("The Collection association no longer exists.");
			return association;
		}

		private CollectionTargetAssociation RequireAssociation(CollectionRevisionIdentity revision, CollectionTargetIdentity target)
		{
			List<CollectionTargetAssociation> matches = _associationStore.GetAssociationsForTarget(target)
				.Where(x => x.Revision.Equals(revision)).ToList();
			if (matches.Count != 1)
				throw new InvalidOperationException("The interrupted C6.14 operation no longer maps to exactly one Collection association.");
			return matches[0];
		}

		private CollectionOperation RequireActiveOperation(CollectionOperationIdentity identity, Guid associationId)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Kind != CollectionOperationKind.UninstallCollectionEffects ||
				operation.Phase != CollectionOperationPhase.ApplyingNativeChildren ||
				operation.ResultState != CollectionOperationResultState.Pending)
				throw new InvalidOperationException("The C6.14 uninstall operation is no longer active at the native-submission boundary.");
			CollectionTargetAssociation association = _associationStore.GetAssociation(associationId);
			if (association == null || !association.Revision.Equals(operation.Revision) || !association.Target.Equals(operation.Target))
				throw new InvalidOperationException("The C6.14 uninstall association changed before native submission.");
			return operation;
		}

		private CollectionOperation RequireRecoverableOperation(CollectionOperationIdentity identity)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Kind != CollectionOperationKind.UninstallCollectionEffects || operation.Revision == null)
				throw new InvalidOperationException("The requested operation is not a persisted C6.14 uninstall-effects operation.");
			if (operation.IsTerminal)
				throw new InvalidOperationException("The C6.14 operation is already terminal and does not require restart reconciliation.");
			if (operation.Phase != CollectionOperationPhase.ApplyingNativeChildren &&
				operation.Phase != CollectionOperationPhase.Recovering &&
				operation.Phase != CollectionOperationPhase.RecoveryRequired)
				throw new InvalidOperationException("The C6.14 operation is not at a restart-reconcilable boundary.");
			return operation;
		}

		private CollectionOperation SaveChild(CollectionOperation operation, CollectionNativeChildOperation child)
		{
			List<CollectionNativeChildOperation> children = operation.NativeChildren.ToList();
			children.Add(child);
			var updated = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
				operation.Revision, operation.PlanIdentity, checked(operation.CheckpointSequence + 1), operation.Phase,
				operation.ResultState, children);
			_operationStore.SaveOperation(updated);
			return updated;
		}

		private static CollectionOperation ReplaceChild(CollectionOperation operation, CollectionNativeChildOperation child)
		{
			var children = operation.NativeChildren.Select(x => x.Sequence == child.Sequence ? child : x).ToList();
			return new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
				operation.Revision, operation.PlanIdentity, checked(operation.CheckpointSequence + 1), operation.Phase,
				operation.ResultState, children);
		}

		private static CollectionOperation WithOperationState(CollectionOperation operation, CollectionOperationPhase phase,
			CollectionOperationResultState result)
		{
			return new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
				operation.Revision, operation.PlanIdentity, checked(operation.CheckpointSequence + 1), phase, result,
				operation.NativeChildren);
		}

		private CollectionOperation MarkRecoveryRequired(CollectionOperation operation, Guid associationId)
		{
			operation = _operationStore.GetOperation(operation.Identity) ?? operation;
			if (operation.Phase == CollectionOperationPhase.RecoveryRequired &&
				operation.ResultState == CollectionOperationResultState.RecoveryRequired)
				return operation;
			operation = WithOperationState(operation, CollectionOperationPhase.RecoveryRequired,
				CollectionOperationResultState.RecoveryRequired);
			_associationStore.SaveUninstallRecoveryRequired(associationId, operation);
			return operation;
		}

		private CollectionOperation CompleteCancellation(CollectionOperation operation, Guid associationId)
		{
			operation = _operationStore.GetOperation(operation.Identity) ?? operation;
			if (!operation.HasCrossedNativeBoundary)
			{
				operation = WithOperationState(operation, CollectionOperationPhase.Completed,
					CollectionOperationResultState.CancelledBeforeApply);
				_operationStore.SaveOperation(operation);
				return operation;
			}

			operation = WithOperationState(operation, CollectionOperationPhase.Completed,
				CollectionOperationResultState.StoppedPartial);
			_associationStore.SaveUninstallStoppedPartial(associationId, operation);
			return operation;
		}

		private CollectionOperation CompleteStopped(CollectionOperation operation, Guid associationId, bool crossedBoundary)
		{
			operation = _operationStore.GetOperation(operation.Identity) ?? operation;
			if (!crossedBoundary && !operation.HasCrossedNativeBoundary)
			{
				operation = WithOperationState(operation, CollectionOperationPhase.Completed,
					CollectionOperationResultState.FailedBeforeApply);
				_operationStore.SaveOperation(operation);
				return operation;
			}
			operation = WithOperationState(operation, CollectionOperationPhase.Completed,
				CollectionOperationResultState.StoppedPartial);
			_associationStore.SaveUninstallStoppedPartial(associationId, operation);
			return operation;
		}

		private CollectionUninstallEffectsResult BuildResult(CollectionOperation operation, Guid associationId,
			CollectionUninstallEffectsPlan plan)
		{
			return new CollectionUninstallEffectsResult(operation, _associationStore.GetAssociation(associationId), false,
				plan == null ? Enumerable.Empty<CollectionUninstallNativeImpact>() : plan.Impacts);
		}

		private static CollectionNativeChildOperation RequireChild(CollectionOperation operation, int sequence,
			CollectionNativeChildCheckpoint checkpoint)
		{
			CollectionNativeChildOperation child = operation.NativeChildren.SingleOrDefault(x => x.Sequence == sequence);
			if (child == null || child.Action != CollectionNativeChildAction.Deactivate || child.Checkpoint != checkpoint)
				throw new InvalidOperationException("The durable C6.14 child is not at the expected native safety checkpoint.");
			return child;
		}

		private static Task WaitForCompletionAsync(IBackgroundTaskSet task)
		{
			if (task.IsCompleted) return Task.FromResult<object>(null);
			var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
			EventHandler<TaskSetCompletedEventArgs> handler = null;
			handler = (sender, args) =>
			{
				task.TaskSetCompleted -= handler;
				completion.TrySetResult(null);
			};
			task.TaskSetCompleted += handler;
			if (task.IsCompleted)
			{
				task.TaskSetCompleted -= handler;
				completion.TrySetResult(null);
			}
			return completion.Task;
		}

		private static ModOperationResult CaptureReportedResult(IBackgroundTaskSet task, ModOperationIdentity expected,
			out bool exactIdentity)
		{
			exactIdentity = false;
			ModInstallerBase nativeTask = task as ModInstallerBase;
			if (nativeTask == null || nativeTask.OperationResult == null)
				return new ModOperationResult(expected, ModOperationReportedStatus.Failed, ModOperationDurability.Unknown,
					"The native uninstaller reached terminal state without publishing its identified operation result.");
			ModOperationResult result = nativeTask.OperationResult;
			if (!Matches(expected, result.Identity))
				return new ModOperationResult(expected, ModOperationReportedStatus.Failed, ModOperationDurability.Unknown,
					"The native uninstaller published a terminal result for a different operation attempt.");
			exactIdentity = true;
			return result;
		}

		private static ModOperationDurability DetermineDurability(ModOperationResult reported, bool exactIdentity,
			bool absent, bool present)
		{
			if (!exactIdentity)
				return ModOperationDurability.Unknown;
			if (absent)
				return ModOperationDurability.VerifiedCommitted;
			if (!present)
				return ModOperationDurability.Unknown;
			if (reported.Durability == ModOperationDurability.NotStarted)
				return ModOperationDurability.NotStarted;
			if (reported.Durability == ModOperationDurability.VerifiedRolledBack)
				return ModOperationDurability.VerifiedRolledBack;
			return ModOperationDurability.Unknown;
		}

		private static bool IsNativeModPresent(CollectionNativeStateIndex state, NativeModInstanceIdentity nativeMod)
		{
			return state.Mods.ContainsKey(nativeMod);
		}

		private static bool HasNativeOwnerReferences(CollectionNativeStateIndex state, NativeModInstanceIdentity nativeMod)
		{
			string key = nativeMod.NativeModKey;
			return state.FilesByOwnerKey.ContainsKey(key) || state.IniEditsByOwnerKey.ContainsKey(key) ||
				state.GameValuesByOwnerKey.ContainsKey(key);
		}

		private static bool IsNativeModFullyAbsent(CollectionNativeStateIndex state, NativeModInstanceIdentity nativeMod)
		{
			return !IsNativeModPresent(state, nativeMod) && !HasNativeOwnerReferences(state, nativeMod);
		}

		private void RequireLiveServices()
		{
			if (_services.ModManager == null || _services.ModActivationMonitor == null)
				throw new InvalidOperationException("C6.14 requires the live ModManager and ModActivationMonitor services.");
		}

		private static bool Matches(ModOperationIdentity left, ModOperationIdentity right)
		{
			return left != null && right != null && left.OperationId == right.OperationId && left.AttemptId == right.AttemptId &&
				left.Origin == right.Origin && left.Fingerprint.Equals(right.Fingerprint);
		}
	}
}
