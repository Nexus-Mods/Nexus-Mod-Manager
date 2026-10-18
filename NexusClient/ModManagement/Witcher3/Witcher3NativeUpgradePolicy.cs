using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Nexus.Client.Games.Witcher3;
using Nexus.Client.ModManagement.InstallationLog;

namespace Nexus.Client.ModManagement.Witcher3
{
    /// <summary>
    /// A characterization decision, never a transferable native write permit. The only candidate
    /// accepted by this initial protocol is a like-for-like, single-owner file replacement with
    /// no deletions, additions, configuration effects, or deployment-root/method changes.
    /// </summary>
    public enum Witcher3NativeUpgradeStatus
    {
        ExactFootprintCandidate = 0,
        NativeStateUncertain = 1,
        InvalidTargetSet = 2,
        OwnerNotFound = 3,
        MethodOrRootMismatch = 4,
        SharedOrUnmanagedComponent = 5,
        NonFileEffectsBlocked = 6,
        FileFootprintChanged = 7,
        ReviewedFootprintChangeCandidate = 8
    }

    public sealed class Witcher3NativeUpgradeDecision
    {
        internal Witcher3NativeUpgradeDecision(Witcher3NativeUpgradeStatus status,
            string reason, long commitSequence, IEnumerable<string> exactTargets)
        {
            Status = status;
            Reason = reason;
            NativeDeploymentCommitSequence = commitSequence;
            ExactTargets = new ReadOnlyCollection<string>(exactTargets.ToList());
        }

        public Witcher3NativeUpgradeStatus Status { get; }
        public string Reason { get; }
        public long NativeDeploymentCommitSequence { get; }
        public ReadOnlyCollection<string> ExactTargets { get; }
        /// <summary>Only characterization. The live lease and Windows rollback gate are separate.</summary>
        public bool IsExactFootprintCandidate => Status == Witcher3NativeUpgradeStatus.ExactFootprintCandidate;
        public bool IsSupportedUpgradeCandidate => IsExactFootprintCandidate ||
            Status == Witcher3NativeUpgradeStatus.ReviewedFootprintChangeCandidate;
    }

    public static class Witcher3NativeUpgradePolicy
    {
        private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

