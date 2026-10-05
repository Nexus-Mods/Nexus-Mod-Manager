using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;

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
			IGameMode gameMode, IMod mod, IPluginManager pluginManager = null)
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

			long performanceStarted = CollectionPerformanceMetrics.StartTiming();
			var files = new Dictionary<ModDeploymentTarget, CollectionPlannedFileEffect>();
			var iniEdits = new Dictionary<CollectionNativeIniKey, CollectionPlannedIniEffect>();
			var gameValues = new Dictionary<string, CollectionPlannedGameValueEffect>(StringComparer.Ordinal);
			var requestedPluginActivations = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			var pluginOrderingEffects = new List<CollectionPlannedPluginEffect>();
			var issues = new List<CollectionEffectPreviewIssue>();

			foreach (ScriptedInstallOperation operation in recipeInput.NativeOperations)
			{
				InstallModFileOperation installFile = operation as InstallModFileOperation;
				if (installFile != null)
				{
					ModDeploymentTarget target = ResolveTarget(gameMode, mod, installFile.DestinationPath, recipeInput.InstallContext.InstallRoot);
					using (FileStream source = mod.GetFileStream(installFile.SourcePath))
					{
						CollectionPerformanceMetrics.RecordArchiveSourceRead(member.MemberKey, installFile.SourcePath, source.Length);
						AddFile(files, target, ComputeContentHash(source), source.Length);
					}
					AddImplicitPluginActivation(requestedPluginActivations, gameMode, pluginManager, target, installFile);
					continue;
				}

				GenerateDataFileOperation generatedFile = operation as GenerateDataFileOperation;
				if (generatedFile != null)
				{
					// Native C5 execution registers generated plugins but deliberately does not request implicit activation.
					byte[] generatedBytes = generatedFile.Data ?? new byte[0];
					AddFile(files, ResolveTarget(gameMode, mod, generatedFile.DestinationPath, recipeInput.InstallContext.InstallRoot),
						ComputeContentHash(generatedBytes), generatedBytes.LongLength);
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
					AddExplicitPluginActivation(requestedPluginActivations, gameMode, mod, pluginManager, activation);
					continue;
				}

				SetPluginOrderIndexOperation absoluteOrder = operation as SetPluginOrderIndexOperation;
				if (absoluteOrder != null)
				{
					pluginOrderingEffects.Add(CollectionPlannedPluginEffect.AbsoluteOrder(absoluteOrder.PluginPath, absoluteOrder.NewIndex));
					continue;
				}

				SetRelativeLoadOrderOperation relativeOrder = operation as SetRelativeLoadOrderOperation;
				if (relativeOrder != null)
				{
					pluginOrderingEffects.Add(CollectionPlannedPluginEffect.RelativeOrder(relativeOrder.PluginPaths));
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

			var plugins = requestedPluginActivations
				.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
				.Select(x => CollectionPlannedPluginEffect.Activation(x.Key, x.Value))
				.Concat(pluginOrderingEffects)
				.ToList();

			var result = new CollectionMemberEffectPreview(member.MemberKey, member.RecipeIdentity,
				recipeInput.InstallContext.Method, recipeInput.InstallContext.InstallRoot,
				files.Values.OrderBy(x => x.Target.Root).ThenBy(x => x.Target.RelativePath, StringComparer.OrdinalIgnoreCase),
				iniEdits.Values.OrderBy(x => x.Key.File, StringComparer.OrdinalIgnoreCase)
					.ThenBy(x => x.Key.Section, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Key.Key, StringComparer.OrdinalIgnoreCase),
				gameValues.Values.OrderBy(x => x.Key, StringComparer.Ordinal), plugins, issues);
			CollectionPerformanceMetrics.RecordEffectPreview(performanceStarted);
			return result;
		}

		private static ModDeploymentTarget ResolveTarget(IGameMode gameMode, IMod mod, string destinationPath, ModInstallRoot installRoot)
		{
			return ModDeploymentTargetResolver.Resolve(gameMode, mod, destinationPath, installRoot);
		}

		private static void AddImplicitPluginActivation(IDictionary<string, bool> requestedActivations, IGameMode gameMode,
			IPluginManager pluginManager, ModDeploymentTarget target, InstallModFileOperation operation)
		{
			if (!WillBecomePhysicalWinner(operation))
				return;
			if (pluginManager == null)
			{
				if (gameMode.UsesPlugins)
					throw new ArgumentNullException(nameof(pluginManager), "Plugin-enabled Collection file-effect preview requires the live plugin manager so implicit activation can be characterized exactly.");
				return;
			}

			string deployedPath = ModDeploymentTargetResolver.GetPhysicalPath(gameMode, target);
			bool activatable = pluginManager.IsActivatiblePluginFile(deployedPath);
			// Gamebryo's native plugin factory requires the file to exist. Collection review happens before
			// mutation, so use the game mode's declared plugin directory/extensions to characterize a future
			// plugin path when the exact reviewed file is not on disk yet. Never override a rejection for an
			// already-existing file.
			if (!activatable && !File.Exists(deployedPath))
				activatable = IsPotentialActivatablePluginPath(gameMode, deployedPath);
			if (activatable && !requestedActivations.ContainsKey(deployedPath))
				requestedActivations.Add(deployedPath, true);
		}

		private static void AddExplicitPluginActivation(IDictionary<string, bool> requestedActivations, IGameMode gameMode, IMod mod,
			IPluginManager pluginManager, SetPluginActivationOperation activation)
		{
			if (pluginManager == null)
			{
				if (gameMode.UsesPlugins)
					throw new ArgumentNullException(nameof(pluginManager), "Plugin-enabled Collection activation preview requires the live plugin manager.");
				return;
			}

			string fixedPath = gameMode.GetModFormatAdjustedPath(mod.Format, activation.PluginPath, false);
			string physicalPath = Path.IsPathRooted(fixedPath)
				? fixedPath
				: Path.Combine(gameMode.GameModeEnvironmentInfo.InstallationPath, fixedPath);
			if (activation.RequireActivatablePlugin)
			{
				bool activatable = pluginManager.IsActivatiblePluginFile(physicalPath);
				if (!activatable && !File.Exists(physicalPath))
					activatable = IsPotentialActivatablePluginPath(gameMode, physicalPath);
				if (!activatable)
					return;
			}

			requestedActivations[physicalPath] = activation.Activate;
		}

		private static bool IsPotentialActivatablePluginPath(IGameMode gameMode, string physicalPath)
		{
			if (gameMode == null || !gameMode.UsesPlugins || String.IsNullOrWhiteSpace(physicalPath) ||
				gameMode.PluginExtensions == null)
				return false;

			string extension = Path.GetExtension(physicalPath);
			if (String.IsNullOrEmpty(extension) || !gameMode.PluginExtensions.Any(candidate =>
				String.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase)))
				return false;

			string pluginDirectory = gameMode.PluginDirectory;
			if (String.IsNullOrWhiteSpace(pluginDirectory))
				return true;

			string normalizedPluginDirectory = Path.GetFullPath(pluginDirectory).TrimEnd(
				Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string normalizedFileDirectory = Path.GetDirectoryName(Path.GetFullPath(physicalPath));
			return String.Equals(normalizedPluginDirectory, normalizedFileDirectory, StringComparison.OrdinalIgnoreCase);
		}

		private static bool WillBecomePhysicalWinner(InstallModFileOperation operation)
		{
			return operation.DeploymentDecision == null || operation.DeploymentDecision.Activate;
		}

		private static void AddFile(IDictionary<ModDeploymentTarget, CollectionPlannedFileEffect> files, ModDeploymentTarget target,
			CollectionContentHash contentHash, long byteLength)
		{
			files[target] = new CollectionPlannedFileEffect(target, contentHash, byteLength);
		}

		private static CollectionContentHash ComputeContentHash(Stream stream)
		{
			if (stream == null) throw new ArgumentNullException(nameof(stream));
			long originalPosition = stream.CanSeek ? stream.Position : 0;
			try
			{
				if (stream.CanSeek) stream.Position = 0;
				using (SHA256 sha256 = SHA256.Create())
					return CollectionContentHash.FromSha256(BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", String.Empty).ToLowerInvariant());
			}
			finally
			{
				if (stream.CanSeek) stream.Position = originalPosition;
			}
		}

		private static CollectionContentHash ComputeContentHash(byte[] bytes)
		{
			using (SHA256 sha256 = SHA256.Create())
				return CollectionContentHash.FromSha256(BitConverter.ToString(sha256.ComputeHash(bytes ?? new byte[0])).Replace("-", String.Empty).ToLowerInvariant());
		}
	}
}
