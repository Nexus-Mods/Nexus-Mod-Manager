using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.Mods;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Submits one already-prepared additive Collection child through the native C3/C5 execution seam while the C4 target reservation is held.
	/// </summary>
	/// <remarks>
	/// C6.7 advances the durable child only through <see cref="CollectionNativeChildCheckpoint.NativeSubmitted"/>. Native task completion is
	/// not treated as durable success here; C6.8 must inspect authoritative native state and checkpoint the observed outcome before the
	/// target reservation is released.
	/// </remarks>
	public sealed class CollectionNativeChildExecutionCoordinator
	{
		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsResolvedPlanStore _planStore;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionsNativeChildRecoveryManifestStore _manifestStore;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;

		/// <summary>Creates a production C6.7 executor over the established native and Collections services.</summary>
		public CollectionNativeChildExecutionCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore,
			CollectionsAssociationStore associationStore, CollectionsNativeChildRecoveryManifestStore manifestStore)
			: this(services, gameStorageService, operationStore, planStore, associationStore, manifestStore,
				CollectionTargetMutationLeaseManager.Shared,
				new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services))
		{
		}

		/// <summary>Creates a C6.7 executor over explicit coordination services.</summary>
		internal CollectionNativeChildExecutionCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore,
			CollectionsAssociationStore associationStore, CollectionsNativeChildRecoveryManifestStore manifestStore,
			CollectionTargetMutationLeaseManager mutationLeaseManager,
			CollectionTargetOwnershipAuthorityValidator authorityValidator)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_planStore = planStore ?? throw new ArgumentNullException(nameof(planStore));
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
			_manifestStore = manifestStore ?? throw new ArgumentNullException(nameof(manifestStore));
			_mutationLeaseManager = mutationLeaseManager ?? throw new ArgumentNullException(nameof(mutationLeaseManager));
			_authorityValidator = authorityValidator ?? throw new ArgumentNullException(nameof(authorityValidator));
		}

		/// <summary>
		/// Revalidates and submits the one prepared child, returning <c>null</c> when the shared native lane is currently busy.
		/// </summary>
		/// <remarks>
		/// The returned result owns the root target mutation reservation through C6.8 authoritative verification. Callers cannot release
		/// that reservation directly. A busy native lane leaves the durable child at RecoveryInputsReady and performs no native start.
		/// </remarks>
		public Task<CollectionNativeChildExecutionResult> SubmitPreparedChildAsync(CollectionOperationIdentity operationIdentity,
			ResolvedCollectionPlan plan, CollectionConflictImpactPlan impactPlan, CollectionMemberEffectPreview reviewedPreview,
			ModInstallationRecipeInput recipeInput, GameStoragePathSet paths)
		{
			return SubmitPreparedChildAsync(operationIdentity, plan, impactPlan, reviewedPreview, recipeInput, paths,
				CancellationToken.None);
		}

		/// <summary>
		/// Revalidates and submits the one prepared child while honoring cancellation before native worker start.
		/// </summary>
		public async Task<CollectionNativeChildExecutionResult> SubmitPreparedChildAsync(CollectionOperationIdentity operationIdentity,
			ResolvedCollectionPlan plan, CollectionConflictImpactPlan impactPlan, CollectionMemberEffectPreview reviewedPreview,
			ModInstallationRecipeInput recipeInput, GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			if (plan == null) throw new ArgumentNullException(nameof(plan));
			if (impactPlan == null) throw new ArgumentNullException(nameof(impactPlan));
			if (reviewedPreview == null) throw new ArgumentNullException(nameof(reviewedPreview));
			if (recipeInput == null) throw new ArgumentNullException(nameof(recipeInput));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			if (_services.ModManager == null || _services.ModActivationMonitor == null)
				throw new InvalidOperationException("C6.7 requires the live ModManager and ModActivationMonitor services.");

			CollectionOperation operation = RequireOperation(operationIdentity, plan);
			CollectionNativeChildOperation child = RequirePreparedChild(operation);
			CollectionNativeChildRecoveryManifest recovery = RequireRecoveryManifest(operation, child, plan);
			ResolvedCollectionMemberPlan member = RequireMember(plan, child);
			ValidateImpactPlan(plan, impactPlan);
			ValidateExecutableFilePriorities(impactPlan.FileImpacts);
			ValidateReviewedPreview(member, reviewedPreview);
			ValidateRecipeInput(plan, child, member, recovery, recipeInput);

			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(plan.Target))
				throw new InvalidOperationException("The live canonical game/storage target no longer matches the approved Collection plan.");

			CollectionTargetMutationLease rootLease = null;
			try
			{
				rootLease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true);
				cancellationToken.ThrowIfCancellationRequested();

				_authorityValidator.ValidateAndReload(rootLease, authority, paths);

				// Reload may have performed native recovery. Re-read every durable correlation before constructing a native task.
				operation = RequireOperation(operationIdentity, plan);
				child = RequirePreparedChild(operation);
				recovery = RequireRecoveryManifest(operation, child, plan);
				ValidateRecipeInput(plan, child, member, recovery, recipeInput);

				CollectionNativeStateIndex liveState = CaptureReloadedState(plan.Target);
				CollectionCurrentStateFingerprint expectedPreparationState = ResolveExpectedPreparationStateFingerprint(operation, child, plan);
				if (!recovery.PreparationStateFingerprint.Equals(expectedPreparationState) ||
					!liveState.Fingerprint.Equals(recovery.PreparationStateFingerprint))
				{
					throw new InvalidOperationException("Authoritative native state changed after C6.6 preparation or no longer matches the latest verified Collection safe boundary.");
				}

				IMod previousMod = ResolvePreviousActiveMod(recovery, liveState, _services.ModManager);
				IMod incomingMod = ResolveIncomingManagedMod(member, previousMod, _services.ModManager);

				// Hashing and replay enumeration may be expensive. Keep the reservation, but do the I/O off the UI continuation.
				await Task.Run(() => ValidateLiveContentAtSubmission(previousMod, incomingMod, recovery,
					recipeInput.Validation.ExpectedContent, paths.InstallInfoPath), cancellationToken).ConfigureAwait(true);
				cancellationToken.ThrowIfCancellationRequested();

				CollectionMemberEffectPreview livePreview = new CollectionMemberEffectPreviewBuilder().Build(member,
					recipeInput, _services.ModManager.GameMode, incomingMod, _services.PluginManager);
				if (!EffectPreviewsEqual(reviewedPreview, livePreview))
					throw new InvalidOperationException("The translated C5 native effects no longer match the exact C6.4 impact preview approved for this child.");

				CollectionNativeChildExecutionEvidence executionEvidence = await Task.Run(() =>
					CaptureExecutionEvidence(member, reviewedPreview, recipeInput, incomingMod, paths.InstallInfoPath,
						_services.ModManager.GameMode), cancellationToken).ConfigureAwait(true);
				cancellationToken.ThrowIfCancellationRequested();
				if (recovery.ExecutionEvidence == null)
				{
					_manifestStore.SaveManifest(recovery.WithExecutionEvidence(executionEvidence));
					recovery = RequireRecoveryManifest(operation, child, plan);
				}
				else if (!ExecutionEvidenceEquals(recovery.ExecutionEvidence, executionEvidence))
				{
					throw new InvalidOperationException("The physical pre-start evidence changed after an earlier C6.7 submission attempt; the Collection must be replanned before mutation.");
				}
				if (recovery.ExecutionEvidence == null)
					throw new InvalidDataException("The exact C6.7 restart-verification evidence was not durably retained before native submission.");

				IBackgroundTaskSet nativeTask = CreateNativeTask(previousMod, incomingMod, recipeInput, _services.ModManager);
				if (nativeTask == null)
					throw new InvalidOperationException("The native operation unexpectedly resolved to no task; C6.7 cannot infer that the reviewed recipe is already satisfied.");
				ModInstallerBase nativeOperation = nativeTask as ModInstallerBase;
				if (nativeOperation == null || !Matches(child.NativeOperation, nativeOperation.OperationIdentity))
					throw new InvalidOperationException("The constructed native task does not preserve the exact prepared Collection operation identity.");
				nativeOperation.AssignParentMutationLease(rootLease);

				CollectionOperation submittedOperation = null;
				CollectionNativeChildOperation submittedChild = null;
				bool accepted = _services.ModActivationMonitor.SubmitWhenIdle(nativeTask, () =>
				{
					// This callback executes after monitor publication but before native worker start. Persisting this checkpoint is the
					// final durable ordering gate before any native mutation can begin.
					cancellationToken.ThrowIfCancellationRequested();
					CollectionOperation latest = RequireOperation(operationIdentity, plan);
					CollectionNativeChildOperation latestChild = RequirePreparedChild(latest);
					if (!Matches(latestChild.NativeOperation, child.NativeOperation))
						throw new InvalidOperationException("The prepared native child changed before the native worker could start.");
					submittedChild = new CollectionNativeChildOperation(latestChild.Sequence, latestChild.Member,
						latestChild.Action, latestChild.NativeOperation, CollectionNativeChildCheckpoint.NativeSubmitted, null);
					submittedOperation = SaveChild(latest, submittedChild);
				});

				if (!accepted)
					return null;
				if (submittedOperation == null || submittedChild == null)
					throw new InvalidOperationException("The native monitor accepted a Collection child without persisting NativeSubmitted first.");

				CollectionNativeChildExecutionResult result = new CollectionNativeChildExecutionResult(submittedOperation,
					submittedChild, nativeTask, recipeInput, incomingMod, rootLease);
				rootLease = null; // Ownership transfers to the C6.8 verification result.
				return result;
			}
			finally
			{
				if (rootLease != null)
					rootLease.Dispose();
			}
		}

		private CollectionOperation RequireOperation(CollectionOperationIdentity identity, ResolvedCollectionPlan plan)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null)
				throw new InvalidOperationException("The Collection operation is not present in the durable operation journal.");
			if (operation.Kind != CollectionOperationKind.ApplyResolvedPlan ||
				operation.Phase != CollectionOperationPhase.ApplyingNativeChildren ||
				operation.ResultState != CollectionOperationResultState.Pending)
			{
				throw new InvalidOperationException("C6.7 requires an active additive operation in ApplyingNativeChildren.");
			}
			if (operation.PlanIdentity == null || !operation.PlanIdentity.Equals(plan.Identity) ||
				operation.Revision == null || !operation.Revision.Equals(plan.Revision) || !operation.Target.Equals(plan.Target))
			{
				throw new ArgumentException("The Collection execution inputs do not belong to the operation's exact approved plan.", nameof(plan));
			}
			if (plan.Policy.Kind != CollectionExecutionPolicyKind.InstallIntoCurrentSetup)
				throw new ArgumentException("C6.7 only executes additive Collection plans.", nameof(plan));

			CollectionResolvedPlanRecord persisted = _planStore.GetPlan(plan.Identity);
			if (persisted == null || !persisted.Revision.Equals(plan.Revision) || !persisted.Target.Equals(plan.Target) ||
				persisted.PolicyKind != plan.Policy.Kind || !persisted.CurrentStateFingerprint.Equals(plan.CurrentStateFingerprint))
			{
				throw new InvalidOperationException("The exact approved Collection plan is not durably persisted for native submission.");
			}
			return operation;
		}

		private static CollectionNativeChildOperation RequirePreparedChild(CollectionOperation operation)
		{
			List<CollectionNativeChildOperation> pending = operation.NativeChildren.Where(x => !x.IsReconciled).ToList();
			if (pending.Count != 1)
				throw new InvalidOperationException("C6.7 requires exactly one serialized unreconciled native child.");
			CollectionNativeChildOperation child = pending[0];
			if (child.Action != CollectionNativeChildAction.ActivateOrReinstall ||
				child.NativeOperation.Origin != ModOperationOrigin.Collection ||
				child.Checkpoint != CollectionNativeChildCheckpoint.RecoveryInputsReady)
			{
				throw new InvalidOperationException("The Collection child is not at the exact RecoveryInputsReady additive submission boundary.");
			}
			return child;
		}

		private CollectionNativeChildRecoveryManifest RequireRecoveryManifest(CollectionOperation operation,
			CollectionNativeChildOperation child, ResolvedCollectionPlan plan)
		{
			CollectionNativeChildRecoveryManifest recovery = _manifestStore.GetManifest(operation, child);
			if (recovery == null)
				throw new InvalidDataException("A prepared Collection child is missing its retained C6.6 recovery manifest.");
			if (recovery.OperationIdentity.OperationId != operation.Identity.OperationId || recovery.ChildSequence != child.Sequence ||
				!recovery.PlanIdentity.Equals(plan.Identity) || !recovery.Member.MemberKey.Equals(child.Member.MemberKey) ||
				recovery.Action != child.Action || !Matches(recovery.NativeOperation, child.NativeOperation))
			{
				throw new InvalidDataException("The retained C6.6 recovery manifest does not match the exact prepared native child.");
			}
			return recovery;
		}

		private CollectionCurrentStateFingerprint ResolveExpectedPreparationStateFingerprint(CollectionOperation operation,
			CollectionNativeChildOperation currentChild, ResolvedCollectionPlan plan)
		{
			CollectionCurrentStateFingerprint expected = plan.CurrentStateFingerprint;
			foreach (CollectionNativeChildOperation previous in operation.NativeChildren.OrderBy(x => x.Sequence))
			{
				if (previous.Sequence >= currentChild.Sequence) break;
				if (!previous.IsReconciled || previous.NativeResult == null ||
					previous.NativeResult.Durability != ModOperationDurability.VerifiedCommitted)
					throw new InvalidOperationException("Every earlier Collection child must be reconciled as VerifiedCommitted before a later reviewed child can be submitted.");
				CollectionNativeChildRecoveryManifest manifest = _manifestStore.GetManifest(operation, previous);
				if (manifest == null || manifest.SafeBoundaryStateFingerprint == null)
					throw new InvalidDataException("An earlier committed Collection child is missing its durable C6.10 safe-boundary fingerprint.");
				expected = manifest.SafeBoundaryStateFingerprint;
			}
			return expected;
		}

		private static ResolvedCollectionMemberPlan RequireMember(ResolvedCollectionPlan plan, CollectionNativeChildOperation child)
		{
			ResolvedCollectionMemberPlan member = plan.SelectedMembers.SingleOrDefault(x => x.MemberKey.Equals(child.Member.MemberKey));
			if (member == null)
				throw new InvalidOperationException("The prepared native child no longer exists in the exact approved Collection plan.");
			if (!child.Member.Revision.Equals(plan.Revision))
				throw new InvalidOperationException("The prepared native child belongs to a different Collection revision.");
			return member;
		}

		private static void ValidateImpactPlan(ResolvedCollectionPlan plan, CollectionConflictImpactPlan impactPlan)
		{
			if (!impactPlan.PlanIdentity.Equals(plan.Identity) || !impactPlan.Target.Equals(plan.Target) ||
				!impactPlan.StateFingerprint.Equals(plan.CurrentStateFingerprint))
			{
				throw new ArgumentException("The C6.4 impact plan does not belong to the exact approved Collection plan.", nameof(impactPlan));
			}
			if (!impactPlan.IsReady)
				throw new InvalidOperationException("A Collection child cannot be submitted until C6.4 conflict/impact review is fully ready.");
		}

		private static void ValidateExecutableFilePriorities(IEnumerable<CollectionFileImpact> fileImpacts)
		{
			if (fileImpacts == null) throw new ArgumentNullException(nameof(fileImpacts));

			foreach (CollectionFileImpact impact in fileImpacts)
			{
				if (impact == null || impact.Writers.Count <= 1)
					continue;

				if (impact.PlannedWinner == null || !impact.Writers.Contains(impact.PlannedWinner))
					throw new InvalidOperationException(String.Format(CultureInfo.InvariantCulture,
						"The reviewed multi-writer target '{0}' has no deterministic C6.4 winner and cannot be submitted.", impact.Target));

				// C6.15.11 reconciles the exact reviewed winner after every required writer owner exists.
				// Child installation order is therefore transient native state, never the authoritative final priority decision.
			}
		}

		private static void ValidateReviewedPreview(ResolvedCollectionMemberPlan member, CollectionMemberEffectPreview preview)
		{
			if (!preview.IsComplete || !preview.MemberKey.Equals(member.MemberKey) || !preview.RecipeIdentity.Equals(member.RecipeIdentity))
				throw new ArgumentException("The supplied effect preview is not the complete reviewed C6.4 preview for this member.", nameof(preview));
		}

		private static void ValidateRecipeInput(ResolvedCollectionPlan plan, CollectionNativeChildOperation child,
			ResolvedCollectionMemberPlan member, CollectionNativeChildRecoveryManifest recovery, ModInstallationRecipeInput recipeInput)
		{
			if (!recipeInput.HasNativePlan)
				throw new ArgumentException("C6.7 requires the already translated C5 native operation plan.", nameof(recipeInput));
			if (!Matches(child.NativeOperation, recipeInput.OperationIdentity))
				throw new ArgumentException("The C5 recipe input does not carry the exact prepared native operation attempt.", nameof(recipeInput));
			if (!StringComparer.Ordinal.Equals(recipeInput.TargetFingerprint, plan.Target.Fingerprint) ||
				!StringComparer.Ordinal.Equals(recipeInput.RecipeFingerprint, member.RecipeIdentity.Fingerprint) ||
				recipeInput.InstallContext.Method != child.NativeOperation.Fingerprint.InstallMethod ||
				recipeInput.InstallContext.InstallRoot != child.NativeOperation.Fingerprint.InstallRoot)
			{
				throw new ArgumentException("The C5 recipe input target, recipe or install context does not match the prepared child.", nameof(recipeInput));
			}
			ModInstallationRecipeExpectedContent expected = recipeInput.Validation.ExpectedContent;
			if (!StringComparer.Ordinal.Equals(expected.Sha256, recovery.IncomingArchive.ContentHash.Value) ||
				expected.ByteLength != recovery.IncomingArchive.ByteLength)
			{
				throw new ArgumentException("The C5 expected archive bytes do not match the exact immutable C6.6 retained incoming archive.", nameof(recipeInput));
			}
		}

		private CollectionNativeStateIndex CaptureReloadedState(CollectionTargetIdentity target)
		{
			ModManager modManager = _services.ModManager;
			return new CollectionNativeStateReader(modManager.InstallationLog, modManager.VirtualModActivator,
				_services.PluginManager, modManager.GameMode, _associationStore).Capture(target);
		}

		private static IMod ResolvePreviousActiveMod(CollectionNativeChildRecoveryManifest recovery,
			CollectionNativeStateIndex state, ModManager modManager)
		{
			if (recovery.PreviousNativeMod == null)
				return null;

			CollectionNativeModState current;
			if (!state.Mods.TryGetValue(recovery.PreviousNativeMod.Identity, out current))
				throw new InvalidOperationException("The native instance selected for reinstall is no longer active after target reload.");
			if (!SameNativeMod(recovery.PreviousNativeMod, current))
				throw new InvalidOperationException("The native instance selected for reinstall changed after C6.6 preparation.");

			List<IMod> matches = modManager.InstallationLog.ActiveMods
				.Where(x => x != null && StringComparer.OrdinalIgnoreCase.Equals(
					modManager.InstallationLog.GetModKey(x), recovery.PreviousNativeMod.Identity.NativeModKey)).ToList();
			if (matches.Count != 1)
				throw new InvalidOperationException("The prepared previous native mod cannot be resolved unambiguously from the reloaded InstallLog.");
			return matches[0];
		}

		private static IMod ResolveIncomingManagedMod(ResolvedCollectionMemberPlan member, IMod previousMod, ModManager modManager)
		{
			string expectedDomain;
			long expectedModId;
			long expectedFileId;
			if (!NexusCollectionModFileArtifactIdentity.TryParse(member.ArtifactChoice.SelectedArtifact,
				out expectedDomain, out expectedModId, out expectedFileId))
			{
				throw new InvalidOperationException("C6.7 currently executes only exact Nexus mod-file artifacts supported by the C4 acquisition pipeline.");
			}

			string currentDomain = modManager.ModRepository == null ? null : modManager.ModRepository.GameDomainName;
			if (!StringComparer.OrdinalIgnoreCase.Equals(currentDomain, expectedDomain))
				throw new InvalidOperationException("The active native repository game does not match the prepared Collection artifact domain.");

			string modId = expectedModId.ToString(CultureInfo.InvariantCulture);
			string fileId = expectedFileId.ToString(CultureInfo.InvariantCulture);
			if (previousMod != null && ModFileIdentity.IsSameRepositoryFile(previousMod.Id, previousMod.DownloadId, modId, fileId))
				return previousMod;

			List<IMod> candidates = modManager.ManagedMods.Where(x => x != null &&
				ModFileIdentity.IsSameRepositoryFile(x.Id, x.DownloadId, modId, fileId)).ToList();
			if (candidates.Count != 1)
				throw new InvalidOperationException(candidates.Count == 0
					? "The exact verified incoming archive is not present in the native managed-mod registry."
					: "Multiple native managed archives match the exact prepared Nexus mod/file identity.");
			return candidates[0];
		}

		private static void ValidateLiveContentAtSubmission(IMod previousMod, IMod incomingMod,
			CollectionNativeChildRecoveryManifest recovery, ModInstallationRecipeExpectedContent incomingExpected,
			string installInfoDirectory)
		{
			if (incomingMod == null) throw new ArgumentNullException(nameof(incomingMod));
			if (incomingExpected == null) throw new ArgumentNullException(nameof(incomingExpected));
			if (String.IsNullOrWhiteSpace(installInfoDirectory))
				throw new ArgumentException("The authoritative InstallInfo directory is required for replay revalidation.", nameof(installInfoDirectory));

			string incomingPath = GetArchivePath(incomingMod);
			if (previousMod != null)
			{
				if (recovery.PreviousArchive == null || recovery.PreviousNativeMod == null)
					throw new InvalidDataException("A reinstall child is missing its retained previous archive recovery descriptor.");

				string previousPath = GetArchivePath(previousMod);
				bool samePhysicalArchive = SamePhysicalPath(previousPath, incomingPath) &&
					StringComparer.Ordinal.Equals(recovery.PreviousArchive.ContentHash.Value, incomingExpected.Sha256) &&
					recovery.PreviousArchive.ByteLength == incomingExpected.ByteLength;
				if (samePhysicalArchive)
					ValidateManagedArchiveBytes(incomingPath, incomingExpected);
				else
				{
					ValidateRecoveryArchiveBytes(previousPath, recovery.PreviousArchive);
					ValidateManagedArchiveBytes(incomingPath, incomingExpected);
				}
				ValidateLiveReplayPreimage(recovery, installInfoDirectory);
			}
			else
			{
				if (recovery.PreviousArchive != null || recovery.PreviousNativeMod != null)
					throw new InvalidDataException("A new activation child unexpectedly contains previous-install recovery identity.");
				ValidateManagedArchiveBytes(incomingPath, incomingExpected);
			}
		}

		private static string GetArchivePath(IMod mod)
		{
			string path = !String.IsNullOrWhiteSpace(mod.ModArchivePath) ? mod.ModArchivePath : mod.Filename;
			if (String.IsNullOrWhiteSpace(path))
				throw new InvalidDataException("A native managed mod does not expose its archive path.");
			return path;
		}

		private static void ValidateRecoveryArchiveBytes(string path, CollectionRecoveryArtifact expected)
		{
			if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
				throw new FileNotFoundException("The live previous native archive required for submission revalidation is unavailable.", path);
			var info = new FileInfo(path);
			if (info.Length != expected.ByteLength || !StringComparer.Ordinal.Equals(ComputeSha256(path), expected.ContentHash.Value))
				throw new InvalidDataException("The live previous native archive changed after C6.6 recovery inputs were retained.");
		}

		private static void ValidateManagedArchiveBytes(string path, ModInstallationRecipeExpectedContent expected)
		{
			if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
				throw new FileNotFoundException("The exact incoming native archive is unavailable at the submission boundary.", path);
			var info = new FileInfo(path);
			if (info.Length != expected.ByteLength || !StringComparer.Ordinal.Equals(ComputeSha256(path), expected.Sha256))
				throw new InvalidDataException("The incoming native archive bytes no longer match the exact C5 recipe content identity.");
		}

		private static void ValidateLiveReplayPreimage(CollectionNativeChildRecoveryManifest recovery, string installInfoDirectory)
		{
			CollectionScriptedReplayRecoverySnapshot snapshot = recovery.ScriptedReplay;
			string replayPath = ScriptedFileSelectionCache.GetDefaultFilePath(recovery.PreviousNativeMod.FileName, installInfoDirectory);
			bool replayExists = File.Exists(replayPath);
			if (replayExists != snapshot.ReplayFileExisted)
				throw new InvalidDataException("The live scripted replay XML presence changed after C6.6 preparation.");
			if (replayExists)
				ValidateRecoveryArchiveBytes(replayPath, snapshot.ReplayFile);

			string payloadDirectory = ScriptedFileSelectionCache.GetPayloadDirectoryPath(replayPath);
			bool payloadDirectoryExists = Directory.Exists(payloadDirectory);
			if (payloadDirectoryExists != snapshot.PayloadDirectoryExisted)
				throw new InvalidDataException("The live scripted replay payload-directory presence changed after C6.6 preparation.");
			if (!payloadDirectoryExists)
				return;

			Dictionary<string, CollectionReplayRecoveryPayload> expected = snapshot.Payloads.ToDictionary(x => x.RelativePath,
				x => x, StringComparer.OrdinalIgnoreCase);
			string[] files = Directory.GetFiles(payloadDirectory, "*", SearchOption.AllDirectories);
			if (files.Length != expected.Count)
				throw new InvalidDataException("The live scripted replay payload set changed after C6.6 preparation.");
			foreach (string file in files)
			{
				string relative = GetRelativeReplayPayloadPath(payloadDirectory, file);
				CollectionReplayRecoveryPayload payload;
				if (!expected.TryGetValue(relative, out payload))
					throw new InvalidDataException("The live scripted replay payload set contains an unretained path.");
				ValidateRecoveryArchiveBytes(file, payload.Artifact);
			}
		}

		private static string GetRelativeReplayPayloadPath(string payloadDirectory, string filePath)
		{
			string root = Path.GetFullPath(payloadDirectory)
				.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			string full = Path.GetFullPath(filePath);
			if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException("A scripted replay payload escaped its expected native payload directory.");
			return full.Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
		}

		private static CollectionNativeChildExecutionEvidence CaptureExecutionEvidence(ResolvedCollectionMemberPlan member,
			CollectionMemberEffectPreview reviewedPreview, ModInstallationRecipeInput recipeInput, IMod incomingMod,
			string installInfoDirectory, Nexus.Client.Games.IGameMode gameMode)
		{
			if (member == null) throw new ArgumentNullException(nameof(member));
			if (reviewedPreview == null) throw new ArgumentNullException(nameof(reviewedPreview));
			if (recipeInput == null) throw new ArgumentNullException(nameof(recipeInput));
			if (incomingMod == null) throw new ArgumentNullException(nameof(incomingMod));
			if (String.IsNullOrWhiteSpace(installInfoDirectory)) throw new ArgumentException("The InstallInfo directory is required.", nameof(installInfoDirectory));
			if (gameMode == null) throw new ArgumentNullException(nameof(gameMode));

			string domain;
			long modId;
			long fileId;
			if (!NexusCollectionModFileArtifactIdentity.TryParse(member.ArtifactChoice.SelectedArtifact, out domain, out modId, out fileId))
				throw new InvalidDataException("The selected Collection artifact is not an exact Nexus mod-file identity.");

			var preFiles = new List<CollectionNativeFileContentEvidence>();
			foreach (CollectionPlannedFileEffect file in reviewedPreview.Files)
				preFiles.Add(CaptureFileEvidence(file.Target, ModDeploymentTargetResolver.GetPhysicalPath(gameMode, file.Target)));

			var expectedByTarget = new Dictionary<ModDeploymentTarget, CollectionNativeFileContentEvidence>();
			var sourceContents = new Dictionary<string, ContentIdentity>(StringComparer.Ordinal);
			var expectedReplay = new List<CollectionExpectedReplayOperation>();
			foreach (ScriptedInstallOperation operation in recipeInput.NativeOperations)
			{
				InstallModFileOperation install = operation as InstallModFileOperation;
				if (install != null)
				{
					ContentIdentity content;
					if (!sourceContents.TryGetValue(install.SourcePath, out content))
					{
						using (FileStream stream = incomingMod.GetFileStream(install.SourcePath)) content = ContentIdentity.FromStream(stream);
						sourceContents.Add(install.SourcePath, content);
					}
					ModDeploymentTarget target = ModDeploymentTargetResolver.Resolve(gameMode, incomingMod,
						install.DestinationPath, recipeInput.InstallContext.InstallRoot);
					expectedByTarget[target] = content.ToEvidence(target);
					expectedReplay.Add(new CollectionExpectedReplayOperation(ScriptedReplayOperationKind.ArchiveFile,
						install.SourcePath, install.DestinationPath, 0, null));
					continue;
				}

				GenerateDataFileOperation generated = operation as GenerateDataFileOperation;
				if (generated != null)
				{
					if (generated.Data == null) throw new InvalidDataException("A generated-file recipe operation has no payload bytes.");
					ContentIdentity content = ContentIdentity.FromBytes(generated.Data);
					ModDeploymentTarget target = ModDeploymentTargetResolver.Resolve(gameMode, incomingMod,
						generated.DestinationPath, recipeInput.InstallContext.InstallRoot);
					expectedByTarget[target] = content.ToEvidence(target);
					expectedReplay.Add(new CollectionExpectedReplayOperation(ScriptedReplayOperationKind.GeneratedFile,
						null, generated.DestinationPath, content.Length, content.Sha256));
				}
			}

			var expectedFiles = new List<CollectionNativeFileContentEvidence>();
			foreach (CollectionPlannedFileEffect file in reviewedPreview.Files)
			{
				CollectionNativeFileContentEvidence evidence;
				if (!expectedByTarget.TryGetValue(file.Target, out evidence))
					throw new InvalidDataException("The exact C5 recipe does not provide bytes for every reviewed file target.");
				expectedFiles.Add(evidence);
			}
			if (expectedByTarget.Count != expectedFiles.Count)
				throw new InvalidDataException("The exact C5 recipe produces file targets outside the reviewed C6.4 effect set.");

			CollectionReplayContentEvidence replayPreimage = CaptureReplayContentEvidence(incomingMod.Filename, installInfoDirectory);
			return new CollectionNativeChildExecutionEvidence(domain, modId, fileId, incomingMod.Filename,
				reviewedPreview, preFiles, expectedFiles, replayPreimage, expectedReplay);
		}

		private static CollectionNativeFileContentEvidence CaptureFileEvidence(ModDeploymentTarget target, string physicalPath)
		{
			if (String.IsNullOrWhiteSpace(physicalPath) || !File.Exists(physicalPath))
				return new CollectionNativeFileContentEvidence(target, false, null, 0);
			var info = new FileInfo(physicalPath);
			return new CollectionNativeFileContentEvidence(target, true,
				CollectionContentHash.FromSha256(ComputeSha256(physicalPath)), info.Length);
		}

		private static CollectionReplayContentEvidence CaptureReplayContentEvidence(string incomingFileName, string installInfoDirectory)
		{
			string replayPath = ScriptedFileSelectionCache.GetDefaultFilePath(incomingFileName, installInfoDirectory);
			bool replayExists = File.Exists(replayPath);
			CollectionContentHash replayHash = replayExists ? CollectionContentHash.FromSha256(ComputeSha256(replayPath)) : null;
			long replayLength = replayExists ? new FileInfo(replayPath).Length : 0;
			string payloadDirectory = ScriptedFileSelectionCache.GetPayloadDirectoryPath(replayPath);
			bool payloadDirectoryExists = Directory.Exists(payloadDirectory);
			var payloads = new List<CollectionReplayPayloadContentEvidence>();
			if (payloadDirectoryExists)
			{
				foreach (string file in Directory.GetFiles(payloadDirectory, "*", SearchOption.AllDirectories))
				{
					var info = new FileInfo(file);
					payloads.Add(new CollectionReplayPayloadContentEvidence(GetRelativeReplayPayloadPath(payloadDirectory, file),
						CollectionContentHash.FromSha256(ComputeSha256(file)), info.Length));
				}
			}
			return new CollectionReplayContentEvidence(replayExists, replayHash, replayLength, payloadDirectoryExists, payloads);
		}

		private sealed class ContentIdentity
		{
			private ContentIdentity(long length, string sha256) { Length = length; Sha256 = sha256; }
			public long Length { get; }
			public string Sha256 { get; }
			public CollectionNativeFileContentEvidence ToEvidence(ModDeploymentTarget target)
			{ return new CollectionNativeFileContentEvidence(target, true, CollectionContentHash.FromSha256(Sha256), Length); }
			public static ContentIdentity FromBytes(byte[] bytes)
			{
				using (var stream = new MemoryStream(bytes, false)) return FromStream(stream);
			}
			public static ContentIdentity FromStream(Stream stream)
			{
				long start = stream.CanSeek ? stream.Position : 0;
				using (SHA256 sha = SHA256.Create())
				{
					byte[] hash = sha.ComputeHash(stream);
					long length = stream.CanSeek ? stream.Position - start : -1;
					if (length < 0) throw new InvalidDataException("The recipe source stream length could not be determined.");
					return new ContentIdentity(length, BitConverter.ToString(hash).Replace("-", String.Empty).ToLowerInvariant());
				}
			}
		}

		private static bool ExecutionEvidenceEquals(CollectionNativeChildExecutionEvidence left, CollectionNativeChildExecutionEvidence right)
		{
			if (left == null || right == null ||
				!StringComparer.OrdinalIgnoreCase.Equals(left.NexusGameDomain, right.NexusGameDomain) ||
				left.NexusModId != right.NexusModId || left.NexusFileId != right.NexusFileId ||
				!StringComparer.OrdinalIgnoreCase.Equals(left.IncomingFileName, right.IncomingFileName) ||
				!EffectPreviewsEqual(left.ReviewedEffects, right.ReviewedEffects) ||
				!FileEvidenceEquals(left.PreFileContents, right.PreFileContents) ||
				!FileEvidenceEquals(left.ExpectedFileContents, right.ExpectedFileContents) ||
				!ReplayEvidenceEquals(left.IncomingReplayPreimage, right.IncomingReplayPreimage) ||
				left.ExpectedReplayOperations.Count != right.ExpectedReplayOperations.Count)
				return false;

			for (int index = 0; index < left.ExpectedReplayOperations.Count; index++)
			{
				CollectionExpectedReplayOperation a = left.ExpectedReplayOperations[index];
				CollectionExpectedReplayOperation b = right.ExpectedReplayOperations[index];
				if (a.Kind != b.Kind || !StringComparer.Ordinal.Equals(a.SourcePath, b.SourcePath) ||
					!StringComparer.Ordinal.Equals(a.DestinationPath, b.DestinationPath) || a.PayloadLength != b.PayloadLength ||
					!StringComparer.OrdinalIgnoreCase.Equals(a.PayloadSha256, b.PayloadSha256)) return false;
			}
			return true;
		}

		private static bool FileEvidenceEquals(IEnumerable<CollectionNativeFileContentEvidence> left,
			IEnumerable<CollectionNativeFileContentEvidence> right)
		{
			List<CollectionNativeFileContentEvidence> a = left.ToList();
			List<CollectionNativeFileContentEvidence> b = right.ToList();
			if (a.Count != b.Count) return false;
			for (int index = 0; index < a.Count; index++)
			{
				if (!a[index].Target.Equals(b[index].Target) || a[index].Existed != b[index].Existed ||
					a[index].ByteLength != b[index].ByteLength ||
					!StringComparer.OrdinalIgnoreCase.Equals(a[index].ContentHash == null ? null : a[index].ContentHash.Value,
						b[index].ContentHash == null ? null : b[index].ContentHash.Value)) return false;
			}
			return true;
		}

		private static bool ReplayEvidenceEquals(CollectionReplayContentEvidence left, CollectionReplayContentEvidence right)
		{
			if (left == null || right == null || left.ReplayFileExisted != right.ReplayFileExisted ||
				left.ReplayFileLength != right.ReplayFileLength || left.PayloadDirectoryExisted != right.PayloadDirectoryExisted ||
				!StringComparer.OrdinalIgnoreCase.Equals(left.ReplayFileHash == null ? null : left.ReplayFileHash.Value,
					right.ReplayFileHash == null ? null : right.ReplayFileHash.Value) || left.Payloads.Count != right.Payloads.Count) return false;
			for (int index = 0; index < left.Payloads.Count; index++)
			{
				CollectionReplayPayloadContentEvidence a = left.Payloads[index];
				CollectionReplayPayloadContentEvidence b = right.Payloads[index];
				if (!StringComparer.OrdinalIgnoreCase.Equals(a.RelativePath, b.RelativePath) || a.ByteLength != b.ByteLength ||
					!StringComparer.OrdinalIgnoreCase.Equals(a.ContentHash.Value, b.ContentHash.Value)) return false;
			}
			return true;
		}

		private static IBackgroundTaskSet CreateNativeTask(IMod previousMod, IMod incomingMod,
			ModInstallationRecipeInput recipeInput, ModManager modManager)
		{
			ConfirmModUpgradeDelegate rejectUnexpectedUpgrade = (oldMod, newMod) => ConfirmUpgradeResult.Cancel;
			ConfirmItemOverwriteDelegate rejectUnexpectedOverwrite = (message, allowPerGroup, allowPerMod) =>
			{
				throw new InvalidOperationException("C6.7 encountered an unexpected native overwrite prompt after exact conflict/impact review.");
			};
			ModInstallContext context = recipeInput.InstallContext;

			if (previousMod == null)
			{
				IBackgroundTaskSet activation = modManager.ActivateMod(incomingMod, rejectUnexpectedUpgrade,
					rejectUnexpectedOverwrite, modManager.ActiveMods, context, true, recipeInput);
				if (activation == null)
					throw new InvalidOperationException("The exact incoming mod became active before C6.7 submission; revalidation is required.");
				return activation;
			}

			if (ReferenceEquals(previousMod, incomingMod) ||
				ModFileIdentity.IsSameRepositoryFile(previousMod.Id, previousMod.DownloadId, incomingMod.Id, incomingMod.DownloadId))
			{
				return modManager.ReinstallMod(previousMod, rejectUnexpectedUpgrade, rejectUnexpectedOverwrite,
					modManager.ActiveMods, context, recipeInput);
			}

			return modManager.CreateUpgradeModOperation(previousMod, incomingMod, rejectUnexpectedOverwrite, context, recipeInput);
		}

		private static bool EffectPreviewsEqual(CollectionMemberEffectPreview left, CollectionMemberEffectPreview right)
		{
			if (left == null || right == null || left.IsComplete != right.IsComplete || !left.MemberKey.Equals(right.MemberKey) ||
				!left.RecipeIdentity.Equals(right.RecipeIdentity) || left.InstallMethod != right.InstallMethod || left.InstallRoot != right.InstallRoot)
				return false;

			return CanonicalFiles(left).SequenceEqual(CanonicalFiles(right), StringComparer.Ordinal) &&
				CanonicalIni(left).SequenceEqual(CanonicalIni(right), StringComparer.Ordinal) &&
				CanonicalGameValues(left).SequenceEqual(CanonicalGameValues(right), StringComparer.Ordinal) &&
				CanonicalPlugins(left).SequenceEqual(CanonicalPlugins(right), StringComparer.Ordinal);
		}

		private static IEnumerable<string> CanonicalFiles(CollectionMemberEffectPreview preview)
		{
			return preview.Files.Select(x => ((int)x.Target.Root).ToString(CultureInfo.InvariantCulture) + "|" +
				x.Target.RelativePath.ToUpperInvariant()).OrderBy(x => x, StringComparer.Ordinal);
		}

		private static IEnumerable<string> CanonicalIni(CollectionMemberEffectPreview preview)
		{
			return preview.IniEdits.Select(x => x.Key.File.ToUpperInvariant() + "|" + x.Key.Section.ToUpperInvariant() + "|" +
				x.Key.Key.ToUpperInvariant() + "|" + (x.Value == null ? "<null>" : "<value>" + x.Value)).OrderBy(x => x, StringComparer.Ordinal);
		}

		private static IEnumerable<string> CanonicalGameValues(CollectionMemberEffectPreview preview)
		{
			return preview.GameValues.Select(x => x.Key + "|" + (x.UnsafeValue == null ? "<null>" : Convert.ToBase64String(x.UnsafeValue)))
				.OrderBy(x => x, StringComparer.Ordinal);
		}

		private static IEnumerable<string> CanonicalPlugins(CollectionMemberEffectPreview preview)
		{
			return preview.PluginEffects.Select(x => ((int)x.Kind).ToString(CultureInfo.InvariantCulture) + "|" +
				String.Join(";", x.PluginPaths.Select(y => y.ToUpperInvariant())) + "|" +
				(x.Active.HasValue ? (x.Active.Value ? "1" : "0") : String.Empty) + "|" +
				(x.AbsoluteIndex.HasValue ? x.AbsoluteIndex.Value.ToString(CultureInfo.InvariantCulture) : String.Empty))
				.OrderBy(x => x, StringComparer.Ordinal);
		}

		private CollectionOperation SaveChild(CollectionOperation operation, CollectionNativeChildOperation child)
		{
			var children = operation.NativeChildren.Select(x => x.Sequence == child.Sequence ? child : x).ToList();
			var updated = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
				operation.Revision, operation.PlanIdentity, checked(operation.CheckpointSequence + 1), operation.Phase,
				operation.ResultState, children);
			_operationStore.SaveOperation(updated);
			CollectionOperation persisted = _operationStore.GetOperation(updated.Identity);
			if (persisted == null)
				throw new InvalidDataException("The Collection operation disappeared after persisting the NativeSubmitted checkpoint.");
			return persisted;
		}

		private static bool SameNativeMod(CollectionNativeModState left, CollectionNativeModState right)
		{
			return right != null && left.Identity.Equals(right.Identity) &&
				StringComparer.OrdinalIgnoreCase.Equals(left.ArchivePath, right.ArchivePath) &&
				StringComparer.OrdinalIgnoreCase.Equals(left.FileName, right.FileName) &&
				StringComparer.Ordinal.Equals(left.NexusModId, right.NexusModId) &&
				StringComparer.Ordinal.Equals(left.NexusFileId, right.NexusFileId) &&
				StringComparer.Ordinal.Equals(left.HumanReadableVersion, right.HumanReadableVersion) &&
				StringComparer.Ordinal.Equals(left.MachineVersion, right.MachineVersion) &&
				left.HasInstallScript == right.HasInstallScript && left.InstallRoot == right.InstallRoot &&
				left.InstallMethod == right.InstallMethod;
		}

		private static bool SamePhysicalPath(string left, string right)
		{
			if (String.IsNullOrWhiteSpace(left) || String.IsNullOrWhiteSpace(right)) return false;
			try { return StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(left), Path.GetFullPath(right)); }
			catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) { return false; }
		}

		private static string ComputeSha256(string path)
		{
			using (SHA256 sha = SHA256.Create())
			using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
				return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", String.Empty).ToLowerInvariant();
		}

		private static bool Matches(ModOperationIdentity left, ModOperationIdentity right)
		{
			return left != null && right != null && left.OperationId == right.OperationId && left.AttemptId == right.AttemptId &&
				left.Origin == right.Origin && left.Fingerprint.Equals(right.Fingerprint);
		}
	}

	/// <summary>
	/// Live C6.7 native-submission result retaining the canonical target mutation reservation for C6.8 verification.
	/// </summary>
	/// <remarks>The reservation intentionally has no public dispose path; C6.8 releases it only after authoritative reconciliation.</remarks>
	public sealed class CollectionNativeChildExecutionResult
	{
		private CollectionTargetMutationLease _mutationLease;

		internal CollectionNativeChildExecutionResult(CollectionOperation operation, CollectionNativeChildOperation child,
			IBackgroundTaskSet nativeTask, ModInstallationRecipeInput recipeInput, IMod incomingMod, CollectionTargetMutationLease mutationLease)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Child = child ?? throw new ArgumentNullException(nameof(child));
			NativeTask = nativeTask ?? throw new ArgumentNullException(nameof(nativeTask));
			RecipeInput = recipeInput ?? throw new ArgumentNullException(nameof(recipeInput));
			IncomingMod = incomingMod ?? throw new ArgumentNullException(nameof(incomingMod));
			_mutationLease = mutationLease ?? throw new ArgumentNullException(nameof(mutationLease));
			if (child.Checkpoint != CollectionNativeChildCheckpoint.NativeSubmitted)
				throw new ArgumentException("A C6.7 execution result requires the durable NativeSubmitted checkpoint.", nameof(child));
		}

		public CollectionOperation Operation { get; }
		public CollectionNativeChildOperation Child { get; }
		public IBackgroundTaskSet NativeTask { get; }
		internal ModInstallationRecipeInput RecipeInput { get; }
		internal IMod IncomingMod { get; }
		public bool IsDisposed { get { return _mutationLease == null || _mutationLease.IsDisposed; } }

		/// <summary>Gets the live root target reservation retained exclusively for C6.8 authoritative verification.</summary>
		internal CollectionTargetMutationLease MutationLease
		{
			get
			{
				if (_mutationLease == null || _mutationLease.IsDisposed)
					throw new ObjectDisposedException(nameof(CollectionNativeChildExecutionResult), "The C6.7 target reservation has already been released.");
				return _mutationLease;
			}
		}

		/// <summary>Releases the C4 target reservation only after C6.8 has completed authoritative verification.</summary>
		internal void ReleaseAfterVerification()
		{
			if (_mutationLease != null && !NativeTask.IsCompleted)
				throw new InvalidOperationException("The target mutation reservation cannot be released while the native child is still running.");
			CollectionTargetMutationLease lease = _mutationLease;
			_mutationLease = null;
			if (lease != null) lease.Dispose();
		}
	}
}
