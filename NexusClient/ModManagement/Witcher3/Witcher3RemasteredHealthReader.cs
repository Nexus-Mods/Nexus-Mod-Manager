using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Security;
using System.Text;
using Nexus.Client.Games.Witcher3;

namespace Nexus.Client.ModManagement.Witcher3
{
	/// <summary>Read-only diagnostics, never a game-load-order or installation authorization.</summary>
	public enum Witcher3RemasteredHealthSeverity
	{
		Information = 0,
		Warning = 1
	}

	public enum Witcher3RemasteredHealthIssueCode
	{
		EditionUnconfirmed = 0,
		Dx12ExecutableMissing = 1,
		SettingsMissing = 2,
		SettingsReadFailed = 3,
		SettingsTooLarge = 4,
		SettingsUnsupportedEncoding = 5,
		SettingsChangedDuringRead = 6,
		SettingsAmbiguous = 7,
		GlobalModsDisabled = 8,
		LocalModsDisabled = 9,
		GlobalModsNotConfirmed = 10,
		LocalModsNotConfirmed = 11
	}

	public enum Witcher3RemasteredSettingsReadStatus
	{
		Loaded = 0,
		Missing = 1,
		ReadFailed = 2,
		TooLarge = 3,
		UnsupportedEncoding = 4,
		ChangedDuringRead = 5,
		Ambiguous = 6
	}

	public enum Witcher3RemasteredModSwitchState
	{
		NotConfirmed = 0,
		Enabled = 1,
		Disabled = 2,
		Ambiguous = 3
	}

	public sealed class Witcher3RemasteredHealthIssue
	{
		internal Witcher3RemasteredHealthIssue(Witcher3RemasteredHealthIssueCode code,
			Witcher3RemasteredHealthSeverity severity)
		{
			Code = code;
			Severity = severity;
		}

		public Witcher3RemasteredHealthIssueCode Code { get; }
		public Witcher3RemasteredHealthSeverity Severity { get; }
	}

	/// <summary>
	/// A disposable point-in-time observation. Even two enabled switches do not prove the
	/// game is currently loading any mod; missing keys/files are NOT interpreted as disabled.
	/// </summary>
	public sealed class Witcher3RemasteredHealthObservation
	{
		internal Witcher3RemasteredHealthObservation(Witcher3Edition edition,
			string dx12Path, bool dx12Observed, string settingsPath,
			Witcher3RemasteredSettingsReadStatus settingsStatus,
			Witcher3RemasteredModSwitchState globalMods,
			Witcher3RemasteredModSwitchState localMods, string diagnostic,
			IEnumerable<Witcher3RemasteredHealthIssue> issues)
		{
			Edition = edition;
			Dx12ExecutablePath = dx12Path;
			Dx12ExecutableObserved = dx12Observed;
			SettingsPath = settingsPath;
			SettingsStatus = settingsStatus;
			GlobalMods = globalMods;
			LocalMods = localMods;
			Diagnostic = diagnostic;
			Issues = new ReadOnlyCollection<Witcher3RemasteredHealthIssue>(
				new List<Witcher3RemasteredHealthIssue>(issues));
		}

		public Witcher3Edition Edition { get; }
		public string Dx12ExecutablePath { get; }
		public bool Dx12ExecutableObserved { get; }
		public string SettingsPath { get; }
		public Witcher3RemasteredSettingsReadStatus SettingsStatus { get; }
		public Witcher3RemasteredModSwitchState GlobalMods { get; }
		public Witcher3RemasteredModSwitchState LocalMods { get; }
		public string Diagnostic { get; }
		public ReadOnlyCollection<Witcher3RemasteredHealthIssue> Issues { get; }
	}

	/// <summary>
	/// W3R.8a: bounded, read-only inspection of the Remastered DX12 binary and its
	/// Documents configuration. Does not create files, run tools, or mutate native state.
	/// Calling code must supply the resolved/redirected Documents directory (not guess it).
	/// </summary>
	public static class Witcher3RemasteredHealthReader
	{
		public const int MaximumSettingsBytes = 2 * 1024 * 1024;
		private const int MaximumLines = 30000;
		private const string ContentManagerSection = "ContentManager/Mods";

