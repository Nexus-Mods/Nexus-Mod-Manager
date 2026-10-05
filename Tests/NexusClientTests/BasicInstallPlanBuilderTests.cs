using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.Mods;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// Verifies C6.15.7 deterministic BasicInstall expansion without executing installer mutation.
	/// </summary>
	[TestFixture]
	public class BasicInstallPlanBuilderTests
	{
		/// <summary>
		/// Verifies ordinary Virtual planning shares ignored-file, readme, path-adjustment and deployment-target semantics.
		/// </summary>
		[Test]
		public void Build_VirtualData_ProducesBoundedAdjustedMappings()
		{
			IMod mod = CreateMod("Normal.7z", "meta.ini", "readme.txt", @"meshes\body.nif");
			IGameMode gameMode = CreateGameMode();
			var context = new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data);

			BasicInstallPlanResult result = new BasicInstallPlanBuilder().Build(mod, gameMode, context, true);

			Assert.That(result.IsSupported, Is.True);
			Assert.That(result.UnsupportedReason, Is.EqualTo(BasicInstallPlanUnsupportedReasonKind.None));
			Assert.That(result.Plan.InstallContext, Is.SameAs(context));
			Assert.That(result.Plan.Files.Count, Is.EqualTo(1));
			BasicInstallPlanFile file = result.Plan.Files[0];
			Assert.That(file.SourcePath, Is.EqualTo(@"meshes\body.nif"));
			Assert.That(file.DestinationPath, Is.EqualTo(@"meshes\body.nif"));
			Assert.That(file.GameInstallPath, Is.EqualTo(@"Game\meshes\body.nif"));
			Assert.That(file.VirtualStoragePath, Is.EqualTo(@"meshes\body.nif"));
			Assert.That(file.DeploymentTarget.Root, Is.EqualTo(ModDeploymentRoot.Data));
			Assert.That(file.DeploymentTarget.RelativePath, Is.EqualTo(@"Target\meshes\body.nif"));
		}

		/// <summary>
		/// Verifies GameRoot wrapper stripping is represented in the final explicit destinations without Data-path rewriting.
		/// </summary>
		[Test]
		public void Build_DirectGameRoot_StripsRecognizedWrapper()
		{
			IMod mod = CreateMod("SKSE64.7z",
				@"skse64_2_02_06_gog\skse64_loader.exe",
				@"skse64_2_02_06_gog\Data\Scripts\Test.pex");
			IGameMode gameMode = CreateGameMode();
			var context = new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.GameRoot);

			BasicInstallPlanResult result = new BasicInstallPlanBuilder().Build(mod, gameMode, context, false);

			Assert.That(result.IsSupported, Is.True);
			CollectionAssert.AreEqual(
				new[] { "skse64_loader.exe", @"Data\Scripts\Test.pex" },
				result.Plan.Files.Select(file => file.DestinationPath).ToArray());
			Assert.That(result.Plan.Files.All(file => file.VirtualStoragePath == null), Is.True);
			Assert.That(result.Plan.Files.All(file => file.DeploymentTarget.Root == ModDeploymentRoot.GameRoot), Is.True);
			CollectionAssert.AreEqual(
				new[] { "skse64_loader.exe", @"Data\Scripts\Test.pex" },
				result.Plan.Files.Select(file => file.DeploymentTarget.RelativePath).ToArray());
		}

		/// <summary>
		/// Verifies native GameRoot wrapper recognition covers the root markers used by the characterized Vortex dinput/enb members.
		/// </summary>
		[TestCase(@"f4se_0_07_09\f4se_loader.exe", "f4se_loader.exe")]
		[TestCase(@"EngineInjector\dinput8.dll", "dinput8.dll")]
		[TestCase(@"ENBWrapper\enbseries.ini", "enbseries.ini")]
		public void Build_DirectGameRoot_StripsCharacterizedRootTypeWrappers(string sourcePath, string destinationPath)
		{
			IMod mod = CreateMod("RootType.7z", sourcePath);
			var context = new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.GameRoot);

			BasicInstallPlanResult result = new BasicInstallPlanBuilder().Build(mod, CreateGameMode(), context, false);

			Assert.That(result.IsSupported, Is.True);
			Assert.That(result.Plan.Files.Single().DestinationPath, Is.EqualTo(destinationPath));
			Assert.That(result.Plan.Files.Single().DeploymentTarget.Root, Is.EqualTo(ModDeploymentRoot.GameRoot));
		}

		/// <summary>
		/// Verifies a special-file game reports the unsupported behavior without invoking its mutating transformation.
		/// </summary>
		[Test]
		public void Build_SpecialFileInstallation_FailsClosedWithoutExecutingTransformation()
		{
			bool specialInstallCalled = false;
			IGameMode gameMode = CreateGameMode(specialFile: true, onSpecialInstall: () => specialInstallCalled = true);

			BasicInstallPlanResult result = new BasicInstallPlanBuilder().Build(
				CreateMod("Special.7z", "special.bin"), gameMode,
				new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), false);

			Assert.That(result.IsSupported, Is.False);
			Assert.That(result.UnsupportedReason, Is.EqualTo(BasicInstallPlanUnsupportedReasonKind.SpecialFileInstallation));
			Assert.That(specialInstallCalled, Is.False);
		}

		[Test]
		public void Build_DeterministicSpecialFileInstallation_UsesReadOnlySelectionWithoutExecutingMutation()
		{
			bool specialInstallCalled = false;
			IGameMode gameMode = CreateGameMode(specialFile: true, onSpecialInstall: () => specialInstallCalled = true,
				deterministicSpecialFiles: new[] { @"Current\payload.bin" });

			BasicInstallPlanResult result = new BasicInstallPlanBuilder().Build(
				CreateMod("Special.7z", @"Legacy\old.pak", @"Current\payload.bin"), gameMode,
				new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), false,
				new[] { new KeyValuePair<string, string>(@"Legacy\old.pak", @"Ignored\override.pak") });

			Assert.That(result.IsSupported, Is.True);
			Assert.That(result.Plan.Files.Count, Is.EqualTo(1));
			Assert.That(result.Plan.Files[0].SourcePath, Is.EqualTo(@"Current\payload.bin"));
			Assert.That(specialInstallCalled, Is.False);
		}

		/// <summary>
		/// Verifies a game requiring ModFileMerge is blocked without invoking the mutation during planning.
		/// </summary>
		[Test]
		public void Build_DeterministicSpecialFileInstallation_CapturesExactGameSpecificValues()
		{
			byte[] expected = new byte[] { 9, 8, 7, 6 };
			IGameMode gameMode = CreateGameMode(specialFile: true,
				deterministicSpecialFiles: new[] { @"Mods\payload.pak" },
				deterministicSpecialGameValues: new[] { new BasicInstallGameSpecificValue("profile|module", expected) });

			BasicInstallPlanResult result = new BasicInstallPlanBuilder().Build(
				CreateMod("Special.7z", "info.json", @"Mods\payload.pak"), gameMode,
				new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), false);

			Assert.That(result.IsSupported, Is.True);
			Assert.That(result.Plan.Files.Count, Is.EqualTo(1));
			Assert.That(result.Plan.GameSpecificValues.Count, Is.EqualTo(1));
			Assert.That(result.Plan.GameSpecificValues[0].Key, Is.EqualTo("profile|module"));
			CollectionAssert.AreEqual(expected, result.Plan.GameSpecificValues[0].Value);
		}

		[Test]
		public void Build_ModFileMerge_FailsClosedWithoutExecutingMerge()
		{
			bool mergeCalled = false;
			IGameMode gameMode = CreateGameMode(requiresMerge: true, onMerge: () => mergeCalled = true);

			BasicInstallPlanResult result = new BasicInstallPlanBuilder().Build(
				CreateMod("Merge.7z", "merged.dat"), gameMode,
				new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data), false);

			Assert.That(result.IsSupported, Is.False);
			Assert.That(result.UnsupportedReason, Is.EqualTo(BasicInstallPlanUnsupportedReasonKind.ModFileMerge));
			Assert.That(mergeCalled, Is.False);
		}

		[Test]
		public void Build_DeterministicModFileMerge_ReplacesMergedArchiveSourceWithGeneratedOutput()
		{
			byte[] merged = new byte[] { 1, 2, 3, 4 };
			var mergePlan = new DeterministicModFileMergePlan("chargenmorphcfg.xml",
				@"NMM_chargenmorphcfg\chargenmorphcfg.xml", merged);
			IGameMode gameMode = CreateGameMode(requiresMerge: true, deterministicMergePlan: mergePlan);

			BasicInstallPlanResult result = new BasicInstallPlanBuilder().Build(
				CreateMod("Merge.7z", "chargenmorphcfg.xml", @"textures\body.dds"), gameMode,
				new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), false, null, new List<IMod>());

			Assert.That(result.IsSupported, Is.True);
			Assert.That(result.Plan.Files.Select(x => x.SourcePath), Is.EquivalentTo(new[] { @"textures\body.dds" }));
			Assert.That(result.Plan.GeneratedFiles.Count, Is.EqualTo(1));
			Assert.That(result.Plan.GeneratedFiles[0].SourcePath, Is.EqualTo("chargenmorphcfg.xml"));
			Assert.That(result.Plan.GeneratedFiles[0].DestinationPath, Is.EqualTo(@"NMM_chargenmorphcfg\chargenmorphcfg.xml"));
			CollectionAssert.AreEqual(merged, result.Plan.GeneratedFiles[0].Data);
		}

		[Test]
		public void DeploymentTargetResolver_DazipStyleSecondaryRoutingKeepsGeneratedMergeOnPrimaryRoot()
		{
			const string mergedPath = @"NMM_chargenmorphcfg\chargenmorphcfg.xml";
			IGameMode gameMode = CreateGameMode(hasSecondaryInstallPath: true,
				checkSecondaryInstall: (mod, path) => !path.EndsWith(mergedPath, StringComparison.OrdinalIgnoreCase));
			IMod mod = CreateMod("Contributor.dazip", "chargenmorphcfg.xml", @"textures\body.dds");

			ModDeploymentTarget archiveTarget = ModDeploymentTargetResolver.Resolve(gameMode, mod, @"textures\body.dds", ModInstallRoot.Data);
			ModDeploymentTarget mergeTarget = ModDeploymentTargetResolver.Resolve(gameMode, mod, mergedPath, ModInstallRoot.Data);

			Assert.That(archiveTarget.Root, Is.EqualTo(ModDeploymentRoot.Secondary));
			Assert.That(mergeTarget.Root, Is.EqualTo(ModDeploymentRoot.Data));
		}

		[Test]
		public void Build_DeterministicModFileMerge_DeferredContributorOmitsSharedGeneratedOutput()
		{
			var mergePlan = new DeterministicModFileMergePlan("chargenmorphcfg.xml",
				@"NMM_chargenmorphcfg\chargenmorphcfg.xml", new byte[] { 5, 6, 7 });
			IGameMode gameMode = CreateGameMode(requiresMerge: true, deterministicMergePlan: mergePlan);

			BasicInstallPlanResult result = new BasicInstallPlanBuilder().Build(
				CreateMod("First.7z", "chargenmorphcfg.xml", @"textures\body.dds"), gameMode,
				new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), false, null, new List<IMod>(), false);

			Assert.That(result.IsSupported, Is.True);
			Assert.That(result.Plan.Files.Select(x => x.SourcePath), Is.EquivalentTo(new[] { @"textures\body.dds" }));
			Assert.That(result.Plan.GeneratedFiles, Is.Empty);
		}

		[Test]
		public void Build_DeterministicModFileMerge_DeferredOnlyEffectFailsClosed()
		{
			var mergePlan = new DeterministicModFileMergePlan("chargenmorphcfg.xml",
				@"NMM_chargenmorphcfg\chargenmorphcfg.xml", new byte[] { 8, 9 });
			IGameMode gameMode = CreateGameMode(requiresMerge: true, deterministicMergePlan: mergePlan);

			BasicInstallPlanResult result = new BasicInstallPlanBuilder().Build(
				CreateMod("OnlyMerge.7z", "chargenmorphcfg.xml"), gameMode,
				new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data), false, null, new List<IMod>(), false);

			Assert.That(result.IsSupported, Is.False);
			Assert.That(result.UnsupportedReason, Is.EqualTo(BasicInstallPlanUnsupportedReasonKind.ModFileMerge));
		}


		/// <summary>
		/// Verifies a game-specific Virtual staging remap remains a bounded BasicInstall mapping.
		/// The exact-file executor now reproduces the same VirtualStorage projection at execution time.
		/// </summary>
		[Test]
		public void Build_VirtualStorageRemap_IsSupportedAndRetained()
		{
			BasicInstallPlanResult result = new BasicInstallPlanBuilder().Build(
				CreateMod("Remap.7z", @"meshes\body.nif"), CreateGameMode(remapVirtualStorage: true),
				new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), false);

			Assert.That(result.IsSupported, Is.True);
			Assert.That(result.UnsupportedReason, Is.EqualTo(BasicInstallPlanUnsupportedReasonKind.None));
			Assert.That(result.Plan.Files.Count, Is.EqualTo(1));
			Assert.That(result.Plan.Files[0].DestinationPath, Is.EqualTo(@"meshes\body.nif"));
			Assert.That(result.Plan.Files[0].VirtualStoragePath, Is.EqualTo(@"Virtual\meshes\body.nif"));
		}

		/// <summary>
		/// Verifies layouts that cannot use the existing one-to-one C5 adapter remain explicit unsupported capability cases.
		/// </summary>
		[Test]
		public void Build_DestinationCollision_FailsClosed()
		{
			var files = new[]
			{
				new KeyValuePair<string, string>(@"one\first.bin", @"target\same.bin"),
				new KeyValuePair<string, string>(@"two\second.bin", @"target\same.bin")
			};

			BasicInstallPlanResult result = new BasicInstallPlanBuilder().Build(
				CreateMod("Collision.7z", @"one\first.bin", @"two\second.bin"), CreateGameMode(),
				new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), false, files);

			Assert.That(result.IsSupported, Is.False);
			Assert.That(result.UnsupportedReason, Is.EqualTo(BasicInstallPlanUnsupportedReasonKind.DestinationCollision));
		}

		/// <summary>
		/// Verifies unsafe GameRoot paths retain the existing native fail-closed validation.
		/// </summary>
		[Test]
		public void Build_GameRootTraversal_IsRejected()
		{
			Assert.Throws<InvalidDataException>(() => new BasicInstallPlanBuilder().Build(
				CreateMod("Unsafe.7z", @"..\outside.dll"), CreateGameMode(),
				new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.GameRoot), false));
		}

		/// <summary>
		/// Verifies a bounded BasicInstall plan feeds the existing C5 simple-file adapter and never emits PerformBasicInstallOperation.
		/// </summary>
		[Test]
		public void CreateSimpleFileRecipe_TranslatesToExistingExplicitFileOperations()
		{
			var context = new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data);
			BasicInstallPlanResult result = new BasicInstallPlanBuilder().Build(
				CreateMod("Normal.7z", @"meshes\body.nif", @"textures\body.dds"), CreateGameMode(), context, false);
			Assert.That(result.IsSupported, Is.True);
			ModInstallationSimpleFileRecipe recipe = result.Plan.CreateSimpleFileRecipe();
			ModInstallationRecipeInput input = CreateRecipeInput(recipe, context);

			ModInstallationRecipeInput translated = new ModInstallationSimpleFileRecipeAdapter().Translate(input, recipe);

			Assert.That(translated.NativeOperations.Count, Is.EqualTo(2));
			Assert.That(translated.NativeOperations.All(operation => operation is InstallModFileOperation), Is.True);
			Assert.That(translated.NativeOperations.Any(operation => operation is PerformBasicInstallOperation), Is.False);
		}

		/// <summary>
		/// Verifies exact-file execution derives the same VirtualStorage staging projection used by BasicInstall planning.
		/// </summary>
		[Test]
		public void StagingResolver_InstallRootAware_UsesVirtualStorageProjection()
		{
			IMod mod = CreateMod("Remap.7z", @"meshes\body.nif");
			IGameMode gameMode = CreateGameMode(remapVirtualStorage: true);
			IVirtualModActivator virtualActivator = InterfaceStub<IVirtualModActivator>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_MultiHDMode":
						return false;
					case "get_VirtualPath":
						return @"C:\Virtual";
					case "get_HDLinkFolder":
						return @"C:\Links";
					default:
						return null;
				}
			});

			string dataRootPath = ScriptedInstallStagingPathResolver.GetStagingPath(
				mod, gameMode, virtualActivator, @"meshes\body.nif", ModInstallRoot.Data, false);
			string gameRootPath = ScriptedInstallStagingPathResolver.GetStagingPath(
				mod, gameMode, virtualActivator, @"meshes\body.nif", ModInstallRoot.GameRoot, false);

			Assert.That(dataRootPath, Is.EqualTo(Path.Combine(@"C:\Virtual", "Remap", @"Virtual\meshes\body.nif")));
			Assert.That(gameRootPath, Is.EqualTo(Path.Combine(@"C:\Virtual", "Remap", @"meshes\body.nif")));
		}

		private static IMod CreateMod(string fileName, params string[] files)
		{
			List<string> archiveFiles = files.ToList();
			return InterfaceStub<IMod>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_Filename":
						return @"C:\Mods\" + fileName;
					case "get_FileName":
						return fileName;
					case "GetFileList":
						return new List<string>(archiveFiles);
					default:
						return null;
				}
			});
		}

		private interface ITestGameMode : IGameMode, IDeterministicSpecialFileInstallPlanProvider,
			IDeterministicSpecialFileGameValuePlanProvider, IDeterministicModFileMergePlanProvider
		{
		}

		private static IGameMode CreateGameMode(bool specialFile = false, bool requiresMerge = false,
			Action onSpecialInstall = null, Action onMerge = null, bool remapVirtualStorage = false,
			IEnumerable<string> deterministicSpecialFiles = null, DeterministicModFileMergePlan deterministicMergePlan = null,
			bool hasSecondaryInstallPath = false, Func<IMod, string, bool> checkSecondaryInstall = null,
			IEnumerable<BasicInstallGameSpecificValue> deterministicSpecialGameValues = null)
		{
			return InterfaceStub<ITestGameMode>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_Name":
						return "Test Game";
					case "get_PluginDirectory":
						return @"C:\Game\Data";
					case "get_RequiresSpecialFileInstallation":
						return specialFile;
					case "IsSpecialFile":
						return specialFile;
					case "SpecialFileInstall":
						onSpecialInstall?.Invoke();
						return new[] { "transformed.bin" };
					case "GetDeterministicSpecialFileInstallPlan":
						return deterministicSpecialFiles;
					case "GetDeterministicSpecialFileGameValues":
						return deterministicSpecialGameValues ?? new BasicInstallGameSpecificValue[0];
					case "IsDeterministicSpecialFileGameValueKey": return args[0] is string;
					case "get_RequiresModFileMerge":
						return requiresMerge;
					case "ModFileMerge":
						onMerge?.Invoke();
						return null;
					case "GetDeterministicModFileMergePlan":
						return deterministicMergePlan;
					case "get_HasSecondaryInstallPath":
						return hasSecondaryInstallPath;
					case "CheckSecondaryInstall":
						return checkSecondaryInstall != null && checkSecondaryInstall((IMod)args[0], (string)args[1]);
					case "GetModFormatAdjustedPath":
						return AdjustPath(args, remapVirtualStorage);
					default:
						return null;
				}
			});
		}

		private static string AdjustPath(object[] args, bool remapVirtualStorage)
		{
			string path = (string)args[1];
			if (args.Length == 4 && args[3] is ModPathContext)
				return @"Game\" + path;
			if (args.Length == 3 && args[2] is ModPathContext)
				return remapVirtualStorage ? @"Virtual\" + path : path;
			if (args.Length == 4 && args[3] is bool)
				return @"Target\" + path;
			if (args.Length == 3 && args[2] is bool)
				return @"Target\" + path;
			return path;
		}

		private static ModInstallationRecipeInput CreateRecipeInput(ModInstallationSimpleFileRecipe recipe, ModInstallContext context)
		{
			var paths = new List<ModInstallationRecipePath>();
			foreach (ModInstallationSimpleFileMapping mapping in recipe.Mappings)
			{
				paths.Add(new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, mapping.SourcePath));
				paths.Add(new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, mapping.DestinationPath));
			}

			var operation = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint("target-sha256:" + new string('a', 64), context, "recipe:c6.15.7-basic-v1"));
			var validation = new ModInstallationRecipeValidation(
				ModInstallationSimpleFileRecipeAdapter.AdapterId,
				ModInstallationSimpleFileRecipeAdapter.AdapterVersion,
				context,
				new ModInstallationRecipeExpectedContent(new string('b', 64), 4096),
				new[]
				{
					new ModInstallationRecipeCapability(ModInstallationSimpleFileRecipeAdapter.CapabilityId,
						ModInstallationSimpleFileRecipeAdapter.CapabilityVersion)
				},
				paths);
			return new ModInstallationRecipeInput(operation, validation);
		}
	}
}
