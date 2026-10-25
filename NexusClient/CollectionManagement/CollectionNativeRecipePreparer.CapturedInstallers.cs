using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;

namespace Nexus.Client.CollectionManagement
{
	public sealed partial class CollectionNativeRecipePreparer
	{
		/// <summary>Uses explicit file-plan capture for native C# scripts and numbered component packages without curator choices.</summary>
		private static bool RequiresCapturedInstaller(ResolvedCollectionMemberPlan member, IMod mod, IGameMode gameMode)
		{
			if (member.HasVortexFileList || member.HasVortexFomodSelection) return false;
			if (mod.HasInstallScript) return mod.InstallScript.Type is IModInstallationScriptPlanningAdapter;
			if (!(gameMode is Nexus.Client.Games.Gamebryo.GamebryoGameModeBase)) return false;
			IReadOnlyList<string> components = CollectionArchiveComponentSelector.GetComponents(mod);
			return components.Count > 1 && components.Any(component => component.StartsWith("00", StringComparison.Ordinal));
		}

		/// <summary>Captures choices once and reuses their exact file/generated/INI effects through normal Collection review and deployment.</summary>
		private PreparedCollectionNativeRecipe PrepareCapturedInstaller(ResolvedCollectionPlan plan, ResolvedCollectionMemberPlan member,
			CollectionVerifiedArchive archive, IMod mod, IGameMode gameMode, ModInstallContext context, CollectionNativeStateIndex state,
			bool skipReadmeFiles, IPluginManager plugins, IModInstallationConditionEnvironment environment, bool allowInteraction,
			CancellationToken cancellationToken, ResolvedCollectionPlan identityPlan = null)
		{
			var replacement = environment as CollectionReplacementEnvironmentProjection;
			ValidateInputs(plan, member, archive, mod, gameMode, context, state, replacement != null, replacement);
			RetainedPreparationManifest retained = GetRetainedPreparationManifest(plan, cancellationToken);
			ValidateRetainedSourceMember(plan, member, retained.Normalized);
			ValidateVerifiedArchive(archive, cancellationToken);
			ValidateManagedModArchive(mod, archive, cancellationToken);
			var store = new CollectionInstallerSelectionStore(_artifactStore, _referenceStore);
			string selectionArtifact;
			IReadOnlyList<ScriptedInstallOperation> captured = store.LoadOperations(plan, member, archive.Artifact, context, out selectionArtifact, cancellationToken);
			if (captured == null)
			{
				if (!allowInteraction) throw new InvalidOperationException("The reviewed installer plan is missing. Refresh the Collection review before continuing installation.");
				if (CaptureInstallerOperations == null) throw new InvalidOperationException("The native Collection installer is not connected to the active UI.");
				environment = environment ?? new CollectionInstallerConditionEnvironment(state, gameMode, retained.Normalized.Manifest,
					Enumerable.Empty<PreparedCollectionNativeRecipe>(), plan.Policy.EnableUnspecifiedPlugins);
				CollectionPreparationProgress.Report(Nexus.Client.Util.Localization.LanguageManager.Format(
					"Collections.Workflow.ChoosingInstallerOptions", "Choose installer options: {0}", member.DisplayName), true);
				captured = NormalizeCapturedOperations(CaptureInstallerOperations(mod, context, environment, cancellationToken), mod);
			}
			cancellationToken.ThrowIfCancellationRequested();
			IReadOnlyList<ScriptedInstallOperation> selectedOperations = captured;
			if (member.HasVortexFileOverrides)
			{
				var mappings = captured.OfType<InstallModFileOperation>().Select(file => new ModInstallationSimpleFileMapping(file.SourcePath, file.DestinationPath))
					.Concat(captured.OfType<GenerateDataFileOperation>().Select(file => new ModInstallationSimpleFileMapping(file.PreparationSourcePath, file.DestinationPath)));
				ModInstallationSimpleFileRecipe filtered = ApplyVortexFileOverrides(member, new ModInstallationSimpleFileRecipe(mappings, true), gameMode, mod, context);
				var destinations = new HashSet<string>(filtered.Mappings.Select(file => file.DestinationPath), StringComparer.OrdinalIgnoreCase);
				captured = captured.Where(operation => operation is InstallModFileOperation
					? destinations.Contains(((InstallModFileOperation)operation).DestinationPath)
					: !(operation is GenerateDataFileOperation) || destinations.Contains(((GenerateDataFileOperation)operation).DestinationPath)).ToList().AsReadOnly();
			}
			var paths = new List<ModInstallationRecipePath>();
			HashSet<string> archiveSources = GetCapturedArchiveSources(mod);
			var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var artifacts = new List<string> { retained.SourceRecord.RawManifestArtifactId, archive.Artifact.ArtifactId };
			foreach (ScriptedInstallOperation operation in captured)
			{
				InstallModFileOperation file = operation as InstallModFileOperation;
				GenerateDataFileOperation generated = operation as GenerateDataFileOperation;
				if (file != null || generated != null)
				{
					string source = file == null ? generated.PreparationSourcePath : file.SourcePath;
					if (!archiveSources.Contains(source.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)))
						throw new InvalidDataException("The captured installer source is absent from the verified archive: " + source);
					AddValidationPath(paths, declared, ModInstallationRecipePathKind.ArchiveSource, source);
					AddValidationPath(paths, declared, ModInstallationRecipePathKind.Destination, file == null ? generated.DestinationPath : file.DestinationPath);
				}
				if (generated != null)
					using (var stream = new MemoryStream(generated.Data, false)) artifacts.Add(_artifactStore.Publish(stream, cancellationToken).ArtifactId);
			}
			var validation = new ModInstallationRecipeValidation(ModInstallationSimpleFileRecipeAdapter.AdapterId,
				ModInstallationSimpleFileRecipeAdapter.AdapterVersion, context,
				new ModInstallationRecipeExpectedContent(archive.Artifact.ContentHash.Value, archive.Artifact.ByteLength),
				new[] { new ModInstallationRecipeCapability(ModInstallationSimpleFileRecipeAdapter.CapabilityId, ModInstallationSimpleFileRecipeAdapter.CapabilityVersion) }, paths);
			var fingerprint = new ModOperationFingerprint(plan.Target.Fingerprint, context, member.RecipeIdentity.Fingerprint);
			var input = new ModInstallationRecipeInput(ModOperationIdentity.CreateNew(ModOperationOrigin.Collection, fingerprint), validation);
			ModInstallationRecipeInput translated = _simpleFileAdapter.TranslatePreparedPlan(input, captured);
			IReadOnlyList<string> patches;
			translated = ApplyVortexBinaryPatches(plan, member, mod, translated, out patches, cancellationToken);
			artifacts.AddRange(patches);
			ValidateManagedModArchive(mod, archive, cancellationToken);
			CollectionMemberEffectPreview preview = _effectPreviewBuilder.BuildForPreparation(member, translated, gameMode, mod, plugins, cancellationToken);
			if (!preview.IsComplete) throw new InvalidDataException("The captured installer effects could not be fully prepared.");
			if (selectionArtifact == null) selectionArtifact = store.SaveOperations(plan, member, archive.Artifact, context, selectedOperations, cancellationToken);
			artifacts.Add(selectionArtifact);
			using (var stream = new MemoryStream())
			using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), true))
			{
				ResolvedCollectionPlan identity = identityPlan ?? plan;
				writer.Write("nmm-ce.captured-installer/1"); writer.Write(identity.Target.Fingerprint);
				writer.Write(identity.ManifestSource.ContentHash.Value); writer.Write(identity.CurrentStateFingerprint.FormatVersion); writer.Write(identity.CurrentStateFingerprint.Value);
				writer.Write(member.RecipeIdentity.Fingerprint); writer.Write(member.MemberKey.ToString()); writer.Write(archive.Artifact.ArtifactId);
				writer.Write(selectionArtifact); writer.Write((int)context.Method); writer.Write((int)context.InstallRoot); writer.Write(skipReadmeFiles);
				WriteBasicGameSpecificValueIdentity(writer, translated); WriteGeneratedPatchIdentity(writer, translated); WriteEffectPreview(writer, preview); writer.Flush();
				using (SHA256 sha = SHA256.Create())
				{
					var preparedIdentity = PreparedCollectionNativeRecipeIdentity.FromFingerprint("sha256:" + BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", String.Empty).ToLowerInvariant());
					return new PreparedCollectionNativeRecipe(member, preparedIdentity, translated, preview, skipReadmeFiles, artifacts.Distinct(StringComparer.Ordinal));
				}
			}
		}

		/// <summary>Freezes final file writes while leaving Collection plugin state to its existing final reconciliation barrier.</summary>
		internal static IReadOnlyList<ScriptedInstallOperation> NormalizeCapturedOperations(IReadOnlyList<ScriptedInstallOperation> captured, IMod mod)
		{
			if (captured == null || captured.Count == 0) throw new InvalidDataException("The installer did not select any native installation operations.");
			var files = new Dictionary<string, ScriptedInstallOperation>(StringComparer.OrdinalIgnoreCase);
			var values = new List<ScriptedInstallOperation>();
			HashSet<string> archiveSources = GetCapturedArchiveSources(mod);
			foreach (ScriptedInstallOperation operation in captured)
			{
				InstallModFileOperation file = operation as InstallModFileOperation;
				GenerateDataFileOperation generated = operation as GenerateDataFileOperation;
				if (file != null || generated != null)
				{
					string destination = new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination,
						file == null ? generated.DestinationPath : file.DestinationPath).Path;
					if (generated != null && String.IsNullOrEmpty(generated.PreparationSourcePath))
					{
						string scriptSource = mod.InstallScript.Type.FileNames.Select(name => Path.Combine("fomod", name)).First(archiveSources.Contains);
						files[destination] = new GenerateDataFileOperation(destination, generated.Data, scriptSource);
						continue;
					}
					files[destination] = file == null ? operation : new InstallModFileOperation(
						new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, file.SourcePath).Path, destination);
					continue;
				}
				if (operation is SetPluginActivationOperation) continue;
				EditIniOperation ini = operation as EditIniOperation;
				if (ini != null)
				{
					var key = new CollectionNativeIniKey(ini.SettingsFileName, ini.Section, ini.Key);
					values.RemoveAll(value => value is EditIniOperation && key.Equals(new CollectionNativeIniKey(
						((EditIniOperation)value).SettingsFileName, ((EditIniOperation)value).Section, ((EditIniOperation)value).Key)));
					values.Add(ini);
					continue;
				}
				EditGameSpecificValueOperation gameValue = operation as EditGameSpecificValueOperation;
				if (gameValue != null)
				{
					values.RemoveAll(previous => previous is EditGameSpecificValueOperation &&
						StringComparer.Ordinal.Equals(((EditGameSpecificValueOperation)previous).Key, gameValue.Key));
					values.Add(new EditGameSpecificValueOperation(gameValue.Key, gameValue.Value, true));
					continue;
				}
				throw new NotSupportedException("The C# installer uses a native operation that cannot yet be retained in a Collection: " + operation.GetType().Name);
			}
			return files.OrderBy(file => file.Key, StringComparer.OrdinalIgnoreCase).Select(file => file.Value).Concat(values).ToList().AsReadOnly();
		}

		/// <summary>Uses the scripted archive inventory so metadata-hidden installer files remain valid sources.</summary>
		private static HashSet<string> GetCapturedArchiveSources(IMod mod)
		{
			IModInstallerSourceInventory inventory = mod as IModInstallerSourceInventory;
			IEnumerable<string> files = inventory == null ? mod.GetFileList() : inventory.GetInstallerSourceFiles(null, true);
			return new HashSet<string>(files.Select(path => path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)), StringComparer.OrdinalIgnoreCase);
		}
	}
}
