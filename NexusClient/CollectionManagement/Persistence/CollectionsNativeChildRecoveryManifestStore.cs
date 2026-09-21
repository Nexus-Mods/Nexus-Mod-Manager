using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>Persists and reloads the immutable recovery-input manifest for one prepared Collection native child.</summary>
	/// <remarks>Manifest bytes live in the existing content-addressed retained store; no Collections transaction spans native/file I/O.</remarks>
	public sealed class CollectionsNativeChildRecoveryManifestStore
	{
		private const string PayloadFormatV1 = "nmm-ce.collections.child-recovery/1";
		private const string PayloadFormatV2 = "nmm-ce.collections.child-recovery/2";
		private const long MaximumManifestBytes = 16L * 1024L * 1024L;
		private const int MaximumPayloadCount = 100000;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionsRetainedArtifactReferenceStore _referenceStore;

		/// <summary>Creates a recovery-manifest store over the existing retained-content stores.</summary>
		public CollectionsNativeChildRecoveryManifestStore(CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore)
		{
			_artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
			_referenceStore = referenceStore ?? throw new ArgumentNullException(nameof(referenceStore));
		}

		/// <summary>Persists one immutable child recovery manifest and binds its role exclusively to the Collection operation.</summary>
		public CollectionRecoveryArtifact SaveManifest(CollectionNativeChildRecoveryManifest manifest)
		{
			if (manifest == null) throw new ArgumentNullException(nameof(manifest));
			if (manifest.ScriptedReplay.Payloads.Count > MaximumPayloadCount)
				throw new InvalidOperationException("The child recovery manifest contains too many scripted replay payloads.");
			byte[] payload = Serialize(manifest);
			if (payload.LongLength <= 0 || payload.LongLength > MaximumManifestBytes)
				throw new InvalidOperationException("The child recovery manifest exceeds the supported retained payload size.");
			CollectionsRetainedArtifact retained;
			using (var stream = new MemoryStream(payload, false)) retained = _artifactStore.Publish(stream);
			_referenceStore.AcquireExclusiveRoleReference(retained.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation,
				manifest.OperationIdentity.ToString(), manifest.ExecutionEvidence == null ? GetManifestRole(manifest.ChildSequence) : GetExecutionManifestRole(manifest.ChildSequence));
			return CollectionRecoveryArtifact.FromRetainedArtifact(retained);
		}

		/// <summary>Loads and validates the unique recovery manifest for one persisted child, or <c>null</c> before it was published.</summary>
		public CollectionNativeChildRecoveryManifest GetManifest(CollectionOperation operation, CollectionNativeChildOperation child)
		{
			if (operation == null) throw new ArgumentNullException(nameof(operation));
			if (child == null) throw new ArgumentNullException(nameof(child));
			if (!operation.NativeChildren.Any(x => x.Sequence == child.Sequence &&
				x.NativeOperation.OperationId == child.NativeOperation.OperationId && x.NativeOperation.AttemptId == child.NativeOperation.AttemptId))
				throw new ArgumentException("The native child must belong to the supplied Collection operation.", nameof(child));

			CollectionsRetainedArtifactReferenceRecord reference = _referenceStore.GetReferenceForOwnerRole(
				CollectionsRetainedArtifactOwnerKind.Operation, operation.Identity.ToString(), GetExecutionManifestRole(child.Sequence));
			if (reference == null)
				reference = _referenceStore.GetReferenceForOwnerRole(CollectionsRetainedArtifactOwnerKind.Operation,
					operation.Identity.ToString(), GetManifestRole(child.Sequence));
			if (reference == null) return null;
			CollectionsRetainedArtifact artifact = _artifactStore.GetArtifact(reference.ArtifactId);
			if (artifact == null) throw new CollectionsStoreSchemaException("A child recovery-manifest reference points to missing retained content.");
			if (artifact.ByteLength <= 0 || artifact.ByteLength > MaximumManifestBytes)
				throw new CollectionsStoreSchemaException("A persisted child recovery manifest has an invalid byte length.");
			if (!_artifactStore.VerifyArtifact(artifact.ArtifactId)) throw new InvalidDataException("The retained child recovery manifest failed its integrity check.");

			try
			{
				CollectionNativeChildRecoveryManifest manifest;
				using (Stream stream = _artifactStore.OpenRead(artifact.ArtifactId))
				using (var reader = new BinaryReader(stream, Encoding.UTF8, false))
				{
					manifest = Deserialize(reader, operation, child);
					if (stream.Position != stream.Length) throw new InvalidDataException("The child recovery manifest contains trailing data.");
				}
				ValidateRetainedInputs(manifest);
				return manifest;
			}
			catch (CollectionsStoreSchemaException) { throw; }
			catch (Exception ex) when (ex is ArgumentException || ex is ArgumentOutOfRangeException || ex is EndOfStreamException || ex is FormatException || ex is IOException)
			{
				throw new CollectionsStoreSchemaException("A persisted Collection child recovery manifest is invalid.", ex);
			}
		}

		/// <summary>Returns the operation-owned retained role for one incoming child archive.</summary>
		public static string GetIncomingArchiveRole(int childSequence) { RequireSequence(childSequence); return String.Format("child-{0:D8}-incoming-archive", childSequence); }

		/// <summary>Returns the operation-owned retained role for one previous native archive.</summary>
		public static string GetPreviousArchiveRole(int childSequence) { RequireSequence(childSequence); return String.Format("child-{0:D8}-previous-archive", childSequence); }

		/// <summary>Returns the operation-owned retained role for one previous scripted replay XML file.</summary>
		public static string GetReplayXmlRole(int childSequence) { RequireSequence(childSequence); return String.Format("child-{0:D8}-replay-xml", childSequence); }

		/// <summary>Returns the operation-owned retained role for one scripted replay payload path.</summary>
		public static string GetReplayPayloadRole(int childSequence, string relativePath)
		{
			RequireSequence(childSequence);
			if (String.IsNullOrWhiteSpace(relativePath)) throw new ArgumentException("A replay payload relative path is required.", nameof(relativePath));
			using (SHA256 sha = SHA256.Create())
			{
				string hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(relativePath))).Replace("-", String.Empty).ToLowerInvariant();
				return String.Format("child-{0:D8}-replay-payload-{1}", childSequence, hash);
			}
		}

		/// <summary>Returns the exclusive retained-reference role used by one child recovery manifest.</summary>
		public static string GetManifestRole(int childSequence)
		{
			RequireSequence(childSequence);
			return String.Format("child-{0:D8}-recovery-manifest-v1", childSequence);
		}


		/// <summary>Returns the exclusive retained-reference role used by the C6.7 execution-evidence manifest.</summary>
		public static string GetExecutionManifestRole(int childSequence)
		{
			RequireSequence(childSequence);
			return String.Format("child-{0:D8}-recovery-manifest-v2", childSequence);
		}

		private void ValidateRetainedInputs(CollectionNativeChildRecoveryManifest manifest)
		{
			ValidateArtifactLease(manifest, GetIncomingArchiveRole(manifest.ChildSequence), manifest.IncomingArchive);
			if (manifest.PreviousArchive != null)
				ValidateArtifactLease(manifest, GetPreviousArchiveRole(manifest.ChildSequence), manifest.PreviousArchive);
			if (manifest.ScriptedReplay.ReplayFile != null)
				ValidateArtifactLease(manifest, GetReplayXmlRole(manifest.ChildSequence), manifest.ScriptedReplay.ReplayFile);
			foreach (CollectionReplayRecoveryPayload payload in manifest.ScriptedReplay.Payloads)
				ValidateArtifactLease(manifest, GetReplayPayloadRole(manifest.ChildSequence, payload.RelativePath), payload.Artifact);
		}

		private void ValidateArtifactLease(CollectionNativeChildRecoveryManifest manifest, string role, CollectionRecoveryArtifact descriptor)
		{
			CollectionsRetainedArtifactReferenceRecord reference = _referenceStore.GetReferenceForOwnerRole(
				CollectionsRetainedArtifactOwnerKind.Operation, manifest.OperationIdentity.ToString(), role);
			if (reference == null || !StringComparer.Ordinal.Equals(reference.ArtifactId, descriptor.ArtifactId))
				throw new CollectionsStoreSchemaException("A child recovery manifest is missing its exact operation-owned retained-content lease.");
			ValidateArtifact(descriptor);
		}

		private void ValidateArtifact(CollectionRecoveryArtifact descriptor)
		{
			CollectionsRetainedArtifact artifact = _artifactStore.GetArtifact(descriptor.ArtifactId);
			if (artifact == null || !artifact.ContentHash.Equals(descriptor.ContentHash) || artifact.ByteLength != descriptor.ByteLength)
				throw new CollectionsStoreSchemaException("A child recovery manifest references missing or mismatched retained content.");
			if (!_artifactStore.VerifyArtifact(artifact.ArtifactId))
				throw new InvalidDataException("A retained child recovery input failed its integrity check.");
		}

		private static void RequireSequence(int childSequence)
		{
			if (childSequence <= 0) throw new ArgumentOutOfRangeException(nameof(childSequence));
		}

		private static byte[] Serialize(CollectionNativeChildRecoveryManifest manifest)
		{
			using (var stream = new MemoryStream())
			using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
			{
				writer.Write(manifest.ExecutionEvidence == null ? PayloadFormatV1 : PayloadFormatV2);
				writer.Write(manifest.OperationIdentity.OperationId.ToString("D"));
				writer.Write(manifest.ChildSequence);
				writer.Write(manifest.PlanIdentity.PlanId.ToString("D"));
				writer.Write(manifest.PlanIdentity.Version);
				writer.Write((int)manifest.Member.MemberKey.Kind);
				writer.Write(manifest.Member.MemberKey.Value);
				writer.Write((int)manifest.Action);
				WriteNativeOperation(writer, manifest.NativeOperation);
				writer.Write(manifest.PreparationStateFingerprint.FormatVersion);
				writer.Write(manifest.PreparationStateFingerprint.Value);
				WriteArtifact(writer, manifest.IncomingArchive);
				writer.Write(manifest.PreviousNativeMod != null);
				if (manifest.PreviousNativeMod != null)
				{
					WriteNativeMod(writer, manifest.PreviousNativeMod);
					WriteArtifact(writer, manifest.PreviousArchive);
				}
				writer.Write(manifest.ScriptedReplay.ReplayFileExisted);
				if (manifest.ScriptedReplay.ReplayFileExisted) WriteArtifact(writer, manifest.ScriptedReplay.ReplayFile);
				writer.Write(manifest.ScriptedReplay.PayloadDirectoryExisted);
				writer.Write(manifest.ScriptedReplay.Payloads.Count);
				foreach (CollectionReplayRecoveryPayload payload in manifest.ScriptedReplay.Payloads)
				{
					writer.Write(payload.RelativePath);
					WriteArtifact(writer, payload.Artifact);
				}
				if (manifest.ExecutionEvidence != null)
					WriteExecutionEvidence(writer, manifest.ExecutionEvidence);
				writer.Flush();
				return stream.ToArray();
			}
		}

		private static CollectionNativeChildRecoveryManifest Deserialize(BinaryReader reader, CollectionOperation operation, CollectionNativeChildOperation child)
		{
			string payloadFormat = reader.ReadString();
			bool hasExecutionEvidence;
			if (StringComparer.Ordinal.Equals(payloadFormat, PayloadFormatV1)) hasExecutionEvidence = false;
			else if (StringComparer.Ordinal.Equals(payloadFormat, PayloadFormatV2)) hasExecutionEvidence = true;
			else throw new InvalidDataException("The child recovery manifest uses an unsupported format.");
			Guid operationId = ReadGuid(reader.ReadString(), "operation");
			int sequence = reader.ReadInt32();
			Guid planId = ReadGuid(reader.ReadString(), "plan");
			int planVersion = reader.ReadInt32();
			CollectionMemberKey memberKey = ReadMemberKey(reader.ReadInt32(), reader.ReadString());
			CollectionNativeChildAction action = (CollectionNativeChildAction)reader.ReadInt32();
			ModOperationIdentity nativeOperation = ReadNativeOperation(reader);
			var stateFingerprint = new CollectionCurrentStateFingerprint(reader.ReadString(), reader.ReadString());
			CollectionRecoveryArtifact incoming = ReadArtifact(reader);
			CollectionNativeModState previousNativeMod = null;
			CollectionRecoveryArtifact previousArchive = null;
			if (reader.ReadBoolean())
			{
				previousNativeMod = ReadNativeMod(reader, operation.Target);
				previousArchive = ReadArtifact(reader);
			}
			bool replayFileExisted = reader.ReadBoolean();
			CollectionRecoveryArtifact replayFile = replayFileExisted ? ReadArtifact(reader) : null;
			bool payloadDirectoryExisted = reader.ReadBoolean();
			int payloadCount = reader.ReadInt32();
			if (payloadCount < 0 || payloadCount > MaximumPayloadCount) throw new InvalidDataException("The child recovery manifest contains an invalid replay-payload count.");
			var payloads = new List<CollectionReplayRecoveryPayload>(payloadCount);
			for (int i = 0; i < payloadCount; i++) payloads.Add(new CollectionReplayRecoveryPayload(reader.ReadString(), ReadArtifact(reader)));
			CollectionNativeChildExecutionEvidence executionEvidence = hasExecutionEvidence ? ReadExecutionEvidence(reader) : null;

			if (operationId != operation.Identity.OperationId || sequence != child.Sequence || operation.PlanIdentity == null ||
				planId != operation.PlanIdentity.PlanId || planVersion != operation.PlanIdentity.Version || !memberKey.Equals(child.Member.MemberKey) ||
				action != child.Action || !Matches(nativeOperation, child.NativeOperation))
				throw new CollectionsStoreSchemaException("A persisted child recovery manifest does not match its operation journal correlation.");

			return new CollectionNativeChildRecoveryManifest(operation.Identity, sequence, operation.PlanIdentity, child.Member, action,
				nativeOperation, stateFingerprint, incoming, previousNativeMod, previousArchive,
				new CollectionScriptedReplayRecoverySnapshot(replayFileExisted, replayFile, payloadDirectoryExisted, payloads), executionEvidence);
		}


		private static void WriteExecutionEvidence(BinaryWriter writer, CollectionNativeChildExecutionEvidence evidence)
		{
			writer.Write(evidence.NexusGameDomain);
			writer.Write(evidence.NexusModId);
			writer.Write(evidence.NexusFileId);
			writer.Write(evidence.IncomingFileName);
			WriteEffectPreview(writer, evidence.ReviewedEffects);
			WriteFileEvidence(writer, evidence.PreFileContents);
			WriteFileEvidence(writer, evidence.ExpectedFileContents);
			WriteReplayContentEvidence(writer, evidence.IncomingReplayPreimage);
			writer.Write(evidence.ExpectedReplayOperations.Count);
			foreach (CollectionExpectedReplayOperation operation in evidence.ExpectedReplayOperations)
			{
				writer.Write((int)operation.Kind);
				writer.Write(operation.SourcePath ?? String.Empty);
				writer.Write(operation.DestinationPath);
				writer.Write(operation.PayloadLength);
				writer.Write(operation.PayloadSha256 ?? String.Empty);
			}
		}

		private static CollectionNativeChildExecutionEvidence ReadExecutionEvidence(BinaryReader reader)
		{
			string domain = reader.ReadString();
			long modId = reader.ReadInt64();
			long fileId = reader.ReadInt64();
			string fileName = reader.ReadString();
			CollectionMemberEffectPreview preview = ReadEffectPreview(reader);
			IReadOnlyList<CollectionNativeFileContentEvidence> preFiles = ReadFileEvidence(reader);
			IReadOnlyList<CollectionNativeFileContentEvidence> expectedFiles = ReadFileEvidence(reader);
			CollectionReplayContentEvidence replayPreimage = ReadReplayContentEvidence(reader);
			int replayCount = reader.ReadInt32();
			if (replayCount < 0 || replayCount > MaximumPayloadCount) throw new InvalidDataException("The execution evidence contains an invalid replay-operation count.");
			var replay = new List<CollectionExpectedReplayOperation>(replayCount);
			for (int i = 0; i < replayCount; i++)
			{
				ScriptedReplayOperationKind kind = (ScriptedReplayOperationKind)reader.ReadInt32();
				string source = reader.ReadString();
				string destination = reader.ReadString();
				long payloadLength = reader.ReadInt64();
				string payloadHash = reader.ReadString();
				replay.Add(new CollectionExpectedReplayOperation(kind, String.IsNullOrEmpty(source) ? null : source,
					destination, payloadLength, String.IsNullOrEmpty(payloadHash) ? null : payloadHash));
			}
			return new CollectionNativeChildExecutionEvidence(domain, modId, fileId, fileName, preview,
				preFiles, expectedFiles, replayPreimage, replay);
		}

		private static void WriteEffectPreview(BinaryWriter writer, CollectionMemberEffectPreview preview)
		{
			writer.Write((int)preview.MemberKey.Kind);
			writer.Write(preview.MemberKey.Value);
			writer.Write(preview.RecipeIdentity.Fingerprint);
			writer.Write((int)preview.InstallMethod);
			writer.Write((int)preview.InstallRoot);
			writer.Write(preview.Files.Count);
			foreach (CollectionPlannedFileEffect file in preview.Files) WriteDeploymentTarget(writer, file.Target);
			writer.Write(preview.IniEdits.Count);
			foreach (CollectionPlannedIniEffect ini in preview.IniEdits)
			{
				writer.Write(ini.Key.File); writer.Write(ini.Key.Section); writer.Write(ini.Key.Key);
				WriteNullableString(writer, ini.Value);
			}
			writer.Write(preview.GameValues.Count);
			foreach (CollectionPlannedGameValueEffect value in preview.GameValues)
			{
				writer.Write(value.Key);
				byte[] bytes = value.UnsafeValue;
				writer.Write(bytes != null);
				if (bytes != null) { writer.Write(bytes.Length); writer.Write(bytes); }
			}
			writer.Write(preview.PluginEffects.Count);
			foreach (CollectionPlannedPluginEffect plugin in preview.PluginEffects)
			{
				writer.Write((int)plugin.Kind);
				writer.Write(plugin.PluginPaths.Count);
				foreach (string path in plugin.PluginPaths) writer.Write(path);
				writer.Write(plugin.Active.HasValue); if (plugin.Active.HasValue) writer.Write(plugin.Active.Value);
				writer.Write(plugin.AbsoluteIndex.HasValue); if (plugin.AbsoluteIndex.HasValue) writer.Write(plugin.AbsoluteIndex.Value);
			}
		}

		private static CollectionMemberEffectPreview ReadEffectPreview(BinaryReader reader)
		{
			CollectionMemberKey memberKey = ReadMemberKey(reader.ReadInt32(), reader.ReadString());
			CollectionRecipeIdentity recipe = CollectionRecipeIdentity.FromFingerprint(reader.ReadString());
			ModInstallMethod method = (ModInstallMethod)reader.ReadInt32();
			ModInstallRoot root = (ModInstallRoot)reader.ReadInt32();
			new ModInstallContext(method, root);
			int fileCount = ReadBoundedCount(reader, "file-effect");
			var files = new List<CollectionPlannedFileEffect>(fileCount);
			for (int i = 0; i < fileCount; i++) files.Add(new CollectionPlannedFileEffect(ReadDeploymentTarget(reader)));
			int iniCount = ReadBoundedCount(reader, "INI-effect");
			var ini = new List<CollectionPlannedIniEffect>(iniCount);
			for (int i = 0; i < iniCount; i++)
				ini.Add(new CollectionPlannedIniEffect(new CollectionNativeIniKey(reader.ReadString(), reader.ReadString(), reader.ReadString()), ReadNullableString(reader)));
			int valueCount = ReadBoundedCount(reader, "game-value-effect");
			var values = new List<CollectionPlannedGameValueEffect>(valueCount);
			for (int i = 0; i < valueCount; i++)
			{
				string key = reader.ReadString();
				byte[] bytes = null;
				if (reader.ReadBoolean())
				{
					int length = ReadBoundedCount(reader, "game-value-byte");
					bytes = reader.ReadBytes(length);
					if (bytes.Length != length) throw new EndOfStreamException();
				}
				values.Add(new CollectionPlannedGameValueEffect(key, bytes));
			}
			int pluginCount = ReadBoundedCount(reader, "plugin-effect");
			var plugins = new List<CollectionPlannedPluginEffect>(pluginCount);
			for (int i = 0; i < pluginCount; i++)
			{
				CollectionPlannedPluginEffectKind kind = (CollectionPlannedPluginEffectKind)reader.ReadInt32();
				int pathCount = ReadBoundedCount(reader, "plugin-path");
				var paths = new List<string>(pathCount);
				for (int p = 0; p < pathCount; p++) paths.Add(reader.ReadString());
				bool? active = reader.ReadBoolean() ? (bool?)reader.ReadBoolean() : null;
				int? index = reader.ReadBoolean() ? (int?)reader.ReadInt32() : null;
				switch (kind)
				{
					case CollectionPlannedPluginEffectKind.Activation:
						if (paths.Count != 1 || !active.HasValue || index.HasValue)
							throw new InvalidDataException("The execution evidence contains malformed plugin activation state.");
						plugins.Add(CollectionPlannedPluginEffect.Activation(paths[0], active.Value));
						break;
					case CollectionPlannedPluginEffectKind.AbsoluteOrderIndex:
						if (paths.Count != 1 || active.HasValue || !index.HasValue)
							throw new InvalidDataException("The execution evidence contains malformed absolute plugin-order state.");
						plugins.Add(CollectionPlannedPluginEffect.AbsoluteOrder(paths[0], index.Value));
						break;
					case CollectionPlannedPluginEffectKind.RelativeOrder:
						if (active.HasValue || index.HasValue)
							throw new InvalidDataException("The execution evidence contains malformed relative plugin-order state.");
						plugins.Add(CollectionPlannedPluginEffect.RelativeOrder(paths));
						break;
					default:
						throw new InvalidDataException("The execution evidence contains an unsupported plugin effect.");
				}
			}
			return new CollectionMemberEffectPreview(memberKey, recipe, method, root, files, ini, values, plugins, new CollectionEffectPreviewIssue[0]);
		}

		private static void WriteFileEvidence(BinaryWriter writer, IEnumerable<CollectionNativeFileContentEvidence> evidence)
		{
			List<CollectionNativeFileContentEvidence> values = evidence.ToList();
			writer.Write(values.Count);
			foreach (CollectionNativeFileContentEvidence value in values)
			{
				WriteDeploymentTarget(writer, value.Target);
				writer.Write(value.Existed);
				if (value.Existed) { writer.Write(value.ContentHash.Value); writer.Write(value.ByteLength); }
			}
		}

		private static IReadOnlyList<CollectionNativeFileContentEvidence> ReadFileEvidence(BinaryReader reader)
		{
			int count = ReadBoundedCount(reader, "file-content-evidence");
			var result = new List<CollectionNativeFileContentEvidence>(count);
			for (int i = 0; i < count; i++)
			{
				ModDeploymentTarget target = ReadDeploymentTarget(reader);
				bool existed = reader.ReadBoolean();
				result.Add(existed
					? new CollectionNativeFileContentEvidence(target, true, CollectionContentHash.FromSha256(reader.ReadString()), reader.ReadInt64())
					: new CollectionNativeFileContentEvidence(target, false, null, 0));
			}
			return result;
		}

		private static void WriteReplayContentEvidence(BinaryWriter writer, CollectionReplayContentEvidence evidence)
		{
			writer.Write(evidence.ReplayFileExisted);
			if (evidence.ReplayFileExisted) { writer.Write(evidence.ReplayFileHash.Value); writer.Write(evidence.ReplayFileLength); }
			writer.Write(evidence.PayloadDirectoryExisted);
			writer.Write(evidence.Payloads.Count);
			foreach (CollectionReplayPayloadContentEvidence payload in evidence.Payloads)
			{
				writer.Write(payload.RelativePath); writer.Write(payload.ContentHash.Value); writer.Write(payload.ByteLength);
			}
		}

		private static CollectionReplayContentEvidence ReadReplayContentEvidence(BinaryReader reader)
		{
			bool replayExists = reader.ReadBoolean();
			CollectionContentHash replayHash = replayExists ? CollectionContentHash.FromSha256(reader.ReadString()) : null;
			long replayLength = replayExists ? reader.ReadInt64() : 0;
			bool payloadDirectoryExists = reader.ReadBoolean();
			int count = ReadBoundedCount(reader, "replay-payload-evidence");
			var payloads = new List<CollectionReplayPayloadContentEvidence>(count);
			for (int i = 0; i < count; i++)
				payloads.Add(new CollectionReplayPayloadContentEvidence(reader.ReadString(), CollectionContentHash.FromSha256(reader.ReadString()), reader.ReadInt64()));
			return new CollectionReplayContentEvidence(replayExists, replayHash, replayLength, payloadDirectoryExists, payloads);
		}

		private static void WriteDeploymentTarget(BinaryWriter writer, ModDeploymentTarget target)
		{ writer.Write((int)target.Root); writer.Write(target.RelativePath); }
		private static ModDeploymentTarget ReadDeploymentTarget(BinaryReader reader)
		{ return ModDeploymentTargetResolver.FromCanonical((ModDeploymentRoot)reader.ReadInt32(), reader.ReadString()); }
		private static void WriteNullableString(BinaryWriter writer, string value)
		{ writer.Write(value != null); if (value != null) writer.Write(value); }
		private static string ReadNullableString(BinaryReader reader)
		{ return reader.ReadBoolean() ? reader.ReadString() : null; }
		private static int ReadBoundedCount(BinaryReader reader, string subject)
		{ int count = reader.ReadInt32(); if (count < 0 || count > MaximumPayloadCount) throw new InvalidDataException("The child recovery manifest contains an invalid " + subject + " count."); return count; }

		private static void WriteNativeMod(BinaryWriter writer, CollectionNativeModState mod)
		{
			writer.Write(mod.Identity.NativeModKey);
			writer.Write(mod.ArchivePath ?? String.Empty);
			writer.Write(mod.FileName ?? String.Empty);
			writer.Write(mod.NexusModId ?? String.Empty);
			writer.Write(mod.NexusFileId ?? String.Empty);
			writer.Write(mod.HumanReadableVersion ?? String.Empty);
			writer.Write(mod.MachineVersion ?? String.Empty);
			writer.Write(mod.HasInstallScript);
			writer.Write((int)mod.InstallRoot);
			writer.Write((int)mod.InstallMethod);
		}

		private static CollectionNativeModState ReadNativeMod(BinaryReader reader, CollectionTargetIdentity target)
		{
			string key = reader.ReadString();
			string archive = reader.ReadString();
			string fileName = reader.ReadString();
			string modId = reader.ReadString();
			string fileId = reader.ReadString();
			string humanVersion = reader.ReadString();
			string machineVersion = reader.ReadString();
			bool hasInstallScript = reader.ReadBoolean();
			ModInstallRoot root = (ModInstallRoot)reader.ReadInt32();
			ModInstallMethod method = (ModInstallMethod)reader.ReadInt32();
			new ModInstallContext(method, root);
			return new CollectionNativeModState(new NativeModInstanceIdentity(target, key), archive, fileName, modId, fileId,
				humanVersion, machineVersion, hasInstallScript, root, method);
		}

		private static void WriteNativeOperation(BinaryWriter writer, ModOperationIdentity operation)
		{
			writer.Write(operation.OperationId.ToString("D")); writer.Write(operation.AttemptId.ToString("D")); writer.Write((int)operation.Origin);
			writer.Write(operation.Fingerprint.TargetFingerprint); writer.Write((int)operation.Fingerprint.InstallMethod);
			writer.Write((int)operation.Fingerprint.InstallRoot); writer.Write(operation.Fingerprint.RecipeFingerprint ?? String.Empty);
		}

		private static ModOperationIdentity ReadNativeOperation(BinaryReader reader)
		{
			Guid operationId = ReadGuid(reader.ReadString(), "native operation");
			Guid attemptId = ReadGuid(reader.ReadString(), "native attempt");
			ModOperationOrigin origin = (ModOperationOrigin)reader.ReadInt32();
			string targetFingerprint = reader.ReadString();
			ModInstallMethod method = (ModInstallMethod)reader.ReadInt32();
			ModInstallRoot root = (ModInstallRoot)reader.ReadInt32();
			string recipe = reader.ReadString();
			return new ModOperationIdentity(operationId, attemptId, origin,
				new ModOperationFingerprint(targetFingerprint, new ModInstallContext(method, root), String.IsNullOrEmpty(recipe) ? null : recipe));
		}

		private static void WriteArtifact(BinaryWriter writer, CollectionRecoveryArtifact artifact)
		{ writer.Write(artifact.ArtifactId); writer.Write(artifact.ContentHash.Value); writer.Write(artifact.ByteLength); }
		private static CollectionRecoveryArtifact ReadArtifact(BinaryReader reader)
		{ return new CollectionRecoveryArtifact(reader.ReadString(), CollectionContentHash.FromSha256(reader.ReadString()), reader.ReadInt64()); }

		private static CollectionMemberKey ReadMemberKey(int rawKind, string value)
		{
			CollectionMemberKeyKind kind = (CollectionMemberKeyKind)rawKind;
			switch (kind)
			{
				case CollectionMemberKeyKind.ProviderStable: return CollectionMemberKey.FromProvider(value);
				case CollectionMemberKeyKind.Local:
					Guid local; if (!Guid.TryParse(value, out local) || local == Guid.Empty) throw new InvalidDataException("Invalid local member identity.");
					return CollectionMemberKey.FromLocal(local);
				case CollectionMemberKeyKind.ValidatedMatch: return CollectionMemberKey.FromValidatedMatch(value);
				default: throw new InvalidDataException("Unsupported member-key kind in child recovery manifest.");
			}
		}

		private static Guid ReadGuid(string value, string subject)
		{ Guid result; if (!Guid.TryParse(value, out result) || result == Guid.Empty) throw new InvalidDataException("Invalid " + subject + " identity."); return result; }
		private static bool Matches(ModOperationIdentity left, ModOperationIdentity right)
		{ return left != null && right != null && left.OperationId == right.OperationId && left.AttemptId == right.AttemptId && left.Origin == right.Origin && left.Fingerprint.Equals(right.Fingerprint); }
	}
}
