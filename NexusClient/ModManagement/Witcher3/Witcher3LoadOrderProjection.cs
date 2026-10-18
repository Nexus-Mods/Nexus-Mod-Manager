using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Nexus.Client.Games.Witcher3;

namespace Nexus.Client.ModManagement.Witcher3
{
	/// <summary>What was OBSERVED, not proof that a specific game build applied the settings.</summary>
	public enum Witcher3OrderConfigurationMode
	{
		DefaultNoModOverrides = 0,
		ExternalConfigurationPresent = 1,
		SettingsUnavailable = 2
	}

	public enum Witcher3OrderEffectEvidence
	{
		UnknownEdition = 0,
		LegacyDocumentedNotRuntimeVerified = 1,
		RemasteredNotVerified = 2
	}

	public enum Witcher3LoadOrderValueSource
	{
		ConfiguredPriority = 0,
		ConfiguredButAmbiguous = 1,
		NotConfigured = 2,
		SettingsUnavailable = 3
	}

	public enum Witcher3LoadOrderOrigin
	{
		NativeManaged = 0,
		ExternalObserved = 1,
		MixedNativeAndExternal = 2,
		GeneratedMergeCandidate = 3,
		Undetermined = 4
	}

	public enum Witcher3LoadOrderPresence
	{
		Observed = 0,
		MissingFromCompleteInventory = 1,
		NotObservedOrInventoryIncomplete = 2
	}

	public enum Witcher3LoadOrderIssueKind
	{
		NativeInventoryHasIssues = 0,
		SettingsHaveIssues = 1,
		SettingsCouldNotBeRead = 2,
		UnmatchedConfiguredComponent = 3,
		ConfiguredComponentMissing = 4,
		DuplicateNativeComponent = 5,
		SettingsFolderNameCaseDiffers = 6
	}

	public sealed class Witcher3LoadOrderIssue
	{
		internal Witcher3LoadOrderIssue(Witcher3LoadOrderIssueKind kind, string componentPath, string message)
		{
			Kind = kind;
			ComponentPath = componentPath;
			Message = message;
		}

		public Witcher3LoadOrderIssueKind Kind { get; }
		public string ComponentPath { get; }
		public string Message { get; }
	}

	public sealed class Witcher3LoadOrderRow
	{
		internal Witcher3LoadOrderRow(string folderName, Witcher3NativeComponent component,
			IEnumerable<Witcher3ModSettingsSection> sections, Witcher3LoadOrderOrigin origin,
			Witcher3LoadOrderPresence presence, Witcher3LoadOrderValueSource source,
			int? priority, bool? enabled)
		{
			FolderName = folderName;
			GameRelativeFolderPath = component?.GameRelativeFolderPath ?? @"Mods\" + folderName;
			Component = component;
			SettingsSections = new ReadOnlyCollection<Witcher3ModSettingsSection>(sections.ToList());
			Origin = origin;
			Presence = presence;
			ValueSource = source;
			ConfiguredPriority = priority;
			ConfiguredEnabled = enabled;
		}

		public string FolderName { get; }
		public string GameRelativeFolderPath { get; }
		/// <summary>Null if there is no indexed component, even if a matching mods.settings section exists.</summary>
		public Witcher3NativeComponent Component { get; }
		public ReadOnlyCollection<Witcher3ModSettingsSection> SettingsSections { get; }
		public bool IsListedInSettings => SettingsSections.Count > 0;
		public Witcher3LoadOrderOrigin Origin { get; }
		public Witcher3LoadOrderPresence Presence { get; }
		public Witcher3LoadOrderValueSource ValueSource { get; }
		/// <summary>Configured in the external file. Null NEVER means last in the game's effective order.</summary>
		public int? ConfiguredPriority { get; }
		/// <summary>Unknown for missing/invalid/unreadable settings; not the NMM archive activation state.</summary>
		public bool? ConfiguredEnabled { get; }
		public IReadOnlyList<Witcher3ComponentNativeOwner> NativeContributors =>
			Component == null ? (IReadOnlyList<Witcher3ComponentNativeOwner>)new Witcher3ComponentNativeOwner[0] : Component.Owners;
	}

	/// <summary>
	/// Read-only joined view of externally configured mod folders and NMM's native component index.
	/// Rows are sorted for DISPLAY only. They are not a proven game-effective order, a desired NMM
	/// order, or an authorization to rewrite mods.settings or touch any native file.
	/// </summary>
	public sealed class Witcher3LoadOrderProjection
	{
		internal Witcher3LoadOrderProjection(Witcher3NativeComponentIndex index,
			Witcher3ModSettingsDocument document, Witcher3ModSettingsReadStatus readStatus,
			Witcher3OrderConfigurationMode mode, Witcher3OrderEffectEvidence effect,
			IEnumerable<Witcher3LoadOrderRow> rows, IEnumerable<Witcher3LoadOrderIssue> issues)
		{
			NativeIndex = index;
			SettingsDocument = document;
			SettingsReadStatus = readStatus;
			ConfigurationMode = mode;
			EffectEvidence = effect;
			Rows = new ReadOnlyCollection<Witcher3LoadOrderRow>(rows.ToList());
			Issues = new ReadOnlyCollection<Witcher3LoadOrderIssue>(issues.ToList());
		}

