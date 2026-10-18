using System;
using System.Xml;
using Nexus.Client.Games.ManagedContent;

namespace Nexus.Client.ModManagement.ManagedContent
{
    /// <summary>Immutable literal XML merge scope; no XPath, scripts or cross-mod baseline is accepted.</summary>
    internal sealed class PackageXmlFragmentRule
    {
        /// <summary>Validates safe independent input/output paths and literal unqualified XML names.</summary>
        internal PackageXmlFragmentRule(string sourceFolder, string destinationPath, string documentRoot,
            string recordElement, string identityAttribute)
        {
            string[] parts;
            if (!ManagedContentPath.TryParse(sourceFolder, out parts)) throw new ArgumentException("Invalid fragment sourceFolder.");
            SourceFolder = String.Join("\\", parts);
            if (!ManagedContentPath.TryParse(destinationPath, out parts)) throw new ArgumentException("Invalid generated destinationPath.");
            DestinationPath = String.Join("\\", parts);
            if (DestinationPath.Equals(SourceFolder, StringComparison.OrdinalIgnoreCase) ||
                DestinationPath.StartsWith(SourceFolder + "\\", StringComparison.OrdinalIgnoreCase) ||
                SourceFolder.StartsWith(DestinationPath + "\\", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Fragment inputs and generated output must have independent paths.");
            DocumentRoot = RequireName(documentRoot);
            RecordElement = RequireName(recordElement);
            IdentityAttribute = RequireName(identityAttribute);
        }

        internal string SourceFolder { get; }
        internal string DestinationPath { get; }
        internal string DocumentRoot { get; }
        internal string RecordElement { get; }
        internal string IdentityAttribute { get; }

        /// <summary>Accepts a bounded XML NCName without interpreting expressions or namespace prefixes.</summary>
        private static string RequireName(string name)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(name) || name.Length > 128) throw new ArgumentException("XML names require 1-128 characters.");
                return XmlConvert.VerifyNCName(name);
            }
            catch (XmlException error) { throw new ArgumentException("Fragment XML names must be literal unqualified names.", error); }
        }
    }
}
