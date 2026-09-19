using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.ModManagement.Scripting.XmlScript;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.Plugins;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// Verifies C5.6 exact FOMOD translation and C5.9 additive projected condition-state integration.
	/// </summary>
	[TestFixture]
	public class ModInstallationFomodRecipeAdapterTests
	{
		/// <summary>
		/// Verifies XML script types advertise the native FOMOD adapter without a NexusClient dependency on XmlScript.
		/// </summary>
		[Test]
		public void XmlScriptType_AdvertisesFomodAdapterContract()
		{
			IModInstallationFomodRecipeAdapter adapter = new XmlScriptType() as IModInstallationFomodRecipeAdapter;

			Assert.That(adapter, Is.Not.Null);
			Assert.That(adapter.AdapterId, Is.EqualTo("nmm-ce.native.fomod"));
			Assert.That(adapter.AdapterVersion, Is.EqualTo(1));
			Assert.That(adapter.CapabilityId, Is.EqualTo("fomod-selection"));
			Assert.That(adapter.CapabilityVersion, Is.EqualTo(1));
		}

		/// <summary>
		/// Verifies required, selected, always-install and flag-conditional files become native typed operations in native order.
		/// </summary>
		[Test]
		public void Translate_ExactSelectionsProduceNativeOperationsInFomodOrder()
		{
			XmlScriptType scriptType = new XmlScriptType();
			XmlScript script = new XmlScript(scriptType, new Version(5, 0));
			script.RequiredInstallFiles.Add(File(@"required\core.bin", @"core.bin", 100));

			InstallStep step = new InstallStep("Core", null, SortOrder.Explicit);
			OptionGroup group = new OptionGroup("Variant", OptionGroupType.SelectExactlyOne, SortOrder.Explicit);
			Option optionA = Option("A", OptionType.Optional,
				File(@"textures\a.dds", @"textures\a.dds", 20));
			optionA.Flags.Add(new ConditionalFlag("variant", "A"));
			Option optionB = Option("B", OptionType.Optional,
				new InstallableFile(@"shared\always.txt", @"shared\always.txt", false, 10, true, false));
			group.Options.Add(optionA);
			group.Options.Add(optionB);
			step.OptionGroups.Add(group);
			script.InstallSteps.Add(step);
			script.ConditionallyInstalledFileSets.Add(new ConditionallyInstalledFileSet(
				new FlagCondition("variant", "A"),
				new List<InstallableFile> { File(@"conditional\a.txt", @"conditional\a.txt", 0) }));

			string[] archiveFiles =
			{
				@"required\core.bin",
				@"textures\a.dds",
				@"shared\always.txt",
				@"conditional\a.txt"
			};
			IMod mod = CreateMod(script, archiveFiles);
			ModInstallationFomodSelectionRecipe recipe = Recipe(new Version(5, 0),
				Step(0, "Core", Group(0, "Variant", Selection(0, "A"))));
			IModInstallationFomodRecipeAdapter adapter = scriptType;
			ModInstallationRecipeInput input = CreateInput(adapter,
				PathSource(@"required\core.bin"), PathDestination(@"core.bin"),
				PathSource(@"shared\always.txt"), PathDestination(@"shared\always.txt"),
				PathSource(@"textures\a.dds"), PathDestination(@"textures\a.dds"),
				PathSource(@"conditional\a.txt"), PathDestination(@"conditional\a.txt"));

			ModInstallationRecipeInput translated = adapter.Translate(
				input, mod, CreateGameMode(), CreateEnvironmentInfo(), null, recipe);

			Assert.That(input.HasNativePlan, Is.False);
			Assert.That(translated.HasNativePlan, Is.True);
			InstallModFileOperation[] files = translated.NativeOperations.OfType<InstallModFileOperation>().ToArray();
			Assert.That(files.Length, Is.EqualTo(4));
			Assert.That(files[0].SourcePath, Is.EqualTo(@"required\core.bin"), "Required files stay before option files.");
			Assert.That(files[1].SourcePath, Is.EqualTo(@"shared\always.txt"), "Option files retain native priority sorting.");
			Assert.That(files[2].SourcePath, Is.EqualTo(@"textures\a.dds"));
			Assert.That(files[3].SourcePath, Is.EqualTo(@"conditional\a.txt"), "Conditional files remain after option files.");
			Assert.That(files.All(file => file.DeploymentDecision == null), Is.True, "C5.6 must not pre-resolve deployment decisions.");
		}

		/// <summary>
		/// Verifies duplicate display names remain unambiguous because exact identities also include parsed positions.
		/// </summary>
		[Test]
		public void Translate_DuplicateOptionNamesUseExactParsedIndex()
		{
			XmlScriptType scriptType = new XmlScriptType();
			XmlScript script = new XmlScript(scriptType, new Version(5, 0));
			InstallStep step = new InstallStep("Step", null, SortOrder.Explicit);
			OptionGroup group = new OptionGroup("Choices", OptionGroupType.SelectExactlyOne, SortOrder.Explicit);
			group.Options.Add(Option("Same", OptionType.Optional, File(@"first.bin", @"first.bin", 0)));
			group.Options.Add(Option("Same", OptionType.Optional, File(@"second.bin", @"second.bin", 0)));
			step.OptionGroups.Add(group);
			script.InstallSteps.Add(step);
			IModInstallationFomodRecipeAdapter adapter = scriptType;
			IMod mod = CreateMod(script, @"first.bin", @"second.bin");
			ModInstallationRecipeInput input = CreateInput(adapter, PathSource(@"second.bin"), PathDestination(@"second.bin"));

			ModInstallationRecipeInput translated = adapter.Translate(
				input, mod, CreateGameMode(), CreateEnvironmentInfo(), null,
				Recipe(new Version(5, 0), Step(0, "Step", Group(0, "Choices", Selection(1, "Same")))));

			InstallModFileOperation operation = translated.NativeOperations.OfType<InstallModFileOperation>().Single();
			Assert.That(operation.SourcePath, Is.EqualTo(@"second.bin"));
			Assert.Throws<InvalidDataException>(() => adapter.Translate(
				input, mod, CreateGameMode(), CreateEnvironmentInfo(), null,
				Recipe(new Version(5, 0), Step(0, "Step", Group(0, "Choices", Selection(1, "Stale name"))))));
		}

		/// <summary>
		/// Verifies hidden steps cannot silently contribute selections while an explicitly empty hidden step is accepted.
		/// </summary>
		[Test]
		public void Translate_HiddenStepRejectsSelectionsAndAcceptsExplicitNone()
		{
			XmlScriptType scriptType = new XmlScriptType();
			XmlScript script = new XmlScript(scriptType, new Version(5, 0));
			script.RequiredInstallFiles.Add(File(@"required.bin", @"required.bin", 0));
			InstallStep step = new InstallStep("Hidden", new FlagCondition("show", "yes"), SortOrder.Explicit);
			OptionGroup group = new OptionGroup("Hidden choices", OptionGroupType.SelectAny, SortOrder.Explicit);
			group.Options.Add(Option("Choice", OptionType.Optional, File(@"hidden.bin", @"hidden.bin", 0)));
			step.OptionGroups.Add(group);
			script.InstallSteps.Add(step);
			IModInstallationFomodRecipeAdapter adapter = scriptType;
			IMod mod = CreateMod(script, @"required.bin", @"hidden.bin");
			ModInstallationRecipeInput input = CreateInput(adapter, PathSource(@"required.bin"), PathDestination(@"required.bin"));

			ModInstallationRecipeInput translated = adapter.Translate(
				input, mod, CreateGameMode(), CreateEnvironmentInfo(), null,
				Recipe(new Version(5, 0), Step(0, "Hidden", Group(0, "Hidden choices"))));

			Assert.That(translated.NativeOperations.OfType<InstallModFileOperation>().Single().SourcePath, Is.EqualTo(@"required.bin"));
			Assert.Throws<InvalidDataException>(() => adapter.Translate(
				input, mod, CreateGameMode(), CreateEnvironmentInfo(), null,
				Recipe(new Version(5, 0), Step(0, "Hidden", Group(0, "Hidden choices", Selection(0, "Choice"))))));
		}

		/// <summary>
		/// Verifies exact selection translation enforces native group cardinality and option usability instead of choosing defaults.
		/// </summary>
		[Test]
		public void Translate_RejectsInvalidCardinalityRequiredAndNotUsableSelections()
		{
			XmlScriptType scriptType = new XmlScriptType();
			IModInstallationFomodRecipeAdapter adapter = scriptType;

			XmlScript exactOne = ScriptWithSingleGroup(scriptType, OptionGroupType.SelectExactlyOne,
				Option("Only", OptionType.Optional, File(@"only.bin", @"only.bin", 0)));
			Assert.Throws<InvalidDataException>(() => adapter.Translate(
				CreateInput(adapter), CreateMod(exactOne, @"only.bin"), CreateGameMode(), CreateEnvironmentInfo(), null,
				Recipe(new Version(5, 0), Step(0, "Step", Group(0, "Group")))));

			XmlScript required = ScriptWithSingleGroup(scriptType, OptionGroupType.SelectAny,
				Option("Required", OptionType.Required, File(@"required.bin", @"required.bin", 0)));
			Assert.Throws<InvalidDataException>(() => adapter.Translate(
				CreateInput(adapter), CreateMod(required, @"required.bin"), CreateGameMode(), CreateEnvironmentInfo(), null,
				Recipe(new Version(5, 0), Step(0, "Step", Group(0, "Group")))));

			XmlScript unusable = ScriptWithSingleGroup(scriptType, OptionGroupType.SelectAny,
				Option("Unsafe", OptionType.NotUsable, File(@"unsafe.bin", @"unsafe.bin", 0)));
			Assert.Throws<DependencyException>(() => adapter.Translate(
				CreateInput(adapter), CreateMod(unusable, @"unsafe.bin"), CreateGameMode(), CreateEnvironmentInfo(), null,
				Recipe(new Version(5, 0), Step(0, "Step", Group(0, "Group", Selection(0, "Unsafe"))))));
		}

		/// <summary>
		/// Verifies stale script identity and adapter/capability versions fail closed before a native plan is attached.
		/// </summary>
		[Test]
		public void Translate_RejectsStaleScriptOrAdapterContract()
		{
			XmlScriptType scriptType = new XmlScriptType();
			XmlScript script = ScriptWithSingleGroup(scriptType, OptionGroupType.SelectAny,
				Option("Choice", OptionType.Optional, File(@"choice.bin", @"choice.bin", 0)));
			IMod mod = CreateMod(script, @"choice.bin");
			IModInstallationFomodRecipeAdapter adapter = scriptType;
			ModInstallationFomodSelectionRecipe recipe = Recipe(new Version(5, 0),
				Step(0, "Step", Group(0, "Group", Selection(0, "Choice"))));

			Assert.Throws<InvalidDataException>(() => adapter.Translate(
				CreateInput(adapter, PathSource(@"choice.bin"), PathDestination(@"choice.bin")), mod,
				CreateGameMode(), CreateEnvironmentInfo(), null,
				Recipe(new Version(4, 0), Step(0, "Step", Group(0, "Group", Selection(0, "Choice"))))));
			Assert.Throws<NotSupportedException>(() => adapter.Translate(
				CreateInput("other.adapter", 1, adapter.CapabilityId, 1,
					PathSource(@"choice.bin"), PathDestination(@"choice.bin")), mod,
				CreateGameMode(), CreateEnvironmentInfo(), null, recipe));
			Assert.Throws<NotSupportedException>(() => adapter.Translate(
				CreateInput(adapter.AdapterId, adapter.AdapterVersion, adapter.CapabilityId, 2,
					PathSource(@"choice.bin"), PathDestination(@"choice.bin")), mod,
				CreateGameMode(), CreateEnvironmentInfo(), null, recipe));
		}

		/// <summary>
		/// Verifies folder entries expand only to actual archive children and consume their exact C5.3 source/destination paths.
		/// </summary>
		[Test]
		public void Translate_FolderSelectionExpandsActualArchiveFiles()
		{
			XmlScriptType scriptType = new XmlScriptType();
			XmlScript script = new XmlScript(scriptType, new Version(5, 0));
			script.RequiredInstallFiles.Add(new InstallableFile(@"folder", @"target", true, 0, false, false));
			IModInstallationFomodRecipeAdapter adapter = scriptType;
			IMod mod = CreateMod(script, @"folder\one.bin", @"folder\sub\two.bin", @"other\ignored.bin");
			ModInstallationRecipeInput input = CreateInput(adapter,
				PathSource(@"folder\one.bin"), PathDestination(@"target\one.bin"),
				PathSource(@"folder\sub\two.bin"), PathDestination(@"target\sub\two.bin"));

			ModInstallationRecipeInput translated = adapter.Translate(
				input, mod, CreateGameMode(), CreateEnvironmentInfo(), null,
				new ModInstallationFomodSelectionRecipe(new Version(5, 0), new ModInstallationFomodStepSelection[0]));

			InstallModFileOperation[] files = translated.NativeOperations.OfType<InstallModFileOperation>().ToArray();
			Assert.That(files.Select(file => file.SourcePath), Is.EqualTo(new[] { @"folder\one.bin", @"folder\sub\two.bin" }));
			Assert.That(files.Select(file => file.DestinationPath), Is.EqualTo(new[] { @"target\one.bin", @"target\sub\two.bin" }));
		}

		/// <summary>
		/// Verifies translation consumes exactly the C5.3 path set and cannot silently add or ignore a FOMOD file effect.
		/// </summary>
		[Test]
		public void Translate_RejectsMissingOrExtraValidatedPaths()
		{
			XmlScriptType scriptType = new XmlScriptType();
			XmlScript script = new XmlScript(scriptType, new Version(5, 0));
			script.RequiredInstallFiles.Add(File(@"required.bin", @"required.bin", 0));
			IModInstallationFomodRecipeAdapter adapter = scriptType;
			IMod mod = CreateMod(script, @"required.bin");
			ModInstallationFomodSelectionRecipe recipe = new ModInstallationFomodSelectionRecipe(
				new Version(5, 0), new ModInstallationFomodStepSelection[0]);

			Assert.Throws<InvalidDataException>(() => adapter.Translate(
				CreateInput(adapter, PathSource(@"required.bin")), mod,
				CreateGameMode(), CreateEnvironmentInfo(), null, recipe));
			Assert.Throws<InvalidDataException>(() => adapter.Translate(
				CreateInput(adapter, PathSource(@"required.bin"), PathDestination(@"required.bin"),
					PathDestination(@"extra.bin")), mod,
				CreateGameMode(), CreateEnvironmentInfo(), null, recipe));
		}

		/// <summary>
		/// Verifies translation preserves native XML activation intent without querying file-dependent activatability too early.
		/// </summary>
		[Test]
		public void Translate_PreservesPluginActivationIntentUntilNativeExecution()
		{
			XmlScriptType scriptType = new XmlScriptType();
			XmlScript script = new XmlScript(scriptType, new Version(5, 0));
			script.RequiredInstallFiles.Add(File(@"docs\readme.txt", @"readme.txt", 0));
			InstallStep step = new InstallStep("Step", null, SortOrder.Explicit);
			OptionGroup group = new OptionGroup("Group", OptionGroupType.SelectAny, SortOrder.Explicit);
			group.Options.Add(Option("Plugin", OptionType.Optional, File(@"plugins\choice.esp", @"choice.esp", 0)));
			step.OptionGroups.Add(group);
			script.InstallSteps.Add(step);
			IModInstallationFomodRecipeAdapter adapter = scriptType;
			IMod mod = CreateMod(script, @"docs\readme.txt", @"plugins\choice.esp");
			int activationQueries = 0;
			IPluginManager pluginManager = InterfaceStub<IPluginManager>.Create((method, args) =>
			{
				if (method.Name == "IsActivatiblePluginFile")
					activationQueries++;
				return null;
			});
			ModInstallationRecipeInput input = CreateInput(adapter,
				PathSource(@"docs\readme.txt"), PathDestination(@"readme.txt"),
				PathSource(@"plugins\choice.esp"), PathDestination(@"choice.esp"));

			ModInstallationRecipeInput translated = adapter.Translate(
				input, mod, CreateGameMode(), CreateEnvironmentInfo(), pluginManager,
				Recipe(new Version(5, 0), Step(0, "Step", Group(0, "Group", Selection(0, "Plugin")))));

			SetPluginActivationOperation[] activations = translated.NativeOperations.OfType<SetPluginActivationOperation>().ToArray();
			Assert.That(activationQueries, Is.EqualTo(0), "Activatability can depend on the file just installed and must be checked by the native executor.");
			Assert.That(activations.Select(operation => operation.PluginPath), Is.EqualTo(new[] { @"readme.txt", @"choice.esp" }));
			Assert.That(activations.All(operation => operation.RequireActivatablePlugin), Is.True);
		}

		/// <summary>
		/// Verifies an unselected plugin that is still installed by FOMOD rules preserves native deactivation intent.
		/// </summary>
		[Test]
		public void Translate_AlwaysInstallUnselectedPluginQueuesGuardedDeactivation()
		{
			XmlScriptType scriptType = new XmlScriptType();
			XmlScript script = new XmlScript(scriptType, new Version(5, 0));
			InstallStep step = new InstallStep("Step", null, SortOrder.Explicit);
			OptionGroup group = new OptionGroup("Group", OptionGroupType.SelectAny, SortOrder.Explicit);
			group.Options.Add(Option("Optional plugin", OptionType.Optional,
				new InstallableFile(@"plugins\optional.esp", @"optional.esp", false, 0, true, false)));
			step.OptionGroups.Add(group);
			script.InstallSteps.Add(step);
			IModInstallationFomodRecipeAdapter adapter = scriptType;
			IPluginManager pluginManager = InterfaceStub<IPluginManager>.Create((method, args) => null);

			ModInstallationRecipeInput translated = adapter.Translate(
				CreateInput(adapter, PathSource(@"plugins\optional.esp"), PathDestination(@"optional.esp")),
				CreateMod(script, @"plugins\optional.esp"), CreateGameMode(), CreateEnvironmentInfo(), pluginManager,
				Recipe(new Version(5, 0), Step(0, "Step", Group(0, "Group"))));

			SetPluginActivationOperation activation = translated.NativeOperations.OfType<SetPluginActivationOperation>().Single();
			Assert.That(activation.PluginPath, Is.EqualTo(@"optional.esp"));
			Assert.That(activation.Activate, Is.False);
			Assert.That(activation.RequireActivatablePlugin, Is.True);
		}

		/// <summary>
		/// Verifies the C5.5 generic native executor applies the XML activatability guard after the preceding file operation.
		/// </summary>
		[Test]
		public void NativeExecutor_PluginActivatabilityIsCheckedAfterFileExecution()
		{
			using (TemporaryDirectory tmp = new TemporaryDirectory())
			{
				var context = new ScriptProxyContext(tmp.Path, null, false, false, "linked");
				context.ActivatablePluginResult = false;
				var executor = new ImmediateScriptedInstallOperationExecutor(
					context.Mod, context.GameMode, null, context.VirtualModActivator,
					context.VirtualModActivator.GetModLinkInstaller(), context.Installers, null, null);

				Assert.That(executor.Execute(new InstallModFileOperation(@"Plugin\Test.esp", @"Test.esp")), Is.True);
				Assert.That(executor.Execute(new SetPluginActivationOperation(@"Test.esp", true, true)), Is.True);

				Assert.That(context.PluginActivationQueryInstallCallCount, Is.EqualTo(1));
				Assert.That(context.LastPluginActivationPath, Is.Null, "The guarded FOMOD activation must be skipped when native plugin management rejects the installed path.");
			}
		}

		/// <summary>
		/// Verifies the FOMOD execution guard is opt-in and does not change legacy scripted activation semantics.
		/// </summary>
		[Test]
		public void NativeExecutor_LegacyActivationOperationDoesNotAddFomodGuard()
		{
			using (TemporaryDirectory tmp = new TemporaryDirectory())
			{
				var context = new ScriptProxyContext(tmp.Path, null, false, false, "linked");
				context.ActivatablePluginResult = false;
				var executor = new ImmediateScriptedInstallOperationExecutor(
					context.Mod, context.GameMode, null, context.VirtualModActivator,
					context.VirtualModActivator.GetModLinkInstaller(), context.Installers, null, null);

				Assert.That(executor.Execute(new SetPluginActivationOperation(@"Legacy.esp", true)), Is.True);

				Assert.That(context.LastPluginActivationPath, Is.EqualTo("adjusted:Legacy.esp"));
				Assert.That(context.LastPluginActivationState, Is.True);
			}
		}

		/// <summary>
		/// Verifies additive projection preserves unrelated active plugins from the current native environment.
		/// </summary>
		[Test]
		public void Translate_ConditionalFilesObserveExistingAdditivePluginState()
		{
			XmlScriptType scriptType = new XmlScriptType();
			XmlScript script = new XmlScript(scriptType, new Version(5, 0));
			script.RequiredInstallFiles.Add(File(@"base.bin", @"base.bin", 0));
			script.ConditionallyInstalledFileSets.Add(new ConditionallyInstalledFileSet(
				new PluginCondition(@"existing.esp", PluginState.Active),
				new List<InstallableFile> { File(@"conditional.bin", @"conditional.bin", 0) }));
			IModInstallationFomodRecipeAdapter adapter = scriptType;
			IGameMode gameMode = CreateGameMode();
			IPluginManager pluginManager = CreatePluginManager(gameMode, new[] { @"existing.esp" }, new[] { @"existing.esp" });

			ModInstallationRecipeInput translated = adapter.Translate(
				CreateInput(adapter,
					PathSource(@"base.bin"), PathDestination(@"base.bin"),
					PathSource(@"conditional.bin"), PathDestination(@"conditional.bin")),
				CreateMod(script, @"base.bin", @"conditional.bin"), gameMode, CreateEnvironmentInfo(), pluginManager,
				new ModInstallationFomodSelectionRecipe(new Version(5, 0), new ModInstallationFomodStepSelection[0]));

			Assert.That(translated.NativeOperations.OfType<InstallModFileOperation>().Select(operation => operation.SourcePath),
				Is.EqualTo(new[] { @"base.bin", @"conditional.bin" }));
		}

		/// <summary>
		/// Verifies selected plugin files become visible to later FOMOD conditional file sets before native execution.
		/// </summary>
		[Test]
		public void Translate_ConditionalFilesObserveProjectedSelectedPluginState()
		{
			XmlScriptType scriptType = new XmlScriptType();
			XmlScript script = new XmlScript(scriptType, new Version(5, 0));
			InstallStep step = new InstallStep("Step", null, SortOrder.Explicit);
			OptionGroup group = new OptionGroup("Group", OptionGroupType.SelectExactlyOne, SortOrder.Explicit);
			group.Options.Add(Option("Plugin", OptionType.Optional, File(@"plugins\choice.esp", @"choice.esp", 0)));
			step.OptionGroups.Add(group);
			script.InstallSteps.Add(step);
			script.ConditionallyInstalledFileSets.Add(new ConditionallyInstalledFileSet(
				new PluginCondition(@"choice.esp", PluginState.Active),
				new List<InstallableFile> { File(@"conditional.bin", @"conditional.bin", 0) }));
			IModInstallationFomodRecipeAdapter adapter = scriptType;
			IGameMode gameMode = CreateGameMode(new ScriptedInstallerTestPluginFactory());
			IPluginManager pluginManager = CreatePluginManager(gameMode, new string[0], new string[0]);

			ModInstallationRecipeInput translated = adapter.Translate(
				CreateInput(adapter,
					PathSource(@"plugins\choice.esp"), PathDestination(@"choice.esp"),
					PathSource(@"conditional.bin"), PathDestination(@"conditional.bin")),
				CreateMod(script, @"plugins\choice.esp", @"conditional.bin"), gameMode, CreateEnvironmentInfo(), pluginManager,
				Recipe(new Version(5, 0), Step(0, "Step", Group(0, "Group", Selection(0, "Plugin")))));

			Assert.That(translated.NativeOperations.OfType<InstallModFileOperation>().Select(operation => operation.SourcePath),
				Is.EqualTo(new[] { @"plugins\choice.esp", @"conditional.bin" }));
		}

		/// <summary>
		/// Verifies an earlier conditional plugin output is projected before the next conditional set is evaluated.
		/// </summary>
		[Test]
		public void Translate_ConditionalFileSetsObserveEarlierProjectedConditionalOutputs()
		{
			XmlScriptType scriptType = new XmlScriptType();
			XmlScript script = new XmlScript(scriptType, new Version(5, 0));
			script.ConditionallyInstalledFileSets.Add(new ConditionallyInstalledFileSet(
				new GameVersionCondition(new Version(0, 0)),
				new List<InstallableFile> { File(@"gate.esp", @"gate.esp", 0) }));
			script.ConditionallyInstalledFileSets.Add(new ConditionallyInstalledFileSet(
				new PluginCondition(@"gate.esp", PluginState.Active),
				new List<InstallableFile> { File(@"after.bin", @"after.bin", 0) }));
			IModInstallationFomodRecipeAdapter adapter = scriptType;
			IGameMode gameMode = CreateGameMode(new ScriptedInstallerTestPluginFactory(), new Version(1, 0));
			IPluginManager pluginManager = CreatePluginManager(gameMode, new string[0], new string[0]);

			ModInstallationRecipeInput translated = adapter.Translate(
				CreateInput(adapter,
					PathSource(@"gate.esp"), PathDestination(@"gate.esp"),
					PathSource(@"after.bin"), PathDestination(@"after.bin")),
				CreateMod(script, @"gate.esp", @"after.bin"), gameMode, CreateEnvironmentInfo(), pluginManager,
				new ModInstallationFomodSelectionRecipe(new Version(5, 0), new ModInstallationFomodStepSelection[0]));

			Assert.That(translated.NativeOperations.OfType<InstallModFileOperation>().Select(operation => operation.SourcePath),
				Is.EqualTo(new[] { @"gate.esp", @"after.bin" }));
		}

		/// <summary>
		/// Verifies FOMOD selection value objects snapshot caller collections and expose immutable public properties only.
		/// </summary>
		[Test]
		public void Contract_IsImmutableAndSnapshotsCallerSelections()
		{
			var options = new List<ModInstallationFomodOptionSelection> { Selection(0, "A") };
			var group = new ModInstallationFomodGroupSelection(0, "Group", options);
			var groups = new List<ModInstallationFomodGroupSelection> { group };
			var step = new ModInstallationFomodStepSelection(0, "Step", groups);
			var steps = new List<ModInstallationFomodStepSelection> { step };
			var recipe = new ModInstallationFomodSelectionRecipe(new Version(5, 0), steps);
			options.Clear();
			groups.Clear();
			steps.Clear();

			Assert.That(group.SelectedOptions.Count, Is.EqualTo(1));
			Assert.That(step.Groups.Count, Is.EqualTo(1));
			Assert.That(recipe.Steps.Count, Is.EqualTo(1));
			Type[] types =
			{
				typeof(ModInstallationFomodOptionSelection),
				typeof(ModInstallationFomodGroupSelection),
				typeof(ModInstallationFomodStepSelection),
				typeof(ModInstallationFomodSelectionRecipe)
			};
			foreach (Type type in types)
			{
				Assert.That(type.GetProperties().Any(property => property.SetMethod != null && property.SetMethod.IsPublic),
					Is.False, type.FullName);
			}
		}

		/// <summary>
		/// Creates a script with one visible step/group and the supplied options.
		/// </summary>
		private static XmlScript ScriptWithSingleGroup(XmlScriptType scriptType, OptionGroupType groupType, params Option[] options)
		{
			var script = new XmlScript(scriptType, new Version(5, 0));
			var step = new InstallStep("Step", null, SortOrder.Explicit);
			var group = new OptionGroup("Group", groupType, SortOrder.Explicit);
			foreach (Option option in options)
				group.Options.Add(option);
			step.OptionGroups.Add(group);
			script.InstallSteps.Add(step);
			return script;
		}

		/// <summary>
		/// Creates one parsed XML option with the supplied static type and files.
		/// </summary>
		private static Option Option(string name, OptionType type, params InstallableFile[] files)
		{
			var option = new Option(name, String.Empty, null, new StaticOptionTypeResolver(type));
			option.Files.AddRange(files);
			return option;
		}

		/// <summary>
		/// Creates one parsed XML file mapping.
		/// </summary>
		private static InstallableFile File(string source, string destination, int priority)
		{
			return new InstallableFile(source, destination, false, priority, false, false);
		}

		/// <summary>
		/// Creates one immutable FOMOD option selection.
		/// </summary>
		private static ModInstallationFomodOptionSelection Selection(int index, string name)
		{
			return new ModInstallationFomodOptionSelection(index, name);
		}

		/// <summary>
		/// Creates one immutable FOMOD group selection.
		/// </summary>
		private static ModInstallationFomodGroupSelection Group(int index, string name,
			params ModInstallationFomodOptionSelection[] selectedOptions)
		{
			return new ModInstallationFomodGroupSelection(index, name, selectedOptions);
		}

		/// <summary>
		/// Creates one immutable FOMOD step selection.
		/// </summary>
		private static ModInstallationFomodStepSelection Step(int index, string name,
			params ModInstallationFomodGroupSelection[] groups)
		{
			return new ModInstallationFomodStepSelection(index, name, groups);
		}

		/// <summary>
		/// Creates one immutable FOMOD selection recipe.
		/// </summary>
		private static ModInstallationFomodSelectionRecipe Recipe(Version version,
			params ModInstallationFomodStepSelection[] steps)
		{
			return new ModInstallationFomodSelectionRecipe(version, steps);
		}

		/// <summary>
		/// Creates a validated FOMOD recipe input using one discovered native adapter contract.
		/// </summary>
		private static ModInstallationRecipeInput CreateInput(IModInstallationFomodRecipeAdapter adapter,
			params ModInstallationRecipePath[] paths)
		{
			return CreateInput(adapter.AdapterId, adapter.AdapterVersion, adapter.CapabilityId, adapter.CapabilityVersion, paths);
		}

		/// <summary>
		/// Creates a validated FOMOD recipe input using explicit adapter/capability metadata.
		/// </summary>
		private static ModInstallationRecipeInput CreateInput(string adapterId, int adapterVersion,
			string capabilityId, int capabilityVersion, params ModInstallationRecipePath[] paths)
		{
			var installContext = new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data);
			var operation = ModOperationIdentity.CreateNew(
				ModOperationOrigin.Collection,
				new ModOperationFingerprint("target-sha256:" + new string('a', 64), installContext, "recipe:c5.6-fomod-v1"));
			var validation = new ModInstallationRecipeValidation(
				adapterId,
				adapterVersion,
				installContext,
				new ModInstallationRecipeExpectedContent(new string('b', 64), 4096),
				new[] { new ModInstallationRecipeCapability(capabilityId, capabilityVersion) },
				paths ?? new ModInstallationRecipePath[0]);
			return new ModInstallationRecipeInput(operation, validation);
		}

		/// <summary>
		/// Creates one validated archive-source path.
		/// </summary>
		private static ModInstallationRecipePath PathSource(string path)
		{
			return new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, path);
		}

		/// <summary>
		/// Creates one validated destination path.
		/// </summary>
		private static ModInstallationRecipePath PathDestination(string path)
		{
			return new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, path);
		}

		/// <summary>
		/// Creates a lightweight mod exposing the supplied actual XML script and archive file list.
		/// </summary>
		private static IMod CreateMod(XmlScript script, params string[] archiveFiles)
		{
			var files = new List<string>(archiveFiles ?? new string[0]);
			return InterfaceStub<IMod>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_HasInstallScript":
						return true;
					case "get_InstallScript":
						return script;
					case "get_Format":
						return null;
					case "get_Filename":
						return "FomodFixture.7z";
					case "GetFileList":
						if (args.Length == 0)
							return new List<string>(files);
						string folder = ((string)args[0]).Replace('/', '\\').TrimEnd('\\') + "\\";
						return files.Where(file => file.Replace('/', '\\').StartsWith(folder, StringComparison.OrdinalIgnoreCase)).ToList();
					case "GetFile":
						string requested = ((string)args[0]).Replace('/', '\\');
						return files.Any(file => String.Equals(file.Replace('/', '\\'), requested, StringComparison.OrdinalIgnoreCase))
							? new byte[] { 1, 2, 3 }
							: null;
					default:
						return null;
				}
			});
		}

		/// <summary>
		/// Creates a lightweight game mode preserving plugin-path normalization used by XML conditions and activation checks.
		/// </summary>
		private static IGameMode CreateGameMode(IPluginFactory pluginFactory = null, Version gameVersion = null)
		{
			string installationPath = Path.Combine(Path.GetTempPath(), "NMMCE-FomodProjection");
			IGameModeEnvironmentInfo environment = InterfaceStub<IGameModeEnvironmentInfo>.Create((method, args) =>
			{
				if (method.Name == "get_InstallationPath")
					return installationPath;
				return null;
			});

			return InterfaceStub<IGameMode>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_GameModeEnvironmentInfo":
						return environment;
					case "get_InstallationPath":
						return installationPath;
					case "get_GameVersion":
						return gameVersion ?? new Version(1, 0);
					case "get_PluginExtensions":
						return new[] { ".esp", ".esm", ".esl" };
					case "GetPluginFactory":
						return pluginFactory;
					case "GetModFormatAdjustedPath":
						string path = (string)args[1];
						return path == null ? String.Empty : "adjusted:" + path;
					default:
						return null;
				}
			});
		}

		/// <summary>
		/// Creates an isolated plugin manager snapshot for additive FOMOD projection tests.
		/// </summary>
		private static IPluginManager CreatePluginManager(IGameMode gameMode, IEnumerable<string> managedPlugins,
			IEnumerable<string> activePlugins)
		{
			string installationPath = gameMode.GameModeEnvironmentInfo.InstallationPath;
			var managed = new ThreadSafeObservableList<Plugin>((managedPlugins ?? new string[0])
				.Select(path => new Plugin(Path.Combine(installationPath, path), String.Empty, null)));
			var active = new ThreadSafeObservableList<Plugin>((activePlugins ?? new string[0])
				.Select(path => new Plugin(Path.Combine(installationPath, path), String.Empty, null)));
			var managedReadOnly = new ReadOnlyObservableList<Plugin>(managed);
			var activeReadOnly = new ReadOnlyObservableList<Plugin>(active);

			return InterfaceStub<IPluginManager>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_ManagedPlugins":
						return managedReadOnly;
					case "get_ActivePlugins":
						return activeReadOnly;
					case "CanChangeActiveState":
					case "CanChangePluginOrder":
						return true;
					default:
						return null;
				}
			});
		}

		/// <summary>
		/// Creates a neutral environment contract for FOMOD conditions which do not require application-version checks.
		/// </summary>
		private static IEnvironmentInfo CreateEnvironmentInfo()
		{
			return InterfaceStub<IEnvironmentInfo>.Create((method, args) => null);
		}
	}
}
