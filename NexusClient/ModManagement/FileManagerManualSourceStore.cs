namespace Nexus.Client.ModManagement
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml.Linq;

    using Nexus.Client.Settings;

    public interface IFileManagerManualSourceStore
    {
        IDictionary<string, FileManagerSource> Load(string gameModeId);
        void SetSource(string gameModeId, string normalizedRelativePath, FileManagerSource source);
    }

    public sealed class SettingsFileManagerManualSourceStore : IFileManagerManualSourceStore
    {
        private const string SettingKeyPrefix = "FileManager.ManualSources.";
        private readonly ISettings _settings;

        public SettingsFileManagerManualSourceStore(ISettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            _settings = settings;
        }

        public IDictionary<string, FileManagerSource> Load(string gameModeId)
        {
            Dictionary<string, FileManagerSource> sources = new Dictionary<string, FileManagerSource>(StringComparer.OrdinalIgnoreCase);
            string key = GetSettingKey(gameModeId);
            if (!_settings.DockPanelLayouts.ContainsKey(key) || String.IsNullOrWhiteSpace(_settings.DockPanelLayouts[key]))
                return sources;

            try
            {
                XDocument document = XDocument.Parse(_settings.DockPanelLayouts[key]);
                XElement root = document.Element("fileManagerManualSources");
                if (root == null)
                    return sources;

                foreach (XElement file in root.Elements("file"))
                {
                    XAttribute pathAttribute = file.Attribute("path");
                    XAttribute sourceAttribute = file.Attribute("source");
                    if (pathAttribute == null || sourceAttribute == null)
                        continue;

                    string normalizedPath = FileManagerQueryService.NormalizePath(pathAttribute.Value);
                    FileManagerSource source;
                    if (normalizedPath.Length == 0 || !Enum.TryParse(sourceAttribute.Value, out source) || !FileManagerSourceDisplay.IsManualSource(source) || source == FileManagerSource.Untracked)
                        continue;

                    sources[normalizedPath] = source;
                }
            }
            catch
            {
            }

            return sources;
        }

        public void SetSource(string gameModeId, string normalizedRelativePath, FileManagerSource source)
        {
            if (!FileManagerSourceDisplay.IsManualSource(source))
                throw new InvalidOperationException("The selected source cannot be assigned manually.");

            string normalizedPath = FileManagerQueryService.NormalizePath(normalizedRelativePath);
            if (normalizedPath.Length == 0)
                throw new ArgumentException("A relative deployment path is required.", "normalizedRelativePath");

            Dictionary<string, FileManagerSource> sources = new Dictionary<string, FileManagerSource>(Load(gameModeId), StringComparer.OrdinalIgnoreCase);
            if (source == FileManagerSource.Untracked)
                sources.Remove(normalizedPath);
            else
                sources[normalizedPath] = source;

            string key = GetSettingKey(gameModeId);
            if (sources.Count == 0)
                _settings.DockPanelLayouts.Remove(key);
            else
                _settings.DockPanelLayouts[key] = Serialize(sources);

            _settings.Save();
        }

        private static string Serialize(IDictionary<string, FileManagerSource> sources)
        {
            XDocument document = new XDocument(
                new XElement("fileManagerManualSources",
                    sources.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x =>
                        new XElement("file",
                            new XAttribute("path", x.Key),
                            new XAttribute("source", x.Value)))));
            return document.ToString(SaveOptions.DisableFormatting);
        }

        private static string GetSettingKey(string gameModeId)
        {
            return SettingKeyPrefix + (gameModeId ?? String.Empty).ToLowerInvariant();
        }
    }
}