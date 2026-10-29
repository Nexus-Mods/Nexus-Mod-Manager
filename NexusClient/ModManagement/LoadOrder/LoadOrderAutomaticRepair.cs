using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace Nexus.Client.ModManagement.LoadOrder
{
	/// <summary>One complete review for recoverable folder cleanup followed by stale-sensitive native order repairs.</summary>
	public sealed class LoadOrderAutomaticRepairReview
	{
		/// <summary>Captures both phases without starting either of them.</summary>
		internal LoadOrderAutomaticRepairReview(LoadOrderFolderRepairReview folder, NativeLoadOrderRepairReview order) { Folder = folder; Order = order; }
		public LoadOrderFolderRepairReview Folder { get; }
		public NativeLoadOrderRepairReview Order { get; }
		public bool HasRepairs => Folder.Items.Count > 0 || Order != null;
	}

	/// <summary>Reports retained content and native publication separately; neither implies that unresolved incompatibilities were fixed.</summary>
	public sealed class LoadOrderAutomaticRepairResult
	{
		/// <summary>Returns evidence for refresh and later recovery UI.</summary>
		internal LoadOrderAutomaticRepairResult(string directory, NativeLoadOrderRecovery order) { RetainedContentDirectory = directory; Order = order; }
		public string RetainedContentDirectory { get; }
		public NativeLoadOrderRecovery Order { get; }
	}

	/// <summary>Exposes completed cleanup evidence if a subsequent order publication fails or is cancelled.</summary>
	public sealed class LoadOrderAutomaticRepairException : IOException
	{
		/// <summary>Retains the recovery location rather than attempting an unsafe compensation across independent journals.</summary>
		internal LoadOrderAutomaticRepairException(string directory, Exception inner)
			: base("Folder content was retained outside the package folder, but the subsequent load-order repair did not complete. Refresh and inspect native recovery before continuing.", inner)
		{ RetainedContentDirectory = directory; }
		public string RetainedContentDirectory { get; }
	}

	public sealed partial class NativeLoadOrderService
	{
		/// <summary>Prepares all safe changes for one Fix action, including sorting against the expected post-cleanup package inventory.</summary>
		public LoadOrderAutomaticRepairReview ReviewAutomaticRepairs(string profile, LoadOrderAnalysis analysis, CancellationToken cancellationToken)
		{
			LoadOrderFolderRepairReview folder = ReviewFolderRepairs(profile, analysis, cancellationToken);
			string[] covered = folder.Items.Select(item => Path.Combine(analysis.Inventory.PackageRoot, item.Name)).ToArray();
			var inventory = new LoadOrderInventory(analysis.Inventory.PackageRoot, analysis.Inventory.Packages, analysis.Inventory.IsComplete,
				analysis.Inventory.Diagnostics.Where(issue => issue.Path == null || !covered.Any(path => SamePath(path, issue.Path)) ||
					issue.Kind != LoadOrderDiagnosticKind.UnexpectedFile && issue.Kind != LoadOrderDiagnosticKind.UnexpectedDirectory));
			LoadOrderAnalysis afterCleanup = LarianLoadOrderAnalyzer.Analyze(analysis.Document, inventory, LarianOrderPreference.Decode(analysis.Preference), _capability.BuiltInModuleIdentities);
			NativeLoadOrderRepairReview order = afterCleanup.HasAutomaticRepairs ? ReviewRepairs(profile, afterCleanup, cancellationToken) : null;
			return new LoadOrderAutomaticRepairReview(folder, order);
		}

		/// <summary>Applies the explicitly accepted review; native publication re-inspects the actual post-cleanup inventory before saving.</summary>
		public LoadOrderAutomaticRepairResult ApplyAutomaticRepairs(LoadOrderAutomaticRepairReview review, bool explicitlyApproved, CancellationToken cancellationToken)
		{
			if (review == null) throw new ArgumentNullException(nameof(review));
			if (!explicitlyApproved) throw new InvalidOperationException("The automatic repair review requires explicit approval.");
			string retained = null;
			try
			{
				if (review.Folder.Items.Count > 0) retained = ApplyFolderRepairs(review.Folder, true, cancellationToken);
				NativeLoadOrderRecovery order = review.Order == null ? null : ApplyRepairs(review.Order, true, cancellationToken);
				return new LoadOrderAutomaticRepairResult(retained, order);
			}
			catch (Exception error) when (retained != null)
			{ throw new LoadOrderAutomaticRepairException(retained, error); }
		}
	}
}
