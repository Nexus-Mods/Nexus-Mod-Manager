using System.Collections.Generic;
using System.Linq;
using System.Text;

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
            message.AppendLine("NMM could not validate the Game Storage folders for this game.");
            message.AppendLine();
            foreach (var item in Items.Where(x => x.Status != GameStorageHealthStatus.Healthy && x.Status != GameStorageHealthStatus.LinkFolderNotRequired))
            {
                message.AppendLine($"{item.Status}: {item.Message}");
                if (!string.IsNullOrWhiteSpace(item.Path))
                    message.AppendLine($"Path: {item.Path}");
                foreach (var fix in item.SuggestedFixes)
                    message.AppendLine($"- {fix}");
                message.AppendLine();
            }
            message.AppendLine("NMM will not create replacement InstallInfo or Mods folders for an existing Game Storage. Missing VirtualInstall and required Link Folder directories may be created during recovery after the selected paths are confirmed.");
            return message.ToString();
        }
    }

    public class GameStorageCandidate
    {
        public string CandidateKind { get; set; }
        public string CandidateRoot { get; set; }
        public string GameId { get; set; }
        public string StorageId { get; set; }
        public string InstallInfoPath { get; set; }
        public string ModsPath { get; set; }
        public string VirtualInstallPath { get; set; }
        public string LinkFolderPath { get; set; }
        public int ConfidenceScore { get; set; }
        public GameStorageCandidateConfidence ConfidenceLevel { get; set; }
        public List<string> Evidence { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
        public bool LinkFolderRequired { get; set; }
        public bool RequiresUserConfirmation { get; set; }
        public bool IsSharedModsLibrary { get; set; }
        public List<string> SharedModsGameIds { get; set; } = new List<string>();
        public string SharedModsDescription { get; set; }
        public string Recommendation { get; set; }
    }
}