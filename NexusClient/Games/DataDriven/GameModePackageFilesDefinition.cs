using System;
using System.IO;
using Nexus.Client.ModManagement.ManagedContent;

namespace Nexus.Client.Games.DataDriven
{
    /// <summary>Declares a bounded package-local XML merge with ordinary native generated-file ownership.</summary>
    public sealed class GameModePackageFilesDefinition
    {
        public int SchemaVersion { get; set; }
        public string Provider { get; set; }
        public int Revision { get; set; }
        public string SourceFolder { get; set; }
        public string DestinationPath { get; set; }
        public string DocumentRoot { get; set; }
        public string RecordElement { get; set; }
        public string IdentityAttribute { get; set; }

        /// <summary>Resolves only supported Data-root hosts; omitted declarations retain the existing pipeline.</summary>
        internal static PackageXmlFragmentRule Create(GameModeDefinition definition)
        {
            GameModePackageFilesDefinition files = definition.PackageFiles;
            if (files == null) return null;
            if (definition.BehaviorProfile != "generic" && definition.BehaviorProfile != "managed-content")
                throw new ArgumentException("packageFiles requires the generic or managed-content host; it cannot replace legacy game-specific merge semantics.");
            if (definition.ModInstall == null || definition.ModInstall.PathAdjustmentProfile != "none" ||
                definition.ModInstall.SupportsGameRootInstall != false || definition.ModInstall.ManagedInstallationPath != null ||
                (definition.ModInstall.GameRootPackageRules != null && definition.ModInstall.GameRootPackageRules.Count != 0))
                throw new ArgumentException("packageFiles requires ordinary Data-root installation without secondary path rules.");
            if (files.SchemaVersion != 1 || files.Provider != "package-xml-elements-v1" || files.Revision <= 0)
                throw new ArgumentException("packageFiles requires schemaVersion 1, provider package-xml-elements-v1 and a positive revision.");
            var rule = new PackageXmlFragmentRule(files.SourceFolder, files.DestinationPath,
                files.DocumentRoot, files.RecordElement, files.IdentityAttribute);
            if (definition.BehaviorProfile == "managed-content")
            {
                try { DataDrivenManagedContentRules.Create(definition.ManagedContent).ValidatePlannedPath(rule.DestinationPath); }
                catch (InvalidDataException error) { throw new ArgumentException("The generated output is outside the configured managed layout.", error); }
            }
            return rule;
        }
    }
}
