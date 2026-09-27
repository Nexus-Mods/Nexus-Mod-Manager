using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.Mods;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Describes how one C7.10b2 scripted-replay artifact set reached its verified live state.</summary>
	public enum CollectionLocalRestoreReplayOutcome
	{
		AlreadySatisfied = 1,
		RestoredAndVerified = 2,
		RecoveredCommitted = 3
	}

	/// <summary>One verified scripted-replay/generated-payload restoration result.</summary>
	public sealed class CollectionLocalRestoreReplayMemberResult
	{
		internal CollectionLocalRestoreReplayMemberResult(string capturedNativeKey, string currentNativeKey,
			CollectionLocalRestoreReplayOutcome outcome)
		{
			CapturedNativeKey = capturedNativeKey ?? String.Empty;
			CurrentNativeKey = currentNativeKey ?? String.Empty;
			Outcome = outcome;
		}

		public string CapturedNativeKey { get; }
		public string CurrentNativeKey { get; }
		public CollectionLocalRestoreReplayOutcome Outcome { get; }
	}

	/// <summary>Result of the C7.10b2 replay slice; plugin/configuration/user-metadata restoration remains pending.</summary>
	public sealed class CollectionLocalRestoreReplayExecutionResult
	{
		private readonly ReadOnlyCollection<CollectionLocalRestoreReplayMemberResult> _members;

		internal CollectionLocalRestoreReplayExecutionResult(CollectionOperation operation,
			IEnumerable<CollectionLocalRestoreReplayMemberResult> members)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			_members = new ReadOnlyCollection<CollectionLocalRestoreReplayMemberResult>(
				(members ?? throw new ArgumentNullException(nameof(members))).ToList());
		}

		public CollectionOperation Operation { get; }
		public ReadOnlyCollection<CollectionLocalRestoreReplayMemberResult> Members { get { return _members; } }
		public bool IsReadyForPluginStateReconciliation
		{
			get { return Operation.Phase == CollectionOperationPhase.PausedAtSafeBoundary && !Operation.HasUnreconciledNativeChild; }
		}
	}

	/// <summary>
	/// Restores retained scripted-installer replay XML and generated payload sidecars after exact owner restoration.
	/// </summary>
	/// <remarks>
	/// The executor never reruns installer code. It restores only C7.4 retained replay artifacts, persists an exact live preimage
	/// before mutation, and accepts restart reconciliation only for the exact preimage, exact desired postimage, or a narrowly
	/// recognizable intermediate state produced by the native replay-artifact copy sequence.
	/// </remarks>
	public sealed class CollectionLocalRestoreReplayExecutor
	{
		private const string IntentFormat = "nmm-ce.collections.local-restore-replay-intent/1";
		private const string IntentRolePrefix = "local-restore-replay-intent-";
		private const string VerifiedRolePrefix = "local-restore-replay-verified-";
		private const string InputRolePrefix = "local-restore-replay-input-";
		private const string CompleteRole = "local-restore-replay-phase-complete-v1";
		private const int CopyBufferSize = 81920;

		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionsRetainedArtifactReferenceStore _referenceStore;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;

		/// <summary>Creates the production C7.10b2 scripted-replay restoration executor.</summary>
		public CollectionLocalRestoreReplayExecutor(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore)
			: this(services, gameStorageService, operationStore, artifactStore, referenceStore,
				CollectionTargetMutationLeaseManager.Shared, new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services))
		{
		}

		internal CollectionLocalRestoreReplayExecutor(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore, CollectionTargetMutationLeaseManager mutationLeaseManager,
			CollectionTargetOwnershipAuthorityValidator authorityValidator)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
			_referenceStore = referenceStore ?? throw new ArgumentNullException(nameof(referenceStore));
			_mutationLeaseManager = mutationLeaseManager ?? throw new ArgumentNullException(nameof(mutationLeaseManager));
			_authorityValidator = authorityValidator ?? throw new ArgumentNullException(nameof(authorityValidator));
			if (_services.ModManager == null || _services.ModManager.DeploymentManager == null)
				throw new InvalidOperationException("C7.10b2 replay restoration requires the live native ModManager and deployment manager.");
		}

		private IModDeploymentManager CurrentDeploymentManager
		{
			get
			{
				if (_services.ModManager == null || _services.ModManager.DeploymentManager == null)
					throw new InvalidOperationException("The current native deployment manager is unavailable after authority reload.");
				return _services.ModManager.DeploymentManager;
			}
		}

		/// <summary>Restores retained replay XML/generated payloads after C7.10b1 reached its verified safe boundary.</summary>
		public Task<CollectionLocalRestoreReplayExecutionResult> ExecuteAsync(CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan reviewedPlan, CollectionLocalRestoreMemberExecutionResult memberPhase,
			CollectionLocalRestoreOwnershipExecutionResult ownershipPhase, GameStoragePathSet paths)
		{
			return ExecuteAsync(sealedCapture, reviewedPlan, memberPhase, ownershipPhase, paths, CancellationToken.None);
		}

		/// <summary>Restores retained replay XML/generated payloads with cooperative cancellation between member mutations.</summary>
		public async Task<CollectionLocalRestoreReplayExecutionResult> ExecuteAsync(CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan reviewedPlan, CollectionLocalRestoreMemberExecutionResult memberPhase,
			CollectionLocalRestoreOwnershipExecutionResult ownershipPhase, GameStoragePathSet paths,
			CancellationToken cancellationToken)
		{
			ValidateInputs(sealedCapture, reviewedPlan, memberPhase, ownershipPhase, paths);
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(reviewedPlan.Target))
				throw new InvalidOperationException("The live canonical target no longer matches the reviewed Local Collection restore.");

			using (CollectionTargetMutationLease rootLease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				_authorityValidator.ValidateAndReload(rootLease, authority, paths);
				CollectionOperation operation = RequireOperation(ownershipPhase.Operation.Identity, reviewedPlan);
				Dictionary<CollectionMemberKey, string> remaps = BuildRemaps(reviewedPlan, memberPhase);
				List<ReplayJob> jobs = BuildJobs(sealedCapture, reviewedPlan, remaps, paths);
				var results = new List<CollectionLocalRestoreReplayMemberResult>();
				string ownerId = operation.Identity.OperationId.ToString("D");

				foreach (ReplayJob job in jobs)
				{
					cancellationToken.ThrowIfCancellationRequested();
					string token = CreateToken(job.ArtifactSet.NativeSnapshotKey);
					string intentRole = IntentRolePrefix + token;
					string verifiedRole = VerifiedRolePrefix + token;
					string tempDirectory = CreateTemporaryDirectory();
					try
					{
						AcquireInputReferences(ownerId, token, job.ArtifactSet, cancellationToken);
						string stagingReplayPath = MaterializeDesired(job.ArtifactSet, tempDirectory, cancellationToken);
						CollectionLocalRestoreReplayArtifactState desired = CollectionLocalRestoreReplayFileSystem.Capture(
							stagingReplayPath, cancellationToken);
						ValidateDesiredState(job.ArtifactSet, desired);

						CollectionsRetainedArtifactReferenceRecord verified = _referenceStore.GetReferenceForOwnerRole(
							CollectionsRetainedArtifactOwnerKind.Operation, ownerId, verifiedRole);
						if (verified != null)
						{
							ReplayIntent verifiedIntent = ReadIntent(verified.ArtifactId);
							ValidateIntent(verifiedIntent, sealedCapture, reviewedPlan, job, desired);
							if (!CollectionLocalRestoreReplayFileSystem.Capture(job.DestinationReplayPath, cancellationToken).Equals(desired))
							{
								MarkRecoveryRequired(operation);
								throw new InvalidOperationException("A durable replay verified checkpoint no longer matches the live scripted replay artifacts.");
							}
							results.Add(new CollectionLocalRestoreReplayMemberResult(job.ArtifactSet.NativeSnapshotKey,
								job.CurrentNativeKey, CollectionLocalRestoreReplayOutcome.AlreadySatisfied));
							continue;
						}

						CollectionsRetainedArtifactReferenceRecord existingIntent = _referenceStore.GetReferenceForOwnerRole(
							CollectionsRetainedArtifactOwnerKind.Operation, ownerId, intentRole);
						ReplayIntent intent;
						if (existingIntent == null)
						{
							CollectionLocalRestoreReplayArtifactState preimage = CollectionLocalRestoreReplayFileSystem.Capture(
								job.DestinationReplayPath, cancellationToken);
							intent = CreateIntent(sealedCapture, reviewedPlan, job, preimage, desired);
							existingIntent = PersistIntent(ownerId, intentRole, intent);
						}
						else
						{
							try
							{
								intent = ReadIntent(existingIntent.ArtifactId);
								ValidateIntent(intent, sealedCapture, reviewedPlan, job, desired);
							}
							catch
							{
								MarkRecoveryRequired(operation);
								throw;
							}

							CollectionLocalRestoreReplayArtifactState live = CollectionLocalRestoreReplayFileSystem.Capture(
								job.DestinationReplayPath, cancellationToken);
							if (live.Equals(desired))
							{
								MarkVerified(ownerId, verifiedRole, existingIntent.ArtifactId);
								results.Add(new CollectionLocalRestoreReplayMemberResult(job.ArtifactSet.NativeSnapshotKey,
									job.CurrentNativeKey, CollectionLocalRestoreReplayOutcome.RecoveredCommitted));
								continue;
							}
							if (!live.Equals(intent.Preimage) &&
								!CollectionLocalRestoreReplayFileSystem.IsSafeTransition(live, intent.Preimage, desired))
							{
								MarkRecoveryRequired(operation);
								throw new InvalidOperationException("The live scripted replay differs from both its exact preimage and a recognizable restore transition state.");
							}
						}

						CollectionLocalRestoreReplayFileSystem.ReplaceArtifacts(stagingReplayPath, job.DestinationReplayPath);
						CollectionLocalRestoreReplayArtifactState postimage = CollectionLocalRestoreReplayFileSystem.Capture(
							job.DestinationReplayPath, cancellationToken);
						if (!postimage.Equals(desired))
						{
							MarkRecoveryRequired(operation);
							throw new InvalidOperationException("Scripted replay restoration returned without establishing the exact retained replay/generated-payload bytes.");
						}

						MarkVerified(ownerId, verifiedRole, existingIntent.ArtifactId);
						results.Add(new CollectionLocalRestoreReplayMemberResult(job.ArtifactSet.NativeSnapshotKey,
							job.CurrentNativeKey, CollectionLocalRestoreReplayOutcome.RestoredAndVerified));
					}
					finally
					{
						TryDeleteDirectory(tempDirectory);
					}
				}

				operation = CompleteReplayPhase(operation, sealedCapture.Capture.Identity, reviewedPlan, results.Count);
				return new CollectionLocalRestoreReplayExecutionResult(operation, results);
			}
		}

		private void ValidateInputs(CollectionSealedCaptureSnapshot sealedCapture, CollectionLocalRestorePlan reviewedPlan,
			CollectionLocalRestoreMemberExecutionResult memberPhase, CollectionLocalRestoreOwnershipExecutionResult ownershipPhase,
			GameStoragePathSet paths)
		{
			if (sealedCapture == null) throw new ArgumentNullException(nameof(sealedCapture));
			if (reviewedPlan == null) throw new ArgumentNullException(nameof(reviewedPlan));
			if (memberPhase == null) throw new ArgumentNullException(nameof(memberPhase));
			if (ownershipPhase == null) throw new ArgumentNullException(nameof(ownershipPhase));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			if (!reviewedPlan.IsReadyForReview || sealedCapture.Capture.Capability != LocalCaptureCapability.LocallyRestorableWithinScope)
				throw new InvalidOperationException("Replay restoration requires one reviewed locally-restorable C7.9 plan.");
			if (!sealedCapture.Capture.Identity.Equals(reviewedPlan.CaptureIdentity) ||
				!sealedCapture.ScriptedReplay.Target.Equals(reviewedPlan.Target))
				throw new InvalidOperationException("Replay restoration inputs do not belong to the same Local Collection capture and target.");
			if (!memberPhase.IsReadyForEffectReconciliation || !ownershipPhase.IsReadyForStatefulEffectReconciliation)
				throw new InvalidOperationException("Replay restoration requires reconciled C7.10a and exact-owner C7.10b1 safe boundaries.");
			if (!memberPhase.Operation.Identity.Equals(ownershipPhase.Operation.Identity) ||
				memberPhase.Operation.Kind != CollectionOperationKind.RestoreLocalCapture ||
				!ownershipPhase.Operation.Target.Equals(reviewedPlan.Target))
				throw new InvalidOperationException("The replay prerequisites do not belong to the same active Local Collection restore operation.");
		}

		private CollectionOperation RequireOperation(CollectionOperationIdentity identity, CollectionLocalRestorePlan plan)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Kind != CollectionOperationKind.RestoreLocalCapture || operation.IsTerminal ||
				operation.ResultState != CollectionOperationResultState.Pending || operation.Phase != CollectionOperationPhase.PausedAtSafeBoundary ||
				!operation.Target.Equals(plan.Target) || operation.HasUnreconciledNativeChild)
				throw new InvalidOperationException("Replay restoration requires the exact active Local Collection restore operation at a reconciled safe boundary.");
			return operation;
		}

		private static Dictionary<CollectionMemberKey, string> BuildRemaps(CollectionLocalRestorePlan plan,
			CollectionLocalRestoreMemberExecutionResult memberPhase)
		{
			Dictionary<CollectionMemberKey, string> result = memberPhase.MemberRemaps.ToDictionary(x => x.MemberKey, x => x.NativeKey);
			if (result.Count != plan.Members.Count || plan.Members.Any(x => !result.ContainsKey(x.SnapshotMemberKey)))
				throw new InvalidOperationException("The member phase did not provide a complete native-key remap for replay restoration.");
			return result;
		}

		private List<ReplayJob> BuildJobs(CollectionSealedCaptureSnapshot sealedCapture, CollectionLocalRestorePlan plan,
			IDictionary<CollectionMemberKey, string> remaps, GameStoragePathSet paths)
		{
			if (!sealedCapture.Capture.Scope.Contains(LocalCaptureScopeArea.InstallerReplayAndGeneratedPayloads))
				return new List<ReplayJob>();

			Dictionary<string, CollectionLocalRestoreMemberPlan> members = plan.Members.ToDictionary(
				x => x.CapturedNativeKey, x => x, StringComparer.OrdinalIgnoreCase);
			Dictionary<string, CollectionInstalledModIdentity> captured = sealedCapture.InstalledIdentities.Mods.ToDictionary(
				x => x.NativeSnapshotKey, x => x, StringComparer.OrdinalIgnoreCase);
			var jobs = new List<ReplayJob>();

			foreach (CollectionScriptedReplayArtifactSet artifactSet in sealedCapture.ScriptedReplay.ArtifactSets
				.OrderBy(x => x.NativeSnapshotKey, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.NativeSnapshotKey, StringComparer.Ordinal))
			{
				CollectionLocalRestoreMemberPlan member;
				CollectionInstalledModIdentity capturedMod;
				if (!members.TryGetValue(artifactSet.NativeSnapshotKey, out member) ||
					!captured.TryGetValue(artifactSet.NativeSnapshotKey, out capturedMod) || !capturedMod.HasInstallScript)
					throw new InvalidDataException("A retained scripted replay artifact set cannot be mapped to its captured scripted native member.");
				if (!artifactSet.CompleteReplayFormat || !artifactSet.ReplayPlanValidated)
					throw new InvalidDataException("A Local Collection replay restore requires the complete validated C7.4 replay format.");

				string currentNativeKey;
				if (!remaps.TryGetValue(member.SnapshotMemberKey, out currentNativeKey))
					throw new InvalidOperationException("A scripted captured member has no current native-key remap.");
				IMod mod = CurrentDeploymentManager.GetOwnerMod(currentNativeKey);
				if (mod == null)
					throw new InvalidOperationException("A scripted captured member is no longer present in current native state.");
				ModInstallContext context = _services.ModManager.CaptureInstalledContext(mod);
				if (context.Method != member.InstallContext.Method || context.InstallRoot != member.InstallContext.InstallRoot)
					throw new InvalidOperationException("A scripted replay member no longer matches its reviewed native install context.");
				string replayPath = ScriptedFileSelectionCache.GetDefaultFilePath(mod.Filename, paths.InstallInfoPath);
				jobs.Add(new ReplayJob(artifactSet, member, currentNativeKey, mod.Filename, replayPath));
			}

			int expected = sealedCapture.InstalledIdentities.Mods.Count(x => x.HasInstallScript);
			if (jobs.Count != expected)
				throw new InvalidDataException("The sealed capture does not contain exactly one replay artifact set for every scripted native member.");
			if (jobs.GroupBy(x => Path.GetFullPath(x.DestinationReplayPath), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() != 1))
				throw new InvalidOperationException("Restored scripted members resolve to a colliding live replay path.");
			return jobs;
		}

		private void AcquireInputReferences(string ownerId, string token, CollectionScriptedReplayArtifactSet artifactSet,
			CancellationToken cancellationToken)
		{
			ValidateRetainedReference(artifactSet.ReplayXml, cancellationToken);
			_referenceStore.AcquireExclusiveRoleReference(artifactSet.ReplayXml.StableArtifactId,
				CollectionsRetainedArtifactOwnerKind.Operation, ownerId, InputRolePrefix + token + "-xml");
			foreach (CollectionScriptedGeneratedPayload payload in artifactSet.GeneratedPayloads.OrderBy(x => x.ReplayOperationIndex))
			{
				ValidateRetainedReference(payload.RetainedArtifact, cancellationToken);
				_referenceStore.AcquireExclusiveRoleReference(payload.RetainedArtifact.StableArtifactId,
					CollectionsRetainedArtifactOwnerKind.Operation, ownerId,
					InputRolePrefix + token + "-payload-" + payload.ReplayOperationIndex.ToString("D6", CultureInfo.InvariantCulture));
			}
		}

		private void ValidateRetainedReference(RetainedArtifactReference reference, CancellationToken cancellationToken)
		{
			if (reference == null)
				throw new InvalidDataException("A scripted replay retained-artifact reference is missing.");
			CollectionsRetainedArtifact artifact = _artifactStore.GetArtifact(reference.StableArtifactId);
			if (artifact == null || artifact.ByteLength != reference.ByteLength || !artifact.ContentHash.Equals(reference.ContentHash) ||
				!_artifactStore.VerifyArtifact(reference.StableArtifactId, cancellationToken))
				throw new InvalidDataException("A scripted replay retained artifact no longer matches its sealed capture identity.");
		}

		private string MaterializeDesired(CollectionScriptedReplayArtifactSet artifactSet, string tempDirectory,
			CancellationToken cancellationToken)
		{
			string replayPath = Path.Combine(tempDirectory, "replay.xml");
			Materialize(artifactSet.ReplayXml, replayPath, cancellationToken);
			var payloadNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (CollectionScriptedGeneratedPayload payload in artifactSet.GeneratedPayloads.OrderBy(x => x.ReplayOperationIndex))
			{
				if (!payloadNames.Add(payload.PayloadFileName))
					throw new InvalidDataException("A retained scripted replay contains duplicate generated-payload file names.");
				string payloadDirectory = ScriptedFileSelectionCache.GetPayloadDirectoryPath(replayPath);
				Directory.CreateDirectory(payloadDirectory);
				Materialize(payload.RetainedArtifact, Path.Combine(payloadDirectory, payload.PayloadFileName), cancellationToken);
			}

			var cache = new ScriptedFileSelectionCache(replayPath);
			if (!cache.HasCompleteReplay)
				throw new InvalidDataException("The retained scripted replay XML no longer declares the complete replay format.");
			IReadOnlyList<ScriptedReplayOperation> operations = cache.LoadReplayOperations() ?? new ScriptedReplayOperation[0];
			if (operations.Count != artifactSet.ReplayOperationCount)
				throw new InvalidDataException("The retained scripted replay operation count differs from the sealed capture.");
			int generatedCount = operations.Count(x => x.Kind == ScriptedReplayOperationKind.GeneratedFile);
			if (generatedCount != artifactSet.GeneratedPayloads.Count)
				throw new InvalidDataException("The retained scripted replay generated-payload count differs from the sealed capture.");
			foreach (CollectionScriptedGeneratedPayload payload in artifactSet.GeneratedPayloads)
			{
				if (payload.ReplayOperationIndex < 0 || payload.ReplayOperationIndex >= operations.Count)
					throw new InvalidDataException("A retained generated payload references an invalid replay operation index.");
				ScriptedReplayOperation operation = operations[payload.ReplayOperationIndex];
				if (operation.Kind != ScriptedReplayOperationKind.GeneratedFile ||
					!StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(operation.PayloadPath), payload.PayloadFileName) ||
					!StringComparer.Ordinal.Equals(operation.DestinationPath ?? String.Empty, payload.DestinationPath ?? String.Empty) ||
					operation.PayloadLength != payload.RetainedArtifact.ByteLength ||
					!StringComparer.OrdinalIgnoreCase.Equals(operation.PayloadHash, payload.RetainedArtifact.ContentHash.Value))
					throw new InvalidDataException("A retained generated payload no longer matches its validated replay operation metadata.");
			}
			return replayPath;
		}

		private void Materialize(RetainedArtifactReference retained, string destination, CancellationToken cancellationToken)
		{
			ValidateRetainedReference(retained, cancellationToken);
			string directory = Path.GetDirectoryName(destination);
			if (!String.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
			using (Stream source = _artifactStore.OpenRead(retained.StableArtifactId))
			using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize))
			{
				var buffer = new byte[CopyBufferSize];
				int read;
				while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
				{
					cancellationToken.ThrowIfCancellationRequested();
					output.Write(buffer, 0, read);
				}
				output.Flush(true);
			}
		}

		private static void ValidateDesiredState(CollectionScriptedReplayArtifactSet artifactSet,
			CollectionLocalRestoreReplayArtifactState desired)
		{
			if (!desired.ReplayFile.Exists || desired.ReplayFile.ByteLength != artifactSet.ReplayXml.ByteLength ||
				!StringComparer.OrdinalIgnoreCase.Equals(desired.ReplayFile.Sha256, artifactSet.ReplayXml.ContentHash.Value))
				throw new InvalidDataException("The materialized replay XML differs from its sealed retained identity.");
			if (desired.Payloads.Count != artifactSet.GeneratedPayloads.Count)
				throw new InvalidDataException("The materialized generated-payload set differs from the sealed replay capture.");
			foreach (CollectionScriptedGeneratedPayload payload in artifactSet.GeneratedPayloads)
			{
				CollectionLocalRestoreReplayPayloadState state = desired.Payloads.SingleOrDefault(x =>
					StringComparer.OrdinalIgnoreCase.Equals(x.RelativePath, payload.PayloadFileName));
				if (state == null || state.File.ByteLength != payload.RetainedArtifact.ByteLength ||
					!StringComparer.OrdinalIgnoreCase.Equals(state.File.Sha256, payload.RetainedArtifact.ContentHash.Value))
					throw new InvalidDataException("A materialized generated replay payload differs from its sealed retained identity.");
			}
		}

		private ReplayIntent CreateIntent(CollectionSealedCaptureSnapshot sealedCapture, CollectionLocalRestorePlan plan,
			ReplayJob job, CollectionLocalRestoreReplayArtifactState preimage, CollectionLocalRestoreReplayArtifactState desired)
		{
			return new ReplayIntent(sealedCapture.Capture.Identity.ToString(), plan.PlanFingerprint,
				job.ArtifactSet.NativeSnapshotKey, job.CurrentNativeKey, job.NativeFileName,
				job.ArtifactSet.ReplayXml.StableArtifactId,
				job.ArtifactSet.GeneratedPayloads.OrderBy(x => x.ReplayOperationIndex)
					.Select(x => new ReplayPayloadIdentity(x.ReplayOperationIndex, x.PayloadFileName, x.RetainedArtifact.StableArtifactId)),
				preimage, desired);
		}

		private CollectionsRetainedArtifactReferenceRecord PersistIntent(string ownerId, string role, ReplayIntent intent)
		{
			CollectionsRetainedArtifact artifact;
			using (var stream = new MemoryStream(SerializeIntent(intent), false)) artifact = _artifactStore.Publish(stream);
			return _referenceStore.AcquireExclusiveRoleReference(artifact.ArtifactId,
				CollectionsRetainedArtifactOwnerKind.Operation, ownerId, role);
		}

		private void MarkVerified(string ownerId, string role, string artifactId)
		{
			_referenceStore.AcquireExclusiveRoleReference(artifactId, CollectionsRetainedArtifactOwnerKind.Operation, ownerId, role);
		}

		private void ValidateIntent(ReplayIntent intent, CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan plan, ReplayJob job, CollectionLocalRestoreReplayArtifactState desired)
		{
			ReplayPayloadIdentity[] expectedPayloads = job.ArtifactSet.GeneratedPayloads.OrderBy(x => x.ReplayOperationIndex)
				.Select(x => new ReplayPayloadIdentity(x.ReplayOperationIndex, x.PayloadFileName, x.RetainedArtifact.StableArtifactId)).ToArray();
			if (intent == null || !StringComparer.Ordinal.Equals(intent.CaptureId, sealedCapture.Capture.Identity.ToString()) ||
				!StringComparer.Ordinal.Equals(intent.PlanFingerprint, plan.PlanFingerprint) ||
				!StringComparer.OrdinalIgnoreCase.Equals(intent.CapturedNativeKey, job.ArtifactSet.NativeSnapshotKey) ||
				!StringComparer.OrdinalIgnoreCase.Equals(intent.CurrentNativeKey, job.CurrentNativeKey) ||
				!StringComparer.OrdinalIgnoreCase.Equals(intent.NativeFileName, job.NativeFileName) ||
				!StringComparer.Ordinal.Equals(intent.ReplayArtifactId, job.ArtifactSet.ReplayXml.StableArtifactId) ||
				!ReplayPayloadIdentity.SequenceEquals(intent.Payloads, expectedPayloads) || !intent.Desired.Equals(desired))
				throw new InvalidDataException("The durable replay restore intent differs from the reviewed sealed Local Collection restore.");
		}

		private ReplayIntent ReadIntent(string artifactId)
		{
			if (!_artifactStore.VerifyArtifact(artifactId))
				throw new InvalidDataException("The durable replay restore intent no longer matches its retained bytes.");
			using (Stream stream = _artifactStore.OpenRead(artifactId))
			using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, false))
			{
				JObject root = JObject.Parse(reader.ReadToEnd());
				if (!StringComparer.Ordinal.Equals((string)root["format"], IntentFormat))
					throw new InvalidDataException("Unsupported Local restore replay-intent format.");
				return new ReplayIntent((string)root["captureId"], (string)root["planFingerprint"],
					(string)root["capturedNativeKey"], (string)root["currentNativeKey"], (string)root["nativeFileName"],
					(string)root["replayArtifactId"], ReadPayloadIdentities((JArray)root["payloads"]),
					ReadArtifactState((JObject)root["preimage"]), ReadArtifactState((JObject)root["desired"]));
			}
		}

		private static byte[] SerializeIntent(ReplayIntent intent)
		{
			using (var stream = new MemoryStream())
			using (var text = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
			using (var writer = new JsonTextWriter(text))
			{
				writer.WriteStartObject();
				writer.WritePropertyName("format"); writer.WriteValue(IntentFormat);
				writer.WritePropertyName("captureId"); writer.WriteValue(intent.CaptureId);
				writer.WritePropertyName("planFingerprint"); writer.WriteValue(intent.PlanFingerprint);
				writer.WritePropertyName("capturedNativeKey"); writer.WriteValue(intent.CapturedNativeKey);
				writer.WritePropertyName("currentNativeKey"); writer.WriteValue(intent.CurrentNativeKey);
				writer.WritePropertyName("nativeFileName"); writer.WriteValue(intent.NativeFileName);
				writer.WritePropertyName("replayArtifactId"); writer.WriteValue(intent.ReplayArtifactId);
				writer.WritePropertyName("payloads"); writer.WriteStartArray();
				foreach (ReplayPayloadIdentity payload in intent.Payloads)
				{
					writer.WriteStartObject();
					writer.WritePropertyName("operationIndex"); writer.WriteValue(payload.OperationIndex);
					writer.WritePropertyName("fileName"); writer.WriteValue(payload.FileName);
					writer.WritePropertyName("artifactId"); writer.WriteValue(payload.ArtifactId);
					writer.WriteEndObject();
				}
				writer.WriteEndArray();
				writer.WritePropertyName("preimage"); WriteArtifactState(writer, intent.Preimage);
				writer.WritePropertyName("desired"); WriteArtifactState(writer, intent.Desired);
				writer.WriteEndObject(); writer.Flush(); text.Flush();
				return stream.ToArray();
			}
		}

		private CollectionOperation CompleteReplayPhase(CollectionOperation operation, LocalCaptureIdentity captureIdentity,
			CollectionLocalRestorePlan plan, int memberCount)
		{
			string ownerId = operation.Identity.OperationId.ToString("D");
			CollectionsRetainedArtifactReferenceRecord existing = _referenceStore.GetReferenceForOwnerRole(
				CollectionsRetainedArtifactOwnerKind.Operation, ownerId, CompleteRole);
			ReplayPhaseCompletion completion;
			if (existing == null)
			{
				CollectionOperation current = _operationStore.GetOperation(operation.Identity) ?? operation;
				completion = new ReplayPhaseCompletion(captureIdentity.ToString(), plan.PlanFingerprint, ownerId,
					current.CheckpointSequence, memberCount);
				CollectionsRetainedArtifact artifact;
				using (var stream = new MemoryStream(SerializePhaseCompletion(completion), false)) artifact = _artifactStore.Publish(stream);
				_referenceStore.AcquireExclusiveRoleReference(artifact.ArtifactId,
					CollectionsRetainedArtifactOwnerKind.Operation, ownerId, CompleteRole);
			}
			else
			{
				try { completion = ReadPhaseCompletion(existing.ArtifactId); }
				catch { MarkRecoveryRequired(operation); throw; }
				if (!StringComparer.Ordinal.Equals(completion.CaptureId, captureIdentity.ToString()) ||
					!StringComparer.Ordinal.Equals(completion.PlanFingerprint, plan.PlanFingerprint) ||
					!StringComparer.Ordinal.Equals(completion.OperationId, ownerId) || completion.MemberCount != memberCount)
				{
					MarkRecoveryRequired(operation);
					throw new InvalidDataException("The durable replay phase-completion marker differs from the reviewed restore.");
				}
			}

			CollectionOperation persisted = _operationStore.GetOperation(operation.Identity) ?? operation;
			if (persisted.CheckpointSequence == completion.CheckpointBefore)
				return AdvanceSafeBoundary(persisted);
			if (persisted.CheckpointSequence == completion.CheckpointBefore + 1 &&
				persisted.Phase == CollectionOperationPhase.PausedAtSafeBoundary &&
				persisted.ResultState == CollectionOperationResultState.Pending)
				return persisted;
			MarkRecoveryRequired(persisted);
			throw new InvalidOperationException("The replay phase-completion checkpoint no longer matches the durable operation journal.");
		}

		private static byte[] SerializePhaseCompletion(ReplayPhaseCompletion completion)
		{
			using (var stream = new MemoryStream())
			using (var text = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
			using (var writer = new JsonTextWriter(text))
			{
				writer.WriteStartObject();
				writer.WritePropertyName("format"); writer.WriteValue("nmm-ce.collections.local-restore-replay-phase/1");
				writer.WritePropertyName("captureId"); writer.WriteValue(completion.CaptureId);
				writer.WritePropertyName("planFingerprint"); writer.WriteValue(completion.PlanFingerprint);
				writer.WritePropertyName("operationId"); writer.WriteValue(completion.OperationId);
				writer.WritePropertyName("checkpointBefore"); writer.WriteValue(completion.CheckpointBefore);
				writer.WritePropertyName("memberCount"); writer.WriteValue(completion.MemberCount);
				writer.WriteEndObject(); writer.Flush(); text.Flush();
				return stream.ToArray();
			}
		}

		private ReplayPhaseCompletion ReadPhaseCompletion(string artifactId)
		{
			if (!_artifactStore.VerifyArtifact(artifactId))
				throw new InvalidDataException("The durable replay phase-completion marker no longer matches its retained bytes.");
			using (Stream stream = _artifactStore.OpenRead(artifactId))
			using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, false))
			{
				JObject root = JObject.Parse(reader.ReadToEnd());
				if (!StringComparer.Ordinal.Equals((string)root["format"], "nmm-ce.collections.local-restore-replay-phase/1"))
					throw new InvalidDataException("Unsupported Local restore replay phase-completion format.");
				return new ReplayPhaseCompletion((string)root["captureId"], (string)root["planFingerprint"],
					(string)root["operationId"], (long)root["checkpointBefore"], (int)root["memberCount"]);
			}
		}

		private CollectionOperation AdvanceSafeBoundary(CollectionOperation operation)
		{
			CollectionOperation current = _operationStore.GetOperation(operation.Identity) ?? operation;
			CollectionOperation updated = new CollectionOperation(current.Identity, current.Kind, current.Collection, current.Target,
				current.Revision, current.PlanIdentity, current.CheckpointSequence + 1, CollectionOperationPhase.PausedAtSafeBoundary,
				CollectionOperationResultState.Pending, current.NativeChildren);
			_operationStore.SaveOperation(updated);
			return updated;
		}

		private void MarkRecoveryRequired(CollectionOperation operation)
		{
			CollectionOperation current = _operationStore.GetOperation(operation.Identity) ?? operation;
			if (current.IsTerminal || current.Phase == CollectionOperationPhase.RecoveryRequired)
				return;
			CollectionOperation updated = new CollectionOperation(current.Identity, current.Kind, current.Collection, current.Target,
				current.Revision, current.PlanIdentity, current.CheckpointSequence + 1, CollectionOperationPhase.RecoveryRequired,
				CollectionOperationResultState.RecoveryRequired, current.NativeChildren);
			_operationStore.SaveOperation(updated);
		}

		private static ReplayPayloadIdentity[] ReadPayloadIdentities(JArray array)
		{
			if (array == null) throw new InvalidDataException("A replay intent is missing its generated-payload identities.");
			return array.OfType<JObject>().Select(x => new ReplayPayloadIdentity((int)x["operationIndex"],
				(string)x["fileName"], (string)x["artifactId"])).ToArray();
		}

		private static void WriteArtifactState(JsonWriter writer, CollectionLocalRestoreReplayArtifactState state)
		{
			writer.WriteStartObject();
			writer.WritePropertyName("replayFile"); WriteFileState(writer, state.ReplayFile);
			writer.WritePropertyName("payloadDirectoryExists"); writer.WriteValue(state.PayloadDirectoryExists);
			writer.WritePropertyName("payloads"); writer.WriteStartArray();
			foreach (CollectionLocalRestoreReplayPayloadState payload in state.Payloads)
			{
				writer.WriteStartObject();
				writer.WritePropertyName("relativePath"); writer.WriteValue(payload.RelativePath);
				writer.WritePropertyName("file"); WriteFileState(writer, payload.File);
				writer.WriteEndObject();
			}
			writer.WriteEndArray(); writer.WriteEndObject();
		}

		private static CollectionLocalRestoreReplayArtifactState ReadArtifactState(JObject value)
		{
			if (value == null) throw new InvalidDataException("A replay intent is missing one exact artifact state.");
			JArray payloads = value["payloads"] as JArray;
			if (payloads == null) throw new InvalidDataException("A replay intent is missing its payload state sequence.");
			return new CollectionLocalRestoreReplayArtifactState(ReadFileState(value["replayFile"] as JObject),
				(bool)value["payloadDirectoryExists"], payloads.OfType<JObject>().Select(x =>
					new CollectionLocalRestoreReplayPayloadState((string)x["relativePath"], ReadFileState(x["file"] as JObject))));
		}

		private static void WriteFileState(JsonWriter writer, CollectionLocalRestoreReplayFileState state)
		{
			writer.WriteStartObject();
			writer.WritePropertyName("exists"); writer.WriteValue(state.Exists);
			writer.WritePropertyName("byteLength"); writer.WriteValue(state.ByteLength);
			writer.WritePropertyName("sha256"); writer.WriteValue(state.Sha256);
			writer.WriteEndObject();
		}

		private static CollectionLocalRestoreReplayFileState ReadFileState(JObject value)
		{
			if (value == null) throw new InvalidDataException("A replay intent is missing one file-state record.");
			return new CollectionLocalRestoreReplayFileState((bool)value["exists"], (long)value["byteLength"], (string)value["sha256"]);
		}

		private static string CreateToken(string value)
		{
			using (SHA256 sha = SHA256.Create())
			{
				byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes((value ?? String.Empty).ToUpperInvariant()));
				return BitConverter.ToString(bytes).Replace("-", String.Empty).ToLowerInvariant().Substring(0, 24);
			}
		}

		private static string CreateTemporaryDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "nmm-c710b2-replay-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}

		private static void TryDeleteDirectory(string path)
		{
			if (String.IsNullOrWhiteSpace(path)) return;
			try { if (Directory.Exists(path)) Directory.Delete(path, true); }
			catch { }
		}

		private sealed class ReplayJob
		{
			internal ReplayJob(CollectionScriptedReplayArtifactSet artifactSet, CollectionLocalRestoreMemberPlan member,
				string currentNativeKey, string nativeFileName, string destinationReplayPath)
			{
				ArtifactSet = artifactSet; Member = member; CurrentNativeKey = currentNativeKey;
				NativeFileName = nativeFileName; DestinationReplayPath = destinationReplayPath;
			}
			internal CollectionScriptedReplayArtifactSet ArtifactSet { get; }
			internal CollectionLocalRestoreMemberPlan Member { get; }
			internal string CurrentNativeKey { get; }
			internal string NativeFileName { get; }
			internal string DestinationReplayPath { get; }
		}

		private sealed class ReplayIntent
		{
			internal ReplayIntent(string captureId, string planFingerprint, string capturedNativeKey, string currentNativeKey,
				string nativeFileName, string replayArtifactId, IEnumerable<ReplayPayloadIdentity> payloads,
				CollectionLocalRestoreReplayArtifactState preimage, CollectionLocalRestoreReplayArtifactState desired)
			{
				CaptureId = captureId ?? String.Empty; PlanFingerprint = planFingerprint ?? String.Empty;
				CapturedNativeKey = capturedNativeKey ?? String.Empty; CurrentNativeKey = currentNativeKey ?? String.Empty;
				NativeFileName = nativeFileName ?? String.Empty; ReplayArtifactId = replayArtifactId ?? String.Empty;
				Payloads = (payloads ?? new ReplayPayloadIdentity[0]).ToArray();
				Preimage = preimage ?? throw new ArgumentNullException(nameof(preimage));
				Desired = desired ?? throw new ArgumentNullException(nameof(desired));
			}
			internal string CaptureId { get; }
			internal string PlanFingerprint { get; }
			internal string CapturedNativeKey { get; }
			internal string CurrentNativeKey { get; }
			internal string NativeFileName { get; }
			internal string ReplayArtifactId { get; }
			internal ReplayPayloadIdentity[] Payloads { get; }
			internal CollectionLocalRestoreReplayArtifactState Preimage { get; }
			internal CollectionLocalRestoreReplayArtifactState Desired { get; }
		}

		private sealed class ReplayPayloadIdentity
		{
			internal ReplayPayloadIdentity(int operationIndex, string fileName, string artifactId)
			{
				OperationIndex = operationIndex; FileName = fileName ?? String.Empty; ArtifactId = artifactId ?? String.Empty;
			}
			internal int OperationIndex { get; }
			internal string FileName { get; }
			internal string ArtifactId { get; }

			internal static bool SequenceEquals(IEnumerable<ReplayPayloadIdentity> left, IEnumerable<ReplayPayloadIdentity> right)
			{
				ReplayPayloadIdentity[] a = (left ?? new ReplayPayloadIdentity[0]).ToArray();
				ReplayPayloadIdentity[] b = (right ?? new ReplayPayloadIdentity[0]).ToArray();
				if (a.Length != b.Length) return false;
				for (int index = 0; index < a.Length; index++)
					if (a[index].OperationIndex != b[index].OperationIndex ||
						!StringComparer.OrdinalIgnoreCase.Equals(a[index].FileName, b[index].FileName) ||
						!StringComparer.Ordinal.Equals(a[index].ArtifactId, b[index].ArtifactId)) return false;
				return true;
			}
		}

		private sealed class ReplayPhaseCompletion
		{
			internal ReplayPhaseCompletion(string captureId, string planFingerprint, string operationId,
				long checkpointBefore, int memberCount)
			{
				CaptureId = captureId; PlanFingerprint = planFingerprint; OperationId = operationId;
				CheckpointBefore = checkpointBefore; MemberCount = memberCount;
			}
			internal string CaptureId { get; }
			internal string PlanFingerprint { get; }
			internal string OperationId { get; }
			internal long CheckpointBefore { get; }
			internal int MemberCount { get; }
		}
	}

	/// <summary>Exact hash/length state of one replay or generated-payload file.</summary>
	internal sealed class CollectionLocalRestoreReplayFileState : IEquatable<CollectionLocalRestoreReplayFileState>
	{
		internal CollectionLocalRestoreReplayFileState(bool exists, long byteLength, string sha256)
		{
			if (byteLength < 0) throw new ArgumentOutOfRangeException(nameof(byteLength));
			Exists = exists; ByteLength = exists ? byteLength : 0; Sha256 = exists ? (sha256 ?? String.Empty) : String.Empty;
			if (Exists && String.IsNullOrWhiteSpace(Sha256)) throw new ArgumentException("An existing replay file state requires a SHA-256 hash.", nameof(sha256));
		}
		internal bool Exists { get; }
		internal long ByteLength { get; }
		internal string Sha256 { get; }
		public bool Equals(CollectionLocalRestoreReplayFileState other)
		{
			return other != null && Exists == other.Exists && ByteLength == other.ByteLength &&
				StringComparer.OrdinalIgnoreCase.Equals(Sha256, other.Sha256);
		}
		public override bool Equals(object obj) { return Equals(obj as CollectionLocalRestoreReplayFileState); }
		public override int GetHashCode() { return (Exists ? 1 : 0) ^ ByteLength.GetHashCode() ^ StringComparer.OrdinalIgnoreCase.GetHashCode(Sha256 ?? String.Empty); }
	}

	/// <summary>Exact relative-path/hash state of one generated-payload sidecar.</summary>
	internal sealed class CollectionLocalRestoreReplayPayloadState
	{
		internal CollectionLocalRestoreReplayPayloadState(string relativePath, CollectionLocalRestoreReplayFileState file)
		{
			if (String.IsNullOrWhiteSpace(relativePath)) throw new ArgumentException("A replay payload relative path is required.", nameof(relativePath));
			RelativePath = relativePath.Replace('\\', '/'); File = file ?? throw new ArgumentNullException(nameof(file));
		}
		internal string RelativePath { get; }
		internal CollectionLocalRestoreReplayFileState File { get; }
	}

	/// <summary>Exact replay XML + payload-sidecar tree state used for durable pre/postimage reconciliation.</summary>
	internal sealed class CollectionLocalRestoreReplayArtifactState : IEquatable<CollectionLocalRestoreReplayArtifactState>
	{
		private readonly ReadOnlyCollection<CollectionLocalRestoreReplayPayloadState> _payloads;
		internal CollectionLocalRestoreReplayArtifactState(CollectionLocalRestoreReplayFileState replayFile,
			bool payloadDirectoryExists, IEnumerable<CollectionLocalRestoreReplayPayloadState> payloads)
		{
			ReplayFile = replayFile ?? throw new ArgumentNullException(nameof(replayFile));
			PayloadDirectoryExists = payloadDirectoryExists;
			List<CollectionLocalRestoreReplayPayloadState> copied = (payloads ?? throw new ArgumentNullException(nameof(payloads)))
				.OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.RelativePath, StringComparer.Ordinal).ToList();
			if (copied.GroupBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() != 1))
				throw new ArgumentException("A replay artifact state cannot contain duplicate payload paths.", nameof(payloads));
			_payloads = new ReadOnlyCollection<CollectionLocalRestoreReplayPayloadState>(copied);
		}
		internal CollectionLocalRestoreReplayFileState ReplayFile { get; }
		internal bool PayloadDirectoryExists { get; }
		internal ReadOnlyCollection<CollectionLocalRestoreReplayPayloadState> Payloads { get { return _payloads; } }
		public bool Equals(CollectionLocalRestoreReplayArtifactState other)
		{
			if (other == null || !ReplayFile.Equals(other.ReplayFile) || PayloadDirectoryExists != other.PayloadDirectoryExists ||
				Payloads.Count != other.Payloads.Count) return false;
			for (int index = 0; index < Payloads.Count; index++)
				if (!StringComparer.OrdinalIgnoreCase.Equals(Payloads[index].RelativePath, other.Payloads[index].RelativePath) ||
					!Payloads[index].File.Equals(other.Payloads[index].File)) return false;
			return true;
		}
		public override bool Equals(object obj) { return Equals(obj as CollectionLocalRestoreReplayArtifactState); }
		public override int GetHashCode() { return ReplayFile.GetHashCode() ^ PayloadDirectoryExists.GetHashCode() ^ Payloads.Count; }
	}

	/// <summary>Filesystem helper for exact replay pre/postimage capture and bounded restart reconciliation.</summary>
	internal static class CollectionLocalRestoreReplayFileSystem
	{
		private const int BufferSize = 81920;

		internal static CollectionLocalRestoreReplayArtifactState Capture(string replayPath, CancellationToken cancellationToken)
		{
			if (String.IsNullOrWhiteSpace(replayPath)) throw new ArgumentException("A replay path is required.", nameof(replayPath));
			string fullReplay = Path.GetFullPath(replayPath);
			CollectionLocalRestoreReplayFileState replay = CaptureFile(fullReplay, cancellationToken);
			string payloadDirectory = ScriptedFileSelectionCache.GetPayloadDirectoryPath(fullReplay);
			bool directoryExists = Directory.Exists(payloadDirectory);
			var payloads = new List<CollectionLocalRestoreReplayPayloadState>();
			if (directoryExists)
			{
				string root = Path.GetFullPath(payloadDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
				foreach (string file in Directory.GetFiles(payloadDirectory, "*", SearchOption.AllDirectories)
					.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ThenBy(x => x, StringComparer.Ordinal))
				{
					cancellationToken.ThrowIfCancellationRequested();
					string full = Path.GetFullPath(file);
					if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
						throw new InvalidDataException("A replay payload escaped its expected sidecar directory.");
					string relative = full.Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
					payloads.Add(new CollectionLocalRestoreReplayPayloadState(relative, CaptureFile(full, cancellationToken)));
				}
			}
			return new CollectionLocalRestoreReplayArtifactState(replay, directoryExists, payloads);
		}

		internal static bool IsSafeTransition(CollectionLocalRestoreReplayArtifactState live,
			CollectionLocalRestoreReplayArtifactState preimage, CollectionLocalRestoreReplayArtifactState desired)
		{
			if (live == null || preimage == null || desired == null) return false;
			bool replayIsPreimage = live.ReplayFile.Equals(preimage.ReplayFile);
			bool replayIsDesired = live.ReplayFile.Equals(desired.ReplayFile);
			if (!replayIsPreimage && !replayIsDesired) return false;
			if (PayloadTreeEquals(live, preimage) || PayloadTreeEquals(live, desired)) return true;
			if (!replayIsDesired) return false;
			if (!live.PayloadDirectoryExists) return true;

			Dictionary<string, CollectionLocalRestoreReplayFileState> desiredFiles = desired.Payloads.ToDictionary(
				x => x.RelativePath, x => x.File, StringComparer.OrdinalIgnoreCase);
			foreach (CollectionLocalRestoreReplayPayloadState payload in live.Payloads)
			{
				CollectionLocalRestoreReplayFileState expected;
				if (!desiredFiles.TryGetValue(payload.RelativePath, out expected) || !payload.File.Equals(expected)) return false;
			}
			return true;
		}

		internal static void ReplaceArtifacts(string sourceReplayPath, string destinationReplayPath)
		{
			ScriptedFileSelectionCache.CopyArtifacts(sourceReplayPath, destinationReplayPath);
		}

		private static bool PayloadTreeEquals(CollectionLocalRestoreReplayArtifactState left,
			CollectionLocalRestoreReplayArtifactState right)
		{
			if (left.PayloadDirectoryExists != right.PayloadDirectoryExists || left.Payloads.Count != right.Payloads.Count) return false;
			for (int index = 0; index < left.Payloads.Count; index++)
				if (!StringComparer.OrdinalIgnoreCase.Equals(left.Payloads[index].RelativePath, right.Payloads[index].RelativePath) ||
					!left.Payloads[index].File.Equals(right.Payloads[index].File)) return false;
			return true;
		}

		private static CollectionLocalRestoreReplayFileState CaptureFile(string path, CancellationToken cancellationToken)
		{
			if (!File.Exists(path)) return new CollectionLocalRestoreReplayFileState(false, 0, String.Empty);
			using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan))
			using (SHA256 sha = SHA256.Create())
			{
				var buffer = new byte[BufferSize];
				long length = 0;
				int read;
				while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
				{
					cancellationToken.ThrowIfCancellationRequested();
					sha.TransformBlock(buffer, 0, read, null, 0); length += read;
				}
				sha.TransformFinalBlock(new byte[0], 0, 0);
				return new CollectionLocalRestoreReplayFileState(true, length,
					BitConverter.ToString(sha.Hash).Replace("-", String.Empty).ToLowerInvariant());
			}
		}
	}
}
