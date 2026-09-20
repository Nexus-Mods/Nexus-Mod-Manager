using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement.Operations;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Immutable retained-artifact evidence used by one Collection native-child recovery manifest.</summary>
	public sealed class CollectionRecoveryArtifact
	{
		/// <summary>Creates one retained recovery-artifact descriptor.</summary>
		public CollectionRecoveryArtifact(string artifactId, CollectionContentHash contentHash, long byteLength)
		{
			ArtifactId = CollectionIdentityValidation.RequireOpaqueToken(artifactId, nameof(artifactId));
			ContentHash = contentHash ?? throw new ArgumentNullException(nameof(contentHash));
			if (byteLength < 0) throw new ArgumentOutOfRangeException(nameof(byteLength));
			ByteLength = byteLength;
		}

		/// <summary>Creates recovery evidence from one sealed Collections retained artifact.</summary>
		public static CollectionRecoveryArtifact FromRetainedArtifact(CollectionsRetainedArtifact artifact)
		{
			if (artifact == null) throw new ArgumentNullException(nameof(artifact));
			return new CollectionRecoveryArtifact(artifact.ArtifactId, artifact.ContentHash, artifact.ByteLength);
		}

		public string ArtifactId { get; }
		public CollectionContentHash ContentHash { get; }
		public long ByteLength { get; }
	}

	/// <summary>One generated scripted-replay payload retained before a native reinstall may replace the live replay set.</summary>
	public sealed class CollectionReplayRecoveryPayload
	{
		/// <summary>Creates one canonical replay-payload recovery record.</summary>
		public CollectionReplayRecoveryPayload(string relativePath, CollectionRecoveryArtifact artifact)
		{
			RelativePath = RequireCanonicalRelativePath(relativePath);
			Artifact = artifact ?? throw new ArgumentNullException(nameof(artifact));
		}

		public string RelativePath { get; }
		public CollectionRecoveryArtifact Artifact { get; }

		private static string RequireCanonicalRelativePath(string value)
		{
			if (String.IsNullOrWhiteSpace(value) || !StringComparer.Ordinal.Equals(value, value.Trim()))
				throw new ArgumentException("A canonical replay payload relative path is required.", nameof(value));
			string normalized = value.Replace('\\', '/');
			if (Path.IsPathRooted(value) || normalized.StartsWith("/", StringComparison.Ordinal) || normalized.IndexOf(':') >= 0)
				throw new ArgumentException("Replay payload paths must remain relative.", nameof(value));
			string[] segments = normalized.Split('/');
			if (segments.Any(x => x.Length == 0 || x == "." || x == ".."))
				throw new ArgumentException("Replay payload paths must not contain empty or traversal segments.", nameof(value));
			return normalized;
		}
	}

	/// <summary>Immutable snapshot of scripted replay artifacts that existed before one native reinstall was submitted.</summary>
	public sealed class CollectionScriptedReplayRecoverySnapshot
	{
		private readonly ReadOnlyCollection<CollectionReplayRecoveryPayload> _payloads;

		/// <summary>Creates one scripted-replay recovery snapshot.</summary>
		public CollectionScriptedReplayRecoverySnapshot(bool replayFileExisted, CollectionRecoveryArtifact replayFile,
			bool payloadDirectoryExisted, IEnumerable<CollectionReplayRecoveryPayload> payloads)
		{
			if (replayFileExisted != (replayFile != null))
				throw new ArgumentException("Replay-file recovery evidence must exactly match whether the replay file existed.", nameof(replayFile));
			if (payloads == null) throw new ArgumentNullException(nameof(payloads));
			List<CollectionReplayRecoveryPayload> copied = payloads.ToList();
			if (copied.Any(x => x == null)) throw new ArgumentException("Replay recovery payloads cannot contain null entries.", nameof(payloads));
			if (!payloadDirectoryExisted && copied.Count != 0)
				throw new ArgumentException("Replay payload files cannot exist when the payload directory was absent.", nameof(payloads));
			if (copied.Select(x => x.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != copied.Count)
				throw new ArgumentException("Replay recovery payload paths must be unique.", nameof(payloads));
			ReplayFileExisted = replayFileExisted;
			ReplayFile = replayFile;
			PayloadDirectoryExisted = payloadDirectoryExisted;
			_payloads = new ReadOnlyCollection<CollectionReplayRecoveryPayload>(copied
				.OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.RelativePath, StringComparer.Ordinal).ToList());
		}

		public bool ReplayFileExisted { get; }
		public CollectionRecoveryArtifact ReplayFile { get; }
		public bool PayloadDirectoryExisted { get; }
		public ReadOnlyCollection<CollectionReplayRecoveryPayload> Payloads { get { return _payloads; } }
	}

	/// <summary>Immutable C6.6 recovery-input manifest retained before one Collection native child may be submitted.</summary>
	/// <remarks>The manifest protects operation intent and immutable input/preimage bytes; it does not replace native deployment recovery.</remarks>
	public sealed class CollectionNativeChildRecoveryManifest
	{
		/// <summary>Creates one exact child recovery-input manifest.</summary>
		public CollectionNativeChildRecoveryManifest(CollectionOperationIdentity operationIdentity, int childSequence,
			CollectionPlanIdentity planIdentity, CollectionOperationMemberReference member, CollectionNativeChildAction action,
			ModOperationIdentity nativeOperation, CollectionCurrentStateFingerprint preparationStateFingerprint,
			CollectionRecoveryArtifact incomingArchive, CollectionNativeModState previousNativeMod,
			CollectionRecoveryArtifact previousArchive, CollectionScriptedReplayRecoverySnapshot scriptedReplay)
		{
			OperationIdentity = operationIdentity ?? throw new ArgumentNullException(nameof(operationIdentity));
			if (childSequence <= 0) throw new ArgumentOutOfRangeException(nameof(childSequence));
			ChildSequence = childSequence;
			PlanIdentity = planIdentity ?? throw new ArgumentNullException(nameof(planIdentity));
			Member = member ?? throw new ArgumentNullException(nameof(member));
			if (!Enum.IsDefined(typeof(CollectionNativeChildAction), action) || action == CollectionNativeChildAction.Unknown)
				throw new ArgumentOutOfRangeException(nameof(action));
			Action = action;
			NativeOperation = nativeOperation ?? throw new ArgumentNullException(nameof(nativeOperation));
			if (nativeOperation.Origin != ModOperationOrigin.Collection)
				throw new ArgumentException("C6.6 additive preparation requires Collection native-operation origin.", nameof(nativeOperation));
			PreparationStateFingerprint = preparationStateFingerprint ?? throw new ArgumentNullException(nameof(preparationStateFingerprint));
			IncomingArchive = incomingArchive ?? throw new ArgumentNullException(nameof(incomingArchive));
			if ((previousNativeMod == null) != (previousArchive == null))
				throw new ArgumentException("A previous native mod and its retained archive recovery input must be supplied together.", nameof(previousArchive));
			PreviousNativeMod = previousNativeMod;
			PreviousArchive = previousArchive;
			ScriptedReplay = scriptedReplay ?? throw new ArgumentNullException(nameof(scriptedReplay));
			if (previousNativeMod == null && (scriptedReplay.ReplayFileExisted || scriptedReplay.PayloadDirectoryExisted || scriptedReplay.Payloads.Count != 0))
				throw new ArgumentException("A newly activated member cannot carry previous-install scripted replay recovery data.", nameof(scriptedReplay));
		}

		public CollectionOperationIdentity OperationIdentity { get; }
		public int ChildSequence { get; }
		public CollectionPlanIdentity PlanIdentity { get; }
		public CollectionOperationMemberReference Member { get; }
		public CollectionNativeChildAction Action { get; }
		public ModOperationIdentity NativeOperation { get; }
		public CollectionCurrentStateFingerprint PreparationStateFingerprint { get; }
		public CollectionRecoveryArtifact IncomingArchive { get; }
		public CollectionNativeModState PreviousNativeMod { get; }
		public CollectionRecoveryArtifact PreviousArchive { get; }
		public CollectionScriptedReplayRecoverySnapshot ScriptedReplay { get; }
	}

	/// <summary>Result returned once one C6.6 native child has durable intent and recovery inputs.</summary>
	public sealed class CollectionNativeChildPreparationResult
	{
		/// <summary>Creates one durable child-preparation result.</summary>
		public CollectionNativeChildPreparationResult(CollectionOperation operation, CollectionNativeChildOperation child,
			CollectionNativeChildRecoveryManifest recoveryManifest)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Child = child ?? throw new ArgumentNullException(nameof(child));
			RecoveryManifest = recoveryManifest ?? throw new ArgumentNullException(nameof(recoveryManifest));
			if (child.Checkpoint != CollectionNativeChildCheckpoint.RecoveryInputsReady)
				throw new ArgumentException("A completed child-preparation result requires the RecoveryInputsReady checkpoint.", nameof(child));
		}
		public CollectionOperation Operation { get; }
		public CollectionNativeChildOperation Child { get; }
		public CollectionNativeChildRecoveryManifest RecoveryManifest { get; }
	}
}
