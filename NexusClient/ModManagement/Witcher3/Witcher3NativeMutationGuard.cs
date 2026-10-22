using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.GameStorage;
using Nexus.Client.Games;
using Nexus.Client.Games.DataDriven;
using Nexus.Client.Games.Witcher3;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;
using Nexus.Client.Util.Localization;
using Nexus.Client.PluginManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;

namespace Nexus.Client.ModManagement.Witcher3
{
    /// <summary>
    /// The exact native lease proof required before any Remastered installation is reviewed.
    /// A process-only fallback or an inherited Collection child lease cannot authorize a new
    /// Remastered write. This testable seam makes the requirements explicit.
    /// </summary>
    public static class Witcher3NativeMutationLeasePolicy
    {
        public static void RequireRootAuthority(bool isRoot, bool hasCrossProcessReservation,
            bool hasValidatedNativeAuthority, bool requiresRecovery)
        {
            if (!isRoot || !hasCrossProcessReservation || !hasValidatedNativeAuthority || requiresRecovery)
                throw new InvalidOperationException(
                    "Witcher 3 Remastered requires a recovered, authoritatively reloaded root cross-process native mutation lease.");
        }
    }

    /// <summary>
    /// A short-lived read-only review bound to one live native mutation lease, NOT a transferable
    /// approval or replacement of native InstallLog authority. Created before the native transaction,
    /// since NativeStateCaptureReader deliberately refuses ambient transactions.
    /// </summary>
    public sealed partial class Witcher3NativeMutationReview
    {
        private readonly IEnvironmentInfo _environment;
        private readonly IGameMode _gameMode;
        private readonly IInstallLog _installLog;
        private readonly CollectionTargetMutationLease _lease;
        private readonly Witcher3NativeComponentIndex _index;
        private readonly NativeStateCaptureSnapshot _native;
        private readonly string _gameRoot;

        /// <summary>Admits only explicit, bounded Collection effects under this recovered root reservation.</summary>
        public void AuthorizeCollectionRecipe(ModInstallationRecipeInput recipe)
        {
            AssertLease(_lease);
            if (recipe == null || !recipe.HasNativePlan || recipe.NativeOperations.Count == 0 || recipe.NativeOperations.Count > 1000000 ||
                recipe.InstallContext.InstallRoot != ModInstallRoot.Data ||
                (recipe.InstallContext.Method != ModInstallMethod.Direct && recipe.InstallContext.Method != ModInstallMethod.Virtual) ||
                !StringComparer.Ordinal.Equals(recipe.TargetFingerprint, _lease.TargetFingerprint) ||
                (recipe.OperationIdentity.Origin != ModOperationOrigin.Collection && recipe.OperationIdentity.Origin != ModOperationOrigin.LocalRestore && recipe.OperationIdentity.Origin != ModOperationOrigin.Recovery))
                throw new InvalidDataException("Witcher Collection recipe does not belong to this reviewed native reservation.");
            if (recipe.OperationIdentity.Origin == ModOperationOrigin.Collection &&
                (!StringComparer.Ordinal.Equals(recipe.Validation.AdapterId, ModInstallationSimpleFileRecipeAdapter.AdapterId) ||
                recipe.Validation.AdapterVersion != ModInstallationSimpleFileRecipeAdapter.AdapterVersion ||
                recipe.Validation.Capabilities.Count != 1 ||
                !StringComparer.Ordinal.Equals(recipe.Validation.Capabilities[0].CapabilityId, ModInstallationSimpleFileRecipeAdapter.CapabilityId) ||
                recipe.Validation.Capabilities[0].Version != ModInstallationSimpleFileRecipeAdapter.CapabilityVersion))
                throw new InvalidDataException("Witcher Collection effects require the reviewed native file/value adapter contract.");
            var records = ((DataDrivenWitcher3RemasteredGameMode)_gameMode).CreateCollectionRecordStore();
            foreach (ScriptedInstallOperation operation in recipe.NativeOperations)
            {
                InstallModFileOperation file = operation as InstallModFileOperation;
                GenerateDataFileOperation generated = operation as GenerateDataFileOperation;
                EditGameSpecificValueOperation value = operation as EditGameSpecificValueOperation;
                if (file != null || generated != null)
                {
                    string destination = CollectionWitcher3Data.ValidateDestination(file == null ? generated.DestinationPath : file.DestinationPath);
                    Witcher3CollectionRecordStore.RequireRegularAncestors(Path.Combine(_gameRoot, destination));
                }
                else if (value != null)
                {
                    if (!value.HasResolvedOverwriteDecision) throw new InvalidDataException("Witcher Collection setting has no reviewed overwrite decision.");
                    string profile, identity;
                    records.ParseKey(value.Key, out profile, out identity); records.ValidatePayload(identity, value.Value);
                    records.GetManifestPath(profile);
                }
                else if (!(operation is RestoreNativeRegistrationOperation) || recipe.NativeOperations.Count != 1 || recipe.OperationIdentity.Origin == ModOperationOrigin.Collection)
                    throw new InvalidDataException("Witcher Collection recipe contains an unsupported native operation.");
            }
        }

