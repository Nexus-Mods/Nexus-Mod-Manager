using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.Games;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;

namespace Nexus.Client.ModManagement.Scripting.XmlScript
{
	/// <summary>
	/// Translates exact FOMOD selections against an actual parsed XML installer definition into native typed operations.
	/// </summary>
	/// <remarks>
	/// This class performs translation only. Execution remains owned by <see cref="ModInstaller"/> and C5.5. Conditional
	/// file sets are evaluated against the native additive projected state; replacement projection remains outside C5.9.
	/// </remarks>
	internal sealed class XmlScriptFomodRecipeAdapter
	{
		/// <summary>
		/// Identifies the C5.6 native FOMOD-selection adapter contract.
		/// </summary>
		internal const string AdapterId = "nmm-ce.native.fomod";

		/// <summary>
		/// Identifies the supported native FOMOD-selection adapter contract version.
		/// </summary>
		internal const int AdapterVersion = 1;

		/// <summary>
		/// Identifies the exact-selection capability consumed by this adapter.
		/// </summary>
		internal const string CapabilityId = "fomod-selection";

		/// <summary>
		/// Identifies the supported exact-selection capability version.
		/// </summary>
		internal const int CapabilityVersion = 1;

		private sealed class FileIntent
		{
			/// <summary>
			/// Initializes one selected file/folder intent and whether the XML UI would request plugin activation for it.
			/// </summary>
			public FileIntent(InstallableFile file, bool activate)
			{
				File = file;
				Activate = activate;
			}

			/// <summary>
			/// Gets the parsed XML file/folder entry.
			/// </summary>
			public InstallableFile File { get; }

			/// <summary>
			/// Gets whether the selected option requests plugin activation for this entry.
			/// </summary>
			public bool Activate { get; }
		}

		/// <summary>
		/// Translates one validated exact FOMOD selection recipe against the mod's actual parsed XML installer definition.
		/// </summary>
		internal ModInstallationRecipeInput Translate(ModInstallationRecipeInput recipeInput, IMod mod, IGameMode gameMode,
			IEnvironmentInfo environmentInfo, IPluginManager pluginManager, ModInstallationFomodSelectionRecipe recipe)
		{
			if (recipeInput == null)
				throw new ArgumentNullException(nameof(recipeInput));
			if (mod == null)
				throw new ArgumentNullException(nameof(mod));
			if (gameMode == null)
				throw new ArgumentNullException(nameof(gameMode));
			if (environmentInfo == null)
				throw new ArgumentNullException(nameof(environmentInfo));
			if (recipe == null)
				throw new ArgumentNullException(nameof(recipe));

			ValidateAdapterContract(recipeInput.Validation);

			XmlScript script = mod.InstallScript as XmlScript;
			if (!mod.HasInstallScript || script == null || !(script.Type is XmlScriptType))
				throw new NotSupportedException("The FOMOD selection adapter requires the mod's actual XML installer definition.");
			if (!recipe.ScriptVersion.Equals(script.Version))
			{
				throw new InvalidDataException(string.Format(
					"The FOMOD selection recipe targets XML script version {0}, but the actual installer is version {1}.",
					recipe.ScriptVersion, script.Version));
			}

			var stateManager = ((XmlScriptType)script.Type).CreateConditionStateManager(mod, gameMode, pluginManager, environmentInfo);
			if (script.ModPrerequisites != null && !script.ModPrerequisites.GetIsFulfilled(stateManager))
				throw new DependencyException(script.ModPrerequisites.GetMessage(stateManager));

			var optionFiles = new List<FileIntent>();
			ValidateAndApplySelections(script, recipe, stateManager, optionFiles);
			optionFiles.Sort((left, right) => left.File.CompareTo(right.File));

			var plan = new ScriptedInstallationPlan();
			ISet<string> archiveFiles = GetNormalizedArchiveFiles(mod);
			ScriptedInstallationProjectedState projectedState = CreateAdditiveProjectedState(mod, gameMode, pluginManager);

			foreach (InstallableFile requiredFile in script.RequiredInstallFiles)
				AppendInstallableFile(plan, projectedState, mod, archiveFiles, requiredFile, true, pluginManager != null);

			foreach (FileIntent intent in optionFiles)
				AppendInstallableFile(plan, projectedState, mod, archiveFiles, intent.File, intent.Activate, pluginManager != null);

			ISet<string> selectableSources = GetSelectableOptionSources(script);
			IPluginConditionStateProvider previousProvider = stateManager.PluginConditionStateProvider;
			if (projectedState != null)
				stateManager.PluginConditionStateProvider = new ProjectedPluginConditionStateProvider(projectedState);
			try
			{
				foreach (ConditionallyInstalledFileSet fileSet in script.ConditionallyInstalledFileSets)
				{
					if (fileSet == null || fileSet.Condition == null)
						throw new InvalidDataException("The actual FOMOD installer contains a conditional file set without a condition.");
					if (!fileSet.Condition.GetIsFulfilled(stateManager))
						continue;

					foreach (InstallableFile file in fileSet.Files)
					{
						if (IsUnselectedOptionFallback(fileSet, file, selectableSources))
							continue;
						AppendInstallableFile(plan, projectedState, mod, archiveFiles, file, true, pluginManager != null);
					}
				}
			}
			finally
			{
				stateManager.PluginConditionStateProvider = previousProvider;
			}

			if (plan.Count == 0)
				throw new InvalidDataException("The exact FOMOD selection produced no supported native installation operations.");

			ValidateDeclaredPaths(recipeInput.Validation.Paths, plan.Operations);
			return recipeInput.WithNativePlan(plan.Operations);
		}

