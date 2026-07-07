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
        private const string DefinitionFileName = "game.json";
        private const string SupportedToolsFileName = "supportedTools.json";

        private readonly GameModeDefinitionValidator _validator = new GameModeDefinitionValidator();

        public GameModeDefinitionLoadResult LoadFromPath(string path)
        {
            if (File.Exists(path))
                return LoadFromFiles(new[] { path });
            return LoadFromDirectory(path);
        }

        public GameModeDefinitionLoadResult LoadFromDirectory(string definitionsDirectory)
        {
            if (string.IsNullOrWhiteSpace(definitionsDirectory) || !Directory.Exists(definitionsDirectory))
                return new GameModeDefinitionLoadResult();

            return LoadFromFiles(Directory.EnumerateFiles(definitionsDirectory, DefinitionFileName, SearchOption.AllDirectories));
        }

        private GameModeDefinitionLoadResult LoadFromFiles(IEnumerable<string> filePaths)
        {
            var result = new GameModeDefinitionLoadResult();
            var definitionsById = new Dictionary<string, GameModeDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (string filePath in filePaths)
            {
                GameModeDefinition definition = LoadDefinition(filePath, result);
                var issues = _validator.Validate(definition).ToList();
                foreach (var issue in issues)
                    result.AddIssue(issue);
                if (definition == null || issues.Any(x => x.Severity == GameModeDefinitionIssueSeverity.Error))
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

        private GameModeDefinition LoadDefinition(string filePath, GameModeDefinitionLoadResult result)
        {
            try
            {
                GameModeDefinition definition = JsonConvert.DeserializeObject<GameModeDefinition>(File.ReadAllText(filePath));
                if (definition != null)
                {
                    definition.DefinitionPath = filePath;
                    definition.DefinitionDirectory = Path.GetDirectoryName(filePath);
                    LoadSupportedTools(definition, result);
                }
                return definition;
            }
            catch (Exception ex)
            {
                result.AddIssue(new GameModeDefinitionIssue { FilePath = filePath, Severity = GameModeDefinitionIssueSeverity.Error, Message = "Could not load definition: " + ex.Message });
                return null;
            }
        }

        private void LoadSupportedTools(GameModeDefinition definition, GameModeDefinitionLoadResult result)
        {
            string toolsPath = Path.Combine(definition.DefinitionDirectory ?? string.Empty, SupportedToolsFileName);
            if (!File.Exists(toolsPath))
                return;

            try
            {
                var toolsDefinition = JsonConvert.DeserializeObject<GameModeSupportedToolsDefinition>(File.ReadAllText(toolsPath));
                if (toolsDefinition?.SupportedTools != null)
                    definition.SupportedTools = toolsDefinition.SupportedTools;
            }
            catch (Exception ex)
            {
                result.AddIssue(new GameModeDefinitionIssue
                {
                    FilePath = toolsPath,
                    GameModeId = definition.ModeId,
                    Severity = GameModeDefinitionIssueSeverity.Error,
                    Message = "Could not load supported tools: " + ex.Message
                });
            }
        }
    }
}
