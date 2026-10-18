using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Nexus.Client.ModManagement;

namespace Nexus.Client.Games.ManagedContent
{
	/// <summary>One immutable, bounded folder-layout rule; paths and prefixes are literal single segments.</summary>
	public sealed class ManagedContentLayoutRule
	{
		/// <summary>Validates and captures one component-folder or generated-folder mapping.</summary>
		public ManagedContentLayoutRule(string id, string kind, string sourceRoot, string componentPrefix,
			string contentFolder, string targetRoot, string targetFolder, string componentKind, int maximumComponentNameLength)
		{
			if (!IsIdentifier(id) || !IsIdentifier(componentKind)) throw new ArgumentException("Rule id and componentKind must be identifiers.");
			if (kind != "component-folder" && kind != "generated-folder") throw new ArgumentException("Unknown layout rule kind: " + kind);
			if (targetRoot != "Data") throw new ArgumentException("Only the native Data target root is supported.");
			if (sourceRoot != null) RequireSegment(sourceRoot);
			RequireSegment(componentPrefix); RequireSegment(contentFolder); RequireSegment(targetFolder);
			if (maximumComponentNameLength < componentPrefix.Length + 12 || maximumComponentNameLength > 255)
				throw new ArgumentException("maximumComponentNameLength must fit a prefix, stem and hash and be at most 255.");
			if (kind == "generated-folder" && !String.Equals(sourceRoot, contentFolder, StringComparison.OrdinalIgnoreCase))
				throw new ArgumentException("A generated-folder rule requires sourceRoot to equal contentFolder.");
			Id = id; Kind = kind; SourceRoot = sourceRoot; ComponentPrefix = componentPrefix;
			ContentFolder = contentFolder; TargetFolder = targetFolder; ComponentKind = componentKind;
			MaximumComponentNameLength = maximumComponentNameLength;
		}

		public string Id { get; }
		public string Kind { get; }
		public string SourceRoot { get; }
		public string ComponentPrefix { get; }
		public string ContentFolder { get; }
		public string TargetFolder { get; }
		public string ComponentKind { get; }
		public int MaximumComponentNameLength { get; }

		/// <summary>Checks the bounded public identifier grammar without interpreting executable code or patterns.</summary>
		private static bool IsIdentifier(string value)
		{
			return !String.IsNullOrWhiteSpace(value) && Char.IsLetter(value[0]) &&
				value.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
				(c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.');
		}

		/// <summary>Rejects paths, wildcards and Windows-ambiguous names where a literal folder is required.</summary>
		internal static void RequireSegment(string value)
		{
			string[] parts;
			if (!ManagedContentPath.TryParse(value, out parts) || parts.Length != 1)
				throw new ArgumentException("A safe literal single path segment is required: " + value);
		}
	}

	/// <summary>One immutable exact native Data-root destination and its derived component/rule provenance.</summary>
	public sealed class ManagedContentLayoutFile
	{
		/// <summary>Captures a selected source and its canonical native destination.</summary>
		internal ManagedContentLayoutFile(string sourcePath, string destinationPath, ManagedContentLayoutRule rule, string componentPath)
		{
			SourcePath = sourcePath; DestinationPath = destinationPath; RuleId = rule.Id;
			ComponentPath = componentPath; ComponentKind = rule.ComponentKind;
		}
		public string SourcePath { get; }
		public string DestinationPath { get; }
		public string RuleId { get; }
		public string ComponentPath { get; }
		public string ComponentKind { get; }
		public ModInstallRoot TargetRoot => ModInstallRoot.Data;
	}

	/// <summary>A read-only layout result, never a native ownership reservation or a write permit.</summary>
	public sealed class ManagedContentLayoutPlan
	{
		/// <summary>Freezes the selected mappings, ignored metadata, blockers and deterministic plan identity.</summary>
		internal ManagedContentLayoutPlan(List<ManagedContentLayoutFile> files, List<string> ignored, List<string> issues, string identity)
		{
			Files = new ReadOnlyCollection<ManagedContentLayoutFile>(files);
			IgnoredFiles = new ReadOnlyCollection<string>(ignored);
			Issues = new ReadOnlyCollection<string>(issues); Identity = identity;
		}
		public IReadOnlyList<ManagedContentLayoutFile> Files { get; }
		public IReadOnlyList<string> IgnoredFiles { get; }
		public IReadOnlyList<string> Issues { get; }
		public string Identity { get; }
		public bool IsSupported => Issues.Count == 0 && Files.Count > 0;
	}

