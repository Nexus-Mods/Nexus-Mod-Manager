using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using ChinhDo.Transactions;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.Mods;
using Nexus.Client.Util.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// Verifies C5.5 execution, C5.7 replay publication, C5.10 final plugin reconciliation and C5.11 replay collision guarding through the native installer path.
	/// </summary>
	[TestFixture]
	public class ModInstallationRecipeExecutionTests
	{
		/// <summary>
		/// Verifies a translated recipe is executed only after the native installer reaches its registration boundary.
		/// </summary>
		[Test]
		public void RunTasks_TranslatedRecipeExecutesThroughNativeInstallerAfterRegistration()
		{
			using (TemporaryDirectory tmp = new TemporaryDirectory())
			{
				var context = new ScriptProxyContext(tmp.Path, null, false, false, "linked");
				byte[] archiveBytes = { 1, 2, 3, 4, 5 };
				File.WriteAllBytes(context.Mod.Filename, archiveBytes);
				ModInstallationRecipeInput recipeInput = CreateTranslatedInput(
					archiveBytes,
					new ModInstallationSimpleFileMapping(@"source\one.bin", @"target\one.bin"));
				var installer = new RecipeExecutionTestInstaller(context, recipeInput);

				installer.ExecuteSynchronously();

				Assert.That(installer.Registered, Is.True);
				Assert.That(installer.Succeeded, Is.True);
				Assert.That(context.FileInstaller.InstallCallCount, Is.EqualTo(1));
				Assert.That(context.LinkCallCount, Is.EqualTo(1));
				Assert.That(context.LastLinkedDestination, Is.EqualTo(@"target\one.bin"));
			}
		}

		/// <summary>
		/// Verifies Direct recipes use the same native typed-operation executor without entering the Virtual link path.
		/// </summary>
		[Test]
		public void RunTasks_DirectRecipeExecutesThroughNativeFileInstaller()
		{
			using (TemporaryDirectory tmp = new TemporaryDirectory())
			{
				var context = new ScriptProxyContext(tmp.Path, null, false, false, "linked");
				byte[] archiveBytes = { 31, 32, 33, 34 };
				File.WriteAllBytes(context.Mod.Filename, archiveBytes);
				var installContext = new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.GameRoot);
				ModInstallationRecipeInput recipeInput = CreateTranslatedInput(
					archiveBytes,
					new ModInstallationSimpleFileMapping(@"source\direct.bin", @"target\direct.bin"),
					installContext);
				var installer = new RecipeExecutionTestInstaller(context, recipeInput);

				installer.ExecuteSynchronously();

				Assert.That(installer.Registered, Is.True);
				Assert.That(installer.Succeeded, Is.True);
				Assert.That(context.FileInstaller.InstallCallCount, Is.EqualTo(1));
				Assert.That(context.FileInstaller.LastInstallPath, Is.EqualTo(@"target\direct.bin"));
				Assert.That(context.LinkCallCount, Is.EqualTo(0));
			}
		}

		/// <summary>
		/// Verifies recipe execution cannot bypass the exact C3 operation/attempt identity carried by the recipe input.
		/// </summary>
		[Test]
		public void RunTasks_MissingAssignedOperationIdentityFailsBeforeNativeMutation()
		{
			using (TemporaryDirectory tmp = new TemporaryDirectory())
			{
				var context = new ScriptProxyContext(tmp.Path, null, false, false, "linked");
				byte[] archiveBytes = { 41, 42, 43, 44 };
				File.WriteAllBytes(context.Mod.Filename, archiveBytes);
				ModInstallationRecipeInput recipeInput = CreateTranslatedInput(
					archiveBytes,
					new ModInstallationSimpleFileMapping(@"source\one.bin", @"target\one.bin"));
				string replayPath = CreateLiveReplay(tmp.Path);
				var installer = new RecipeExecutionTestInstaller(context, recipeInput, false);

				installer.ExecuteSynchronously();

				Assert.That(installer.Registered, Is.False);
				Assert.That(installer.Succeeded, Is.False);
				Assert.That(context.FileInstaller.InstallCallCount, Is.EqualTo(0));
				Assert.That(File.Exists(replayPath), Is.True);
			}
		}

		/// <summary>
		/// Verifies untranslated recipe input fails closed before replay deletion, registration, or file installation.
		/// </summary>
		[Test]
		public void RunTasks_UntranslatedRecipeFailsBeforeNativeMutation()
		{
			using (TemporaryDirectory tmp = new TemporaryDirectory())
			{
				var context = new ScriptProxyContext(tmp.Path, null, false, false, "linked");
				byte[] archiveBytes = { 6, 7, 8, 9 };
				File.WriteAllBytes(context.Mod.Filename, archiveBytes);
				ModInstallationSimpleFileMapping mapping = new ModInstallationSimpleFileMapping(@"source\one.bin", @"target\one.bin");
				ModInstallationRecipeInput recipeInput = CreateInput(archiveBytes, mapping);
				string replayPath = CreateLiveReplay(tmp.Path);
				var installer = new RecipeExecutionTestInstaller(context, recipeInput);

				installer.ExecuteSynchronously();

				Assert.That(installer.Registered, Is.False);
				Assert.That(installer.Succeeded, Is.False);
				Assert.That(context.FileInstaller.InstallCallCount, Is.EqualTo(0));
				Assert.That(File.Exists(replayPath), Is.True);
			}
		}

		/// <summary>
		/// Verifies an exact-content mismatch is rejected before the installer mutates native ownership or replay state.
		/// </summary>
		[Test]
		public void RunTasks_ContentMismatchFailsBeforeNativeMutation()
		{
			using (TemporaryDirectory tmp = new TemporaryDirectory())
			{
				var context = new ScriptProxyContext(tmp.Path, null, false, false, "linked");
				byte[] expectedBytes = { 10, 11, 12, 13 };
				byte[] actualBytes = { 10, 11, 12, 14 };
				File.WriteAllBytes(context.Mod.Filename, actualBytes);
				ModInstallationRecipeInput recipeInput = CreateTranslatedInput(
					expectedBytes,
					new ModInstallationSimpleFileMapping(@"source\one.bin", @"target\one.bin"));
				string replayPath = CreateLiveReplay(tmp.Path);
				var installer = new RecipeExecutionTestInstaller(context, recipeInput);

				installer.ExecuteSynchronously();

				Assert.That(installer.Registered, Is.False);
				Assert.That(installer.Succeeded, Is.False);
				Assert.That(context.FileInstaller.InstallCallCount, Is.EqualTo(0));
				Assert.That(File.Exists(replayPath), Is.True);
			}
		}

		/// <summary>
		/// Verifies two distinct active native archives cannot alias the same basename-derived live replay before mutation.
		/// </summary>
		[Test]
		public void RunTasks_DistinctActiveArchiveWithSameReplayBasenameFailsBeforeNativeMutation()
		{
			using (TemporaryDirectory tmp = new TemporaryDirectory())
			{
				var context = new ScriptProxyContext(tmp.Path, null, false, false, "linked");
				byte[] archiveBytes = { 15, 16, 17, 18 };
				File.WriteAllBytes(context.Mod.Filename, archiveBytes);
				IMod collidingActiveMod = CreateModStub(Path.Combine(tmp.Path, "Other", "ExampleMod.7z"));
				ModInstallationRecipeInput recipeInput = CreateTranslatedInput(
					archiveBytes,
					new ModInstallationSimpleFileMapping(@"source\one.bin", @"target\one.bin"));
				string replayPath = CreateLiveReplay(tmp.Path);
				var installer = new RecipeExecutionTestInstaller(context, recipeInput, true, null, new[] { collidingActiveMod });

				installer.ExecuteSynchronously();

				Assert.That(installer.Registered, Is.False);
				Assert.That(installer.Succeeded, Is.False);
				Assert.That(context.FileInstaller.InstallCallCount, Is.EqualTo(0));
				Assert.That(File.Exists(replayPath), Is.True);
				Assert.That(installer.CompletionMessage, Does.Contain("legacy replay basename"));
			}
		}

		/// <summary>
		/// Verifies an unrelated active archive with a different replay basename does not block recipe execution.
		/// </summary>
		[Test]
		public void RunTasks_DifferentReplayBasenameDoesNotBlockRecipeExecution()
		{
			using (TemporaryDirectory tmp = new TemporaryDirectory())
			{
				var context = new ScriptProxyContext(tmp.Path, null, false, false, "linked");
				byte[] archiveBytes = { 19, 20, 21, 22 };
				File.WriteAllBytes(context.Mod.Filename, archiveBytes);
				IMod unrelatedActiveMod = CreateModStub(Path.Combine(tmp.Path, "Other", "DifferentMod.zip"));
				ModInstallationRecipeInput recipeInput = CreateTranslatedInput(
					archiveBytes,
					new ModInstallationSimpleFileMapping(@"source\one.bin", @"target\one.bin"));
				var installer = new RecipeExecutionTestInstaller(context, recipeInput, true, null, new[] { unrelatedActiveMod });

				installer.ExecuteSynchronously();

				Assert.That(installer.Registered, Is.True);
				Assert.That(installer.Succeeded, Is.True);
				Assert.That(context.FileInstaller.InstallCallCount, Is.EqualTo(1));
			}
		}

		/// <summary>
		/// Verifies the C5.5 executor preserves the translated operation sequence and does not stop after the first mapping.
		/// </summary>
		[Test]
		public void RunTasks_ExecutesEveryTranslatedSimpleFileOperation()
		{
			using (TemporaryDirectory tmp = new TemporaryDirectory())
			{
				var context = new ScriptProxyContext(tmp.Path, null, false, false, "linked");
				byte[] archiveBytes = { 20, 21, 22, 23, 24 };
				File.WriteAllBytes(context.Mod.Filename, archiveBytes);
				var recipe = new ModInstallationSimpleFileRecipe(new[]
				{
					new ModInstallationSimpleFileMapping(@"source\one.bin", @"target\one.bin"),
					new ModInstallationSimpleFileMapping(@"source\two.bin", @"target\two.bin")
				});
				ModInstallationRecipeInput recipeInput = Translate(CreateInput(archiveBytes, recipe), recipe);
				var installer = new RecipeExecutionTestInstaller(context, recipeInput);

				installer.ExecuteSynchronously();

				Assert.That(installer.Succeeded, Is.True);
				Assert.That(context.FileInstaller.InstallCallCount, Is.EqualTo(2));
				Assert.That(context.LinkCallCount, Is.EqualTo(2));
				Assert.That(context.LastLinkedDestination, Is.EqualTo(@"target\two.bin"));
			}
		}

		/// <summary>
		/// Verifies successful recipe execution publishes the exact archive-file mapping through the native live replay cache.
		/// </summary>
		[Test]
		public void RunTasks_RecipePublishesNativeArchiveReplay()
		{
			using (TemporaryDirectory tmp = new TemporaryDirectory())
			{
				var context = new ScriptProxyContext(tmp.Path, null, false, false, "linked");
				byte[] archiveBytes = { 51, 52, 53, 54 };
				File.WriteAllBytes(context.Mod.Filename, archiveBytes);
				ModInstallationRecipeInput recipeInput = CreateTranslatedInput(
					archiveBytes,
					new ModInstallationSimpleFileMapping(@"source\replay.bin", @"target\replay.bin"));
				var installer = new RecipeExecutionTestInstaller(context, recipeInput);

				installer.ExecuteSynchronously();

				var replay = new ScriptedFileSelectionCache(context.Mod, context.GameMode);
				Assert.That(installer.Succeeded, Is.True);
				Assert.That(replay.HasCompleteReplay, Is.True);
				IReadOnlyList<ScriptedReplayOperation> operations = replay.LoadReplayOperations();
				Assert.That(operations.Count, Is.EqualTo(1));
				Assert.That(operations[0].Kind, Is.EqualTo(ScriptedReplayOperationKind.ArchiveFile));
				Assert.That(operations[0].SourcePath, Is.EqualTo(@"source\replay.bin"));
				Assert.That(operations[0].DestinationPath, Is.EqualTo(@"target\replay.bin"));
			}
		}

		/// <summary>
		/// Verifies generated recipe output is published as the native replay entry plus validated payload sidecar bytes.
		/// </summary>
		[Test]
		public void RunTasks_GeneratedRecipeOperationPublishesReplayPayloadSidecar()
		{
			using (TemporaryDirectory tmp = new TemporaryDirectory())
			{
				var context = new ScriptProxyContext(tmp.Path, null, false, false, "linked");
				byte[] archiveBytes = { 61, 62, 63, 64 };
				byte[] generatedBytes = { 7, 1, 7, 2, 7, 3 };
				const string destination = @"config\generated.bin";
				File.WriteAllBytes(context.Mod.Filename, archiveBytes);
				ModInstallationRecipeInput recipeInput = CreateGeneratedPlanInput(archiveBytes, destination, generatedBytes);
				var installer = new RecipeExecutionTestInstaller(context, recipeInput);

				installer.ExecuteSynchronously();

				var replay = new ScriptedFileSelectionCache(context.Mod, context.GameMode);
				Assert.That(installer.Succeeded, Is.True);
				Assert.That(context.FileInstaller.GenerateCallCount, Is.EqualTo(1));
				Assert.That(context.LinkCallCount, Is.EqualTo(0));
				Assert.That(replay.HasCompleteReplay, Is.True);
				IReadOnlyList<ScriptedReplayOperation> operations = replay.LoadReplayOperations();
				Assert.That(operations.Count, Is.EqualTo(1));
				Assert.That(operations[0].Kind, Is.EqualTo(ScriptedReplayOperationKind.GeneratedFile));
				Assert.That(operations[0].DestinationPath, Is.EqualTo(destination));
				Assert.That(operations[0].PayloadLength, Is.EqualTo(generatedBytes.LongLength));
				Assert.That(operations[0].PayloadHash, Is.EqualTo(ComputeSha256Hex(generatedBytes)));
				CollectionAssert.AreEqual(generatedBytes, File.ReadAllBytes(operations[0].PayloadPath));
				Assert.That(Path.GetDirectoryName(operations[0].PayloadPath),
					Is.EqualTo(ScriptedFileSelectionCache.GetPayloadDirectoryPath(replay.FilePath)));
			}
		}

		/// <summary>
		/// Verifies an exact recipe cannot commit successfully when native final plugin reconciliation rejects its requested state.
		/// </summary>
		[Test]
		public void RunTasks_PluginReconciliationFailureFailsRecipeCompletion()
		{
			using (TemporaryDirectory tmp = new TemporaryDirectory())
			{
				var context = new ScriptProxyContext(tmp.Path, null, false, false, "linked");
				byte[] archiveBytes = { 71, 72, 73, 74 };
				File.WriteAllBytes(context.Mod.Filename, archiveBytes);
				var pluginManager = new RecordingPluginManagerProxy(new string[0])
				{
					ReconciliationResult = false
				};
				ModInstallationRecipeInput recipeInput = CreatePluginPlanInput(archiveBytes, @"Blocked.esp", true);
				var installer = new RecipeExecutionTestInstaller(context, recipeInput, true, pluginManager.Manager);

				installer.ExecuteSynchronously();

				Assert.That(installer.Registered, Is.True, "Final reconciliation occurs after the normal native registration boundary.");
				Assert.That(installer.Succeeded, Is.False);
				Assert.That(pluginManager.ReconciliationCalls, Is.EqualTo(1));
				Assert.That(installer.CompletionMessage, Does.Contain("final plugin state"));
			}
		}

		/// <summary>
		/// Creates a translated one-file recipe with exact source-content expectations.
		/// </summary>
		private static ModInstallationRecipeInput CreateTranslatedInput(byte[] expectedArchiveBytes,
			ModInstallationSimpleFileMapping mapping)
		{
			return CreateTranslatedInput(expectedArchiveBytes, mapping,
				new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data));
		}

		/// <summary>
		/// Creates a translated one-file recipe for an explicit native install method/root.
		/// </summary>
		private static ModInstallationRecipeInput CreateTranslatedInput(byte[] expectedArchiveBytes,
			ModInstallationSimpleFileMapping mapping, ModInstallContext installContext)
		{
			var recipe = new ModInstallationSimpleFileRecipe(new[] { mapping });
			return Translate(CreateInput(expectedArchiveBytes, recipe, installContext), recipe);
		}

		/// <summary>
		/// Attaches the C5.4 native operation plan to a validated recipe input.
		/// </summary>
		private static ModInstallationRecipeInput Translate(ModInstallationRecipeInput input,
			ModInstallationSimpleFileRecipe recipe)
		{
			return new ModInstallationSimpleFileRecipeAdapter().Translate(input, recipe);
		}

		/// <summary>
		/// Creates a validated but untranslated recipe input for the supplied exact mappings.
		/// </summary>
		private static ModInstallationRecipeInput CreateInput(byte[] expectedArchiveBytes,
			params ModInstallationSimpleFileMapping[] mappings)
		{
			return CreateInput(expectedArchiveBytes, new ModInstallationSimpleFileRecipe(mappings));
		}

		/// <summary>
		/// Creates a validated but untranslated recipe input for the supplied exact-file recipe.
		/// </summary>
		private static ModInstallationRecipeInput CreateInput(byte[] expectedArchiveBytes,
			ModInstallationSimpleFileRecipe recipe)
		{
			return CreateInput(expectedArchiveBytes, recipe,
				new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data));
		}

		/// <summary>
		/// Creates a validated but untranslated recipe input for an explicit native install method/root.
		/// </summary>
		private static ModInstallationRecipeInput CreateInput(byte[] expectedArchiveBytes,
			ModInstallationSimpleFileRecipe recipe, ModInstallContext installContext)
		{
			var operation = ModOperationIdentity.CreateNew(
				ModOperationOrigin.Collection,
				new ModOperationFingerprint("target-sha256:" + new string('a', 64), installContext, "recipe:c5.5-simple-v1"));
			var paths = new List<ModInstallationRecipePath>();
			foreach (ModInstallationSimpleFileMapping mapping in recipe.Mappings)
			{
				paths.Add(new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, mapping.SourcePath));
				paths.Add(new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, mapping.DestinationPath));
			}
			var validation = new ModInstallationRecipeValidation(
				ModInstallationSimpleFileRecipeAdapter.AdapterId,
				ModInstallationSimpleFileRecipeAdapter.AdapterVersion,
				installContext,
				new ModInstallationRecipeExpectedContent(ComputeSha256Hex(expectedArchiveBytes), expectedArchiveBytes.LongLength),
				new[]
				{
					new ModInstallationRecipeCapability(
						ModInstallationSimpleFileRecipeAdapter.CapabilityId,
						ModInstallationSimpleFileRecipeAdapter.CapabilityVersion)
				},
				paths);
			return new ModInstallationRecipeInput(operation, validation);
		}

		/// <summary>
		/// Creates a generated-file native plan through the internal adapter seam so C5.7 can exercise payload replay publication.
		/// </summary>
		private static ModInstallationRecipeInput CreateGeneratedPlanInput(byte[] expectedArchiveBytes, string destination, byte[] generatedBytes)
		{
			var installContext = new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data);
			var operation = ModOperationIdentity.CreateNew(
				ModOperationOrigin.Collection,
				new ModOperationFingerprint("target-sha256:" + new string('b', 64), installContext, "recipe:c5.7-generated-v1"));
			var validation = new ModInstallationRecipeValidation(
				"nmm-ce.native.generated-file-test",
				1,
				installContext,
				new ModInstallationRecipeExpectedContent(ComputeSha256Hex(expectedArchiveBytes), expectedArchiveBytes.LongLength),
				new[] { new ModInstallationRecipeCapability("generated-file", 1) },
				new[] { new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, destination) });
			var input = new ModInstallationRecipeInput(operation, validation);
			MethodInfo withNativePlan = typeof(ModInstallationRecipeInput).GetMethod(
				"WithNativePlan", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(withNativePlan, Is.Not.Null, "The test requires the internal native adapter seam introduced by C5.4.");
			return (ModInstallationRecipeInput)withNativePlan.Invoke(input, new object[]
			{
				new ScriptedInstallOperation[] { new GenerateDataFileOperation(destination, generatedBytes) }
			});
		}

		/// <summary>
		/// Creates an exact native plugin-state plan through the internal adapter seam for C5.10 completion testing.
		/// </summary>
		private static ModInstallationRecipeInput CreatePluginPlanInput(byte[] expectedArchiveBytes, string pluginPath, bool activate)
		{
			var installContext = new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data);
			var operation = ModOperationIdentity.CreateNew(
				ModOperationOrigin.Collection,
				new ModOperationFingerprint("target-sha256:" + new string('c', 64), installContext, "recipe:c5.10-plugin-v1"));
			var validation = new ModInstallationRecipeValidation(
				"nmm-ce.native.plugin-state-test",
				1,
				installContext,
				new ModInstallationRecipeExpectedContent(ComputeSha256Hex(expectedArchiveBytes), expectedArchiveBytes.LongLength),
				new[] { new ModInstallationRecipeCapability("plugin-state", 1) },
				new[] { new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, pluginPath) });
			var input = new ModInstallationRecipeInput(operation, validation);
			MethodInfo withNativePlan = typeof(ModInstallationRecipeInput).GetMethod(
				"WithNativePlan", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(withNativePlan, Is.Not.Null, "The test requires the internal native adapter seam introduced by C5.4.");
			return (ModInstallationRecipeInput)withNativePlan.Invoke(input, new object[]
			{
				new ScriptedInstallOperation[] { new SetPluginActivationOperation(pluginPath, activate) }
			});
		}

		/// <summary>
		/// Creates the minimal mod identity required to characterize a second active archive's replay basename.
		/// </summary>
		private static IMod CreateModStub(string filename)
		{
			return InterfaceStub<IMod>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_Filename":
					case "get_FileName":
						return filename;
					case "get_ModName":
						return Path.GetFileNameWithoutExtension(filename);
					case "get_HumanReadableVersion":
						return "1.0";
					default:
						return null;
				}
			});
		}

		/// <summary>
		/// Computes the canonical lowercase SHA-256 digest used by recipe validation fixtures.
		/// </summary>
		private static string ComputeSha256Hex(byte[] bytes)
		{
			using (SHA256 sha256 = SHA256.Create())
				return BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", String.Empty).ToLowerInvariant();
		}

		/// <summary>
		/// Creates a live replay marker whose preservation proves C5.5 validation ran before destructive preparation.
		/// </summary>
		private static string CreateLiveReplay(string rootPath)
		{
			string scriptedDirectory = Path.Combine(rootPath, "InstallInfo", "Scripted");
			Directory.CreateDirectory(scriptedDirectory);
			string replayPath = Path.Combine(scriptedDirectory, "ExampleMod.xml");
			File.WriteAllText(replayPath, "<FileList ReplayVersion=\"2\" />");
			return replayPath;
		}

		/// <summary>
		/// Exposes the synchronous native installer path while replacing only persistence/file-installer dependencies required by this fixture.
		/// </summary>
		private sealed class RecipeExecutionTestInstaller : ModInstaller
		{
			private readonly RecordingModFileInstaller m_mfiFileInstaller;
			private readonly RecordingIniInstaller m_iniInstaller;

			/// <summary>
			/// Initializes a recipe-bearing native installer over the isolated scripted-installer test context.
			/// </summary>
			public RecipeExecutionTestInstaller(ScriptProxyContext context, ModInstallationRecipeInput recipeInput,
				bool assignOperationIdentity = true, Nexus.Client.PluginManagement.IPluginManager pluginManager = null,
				IEnumerable<IMod> activeMods = null)
				: base(context.Mod, context.GameMode, null, null, null, CreateInstallLog(activeMods), pluginManager, context.VirtualModActivator,
					CreateDeploymentManager(recipeInput.InstallContext.Method), null, null, null, recipeInput.InstallContext, recipeInput)
			{
				m_mfiFileInstaller = context.FileInstaller;
				m_iniInstaller = context.IniInstaller;
				if (assignOperationIdentity)
					AssignOperationIdentity(recipeInput.OperationIdentity);
			}

			/// <summary>
			/// Creates the minimal install-log contract required by Direct overwrite infrastructure in this fixture.
			/// </summary>
			private static IInstallLog CreateInstallLog(IEnumerable<IMod> activeMods)
			{
				var activeList = new ThreadSafeObservableList<IMod>();
				if (activeMods != null)
				{
					foreach (IMod activeMod in activeMods)
						activeList.Add(activeMod);
				}
				var readOnlyActiveMods = new ReadOnlyObservableList<IMod>(activeList);

				return InterfaceStub<IInstallLog>.Create((method, args) =>
				{
					if (method.Name == "get_OriginalValuesKey")
						return "ORIGINAL";
					if (method.Name == "get_ActiveMods")
						return readOnlyActiveMods;
					return null;
				});
			}

			/// <summary>
			/// Creates a neutral method-neutral deployment manager only when Direct construction requires one.
			/// </summary>
			private static IModDeploymentManager CreateDeploymentManager(ModInstallMethod installMethod)
			{
				if (installMethod != ModInstallMethod.Direct)
					return null;

				return InterfaceStub<IModDeploymentManager>.Create((method, args) => null);
			}

			/// <summary>
			/// Gets whether the normal native registration hook was reached before recipe execution.
			/// </summary>
			public bool Registered { get; private set; }

			/// <summary>
			/// Runs the otherwise asynchronous installer body synchronously for deterministic unit testing.
			/// </summary>
			public void ExecuteSynchronously()
			{
				RunTasks();
			}

			/// <summary>
			/// Supplies the recording file installer while keeping the production recipe executor unchanged.
			/// </summary>
			protected override IModFileInstaller CreateFileInstaller(TxFileManager p_tfmFileManager,
				ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
			{
				return m_mfiFileInstaller;
			}

			/// <summary>
			/// Supplies the in-memory INI installer required by the native InstallerGroup.
			/// </summary>
			protected override IIniInstaller CreateIniInstaller(TxFileManager p_tfmFileManager,
				ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
			{
				return m_iniInstaller;
			}

			/// <summary>
			/// Omits game-specific edits because C5.4 simple-file recipes contain no such operations.
			/// </summary>
			protected override IGameSpecificValueInstaller CreateGameSpecificValueInstaller(TxFileManager p_tfmFileManager,
				ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
			{
				return null;
			}

			/// <summary>
			/// Records the existing native registration boundary without requiring a persistent InstallLog fixture.
			/// </summary>
			protected override void RegisterMod()
			{
				Registered = true;
			}
		}
	}
}