        public static Witcher3NativeUpgradeDecision Assess(Witcher3NativeComponentIndex index,
            NativeStateCaptureSnapshot native, string replacedOwnerKey, ModInstallMethod method,
            ModInstallRoot root, IEnumerable<string> proposedTargets)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));
            if (native == null) throw new ArgumentNullException(nameof(native));
            if (proposedTargets == null) throw new ArgumentNullException(nameof(proposedTargets));
            long sequence = index.NativeDeploymentCommitSequence;
            if (!index.HasCompleteDiskInventory || index.Issues.Count != 0 || native.Issues.Count != 0 ||
                native.InstallLog.DeploymentCommitSequence != sequence ||
                native.DeploymentCoverage == NativeStateCaptureCoverage.Unavailable ||
                native.DeploymentCoverage == NativeStateCaptureCoverage.Partial)
                return Deny(Witcher3NativeUpgradeStatus.NativeStateUncertain,
                    "A complete committed native and disk snapshot from the same lease is required.", sequence);
            if (String.IsNullOrWhiteSpace(replacedOwnerKey))
                return Deny(Witcher3NativeUpgradeStatus.OwnerNotFound, "A registered native owner key is required.", sequence);

            List<InstallLogReadMod> registrations = native.InstallLog.Mods
                .Where(mod => Names.Equals(mod.ModKey, replacedOwnerKey) && !mod.Hidden).ToList();
            if (registrations.Count != 1)
                return Deny(Witcher3NativeUpgradeStatus.OwnerNotFound,
                    "Exactly one active native registration must match the replaced owner.", sequence);
            InstallLogReadMod registered = registrations[0];
            if (root != ModInstallRoot.Data || registered.InstallRoot != root ||
                (method != ModInstallMethod.Direct && method != ModInstallMethod.Virtual) ||
                registered.InstallMethod != method)
                return Deny(Witcher3NativeUpgradeStatus.MethodOrRootMismatch,
                    "The upgrade must retain the existing ordinary Data root and Direct/Virtual method.", sequence);
            if (registered.HasInstallScript ||
                native.InstallLog.IniEdits.Any(edit => edit.Values.Any(value => Names.Equals(value.OwnerKey, replacedOwnerKey))) ||
                native.InstallLog.GameValues.Any(value => value.Values.Any(entry => Names.Equals(entry.OwnerKey, replacedOwnerKey))) ||
                native.ReplayReferences.Any(reference => Names.Equals(reference.ModKey, replacedOwnerKey)))
                return Deny(Witcher3NativeUpgradeStatus.NonFileEffectsBlocked,
                    "Script replay, INI edits and game-specific values require a separate rollback protocol.", sequence);

            var proposed = new HashSet<string>(Names);
            try
            {
                foreach (string target in proposedTargets)
                {
                    string checkedPath = Witcher3BasicInstallMapping.ValidatePlannedPath(target);
                    if (!String.Equals(checkedPath, target, StringComparison.Ordinal) || !proposed.Add(checkedPath))
                        return Deny(Witcher3NativeUpgradeStatus.InvalidTargetSet,
                            "Upgrade target is duplicate or not in the native canonical spelling: " + target, sequence);
                    string[] segments = checkedPath.Split('\\');
                    if (segments.Length < 4 || (segments[0] == "Mods" &&
                        (Names.Equals(segments[1], "mod0000_MergedFiles") ||
                         Names.Equals(segments[1], "mod0000____CompilationTrigger"))))
                        return Deny(Witcher3NativeUpgradeStatus.InvalidTargetSet,
                            "Generated, root-level and unsupported targets cannot be upgraded.", sequence);
                }
            }
            catch (Exception error) when (error is InvalidDataException || error is ArgumentException || error is NotSupportedException)
            {
                return Deny(Witcher3NativeUpgradeStatus.InvalidTargetSet, error.Message, sequence);
            }
            if (proposed.Count == 0)
                return Deny(Witcher3NativeUpgradeStatus.InvalidTargetSet, "An empty upgrade has no native footprint.", sequence);

            var managedFiles = new HashSet<string>(Names);
            var components = index.Components.Where(component =>
                component.Owners.Any(owner => Names.Equals(owner.NativeModKey, replacedOwnerKey))).ToList();
            if (components.Count == 0)
                return Deny(Witcher3NativeUpgradeStatus.OwnerNotFound,
                    "The active owner has no observed Witcher mod or DLC component files.", sequence);
            foreach (Witcher3NativeComponent component in components)
            {
                if (component.IsMergeCandidate || component.ObservedOnDisk != true ||
                    component.Owners.Count != 1 || !Names.Equals(component.Owners[0].NativeModKey, replacedOwnerKey) ||
                    component.Owners[0].InstallMethod != method || component.HasMixedManagedAndUnmanagedFiles ||
                    !component.HasContentPayload || component.Files.Count == 0)
                    return Deny(Witcher3NativeUpgradeStatus.SharedOrUnmanagedComponent,
                        "The replaced component is shared, generated, unverified or not exclusively owned: " +
                        component.GameRelativeFolderPath, sequence);
                foreach (Witcher3ComponentFile file in component.Files)
                {
                    if (file.ObservedOnDisk != true || file.WinnerAmbiguous || file.HasOriginalFallback ||
                        file.Source == Witcher3ComponentFileSource.UnmanagedDiskFile ||
                        file.NativeOwnerKeys.Count != 1 || !Names.Equals(file.NativeOwnerKeys[0], replacedOwnerKey) ||
                        !Names.Equals(file.IntendedWinnerKey, replacedOwnerKey))
                        return Deny(Witcher3NativeUpgradeStatus.SharedOrUnmanagedComponent,
                            "A file has unverified, shadowed or external ownership: " + file.GameRelativePath, sequence);
                    managedFiles.Add(file.GameRelativePath);
                }
            }

            // Reconcile against ALL authoritative native file targets, not merely the component
            // projection. Otherwise ModUpgrader could silently delete an old bin/config or
            // game-root file which the Witcher component index intentionally does not display.
            var registeredTargets = new HashSet<ModDeploymentTarget>();
            foreach (InstallLogReadFile file in native.InstallLog.Files)
                if (file.OwnerKeys.Contains(replacedOwnerKey, Names)) registeredTargets.Add(file.Target);
            foreach (InstallLogReadDeploymentTarget entry in native.InstallLog.DeploymentTargets)
                if (entry.OwnerKeys.Contains(replacedOwnerKey, Names)) registeredTargets.Add(entry.Target);
            foreach (VirtualModReadLink link in native.VirtualState.Links)
                if (Names.Equals(link.OwnerKey, replacedOwnerKey)) registeredTargets.Add(link.Target);
            // Every old owner target must be fully accounted for; otherwise a component-only
            // index could hide an older bin/config/script effect during an upgrade.
            var expectedOldTargets = new HashSet<ModDeploymentTarget>(
                managedFiles.Select(target => ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, target)));
            if (!registeredTargets.SetEquals(expectedOldTargets))
                return Deny(Witcher3NativeUpgradeStatus.FileFootprintChanged,
                    "The previous owner contains effects outside its verified Mods/DLC component files.", sequence);

            var indexedComponents = index.Components.ToDictionary(item => item.GameRelativeFolderPath, Names);
            foreach (string target in proposed.Where(path => !managedFiles.Contains(path)))
            {
                string[] parts = target.Split('\\');
                Witcher3NativeComponent component;
                if (indexedComponents.TryGetValue(parts[0] + "\\" + parts[1], out component) &&
                    (component.Owners.Count != 1 ||
                     !Names.Equals(component.Owners[0].NativeModKey, replacedOwnerKey) ||
                     component.HasMixedManagedAndUnmanagedFiles || component.IsMergeCandidate))
                    return Deny(Witcher3NativeUpgradeStatus.SharedOrUnmanagedComponent,
                        "An added file would enter another owner's or unmanaged component: " + target, sequence);
                var nativeTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, target);
                if (native.InstallLog.Files.Any(file => file.Target.Equals(nativeTarget)) ||
                    native.InstallLog.DeploymentTargets.Any(file => file.Target.Equals(nativeTarget)) ||
                    native.VirtualState.Links.Any(link => link.Target.Equals(nativeTarget)))
                    return Deny(Witcher3NativeUpgradeStatus.SharedOrUnmanagedComponent,
                        "An added file already has a native deployment history: " + target, sequence);
            }

            bool sameFootprint = managedFiles.SetEquals(proposed);
            return new Witcher3NativeUpgradeDecision(sameFootprint
                    ? Witcher3NativeUpgradeStatus.ExactFootprintCandidate
                    : Witcher3NativeUpgradeStatus.ReviewedFootprintChangeCandidate,
                sameFootprint ? "Like-for-like owner upgrade." :
                    "Exclusive owner file additions/removals are reviewed; native upgrade handles the transactional delta.",
                sequence, proposed.OrderBy(path => path, Names).ThenBy(path => path, StringComparer.Ordinal));
        }

        private static Witcher3NativeUpgradeDecision Deny(Witcher3NativeUpgradeStatus status,
            string reason, long sequence)
        {
            return new Witcher3NativeUpgradeDecision(status, reason, sequence, new string[0]);
        }
    }
}
