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
using Nexus.UI.Controls;

namespace Nexus.Client.CollectionManagement.UI
{
	/// <summary>
	/// Collections surface for provider preview, additive apply, Local capture/restore and installed Collection management.
	/// </summary>
	/// <remarks>
	/// The control consumes application-level workflow services and never constructs or invokes native C4/C5/C6
	/// persistence or mutation coordinators directly. Remote/local recipe replacement and richer management commands remain out of scope.
	/// </remarks>
	public sealed class CollectionsPreviewControl : ManagedFontDockContent
	{
		private readonly Button _saveCurrentSetupButton;
		private readonly ComboBox _localCaptureCombo;
		private readonly Button _restoreLocalCaptureButton;
		private readonly ComboBox _managedAssociationCombo;
		private readonly Button _detachAssociationButton;
		private readonly Button _removeAssociationEffectsButton;
		private readonly Button _importButton;
		private readonly Button _downloadPrepareButton;
		private readonly CheckBox _autoOverwriteArchivesCheckBox;
		private readonly ToolTip _toolTip;
		private readonly Button _resumeButton;
		private readonly Button _openPendingButton;
		private readonly Button _installButton;
		private readonly Button _clearButton;
		private readonly Button _exportTechnicalReportButton;
		private readonly Label _instructionLabel;
		private readonly Label _workflowStatusLabel;
		private readonly Label _workflowActivityIconLabel;
		private readonly Label _workflowEtaLabel;
		private readonly System.Windows.Forms.Timer _workflowActivityAnimationTimer;
		private readonly Label _collectionValue;
		private readonly Label _curatorValue;
		private readonly Label _locatorValue;
		private readonly Label _revisionValue;
		private readonly Label _compatibilityValue;
		private readonly Label _contentValue;
		private readonly Label _appliedValue;
		private readonly TextBox _summaryBox;
		private readonly ListView _membersView;
		private readonly Panel _membersHost;
		private readonly Panel _membersLoadingOverlay;
		private readonly Label _membersLoadingLabel;
		private readonly ProgressBar _membersLoadingProgress;
		private readonly ListView _issuesView;
		private readonly ListView _reviewActionsView;
		private readonly Label _membersHeader;
		private readonly Label _issuesHeader;
		private readonly Label _reviewActionsHeader;
		private readonly CheckBox _showErrorIssuesCheckBox;
		private readonly CheckBox _showWarningIssuesCheckBox;
		private readonly CheckBox _showInfoIssuesCheckBox;
		private readonly TableLayoutPanel _reviewPanel;
		private readonly List<CollectionReviewItem> _reviewItems = new List<CollectionReviewItem>();
		private int _reviewErrorCount;
		private int _reviewWarningCount;
		private int _reviewInfoCount;
		private int _reviewActionCount;

		private NexusCollectionNxmDispatcher _dispatcher;
		private NexusCollectionPreviewController _controller;
		private CollectionAdditiveApplicationService _workflow;
		private CollectionLocalCaptureApplicationService _captureWorkflow;
		private CollectionManagementApplicationService _managementWorkflow;
		private NexusCollectionPreviewSnapshot _snapshot;
		private CollectionAdditiveWorkflowPreparationResult _preparation;
		private CollectionMemberAcquisitionBatch _acquisitionBatch;
		private CollectionManagementAssociationPresentation _managedAssociationPresentation;
		private CollectionOperationIdentity _operationIdentity;
		private CollectionOperation _operationSnapshot;
		private CollectionPlanIdentity _reviewedPlanIdentity;
		private IReadOnlyList<CollectionAdditiveWorkflowRecoveryResult> _recoveryResults = new CollectionAdditiveWorkflowRecoveryResult[0];
		private IReadOnlyList<CollectionLocalRestoreWorkflowResult> _localRestoreRecoveryResults = new CollectionLocalRestoreWorkflowResult[0];
		private CancellationTokenSource _previewCancellation;
		private CancellationTokenSource _workflowCancellation;
		private CollectionUiContext _displayContext;
		private CollectionUiContext _workflowContext;
		private CollectionUiContext _currentSetupActionContext;
		private CollectionUiContext _localCaptureActionContext;
		private CollectionUiContext _managedAssociationActionContext;
		private CollectionUiContext _incomingActionContext;
		private int _previewGeneration;
		private bool _initialized;
		private bool _workflowBusy;
		private bool _selectionDirty;
		private bool _selectionCapabilityBlocked;
		private bool _suppressMemberCheckEvents;
		private bool _suppressManagedAssociationSelection;
		private readonly System.Windows.Forms.Timer _acquisitionRefreshTimer;
		private readonly HashSet<Guid> _autoResumedQueueOperations = new HashSet<Guid>();
		private Exception _lastTechnicalFailure;
		private string _lastTechnicalFailureCode;
		private int _lastTechnicalFailureGeneration = -1;
		private CollectionWorkflowActivitySnapshot _workflowActivity;
		private CollectionWorkflowEtaSnapshot _workflowEta;
		private readonly CollectionWorkflowEtaEstimator _workflowEtaEstimator;
		private int _workflowActivityAnimationFrame;