		public static Witcher3RemasteredHealthObservation Read(string physicalGameRoot,
			string documentsDirectory, Witcher3Edition edition)
		{
			if (String.IsNullOrWhiteSpace(physicalGameRoot) || !Path.IsPathRooted(physicalGameRoot))
				throw new ArgumentException("An absolute Witcher game root is required.", nameof(physicalGameRoot));
			if (String.IsNullOrWhiteSpace(documentsDirectory) || !Path.IsPathRooted(documentsDirectory))
				throw new ArgumentException("An absolute Documents directory is required.", nameof(documentsDirectory));

			string dx12 = Path.Combine(Path.GetFullPath(physicalGameRoot), "bin", "x64_dx12", "witcher3.exe");
			string settings = Path.Combine(Path.GetFullPath(documentsDirectory), "The Witcher 3", "dx12user.settings");
			bool dx12Present = File.Exists(dx12);
			SettingsObservation observed = ReadSettings(settings);
			var issues = new List<Witcher3RemasteredHealthIssue>();
			if (edition != Witcher3Edition.Remastered)
				Add(issues, Witcher3RemasteredHealthIssueCode.EditionUnconfirmed,
					Witcher3RemasteredHealthSeverity.Warning);
			if (!dx12Present)
				Add(issues, Witcher3RemasteredHealthIssueCode.Dx12ExecutableMissing,
					Witcher3RemasteredHealthSeverity.Warning);

			switch (observed.Status)
			{
				case Witcher3RemasteredSettingsReadStatus.Missing:
					Add(issues, Witcher3RemasteredHealthIssueCode.SettingsMissing,
						Witcher3RemasteredHealthSeverity.Information);
					break;
				case Witcher3RemasteredSettingsReadStatus.ReadFailed:
					Add(issues, Witcher3RemasteredHealthIssueCode.SettingsReadFailed,
						Witcher3RemasteredHealthSeverity.Warning);
					break;
				case Witcher3RemasteredSettingsReadStatus.TooLarge:
					Add(issues, Witcher3RemasteredHealthIssueCode.SettingsTooLarge,
						Witcher3RemasteredHealthSeverity.Warning);
					break;
				case Witcher3RemasteredSettingsReadStatus.UnsupportedEncoding:
					Add(issues, Witcher3RemasteredHealthIssueCode.SettingsUnsupportedEncoding,
						Witcher3RemasteredHealthSeverity.Warning);
					break;
				case Witcher3RemasteredSettingsReadStatus.ChangedDuringRead:
					Add(issues, Witcher3RemasteredHealthIssueCode.SettingsChangedDuringRead,
						Witcher3RemasteredHealthSeverity.Warning);
					break;
				case Witcher3RemasteredSettingsReadStatus.Ambiguous:
					Add(issues, Witcher3RemasteredHealthIssueCode.SettingsAmbiguous,
						Witcher3RemasteredHealthSeverity.Warning);
					break;
				case Witcher3RemasteredSettingsReadStatus.Loaded:
					AddSwitchIssue(issues, observed.GlobalMods,
						Witcher3RemasteredHealthIssueCode.GlobalModsDisabled,
						Witcher3RemasteredHealthIssueCode.GlobalModsNotConfirmed);
					AddSwitchIssue(issues, observed.LocalMods,
						Witcher3RemasteredHealthIssueCode.LocalModsDisabled,
						Witcher3RemasteredHealthIssueCode.LocalModsNotConfirmed);
					break;
			}

			return new Witcher3RemasteredHealthObservation(edition, dx12, dx12Present,
				settings, observed.Status, observed.GlobalMods, observed.LocalMods,
				observed.Diagnostic, issues);
		}

		private static void Add(List<Witcher3RemasteredHealthIssue> issues,
			Witcher3RemasteredHealthIssueCode code, Witcher3RemasteredHealthSeverity severity)
		{
			issues.Add(new Witcher3RemasteredHealthIssue(code, severity));
		}

		private static void AddSwitchIssue(List<Witcher3RemasteredHealthIssue> issues,
			Witcher3RemasteredModSwitchState state,
			Witcher3RemasteredHealthIssueCode disabled, Witcher3RemasteredHealthIssueCode unknown)
		{
			if (state == Witcher3RemasteredModSwitchState.Disabled)
				Add(issues, disabled, Witcher3RemasteredHealthSeverity.Warning);
			else if (state != Witcher3RemasteredModSwitchState.Enabled)
				Add(issues, unknown, Witcher3RemasteredHealthSeverity.Information);
		}

		private sealed class SettingsObservation
		{
			internal SettingsObservation(Witcher3RemasteredSettingsReadStatus status,
				Witcher3RemasteredModSwitchState globalMods = Witcher3RemasteredModSwitchState.NotConfirmed,
				Witcher3RemasteredModSwitchState localMods = Witcher3RemasteredModSwitchState.NotConfirmed,
				string diagnostic = null)
			{
				Status = status;
				GlobalMods = globalMods;
				LocalMods = localMods;
				Diagnostic = diagnostic;
			}
			internal Witcher3RemasteredSettingsReadStatus Status { get; }
			internal Witcher3RemasteredModSwitchState GlobalMods { get; }
			internal Witcher3RemasteredModSwitchState LocalMods { get; }
			internal string Diagnostic { get; }
		}

