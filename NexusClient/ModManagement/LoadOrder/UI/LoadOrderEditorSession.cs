using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.ModManagement.LoadOrder.UI
{
	/// <summary>Detached presentation/edit state; inclusion and physical package activation remain distinct.</summary>
	internal sealed class LoadOrderEditorSession
	{
		private readonly string[] _original;
		private readonly Dictionary<string, string> _names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		/// <summary>Creates an editable draft only when the complete diagnosis permits it.</summary>
		internal LoadOrderEditorSession(LoadOrderAnalysis analysis)
			: this(analysis, false) { }

		/// <summary>Keeps provisional package evidence read-only until authoritative inspection completes.</summary>
		private LoadOrderEditorSession(LoadOrderAnalysis analysis, bool inspecting)
		{
			Analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
			IsInspecting = inspecting;
			_original = analysis.Document.Entries.Where(entry => !entry.IsProtected).Select(entry => entry.Identity).ToArray();
			foreach (NativeLoadOrderEntry entry in analysis.Document.Entries) _names[entry.Identity] = entry.Name;
			LoadOrderModuleInfo[] modules = analysis.Inventory.Packages.SelectMany(package => package.Inspection.Modules).ToArray();
			foreach (LoadOrderModuleInfo module in modules)
			{
				_available.Add(module.Metadata.Identity);
				if (!_names.ContainsKey(module.Metadata.Identity)) _names[module.Metadata.Identity] = module.Metadata.Name;
			}
			foreach (LoadOrderModuleReference reference in modules.SelectMany(module => module.Dependencies.Concat(module.Conflicts)))
				if (!_names.ContainsKey(reference.Identity) && !String.IsNullOrWhiteSpace(reference.Name)) _names[reference.Identity] = reference.Name;
			if (!inspecting && analysis.CanPrepareRepair) Draft = new NativeLoadOrderDraft(analysis.Document, analysis.Inventory);
		}
		/// <summary>Displays saved positions and positive package evidence without diagnosing uninspected packages as missing.</summary>
		internal static LoadOrderEditorSession Preview(LarianLoadOrderDocument document, IEnumerable<LoadOrderPackageObservation> packages = null)
		{
			var inventory = new LoadOrderInventory(null, packages ?? Enumerable.Empty<LoadOrderPackageObservation>(), false, new LoadOrderDiagnostic[0]);
			return new LoadOrderEditorSession(new LoadOrderAnalysis(document, inventory, null, new LoadOrderIssue[0], new LoadOrderRepairStep[0],
				document.Entries.Where(entry => !entry.IsProtected).Select(entry => entry.Identity), false), true);
		}
		internal LoadOrderAnalysis Analysis { get; }
		internal bool IsInspecting { get; }
		internal NativeLoadOrderDraft Draft { get; }
		internal bool IsDirty => Draft != null && !_original.SequenceEqual(Draft.IncludedIdentities);
		internal IReadOnlyList<string> Included => Draft?.IncludedIdentities ?? Array.AsReadOnly(_original);
		/// <summary>Reports observed package availability without rescanning the module inventory for every dependency row.</summary>
		internal bool IsAvailable(string identity) => _available.Contains(identity);

		/// <summary>Produces one list: fixed game records, included modules in order, excluded modules and physical package observations.</summary>
		internal List<LoadOrderModuleRow> Rows()
		{
			var result = new List<LoadOrderModuleRow>();
			var groups = Analysis.Inventory.Packages.SelectMany(package => package.Inspection.Modules.Select(module => new { Package = package, Module = module }))
				.GroupBy(item => LarianLoadOrderDocument.Identity(item.Module.Metadata.Identity)).ToDictionary(group => group.Key, group => group.ToArray());
			var native = Analysis.Document.Entries.ToDictionary(entry => entry.Identity);
			string[] includedIds = Included.ToArray(); var includedSet = new HashSet<string>(includedIds);
			var positions = includedIds.Select((id, index) => new { id, index }).ToDictionary(item => item.id, item => item.index + 1);
			var identities = Analysis.Document.Entries.Where(entry => entry.IsProtected).Select(entry => entry.Identity).Concat(includedIds)
				.Concat(groups.Keys.Except(includedIds).Where(id => !native.ContainsKey(id) || !native[id].IsProtected)
					.OrderBy(id => groups[id][0].Module.Metadata.Name, StringComparer.CurrentCultureIgnoreCase)).Distinct();
			foreach (string id in identities)
			{
				native.TryGetValue(id, out NativeLoadOrderEntry entry);
				LoadOrderModuleInfo module = groups.ContainsKey(id) && groups[id].Length == 1 ? groups[id][0].Module : null;
				LoadOrderPackageObservation[] packages = groups.ContainsKey(id) ? groups[id].Select(item => item.Package).Distinct().ToArray() : new LoadOrderPackageObservation[0];
				bool protectedRecord = entry?.IsProtected == true;
				bool included = protectedRecord || includedSet.Contains(id);
				bool duplicate = groups.ContainsKey(id) && groups[id].Length > 1;
				var row = new LoadOrderModuleRow
				{
					Key = id, Identity = id, Module = module, Packages = packages, Included = included,
					Name = module?.Metadata.Name ?? entry?.Name ?? (groups.ContainsKey(id) ? String.Join(" / ", groups[id].Select(item => item.Module.Metadata.Name).Distinct()) : id),
					Folder = module?.Metadata.Folder ?? entry?.Folder,
					Author = module?.Metadata.Author, Version = module?.Metadata.Version,
					CanToggle = Draft != null && !protectedRecord && !duplicate && (module == null || module.Kind != LoadOrderModuleKind.Campaign) && (entry != null || module?.Kind == LoadOrderModuleKind.AddOn && !Analysis.Document.UsesLegacyModOrder),
					Position = protectedRecord || !included ? (int?)null : positions[id],
					State = protectedRecord ? T("State.Game", "Game-owned") : duplicate ? T("State.Duplicate", "Duplicate UUID") : IsInspecting && packages.Length == 0 ? T("State.Pending", "Awaiting inspection") : included ? T("State.Included", "Included")
						: module?.Kind == LoadOrderModuleKind.Campaign ? T("State.Campaign", "Select campaign in game") : T("State.Excluded", "Excluded"),
					Explanation = protectedRecord ? T("Details.Game", "This game-owned record stays included and cannot be moved.")
						: duplicate ? T("Details.Duplicate", "Multiple packages declare this UUID. Resolve the duplicates through NMM before editing the order.")
						: IsInspecting ? T("Details.Inspecting", "This is the saved inclusion and position. Package details and dependency checks are still loading. Editing is available after inspection completes.")
						: module?.Kind == LoadOrderModuleKind.Campaign ? T("Details.Campaign", "Campaign selection is managed by the game. This page orders add-on modules.")
						: T("Details.Inclusion", "Inclusion changes the game's module list. Scripts or override files in an installed package can remain effective when its module is excluded. Disable or uninstall the mod through NMM to stop its physical effects.")
				};
				LoadOrderContentFeatures features = packages.Aggregate(LoadOrderContentFeatures.None, (value, package) => value | package.Inspection.Features);
				if (module != null) features |= module.Features;
				if (module?.RequiredScriptExtenderVersion != null) features |= LoadOrderContentFeatures.ScriptExtender;
				row.Badges = Badges(features, protectedRecord, duplicate || packages.Any(package => package.Inspection.Status != LoadOrderInspectionStatus.Complete) || !protectedRecord && packages.Length == 0);
				result.Add(row);
			}
			foreach (LoadOrderPackageObservation package in Analysis.Inventory.Packages.Where(package => package.Inspection.Modules.Count == 0))
			{
				bool overrides = package.Inspection.Kind == LoadOrderPackageKind.Override;
				bool continuation = package.Inspection.Kind == LoadOrderPackageKind.Continuation;
				result.Add(new LoadOrderModuleRow
				{
					Key = package.File.Path, Name = package.File.FileName, Packages = new[] { package },
					State = overrides ? T("State.Override", "Always active override") : continuation ? T("State.Part", "Package part") : T("State.Unverified", "Inspection required"),
					Badges = Badges(package.Inspection.Features | (overrides ? LoadOrderContentFeatures.GameFileOverrides : LoadOrderContentFeatures.None), false, package.Inspection.Status != LoadOrderInspectionStatus.Complete),
					Explanation = overrides ? T("Details.Override", "This package overrides game files and is active independently of module order. Disable or uninstall it through NMM to stop its effects.")
						: continuation ? String.Format(T("Details.Part", "This is part of {0}; it is not a separate module to include or reorder."), package.Inspection.PrimaryFileName)
						: T("Details.Unverified", "The package could not be fully classified. Review its inspection findings before changing the order.")
				});
			}
			return result;
		}

		/// <summary>Changes only the detached inclusion state after checking the selected row's permissions.</summary>
		internal void Include(LoadOrderModuleRow row, bool included)
		{
			if (row == null || !row.CanToggle || Draft == null) throw new InvalidOperationException("The selected row cannot change inclusion.");
			Draft.SetIncluded(row.Identity, included);
		}

		/// <summary>Moves by the actual included order, never by a filtered grid row index.</summary>
		internal void Move(LoadOrderModuleRow row, int offset)
		{
			if (row?.Position == null || !row.CanToggle || Draft == null) throw new InvalidOperationException("The selected row cannot move.");
			int position = Included.ToList().IndexOf(row.Identity) + offset;
			if (position < 0 || position >= Included.Count) return;
			Draft.Move(row.Identity, position);
		}

		/// <summary>Places one included module before/after another, compensating for removal of its old position.</summary>
		internal void MoveRelative(LoadOrderModuleRow source, LoadOrderModuleRow target, bool after)
		{
			if (Draft == null || source?.CanToggle != true || target?.CanToggle != true || !source.Position.HasValue || !target.Position.HasValue)
				throw new InvalidOperationException("Only included add-on modules can be reordered.");
			string[] included = Included.ToArray(); int sourceIndex = Array.IndexOf(included, source.Identity), targetIndex = Array.IndexOf(included, target.Identity);
			if (sourceIndex < 0 || targetIndex < 0) throw new InvalidOperationException("The dragged module is no longer included.");
			int position = targetIndex + (after ? 1 : 0); if (sourceIndex < position) position--;
			Draft.Move(source.Identity, position);
		}

		/// <summary>Names issue/dependency subjects using observed mod names while retaining unresolved UUIDs.</summary>
		internal string NameFor(string identity)
		{
			if (String.IsNullOrEmpty(identity)) return String.Empty;
			return _names.TryGetValue(identity, out string name) ? name : identity;
		}

		/// <summary>Produces stable token values; translated captions never become token separators.</summary>
		private static string Badges(LoadOrderContentFeatures features, bool game, bool incomplete)
		{
			var tokens = new List<string>();
			if (game) tokens.Add("Game");
			if ((features & LoadOrderContentFeatures.LuaScripts) != 0) tokens.Add("Lua");
			if ((features & LoadOrderContentFeatures.OsirisScripts) != 0) tokens.Add("Osiris");
			if ((features & LoadOrderContentFeatures.ScriptExtender) != 0) tokens.Add("Extender");
			if ((features & LoadOrderContentFeatures.EmbeddedModFixer) != 0) tokens.Add("Fixer");
			if ((features & LoadOrderContentFeatures.GameFileOverrides) != 0) tokens.Add("Override");
			if (incomplete) tokens.Add("Unverified");
			return String.Join(",", tokens);
		}

		/// <summary>Uses NMM's current language with an English fallback.</summary>
		internal static string T(string key, string fallback) => LanguageManager.Get("LoadOrder.UI." + key, fallback);
	}

	/// <summary>Detached single-list row; null inclusion denotes a physical package rather than an orderable module.</summary>
	internal sealed class LoadOrderModuleRow
	{
		public string Key { get; set; }
		public string Identity { get; set; }
		public int? Position { get; set; }
		public bool? Included { get; set; }
		public string Name { get; set; }
		public string State { get; set; }
		public string Badges { get; set; }
		public string Author { get; set; }
		public string Version { get; set; }
		public string Folder { get; set; }
		internal bool CanToggle { get; set; }
		internal string Explanation { get; set; }
		internal LoadOrderModuleInfo Module { get; set; }
		internal LoadOrderPackageObservation[] Packages { get; set; }

		/// <summary>Provides copyable metadata and explicit directions rather than treating missing flags as proof of script absence.</summary>
		internal string Details()
		{
			var text = new StringBuilder(); text.AppendLine(Explanation); text.AppendLine();
			if (!String.IsNullOrWhiteSpace(Author)) text.AppendLine(String.Format(LoadOrderEditorSession.T("Details.Author", "Author: {0}"), Author));
			if (!String.IsNullOrWhiteSpace(Version)) text.AppendLine(String.Format(LoadOrderEditorSession.T("Details.Version", "Packed version: {0}"), Version));
			if (!String.IsNullOrWhiteSpace(Folder)) text.AppendLine(String.Format(LoadOrderEditorSession.T("Details.Folder", "Module folder: {0}"), Folder));
			if (Module?.RequiredScriptExtenderVersion != null) text.AppendLine(String.Format(LoadOrderEditorSession.T("Details.Extender", "Requires Script Extender version {0} or newer. Verify the runtime installed for this game."), Module.RequiredScriptExtenderVersion));
			else if ((Badges ?? String.Empty).Split(',').Contains("Extender")) text.AppendLine(LoadOrderEditorSession.T("Details.ExtenderUnknown", "This package declares Script Extender support. Verify that the compatible runtime is installed for the game."));
			if ((Badges ?? String.Empty).Split(',').Contains("Fixer")) text.AppendLine(LoadOrderEditorSession.T("Details.Fixer", "Embedded Mod Fixer content was found. Check the author's compatibility guidance for your game version."));
			if (Module?.Metadata.Tags.Count > 0) text.AppendLine(String.Format(LoadOrderEditorSession.T("Details.Tags", "Tags: {0}"), String.Join(", ", Module.Metadata.Tags)));
			if (Identity != null) text.AppendLine(String.Format(LoadOrderEditorSession.T("Details.Identity", "UUID: {0}"), Identity));
			foreach (LoadOrderPackageObservation package in Packages ?? new LoadOrderPackageObservation[0])
			{
				text.AppendLine(); text.AppendLine(package.File.Path);
				if (package.Inspection.Status != LoadOrderInspectionStatus.Complete) text.AppendLine(LoadOrderEditorSession.T("Details.Partial", "Inspection is incomplete. Missing feature badges do not prove that the package contains no scripts."));
				if (!String.IsNullOrWhiteSpace(package.Inspection.TechnicalDetails)) text.AppendLine(package.Inspection.TechnicalDetails);
			}
			if (!String.IsNullOrWhiteSpace(Module?.Metadata.Description)) { text.AppendLine(); text.AppendLine(Module.Metadata.Description); }
			return text.ToString();
		}
	}
}
