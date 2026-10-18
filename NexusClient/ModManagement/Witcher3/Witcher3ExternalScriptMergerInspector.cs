using System;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Security;
using System.Text.RegularExpressions;

namespace Nexus.Client.ModManagement.Witcher3
{
	/// <summary>Evidence from a selected external executable, not evidence of a working merge.</summary>
	public enum Witcher3ScriptMergerReadStatus
	{
		Observed = 0,
		Missing = 1,
		UnexpectedExecutable = 2,
		ReadFailed = 3,
		ChangedDuringRead = 4,
		TooLarge = 5
	}

	public enum Witcher3ScriptMergerFamily
	{
		Unknown = 0,
		Classic = 1,
		Remastered = 2,
		FreshAutomated = 3
	}

	public enum Witcher3ScriptMergerCompatibility
	{
		Unconfirmed = 0,
		LegacyReleaseWarning = 1,
		RemasteredSupportClaimed = 2,
		RemasteredSupportWithGogCaveat = 3
	}

	/// <summary>
	/// Win32 VERSIONINFO fields are self-reported by the binary author and are NOT proof
	/// of authenticity, installed game targeting, script encoding or working merges.
	/// </summary>
	public sealed class Witcher3ScriptMergerVersionMetadata
	{
		public Witcher3ScriptMergerVersionMetadata(string productName, string fileDescription,
			string companyName, string productVersion, string fileVersion)
		{
			ProductName = Clean(productName);
			FileDescription = Clean(fileDescription);
			CompanyName = Clean(companyName);
			ProductVersion = Clean(productVersion);
			FileVersion = Clean(fileVersion);
		}

		public string ProductName { get; }
		public string FileDescription { get; }
		public string CompanyName { get; }
		public string ProductVersion { get; }
		public string FileVersion { get; }

		private static string Clean(string raw)
		{
			if (String.IsNullOrEmpty(raw)) return String.Empty;
			var result = new System.Text.StringBuilder(Math.Min(raw.Length, 128));
			foreach (char character in raw)
			{
				if (result.Length >= 128) break;
				if (!Char.IsControl(character)) result.Append(character);
			}
			return result.ToString().Trim();
		}
	}

	public sealed class Witcher3ScriptMergerToolObservation
	{
		internal Witcher3ScriptMergerToolObservation(string path,
			Witcher3ScriptMergerReadStatus status, Witcher3ScriptMergerFamily family,
			Witcher3ScriptMergerCompatibility compatibility,
			Witcher3ScriptMergerVersionMetadata metadata, string versionLabel)
		{
			ExecutablePath = path;
			Status = status;
			Family = family;
			Compatibility = compatibility;
			Metadata = metadata;
			VersionLabel = versionLabel ?? String.Empty;
		}

		public string ExecutablePath { get; }
		public Witcher3ScriptMergerReadStatus Status { get; }
		public Witcher3ScriptMergerFamily Family { get; }
		public Witcher3ScriptMergerCompatibility Compatibility { get; }
		public Witcher3ScriptMergerVersionMetadata Metadata { get; }
		public string VersionLabel { get; }
	}