		/// <summary>
		/// Verifies that C5.3 validation selected exactly the adapter/capability contract implemented here.
		/// </summary>
		private static void ValidateAdapterContract(ModInstallationRecipeValidation validation)
		{
			if (!StringComparer.Ordinal.Equals(validation.AdapterId, AdapterId) || validation.AdapterVersion != AdapterVersion)
			{
				throw new NotSupportedException(string.Format(
					"The FOMOD selection adapter cannot translate recipe adapter '{0}' version {1}.",
					validation.AdapterId, validation.AdapterVersion));
			}

			if (validation.Capabilities.Count != 1 ||
				!StringComparer.Ordinal.Equals(validation.Capabilities[0].CapabilityId, CapabilityId) ||
				validation.Capabilities[0].Version != CapabilityVersion)
			{
				throw new NotSupportedException("The FOMOD selection adapter requires exactly fomod-selection capability version 1 and cannot ignore additional recipe capabilities.");
			}
		}

		/// <summary>
		/// Validates the complete parsed step/group shape, exact selection state and selected file intents.
		/// </summary>
		private static void ValidateAndApplySelections(XmlScript script, ModInstallationFomodSelectionRecipe recipe,
			ConditionStateManager stateManager, IList<FileIntent> optionFiles)
		{
			ValidateSelectionShape(script, recipe);

			var visibleSteps = new bool[script.InstallSteps.Count];
			var selectedByGroup = new Dictionary<OptionGroup, HashSet<int>>();
			var selectedFlagValues = new Dictionary<string, string>(StringComparer.Ordinal);

			for (int stepIndex = 0; stepIndex < script.InstallSteps.Count; stepIndex++)
			{
				InstallStep step = script.InstallSteps[stepIndex];
				ModInstallationFomodStepSelection stepSelection = recipe.Steps[stepIndex];
				bool stepVisible = step.GetIsVisible(stateManager);
				visibleSteps[stepIndex] = stepVisible;

				for (int groupIndex = 0; groupIndex < step.OptionGroups.Count; groupIndex++)
				{
					OptionGroup group = step.OptionGroups[groupIndex];
					ModInstallationFomodGroupSelection groupSelection = stepSelection.Groups[groupIndex];
					HashSet<int> selectedIndices = ResolveSelectedOptions(group, groupSelection);
					selectedByGroup[group] = selectedIndices;

					if (!stepVisible)
					{
						if (selectedIndices.Count != 0)
							throw new InvalidDataException("A FOMOD recipe cannot select options from an install step which is not visible in the current installer state.");
						continue;
					}

					ValidateGroupCardinality(group, selectedIndices.Count);
					foreach (int optionIndex in selectedIndices.OrderBy(index => index))
					{
						Option selectedOption = group.Options[optionIndex];
						foreach (ConditionalFlag flag in selectedOption.Flags)
						{
							string existingValue;
							if (selectedFlagValues.TryGetValue(flag.Name, out existingValue) &&
								!StringComparer.Ordinal.Equals(existingValue, flag.ConditionalValue))
							{
								throw new InvalidDataException(string.Format(
									"The exact FOMOD selection assigns conflicting values to flag '{0}'.", flag.Name));
							}
							selectedFlagValues[flag.Name] = flag.ConditionalValue;
							stateManager.SetFlagValue(flag.Name, flag.ConditionalValue, selectedOption);
						}
					}
				}
			}

			for (int stepIndex = 0; stepIndex < script.InstallSteps.Count; stepIndex++)
			{
				InstallStep step = script.InstallSteps[stepIndex];
				if (step.GetIsVisible(stateManager) != visibleSteps[stepIndex])
				{
					throw new InvalidDataException(
						"The exact FOMOD selections change an install step's visibility after that step is resolved; this dynamic selection requires interactive/native projected handling.");
				}
				if (!visibleSteps[stepIndex])
					continue;

				foreach (OptionGroup group in step.OptionGroups)
				{
					HashSet<int> selectedIndices = selectedByGroup[group];
					for (int optionIndex = 0; optionIndex < group.Options.Count; optionIndex++)
					{
						Option option = group.Options[optionIndex];
						bool selected = selectedIndices.Contains(optionIndex);
						OptionType optionType = option.GetOptionType(stateManager);
						if (optionType == OptionType.Required && !selected)
							throw new InvalidDataException(string.Format("Required FOMOD option '{0}' was not selected.", option.Name));
						if (optionType == OptionType.NotUsable && selected)
							throw new DependencyException(string.Format("Selected FOMOD option '{0}' is not usable in the current installer state.", option.Name));

						foreach (InstallableFile file in option.Files)
						{
							if (selected)
								optionFiles.Add(new FileIntent(file, IsSelectedOptionPluginCandidate(file)));
							else if (file.AlwaysInstall || (file.InstallIfUsable && optionType != OptionType.NotUsable))
								optionFiles.Add(new FileIntent(file, false));
						}
					}
				}
			}
		}

