using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
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

		/// <summary>
		/// Verifies a game requiring ModFileMerge is blocked without invoking the mutation during planning.
		/// </summary>
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


		/// <summary>
		/// Verifies a game-specific Virtual staging remap is not silently collapsed into the one-path C5 file operation.
		/// </summary>
		[Test]
		public void Build_VirtualStorageRemap_FailsClosed()
		{
			BasicInstallPlanResult result = new BasicInstallPlanBuilder().Build(
				CreateMod("Remap.7z", @"meshes\body.nif"), CreateGameMode(remapVirtualStorage: true),
				new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), false);

			Assert.That(result.IsSupported, Is.False);
			Assert.That(result.UnsupportedReason, Is.EqualTo(BasicInstallPlanUnsupportedReasonKind.UnrepresentableVirtualStoragePath));
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

		private static IGameMode CreateGameMode(bool specialFile = false, bool requiresMerge = false,
			Action onSpecialInstall = null, Action onMerge = null, bool remapVirtualStorage = false)
		{
			return InterfaceStub<IGameMode>.Create((method, args) =>
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
					case "get_RequiresModFileMerge":
						return requiresMerge;
					case "ModFileMerge":
						onMerge?.Invoke();
						return null;
					case "get_HasSecondaryInstallPath":
						return false;
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