	/// <summary>
	/// Configured deterministic folder mapping shared by native BasicInstall and its Collections preview.
	/// Performs no filesystem reads or writes and never changes an install method or root.
	/// </summary>
	public sealed class ManagedContentLayoutPlanner
	{
		public const string ProviderId = "bounded-folder-layout-v1";
		public const int MaximumFiles = 100000;
		private readonly ManagedContentLayoutRule[] _rules;
		private readonly HashSet<string> _ignoredNames;
		private readonly string[] _rejectedSuffixes;
		private readonly int _maximumWrapperSegments;
		private readonly string _configurationIdentity;

		/// <summary>Resolves and snapshots the supported rule vocabulary once per game-mode instance.</summary>
		public ManagedContentLayoutPlanner(string provider, int revision, int maximumWrapperSegments,
			IEnumerable<ManagedContentLayoutRule> rules, IEnumerable<string> ignoredFileNames, IEnumerable<string> rejectedFileSuffixes)
		{
			if (provider != ProviderId) throw new ArgumentException("Unknown installLayout provider: " + provider);
			if (revision < 1) throw new ArgumentException("installLayout.revision must be positive.");
			if (maximumWrapperSegments < 0 || maximumWrapperSegments > 2) throw new ArgumentException("maximumWrapperSegments must be between 0 and 2.");
			_rules = rules == null ? new ManagedContentLayoutRule[0] : rules.ToArray();
			if (_rules.Length < 1 || _rules.Length > 32 || _rules.Any(rule => rule == null)) throw new ArgumentException("installLayout requires 1 to 32 non-null rules.");
			if (_rules.Select(rule => rule.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _rules.Length) throw new ArgumentException("Duplicate layout rule id.");
			_ignoredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string name in ignoredFileNames ?? Enumerable.Empty<string>())
			{
				ManagedContentLayoutRule.RequireSegment(name);
				if (!_ignoredNames.Add(name) || _ignoredNames.Count > 32) throw new ArgumentException("Ignored file names must be unique and bounded to 32.");
			}
			_rejectedSuffixes = (rejectedFileSuffixes ?? Enumerable.Empty<string>()).ToArray();
			if (_rejectedSuffixes.Length > 32 || _rejectedSuffixes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != _rejectedSuffixes.Length)
				throw new ArgumentException("Rejected file suffixes must be unique and bounded to 32.");
			foreach (string suffix in _rejectedSuffixes) ManagedContentLayoutRule.RequireSegment(suffix);
			_maximumWrapperSegments = maximumWrapperSegments;
			var identity = new StringBuilder();
			Append(identity, provider); Append(identity, revision.ToString(CultureInfo.InvariantCulture));
			Append(identity, maximumWrapperSegments.ToString(CultureInfo.InvariantCulture));
			foreach (ManagedContentLayoutRule rule in _rules)
				foreach (string value in new[] { rule.Id, rule.Kind, rule.SourceRoot, rule.ComponentPrefix, rule.ContentFolder,
					rule.TargetFolder, rule.ComponentKind, rule.MaximumComponentNameLength.ToString(CultureInfo.InvariantCulture) }) Append(identity, value);
			Append(identity, "ignored"); foreach (string name in _ignoredNames.OrderBy(value => value, StringComparer.Ordinal)) Append(identity, name);
			Append(identity, "rejected"); foreach (string suffix in _rejectedSuffixes.OrderBy(value => value, StringComparer.Ordinal)) Append(identity, suffix);
			_configurationIdentity = Hash(identity.ToString());
		}

		/// <summary>Exposes the immutable configured rules for native-derived component diagnostics.</summary>
		public IReadOnlyList<ManagedContentLayoutRule> Rules => Array.AsReadOnly(_rules);

