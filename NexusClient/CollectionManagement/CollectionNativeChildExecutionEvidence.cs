using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Scripting;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Exact byte-state evidence for one root-aware deployment target at a Collection native-child boundary.</summary>
	public sealed class CollectionNativeFileContentEvidence
	{
		/// <summary>Creates one deployment-target content observation.</summary>
		public CollectionNativeFileContentEvidence(ModDeploymentTarget target, bool existed,
			CollectionContentHash contentHash, long byteLength)
		{
			Target = target ?? throw new ArgumentNullException(nameof(target));
			if (existed != (contentHash != null))
				throw new ArgumentException("File content evidence must carry a hash exactly when the target existed.", nameof(contentHash));
			if (contentHash != null && contentHash.Algorithm != CollectionContentHashAlgorithm.Sha256)
				throw new ArgumentException("File content evidence requires SHA-256.", nameof(contentHash));
			if (existed && byteLength < 0)
				throw new ArgumentOutOfRangeException(nameof(byteLength));
			if (!existed && byteLength != 0)
				throw new ArgumentException("Absent file content evidence must use zero byte length.", nameof(byteLength));
			Existed = existed;
			ContentHash = contentHash;
			ByteLength = byteLength;
		}

		public ModDeploymentTarget Target { get; }
		public bool Existed { get; }
		public CollectionContentHash ContentHash { get; }
		public long ByteLength { get; }
	}

	/// <summary>Exact byte-state evidence for one scripted replay sidecar payload.</summary>
	public sealed class CollectionReplayPayloadContentEvidence
	{
		/// <summary>Creates one replay payload observation.</summary>
		public CollectionReplayPayloadContentEvidence(string relativePath, CollectionContentHash contentHash, long byteLength)
		{
			if (String.IsNullOrWhiteSpace(relativePath))
				throw new ArgumentException("A replay payload relative path is required.", nameof(relativePath));
			if (!StringComparer.Ordinal.Equals(relativePath, relativePath.Trim()))
				throw new ArgumentException("Replay payload paths must not contain leading or trailing whitespace.", nameof(relativePath));
			string normalized = relativePath.Replace('\\', '/');
			if (Path.IsPathRooted(relativePath) || normalized.StartsWith("/", StringComparison.Ordinal) || normalized.IndexOf(':') >= 0 ||
				normalized.Split('/').Any(x => x.Length == 0 || x == "." || x == ".."))
				throw new ArgumentException("Replay payload paths must be canonical relative paths without traversal.", nameof(relativePath));
			RelativePath = normalized;
			ContentHash = contentHash ?? throw new ArgumentNullException(nameof(contentHash));
			if (contentHash.Algorithm != CollectionContentHashAlgorithm.Sha256)
				throw new ArgumentException("Replay payload content evidence requires SHA-256.", nameof(contentHash));
			if (byteLength < 0) throw new ArgumentOutOfRangeException(nameof(byteLength));
			ByteLength = byteLength;
		}

		public string RelativePath { get; }
		public CollectionContentHash ContentHash { get; }
		public long ByteLength { get; }
	}

	/// <summary>Exact pre-operation content observation for one native scripted replay path and its payload directory.</summary>
	public sealed class CollectionReplayContentEvidence
	{
		private readonly ReadOnlyCollection<CollectionReplayPayloadContentEvidence> _payloads;

		/// <summary>Creates one replay content observation.</summary>
		public CollectionReplayContentEvidence(bool replayFileExisted, CollectionContentHash replayFileHash,
			long replayFileLength, bool payloadDirectoryExisted, IEnumerable<CollectionReplayPayloadContentEvidence> payloads)
		{
			if (replayFileExisted != (replayFileHash != null))
				throw new ArgumentException("Replay content evidence must carry a hash exactly when the replay file existed.", nameof(replayFileHash));
			if (replayFileHash != null && replayFileHash.Algorithm != CollectionContentHashAlgorithm.Sha256)
				throw new ArgumentException("Replay content evidence requires SHA-256.", nameof(replayFileHash));
			if (replayFileExisted && replayFileLength < 0) throw new ArgumentOutOfRangeException(nameof(replayFileLength));
			if (!replayFileExisted && replayFileLength != 0)
				throw new ArgumentException("Absent replay content evidence must use zero byte length.", nameof(replayFileLength));
			if (payloads == null) throw new ArgumentNullException(nameof(payloads));
			List<CollectionReplayPayloadContentEvidence> copied = payloads.ToList();
			if (copied.Any(x => x == null)) throw new ArgumentException("Replay content evidence cannot contain null payloads.", nameof(payloads));
			if (!payloadDirectoryExisted && copied.Count != 0)
				throw new ArgumentException("Replay payload evidence requires the payload directory to exist.", nameof(payloads));
			if (copied.Select(x => x.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != copied.Count)
				throw new ArgumentException("Replay payload evidence paths must be unique.", nameof(payloads));
			ReplayFileExisted = replayFileExisted;
			ReplayFileHash = replayFileHash;
			ReplayFileLength = replayFileLength;
			PayloadDirectoryExisted = payloadDirectoryExisted;
			_payloads = new ReadOnlyCollection<CollectionReplayPayloadContentEvidence>(copied
				.OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.RelativePath, StringComparer.Ordinal).ToList());
		}

		public bool ReplayFileExisted { get; }
		public CollectionContentHash ReplayFileHash { get; }
		public long ReplayFileLength { get; }
		public bool PayloadDirectoryExisted { get; }
		public ReadOnlyCollection<CollectionReplayPayloadContentEvidence> Payloads { get { return _payloads; } }
	}

	/// <summary>One ordered scripted-replay record expected after an exact C5 recipe commits.</summary>
	public sealed class CollectionExpectedReplayOperation
	{
		/// <summary>Creates one expected live replay record.</summary>
		public CollectionExpectedReplayOperation(ScriptedReplayOperationKind kind, string sourcePath,
			string destinationPath, long payloadLength, string payloadSha256)
		{
			if (!Enum.IsDefined(typeof(ScriptedReplayOperationKind), kind) || kind == ScriptedReplayOperationKind.BasicInstall)
				throw new ArgumentOutOfRangeException(nameof(kind));
			if (String.IsNullOrWhiteSpace(destinationPath))
				throw new ArgumentException("An expected replay destination path is required.", nameof(destinationPath));
			if (kind == ScriptedReplayOperationKind.ArchiveFile)
			{
				if (String.IsNullOrWhiteSpace(sourcePath))
					throw new ArgumentException("Archive replay evidence requires a source path.", nameof(sourcePath));
				if (payloadLength != 0 || payloadSha256 != null)
					throw new ArgumentException("Archive replay evidence cannot carry generated payload metadata.");
			}
			else
			{
				if (sourcePath != null)
					throw new ArgumentException("Generated replay evidence cannot carry an archive source path.", nameof(sourcePath));
				if (payloadLength < 0) throw new ArgumentOutOfRangeException(nameof(payloadLength));
				CollectionContentHash.FromSha256(payloadSha256);
			}
			Kind = kind;
			SourcePath = sourcePath;
			DestinationPath = destinationPath;
			PayloadLength = payloadLength;
			PayloadSha256 = payloadSha256 == null ? null : payloadSha256.ToLowerInvariant();
		}

		public ScriptedReplayOperationKind Kind { get; }
		public string SourcePath { get; }
		public string DestinationPath { get; }
		public long PayloadLength { get; }
		public string PayloadSha256 { get; }
	}

	/// <summary>
	/// Durable C6.7 execution evidence sufficient to re-evaluate the exact intended native result after process restart.
	/// </summary>
	/// <remarks>This is verification evidence only. It neither owns deployed files nor authorizes replaying a native installer.</remarks>
	public sealed class CollectionNativeChildExecutionEvidence
	{
		private readonly ReadOnlyCollection<CollectionNativeFileContentEvidence> _preFileContents;
		private readonly ReadOnlyCollection<CollectionNativeFileContentEvidence> _expectedFileContents;
		private readonly ReadOnlyCollection<CollectionExpectedReplayOperation> _expectedReplayOperations;

		/// <summary>Creates one immutable native-child restart-verification evidence snapshot.</summary>
		public CollectionNativeChildExecutionEvidence(string nexusGameDomain, long nexusModId, long nexusFileId,
			string incomingFileName, CollectionMemberEffectPreview reviewedEffects,
			IEnumerable<CollectionNativeFileContentEvidence> preFileContents,
			IEnumerable<CollectionNativeFileContentEvidence> expectedFileContents,
			CollectionReplayContentEvidence incomingReplayPreimage,
			IEnumerable<CollectionExpectedReplayOperation> expectedReplayOperations)
		{
			if (String.IsNullOrWhiteSpace(nexusGameDomain) || !StringComparer.Ordinal.Equals(nexusGameDomain, nexusGameDomain.Trim()))
				throw new ArgumentException("A canonical Nexus game domain is required.", nameof(nexusGameDomain));
			if (nexusModId <= 0) throw new ArgumentOutOfRangeException(nameof(nexusModId));
			if (nexusFileId <= 0) throw new ArgumentOutOfRangeException(nameof(nexusFileId));
			if (String.IsNullOrWhiteSpace(incomingFileName) || !StringComparer.Ordinal.Equals(incomingFileName, incomingFileName.Trim()))
				throw new ArgumentException("The incoming native mod file name must be a non-empty exact value without surrounding whitespace.", nameof(incomingFileName));
			ReviewedEffects = reviewedEffects ?? throw new ArgumentNullException(nameof(reviewedEffects));
			if (!reviewedEffects.IsComplete) throw new ArgumentException("Restart evidence requires a complete reviewed native-effect preview.", nameof(reviewedEffects));
			_preFileContents = CopyUnique(preFileContents, nameof(preFileContents));
			_expectedFileContents = CopyUnique(expectedFileContents, nameof(expectedFileContents));
			incomingReplayPreimage = incomingReplayPreimage ?? throw new ArgumentNullException(nameof(incomingReplayPreimage));
			if (_preFileContents.Count != reviewedEffects.Files.Count || _expectedFileContents.Count != reviewedEffects.Files.Count)
				throw new ArgumentException("Restart file evidence must cover every reviewed file target exactly once.");
			var reviewedTargets = new HashSet<ModDeploymentTarget>(reviewedEffects.Files.Select(x => x.Target));
			if (_preFileContents.Any(x => !reviewedTargets.Contains(x.Target)) ||
				_expectedFileContents.Any(x => !reviewedTargets.Contains(x.Target) || !x.Existed))
				throw new ArgumentException("Restart file evidence does not match the reviewed native file targets.");
			if (expectedReplayOperations == null) throw new ArgumentNullException(nameof(expectedReplayOperations));
			List<CollectionExpectedReplayOperation> replay = expectedReplayOperations.ToList();
			if (replay.Any(x => x == null)) throw new ArgumentException("Expected replay operations cannot contain null entries.", nameof(expectedReplayOperations));
			NexusGameDomain = nexusGameDomain;
			NexusModId = nexusModId;
			NexusFileId = nexusFileId;
			IncomingFileName = incomingFileName;
			IncomingReplayPreimage = incomingReplayPreimage;
			_expectedReplayOperations = new ReadOnlyCollection<CollectionExpectedReplayOperation>(replay);
		}

		public string NexusGameDomain { get; }
		public long NexusModId { get; }
		public long NexusFileId { get; }
		public string IncomingFileName { get; }
		public CollectionMemberEffectPreview ReviewedEffects { get; }
		public ReadOnlyCollection<CollectionNativeFileContentEvidence> PreFileContents { get { return _preFileContents; } }
		public ReadOnlyCollection<CollectionNativeFileContentEvidence> ExpectedFileContents { get { return _expectedFileContents; } }
		public CollectionReplayContentEvidence IncomingReplayPreimage { get; }
		public ReadOnlyCollection<CollectionExpectedReplayOperation> ExpectedReplayOperations { get { return _expectedReplayOperations; } }

		private static ReadOnlyCollection<CollectionNativeFileContentEvidence> CopyUnique(
			IEnumerable<CollectionNativeFileContentEvidence> values, string parameterName)
		{
			if (values == null) throw new ArgumentNullException(parameterName);
			List<CollectionNativeFileContentEvidence> copied = values.ToList();
			if (copied.Any(x => x == null)) throw new ArgumentException("File content evidence cannot contain null entries.", parameterName);
			if (copied.Select(x => x.Target).Distinct().Count() != copied.Count)
				throw new ArgumentException("File content evidence targets must be unique.", parameterName);
			return new ReadOnlyCollection<CollectionNativeFileContentEvidence>(copied
				.OrderBy(x => x.Target.Root).ThenBy(x => x.Target.RelativePath, StringComparer.OrdinalIgnoreCase).ToList());
		}
	}
}
