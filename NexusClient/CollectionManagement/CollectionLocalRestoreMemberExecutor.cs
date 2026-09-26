using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Records the C7.10a member-to-native remap established after authoritative native verification.</summary>
	public sealed class CollectionLocalRestoreMemberRemap
	{
		public CollectionLocalRestoreMemberRemap(CollectionMemberKey memberKey, string nativeKey)
		{
			MemberKey = memberKey ?? throw new ArgumentNullException(nameof(memberKey));
			if (String.IsNullOrWhiteSpace(nativeKey))
				throw new ArgumentException("A restored native key is required.", nameof(nativeKey));
			NativeKey = nativeKey;
		}

		public CollectionMemberKey MemberKey { get; }
		public string NativeKey { get; }
	}

	/// <summary>Result of the C7.10a native-member phase; owner/effect reconciliation remains intentionally pending.</summary>
	public sealed class CollectionLocalRestoreMemberExecutionResult
	{
		private readonly ReadOnlyCollection<CollectionLocalRestoreMemberRemap> _memberRemaps;

		internal CollectionLocalRestoreMemberExecutionResult(CollectionOperation operation,
			IEnumerable<CollectionLocalRestoreMemberRemap> memberRemaps)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			_memberRemaps = new ReadOnlyCollection<CollectionLocalRestoreMemberRemap>(
				(memberRemaps ?? throw new ArgumentNullException(nameof(memberRemaps))).ToList());
		}

		public CollectionOperation Operation { get; }
		public ReadOnlyCollection<CollectionLocalRestoreMemberRemap> MemberRemaps { get { return _memberRemaps; } }
		/// <summary>Gets whether C7.10a reached the durable boundary at which C7.10b may reconcile owners/effects.</summary>
		public bool IsReadyForEffectReconciliation
		{
			get { return Operation.Phase == CollectionOperationPhase.PausedAtSafeBoundary && !Operation.HasUnreconciledNativeChild; }
		}
	}

	/// <summary>
	/// C7.10a executor for the native-member portion of a reviewed Local Collection restore.
	/// </summary>
	/// <remarks>
	/// This phase removes current native registrations excluded by C7.9, reuses proven compatible registrations and recreates
	/// missing captured members from retained archives through registration-only native installer recipes. It deliberately stops at a
	/// durable safe boundary before owner-stack/winner and non-file effect reconciliation, which belongs to C7.10b.
	/// </remarks>
	public sealed class CollectionLocalRestoreMemberExecutor
	{
		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsAssociationStore _associationStore;
		private const string RestoreIntentRole = "local-restore-plan-v1";
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionsRetainedArtifactReferenceStore _artifactReferenceStore;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;

		/// <summary>Creates the production C7.10a executor over existing native and Collections services.</summary>
		public CollectionLocalRestoreMemberExecutor(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsAssociationStore associationStore,
			CollectionsRetainedArtifactStore artifactStore, CollectionsRetainedArtifactReferenceStore artifactReferenceStore)
			: this(services, gameStorageService, operationStore, associationStore, artifactStore, artifactReferenceStore,
				CollectionTargetMutationLeaseManager.Shared, new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services))
		{
		}

		internal CollectionLocalRestoreMemberExecutor(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsAssociationStore associationStore,
			CollectionsRetainedArtifactStore artifactStore, CollectionsRetainedArtifactReferenceStore artifactReferenceStore, CollectionTargetMutationLeaseManager mutationLeaseManager,
			CollectionTargetOwnershipAuthorityValidator authorityValidator)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
			_artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
			_artifactReferenceStore = artifactReferenceStore ?? throw new ArgumentNullException(nameof(artifactReferenceStore));
			_mutationLeaseManager = mutationLeaseManager ?? throw new ArgumentNullException(nameof(mutationLeaseManager));
			_authorityValidator = authorityValidator ?? throw new ArgumentNullException(nameof(authorityValidator));
		}

		/// <summary>Executes only the C7.10a native-member phase of one exact reviewed C7.9 plan.</summary>
		public Task<CollectionLocalRestoreMemberExecutionResult> ExecuteAsync(CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan reviewedPlan, GameStoragePathSet paths)
		{
			return ExecuteAsync(sealedCapture, reviewedPlan, paths, CancellationToken.None);
		}

		/// <summary>Executes C7.10a while honoring cancellation before each native worker crosses the mutation boundary.</summary>
		public async Task<CollectionLocalRestoreMemberExecutionResult> ExecuteAsync(CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan reviewedPlan, GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			RequireLiveServices();
			if (sealedCapture == null) throw new ArgumentNullException(nameof(sealedCapture));
			if (reviewedPlan == null) throw new ArgumentNullException(nameof(reviewedPlan));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			if (!reviewedPlan.IsReadyForReview)
				throw new InvalidOperationException("C7.10a cannot execute a restore plan containing unresolved C7.9 issues.");
			if (sealedCapture.Capture.Capability != LocalCaptureCapability.LocallyRestorableWithinScope)
				throw new InvalidOperationException("C7.10a requires a capture sealed as locally restorable within its declared scope.");
			if (!sealedCapture.Capture.Identity.Equals(reviewedPlan.CaptureIdentity))
				throw new InvalidOperationException("The reviewed C7.9 plan belongs to a different Local Collection capture.");

			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(reviewedPlan.Target) || !authority.Target.Equals(sealedCapture.Capture.SourceTarget))
				throw new InvalidOperationException("The live canonical target no longer matches the reviewed Local Collection restore.");

			using (CollectionTargetMutationLease rootLease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				cancellationToken.ThrowIfCancellationRequested();
				_authorityValidator.ValidateAndReload(rootLease, authority, paths);
				if (_operationStore.GetIncompleteOperations(reviewedPlan.Target).Count != 0)
					throw new InvalidOperationException("C7.10a cannot begin while another Collection operation for this target is incomplete.");

				NativeObservation observation = CaptureState(reviewedPlan.Target);
				CollectionLocalRestorePlan livePlan = CreatePlanner().Plan(sealedCapture, reviewedPlan.Target,
					observation.CollectionState.Fingerprint, observation.NativeState, cancellationToken);
				if (!PlansEqual(reviewedPlan, livePlan))
					throw new InvalidOperationException("Native state changed after the C7.9 restore review; rebuild and review the restore plan before mutation.");

				CollectionOperation operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(),
					CollectionOperationKind.RestoreLocalCapture, sealedCapture.Capture.Revision.Collection, reviewedPlan.Target,
					sealedCapture.Capture.Revision, null, 1, CollectionOperationPhase.ApplyingNativeChildren,
					CollectionOperationResultState.Pending, new CollectionNativeChildOperation[0]);
				PersistRestoreIntent(operation, sealedCapture.Capture.Identity, reviewedPlan);
				_operationStore.SaveOperation(operation);

				int nextSequence = 1;
				foreach (string nativeKey in reviewedPlan.CurrentNativeKeysToRemove)
				{
					PauseBeforeCancelledChild(operation, cancellationToken);
					IMod nativeMod = ResolveLiveMod(nativeKey);
					if (nativeMod == null)
						continue;
					ModInstallContext context = _services.ModManager.CaptureInstalledContext(nativeMod);
					CollectionNativeChildOperation child = CreateRemovalChild(operation, sealedCapture.Capture.Revision,
						nativeKey, context, nextSequence++);
					operation = SaveChild(operation, child);
					IBackgroundTaskSet nativeTask = _services.ModManager.CreateCollectionDeactivationOperation(nativeMod,
						_services.ModManager.ActiveMods, child.NativeOperation);
					if (nativeTask == null)
						throw new InvalidOperationException("A reviewed Local restore removal disappeared before native submission.");
					AssignLeaseAndValidateIdentity(nativeTask, child.NativeOperation, rootLease);
					operation = await SubmitVerifyAndReconcileAsync(operation, child, nativeTask, rootLease, authority, paths,
						() => ResolveLiveMod(nativeKey) == null, () => ResolveLiveMod(nativeKey) != null, cancellationToken).ConfigureAwait(true);
				}

				var remaps = new List<CollectionLocalRestoreMemberRemap>();
				foreach (CollectionLocalRestoreMemberPlan member in reviewedPlan.Members)
				{
					PauseBeforeCancelledChild(operation, cancellationToken);
					if (member.Action == CollectionLocalRestoreMemberAction.ReuseExistingNative)
					{
						RequireLiveMod(member.CurrentNativeKey);
						remaps.Add(new CollectionLocalRestoreMemberRemap(member.SnapshotMemberKey, member.CurrentNativeKey));
						continue;
					}
					if (member.Action != CollectionLocalRestoreMemberAction.RecreateFromRetainedArchive)
						throw new InvalidOperationException("C7.10a encountered a restore member that still requires manual review.");

					CollectionInstalledModIdentity captured = RequireCapturedIdentity(sealedCapture, member.CapturedNativeKey);
					string archivePath = MaterializeRetainedArchive(member.RetainedArchive, cancellationToken);
					IMod mod = RegisterRestoredArchive(archivePath, captured);
					string recipeFingerprint = CreateRecipeFingerprint(sealedCapture.Capture.Identity, member);
					var nativeIdentity = ModOperationIdentity.CreateNew(ModOperationOrigin.LocalRestore,
						new ModOperationFingerprint(reviewedPlan.Target.Fingerprint, member.InstallContext, recipeFingerprint));
					ModInstallationRecipeInput recipeInput = BuildRecipeInput(member, nativeIdentity);
					var child = new CollectionNativeChildOperation(nextSequence++,
						new CollectionOperationMemberReference(sealedCapture.Capture.Revision, member.SnapshotMemberKey),
						CollectionNativeChildAction.ActivateOrReinstall, nativeIdentity,
						CollectionNativeChildCheckpoint.RecoveryInputsReady, null);
					operation = SaveChild(operation, child);

					IBackgroundTaskSet nativeTask = _services.ModManager.ActivateMod(mod,
						(oldMod, newMod) => ConfirmUpgradeResult.NormalActivation,
						(message, perGroup, perMod) => OverwriteResult.YesToAll,
						_services.ModManager.ActiveMods, member.InstallContext, true, recipeInput);
					if (nativeTask == null)
						throw new InvalidOperationException("The retained Local Collection member could not create a native activation task.");
					AssignLeaseAndValidateIdentity(nativeTask, child.NativeOperation, rootLease);
					string remappedKey = null;
					operation = await SubmitVerifyAndReconcileAsync(operation, child, nativeTask, rootLease, authority, paths,
						() => TryResolveCommittedMember(archivePath, member.InstallContext, out remappedKey),
						() => ResolveLiveModByArchive(archivePath) == null, cancellationToken).ConfigureAwait(true);
					if (String.IsNullOrWhiteSpace(remappedKey))
						throw new InvalidOperationException("C7.10a native verification committed a member without establishing its current native-key remap.");
					remaps.Add(new CollectionLocalRestoreMemberRemap(member.SnapshotMemberKey, remappedKey));
				}

				_authorityValidator.ValidateAndReload(rootLease, authority, paths);
				observation = CaptureState(reviewedPlan.Target);
				CollectionLocalRestorePlan memberPhasePlan = CreatePlanner().Plan(sealedCapture, reviewedPlan.Target,
					observation.CollectionState.Fingerprint, observation.NativeState, cancellationToken);
				if (memberPhasePlan.Members.Any(x => x.Action != CollectionLocalRestoreMemberAction.ReuseExistingNative) ||
					memberPhasePlan.CurrentNativeKeysToRemove.Count != 0)
				{
					operation = MarkRecoveryRequired(operation);
					throw new InvalidOperationException("C7.10a completed native children but authoritative state does not prove the captured native member set is restored.");
				}

				operation = WithOperationState(_operationStore.GetOperation(operation.Identity) ?? operation,
					CollectionOperationPhase.PausedAtSafeBoundary, CollectionOperationResultState.Pending);
				_operationStore.SaveOperation(operation);
				return new CollectionLocalRestoreMemberExecutionResult(operation, remaps.OrderBy(x => x.MemberKey.ToString(), StringComparer.Ordinal));
			}
		}

		/// <summary>Creates the read-only C7.9 planner over the same sealed retained-artifact authority used by this executor.</summary>
		private CollectionLocalRestorePlanner CreatePlanner()
		{
			return new CollectionLocalRestorePlanner(_artifactStore);
		}

		/// <summary>Persists the exact reviewed restore intent before any native child is submitted.</summary>
		private void PersistRestoreIntent(CollectionOperation operation, LocalCaptureIdentity captureIdentity,
			CollectionLocalRestorePlan plan)
		{
			byte[] bytes = SerializeRestoreIntent(captureIdentity, plan);
			CollectionsRetainedArtifact artifact;
			using (var stream = new MemoryStream(bytes, false))
				artifact = _artifactStore.Publish(stream);
			_artifactReferenceStore.AcquireExclusiveRoleReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation,
				operation.Identity.OperationId.ToString("D"), RestoreIntentRole);
		}

		/// <summary>Serializes the exact reviewed C7.9 plan so later C7.10 recovery/effect phases never reconstruct approval from guesses.</summary>
		internal static byte[] SerializeRestoreIntent(LocalCaptureIdentity captureIdentity, CollectionLocalRestorePlan plan)
		{
			if (captureIdentity == null) throw new ArgumentNullException(nameof(captureIdentity));
			if (plan == null) throw new ArgumentNullException(nameof(plan));
			using (var stream = new MemoryStream())
			using (var text = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
			using (var writer = new JsonTextWriter(text))
			{
				writer.Formatting = Formatting.None;
				writer.WriteStartObject();
				WriteProperty(writer, "format", "nmm-ce.collections.local-restore-plan/1");
				WriteProperty(writer, "captureId", captureIdentity.ToString());
				WriteProperty(writer, "targetFingerprint", plan.Target.Fingerprint);
				WriteProperty(writer, "planFingerprint", plan.PlanFingerprint);
				writer.WritePropertyName("stateFingerprint");
				writer.WriteStartObject();
				writer.WritePropertyName("formatVersion"); writer.WriteValue(plan.CurrentStateFingerprint.FormatVersion);
				WriteProperty(writer, "value", plan.CurrentStateFingerprint.Value);
				writer.WriteEndObject();
				writer.WritePropertyName("deploymentCommitSequence"); writer.WriteValue(plan.CurrentDeploymentCommitSequence);
				WriteProperty(writer, "originalValuesKey", plan.CurrentOriginalValuesKey);

				writer.WritePropertyName("members");
				writer.WriteStartArray();
				foreach (CollectionLocalRestoreMemberPlan member in plan.Members)
				{
					writer.WriteStartObject();
					writer.WritePropertyName("keyKind"); writer.WriteValue((int)member.SnapshotMemberKey.Kind);
					WriteProperty(writer, "keyValue", member.SnapshotMemberKey.Value);
					WriteProperty(writer, "capturedNativeKey", member.CapturedNativeKey);
					writer.WritePropertyName("action"); writer.WriteValue((int)member.Action);
					WriteProperty(writer, "currentNativeKey", member.CurrentNativeKey);
					writer.WritePropertyName("installMethod"); writer.WriteValue((int)member.InstallContext.Method);
					writer.WritePropertyName("installRoot"); writer.WriteValue((int)member.InstallContext.InstallRoot);
					WriteProperty(writer, "archiveArtifactId", member.RetainedArchive == null ? String.Empty : member.RetainedArchive.RetainedArtifact.StableArtifactId);
					writer.WriteEndObject();
				}
				writer.WriteEndArray();

				writer.WritePropertyName("removeNativeKeys");
				writer.WriteStartArray();
				foreach (string key in plan.CurrentNativeKeysToRemove) writer.WriteValue(key);
				writer.WriteEndArray();

				writer.WritePropertyName("deploymentTargets");
				writer.WriteStartArray();
				foreach (CollectionLocalRestoreDeploymentPlan deployment in plan.DeploymentTargets)
				{
					writer.WriteStartObject();
					writer.WritePropertyName("root"); writer.WriteValue((int)deployment.Target.Root);
					WriteProperty(writer, "path", deployment.Target.RelativePath);
					writer.WritePropertyName("promoted"); writer.WriteValue(deployment.Promoted);
					writer.WritePropertyName("owners"); writer.WriteStartArray();
					foreach (CollectionLocalRestoreOwnerBinding owner in deployment.Owners)
					{
						writer.WriteStartObject();
						writer.WritePropertyName("stackIndex"); writer.WriteValue(owner.StackIndex);
						WriteProperty(writer, "capturedOwnerKey", owner.CapturedOwnerKey);
						writer.WritePropertyName("capturedOwnerKind"); writer.WriteValue((int)owner.CapturedOwnerKind);
						writer.WritePropertyName("currentWinner"); writer.WriteValue(owner.CurrentWinner);
						writer.WritePropertyName("bindingKind"); writer.WriteValue((int)owner.BindingKind);
						WriteProperty(writer, "currentNativeKey", owner.CurrentNativeKey);
						WriteProperty(writer, "recreatedMember", owner.RecreatedSnapshotMember == null ? String.Empty : owner.RecreatedSnapshotMember.ToString());
						writer.WriteEndObject();
					}
					writer.WriteEndArray();
					writer.WriteEndObject();
				}
				writer.WriteEndArray();
				writer.WriteEndObject();
				writer.Flush(); text.Flush();
				return stream.ToArray();
			}
		}

		/// <summary>Writes one non-null string property into the durable restore-intent payload.</summary>
		private static void WriteProperty(JsonWriter writer, string name, string value)
		{
			writer.WritePropertyName(name);
			writer.WriteValue(value ?? String.Empty);
		}

		/// <summary>Captures authoritative native and Collection-indexed state for one target in a single observation.</summary>
		private NativeObservation CaptureState(CollectionTargetIdentity target)
		{
			ModManager manager = _services.ModManager;
			NativeStateCaptureSnapshot native = new NativeStateCaptureReader(manager.InstallationLog, manager.VirtualModActivator,
				manager.DeploymentManager, _services.PluginManager, manager.GameMode).Capture();
			CollectionNativeStateIndex collection = new CollectionNativeStateReader(manager.InstallationLog, manager.VirtualModActivator,
				_services.PluginManager, manager.GameMode, _associationStore).Capture(target, native);
			return new NativeObservation(native, collection);
		}

		/// <summary>Submits one prepared native child, then checkpoints its reported and authoritatively observed durability.</summary>
		private async Task<CollectionOperation> SubmitVerifyAndReconcileAsync(CollectionOperation operation,
			CollectionNativeChildOperation child, IBackgroundTaskSet nativeTask, CollectionTargetMutationLease rootLease, CollectionTargetAuthority authority,
			GameStoragePathSet paths, Func<bool> committedProbe, Func<bool> rolledBackProbe, CancellationToken cancellationToken)
		{
			bool accepted;
			try
			{
				accepted = _services.ModActivationMonitor.SubmitWhenIdle(nativeTask, () =>
				{
					cancellationToken.ThrowIfCancellationRequested();
					CollectionOperation current = RequireOperation(operation.Identity);
					CollectionNativeChildOperation currentChild = RequireChild(current, child.Sequence, CollectionNativeChildCheckpoint.RecoveryInputsReady);
					current = ReplaceChild(current, new CollectionNativeChildOperation(currentChild.Sequence, currentChild.Member,
						currentChild.Action, currentChild.NativeOperation, CollectionNativeChildCheckpoint.NativeSubmitted, null));
					_operationStore.SaveOperation(current);
				});
			}
			catch
			{
				CollectionOperation persisted = _operationStore.GetOperation(operation.Identity) ?? operation;
				if (persisted.HasCrossedNativeBoundary)
					MarkRecoveryRequired(persisted);
				throw;
			}
			if (!accepted)
				throw new InvalidOperationException("The shared native operation seam rejected a reviewed Local Collection restore child.");

			try
			{
				await WaitForCompletionAsync(nativeTask).ConfigureAwait(true);
				bool exactResult;
				ModOperationResult reported = CaptureReportedResult(nativeTask, child.NativeOperation, out exactResult);
				_authorityValidator.ValidateAndReload(rootLease, authority, paths);
				bool committed = committedProbe();
				bool rolledBack = rolledBackProbe();
				ModOperationDurability durability = DetermineDurability(reported, exactResult, committed, rolledBack);
				var verified = new ModOperationResult(child.NativeOperation, reported.ReportedStatus, durability, reported.Message);

				operation = RequireOperation(operation.Identity);
				CollectionNativeChildOperation submitted = RequireChild(operation, child.Sequence, CollectionNativeChildCheckpoint.NativeSubmitted);
				var terminal = new CollectionNativeChildOperation(submitted.Sequence, submitted.Member, submitted.Action,
					submitted.NativeOperation, CollectionNativeChildCheckpoint.NativeTerminalObserved, verified);
				operation = ReplaceChild(operation, terminal);
				_operationStore.SaveOperation(operation);
				if (durability != ModOperationDurability.VerifiedCommitted)
				{
					if (durability == ModOperationDurability.Unknown)
						MarkRecoveryRequired(operation);
					throw new InvalidOperationException("The Local Collection native child did not verify as durably committed.");
				}
				var reconciled = new CollectionNativeChildOperation(terminal.Sequence, terminal.Member, terminal.Action,
					terminal.NativeOperation, CollectionNativeChildCheckpoint.Reconciled, terminal.NativeResult);
				operation = ReplaceChild(operation, reconciled);
				_operationStore.SaveOperation(operation);
				return operation;
			}
			catch
			{
				CollectionOperation persisted = _operationStore.GetOperation(operation.Identity) ?? operation;
				if (persisted.HasCrossedNativeBoundary && !persisted.IsTerminal)
					MarkRecoveryRequired(persisted);
				throw;
			}
		}

		/// <summary>Creates a durable LocalRestore deactivation child for a current native registration absent from the capture.</summary>
		private CollectionNativeChildOperation CreateRemovalChild(CollectionOperation operation,
			CollectionRevisionIdentity revision, string nativeKey, ModInstallContext context, int sequence)
		{
			var identity = ModOperationIdentity.CreateNew(ModOperationOrigin.LocalRestore,
				new ModOperationFingerprint(operation.Target.Fingerprint, context, null));
			var member = new CollectionOperationMemberReference(revision, CreateRemovalJournalKey(operation, nativeKey));
			return new CollectionNativeChildOperation(sequence, member, CollectionNativeChildAction.Deactivate,
				identity, CollectionNativeChildCheckpoint.RecoveryInputsReady, null);
		}

		/// <summary>Creates a deterministic journal-only member key for a current native mod that has no captured member identity.</summary>
		private static CollectionMemberKey CreateRemovalJournalKey(CollectionOperation operation, string nativeKey)
		{
			byte[] bytes = Encoding.UTF8.GetBytes(operation.Identity.OperationId.ToString("D") + "\n" + nativeKey);
			using (SHA256 sha = SHA256.Create())
			{
				byte[] hash = sha.ComputeHash(bytes);
				byte[] guidBytes = new byte[16];
				Buffer.BlockCopy(hash, 0, guidBytes, 0, guidBytes.Length);
				return CollectionMemberKey.FromLocal(new Guid(guidBytes));
			}
		}

		/// <summary>Materializes and re-verifies one sealed retained archive inside the current game mod library.</summary>
		private string MaterializeRetainedArchive(CollectionCapturedArchiveArtifact archive, CancellationToken cancellationToken)
		{
			if (archive == null)
				throw new InvalidOperationException("A recreate action requires its sealed retained archive descriptor.");
			RetainedArtifactReference retained = archive.RetainedArtifact;
			if (!_artifactStore.VerifyArtifact(retained.StableArtifactId, cancellationToken))
				throw new InvalidDataException("The retained Local Collection archive failed verification immediately before restoration.");

			string fileName = Path.GetFileName(archive.FileName);
			if (String.IsNullOrWhiteSpace(fileName) || !StringComparer.Ordinal.Equals(fileName, archive.FileName))
				throw new InvalidDataException("The sealed Local Collection archive name is not a single safe file name.");
			string directory = _services.ModManager.CurrentGameModeModDirectory;
			Directory.CreateDirectory(directory);
			string destination = Path.Combine(directory, fileName);
			if (File.Exists(destination) && !FileMatches(destination, retained, cancellationToken))
			{
				string stem = Path.GetFileNameWithoutExtension(fileName);
				string extension = Path.GetExtension(fileName);
				destination = Path.Combine(directory, stem + ".localrestore-" + retained.ContentHash.Value.Substring(0, 12) + extension);
				if (File.Exists(destination) && !FileMatches(destination, retained, cancellationToken))
					throw new IOException("The deterministic Local Collection restore archive destination already contains different bytes.");
			}
			if (File.Exists(destination))
				return destination;

			string staging = destination + "." + Guid.NewGuid().ToString("N") + ".restoretmp";
			try
			{
				using (Stream source = _artifactStore.OpenRead(retained.StableArtifactId))
				using (var target = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
					var buffer = new byte[81920];
					int read;
					while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
					{
						cancellationToken.ThrowIfCancellationRequested();
						target.Write(buffer, 0, read);
					}
					target.Flush(true);
				}
				if (!FileMatches(staging, retained, cancellationToken))
					throw new InvalidDataException("Materialized Local Collection archive bytes do not match their sealed retained identity.");
				File.Move(staging, destination);
				return destination;
			}
			finally
			{
				if (File.Exists(staging))
					File.Delete(staging);
			}
		}

		/// <summary>Registers one materialized retained archive and reapplies its captured Nexus/version metadata without network access.</summary>
		private IMod RegisterRestoredArchive(string archivePath, CollectionInstalledModIdentity captured)
		{
			IMod mod = _services.ModManager.RegisterLocalRestoreArchive(archivePath, null);
			if (mod == null)
				throw new InvalidDataException("The retained Local Collection archive is not a native mod format supported by the current game mode.");
			var info = new ModInfo(mod) { Id = captured.NexusModId, DownloadId = captured.NexusFileId,
				HumanReadableVersion = captured.HumanReadableVersion };
			Version machineVersion;
			if (!String.IsNullOrWhiteSpace(captured.MachineVersion) && Version.TryParse(captured.MachineVersion, out machineVersion))
				info.MachineVersion = machineVersion;
			mod.UpdateInfo(info, true);
			return mod;
		}

		/// <summary>Builds the registration-only native recipe bound to the exact retained archive and restore attempt.</summary>
		private ModInstallationRecipeInput BuildRecipeInput(CollectionLocalRestoreMemberPlan member,
			ModOperationIdentity operationIdentity)
		{
			RetainedArtifactReference archive = member.RetainedArchive.RetainedArtifact;
			var validation = new ModInstallationRecipeValidation(ModInstallationRestoreRegistrationRecipeAdapter.AdapterId,
				ModInstallationRestoreRegistrationRecipeAdapter.AdapterVersion, member.InstallContext,
				new ModInstallationRecipeExpectedContent(archive.ContentHash.Value, archive.ByteLength),
				new[] { new ModInstallationRecipeCapability(ModInstallationRestoreRegistrationRecipeAdapter.CapabilityId,
					ModInstallationRestoreRegistrationRecipeAdapter.CapabilityVersion) }, new ModInstallationRecipePath[0]);
			return new ModInstallationRestoreRegistrationRecipeAdapter().Translate(
				new ModInstallationRecipeInput(operationIdentity, validation));
		}

		/// <summary>Creates the deterministic recipe identity for registration-only restoration of one captured member.</summary>
		private static string CreateRecipeFingerprint(LocalCaptureIdentity captureIdentity,
			CollectionLocalRestoreMemberPlan member)
		{
			string value = captureIdentity + "\n" + member.SnapshotMemberKey + "\n" +
				member.RetainedArchive.RetainedArtifact.ContentHash.Value + "\nregistration-only-v1";
			using (SHA256 sha = SHA256.Create())
				return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", String.Empty).ToLowerInvariant();
		}

		/// <summary>Verifies a materialized file against one retained artifact length and SHA-256 identity.</summary>
		private static bool FileMatches(string path, RetainedArtifactReference retained, CancellationToken cancellationToken)
		{
			var info = new FileInfo(path);
			if (!info.Exists || info.Length != retained.ByteLength)
				return false;
			using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan))
			using (SHA256 sha = SHA256.Create())
			{
				byte[] buffer = new byte[81920];
				int read;
				while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
				{
					cancellationToken.ThrowIfCancellationRequested();
					sha.TransformBlock(buffer, 0, read, null, 0);
				}
				sha.TransformFinalBlock(new byte[0], 0, 0);
				string actual = BitConverter.ToString(sha.Hash).Replace("-", String.Empty).ToLowerInvariant();
				return StringComparer.Ordinal.Equals(actual, retained.ContentHash.Value);
			}
		}

		/// <summary>Resolves one unambiguous captured installed-mod identity by its snapshot native key.</summary>
		private CollectionInstalledModIdentity RequireCapturedIdentity(CollectionSealedCaptureSnapshot capture, string nativeKey)
		{
			List<CollectionInstalledModIdentity> matches = capture.InstalledIdentities.Mods
				.Where(x => StringComparer.Ordinal.Equals(x.NativeSnapshotKey, nativeKey)).ToList();
			if (matches.Count != 1)
				throw new InvalidDataException("The sealed capture does not map the restore member to exactly one captured native identity.");
			return matches[0];
		}

		/// <summary>Resolves an active native mod by its current InstallLog key, returning null when absent.</summary>
		private IMod ResolveLiveMod(string nativeKey)
		{
			return _services.ModManager.ActiveMods.SingleOrDefault(mod =>
			{
				try { return StringComparer.Ordinal.Equals(_services.ModManager.InstallationLog.GetModKey(mod), nativeKey); }
				catch { return false; }
			});
		}

		/// <summary>Requires one current active native mod to still exist under its reviewed key.</summary>
		private IMod RequireLiveMod(string nativeKey)
		{
			IMod mod = ResolveLiveMod(nativeKey);
			if (mod == null)
				throw new InvalidOperationException("A native registration selected for Local Collection reuse is no longer active.");
			return mod;
		}

		/// <summary>Resolves an active native mod by exact materialized archive path.</summary>
		private IMod ResolveLiveModByArchive(string archivePath)
		{
			return _services.ModManager.ActiveMods.SingleOrDefault(x =>
				StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(x.Filename), Path.GetFullPath(archivePath)));
		}

		/// <summary>Verifies a recreated member is active under the reviewed context and returns its newly authoritative native key.</summary>
		private bool TryResolveCommittedMember(string archivePath, ModInstallContext expectedContext, out string nativeKey)
		{
			nativeKey = null;
			IMod mod = ResolveLiveModByArchive(archivePath);
			if (mod == null)
				return false;
			ModInstallContext actual = _services.ModManager.CaptureInstalledContext(mod);
			if (actual.Method != expectedContext.Method || actual.InstallRoot != expectedContext.InstallRoot)
				return false;
			try
			{
				nativeKey = _services.ModManager.InstallationLog.GetModKey(mod);
				return !String.IsNullOrWhiteSpace(nativeKey);
			}
			catch
			{
				return false;
			}
		}

		/// <summary>Compares the immutable approval-relevant identities of two C7.9 plans.</summary>
		private static bool PlansEqual(CollectionLocalRestorePlan left, CollectionLocalRestorePlan right)
		{
			return left != null && right != null && left.CaptureIdentity.Equals(right.CaptureIdentity) &&
				left.Target.Equals(right.Target) && left.CurrentStateFingerprint.Equals(right.CurrentStateFingerprint) &&
				left.CurrentDeploymentCommitSequence == right.CurrentDeploymentCommitSequence &&
				StringComparer.Ordinal.Equals(left.CurrentOriginalValuesKey, right.CurrentOriginalValuesKey) &&
				StringComparer.Ordinal.Equals(left.PlanFingerprint, right.PlanFingerprint);
		}

		/// <summary>Proves a native task retained the exact reviewed identity before assigning the already-held target lease.</summary>
		private static void AssignLeaseAndValidateIdentity(IBackgroundTaskSet task, ModOperationIdentity expected,
			CollectionTargetMutationLease lease)
		{
			ModInstallerBase installer = task as ModInstallerBase;
			if (installer == null || installer.OperationIdentity == null || !Matches(installer.OperationIdentity, expected))
				throw new InvalidOperationException("The native Local Collection restore task did not retain the exact durable operation identity.");
			installer.AssignParentMutationLease(lease);
		}

		/// <summary>Compares complete native logical/attempt identity including fingerprint and origin.</summary>
		private static bool Matches(ModOperationIdentity left, ModOperationIdentity right)
		{
			return left != null && right != null && left.OperationId == right.OperationId && left.AttemptId == right.AttemptId &&
				left.Origin == right.Origin && left.Fingerprint.Equals(right.Fingerprint);
		}

		/// <summary>Adds or replaces one child snapshot and persists the next operation checkpoint.</summary>
		private CollectionOperation SaveChild(CollectionOperation operation, CollectionNativeChildOperation child)
		{
			var children = operation.NativeChildren.Where(x => x.Sequence != child.Sequence).Concat(new[] { child });
			CollectionOperation updated = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
				operation.Revision, operation.PlanIdentity, operation.CheckpointSequence + 1, operation.Phase, operation.ResultState, children);
			_operationStore.SaveOperation(updated);
			return updated;
		}

		/// <summary>Returns the next immutable operation snapshot with one child checkpoint replaced.</summary>
		private static CollectionOperation ReplaceChild(CollectionOperation operation, CollectionNativeChildOperation child)
		{
			var children = operation.NativeChildren.Where(x => x.Sequence != child.Sequence).Concat(new[] { child });
			return new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target, operation.Revision,
				operation.PlanIdentity, operation.CheckpointSequence + 1, operation.Phase, operation.ResultState, children);
		}

		/// <summary>Returns the next immutable operation snapshot with a new phase/result checkpoint.</summary>
		private static CollectionOperation WithOperationState(CollectionOperation operation, CollectionOperationPhase phase,
			CollectionOperationResultState state)
		{
			return new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
				operation.Revision, operation.PlanIdentity, operation.CheckpointSequence + 1, phase, state, operation.NativeChildren);
		}

		/// <summary>Persists a safe-boundary pause when cancellation arrives before the next native child is submitted.</summary>
		private void PauseBeforeCancelledChild(CollectionOperation operation, CancellationToken cancellationToken)
		{
			if (!cancellationToken.IsCancellationRequested)
				return;
			CollectionOperation current = _operationStore.GetOperation(operation.Identity) ?? operation;
			if (!current.HasUnreconciledNativeChild)
			{
				current = WithOperationState(current, CollectionOperationPhase.PausedAtSafeBoundary, CollectionOperationResultState.Pending);
				_operationStore.SaveOperation(current);
			}
			cancellationToken.ThrowIfCancellationRequested();
		}

		/// <summary>Persists the restore operation as requiring authoritative recovery before more native work.</summary>
		private CollectionOperation MarkRecoveryRequired(CollectionOperation operation)
		{
			CollectionOperation updated = WithOperationState(operation, CollectionOperationPhase.RecoveryRequired,
				CollectionOperationResultState.RecoveryRequired);
			_operationStore.SaveOperation(updated);
			return updated;
		}

		/// <summary>Loads the current mutable Local Collection restore operation from the durable journal.</summary>
		private CollectionOperation RequireOperation(CollectionOperationIdentity identity)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Kind != CollectionOperationKind.RestoreLocalCapture || operation.IsTerminal)
				throw new InvalidOperationException("The active Local Collection restore operation journal is unavailable or no longer mutable.");
			return operation;
		}

		/// <summary>Requires one persisted child to be present at the exact expected durable checkpoint.</summary>
		private static CollectionNativeChildOperation RequireChild(CollectionOperation operation, int sequence,
			CollectionNativeChildCheckpoint checkpoint)
		{
			CollectionNativeChildOperation child = operation.NativeChildren.SingleOrDefault(x => x.Sequence == sequence);
			if (child == null || child.Checkpoint != checkpoint)
				throw new InvalidOperationException("The persisted Local Collection restore child is not at the expected durable checkpoint.");
			return child;
		}

		/// <summary>Bridges the native completion event to a task without running continuations inline in the event callback.</summary>
		private static Task WaitForCompletionAsync(IBackgroundTaskSet task)
		{
			if (task.IsCompleted)
				return Task.FromResult<object>(null);
			var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
			EventHandler<TaskSetCompletedEventArgs> handler = null;
			handler = (sender, args) => { task.TaskSetCompleted -= handler; completion.TrySetResult(null); };
			task.TaskSetCompleted += handler;
			if (task.IsCompleted)
			{
				task.TaskSetCompleted -= handler;
				completion.TrySetResult(null);
			}
			return completion.Task;
		}

		/// <summary>Captures the native task result while reporting whether it belongs to the exact durable child attempt.</summary>
		private static ModOperationResult CaptureReportedResult(IBackgroundTaskSet task, ModOperationIdentity expected, out bool exactIdentity)
		{
			ModInstallerBase installer = task as ModInstallerBase;
			ModOperationResult result = installer == null ? null : installer.OperationResult;
			exactIdentity = result != null && Matches(result.Identity, expected);
			if (result != null)
				return result;
			return new ModOperationResult(expected, ModOperationReportedStatus.Failed, ModOperationDurability.Unknown,
				"The native task completed without a durable operation result.");
		}

		/// <summary>Classifies durability from authoritative committed/rolled-back probes rather than task completion alone.</summary>
		private static ModOperationDurability DetermineDurability(ModOperationResult reported, bool exactIdentity,
			bool committedState, bool rolledBackState)
		{
			if (committedState && !rolledBackState)
				return ModOperationDurability.VerifiedCommitted;
			if (rolledBackState && !committedState)
				return ModOperationDurability.VerifiedRolledBack;
			if (exactIdentity && reported != null && reported.Durability == ModOperationDurability.VerifiedCommitted && committedState)
				return ModOperationDurability.VerifiedCommitted;
			return ModOperationDurability.Unknown;
		}

		/// <summary>Validates that the live native manager and shared activation-monitor seam required by restore are available.</summary>
		private void RequireLiveServices()
		{
			if (_services.ModManager == null || _services.ModActivationMonitor == null)
				throw new InvalidOperationException("C7.10a requires live native mod-management and activation-monitor services.");
		}

		private sealed class NativeObservation
		{
			/// <summary>Creates one paired native/Collection state observation.</summary>
			internal NativeObservation(NativeStateCaptureSnapshot nativeState, CollectionNativeStateIndex collectionState)
			{
				NativeState = nativeState;
				CollectionState = collectionState;
			}
			internal NativeStateCaptureSnapshot NativeState { get; }
			internal CollectionNativeStateIndex CollectionState { get; }
		}
	}
}