		/// <summary>
		/// Verifies exact step/group positions and names before any condition state is changed.
		/// </summary>
		private static void ValidateSelectionShape(XmlScript script, ModInstallationFomodSelectionRecipe recipe)
		{
			if (recipe.Steps.Count != script.InstallSteps.Count)
				throw new InvalidDataException("The FOMOD selection recipe does not describe every install step in the actual installer definition.");

			for (int stepIndex = 0; stepIndex < script.InstallSteps.Count; stepIndex++)
			{
				InstallStep step = script.InstallSteps[stepIndex];
				ModInstallationFomodStepSelection stepSelection = recipe.Steps[stepIndex];
				if (stepSelection.StepIndex != stepIndex || !StringComparer.Ordinal.Equals(stepSelection.StepName, step.Name))
					throw new InvalidDataException("A FOMOD install-step identity no longer matches the actual installer definition.");
				if (stepSelection.Groups.Count != step.OptionGroups.Count)
					throw new InvalidDataException("A FOMOD step selection does not describe every option group in the actual installer definition.");

				for (int groupIndex = 0; groupIndex < step.OptionGroups.Count; groupIndex++)
				{
					OptionGroup group = step.OptionGroups[groupIndex];
					ModInstallationFomodGroupSelection groupSelection = stepSelection.Groups[groupIndex];
					if (groupSelection.GroupIndex != groupIndex || !StringComparer.Ordinal.Equals(groupSelection.GroupName, group.Name))
						throw new InvalidDataException("A FOMOD option-group identity no longer matches the actual installer definition.");
				}
			}
		}

