using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DevExpress.Utils;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraEditors.Repository;
using Nexus.Client.UI;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.CollectionManagement.UI
{
	/// <summary>Shows every reviewed Collection removal and preservation decision before uninstall begins.</summary>
	internal sealed class CollectionUninstallReviewDialog : ManagedFontXtraForm
	{
		/// <summary>Gets the exact removal list approved in this dialog.</summary>
		internal CollectionUninstallEffectsPlan ReviewedPlan { get; private set; }

		/// <summary>Creates a searchable removal review with named mods and plain-language preservation reasons.</summary>
		internal CollectionUninstallReviewDialog(string collectionName, CollectionUninstallEffectsPlan plan,
			Func<CollectionUninstallNativeImpact, string> subject, Func<CollectionUninstallNativeImpact, string> reason)
		{
			if (plan == null) throw new ArgumentNullException(nameof(plan));
			ReviewedPlan = plan;
			if (subject == null) throw new ArgumentNullException(nameof(subject));
			if (reason == null) throw new ArgumentNullException(nameof(reason));
			DevExpressDisplaySettings settings = DevExpressDisplaySettings.CreateFromSettings(Properties.Settings.Default);
			Font = settings.Font;
			AutoScaleMode = AutoScaleMode.Font;
			Text = LanguageManager.Get("Collections.Management.Removal.ReviewTitle", "Review Collection uninstall");
			StartPosition = FormStartPosition.CenterParent;
			FormBorderStyle = FormBorderStyle.Sizable;
			MinimizeBox = false;
			ShowInTaskbar = false;
			MinimumSize = new Size(720, 420);
			ClientSize = new Size(940, 560);

			LabelControl summary = new LabelControl
			{
				Dock = DockStyle.Top, AutoSizeMode = LabelAutoSizeMode.Vertical, UseMnemonic = false,
				Padding = new Padding(12),
				Text = LanguageManager.Format("Collections.Management.Removal.ReviewSummary",
					"Uninstall '{0}'?\r\n\r\n{1} mods will be uninstalled. {2} mods will be kept.\r\nCheck the list before continuing. Mods marked 'Keep' stay installed.", collectionName,
					plan.Impacts.Count(x => x.RequiresNativeRemoval),
					plan.Impacts.Count(x => !x.RequiresNativeRemoval && x.Disposition != CollectionUninstallNativeDisposition.AlreadyAbsent))
			};
			summary.Appearance.TextOptions.WordWrap = WordWrap.Wrap;
			summary.Appearance.Options.UseTextOptions = true;

			GridControl grid = new GridControl { Dock = DockStyle.Fill };
			GridView view = new GridView(grid);
			grid.MainView = view;
			grid.ViewCollection.Add(view);
			view.OptionsBehavior.Editable = plan.ExplicitMemberRemoval;
			view.OptionsBehavior.EditorShowMode = EditorShowMode.MouseDown;
			view.OptionsView.ShowGroupPanel = false;
			view.OptionsView.ShowIndicator = false;
			view.OptionsFind.AlwaysVisible = true;
			view.OptionsView.RowAutoHeight = true;
			view.Appearance.Row.TextOptions.WordWrap = WordWrap.Wrap;
			view.Appearance.Row.Options.UseTextOptions = true;
			view.Columns.AddVisible(nameof(RemovalRow.ModName), LanguageManager.Get("Collections.Ownership.ModName", "Installed mod")).Width = 290;
			view.Columns.AddVisible(nameof(RemovalRow.Action), LanguageManager.Get("Collections.Management.Removal.Action", "Action")).Width = 110;
			view.Columns.AddVisible(nameof(RemovalRow.Reason), LanguageManager.Get("Collections.Management.Removal.Reason", "Why")).Width = 500;
			var rows = new BindingList<RemovalRow>(plan.Impacts.Select(x => new RemovalRow(x, subject(x),
				x.RequiresNativeRemoval ? LanguageManager.Get("Collections.Management.Removal.Uninstall", "Uninstall") :
					x.Disposition == CollectionUninstallNativeDisposition.AlreadyAbsent ? LanguageManager.Get("Collections.Status.AlreadyAbsent", "Already absent") :
					LanguageManager.Get("Collections.Management.Removal.Keep", "Keep"), reason(x)))
				.OrderBy(x => x.ModName, StringComparer.CurrentCultureIgnoreCase).ToList());
			grid.DataSource = rows;
			if (plan.ExplicitMemberRemoval)
			{
				var check = new RepositoryItemCheckEdit();
				grid.RepositoryItems.Add(check);
				DevExpress.XtraGrid.Columns.GridColumn removeColumn = view.Columns.AddVisible(nameof(RemovalRow.Remove),
					LanguageManager.Get("Collections.Management.Removal.Uninstall", "Uninstall"));
				removeColumn.ColumnEdit = check;
				removeColumn.VisibleIndex = 0;
				removeColumn.Width = 70;
				foreach (DevExpress.XtraGrid.Columns.GridColumn column in view.Columns)
					column.OptionsColumn.AllowEdit = column == removeColumn;
				Action updateSummary = () =>
				{
					foreach (RemovalRow row in rows.Where(x => x.Impact.RequiresNativeRemoval))
						row.Action = row.Remove ? LanguageManager.Get("Collections.Management.Removal.Uninstall", "Uninstall") :
							LanguageManager.Get("Collections.Management.Removal.Keep", "Keep");
					summary.Text = LanguageManager.Format("Collections.Management.Removal.ChooseSummary",
						"Uninstall '{0}'?\r\n\r\n{1} mods selected. Uncheck any mod you want to keep.\r\nAny unfinished operation for this Collection will be stopped. Downloaded archives are kept.",
						collectionName, rows.Count(x => x.Remove));
					grid.RefreshDataSource();
				};
				check.EditValueChanged += (sender, args) => { view.PostEditor(); };
				view.CellValueChanged += (sender, args) => { updateSummary(); };
				view.ShowingEditor += (sender, args) =>
				{
					RemovalRow row = view.GetFocusedRow() as RemovalRow;
					if (row == null || !row.Impact.RequiresNativeRemoval) args.Cancel = true;
				};
				updateSummary();
			}

			PanelControl buttons = new PanelControl { Dock = DockStyle.Bottom, Height = 52, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
			SimpleButton cancel = new SimpleButton
			{
				Text = LanguageManager.Get("Common.Action.Cancel", "Cancel"), AutoSize = true,
				DialogResult = DialogResult.Cancel, Dock = DockStyle.Right, Padding = new Padding(10)
			};
			SimpleButton uninstall = new SimpleButton
			{
				Text = LanguageManager.Get("Collections.Management.Removal.Confirm", "Uninstall Collection"), AutoSize = true,
				Dock = DockStyle.Right, Padding = new Padding(10)
			};
			uninstall.Click += (sender, args) =>
			{
				view.PostEditor();
				view.UpdateCurrentRow();
				ReviewedPlan = plan.WithKeptMods(rows.Where(x => x.Impact.RequiresNativeRemoval && !x.Remove).Select(x => x.Impact.NativeMod));
				DialogResult = DialogResult.OK;
			};
			NmmIconProvider.BindDialogButton(uninstall, NmmIconAction.Uninstall);
			NmmIconProvider.BindDialogButton(cancel, NmmIconAction.Cancel);
			buttons.Controls.Add(uninstall);
			buttons.Controls.Add(cancel);
			Controls.Add(grid);
			Controls.Add(summary);
			Controls.Add(buttons);
			AcceptButton = cancel;
			CancelButton = cancel;
			DevExpressDisplaySettingsApplier.ApplyToControlTree(this, settings);
		}

		/// <summary>One display-only reviewed native-mod removal decision.</summary>
		private sealed class RemovalRow
		{
			/// <summary>Creates a named removal or preservation row.</summary>
			internal RemovalRow(CollectionUninstallNativeImpact impact, string modName, string action, string reason)
			{
				Impact = impact;
				Remove = impact.RequiresNativeRemoval && impact.StandaloneUse != StandaloneModUse.ExplicitStandaloneUse && impact.SurvivingAssociationIds.Count == 0;
				ModName = modName;
				Action = action;
				Reason = reason;
			}
			internal CollectionUninstallNativeImpact Impact { get; private set; }
			public bool Remove { get; set; }
			public string ModName { get; private set; }
			public string Action { get; set; }
			public string Reason { get; private set; }
		}
	}
}
