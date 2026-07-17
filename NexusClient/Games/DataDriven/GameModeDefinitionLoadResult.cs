using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexus.Client.Games.DataDriven
{
    public class GameModeDefinitionLoadResult
    {
        private readonly List<GameModeDefinition> _definitions = new List<GameModeDefinition>();
        private readonly List<GameModeDefinitionIssue> _issues = new List<GameModeDefinitionIssue>();

        public IList<GameModeDefinition> Definitions => _definitions;
        public IList<GameModeDefinitionIssue> Issues => _issues;
        public bool HasErrors => _issues.Any(x => x.Severity == GameModeDefinitionIssueSeverity.Error);

        public void AddDefinition(GameModeDefinition definition)
        {
            if (definition != null)
                _definitions.Add(definition);
        }

        public void AddIssue(GameModeDefinitionIssue issue)
        {
            if (issue != null)
                _issues.Add(issue);
        }
    }

    public class GameModeDefinitionIssue
    {
        public string FilePath { get; set; }
        public string GameModeId { get; set; }
        public string PropertyPath { get; set; }
        public GameModeDefinitionIssueSeverity Severity { get; set; }
        public string Message { get; set; }

        public override string ToString()
        {
            string file = string.IsNullOrWhiteSpace(FilePath) ? string.Empty : " " + FilePath;
            string id = string.IsNullOrWhiteSpace(GameModeId) ? string.Empty : " [" + GameModeId + "]";
            string property = string.IsNullOrWhiteSpace(PropertyPath) ? string.Empty : " (" + PropertyPath + ")";
            return Severity + id + file + property + ": " + Message;
        }
    }

    public enum GameModeDefinitionIssueSeverity
    {
        Warning,
        Error
    }
}
