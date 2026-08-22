using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.GameStorage
{
    public class GameStorageHealthItem
    {
        public GameStorageFolderRole? Role { get; set; }
        public string Path { get; set; }
        public GameStorageHealthStatus Status { get; set; }
        public string Message { get; set; }
        public bool IsRequired { get; set; }
        public bool IsRecoverable { get; set; }
        public List<string> SuggestedFixes { get; set; } = new List<string>();
    }

    public class GameStorageHealthCheck
    {
        public string GameId { get; set; }
        public string StorageId { get; set; }
        public bool IsHealthy => Items.All(x => x.Status == GameStorageHealthStatus.Healthy || x.Status == GameStorageHealthStatus.LegacyValidNeedsInitialization || x.Status == GameStorageHealthStatus.CompatibleSharedModsLibrary || x.Status == GameStorageHealthStatus.LinkFolderNotRequired);
        public bool NeedsInitialization => Items.Any(x => x.Status == GameStorageHealthStatus.LegacyValidNeedsInitialization);
        public List<GameStorageHealthItem> Items { get; } = new List<GameStorageHealthItem>();

        public string ToUserMessage()
        {
            var message = new StringBuilder();
            message.AppendLine(LanguageManager.Get("GameStorage.Health.ValidationFailed", "NMM could not validate the Game Storage folders for this game."));
            message.AppendLine();
            foreach (var item in Items.Where(x => x.Status != GameStorageHealthStatus.Healthy && x.Status != GameStorageHealthStatus.LinkFolderNotRequired))
            {
                message.AppendLine(LanguageManager.Format("GameStorage.Health.Item", "{0}: {1}", GameStorageLocalization.GetHealthStatusName(item.Status), item.Message));
                if (!string.IsNullOrWhiteSpace(item.Path))
                    message.AppendLine(LanguageManager.Format("GameStorage.Health.Path", "Path: {0}", item.Path));
                foreach (var fix in item.SuggestedFixes)
                    message.AppendLine("- " + fix);
                message.AppendLine();
            }
            message.AppendLine(LanguageManager.Get("GameStorage.Health.RecoveryPolicy", "NMM will not create replacement InstallInfo or Mods folders for an existing Game Storage. Missing VirtualInstall and required Link Folder directories may be created during recovery after the selected paths are confirmed."));
            return message.ToString();
        }
    }

    public class GameStorageCandidate
    {
        private string _candidateKind;
        private string _candidateKindDisplay;
        private GameStorageCandidateConfidence _confidenceLevel;
        private string _confidenceDisplay;
        private string _recommendation;
        private string _recommendationDisplay;

        public string CandidateKind
        {
            get { return _candidateKind; }
            set
            {
                _candidateKind = value;
                _candidateKindDisplay = GameStorageLocalization.GetCandidateKindName(value);
            }
        }

        public string CandidateKindDisplay => _candidateKindDisplay ?? GameStorageLocalization.GetCandidateKindName(_candidateKind);
        public string CandidateRoot { get; set; }
        public string GameId { get; set; }
        public string StorageId { get; set; }
        public string InstallInfoPath { get; set; }
        public string ModsPath { get; set; }
        public string VirtualInstallPath { get; set; }
        public string LinkFolderPath { get; set; }
        public int ConfidenceScore { get; set; }
        public GameStorageCandidateConfidence ConfidenceLevel
        {
            get { return _confidenceLevel; }
            set
            {
                _confidenceLevel = value;
                _confidenceDisplay = GameStorageLocalization.GetConfidenceName(value);
            }
        }
        public string ConfidenceDisplay => _confidenceDisplay ?? GameStorageLocalization.GetConfidenceName(_confidenceLevel);
        public List<string> Evidence { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
        public bool LinkFolderRequired { get; set; }
        public bool RequiresUserConfirmation { get; set; }
        public bool IsSharedModsLibrary { get; set; }
        public List<string> SharedModsGameIds { get; set; } = new List<string>();
        public string SharedModsDescription { get; set; }
        public string Recommendation
        {
            get { return _recommendation; }
            set
            {
                _recommendation = value;
                _recommendationDisplay = GameStorageLocalization.GetRecommendation(value);
            }
        }
        public string RecommendationDisplay => _recommendationDisplay ?? GameStorageLocalization.GetRecommendation(_recommendation);
    }

    internal static class GameStorageLocalization
    {
        private static readonly string[] FolderRoleNames =
        {
            LanguageManager.Get("GameStorage.FolderRole.InstallInfo", "Install Info"),
            LanguageManager.Get("GameStorage.FolderRole.Mods", "Mods"),
            LanguageManager.Get("GameStorage.FolderRole.VirtualInstall", "Virtual Install"),
            LanguageManager.Get("GameStorage.FolderRole.LinkFolder", "Link Folder"),
            LanguageManager.Get("GameStorage.FolderRole.Cache", "Cache"),
            LanguageManager.Get("GameStorage.FolderRole.Backups", "Backups")
        };

        private static readonly string[] HealthStatusNames =
        {
            LanguageManager.Get("GameStorage.HealthStatus.Healthy", "Healthy"),
            LanguageManager.Get("GameStorage.HealthStatus.MissingStorageRoot", "Missing storage root"),
            LanguageManager.Get("GameStorage.HealthStatus.MissingInstallInfo", "Missing Install Info"),
            LanguageManager.Get("GameStorage.HealthStatus.MissingMods", "Missing Mods"),
            LanguageManager.Get("GameStorage.HealthStatus.MissingVirtualInstall", "Missing Virtual Install"),
            LanguageManager.Get("GameStorage.HealthStatus.MissingInstallLog", "Missing InstallLog"),
            LanguageManager.Get("GameStorage.HealthStatus.MissingLinkFolder", "Missing Link Folder"),
            LanguageManager.Get("GameStorage.HealthStatus.LinkFolderRequired", "Link Folder required"),
            LanguageManager.Get("GameStorage.HealthStatus.LinkFolderNotRequired", "Link Folder not required"),
            LanguageManager.Get("GameStorage.HealthStatus.LinkFolderOnWrongDrive", "Link Folder on wrong drive"),
            LanguageManager.Get("GameStorage.HealthStatus.MismatchedGame", "Mismatched game"),
            LanguageManager.Get("GameStorage.HealthStatus.MismatchedStorageId", "Mismatched storage ID"),
            LanguageManager.Get("GameStorage.HealthStatus.SuspiciousEmptyFolder", "Suspicious empty folder"),
            LanguageManager.Get("GameStorage.HealthStatus.PartialMatch", "Partial match"),
            LanguageManager.Get("GameStorage.HealthStatus.LegacyValidNeedsInitialization", "Legacy valid; initialization required"),
            LanguageManager.Get("GameStorage.HealthStatus.CompatibleSharedModsLibrary", "Compatible shared Mods library"),
            LanguageManager.Get("GameStorage.HealthStatus.NotWritable", "Not writable"),
            LanguageManager.Get("GameStorage.HealthStatus.Unknown", "Unknown")
        };

        private static readonly string[] ConfidenceNames =
        {
            LanguageManager.Get("GameStorage.Confidence.Low", "Low"),
            LanguageManager.Get("GameStorage.Confidence.Medium", "Medium"),
            LanguageManager.Get("GameStorage.Confidence.High", "High")
        };

        private static readonly Dictionary<string, string> CandidateKindNames =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Proposed setup", LanguageManager.Get("GameStorage.CandidateKind.ProposedSetup", "Proposed setup") },
                { "Known good registry", LanguageManager.Get("GameStorage.CandidateKind.KnownGoodRegistry", "Known good registry") },
                { "Known registry", LanguageManager.Get("GameStorage.CandidateKind.KnownRegistry", "Known registry") },
                { "Last-known-good backup", LanguageManager.Get("GameStorage.CandidateKind.LastKnownGoodBackup", "Last-known-good backup") },
                { "Existing shared-root setup", LanguageManager.Get("GameStorage.CandidateKind.ExistingSharedRootSetup", "Existing shared-root setup") },
                { "Shared root + game-drive staging", LanguageManager.Get("GameStorage.CandidateKind.SharedRootGameDriveStaging", "Shared root + game-drive staging") },
                { "Shared Mods library", LanguageManager.Get("GameStorage.CandidateKind.SharedModsLibrary", "Shared Mods library") },
                { "Shared Mods library backup", LanguageManager.Get("GameStorage.CandidateKind.SharedModsLibraryBackup", "Shared Mods library backup") },
                { "Selected root manifest", LanguageManager.Get("GameStorage.CandidateKind.SelectedRootManifest", "Selected root manifest") },
                { "Root manifest", LanguageManager.Get("GameStorage.CandidateKind.RootManifest", "Root manifest") },
                { "Folder manifests", LanguageManager.Get("GameStorage.CandidateKind.FolderManifests", "Folder manifests") },
                { "Legacy NMM setup", LanguageManager.Get("GameStorage.CandidateKind.LegacyNmmSetup", "Legacy NMM setup") },
                { "Possible InstallInfo folder", LanguageManager.Get("GameStorage.CandidateKind.PossibleInstallInfoFolder", "Possible InstallInfo folder") },
                { "Registry", LanguageManager.Get("GameStorage.CandidateKind.Registry", "Registry") },
                { "Manual paths", LanguageManager.Get("GameStorage.CandidateKind.ManualPaths", "Manual paths") }
            };

        private static readonly Dictionary<string, string> Recommendations =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Preserves the existing InstallInfo and VirtualInstall folders beside the shared Mods library. VirtualInstall is on a different drive from the game, so a Link Folder on the game drive is required.", LanguageManager.Get("GameStorage.Recommendation.PreserveSharedRootWithLinkFolder", "Preserves the existing InstallInfo and VirtualInstall folders beside the shared Mods library. VirtualInstall is on a different drive from the game, so a Link Folder on the game drive is required.") },
                { "Uses this Game Mode's existing InstallInfo and VirtualInstall folders beside the shared Mods library.", LanguageManager.Get("GameStorage.Recommendation.UseExistingSharedRoot", "Uses this Game Mode's existing InstallInfo and VirtualInstall folders beside the shared Mods library.") },
                { "Suggested because the existing VirtualInstall is on a different drive from the game. This keeps the detected InstallInfo and shared Mods paths, but places VirtualInstall on the game drive and avoids a Link Folder.", LanguageManager.Get("GameStorage.Recommendation.GameDriveVirtualInstall", "Suggested because the existing VirtualInstall is on a different drive from the game. This keeps the detected InstallInfo and shared Mods paths, but places VirtualInstall on the game drive and avoids a Link Folder.") },
                { "Shares only the Mods folder. InstallInfo and VirtualInstall remain on the current proposed paths.", LanguageManager.Get("GameStorage.Recommendation.ShareModsOnly", "Shares only the Mods folder. InstallInfo and VirtualInstall remain on the current proposed paths.") }
            };

        public static string GetFolderRoleName(GameStorageFolderRole role)
        {
            int index = (int)role;
            return index >= 0 && index < FolderRoleNames.Length ? FolderRoleNames[index] : role.ToString();
        }

        public static string GetFolderRoleName(GameStorageFolderRole? role)
        {
            return role.HasValue ? GetFolderRoleName(role.Value) : string.Empty;
        }

        public static string GetHealthStatusName(GameStorageHealthStatus status)
        {
            int index = (int)status;
            return index >= 0 && index < HealthStatusNames.Length ? HealthStatusNames[index] : status.ToString();
        }

        public static string GetConfidenceName(GameStorageCandidateConfidence confidence)
        {
            int index = (int)confidence;
            return index >= 0 && index < ConfidenceNames.Length ? ConfidenceNames[index] : confidence.ToString();
        }

        public static string GetCandidateKindName(string candidateKind)
        {
            if (string.IsNullOrEmpty(candidateKind))
                return candidateKind;
            string value;
            return CandidateKindNames.TryGetValue(candidateKind, out value) ? value : candidateKind;
        }

        public static string GetRecommendation(string recommendation)
        {
            if (string.IsNullOrEmpty(recommendation))
                return recommendation;
            string value;
            return Recommendations.TryGetValue(recommendation, out value) ? value : recommendation;
        }
    }
}
