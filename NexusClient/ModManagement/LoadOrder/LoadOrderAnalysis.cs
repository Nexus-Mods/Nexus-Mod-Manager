using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.ModManagement.LoadOrder
{
	/// <summary>Actionable issue identities shared by the generic page and provider-specific diagnosis.</summary>
	public enum LoadOrderIssueKind
	{
		Inventory, MissingPackage, InactiveDependency, ExcludedDependency, MissingDependency, DependencyVersion,
		DependencyOrder, DeclaredConflict, DependencyCycle, CuratorOrder, CuratorDependencyConflict,
		ScriptExtenderRequirement, EmbeddedModFixer, OverrideAlwaysActive, RecoveryRequired, AnalysisLimit, NativeMetadata,
		SavedExclusionMismatch, CuratorModuleInactive, CuratorModuleUnavailable
	}

	/// <summary>A localizable explanation and direction, separate from technical package evidence.</summary>
	public sealed class LoadOrderIssue
	{
		/// <summary>Captures the finding without implying that every problem has a safe automatic solution.</summary>
		internal LoadOrderIssue(LoadOrderIssueKind kind, LoadOrderDiagnosticSeverity severity, string message, string direction,
			IEnumerable<string> identities = null, string path = null, bool automaticallyRepairable = false)
		{
			Kind = kind; Severity = severity; Message = message; Direction = direction; Path = path;
			ModuleIdentities = Array.AsReadOnly((identities ?? Enumerable.Empty<string>()).ToArray());
			AutomaticallyRepairable = automaticallyRepairable;
		}
		public LoadOrderIssueKind Kind { get; }
		public LoadOrderDiagnosticSeverity Severity { get; }
		public string LocalizationKey => "LoadOrder.Issues." + Kind;
		public string Message { get; }
		public string Direction { get; }
		public string Path { get; }
		public IReadOnlyList<string> ModuleIdentities { get; }
		public bool AutomaticallyRepairable { get; internal set; }
	}

	/// <summary>Concrete changes displayed before a repair is explicitly applied.</summary>
	public enum LoadOrderRepairKind { IncludeDependency, RemoveMissingRecord, Sort, RestoreCuratorOrder, RemoveExcludedRecord, RefreshMetadata, IncludeCuratorModule }

	/// <summary>One detached inclusion or ordering change; package bytes are never edited.</summary>
	public sealed class LoadOrderRepairStep
	{
		/// <summary>Captures a localizable proposal rather than executing an implicit fix.</summary>
		internal LoadOrderRepairStep(LoadOrderRepairKind kind, string identity = null) { Kind = kind; ModuleIdentity = identity; }
		public LoadOrderRepairKind Kind { get; }
		public string ModuleIdentity { get; }
		public string Description => LanguageManager.Get("LoadOrder.Repairs." + Kind, Kind == LoadOrderRepairKind.IncludeDependency ? "Include the installed dependency."
			: Kind == LoadOrderRepairKind.IncludeCuratorModule ? "Include the installed module selected by the curator."
			: Kind == LoadOrderRepairKind.RemoveMissingRecord ? "Remove the stale module record; no package is deleted."
			: Kind == LoadOrderRepairKind.RemoveExcludedRecord ? "Restore the saved module exclusion; no package is deleted."
			: Kind == LoadOrderRepairKind.RefreshMetadata ? "Refresh the native module metadata from its inspected package."
			: Kind == LoadOrderRepairKind.RestoreCuratorOrder ? "Restore the curator's relative order." : "Place dependencies before their dependents, preserving existing order where possible.");
	}

	/// <summary>Read-only diagnosis and deterministic repair proposal bound to one manifest, inventory and preference observation.</summary>
	public sealed class LoadOrderAnalysis
	{
		internal readonly byte[] Preference;
		internal readonly string[] ProposedOrder;
		internal readonly string[] RefreshMetadata;
		/// <summary>Freezes all evidence and proposed changes for a later stale-sensitive review.</summary>
		internal LoadOrderAnalysis(LarianLoadOrderDocument document, LoadOrderInventory inventory, byte[] preference,
			IEnumerable<LoadOrderIssue> issues, IEnumerable<LoadOrderRepairStep> steps, IEnumerable<string> proposedOrder, bool canPrepareRepair)
		{
			Document = document; Inventory = inventory; Preference = preference == null ? null : (byte[])preference.Clone();
			Issues = Array.AsReadOnly(issues.ToArray()); Repairs = Array.AsReadOnly(steps.ToArray());
			ProposedOrder = proposedOrder.ToArray(); CanPrepareRepair = canPrepareRepair;
			RefreshMetadata = Repairs.Where(step => step.Kind == LoadOrderRepairKind.RefreshMetadata).Select(step => step.ModuleIdentity).ToArray();
		}
		public LarianLoadOrderDocument Document { get; }
		public LoadOrderInventory Inventory { get; }
		public IReadOnlyList<LoadOrderIssue> Issues { get; }
		public IReadOnlyList<LoadOrderRepairStep> Repairs { get; }
		public bool CanPrepareRepair { get; }
		public bool HasAutomaticRepairs => CanPrepareRepair && Repairs.Count > 0;
		public IReadOnlyList<string> SuggestedOrder => Array.AsReadOnly((string[])ProposedOrder.Clone());
	}
}
