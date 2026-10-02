using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	public enum CollectionReplacementNativeDecision
	{
		Unknown = 0,
		Retain = 1,
		Remove = 2,
		Protected = 3
	}

	public enum CollectionReplacementAssociationDecision
	{
		Unknown = 0,
		Preserve = 1,
		TransitionOut = 2
	}

	public enum CollectionReplacementRecoveryInputKind
	{
		Unknown = 0,
		NativeIdentity = 1,
		ReconstructionPayload = 2,
		ScriptedReplay = 3,
		NativeEffects = 4,
		ProfileProtection = 5
	}

	public enum CollectionReplacementOptionalBackupStatus
	{
		Unknown = 0,
		NotRequested = 1,
		Completed = 2
	}

	public enum CollectionReplacementStartupDisposition
	{
		Unknown = 0,
		PreparationIncomplete = 1,
		PreparedBeforeNativeBoundary = 2,
		RecoveryRequired = 3,
		InvalidPersistedState = 4,
		OutgoingRemovalVerified = 5,
		PhaseAmendmentReviewRequired = 6,
		IncomingNativeWorkReady = 7,
		PhaseAmendmentRevalidationRequired = 8,
		IncomingNativeWorkVerified = 9
	}

	public sealed class CollectionReplacementNativeApproval
	{
		public CollectionReplacementNativeApproval(string nativeModKey, CollectionReplacementNativeDecision decision)
		{
			NativeModKey = CollectionIdentityValidation.RequireOpaqueToken(nativeModKey, nameof(nativeModKey));
			if (!Enum.IsDefined(typeof(CollectionReplacementNativeDecision), decision) || decision == CollectionReplacementNativeDecision.Unknown)
				throw new ArgumentOutOfRangeException(nameof(decision));
			Decision = decision;
		}
		public string NativeModKey { get; }
		public CollectionReplacementNativeDecision Decision { get; }
	}

	public sealed class CollectionReplacementAssociationApproval
	{
		public CollectionReplacementAssociationApproval(Guid associationId, CollectionReplacementAssociationDecision decision)
		{
			if (associationId == Guid.Empty) throw new ArgumentException("A replacement association approval requires a non-empty identity.", nameof(associationId));
			if (!Enum.IsDefined(typeof(CollectionReplacementAssociationDecision), decision) || decision == CollectionReplacementAssociationDecision.Unknown)
				throw new ArgumentOutOfRangeException(nameof(decision));
			AssociationId = associationId;
			Decision = decision;
		}
		public Guid AssociationId { get; }
		public CollectionReplacementAssociationDecision Decision { get; }
	}

	public sealed class CollectionReplacementFileWinnerApproval
	{
		public CollectionReplacementFileWinnerApproval(ModDeploymentTarget target, CollectionMemberKey winnerMemberKey)
		{
			Target = target ?? throw new ArgumentNullException(nameof(target));
			WinnerMemberKey = winnerMemberKey ?? throw new ArgumentNullException(nameof(winnerMemberKey));
		}

		public ModDeploymentTarget Target { get; }
		public CollectionMemberKey WinnerMemberKey { get; }
	}

	public sealed class CollectionReplacementPreparedRecipeApproval
	{
		private const string EffectFingerprintFormat = "nmm-ce.collections.replacement-effect-preview/1";

		public CollectionReplacementPreparedRecipeApproval(CollectionMemberKey memberKey, string providerRecipeFingerprint,
			string preparedNativeFingerprint, string effectFingerprint, ModInstallMethod method, ModInstallRoot root, string adapterId, int adapterVersion)
		{
			MemberKey = memberKey ?? throw new ArgumentNullException(nameof(memberKey));
			ProviderRecipeFingerprint = CollectionIdentityValidation.RequireOpaqueToken(providerRecipeFingerprint, nameof(providerRecipeFingerprint));
			PreparedNativeFingerprint = CollectionIdentityValidation.RequireOpaqueToken(preparedNativeFingerprint, nameof(preparedNativeFingerprint));
			EffectFingerprint = CollectionIdentityValidation.RequireOpaqueToken(effectFingerprint, nameof(effectFingerprint));
			if (!Enum.IsDefined(typeof(ModInstallMethod), method)) throw new ArgumentOutOfRangeException(nameof(method));
			if (!Enum.IsDefined(typeof(ModInstallRoot), root)) throw new ArgumentOutOfRangeException(nameof(root));
			AdapterId = CollectionIdentityValidation.RequireOpaqueToken(adapterId, nameof(adapterId));
			if (adapterVersion <= 0) throw new ArgumentOutOfRangeException(nameof(adapterVersion));
			Method = method;
			Root = root;
			AdapterVersion = adapterVersion;
		}
		public CollectionMemberKey MemberKey { get; }
		public string ProviderRecipeFingerprint { get; }
		public string PreparedNativeFingerprint { get; }
		public string EffectFingerprint { get; }
		public ModInstallMethod Method { get; }
		public ModInstallRoot Root { get; }
		public string AdapterId { get; }
		public int AdapterVersion { get; }

		/// <summary>Creates the canonical C8 replacement-review evidence for one already prepared native recipe.</summary>
		public static CollectionReplacementPreparedRecipeApproval FromPreparedRecipe(PreparedCollectionNativeRecipe recipe)
		{
			if (recipe == null) throw new ArgumentNullException(nameof(recipe));
			return new CollectionReplacementPreparedRecipeApproval(recipe.Member.MemberKey, recipe.ProviderRecipeIdentity.Fingerprint,
				recipe.PreparedNativeIdentity.Fingerprint, ComputeEffectFingerprint(recipe.EffectPreview),
				recipe.InstallContext.Method, recipe.InstallContext.InstallRoot, recipe.AdapterId, recipe.AdapterVersion);
		}

		internal static string ComputeEffectFingerprint(CollectionMemberEffectPreview preview)
		{
			if (preview == null) throw new ArgumentNullException(nameof(preview));
			using (var stream = new MemoryStream())
			using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), true))
			{
				writer.Write(EffectFingerprintFormat);
				writer.Write((int)preview.MemberKey.Kind);
				writer.Write(preview.MemberKey.Value);
				writer.Write(preview.RecipeIdentity.Fingerprint);
				writer.Write((int)preview.InstallMethod);
				writer.Write((int)preview.InstallRoot);
				writer.Write(preview.Files.Count);
				foreach (CollectionPlannedFileEffect file in preview.Files.OrderBy(x => (int)x.Target.Root).ThenBy(x => x.Target.RelativePath, StringComparer.OrdinalIgnoreCase))
				{
					writer.Write((int)file.Target.Root);
					writer.Write(file.Target.RelativePath);
				}
				writer.Write(preview.IniEdits.Count);
				foreach (CollectionPlannedIniEffect edit in preview.IniEdits.OrderBy(x => x.Key.ToString(), StringComparer.OrdinalIgnoreCase))
				{
					writer.Write(edit.Key.File); writer.Write(edit.Key.Section); writer.Write(edit.Key.Key);
					writer.Write(edit.Value != null); if (edit.Value != null) writer.Write(edit.Value);
				}
				writer.Write(preview.GameValues.Count);
				foreach (CollectionPlannedGameValueEffect value in preview.GameValues.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
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
					writer.Write(plugin.Active.HasValue); if (plugin.Active.HasValue) writer.Write(plugin.Active.Value);
					writer.Write(plugin.AbsoluteIndex.HasValue); if (plugin.AbsoluteIndex.HasValue) writer.Write(plugin.AbsoluteIndex.Value);
					writer.Write(plugin.PluginPaths.Count); foreach (string path in plugin.PluginPaths) writer.Write(path);
				}
				writer.Write(preview.Issues.Count);
				foreach (CollectionEffectPreviewIssue issue in preview.Issues.OrderBy(x => (int)x.Kind).ThenBy(x => x.OperationType, StringComparer.Ordinal).ThenBy(x => x.Message, StringComparer.Ordinal))
				{
					writer.Write((int)issue.Kind); writer.Write(issue.OperationType); writer.Write(issue.Message);
				}
				writer.Flush();
				using (SHA256 sha = SHA256.Create())
					return "sha256:" + String.Concat(sha.ComputeHash(stream.ToArray()).Select(x => x.ToString("x2")));
			}
		}
	}

	public sealed class CollectionReplacementProfileProtectionSnapshot
	{
		private CollectionReplacementProfileProtectionSnapshot(bool hadCurrentProfile, string profileId, string profileName, string contentFingerprint)
		{
			HadCurrentProfile = hadCurrentProfile;
			ProfileId = profileId;
			ProfileName = profileName ?? String.Empty;
			ContentFingerprint = contentFingerprint;
			if (hadCurrentProfile)
			{
				ProfileId = CollectionIdentityValidation.RequireOpaqueToken(profileId, nameof(profileId));
				ContentFingerprint = CollectionIdentityValidation.RequireOpaqueToken(contentFingerprint, nameof(contentFingerprint));
			}
			else if (!String.IsNullOrEmpty(profileId) || !String.IsNullOrEmpty(contentFingerprint))
				throw new ArgumentException("A no-profile protection snapshot cannot contain profile identity or content state.");
		}
		public bool HadCurrentProfile { get; }
		public string ProfileId { get; private set; }
		public string ProfileName { get; }
		public string ContentFingerprint { get; private set; }
		public static CollectionReplacementProfileProtectionSnapshot NoCurrentProfile()
		{
			return new CollectionReplacementProfileProtectionSnapshot(false, null, null, null);
		}
		public static CollectionReplacementProfileProtectionSnapshot Preserve(string profileId, string profileName, string contentFingerprint)
		{
			return new CollectionReplacementProfileProtectionSnapshot(true, profileId, profileName, contentFingerprint);
		}
	}

	public sealed class CollectionReplacementRecoveryInput
	{
		public CollectionReplacementRecoveryInput(CollectionReplacementRecoveryInputKind kind, string subjectKey, string role, string artifactId)
		{
			if (!Enum.IsDefined(typeof(CollectionReplacementRecoveryInputKind), kind) || kind == CollectionReplacementRecoveryInputKind.Unknown)
				throw new ArgumentOutOfRangeException(nameof(kind));
			Kind = kind;
			SubjectKey = CollectionIdentityValidation.RequireOpaqueToken(subjectKey, nameof(subjectKey));
			Role = CollectionIdentityValidation.RequireOpaqueToken(role, nameof(role));
			ArtifactId = CollectionIdentityValidation.RequireOpaqueToken(artifactId, nameof(artifactId));
		}
		public CollectionReplacementRecoveryInputKind Kind { get; }
		public string SubjectKey { get; }
		public string Role { get; }
		public string ArtifactId { get; }
	}

	public sealed class CollectionReplacementOptionalBackupResult
	{
		private CollectionReplacementOptionalBackupResult(CollectionReplacementOptionalBackupStatus status, string captureIdentity)
		{
			if (!Enum.IsDefined(typeof(CollectionReplacementOptionalBackupStatus), status) || status == CollectionReplacementOptionalBackupStatus.Unknown)
				throw new ArgumentOutOfRangeException(nameof(status));
			Status = status;
			CaptureIdentity = captureIdentity;
			if (status == CollectionReplacementOptionalBackupStatus.Completed)
				CaptureIdentity = CollectionIdentityValidation.RequireOpaqueToken(captureIdentity, nameof(captureIdentity));
			else if (!String.IsNullOrEmpty(captureIdentity))
				throw new ArgumentException("A non-completed optional backup cannot carry a capture identity.", nameof(captureIdentity));
		}
		public CollectionReplacementOptionalBackupStatus Status { get; }
		public string CaptureIdentity { get; private set; }
		public static CollectionReplacementOptionalBackupResult NotRequested() { return new CollectionReplacementOptionalBackupResult(CollectionReplacementOptionalBackupStatus.NotRequested, null); }
		public static CollectionReplacementOptionalBackupResult Completed(string captureIdentity) { return new CollectionReplacementOptionalBackupResult(CollectionReplacementOptionalBackupStatus.Completed, captureIdentity); }
	}

	public sealed class CollectionReplacementReviewedIntent
	{
		private readonly ReadOnlyCollection<CollectionReplacementNativeApproval> _nativeApprovals;
		private readonly ReadOnlyCollection<CollectionReplacementAssociationApproval> _associationApprovals;
		private readonly ReadOnlyCollection<CollectionReplacementPreparedRecipeApproval> _preparedRecipes;
		private readonly ReadOnlyCollection<CollectionReplacementFileWinnerApproval> _fileWinnerApprovals;

		internal CollectionReplacementReviewedIntent(CollectionPlanIdentity planIdentity, CollectionRevisionIdentity revision,
			CollectionTargetIdentity target, CollectionCurrentStateFingerprint nativeStateFingerprint,
			CollectionCurrentStateFingerprint decisionInputFingerprint, string diffFingerprint, string environmentFingerprint, CollectionReplacementBackupChoice backupChoice,
			bool preservesUnknownOrUnmanagedContent, CollectionReplacementProfileProtectionSnapshot profileProtection,
			IEnumerable<CollectionReplacementNativeApproval> nativeApprovals,
			IEnumerable<CollectionReplacementAssociationApproval> associationApprovals,
			IEnumerable<CollectionReplacementPreparedRecipeApproval> preparedRecipes,
			IEnumerable<CollectionReplacementFileWinnerApproval> fileWinnerApprovals)
		{
			PlanIdentity = planIdentity ?? throw new ArgumentNullException(nameof(planIdentity));
			Revision = revision ?? throw new ArgumentNullException(nameof(revision));
			Target = target ?? throw new ArgumentNullException(nameof(target));
			NativeStateFingerprint = nativeStateFingerprint ?? throw new ArgumentNullException(nameof(nativeStateFingerprint));
			DecisionInputFingerprint = decisionInputFingerprint ?? throw new ArgumentNullException(nameof(decisionInputFingerprint));
			DiffFingerprint = CollectionIdentityValidation.RequireOpaqueToken(diffFingerprint, nameof(diffFingerprint));
			EnvironmentFingerprint = CollectionIdentityValidation.RequireOpaqueToken(environmentFingerprint, nameof(environmentFingerprint));
			if (backupChoice != CollectionReplacementBackupChoice.CreateLocalCollection && backupChoice != CollectionReplacementBackupChoice.ContinueWithoutLocalCollection)
				throw new ArgumentException("A replacement review requires an explicit persistent-backup decision.", nameof(backupChoice));
			if (!preservesUnknownOrUnmanagedContent) throw new ArgumentException("Replacement review cannot authorize blanket deletion of unknown/unmanaged content.", nameof(preservesUnknownOrUnmanagedContent));
			BackupChoice = backupChoice;
			PreservesUnknownOrUnmanagedContent = true;
			ProfileProtection = profileProtection ?? throw new ArgumentNullException(nameof(profileProtection));
			_nativeApprovals = CopyUnique(nativeApprovals, nameof(nativeApprovals), x => x.NativeModKey);
			_associationApprovals = CopyUnique(associationApprovals, nameof(associationApprovals), x => x.AssociationId.ToString("D"));
			_preparedRecipes = CopyUnique(preparedRecipes, nameof(preparedRecipes), x => x.MemberKey.ToString());
			_fileWinnerApprovals = CopyUnique(fileWinnerApprovals, nameof(fileWinnerApprovals), x => x.Target.ToString());
		}

		public CollectionPlanIdentity PlanIdentity { get; }
		public CollectionRevisionIdentity Revision { get; }
		public CollectionTargetIdentity Target { get; }
		public CollectionCurrentStateFingerprint NativeStateFingerprint { get; }
		public CollectionCurrentStateFingerprint DecisionInputFingerprint { get; }
		public string DiffFingerprint { get; }
		public string EnvironmentFingerprint { get; }
		public CollectionReplacementBackupChoice BackupChoice { get; }
		public bool PreservesUnknownOrUnmanagedContent { get; }
		public CollectionReplacementProfileProtectionSnapshot ProfileProtection { get; }
		public ReadOnlyCollection<CollectionReplacementNativeApproval> NativeApprovals { get { return _nativeApprovals; } }
		public ReadOnlyCollection<CollectionReplacementAssociationApproval> AssociationApprovals { get { return _associationApprovals; } }
		public ReadOnlyCollection<CollectionReplacementPreparedRecipeApproval> PreparedRecipes { get { return _preparedRecipes; } }
		public ReadOnlyCollection<CollectionReplacementFileWinnerApproval> FileWinnerApprovals { get { return _fileWinnerApprovals; } }

		public void ValidateReviewEvidence(CollectionReplacementDiffPlan diff, CollectionReplacementEnvironmentProjection environment)
		{
			if (diff == null) throw new ArgumentNullException(nameof(diff));
			if (environment == null) throw new ArgumentNullException(nameof(environment));
			if (!PlanIdentity.Equals(diff.PlanIdentity) || !Target.Equals(diff.Target) ||
				!StringComparer.Ordinal.Equals(DiffFingerprint, ComputeDiffFingerprint(diff)) ||
				!StringComparer.Ordinal.Equals(EnvironmentFingerprint, ComputeEnvironmentFingerprint(environment)))
				throw new InvalidOperationException("The replacement diff/environment no longer matches the exact approved review evidence.");
		}

		public void ValidateCurrentSetup(CollectionReplacementCurrentSetupSnapshot current)
		{
			if (current == null) throw new ArgumentNullException(nameof(current));
			if (!current.NativeState.Target.Equals(Target) || !current.NativeState.Fingerprint.Equals(NativeStateFingerprint) ||
				!current.DecisionFingerprint.Equals(DecisionInputFingerprint))
				throw new InvalidOperationException("The reviewed replacement approval is stale because native state, customization, drift or standalone provenance changed.");
		}

		public static CollectionReplacementReviewedIntent Create(CollectionReplacementDiffPlan diff,
			CollectionReplacementEnvironmentProjection environment, IEnumerable<CollectionReplacementNativeApproval> nativeApprovals,
			IEnumerable<CollectionReplacementAssociationApproval> associationApprovals,
			IEnumerable<CollectionReplacementPreparedRecipeApproval> preparedRecipes,
			IEnumerable<CollectionReplacementFileWinnerApproval> fileWinnerApprovals,
			CollectionReplacementProfileProtectionSnapshot profileProtection)
		{
			if (diff == null) throw new ArgumentNullException(nameof(diff));
			if (environment == null) throw new ArgumentNullException(nameof(environment));
			if (!ReferenceEquals(environment.Diff, diff)) throw new ArgumentException("Replacement environment must belong to the exact diff being reviewed.", nameof(environment));
			if (diff.IncomingPlan.Policy.Kind != CollectionExecutionPolicyKind.ReplaceCurrentManagedSetup)
				throw new ArgumentException("Only replacement policy can produce a replacement reviewed intent.", nameof(diff));
			if (!diff.IncomingPlan.Policy.HasResolvedReplacementBackupDecision)
				throw new InvalidOperationException("Replacement backup choice must be resolved before exact review can be persisted.");
			if (!diff.CurrentStateMatchesPlan || diff.HasBlockers || !environment.IsReadyForSupportedConditions)
				throw new InvalidOperationException("A stale, blocked or unprovable replacement plan cannot enter exact review.");

			List<CollectionReplacementNativeApproval> native = (nativeApprovals ?? throw new ArgumentNullException(nameof(nativeApprovals))).ToList();
			var nativeByKey = native.ToDictionary(x => x.NativeModKey, StringComparer.Ordinal);
			foreach (CollectionReplacementNativeModImpact impact in diff.NativeMods)
			{
				CollectionReplacementNativeApproval approval;
				if (!nativeByKey.TryGetValue(impact.NativeMod.Identity.NativeModKey, out approval))
					throw new InvalidOperationException("Every current native mod must have one explicit reviewed replacement decision.");
				if (impact.RemovalDecision == CollectionReplacementRemovalDecision.Blocked && approval.Decision != CollectionReplacementNativeDecision.Protected)
					throw new InvalidOperationException("A blocked native mod cannot be approved for replacement removal.");
				if (impact.RemovalDecision == CollectionReplacementRemovalDecision.Protected && approval.Decision != CollectionReplacementNativeDecision.Protected)
					throw new InvalidOperationException("A protected/shared native mod must remain protected in replacement review.");
				if (approval.Decision == CollectionReplacementNativeDecision.Remove &&
					impact.RemovalDecision != CollectionReplacementRemovalDecision.EligibleForReviewedRemoval &&
					impact.RemovalDecision != CollectionReplacementRemovalDecision.RequiresExplicitReview)
					throw new InvalidOperationException("Replacement review cannot remove a native mod that C8.1 did not classify as reviewable for removal.");
			}
			if (nativeByKey.Count != diff.NativeMods.Count) throw new InvalidOperationException("Replacement review contains native decisions outside the exact diff.");

			List<CollectionReplacementAssociationApproval> associations = (associationApprovals ?? throw new ArgumentNullException(nameof(associationApprovals))).ToList();
			var associationById = associations.ToDictionary(x => x.AssociationId);
			foreach (CollectionReplacementAssociationImpact impact in diff.Associations)
			{
				CollectionReplacementAssociationApproval approval;
				if (!associationById.TryGetValue(impact.Association.AssociationId, out approval))
					throw new InvalidOperationException("Every outgoing/surviving association must have one explicit reviewed transition decision.");
				if (impact.Disposition == CollectionReplacementAssociationDisposition.SurvivesCompatible && approval.Decision != CollectionReplacementAssociationDecision.Preserve)
					throw new InvalidOperationException("A compatible surviving association cannot be transitioned out implicitly.");
				if (impact.Disposition == CollectionReplacementAssociationDisposition.Blocked)
					throw new InvalidOperationException("A blocked association cannot enter exact replacement review.");
			}
			if (associationById.Count != diff.Associations.Count) throw new InvalidOperationException("Replacement review contains association decisions outside the exact diff.");

			List<CollectionReplacementPreparedRecipeApproval> recipes = (preparedRecipes ?? throw new ArgumentNullException(nameof(preparedRecipes))).ToList();
			var recipeByMember = recipes.ToDictionary(x => x.MemberKey);
			foreach (CollectionReplacementIncomingMemberImpact impact in diff.IncomingMembers.Where(x => x.Disposition == CollectionReplacementDiffDisposition.IncomingOnly || x.Disposition == CollectionReplacementDiffDisposition.ReinstallOrChange))
			{
				CollectionReplacementPreparedRecipeApproval recipe;
				if (!recipeByMember.TryGetValue(impact.Member.MemberKey, out recipe) ||
					!StringComparer.Ordinal.Equals(recipe.ProviderRecipeFingerprint, impact.Member.RecipeIdentity.Fingerprint))
					throw new InvalidOperationException("Every incoming install/reinstall must be bound to one exact prepared-native recipe identity before approval.");
			}
			if (recipes.Any(x => diff.IncomingPlan.SelectedMembers.All(m => !m.MemberKey.Equals(x.MemberKey))))
				throw new InvalidOperationException("Replacement review contains a prepared recipe outside the selected incoming closure.");

			List<CollectionReplacementFileWinnerApproval> winners = (fileWinnerApprovals ?? throw new ArgumentNullException(nameof(fileWinnerApprovals))).ToList();
			var selectedKeys = new HashSet<CollectionMemberKey>(diff.IncomingPlan.SelectedMembers.Select(x => x.MemberKey));
			if (winners.Any(x => !selectedKeys.Contains(x.WinnerMemberKey)))
				throw new InvalidOperationException("Replacement review contains a file winner outside the selected incoming closure.");

			return new CollectionReplacementReviewedIntent(diff.PlanIdentity, diff.IncomingRevision, diff.Target,
				diff.NativeStateFingerprint, diff.DecisionInputFingerprint, ComputeDiffFingerprint(diff), ComputeEnvironmentFingerprint(environment),
				diff.IncomingPlan.Policy.ReplacementBackupChoice,
				diff.PreservesUnknownOrUnmanagedContent, profileProtection, native, associations, recipes, winners);
		}

		private static string ComputeDiffFingerprint(CollectionReplacementDiffPlan diff)
		{
			var lines = new List<string>();
			foreach (CollectionReplacementIncomingMemberImpact x in diff.IncomingMembers) lines.Add("I|" + x.Member.MemberKey + "|" + (int)x.Disposition + "|" + (int)x.MatchDisposition);
			foreach (CollectionReplacementNativeModImpact x in diff.NativeMods) lines.Add("N|" + x.NativeMod.Identity.NativeModKey + "|" + (int)x.Disposition + "|" + (int)x.RemovalDecision);
			foreach (CollectionReplacementAssociationImpact x in diff.Associations) lines.Add("A|" + x.Association.AssociationId.ToString("D") + "|" + (int)x.Disposition);
			foreach (CollectionReplacementEffectImpact x in diff.Effects) lines.Add("E|" + (int)x.Kind + "|" + x.ResourceKey + "|" + (int)x.Disposition + "|" + (x.CurrentOwnerKey ?? "") + "|" + (x.ProjectedOwnerKey ?? ""));
			return HashLines("replacement-diff-review-v1", lines);
		}

		internal static string ComputeEnvironmentFingerprint(CollectionReplacementEnvironmentProjection environment)
		{
			if (environment == null) throw new ArgumentNullException(nameof(environment));
			var lines = new List<string>();
			lines.Add("C|PluginCoverage|" + (int)environment.PluginCoverage);
			foreach (var x in environment.Files.OrderBy(x => x.Key.Root).ThenBy(x => x.Key.RelativePath, StringComparer.OrdinalIgnoreCase)) lines.Add("F|" + x.Key + "|" + (int)x.Value.Knowledge + "|" + x.Value.Visible + "|" + (x.Value.OwnerKey ?? ""));
			foreach (var x in environment.IniValues.OrderBy(x => x.Key.ToString(), StringComparer.OrdinalIgnoreCase)) lines.Add("C|" + x.Key + "|" + (int)x.Value.Knowledge + "|" + (x.Value.Value ?? "") + "|" + (x.Value.OwnerKey ?? ""));
			foreach (var x in environment.Plugins.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)) lines.Add("P|" + x.Key + "|" + (x.Value.Registered.HasValue ? x.Value.Registered.Value.ToString() : "?") + "|" + (x.Value.Active.HasValue ? x.Value.Active.Value.ToString() : "?"));
			foreach (string issue in environment.Issues.OrderBy(x => x, StringComparer.Ordinal)) lines.Add("X|" + issue);
			return HashLines("replacement-environment-review-v1", lines);
		}

		private static string HashLines(string version, IEnumerable<string> lines)
		{
			string text = version + "\n" + String.Join("\n", lines);
			using (SHA256 sha = SHA256.Create())
				return "sha256:" + String.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(text)).Select(x => x.ToString("x2")));
		}

		private static ReadOnlyCollection<T> CopyUnique<T>(IEnumerable<T> values, string parameterName, Func<T, string> key) where T : class
		{
			List<T> copied = (values ?? throw new ArgumentNullException(parameterName)).ToList();
			if (copied.Any(x => x == null) || copied.Select(key).Distinct(StringComparer.Ordinal).Count() != copied.Count)
				throw new ArgumentException("Reviewed replacement collections cannot contain null or duplicate identities.", parameterName);
			return new ReadOnlyCollection<T>(copied.OrderBy(key, StringComparer.Ordinal).ToList());
		}
	}

	public static class CollectionReplacementReviewedIntentCodec
	{
		public const string PayloadFormat = "nmm-ce.collections.replacement-reviewed-intent/2";
		public const string LegacyPayloadFormat = "nmm-ce.collections.replacement-reviewed-intent/1";

		public static bool IsSupportedPayloadFormat(string payloadFormat)
		{
			return StringComparer.Ordinal.Equals(payloadFormat, PayloadFormat) || StringComparer.Ordinal.Equals(payloadFormat, LegacyPayloadFormat);
		}
		private sealed class Dto
		{
			public string PlanId; public int PlanVersion; public int Origin; public string CollectionId; public string RevisionId; public long? NexusRevision;
			public string Target; public string NativeFormat; public string NativeFingerprint; public string DecisionFormat; public string DecisionFingerprint; public string DiffFingerprint; public string EnvironmentFingerprint;
			public int BackupChoice; public bool PreserveUnknown; public ProfileDto Profile; public List<NativeDto> Native; public List<AssociationDto> Associations; public List<RecipeDto> Recipes; public List<WinnerDto> Winners;
		}
		private sealed class ProfileDto { public bool Had; public string Id; public string Name; public string Fingerprint; }
		private sealed class NativeDto { public string Key; public int Decision; }
		private sealed class AssociationDto { public string Id; public int Decision; }
		private sealed class RecipeDto { public int KeyKind; public string KeyValue; public string Provider; public string Prepared; public string Effects; public int Method; public int Root; public string Adapter; public int Version; }
		private sealed class WinnerDto { public int Root; public string Path; public int KeyKind; public string KeyValue; }

		public static byte[] Serialize(CollectionReplacementReviewedIntent intent)
		{
			if (intent == null) throw new ArgumentNullException(nameof(intent));
			Dto dto = ToDto(intent);
			return new UTF8Encoding(false, true).GetBytes(JsonConvert.SerializeObject(dto, Formatting.None));
		}

		public static CollectionReplacementReviewedIntent Deserialize(byte[] payload)
		{
			if (payload == null || payload.Length == 0) throw new InvalidDataException("Replacement reviewed intent payload is empty.");
			try
			{
				Dto dto = JsonConvert.DeserializeObject<Dto>(new UTF8Encoding(false, true).GetString(payload));
				if (dto == null) throw new InvalidDataException("Replacement reviewed intent payload is invalid.");
				CollectionIdentity collection = ReadCollection(dto.Origin, dto.CollectionId);
				CollectionRevisionIdentity revision = collection.Origin == CollectionOrigin.NexusMods
					? CollectionRevisionIdentity.FromNexus(collection, dto.RevisionId, dto.NexusRevision ?? 0)
					: CollectionRevisionIdentity.FromLocal(collection, Guid.Parse(dto.RevisionId));
				CollectionReplacementProfileProtectionSnapshot profile = dto.Profile != null && dto.Profile.Had
					? CollectionReplacementProfileProtectionSnapshot.Preserve(dto.Profile.Id, dto.Profile.Name, dto.Profile.Fingerprint)
					: CollectionReplacementProfileProtectionSnapshot.NoCurrentProfile();
				return new CollectionReplacementReviewedIntent(
					CollectionPlanIdentity.From(Guid.Parse(dto.PlanId), dto.PlanVersion), revision, CollectionTargetIdentity.FromFingerprint(dto.Target),
					new CollectionCurrentStateFingerprint(dto.NativeFormat, dto.NativeFingerprint), new CollectionCurrentStateFingerprint(dto.DecisionFormat, dto.DecisionFingerprint),
					dto.DiffFingerprint, dto.EnvironmentFingerprint, (CollectionReplacementBackupChoice)dto.BackupChoice, dto.PreserveUnknown, profile,
					(dto.Native ?? new List<NativeDto>()).Select(x => new CollectionReplacementNativeApproval(x.Key, (CollectionReplacementNativeDecision)x.Decision)),
					(dto.Associations ?? new List<AssociationDto>()).Select(x => new CollectionReplacementAssociationApproval(Guid.Parse(x.Id), (CollectionReplacementAssociationDecision)x.Decision)),
					(dto.Recipes ?? new List<RecipeDto>()).Select(x => new CollectionReplacementPreparedRecipeApproval(ReadMemberKey(x.KeyKind, x.KeyValue), x.Provider, x.Prepared, x.Effects, (ModInstallMethod)x.Method, (ModInstallRoot)x.Root, x.Adapter, x.Version)),
					(dto.Winners ?? new List<WinnerDto>()).Select(x => new CollectionReplacementFileWinnerApproval(ModDeploymentTargetResolver.FromCanonical((ModDeploymentRoot)x.Root, x.Path), ReadMemberKey(x.KeyKind, x.KeyValue))));
			}
			catch (Exception ex) when (!(ex is InvalidDataException))
			{
				throw new InvalidDataException("Replacement reviewed intent payload is malformed or uses unsupported identities.", ex);
			}
		}

		private static Dto ToDto(CollectionReplacementReviewedIntent x)
		{
			return new Dto {
				PlanId=x.PlanIdentity.PlanId.ToString("D"), PlanVersion=x.PlanIdentity.Version, Origin=(int)x.Revision.Collection.Origin, CollectionId=x.Revision.Collection.StableId,
				RevisionId=x.Revision.StableRevisionId, NexusRevision=x.Revision.NexusRevisionNumber, Target=x.Target.Fingerprint,
				NativeFormat=x.NativeStateFingerprint.FormatVersion, NativeFingerprint=x.NativeStateFingerprint.Value,
				DecisionFormat=x.DecisionInputFingerprint.FormatVersion, DecisionFingerprint=x.DecisionInputFingerprint.Value, DiffFingerprint=x.DiffFingerprint, EnvironmentFingerprint=x.EnvironmentFingerprint,
				BackupChoice=(int)x.BackupChoice, PreserveUnknown=x.PreservesUnknownOrUnmanagedContent,
				Profile=new ProfileDto { Had=x.ProfileProtection.HadCurrentProfile, Id=x.ProfileProtection.ProfileId, Name=x.ProfileProtection.ProfileName, Fingerprint=x.ProfileProtection.ContentFingerprint },
				Native=x.NativeApprovals.Select(n => new NativeDto { Key=n.NativeModKey, Decision=(int)n.Decision }).ToList(),
				Associations=x.AssociationApprovals.Select(a => new AssociationDto { Id=a.AssociationId.ToString("D"), Decision=(int)a.Decision }).ToList(),
				Recipes=x.PreparedRecipes.Select(r => new RecipeDto { KeyKind=(int)r.MemberKey.Kind, KeyValue=r.MemberKey.Value, Provider=r.ProviderRecipeFingerprint, Prepared=r.PreparedNativeFingerprint, Effects=r.EffectFingerprint, Method=(int)r.Method, Root=(int)r.Root, Adapter=r.AdapterId, Version=r.AdapterVersion }).ToList(),
				Winners=x.FileWinnerApprovals.Select(w => new WinnerDto { Root=(int)w.Target.Root, Path=w.Target.RelativePath, KeyKind=(int)w.WinnerMemberKey.Kind, KeyValue=w.WinnerMemberKey.Value }).ToList()
			};
		}

		private static CollectionIdentity ReadCollection(int origin, string id)
		{
			if ((CollectionOrigin)origin == CollectionOrigin.NexusMods) return CollectionIdentity.FromNexus(id);
			if ((CollectionOrigin)origin == CollectionOrigin.Local) return CollectionIdentity.FromLocal(Guid.Parse(id));
			throw new InvalidDataException("Unsupported Collection origin in replacement reviewed intent.");
		}

		private static CollectionMemberKey ReadMemberKey(int kind, string value)
		{
			CollectionMemberKeyKind parsed = (CollectionMemberKeyKind)kind;
			switch (parsed)
			{
				case CollectionMemberKeyKind.ProviderStable: return CollectionMemberKey.FromProvider(value);
				case CollectionMemberKeyKind.ValidatedMatch: return CollectionMemberKey.FromValidatedMatch(value);
				case CollectionMemberKeyKind.Local: Guid localId; if (!Guid.TryParse(value, out localId)) throw new InvalidDataException("Invalid Local member key in replacement reviewed intent."); return CollectionMemberKey.FromLocal(localId);
				default: throw new InvalidDataException("Unsupported member-key kind in replacement reviewed intent.");
			}
		}
	}

	public sealed class CollectionReplacementRecoveryBoundary
	{
		private readonly ReadOnlyCollection<CollectionReplacementRecoveryInput> _inputs;
		internal CollectionReplacementRecoveryBoundary(CollectionPlanIdentity planIdentity, CollectionReplacementOptionalBackupResult optionalBackup,
			IEnumerable<CollectionReplacementRecoveryInput> inputs)
		{
			PlanIdentity = planIdentity ?? throw new ArgumentNullException(nameof(planIdentity));
			OptionalBackup = optionalBackup ?? throw new ArgumentNullException(nameof(optionalBackup));
			List<CollectionReplacementRecoveryInput> copied = (inputs ?? throw new ArgumentNullException(nameof(inputs))).ToList();
			if (copied.Any(x => x == null) || copied.Select(x => x.Role).Distinct(StringComparer.Ordinal).Count() != copied.Count)
				throw new ArgumentException("Replacement recovery preparation requires uniquely-role-bound operation recovery inputs.", nameof(inputs));
			_inputs = new ReadOnlyCollection<CollectionReplacementRecoveryInput>(copied.OrderBy(x => x.Role, StringComparer.Ordinal).ToList());
		}
		public CollectionPlanIdentity PlanIdentity { get; }
		public CollectionReplacementOptionalBackupResult OptionalBackup { get; }
		public ReadOnlyCollection<CollectionReplacementRecoveryInput> Inputs { get { return _inputs; } }
	}

	public sealed class CollectionReplacementOperationCoordinator
	{
		private const string RecoveryBoundaryRole = "replacement-recovery-boundary-v1";
		private const string RecoveryInputRolePrefix = "replacement-recovery-input-v1-";

		private sealed class RecoveryBoundaryDto
		{
			public string PlanId;
			public int PlanVersion;
			public int Backup;
			public string Capture;
			public List<RecoveryInputDto> Inputs;
		}

		private sealed class RecoveryInputDto
		{
			public int Kind;
			public string SubjectKey;
			public string Role;
			public string ArtifactId;
		}
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsResolvedPlanStore _planStore;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionsRetainedArtifactReferenceStore _referenceStore;

		public CollectionReplacementOperationCoordinator(CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore,
			CollectionsRetainedArtifactStore artifactStore, CollectionsRetainedArtifactReferenceStore referenceStore)
		{
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_planStore = planStore ?? throw new ArgumentNullException(nameof(planStore));
			_artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
			_referenceStore = referenceStore ?? throw new ArgumentNullException(nameof(referenceStore));
		}

		public CollectionOperation CreateReviewedOperation(CollectionReplacementDiffPlan diff, CollectionReplacementReviewedIntent intent)
		{
			if (diff == null) throw new ArgumentNullException(nameof(diff));
			if (intent == null) throw new ArgumentNullException(nameof(intent));
			if (!intent.PlanIdentity.Equals(diff.PlanIdentity) || !intent.Revision.Equals(diff.IncomingRevision) || !intent.Target.Equals(diff.Target))
				throw new ArgumentException("Replacement reviewed intent does not belong to the exact diff/plan.", nameof(intent));
			byte[] payload = CollectionReplacementReviewedIntentCodec.Serialize(intent);
			_planStore.SavePlan(diff.IncomingPlan, CollectionReplacementReviewedIntentCodec.PayloadFormat, payload);
			var operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(), CollectionOperationKind.ReplaceCurrentManagedSetup,
				diff.IncomingRevision.Collection, diff.Target, diff.IncomingRevision, diff.PlanIdentity, 1,
				CollectionOperationPhase.ReadyForReview, CollectionOperationResultState.Pending, new CollectionNativeChildOperation[0]);
			_operationStore.SaveOperation(operation);
			return operation;
		}

		/// <summary>Cancels an exact replacement review before any native mutation/recovery preparation has begun.</summary>
		public CollectionOperation CancelBeforeApply(CollectionOperationIdentity identity, CollectionPlanIdentity expectedPlan)
		{
			CollectionOperation operation = RequireOperation(identity, CollectionOperationPhase.ReadyForReview);
			RequireIntent(operation, expectedPlan);
			if (operation.HasCrossedNativeBoundary)
				throw new InvalidOperationException("Replacement cannot be cancelled as pre-apply after native mutation has begun.");
			var cancelled = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target, operation.Revision,
				operation.PlanIdentity, checked(operation.CheckpointSequence + 1), CollectionOperationPhase.Completed,
				CollectionOperationResultState.CancelledBeforeApply, operation.NativeChildren);
			_operationStore.SaveOperation(cancelled);
			return cancelled;
		}

		public CollectionOperation Approve(CollectionOperationIdentity identity, CollectionPlanIdentity expectedPlan,
			CollectionReplacementCurrentSetupSnapshot current)
		{
			CollectionOperation operation = RequireOperation(identity, CollectionOperationPhase.ReadyForReview);
			CollectionReplacementReviewedIntent intent = RequireIntent(operation, expectedPlan);
			intent.ValidateCurrentSetup(current);
			return Save(operation, CollectionOperationPhase.CapturingOptionalLocalBackup);
		}

		public CollectionOperation PrepareMandatoryRecovery(CollectionOperationIdentity identity, CollectionPlanIdentity expectedPlan,
			CollectionReplacementCurrentSetupSnapshot current, CollectionReplacementOptionalBackupResult optionalBackup,
			IEnumerable<CollectionReplacementRecoveryInput> recoveryInputs)
		{
			CollectionOperation operation = RequireOperation(identity, CollectionOperationPhase.CapturingOptionalLocalBackup);
			CollectionReplacementReviewedIntent intent = RequireIntent(operation, expectedPlan);
			intent.ValidateCurrentSetup(current);
			ValidateBackup(intent, optionalBackup);
			var boundary = new CollectionReplacementRecoveryBoundary(expectedPlan, optionalBackup, recoveryInputs);
			ValidateRecoveryCoverage(intent, boundary.Inputs);
			string ownerId = operation.Identity.OperationId.ToString("D");
			foreach (CollectionReplacementRecoveryInput input in boundary.Inputs)
			{
				CollectionsRetainedArtifact artifact = _artifactStore.GetArtifact(input.ArtifactId);
				if (artifact == null || !_artifactStore.VerifyArtifact(input.ArtifactId))
					throw new InvalidDataException("A mandatory replacement recovery input is missing or failed immutable-content verification: " + input.Role);
				_referenceStore.AcquireExclusiveRoleReference(input.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation, ownerId,
					RecoveryInputRolePrefix + input.Role);
			}
			byte[] boundaryPayload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new {
				PlanId=expectedPlan.PlanId.ToString("D"), PlanVersion=expectedPlan.Version, Backup=(int)optionalBackup.Status,
				Capture=optionalBackup.CaptureIdentity, Inputs=boundary.Inputs.Select(x => new { Kind=(int)x.Kind, x.SubjectKey, x.Role, x.ArtifactId }).ToArray()
			}, Formatting.None));
			CollectionsRetainedArtifact boundaryArtifact;
			using (var stream = new MemoryStream(boundaryPayload, false)) boundaryArtifact = _artifactStore.Publish(stream);
			_referenceStore.AcquireExclusiveRoleReference(boundaryArtifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation,
				ownerId, RecoveryBoundaryRole);
			return Save(operation, CollectionOperationPhase.ReadyToApply);
		}

		/// <summary>Loads the immutable exact C8.3 review for one replacement operation without reconstructing approval.</summary>
		public CollectionReplacementReviewedIntent GetReviewedIntent(CollectionOperationIdentity identity, CollectionPlanIdentity expectedPlan)
		{
			if (identity == null) throw new ArgumentNullException(nameof(identity));
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Kind != CollectionOperationKind.ReplaceCurrentManagedSetup || operation.IsTerminal)
				throw new InvalidOperationException("Replacement operation is not present as an active durable journal entry.");
			return RequireIntent(operation, expectedPlan);
		}

		/// <summary>Loads and integrity-checks the operation-owned mandatory C8.3 recovery boundary for C8.7.</summary>
		public CollectionReplacementRecoveryBoundary GetRecoveryBoundary(CollectionOperationIdentity identity, CollectionPlanIdentity expectedPlan)
		{
			if (identity == null) throw new ArgumentNullException(nameof(identity));
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Kind != CollectionOperationKind.ReplaceCurrentManagedSetup || operation.IsTerminal)
				throw new InvalidOperationException("Replacement operation is not present as an active durable journal entry.");
			CollectionReplacementReviewedIntent intent = RequireIntent(operation, expectedPlan);
			return LoadAndValidatePersistedRecoveryBoundary(operation, intent);
		}

		/// <summary>Crosses only the Collection journal boundary into C8.4 after revalidating exact review and mandatory recovery bytes.</summary>
		public CollectionOperation BeginOutgoingRemoval(CollectionOperationIdentity identity, CollectionPlanIdentity expectedPlan,
			CollectionReplacementCurrentSetupSnapshot current)
		{
			CollectionOperation operation = RequireOperation(identity, CollectionOperationPhase.ReadyToApply);
			CollectionReplacementReviewedIntent intent = RequireIntent(operation, expectedPlan);
			intent.ValidateCurrentSetup(current);
			LoadAndValidatePersistedRecoveryBoundary(operation, intent);
			return Save(operation, CollectionOperationPhase.RemovingOutgoingNativeChildren);
		}

		public CollectionReplacementStartupInspection InspectInterrupted(CollectionOperation operation)
		{
			if (operation == null) throw new ArgumentNullException(nameof(operation));
			if (operation.Kind != CollectionOperationKind.ReplaceCurrentManagedSetup)
				throw new ArgumentException("Startup replacement inspection only accepts replacement operations.", nameof(operation));
			try
			{
				CollectionReplacementReviewedIntent intent = RequireIntent(operation, operation.PlanIdentity);
				bool requiresRecoveryBoundary = !operation.IsTerminal && (operation.Phase == CollectionOperationPhase.ReadyToApply ||
					operation.Phase == CollectionOperationPhase.RemovingOutgoingNativeChildren ||
					operation.Phase == CollectionOperationPhase.PausedAtSafeBoundary ||
					operation.Phase == CollectionOperationPhase.OutgoingRemovalVerified ||
					operation.Phase == CollectionOperationPhase.AwaitingReplacementPhaseAmendment ||
					operation.Phase == CollectionOperationPhase.ReadyForIncomingNativeChildren ||
					operation.Phase == CollectionOperationPhase.ReplacementBarrierRevalidationRequired ||
					operation.Phase == CollectionOperationPhase.InstallingIncomingNativeChildren ||
					operation.Phase == CollectionOperationPhase.IncomingNativeChildrenVerified ||
					operation.Phase == CollectionOperationPhase.Recovering ||
					operation.Phase == CollectionOperationPhase.RecoveryRequired ||
					operation.HasCrossedNativeBoundary);
				bool hasBoundary = false;
				if (requiresRecoveryBoundary)
				{
					LoadAndValidatePersistedRecoveryBoundary(operation, intent);
					hasBoundary = true;
				}
				if (operation.Phase == CollectionOperationPhase.OutgoingRemovalVerified)
					return new CollectionReplacementStartupInspection(operation, CollectionReplacementStartupDisposition.OutgoingRemovalVerified, "Outgoing native removal is durably verified; continuation must enter the C8.5 replacement barrier rather than replaying removals.");
				if (operation.Phase == CollectionOperationPhase.AwaitingReplacementPhaseAmendment)
					return new CollectionReplacementStartupInspection(operation, CollectionReplacementStartupDisposition.PhaseAmendmentReviewRequired, "C8.5 recorded a supported post-mutation delta; the exact immutable barrier observation requires explicit amendment approval before incoming work can continue.");
				if (operation.Phase == CollectionOperationPhase.ReadyForIncomingNativeChildren)
					return new CollectionReplacementStartupInspection(operation, CollectionReplacementStartupDisposition.IncomingNativeWorkReady, "The post-removal C8.5 barrier is durably verified; C8.6 may begin incoming native work without replaying outgoing removal.");
				if (operation.Phase == CollectionOperationPhase.IncomingNativeChildrenVerified)
					return new CollectionReplacementStartupInspection(operation, CollectionReplacementStartupDisposition.IncomingNativeWorkVerified, "C8.6 incoming native work is durably verified; continuation belongs to C8.8 aggregate verification/association reconciliation rather than replaying incoming children.");
				if (operation.Phase == CollectionOperationPhase.ReplacementBarrierRevalidationRequired)
					return new CollectionReplacementStartupInspection(operation, CollectionReplacementStartupDisposition.PhaseAmendmentRevalidationRequired, "An explicit C8.5 phase amendment is durable, but its observed state must be revalidated before any further incoming native work.");
				if (operation.HasCrossedNativeBoundary || operation.Phase == CollectionOperationPhase.Recovering || operation.Phase == CollectionOperationPhase.RecoveryRequired)
					return new CollectionReplacementStartupInspection(operation, CollectionReplacementStartupDisposition.RecoveryRequired, "Replacement crossed the native boundary and must route to C8.7 recovery/compensation before continuation.");
				if ((operation.Phase == CollectionOperationPhase.ReadyToApply || operation.Phase == CollectionOperationPhase.RemovingOutgoingNativeChildren ||
					operation.Phase == CollectionOperationPhase.PausedAtSafeBoundary) && hasBoundary)
					return new CollectionReplacementStartupInspection(operation, CollectionReplacementStartupDisposition.PreparedBeforeNativeBoundary, "Exact review and mandatory operation recovery inputs are durable; no native child crossed the mutation boundary.");
				return new CollectionReplacementStartupInspection(operation, CollectionReplacementStartupDisposition.PreparationIncomplete, "Replacement remains before the native boundary and must resume review/backup/recovery preparation.");
			}
			catch (Exception ex) when (ex is InvalidDataException || ex is InvalidOperationException || ex is ArgumentException)
			{
				return new CollectionReplacementStartupInspection(operation, CollectionReplacementStartupDisposition.InvalidPersistedState, ex.Message);
			}
		}

		private CollectionReplacementReviewedIntent RequireIntent(CollectionOperation operation, CollectionPlanIdentity expectedPlan)
		{
			if (expectedPlan == null || operation.PlanIdentity == null || !operation.PlanIdentity.Equals(expectedPlan))
				throw new InvalidOperationException("Replacement continuation does not refer to the exact reviewed plan.");
			CollectionResolvedPlanRecord record = _planStore.GetPlan(expectedPlan);
			if (record == null || !CollectionReplacementReviewedIntentCodec.IsSupportedPayloadFormat(record.PayloadFormat))
				throw new InvalidDataException("The immutable replacement reviewed intent is missing or uses an unsupported version.");
			CollectionReplacementReviewedIntent intent = CollectionReplacementReviewedIntentCodec.Deserialize(record.Payload);
			if (!intent.PlanIdentity.Equals(expectedPlan) || !intent.Target.Equals(operation.Target) || !intent.Revision.Equals(operation.Revision) ||
				record.PolicyKind != CollectionExecutionPolicyKind.ReplaceCurrentManagedSetup || !record.CurrentStateFingerprint.Equals(intent.NativeStateFingerprint))
				throw new InvalidDataException("Persisted replacement review metadata no longer matches its immutable operation/plan record.");
			return intent;
		}

		private CollectionReplacementRecoveryBoundary LoadAndValidatePersistedRecoveryBoundary(CollectionOperation operation, CollectionReplacementReviewedIntent intent)
		{
			string ownerId = operation.Identity.OperationId.ToString("D");
			CollectionsRetainedArtifactReferenceRecord boundaryReference = _referenceStore.GetReferenceForOwnerRole(
				CollectionsRetainedArtifactOwnerKind.Operation, ownerId, RecoveryBoundaryRole);
			if (boundaryReference == null || !_artifactStore.VerifyArtifact(boundaryReference.ArtifactId))
				throw new InvalidDataException("The mandatory replacement recovery boundary is missing or failed immutable-content verification.");

			RecoveryBoundaryDto dto;
			try
			{
				using (Stream stream = _artifactStore.OpenRead(boundaryReference.ArtifactId))
				using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true))
					dto = JsonConvert.DeserializeObject<RecoveryBoundaryDto>(reader.ReadToEnd());
			}
			catch (Exception ex) when (!(ex is InvalidDataException))
			{
				throw new InvalidDataException("The mandatory replacement recovery boundary is malformed.", ex);
			}
			if (dto == null || !StringComparer.Ordinal.Equals(dto.PlanId, intent.PlanIdentity.PlanId.ToString("D")) ||
				dto.PlanVersion != intent.PlanIdentity.Version)
				throw new InvalidDataException("The mandatory replacement recovery boundary belongs to another reviewed plan.");

			CollectionReplacementOptionalBackupResult backup;
			if ((CollectionReplacementOptionalBackupStatus)dto.Backup == CollectionReplacementOptionalBackupStatus.Completed)
				backup = CollectionReplacementOptionalBackupResult.Completed(dto.Capture);
			else if ((CollectionReplacementOptionalBackupStatus)dto.Backup == CollectionReplacementOptionalBackupStatus.NotRequested)
				backup = CollectionReplacementOptionalBackupResult.NotRequested();
			else
				throw new InvalidDataException("The mandatory replacement recovery boundary contains an unsupported optional-backup state.");
			ValidateBackup(intent, backup);

			List<CollectionReplacementRecoveryInput> inputs = (dto.Inputs ?? new List<RecoveryInputDto>()).Select(x =>
				new CollectionReplacementRecoveryInput((CollectionReplacementRecoveryInputKind)x.Kind, x.SubjectKey, x.Role, x.ArtifactId)).ToList();
			var boundary = new CollectionReplacementRecoveryBoundary(intent.PlanIdentity, backup, inputs);
			ValidateRecoveryCoverage(intent, boundary.Inputs);
			foreach (CollectionReplacementRecoveryInput input in boundary.Inputs)
			{
				CollectionsRetainedArtifactReferenceRecord reference = _referenceStore.GetReferenceForOwnerRole(
					CollectionsRetainedArtifactOwnerKind.Operation, ownerId, RecoveryInputRolePrefix + input.Role);
				if (reference == null || !StringComparer.Ordinal.Equals(reference.ArtifactId, input.ArtifactId) ||
					!_artifactStore.VerifyArtifact(input.ArtifactId))
					throw new InvalidDataException("A mandatory replacement recovery input is no longer durably available: " + input.Role);
			}
			return boundary;
		}

		private static void ValidateRecoveryCoverage(CollectionReplacementReviewedIntent intent, IEnumerable<CollectionReplacementRecoveryInput> inputs)
		{
			List<CollectionReplacementRecoveryInput> all = inputs.ToList();
			foreach (CollectionReplacementNativeApproval removal in intent.NativeApprovals.Where(x => x.Decision == CollectionReplacementNativeDecision.Remove))
			{
				if (!all.Any(x => x.Kind == CollectionReplacementRecoveryInputKind.NativeIdentity && StringComparer.Ordinal.Equals(x.SubjectKey, removal.NativeModKey)) ||
					!all.Any(x => x.Kind == CollectionReplacementRecoveryInputKind.ReconstructionPayload && StringComparer.Ordinal.Equals(x.SubjectKey, removal.NativeModKey)) ||
					!all.Any(x => x.Kind == CollectionReplacementRecoveryInputKind.NativeEffects && StringComparer.Ordinal.Equals(x.SubjectKey, removal.NativeModKey)))
					throw new InvalidOperationException("Every reviewed outgoing removal requires retained native identity, reconstruction payload and native-effect recovery evidence before replacement can become ready to apply.");
			}
			if (intent.ProfileProtection.HadCurrentProfile && !all.Any(x => x.Kind == CollectionReplacementRecoveryInputKind.ProfileProtection &&
				StringComparer.Ordinal.Equals(x.SubjectKey, intent.ProfileProtection.ProfileId)))
				throw new InvalidOperationException("The reviewed outgoing profile must have operation-owned protection evidence before replacement can become ready to apply.");
		}

		private static void ValidateBackup(CollectionReplacementReviewedIntent intent, CollectionReplacementOptionalBackupResult result)
		{
			if (result == null) throw new ArgumentNullException(nameof(result));
			if (intent.BackupChoice == CollectionReplacementBackupChoice.CreateLocalCollection && result.Status != CollectionReplacementOptionalBackupStatus.Completed)
				throw new InvalidOperationException("The requested persistent Local Collection backup must complete before replacement can become ready to apply.");
			if (intent.BackupChoice == CollectionReplacementBackupChoice.ContinueWithoutLocalCollection && result.Status != CollectionReplacementOptionalBackupStatus.NotRequested)
				throw new InvalidOperationException("Replacement was reviewed without a persistent Local Collection backup; mandatory operation recovery inputs remain required independently.");
		}

		private CollectionOperation RequireOperation(CollectionOperationIdentity identity, CollectionOperationPhase phase)
		{
			if (identity == null) throw new ArgumentNullException(nameof(identity));
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Kind != CollectionOperationKind.ReplaceCurrentManagedSetup)
				throw new InvalidOperationException("Replacement operation is not present in the durable journal.");
			if (operation.Phase != phase || operation.IsTerminal || operation.HasCrossedNativeBoundary)
				throw new InvalidOperationException("Replacement operation is not at the required pre-native phase.");
			return operation;
		}

		private CollectionOperation Save(CollectionOperation current, CollectionOperationPhase phase)
		{
			var next = new CollectionOperation(current.Identity, current.Kind, current.Collection, current.Target, current.Revision,
				current.PlanIdentity, checked(current.CheckpointSequence + 1), phase, CollectionOperationResultState.Pending, current.NativeChildren);
			_operationStore.SaveOperation(next);
			return next;
		}
	}

	public sealed class CollectionReplacementStartupInspection
	{
		internal CollectionReplacementStartupInspection(CollectionOperation operation, CollectionReplacementStartupDisposition disposition, string message)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			if (!Enum.IsDefined(typeof(CollectionReplacementStartupDisposition), disposition) || disposition == CollectionReplacementStartupDisposition.Unknown)
				throw new ArgumentOutOfRangeException(nameof(disposition));
			Disposition = disposition;
			Message = message ?? String.Empty;
		}
		public CollectionOperation Operation { get; }
		public CollectionReplacementStartupDisposition Disposition { get; }
		public string Message { get; }
	}
}