        private Witcher3NativeMutationReview(IEnvironmentInfo environment, IGameMode gameMode,
            IInstallLog installLog, CollectionTargetMutationLease lease,
            Witcher3NativeComponentIndex index, NativeStateCaptureSnapshot native, string gameRoot)
        {
            _environment = environment;
            _gameMode = gameMode;
            _installLog = installLog;
            _lease = lease;
            _index = index;
            _native = native;
            _gameRoot = gameRoot;
        }

        /// <summary>
        /// Called under the existing native mutation reservation AFTER NMM's authoritative reload,
        /// but BEFORE creating a native TransactionScope or registering a new mod.
        /// </summary>
        public static Witcher3NativeMutationReview Capture(IEnvironmentInfo environment, IGameMode gameMode,
            IInstallLog installLog, IVirtualModActivator virtualActivator,
            IModDeploymentManager deploymentManager, IPluginManager pluginManager,
            CollectionTargetMutationLease mutationLease)
        {
            if (environment == null) throw new ArgumentNullException(nameof(environment));
            if (gameMode == null) throw new ArgumentNullException(nameof(gameMode));
            if (installLog == null) throw new ArgumentNullException(nameof(installLog));
            if (virtualActivator == null) throw new ArgumentNullException(nameof(virtualActivator));
            if (mutationLease == null) throw new ArgumentNullException(nameof(mutationLease));
            if (!String.Equals(gameMode.ModeId, Witcher3RootOwnershipGuard.RemasteredModeId,
                StringComparison.OrdinalIgnoreCase) || !(gameMode is DataDrivenWitcher3RemasteredGameMode))
                throw new InvalidOperationException("Only the dedicated Witcher 3 Remastered native adapter may request authorization.");
            AssertLease(mutationLease);

            var storage = new GameStorageService(environment);
            var authority = new CollectionTargetIdentityResolver(storage).Resolve(storage.FromGameMode(gameMode));
            if (!StringComparer.Ordinal.Equals(authority.Target.Fingerprint, mutationLease.TargetFingerprint))
                throw new InvalidOperationException("Native mutation lease is bound to a different physical game/storage authority.");

            string root = gameMode.GameModeEnvironmentInfo.InstallationPath;
            if (String.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root))
                throw new InvalidDataException("The Remastered installation root is not an absolute directory.");
            root = Path.GetFullPath(root);
            // Use the final active service path, not just the original settings value:
            // setup may have redirected InstallInfo after startup preflight.
            string installInfo = gameMode.GameModeEnvironmentInfo.InstallInfoDirectory;
            Witcher3RootOwnershipDecision decision = Witcher3RootOwnershipGuard.CheckForNativeMutation(
                environment, gameMode.ModeId, root, installInfo);
            if (!decision.AllowsNativeMutation)
                throw new InvalidDataException("Witcher 3 native ownership is not exclusive: " + decision.Reason);
            Witcher3InstallationObservation edition = Witcher3InstallationProbe.Inspect(root);
            if (!DataDrivenWitcher3RemasteredGameModeDescriptor.IsConfirmedRemastered(edition.Edition))
                throw new InvalidDataException("Remastered 5.x executable evidence changed since game activation.");

            var native = new NativeStateCaptureReader(installLog, virtualActivator,
                deploymentManager, pluginManager, gameMode).Capture();
            var disk = Witcher3ComponentDiskScan.Scan(root);
            var index = Witcher3NativeComponentIndexer.Build(native, root, disk);
            // No need to wait until there is an install plan to reject a broken capture.
            if (!index.HasCompleteDiskInventory || index.Issues.Count != 0)
                throw new InvalidDataException("Remastered native state/disk cannot be completely verified: " +
                    String.Join("; ", index.Issues.Take(3).Select(issue => issue.Message)));
            return new Witcher3NativeMutationReview(environment, gameMode, installLog, mutationLease, index, native, root);
        }

        /// <summary>
        /// Called only after the native archive was opened read-only. Preserves original source
        /// entries and freezes the entire selected basic-install mapping for the native worker.
        /// This initial lane authorizes only previously absent Mods/DLC components.
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, string>> AuthorizeBasicInstall(IMod mod,
            ModInstallContext installContext)
        {
            return AuthorizeComponentFiles(mod, installContext, null);
        }

