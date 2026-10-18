using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Nexus.Client.ModManagement.Witcher3;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.ModManagement.ManagedContent
{
    /// <summary>Immutable selected-tool policy; it grants no native ownership of external output.</summary>
    internal sealed class ExternalToolLaunchRule
    {
        internal const string ManualCompatibility = "manual-version-info-v1";
        internal const string RemasteredCompatibility = "witcher3-remastered-script-merger-v1";

        /// <summary>Copies validated executable alternatives for one named compatibility provider.</summary>
        internal ExternalToolLaunchRule(string compatibility, IEnumerable<string> executableNames)
        {
            Compatibility = compatibility;
            ExecutableNames = Array.AsReadOnly(executableNames.ToArray());
        }

        internal string Compatibility { get; }
        internal IReadOnlyList<string> ExecutableNames { get; }

        /// <summary>Retains the executable alternatives characterized by the Witcher inspector.</summary>
        internal static bool IsScriptMergerName(string name)
        {
            return String.Equals(name, "WitcherScriptMerger.exe", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(name, "ScriptMerger.exe", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(name, "SMFAE.exe", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Detached version evidence and byte identity, never proof of authenticity or a successful merge.</summary>
    internal sealed class ExternalToolLaunchObservation
    {
        /// <summary>Captures one bounded inspection result for the launch review.</summary>
        internal ExternalToolLaunchObservation(string path, bool canLaunch, string assessment,
            string product, string version, string sha256, long length, DateTime lastWriteUtc)
        {
            ExecutablePath = path;
            CanLaunch = canLaunch;
            Assessment = assessment;
            Product = product;
            Version = version;
            Sha256 = sha256;
            Length = length;
            LastWriteUtc = lastWriteUtc;
        }

        internal string ExecutablePath { get; }
        internal bool CanLaunch { get; }
        internal string Assessment { get; }
        internal string Product { get; }
        internal string Version { get; }
        internal string Sha256 { get; }
        internal long Length { get; }
        internal DateTime LastWriteUtc { get; }

        /// <summary>Rejects changed executable bytes or version evidence after modal approval.</summary>
        internal bool Matches(ExternalToolLaunchObservation other)
        {
            return CanLaunch && other != null && other.CanLaunch &&
                String.Equals(ExecutablePath, other.ExecutablePath, StringComparison.OrdinalIgnoreCase) &&
                Sha256 == other.Sha256 && Length == other.Length && LastWriteUtc == other.LastWriteUtc &&
                Product == other.Product && Version == other.Version && Assessment == other.Assessment;
        }
    }

    /// <summary>Reads one selected executable without running it or scanning/installing tools.</summary>
    internal static class ExternalToolLaunchReview
    {
        private const long MaximumExecutableBytes = 512L * 1024 * 1024;

        /// <summary>Reads bounded VERSIONINFO and hashes the selected file while denying ordinary writers.</summary>
        internal static ExternalToolLaunchObservation Inspect(ExternalToolLaunchRule rule, string executablePath)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            try
            {
                if (String.IsNullOrWhiteSpace(executablePath) || !Path.IsPathRooted(executablePath))
                    return Failed(executablePath);
                string path = Path.GetFullPath(executablePath);
                if (!rule.ExecutableNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase) ||
                    (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                    return Failed(path);
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    long length = stream.Length;
                    DateTime lastWrite = File.GetLastWriteTimeUtc(path);
                    if (length <= 0 || length > MaximumExecutableBytes)
                        return Failed(path);
                    FileVersionInfo metadata = FileVersionInfo.GetVersionInfo(path);
                    string assessment = LanguageManager.Get("ManagedContent.Tool.ManualEvidence",
                        "No game compatibility provider is configured. Verify the tool release and its target game manually.");
                    bool canLaunch = true;
                    if (rule.Compatibility == ExternalToolLaunchRule.RemasteredCompatibility)
                    {
                        Witcher3ScriptMergerToolObservation merger = Witcher3ExternalScriptMergerInspector.Inspect(path);
                        canLaunch = merger.Status == Witcher3ScriptMergerReadStatus.Observed &&
                            merger.Compatibility == Witcher3ScriptMergerCompatibility.RemasteredSupportClaimed;
                        assessment = canLaunch
                            ? LanguageManager.Get("Witcher3.Health.MergerClaimed", "Metadata matches a release family/version advertising Remastered support, but this is NOT runtime compatibility certification.")
                            : LanguageManager.Get("ManagedContent.Tool.MergerBlocked", "This merger's metadata does not establish a Remastered release without known detection caveats. Legacy, unconfirmed and SM-FAE rc.4 selections cannot be launched here. Update or verify the release with its publisher.");
                    }
                    string hash;
                    using (SHA256 algorithm = SHA256.Create())
                        hash = BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", String.Empty);
                    if (stream.Length != length || File.GetLastWriteTimeUtc(path) != lastWrite)
                        return Failed(path);
                    return new ExternalToolLaunchObservation(path, canLaunch, assessment, Clean(metadata.ProductName),
                        Clean(!String.IsNullOrWhiteSpace(metadata.ProductVersion) ? metadata.ProductVersion : metadata.FileVersion),
                        hash, length, lastWrite);
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                error is SecurityException || error is ArgumentException || error is NotSupportedException ||
                error is Win32Exception || error is BadImageFormatException || error is CryptographicException)
            {
                return Failed(executablePath);
            }
        }

        /// <summary>Produces an explicit unavailable result rather than accepting incomplete evidence.</summary>
        private static ExternalToolLaunchObservation Failed(string path)
        {
            return new ExternalToolLaunchObservation(path, false,
                LanguageManager.Get("ManagedContent.Tool.InspectionFailed", "The selected executable is missing, linked, inaccessible, changed during inspection or exceeds the inspection limit. Select a regular supported executable and try again."),
                String.Empty, String.Empty, null, 0, DateTime.MinValue);
        }

        /// <summary>Bounds and removes control characters from self-reported executable metadata.</summary>
        private static string Clean(string text)
        {
            var result = new StringBuilder();
            foreach (char character in text ?? String.Empty)
            {
                if (result.Length >= 128) break;
                if (!Char.IsControl(character)) result.Append(character);
            }
            return result.ToString().Trim();
        }
    }
}
