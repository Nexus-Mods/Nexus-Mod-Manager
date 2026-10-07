using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Versioned codec for the exact reviewed C7.9 intent retained by C7.10 Local Collection restore.</summary>
	internal static class CollectionLocalRestoreIntentCodec
	{
		private const string Format = "nmm-ce.collections.local-restore-plan/1";

		/// <summary>Serializes the exact reviewed plan into its immutable operation-owned intent payload.</summary>
		internal static byte[] Serialize(LocalCaptureIdentity captureIdentity, CollectionLocalRestorePlan plan)
		{
			if (captureIdentity == null) throw new ArgumentNullException(nameof(captureIdentity));
			if (plan == null) throw new ArgumentNullException(nameof(plan));
			if (!captureIdentity.Equals(plan.CaptureIdentity))
				throw new InvalidDataException("The Local restore intent source differs from the approved capture identity.");
			using (var stream = new MemoryStream())
			using (var text = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
			using (var writer = new JsonTextWriter(text))
			{
				writer.Formatting = Formatting.None;
				writer.WriteStartObject();
				WriteProperty(writer, "format", Format);
				WriteProperty(writer, "captureId", captureIdentity.ToString());
				WriteProperty(writer, "targetFingerprint", plan.Target.Fingerprint);
				WriteProperty(writer, "planFingerprint", plan.PlanFingerprint);
				writer.WritePropertyName("stateFingerprint");
				writer.WriteStartObject();
				writer.WritePropertyName("formatVersion"); writer.WriteValue(plan.CurrentStateFingerprint.FormatVersion);
				WriteProperty(writer, "value", plan.CurrentStateFingerprint.Value);
				writer.WriteEndObject();
				writer.WritePropertyName("deploymentCommitSequence"); writer.WriteValue(plan.CurrentDeploymentCommitSequence);
				WriteProperty(writer, "originalValuesKey", plan.CurrentOriginalValuesKey);

				writer.WritePropertyName("members");
				writer.WriteStartArray();
				foreach (CollectionLocalRestoreMemberPlan member in plan.Members)
				{
					writer.WriteStartObject();
					writer.WritePropertyName("keyKind"); writer.WriteValue((int)member.SnapshotMemberKey.Kind);
					WriteProperty(writer, "keyValue", member.SnapshotMemberKey.Value);
					WriteProperty(writer, "capturedNativeKey", member.CapturedNativeKey);
					writer.WritePropertyName("action"); writer.WriteValue((int)member.Action);
					WriteProperty(writer, "currentNativeKey", member.CurrentNativeKey);
					writer.WritePropertyName("installMethod"); writer.WriteValue((int)member.InstallContext.Method);
					writer.WritePropertyName("installRoot"); writer.WriteValue((int)member.InstallContext.InstallRoot);
					WriteProperty(writer, "archiveArtifactId", member.RetainedArchive == null ? String.Empty : member.RetainedArchive.RetainedArtifact.StableArtifactId);
					writer.WriteEndObject();
				}
				writer.WriteEndArray();

				writer.WritePropertyName("removeNativeKeys");
				writer.WriteStartArray();
				foreach (string key in plan.CurrentNativeKeysToRemove) writer.WriteValue(key);
				writer.WriteEndArray();

				writer.WritePropertyName("deploymentTargets");
				writer.WriteStartArray();
				foreach (CollectionLocalRestoreDeploymentPlan deployment in plan.DeploymentTargets)
				{
					writer.WriteStartObject();
					writer.WritePropertyName("root"); writer.WriteValue((int)deployment.Target.Root);
					WriteProperty(writer, "path", deployment.Target.RelativePath);
					writer.WritePropertyName("promoted"); writer.WriteValue(deployment.Promoted);
					writer.WritePropertyName("owners"); writer.WriteStartArray();
					foreach (CollectionLocalRestoreOwnerBinding owner in deployment.Owners)
					{
						writer.WriteStartObject();
						writer.WritePropertyName("stackIndex"); writer.WriteValue(owner.StackIndex);
						WriteProperty(writer, "capturedOwnerKey", owner.CapturedOwnerKey);
						writer.WritePropertyName("capturedOwnerKind"); writer.WriteValue((int)owner.CapturedOwnerKind);
						writer.WritePropertyName("currentWinner"); writer.WriteValue(owner.CurrentWinner);
						writer.WritePropertyName("bindingKind"); writer.WriteValue((int)owner.BindingKind);
						WriteProperty(writer, "currentNativeKey", owner.CurrentNativeKey);
						WriteProperty(writer, "recreatedMember", owner.RecreatedSnapshotMember == null ? String.Empty : owner.RecreatedSnapshotMember.ToString());
						writer.WriteEndObject();
					}
					writer.WriteEndArray();
					writer.WriteEndObject();
				}
				writer.WriteEndArray();
				writer.WriteEndObject();
				writer.Flush(); text.Flush();
				return stream.ToArray();
			}
		}

		/// <summary>Reads only the durable capture identity needed to locate the retained sealed package.</summary>
		internal static LocalCaptureIdentity ReadCaptureIdentity(byte[] bytes)
		{
			JObject root = ParseRoot(bytes);
			RequireFormat(root);
			Guid captureId;
			if (!Guid.TryParseExact(RequireString(root, "captureId", false), "D", out captureId) || captureId == Guid.Empty)
				throw new InvalidDataException("The Local restore intent contains an invalid capture identity.");
			return LocalCaptureIdentity.From(captureId);
		}

		/// <summary>Rehydrates the exact approved C7.9 plan using immutable descriptors from its sealed Local Capture package.</summary>
		internal static CollectionLocalRestorePlan Deserialize(byte[] bytes, CollectionSealedCaptureSnapshot sealedCapture)
		{
			if (sealedCapture == null) throw new ArgumentNullException(nameof(sealedCapture));
			JObject root = ParseRoot(bytes);
			RequireFormat(root);
			LocalCaptureIdentity captureIdentity = ReadCaptureIdentity(bytes);
			if (!captureIdentity.Equals(sealedCapture.Capture.Identity))
				throw new InvalidDataException("The reviewed Local restore intent belongs to a different sealed capture.");

			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint(RequireString(root, "targetFingerprint", false));
			string planFingerprint = RequireString(root, "planFingerprint", false);
			JObject state = RequireObject(root, "stateFingerprint");
			var stateFingerprint = new CollectionCurrentStateFingerprint(RequireString(state, "formatVersion", false),
				RequireString(state, "value", false));
			long deploymentCommitSequence = RequireInt64(root, "deploymentCommitSequence");
			string originalValuesKey = RequireString(root, "originalValuesKey", true);

			var members = new List<CollectionLocalRestoreMemberPlan>();
			foreach (JToken token in RequireArray(root, "members"))
			{
				JObject value = token as JObject;
				if (value == null) throw new InvalidDataException("The Local restore intent contains an invalid member record.");
				CollectionMemberKey memberKey = ReadMemberKey((CollectionMemberKeyKind)RequireInt32(value, "keyKind"),
					RequireString(value, "keyValue", false));
				string capturedNativeKey = RequireString(value, "capturedNativeKey", false);
				CollectionLocalRestoreMemberAction action = (CollectionLocalRestoreMemberAction)RequireInt32(value, "action");
				if (!Enum.IsDefined(typeof(CollectionLocalRestoreMemberAction), action))
					throw new InvalidDataException("The Local restore intent contains an unsupported member action.");
				string currentNativeKey = RequireString(value, "currentNativeKey", true);
				var installContext = new ModInstallContext((ModInstallMethod)RequireInt32(value, "installMethod"),
					(ModInstallRoot)RequireInt32(value, "installRoot"));
				string archiveArtifactId = RequireString(value, "archiveArtifactId", true);
				CollectionCapturedArchiveArtifact archive = ResolveArchive(sealedCapture, capturedNativeKey, archiveArtifactId);
				if (action == CollectionLocalRestoreMemberAction.RecreateFromRetainedArchive && archive == null)
					throw new InvalidDataException("A reviewed recreate member no longer resolves its retained source archive.");
				members.Add(new CollectionLocalRestoreMemberPlan(memberKey, capturedNativeKey, action, currentNativeKey,
					installContext, archive));
			}
			if (members.GroupBy(x => x.SnapshotMemberKey).Any(x => x.Count() != 1))
				throw new InvalidDataException("The Local restore intent contains duplicate reviewed member identities.");

			var removeKeys = new List<string>();
			foreach (JToken token in RequireArray(root, "removeNativeKeys"))
			{
				string key = token.Type == JTokenType.String ? (string)token : null;
				if (String.IsNullOrWhiteSpace(key)) throw new InvalidDataException("The Local restore intent contains an invalid native removal key.");
				removeKeys.Add(key);
			}
			if (removeKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != removeKeys.Count)
				throw new InvalidDataException("The Local restore intent contains duplicate native removal keys.");

			var deployments = new List<CollectionLocalRestoreDeploymentPlan>();
			foreach (JToken token in RequireArray(root, "deploymentTargets"))
			{
				JObject value = token as JObject;
				if (value == null) throw new InvalidDataException("The Local restore intent contains an invalid deployment record.");
				ModDeploymentTarget deploymentTarget = ModDeploymentTargetResolver.FromCanonical(
					(ModDeploymentRoot)RequireInt32(value, "root"), RequireString(value, "path", false));
				bool promoted = RequireBoolean(value, "promoted");
				var owners = new List<CollectionLocalRestoreOwnerBinding>();
				foreach (JToken ownerToken in RequireArray(value, "owners"))
				{
					JObject owner = ownerToken as JObject;
					if (owner == null) throw new InvalidDataException("The Local restore intent contains an invalid owner record.");
					int stackIndex = RequireInt32(owner, "stackIndex");
					NativeStateCaptureDeploymentOwnerKind ownerKind = (NativeStateCaptureDeploymentOwnerKind)RequireInt32(owner, "capturedOwnerKind");
					if (!Enum.IsDefined(typeof(NativeStateCaptureDeploymentOwnerKind), ownerKind))
						throw new InvalidDataException("The Local restore intent contains an unsupported captured owner kind.");
					CollectionLocalRestoreOwnerBindingKind bindingKind = (CollectionLocalRestoreOwnerBindingKind)RequireInt32(owner, "bindingKind");
					if (!Enum.IsDefined(typeof(CollectionLocalRestoreOwnerBindingKind), bindingKind))
						throw new InvalidDataException("The Local restore intent contains an unsupported owner binding kind.");
					string recreated = RequireString(owner, "recreatedMember", true);
					owners.Add(new CollectionLocalRestoreOwnerBinding(stackIndex, RequireString(owner, "capturedOwnerKey", true),
						ownerKind, RequireBoolean(owner, "currentWinner"), bindingKind,
						RequireString(owner, "currentNativeKey", true), String.IsNullOrWhiteSpace(recreated) ? null : ReadMemberKey(recreated)));
				}
				if (owners.Select(x => x.StackIndex).Distinct().Count() != owners.Count ||
					owners.OrderBy(x => x.StackIndex).Select((x, index) => x.StackIndex == index).Any(x => !x))
					throw new InvalidDataException("The Local restore intent contains a non-canonical deployment owner order.");
				deployments.Add(new CollectionLocalRestoreDeploymentPlan(deploymentTarget, promoted, owners));
			}
			if (deployments.GroupBy(x => x.Target).Any(x => x.Count() != 1))
				throw new InvalidDataException("The Local restore intent contains duplicate deployment targets.");

			string recomputedFingerprint = CollectionLocalRestorePlanner.CreatePlanFingerprint(sealedCapture.Capture, target,
				stateFingerprint, deploymentCommitSequence, originalValuesKey, members, deployments, removeKeys,
				new CollectionLocalRestorePlanIssue[0]);
			if (!StringComparer.Ordinal.Equals(planFingerprint, recomputedFingerprint))
				throw new InvalidDataException("The Local restore intent plan fingerprint does not match its reviewed payload.");

			var plan = new CollectionLocalRestorePlan(captureIdentity, target, stateFingerprint, deploymentCommitSequence,
				originalValuesKey, planFingerprint, members, deployments, removeKeys, new CollectionLocalRestorePlanIssue[0]);
			byte[] canonical = Serialize(captureIdentity, plan);
			if (!bytes.SequenceEqual(canonical))
				throw new InvalidDataException("The Local restore intent is not the exact canonical v1 payload produced at review time.");
			return plan;
		}

		private static CollectionCapturedArchiveArtifact ResolveArchive(CollectionSealedCaptureSnapshot sealedCapture,
			string capturedNativeKey, string artifactId)
		{
			if (String.IsNullOrWhiteSpace(artifactId)) return null;
			List<CollectionCapturedArchiveArtifact> matches = sealedCapture.Archives.Where(x =>
				StringComparer.OrdinalIgnoreCase.Equals(x.NativeSnapshotKey, capturedNativeKey) &&
				StringComparer.Ordinal.Equals(x.RetainedArtifact.StableArtifactId, artifactId)).ToList();
			if (matches.Count != 1)
				throw new InvalidDataException("The reviewed Local restore intent no longer resolves exactly one retained archive descriptor.");
			return matches[0];
		}

		private static CollectionMemberKey ReadMemberKey(CollectionMemberKeyKind kind, string value)
		{
			switch (kind)
			{
				case CollectionMemberKeyKind.ProviderStable: return CollectionMemberKey.FromProvider(value);
				case CollectionMemberKeyKind.ValidatedMatch: return CollectionMemberKey.FromValidatedMatch(value);
				case CollectionMemberKeyKind.Local:
					Guid local;
					if (!Guid.TryParseExact(value, "D", out local) || local == Guid.Empty)
						throw new InvalidDataException("The Local restore intent contains an invalid local member identity.");
					return CollectionMemberKey.FromLocal(local);
				default: throw new InvalidDataException("The Local restore intent contains an unsupported member-key kind.");
			}
		}

		private static CollectionMemberKey ReadMemberKey(string value)
		{
			int separator = String.IsNullOrWhiteSpace(value) ? -1 : value.IndexOf(':');
			CollectionMemberKeyKind kind;
			if (separator <= 0 || separator == value.Length - 1 ||
				!Enum.TryParse(value.Substring(0, separator), false, out kind))
				throw new InvalidDataException("The Local restore intent contains an invalid recreated-member identity.");
			return ReadMemberKey(kind, value.Substring(separator + 1));
		}

		private static JObject ParseRoot(byte[] bytes)
		{
			if (bytes == null) throw new ArgumentNullException(nameof(bytes));
			if (bytes.Length == 0) throw new InvalidDataException("A Local restore intent cannot be empty.");
			try
			{
				string json = new UTF8Encoding(false, true).GetString(bytes);
				return JObject.Parse(json);
			}
			catch (Exception exception) when (exception is DecoderFallbackException || exception is JsonException)
			{
				throw new InvalidDataException("The Local restore intent is not valid UTF-8 JSON.", exception);
			}
		}

		private static void RequireFormat(JObject root)
		{
			if (!StringComparer.Ordinal.Equals(RequireString(root, "format", false), Format))
				throw new InvalidDataException("The Local restore intent format/version is unsupported.");
		}

		private static JObject RequireObject(JObject root, string name)
		{
			JObject value = root[name] as JObject;
			if (value == null) throw new InvalidDataException("The Local restore intent is missing object '" + name + "'.");
			return value;
		}

		private static JArray RequireArray(JObject root, string name)
		{
			JArray value = root[name] as JArray;
			if (value == null) throw new InvalidDataException("The Local restore intent is missing array '" + name + "'.");
			return value;
		}

		private static string RequireString(JObject root, string name, bool allowEmpty)
		{
			JToken token = root[name];
			string value = token != null && token.Type == JTokenType.String ? (string)token : null;
			if (value == null || (!allowEmpty && String.IsNullOrWhiteSpace(value)))
				throw new InvalidDataException("The Local restore intent contains an invalid string property '" + name + "'.");
			return value;
		}

		private static int RequireInt32(JObject root, string name)
		{
			JToken token = root[name];
			if (token == null || token.Type != JTokenType.Integer) throw new InvalidDataException("The Local restore intent contains an invalid integer property '" + name + "'.");
			try { return token.Value<int>(); }
			catch (Exception exception) when (exception is OverflowException || exception is FormatException || exception is InvalidCastException) { throw new InvalidDataException("The Local restore intent integer property '" + name + "' is out of range.", exception); }
		}

		private static long RequireInt64(JObject root, string name)
		{
			JToken token = root[name];
			if (token == null || token.Type != JTokenType.Integer) throw new InvalidDataException("The Local restore intent contains an invalid integer property '" + name + "'.");
			try { return token.Value<long>(); }
			catch (Exception exception) when (exception is OverflowException || exception is FormatException || exception is InvalidCastException) { throw new InvalidDataException("The Local restore intent integer property '" + name + "' is out of range.", exception); }
		}

		private static bool RequireBoolean(JObject root, string name)
		{
			JToken token = root[name];
			if (token == null || token.Type != JTokenType.Boolean) throw new InvalidDataException("The Local restore intent contains an invalid boolean property '" + name + "'.");
			return token.Value<bool>();
		}

		private static void WriteProperty(JsonWriter writer, string name, string value)
		{
			writer.WritePropertyName(name);
			writer.WriteValue(value ?? String.Empty);
		}
	}
}
