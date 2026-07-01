using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Nexus.Client.Games.DataDriven
{
    public class GameModeDefinitionLoader
    {
        private readonly GameModeDefinitionValidator _validator = new GameModeDefinitionValidator();

        public GameModeDefinitionLoadResult LoadFromDirectory(string definitionsDirectory)
        {
            var result = new GameModeDefinitionLoadResult();
            if (string.IsNullOrWhiteSpace(definitionsDirectory) || !Directory.Exists(definitionsDirectory))
                return result;

            var definitionsById = new Dictionary<string, GameModeDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (string filePath in Directory.EnumerateFiles(definitionsDirectory, "*.json", SearchOption.AllDirectories))
            {
                GameModeDefinition definition = null;
                try
                {
                    definition = JsonConvert.DeserializeObject<GameModeDefinition>(File.ReadAllText(filePath));
                    if (definition != null)
                    {
                        definition.DefinitionPath = filePath;
                        definition.DefinitionDirectory = Path.GetDirectoryName(filePath);
                    }
                }
                catch (Exception ex)
                {
                    result.AddIssue(new GameModeDefinitionIssue { FilePath = filePath, Severity = GameModeDefinitionIssueSeverity.Error, Message = "Could not load definition: " + ex.Message });
                    continue;
                }

                var issues = _validator.Validate(definition).ToList();
                foreach (var issue in issues)
                    result.AddIssue(issue);
                if (issues.Any(x => x.Severity == GameModeDefinitionIssueSeverity.Error))
                    continue;

                if (definitionsById.ContainsKey(definition.ModeId))
                {
                    result.AddIssue(new GameModeDefinitionIssue
                    {
                        FilePath = filePath,
                        GameModeId = definition.ModeId,
                        Severity = GameModeDefinitionIssueSeverity.Error,
                        Message = "Duplicate data-driven GameMode id. First definition: " + definitionsById[definition.ModeId].DefinitionPath
                    });
                    continue;
                }

                definitionsById[definition.ModeId] = definition;
                result.AddDefinition(definition);
            }

            foreach (var issue in result.Issues)
                Trace.WriteLine("GameMode definition " + issue);

            return result;
        }
    }
}