		private static readonly string[] WorkflowActivityAnimationFrames = { "◐", "◓", "◑", "◒" };

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
			_acquisitionRefreshTimer = new System.Windows.Forms.Timer { Interval = 750 };
			_acquisitionRefreshTimer.Tick += AcquisitionRefreshTimer_Tick;
			_workflowActivityAnimationTimer = new System.Windows.Forms.Timer { Interval = 125 };
			_workflowActivityAnimationTimer.Tick += WorkflowActivityAnimationTimer_Tick;
			_toolTip = new ToolTip();
			_workflowActivity = CollectionWorkflowActivityBuilder.Idle(L("Collections.Workflow.Idle", "Workflow: idle"));
			_workflowEtaEstimator = new CollectionWorkflowEtaEstimator();
			_workflowEta = CollectionWorkflowEtaSnapshot.Unavailable("idle");

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
			_saveCurrentSetupButton = new Button
			{
				AutoSize = true,
				Text = L("Collections.Actions.SaveCurrentSetup", "Save current setup as Local Collection"),
				Enabled = false
			};
			_saveCurrentSetupButton.Click += SaveCurrentSetupButton_Click;
			_localCaptureCombo = new ComboBox
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				Width = 280,
				Enabled = false
			};
			_localCaptureCombo.SelectedIndexChanged += LocalCaptureCombo_SelectedIndexChanged;
			_restoreLocalCaptureButton = new Button
			{
				AutoSize = true,
				Text = L("Collections.Actions.RestoreLocal", "Restore Local Collection..."),
				Enabled = false
			};
			_restoreLocalCaptureButton.Click += RestoreLocalCaptureButton_Click;
			_managedAssociationCombo = new ComboBox
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				Width = 280,
				Enabled = false
			};
			_managedAssociationCombo.SelectedIndexChanged += ManagedAssociationCombo_SelectedIndexChanged;
			_detachAssociationButton = new Button
			{
				AutoSize = true,
				Text = L("Collections.Actions.DetachTracking", "Stop tracking this Collection..."),
				Enabled = false
			};
			_detachAssociationButton.Click += DetachAssociationButton_Click;
			_removeAssociationEffectsButton = new Button
			{
				AutoSize = true,
				Text = L("Collections.Actions.RemoveEffects", "Review removal..."),
				Enabled = false
			};
			_removeAssociationEffectsButton.Click += RemoveAssociationEffectsButton_Click;
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
			_autoOverwriteArchivesCheckBox = new CheckBox
			{
				AutoSize = true,
				Text = L("Collections.Actions.AutoOverwriteArchives", "Overwrite automatically all present archives"),
				Checked = false,
				Enabled = false,
				Padding = new Padding(3, 3, 3, 0)
			};
			_toolTip.SetToolTip(_autoOverwriteArchivesCheckBox, L("Collections.Actions.AutoOverwriteArchivesHelp",
				"For Collection downloads/imports queued by this Download / Prepare batch, replace existing NMM mod archives at their original archive path without asking for each collision. Verified archives are still reused; manually added files outside this correlated workflow and installation file-conflict rules are unchanged."));
			_resumeButton = new Button
			{
				AutoSize = true,
				Text = L("Collections.Actions.ResumePreparation", "Check downloads and continue"),
				Enabled = false,
				Visible = false
			};
			_resumeButton.Click += ResumeButton_Click;
			_openPendingButton = new Button
			{
				AutoSize = true,
				Text = L("Collections.Actions.OpenDownloadPage", "Download selected missing mod"),
				Enabled = false,
				Visible = false
			};
			_openPendingButton.Click += OpenPendingButton_Click;
			_installButton = new Button
			{
				AutoSize = true,
				Text = L("Collections.Actions.InstallCurrent", "Review and install..."),
				Enabled = false
			};
			_installButton.Click += InstallButton_Click;
			_clearButton = new Button
			{
				AutoSize = true,
				Text = L("Collections.Actions.ClearPreview", "Close this preview")
			};
			_clearButton.Click += ClearButton_Click;
			_exportTechnicalReportButton = new Button
			{
				AutoSize = true,
				Text = L("Collections.Actions.ExportTechnicalReport", "Export Technical Report..."),
				Enabled = false
			};
			_exportTechnicalReportButton.Click += ExportTechnicalReportButton_Click;
			_instructionLabel = new Label
			{
				AutoSize = true,
				MaximumSize = new Size(760, 0),
				Padding = new Padding(12, 6, 0, 0),
				Text = L("Collections.Preview.Instructions", "Open a Nexus Collection NXM link to load it, choose optional mods, download and prepare, review the changes, then install. Select a saved Local Collection above to review and restore it.")
			};
			Control currentSetupGroup = CreateActionGroup(L("Collections.Context.CurrentSetup", "Current game setup"),
				_saveCurrentSetupButton);
			Control savedLocalGroup = CreateActionGroup(L("Collections.Context.SavedLocal", "Saved Local Collection"),
				_localCaptureCombo, _restoreLocalCaptureButton);
			Control installedGroup = CreateActionGroup(L("Collections.Context.Installed", "Installed Collection"),
				_managedAssociationCombo, _detachAssociationButton, _removeAssociationEffectsButton);
			Control incomingGroup = CreateActionGroup(L("Collections.Context.Incoming", "Incoming Collection"),
				_importButton, _downloadPrepareButton, _autoOverwriteArchivesCheckBox, _resumeButton, _openPendingButton, _installButton, _clearButton);
			Control supportGroup = CreateActionGroup(L("Collections.Context.Support", "Support"), _exportTechnicalReportButton);

			toolbar.Controls.Add(currentSetupGroup);
			toolbar.Controls.Add(savedLocalGroup);
			toolbar.Controls.Add(installedGroup);
			toolbar.Controls.Add(incomingGroup);
			toolbar.Controls.Add(supportGroup);
			toolbar.SetFlowBreak(supportGroup, true);
			toolbar.Controls.Add(_instructionLabel);
			toolbar.SetFlowBreak(_instructionLabel, true);
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

			var workflowStatusPanel = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				AutoSize = true,
				ColumnCount = 3,
				RowCount = 1,
				Margin = Padding.Empty
			};
			workflowStatusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22F));
			workflowStatusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			workflowStatusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			_workflowActivityIconLabel = new Label
			{
				Dock = DockStyle.Fill,
				TextAlign = ContentAlignment.MiddleCenter,
				Margin = Padding.Empty,
				Padding = new Padding(0, 3, 3, 6),
				Text = String.Empty,
				AccessibleName = L("Collections.Workflow.ActivityIndicatorAccessibleName", "Workflow activity indicator")
			};
			_workflowStatusLabel = new Label
			{
				AutoSize = true,
				MaximumSize = new Size(1200, 0),
				Padding = new Padding(0, 3, 0, 6),
				Text = L("Collections.Workflow.Idle", "Workflow: idle")
			};
			_workflowEtaLabel = new Label
			{
				AutoSize = true,
				TextAlign = ContentAlignment.MiddleRight,
				Padding = new Padding(12, 3, 0, 6),
				Text = String.Empty,
				Visible = false
			};
			workflowStatusPanel.Controls.Add(_workflowActivityIconLabel, 0, 0);
			workflowStatusPanel.Controls.Add(_workflowStatusLabel, 1, 0);
			workflowStatusPanel.Controls.Add(_workflowEtaLabel, 2, 0);
			root.Controls.Add(workflowStatusPanel, 0, 3);

			var splitHeaders = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				AutoSize = true,
				ColumnCount = 2
			};
			splitHeaders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
			splitHeaders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
			_membersHeader = new Label { AutoSize = true, Text = L("Collections.Preview.Members", "Members") };
			_issuesHeader = new Label
			{
				AutoSize = true,
				Text = L("Collections.Preview.Issues", "Review / issues"),
				Margin = new Padding(0, 3, 12, 0)
			};
			_showErrorIssuesCheckBox = new CheckBox { AutoSize = true, Checked = true, Margin = new Padding(0, 0, 8, 0) };
			_showWarningIssuesCheckBox = new CheckBox { AutoSize = true, Checked = false, Margin = new Padding(0, 0, 8, 0) };
			_showInfoIssuesCheckBox = new CheckBox { AutoSize = true, Checked = false, Margin = new Padding(0, 0, 0, 0) };
			_showErrorIssuesCheckBox.CheckedChanged += ReviewSeverityFilter_CheckedChanged;
			_showWarningIssuesCheckBox.CheckedChanged += ReviewSeverityFilter_CheckedChanged;
			_showInfoIssuesCheckBox.CheckedChanged += ReviewSeverityFilter_CheckedChanged;

			var issuesHeaderPanel = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill,
				AutoSize = true,
				WrapContents = true,
				FlowDirection = FlowDirection.LeftToRight,
				Margin = Padding.Empty
			};
			issuesHeaderPanel.Controls.Add(_issuesHeader);
			issuesHeaderPanel.Controls.Add(_showErrorIssuesCheckBox);
			issuesHeaderPanel.Controls.Add(_showWarningIssuesCheckBox);
			issuesHeaderPanel.Controls.Add(_showInfoIssuesCheckBox);
			splitHeaders.Controls.Add(_membersHeader, 0, 0);
			splitHeaders.Controls.Add(issuesHeaderPanel, 1, 0);
			root.Controls.Add(splitHeaders, 0, 4);

			var contentGrid = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 2,
				RowCount = 1
			};
			contentGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
			contentGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));

			_membersHost = new Panel
			{
				Dock = DockStyle.Fill,
				Margin = Padding.Empty
			};
			_membersView = CreateListView();
			_membersView.CheckBoxes = true;
			_membersView.ItemCheck += MembersView_ItemCheck;
			_membersView.SelectedIndexChanged += MembersView_SelectedIndexChanged;
			_membersView.Columns.Add(L("Collections.Columns.Member", "Member"), 210);
			_membersView.Columns.Add(L("Collections.Columns.Requirement", "Requirement"), 88);
			_membersView.Columns.Add(L("Collections.Columns.Selection", "Selection"), 82);
			_membersView.Columns.Add(L("Collections.Columns.Compatibility", "Compatibility"), 112);
			_membersView.Columns.Add(L("Collections.Columns.Artifact", "Artifact"), 240);
			_membersHost.Controls.Add(_membersView);

			_membersLoadingOverlay = new Panel
			{
				Dock = DockStyle.Fill,
				BackColor = SystemColors.Window,
				BorderStyle = BorderStyle.FixedSingle,
				Visible = false
			};
			var membersLoadingLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 4,
				Padding = new Padding(16)
			};
			membersLoadingLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
			membersLoadingLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			membersLoadingLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			membersLoadingLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
			_membersLoadingLabel = new Label
			{
				AutoSize = true,
				Anchor = AnchorStyles.None,
				MaximumSize = new Size(520, 0),
				TextAlign = ContentAlignment.MiddleCenter,
				Margin = new Padding(0, 0, 0, 8)
			};
			_membersLoadingProgress = new ProgressBar
			{
				Anchor = AnchorStyles.None,
				Style = ProgressBarStyle.Marquee,
				MarqueeAnimationSpeed = 30,
				Width = 180,
				Height = 16,
				TabStop = false
			};
			membersLoadingLayout.Controls.Add(_membersLoadingLabel, 0, 1);
			membersLoadingLayout.Controls.Add(_membersLoadingProgress, 0, 2);
			_membersLoadingOverlay.Controls.Add(membersLoadingLayout);
			_membersHost.Controls.Add(_membersLoadingOverlay);
			contentGrid.Controls.Add(_membersHost, 0, 0);

			_reviewPanel = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 3,
				Margin = Padding.Empty
			};
			_reviewPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			_reviewPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));
			_reviewPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));

			_issuesView = CreateReviewListView();
			_reviewPanel.Controls.Add(_issuesView, 0, 0);

			_reviewActionsHeader = new Label
			{
				AutoSize = true,
				Dock = DockStyle.Fill,
				Padding = new Padding(0, 5, 0, 3),
				Visible = false
			};
			_reviewPanel.Controls.Add(_reviewActionsHeader, 0, 1);

			_reviewActionsView = CreateReviewListView();
			_reviewActionsView.Visible = false;
			_reviewPanel.Controls.Add(_reviewActionsView, 0, 2);
			contentGrid.Controls.Add(_reviewPanel, 1, 0);
			root.Controls.Add(contentGrid, 0, 5);

			_displayContext = CollectionUiContext.None(_previewGeneration);
			RenderWorkflowActivity();
			RenderEmptyState();
		}

		/// <summary>Connects the surface to the incoming Collection dispatcher in preview-only compatibility mode.</summary>
		public void Initialize(NexusCollectionNxmDispatcher dispatcher)
		{
			Initialize(dispatcher, null, null, null);
		}

		/// <summary>Connects the surface to the incoming dispatcher and production additive workflow service.</summary>
		public void Initialize(NexusCollectionNxmDispatcher dispatcher, CollectionAdditiveApplicationService workflow)
		{
			Initialize(dispatcher, workflow, null, null);
		}

		/// <summary>Connects additive and Local Collection capture application workflows to this permanent surface.</summary>
		public void Initialize(NexusCollectionNxmDispatcher dispatcher, CollectionAdditiveApplicationService workflow,
			CollectionLocalCaptureApplicationService captureWorkflow)
		{
			Initialize(dispatcher, workflow, captureWorkflow, null);
		}

		/// <summary>Connects additive, capture and basic installed-Collection management workflows to this permanent surface.</summary>
		public void Initialize(NexusCollectionNxmDispatcher dispatcher, CollectionAdditiveApplicationService workflow,
			CollectionLocalCaptureApplicationService captureWorkflow, CollectionManagementApplicationService managementWorkflow)
		{
			if (ReferenceEquals(_dispatcher, dispatcher) && ReferenceEquals(_workflow, workflow) &&
				ReferenceEquals(_captureWorkflow, captureWorkflow) && ReferenceEquals(_managementWorkflow, managementWorkflow) && _initialized)
				return;

			DetachDispatcher();
			CancelPreviewWork();
			CancelWorkflowWork();
			_snapshot = null;
			_managedAssociationPresentation = null;
			_displayContext = CollectionUiContext.None(_previewGeneration);
			_workflow = workflow;
			_captureWorkflow = captureWorkflow;
			_managementWorkflow = managementWorkflow;
			ResetWorkflowViewState();
			RenderEmptyState();
			RefreshLocalCaptures();
			RefreshManagedAssociations();

			_dispatcher = dispatcher;
			_initialized = true;
			_controller = dispatcher == null ? null : new NexusCollectionPreviewController(dispatcher.Provider);
			if (_dispatcher != null)
				_dispatcher.DispatchCompleted += Dispatcher_DispatchCompleted;

			UpdateActionButtons();
			if (IsHandleCreated)
			{
				BeginInvoke((Action)DrainIncomingQueue);
				if (_workflow != null || _managementWorkflow != null)
					BeginInvoke((Action)BeginRecoveryReconciliation);
			}
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			if (_initialized)
			{
				BeginInvoke((Action)DrainIncomingQueue);
				if (_workflow != null || _managementWorkflow != null)
					BeginInvoke((Action)BeginRecoveryReconciliation);
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				_acquisitionRefreshTimer.Stop();
				_acquisitionRefreshTimer.Dispose();
				_toolTip.Dispose();
				DetachDispatcher();
				CancelPreviewWork();
				CancelWorkflowWork();
				_workflowActivityAnimationTimer.Stop();
				_workflowActivityAnimationTimer.Dispose();
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
			// Do not replace the visible context while a foreground workflow owns this surface. The dispatcher queue is
			// bounded and retains the request; EndWorkflowWork schedules another drain after the foreground boundary closes.
			if (_dispatcher == null || IsDisposed || Disposing || _workflowBusy)
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
			_lastTechnicalFailure = null;
			_lastTechnicalFailureCode = null;
			_lastTechnicalFailureGeneration = -1;
			CollectionUiContext previewContext = CollectionUiContext.Incoming(generation, null, null);
			_displayContext = previewContext;
			CancelPreviewWork(false);
			_previewCancellation = new CancellationTokenSource();
			CancellationToken token = _previewCancellation.Token;

			_snapshot = null;
			_managedAssociationPresentation = null;
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
				_managedAssociationPresentation = null;
				if (snapshot.Revision != null)
					previewContext = previewContext.WithRevision(snapshot.Revision.Identity);
				_displayContext = previewContext;
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
				RememberTechnicalFailure("preview.unexpected-failure", ex);
				RenderUnexpectedFailure(dispatch.Link, ex);
			}
		}

		private void LocalCaptureCombo_SelectedIndexChanged(object sender, EventArgs e)
		{
			UpdateActionButtons();
		}

		private void RefreshLocalCaptures()
		{
			LocalCaptureIdentity selectedId = null;
			CollectionManagementLocalCapture selected = _localCaptureCombo.SelectedItem as CollectionManagementLocalCapture;
			if (selected != null) selectedId = selected.CaptureIdentity;

			_localCaptureCombo.BeginUpdate();
			try
			{
				_localCaptureCombo.Items.Clear();
				if (_managementWorkflow == null) return;
				foreach (CollectionManagementLocalCapture capture in _managementWorkflow.GetLocalCaptures())
					_localCaptureCombo.Items.Add(capture);
				if (_localCaptureCombo.Items.Count > 0)
				{
					int selectedIndex = 0;
					if (selectedId != null)
					{
						for (int index = 0; index < _localCaptureCombo.Items.Count; index++)
						{
							CollectionManagementLocalCapture candidate = _localCaptureCombo.Items[index] as CollectionManagementLocalCapture;
							if (candidate != null && candidate.CaptureIdentity.Equals(selectedId))
							{
								selectedIndex = index;
								break;
							}
						}
					}
					_localCaptureCombo.SelectedIndex = selectedIndex;
				}
			}
			catch (Exception ex)
			{
				Trace.TraceError("Local Collection capture list refresh failed: " + ex);
			}
			finally
			{
				_localCaptureCombo.EndUpdate();
				UpdateActionButtons();
			}
		}

		private void ManagedAssociationCombo_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (_suppressManagedAssociationSelection)
				return;
			ShowSelectedManagedAssociation();
		}

		private void RefreshManagedAssociations()
		{
			Guid selectedId = Guid.Empty;
			CollectionManagementAssociation selected = _managedAssociationCombo.SelectedItem as CollectionManagementAssociation;
			if (selected != null)
				selectedId = selected.AssociationId;

			_suppressManagedAssociationSelection = true;
			_managedAssociationCombo.BeginUpdate();
			try
			{
				_managedAssociationCombo.Items.Clear();
				if (_managementWorkflow == null)
					return;

				IReadOnlyList<CollectionManagementAssociation> associations = _managementWorkflow.GetAssociations();
				foreach (CollectionManagementAssociation association in associations)
					_managedAssociationCombo.Items.Add(association);

				if (_managedAssociationCombo.Items.Count > 0)
				{
					int selectedIndex = -1;

					// When a Collection preview is loaded, prefer its exact installed association. This keeps the
					// management actions visually tied to the revision the user is currently reviewing.
					if (_snapshot != null && _snapshot.Revision != null)
					{
						for (int index = 0; index < _managedAssociationCombo.Items.Count; index++)
						{
							CollectionManagementAssociation candidate = _managedAssociationCombo.Items[index] as CollectionManagementAssociation;
							if (candidate != null && candidate.Association.Revision.Equals(_snapshot.Revision.Identity))
							{
								selectedIndex = index;
								break;
							}
						}
					}

					if (selectedIndex < 0 && selectedId != Guid.Empty)
					{
						for (int index = 0; index < _managedAssociationCombo.Items.Count; index++)
						{
							CollectionManagementAssociation candidate = _managedAssociationCombo.Items[index] as CollectionManagementAssociation;
							if (candidate != null && candidate.AssociationId == selectedId)
							{
								selectedIndex = index;
								break;
							}
						}
					}

					_managedAssociationCombo.SelectedIndex = selectedIndex < 0 ? 0 : selectedIndex;
				}
			}
			catch (Exception ex)
			{
				Trace.TraceError("Installed Collection management list refresh failed: " + ex);
			}
			finally
			{
				_managedAssociationCombo.EndUpdate();
				_suppressManagedAssociationSelection = false;
				if (_snapshot == null || _managedAssociationPresentation != null)
					ShowSelectedManagedAssociation();
				else
				{
					ApplyManagedAssociationPresentation();
					UpdateActionButtons();
				}
			}
		}

		private void ShowSelectedManagedAssociation()
		{
			CollectionManagementAssociation selected = _managedAssociationCombo.SelectedItem as CollectionManagementAssociation;
			if (_managementWorkflow == null || selected == null)
			{
				bool wasManagedAssociationView = _managedAssociationPresentation != null;
				_managedAssociationPresentation = null;
				if (wasManagedAssociationView)
				{
					_snapshot = null;
					_displayContext = CollectionUiContext.None(++_previewGeneration);
					ResetWorkflowViewState();
				}
				if (_snapshot == null)
					RenderEmptyState();
				UpdateActionButtons();
				return;
			}

			try
			{
				CollectionManagementAssociationPresentation presentation = _managementWorkflow.GetAssociationPresentation(selected.AssociationId);
				if (presentation == null)
				{
					_managedAssociationPresentation = null;
					_snapshot = null;
					_displayContext = CollectionUiContext.None(++_previewGeneration);
					ResetWorkflowViewState();
					RenderEmptyState();
					UpdateActionButtons();
					return;
				}

				NexusCollectionNxmLink retainedLink = _snapshot != null && _snapshot.Revision != null && presentation.Revision != null &&
					_snapshot.Revision.Identity.Equals(presentation.Revision.Identity) ? _snapshot.Link : null;

				int generation = ++_previewGeneration;
				CancelPreviewWork(false);
				_displayContext = CollectionUiContext.Installed(generation,
					presentation.Association.Association.Revision, selected.AssociationId);
				_managedAssociationPresentation = presentation;
				ResetWorkflowViewState();
				_instructionLabel.Text = L("Collections.Management.InstalledInstructions",
					"Viewing an installed Collection. Stop tracking keeps the currently installed effects in place; Review removal opens a separate safety review that preserves shared or non-removable content. Open a Nexus Collection link to review another revision.");

				NexusCollectionPreviewSnapshot snapshot = null;
				if (presentation.Definition != null && presentation.Revision != null)
				{
					snapshot = new NexusCollectionPreviewSnapshot(
						retainedLink, null, null, presentation.Definition, presentation.Revision,
						presentation.RetainedManifest, null, null, null);
				}
				_snapshot = snapshot;

				if (snapshot != null)
				{
					RenderSnapshot(snapshot);
					if (presentation.Association.State == CollectionAssociationState.Applied)
						RenderIssues(snapshot, false, false);
					ApplyManagedAssociationMemberState(presentation);
					AppendAppliedManifestResolutionIssues(presentation);
					AppendManagedAssociationIssues(presentation);
				}
				else
				{
					RenderManagedAssociationHeaderOnly(presentation);
				}

				_workflowStatusLabel.Text = L("Collections.Workflow.InstalledView",
					"Workflow: viewing the durably tracked installed Collection; no mutation is in progress.");
				BindMatchingRecovery();
				UpdateActionButtons();
			}
			catch (Exception ex)
			{
				Trace.TraceError("Installed Collection presentation failed: " + ex);
				RememberTechnicalFailure("association.presentation-failed", ex);
				_managedAssociationPresentation = null;
				_snapshot = null;
				RenderEmptyState();
				CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForFailure("association.presentation-failed", ex.Message);
				_workflowStatusLabel.Text = L("Collections.Workflow.InstalledViewFailed",
					"Workflow: the installed Collection is still tracked, but its retained details could not be loaded.");
				AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic, L("Collections.Status.ActionRequired", "Action required"),
					"association.presentation-failed", selected.Association.Revision.ToString(), userMessage);
				UpdateIssuesHeader();
				UpdateActionButtons();
			}
		}

		private void ApplyManagedAssociationMemberState(CollectionManagementAssociationPresentation presentation)
		{
			if (presentation == null || presentation.RetainedManifest == null)
				return;
			var bound = new HashSet<CollectionMemberKey>(presentation.BoundMemberKeys);
			bool applied = presentation.Association.State == CollectionAssociationState.Applied;
			_suppressMemberCheckEvents = true;
			try
			{
				foreach (ListViewItem item in _membersView.Items)
				{
					NormalizedCollectionMember member = item.Tag as NormalizedCollectionMember;
					if (member == null || !member.IdentityResolution.IsResolved)
						continue;
					item.Checked = bound.Contains(member.IdentityResolution.Key);
					if (item.SubItems.Count > 2)
						item.SubItems[2].Text = item.Checked
							? L("Collections.Member.Selected", "Selected")
							: L("Collections.Member.Unselected", "Not selected");
					if (applied && item.Checked && item.SubItems.Count > 3)
						item.SubItems[3].Text = L("Collections.Status.Supported", "Supported");
				}
			}
			finally
			{
				_suppressMemberCheckEvents = false;
			}
		}

		/// <summary>
		/// Replaces raw pre-install Prefer Exact warnings with the durable outcome proven by an Applied association.
		/// </summary>
		private void AppendAppliedManifestResolutionIssues(CollectionManagementAssociationPresentation presentation)
		{
			if (presentation == null || presentation.Association.State != CollectionAssociationState.Applied ||
				presentation.RetainedManifest == null || presentation.RetainedManifest.CapabilityReport == null)
				return;

			var bound = new HashSet<CollectionMemberKey>(presentation.BoundMemberKeys);
			foreach (CollectionMemberCapabilityReport memberReport in presentation.RetainedManifest.CapabilityReport.MemberReports)
			{
				NormalizedCollectionMember member = memberReport.Member;
				if (member == null || !member.IdentityResolution.IsResolved || !bound.Contains(member.IdentityResolution.Key))
					continue;

				CollectionCapabilityIssue preferIssue = memberReport.Issues.FirstOrDefault(x =>
					StringComparer.Ordinal.Equals(x.Code, CollectionNexusPreferExactPolicyResolver.PreferIssueCode));
				if (preferIssue == null)
					continue;

				string artifact = member.Artifact == null ? member.IdentityResolution.Key.ToString() : member.Artifact.ToString();
				AddReviewItem(CollectionReviewSeverity.Info, CollectionReviewItemKind.Progress, L("Collections.Status.Supported", "Ready"), "member.source-policy-prefer-resolved-exact",
					preferIssue.FieldPath ?? string.Empty,
					LanguageManager.Format("Collections.PreferExact.AppliedResolution",
						"The Collection's 'prefer' file policy was resolved during preparation to the curator's requested Nexus file ({0}); the installed member was verified. A newer-file fallback was not used.", artifact),
					member.IdentityResolution.Key);
			}
		}

		private void AppendManagedAssociationIssues(CollectionManagementAssociationPresentation presentation)
		{
			if (presentation == null)
				return;
			string status = presentation.Association.State == CollectionAssociationState.Applied
				? L("Collections.Status.Supported", "Ready")
				: L("Collections.Status.ActionRequired", "Action required");
			AddReviewItem(CollectionReviewPresentationClassifier.ForAssociation(presentation.Association.State),
				presentation.Association.State == CollectionAssociationState.Applied ? CollectionReviewItemKind.Progress : CollectionReviewItemKind.Diagnostic,
				status, "association." + presentation.Association.State.ToString().ToLowerInvariant(),
				presentation.Association.Association.Revision.ToString(), FormatManagedAssociationState(presentation.Association.State));
			if (!String.IsNullOrWhiteSpace(presentation.RetainedSourceIssue))
			{
				AddPresentedReviewItem(CollectionReviewSeverity.Warning, CollectionReviewItemKind.Diagnostic, L("Collections.Status.ActionRequired", "Action required"),
					"association.retained-source", presentation.Association.Association.Revision.ToString(),
					CollectionUserMessagePresenter.ForRetainedSourceIssue(presentation.RetainedSourceIssue));
			}
			UpdateIssuesHeader();
		}

		private void RenderManagedAssociationHeaderOnly(CollectionManagementAssociationPresentation presentation)
		{
			_collectionValue.Text = presentation.Association.DisplayName;
			_curatorValue.Text = presentation.Definition == null || String.IsNullOrWhiteSpace(presentation.Definition.AuthorDisplayName)
				? L("Collections.Value.Unknown", "Unknown") : presentation.Definition.AuthorDisplayName;
			_locatorValue.Text = FormatManagedAssociationLocator(presentation);
			_revisionValue.Text = presentation.Association.RevisionLabel;
			_compatibilityValue.Text = L("Collections.Status.Compatibility.AppliedVerified", "Supported when applied - retained manifest unavailable");
			_contentValue.Text = presentation.Association.State == CollectionAssociationState.Applied
				? L("Collections.Status.Content.Applied", "Prepared content applied and verified")
				: L("Collections.Status.Content.NoSelection", "Retained installed association");
			_summaryBox.Text = presentation.Definition == null || String.IsNullOrWhiteSpace(presentation.Definition.Summary)
				? L("Collections.Preview.NoSummary", "No collection summary was returned. Decorative metadata is optional and does not establish identity or readiness.")
				: presentation.Definition.Summary;
			_membersView.Items.Clear();
			ClearReviewItems();
			_membersHeader.Text = L("Collections.Preview.Members", "Members");
			_issuesHeader.Text = L("Collections.Preview.Issues", "Review / issues");
			ApplyManagedAssociationPresentation();
			AppendManagedAssociationIssues(presentation);
		}

		private string FormatManagedAssociationLocator(CollectionManagementAssociationPresentation presentation)
		{
			if (presentation == null || presentation.Association == null)
				return L("Collections.Value.Unknown", "Unknown");
			CollectionIdentity identity = presentation.Association.Association.Revision.Collection;
			if (identity.Origin != CollectionOrigin.NexusMods)
				return identity.ToString();
			string domain = presentation.NexusGameDomain;
			if (String.IsNullOrWhiteSpace(domain))
				return "collection " + identity.StableId;
			return domain + " / collection " + identity.StableId;
		}

		private static string FormatManagedAssociationState(CollectionAssociationState state)
		{
			switch (state)
			{
				case CollectionAssociationState.Applied:
					return L("Collections.Management.State.Applied", "The installed Collection revision is applied and verified for the current target.");
				case CollectionAssociationState.Modified:
					return L("Collections.Management.State.Modified", "The installed Collection is still tracked, but the current installed state differs from the reviewed revision.");
				case CollectionAssociationState.Incomplete:
					return L("Collections.Management.State.Incomplete", "This installed Collection is incomplete. Review its current state and resume it before treating it as fully applied.");
				case CollectionAssociationState.Recovering:
					return L("Collections.Management.State.Recovering", "This installed Collection needs recovery before NMM can safely make more managed changes.");
				default:
					return L("Collections.Management.State.Unknown", "NMM cannot determine the current installed Collection state from the available information.");
			}
		}

		private void DetachAssociationButton_Click(object sender, EventArgs e)
		{
			CollectionManagementAssociation selected = _managedAssociationCombo.SelectedItem as CollectionManagementAssociation;
			CollectionUiContext context = _managedAssociationActionContext;
			if (_managementWorkflow == null || selected == null || _workflowBusy || context == null ||
				context.AssociationId != selected.AssociationId)
				return;

			string message = LanguageManager.Format("Collections.Management.DetachPrompt",
				"Stop tracking '{0}' as a Collection?\r\n\r\nInstalled mods, files, plugins and configuration effects will stay exactly as they are. NMM will simply stop associating them with this Collection.",
				selected.DisplayName);
			if (MessageBox.Show(this, message, L("Collections.Actions.DetachTracking", "Stop tracking this Collection..."),
				MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes ||
				!IsActionContextCurrent(context))
				return;

			BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Managing, L("Collections.Management.Detaching", "Stopping Collection tracking without changing installed content..."));
			try
			{
				CollectionDetachResult result = _managementWorkflow.Detach(selected.AssociationId);
				RefreshManagedAssociations();
				_workflowStatusLabel.Text = LanguageManager.Format("Collections.Management.Detached",
					"Workflow: NMM stopped tracking '{0}'; installed content was preserved.", selected.DisplayName);
				MessageBox.Show(this, LanguageManager.Format("Collections.Management.DetachedMessage",
					"'{0}' is no longer tracked as an installed Collection. {1} installed mod instance(s) remain available for standalone use.",
					selected.DisplayName, result.StandaloneProvenance.Count),
					L("Collections.Management.DetachedTitle", "Collection detached"), MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection detach failed: " + ex);
				RememberTechnicalFailure("association.detach-failed", ex, context);
				CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForFailure("association.detach-failed", ex.Message);
				MessageBox.Show(this, BuildUserDialogMessage(userMessage), L("Collections.Management.DetachFailed", "Collection tracking change failed"),
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally
			{
				EndWorkflowWork(context);
			}
		}

		private async void RemoveAssociationEffectsButton_Click(object sender, EventArgs e)
		{
			CollectionManagementAssociation selected = _managedAssociationCombo.SelectedItem as CollectionManagementAssociation;
			CollectionUiContext context = _managedAssociationActionContext;
			if (_managementWorkflow == null || selected == null || _workflowBusy || context == null ||
				context.AssociationId != selected.AssociationId)
				return;

			CancellationToken token = BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Managing, L("Collections.Management.ReviewingRemoval",
				"Reviewing which Collection effects can be removed safely..."));
			try
			{
				CollectionUninstallEffectsPlan plan = await _managementWorkflow.PreviewEffectRemovalAsync(selected.AssociationId, token);
				if (!IsWorkflowContextCurrent(context, token))
					return;

				if (plan.HasBlockedImpacts)
				{
					string blocked = String.Join(Environment.NewLine, plan.Impacts.Where(x => x.BlocksExecution)
						.Select(x => "- " + FormatUninstallImpactSubject(x) + ": " + CollectionUserMessagePresenter.SanitizeInternalTerminology(x.Reason)));
					MessageBox.Show(this, LanguageManager.Format("Collections.Management.RemovalBlockedMessage",
						"Safe automatic removal is blocked by the current managed setup. Nothing was changed.\r\n\r\n{0}", blocked),
						L("Collections.Management.RemovalBlockedTitle", "Collection effect removal blocked"),
						MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}

				int removeCount = plan.Impacts.Count(x => x.RequiresNativeRemoval);
				int preserveCount = plan.Impacts.Count - removeCount;
				string confirmation = LanguageManager.Format("Collections.Management.RemoveEffectsPrompt",
					"Remove dispensable effects for '{0}'?\r\n\r\nInstalled mods proven exclusive to this Collection and safe to remove: {1}\r\nShared, standalone, customized, already absent, or conservatively preserved instances: {2}\r\n\r\nNMM will recheck this review before changing anything. Collection tracking is removed only after the reviewed removal finishes and is verified.",
					selected.DisplayName, removeCount, preserveCount);
				if (MessageBox.Show(this, confirmation, L("Collections.Actions.RemoveEffects", "Review removal..."),
					MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes ||
					!IsWorkflowContextCurrent(context, token))
					return;

				_workflowStatusLabel.Text = L("Collections.Management.RemovingEffects",
					"Removing only the reviewed dispensable Collection effects...");
				CollectionUninstallEffectsResult result = await _managementWorkflow.RemoveEffectsAsync(plan, token);
				if (!IsWorkflowContextCurrent(context, token))
					return;
				RefreshManagedAssociations();
				if (result.IsSuccessful)
				{
					_workflowStatusLabel.Text = LanguageManager.Format("Collections.Management.RemovalComplete",
						"Workflow: '{0}' dispensable effects removed and association cleared.", selected.DisplayName);
					MessageBox.Show(this, LanguageManager.Format("Collections.Management.RemovalCompleteMessage",
						"Safe Collection effect removal completed for '{0}'. Preserved/shared state was left in place.", selected.DisplayName),
						L("Collections.Management.RemovalCompleteTitle", "Collection effects removed"),
						MessageBoxButtons.OK, MessageBoxIcon.Information);
				}
				else
					_workflowStatusLabel.Text = L("Collections.Management.RemovalStopped",
						"Workflow: Collection effect removal stopped before a verified complete result; recovery may be required.");
			}
			catch (OperationCanceledException)
			{
				if (IsWorkflowOwnerCurrent(context))
					_workflowStatusLabel.Text = L("Collections.Management.RemovalCancelled",
						"Workflow: Collection effect-removal cancellation requested; NMM will reconcile the installed state before further work.");
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection effect removal failed: " + ex);
				RememberTechnicalFailure("association.remove-failed", ex, context);
				if (IsWorkflowContextCurrent(context, token))
				{
					CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForFailure("association.remove-failed", ex.Message);
					SetWorkflowPresentation(userMessage);
					MessageBox.Show(this, BuildUserDialogMessage(userMessage), L("Collections.Management.RemovalFailed", "Collection effect removal stopped"),
						MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
			finally
			{
				EndWorkflowWork(context);
			}
		}

		private async void RestoreLocalCaptureButton_Click(object sender, EventArgs e)
		{
			CollectionManagementLocalCapture selected = _localCaptureCombo.SelectedItem as CollectionManagementLocalCapture;
			CollectionUiContext context = _localCaptureActionContext;
			if (_managementWorkflow == null || selected == null || _workflowBusy || context == null ||
				context.LocalCapture == null || !context.LocalCapture.Equals(selected.CaptureIdentity)) return;
			CancellationToken token = BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Restoring, L("Collections.LocalRestore.Preparing", "Preparing Local Collection restore review..."));
			try
			{
				CollectionLocalRestorePreview preview = await Task.Run(() =>
					_managementWorkflow.PreviewLocalRestoreAsync(selected.CaptureIdentity, token), token);
				if (!IsWorkflowContextCurrent(context, token)) return;
				if (!preview.IsReadyForRestore)
				{
					string reasons = preview.Plan.Issues.Count == 0
						? L("Collections.LocalRestore.BlockedUnknown", "The saved capture cannot be restored automatically from the current state.")
						: String.Join(Environment.NewLine, preview.Plan.Issues.Select(x => "- " + CollectionUserMessagePresenter.SanitizeInternalTerminology(x.Message)));
					_workflowStatusLabel.Text = L("Collections.LocalRestore.Blocked", "Workflow: Local Collection restore requires action before it can run.");
					MessageBox.Show(this, reasons, L("Collections.LocalRestore.BlockedTitle", "Local Collection restore blocked"),
						MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}

				string review = LanguageManager.Format("Collections.LocalRestore.ReviewPrompt",
					"Restore the saved Local Collection '{0}'?\r\n\r\nReviewed managed members: {1}\r\nCurrent managed registrations to remove: {2}\r\nManaged file targets to restore: {3}\r\n\r\nThis restores the NMM-managed state recorded by the saved Local Collection. The current NMM profile is preserved before changes begin, and previous Collection tracking is reconciled with the restored setup. Unknown or unmanaged files are not blanket-deleted.\r\n\r\nProceed with these reviewed changes?",
					selected.DisplayName, preview.Plan.Members.Count, preview.Plan.CurrentNativeKeysToRemove.Count, preview.Plan.DeploymentTargets.Count);
				if (MessageBox.Show(this, review, L("Collections.Actions.RestoreLocal", "Restore Local Collection..."),
					MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
				{
					if (IsWorkflowContextCurrent(context, token))
						_workflowStatusLabel.Text = L("Collections.LocalRestore.ReviewCancelled", "Workflow: Local Collection restore review cancelled; no restore operation was created.");
					return;
				}
				if (!IsWorkflowContextCurrent(context, token))
					return;

				_workflowStatusLabel.Text = L("Collections.LocalRestore.Applying", "Workflow: restoring Local Collection and verifying the final managed state...");
				CollectionLocalRestoreWorkflowResult result = await Task.Run(() =>
					_managementWorkflow.RestoreLocalCaptureAsync(preview, token), token);
				if (!IsWorkflowContextCurrent(context, token)) return;
				RefreshLocalCaptures();
				RefreshManagedAssociations();
				if (result.IsSuccessful)
				{
					_workflowStatusLabel.Text = LanguageManager.Format("Collections.LocalRestore.Completed",
						"Workflow: Local Collection '{0}' restored and fully verified.", selected.DisplayName);
					MessageBox.Show(this, LanguageManager.Format("Collections.LocalRestore.CompletedMessage",
						"'{0}' was restored. Members, owner stacks/payloads, replay artifacts, supported plugin/configuration state, logical metadata and the profile/association boundary all passed final verification.", selected.DisplayName),
						L("Collections.LocalRestore.CompletedTitle", "Local Collection restored"), MessageBoxButtons.OK, MessageBoxIcon.Information);
				}
				else
				{
					CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForLocalRestore(result.Status, result.Message);
					SetWorkflowPresentation(userMessage);
					MessageBox.Show(this, BuildUserDialogMessage(userMessage), L("Collections.LocalRestore.RecoveryTitle", "Local Collection restore requires recovery"),
						MessageBoxButtons.OK, MessageBoxIcon.Warning);
				}
			}
			catch (OperationCanceledException)
			{
				if (IsWorkflowOwnerCurrent(context))
					_workflowStatusLabel.Text = L("Collections.LocalRestore.Paused", "Workflow: Local Collection restore paused at a safe boundary; startup reconciliation will resume it.");
			}
			catch (Exception ex)
			{
				Trace.TraceError("Local Collection restore failed: " + ex);
				RememberTechnicalFailure("local-restore.failed", ex, context);
				if (IsWorkflowContextCurrent(context, token))
				{
					CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForFailure("local-restore.failed", ex.Message);
					SetWorkflowPresentation(userMessage);
					MessageBox.Show(this, BuildUserDialogMessage(userMessage), L("Collections.LocalRestore.FailedTitle", "Local Collection restore stopped"),
						MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
			finally
			{
				EndWorkflowWork(context);
			}
		}

		private async void SaveCurrentSetupButton_Click(object sender, EventArgs e)
		{
			if (_captureWorkflow == null || _workflowBusy)
				return;

			PromptDialog nameDialog = PromptDialog.ShowDialog(null, this,
				L("Collections.Capture.NamePrompt", "Name for the Local Collection:"),
				L("Collections.Actions.SaveCurrentSetup", "Save current setup as Local Collection"),
				L("Collections.Capture.DefaultName", "Current setup"), null, null);
			if (nameDialog == null || String.IsNullOrWhiteSpace(nameDialog.EnteredText))
				return;

			DialogResult capabilityChoice = MessageBox.Show(this,
				L("Collections.Capture.CapabilityPrompt",
					"Choose the capture promise.\r\n\r\nYes: Locally restorable within scope (fails rather than silently downgrading if required content/state cannot be retained).\r\n\r\nNo: Recipe only (records reconstruction intent even when external acquisition/manual work may still be required)."),
				L("Collections.Capture.CapabilityTitle", "Local Collection capture capability"),
				MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
			if (capabilityChoice == DialogResult.Cancel)
				return;

			LocalCaptureCapability capability = capabilityChoice == DialogResult.Yes
				? LocalCaptureCapability.LocallyRestorableWithinScope
				: LocalCaptureCapability.RecipeOnly;
			_currentSetupActionContext = CollectionUiContext.CurrentSetup(_previewGeneration);
			CollectionUiContext context = _currentSetupActionContext;
			CancellationToken token = BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Capturing, L("Collections.Capture.Saving",
				"Capturing, verifying and retaining the current setup..."));
			try
			{
				var request = new CollectionSaveCurrentSetupRequest(nameDialog.EnteredText, capability);
				CollectionSaveCurrentSetupResult result = await _captureWorkflow.SaveCurrentSetupAsync(request, token);
				if (!IsWorkflowContextCurrent(context, token))
					return;

				if (result.IsSaved)
				{
					RefreshLocalCaptures();
					string capabilityLabel = FormatCaptureCapability(result.Capture.Capability);
					_workflowStatusLabel.Text = LanguageManager.Format("Collections.Capture.SavedStatus",
						"Workflow: Local Collection saved - {0}.", capabilityLabel);
					MessageBox.Show(this, LanguageManager.Format("Collections.Capture.SavedMessage",
						"The current setup was saved as '{0}'.\r\n\r\nCapability: {1}",
						result.Definition.DisplayName, capabilityLabel),
						L("Collections.Capture.SavedTitle", "Local Collection saved"), MessageBoxButtons.OK, MessageBoxIcon.Information);
					return;
				}

				string reasons = result.Issues.Count == 0
					? L("Collections.Capture.NotSealedUnknown", "The capture could not be sealed.")
					: String.Join(Environment.NewLine, result.Issues.Select(x => "- " + CollectionUserMessagePresenter.SanitizeInternalTerminology(x.Message)));
				_workflowStatusLabel.Text = L("Collections.Capture.NotSaved", "Workflow: Local Collection was not saved; requested capability could not be sealed.");
				MessageBox.Show(this, LanguageManager.Format("Collections.Capture.NotSavedMessage",
					"Nothing was saved and the requested capability was not downgraded.\r\n\r\n{0}", reasons),
					L("Collections.Capture.NotSavedTitle", "Local Collection not saved"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
			catch (OperationCanceledException)
			{
				if (IsWorkflowOwnerCurrent(context))
					_workflowStatusLabel.Text = L("Collections.Capture.Cancelled", "Workflow: Local Collection capture cancelled before publication.");
			}
			catch (Exception ex)
			{
				Trace.TraceError("Local Collection capture failed: " + ex);
				RememberTechnicalFailure("capture.failed", ex, context);
				if (IsWorkflowContextCurrent(context, token))
				{
					CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForFailure("capture.failed", ex.Message);
					SetWorkflowPresentation(userMessage);
					MessageBox.Show(this, BuildUserDialogMessage(userMessage), L("Collections.Capture.FailedTitle", "Local Collection capture failed"),
						MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
			finally
			{
				EndWorkflowWork(context);
			}
		}

		private static string FormatCaptureCapability(LocalCaptureCapability capability)
		{
			return capability == LocalCaptureCapability.LocallyRestorableWithinScope
				? L("Collections.Capture.Capability.Restorable", "Locally restorable within scope")
				: L("Collections.Capture.Capability.RecipeOnly", "Recipe only");
		}

		private async void ImportButton_Click(object sender, EventArgs e)
		{
			NexusCollectionPreviewSnapshot sourceSnapshot = _snapshot;
			CollectionUiContext context = _incomingActionContext;
			if (sourceSnapshot == null || !sourceSnapshot.HasConcreteRevision || context == null)
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
				if (dialog.ShowDialog(this) != DialogResult.OK || !IsActionContextCurrent(context))
					return;

				CancellationToken token = BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Importing, L("Collections.Workflow.Importing", "Importing and retaining the Collection source..."));
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

					if (!IsWorkflowContextCurrent(context, token))
						return;
					_snapshot = imported;
					_managedAssociationPresentation = null;
					ResetWorkflowViewState();
					RenderSnapshot(imported);
					_workflowStatusLabel.Text = L("Collections.Workflow.SourceRetained", "Workflow: Collection source retained; choose optional mods and prepare the review.");
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception ex)
				{
					Trace.TraceError("Collection bundle import failed: " + ex);
					RememberTechnicalFailure("bundle.import-failed", ex, context);
					if (IsWorkflowContextCurrent(context, token))
					{
						_contentValue.Text = L("Collections.Status.Content.ImportFailed", "Bundle import failed");
						CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForFailure("bundle.import-failed", ex.Message);
						SetWorkflowPresentation(userMessage);
						AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic, L("Collections.Status.ActionRequired", "Action required"),
							"bundle.import-failed", sourceSnapshot.Revision == null ? String.Empty : sourceSnapshot.Revision.Identity.ToString(), userMessage);
					}
				}
				finally
				{
					EndWorkflowWork(context);
				}
			}
		}

		private async void DownloadPrepareButton_Click(object sender, EventArgs e)
		{
			CollectionUiContext context = _incomingActionContext;
			if (_workflow == null || _snapshot == null || !_snapshot.HasConcreteRevision || context == null)
				return;

			CollectionArchiveOverwritePolicy archiveOverwritePolicy = _autoOverwriteArchivesCheckBox.Checked
				? CollectionArchiveOverwritePolicy.OverwriteExistingArchives
				: CollectionArchiveOverwritePolicy.Prompt;
			CancellationToken token = BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Preparing, L("Collections.Workflow.Preparing", "Preparing Collection installation review..."));
			try
			{
				if (_operationIdentity != null && (_operationSnapshot == null || !_operationSnapshot.HasCrossedNativeBoundary))
					CancelSupersededPreparation();

				NexusCollectionPreviewSnapshot snapshot = _snapshot;
				if (!snapshot.HasManifestPreview)
				{
					_workflowStatusLabel.Text = L("Collections.Workflow.Downloading", "Downloading and retaining the Collection bundle...");
					snapshot = await _workflow.DownloadAndRetainBundleAsync(snapshot, token);
					if (!IsWorkflowContextCurrent(context, token))
						return;
					_snapshot = snapshot;
					_managedAssociationPresentation = null;
					RenderSnapshot(snapshot);
				}

				CollectionEffectiveSelection selection = _workflow.BuildEffectiveSelection(snapshot, BuildOptionalSelections());
				_selectionDirty = false;
				_selectionCapabilityBlocked = false;
				if (selection.CapabilityReport.Status != CollectionCompatibilityStatus.Supported &&
					!CanResolvePreferExactDuringPreparation(selection.CapabilityReport))
				{
					RenderCapabilityPreparationGate(selection.CapabilityReport);
					return;
				}
				CollectionAdditiveWorkflowPreparationResult result = await _workflow.PrepareAsync(
					selection, archiveOverwritePolicy, ConfirmArchiveOverwrite, token);
				if (!IsWorkflowContextCurrent(context, token))
					return;
				RenderPreparationResult(result);
			}
			catch (CollectionArchiveOverwritePolicyConflictException ex)
			{
				Trace.TraceWarning("Collection archive overwrite policy conflict: " + ex);
				RememberTechnicalFailure("acquisition.overwrite-policy-conflict", ex, context);
				if (IsWorkflowContextCurrent(context, token))
				{
					CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForFailure(
						"acquisition.overwrite-policy-conflict", ex.Message);
					SetWorkflowPresentation(userMessage);
					AddPresentedReviewItem(CollectionReviewSeverity.Warning, CollectionReviewItemKind.ManualAction,
						L("Collections.Status.ActionRequired", "Action required"), "acquisition.overwrite-policy-conflict",
						GetCurrentCollectionSubject(), userMessage);
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection additive preparation failed: " + ex);
				RememberTechnicalFailure("workflow.prepare-failed", ex, context);
				if (IsWorkflowContextCurrent(context, token))
				{
					CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForFailure("workflow.prepare-failed", ex.Message);
					SetWorkflowPresentation(userMessage);
					AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic, L("Collections.Status.ActionRequired", "Action required"),
						"workflow.prepare-failed", GetCurrentCollectionSubject(), userMessage);
				}
			}
			finally
			{
				EndWorkflowWork(context);
			}
		}

		private async void ResumeButton_Click(object sender, EventArgs e)
		{
			CollectionUiContext context = _incomingActionContext;
			if (_workflow == null || _acquisitionBatch == null || context == null)
				return;
			CancellationToken token = BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Preparing, L("Collections.Workflow.ResumingPreparation", "Checking acquired member archives and resuming preparation..."));
			try
			{
				CollectionAdditiveWorkflowPreparationResult result = await _workflow.ResumePreparationAsync(_acquisitionBatch, token);
				if (!IsWorkflowContextCurrent(context, token))
					return;
				RenderPreparationResult(result);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection preparation resume failed: " + ex);
				RememberTechnicalFailure("workflow.resume-failed", ex, context);
				if (IsWorkflowContextCurrent(context, token))
				{
					CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForFailure("workflow.resume-failed", ex.Message);
					SetWorkflowPresentation(userMessage);
					AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic, L("Collections.Status.ActionRequired", "Action required"),
						"workflow.resume-failed", GetCurrentCollectionSubject(), userMessage);
				}
			}
			finally
			{
				EndWorkflowWork(context);
			}
		}

		/// <summary>
		/// Keeps an input-paused Premium/bundled acquisition moving once all native AddMod tasks have finished.
		/// Manual/free acquisition remains explicitly user-driven.
		/// </summary>
		private void UpdateAutomatedAcquisitionRefresh(CollectionMemberAcquisitionBatch batch)
		{
			_acquisitionRefreshTimer.Stop();
			RefreshWorkflowActivity();
			if (batch == null || batch.IsReady || !batch.IsAwaitingInput || HasManualAcquisitionAction(batch))
				return;

			if (GetQueuedAcquisitionStates(batch).Count > 0)
				_acquisitionRefreshTimer.Start();
		}

		private async void AcquisitionRefreshTimer_Tick(object sender, EventArgs e)
		{
			RefreshWorkflowActivity();
			if (_workflowBusy || _workflow == null || _acquisitionBatch == null || !_acquisitionBatch.IsAwaitingInput)
				return;

			List<CollectionMemberAcquisitionState> queued = GetQueuedAcquisitionStates(_acquisitionBatch);
			if (queued.Count == 0)
			{
				_acquisitionRefreshTimer.Stop();
				return;
			}

			// Do not auto-retry failed/cancelled native work. The explicit resume button remains available
			// so the next probe can surface the durable acquisition/restart state to the user.
			if (queued.Any(x => x.QueueCorrelation.Task.Status == Nexus.Client.BackgroundTasks.TaskStatus.Error ||
				x.QueueCorrelation.Task.Status == Nexus.Client.BackgroundTasks.TaskStatus.Cancelled))
			{
				_acquisitionRefreshTimer.Stop();
				return;
			}

			if (queued.Any(x => x.QueueCorrelation.Task.Status != Nexus.Client.BackgroundTasks.TaskStatus.Complete))
				return;

			List<Guid> queueOperations = queued.Select(x => x.QueueCorrelation.QueueOperationId).Distinct().ToList();
			if (queueOperations.All(x => _autoResumedQueueOperations.Contains(x)))
			{
				_acquisitionRefreshTimer.Stop();
				return;
			}

			foreach (Guid operation in queueOperations)
				_autoResumedQueueOperations.Add(operation);
			_acquisitionRefreshTimer.Stop();

			CollectionUiContext context = _incomingActionContext;
			if (context == null)
				return;
			CancellationToken token = BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Preparing, L("Collections.Workflow.AcquisitionCompleted",
				"Queued Collection downloads completed; verifying archives and continuing preparation..."));
			try
			{
				CollectionAdditiveWorkflowPreparationResult result = await _workflow.ResumePreparationAsync(_acquisitionBatch, token);
				if (!IsWorkflowContextCurrent(context, token))
					return;
				RenderPreparationResult(result);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection automatic preparation resume failed: " + ex);
				RememberTechnicalFailure("workflow.resume-failed", ex, context);
				if (IsWorkflowContextCurrent(context, token))
				{
					CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForFailure("workflow.resume-failed", ex.Message);
					SetWorkflowPresentation(userMessage);
					AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic, L("Collections.Status.ActionRequired", "Action required"),
						"workflow.resume-failed", GetCurrentCollectionSubject(), userMessage);
				}
			}
			finally
			{
				EndWorkflowWork(context);
			}
		}

		private static List<CollectionMemberAcquisitionState> GetQueuedAcquisitionStates(CollectionMemberAcquisitionBatch batch)
		{
			if (batch == null)
				return new List<CollectionMemberAcquisitionState>();
			return batch.Members.Where(x =>
				(x.Disposition == CollectionMemberAcquisitionDisposition.PremiumQueued ||
				 x.Disposition == CollectionMemberAcquisitionDisposition.BundledQueued) &&
				x.QueueCorrelation != null && x.QueueCorrelation.Task != null).ToList();
		}

		private void OpenPendingButton_Click(object sender, EventArgs e)
		{
			CollectionUiContext context = _incomingActionContext;
			CollectionManualAcquisitionPendingAction pending = GetSelectedOrFirstPendingAction();
			if (context == null || !IsActionContextCurrent(context) || pending == null || pending.BrowserUri == null)
				return;
			try
			{
				Process.Start(pending.BrowserUri.ToString());
			}
			catch (Exception ex)
			{
				RememberTechnicalFailure("acquisition.open-page-failed", ex, context);
				CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForFailure("acquisition.open-page-failed", ex.Message);
				AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.ManualAction, L("Collections.Status.ActionRequired", "Action required"),
					"acquisition.open-page-failed", FormatMemberSubject(pending.Request.MemberKey), userMessage, pending.Request.MemberKey,
					"Member: " + pending.Request.MemberKey);
			}
		}

		private async void InstallButton_Click(object sender, EventArgs e)
		{
			CollectionUiContext context = _incomingActionContext;
			if (_workflow == null || _operationIdentity == null || _reviewedPlanIdentity == null || _selectionDirty || context == null)
				return;
			CollectionOperationIdentity reviewedOperationIdentity = _operationIdentity;
			CollectionPlanIdentity reviewedPlanIdentity = _reviewedPlanIdentity;
			context = context.WithOperation(reviewedOperationIdentity);

			CollectionAdditiveWorkflowReview review;
			CancellationToken reviewToken = BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Reviewing,
				L("Collections.Workflow.ValidatingReview", "Revalidating the reviewed plan before approval..."));
			try
			{
				review = await Task.Run(() => _workflow.GetReview(reviewedOperationIdentity), reviewToken);
				if (!IsWorkflowContextCurrent(context, reviewToken))
					return;
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection review reload failed: " + ex);
				RememberTechnicalFailure("workflow.review-invalid", ex, context);
				if (IsWorkflowContextCurrent(context, reviewToken))
				{
					CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForFailure("workflow.review-invalid", ex.Message);
					AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic, L("Collections.Status.ActionRequired", "Action required"),
						"workflow.review-invalid", GetCurrentCollectionSubject(), userMessage);
					SetWorkflowPresentation(userMessage);
				}
				return;
			}
			finally
			{
				EndWorkflowWork(context);
			}

			if (!review.IsReady || review.Operation.PlanIdentity == null || !review.Operation.PlanIdentity.Equals(reviewedPlanIdentity))
			{
				_workflowStatusLabel.Text = L("Collections.Workflow.ReviewInvalid", "Workflow: the reviewed plan changed or is no longer safely resumable; prepare it again.");
				SetWorkflowActivity(CollectionWorkflowActivityBuilder.Paused(CollectionWorkflowActivityPhase.Reviewing, _workflowStatusLabel.Text, false));
				_installButton.Enabled = false;
				return;
			}

			_operationSnapshot = review.Operation;
			RenderExactReview(review);
			string confirmation = BuildApprovalConfirmation(review);
			if (MessageBox.Show(this, confirmation, L("Collections.Actions.InstallCurrent", "Review and install..."),
				MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes ||
				!IsActionContextCurrent(context))
				return;

			CancellationToken token = BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Applying, L("Collections.Workflow.Applying", "Applying the approved Collection plan..."));
			try
			{
				CollectionAdditiveWorkflowApplyResult result = await Task.Run(() =>
					_workflow.ApproveAndApplyAsync(reviewedOperationIdentity, reviewedPlanIdentity, token), token);
				if (!IsWorkflowContextCurrent(context, token))
					return;
				RenderApplyResult(result);
				RefreshManagedAssociations();
			}
			catch (OperationCanceledException)
			{
				if (IsWorkflowOwnerCurrent(context))
					_workflowStatusLabel.Text = L("Collections.Workflow.CancelRequested", "Workflow: cancellation requested; NMM will reconcile the installed state before any further work.");
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection additive apply failed: " + ex);
				RememberTechnicalFailure("workflow.apply-failed", ex, context);
				if (IsWorkflowContextCurrent(context, token))
				{
					CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForFailure("workflow.apply-failed", ex.Message);
					SetWorkflowPresentation(userMessage);
					AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic, L("Collections.Status.ActionRequired", "Action required"),
						"workflow.apply-failed", GetCurrentCollectionSubject(), userMessage);
				}
			}
			finally
			{
				EndWorkflowWork(context);
			}
		}

		private void ExportTechnicalReportButton_Click(object sender, EventArgs e)
		{
			CollectionTechnicalReportSnapshot report;
			try
			{
				report = BuildTechnicalReportSnapshot();
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection technical report snapshot failed: " + ex);
				MessageBox.Show(this,
					L("Collections.TechnicalReport.BuildFailed", "NMM could not build the technical report for the current Collection state."),
					L("Collections.TechnicalReport.FailedTitle", "Technical report failed"),
					MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			using (var dialog = new SaveFileDialog
			{
				AddExtension = true,
				DefaultExt = "json",
				RestoreDirectory = true,
				Filter = L("Collections.TechnicalReport.JsonFilter", "JSON files (*.json)|*.json|All files (*.*)|*.*"),
				FileName = BuildTechnicalReportFileName(),
				Title = L("Collections.TechnicalReport.SaveTitle", "Export Collections Technical Report")
			})
			{
				if (dialog.ShowDialog(this) != DialogResult.OK)
					return;
				try
				{
					CollectionTechnicalReportSerializer.Save(dialog.FileName, report);
				}
				catch (Exception ex)
				{
					Trace.TraceError("Collection technical report export failed: " + ex);
					MessageBox.Show(this,
						L("Collections.TechnicalReport.SaveFailed", "NMM could not save the technical report to the selected location."),
						L("Collections.TechnicalReport.FailedTitle", "Technical report failed"),
						MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
		}

		private CollectionTechnicalReportSnapshot BuildTechnicalReportSnapshot()
		{
			string version = typeof(CollectionsPreviewControl).Assembly.GetName().Version == null
				? String.Empty
				: typeof(CollectionsPreviewControl).Assembly.GetName().Version.ToString();
			var report = new CollectionTechnicalReportSnapshot(version);
			CollectionUiContext context = _displayContext ?? CollectionUiContext.None(_previewGeneration);
			report.Context = new CollectionTechnicalReportContext
			{
				Kind = context.Kind.ToString(),
				Generation = context.Generation,
				RevisionIdentity = context.Revision == null ? null : context.Revision.ToString(),
				OperationIdentity = context.Operation == null ? (_operationIdentity == null ? null : _operationIdentity.ToString()) : context.Operation.ToString(),
				AssociationIdentity = context.AssociationId.HasValue ? context.AssociationId.Value.ToString("D") : null,
				LocalCaptureIdentity = context.LocalCapture == null ? null : context.LocalCapture.ToString(),
				CompatibilityStatus = CollectionTechnicalReportSanitizer.SanitizeText(_compatibilityValue.Text),
				ContentStatus = CollectionTechnicalReportSanitizer.SanitizeText(_contentValue.Text),
				AppliedStatus = CollectionTechnicalReportSanitizer.SanitizeText(_appliedValue.Text)
			};

			Nexus.Client.GameStorage.GameStoragePathSet targetPaths = null;
			if (_workflow != null)
			{
				try { targetPaths = _workflow.GetTargetPaths(); }
				catch (Exception ex) { report.UnavailableData.Add("target-context: " + CollectionTechnicalReportSanitizer.SanitizeText(ex.Message)); }
			}
			string targetFingerprint = _operationSnapshot == null ? null : _operationSnapshot.Target.ToString();
			if (targetFingerprint == null && _managedAssociationPresentation != null)
				targetFingerprint = _managedAssociationPresentation.Association.Association.Target.ToString();
			if (targetFingerprint == null && _recoveryResults.Count > 0)
				targetFingerprint = _recoveryResults[0].Operation.Target.ToString();
			if (targetFingerprint == null && _localRestoreRecoveryResults.Count > 0)
				targetFingerprint = _localRestoreRecoveryResults[0].Operation.Target.ToString();
			string fallbackGameId = _snapshot != null && _snapshot.Link != null ? _snapshot.Link.GameDomain :
				(_managedAssociationPresentation == null ? null : _managedAssociationPresentation.NexusGameDomain);
			report.Target = new CollectionTechnicalReportTarget
			{
				GameId = CollectionTechnicalReportSanitizer.SanitizeText(targetPaths == null ? fallbackGameId : targetPaths.GameId),
				GameName = targetPaths == null ? null : CollectionTechnicalReportSanitizer.SanitizeText(targetPaths.GameName),
				TargetFingerprint = targetFingerprint
			};
			if (targetPaths == null)
				report.UnavailableData.Add(_workflow == null
					? "target-context: unavailable because the additive application service is not connected"
					: "target-context: unavailable because the active target paths could not be read");
			if (targetFingerprint == null)
				report.UnavailableData.Add("target-fingerprint: unavailable because no durable operation or installed association identifies the target");

			PopulateTechnicalReportCollection(report);
			PopulateTechnicalReportMembers(report);
			PopulateTechnicalReportAcquisition(report);
			PopulateTechnicalReportReview(report);
			PopulateTechnicalReportOperation(report);
			PopulateTechnicalReportRecovery(report);
			PopulateTechnicalReportExceptions(report);

			RefreshWorkflowActivity();
			report.Progress = new CollectionTechnicalReportProgress
			{
				WorkflowStatus = CollectionTechnicalReportSanitizer.SanitizeText(_workflowStatusLabel.Text),
				OperationPhase = _operationSnapshot == null ? null : _operationSnapshot.Phase.ToString(),
				ActivityState = _workflowActivity == null ? null : _workflowActivity.State.ToString(),
				ActivityPhase = _workflowActivity == null ? null : _workflowActivity.Phase.ToString(),
				CommandsLocked = _workflowActivity != null && _workflowActivity.CommandsLocked,
				WorkActive = _workflowActivity != null && _workflowActivity.IsWorkActive,
				BackgroundContinuation = _workflowActivity != null && _workflowActivity.IsBackgroundContinuation,
				ProgressCurrent = _workflowActivity == null ? null : _workflowActivity.Current,
				ProgressTotal = _workflowActivity == null ? null : _workflowActivity.Total,
				ProgressBasis = _workflowActivity == null ? null : _workflowActivity.ProgressBasis,
				ArchiveOverwritePolicy = _acquisitionBatch == null ? null : _acquisitionBatch.ArchiveOverwritePolicy.ToString(),
				EtaAvailable = _workflowEta != null && _workflowEta.IsAvailable,
				EtaEstimating = _workflowEta != null && _workflowEta.IsEstimating,
				EtaSeconds = _workflowEta != null && _workflowEta.Remaining.HasValue ? (long?)Math.Ceiling(_workflowEta.Remaining.Value.TotalSeconds) : null,
				EtaRemainingBytes = _workflowEta == null ? null : _workflowEta.RemainingBytes,
				EtaBytesPerSecond = _workflowEta == null ? null : _workflowEta.BytesPerSecond,
				EtaSampleCount = _workflowEta == null ? 0 : _workflowEta.SampleCount,
				EtaBasis = _workflowEta == null ? "unavailable" : _workflowEta.Basis
			};
			if (_acquisitionBatch != null)
			{
				foreach (CollectionMemberAcquisitionState state in _acquisitionBatch.Members)
				{
					string key = state.Disposition.ToString();
					int count;
					report.Progress.AcquisitionCounts.TryGetValue(key, out count);
					report.Progress.AcquisitionCounts[key] = count + 1;
				}
			}
			else
				report.UnavailableData.Add("acquisition-progress: unavailable because there is no active or retained acquisition batch in this UI context");
			report.UnavailableData.Add("progress-samples: workflow activity and deduplicated acquisition producer counts are available; byte/time samples are deferred to the ETA step");
			report.UnavailableData.Add("eta: unavailable because ETA telemetry is scheduled for a later UX step");
			if (_operationSnapshot == null)
				report.UnavailableData.Add("operation: unavailable because no durable Collection operation exists for the displayed context");
			if (_reviewedPlanIdentity == null)
				report.UnavailableData.Add("reviewed-plan: unavailable because no exact approved/reviewable plan identity exists for the displayed context");
			return report;
		}

		private void PopulateTechnicalReportCollection(CollectionTechnicalReportSnapshot report)
		{
			CollectionDefinition definition = _snapshot == null ? null : _snapshot.Definition;
			CollectionRevision revision = _snapshot == null ? null : _snapshot.Revision;
			if (_managedAssociationPresentation != null)
			{
				definition = _managedAssociationPresentation.Definition ?? definition;
				revision = _managedAssociationPresentation.Revision ?? revision;
			}
			CollectionRevisionIdentity revisionIdentity = revision == null
				? (_displayContext == null ? null : _displayContext.Revision)
				: revision.Identity;
			string fallbackDisplayName = _managedAssociationPresentation == null
				? _collectionValue.Text
				: _managedAssociationPresentation.Association.DisplayName;
			string fallbackRevisionLabel = _managedAssociationPresentation == null
				? _revisionValue.Text
				: _managedAssociationPresentation.Association.RevisionLabel;
			report.Collection = new CollectionTechnicalReportCollection
			{
				Identity = definition == null ? (revisionIdentity == null ? null : revisionIdentity.Collection.ToString()) : definition.Identity.ToString(),
				DisplayName = CollectionTechnicalReportSanitizer.SanitizeText(definition == null ? fallbackDisplayName : definition.DisplayName),
				Curator = CollectionTechnicalReportSanitizer.SanitizeText(definition == null ? _curatorValue.Text : definition.AuthorDisplayName),
				RevisionIdentity = revisionIdentity == null ? null : revisionIdentity.ToString(),
				RevisionLabel = CollectionTechnicalReportSanitizer.SanitizeText(revision == null ? fallbackRevisionLabel : revision.RevisionLabel),
				Locator = _snapshot == null || _snapshot.Link == null || _snapshot.Link.SourceUri == null
					? null
					: CollectionTechnicalReportSanitizer.SanitizeText(_snapshot.Link.SourceUri.ToString())
			};
		}

		private void PopulateTechnicalReportMembers(CollectionTechnicalReportSnapshot report)
		{
			Dictionary<int, CollectionMemberCapabilityReport> capabilityByOrdinal = new Dictionary<int, CollectionMemberCapabilityReport>();
			if (_snapshot != null && _snapshot.CapabilityReport != null)
				foreach (CollectionMemberCapabilityReport memberReport in _snapshot.CapabilityReport.MemberReports)
					capabilityByOrdinal[memberReport.Member.SourceOrdinal] = memberReport;

			foreach (ListViewItem item in _membersView.Items)
			{
				NormalizedCollectionMember member = item.Tag as NormalizedCollectionMember;
				if (member == null)
					continue;
				CollectionMemberCapabilityReport capability;
				capabilityByOrdinal.TryGetValue(member.SourceOrdinal, out capability);
				report.Members.Add(new CollectionTechnicalReportMember
				{
					MemberKey = member.IdentityResolution.IsResolved ? member.IdentityResolution.Key.ToString() : null,
					SourceOrdinal = member.SourceOrdinal,
					DisplayName = CollectionTechnicalReportSanitizer.SanitizeText(member.DisplayName),
					Requirement = member.Requirement.ToString(),
					Selected = member.IsRequired || item.Checked,
					Compatibility = capability == null ? null : capability.Status.ToString(),
					Artifact = member.Artifact == null ? null : CollectionTechnicalReportSanitizer.SanitizeText(member.Artifact.ToString())
				});
			}
		}

		private void PopulateTechnicalReportAcquisition(CollectionTechnicalReportSnapshot report)
		{
			if (_acquisitionBatch == null)
				return;
			foreach (CollectionMemberAcquisitionState state in _acquisitionBatch.Members)
			{
				CollectionAcquisitionRequest request = state.Request ?? (state.QueueCorrelation == null ? null : state.QueueCorrelation.Request);
				CollectionVerifiedArchive verified = state.VerifiedArchive;
				report.Acquisitions.Add(new CollectionTechnicalReportAcquisition
				{
					MemberKey = state.Match.Member.MemberKey.ToString(),
					Disposition = state.Disposition.ToString(),
					MatchDisposition = state.Match.Disposition.ToString(),
					MatchReason = state.Match.Reason.ToString(),
					RequestId = request == null ? null : request.RequestId.ToString("D"),
					QueueOperationId = state.QueueCorrelation == null ? null : state.QueueCorrelation.QueueOperationId.ToString("D"),
					ArchiveSource = verified == null ? null : verified.SourceKind.ToString(),
					VerificationBasis = verified == null ? null : verified.VerificationBasis.ToString(),
					RetainedArtifactId = verified == null ? null : verified.Artifact.ArtifactId,
					ContentHash = verified == null ? null : verified.Artifact.ContentHash.ToString(),
					ByteLength = verified == null ? (long?)null : verified.Artifact.ByteLength,
					PendingActions = state.PendingAction == null ? null : state.PendingAction.AllowedActions.ToString(),
					BrowserUri = state.PendingAction == null || state.PendingAction.BrowserUri == null
						? null
						: CollectionTechnicalReportSanitizer.SanitizeText(state.PendingAction.BrowserUri.ToString())
				});
			}
		}

		private void PopulateTechnicalReportReview(CollectionTechnicalReportSnapshot report)
		{
			foreach (CollectionReviewItem item in _reviewItems.ToArray())
			{
				report.ReviewItems.Add(new CollectionTechnicalReportReviewItem
				{
					Severity = item.Severity.ToString(),
					Kind = item.Kind.ToString(),
					Code = item.Code,
					Subject = CollectionTechnicalReportSanitizer.SanitizeText(item.Subject),
					Explanation = CollectionTechnicalReportSanitizer.SanitizeText(item.Explanation),
					NextAction = CollectionTechnicalReportSanitizer.SanitizeText(item.NextAction),
					TechnicalDetail = CollectionTechnicalReportSanitizer.SanitizeText(item.TechnicalDetail),
					MemberKey = item.MemberKey == null ? null : item.MemberKey.ToString()
				});
			}
		}

		private void PopulateTechnicalReportOperation(CollectionTechnicalReportSnapshot report)
		{
			CollectionOperation operation = _operationSnapshot;
			if (operation == null)
				return;
			var item = new CollectionTechnicalReportOperation
			{
				Identity = operation.Identity.ToString(),
				Kind = operation.Kind.ToString(),
				Phase = operation.Phase.ToString(),
				ResultState = operation.ResultState.ToString(),
				PlanIdentity = operation.PlanIdentity == null ? null : operation.PlanIdentity.ToString(),
				CheckpointSequence = operation.CheckpointSequence,
				RequiresRecovery = operation.RequiresRecovery,
				HasCrossedNativeBoundary = operation.HasCrossedNativeBoundary
			};
			foreach (CollectionNativeChildOperation child in operation.NativeChildren)
			{
				item.NativeChildren.Add(new CollectionTechnicalReportNativeChild
				{
					Sequence = child.Sequence,
					Member = child.Member.ToString(),
					Action = child.Action.ToString(),
					OperationId = child.NativeOperation.OperationId.ToString("D"),
					AttemptId = child.NativeOperation.AttemptId.ToString("D"),
					Origin = child.NativeOperation.Origin.ToString(),
					Checkpoint = child.Checkpoint.ToString(),
					ReportedStatus = child.NativeResult == null ? null : child.NativeResult.ReportedStatus.ToString(),
					Durability = child.NativeResult == null ? null : child.NativeResult.Durability.ToString(),
					Message = child.NativeResult == null ? null : CollectionTechnicalReportSanitizer.SanitizeText(child.NativeResult.Message)
				});
			}
			report.Operation = item;
		}

		private void PopulateTechnicalReportRecovery(CollectionTechnicalReportSnapshot report)
		{
			foreach (CollectionAdditiveWorkflowRecoveryResult result in _recoveryResults ?? new CollectionAdditiveWorkflowRecoveryResult[0])
				report.Recovery.Add(new CollectionTechnicalReportRecoveryItem
				{
					Scope = "additive",
					Status = result.Status.ToString(),
					OperationIdentity = result.Operation.Identity.ToString(),
					Phase = result.Operation.Phase.ToString(),
					ResultState = result.Operation.ResultState.ToString(),
					Message = CollectionTechnicalReportSanitizer.SanitizeText(result.Message)
				});
			foreach (CollectionLocalRestoreWorkflowResult result in _localRestoreRecoveryResults ?? new CollectionLocalRestoreWorkflowResult[0])
				report.Recovery.Add(new CollectionTechnicalReportRecoveryItem
				{
					Scope = "local-restore",
					Status = result.Status.ToString(),
					OperationIdentity = result.Operation.Identity.ToString(),
					Phase = result.Operation.Phase.ToString(),
					ResultState = result.Operation.ResultState.ToString(),
					Message = CollectionTechnicalReportSanitizer.SanitizeText(result.Message)
				});
		}

		private void PopulateTechnicalReportExceptions(CollectionTechnicalReportSnapshot report)
		{
			if (_snapshot != null)
			{
				AddTechnicalReportException(report, "provider.revision", _snapshot.RevisionError);
				AddTechnicalReportException(report, "provider.summary", _snapshot.SummaryError);
			}
			if (_lastTechnicalFailure != null && _lastTechnicalFailureGeneration == _previewGeneration)
				AddTechnicalReportException(report, _lastTechnicalFailureCode ?? "ui.last-failure", _lastTechnicalFailure);
			if (report.Exceptions.Count == 0)
				report.UnavailableData.Add("exceptions: no exception object is currently retained for this UI context; diagnostic technicalDetail fields may still contain the reported failure text");
		}

		private static void AddTechnicalReportException(CollectionTechnicalReportSnapshot report, string source, Exception exception)
		{
			for (Exception current = exception; current != null; current = current.InnerException)
			{
				report.Exceptions.Add(new CollectionTechnicalReportException
				{
					Source = source,
					Type = current.GetType().FullName,
					Message = CollectionTechnicalReportSanitizer.SanitizeText(current.Message),
					StackTrace = CollectionTechnicalReportSanitizer.SanitizeText(current.StackTrace)
				});
			}
		}

		private string BuildTechnicalReportFileName()
		{
			string name = _snapshot != null && _snapshot.Definition != null ? _snapshot.Definition.DisplayName :
				(_managedAssociationPresentation != null ? _managedAssociationPresentation.Association.DisplayName : "collection");
			if (String.IsNullOrWhiteSpace(name))
				name = "collection";
			foreach (char value in Path.GetInvalidFileNameChars())
				name = name.Replace(value, '_');
			name = name.Trim();
			if (name.Length == 0)
				name = "collection";
			if (name.Length > 80)
				name = name.Substring(0, 80);
			return "NMM-Collections-Report-" + name + ".json";
		}

		private bool HasTechnicalReportContent()
		{
			return _snapshot != null || _managedAssociationPresentation != null || _operationSnapshot != null ||
				_reviewItems.Count > 0 || _recoveryResults.Count > 0 || _localRestoreRecoveryResults.Count > 0;
		}

		private void RememberTechnicalFailure(string code, Exception exception, CollectionUiContext context = null)
		{
			if (exception == null)
				return;
			int generation = context == null ? _previewGeneration : context.Generation;
			if (generation != _previewGeneration)
				return;
			_lastTechnicalFailureCode = code;
			_lastTechnicalFailure = exception;
			_lastTechnicalFailureGeneration = generation;
		}

		private void ClearButton_Click(object sender, EventArgs e)
		{
			CancelPreviewWork();
			CancelWorkflowWork();
			_displayContext = CollectionUiContext.None(_previewGeneration);
			TryCancelUnappliedPreparation();
			_snapshot = null;
			_managedAssociationPresentation = null;
			ResetWorkflowViewState();
			ShowSelectedManagedAssociation();
		}

		private async void BeginRecoveryReconciliation()
		{
			if ((_workflow == null && _managementWorkflow == null) || _workflowBusy || IsDisposed || Disposing)
				return;
			CollectionUiContext context = CollectionUiContext.CurrentSetup(_previewGeneration);
			CancellationToken token = BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Recovering, L("Collections.Workflow.Recovering", "Checking incomplete Collection operations for the active target..."));
			try
			{
				IReadOnlyList<CollectionAdditiveWorkflowRecoveryResult> results = _workflow == null
					? new CollectionAdditiveWorkflowRecoveryResult[0]
					: await _workflow.ReconcileIncompleteTargetAsync(token);
				IReadOnlyList<CollectionLocalRestoreWorkflowResult> localRestoreResults = _managementWorkflow == null
					? new CollectionLocalRestoreWorkflowResult[0]
					: await Task.Run(() => _managementWorkflow.ReconcileInterruptedLocalRestoresAsync(token), token);
				IReadOnlyList<CollectionUninstallEffectsResult> effectRemovalResults = new CollectionUninstallEffectsResult[0];
				if (_managementWorkflow != null && localRestoreResults.All(x => x.IsSuccessful))
					effectRemovalResults = await _managementWorkflow.ReconcileInterruptedEffectRemovalAsync(token);

				_recoveryResults = results ?? new CollectionAdditiveWorkflowRecoveryResult[0];
				_localRestoreRecoveryResults = localRestoreResults ?? new CollectionLocalRestoreWorkflowResult[0];
				if (!IsWorkflowContextCurrent(context, token))
					return;

				// Applied-member metadata enrichment already runs after successful additive finalization. Startup recovery must
				// not turn an idle Collections tab into an unsolicited Nexus metadata scan for previously applied Collections.
				RefreshLocalCaptures();
				RefreshManagedAssociations();
				if (_snapshot != null)
				{
					RenderIssues(_snapshot);
					BindMatchingRecovery();
				}
				else
				{
					int unresolvedCount = _recoveryResults.Count + _localRestoreRecoveryResults.Count(x => !x.IsSuccessful);
					int completedLocalRestores = _localRestoreRecoveryResults.Count(x => x.IsSuccessful);
					if (unresolvedCount > 0)
						_workflowStatusLabel.Text = LanguageManager.Format("Collections.Workflow.IncompleteCount",
							"Workflow: {0} incomplete Collection operation(s) reconciled for this target. Review the reported recovery state before new managed work.", unresolvedCount);
					else if (completedLocalRestores > 0)
						_workflowStatusLabel.Text = LanguageManager.Format("Collections.LocalRestore.StartupCompleted",
							"Workflow: {0} interrupted Local Collection restore operation(s) resumed and fully verified.", completedLocalRestores);
					else
						_workflowStatusLabel.Text = L("Collections.Workflow.Idle", "Workflow: idle");
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection startup reconciliation failed: " + ex);
				RememberTechnicalFailure("workflow.recovery-failed", ex, context);
				if (IsWorkflowContextCurrent(context, token))
				{
					CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForFailure("workflow.recovery-failed", ex.Message);
					SetWorkflowPresentation(userMessage);
					AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic, L("Collections.Status.ActionRequired", "Action required"),
						"workflow.recovery-failed", GetCurrentCollectionSubject(), userMessage);
				}
			}
			finally
			{
				EndWorkflowWork(context);
			}
		}

		private void MembersView_SelectedIndexChanged(object sender, EventArgs e)
		{
			UpdatePendingDownloadActionLabel(GetSelectedOrFirstPendingAction());
		}

		private void MembersView_ItemCheck(object sender, ItemCheckEventArgs e)
		{
			if (_suppressMemberCheckEvents || e.Index < 0 || e.Index >= _membersView.Items.Count)
				return;
			if (_managedAssociationPresentation != null)
			{
				e.NewValue = e.CurrentValue;
				return;
			}
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
			CollectionUiContext selectionContext = _incomingActionContext;
			if (selectionContext == null)
			{
				e.NewValue = e.CurrentValue;
				return;
			}
			BeginInvoke((Action)(() =>
			{
				if (!IsActionContextCurrent(selectionContext))
					return;
				_selectionDirty = true;
				_selectionCapabilityBlocked = false;
				if (_operationSnapshot == null || !_operationSnapshot.HasCrossedNativeBoundary)
				{
					TryCancelUnappliedPreparation();
					_operationIdentity = null;
					_operationSnapshot = null;
					ClearIncomingDisplayOperation();
					_reviewedPlanIdentity = null;
					_preparation = null;
					_acquisitionBatch = null;
				}
				_installButton.Enabled = false;
				UpdateMemberSelectionText();
				_workflowStatusLabel.Text = L("Collections.Workflow.SelectionChanged", "Workflow: optional selection changed; prepare a new review before installation.");
				RefreshWorkflowActivity();
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
			_locatorValue.Text = snapshot.Link != null
				? snapshot.Link.GameDomain + " / " + snapshot.Link.CollectionSlug
				: (_managedAssociationPresentation != null ? FormatManagedAssociationLocator(_managedAssociationPresentation) : L("Collections.Value.Unknown", "Unknown"));
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
			ApplyManagedAssociationPresentation();

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
			RenderIssues(snapshot, true, true);
		}

		private static bool CapabilityIssueAffectsSelectedOperation(CollectionCapabilityReport report, CollectionCapabilityIssue issue)
		{
			if (report == null || issue == null || issue.Target == CollectionCapabilityIssueTarget.Manifest)
				return true;

			CollectionMemberCapabilityReport memberReport = report.MemberReports.FirstOrDefault(x =>
				x.Member.SourceOrdinal == issue.SourceOrdinal.GetValueOrDefault(-1));
			return memberReport == null || !memberReport.IsUnselectedOptional;
		}

		/// <summary>
		/// Renders provider diagnostics, optionally including raw manifest capability and startup-recovery rows.
		/// Exact reviewed plans suppress those pre-review rows because their decisions have already been resolved durably.
		/// </summary>
		private void RenderIssues(NexusCollectionPreviewSnapshot snapshot, bool includeCapabilityIssues, bool includeRecoveryIssues)
		{
			_issuesView.BeginUpdate();
			try
			{
				ClearReviewItems();
				if (snapshot.RevisionError != null)
					AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic, L("Collections.Status.ActionRequired", "Action required"),
						"provider.revision-failed", GetCurrentCollectionSubject(), CollectionUserMessagePresenter.ForProviderMessage(snapshot.RevisionError.Message, true));
				if (snapshot.SummaryError != null)
					AddPresentedReviewItem(CollectionReviewSeverity.Warning, CollectionReviewItemKind.Diagnostic, L("Collections.Status.ActionRequired", "Action required"),
						"provider.summary-failed", GetCurrentCollectionSubject(), CollectionUserMessagePresenter.ForProviderMessage(snapshot.SummaryError.Message, false));
				if (!string.IsNullOrWhiteSpace(snapshot.MetadataWarning))
					AddPresentedReviewItem(CollectionReviewSeverity.Warning, CollectionReviewItemKind.Diagnostic, L("Collections.Status.ActionRequired", "Action required"),
						"provider.identity-mismatch", GetCurrentCollectionSubject(), CollectionUserMessagePresenter.ForProviderMessage(snapshot.MetadataWarning, false));
				AddGraphQlErrors("provider.revision", snapshot.RevisionLookup?.Errors, CollectionReviewSeverity.Error);
				AddGraphQlErrors("provider.summary", snapshot.SummaryLookup?.Errors, CollectionReviewSeverity.Warning);
				if (includeCapabilityIssues && snapshot.HasManifestPreview)
				{
					foreach (CollectionCapabilityIssue issue in snapshot.CapabilityReport.AllIssues)
					{
						string subject = issue.MemberKey == null ? issue.FieldPath ?? String.Empty : FormatMemberSubject(issue.MemberKey);
						AddPresentedReviewItem(CollectionReviewPresentationClassifier.ForCapabilityIssue(issue.Status, CapabilityIssueAffectsSelectedOperation(snapshot.CapabilityReport, issue)),
							CollectionReviewItemKind.Diagnostic, FormatCompatibility(issue.Status), issue.Code, subject,
							CollectionUserMessagePresenter.ForCapability(issue.Status, issue.Reason), issue.MemberKey,
							CombineTechnicalDetail(issue.FieldPath, issue.MemberKey == null ? null : "Member: " + issue.MemberKey));
					}
				}
				if (!snapshot.HasConcreteRevision && snapshot.RevisionError == null)
					AddReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic, L("Collections.Status.ActionRequired", "Action required"), "provider.identity-incomplete", string.Empty,
						L("Collections.Preview.IdentityIncomplete", "The provider response did not contain the stable collection and revision identity required for a trusted manifest preview."));
				if (includeRecoveryIssues)
					AppendRecoveryIssues();
				UpdateIssuesHeader();
			}
			finally
			{
				_issuesView.EndUpdate();
			}
		}

		private static bool CanResolvePreferExactDuringPreparation(CollectionCapabilityReport report)
		{
			if (report == null || report.Status != CollectionCompatibilityStatus.ActionRequired)
				return false;
			if (report.ManifestIssues.Count > 0)
				return false;

			bool sawResolvablePrefer = false;
			foreach (CollectionMemberCapabilityReport memberReport in report.MemberReports.Where(x => x.Member.IsSelected))
			{
				foreach (CollectionCapabilityIssue issue in memberReport.Issues)
				{
					if (issue.Status == CollectionCompatibilityStatus.Unsupported ||
						!StringComparer.Ordinal.Equals(issue.Code, CollectionNexusPreferExactPolicyResolver.PreferIssueCode))
						return false;
					sawResolvablePrefer = true;
				}
			}
			return sawResolvablePrefer;
		}

		private void RenderCapabilityPreparationGate(CollectionCapabilityReport report)
		{
			if (report == null)
				throw new ArgumentNullException(nameof(report));

			CollectionCapabilityIssue blockingIssue = report.ManifestIssues.FirstOrDefault(x => x.Status == report.Status) ??
				report.MemberReports.Where(x => x.IsSelected).SelectMany(x => x.Issues).FirstOrDefault(x => x.Status == report.Status) ??
				report.AllIssues.FirstOrDefault(x => x.Status == report.Status);
			string reason = blockingIssue == null
				? L("Collections.Workflow.CapabilityGate", "The selected Collection behavior is outside the currently supported automatic-installation capability.")
				: blockingIssue.Reason;
			CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForCapability(report.Status, reason);

			_preparation = null;
			_acquisitionBatch = null;
			_selectionCapabilityBlocked = true;
			_operationIdentity = null;
			_operationSnapshot = null;
			ClearIncomingDisplayOperation();
			_reviewedPlanIdentity = null;
			if (_snapshot != null)
				RenderIssues(_snapshot);
			AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic, FormatCompatibility(report.Status),
				"workflow.capability-gate", blockingIssue == null ? GetCurrentCollectionSubject() : blockingIssue.FieldPath ?? GetCurrentCollectionSubject(), userMessage,
				blockingIssue == null ? null : blockingIssue.MemberKey, blockingIssue == null ? null : CombineTechnicalDetail(blockingIssue.FieldPath, blockingIssue.MemberKey == null ? null : "Member: " + blockingIssue.MemberKey));
			_contentValue.Text = report.Status == CollectionCompatibilityStatus.ActionRequired
				? L("Collections.Status.Content.ActionRequired", "Selected Collection behavior requires a concrete decision before preparation")
				: L("Collections.Status.Content.Blocked", "Selected Collection behavior is not supported by the current automatic-installation capability");
			_appliedValue.Text = L("Collections.Status.Applied.Blocked", "Not applied - blocked before installation preparation");
			SetWorkflowPresentation(userMessage);
			UpdateIssuesHeader();
			UpdateActionButtons();
		}

		private void RenderPreparationResult(CollectionAdditiveWorkflowPreparationResult result)
		{
			_preparation = result ?? throw new ArgumentNullException(nameof(result));
			_acquisitionBatch = result.AcquisitionBatch;
			UpdateAutomatedAcquisitionRefresh(_acquisitionBatch);
			_operationIdentity = result.Operation.Identity;
			_operationSnapshot = result.Operation;
			BindIncomingDisplayOperation(_operationIdentity);
			_reviewedPlanIdentity = result.IsReadyForReview ? result.Operation.PlanIdentity : null;
			_selectionDirty = false;
			if (_snapshot != null)
				RenderIssues(_snapshot, !result.IsReadyForReview, !result.IsReadyForReview);

			bool preparationHasManualAction = HasManualAcquisitionAction(result.AcquisitionBatch);
			bool preparationHasBlockedAcquisition = result.AcquisitionBatch != null &&
				result.AcquisitionBatch.Members.Any(x => x.Disposition == CollectionMemberAcquisitionDisposition.Blocked);
			CollectionReviewItemKind preparationKind = result.Status == CollectionAdditiveWorkflowPreparationStatus.AwaitingInput && preparationHasManualAction
				? CollectionReviewItemKind.ManualAction
				: (CollectionReviewPresentationClassifier.ForPreparation(result.Status, preparationHasManualAction, preparationHasBlockedAcquisition) == CollectionReviewSeverity.Info
					? CollectionReviewItemKind.Progress
					: CollectionReviewItemKind.Diagnostic);
			CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForPreparation(result.Status, result.Message,
				preparationHasManualAction, preparationHasBlockedAcquisition);
			AddPresentedReviewItem(CollectionReviewPresentationClassifier.ForPreparation(result.Status, preparationHasManualAction, preparationHasBlockedAcquisition),
				preparationKind, FormatPreparationStatus(result), "workflow.preparation", GetCurrentCollectionSubject(), userMessage, null,
				"Operation: " + result.Operation.Identity);
			AppendAcquisitionReview(result.AcquisitionBatch);
			AppendDependencyReview(result.DependencyPlan);
			AppendImpactReview(result.ImpactPlan);
			UpdateIssuesHeader();

			switch (result.Status)
			{
				case CollectionAdditiveWorkflowPreparationStatus.AwaitingInput:
					if (HasManualAcquisitionAction(result.AcquisitionBatch))
					{
						_contentValue.Text = L("Collections.Status.Content.AwaitingInput", "Member archives awaiting download / user input");
						_appliedValue.Text = L("Collections.Status.Applied.Preparing", "Not applied - preparation paused for input");
					}
					else
					{
						_contentValue.Text = L("Collections.Status.Content.AcquisitionPending", "Member archives are downloading or queued for NMM import");
						_appliedValue.Text = L("Collections.Status.Applied.AcquisitionPending", "Not applied - waiting for member acquisition");
					}
					break;
				case CollectionAdditiveWorkflowPreparationStatus.ReadyForReview:
					_contentValue.Text = L("Collections.Status.Content.ReviewReady", "All required content verified; installation changes are ready for review");
					_appliedValue.Text = L("Collections.Status.Applied.ReviewReady", "Not applied - reviewed changes await explicit approval");
					break;
				case CollectionAdditiveWorkflowPreparationStatus.ActionRequired:
					_contentValue.Text = L("Collections.Status.Content.ActionRequired", "Prepared content requires a user decision that NMM cannot apply automatically");
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
			SetWorkflowPresentation(userMessage);
			RefreshWorkflowActivity();
			UpdateActionButtons();
		}

		private void RenderApplyResult(CollectionAdditiveWorkflowApplyResult result)
		{
			if (result == null)
				return;
			_operationIdentity = result.Operation.Identity;
			_operationSnapshot = result.Operation;
			BindIncomingDisplayOperation(_operationIdentity);
			CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForApply(result.Status, result.Message);
			SetWorkflowPresentation(userMessage);
			AddPresentedReviewItem(CollectionReviewPresentationClassifier.ForApply(result.Status),
				result.IsCommitted ? CollectionReviewItemKind.Progress : CollectionReviewItemKind.Diagnostic,
				result.IsCommitted ? L("Collections.Status.Supported", "Ready") : L("Collections.Status.ActionRequired", "Action required"),
				"workflow.apply-result", GetCurrentCollectionSubject(), userMessage, null,
				CombineTechnicalDetail("Apply status: " + result.Status, "Operation: " + result.Operation.Identity));
			UpdateIssuesHeader();
			if (result.IsCommitted)
			{
				_appliedValue.Text = L("Collections.Status.Applied.Applied", "Applied and verified");
				_contentValue.Text = L("Collections.Status.Content.Applied", "Prepared content applied and verified");
				_compatibilityValue.Text = L("Collections.Status.Compatibility.AppliedVerified", "Supported - applied revision verified");
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
			RefreshWorkflowActivity();
			UpdateActionButtons();
		}

		private void AppendAcquisitionReview(CollectionMemberAcquisitionBatch batch)
		{
			if (batch == null)
				return;
			foreach (CollectionMemberAcquisitionState state in batch.Members)
			{
				CollectionMemberKey memberKey = state.Match.Member.MemberKey;
				string technical = "Disposition: " + state.Disposition;
				if (state.PendingAction != null)
					technical += "; allowed manual actions: " + state.PendingAction.AllowedActions;
				CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForAcquisition(state.Disposition, technical);
				AddPresentedReviewItem(CollectionReviewPresentationClassifier.ForAcquisition(state.Disposition),
					CollectionReviewPresentationClassifier.KindForAcquisition(state.Disposition), FormatAcquisitionStatus(state.Disposition),
					"acquisition." + state.Disposition.ToString().ToLowerInvariant(), FormatMemberSubject(memberKey), userMessage, memberKey,
					"Member: " + memberKey);
			}

			if (batch.ArchiveOverwritePolicy.AutomaticallyOverwritesExistingArchives &&
				batch.Members.Any(x => x.PendingAction != null))
			{
				AddReviewItem(CollectionReviewSeverity.Warning, CollectionReviewItemKind.ManualAction,
					L("Collections.Status.ActionRequired", "Action required"), "acquisition.auto-overwrite-manual-limit",
					GetCurrentCollectionSubject(), L("Collections.Messages.Acquisition.AutoOverwriteManualLimit",
						"Automatic archive overwrite applies only to Collection downloads/imports queued directly by this preparation. A browser or other generic manual Add Mod return keeps the normal archive-overwrite prompt."), null,
					L("Collections.Messages.Next.CompleteManualDownloadNormally",
						"Complete the manual download normally, then choose Check downloads and continue."),
					"Archive overwrite policy: " + batch.ArchiveOverwritePolicy);
			}
		}

		private void AppendDependencyReview(CollectionDependencyPhasePlan dependencyPlan)
		{
			if (dependencyPlan == null)
				return;
			foreach (CollectionExecutionPhase phase in dependencyPlan.Phases)
			{
				string members = string.Join(", ", phase.Members.Select(x => FormatMemberSubject(x.MemberKey)));
				string technicalMembers = string.Join(", ", phase.Members.Select(x => x.MemberKey.ToString()));
				AddReviewItem(CollectionReviewSeverity.Info, CollectionReviewItemKind.Progress, L("Collections.Status.Supported", "Review"),
					"dependency.phase", LanguageManager.Format("Collections.Review.InstallGroup", "Install group {0}", phase.PhaseNumber), members, null,
					String.Empty, "Members: " + technicalMembers);
			}
			foreach (CollectionDependencyPhaseIssue issue in dependencyPlan.Issues)
			{
				string subject = issue.MemberKey == null ? GetCurrentCollectionSubject() : FormatMemberSubject(issue.MemberKey);
				AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic, L("Collections.Status.Unsupported", "Blocked"),
					"dependency." + issue.Kind.ToString().ToLowerInvariant(), subject, CollectionUserMessagePresenter.ForDependency(issue.Reason),
					issue.MemberKey, CombineTechnicalDetail("Dependency issue: " + issue.Kind, issue.MemberKey == null ? null : "Member: " + issue.MemberKey));
			}
		}

		private void AppendImpactReview(CollectionConflictImpactPlan impactPlan)
		{
			if (impactPlan == null)
				return;
			foreach (CollectionConflictImpactIssue issue in impactPlan.Issues)
			{
				string subject = issue.MemberKey == null
					? (String.IsNullOrWhiteSpace(issue.SubjectKey) ? GetCurrentCollectionSubject() : issue.SubjectKey)
					: FormatMemberSubject(issue.MemberKey);
				AddPresentedReviewItem(CollectionReviewPresentationClassifier.ForImpact(issue.Status), CollectionReviewItemKind.Diagnostic,
					FormatImpactStatus(issue.Status), "impact." + issue.Kind.ToString().ToLowerInvariant(), subject,
					CollectionUserMessagePresenter.ForImpact(issue.Status, issue.Message), issue.MemberKey,
					CombineTechnicalDetail("Impact kind: " + issue.Kind, issue.SubjectKey, issue.MemberKey == null ? null : "Member: " + issue.MemberKey));
			}

			foreach (CollectionFileImpact impact in impactPlan.FileImpacts)
			{
				string winnerName = impact.PlannedWinner == null ? L("Collections.Review.NoPlannedWinner", "No Collection member") : FormatMemberSubject(impact.PlannedWinner);
				string explanation = impact.PlannedWinner == null
					? L("Collections.Review.FileNoWinner", "No Collection member is selected as the final provider for this file.")
					: LanguageManager.Format("Collections.Review.FileWinner", "After installation, {0} is planned to provide this file.", winnerName);
				string technical = CombineTechnicalDetail(
					"Planned winner: " + (impact.PlannedWinner == null ? "none" : impact.PlannedWinner.ToString()),
					"Writers: " + string.Join(", ", impact.Writers.Select(x => x.ToString())),
					"Current owner: " + (impact.CurrentOwnerKey ?? "none"));
				AddReviewItem(CollectionReviewSeverity.Info, CollectionReviewItemKind.PlannedEffect, L("Collections.Status.Supported", "Review"),
					"impact.file", impact.Target.ToString(), explanation, null,
					L("Collections.Messages.Next.ReviewPlannedChange", "Review this planned change before installing."), technical);
			}

			foreach (CollectionPluginImpact impact in impactPlan.PluginImpacts)
			{
				string memberName = FormatMemberSubject(impact.MemberKey);
				string plugins = string.Join(", ", impact.Effect.PluginPaths);
				string explanation;
				switch (impact.Effect.Kind)
				{
					case CollectionPlannedPluginEffectKind.Activation:
						explanation = LanguageManager.Format(impact.Effect.Active.GetValueOrDefault()
							? "Collections.Review.PluginEnable" : "Collections.Review.PluginDisable",
							impact.Effect.Active.GetValueOrDefault() ? "{0} will enable {1}." : "{0} will disable {1}.", memberName, plugins);
						break;
					case CollectionPlannedPluginEffectKind.AbsoluteOrderIndex:
						explanation = LanguageManager.Format("Collections.Review.PluginOrder", "{0} will set the reviewed load-order position for {1}.", memberName, plugins);
						break;
					default:
						explanation = LanguageManager.Format("Collections.Review.PluginRelativeOrder", "{0} will apply the reviewed relative load order for {1}.", memberName, plugins);
						break;
				}
				AddReviewItem(CollectionReviewSeverity.Info, CollectionReviewItemKind.PlannedEffect, L("Collections.Status.Supported", "Review"),
					"impact.plugin", plugins, explanation, impact.MemberKey,
					L("Collections.Messages.Next.ReviewPlannedChange", "Review this planned change before installing."),
					CombineTechnicalDetail("Member: " + impact.MemberKey, "Effect: " + impact.Effect.Kind, impact.Effect.Active.HasValue ? "Active: " + impact.Effect.Active.Value : null));
			}

			foreach (CollectionConfigurationImpact impact in impactPlan.ConfigurationImpacts)
			{
				string memberName = FormatMemberSubject(impact.MemberKey);
				string explanation = impact.Kind == CollectionConfigurationImpactKind.Ini
					? LanguageManager.Format("Collections.Review.ConfigurationIni", "{0} will apply the reviewed INI/configuration change.", memberName)
					: LanguageManager.Format("Collections.Review.ConfigurationGameValue", "{0} will apply the reviewed game-specific setting change.", memberName);
				AddReviewItem(CollectionReviewSeverity.Info, CollectionReviewItemKind.PlannedEffect, L("Collections.Status.Supported", "Review"),
					"impact.configuration", impact.SubjectKey, explanation, impact.MemberKey,
					L("Collections.Messages.Next.ReviewPlannedChange", "Review this planned change before installing."),
					CombineTechnicalDetail("Member: " + impact.MemberKey, "Configuration kind: " + impact.Kind, "Current owner: " + (impact.CurrentOwnerKey ?? "none")));
			}

			foreach (CollectionAssociationImpact impact in impactPlan.AssociationImpacts)
			{
				AddReviewItem(CollectionReviewSeverity.Info, CollectionReviewItemKind.PlannedEffect, L("Collections.Status.Supported", "Review"),
					"impact.association", impact.Association.Revision.ToString(),
					L("Collections.Review.AssociationImpact", "Another installed Collection shares managed state affected by these reviewed changes."), null,
					L("Collections.Messages.Next.ReviewPlannedChange", "Review this planned change before installing."), "Impact kind: " + impact.Kind);
			}
		}

		private void AppendRecoveryIssues()
		{
			foreach (CollectionAdditiveWorkflowRecoveryResult recovery in _recoveryResults)
			{
				Trace.TraceWarning("Collection recovery operation {0} ({1}): {2}",
					recovery.Operation.Identity, recovery.Status, recovery.Message);
				CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForRecovery(recovery.Status, recovery.Message);
				string subject = recovery.Operation.Revision == null ? GetCurrentCollectionSubject() : recovery.Operation.Revision.ToString();
				AddPresentedReviewItem(CollectionReviewPresentationClassifier.ForRecovery(recovery.Status),
					recovery.Status == CollectionAdditiveWorkflowRecoveryStatus.ReviewRequired || recovery.Status == CollectionAdditiveWorkflowRecoveryStatus.ReadyToResume
						? CollectionReviewItemKind.ManualAction : CollectionReviewItemKind.Diagnostic,
					FormatRecoveryStatus(recovery.Status), "recovery." + recovery.Status.ToString().ToLowerInvariant(), subject, userMessage, null,
					CombineTechnicalDetail("Recovery status: " + recovery.Status, "Operation: " + recovery.Operation.Identity));
			}
			foreach (CollectionLocalRestoreWorkflowResult recovery in _localRestoreRecoveryResults)
			{
				if (!recovery.IsSuccessful)
				{
					CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForLocalRestore(recovery.Status, recovery.Message);
					AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic, L("Collections.Status.ActionRequired", "Action required"),
						"workflow.local-restore-recovery", recovery.Operation.Revision == null ? L("Collections.Review.LocalCollectionRestore", "Local Collection restore") : recovery.Operation.Revision.ToString(),
						userMessage, null, CombineTechnicalDetail("Restore status: " + recovery.Status, "Operation: " + recovery.Operation.Identity));
				}
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
			BindIncomingDisplayOperation(_operationIdentity);
			_reviewedPlanIdentity = matching.Operation.PlanIdentity;
			_selectionDirty = false;
			SetWorkflowPresentation(CollectionUserMessagePresenter.ForRecovery(matching.Status, matching.Message));
			_appliedValue.Text = matching.Status == CollectionAdditiveWorkflowRecoveryStatus.ReadyToResume
				? L("Collections.Status.Applied.ResumeReady", "Incomplete apply reconciled - explicit resume available")
				: LanguageManager.Get("Collections.Status.Applied.RecoveredReviewReady", "Recovered review awaits explicit approval");
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

		private void UpdatePendingDownloadActionLabel(CollectionManualAcquisitionPendingAction pending)
		{
			if (pending == null)
			{
				_openPendingButton.Text = L("Collections.Actions.OpenDownloadPage", "Download selected missing mod");
				return;
			}

			string displayName = GetMemberDisplayName(pending.Request.MemberKey);
			_openPendingButton.Text = String.IsNullOrWhiteSpace(displayName)
				? L("Collections.Actions.OpenDownloadPage", "Download selected missing mod")
				: LanguageManager.Format("Collections.Actions.OpenDownloadPageNamed", "Download missing mod: {0}", displayName);
		}

		private string GetMemberDisplayName(CollectionMemberKey memberKey)
		{
			if (memberKey == null || _snapshot == null || _snapshot.BundleImport == null || _snapshot.BundleImport.Manifest == null)
				return null;

			NormalizedCollectionMember member = _snapshot.BundleImport.Manifest.Members.FirstOrDefault(x =>
				x != null && x.IdentityResolution != null && x.IdentityResolution.IsResolved && x.IdentityResolution.Key.Equals(memberKey));
			return member == null ? null : member.DisplayName;
		}

		private string FormatMemberSubject(CollectionMemberKey memberKey)
		{
			string displayName = GetMemberDisplayName(memberKey);
			return String.IsNullOrWhiteSpace(displayName)
				? L("Collections.Review.Member", "Collection member")
				: displayName;
		}

		private string GetCurrentCollectionSubject()
		{
			if (_snapshot != null)
			{
				if (_snapshot.Definition != null && !String.IsNullOrWhiteSpace(_snapshot.Definition.DisplayName))
					return _snapshot.Definition.DisplayName;
				if (_snapshot.Link != null && !String.IsNullOrWhiteSpace(_snapshot.Link.CollectionSlug))
					return _snapshot.Link.CollectionSlug;
			}
			if (_managedAssociationPresentation != null && _managedAssociationPresentation.Definition != null &&
				!String.IsNullOrWhiteSpace(_managedAssociationPresentation.Definition.DisplayName))
				return _managedAssociationPresentation.Definition.DisplayName;
			return L("Collections.Review.CurrentCollection", "Current Collection");
		}

		private string FormatUninstallImpactSubject(CollectionUninstallNativeImpact impact)
		{
			if (impact == null)
				return L("Collections.Review.ManagedMod", "Managed mod");

			string[] names = impact.MemberKeys.Select(FormatMemberSubject).Where(x => !String.IsNullOrWhiteSpace(x))
				.Distinct(StringComparer.CurrentCultureIgnoreCase).ToArray();
			return names.Length == 0 ? L("Collections.Review.ManagedMod", "Managed mod") : String.Join(", ", names);
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
					ClearIncomingDisplayOperation();
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
				RenderIssues(_snapshot, false, false);
			AddReviewItem(CollectionReviewSeverity.Info, CollectionReviewItemKind.Progress, L("Collections.Status.Supported", "Ready"),
				"workflow.exact-review", GetCurrentCollectionSubject(),
				L("Collections.Review.ExactReviewReady", "The reviewed installation changes are still valid for the current setup."), null,
				L("Collections.Messages.Next.ReviewAndInstall", "Review the planned changes, then choose Review and install..."),
				"Plan: " + review.Operation.PlanIdentity);
			AppendDependencyReview(review.Runtime.DependencyPlan);
			AppendImpactReview(review.Runtime.ImpactPlan);
			UpdateIssuesHeader();
		}

		private string BuildApprovalConfirmation(CollectionAdditiveWorkflowReview review)
		{
			var text = new StringBuilder();
			text.AppendLine(L("Collections.Review.ConfirmHeading", "Install these reviewed Collection changes into the current setup?"));
			text.AppendLine();
			text.AppendLine(LanguageManager.Format("Collections.Review.ConfirmCollection", "Collection: {0}", GetCurrentCollectionSubject()));
			if (_snapshot != null && _snapshot.Revision != null)
				text.AppendLine(LanguageManager.Format("Collections.Review.ConfirmRevision", "Revision: {0}", _revisionValue.Text));
			if (review.Runtime != null)
			{
				text.AppendLine(LanguageManager.Format("Collections.Review.ConfirmSelectedMembers", "Selected members: {0}", review.Runtime.Plan.SelectedMembers.Count));
				text.AppendLine(LanguageManager.Format("Collections.Review.ConfirmFileChanges", "File changes: {0}", review.Runtime.ImpactPlan.FileImpacts.Count));
				text.AppendLine(LanguageManager.Format("Collections.Review.ConfirmPluginChanges", "Plugin changes: {0}", review.Runtime.ImpactPlan.PluginImpacts.Count));
				text.AppendLine(LanguageManager.Format("Collections.Review.ConfirmConfigurationChanges", "Configuration changes: {0}", review.Runtime.ImpactPlan.ConfigurationImpacts.Count));
				text.AppendLine(LanguageManager.Format("Collections.Review.ConfirmAffectedCollections", "Other installed Collections affected: {0}", review.Runtime.ImpactPlan.AssociationImpacts.Count));
			}
			text.AppendLine();
			text.AppendLine(L("Collections.Review.ConfirmDetail", "The complete reviewed changes are listed in the Review / issues pane. NMM will install only the changes you reviewed; if the current setup changes, this review becomes invalid and must be rebuilt."));
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

		private void RefreshWorkflowActivity()
		{
			if (_workflowBusy && _workflowContext != null)
				return;

			if (_acquisitionBatch != null && _acquisitionBatch.IsAwaitingInput)
			{
				List<CollectionWorkflowProducerActivity> producers = GetQueuedAcquisitionStates(_acquisitionBatch)
					.Where(x => x.QueueCorrelation != null && x.QueueCorrelation.Task != null)
					.Select(x => CollectionWorkflowProducerActivity.FromTask(x.QueueCorrelation.QueueOperationId, x.QueueCorrelation.Task))
					.ToList();
				bool waitingForUser = HasManualAcquisitionAction(_acquisitionBatch);
				SetWorkflowActivity(CollectionWorkflowActivityBuilder.FromAcquisition(producers, waitingForUser, _workflowStatusLabel.Text));
				if (!waitingForUser)
					SetWorkflowEta(_workflowEtaEstimator.UpdateAcquisition(producers));
				return;
			}

			if (_operationSnapshot != null)
			{
				if (_operationSnapshot.ResultState == CollectionOperationResultState.RecoveryRequired ||
					_operationSnapshot.ResultState == CollectionOperationResultState.FailedBeforeApply ||
					_operationSnapshot.ResultState == CollectionOperationResultState.StoppedPartial)
				{
					SetWorkflowActivity(CollectionWorkflowActivityBuilder.Failed(ResolveOperationActivityPhase(_operationSnapshot.Phase), _workflowStatusLabel.Text));
					return;
				}
				if (_operationSnapshot.Phase == CollectionOperationPhase.AwaitingInput)
				{
					SetWorkflowActivity(CollectionWorkflowActivityBuilder.Waiting(CollectionWorkflowActivityPhase.Acquiring, _workflowStatusLabel.Text, false));
					return;
				}
				if (_operationSnapshot.Phase == CollectionOperationPhase.PausedAtSafeBoundary ||
					_operationSnapshot.Phase == CollectionOperationPhase.RecoveryRequired)
				{
					SetWorkflowActivity(CollectionWorkflowActivityBuilder.Paused(ResolveOperationActivityPhase(_operationSnapshot.Phase), _workflowStatusLabel.Text, false));
					return;
				}
				if (_operationSnapshot.ResultState == CollectionOperationResultState.Committed ||
					_operationSnapshot.ResultState == CollectionOperationResultState.CancelledBeforeApply ||
					_operationSnapshot.ResultState == CollectionOperationResultState.RolledBack ||
					_operationSnapshot.Phase == CollectionOperationPhase.Completed ||
					_operationSnapshot.Phase == CollectionOperationPhase.ReadyForReview || _operationSnapshot.Phase == CollectionOperationPhase.ReadyToApply)
				{
					SetWorkflowActivity(CollectionWorkflowActivityBuilder.Completed(ResolveOperationActivityPhase(_operationSnapshot.Phase), _workflowStatusLabel.Text));
					return;
				}
				if (_operationSnapshot.ResultState == CollectionOperationResultState.Pending)
				{
					SetWorkflowActivity(CollectionWorkflowActivityBuilder.Paused(ResolveOperationActivityPhase(_operationSnapshot.Phase), _workflowStatusLabel.Text, false));
					return;
				}
			}

			SetWorkflowActivity(CollectionWorkflowActivityBuilder.Idle(_workflowStatusLabel.Text));
		}

		private void SetWorkflowActivity(CollectionWorkflowActivitySnapshot snapshot)
		{
			CollectionWorkflowActivityPhase previousPhase = _workflowActivity == null ? CollectionWorkflowActivityPhase.None : _workflowActivity.Phase;
			_workflowActivity = snapshot ?? CollectionWorkflowActivityBuilder.Idle(_workflowStatusLabel == null ? String.Empty : _workflowStatusLabel.Text);
			if (previousPhase != _workflowActivity.Phase || _workflowActivity.Phase != CollectionWorkflowActivityPhase.Acquiring ||
				(_workflowActivity.State != CollectionWorkflowActivityState.Working && _workflowActivity.State != CollectionWorkflowActivityState.Queued))
			{
				_workflowEtaEstimator.Reset();
				_workflowEta = CollectionWorkflowEtaSnapshot.Unavailable("activity-not-estimating");
			}
			RenderWorkflowActivity();
			RenderWorkflowEta();
		}

		private void SetWorkflowEta(CollectionWorkflowEtaSnapshot snapshot)
		{
			_workflowEta = snapshot ?? CollectionWorkflowEtaSnapshot.Unavailable("eta-null");
			RenderWorkflowEta();
		}

		private void RenderWorkflowEta()
		{
			if (_workflowEtaLabel == null)
				return;

			bool acquisitionCanEstimate = _workflowActivity != null && _workflowActivity.Phase == CollectionWorkflowActivityPhase.Acquiring &&
				(_workflowActivity.State == CollectionWorkflowActivityState.Working || _workflowActivity.State == CollectionWorkflowActivityState.Queued);
			if (!acquisitionCanEstimate || _workflowEta == null)
			{
				_workflowEtaLabel.Text = String.Empty;
				_workflowEtaLabel.Visible = false;
				return;
			}

			if (_workflowEta.IsAvailable && _workflowEta.Remaining.HasValue)
			{
				_workflowEtaLabel.Text = LanguageManager.Format("Collections.Workflow.Eta.Downloads",
					"Downloads: about {0} remaining", FormatEtaDuration(_workflowEta.Remaining.Value));
				_workflowEtaLabel.Visible = true;
				return;
			}

			if (_workflowEta.IsEstimating)
			{
				_workflowEtaLabel.Text = L("Collections.Workflow.Eta.Estimating", "Estimating time remaining...");
				_workflowEtaLabel.Visible = true;
				return;
			}

			_workflowEtaLabel.Text = String.Empty;
			_workflowEtaLabel.Visible = false;
		}

		private string FormatEtaDuration(TimeSpan remaining)
		{
			if (remaining < TimeSpan.FromMinutes(1))
				return L("Collections.Workflow.Eta.LessThanMinute", "less than a minute");

			int totalMinutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
			if (totalMinutes < 90)
				return LanguageManager.Format("Collections.Workflow.Eta.Minutes", "{0} min", totalMinutes);

			int hours = totalMinutes / 60;
			int minutes = totalMinutes % 60;
			return minutes == 0
				? LanguageManager.Format("Collections.Workflow.Eta.Hours", "{0} hr", hours)
				: LanguageManager.Format("Collections.Workflow.Eta.HoursMinutes", "{0} hr {1} min", hours, minutes);
		}

		private void RenderWorkflowActivity()
		{
			if (_workflowActivityIconLabel == null || _membersLoadingOverlay == null || _membersLoadingLabel == null ||
				_membersLoadingProgress == null || _workflowActivityAnimationTimer == null)
				return;

			CollectionWorkflowActivityPresentation presentation = CollectionWorkflowActivityPresentationBuilder.Build(_workflowActivity);
			_workflowActivityIconLabel.AccessibleDescription = FormatWorkflowActivityText(_workflowActivity);
			_membersLoadingOverlay.BackColor = _membersView.BackColor;
			_membersLoadingLabel.BackColor = _membersView.BackColor;
			_membersLoadingLabel.ForeColor = _membersView.ForeColor;
			if (presentation.AnimateIcon)
			{
				if (!_workflowActivityAnimationTimer.Enabled)
				{
					_workflowActivityAnimationFrame = 0;
					_workflowActivityIconLabel.Text = WorkflowActivityAnimationFrames[0];
					_workflowActivityAnimationTimer.Start();
				}
			}
			else
			{
				_workflowActivityAnimationTimer.Stop();
				_workflowActivityIconLabel.Text = presentation.IconText;
			}

			_membersLoadingOverlay.Visible = presentation.ShowMemberLoadingOverlay;
			if (presentation.ShowMemberLoadingOverlay)
			{
				_membersLoadingLabel.Text = FormatWorkflowActivityText(_workflowActivity);
				ConfigureMemberLoadingProgress(_workflowActivity);
				_membersLoadingProgress.Visible = true;
				_membersLoadingOverlay.BringToFront();
			}
			else
			{
				_membersLoadingProgress.Visible = false;
			}
		}

		private void ConfigureMemberLoadingProgress(CollectionWorkflowActivitySnapshot snapshot)
		{
			if (snapshot != null && !snapshot.IsIndeterminate && snapshot.Current.HasValue && snapshot.Total.HasValue &&
				snapshot.Total.Value > 0 && snapshot.Current.Value >= 0)
			{
				_membersLoadingProgress.Style = ProgressBarStyle.Continuous;
				_membersLoadingProgress.Minimum = 0;
				_membersLoadingProgress.Maximum = 1000;
				double fraction = Math.Min(1D, (double)snapshot.Current.Value / snapshot.Total.Value);
				_membersLoadingProgress.Value = Math.Max(0, Math.Min(_membersLoadingProgress.Maximum, (int)Math.Round(fraction * _membersLoadingProgress.Maximum)));
				return;
			}

			_membersLoadingProgress.Style = ProgressBarStyle.Marquee;
			_membersLoadingProgress.MarqueeAnimationSpeed = 30;
		}

		private void WorkflowActivityAnimationTimer_Tick(object sender, EventArgs e)
		{
			if (_workflowActivity == null || !_workflowActivity.IsWorkActive || _workflowActivity.State != CollectionWorkflowActivityState.Working)
			{
				_workflowActivityAnimationTimer.Stop();
				RenderWorkflowActivity();
				return;
			}

			_workflowActivityAnimationFrame = (_workflowActivityAnimationFrame + 1) % WorkflowActivityAnimationFrames.Length;
			_workflowActivityIconLabel.Text = WorkflowActivityAnimationFrames[_workflowActivityAnimationFrame];
		}

		private static string FormatWorkflowActivityText(CollectionWorkflowActivitySnapshot snapshot)
		{
			if (snapshot == null)
				return String.Empty;
			if (snapshot.Current.HasValue && snapshot.Total.HasValue && snapshot.Total.Value > 0)
				return String.Format(CultureInfo.CurrentCulture, "{0} ({1}/{2})", snapshot.StatusText, snapshot.Current.Value, snapshot.Total.Value);
			return snapshot.StatusText;
		}

		private static CollectionWorkflowActivityPhase ResolveOperationActivityPhase(CollectionOperationPhase phase)
		{
			switch (phase)
			{
				case CollectionOperationPhase.Resolving:
				case CollectionOperationPhase.Preparing:
				case CollectionOperationPhase.Revalidating:
					return CollectionWorkflowActivityPhase.Preparing;
				case CollectionOperationPhase.AwaitingInput:
					return CollectionWorkflowActivityPhase.Acquiring;
				case CollectionOperationPhase.ReadyForReview:
				case CollectionOperationPhase.ReadyToApply:
					return CollectionWorkflowActivityPhase.Reviewing;
				case CollectionOperationPhase.ApplyingNativeChildren:
				case CollectionOperationPhase.PausedAtSafeBoundary:
					return CollectionWorkflowActivityPhase.Applying;
				case CollectionOperationPhase.Verifying:
					return CollectionWorkflowActivityPhase.Verifying;
				case CollectionOperationPhase.Recovering:
				case CollectionOperationPhase.RecoveryRequired:
					return CollectionWorkflowActivityPhase.Recovering;
				default:
					return CollectionWorkflowActivityPhase.None;
			}
		}

		private CancellationToken BeginWorkflowWork(CollectionUiContext context, CollectionWorkflowActivityPhase phase, string status)
		{
			if (context == null)
				throw new ArgumentNullException(nameof(context));

			CancelWorkflowWork();
			_workflowCancellation = new CancellationTokenSource();
			_workflowContext = context;
			_workflowBusy = true;
			_workflowStatusLabel.Text = status;
			SetWorkflowActivity(CollectionWorkflowActivityBuilder.Foreground(phase, status));
			UpdateActionButtons();
			return _workflowCancellation.Token;
		}

		private void EndWorkflowWork(CollectionUiContext context)
		{
			if (!ReferenceEquals(_workflowContext, context))
				return;

			_workflowContext = null;
			_workflowBusy = false;
			RefreshWorkflowActivity();
			UpdateActionButtons();

			// An NXM request may have arrived while apply/restore/capture owned the UI. Do not discard or activate it
			// mid-mutation; once the foreground operation is finished, process the newest retained request normally.
			if (_dispatcher != null && IsHandleCreated && !IsDisposed && !Disposing)
			{
				try
				{
					BeginInvoke((Action)DrainIncomingQueue);
				}
				catch (InvalidOperationException)
				{
					// Handle teardown can race the final workflow completion. The dispatcher queue remains bounded.
				}
			}
		}

		private bool IsWorkflowOwnerCurrent(CollectionUiContext context)
		{
			return context != null && !IsDisposed && !Disposing &&
				ReferenceEquals(_workflowContext, context) && context.IsCurrentGeneration(_previewGeneration);
		}

		private bool IsWorkflowContextCurrent(CollectionUiContext context, CancellationToken token)
		{
			return !token.IsCancellationRequested && IsWorkflowOwnerCurrent(context);
		}

		private bool IsActionContextCurrent(CollectionUiContext context)
		{
			return context != null && !IsDisposed && !Disposing && context.IsCurrentGeneration(_previewGeneration);
		}

		private void BindIncomingDisplayOperation(CollectionOperationIdentity operation)
		{
			if (_displayContext == null || _displayContext.Kind != CollectionUiContextKind.IncomingCollection ||
				!_displayContext.IsCurrentGeneration(_previewGeneration))
				return;

			_displayContext = _displayContext.WithOperation(operation);
		}

		private void ClearIncomingDisplayOperation()
		{
			BindIncomingDisplayOperation(null);
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
			_workflowContext = null;
			_workflowBusy = false;
			RefreshWorkflowActivity();
		}

		private void ResetWorkflowViewState()
		{
			_acquisitionRefreshTimer.Stop();
			_autoResumedQueueOperations.Clear();
			_preparation = null;
			_acquisitionBatch = null;
			_operationIdentity = null;
			_operationSnapshot = null;
			ClearIncomingDisplayOperation();
			_reviewedPlanIdentity = null;
			_selectionDirty = false;
			_selectionCapabilityBlocked = false;
			_workflowStatusLabel.Text = _workflow == null
				? L("Collections.Workflow.PreviewOnly", "Workflow: preview only - Collection installation is unavailable in this session.")
				: L("Collections.Workflow.Idle", "Workflow: idle");
			SetWorkflowActivity(CollectionWorkflowActivityBuilder.Idle(_workflowStatusLabel.Text));
		}

		private void UpdateActionButtons()
		{
			_currentSetupActionContext = CollectionUiContext.CurrentSetup(_previewGeneration);
			_saveCurrentSetupButton.Enabled = !_workflowBusy && _captureWorkflow != null;
			CollectionManagementLocalCapture selectedCapture = _localCaptureCombo.SelectedItem as CollectionManagementLocalCapture;
			bool hasLocalCapture = _managementWorkflow != null && selectedCapture != null;
			_localCaptureActionContext = hasLocalCapture
				? CollectionUiContext.SavedLocal(_previewGeneration, selectedCapture.Capture.Revision, selectedCapture.CaptureIdentity)
				: null;
			_localCaptureCombo.Enabled = !_workflowBusy && _managementWorkflow != null && _localCaptureCombo.Items.Count > 0;
			_restoreLocalCaptureButton.Enabled = !_workflowBusy && hasLocalCapture &&
				selectedCapture.Capability == LocalCaptureCapability.LocallyRestorableWithinScope;

			CollectionManagementAssociation selectedAssociation = _managedAssociationCombo.SelectedItem as CollectionManagementAssociation;
			bool hasManagedAssociation = _managementWorkflow != null && selectedAssociation != null;
			_managedAssociationActionContext = hasManagedAssociation
				? CollectionUiContext.Installed(_previewGeneration, selectedAssociation.Association.Revision, selectedAssociation.AssociationId)
				: null;
			_managedAssociationCombo.Enabled = !_workflowBusy && _managementWorkflow != null && _managedAssociationCombo.Items.Count > 0;
			_detachAssociationButton.Enabled = !_workflowBusy && hasManagedAssociation;
			_removeAssociationEffectsButton.Enabled = !_workflowBusy && hasManagedAssociation;

			bool hasConcreteRevision = _snapshot != null && _snapshot.HasConcreteRevision;
			_incomingActionContext = _displayContext != null && _displayContext.Kind == CollectionUiContextKind.IncomingCollection &&
				_displayContext.IsCurrentGeneration(_previewGeneration) ? _displayContext : null;
			if (_incomingActionContext != null && _operationIdentity != null)
				_incomingActionContext = _incomingActionContext.WithOperation(_operationIdentity);
			CollectionManagementAssociation matchingAssociation = FindMatchingManagedAssociation();
			bool sameRevisionAlreadyApplied = matchingAssociation != null && matchingAssociation.State == CollectionAssociationState.Applied;
			bool installedAssociationView = _managedAssociationPresentation != null;
			_importButton.Enabled = !_workflowBusy && hasConcreteRevision && !installedAssociationView && !sameRevisionAlreadyApplied;
			_downloadPrepareButton.Enabled = !_workflowBusy && _workflow != null && hasConcreteRevision && !installedAssociationView && !sameRevisionAlreadyApplied && !_selectionCapabilityBlocked &&
				(_acquisitionBatch == null || _selectionDirty || (_preparation != null &&
				 (_preparation.Status == CollectionAdditiveWorkflowPreparationStatus.PreparationRequired ||
				  _preparation.Status == CollectionAdditiveWorkflowPreparationStatus.ActionRequired ||
				  _preparation.Status == CollectionAdditiveWorkflowPreparationStatus.Blocked)));
			_autoOverwriteArchivesCheckBox.Enabled = _downloadPrepareButton.Enabled;
			_resumeButton.Visible = _acquisitionBatch != null && !_acquisitionBatch.IsReady;
			_resumeButton.Enabled = !_workflowBusy && _resumeButton.Visible;
			CollectionManualAcquisitionPendingAction pendingDownload = GetSelectedOrFirstPendingAction();
			_openPendingButton.Visible = pendingDownload != null && pendingDownload.BrowserUri != null;
			_openPendingButton.Enabled = !_workflowBusy && _openPendingButton.Visible;
			UpdatePendingDownloadActionLabel(pendingDownload);
			bool exactReview = _workflow != null && _operationIdentity != null && _reviewedPlanIdentity != null && !_selectionDirty;
			_installButton.Enabled = !_workflowBusy && exactReview && !sameRevisionAlreadyApplied;
			if (sameRevisionAlreadyApplied)
				_installButton.Text = L("Collections.Actions.AlreadyApplied", "Already applied");
			else if (exactReview && _preparation == null)
				_installButton.Text = L("Collections.Actions.ResumeApply", "Review and continue...");
			else
				_installButton.Text = L("Collections.Actions.InstallCurrent", "Review and install...");
			_clearButton.Enabled = !_workflowBusy && !installedAssociationView && (_snapshot != null || _operationIdentity != null || _preparation != null || _acquisitionBatch != null);
			_exportTechnicalReportButton.Enabled = HasTechnicalReportContent();
			_membersView.Enabled = !_workflowBusy && (_operationSnapshot == null || (!_operationSnapshot.HasCrossedNativeBoundary && !_operationSnapshot.IsSuccessful));
		}

		private CollectionManagementAssociation FindMatchingManagedAssociation()
		{
			if (_managedAssociationPresentation != null)
				return _managedAssociationPresentation.Association;
			if (_snapshot == null || _snapshot.Revision == null)
				return null;
			foreach (object item in _managedAssociationCombo.Items)
			{
				CollectionManagementAssociation association = item as CollectionManagementAssociation;
				if (association != null && association.Association.Revision.Equals(_snapshot.Revision.Identity))
					return association;
			}
			return null;
		}

		private void ApplyManagedAssociationPresentation()
		{
			CollectionManagementAssociation association = FindMatchingManagedAssociation();
			if (association == null)
				return;

			switch (association.State)
			{
				case CollectionAssociationState.Applied:
					_appliedValue.Text = L("Collections.Status.Applied.Applied", "Applied and verified");
					_contentValue.Text = L("Collections.Status.Content.Applied", "Prepared content applied and verified");
					_compatibilityValue.Text = L("Collections.Status.Compatibility.AppliedVerified", "Supported - applied revision verified");
					break;
				case CollectionAssociationState.Modified:
					_appliedValue.Text = L("Collections.Status.Applied.Modified", "Applied - modified from reviewed revision");
					break;
				case CollectionAssociationState.Incomplete:
					_appliedValue.Text = L("Collections.Status.Applied.Incomplete", "Incomplete - review/resume required");
					break;
				case CollectionAssociationState.Recovering:
					_appliedValue.Text = L("Collections.Status.Applied.RecoveryRequired", "Recovery required before further mutation");
					break;
			}
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
			SetDefaultInstruction();
			_collectionValue.Text = link?.CollectionSlug ?? L("Collections.Value.Unknown", "Unknown");
			_curatorValue.Text = L("Collections.Value.Loading", "Loading...");
			_locatorValue.Text = link == null ? L("Collections.Value.Unknown", "Unknown") : link.GameDomain + " / " + link.CollectionSlug;
			_revisionValue.Text = link == null ? L("Collections.Value.Unknown", "Unknown") : link.RevisionRequest.IsLatest
				? L("Collections.Preview.ResolvingLatest", "Resolving latest...")
				: "#" + link.RevisionRequest.RevisionNumber.Value.ToString(CultureInfo.InvariantCulture);
			_compatibilityValue.Text = L("Collections.Value.Loading", "Loading...");
			_contentValue.Text = L("Collections.Status.Content.WaitingRevision", "Waiting for concrete revision");
			_appliedValue.Text = L("Collections.Status.Applied.NotApplied", "Not applied");
			_summaryBox.Text = L("Collections.Preview.Loading", "Resolving Collection metadata. No installed mod state is being changed.");
			_membersView.Items.Clear();
			ClearReviewItems();
			_membersHeader.Text = L("Collections.Preview.Members", "Members");
			_issuesHeader.Text = L("Collections.Preview.Issues", "Review / issues");
		}

		private void RenderUnexpectedFailure(NexusCollectionNxmLink link, Exception exception)
		{
			CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForFailure("preview.unexpected-failure", exception == null ? String.Empty : exception.Message);
			_collectionValue.Text = link?.CollectionSlug ?? L("Collections.Value.Unknown", "Unknown");
			_curatorValue.Text = L("Collections.Value.Unknown", "Unknown");
			_locatorValue.Text = link == null ? L("Collections.Value.Unknown", "Unknown") : link.GameDomain + " / " + link.CollectionSlug;
			_revisionValue.Text = L("Collections.Value.Unresolved", "Unresolved");
			_compatibilityValue.Text = L("Collections.Status.ActionRequired", "Action required");
			_contentValue.Text = L("Collections.Status.Content.WaitingRevision", "Waiting for concrete revision");
			_appliedValue.Text = L("Collections.Status.Applied.NotApplied", "Not applied");
			_summaryBox.Text = BuildUserDialogMessage(userMessage);
			_membersView.Items.Clear();
			ClearReviewItems();
			AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic, L("Collections.Status.ActionRequired", "Action required"),
				"preview.unexpected-failure", link == null ? String.Empty : link.CollectionSlug, userMessage);
			UpdateIssuesHeader();
		}

		private void RenderEmptyState()
		{
			SetDefaultInstruction();
			_collectionValue.Text = "-";
			_curatorValue.Text = "-";
			_locatorValue.Text = "-";
			_revisionValue.Text = "-";
			_compatibilityValue.Text = L("Collections.Status.Compatibility.NotEvaluated", "Not evaluated - manifest not imported");
			_contentValue.Text = L("Collections.Status.Content.NoSelection", "No revision selected");
			_appliedValue.Text = L("Collections.Status.Applied.NotApplied", "Not applied");
			_summaryBox.Text = L("Collections.Preview.EmptySummary", "No Collection preview is loaded.");
			_membersView.Items.Clear();
			ClearReviewItems();
			_membersHeader.Text = L("Collections.Preview.Members", "Members");
			_issuesHeader.Text = L("Collections.Preview.Issues", "Review / issues");
		}

		private void SetDefaultInstruction()
		{
			_instructionLabel.Text = L("Collections.Preview.Instructions",
				"Open a Nexus Collection NXM link to load it, choose optional mods, download and prepare, review the changes, then install. Select a saved Local Collection above to review and restore it.");
		}

		private void AddGraphQlErrors(string codePrefix, IReadOnlyList<NexusGraphQlError> errors, CollectionReviewSeverity severity)
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
				string technicalMessage = error.Message ?? L("Collections.Preview.ProviderError", "Nexus returned a GraphQL field error.");
				AddPresentedReviewItem(severity, CollectionReviewItemKind.Diagnostic, L("Collections.Status.ActionRequired", "Action required"),
					code, GetCurrentCollectionSubject(), CollectionUserMessagePresenter.ForProviderMessage(technicalMessage, severity == CollectionReviewSeverity.Error),
					null, String.IsNullOrWhiteSpace(field) ? null : "Provider path: " + field);
			}
		}

		private static string ResolveUserMessage(CollectionUserMessagePresentation presentation)
		{
			if (presentation == null)
				return String.Empty;
			return String.IsNullOrWhiteSpace(presentation.MessageKey)
				? presentation.MessageFallback
				: L(presentation.MessageKey, presentation.MessageFallback);
		}

		private static string ResolveNextAction(CollectionUserMessagePresentation presentation)
		{
			if (presentation == null)
				return String.Empty;
			return String.IsNullOrWhiteSpace(presentation.NextActionKey)
				? presentation.NextActionFallback
				: L(presentation.NextActionKey, presentation.NextActionFallback);
		}

		private static string BuildUserDialogMessage(CollectionUserMessagePresentation presentation)
		{
			string message = ResolveUserMessage(presentation);
			string nextAction = ResolveNextAction(presentation);
			return String.IsNullOrWhiteSpace(nextAction) ? message : message + Environment.NewLine + Environment.NewLine + nextAction;
		}

		private static string CombineTechnicalDetail(params string[] details)
		{
			return String.Join(Environment.NewLine, (details ?? new string[0]).Where(x => !String.IsNullOrWhiteSpace(x)));
		}

		private void AddPresentedReviewItem(CollectionReviewSeverity severity, CollectionReviewItemKind kind, string status,
			string code, string subject, CollectionUserMessagePresentation presentation, CollectionMemberKey memberKey = null,
			string additionalTechnicalDetail = null)
		{
			if (presentation == null)
				throw new ArgumentNullException(nameof(presentation));
			AddReviewItem(severity, kind, status, code, subject, ResolveUserMessage(presentation), memberKey,
				ResolveNextAction(presentation), CombineTechnicalDetail(presentation.TechnicalDetail, additionalTechnicalDetail));
		}

		private void SetWorkflowPresentation(CollectionUserMessagePresentation presentation)
		{
			_workflowStatusLabel.Text = LanguageManager.Format("Collections.Workflow.UserStatus", "Workflow: {0}", ResolveUserMessage(presentation));
		}

		private void AddReviewItem(CollectionReviewSeverity severity, CollectionReviewItemKind kind, string status,
			string code, string subject, string explanation, CollectionMemberKey memberKey = null,
			string nextAction = null, string technicalDetail = null)
		{
			AddReviewItem(new CollectionReviewItem(severity, kind, status, code, subject, explanation, memberKey, nextAction, technicalDetail));
		}

		private void AddReviewItem(CollectionReviewItem reviewItem)
		{
			if (reviewItem == null)
				throw new ArgumentNullException(nameof(reviewItem));

			_reviewItems.Add(reviewItem);
			TrackReviewItemCount(reviewItem);
			if (CollectionReviewPresentationFilter.IsPersistentReviewAction(reviewItem))
			{
				_reviewActionsView.Items.Add(CreateReviewListViewItem(reviewItem));
			}
			else if (CollectionReviewPresentationFilter.IsSeverityFiltered(reviewItem) && ReviewSeverityIsVisible(reviewItem))
			{
				RemoveFilteredEmptyMessage();
				_issuesView.Items.Add(CreateReviewListViewItem(reviewItem));
			}
			UpdateIssuesHeader();
		}

		private void ClearReviewItems()
		{
			_reviewItems.Clear();
			_reviewErrorCount = 0;
			_reviewWarningCount = 0;
			_reviewInfoCount = 0;
			_reviewActionCount = 0;
			_issuesView.Items.Clear();
			_reviewActionsView.Items.Clear();
			UpdateReviewFilterLabels();
			UpdateReviewActionsVisibility();
		}

		private void ReviewSeverityFilter_CheckedChanged(object sender, EventArgs e)
		{
			RefreshFilteredReviewIssues();
		}

		private void RefreshFilteredReviewIssues()
		{
			_issuesView.BeginUpdate();
			try
			{
				_issuesView.Items.Clear();
				foreach (CollectionReviewItem reviewItem in _reviewItems)
				{
					if (CollectionReviewPresentationFilter.IsSeverityFiltered(reviewItem) && ReviewSeverityIsVisible(reviewItem))
						_issuesView.Items.Add(CreateReviewListViewItem(reviewItem));
				}
			}
			finally
			{
				_issuesView.EndUpdate();
			}

			EnsureFilteredEmptyMessage();
			UpdateReviewFilterLabels();
		}

		private bool ReviewSeverityIsVisible(CollectionReviewItem reviewItem)
		{
			return CollectionReviewPresentationFilter.MatchesSeverity(reviewItem, _showErrorIssuesCheckBox.Checked,
				_showWarningIssuesCheckBox.Checked, _showInfoIssuesCheckBox.Checked);
		}

		private void TrackReviewItemCount(CollectionReviewItem reviewItem)
		{
			if (CollectionReviewPresentationFilter.IsPersistentReviewAction(reviewItem))
			{
				_reviewActionCount++;
				return;
			}
			if (!CollectionReviewPresentationFilter.IsSeverityFiltered(reviewItem))
				return;

			switch (reviewItem.Severity)
			{
				case CollectionReviewSeverity.Error:
					_reviewErrorCount++;
					break;
				case CollectionReviewSeverity.Warning:
					_reviewWarningCount++;
					break;
				case CollectionReviewSeverity.Info:
					_reviewInfoCount++;
					break;
			}
		}

		private void EnsureFilteredEmptyMessage()
		{
			if (_issuesView.Items.Count > 0)
				return;
			if (_reviewItems.Count == 0 && _snapshot == null && _managedAssociationPresentation == null)
				return;

			string message;
			if (!_showErrorIssuesCheckBox.Checked && !_showWarningIssuesCheckBox.Checked && !_showInfoIssuesCheckBox.Checked)
				message = L("Collections.Review.Filters.NoneSelected", "No severity filters selected.");
			else if (_showErrorIssuesCheckBox.Checked && !_showWarningIssuesCheckBox.Checked && !_showInfoIssuesCheckBox.Checked)
				message = L("Collections.Review.Filters.NoErrors", "No errors.");
			else if (!_showErrorIssuesCheckBox.Checked && _showWarningIssuesCheckBox.Checked && !_showInfoIssuesCheckBox.Checked)
				message = L("Collections.Review.Filters.NoWarnings", "No warnings.");
			else if (!_showErrorIssuesCheckBox.Checked && !_showWarningIssuesCheckBox.Checked && _showInfoIssuesCheckBox.Checked)
				message = L("Collections.Review.Filters.NoInfo", "No information messages.");
			else
				message = L("Collections.Review.Filters.NoMatches", "No matching review items.");

			_issuesView.Items.Add(new ListViewItem(message) { ForeColor = SystemColors.GrayText });
		}

		private void RemoveFilteredEmptyMessage()
		{
			if (_issuesView.Items.Count == 1 && !(_issuesView.Items[0].Tag is CollectionReviewItem))
				_issuesView.Items.Clear();
		}

		private void UpdateIssuesHeader()
		{
			int issueCount = _reviewErrorCount + _reviewWarningCount + _reviewInfoCount;
			_issuesHeader.Text = LanguageManager.Format("Collections.Preview.IssueCount", "Review / issues ({0})", issueCount);
			EnsureFilteredEmptyMessage();
			UpdateReviewFilterLabels();
			UpdateReviewActionsVisibility();
			_exportTechnicalReportButton.Enabled = HasTechnicalReportContent();
		}

		private void UpdateReviewFilterLabels()
		{
			_showErrorIssuesCheckBox.Text = LanguageManager.Format("Collections.Review.Filters.Errors", "Errors ({0})", _reviewErrorCount);
			_showWarningIssuesCheckBox.Text = LanguageManager.Format("Collections.Review.Filters.Warnings", "Warnings ({0})", _reviewWarningCount);
			_showInfoIssuesCheckBox.Text = LanguageManager.Format("Collections.Review.Filters.Info", "Info ({0})", _reviewInfoCount);
		}

		private void UpdateReviewActionsVisibility()
		{
			bool visible = _reviewActionCount > 0;
			_reviewActionsHeader.Text = LanguageManager.Format("Collections.Review.ActionsCount", "Required actions / planned changes ({0})", _reviewActionCount);
			_reviewActionsHeader.Visible = visible;
			_reviewActionsView.Visible = visible;

			_reviewPanel.RowStyles[0].SizeType = SizeType.Percent;
			_reviewPanel.RowStyles[0].Height = visible ? 60F : 100F;
			_reviewPanel.RowStyles[1].SizeType = visible ? SizeType.AutoSize : SizeType.Absolute;
			_reviewPanel.RowStyles[1].Height = 0F;
			_reviewPanel.RowStyles[2].SizeType = visible ? SizeType.Percent : SizeType.Absolute;
			_reviewPanel.RowStyles[2].Height = visible ? 40F : 0F;
		}

		private static ListViewItem CreateReviewListViewItem(CollectionReviewItem reviewItem)
		{
			var item = new ListViewItem(reviewItem.StatusText) { Tag = reviewItem };
			item.SubItems.Add(reviewItem.Subject);
			item.SubItems.Add(reviewItem.Explanation);
			item.SubItems.Add(reviewItem.NextAction);
			var tooltip = new StringBuilder();
			tooltip.AppendLine(reviewItem.StatusText);
			if (!String.IsNullOrWhiteSpace(reviewItem.Subject)) tooltip.AppendLine(reviewItem.Subject);
			if (!String.IsNullOrWhiteSpace(reviewItem.Explanation)) tooltip.AppendLine(reviewItem.Explanation);
			if (!String.IsNullOrWhiteSpace(reviewItem.NextAction)) tooltip.Append(reviewItem.NextAction);
			item.ToolTipText = tooltip.ToString().TrimEnd();
			return item;
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

		private static Control CreateActionGroup(string caption, params Control[] actions)
		{
			var group = new TableLayoutPanel
			{
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				ColumnCount = 1,
				RowCount = 2,
				Margin = new Padding(0, 0, 12, 4),
				Padding = Padding.Empty
			};
			group.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			group.RowStyles.Add(new RowStyle(SizeType.AutoSize));

			var heading = new Label
			{
				AutoSize = true,
				Text = caption ?? String.Empty,
				Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
				Margin = new Padding(3, 0, 3, 2)
			};
			var actionPanel = new FlowLayoutPanel
			{
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				WrapContents = true,
				FlowDirection = FlowDirection.LeftToRight,
				Margin = Padding.Empty,
				Padding = Padding.Empty,
				MaximumSize = new Size(760, 0)
			};
			foreach (Control action in actions ?? new Control[0])
			{
				if (action != null)
					actionPanel.Controls.Add(action);
			}

			group.Controls.Add(heading, 0, 0);
			group.Controls.Add(actionPanel, 0, 1);
			return group;
		}

		private static ListView CreateReviewListView()
		{
			ListView view = CreateListView();
			view.Columns.Add(L("Collections.Columns.Status", "Status"), 90);
			view.Columns.Add(L("Collections.Columns.Field", "Subject"), 170);
			view.Columns.Add(L("Collections.Columns.Reason", "What happened / planned change"), 315);
			view.Columns.Add(L("Collections.Columns.NextAction", "What you can do"), 285);
			return view;
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
				ShowItemToolTips = true,
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

		private static string FormatPreparationStatus(CollectionAdditiveWorkflowPreparationResult result)
		{
			if (result == null)
				return L("Collections.Status.ActionRequired", "Action required");

			switch (result.Status)
			{
				case CollectionAdditiveWorkflowPreparationStatus.ReadyForReview:
					return L("Collections.Status.Supported", "Ready");
				case CollectionAdditiveWorkflowPreparationStatus.Blocked:
					return L("Collections.Status.Unsupported", "Blocked");
				case CollectionAdditiveWorkflowPreparationStatus.AwaitingInput:
					if (result.AcquisitionBatch != null &&
						result.AcquisitionBatch.Members.Any(x => x.Disposition == CollectionMemberAcquisitionDisposition.Blocked))
						return L("Collections.Status.Unsupported", "Blocked");
					return HasManualAcquisitionAction(result.AcquisitionBatch)
						? L("Collections.Status.ActionRequired", "Action required")
						: L("Collections.Status.Pending", "Pending");
				default:
					return L("Collections.Status.ActionRequired", "Action required");
			}
		}

		private static bool HasManualAcquisitionAction(CollectionMemberAcquisitionBatch batch)
		{
			return batch != null && batch.Members.Any(x =>
				x.Disposition == CollectionMemberAcquisitionDisposition.ManualInputRequired ||
				x.Disposition == CollectionMemberAcquisitionDisposition.RestartActionRequired);
		}

		private static string FormatAcquisitionStatus(CollectionMemberAcquisitionDisposition disposition)
		{
			switch (disposition)
			{
				case CollectionMemberAcquisitionDisposition.ReadyInstalled:
				case CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive:
					return L("Collections.Status.Supported", "Ready");
				case CollectionMemberAcquisitionDisposition.PremiumQueued:
				case CollectionMemberAcquisitionDisposition.BundledQueued:
					return L("Collections.Status.Pending", "Pending");
				case CollectionMemberAcquisitionDisposition.Blocked:
					return L("Collections.Status.Unsupported", "Blocked");
				default:
					return L("Collections.Status.ActionRequired", "Action required");
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
