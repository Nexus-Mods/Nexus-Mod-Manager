using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.Games.Witcher3;

namespace Nexus.Client.ModManagement.Witcher3
{
    /// <summary>Classifies an exact Mods/DLC target set. Only NewComponentsEligible is a possible write candidate.</summary>
    public enum Witcher3NativeWriteStatus
    {
        NewComponentsEligible = 0,
        NativeStateUncertain = 1,
        InvalidTargetSet = 2,
        ExternalComponentBlocked = 3,
        SharedComponentBlocked = 4,
        ManagedComponentBlocked = 5,
        OwnedUpdateCandidateBlocked = 6
    }

    /// <summary>
    /// Detached policy output, NOT a deployment authorization token. Callers must additionally
    /// hold a live, reloaded cross-process native mutation lease and repeat filesystem preflight.
    /// </summary>
    public sealed class Witcher3NativeWriteDecision
    {
        internal Witcher3NativeWriteDecision(Witcher3NativeWriteStatus status, string reason, long nativeSequence)
        {
            Status = status;
            Reason = reason;
            NativeDeploymentCommitSequence = nativeSequence;
        }

        public Witcher3NativeWriteStatus Status { get; }
        public string Reason { get; }
        public long NativeDeploymentCommitSequence { get; }
        public bool EligibleForNewComponentPreflight => Status == Witcher3NativeWriteStatus.NewComponentsEligible;
    }

    /// <summary>
    /// Fail-closed read-only ownership policy. A Witcher folder may have several native owners,
    /// while one NMM archive may contribute multiple independently ordered folders. The policy
    /// never promotes a file's intended winner into ownership authority for a whole folder.
    /// </summary>
    public static class Witcher3NativeWritePolicy
    {
        private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

        public static Witcher3NativeWriteDecision Assess(Witcher3NativeComponentIndex index,
            IEnumerable<string> plannedGameRelativeTargets, string replacingNativeOwnerKey = null)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));
            if (plannedGameRelativeTargets == null) throw new ArgumentNullException(nameof(plannedGameRelativeTargets));
            long sequence = index.NativeDeploymentCommitSequence;
            if (!index.HasCompleteDiskInventory || index.Issues.Count != 0)
                return Deny(Witcher3NativeWriteStatus.NativeStateUncertain,
                    "A complete, consistent native ownership capture and disk inventory are required.", sequence);

            var planned = new HashSet<string>(Names);
            var components = new HashSet<string>(Names);
            try
            {
                foreach (string target in plannedGameRelativeTargets)
                {
                    string checkedPath = Witcher3BasicInstallMapping.ValidatePlannedPath(target);
                    // A data-root path must be the same exact canonical game-relative spelling
                    // that the basic installer will pass to native deployment.
                    if (!String.Equals(checkedPath, target, StringComparison.Ordinal) ||
                        !planned.Add(checkedPath))
                        return Deny(Witcher3NativeWriteStatus.InvalidTargetSet,
                            "Duplicate or noncanonical planned Witcher 3 destination: " + target, sequence);
                    string[] parts = checkedPath.Split('\\');
                    if (parts.Length < 4)
                        return Deny(Witcher3NativeWriteStatus.InvalidTargetSet,
                            "A planned destination must include a component content file.", sequence);
                    if (parts[0] == "Mods" &&
                        (Names.Equals(parts[1], "mod0000_MergedFiles") ||
                         Names.Equals(parts[1], "mod0000____CompilationTrigger")))
                        return Deny(Witcher3NativeWriteStatus.InvalidTargetSet,
                            "Generated Script Merger folders must not be installed as ordinary archives.", sequence);
                    components.Add(parts[0] + "\\" + parts[1]);
                }
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is ArgumentException || ex is NotSupportedException)
            {
                return Deny(Witcher3NativeWriteStatus.InvalidTargetSet, ex.Message, sequence);
            }
            if (planned.Count == 0)
                return Deny(Witcher3NativeWriteStatus.InvalidTargetSet,
                    "No reviewed Witcher 3 file destinations were supplied.", sequence);

            var known = index.Components.ToDictionary(x => x.GameRelativeFolderPath, Names);
            foreach (string componentPath in components.OrderBy(x => x, Names))
            {
                Witcher3NativeComponent component;
                if (!known.TryGetValue(componentPath, out component))
                    continue;
                if (component.ObservedOnDisk != true)
                    return Deny(Witcher3NativeWriteStatus.NativeStateUncertain,
                        "A native component was recorded but is not verified on disk: " + componentPath, sequence);
                if (component.IsEntirelyExternal || component.Owners.Count == 0)
                    return Deny(Witcher3NativeWriteStatus.ExternalComponentBlocked,
                        "Existing manual/external component cannot be adopted: " + componentPath, sequence);
                if (component.HasMixedManagedAndUnmanagedFiles || component.Owners.Count != 1 ||
                    component.Files.Any(file => file.WinnerAmbiguous || file.ObservedOnDisk != true ||
                        file.NativeOwnerKeys.Count != 1 || file.HasOriginalFallback ||
                        file.Source == Witcher3ComponentFileSource.UnmanagedDiskFile))
                    return Deny(Witcher3NativeWriteStatus.SharedComponentBlocked,
                        "Existing component has shared, unmanaged, shadowed or uncertain file ownership: " + componentPath,
                        sequence);

                // Even an exclusively owned component cannot be overwritten until a separately
                // reviewed native update/rollback protocol exists. A matching native key is only
                // evidence to present to that future protocol, NOT permission to overwrite.
                if (!String.IsNullOrWhiteSpace(replacingNativeOwnerKey) &&
                    Names.Equals(component.Owners[0].NativeModKey, replacingNativeOwnerKey) &&
                    component.Files.All(file => Names.Equals(file.IntendedWinnerKey, replacingNativeOwnerKey)))
                    return Deny(Witcher3NativeWriteStatus.OwnedUpdateCandidateBlocked,
                        "Existing component belongs exclusively to the proposed native owner, but updates remain gated: " + componentPath,
                        sequence);
                return Deny(Witcher3NativeWriteStatus.ManagedComponentBlocked,
                    "Existing managed component requires explicit ownership-aware update review: " + componentPath, sequence);
            }

            return new Witcher3NativeWriteDecision(Witcher3NativeWriteStatus.NewComponentsEligible,
                "Every requested Mods/DLC component is absent from the complete native and disk observation; live path preflight is still required.", sequence);
        }

        private static Witcher3NativeWriteDecision Deny(Witcher3NativeWriteStatus status, string reason, long sequence)
        {
            return new Witcher3NativeWriteDecision(status, reason, sequence);
        }
    }
}
