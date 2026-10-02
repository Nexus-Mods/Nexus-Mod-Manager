using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
	/// <summary>Describes how one installed optional Collection member can be removed safely.</summary>
	public enum CollectionInstalledMemberRemovalDisposition
	{
		Unknown = 0,
		RemoveNativeMod = 1,
		AlreadyAbsent = 2,
		PreserveOtherMember = 3,
		PreserveSharedCollection = 4,
		PreserveStandalone = 5,
		PreserveUnknownProvenance = 6,
		PreserveCustomized = 7,
		BlockedSharedMissing = 8,
		BlockedNativeState = 9
	}

	/// <summary>Immutable review for removing one bound member from an installed Collection.</summary>
	public sealed class CollectionInstalledMemberRemovalPlan
	{
		private readonly ReadOnlyCollection<Guid> _survivingAssociationIds;

		internal CollectionInstalledMemberRemovalPlan(CollectionTargetAssociation association, CollectionMemberBinding binding,
			CollectionCurrentStateFingerprint stateFingerprint, CollectionInstalledMemberRemovalDisposition disposition,
			StandaloneModUse standaloneUse, IEnumerable<Guid> survivingAssociationIds,
			ModInstallMethod? installMethod, ModInstallRoot? installRoot, string reason)
		{
			Association = association ?? throw new ArgumentNullException(nameof(association));
			Binding = binding ?? throw new ArgumentNullException(nameof(binding));
			StateFingerprint = stateFingerprint ?? throw new ArgumentNullException(nameof(stateFingerprint));
			if (binding.Association.AssociationId != association.AssociationId)
				throw new ArgumentException("The reviewed member binding must belong to the reviewed installed Collection association.", nameof(binding));
			if (!Enum.IsDefined(typeof(CollectionInstalledMemberRemovalDisposition), disposition) || disposition == CollectionInstalledMemberRemovalDisposition.Unknown)
				throw new ArgumentOutOfRangeException(nameof(disposition));
			if (!Enum.IsDefined(typeof(StandaloneModUse), standaloneUse))
				throw new ArgumentOutOfRangeException(nameof(standaloneUse));
			Disposition = disposition;
			StandaloneUse = standaloneUse;
			_survivingAssociationIds = new ReadOnlyCollection<Guid>((survivingAssociationIds ?? Enumerable.Empty<Guid>())
				.Distinct().OrderBy(x => x).ToList());
			InstallMethod = installMethod;
			InstallRoot = installRoot;
			Reason = reason ?? String.Empty;
		}

		public CollectionTargetAssociation Association { get; }
		public CollectionMemberBinding Binding { get; }
		public CollectionMemberKey MemberKey { get { return Binding.MemberKey; } }
		public NativeModInstanceIdentity NativeMod { get { return Binding.NativeMod; } }
		public CollectionCurrentStateFingerprint StateFingerprint { get; }
		public CollectionInstalledMemberRemovalDisposition Disposition { get; }
		public StandaloneModUse StandaloneUse { get; }
		public ReadOnlyCollection<Guid> SurvivingAssociationIds { get { return _survivingAssociationIds; } }
		public ModInstallMethod? InstallMethod { get; }
		public ModInstallRoot? InstallRoot { get; }
		public string Reason { get; }
		public bool RequiresNativeMutation { get { return Disposition == CollectionInstalledMemberRemovalDisposition.RemoveNativeMod; } }
		public bool HasBlockedImpact
		{
			get
			{
				return Disposition == CollectionInstalledMemberRemovalDisposition.BlockedSharedMissing ||
					Disposition == CollectionInstalledMemberRemovalDisposition.BlockedNativeState;
			}
		}
	}

	/// <summary>Result of removing one installed Collection member.</summary>
	public sealed class CollectionInstalledMemberRemovalResult
	{
		internal CollectionInstalledMemberRemovalResult(CollectionOperation operation, CollectionTargetAssociation association,
			CollectionMemberKey memberKey, CollectionInstalledMemberRemovalDisposition disposition, bool bindingRemoved)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Association = association;
			MemberKey = memberKey ?? throw new ArgumentNullException(nameof(memberKey));
			Disposition = disposition;
			BindingRemoved = bindingRemoved;
		}

		public CollectionOperation Operation { get; }
		public CollectionTargetAssociation Association { get; }
		public CollectionMemberKey MemberKey { get; }
		public CollectionInstalledMemberRemovalDisposition Disposition { get; }
		public bool BindingRemoved { get; }
		public bool IsSuccessful { get { return Operation.IsSuccessful && BindingRemoved; } }
	}

	/// <summary>
	/// Removes one installed optional Collection member while preserving native state which is still shared, standalone,
	/// ambiguous or customized. Native removal uses the existing NMM uninstaller and a durable C9 operation journal.
	/// </summary>
	public sealed class CollectionInstalledMemberRemovalCoordinator
	{
		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;

		public CollectionInstalledMemberRemovalCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsAssociationStore associationStore)
			: this(services, gameStorageService, operationStore, associationStore, CollectionTargetMutationLeaseManager.Shared,
				new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services))
		{
		}

		internal CollectionInstalledMemberRemovalCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsAssociationStore associationStore,
			CollectionTargetMutationLeaseManager mutationLeaseManager, CollectionTargetOwnershipAuthorityValidator authorityValidator)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
			_mutationLeaseManager = mutationLeaseManager ?? throw new ArgumentNullException(nameof(mutationLeaseManager));
			_authorityValidator = authorityValidator ?? throw new ArgumentNullException(nameof(authorityValidator));
		}

		public async Task<CollectionInstalledMemberRemovalPlan> PreviewAsync(Guid associationId, CollectionMemberKey memberKey,
			GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			RequireLiveServices();
			if (associationId == Guid.Empty) throw new ArgumentException("A non-empty Collection association identifier is required.", nameof(associationId));
			if (memberKey == null) throw new ArgumentNullException(nameof(memberKey));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			CollectionTargetAssociation association = RequireAssociation(associationId);
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(association.Target))
				throw new InvalidOperationException("The live canonical game/storage target no longer matches the installed Collection association.");

			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				cancellationToken.ThrowIfCancellationRequested();
				_authorityValidator.ValidateAndReload(lease, authority, paths);
				association = RequireAssociation(associationId);
				RequireNoIncompleteOperation(association.Target);
				CollectionMemberBinding binding = RequireBinding(associationId, memberKey);
				return BuildPlanForState(_associationStore, association, binding, CaptureReloadedState(association.Target));
			}
		}

		public async Task<CollectionInstalledMemberRemovalResult> ExecuteAsync(CollectionInstalledMemberRemovalPlan reviewedPlan,
			GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			RequireLiveServices();
			if (reviewedPlan == null) throw new ArgumentNullException(nameof(reviewedPlan));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			if (reviewedPlan.HasBlockedImpact)
				throw new InvalidOperationException("The reviewed member-removal plan contains a native-state conflict that blocks safe removal.");

			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(reviewedPlan.Association.Target))
				throw new InvalidOperationException("The live canonical game/storage target no longer matches the reviewed member-removal plan.");

			using (CollectionTargetMutationLease rootLease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				cancellationToken.ThrowIfCancellationRequested();
				_authorityValidator.ValidateAndReload(rootLease, authority, paths);
				CollectionTargetAssociation association = RequireAssociation(reviewedPlan.Association.AssociationId);
				RequireNoIncompleteOperation(association.Target);
				CollectionMemberBinding binding = RequireBinding(association.AssociationId, reviewedPlan.MemberKey);
				CollectionInstalledMemberRemovalPlan livePlan = BuildPlanForState(_associationStore, association, binding,
					CaptureReloadedState(association.Target));
				if (!PlansEqual(reviewedPlan, livePlan))
					throw new InvalidOperationException("Collection/native state changed after the installed-member removal preview; review the member again before mutation.");

				if (!livePlan.RequiresNativeMutation)
				{
					CollectionOperation completed = new CollectionOperation(CollectionOperationIdentity.CreateNew(),
						CollectionOperationKind.RemoveCollectionMemberEffects, association.Revision.Collection, association.Target,
						association.Revision, null, 1, CollectionOperationPhase.Completed, CollectionOperationResultState.Committed,
						new CollectionNativeChildOperation[0]);
					bool preserveAsStandalone = RequiresStandalonePreservation(livePlan);
					_associationStore.CompleteMemberRemovalWithoutNativeMutation(reviewedPlan.Association, reviewedPlan.Binding,
						completed, preserveAsStandalone, livePlan.Disposition == CollectionInstalledMemberRemovalDisposition.AlreadyAbsent);
					return BuildResult(completed, association.AssociationId, livePlan, true);
				}

				CollectionOperationIdentity operationIdentity = CollectionOperationIdentity.CreateNew();
				CollectionOperation shell = new CollectionOperation(operationIdentity,
					CollectionOperationKind.RemoveCollectionMemberEffects, association.Revision.Collection, association.Target,
					association.Revision, null, 1, CollectionOperationPhase.ApplyingNativeChildren,
					CollectionOperationResultState.Pending, new CollectionNativeChildOperation[0]);
				CollectionNativeChildOperation child = CreatePreparedChild(shell, livePlan);
				CollectionOperation operation = new CollectionOperation(operationIdentity,
					CollectionOperationKind.RemoveCollectionMemberEffects, association.Revision.Collection, association.Target,
					association.Revision, null, 1, CollectionOperationPhase.ApplyingNativeChildren,
					CollectionOperationResultState.Pending, new[] { child });
				_associationStore.BeginMemberRemoval(reviewedPlan.Association, reviewedPlan.Binding, operation);

				IBackgroundTaskSet nativeTask;
				try
				{
					IMod nativeMod = ResolveLiveMod(livePlan.NativeMod);
					nativeTask = _services.ModManager.CreateCollectionDeactivationOperation(nativeMod,
						_services.ModManager.ActiveMods, child.NativeOperation);
					if (nativeTask == null)
						return BuildResult(CompleteStopped(operation, association.AssociationId, false), association.AssociationId, livePlan, false);
					ModInstallerBase installerTask = nativeTask as ModInstallerBase;
					if (installerTask == null || installerTask.OperationIdentity == null || !Matches(installerTask.OperationIdentity, child.NativeOperation))
						throw new InvalidOperationException("The native uninstaller did not retain the exact installed-member removal operation identity.");
					installerTask.AssignParentMutationLease(rootLease);
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
						CollectionOperation current = RequireActiveOperation(operation.Identity);
						CollectionNativeChildOperation currentChild = RequireChild(current, CollectionNativeChildCheckpoint.RecoveryInputsReady);
						var submitted = new CollectionNativeChildOperation(currentChild.Sequence, currentChild.Member,
							currentChild.Action, currentChild.NativeOperation, CollectionNativeChildCheckpoint.NativeSubmitted, null);
						current = ReplaceChild(current, submitted);
						_associationStore.SaveMemberRemovalChildSubmission(association.AssociationId, livePlan.MemberKey,
							current, livePlan.NativeMod);
					});
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					return BuildResult(CompleteCancellation(operation, association.AssociationId), association.AssociationId, livePlan, false);
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
					return BuildResult(CompleteStopped(_operationStore.GetOperation(operation.Identity), association.AssociationId, false),
						association.AssociationId, livePlan, false);

				try
				{
					await WaitForCompletionAsync(nativeTask).ConfigureAwait(true);
					bool exactResult;
					ModOperationResult reported = CaptureReportedResult(nativeTask, child.NativeOperation, out exactResult);
					_authorityValidator.ValidateAndReload(rootLease, authority, paths);
					CollectionNativeStateIndex state = CaptureReloadedState(association.Target);
					ModOperationDurability durability = DetermineDurability(reported, exactResult,
						IsNativeModFullyAbsent(state, livePlan.NativeMod), IsNativeModPresent(state, livePlan.NativeMod));
					var verifiedResult = new ModOperationResult(child.NativeOperation, reported.ReportedStatus, durability, reported.Message);
					operation = _operationStore.GetOperation(operation.Identity);
					CollectionNativeChildOperation submittedChild = RequireChild(operation, CollectionNativeChildCheckpoint.NativeSubmitted);
					var terminal = new CollectionNativeChildOperation(submittedChild.Sequence, submittedChild.Member,
						submittedChild.Action, submittedChild.NativeOperation, CollectionNativeChildCheckpoint.NativeTerminalObserved,
						verifiedResult);
					operation = ReplaceChild(operation, terminal);
					_operationStore.SaveOperation(operation);
					if (durability == ModOperationDurability.Unknown)
						return BuildResult(MarkRecoveryRequired(operation, association.AssociationId), association.AssociationId, livePlan, false);

					var reconciled = new CollectionNativeChildOperation(terminal.Sequence, terminal.Member, terminal.Action,
						terminal.NativeOperation, CollectionNativeChildCheckpoint.Reconciled, terminal.NativeResult);
					operation = ReplaceChild(operation, reconciled);
					_operationStore.SaveOperation(operation);
					if (durability != ModOperationDurability.VerifiedCommitted)
						return BuildResult(CompleteStopped(operation, association.AssociationId, true), association.AssociationId, livePlan, false);

					_authorityValidator.ValidateAndReload(rootLease, authority, paths);
					state = CaptureReloadedState(association.Target);
					if (!IsNativeModFullyAbsent(state, livePlan.NativeMod))
						throw new InvalidOperationException("The removed optional Collection member is still present in authoritative native state after verified native completion.");
					operation = WithOperationState(operation, CollectionOperationPhase.Completed, CollectionOperationResultState.Committed);
					_associationStore.CompleteMemberRemovalAfterNativeRemoval(association.AssociationId, livePlan.MemberKey,
						livePlan.NativeMod, operation);
					return BuildResult(operation, association.AssociationId, livePlan, true);
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

		public async Task<CollectionInstalledMemberRemovalResult> ReconcileInterruptedAsync(CollectionOperationIdentity operationIdentity,
			GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			RequireLiveServices();
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			CollectionOperation operation = RequireRecoverableOperation(operationIdentity);
			CollectionNativeChildOperation child = operation.NativeChildren.SingleOrDefault();
			if (child == null)
				throw new InvalidOperationException("The interrupted installed-member removal lost its durable member/native child correlation.");

			CollectionMemberBinding binding = ResolveJournalBinding(operation, child.Member.MemberKey);
			CollectionTargetAssociation association = RequireAssociation(binding.Association.AssociationId);
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(operation.Target))
				throw new InvalidOperationException("The live canonical game/storage target no longer matches the interrupted member-removal operation.");

			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				cancellationToken.ThrowIfCancellationRequested();
				_authorityValidator.ValidateAndReload(lease, authority, paths);
				operation = RequireRecoverableOperation(operationIdentity);
				binding = ResolveJournalBinding(operation, operation.NativeChildren.Single().Member.MemberKey);
				association = RequireAssociation(binding.Association.AssociationId);
				child = operation.NativeChildren.Single();
				if (!child.HasCrossedNativeBoundary)
				{
					operation = WithOperationState(operation, CollectionOperationPhase.Completed, CollectionOperationResultState.FailedBeforeApply);
					_operationStore.SaveOperation(operation);
					return new CollectionInstalledMemberRemovalResult(operation, association, binding.MemberKey,
						CollectionInstalledMemberRemovalDisposition.RemoveNativeMod, false);
				}

				CollectionNativeStateIndex state = CaptureReloadedState(operation.Target);
				bool absent = IsNativeModFullyAbsent(state, binding.NativeMod);
				bool present = IsNativeModPresent(state, binding.NativeMod);
				ModOperationDurability durability;
				if (absent)
					durability = ModOperationDurability.VerifiedCommitted;
				else if (child.NativeResult != null && (child.NativeResult.Durability == ModOperationDurability.NotStarted ||
					child.NativeResult.Durability == ModOperationDurability.VerifiedRolledBack) && present)
					durability = child.NativeResult.Durability;
				else
					durability = ModOperationDurability.Unknown;

				if (child.Checkpoint != CollectionNativeChildCheckpoint.Reconciled || child.NativeResult == null ||
					child.NativeResult.Durability != durability)
				{
					ModOperationReportedStatus status = child.NativeResult == null ? ModOperationReportedStatus.Failed : child.NativeResult.ReportedStatus;
					var result = new ModOperationResult(child.NativeOperation, status, durability,
						"Installed-member removal durability was reconstructed from authoritative native state after restart.");
					var reconciled = new CollectionNativeChildOperation(child.Sequence, child.Member, child.Action,
						child.NativeOperation, CollectionNativeChildCheckpoint.Reconciled, result);
					operation = ReplaceChild(operation, reconciled);
					_operationStore.SaveOperation(operation);
				}

				if (durability == ModOperationDurability.Unknown)
				{
					operation = MarkRecoveryRequired(operation, association.AssociationId);
					return new CollectionInstalledMemberRemovalResult(operation, _associationStore.GetAssociation(association.AssociationId),
						binding.MemberKey, CollectionInstalledMemberRemovalDisposition.RemoveNativeMod, false);
				}
				if (durability != ModOperationDurability.VerifiedCommitted)
				{
					operation = CompleteStopped(operation, association.AssociationId, true);
					return new CollectionInstalledMemberRemovalResult(operation, _associationStore.GetAssociation(association.AssociationId),
						binding.MemberKey, CollectionInstalledMemberRemovalDisposition.RemoveNativeMod, false);
				}

				operation = WithOperationState(operation, CollectionOperationPhase.Completed, CollectionOperationResultState.Committed);
				_associationStore.CompleteMemberRemovalAfterNativeRemoval(association.AssociationId, binding.MemberKey,
					binding.NativeMod, operation);
				return new CollectionInstalledMemberRemovalResult(operation, _associationStore.GetAssociation(association.AssociationId),
					binding.MemberKey, CollectionInstalledMemberRemovalDisposition.RemoveNativeMod, true);
			}
		}

		internal static CollectionInstalledMemberRemovalPlan BuildPlanForState(CollectionsAssociationStore associationStore,
			CollectionTargetAssociation association, CollectionMemberBinding binding, CollectionNativeStateIndex state)
		{
			if (associationStore == null) throw new ArgumentNullException(nameof(associationStore));
			if (association == null) throw new ArgumentNullException(nameof(association));
			if (binding == null) throw new ArgumentNullException(nameof(binding));
			if (state == null) throw new ArgumentNullException(nameof(state));
			if (association.State == CollectionAssociationState.Recovering || association.State == CollectionAssociationState.Incomplete)
				throw new InvalidOperationException("Only Applied or Modified installed Collections can remove an optional member.");
			if (binding.Association.AssociationId != association.AssociationId)
				throw new InvalidOperationException("The installed member binding no longer belongs to the reviewed association.");

			List<CollectionMemberBinding> allBindings = associationStore.GetBindingsForNativeMod(binding.NativeMod).ToList();
			List<CollectionMemberBinding> remainingBindings = allBindings.Where(x =>
				x.Association.AssociationId != association.AssociationId || !x.MemberKey.Equals(binding.MemberKey)).ToList();
			Guid[] survivingAssociations = remainingBindings.Where(x => x.Association.AssociationId != association.AssociationId)
				.Select(x => x.Association.AssociationId).Distinct().OrderBy(x => x).ToArray();
			bool otherMemberInSameAssociation = remainingBindings.Any(x => x.Association.AssociationId == association.AssociationId);
			NativeModProvenance provenance = associationStore.GetNativeModProvenance(binding.NativeMod);
			IReadOnlyList<UserOverride> overrides = associationStore.GetOverrides(association.AssociationId);
			IReadOnlyList<CollectionDriftObservation> drift = associationStore.GetDriftObservations(association.AssociationId);
			bool customized = overrides.Any(x => x.Requirement.MemberKey == null || x.Requirement.MemberKey.Equals(binding.MemberKey)) ||
				drift.Any(x => x.Requirement.MemberKey == null || x.Requirement.MemberKey.Equals(binding.MemberKey));
			bool present = IsNativeModPresent(state, binding.NativeMod);
			bool hasReferences = HasNativeOwnerReferences(state, binding.NativeMod);
			CollectionNativeModState nativeState;
			state.Mods.TryGetValue(binding.NativeMod, out nativeState);

			CollectionInstalledMemberRemovalDisposition disposition;
			string reason;
			if (!present && remainingBindings.Count > 0)
			{
				disposition = CollectionInstalledMemberRemovalDisposition.BlockedSharedMissing;
				reason = "The same native mod is still required by another Collection binding, but authoritative native state no longer contains that instance.";
			}
			else if (customized)
			{
				disposition = CollectionInstalledMemberRemovalDisposition.PreserveCustomized;
				reason = "This member has deliberate local customization or detected drift, so NMM will stop tracking the member but preserve the native mod as standalone state.";
			}
			else if (otherMemberInSameAssociation)
			{
				disposition = CollectionInstalledMemberRemovalDisposition.PreserveOtherMember;
				reason = "Another member in the same installed Collection is bound to the same native mod instance, so the native mod must remain installed.";
			}
			else if (survivingAssociations.Length > 0)
			{
				disposition = CollectionInstalledMemberRemovalDisposition.PreserveSharedCollection;
				reason = "Another installed Collection still binds the same native mod instance, so only this member binding can be removed.";
			}
			else if (!present && hasReferences)
			{
				disposition = CollectionInstalledMemberRemovalDisposition.BlockedNativeState;
				reason = "Native ownership/effect records still reference the member's mod key, but no active native mod registration can drive a safe uninstall.";
			}
			else if (!present)
			{
				disposition = CollectionInstalledMemberRemovalDisposition.AlreadyAbsent;
				reason = "The member's native mod and owned effects are already absent; only the Collection member binding needs to be removed.";
			}
			else if (provenance.StandaloneUse == StandaloneModUse.ExplicitStandaloneUse)
			{
				disposition = CollectionInstalledMemberRemovalDisposition.PreserveStandalone;
				reason = "Explicit standalone provenance requires the native mod independently from this Collection member.";
			}
			else if (provenance.StandaloneUse == StandaloneModUse.Unknown)
			{
				disposition = CollectionInstalledMemberRemovalDisposition.PreserveUnknownProvenance;
				reason = "Standalone provenance is unknown, so NMM will stop tracking the member without uninstalling the native mod.";
			}
			else
			{
				string coverageFailure = GetAuthoritativeRemovalCoverageFailure(state, binding.NativeMod);
				if (coverageFailure != null)
				{
					disposition = CollectionInstalledMemberRemovalDisposition.BlockedNativeState;
					reason = coverageFailure;
				}
				else
				{
					disposition = CollectionInstalledMemberRemovalDisposition.RemoveNativeMod;
					reason = "No other Collection binding or standalone provenance protects this native mod, so native uninstallation is permitted.";
				}
			}

			return new CollectionInstalledMemberRemovalPlan(association, binding, state.Fingerprint, disposition,
				provenance.StandaloneUse, survivingAssociations,
				nativeState == null ? (ModInstallMethod?)null : nativeState.InstallMethod,
				nativeState == null ? (ModInstallRoot?)null : nativeState.InstallRoot, reason);
		}

		internal static CollectionMemberKey CreateRemovalJournalKey(Guid associationId, CollectionMemberKey memberKey)
		{
			if (associationId == Guid.Empty) throw new ArgumentException("A non-empty association identifier is required.", nameof(associationId));
			if (memberKey == null) throw new ArgumentNullException(nameof(memberKey));
			byte[] bytes = Encoding.UTF8.GetBytes("collection-member-remove-v1\n" + associationId.ToString("D") + "\n" + memberKey.ToString());
			using (SHA256 sha = SHA256.Create())
			{
				byte[] hash = sha.ComputeHash(bytes);
				byte[] guidBytes = new byte[16];
				Buffer.BlockCopy(hash, 0, guidBytes, 0, guidBytes.Length);
				return CollectionMemberKey.FromLocal(new Guid(guidBytes));
			}
		}

		private static string GetAuthoritativeRemovalCoverageFailure(CollectionNativeStateIndex state, NativeModInstanceIdentity nativeMod)
		{
			if (state.AssociationCoverage != CollectionNativeStateCoverage.Complete)
				return "Collection association coverage is unavailable, so NMM cannot prove this optional member is dispensable.";
			if (state.PluginCoverage == CollectionNativeStateCoverage.Unavailable)
				return "Native plugin-state coverage is unavailable, so NMM cannot prove complete member removal safely.";

			ReadOnlyCollection<CollectionNativeFileState> ownedFiles;
			ReadOnlyCollection<CollectionNativeIniState> ownedIni;
			state.FilesByOwnerKey.TryGetValue(nativeMod.NativeModKey, out ownedFiles);
			state.IniEditsByOwnerKey.TryGetValue(nativeMod.NativeModKey, out ownedIni);
			var fileResources = new HashSet<string>((ownedFiles ?? new ReadOnlyCollection<CollectionNativeFileState>(new List<CollectionNativeFileState>()))
				.Select(x => x.Target.ToString()), StringComparer.OrdinalIgnoreCase);
			var fileRoots = new HashSet<string>((ownedFiles ?? new ReadOnlyCollection<CollectionNativeFileState>(new List<CollectionNativeFileState>()))
				.Select(x => x.Target.Root.ToString()), StringComparer.OrdinalIgnoreCase);
			var iniResources = new HashSet<string>((ownedIni ?? new ReadOnlyCollection<CollectionNativeIniState>(new List<CollectionNativeIniState>()))
				.Select(x => x.Key.ToString()), StringComparer.OrdinalIgnoreCase);
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
					return String.Format("Native-state coverage for this member is incomplete at '{0}': {1}", issue.ResourceKey, issue.Message);
			}
			return null;
		}

		private CollectionMemberBinding ResolveJournalBinding(CollectionOperation operation, CollectionMemberKey journalKey)
		{
			var matches = new List<CollectionMemberBinding>();
			foreach (CollectionTargetAssociation association in _associationStore.GetAssociationsForTarget(operation.Target)
				.Where(x => x.Revision.Equals(operation.Revision)))
			{
				foreach (CollectionMemberBinding binding in _associationStore.GetBindings(association.AssociationId))
					if (CreateRemovalJournalKey(association.AssociationId, binding.MemberKey).Equals(journalKey))
						matches.Add(binding);
			}
			if (matches.Count != 1)
				throw new InvalidOperationException("The interrupted installed-member removal no longer maps to exactly one durable Collection member binding.");
			return matches[0];
		}

		private CollectionNativeChildOperation CreatePreparedChild(CollectionOperation operation, CollectionInstalledMemberRemovalPlan plan)
		{
			if (!plan.InstallMethod.HasValue || !plan.InstallRoot.HasValue)
				throw new InvalidOperationException("The removable optional Collection member is missing its installed native context.");
			var identity = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint(operation.Target.Fingerprint,
					new ModInstallContext(plan.InstallMethod.Value, plan.InstallRoot.Value), null));
			var member = new CollectionOperationMemberReference(operation.Revision,
				CreateRemovalJournalKey(plan.Association.AssociationId, plan.MemberKey));
			return new CollectionNativeChildOperation(1, member, CollectionNativeChildAction.Deactivate, identity,
				CollectionNativeChildCheckpoint.RecoveryInputsReady, null);
		}

		private static bool PlansEqual(CollectionInstalledMemberRemovalPlan left, CollectionInstalledMemberRemovalPlan right)
		{
			return left != null && right != null && left.Association.AssociationId == right.Association.AssociationId &&
				left.Association.State == right.Association.State && left.Binding.MemberKey.Equals(right.Binding.MemberKey) &&
				left.Binding.NativeMod.Equals(right.Binding.NativeMod) && left.Binding.BindingKind == right.Binding.BindingKind &&
				left.Binding.VerifiedRecipe.Equals(right.Binding.VerifiedRecipe) && left.StateFingerprint.Equals(right.StateFingerprint) &&
				left.Disposition == right.Disposition && left.StandaloneUse == right.StandaloneUse &&
				left.InstallMethod == right.InstallMethod && left.InstallRoot == right.InstallRoot &&
				left.SurvivingAssociationIds.SequenceEqual(right.SurvivingAssociationIds);
		}

		private static bool RequiresStandalonePreservation(CollectionInstalledMemberRemovalPlan plan)
		{
			return plan.Disposition == CollectionInstalledMemberRemovalDisposition.PreserveStandalone ||
				plan.Disposition == CollectionInstalledMemberRemovalDisposition.PreserveUnknownProvenance ||
				plan.Disposition == CollectionInstalledMemberRemovalDisposition.PreserveCustomized;
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
			if (operation.Phase == CollectionOperationPhase.RecoveryRequired && operation.ResultState == CollectionOperationResultState.RecoveryRequired)
				return operation;
			operation = WithOperationState(operation, CollectionOperationPhase.RecoveryRequired, CollectionOperationResultState.RecoveryRequired);
			_associationStore.SaveMemberRemovalRecoveryRequired(associationId, operation);
			return operation;
		}

		private CollectionOperation CompleteCancellation(CollectionOperation operation, Guid associationId)
		{
			operation = _operationStore.GetOperation(operation.Identity) ?? operation;
			if (!operation.HasCrossedNativeBoundary)
			{
				operation = WithOperationState(operation, CollectionOperationPhase.Completed, CollectionOperationResultState.CancelledBeforeApply);
				_operationStore.SaveOperation(operation);
				return operation;
			}
			operation = WithOperationState(operation, CollectionOperationPhase.Completed, CollectionOperationResultState.StoppedPartial);
			_associationStore.SaveMemberRemovalStoppedPartial(associationId, operation);
			return operation;
		}

		private CollectionOperation CompleteStopped(CollectionOperation operation, Guid associationId, bool crossedBoundary)
		{
			operation = _operationStore.GetOperation(operation.Identity) ?? operation;
			if (!crossedBoundary && !operation.HasCrossedNativeBoundary)
			{
				operation = WithOperationState(operation, CollectionOperationPhase.Completed, CollectionOperationResultState.FailedBeforeApply);
				_operationStore.SaveOperation(operation);
				return operation;
			}
			operation = WithOperationState(operation, CollectionOperationPhase.Completed, CollectionOperationResultState.StoppedPartial);
			_associationStore.SaveMemberRemovalStoppedPartial(associationId, operation);
			return operation;
		}

		private CollectionInstalledMemberRemovalResult BuildResult(CollectionOperation operation, Guid associationId,
			CollectionInstalledMemberRemovalPlan plan, bool bindingRemoved)
		{
			return new CollectionInstalledMemberRemovalResult(operation, _associationStore.GetAssociation(associationId),
				plan.MemberKey, plan.Disposition, bindingRemoved);
		}

		private CollectionOperation RequireActiveOperation(CollectionOperationIdentity identity)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Kind != CollectionOperationKind.RemoveCollectionMemberEffects || operation.Revision == null ||
				operation.Phase != CollectionOperationPhase.ApplyingNativeChildren || operation.ResultState != CollectionOperationResultState.Pending)
				throw new InvalidOperationException("The installed-member removal operation is no longer active at the expected native boundary.");
			return operation;
		}

		private CollectionOperation RequireRecoverableOperation(CollectionOperationIdentity identity)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Kind != CollectionOperationKind.RemoveCollectionMemberEffects || operation.Revision == null)
				throw new InvalidOperationException("The requested operation is not a persisted installed-member removal operation.");
			if (operation.IsTerminal)
				throw new InvalidOperationException("The installed-member removal operation is already terminal.");
			if (operation.Phase != CollectionOperationPhase.ApplyingNativeChildren &&
				operation.Phase != CollectionOperationPhase.Recovering && operation.Phase != CollectionOperationPhase.RecoveryRequired)
				throw new InvalidOperationException("The installed-member removal operation is not at a restart-reconcilable boundary.");
			return operation;
		}

		private static CollectionNativeChildOperation RequireChild(CollectionOperation operation, CollectionNativeChildCheckpoint checkpoint)
		{
			CollectionNativeChildOperation child = operation.NativeChildren.SingleOrDefault();
			if (child == null || child.Action != CollectionNativeChildAction.Deactivate || child.Checkpoint != checkpoint)
				throw new InvalidOperationException("The durable installed-member removal child is not at the expected safety checkpoint.");
			return child;
		}

		private CollectionTargetAssociation RequireAssociation(Guid associationId)
		{
			CollectionTargetAssociation association = _associationStore.GetAssociation(associationId);
			if (association == null)
				throw new InvalidOperationException("The installed Collection association no longer exists.");
			return association;
		}

		private CollectionMemberBinding RequireBinding(Guid associationId, CollectionMemberKey memberKey)
		{
			CollectionMemberBinding binding = _associationStore.GetBindings(associationId).SingleOrDefault(x => x.MemberKey.Equals(memberKey));
			if (binding == null)
				throw new InvalidOperationException("The selected installed Collection member is no longer bound to this association.");
			return binding;
		}

		private CollectionNativeStateIndex CaptureReloadedState(CollectionTargetIdentity target)
		{
			ModManager modManager = _services.ModManager;
			return new CollectionNativeStateReader(modManager.InstallationLog, modManager.VirtualModActivator,
				_services.PluginManager, modManager.GameMode, _associationStore).Capture(target);
		}

		private IMod ResolveLiveMod(NativeModInstanceIdentity nativeMod)
		{
			List<IMod> matches = _services.ModManager.ActiveMods.Where(mod =>
			{
				string key;
				try { key = _services.ModManager.InstallationLog.GetModKey(mod); }
				catch { return false; }
				return StringComparer.Ordinal.Equals(key, nativeMod.NativeModKey);
			}).ToList();
			if (matches.Count != 1)
				throw new InvalidOperationException("The reviewed optional Collection member cannot be resolved to exactly one live active native mod instance.");
			return matches[0];
		}

		private void RequireNoIncompleteOperation(CollectionTargetIdentity target)
		{
			if (_operationStore.GetIncompleteOperations(target).Count != 0)
				throw new InvalidOperationException("Installed-member removal cannot begin while another Collection operation for this game is incomplete.");
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

		private static ModOperationResult CaptureReportedResult(IBackgroundTaskSet task, ModOperationIdentity expected, out bool exactIdentity)
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

		private static ModOperationDurability DetermineDurability(ModOperationResult reported, bool exactIdentity, bool absent, bool present)
		{
			if (!exactIdentity) return ModOperationDurability.Unknown;
			if (absent) return ModOperationDurability.VerifiedCommitted;
			if (!present) return ModOperationDurability.Unknown;
			if (reported.Durability == ModOperationDurability.NotStarted) return ModOperationDurability.NotStarted;
			if (reported.Durability == ModOperationDurability.VerifiedRolledBack) return ModOperationDurability.VerifiedRolledBack;
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
				throw new InvalidOperationException("Installed-member removal requires the live ModManager and ModActivationMonitor services.");
		}

		private static bool Matches(ModOperationIdentity left, ModOperationIdentity right)
		{
			return left != null && right != null && left.OperationId == right.OperationId && left.AttemptId == right.AttemptId &&
				left.Origin == right.Origin && left.Fingerprint.Equals(right.Fingerprint);
		}
	}
}
