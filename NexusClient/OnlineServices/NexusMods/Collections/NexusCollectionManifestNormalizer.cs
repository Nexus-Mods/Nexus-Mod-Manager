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
	/// The normalizer intentionally advertises only a narrow, verified subset. Fields which can change installation behavior but
	/// do not yet have a native NMM adapter are retained in the raw bytes and reported as Unsupported instead of being
	/// silently ignored. Optional unsupported members stay visible without blocking while unselected.
	/// </remarks>
	public sealed class NexusCollectionManifestNormalizer
	{
		public const int MaxManifestBytes = 8 * 1024 * 1024;
		public const string SchemaIdentity = "vortex.collection-json/ICollection@2.6.3";
		public const string NormalizerVersion = "nmm-ce.collections.normalizer/3";
		private const int MaxJsonDepth = 64;
		private const double OptionalInstallationPhase = 666d;
		private const string SupportedReferenceTagScheme = "v1";

		private static readonly HashSet<string> RootFields = new HashSet<string>(StringComparer.Ordinal)
		{
			"info", "mods", "modRules", "collectionConfig", "plugins", "pluginRules", "tools"
		};

		private static readonly HashSet<string> InfoFields = new HashSet<string>(StringComparer.Ordinal)
		{
			"author", "authorUrl", "name", "description", "installInstructions", "domainName", "gameVersions"
		};

		private static readonly HashSet<string> CollectionConfigFields = new HashSet<string>(StringComparer.Ordinal)
		{
			"recommendNewProfile", "referenceTagScheme"
		};

		private static readonly HashSet<string> PluginRuleFields = new HashSet<string>(StringComparer.Ordinal)
		{
			"plugins", "groups"
		};

		private static readonly HashSet<string> PluginStateFields = new HashSet<string>(StringComparer.Ordinal)
		{
			"name", "enabled"
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

		private static readonly HashSet<string> FomodChoiceRootFields = new HashSet<string>(StringComparer.Ordinal)
		{
			"type", "options"
		};

		private static readonly HashSet<string> FomodStepFields = new HashSet<string>(StringComparer.Ordinal)
		{
			"name", "groups"
		};

		private static readonly HashSet<string> FomodGroupFields = new HashSet<string>(StringComparer.Ordinal)
		{
			"name", "choices"
		};

		private static readonly HashSet<string> FomodSelectedChoiceFields = new HashSet<string>(StringComparer.Ordinal)
		{
			"name", "idx"
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
				throw new InvalidDataException("collection.json exceeds the bounded manifest size limit.");

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

					drafts.Add(CreateMemberDraft(memberObject, index, optional, revision.Identity));
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
						"More than one manifest member references the same exact normalized artifact identity; review is required before assigning a durable member key.");
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
					draft.DisplayName,
					draft.InstallationPhase,
					draft.InstallRootBehavior,
					draft.VortexFomodSelection);
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

			JArray rawModRules = root["modRules"] as JArray;
			List<CollectionExternalFilePriorityRule> externalFilePriorityRules;
			List<CollectionFilePriorityRule> filePriorityRules = NormalizeFilePriorityRules(
				rawModRules, drafts, members, allDeclaredIssues, out externalFilePriorityRules);
			List<CollectionConflictConstraint> conflictConstraints = NormalizeConflictConstraints(
				rawModRules, drafts, members, allDeclaredIssues);
			List<CollectionDesiredPluginState> pluginStates = NormalizePluginStates(root["plugins"], allDeclaredIssues);

			CollectionManifestMemberSetCompleteness completeness = memberSetIncomplete
				? CollectionManifestMemberSetCompleteness.Incomplete
				: CollectionManifestMemberSetCompleteness.Complete;
			NormalizedCollectionManifest manifest = new NormalizedCollectionManifest(
				revision.Identity,
				source,
				completeness,
				memberSetIncomplete ? incompletenessReason : null,
				members,
				null,
				filePriorityRules,
				pluginStates,
				conflictConstraints,
				externalFilePriorityRules);

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

		private static MemberDraft CreateMemberDraft(JObject memberObject, int sourceOrdinal, bool optional, CollectionRevisionIdentity revision)
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
			draft.Version = ReadString(memberObject, "version");
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
				NormalizeSource(memberObject, source, memberPath, draft, revision);
			}

			ValidateInstallBehavior(memberObject, memberPath, draft);
			if (draft.Optional)
				draft.InstallationPhase = OptionalInstallationPhase;
			return draft;
		}

		private static void NormalizeSource(JObject memberObject, JObject source, string memberPath, MemberDraft draft, CollectionRevisionIdentity revision)
		{
			string sourcePath = memberPath + ".source";
			AddUnknownMemberFields(source, SourceFields, sourcePath, draft.Issues);
			ValidateOptionalString(source, "url", sourcePath, draft.Issues);
			ValidateOptionalString(source, "instructions", sourcePath, draft.Issues);
			ValidateOptionalString(source, "md5", sourcePath, draft.Issues);
			ValidateOptionalString(source, "logicalFilename", sourcePath, draft.Issues);
			ValidateOptionalString(source, "fileExpression", sourcePath, draft.Issues);
			ValidateOptionalString(source, "tag", sourcePath, draft.Issues);
			string sourceTag = ReadString(source, "tag");
			if (!String.IsNullOrWhiteSpace(sourceTag) && StringComparer.Ordinal.Equals(sourceTag, sourceTag.Trim()))
				draft.SourceTag = sourceTag;
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

			if (StringComparer.Ordinal.Equals(sourceType, "bundle"))
			{
				NormalizeBundledSource(source, sourcePath, draft, revision);
				return;
			}

			if (!StringComparer.Ordinal.Equals(sourceType, "nexus"))
			{
				draft.Issues.Add(new PendingMemberIssue(
					CollectionCompatibilityStatus.Unsupported,
					"member.source-type-unsupported",
					"The Vortex source type '" + sourceType + "' does not have a characterized NMM acquisition mapping.",
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
			draft.SourceDomain = domain;
			draft.SourceModId = modId;
			draft.SourceFileId = fileId;
			draft.SourceMd5 = ReadString(source, "md5");
			draft.SourceLogicalFilename = ReadString(source, "logicalFilename");
			draft.SourceFileExpression = ReadString(source, "fileExpression");

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

		private static void NormalizeBundledSource(JObject source, string sourcePath, MemberDraft draft, CollectionRevisionIdentity revision)
		{
			string tag = ReadString(source, "tag");
			string fileExpression = ReadString(source, "fileExpression");
			long fileSize;

			if (!CollectionBundledArtifactIdentity.IsValidReferenceTag(tag))
			{
				draft.Issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported,
					"member.bundle-tag-invalid",
					"A bundled Vortex member requires one normalized path-free reference tag so logical identity remains stable across recompression.",
					sourcePath + ".tag"));
				return;
			}
			if (!NexusCollectionBundledArtifactMaterializer.IsSafeLeafName(fileExpression))
			{
				draft.Issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported,
					"member.bundle-path-invalid",
					"A bundled Vortex member must identify exactly one safe directory name beneath the Collection's bundled/ root.",
					sourcePath + ".fileExpression"));
				return;
			}
			if (!TryReadPositiveInteger(source["fileSize"], out fileSize) || fileSize > NexusCollectionBundleAcquirer.DefaultMaximumBundleBytes)
			{
				draft.Issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported,
					"member.bundle-size-invalid",
					"A bundled Vortex member requires a positive bounded integer fileSize before NMM can materialize its embedded directory safely.",
					sourcePath + ".fileSize"));
				return;
			}

			foreach (string unsupportedField in new[] { "url", "modId", "fileId", "md5", "logicalFilename" })
			{
				JToken token = source[unsupportedField];
				if (HasContent(token))
				{
					draft.Issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported,
						"member.bundle-source-semantics-unsupported",
						"The bundled Vortex source also contains uncharacterized source narrowing data; NMM will not silently ignore it.",
						sourcePath + "." + unsupportedField));
				}
			}

			JToken updatePolicyToken = source["updatePolicy"];
			if (updatePolicyToken != null && updatePolicyToken.Type != JTokenType.Null &&
				(updatePolicyToken.Type != JTokenType.String || !StringComparer.Ordinal.Equals((string)updatePolicyToken, "exact")))
			{
				draft.Issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported,
					"member.bundle-update-policy-unsupported",
					"Embedded Collection members are characterized only for exact Vortex bundle acquisition.",
					sourcePath + ".updatePolicy"));
			}

			string stableArtifactId = CollectionBundledArtifactIdentity.Format(revision, tag);
			draft.StableMatchKey = CollectionBundledArtifactIdentity.Scheme + ":" + stableArtifactId;
			draft.Artifact = new CollectionArtifactReference(CollectionBundledArtifactIdentity.Scheme, stableArtifactId, null);
			draft.SourceTag = tag;
			draft.SourceFileExpression = fileExpression;
		}

		private static void ValidateInstallBehavior(JObject memberObject, string memberPath, MemberDraft draft)
		{
			List<PendingMemberIssue> issues = draft.Issues;
			AddUnsupportedWhenPopulated(memberObject, "hashes", "member.file-list-unsupported",
				"Vortex hashes/fileList install specifications do not have a characterized native NMM recipe translation.", memberPath, issues);
			ValidateFomodChoices(memberObject["choices"], memberPath + ".choices", draft);
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
						"member.phase-invalid", "A collection member phase must be a finite number.", memberPath + ".phase"));
				}
				else
				{
					draft.InstallationPhase = phaseValue;
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
						if (modType.Equals("dinput", StringComparison.OrdinalIgnoreCase))
						{
							draft.InstallRootBehavior = CollectionMemberInstallRootBehavior.VortexDInputGameRoot;
						}
						else if (modType.Equals("enb", StringComparison.OrdinalIgnoreCase))
						{
							draft.InstallRootBehavior = CollectionMemberInstallRootBehavior.VortexEnbGameRoot;
						}
						else
						{
							issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported,
								"member.mod-type-unsupported",
								"This Vortex member type has no characterized NMM native install-root mapping.",
								memberPath + ".details.type"));
						}
					}
				}
			}

			if (draft.VortexFomodSelection != null && draft.InstallRootBehavior != CollectionMemberInstallRootBehavior.Default)
			{
				issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported,
					"member.fomod-mod-type-combination-unsupported",
					"Vortex FOMOD choice replay combined with a game-root mod type has no characterized native installer-priority translation.",
					memberPath + ".choices"));
			}
		}


		private static void ValidateFomodChoices(JToken token, string path, MemberDraft draft)
		{
			if (token == null || token.Type == JTokenType.Null)
				return;
			JObject root = token as JObject;
			if (root == null)
			{
				draft.Issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported,
					"member.installer-choices-invalid", "Vortex installer choices must be a JSON object.", path));
				return;
			}
			AddUnknownMemberFields(root, FomodChoiceRootFields, path, draft.Issues);
			JToken typeToken = root["type"];
			if (typeToken == null || typeToken.Type != JTokenType.String ||
				!StringComparer.Ordinal.Equals(typeToken.Value<string>(), "fomod"))
			{
				draft.Issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported,
					"member.installer-choices-type-unsupported", "Only Vortex FOMOD installer choices are characterized by this native translation.", path + ".type"));
				return;
			}
			JArray options = root["options"] as JArray;
			if (options == null || options.Count == 0)
			{
				draft.Issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported,
					"member.installer-choices-invalid", "A characterized Vortex FOMOD choice record requires a non-empty options array.", path + ".options"));
				return;
			}

			var steps = new List<CollectionVortexFomodStepSelection>();
			bool valid = true;
			for (int stepIndex = 0; stepIndex < options.Count; stepIndex++)
			{
				string stepPath = path + ".options[" + stepIndex.ToString(CultureInfo.InvariantCulture) + "]";
				JObject stepObject = options[stepIndex] as JObject;
				if (stepObject == null)
				{
					draft.Issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported,
						"member.installer-choices-invalid", "A Vortex FOMOD install step must be a JSON object.", stepPath));
					valid = false; continue;
				}
				AddUnknownMemberFields(stepObject, FomodStepFields, stepPath, draft.Issues);
				string stepName;
				if (!TryReadNonBlankString(stepObject["name"], out stepName))
				{
					draft.Issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported,
						"member.installer-choices-invalid", "A Vortex FOMOD install step requires a non-empty name.", stepPath + ".name"));
					valid = false; continue;
				}
				JArray groupsToken = stepObject["groups"] as JArray;
				if (groupsToken == null)
				{
					draft.Issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported,
						"member.installer-choices-invalid", "A Vortex FOMOD install step requires a groups array.", stepPath + ".groups"));
					valid = false; continue;
				}
				var groups = new List<CollectionVortexFomodGroupSelection>();
				for (int groupIndex = 0; groupIndex < groupsToken.Count; groupIndex++)
				{
					string groupPath = stepPath + ".groups[" + groupIndex.ToString(CultureInfo.InvariantCulture) + "]";
					JObject groupObject = groupsToken[groupIndex] as JObject;
					if (groupObject == null)
					{
						draft.Issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported,
							"member.installer-choices-invalid", "A Vortex FOMOD option group must be a JSON object.", groupPath));
						valid = false; continue;
					}
					AddUnknownMemberFields(groupObject, FomodGroupFields, groupPath, draft.Issues);
					string groupName;
					if (!TryReadNonBlankString(groupObject["name"], out groupName))
					{
						draft.Issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported,
							"member.installer-choices-invalid", "A Vortex FOMOD option group requires a non-empty name.", groupPath + ".name"));
						valid = false; continue;
					}
					JArray choicesToken = groupObject["choices"] as JArray;
					if (choicesToken == null)
					{
						draft.Issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported,
							"member.installer-choices-invalid", "A Vortex FOMOD option group requires a choices array, including an empty array when nothing was selected.", groupPath + ".choices"));
						valid = false; continue;
					}
					var choices = new List<CollectionVortexFomodChoice>();
					var indices = new HashSet<int>();
					for (int choiceIndex = 0; choiceIndex < choicesToken.Count; choiceIndex++)
					{
						string choicePath = groupPath + ".choices[" + choiceIndex.ToString(CultureInfo.InvariantCulture) + "]";
						JObject choiceObject = choicesToken[choiceIndex] as JObject;
						if (choiceObject == null)
						{
							draft.Issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported,
								"member.installer-choices-invalid", "A selected Vortex FOMOD option must be a JSON object.", choicePath));
							valid = false; continue;
						}
						AddUnknownMemberFields(choiceObject, FomodSelectedChoiceFields, choicePath, draft.Issues);
						string choiceName; int idx;
						if (!TryReadNonBlankString(choiceObject["name"], out choiceName) || !TryReadNonNegativeInt(choiceObject["idx"], out idx) || !indices.Add(idx))
						{
							draft.Issues.Add(new PendingMemberIssue(CollectionCompatibilityStatus.Unsupported,
								"member.installer-choices-invalid", "A selected Vortex FOMOD option requires a non-empty name and a unique non-negative integer idx.", choicePath));
							valid = false; continue;
						}
						choices.Add(new CollectionVortexFomodChoice(idx, choiceName));
					}
					groups.Add(new CollectionVortexFomodGroupSelection(groupName, choices));
				}
				steps.Add(new CollectionVortexFomodStepSelection(stepName, groups));
			}
			if (valid)
				draft.VortexFomodSelection = new CollectionVortexFomodSelection(steps);
		}

		private static bool TryReadNonBlankString(JToken token, out string value)
		{
			value = token != null && token.Type == JTokenType.String ? token.Value<string>() : null;
			return !String.IsNullOrWhiteSpace(value);
		}

		private static bool TryReadNonNegativeInt(JToken token, out int value)
		{
			value = 0;
			if (token == null || token.Type != JTokenType.Integer)
				return false;
			long raw;
			try { raw = token.Value<long>(); }
			catch (Exception ex) when (ex is OverflowException || ex is FormatException || ex is InvalidCastException) { return false; }
			if (raw < 0 || raw > Int32.MaxValue)
				return false;
			value = (int)raw;
			return true;
		}

		/// <summary>
		/// Characterizes portable before/after modRules whose endpoints resolve exactly to normalized members.
		/// </summary>
		private static List<CollectionFilePriorityRule> NormalizeFilePriorityRules(JArray rawRules,
			IReadOnlyList<MemberDraft> drafts, IReadOnlyList<NormalizedCollectionMember> members,
			List<CollectionCapabilityIssue> issues, out List<CollectionExternalFilePriorityRule> externalRules)
		{
			var result = new List<CollectionFilePriorityRule>();
			externalRules = new List<CollectionExternalFilePriorityRule>();
			if (rawRules == null || rawRules.Count == 0)
				return result;

			var candidates = new List<RuleReferenceCandidate>();
			for (int index = 0; index < drafts.Count && index < members.Count; index++)
			{
				if (!members[index].IdentityResolution.IsResolved)
					continue;
				candidates.Add(new RuleReferenceCandidate(members[index].IdentityResolution.Key, drafts[index]));
			}

			var unique = new HashSet<CollectionFilePriorityRule>();
			var uniqueExternal = new HashSet<CollectionExternalFilePriorityRule>();
			for (int index = 0; index < rawRules.Count; index++)
			{
				string path = "$.modRules[" + index.ToString(CultureInfo.InvariantCulture) + "]";
				JObject rule = rawRules[index] as JObject;
				if (rule == null)
				{
					issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
						"manifest.mod-rule-invalid", "A collection modRule must be a JSON object.", path));
					continue;
				}

				string type = ReadString(rule, "type");
				if (StringComparer.Ordinal.Equals(type, "conflicts"))
					continue; // Compatibility constraints are normalized separately; never reinterpret them as file priority.
				if (!StringComparer.Ordinal.Equals(type, "before") && !StringComparer.Ordinal.Equals(type, "after"))
				{
					issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
						"manifest.mod-rule-type-unsupported", "Only characterized Vortex before/after priority rules and conflicts compatibility constraints are supported; other modRule types remain unsupported.", path + ".type"));
					continue;
				}

				CollectionMemberKey sourceMember;
				CollectionMemberKey referenceMember;
				string failure;
				if (!TryResolveFilePrioritySourceReference(rule["source"] as JObject, candidates, out sourceMember, out failure))
				{
					issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
						"manifest.mod-rule-source-unresolved", failure, path + ".source"));
					continue;
				}
				if (!TryResolveRuleReference(rule["reference"] as JObject, candidates, out referenceMember, out failure))
				{
					RuleReferenceCandidate sourceCandidate = candidates.Single(x => x.MemberKey.Equals(sourceMember));
					CollectionConflictReference externalReference;
					if (!TryNormalizeExternalPriorityReference(rule["reference"] as JObject, sourceCandidate.SourceDomain, out externalReference, out failure))
					{
						issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
							"manifest.mod-rule-reference-unresolved", failure, path + ".reference"));
						continue;
					}
					CollectionExternalFilePriorityRule external = new CollectionExternalFilePriorityRule(sourceMember, externalReference,
						StringComparer.Ordinal.Equals(type, "before"));
					if (uniqueExternal.Add(external)) externalRules.Add(external);
					continue;
				}
				if (sourceMember.Equals(referenceMember))
				{
					issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
						"manifest.mod-rule-self-reference", "A before/after file-priority rule cannot target the same normalized member.", path));
					continue;
				}

				CollectionFilePriorityRule normalized = StringComparer.Ordinal.Equals(type, "before")
					? new CollectionFilePriorityRule(sourceMember, referenceMember)
					: new CollectionFilePriorityRule(referenceMember, sourceMember);
				if (unique.Add(normalized))
					result.Add(normalized);
			}
			return result;
		}


		/// <summary>
		/// Resolves the Collection-member side of a before/after rule. Vortex may export the installed archive
		/// fileExpression on a rule even though that expression is not retained on the Collection member source.
		/// When generic exact resolution cannot observe that expression, one unique exact source MD5 plus every
		/// other retained identity field may bind the rule source without guessing archive-name semantics.
		/// </summary>
		private static bool TryResolveFilePrioritySourceReference(JObject reference, IEnumerable<RuleReferenceCandidate> candidates,
			out CollectionMemberKey memberKey, out string failure)
		{
			if (TryResolveRuleReference(reference, candidates, out memberKey, out failure))
				return true;

			memberKey = null;
			if (reference == null)
				return false;

			HashSet<string> allowed = new HashSet<string>(StringComparer.Ordinal)
			{
				"id", "idHint", "archiveId", "md5Hint", "description", "instructions",
				"fileMD5", "logicalFileName", "fileExpression", "versionMatch", "repo", "tag", "gameId"
			};
			foreach (JProperty property in reference.Properties())
			{
				if (!allowed.Contains(property.Name) && property.Value.Type != JTokenType.Null)
				{
					failure = "The before/after rule source contains an uncharacterized matching field ('" + property.Name + "').";
					return false;
				}
			}
			foreach (string field in new[] { "idHint", "archiveId", "md5Hint", "description", "instructions", "fileMD5", "logicalFileName", "fileExpression", "versionMatch", "tag", "gameId" })
			{
				JToken token = reference[field];
				if (token != null && token.Type != JTokenType.Null && token.Type != JTokenType.String)
				{
					failure = "The before/after rule source field '" + field + "' must be a string when present.";
					return false;
				}
			}
			if (reference["id"] != null && reference["id"].Type != JTokenType.Null)
			{
				failure = "A Vortex-local before/after rule source id cannot be rebound to a retained Collection member.";
				return false;
			}
			if (reference["repo"] != null && reference["repo"].Type != JTokenType.Null)
			{
				failure = "The characterized MD5 fallback for a before/after rule source does not ignore repository narrowing semantics.";
				return false;
			}
			if (!String.IsNullOrEmpty(ReadString(reference, "tag")))
			{
				failure = "The characterized MD5 fallback for a before/after rule source does not ignore Vortex reference-tag semantics.";
				return false;
			}

			string fileMd5 = ReadString(reference, "fileMD5");
			if (String.IsNullOrWhiteSpace(fileMd5) || !StringComparer.Ordinal.Equals(fileMd5, fileMd5.Trim()))
			{
				failure = "A before/after rule source that cannot be resolved by the exact Vortex reference subset requires one normalized exact fileMD5 to bind it to the retained member.";
				return false;
			}

			string logicalFileName = ReadString(reference, "logicalFileName");
			string fileExpression = ReadString(reference, "fileExpression");
			string gameId = NormalizeDomain(ReadString(reference, "gameId"));
			string versionMatch = ReadString(reference, "versionMatch");
			if (!IsNormalizedOptionalString(logicalFileName) || !IsNormalizedOptionalString(fileExpression) ||
				!IsNormalizedOptionalString(versionMatch))
			{
				failure = "Before/after rule source identity strings must not contain leading/trailing whitespace.";
				return false;
			}
			if (!String.IsNullOrEmpty(fileExpression) && ContainsGlobSyntax(fileExpression))
			{
				failure = "The characterized before/after rule source fallback does not reinterpret Vortex glob fileExpression semantics.";
				return false;
			}
			if (!String.IsNullOrEmpty(versionMatch) && versionMatch != "*" &&
				(versionMatch.StartsWith(">", StringComparison.Ordinal) || versionMatch.StartsWith("<", StringComparison.Ordinal) ||
				 versionMatch.StartsWith("=", StringComparison.Ordinal) || versionMatch.IndexOf("+prefer", StringComparison.Ordinal) >= 0 ||
				 versionMatch.IndexOf(" ", StringComparison.Ordinal) >= 0 || versionMatch.IndexOf("||", StringComparison.Ordinal) >= 0))
			{
				failure = "The before/after rule source uses a fuzzy/range version matcher outside the characterized exact-MD5 fallback.";
				return false;
			}

			List<RuleReferenceCandidate> matches = candidates.Where(candidate =>
				StringComparer.Ordinal.Equals(fileMd5, candidate.SourceMd5) &&
				(String.IsNullOrEmpty(logicalFileName) || StringComparer.Ordinal.Equals(logicalFileName, candidate.SourceLogicalFilename)) &&
				(String.IsNullOrEmpty(gameId) || StringComparer.Ordinal.Equals(gameId, candidate.SourceDomain)) &&
				(String.IsNullOrEmpty(versionMatch) || versionMatch == "*" || StringComparer.Ordinal.Equals(versionMatch, candidate.Version)) &&
				(String.IsNullOrEmpty(fileExpression) || String.IsNullOrEmpty(candidate.SourceFileExpression) ||
					StringComparer.Ordinal.Equals(fileExpression, candidate.SourceFileExpression))).ToList();
			if (matches.Count != 1)
			{
				failure = matches.Count == 0
					? "The before/after rule source does not match a retained Collection member by its exact source MD5 and retained identity fields."
					: "The before/after rule source MD5/identity fields match more than one retained Collection member.";
				return false;
			}

			memberKey = matches[0].MemberKey;
			failure = null;
			return true;
		}


		private static bool TryNormalizeExternalPriorityReference(JObject reference, string sourceDomain,
			out CollectionConflictReference normalized, out string failure)
		{
			normalized = null;
			failure = "The external before/after endpoint is not a characterized portable Vortex reference.";
			if (reference == null) return false;

			HashSet<string> allowed = new HashSet<string>(StringComparer.Ordinal)
			{
				"id", "idHint", "archiveId", "md5Hint", "description", "instructions",
				"fileMD5", "logicalFileName", "fileExpression", "versionMatch", "repo", "tag", "gameId"
			};
			foreach (JProperty property in reference.Properties())
				if (!allowed.Contains(property.Name) && property.Value.Type != JTokenType.Null)
				{ failure = "The external priority endpoint contains an uncharacterized matching field ('" + property.Name + "')."; return false; }
			if (reference["id"] != null && reference["id"].Type != JTokenType.Null)
			{ failure = "A Vortex-local external priority endpoint id cannot be replayed as portable NMM review state."; return false; }
			if (reference["repo"] != null && reference["repo"].Type != JTokenType.Null)
			{ failure = "External before/after Nexus repository endpoints are not yet characterized by this bounded compatibility slice."; return false; }
			foreach (string field in new[] { "idHint", "archiveId", "md5Hint", "description", "instructions", "fileMD5", "logicalFileName", "fileExpression", "versionMatch", "tag", "gameId" })
			{
				JToken token = reference[field];
				if (token != null && token.Type != JTokenType.Null && token.Type != JTokenType.String)
				{ failure = "The external priority endpoint field '" + field + "' must be a string when present."; return false; }
			}
			string logical = ReadString(reference, "logicalFileName");
			string expression = ReadString(reference, "fileExpression");
			string fileMd5 = ReadString(reference, "fileMD5");
			string tag = ReadString(reference, "tag");
			string gameId = NormalizeDomain(ReadString(reference, "gameId"));
			string version = ReadString(reference, "versionMatch");
			if (!String.IsNullOrEmpty(tag))
			{ failure = "External priority reference tags are not retained by NMM native mod state."; return false; }
			if (!String.IsNullOrEmpty(gameId) && !StringComparer.Ordinal.Equals(gameId, sourceDomain))
			{ failure = "Cross-game external before/after endpoints are outside the characterized single-target model."; return false; }
			if (!IsNormalizedOptionalString(logical) || !IsNormalizedOptionalString(expression) || !IsNormalizedOptionalString(fileMd5))
			{ failure = "External priority identity strings must not contain leading/trailing whitespace."; return false; }
			if (!String.IsNullOrEmpty(expression) && ContainsGlobSyntax(expression))
			{ failure = "Vortex glob fileExpression matching is not characterized for external before/after endpoints."; return false; }
			if (String.IsNullOrWhiteSpace(version)) version = "*";
			if (!StringComparer.Ordinal.Equals(version, "*"))
			{ failure = "Only wildcard-version external before/after endpoints are characterized by this compatibility slice."; return false; }
			if (String.IsNullOrWhiteSpace(logical))
			{ failure = "A characterized external before/after endpoint requires an exact logicalFileName."; return false; }
			CollectionVortexVersionMatch matcher;
			if (!CollectionVortexVersionMatch.TryCreate(version, out matcher, out failure)) return false;
			normalized = new CollectionConflictReference(fileMd5, logical, expression, gameId, null, null, null, null, null, matcher);
			return true;
		}


		/// <summary>
		/// Characterizes Vortex conflicts rules as compatibility constraints. The source must resolve to one Collection member;
		/// the target reference remains portable so it can be checked against both the selected closure and current NMM state.
		/// </summary>
		private static List<CollectionConflictConstraint> NormalizeConflictConstraints(JArray rawRules,
			IReadOnlyList<MemberDraft> drafts, IReadOnlyList<NormalizedCollectionMember> members,
			List<CollectionCapabilityIssue> issues)
		{
			var result = new List<CollectionConflictConstraint>();
			if (rawRules == null || rawRules.Count == 0)
				return result;

			var candidates = new List<RuleReferenceCandidate>();
			for (int index = 0; index < drafts.Count && index < members.Count; index++)
			{
				if (!members[index].IdentityResolution.IsResolved)
					continue;
				candidates.Add(new RuleReferenceCandidate(members[index].IdentityResolution.Key, drafts[index]));
			}

			var unique = new HashSet<CollectionConflictConstraint>();
			for (int index = 0; index < rawRules.Count; index++)
			{
				JObject rule = rawRules[index] as JObject;
				if (rule == null || !StringComparer.Ordinal.Equals(ReadString(rule, "type"), "conflicts"))
					continue;

				string path = "$.modRules[" + index.ToString(CultureInfo.InvariantCulture) + "]";
				CollectionMemberKey sourceMember;
				string failure;
				if (!TryResolveConflictSourceReference(rule["source"] as JObject, candidates, out sourceMember, out failure))
				{
					issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
						"manifest.conflict-rule-source-unresolved", failure, path + ".source"));
					continue;
				}

				RuleReferenceCandidate sourceCandidate = candidates.Single(x => x.MemberKey.Equals(sourceMember));
				CollectionConflictReference reference;
				if (!TryNormalizeConflictReference(rule["reference"] as JObject, sourceCandidate.SourceDomain, out reference, out failure))
				{
					issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
						"manifest.conflict-rule-reference-unsupported", failure, path + ".reference"));
					continue;
				}

				List<CollectionMemberKey> memberMatches = candidates
					.Where(candidate => ConflictReferenceMatchesCandidate(reference, candidate))
					.Select(candidate => candidate.MemberKey)
					.OrderBy(key => (int)key.Kind).ThenBy(key => key.Value, StringComparer.Ordinal).ToList();
				CollectionConflictConstraint normalized = new CollectionConflictConstraint(sourceMember, reference, memberMatches);
				if (unique.Add(normalized))
					result.Add(normalized);
			}
			return result;
		}

		/// <summary>
		/// Resolves the source side of a conflicts rule to the retained Collection member that owns the rule.
		/// Vortex exports Nexus conflict sources with a generated fileExpression that is not repeated in the
		/// Collection member source object. When exact generic resolution cannot observe that expression, a
		/// unique exact source MD5 plus all other retained source fields may bind it more strictly than Vortex.
		/// </summary>
		private static bool TryResolveConflictSourceReference(JObject reference, IEnumerable<RuleReferenceCandidate> candidates,
			out CollectionMemberKey memberKey, out string failure)
		{
			if (TryResolveRuleReference(reference, candidates, out memberKey, out failure))
				return true;

			memberKey = null;
			if (reference == null)
				return false;
			HashSet<string> allowed = new HashSet<string>(StringComparer.Ordinal)
			{
				"id", "idHint", "archiveId", "md5Hint", "description", "instructions",
				"fileMD5", "logicalFileName", "fileExpression", "versionMatch", "repo", "tag", "gameId"
			};
			foreach (JProperty property in reference.Properties())
			{
				if (!allowed.Contains(property.Name) && property.Value.Type != JTokenType.Null)
				{
					failure = "The conflicts-rule source contains an uncharacterized matching field ('" + property.Name + "').";
					return false;
				}
			}
			foreach (string field in new[] { "idHint", "archiveId", "md5Hint", "description", "instructions", "fileMD5", "logicalFileName", "fileExpression", "versionMatch", "tag", "gameId" })
			{
				JToken token = reference[field];
				if (token != null && token.Type != JTokenType.Null && token.Type != JTokenType.String)
				{
					failure = "The conflicts-rule source field '" + field + "' must be a string when present.";
					return false;
				}
			}
			if (!String.IsNullOrEmpty(ReadString(reference, "tag")))
			{
				failure = "The characterized conflicts-rule source MD5 fallback does not ignore Vortex reference-tag semantics.";
				return false;
			}
			string fileMd5 = ReadString(reference, "fileMD5");
			if (String.IsNullOrWhiteSpace(fileMd5) || !StringComparer.Ordinal.Equals(fileMd5, fileMd5.Trim()))
			{
				failure = "A conflicts-rule source that cannot be resolved by the exact Vortex reference subset requires one normalized exact fileMD5 to bind it to the retained member.";
				return false;
			}
			if (reference["id"] != null && reference["id"].Type != JTokenType.Null)
			{
				failure = "A Vortex-local conflicts-rule source id cannot be rebound to a retained Collection member.";
				return false;
			}
			if (reference["repo"] != null && reference["repo"].Type != JTokenType.Null)
			{
				failure = "The characterized MD5 fallback for a conflicts-rule source does not ignore repository narrowing semantics.";
				return false;
			}

			string versionMatch = ReadString(reference, "versionMatch");
			if (!String.IsNullOrEmpty(versionMatch) && versionMatch != "*")
			{
				failure = "The characterized MD5 fallback for a conflicts-rule source accepts only Vortex wildcard source versions.";
				return false;
			}
			string logicalFileName = ReadString(reference, "logicalFileName");
			string gameId = NormalizeDomain(ReadString(reference, "gameId"));
			string fileExpression = ReadString(reference, "fileExpression");
			if (!IsNormalizedOptionalString(logicalFileName) || !IsNormalizedOptionalString(fileExpression))
			{
				failure = "Conflicts-rule source identity strings must not contain leading/trailing whitespace.";
				return false;
			}
			if (!String.IsNullOrEmpty(fileExpression) && ContainsGlobSyntax(fileExpression))
			{
				failure = "The characterized conflicts-rule source fallback does not reinterpret Vortex glob fileExpression semantics.";
				return false;
			}

			List<RuleReferenceCandidate> exactMd5Matches = candidates.Where(candidate =>
				StringComparer.Ordinal.Equals(fileMd5, candidate.SourceMd5) &&
				(String.IsNullOrEmpty(logicalFileName) || StringComparer.Ordinal.Equals(logicalFileName, candidate.SourceLogicalFilename)) &&
				(String.IsNullOrEmpty(gameId) || StringComparer.Ordinal.Equals(gameId, candidate.SourceDomain)) &&
				(String.IsNullOrEmpty(fileExpression) || String.IsNullOrEmpty(candidate.SourceFileExpression) ||
					StringComparer.Ordinal.Equals(fileExpression, candidate.SourceFileExpression))).ToList();
			if (exactMd5Matches.Count != 1)
			{
				failure = exactMd5Matches.Count == 0
					? "The conflicts-rule source does not match a retained Collection member by its exact source MD5 and retained identity fields."
					: "The conflicts-rule source MD5/identity fields match more than one retained Collection member.";
				return false;
			}
			memberKey = exactMd5Matches[0].MemberKey;
			failure = null;
			return true;
		}

		private static bool TryNormalizeConflictReference(JObject reference, string sourceDomain, out CollectionConflictReference normalized, out string failure)
		{
			normalized = null;
			failure = "The Vortex conflict rule does not contain a characterized portable target reference.";
			if (reference == null)
				return false;

			HashSet<string> allowed = new HashSet<string>(StringComparer.Ordinal)
			{
				"id", "idHint", "archiveId", "md5Hint", "description", "instructions",
				"fileMD5", "logicalFileName", "fileExpression", "versionMatch", "repo", "tag", "gameId"
			};
			foreach (JProperty property in reference.Properties())
			{
				if (!allowed.Contains(property.Name) && property.Value.Type != JTokenType.Null)
				{
					failure = "The conflict target contains an uncharacterized matching field ('" + property.Name + "').";
					return false;
				}
			}

			// id is local Vortex state and changes the matcher itself; NMM will not pretend it is portable.
			if (reference["id"] != null && reference["id"].Type != JTokenType.Null)
			{
				failure = "A Vortex-local conflict reference id cannot be replayed as a portable NMM compatibility constraint.";
				return false;
			}
			foreach (string field in new[] { "idHint", "archiveId", "md5Hint", "description", "instructions", "fileMD5", "logicalFileName", "fileExpression", "versionMatch", "tag", "gameId" })
			{
				JToken token = reference[field];
				if (token != null && token.Type != JTokenType.Null && token.Type != JTokenType.String)
				{
					failure = "The conflict target field '" + field + "' must be a string when present.";
					return false;
				}
			}

			string fileMd5 = ReadString(reference, "fileMD5");
			string logicalFileName = ReadString(reference, "logicalFileName");
			string fileExpression = ReadString(reference, "fileExpression");
			string tag = ReadString(reference, "tag");
			string gameId = NormalizeDomain(ReadString(reference, "gameId"));
			if (!String.IsNullOrEmpty(tag))
			{
				failure = "Conflict target reference tags are not retained by NMM native mod state and therefore cannot be used for a complete compatibility check.";
				return false;
			}
			if (!String.IsNullOrEmpty(gameId) && !StringComparer.Ordinal.Equals(gameId, sourceDomain))
			{
				failure = "Cross-game Vortex conflict references are outside the characterized single-target Collection compatibility model.";
				return false;
			}
			if (!IsNormalizedOptionalString(fileMd5) || !IsNormalizedOptionalString(logicalFileName) ||
				!IsNormalizedOptionalString(fileExpression) || !IsNormalizedOptionalString(tag))
			{
				failure = "Conflict identity strings must not contain leading/trailing whitespace.";
				return false;
			}
			if (!String.IsNullOrEmpty(fileExpression) && ContainsGlobSyntax(fileExpression))
			{
				failure = "Vortex glob fileExpression conflict matching is not characterized; only exact archive expressions are supported.";
				return false;
			}

			string rawVersion = ReadString(reference, "versionMatch");
			if (String.IsNullOrWhiteSpace(rawVersion)) rawVersion = "*";
			CollectionVortexVersionMatch versionMatch;
			if (!CollectionVortexVersionMatch.TryCreate(rawVersion, out versionMatch, out failure))
				return false;

			string repository = null;
			string repositoryGameId = null;
			long? repositoryModId = null;
			long? repositoryFileId = null;
			JObject repo = reference["repo"] as JObject;
			if (reference["repo"] != null && reference["repo"].Type != JTokenType.Null && repo == null)
			{
				failure = "The conflict target repository reference is malformed.";
				return false;
			}
			if (repo != null)
			{
				HashSet<string> allowedRepo = new HashSet<string>(StringComparer.Ordinal)
				{
					"repository", "gameId", "modId", "fileId", "campaign"
				};
				foreach (JProperty property in repo.Properties())
				{
					if (!allowedRepo.Contains(property.Name) && property.Value.Type != JTokenType.Null)
					{
						failure = "The conflict repository reference contains an uncharacterized field ('" + property.Name + "').";
						return false;
					}
				}
				repository = ReadString(repo, "repository");
				if (!StringComparer.OrdinalIgnoreCase.Equals(repository, "nexus"))
				{
					failure = "Only Nexus repository conflict references are characterized.";
					return false;
				}
				long modId;
				if (!TryReadPositiveIntegerFlexible(repo["modId"], out modId))
				{
					failure = "A Nexus conflict repository reference requires a positive modId.";
					return false;
				}
				repositoryModId = modId;
				if (repo["fileId"] != null && repo["fileId"].Type != JTokenType.Null)
				{
					long fileId;
					if (!TryReadPositiveIntegerFlexible(repo["fileId"], out fileId))
					{
						failure = "A Nexus conflict repository fileId must be a positive integer when present.";
						return false;
					}
					repositoryFileId = fileId;
				}
				repositoryGameId = NormalizeDomain(ReadString(repo, "gameId"));
				if (!String.IsNullOrEmpty(repositoryGameId) && !StringComparer.Ordinal.Equals(repositoryGameId, sourceDomain))
				{
					failure = "Cross-game Nexus conflict repository references are outside the characterized single-target Collection compatibility model.";
					return false;
				}
				if (!versionMatch.IsAny && !versionMatch.IsRange && !repositoryFileId.HasValue)
				{
					failure = "An exact-version Nexus conflict repository reference requires fileId under the characterized Vortex matcher subset.";
					return false;
				}
			}

			bool fuzzyVersion = versionMatch.IsAny || versionMatch.IsRange;
			bool hasPortableIdentity = !String.IsNullOrWhiteSpace(logicalFileName) || !String.IsNullOrWhiteSpace(fileExpression) ||
				!String.IsNullOrWhiteSpace(tag) || !String.IsNullOrWhiteSpace(repository) ||
				(!fuzzyVersion && !String.IsNullOrWhiteSpace(fileMd5));
			if (!hasPortableIdentity)
			{
				failure = "The conflict target has no portable identifying marker under Vortex fuzzy-version semantics; versionMatch (and fuzzy fileMD5) cannot identify a mod by themselves.";
				return false;
			}

			normalized = new CollectionConflictReference(fileMd5, logicalFileName, fileExpression, gameId, tag,
				repository, repositoryGameId, repositoryModId, repositoryFileId, versionMatch);
			return true;
		}

		private static bool ConflictReferenceMatchesCandidate(CollectionConflictReference reference, RuleReferenceCandidate candidate)
		{
			bool fuzzy = reference.VersionMatch.IsAny || reference.VersionMatch.IsRange;
			if (!String.IsNullOrEmpty(reference.Tag) && StringComparer.Ordinal.Equals(reference.Tag, candidate.SourceTag))
				return true; // Vortex reference tags are decisive when they hit.
			if (!String.IsNullOrEmpty(reference.FileMd5) && !fuzzy &&
				!StringComparer.Ordinal.Equals(reference.FileMd5, candidate.SourceMd5))
				return false;

			if (!String.IsNullOrEmpty(reference.Repository))
			{
				if (!reference.RepositoryModId.HasValue || candidate.SourceModId != reference.RepositoryModId.Value)
					return false;
				if (!String.IsNullOrEmpty(reference.RepositoryGameId) &&
					!StringComparer.Ordinal.Equals(reference.RepositoryGameId, candidate.SourceDomain))
					return false;
				if (!fuzzy)
					return reference.RepositoryFileId.HasValue && candidate.SourceFileId == reference.RepositoryFileId.Value;
			}
			else if (!String.IsNullOrEmpty(reference.FileMd5) && !fuzzy &&
				StringComparer.Ordinal.Equals(reference.FileMd5, candidate.SourceMd5))
				return true;

			if (!String.IsNullOrEmpty(reference.LogicalFileName) &&
				!StringComparer.Ordinal.Equals(reference.LogicalFileName, candidate.SourceLogicalFilename) &&
				String.IsNullOrEmpty(reference.FileExpression))
				return false;
			if (!String.IsNullOrEmpty(reference.FileExpression) &&
				!StringComparer.Ordinal.Equals(reference.FileExpression, candidate.SourceFileExpression))
				return false;
			if (!String.IsNullOrEmpty(reference.GameId) && !StringComparer.Ordinal.Equals(reference.GameId, candidate.SourceDomain))
				return false;
			return reference.VersionMatch.Evaluate(candidate.Version) == CollectionVortexVersionMatchResult.Match;
		}

		private static bool IsNormalizedOptionalString(string value)
		{
			return value == null || (!String.IsNullOrWhiteSpace(value) && StringComparer.Ordinal.Equals(value, value.Trim()));
		}

		private static bool ContainsGlobSyntax(string value)
		{
			return value.IndexOf('*') >= 0 || value.IndexOf('?') >= 0 || value.IndexOf('[') >= 0 || value.IndexOf(']') >= 0;
		}

		private static bool TryResolveRuleReference(JObject reference,
			IEnumerable<RuleReferenceCandidate> candidates, out CollectionMemberKey memberKey, out string failure)
		{
			memberKey = null;
			failure = "The modRule endpoint does not expose an exact portable member reference supported by the current file-priority characterization.";
			if (reference == null)
				return false;

			HashSet<string> allowed = new HashSet<string>(StringComparer.Ordinal)
			{
				"id", "idHint", "archiveId", "md5Hint", "description", "instructions",
				"fileMD5", "logicalFileName", "fileExpression", "versionMatch", "repo", "tag", "gameId"
			};
			foreach (JProperty property in reference.Properties())
			{
				if (!allowed.Contains(property.Name) && property.Value.Type != JTokenType.Null)
				{
					failure = "The modRule endpoint contains an uncharacterized matching field ('" + property.Name + "'); NMM will not ignore narrowing reference semantics.";
					return false;
				}
			}

			foreach (string field in new[] { "fileMD5", "logicalFileName", "fileExpression", "versionMatch", "tag", "gameId" })
			{
				JToken token = reference[field];
				if (token != null && token.Type != JTokenType.Null && token.Type != JTokenType.String)
				{
					failure = "The modRule endpoint field '" + field + "' must be a string when present.";
					return false;
				}
			}

			JObject repo = reference["repo"] as JObject;
			if (reference["repo"] != null && reference["repo"].Type != JTokenType.Null && repo == null)
			{
				failure = "The modRule repository reference is malformed.";
				return false;
			}
			if (repo != null)
			{
				HashSet<string> allowedRepo = new HashSet<string>(StringComparer.Ordinal)
				{
					"repository", "gameId", "modId", "fileId", "campaign"
				};
				foreach (JProperty property in repo.Properties())
				{
					if (!allowedRepo.Contains(property.Name) && property.Value.Type != JTokenType.Null)
					{
						failure = "The modRule repository reference contains an uncharacterized matching field ('" + property.Name + "').";
						return false;
					}
				}
			}

			string tag = ReadString(reference, "tag");
			if (!String.IsNullOrWhiteSpace(tag))
			{
				if (!StringComparer.Ordinal.Equals(tag, tag.Trim()))
				{
					failure = "The modRule tag reference contains leading/trailing whitespace and is not normalized.";
					return false;
				}
				List<RuleReferenceCandidate> tagMatches = candidates.Where(x => StringComparer.Ordinal.Equals(x.SourceTag, tag)).ToList();
				if (tagMatches.Count == 1)
				{
					memberKey = tagMatches[0].MemberKey;
					return true; // Vortex treats a matching collection reference tag as decisive.
				}
				if (tagMatches.Count > 1)
				{
					failure = "The modRule tag matches more than one normalized member.";
					return false;
				}
			}

			string versionMatch = ReadString(reference, "versionMatch");
			if (!String.IsNullOrWhiteSpace(versionMatch) && versionMatch != "*" &&
				(versionMatch.StartsWith(">", StringComparison.Ordinal) || versionMatch.StartsWith("<", StringComparison.Ordinal) ||
				 versionMatch.StartsWith("=", StringComparison.Ordinal) || versionMatch.IndexOf("+prefer", StringComparison.Ordinal) >= 0 ||
				 versionMatch.IndexOf(" ", StringComparison.Ordinal) >= 0 || versionMatch.IndexOf("||", StringComparison.Ordinal) >= 0))
			{
				failure = "The modRule endpoint uses a fuzzy/range version matcher which NMM does not reinterpret without characterized Vortex semver semantics.";
				return false;
			}

			List<RuleReferenceCandidate> matches = candidates.Where(x => RuleReferenceMatches(reference, repo, x)).ToList();
			if (matches.Count == 0)
			{
				failure = "The modRule endpoint does not match a member in the retained collection revision under the characterized Vortex reference subset.";
				return false;
			}
			if (matches.Count != 1)
			{
				failure = "The modRule endpoint matches more than one normalized member and cannot be used as a deterministic file-priority rule.";
				return false;
			}

			memberKey = matches[0].MemberKey;
			return true;
		}

		private static bool RuleReferenceMatches(JObject reference, JObject repo, RuleReferenceCandidate candidate)
		{
			bool hasMarker = false;
			string fileMd5 = ReadString(reference, "fileMD5");
			string logicalFileName = ReadString(reference, "logicalFileName");
			string fileExpression = ReadString(reference, "fileExpression");
			string versionMatch = ReadString(reference, "versionMatch");
			string gameId = NormalizeDomain(ReadString(reference, "gameId"));
			bool fuzzyVersion = StringComparer.Ordinal.Equals(versionMatch, "*");

			if (repo != null)
			{
				hasMarker = true;
				string repository = ReadString(repo, "repository");
				long modId;
				long fileId;
				if (!StringComparer.OrdinalIgnoreCase.Equals(repository, "nexus") ||
					!TryReadPositiveIntegerFlexible(repo["modId"], out modId) ||
					!TryReadPositiveIntegerFlexible(repo["fileId"], out fileId) ||
					candidate.SourceModId != modId || candidate.SourceFileId != fileId)
					return false;
				string repoGame = NormalizeDomain(ReadString(repo, "gameId"));
				if (repoGame != null && !StringComparer.Ordinal.Equals(repoGame, candidate.SourceDomain))
					return false;
			}
			if (gameId != null && !StringComparer.Ordinal.Equals(gameId, candidate.SourceDomain))
				return false;
			if (!String.IsNullOrEmpty(fileMd5))
			{
				hasMarker = true;
				if (!fuzzyVersion && !StringComparer.Ordinal.Equals(fileMd5, candidate.SourceMd5))
					return false;
			}
			if (!String.IsNullOrEmpty(logicalFileName))
			{
				hasMarker = true;
				if (!StringComparer.Ordinal.Equals(logicalFileName, candidate.SourceLogicalFilename))
					return false;
			}
			if (!String.IsNullOrEmpty(fileExpression))
			{
				hasMarker = true;
				// Vortex supports glob matching here. NMM accepts only the exact retained expression rather than guessing glob semantics.
				if (!StringComparer.Ordinal.Equals(fileExpression, candidate.SourceFileExpression))
					return false;
			}
			if (!String.IsNullOrEmpty(versionMatch) && versionMatch != "*" &&
				!StringComparer.Ordinal.Equals(versionMatch, candidate.Version))
				return false;
			return hasMarker;
		}

		private static bool TryReadPositiveIntegerFlexible(JToken token, out long value)
		{
			if (TryReadPositiveInteger(token, out value))
				return true;
			value = 0;
			if (token == null || token.Type != JTokenType.String)
				return false;
			return Int64.TryParse((string)token, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;
		}


		private static void ValidateTopLevelBehavior(JObject root, List<CollectionCapabilityIssue> issues)
		{
			JToken modRules = root["modRules"];
			if (modRules == null || modRules.Type != JTokenType.Array)
			{
				issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
					"manifest.mod-rules-invalid", "The verified Vortex collection schema requires a modRules array.", "$.modRules"));
			}

			ValidateTools(root["tools"], issues);
			ValidateCollectionConfig(root["collectionConfig"], issues);
			ValidatePluginRules(root["pluginRules"], issues);
		}

		/// <summary>
		/// Accepts only the characterized no-op tools representation; executable Collection tools remain unsupported.
		/// </summary>
		private static void ValidateTools(JToken toolsToken, List<CollectionCapabilityIssue> issues)
		{
			if (toolsToken == null || toolsToken.Type == JTokenType.Null)
				return;

			JArray tools = toolsToken as JArray;
			if (tools == null)
			{
				issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
					"manifest.tools-invalid", "tools must be an array when present.", "$.tools"));
				return;
			}

			if (tools.Count > 0)
			{
				issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
					"manifest.tools-unsupported", "Non-empty Collection tools/commands require an explicitly characterized safe execution lifecycle.", "$.tools"));
			}
		}

		/// <summary>
		/// Validates the narrow Collection configuration subset that does not change native installation effects.
		/// </summary>
		private static void ValidateCollectionConfig(JToken configToken, List<CollectionCapabilityIssue> issues)
		{
			if (configToken == null || configToken.Type == JTokenType.Null)
				return;

			JObject config = configToken as JObject;
			if (config == null)
			{
				issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
					"manifest.collection-config-invalid", "collectionConfig must be an object when present.", "$.collectionConfig"));
				return;
			}

			foreach (JProperty property in config.Properties())
			{
				if (!CollectionConfigFields.Contains(property.Name))
				{
					issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
						"manifest.collection-config-field-unsupported",
						"collectionConfig contains an uncharacterized manager behavior; NMM will not silently ignore it.",
						"$.collectionConfig." + property.Name));
				}
			}

			// Vortex treats this as profile/setup guidance. It is validated here but does not change native install effects.
			JToken recommendNewProfile = config["recommendNewProfile"];
			if (recommendNewProfile != null && recommendNewProfile.Type != JTokenType.Null && recommendNewProfile.Type != JTokenType.Boolean)
			{
				issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
					"manifest.collection-config-invalid",
					"collectionConfig.recommendNewProfile must be a boolean; it is advisory and does not alter native install semantics.",
					"$.collectionConfig.recommendNewProfile"));
			}

			// v1 identifies Vortex deterministic reference tags. NMM consumes retained source.tag values exactly and never
			// regenerates a different tag while normalizing this characterized scheme.
			JToken referenceTagScheme = config["referenceTagScheme"];
			if (referenceTagScheme == null || referenceTagScheme.Type == JTokenType.Null)
				return;
			if (referenceTagScheme.Type != JTokenType.String)
			{
				issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
					"manifest.reference-tag-scheme-invalid", "collectionConfig.referenceTagScheme must be a string when present.",
					"$.collectionConfig.referenceTagScheme"));
				return;
			}

			if (!StringComparer.Ordinal.Equals((string)referenceTagScheme, SupportedReferenceTagScheme))
			{
				issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
					"manifest.reference-tag-scheme-unsupported",
					"The Collection uses an uncharacterized referenceTagScheme; NMM will not assume compatible member-reference semantics.",
					"$.collectionConfig.referenceTagScheme"));
			}
		}

		/// <summary>Normalizes Vortex desired plugin declarations without assigning them to unselected/non-member plugins.</summary>
		private static List<CollectionDesiredPluginState> NormalizePluginStates(JToken pluginsToken, List<CollectionCapabilityIssue> issues)
		{
			if (pluginsToken == null)
				return null;
			if (pluginsToken.Type == JTokenType.Null)
			{
				issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
					"manifest.plugins-invalid", "plugins must be an array when present.", "$.plugins"));
				return new List<CollectionDesiredPluginState>();
			}
			var result = new List<CollectionDesiredPluginState>();
			JArray plugins = pluginsToken as JArray;
			if (plugins == null)
			{
				issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
					"manifest.plugins-invalid", "plugins must be an array when present.", "$.plugins"));
				return result;
			}

			var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			for (int index = 0; index < plugins.Count; index++)
			{
				string path = "$.plugins[" + index.ToString(CultureInfo.InvariantCulture) + "]";
				JObject plugin = plugins[index] as JObject;
				if (plugin == null)
				{
					issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
						"manifest.plugins-invalid", "Each plugins entry must be an object.", path));
					continue;
				}
				foreach (JProperty property in plugin.Properties())
					if (!PluginStateFields.Contains(property.Name))
						issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
							"manifest.plugins-field-unsupported", "A Collection plugin entry contains an uncharacterized field.", path + "." + property.Name));

				JToken nameToken = plugin["name"];
				string name = nameToken != null && nameToken.Type == JTokenType.String ? (string)nameToken : null;
				if (String.IsNullOrWhiteSpace(name) || !StringComparer.Ordinal.Equals(name, name.Trim()) ||
					name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0)
				{
					issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
						"manifest.plugins-invalid", "A Collection plugin name must be one non-empty file name without path separators.", path + ".name"));
					continue;
				}
				JToken enabledToken = plugin["enabled"];
				bool enabled = false;
				if (enabledToken != null)
				{
					if (enabledToken.Type != JTokenType.Boolean)
					{
						issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
							"manifest.plugins-invalid", "Collection plugin enabled must be a boolean when present.", path + ".enabled"));
						continue;
					}
					enabled = (bool)enabledToken;
				}
				if (!names.Add(name))
				{
					issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
						"manifest.plugins-duplicate", "Collection plugin declarations must be unique by plugin file name.", path + ".name"));
					continue;
				}
				result.Add(new CollectionDesiredPluginState(name, enabled));
			}
			return result;
		}

		/// <summary>
		/// Accepts structurally empty Vortex pluginRules while keeping actual plugin/load-order rules fail-closed.
		/// </summary>
		private static void ValidatePluginRules(JToken pluginRulesToken, List<CollectionCapabilityIssue> issues)
		{
			if (pluginRulesToken == null || pluginRulesToken.Type == JTokenType.Null)
				return;

			JObject pluginRules = pluginRulesToken as JObject;
			if (pluginRules == null)
			{
				issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
					"manifest.plugin-rules-invalid", "pluginRules must be an object when present.", "$.pluginRules"));
				return;
			}

			foreach (JProperty property in pluginRules.Properties())
			{
				if (!PluginRuleFields.Contains(property.Name))
				{
					issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
						"manifest.plugin-rules-field-unsupported",
						"pluginRules contains an uncharacterized field; NMM will not assume it is cosmetic.",
						"$.pluginRules." + property.Name));
				}
			}

			ValidateEmptyPluginRuleArray(pluginRules, "plugins", issues);
			ValidateEmptyPluginRuleArray(pluginRules, "groups", issues);
		}

		/// <summary>
		/// Validates one pluginRules array as either absent/empty or explicitly unsupported when populated.
		/// </summary>
		private static void ValidateEmptyPluginRuleArray(JObject pluginRules, string field, List<CollectionCapabilityIssue> issues)
		{
			JToken token = pluginRules[field];
			if (token == null || token.Type == JTokenType.Null)
				return;

			JArray array = token as JArray;
			if (array == null)
			{
				issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
					"manifest.plugin-rules-invalid", "pluginRules." + field + " must be an array when present.", "$.pluginRules." + field));
				return;
			}

			if (array.Count > 0)
			{
				issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
					"manifest.plugin-rules-unsupported",
					"Non-empty Collection plugin/load-order rules require characterized native plugin-rule semantics.",
					"$.pluginRules." + field));
			}
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
			// Installation phase is scheduler metadata, not installed-output identity.
			recipe.Remove("phase");
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

		private sealed class RuleReferenceCandidate
		{
			public RuleReferenceCandidate(CollectionMemberKey memberKey, MemberDraft draft)
			{
				MemberKey = memberKey;
				SourceTag = draft.SourceTag;
				SourceDomain = draft.SourceDomain;
				SourceModId = draft.SourceModId;
				SourceFileId = draft.SourceFileId;
				SourceMd5 = draft.SourceMd5;
				SourceLogicalFilename = draft.SourceLogicalFilename;
				SourceFileExpression = draft.SourceFileExpression;
				Version = draft.Version;
			}
			public CollectionMemberKey MemberKey { get; }
			public string SourceTag { get; }
			public string SourceDomain { get; }
			public long SourceModId { get; }
			public long SourceFileId { get; }
			public string SourceMd5 { get; }
			public string SourceLogicalFilename { get; }
			public string SourceFileExpression { get; }
			public string Version { get; }
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
			public string SourceTag { get; set; }
			public string SourceDomain { get; set; }
			public long SourceModId { get; set; }
			public long SourceFileId { get; set; }
			public string SourceMd5 { get; set; }
			public string SourceLogicalFilename { get; set; }
			public string SourceFileExpression { get; set; }
			public string Version { get; set; }
			public CollectionArtifactReference Artifact { get; set; }
			public CollectionRecipeIdentity RecipeIdentity { get; set; }
			public string DisplayName { get; set; }
			public double InstallationPhase { get; set; }
			public CollectionMemberInstallRootBehavior InstallRootBehavior { get; set; }
			public CollectionVortexFomodSelection VortexFomodSelection { get; set; }
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
