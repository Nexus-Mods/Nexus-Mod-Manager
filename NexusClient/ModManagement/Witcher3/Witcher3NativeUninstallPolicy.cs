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
    /// A read-only, exact-footprint characterization of a native Witcher 3 removal.
    /// The decision is NOT a transferable permission to delete files or proof of rollback.
    /// </summary>
    public enum Witcher3NativeUninstallStatus
    {
        ExactRemovalCandidate = 0,
        NativeStateUncertain = 1,
        OwnerNotFound = 2,
        UnsupportedRegistration = 3,
        NonFileEffectsBlocked = 4,
        SharedOrUnmanagedComponent = 5,
        NativeFootprintMismatch = 6,
        InvalidTarget = 7,
        VirtualFallbackUnverified = 8
    }

    public sealed class Witcher3NativeUninstallDecision
    {
        internal Witcher3NativeUninstallDecision(Witcher3NativeUninstallStatus status,
            string reason, long sequence, IEnumerable<string> exactTargets)
        {
            Status = status;
            Reason = reason;
            NativeDeploymentCommitSequence = sequence;
            ExactTargets = new ReadOnlyCollection<string>(exactTargets.ToList());
        }

        public Witcher3NativeUninstallStatus Status { get; }
        public string Reason { get; }
        public long NativeDeploymentCommitSequence { get; }
        public ReadOnlyCollection<string> ExactTargets { get; }
        public bool IsExactRemovalCandidate => Status == Witcher3NativeUninstallStatus.ExactRemovalCandidate;
    }

    /// <summary>
    /// Checks the entire committed InstallLog/VMA owner footprint, not just the visible
    /// mod-folder inventory. This prevents an apparently simple uninstall from silently
    /// removing older bin/config files, shared winners, external fallbacks or script effects.
    /// The caller still requires a live native root mutation lease and filesystem preflight.
    /// </summary>
    public static class Witcher3NativeUninstallPolicy
    {
        private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

        public static Witcher3NativeUninstallDecision Assess(Witcher3NativeComponentIndex index,
            NativeStateCaptureSnapshot native, string nativeOwnerKey)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));
            if (native == null) throw new ArgumentNullException(nameof(native));
            long sequence = index.NativeDeploymentCommitSequence;
            if (!index.HasCompleteDiskInventory || index.Issues.Count != 0 || native.Issues.Count != 0 ||
                native.InstallLog.DeploymentCommitSequence != sequence ||
                native.DeploymentCoverage == NativeStateCaptureCoverage.Unavailable ||
                native.DeploymentCoverage == NativeStateCaptureCoverage.Partial)
                return Deny(Witcher3NativeUninstallStatus.NativeStateUncertain,
                    "Removal requires one complete and consistent committed native and disk observation.", sequence);
            if (String.IsNullOrWhiteSpace(nativeOwnerKey))
                return Deny(Witcher3NativeUninstallStatus.OwnerNotFound,
                    "Removal requires a registered native owner key.", sequence);

            List<InstallLogReadMod> registrations = native.InstallLog.Mods
                .Where(mod => !mod.Hidden && Names.Equals(mod.ModKey, nativeOwnerKey)).ToList();
            if (registrations.Count != 1)
                return Deny(Witcher3NativeUninstallStatus.OwnerNotFound,
                    "Exactly one active native owner registration is required.", sequence);
            InstallLogReadMod owner = registrations[0];
            if (owner.InstallRoot != ModInstallRoot.Data ||
                (owner.InstallMethod != ModInstallMethod.Direct && owner.InstallMethod != ModInstallMethod.Virtual))
                return Deny(Witcher3NativeUninstallStatus.UnsupportedRegistration,
                    "Only ordinary Data-root Direct/Virtual ownership can be reviewed.", sequence);
            if (owner.HasInstallScript ||
                native.InstallLog.IniEdits.Any(edit => edit.Values.Any(value => Names.Equals(value.OwnerKey, nativeOwnerKey))) ||
                native.InstallLog.GameValues.Any(value => value.Values.Any(entry => Names.Equals(entry.OwnerKey, nativeOwnerKey))) ||
                native.ReplayReferences.Any(reference => Names.Equals(reference.ModKey, nativeOwnerKey)))
                return Deny(Witcher3NativeUninstallStatus.NonFileEffectsBlocked,
                    "Script replay, INI edits or game-value effects need their own removal contract.", sequence);

            var expected = new HashSet<string>(Names);
            List<Witcher3NativeComponent> components = index.Components.Where(component =>
                component.Owners.Any(mod => Names.Equals(mod.NativeModKey, nativeOwnerKey))).ToList();
            if (components.Count == 0)
                return Deny(Witcher3NativeUninstallStatus.OwnerNotFound,
                    "The native owner has no verified Witcher Mods/DLC components.", sequence);
            foreach (Witcher3NativeComponent component in components)
            {
                if (component.IsMergeCandidate || component.ObservedOnDisk != true ||
                    component.Owners.Count != 1 || !Names.Equals(component.Owners[0].NativeModKey, nativeOwnerKey) ||
                    component.Owners[0].InstallMethod != owner.InstallMethod ||
                    component.HasMixedManagedAndUnmanagedFiles || !component.HasContentPayload || component.Files.Count == 0)
                    return Deny(Witcher3NativeUninstallStatus.SharedOrUnmanagedComponent,
                        "The component is shared, generated, externally modified or not entirely verified: " +
                        component.GameRelativeFolderPath, sequence);
                foreach (Witcher3ComponentFile file in component.Files)
                {
                    if (file.ObservedOnDisk != true || file.WinnerAmbiguous || file.HasOriginalFallback ||
                        file.Source == Witcher3ComponentFileSource.UnmanagedDiskFile ||
                        file.NativeOwnerKeys.Count != 1 || !Names.Equals(file.NativeOwnerKeys[0], nativeOwnerKey) ||
                        !Names.Equals(file.IntendedWinnerKey, nativeOwnerKey))
                        return Deny(Witcher3NativeUninstallStatus.SharedOrUnmanagedComponent,
                            "A file has mixed, shadowed, original or uncertain ownership: " + file.GameRelativePath, sequence);
                    string canonical;
                    try { canonical = Witcher3BasicInstallMapping.ValidatePlannedPath(file.GameRelativePath); }
                    catch (Exception ex) when (ex is InvalidDataException || ex is ArgumentException || ex is NotSupportedException)
                    {
                        return Deny(Witcher3NativeUninstallStatus.InvalidTarget, ex.Message, sequence);
                    }
                    string[] segments = canonical.Split('\\');
                    if (!String.Equals(canonical, file.GameRelativePath, StringComparison.Ordinal) ||
                        segments.Length < 4 || !Names.Equals(segments[2], "content") ||
                        !expected.Add(canonical))
                        return Deny(Witcher3NativeUninstallStatus.InvalidTarget,
                            "A native target is noncanonical, duplicated or outside the supported content layout: " +
                            file.GameRelativePath, sequence);
                }
            }

            var registered = new HashSet<ModDeploymentTarget>();
            foreach (InstallLogReadFile file in native.InstallLog.Files)
                if (file.OwnerKeys.Contains(nativeOwnerKey, Names)) registered.Add(file.Target);
            foreach (InstallLogReadDeploymentTarget deployment in native.InstallLog.DeploymentTargets)
                if (deployment.OwnerKeys.Contains(nativeOwnerKey, Names)) registered.Add(deployment.Target);
            foreach (VirtualModReadLink link in native.VirtualState.Links)
                if (Names.Equals(link.OwnerKey, nativeOwnerKey)) registered.Add(link.Target);
            var exact = new HashSet<ModDeploymentTarget>(expected.Select(path =>
                ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, path)));
            if (!registered.SetEquals(exact))
                return Deny(Witcher3NativeUninstallStatus.NativeFootprintMismatch,
                    "Native ownership includes files outside the reviewed components or omits a component file.", sequence);

            // Pure Virtual file removal can restore an original overwrite file. Before allowing
            // a candidate, require explicit evidence that every active link has NO such fallback.
            // A missing observation is not evidence of absence.
            if (owner.InstallMethod == ModInstallMethod.Virtual)
            {
                foreach (ModDeploymentTarget target in exact)
                {
                    List<VirtualModReadLink> links = native.VirtualState.Links
                        .Where(link => link.Target.Equals(target) && link.Active).ToList();
                    List<NativeStateCaptureVirtualFallback> fallbacks = native.VirtualFallbacks
                        .Where(fallback => fallback.Target.Equals(target)).ToList();
                    if (links.Count != 1 || !Names.Equals(links[0].OwnerKey, nativeOwnerKey) ||
                        fallbacks.Count != 1 || fallbacks[0].State != NativeStateCaptureVirtualFallbackState.ExplicitlyAbsent)
                        return Deny(Witcher3NativeUninstallStatus.VirtualFallbackUnverified,
                            "A Virtual link or its unmanaged/original fallback cannot be proved safe to remove: " + target, sequence);
                }
            }
            else if (native.VirtualState.Links.Any(link => Names.Equals(link.OwnerKey, nativeOwnerKey)))
                return Deny(Witcher3NativeUninstallStatus.NativeFootprintMismatch,
                    "A Direct owner unexpectedly has Virtual deployment links.", sequence);

            return new Witcher3NativeUninstallDecision(Witcher3NativeUninstallStatus.ExactRemovalCandidate,
                "Exclusive native file targets only; byte integrity and transaction recovery require Windows verification.",
                sequence, expected.OrderBy(path => path, Names).ThenBy(path => path, StringComparer.Ordinal));
        }

        private static Witcher3NativeUninstallDecision Deny(Witcher3NativeUninstallStatus status,
            string reason, long sequence)
        {
            return new Witcher3NativeUninstallDecision(status, reason, sequence, new string[0]);
        }
    }
}