		/// <summary>
		/// Resolves exact option indices/names and rejects stale selections before any output is produced.
		/// </summary>
		private static HashSet<int> ResolveSelectedOptions(OptionGroup group, ModInstallationFomodGroupSelection selection)
		{
			var selected = new HashSet<int>();
			foreach (ModInstallationFomodOptionSelection optionSelection in selection.SelectedOptions)
			{
				if (optionSelection.OptionIndex < 0 || optionSelection.OptionIndex >= group.Options.Count)
					throw new InvalidDataException("A selected FOMOD option index is outside the actual installer group.");
				Option option = group.Options[optionSelection.OptionIndex];
				if (!StringComparer.Ordinal.Equals(optionSelection.OptionName, option.Name))
					throw new InvalidDataException("A selected FOMOD option identity no longer matches the actual installer definition.");
				if (!selected.Add(optionSelection.OptionIndex))
					throw new InvalidDataException("A FOMOD option was selected more than once.");
			}
			return selected;
		}

		/// <summary>
		/// Enforces the actual FOMOD group-selection contract without substituting native/default choices.
		/// </summary>
		private static void ValidateGroupCardinality(OptionGroup group, int selectedCount)
		{
			switch (group.Type)
			{
				case OptionGroupType.SelectAll:
					if (selectedCount != group.Options.Count)
						throw new InvalidDataException(string.Format("FOMOD group '{0}' requires all options to be selected.", group.Name));
					break;
				case OptionGroupType.SelectAtLeastOne:
					if (selectedCount < 1)
						throw new InvalidDataException(string.Format("FOMOD group '{0}' requires at least one selected option.", group.Name));
					break;
				case OptionGroupType.SelectAtMostOne:
					if (selectedCount > 1)
						throw new InvalidDataException(string.Format("FOMOD group '{0}' permits at most one selected option.", group.Name));
					break;
				case OptionGroupType.SelectExactlyOne:
					if (selectedCount != 1)
						throw new InvalidDataException(string.Format("FOMOD group '{0}' requires exactly one selected option.", group.Name));
					break;
				case OptionGroupType.SelectAny:
					break;
				default:
					throw new InvalidDataException("The actual FOMOD installer contains an unsupported option-group type.");
			}
		}

