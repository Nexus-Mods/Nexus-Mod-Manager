using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.Games.Witcher3;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Witcher3;
using Nexus.Client.Mods;
using Nexus.Client.Util;

namespace Nexus.Client.Games.DataDriven
{
    /// <summary>
    /// Thin native-install adapter for the Remastered identity. It reuses the real NMM
    /// Direct/Virtual file installers and InstallLog rather than creating a Witcher file
    /// writer. This class is wired to the factory behind the W3R activation gate.
    /// </summary>
    public sealed class DataDrivenWitcher3RemasteredGameMode : DataDrivenGameMode,
        IReviewedBasicInstallMappingProvider
    {
        public DataDrivenWitcher3RemasteredGameMode(IEnvironmentInfo environmentInfo,
            FileUtil fileUtility, GameModeDefinition definition) : base(environmentInfo, fileUtility, definition)
        {
            if (definition == null ||
                !String.Equals(definition.ModeId, "Witcher3Remastered", StringComparison.Ordinal) ||
                !String.Equals(definition.BehaviorProfile, "witcher3remastered", StringComparison.Ordinal))
                throw new ArgumentException("A dedicated Remastered game definition is required.", nameof(definition));
        }

        public IReadOnlyList<KeyValuePair<string, string>> MapBasicInstallFiles(IMod mod,
            ModInstallContext installContext, IEnumerable<KeyValuePair<string, string>> selectedFiles)
        {
            return MapReviewedFiles(mod, installContext, selectedFiles, false);
        }

        /// <summary>
        /// A path-only preview for the native upgrader. NOT an upgrade authorization:
        /// the caller must hold the recovered native mutation lease and compare this exact
        /// mapping with the complete existing InstallLog/VMA ownership footprint.
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, string>> MapBasicUpgradeFiles(IMod mod,
            ModInstallContext installContext, IEnumerable<KeyValuePair<string, string>> selectedFiles)
        {
            return MapReviewedFiles(mod, installContext, selectedFiles, true);
        }

        private IReadOnlyList<KeyValuePair<string, string>> MapReviewedFiles(IMod mod,
            ModInstallContext installContext, IEnumerable<KeyValuePair<string, string>> selectedFiles,
            bool ownershipWillBeReviewedByNativeLease)
        {
            if (mod == null) throw new ArgumentNullException(nameof(mod));
            if (selectedFiles == null) throw new ArgumentNullException(nameof(selectedFiles));
            if (mod.HasInstallScript)
                throw new InvalidDataException("Remastered scripted installers need a separate selected-operation review.");

            string root = GameModeEnvironmentInfo.InstallationPath;
            Witcher3InstallationObservation observation = Witcher3InstallationProbe.Inspect(root);
            if (!DataDrivenWitcher3RemasteredGameModeDescriptor.IsConfirmedRemastered(observation.Edition))
                throw new InvalidDataException("The target game root no longer has confirmed Remastered 5.x executable evidence.");

            List<string> archiveFiles = mod.GetFileList();
            if (archiveFiles == null)
                throw new InvalidDataException("The Witcher 3 archive file inventory is unavailable.");
            if (archiveFiles.Any(path => String.IsNullOrWhiteSpace(path)))
                throw new InvalidDataException("The Witcher 3 archive inventory contains an empty file path.");
            var allowedSources = new HashSet<string>(archiveFiles.Select(path => path.Replace('/', '\\')),
                StringComparer.OrdinalIgnoreCase);
            var requested = selectedFiles.ToList();
            foreach (KeyValuePair<string, string> file in requested)
                if (String.IsNullOrWhiteSpace(file.Key) ||
                    !allowedSources.Contains(file.Key.Replace('/', '\\')))
                    throw new InvalidDataException("The selected Witcher source is not part of this archive: " + file.Key);

            string archiveFileName = Path.GetFileName(mod.Filename ?? String.Empty);
            if (String.IsNullOrWhiteSpace(archiveFileName))
                throw new InvalidDataException("The Witcher 3 archive needs a stable local archive filename.");
            string identity = "witcher3|" + (mod.DownloadId ?? String.Empty) + "|" + archiveFileName;
            string displayName = String.IsNullOrWhiteSpace(mod.ModName)
                ? Path.GetFileNameWithoutExtension(archiveFileName) : mod.ModName;
            IReadOnlyList<KeyValuePair<string, string>> mapped = Witcher3BasicInstallMapping.Map(
                requested, identity, displayName, observation.Edition.Edition, installContext);
            // Fail closed for any pre-existing component or reparse ancestor. This is only
            // an initial collision preflight; not a substitute for atomic ownership locking.
            if (!ownershipWillBeReviewedByNativeLease)
                Witcher3NativeDestinationPreflight.RequireNewComponents(root, mapped.Select(file => file.Value));
            return mapped;
        }

        // VMA may call the overload without IMod during native relinking. Both
        // paths must accept only the EXACT same already-reviewed game-relative target.
        public override string GetModFormatAdjustedPath(IModFormat format, string path, bool ignoreIfPresent)
        {
            return Witcher3BasicInstallMapping.ValidatePlannedPath(path);
        }

        public override string GetModFormatAdjustedPath(IModFormat format, string path,
            IMod mod, bool ignoreIfPresent)
        {
            return Witcher3BasicInstallMapping.ValidatePlannedPath(path);
        }
    }
}
