using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using ChinhDo.Transactions;
using Newtonsoft.Json;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;
using Nexus.Client.Util;

namespace Nexus.Client.Games.DataDriven
{
    /// <summary>
    /// Data-driven Baldur's Gate 3 behavior profile.
    /// The definition owns discovery, resources, launcher and ordinary install policy; this class retains only BG3-specific native behavior.
    /// </summary>
    public sealed class DataDrivenBaldursGate3GameMode : DataDrivenGameMode, IDeterministicSpecialFileGameValuePlanProvider
    {
        private const string ManifestMetadataFileName = "info.json";

        public DataDrivenBaldursGate3GameMode(IEnvironmentInfo environmentInfo, FileUtil fileUtility, GameModeDefinition definition)
            : base(environmentInfo, fileUtility, definition)
        {
        }

        /// <summary>
        /// Preserves the legacy file-version lookup for the BG3 launcher.
        /// </summary>
        public override Version GameVersion
        {
            get
            {
                foreach (string executable in GameExecutables ?? new string[0])
                {
                    string fullPath = Path.Combine(ExecutablePath ?? string.Empty, executable);
                    if (!File.Exists(fullPath))
                        continue;

                    string version = FileVersionInfo.GetVersionInfo(fullPath).FileVersion;
                    Version parsed;
                    if (!string.IsNullOrWhiteSpace(version) && Version.TryParse(version.Replace(", ", "."), out parsed))
                        return parsed;
                }

                return null;
            }
        }

        public override IEnumerable<string> WritablePaths => null;
        public override bool HasSecondaryInstallPath => true;
        public override bool RequiresSpecialFileInstallation => true;

        public override string GetModFormatAdjustedPath(IModFormat p_mftModFormat, string p_strPath, bool p_booIgnoreIfPresent)
        {
            return IsManifestMetadataFile(p_strPath)
                ? string.Empty
                : base.GetModFormatAdjustedPath(p_mftModFormat, p_strPath, p_booIgnoreIfPresent);
        }

        public override string GetModFormatAdjustedPath(IModFormat p_mftModFormat, string p_strPath, IMod p_modMod, bool p_booIgnoreIfPresent)
        {
            return IsManifestMetadataFile(p_strPath)
                ? string.Empty
                : base.GetModFormatAdjustedPath(p_mftModFormat, p_strPath, p_modMod, p_booIgnoreIfPresent);
        }

        public override IEnumerable<string> SpecialFileInstall(IMod p_modSelectedMod)
        {
            if (HasRootManifestMetadata(p_modSelectedMod))
                AddManifest(p_modSelectedMod);
            return null;
        }

        public override void SpecialFileUninstall(IMod p_modSelectedMod)
        {
            if (HasRootManifestMetadata(p_modSelectedMod))
                RemoveManifest(p_modSelectedMod);
        }

        public override bool IsSpecialFile(IEnumerable<string> p_strFiles)
        {
            return p_strFiles != null && p_strFiles.Contains(ManifestMetadataFileName, StringComparer.OrdinalIgnoreCase);
        }

        public override bool CheckSecondaryInstall(IMod p_modMod, string optionalFileCheck)
        {
            return IsSecondaryInstallPath(optionalFileCheck);
        }

        /// <summary>Returns the same ordinary archive-file selection used after the legacy BG3 manifest edit.</summary>
        public override IEnumerable<string> GetDeterministicSpecialFileInstallPlan(IMod p_modSelectedMod)
        {
            return p_modSelectedMod == null ? null : p_modSelectedMod.GetFileList();
        }

        /// <summary>Plans exact per-profile ModuleShortDesc entries without modifying modsettings.lsx.</summary>
        public IEnumerable<BasicInstallGameSpecificValue> GetDeterministicSpecialFileGameValues(IMod p_modSelectedMod)
        {
            if (p_modSelectedMod == null)
                throw new ArgumentNullException(nameof(p_modSelectedMod));
            if (!HasRootManifestMetadata(p_modSelectedMod))
                return new BasicInstallGameSpecificValue[0];
            return DataDrivenBaldursGate3ModSettingsValue.BuildPlanFromInfoBytes(
                p_modSelectedMod.GetFile(ManifestMetadataFileName), DataDrivenBaldursGate3ModSettingsValue.ProfilesDirectory);
        }

        /// <summary>Identifies game-specific keys owned by the deterministic BG3 special-install contract.</summary>
        public bool IsDeterministicSpecialFileGameValueKey(string p_strKey)
        {
            return DataDrivenBaldursGate3ModSettingsValue.IsSupportedKey(p_strKey);
        }

        public override IGameSpecificValueInstaller GetGameSpecificValueInstaller(IMod p_modMod, IInstallLog p_ilgInstallLog,
            TxFileManager p_tfmFileManager, FileUtil p_futFileUtility, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
        {
            return new DataDrivenBaldursGate3GameSpecificValueInstaller(p_modMod, p_ilgInstallLog, p_tfmFileManager, p_dlgOverwriteConfirmationDelegate);
        }

        public override IGameSpecificValueInstaller GetGameSpecificValueUpgradeInstaller(IMod p_modMod, IInstallLog p_ilgInstallLog,
            TxFileManager p_tfmFileManager, FileUtil p_futFileUtility, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
        {
            return new DataDrivenBaldursGate3GameSpecificValueUpgradeInstaller(p_modMod, p_ilgInstallLog, p_tfmFileManager, p_dlgOverwriteConfirmationDelegate);
        }

        internal static bool IsSecondaryInstallPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            string normalized = path
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .ToLowerInvariant();

            return normalized.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                   normalized.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) ||
                   normalized.StartsWith("data" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                   normalized.Contains(Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar);
        }

        internal static bool IsManifestMetadataFile(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   string.Equals(Path.GetFileName(path), ManifestMetadataFileName, StringComparison.InvariantCultureIgnoreCase);
        }

        private static bool HasRootManifestMetadata(IMod mod)
        {
            return mod != null && mod.GetFileList().Contains(ManifestMetadataFileName, StringComparer.OrdinalIgnoreCase);
        }

        private static string GetProfilesDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Larian Studios\Baldur's Gate 3\PlayerProfiles");
        }

