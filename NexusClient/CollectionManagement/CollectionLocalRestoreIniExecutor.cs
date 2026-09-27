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
	/// <summary>Describes how the C7.10b4 INI-history phase reached its verified live state.</summary>
	public enum CollectionLocalRestoreIniOutcome
	{
		NotApplicable = 1,
		AlreadySatisfied = 2,
		RestoredAndVerified = 3,
		RecoveredCommitted = 4
	}

	/// <summary>Result of the C7.10b4 INI-history phase; game-specific/user-metadata restoration remains pending.</summary>
	public sealed class CollectionLocalRestoreIniExecutionResult
	{
		internal CollectionLocalRestoreIniExecutionResult(CollectionOperation operation, CollectionLocalRestoreIniOutcome outcome)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation)); Outcome = outcome;
		}
		public CollectionOperation Operation { get; }
		public CollectionLocalRestoreIniOutcome Outcome { get; }
		public bool IsReadyForGameSpecificStateReconciliation
		{
			get { return Operation.Phase == CollectionOperationPhase.PausedAtSafeBoundary && !Operation.HasUnreconciledNativeChild; }
		}
	}

	/// <summary>Restores exact captured INI values and ordered native owner history through the native ModManager boundary.</summary>
	public sealed class CollectionLocalRestoreIniExecutor
	{
		private const string IntentFormat = "nmm-ce.collections.local-restore-ini-intent/1";
		private const string IntentRole = "local-restore-ini-intent-v1";
		private const string VerifiedRole = "local-restore-ini-verified-v1";
		private const string CompleteRole = "local-restore-ini-phase-complete-v1";

		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionsRetainedArtifactReferenceStore _referenceStore;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;

		public CollectionLocalRestoreIniExecutor(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore)
			: this(services, gameStorageService, operationStore, artifactStore, referenceStore,
				CollectionTargetMutationLeaseManager.Shared, new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services))
		{
		}

		internal CollectionLocalRestoreIniExecutor(ServiceManager services, GameStorageService gameStorageService,
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
			if (_services.ModManager == null) throw new InvalidOperationException("INI restoration requires the live native ModManager.");
		}

		public Task<CollectionLocalRestoreIniExecutionResult> ExecuteAsync(CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan reviewedPlan, CollectionLocalRestoreMemberExecutionResult memberPhase,
			CollectionLocalRestorePluginExecutionResult pluginPhase, GameStoragePathSet paths)
		{
			return ExecuteAsync(sealedCapture, reviewedPlan, memberPhase, pluginPhase, paths, CancellationToken.None);
		}

		public async Task<CollectionLocalRestoreIniExecutionResult> ExecuteAsync(CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan reviewedPlan, CollectionLocalRestoreMemberExecutionResult memberPhase,
			CollectionLocalRestorePluginExecutionResult pluginPhase, GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			ValidateInputs(sealedCapture, reviewedPlan, memberPhase, pluginPhase, paths);
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(reviewedPlan.Target))
				throw new InvalidOperationException("The live canonical target no longer matches the reviewed Local Collection restore.");

			using (CollectionTargetMutationLease rootLease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				_authorityValidator.ValidateAndReload(rootLease, authority, paths);
				CollectionOperation operation = RequireOperation(pluginPhase.Operation.Identity, reviewedPlan);
				bool applicable = sealedCapture.Capture.Scope.Contains(LocalCaptureScopeArea.NativeConfigurationEffects);
				CollectionLocalRestoreIniState desired = BuildDesiredState(sealedCapture, reviewedPlan, memberPhase, applicable);
				CollectionLocalRestoreIniOutcome outcome;

				if (!applicable)
				{
					if (sealedCapture.NativeEffects.IniCoverage == NativeStateCaptureCoverage.NotApplicable && sealedCapture.NativeEffects.IniEdits.Count != 0)
						throw new InvalidDataException("A non-applicable native INI capture cannot contain INI records.");
					outcome = CollectionLocalRestoreIniOutcome.NotApplicable;
				}
				else
				{
					string ownerId = operation.Identity.OperationId.ToString("D");
					CollectionLocalRestoreIniState live = CaptureCurrentState(reviewedPlan.Target);
					CollectionsRetainedArtifactReferenceRecord verified = _referenceStore.GetReferenceForOwnerRole(
						CollectionsRetainedArtifactOwnerKind.Operation, ownerId, VerifiedRole);
					if (verified != null)
					{
						IniIntent verifiedIntent = ReadIntent(verified.ArtifactId);
						ValidateIntent(verifiedIntent, sealedCapture, reviewedPlan, desired);
						if (!live.Equals(desired) || !VmaReplayMatches(desired))
						{
							MarkRecoveryRequired(operation);
							throw new InvalidOperationException("A durable INI verified checkpoint no longer matches authoritative native INI state.");
						}
						outcome = CollectionLocalRestoreIniOutcome.AlreadySatisfied;
					}
					else
					{
						CollectionsRetainedArtifactReferenceRecord existingIntent = _referenceStore.GetReferenceForOwnerRole(
							CollectionsRetainedArtifactOwnerKind.Operation, ownerId, IntentRole);
						IniIntent intent;
						if (existingIntent == null)
						{
							intent = new IniIntent(sealedCapture.Capture.Identity.ToString(), reviewedPlan.PlanFingerprint, live, desired);
							existingIntent = PersistIntent(ownerId, intent);
						}
						else
						{
							try { intent = ReadIntent(existingIntent.ArtifactId); ValidateIntent(intent, sealedCapture, reviewedPlan, desired); }
							catch { MarkRecoveryRequired(operation); throw; }
							if (live.Equals(desired) && VmaReplayMatches(desired))
							{
								MarkVerified(ownerId, existingIntent.ArtifactId);
								outcome = CollectionLocalRestoreIniOutcome.RecoveredCommitted;
								operation = CompleteIniPhase(operation, sealedCapture.Capture.Identity, reviewedPlan, desired.Entries.Count);
								return new CollectionLocalRestoreIniExecutionResult(operation, outcome);
							}
							if (!live.Equals(intent.Preimage))
							{
								MarkRecoveryRequired(operation);
								throw new InvalidOperationException("The live INI state differs from both its exact preimage and desired restore postimage.");
							}
						}

						cancellationToken.ThrowIfCancellationRequested();
						_services.ModManager.RestoreCapturedIniState(BuildNativeEntries(desired));
						CollectionLocalRestoreIniState postimage = CaptureCurrentState(reviewedPlan.Target);
						if (!postimage.Equals(desired) || !VmaReplayMatches(desired))
						{
							MarkRecoveryRequired(operation);
							throw new InvalidOperationException("Native INI restoration returned without establishing the exact captured value/history state.");
						}
						MarkVerified(ownerId, existingIntent.ArtifactId);
						outcome = CollectionLocalRestoreIniOutcome.RestoredAndVerified;
					}
				}

				operation = CompleteIniPhase(operation, sealedCapture.Capture.Identity, reviewedPlan, desired.Entries.Count);
				return new CollectionLocalRestoreIniExecutionResult(operation, outcome);
			}
		}

		private void ValidateInputs(CollectionSealedCaptureSnapshot sealedCapture, CollectionLocalRestorePlan reviewedPlan,
			CollectionLocalRestoreMemberExecutionResult memberPhase, CollectionLocalRestorePluginExecutionResult pluginPhase,
			GameStoragePathSet paths)
		{
			if (sealedCapture == null) throw new ArgumentNullException(nameof(sealedCapture));
			if (reviewedPlan == null) throw new ArgumentNullException(nameof(reviewedPlan));
			if (memberPhase == null) throw new ArgumentNullException(nameof(memberPhase));
			if (pluginPhase == null) throw new ArgumentNullException(nameof(pluginPhase));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			if (!reviewedPlan.IsReadyForReview || sealedCapture.Capture.Capability != LocalCaptureCapability.LocallyRestorableWithinScope)
				throw new InvalidOperationException("INI restoration requires one reviewed locally-restorable C7.9 plan.");
			if (!sealedCapture.Capture.Identity.Equals(reviewedPlan.CaptureIdentity) || !sealedCapture.NativeEffects.Target.Equals(reviewedPlan.Target))
				throw new InvalidOperationException("INI restoration inputs do not belong to the same Local Collection capture and target.");
			if (!pluginPhase.IsReadyForIniStateReconciliation || pluginPhase.Operation.Kind != CollectionOperationKind.RestoreLocalCapture ||
				!pluginPhase.Operation.Target.Equals(reviewedPlan.Target) || !memberPhase.Operation.Identity.Equals(pluginPhase.Operation.Identity))
				throw new InvalidOperationException("INI restoration requires the verified plugin safe boundary of the same restore operation.");
			if (sealedCapture.Capture.Scope.Contains(LocalCaptureScopeArea.NativeConfigurationEffects) &&
				sealedCapture.NativeEffects.IniCoverage != NativeStateCaptureCoverage.Complete &&
				sealedCapture.NativeEffects.IniCoverage != NativeStateCaptureCoverage.NotApplicable)
				throw new InvalidDataException("A locally-restorable INI scope requires complete or non-applicable captured INI coverage.");
		}

		private CollectionOperation RequireOperation(CollectionOperationIdentity identity, CollectionLocalRestorePlan plan)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Kind != CollectionOperationKind.RestoreLocalCapture || operation.IsTerminal ||
				operation.ResultState != CollectionOperationResultState.Pending || operation.Phase != CollectionOperationPhase.PausedAtSafeBoundary ||
				!operation.Target.Equals(plan.Target) || operation.HasUnreconciledNativeChild)
				throw new InvalidOperationException("INI restoration requires the exact active Local Collection restore operation at a reconciled safe boundary.");
			return operation;
		}

		private CollectionLocalRestoreIniState BuildDesiredState(CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan plan, CollectionLocalRestoreMemberExecutionResult memberPhase, bool applicable)
		{
			if (!applicable) return new CollectionLocalRestoreIniState(new CollectionLocalRestoreIniEntry[0]);
			Dictionary<CollectionMemberKey, string> remaps = memberPhase.MemberRemaps.ToDictionary(x => x.MemberKey, x => x.NativeKey);
			Dictionary<string, CollectionMemberKey> membersByCapturedKey = plan.Members.ToDictionary(x => x.CapturedNativeKey,
				x => x.SnapshotMemberKey, StringComparer.OrdinalIgnoreCase);
			var entries = new List<CollectionLocalRestoreIniEntry>();
			foreach (CollectionCapturedIniEffect effect in sealedCapture.NativeEffects.IniEdits)
			{
				if (effect.Values.Count == 0) throw new InvalidDataException("A captured INI setting has no owner history.");
				var owners = new List<CollectionLocalRestoreIniOwner>();
				foreach (CollectionCapturedIniOwnerValue owner in effect.Values)
				{
					if (owner.OwnerKind == CollectionNativeEffectOwnerKind.OriginalValue)
						owners.Add(new CollectionLocalRestoreIniOwner(plan.CurrentOriginalValuesKey, owner.OwnerKind, owner.Value));
					else if (owner.OwnerKind == CollectionNativeEffectOwnerKind.NativeMod)
					{
						CollectionMemberKey memberKey; string nativeKey;
						if (!membersByCapturedKey.TryGetValue(owner.OwnerKey, out memberKey) || !remaps.TryGetValue(memberKey, out nativeKey))
							throw new InvalidOperationException("A captured INI owner has no current C7.10a native-key remap.");
						owners.Add(new CollectionLocalRestoreIniOwner(nativeKey, owner.OwnerKind, owner.Value));
					}
					else throw new InvalidDataException("A locally-restorable INI capture contains an unresolved owner.");
				}
				entries.Add(new CollectionLocalRestoreIniEntry(effect.File, effect.Section, effect.Key, owners,
					effect.Values[effect.Values.Count - 1].Value));
			}
			return new CollectionLocalRestoreIniState(entries);
		}

		private CollectionLocalRestoreIniState CaptureCurrentState(CollectionTargetIdentity target)
		{
			ModManager manager = _services.ModManager;
			var nativeReader = new NativeStateCaptureReader(manager.InstallationLog, manager.VirtualModActivator,
				manager.DeploymentManager, _services.PluginManager, manager.GameMode);
			NativeStateCaptureSnapshot native = nativeReader.Capture();
			CollectionNativeEffectSnapshot effects = new CollectionNativeEffectCaptureReader(nativeReader).Capture(target, native);
			if (effects.IniCoverage != NativeStateCaptureCoverage.Complete && effects.IniCoverage != NativeStateCaptureCoverage.NotApplicable)
				throw new InvalidOperationException("Current native INI state is ambiguous and cannot be used as exact restart evidence.");
			var entries = effects.IniEdits.Select(effect => new CollectionLocalRestoreIniEntry(effect.File, effect.Section, effect.Key,
				effect.Values.Select(x => new CollectionLocalRestoreIniOwner(x.OwnerKey, x.OwnerKind, x.Value)),
				IniMethods.GetPrivateProfileString(effect.Section, effect.Key, null, effect.File)));
			return new CollectionLocalRestoreIniState(entries);
		}

		private bool VmaReplayMatches(CollectionLocalRestoreIniState desired)
		{
			return _services.ModManager.VirtualModActivator.CapturedIniEditLogMatches(BuildNativeEntries(desired));
		}

		private IReadOnlyList<ModIniRestoreEntry> BuildNativeEntries(CollectionLocalRestoreIniState desired)
		{
			IInstallLog installLog = _services.ModManager.InstallationLog;
			var result = new List<ModIniRestoreEntry>();
			foreach (CollectionLocalRestoreIniEntry entry in desired.Entries)
			{
				var owners = new List<ModIniRestoreOwner>();
				foreach (CollectionLocalRestoreIniOwner owner in entry.Owners)
				{
					bool original = owner.OwnerKind == CollectionNativeEffectOwnerKind.OriginalValue;
					IMod mod = original ? null : _services.ModManager.ActiveMods.FirstOrDefault(x =>
						owner.OwnerKey.Equals(installLog.GetModKey(x), StringComparison.OrdinalIgnoreCase));
					if (!original && mod == null) throw new InvalidOperationException("A desired INI owner is no longer an active native mod registration.");
					owners.Add(new ModIniRestoreOwner(owner.OwnerKey, original, mod, owner.Value));
				}
				result.Add(new ModIniRestoreEntry(entry.File, entry.Section, entry.Key, owners));
			}
			return result;
		}

		private CollectionsRetainedArtifactReferenceRecord PersistIntent(string ownerId, IniIntent intent)
		{
			CollectionsRetainedArtifact artifact; using (var stream = new MemoryStream(SerializeIntent(intent), false)) artifact = _artifactStore.Publish(stream);
			_referenceStore.AcquireExclusiveRoleReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation, ownerId, IntentRole);
			return _referenceStore.GetReferenceForOwnerRole(CollectionsRetainedArtifactOwnerKind.Operation, ownerId, IntentRole);
		}
		private void MarkVerified(string ownerId, string artifactId)
		{
			_referenceStore.AcquireExclusiveRoleReference(artifactId, CollectionsRetainedArtifactOwnerKind.Operation, ownerId, VerifiedRole);
		}
		private static byte[] SerializeIntent(IniIntent intent)
		{
			using (var stream = new MemoryStream()) using (var text = new StreamWriter(stream, new UTF8Encoding(false), 4096, true)) using (var writer = new JsonTextWriter(text))
			{
				writer.WriteStartObject(); writer.WritePropertyName("format"); writer.WriteValue(IntentFormat);
				writer.WritePropertyName("captureId"); writer.WriteValue(intent.CaptureId); writer.WritePropertyName("planFingerprint"); writer.WriteValue(intent.PlanFingerprint);
				writer.WritePropertyName("preimage"); WriteState(writer, intent.Preimage); writer.WritePropertyName("desired"); WriteState(writer, intent.Desired);
				writer.WriteEndObject(); writer.Flush(); text.Flush(); return stream.ToArray();
			}
		}
		private IniIntent ReadIntent(string artifactId)
		{
			if (!_artifactStore.VerifyArtifact(artifactId)) throw new InvalidDataException("The durable INI restore intent no longer matches its retained bytes.");
			using (Stream stream = _artifactStore.OpenRead(artifactId)) using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, false))
			{
				JObject root = JObject.Parse(reader.ReadToEnd()); if (!StringComparer.Ordinal.Equals((string)root["format"], IntentFormat)) throw new InvalidDataException("Unsupported Local restore INI intent format.");
				return new IniIntent((string)root["captureId"], (string)root["planFingerprint"], ReadState(root["preimage"] as JArray), ReadState(root["desired"] as JArray));
			}
		}
		private static void ValidateIntent(IniIntent intent, CollectionSealedCaptureSnapshot sealedCapture, CollectionLocalRestorePlan plan, CollectionLocalRestoreIniState desired)
		{
			if (intent == null || !StringComparer.Ordinal.Equals(intent.CaptureId, sealedCapture.Capture.Identity.ToString()) ||
				!StringComparer.Ordinal.Equals(intent.PlanFingerprint, plan.PlanFingerprint) || !intent.Desired.Equals(desired))
				throw new InvalidDataException("The durable INI restore intent differs from the reviewed Local Collection restore.");
		}

		private CollectionOperation CompleteIniPhase(CollectionOperation operation, LocalCaptureIdentity captureIdentity, CollectionLocalRestorePlan plan, int iniCount)
		{
			string ownerId = operation.Identity.OperationId.ToString("D"); CollectionsRetainedArtifactReferenceRecord existing = _referenceStore.GetReferenceForOwnerRole(CollectionsRetainedArtifactOwnerKind.Operation, ownerId, CompleteRole);
			IniPhaseCompletion completion;
			if (existing == null)
			{
				CollectionOperation current = _operationStore.GetOperation(operation.Identity) ?? operation;
				completion = new IniPhaseCompletion(captureIdentity.ToString(), plan.PlanFingerprint, ownerId, current.CheckpointSequence, iniCount);
				CollectionsRetainedArtifact artifact; using (var stream = new MemoryStream(SerializePhaseCompletion(completion), false)) artifact = _artifactStore.Publish(stream);
				_referenceStore.AcquireExclusiveRoleReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation, ownerId, CompleteRole);
			}
			else
			{
				try { completion = ReadPhaseCompletion(existing.ArtifactId); } catch { MarkRecoveryRequired(operation); throw; }
				if (!StringComparer.Ordinal.Equals(completion.CaptureId, captureIdentity.ToString()) || !StringComparer.Ordinal.Equals(completion.PlanFingerprint, plan.PlanFingerprint) ||
					!StringComparer.Ordinal.Equals(completion.OperationId, ownerId) || completion.IniCount != iniCount)
				{ MarkRecoveryRequired(operation); throw new InvalidDataException("The durable INI phase-completion marker differs from the reviewed restore."); }
			}
			CollectionOperation persisted = _operationStore.GetOperation(operation.Identity) ?? operation;
			if (persisted.CheckpointSequence == completion.CheckpointBefore) return AdvanceSafeBoundary(persisted);
			if (persisted.CheckpointSequence == completion.CheckpointBefore + 1 && persisted.Phase == CollectionOperationPhase.PausedAtSafeBoundary && persisted.ResultState == CollectionOperationResultState.Pending) return persisted;
			MarkRecoveryRequired(persisted); throw new InvalidOperationException("The INI phase-completion checkpoint no longer matches the durable operation journal.");
		}
		private static byte[] SerializePhaseCompletion(IniPhaseCompletion completion)
		{
			using (var stream = new MemoryStream()) using (var text = new StreamWriter(stream, new UTF8Encoding(false), 4096, true)) using (var writer = new JsonTextWriter(text))
			{
				writer.WriteStartObject(); writer.WritePropertyName("format"); writer.WriteValue("nmm-ce.collections.local-restore-ini-phase/1");
				writer.WritePropertyName("captureId"); writer.WriteValue(completion.CaptureId); writer.WritePropertyName("planFingerprint"); writer.WriteValue(completion.PlanFingerprint);
				writer.WritePropertyName("operationId"); writer.WriteValue(completion.OperationId); writer.WritePropertyName("checkpointBefore"); writer.WriteValue(completion.CheckpointBefore);
				writer.WritePropertyName("iniCount"); writer.WriteValue(completion.IniCount); writer.WriteEndObject(); writer.Flush(); text.Flush(); return stream.ToArray();
			}
		}
		private IniPhaseCompletion ReadPhaseCompletion(string artifactId)
		{
			if (!_artifactStore.VerifyArtifact(artifactId)) throw new InvalidDataException("The durable INI phase-completion marker no longer matches its retained bytes.");
			using (Stream stream = _artifactStore.OpenRead(artifactId)) using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, false))
			{
				JObject root = JObject.Parse(reader.ReadToEnd()); if (!StringComparer.Ordinal.Equals((string)root["format"], "nmm-ce.collections.local-restore-ini-phase/1")) throw new InvalidDataException("Unsupported Local restore INI phase-completion format.");
				return new IniPhaseCompletion((string)root["captureId"], (string)root["planFingerprint"], (string)root["operationId"], (long)root["checkpointBefore"], (int)root["iniCount"]);
			}
		}

		private static void WriteState(JsonWriter writer, CollectionLocalRestoreIniState state)
		{
			writer.WriteStartArray(); foreach (CollectionLocalRestoreIniEntry entry in state.Entries)
			{
				writer.WriteStartObject(); writer.WritePropertyName("file"); writer.WriteValue(entry.File); writer.WritePropertyName("section"); writer.WriteValue(entry.Section); writer.WritePropertyName("key"); writer.WriteValue(entry.Key);
				writer.WritePropertyName("effectiveValue"); writer.WriteValue(entry.EffectiveValue); writer.WritePropertyName("owners"); writer.WriteStartArray();
				foreach (CollectionLocalRestoreIniOwner owner in entry.Owners)
				{ writer.WriteStartObject(); writer.WritePropertyName("ownerKey"); writer.WriteValue(owner.OwnerKey); writer.WritePropertyName("ownerKind"); writer.WriteValue((int)owner.OwnerKind); writer.WritePropertyName("value"); writer.WriteValue(owner.Value); writer.WriteEndObject(); }
				writer.WriteEndArray(); writer.WriteEndObject();
			} writer.WriteEndArray();
		}
		private static CollectionLocalRestoreIniState ReadState(JArray array)
		{
			if (array == null) throw new InvalidDataException("An INI restore intent is missing one exact INI-state sequence.");
			return new CollectionLocalRestoreIniState(array.OfType<JObject>().Select(x => new CollectionLocalRestoreIniEntry((string)x["file"], (string)x["section"], (string)x["key"],
				((JArray)x["owners"]).OfType<JObject>().Select(o => new CollectionLocalRestoreIniOwner((string)o["ownerKey"], (CollectionNativeEffectOwnerKind)(int)o["ownerKind"], o["value"].Type == JTokenType.Null ? null : (string)o["value"])),
				x["effectiveValue"].Type == JTokenType.Null ? null : (string)x["effectiveValue"])));
		}

		private CollectionOperation AdvanceSafeBoundary(CollectionOperation operation)
		{
			CollectionOperation current = _operationStore.GetOperation(operation.Identity) ?? operation;
			CollectionOperation updated = new CollectionOperation(current.Identity, current.Kind, current.Collection, current.Target, current.Revision, current.PlanIdentity,
				current.CheckpointSequence + 1, CollectionOperationPhase.PausedAtSafeBoundary, CollectionOperationResultState.Pending, current.NativeChildren);
			_operationStore.SaveOperation(updated); return updated;
		}
		private void MarkRecoveryRequired(CollectionOperation operation)
		{
			CollectionOperation current = _operationStore.GetOperation(operation.Identity) ?? operation; if (current.IsTerminal || current.Phase == CollectionOperationPhase.RecoveryRequired) return;
			CollectionOperation updated = new CollectionOperation(current.Identity, current.Kind, current.Collection, current.Target, current.Revision, current.PlanIdentity,
				current.CheckpointSequence + 1, CollectionOperationPhase.RecoveryRequired, CollectionOperationResultState.RecoveryRequired, current.NativeChildren);
			_operationStore.SaveOperation(updated);
		}

		private sealed class IniIntent
		{
			internal IniIntent(string captureId, string planFingerprint, CollectionLocalRestoreIniState preimage, CollectionLocalRestoreIniState desired)
			{ CaptureId = captureId ?? String.Empty; PlanFingerprint = planFingerprint ?? String.Empty; Preimage = preimage ?? throw new ArgumentNullException(nameof(preimage)); Desired = desired ?? throw new ArgumentNullException(nameof(desired)); }
			internal string CaptureId { get; } internal string PlanFingerprint { get; } internal CollectionLocalRestoreIniState Preimage { get; } internal CollectionLocalRestoreIniState Desired { get; }
		}
		private sealed class IniPhaseCompletion
		{
			internal IniPhaseCompletion(string captureId, string planFingerprint, string operationId, long checkpointBefore, int iniCount)
			{ CaptureId = captureId; PlanFingerprint = planFingerprint; OperationId = operationId; CheckpointBefore = checkpointBefore; IniCount = iniCount; }
			internal string CaptureId { get; } internal string PlanFingerprint { get; } internal string OperationId { get; } internal long CheckpointBefore { get; } internal int IniCount { get; }
		}
	}

	public sealed class CollectionLocalRestoreIniOwner : IEquatable<CollectionLocalRestoreIniOwner>
	{
		public CollectionLocalRestoreIniOwner(string ownerKey, CollectionNativeEffectOwnerKind ownerKind, string value)
		{ if (String.IsNullOrWhiteSpace(ownerKey)) throw new ArgumentException("An INI owner key is required.", nameof(ownerKey)); OwnerKey = ownerKey; OwnerKind = ownerKind; Value = value; }
		public string OwnerKey { get; } public CollectionNativeEffectOwnerKind OwnerKind { get; } public string Value { get; }
		public bool Equals(CollectionLocalRestoreIniOwner other) { return other != null && StringComparer.OrdinalIgnoreCase.Equals(OwnerKey, other.OwnerKey) && OwnerKind == other.OwnerKind && StringComparer.Ordinal.Equals(Value, other.Value); }
		public override bool Equals(object obj) { return Equals(obj as CollectionLocalRestoreIniOwner); } public override int GetHashCode() { return StringComparer.OrdinalIgnoreCase.GetHashCode(OwnerKey); }
	}
	public sealed class CollectionLocalRestoreIniEntry : IEquatable<CollectionLocalRestoreIniEntry>
	{
		private readonly ReadOnlyCollection<CollectionLocalRestoreIniOwner> _owners;
		public CollectionLocalRestoreIniEntry(string file, string section, string key, IEnumerable<CollectionLocalRestoreIniOwner> owners, string effectiveValue)
		{
			File = file ?? String.Empty; Section = section ?? String.Empty; Key = key ?? String.Empty; List<CollectionLocalRestoreIniOwner> copied = (owners ?? throw new ArgumentNullException(nameof(owners))).ToList();
			if (copied.Any(x => x == null)) throw new ArgumentException("INI state cannot contain null owners.", nameof(owners)); _owners = new ReadOnlyCollection<CollectionLocalRestoreIniOwner>(copied); EffectiveValue = effectiveValue;
		}
		public string File { get; } public string Section { get; } public string Key { get; } public ReadOnlyCollection<CollectionLocalRestoreIniOwner> Owners { get { return _owners; } } public string EffectiveValue { get; }
		public bool Equals(CollectionLocalRestoreIniEntry other)
		{
			return other != null && StringComparer.OrdinalIgnoreCase.Equals(File, other.File) && StringComparer.OrdinalIgnoreCase.Equals(Section, other.Section) && StringComparer.OrdinalIgnoreCase.Equals(Key, other.Key) &&
				StringComparer.Ordinal.Equals(EffectiveValue, other.EffectiveValue) && _owners.Count == other._owners.Count && _owners.Zip(other._owners, (a,b) => a.Equals(b)).All(x => x);
		}
		public override bool Equals(object obj) { return Equals(obj as CollectionLocalRestoreIniEntry); } public override int GetHashCode() { return StringComparer.OrdinalIgnoreCase.GetHashCode(File + "|" + Section + "|" + Key); }
	}
	public sealed class CollectionLocalRestoreIniState : IEquatable<CollectionLocalRestoreIniState>
	{
		private readonly ReadOnlyCollection<CollectionLocalRestoreIniEntry> _entries;
		public CollectionLocalRestoreIniState(IEnumerable<CollectionLocalRestoreIniEntry> entries)
		{
			List<CollectionLocalRestoreIniEntry> copied = (entries ?? throw new ArgumentNullException(nameof(entries))).ToList(); if (copied.Any(x => x == null)) throw new ArgumentException("INI state cannot contain null entries.", nameof(entries));
			copied = copied.OrderBy(x => x.File, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Section, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.File, StringComparer.Ordinal).ThenBy(x => x.Section, StringComparer.Ordinal).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();
			_entries = new ReadOnlyCollection<CollectionLocalRestoreIniEntry>(copied);
		}
		public ReadOnlyCollection<CollectionLocalRestoreIniEntry> Entries { get { return _entries; } }
		public bool Equals(CollectionLocalRestoreIniState other) { return other != null && _entries.Count == other._entries.Count && _entries.Zip(other._entries, (a,b) => a.Equals(b)).All(x => x); }
		public override bool Equals(object obj) { return Equals(obj as CollectionLocalRestoreIniState); } public override int GetHashCode() { return _entries.Count; }
	}
}