		public Witcher3NativeComponentIndex NativeIndex { get; }
		public Witcher3ModSettingsDocument SettingsDocument { get; }
		public Witcher3ModSettingsReadStatus SettingsReadStatus { get; }
		public Witcher3OrderConfigurationMode ConfigurationMode { get; }
		public Witcher3OrderEffectEvidence EffectEvidence { get; }
		public long NativeDeploymentCommitSequence => NativeIndex.NativeDeploymentCommitSequence;
		public ReadOnlyCollection<Witcher3LoadOrderRow> Rows { get; }
		public ReadOnlyCollection<Witcher3LoadOrderIssue> Issues { get; }
		/// <summary>No W3R.4 code can claim NMM owns an external file or validated Remastered ordering.</summary>
		public bool IsNmmManagedOrder => false;
		public bool IsGameEffectiveOrderRuntimeVerified => false;
		public bool AuthorizesModifyingSettings => false;
	}

	public static class Witcher3LoadOrderProjector
	{
		private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

		private sealed class PendingRow
		{
			internal string Name;
			internal Witcher3NativeComponent Component;
			internal readonly List<Witcher3ModSettingsSection> Settings = new List<Witcher3ModSettingsSection>();
		}

		/// <summary>Pure overload for fixtures and snapshots already captured by a caller.</summary>
		public static Witcher3LoadOrderProjection Project(Witcher3NativeComponentIndex index,
			Witcher3ModSettingsDocument document, Witcher3Edition edition = Witcher3Edition.Ambiguous)
		{
			if (document == null) throw new ArgumentNullException(nameof(document));
			return ProjectCore(index, document, Witcher3ModSettingsReadStatus.Loaded, edition);
		}

		/// <summary>Integration with the bounded file reader. Missing != unreadable != empty.</summary>
		public static Witcher3LoadOrderProjection Project(Witcher3NativeComponentIndex index,
			Witcher3ModSettingsFileReadResult read, Witcher3Edition edition = Witcher3Edition.Ambiguous)
		{
			if (read == null) throw new ArgumentNullException(nameof(read));
			return ProjectCore(index, read.Document, read.Status, edition);
		}

