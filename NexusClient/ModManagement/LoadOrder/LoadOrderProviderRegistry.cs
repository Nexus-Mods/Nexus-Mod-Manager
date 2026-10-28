using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Nexus.Client.Games;

namespace Nexus.Client.ModManagement.LoadOrder
{
	/// <summary>Package-reader boundary; implementations must be read-only and propagate cancellation.</summary>
	public interface ILoadOrderPackageInspector
	{
		bool IsAvailable { get; }
		/// <summary>Inspects one entry with its bounded sibling inventory, permitting explicit split-package identification.</summary>
		LoadOrderPackageInspection Inspect(LoadOrderPackageFile file, IReadOnlyList<LoadOrderPackageFile> availableFiles, CancellationToken cancellationToken);
	}

	/// <summary>UI-independent, explicitly requested inventory reads; no background scan or write is implied.</summary>
	public interface ILoadOrderInventoryProvider
	{
		LoadOrderCapability Capability { get; }
		LoadOrderInventory ReadInventory(CancellationToken cancellationToken);
	}

	/// <summary>Built-in provider dispatch by capability identifier, never by game name or ModeId.</summary>
	public static class LoadOrderProviderRegistry
	{
		/// <summary>Creates the declared inventory provider without scanning, starting helpers or changing native state.</summary>
		public static ILoadOrderInventoryProvider Create(IGameMode mode)
		{
			if (mode == null) throw new ArgumentNullException(nameof(mode));
			LoadOrderCapability capability = (mode as ILoadOrderCapabilityProvider)?.LoadOrder;
			if (capability == null) return null;
			if (capability.Provider != LoadOrderCapability.LarianProviderId || capability.Revision != 1)
				throw new NotSupportedException("The configured load-order inventory provider is not registered.");
			return new PackageLoadOrderInventoryProvider(capability,
				() => ModDeploymentTargetResolver.GetPhysicalRootPath(mode, ModDeploymentRoot.Data),
				new LarianPackageInspectorClient(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "LoadOrder", "LarianPackageInspector.exe")));
		}
	}

	/// <summary>Combines a flat directory scan with isolated package results, preserving partial successes.</summary>
	public sealed class PackageLoadOrderInventoryProvider : ILoadOrderInventoryProvider
	{
		private readonly Func<string> _packageRoot;
		private readonly ILoadOrderPackageInspector _inspector;

		/// <summary>Accepts an explicit root resolver and reader for production services or isolated fixtures.</summary>
		public PackageLoadOrderInventoryProvider(LoadOrderCapability capability, Func<string> packageRoot, ILoadOrderPackageInspector inspector)
		{
			Capability = capability ?? throw new ArgumentNullException(nameof(capability));
			_packageRoot = packageRoot ?? throw new ArgumentNullException(nameof(packageRoot));
			_inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
		}
		public LoadOrderCapability Capability { get; }

		/// <summary>Reads evidence off the UI thread; does not infer effective order, activation or installation ownership.</summary>
		public LoadOrderInventory ReadInventory(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			LoadOrderPackageDirectoryObservation directory = LoadOrderPackageDirectoryScanner.Scan(_packageRoot(), Capability.DirectoryPolicy, cancellationToken);
			var diagnostics = new List<LoadOrderDiagnostic>(directory.Diagnostics);
			var packages = new List<LoadOrderPackageObservation>();
			bool available = _inspector.IsAvailable;
			bool complete = directory.IsComplete;
			if (!available && directory.Files.Count > 0)
				diagnostics.Add(new LoadOrderDiagnostic(LoadOrderDiagnosticKind.InspectorUnavailable, LoadOrderDiagnosticSeverity.Error, directory.Root));
			foreach (LoadOrderPackageFile file in directory.Files)
			{
				cancellationToken.ThrowIfCancellationRequested();
				LoadOrderPackageInspection inspection;
				bool changed = false;
				try
				{
					inspection = available ? _inspector.Inspect(file, directory.Files, cancellationToken) : Failure(LoadOrderInspectionStatus.Unavailable, null);
					if (inspection == null) throw new InvalidDataException("The package reader returned no result.");
					if (inspection.Status == LoadOrderInspectionStatus.Complete || inspection.Status == LoadOrderInspectionStatus.Partial)
					{
						changed = !ValidateReadFiles(file, directory.Files, inspection);
						if (changed) inspection = Failure(LoadOrderInspectionStatus.Failed, "A package changed during inspection; refresh the inventory.");
					}
				}
				catch (Exception error) when (LoadOrderPackageDirectoryScanner.IsReadError(error) || error is ArgumentException)
				{
					inspection = Failure(LoadOrderInspectionStatus.Failed, error.Message);
				}
				packages.Add(new LoadOrderPackageObservation(file, inspection));
				if (inspection.Status == LoadOrderInspectionStatus.Complete) continue;
				complete = false;
				if (!available) continue;
				LoadOrderDiagnosticKind kind = changed ? LoadOrderDiagnosticKind.PackageChanged :
					inspection.Status == LoadOrderInspectionStatus.Partial ? LoadOrderDiagnosticKind.PackageInspectionIncomplete :
					inspection.Status == LoadOrderInspectionStatus.Unavailable ? LoadOrderDiagnosticKind.InspectorUnavailable : LoadOrderDiagnosticKind.PackageInspectionFailed;
				diagnostics.Add(new LoadOrderDiagnostic(kind, LoadOrderDiagnosticSeverity.Error, file.Path, technicalDetails: inspection.TechnicalDetails));
			}
			foreach (var duplicate in packages.SelectMany(package => package.Inspection.Modules.Select(module => new { package.File.Path, Module = module }))
				.GroupBy(item => item.Module.Metadata.Identity, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
				foreach (var item in duplicate)
					diagnostics.Add(new LoadOrderDiagnostic(LoadOrderDiagnosticKind.DuplicateModuleIdentity, LoadOrderDiagnosticSeverity.Error, item.Path, duplicate.Key));
			return new LoadOrderInventory(directory.Root, packages, complete, diagnostics);
		}

		/// <summary>Checks all reported split-package inputs against observed siblings and current metadata.</summary>
		private static bool ValidateReadFiles(LoadOrderPackageFile file, IReadOnlyList<LoadOrderPackageFile> availableFiles, LoadOrderPackageInspection inspection)
		{
			if (!inspection.ReadFileNames.Contains(file.FileName, StringComparer.OrdinalIgnoreCase))
				throw new InvalidDataException("The package reader did not identify its input file.");
			foreach (string name in inspection.ReadFileNames.Concat(inspection.PrimaryFileName == null ? new string[0] : new[] { inspection.PrimaryFileName }))
			{
				LoadOrderPackageFile expected = availableFiles.SingleOrDefault(candidate => String.Equals(candidate.FileName, name, StringComparison.OrdinalIgnoreCase));
				if (expected == null) throw new InvalidDataException("The package reader referenced an unobserved sibling file.");
				LoadOrderPackageFile current = LoadOrderPackageDirectoryScanner.Observe(expected.Path);
				if (expected.Length != current.Length || expected.LastWriteTimeUtcTicks != current.LastWriteTimeUtcTicks)
					return false;
			}
			return true;
		}

		/// <summary>Represents an unavailable or failed reader without inventing module metadata.</summary>
		private static LoadOrderPackageInspection Failure(LoadOrderInspectionStatus status, string details)
		{
			if (details != null) details = details.Replace('\0', '\uFFFD');
			if (details != null && details.Length > 8192) details = details.Substring(0, 8192);
			return new LoadOrderPackageInspection(status, LoadOrderPackageKind.Unknown, null, LoadOrderContentFeatures.None, null, technicalDetails: details);
		}
	}
}
