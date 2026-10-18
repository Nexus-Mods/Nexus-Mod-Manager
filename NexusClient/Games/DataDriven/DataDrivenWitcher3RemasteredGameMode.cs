using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.Games.Witcher3;
using Nexus.Client.Games.ManagedContent;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.ManagedContent;
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
        IReviewedBasicInstallMappingProvider, IManagedContentComponentProvider, IManagedContentOrderDocumentProvider
    {
        private readonly GameModeDefinition _remasteredDefinition;
        private readonly ManagedContentLayoutPlanner _layout;
        private readonly IniOrderDocumentRule _orderDocument;
        public DataDrivenWitcher3RemasteredGameMode(IEnvironmentInfo environmentInfo,
            FileUtil fileUtility, GameModeDefinition definition) : base(environmentInfo, fileUtility, definition)
        {
            if (definition == null ||
                !String.Equals(definition.ModeId, "Witcher3Remastered", StringComparison.Ordinal) ||
                !String.Equals(definition.BehaviorProfile, "witcher3remastered", StringComparison.Ordinal))
                throw new ArgumentException("A dedicated Remastered game definition is required.", nameof(definition));
            _remasteredDefinition = definition;
            if (definition.ManagedContent != null)
                _layout = DataDrivenManagedContentRules.Create(definition.ManagedContent);
            _orderDocument = DataDrivenManagedContentRules.CreateWitcher3Ordering(definition.ManagedContent);
        }

        /// <summary>Exposes the fixed historical Mods/DLC observation scope without widening install or activation policy.</summary>
        public IReadOnlyList<ManagedContentLayoutRule> ComponentRules => Witcher3NativeComponentIndexer.NativeComponentRules;

        /// <summary>Exposes the declared fixed-policy settings observation without enabling Remastered order writes.</summary>
        public IniOrderDocumentRule OrderDocument => _orderDocument;

        protected override IGameLauncher CreateGameLauncher()
        {
            return new DataDrivenWitcher3RemasteredGameLauncher(this, EnvironmentInfo, _remasteredDefinition);
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

            ManagedContentInstallSelection selection = ManagedContentInstallSelection.Create(
                mod, installContext, selectedFiles, "witcher3");
            IReadOnlyList<KeyValuePair<string, string>> mapped = Witcher3BasicInstallMapping.Map(
                selection.Files, selection.PackageIdentity, selection.SuggestedName, observation.Edition.Edition, selection.InstallContext);
            if (_layout != null)
            {
                IReadOnlyList<KeyValuePair<string, string>> configured = selection.Map(_layout);
                // Keep the characterized Witcher policy until the remaining layout semantics
                // have a verified replacement; both paths consume the same frozen selection.
                if (!mapped.SequenceEqual(configured))
                    throw new InvalidDataException("Configured layout differs from the characterized Witcher native mappings.");
                mapped = configured;
            }
            // Component scans count the component itself as depth zero. Keep future native
            // upgrade/removal review able to observe every file accepted during installation.
            if (mapped.Any(file => file.Value.Split('\\').Length - 3 > Witcher3ComponentDiskScan.DefaultMaximumDepth))
                throw new InvalidDataException("The archive exceeds the native Witcher component observation depth.");
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
