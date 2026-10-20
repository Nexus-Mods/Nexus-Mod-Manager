using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.Games.Witcher3;
using Nexus.Client.ModManagement.InstallationLog;

namespace Nexus.Client.ModManagement.Witcher3
{
    /// <summary>
    /// Read-only check AFTER a native uninstall transaction has finished. Failure means a
    /// recovery-required/unknown durability result; it cannot undo a committed transaction.
    /// The physical-presence callback is evaluated only for the reviewed owned targets.
    /// </summary>
    public static class Witcher3NativeUninstallPostcondition
    {
        private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

        public static void RequireReconciled(NativeStateCaptureSnapshot committedAfter,
            string removedOwnerKey, IEnumerable<string> reviewedTargets, Func<string, bool> isStillPresent)
        {
            if (committedAfter == null) throw new ArgumentNullException(nameof(committedAfter));
            if (String.IsNullOrWhiteSpace(removedOwnerKey))
                throw new ArgumentException("The removed native owner key is required.", nameof(removedOwnerKey));
            if (reviewedTargets == null) throw new ArgumentNullException(nameof(reviewedTargets));
            if (isStillPresent == null) throw new ArgumentNullException(nameof(isStillPresent));
            if (committedAfter.Issues.Count != 0 ||
                committedAfter.DeploymentCoverage == NativeStateCaptureCoverage.Unavailable ||
                committedAfter.DeploymentCoverage == NativeStateCaptureCoverage.Partial)
                throw new InvalidDataException("Post-removal native authority is incomplete; recovery verification is required.");
            var targets = new HashSet<string>(Names);
            foreach (string target in reviewedTargets)
            {
                string canonical = Witcher3BasicInstallMapping.ValidatePlannedPath(target);
                if (!String.Equals(target, canonical, StringComparison.Ordinal) || !targets.Add(canonical))
                    throw new InvalidDataException("The recorded uninstall target list is inconsistent.");
            }
            if (targets.Count == 0)
                throw new InvalidDataException("A removal cannot be reconciled without its original reviewed file set.");

            RequireOwnerAbsent(committedAfter, removedOwnerKey);

            foreach (string target in targets.OrderBy(path => path, Names))
                if (isStillPresent(target))
                    throw new InvalidDataException("A reviewed removal target still exists on disk: " + target);
        }

        /// <summary>Checks native owner removal independently of whether files are absent or restored to previous owners.</summary>
        internal static void RequireOwnerAbsent(NativeStateCaptureSnapshot committedAfter, string removedOwnerKey)
        {
            if (committedAfter == null || String.IsNullOrWhiteSpace(removedOwnerKey) || committedAfter.Issues.Count != 0 ||
                committedAfter.DeploymentCoverage == NativeStateCaptureCoverage.Unavailable || committedAfter.DeploymentCoverage == NativeStateCaptureCoverage.Partial)
                throw new InvalidDataException("Post-removal native authority is incomplete.");
            if (committedAfter.InstallLog.Mods.Any(mod => Names.Equals(mod.ModKey, removedOwnerKey)) ||
                committedAfter.InstallLog.Files.Any(file => file.OwnerKeys.Contains(removedOwnerKey, Names)) ||
                committedAfter.InstallLog.DeploymentTargets.Any(target => target.OwnerKeys.Contains(removedOwnerKey, Names)) ||
                committedAfter.VirtualState.Links.Any(link => Names.Equals(link.OwnerKey, removedOwnerKey)) ||
                committedAfter.DeploymentTargets.Any(target => target.Owners.Any(owner => Names.Equals(owner.OwnerKey, removedOwnerKey))) ||
                committedAfter.InstallLog.IniEdits.Any(edit => edit.Values.Any(value => Names.Equals(value.OwnerKey, removedOwnerKey))) ||
                committedAfter.InstallLog.GameValues.Any(value => value.Values.Any(entry => Names.Equals(entry.OwnerKey, removedOwnerKey))) ||
                committedAfter.ReplayReferences.Any(reference => Names.Equals(reference.ModKey, removedOwnerKey)))
                throw new InvalidDataException("The removed native owner still appears in committed InstallLog or deployment state.");
        }
    }
}