		/// <summary>
		/// Adds one actual FOMOD file/folder entry to the typed native plan using the legacy XML mapping semantics.
		/// </summary>
		private static void AppendInstallableFile(ScriptedInstallationPlan plan, ScriptedInstallationProjectedState projectedState,
			IMod mod, ISet<string> archiveFiles, InstallableFile file, bool activate, bool hasPluginManager)
		{
			string source = file.Source;
			string destination = file.Destination;
			if (String.IsNullOrWhiteSpace(source))
				throw new InvalidDataException("The actual FOMOD installer contains an empty archive source path.");
			if (!file.IsFolder && (ModInstallFileFilter.IsIgnored(source) || ModInstallFileFilter.IsIgnored(destination)))
				return;

			if (!file.IsFolder)
			{
				string canonicalSource = CanonicalPath(ModInstallationRecipePathKind.ArchiveSource, source);
				if (!archiveFiles.Contains(NormalizeInstallerPath(canonicalSource)))
					throw new InvalidDataException(string.Format("FOMOD source file '{0}' is not present in the verified mod archive.", source));
				string effectiveDestination = String.IsNullOrEmpty(destination) || destination.Equals(".", StringComparison.Ordinal)
					? Path.GetFileName(source)
					: destination;
				string canonicalDestination = CanonicalPath(ModInstallationRecipePathKind.Destination, effectiveDestination);
				AppendOperation(plan, projectedState, new InstallModFileOperation(canonicalSource, canonicalDestination));

				string activationPath = String.IsNullOrEmpty(destination) ? source : destination;
				if (hasPluginManager)
					AppendOperation(plan, projectedState, new SetPluginActivationOperation(activationPath, activate, true));
				return;
			}

			IList<string> sourceFiles = mod.GetFileList(source, true);
			if (sourceFiles == null)
				throw new InvalidDataException(string.Format("FOMOD folder source '{0}' could not be enumerated from the verified mod archive.", source));
			List<string> modFiles = sourceFiles.Where(path => !ModInstallFileFilter.IsIgnored(path)).ToList();
			string from = source.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			if (!from.EndsWith(Path.DirectorySeparatorChar.ToString()))
				from += Path.DirectorySeparatorChar;
			string to = destination.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			if (to.Length > 0 && !to.EndsWith(Path.DirectorySeparatorChar.ToString()))
				to += Path.DirectorySeparatorChar;

			foreach (string modFile in modFiles)
			{
				string normalizedModFile = String.IsNullOrWhiteSpace(modFile)
					? null
					: modFile.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
				if (String.IsNullOrWhiteSpace(normalizedModFile) || normalizedModFile.Length < from.Length ||
					!normalizedModFile.StartsWith(from, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException(string.Format("FOMOD folder source '{0}' returned an invalid archive child path '{1}'.", source, modFile));
				}
				string relativeName = normalizedModFile.Substring(from.Length, normalizedModFile.Length - from.Length);
				if (String.IsNullOrWhiteSpace(relativeName))
					throw new InvalidDataException(string.Format("FOMOD folder source '{0}' returned an empty archive child path.", source));
				string newFileName = to.Length > 0 ? Path.Combine(to, relativeName) : relativeName;
				string canonicalSource = CanonicalPath(ModInstallationRecipePathKind.ArchiveSource, modFile);
				if (!archiveFiles.Contains(NormalizeInstallerPath(canonicalSource)))
					throw new InvalidDataException(string.Format("FOMOD source file '{0}' is not present in the verified mod archive.", modFile));
				string canonicalDestination = CanonicalPath(ModInstallationRecipePathKind.Destination, newFileName);
				AppendOperation(plan, projectedState, new InstallModFileOperation(canonicalSource, canonicalDestination));
				if (hasPluginManager && destination.Length == 0)
					AppendOperation(plan, projectedState, new SetPluginActivationOperation(relativeName, activate, true));
			}
		}

		/// <summary>
		/// Creates the native additive projection used while conditional file sets are translated.
		/// </summary>
		private static ScriptedInstallationProjectedState CreateAdditiveProjectedState(IMod mod, IGameMode gameMode,
			IPluginManager pluginManager)
		{
			return pluginManager == null ? null : new ScriptedInstallationProjectedState(mod, gameMode, pluginManager);
		}

		/// <summary>
		/// Adds one native operation to the translated plan and to the additive projection visible to later conditions.
		/// </summary>
		private static void AppendOperation(ScriptedInstallationPlan plan, ScriptedInstallationProjectedState projectedState,
			ScriptedInstallOperation operation)
		{
			plan.Add(operation);
			if (projectedState != null)
				projectedState.Apply(operation);
		}

		/// <summary>
		/// Gets whether the legacy FOMOD UI would request activation for a selected option file entry.
		/// </summary>
		private static bool IsSelectedOptionPluginCandidate(InstallableFile file)
		{
			if (file.IsFolder)
				return file.Destination.Length == 0;

			string path = String.IsNullOrEmpty(file.Destination) ? file.Source : file.Destination;
			return path.EndsWith(".esm", StringComparison.OrdinalIgnoreCase) ||
				path.EndsWith(".esp", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Builds the normalized set of actual archive files exposed by the verified mod without rejecting unrelated entries.
		/// </summary>
		private static ISet<string> GetNormalizedArchiveFiles(IMod mod)
		{
			var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string path in mod.GetFileList())
			{
				if (!String.IsNullOrWhiteSpace(path))
					result.Add(NormalizeInstallerPath(path));
			}
			return result;
		}

		/// <summary>
		/// Gets the selectable archive sources used to suppress inactive-option fallback records.
		/// </summary>
		private static ISet<string> GetSelectableOptionSources(XmlScript script)
		{
			var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (InstallStep step in script.InstallSteps)
				foreach (OptionGroup group in step.OptionGroups)
					foreach (Option option in group.Options)
						foreach (InstallableFile file in option.Files)
							if (!String.IsNullOrEmpty(file.Source))
								result.Add(NormalizeInstallerPath(file.Source));
			return result;
		}

		/// <summary>
		/// Matches the native XML installer's suppression of inactive-option fallback file sets.
		/// </summary>
		private static bool IsUnselectedOptionFallback(ConditionallyInstalledFileSet fileSet, InstallableFile file,
			ISet<string> selectableSources)
		{
			if (fileSet == null || file == null || String.IsNullOrEmpty(file.Source))
				return false;
			return IsInactiveFlagCondition(fileSet.Condition) && selectableSources.Contains(NormalizeInstallerPath(file.Source));
		}

		/// <summary>
		/// Identifies flag conditions that represent an option remaining inactive.
		/// </summary>
		private static bool IsInactiveFlagCondition(ICondition condition)
		{
			FlagCondition flagCondition = condition as FlagCondition;
			if (flagCondition != null)
				return String.Equals(flagCondition.Value, "Inactive", StringComparison.OrdinalIgnoreCase);

			CompositeCondition composite = condition as CompositeCondition;
			if (composite == null || composite.Operator == ConditionOperator.Or)
				return false;
			return composite.Conditions.Count > 0 && composite.Conditions.All(IsInactiveFlagCondition);
		}

		/// <summary>
		/// Verifies that translation consumes exactly the source/destination path set admitted by C5.3 validation.
		/// </summary>
		private static void ValidateDeclaredPaths(IReadOnlyList<ModInstallationRecipePath> declaredPaths,
			IReadOnlyList<ScriptedInstallOperation> operations)
		{
			var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (InstallModFileOperation operation in operations.OfType<InstallModFileOperation>())
			{
				expected.Add(CreatePathKey(ModInstallationRecipePathKind.ArchiveSource, operation.SourcePath));
				expected.Add(CreatePathKey(ModInstallationRecipePathKind.Destination, operation.DestinationPath));
			}

			var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (ModInstallationRecipePath path in declaredPaths)
			{
				string key = CreatePathKey(path.Kind, path.Path);
				if (!declared.Add(key))
					throw new InvalidDataException("Recipe validation contains duplicate path declarations.");
				if (!expected.Contains(key))
					throw new InvalidDataException("Recipe validation contains a path which is not produced by the exact FOMOD selection.");
			}
			if (declared.Count != expected.Count)
				throw new InvalidDataException("The exact FOMOD selection produces a source or destination path which was not admitted by recipe validation.");
		}

		/// <summary>
		/// Canonicalizes one final file-operation path through the common C5.3 path boundary.
		/// </summary>
		private static string CanonicalPath(ModInstallationRecipePathKind kind, string path)
		{
			return new ModInstallationRecipePath(kind, path).Path;
		}

		/// <summary>
		/// Creates a comparison key which keeps archive-source and destination namespaces distinct.
		/// </summary>
		private static string CreatePathKey(ModInstallationRecipePathKind kind, string path)
		{
			return ((int)kind).ToString() + "\0" + path;
		}

		/// <summary>
		/// Normalizes a native XML-installer path to the platform directory separator.
		/// </summary>
		private static string NormalizeInstallerPath(string path)
		{
			return path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
		}
	}
}
