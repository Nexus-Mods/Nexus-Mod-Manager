using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Nexus.Client.ModManagement.LoadOrder
{
	/// <summary>Exports detached scan evidence without rescanning packages or changing native order and ownership.</summary>
	internal static class LoadOrderDiagnosticReport
	{
		/// <summary>Preserves raw versions, inspection failures, relationship resolution and compared native fields in a versioned report.</summary>
		internal static string Create(LoadOrderAnalysis analysis, LoadOrderCapability capability, string modeId, string profile,
			IEnumerable<string> displayedOrder, bool inspectionFinished, bool observationCurrent, DateTime observedAtUtc, string lastError, NativeLoadOrderRecovery recovery = null)
		{
			if (analysis == null || capability == null) throw new ArgumentNullException(analysis == null ? nameof(analysis) : nameof(capability));
			string[] order = displayedOrder.ToArray();
			var included = new HashSet<string>(order, StringComparer.OrdinalIgnoreCase);
			var gameProvided = new HashSet<string>(capability.BuiltInModuleIdentities, StringComparer.OrdinalIgnoreCase);
			gameProvided.UnionWith(analysis.Document.Entries.Where(entry => entry.IsProtected).Select(entry => entry.Identity));
			var modules = analysis.Inventory.Packages.SelectMany(package => package.Inspection.Modules)
				.GroupBy(module => module.Metadata.Identity, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
			var report = new
			{
				SchemaVersion = 1,
				GeneratedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
				ObservedAtUtc = observedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
				ApplicationVersion = typeof(LoadOrderDiagnosticReport).Assembly.GetName().Version.ToString(),
				GameModeId = modeId, GameProfile = profile, capability.Provider, capability.Revision,
				InspectionFinished = inspectionFinished, ObservationCurrent = observationCurrent, LastOperationError = lastError,
				Recovery = recovery == null ? null : new { State = recovery.State.ToString(), recovery.Diagnostic, recovery.BackupPath },
				Scope = "The configured package folder and saved module metadata only. The game installation, DLC entitlement, loaded runtime and save-game compatibility are not verified. Inspection failures are not proof of game-invalid packages. Local file paths are included.",
				InspectorExecutable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "LoadOrder", "LarianPackageInspector.exe"),
				Summary = new
				{
					analysis.Inventory.IsComplete, analysis.CanPrepareRepair,
					Errors = analysis.Issues.Count(issue => issue.Severity == LoadOrderDiagnosticSeverity.Error),
					Warnings = analysis.Issues.Count(issue => issue.Severity == LoadOrderDiagnosticSeverity.Warning),
					FindingsByKind = analysis.Issues.GroupBy(issue => issue.Kind).ToDictionary(group => group.Key.ToString(), group => group.Count()),
					PackageInspectionsByStatus = analysis.Inventory.Packages.GroupBy(package => package.Inspection.Status).ToDictionary(group => group.Key.ToString(), group => group.Count())
				},
				GameProvidedModuleIdentities = gameProvided.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray(),
				Manifest = new
				{
					analysis.Document.FilePath, analysis.Document.Sha256, analysis.Document.UsesLegacyModOrder,
					Entries = analysis.Document.Entries.Select(entry => new
					{
						entry.Identity, entry.Name, entry.Folder, entry.IsProtected,
						Version = LarianLoadOrderDocument.Field(analysis.Document.Records[entry.Identity], "Version64"),
						RecordXml = analysis.Document.Records[entry.Identity].OuterXml
					}).ToArray()
				},
				DisplayedIncludedOrder = order,
				SavedPolicy = LarianOrderPreference.Decode(analysis.Preference),
				PackageRoot = analysis.Inventory.PackageRoot,
				Packages = analysis.Inventory.Packages.Select(package => new
				{
					package.File.Path, package.File.Length, LastWriteTimeUtcTicks = package.File.LastWriteTimeUtcTicks.ToString(CultureInfo.InvariantCulture),
					Status = package.Inspection.Status.ToString(), Kind = package.Inspection.Kind.ToString(), Features = package.Inspection.Features.ToString(),
					package.Inspection.ReadFileNames, package.Inspection.PrimaryFileName, package.Inspection.TechnicalDetails,
					Modules = package.Inspection.Modules.Select(module => new
					{
						module.Metadata, Kind = module.Kind.ToString(), Features = module.Features.ToString(), module.RequiredScriptExtenderVersion,
						Dependencies = module.Dependencies.Select(reference => new
						{
							reference.Identity, reference.Name, reference.Version,
							Resolution = gameProvided.Contains(reference.Identity) ? "GameProvidedByConfiguration"
								: !modules.TryGetValue(reference.Identity, out int count) ? analysis.Inventory.IsComplete ? "NotFoundInPackageFolder" : "UnverifiedIncompleteInspection"
								: count > 1 ? "Ambiguous" : included.Contains(reference.Identity) ? "InstalledIncluded" : "InstalledExcluded"
						}).ToArray(),
						module.Conflicts
					}).ToArray()
				}).ToArray(),
				InventoryDiagnostics = analysis.Inventory.Diagnostics.Select(diagnostic => new
				{
					Kind = diagnostic.Kind.ToString(), SourceSeverity = diagnostic.Severity.ToString(), diagnostic.Path, diagnostic.ModuleIdentity, diagnostic.TechnicalDetails
				}).ToArray(),
				Issues = analysis.Issues.Select(issue => new
				{
					Kind = issue.Kind.ToString(), Severity = issue.Severity.ToString(), issue.Message, issue.Direction,
					issue.ModuleIdentities, issue.Path, issue.TechnicalDetails, issue.AutomaticallyRepairable
				}).ToArray(),
				ProposedRepairs = analysis.Repairs.Select(repair => new { Kind = repair.Kind.ToString(), repair.ModuleIdentity, repair.Description }).ToArray(),
				SuggestedOrder = analysis.SuggestedOrder
			};
			return JsonConvert.SerializeObject(report, Formatting.Indented, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None });
		}
	}
}