        /// <summary>Reviews all ordinary files, or the component portion of an independently reviewed menu plan.</summary>
        private IReadOnlyList<KeyValuePair<string, string>> AuthorizeComponentFiles(IMod mod,
            ModInstallContext installContext, IEnumerable<KeyValuePair<string, string>> selectedFiles)
        {
            if (mod == null) throw new ArgumentNullException(nameof(mod));
            if (installContext == null) throw new ArgumentNullException(nameof(installContext));
            AssertLease(_lease);
            if (mod.HasInstallScript || installContext.InstallRoot != ModInstallRoot.Data ||
                (installContext.Method != ModInstallMethod.Direct && installContext.Method != ModInstallMethod.Virtual))
                throw new InvalidDataException("Remastered requires an ordinary Direct/Virtual basic archive; scripted and recipe installs remain blocked.");
            // RegisterMod() is an update when NMM already has this archive in native state.
            // Never let a new-folder mapping silently turn an existing owner into a second
            // registration or orphan its older managed files.
            if (!String.IsNullOrWhiteSpace(_installLog.GetModKey(mod)))
                throw new InvalidDataException("An already registered Witcher 3 archive requires a separate ownership-aware update workflow.");
            var adapter = _gameMode as IReviewedBasicInstallMappingProvider;
            if (adapter == null)
                throw new InvalidOperationException("Remastered native adapter is not the reviewed basic installer.");
            List<string> sources = mod.GetFileList();
            if (sources == null)
                throw new InvalidDataException("The Witcher 3 archive inventory is unavailable.");
            var selected = selectedFiles ?? sources.Where(file => !ModInstallFileFilter.IsIgnored(file))
                .Select(file => new KeyValuePair<string, string>(file, null)).ToList();
            var mapped = adapter.MapBasicInstallFiles(mod, installContext, selected);
            Witcher3NativeWriteDecision decision = Witcher3NativeWritePolicy.Assess(_index,
                mapped.Select(file => file.Value));
            if (!decision.EligibleForNewComponentPreflight)
                throw new InvalidDataException("Witcher 3 native ownership review blocked installation (" +
                    decision.Status + "): " + decision.Reason);
            // Remastered MapBasicInstallFiles performs the first path check. Repeat while
            // holding this exact lease immediately after the native/index reconciliation.
            Witcher3NativeDestinationPreflight.RequireNewComponents(_gameRoot, mapped.Select(file => file.Value));
            return new ReadOnlyCollection<KeyValuePair<string, string>>(mapped.ToList());
        }

