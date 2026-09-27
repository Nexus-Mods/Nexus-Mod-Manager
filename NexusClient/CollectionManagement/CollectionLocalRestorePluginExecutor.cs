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
using Nexus.Client.PluginManagement;
using Nexus.Client.Plugins;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Describes how the C7.10b3 plugin-state phase reached its verified live state.</summary>
	public enum CollectionLocalRestorePluginOutcome
	{
		NotApplicable = 1,
		AlreadySatisfied = 2,
		RestoredAndVerified = 3,
		RecoveredCommitted = 4
	}

	/// <summary>Result of the C7.10b3 plugin-state phase; INI/game-specific/user-metadata restoration remains pending.</summary>
	public sealed class CollectionLocalRestorePluginExecutionResult
	{
		internal CollectionLocalRestorePluginExecutionResult(CollectionOperation operation, CollectionLocalRestorePluginOutcome outcome)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Outcome = outcome;
		}

		public CollectionOperation Operation { get; }
		public CollectionLocalRestorePluginOutcome Outcome { get; }
		public bool IsReadyForIniStateReconciliation
		{
			get { return Operation.Phase == CollectionOperationPhase.PausedAtSafeBoundary && !Operation.HasUnreconciledNativeChild; }
		}
	}

	/// <summary>Restores the complete captured plugin order and activation state through the native plugin-policy boundary.</summary>
	/// <remarks>
	/// The executor persists the exact live plugin preimage before mutation. Restart reconciliation accepts only that exact
	/// preimage or the exact desired postimage; the native PluginManager transaction owns the order/activation write atomicity.
	/// </remarks>
	public sealed class CollectionLocalRestorePluginExecutor
	{
		private const string IntentFormat = "nmm-ce.collections.local-restore-plugin-intent/1";
		private const string IntentRole = "local-restore-plugin-intent-v1";
		private const string VerifiedRole = "local-restore-plugin-verified-v1";
		private const string CompleteRole = "local-restore-plugin-phase-complete-v1";

		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionsRetainedArtifactReferenceStore _referenceStore;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;

		/// <summary>Creates the production plugin-state restoration executor.</summary>
		public CollectionLocalRestorePluginExecutor(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore)
			: this(services, gameStorageService, operationStore, artifactStore, referenceStore,
				CollectionTargetMutationLeaseManager.Shared, new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services))
		{
		}

		internal CollectionLocalRestorePluginExecutor(ServiceManager services, GameStorageService gameStorageService,
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
			if (_services.ModManager == null || _services.PluginManager == null)
				throw new InvalidOperationException("Plugin restoration requires the live native ModManager and plugin manager.");
		}

		/// <summary>Restores the captured plugin state after replay restoration reached its verified safe boundary.</summary>
		public Task<CollectionLocalRestorePluginExecutionResult> ExecuteAsync(CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan reviewedPlan, CollectionLocalRestoreReplayExecutionResult replayPhase,
			GameStoragePathSet paths)
		{
			return ExecuteAsync(sealedCapture, reviewedPlan, replayPhase, paths, CancellationToken.None);
		}

		/// <summary>Restores the captured plugin state with cooperative cancellation before the atomic native mutation.</summary>
		public async Task<CollectionLocalRestorePluginExecutionResult> ExecuteAsync(CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan reviewedPlan, CollectionLocalRestoreReplayExecutionResult replayPhase,
			GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			ValidateInputs(sealedCapture, reviewedPlan, replayPhase, paths);
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(reviewedPlan.Target))
				throw new InvalidOperationException("The live canonical target no longer matches the reviewed Local Collection restore.");

			using (CollectionTargetMutationLease rootLease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				_authorityValidator.ValidateAndReload(rootLease, authority, paths);
				CollectionOperation operation = RequireOperation(replayPhase.Operation.Identity, reviewedPlan);
				IPluginManager pluginManager = _services.PluginManager;
				bool applicable = sealedCapture.Capture.Scope.Contains(LocalCaptureScopeArea.PluginState) &&
					sealedCapture.NativeEffects.PluginCoverage != NativeStateCaptureCoverage.NotApplicable;
				CollectionLocalRestorePluginState desired = BuildDesiredState(sealedCapture, pluginManager, applicable);
				CollectionLocalRestorePluginOutcome outcome;

				if (!applicable)
				{
					if (sealedCapture.NativeEffects.PluginCoverage == NativeStateCaptureCoverage.NotApplicable &&
						sealedCapture.NativeEffects.Plugins.Count != 0)
						throw new InvalidDataException("A non-applicable native plugin capture cannot contain plugin records.");
					outcome = CollectionLocalRestorePluginOutcome.NotApplicable;
				}
				else
				{
					ValidatePolicyCanReproduceDesired(pluginManager, desired);
					string ownerId = operation.Identity.OperationId.ToString("D");
					CollectionsRetainedArtifactReferenceRecord verified = _referenceStore.GetReferenceForOwnerRole(
						CollectionsRetainedArtifactOwnerKind.Operation, ownerId, VerifiedRole);
					CollectionLocalRestorePluginState live = CaptureState(pluginManager);

					if (verified != null)
					{
						PluginIntent verifiedIntent = ReadIntent(verified.ArtifactId);
						ValidateIntent(verifiedIntent, sealedCapture, reviewedPlan, desired);
						if (!live.Equals(desired))
						{
							MarkRecoveryRequired(operation);
							throw new InvalidOperationException("A durable plugin verified checkpoint no longer matches authoritative plugin state.");
						}
						outcome = CollectionLocalRestorePluginOutcome.AlreadySatisfied;
					}
					else
					{
						CollectionsRetainedArtifactReferenceRecord existingIntent = _referenceStore.GetReferenceForOwnerRole(
							CollectionsRetainedArtifactOwnerKind.Operation, ownerId, IntentRole);
						PluginIntent intent;
						if (existingIntent == null)
						{
							intent = new PluginIntent(sealedCapture.Capture.Identity.ToString(), reviewedPlan.PlanFingerprint,
								live, desired);
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
								outcome = CollectionLocalRestorePluginOutcome.RecoveredCommitted;
								operation = CompletePluginPhase(operation, sealedCapture.Capture.Identity, reviewedPlan,
									desired.Entries.Count);
								return new CollectionLocalRestorePluginExecutionResult(operation, outcome);
							}
							if (!live.Equals(intent.Preimage))
							{
								MarkRecoveryRequired(operation);
								throw new InvalidOperationException("The live plugin state differs from both its exact preimage and desired restore postimage.");
							}
						}

						cancellationToken.ThrowIfCancellationRequested();
						ApplyDesiredState(pluginManager, desired);
						CollectionLocalRestorePluginState postimage = CaptureState(pluginManager);
						if (!postimage.Equals(desired))
						{
							MarkRecoveryRequired(operation);
							throw new InvalidOperationException("Native plugin restoration returned without establishing the exact captured plugin state.");
						}
						MarkVerified(ownerId, existingIntent.ArtifactId);
						outcome = CollectionLocalRestorePluginOutcome.RestoredAndVerified;
					}
				}

				operation = CompletePluginPhase(operation, sealedCapture.Capture.Identity, reviewedPlan, desired.Entries.Count);
				return new CollectionLocalRestorePluginExecutionResult(operation, outcome);
			}
		}

		private void ValidateInputs(CollectionSealedCaptureSnapshot sealedCapture, CollectionLocalRestorePlan reviewedPlan,
			CollectionLocalRestoreReplayExecutionResult replayPhase, GameStoragePathSet paths)
		{
			if (sealedCapture == null) throw new ArgumentNullException(nameof(sealedCapture));
			if (reviewedPlan == null) throw new ArgumentNullException(nameof(reviewedPlan));
			if (replayPhase == null) throw new ArgumentNullException(nameof(replayPhase));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			if (!reviewedPlan.IsReadyForReview || sealedCapture.Capture.Capability != LocalCaptureCapability.LocallyRestorableWithinScope)
				throw new InvalidOperationException("Plugin restoration requires one reviewed locally-restorable C7.9 plan.");
			if (!sealedCapture.Capture.Identity.Equals(reviewedPlan.CaptureIdentity) ||
				!sealedCapture.NativeEffects.Target.Equals(reviewedPlan.Target))
				throw new InvalidOperationException("Plugin restoration inputs do not belong to the same Local Collection capture and target.");
			if (!replayPhase.IsReadyForPluginStateReconciliation || replayPhase.Operation.Kind != CollectionOperationKind.RestoreLocalCapture ||
				!replayPhase.Operation.Target.Equals(reviewedPlan.Target))
				throw new InvalidOperationException("Plugin restoration requires the verified replay safe boundary of the same restore operation.");
			if (sealedCapture.Capture.Scope.Contains(LocalCaptureScopeArea.PluginState) &&
				sealedCapture.NativeEffects.PluginCoverage != NativeStateCaptureCoverage.Complete &&
				sealedCapture.NativeEffects.PluginCoverage != NativeStateCaptureCoverage.NotApplicable)
				throw new InvalidDataException("A locally-restorable plugin scope requires complete or non-applicable captured plugin coverage.");
		}

		private CollectionOperation RequireOperation(CollectionOperationIdentity identity, CollectionLocalRestorePlan plan)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Kind != CollectionOperationKind.RestoreLocalCapture || operation.IsTerminal ||
				operation.ResultState != CollectionOperationResultState.Pending || operation.Phase != CollectionOperationPhase.PausedAtSafeBoundary ||
				!operation.Target.Equals(plan.Target) || operation.HasUnreconciledNativeChild)
				throw new InvalidOperationException("Plugin restoration requires the exact active Local Collection restore operation at a reconciled safe boundary.");
			return operation;
		}

		private static CollectionLocalRestorePluginState BuildDesiredState(CollectionSealedCaptureSnapshot sealedCapture,
			IPluginManager pluginManager, bool applicable)
		{
			if (!applicable)
				return new CollectionLocalRestorePluginState(new CollectionLocalRestorePluginEntry[0]);
			List<NativeStateCapturePlugin> captured = sealedCapture.NativeEffects.Plugins.OrderBy(x => x.Priority).ToList();
			var entries = new List<CollectionLocalRestorePluginEntry>(captured.Count);
			for (int index = 0; index < captured.Count; index++)
			{
				NativeStateCapturePlugin plugin = captured[index];
				if (plugin.Priority != index)
					throw new InvalidDataException("The captured plugin state is not a contiguous exact load-order sequence.");
				Plugin registered = pluginManager.GetRegisteredPlugin(plugin.FileName);
				if (registered == null)
					throw new InvalidOperationException("A captured plugin is not registered in current native plugin state: " + plugin.FileName);
				entries.Add(new CollectionLocalRestorePluginEntry(registered.Filename, plugin.Active, plugin.Priority,
					plugin.AllocatedIndex, plugin.ModIndex));
			}
			if (entries.GroupBy(x => x.FileName, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() != 1))
				throw new InvalidDataException("The captured plugin state contains duplicate plugin identities.");
			return new CollectionLocalRestorePluginState(entries);
		}

		private static CollectionLocalRestorePluginState CaptureState(IPluginManager pluginManager)
		{
			PluginSnapshot snapshot = pluginManager.CurrentSnapshot;
			if (snapshot == null)
				throw new InvalidOperationException("The authoritative native plugin snapshot is unavailable.");
			return new CollectionLocalRestorePluginState(snapshot.Entries.Where(x => x != null && x.Plugin != null)
				.OrderBy(x => x.Priority).Select(x => new CollectionLocalRestorePluginEntry(x.Plugin.Filename, x.Active,
					x.Priority, x.AllocatedIndex, x.ModIndex)));
		}

		private static void ValidatePolicyCanReproduceDesired(IPluginManager pluginManager, CollectionLocalRestorePluginState desired)
		{
			Dictionary<string, Plugin> registered = desired.Entries.ToDictionary(x => x.FileName,
				x => pluginManager.GetRegisteredPlugin(x.FileName), StringComparer.OrdinalIgnoreCase);
			if (registered.Values.Any(x => x == null))
				throw new InvalidOperationException("The captured plugin state references an unavailable current plugin registration.");
			List<Plugin> desiredOrder = desired.Entries.Select(x => registered[x.FileName]).ToList();
			List<Plugin> desiredActive = desired.Entries.Where(x => x.Active).Select(x => registered[x.FileName]).ToList();
			PluginSnapshot current = pluginManager.CurrentSnapshot;
			if (current == null)
				throw new InvalidOperationException("The current plugin snapshot is unavailable for policy validation.");
			List<Plugin> currentOrder = current.Entries.Where(x => x != null && x.Plugin != null).OrderBy(x => x.Priority).Select(x => x.Plugin).ToList();
			List<Plugin> currentActive = current.Entries.Where(x => x != null && x.Plugin != null && x.Active).Select(x => x.Plugin).ToList();
			PluginStateResolution resolved = pluginManager.ResolvePluginState(currentOrder, currentActive, desiredOrder, desiredActive);
			if (resolved == null || !resolved.IsAllowed ||
				!resolved.OrderedPlugins.Select(x => x.Filename).SequenceEqual(desired.Entries.Select(x => x.FileName), StringComparer.OrdinalIgnoreCase) ||
				!new HashSet<string>(resolved.ActivePlugins.Select(x => x.Filename), StringComparer.OrdinalIgnoreCase)
					.SetEquals(desired.Entries.Where(x => x.Active).Select(x => x.FileName)))
				throw new InvalidOperationException("The current game plugin policy cannot reproduce the exact captured plugin order/activation state.");
		}

		private static void ApplyDesiredState(IPluginManager pluginManager, CollectionLocalRestorePluginState desired)
		{
			Dictionary<string, Plugin> registered = desired.Entries.ToDictionary(x => x.FileName,
				x => pluginManager.GetRegisteredPlugin(x.FileName), StringComparer.OrdinalIgnoreCase);
			pluginManager.ApplyPluginState(desired.Entries.Select(x => registered[x.FileName]).ToList(),
				desired.Entries.Where(x => x.Active).Select(x => registered[x.FileName]).ToList());
		}

		private CollectionsRetainedArtifactReferenceRecord PersistIntent(string ownerId, PluginIntent intent)
		{
			CollectionsRetainedArtifact artifact;
			using (var stream = new MemoryStream(SerializeIntent(intent), false)) artifact = _artifactStore.Publish(stream);
			_referenceStore.AcquireExclusiveRoleReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation,
				ownerId, IntentRole);
			return _referenceStore.GetReferenceForOwnerRole(CollectionsRetainedArtifactOwnerKind.Operation, ownerId, IntentRole);
		}

		private void MarkVerified(string ownerId, string artifactId)
		{
			_referenceStore.AcquireExclusiveRoleReference(artifactId, CollectionsRetainedArtifactOwnerKind.Operation, ownerId, VerifiedRole);
		}

		private static byte[] SerializeIntent(PluginIntent intent)
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

		private PluginIntent ReadIntent(string artifactId)
		{
			if (!_artifactStore.VerifyArtifact(artifactId))
				throw new InvalidDataException("The durable plugin restore intent no longer matches its retained bytes.");
			using (Stream stream = _artifactStore.OpenRead(artifactId))
			using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, false))
			{
				JObject root = JObject.Parse(reader.ReadToEnd());
				if (!StringComparer.Ordinal.Equals((string)root["format"], IntentFormat))
					throw new InvalidDataException("Unsupported Local restore plugin intent format.");
				return new PluginIntent((string)root["captureId"], (string)root["planFingerprint"],
					ReadState(root["preimage"] as JArray), ReadState(root["desired"] as JArray));
			}
		}

		private static void ValidateIntent(PluginIntent intent, CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan plan, CollectionLocalRestorePluginState desired)
		{
			if (intent == null || !StringComparer.Ordinal.Equals(intent.CaptureId, sealedCapture.Capture.Identity.ToString()) ||
				!StringComparer.Ordinal.Equals(intent.PlanFingerprint, plan.PlanFingerprint) || !intent.Desired.Equals(desired))
				throw new InvalidDataException("The durable plugin restore intent differs from the reviewed Local Collection restore.");
		}

		private CollectionOperation CompletePluginPhase(CollectionOperation operation, LocalCaptureIdentity captureIdentity,
			CollectionLocalRestorePlan plan, int pluginCount)
		{
			string ownerId = operation.Identity.OperationId.ToString("D");
			CollectionsRetainedArtifactReferenceRecord existing = _referenceStore.GetReferenceForOwnerRole(
				CollectionsRetainedArtifactOwnerKind.Operation, ownerId, CompleteRole);
			PluginPhaseCompletion completion;
			if (existing == null)
			{
				CollectionOperation current = _operationStore.GetOperation(operation.Identity) ?? operation;
				completion = new PluginPhaseCompletion(captureIdentity.ToString(), plan.PlanFingerprint, ownerId,
					current.CheckpointSequence, pluginCount);
				CollectionsRetainedArtifact artifact;
				using (var stream = new MemoryStream(SerializePhaseCompletion(completion), false)) artifact = _artifactStore.Publish(stream);
				_referenceStore.AcquireExclusiveRoleReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation,
					ownerId, CompleteRole);
			}
			else
			{
				try { completion = ReadPhaseCompletion(existing.ArtifactId); }
				catch { MarkRecoveryRequired(operation); throw; }
				if (!StringComparer.Ordinal.Equals(completion.CaptureId, captureIdentity.ToString()) ||
					!StringComparer.Ordinal.Equals(completion.PlanFingerprint, plan.PlanFingerprint) ||
					!StringComparer.Ordinal.Equals(completion.OperationId, ownerId) || completion.PluginCount != pluginCount)
				{
					MarkRecoveryRequired(operation);
					throw new InvalidDataException("The durable plugin phase-completion marker differs from the reviewed restore.");
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
			throw new InvalidOperationException("The plugin phase-completion checkpoint no longer matches the durable operation journal.");
		}

		private static byte[] SerializePhaseCompletion(PluginPhaseCompletion completion)
		{
			using (var stream = new MemoryStream())
			using (var text = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
			using (var writer = new JsonTextWriter(text))
			{
				writer.WriteStartObject();
				writer.WritePropertyName("format"); writer.WriteValue("nmm-ce.collections.local-restore-plugin-phase/1");
				writer.WritePropertyName("captureId"); writer.WriteValue(completion.CaptureId);
				writer.WritePropertyName("planFingerprint"); writer.WriteValue(completion.PlanFingerprint);
				writer.WritePropertyName("operationId"); writer.WriteValue(completion.OperationId);
				writer.WritePropertyName("checkpointBefore"); writer.WriteValue(completion.CheckpointBefore);
				writer.WritePropertyName("pluginCount"); writer.WriteValue(completion.PluginCount);
				writer.WriteEndObject(); writer.Flush(); text.Flush();
				return stream.ToArray();
			}
		}

		private PluginPhaseCompletion ReadPhaseCompletion(string artifactId)
		{
			if (!_artifactStore.VerifyArtifact(artifactId))
				throw new InvalidDataException("The durable plugin phase-completion marker no longer matches its retained bytes.");
			using (Stream stream = _artifactStore.OpenRead(artifactId))
			using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, false))
			{
				JObject root = JObject.Parse(reader.ReadToEnd());
				if (!StringComparer.Ordinal.Equals((string)root["format"], "nmm-ce.collections.local-restore-plugin-phase/1"))
					throw new InvalidDataException("Unsupported Local restore plugin phase-completion format.");
				return new PluginPhaseCompletion((string)root["captureId"], (string)root["planFingerprint"],
					(string)root["operationId"], (long)root["checkpointBefore"], (int)root["pluginCount"]);
			}
		}

		private static void WriteState(JsonWriter writer, CollectionLocalRestorePluginState state)
		{
			writer.WriteStartArray();
			foreach (CollectionLocalRestorePluginEntry entry in state.Entries)
			{
				writer.WriteStartObject();
				writer.WritePropertyName("fileName"); writer.WriteValue(entry.FileName);
				writer.WritePropertyName("active"); writer.WriteValue(entry.Active);
				writer.WritePropertyName("priority"); writer.WriteValue(entry.Priority);
				writer.WritePropertyName("allocatedIndex"); if (entry.AllocatedIndex.HasValue) writer.WriteValue(entry.AllocatedIndex.Value); else writer.WriteNull();
				writer.WritePropertyName("modIndex"); writer.WriteValue(entry.ModIndex);
				writer.WriteEndObject();
			}
			writer.WriteEndArray();
		}

		private static CollectionLocalRestorePluginState ReadState(JArray array)
		{
			if (array == null) throw new InvalidDataException("A plugin restore intent is missing one exact plugin-state sequence.");
			return new CollectionLocalRestorePluginState(array.OfType<JObject>().Select(x =>
				new CollectionLocalRestorePluginEntry((string)x["fileName"], (bool)x["active"], (int)x["priority"],
					x["allocatedIndex"].Type == JTokenType.Null ? (int?)null : (int)x["allocatedIndex"], (string)x["modIndex"])));
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

		private sealed class PluginIntent
		{
			internal PluginIntent(string captureId, string planFingerprint, CollectionLocalRestorePluginState preimage,
				CollectionLocalRestorePluginState desired)
			{
				CaptureId = captureId ?? String.Empty; PlanFingerprint = planFingerprint ?? String.Empty;
				Preimage = preimage ?? throw new ArgumentNullException(nameof(preimage));
				Desired = desired ?? throw new ArgumentNullException(nameof(desired));
			}
			internal string CaptureId { get; }
			internal string PlanFingerprint { get; }
			internal CollectionLocalRestorePluginState Preimage { get; }
			internal CollectionLocalRestorePluginState Desired { get; }
		}

		private sealed class PluginPhaseCompletion
		{
			internal PluginPhaseCompletion(string captureId, string planFingerprint, string operationId, long checkpointBefore, int pluginCount)
			{
				CaptureId = captureId; PlanFingerprint = planFingerprint; OperationId = operationId;
				CheckpointBefore = checkpointBefore; PluginCount = pluginCount;
			}
			internal string CaptureId { get; }
			internal string PlanFingerprint { get; }
			internal string OperationId { get; }
			internal long CheckpointBefore { get; }
			internal int PluginCount { get; }
		}
	}

	/// <summary>One canonical observable plugin-state entry used by Local Restore restart evidence.</summary>
	public sealed class CollectionLocalRestorePluginEntry : IEquatable<CollectionLocalRestorePluginEntry>
	{
		public CollectionLocalRestorePluginEntry(string fileName, bool active, int priority, int? allocatedIndex, string modIndex)
		{
			if (String.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("A plugin identity is required.", nameof(fileName));
			FileName = fileName; Active = active; Priority = priority; AllocatedIndex = allocatedIndex; ModIndex = modIndex ?? String.Empty;
		}
		public string FileName { get; }
		public bool Active { get; }
		public int Priority { get; }
		public int? AllocatedIndex { get; }
		public string ModIndex { get; }
		public bool Equals(CollectionLocalRestorePluginEntry other)
		{
			return other != null && StringComparer.OrdinalIgnoreCase.Equals(FileName, other.FileName) && Active == other.Active &&
				Priority == other.Priority && AllocatedIndex == other.AllocatedIndex && StringComparer.Ordinal.Equals(ModIndex, other.ModIndex);
		}
		public override bool Equals(object obj) { return Equals(obj as CollectionLocalRestorePluginEntry); }
		public override int GetHashCode() { return StringComparer.OrdinalIgnoreCase.GetHashCode(FileName); }
	}

	/// <summary>Canonical exact order/activation observation used as plugin restore pre/postimage.</summary>
	public sealed class CollectionLocalRestorePluginState : IEquatable<CollectionLocalRestorePluginState>
	{
		private readonly ReadOnlyCollection<CollectionLocalRestorePluginEntry> _entries;
		public CollectionLocalRestorePluginState(IEnumerable<CollectionLocalRestorePluginEntry> entries)
		{
			List<CollectionLocalRestorePluginEntry> copied = (entries ?? throw new ArgumentNullException(nameof(entries))).ToList();
			if (copied.Any(x => x == null)) throw new ArgumentException("Plugin state cannot contain null entries.", nameof(entries));
			_entries = new ReadOnlyCollection<CollectionLocalRestorePluginEntry>(copied);
		}
		public ReadOnlyCollection<CollectionLocalRestorePluginEntry> Entries { get { return _entries; } }
		public bool Equals(CollectionLocalRestorePluginState other)
		{
			return other != null && _entries.Count == other._entries.Count && _entries.Zip(other._entries, (a, b) => a.Equals(b)).All(x => x);
		}
		public override bool Equals(object obj) { return Equals(obj as CollectionLocalRestorePluginState); }
		public override int GetHashCode() { return _entries.Count; }
	}
}
