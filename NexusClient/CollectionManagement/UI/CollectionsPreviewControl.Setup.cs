using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraTab;

namespace Nexus.Client.CollectionManagement.UI
{
	/// <summary>Collection instructions and explicitly reviewed post-install setup packages.</summary>
	public sealed partial class CollectionsPreviewControl
	{
		private XtraTabPage _setupPage;
		private SimpleButton _setupNotice;
		private Control _setupSection;
		private LabelControl _setupSummary;
		private bool _setupNeedsAttention;
		private bool _setupRequiresRecovery;
		private SimpleButton _setupImport;
		private SimpleButton _setupUndo;
		private SimpleButton _setupAcknowledge;
		private MemoEdit _setupInstructions;
		private MemoEdit _setupHistory;
		private LabelControl _setupHint;
		private FlowLayoutPanel _setupLinks;
		private string _setupUiKey;
		private string _setupInstructionText;
		private bool _setupCanUndo;
		private bool _setupAcknowledged;

		/// <summary>Adds a drop target and visible instruction notice without changing existing tab identities.</summary>
		private void InitializeSetupPage(DevExpress.Utils.Layout.TablePanel previewLayout)
		{
			_setupPage = CreateCollectionPage(_reviewTabs, L("Collections.Setup.Tab", "Setup & instructions"));
			_setupNotice = new SimpleButton { AutoSize = true, Text = L("Collections.Presentation.ReviewSetup", "Review setup...") };
			var setupSection = CreateCollectionTable(3);
			setupSection.Padding = new Padding(0, 8, 0, 4);
			_setupSection = setupSection;
			var setupHeading = CreateCollectionCaption(L("Collections.Presentation.AdditionalSetup", "Additional setup"));
			setupHeading.Appearance.FontStyleDelta = System.Drawing.FontStyle.Bold;
			_setupSummary = CreateCollectionCaption(String.Empty);
			var setupActions = new CollectionActionPanel();
			setupActions.Controls.Add(_setupNotice);
			AddCollectionCell(setupSection, setupHeading, 0);
			AddCollectionCell(setupSection, _setupSummary, 1);
			AddCollectionCell(setupSection, setupActions, 2);
			_setupNotice.Click += (sender, args) => _reviewTabs.SelectedTabPage = _setupPage;
			_setupSummary.TextChanged += (sender, args) => QueueDocumentLayout();
			_setupSection.VisibleChanged += (sender, args) => QueueDocumentLayout();
			previewLayout.Rows.Add(new DevExpress.Utils.Layout.TablePanelRow(DevExpress.Utils.Layout.TablePanelEntityStyle.AutoSize, 1F));
			previewLayout.SetCell(_incomingActionsPanel, 3, 0); AddCollectionCell(previewLayout, _setupSection, 2);
			var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8), AllowDrop = true };
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
			_setupPage.Controls.Add(layout);
			_setupHint = new LabelControl { Dock = DockStyle.Top, AutoSizeMode = LabelAutoSizeMode.Vertical, AllowDrop = true,
				Text = L("Collections.Setup.DropHelp", "Drop an additional setup ZIP here, or choose Review setup package. Select the Collection revision first. Apply setup files only after its installation finishes. Personal settings are optional.") };
			layout.Controls.Add(_setupHint, 0, 0);
			var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
			_setupImport = new SimpleButton { AutoSize = true, Text = L("Collections.Setup.Import", "Review setup package...") };
			_setupUndo = new SimpleButton { AutoSize = true, Text = L("Collections.Setup.Undo", "Undo / recover last setup package...") };
			_setupAcknowledge = new SimpleButton { AutoSize = true };
			actions.Controls.AddRange(new Control[] { _setupImport, _setupUndo, _setupAcknowledge }); layout.Controls.Add(actions, 0, 1);
			_setupInstructions = new MemoEdit { Dock = DockStyle.Fill, AllowDrop = true }; _setupInstructions.Properties.ReadOnly = true;
			layout.Controls.Add(_setupInstructions, 0, 2);
			_setupLinks = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true }; layout.Controls.Add(_setupLinks, 0, 3);
			_setupHistory = new MemoEdit { Dock = DockStyle.Fill }; _setupHistory.Properties.ReadOnly = true; layout.Controls.Add(_setupHistory, 0, 4);
			_setupImport.Click += async (sender, args) =>
			{
				CollectionRevisionIdentity revision = SetupRevision();
				if (_workflow != null && revision != null && HasWabbajackSetup(revision))
				{
					await ReviewSetupPackageAsync(null, true); return;
				}
				using (var dialog = new OpenFileDialog { Filter = L("Collections.Setup.ZipFilter", "Setup ZIP archives (*.zip)|*.zip"), CheckFileExists = true })
					if (dialog.ShowDialog(this) == DialogResult.OK) await ReviewSetupPackageAsync(dialog.FileName);
			};
			_setupUndo.Click += async (sender, args) => await UndoSetupPackageAsync();
			_setupAcknowledge.Click += (sender, args) =>
			{
				CollectionRevisionIdentity revision = SetupRevision(); if (revision == null || _workflow == null || _workflowBusy) return;
				try { _workflow.SetupPackages.AcknowledgeInstructions(revision, SetupAcknowledgementText()); _setupUiKey = null; UpdateSetupUi(); RefreshSetupInstructionIssue(); UpdateCollectionPresentation(); }
				catch (Exception error) { ShowSetupError(error); }
			};
			foreach (Control target in new Control[] { layout, _setupHint, _setupInstructions })
			{
				target.DragEnter += (sender, args) => { if (!_workflowBusy && _workflow != null && SetupRevision() != null && args.Data.GetDataPresent(DataFormats.FileDrop)) args.Effect = DragDropEffects.Copy; };
				target.DragDrop += async (sender, args) =>
				{
					string[] paths = args.Data.GetData(DataFormats.FileDrop) as string[];
					if (paths != null && paths.Length == 1) await ReviewSetupPackageAsync(paths[0]);
				};
			}
		}

		/// <summary>Uses the displayed immutable revision rather than guessing from a setup archive's filename.</summary>
		private CollectionRevisionIdentity SetupRevision() { return _managedAssociationPresentation != null ? _managedAssociationPresentation.Association.Association.Revision : _snapshot?.Revision?.Identity; }
		/// <summary>Separates before-install review from the post-install completion acknowledgement.</summary>
		private string SetupAcknowledgementText() { return _setupInstructionText + (SetupInstalled() ? "\npost-install" : "\npre-install"); }
		/// <summary>Observes verified installation state; setup file publication independently revalidates it under the target lease.</summary>
		private bool SetupInstalled() { CollectionRevisionIdentity revision = SetupRevision(); return revision != null &&
			((_operationSnapshot != null && revision.Equals(_operationSnapshot.Revision) && _operationSnapshot.IsSuccessful) ||
			(_managedAssociationPresentation != null && revision.Equals(_managedAssociationPresentation.Association.Association.Revision) &&
			_managedAssociationPresentation.Association.Association.State == CollectionAssociationState.Applied)); }

		/// <summary>Updates instruction visibility on revision/installation changes while avoiding repeated receipt reads during progress updates.</summary>
		private void UpdateSetupUi()
		{
			if (_setupPage == null || IsDisposed || Disposing) return;
			CollectionRevisionIdentity revision = SetupRevision(); bool available = _workflow != null && revision != null;
			_setupPage.PageVisible = available; _setupImport.Enabled = available && !_workflowBusy;
			_setupUndo.Enabled = available && !_workflowBusy && _setupCanUndo;
			_setupAcknowledge.Enabled = available && !_workflowBusy && !_setupAcknowledged;
			if (!available) { _setupSection.Visible = false; _setupUiKey = null; return; }
			bool wabbajackSettings = HasWabbajackSetup(revision);
			_setupImport.Text = wabbajackSettings ? L("Collections.Setup.WabbajackSettings", "Review Wabbajack settings...") : L("Collections.Setup.Import", "Review setup package...");
			_setupHint.Text = wabbajackSettings
				? L("Collections.Setup.WabbajackSettingsHelp", "NMM has already created a ZIP with the selected Wabbajack profile's game INI files. After the mods finish installing, click Review Wabbajack settings... to review and apply them. The ZIP is selected automatically; you do not need to download or find another file.")
				: L("Collections.Setup.DropHelp", "Drop an additional setup ZIP here, or choose Review setup package. Select the Collection revision first. Apply setup files only after its installation finishes. Personal settings are optional.");
			string text = _snapshot != null && _snapshot.HasManifestPreview ? _snapshot.CapabilityReport.Manifest.SetupGuidance?.InstallInstructions : null;
			string key = revision + "\n" + text + "\n" + SetupInstalled() + "\n" + wabbajackSettings;
			if (_setupUiKey == key) return;
			_setupUiKey = key; _setupInstructionText = text ?? String.Empty; _setupAcknowledged = false;
			_setupInstructions.Text = String.IsNullOrWhiteSpace(text) ? L("Collections.Setup.NoInstructions", "This revision supplies no curator installation instructions. Additional setup archives can still be reviewed here.") : text;
			_setupAcknowledge.Visible = !String.IsNullOrWhiteSpace(text);
			_setupAcknowledge.Text = SetupInstalled() ? L("Collections.Setup.AcknowledgeAfter", "I completed the curator's instructions") : L("Collections.Setup.AcknowledgeBefore", "I reviewed the curator's instructions");
			try
			{
				_setupHistory.Text = _workflow.SetupPackages.History(revision);
				_setupCanUndo = _workflow.SetupPackages.CanUndo(revision);
				_setupAcknowledged = _workflow.SetupPackages.InstructionsAcknowledged(revision, SetupAcknowledgementText());
				_setupAcknowledge.Enabled = !_workflowBusy && !_setupAcknowledged;
				CollectionSetupPackageState state = _workflow.SetupPackages.GetState(revision);
				_setupRequiresRecovery = state == CollectionSetupPackageState.RecoveryRequired;
				_setupNeedsAttention = state == CollectionSetupPackageState.RecoveryRequired || (!_setupAcknowledged && !String.IsNullOrWhiteSpace(text));
				_setupSummary.Text = state == CollectionSetupPackageState.RecoveryRequired ? L("Collections.Presentation.SetupInterrupted", "Setup was interrupted. Review recovery before continuing.")
					: state == CollectionSetupPackageState.Applied ? (_setupAcknowledged || String.IsNullOrWhiteSpace(text)
						? L("Collections.Presentation.SetupApplied", "Selected setup files applied.") : L("Collections.Presentation.SetupAppliedReview", "Selected setup files applied. Curator instructions still need review."))
					: _setupAcknowledged ? L("Collections.Presentation.InstructionsReviewed", "Curator instructions acknowledged.")
					: SetupInstalled() ? L("Collections.Presentation.SetupReviewNeeded", "Curator instructions still need review.")
					: L("Collections.Presentation.SetupReviewBefore", "Review the curator's instructions before installing.");
				if (wabbajackSettings && state == CollectionSetupPackageState.None && !_setupAcknowledged)
					_setupSummary.Text = SetupInstalled() ? L("Collections.Setup.WabbajackSettingsReady", "Mods installed. Open Setup & instructions and click Review Wabbajack settings... to apply the profile's game INI files.")
						: L("Collections.Setup.WabbajackSettingsAfterInstall", "NMM has prepared the profile's game INI files. Apply them from Setup & instructions after the mods finish installing.");
				if (state == CollectionSetupPackageState.Restored) _setupSummary.Text = L("Collections.Presentation.SetupRestored", "Last setup package restored. Review instructions before reapplying.");
				_setupSection.Visible = !String.IsNullOrWhiteSpace(text) || state != CollectionSetupPackageState.None;
			}
			catch (Exception error) { _setupHistory.Text = error.Message; _setupCanUndo = true; _setupNeedsAttention = true; _setupRequiresRecovery = true; _setupSummary.Text = L("Collections.Presentation.SetupHistoryError", "Setup history needs attention. Review its recovery details."); _setupSection.Visible = true; }
			_setupUndo.Enabled = !_workflowBusy && _setupCanUndo;
			foreach (Control control in _setupLinks.Controls.Cast<Control>().ToArray()) { _setupLinks.Controls.Remove(control); control.Dispose(); }
			foreach (Match match in Regex.Matches(text ?? String.Empty, @"https?://[^\s<>\[\]()""']+", RegexOptions.IgnoreCase).Cast<Match>().GroupBy(x => x.Value).Select(x => x.First()))
			{
				Uri uri; if (!Uri.TryCreate(match.Value.TrimEnd('.', ','), UriKind.Absolute, out uri) || (uri.Scheme != "https" && uri.Scheme != "http")) continue;
				bool additional = AdditionalNexusMod(uri);
				var link = new SimpleButton { AutoSize = true, Text = (additional ? L("Collections.Setup.ExternalMod", "Linked mod outside this Collection: ") : String.Empty) + uri.Host + uri.AbsolutePath, Tag = uri };
				link.Click += (sender, args) => { try { Process.Start(((Uri)((Control)sender).Tag).AbsoluteUri); } catch (Exception error) { ShowSetupError(error); } };
				_setupLinks.Controls.Add(link);
			}
		}

		/// <summary>Updates only the instruction reminder, retaining installation, recovery and review diagnostics.</summary>
		private void RefreshSetupInstructionIssue()
		{
			if (_snapshot == null || !_snapshot.HasManifestPreview) return;
			List<CollectionReviewItem> retained = _reviewItems.Where(x => !x.Code.StartsWith("manifest.guidance.", StringComparison.Ordinal)).ToList();
			BeginReviewBatch();
			try { ClearReviewItems(); foreach (CollectionReviewItem item in retained) AddReviewItem(item); AppendSetupGuidance(_snapshot.CapabilityReport.Manifest.SetupGuidance); }
			finally { EndReviewBatch(); }
		}

		/// <summary>Flags linked Nexus mod identities absent from the manifest without inferring that every link is a requirement.</summary>
		private bool AdditionalNexusMod(Uri uri)
		{
			if (!(uri.Host.Equals("nexusmods.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".nexusmods.com", StringComparison.OrdinalIgnoreCase)) || !_snapshot.HasManifestPreview) return false;
			Match match = Regex.Match(uri.AbsolutePath, @"^/(?:games/)?([^/]+)/mods/([0-9]+)", RegexOptions.IgnoreCase);
			long linked; if (!match.Success || !Int64.TryParse(match.Groups[2].Value, out linked)) return false;
			return !_snapshot.CapabilityReport.Manifest.Members.Any(member =>
			{
				string domain; long mod, file;
				return NexusCollectionModFileArtifactIdentity.TryParse(member.Artifact, out domain, out mod, out file) &&
					mod == linked && domain.Equals(match.Groups[1].Value, StringComparison.OrdinalIgnoreCase);
			});
		}

		/// <summary>Inspects the dropped archive, obtains explicit selection approval, then delegates publication to the application service.</summary>
		private async Task ReviewSetupPackageAsync(string path, bool wabbajackSettings = false)
		{
			CollectionRevisionIdentity revision = SetupRevision(); if (_workflowBusy || revision == null || _workflow == null) return;
			CollectionUiContext context = _displayContext ?? CollectionUiContext.CurrentSetup(_previewGeneration);
			string outcome = _workflowStatusLabel.Text; bool failed = false; bool publishing = false;
			CancellationToken token = BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Managing, L("Collections.Setup.Inspecting", "Inspecting additional setup files..."));
			try
			{
				CollectionSetupPackage package = await Task.Run(() => wabbajackSettings
					? _workflow.SetupPackages.PreviewWabbajack(revision) : _workflow.SetupPackages.Preview(path, revision), token);
				if (!IsWorkflowContextCurrent(context, token)) return;
				using (var dialog = new CollectionSetupPackageReviewDialog(package, _workflow.SetupPackages, SetupInstalled()))
				{
					if (dialog.ShowDialog(this) != DialogResult.OK || !IsWorkflowContextCurrent(context, token)) return;
					publishing = true;
					SetWorkflowActivity(CollectionWorkflowActivityBuilder.Foreground(CollectionWorkflowActivityPhase.Managing,
						L("Collections.Presentation.ApplyingSetup", "Applying selected setup files...")));
					string result = await Task.Run(() => _workflow.SetupPackages.Apply(package, revision, token), token);
					outcome = result;
					if (!IsWorkflowContextCurrent(context, token)) return;
					ShowSetupCompletion(result);
					XtraMessageBox.Show(this, result, L("Collections.Setup.Tab", "Setup & instructions"), MessageBoxButtons.OK, MessageBoxIcon.Information);
				}
			}
			catch (OperationCanceledException) { if (publishing) outcome = L("Collections.Presentation.SetupStopped", "Setup stopped. Review setup history for recovery before continuing."); }
			catch (Exception error) { failed = true; outcome = L("Collections.Presentation.SetupFailed", "Setup could not finish. Review its details and recovery information."); if (IsWorkflowContextCurrent(context, token)) ShowSetupError(error); }
			finally { FinishSetupWork(context, outcome, failed); }
		}

		/// <summary>Explicitly restores a package or reconciles an interrupted publication using its retained pre/postimages.</summary>
		private async Task UndoSetupPackageAsync()
		{
			CollectionRevisionIdentity revision = SetupRevision(); if (_workflowBusy || revision == null || _workflow == null) return;
			if (XtraMessageBox.Show(this, L("Collections.Setup.UndoPrompt", "Restore the latest setup package's original files? Files changed since application will stop the undo. Backups will be kept."),
				L("Collections.Setup.Undo", "Undo / recover last setup package..."), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
			CollectionUiContext context = _displayContext ?? CollectionUiContext.CurrentSetup(_previewGeneration);
			string outcome = L("Collections.Presentation.SetupStopped", "Setup stopped. Review setup history for recovery before continuing."); bool failed = false;
			CancellationToken token = BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Managing, L("Collections.Setup.Undoing", "Checking and restoring setup backups..."));
			try { string result = await Task.Run(() => _workflow.SetupPackages.Undo(revision, token), token); outcome = result; if (!IsWorkflowContextCurrent(context, token)) return; ShowSetupCompletion(result); XtraMessageBox.Show(this, result, L("Collections.Setup.Tab", "Setup & instructions"), MessageBoxButtons.OK, MessageBoxIcon.Information); }
			catch (OperationCanceledException) { }
			catch (Exception error) { failed = true; outcome = L("Collections.Presentation.SetupUndoFailed", "Setup restore could not finish. Review its recovery details."); if (IsWorkflowContextCurrent(context, token)) ShowSetupError(error); }
			finally { FinishSetupWork(context, outcome, failed); }
		}
		/// <summary>Shows the durable result immediately, including while the completion message is open.</summary>
		private void ShowSetupCompletion(string result)
		{
			_workflowStatusLabel.Text = result;
			SetWorkflowActivity(CollectionWorkflowActivityBuilder.Completed(CollectionWorkflowActivityPhase.Managing, result));
			_setupUiKey = null; UpdateSetupUi();
		}

		/// <summary>Replaces transient inspection text before the generic workflow recomputes its final activity.</summary>
		private void FinishSetupWork(CollectionUiContext context, string outcome, bool failed)
		{
			if (ReferenceEquals(_workflowContext, context))
			{
				_workflowStatusLabel.Text = outcome;
				SetWorkflowActivity(failed ? CollectionWorkflowActivityBuilder.Failed(CollectionWorkflowActivityPhase.Managing, outcome) : CollectionWorkflowActivityBuilder.Idle(outcome));
			}
			_setupUiKey = null; EndWorkflowWork(context); UpdateSetupUi();
		}

		/// <summary>Surfaces actionable setup errors and retains diagnostics in the application's existing trace log.</summary>
		private void ShowSetupError(Exception error) { Trace.TraceError("Collection setup package: " + error); XtraMessageBox.Show(this, error.Message + Environment.NewLine + L("Collections.Setup.RecoverHint", "If publication was interrupted, use Undo / recover last setup package. Existing backups are retained."), L("Collections.Setup.Error", "Collection setup needs attention"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
	}
}