        private static Bg3Json LoadJson(string json, bool isBorked)
        {
            Bg3Json items = new Bg3Json();
            if (!isBorked)
            {
                items = JsonConvert.DeserializeObject<Bg3Json>(json);
            }
            else
            {
                Bg3BorkedJson borked = JsonConvert.DeserializeObject<Bg3BorkedJson>(json);
                items.MD5 = string.Empty;
                items.Mods = new[]
                {
                    new Bg3Mod
                    {
                        Folder = borked.name,
                        UUID = Guid.NewGuid().ToString(),
                        Name = borked.name,
                        Version = borked.version_number
                    }
                };
            }

            if (items != null)
                items.IsBorked = isBorked;
            return items;
        }

        private static Bg3Json ReadManifestMetadata(IMod mod)
        {
            if (!HasRootManifestMetadata(mod))
                return null;
            return LoadJson(Encoding.ASCII.GetString(mod.GetFile(ManifestMetadataFileName)), false);
        }

        private static void AddManifest(IMod mod)
        {
            string profilesDirectory = GetProfilesDirectory();
            if (!Directory.Exists(profilesDirectory))
                return;

            Bg3Json currentMod = ReadManifestMetadata(mod);
            if (currentMod == null || currentMod.Mods == null)
                return;

            foreach (DirectoryInfo directory in new DirectoryInfo(profilesDirectory).GetDirectories())
            {
                if (directory.Name.Equals("default", StringComparison.OrdinalIgnoreCase))
                    continue;

                string manifest = Path.Combine(directory.FullName, "modsettings.lsx");
                if (!File.Exists(manifest))
                    continue;

                XmlDocument document = new XmlDocument();
                document.Load(manifest);
                XmlNode modsNode = document.DocumentElement.SelectSingleNode("//*[@id='Mods']");
                XmlNode children = modsNode.FirstChild;

                foreach (Bg3Mod manifestMod in currentMod.Mods)
                {
                    XmlNode existing = document.DocumentElement.SelectSingleNode("//*[@value='" + manifestMod.UUID + "']");
                    if (existing != null)
                        continue;

                    XmlElement modDesc = document.CreateElement("node");
                    modDesc.SetAttribute("id", "ModuleShortDesc");
                    AppendAttribute(document, modDesc, "Folder", "LSString", manifestMod.Folder ?? manifestMod.folderName);
                    AppendAttribute(document, modDesc, "MD5", "LSString", string.Empty);
                    AppendAttribute(document, modDesc, "Name", "LSString", manifestMod.Name ?? manifestMod.modName);
                    AppendAttribute(document, modDesc, "UUID", "FixedString", manifestMod.UUID);
                    AppendAttribute(document, modDesc, "Version64", "int64", manifestMod.Version ?? "1");
                    children.AppendChild(modDesc);
                }

                SaveManifest(document, manifest);
            }
        }

        private static void RemoveManifest(IMod mod)
        {
            string profilesDirectory = GetProfilesDirectory();
            if (!Directory.Exists(profilesDirectory))
                return;

            Bg3Json currentMod = ReadManifestMetadata(mod);
            if (currentMod == null || currentMod.Mods == null)
                return;

            foreach (DirectoryInfo directory in new DirectoryInfo(profilesDirectory).GetDirectories())
            {
                if (directory.Name.Equals("default", StringComparison.OrdinalIgnoreCase))
                    continue;

                string manifest = Path.Combine(directory.FullName, "modsettings.lsx");
                if (!File.Exists(manifest))
                    continue;

                XmlDocument document = new XmlDocument();
                document.Load(manifest);
                XmlNode modsNode = document.DocumentElement.SelectSingleNode("//*[@id='Mods']");
                XmlNode children = modsNode.FirstChild;

                foreach (Bg3Mod manifestMod in currentMod.Mods)
                {
                    XmlNode match = currentMod.IsBorked
                        ? document.DocumentElement.SelectSingleNode("//*[@value='" + manifestMod.Name + "']")
                        : document.DocumentElement.SelectSingleNode("//*[@value='" + manifestMod.UUID + "']");
                    if (match != null)
                        children.RemoveChild(match.ParentNode);
                }

                SaveManifest(document, manifest);
            }
        }

        private static void AppendAttribute(XmlDocument document, XmlElement parent, string id, string type, string value)
        {
            XmlElement attribute = document.CreateElement("attribute");
            attribute.SetAttribute("id", id);
            attribute.SetAttribute("type", type);
            attribute.SetAttribute("value", value ?? string.Empty);
            parent.AppendChild(attribute);
        }

        private static void SaveManifest(XmlDocument document, string path)
        {
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "\t"
            };
            using (XmlWriter writer = XmlWriter.Create(path, settings))
                document.Save(writer);
        }

        private sealed class Bg3Json
        {
            public Bg3Mod[] Mods;
            public string MD5;
            public bool IsBorked;
        }

        private sealed class Bg3Mod
        {
            public string Author;
            public string Name;
            public string modName;
            public string Folder;
            public string folderName;
            public string Version;
            public string Description;
            public string UUID;
            public string Created;
            public string[] Dependencies;
            public string Group;
        }

        private sealed class Bg3BorkedJson
        {
            public string name;
            public string version_number;
            public string website_url;
            public string description;
            public string[] dependencies;
        }
    }
}