		private static SettingsObservation ReadSettings(string path)
		{
			try
			{
				var before = new FileInfo(path);
				before.Refresh();
				// FileInfo.Exists may hide access failures. Attempt an actual read so
				// access denied becomes ReadFailed rather than a false Missing result.
				long beforeSize = before.Exists ? before.Length : -1;
				DateTime beforeWrite = before.Exists ? before.LastWriteTimeUtc : DateTime.MinValue;
				byte[] bytes;
				using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
					FileShare.ReadWrite | FileShare.Delete))
				{
					if (stream.Length > MaximumSettingsBytes)
						return new SettingsObservation(Witcher3RemasteredSettingsReadStatus.TooLarge);
					using (var memory = new MemoryStream())
					{
						var buffer = new byte[8192];
						int read;
						while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
						{
							if (memory.Length + read > MaximumSettingsBytes)
								return new SettingsObservation(Witcher3RemasteredSettingsReadStatus.TooLarge);
							memory.Write(buffer, 0, read);
						}
						bytes = memory.ToArray();
					}
				}
				var after = new FileInfo(path);
				after.Refresh();
				if (!before.Exists || !after.Exists || beforeSize != bytes.Length || after.Length != bytes.Length ||
					beforeWrite != after.LastWriteTimeUtc)
					return new SettingsObservation(Witcher3RemasteredSettingsReadStatus.ChangedDuringRead);

				string text;
				if (!TryDecode(bytes, out text))
					return new SettingsObservation(Witcher3RemasteredSettingsReadStatus.UnsupportedEncoding);
				return ParseSettings(text);
			}
			catch (FileNotFoundException)
			{
				return new SettingsObservation(Witcher3RemasteredSettingsReadStatus.Missing);
			}
			catch (DirectoryNotFoundException)
			{
				return new SettingsObservation(Witcher3RemasteredSettingsReadStatus.Missing);
			}
			catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
				error is SecurityException || error is NotSupportedException)
			{
				return new SettingsObservation(Witcher3RemasteredSettingsReadStatus.ReadFailed,
					diagnostic: error.Message);
			}
		}

		private static bool TryDecode(byte[] bytes, out string text)
		{
			text = null;
			if (bytes.Length >= 4 && ((bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF) ||
				(bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)))
				return false;
			int offset = 0;
			Encoding decoder = new UTF8Encoding(false, true);
			if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
				offset = 3;
			else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
			{
				decoder = new UnicodeEncoding(false, true, true);
				offset = 2;
			}
			else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
			{
				decoder = new UnicodeEncoding(true, true, true);
				offset = 2;
			}
			try
			{
				text = decoder.GetString(bytes, offset, bytes.Length - offset);
				return text.IndexOf('\0') < 0;
			}
			catch (DecoderFallbackException) { return false; }
		}

		private static SettingsObservation ParseSettings(string text)
		{
			bool inMods = false;
			int sections = 0;
			bool globalSeen = false;
			bool localSeen = false;
			bool ambiguous = false;
			var global = Witcher3RemasteredModSwitchState.NotConfirmed;
			var local = Witcher3RemasteredModSwitchState.NotConfirmed;
			int lines = 0;
			foreach (string raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
			{
				if (++lines > MaximumLines)
					return new SettingsObservation(Witcher3RemasteredSettingsReadStatus.Ambiguous,
						diagnostic: "The INI line limit was reached.");
				string line = raw.Trim();
				if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal) ||
					line.StartsWith("#", StringComparison.Ordinal)) continue;
				if (line[0] == '[')
				{
					if (!line.EndsWith("]", StringComparison.Ordinal))
					{
						ambiguous = true;
						inMods = false;
						continue;
					}
					string name = line.Substring(1, line.Length - 2).Trim();
					inMods = String.Equals(name, ContentManagerSection, StringComparison.OrdinalIgnoreCase);
					if (inMods) sections++;
					continue;
				}
				if (!inMods) continue;
				int separator = line.IndexOf('=');
				if (separator < 1)
				{
					if (line.StartsWith("Enabled", StringComparison.OrdinalIgnoreCase))
						ambiguous = true;
					continue;
				}
				string key = line.Substring(0, separator).Trim();
				string value = line.Substring(separator + 1).Trim();
				if (String.Equals(key, "Enabled", StringComparison.OrdinalIgnoreCase))
				{
					if (globalSeen) ambiguous = true;
					globalSeen = true;
					global = ParseSwitch(value);
				}
				else if (String.Equals(key, "EnabledLocal", StringComparison.OrdinalIgnoreCase))
				{
					if (localSeen) ambiguous = true;
					localSeen = true;
					local = ParseSwitch(value);
				}
			}
			if (sections > 1 || ambiguous)
				return new SettingsObservation(Witcher3RemasteredSettingsReadStatus.Ambiguous,
					Witcher3RemasteredModSwitchState.Ambiguous,
					Witcher3RemasteredModSwitchState.Ambiguous,
					"Duplicate content-manager settings or an ambiguous INI section were observed.");
			return new SettingsObservation(Witcher3RemasteredSettingsReadStatus.Loaded, global, local);
		}

		private static Witcher3RemasteredModSwitchState ParseSwitch(string value)
		{
			if (String.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
				String.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
				return Witcher3RemasteredModSwitchState.Enabled;
			if (String.Equals(value, "0", StringComparison.OrdinalIgnoreCase) ||
				String.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
				return Witcher3RemasteredModSwitchState.Disabled;
			return Witcher3RemasteredModSwitchState.NotConfirmed;
		}
	}
}