	/// <summary>
	/// W3R.8c: inspect one explicitly selected external executable using VERSIONINFO only.
	/// No launching, filesystem search, tool installation, script read or game-file writes.
	/// Metadata alone cannot certify a merger or previously generated output. A generic
	/// WitcherScriptMerger.exe and a version number do not uniquely identify a release family.
	/// </summary>
	public static class Witcher3ExternalScriptMergerInspector
	{
		public const long MaximumExecutableBytes = 512L * 1024 * 1024;
		private static readonly Regex VersionPattern = new Regex(
			@"(?<!\d)(\d{1,4})\.(\d{1,3})(?:\.(\d{1,3}))?(?:[-._\s]*rc[-._\s]*(\d{1,3}))?(?!\d)",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

		public static Witcher3ScriptMergerToolObservation Inspect(string executablePath)
		{
			if (String.IsNullOrWhiteSpace(executablePath) || !Path.IsPathRooted(executablePath))
				throw new ArgumentException("An absolute selected executable path is required.", nameof(executablePath));
			string fullPath = Path.GetFullPath(executablePath);
			if (!IsExpectedExecutableName(Path.GetFileName(fullPath)))
				return Unknown(fullPath, Witcher3ScriptMergerReadStatus.UnexpectedExecutable);
			try
			{
				var before = new FileInfo(fullPath);
				before.Refresh();
				if (!before.Exists)
				{
					// FileInfo.Exists can also be false when metadata access fails.
					// Distinguish that from a genuinely missing selection when possible.
					File.GetAttributes(fullPath);
					return Unknown(fullPath, Witcher3ScriptMergerReadStatus.ReadFailed);
				}
				long length = before.Length;
				DateTime lastWrite = before.LastWriteTimeUtc;
				if (length > MaximumExecutableBytes)
					return Unknown(fullPath, Witcher3ScriptMergerReadStatus.TooLarge);

				// This OS API reads PE metadata; it does not load or run the executable.
				FileVersionInfo info = FileVersionInfo.GetVersionInfo(fullPath);
				var after = new FileInfo(fullPath);
				after.Refresh();
				if (!after.Exists || after.Length != length || after.LastWriteTimeUtc != lastWrite)
					return Unknown(fullPath, Witcher3ScriptMergerReadStatus.ChangedDuringRead);
				return Assess(fullPath, new Witcher3ScriptMergerVersionMetadata(
					info.ProductName, info.FileDescription, info.CompanyName,
					info.ProductVersion, info.FileVersion));
			}
			catch (FileNotFoundException) { return Unknown(fullPath, Witcher3ScriptMergerReadStatus.Missing); }
			catch (DirectoryNotFoundException) { return Unknown(fullPath, Witcher3ScriptMergerReadStatus.Missing); }
			catch (IOException) { return Unknown(fullPath, Witcher3ScriptMergerReadStatus.ReadFailed); }
			catch (UnauthorizedAccessException) { return Unknown(fullPath, Witcher3ScriptMergerReadStatus.ReadFailed); }
			catch (SecurityException) { return Unknown(fullPath, Witcher3ScriptMergerReadStatus.ReadFailed); }
			catch (ArgumentException) { return Unknown(fullPath, Witcher3ScriptMergerReadStatus.ReadFailed); }
			catch (NotSupportedException) { return Unknown(fullPath, Witcher3ScriptMergerReadStatus.ReadFailed); }
			catch (Win32Exception) { return Unknown(fullPath, Witcher3ScriptMergerReadStatus.ReadFailed); }
			catch (BadImageFormatException) { return Unknown(fullPath, Witcher3ScriptMergerReadStatus.ReadFailed); }
		}

		/// <summary>Pure assessment for tested, observed metadata. Does not inspect disk.</summary>
		public static Witcher3ScriptMergerToolObservation Assess(string executablePath,
			Witcher3ScriptMergerVersionMetadata metadata)
		{
			if (metadata == null) throw new ArgumentNullException(nameof(metadata));
			if (String.IsNullOrWhiteSpace(executablePath))
				throw new ArgumentException("An executable path is required.", nameof(executablePath));
			if (!IsExpectedExecutableName(Path.GetFileName(executablePath)))
				return Unknown(executablePath, Witcher3ScriptMergerReadStatus.UnexpectedExecutable);

			Witcher3ScriptMergerFamily family = ResolveFamily(metadata);
			string versionLabel = !String.IsNullOrWhiteSpace(metadata.ProductVersion)
				? metadata.ProductVersion : metadata.FileVersion;
			Witcher3ScriptMergerCompatibility compatibility = Witcher3ScriptMergerCompatibility.Unconfirmed;
			if (family == Witcher3ScriptMergerFamily.Classic)
			{
				// An explicitly identified original/classic family is not 5.x compatible.
				compatibility = Witcher3ScriptMergerCompatibility.LegacyReleaseWarning;
			}
			else
			{
				VersionNumber version;
				if (TryParseVersion(versionLabel, out version))
				{
					if (family == Witcher3ScriptMergerFamily.Remastered && version.Major >= 1)
						compatibility = Witcher3ScriptMergerCompatibility.RemasteredSupportClaimed;
					if (family == Witcher3ScriptMergerFamily.FreshAutomated)
						compatibility = AssessFreshAutomated(version);
				}
			}
			return new Witcher3ScriptMergerToolObservation(executablePath,
				Witcher3ScriptMergerReadStatus.Observed, family, compatibility, metadata, versionLabel);
		}

		private static Witcher3ScriptMergerToolObservation Unknown(string path,
			Witcher3ScriptMergerReadStatus status)
		{
			return new Witcher3ScriptMergerToolObservation(path, status,
				Witcher3ScriptMergerFamily.Unknown, Witcher3ScriptMergerCompatibility.Unconfirmed,
				null, String.Empty);
		}

		private static bool IsExpectedExecutableName(string fileName)
		{
			return String.Equals(fileName, "WitcherScriptMerger.exe", StringComparison.OrdinalIgnoreCase) ||
				String.Equals(fileName, "ScriptMerger.exe", StringComparison.OrdinalIgnoreCase) ||
				String.Equals(fileName, "SMFAE.exe", StringComparison.OrdinalIgnoreCase);
		}

		private static Witcher3ScriptMergerFamily ResolveFamily(Witcher3ScriptMergerVersionMetadata metadata)
		{
			// Not the filename or numerical version. These are untrusted, publisher-authored
			// markers and produce an *advisory*, not proof that the tool is genuine.
			string markers = (metadata.ProductName + " " + metadata.FileDescription + " " +
				metadata.CompanyName).ToLowerInvariant();
			if (markers.Contains("fresh and automated") || markers.Contains("fresh & automated") ||
				markers.Contains("sm-fae") || markers.Contains("phaz42"))
				return Witcher3ScriptMergerFamily.FreshAutomated;
			bool remastered = markers.Contains("remastered");
			bool classic = markers.Contains("classic") || markers.Contains("anothersymbiote");
			if (remastered && classic) return Witcher3ScriptMergerFamily.Unknown;
			if (remastered) return Witcher3ScriptMergerFamily.Remastered;
			if (classic) return Witcher3ScriptMergerFamily.Classic;
			return Witcher3ScriptMergerFamily.Unknown;
		}

		private static Witcher3ScriptMergerCompatibility AssessFreshAutomated(VersionNumber version)
		{
			// SM-FAE's 0.9.8-rc.4 first added 5.x support; rc.5 fixed GOG detection.
			// 0.9.8 without an rc marker is ambiguous in binary VERSIONINFO and therefore
			// cannot be treated as either the older or newer release candidate.
			if (version.Major >= 1)
				return Witcher3ScriptMergerCompatibility.RemasteredSupportClaimed;
			if (version.Major != 0)
				return Witcher3ScriptMergerCompatibility.Unconfirmed;
			if (version.Minor < 9 || (version.Minor == 9 && version.Patch < 8))
				return Witcher3ScriptMergerCompatibility.LegacyReleaseWarning;
			if (version.Minor == 9 && version.Patch == 8 && version.ReleaseCandidate.HasValue)
				return version.ReleaseCandidate.Value >= 5
					? Witcher3ScriptMergerCompatibility.RemasteredSupportClaimed
					: version.ReleaseCandidate.Value == 4
						? Witcher3ScriptMergerCompatibility.RemasteredSupportWithGogCaveat
						: Witcher3ScriptMergerCompatibility.LegacyReleaseWarning;
			return Witcher3ScriptMergerCompatibility.Unconfirmed;
		}

		private struct VersionNumber
		{
			internal int Major;
			internal int Minor;
			internal int Patch;
			internal int? ReleaseCandidate;
		}

		private static bool TryParseVersion(string text, out VersionNumber version)
		{
			version = new VersionNumber();
			if (String.IsNullOrWhiteSpace(text)) return false;
			Match match = VersionPattern.Match(text);
			if (!match.Success) return false;
			int major, minor, patch = 0, rc;
			if (!Int32.TryParse(match.Groups[1].Value, out major) ||
				!Int32.TryParse(match.Groups[2].Value, out minor) ||
				(match.Groups[3].Success && !Int32.TryParse(match.Groups[3].Value, out patch)))
				return false;
			version.Major = major;
			version.Minor = minor;
			version.Patch = patch;
			if (match.Groups[4].Success)
			{
				if (!Int32.TryParse(match.Groups[4].Value, out rc)) return false;
				version.ReleaseCandidate = rc;
			}
			return true;
		}
	}
}
