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

		internal sealed class ConditionEnvironmentPluginStateProvider : IPluginConditionStateProvider
		{
			private readonly IModInstallationConditionEnvironment _environment;

			public ConditionEnvironmentPluginStateProvider(IModInstallationConditionEnvironment environment)
			{
				_environment = environment ?? throw new ArgumentNullException(nameof(environment));
			}

			public bool IsPluginRegistered(string pluginPath)
			{
				bool value;
				if (!_environment.TryGetPluginRegistered(pluginPath, out value))
					throw new NotSupportedException("The projected replacement environment cannot prove plugin registration state for '" + pluginPath + "'.");
				return value;
			}

			public bool IsPluginActive(string pluginPath)
			{
				bool value;
				if (!_environment.TryGetPluginActive(pluginPath, out value))
					throw new NotSupportedException("The projected replacement environment cannot prove plugin activation state for '" + pluginPath + "'.");
				return value;
			}
		}

		/// <summary>Gets the exact parsed XML/FOMOD version from the verified mod.</summary>
		internal Version GetScriptVersion(IMod mod)
		{
			return GetScript(mod).Version;
		}

		/// <summary>Resolves the exact file-path trust boundary for one selection before C5.3 validation.</summary>
		internal IReadOnlyList<ModInstallationRecipePath> GetValidationPaths(IMod mod, IGameMode gameMode,
			IEnvironmentInfo environmentInfo, IPluginManager pluginManager, ModInstallationFomodSelectionRecipe recipe)
		{
			return GetValidationPaths(mod, gameMode, environmentInfo, pluginManager, recipe, null);
		}

		internal IReadOnlyList<ModInstallationRecipePath> GetValidationPaths(IMod mod, IGameMode gameMode,
			IEnvironmentInfo environmentInfo, IPluginManager pluginManager, ModInstallationFomodSelectionRecipe recipe,
			IModInstallationConditionEnvironment conditionEnvironment)
		{
			IReadOnlyList<ScriptedInstallOperation> operations = BuildPlan(mod, gameMode, environmentInfo, pluginManager, recipe, conditionEnvironment);
			return GetValidationPaths(operations);
		}

		/// <summary>Derives the path envelope from an already evaluated immutable operation list.</summary>
		private static IReadOnlyList<ModInstallationRecipePath> GetValidationPaths(IReadOnlyList<ScriptedInstallOperation> operations)
		{
			var result = new List<ModInstallationRecipePath>();
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (InstallModFileOperation operation in operations.OfType<InstallModFileOperation>())
			{
				var source = new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, operation.SourcePath);
				var destination = new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, operation.DestinationPath);
				if (seen.Add("S\0" + source.Path)) result.Add(source);
				if (seen.Add("D\0" + destination.Path)) result.Add(destination);
			}
			return result;
		}

		/// <summary>
		/// Translates one validated exact FOMOD selection recipe against the mod's actual parsed XML installer definition.
		/// </summary>
		internal ModInstallationRecipeInput Translate(ModInstallationRecipeInput recipeInput, IMod mod, IGameMode gameMode,
			IEnvironmentInfo environmentInfo, IPluginManager pluginManager, ModInstallationFomodSelectionRecipe recipe)
		{
			return Translate(recipeInput, mod, gameMode, environmentInfo, pluginManager, recipe, null);
		}

		internal ModInstallationRecipeInput Translate(ModInstallationRecipeInput recipeInput, IMod mod, IGameMode gameMode,
			IEnvironmentInfo environmentInfo, IPluginManager pluginManager, ModInstallationFomodSelectionRecipe recipe,
			IModInstallationConditionEnvironment conditionEnvironment)
		{
			if (recipeInput == null)
				throw new ArgumentNullException(nameof(recipeInput));
			ValidateAdapterContract(recipeInput.Validation);
			IReadOnlyList<ScriptedInstallOperation> operations = BuildPlan(mod, gameMode, environmentInfo, pluginManager, recipe, conditionEnvironment);
			ValidateDeclaredPaths(recipeInput.Validation.Paths, operations);
			return recipeInput.WithNativePlan(operations);
		}

		/// <summary>Evaluates one installer plan and validates the caller's envelope against exactly that plan.</summary>
		internal ModInstallationRecipeInput Prepare(IMod mod, IGameMode gameMode, IEnvironmentInfo environmentInfo,
			IPluginManager pluginManager, ModInstallationFomodSelectionRecipe recipe, IModInstallationConditionEnvironment conditionEnvironment,
			Func<IReadOnlyList<ModInstallationRecipePath>, ModInstallationRecipeInput> createValidatedInput)
		{
			if (createValidatedInput == null) throw new ArgumentNullException(nameof(createValidatedInput));
			IReadOnlyList<ScriptedInstallOperation> operations = BuildPlan(mod, gameMode, environmentInfo, pluginManager, recipe, conditionEnvironment);
			ModInstallationRecipeInput input = createValidatedInput(GetValidationPaths(operations));
			if (input == null) throw new InvalidOperationException("FOMOD preparation requires a validated recipe envelope.");
			ValidateAdapterContract(input.Validation);
			ValidateDeclaredPaths(input.Validation.Paths, operations);
			return input.WithNativePlan(operations);
		}

		private static IReadOnlyList<ScriptedInstallOperation> BuildPlan(IMod mod, IGameMode gameMode,
			IEnvironmentInfo environmentInfo, IPluginManager pluginManager, ModInstallationFomodSelectionRecipe recipe,
			IModInstallationConditionEnvironment conditionEnvironment)
		{
			if (mod == null) throw new ArgumentNullException(nameof(mod));
			if (gameMode == null) throw new ArgumentNullException(nameof(gameMode));
			if (environmentInfo == null) throw new ArgumentNullException(nameof(environmentInfo));
			if (recipe == null) throw new ArgumentNullException(nameof(recipe));

			XmlScript script = GetScript(mod);
			if (!recipe.ScriptVersion.Equals(script.Version))
			{
				throw new InvalidDataException(string.Format(
					"The FOMOD selection recipe targets XML script version {0}, but the actual installer is version {1}.",
					recipe.ScriptVersion, script.Version));
			}

			var stateManager = ((XmlScriptType)script.Type).CreateConditionStateManager(mod, gameMode, pluginManager, environmentInfo);
			if (conditionEnvironment != null)
				stateManager.PluginConditionStateProvider = new ConditionEnvironmentPluginStateProvider(conditionEnvironment);
			if (!recipe.HonorRecordedSelections && script.ModPrerequisites != null && !script.ModPrerequisites.GetIsFulfilled(stateManager))
				throw new DependencyException(script.ModPrerequisites.GetMessage(stateManager));

			var optionFiles = new List<FileIntent>();
			ValidateAndApplySelections(script, recipe, stateManager, optionFiles);
			optionFiles.Sort((left, right) => left.File.CompareTo(right.File));

			var plan = new ScriptedInstallationPlan();
			ISet<string> archiveFiles = GetNormalizedArchiveFiles(mod);
			ScriptedInstallationProjectedState projectedState = CreateProjectedState(mod, gameMode, pluginManager, conditionEnvironment);

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
					if (!fileSet.Condition.GetIsFulfilled(stateManager)) continue;
					foreach (InstallableFile file in fileSet.Files)
					{
						if (IsUnselectedOptionFallback(fileSet, file, selectableSources)) continue;
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
			return plan.Operations;
		}

		private static XmlScript GetScript(IMod mod)
		{
			if (mod == null) throw new ArgumentNullException(nameof(mod));
			XmlScript script = mod.InstallScript as XmlScript;
			if (!mod.HasInstallScript || script == null || !(script.Type is XmlScriptType))
				throw new NotSupportedException("The FOMOD selection adapter requires the mod's actual XML installer definition.");
			return script;
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
			if (recipe.HonorRecordedSelections) recipe = AlignRecordedOptionalGroups(script, recipe);
			ValidateSelectionShape(script, recipe);

			var visibleSteps = new bool[script.InstallSteps.Count];
			var selectedByGroup = new Dictionary<OptionGroup, HashSet<int>>();
			var selectedFlagValues = new Dictionary<string, string>(StringComparer.Ordinal);

			for (int stepIndex = 0; stepIndex < script.InstallSteps.Count; stepIndex++)
			{
				InstallStep step = script.InstallSteps[stepIndex];
				ModInstallationFomodStepSelection stepSelection = recipe.Steps[stepIndex];
				// Automatic Collections replay the curator's choices even before prerequisite mods are deployed.
				bool hasRecordedChoices = stepSelection.Groups.Any(group => group.SelectedOptions.Count != 0);
				bool stepVisible = recipe.HonorRecordedSelections && stepSelection.IsVisible.HasValue
					? stepSelection.IsVisible.Value : (recipe.HonorRecordedSelections && hasRecordedChoices) || step.GetIsVisible(stateManager);
				visibleSteps[stepIndex] = stepVisible;

				for (int groupIndex = 0; groupIndex < step.OptionGroups.Count; groupIndex++)
				{
					OptionGroup group = step.OptionGroups[groupIndex];
					ModInstallationFomodGroupSelection groupSelection = stepSelection.Groups[groupIndex];
					HashSet<int> selectedIndices = ResolveSelectedOptions(group, groupSelection, recipe.HonorRecordedSelections);
					selectedByGroup[group] = selectedIndices;

					if (!stepVisible)
					{
						if (selectedIndices.Count != 0)
							throw new InvalidDataException("A FOMOD recipe cannot select options from an install step which is not visible in the current installer state.");
						continue;
					}

					// Recorded empty groups remain empty instead of inventing a current-state default.
					if (!recipe.HonorRecordedSelections || selectedIndices.Count != 0)
						ValidateGroupCardinality(group, selectedIndices.Count);
					foreach (int optionIndex in selectedIndices.OrderBy(index => index))
					{
						Option selectedOption = group.Options[optionIndex];
						foreach (ConditionalFlag flag in selectedOption.Flags)
						{
							string existingValue;
							// Native wizard selections overwrite earlier defaults; replay recorded choices in installer order.
							if (!recipe.HonorRecordedSelections && selectedFlagValues.TryGetValue(flag.Name, out existingValue) &&
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
				if (!recipe.HonorRecordedSelections && step.GetIsVisible(stateManager) != visibleSteps[stepIndex])
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
						OptionType optionType = recipe.HonorRecordedSelections && selected
							? OptionType.Optional : option.GetOptionType(stateManager);
						if (!recipe.HonorRecordedSelections && optionType == OptionType.Required && !selected)
							throw new InvalidDataException(string.Format("Required FOMOD option '{0}' was not selected.", option.Name));
						if (!recipe.HonorRecordedSelections && optionType == OptionType.NotUsable && selected && !IsNoEffectInformationalOption(option))
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
		/// Some Vortex Collections serialize selected informational/credit choices which the XML
		/// installer itself labels NotUsable. An option with neither installable files nor
		/// conditional flags cannot change the native plan or installer condition state.
		/// Outside automatic recorded-choice replay, an option which changes either remains a dependency error.
		/// </summary>
		private static bool IsNoEffectInformationalOption(Option option)
		{
			return option != null && option.Files != null && option.Files.Count == 0 &&
				option.Flags != null && option.Flags.Count == 0;
		}

		/// <summary>Replays uniquely named recorded groups after an installer adds optional groups, leaving new choices unselected.</summary>
		private static ModInstallationFomodSelectionRecipe AlignRecordedOptionalGroups(XmlScript script, ModInstallationFomodSelectionRecipe recipe)
		{
			if (recipe.Steps.Count != script.InstallSteps.Count) return recipe;
			var steps = new List<ModInstallationFomodStepSelection>();
			for (int stepIndex = 0; stepIndex < script.InstallSteps.Count; stepIndex++)
			{
				InstallStep step = script.InstallSteps[stepIndex];
				ModInstallationFomodStepSelection recorded = recipe.Steps[stepIndex];
				if (recorded.Groups.Count == step.OptionGroups.Count)
				{
					steps.Add(recorded);
					continue;
				}
				var groups = new List<ModInstallationFomodGroupSelection>();
				var consumed = new HashSet<ModInstallationFomodGroupSelection>();
				for (int index = 0; index < step.OptionGroups.Count; index++)
				{
					OptionGroup group = step.OptionGroups[index];
					List<ModInstallationFomodGroupSelection> matches = recorded.Groups.Where(x =>
						StringComparer.Ordinal.Equals(x.GroupName, group.Name)).ToList();
					if (matches.Count > 1 || (matches.Count == 1 && step.OptionGroups.Count(x =>
						StringComparer.Ordinal.Equals(x.Name, group.Name)) != 1)) return recipe;
					if (matches.Count == 1)
					{
						consumed.Add(matches[0]);
						groups.Add(new ModInstallationFomodGroupSelection(index, group.Name, matches[0].SelectedOptions));
					}
					else
					{
						if (group.Type != OptionGroupType.SelectAny && group.Type != OptionGroupType.SelectAtMostOne) return recipe;
						groups.Add(new ModInstallationFomodGroupSelection(index, group.Name, new ModInstallationFomodOptionSelection[0]));
					}
				}
				if (consumed.Count != recorded.Groups.Count) return recipe;
				steps.Add(new ModInstallationFomodStepSelection(recorded.StepIndex, recorded.StepName, groups, recorded.IsVisible));
			}
			return new ModInstallationFomodSelectionRecipe(recipe.ScriptVersion, steps, true);
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
				// Vortex may encode an unnamed legacy step as absent/null/empty. These are
				// equivalent only if the actual native step is also unnamed; all named steps
				// still require exact name and position equality.
				bool sameStepName = StringComparer.Ordinal.Equals(stepSelection.StepName, step.Name) ||
					(String.IsNullOrEmpty(stepSelection.StepName) && String.IsNullOrEmpty(step.Name));
				if (stepSelection.StepIndex != stepIndex || !sameStepName)
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
		private static HashSet<int> ResolveSelectedOptions(OptionGroup group, ModInstallationFomodGroupSelection selection,
			bool honorRecordedSelections)
		{
			var selected = new HashSet<int>();
			foreach (ModInstallationFomodOptionSelection optionSelection in selection.SelectedOptions)
			{
				if (optionSelection.OptionIndex < 0 || optionSelection.OptionIndex >= group.Options.Count)
					throw new InvalidDataException("A selected FOMOD option index is outside the actual installer group.");
				Option option = group.Options[optionSelection.OptionIndex];
				int resolvedIndex = optionSelection.OptionIndex;
				if (!StringComparer.Ordinal.Equals(optionSelection.OptionName, option.Name))
				{
					// A Collection's recorded name identifies its choice even when its index follows a different
					// Vortex ordering. Normal native recipes retain strict indices for explicitly ordered groups.
					List<int> namedIndices = group.OptionSortOrder == SortOrder.Explicit && !honorRecordedSelections
						? new List<int>()
						: Enumerable.Range(0, group.Options.Count)
							.Where(index => StringComparer.Ordinal.Equals(group.Options[index].Name, optionSelection.OptionName)).ToList();
					if (namedIndices.Count != 1)
						throw new InvalidDataException(String.Format("Selected FOMOD option '{0}' at index {1} does not match '{2}' in group '{3}', and cannot be resolved uniquely.",
							optionSelection.OptionName, optionSelection.OptionIndex, option.Name, group.Name));
					resolvedIndex = namedIndices[0];
				}
				if (!selected.Add(resolvedIndex))
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

			// FOMOD root/dot notation is installer syntax, not an absolute native recipe path.
			source = NormalizeFomodPath(ModInstallationRecipePathKind.ArchiveSource, source, file.IsFolder);
			destination = NormalizeFomodPath(ModInstallationRecipePathKind.Destination, destination, true);

			if (!file.IsFolder)
			{
				string canonicalSource = CanonicalPath(ModInstallationRecipePathKind.ArchiveSource, source);
				if (!archiveFiles.Contains(NormalizeInstallerPath(canonicalSource)))
					throw new InvalidDataException(string.Format("FOMOD source file '{0}' is not present in the verified mod archive.", source));
				string effectiveDestination = String.IsNullOrEmpty(destination) || destination.Equals(".", StringComparison.Ordinal)
					? Path.GetFileName(source)
					: destination;
				// A file entry may name a destination directory; resolve it before applying file-path validation.
				bool destinationIsDirectory = effectiveDestination.EndsWith("\\", StringComparison.Ordinal) ||
					effectiveDestination.EndsWith("/", StringComparison.Ordinal);
				if (destinationIsDirectory)
					effectiveDestination += Path.GetFileName(canonicalSource);
				string canonicalDestination = CanonicalPath(ModInstallationRecipePathKind.Destination, effectiveDestination);
				AppendOperation(plan, projectedState, new InstallModFileOperation(canonicalSource, canonicalDestination));

				if (hasPluginManager)
					AppendOperation(plan, projectedState, new SetPluginActivationOperation(canonicalDestination, activate, true));
				return;
			}

			IModInstallerSourceInventory installerInventory = mod as IModInstallerSourceInventory;
			IList<string> sourceFiles = installerInventory == null
				? (source.Length == 0 ? mod.GetFileList() : mod.GetFileList(source, true))
				: installerInventory.GetInstallerSourceFiles(source.Length == 0 ? null : source, true);
			if (sourceFiles == null)
				throw new InvalidDataException(string.Format("FOMOD folder source '{0}' could not be enumerated from the verified mod archive.", source));
			List<string> modFiles = sourceFiles.Where(path => !ModInstallFileFilter.IsIgnored(path)).ToList();
			string from = source.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			if (from.Length > 0 && !from.EndsWith(Path.DirectorySeparatorChar.ToString()))
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
		private static ScriptedInstallationProjectedState CreateProjectedState(IMod mod, IGameMode gameMode,
			IPluginManager pluginManager, IModInstallationConditionEnvironment conditionEnvironment)
		{
			if (pluginManager == null)
				return null;
			return conditionEnvironment == null
				? new ScriptedInstallationProjectedState(mod, gameMode, pluginManager)
				: new ScriptedInstallationProjectedState(mod, gameMode, pluginManager, conditionEnvironment);
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
				return NormalizeFomodPath(ModInstallationRecipePathKind.Destination, file.Destination, true).Length == 0;

			string destination = NormalizeFomodPath(ModInstallationRecipePathKind.Destination, file.Destination, true);
			string path = destination.Length == 0 || destination.EndsWith("\\", StringComparison.Ordinal) ? file.Source : destination;
			return path.EndsWith(".esm", StringComparison.OrdinalIgnoreCase) ||
				path.EndsWith(".esp", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Builds the normalized set of actual archive files exposed by the verified mod without rejecting unrelated entries.
		/// </summary>
		private static ISet<string> GetNormalizedArchiveFiles(IMod mod)
		{
			var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			// GetFileList intentionally hides fomod/** from ordinary mod listings. The XML
			// installer may select real asset files inside that directory; use the format's
			// unfiltered index when available, retaining the same exact-path checks below.
			IModInstallerSourceInventory installerInventory = mod as IModInstallerSourceInventory;
			IEnumerable<string> sourceFiles = installerInventory == null
				? mod.GetFileList()
				: installerInventory.GetInstallerSourceFiles(null, true);
			foreach (string path in sourceFiles)
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

		/// <summary>Resolves FOMOD dot and destination-root notation before validating the canonical relative recipe path.</summary>
		private static string NormalizeFomodPath(ModInstallationRecipePathKind kind, string path, bool allowRoot)
		{
			string normalized = (path ?? String.Empty).Replace('/', '\\');
			if (kind == ModInstallationRecipePathKind.Destination && normalized == "\\")
				return String.Empty;
			if (normalized.StartsWith("\\", StringComparison.Ordinal))
				return CanonicalPath(kind, normalized);

			bool directory = normalized.EndsWith("\\", StringComparison.Ordinal) || normalized.EndsWith("\\.", StringComparison.Ordinal);
			// Remove only current-directory components. Empty interior components, parent traversal, drive/UNC paths
			// and Windows aliases still reach the strict recipe validator and are rejected there.
			string[] parts = normalized.Split('\\');
			for (int index = 0; index < parts.Length - 1; index++)
				if (parts[index].Length == 0) return CanonicalPath(kind, normalized);
			normalized = String.Join("\\", parts.Where(part => part != "."));
			if (normalized.EndsWith("\\", StringComparison.Ordinal))
				normalized = normalized.Substring(0, normalized.Length - 1);
			if (allowRoot && normalized.Length == 0)
				return String.Empty;

			string canonical = CanonicalPath(kind, normalized);
			return directory ? canonical + "\\" : canonical;
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
