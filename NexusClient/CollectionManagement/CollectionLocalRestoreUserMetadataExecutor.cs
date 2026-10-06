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
using Newtonsoft.Json.Linq;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement;
using Nexus.Client.Mods;
using Nexus.Client.Mods.Formats.FOMod;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Describes how the C7.10b6 logical user-metadata phase reached its verified live state.</summary>
	public enum CollectionLocalRestoreUserMetadataOutcome
	{
		NotApplicable = 1,
		AlreadySatisfied = 2,
		RestoredAndVerified = 3,
		RecoveredCommitted = 4
	}

	/// <summary>Result of the C7.10b6 logical user-metadata phase; the profile boundary remains pending.</summary>
	public sealed class CollectionLocalRestoreUserMetadataExecutionResult
	{
		internal CollectionLocalRestoreUserMetadataExecutionResult(CollectionOperation operation, CollectionLocalRestoreUserMetadataOutcome outcome)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Outcome = outcome;
		}

		public CollectionOperation Operation { get; }
		public CollectionLocalRestoreUserMetadataOutcome Outcome { get; }
		public bool IsReadyForProfileReconciliation
		{
			get { return Operation.Phase == CollectionOperationPhase.PausedAtSafeBoundary && !Operation.HasUnreconciledNativeChild; }
		}
	}

	/// <summary>Restores captured logical Sort assignments and path-scoped FOMod screenshot overrides without replacing shared databases.</summary>
	public sealed class CollectionLocalRestoreUserMetadataExecutor
	{
		private const string IntentFormat = "nmm-ce.collections.local-restore-user-metadata-intent/1";
		private const string IntentRole = "local-restore-user-metadata-intent-v1";
		private const string VerifiedRole = "local-restore-user-metadata-verified-v1";
		private const string CompleteRole = "local-restore-user-metadata-phase-complete-v1";
		private const int CopyBufferSize = 81920;

		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionsRetainedArtifactReferenceStore _referenceStore;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;

		public CollectionLocalRestoreUserMetadataExecutor(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore)
			: this(services, gameStorageService, operationStore, artifactStore, referenceStore,
				CollectionTargetMutationLeaseManager.Shared, new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services))
		{
		}

		internal CollectionLocalRestoreUserMetadataExecutor(ServiceManager services, GameStorageService gameStorageService,
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
			if (_services.ModManager == null)
				throw new InvalidOperationException("Logical user-metadata restoration requires the live native ModManager.");
		}

		public Task<CollectionLocalRestoreUserMetadataExecutionResult> ExecuteAsync(CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan reviewedPlan, CollectionLocalRestoreMemberExecutionResult memberPhase,
			CollectionLocalRestoreGameValueExecutionResult gameValuePhase, GameStoragePathSet paths)
		{
			return ExecuteAsync(sealedCapture, reviewedPlan, memberPhase, gameValuePhase, paths, CancellationToken.None);
		}

		public async Task<CollectionLocalRestoreUserMetadataExecutionResult> ExecuteAsync(CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan reviewedPlan, CollectionLocalRestoreMemberExecutionResult memberPhase,
			CollectionLocalRestoreGameValueExecutionResult gameValuePhase, GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			ValidateInputs(sealedCapture, reviewedPlan, memberPhase, gameValuePhase, paths);
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(reviewedPlan.Target))
				throw new InvalidOperationException("The live canonical target no longer matches the reviewed Local Collection restore.");

			using (CollectionTargetMutationLease rootLease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				_authorityValidator.ValidateAndReload(rootLease, authority, paths);
				CollectionOperation operation = RequireOperation(gameValuePhase.Operation.Identity, reviewedPlan);
				bool applicable = sealedCapture.Capture.Scope.Contains(LocalCaptureScopeArea.UserMetadata);
				CollectionLocalRestoreUserMetadataOutcome outcome;
				CollectionLocalRestoreUserMetadataState desired = BuildDesiredState(sealedCapture, reviewedPlan, memberPhase, applicable);

				if (!applicable)
				{
					outcome = CollectionLocalRestoreUserMetadataOutcome.NotApplicable;
				}
				else
				{
					string ownerId = operation.Identity.OperationId.ToString("D");
					CollectionLocalRestoreUserMetadataState live = CaptureCurrentState(desired);
					CollectionsRetainedArtifactReferenceRecord verified = _referenceStore.GetReferenceForOwnerRole(
						CollectionsRetainedArtifactOwnerKind.Operation, ownerId, VerifiedRole);
					if (verified != null)
					{
						UserMetadataIntent verifiedIntent = ReadIntent(verified.ArtifactId);
						ValidateIntent(verifiedIntent, sealedCapture, reviewedPlan, desired);
						if (!live.Equals(desired))
						{
							MarkRecoveryRequired(operation);
							throw new InvalidOperationException("A durable user-metadata verified checkpoint no longer matches authoritative logical metadata state.");
						}
						outcome = CollectionLocalRestoreUserMetadataOutcome.AlreadySatisfied;
					}
					else
					{
						CollectionsRetainedArtifactReferenceRecord existingIntent = _referenceStore.GetReferenceForOwnerRole(
							CollectionsRetainedArtifactOwnerKind.Operation, ownerId, IntentRole);
						UserMetadataIntent intent;
						if (existingIntent == null)
						{
							intent = new UserMetadataIntent(sealedCapture.Capture.Identity.ToString(), reviewedPlan.PlanFingerprint, live, desired);
							existingIntent = PersistIntent(ownerId, intent);
						}
						else
						{
							try
							{
								intent = ReadIntent(existingIntent.ArtifactId);
								ValidateIntent(intent, sealedCapture, reviewedPlan, desired);
							}
							catch
							{
								MarkRecoveryRequired(operation);
								throw;
							}
							if (live.Equals(desired))
							{
								MarkVerified(ownerId, existingIntent.ArtifactId);
								outcome = CollectionLocalRestoreUserMetadataOutcome.RecoveredCommitted;
								operation = CompleteUserMetadataPhase(operation, sealedCapture.Capture.Identity, reviewedPlan, desired.Entries.Count);
								return new CollectionLocalRestoreUserMetadataExecutionResult(operation, outcome);
							}
							if (!IsRecognizedTransition(live, intent.Preimage, desired))
							{
								MarkRecoveryRequired(operation);
								throw new InvalidOperationException("The live user-metadata state contains values outside both the exact preimage and reviewed restore postimage.");
							}
						}

						ApplyDesiredState(desired, cancellationToken);
						CollectionLocalRestoreUserMetadataState postimage = CaptureCurrentState(desired);
						if (!postimage.Equals(desired))
						{
							MarkRecoveryRequired(operation);
							throw new InvalidOperationException("Logical user-metadata restoration returned without establishing the exact captured Sort and screenshot state.");
						}
						MarkVerified(ownerId, existingIntent.ArtifactId);
						outcome = CollectionLocalRestoreUserMetadataOutcome.RestoredAndVerified;
					}
				}

				operation = CompleteUserMetadataPhase(operation, sealedCapture.Capture.Identity, reviewedPlan, desired.Entries.Count);
				return new CollectionLocalRestoreUserMetadataExecutionResult(operation, outcome);
			}
		}

		/// <summary>Verifies captured logical Sort/screenshot metadata without replacing or mutating the shared metadata database.</summary>
		internal void VerifyFinalState(CollectionSealedCaptureSnapshot sealedCapture, CollectionLocalRestorePlan reviewedPlan,
			CollectionLocalRestoreMemberExecutionResult memberPhase)
		{
			if (sealedCapture == null) throw new ArgumentNullException(nameof(sealedCapture));
			if (reviewedPlan == null) throw new ArgumentNullException(nameof(reviewedPlan));
			if (memberPhase == null) throw new ArgumentNullException(nameof(memberPhase));
			bool applicable = sealedCapture.Capture.Scope.Contains(LocalCaptureScopeArea.UserMetadata);
			CollectionLocalRestoreUserMetadataState desired = BuildDesiredState(sealedCapture, reviewedPlan, memberPhase, applicable);
			if (!applicable)
				return;
			if (!CaptureCurrentState(desired).Equals(desired))
				throw new InvalidOperationException("Final Local restore verification found logical user metadata that no longer matches the sealed capture.");
		}

		private void ValidateInputs(CollectionSealedCaptureSnapshot sealedCapture, CollectionLocalRestorePlan reviewedPlan,
			CollectionLocalRestoreMemberExecutionResult memberPhase, CollectionLocalRestoreGameValueExecutionResult gameValuePhase,
			GameStoragePathSet paths)
		{
			if (sealedCapture == null) throw new ArgumentNullException(nameof(sealedCapture));
			if (reviewedPlan == null) throw new ArgumentNullException(nameof(reviewedPlan));
			if (memberPhase == null) throw new ArgumentNullException(nameof(memberPhase));
			if (gameValuePhase == null) throw new ArgumentNullException(nameof(gameValuePhase));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			if (!reviewedPlan.IsReadyForReview || sealedCapture.Capture.Capability != LocalCaptureCapability.LocallyRestorableWithinScope)
				throw new InvalidOperationException("User-metadata restoration requires one reviewed locally-restorable C7.9 plan.");
			if (!sealedCapture.Capture.Identity.Equals(reviewedPlan.CaptureIdentity) || !sealedCapture.UserMetadata.Target.Equals(reviewedPlan.Target))
				throw new InvalidOperationException("User-metadata restoration inputs do not belong to the same Local Collection capture and target.");
			if (!gameValuePhase.IsReadyForUserMetadataReconciliation || gameValuePhase.Operation.Kind != CollectionOperationKind.RestoreLocalCapture ||
				!gameValuePhase.Operation.Target.Equals(reviewedPlan.Target) || !memberPhase.Operation.Identity.Equals(gameValuePhase.Operation.Identity))
				throw new InvalidOperationException("User-metadata restoration requires the verified game-specific safe boundary of the same restore operation.");
			if (sealedCapture.Capture.Scope.Contains(LocalCaptureScopeArea.UserMetadata))
			{
				if (sealedCapture.UserMetadata.SortCoverage != NativeStateCaptureCoverage.Complete &&
					sealedCapture.UserMetadata.SortCoverage != NativeStateCaptureCoverage.NotApplicable)
					throw new InvalidDataException("A locally-restorable user-metadata scope requires complete or non-applicable Sort coverage.");
				if (sealedCapture.UserMetadata.ScreenshotCoverage != NativeStateCaptureCoverage.Complete &&
					sealedCapture.UserMetadata.ScreenshotCoverage != NativeStateCaptureCoverage.NotApplicable)
					throw new InvalidDataException("A locally-restorable user-metadata scope requires complete or non-applicable screenshot coverage.");
			}
		}

		private CollectionOperation RequireOperation(CollectionOperationIdentity identity, CollectionLocalRestorePlan plan)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Kind != CollectionOperationKind.RestoreLocalCapture || operation.IsTerminal ||
				operation.ResultState != CollectionOperationResultState.Pending || operation.Phase != CollectionOperationPhase.PausedAtSafeBoundary ||
				!operation.Target.Equals(plan.Target) || operation.HasUnreconciledNativeChild)
				throw new InvalidOperationException("User-metadata restoration requires the exact active Local Collection restore operation at a reconciled safe boundary.");
			return operation;
		}

		private CollectionLocalRestoreUserMetadataState BuildDesiredState(CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan plan, CollectionLocalRestoreMemberExecutionResult memberPhase, bool applicable)
		{
			if (!applicable)
				return new CollectionLocalRestoreUserMetadataState(new CollectionLocalRestoreUserMetadataEntry[0]);

			Dictionary<string, CollectionLocalRestoreMemberPlan> planMembers = plan.Members.ToDictionary(x => x.CapturedNativeKey,
				x => x, StringComparer.OrdinalIgnoreCase);
			Dictionary<CollectionMemberKey, string> remaps = memberPhase.MemberRemaps.ToDictionary(x => x.MemberKey, x => x.NativeKey);
			Dictionary<string, CollectionCapturedSortAssignment> sorts = sealedCapture.UserMetadata.SortAssignments.ToDictionary(
				x => x.NativeSnapshotKey, StringComparer.OrdinalIgnoreCase);
			Dictionary<string, CollectionCapturedScreenshotOverride> screenshots = sealedCapture.UserMetadata.ScreenshotOverrides.ToDictionary(
				x => x.NativeSnapshotKey, StringComparer.OrdinalIgnoreCase);
			var entries = new List<CollectionLocalRestoreUserMetadataEntry>();

			foreach (CollectionInstalledModIdentity capturedMember in sealedCapture.InstalledIdentities.Mods.OrderBy(x => x.NativeSnapshotKey, StringComparer.OrdinalIgnoreCase))
			{
				CollectionLocalRestoreMemberPlan planMember;
				string nativeKey;
				if (!planMembers.TryGetValue(capturedMember.NativeSnapshotKey, out planMember) ||
					!remaps.TryGetValue(planMember.SnapshotMemberKey, out nativeKey))
					throw new InvalidOperationException("A captured user-metadata member has no verified current native-key remap.");
				IMod mod = RequireLiveMember(nativeKey);
				string archivePath = GetArchivePath(mod);

				CollectionLocalRestoreUserSortState sort = null;
				if (sealedCapture.UserMetadata.SortCoverage == NativeStateCaptureCoverage.Complete)
				{
					CollectionCapturedSortAssignment capturedSort;
					if (!sorts.TryGetValue(capturedMember.NativeSnapshotKey, out capturedSort))
						throw new InvalidDataException("Complete user-metadata capture is missing a Sort assignment for one installed member.");
					sort = new CollectionLocalRestoreUserSortState(true, capturedSort.SortNumber, capturedSort.AssignmentState,
						capturedSort.RecordedModId, capturedSort.RecordedDownloadId);
				}

				CollectionLocalRestoreUserScreenshotState screenshot = null;
				if (sealedCapture.UserMetadata.ScreenshotCoverage == NativeStateCaptureCoverage.Complete)
				{
					screenshot = CollectionLocalRestoreUserScreenshotState.None();
					CollectionCapturedScreenshotOverride capturedScreenshot;
					if (screenshots.TryGetValue(capturedMember.NativeSnapshotKey, out capturedScreenshot))
					{
						ValidateRetainedScreenshot(capturedScreenshot.RetainedArtifact);
						var archiveInfo = new FileInfo(archivePath);
						if (!archiveInfo.Exists)
							throw new FileNotFoundException("The restored archive required by captured screenshot metadata is unavailable.", archivePath);
						screenshot = CollectionLocalRestoreUserScreenshotState.CreatePresent(ModFormatScreenshotOverrideReadState.Current,
							capturedScreenshot.ScreenshotPath, capturedScreenshot.RetainedArtifact.ContentHash.Value,
							capturedScreenshot.RetainedArtifact.ByteLength, archiveInfo.Length, archiveInfo.LastWriteTimeUtc.Ticks,
							capturedScreenshot.UpdatedUtcTicks, capturedScreenshot.RetainedArtifact.StableArtifactId);
					}
				}
				entries.Add(new CollectionLocalRestoreUserMetadataEntry(capturedMember.NativeSnapshotKey, nativeKey, archivePath, sort, screenshot));
			}
			return new CollectionLocalRestoreUserMetadataState(entries);
		}

		private CollectionLocalRestoreUserMetadataState CaptureCurrentState(CollectionLocalRestoreUserMetadataState template)
		{
			ModSortOrderService sortService = _services.ModManager.SortOrderService;
			IModFormatUserMetadata userMetadata = GetUserMetadataFormat();
			var entries = new List<CollectionLocalRestoreUserMetadataEntry>();
			foreach (CollectionLocalRestoreUserMetadataEntry desiredEntry in template.Entries)
			{
				CollectionLocalRestoreUserSortState sort = null;
				if (desiredEntry.Sort != null)
				{
					if (sortService == null)
						throw new InvalidOperationException("The live Sort service is unavailable during exact user-metadata restoration.");
					ModSortOrderRecord currentSort;
					if (!sortService.TryGetResolvedAssignment(desiredEntry.ArchivePath, out currentSort))
						sort = new CollectionLocalRestoreUserSortState(false, null, ModSortOrderAssignmentState.BaselineBlank, null, null);
					else
						sort = new CollectionLocalRestoreUserSortState(true, currentSort.SortNumber, currentSort.AssignmentState,
							currentSort.ModId, currentSort.DownloadId);
				}

				CollectionLocalRestoreUserScreenshotState screenshot = null;
				if (desiredEntry.Screenshot != null)
				{
					screenshot = CollectionLocalRestoreUserScreenshotState.None();
					if (userMetadata == null || !userMetadata.IsUserMetadataUsable)
						throw new InvalidOperationException("The logical FOMod user-metadata reader is unavailable during exact screenshot restoration.");
					ModFormatScreenshotOverrideReadResult result = userMetadata.ReadScreenshotOverride(desiredEntry.ArchivePath);
					if (result.State != ModFormatScreenshotOverrideReadState.None)
					{
						ModFormatScreenshotOverrideRecord record = result.Record;
						byte[] bytes = record.ScreenshotData;
						screenshot = CollectionLocalRestoreUserScreenshotState.CreatePresent(result.State, record.ScreenshotPath,
							ComputeSha256(bytes), bytes.LongLength, record.ArchiveLength, record.ArchiveWriteTimeUtcTicks,
							record.UpdatedUtcTicks, null);
					}
				}
				entries.Add(new CollectionLocalRestoreUserMetadataEntry(desiredEntry.NativeSnapshotKey, desiredEntry.NativeKey,
					desiredEntry.ArchivePath, sort, screenshot));
			}
			return new CollectionLocalRestoreUserMetadataState(entries);
		}

		private void ApplyDesiredState(CollectionLocalRestoreUserMetadataState desired, CancellationToken cancellationToken)
		{
			ModSortOrderService sortService = _services.ModManager.SortOrderService;
			IModFormatUserMetadata userMetadata = GetUserMetadataFormat();
			foreach (CollectionLocalRestoreUserMetadataEntry entry in desired.Entries)
			{
				cancellationToken.ThrowIfCancellationRequested();
				IMod mod = RequireLiveMember(entry.NativeKey);
				if (entry.Sort != null)
				{
					if (sortService == null || !entry.Sort.Present)
						throw new InvalidOperationException("The exact desired Sort state cannot be applied by the current native Sort service.");
					sortService.RestoreResolvedAssignment(mod, entry.Sort.SortNumber, entry.Sort.AssignmentState,
						entry.Sort.RecordedModId, entry.Sort.RecordedDownloadId);
				}
				if (entry.Screenshot != null)
				{
					if (userMetadata == null || !userMetadata.IsUserMetadataUsable)
						throw new InvalidOperationException("The logical FOMod user-metadata writer is unavailable during exact screenshot restoration.");
					if (!entry.Screenshot.Present)
						userMetadata.RemoveScreenshotOverride(entry.ArchivePath);
					else
					{
						byte[] bytes = ReadRetainedScreenshot(entry.Screenshot.RetainedArtifactId, entry.Screenshot.ContentHash,
							entry.Screenshot.ByteLength, cancellationToken);
						userMetadata.RestoreScreenshotOverride(entry.ArchivePath, entry.Screenshot.ScreenshotPath, bytes, entry.Screenshot.UpdatedUtcTicks);
					}
				}
			}
		}

		private IModFormatUserMetadata GetUserMetadataFormat()
		{
			return _services.ModManager.ModFormats
				.FirstOrDefault(x => String.Equals(x.Id, "FOMod", StringComparison.OrdinalIgnoreCase)) as IModFormatUserMetadata;
		}

		internal static bool IsRecognizedTransition(CollectionLocalRestoreUserMetadataState live,
			CollectionLocalRestoreUserMetadataState preimage, CollectionLocalRestoreUserMetadataState desired)
		{
			if (live == null || preimage == null || desired == null || live.Entries.Count != desired.Entries.Count || preimage.Entries.Count != desired.Entries.Count)
				return false;
			for (int index = 0; index < desired.Entries.Count; index++)
			{
				CollectionLocalRestoreUserMetadataEntry current = live.Entries[index];
				CollectionLocalRestoreUserMetadataEntry before = preimage.Entries[index];
				CollectionLocalRestoreUserMetadataEntry after = desired.Entries[index];
				if (!current.HasSameBinding(after) || !before.HasSameBinding(after))
					return false;
				if (!Equals(current.Sort, before.Sort) && !Equals(current.Sort, after.Sort))
					return false;
				if (!Equals(current.Screenshot, before.Screenshot) && !Equals(current.Screenshot, after.Screenshot))
					return false;
			}
			return true;
		}

		private IMod RequireLiveMember(string nativeKey)
		{
			IMod mod = _services.ModManager.DeploymentManager == null ? null : _services.ModManager.DeploymentManager.GetOwnerMod(nativeKey);
			if (mod == null)
				throw new InvalidOperationException("A restored user-metadata member is no longer active in current native state.");
			return mod;
		}

		private static string GetArchivePath(IMod mod)
		{
			string path = !String.IsNullOrWhiteSpace(mod.ModArchivePath) ? mod.ModArchivePath : mod.Filename;
			if (String.IsNullOrWhiteSpace(path))
				throw new InvalidOperationException("A restored user-metadata member has no current archive path.");
			return Path.GetFullPath(path);
		}

		private void ValidateRetainedScreenshot(RetainedArtifactReference retained)
		{
			if (retained == null || !_artifactStore.VerifyArtifact(retained.StableArtifactId))
				throw new InvalidDataException("A retained screenshot override no longer matches its sealed artifact identity.");
			CollectionsRetainedArtifact artifact = _artifactStore.GetArtifact(retained.StableArtifactId);
			if (artifact == null || artifact.ByteLength != retained.ByteLength || !artifact.ContentHash.Equals(retained.ContentHash))
				throw new InvalidDataException("A retained screenshot override descriptor differs from the retained artifact store.");
		}

		private byte[] ReadRetainedScreenshot(string artifactId, string expectedHash, long expectedLength, CancellationToken cancellationToken)
		{
			if (String.IsNullOrWhiteSpace(artifactId) || !_artifactStore.VerifyArtifact(artifactId, cancellationToken))
				throw new InvalidDataException("The retained screenshot override is unavailable or corrupt.");
			CollectionsRetainedArtifact artifact = _artifactStore.GetArtifact(artifactId);
			if (artifact == null || artifact.ByteLength != expectedLength || !StringComparer.Ordinal.Equals(artifact.ContentHash.Value, expectedHash))
				throw new InvalidDataException("The retained screenshot override no longer matches the reviewed desired state.");
			if (expectedLength > Int32.MaxValue)
				throw new InvalidDataException("A retained screenshot override is too large to materialize safely.");
			using (Stream stream = _artifactStore.OpenRead(artifactId))
			using (var memory = new MemoryStream((int)expectedLength))
			{
				var buffer = new byte[CopyBufferSize];
				int read;
				while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
				{
					cancellationToken.ThrowIfCancellationRequested();
					memory.Write(buffer, 0, read);
				}
				byte[] bytes = memory.ToArray();
				if (bytes.LongLength != expectedLength || !StringComparer.Ordinal.Equals(ComputeSha256(bytes), expectedHash))
					throw new InvalidDataException("The retained screenshot bytes failed exact materialization verification.");
				return bytes;
			}
		}

		private CollectionsRetainedArtifactReferenceRecord PersistIntent(string ownerId, UserMetadataIntent intent)
		{
			byte[] bytes = SerializeIntent(intent);
			CollectionsRetainedArtifact artifact;
			using (var stream = new MemoryStream(bytes, false))
				artifact = _artifactStore.Publish(stream);
			_referenceStore.AcquireExclusiveRoleReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation, ownerId, IntentRole);
			return _referenceStore.GetReferenceForOwnerRole(CollectionsRetainedArtifactOwnerKind.Operation, ownerId, IntentRole);
		}

		private void MarkVerified(string ownerId, string artifactId)
		{
			_referenceStore.AcquireExclusiveRoleReference(artifactId, CollectionsRetainedArtifactOwnerKind.Operation, ownerId, VerifiedRole);
		}

		private byte[] SerializeIntent(UserMetadataIntent intent)
		{
			using (var stream = new MemoryStream())
			using (var text = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
			using (var writer = new JsonTextWriter(text))
			{
				writer.WriteStartObject();
				writer.WritePropertyName("format"); writer.WriteValue(IntentFormat);
				writer.WritePropertyName("captureId"); writer.WriteValue(intent.CaptureId);
				writer.WritePropertyName("planFingerprint"); writer.WriteValue(intent.PlanFingerprint);
				writer.WritePropertyName("preimage"); WriteState(writer, intent.Preimage);
				writer.WritePropertyName("desired"); WriteState(writer, intent.Desired);
				writer.WriteEndObject(); writer.Flush(); text.Flush();
				return stream.ToArray();
			}
		}

		private UserMetadataIntent ReadIntent(string artifactId)
		{
			if (!_artifactStore.VerifyArtifact(artifactId))
				throw new InvalidDataException("The durable user-metadata restore intent no longer matches its retained bytes.");
			using (Stream stream = _artifactStore.OpenRead(artifactId))
			using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, false))
			{
				JObject root = JObject.Parse(reader.ReadToEnd());
				if (!StringComparer.Ordinal.Equals((string)root["format"], IntentFormat))
					throw new InvalidDataException("Unsupported Local restore user-metadata intent format.");
				return new UserMetadataIntent((string)root["captureId"], (string)root["planFingerprint"],
					ReadState(root["preimage"] as JArray), ReadState(root["desired"] as JArray));
			}
		}

		private static void ValidateIntent(UserMetadataIntent intent, CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan plan, CollectionLocalRestoreUserMetadataState desired)
		{
			if (intent == null || !StringComparer.Ordinal.Equals(intent.CaptureId, sealedCapture.Capture.Identity.ToString()) ||
				!StringComparer.Ordinal.Equals(intent.PlanFingerprint, plan.PlanFingerprint) || !intent.Desired.Equals(desired) ||
				!intent.Desired.HasSameRetainedArtifacts(desired))
				throw new InvalidDataException("The durable user-metadata restore intent differs from the reviewed Local Collection restore.");
		}

		private CollectionOperation CompleteUserMetadataPhase(CollectionOperation operation, LocalCaptureIdentity captureIdentity,
			CollectionLocalRestorePlan plan, int memberCount)
		{
			string ownerId = operation.Identity.OperationId.ToString("D");
			CollectionsRetainedArtifactReferenceRecord existing = _referenceStore.GetReferenceForOwnerRole(
				CollectionsRetainedArtifactOwnerKind.Operation, ownerId, CompleteRole);
			UserMetadataPhaseCompletion completion;
			if (existing == null)
			{
				completion = new UserMetadataPhaseCompletion(captureIdentity.ToString(), plan.PlanFingerprint, ownerId,
					operation.CheckpointSequence, memberCount);
				byte[] bytes = SerializePhaseCompletion(completion);
				CollectionsRetainedArtifact artifact;
				using (var stream = new MemoryStream(bytes, false)) artifact = _artifactStore.Publish(stream);
				_referenceStore.AcquireExclusiveRoleReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation, ownerId, CompleteRole);
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
					throw new InvalidDataException("The durable user-metadata phase-completion marker differs from the reviewed restore.");
				}
			}
			CollectionOperation persisted = _operationStore.GetOperation(operation.Identity) ?? operation;
			if (persisted.CheckpointSequence == completion.CheckpointBefore)
				return AdvanceSafeBoundary(persisted);
			if (persisted.CheckpointSequence >= completion.CheckpointBefore + 1 && persisted.Phase == CollectionOperationPhase.PausedAtSafeBoundary &&
				persisted.ResultState == CollectionOperationResultState.Pending)
				return persisted;
			MarkRecoveryRequired(persisted);
			throw new InvalidOperationException("The user-metadata phase-completion checkpoint no longer matches the durable operation journal.");
		}

		private static byte[] SerializePhaseCompletion(UserMetadataPhaseCompletion completion)
		{
			using (var stream = new MemoryStream())
			using (var text = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
			using (var writer = new JsonTextWriter(text))
			{
				writer.WriteStartObject();
				writer.WritePropertyName("format"); writer.WriteValue("nmm-ce.collections.local-restore-user-metadata-phase/1");
				writer.WritePropertyName("captureId"); writer.WriteValue(completion.CaptureId);
				writer.WritePropertyName("planFingerprint"); writer.WriteValue(completion.PlanFingerprint);
				writer.WritePropertyName("operationId"); writer.WriteValue(completion.OperationId);
				writer.WritePropertyName("checkpointBefore"); writer.WriteValue(completion.CheckpointBefore);
				writer.WritePropertyName("memberCount"); writer.WriteValue(completion.MemberCount);
				writer.WriteEndObject(); writer.Flush(); text.Flush();
				return stream.ToArray();
			}
		}

		private UserMetadataPhaseCompletion ReadPhaseCompletion(string artifactId)
		{
			if (!_artifactStore.VerifyArtifact(artifactId))
				throw new InvalidDataException("The durable user-metadata phase-completion marker no longer matches its retained bytes.");
			using (Stream stream = _artifactStore.OpenRead(artifactId))
			using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, false))
			{
				JObject root = JObject.Parse(reader.ReadToEnd());
				if (!StringComparer.Ordinal.Equals((string)root["format"], "nmm-ce.collections.local-restore-user-metadata-phase/1"))
					throw new InvalidDataException("Unsupported Local restore user-metadata phase-completion format.");
				return new UserMetadataPhaseCompletion((string)root["captureId"], (string)root["planFingerprint"],
					(string)root["operationId"], (long)root["checkpointBefore"], (int)root["memberCount"]);
			}
		}

		private static void WriteState(JsonWriter writer, CollectionLocalRestoreUserMetadataState state)
		{
			writer.WriteStartArray();
			foreach (CollectionLocalRestoreUserMetadataEntry entry in state.Entries)
			{
				writer.WriteStartObject();
				writer.WritePropertyName("snapshotKey"); writer.WriteValue(entry.NativeSnapshotKey);
				writer.WritePropertyName("nativeKey"); writer.WriteValue(entry.NativeKey);
				writer.WritePropertyName("archivePath"); writer.WriteValue(entry.ArchivePath);
				writer.WritePropertyName("sort"); WriteSort(writer, entry.Sort);
				writer.WritePropertyName("screenshot"); WriteScreenshot(writer, entry.Screenshot);
				writer.WriteEndObject();
			}
			writer.WriteEndArray();
		}

		private static CollectionLocalRestoreUserMetadataState ReadState(JArray array)
		{
			if (array == null) throw new InvalidDataException("A user-metadata restore intent is missing one exact state sequence.");
			var entries = new List<CollectionLocalRestoreUserMetadataEntry>();
			foreach (JObject item in array.OfType<JObject>())
				entries.Add(new CollectionLocalRestoreUserMetadataEntry((string)item["snapshotKey"], (string)item["nativeKey"],
					(string)item["archivePath"], ReadSort(item["sort"]), ReadScreenshot(item["screenshot"])));
			return new CollectionLocalRestoreUserMetadataState(entries);
		}

		private static void WriteSort(JsonWriter writer, CollectionLocalRestoreUserSortState sort)
		{
			if (sort == null) { writer.WriteNull(); return; }
			writer.WriteStartObject();
			writer.WritePropertyName("present"); writer.WriteValue(sort.Present);
			writer.WritePropertyName("sortNumber"); if (sort.SortNumber.HasValue) writer.WriteValue(sort.SortNumber.Value); else writer.WriteNull();
			writer.WritePropertyName("assignmentState"); writer.WriteValue((int)sort.AssignmentState);
			writer.WritePropertyName("modId"); writer.WriteValue(sort.RecordedModId);
			writer.WritePropertyName("downloadId"); writer.WriteValue(sort.RecordedDownloadId);
			writer.WriteEndObject();
		}

		private static CollectionLocalRestoreUserSortState ReadSort(JToken token)
		{
			if (token == null || token.Type == JTokenType.Null) return null;
			JObject value = token as JObject;
			if (value == null) throw new InvalidDataException("A persisted user Sort state is malformed.");
			return new CollectionLocalRestoreUserSortState((bool)value["present"], value["sortNumber"].Type == JTokenType.Null ? (int?)null : (int)value["sortNumber"],
				(ModSortOrderAssignmentState)(int)value["assignmentState"], (string)value["modId"], (string)value["downloadId"]);
		}

		private static void WriteScreenshot(JsonWriter writer, CollectionLocalRestoreUserScreenshotState screenshot)
		{
			if (screenshot == null) { writer.WriteNull(); return; }
			writer.WriteStartObject();
			writer.WritePropertyName("present"); writer.WriteValue(screenshot.Present);
			writer.WritePropertyName("readState"); writer.WriteValue((int)screenshot.ReadState);
			writer.WritePropertyName("screenshotPath"); writer.WriteValue(screenshot.ScreenshotPath);
			writer.WritePropertyName("hash"); writer.WriteValue(screenshot.ContentHash);
			writer.WritePropertyName("length"); writer.WriteValue(screenshot.ByteLength);
			writer.WritePropertyName("archiveLength"); writer.WriteValue(screenshot.ArchiveLength);
			writer.WritePropertyName("archiveWriteUtc"); writer.WriteValue(screenshot.ArchiveWriteTimeUtcTicks);
			writer.WritePropertyName("updatedUtc"); writer.WriteValue(screenshot.UpdatedUtcTicks);
			writer.WritePropertyName("artifactId"); writer.WriteValue(screenshot.RetainedArtifactId);
			writer.WriteEndObject();
		}

		private static CollectionLocalRestoreUserScreenshotState ReadScreenshot(JToken token)
		{
			if (token == null || token.Type == JTokenType.Null) return null;
			JObject value = token as JObject;
			if (value == null) throw new InvalidDataException("A persisted user screenshot state is malformed.");
			if (!(bool)value["present"]) return CollectionLocalRestoreUserScreenshotState.None();
			return CollectionLocalRestoreUserScreenshotState.CreatePresent((ModFormatScreenshotOverrideReadState)(int)value["readState"],
				(string)value["screenshotPath"], (string)value["hash"], (long)value["length"], (long)value["archiveLength"],
				(long)value["archiveWriteUtc"], (long)value["updatedUtc"], (string)value["artifactId"]);
		}

		private static string ComputeSha256(byte[] bytes)
		{
			using (SHA256 sha256 = SHA256.Create())
				return BitConverter.ToString(sha256.ComputeHash(bytes ?? new byte[0])).Replace("-", String.Empty).ToLowerInvariant();
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
			if (current.IsTerminal || current.Phase == CollectionOperationPhase.RecoveryRequired) return;
			CollectionOperation updated = new CollectionOperation(current.Identity, current.Kind, current.Collection, current.Target,
				current.Revision, current.PlanIdentity, current.CheckpointSequence + 1, CollectionOperationPhase.RecoveryRequired,
				CollectionOperationResultState.RecoveryRequired, current.NativeChildren);
			_operationStore.SaveOperation(updated);
		}

		private sealed class UserMetadataIntent
		{
			internal UserMetadataIntent(string captureId, string planFingerprint, CollectionLocalRestoreUserMetadataState preimage,
				CollectionLocalRestoreUserMetadataState desired)
			{
				CaptureId = captureId ?? String.Empty; PlanFingerprint = planFingerprint ?? String.Empty;
				Preimage = preimage ?? throw new ArgumentNullException(nameof(preimage)); Desired = desired ?? throw new ArgumentNullException(nameof(desired));
			}
			internal string CaptureId { get; }
			internal string PlanFingerprint { get; }
			internal CollectionLocalRestoreUserMetadataState Preimage { get; }
			internal CollectionLocalRestoreUserMetadataState Desired { get; }
		}

		private sealed class UserMetadataPhaseCompletion
		{
			internal UserMetadataPhaseCompletion(string captureId, string planFingerprint, string operationId, long checkpointBefore, int memberCount)
			{
				CaptureId = captureId; PlanFingerprint = planFingerprint; OperationId = operationId; CheckpointBefore = checkpointBefore; MemberCount = memberCount;
			}
			internal string CaptureId { get; }
			internal string PlanFingerprint { get; }
			internal string OperationId { get; }
			internal long CheckpointBefore { get; }
			internal int MemberCount { get; }
		}
	}

	internal sealed class CollectionLocalRestoreUserSortState : IEquatable<CollectionLocalRestoreUserSortState>
	{
		public CollectionLocalRestoreUserSortState(bool present, int? sortNumber, ModSortOrderAssignmentState assignmentState,
			string recordedModId, string recordedDownloadId)
		{
			Present = present; SortNumber = sortNumber; AssignmentState = assignmentState;
			RecordedModId = recordedModId ?? String.Empty; RecordedDownloadId = recordedDownloadId ?? String.Empty;
		}
		public bool Present { get; }
		public int? SortNumber { get; }
		public ModSortOrderAssignmentState AssignmentState { get; }
		public string RecordedModId { get; }
		public string RecordedDownloadId { get; }
		public bool Equals(CollectionLocalRestoreUserSortState other)
		{
			return other != null && Present == other.Present && SortNumber == other.SortNumber && AssignmentState == other.AssignmentState &&
				StringComparer.OrdinalIgnoreCase.Equals(RecordedModId, other.RecordedModId) && StringComparer.OrdinalIgnoreCase.Equals(RecordedDownloadId, other.RecordedDownloadId);
		}
		public override bool Equals(object obj) { return Equals(obj as CollectionLocalRestoreUserSortState); }
		public override int GetHashCode() { return Present.GetHashCode() ^ AssignmentState.GetHashCode() ^ (SortNumber ?? 0); }
	}

	internal sealed class CollectionLocalRestoreUserScreenshotState : IEquatable<CollectionLocalRestoreUserScreenshotState>
	{
		private CollectionLocalRestoreUserScreenshotState(bool present, ModFormatScreenshotOverrideReadState readState, string screenshotPath,
			string contentHash, long byteLength, long archiveLength, long archiveWriteTimeUtcTicks, long updatedUtcTicks, string retainedArtifactId)
		{
			Present = present; ReadState = readState; ScreenshotPath = screenshotPath ?? String.Empty; ContentHash = contentHash ?? String.Empty;
			ByteLength = byteLength; ArchiveLength = archiveLength; ArchiveWriteTimeUtcTicks = archiveWriteTimeUtcTicks; UpdatedUtcTicks = updatedUtcTicks;
			RetainedArtifactId = retainedArtifactId ?? String.Empty;
		}
		public static CollectionLocalRestoreUserScreenshotState None() { return new CollectionLocalRestoreUserScreenshotState(false, ModFormatScreenshotOverrideReadState.None, null, null, 0, 0, 0, 0, null); }
		public static CollectionLocalRestoreUserScreenshotState CreatePresent(ModFormatScreenshotOverrideReadState readState, string screenshotPath,
			string contentHash, long byteLength, long archiveLength, long archiveWriteTimeUtcTicks, long updatedUtcTicks, string retainedArtifactId)
		{
			return new CollectionLocalRestoreUserScreenshotState(true, readState, screenshotPath, contentHash, byteLength, archiveLength,
				archiveWriteTimeUtcTicks, updatedUtcTicks, retainedArtifactId);
		}
		public bool Present { get; }
		public ModFormatScreenshotOverrideReadState ReadState { get; }
		public string ScreenshotPath { get; }
		public string ContentHash { get; }
		public long ByteLength { get; }
		public long ArchiveLength { get; }
		public long ArchiveWriteTimeUtcTicks { get; }
		public long UpdatedUtcTicks { get; }
		public string RetainedArtifactId { get; }
		public bool Equals(CollectionLocalRestoreUserScreenshotState other)
		{
			return other != null && Present == other.Present && ReadState == other.ReadState &&
				StringComparer.OrdinalIgnoreCase.Equals(ScreenshotPath, other.ScreenshotPath) && StringComparer.Ordinal.Equals(ContentHash, other.ContentHash) &&
				ByteLength == other.ByteLength && ArchiveLength == other.ArchiveLength && ArchiveWriteTimeUtcTicks == other.ArchiveWriteTimeUtcTicks &&
				UpdatedUtcTicks == other.UpdatedUtcTicks;
		}
		public override bool Equals(object obj) { return Equals(obj as CollectionLocalRestoreUserScreenshotState); }
		public override int GetHashCode() { return Present.GetHashCode() ^ ReadState.GetHashCode() ^ ByteLength.GetHashCode(); }
	}

	internal sealed class CollectionLocalRestoreUserMetadataEntry : IEquatable<CollectionLocalRestoreUserMetadataEntry>
	{
		public CollectionLocalRestoreUserMetadataEntry(string nativeSnapshotKey, string nativeKey, string archivePath,
			CollectionLocalRestoreUserSortState sort, CollectionLocalRestoreUserScreenshotState screenshot)
		{
			if (String.IsNullOrWhiteSpace(nativeSnapshotKey)) throw new ArgumentException("A captured native key is required.", nameof(nativeSnapshotKey));
			if (String.IsNullOrWhiteSpace(nativeKey)) throw new ArgumentException("A current native key is required.", nameof(nativeKey));
			if (String.IsNullOrWhiteSpace(archivePath)) throw new ArgumentException("A current archive path is required.", nameof(archivePath));
			NativeSnapshotKey = nativeSnapshotKey; NativeKey = nativeKey; ArchivePath = archivePath; Sort = sort; Screenshot = screenshot;
		}
		public string NativeSnapshotKey { get; }
		public string NativeKey { get; }
		public string ArchivePath { get; }
		public CollectionLocalRestoreUserSortState Sort { get; }
		public CollectionLocalRestoreUserScreenshotState Screenshot { get; }
		internal bool HasSameBinding(CollectionLocalRestoreUserMetadataEntry other)
		{
			return other != null && StringComparer.OrdinalIgnoreCase.Equals(NativeSnapshotKey, other.NativeSnapshotKey) &&
				StringComparer.OrdinalIgnoreCase.Equals(NativeKey, other.NativeKey) && StringComparer.OrdinalIgnoreCase.Equals(ArchivePath, other.ArchivePath);
		}
		public bool Equals(CollectionLocalRestoreUserMetadataEntry other)
		{
			return HasSameBinding(other) && Equals(Sort, other.Sort) && Equals(Screenshot, other.Screenshot);
		}
		public override bool Equals(object obj) { return Equals(obj as CollectionLocalRestoreUserMetadataEntry); }
		public override int GetHashCode() { return StringComparer.OrdinalIgnoreCase.GetHashCode(NativeSnapshotKey); }
	}

	internal sealed class CollectionLocalRestoreUserMetadataState : IEquatable<CollectionLocalRestoreUserMetadataState>
	{
		private readonly ReadOnlyCollection<CollectionLocalRestoreUserMetadataEntry> _entries;
		public CollectionLocalRestoreUserMetadataState(IEnumerable<CollectionLocalRestoreUserMetadataEntry> entries)
		{
			List<CollectionLocalRestoreUserMetadataEntry> copied = (entries ?? throw new ArgumentNullException(nameof(entries))).OrderBy(x => x.NativeSnapshotKey, StringComparer.OrdinalIgnoreCase).ToList();
			if (copied.Any(x => x == null) || copied.GroupBy(x => x.NativeSnapshotKey, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() != 1))
				throw new ArgumentException("User-metadata state contains null or duplicate captured members.", nameof(entries));
			_entries = new ReadOnlyCollection<CollectionLocalRestoreUserMetadataEntry>(copied);
		}
		public ReadOnlyCollection<CollectionLocalRestoreUserMetadataEntry> Entries { get { return _entries; } }
		internal bool HasSameRetainedArtifacts(CollectionLocalRestoreUserMetadataState other)
		{
			if (other == null || _entries.Count != other._entries.Count) return false;
			for (int index = 0; index < _entries.Count; index++)
			{
				CollectionLocalRestoreUserScreenshotState left = _entries[index].Screenshot;
				CollectionLocalRestoreUserScreenshotState right = other._entries[index].Screenshot;
				if ((left == null) != (right == null)) return false;
				if (left != null && !StringComparer.Ordinal.Equals(left.RetainedArtifactId, right.RetainedArtifactId)) return false;
			}
			return true;
		}
		public bool Equals(CollectionLocalRestoreUserMetadataState other)
		{
			return other != null && _entries.SequenceEqual(other._entries);
		}
		public override bool Equals(object obj) { return Equals(obj as CollectionLocalRestoreUserMetadataState); }
		public override int GetHashCode() { return _entries.Count; }
	}
}