		private static Witcher3LoadOrderProjection ProjectCore(Witcher3NativeComponentIndex index,
			Witcher3ModSettingsDocument document, Witcher3ModSettingsReadStatus status, Witcher3Edition edition)
		{
			if (index == null) throw new ArgumentNullException(nameof(index));
			if (status == Witcher3ModSettingsReadStatus.Loaded && document == null)
				throw new ArgumentException("Loaded settings must include a parsed document.", nameof(document));
			var issues = new List<Witcher3LoadOrderIssue>();
			if (index.Issues.Count > 0)
				issues.Add(new Witcher3LoadOrderIssue(Witcher3LoadOrderIssueKind.NativeInventoryHasIssues,
					null, "Native ownership or disk inventory reports uncertainties; do not infer deployed winners."));
			if (status == Witcher3ModSettingsReadStatus.Loaded && !document.IsUnambiguous)
				issues.Add(new Witcher3LoadOrderIssue(Witcher3LoadOrderIssueKind.SettingsHaveIssues,
					null, "mods.settings has ambiguous syntax or values; all numeric priorities are diagnostic."));
			if (status != Witcher3ModSettingsReadStatus.Loaded && status != Witcher3ModSettingsReadStatus.Missing)
				issues.Add(new Witcher3LoadOrderIssue(Witcher3LoadOrderIssueKind.SettingsCouldNotBeRead,
					null, "mods.settings could not be read reliably; absence of overrides cannot be inferred."));

			bool loaded = status == Witcher3ModSettingsReadStatus.Loaded && document.IsComplete;
			var pending = new Dictionary<string, PendingRow>(Names);
			foreach (var group in index.Components.Where(x => x.IsModLoadOrderCandidate)
				.GroupBy(x => x.FolderName, Names))
			{
				var members = group.OrderBy(x => x.GameRelativeFolderPath, StringComparer.Ordinal).ToList();
				if (members.Count > 1)
					issues.Add(new Witcher3LoadOrderIssue(Witcher3LoadOrderIssueKind.DuplicateNativeComponent,
						members[0].GameRelativeFolderPath, "More than one native component resolves to the same mod folder."));
				pending.Add(group.Key, new PendingRow { Name = members[0].FolderName,
					Component = members.Count == 1 ? members[0] : null });
			}
			if (loaded)
			{
				foreach (Witcher3ModSettingsSection section in document.ModSections)
				{
					PendingRow row;
					if (!pending.TryGetValue(section.Name, out row))
					{
						row = new PendingRow { Name = section.Name };
						pending.Add(section.Name, row);
						issues.Add(new Witcher3LoadOrderIssue(Witcher3LoadOrderIssueKind.UnmatchedConfiguredComponent,
							@"Mods\" + section.Name,
							"A settings section has no matching native/disk component; it may be external, stale or unobserved."));
					}
					else if (row.Component != null && !String.Equals(row.Name, section.Name, StringComparison.Ordinal))
						issues.Add(new Witcher3LoadOrderIssue(Witcher3LoadOrderIssueKind.SettingsFolderNameCaseDiffers,
							row.Component.GameRelativeFolderPath, "The settings section and installed folder use different casing."));
					row.Settings.Add(section);
				}
			}

			var rows = new List<Witcher3LoadOrderRow>();
			foreach (PendingRow candidate in pending.Values)
			{
				Witcher3LoadOrderPresence presence = Presence(candidate.Component, index.HasCompleteDiskInventory);
				if (candidate.Settings.Count > 0 && presence == Witcher3LoadOrderPresence.MissingFromCompleteInventory)
					issues.Add(new Witcher3LoadOrderIssue(Witcher3LoadOrderIssueKind.ConfiguredComponentMissing,
						@"Mods\" + candidate.Name, "A configured folder is absent from a COMPLETE disk inventory."));
				Witcher3ModSettingsSection section = candidate.Settings.Count == 1 ? candidate.Settings[0] : null;
				int? priority = section?.HasUniqueName == true ? section.Priority : null;
				bool? enabled = section?.HasUniqueName == true ? section.Enabled : null;
				Witcher3LoadOrderValueSource source;
				if (!loaded)
					source = status == Witcher3ModSettingsReadStatus.Missing
						? Witcher3LoadOrderValueSource.NotConfigured : Witcher3LoadOrderValueSource.SettingsUnavailable;
				else if (candidate.Settings.Count == 0)
					source = Witcher3LoadOrderValueSource.NotConfigured;
				else if (section != null && priority.HasValue && document.IsUnambiguous)
					source = Witcher3LoadOrderValueSource.ConfiguredPriority;
				else
					source = Witcher3LoadOrderValueSource.ConfiguredButAmbiguous;
				rows.Add(new Witcher3LoadOrderRow(candidate.Name, candidate.Component,
					candidate.Settings, Origin(candidate.Component), presence, source, priority, enabled));
			}
			// Only presentation sorting; never reinstall archives, synthesize priority or claim 5.x applied this order.
			var ordered = rows.OrderBy(x => x.ValueSource == Witcher3LoadOrderValueSource.ConfiguredPriority ? 0 :
				x.ValueSource == Witcher3LoadOrderValueSource.ConfiguredButAmbiguous ? 1 : 2)
				.ThenBy(x => x.ValueSource == Witcher3LoadOrderValueSource.ConfiguredPriority
					? x.ConfiguredPriority : null)
				.ThenBy(x => x.FolderName, Names)
				.ThenBy(x => x.FolderName, StringComparer.Ordinal);
			Witcher3OrderConfigurationMode mode = !loaded
				? status == Witcher3ModSettingsReadStatus.Missing
					? Witcher3OrderConfigurationMode.DefaultNoModOverrides
					: Witcher3OrderConfigurationMode.SettingsUnavailable
				: document.ModSections.Any(x => x.HasPriorityField || x.HasEnabledField)
					? Witcher3OrderConfigurationMode.ExternalConfigurationPresent
					: Witcher3OrderConfigurationMode.DefaultNoModOverrides;
			Witcher3OrderEffectEvidence effect = edition == Witcher3Edition.Legacy
				? Witcher3OrderEffectEvidence.LegacyDocumentedNotRuntimeVerified
				: edition == Witcher3Edition.Remastered
					? Witcher3OrderEffectEvidence.RemasteredNotVerified
					: Witcher3OrderEffectEvidence.UnknownEdition;
			return new Witcher3LoadOrderProjection(index, document, status,
				mode, effect, ordered, issues);
		}

		private static Witcher3LoadOrderPresence Presence(Witcher3NativeComponent component, bool completeDisk)
		{
			if (component?.ObservedOnDisk == true) return Witcher3LoadOrderPresence.Observed;
			if (component?.ObservedOnDisk == false || (component == null && completeDisk))
				return Witcher3LoadOrderPresence.MissingFromCompleteInventory;
			return Witcher3LoadOrderPresence.NotObservedOrInventoryIncomplete;
		}

		private static Witcher3LoadOrderOrigin Origin(Witcher3NativeComponent component)
		{
			if (component == null) return Witcher3LoadOrderOrigin.Undetermined;
			if (component.IsMergeCandidate) return Witcher3LoadOrderOrigin.GeneratedMergeCandidate;
			if (component.HasMixedManagedAndUnmanagedFiles) return Witcher3LoadOrderOrigin.MixedNativeAndExternal;
			if (component.Owners.Count > 0) return Witcher3LoadOrderOrigin.NativeManaged;
			return component.IsEntirelyExternal ? Witcher3LoadOrderOrigin.ExternalObserved
				: Witcher3LoadOrderOrigin.Undetermined;
		}
	}
}
