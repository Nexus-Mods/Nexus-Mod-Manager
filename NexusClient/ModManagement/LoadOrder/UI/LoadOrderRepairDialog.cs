using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid.Views.Grid;
using Nexus.Client.UI;

namespace Nexus.Client.ModManagement.LoadOrder.UI
{
	/// <summary>Concrete human-readable repair review with all retained paths and inclusion/order changes.</summary>
	internal sealed class LoadOrderRepairDialog : ManagedFontXtraForm
	{
		/// <summary>Shows exact proposed actions without exposing raw XML or treating unresolved problems as repaired.</summary>
		internal LoadOrderRepairDialog(LoadOrderAutomaticRepairReview review, LoadOrderEditorSession session)
		{
			if (review == null || session == null) throw new ArgumentNullException(review == null ? nameof(review) : nameof(session));
			Text = T("FixReviewTitle", "Review automatic fixes"); ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent;
			AutoScaleMode = AutoScaleMode.Font; MinimumSize = new Size(640, 440); ClientSize = new Size(960, 640);
			var root = LoadOrderUiLayout.Table(new[] { 100D }, new[] { 0D, 75D, 25D, 0D }); Controls.Add(root);
			var rows = new List<RepairRow>();
			foreach (LoadOrderFolderRepairItem item in review.Folder.Items)
				rows.Add(new RepairRow { Action = T("FixMove", "Retain outside package folder"), Subject = item.Name,
					Before = Path.Combine(session.Analysis.Inventory.PackageRoot, item.Name), After = Path.Combine(review.Folder.RetainedContentDirectory, item.Name) });
			if (review.Order != null)
			{
				NativeLoadOrderPreview preview = review.Order.Publication.Preview;
				string[] before = preview.Before.Entries.Where(entry => !entry.IsProtected).Select(entry => entry.Identity).ToArray();
				string[] after = preview.After.Entries.Where(entry => !entry.IsProtected).Select(entry => entry.Identity).ToArray();
				foreach (string id in before.Union(after))
				{
					int oldPosition = Array.IndexOf(before, id), newPosition = Array.IndexOf(after, id);
					if (oldPosition == newPosition) continue;
					rows.Add(new RepairRow { Action = oldPosition < 0 ? T("FixInclude", "Include module") : newPosition < 0 ? T("FixExclude", "Exclude module") : T("FixReorder", "Change load position"),
						Subject = session.NameFor(id), Before = oldPosition < 0 ? T("State.Excluded", "Excluded") : (oldPosition + 1).ToString(), After = newPosition < 0 ? T("State.Excluded", "Excluded") : (newPosition + 1).ToString() });
				}
				foreach (LoadOrderRepairStep step in review.Order.Analysis.Repairs.Where(step => step.Kind == LoadOrderRepairKind.RefreshMetadata))
					rows.Add(new RepairRow { Action = step.Description, Subject = session.NameFor(step.ModuleIdentity), Before = T("FixOldMetadata", "Saved module metadata"), After = T("FixNewMetadata", "Inspected package metadata") });
			}
			LoadOrderUiLayout.Put(root, LoadOrderUiLayout.Label(String.Format(T("FixReviewSummary", "{0} proposed changes. Order edits are backed up. Eligible unrelated files are retained outside the package folder; NMM-owned content and packages are not moved. Downloads and compatibility choices remain manual."), rows.Count)), 0);
			GridView view; var grid = LoadOrderControl.CreateGrid(out view, false);
			view.Columns.AddVisible("Action", T("Review.Action", "Action")); view.Columns.AddVisible("Subject", T("Review.Subject", "Mod or item"));
			view.Columns.AddVisible("Before", T("Review.Before", "Current")); view.Columns.AddVisible("After", T("Review.After", "Proposed"));
			grid.DataSource = new BindingList<RepairRow>(rows); LoadOrderUiLayout.Put(root, grid, 1);
			var directions = LoadOrderUiLayout.Memo();
			directions.Text = String.Join(Environment.NewLine + Environment.NewLine, review.Folder.Issues.Select(issue => (issue.Path ?? String.Empty) + Environment.NewLine + issue.Message + Environment.NewLine + issue.Direction));
			if (String.IsNullOrEmpty(directions.Text)) directions.Text = T("FixRemaining", "After applying these fixes, NMM will inspect the order again and show any remaining problems. No conflicting mod is chosen for you and no package content is rewritten.");
			LoadOrderUiLayout.Put(root, directions, 2);
			var buttons = LoadOrderUiLayout.Group(root, 3, new[] { 100D, 0D, 0D });
			var cancel = new SimpleButton { Text = T("CancelReview", "Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
			var apply = new SimpleButton { Text = T("ApplyFixes", "Apply fixes"), AutoSize = true, DialogResult = DialogResult.OK };
			LoadOrderUiLayout.Put(root, cancel, 0, 1, parent: buttons); LoadOrderUiLayout.Put(root, apply, 0, 2, parent: buttons);
			AcceptButton = apply; CancelButton = cancel;
		}

		/// <summary>Uses the current application font and density on every DevExpress editor and grid.</summary>
		internal void ApplyDisplaySettings(DevExpressDisplaySettings settings)
		{ if (settings != null) { Font = settings.Font; DevExpressDisplaySettingsApplier.ApplyToControlTree(this, settings); } }
		/// <summary>Resolves shared localized page text.</summary>
		private static string T(string key, string fallback) => LoadOrderControl.T(key, fallback);
		/// <summary>Detached before/after values for a concrete repair action.</summary>
		private sealed class RepairRow { public string Action { get; set; } public string Subject { get; set; } public string Before { get; set; } public string After { get; set; } }
	}
}
