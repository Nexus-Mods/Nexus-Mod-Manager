using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.Mods;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Observes one submitted C6.7 child to native termination, reloads authoritative native state and checkpoints the independently verified durability.
	/// </summary>
	/// <remarks>
	/// C6.8 stops at <see cref="CollectionNativeChildCheckpoint.NativeTerminalObserved"/>. Collection bindings/associations are not written here;
	/// C6.10 owns provenance reconciliation and the later Reconciled checkpoint.
	/// </remarks>
	public sealed class CollectionNativeChildVerificationCoordinator
	{
		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsResolvedPlanStore _planStore;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionsNativeChildRecoveryManifestStore _manifestStore;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;

		/// <summary>Creates a production C6.8 verifier over the established native and Collections services.</summary>
		public CollectionNativeChildVerificationCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore,
			CollectionsAssociationStore associationStore, CollectionsNativeChildRecoveryManifestStore manifestStore)
			: this(services, gameStorageService, operationStore, planStore, associationStore, manifestStore,
				new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services))
		{
		}

		/// <summary>Creates a C6.8 verifier over explicit authority-validation services.</summary>
		internal CollectionNativeChildVerificationCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore,
			CollectionsAssociationStore associationStore, CollectionsNativeChildRecoveryManifestStore manifestStore,
			CollectionTargetOwnershipAuthorityValidator authorityValidator)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_planStore = planStore ?? throw new ArgumentNullException(nameof(planStore));
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
			_manifestStore = manifestStore ?? throw new ArgumentNullException(nameof(manifestStore));
			_authorityValidator = authorityValidator ?? throw new ArgumentNullException(nameof(authorityValidator));
		}

		/// <summary>
		/// Waits for the exact submitted native attempt, persists its terminal report, reloads authoritative state and records the independently verified durability.
		/// </summary>
		/// <remarks>
		/// Cancellation is intentionally not accepted after C6.7 crossed the native boundary. Once native submission occurred, this method must reconcile what actually
		/// happened before releasing the target reservation; caller cancellation cannot safely imply rollback or non-execution.
		/// </remarks>
		public async Task<CollectionNativeChildVerificationResult> VerifySubmittedChildAsync(
			CollectionNativeChildExecutionResult executionResult, ResolvedCollectionPlan plan,
			CollectionMemberEffectPreview reviewedPreview, GameStoragePathSet paths)
		{
			if (executionResult == null) throw new ArgumentNullException(nameof(executionResult));
			if (plan == null) throw new ArgumentNullException(nameof(plan));
			if (reviewedPreview == null) throw new ArgumentNullException(nameof(reviewedPreview));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			if (executionResult.IsDisposed)
				throw new ObjectDisposedException(nameof(executionResult), "The C6.7 target reservation was already released; use C6.9 reconciliation instead.");
			if (_services.ModManager == null)
				throw new InvalidOperationException("C6.8 requires the live ModManager service.");

			bool nativeCompleted = executionResult.NativeTask.IsCompleted;
			try
			{
				CollectionOperation operation = RequireOperation(executionResult, plan);
				CollectionNativeChildOperation child = RequireSubmittedChild(operation, executionResult);
				ResolvedCollectionMemberPlan member = RequireMember(plan, child);
				ValidateReviewedPreview(member, reviewedPreview);
				ValidateExecutionRecipe(child, reviewedPreview, executionResult.RecipeInput);
				CollectionNativeChildRecoveryManifest recovery = RequireRecoveryManifest(operation, child, plan);

				await WaitForCompletionAsync(executionResult.NativeTask).ConfigureAwait(true);
				nativeCompleted = true;

				bool exactReportedIdentity;
				ModOperationResult reportedResult = CaptureReportedResult(executionResult.NativeTask, child.NativeOperation, out exactReportedIdentity);
				ModOperationResult pendingVerification = new ModOperationResult(child.NativeOperation,
					reportedResult.ReportedStatus, ModOperationDurability.Unknown, reportedResult.Message);
				child = new CollectionNativeChildOperation(child.Sequence, child.Member, child.Action, child.NativeOperation,
					CollectionNativeChildCheckpoint.NativeTerminalObserved, pendingVerification);
				operation = SaveChild(operation, child,
					"The Collection operation disappeared after persisting the native terminal report.");

				// The terminal report is now durable. Reload native authority while the C6.7 root reservation is still held;
				// deployment/VMA recovery may run here, and that recovered reality is what C6.8 verifies.
				CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
				if (!authority.Target.Equals(plan.Target))
					throw new InvalidOperationException("The live canonical game/storage target changed before C6.8 verification.");
				_authorityValidator.ValidateAndReload(executionResult.MutationLease, authority, paths);

				operation = RequireOperation(executionResult, plan);
				child = RequireTerminalChild(operation, executionResult);
				recovery = RequireRecoveryManifest(operation, child, plan);
				CollectionNativeStateIndex state = CaptureReloadedState(plan.Target);

				CollectionNativeModState verifiedNativeMod = null;
				bool committed = exactReportedIdentity && TryVerifyCommittedState(member, reviewedPreview, recovery, state,
					executionResult.RecipeInput, executionResult.IncomingMod, _services.ModManager.GameMode,
					_services.ModManager.ModRepository == null ? null : _services.ModManager.ModRepository.GameDomainName, out verifiedNativeMod);
				bool rolledBack = exactReportedIdentity && TryVerifyRolledBackState(reportedResult, reviewedPreview, recovery, state, paths.InstallInfoPath);
				ModOperationDurability verifiedDurability = exactReportedIdentity
					? DetermineVerifiedDurability(reportedResult, committed, rolledBack)
					: ModOperationDurability.Unknown;
				if (verifiedDurability == ModOperationDurability.VerifiedCommitted)
				{
					if (recovery.TerminalStateFingerprint == null)
					{
						_manifestStore.SaveManifest(recovery.WithTerminalStateFingerprint(state.Fingerprint));
						recovery = RequireRecoveryManifest(operation, child, plan);
					}
					if (!state.Fingerprint.Equals(recovery.TerminalStateFingerprint))
						throw new InvalidDataException("The retained committed terminal-state fingerprint does not match authoritative C6.8 native state.");
				}

				var verifiedResult = new ModOperationResult(child.NativeOperation, reportedResult.ReportedStatus,
					verifiedDurability, reportedResult.Message);
				child = new CollectionNativeChildOperation(child.Sequence, child.Member, child.Action, child.NativeOperation,
					CollectionNativeChildCheckpoint.NativeTerminalObserved, verifiedResult);
				operation = SaveChild(operation, child,
					"The Collection operation disappeared after persisting the independently verified native durability.");

				return new CollectionNativeChildVerificationResult(operation, child, state, verifiedNativeMod,
					reportedResult.Durability);
			}
			catch
			{
				// Once C6.7 returned a submitted child this verifier owns the live reservation. Even an input/store/reload
				// failure must not leak that reservation while the native worker is still running. Leave the durable child at
				// NativeSubmitted/NativeTerminalObserved and let C6.9 reconcile the released target afterward.
				if (!nativeCompleted)
				{
					await WaitForCompletionAsync(executionResult.NativeTask).ConfigureAwait(true);
					nativeCompleted = true;
				}
				throw;
			}
			finally
			{
				if (nativeCompleted && !executionResult.IsDisposed)
					executionResult.ReleaseAfterVerification();
			}
		}

		private CollectionOperation RequireOperation(CollectionNativeChildExecutionResult executionResult, ResolvedCollectionPlan plan)
		{
			CollectionOperation operation = _operationStore.GetOperation(executionResult.Operation.Identity);
			if (operation == null)
				throw new InvalidOperationException("The Collection operation is not present in the durable operation journal.");
			if (operation.Kind != CollectionOperationKind.ApplyResolvedPlan ||
				operation.Phase != CollectionOperationPhase.ApplyingNativeChildren ||
				operation.ResultState != CollectionOperationResultState.Pending)
			{
				throw new InvalidOperationException("C6.8 requires an active additive operation in ApplyingNativeChildren.");
			}
			if (operation.PlanIdentity == null || !operation.PlanIdentity.Equals(plan.Identity) ||
				operation.Revision == null || !operation.Revision.Equals(plan.Revision) || !operation.Target.Equals(plan.Target))
			{
				throw new ArgumentException("The Collection verification inputs do not belong to the operation's exact approved plan.", nameof(plan));
			}
			if (plan.Policy.Kind != CollectionExecutionPolicyKind.InstallIntoCurrentSetup)
				throw new ArgumentException("C6.8 only verifies additive Collection execution.", nameof(plan));

			CollectionResolvedPlanRecord persisted = _planStore.GetPlan(plan.Identity);
			if (persisted == null || !persisted.Revision.Equals(plan.Revision) || !persisted.Target.Equals(plan.Target) ||
				persisted.PolicyKind != plan.Policy.Kind)
			{
				throw new InvalidOperationException("The exact approved Collection plan is not durably persisted for C6.8 verification.");
			}
			return operation;
		}

		private static CollectionNativeChildOperation RequireSubmittedChild(CollectionOperation operation,
			CollectionNativeChildExecutionResult executionResult)
		{
			List<CollectionNativeChildOperation> pending = operation.NativeChildren.Where(x => !x.IsReconciled).ToList();
			if (pending.Count != 1)
				throw new InvalidOperationException("C6.8 requires exactly one serialized unreconciled native child.");
			CollectionNativeChildOperation child = pending[0];
			if (child.Checkpoint != CollectionNativeChildCheckpoint.NativeSubmitted ||
				!Matches(child.NativeOperation, executionResult.Child.NativeOperation) || child.Sequence != executionResult.Child.Sequence)
			{
				throw new InvalidOperationException("The durable Collection child is not the exact C6.7 NativeSubmitted attempt.");
			}
			return child;
		}

		private static CollectionNativeChildOperation RequireTerminalChild(CollectionOperation operation,
			CollectionNativeChildExecutionResult executionResult)
		{
			List<CollectionNativeChildOperation> pending = operation.NativeChildren.Where(x => !x.IsReconciled).ToList();
			if (pending.Count != 1)
				throw new InvalidOperationException("C6.8 requires exactly one serialized unreconciled native child.");
			CollectionNativeChildOperation child = pending[0];
			if (child.Checkpoint != CollectionNativeChildCheckpoint.NativeTerminalObserved || child.NativeResult == null ||
				!Matches(child.NativeOperation, executionResult.Child.NativeOperation) || child.Sequence != executionResult.Child.Sequence)
			{
				throw new InvalidOperationException("The durable Collection child is not the exact terminal C6.7 native attempt.");
			}
			return child;
		}

		private CollectionNativeChildRecoveryManifest RequireRecoveryManifest(CollectionOperation operation,
			CollectionNativeChildOperation child, ResolvedCollectionPlan plan)
		{
			CollectionNativeChildRecoveryManifest recovery = _manifestStore.GetManifest(operation, child);
			if (recovery == null)
				throw new InvalidDataException("The submitted Collection child is missing its retained C6.6 recovery manifest.");
			if (!recovery.PlanIdentity.Equals(plan.Identity) || !recovery.Member.MemberKey.Equals(child.Member.MemberKey) ||
				recovery.ChildSequence != child.Sequence || !Matches(recovery.NativeOperation, child.NativeOperation))
			{
				throw new InvalidDataException("The retained C6.6 recovery manifest does not match the exact C6.8 native child.");
			}
			return recovery;
		}

		private static ResolvedCollectionMemberPlan RequireMember(ResolvedCollectionPlan plan, CollectionNativeChildOperation child)
		{
			List<ResolvedCollectionMemberPlan> matches = plan.SelectedMembers.Where(x => x.MemberKey.Equals(child.Member.MemberKey)).ToList();
			if (matches.Count != 1)
				throw new InvalidOperationException("The submitted native child does not identify exactly one member in the approved Collection plan.");
			return matches[0];
		}

		private static void ValidateReviewedPreview(ResolvedCollectionMemberPlan member, CollectionMemberEffectPreview preview)
		{
			if (!preview.IsComplete || !preview.MemberKey.Equals(member.MemberKey) || !preview.RecipeIdentity.Equals(member.RecipeIdentity))
				throw new ArgumentException("The supplied effect preview is not the complete reviewed C6.4 preview for this member.", nameof(preview));
		}

		private static void ValidateExecutionRecipe(CollectionNativeChildOperation child, CollectionMemberEffectPreview preview,
			ModInstallationRecipeInput recipeInput)
		{
			if (recipeInput == null || !recipeInput.HasNativePlan || recipeInput.NativeOperations == null || recipeInput.NativeOperations.Count == 0 ||
				!Matches(child.NativeOperation, recipeInput.OperationIdentity) ||
				!StringComparer.Ordinal.Equals(preview.RecipeIdentity.Fingerprint, recipeInput.RecipeFingerprint) ||
				recipeInput.InstallContext.Method != preview.InstallMethod || recipeInput.InstallContext.InstallRoot != preview.InstallRoot)
			{
				throw new InvalidDataException("The C6.7 execution result no longer carries the exact translated C5 recipe submitted for this child.");
			}
		}

		private CollectionNativeStateIndex CaptureReloadedState(CollectionTargetIdentity target)
		{
			ModManager modManager = _services.ModManager;
			return new CollectionNativeStateReader(modManager.InstallationLog, modManager.VirtualModActivator,
				_services.PluginManager, modManager.GameMode, _associationStore).Capture(target);
		}

		private CollectionOperation SaveChild(CollectionOperation operation, CollectionNativeChildOperation child, string missingMessage)
		{
			var children = operation.NativeChildren.Select(x => x.Sequence == child.Sequence ? child : x).ToList();
			var updated = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
				operation.Revision, operation.PlanIdentity, checked(operation.CheckpointSequence + 1), operation.Phase,
				operation.ResultState, children);
			_operationStore.SaveOperation(updated);
			CollectionOperation persisted = _operationStore.GetOperation(updated.Identity);
			if (persisted == null)
				throw new InvalidDataException(missingMessage);
			return persisted;
		}

		private static Task WaitForCompletionAsync(IBackgroundTaskSet task)
		{
			if (task == null) throw new ArgumentNullException(nameof(task));
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

		private static ModOperationResult CaptureReportedResult(IBackgroundTaskSet task, ModOperationIdentity expectedIdentity,
			out bool exactIdentity)
		{
			exactIdentity = false;
			ModInstallerBase nativeTask = task as ModInstallerBase;
			if (nativeTask == null || nativeTask.OperationResult == null)
			{
				return new ModOperationResult(expectedIdentity, ModOperationReportedStatus.Failed,
					ModOperationDurability.Unknown, "The native task reached terminal state without publishing its identified operation result.");
			}

			ModOperationResult result = nativeTask.OperationResult;
			if (!Matches(expectedIdentity, result.Identity))
			{
				return new ModOperationResult(expectedIdentity, ModOperationReportedStatus.Failed,
					ModOperationDurability.Unknown, "The native task published a terminal result for a different operation attempt.");
			}
			exactIdentity = true;
			return result;
		}

		private static ModOperationDurability DetermineVerifiedDurability(ModOperationResult reportedResult,
			bool committedStateVerified, bool rolledBackStateVerified)
		{
			if (reportedResult == null) throw new ArgumentNullException(nameof(reportedResult));
			if (committedStateVerified)
				return ModOperationDurability.VerifiedCommitted;
			if (reportedResult.Durability == ModOperationDurability.VerifiedCommitted)
				return ModOperationDurability.Unknown;
			if (!rolledBackStateVerified)
				return ModOperationDurability.Unknown;
			return reportedResult.Durability == ModOperationDurability.NotStarted
				? ModOperationDurability.NotStarted
				: ModOperationDurability.VerifiedRolledBack;
		}

		private static bool TryVerifyCommittedState(ResolvedCollectionMemberPlan member, CollectionMemberEffectPreview preview,
			CollectionNativeChildRecoveryManifest recovery, CollectionNativeStateIndex state, ModInstallationRecipeInput recipeInput,
			IMod incomingMod, IGameMode gameMode, string currentDomain, out CollectionNativeModState nativeMod)
		{
			nativeMod = null;
			if (member == null || preview == null || recovery == null || state == null || recipeInput == null || incomingMod == null || gameMode == null)
				return false;

			string expectedDomain;
			long expectedModId;
			long expectedFileId;
			if (!NexusCollectionModFileArtifactIdentity.TryParse(member.ArtifactChoice.SelectedArtifact,
				out expectedDomain, out expectedModId, out expectedFileId) || String.IsNullOrWhiteSpace(expectedDomain) ||
				!StringComparer.OrdinalIgnoreCase.Equals(currentDomain, expectedDomain))
				return false;

			string modId = expectedModId.ToString(CultureInfo.InvariantCulture);
			string fileId = expectedFileId.ToString(CultureInfo.InvariantCulture);
			List<CollectionNativeModState> candidates = state.Mods.Values.Where(x =>
				StringComparer.Ordinal.Equals(x.NexusModId, modId) &&
				StringComparer.Ordinal.Equals(x.NexusFileId, fileId) &&
				x.InstallMethod == preview.InstallMethod && x.InstallRoot == preview.InstallRoot &&
				MatchesArchive(x.ArchivePath, recovery.IncomingArchive)).ToList();
			if (candidates.Count != 1)
				return false;

			nativeMod = candidates[0];
			if (!MatchesArchive(incomingMod.Filename, recovery.IncomingArchive) ||
				!VerifyMemberEffects(state, nativeMod, preview) ||
				!VerifyExpectedFileContents(state, preview, recipeInput, incomingMod, gameMode) ||
				!VerifyLiveReplayAgainstRecipe(recipeInput, incomingMod, gameMode))
			{
				nativeMod = null;
				return false;
			}
			return true;
		}

		private static bool TryVerifyRolledBackState(ModOperationResult reportedResult, CollectionMemberEffectPreview preview,
			CollectionNativeChildRecoveryManifest recovery, CollectionNativeStateIndex state, string installInfoDirectory)
		{
			if (reportedResult == null || preview == null || recovery == null || state == null || String.IsNullOrWhiteSpace(installInfoDirectory))
				return false;
			if (!state.Fingerprint.Equals(recovery.PreparationStateFingerprint))
				return false;

			// Native NotStarted is the only case where the worker itself proves that replay/file mutation was never entered.
			if (reportedResult.Durability == ModOperationDurability.NotStarted)
				return true;

			// A native transaction that reports its registration boundary committed but fails exact recipe verification is ambiguous,
			// even if the metadata fingerprint happens to match the pre-state (for example, same-key reinstall with changed bytes).
			if (reportedResult.Durability == ModOperationDurability.VerifiedCommitted)
				return false;

			// C6.6 does not retain per-target physical preimage hashes. Do not claim a file-touching rollback merely because
			// ownership metadata returned to the same fingerprint; C6.9/native recovery must resolve that case.
			if (preview.Files.Count != 0)
				return false;

			// A failed new activation has no prior live replay/archive snapshot to prove after mutation began. Stay conservative.
			if (recovery.PreviousNativeMod == null || recovery.PreviousArchive == null)
				return false;

			return VerifyPreviousNativeMod(recovery, state) && VerifyReplayPreimage(recovery, installInfoDirectory);
		}

		private static bool VerifyPreviousNativeMod(CollectionNativeChildRecoveryManifest recovery, CollectionNativeStateIndex state)
		{
			CollectionNativeModState previous = recovery.PreviousNativeMod;
			if (previous == null || recovery.PreviousArchive == null)
				return false;
			CollectionNativeModState current;
			if (!state.ModsByNativeKey.TryGetValue(previous.Identity.NativeModKey, out current))
				return false;
			return current.InstallMethod == previous.InstallMethod && current.InstallRoot == previous.InstallRoot &&
				StringComparer.Ordinal.Equals(current.NexusModId, previous.NexusModId) &&
				StringComparer.Ordinal.Equals(current.NexusFileId, previous.NexusFileId) &&
				MatchesArchive(current.ArchivePath, recovery.PreviousArchive);
		}

		private static bool VerifyReplayPreimage(CollectionNativeChildRecoveryManifest recovery, string installInfoDirectory)
		{
			try
			{
				string replayPath = ScriptedFileSelectionCache.GetDefaultFilePath(recovery.PreviousNativeMod.FileName, installInfoDirectory);
				CollectionScriptedReplayRecoverySnapshot expected = recovery.ScriptedReplay;
				if (File.Exists(replayPath) != expected.ReplayFileExisted)
					return false;
				if (expected.ReplayFileExisted && !MatchesFile(replayPath, expected.ReplayFile))
					return false;

				string payloadDirectory = ScriptedFileSelectionCache.GetPayloadDirectoryPath(replayPath);
				if (Directory.Exists(payloadDirectory) != expected.PayloadDirectoryExisted)
					return false;
				if (!expected.PayloadDirectoryExisted)
					return true;

				var expectedByPath = expected.Payloads.ToDictionary(x => x.RelativePath, StringComparer.OrdinalIgnoreCase);
				string[] files = Directory.GetFiles(payloadDirectory, "*", SearchOption.AllDirectories);
				if (files.Length != expectedByPath.Count)
					return false;
				foreach (string file in files)
				{
					string relative = GetCanonicalRelativePath(payloadDirectory, file);
					CollectionReplayRecoveryPayload payload;
					if (!expectedByPath.TryGetValue(relative, out payload) || !MatchesFile(file, payload.Artifact))
						return false;
				}
				return true;
			}
			catch (Exception exception) when (IsVerificationIoException(exception) || exception is ArgumentException)
			{
				return false;
			}
		}

		private static bool VerifyMemberEffects(CollectionNativeStateIndex state, CollectionNativeModState nativeMod,
			CollectionMemberEffectPreview preview)
		{
			if (state == null || nativeMod == null || preview == null || !preview.IsComplete)
				return false;
			string ownerKey = nativeMod.Identity.NativeModKey;

			foreach (CollectionPlannedFileEffect effect in preview.Files)
			{
				CollectionNativeFileState file;
				if (!state.Files.TryGetValue(effect.Target, out file) ||
					!StringComparer.OrdinalIgnoreCase.Equals(file.EffectiveOwnerKey, ownerKey) ||
					String.IsNullOrWhiteSpace(file.PhysicalPath) || !File.Exists(file.PhysicalPath))
					return false;
			}

			foreach (CollectionPlannedIniEffect effect in preview.IniEdits)
			{
				CollectionNativeIniState ini;
				if (!state.IniEdits.TryGetValue(effect.Key, out ini) || ini.Values.Count == 0)
					return false;
				CollectionNativeTextOwnerValue current = ini.Values[ini.Values.Count - 1];
				if (!StringComparer.OrdinalIgnoreCase.Equals(current.OwnerKey, ownerKey) ||
					!StringComparer.Ordinal.Equals(current.Value, effect.Value))
					return false;
			}

			foreach (CollectionPlannedGameValueEffect effect in preview.GameValues)
			{
				CollectionNativeGameValueState value;
				if (!state.GameValues.TryGetValue(effect.Key, out value) || value.Values.Count == 0)
					return false;
				CollectionNativeBinaryOwnerValue current = value.Values[value.Values.Count - 1];
				if (!StringComparer.OrdinalIgnoreCase.Equals(current.OwnerKey, ownerKey) ||
					!ByteArraysEqual(current.UnsafeValue, effect.UnsafeValue))
					return false;
			}

			if (preview.PluginEffects.Count > 0 && state.PluginCoverage != CollectionNativeStateCoverage.Complete)
				return false;
			foreach (CollectionPlannedPluginEffect effect in preview.PluginEffects)
				if (!VerifyPluginEffect(state, effect)) return false;

			return true;
		}

		private static bool VerifyExpectedFileContents(CollectionNativeStateIndex state, CollectionMemberEffectPreview preview,
			ModInstallationRecipeInput recipeInput, IMod incomingMod, IGameMode gameMode)
		{
			try
			{
				var expected = new Dictionary<ModDeploymentTarget, FileContentIdentity>();
				var sourceContents = new Dictionary<string, FileContentIdentity>(StringComparer.OrdinalIgnoreCase);
				foreach (ScriptedInstallOperation operation in recipeInput.NativeOperations)
				{
					InstallModFileOperation install = operation as InstallModFileOperation;
					if (install != null)
					{
						ModDeploymentTarget target = ModDeploymentTargetResolver.Resolve(gameMode, incomingMod,
							install.DestinationPath, recipeInput.InstallContext.InstallRoot);
						string source = install.SourcePath;
						FileContentIdentity content;
						if (!sourceContents.TryGetValue(source, out content))
						{
							using (FileStream stream = incomingMod.GetFileStream(source))
								content = FileContentIdentity.FromStream(stream);
							sourceContents.Add(source, content);
						}
						expected[target] = content;
						continue;
					}

					GenerateDataFileOperation generated = operation as GenerateDataFileOperation;
					if (generated != null)
					{
						if (generated.Data == null) return false;
						ModDeploymentTarget target = ModDeploymentTargetResolver.Resolve(gameMode, incomingMod,
							generated.DestinationPath, recipeInput.InstallContext.InstallRoot);
						expected[target] = FileContentIdentity.FromBytes(generated.Data);
					}
				}

				if (expected.Count != preview.Files.Count)
					return false;
				foreach (CollectionPlannedFileEffect effect in preview.Files)
				{
					FileContentIdentity content;
					CollectionNativeFileState file;
					if (!expected.TryGetValue(effect.Target, out content) || !state.Files.TryGetValue(effect.Target, out file) ||
						String.IsNullOrWhiteSpace(file.PhysicalPath) || !content.Matches(file.PhysicalPath))
						return false;
				}
				return true;
			}
			catch (Exception exception) when (IsVerificationIoException(exception) || exception is ArgumentException || exception is InvalidOperationException)
			{
				return false;
			}
		}

		private static bool VerifyLiveReplayAgainstRecipe(ModInstallationRecipeInput recipeInput, IMod incomingMod, IGameMode gameMode)
		{
			try
			{
				var expected = recipeInput.NativeOperations.Where(x => x is InstallModFileOperation || x is GenerateDataFileOperation).ToList();
				var cache = new ScriptedFileSelectionCache(incomingMod, gameMode);
				string payloadDirectory = ScriptedFileSelectionCache.GetPayloadDirectoryPath(cache.FilePath);
				if (expected.Count == 0)
					return !cache.Exists && !Directory.Exists(payloadDirectory);
				if (!cache.HasCompleteReplay)
					return false;

				IReadOnlyList<ScriptedReplayOperation> actual = cache.LoadReplayOperations();
				if (actual == null || actual.Count != expected.Count)
					return false;

				var referencedPayloads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				for (int index = 0; index < expected.Count; index++)
				{
					InstallModFileOperation install = expected[index] as InstallModFileOperation;
					ScriptedReplayOperation replay = actual[index];
					if (install != null)
					{
						if (replay.Kind != ScriptedReplayOperationKind.ArchiveFile ||
							!StringComparer.Ordinal.Equals(replay.SourcePath, install.SourcePath) ||
							!StringComparer.Ordinal.Equals(replay.DestinationPath, install.DestinationPath))
							return false;
						continue;
					}

					GenerateDataFileOperation generated = expected[index] as GenerateDataFileOperation;
					if (generated == null || generated.Data == null || replay.Kind != ScriptedReplayOperationKind.GeneratedFile ||
						!StringComparer.Ordinal.Equals(replay.DestinationPath, generated.DestinationPath))
						return false;
					FileContentIdentity generatedContent = FileContentIdentity.FromBytes(generated.Data);
					if (replay.PayloadLength != generatedContent.Length ||
						!StringComparer.OrdinalIgnoreCase.Equals(replay.PayloadHash, generatedContent.Sha256) ||
						String.IsNullOrWhiteSpace(replay.PayloadPath))
						return false;
					referencedPayloads.Add(Path.GetFullPath(replay.PayloadPath));
				}

				if (referencedPayloads.Count == 0)
					return !Directory.Exists(payloadDirectory);
				if (!Directory.Exists(payloadDirectory))
					return false;
				string[] livePayloads = Directory.GetFiles(payloadDirectory, "*", SearchOption.AllDirectories)
					.Select(Path.GetFullPath).ToArray();
				return livePayloads.Length == referencedPayloads.Count && livePayloads.All(referencedPayloads.Contains);
			}
			catch (Exception exception) when (IsVerificationIoException(exception) || exception is ArgumentException || exception is InvalidOperationException)
			{
				return false;
			}
		}

		private static bool VerifyPluginEffect(CollectionNativeStateIndex state, CollectionPlannedPluginEffect effect)
		{
			if (effect.Kind == CollectionPlannedPluginEffectKind.Activation)
			{
				CollectionNativePluginState plugin = FindPlugin(state, effect.PluginPaths[0]);
				return plugin != null && plugin.Active == effect.Active.Value;
			}
			if (effect.Kind == CollectionPlannedPluginEffectKind.AbsoluteOrderIndex)
			{
				CollectionNativePluginState plugin = FindPlugin(state, effect.PluginPaths[0]);
				return plugin != null && plugin.Priority == effect.AbsoluteIndex.Value;
			}
			if (effect.Kind == CollectionPlannedPluginEffectKind.RelativeOrder)
			{
				int previous = Int32.MinValue;
				foreach (string path in effect.PluginPaths)
				{
					CollectionNativePluginState plugin = FindPlugin(state, path);
					if (plugin == null || plugin.Priority <= previous) return false;
					previous = plugin.Priority;
				}
				return true;
			}
			return false;
		}

		private static CollectionNativePluginState FindPlugin(CollectionNativeStateIndex state, string path)
		{
			string fileName = Path.GetFileName((path ?? String.Empty).Replace('/', Path.DirectorySeparatorChar));
			if (String.IsNullOrWhiteSpace(fileName)) return null;
			CollectionNativePluginState plugin;
			if (state.Plugins.TryGetValue(fileName, out plugin)) return plugin;
			return state.Plugins.Values.FirstOrDefault(x =>
				StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(x.FileName), fileName));
		}

		private static bool MatchesArchive(string archivePath, CollectionRecoveryArtifact expected)
		{
			return MatchesFile(archivePath, expected);
		}

		private static bool MatchesFile(string path, CollectionRecoveryArtifact expected)
		{
			if (String.IsNullOrWhiteSpace(path) || expected == null ||
				expected.ContentHash.Algorithm != CollectionContentHashAlgorithm.Sha256)
				return false;
			return new FileContentIdentity(expected.ByteLength, expected.ContentHash.Value).Matches(path);
		}

		private static string GetCanonicalRelativePath(string rootDirectory, string filePath)
		{
			string root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			string full = Path.GetFullPath(filePath);
			if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException("A scripted replay payload escaped its expected sidecar directory.");
			return full.Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
		}

		private static bool IsVerificationIoException(Exception exception)
		{
			return exception is IOException || exception is UnauthorizedAccessException || exception is NotSupportedException ||
				exception is System.Security.SecurityException || exception is CryptographicException;
		}

		private sealed class FileContentIdentity
		{
			public FileContentIdentity(long length, string sha256)
			{
				Length = length;
				Sha256 = sha256 ?? String.Empty;
			}

			public long Length { get; }
			public string Sha256 { get; }

			public static FileContentIdentity FromBytes(byte[] bytes)
			{
				if (bytes == null) throw new ArgumentNullException(nameof(bytes));
				using (var stream = new MemoryStream(bytes, false))
					return FromStream(stream);
			}

			public static FileContentIdentity FromStream(Stream stream)
			{
				if (stream == null) throw new ArgumentNullException(nameof(stream));
				long start = stream.CanSeek ? stream.Position : 0;
				using (SHA256 sha = SHA256.Create())
				{
					byte[] hash = sha.ComputeHash(stream);
					long length = stream.CanSeek ? stream.Position - start : -1;
					if (length < 0) throw new InvalidDataException("The expected file stream length could not be determined.");
					return new FileContentIdentity(length, BitConverter.ToString(hash).Replace("-", String.Empty).ToLowerInvariant());
				}
			}

			public bool Matches(string path)
			{
				if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
				try
				{
					var info = new FileInfo(path);
					if (info.Length != Length) return false;
					using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan))
					{
						FileContentIdentity actual = FromStream(stream);
						return actual.Length == Length && StringComparer.OrdinalIgnoreCase.Equals(actual.Sha256, Sha256);
					}
				}
				catch (Exception exception) when (IsVerificationIoException(exception) || exception is ArgumentException)
				{
					return false;
				}
			}
		}

		private static bool ByteArraysEqual(byte[] left, byte[] right)
		{
			if (ReferenceEquals(left, right)) return true;
			if (left == null || right == null || left.Length != right.Length) return false;
			for (int index = 0; index < left.Length; index++) if (left[index] != right[index]) return false;
			return true;
		}

		private static bool Matches(ModOperationIdentity left, ModOperationIdentity right)
		{
			return left != null && right != null && left.OperationId == right.OperationId && left.AttemptId == right.AttemptId &&
				left.Origin == right.Origin && left.Fingerprint.Equals(right.Fingerprint);
		}
	}

	/// <summary>Result of C6.8 authoritative native verification for one submitted Collection child.</summary>
	/// <remarks>The child remains NativeTerminalObserved until C6.10 reconciles Collection provenance/associations.</remarks>
	public sealed class CollectionNativeChildVerificationResult
	{
		/// <summary>Creates one immutable C6.8 verification result from the durable terminal child and reloaded native state.</summary>
		internal CollectionNativeChildVerificationResult(CollectionOperation operation, CollectionNativeChildOperation child,
			CollectionNativeStateIndex nativeState, CollectionNativeModState verifiedNativeMod,
			ModOperationDurability nativeReportedDurability)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Child = child ?? throw new ArgumentNullException(nameof(child));
			NativeState = nativeState ?? throw new ArgumentNullException(nameof(nativeState));
			if (child.Checkpoint != CollectionNativeChildCheckpoint.NativeTerminalObserved || child.NativeResult == null)
				throw new ArgumentException("A C6.8 verification result requires the durable NativeTerminalObserved checkpoint.", nameof(child));
			VerifiedNativeMod = verifiedNativeMod;
			NativeReportedDurability = nativeReportedDurability;
		}

		public CollectionOperation Operation { get; }
		public CollectionNativeChildOperation Child { get; }
		public CollectionNativeStateIndex NativeState { get; }
		public CollectionNativeModState VerifiedNativeMod { get; }
		public ModOperationDurability NativeReportedDurability { get; }
		public ModOperationDurability VerifiedDurability { get { return Child.NativeResult.Durability; } }
		public bool HasVerifiedCommit { get { return VerifiedDurability == ModOperationDurability.VerifiedCommitted; } }
		public bool RequiresRecovery { get { return VerifiedDurability == ModOperationDurability.Unknown; } }
	}
}
