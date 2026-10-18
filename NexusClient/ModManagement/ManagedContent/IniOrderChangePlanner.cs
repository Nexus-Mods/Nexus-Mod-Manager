using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Nexus.Client.ModManagement.ManagedContent
{
	/// <summary>The requested game-folder position and, optionally, a new game-enabled flag.</summary>
	public sealed class IniOrderEntry
	{
		/// <summary>Captures one requested component and an optional explicit enabled edit.</summary>
		public IniOrderEntry(string folderName, bool? enabled = null)
		{
			FolderName = folderName;
			Enabled = enabled;
		}

		public string FolderName { get; }
		/// <summary>Null preserves the source Enabled key, including its absence.</summary>
		public bool? Enabled { get; }
	}

	/// <summary>Outcomes of pure order-file planning.</summary>
	public enum IniOrderChangePlanStatus
	{
		Blocked = 0,
		Unchanged = 1,
		PreviewAvailable = 2
	}

	/// <summary>The explicitly requested class of order-file change.</summary>
	public enum IniOrderChangeKind
	{
		ManagedOrder = 0,
		RestoreGameDefaults = 1
	}

	/// <summary>A semantic delta for one configured game component.</summary>
	public sealed class IniOrderChange
	{
		/// <summary>Captures the previous and proposed semantic values.</summary>
		internal IniOrderChange(string folderName, int? previousPriority,
			int? proposedPriority, bool? previousEnabled, bool? proposedEnabled)
		{
			FolderName = folderName;
			PreviousPriority = previousPriority;
			ProposedPriority = proposedPriority;
			PreviousEnabled = previousEnabled;
			ProposedEnabled = proposedEnabled;
		}

		public string FolderName { get; }
		public int? PreviousPriority { get; }
		public int? ProposedPriority { get; }
		public bool? PreviousEnabled { get; }
		public bool? ProposedEnabled { get; }
	}

	/// <summary>
	/// Read-only, immutable review result. Hashes include the original BOM and all bytes.
	/// No caller is authorized to write the order file merely because a preview is available.
	/// </summary>
	public sealed class IniOrderChangePreview
	{
		/// <summary>Freezes exact source/proposed identities and the interpreted format.</summary>
		internal IniOrderChangePreview(IniOrderChangePlanStatus status,
			string diagnostic, IniOrderFileObservation observed, string proposedText,
			long? proposedByteLength, string proposedSha256,
			IEnumerable<IniOrderChange> changes,
			IniOrderChangeKind kind = IniOrderChangeKind.ManagedOrder,
			IniOrderFormat missingFormat = null, IniOrderTextEncoding? newFileEncoding = null)
		{
			Format = observed.Document?.Format ?? missingFormat;
			Status = status;
			Kind = kind;
			Diagnostic = diagnostic;
			FilePath = observed.FilePath;
			ObservedStatus = observed.Status;
			ObservedByteLength = observed.ByteLength;
			ObservedSha256 = observed.ContentSha256;
			TextEncoding = observed.Encoding ?? newFileEncoding;
			ProposedText = proposedText;
			ProposedByteLength = proposedByteLength;
			ProposedSha256 = proposedSha256;
			Changes = new ReadOnlyCollection<IniOrderChange>(changes.ToList());
		}

		public IniOrderFormat Format { get; }
		public IniOrderChangePlanStatus Status { get; }
		public IniOrderChangeKind Kind { get; }
		public string Diagnostic { get; }
		public string FilePath { get; }
		public IniOrderReadStatus ObservedStatus { get; }
		public long? ObservedByteLength { get; }
		public string ObservedSha256 { get; }
		public IniOrderTextEncoding? TextEncoding { get; }
		/// <summary>Null when blocked; otherwise exact replacement text in the original encoding.</summary>
		public string ProposedText { get; }
		public long? ProposedByteLength { get; }
		public string ProposedSha256 { get; }
		public ReadOnlyCollection<IniOrderChange> Changes { get; }
		/// <summary>A preview alone never authorizes file mutation.</summary>
		public bool AuthorizesFileWrite => false;
	}

	/// <summary>Pure lossless sequential-order and override-removal previews; publication requires independent authorization.</summary>
	public static class IniOrderChangePlanner
	{
		public const int MaxGamePriority = 9999;
		private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;


		/// <summary>
		/// Reorders all and only already configured component sections.
		/// Callers cannot bypass the explicit physical-folder selection boundary here.
		/// </summary>
		public static IniOrderChangePreview Prepare(
			IniOrderFileObservation observed,
			IEnumerable<IniOrderEntry> desiredOrder, bool explicitlyRequestedManagedOrder)
		{
			return PrepareWithNewFolders(observed, desiredOrder, explicitlyRequestedManagedOrder,
				Enumerable.Empty<string>());
		}

		/// <summary>
		/// Extends managed ordering with explicit additions from a complete physical inventory.
		/// allowedNewFolders is NOT a request to add everything: each selected folder must also
		/// occur exactly once in desiredOrder. The UI separately refreshes that inventory.
		/// </summary>
		public static IniOrderChangePreview PrepareWithNewFolders(
			IniOrderFileObservation observed,
			IEnumerable<IniOrderEntry> desiredOrder, bool explicitlyRequestedManagedOrder,
			IEnumerable<string> allowedNewFolders, IniOrderFormat missingFormat = null,
			bool preserveExistingPriorities = false)
		{
			if (observed == null) throw new ArgumentNullException(nameof(observed));
			if (desiredOrder == null) throw new ArgumentNullException(nameof(desiredOrder));
			if (allowedNewFolders == null) throw new ArgumentNullException(nameof(allowedNewFolders));
			if (observed.Status == IniOrderReadStatus.Missing && explicitlyRequestedManagedOrder)
				return PrepareNewFile(observed, desiredOrder, allowedNewFolders, missingFormat);
			if (!explicitlyRequestedManagedOrder)
				return Blocked(observed, "Managed ordering must be explicitly requested.");
			string original;
			string problem = ValidatePreimage(observed, out original);
			if (problem != null) return Blocked(observed, problem);

			var sections = observed.Document.ComponentSections.ToList();
			var requested = desiredOrder.Take(MaxGamePriority + 1).ToList();
			if (requested.Count == 0 || requested.Count > MaxGamePriority ||
				requested.Count < sections.Count)
				return Blocked(observed, "The request must contain 1-9999 folders and all configured component sections.");
			if (requested.Any(x => x == null || !observed.Document.Format.IsComponentName(x.FolderName)) ||
				requested.Select(x => x.FolderName).Distinct(Names).Count() != requested.Count)
				return Blocked(observed, "The request contains null, unsafe or duplicate component names.");

			var existing = sections.ToDictionary(x => x.Name, Names);
			var permitted = new HashSet<string>(Names);
			foreach (string folder in allowedNewFolders)
			{
				if (!observed.Document.Format.IsComponentName(folder) || !permitted.Add(folder))
					return Blocked(observed, "The new-folder allowlist is ambiguous or contains an unsafe name.");
			}
			var desiredNames = new HashSet<string>(requested.Select(x => x.FolderName), Names);
			if (sections.Any(x => !desiredNames.Contains(x.Name)) ||
				requested.Any(x => !existing.ContainsKey(x.FolderName) && !permitted.Contains(x.FolderName)) ||
				(preserveExistingPriorities && (permitted.Count > 0 || requested.Count != sections.Count)))
				return Blocked(observed, "All existing sections must remain; new folders must be explicitly observed and selected.");

			var lines = SplitLines(original);
			var sectionStarts = observed.Document.Sections;
			var nextHeader = new Dictionary<int, int>();
			for (int i = 0; i < sectionStarts.Count; i++)
				nextHeader.Add(sectionStarts[i].LineNumber, i + 1 < sectionStarts.Count
					? sectionStarts[i + 1].LineNumber : lines.Count + 1);
			var replacements = new Dictionary<int, string>();
			var additions = new Dictionary<int, List<string>>();
			var newSections = new List<IniOrderEntry>();
			var changes = new List<IniOrderChange>();
			IniOrderSection firstSection;
			bool keepMergeZero = requested.Count > 0 &&
				Names.Equals(requested[0].FolderName, "mod0000_MergedFiles") &&
				existing.TryGetValue(requested[0].FolderName, out firstSection) && firstSection.Priority == 0;
			for (int i = 0; i < requested.Count; i++)
			{
				IniOrderEntry entry = requested[i];
				IniOrderSection section;
				bool hasExisting = existing.TryGetValue(entry.FolderName, out section);
				int? priority = preserveExistingPriorities && hasExisting
					? section.Priority : (int?)(i + (keepMergeZero ? 0 : 1));
				if (!hasExisting)
				{
					// A newly enlisted component is explicitly game-enabled; null is not
					// interpreted as an implicit game default in a managed section.
					bool enabled = entry.Enabled ?? true;
					newSections.Add(new IniOrderEntry(entry.FolderName, enabled));
					changes.Add(new IniOrderChange(entry.FolderName, null,
						priority, null, enabled));
					continue;
				}
				bool? newEnabled = entry.Enabled ?? section.Enabled;
				if (section.Priority != priority || section.Enabled != newEnabled)
					changes.Add(new IniOrderChange(section.Name, section.Priority,
						priority, section.Enabled, newEnabled));
				if (section.Priority != priority && priority.HasValue)
					SetValue(section, observed.Document.Format.PriorityKey, priority.Value.ToString(CultureInfo.InvariantCulture), lines,
						replacements, additions, nextHeader);
				if (entry.Enabled.HasValue && section.Enabled != newEnabled)
					SetValue(section, observed.Document.Format.EnabledKey, newEnabled.Value ? "1" : "0", lines,
						replacements, additions, nextHeader);
			}

			string proposed = AppendNewSections(Render(lines, replacements, additions), lines,
					newSections, requested, observed.Document.Format, keepMergeZero);
			IniOrderDocument verified = IniOrderParser.Parse(proposed, observed.Document.Format);
			if (!verified.IsUnambiguous)
				return Blocked(observed, "The proposed text could not be reparsed without ambiguity.");
			var verifiedByName = verified.ComponentSections.ToDictionary(x => x.Name, Names);
			if (verifiedByName.Count != requested.Count || requested.Where((x, i) =>
				!verifiedByName.ContainsKey(x.FolderName) ||
				verifiedByName[x.FolderName].Priority !=
					(preserveExistingPriorities && existing.ContainsKey(x.FolderName)
						? existing[x.FolderName].Priority : (int?)(i + (keepMergeZero ? 0 : 1))) ||
				(verifiedByName[x.FolderName].Enabled != (existing.ContainsKey(x.FolderName)
					? x.Enabled ?? existing[x.FolderName].Enabled : x.Enabled ?? true))).Any())
				return Blocked(observed, "The proposed settings do not match the complete requested order.");

			return Preview(observed, original, proposed, changes,
				IniOrderChangeKind.ManagedOrder);
		}

		/// <summary>
		/// First-file workflow: a genuinely missing preimage has NO hash or byte length.
		/// Require explicit folder selection and a trusted compiled format; never infer an
		/// existing empty file. The publisher enforces absence again under native authority.
		/// </summary>
		private static IniOrderChangePreview PrepareNewFile(IniOrderFileObservation observed,
			IEnumerable<IniOrderEntry> desiredOrder, IEnumerable<string> allowedNewFolders,
			IniOrderFormat format)
		{
			if (format == null || observed.Document != null || observed.Encoding.HasValue ||
				observed.ByteLength.HasValue || observed.ContentSha256 != null)
				return Blocked(observed, "An exact missing-file observation and a declared format are required.");
			var requested = desiredOrder.Take(MaxGamePriority + 1).ToList();
			if (requested.Count < 1 || requested.Count > MaxGamePriority ||
				requested.Any(entry => entry == null || !format.IsComponentName(entry.FolderName)) ||
				requested.Select(entry => entry.FolderName).Distinct(Names).Count() != requested.Count)
				return Blocked(observed, "Select 1-9999 distinct safe folders to create the order file.");
			var allowed = allowedNewFolders.ToList();
			if (allowed.Count != requested.Count || allowed.Any(name => !format.IsComponentName(name)) ||
				allowed.Distinct(Names).Count() != allowed.Count ||
				!new HashSet<string>(allowed, Names).SetEquals(requested.Select(entry => entry.FolderName)))
				return Blocked(observed, "Every newly configured folder must be individually observed and selected.");
			var text = new StringBuilder();
			var changes = new List<IniOrderChange>();
			for (int i = 0; i < requested.Count; i++)
			{
				bool enabled = requested[i].Enabled ?? true;
				text.Append('[').Append(requested[i].FolderName).Append("]\r\n")
					.Append(format.EnabledKey).Append('=').Append(enabled ? '1' : '0').Append("\r\n")
					.Append(format.PriorityKey).Append('=').Append(i + 1).Append("\r\n");
				changes.Add(new IniOrderChange(requested[i].FolderName, null, i + 1, null, enabled));
			}
			string proposed = text.ToString();
			IniOrderDocument parsed = IniOrderParser.Parse(proposed, format);
			if (!parsed.IsUnambiguous || parsed.ComponentSections.Count() != requested.Count)
				return Blocked(observed, "The generated settings failed structural validation.");
			byte[] bytes = Encode(proposed, IniOrderTextEncoding.Utf8);
			if (bytes.Length > IniOrderFileReader.DefaultMaxBytes)
				return Blocked(observed, "The new settings file exceeds the bounded write limit.");
			return new IniOrderChangePreview(IniOrderChangePlanStatus.PreviewAvailable,
				null, observed, proposed, bytes.Length, Hash(bytes), changes,
				IniOrderChangeKind.ManagedOrder, format, IniOrderTextEncoding.Utf8);
		}

		/// <summary>
		/// Removes only the configured priority/enabled keys from component sections, including
		/// external entries. Preserves headers, comments, unknown keys/sections and encoding.
		/// An absent file stays absent; restoration never deletes the file.
		/// </summary>
		public static IniOrderChangePreview PrepareRestoreGameDefaults(
			IniOrderFileObservation observed,
			bool explicitlyRequestedRestore)
		{
			if (observed == null) throw new ArgumentNullException(nameof(observed));
			if (!explicitlyRequestedRestore)
				return Blocked(observed, "Restoring game defaults must be explicitly requested.");
			string original;
			string problem = ValidatePreimage(observed, out original);
			if (problem != null) return Blocked(observed, problem);

			var removals = new HashSet<int>();
			var changes = new List<IniOrderChange>();
			foreach (IniOrderSection section in observed.Document.ComponentSections)
			{
				var fields = section.Values.Where(x => Names.Equals(x.Key, observed.Document.Format.PriorityKey) ||
					Names.Equals(x.Key, observed.Document.Format.EnabledKey)).ToList();
				if (fields.Count == 0) continue;
				foreach (IniOrderValue field in fields) removals.Add(field.LineNumber);
				changes.Add(new IniOrderChange(section.Name, section.Priority,
					null, section.Enabled, null));
			}
			var lines = SplitLines(original);
			string proposed = Render(lines, new Dictionary<int, string>(),
				new Dictionary<int, List<string>>(), removals);
			IniOrderDocument verified = IniOrderParser.Parse(proposed, observed.Document.Format);
			if (!verified.IsUnambiguous ||
				verified.ComponentSections.Count() != observed.Document.ComponentSections.Count() ||
				verified.ComponentSections.Any(x => x.HasEnabledField || x.HasPriorityField))
				return Blocked(observed, "The proposed default-order file failed exact validation.");
			return Preview(observed, original, proposed, changes,
				IniOrderChangeKind.RestoreGameDefaults);
		}

		/// <summary>Requires an existing unambiguous document that reproduces its observed bytes exactly.</summary>
		private static string ValidatePreimage(IniOrderFileObservation observed, out string original)
		{
			original = null;
			if (observed.Status != IniOrderReadStatus.Loaded || observed.Document == null ||
				!observed.Encoding.HasValue || !observed.ByteLength.HasValue ||
				String.IsNullOrEmpty(observed.ContentSha256))
				return "An unchanged, fully decoded existing order-file preimage is required.";
			if (!observed.Document.IsUnambiguous)
				return "The order file has ambiguous or invalid entries; no partial correction is attempted.";
			original = observed.Document.OriginalText;
			byte[] observedBytes = Encode(original, observed.Encoding.Value);
			if (observedBytes.Length != observed.ByteLength.Value ||
				!String.Equals(Hash(observedBytes), observed.ContentSha256, StringComparison.OrdinalIgnoreCase))
				return "The decoded preimage does not reproduce the observed original bytes.";
			return null;
		}

		/// <summary>Hashes the exact proposed bytes in the original encoding.</summary>
		private static IniOrderChangePreview Preview(
			IniOrderFileObservation observed, string original, string proposed,
			IEnumerable<IniOrderChange> changes, IniOrderChangeKind kind)
		{
			byte[] bytes = Encode(proposed, observed.Encoding.Value);
			bool unchanged = String.Equals(proposed, original, StringComparison.Ordinal);
			return new IniOrderChangePreview(unchanged
				? IniOrderChangePlanStatus.Unchanged
				: IniOrderChangePlanStatus.PreviewAvailable,
				null, observed, proposed, bytes.Length, Hash(bytes), changes, kind);
		}

		/// <summary>Appends only explicitly selected new components using the configured literal keys.</summary>
		private static string AppendNewSections(string proposed, IList<IniOrderSourceLine> sourceLines,
			IList<IniOrderEntry> additions,
			IList<IniOrderEntry> requestedOrder, IniOrderFormat format, bool keepMergeZero)
		{
			if (additions.Count == 0) return proposed;
			string newline = PickNewline(sourceLines, sourceLines.Count - 1);
			var byName = additions.ToDictionary(x => x.FolderName, Names);
			var result = new StringBuilder(proposed);
			if (result.Length > 0 && result[result.Length - 1] != '\n' && result[result.Length - 1] != '\r')
				result.Append(newline);
			for (int i = 0; i < requestedOrder.Count; i++)
			{
				IniOrderEntry added;
				if (!byName.TryGetValue(requestedOrder[i].FolderName, out added)) continue;
				result.Append('[').Append(added.FolderName).Append(']').Append(newline);
				result.Append(format.EnabledKey).Append('=').Append(added.Enabled.Value ? '1' : '0').Append(newline);
				result.Append(format.PriorityKey).Append('=').Append(i + (keepMergeZero ? 0 : 1)).Append(newline);
			}
			return result.ToString();
		}

		/// <summary>Replaces a known value or inserts an absent key while preserving surrounding source text.</summary>
		private static void SetValue(IniOrderSection section, string key, string value,
			IList<IniOrderSourceLine> lines, IDictionary<int, string> replacements,
			IDictionary<int, List<string>> additions, IDictionary<int, int> nextHeader)
		{
			IniOrderValue current = section.Values.SingleOrDefault(x =>
				String.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
			if (current != null)
			{
				string original = lines[current.LineNumber - 1].Text;
				replacements.Add(current.LineNumber, IniOrderTextEdits.ReplaceValue(original, value));
				return;
			}

			int nextSection = nextHeader[section.LineNumber];
			int afterLine = nextSection - 1;
			List<string> pending;
			if (!additions.TryGetValue(afterLine, out pending))
			{
				pending = new List<string>();
				additions.Add(afterLine, pending);
			}
			pending.Add(key + "=" + value);
		}

		/// <summary>Uses the shared line splitter without changing the planner's semantic review.</summary>
		private static List<IniOrderSourceLine> SplitLines(string text) => IniOrderTextEdits.SplitLines(text);

		/// <summary>Uses the shared renderer for existing reviewed replacements, insertions and key removals.</summary>
		private static string Render(IList<IniOrderSourceLine> lines, IDictionary<int, string> replacements,
			IDictionary<int, List<string>> additions, ISet<int> removals = null) => IniOrderTextEdits.Render(lines, replacements, additions, removals);

		/// <summary>Preserves the nearest source newline when adding a reviewed setting or section.</summary>
		private static string PickNewline(IList<IniOrderSourceLine> lines, int afterIndex) => IniOrderTextEdits.PickNewline(lines, afterIndex);

		/// <summary>Preserves the publisher's existing exact original-encoding byte contract.</summary>
		internal static byte[] Encode(string text, IniOrderTextEncoding kind) => IniOrderByteCodec.Encode(text, (IniOrderTextEncoding)kind);

		/// <summary>Preserves the planner's original/proposed exact-byte identities.</summary>
		private static string Hash(byte[] bytes) => IniOrderByteCodec.Hash(bytes);

		/// <summary>Produces a non-writable blocked preview without proposing replacement bytes.</summary>
		private static IniOrderChangePreview Blocked(
			IniOrderFileObservation observed, string reason)
		{
			return new IniOrderChangePreview(IniOrderChangePlanStatus.Blocked,
				reason, observed, null, null, null, new IniOrderChange[0]);
		}
	}
}