        /// <summary>Retains component ownership review and admits only previously absent custom menu files.</summary>
        internal IReadOnlyList<KeyValuePair<string, string>> AuthorizeMenuInstall(IMod mod,
            ModInstallContext context, Witcher3StandaloneMenuInstallPlan plan)
        {
            AssertLease(_lease);
            var records = ((DataDrivenWitcher3RemasteredGameMode)_gameMode).CreateCollectionRecordStore();
            foreach (string menu in plan.MenuPaths)
            {
                string path = records.GetManifestPath(menu);
                ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, menu);
                if (File.Exists(path) || Directory.Exists(path) ||
                    _native.InstallLog.Files.Any(file => file.Target.Equals(target)) ||
                    _native.InstallLog.DeploymentTargets.Any(file => file.Target.Equals(target)) ||
                    _native.VirtualState.Links.Any(link => link.Target.Equals(target)))
                    throw new Witcher3InstallException(LanguageManager.Format("Witcher3.Install.MenuExists",
                        "The settings menu file already exists: {0}. Remove the mod that installed it, or back up and remove a manually installed copy, then try again.", menu));
            }
            foreach (EditGameSpecificValueOperation value in plan.Operations.OfType<EditGameSpecificValueOperation>())
            {
                string profile, identity;
                records.ParseKey(value.Key, out profile, out identity);
                records.ValidatePayload(identity, value.Value);
                records.GetManifestPath(profile);
            }
            IReadOnlyList<KeyValuePair<string, string>> mapped = AuthorizeComponentFiles(mod, context, plan.ComponentFiles);
            if (!mapped.SequenceEqual(plan.ComponentFiles))
                throw new InvalidDataException("Standalone menu components changed during native review.");
            return mapped;
        }

        /// <summary>
        /// The only W3R upgrade lane prepared for future Windows verification: one previously
        /// registered owner and exactly the same set of installed file targets. There are no
        /// implicit deletions, additions or third-party ownership transfers. The native
        /// ModUpgrader still owns the transaction, backup handling and owner-key replacement.
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, string>> AuthorizeBasicUpgrade(IMod oldMod, IMod newMod,
            ModInstallContext installContext)
        {
            if (oldMod == null) throw new ArgumentNullException(nameof(oldMod));
            if (newMod == null) throw new ArgumentNullException(nameof(newMod));
            if (installContext == null) throw new ArgumentNullException(nameof(installContext));
            AssertLease(_lease);
            if (oldMod.HasInstallScript || newMod.HasInstallScript ||
                installContext.InstallRoot != ModInstallRoot.Data ||
                (installContext.Method != ModInstallMethod.Direct && installContext.Method != ModInstallMethod.Virtual))
                throw new InvalidDataException("Remastered upgrade review supports only nonscripted Direct/Virtual basic archives at the Data root.");

            string oldKey = _installLog.GetModKey(oldMod);
            if (String.IsNullOrWhiteSpace(oldKey))
                throw new InvalidDataException("The replaced Witcher 3 archive has no active native InstallLog owner.");
            if (_installLog.GetInstalledGameSpecificValueEdits(oldMod).Any(key => key.StartsWith(Witcher3CollectionRecordStore.Prefix, StringComparison.Ordinal)))
                throw Witcher3StandaloneMenuInstallPlan.UpgradeRequiresRemoval();
            // A different already-active new mod must never be adopted as the replacement.
            // Self-upgrades (reinstall/reactivate) reuse the same IMod instance.
            string newKey = _installLog.GetModKey(newMod);
            if (!Object.ReferenceEquals(oldMod, newMod) && !String.IsNullOrWhiteSpace(newKey))
                throw new InvalidDataException("A Witcher 3 upgrade cannot replace a different already-registered archive.");
            if (_installLog.GetModInstallMethod(oldMod) != installContext.Method ||
                _installLog.GetModInstallRoot(oldMod) != installContext.InstallRoot)
                throw new InvalidDataException("An upgrade cannot change the old owner's native install method or root.");

            var adapter = _gameMode as DataDrivenWitcher3RemasteredGameMode;
            if (adapter == null)
                throw new InvalidOperationException("The active game mode is not the dedicated Remastered upgrade adapter.");
            List<string> sources = newMod.GetFileList();
            if (sources == null)
                throw new InvalidDataException("The replacement Witcher 3 archive file inventory is missing.");
            var selected = sources.Where(file => !ModInstallFileFilter.IsIgnored(file))
                .Select(file => new KeyValuePair<string, string>(file, null)).ToList();
            var mapped = adapter.MapBasicUpgradeFiles(newMod, installContext, selected);
            Witcher3NativeUpgradeDecision decision = Witcher3NativeUpgradePolicy.Assess(_index,
                _native, oldKey, installContext.Method, installContext.InstallRoot,
                mapped.Select(file => file.Value));
            if (!decision.IsSupportedUpgradeCandidate)
                throw new InvalidDataException("Witcher 3 native upgrade was blocked (" +
                    decision.Status + "): " + decision.Reason);
            Witcher3NativeUpgradeDestinationPreflight.RequireUpgradeTargets(_gameRoot,
                _index.Components.Where(component => component.Owners.Any(owner =>
                    String.Equals(owner.NativeModKey, oldKey, StringComparison.OrdinalIgnoreCase)))
                    .SelectMany(component => component.Files.Select(file => file.GameRelativePath)),
                mapped.Select(file => file.Value));
            return new ReadOnlyCollection<KeyValuePair<string, string>>(mapped.ToList());
        }

        /// <summary>
        /// Characterizes the complete native owner footprint under the same root lease used
        /// for installation. Must precede the first Virtual disable or TransactionScope.
        /// </summary>
        public Witcher3NativeUninstallDecision AuthorizeBasicUninstall(IMod mod)
        {
            if (mod == null) throw new ArgumentNullException(nameof(mod));
            AssertLease(_lease);
            if (mod.HasInstallScript)
                throw new InvalidDataException("Scripted Witcher 3 uninstall requires a separate native effects review.");
            string ownerKey = _installLog.GetModKey(mod);
            Witcher3NativeUninstallDecision decision = Witcher3NativeUninstallPolicy.Assess(_index, _native, ownerKey);
            if (!decision.IsExactRemovalCandidate)
                throw new InvalidDataException("Witcher 3 native removal was blocked (" +
                    decision.Status + "): " + decision.Reason);
            Witcher3NativeUninstallDestinationPreflight.RequireExistingTargets(_gameRoot, decision.ExactTargets);
            _completedRemovalVerified = false;
            _reviewedUninstallOwnerKey = ownerKey;
            _reviewedUninstallTargets = decision.ExactTargets;
            return decision;
        }

        private bool _completedRemovalVerified;
        private string _reviewedUninstallOwnerKey;
        private ReadOnlyCollection<string> _reviewedUninstallTargets;

        /// <summary>
        /// Re-reads committed native state after the uninstall transaction and after pending
        /// deployment changes have been published. Failure must be reported as durability
        /// unknown; a committed transaction cannot be retroactively rolled back here.
        /// </summary>
        public void VerifyCompletedUninstall(IVirtualModActivator virtualActivator,
            IModDeploymentManager deploymentManager, IPluginManager pluginManager)
        {
            AssertLease(_lease);
            if (String.IsNullOrWhiteSpace(_reviewedUninstallOwnerKey) || _reviewedUninstallTargets == null)
                throw new InvalidOperationException("No Witcher 3 removal was authorized by this lease review.");
            Witcher3RootOwnershipDecision ownership = Witcher3RootOwnershipGuard.CheckForNativeMutation(
                _environment, _gameMode.ModeId, _gameRoot,
                _gameMode.GameModeEnvironmentInfo.InstallInfoDirectory);
            if (!ownership.AllowsNativeMutation)
                throw new InvalidDataException("A second Witcher 3 physical-root owner appeared during removal: " + ownership.Reason);
            if (!DataDrivenWitcher3RemasteredGameModeDescriptor.IsConfirmedRemastered(
                Witcher3InstallationProbe.Inspect(_gameRoot).Edition))
                throw new InvalidDataException("The game edition changed during a native Witcher 3 removal.");
            var after = new NativeStateCaptureReader(_installLog, virtualActivator,
                deploymentManager, pluginManager, _gameMode).Capture();
            if (VerifyCollectionUninstall(after)) { _completedRemovalVerified = true; return; }
            Witcher3NativeUninstallPostcondition.RequireReconciled(after,
                _reviewedUninstallOwnerKey, _reviewedUninstallTargets, target =>
                {
                    string path = Path.Combine(_gameRoot, target.Replace('\\', Path.DirectorySeparatorChar));
                    try { File.GetAttributes(path); return true; }
                    catch (FileNotFoundException) { return false; }
                    catch (DirectoryNotFoundException) { return false; }
                });
            _completedRemovalVerified = true;
        }

        /// <summary>Removes only empty ancestors of the exact removed files after native removal has been verified.</summary>
        internal void RemoveEmptyUninstalledDirectories()
        {
            if (!_completedRemovalVerified)
                throw new InvalidOperationException("Empty-directory cleanup requires verified completed native removal.");
            // Promoted removal preserves directories. Leaving an empty component behind would block
            // the next new-component install. Never recurse or remove Mods/DLC themselves.
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string target in _reviewedUninstallTargets)
            {
                string[] parts = CollectionWitcher3Data.ValidateDestination(target).Split('\\');
                if (parts[0].Equals("bin", StringComparison.OrdinalIgnoreCase)) continue;
                for (int count = 2; count < parts.Length; ++count)
                    directories.Add(String.Join("\\", parts.Take(count)));
            }
            foreach (string relative in directories.OrderByDescending(path => path.Length).ThenBy(path => path, StringComparer.Ordinal))
            {
                AssertLease(_lease);
                FileAttributes rootAttributes = File.GetAttributes(_gameRoot);
                if ((rootAttributes & FileAttributes.Directory) == 0 || (rootAttributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("The reviewed removal root changed before directory cleanup.");
                string cursor = _gameRoot;
                bool exists = true;
                foreach (string segment in relative.Split('\\'))
                {
                    cursor = Path.Combine(cursor, segment);
                    FileAttributes attributes;
                    try { attributes = File.GetAttributes(cursor); }
                    catch (FileNotFoundException) { exists = false; break; }
                    catch (DirectoryNotFoundException) { exists = false; break; }
                    if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("A reviewed removal directory changed type or became linked.");
                }
                if (exists && !Directory.EnumerateFileSystemEntries(cursor).Any())
                    Directory.Delete(cursor, false);
            }
        }

        private static void AssertLease(CollectionTargetMutationLease lease)
        {
            if (lease.IsDisposed)
                throw new ObjectDisposedException(nameof(CollectionTargetMutationLease));
            Witcher3NativeMutationLeasePolicy.RequireRootAuthority(lease.IsRoot,
                lease.HasCrossProcessReservation, lease.HasValidatedNativeAuthority, lease.RequiresRecovery);
        }
    }
}
