using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Describes how the C7.10b5 game-specific-value phase reached its verified live state.</summary>
	public enum CollectionLocalRestoreGameValueOutcome
	{
		NotApplicable = 1,
		AlreadySatisfied = 2,
		RestoredAndVerified = 3,
		RecoveredCommitted = 4
	}

	/// <summary>Result of the C7.10b5 game-specific phase; logical user-metadata restoration remains pending.</summary>
	public sealed class CollectionLocalRestoreGameValueExecutionResult
	{
		internal CollectionLocalRestoreGameValueExecutionResult(CollectionOperation operation, CollectionLocalRestoreGameValueOutcome outcome)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Outcome = outcome;
		}

		public CollectionOperation Operation { get; }
		public CollectionLocalRestoreGameValueOutcome Outcome { get; }
		public bool IsReadyForUserMetadataReconciliation
		{
			get { return Operation.Phase == CollectionOperationPhase.PausedAtSafeBoundary && !Operation.HasUnreconciledNativeChild; }
		}
	}

	/// <summary>Restores supported captured game-specific binary values and exact ordered native owner history.</summary>
	public sealed class CollectionLocalRestoreGameValueExecutor
	{
		private const string IntentFormat = "nmm-ce.collections.local-restore-game-value-intent/1";
		private const string IntentRole = "local-restore-game-value-intent-v1";
		private const string VerifiedRole = "local-restore-game-value-verified-v1";
		private const string CompleteRole = "local-restore-game-value-phase-complete-v1";

		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionsRetainedArtifactReferenceStore _referenceStore;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;

		public CollectionLocalRestoreGameValueExecutor(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore)
			: this(services, gameStorageService, operationStore, artifactStore, referenceStore,
				CollectionTargetMutationLeaseManager.Shared, new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services))
		{
		}

		internal CollectionLocalRestoreGameValueExecutor(ServiceManager services, GameStorageService gameStorageService,
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
				throw new InvalidOperationException("Game-specific restoration requires the live native ModManager.");
		}

		public Task<CollectionLocalRestoreGameValueExecutionResult> ExecuteAsync(CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan reviewedPlan, CollectionLocalRestoreMemberExecutionResult memberPhase,
			CollectionLocalRestoreIniExecutionResult iniPhase, GameStoragePathSet paths)
		{
			return ExecuteAsync(sealedCapture, reviewedPlan, memberPhase, iniPhase, paths, CancellationToken.None);
		}

		public async Task<CollectionLocalRestoreGameValueExecutionResult> ExecuteAsync(CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan reviewedPlan, CollectionLocalRestoreMemberExecutionResult memberPhase,
			CollectionLocalRestoreIniExecutionResult iniPhase, GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			ValidateInputs(sealedCapture, reviewedPlan, memberPhase, iniPhase, paths);
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(reviewedPlan.Target))
				throw new InvalidOperationException("The live canonical target no longer matches the reviewed Local Collection restore.");

			using (CollectionTargetMutationLease rootLease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				_authorityValidator.ValidateAndReload(rootLease, authority, paths);
				CollectionOperation operation = RequireOperation(iniPhase.Operation.Identity, reviewedPlan);
				bool applicable = sealedCapture.Capture.Scope.Contains(LocalCaptureScopeArea.NativeConfigurationEffects);
				CollectionLocalRestoreGameValueState desired = BuildDesiredState(sealedCapture, reviewedPlan, memberPhase, applicable);
				CollectionLocalRestoreGameValueOutcome outcome;

				if (!applicable)
				{
					if (sealedCapture.NativeEffects.GameValueCoverage == NativeStateCaptureCoverage.NotApplicable && sealedCapture.NativeEffects.GameValues.Count != 0)
						throw new InvalidDataException("A non-applicable native game-specific capture cannot contain game-value records.");
					outcome = CollectionLocalRestoreGameValueOutcome.NotApplicable;
				}
				else
				{
					string ownerId = operation.Identity.OperationId.ToString("D");
					CollectionLocalRestoreGameValueState live = CaptureCurrentState(reviewedPlan.Target);
					CollectionsRetainedArtifactReferenceRecord verified = _referenceStore.GetReferenceForOwnerRole(
						CollectionsRetainedArtifactOwnerKind.Operation, ownerId, VerifiedRole);
					if (verified != null)
					{
						GameValueIntent verifiedIntent = ReadIntent(verified.ArtifactId);
						ValidateIntent(verifiedIntent, sealedCapture, reviewedPlan, desired);
						if (!live.Equals(desired))
						{
							MarkRecoveryRequired(operation);
							throw new InvalidOperationException("A durable game-specific verified checkpoint no longer matches authoritative native state.");
						}
						outcome = CollectionLocalRestoreGameValueOutcome.AlreadySatisfied;
					}
					else
					{
						CollectionsRetainedArtifactReferenceRecord existingIntent = _referenceStore.GetReferenceForOwnerRole(
							CollectionsRetainedArtifactOwnerKind.Operation, ownerId, IntentRole);
						GameValueIntent intent;
						if (existingIntent == null)
						{
							intent = new GameValueIntent(sealedCapture.Capture.Identity.ToString(), reviewedPlan.PlanFingerprint, live, desired);
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
								outcome = CollectionLocalRestoreGameValueOutcome.RecoveredCommitted;
								operation = CompleteGameValuePhase(operation, sealedCapture.Capture.Identity, reviewedPlan, desired.Entries.Count);
								return new CollectionLocalRestoreGameValueExecutionResult(operation, outcome);
							}
							if (!live.Equals(intent.Preimage))
							{
								MarkRecoveryRequired(operation);
								throw new InvalidOperationException("The live game-specific state differs from both its exact preimage and desired restore postimage.");
							}
						}

						cancellationToken.ThrowIfCancellationRequested();
						_services.ModManager.RestoreCapturedGameSpecificValueState(BuildNativeEntries(desired));
						CollectionLocalRestoreGameValueState postimage = CaptureCurrentState(reviewedPlan.Target);
						if (!postimage.Equals(desired))
						{
							MarkRecoveryRequired(operation);
							throw new InvalidOperationException("Native game-specific restoration returned without establishing the exact captured physical bytes and owner history.");
						}
						MarkVerified(ownerId, existingIntent.ArtifactId);
						outcome = CollectionLocalRestoreGameValueOutcome.RestoredAndVerified;
					}
				}

				operation = CompleteGameValuePhase(operation, sealedCapture.Capture.Identity, reviewedPlan, desired.Entries.Count);
				return new CollectionLocalRestoreGameValueExecutionResult(operation, outcome);
			}
		}

		/// <summary>Verifies captured game-specific values and owner history without mutation.</summary>
		internal void VerifyFinalState(CollectionSealedCaptureSnapshot sealedCapture, CollectionLocalRestorePlan reviewedPlan,
			CollectionLocalRestoreMemberExecutionResult memberPhase)
		{
			if (sealedCapture == null) throw new ArgumentNullException(nameof(sealedCapture));
			if (reviewedPlan == null) throw new ArgumentNullException(nameof(reviewedPlan));
			if (memberPhase == null) throw new ArgumentNullException(nameof(memberPhase));
			bool applicable = sealedCapture.Capture.Scope.Contains(LocalCaptureScopeArea.NativeConfigurationEffects);
			CollectionLocalRestoreGameValueState desired = BuildDesiredState(sealedCapture, reviewedPlan, memberPhase, applicable);
			if (!applicable)
			{
				if (sealedCapture.NativeEffects.GameValueCoverage == NativeStateCaptureCoverage.NotApplicable && sealedCapture.NativeEffects.GameValues.Count != 0)
					throw new InvalidDataException("A non-applicable native game-specific capture cannot contain game-value records.");
				return;
			}
			if (!CaptureCurrentState(reviewedPlan.Target).Equals(desired))
				throw new InvalidOperationException("Final Local restore verification found game-specific state that no longer matches the sealed capture.");
		}

		private void ValidateInputs(CollectionSealedCaptureSnapshot sealedCapture, CollectionLocalRestorePlan reviewedPlan,
			CollectionLocalRestoreMemberExecutionResult memberPhase, CollectionLocalRestoreIniExecutionResult iniPhase,
			GameStoragePathSet paths)
		{
			if (sealedCapture == null) throw new ArgumentNullException(nameof(sealedCapture));
			if (reviewedPlan == null) throw new ArgumentNullException(nameof(reviewedPlan));
			if (memberPhase == null) throw new ArgumentNullException(nameof(memberPhase));
			if (iniPhase == null) throw new ArgumentNullException(nameof(iniPhase));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			if (!reviewedPlan.IsReadyForReview || sealedCapture.Capture.Capability != LocalCaptureCapability.LocallyRestorableWithinScope)
				throw new InvalidOperationException("Game-specific restoration requires one reviewed locally-restorable C7.9 plan.");
			if (!sealedCapture.Capture.Identity.Equals(reviewedPlan.CaptureIdentity) || !sealedCapture.NativeEffects.Target.Equals(reviewedPlan.Target))
				throw new InvalidOperationException("Game-specific restoration inputs do not belong to the same Local Collection capture and target.");
			if (!iniPhase.IsReadyForGameSpecificStateReconciliation || iniPhase.Operation.Kind != CollectionOperationKind.RestoreLocalCapture ||
				!iniPhase.Operation.Target.Equals(reviewedPlan.Target) || !memberPhase.Operation.Identity.Equals(iniPhase.Operation.Identity))
				throw new InvalidOperationException("Game-specific restoration requires the verified INI safe boundary of the same restore operation.");
			if (sealedCapture.Capture.Scope.Contains(LocalCaptureScopeArea.NativeConfigurationEffects) &&
				sealedCapture.NativeEffects.GameValueCoverage != NativeStateCaptureCoverage.Complete &&
				sealedCapture.NativeEffects.GameValueCoverage != NativeStateCaptureCoverage.NotApplicable)
				throw new InvalidDataException("A locally-restorable game-specific scope requires complete or non-applicable captured coverage.");
		}

		private CollectionOperation RequireOperation(CollectionOperationIdentity identity, CollectionLocalRestorePlan plan)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Kind != CollectionOperationKind.RestoreLocalCapture || operation.IsTerminal ||
				operation.ResultState != CollectionOperationResultState.Pending || operation.Phase != CollectionOperationPhase.PausedAtSafeBoundary ||
				!operation.Target.Equals(plan.Target) || operation.HasUnreconciledNativeChild)
				throw new InvalidOperationException("Game-specific restoration requires the exact active Local Collection restore operation at a reconciled safe boundary.");
			return operation;
		}

		private CollectionLocalRestoreGameValueState BuildDesiredState(CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan plan, CollectionLocalRestoreMemberExecutionResult memberPhase, bool applicable)
		{
			if (!applicable)
				return new CollectionLocalRestoreGameValueState(new CollectionLocalRestoreGameValueEntry[0]);
			Dictionary<CollectionMemberKey, string> remaps = memberPhase.MemberRemaps.ToDictionary(x => x.MemberKey, x => x.NativeKey);
			Dictionary<string, CollectionMemberKey> membersByCapturedKey = plan.Members.ToDictionary(x => x.CapturedNativeKey,
				x => x.SnapshotMemberKey, StringComparer.OrdinalIgnoreCase);
			var entries = new List<CollectionLocalRestoreGameValueEntry>();
			foreach (CollectionCapturedGameValueEffect effect in sealedCapture.NativeEffects.GameValues)
			{
				if (effect.Values.Count == 0)
					throw new InvalidDataException("A captured game-specific value has no owner history.");
				var owners = new List<CollectionLocalRestoreGameValueOwner>();
				foreach (CollectionCapturedGameValueOwnerValue owner in effect.Values)
				{
					if (owner.OwnerKind == CollectionNativeEffectOwnerKind.OriginalValue)
						owners.Add(new CollectionLocalRestoreGameValueOwner(plan.CurrentOriginalValuesKey, owner.OwnerKind, owner.Value));
					else if (owner.OwnerKind == CollectionNativeEffectOwnerKind.NativeMod)
					{
						CollectionMemberKey memberKey;
						string nativeKey;
						if (!membersByCapturedKey.TryGetValue(owner.OwnerKey, out memberKey) || !remaps.TryGetValue(memberKey, out nativeKey))
							throw new InvalidOperationException("A captured game-specific owner has no current C7.10a native-key remap.");
						owners.Add(new CollectionLocalRestoreGameValueOwner(nativeKey, owner.OwnerKind, owner.Value));
					}
					else
						throw new InvalidDataException("A locally-restorable game-specific capture contains an unresolved owner.");
				}
				entries.Add(new CollectionLocalRestoreGameValueEntry(effect.Key, owners,
					effect.Values[effect.Values.Count - 1].Value));
			}
			return new CollectionLocalRestoreGameValueState(entries);
		}

		private CollectionLocalRestoreGameValueState CaptureCurrentState(CollectionTargetIdentity target)
		{
			ModManager manager = _services.ModManager;
			var nativeReader = new NativeStateCaptureReader(manager.InstallationLog, manager.VirtualModActivator,
				manager.DeploymentManager, _services.PluginManager, manager.GameMode);
			NativeStateCaptureSnapshot native = nativeReader.Capture();
			CollectionNativeEffectSnapshot effects = new CollectionNativeEffectCaptureReader(nativeReader).Capture(target, native);
			if (effects.GameValueCoverage != NativeStateCaptureCoverage.Complete && effects.GameValueCoverage != NativeStateCaptureCoverage.NotApplicable)
				throw new InvalidOperationException("Current native game-specific state is ambiguous and cannot be used as exact restart evidence.");
			var entries = new List<CollectionLocalRestoreGameValueEntry>();
			foreach (CollectionCapturedGameValueEffect effect in effects.GameValues)
			{
				byte[] physical;
				if (!manager.TryCaptureCurrentGameSpecificValue(effect.Key, out physical))
					throw new NotSupportedException("The active game mode cannot read exact physical bytes for game-specific key '" + effect.Key + "'.");
				entries.Add(new CollectionLocalRestoreGameValueEntry(effect.Key,
					effect.Values.Select(x => new CollectionLocalRestoreGameValueOwner(x.OwnerKey, x.OwnerKind, x.Value)), physical));
			}
			return new CollectionLocalRestoreGameValueState(entries);
		}

		private IReadOnlyList<ModGameSpecificValueRestoreEntry> BuildNativeEntries(CollectionLocalRestoreGameValueState desired)
		{
			IInstallLog installLog = _services.ModManager.InstallationLog;
			var result = new List<ModGameSpecificValueRestoreEntry>();
			foreach (CollectionLocalRestoreGameValueEntry entry in desired.Entries)
			{
				var owners = new List<ModGameSpecificValueRestoreOwner>();
				foreach (CollectionLocalRestoreGameValueOwner owner in entry.Owners)
				{
					bool original = owner.OwnerKind == CollectionNativeEffectOwnerKind.OriginalValue;
					IMod mod = original ? null : _services.ModManager.ActiveMods.FirstOrDefault(x =>
						owner.OwnerKey.Equals(installLog.GetModKey(x), StringComparison.OrdinalIgnoreCase));
					if (!original && mod == null)
						throw new InvalidOperationException("A desired game-specific owner is no longer an active native mod registration.");
					owners.Add(new ModGameSpecificValueRestoreOwner(owner.OwnerKey, original, mod, owner.Value));
				}
				result.Add(new ModGameSpecificValueRestoreEntry(entry.Key, owners));
			}
			return result;
		}

		private CollectionsRetainedArtifactReferenceRecord PersistIntent(string ownerId, GameValueIntent intent)
		{
			CollectionsRetainedArtifact artifact;
			using (var stream = new MemoryStream(SerializeIntent(intent), false))
				artifact = _artifactStore.Publish(stream);
			_referenceStore.AcquireExclusiveRoleReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation, ownerId, IntentRole);
			return _referenceStore.GetReferenceForOwnerRole(CollectionsRetainedArtifactOwnerKind.Operation, ownerId, IntentRole);
		}

		private void MarkVerified(string ownerId, string artifactId)
		{
			_referenceStore.AcquireExclusiveRoleReference(artifactId, CollectionsRetainedArtifactOwnerKind.Operation, ownerId, VerifiedRole);
		}

		private static byte[] SerializeIntent(GameValueIntent intent)
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

		private GameValueIntent ReadIntent(string artifactId)
		{
			if (!_artifactStore.VerifyArtifact(artifactId))
				throw new InvalidDataException("The durable game-specific restore intent no longer matches its retained bytes.");
			using (Stream stream = _artifactStore.OpenRead(artifactId))
			using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, false))
			{
				JObject root = JObject.Parse(reader.ReadToEnd());
				if (!StringComparer.Ordinal.Equals((string)root["format"], IntentFormat))
					throw new InvalidDataException("Unsupported Local restore game-specific intent format.");
				return new GameValueIntent((string)root["captureId"], (string)root["planFingerprint"],
					ReadState(root["preimage"] as JArray), ReadState(root["desired"] as JArray));
			}
		}

		private static void ValidateIntent(GameValueIntent intent, CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan plan, CollectionLocalRestoreGameValueState desired)
		{
			if (intent == null || !StringComparer.Ordinal.Equals(intent.CaptureId, sealedCapture.Capture.Identity.ToString()) ||
				!StringComparer.Ordinal.Equals(intent.PlanFingerprint, plan.PlanFingerprint) || !intent.Desired.Equals(desired))
				throw new InvalidDataException("The durable game-specific restore intent differs from the reviewed Local Collection restore.");
		}

		private CollectionOperation CompleteGameValuePhase(CollectionOperation operation, LocalCaptureIdentity captureIdentity,
			CollectionLocalRestorePlan plan, int valueCount)
		{
			string ownerId = operation.Identity.OperationId.ToString("D");
			CollectionsRetainedArtifactReferenceRecord existing = _referenceStore.GetReferenceForOwnerRole(
				CollectionsRetainedArtifactOwnerKind.Operation, ownerId, CompleteRole);
			GameValuePhaseCompletion completion;
			if (existing == null)
			{
				CollectionOperation current = _operationStore.GetOperation(operation.Identity) ?? operation;
				completion = new GameValuePhaseCompletion(captureIdentity.ToString(), plan.PlanFingerprint, ownerId,
					current.CheckpointSequence, valueCount);
				CollectionsRetainedArtifact artifact;
				using (var stream = new MemoryStream(SerializePhaseCompletion(completion), false))
					artifact = _artifactStore.Publish(stream);
				_referenceStore.AcquireExclusiveRoleReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation, ownerId, CompleteRole);
			}
			else
			{
				try { completion = ReadPhaseCompletion(existing.ArtifactId); }
				catch { MarkRecoveryRequired(operation); throw; }
				if (!StringComparer.Ordinal.Equals(completion.CaptureId, captureIdentity.ToString()) ||
					!StringComparer.Ordinal.Equals(completion.PlanFingerprint, plan.PlanFingerprint) ||
					!StringComparer.Ordinal.Equals(completion.OperationId, ownerId) || completion.ValueCount != valueCount)
				{
					MarkRecoveryRequired(operation);
					throw new InvalidDataException("The durable game-specific phase-completion marker differs from the reviewed restore.");
				}
			}
			CollectionOperation persisted = _operationStore.GetOperation(operation.Identity) ?? operation;
			if (persisted.CheckpointSequence == completion.CheckpointBefore)
				return AdvanceSafeBoundary(persisted);
			if (persisted.CheckpointSequence >= completion.CheckpointBefore + 1 &&
				persisted.Phase == CollectionOperationPhase.PausedAtSafeBoundary &&
				persisted.ResultState == CollectionOperationResultState.Pending)
				return persisted;
			MarkRecoveryRequired(persisted);
			throw new InvalidOperationException("The game-specific phase-completion checkpoint no longer matches the durable operation journal.");
		}

		private static byte[] SerializePhaseCompletion(GameValuePhaseCompletion completion)
		{
			using (var stream = new MemoryStream())
			using (var text = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
			using (var writer = new JsonTextWriter(text))
			{
				writer.WriteStartObject();
				writer.WritePropertyName("format"); writer.WriteValue("nmm-ce.collections.local-restore-game-value-phase/1");
				writer.WritePropertyName("captureId"); writer.WriteValue(completion.CaptureId);
				writer.WritePropertyName("planFingerprint"); writer.WriteValue(completion.PlanFingerprint);
				writer.WritePropertyName("operationId"); writer.WriteValue(completion.OperationId);
				writer.WritePropertyName("checkpointBefore"); writer.WriteValue(completion.CheckpointBefore);
				writer.WritePropertyName("valueCount"); writer.WriteValue(completion.ValueCount);
				writer.WriteEndObject(); writer.Flush(); text.Flush();
				return stream.ToArray();
			}
		}

		private GameValuePhaseCompletion ReadPhaseCompletion(string artifactId)
		{
			if (!_artifactStore.VerifyArtifact(artifactId))
				throw new InvalidDataException("The durable game-specific phase-completion marker no longer matches its retained bytes.");
			using (Stream stream = _artifactStore.OpenRead(artifactId))
			using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, false))
			{
				JObject root = JObject.Parse(reader.ReadToEnd());
				if (!StringComparer.Ordinal.Equals((string)root["format"], "nmm-ce.collections.local-restore-game-value-phase/1"))
					throw new InvalidDataException("Unsupported Local restore game-specific phase-completion format.");
				return new GameValuePhaseCompletion((string)root["captureId"], (string)root["planFingerprint"],
					(string)root["operationId"], (long)root["checkpointBefore"], (int)root["valueCount"]);
			}
		}

		private static void WriteState(JsonWriter writer, CollectionLocalRestoreGameValueState state)
		{
			writer.WriteStartArray();
			foreach (CollectionLocalRestoreGameValueEntry entry in state.Entries)
			{
				writer.WriteStartObject();
				writer.WritePropertyName("key"); writer.WriteValue(entry.Key);
				writer.WritePropertyName("physicalValue"); WriteBytes(writer, entry.PhysicalValue);
				writer.WritePropertyName("owners"); writer.WriteStartArray();
				foreach (CollectionLocalRestoreGameValueOwner owner in entry.Owners)
				{
					writer.WriteStartObject();
					writer.WritePropertyName("ownerKey"); writer.WriteValue(owner.OwnerKey);
					writer.WritePropertyName("ownerKind"); writer.WriteValue((int)owner.OwnerKind);
					writer.WritePropertyName("value"); WriteBytes(writer, owner.Value);
					writer.WriteEndObject();
				}
				writer.WriteEndArray(); writer.WriteEndObject();
			}
			writer.WriteEndArray();
		}

		private static CollectionLocalRestoreGameValueState ReadState(JArray array)
		{
			if (array == null)
				throw new InvalidDataException("A game-specific restore intent is missing one exact state sequence.");
			return new CollectionLocalRestoreGameValueState(array.OfType<JObject>().Select(x =>
				new CollectionLocalRestoreGameValueEntry((string)x["key"],
					((JArray)x["owners"]).OfType<JObject>().Select(o => new CollectionLocalRestoreGameValueOwner(
						(string)o["ownerKey"], (CollectionNativeEffectOwnerKind)(int)o["ownerKind"], ReadBytes(o["value"]))),
					ReadBytes(x["physicalValue"]))));
		}

		private static void WriteBytes(JsonWriter writer, byte[] value)
		{
			if (value == null) writer.WriteNull();
			else writer.WriteValue(Convert.ToBase64String(value));
		}

		private static byte[] ReadBytes(JToken token)
		{
			if (token == null || token.Type == JTokenType.Null)
				return null;
			return Convert.FromBase64String((string)token);
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

		private sealed class GameValueIntent
		{
			internal GameValueIntent(string captureId, string planFingerprint, CollectionLocalRestoreGameValueState preimage,
				CollectionLocalRestoreGameValueState desired)
			{
				CaptureId = captureId ?? String.Empty;
				PlanFingerprint = planFingerprint ?? String.Empty;
				Preimage = preimage ?? throw new ArgumentNullException(nameof(preimage));
				Desired = desired ?? throw new ArgumentNullException(nameof(desired));
			}
			internal string CaptureId { get; }
			internal string PlanFingerprint { get; }
			internal CollectionLocalRestoreGameValueState Preimage { get; }
			internal CollectionLocalRestoreGameValueState Desired { get; }
		}

		private sealed class GameValuePhaseCompletion
		{
			internal GameValuePhaseCompletion(string captureId, string planFingerprint, string operationId, long checkpointBefore, int valueCount)
			{
				CaptureId = captureId; PlanFingerprint = planFingerprint; OperationId = operationId;
				CheckpointBefore = checkpointBefore; ValueCount = valueCount;
			}
			internal string CaptureId { get; }
			internal string PlanFingerprint { get; }
			internal string OperationId { get; }
			internal long CheckpointBefore { get; }
			internal int ValueCount { get; }
		}
	}

	public sealed class CollectionLocalRestoreGameValueOwner : IEquatable<CollectionLocalRestoreGameValueOwner>
	{
		private readonly byte[] _value;
		public CollectionLocalRestoreGameValueOwner(string ownerKey, CollectionNativeEffectOwnerKind ownerKind, byte[] value)
		{
			if (String.IsNullOrWhiteSpace(ownerKey)) throw new ArgumentException("A game-specific owner key is required.", nameof(ownerKey));
			OwnerKey = ownerKey; OwnerKind = ownerKind; _value = value == null ? null : (byte[])value.Clone();
		}
		public string OwnerKey { get; }
		public CollectionNativeEffectOwnerKind OwnerKind { get; }
		public byte[] Value { get { return _value == null ? null : (byte[])_value.Clone(); } }
		public bool Equals(CollectionLocalRestoreGameValueOwner other)
		{
			return other != null && StringComparer.OrdinalIgnoreCase.Equals(OwnerKey, other.OwnerKey) &&
				OwnerKind == other.OwnerKind && BytesEqual(_value, other._value);
		}
		public override bool Equals(object obj) { return Equals(obj as CollectionLocalRestoreGameValueOwner); }
		public override int GetHashCode() { return StringComparer.OrdinalIgnoreCase.GetHashCode(OwnerKey); }
		internal static bool BytesEqual(byte[] left, byte[] right)
		{
			if (ReferenceEquals(left, right)) return true;
			if (left == null || right == null || left.Length != right.Length) return false;
			for (int index = 0; index < left.Length; index++) if (left[index] != right[index]) return false;
			return true;
		}
	}

	public sealed class CollectionLocalRestoreGameValueEntry : IEquatable<CollectionLocalRestoreGameValueEntry>
	{
		private readonly ReadOnlyCollection<CollectionLocalRestoreGameValueOwner> _owners;
		private readonly byte[] _physicalValue;
		public CollectionLocalRestoreGameValueEntry(string key, IEnumerable<CollectionLocalRestoreGameValueOwner> owners, byte[] physicalValue)
		{
			if (String.IsNullOrWhiteSpace(key)) throw new ArgumentException("A game-specific state key is required.", nameof(key));
			Key = key;
			List<CollectionLocalRestoreGameValueOwner> copied = (owners ?? throw new ArgumentNullException(nameof(owners))).ToList();
			if (copied.Any(x => x == null)) throw new ArgumentException("Game-specific state cannot contain null owners.", nameof(owners));
			_owners = new ReadOnlyCollection<CollectionLocalRestoreGameValueOwner>(copied);
			_physicalValue = physicalValue == null ? null : (byte[])physicalValue.Clone();
		}
		public string Key { get; }
		public ReadOnlyCollection<CollectionLocalRestoreGameValueOwner> Owners { get { return _owners; } }
		public byte[] PhysicalValue { get { return _physicalValue == null ? null : (byte[])_physicalValue.Clone(); } }
		public bool Equals(CollectionLocalRestoreGameValueEntry other)
		{
			return other != null && StringComparer.Ordinal.Equals(Key, other.Key) &&
				CollectionLocalRestoreGameValueOwner.BytesEqual(_physicalValue, other._physicalValue) &&
				_owners.Count == other._owners.Count && _owners.Zip(other._owners, (a, b) => a.Equals(b)).All(x => x);
		}
		public override bool Equals(object obj) { return Equals(obj as CollectionLocalRestoreGameValueEntry); }
		public override int GetHashCode() { return StringComparer.Ordinal.GetHashCode(Key); }
	}

	public sealed class CollectionLocalRestoreGameValueState : IEquatable<CollectionLocalRestoreGameValueState>
	{
		private readonly ReadOnlyCollection<CollectionLocalRestoreGameValueEntry> _entries;
		public CollectionLocalRestoreGameValueState(IEnumerable<CollectionLocalRestoreGameValueEntry> entries)
		{
			List<CollectionLocalRestoreGameValueEntry> copied = (entries ?? throw new ArgumentNullException(nameof(entries))).ToList();
			if (copied.Any(x => x == null)) throw new ArgumentException("Game-specific state cannot contain null entries.", nameof(entries));
			copied = copied.OrderBy(x => x.Key, StringComparer.Ordinal).ToList();
			_entries = new ReadOnlyCollection<CollectionLocalRestoreGameValueEntry>(copied);
		}
		public ReadOnlyCollection<CollectionLocalRestoreGameValueEntry> Entries { get { return _entries; } }
		public bool Equals(CollectionLocalRestoreGameValueState other)
		{
			return other != null && _entries.Count == other._entries.Count && _entries.Zip(other._entries, (a, b) => a.Equals(b)).All(x => x);
		}
		public override bool Equals(object obj) { return Equals(obj as CollectionLocalRestoreGameValueState); }
		public override int GetHashCode() { return _entries.Count; }
	}
}
