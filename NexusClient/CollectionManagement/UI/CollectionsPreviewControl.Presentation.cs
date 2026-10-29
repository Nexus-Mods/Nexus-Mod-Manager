using System;
using System.Drawing;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using DevExpress.Utils.Layout;
using DevExpress.XtraBars;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid.Views.Base;
using DevExpress.XtraGrid.Views.Grid;
using Nexus.Client.UI;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.CollectionManagement.UI
{
	/// <summary>Compact Collection navigation, installation options and selected-member presentation.</summary>
	public sealed partial class CollectionsPreviewControl
	{
		private LabelControl _collectionSubtitle;
		private SimpleButton _installationOptionsButton;
		private LabelControl _installationOptionsSummary;
		private DropDownButton _moreCollectionActionsButton;
		private PopupMenu _moreCollectionActionsMenu;
		private BarButtonItem _openCollectionPageMenuItem;
		private SimpleButton _advancedInstallOptionsButton;
		private bool _advancedInstallOptionsVisible;
		private ManagedFontXtraForm _installationOptionsDialog;
		private TablePanel _selectedMemberSection;
		private LabelControl _selectedMemberDetails;
		private DropDownButton _selectedMemberActionsButton;

		/// <summary>Creates compact skin-aware text using the shared font and wrapping behavior.</summary>
		private static LabelControl CreateCollectionCaption(string text)
		{
			LabelControl label = CreateApprovalLabel(text);
			label.Padding = new Padding(0, 0, 0, 3);
			return label;
		}

		/// <summary>Creates a small shared toolbar while retaining every existing command and preference source.</summary>
		private void InitializeCollectionToolbar()
		{
			_installationOptionsButton = new SimpleButton { AutoSize = true, Text = L("Collections.Presentation.Options", "Installation options...") };
			_installationOptionsButton.Click += (sender, args) => ShowCollectionInstallationOptions();
			_installationOptionsSummary = CreateCollectionCaption(String.Empty);
			_moreCollectionActionsButton = new DropDownButton { AutoSize = true, Text = L("Collections.Presentation.MoreActions", "More actions") };
		}

		/// <summary>Routes secondary actions through their existing guarded handlers, including removal review.</summary>
		private void InitializeCollectionActionMenu()
		{
			_moreCollectionActionsMenu = new PopupMenu(_memberMenuManager);
			_openCollectionPageMenuItem = new BarButtonItem(_memberMenuManager, L("Collections.Actions.OpenCollectionPage", "Go to Collection page"));
			_openCollectionPageMenuItem.ItemClick += (sender, args) => OpenSelectedCollectionPage();
			_moreCollectionActionsMenu.AddItem(_openCollectionPageMenuItem);
			AddMemberActionMenuItem(_moreCollectionActionsMenu, _cloneManagedAssociationButton, CloneManagedAssociationButton_Click, false);
			AddMemberActionMenuItem(_moreCollectionActionsMenu, _exportTechnicalReportButton, ExportTechnicalReportButton_Click, false);
			AddMemberActionMenuItem(_moreCollectionActionsMenu, _manageAssociationRemovalButton, ManageAssociationRemovalButton_Click, true);
			_moreCollectionActionsButton.DropDownControl = _moreCollectionActionsMenu;
		}

		/// <summary>Resolves the displayed Nexus Collection's page without borrowing another preview's locator.</summary>
		private Uri GetSelectedCollectionPageUri()
		{
			CollectionRevisionIdentity revision = SetupRevision();
			if (revision == null || revision.Collection.Origin != CollectionOrigin.NexusMods) return null;
			string domain;
			string slug;
			if (_managedAssociationPresentation != null)
			{
				domain = _managedAssociationPresentation.NexusGameDomain;
				slug = _managedAssociationPresentation.NexusCollectionSlug;
			}
			else
			{
				domain = _snapshot?.Link?.GameDomain;
				slug = _snapshot?.Link?.CollectionSlug;
			}
			if (String.IsNullOrWhiteSpace(domain) || String.IsNullOrWhiteSpace(slug)) return null;
			return new Uri("https://www.nexusmods.com/games/" + Uri.EscapeDataString(domain) + "/collections/" + Uri.EscapeDataString(slug));
		}

		/// <summary>Opens the currently selected Collection in the browser using the existing page-launch behavior.</summary>
		private void OpenSelectedCollectionPage()
		{
			Uri page = GetSelectedCollectionPageUri();
			if (page == null) return;
			try { Process.Start(page.AbsoluteUri); }
			catch (Exception ex)
			{
				Trace.TraceWarning("Unable to open Collection page: " + ex);
				XtraMessageBox.Show(this, L("Collections.Actions.OpenCollectionPageFailed", "NMM could not open the Collection page in your browser.") +
					Environment.NewLine + ex.Message, L("Collections.Actions.OpenCollectionPage", "Go to Collection page"), MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		/// <summary>Shows existing live preference controls in a modal surface; their persistence and policy behavior stay unchanged.</summary>
		private void ShowCollectionInstallationOptions()
		{
			if (_workflowBusy || _installationOptionsDialog != null) return;
			Control owner = _installPreferencesSection.Parent;
			using (DevExpressDisplaySettings settings = DevExpressDisplaySettings.CreateFromSettings(Properties.Settings.Default))
			using (var dialog = new ManagedFontXtraForm { Text = L("Collections.Presentation.Options", "Installation options..."),
				StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, MinimizeBox = false, MaximizeBox = false })
			{
				_installationOptionsDialog = dialog;
				var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
				layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
				layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				layout.Controls.Add(CreateApprovalLabel(L("Collections.Presentation.OptionsHelp", "These preferences are saved immediately and apply to future installation reviews. They do not change an installed Collection.")), 0, 0);
				var body = new XtraScrollableControl { Dock = DockStyle.Fill, AutoScroll = true };
				layout.Controls.Add(body, 0, 1);
				var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
				var close = new SimpleButton { AutoSize = true, Text = L("Common.Button.Close", "Close"), DialogResult = DialogResult.OK };
				actions.Controls.Add(close); layout.Controls.Add(actions, 0, 2); dialog.Controls.Add(layout);
				dialog.AcceptButton = close; dialog.CancelButton = close;
				// Existing editors retain application-owned fonts; the temporary dialog's font must not outlive its settings.
				ApplyCollectionDialogDisplaySettings(dialog, settings);
				try
				{
					body.Controls.Add(_installPreferencesSection);
					_installPreferencesSection.ShowCaption = false;
					_installPreferencesSection.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
					_installPreferencesSection.Dock = DockStyle.Top;
					_installPreferencesSection.Visible = true;
					SetAdvancedInstallOptionsVisible(false);
					ResizeInstallationOptionsDialog();
					dialog.Shown += (sender, args) => ResizeInstallationOptionsDialog();
					dialog.ShowDialog(this);
				}
				finally
				{
					_installPreferencesSection.Visible = false;
					body.Controls.Remove(_installPreferencesSection);
					owner.Controls.Add(_installPreferencesSection);
					_collectionHeaderLayout.SetCell(_installPreferencesSection, 1, 0);
					_installPreferencesSection.Dock = DockStyle.Fill;
					_installationOptionsDialog = null;
					UpdateInstallationOptionsSummary(); QueueDocumentLayout();
				}
			}
		}

		/// <summary>Expands less frequently used verification and plugin options without changing their stored values.</summary>
		private void ToggleAdvancedInstallOptions()
		{
			SetAdvancedInstallOptionsVisible(!_advancedInstallOptionsVisible);
			ResizeInstallationOptionsDialog();
		}

		/// <summary>Changes only the visibility of advanced preference controls.</summary>
		private void SetAdvancedInstallOptionsVisible(bool visible)
		{
			_advancedInstallOptionsVisible = visible;
			foreach (Control control in new Control[] { _skipExistingArchiveMd5CheckBox, _skipExistingArchiveMd5Hint, _skipContentChecksCheckBox, _skipContentChecksHint, _enableUnspecifiedPluginsCheckBox, _enableUnspecifiedPluginsHint })
				control.Visible = visible;
			UpdateAdvancedInstallOptionsCaption();
		}

		/// <summary>Keeps non-default advanced choices discoverable when their controls are collapsed.</summary>
		private void UpdateAdvancedInstallOptionsCaption()
		{
			if (_advancedInstallOptionsButton == null) return;
			int enabled = new[] { _skipExistingArchiveMd5CheckBox.Checked, _skipContentChecksCheckBox.Checked, _enableUnspecifiedPluginsCheckBox.Checked }.Count(x => x);
			_advancedInstallOptionsButton.Text = _advancedInstallOptionsVisible ? L("Collections.Presentation.HideAdvancedOptions", "Hide advanced options") :
				LanguageManager.Format("Collections.Presentation.AdvancedOptionsCount", "Advanced options ({0} enabled)...", enabled);
		}

		/// <summary>Sizes the options dialog from current font metrics and leaves scrolling available on smaller displays.</summary>
		private void ResizeInstallationOptionsDialog()
		{
			if (_installationOptionsDialog == null) return;
			Rectangle screen = Screen.FromControl(this).WorkingArea;
			int width = Math.Min(screen.Width * 9 / 10, Math.Max(620, Font.Height * 48));
			int height = Math.Min(screen.Height * 4 / 5, Math.Max(260, Font.Height * (_advancedInstallOptionsVisible ? 42 : 19)));
			_installationOptionsDialog.ClientSize = new Size(width, height);
			_installPreferencesSection.Controls[0].PerformLayout();
			_installPreferencesSection.Height = _installPreferencesSection.GetPreferredSize(new Size(Math.Max(1, width - 50), 0)).Height;
		}

		/// <summary>Summarizes actual saved policy choices without claiming that they describe past verification.</summary>
		private void UpdateInstallationOptionsSummary()
		{
			if (_installationOptionsSummary == null || _automaticInstallCheckBox == null) return;
			string mode = _automaticInstallCheckBox.Checked ? L("Collections.Presentation.Automatic", "Automatic installation") : L("Collections.Presentation.Manual", "Manual installation");
			string conflicts = _overwriteInstalledFilesCheckBox.Checked ? L("Collections.Presentation.ReplaceConflicts", "Replace conflicts") : L("Collections.Presentation.AskConflicts", "Ask about conflicts");
			_installationOptionsSummary.Text = mode + " · " + conflicts + (_skipContentChecksCheckBox.Checked ? " · " + L("Collections.Presentation.ExtraAuditsOff", "Additional audits off") : String.Empty);
			UpdateAdvancedInstallOptionsCaption();
			_installationOptionsButton.Enabled = !_workflowBusy;
		}

		/// <summary>Provides visible mod commands and complete ownership context beside the selected row.</summary>
		private void InitializeSelectedMemberDetails(TablePanel membersLayout)
		{
			_selectedMemberSection = CreateCollectionTable(2);
			_selectedMemberSection.Padding = new Padding(0, 7, 0, 0);
			_selectedMemberDetails = CreateCollectionCaption(String.Empty);
			_showManagedMemberButton.Text = L("Collections.Presentation.ShowInMods", "Show in Mods");
			_showManagedMemberImpactButton.Text = L("Collections.Presentation.OwnershipDetails", "Ownership details...");
			_selectedMemberActionsButton = new DropDownButton { AutoSize = true, Text = L("Collections.Presentation.ModActions", "Mod actions"), DropDownControl = _memberActionsMenu };
			var actions = new CollectionActionPanel();
			actions.Controls.AddRange(new Control[] { _showManagedMemberButton, _showManagedMemberImpactButton, _selectedMemberActionsButton });
			AddCollectionCell(_selectedMemberSection, _selectedMemberDetails, 0);
			AddCollectionCell(_selectedMemberSection, actions, 1);
			AddCollectionCell(membersLayout, _selectedMemberSection, 2);
		}

		/// <summary>Recognizes a finished recipe without treating a new review or recovery as installation history.</summary>
		private bool HasCompletedCollectionPresentation()
		{
			CollectionRevisionIdentity revision = SetupRevision();
			if (revision == null || _managedMemberExpansionAssociationId.HasValue || (_operationSnapshot != null && !_operationSnapshot.IsSuccessful) ||
				_reviewItems.Any(x => x.Kind == CollectionReviewItemKind.PlannedEffect && x.Code.StartsWith("verify-repair.", StringComparison.Ordinal))) return false;
			if (_managedAssociationPresentation != null)
				return _managedAssociationPresentation.Association.PendingRevision == null &&
					(_managedAssociationPresentation.Association.State == CollectionAssociationState.Applied || _managedAssociationPresentation.Association.State == CollectionAssociationState.Modified);
			return _operationSnapshot != null && revision.Equals(_operationSnapshot.Revision) && _operationSnapshot.IsSuccessful;
		}

		/// <summary>Applies context-specific presentation after the existing workflow has established command permissions.</summary>
		private void UpdateCollectionPresentation()
		{
			if (_collectionSubtitle == null || _membersView == null) return;
			_collectionValue.Appearance.FontStyleDelta = FontStyle.Bold;
			_collectionValue.Appearance.FontSizeDelta = 2;
			CollectionRevisionIdentity revision = SetupRevision();
			int? count = _snapshot != null && _snapshot.HasManifestPreview ? (int?)_snapshot.CapabilityReport.Manifest.Members.Count : _managedAssociationPresentation?.Members.Count;
			_collectionSubtitle.Text = revision == null ? _revisionValue.Text : count.HasValue ? LanguageManager.Format("Collections.Presentation.RevisionMods", "{0} · {1} mods", FormatRevisionActionLabel(revision), count.Value) : FormatRevisionActionLabel(revision);
			_collectionSubtitle.Visible = revision != null;
			UpdateInstallationOptionsSummary();
			if (_setupSummary != null)
			{
				_setupSummary.MaximumSize = new Size(Math.Max(220, Font.Height * 56), 0);
				_setupSummary.Appearance.Options.UseForeColor = _setupNeedsAttention;
				if (_setupNeedsAttention)
				{
					NmmColorPalette palette = NmmColorPalette.Resolve(NmmIconProvider.CurrentColorProfile, DevExpressDisplaySettingsApplier.IsDarkSkinSurface());
					_setupSummary.Appearance.ForeColor = _setupRequiresRecovery ? palette.PluginErrorColor : palette.PluginWarningColor;
				}
			}
			_openCollectionPageMenuItem.Enabled = GetSelectedCollectionPageUri() != null;
			_openCollectionPageMenuItem.Visibility = _openCollectionPageMenuItem.Enabled ? BarItemVisibility.Always : BarItemVisibility.Never;
			foreach (BarItemLink link in _moreCollectionActionsMenu.ItemLinks)
			{
				SimpleButton source = link.Item.Tag as SimpleButton;
				if (source == null) continue;
				link.Item.Caption = source.Text;
				link.Item.Enabled = source.Enabled;
				link.Item.Visibility = source.Enabled ? BarItemVisibility.Always : BarItemVisibility.Never;
			}
			_moreCollectionActionsButton.Enabled = _moreCollectionActionsMenu.ItemLinks.Cast<BarItemLink>().Any(x => x.Item.Enabled);
			_moreCollectionActionsButton.Visible = _moreCollectionActionsButton.Enabled;
			bool selectionPhase = _managedAssociationPresentation == null && _incomingActionContext != null &&
				(_operationSnapshot == null || (!_operationSnapshot.HasCrossedNativeBoundary && !_operationSnapshot.IsSuccessful));
			_managedAssociationCombo.Visible = _managedAssociationCombo.Properties.Items.Count > 0;
			_membersView.Columns[nameof(MemberGridRow.Selected)].Visible = selectionPhase;
			if (selectionPhase) _membersView.Columns[nameof(MemberGridRow.Selected)].VisibleIndex = 0;
			_membersView.Columns[nameof(MemberGridRow.ManagedState)].Caption = L("Collections.Presentation.ModStatus", "Status");
			MemberGridRow row = _membersView.GetFocusedRow() as MemberGridRow;
			_selectedMemberSection.Visible = row != null && !_workflowBusy;
			_selectedMemberDetails.Text = row == null ? String.Empty : row.DisplayName + Environment.NewLine + row.ManagedState;
			_showManagedMemberButton.Visible = _showManagedMemberButton.Enabled;
			_showManagedMemberImpactButton.Visible = _showManagedMemberImpactButton.Enabled;
			_selectedMemberActionsButton.Enabled = _memberActionsMenu.ItemLinks.Cast<BarItemLink>().Any(x => x.Item.Enabled && x.Item.Visibility == BarItemVisibility.Always);
			bool completed = HasCompletedCollectionPresentation();
			_workflowStatusLabel.Visible = !completed || _workflowBusy || _workflowActivity == null ||
				(_workflowActivity.State != CollectionWorkflowActivityState.Idle && _workflowActivity.State != CollectionWorkflowActivityState.Completed);
			UpdateReviewActionsVisibility();
			QueueDocumentLayout();
		}

		/// <summary>Shows a compact status while retaining full ownership text for filtering, tooltips and selected-mod details.</summary>
		private void MembersView_CustomColumnDisplayText(object sender, CustomColumnDisplayTextEventArgs e)
		{
			if (e.Column == null || e.Column.FieldName != nameof(MemberGridRow.ManagedState) || e.ListSourceRowIndex < 0 || e.ListSourceRowIndex >= _memberRows.Count) return;
			CollectionManagementMemberPresentation member = _memberRows[e.ListSourceRowIndex].ManagedMember;
			if (member == null || (_managedAssociationPresentation != null &&
				(_managedAssociationPresentation.Association.PendingRevision != null || _managedAssociationPresentation.Association.State == CollectionAssociationState.Recovering || _managedAssociationPresentation.Association.State == CollectionAssociationState.Incomplete))) return;
			if (member.HasDetectedDrift) { bool described; e.DisplayText = FormatManagedMemberCurrentState(member, out described); }
			else if (member.HasExplicitLocalDecision) e.DisplayText = L("Collections.Presentation.LocalChanges", "Installed · local changes");
			else if (member.IsSharedAcrossCollections) e.DisplayText = L("Collections.Presentation.SharedMod", "Installed · shared");
			else e.DisplayText = L("Collections.Presentation.InstalledMod", "Installed");
		}

		/// <summary>Uses NMM's accessible severity colours for observed missing or modified installed members.</summary>
		private void MembersView_RowCellStyle(object sender, RowCellStyleEventArgs e)
		{
			if (e.Column == null || e.Column.FieldName != nameof(MemberGridRow.ManagedState)) return;
			MemberGridRow row = _membersView.GetRow(e.RowHandle) as MemberGridRow;
			CollectionManagementMemberPresentation member = row?.ManagedMember;
			if (member == null || !member.HasDetectedDrift) return;
			bool missing = member.DriftObservations.Any(x => x.Requirement.Aspect == CollectionRequirementAspect.MemberParticipation && x.ObservedState.Kind == CollectionRequirementStateKind.Absent);
			NmmColorPalette palette = NmmColorPalette.Resolve(NmmIconProvider.CurrentColorProfile, DevExpressDisplaySettingsApplier.IsDarkSkinSurface());
			e.Appearance.ForeColor = missing ? palette.PluginErrorColor : palette.PluginWarningColor;
			e.Appearance.Options.UseForeColor = true;
		}
	}
}
