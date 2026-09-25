using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Scripting;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Retains scripted-installer replay XML and generated-file sidecars for the exact native members observed by a Local Collection capture.
	/// </summary>
	/// <remarks>
	/// The service never relies on retained live InstallInfo paths after capture and never reruns scripts. Immutable bytes are published
	/// through the shared Collections retained-artifact store and protected by capture-scoped lifetime references. The later C7 sealing
	/// boundary remains responsible for deciding whether missing or legacy replay state permits a locally-restorable claim.
	/// </remarks>
	public sealed class CollectionScriptedReplayCaptureService
	{
		private const int CopyBufferSize = 81920;
		private readonly NativeStateCaptureReader _nativeStateReader;
		private readonly CollectionInstalledIdentityCaptureReader _installedIdentityReader;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionsRetainedArtifactReferenceStore _referenceStore;

		/// <summary>Creates a C7.4 scripted-replay retention service over the C7.1/C7.2 capture boundaries.</summary>
		public CollectionScriptedReplayCaptureService(NativeStateCaptureReader nativeStateReader,
			CollectionInstalledIdentityCaptureReader installedIdentityReader, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore)
		{
			_nativeStateReader = nativeStateReader ?? throw new ArgumentNullException(nameof(nativeStateReader));
			_installedIdentityReader = installedIdentityReader ?? throw new ArgumentNullException(nameof(installedIdentityReader));
			_artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
			_referenceStore = referenceStore ?? throw new ArgumentNullException(nameof(referenceStore));
		}

		/// <summary>Captures scripted replay artifacts for one target using a single shared native-state observation.</summary>
		public CollectionScriptedReplaySnapshot Capture(CollectionTargetIdentity target, LocalCaptureIdentity captureIdentity)
		{
			return Capture(target, captureIdentity, CancellationToken.None);
		}

		/// <summary>Captures scripted replay artifacts with cooperative cancellation during retained-content copies.</summary>
		public CollectionScriptedReplaySnapshot Capture(CollectionTargetIdentity target, LocalCaptureIdentity captureIdentity,
			CancellationToken cancellationToken)
		{
			if (target == null)
				throw new ArgumentNullException(nameof(target));
			if (captureIdentity == null)
				throw new ArgumentNullException(nameof(captureIdentity));

			NativeStateCaptureSnapshot nativeState = _nativeStateReader.Capture();
			CollectionInstalledIdentitySnapshot installedIdentities = _installedIdentityReader.Capture(target, nativeState);
			return Capture(target, captureIdentity, nativeState, installedIdentities, cancellationToken);
		}

		internal CollectionScriptedReplaySnapshot Capture(CollectionTargetIdentity target, LocalCaptureIdentity captureIdentity,
			NativeStateCaptureSnapshot nativeState, CollectionInstalledIdentitySnapshot installedIdentities,
			CancellationToken cancellationToken)
		{
			if (target == null)
				throw new ArgumentNullException(nameof(target));
			if (captureIdentity == null)
				throw new ArgumentNullException(nameof(captureIdentity));
			if (nativeState == null)
				throw new ArgumentNullException(nameof(nativeState));
			if (installedIdentities == null)
				throw new ArgumentNullException(nameof(installedIdentities));
			if (!target.Equals(installedIdentities.Target))
				throw new InvalidOperationException("The installed-identity snapshot belongs to a different Collection target.");
			if (installedIdentities.DeploymentCommitSequence != nativeState.InstallLog.DeploymentCommitSequence)
				throw new InvalidOperationException("Scripted replay capture requires installed identities from the same native-state observation.");

			List<CollectionInstalledModIdentity> scriptedMods = installedIdentities.Mods.Where(x => x.HasInstallScript)
				.OrderBy(x => x.NativeSnapshotKey, StringComparer.OrdinalIgnoreCase).ToList();
			if (scriptedMods.Count == 0)
			{
				return new CollectionScriptedReplaySnapshot(target, captureIdentity,
					nativeState.InstallLog.DeploymentCommitSequence, new CollectionScriptedReplayArtifactSet[0],
					NativeStateCaptureCoverage.NotApplicable, new CollectionScriptedReplayIssue[0]);
			}

			var issues = new List<CollectionScriptedReplayIssue>();
			var artifactSets = new List<CollectionScriptedReplayArtifactSet>();
			NativeStateCaptureCoverage coverage = NativeStateCaptureCoverage.Complete;
			Dictionary<string, List<NativeStateCaptureReplayReference>> referencesByMod = nativeState.ReplayReferences
				.GroupBy(x => x.ModKey, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);

			var collidingPaths = new HashSet<string>(nativeState.ReplayReferences
				.Where(x => !String.IsNullOrWhiteSpace(x.CachePath))
				.GroupBy(x => x.CachePath, StringComparer.OrdinalIgnoreCase)
				.Where(x => x.Select(y => y.ModKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
				.Select(x => x.Key), StringComparer.OrdinalIgnoreCase);

			foreach (CollectionInstalledModIdentity mod in scriptedMods)
			{
				List<NativeStateCaptureReplayReference> references;
				if (!referencesByMod.TryGetValue(mod.NativeSnapshotKey, out references) || references.Count == 0)
				{
					MarkPartial(issues, ref coverage, CollectionScriptedReplayIssueKind.ReplayReferenceUnavailable,
						mod.NativeSnapshotKey, "The scripted native member has no authoritative live replay reference in the generic native capture.");
					continue;
				}
				if (references.Count != 1)
				{
					MarkPartial(issues, ref coverage, CollectionScriptedReplayIssueKind.ReplayReferenceAmbiguous,
						mod.NativeSnapshotKey, "The scripted native member maps to more than one replay reference and cannot be retained unambiguously.");
					continue;
				}

				NativeStateCaptureReplayReference replayReference = references[0];
				if (String.IsNullOrWhiteSpace(replayReference.CachePath))
				{
					MarkPartial(issues, ref coverage, CollectionScriptedReplayIssueKind.ReplayReferenceUnavailable,
						mod.NativeSnapshotKey, "The scripted replay reference does not contain a live cache path.");
					continue;
				}
				if (collidingPaths.Contains(replayReference.CachePath))
				{
					MarkPartial(issues, ref coverage, CollectionScriptedReplayIssueKind.ReplayPathCollision,
						mod.NativeSnapshotKey, "Distinct scripted native members resolve to the same legacy basename-derived replay path.");
					continue;
				}

				CollectionScriptedReplayArtifactSet artifactSet = CaptureArtifactSet(captureIdentity, mod, replayReference,
					issues, ref coverage, cancellationToken);
				if (artifactSet != null)
					artifactSets.Add(artifactSet);
			}

			return new CollectionScriptedReplaySnapshot(target, captureIdentity,
				nativeState.InstallLog.DeploymentCommitSequence, artifactSets, coverage, issues);
		}

		private CollectionScriptedReplayArtifactSet CaptureArtifactSet(LocalCaptureIdentity captureIdentity,
			CollectionInstalledModIdentity mod, NativeStateCaptureReplayReference replayReference,
			List<CollectionScriptedReplayIssue> issues, ref NativeStateCaptureCoverage coverage,
			CancellationToken cancellationToken)
		{
			FileStream replaySource;
			try
			{
				replaySource = new FileStream(Path.GetFullPath(replayReference.CachePath), FileMode.Open, FileAccess.Read,
					FileShare.Read, CopyBufferSize, FileOptions.SequentialScan);
			}
			catch (FileNotFoundException)
			{
				MarkPartial(issues, ref coverage, CollectionScriptedReplayIssueKind.ReplayFileMissing,
					mod.NativeSnapshotKey, "The live scripted replay XML no longer exists.");
				return null;
			}
			catch (DirectoryNotFoundException)
			{
				MarkPartial(issues, ref coverage, CollectionScriptedReplayIssueKind.ReplayFileMissing,
					mod.NativeSnapshotKey, "The live scripted replay directory no longer exists.");
				return null;
			}

			using (replaySource)
			{
				var cache = new ScriptedFileSelectionCache(replayReference.CachePath);
				bool completeReplayFormat = cache.HasCompleteReplay;
				replaySource.Position = 0;
				RetainedArtifactReference replayXml = RetainStream(captureIdentity,
					CreateReplayRole(mod.NativeSnapshotKey), replaySource, cancellationToken);

				IReadOnlyList<ScriptedReplayOperation> operations;
				try
				{
					operations = cache.LoadReplayOperations() ?? new ScriptedReplayOperation[0];
				}
				catch (FileNotFoundException exception)
				{
					MarkPartial(issues, ref coverage, CollectionScriptedReplayIssueKind.GeneratedPayloadMissing,
						mod.NativeSnapshotKey, exception.Message);
					return new CollectionScriptedReplayArtifactSet(mod.NativeSnapshotKey, replayXml,
						completeReplayFormat, false, 0, new CollectionScriptedGeneratedPayload[0], mod.Provenance);
				}
				catch (DirectoryNotFoundException exception)
				{
					MarkPartial(issues, ref coverage, CollectionScriptedReplayIssueKind.GeneratedPayloadMissing,
						mod.NativeSnapshotKey, exception.Message);
					return new CollectionScriptedReplayArtifactSet(mod.NativeSnapshotKey, replayXml,
						completeReplayFormat, false, 0, new CollectionScriptedGeneratedPayload[0], mod.Provenance);
				}
				catch (Exception exception) when (exception is InvalidDataException || exception is XmlException)
				{
					MarkPartial(issues, ref coverage, CollectionScriptedReplayIssueKind.ReplayPlanInvalid,
						mod.NativeSnapshotKey, exception.Message);
					return new CollectionScriptedReplayArtifactSet(mod.NativeSnapshotKey, replayXml,
						completeReplayFormat, false, 0, new CollectionScriptedGeneratedPayload[0], mod.Provenance);
				}

				if (!completeReplayFormat)
				{
					MarkPartial(issues, ref coverage, CollectionScriptedReplayIssueKind.ReplayFormatIncomplete,
						mod.NativeSnapshotKey, "The scripted replay XML predates the complete replay format and cannot prove that every replayable file operation was recorded.");
				}

				var generatedPayloads = new List<CollectionScriptedGeneratedPayload>();
				for (int operationIndex = 0; operationIndex < operations.Count; operationIndex++)
				{
					ScriptedReplayOperation operation = operations[operationIndex];
					if (operation.Kind != ScriptedReplayOperationKind.GeneratedFile)
						continue;
					CollectionScriptedGeneratedPayload retained = RetainGeneratedPayload(captureIdentity,
						mod.NativeSnapshotKey, operationIndex, operation, issues, ref coverage, cancellationToken);
					if (retained != null)
						generatedPayloads.Add(retained);
				}

				return new CollectionScriptedReplayArtifactSet(mod.NativeSnapshotKey, replayXml,
					completeReplayFormat, true, operations.Count, generatedPayloads, mod.Provenance);
			}
		}

		private CollectionScriptedGeneratedPayload RetainGeneratedPayload(LocalCaptureIdentity captureIdentity,
			string nativeSnapshotKey, int operationIndex, ScriptedReplayOperation operation,
			List<CollectionScriptedReplayIssue> issues, ref NativeStateCaptureCoverage coverage,
			CancellationToken cancellationToken)
		{
			CollectionsRetainedArtifact artifact;
			try
			{
				artifact = _artifactStore.PublishFile(operation.PayloadPath, cancellationToken);
			}
			catch (FileNotFoundException)
			{
				MarkPartial(issues, ref coverage, CollectionScriptedReplayIssueKind.GeneratedPayloadMissing,
					nativeSnapshotKey, "A generated scripted replay payload disappeared while it was being retained.");
				return null;
			}
			catch (DirectoryNotFoundException)
			{
				MarkPartial(issues, ref coverage, CollectionScriptedReplayIssueKind.GeneratedPayloadMissing,
					nativeSnapshotKey, "A generated scripted replay payload directory disappeared while it was being retained.");
				return null;
			}

			if (artifact.ByteLength != operation.PayloadLength ||
				!StringComparer.OrdinalIgnoreCase.Equals(artifact.ContentHash.Value, operation.PayloadHash))
			{
				MarkPartial(issues, ref coverage, CollectionScriptedReplayIssueKind.GeneratedPayloadChangedDuringCapture,
					nativeSnapshotKey, "A generated scripted replay payload changed after replay validation and was not bound to the capture.");
				return null;
			}

			string role = CreatePayloadRole(nativeSnapshotKey, operationIndex);
			_referenceStore.AcquireExclusiveRoleReference(artifact.ArtifactId,
				CollectionsRetainedArtifactOwnerKind.Capture, captureIdentity.ToString(), role);
			var retainedArtifact = new RetainedArtifactReference(artifact.ArtifactId, role,
				artifact.ContentHash, artifact.ByteLength);
			return new CollectionScriptedGeneratedPayload(operationIndex, Path.GetFileName(operation.PayloadPath),
				operation.DestinationPath, retainedArtifact);
		}

		private RetainedArtifactReference RetainStream(LocalCaptureIdentity captureIdentity, string role, Stream source,
			CancellationToken cancellationToken)
		{
			CollectionsRetainedArtifact artifact = _artifactStore.Publish(source, cancellationToken);
			_referenceStore.AcquireExclusiveRoleReference(artifact.ArtifactId,
				CollectionsRetainedArtifactOwnerKind.Capture, captureIdentity.ToString(), role);
			return new RetainedArtifactReference(artifact.ArtifactId, role, artifact.ContentHash, artifact.ByteLength);
		}

		private static void MarkPartial(List<CollectionScriptedReplayIssue> issues, ref NativeStateCaptureCoverage coverage,
			CollectionScriptedReplayIssueKind kind, string resourceKey, string message)
		{
			coverage = NativeStateCaptureCoverage.Partial;
			issues.Add(new CollectionScriptedReplayIssue(kind, resourceKey, message));
		}

		private static string CreateReplayRole(string nativeSnapshotKey)
		{
			return "scripted-replay:" + CreateNativeRoleToken(nativeSnapshotKey);
		}

		private static string CreatePayloadRole(string nativeSnapshotKey, int operationIndex)
		{
			return String.Format(CultureInfo.InvariantCulture, "scripted-payload:{0}:{1}",
				CreateNativeRoleToken(nativeSnapshotKey), operationIndex);
		}

		private static string CreateNativeRoleToken(string nativeSnapshotKey)
		{
			if (String.IsNullOrWhiteSpace(nativeSnapshotKey))
				throw new ArgumentException("A native snapshot key is required.", nameof(nativeSnapshotKey));
			using (SHA256 sha256 = SHA256.Create())
			{
				byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(nativeSnapshotKey.ToUpperInvariant()));
				var builder = new StringBuilder(hash.Length * 2);
				foreach (byte value in hash)
					builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
				return builder.ToString();
			}
		}
	}
}
