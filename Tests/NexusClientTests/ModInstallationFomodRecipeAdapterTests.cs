using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Linq;
using Nexus.Client;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.ModManagement.Scripting.XmlScript;
using Nexus.Client.Mods;
using Nexus.Client.Mods.Formats.FOMod;
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
		/// <summary>Serializes an unset module title as empty text, matching the default HeaderInfo contract.</summary>
		[TestCase(null)]
		[TestCase("")]
		[TestCase("Example")]
		public void SaveScript_DefaultHeaderTitleDoesNotThrow(string title)
		{
			var scriptType = new XmlScriptType();
			var script = new XmlScript(scriptType, new Version(5, 0));
			script.HeaderInfo.Title = title;
			System.Xml.Linq.XDocument xml = System.Xml.Linq.XDocument.Parse(scriptType.SaveScript(script));
			Assert.That(xml.Root.Element("moduleName").Value, Is.EqualTo(title ?? String.Empty));
		}

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
		/// Verifies orchestration can derive both the actual FOMOD version and the exact path trust boundary from native translation.
		/// </summary>
		[Test]
		public void Adapter_ExposesActualScriptVersionAndExactValidationPaths()
		{
			XmlScriptType scriptType = new XmlScriptType();
			XmlScript script = ScriptWithSingleGroup(scriptType, OptionGroupType.SelectExactlyOne,
				Option("4k With All Locations", OptionType.Optional, File(@"textures\4k.dds", @"textures\map.dds", 0)),
				Option("2k", OptionType.Optional, File(@"textures\2k.dds", @"textures\map.dds", 0)));
			IModInstallationFomodRecipePlanningAdapter adapter = scriptType;
			IMod mod = CreateMod(script, @"textures\4k.dds", @"textures\2k.dds");
			ModInstallationFomodSelectionRecipe recipe = Recipe(new Version(5, 0),
				Step(0, "Step", Group(0, "Group", Selection(0, "4k With All Locations"))));

			Assert.That(adapter.GetScriptVersion(mod), Is.EqualTo(new Version(5, 0)));
			IReadOnlyList<ModInstallationRecipePath> paths = adapter.GetValidationPaths(
				mod, CreateGameMode(), CreateEnvironmentInfo(), null, recipe);

			Assert.That(paths.Select(path => path.Kind), Is.EqualTo(new[]
			{
				ModInstallationRecipePathKind.ArchiveSource,
				ModInstallationRecipePathKind.Destination
			}));
			Assert.That(paths.Select(path => path.Path), Is.EqualTo(new[] { @"textures\4k.dds", @"textures\map.dds" }));

			ModInstallationRecipeInput translated = adapter.Translate(CreateInput(adapter, paths.ToArray()), mod,
				CreateGameMode(), CreateEnvironmentInfo(), null, recipe);
			InstallModFileOperation file = translated.NativeOperations.OfType<InstallModFileOperation>().Single();
			Assert.That(file.SourcePath, Is.EqualTo(@"textures\4k.dds"));
			Assert.That(file.DestinationPath, Is.EqualTo(@"textures\map.dds"));
		}

		/// <summary>Recorded groups survive an inserted optional group, with new choices remaining unselected; required additions remain actionable.</summary>
		[TestCase(OptionGroupType.SelectAny, true, true)]
		[TestCase(OptionGroupType.SelectAtMostOne, true, true)]
		[TestCase(OptionGroupType.SelectExactlyOne, true, false)]
		[TestCase(OptionGroupType.SelectAny, false, false)]
		public void Prepare_RecordedChoicesAllowOnlyUnselectedOptionalGroupAdditions(OptionGroupType newGroupType, bool honorRecorded, bool succeeds)
		{
			XmlScriptType adapter = new XmlScriptType();
			XmlScript script = new XmlScript(adapter, new Version(5, 0));
			var step = new InstallStep("Installation", null, SortOrder.Explicit);
			var added = new OptionGroup("Remove Fake Glow", newGroupType, SortOrder.Explicit);
			added.Options.Add(Option("Remove Candle Glow", OptionType.Optional, File("extra.bin", "extra.bin", 0)));
			var retained = new OptionGroup("Custom Textures", OptionGroupType.SelectExactlyOne, SortOrder.Explicit);
			retained.Options.Add(Option("No", OptionType.Optional, File("saved.bin", "saved.bin", 0)));
			step.OptionGroups.Add(added);
			step.OptionGroups.Add(retained);
			script.InstallSteps.Add(step);
			var recipe = new ModInstallationFomodSelectionRecipe(new Version(5, 0),
				new[] { Step(0, "Installation", Group(0, "Custom Textures", Selection(0, "No"))) }, honorRecorded);
			Func<ModInstallationRecipeInput> prepare = () => adapter.Prepare(CreateMod(script, "extra.bin", "saved.bin"),
				CreateGameMode(), CreateEnvironmentInfo(), null, recipe, null, paths => CreateInput(adapter, paths.ToArray()));
			if (!succeeds) Assert.Throws<InvalidDataException>(() => prepare());
			else
			{
				ModInstallationRecipeInput translated = prepare();
				Assert.That(translated.NativeOperations.OfType<InstallModFileOperation>().Select(x => x.SourcePath), Is.EqualTo(new[] { "saved.bin" }));
			}
		}

		/// <summary>Missing or renamed selected options are never guessed even when replaying recorded choices.</summary>
		[Test]
		public void Prepare_RecordedChoicesRejectRenamedSelectedOption()
		{
			XmlScriptType adapter = new XmlScriptType();
			XmlScript script = ScriptWithSingleGroup(adapter, OptionGroupType.SelectExactlyOne,
				Option("Standard", OptionType.Optional, File("standard.bin", "standard.bin", 0)));
			var recipe = new ModInstallationFomodSelectionRecipe(new Version(5, 0),
				new[] { Step(0, "Step", Group(0, "Group", Selection(0, "ESP + Master"))) }, true);
			Assert.Throws<InvalidDataException>(() => adapter.Prepare(CreateMod(script, "standard.bin"), CreateGameMode(),
				CreateEnvironmentInfo(), null, recipe, null, paths => CreateInput(adapter, paths.ToArray())));
		}

		/// <summary>Single-pass preparation obtains archive inventory once and still rejects an incomplete validation envelope.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void Prepare_EvaluatesOnePlanAndValidatesItsEnvelope(bool omitPath)
		{
			XmlScriptType adapter = new XmlScriptType();
			XmlScript script = new XmlScript(adapter, new Version(5, 0));
			script.RequiredInstallFiles.Add(File("saved.bin", "saved.bin", 0));
			int inventoryReads = 0;
			IMod mod = InterfaceStub<IMod>.Create((method, args) =>
			{
				if (method.Name == "get_HasInstallScript") return true;
				if (method.Name == "get_InstallScript") return script;
				if (method.Name == "get_Filename") return "fixture.zip";
				if (method.Name == "GetFileList") { inventoryReads++; return new List<string> { "saved.bin" }; }
				return null;
			});
			Func<ModInstallationRecipeInput> prepare = () => adapter.Prepare(mod, CreateGameMode(), CreateEnvironmentInfo(),
				null, Recipe(new Version(5, 0)), null, paths => CreateInput(adapter, omitPath ? new ModInstallationRecipePath[0] : paths.ToArray()));
			if (omitPath) Assert.Throws<InvalidDataException>(() => prepare());
			else Assert.That(prepare().NativeOperations.OfType<InstallModFileOperation>().Single().SourcePath, Is.EqualTo("saved.bin"));
			Assert.That(inventoryReads, Is.EqualTo(1));
		}

		/// <summary>
		/// An XML installer may reference real assets under fomod/options even though the
		/// ordinary mod-file inventory hides them as part of its metadata filter.
		/// </summary>
		[Test]
		public void Translate_FomodOptionsAssetHiddenFromNormalListingIsStillInstallable()
		{
			const string source = @"fomod\options\01-EyesHumanLashesTEOBThin.dds";
			const string destination = @"textures\humanlashes.dds";
			XmlScriptType scriptType = new XmlScriptType();
			XmlScript script = new XmlScript(scriptType, new Version(5, 0));
			script.RequiredInstallFiles.Add(File(source, destination, 0));
			IMod mod = CreateModWithHiddenFomodAssets(script, source);
			Assert.That(mod.GetFileList(), Does.Not.Contain(source));
			IModInstallationFomodRecipePlanningAdapter adapter = scriptType;
			ModInstallationFomodSelectionRecipe selection = Recipe(new Version(5, 0));

			IReadOnlyList<ModInstallationRecipePath> paths = adapter.GetValidationPaths(
				mod, CreateGameMode(), CreateEnvironmentInfo(), null, selection);
			Assert.That(paths.Select(p => p.Path), Is.EqualTo(new[] { source, destination }));
			ModInstallationRecipeInput installed = adapter.Translate(
				CreateInput(adapter, paths.ToArray()), mod, CreateGameMode(), CreateEnvironmentInfo(), null, selection);
			Assert.That(installed.NativeOperations.OfType<InstallModFileOperation>().Single().SourcePath, Is.EqualTo(source));
		}

		/// <summary>File entries can target a directory while validation and replay still name the exact installed file.</summary>
		[TestCase(@"textures\terrain\blackreach\")]
		[TestCase("textures/terrain/blackreach/")]
		public void Translate_FileDestinationDirectoryAppendsSourceFileName(string destination)
		{
			const string source = @"Backgrounds\stairs\back.dds";
			const string expected = @"textures\terrain\blackreach\back.dds";
			XmlScriptType scriptType = new XmlScriptType();
			XmlScript script = new XmlScript(scriptType, new Version(5, 0));
			script.RequiredInstallFiles.Add(File(source, destination, 1));
			IMod mod = CreateMod(script, source);
			IModInstallationFomodRecipePlanningAdapter adapter = scriptType;
			ModInstallationFomodSelectionRecipe selection = Recipe(new Version(5, 0));
			IReadOnlyList<ModInstallationRecipePath> paths = adapter.GetValidationPaths(mod, CreateGameMode(), CreateEnvironmentInfo(), null, selection);
			Assert.That(paths.Single(x => x.Kind == ModInstallationRecipePathKind.Destination).Path, Is.EqualTo(expected));
			ModInstallationRecipeInput translated = adapter.Translate(CreateInput(adapter, paths.ToArray()), mod,
				CreateGameMode(), CreateEnvironmentInfo(), null, selection);
			Assert.That(translated.NativeOperations.OfType<InstallModFileOperation>().Single().DestinationPath, Is.EqualTo(expected));
		}

		/// <summary>Collection translation resolves FOMOD path spelling before both validation and plugin activation.</summary>
		[TestCase(@"FSMPM\FSMPM - The FSMP MCM.esp", @".\FSMPM - The FSMP MCM.esp", "FSMPM - The FSMP MCM.esp")]
		[TestCase(@".\plugins\choice.esp", @".\", "choice.esp")]
		[TestCase(@"plugins\.\choice.esp", @"\", "choice.esp")]
		[TestCase(@"plugins\choice.esp", "/", "choice.esp")]
		[TestCase(@"plugins\choice.esp", @".\output\", @"output\choice.esp")]
		[TestCase(@"plugins\choice.esp", @"output\.", @"output\choice.esp")]
		public void Prepare_FomodFilePathsProduceCanonicalDeploymentAndActivation(string source, string destination, string expected)
		{
			XmlScriptType adapter = new XmlScriptType();
			var script = new XmlScript(adapter, new Version(5, 0));
			script.RequiredInstallFiles.Add(File(source, destination, 0));
			string archiveSource = source.Replace(@".\", String.Empty);
			IMod mod = CreateMod(script, archiveSource);
			IGameMode game = CreateGameMode();
			IPluginManager plugins = CreatePluginManager(game, new string[0], new string[0]);
			ModInstallationRecipeInput prepared = adapter.Prepare(mod, game, CreateEnvironmentInfo(), plugins,
				Recipe(new Version(5, 0)), null, paths => CreateInput(adapter, paths.ToArray()));

			InstallModFileOperation file = prepared.NativeOperations.OfType<InstallModFileOperation>().Single();
			Assert.That(file.SourcePath, Is.EqualTo(archiveSource));
			Assert.That(file.DestinationPath, Is.EqualTo(expected));
			Assert.That(prepared.Validation.Paths.Single(path => path.Kind == ModInstallationRecipePathKind.Destination).Path, Is.EqualTo(expected));
			Assert.That(prepared.NativeOperations.OfType<SetPluginActivationOperation>().Single().PluginPath, Is.EqualTo(expected));
		}

		/// <summary>Folder expansion preserves complete subdirectories for dot-prefixed destinations and the FOMOD destination-root marker.</summary>
		[TestCase(@".\DIP", @".\DIP", @"DIP\qt6core.dll", @"DIP\sub\second.dll")]
		[TestCase("DIP", @"\", "qt6core.dll", @"sub\second.dll")]
		[TestCase("DIP", ".", "qt6core.dll", @"sub\second.dll")]
		[TestCase("DIP", @".\", "qt6core.dll", @"sub\second.dll")]
		[TestCase("DIP", "/", "qt6core.dll", @"sub\second.dll")]
		public void Prepare_FomodFolderPathsExpandWithinTheDeclaredRoot(string source, string destination, string expectedFirst, string expectedSecond)
		{
			XmlScriptType adapter = new XmlScriptType();
			var script = new XmlScript(adapter, new Version(5, 0));
			script.RequiredInstallFiles.Add(new InstallableFile(source, destination, true, 0, false, false));
			IMod mod = CreateMod(script, @"DIP\qt6core.dll", @"DIP\sub\second.dll", @"unrelated\other.dll");
			ModInstallationRecipeInput prepared = adapter.Prepare(mod, CreateGameMode(), CreateEnvironmentInfo(), null,
				Recipe(new Version(5, 0)), null, paths => CreateInput(adapter, paths.ToArray()));
			Assert.That(prepared.NativeOperations.OfType<InstallModFileOperation>().Select(file => file.DestinationPath),
				Is.EqualTo(new[] { expectedFirst, expectedSecond }));
		}

		/// <summary>A root destination in a selected folder installs and activates the canonical plugin name.</summary>
		[TestCase(@"\", true)]
		[TestCase(@".\", true)]
		[TestCase(".", true)]
		[TestCase(@"\", false)]
		[TestCase(@".\", false)]
		[TestCase(".", false)]
		public void Prepare_FomodRootMappingsPreserveSelectedPluginActivation(string destination, bool folder)
		{
			XmlScriptType adapter = new XmlScriptType();
			XmlScript script = ScriptWithSingleGroup(adapter, OptionGroupType.SelectExactlyOne,
				Option("Patch", OptionType.Optional, new InstallableFile(folder ? "patch" : @"patch\choice.esp", destination, folder, 0, false, false)));
			IGameMode game = CreateGameMode();
			ModInstallationRecipeInput prepared = adapter.Prepare(CreateMod(script, @"patch\choice.esp"), game,
				CreateEnvironmentInfo(), CreatePluginManager(game, new string[0], new string[0]),
				Recipe(new Version(5, 0), Step(0, "Step", Group(0, "Group", Selection(0, "Patch")))),
				null, paths => CreateInput(adapter, paths.ToArray()));
			SetPluginActivationOperation activation = prepared.NativeOperations.OfType<SetPluginActivationOperation>().Single();
			Assert.That(activation.PluginPath, Is.EqualTo("choice.esp"));
			Assert.That(activation.Activate, Is.True);
		}

		/// <summary>Uses the reported mail script and DIP source layouts to verify nested and installer-only files reach the intended roots.</summary>
		[TestCase("Animations", @"\", @"Scripts\WICourierScript.pex", @"Scripts\WICourierScript.pex")]
		[TestCase(@"fomod\DIP", @".\DIP", "qt6core.dll", @"DIP\qt6core.dll")]
		public void Prepare_ReportedFolderMappingsKeepExactFileDestinations(string source, string destination, string relativeFile, string expected)
		{
			XmlScriptType adapter = new XmlScriptType();
			var script = new XmlScript(adapter, new Version(5, 0));
			script.RequiredInstallFiles.Add(new InstallableFile(source, destination, true, 0, false, false));
			string archiveFile = source + "\\" + relativeFile;
			ModInstallationRecipeInput prepared = adapter.Prepare(CreateModWithHiddenFomodAssets(script, archiveFile),
				CreateGameMode(), CreateEnvironmentInfo(), null, Recipe(new Version(5, 0)), null,
				paths => CreateInput(adapter, paths.ToArray()));
			InstallModFileOperation file = prepared.NativeOperations.OfType<InstallModFileOperation>().Single();
			Assert.That(file.SourcePath, Is.EqualTo(archiveFile));
			Assert.That(file.DestinationPath, Is.EqualTo(expected));
		}

		/// <summary>Root-source dot notation enumerates archive files without stripping their first path component.</summary>
		[TestCase(".")]
		[TestCase(@".\")]
		public void Prepare_FomodArchiveRootPreservesNestedPaths(string source)
		{
			XmlScriptType adapter = new XmlScriptType();
			var script = new XmlScript(adapter, new Version(5, 0));
			script.RequiredInstallFiles.Add(new InstallableFile(source, @"\", true, 0, false, false));
			ModInstallationRecipeInput prepared = adapter.Prepare(CreateMod(script, @"Scripts\script.pex", "root.esp"),
				CreateGameMode(), CreateEnvironmentInfo(), null, Recipe(new Version(5, 0)), null,
				paths => CreateInput(adapter, paths.ToArray()));
			Assert.That(prepared.NativeOperations.OfType<InstallModFileOperation>().Select(file => file.DestinationPath),
				Is.EqualTo(new[] { @"Scripts\script.pex", "root.esp" }));
		}

		/// <summary>Directory notation does not permit traversal, rooted paths, or empty interior components.</summary>
		[TestCase(@"..\")]
		[TestCase(@"C:\textures\")]
		[TestCase(@"\textures\")]
		[TestCase(@"textures\\terrain\")]
		[TestCase(@".\..\escape\")]
		[TestCase(@".\C:\escape\")]
		[TestCase(@"\\server\share\")]
		[TestCase(@".\NUL\")]
		[TestCase(@".\\")]
		public void Translate_InvalidDestinationDirectoryIsRejected(string destination)
		{
			XmlScriptType scriptType = new XmlScriptType();
			XmlScript script = new XmlScript(scriptType, new Version(5, 0));
			script.RequiredInstallFiles.Add(File("back.dds", destination, 0));
			IModInstallationFomodRecipePlanningAdapter adapter = scriptType;
			Assert.Throws<InvalidDataException>(() => adapter.GetValidationPaths(CreateMod(script, "back.dds"),
				CreateGameMode(), CreateEnvironmentInfo(), null, Recipe(new Version(5, 0))));
		}

		/// <summary>FOMOD source normalization cannot turn a rooted or escaping folder into an admitted archive source.</summary>
		[TestCase(@"\")]
		[TestCase(@"\DIP")]
		[TestCase(@"\\server\share")]
		[TestCase(@".\..\DIP")]
		[TestCase(@".\C:\DIP")]
		[TestCase(@".\\")]
		public void Prepare_UnsafeFolderSourceIsRejectedBeforeExpansion(string source)
		{
			XmlScriptType adapter = new XmlScriptType();
			var script = new XmlScript(adapter, new Version(5, 0));
			script.RequiredInstallFiles.Add(new InstallableFile(source, ".", true, 0, false, false));
			Assert.Throws<InvalidDataException>(() => adapter.Prepare(CreateMod(script, @"DIP\qt6core.dll"),
				CreateGameMode(), CreateEnvironmentInfo(), null, Recipe(new Version(5, 0)), null,
				paths => CreateInput(adapter, paths.ToArray())));
		}

		[Test]
		public void Translate_FomodOptionsFolderExpandsOnlyPresentInstallerAssets()
		{
			XmlScriptType scriptType = new XmlScriptType();
			XmlScript script = new XmlScript(scriptType, new Version(5, 0));
			script.RequiredInstallFiles.Add(new InstallableFile(@"fomod\options", "textures", true, 0, false, false));
			IMod mod = CreateModWithHiddenFomodAssets(script,
				@"fomod\options\a.dds", @"fomod\options\sub\b.dds", @"other\c.dds");
			IModInstallationFomodRecipePlanningAdapter adapter = scriptType;
			ModInstallationFomodSelectionRecipe selection = Recipe(new Version(5, 0));

			IReadOnlyList<ModInstallationRecipePath> paths = adapter.GetValidationPaths(
				mod, CreateGameMode(), CreateEnvironmentInfo(), null, selection);
			ModInstallationRecipeInput installed = adapter.Translate(
				CreateInput(adapter, paths.ToArray()), mod, CreateGameMode(), CreateEnvironmentInfo(), null, selection);
			Assert.That(installed.NativeOperations.OfType<InstallModFileOperation>().Select(op => op.SourcePath),
				Is.EquivalentTo(new[] { @"fomod\options\a.dds", @"fomod\options\sub\b.dds" }));
		}

		[Test]
		public void Translate_MissingInstallerAssetStillFailsClosed()
		{
			XmlScriptType scriptType = new XmlScriptType();
			XmlScript script = new XmlScript(scriptType, new Version(5, 0));
			script.RequiredInstallFiles.Add(File(@"fomod\options\missing.dds", @"textures\out.dds", 0));
			IMod mod = CreateModWithHiddenFomodAssets(script, @"fomod\options\different.dds");
			IModInstallationFomodRecipePlanningAdapter adapter = scriptType;
			Assert.Throws<InvalidDataException>(() => adapter.GetValidationPaths(
				mod, CreateGameMode(), CreateEnvironmentInfo(), null, Recipe(new Version(5, 0))));
		}

		/// <summary>
		/// Verifies required, selected, always-install and flag-conditional files become native typed operations in native order.
		/// </summary>
		[Test]
		public void Translate_UnnamedLegacyFomodStepIsMatchedByPositionWithoutMatchingNamedSteps()
		{
			XmlScriptType scriptType = new XmlScriptType();
			XmlScript script = new XmlScript(scriptType, new Version(5, 0));
			var step = new InstallStep(null, null, SortOrder.Explicit);
			var group = new OptionGroup("Group", OptionGroupType.SelectExactlyOne, SortOrder.Explicit);
			group.Options.Add(Option("Selected", OptionType.Optional, File(@"choice.bin", @"choice.bin", 0)));
			step.OptionGroups.Add(group);
			script.InstallSteps.Add(step);
			IMod mod = CreateMod(script, @"choice.bin");
			IModInstallationFomodRecipeAdapter adapter = scriptType;
			ModInstallationRecipeInput input = CreateInput(adapter, PathSource(@"choice.bin"), PathDestination(@"choice.bin"));

			ModInstallationFomodSelectionRecipe missingName = Recipe(new Version(5, 0),
				Step(0, null, Group(0, "Group", Selection(0, "Selected"))));
			ModInstallationRecipeInput translated = adapter.Translate(
				input, mod, CreateGameMode(), CreateEnvironmentInfo(), null, missingName);
			Assert.That(translated.NativeOperations.OfType<InstallModFileOperation>().Count(), Is.EqualTo(1));

			ModInstallationFomodSelectionRecipe emptyName = Recipe(new Version(5, 0),
				Step(0, "", Group(0, "Group", Selection(0, "Selected"))));
			Assert.DoesNotThrow(() => adapter.Translate(input, mod, CreateGameMode(), CreateEnvironmentInfo(), null, emptyName));

			ModInstallationFomodSelectionRecipe inventedName = Recipe(new Version(5, 0),
				Step(0, "Not this step", Group(0, "Group", Selection(0, "Selected"))));
			Assert.Throws<InvalidDataException>(() => adapter.Translate(
				input, mod, CreateGameMode(), CreateEnvironmentInfo(), null, inventedName));
		}

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

		/// <summary>Automatic replay installs the recorded patch despite unmet prerequisites, hidden steps and current option types.</summary>
		[Test]
		public void Translate_RecordedChoicesOverrideCurrentInstallerConditions()
		{
			XmlScriptType type = new XmlScriptType();
			Option patch = Option("Cutting Room Floor (by Arthmoor)", OptionType.NotUsable,
				File(@"patch\crf.bin", @"crf.bin", 0));
			patch.Flags.Add(new ConditionalFlag("patch", "crf"));
			XmlScript script = ScriptWithSingleGroup(type, OptionGroupType.SelectAny, patch,
				Option("Current default", OptionType.Required, File("default.bin", "default.bin", 0)));
			script.ModPrerequisites = new FlagCondition("not-installed", "present");
			script.InstallSteps[0].VisibilityCondition = new FlagCondition("not-installed", "present");
			script.ConditionallyInstalledFileSets.Add(new ConditionallyInstalledFileSet(new FlagCondition("patch", "crf"),
				new List<InstallableFile> { File("conditional.bin", "conditional.bin", 0) }));
			IMod mod = CreateMod(script, @"patch\crf.bin", "default.bin", "conditional.bin");
			ModInstallationFomodStepSelection[] steps = { Step(0, "Step", Group(0, "Group", Selection(0, patch.Name))) };
			IModInstallationFomodRecipePlanningAdapter adapter = type;
			Assert.Throws<DependencyException>(() => adapter.GetValidationPaths(mod, CreateGameMode(), CreateEnvironmentInfo(), null,
				new ModInstallationFomodSelectionRecipe(new Version(5, 0), steps)));
			var recipe = new ModInstallationFomodSelectionRecipe(new Version(5, 0), steps, true);
			IReadOnlyList<ModInstallationRecipePath> paths = adapter.GetValidationPaths(mod, CreateGameMode(), CreateEnvironmentInfo(), null, recipe);
			ModInstallationRecipeInput translated = adapter.Translate(CreateInput(adapter, paths.ToArray()), mod,
				CreateGameMode(), CreateEnvironmentInfo(), null, recipe);
			Assert.That(translated.NativeOperations.OfType<InstallModFileOperation>().Select(x => x.SourcePath),
				Is.EqualTo(new[] { @"patch\crf.bin", "conditional.bin" }));
		}

		/// <summary>Sorted groups resolve a unique saved name when Vortex and native ordering assign different indices.</summary>
		[TestCase(SortOrder.Ascending)]
		[TestCase(SortOrder.Descending)]
		public void Translate_SortedChoicesResolveUniqueNamesAcrossOrderingDifferences(SortOrder order)
		{
			XmlScriptType type = new XmlScriptType();
			XmlScript script = ScriptWithSingleGroup(type, OptionGroupType.SelectAny,
				Option("Aela Restored", OptionType.Optional, File("other.bin", "other.bin", 0)),
				Option("Immersive Citizens", OptionType.Optional, File("selected.bin", "selected.bin", 0)));
			OptionGroup group = script.InstallSteps[0].OptionGroups[0];
			group.OptionSortOrder = order;
			int differentIndex = Enumerable.Range(0, group.Options.Count).Single(x => group.Options[x].Name == "Aela Restored");
			var recipe = Recipe(new Version(5, 0), Step(0, "Step", Group(0, "Group", Selection(differentIndex, "Immersive Citizens"))));
			IModInstallationFomodRecipePlanningAdapter adapter = type;
			IMod mod = CreateMod(script, "other.bin", "selected.bin");
			IReadOnlyList<ModInstallationRecipePath> paths = adapter.GetValidationPaths(mod, CreateGameMode(), CreateEnvironmentInfo(), null, recipe);
			ModInstallationRecipeInput translated = adapter.Translate(CreateInput(adapter, paths.ToArray()), mod,
				CreateGameMode(), CreateEnvironmentInfo(), null, recipe);
			Assert.That(translated.NativeOperations.OfType<InstallModFileOperation>().Single().SourcePath, Is.EqualTo("selected.bin"));
		}

		/// <summary>Automatic Collections use the exact recorded DLL name when its saved index identifies another runtime.</summary>
		[Test]
		public void Translate_RecordedDllChoiceResolvesUniqueNameInExplicitGroup()
		{
			XmlScriptType type = new XmlScriptType();
			const string selectedName = "SSE/AE v1.6.1170 (\"Anniversary/Special Edition\")";
			XmlScript script = ScriptWithSingleGroup(type, OptionGroupType.SelectExactlyOne,
				Option("SSE/AE v1.7.99+", OptionType.Optional, File("future.dll", "selected.dll", 0)),
				Option("SSE v1.5.97", OptionType.Optional, File("old.dll", "selected.dll", 0)),
				Option(selectedName, OptionType.Optional, File("1170.dll", "selected.dll", 0)));
			ModInstallationFomodStepSelection[] steps = { Step(0, "Step", Group(0, "Group", Selection(1, selectedName))) };
			var recipe = new ModInstallationFomodSelectionRecipe(new Version(5, 0), steps, true);
			IModInstallationFomodRecipePlanningAdapter adapter = type;
			IMod mod = CreateMod(script, "future.dll", "old.dll", "1170.dll");
			IReadOnlyList<ModInstallationRecipePath> paths = adapter.GetValidationPaths(mod, CreateGameMode(), CreateEnvironmentInfo(), null, recipe);
			ModInstallationRecipeInput translated = adapter.Translate(CreateInput(adapter, paths.ToArray()), mod,
				CreateGameMode(), CreateEnvironmentInfo(), null, recipe);
			Assert.That(translated.NativeOperations.OfType<InstallModFileOperation>().Single().SourcePath, Is.EqualTo("1170.dll"));
		}

		/// <summary>Automatic name resolution cannot select among duplicate names when the saved position does not match.</summary>
		[Test]
		public void Translate_RecordedChoicesRejectAmbiguousNames()
		{
			XmlScriptType type = new XmlScriptType();
			XmlScript script = ScriptWithSingleGroup(type, OptionGroupType.SelectAny,
				Option("A", OptionType.Optional, File("a.bin", "a.bin", 0)),
				Option("B", OptionType.Optional, File("b.bin", "b.bin", 0)),
				Option("B", OptionType.Optional, File("c.bin", "c.bin", 0)));
			var recipe = new ModInstallationFomodSelectionRecipe(new Version(5, 0),
				new[] { Step(0, "Step", Group(0, "Group", Selection(0, "B"))) }, true);
			IModInstallationFomodRecipePlanningAdapter adapter = type;
			Assert.Throws<InvalidDataException>(() => adapter.GetValidationPaths(CreateMod(script, "a.bin", "b.bin", "c.bin"),
				CreateGameMode(), CreateEnvironmentInfo(), null, recipe));
		}

		/// <summary>Ordering compatibility does not guess among duplicate names or change explicitly ordered native identities.</summary>
		[TestCase(SortOrder.Ascending, true)]
		[TestCase(SortOrder.Explicit, false)]
		public void Translate_OrderingFallbackRejectsAmbiguousOrExplicitChoices(SortOrder order, bool duplicate)
		{
			XmlScriptType type = new XmlScriptType();
			XmlScript script = ScriptWithSingleGroup(type, OptionGroupType.SelectAny,
				Option("A", OptionType.Optional, File("a.bin", "a.bin", 0)),
				Option("B", OptionType.Optional, File("b.bin", "b.bin", 0)));
			OptionGroup group = script.InstallSteps[0].OptionGroups[0];
			if (duplicate) group.Options.Add(Option("B", OptionType.Optional, File("c.bin", "c.bin", 0)));
			group.OptionSortOrder = order;
			IModInstallationFomodRecipePlanningAdapter adapter = type;
			Assert.Throws<InvalidDataException>(() => adapter.GetValidationPaths(CreateMod(script, "a.bin", "b.bin", "c.bin"),
				CreateGameMode(), CreateEnvironmentInfo(), null, Recipe(new Version(5, 0),
					Step(0, "Step", Group(0, "Group", Selection(0, "B"))))));
		}

		/// <summary>Recorded choices overwrite an earlier flag default and select only the files for the final value.</summary>
		[TestCase("Off", "On", "on.bin")]
		[TestCase("On", "Off", "off.bin")]
		public void Translate_RecordedFlagAssignmentsUseLastValue(string initialValue, string finalValue, string expectedFile)
		{
			XmlScriptType type = new XmlScriptType();
			Option initial = Option("Quality", OptionType.Optional);
			initial.Flags.Add(new ConditionalFlag("CathedralConcept", initialValue));
			Option later = Option("Landscapes - Cathedral Concept", OptionType.Optional);
			later.Flags.Add(new ConditionalFlag("CathedralConcept", finalValue));
			XmlScript script = ScriptWithSingleGroup(type, OptionGroupType.SelectAny, initial, later);
			script.ConditionallyInstalledFileSets.Add(new ConditionallyInstalledFileSet(new FlagCondition("CathedralConcept", "On"),
				new List<InstallableFile> { File("on.bin", "on.bin", 0) }));
			script.ConditionallyInstalledFileSets.Add(new ConditionallyInstalledFileSet(new FlagCondition("CathedralConcept", "Off"),
				new List<InstallableFile> { File("off.bin", "off.bin", 0) }));
			IMod mod = CreateMod(script, "on.bin", "off.bin");
			ModInstallationFomodStepSelection[] steps = { Step(0, "Step", Group(0, "Group", Selection(0, initial.Name), Selection(1, later.Name))) };
			IModInstallationFomodRecipePlanningAdapter adapter = type;
			Assert.Throws<InvalidDataException>(() => adapter.GetValidationPaths(mod, CreateGameMode(), CreateEnvironmentInfo(), null,
				new ModInstallationFomodSelectionRecipe(new Version(5, 0), steps)));
			var recipe = new ModInstallationFomodSelectionRecipe(new Version(5, 0), steps, true);
			IReadOnlyList<ModInstallationRecipePath> paths = adapter.GetValidationPaths(mod, CreateGameMode(), CreateEnvironmentInfo(), null, recipe);
			ModInstallationRecipeInput translated = adapter.Translate(CreateInput(adapter, paths.ToArray()), mod,
				CreateGameMode(), CreateEnvironmentInfo(), null, recipe);
			Assert.That(translated.NativeOperations.OfType<InstallModFileOperation>().Single().SourcePath, Is.EqualTo(expectedFile));
		}

		/// <summary>Automatic replay does not invent choices or install files from an unselected hidden step.</summary>
		[Test]
		public void Translate_RecordedEmptyHiddenStepDoesNotChooseDefaults()
		{
			XmlScriptType type = new XmlScriptType();
			XmlScript script = ScriptWithSingleGroup(type, OptionGroupType.SelectExactlyOne,
				Option("Default", OptionType.Required, new InstallableFile("default.bin", "default.bin", false, 0, true, false)));
			script.InstallSteps[0].VisibilityCondition = new FlagCondition("not-installed", "present");
			script.RequiredInstallFiles.Add(File("core.bin", "core.bin", 0));
			IMod mod = CreateMod(script, "core.bin", "default.bin");
			var recipe = new ModInstallationFomodSelectionRecipe(new Version(5, 0),
				new[] { Step(0, "Step", Group(0, "Group")) }, true);
			IModInstallationFomodRecipePlanningAdapter adapter = type;
			IReadOnlyList<ModInstallationRecipePath> paths = adapter.GetValidationPaths(mod, CreateGameMode(), CreateEnvironmentInfo(), null, recipe);
			ModInstallationRecipeInput translated = adapter.Translate(CreateInput(adapter, paths.ToArray()), mod,
				CreateGameMode(), CreateEnvironmentInfo(), null, recipe);
			Assert.That(translated.NativeOperations.OfType<InstallModFileOperation>().Single().SourcePath, Is.EqualTo("core.bin"));
		}

		/// <summary>Recorded-choice replay still binds to the actual installer and exact archive source paths.</summary>
		[Test]
		public void Translate_RecordedChoicesStillValidateOptionIdentityAndSourcePaths()
		{
			XmlScriptType type = new XmlScriptType();
			XmlScript script = ScriptWithSingleGroup(type, OptionGroupType.SelectAny,
				Option("Patch", OptionType.NotUsable, File("patch.bin", "patch.bin", 0)));
			IModInstallationFomodRecipePlanningAdapter adapter = type;
			var staleRecipe = new ModInstallationFomodSelectionRecipe(new Version(5, 0),
				new[] { Step(0, "Step", Group(0, "Group", Selection(0, "Different patch"))) }, true);
			Assert.Throws<InvalidDataException>(() => adapter.GetValidationPaths(CreateMod(script, "patch.bin"),
				CreateGameMode(), CreateEnvironmentInfo(), null, staleRecipe));
			var recipe = new ModInstallationFomodSelectionRecipe(new Version(5, 0),
				new[] { Step(0, "Step", Group(0, "Group", Selection(0, "Patch"))) }, true);
			Assert.Throws<InvalidDataException>(() => adapter.GetValidationPaths(CreateMod(script, "different.bin"),
				CreateGameMode(), CreateEnvironmentInfo(), null, recipe));
		}

		[Test]
		public void Translate_SelectedUnusablePureCreditOptionIsANoOp()
		{
			XmlScriptType type = new XmlScriptType();
			XmlScript script = ScriptWithSingleGroup(type, OptionGroupType.SelectAny,
				Option("La Coiffe Collection by LunaEversor", OptionType.NotUsable));
			script.RequiredInstallFiles.Add(File(@"core\main.bin", @"main.bin", 0));
			IMod mod = CreateMod(script, @"core\main.bin");
			var recipe = Recipe(new Version(5, 0), Step(0, "Step", Group(0, "Group",
				Selection(0, "La Coiffe Collection by LunaEversor"))));
			IModInstallationFomodRecipeAdapter adapter = type;
			ModInstallationRecipeInput input = CreateInput(adapter,
				PathSource(@"core\main.bin"), PathDestination(@"main.bin"));
			ModInstallationRecipeInput plan = adapter.Translate(input, mod, CreateGameMode(), CreateEnvironmentInfo(), null, recipe);
			Assert.That(plan.NativeOperations.OfType<InstallModFileOperation>().Select(x => x.SourcePath),
				Is.EqualTo(new[] { @"core\main.bin" }));
		}

		[Test]
		public void Translate_SelectedUnusableCreditWithFlagsOrFilesStillBlocks()
		{
			XmlScriptType type = new XmlScriptType();
			Option flagged = Option("Credit with installer flag", OptionType.NotUsable);
			flagged.Flags.Add(new ConditionalFlag("feature", "enabled"));
			XmlScript flagScript = ScriptWithSingleGroup(type, OptionGroupType.SelectAny, flagged);
			IMod mod = CreateMod(flagScript);
			var recipe = Recipe(new Version(5, 0), Step(0, "Step", Group(0, "Group", Selection(0, flagged.Name))));
			Assert.Throws<DependencyException>(() => type.Translate(CreateInput(type), mod,
				CreateGameMode(), CreateEnvironmentInfo(), null, recipe));

			XmlScript fileScript = ScriptWithSingleGroup(type, OptionGroupType.SelectAny,
				Option("Credit with files", OptionType.NotUsable, File(@"data.bin", @"data.bin", 0)));
			Assert.Throws<DependencyException>(() => type.Translate(CreateInput(type), CreateMod(fileScript, @"data.bin"),
				CreateGameMode(), CreateEnvironmentInfo(), null, Recipe(new Version(5, 0), Step(0, "Step",
				Group(0, "Group", Selection(0, "Credit with files"))))));
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
			var managedPlugins = new ReadOnlyObservableList<Plugin>(new ThreadSafeObservableList<Plugin>());
			var activePlugins = new ReadOnlyObservableList<Plugin>(new ThreadSafeObservableList<Plugin>());
			IPluginManager pluginManager = InterfaceStub<IPluginManager>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_ManagedPlugins":
						return managedPlugins;
					case "get_ActivePlugins":
						return activePlugins;
					case "IsActivatiblePluginFile":
						activationQueries++;
						return false;
					default:
						return null;
				}
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
			IGameMode gameMode = CreateGameMode();
			IPluginManager pluginManager = CreatePluginManager(gameMode, new string[0], new string[0]);

			ModInstallationRecipeInput translated = adapter.Translate(
				CreateInput(adapter, PathSource(@"plugins\optional.esp"), PathDestination(@"optional.esp")),
				CreateMod(script, @"plugins\optional.esp"), gameMode, CreateEnvironmentInfo(), pluginManager,
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
		/// Verifies the explicit replacement condition environment overrides the live plugin baseline for conditional files.
		/// </summary>
		[Test]
		public void Translate_ConditionalFilesUseExplicitProjectedEnvironmentInsteadOfLivePluginState()
		{
			XmlScriptType scriptType = new XmlScriptType();
			XmlScript script = new XmlScript(scriptType, new Version(5, 0));
			script.RequiredInstallFiles.Add(File(@"base.bin", @"base.bin", 0));
			script.ConditionallyInstalledFileSets.Add(new ConditionallyInstalledFileSet(
				new PluginCondition(@"existing.esp", PluginState.Active),
				new List<InstallableFile> { File(@"conditional.bin", @"conditional.bin", 0) }));
			IModInstallationFomodRecipePlanningAdapter adapter = scriptType;
			IGameMode gameMode = CreateGameMode();
			IPluginManager livePluginManager = CreatePluginManager(gameMode, new string[0], new string[0]);
			var projected = new TestConditionEnvironment(new[] { @"existing.esp" }, new[] { @"existing.esp" });
			IMod mod = CreateMod(script, @"base.bin", @"conditional.bin");
			var recipe = new ModInstallationFomodSelectionRecipe(new Version(5, 0), new ModInstallationFomodStepSelection[0]);

			IReadOnlyList<ModInstallationRecipePath> paths = adapter.GetValidationPaths(
				mod, gameMode, CreateEnvironmentInfo(), livePluginManager, recipe, projected);
			ModInstallationRecipeInput translated = adapter.Translate(CreateInput(adapter, paths.ToArray()), mod, gameMode,
				CreateEnvironmentInfo(), livePluginManager, recipe, projected);

			Assert.That(translated.NativeOperations.OfType<InstallModFileOperation>().Select(operation => operation.SourcePath),
				Is.EqualTo(new[] { @"base.bin", @"conditional.bin" }));
		}

		/// <summary>
		/// Verifies unprovable projected plugin facts fail closed instead of being interpreted as inactive/absent.
		/// </summary>
		[TestCase(PluginState.Active)]
		[TestCase(PluginState.Inactive)]
		[TestCase(PluginState.Missing)]
		public void GetValidationPaths_UnprovableProjectedPluginCondition_FailsClosed(PluginState state)
		{
			XmlScriptType scriptType = new XmlScriptType();
			XmlScript script = new XmlScript(scriptType, new Version(5, 0));
			script.ConditionallyInstalledFileSets.Add(new ConditionallyInstalledFileSet(
				new PluginCondition(@"unknown.esp", state),
				new List<InstallableFile> { File(@"conditional.bin", @"conditional.bin", 0) }));
			IModInstallationFomodRecipePlanningAdapter adapter = scriptType;
			IGameMode gameMode = CreateGameMode();
			var projected = new TestConditionEnvironment(new string[0], new string[0], false);

			Assert.Throws<NotSupportedException>(() => adapter.GetValidationPaths(
				CreateMod(script, @"conditional.bin"), gameMode, CreateEnvironmentInfo(),
				CreatePluginManager(gameMode, new string[0], new string[0]),
				new ModInstallationFomodSelectionRecipe(new Version(5, 0), new ModInstallationFomodStepSelection[0]), projected));
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

		/// <summary>Resumed recipes can read sibling DLC sources from cached metadata without enumerating files first.</summary>
		[TestCase("file")]
		[TestCase("stream")]
		[TestCase("copy")]
		[TestCase("batch")]
		[TestCase("exists")]
		[TestCase("source-path")]
		public void Archive_CachedPrefixPreservesSiblingSourcesBeforeEnumeration(string access)
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-fomod-sibling-" + Guid.NewGuid().ToString("N"));
			string cacheDirectory = Path.Combine(root, "ModCache");
			Directory.CreateDirectory(root);
			try
			{
				const string prefix = "modRazorRemasteredNew";
				const string sibling = @"dlcRazorRemasteredNew\content\br.w3strings";
				const string primary = @"content\scripts\local\razor.ws";
				byte[] payload = Encoding.UTF8.GetBytes("sibling DLC payload");
				string archivePath = Path.Combine(root, "razor.zip");
				using (var stream = new FileStream(archivePath, FileMode.Create))
				using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
				{
					WriteInstallerRootEntry(zip, sibling.Replace('\\', '/'), payload);
					WriteInstallerRootEntry(zip, prefix + "/" + primary.Replace('\\', '/'), payload);
				}
				IEnvironmentInfo environment = InterfaceStub<IEnvironmentInfo>.Create((method, args) =>
					method.Name == "get_TemporaryPath" ? root : null);
				IModCacheManager cacheManager = InterfaceStub<IModCacheManager>.Create((method, args) =>
					method.Name == "get_ModCacheDirectory" ? cacheDirectory :
					method.Name == "get_EnvironmentInfo" ? (object)environment : null);
				IScriptTypeRegistry registry = InterfaceStub<IScriptTypeRegistry>.Create((method, args) =>
					method.Name == "get_Types" ? new List<IScriptType>() : null);
				Type cacheType = typeof(FOMod).Assembly.GetType("Nexus.Client.Mods.Formats.FOMod.FOModArchiveMetadataCache", true);
				Type metadataType = typeof(FOMod).Assembly.GetType("Nexus.Client.Mods.Formats.FOMod.FOModArchiveMetadata", true);
				object cache = Activator.CreateInstance(cacheType, cacheDirectory);
				object metadata = Activator.CreateInstance(metadataType);
				metadataType.GetProperty("PrefixPath").SetValue(metadata, prefix);
				metadataType.GetProperty("InfoXml").SetValue(metadata, Encoding.UTF8.GetBytes("<fomod/>"));
				cacheType.GetMethod("SaveDurable", BindingFlags.Instance | BindingFlags.NonPublic)
					.Invoke(cache, new[] { (object)archivePath, metadata });
				var mod = new FOMod(archivePath, null, new[] { "content" }, "", new string[0], cacheManager, registry, false, false);
				Assert.IsFalse((bool)typeof(FOMod).GetField("_movedArchiveInitialized", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(mod),
					"The cached fixture must reach the first source read without initializing its archive map.");
				if (access == "exists") Assert.IsTrue(mod.ContainsFile(sibling));
				else if (access == "source-path") Assert.AreEqual(sibling, mod.GetArchiveSourcePath(sibling));
				else if (access == "file") CollectionAssert.AreEqual(payload, mod.GetFile(sibling));
				else if (access == "batch")
				{
					string output = Path.Combine(root, "copied.w3strings");
					mod.CopyFilesTo(new Dictionary<string, string> { { sibling, output } }, System.Threading.CancellationToken.None, null);
					CollectionAssert.AreEqual(payload, System.IO.File.ReadAllBytes(output));
				}
				else using (var output = new MemoryStream())
				{
					if (access == "copy") mod.CopyFileTo(sibling, output);
					else using (Stream input = mod.GetFileStream(sibling)) input.CopyTo(output);
					CollectionAssert.AreEqual(payload, output.ToArray());
				}
				CollectionAssert.AreEqual(payload, mod.GetFile(primary));
				Assert.That(mod.GetFileList(), Is.EquivalentTo(new[] { sibling, primary }));
				Assert.IsFalse(mod.ContainsFile(@"dlcRazorRemasteredNew\content\missing.w3strings"));
			}
			finally
			{
				ReleaseInstallerRootMetadataDatabase(cacheDirectory);
				Directory.Delete(root, true);
			}
		}

		/// <summary>Detects Data-wrapped installers and replays their relative files with cold, warm, and legacy negative metadata.</summary>
		[TestCase("", 0, true)]
		[TestCase("", 1, true)]
		[TestCase("", 2, true)]
		[TestCase("Wrapper", 0, true)]
		[TestCase("Wrapper", 1, true)]
		[TestCase("Wrapper", 2, true)]
		[TestCase("", 0, false)]
		[TestCase("Wrapper", 1, false)]
		public void Archive_DataWrappedInstallerUsesItsOwnSourceRoot(string wrapper, int cacheState, bool scriptPresent)
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-fomod-data-root-" + Guid.NewGuid().ToString("N"));
			string cacheDirectory = Path.Combine(root, "ModCache");
			Directory.CreateDirectory(root);
			try
			{
				const string source = @"Replacer_Clutter\Meshes\vendor.nif";
				const string destination = @"Meshes\vendor.nif";
				var scriptType = new XmlScriptType();
				XmlScript script = ScriptWithSingleGroup(scriptType, OptionGroupType.SelectExactlyOne,
					Option("Standalone with Clutter", OptionType.Optional, File("other.bin", "other.bin", 0)),
					Option("Standalone without Clutter", OptionType.Optional, File("other.bin", "other.bin", 0)),
					Option("Replace Vanilla with Clutter", OptionType.Optional, File(source, destination, 0)));
				byte[] payload = Encoding.UTF8.GetBytes("selected mesh");
				string archivePath = Path.Combine(root, "vendor-stalls.zip");
				string dataRoot = String.IsNullOrEmpty(wrapper) ? "Data/" : wrapper + "/Data/";
				using (var stream = new FileStream(archivePath, FileMode.Create))
				using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
				{
					WriteInstallerRootEntry(zip, dataRoot + source.Replace('\\', '/'), payload);
					WriteInstallerRootEntry(zip, dataRoot + "other.bin", payload);
					if (scriptPresent)
						WriteInstallerRootEntry(zip, dataRoot + "fomod/ModuleConfig.xml",
							Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(scriptType.SaveScript(script))).ToArray());
				}
				IEnvironmentInfo environment = InterfaceStub<IEnvironmentInfo>.Create((method, args) =>
					method.Name == "get_TemporaryPath" ? root : null);
				IModCacheManager cacheManager = InterfaceStub<IModCacheManager>.Create((method, args) =>
					method.Name == "get_ModCacheDirectory" ? cacheDirectory :
					method.Name == "get_EnvironmentInfo" ? (object)environment : null);
				IScriptTypeRegistry registry = InterfaceStub<IScriptTypeRegistry>.Create((method, args) =>
					method.Name == "get_Types" ? new List<IScriptType> { scriptType } : null);
				if (cacheState == 2)
				{
					// Reproduce the old cached result without modifying the archive or requiring a user cache reset.
					Type cacheType = typeof(FOMod).Assembly.GetType("Nexus.Client.Mods.Formats.FOMod.FOModArchiveMetadataCache", true);
					Type metadataType = typeof(FOMod).Assembly.GetType("Nexus.Client.Mods.Formats.FOMod.FOModArchiveMetadata", true);
					object cache = Activator.CreateInstance(cacheType, cacheDirectory);
					object metadata = Activator.CreateInstance(metadataType);
					metadataType.GetProperty("PrefixPath").SetValue(metadata, wrapper);
					metadataType.GetProperty("InfoXml").SetValue(metadata, Encoding.UTF8.GetBytes("<fomod/>"));
					cacheType.GetMethod("SaveDurable", BindingFlags.Instance | BindingFlags.NonPublic)
						.Invoke(cache, new[] { (object)archivePath, metadata });
				}
				if (cacheState == 1)
					new FOMod(archivePath, null, new[] { "Meshes" }, "Data", new[] { ".esp" }, cacheManager, registry, true, false);
				var mod = new FOMod(archivePath, null, new[] { "Meshes" }, "Data", new[] { ".esp" }, cacheManager, registry, true, false);
				Assert.That(mod.HasInstallScript, Is.EqualTo(scriptPresent));
				if (!scriptPresent)
				{
					Assert.That(mod.GetFileList(), Does.Contain(Path.Combine("Data", source)), "Ordinary Data-wrapped mod layouts must stay unchanged.");
					return;
				}
				Assert.That(mod.GetFile(source), Is.EqualTo(payload));
				Assert.That(mod.GetFileList(), Does.Contain(source));
				IModInstallationFomodRecipePlanningAdapter adapter = (IModInstallationFomodRecipePlanningAdapter)mod.InstallScript.Type;
				ModInstallationFomodSelectionRecipe selection = Recipe(new Version(5, 0),
					Step(0, "Step", Group(0, "Group", Selection(2, "Replace Vanilla with Clutter"))));
				IReadOnlyList<ModInstallationRecipePath> paths = adapter.GetValidationPaths(mod, CreateGameMode(), environment, null, selection);
				Assert.That(paths.Where(path => path.Kind == ModInstallationRecipePathKind.ArchiveSource).Select(path => path.Path),
					Is.EqualTo(new[] { source }));
				ModInstallationRecipeInput translated = adapter.Translate(CreateInput(adapter, paths.ToArray()), mod,
					CreateGameMode(), environment, null, selection);
				InstallModFileOperation file = translated.NativeOperations.OfType<InstallModFileOperation>().Single();
				Assert.That(file.SourcePath, Is.EqualTo(source));
				Assert.That(file.DestinationPath, Is.EqualTo(destination));
			}
			finally
			{
				ReleaseInstallerRootMetadataDatabase(cacheDirectory);
				Directory.Delete(root, true);
			}
		}

		/// <summary>Retains ENB include paths for fresh, cached, and legacy flattened archive metadata.</summary>
		[TestCase("", 0)]
		[TestCase("", 1)]
		[TestCase("", 2)]
		[TestCase("Wrapper", 0)]
		[TestCase("Wrapper", 1)]
		[TestCase("Wrapper", 2)]
		public void Archive_EnbShaderLibraryKeepsItsIncludeRoot(string wrapper, int cacheState)
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-enb-root-" + Guid.NewGuid().ToString("N"));
			string cacheDirectory = Path.Combine(root, "ModCache");
			Directory.CreateDirectory(root);
			try
			{
				string archivePath = Path.Combine(root, "shader-core.zip");
				string archiveBase = String.IsNullOrEmpty(wrapper) ? String.Empty : wrapper + "/";
				const string shader = @"enbseries\enbeffect.fx";
				const string include = @"enbseries\KiSU\Common.fxh";
				const string texture = @"enbseries\KiSU\Textures\noise.dds";
				byte[] payload = Encoding.UTF8.GetBytes("shader fixture");
				using (var stream = new FileStream(archivePath, FileMode.Create))
				using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
				{
					foreach (string path in new[] { shader, include, texture })
						WriteInstallerRootEntry(zip, archiveBase + path.Replace('\\', '/'), payload);
					WriteInstallerRootEntry(zip, archiveBase + "vortex_override_instructions.json", payload);
				}
				IEnvironmentInfo environment = InterfaceStub<IEnvironmentInfo>.Create((method, args) =>
					method.Name == "get_TemporaryPath" ? root : null);
				IModCacheManager cacheManager = InterfaceStub<IModCacheManager>.Create((method, args) =>
					method.Name == "get_ModCacheDirectory" ? cacheDirectory :
					method.Name == "get_EnvironmentInfo" ? (object)environment : null);
				IScriptTypeRegistry registry = InterfaceStub<IScriptTypeRegistry>.Create((method, args) =>
					method.Name == "get_Types" ? new List<IScriptType>() : null);
				if (cacheState == 2)
				{
					Type cacheType = typeof(FOMod).Assembly.GetType("Nexus.Client.Mods.Formats.FOMod.FOModArchiveMetadataCache", true);
					Type metadataType = typeof(FOMod).Assembly.GetType("Nexus.Client.Mods.Formats.FOMod.FOModArchiveMetadata", true);
					object cache = Activator.CreateInstance(cacheType, cacheDirectory);
					object metadata = Activator.CreateInstance(metadataType);
					metadataType.GetProperty("PrefixPath").SetValue(metadata, Path.Combine(wrapper, @"enbseries\KiSU"));
					metadataType.GetProperty("InfoXml").SetValue(metadata, Encoding.UTF8.GetBytes("<fomod/>"));
					cacheType.GetMethod("SaveDurable", BindingFlags.Instance | BindingFlags.NonPublic)
						.Invoke(cache, new[] { (object)archivePath, metadata });
				}
				if (cacheState == 1)
					new FOMod(archivePath, null, new[] { "Textures", "Meshes" }, "Data", new[] { ".esp" }, cacheManager, registry, true, false);
				var mod = new FOMod(archivePath, null, new[] { "Textures", "Meshes" }, "Data", new[] { ".esp" }, cacheManager, registry, true, false);
				Assert.That(mod.GetFileList(), Is.EquivalentTo(new[] { shader, include, texture, "vortex_override_instructions.json" }));
				Assert.That(mod.GetFile(include), Is.EqualTo(payload));
				Assert.That(mod.GetFile(texture), Is.EqualTo(payload));
			}
			finally
			{
				ReleaseInstallerRootMetadataDatabase(cacheDirectory);
				Directory.Delete(root, true);
			}
		}

		/// <summary>Writes one archive entry for the real installer-root regression fixture.</summary>
		private static void WriteInstallerRootEntry(ZipArchive archive, string name, byte[] bytes)
		{
			using (Stream stream = archive.CreateEntry(name).Open())
				stream.Write(bytes, 0, bytes.Length);
		}

		/// <summary>Releases the shared metadata connection so the temporary regression fixture can be removed.</summary>
		private static void ReleaseInstallerRootMetadataDatabase(string cacheDirectory)
		{
			Type cacheType = typeof(FOMod).Assembly.GetType("Nexus.Client.Mods.Formats.FOMod.FOModArchiveMetadataCache", true);
			object databaseLock = cacheType.GetField("DatabaseLock", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
			var databases = (System.Collections.IDictionary)cacheType.GetField("Databases", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
			string databasePath = Path.Combine(cacheDirectory, "fomodArchiveMetadata.sqlite");
			lock (databaseLock)
			{
				object database = databases[databasePath];
				if (database == null) return;
				Type databaseType = database.GetType();
				object syncRoot = databaseType.GetField("SyncRoot", BindingFlags.Instance | BindingFlags.Public).GetValue(database);
				lock (syncRoot)
				{
					cacheType.GetMethod("CommitDatabase", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { database });
					((IDisposable)databaseType.GetField("Connection", BindingFlags.Instance | BindingFlags.Public).GetValue(database)).Dispose();
				}
				databases.Remove(databasePath);
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

		private interface IModWithInstallerSourceInventory : IMod, IModInstallerSourceInventory
		{
		}

		/// <summary>
		/// Emulates the real FOMod behavior where ordinary enumeration hides all fomod/*
		/// entries but the native installer can access them through exact logical paths.
		/// </summary>
		private static IMod CreateModWithHiddenFomodAssets(XmlScript script, params string[] archiveFiles)
		{
			var files = new List<string>(archiveFiles);
			return InterfaceStub<IModWithInstallerSourceInventory>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_HasInstallScript": return true;
					case "get_InstallScript": return script;
					case "get_Filename": return "FomodFixture.7z";
					case "GetFileList":
						return FilterFixtureFiles(files.Where(file =>
							!file.Replace('/', '\\').StartsWith(@"fomod\", StringComparison.OrdinalIgnoreCase)), args);
					case "GetInstallerSourceFiles":
						return FilterFixtureFiles(files, args);
					default: return null;
				}
			});
		}

		private static List<string> FilterFixtureFiles(IEnumerable<string> files, object[] args)
		{
			if (args.Length == 0 || String.IsNullOrEmpty(args[0] as string))
				return files.ToList();
			string prefix = ((string)args[0]).Replace('/', '\\').TrimEnd('\\') + @"\";
			return files.Where(file => file.Replace('/', '\\').StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
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

		private sealed class TestConditionEnvironment : IModInstallationConditionEnvironment
		{
			private readonly HashSet<string> _registered;
			private readonly HashSet<string> _active;
			private readonly bool _proveAbsence;

			public TestConditionEnvironment(IEnumerable<string> registered, IEnumerable<string> active, bool proveAbsence = true)
			{
				_registered = new HashSet<string>(registered ?? new string[0], StringComparer.OrdinalIgnoreCase);
				_active = new HashSet<string>(active ?? new string[0], StringComparer.OrdinalIgnoreCase);
				_proveAbsence = proveAbsence;
			}

			public IReadOnlyList<string> RegisteredPlugins { get { return _registered.OrderBy(x => x).ToArray(); } }
			public IReadOnlyList<string> ActivePlugins { get { return _active.OrderBy(x => x).ToArray(); } }

			public bool TryGetDataFileExists(ModDeploymentRoot root, string relativePath, out bool exists)
			{
				exists = false;
				return false;
			}

			public bool TryGetIniString(string settingsFileName, string section, string key, out string value)
			{
				value = null;
				return false;
			}

			public bool TryGetPluginRegistered(string pluginPath, out bool registered)
			{
				registered = _registered.Contains(Path.GetFileName(pluginPath));
				return registered || _proveAbsence;
			}

			public bool TryGetPluginActive(string pluginPath, out bool active)
			{
				active = _active.Contains(Path.GetFileName(pluginPath));
				return active || _proveAbsence;
			}
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
