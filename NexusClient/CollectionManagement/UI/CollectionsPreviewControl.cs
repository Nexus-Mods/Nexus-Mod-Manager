using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Nexus.Client.CollectionManagement;
using Nexus.Client.ModAuthoring;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using Nexus.Client.OnlineServices.NexusMods.GraphQl;
using Nexus.Client.UI;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.CollectionManagement.UI
{
	/// <summary>
	/// C6.15 additive Collections surface for provider preview, durable preparation, exact review and explicit apply.
	/// </summary>
	/// <remarks>
	/// The control consumes one application-level workflow service and never constructs or invokes native C4/C5/C6
	/// persistence or mutation coordinators directly. Replacement and later management commands remain out of scope.
	/// </remarks>
	public sealed class CollectionsPreviewControl : ManagedFontDockContent
	{
		private readonly Button _importButton;
		private readonly Button _downloadPrepareButton;
		private readonly Button _resumeButton;
		private readonly Button _openPendingButton;
		private readonly Button _installButton;
		private readonly Button _clearButton;
		private readonly Label _instructionLabel;
		private readonly Label _workflowStatusLabel;
		private readonly Label _collectionValue;
		private readonly Label _curatorValue;
		private readonly Label _locatorValue;
		private readonly Label _revisionValue;
		private readonly Label _compatibilityValue;
		private readonly Label _contentValue;
		private readonly Label _appliedValue;
		private readonly TextBox _summaryBox;
		private readonly ListView _membersView;
		private readonly ListView _issuesView;
		private readonly Label _membersHeader;
		private readonly Label _issuesHeader;

		private NexusCollectionNxmDispatcher _dispatcher;
		private NexusCollectionPreviewController _controller;
		private CollectionAdditiveApplicationService _workflow;
		private NexusCollectionPreviewSnapshot _snapshot;
		private CollectionAdditiveWorkflowPreparationResult _preparation;
		private CollectionMemberAcquisitionBatch _acquisitionBatch;
		private CollectionOperationIdentity _operationIdentity;
		private CollectionOperation _operationSnapshot;
		private CollectionPlanIdentity _reviewedPlanIdentity;
		private IReadOnlyList<CollectionAdditiveWorkflowRecoveryResult> _recoveryResults = new CollectionAdditiveWorkflowRecoveryResult[0];
		private CancellationTokenSource _previewCancellation;
		private CancellationTokenSource _workflowCancellation;
		private int _previewGeneration;
		private bool _initialized;
		private bool _workflowBusy;
		private bool _selectionDirty;
		private bool _suppressMemberCheckEvents;

		/// <summary>
		/// Raised on the UI thread when an incoming Collection NXM request should bring this permanent document forward.
		/// </summary>
		public event EventHandler PreviewActivated = delegate { };

		public CollectionsPreviewControl()
		{
			Text = L("Collections.Title", "Collections");
			Name = "CollectionsDocument";
			HideOnClose = true;
			AutoScaleMode = AutoScaleMode.Font;
			BackColor = SystemColors.Window;

			var root = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 6,
				Padding = new Padding(8)
			};
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82F));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			Controls.Add(root);

			var toolbar = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill,
				AutoSize = true,
				WrapContents = true,
				FlowDirection = FlowDirection.LeftToRight,
				Padding = new Padding(0, 0, 0, 6)
			};
			_importButton = new Button
			{
				AutoSize = true,
				Text = L("Collections.Actions.Import", "Import bundle / collection.json..."),
				Enabled = false
			};
			_importButton.Click += ImportButton_Click;
			_downloadPrepareButton = new Button
			{
				AutoSize = true,
				Text = L("Collections.Actions.DownloadPrepare", "Download / Prepare"),
				Enabled = false
			};
			_downloadPrepareButton.Click += DownloadPrepareButton_Click;
			_resumeButton = new Button
			{
				AutoSize = true,
				Text = L("Collections.Actions.ResumePreparation", "Check / Resume preparation"),
				Enabled = false,
				Visible = false
			};
			_resumeButton.Click += ResumeButton_Click;
			_openPendingButton = new Button
			{
				AutoSize = true,
				Text = L("Collections.Actions.OpenDownloadPage", "Open pending download page"),
				Enabled = false,
				Visible = false
			};
			_openPendingButton.Click += OpenPendingButton_Click;
			_installButton = new Button
			{
				AutoSize = true,
				Text = L("Collections.Actions.InstallCurrent", "Install into current setup"),
				Enabled = false
			};
			_installButton.Click += InstallButton_Click;
			_clearButton = new Button
			{
				AutoSize = true,
				Text = L("Collections.Actions.ClearPreview", "Clear")
			};
			_clearButton.Click += ClearButton_Click;
			_instructionLabel = new Label
			{
				AutoSize = true,
				MaximumSize = new Size(760, 0),
				Padding = new Padding(12, 6, 0, 0),
				Text = L("Collections.Preview.Instructions", "Open a Nexus Collection NXM link, download or import its exact bundle, choose supported optional members, prepare the review, then explicitly approve installation.")
			};
			toolbar.Controls.Add(_importButton);
			toolbar.Controls.Add(_downloadPrepareButton);
			toolbar.Controls.Add(_resumeButton);
			toolbar.Controls.Add(_openPendingButton);
			toolbar.Controls.Add(_installButton);
			toolbar.Controls.Add(_clearButton);
			toolbar.Controls.Add(_instructionLabel);
			root.Controls.Add(toolbar, 0, 0);

			var header = new TableLayoutPanel
			{
				Dock = DockStyle.Top,
				AutoSize = true,
				ColumnCount = 4,
				RowCount = 4,
				Padding = new Padding(0, 0, 0, 6)
			};
			header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
			header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

			_collectionValue = AddHeaderRow(header, 0, 0, L("Collections.Fields.Collection", "Collection:"));
			_curatorValue = AddHeaderRow(header, 0, 2, L("Collections.Fields.Curator", "Curator:"));
			_locatorValue = AddHeaderRow(header, 1, 0, L("Collections.Fields.Locator", "Nexus locator:"));
			_revisionValue = AddHeaderRow(header, 1, 2, L("Collections.Fields.Revision", "Revision:"));
			_compatibilityValue = AddHeaderRow(header, 2, 0, L("Collections.Status.Compatibility", "Compatibility:"));
			_contentValue = AddHeaderRow(header, 2, 2, L("Collections.Status.Content", "Content readiness:"));
			_appliedValue = AddHeaderRow(header, 3, 0, L("Collections.Status.Applied", "Applied state:"));
			header.SetColumnSpan(_appliedValue, 3);
			root.Controls.Add(header, 0, 1);

			_summaryBox = new TextBox
			{
				Dock = DockStyle.Fill,
				Multiline = true,
				ReadOnly = true,
				ScrollBars = ScrollBars.Vertical,
				BackColor = SystemColors.Window,
				Text = L("Collections.Preview.EmptySummary", "No Collection preview is loaded.")
			};
			root.Controls.Add(_summaryBox, 0, 2);

			_workflowStatusLabel = new Label
			{
				AutoSize = true,
				MaximumSize = new Size(1200, 0),
				Padding = new Padding(0, 3, 0, 6),
				Text = L("Collections.Workflow.Idle", "Workflow: idle")
			};
			root.Controls.Add(_workflowStatusLabel, 0, 3);

			var splitHeaders = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				AutoSize = true,
				ColumnCount = 2
			};
			splitHeaders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
			splitHeaders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
			_membersHeader = new Label { AutoSize = true, Text = L("Collections.Preview.Members", "Members") };
			_issuesHeader = new Label { AutoSize = true, Text = L("Collections.Preview.Issues", "Review / issues") };
			splitHeaders.Controls.Add(_membersHeader, 0, 0);
			splitHeaders.Controls.Add(_issuesHeader, 1, 0);
			root.Controls.Add(splitHeaders, 0, 4);

			var contentGrid = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 2,
				RowCount = 1
			};
			contentGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
			contentGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));

			_membersView = CreateListView();
			_membersView.CheckBoxes = true;
			_membersView.ItemCheck += MembersView_ItemCheck;
			_membersView.Columns.Add(L("Collections.Columns.Member", "Member"), 210);
			_membersView.Columns.Add(L("Collections.Columns.Requirement", "Requirement"), 88);
			_membersView.Columns.Add(L("Collections.Columns.Selection", "Selection"), 82);
			_membersView.Columns.Add(L("Collections.Columns.Compatibility", "Compatibility"), 112);
			_membersView.Columns.Add(L("Collections.Columns.Artifact", "Artifact"), 240);
			contentGrid.Controls.Add(_membersView, 0, 0);

			_issuesView = CreateListView();
			_issuesView.Columns.Add(L("Collections.Columns.Status", "Status"), 105);
			_issuesView.Columns.Add(L("Collections.Columns.Code", "Code"), 180);
			_issuesView.Columns.Add(L("Collections.Columns.Field", "Subject"), 180);
			_issuesView.Columns.Add(L("Collections.Columns.Reason", "Reason / reviewed effect"), 420);
			contentGrid.Controls.Add(_issuesView, 1, 0);
			root.Controls.Add(contentGrid, 0, 5);

			RenderEmptyState();
		}

		/// <summary>Connects the surface to the incoming Collection dispatcher in preview-only compatibility mode.</summary>
		public void Initialize(NexusCollectionNxmDispatcher dispatcher)
		{
			Initialize(dispatcher, null);
		}

		/// <summary>Connects the surface to the incoming dispatcher and production additive workflow service.</summary>
		public void Initialize(NexusCollectionNxmDispatcher dispatcher, CollectionAdditiveApplicationService workflow)
		{
			if (ReferenceEquals(_dispatcher, dispatcher) && ReferenceEquals(_workflow, workflow) && _initialized)
				return;

			DetachDispatcher();
			CancelPreviewWork();
			CancelWorkflowWork();
			_snapshot = null;
			_workflow = workflow;
			ResetWorkflowViewState();
			RenderEmptyState();

			_dispatcher = dispatcher;
			_initialized = true;
			_controller = dispatcher == null ? null : new NexusCollectionPreviewController(dispatcher.Provider);
			if (_dispatcher != null)
				_dispatcher.DispatchCompleted += Dispatcher_DispatchCompleted;

			UpdateActionButtons();
			if (IsHandleCreated)
			{
				BeginInvoke((Action)DrainIncomingQueue);
				if (_workflow != null)
					BeginInvoke((Action)BeginRecoveryReconciliation);
			}
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			if (_initialized)
			{
				BeginInvoke((Action)DrainIncomingQueue);
				if (_workflow != null)
					BeginInvoke((Action)BeginRecoveryReconciliation);
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				DetachDispatcher();
				CancelPreviewWork();
				CancelWorkflowWork();
			}
			base.Dispose(disposing);
		}

		private void Dispatcher_DispatchCompleted(object sender, NexusCollectionNxmDispatchCompletedEventArgs e)
		{
			if (IsDisposed || Disposing || !IsHandleCreated)
				return;
			try
			{
				BeginInvoke((Action)DrainIncomingQueue);
			}
			catch (InvalidOperationException)
			{
				// The bounded dispatcher queue preserves the result until the UI handle is available again.
			}
		}

		private void DrainIncomingQueue()
		{
			if (_dispatcher == null || IsDisposed || Disposing)
				return;
			NexusCollectionNxmDispatchResult result;
			NexusCollectionNxmDispatchResult newest = null;
			while (_dispatcher.TryDequeueCompleted(out result))
				newest = result;
			if (newest != null)
				BeginRemotePreview(newest);
		}

		private async void BeginRemotePreview(NexusCollectionNxmDispatchResult dispatch)
		{
			if (_controller == null)
				return;

			int generation = ++_previewGeneration;
			CancelPreviewWork(false);
			_previewCancellation = new CancellationTokenSource();
			CancellationToken token = _previewCancellation.Token;

			_snapshot = null;
			ResetWorkflowViewState();
			ShowLoading(dispatch.Link);
			UpdateActionButtons();
			PreviewActivated(this, EventArgs.Empty);

			try
			{
				NexusCollectionPreviewSnapshot snapshot = await _controller.CreateFromDispatchAsync(dispatch, token);
				if (token.IsCancellationRequested || generation != _previewGeneration || IsDisposed)
					return;
				_snapshot = snapshot;
				RenderSnapshot(snapshot);
				BindMatchingRecovery();
			}
			catch (OperationCanceledException)
			{
				// A newer Collection selector superseded this metadata request.
			}
			catch (Exception ex)
			{
				if (generation != _previewGeneration || IsDisposed)
					return;
				Trace.TraceError("Collection preview metadata failed: " + ex);
				RenderUnexpectedFailure(dispatch.Link, ex);
			}
		}

		private async void ImportButton_Click(object sender, EventArgs e)
		{
			NexusCollectionPreviewSnapshot sourceSnapshot = _snapshot;
			if (sourceSnapshot == null || !sourceSnapshot.HasConcreteRevision)
			{
				_summaryBox.Text = L("Collections.Preview.ImportNeedsRevision", "Resolve a concrete Nexus Collection revision before importing its bundle or collection.json.");
				return;
			}

			using (var dialog = new OpenFileDialog())
			{
				dialog.Title = L("Collections.Preview.ImportTitle", "Import Nexus Collection bundle");
				dialog.CheckFileExists = true;
				dialog.CheckPathExists = true;
				dialog.Multiselect = false;
				dialog.Filter = L("Collections.Preview.ImportFilter", "Collection bundle or manifest|*.zip;*.7z;*.rar;collection.json|All files|*.*");
				if (dialog.ShowDialog(this) != DialogResult.OK)
					return;

				int generation = _previewGeneration;
				CancellationToken token = BeginWorkflowWork(L("Collections.Workflow.Importing", "Importing and retaining exact Collection source..."));
				try
				{
					NexusCollectionPreviewSnapshot imported;
					if (_workflow != null)
					{
						string sourcePath = dialog.FileName;
						imported = await Task.Run(() => _workflow.ImportAndRetainLocalBundle(sourceSnapshot, sourcePath, token), token);
					}
					else if (_controller != null)
					{
						string sourcePath = dialog.FileName;
						imported = await Task.Run(() => _controller.ImportFile(sourceSnapshot, sourcePath), token);
					}
					else
						return;

					if (token.IsCancellationRequested || generation != _previewGeneration || IsDisposed)
						return;
					_snapshot = imported;
					ResetWorkflowViewState();
					RenderSnapshot(imported);
					_workflowStatusLabel.Text = L("Collections.Workflow.SourceRetained", "Workflow: exact Collection source retained; choose optionals and prepare the review.");
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception ex)
				{
					Trace.TraceError("Collection bundle import failed: " + ex);
					_contentValue.Text = L("Collections.Status.Content.ImportFailed", "Bundle import failed");
					AddIssueRow(L("Collections.Status.ActionRequired", "Action required"), "bundle.import-failed", string.Empty, ex.Message);
				}
				finally
				{
					EndWorkflowWork();
				}
			}
		}

		private async void DownloadPrepareButton_Click(object sender, EventArgs e)
		{
			if (_workflow == null || _snapshot == null || !_snapshot.HasConcreteRevision)
				return;

			CancellationToken token = BeginWorkflowWork(L("Collections.Workflow.Preparing", "Preparing exact additive Collection review..."));
			try
			{
				if (_operationIdentity != null && (_operationSnapshot == null || !_operationSnapshot.HasCrossedNativeBoundary))
					CancelSupersededPreparation();

				NexusCollectionPreviewSnapshot snapshot = _snapshot;
				if (!snapshot.HasManifestPreview)
				{
					_workflowStatusLabel.Text = L("Collections.Workflow.Downloading", "Downloading and retaining the exact Collection bundle...");
					snapshot = await _workflow.DownloadAndRetainBundleAsync(snapshot, token);
					if (token.IsCancellationRequested || IsDisposed)
						return;
					_snapshot = snapshot;
					RenderSnapshot(snapshot);
				}

				CollectionEffectiveSelection selection = _workflow.BuildEffectiveSelection(snapshot, BuildOptionalSelections());
				_selectionDirty = false;
				CollectionAdditiveWorkflowPreparationResult result = await _workflow.PrepareAsync(selection, ConfirmArchiveOverwrite, token);
				if (token.IsCancellationRequested || IsDisposed)
					return;
				RenderPreparationResult(result);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection additive preparation failed: " + ex);
				_workflowStatusLabel.Text = L("Collections.Workflow.PreparationFailed", "Workflow: preparation failed.");
				AddIssueRow(L("Collections.Status.ActionRequired", "Action required"), "workflow.prepare-failed", string.Empty, ex.Message);
			}
			finally
			{
				EndWorkflowWork();
			}
		}

		private async void ResumeButton_Click(object sender, EventArgs e)
		{
			if (_workflow == null || _acquisitionBatch == null)
				return;
			CancellationToken token = BeginWorkflowWork(L("Collections.Workflow.ResumingPreparation", "Checking acquired member archives and resuming preparation..."));
			try
			{
				CollectionAdditiveWorkflowPreparationResult result = await _workflow.ResumePreparationAsync(_acquisitionBatch, token);
				if (token.IsCancellationRequested || IsDisposed)
					return;
				RenderPreparationResult(result);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection preparation resume failed: " + ex);
				_workflowStatusLabel.Text = L("Collections.Workflow.ResumeFailed", "Workflow: preparation resume failed.");
				AddIssueRow(L("Collections.Status.ActionRequired", "Action required"), "workflow.resume-failed", string.Empty, ex.Message);
			}
			finally
			{
				EndWorkflowWork();
			}
		}

		private void OpenPendingButton_Click(object sender, EventArgs e)
		{
			CollectionManualAcquisitionPendingAction pending = GetSelectedOrFirstPendingAction();
			if (pending == null || pending.BrowserUri == null)
				return;
			try
			{
				Process.Start(pending.BrowserUri.ToString());
			}
			catch (Exception ex)
			{
				AddIssueRow(L("Collections.Status.ActionRequired", "Action required"), "acquisition.open-page-failed", pending.Request.MemberKey.ToString(), ex.Message);
			}
		}

		private async void InstallButton_Click(object sender, EventArgs e)
		{
			if (_workflow == null || _operationIdentity == null || _reviewedPlanIdentity == null || _selectionDirty)
				return;

			CollectionAdditiveWorkflowReview review;
			try
			{
				_workflowStatusLabel.Text = L("Collections.Workflow.ValidatingReview", "Revalidating exact reviewed plan before approval...");
				SetWorkflowBusy(true);
				review = await Task.Run(() => _workflow.GetReview(_operationIdentity));
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection review reload failed: " + ex);
				AddIssueRow(L("Collections.Status.ActionRequired", "Action required"), "workflow.review-invalid", string.Empty, ex.Message);
				_workflowStatusLabel.Text = L("Collections.Workflow.ReviewInvalid", "Workflow: the reviewed plan must be prepared again.");
				return;
			}
			finally
			{
				SetWorkflowBusy(false);
			}

			if (!review.IsReady || review.Operation.PlanIdentity == null || !review.Operation.PlanIdentity.Equals(_reviewedPlanIdentity))
			{
				_workflowStatusLabel.Text = L("Collections.Workflow.ReviewInvalid", "Workflow: the reviewed plan changed or is no longer safely resumable; prepare it again.");
				_installButton.Enabled = false;
				return;
			}

			_operationSnapshot = review.Operation;
			RenderExactReview(review);
			string confirmation = BuildApprovalConfirmation(review);
			if (MessageBox.Show(this, confirmation, L("Collections.Actions.InstallCurrent", "Install into current setup"),
				MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
				return;

			CancellationToken token = BeginWorkflowWork(L("Collections.Workflow.Applying", "Applying the exact approved additive Collection plan..."));
			try
			{
				CollectionOperationIdentity operationIdentity = _operationIdentity;
				CollectionPlanIdentity planIdentity = _reviewedPlanIdentity;
				CollectionAdditiveWorkflowApplyResult result = await Task.Run(() => _workflow.ApproveAndApplyAsync(operationIdentity, planIdentity, token), token);
				if (token.IsCancellationRequested || IsDisposed)
					return;
				RenderApplyResult(result);
			}
			catch (OperationCanceledException)
			{
				_workflowStatusLabel.Text = L("Collections.Workflow.CancelRequested", "Workflow: cancellation requested; durable native reality will be reconciled before any further work.");
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection additive apply failed: " + ex);
				_workflowStatusLabel.Text = L("Collections.Workflow.ApplyFailed", "Workflow: apply failed; recovery may be required.");
				AddIssueRow(L("Collections.Status.ActionRequired", "Action required"), "workflow.apply-failed", string.Empty, ex.Message);
			}
			finally
			{
				EndWorkflowWork();
			}
		}

		private void ClearButton_Click(object sender, EventArgs e)
		{
			++_previewGeneration;
			CancelPreviewWork();
			CancelWorkflowWork();
			TryCancelUnappliedPreparation();
			_snapshot = null;
			ResetWorkflowViewState();
			RenderEmptyState();
			UpdateActionButtons();
		}

		private async void BeginRecoveryReconciliation()
		{
			if (_workflow == null || _workflowBusy || IsDisposed || Disposing)
				return;
			CancellationToken token = BeginWorkflowWork(L("Collections.Workflow.Recovering", "Checking incomplete Collection operations for the active target..."));
			try
			{
				IReadOnlyList<CollectionAdditiveWorkflowRecoveryResult> results = await _workflow.ReconcileIncompleteTargetAsync(token);
				if (token.IsCancellationRequested || IsDisposed)
					return;
				_recoveryResults = results ?? new CollectionAdditiveWorkflowRecoveryResult[0];
				if (_snapshot != null)
				{
					RenderIssues(_snapshot);
					BindMatchingRecovery();
				}
				else if (_recoveryResults.Count > 0)
				{
					_workflowStatusLabel.Text = LanguageManager.Format("Collections.Workflow.IncompleteCount", "Workflow: {0} incomplete Collection operation(s) reconciled for this target. Open the matching revision to review/resume.", _recoveryResults.Count);
				}
				else
					_workflowStatusLabel.Text = L("Collections.Workflow.Idle", "Workflow: idle");
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection startup reconciliation failed: " + ex);
				_workflowStatusLabel.Text = L("Collections.Workflow.RecoveryFailed", "Workflow: incomplete-operation reconciliation failed.");
				AddIssueRow(L("Collections.Status.ActionRequired", "Action required"), "workflow.recovery-failed", string.Empty, ex.Message);
			}
			finally
			{
				EndWorkflowWork();
			}
		}

		private void MembersView_ItemCheck(object sender, ItemCheckEventArgs e)
		{
			if (_suppressMemberCheckEvents || e.Index < 0 || e.Index >= _membersView.Items.Count)
				return;
			NormalizedCollectionMember member = _membersView.Items[e.Index].Tag as NormalizedCollectionMember;
			if (member == null)
				return;
			if (member.Requirement == CollectionMemberRequirement.Required)
			{
				e.NewValue = CheckState.Checked;
				return;
			}
			if (!member.IdentityResolution.IsResolved)
			{
				e.NewValue = e.CurrentValue;
				return;
			}
			BeginInvoke((Action)(() =>
			{
				if (IsDisposed)
					return;
				_selectionDirty = true;
				if (_operationSnapshot == null || !_operationSnapshot.HasCrossedNativeBoundary)
				{
					TryCancelUnappliedPreparation();
					_operationIdentity = null;
					_operationSnapshot = null;
					_reviewedPlanIdentity = null;
					_preparation = null;
					_acquisitionBatch = null;
				}
				_installButton.Enabled = false;
				UpdateMemberSelectionText();
				_workflowStatusLabel.Text = L("Collections.Workflow.SelectionChanged", "Workflow: optional selection changed; prepare a new exact review before installation.");
				UpdateActionButtons();
			}));
		}

		private void RenderSnapshot(NexusCollectionPreviewSnapshot snapshot)
		{
			string selectedMember = GetSelectedMemberToken();
			int previousTopIndex = _membersView.TopItem == null ? -1 : _membersView.TopItem.Index;
			CollectionDefinition definition = snapshot.Definition;
			CollectionRevision revision = snapshot.Revision;
			NexusCollectionRevisionMetadata providerRevision = snapshot.RevisionLookup?.Revision;
			NexusCollectionSummaryMetadata providerSummary = snapshot.SummaryLookup?.Collection;

			_collectionValue.Text = FirstNonEmpty(definition?.DisplayName, providerSummary?.Name, snapshot.Link?.CollectionSlug, L("Collections.Value.Unknown", "Unknown"));
			_curatorValue.Text = FirstNonEmpty(definition?.AuthorDisplayName, providerSummary?.AuthorDisplayName, L("Collections.Value.Unknown", "Unknown"));
			_locatorValue.Text = snapshot.Link == null ? L("Collections.Value.Unknown", "Unknown") : snapshot.Link.GameDomain + " / " + snapshot.Link.CollectionSlug;
			if (revision != null)
			{
				_revisionValue.Text = LanguageManager.Format("Collections.Preview.ConcreteRevision", "#{0} (collection {1}, revision {2})",
					revision.Identity.NexusRevisionNumber, revision.Collection.StableId, revision.Identity.StableRevisionId);
			}
			else if (providerRevision != null && providerRevision.RevisionNumber.HasValue)
				_revisionValue.Text = LanguageManager.Format("Collections.Preview.PartialRevision", "#{0} (identity incomplete)", providerRevision.RevisionNumber.Value);
			else
				_revisionValue.Text = snapshot.Link != null && snapshot.Link.RevisionRequest.IsLatest
					? L("Collections.Preview.LatestUnresolved", "latest (not resolved)")
					: L("Collections.Value.Unknown", "Unknown");

			if (snapshot.HasManifestPreview)
			{
				_compatibilityValue.Text = FormatCompatibility(snapshot.CapabilityReport.Status);
				_contentValue.Text = _workflow == null
					? L("Collections.Status.Content.ManifestImported", "Manifest imported; member archives not prepared")
					: L("Collections.Status.Content.SourceReady", "Exact Collection source retained; member preparation not yet reviewed");
			}
			else
			{
				_compatibilityValue.Text = L("Collections.Status.Compatibility.NotEvaluated", "Not evaluated - manifest not imported");
				_contentValue.Text = snapshot.HasConcreteRevision
					? L("Collections.Status.Content.BundleNotImported", "Bundle not downloaded / imported")
					: L("Collections.Status.Content.WaitingRevision", "Waiting for concrete revision");
			}
			_appliedValue.Text = L("Collections.Status.Applied.NotApplied", "Not applied");

			string summary = definition?.Summary ?? providerSummary?.Summary;
			_summaryBox.Text = string.IsNullOrWhiteSpace(summary)
				? L("Collections.Preview.NoSummary", "No collection summary was returned. Decorative metadata is optional and does not establish identity or readiness.")
				: summary;

			RenderMembers(snapshot, selectedMember, previousTopIndex);
			RenderIssues(snapshot);
			UpdateActionButtons();
		}

		private void RenderMembers(NexusCollectionPreviewSnapshot snapshot, string selectedToken, int previousTopIndex)
		{
			_membersView.BeginUpdate();
			_suppressMemberCheckEvents = true;
			try
			{
				_membersView.Items.Clear();
				if (!snapshot.HasManifestPreview)
				{
					_membersHeader.Text = L("Collections.Preview.Members", "Members");
					return;
				}

				CollectionCapabilityReport report = snapshot.CapabilityReport;
				_membersHeader.Text = LanguageManager.Format("Collections.Preview.MemberCount", "Members ({0})", report.Manifest.Members.Count);
				foreach (CollectionMemberCapabilityReport memberReport in report.MemberReports)
				{
					NormalizedCollectionMember member = memberReport.Member;
					string token = MemberToken(member);
					string displayName = string.IsNullOrWhiteSpace(member.DisplayName) ? token : member.DisplayName;
					var item = new ListViewItem(displayName) { Tag = member, Checked = member.IsRequired || member.IsSelected };
					item.SubItems.Add(member.IsRequired ? L("Collections.Member.Required", "Required") : L("Collections.Member.Optional", "Optional"));
					item.SubItems.Add(item.Checked ? L("Collections.Member.Selected", "Selected") : L("Collections.Member.Unselected", "Not selected"));
					item.SubItems.Add(FormatCompatibility(memberReport.Status));
					item.SubItems.Add(member.Artifact == null ? L("Collections.Value.Unresolved", "Unresolved") : member.Artifact.ToString());
					_membersView.Items.Add(item);
				}

				if (!string.IsNullOrEmpty(selectedToken))
				{
					foreach (ListViewItem item in _membersView.Items)
					{
						NormalizedCollectionMember member = item.Tag as NormalizedCollectionMember;
						if (member != null && StringComparer.Ordinal.Equals(MemberToken(member), selectedToken))
						{
							item.Selected = true;
							item.Focused = true;
							break;
						}
					}
				}
				if (previousTopIndex >= 0 && _membersView.Items.Count > 0)
					_membersView.EnsureVisible(Math.Min(previousTopIndex, _membersView.Items.Count - 1));
			}
			finally
			{
				_suppressMemberCheckEvents = false;
				_membersView.EndUpdate();
			}
		}

		private void RenderIssues(NexusCollectionPreviewSnapshot snapshot)
		{
			_issuesView.BeginUpdate();
			try
			{
				_issuesView.Items.Clear();
				if (snapshot.RevisionError != null)
					AddIssueRow(L("Collections.Status.ActionRequired", "Action required"), "provider.revision-failed", string.Empty, snapshot.RevisionError.Message);
				if (snapshot.SummaryError != null)
					AddIssueRow(L("Collections.Status.ActionRequired", "Action required"), "provider.summary-failed", string.Empty, snapshot.SummaryError.Message);
				if (!string.IsNullOrWhiteSpace(snapshot.MetadataWarning))
					AddIssueRow(L("Collections.Status.ActionRequired", "Action required"), "provider.identity-mismatch", string.Empty, snapshot.MetadataWarning);
				AddGraphQlErrors("provider.revision", snapshot.RevisionLookup?.Errors);
				AddGraphQlErrors("provider.summary", snapshot.SummaryLookup?.Errors);
				if (snapshot.HasManifestPreview)
				{
					foreach (CollectionCapabilityIssue issue in snapshot.CapabilityReport.AllIssues)
						AddIssueRow(FormatCompatibility(issue.Status), issue.Code, issue.FieldPath ?? string.Empty, issue.Reason);
				}
				if (!snapshot.HasConcreteRevision && snapshot.RevisionError == null)
					AddIssueRow(L("Collections.Status.ActionRequired", "Action required"), "provider.identity-incomplete", string.Empty,
						L("Collections.Preview.IdentityIncomplete", "The provider response did not contain the stable collection and revision identity required for a trusted manifest preview."));
				AppendRecoveryIssues();
				UpdateIssuesHeader();
			}
			finally
			{
				_issuesView.EndUpdate();
			}
		}

		private void RenderPreparationResult(CollectionAdditiveWorkflowPreparationResult result)
		{
			_preparation = result ?? throw new ArgumentNullException(nameof(result));
			_acquisitionBatch = result.AcquisitionBatch;
			_operationIdentity = result.Operation.Identity;
			_operationSnapshot = result.Operation;
			_reviewedPlanIdentity = result.IsReadyForReview ? result.Operation.PlanIdentity : null;
			_selectionDirty = false;
			if (_snapshot != null)
				RenderIssues(_snapshot);

			AddIssueRow(FormatPreparationStatus(result.Status), "workflow.preparation", result.Operation.Identity.ToString(), result.Message);
			AppendAcquisitionReview(result.AcquisitionBatch);
			AppendDependencyReview(result.DependencyPlan);
			AppendImpactReview(result.ImpactPlan);
			UpdateIssuesHeader();

			switch (result.Status)
			{
				case CollectionAdditiveWorkflowPreparationStatus.AwaitingInput:
					_contentValue.Text = L("Collections.Status.Content.AwaitingInput", "Member archives awaiting download / user input");
					_appliedValue.Text = L("Collections.Status.Applied.Preparing", "Not applied - preparation paused for input");
					break;
				case CollectionAdditiveWorkflowPreparationStatus.ReadyForReview:
					_contentValue.Text = L("Collections.Status.Content.ReviewReady", "All required content verified; exact impact review ready");
					_appliedValue.Text = LanguageManager.Format("Collections.Status.Applied.ReviewReady", "Not applied - plan {0} awaits explicit approval", result.Operation.PlanIdentity);
					break;
				case CollectionAdditiveWorkflowPreparationStatus.ActionRequired:
					_contentValue.Text = L("Collections.Status.Content.ActionRequired", "Prepared content requires a durable user decision not supported by Gate A");
					_appliedValue.Text = L("Collections.Status.Applied.Blocked", "Not applied - action required");
					break;
				case CollectionAdditiveWorkflowPreparationStatus.PreparationRequired:
					_contentValue.Text = L("Collections.Status.Content.PreparationRequired", "Preparation must be refreshed before review");
					_appliedValue.Text = L("Collections.Status.Applied.NotApplied", "Not applied");
					break;
				default:
					_contentValue.Text = L("Collections.Status.Content.Blocked", "Preparation blocked");
					_appliedValue.Text = L("Collections.Status.Applied.Blocked", "Not applied - blocked");
					break;
			}
			_workflowStatusLabel.Text = "Workflow: " + result.Message;
			UpdateActionButtons();
		}

		private void RenderApplyResult(CollectionAdditiveWorkflowApplyResult result)
		{
			if (result == null)
				return;
			_operationIdentity = result.Operation.Identity;
			_operationSnapshot = result.Operation;
			_workflowStatusLabel.Text = "Workflow: " + result.Message;
			AddIssueRow(result.IsCommitted ? L("Collections.Status.Supported", "Ready") : L("Collections.Status.ActionRequired", "Action required"),
				"workflow.apply-result", result.Status.ToString(), result.Message);
			UpdateIssuesHeader();
			if (result.IsCommitted)
			{
				_appliedValue.Text = L("Collections.Status.Applied.Applied", "Applied and verified");
				_contentValue.Text = L("Collections.Status.Content.Applied", "Prepared content applied and verified");
				_reviewedPlanIdentity = null;
				_acquisitionBatch = null;
				_preparation = null;
			}
			else if (result.Status == CollectionAdditiveWorkflowApplyStatus.PausedAtSafeBoundary)
				_appliedValue.Text = L("Collections.Status.Applied.Paused", "Partially applied - paused at verified safe boundary");
			else if (result.Status == CollectionAdditiveWorkflowApplyStatus.RecoveryRequired)
				_appliedValue.Text = L("Collections.Status.Applied.RecoveryRequired", "Recovery required before further mutation");
			else if (result.Status == CollectionAdditiveWorkflowApplyStatus.RepreparationRequired)
				_appliedValue.Text = L("Collections.Status.Applied.Reprepare", "Not applied - preparation must be rebuilt");
			else
				_appliedValue.Text = L("Collections.Status.Applied.StoppedPartial", "Stopped after partial verified progress");
			UpdateActionButtons();
		}

		private void AppendAcquisitionReview(CollectionMemberAcquisitionBatch batch)
		{
			if (batch == null)
				return;
			foreach (CollectionMemberAcquisitionState state in batch.Members)
			{
				string subject = state.Match.Member.MemberKey.ToString();
				string reason = state.Disposition.ToString();
				if (state.PendingAction != null)
					reason += " - manual/free input supported: " + state.PendingAction.AllowedActions;
				AddIssueRow(state.IsReady ? L("Collections.Status.Supported", "Ready") : L("Collections.Status.ActionRequired", "Action required"),
					"acquisition." + state.Disposition.ToString().ToLowerInvariant(), subject, reason);
			}
		}

		private void AppendDependencyReview(CollectionDependencyPhasePlan dependencyPlan)
		{
			if (dependencyPlan == null)
				return;
			foreach (CollectionExecutionPhase phase in dependencyPlan.Phases)
			{
				string members = string.Join(", ", phase.Members.Select(x => x.MemberKey.ToString()));
				AddIssueRow(L("Collections.Status.Supported", "Review"), "dependency.phase", phase.PhaseNumber.ToString(CultureInfo.InvariantCulture), members);
			}
			foreach (CollectionDependencyPhaseIssue issue in dependencyPlan.Issues)
				AddIssueRow(L("Collections.Status.Unsupported", "Blocked"), "dependency." + issue.Kind.ToString().ToLowerInvariant(),
					issue.MemberKey == null ? string.Empty : issue.MemberKey.ToString(), issue.Reason);
		}

		private void AppendImpactReview(CollectionConflictImpactPlan impactPlan)
		{
			if (impactPlan == null)
				return;
			foreach (CollectionConflictImpactIssue issue in impactPlan.Issues)
				AddIssueRow(FormatImpactStatus(issue.Status), "impact." + issue.Kind.ToString().ToLowerInvariant(), issue.SubjectKey, issue.Message);
			foreach (CollectionFileImpact impact in impactPlan.FileImpacts)
			{
				string winner = impact.PlannedWinner == null ? "none" : impact.PlannedWinner.ToString();
				string writers = string.Join(", ", impact.Writers.Select(x => x.ToString()));
				AddIssueRow(L("Collections.Status.Supported", "Review"), "impact.file", impact.Target.ToString(),
					"Reviewed winner: " + winner + "; writers: " + writers + "; current owner: " + (impact.CurrentOwnerKey ?? "none"));
			}
			foreach (CollectionPluginImpact impact in impactPlan.PluginImpacts)
				AddIssueRow(L("Collections.Status.Supported", "Review"), "impact.plugin", string.Join(", ", impact.Effect.PluginPaths),
					impact.MemberKey + " -> " + impact.Effect.Kind + (impact.Effect.Active.HasValue ? " active=" + impact.Effect.Active.Value : string.Empty));
			foreach (CollectionConfigurationImpact impact in impactPlan.ConfigurationImpacts)
				AddIssueRow(L("Collections.Status.Supported", "Review"), "impact.configuration", impact.SubjectKey,
					impact.MemberKey + " -> " + impact.Kind + "; current owner: " + (impact.CurrentOwnerKey ?? "none"));
			foreach (CollectionAssociationImpact impact in impactPlan.AssociationImpacts)
				AddIssueRow(L("Collections.Status.Supported", "Review"), "impact.association", impact.Association.Revision.ToString(), impact.Kind.ToString());
		}

		private void AppendRecoveryIssues()
		{
			foreach (CollectionAdditiveWorkflowRecoveryResult recovery in _recoveryResults)
			{
				AddIssueRow(FormatRecoveryStatus(recovery.Status), "recovery." + recovery.Status.ToString().ToLowerInvariant(),
					recovery.Operation.Revision == null ? recovery.Operation.Identity.ToString() : recovery.Operation.Revision.ToString(), recovery.Message);
			}
		}

		private void BindMatchingRecovery()
		{
			if (_snapshot == null || _snapshot.Revision == null || _recoveryResults == null)
				return;
			CollectionAdditiveWorkflowRecoveryResult matching = _recoveryResults.FirstOrDefault(x =>
				x != null && x.Operation != null && x.Operation.Revision != null && x.Operation.Revision.Equals(_snapshot.Revision.Identity) &&
				(x.Status == CollectionAdditiveWorkflowRecoveryStatus.ReviewRequired || x.Status == CollectionAdditiveWorkflowRecoveryStatus.ReadyToResume));
			if (matching == null || matching.Operation.PlanIdentity == null)
				return;
			_operationIdentity = matching.Operation.Identity;
			_operationSnapshot = matching.Operation;
			_reviewedPlanIdentity = matching.Operation.PlanIdentity;
			_selectionDirty = false;
			_workflowStatusLabel.Text = "Workflow: " + matching.Message;
			_appliedValue.Text = matching.Status == CollectionAdditiveWorkflowRecoveryStatus.ReadyToResume
				? L("Collections.Status.Applied.ResumeReady", "Incomplete apply reconciled - explicit resume available")
				: L("Collections.Status.Applied.ReviewReady", "Recovered review awaits explicit approval");
			UpdateActionButtons();
		}

		private IEnumerable<CollectionOptionalMemberSelection> BuildOptionalSelections()
		{
			var result = new List<CollectionOptionalMemberSelection>();
			foreach (ListViewItem item in _membersView.Items)
			{
				NormalizedCollectionMember member = item.Tag as NormalizedCollectionMember;
				if (member == null || member.Requirement != CollectionMemberRequirement.Optional || !member.IdentityResolution.IsResolved)
					continue;
				result.Add(new CollectionOptionalMemberSelection(member.IdentityResolution.Key,
					item.Checked ? CollectionMemberSelection.Selected : CollectionMemberSelection.Unselected));
			}
			return result;
		}

		private CollectionManualAcquisitionPendingAction GetSelectedOrFirstPendingAction()
		{
			if (_acquisitionBatch == null)
				return null;
			NormalizedCollectionMember selected = _membersView.SelectedItems.Count == 0 ? null : _membersView.SelectedItems[0].Tag as NormalizedCollectionMember;
			if (selected != null && selected.IdentityResolution.IsResolved)
			{
				CollectionMemberAcquisitionState selectedState = _acquisitionBatch.Members.FirstOrDefault(x => x.Match.Member.MemberKey.Equals(selected.IdentityResolution.Key));
				if (selectedState != null && selectedState.PendingAction != null && selectedState.PendingAction.BrowserUri != null)
					return selectedState.PendingAction;
			}
			return _acquisitionBatch.Members.Where(x => x.PendingAction != null && x.PendingAction.BrowserUri != null)
				.Select(x => x.PendingAction).FirstOrDefault();
		}

		private void CancelSupersededPreparation()
		{
			if (_workflow == null || _operationIdentity == null)
				return;
			try
			{
				CollectionOperation cancelled = _workflow.CancelBeforeApply(_operationIdentity);
				if (cancelled.IsTerminal)
				{
					_operationIdentity = null;
					_operationSnapshot = null;
					_reviewedPlanIdentity = null;
					_preparation = null;
					_acquisitionBatch = null;
				}
			}
			catch (InvalidOperationException)
			{
				// Once native mutation has begun the durable operation must be recovered/resumed, never hidden by a UI reprepare.
				throw;
			}
		}

		private void TryCancelUnappliedPreparation()
		{
			if (_workflow == null || _operationIdentity == null)
				return;
			try
			{
				_workflow.CancelBeforeApply(_operationIdentity);
			}
			catch (InvalidOperationException)
			{
				// Applied/crossed-boundary work remains durable and will be surfaced by target reconciliation.
			}
		}

		private void RenderExactReview(CollectionAdditiveWorkflowReview review)
		{
			if (review == null || review.Runtime == null)
				return;
			if (_snapshot != null)
				RenderIssues(_snapshot);
			AddIssueRow(L("Collections.Status.Supported", "Ready"), "workflow.exact-review", review.Operation.PlanIdentity.ToString(),
				"Exact reviewed plan revalidated against current native state.");
			AppendDependencyReview(review.Runtime.DependencyPlan);
			AppendImpactReview(review.Runtime.ImpactPlan);
			UpdateIssuesHeader();
		}

		private string BuildApprovalConfirmation(CollectionAdditiveWorkflowReview review)
		{
			var text = new StringBuilder();
			text.AppendLine(L("Collections.Review.ConfirmHeading", "Apply this exact reviewed Collection plan to the current setup?"));
			text.AppendLine();
			text.AppendLine("Plan: " + review.Operation.PlanIdentity);
			if (review.Runtime != null)
			{
				text.AppendLine("Selected members: " + review.Runtime.Plan.SelectedMembers.Count.ToString(CultureInfo.InvariantCulture));
				text.AppendLine("File impacts: " + review.Runtime.ImpactPlan.FileImpacts.Count.ToString(CultureInfo.InvariantCulture));
				text.AppendLine("Plugin impacts: " + review.Runtime.ImpactPlan.PluginImpacts.Count.ToString(CultureInfo.InvariantCulture));
				text.AppendLine("Configuration impacts: " + review.Runtime.ImpactPlan.ConfigurationImpacts.Count.ToString(CultureInfo.InvariantCulture));
				text.AppendLine("Affected existing Collection associations: " + review.Runtime.ImpactPlan.AssociationImpacts.Count.ToString(CultureInfo.InvariantCulture));
			}
			text.AppendLine();
			text.AppendLine(L("Collections.Review.ConfirmDetail", "The complete reviewed effects are listed in the Review / issues pane. NMM will apply only this exact plan version; any changed native state invalidates the review."));
			return text.ToString();
		}

		private bool ConfirmArchiveOverwrite(string oldPath, out string newPath)
		{
			if (InvokeRequired)
			{
				bool accepted = false;
				string resolved = null;
				Invoke((MethodInvoker)(() => accepted = ConfirmArchiveOverwrite(oldPath, out resolved)));
				newPath = resolved;
				return accepted;
			}

			string candidate = oldPath;
			if (File.Exists(oldPath))
			{
				string extension = Path.GetExtension(oldPath);
				string directory = Path.GetDirectoryName(oldPath);
				for (int index = 2; index < Int32.MaxValue && File.Exists(candidate); index++)
					candidate = Path.Combine(directory, String.Format(CultureInfo.InvariantCulture, "{0} ({1}){2}", Path.GetFileNameWithoutExtension(oldPath), index, extension));
				if (File.Exists(candidate))
					throw new IOException("Cannot write the acquired mod archive because no unused filename could be selected.");

				DialogResult choice = MessageBox.Show(this,
					LanguageManager.Format("Collections.Acquisition.Overwrite", "A mod archive already exists at:\r\n{0}\r\n\r\nYes = overwrite, No = keep both using a new filename, Cancel = stop this acquisition.", oldPath),
					L("Collections.Acquisition.OverwriteTitle", "Collection member archive already exists"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
				if (choice == DialogResult.Cancel)
				{
					newPath = null;
					return false;
				}
				if (choice == DialogResult.Yes)
					candidate = oldPath;
			}
			newPath = candidate;
			return true;
		}

		private CancellationToken BeginWorkflowWork(string status)
		{
			CancelWorkflowWork();
			_workflowCancellation = new CancellationTokenSource();
			_workflowBusy = true;
			_workflowStatusLabel.Text = status;
			UpdateActionButtons();
			return _workflowCancellation.Token;
		}

		private void EndWorkflowWork()
		{
			_workflowBusy = false;
			UpdateActionButtons();
		}

		private void SetWorkflowBusy(bool busy)
		{
			_workflowBusy = busy;
			UpdateActionButtons();
		}

		private void CancelPreviewWork(bool incrementGeneration = true)
		{
			if (incrementGeneration)
				++_previewGeneration;
			if (_previewCancellation != null)
			{
				_previewCancellation.Cancel();
				_previewCancellation.Dispose();
				_previewCancellation = null;
			}
		}

		private void CancelWorkflowWork()
		{
			if (_workflowCancellation != null)
			{
				_workflowCancellation.Cancel();
				_workflowCancellation.Dispose();
				_workflowCancellation = null;
			}
			_workflowBusy = false;
		}

		private void ResetWorkflowViewState()
		{
			_preparation = null;
			_acquisitionBatch = null;
			_operationIdentity = null;
			_operationSnapshot = null;
			_reviewedPlanIdentity = null;
			_selectionDirty = false;
			_workflowStatusLabel.Text = _workflow == null
				? L("Collections.Workflow.PreviewOnly", "Workflow: preview only - additive application service is unavailable.")
				: L("Collections.Workflow.Idle", "Workflow: idle");
		}

		private void UpdateActionButtons()
		{
			bool hasConcreteRevision = _snapshot != null && _snapshot.HasConcreteRevision;
			_importButton.Enabled = !_workflowBusy && hasConcreteRevision;
			_downloadPrepareButton.Enabled = !_workflowBusy && _workflow != null && hasConcreteRevision &&
				(_acquisitionBatch == null || _selectionDirty || (_preparation != null &&
				 (_preparation.Status == CollectionAdditiveWorkflowPreparationStatus.PreparationRequired ||
				  _preparation.Status == CollectionAdditiveWorkflowPreparationStatus.ActionRequired ||
				  _preparation.Status == CollectionAdditiveWorkflowPreparationStatus.Blocked)));
			_resumeButton.Visible = _acquisitionBatch != null && !_acquisitionBatch.IsReady;
			_resumeButton.Enabled = !_workflowBusy && _resumeButton.Visible;
			_openPendingButton.Visible = _acquisitionBatch != null && _acquisitionBatch.Members.Any(x => x.PendingAction != null && x.PendingAction.BrowserUri != null);
			_openPendingButton.Enabled = !_workflowBusy && _openPendingButton.Visible;
			bool exactReview = _workflow != null && _operationIdentity != null && _reviewedPlanIdentity != null && !_selectionDirty;
			_installButton.Enabled = !_workflowBusy && exactReview;
			if (exactReview && _preparation == null)
				_installButton.Text = L("Collections.Actions.ResumeApply", "Review / Resume apply");
			else
				_installButton.Text = L("Collections.Actions.InstallCurrent", "Install into current setup");
			_membersView.Enabled = !_workflowBusy && (_operationSnapshot == null || (!_operationSnapshot.HasCrossedNativeBoundary && !_operationSnapshot.IsSuccessful));
		}

		private void UpdateMemberSelectionText()
		{
			foreach (ListViewItem item in _membersView.Items)
			{
				if (item.SubItems.Count > 2)
					item.SubItems[2].Text = item.Checked ? L("Collections.Member.Selected", "Selected") : L("Collections.Member.Unselected", "Not selected");
			}
		}

		private void ShowLoading(NexusCollectionNxmLink link)
		{
			_collectionValue.Text = link?.CollectionSlug ?? L("Collections.Value.Unknown", "Unknown");
			_curatorValue.Text = L("Collections.Value.Loading", "Loading...");
			_locatorValue.Text = link == null ? L("Collections.Value.Unknown", "Unknown") : link.GameDomain + " / " + link.CollectionSlug;
			_revisionValue.Text = link == null ? L("Collections.Value.Unknown", "Unknown") : link.RevisionRequest.IsLatest
				? L("Collections.Preview.ResolvingLatest", "Resolving latest...")
				: "#" + link.RevisionRequest.RevisionNumber.Value.ToString(CultureInfo.InvariantCulture);
			_compatibilityValue.Text = L("Collections.Value.Loading", "Loading...");
			_contentValue.Text = L("Collections.Status.Content.WaitingRevision", "Waiting for concrete revision");
			_appliedValue.Text = L("Collections.Status.Applied.NotApplied", "Not applied");
			_summaryBox.Text = L("Collections.Preview.Loading", "Resolving Collection metadata. No native mod state is being changed.");
			_membersView.Items.Clear();
			_issuesView.Items.Clear();
			_membersHeader.Text = L("Collections.Preview.Members", "Members");
			_issuesHeader.Text = L("Collections.Preview.Issues", "Review / issues");
		}

		private void RenderUnexpectedFailure(NexusCollectionNxmLink link, Exception exception)
		{
			_collectionValue.Text = link?.CollectionSlug ?? L("Collections.Value.Unknown", "Unknown");
			_curatorValue.Text = L("Collections.Value.Unknown", "Unknown");
			_locatorValue.Text = link == null ? L("Collections.Value.Unknown", "Unknown") : link.GameDomain + " / " + link.CollectionSlug;
			_revisionValue.Text = L("Collections.Value.Unresolved", "Unresolved");
			_compatibilityValue.Text = L("Collections.Status.ActionRequired", "Action required");
			_contentValue.Text = L("Collections.Status.Content.WaitingRevision", "Waiting for concrete revision");
			_appliedValue.Text = L("Collections.Status.Applied.NotApplied", "Not applied");
			_summaryBox.Text = exception.Message;
			_membersView.Items.Clear();
			_issuesView.Items.Clear();
			AddIssueRow(L("Collections.Status.ActionRequired", "Action required"), "preview.unexpected-failure", string.Empty, exception.Message);
			UpdateIssuesHeader();
		}

		private void RenderEmptyState()
		{
			_collectionValue.Text = "-";
			_curatorValue.Text = "-";
			_locatorValue.Text = "-";
			_revisionValue.Text = "-";
			_compatibilityValue.Text = L("Collections.Status.Compatibility.NotEvaluated", "Not evaluated - manifest not imported");
			_contentValue.Text = L("Collections.Status.Content.NoSelection", "No revision selected");
			_appliedValue.Text = L("Collections.Status.Applied.NotApplied", "Not applied");
			_summaryBox.Text = L("Collections.Preview.EmptySummary", "No Collection preview is loaded.");
			_membersView.Items.Clear();
			_issuesView.Items.Clear();
			_membersHeader.Text = L("Collections.Preview.Members", "Members");
			_issuesHeader.Text = L("Collections.Preview.Issues", "Review / issues");
		}

		private void AddGraphQlErrors(string codePrefix, IReadOnlyList<NexusGraphQlError> errors)
		{
			if (errors == null)
				return;
			for (int index = 0; index < errors.Count; index++)
			{
				NexusGraphQlError error = errors[index];
				if (error == null)
					continue;
				string code = string.IsNullOrWhiteSpace(error.Code) ? codePrefix : codePrefix + "." + error.Code;
				string field = error.Path == null ? string.Empty : string.Join(".", error.Path.Select(segment => Convert.ToString(segment, CultureInfo.InvariantCulture)));
				AddIssueRow(L("Collections.Status.ActionRequired", "Action required"), code, field, error.Message ?? L("Collections.Preview.ProviderError", "Nexus returned a GraphQL field error."));
			}
		}

		private void AddIssueRow(string status, string code, string field, string reason)
		{
			var item = new ListViewItem(status ?? string.Empty);
			item.SubItems.Add(code ?? string.Empty);
			item.SubItems.Add(field ?? string.Empty);
			item.SubItems.Add(reason ?? string.Empty);
			_issuesView.Items.Add(item);
		}

		private void UpdateIssuesHeader()
		{
			_issuesHeader.Text = LanguageManager.Format("Collections.Preview.IssueCount", "Review / issues ({0})", _issuesView.Items.Count);
		}

		private string GetSelectedMemberToken()
		{
			if (_membersView.SelectedItems.Count == 0)
				return null;
			NormalizedCollectionMember member = _membersView.SelectedItems[0].Tag as NormalizedCollectionMember;
			return member == null ? null : MemberToken(member);
		}

		private void DetachDispatcher()
		{
			if (_dispatcher != null)
				_dispatcher.DispatchCompleted -= Dispatcher_DispatchCompleted;
			_dispatcher = null;
			_controller = null;
		}

		private static string MemberToken(NormalizedCollectionMember member)
		{
			return member.IdentityResolution.IsResolved
				? member.IdentityResolution.Key.ToString()
				: "ordinal:" + member.SourceOrdinal.ToString(CultureInfo.InvariantCulture);
		}

		private static ListView CreateListView()
		{
			return new ListView
			{
				Dock = DockStyle.Fill,
				View = View.Details,
				FullRowSelect = true,
				HideSelection = false,
				MultiSelect = false,
				UseCompatibleStateImageBehavior = false
			};
		}

		private static Label AddHeaderRow(TableLayoutPanel table, int row, int column, string caption)
		{
			var name = new Label
			{
				AutoSize = true,
				Text = caption,
				Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
				Margin = new Padding(0, 3, 8, 3)
			};
			var value = new Label
			{
				AutoSize = true,
				Text = "-",
				Margin = new Padding(0, 3, 18, 3)
			};
			table.Controls.Add(name, column, row);
			table.Controls.Add(value, column + 1, row);
			return value;
		}

		private static string FormatCompatibility(CollectionCompatibilityStatus status)
		{
			switch (status)
			{
				case CollectionCompatibilityStatus.Supported: return L("Collections.Status.Supported", "Supported");
				case CollectionCompatibilityStatus.ActionRequired: return L("Collections.Status.ActionRequired", "Action required");
				case CollectionCompatibilityStatus.Unsupported: return L("Collections.Status.Unsupported", "Unsupported");
				default: return L("Collections.Value.Unknown", "Unknown");
			}
		}

		private static string FormatPreparationStatus(CollectionAdditiveWorkflowPreparationStatus status)
		{
			switch (status)
			{
				case CollectionAdditiveWorkflowPreparationStatus.ReadyForReview: return L("Collections.Status.Supported", "Ready");
				case CollectionAdditiveWorkflowPreparationStatus.Blocked: return L("Collections.Status.Unsupported", "Blocked");
				default: return L("Collections.Status.ActionRequired", "Action required");
			}
		}

		private static string FormatImpactStatus(CollectionConflictImpactStatus status)
		{
			return status == CollectionConflictImpactStatus.Blocked
				? L("Collections.Status.Unsupported", "Blocked")
				: L("Collections.Status.ActionRequired", "Action required");
		}

		private static string FormatRecoveryStatus(CollectionAdditiveWorkflowRecoveryStatus status)
		{
			return status == CollectionAdditiveWorkflowRecoveryStatus.ReviewRequired || status == CollectionAdditiveWorkflowRecoveryStatus.ReadyToResume
				? L("Collections.Status.Supported", "Ready")
				: L("Collections.Status.ActionRequired", "Action required");
		}

		private static string FirstNonEmpty(params string[] values)
		{
			foreach (string value in values)
				if (!string.IsNullOrWhiteSpace(value))
					return value;
			return string.Empty;
		}

		private static string L(string key, string fallback)
		{
			return LanguageManager.Get(key, fallback);
		}
	}
}
