using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Nexus.Client.CollectionManagement;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Nexus.Client.OnlineServices.NexusMods.Collections
{
	/// <summary>
	/// Converts retained Vortex/Nexus collection.json bytes into the provider-neutral C1 normalized model.
	/// </summary>
	/// <remarks>
	/// C2.5 intentionally advertises only a narrow, verified subset. Fields which can change installation behavior but
	/// do not yet have a native NMM adapter are retained in the raw bytes and reported as Unsupported instead of being
	/// silently ignored. Optional unsupported members stay visible without blocking while unselected.
	/// </remarks>
	public sealed class NexusCollectionManifestNormalizer
	{
		public const int MaxManifestBytes = 8 * 1024 * 1024;
		public const string SchemaIdentity = "vortex.collection-json/ICollection@2.6.3";
		public const string NormalizerVersion = "nmm-ce.collections.normalizer/1";
		private const int MaxJsonDepth = 64;

		private static readonly HashSet<string> RootFields = new HashSet<string>(StringComparer.Ordinal)
		{
			"info", "mods", "modRules", "collectionConfig", "plugins", "pluginRules"
		};

		private static readonly HashSet<string> InfoFields = new HashSet<string>(StringComparer.Ordinal)
		{
			"author", "authorUrl", "name", "description", "installInstructions", "domainName", "gameVersions"
		};

		private static readonly HashSet<string> MemberFields = new HashSet<string>(StringComparer.Ordinal)
		{
			"name", "version", "optional", "domainName", "source", "hashes", "choices", "patches",
			"instructions", "author", "details", "phase", "fileOverrides"
		};

		private static readonly HashSet<string> SourceFields = new HashSet<string>(StringComparer.Ordinal)
		{
			"type", "url", "instructions", "modId", "fileId", "updatePolicy", "adultContent", "md5",
			"fileSize", "logicalFilename", "fileExpression", "tag"
		};

		private static readonly HashSet<string> DetailsFields = new HashSet<string>(StringComparer.Ordinal)
		{
			"type", "category"
		};

		/// <summary>
		/// Normalizes exact collection.json bytes for one immutable Nexus Collection revision.
		/// </summary>
		public NexusCollectionManifestNormalizationResult Normalize(byte[] rawManifestBytes, CollectionRevision revision)
		{
			if (rawManifestBytes == null)
				throw new ArgumentNullException(nameof(rawManifestBytes));
			if (revision == null)
				throw new ArgumentNullException(nameof(revision));
			if (!revision.IsRemoteBaseline)
				throw new ArgumentException("The Nexus collection manifest adapter requires a Nexus Collection revision.", nameof(revision));
			if (rawManifestBytes.Length > MaxManifestBytes)
				throw new InvalidDataException("collection.json exceeds the C2.5 bounded manifest size limit.");

			byte[] retainedBytes = (byte[])rawManifestBytes.Clone();
			CollectionManifestSourceSnapshot source = CreateSourceSnapshot(retainedBytes);

			string json;
			try
			{
				json = new UTF8Encoding(false, true).GetString(retainedBytes);
			}
			catch (DecoderFallbackException ex)
			{
				return CreateFatalResult(retainedBytes, revision.Identity, source,
					"manifest.encoding-invalid", "collection.json is not valid UTF-8: " + ex.Message, "$");
			}

			// Preserve the exact bytes/hash above, but tolerate the standard UTF-8 BOM for JSON parsing.
			if (json.Length > 0 && json[0] == '\uFEFF')
				json = json.Substring(1);

			JToken parsed;
			try
			{
				using (var stringReader = new StringReader(json))
				using (var jsonReader = new JsonTextReader(stringReader))
				{
					jsonReader.DateParseHandling = DateParseHandling.None;
					jsonReader.MaxDepth = MaxJsonDepth;
					parsed = JToken.Load(jsonReader, new JsonLoadSettings
					{
						CommentHandling = CommentHandling.Ignore,
						DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
						LineInfoHandling = LineInfoHandling.Load
					});
					if (jsonReader.Read())
						throw new JsonReaderException("collection.json contains trailing JSON content after the root value.");
				}
			}
			catch (JsonException ex)
			{
				return CreateFatalResult(retainedBytes, revision.Identity, source,
					"manifest.json-invalid", "collection.json could not be parsed safely: " + ex.Message, "$");
			}

			JObject root = parsed as JObject;
			if (root == null)
			{
				return CreateFatalResult(retainedBytes, revision.Identity, source,
					"manifest.root-invalid", "collection.json must contain one JSON object at its root.", "$");
			}

			List<CollectionCapabilityIssue> manifestIssues = new List<CollectionCapabilityIssue>();
			AddUnknownManifestFields(root, RootFields, "$", manifestIssues);
			ValidateInfo(root["info"], manifestIssues);
			ValidateTopLevelBehavior(root, manifestIssues);

			JArray rawMembers = root["mods"] as JArray;
			bool memberSetIncomplete = false;
			string incompletenessReason = null;
			List<MemberDraft> drafts = new List<MemberDraft>();

			if (rawMembers == null)
			{
				memberSetIncomplete = true;
				incompletenessReason = "collection.json does not expose a complete mods array.";
				manifestIssues.Add(CollectionCapabilityIssue.ForManifest(
					CollectionCompatibilityStatus.Unsupported,
					"manifest.mods-invalid",
					"The verified Vortex collection schema requires a mods array.",
					"$.mods"));
			}
			else
			{
				for (int index = 0; index < rawMembers.Count; index++)
				{
					JObject memberObject = rawMembers[index] as JObject;
					if (memberObject == null)
					{
						memberSetIncomplete = true;
						incompletenessReason = "One or more manifest members could not be normalized without inventing required fields.";
						manifestIssues.Add(CollectionCapabilityIssue.ForManifest(
							CollectionCompatibilityStatus.Unsupported,
							"manifest.member-invalid",
							"A mods entry must be a JSON object.",
							"$.mods[" + index.ToString(CultureInfo.InvariantCulture) + "]"));
						continue;
					}

					bool optional;
					if (!TryReadRequiredBoolean(memberObject, "optional", out optional))
					{
						memberSetIncomplete = true;
						incompletenessReason = "One or more manifest members could not be normalized without guessing whether they are required or optional.";
						manifestIssues.Add(CollectionCapabilityIssue.ForManifest(
							CollectionCompatibilityStatus.Unsupported,
							"manifest.member-requirement-invalid",
							"A collection member must contain a boolean optional field.",
							"$.mods[" + index.ToString(CultureInfo.InvariantCulture) + "].optional"));
						continue;
					}

					drafts.Add(CreateMemberDraft(memberObject, index, optional));
				}
			}

			Dictionary<string, int> candidateCounts = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (MemberDraft draft in drafts)
			{
				if (draft.StableMatchKey == null)
					continue;
				int count;
				candidateCounts.TryGetValue(draft.StableMatchKey, out count);
				candidateCounts[draft.StableMatchKey] = count + 1;
			}

			List<NormalizedCollectionMember> members = new List<NormalizedCollectionMember>();
			List<CollectionCapabilityIssue> allDeclaredIssues = new List<CollectionCapabilityIssue>(manifestIssues);
			foreach (MemberDraft draft in drafts)
			{
				CollectionMemberIdentityResolution identityResolution;
				if (draft.StableMatchKey == null)
				{
					identityResolution = CollectionMemberIdentityResolution.Missing(
						"The manifest member does not expose a stable, currently supported identity tuple.");
				}
				else if (candidateCounts[draft.StableMatchKey] > 1)
				{
					identityResolution = CollectionMemberIdentityResolution.Ambiguous(
						"More than one manifest member references the same exact Nexus game/mod/file identity; review is required before assigning a durable member key.");
				}
				else
				{
					identityResolution = CollectionMemberIdentityResolution.Resolved(
						CollectionMemberKey.FromValidatedMatch(draft.StableMatchKey));
				}

				NormalizedCollectionMember member = new NormalizedCollectionMember(
					draft.SourceOrdinal,
					identityResolution,
					draft.Optional ? CollectionMemberRequirement.Optional : CollectionMemberRequirement.Required,
					draft.Optional ? CollectionMemberSelection.Unselected : CollectionMemberSelection.Selected,
					draft.Artifact,
					draft.RecipeIdentity,
					draft.DisplayName);
				members.Add(member);

				foreach (PendingMemberIssue pending in draft.Issues)
				{
					allDeclaredIssues.Add(CollectionCapabilityIssue.ForMember(
						pending.Status,
						pending.Code,
						pending.Reason,
						member,
						pending.FieldPath));
				}
			}

			CollectionManifestMemberSetCompleteness completeness = memberSetIncomplete
				? CollectionManifestMemberSetCompleteness.Incomplete
				: CollectionManifestMemberSetCompleteness.Complete;
			NormalizedCollectionManifest manifest = new NormalizedCollectionManifest(
				revision.Identity,
				source,
				completeness,
				memberSetIncomplete ? incompletenessReason : null,
				members);

			if (revision.DeclaredMemberCount.HasValue && rawMembers != null && revision.DeclaredMemberCount.Value != rawMembers.Count)
			{
				allDeclaredIssues.Add(CollectionCapabilityIssue.ForManifest(
					CollectionCompatibilityStatus.ActionRequired,
					"manifest.declared-member-count-mismatch",
					"The provider-declared member count does not match the retained collection.json mods array; refresh or review the concrete revision before planning.",
					"$.mods"));
			}

			CollectionCapabilityReport report = CollectionCapabilityReport.Create(manifest, allDeclaredIssues);
			return new NexusCollectionManifestNormalizationResult(retainedBytes, manifest, report);
		}

		private static MemberDraft CreateMemberDraft(JObject memberObject, int sourceOrdinal, bool optional)
		{
			var draft = new MemberDraft(sourceOrdinal, optional);
			string memberPath = "$.mods[" + sourceOrdinal.ToString(CultureInfo.InvariantCulture) + "]";

			AddUnknownMemberFields(memberObject, MemberFields, memberPath, draft.Issues);
			ValidateRequiredString(memberObject, "name", memberPath, draft.Issues);
			ValidateRequiredString(memberObject, "version", memberPath, draft.Issues);
			ValidateRequiredString(memberObject, "domainName", memberPath, draft.Issues);
			ValidateOptionalString(memberObject, "instructions", memberPath, draft.Issues);
			ValidateOptionalString(memberObject, "author", memberPath, draft.Issues);

			draft.DisplayName = NormalizeDisplayValue(ReadString(memberObject, "name"));
			draft.RecipeIdentity = CollectionRecipeIdentity.FromFingerprint("sha256:" + ComputeSha256Hex(Canonicalize(BuildRecipeToken(memberObject))));

			JObject source = memberObject["source"] as JObject;
			if (source == null)
			{
				draft.Issues.Add(new PendingMemberIssue(
					CollectionCompatibilityStatus.Unsupported,
					"member.source-invalid",
					"The verified Vortex collection schema requires a source object for every member.",
					memberPath + ".source"));
			}
			else
			{
				NormalizeSource(memberObject, source, memberPath, draft);
			}

			ValidateInstallBehavior(memberObject, memberPath, draft.Issues);
			return draft;
		}

		private static void NormalizeSource(JObject memberObject, JObject source, string memberPath, MemberDraft draft)
		{
			string sourcePath = memberPath + ".source";
			AddUnknownMemberFields(source, SourceFields, sourcePath, draft.Issues);
			ValidateOptionalString(source, "url", sourcePath, draft.Issues);
			ValidateOptionalString(source, "instructions", sourcePath, draft.Issues);
			ValidateOptionalString(source, "md5", sourcePath, draft.Issues);
			ValidateOptionalString(source, "logicalFilename", sourcePath, draft.Issues);
			ValidateOptionalString(source, "fileExpression", sourcePath, draft.Issues);
			ValidateOptionalString(source, "tag", sourcePath, draft.Issues);
			ValidateOptionalBoolean(source, "adultContent", sourcePath, draft.Issues);
			ValidateOptionalNumber(source, "fileSize", sourcePath, draft.Issues);

			JToken sourceTypeToken = source["type"];
			string sourceType = sourceTypeToken != null && sourceTypeToken.Type == JTokenType.String
				? (string)sourceTypeToken
				: null;
			if (sourceType == null)
			{
				draft.Issues.Add(new PendingMemberIssue(
					CollectionCompatibilityStatus.Unsupported,
					"member.source-type-invalid",
					"The source.type field must be one of the characterized Vortex source labels.",
					sourcePath + ".type"));
				return;
			}

			if (!StringComparer.Ordinal.Equals(sourceType, "nexus"))
			{
				draft.Issues.Add(new PendingMemberIssue(
					CollectionCompatibilityStatus.Unsupported,
					"member.source-type-unsupported",
					"C2.5 does not yet translate the Vortex source type '" + sourceType + "' into NMM acquisition semantics.",
					sourcePath + ".type"));
				return;
			}

			long modId;
			long fileId;
			string rawDomain = ReadString(memberObject, "domainName");
			JToken modIdToken = source["modId"];
			JToken fileIdToken = source["fileId"];
			bool missingIdentity = string.IsNullOrWhiteSpace(rawDomain) ||
				modIdToken == null || modIdToken.Type == JTokenType.Null ||
				fileIdToken == null || fileIdToken.Type == JTokenType.Null;
			if (missingIdentity)
			{
				draft.Issues.Add(new PendingMemberIssue(
					CollectionCompatibilityStatus.ActionRequired,
					"member.nexus-source-incomplete",
					"A Nexus source needs a non-empty game domain and explicit modId/fileId before NMM can establish exact member/artifact identity.",
					sourcePath));
				return;
			}

			string domain = NormalizeDomain(rawDomain);
			bool hasModId = TryReadPositiveInteger(modIdToken, out modId);
			bool hasFileId = TryReadPositiveInteger(fileIdToken, out fileId);
			if (domain == null || !hasModId || !hasFileId)
			{
				draft.Issues.Add(new PendingMemberIssue(
					CollectionCompatibilityStatus.Unsupported,
					"member.nexus-source-invalid",
					"A Nexus source contains malformed game/mod/file identity data; NMM will not guess or normalize it into a different identity.",
					sourcePath));
				return;
			}

			string stableArtifactId = string.Format(CultureInfo.InvariantCulture, "{0}/{1}/{2}", domain, modId, fileId);
			draft.StableMatchKey = "nexus-mod-file:" + stableArtifactId;
			draft.Artifact = new CollectionArtifactReference("nexus-mod-file", stableArtifactId, null);

			JToken updatePolicyToken = source["updatePolicy"];
			if (updatePolicyToken == null || updatePolicyToken.Type == JTokenType.Null)
				return; // Vortex currently defaults an absent policy to exact.
			if (updatePolicyToken.Type != JTokenType.String)
			{
				draft.Issues.Add(new PendingMemberIssue(
					CollectionCompatibilityStatus.Unsupported,
					"member.update-policy-invalid",
					"source.updatePolicy must be exact, latest or prefer.",
					sourcePath + ".updatePolicy"));
				return;
			}

			string updatePolicy = (string)updatePolicyToken;
			if (StringComparer.Ordinal.Equals(updatePolicy, "exact"))
				return;
			if (StringComparer.Ordinal.Equals(updatePolicy, "latest") || StringComparer.Ordinal.Equals(updatePolicy, "prefer"))
			{
				draft.Issues.Add(new PendingMemberIssue(
					CollectionCompatibilityStatus.ActionRequired,
					"member.source-policy-needs-resolution",
					"The Vortex '" + updatePolicy + "' policy must be resolved to a concrete Nexus file before an immutable NMM plan can be produced.",
					sourcePath + ".updatePolicy"));
				return;
			}

			draft.Issues.Add(new PendingMemberIssue(
				CollectionCompatibilityStatus.Unsupported,
				"member.update-policy-unsupported",
				"The manifest contains an uncharacterized source.updatePolicy value.",
				sourcePath + ".updatePolicy"));
		}

		private static void ValidateInstallBehavior(JObject memberObject, string memberPath, List<PendingMemberIssue> issues)
		{
			AddUnsupportedWhenPopulated(memberObject, "hashes", "member.file-list-unsupported",
				"C2.5 has not yet translated Vortex hashes/fileList install specifications into native NMM recipe input.", memberPath, issues);
			AddUnsupportedWhenPopulated(memberObject, "choices", "member.installer-choices-unsupported",
				"C2.5 has not yet translated Vortex installer choices into a native NMM installer recipe.", memberPath, issues);
			AddUnsupportedWhenPopulated(memberObject, "patches", "member.patches-unsupported",
				"Collection member patches require a separately validated native ownership/removal adapter.", memberPath, issues);
			AddUnsupportedWhenPopulated(memberObject, "fileOverrides", "member.file-overrides-unsupported",
				"Collection fileOverrides require native per-path planning before they can be applied safely.", memberPath, issues);

			JToken phase = memberObject["phase"];
			if (phase != null && phase.Type != JTokenType.Null)
			{
				double phaseValue;
				if (!TryReadFiniteNumber(phase, out phaseValue))
				{
					issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported,
						"member.phase-invalid", "A collection member phase must be numeric.", memberPath + ".phase"));
				}
				else if (Math.Abs(phaseValue) > Double.Epsilon)
				{
					issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported,
						"member.phase-unsupported", "Non-zero collection installation phases require the later typed dependency/phase planner.", memberPath + ".phase"));
				}
			}

			JToken detailsToken = memberObject["details"];
			if (detailsToken != null && detailsToken.Type != JTokenType.Null)
			{
				JObject details = detailsToken as JObject;
				if (details == null)
				{
					issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported,
						"member.details-invalid", "Member details must be a JSON object when present.", memberPath + ".details"));
				}
				else
				{
					AddUnknownMemberFields(details, DetailsFields, memberPath + ".details", issues);
					ValidateOptionalString(details, "type", memberPath + ".details", issues);
					ValidateOptionalString(details, "category", memberPath + ".details", issues);
					string modType = ReadString(details, "type");
					if (!string.IsNullOrWhiteSpace(modType))
					{
						issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported,
							"member.mod-type-unsupported", "A Vortex-specific member type needs an explicit NMM native ownership/deployment mapping.", memberPath + ".details.type"));
					}
				}
			}
		}

		private static void ValidateTopLevelBehavior(JObject root, List<CollectionCapabilityIssue> issues)
		{
			JToken modRules = root["modRules"];
			if (modRules == null || modRules.Type != JTokenType.Array)
			{
				issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
					"manifest.mod-rules-invalid", "The verified Vortex collection schema requires a modRules array.", "$.modRules"));
			}
			else if (((JArray)modRules).Count > 0)
			{
				issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
					"manifest.mod-rules-unsupported", "Collection dependency/order/conflict rules require the later typed dependency planner.", "$.modRules"));
			}

			AddUnsupportedManifestWhenPopulated(root, "collectionConfig", "manifest.collection-config-unsupported",
				"collectionConfig contains manager/game behavior which C2.5 does not execute or silently ignore.", issues);
			AddUnsupportedManifestWhenPopulated(root, "plugins", "manifest.plugins-unsupported",
				"Collection plugin state requires a native plugin-state adapter and ownership/recovery semantics.", issues);
			AddUnsupportedManifestWhenPopulated(root, "pluginRules", "manifest.plugin-rules-unsupported",
				"Collection plugin rules require the later native plugin/load-order planner.", issues);
		}

		private static void ValidateInfo(JToken infoToken, List<CollectionCapabilityIssue> issues)
		{
			JObject info = infoToken as JObject;
			if (info == null)
			{
				issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
					"manifest.info-invalid", "The verified Vortex collection schema requires an info object.", "$.info"));
				return;
			}

			AddUnknownManifestFields(info, InfoFields, "$.info", issues);
			foreach (string field in new[] { "author", "authorUrl", "name", "description", "domainName" })
			{
				JToken token = info[field];
				if (token == null || token.Type != JTokenType.String)
				{
					issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
						"manifest.info-field-invalid", "The info." + field + " field must be a string in the verified Vortex schema.", "$.info." + field));
				}
			}

			JToken installInstructions = info["installInstructions"];
			if (installInstructions != null && installInstructions.Type != JTokenType.Null && installInstructions.Type != JTokenType.String)
				issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
					"manifest.info-field-invalid", "info.installInstructions must be a string when present.", "$.info.installInstructions"));

			JToken gameVersions = info["gameVersions"];
			if (gameVersions != null && gameVersions.Type != JTokenType.Null)
			{
				JArray array = gameVersions as JArray;
				if (array == null || array.Any(item => item.Type != JTokenType.String))
					issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
						"manifest.info-field-invalid", "info.gameVersions must be an array of strings when present.", "$.info.gameVersions"));
			}
		}

		private static NexusCollectionManifestNormalizationResult CreateFatalResult(
			byte[] rawBytes,
			CollectionRevisionIdentity revision,
			CollectionManifestSourceSnapshot source,
			string code,
			string reason,
			string fieldPath)
		{
			NormalizedCollectionManifest manifest = new NormalizedCollectionManifest(
				revision,
				source,
				CollectionManifestMemberSetCompleteness.Incomplete,
				"The retained collection.json could not be normalized into a complete member set.",
				new NormalizedCollectionMember[0]);
			CollectionCapabilityReport report = CollectionCapabilityReport.Create(manifest, new[]
			{
				CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported, code, reason, fieldPath)
			});
			return new NexusCollectionManifestNormalizationResult(rawBytes, manifest, report);
		}

		private static CollectionManifestSourceSnapshot CreateSourceSnapshot(byte[] rawBytes)
		{
			return new CollectionManifestSourceSnapshot(
				CollectionContentHash.FromSha256(ComputeSha256Hex(rawBytes)),
				rawBytes.LongLength,
				SchemaIdentity,
				NormalizerVersion);
		}

		private static JToken BuildRecipeToken(JObject memberObject)
		{
			JObject recipe = (JObject)memberObject.DeepClone();
			recipe.Remove("name");
			recipe.Remove("author");
			recipe.Remove("instructions");
			JObject details = recipe["details"] as JObject;
			if (details != null)
			{
				details.Remove("category");
				if (!details.Properties().Any())
					recipe.Remove("details");
			}
			return recipe;
		}

		private static JToken Canonicalize(JToken token)
		{
			JObject objectToken = token as JObject;
			if (objectToken != null)
			{
				JObject result = new JObject();
				foreach (JProperty property in objectToken.Properties().OrderBy(property => property.Name, StringComparer.Ordinal))
					result.Add(property.Name, Canonicalize(property.Value));
				return result;
			}

			JArray arrayToken = token as JArray;
			if (arrayToken != null)
				return new JArray(arrayToken.Select(Canonicalize));

			return token.DeepClone();
		}

		private static string ComputeSha256Hex(JToken token)
		{
			return ComputeSha256Hex(Encoding.UTF8.GetBytes(token.ToString(Formatting.None)));
		}

		internal static string ComputeSha256Hex(byte[] bytes)
		{
			using (SHA256 sha = SHA256.Create())
				return ToHex(sha.ComputeHash(bytes));
		}

		internal static string ComputeSha256Hex(Stream stream)
		{
			using (SHA256 sha = SHA256.Create())
				return ToHex(sha.ComputeHash(stream));
		}

		private static string ToHex(byte[] bytes)
		{
			StringBuilder builder = new StringBuilder(bytes.Length * 2);
			foreach (byte value in bytes)
				builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
			return builder.ToString();
		}

		private static void AddUnknownManifestFields(JObject value, HashSet<string> allowed, string path, List<CollectionCapabilityIssue> issues)
		{
			foreach (JProperty property in value.Properties())
			{
				if (!allowed.Contains(property.Name))
				{
					issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
						"manifest.unknown-field", "The manifest contains an uncharacterized field; NMM will not assume it is cosmetic.", path + "." + property.Name));
				}
			}
		}

		private static void AddUnknownMemberFields(JObject value, HashSet<string> allowed, string path, List<PendingMemberIssue> issues)
		{
			foreach (JProperty property in value.Properties())
			{
				if (!allowed.Contains(property.Name))
				{
					issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported,
						"member.unknown-field", "The member contains an uncharacterized field; NMM will not assume it is cosmetic.", path + "." + property.Name));
				}
			}
		}

		private static void AddUnsupportedManifestWhenPopulated(JObject owner, string field, string code, string reason, List<CollectionCapabilityIssue> issues)
		{
			JToken token = owner[field];
			if (HasContent(token))
				issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported, code, reason, "$." + field));
		}

		private static void AddUnsupportedWhenPopulated(JObject owner, string field, string code, string reason, string memberPath, List<PendingMemberIssue> issues)
		{
			JToken token = owner[field];
			if (HasContent(token))
				issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported, code, reason, memberPath + "." + field));
		}

		private static bool HasContent(JToken token)
		{
			if (token == null || token.Type == JTokenType.Null)
				return false;
			JContainer container = token as JContainer;
			return container == null || container.HasValues;
		}

		private static bool TryReadRequiredBoolean(JObject owner, string field, out bool value)
		{
			JToken token = owner[field];
			if (token != null && token.Type == JTokenType.Boolean)
			{
				value = (bool)token;
				return true;
			}
			value = false;
			return false;
		}

		private static bool TryReadPositiveInteger(JToken token, out long value)
		{
			value = 0;
			if (token == null || token.Type != JTokenType.Integer)
				return false;
			try
			{
				value = token.Value<long>();
				return value > 0;
			}
			catch (Exception ex) when (ex is OverflowException || ex is FormatException || ex is InvalidCastException)
			{
				value = 0;
				return false;
			}
		}

		private static bool TryReadFiniteNumber(JToken token, out double value)
		{
			value = 0;
			if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
				return false;
			value = token.Value<double>();
			return !Double.IsNaN(value) && !Double.IsInfinity(value);
		}

		private static void ValidateRequiredString(JObject owner, string field, string path, List<PendingMemberIssue> issues)
		{
			JToken token = owner[field];
			if (token == null || token.Type != JTokenType.String)
				issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported, "member.schema-field-invalid",
					"The verified Vortex collection schema requires member." + field + " to be a string.", path + "." + field));
		}

		private static void ValidateOptionalString(JObject owner, string field, string path, List<PendingMemberIssue> issues)
		{
			JToken token = owner[field];
			if (token != null && token.Type != JTokenType.Null && token.Type != JTokenType.String)
				issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported, "member.schema-field-invalid",
					field + " must be a string when present.", path + "." + field));
		}

		private static void ValidateOptionalBoolean(JObject owner, string field, string path, List<PendingMemberIssue> issues)
		{
			JToken token = owner[field];
			if (token != null && token.Type != JTokenType.Null && token.Type != JTokenType.Boolean)
				issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported, "member.schema-field-invalid",
					field + " must be a boolean when present.", path + "." + field));
		}

		private static void ValidateOptionalNumber(JObject owner, string field, string path, List<PendingMemberIssue> issues)
		{
			JToken token = owner[field];
			double ignored;
			if (token != null && token.Type != JTokenType.Null && !TryReadFiniteNumber(token, out ignored))
				issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported, "member.schema-field-invalid",
					field + " must be a finite number when present.", path + "." + field));
		}

		private static string ReadString(JObject owner, string field)
		{
			JToken token = owner[field];
			return token != null && token.Type == JTokenType.String ? (string)token : null;
		}

		private static string NormalizeDisplayValue(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			return value.Trim();
		}

		private static string NormalizeDomain(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			string trimmed = value.Trim();
			if (trimmed.IndexOf('/') >= 0 || trimmed.IndexOf('\\') >= 0 || trimmed.IndexOf(':') >= 0)
				return null;
			return trimmed.ToLowerInvariant();
		}

		private sealed class MemberDraft
		{
			public MemberDraft(int sourceOrdinal, bool optional)
			{
				SourceOrdinal = sourceOrdinal;
				Optional = optional;
				Issues = new List<PendingMemberIssue>();
			}

			public int SourceOrdinal { get; }
			public bool Optional { get; }
			public string StableMatchKey { get; set; }
			public CollectionArtifactReference Artifact { get; set; }
			public CollectionRecipeIdentity RecipeIdentity { get; set; }
			public string DisplayName { get; set; }
			public List<PendingMemberIssue> Issues { get; }
		}

		private sealed class PendingMemberIssue
		{
			public PendingMemberIssue(CollectionCompatibilityStatus status, string code, string reason, string fieldPath)
			{
				Status = status;
				Code = code;
				Reason = reason;
				FieldPath = fieldPath;
			}

			public CollectionCompatibilityStatus Status { get; }
			public string Code { get; }
			public string Reason { get; }
			public string FieldPath { get; }
		}
	}
}
