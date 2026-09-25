using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Summarizes the durable identity/header of one versioned C7 Local Collection capture package.</summary>
	public sealed class CollectionLocalCapturePackageInspection
	{
		internal CollectionLocalCapturePackageInspection(int formatVersion, LocalCaptureIdentity captureIdentity,
			LocalCaptureCapability capability, int captureSchemaVersion, int capabilityVersion,
			CollectionTargetIdentity sourceTarget, long deploymentCommitSequence)
		{
			FormatVersion = formatVersion;
			CaptureIdentity = captureIdentity;
			Capability = capability;
			CaptureSchemaVersion = captureSchemaVersion;
			CapabilityVersion = capabilityVersion;
			SourceTarget = sourceTarget;
			DeploymentCommitSequence = deploymentCommitSequence;
		}

		public int FormatVersion { get; }
		public LocalCaptureIdentity CaptureIdentity { get; }
		public LocalCaptureCapability Capability { get; }
		public int CaptureSchemaVersion { get; }
		public int CapabilityVersion { get; }
		public CollectionTargetIdentity SourceTarget { get; }
		public long DeploymentCommitSequence { get; }
	}

	/// <summary>
	/// Encodes the complete sealed C7.2-C7.7 capture state into one versioned immutable package artifact.
	/// </summary>
	/// <remarks>
	/// The package is Collections-owned durable reconstruction input. Live archive/replay paths may remain as provenance,
	/// but a locally-restorable claim is backed by the retained artifact identities recorded by the sealed capture.
	/// </remarks>
	public static class CollectionLocalCapturePackageCodec
	{
		public const int CurrentFormatVersion = 1;
		private const string FormatIdentity = "nmm-ce-local-collection-capture";

		/// <summary>Serializes one successful seal result as compact UTF-8 JSON without a BOM.</summary>
		public static byte[] Serialize(CollectionCaptureSealResult result)
		{
			if (result == null)
				throw new ArgumentNullException(nameof(result));
			if (!result.IsSealed || result.SealedCapture == null)
				throw new ArgumentException("Only a successfully sealed capture can be persisted as a Local Collection package.", nameof(result));

			CollectionSealedCaptureSnapshot snapshot = result.SealedCapture;
			LocalCapture capture = snapshot.Capture;
			var serializer = JsonSerializer.CreateDefault();
			var root = new JObject
			{
				["format"] = FormatIdentity,
				["formatVersion"] = CurrentFormatVersion,
				["captureIdentity"] = capture.Identity.ToString(),
				["capability"] = (int)capture.Capability,
				["captureSchemaVersion"] = capture.SchemaVersion,
				["capabilityVersion"] = capture.CapabilityVersion,
				["sourceTarget"] = capture.SourceTarget.Fingerprint,
				["deploymentCommitSequence"] = snapshot.InstalledIdentities.DeploymentCommitSequence,
				["capture"] = JToken.FromObject(capture, serializer),
				["installedIdentities"] = JToken.FromObject(snapshot.InstalledIdentities, serializer),
				["archives"] = JToken.FromObject(snapshot.Archives, serializer),
				["ownerPayloads"] = JToken.FromObject(snapshot.OwnerPayloads, serializer),
				["scriptedReplay"] = JToken.FromObject(snapshot.ScriptedReplay, serializer),
				["nativeEffects"] = JToken.FromObject(snapshot.NativeEffects, serializer),
				["userMetadata"] = JToken.FromObject(snapshot.UserMetadata, serializer),
				["sealingIssues"] = JToken.FromObject(result.Issues, serializer)
			};

			return new UTF8Encoding(false, true).GetBytes(root.ToString(Formatting.None));
		}

		/// <summary>Validates the versioned package envelope and returns its durable identity/header.</summary>
		public static CollectionLocalCapturePackageInspection Inspect(byte[] packageBytes)
		{
			return InspectRoot(ParseRoot(packageBytes));
		}

		/// <summary>Rehydrates the complete immutable sealed capture used by C7.9 restore planning.</summary>
		public static CollectionSealedCaptureSnapshot Deserialize(byte[] packageBytes)
		{
			JObject root = ParseRoot(packageBytes);
			CollectionLocalCapturePackageInspection inspection = InspectRoot(root);
			JsonSerializer serializer = CreateRestoreSerializer();
			try
			{
				LocalCapture capture = DeserializeCapture(RequireObject(root, "capture"), serializer);
				CollectionInstalledIdentitySnapshot installed = RequireObject(root, "installedIdentities")
					.ToObject<CollectionInstalledIdentitySnapshot>(serializer);
				List<CollectionCapturedArchiveArtifact> archives = RequireArray(root, "archives")
					.ToObject<List<CollectionCapturedArchiveArtifact>>(serializer);
				CollectionOwnerPayloadSnapshot ownerPayloads = RequireObject(root, "ownerPayloads")
					.ToObject<CollectionOwnerPayloadSnapshot>(serializer);
				CollectionScriptedReplaySnapshot replay = RequireObject(root, "scriptedReplay")
					.ToObject<CollectionScriptedReplaySnapshot>(serializer);
				CollectionNativeEffectSnapshot effects = RequireObject(root, "nativeEffects")
					.ToObject<CollectionNativeEffectSnapshot>(serializer);
				CollectionUserMetadataSnapshot metadata = RequireObject(root, "userMetadata")
					.ToObject<CollectionUserMetadataSnapshot>(serializer);

				if (capture == null || installed == null || archives == null || ownerPayloads == null || replay == null || effects == null || metadata == null)
					throw new InvalidDataException("The Local Collection capture package contains an empty required snapshot section.");
				if (!capture.Identity.Equals(inspection.CaptureIdentity) || capture.Capability != inspection.Capability ||
					capture.SchemaVersion != inspection.CaptureSchemaVersion || capture.CapabilityVersion != inspection.CapabilityVersion ||
					!capture.SourceTarget.Equals(inspection.SourceTarget) || installed.DeploymentCommitSequence != inspection.DeploymentCommitSequence)
					throw new InvalidDataException("The Local Collection capture package header does not match its serialized capture body.");

				return new CollectionSealedCaptureSnapshot(capture, installed, archives, ownerPayloads, replay, effects, metadata);
			}
			catch (InvalidDataException)
			{
				throw;
			}
			catch (Exception exception) when (exception is JsonException || exception is ArgumentException || exception is InvalidOperationException)
			{
				throw new InvalidDataException("The Local Collection capture package body is invalid or internally inconsistent.", exception);
			}
		}

		private static JObject ParseRoot(byte[] packageBytes)
		{
			if (packageBytes == null)
				throw new ArgumentNullException(nameof(packageBytes));
			if (packageBytes.Length == 0)
				throw new InvalidDataException("A Local Collection capture package cannot be empty.");

			try
			{
				string json = new UTF8Encoding(false, true).GetString(packageBytes);
				return JObject.Parse(json);
			}
			catch (Exception exception) when (exception is DecoderFallbackException || exception is JsonException)
			{
				throw new InvalidDataException("The Local Collection capture package is not valid UTF-8 JSON.", exception);
			}
		}

		private static CollectionLocalCapturePackageInspection InspectRoot(JObject root)
		{
			RequireString(root, "format", FormatIdentity);
			int formatVersion = RequirePositiveInt(root, "formatVersion");
			if (formatVersion != CurrentFormatVersion)
				throw new InvalidDataException("The Local Collection capture package format version is not supported.");

			Guid captureId;
			if (!Guid.TryParse(RequireString(root, "captureIdentity", null), out captureId) || captureId == Guid.Empty)
				throw new InvalidDataException("The Local Collection capture package has an invalid capture identity.");
			LocalCaptureCapability capability = (LocalCaptureCapability)RequirePositiveInt(root, "capability");
			if (!Enum.IsDefined(typeof(LocalCaptureCapability), capability) || capability == LocalCaptureCapability.Unknown)
				throw new InvalidDataException("The Local Collection capture package has an invalid capability.");
			int captureSchemaVersion = RequirePositiveInt(root, "captureSchemaVersion");
			int capabilityVersion = RequirePositiveInt(root, "capabilityVersion");
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint(RequireString(root, "sourceTarget", null));
			long deploymentCommitSequence = RequireNonNegativeLong(root, "deploymentCommitSequence");

			RequireObject(root, "capture");
			RequireObject(root, "installedIdentities");
			RequireArray(root, "archives");
			RequireObject(root, "ownerPayloads");
			RequireObject(root, "scriptedReplay");
			RequireObject(root, "nativeEffects");
			RequireObject(root, "userMetadata");
			RequireArray(root, "sealingIssues");

			return new CollectionLocalCapturePackageInspection(formatVersion, LocalCaptureIdentity.From(captureId), capability,
				captureSchemaVersion, capabilityVersion, target, deploymentCommitSequence);
		}

		private static LocalCapture DeserializeCapture(JObject token, JsonSerializer serializer)
		{
			LocalCaptureIdentity identity = RequireObject(token, "Identity").ToObject<LocalCaptureIdentity>(serializer);
			CollectionRevisionIdentity revision = RequireObject(token, "Revision").ToObject<CollectionRevisionIdentity>(serializer);
			CollectionTargetIdentity target = RequireObject(token, "SourceTarget").ToObject<CollectionTargetIdentity>(serializer);
			CollectionCurrentStateFingerprint fingerprint = RequireObject(token, "CapturedStateFingerprint").ToObject<CollectionCurrentStateFingerprint>(serializer);
			LocalCaptureScope scope = RequireObject(token, "Scope").ToObject<LocalCaptureScope>(serializer);
			LocalCaptureCapability capability = (LocalCaptureCapability)RequirePositiveInt(token, "Capability");
			int schemaVersion = RequirePositiveInt(token, "SchemaVersion");
			int capabilityVersion = RequirePositiveInt(token, "CapabilityVersion");
			List<RetainedArtifactReference> retained = RequireArray(token, "RetainedArtifacts").ToObject<List<RetainedArtifactReference>>(serializer);
			List<LocalCaptureExclusion> exclusions = RequireArray(token, "Exclusions").ToObject<List<LocalCaptureExclusion>>(serializer);
			List<LocalCaptureNativeRecordMapping> mappings = RequireArray(token, "NativeRecordMappings").ToObject<List<LocalCaptureNativeRecordMapping>>(serializer);
			return new LocalCapture(identity, revision, target, fingerprint, scope, capability, schemaVersion, capabilityVersion,
				retained, exclusions, mappings);
		}

		private static JsonSerializer CreateRestoreSerializer()
		{
			JsonSerializer serializer = JsonSerializer.CreateDefault();
			serializer.Converters.Add(new CollectionIdentityJsonConverter());
			serializer.Converters.Add(new CollectionRevisionIdentityJsonConverter());
			serializer.Converters.Add(new LocalCaptureIdentityJsonConverter());
			serializer.Converters.Add(new CollectionTargetIdentityJsonConverter());
			serializer.Converters.Add(new CollectionMemberKeyJsonConverter());
			serializer.Converters.Add(new CollectionRecipeIdentityJsonConverter());
			serializer.Converters.Add(new CollectionContentHashJsonConverter());
			serializer.Converters.Add(new ModDeploymentTargetJsonConverter());
			return serializer;
		}

		private static string RequireString(JObject root, string propertyName, string expected)
		{
			JToken token = root[propertyName];
			if (token == null || token.Type != JTokenType.String || String.IsNullOrWhiteSpace((string)token))
				throw new InvalidDataException("The Local Collection capture package is missing required string field '" + propertyName + "'.");
			string value = (string)token;
			if (expected != null && !StringComparer.Ordinal.Equals(value, expected))
				throw new InvalidDataException("The Local Collection capture package has an unexpected '" + propertyName + "' value.");
			return value;
		}

		private static int RequirePositiveInt(JObject root, string propertyName)
		{
			JToken token = root[propertyName];
			int value;
			if (token == null || token.Type != JTokenType.Integer || !Int32.TryParse(token.ToString(), out value) || value <= 0)
				throw new InvalidDataException("The Local Collection capture package has an invalid '" + propertyName + "' value.");
			return value;
		}

		private static long RequireNonNegativeLong(JObject root, string propertyName)
		{
			JToken token = root[propertyName];
			long value;
			if (token == null || token.Type != JTokenType.Integer || !Int64.TryParse(token.ToString(), out value) || value < 0)
				throw new InvalidDataException("The Local Collection capture package has an invalid '" + propertyName + "' value.");
			return value;
		}

		private static JObject RequireObject(JObject root, string propertyName)
		{
			JObject value = root[propertyName] as JObject;
			if (value == null)
				throw new InvalidDataException("The Local Collection capture package is missing required object '" + propertyName + "'.");
			return value;
		}

		private static JArray RequireArray(JObject root, string propertyName)
		{
			JArray value = root[propertyName] as JArray;
			if (value == null)
				throw new InvalidDataException("The Local Collection capture package is missing required array '" + propertyName + "'.");
			return value;
		}

		private abstract class ReadOnlyJsonConverter : JsonConverter
		{
			public override bool CanWrite { get { return false; } }
			public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
			{
				throw new NotSupportedException();
			}
		}

		private sealed class CollectionIdentityJsonConverter : ReadOnlyJsonConverter
		{
			public override bool CanConvert(Type objectType) { return objectType == typeof(CollectionIdentity); }
			public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
			{
				JObject value = JObject.Load(reader);
				CollectionOrigin origin = (CollectionOrigin)RequirePositiveInt(value, "Origin");
				string stableId = RequireString(value, "StableId", null);
				if (origin == CollectionOrigin.NexusMods)
					return CollectionIdentity.FromNexus(stableId);
				if (origin == CollectionOrigin.Local)
				{
					Guid id;
					if (!Guid.TryParse(stableId, out id) || id == Guid.Empty)
						throw new InvalidDataException("A serialized local Collection identity is invalid.");
					return CollectionIdentity.FromLocal(id);
				}
				throw new InvalidDataException("A serialized Collection identity has an unsupported origin.");
			}
		}

		private sealed class CollectionRevisionIdentityJsonConverter : ReadOnlyJsonConverter
		{
			public override bool CanConvert(Type objectType) { return objectType == typeof(CollectionRevisionIdentity); }
			public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
			{
				JObject value = JObject.Load(reader);
				CollectionIdentity collection = RequireObject(value, "Collection").ToObject<CollectionIdentity>(serializer);
				string stableId = RequireString(value, "StableRevisionId", null);
				if (collection.Origin == CollectionOrigin.Local)
				{
					Guid id;
					if (!Guid.TryParse(stableId, out id) || id == Guid.Empty)
						throw new InvalidDataException("A serialized local Collection revision identity is invalid.");
					return CollectionRevisionIdentity.FromLocal(collection, id);
				}
				JToken revisionNumberToken = value["NexusRevisionNumber"];
				long revisionNumber;
				if (revisionNumberToken == null || revisionNumberToken.Type != JTokenType.Integer || !Int64.TryParse(revisionNumberToken.ToString(), out revisionNumber) || revisionNumber <= 0)
					throw new InvalidDataException("A serialized Nexus Collection revision identity has no valid revision number.");
				return CollectionRevisionIdentity.FromNexus(collection, stableId, revisionNumber);
			}
		}

		private sealed class LocalCaptureIdentityJsonConverter : ReadOnlyJsonConverter
		{
			public override bool CanConvert(Type objectType) { return objectType == typeof(LocalCaptureIdentity); }
			public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
			{
				JObject value = JObject.Load(reader);
				Guid id;
				JToken token = value["CaptureId"];
				if (token == null || !Guid.TryParse(token.ToString(), out id) || id == Guid.Empty)
					throw new InvalidDataException("A serialized Local Collection capture identity is invalid.");
				return LocalCaptureIdentity.From(id);
			}
		}

		private sealed class CollectionTargetIdentityJsonConverter : ReadOnlyJsonConverter
		{
			public override bool CanConvert(Type objectType) { return objectType == typeof(CollectionTargetIdentity); }
			public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
			{
				return CollectionTargetIdentity.FromFingerprint(RequireString(JObject.Load(reader), "Fingerprint", null));
			}
		}

		private sealed class CollectionMemberKeyJsonConverter : ReadOnlyJsonConverter
		{
			public override bool CanConvert(Type objectType) { return objectType == typeof(CollectionMemberKey); }
			public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
			{
				JObject value = JObject.Load(reader);
				CollectionMemberKeyKind kind = (CollectionMemberKeyKind)RequirePositiveInt(value, "Kind");
				string key = RequireString(value, "Value", null);
				if (kind == CollectionMemberKeyKind.ProviderStable)
					return CollectionMemberKey.FromProvider(key);
				if (kind == CollectionMemberKeyKind.ValidatedMatch)
					return CollectionMemberKey.FromValidatedMatch(key);
				if (kind == CollectionMemberKeyKind.Local)
				{
					Guid id;
					if (!Guid.TryParse(key, out id) || id == Guid.Empty)
						throw new InvalidDataException("A serialized Local Collection member identity is invalid.");
					return CollectionMemberKey.FromLocal(id);
				}
				throw new InvalidDataException("A serialized Collection member identity has an unsupported kind.");
			}
		}

		private sealed class CollectionRecipeIdentityJsonConverter : ReadOnlyJsonConverter
		{
			public override bool CanConvert(Type objectType) { return objectType == typeof(CollectionRecipeIdentity); }
			public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
			{
				return CollectionRecipeIdentity.FromFingerprint(RequireString(JObject.Load(reader), "Fingerprint", null));
			}
		}

		private sealed class CollectionContentHashJsonConverter : ReadOnlyJsonConverter
		{
			public override bool CanConvert(Type objectType) { return objectType == typeof(CollectionContentHash); }
			public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
			{
				JObject value = JObject.Load(reader);
				CollectionContentHashAlgorithm algorithm = (CollectionContentHashAlgorithm)RequirePositiveInt(value, "Algorithm");
				if (algorithm != CollectionContentHashAlgorithm.Sha256)
					throw new InvalidDataException("A serialized retained artifact uses an unsupported content-hash algorithm.");
				return CollectionContentHash.FromSha256(RequireString(value, "Value", null));
			}
		}

		private sealed class ModDeploymentTargetJsonConverter : ReadOnlyJsonConverter
		{
			public override bool CanConvert(Type objectType) { return objectType == typeof(ModDeploymentTarget); }
			public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
			{
				JObject value = JObject.Load(reader);
				JToken rootToken = value["Root"];
				int rootValue;
				if (rootToken == null || rootToken.Type != JTokenType.Integer || !Int32.TryParse(rootToken.ToString(), out rootValue))
					throw new InvalidDataException("A serialized deployment target has an invalid root.");
				return ModDeploymentTargetResolver.FromCanonical((ModDeploymentRoot)rootValue,
					RequireString(value, "RelativePath", null));
			}
		}
	}
}
