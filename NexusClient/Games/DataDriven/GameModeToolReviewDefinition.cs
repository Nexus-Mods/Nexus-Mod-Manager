using System;
using System.Linq;
using Nexus.Client.ModManagement.ManagedContent;

namespace Nexus.Client.Games.DataDriven
{
    /// <summary>Declares an optional, manually reviewed external tool with unmanaged output.</summary>
    public sealed class GameModeToolReviewDefinition
    {
        public int SchemaVersion { get; set; }
        public string Provider { get; set; }
        public int Revision { get; set; }
        public string Compatibility { get; set; }
        public string OutputPolicy { get; set; }

        /// <summary>Resolves a supported declaration without changing legacy tool behavior when absent.</summary>
        internal static ExternalToolLaunchRule Create(GameModeToolDefinition tool, GameModeDefinition game)
        {
            if (tool == null) throw new ArgumentNullException(nameof(tool));
            GameModeToolReviewDefinition review = tool.LaunchReview;
            if (review == null) return null;
            if (review.SchemaVersion != 1 || review.Provider != "reviewed-external-tool-v1" ||
                review.Revision <= 0 || review.OutputPolicy != "external-unmanaged-v1")
                throw new ArgumentException("launchReview requires schemaVersion 1, provider reviewed-external-tool-v1, a positive revision and outputPolicy external-unmanaged-v1.");
            if (review.Compatibility != ExternalToolLaunchRule.ManualCompatibility &&
                review.Compatibility != ExternalToolLaunchRule.RemasteredCompatibility)
                throw new ArgumentException("Unsupported external-tool compatibility provider.");
            if (review.Compatibility == ExternalToolLaunchRule.RemasteredCompatibility &&
                (game == null || game.ModeId != "Witcher3Remastered" || game.BehaviorProfile != "witcher3remastered"))
                throw new ArgumentException("The Remastered Script Merger compatibility provider requires the Remastered host.");
            string[] names = tool.ExecutableNames ?? new[] { tool.ExecutableName };
            if (names.Length == 0 || names.Length > 16 || names.Any(name => !DataDrivenDefinitionRules.IsSafeToolExecutableName(name)) ||
                names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length ||
                (tool.ExecutableNames != null && tool.ExecutableName != null))
                throw new ArgumentException("Reviewed tools require 1-16 distinct safe executable alternatives.");
            if (review.Compatibility == ExternalToolLaunchRule.RemasteredCompatibility &&
                names.Any(name => !ExternalToolLaunchRule.IsScriptMergerName(name)))
                throw new ArgumentException("The Remastered provider accepts only characterized Script Merger executable names.");
            return new ExternalToolLaunchRule(review.Compatibility, names);
        }
    }
}
