using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.Mods;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Converts an already translated C5 native recipe into detached C6.4 impact-planning effects without executing it.
	/// </summary>
	public sealed class CollectionMemberEffectPreviewBuilder
	{
		/// <summary>
		/// Builds a root-aware read-only effect preview from one exact resolved member and translated native recipe.
		/// </summary>
		public CollectionMemberEffectPreview Build(ResolvedCollectionMemberPlan member, ModInstallationRecipeInput recipeInput,
			IGameMode gameMode, IMod mod)
		{
			if (member == null)
				throw new ArgumentNullException(nameof(member));
			if (recipeInput == null)
				throw new ArgumentNullException(nameof(recipeInput));
			if (gameMode == null)
				throw new ArgumentNullException(nameof(gameMode));
			if (mod == null)
				throw new ArgumentNullException(nameof(mod));
			if (!StringComparer.Ordinal.Equals(member.RecipeIdentity.Fingerprint, recipeInput.RecipeFingerprint))
				throw new ArgumentException("The translated native recipe must match the resolved Collection member recipe identity.", nameof(recipeInput));
			if (!recipeInput.HasNativePlan)
				throw new ArgumentException("C6.4 effect preview requires an already translated C5 native operation plan.", nameof(recipeInput));

			var files = new Dictionary<ModDeploymentTarget, CollectionPlannedFileEffect>();
			var iniEdits = new Dictionary<CollectionNativeIniKey, CollectionPlannedIniEffect>();
			var gameValues = new Dictionary<string, CollectionPlannedGameValueEffect>(StringComparer.Ordinal);
			var plugins = new List<CollectionPlannedPluginEffect>();
			var issues = new List<CollectionEffectPreviewIssue>();

			foreach (ScriptedInstallOperation operation in recipeInput.NativeOperations)
			{
				InstallModFileOperation installFile = operation as InstallModFileOperation;
				if (installFile != null)
				{
					AddFile(files, ResolveTarget(gameMode, mod, installFile.DestinationPath, recipeInput.InstallContext.InstallRoot));
					continue;
				}

				GenerateDataFileOperation generatedFile = operation as GenerateDataFileOperation;
				if (generatedFile != null)
				{
					AddFile(files, ResolveTarget(gameMode, mod, generatedFile.DestinationPath, recipeInput.InstallContext.InstallRoot));
					continue;
				}

				EditIniOperation editIni = operation as EditIniOperation;
				if (editIni != null)
				{
					var key = new CollectionNativeIniKey(editIni.SettingsFileName, editIni.Section, editIni.Key);
					iniEdits[key] = new CollectionPlannedIniEffect(key, editIni.Value);
					continue;
				}

				EditGameSpecificValueOperation editGameValue = operation as EditGameSpecificValueOperation;
				if (editGameValue != null)
				{
					gameValues[editGameValue.Key] = new CollectionPlannedGameValueEffect(editGameValue.Key, editGameValue.Value);
					continue;
				}

				SetPluginActivationOperation activation = operation as SetPluginActivationOperation;
				if (activation != null)
				{
					plugins.Add(CollectionPlannedPluginEffect.Activation(activation.PluginPath, activation.Activate));
					continue;
				}

				SetPluginOrderIndexOperation absoluteOrder = operation as SetPluginOrderIndexOperation;
				if (absoluteOrder != null)
				{
					plugins.Add(CollectionPlannedPluginEffect.AbsoluteOrder(absoluteOrder.PluginPath, absoluteOrder.NewIndex));
					continue;
				}

				SetRelativeLoadOrderOperation relativeOrder = operation as SetRelativeLoadOrderOperation;
				if (relativeOrder != null)
				{
					plugins.Add(CollectionPlannedPluginEffect.RelativeOrder(relativeOrder.PluginPaths));
					continue;
				}

				if (operation is PerformBasicInstallOperation)
				{
					issues.Add(new CollectionEffectPreviewIssue(CollectionEffectPreviewIssueKind.UnboundedBasicInstall,
						operation.GetType().FullName, "Basic install must be expanded to explicit native effects before exact Collection impact planning."));
					continue;
				}

				if (operation is SetLoadOrderOperation || operation is MovePluginsInLoadOrderOperation)
				{
					issues.Add(new CollectionEffectPreviewIssue(CollectionEffectPreviewIssueKind.LegacyPluginIndexOrdering,
						operation.GetType().FullName, "Legacy plugin-index ordering is not a stable cross-member impact identity and must be translated to stable plugin paths first."));
					continue;
				}

				issues.Add(new CollectionEffectPreviewIssue(CollectionEffectPreviewIssueKind.UnsupportedNativeOperation,
					operation.GetType().FullName, "The translated native operation has no characterized C6.4 impact adapter."));
			}

			return new CollectionMemberEffectPreview(member.MemberKey, member.RecipeIdentity,
				recipeInput.InstallContext.Method, recipeInput.InstallContext.InstallRoot,
				files.Values.OrderBy(x => x.Target.Root).ThenBy(x => x.Target.RelativePath, StringComparer.OrdinalIgnoreCase),
				iniEdits.Values.OrderBy(x => x.Key.File, StringComparer.OrdinalIgnoreCase)
					.ThenBy(x => x.Key.Section, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Key.Key, StringComparer.OrdinalIgnoreCase),
				gameValues.Values.OrderBy(x => x.Key, StringComparer.Ordinal), plugins, issues);
		}

		private static ModDeploymentTarget ResolveTarget(IGameMode gameMode, IMod mod, string destinationPath, ModInstallRoot installRoot)
		{
			return ModDeploymentTargetResolver.Resolve(gameMode, mod, destinationPath, installRoot);
		}

		private static void AddFile(IDictionary<ModDeploymentTarget, CollectionPlannedFileEffect> files, ModDeploymentTarget target)
		{
			if (!files.ContainsKey(target))
				files.Add(target, new CollectionPlannedFileEffect(target));
		}
	}
}
