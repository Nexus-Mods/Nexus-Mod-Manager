using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>Persists and reloads the immutable recovery-input manifest for one prepared Collection native child.</summary>
	/// <remarks>Manifest bytes live in the existing content-addressed retained store; no Collections transaction spans native/file I/O.</remarks>
	public sealed class CollectionsNativeChildRecoveryManifestStore
	{
		private const string PayloadFormat = "nmm-ce.collections.child-recovery/1";
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
				manifest.OperationIdentity.ToString(), GetManifestRole(manifest.ChildSequence));
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
				CollectionsRetainedArtifactOwnerKind.Operation, operation.Identity.ToString(), GetManifestRole(child.Sequence));
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
				writer.Write(PayloadFormat);
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
				writer.Flush();
				return stream.ToArray();
			}
		}

		private static CollectionNativeChildRecoveryManifest Deserialize(BinaryReader reader, CollectionOperation operation, CollectionNativeChildOperation child)
		{
			if (!StringComparer.Ordinal.Equals(reader.ReadString(), PayloadFormat)) throw new InvalidDataException("The child recovery manifest uses an unsupported format.");
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

			if (operationId != operation.Identity.OperationId || sequence != child.Sequence || operation.PlanIdentity == null ||
				planId != operation.PlanIdentity.PlanId || planVersion != operation.PlanIdentity.Version || !memberKey.Equals(child.Member.MemberKey) ||
				action != child.Action || !Matches(nativeOperation, child.NativeOperation))
				throw new CollectionsStoreSchemaException("A persisted child recovery manifest does not match its operation journal correlation.");

			return new CollectionNativeChildRecoveryManifest(operation.Identity, sequence, operation.PlanIdentity, child.Member, action,
				nativeOperation, stateFingerprint, incoming, previousNativeMod, previousArchive,
				new CollectionScriptedReplayRecoverySnapshot(replayFileExisted, replayFile, payloadDirectoryExisted, payloads));
		}

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
