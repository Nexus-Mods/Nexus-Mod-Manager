using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Nexus.Client.Games.DataDriven
{
    public class GameModeDefinitionLoader
    {
        private const string DefinitionFileName = "game.json";
        private const string SupportedToolsFileName = "supportedTools.json";

        private readonly GameModeDefinitionValidator _validator = new GameModeDefinitionValidator();
        private readonly GameModeSupportedToolsDefinitionValidator _supportedToolsValidator = new GameModeSupportedToolsDefinitionValidator();
        private readonly JsonSerializerSettings _serializerSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error
        };

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
            IEnumerable<string> orderedPaths = (filePaths ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x, StringComparer.Ordinal);

            foreach (string filePath in orderedPaths)
            {
                GameModeDefinition definition = LoadDefinition(filePath, result);
                if (definition == null)
                    continue;

                List<GameModeDefinitionIssue> issues = _validator.Validate(definition).ToList();
                AddIssues(result, issues);
                if (issues.Any(x => x.Severity == GameModeDefinitionIssueSeverity.Error))
                    continue;

                if (!LoadSupportedTools(definition, result))
                    continue;

                GameModeDefinition existing;
                if (definitionsById.TryGetValue(definition.ModeId, out existing))
                {
                    result.AddIssue(new GameModeDefinitionIssue
                    {
                        FilePath = filePath,
                        GameModeId = definition.ModeId,
                        PropertyPath = "modeId",
                        Severity = GameModeDefinitionIssueSeverity.Error,
                        Message = "Duplicate data-driven GameMode id. First definition: " + existing.DefinitionPath
                    });
                    continue;
                }

                definitionsById[definition.ModeId] = definition;
                result.AddDefinition(definition);
            }

            GameModeStorageSharingRegistry.ReplaceDefinitions(result.Definitions);

            foreach (GameModeDefinitionIssue issue in result.Issues)
                Trace.WriteLine("GameMode definition " + issue);

            return result;
        }

        private GameModeDefinition LoadDefinition(string filePath, GameModeDefinitionLoadResult result)
        {
            try
            {
                string json = File.ReadAllText(filePath);
                JObject root = JObject.Parse(json);
                RejectNullValues(root);
                RejectProperties(root, "supportedTools", "definitionPath", "definitionDirectory");

                GameModeDefinition definition = JsonConvert.DeserializeObject<GameModeDefinition>(json, _serializerSettings);
                if (definition == null)
                    throw new JsonSerializationException("Definition deserialized to null.");

                definition.DefinitionPath = filePath;
                definition.DefinitionDirectory = Path.GetDirectoryName(filePath);
                definition.SupportedTools = new List<GameModeToolDefinition>();
                return definition;
            }
            catch (Exception ex)
            {
                result.AddIssue(new GameModeDefinitionIssue
                {
                    FilePath = filePath,
                    PropertyPath = GetJsonPath(ex),
                    Severity = GameModeDefinitionIssueSeverity.Error,
                    Message = "Could not load definition: " + ex.Message
                });
                return null;
            }
        }

        private bool LoadSupportedTools(GameModeDefinition definition, GameModeDefinitionLoadResult result)
        {
            string toolsPath = Path.Combine(definition.DefinitionDirectory ?? string.Empty, SupportedToolsFileName);
            if (!File.Exists(toolsPath))
            {
                definition.SupportedTools = new List<GameModeToolDefinition>();
                return true;
            }

            try
            {
                string json = File.ReadAllText(toolsPath);
                JObject root = JObject.Parse(json);
                RejectNullValues(root);
                RejectProperties(root, "definitionPath");

                GameModeSupportedToolsDefinition toolsDefinition = JsonConvert.DeserializeObject<GameModeSupportedToolsDefinition>(json, _serializerSettings);
                if (toolsDefinition == null)
                    throw new JsonSerializationException("Supported-tools definition deserialized to null.");

                toolsDefinition.DefinitionPath = toolsPath;
                List<GameModeDefinitionIssue> issues = _supportedToolsValidator.Validate(toolsDefinition, definition).ToList();
                AddIssues(result, issues);
                if (issues.Any(x => x.Severity == GameModeDefinitionIssueSeverity.Error))
                    return false;

                definition.SupportedTools = new List<GameModeToolDefinition>(toolsDefinition.SupportedTools);
                return true;
            }
            catch (Exception ex)
            {
                result.AddIssue(new GameModeDefinitionIssue
                {
                    FilePath = toolsPath,
                    GameModeId = definition.ModeId,
                    PropertyPath = GetJsonPath(ex),
                    Severity = GameModeDefinitionIssueSeverity.Error,
                    Message = "Could not load supported tools: " + ex.Message
                });
                return false;
            }
        }

        private static void RejectProperties(JObject root, params string[] propertyNames)
        {
            foreach (string propertyName in propertyNames ?? new string[0])
            {
                JProperty property = root.Properties().FirstOrDefault(x => string.Equals(x.Name, propertyName, StringComparison.OrdinalIgnoreCase));
                if (property != null)
                {
                    string message = string.Equals(propertyName, "supportedTools", StringComparison.OrdinalIgnoreCase)
                        ? "Inline supportedTools is not allowed. Use the sibling supportedTools.json file."
                        : "Runtime-only property is not allowed in the JSON document: " + property.Name;
                    throw new JsonSerializationException(message, property.Path, 0, 0, null);
                }
            }
        }

        private static void RejectNullValues(JContainer token)
        {
            JToken nullToken = token.DescendantsAndSelf().FirstOrDefault(x => x.Type == JTokenType.Null || x.Type == JTokenType.Undefined);
            if (nullToken != null)
                throw new JsonSerializationException("Null values are not allowed by the data-driven Game Mode contract.", nullToken.Path, 0, 0, null);
        }

        private static void AddIssues(GameModeDefinitionLoadResult result, IEnumerable<GameModeDefinitionIssue> issues)
        {
            foreach (GameModeDefinitionIssue issue in issues ?? Enumerable.Empty<GameModeDefinitionIssue>())
                result.AddIssue(issue);
        }

        private static string GetJsonPath(Exception exception)
        {
            JsonSerializationException serializationException = exception as JsonSerializationException;
            if (serializationException != null && !string.IsNullOrWhiteSpace(serializationException.Path))
                return serializationException.Path;

            JsonReaderException readerException = exception as JsonReaderException;
            if (readerException != null && !string.IsNullOrWhiteSpace(readerException.Path))
                return readerException.Path;

            return null;
        }
    }
}