		/// <summary>Plans actual selected source/destination pairs, rejecting ambiguous wrappers, rules and collisions.</summary>
		public ManagedContentLayoutPlan Plan(IEnumerable<KeyValuePair<string, string>> selectedFiles, string packageIdentity,
			string suggestedName, ModInstallContext context)
		{
			if (selectedFiles == null) throw new ArgumentNullException(nameof(selectedFiles));
			if (context == null) throw new ArgumentNullException(nameof(context));
			var files = new List<ManagedContentLayoutFile>(); var ignored = new List<string>(); var issues = new List<string>();
			var inputs = new List<KeyValuePair<string, string[]>>();
			var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var identity = new StringBuilder(); Append(identity, _configurationIdentity);
			Append(identity, packageIdentity); Append(identity, suggestedName); Append(identity, context.Method.ToString()); Append(identity, context.InstallRoot.ToString());
			if (context.InstallRoot != ModInstallRoot.Data) issues.Add("Only the ordinary native Data install root is supported.");
			int count = 0;
			foreach (KeyValuePair<string, string> input in selectedFiles)
			{
				if (count++ == MaximumFiles) { issues.Add("The bounded 100000-file selection limit was exceeded."); break; }
				Append(identity, input.Key); Append(identity, input.Value);
				string[] source; string[] destination;
				if (!ManagedContentPath.TryParse(input.Key, out source) || !ManagedContentPath.TryParse(
					String.IsNullOrWhiteSpace(input.Value) ? input.Key : input.Value, out destination))
				{ issues.Add("Unsafe source or selected destination: " + input.Key); continue; }
				if (!sources.Add(String.Join("\\", source))) issues.Add("Duplicate archive source: " + input.Key);
				inputs.Add(new KeyValuePair<string, string[]>(input.Key, destination));
			}
			// Use the outermost matching shape, as the existing Witcher planner does.
			// All payloads must agree on its wrapper; overlapping rules still block below.
			string[] wrapper = null;
			foreach (KeyValuePair<string, string[]> input in inputs)
			{
				if (IsDocumentation(input.Value, 0)) continue;
				List<int> anchors = Enumerable.Range(0, Math.Min(_maximumWrapperSegments + 1, input.Value.Length))
					.Where(offset => _rules.Any(rule => Matches(rule, input.Value, offset))).ToList();
				if (anchors.Count == 0) continue;
				string[] candidate = input.Value.Take(anchors[0]).ToArray();
				if (wrapper == null) wrapper = candidate;
				else if (!wrapper.SequenceEqual(candidate, StringComparer.OrdinalIgnoreCase)) issues.Add("Inconsistent layout wrappers: " + input.Key);
			}
			int skip = wrapper == null ? 0 : wrapper.Length;
			var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var spelling = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (KeyValuePair<string, string[]> input in inputs)
			{
				string[] parts = input.Value;
				if (IsDocumentation(parts, 0) || (StartsWith(parts, wrapper) && IsDocumentation(parts, skip))) { ignored.Add(input.Key); continue; }
				if (!StartsWith(parts, wrapper)) { issues.Add("Payload outside the common wrapper: " + input.Key); continue; }
				ManagedContentLayoutRule[] matches = _rules.Where(rule => Matches(rule, parts, skip)).ToArray();
				if (matches.Length != 1) { issues.Add((matches.Length == 0 ? "Unsupported payload: " : "Ambiguous layout rules: ") + input.Key); continue; }
				ManagedContentLayoutRule match = matches[0];
				if (_rejectedSuffixes.Any(suffix => input.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
					String.Join("\\", parts).EndsWith(suffix, StringComparison.OrdinalIgnoreCase))) { issues.Add("Payload needs a separate effect adapter: " + input.Key); continue; }
				int contentOffset = skip + (match.SourceRoot == null ? 0 : 1);
				string component;
				IEnumerable<string> remainder;
				if (match.Kind == "generated-folder")
				{
					component = ManagedContentPath.GenerateComponentName(packageIdentity, suggestedName, match.ComponentPrefix, match.MaximumComponentNameLength);
					remainder = parts.Skip(skip);
				}
				else { component = parts[contentOffset]; remainder = parts.Skip(contentOffset + 1); }
				if (component == null || component.Length > match.MaximumComponentNameLength) { issues.Add("Missing stable package name/identity or overlong component: " + input.Key); continue; }
				string componentPath = match.TargetFolder + "\\" + component;
				string previous;
				if (spelling.TryGetValue(componentPath, out previous) && previous != componentPath) issues.Add("Inconsistent component spelling: " + input.Key);
				spelling[componentPath] = componentPath;
				string target = componentPath + "\\" + String.Join("\\", remainder);
				string[] safeTarget;
				if (!ManagedContentPath.TryParse(target, out safeTarget)) { issues.Add("Unsafe generated destination: " + input.Key); continue; }
				if (!targets.Add(target)) issues.Add("Duplicate Windows destination: " + target);
				files.Add(new ManagedContentLayoutFile(input.Key, target, match, componentPath));
			}
			foreach (ManagedContentLayoutFile file in files)
				for (int index = file.DestinationPath.IndexOf('\\'); index >= 0; index = file.DestinationPath.IndexOf('\\', index + 1))
					if (targets.Contains(file.DestinationPath.Substring(0, index))) issues.Add("File/directory destination collision: " + file.DestinationPath);
			if (files.Count == 0) issues.Add("No supported payload files were selected.");
			return new ManagedContentLayoutPlan(files, ignored, issues, Hash(identity.ToString()));
		}

