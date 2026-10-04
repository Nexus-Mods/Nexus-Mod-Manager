using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Scripting.Operations;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Durable C10.10 repair approval. It records only the exact qualified repair scope that was reviewed before mutation;
	/// later restart preparation may prove that work is already satisfied, but may never add a newly observed repair.
	/// </summary>
	public sealed class CollectionVerifyRepairReviewedIntent
	{
		private const string FingerprintFormat = "nmm-ce.collections.verify-repair-review-fingerprint/1";
		private readonly ReadOnlyCollection<CollectionVerifyRepairReviewedFinding> _repairs;
		private readonly ReadOnlyCollection<CollectionVerifyRepairPreparedRecipeReview> _preparedRecipes;

		internal CollectionVerifyRepairReviewedIntent(Guid associationId, CollectionAssociationState associationState,
			CollectionRevisionIdentity revision, CollectionTargetIdentity target, CollectionCurrentStateFingerprint observedState,
			IEnumerable<CollectionVerifyRepairReviewedFinding> repairs,
			IEnumerable<CollectionVerifyRepairPreparedRecipeReview> preparedRecipes, string reviewFingerprint)
		{
			if (associationId == Guid.Empty) throw new ArgumentException("Verify/repair review requires a non-empty association identity.", nameof(associationId));
			if (!Enum.IsDefined(typeof(CollectionAssociationState), associationState) || associationState == CollectionAssociationState.Unknown)
				throw new ArgumentOutOfRangeException(nameof(associationState));
			AssociationId = associationId;
			AssociationState = associationState;
			Revision = revision ?? throw new ArgumentNullException(nameof(revision));
			Target = target ?? throw new ArgumentNullException(nameof(target));
			ObservedStateFingerprint = observedState ?? throw new ArgumentNullException(nameof(observedState));

			List<CollectionVerifyRepairReviewedFinding> repairList = (repairs ?? throw new ArgumentNullException(nameof(repairs))).ToList();
			if (repairList.Count == 0 || repairList.Any(x => x == null) || repairList.Any(x => !x.IsQualifiedRepair))
				throw new ArgumentException("A verify/repair reviewed intent requires at least one qualified repair finding.", nameof(repairs));
			if (repairList.GroupBy(x => x.StableKey, StringComparer.Ordinal).Any(x => x.Count() > 1))
				throw new ArgumentException("A verify/repair reviewed intent cannot contain duplicate repair requirements.", nameof(repairs));
			repairList = repairList.OrderBy(x => x.StableKey, StringComparer.Ordinal).ToList();
			_repairs = new ReadOnlyCollection<CollectionVerifyRepairReviewedFinding>(repairList);

			List<CollectionVerifyRepairPreparedRecipeReview> recipeList = (preparedRecipes ?? throw new ArgumentNullException(nameof(preparedRecipes))).ToList();
			if (recipeList.Any(x => x == null) || recipeList.GroupBy(x => x.MemberKey).Any(x => x.Count() > 1))
				throw new ArgumentException("Verify/repair prepared recipe reviews must be non-null and unique by member.", nameof(preparedRecipes));
			recipeList = recipeList.OrderBy(x => x.MemberKey.Kind).ThenBy(x => x.MemberKey.Value, StringComparer.Ordinal).ToList();
			_preparedRecipes = new ReadOnlyCollection<CollectionVerifyRepairPreparedRecipeReview>(recipeList);

			ReviewFingerprint = CollectionIdentityValidation.RequireOpaqueToken(reviewFingerprint, nameof(reviewFingerprint));
			string computed = ComputeFingerprint(this);
			if (!StringComparer.Ordinal.Equals(computed, ReviewFingerprint))
				throw new InvalidDataException("The persisted verify/repair review fingerprint does not match its immutable contents.");
		}

		public Guid AssociationId { get; }
		public CollectionAssociationState AssociationState { get; }
		public CollectionRevisionIdentity Revision { get; }
		public CollectionTargetIdentity Target { get; }
		public CollectionCurrentStateFingerprint ObservedStateFingerprint { get; }
		public ReadOnlyCollection<CollectionVerifyRepairReviewedFinding> Repairs { get { return _repairs; } }
		public ReadOnlyCollection<CollectionVerifyRepairPreparedRecipeReview> PreparedRecipes { get { return _preparedRecipes; } }
		public string ReviewFingerprint { get; }

		public static CollectionVerifyRepairReviewedIntent Create(CollectionVerifyRepairPlan plan)
		{
			if (plan == null) throw new ArgumentNullException(nameof(plan));
			if (!plan.CanExecuteQualifiedRepair || plan.ResolvedPlan == null)
				throw new ArgumentException("Only an exact qualified verify/repair plan can become durable repair intent.", nameof(plan));
			var repairs = plan.Findings.Where(x => x.IsRepairable).Select(CollectionVerifyRepairReviewedFinding.From).ToList();
			var nativeRepairMembers = new HashSet<CollectionMemberKey>(repairs.Where(x => x.MemberKey != null &&
				x.Aspect != CollectionRequirementAspect.MemberEnabledState).Select(x => x.MemberKey));
			var recipes = plan.PreparedRecipes.Where(x => nativeRepairMembers.Contains(x.Member.MemberKey))
				.Select(CollectionVerifyRepairPreparedRecipeReview.From).ToList();
			var shell = new CollectionVerifyRepairReviewedIntentData(plan.Association.AssociationId, plan.Association.State,
				plan.Association.Revision, plan.Association.Target, plan.StateFingerprint, repairs, recipes);
			string fingerprint = ComputeFingerprint(shell);
			return new CollectionVerifyRepairReviewedIntent(shell.AssociationId, shell.AssociationState, shell.Revision, shell.Target,
				shell.ObservedStateFingerprint, shell.Repairs, shell.PreparedRecipes, fingerprint);
		}

		/// <summary>Validates the original in-memory reviewed plan before first native mutation.</summary>
		public void ValidateInitialPlan(CollectionVerifyRepairPlan plan)
		{
			CollectionVerifyRepairReviewedIntent current = Create(plan ?? throw new ArgumentNullException(nameof(plan)));
			if (!StringComparer.Ordinal.Equals(ReviewFingerprint, current.ReviewFingerprint))
				throw new InvalidOperationException("The verify/repair plan changed after its exact repair scope was persisted.");
		}

		/// <summary>
		/// Rebuilds a restart-safe execution plan from fresh read-only verification while preserving the exact original repair scope.
		/// Newly observed repair/action requirements are never absorbed into the old approval.
		/// </summary>
		public CollectionVerifyRepairPlan BuildResumePlan(CollectionVerifyRepairPlan current)
		{
			if (current == null) throw new ArgumentNullException(nameof(current));
			ValidateAssociation(current.Association);
			if (!current.ExactEffectVerificationAvailable || current.ResolvedPlan == null)
				throw new InvalidOperationException("Verify/repair restart cannot reconstruct complete exact native recipe coverage.");
			if (current.HasActionRequired)
				throw new InvalidOperationException("Verify/repair restart found a new ActionRequired condition; the previous approval cannot be widened.");

			Dictionary<string, CollectionVerifyRepairReviewedFinding> approved = _repairs.ToDictionary(x => x.StableKey, StringComparer.Ordinal);
			foreach (CollectionVerifyRepairFinding finding in current.Findings.Where(x => x.IsRepairable))
			{
				CollectionVerifyRepairReviewedFinding expected;
				string key = CollectionVerifyRepairReviewedFinding.GetStableKey(finding);
				if (!approved.TryGetValue(key, out expected) || !expected.Matches(finding))
					throw new InvalidOperationException("Verify/repair restart found a repairable difference outside the exact previously approved scope.");
			}

			ValidatePreparedRecipes(current.PreparedRecipes);
			List<CollectionVerifyRepairFinding> approvedFindings = _repairs.Select(x => x.ToFinding(current.Association)).ToList();
			return new CollectionVerifyRepairPlan(current.Association, ObservedStateFingerprint, approvedFindings, true,
				current.ResolvedPlan, current.PreparedRecipes);
		}

		private void ValidateAssociation(CollectionTargetAssociation association)
		{
			if (association == null || association.AssociationId != AssociationId || association.State != AssociationState ||
				!association.Revision.Equals(Revision) || !association.Target.Equals(Target))
				throw new InvalidOperationException("The Collection association changed after the qualified verify/repair review.");
		}

		private void ValidatePreparedRecipes(IEnumerable<PreparedCollectionNativeRecipe> preparedRecipes)
		{
			Dictionary<CollectionMemberKey, PreparedCollectionNativeRecipe> current = (preparedRecipes ?? Enumerable.Empty<PreparedCollectionNativeRecipe>())
				.ToDictionary(x => x.Member.MemberKey);
			foreach (CollectionVerifyRepairPreparedRecipeReview expected in _preparedRecipes)
			{
				PreparedCollectionNativeRecipe actual;
				if (!current.TryGetValue(expected.MemberKey, out actual) || !expected.Matches(actual))
					throw new InvalidOperationException("Verify/repair restart reconstructed different native recipe output from the reviewed repair.");
			}
		}

		private static string ComputeFingerprint(CollectionVerifyRepairReviewedIntent intent)
		{
			return ComputeFingerprint(new CollectionVerifyRepairReviewedIntentData(intent.AssociationId, intent.AssociationState,
				intent.Revision, intent.Target, intent.ObservedStateFingerprint, intent.Repairs, intent.PreparedRecipes));
		}

		private static string ComputeFingerprint(CollectionVerifyRepairReviewedIntentData data)
		{
			using (var stream = new MemoryStream())
			using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), true))
			{
				writer.Write(FingerprintFormat);
				writer.Write(data.AssociationId.ToString("D"));
				writer.Write((int)data.AssociationState);
				writer.Write((int)data.Revision.Collection.Origin);
				writer.Write(data.Revision.Collection.StableId);
				writer.Write(data.Revision.StableRevisionId);
				writer.Write(data.Revision.NexusRevisionNumber ?? -1L);
				writer.Write(data.Target.Fingerprint);
				writer.Write(data.ObservedStateFingerprint.FormatVersion);
				writer.Write(data.ObservedStateFingerprint.Value);
				writer.Write(data.Repairs.Count);
				foreach (CollectionVerifyRepairReviewedFinding repair in data.Repairs.OrderBy(x => x.StableKey, StringComparer.Ordinal))
				{
					writer.Write(repair.StableKey);
					writer.Write((int)repair.Kind);
					WriteState(writer, repair.ExpectedState);
					WriteState(writer, repair.ObservedState);
				}
				writer.Write(data.PreparedRecipes.Count);
				foreach (CollectionVerifyRepairPreparedRecipeReview recipe in data.PreparedRecipes.OrderBy(x => x.MemberKey.Kind).ThenBy(x => x.MemberKey.Value, StringComparer.Ordinal))
				{
					writer.Write((int)recipe.MemberKey.Kind);
					writer.Write(recipe.MemberKey.Value);
					writer.Write(recipe.ProviderRecipeFingerprint);
					writer.Write(recipe.PreparedNativeFingerprint);
					writer.Write(recipe.ExecutableDescriptorFingerprint);
					writer.Write((int)recipe.InstallMethod);
					writer.Write((int)recipe.InstallRoot);
				}
				writer.Flush();
				using (SHA256 sha = SHA256.Create())
					return BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", String.Empty).ToLowerInvariant();
			}
		}

		private static void WriteState(BinaryWriter writer, CollectionRequirementState state)
		{
			writer.Write(state != null);
			if (state == null) return;
			writer.Write((int)state.Kind);
			writer.Write(state.FormatVersion ?? String.Empty);
			writer.Write(state.Fingerprint ?? String.Empty);
		}

		private sealed class CollectionVerifyRepairReviewedIntentData
		{
			public CollectionVerifyRepairReviewedIntentData(Guid associationId, CollectionAssociationState associationState,
				CollectionRevisionIdentity revision, CollectionTargetIdentity target, CollectionCurrentStateFingerprint observedStateFingerprint,
				IEnumerable<CollectionVerifyRepairReviewedFinding> repairs, IEnumerable<CollectionVerifyRepairPreparedRecipeReview> preparedRecipes)
			{
				AssociationId = associationId; AssociationState = associationState; Revision = revision; Target = target;
				ObservedStateFingerprint = observedStateFingerprint; Repairs = repairs.ToList(); PreparedRecipes = preparedRecipes.ToList();
			}
			public Guid AssociationId; public CollectionAssociationState AssociationState; public CollectionRevisionIdentity Revision;
			public CollectionTargetIdentity Target; public CollectionCurrentStateFingerprint ObservedStateFingerprint;
			public IList<CollectionVerifyRepairReviewedFinding> Repairs; public IList<CollectionVerifyRepairPreparedRecipeReview> PreparedRecipes;
		}
	}

	/// <summary>One exact repair requirement included in the durable C10.10 approval.</summary>
	public sealed class CollectionVerifyRepairReviewedFinding
	{
		internal CollectionVerifyRepairReviewedFinding(CollectionMemberKey memberKey, CollectionRequirementAspect aspect, string subjectKey,
			CollectionVerifyRepairFindingKind kind, CollectionRequirementState expectedState, CollectionRequirementState observedState)
		{
			MemberKey = memberKey;
			if (!Enum.IsDefined(typeof(CollectionRequirementAspect), aspect) || aspect == CollectionRequirementAspect.Unknown)
				throw new ArgumentOutOfRangeException(nameof(aspect));
			if (!Enum.IsDefined(typeof(CollectionVerifyRepairFindingKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
			Aspect = aspect;
			SubjectKey = subjectKey;
			Kind = kind;
			ExpectedState = expectedState;
			ObservedState = observedState;
			StableKey = BuildStableKey(memberKey, aspect, subjectKey);
			IsQualifiedRepair = aspect == CollectionRequirementAspect.MemberEnabledState ||
				kind == CollectionVerifyRepairFindingKind.ManagedFileEffectMismatch ||
				kind == CollectionVerifyRepairFindingKind.IniEffectMismatch ||
				kind == CollectionVerifyRepairFindingKind.GameValueEffectMismatch ||
				kind == CollectionVerifyRepairFindingKind.PluginEffectMismatch;
		}

		public CollectionMemberKey MemberKey { get; }
		public CollectionRequirementAspect Aspect { get; }
		public string SubjectKey { get; }
		public CollectionVerifyRepairFindingKind Kind { get; }
		public CollectionRequirementState ExpectedState { get; }
		public CollectionRequirementState ObservedState { get; }
		public string StableKey { get; }
		public bool IsQualifiedRepair { get; }

		internal static CollectionVerifyRepairReviewedFinding From(CollectionVerifyRepairFinding finding)
		{
			if (finding == null || !finding.IsRepairable || finding.Requirement == null)
				throw new ArgumentException("Reviewed verify/repair scope accepts only repairable findings with stable requirements.", nameof(finding));
			return new CollectionVerifyRepairReviewedFinding(finding.MemberKey, finding.Requirement.Aspect, finding.Requirement.SubjectKey,
				finding.Kind, finding.ExpectedState, finding.ObservedState);
		}

		internal static string GetStableKey(CollectionVerifyRepairFinding finding)
		{
			if (finding == null || finding.Requirement == null) return String.Empty;
			return BuildStableKey(finding.MemberKey, finding.Requirement.Aspect, finding.Requirement.SubjectKey);
		}

		internal bool Matches(CollectionVerifyRepairFinding finding)
		{
			return finding != null && finding.IsRepairable && StringComparer.Ordinal.Equals(StableKey, GetStableKey(finding)) &&
				Kind == finding.Kind && Equals(ExpectedState, finding.ExpectedState) && Equals(ObservedState, finding.ObservedState);
		}

		internal CollectionVerifyRepairFinding ToFinding(CollectionTargetAssociation association)
		{
			var requirement = new CollectionRequirementReference(association, MemberKey, Aspect, SubjectKey);
			return new CollectionVerifyRepairFinding(MemberKey, requirement, Kind, CollectionVerifyRepairDisposition.RestoreExpectedState,
				ExpectedState, ObservedState, "Qualified repair resumed from the exact durable C10.10 reviewed scope.");
		}

		private static string BuildStableKey(CollectionMemberKey memberKey, CollectionRequirementAspect aspect, string subjectKey)
		{
			return (memberKey == null ? "-" : ((int)memberKey.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + memberKey.Value) + "|" +
				((int)aspect).ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + (subjectKey ?? String.Empty);
		}
	}

	/// <summary>Prepared-native identity captured by the exact reviewed repair.</summary>
	public sealed class CollectionVerifyRepairPreparedRecipeReview
	{
		internal CollectionVerifyRepairPreparedRecipeReview(CollectionMemberKey memberKey, string providerRecipeFingerprint,
			string preparedNativeFingerprint, string executableDescriptorFingerprint, ModInstallMethod installMethod, ModInstallRoot installRoot)
		{
			MemberKey = memberKey ?? throw new ArgumentNullException(nameof(memberKey));
			ProviderRecipeFingerprint = CollectionIdentityValidation.RequireOpaqueToken(providerRecipeFingerprint, nameof(providerRecipeFingerprint));
			PreparedNativeFingerprint = CollectionIdentityValidation.RequireOpaqueToken(preparedNativeFingerprint, nameof(preparedNativeFingerprint));
			ExecutableDescriptorFingerprint = CollectionIdentityValidation.RequireOpaqueToken(executableDescriptorFingerprint, nameof(executableDescriptorFingerprint));
			if (!Enum.IsDefined(typeof(ModInstallMethod), installMethod)) throw new ArgumentOutOfRangeException(nameof(installMethod));
			if (installRoot != ModInstallRoot.Data && installRoot != ModInstallRoot.GameRoot) throw new ArgumentOutOfRangeException(nameof(installRoot));
			InstallMethod = installMethod;
			InstallRoot = installRoot;
		}

		public CollectionMemberKey MemberKey { get; }
		public string ProviderRecipeFingerprint { get; }
		public string PreparedNativeFingerprint { get; }
		public string ExecutableDescriptorFingerprint { get; }
		public ModInstallMethod InstallMethod { get; }
		public ModInstallRoot InstallRoot { get; }

		internal static CollectionVerifyRepairPreparedRecipeReview From(PreparedCollectionNativeRecipe recipe)
		{
			return new CollectionVerifyRepairPreparedRecipeReview(recipe.Member.MemberKey, recipe.ProviderRecipeIdentity.Fingerprint,
				recipe.PreparedNativeIdentity.Fingerprint, ComputeExecutableDescriptorFingerprint(recipe),
				recipe.InstallContext.Method, recipe.InstallContext.InstallRoot);
		}

		internal bool Matches(PreparedCollectionNativeRecipe recipe)
		{
			return recipe != null && MemberKey.Equals(recipe.Member.MemberKey) &&
				StringComparer.Ordinal.Equals(ProviderRecipeFingerprint, recipe.ProviderRecipeIdentity.Fingerprint) &&
				StringComparer.Ordinal.Equals(ExecutableDescriptorFingerprint, ComputeExecutableDescriptorFingerprint(recipe)) &&
				InstallMethod == recipe.InstallContext.Method && InstallRoot == recipe.InstallContext.InstallRoot;
		}

		private static string ComputeExecutableDescriptorFingerprint(PreparedCollectionNativeRecipe recipe)
		{
			if (recipe == null) throw new ArgumentNullException(nameof(recipe));
			using (var stream = new MemoryStream())
			using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), true))
			{
				writer.Write("nmm-ce.collections.verify-repair-executable-recipe/1");
				writer.Write(recipe.ProviderRecipeIdentity.Fingerprint);
				WriteSourcePolicySubstitution(writer, recipe.Member.ArtifactChoice);
				writer.Write((int)recipe.InstallContext.Method);
				writer.Write((int)recipe.InstallContext.InstallRoot);
				writer.Write(recipe.SkipReadmeFiles);
				ModInstallationRecipeValidation validation = recipe.Validation;
				writer.Write(validation.AdapterId);
				writer.Write(validation.AdapterVersion);
				writer.Write(validation.ExpectedContent.Sha256);
				writer.Write(validation.ExpectedContent.ByteLength);
				writer.Write(validation.Capabilities.Count);
				foreach (ModInstallationRecipeCapability capability in validation.Capabilities)
				{
					writer.Write(capability.CapabilityId);
					writer.Write(capability.Version);
				}
				writer.Write(validation.Paths.Count);
				foreach (ModInstallationRecipePath path in validation.Paths)
				{
					writer.Write((int)path.Kind);
					writer.Write(path.Path);
				}

				if (!recipe.RecipeInput.HasNativePlan)
					throw new InvalidDataException("Qualified verify/repair requires a translated native recipe plan.");
				writer.Write(recipe.RecipeInput.NativeOperations.Count);
				foreach (var operation in recipe.RecipeInput.NativeOperations)
				{
					InstallModFileOperation file = operation as InstallModFileOperation;
					if (file == null || file.DeploymentDecision != null)
						throw new NotSupportedException("Qualified verify/repair restart currently persists only the characterized unresolved exact file-install recipe subset.");
					writer.Write(file.SourcePath);
					writer.Write(file.DestinationPath);
				}

				CollectionMemberEffectPreview preview = recipe.EffectPreview;
				writer.Write(preview.Files.Count);
				foreach (CollectionPlannedFileEffect file in preview.Files)
				{
					writer.Write((int)file.Target.Root);
					writer.Write(file.Target.RelativePath);
					writer.Write(file.HasExactContentIdentity);
					if (file.HasExactContentIdentity)
					{
						writer.Write((int)file.ExpectedContentHash.Algorithm);
						writer.Write(file.ExpectedContentHash.Value);
						writer.Write(file.ExpectedByteLength.Value);
					}
				}
				writer.Write(preview.IniEdits.Count);
				foreach (CollectionPlannedIniEffect ini in preview.IniEdits)
				{
					writer.Write(ini.Key.File); writer.Write(ini.Key.Section); writer.Write(ini.Key.Key); writer.Write(ini.Value ?? String.Empty);
				}
				writer.Write(preview.GameValues.Count);
				foreach (CollectionPlannedGameValueEffect value in preview.GameValues)
				{
					writer.Write(value.Key); byte[] bytes = value.UnsafeValue; writer.Write(bytes == null ? -1 : bytes.Length); if (bytes != null) writer.Write(bytes);
				}
				writer.Write(preview.PluginEffects.Count);
				foreach (CollectionPlannedPluginEffect plugin in preview.PluginEffects)
				{
					writer.Write((int)plugin.Kind); writer.Write(plugin.Active.HasValue); if (plugin.Active.HasValue) writer.Write(plugin.Active.Value);
					writer.Write(plugin.AbsoluteIndex.HasValue); if (plugin.AbsoluteIndex.HasValue) writer.Write(plugin.AbsoluteIndex.Value);
					writer.Write(plugin.PluginPaths.Count); foreach (string path in plugin.PluginPaths) writer.Write(path);
				}
				writer.Flush();
				using (SHA256 sha = SHA256.Create())
					return BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", String.Empty).ToLowerInvariant();
			}
		}

		private static void WriteSourcePolicySubstitution(BinaryWriter writer, CollectionResolvedArtifactChoice choice)
		{
			if (writer == null) throw new ArgumentNullException(nameof(writer));
			if (choice == null || !choice.IsSubstitution) return;

			// Do not emit anything for exact choices: this intentionally preserves the descriptor hash used by legacy /1 reviews.
			writer.Write("nmm-ce.collections.verify-repair-source-policy-substitution/1");
			writer.Write((int)choice.Kind);
			WriteArtifact(writer, choice.RequestedArtifact);
			WriteArtifact(writer, choice.SelectedArtifact);
			writer.Write(choice.SubstitutionRuleId);
		}

		private static void WriteArtifact(BinaryWriter writer, CollectionArtifactReference artifact)
		{
			if (artifact == null) throw new InvalidDataException("A reviewed source-policy substitution is missing artifact identity.");
			writer.Write(artifact.Scheme);
			writer.Write(artifact.StableId);
			writer.Write(artifact.ExpectedContentHash != null);
			if (artifact.ExpectedContentHash != null)
			{
				writer.Write((int)artifact.ExpectedContentHash.Algorithm);
				writer.Write(artifact.ExpectedContentHash.Value);
			}
		}
	}

	/// <summary>Versioned persistence codec for one exact qualified C10.10 repair approval.</summary>
	public static class CollectionVerifyRepairReviewedIntentCodec
	{
		public const string PayloadFormat = "nmm-ce.collections.verify-repair-review/1";

		public static byte[] Serialize(CollectionVerifyRepairReviewedIntent intent)
		{
			if (intent == null) throw new ArgumentNullException(nameof(intent));
			var dto = new IntentDto
			{
				AssociationId = intent.AssociationId.ToString("D"), AssociationState = (int)intent.AssociationState,
				Revision = RevisionDto.From(intent.Revision), Target = intent.Target.Fingerprint,
				StateFormat = intent.ObservedStateFingerprint.FormatVersion, StateValue = intent.ObservedStateFingerprint.Value,
				Repairs = intent.Repairs.Select(FindingDto.From).ToList(), Recipes = intent.PreparedRecipes.Select(RecipeDto.From).ToList(),
				ReviewFingerprint = intent.ReviewFingerprint
			};
			return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(dto, Formatting.None));
		}

		public static CollectionVerifyRepairReviewedIntent Deserialize(byte[] payload)
		{
			if (payload == null) throw new ArgumentNullException(nameof(payload));
			if (payload.Length == 0) throw new ArgumentException("A verify/repair review payload cannot be empty.", nameof(payload));
			try
			{
				IntentDto dto = JsonConvert.DeserializeObject<IntentDto>(new UTF8Encoding(false, true).GetString(payload));
				if (dto == null) throw new InvalidDataException("The verify/repair review payload is empty.");
				Guid associationId = ParseGuid(dto.AssociationId, "association");
				return new CollectionVerifyRepairReviewedIntent(associationId, (CollectionAssociationState)dto.AssociationState,
					dto.Revision.ToRevision(), CollectionTargetIdentity.FromFingerprint(dto.Target),
					new CollectionCurrentStateFingerprint(dto.StateFormat, dto.StateValue), Require(dto.Repairs, "repair findings").Select(x => x.ToFinding()),
					Require(dto.Recipes, "prepared recipes").Select(x => x.ToRecipe()), dto.ReviewFingerprint);
			}
			catch (InvalidDataException) { throw; }
			catch (Exception ex) when (ex is ArgumentException || ex is ArgumentOutOfRangeException || ex is FormatException || ex is JsonException || ex is NullReferenceException)
			{
				throw new InvalidDataException("The verify/repair review payload is malformed or inconsistent.", ex);
			}
		}

		private static IList<T> Require<T>(IList<T> value, string name)
		{
			if (value == null) throw new InvalidDataException("The verify/repair review is missing " + name + ".");
			return value;
		}

		private static Guid ParseGuid(string value, string name)
		{
			Guid parsed; if (!Guid.TryParseExact(value, "D", out parsed) || parsed == Guid.Empty) throw new InvalidDataException("Invalid " + name + " identifier.");
			return parsed;
		}

		private static CollectionIdentity ReadCollection(int origin, string stableId)
		{
			CollectionOrigin value = (CollectionOrigin)origin;
			if (value == CollectionOrigin.NexusMods) return CollectionIdentity.FromNexus(stableId);
			if (value == CollectionOrigin.Local) return CollectionIdentity.FromLocal(ParseGuid(stableId, "local Collection"));
			throw new InvalidDataException("Unsupported Collection origin in verify/repair review.");
		}

		private static CollectionMemberKey ReadMemberKey(int kind, string value)
		{
			switch ((CollectionMemberKeyKind)kind)
			{
				case CollectionMemberKeyKind.ProviderStable: return CollectionMemberKey.FromProvider(value);
				case CollectionMemberKeyKind.Local: return CollectionMemberKey.FromLocal(ParseGuid(value, "local member"));
				case CollectionMemberKeyKind.ValidatedMatch: return CollectionMemberKey.FromValidatedMatch(value);
				default: throw new InvalidDataException("Unsupported member-key kind in verify/repair review.");
			}
		}

		private static CollectionRequirementState ReadState(StateDto dto)
		{
			if (dto == null) return null;
			CollectionRequirementStateKind kind = (CollectionRequirementStateKind)dto.Kind;
			if (kind == CollectionRequirementStateKind.Absent) return CollectionRequirementState.Absent();
			if (kind == CollectionRequirementStateKind.Present) return CollectionRequirementState.Present(dto.Format, dto.Fingerprint);
			throw new InvalidDataException("Unsupported requirement state in verify/repair review.");
		}

		private sealed class IntentDto
		{
			public string AssociationId; public int AssociationState; public RevisionDto Revision; public string Target;
			public string StateFormat; public string StateValue; public List<FindingDto> Repairs; public List<RecipeDto> Recipes; public string ReviewFingerprint;
		}
		private sealed class RevisionDto
		{
			public int Origin; public string Collection; public string Revision; public long? Number;
			public static RevisionDto From(CollectionRevisionIdentity value) { return new RevisionDto { Origin=(int)value.Collection.Origin, Collection=value.Collection.StableId, Revision=value.StableRevisionId, Number=value.NexusRevisionNumber }; }
			public CollectionRevisionIdentity ToRevision()
			{
				CollectionIdentity collection = ReadCollection(Origin, Collection);
				if (collection.Origin == CollectionOrigin.NexusMods)
				{
					if (!Number.HasValue) throw new InvalidDataException("A Nexus verify/repair review requires its concrete revision number.");
					return CollectionRevisionIdentity.FromNexus(collection, Revision, Number.Value);
				}
				if (Number.HasValue) throw new InvalidDataException("A Local verify/repair review cannot carry a Nexus revision number.");
				return CollectionRevisionIdentity.FromLocal(collection, ParseGuid(Revision, "local revision"));
			}
		}
		private sealed class FindingDto
		{
			public bool HasMember; public int MemberKind; public string Member; public int Aspect; public string Subject; public int Kind; public StateDto Expected; public StateDto Observed;
			public static FindingDto From(CollectionVerifyRepairReviewedFinding value) { return new FindingDto { HasMember=value.MemberKey != null, MemberKind=value.MemberKey == null ? 0 : (int)value.MemberKey.Kind, Member=value.MemberKey == null ? null : value.MemberKey.Value, Aspect=(int)value.Aspect, Subject=value.SubjectKey, Kind=(int)value.Kind, Expected=StateDto.From(value.ExpectedState), Observed=StateDto.From(value.ObservedState) }; }
			public CollectionVerifyRepairReviewedFinding ToFinding() { return new CollectionVerifyRepairReviewedFinding(HasMember ? ReadMemberKey(MemberKind, Member) : null, (CollectionRequirementAspect)Aspect, Subject, (CollectionVerifyRepairFindingKind)Kind, ReadState(Expected), ReadState(Observed)); }
		}
		private sealed class RecipeDto
		{
			public int MemberKind; public string Member; public string Provider; public string Prepared; public string Executable; public int Method; public int Root;
			public static RecipeDto From(CollectionVerifyRepairPreparedRecipeReview value) { return new RecipeDto { MemberKind=(int)value.MemberKey.Kind, Member=value.MemberKey.Value, Provider=value.ProviderRecipeFingerprint, Prepared=value.PreparedNativeFingerprint, Executable=value.ExecutableDescriptorFingerprint, Method=(int)value.InstallMethod, Root=(int)value.InstallRoot }; }
			public CollectionVerifyRepairPreparedRecipeReview ToRecipe() { return new CollectionVerifyRepairPreparedRecipeReview(ReadMemberKey(MemberKind, Member), Provider, Prepared, Executable, (ModInstallMethod)Method, (ModInstallRoot)Root); }
		}
		private sealed class StateDto
		{
			public int Kind; public string Format; public string Fingerprint;
			public static StateDto From(CollectionRequirementState value) { return value == null ? null : new StateDto { Kind=(int)value.Kind, Format=value.FormatVersion, Fingerprint=value.Fingerprint }; }
		}
	}
}
