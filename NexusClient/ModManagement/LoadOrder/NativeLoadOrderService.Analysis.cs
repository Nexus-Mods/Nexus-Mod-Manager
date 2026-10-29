using System;
using System.Linq;
using System.Threading;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.ModManagement.LoadOrder
{
	/// <summary>Explicitly reviewable repairs bound to exact native preimages, ownership and package evidence.</summary>
	public sealed class NativeLoadOrderRepairReview
	{
		/// <summary>Retains the diagnosis and the existing native publication review without introducing another writer.</summary>
		internal NativeLoadOrderRepairReview(LoadOrderAnalysis analysis, NativeLoadOrderReview publication) { Analysis = analysis; Publication = publication; }
		public LoadOrderAnalysis Analysis { get; }
		public NativeLoadOrderReview Publication { get; }
	}

	public sealed partial class NativeLoadOrderService
	{
		/// <summary>Explicitly inspects packages, native order and current preferences under freshly reloaded native authority.</summary>
		public LoadOrderAnalysis Analyze(string profile, CancellationToken cancellationToken)
		{
			return WithAuthority(authority =>
			{
				cancellationToken.ThrowIfCancellationRequested();
				LarianLoadOrderDocument document = Read(profile);
				LoadOrderInventory inventory = LoadOrderProviderRegistry.Create(_mode).ReadInventory(cancellationToken);
				byte[] preference = LarianOrderPreference.Read(_services.ModManager.InstallationLog, profile);
				LoadOrderAnalysis analysis = LarianLoadOrderAnalyzer.Analyze(document, inventory, LarianOrderPreference.Decode(preference), _capability.BuiltInModuleIdentities);
				NativeLoadOrderRecovery recovery;
				try { recovery = Inspect(Manifest(profile), LoadPending(profile, authority)); }
				catch (Exception error) when (IsRecoveryError(error)) { recovery = new NativeLoadOrderRecovery(NativeLoadOrderRecoveryState.Conflict, error.Message, null); }
				if (recovery.State == NativeLoadOrderRecoveryState.None) return analysis;
				var issue = new LoadOrderIssue(LoadOrderIssueKind.RecoveryRequired, LoadOrderDiagnosticSeverity.Error,
					LanguageManager.Get("LoadOrder.Issues.RecoveryRequired.Message", "An earlier native load-order publication needs recovery."),
					LanguageManager.Get("LoadOrder.Issues.RecoveryRequired.Direction", "Review the retained original and publication state, then explicitly restore, complete or acknowledge recovery before repairing."), path: document.FilePath);
				return new LoadOrderAnalysis(document, inventory, preference, analysis.Issues.Concat(new[] { issue }), new LoadOrderRepairStep[0], analysis.ProposedOrder, false);
			});
		}

		/// <summary>Builds the repair preview without writing or arbitrarily selecting conflicting packages.</summary>
		public NativeLoadOrderRepairReview ReviewRepairs(string profile, LoadOrderAnalysis analysis)
		{
			if (analysis == null) throw new ArgumentNullException(nameof(analysis));
			if (!analysis.HasAutomaticRepairs) throw new InvalidOperationException("There are no safe automatic order repairs in this analysis.");
			var draft = new NativeLoadOrderDraft(analysis.Document, analysis.Inventory);
			foreach (string id in draft.IncludedIdentities.Except(analysis.ProposedOrder).ToArray()) draft.SetIncluded(id, false);
			foreach (string id in analysis.ProposedOrder.Except(draft.IncludedIdentities)) draft.SetIncluded(id, true);
			for (int index = 0; index < analysis.ProposedOrder.Length; index++) draft.Move(analysis.ProposedOrder[index], index);
			foreach (string id in analysis.RefreshMetadata.Where(draft.IncludedIdentities.Contains)) draft.RefreshMetadata(id);
			NativeLoadOrderReview review = Review(profile, draft);
			NativeOrderValueChange preferenceChange = review.Pending.Changes.Single(change => change.Key == LarianOrderPreference.Key(profile));
			if (!Equal(preferenceChange.Before, analysis.Preference)) throw new InvalidOperationException("Order preferences changed after diagnosis. Refresh before repair.");
			LarianOrderPreference previous = LarianOrderPreference.Decode(analysis.Preference);
			string[] excluded = (previous?.Excluded ?? new string[0]).Union(analysis.Document.Entries.Where(entry => !entry.IsProtected).Select(entry => entry.Identity).Except(analysis.ProposedOrder)).ToArray();
			preferenceChange.After = new LarianOrderPreference
			{
				Source = previous?.Source ?? "Manual", Managed = previous?.Managed ?? new string[0], Excluded = excluded,
				Order = analysis.ProposedOrder.Concat((previous?.Order ?? new string[0]).Except(analysis.ProposedOrder).Except(excluded)).ToArray()
			}.Encode();
			return new NativeLoadOrderRepairReview(analysis, review);
		}

		/// <summary>Publishes approved repairs through the existing durable save/recovery protocol; unresolved issues remain visible.</summary>
		public NativeLoadOrderRecovery ApplyRepairs(NativeLoadOrderRepairReview review, bool explicitlyApproved, CancellationToken cancellationToken)
		{
			if (review == null) throw new ArgumentNullException(nameof(review));
			return Save(review.Publication, explicitlyApproved, cancellationToken);
		}
	}
}
