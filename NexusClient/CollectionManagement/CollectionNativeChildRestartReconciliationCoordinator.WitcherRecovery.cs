using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.GameStorage;
using Nexus.Client.Games.DataDriven;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Witcher3;
using Nexus.Client.Mods;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Bounded forward repair for legacy Witcher Collection activations missing execution evidence.</summary>
	public sealed partial class CollectionNativeChildRestartReconciliationCoordinator
	{
		/// <summary>Explicitly finishes an unregistered Witcher activation using its retained reviewed recipe.</summary>
		internal Task<CollectionNativeChildRestartReconciliationResult> RepairInterruptedActivationAsync(
			CollectionOperationIdentity operation, GameStoragePathSet paths, PreparedCollectionNativeRecipe recipe,
			CancellationToken cancellationToken)
		{
			return ReconcileCoreAsync(operation, paths, cancellationToken, CollectionNativeChildWorkflowMode.Additive, recipe);
		}

		/// <summary>Preserves an exact orphan postimage as the native installer's original files before completing registration.</summary>
		private async Task RepairUnregisteredWitcherActivationAsync(CollectionOperation operation, CollectionNativeChildOperation child,
			CollectionNativeChildRecoveryManifest recovery, CollectionNativeStateIndex state, PreparedCollectionNativeRecipe prepared,
			GameStoragePathSet paths, CollectionTargetMutationLease lease, CancellationToken cancellationToken)
		{
			ModManager manager = _services.ModManager;
			DataDrivenWitcher3RemasteredGameMode game = manager.GameMode as DataDrivenWitcher3RemasteredGameMode;
			if (game == null || (child.NativeResult != null && child.NativeResult.Durability != ModOperationDurability.Unknown) ||
				recovery.PreviousNativeMod != null || recovery.PreviousArchive != null ||
				recovery.TerminalStateFingerprint != null || !state.Fingerprint.Equals(recovery.PreparationStateFingerprint) ||
				!prepared.Member.MemberKey.Equals(child.Member.MemberKey) || !prepared.EffectPreview.IsComplete ||
				prepared.EffectPreview.InstallRootCorrection != null || prepared.EffectPreview.IniEdits.Count != 0 ||
				prepared.EffectPreview.PluginEffects.Count != 0 || prepared.InstallContext.Method != ModInstallMethod.Virtual ||
				recovery.ScriptedReplay.ReplayFileExisted || recovery.ScriptedReplay.PayloadDirectoryExisted ||
				recovery.ScriptedReplay.Payloads.Count != 0 ||
				!StringComparer.Ordinal.Equals(prepared.Validation.ExpectedContent.Sha256, recovery.IncomingArchive.ContentHash.Value) ||
				prepared.Validation.ExpectedContent.ByteLength != recovery.IncomingArchive.ByteLength)
				throw new InvalidOperationException("This interrupted install cannot be completed automatically because its original native state or reviewed recipe has changed. Existing files were preserved.");

			IMod incoming = CollectionNativeChildExecutionCoordinator.ResolveIncomingManagedMod(prepared.Member, null,
				recovery.IncomingArchive, manager, cancellationToken);
			ModInstallationRecipeInput input = prepared.RecipeInput.ForOperationIdentity(child.NativeOperation).WithDeferredPluginReconciliation();
			if (state.Mods.Values.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x.FileName, incoming.Filename)))
				throw new InvalidOperationException("The interrupted mod already has a native registration; automatic activation repair was refused.");

			// This is forward recovery, not a claim that missing original-file evidence was reconstructed.
			// Existing exact files become native original-value backups. Nothing is deleted as crash residue.
			IBackgroundTaskSet task = manager.ActivateMod(incoming, (oldMod, newMod) => ConfirmUpgradeResult.Cancel,
				(message, allowGroup, allowMod) => OverwriteResult.Yes, manager.ActiveMods, input.InstallContext, true, input);
			ModInstaller installer = task as ModInstaller;
			if (installer == null || !Matches(installer.OperationIdentity, child.NativeOperation))
				throw new InvalidOperationException("The interrupted mod could not be prepared for the exact reviewed activation.");
			installer.AssignParentMutationLease(lease);
			installer.SetCollectionArchivePreparation(openedMod =>
			{
				cancellationToken.ThrowIfCancellationRequested();
				CollectionMemberEffectPreview exact = new CollectionMemberEffectPreviewBuilder().BuildFromOpenArchive(
					prepared.Member, input, game, openedMod, _services.PluginManager, cancellationToken);
				exact = CollectionNativeChildExecutionCoordinator.ResolveExactSubmissionPreview(prepared.EffectPreview, exact);
				RequireRecoverableWitcherFiles(exact, state, game);
				Witcher3CollectionRecordStore records = game.CreateCollectionRecordStore();
				foreach (CollectionPlannedGameValueEffect value in exact.GameValues)
				{
					string profile, identity;
					records.ParseKey(value.Key, out profile, out identity);
					byte[] current;
					if (state.GameValues.ContainsKey(value.Key) || (records.TryReadValue(records.GetManifestPath(profile), identity,
						out current) && !ByteArraysEqual(current, value.Value)))
						throw new InvalidOperationException("Recovery found changed or owned Witcher settings at '" + profile + "'. Existing settings were preserved.");
				}
				CollectionNativeChildExecutionEvidence evidence = CollectionNativeChildExecutionCoordinator.CaptureExecutionEvidence(
					prepared.Member, exact, input, incoming, paths.InstallInfoPath, game);
				string replayPath = ScriptedFileSelectionCache.GetDefaultFilePath(incoming.Filename, paths.InstallInfoPath);
				string replayFailure;
				if ((File.Exists(replayPath) || Directory.Exists(ScriptedFileSelectionCache.GetPayloadDirectoryPath(replayPath))) &&
					!VerifyExpectedReplayAtPath(evidence, paths.InstallInfoPath, out replayFailure))
					throw new InvalidOperationException("The interrupted install's saved replay differs from the retained recipe: " + replayFailure);
				_manifestStore.SaveManifest(recovery.WithExecutionEvidence(evidence));
				if (_manifestStore.GetManifest(operation, child).ExecutionEvidence == null)
					throw new InvalidDataException("Recovery evidence could not be retained before completing the interrupted installation.");
			});
			CollectionPreparationProgress.Report(LanguageManager.Format("Collections.Workflow.CompletingInterruptedWitcherMod",
				"Completing the interrupted Witcher mod installation: {0}", prepared.Member.DisplayName), true);
			if (!_services.ModActivationMonitor.SubmitWhenIdle(task, () =>
			{
				cancellationToken.ThrowIfCancellationRequested();
				// Preserve the original failed/unknown report and monotonic child checkpoint.
				// The forward repair does not rewrite history; final verification classifies its installed reality.
				SaveChild(operation, child);
			}))
				throw new InvalidOperationException("The mod activation queue is busy. Wait for it to finish before continuing recovery.");

			// Once accepted, cancellation cannot release the mutation lease while the native worker is running.
			await CollectionNativeChildVerificationCoordinator.WaitForCompletionAsync(task).ConfigureAwait(true);
			ModOperationResult result = installer.OperationResult;
			if (result == null || !Matches(result.Identity, child.NativeOperation))
				throw new InvalidDataException("The recovery installer did not publish the expected native operation result.");
			Trace.TraceInformation("Collection interrupted activation repair completed: {0}; durability: {1}; {2}",
				result.ReportedStatus, result.Durability, result.Message);
			_authorityValidator.ValidateAndReload(lease, new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths), paths);
		}

		/// <summary>Checks exact orphan bytes without mutating the game or assuming an unowned file was originally absent.</summary>
		internal static bool CanRecoverUnownedWitcherOutput(CollectionPlannedFileEffect file, bool owned, string path)
		{
			if (file == null || owned || !file.HasExactContentIdentity ||
				file.ExpectedContentHash.Algorithm != CollectionContentHashAlgorithm.Sha256 || String.IsNullOrWhiteSpace(path)) return false;
			Witcher3CollectionRecordStore.RequireRegularAncestors(path);
			return !Directory.Exists(path) && (!File.Exists(path) ||
				MatchesContentFile(path, file.ExpectedContentHash, file.ExpectedByteLength.Value));
		}

		/// <summary>Allows only absent or byte-identical, unowned reviewed outputs through the forward recovery boundary.</summary>
		private static void RequireRecoverableWitcherFiles(CollectionMemberEffectPreview preview, CollectionNativeStateIndex state,
			DataDrivenWitcher3RemasteredGameMode game)
		{
			foreach (CollectionPlannedFileEffect file in preview.Files)
			{
				string path = ModDeploymentTargetResolver.GetPhysicalPath(game, file.Target);
				if (!CanRecoverUnownedWitcherOutput(file, state.Files.ContainsKey(file.Target), path))
					throw new InvalidOperationException("Recovery found changed or owned files at '" + file.Target + "'. Existing files were preserved.");
			}
		}
	}
}