		/// <summary>Returns only an already canonical configured destination; never strips wrappers or adds a root twice.</summary>
		public string ValidatePlannedPath(string path)
		{
			string[] parts;
			if (!ManagedContentPath.TryParse(path, out parts)) throw new InvalidDataException("Unsafe managed-content native destination: " + path);
			ManagedContentLayoutRule[] matches = _rules.Where(rule => parts.Length >= 4 &&
				String.Equals(parts[0], rule.TargetFolder, StringComparison.OrdinalIgnoreCase) &&
				parts[1].Length > rule.ComponentPrefix.Length && parts[1].Length <= rule.MaximumComponentNameLength &&
				parts[1].StartsWith(rule.ComponentPrefix, StringComparison.OrdinalIgnoreCase) &&
				String.Equals(parts[2], rule.ContentFolder, StringComparison.OrdinalIgnoreCase)).ToArray();
			if (matches.Length == 0 || _rejectedSuffixes.Any(suffix => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
				throw new InvalidDataException("Path is not a configured managed-content destination: " + path);
			parts[0] = matches[0].TargetFolder;
			return String.Join("\\", parts);
		}

		/// <summary>Matches one bounded literal folder shape without globs, recursion or filesystem inspection.</summary>
		private static bool Matches(ManagedContentLayoutRule rule, string[] parts, int offset)
		{
			if (rule.SourceRoot != null)
			{
				if (offset >= parts.Length || !String.Equals(parts[offset], rule.SourceRoot, StringComparison.OrdinalIgnoreCase)) return false;
				offset++;
			}
			if (rule.Kind == "generated-folder") return offset < parts.Length;
			return parts.Length >= offset + 3 && parts[offset].Length > rule.ComponentPrefix.Length &&
				parts[offset].StartsWith(rule.ComponentPrefix, StringComparison.OrdinalIgnoreCase) &&
				String.Equals(parts[offset + 1], rule.ContentFolder, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>Limits ignored documentation to exact filenames at the archive or selected wrapper root.</summary>
		private bool IsDocumentation(string[] parts, int offset) => parts.Length == offset + 1 && _ignoredNames.Contains(parts[offset]);

		/// <summary>Checks a previously resolved wrapper using Windows path comparison.</summary>
		private static bool StartsWith(string[] parts, string[] prefix) => prefix == null ||
			(parts.Length >= prefix.Length && parts.Take(prefix.Length).SequenceEqual(prefix, StringComparer.OrdinalIgnoreCase));

		/// <summary>Length-prefixes identity inputs so separators in archive titles cannot create aliases.</summary>
		private static void Append(StringBuilder builder, string value) => builder.Append((value == null ? -1 : value.Length).ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);

		/// <summary>Hashes the immutable provider/configuration and exact operation inputs.</summary>
		private static string Hash(string value)
		{
			using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", String.Empty);
		}
	}
}
