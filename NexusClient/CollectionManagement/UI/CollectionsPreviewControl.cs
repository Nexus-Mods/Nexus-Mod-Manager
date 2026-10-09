using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.Utils;
using DevExpress.Utils.Layout;
using DevExpress.XtraTab;
using DevExpress.XtraBars;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Columns;
using DevExpress.XtraGrid.Views.Base;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraEditors.Repository;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModAuthoring;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.Mods;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using Nexus.Client.OnlineServices.NexusMods.GraphQl;
using Nexus.Client.UI;
using Nexus.Client.Util.Localization;
using Nexus.UI.Controls;

namespace Nexus.Client.CollectionManagement.UI
{
	/// <summary>Requests navigation from an installed Collection member to its exact live NMM mod.</summary>
	public sealed class CollectionManagedModRequestEventArgs : EventArgs
	{
		public CollectionManagedModRequestEventArgs(IMod mod)
		{
			Mod = mod ?? throw new ArgumentNullException(nameof(mod));
		}

		public IMod Mod { get; }
	}

	/// <summary>
	/// Collections surface for provider preview, additive/replacement apply, Local capture/restore and installed Collection management.
	/// </summary>
	/// <remarks>
	/// The control consumes application-level workflow services and never constructs or invokes native C4/C5/C6/C8
	/// persistence or mutation coordinators directly. Replacement is exposed as a separate explicitly reviewed product command.
	/// </remarks>
	public sealed class CollectionsPreviewControl : ManagedFontDockContent
	{
		private readonly SimpleButton _saveCurrentSetupButton;
		private readonly ComboBoxEdit _localCaptureCombo;
		private readonly SimpleButton _restoreLocalCaptureButton;
		private readonly SimpleButton _deleteLocalCaptureButton;
		private readonly ComboBoxEdit _localWorkingCopyCombo;
		private readonly SimpleButton _editLocalWorkingCopyButton;
		private readonly SimpleButton _saveLocalWorkingCopyRevisionButton;
		private readonly ComboBoxEdit _managedAssociationCombo;
		private readonly SimpleButton _manageAssociationRemovalButton;
		private readonly SimpleButton _compareUpdateButton;
		private readonly SimpleButton _verifyRepairButton;
		private readonly SimpleButton _cloneManagedAssociationButton;
		private readonly SimpleButton _showManagedMemberButton;
		private readonly SimpleButton _showManagedMemberImpactButton;
		private readonly SimpleButton _addManagedOptionalMemberButton;
		private readonly SimpleButton _removeManagedOptionalMemberButton;
		private readonly SimpleButton _ignoreMemberDifferenceButton;
		private readonly SimpleButton _stopIgnoringMemberDifferenceButton;
		private readonly SimpleButton _acceptMemberDriftButton;
		private readonly SimpleButton _clearMemberOverrideButton;
		private readonly SimpleButton _importButton;
		private readonly SimpleButton _downloadPrepareButton;
		private readonly SimpleButton _resolveFileConflictsButton;
		private readonly CheckEdit _autoOverwriteArchivesCheckBox;
		private readonly ToolTipController _toolTip;
		private readonly SimpleButton _resumeButton;
		private readonly SimpleButton _openPendingButton;
		private readonly SimpleButton _installButton;
		private readonly SimpleButton _primaryIncomingActionButton;
		private SimpleButton _primaryIncomingActionSource;
		private bool _primaryIncomingReviewsBlockedIssues;
		private bool _primaryIncomingReviewsRevisionOwnership;
		private CollectionRevisionUpdatePlan _blockedRevisionUpdatePlan;
		private readonly LabelControl _previewContextLabel;
		private readonly CollectionSectionGroup _previewSection;
		private readonly DevExpress.XtraEditors.GroupControl _detailsSection;
		private readonly CollectionActionPanel _incomingActionsPanel;
		private readonly Control _advancedIncomingGroup;
		private readonly Control _currentSetupGroup;
		private readonly Control _savedLocalGroup;
		private readonly Control _localWorkingCopyGroup;
		private readonly Control _installedGroup;
		private readonly CheckEdit _replacementBackupCheckBox;
		private readonly SimpleButton _replaceButton;
		private readonly SimpleButton _clearButton;
		private readonly SimpleButton _exportTechnicalReportButton;
		private readonly LabelControl _instructionLabel;
		private readonly LabelControl _workflowStatusLabel;
		private Bitmap _workflowActivityIconBitmap;
		private readonly LabelControl _workflowEtaLabel;
		private readonly System.Windows.Forms.Timer _workflowActivityAnimationTimer;
		private readonly LabelControl _collectionValue;
		private readonly LabelControl _curatorValue;
		private readonly LabelControl _locatorValue;
		private readonly LabelControl _revisionValue;
		private readonly LabelControl _compatibilityValue;
		private readonly LabelControl _contentValue;
		private readonly LabelControl _appliedValue;
		private readonly MemoEdit _summaryBox;
		private readonly GridControl _membersGrid;
		private readonly GridView _membersView;
		private readonly BindingList<MemberGridRow> _memberRows = new BindingList<MemberGridRow>();
		private readonly BarManager _memberMenuManager;
		private readonly PopupMenu _memberActionsMenu;
		private readonly PanelControl _membersHost;
		private readonly PanelControl _membersLoadingOverlay;
		private readonly LabelControl _membersLoadingLabel;
		private readonly ProgressBarControl _membersLoadingProgress;
		private readonly MarqueeProgressBarControl _membersLoadingMarquee;
		private readonly SimpleButton _membersLoadingCancelButton;
		private readonly GridControl _issuesGrid;
		private readonly GridView _issuesView;
		private readonly BindingList<ReviewGridRow> _issueRows = new BindingList<ReviewGridRow>();
		private readonly GridControl _reviewActionsGrid;
		private readonly GridView _reviewActionsView;
		private readonly BindingList<ReviewGridRow> _reviewRows = new BindingList<ReviewGridRow>();
		private readonly LabelControl _membersHeader;
		private readonly TextEdit _memberSearchTextBox;
		private readonly ComboBoxEdit _memberFilterCombo;
		private readonly LabelControl _issuesHeader;
		private readonly LabelControl _reviewActionsHeader;
		private readonly CheckEdit _showErrorIssuesCheckBox;
		private readonly CheckEdit _showWarningIssuesCheckBox;
		private readonly CheckEdit _showInfoIssuesCheckBox;
		private readonly XtraTabControl _reviewTabs;
		private readonly XtraTabPage _membersPage;
		private readonly XtraTabPage _issuesPage;
		private readonly XtraTabPage _reviewActionsPage;
		private int _lastPresentedReviewErrorCount;
		private bool _wasReadyForReview;
		private bool _reviewNavigationPending;
		private bool _wasShowingMemberLoadingOverlay;
		private readonly XtraTabPage _localCollectionsPage;
		private readonly XtraTabPage _advancedOptionsPage;
		private readonly LabelControl _memberSelectionHint;
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
		private CollectionReplacementApplicationService _replacementWorkflow;
		private CollectionRevisionUpdateApplicationService _revisionUpdateWorkflow;
		private NexusCollectionPreviewSnapshot _snapshot;
		private CollectionAdditiveWorkflowPreparationResult _preparation;
		private CollectionReplacementWorkflowReview _replacementReview;
		private CollectionRevisionUpdateWorkflowReview _revisionUpdateReview;
		private CollectionRevisionUpdatePreparationBatch _revisionUpdatePreparation;
		private CollectionRevisionUpdateWorkflowResult _revisionUpdateResult;
		private CollectionMemberAcquisitionBatch _acquisitionBatch;
		private CollectionManagementAssociationPresentation _managedAssociationPresentation;
		private CollectionOperationIdentity _operationIdentity;
		private CollectionOperation _operationSnapshot;
		private CollectionTechnicalReportSnapshot _completedRemovalReport;
		private IReadOnlyList<CollectionReviewItem> _completedRemovalItems = new CollectionReviewItem[0];
		private CollectionPlanIdentity _reviewedPlanIdentity;
		private IReadOnlyList<CollectionAdditiveWorkflowRecoveryResult> _recoveryResults = new CollectionAdditiveWorkflowRecoveryResult[0];
		private IReadOnlyList<CollectionLocalRestoreWorkflowResult> _localRestoreRecoveryResults = new CollectionLocalRestoreWorkflowResult[0];
		private IReadOnlyList<CollectionManagementLocalRestoreRecoverySource> _localRestoreRecoverySources = new CollectionManagementLocalRestoreRecoverySource[0];
		private CollectionManagementLocalRestoreRecoverySource _stoppedLocalRestoreSource;
		private IReadOnlyList<CollectionRevisionUpdateWorkflowResult> _revisionUpdateRecoveryResults = new CollectionRevisionUpdateWorkflowResult[0];
		private IReadOnlyList<CollectionVerifyRepairRecoveryResult> _verifyRepairRecoveryResults = new CollectionVerifyRepairRecoveryResult[0];
		private CancellationTokenSource _previewCancellation;
		private CancellationTokenSource _workflowCancellation;
		private CollectionUiContext _displayContext;
		private CollectionUiContext _workflowContext;
		private CollectionUiContext _currentSetupActionContext;
		private CollectionUiContext _localCaptureActionContext;
		private CollectionUiContext _managedAssociationActionContext;
		private CollectionUiContext _incomingActionContext;
		private Guid? _managedMemberExpansionAssociationId;
		private int _previewGeneration;
		private bool _initialized;
		private bool _workflowBusy;
		private bool _selectionDirty;
		private bool _selectionCapabilityBlocked;
		private bool _hasInterruptedReplacement;
		private bool _suppressMemberCheckEvents;
		private bool _suppressManagedAssociationSelection;
		private readonly Dictionary<CollectionMemberKey, CollectionMemberSelection> _optionalMemberSelectionState =
			new Dictionary<CollectionMemberKey, CollectionMemberSelection>();
		private readonly Dictionary<CollectionMemberKey, CollectionMemberSelection> _inheritedRevisionOptionalSelections =
			new Dictionary<CollectionMemberKey, CollectionMemberSelection>();
		private NexusCollectionPreviewSnapshot _inheritedRevisionPreview;
		private Guid? _inheritedRevisionAssociationId;
		private CollectionRevisionIdentity _inheritedInstalledRevision;
		private readonly System.Windows.Forms.Timer _acquisitionRefreshTimer;
		private readonly HashSet<Guid> _autoResumedQueueOperations = new HashSet<Guid>();
		private Exception _lastTechnicalFailure;
		private string _lastTechnicalFailureCode;
		private int _lastTechnicalFailureGeneration = -1;
		private CollectionWorkflowActivitySnapshot _workflowActivity;
		private CollectionWorkflowEtaSnapshot _workflowEta;
		private readonly CollectionWorkflowEtaEstimator _workflowEtaEstimator;
		private int _workflowActivityAnimationFrame;

		private sealed class MemberFilterChoice
		{
			internal MemberFilterChoice(CollectionMemberListFilterKind kind, string label)
			{
				Kind = kind;
				Label = label ?? String.Empty;
			}

			internal CollectionMemberListFilterKind Kind { get; }
			internal string Label { get; }
			public override string ToString() { return Label; }
		}

		private sealed class RetainedRevisionChoice
		{
			internal RetainedRevisionChoice(CollectionRevision revision)
			{
				Revision = revision ?? throw new ArgumentNullException(nameof(revision));
			}

			internal CollectionRevision Revision { get; }
			public override string ToString()
			{
				string revision = FormatRevisionActionLabel(Revision.Identity);
				return String.IsNullOrWhiteSpace(Revision.RevisionLabel) ||
					StringComparer.CurrentCultureIgnoreCase.Equals(Revision.RevisionLabel, revision)
					? revision : revision + " - " + Revision.RevisionLabel;
			}
		}

		/// <summary>Display data for the DevExpress member grid; the domain member remains the source of identity.</summary>
		private sealed class MemberGridRow
		{
			internal MemberGridRow(NormalizedCollectionMember member, string displayName, bool selected,
				string requirement, string compatibility, string artifact, string managedState)
			{
				Member = member;
				DisplayName = displayName;
				Selected = selected;
				Requirement = requirement;
				Compatibility = compatibility;
				Artifact = artifact;
				ManagedState = managedState;
			}

			public string DisplayName { get; private set; }
			public bool Selected { get; set; }
			public string Requirement { get; private set; }
			public string Selection { get { return Selected ? L("Collections.Member.Selected", "Selected") : L("Collections.Member.Unselected", "Not selected"); } }
			public string Compatibility { get; set; }
			public string Artifact { get; private set; }
			public string ManagedState { get; set; }
			internal NormalizedCollectionMember Member { get; private set; }
		}

		/// <summary>Bound review row; unfiltered action rows and filtered diagnostics retain their independent lists.</summary>
		private sealed class ReviewGridRow
		{
			internal ReviewGridRow(CollectionReviewItem item, string status, string subject, string reason,
				string nextAction, string tooltip)
			{
				Item = item;
				Status = status;
				Subject = subject;
				Reason = reason;
				NextAction = nextAction;
				Tooltip = tooltip;
			}

			public string Status { get; private set; }
			public string Subject { get; private set; }
			public string Reason { get; private set; }
			public string NextAction { get; private set; }
			internal string Tooltip { get; private set; }
			internal CollectionReviewItem Item { get; private set; }
		}

		/// <summary>
		/// Raised on the UI thread when an incoming Collection NXM request should bring this permanent document forward.
		/// </summary>
		public event EventHandler PreviewActivated = delegate { };

		/// <summary>Raised when the installed-member UI asks the main window to show the exact native NMM mod.</summary>
		public event EventHandler<CollectionManagedModRequestEventArgs> ManagedModRequested = delegate { };

		/// <summary>Raised after the durable installed-association list has been refreshed.</summary>
		public event EventHandler ManagedAssociationsChanged = delegate { };

		/// <summary>
		/// Refreshes durable installed-Collection state after ordinary NMM mod management may have changed native reality.
		/// </summary>
		/// <remarks>
		/// This is a read-only UI refresh. Manual mutation tracking remains owned by the normal NMM mutation path; this method
		/// simply reloads the resulting association/drift presentation when the user returns to the Collections document.
		/// </remarks>
		public void RefreshInstalledState()
		{
			if (InvokeRequired)
			{
				BeginInvoke((Action)RefreshInstalledState);
				return;
			}
			if (IsDisposed || Disposing || _workflowBusy || _managementWorkflow == null)
				return;

			RefreshManagedAssociations(_snapshot != null);
		}

		/// <summary>Synchronizes Collection controls and context actions with the global DevExpress display options.</summary>
		internal void ApplyDisplaySettings(DevExpressDisplaySettings settings)
		{
			if (settings == null) return;
			DevExpressDisplaySettingsApplier.ApplySkinSurface(this);
			MakeLayoutSkinTransparent(this);
			DevExpressDisplaySettingsApplier.ApplyToControlTree(this, settings);
			DevExpressDisplaySettingsApplier.ApplyToBarManager(_memberMenuManager, settings);
			EnsureActionCheckCaptionsFit();
			// The status image is rasterized from NMM's semantic SVGs in the current accessibility palette.
			// Refresh it after any icon-style, icon-size or color-profile change.
			RenderWorkflowActivity();
		}

		public CollectionsPreviewControl()
		{
			Text = L("Collections.Title", "Collections");
			Name = "CollectionsDocument";
			HideOnClose = true;
			AutoScaleMode = AutoScaleMode.Font;
			DevExpressDisplaySettingsApplier.ApplySkinSurface(this);
			_acquisitionRefreshTimer = new System.Windows.Forms.Timer { Interval = 750 };
			_acquisitionRefreshTimer.Tick += AcquisitionRefreshTimer_Tick;
			_workflowActivityAnimationTimer = new System.Windows.Forms.Timer { Interval = 125 };
			_workflowActivityAnimationTimer.Tick += WorkflowActivityAnimationTimer_Tick;
			_toolTip = new ToolTipController();
			_workflowActivity = CollectionWorkflowActivityBuilder.Idle(L("Collections.Workflow.Idle", "Workflow: idle"));
			_workflowEtaEstimator = new CollectionWorkflowEtaEstimator();
			_workflowEta = CollectionWorkflowEtaSnapshot.Unavailable("idle");

			TablePanel root = CreateCollectionTable(3);
			root.Padding = new Padding(8);
			root.Rows[2].Style = TablePanelEntityStyle.Relative;
			Controls.Add(root);

			_saveCurrentSetupButton = new SimpleButton
			{
				AutoSize = true,
				Text = L("Collections.Actions.SaveCurrentSetup", "Save current setup as Local Collection"),
				Enabled = false
			};
			_saveCurrentSetupButton.Click += SaveCurrentSetupButton_Click;
			_localCaptureCombo = new ComboBoxEdit
			{
				Width = 280,
				Enabled = false
			};
			_localCaptureCombo.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
			_localCaptureCombo.Properties.NullText = L("Collections.LocalDelete.SelectBackup", "Select a saved Local Collection");
			_localCaptureCombo.SelectedIndexChanged += LocalCaptureCombo_SelectedIndexChanged;
			_restoreLocalCaptureButton = new SimpleButton
			{
				AutoSize = true,
				Text = L("Collections.Actions.RestoreLocal", "Restore Local Collection..."),
				Enabled = false
			};
			_restoreLocalCaptureButton.Click += RestoreLocalCaptureButton_Click;
			_deleteLocalCaptureButton = new SimpleButton
			{
				AutoSize = true,
				Text = L("Collections.Actions.DeleteLocal", "Delete Local Collection..."),
				Enabled = false
			};
			_deleteLocalCaptureButton.Click += DeleteLocalCaptureButton_Click;
			NmmIconProvider.BindDialogButton(_deleteLocalCaptureButton, NmmIconAction.Delete);
			_localWorkingCopyCombo = new ComboBoxEdit
			{
				Width = 320,
				Enabled = false
			};
			_localWorkingCopyCombo.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
			_localWorkingCopyCombo.SelectedIndexChanged += LocalWorkingCopyCombo_SelectedIndexChanged;
			_editLocalWorkingCopyButton = new SimpleButton
			{
				AutoSize = true,
				Text = L("Collections.Actions.EditLocalWorkingCopy", "Edit working copy..."),
				Enabled = false
			};
			_editLocalWorkingCopyButton.Click += EditLocalWorkingCopyButton_Click;
			_saveLocalWorkingCopyRevisionButton = new SimpleButton
			{
				AutoSize = true,
				Text = L("Collections.Actions.SaveLocalWorkingCopyRevision", "Save Local revision..."),
				Enabled = false
			};
			_saveLocalWorkingCopyRevisionButton.Click += SaveLocalWorkingCopyRevisionButton_Click;
			_managedAssociationCombo = new ComboBoxEdit
			{
				Width = 280,
				Enabled = false
			};
			_managedAssociationCombo.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
			_managedAssociationCombo.Properties.NullText = L("Collections.Management.NoneInstalled", "No installed Collections");
			_managedAssociationCombo.SelectedIndexChanged += ManagedAssociationCombo_SelectedIndexChanged;
			_manageAssociationRemovalButton = new SimpleButton
			{
				AutoSize = true,
				Text = L("Collections.Actions.UninstallCollection", "Uninstall Collection..."),
				Enabled = false
			};
			_manageAssociationRemovalButton.Click += ManageAssociationRemovalButton_Click;
			_compareUpdateButton = new SimpleButton
			{
				AutoSize = true,
				Text = L("Collections.Actions.CompareUpdate", "Compare / Update..."),
				Enabled = false
			};
			_compareUpdateButton.Click += CompareUpdateButton_Click;
			_verifyRepairButton = new SimpleButton
			{
				AutoSize = true,
				Text = L("Collections.Actions.VerifyRepair", "Verify / Repair..."),
				Enabled = false
			};
			_verifyRepairButton.Click += VerifyRepairButton_Click;
			_cloneManagedAssociationButton = new SimpleButton
			{
				AutoSize = true,
				Text = L("Collections.Actions.CloneLocalWorkingCopy", "Clone to Local working copy..."),
				Enabled = false
			};
			_cloneManagedAssociationButton.Click += CloneManagedAssociationButton_Click;
			_showManagedMemberButton = new SimpleButton
			{
				AutoSize = true,
				Text = L("Collections.Actions.ShowMemberInMods", "Show selected member in Mods"),
				Enabled = false
			};
			_showManagedMemberButton.Click += ShowManagedMemberButton_Click;
			_showManagedMemberImpactButton = new SimpleButton
			{
				AutoSize = true,
				Text = L("Collections.Actions.ShowMemberImpact", "Show member impact..."),
				Enabled = false
			};
			_showManagedMemberImpactButton.Click += ShowManagedMemberImpactButton_Click;
			_addManagedOptionalMemberButton = new SimpleButton
			{
				AutoSize = true,
				Text = L("Collections.Actions.AddOptionalMember", "Add selected optional member..."),
				Enabled = false
			};
			_addManagedOptionalMemberButton.Click += AddManagedOptionalMemberButton_Click;
			_removeManagedOptionalMemberButton = new SimpleButton
			{
				AutoSize = true,
				Text = L("Collections.Actions.RemoveOptionalMember", "Remove selected optional member..."),
				Enabled = false
			};
			_removeManagedOptionalMemberButton.Click += RemoveManagedOptionalMemberButton_Click;
			_ignoreMemberDifferenceButton = new SimpleButton
			{
				AutoSize = true,
				Text = L("Collections.Actions.IgnoreMemberDifference", "Ignore selected member difference..."),
				Enabled = false
			};
			_ignoreMemberDifferenceButton.Click += IgnoreMemberDifferenceButton_Click;
			_stopIgnoringMemberDifferenceButton = new SimpleButton
			{
				AutoSize = true,
				Text = L("Collections.Actions.StopIgnoringMemberDifference", "Stop ignoring selected member difference..."),
				Enabled = false
			};
			_stopIgnoringMemberDifferenceButton.Click += StopIgnoringMemberDifferenceButton_Click;
			_acceptMemberDriftButton = new SimpleButton
			{
				AutoSize = true,
				Text = L("Collections.Actions.AcceptMemberDrift", "Adopt selected member change..."),
				Enabled = false
			};
			_acceptMemberDriftButton.Click += AcceptMemberDriftButton_Click;
			_clearMemberOverrideButton = new SimpleButton
			{
				AutoSize = true,
				Text = L("Collections.Actions.ClearMemberOverride", "Clear selected member override..."),
				Enabled = false
			};
			_clearMemberOverrideButton.Click += ClearMemberOverrideButton_Click;
			_importButton = new SimpleButton
			{
				AutoSize = true,
				Text = L("Collections.Actions.Import", "Import bundle / collection.json..."),
				Enabled = false
			};
			_importButton.Click += ImportButton_Click;
			_downloadPrepareButton = new SimpleButton
			{
				AutoSize = true,
				Text = L("Collections.Actions.DownloadPrepare", "Download / Prepare"),
				Enabled = false,
				Visible = false
			};
			_downloadPrepareButton.Click += DownloadPrepareButton_Click;
			_resolveFileConflictsButton = new SimpleButton
			{
				AutoSize = true,
				Text = L("Collections.Actions.ResolveFileConflicts", "Resolve file conflicts..."),
				Enabled = false,
				Visible = false
			};
			_resolveFileConflictsButton.Click += ResolveFileConflictsButton_Click;
			_autoOverwriteArchivesCheckBox = new CheckEdit
			{
				AutoSize = true,
				Text = L("Collections.Actions.AutoOverwriteArchives", "Overwrite automatically all present archives"),
				Checked = false,
				Enabled = false,
				Padding = new Padding(3, 3, 3, 0)
			};
			_toolTip.SetToolTip(_autoOverwriteArchivesCheckBox, L("Collections.Actions.AutoOverwriteArchivesHelp",
				"For Collection downloads/imports queued by this Download / Prepare batch, replace existing NMM mod archives at their original archive path without asking for each collision. Verified archives are still reused; manually added files outside this correlated workflow and installation file-conflict rules are unchanged."));
			_resumeButton = new SimpleButton
			{
				AutoSize = true,
				Text = L("Collections.Actions.ResumePreparation", "Check downloads and continue"),
				Enabled = false,
				Visible = false
			};
			_resumeButton.Click += ResumeButton_Click;
			_openPendingButton = new SimpleButton
			{
				AutoSize = true,
				Text = L("Collections.Actions.OpenDownloadPage", "Download selected missing mod"),
				Enabled = false,
				Visible = false
			};
			_openPendingButton.Click += OpenPendingButton_Click;
			_installButton = new SimpleButton
			{
				AutoSize = true,
				Text = L("Collections.Actions.InstallCurrent", "Review and install..."),
				Enabled = false,
				Visible = false
			};
			_installButton.Click += InstallButton_Click;
			_primaryIncomingActionButton = new SimpleButton
			{
				AutoSize = true,
				MinimumSize = new Size(150, 0),
				Text = _downloadPrepareButton.Text,
				Enabled = false,
				Appearance = { FontStyleDelta = FontStyle.Bold }
			};
			_primaryIncomingActionButton.Click += PrimaryIncomingActionButton_Click;
			_replacementBackupCheckBox = new CheckEdit
			{
				AutoSize = true,
				Text = L("Collections.Actions.ReplaceBackup", "Create Local Collection backup first"),
				Checked = true,
				Enabled = false,
				Padding = new Padding(3, 3, 3, 0)
			};
			_toolTip.SetToolTip(_replacementBackupCheckBox, L("Collections.Actions.ReplaceBackupHelp",
				"Replacement always prepares mandatory operation recovery. This option additionally saves a persistent Local Collection before managed effects are removed."));
			_replaceButton = new SimpleButton
			{
				AutoSize = true,
				Text = L("Collections.Actions.ReplaceCurrent", "Replace current managed setup..."),
				Enabled = false
			};
			_replaceButton.Click += ReplaceButton_Click;
			_clearButton = new SimpleButton
			{
				AutoSize = true,
				Text = L("Collections.Actions.ClearPreview", "Close this preview")
			};
			_clearButton.Click += ClearButton_Click;
			_exportTechnicalReportButton = new SimpleButton
			{
				AutoSize = true,
				Text = L("Collections.Actions.ExportTechnicalReport", "Export Technical Report..."),
				Enabled = false
			};
			_exportTechnicalReportButton.Click += ExportTechnicalReportButton_Click;
			_instructionLabel = new LabelControl
			{
				AutoSizeMode = LabelAutoSizeMode.Vertical,
				Dock = DockStyle.Fill,
				MaximumSize = new Size(760, 0),
				Padding = new Padding(0, 4, 0, 8),
				Text = L("Collections.Preview.Instructions", "Use the Incoming Collection primary action for the next workflow step. Right-click a member for member-specific actions. Compare / Update and Verify / Repair manage installed Collections; Replace current managed setup remains a separate destructive reviewed transition.")
			};
			_instructionLabel.Appearance.TextOptions.WordWrap = WordWrap.Wrap;
			_instructionLabel.Appearance.Options.UseTextOptions = true;
			_currentSetupGroup = CreateActionGroup(L("Collections.Context.CurrentSetup", "Current game setup"),
				_saveCurrentSetupButton);
			_savedLocalGroup = CreateActionGroup(L("Collections.Context.SavedLocal", "Saved Local Collection"),
				_localCaptureCombo, _restoreLocalCaptureButton, _deleteLocalCaptureButton);
			_localWorkingCopyGroup = CreateActionGroup(L("Collections.Context.LocalWorkingCopy", "Local working copies"),
				_localWorkingCopyCombo, _editLocalWorkingCopyButton, _saveLocalWorkingCopyRevisionButton);
			Control installedActions = CreateActionGroup(L("Collections.Context.Installed", "Installed Collection"),
				_managedAssociationCombo, _compareUpdateButton, _verifyRepairButton, _manageAssociationRemovalButton, _cloneManagedAssociationButton);
			installedActions.Controls[0].Visible = false;
			_installedGroup = new CollectionSectionGroup(L("Collections.Context.Installed", "Installed Collection"), installedActions);
			LabelControl previewContextLabel;
			CollectionActionPanel incomingActionsPanel;
			Control incomingGroup = CreateActionGroup(L("Collections.Context.Incoming", "Incoming Collection"),
				out previewContextLabel, out incomingActionsPanel,
				_primaryIncomingActionButton, _importButton, _clearButton, _exportTechnicalReportButton,
				_downloadPrepareButton, _resolveFileConflictsButton, _resumeButton, _openPendingButton, _installButton);
			_previewContextLabel = previewContextLabel;
			_incomingActionsPanel = incomingActionsPanel;
			_previewContextLabel.Visible = false;
			TablePanel previewLayout = (TablePanel)incomingGroup;
			previewLayout.Rows.Add(new TablePanelRow(TablePanelEntityStyle.AutoSize, 1F));
			previewLayout.Controls.Remove(_previewContextLabel);
			previewLayout.SetCell(_incomingActionsPanel, 2, 0);
			_previewSection = new CollectionSectionGroup(L("Collections.Context.Incoming", "Incoming Collection"), previewLayout);
			AddCollectionCell(root, _previewSection, 0);
			AddCollectionCell(root, _installedGroup, 1);

			// Keep identity, current status and the next action together; move reference information out of the working area.
			TablePanel header = CreateCollectionTable(2, 4);
			header.AutoSize = true;
			header.Columns[0].Style = TablePanelEntityStyle.AutoSize;
			header.Columns[2].Style = TablePanelEntityStyle.AutoSize;
			_collectionValue = AddHeaderRow(header, 0, 0, L("Collections.Fields.Collection", "Collection:"));
			_revisionValue = AddHeaderRow(header, 0, 2, L("Collections.Fields.Revision", "Revision:"));
			_appliedValue = AddHeaderRow(header, 1, 0, L("Collections.Status.Applied", "Applied state:"));
			_contentValue = AddHeaderRow(header, 1, 2, L("Collections.Status.Content", "Content readiness:"));
			AddCollectionCell(previewLayout, header, 0);

			TablePanel workflowStatusPanel = CreateCollectionTable(1, 2);
			workflowStatusPanel.AutoSize = true;
			workflowStatusPanel.Columns[1].Style = TablePanelEntityStyle.AutoSize;
			_workflowStatusLabel = new LabelControl
			{
				AutoSizeMode = LabelAutoSizeMode.Vertical, Dock = DockStyle.Fill,
				Padding = new Padding(0, 4, 0, 4), ImageAlignToText = ImageAlignToText.LeftCenter,
				IndentBetweenImageAndText = 7, Text = L("Collections.Workflow.Idle", "Workflow: idle")
			};
			_workflowStatusLabel.Appearance.TextOptions.WordWrap = WordWrap.Wrap;
			_workflowStatusLabel.Appearance.Options.UseTextOptions = true;
			_workflowEtaLabel = new LabelControl
			{
				AutoSizeMode = LabelAutoSizeMode.Horizontal, Padding = new Padding(12, 3, 0, 6),
				Text = String.Empty, Visible = false
			};
			_workflowEtaLabel.Appearance.TextOptions.HAlignment = HorzAlignment.Far;
			AddCollectionCell(workflowStatusPanel, _workflowStatusLabel, 0);
			AddCollectionCell(workflowStatusPanel, _workflowEtaLabel, 0, 1);
			AddCollectionCell(previewLayout, workflowStatusPanel, 1);

			_reviewTabs = new XtraTabControl { Dock = DockStyle.Fill };
			_membersPage = CreateCollectionPage(_reviewTabs, L("Collections.Tabs.Mods", "Choose mods"));
			_issuesPage = CreateCollectionPage(_reviewTabs, L("Collections.Preview.Issues", "Review / issues"));
			_reviewActionsPage = CreateCollectionPage(_reviewTabs, L("Collections.Tabs.Changes", "Changes to review"));
			XtraTabPage detailsPage = CreateCollectionPage(_reviewTabs, L("Collections.Tabs.Details", "About this Collection"));
			_localCollectionsPage = CreateCollectionPage(_reviewTabs, L("Collections.Tabs.Local", "Backups and local Collections"));
			_advancedOptionsPage = CreateCollectionPage(_reviewTabs, L("Collections.Tabs.Advanced", "Advanced options"));
			TablePanel detailsLayout = CreateCollectionTable(3);
			detailsLayout.Rows[2].Style = TablePanelEntityStyle.Relative;
			TablePanel detailsHeader = CreateCollectionTable(3, 2);
			detailsHeader.Columns[0].Style = TablePanelEntityStyle.AutoSize;
			_curatorValue = AddHeaderRow(detailsHeader, 0, 0, L("Collections.Fields.Curator", "Curator:"));
			_locatorValue = AddHeaderRow(detailsHeader, 1, 0, L("Collections.Fields.Locator", "Nexus locator:"));
			_compatibilityValue = AddHeaderRow(detailsHeader, 2, 0, L("Collections.Status.Compatibility", "Compatibility:"));
			_summaryBox = new MemoEdit
			{
				Dock = DockStyle.Fill, ReadOnly = true,
				Text = L("Collections.Preview.EmptySummary", "No Collection preview is loaded.")
			};
			_summaryBox.Properties.ScrollBars = ScrollBars.Vertical;
			AddCollectionCell(detailsLayout, detailsHeader, 0);
			AddCollectionCell(detailsLayout, _instructionLabel, 1);
			AddCollectionCell(detailsLayout, _summaryBox, 2);
			detailsPage.Controls.Add(detailsLayout);
			TablePanel localLayout = CreateCollectionTable(4);
			localLayout.AutoScroll = true;
			AddCollectionCell(localLayout, _currentSetupGroup, 0);
			AddCollectionCell(localLayout, _savedLocalGroup, 1);
			AddCollectionCell(localLayout, _localWorkingCopyGroup, 2);
			_localCollectionsPage.Controls.Add(localLayout);
			_advancedIncomingGroup = CreateActionGroup(L("Collections.Tabs.Advanced", "Advanced options"),
				_autoOverwriteArchivesCheckBox, _replacementBackupCheckBox, _replaceButton);
			TablePanel advancedLayout = CreateCollectionTable(2);
			AddCollectionCell(advancedLayout, _advancedIncomingGroup, 0);
			LabelControl advancedHint = CreateApprovalLabel(L("Collections.Advanced.Help",
				"These options can replace downloaded archives or the current mod setup. Use the main action above to install or update this Collection."));
			AddCollectionCell(advancedLayout, advancedHint, 1);
			_advancedOptionsPage.Controls.Add(advancedLayout);

			_membersHeader = new LabelControl { AutoSizeMode = LabelAutoSizeMode.Horizontal, Text = L("Collections.Preview.Members", "Members"), Margin = new Padding(0, 3, 10, 0) };
			_memberSearchTextBox = new TextEdit
			{
				Width = 180,
				Margin = new Padding(0, 0, 8, 0),
				Enabled = false
			};
			_memberSearchTextBox.TextChanged += MemberListFilter_Changed;
			_memberFilterCombo = new ComboBoxEdit
			{
				Width = 135,
				Margin = Padding.Empty,
				Enabled = false
			};
			_memberFilterCombo.Properties.Items.Add(new MemberFilterChoice(CollectionMemberListFilterKind.All, L("Collections.MemberFilter.All", "All")));
			_memberFilterCombo.Properties.Items.Add(new MemberFilterChoice(CollectionMemberListFilterKind.Selected, L("Collections.MemberFilter.Selected", "Selected")));
			_memberFilterCombo.Properties.Items.Add(new MemberFilterChoice(CollectionMemberListFilterKind.Unselected, L("Collections.MemberFilter.Unselected", "Not selected")));
			_memberFilterCombo.Properties.Items.Add(new MemberFilterChoice(CollectionMemberListFilterKind.Required, L("Collections.MemberFilter.Required", "Required")));
			_memberFilterCombo.Properties.Items.Add(new MemberFilterChoice(CollectionMemberListFilterKind.Optional, L("Collections.MemberFilter.Optional", "Optional")));
			_memberFilterCombo.Properties.Items.Add(new MemberFilterChoice(CollectionMemberListFilterKind.NeedsAttention, L("Collections.MemberFilter.NeedsAttention", "Needs attention")));
			_memberFilterCombo.Properties.Items.Add(new MemberFilterChoice(CollectionMemberListFilterKind.ChangedLocally, L("Collections.MemberFilter.ChangedLocally", "Changed locally")));
			_memberFilterCombo.SelectedIndex = 0;
			_memberFilterCombo.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
			_memberFilterCombo.SelectedIndexChanged += MemberListFilter_Changed;
			_issuesHeader = new LabelControl
			{
				AutoSizeMode = LabelAutoSizeMode.Horizontal,
				Text = L("Collections.Preview.Issues", "Review / issues"),
				Margin = new Padding(0, 3, 12, 0)
			};
			_showErrorIssuesCheckBox = new CheckEdit { AutoSize = true, Checked = true, Margin = new Padding(0, 0, 8, 0) };
			_showWarningIssuesCheckBox = new CheckEdit { AutoSize = true, Checked = true, Margin = new Padding(0, 0, 8, 0) };
			_showInfoIssuesCheckBox = new CheckEdit { AutoSize = true, Checked = true, Margin = new Padding(0, 0, 0, 0) };
			_showErrorIssuesCheckBox.CheckedChanged += ReviewSeverityFilter_CheckedChanged;
			_showWarningIssuesCheckBox.CheckedChanged += ReviewSeverityFilter_CheckedChanged;
			_showInfoIssuesCheckBox.CheckedChanged += ReviewSeverityFilter_CheckedChanged;

			CollectionActionPanel issuesHeaderPanel = new CollectionActionPanel();
			issuesHeaderPanel.Controls.Add(_issuesHeader);
			issuesHeaderPanel.Controls.Add(_showErrorIssuesCheckBox);
			issuesHeaderPanel.Controls.Add(_showWarningIssuesCheckBox);
			issuesHeaderPanel.Controls.Add(_showInfoIssuesCheckBox);
			CollectionActionPanel membersHeaderPanel = new CollectionActionPanel();
			membersHeaderPanel.Controls.Add(_membersHeader);
			membersHeaderPanel.Controls.Add(new LabelControl
			{
				AutoSizeMode = LabelAutoSizeMode.Horizontal, Text = L("Collections.MemberFilter.Find", "Find:"),
				Margin = new Padding(0, 3, 4, 0)
			});
			membersHeaderPanel.Controls.Add(_memberSearchTextBox);
			membersHeaderPanel.Controls.Add(_memberFilterCombo);
			_memberSelectionHint = new LabelControl
			{
				AutoSizeMode = LabelAutoSizeMode.Horizontal, Margin = new Padding(12, 3, 0, 0),
				Text = L("Collections.Members.OptionalHint", "Required mods are included. Tick optional mods you want to use.")
			};
			membersHeaderPanel.Controls.Add(_memberSelectionHint);
			TablePanel membersLayout = CreateCollectionTable(2);
			membersLayout.Rows[1].Style = TablePanelEntityStyle.Relative;
			AddCollectionCell(membersLayout, membersHeaderPanel, 0);
			_membersPage.Controls.Add(membersLayout);

			_membersHost = new PanelControl
			{
				Dock = DockStyle.Fill,
				BorderStyle = BorderStyles.NoBorder,
				Margin = Padding.Empty
			};
			_membersGrid = new GridControl { Dock = DockStyle.Fill };
			_membersView = CreateCollectionGridView(_membersGrid);
			_membersGrid.DataSource = _memberRows;
			// Toggle directly instead of making the first click focus a row and open its editor.
			_membersView.OptionsBehavior.Editable = false;
			_membersView.OptionsView.ColumnAutoWidth = true;
			_membersView.MouseDown += MembersView_MouseDown;
			_membersGrid.KeyDown += MembersGrid_KeyDown;
			_membersView.FocusedRowChanged += MembersView_FocusedRowChanged;
			_membersView.CellValueChanged += MembersView_CellValueChanged;
			_membersGrid.MouseDown += MembersGrid_MouseDown;
			RepositoryItemCheckEdit memberCheck = new RepositoryItemCheckEdit { AllowGrayed = false };
			_membersGrid.RepositoryItems.Add(memberCheck);
			GridColumn checkColumn = AddCollectionGridColumn(_membersView, nameof(MemberGridRow.Selected),
				L("Collections.Columns.Use", "Use"), 45);
			checkColumn.ColumnEdit = memberCheck;
			checkColumn.OptionsColumn.FixedWidth = true;
			AddCollectionGridColumn(_membersView, nameof(MemberGridRow.DisplayName), L("Collections.Columns.ModName", "Mod"), 320);
			AddCollectionGridColumn(_membersView, nameof(MemberGridRow.Requirement), L("Collections.Columns.Requirement", "Requirement"), 88);
			AddCollectionGridColumn(_membersView, nameof(MemberGridRow.Selection), L("Collections.Columns.Selection", "Selection"), 82).Visible = false;
			AddCollectionGridColumn(_membersView, nameof(MemberGridRow.Compatibility), L("Collections.Columns.Compatibility", "Compatibility"), 112);
			AddCollectionGridColumn(_membersView, nameof(MemberGridRow.Artifact), L("Collections.Columns.Artifact", "Artifact"), 240).Visible = false;
			AddCollectionGridColumn(_membersView, nameof(MemberGridRow.ManagedState), L("Collections.Columns.ManagedState", "Collection state"), 260);

			_memberMenuManager = new BarManager { Form = this };
			_memberActionsMenu = new PopupMenu(_memberMenuManager);
			AddMemberActionMenuItem(_memberActionsMenu, _showManagedMemberButton, ShowManagedMemberButton_Click, false);
			AddMemberActionMenuItem(_memberActionsMenu, _showManagedMemberImpactButton, ShowManagedMemberImpactButton_Click, false);
			AddMemberActionMenuItem(_memberActionsMenu, _addManagedOptionalMemberButton, AddManagedOptionalMemberButton_Click, true);
			AddMemberActionMenuItem(_memberActionsMenu, _removeManagedOptionalMemberButton, RemoveManagedOptionalMemberButton_Click, false);
			AddMemberActionMenuItem(_memberActionsMenu, _ignoreMemberDifferenceButton, IgnoreMemberDifferenceButton_Click, true);
			AddMemberActionMenuItem(_memberActionsMenu, _stopIgnoringMemberDifferenceButton, StopIgnoringMemberDifferenceButton_Click, false);
			AddMemberActionMenuItem(_memberActionsMenu, _acceptMemberDriftButton, AcceptMemberDriftButton_Click, false);
			AddMemberActionMenuItem(_memberActionsMenu, _clearMemberOverrideButton, ClearMemberOverrideButton_Click, false);
			_membersGrid.ToolTipController = _toolTip;
			_toolTip.GetActiveObjectInfo += (sender, args) =>
			{
				if (args.SelectedControl != _membersGrid || args.Info != null) return;
				var hit = _membersView.CalcHitInfo(args.ControlMousePosition);
				if (hit.InRowCell || hit.InRow)
				{
					MemberGridRow row = _membersView.GetRow(hit.RowHandle) as MemberGridRow;
					if (row != null) args.Info = new ToolTipControlInfo(row,
						row.DisplayName + Environment.NewLine + row.Requirement + " - " + row.Selection + Environment.NewLine +
						row.ManagedState + Environment.NewLine + row.Artifact + Environment.NewLine +
						L("Collections.Member.ContextHint", "Right-click a member for Collection-specific actions."));
				}
			};
			_membersHost.Controls.Add(_membersGrid);

			_membersLoadingOverlay = new PanelControl
			{
				Dock = DockStyle.Fill,
				BorderStyle = BorderStyles.Simple,
				Visible = false
			};
			TablePanel membersLoadingLayout = CreateCollectionTable(5, 3);
			membersLoadingLayout.Padding = new Padding(16);
			// TablePanel stretches cell contents regardless of Anchor; bound the shared progress host to a center column.
			membersLoadingLayout.Columns[1].Style = TablePanelEntityStyle.Absolute;
			membersLoadingLayout.Columns[1].Width = 220;
			membersLoadingLayout.Rows[0].Style = TablePanelEntityStyle.Relative;
			membersLoadingLayout.Rows[4].Style = TablePanelEntityStyle.Relative;
			_membersLoadingLabel = new LabelControl
			{
				AutoSizeMode = LabelAutoSizeMode.Vertical,
				Dock = DockStyle.Fill,
				Margin = new Padding(0, 0, 0, 8)
			};
			_membersLoadingProgress = new ProgressBarControl
			{
				Anchor = AnchorStyles.None, Width = 220, Height = 16,
				TabStop = false, Visible = false
			};
			_membersLoadingProgress.Properties.Minimum = 0;
			_membersLoadingProgress.Properties.Maximum = 1000;
			_membersLoadingMarquee = new MarqueeProgressBarControl
			{
				Anchor = AnchorStyles.None, Width = 220, Height = 16, TabStop = false
			};
			_membersLoadingMarquee.Properties.MarqueeAnimationSpeed = 30;
			_membersLoadingCancelButton = new SimpleButton
			{
				Anchor = AnchorStyles.None,
				AutoSize = true,
				Text = L("Collections.Capture.Cancel", "Cancel capture"),
				Visible = false,
				Margin = new Padding(0, 10, 0, 0)
			};
			_membersLoadingCancelButton.Click += MembersLoadingCancelButton_Click;
			_membersLoadingLabel.Appearance.TextOptions.HAlignment = HorzAlignment.Center;
			AddCollectionCell(membersLoadingLayout, _membersLoadingLabel, 1);
			membersLoadingLayout.SetColumnSpan(_membersLoadingLabel, 3);
			var progressHost = new PanelControl
			{
				Anchor = AnchorStyles.None, Width = 220, Height = 18, BorderStyle = BorderStyles.NoBorder
			};
			_membersLoadingProgress.Dock = DockStyle.Fill;
			_membersLoadingMarquee.Dock = DockStyle.Fill;
			progressHost.Controls.Add(_membersLoadingProgress);
			progressHost.Controls.Add(_membersLoadingMarquee);
			AddCollectionCell(membersLoadingLayout, progressHost, 2, 1);
			AddCollectionCell(membersLoadingLayout, _membersLoadingCancelButton, 3, 1);
			_membersLoadingOverlay.Controls.Add(membersLoadingLayout);
			_membersHost.Controls.Add(_membersLoadingOverlay);
			AddCollectionCell(membersLayout, _membersHost, 1);
			TablePanel issuesLayout = CreateCollectionTable(2);
			issuesLayout.Rows[1].Style = TablePanelEntityStyle.Relative;
			_issuesGrid = CreateReviewGrid(_issueRows, out _issuesView);
			AddCollectionCell(issuesLayout, issuesHeaderPanel, 0);
			AddCollectionCell(issuesLayout, _issuesGrid, 1);
			_issuesPage.Controls.Add(issuesLayout);
			TablePanel changesLayout = CreateCollectionTable(2);
			changesLayout.Rows[1].Style = TablePanelEntityStyle.Relative;
			_reviewActionsHeader = new LabelControl
			{
				AutoSizeMode = LabelAutoSizeMode.Vertical, Dock = DockStyle.Fill,
				Padding = new Padding(0, 4, 0, 4)
			};
			_reviewActionsGrid = CreateReviewGrid(_reviewRows, out _reviewActionsView);
			AddCollectionCell(changesLayout, _reviewActionsHeader, 0);
			AddCollectionCell(changesLayout, _reviewActionsGrid, 1);
			_reviewActionsPage.Controls.Add(changesLayout);
			_detailsSection = new GroupControl
			{
				Dock = DockStyle.Fill, ShowCaption = false, BorderStyle = BorderStyles.NoBorder
			};
			_detailsSection.Controls.Add(_reviewTabs);
			AddCollectionCell(root, _detailsSection, 2);
			MakeLayoutSkinTransparent(root);
			EnsureActionCheckCaptionsFit();

			_displayContext = CollectionUiContext.None(_previewGeneration);
			RenderWorkflowActivity();
			RenderEmptyState();
		}

		/// <summary>Connects the surface to the incoming Collection dispatcher in preview-only compatibility mode.</summary>
		public void Initialize(NexusCollectionNxmDispatcher dispatcher)
		{
			Initialize(dispatcher, null, null, null, null, null);
		}

		/// <summary>Connects the surface to the incoming dispatcher and production additive workflow service.</summary>
		public void Initialize(NexusCollectionNxmDispatcher dispatcher, CollectionAdditiveApplicationService workflow)
		{
			Initialize(dispatcher, workflow, null, null, null, null);
		}

		/// <summary>Connects additive and Local Collection capture application workflows to this permanent surface.</summary>
		public void Initialize(NexusCollectionNxmDispatcher dispatcher, CollectionAdditiveApplicationService workflow,
			CollectionLocalCaptureApplicationService captureWorkflow)
		{
			Initialize(dispatcher, workflow, captureWorkflow, null, null, null);
		}

		/// <summary>Connects additive, capture and basic installed-Collection management workflows to this permanent surface.</summary>
		public void Initialize(NexusCollectionNxmDispatcher dispatcher, CollectionAdditiveApplicationService workflow,
			CollectionLocalCaptureApplicationService captureWorkflow, CollectionManagementApplicationService managementWorkflow)
		{
			Initialize(dispatcher, workflow, captureWorkflow, managementWorkflow, null, null);
		}

		/// <summary>Connects additive, replacement, capture and installed-Collection workflows to this permanent surface.</summary>
		public void Initialize(NexusCollectionNxmDispatcher dispatcher, CollectionAdditiveApplicationService workflow,
			CollectionLocalCaptureApplicationService captureWorkflow, CollectionManagementApplicationService managementWorkflow,
			CollectionReplacementApplicationService replacementWorkflow)
		{
			Initialize(dispatcher, workflow, captureWorkflow, managementWorkflow, replacementWorkflow, null);
		}

		/// <summary>Connects additive, replacement, revision-update, capture and installed-Collection workflows to this permanent surface.</summary>
		public void Initialize(NexusCollectionNxmDispatcher dispatcher, CollectionAdditiveApplicationService workflow,
			CollectionLocalCaptureApplicationService captureWorkflow, CollectionManagementApplicationService managementWorkflow,
			CollectionReplacementApplicationService replacementWorkflow, CollectionRevisionUpdateApplicationService revisionUpdateWorkflow)
		{
			if (ReferenceEquals(_dispatcher, dispatcher) && ReferenceEquals(_workflow, workflow) &&
				ReferenceEquals(_captureWorkflow, captureWorkflow) && ReferenceEquals(_managementWorkflow, managementWorkflow) &&
				ReferenceEquals(_replacementWorkflow, replacementWorkflow) && ReferenceEquals(_revisionUpdateWorkflow, revisionUpdateWorkflow) && _initialized)
				return;

			DetachDispatcher();
			_completedRemovalReport = null;
			_completedRemovalItems = new CollectionReviewItem[0];
			CancelPreviewWork();
			CancelWorkflowWork();
			_snapshot = null;
			_managedAssociationPresentation = null;
			_displayContext = CollectionUiContext.None(_previewGeneration);
			_workflow = workflow;
			_captureWorkflow = captureWorkflow;
			_managementWorkflow = managementWorkflow;
			_replacementWorkflow = replacementWorkflow;
			_revisionUpdateWorkflow = revisionUpdateWorkflow;
			ResetWorkflowViewState();
			RenderEmptyState();
			RefreshLocalCaptures();
			RefreshLocalWorkingCopies();
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
				_memberMenuManager.Dispose();
				DetachDispatcher();
				CancelPreviewWork();
				CancelWorkflowWork();
				_workflowActivityAnimationTimer.Stop();
				_workflowActivityAnimationTimer.Dispose();
				// The NMM icon provider owns its cached bitmaps; only dispose our private rendered copy.
				if (_workflowStatusLabel != null && !_workflowStatusLabel.IsDisposed)
					_workflowStatusLabel.ImageOptions.Image = null;
				_workflowActivityIconBitmap?.Dispose();
				_workflowActivityIconBitmap = null;
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
			{
				if (result.UiQueuePublishedTimestamp != 0)
					CollectionPerformanceMetrics.RecordNxmUiDispatch(result.UiQueuePublishedTimestamp);
				newest = result;
			}
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

			_localCaptureCombo.Properties.Items.BeginUpdate();
			try
			{
				_localCaptureCombo.SelectedIndex = -1;
				_localCaptureCombo.EditValue = null;
				_localCaptureCombo.Properties.Items.Clear();
				if (_managementWorkflow == null) return;
				IReadOnlyList<CollectionManagementLocalCapture> captures = _managementWorkflow.GetLocalCaptures();
				foreach (CollectionManagementLocalCapture capture in captures)
					_localCaptureCombo.Properties.Items.Add(capture);
				_localCaptureCombo.SelectedIndex = FindLocalCaptureSelectionIndex(captures, selectedId);
			}
			catch (Exception ex)
			{
				Trace.TraceError("Local Collection capture list refresh failed: " + ex);
			}
			finally
			{
				_localCaptureCombo.Properties.Items.EndUpdate();
				UpdateActionButtons();
			}
		}

		/// <summary>Preserves only an exact saved capture selection; missing selections never fall back to the first entry.</summary>
		internal static int FindLocalCaptureSelectionIndex(IReadOnlyList<CollectionManagementLocalCapture> captures, LocalCaptureIdentity selectedId)
		{
			if (captures == null) throw new ArgumentNullException(nameof(captures));
			if (selectedId == null) return -1;
			int selectedIndex = -1;
			for (int index = 0; index < captures.Count; index++)
			{
				if (!captures[index].CaptureIdentity.Equals(selectedId)) continue;
				if (selectedIndex >= 0) throw new InvalidDataException("The saved Local Collection selector contains a duplicate capture identity.");
				selectedIndex = index;
			}
			return selectedIndex;
		}

		private void RefreshLocalWorkingCopies(CollectionIdentity selectedCollection = null)
		{
			string selectedId = selectedCollection == null ? null : selectedCollection.StableId;
			CollectionManagementLocalWorkingCopy current = _localWorkingCopyCombo.SelectedItem as CollectionManagementLocalWorkingCopy;
			if (selectedId == null && current != null)
				selectedId = current.Collection.StableId;

			_localWorkingCopyCombo.Properties.Items.BeginUpdate();
			try
			{
				_localWorkingCopyCombo.Properties.Items.Clear();
				if (_managementWorkflow == null)
					return;
				foreach (CollectionManagementLocalWorkingCopy workingCopy in _managementWorkflow.GetLocalWorkingCopies())
					_localWorkingCopyCombo.Properties.Items.Add(workingCopy);
				if (_localWorkingCopyCombo.Properties.Items.Count > 0)
				{
					int selectedIndex = 0;
					if (selectedId != null)
					{
						for (int index = 0; index < _localWorkingCopyCombo.Properties.Items.Count; index++)
						{
							CollectionManagementLocalWorkingCopy candidate = _localWorkingCopyCombo.Properties.Items[index] as CollectionManagementLocalWorkingCopy;
							if (candidate != null && StringComparer.Ordinal.Equals(candidate.Collection.StableId, selectedId))
							{
								selectedIndex = index;
								break;
							}
						}
					}
					_localWorkingCopyCombo.SelectedIndex = selectedIndex;
				}
			}
			catch (Exception ex)
			{
				Trace.TraceError("Local Collection working-copy list refresh failed: " + ex);
			}
			finally
			{
				_localWorkingCopyCombo.Properties.Items.EndUpdate();
				UpdateActionButtons();
			}
		}

		private void LocalWorkingCopyCombo_SelectedIndexChanged(object sender, EventArgs e)
		{
			UpdateActionButtons();
		}

		private void ManagedAssociationCombo_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (_suppressManagedAssociationSelection)
				return;
			ShowSelectedManagedAssociation();
		}

		private void RefreshManagedAssociations(bool preserveWorkflowView = false)
		{
			Guid selectedId = Guid.Empty;
			CollectionManagementAssociation selected = _managedAssociationCombo.SelectedItem as CollectionManagementAssociation;
			if (selected != null)
				selectedId = selected.AssociationId;

			_suppressManagedAssociationSelection = true;
			_managedAssociationCombo.Properties.Items.BeginUpdate();
			try
			{
				_managedAssociationCombo.Properties.Items.Clear();
				_managedAssociationCombo.SelectedIndex = -1;
				_managedAssociationCombo.EditValue = null;
				if (_managementWorkflow == null)
					return;

				IReadOnlyList<CollectionManagementAssociation> associations = _managementWorkflow.GetAssociations();
				foreach (CollectionManagementAssociation association in associations)
					_managedAssociationCombo.Properties.Items.Add(association);

				if (_managedAssociationCombo.Properties.Items.Count > 0)
				{
					int selectedIndex = -1;

					// When a Collection preview is loaded, prefer its exact installed association. This keeps the
					// management actions visually tied to the revision the user is currently reviewing.
					if (_snapshot != null && _snapshot.Revision != null)
					{
						for (int index = 0; index < _managedAssociationCombo.Properties.Items.Count; index++)
						{
							CollectionManagementAssociation candidate = _managedAssociationCombo.Properties.Items[index] as CollectionManagementAssociation;
							if (candidate != null && candidate.Association.Revision.Equals(_snapshot.Revision.Identity))
							{
								selectedIndex = index;
								break;
							}
						}
					}

					if (selectedIndex < 0 && selectedId != Guid.Empty)
					{
						for (int index = 0; index < _managedAssociationCombo.Properties.Items.Count; index++)
						{
							CollectionManagementAssociation candidate = _managedAssociationCombo.Properties.Items[index] as CollectionManagementAssociation;
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
				_managedAssociationCombo.Properties.Items.EndUpdate();
				_suppressManagedAssociationSelection = false;
				if (preserveWorkflowView && _snapshot != null)
				{
					// Refresh the installed selector without resetting the current journal/result. After a successful incoming apply,
					// _managedAssociationPresentation is still null, so bind the exact matching durable association here as an overlay.
					// The same path makes manual Mods-tab mutations visible when the user returns to Collections.
					CollectionManagementAssociation current = _managedAssociationCombo.SelectedItem as CollectionManagementAssociation;
					bool currentMatchesSnapshot = current != null && _snapshot.Revision != null &&
						current.Association.Revision.Equals(_snapshot.Revision.Identity);
					if (currentMatchesSnapshot)
					{
						try
						{
							CollectionManagementAssociationPresentation previous = _managedAssociationPresentation;
							_managedAssociationPresentation = _managementWorkflow.GetAssociationPresentation(current.AssociationId);
							bool stateChanged = !HasSameManagedAssociationState(previous, _managedAssociationPresentation);
							ApplyManagedAssociationPresentation();
							ApplyManagedAssociationMemberState(_managedAssociationPresentation);
							if (stateChanged && _managedAssociationPresentation != null &&
								_managedAssociationPresentation.Association.State != CollectionAssociationState.Applied)
							{
								RenderIssues(_snapshot, false, false);
								AppendManagedAssociationIssues(_managedAssociationPresentation);
								_workflowStatusLabel.Text = L("Collections.Workflow.ManagedStateReview",
									"Workflow: the tracked installed Collection state changed; use Verify / Repair to review its current requirements.");
							}
						}
						catch (Exception ex)
						{
							Trace.TraceError("Installed Collection result presentation refresh failed: " + ex);
						}
					}
					UpdateActionButtons();
				}
				else if (_snapshot == null || _managedAssociationPresentation != null)
					ShowSelectedManagedAssociation();
				else
				{
					ApplyManagedAssociationPresentation();
					UpdateActionButtons();
				}
				ManagedAssociationsChanged(this, EventArgs.Empty);
			}
		}

		/// <summary>Detects changes in the durable installed presentation without discarding an unchanged verification review.</summary>
		internal static bool HasSameManagedAssociationState(CollectionManagementAssociationPresentation previous,
			CollectionManagementAssociationPresentation current)
		{
			if (previous == null || current == null || previous.Association.AssociationId != current.Association.AssociationId ||
				!previous.Association.Association.Revision.Equals(current.Association.Association.Revision) ||
				!previous.Association.Association.Target.Equals(current.Association.Association.Target) ||
				previous.Association.State != current.Association.State ||
				previous.Association.LocalRestorePending != current.Association.LocalRestorePending ||
				previous.HasRetainedManifest != current.HasRetainedManifest ||
				!StringComparer.Ordinal.Equals(previous.RetainedSourceIssue, current.RetainedSourceIssue) ||
				previous.Members.Count != current.Members.Count ||
				previous.Customization.UserOverrides.Count != current.Customization.UserOverrides.Count ||
				previous.Customization.DriftObservations.Count != current.Customization.DriftObservations.Count)
				return false;
			return previous.Members.All(x => current.Members.Any(y => x.MemberKey.Equals(y.MemberKey) &&
				x.NativeMod.Equals(y.NativeMod) && x.Binding.VerifiedRecipe.Equals(y.Binding.VerifiedRecipe) &&
				x.Binding.BindingKind == y.Binding.BindingKind && x.CollectionAssociationCount == y.CollectionAssociationCount &&
				x.Provenance.StandaloneUse == y.Provenance.StandaloneUse)) &&
				previous.Customization.UserOverrides.All(x => current.Customization.UserOverrides.Any(y =>
					x.OverrideId == y.OverrideId && x.Requirement.Equals(y.Requirement) &&
					x.BaselineState.Equals(y.BaselineState) && x.UserChosenState.Equals(y.UserChosenState) &&
					StringComparer.Ordinal.Equals(x.Note, y.Note))) &&
				previous.Customization.DriftObservations.All(x => current.Customization.DriftObservations.Any(y =>
					x.ObservationId == y.ObservationId && x.Requirement.Equals(y.Requirement) &&
					x.ExpectedState.Equals(y.ExpectedState) && x.ObservedState.Equals(y.ObservedState) &&
					StringComparer.Ordinal.Equals(x.Detail, y.Detail)));
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
					"Viewing an installed Collection. Compare / Update lets you choose another revision already retained by NMM; a revision not listed must be opened/imported once before it can be selected here. Verify / Repair checks the exact installed revision without silently changing local decisions. Stop tracking keeps installed effects in place; Review removal is a separate safety review.");

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

				CollectionRevisionUpdateWorkflowResult pending = FindInterruptedRevisionUpdateForAssociation(presentation.Association);
				if (presentation.Association.PendingRevision != null)
				{
					string recordedRevision = FormatRevisionActionLabel(presentation.Association.Association.Revision);
					string targetRevision = FormatRevisionActionLabel(presentation.Association.PendingRevision);
					if (pending != null)
					{
						_operationSnapshot = pending.Operation;
						AppendRevisionUpdateStopDiagnostics(pending);
					}
					ApplyInstalledRevisionUpdateGuidance(pending, recordedRevision, targetRevision);
					if (pending != null && (pending.Status == CollectionRevisionUpdateWorkflowStatus.ExplicitReviewRequired ||
						pending.Status == CollectionRevisionUpdateWorkflowStatus.RecoveryRequired))
						ShowFirstBlockingIssue();
				}
				else
					_workflowStatusLabel.Text = L("Collections.Workflow.InstalledView",
						"Workflow: Viewing your installed Collection. No changes are being made.");
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

		private void ApplyInstalledRevisionUpdateGuidance(CollectionRevisionUpdateWorkflowResult pending,
			string recordedRevision, string targetRevision)
		{
			if (pending == null)
			{
				_instructionLabel.Text = LanguageManager.Format("Collections.Update.InstalledPendingInstructions",
					"Revision change in progress: {0} -> {1}. {0} remains the recorded revision until the transition completes and the final setup is verified. Use the highlighted Continue action.",
					recordedRevision, targetRevision);
				_workflowStatusLabel.Text = LanguageManager.Format("Collections.Update.InstalledPendingStatus",
					"Workflow: revision change {0} -> {1} is incomplete. Continue it to reach the next real user decision.",
					recordedRevision, targetRevision);
				return;
			}

			switch (pending.Status)
			{
				case CollectionRevisionUpdateWorkflowStatus.ReadyForReview:
					_instructionLabel.Text = LanguageManager.Format("Collections.Update.InstalledPendingReadyInstructions",
						"Revision change ready for review: {0} -> {1}. The old revision remains recorded until you approve the reviewed remaining changes and final verification succeeds.",
						recordedRevision, targetRevision);
					_workflowStatusLabel.Text = LanguageManager.Format("Collections.Update.InstalledPendingReadyStatus",
						"Workflow: {0} -> {1} is waiting for your approval. Use the highlighted Review and approve action.", recordedRevision, targetRevision);
					break;
				case CollectionRevisionUpdateWorkflowStatus.AwaitingInput:
					_instructionLabel.Text = LanguageManager.Format("Collections.Update.InstalledPendingInputInstructions",
						"Revision change waiting for required files: {0} -> {1}. The highlighted Continue action checks the retained/downloaded files and proceeds automatically when they are ready.",
						recordedRevision, targetRevision);
					_workflowStatusLabel.Text = LanguageManager.Format("Collections.Update.InstalledPendingInputStatus",
						"Workflow: {0} -> {1} is waiting for required input. Continue once the requested files are available.", recordedRevision, targetRevision);
					break;
				case CollectionRevisionUpdateWorkflowStatus.ExplicitReviewRequired:
					_instructionLabel.Text = LanguageManager.Format("Collections.Update.InstalledPendingBlockedInstructions",
						"Revision change blocked: {0} -> {1}. Review the highlighted blocking issues below. Re-running the continuation does not resolve these findings unless the reported condition has actually changed.",
						recordedRevision, targetRevision);
					_workflowStatusLabel.Text = LanguageManager.Format("Collections.Update.InstalledPendingBlockedStatus",
						"Workflow: {0} -> {1} is blocked by {2} issue(s). Review the blocking issues before rechecking.", recordedRevision, targetRevision, Math.Max(1, _reviewErrorCount));
					break;
				case CollectionRevisionUpdateWorkflowStatus.RecoveryRequired:
					if (CanRecheckReportedSuccessfulRevisionChild(pending.Operation))
					{
						_instructionLabel.Text = GetRevisionUpdateInstructions(pending.Status, pending.Operation.Revision, pending.Operation);
						_workflowStatusLabel.Text = LanguageManager.Format("Collections.Workflow.UserStatus", "Workflow: {0}", _instructionLabel.Text);
						break;
					}
					_instructionLabel.Text = LanguageManager.Format("Collections.Update.InstalledPendingRecoveryInstructions",
						"Revision change requires recovery review: {0} -> {1}. NMM will not replay ambiguous native work. Review the reported recovery issue and Technical Report before continuing.",
						recordedRevision, targetRevision);
					_workflowStatusLabel.Text = LanguageManager.Format("Collections.Update.InstalledPendingRecoveryStatus",
						"Workflow: {0} -> {1} requires recovery review before more native changes can run.", recordedRevision, targetRevision);
					break;
				case CollectionRevisionUpdateWorkflowStatus.PausedAtSafeBoundary:
					_instructionLabel.Text = LanguageManager.Format("Collections.Update.InstalledPendingPausedInstructions",
						"Revision change paused safely: {0} -> {1}. Continue once to revalidate completed work and proceed automatically to the next user decision.",
						recordedRevision, targetRevision);
					_workflowStatusLabel.Text = LanguageManager.Format("Collections.Update.InstalledPendingPausedStatus",
						"Workflow: {0} -> {1} is paused at a safe boundary and can be continued.", recordedRevision, targetRevision);
					break;
				default:
					_instructionLabel.Text = LanguageManager.Format("Collections.Update.InstalledPendingInstructions",
						"Revision change in progress: {0} -> {1}. {0} remains the recorded revision until the transition completes and the final setup is verified. Use the highlighted Continue action.",
						recordedRevision, targetRevision);
					_workflowStatusLabel.Text = LanguageManager.Format("Collections.Update.InstalledPendingStatus",
						"Workflow: revision change {0} -> {1} is incomplete. Continue it to reach the next real user decision.", recordedRevision, targetRevision);
					break;
			}
		}

		private void ApplyManagedAssociationMemberState(CollectionManagementAssociationPresentation presentation)
		{
			if (presentation == null || presentation.RetainedManifest == null)
				return;
			Dictionary<CollectionMemberKey, CollectionManagementMemberPresentation> managedMembers = presentation.Members
				.ToDictionary(x => x.MemberKey, x => x);
			bool pendingRevision = presentation.Association.PendingRevision != null;
			bool applied = !pendingRevision && presentation.Association.State == CollectionAssociationState.Applied;
			CollectionRevisionUpdateWorkflowResult pending = FindInterruptedRevisionUpdateForAssociation(presentation.Association);
			HashSet<CollectionMemberKey> removed = new HashSet<CollectionMemberKey>(pending == null
				? Enumerable.Empty<CollectionMemberKey>() : pending.Operation.NativeChildren.Where(x =>
					x.Action == CollectionNativeChildAction.Deactivate && x.IsReconciled && x.HasVerifiedCommittedNativeState)
					.Select(x => x.Member.MemberKey));
			_suppressMemberCheckEvents = true;
			try
			{
				foreach (MemberGridRow row in _memberRows)
				{
					NormalizedCollectionMember member = row.Member;
					if (member == null || !member.IdentityResolution.IsResolved)
						continue;
					CollectionManagementMemberPresentation managedMember;
					bool bound = managedMembers.TryGetValue(member.IdentityResolution.Key, out managedMember);
					row.Selected = bound;
					if (applied && bound)
						row.Compatibility = managedMember.HasDetectedDrift
							? L("Collections.Status.ActionRequired", "Action required")
							: L("Collections.Status.Supported", "Supported");
					row.ManagedState = removed.Contains(member.IdentityResolution.Key)
						? L("Collections.Update.MemberRemovedPending", "Removed during unfinished revision change")
						: pendingRevision && bound ? L("Collections.Update.MemberPreviousBinding", "Previous revision binding; current state not verified")
						: bound ? FormatManagedMemberState(managedMember)
						: L("Collections.Member.ManagedState.NotBound", "Not part of installed recipe");
				}
				_membersView.RefreshData();
			}
			finally
			{
				_suppressMemberCheckEvents = false;
			}
		}

		private static string FormatManagedMemberState(CollectionManagementMemberPresentation member)
		{
			if (member == null)
				return L("Collections.Value.Unknown", "Unknown");

			bool driftAlreadyDescribed;
			string currentState = FormatManagedMemberCurrentState(member, out driftAlreadyDescribed);
			var parts = new List<string> { currentState };
			if (member.IsSharedAcrossCollections)
				parts.Add(LanguageManager.Format("Collections.Member.ManagedState.Shared", "shared by {0} Collections", member.CollectionAssociationCount));
			switch (member.Provenance.StandaloneUse)
			{
				case StandaloneModUse.ExplicitStandaloneUse:
					parts.Add(L("Collections.Member.ManagedState.Standalone", "also used standalone"));
					break;
				case StandaloneModUse.Unknown:
					parts.Add(L("Collections.Member.ManagedState.ProvenanceUnknown", "standalone use unknown"));
					break;
			}
			if (member.HasExplicitLocalDecision)
				parts.Add(L("Collections.Member.ManagedState.Override", "local override"));
			if (member.HasDetectedDrift && !driftAlreadyDescribed)
				parts.Add(L("Collections.Member.ManagedState.Modified", "modified from Collection state"));
			return String.Join(", ", parts);
		}

		private static string FormatManagedMemberCurrentState(CollectionManagementMemberPresentation member, out bool driftDescribed)
		{
			driftDescribed = false;
			CollectionDriftObservation participation = member.DriftObservations.FirstOrDefault(x =>
				x.Requirement.Aspect == CollectionRequirementAspect.MemberParticipation);
			if (participation != null && participation.ObservedState.Kind == CollectionRequirementStateKind.Absent)
			{
				driftDescribed = true;
				return L("Collections.Member.ManagedState.Missing", "Missing - expected by Collection");
			}

			CollectionDriftObservation enabledState = member.DriftObservations.FirstOrDefault(x =>
				x.Requirement.Aspect == CollectionRequirementAspect.MemberEnabledState);
			bool observedEnabled;
			bool expectedEnabled;
			if (enabledState != null &&
				CollectionMemberRequirementStates.TryGetEnabled(enabledState.ObservedState, out observedEnabled) &&
				CollectionMemberRequirementStates.TryGetEnabled(enabledState.ExpectedState, out expectedEnabled) &&
				observedEnabled != expectedEnabled)
			{
				driftDescribed = true;
				return observedEnabled
					? L("Collections.Member.ManagedState.UnexpectedEnabled", "Installed and enabled - Collection expects disabled")
					: L("Collections.Member.ManagedState.UnexpectedDisabled", "Installed but disabled - Collection expects enabled");
			}

			if (member.HasDetectedDrift)
			{
				driftDescribed = true;
				return L("Collections.Member.ManagedState.Modified", "Installed - modified from Collection state");
			}

			return member.Binding.BindingKind == CollectionMemberBindingKind.AdoptedExisting
				? L("Collections.Member.ManagedState.Adopted", "Installed - adopted existing mod")
				: L("Collections.Member.ManagedState.Installed", "Installed by Collection");
		}

		/// <summary>
		/// Replaces raw pre-install Nexus source-policy warnings with the durable outcome proven by an Applied association.
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

				CollectionCapabilityIssue policyIssue = memberReport.Issues.FirstOrDefault(CollectionNexusSourcePolicyResolver.IsResolvableIssue);
				if (policyIssue == null)
					continue;

				string policy = StringComparer.Ordinal.Equals(policyIssue.Code, CollectionNexusSourcePolicyResolver.PreferIssueCode)
					? "prefer"
					: "latest";
				AddReviewItem(CollectionReviewSeverity.Info, CollectionReviewItemKind.Progress, L("Collections.Status.Supported", "Ready"), "member.source-policy-resolved",
					policyIssue.FieldPath ?? string.Empty,
					LanguageManager.Format("Collections.SourcePolicy.AppliedResolution",
						"The Collection's '{0}' Nexus file policy was resolved to one concrete Nexus file during preparation; the applied member was verified against the durable reviewed plan.", policy),
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
			CollectionOperation pending = FindInterruptedInstallation(presentation.Association.Association.Revision);
			if (pending != null)
			{
				CollectionAdditiveWorkflowRecoveryResult recovery = _recoveryResults.FirstOrDefault(x => x.Operation.Identity.Equals(pending.Identity));
				CollectionNativeChildOperation failed = pending.NativeChildren.LastOrDefault(x => x.NativeResult != null &&
					x.NativeResult.ReportedStatus != ModOperationReportedStatus.Succeeded);
				CollectionUserMessagePresentation message = CollectionUserMessagePresenter.ForRecovery(
					pending.RequiresRecovery ? CollectionAdditiveWorkflowRecoveryStatus.RecoveryRequired : CollectionAdditiveWorkflowRecoveryStatus.ReadyToResume,
					CombineTechnicalDetail(recovery == null ? null : recovery.Message, failed == null ? null : failed.NativeResult.Message));
				AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic,
					L("Collections.Status.ActionRequired", "Action required"), "association.interrupted-installation",
					failed == null ? GetCurrentCollectionSubject() : FormatMemberSubject(failed.Member.MemberKey), message);
			}
			else
				AddReviewItem(CollectionReviewPresentationClassifier.ForAssociation(presentation.Association.State),
					presentation.Association.State == CollectionAssociationState.Applied ? CollectionReviewItemKind.Progress : CollectionReviewItemKind.Diagnostic,
					status, "association." + presentation.Association.State.ToString().ToLowerInvariant(),
					presentation.Association.Association.Revision.ToString(), FormatManagedAssociationState(presentation.Association.State), null,
					presentation.Association.State == CollectionAssociationState.Incomplete
						? L("Collections.Management.VerifyCurrentState", "Use Verify / Repair to check the current setup against this installed Collection revision.") : null);
			if (!String.IsNullOrWhiteSpace(presentation.RetainedSourceIssue))
			{
				AddPresentedReviewItem(CollectionReviewSeverity.Warning, CollectionReviewItemKind.Diagnostic, L("Collections.Status.ActionRequired", "Action required"),
					"association.retained-source", presentation.Association.Association.Revision.ToString(),
					CollectionUserMessagePresenter.ForRetainedSourceIssue(presentation.RetainedSourceIssue));
			}
			AppendManagedCustomizationIssues(presentation);
			UpdateIssuesHeader();
		}

		private void AppendManagedCustomizationIssues(CollectionManagementAssociationPresentation presentation)
		{
			if (presentation == null || presentation.Customization == null)
				return;
			foreach (UserOverride userOverride in presentation.Customization.UserOverrides)
			{
				string subject = FormatRequirementSubject(userOverride.Requirement);
				string message = String.IsNullOrWhiteSpace(userOverride.Note)
					? LanguageManager.Format("Collections.Management.OverrideMessage", "A deliberate local override is recorded for {0}. NMM will preserve this decision until you explicitly change it.", subject)
					: LanguageManager.Format("Collections.Management.OverrideMessageWithNote", "A deliberate local override is recorded for {0}: {1}", subject, userOverride.Note);
				AddReviewItem(CollectionReviewSeverity.Info, CollectionReviewItemKind.Progress,
					L("Collections.Management.LocalOverride", "Local override"), "association.override." + userOverride.OverrideId.ToString("N"),
					subject, message, userOverride.Requirement.MemberKey, null,
					CombineTechnicalDetail("Baseline: " + userOverride.BaselineState, "Chosen: " + userOverride.UserChosenState));
			}
			foreach (CollectionDriftObservation drift in presentation.Customization.DriftObservations)
			{
				string subject = FormatRequirementSubject(drift.Requirement);
				AddReviewItem(CollectionReviewSeverity.Warning, CollectionReviewItemKind.Diagnostic,
					L("Collections.Status.ActionRequired", "Action required"), "association.drift." + drift.ObservationId.ToString("N"), subject,
					LanguageManager.Format("Collections.Management.DriftMessage", "The installed state differs from the expected Collection state for {0}. The difference is tracked and will not be silently repaired.", subject),
					drift.Requirement.MemberKey, null, CombineTechnicalDetail("Expected: " + drift.ExpectedState, "Observed: " + drift.ObservedState));
			}
		}

		private static string FormatRequirementSubject(CollectionRequirementReference requirement)
		{
			if (requirement == null)
				return L("Collections.Value.Unknown", "Unknown");
			string area = requirement.Aspect.ToString();
			if (requirement.MemberKey != null)
				return area + " / " + requirement.MemberKey;
			if (!String.IsNullOrWhiteSpace(requirement.SubjectKey))
				return area + " / " + requirement.SubjectKey;
			return area;
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
			_memberRows.Clear();
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
			string slug = presentation.NexusCollectionSlug;
			if (!String.IsNullOrWhiteSpace(slug))
				return String.IsNullOrWhiteSpace(domain) ? slug : domain + " / " + slug;
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
					return L("Collections.Management.State.IncompleteVerify", "This installed Collection has not been fully verified against its retained revision. Use Verify / Repair to assess the current setup and any remaining requirements.");
				case CollectionAssociationState.Recovering:
					return L("Collections.Management.State.Recovering", "This installed Collection needs recovery before NMM can safely make more managed changes.");
				default:
					return L("Collections.Management.State.Unknown", "NMM cannot determine the current installed Collection state from the available information.");
			}
		}

		private void CloneManagedAssociationButton_Click(object sender, EventArgs e)
		{
			if (_managementWorkflow == null || _workflowBusy)
				return;
			CollectionManagementAssociation selected = _managedAssociationCombo.SelectedItem as CollectionManagementAssociation;
			if (selected == null)
				return;

			string defaultName = selected.DisplayName + " (Local copy)";
			PromptDialog nameDialog = PromptDialog.ShowDialog(null, this,
				L("Collections.WorkingCopy.NamePrompt", "Name for the Local working copy:"),
				L("Collections.Actions.CloneLocalWorkingCopy", "Clone to Local working copy..."),
				defaultName, null, null);
			if (nameDialog == null || String.IsNullOrWhiteSpace(nameDialog.EnteredText))
				return;

			try
			{
				CollectionManagementLocalWorkingCopy clone = _managementWorkflow
					.CloneAssociationToLocalWorkingCopy(selected.AssociationId, nameDialog.EnteredText);
				RefreshLocalWorkingCopies(clone.Collection);
				_workflowStatusLabel.Text = LanguageManager.Format("Collections.WorkingCopy.CreatedStatus",
					"Workflow: Local working copy '{0}' created from {1}.", clone.DisplayName, clone.SourceDisplayName);
				XtraMessageBox.Show(this, LanguageManager.Format("Collections.WorkingCopy.CreatedMessage",
					"Created Local working copy '{0}'. The installed Collection and its native mods were not changed.", clone.DisplayName),
					L("Collections.WorkingCopy.CreatedTitle", "Local working copy created"), MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection local working-copy clone failed: " + ex);
				XtraMessageBox.Show(this, ex.Message, L("Collections.WorkingCopy.FailedTitle", "Local working-copy clone failed"),
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void EditLocalWorkingCopyButton_Click(object sender, EventArgs e)
		{
			if (_managementWorkflow == null || _workflowBusy) return;
			CollectionManagementLocalWorkingCopy selected = _localWorkingCopyCombo.SelectedItem as CollectionManagementLocalWorkingCopy;
			if (selected == null) return;
			try
			{
				CollectionLocalWorkingCopyEditSnapshot snapshot = _managementWorkflow.GetLocalWorkingCopyEditSnapshot(selected.Collection);
				using (var dialog = new CollectionLocalWorkingCopyEditorDialog(snapshot))
				{
					if (dialog.ShowDialog(this) != DialogResult.OK) return;
					CollectionLocalWorkingCopyEditSnapshot updated = _managementWorkflow.SaveLocalWorkingCopyDraft(snapshot,
						dialog.DisplayName, dialog.Summary, dialog.GetDecisions());
					RefreshLocalWorkingCopies(updated.Record.Collection);
					_workflowStatusLabel.Text = LanguageManager.Format("Collections.WorkingCopy.EditedStatus",
						"Workflow: Local working copy '{0}' updated. Installed mods were not changed.", updated.Definition.DisplayName);
				}
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection Local working-copy edit failed: " + ex);
				XtraMessageBox.Show(this, ex.Message, L("Collections.WorkingCopy.EditFailedTitle", "Local working-copy edit failed"),
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void SaveLocalWorkingCopyRevisionButton_Click(object sender, EventArgs e)
		{
			if (_managementWorkflow == null || _workflowBusy) return;
			CollectionManagementLocalWorkingCopy selected = _localWorkingCopyCombo.SelectedItem as CollectionManagementLocalWorkingCopy;
			if (selected == null) return;
			PromptDialog labelDialog = PromptDialog.ShowDialog(null, this,
				L("Collections.WorkingCopy.RevisionLabelPrompt", "Label for the immutable Local revision:"),
				L("Collections.Actions.SaveLocalWorkingCopyRevision", "Save Local revision..."),
				"Local revision " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture), null, null);
			if (labelDialog == null || String.IsNullOrWhiteSpace(labelDialog.EnteredText)) return;
			try
			{
				CollectionRevision revision = _managementWorkflow.SaveLocalWorkingCopyRevision(selected.Collection, labelDialog.EnteredText, null);
				RefreshLocalWorkingCopies(selected.Collection);
				_workflowStatusLabel.Text = LanguageManager.Format("Collections.WorkingCopy.RevisionSavedStatus",
					"Workflow: immutable Local revision '{0}' saved from '{1}'.", revision.RevisionLabel, selected.DisplayName);
				XtraMessageBox.Show(this, LanguageManager.Format("Collections.WorkingCopy.RevisionSavedMessage",
					"Saved Local revision '{0}'. This did not change the installed game setup.", revision.RevisionLabel),
					L("Collections.WorkingCopy.RevisionSavedTitle", "Local revision saved"), MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection Local working-copy revision save failed: " + ex);
				XtraMessageBox.Show(this, ex.Message, L("Collections.WorkingCopy.RevisionSaveFailedTitle", "Local revision save failed"),
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void ShowManagedMemberImpactButton_Click(object sender, EventArgs e)
		{
			CollectionManagementAssociation association = _managedAssociationCombo.SelectedItem as CollectionManagementAssociation;
			CollectionManagementMemberPresentation member = GetSelectedManagedMemberPresentation();
			if (_managementWorkflow == null || association == null || member == null || _workflowBusy) return;
			try
			{
				CollectionManagementMemberImpact impact = _managementWorkflow.GetMemberImpact(association.AssociationId, member.MemberKey);
				var lines = new List<string>
				{
					"Native mod: " + impact.SelectedBinding.NativeMod.NativeModKey,
					"Standalone provenance: " + impact.Provenance.StandaloneUse,
					"Collection associations using this native mod: " + impact.CollectionAssociationCount.ToString(CultureInfo.CurrentCulture),
					"Automatic native removal protected: " + (impact.StandaloneUseProtectsFromAutomaticRemoval || impact.IsSharedAcrossCollections ? "yes" : "no"),
					"Explicit local decisions: " + (impact.HasAnyOverride ? "yes" : "no"),
					"Detected drift: " + (impact.HasAnyDrift ? "yes" : "no"),
					String.Empty,
					"Pins:"
				};
				foreach (CollectionMemberPinImpact pin in impact.Pins)
					lines.Add("- " + pin.Revision + " / " + pin.Binding.MemberKey + " / recipe " + pin.VerifiedRecipe.Fingerprint);
				XtraMessageBox.Show(this, String.Join(Environment.NewLine, lines),
					L("Collections.Management.MemberImpactTitle", "Collection member impact"), MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection member impact view failed: " + ex);
				XtraMessageBox.Show(this, ex.Message, L("Collections.Management.MemberImpactFailed", "Unable to inspect member impact"),
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void ShowManagedMemberButton_Click(object sender, EventArgs e)
		{
			CollectionManagementAssociation selectedAssociation = _managedAssociationCombo.SelectedItem as CollectionManagementAssociation;
			CollectionManagementMemberPresentation member = GetSelectedManagedMemberPresentation();
			CollectionUiContext context = _managedAssociationActionContext;
			if (_managementWorkflow == null || selectedAssociation == null || member == null || _workflowBusy ||
				context == null || context.AssociationId != selectedAssociation.AssociationId)
				return;

			try
			{
				IMod mod = _managementWorkflow.ResolveManagedMemberMod(selectedAssociation.AssociationId, member.MemberKey);
				ManagedModRequested(this, new CollectionManagedModRequestEventArgs(mod));
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection member Mods navigation failed: " + ex);
				RememberTechnicalFailure("association.member-show-mod-failed", ex, context);
				XtraMessageBox.Show(this, BuildUserDialogMessage(CollectionUserMessagePresenter.ForFailure(
					"association.member-show-mod-failed", ex.Message)),
					L("Collections.Management.ShowMemberFailed", "Unable to show Collection member"),
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void AddManagedOptionalMemberButton_Click(object sender, EventArgs e)
		{
			CollectionManagementAssociation selectedAssociation = _managedAssociationCombo.SelectedItem as CollectionManagementAssociation;
			CollectionManagementAssociationPresentation presentation = _managedAssociationPresentation;
			NormalizedCollectionMember member = GetSelectedNormalizedMember();
			CollectionUiContext context = _managedAssociationActionContext;
			if (_workflow == null || selectedAssociation == null || presentation == null || member == null || _workflowBusy ||
				context == null || context.AssociationId != selectedAssociation.AssociationId ||
				selectedAssociation.State != CollectionAssociationState.Applied || !presentation.HasRetainedManifest ||
				member.Requirement != CollectionMemberRequirement.Optional || !member.IdentityResolution.IsResolved ||
				presentation.BoundMemberKeys.Contains(member.IdentityResolution.Key))
				return;

			string memberName = member.DisplayName ?? member.IdentityResolution.Key.ToString();
			string confirmation = LanguageManager.Format("Collections.Management.AddOptionalMemberPrompt",
				"Add optional member '{0}' to this installed Collection?\r\n\r\nNMM will keep every currently bound member selected, add this member to the same revision, prepare the normal additive safety review, and will not remove unrelated mods.",
				memberName);
			if (XtraMessageBox.Show(this, confirmation, L("Collections.Actions.AddOptionalMember", "Add selected optional member..."),
				MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes ||
				!IsActionContextCurrent(context))
				return;

			NexusCollectionPreviewSnapshot snapshot = _snapshot;
			if (snapshot == null || !snapshot.HasManifestPreview || snapshot.Revision == null ||
				!snapshot.Revision.Identity.Equals(selectedAssociation.Association.Revision))
				throw new InvalidOperationException("The installed Collection member view no longer has the exact retained revision required for member expansion.");

			IReadOnlyList<CollectionOptionalMemberSelection> expansionSelections = BuildManagedMemberExpansionSelections(
				snapshot.CapabilityReport.Manifest, presentation.BoundMemberKeys, member.IdentityResolution.Key);
			CancelPreviewWork(false);
			CancelWorkflowWork();
			int generation = ++_previewGeneration;
			ResetWorkflowViewState();
			foreach (CollectionOptionalMemberSelection selection in expansionSelections)
				_optionalMemberSelectionState[selection.MemberKey] = selection.Selection;

			_managedMemberExpansionAssociationId = selectedAssociation.AssociationId;
			_managedAssociationPresentation = null;
			_displayContext = CollectionUiContext.Incoming(generation, snapshot.Revision.Identity, null);
			_snapshot = snapshot;
			RenderSnapshot(snapshot);
			_instructionLabel.Text = LanguageManager.Format("Collections.Management.AddOptionalMemberInstructions",
				"Adding optional member '{0}' to the installed Collection. Review the prepared additive changes before installation; existing bound members remain selected.",
				memberName);
			_workflowStatusLabel.Text = LanguageManager.Format("Collections.Management.AddOptionalMemberPreparing",
				"Workflow: preparing '{0}' as an addition to the installed Collection...", memberName);
			UpdateActionButtons();
			DownloadPrepareButton_Click(_addManagedOptionalMemberButton, EventArgs.Empty);
		}


		private async void RemoveManagedOptionalMemberButton_Click(object sender, EventArgs e)
		{
			CollectionManagementAssociation selectedAssociation = _managedAssociationCombo.SelectedItem as CollectionManagementAssociation;
			CollectionManagementAssociationPresentation presentation = _managedAssociationPresentation;
			NormalizedCollectionMember normalizedMember = GetSelectedNormalizedMember();
			CollectionManagementMemberPresentation managedMember = GetSelectedManagedMemberPresentation();
			CollectionUiContext context = _managedAssociationActionContext;
			if (_managementWorkflow == null || selectedAssociation == null || presentation == null || normalizedMember == null ||
				managedMember == null || _workflowBusy || context == null || context.AssociationId != selectedAssociation.AssociationId ||
				!presentation.HasRetainedManifest || normalizedMember.Requirement != CollectionMemberRequirement.Optional ||
				!normalizedMember.IdentityResolution.IsResolved || !managedMember.MemberKey.Equals(normalizedMember.IdentityResolution.Key) ||
				selectedAssociation.State == CollectionAssociationState.Recovering || selectedAssociation.State == CollectionAssociationState.Incomplete)
				return;

			string memberName = normalizedMember.DisplayName ?? normalizedMember.IdentityResolution.Key.ToString();
			CancellationToken token = BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Managing,
				LanguageManager.Format("Collections.Management.ReviewingOptionalMemberRemoval",
					"Reviewing safe removal of optional member '{0}'...", memberName));
			try
			{
				CollectionInstalledMemberRemovalPlan plan = await _managementWorkflow.PreviewOptionalMemberRemovalAsync(
					selectedAssociation.AssociationId, managedMember.MemberKey, token);
				if (!IsWorkflowContextCurrent(context, token))
					return;
				if (plan.HasBlockedImpact)
				{
					XtraMessageBox.Show(this, LanguageManager.Format("Collections.Management.OptionalMemberRemovalBlocked",
						"NMM cannot remove '{0}' safely from the installed Collection right now. Nothing was changed.\r\n\r\n{1}",
						memberName, CollectionUserMessagePresenter.SanitizeInternalTerminology(plan.Reason)),
						L("Collections.Management.OptionalMemberRemovalBlockedTitle", "Optional member removal blocked"),
						MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}

				string effect = plan.RequiresNativeMutation
					? L("Collections.Management.OptionalMemberRemovalNative", "The native mod is exclusive to this member and will be uninstalled through NMM's normal uninstaller.")
					: L("Collections.Management.OptionalMemberRemovalPreserved", "The native mod will remain installed; only this Collection member association will be removed.");
				string confirmation = LanguageManager.Format("Collections.Management.RemoveOptionalMemberPrompt",
					"Remove optional member '{0}' from this installed Collection?\r\n\r\n{1}\r\n\r\nReason: {2}\r\n\r\nThe Collection will be marked Modified because its installed member set will intentionally differ from the retained revision.",
					memberName, effect, CollectionUserMessagePresenter.SanitizeInternalTerminology(plan.Reason));
				if (XtraMessageBox.Show(this, confirmation, L("Collections.Actions.RemoveOptionalMember", "Remove selected optional member..."),
					MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes ||
					!IsWorkflowContextCurrent(context, token))
					return;

				_workflowStatusLabel.Text = LanguageManager.Format("Collections.Management.RemovingOptionalMember",
					"Removing optional member '{0}' from the installed Collection...", memberName);
				CollectionInstalledMemberRemovalResult result = await _managementWorkflow.RemoveOptionalMemberAsync(plan, token);
				if (!IsWorkflowContextCurrent(context, token))
					return;
				RefreshManagedAssociations(true);
				if (result.IsSuccessful)
				{
					_workflowStatusLabel.Text = LanguageManager.Format("Collections.Management.OptionalMemberRemoved",
						"Workflow: optional member '{0}' was removed from the installed Collection.", memberName);
					XtraMessageBox.Show(this, LanguageManager.Format("Collections.Management.OptionalMemberRemovedMessage",
						"Optional member '{0}' was removed from Collection tracking. {1}", memberName, effect),
						L("Collections.Management.OptionalMemberRemovedTitle", "Optional member removed"),
						MessageBoxButtons.OK, MessageBoxIcon.Information);
				}
				else
				{
					_workflowStatusLabel.Text = L("Collections.Management.OptionalMemberRemovalStopped",
						"Workflow: optional-member removal stopped before a verified complete result; recovery may be required.");
				}
			}
			catch (OperationCanceledException)
			{
				if (IsWorkflowOwnerCurrent(context))
					_workflowStatusLabel.Text = L("Collections.Management.OptionalMemberRemovalCancelled",
						"Workflow: optional-member removal cancellation requested; NMM will reconcile native state before further managed work.");
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection optional member removal failed: " + ex);
				RememberTechnicalFailure("association.member-remove-failed", ex, context);
				XtraMessageBox.Show(this, BuildUserDialogMessage(CollectionUserMessagePresenter.ForFailure(
					"association.member-remove-failed", ex.Message)),
					L("Collections.Management.OptionalMemberRemovalFailed", "Optional member removal failed"),
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally
			{
				EndWorkflowWork(context);
			}
		}

		private void IgnoreMemberDifferenceButton_Click(object sender, EventArgs e)
		{
			CollectionManagementAssociation selectedAssociation = _managedAssociationCombo.SelectedItem as CollectionManagementAssociation;
			CollectionManagementMemberPresentation member = GetSelectedManagedMemberPresentation();
			CollectionUiContext context = _managedAssociationActionContext;
			List<CollectionDriftObservation> memberStateDrift = member == null ? new List<CollectionDriftObservation>() : member.DriftObservations
				.Where(x => CollectionMemberRequirementStates.IsIgnorableMemberDifference(x.Requirement.Aspect)).ToList();
			if (_managementWorkflow == null || selectedAssociation == null || member == null || memberStateDrift.Count == 0 ||
				_workflowBusy || context == null || context.AssociationId != selectedAssociation.AssociationId)
				return;

			CollectionDriftObservation drift = ChooseManagedCustomization(
				L("Collections.Management.ChooseMemberDifferenceTitle", "Choose member difference"),
				L("Collections.Management.ChooseMemberDifferencePrompt", "Choose the missing/disabled member difference that NMM should treat as an intentional local deviation."),
				memberStateDrift, FormatDriftChoice);
			if (drift == null)
				return;

			string memberName = GetMemberDisplayName(member.MemberKey) ?? member.MemberKey.ToString();
			string confirmation = LanguageManager.Format("Collections.Management.IgnoreMemberDifferencePrompt",
				"Ignore the current {0} difference for '{1}'?\r\n\r\nNMM will preserve this as an explicit local decision instead of treating the current missing/disabled state as unresolved drift. No native mod state is changed.",
				FormatRequirementAspect(drift.Requirement.Aspect), memberName);
			if (XtraMessageBox.Show(this, confirmation, L("Collections.Actions.IgnoreMemberDifference", "Ignore selected member difference..."),
				MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes ||
				!IsActionContextCurrent(context))
				return;

			BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Managing,
				L("Collections.Management.IgnoringMemberDifference", "Recording the selected member difference as an intentional local decision..."));
			try
			{
				_managementWorkflow.IgnoreMemberDifference(selectedAssociation.AssociationId, member.MemberKey, drift.ObservationId);
				RefreshManagedAssociations();
				_workflowStatusLabel.Text = LanguageManager.Format("Collections.Management.MemberDifferenceIgnored",
					"Workflow: the current {0} difference for '{1}' is now an intentional local Collection decision.",
					FormatRequirementAspect(drift.Requirement.Aspect), memberName);
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection member ignore failed: " + ex);
				RememberTechnicalFailure("association.member-ignore-failed", ex, context);
				XtraMessageBox.Show(this, BuildUserDialogMessage(CollectionUserMessagePresenter.ForFailure(
					"association.member-ignore-failed", ex.Message)),
					L("Collections.Management.MemberDecisionFailed", "Collection member decision failed"),
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally
			{
				EndWorkflowWork(context);
			}
		}

		private void StopIgnoringMemberDifferenceButton_Click(object sender, EventArgs e)
		{
			CollectionManagementAssociation selectedAssociation = _managedAssociationCombo.SelectedItem as CollectionManagementAssociation;
			CollectionManagementMemberPresentation member = GetSelectedManagedMemberPresentation();
			CollectionUiContext context = _managedAssociationActionContext;
			List<UserOverride> ignored = member == null ? new List<UserOverride>() : member.UserOverrides
				.Where(x => CollectionMemberRequirementStates.IsIgnorableMemberDifference(x.Requirement.Aspect)).ToList();
			if (_managementWorkflow == null || selectedAssociation == null || member == null || ignored.Count == 0 ||
				_workflowBusy || context == null || context.AssociationId != selectedAssociation.AssociationId)
				return;

			UserOverride userOverride = ChooseManagedCustomization(
				L("Collections.Management.ChooseIgnoredMemberDifferenceTitle", "Choose ignored member difference"),
				L("Collections.Management.ChooseIgnoredMemberDifferencePrompt", "Choose the member difference that should return to the Collection baseline."),
				ignored, FormatOverrideChoice);
			if (userOverride == null)
				return;

			string memberName = GetMemberDisplayName(member.MemberKey) ?? member.MemberKey.ToString();
			string confirmation = LanguageManager.Format("Collections.Management.StopIgnoringMemberDifferencePrompt",
				"Stop ignoring the {0} difference for '{1}'?\r\n\r\nNMM will re-establish the Collection baseline as the expected state, using fresh native state to decide whether a difference still needs attention. It will not install, enable or remove anything automatically.",
				FormatRequirementAspect(userOverride.Requirement.Aspect), memberName);
			if (XtraMessageBox.Show(this, confirmation, L("Collections.Actions.StopIgnoringMemberDifference", "Stop ignoring selected member difference..."),
				MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes ||
				!IsActionContextCurrent(context))
				return;

			BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Managing,
				L("Collections.Management.StopIgnoringMemberDifference", "Restoring the Collection baseline expectation for the selected member..."));
			try
			{
				_managementWorkflow.StopIgnoringMemberDifference(selectedAssociation.AssociationId, member.MemberKey, userOverride.OverrideId);
				RefreshManagedAssociations();
				_workflowStatusLabel.Text = LanguageManager.Format("Collections.Management.MemberDifferenceNoLongerIgnored",
					"Workflow: '{0}' now follows the Collection baseline expectation again; any remaining native difference is shown as drift.", memberName);
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection member stop-ignore failed: " + ex);
				RememberTechnicalFailure("association.member-stop-ignore-failed", ex, context);
				XtraMessageBox.Show(this, BuildUserDialogMessage(CollectionUserMessagePresenter.ForFailure(
					"association.member-stop-ignore-failed", ex.Message)),
					L("Collections.Management.MemberDecisionFailed", "Collection member decision failed"),
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally
			{
				EndWorkflowWork(context);
			}
		}

		private void AcceptMemberDriftButton_Click(object sender, EventArgs e)
		{
			CollectionManagementAssociation selectedAssociation = _managedAssociationCombo.SelectedItem as CollectionManagementAssociation;
			CollectionManagementMemberPresentation member = GetSelectedManagedMemberPresentation();
			CollectionUiContext context = _managedAssociationActionContext;
			List<CollectionDriftObservation> generalDrift = member == null ? new List<CollectionDriftObservation>() : member.DriftObservations
				.Where(x => !CollectionMemberRequirementStates.IsIgnorableMemberDifference(x.Requirement.Aspect)).ToList();
			if (_managementWorkflow == null || selectedAssociation == null || member == null || generalDrift.Count == 0 ||
				_workflowBusy || context == null || context.AssociationId != selectedAssociation.AssociationId)
				return;

			CollectionDriftObservation drift = ChooseManagedCustomization(
				L("Collections.Management.ChooseDriftTitle", "Choose detected member change"),
				L("Collections.Management.ChooseDriftPrompt", "Choose the detected difference to adopt as an explicit local Collection decision."),
				generalDrift, FormatDriftChoice);
			if (drift == null)
				return;

			string memberName = GetMemberDisplayName(member.MemberKey) ?? member.MemberKey.ToString();
			string confirmation = LanguageManager.Format("Collections.Management.AcceptDriftPrompt",
				"Adopt the current {0} state for '{1}' as an explicit local Collection override?\r\n\r\nThis records your intent only. It does not change the installed mod, files, plugins or configuration. Future verify/update/repair work must preserve or explicitly revisit this local choice.",
				FormatRequirementAspect(drift.Requirement.Aspect), memberName);
			if (XtraMessageBox.Show(this, confirmation, L("Collections.Actions.AcceptMemberDrift", "Adopt selected member change..."),
				MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes ||
				!IsActionContextCurrent(context))
				return;

			BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Managing,
				L("Collections.Management.AcceptingDrift", "Recording the selected installed state as a local Collection decision..."));
			try
			{
				_managementWorkflow.AcceptMemberDrift(selectedAssociation.AssociationId, member.MemberKey, drift.ObservationId,
					"Adopted from the installed Collection management view.");
				RefreshManagedAssociations();
				_workflowStatusLabel.Text = LanguageManager.Format("Collections.Management.DriftAccepted",
					"Workflow: the current {0} state for '{1}' is now tracked as an explicit local Collection choice.",
					FormatRequirementAspect(drift.Requirement.Aspect), memberName);
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection member drift adoption failed: " + ex);
				RememberTechnicalFailure("association.member-drift-accept-failed", ex, context);
				XtraMessageBox.Show(this, BuildUserDialogMessage(CollectionUserMessagePresenter.ForFailure(
					"association.member-drift-accept-failed", ex.Message)),
					L("Collections.Management.MemberDecisionFailed", "Collection member decision failed"),
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally
			{
				EndWorkflowWork(context);
			}
		}

		private void ClearMemberOverrideButton_Click(object sender, EventArgs e)
		{
			CollectionManagementAssociation selectedAssociation = _managedAssociationCombo.SelectedItem as CollectionManagementAssociation;
			CollectionManagementMemberPresentation member = GetSelectedManagedMemberPresentation();
			CollectionUiContext context = _managedAssociationActionContext;
			List<UserOverride> generalOverrides = member == null ? new List<UserOverride>() : member.UserOverrides
				.Where(x => !CollectionMemberRequirementStates.IsIgnorableMemberDifference(x.Requirement.Aspect)).ToList();
			if (_managementWorkflow == null || selectedAssociation == null || member == null || generalOverrides.Count == 0 ||
				_workflowBusy || context == null || context.AssociationId != selectedAssociation.AssociationId)
				return;

			UserOverride userOverride = ChooseManagedCustomization(
				L("Collections.Management.ChooseOverrideTitle", "Choose local member override"),
				L("Collections.Management.ChooseOverridePrompt", "Choose the explicit local decision to stop preserving as an override."),
				generalOverrides, FormatOverrideChoice);
			if (userOverride == null)
				return;

			string memberName = GetMemberDisplayName(member.MemberKey) ?? member.MemberKey.ToString();
			string confirmation = LanguageManager.Format("Collections.Management.ClearOverridePrompt",
				"Clear the local {0} override for '{1}'?\r\n\r\nThe installed state will not be changed. If it still differs from the Collection baseline, NMM will immediately keep that difference as detected drift requiring review.",
				FormatRequirementAspect(userOverride.Requirement.Aspect), memberName);
			if (XtraMessageBox.Show(this, confirmation, L("Collections.Actions.ClearMemberOverride", "Clear selected member override..."),
				MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes ||
				!IsActionContextCurrent(context))
				return;

			BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Managing,
				L("Collections.Management.ClearingOverride", "Clearing the selected local Collection override without changing installed content..."));
			try
			{
				_managementWorkflow.ClearMemberOverride(selectedAssociation.AssociationId, member.MemberKey, userOverride.OverrideId);
				RefreshManagedAssociations();
				_workflowStatusLabel.Text = LanguageManager.Format("Collections.Management.OverrideCleared",
					"Workflow: the local {0} override for '{1}' was cleared; installed content was not changed.",
					FormatRequirementAspect(userOverride.Requirement.Aspect), memberName);
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection member override clear failed: " + ex);
				RememberTechnicalFailure("association.member-override-clear-failed", ex, context);
				XtraMessageBox.Show(this, BuildUserDialogMessage(CollectionUserMessagePresenter.ForFailure(
					"association.member-override-clear-failed", ex.Message)),
					L("Collections.Management.MemberDecisionFailed", "Collection member decision failed"),
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally
			{
				EndWorkflowWork(context);
			}
		}


		private async void CompareUpdateButton_Click(object sender, EventArgs e)
		{
			if (_workflowBusy || _revisionUpdateWorkflow == null || _workflow == null)
				return;
			bool installedAssociationAction = _managedAssociationPresentation != null;
			bool installedPendingAction = installedAssociationAction &&
				FindInterruptedRevisionUpdateForAssociation(_managedAssociationPresentation.Association) != null;
			if (!installedAssociationAction && !installedPendingAction && (_snapshot == null || !_snapshot.HasConcreteRevision))
				return;

			if (_managedAssociationPresentation != null)
			{
				CollectionRevisionUpdateWorkflowResult pending = FindInterruptedRevisionUpdateForAssociation(_managedAssociationPresentation.Association);
				try
				{
					if (pending != null)
					{
						// The installed view's primary revision action is continuation, not navigation. Load the retained
						// candidate and fall through into the same durable ResumeAsync path in this single click.
						ShowInterruptedRevisionUpdate(pending);
					}
					else
					{
						CollectionRevision selectedRevision = ChooseRetainedRevisionCandidate(_managedAssociationPresentation.Association);
						if (selectedRevision == null) return;
						ShowRetainedRevisionCandidate(selectedRevision);
						return;
					}
				}
				catch (Exception ex)
				{
					RememberTechnicalFailure("revision-update.preview-failed", ex);
					AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic,
						L("Collections.Status.ActionRequired", "Action required"), "revision-update.preview-failed",
						GetCurrentCollectionSubject(), CollectionUserMessagePresenter.FromRaw(ex.Message, GetRevisionUpdateBlockedNextAction()));
					UpdateIssuesHeader();
					return;
				}
			}

			CollectionUiContext context = _incomingActionContext;
			CollectionManagementAssociation sourceAssociation = FindRevisionUpdateSourceAssociation();
			CollectionRevisionUpdateWorkflowResult interrupted = _revisionUpdateResult ?? FindMatchingInterruptedRevisionUpdate();
			if (context == null || (sourceAssociation == null && interrupted == null) ||
				((interrupted == null || interrupted.IsCommitted) && !CanSupersedePreparationForRevisionChange(_operationSnapshot)))
				return;

			_blockedRevisionUpdatePlan = null;
			bool continuing = interrupted != null && !interrupted.IsCommitted;
			CancellationToken token = BeginWorkflowWork(context,
				continuing ? CollectionWorkflowActivityPhase.Recovering : CollectionWorkflowActivityPhase.Reviewing,
				continuing ? L("Collections.Update.ContinuingRevision", "Checking completed steps and continuing the approved revision change...")
					: L("Collections.Update.BuildingReview", "Building exact Collection revision comparison..."));
			try
			{
				if (interrupted != null && !interrupted.IsCommitted)
				{
					ClearRevisionUpdateContinuationItems(false);
					LoadInterruptedManifestForIncoming(interrupted);
					_revisionUpdateResult = interrupted;
					await ContinueInterruptedRevisionUpdateAsync(context, token, interrupted);
					return;
				}

				// A failed additive preparation has no place in a revision change. The coordinator rechecks
				// the durable native boundary before cancelling it; already-started native work remains protected.
				if (_operationSnapshot != null && !_operationSnapshot.IsTerminal &&
					_operationSnapshot.Kind == CollectionOperationKind.ApplyResolvedPlan)
					CancelSupersededPreparation();

				NexusCollectionPreviewSnapshot candidate = _snapshot;
				if (!candidate.HasManifestPreview)
				{
					_workflowStatusLabel.Text = L("Collections.Update.DownloadingCandidate", "Downloading and retaining the candidate Collection revision...");
					candidate = await _workflow.DownloadAndRetainBundleAsync(candidate, token);
					if (!IsWorkflowContextCurrent(context, token)) return;
					_snapshot = candidate;
					RenderSnapshot(candidate);
				}

				_revisionUpdateReview = await _revisionUpdateWorkflow.PrepareReviewAsync(sourceAssociation.AssociationId,
					candidate.Revision.Identity, BuildExplicitRevisionOptionalSelections(), token);
				if (!IsWorkflowContextCurrent(context, token)) return;
				_revisionUpdateResult = null;
				_revisionUpdatePreparation = null;
				RenderRevisionUpdateReview(_revisionUpdateReview);
				await ApproveRevisionUpdateReviewAsync(context, token, _revisionUpdateReview);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection revision update failed: " + ex);
				RememberTechnicalFailure("revision-update.failed", ex, context);
				if (IsWorkflowContextCurrent(context, token))
				{
					CollectionRevisionUpdateWorkflowResult pending = _revisionUpdateResult ?? interrupted;
					string nextAction = pending != null && !pending.IsCommitted
						? GetRevisionUpdateActionText(pending.Status, pending.Operation.Revision, pending.Operation)
						: L("Collections.Update.ReviewRevisionChangeButton", "Review revision change...");
					CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.FromRaw(ex.Message,
						LanguageManager.Format("Collections.Update.FailureTargetNextAction",
							"Use '{0}' for the next step. If this error repeats, export a Technical Report for this incomplete change.", nextAction));
					_contentValue.Text = L("Collections.Update.CouldNotContinue", "Revision change could not continue");
					_appliedValue.Text = _operationSnapshot != null && _operationSnapshot.HasCrossedNativeBoundary
						? L("Collections.Update.PartiallyChanged", "Partially changed - continuation required")
						: L("Collections.Update.NotFinished", "Revision change not finished");
					SetWorkflowPresentation(userMessage);
					AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic,
						L("Collections.Status.ActionRequired", "Action required"), "revision-update.failed",
						_snapshot.Revision == null ? String.Empty : _snapshot.Revision.Identity.ToString(), userMessage);
				}
			}
			finally
			{
				EndWorkflowWork(context);
			}
		}

		/// <summary>Reviews unknown-history ownership in one batch before rebuilding the revision comparison.</summary>
		private async void ReviewRevisionUpdateOwnership_Click(object sender, EventArgs e)
		{
			CollectionRevisionUpdatePlan plan = _blockedRevisionUpdatePlan;
			CollectionUiContext context = _incomingActionContext;
			if (_workflowBusy || _revisionUpdateWorkflow == null || plan == null || context == null ||
				context.Revision == null || !context.Revision.Equals(plan.NewPlan.Revision)) return;
			List<CollectionRevisionUpdateMemberPlan> members = plan.Members.Where(x => x.RequiresStandaloneUseConfirmation).ToList();
			if (members.Count == 0) return;
			IReadOnlyList<CollectionMemberKey> collectionOnlyMembers;
			IReadOnlyList<CollectionMemberKey> independentMembers;
			using (CollectionOwnershipReviewDialog dialog = new CollectionOwnershipReviewDialog(members))
			{
				if (dialog.ShowDialog(this) != DialogResult.OK || !IsActionContextCurrent(context)) return;
				collectionOnlyMembers = dialog.CollectionOnlyMembers;
				independentMembers = dialog.IndependentMembers;
			}

			bool confirmed = false;
			CancellationToken token = BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Managing,
				L("Collections.Update.RecordingOwnership", "Recording each mod's ownership decision..."));
			try
			{
				await _revisionUpdateWorkflow.ReviewStandaloneUseAsync(plan, collectionOnlyMembers, independentMembers, token);
				if (!IsWorkflowContextCurrent(context, token)) return;
				_blockedRevisionUpdatePlan = null;
				confirmed = true;
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection ownership confirmation failed: " + ex);
				RememberTechnicalFailure("revision-update.ownership-failed", ex, context);
				if (IsWorkflowContextCurrent(context, token))
				{
					_workflowStatusLabel.Text = L("Collections.Update.OwnershipConfirmationFailed",
						"Workflow: Collection ownership could not be confirmed. Review the error before continuing.");
					XtraMessageBox.Show(this, ex.Message, L("Collections.Update.ReviewOwnership", "Review Collection ownership..."),
						MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
			finally
			{
				EndWorkflowWork(context);
			}
			if (confirmed && IsActionContextCurrent(context)) CompareUpdateButton_Click(sender, EventArgs.Empty);
		}

		private async Task ApproveRevisionUpdateReviewAsync(CollectionUiContext context, CancellationToken token,
			CollectionRevisionUpdateWorkflowReview review)
		{
			if (review == null || !IsWorkflowContextCurrent(context, token)) return;
			CollectionRevisionUpdatePlan plan = review.UpdatePlan;
			if (plan.HasBlockingActionRequired)
			{
				_operationSnapshot = _revisionUpdateWorkflow.CancelBeforeApply(review);
				_revisionUpdateRecoveryResults = _revisionUpdateRecoveryResults.Where(x => !x.Operation.Identity.Equals(review.Operation.Identity)).ToList();
				_revisionUpdateReview = null;
				_revisionUpdateResult = null;
				_blockedRevisionUpdatePlan = plan;
				_contentValue.Text = L("Collections.Update.ContentBlocked", "Revision comparison blocked - review the issues");
				_appliedValue.Text = L("Collections.Update.NotAppliedBlocked", "Not applied - revision review blocked");
				_workflowStatusLabel.Text = plan.Members.Any(x => x.RequiresStandaloneUseConfirmation)
					? LanguageManager.Format("Collections.Update.OwnershipBlockedStatus",
						"Workflow: revision change blocked by unknown or protected standalone use: {0}. Choose Review Collection ownership... to confirm how these mods are used.",
						String.Join(", ", plan.Members.Where(x => x.RequiresStandaloneUseConfirmation).Select(FormatRevisionUpdateMemberSubject)))
					: L("Collections.Update.BlockedIssues", "Workflow: unresolved revision decisions need attention. Review the named errors below, then run the comparison again.");
				return;
			}

			int added = plan.Members.Count(x => x.ChangeKind == CollectionRevisionUpdateChangeKind.Added);
			int removed = plan.Members.Count(x => x.ChangeKind == CollectionRevisionUpdateChangeKind.Removed);
			int changed = plan.Members.Count(x => x.ChangeKind != CollectionRevisionUpdateChangeKind.Unchanged &&
				x.ChangeKind != CollectionRevisionUpdateChangeKind.Added && x.ChangeKind != CollectionRevisionUpdateChangeKind.Removed);
			int overrideCount = plan.UnscopedOverrides.Count + plan.Members.Sum(x => x.Overrides.Count);
			List<CollectionRevisionUpdateMemberPlan> preservedStandalone = plan.Members
				.Where(x => x.Disposition == CollectionRevisionUpdateDisposition.PreserveStandalone).ToList();
			if (!ConfirmRevisionUpdate(plan, added, removed, changed, overrideCount, preservedStandalone) ||
				!IsWorkflowContextCurrent(context, token))
			{
				_operationSnapshot = _revisionUpdateWorkflow.CancelBeforeApply(review);
				_revisionUpdateRecoveryResults = _revisionUpdateRecoveryResults.Where(x => !x.Operation.Identity.Equals(review.Operation.Identity)).ToList();
				_revisionUpdateReview = null;
				_revisionUpdateResult = null;
				_contentValue.Text = L("Collections.Update.ComparisonDeclined", "Revision comparison complete; update declined");
				_appliedValue.Text = L("Collections.Update.NotAppliedDeclined", "Not applied - update declined");
				_workflowStatusLabel.Text = L("Collections.Update.Cancelled", "Workflow: revision update cancelled before native mutation.");
				return;
			}

			SetWorkflowActivity(CollectionWorkflowActivityBuilder.Foreground(CollectionWorkflowActivityPhase.Applying,
				L("Collections.Update.Applying", "Applying the approved Collection revision update...")));
			_appliedValue.Text = L("Collections.Update.ApplyingState", "Revision change in progress");
			_contentValue.Text = L("Collections.Update.ApplyingContent", "Applying approved changes and verifying installed files...");
			UpdateActionButtons();
			CollectionArchiveOverwritePolicy overwritePolicy = _autoOverwriteArchivesCheckBox.Checked
				? CollectionArchiveOverwritePolicy.OverwriteExistingArchives
				: CollectionArchiveOverwritePolicy.Prompt;
			CollectionRevisionUpdateWorkflowResult result = await _revisionUpdateWorkflow.ApproveAndApplyAsync(review,
				overwritePolicy, ConfirmArchiveOverwrite, token);
			if (!IsWorkflowContextCurrent(context, token)) return;
			_revisionUpdateResult = result;
			_revisionUpdatePreparation = result.Preparation;
			RenderRevisionUpdateWorkflowResult(result);
		}

		private async Task ContinueInterruptedRevisionUpdateAsync(CollectionUiContext context, CancellationToken token,
			CollectionRevisionUpdateWorkflowResult interrupted)
		{
			CollectionRevisionUpdateWorkflowResult result = interrupted;
			if (result.Status == CollectionRevisionUpdateWorkflowStatus.ReadyForReview)
			{
				_revisionUpdateReview = _revisionUpdateWorkflow.LoadPendingReview(result.Operation.Identity);
				RenderRevisionUpdateReview(_revisionUpdateReview);
				await ApproveRevisionUpdateReviewAsync(context, token, _revisionUpdateReview);
				return;
			}

			if (result.Status == CollectionRevisionUpdateWorkflowStatus.AwaitingInput && _revisionUpdatePreparation != null && _revisionUpdateReview != null)
				result = await _revisionUpdateWorkflow.ProbePreparationAndContinueAsync(_revisionUpdatePreparation, _revisionUpdateReview, token);
			else if (_revisionUpdatePreparation != null &&
				_revisionUpdatePreparation.Operation.Identity.Equals(result.Operation.Identity))
				result = await _revisionUpdateWorkflow.ResumePreparedAsync(_revisionUpdatePreparation, token);
			else
				result = await _revisionUpdateWorkflow.ResumeAsync(result.Operation.Identity, token);
			if (!IsWorkflowContextCurrent(context, token)) return;

			if (result.Status == CollectionRevisionUpdateWorkflowStatus.ReadyForReview)
			{
				_revisionUpdateResult = result;
				_revisionUpdateReview = _revisionUpdateWorkflow.LoadPendingReview(result.Operation.Identity);
				RenderRevisionUpdateReview(_revisionUpdateReview);
				await ApproveRevisionUpdateReviewAsync(context, token, _revisionUpdateReview);
				return;
			}
			_revisionUpdateResult = result;
			_revisionUpdatePreparation = result.Preparation;
			RenderRevisionUpdateWorkflowResult(result);
		}

		/// <summary>Checks interrupted native work before repair or an explicitly requested uninstall, retaining completed installs.</summary>
		private async Task<bool> ReconcileInstallationBeforeManagementAsync(CollectionManagementAssociation selected,
			CollectionUiContext context, CancellationToken token, bool stopForRemoval)
		{
			if (_workflow == null) return true;
			IReadOnlyList<CollectionAdditiveWorkflowRecoveryResult> results = await _workflow.ReconcileIncompleteTargetAsync(token);
			if (!IsWorkflowContextCurrent(context, token)) return false;
			_recoveryResults = results.Where(x => !x.Operation.IsTerminal).ToList();
			CollectionAdditiveWorkflowRecoveryResult matching = results.FirstOrDefault(x =>
				selected.Association.Revision.Equals(x.Operation.Revision));
			if (matching != null) _operationSnapshot = matching.Operation;
			if (stopForRemoval && matching != null && !matching.Operation.IsTerminal && !matching.Operation.RequiresRecovery)
			{
				IReadOnlyList<CollectionOperation> stopped = await _workflow.StopReconciledInstallationForRemovalAsync(selected.Association.Revision, token);
				if (!IsWorkflowContextCurrent(context, token)) return false;
				foreach (CollectionOperation operation in stopped)
				{
					_recoveryResults = _recoveryResults.Where(x => !x.Operation.Identity.Equals(operation.Identity)).ToList();
					if (_operationSnapshot != null && _operationSnapshot.Identity.Equals(operation.Identity))
						_operationSnapshot = operation;
				}
			}
			RefreshManagedAssociations(true);
			if (!IsWorkflowContextCurrent(context, token)) return false;
			CollectionOperation pending = FindInterruptedInstallation(selected.Association.Revision);
			if (pending == null) return true;
			BindMatchingRecovery();
			string nextAction = pending.RequiresRecovery || pending.HasUnreconciledNativeChild
				? L("Collections.Actions.CheckRecoveryContinue", "Check recovery and continue...")
				: L("Collections.Actions.ResumeApply", "Review and continue...");
			if (pending.RequiresRecovery || pending.HasUnreconciledNativeChild)
				SetWorkflowPresentation(CollectionUserMessagePresenter.ForRecovery(CollectionAdditiveWorkflowRecoveryStatus.RecoveryRequired,
					matching == null ? null : matching.Message));
			else
				_workflowStatusLabel.Text = LanguageManager.Format("Collections.Management.CheckInterruptedInstallationAction",
					"Workflow: Installation is unfinished. Choose '{0}' to review the remaining installation changes, or choose Uninstall Collection to review removal.", nextAction);
			ShowFirstBlockingIssue();
			return false;
		}

		/// <summary>Finds the active installation owned by this revision, including when its partial association is selected.</summary>
		private CollectionOperation FindInterruptedInstallation(CollectionRevisionIdentity revision)
		{
			if (revision == null) return null;
			if (_operationSnapshot != null && !_operationSnapshot.IsTerminal &&
				_operationSnapshot.Kind == CollectionOperationKind.ApplyResolvedPlan && revision.Equals(_operationSnapshot.Revision))
				return _operationSnapshot;
			return _recoveryResults.Select(x => x.Operation).FirstOrDefault(x => !x.IsTerminal &&
				x.Kind == CollectionOperationKind.ApplyResolvedPlan && revision.Equals(x.Revision));
		}

		private async void VerifyRepairButton_Click(object sender, EventArgs e)
		{
			CollectionManagementAssociation selected = _managedAssociationCombo.SelectedItem as CollectionManagementAssociation;
			CollectionUiContext context = _managedAssociationActionContext;
			if (_workflowBusy || _managementWorkflow == null || selected == null || _managedAssociationPresentation == null ||
				context == null || context.AssociationId != selected.AssociationId)
				return;

			CancellationToken token = BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Verifying,
				L("Collections.VerifyRepair.Verifying", "Verifying the installed Collection against its exact retained revision..."));
			try
			{
				if (!await ReconcileInstallationBeforeManagementAsync(selected, context, token, false)) return;
				SetWorkflowActivity(CollectionWorkflowActivityBuilder.Foreground(CollectionWorkflowActivityPhase.Verifying,
					L("Collections.VerifyRepair.Verifying", "Verifying the installed Collection against its exact retained revision...")));
				CollectionVerifyRepairPlan plan = await _managementWorkflow.PreviewVerifyRepairAsync(selected.AssociationId, token);
				if (!IsWorkflowContextCurrent(context, token) || plan == null) return;
				RenderVerifyRepairPlan(plan);
				if (plan.IsHealthyAtCurrentCoverage)
				{
					bool reconciled = await _managementWorkflow.ReconcileVerifiedHealthyStateAsync(plan, token);
					if (!IsWorkflowContextCurrent(context, token)) return;
					if (reconciled)
						RefreshManagedAssociations();
					_workflowStatusLabel.Text = reconciled
						? L("Collections.VerifyRepair.HealthyStateReconciled", "Workflow: verification confirmed the Collection state is healthy; installed status and obsolete drift tracking were reconciled.")
						: L("Collections.VerifyRepair.Healthy", "Workflow: the installed Collection is healthy within the currently supported verification coverage.");
					return;
				}
				if (!plan.CanExecuteQualifiedRepair)
				{
					string reason = plan.Findings.FirstOrDefault(x => x.RequiresAction)?.Detail ??
						L("Collections.VerifyRepair.NotQualifiedReason", "NMM could not prepare a safe repair for these differences.");
					_workflowStatusLabel.Text = LanguageManager.Format("Collections.VerifyRepair.BlockedStatus", "Workflow: Repair unavailable. {0}", reason);
					XtraMessageBox.Show(this, reason, L("Collections.VerifyRepair.BlockedTitle", "Collection repair unavailable"),
						MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}

				List<string> repairNames = plan.Findings.Where(x => x.IsRepairable && x.MemberKey != null)
					.Select(x => FormatMemberSubject(x.MemberKey)).Distinct().ToList();
				string repairMods = String.Join(Environment.NewLine, repairNames.Take(12).Select(x => "- " + x));
				if (repairNames.Count > 12)
					repairMods += Environment.NewLine + LanguageManager.Format("Collections.VerifyRepair.MoreMods", "...and {0} more mods shown in the repair review.", repairNames.Count - 12);
				string prompt = LanguageManager.Format("Collections.VerifyRepair.SimpleApprovalPrompt",
					"Repair this Collection?\r\n\r\nNMM will restore the installed revision's exact files and settings for the items below. Your saved local choices will be kept.\r\n\r\n{0}\r\n\r\nStart repair?",
					String.IsNullOrEmpty(repairMods) ? L("Collections.VerifyRepair.PluginOrderItem", "- Collection plugin order") : repairMods);
				if (XtraMessageBox.Show(this, prompt, L("Collections.Actions.VerifyRepair", "Verify / Repair..."),
					MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes ||
					!IsWorkflowContextCurrent(context, token))
				{
					_workflowStatusLabel.Text = L("Collections.VerifyRepair.RepairDeclined", "Workflow: verification completed; qualified repair was not started.");
					return;
				}

				SetWorkflowActivity(CollectionWorkflowActivityBuilder.Foreground(CollectionWorkflowActivityPhase.Applying,
					L("Collections.VerifyRepair.Repairing", "Applying the qualified Collection repair...")));
				CollectionVerifyRepairExecutionResult result = await _managementWorkflow.ExecuteQualifiedRepairAsync(plan, token);
				if (!IsWorkflowContextCurrent(context, token)) return;
				RefreshManagedAssociations();
				_workflowStatusLabel.Text = LanguageManager.Format("Collections.VerifyRepair.Repaired",
					"Workflow: qualified repair completed and verified ({0} repaired requirement(s)).", result.RepairedCount);
				XtraMessageBox.Show(this, _workflowStatusLabel.Text, L("Collections.Actions.VerifyRepair", "Verify / Repair..."),
					MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection verify/repair failed: " + ex);
				RememberTechnicalFailure("verify-repair.failed", ex, context);
				if (IsWorkflowContextCurrent(context, token))
				{
					CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForFailure("verify-repair.failed", ex.Message);
					SetWorkflowPresentation(userMessage);
					AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic,
						L("Collections.Status.ActionRequired", "Action required"), "verify-repair.failed",
						selected.Association.Revision.ToString(), userMessage);
				}
			}
			finally
			{
				EndWorkflowWork(context);
			}
		}

		private void RenderRevisionUpdateReview(CollectionRevisionUpdateWorkflowReview review)
		{
			if (review == null) return;
			_operationSnapshot = review.Operation;
			CollectionRevisionUpdatePlan plan = review.UpdatePlan;
			_contentValue.Text = plan.HasBlockingActionRequired
				? L("Collections.Update.ContentBlocked", "Revision comparison blocked - review the issues")
				: L("Collections.Update.ComparisonComplete", "Exact Collection source retained; revision comparison complete");
			_appliedValue.Text = plan.HasBlockingActionRequired
				? L("Collections.Update.NotAppliedBlocked", "Not applied - revision review blocked")
				: L("Collections.Status.Applied.NotApplied", "Not applied");
			ClearReviewItems();
			_issuesHeader.Text = L("Collections.Update.ReviewHeader", "Revision update review");
			bool hasGlobalBlocker = plan.HasAssociationBlocker || plan.UnscopedDrift.Count > 0;
			AddReviewItem(hasGlobalBlocker ? CollectionReviewSeverity.Error : CollectionReviewSeverity.Info,
				hasGlobalBlocker ? CollectionReviewItemKind.Diagnostic : CollectionReviewItemKind.Progress,
				plan.HasBlockingActionRequired ? L("Collections.Status.ActionRequired", "Action required") : L("Collections.Status.Supported", "Ready"),
				"revision-update.summary", plan.NewPlan.Revision.ToString(),
				LanguageManager.Format("Collections.Update.Summary",
					"Comparing installed revision {0} with candidate revision {1}. The review contains {2} member row(s) and {3} exact effect row(s).",
					plan.OldPlan.Revision, plan.NewPlan.Revision, plan.Members.Count, plan.Effects.Count));

			foreach (CollectionRevisionUpdateMemberPlan member in plan.Members.Where(x => x.ChangeKind != CollectionRevisionUpdateChangeKind.Unchanged || x.RequiresExplicitReview))
			{
				CollectionReviewSeverity severity = member.Disposition == CollectionRevisionUpdateDisposition.DriftRequiresReview ||
					member.Disposition == CollectionRevisionUpdateDisposition.ActionRequired ? CollectionReviewSeverity.Error :
					member.Disposition == CollectionRevisionUpdateDisposition.PreserveOverrideForReview ? CollectionReviewSeverity.Warning : CollectionReviewSeverity.Info;
				CollectionReviewItemKind kind = severity == CollectionReviewSeverity.Error ? CollectionReviewItemKind.Diagnostic :
					member.RequiresExplicitReview ? CollectionReviewItemKind.ManualAction : CollectionReviewItemKind.PlannedEffect;
				string detail = String.IsNullOrWhiteSpace(member.Detail)
					? LanguageManager.Format("Collections.Update.MemberDetail", "Change: {0}; current state: {1}; disposition: {2}.", member.ChangeKind, member.CurrentStateKind, member.Disposition)
					: member.Detail;
				string nextAction = member.RequiresStandaloneUseConfirmation
					? L("Collections.Update.Next.ReviewOwnership", "Choose Review Collection ownership... to keep this installation independently or let Collections manage it.")
					: member.Disposition == CollectionRevisionUpdateDisposition.PreserveStandalone
						? L("Collections.Update.Next.PreserveStandalone", "This mod will stay installed independently; Collection removal is not planned.")
						: severity == CollectionReviewSeverity.Error
							? L("Collections.Update.Next.VerifyInstalled", "Select the installed Collection and use Verify / Repair to resolve this member's state, then review the revision again.")
							: member.PreparationKind == CollectionRevisionUpdatePreparationKind.ReprepareRequired
								? L("Collections.Update.Next.PrepareAfterApproval", "The revision update will prepare the required archives after approval.")
								: String.Empty;
				AddReviewItem(severity, kind, member.Disposition == CollectionRevisionUpdateDisposition.PreserveStandalone
					? L("Collections.Update.Status.PreservedStandalone", "Preserved independently")
					: member.RequiresExplicitReview ? L("Collections.Status.ActionRequired", "Action required") : member.ChangeKind.ToString(),
					"revision-update.member." + member.MemberKey.ToString(), FormatRevisionUpdateMemberSubject(member), detail, member.MemberKey,
					nextAction, "Change: " + member.ChangeKind + "; current state: " + member.CurrentStateKind +
					"; disposition: " + member.Disposition + "; standalone protected: " + member.StandaloneProtected);
			}

			foreach (var effectGroup in plan.Effects.Where(x => x.ChangeKind != CollectionRevisionUpdateEffectChangeKind.Unchanged)
				.GroupBy(x => new { x.MemberKey, x.Kind, x.ChangeKind }))
			{
				AddReviewItem(CollectionReviewSeverity.Info, CollectionReviewItemKind.PlannedEffect, effectGroup.Key.ChangeKind.ToString(),
					"revision-update.effects." + effectGroup.Key.MemberKey + "." + effectGroup.Key.Kind + "." + effectGroup.Key.ChangeKind,
					FormatMemberSubject(effectGroup.Key.MemberKey),
					LanguageManager.Format("Collections.Update.EffectSummary", "{0} {1} effect(s): {2}.", effectGroup.Count(), effectGroup.Key.Kind, effectGroup.Key.ChangeKind),
					effectGroup.Key.MemberKey);
			}
			UpdateIssuesHeader();
			_workflowStatusLabel.Text = plan.HasBlockingActionRequired
				? L("Collections.Update.ReviewBlocked", "Workflow: revision comparison completed; unresolved decisions block update approval.")
				: L("Collections.Update.ReviewReady", "Workflow: exact revision comparison is ready for explicit approval.");
		}

		/// <summary>Resolves a comparison member name from either revision so removed members retain their names.</summary>
		private string FormatRevisionUpdateMemberSubject(CollectionRevisionUpdateMemberPlan member)
		{
			ResolvedCollectionMemberPlan source = member.NewMember ?? member.OldMember;
			return source == null || String.IsNullOrWhiteSpace(source.DisplayName) ? FormatMemberSubject(member.MemberKey) : source.DisplayName;
		}

		private void RenderRevisionUpdateWorkflowResult(CollectionRevisionUpdateWorkflowResult result)
		{
			if (result == null) return;
			LoadInterruptedManifestForIncoming(result);
			_operationSnapshot = result.Operation;
			_operationIdentity = result.Operation.Identity;
			BindIncomingDisplayOperation(result.Operation.Identity);
			_revisionUpdateRecoveryResults = _revisionUpdateRecoveryResults.Where(x => !x.Operation.Identity.Equals(result.Operation.Identity))
				.Concat(result.IsCommitted ? Enumerable.Empty<CollectionRevisionUpdateWorkflowResult>() : new[] { result }).ToList();
			ClearRevisionUpdateContinuationItems(result.Preparation != null);
			ApplyRevisionUpdateMemberState(result);
			if (result.Preparation != null)
			{
				foreach (CollectionRevisionUpdatePreparationMemberState member in result.Preparation.Members)
				{
					bool archiveReady = member.IsPrepared || member.VerifiedArchive != null;
					bool queued = member.Disposition == CollectionMemberAcquisitionDisposition.PremiumQueued ||
						member.Disposition == CollectionMemberAcquisitionDisposition.BundledQueued ||
						member.Disposition == CollectionMemberAcquisitionDisposition.DirectQueued;
					CollectionReviewSeverity severity = archiveReady || queued ? CollectionReviewSeverity.Info : CollectionReviewSeverity.Warning;
					CollectionReviewItemKind kind = member.PendingAction != null ? CollectionReviewItemKind.ManualAction : CollectionReviewItemKind.Progress;
					string status = member.IsPrepared ? L("Collections.Update.MemberReady", "Ready to install")
						: archiveReady ? L("Collections.Update.ArchiveReady", "File ready")
						: queued ? L("Collections.Update.MemberDownloading", "Downloading") : L("Collections.Status.Pending", "Pending");
					CollectionUserMessagePresentation message = archiveReady
						? CollectionUserMessagePresenter.FromRaw(L("Collections.Update.FileReadyDetail", "The exact mod file is verified. NMM will use it to complete the approved revision change."), String.Empty)
						: CollectionUserMessagePresenter.ForAcquisition(member.Disposition, member.Disposition.ToString());
					AddPresentedReviewItem(severity, kind, status, "revision-update.acquisition." + member.UpdateMember.MemberKey,
						FormatMemberSubject(member.UpdateMember.MemberKey), message, member.UpdateMember.MemberKey);
				}
				AppendFolderCorrectionReview(result.Preparation.PreparedRecipes);
			}

			if (!result.IsCommitted)
			{
				_contentValue.Text = GetRevisionUpdateContentText(result.Status);
				_appliedValue.Text = result.Operation.HasCrossedNativeBoundary
					? result.Status == CollectionRevisionUpdateWorkflowStatus.RecoveryRequired
						? L("Collections.Update.PartiallyChangedRecovery", "Partially changed - recovery required")
						: L("Collections.Update.PartiallyChanged", "Partially changed - continuation required")
					: L("Collections.Update.NotFinished", "Revision change not finished");
			}

			string targetRevisionLabel = FormatRevisionActionLabel(result.Operation.Revision);
			switch (result.Status)
			{
				case CollectionRevisionUpdateWorkflowStatus.Committed:
					string committedStatus = L("Collections.Update.Committed", "Workflow: Collection revision update committed and aggregate state verified.");
					_revisionUpdateReview = null;
					_revisionUpdatePreparation = null;
					_revisionUpdateResult = null;
					_revisionUpdateRecoveryResults = _revisionUpdateRecoveryResults.Where(x => !x.Operation.Identity.Equals(result.Operation.Identity)).ToList();
					_operationSnapshot = null;
					_operationIdentity = null;
					ClearIncomingDisplayOperation();
					// Publication changed the durable association from the previous revision to the candidate. Do not leave
					// the control in its former IncomingCollection context: that context keeps the old continuation guidance
					// alive until another navigation/restart. Rebind the just-published association as a normal installed view
					// immediately so instructions, member participation and action visibility all describe the terminal state.
					RefreshManagedAssociations(true);
					ShowSelectedManagedAssociation();
					_workflowStatusLabel.Text = committedStatus;
					SetWorkflowActivity(CollectionWorkflowActivityBuilder.Idle(committedStatus));
					break;
				case CollectionRevisionUpdateWorkflowStatus.AwaitingInput:
					_workflowStatusLabel.Text = GetRevisionUpdateDownloadStatus(result.Preparation);
					break;
				case CollectionRevisionUpdateWorkflowStatus.ReadyForReview:
					_workflowStatusLabel.Text = LanguageManager.Format("Collections.Update.PendingApprovalStatus",
						"Workflow: the change to {0} is ready for review. Review and approve the remaining changes to continue.", targetRevisionLabel);
					break;
				case CollectionRevisionUpdateWorkflowStatus.PausedAtSafeBoundary:
					_workflowStatusLabel.Text = LanguageManager.Format("Collections.Update.PausedRevisionStatus",
						"Workflow: the change to {0} is paused at a safe boundary. Continue it to revalidate completed steps and proceed.", targetRevisionLabel);
					break;
				case CollectionRevisionUpdateWorkflowStatus.RecoveryRequired:
					_workflowStatusLabel.Text = LanguageManager.Format("Collections.Workflow.UserStatus", "Workflow: {0}",
						GetRevisionUpdateInstructions(result.Status, result.Operation.Revision, result.Operation));
					break;
				case CollectionRevisionUpdateWorkflowStatus.ExplicitReviewRequired:
					_workflowStatusLabel.Text = LanguageManager.Format("Collections.Update.StoppedRevisionStatus",
						"Workflow: the change to {0} is blocked. Review the named issues below; Continue reruns the exact checks after the blocking condition changes.", targetRevisionLabel);
					break;
				default:
					_workflowStatusLabel.Text = LanguageManager.Format("Collections.Update.IncompleteRevisionStatus",
						"Workflow: the change to {0} is incomplete. Review the reason below, then Continue to revalidate completed steps.", targetRevisionLabel);
					break;
			}
			AppendRevisionUpdateStopDiagnostics(result);
			UpdateIssuesHeader();
			if (result.Status == CollectionRevisionUpdateWorkflowStatus.ExplicitReviewRequired ||
				result.Status == CollectionRevisionUpdateWorkflowStatus.RecoveryRequired)
				ShowFirstBlockingIssue();
			UpdateActionButtons();
		}

		/// <summary>Replaces obsolete continuation diagnostics while preserving the reviewed revision changes.</summary>
		private void ClearRevisionUpdateContinuationItems(bool replaceAcquisition)
		{
			List<CollectionReviewItem> retained = _reviewItems.Where(x => x.Code != "revision-update.failed" &&
				x.Code != "revision-update.continuation-stopped" &&
				!x.Code.StartsWith("revision-update.execution.", StringComparison.Ordinal) &&
				!x.Code.StartsWith("revision-update.interrupted.", StringComparison.Ordinal) &&
				(!replaceAcquisition || !x.Code.StartsWith("revision-update.acquisition.", StringComparison.Ordinal))).ToList();
			if (retained.Count == _reviewItems.Count) return;
			ClearReviewItems();
			foreach (CollectionReviewItem item in retained) AddReviewItem(item);
		}

		/// <summary>Explains which revision files are ready and what the continuation button will do.</summary>
		private string GetRevisionUpdateDownloadStatus(CollectionRevisionUpdatePreparationBatch preparation)
		{
			if (preparation == null)
				return L("Collections.Update.AwaitingRevisionFiles", "Workflow: wait for the downloads to finish, then choose Continue revision change to verify the files and install the approved changes.");
			int ready = preparation.Members.Count(x => x.IsPrepared || x.VerifiedArchive != null);
			return LanguageManager.Format("Collections.Update.RevisionFilesProgress",
				"Workflow: {0} of {1} required mod files are ready. Complete any remaining downloads or requested input, then choose Continue revision change to verify the files and install the approved changes.",
				ready, preparation.Members.Count);
		}

		private void RenderVerifyRepairPlan(CollectionVerifyRepairPlan plan)
		{
			ClearReviewItems();
			if (!plan.IsHealthyAtCurrentCoverage)
			{
				_contentValue.Text = L("Collections.VerifyRepair.DifferencesContent", "Verification found differences");
				_compatibilityValue.Text = L("Collections.VerifyRepair.DifferencesCompatibility", "Supported revision - current setup differs");
			}
			_issuesHeader.Text = L("Collections.VerifyRepair.ReviewHeader", "Verify / Repair review");
			List<CollectionVerifyRepairFinding> visible = plan.Findings.Where(x => x.Kind != CollectionVerifyRepairFindingKind.Satisfied).ToList();
			if (visible.Count == 0)
			{
				AddReviewItem(CollectionReviewSeverity.Info, CollectionReviewItemKind.Progress, L("Collections.Status.Supported", "Healthy"),
					"verify-repair.healthy", plan.Association.Revision.ToString(),
					L("Collections.VerifyRepair.HealthyDetail", "No differences were found within the currently supported exact verification coverage."));
			}
			foreach (CollectionVerifyRepairFinding finding in visible)
			{
				CollectionReviewSeverity severity = finding.RequiresAction ? CollectionReviewSeverity.Error :
					finding.IsRepairable ? CollectionReviewSeverity.Warning : CollectionReviewSeverity.Info;
				CollectionReviewItemKind kind = finding.RequiresAction ? CollectionReviewItemKind.Diagnostic :
					finding.IsRepairable ? CollectionReviewItemKind.PlannedEffect : CollectionReviewItemKind.Progress;
				bool folderCorrection = finding.Requirement != null && StringComparer.Ordinal.Equals(finding.Requirement.SubjectKey, "install-folder");
				string subject = folderCorrection || finding.Requirement == null ? FormatMemberSubject(finding.MemberKey) : FormatRequirementSubject(finding.Requirement);
				AddReviewItem(severity, kind,
					finding.RequiresAction ? L("Collections.Status.ActionRequired", "Action required") : finding.IsRepairable ? L("Collections.VerifyRepair.Repairable", "Repairable") : L("Collections.Status.Supported", "Preserved"),
					"verify-repair.finding." + finding.Kind + "." + (finding.MemberKey == null ? "global" : finding.MemberKey.ToString()), subject,
					folderCorrection && finding.IsRepairable ? L("Collections.FolderCorrection.Detail", "Move this mod from Data to the game folder. Its old Data files will be removed, and any files it replaced will be restored.") : finding.Detail, finding.MemberKey, folderCorrection && finding.IsRepairable ? L("Collections.FolderCorrection.RepairAction", "Choose Repair to move this mod.") : finding.IsRepairable ? L("Collections.VerifyRepair.QualifiedRepairAction", "Restore the exact reviewed Collection state for this requirement.") : null,
					finding.Requirement == null ? null : CombineTechnicalDetail("Expected: " + finding.ExpectedState, "Observed: " + finding.ObservedState));
			}
			AppendFolderCorrectionReview(plan.PreparedRecipes.Where(x => plan.Findings.Any(f => f.IsRepairable &&
				f.MemberKey != null && f.MemberKey.Equals(x.Member.MemberKey) && f.Requirement != null &&
				StringComparer.Ordinal.Equals(f.Requirement.SubjectKey, "install-folder"))));
			UpdateIssuesHeader();
		}

		private CollectionManagementAssociation FindRevisionUpdateSourceAssociation()
		{
			if (_snapshot == null || _snapshot.Revision == null) return null;
			List<CollectionManagementAssociation> candidates = _managedAssociationCombo.Properties.Items.Cast<object>()
				.Select(x => x as CollectionManagementAssociation).Where(x => x != null &&
					IsRevisionChangeCandidate(_snapshot.Revision.Identity, x.Association.Revision)).ToList();
			return candidates.Count == 1 ? candidates[0] : null;
		}

		/// <summary>Recognizes another exact revision of the installed Collection, including an older revision.</summary>
		internal static bool IsRevisionChangeCandidate(CollectionRevisionIdentity incoming, CollectionRevisionIdentity installed)
		{
			return incoming != null && installed != null && incoming.Collection.Equals(installed.Collection) &&
				!incoming.Equals(installed);
		}

		/// <summary>Allows revision review to replace only an unused additive preparation.</summary>
		internal static bool CanSupersedePreparationForRevisionChange(CollectionOperation operation)
		{
			return operation == null || operation.IsTerminal ||
				(operation.Kind == CollectionOperationKind.ApplyResolvedPlan && !operation.HasCrossedNativeBoundary);
		}

		/// <summary>Offers download continuation only for an input-paused preparation with no blocking members.</summary>
		internal static bool CanResumePreparation(CollectionMemberAcquisitionBatch batch)
		{
			return batch != null && batch.IsAwaitingInput && !batch.HasBlockedMembers;
		}

		/// <summary>Finds the candidate operation belonging to the displayed previous revision and current target.</summary>
		private CollectionRevisionUpdateWorkflowResult FindInterruptedRevisionUpdateForAssociation(CollectionManagementAssociation association)
		{
			if (association == null || association.PendingRevision == null) return null;
			CollectionRevisionUpdateWorkflowResult retained = _revisionUpdateRecoveryResults.FirstOrDefault(x => x != null && !x.IsCommitted &&
				x.Operation.Target.Equals(association.Association.Target) && x.Operation.Revision != null &&
				x.Operation.Revision.Equals(association.PendingRevision) &&
				(x.Preparation == null || x.Preparation.CurrentPlan.Association.AssociationId == association.AssociationId));
			if (retained != null || _revisionUpdateWorkflow == null) return retained;
			try
			{
				return _revisionUpdateWorkflow.InspectInterruptedForAssociation(association.AssociationId);
			}
			catch (Exception ex)
			{
				Trace.TraceWarning("Durable revision-update continuation lookup failed: " + ex.Message);
				return null;
			}
		}

		/// <summary>Lets an installed Collection choose another exact revision that NMM has already retained locally.</summary>
		private CollectionRevision ChooseRetainedRevisionCandidate(CollectionManagementAssociation installedAssociation)
		{
			if (installedAssociation == null || _revisionUpdateWorkflow == null) return null;
			IReadOnlyList<CollectionRevision> candidates = _revisionUpdateWorkflow.GetRetainedRevisionCandidates(installedAssociation.AssociationId);
			if (candidates.Count == 0)
			{
				XtraMessageBox.Show(this,
					L("Collections.Update.NoRetainedRevisionCandidates",
						"NMM does not currently have another exact revision of this Collection retained locally. Open or import the concrete Nexus revision once; after NMM retains it, Compare / Update can select it directly here."),
					L("Collections.Actions.CompareUpdate", "Compare / Update..."), MessageBoxButtons.OK, MessageBoxIcon.Information);
				return null;
			}

			using (DevExpressDisplaySettings dialogSettings = DevExpressDisplaySettings.CreateFromSettings(Properties.Settings.Default))
			using (var dialog = new ManagedFontXtraForm())
			{
				dialog.Text = L("Collections.Update.ChooseRevisionTitle", "Choose Collection revision");
				dialog.StartPosition = FormStartPosition.CenterParent;
				dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
				dialog.MinimizeBox = false;
				dialog.MaximizeBox = false;
				dialog.ShowInTaskbar = false;
				dialog.ClientSize = new Size(500, 330);

				var layout = new TableLayoutPanel
				{
					Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12)
				};
				layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
				layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
				layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				dialog.Controls.Add(layout);

				LabelControl current = new LabelControl
				{
					AutoSizeMode = LabelAutoSizeMode.Vertical, Dock = DockStyle.Fill,
					Text = LanguageManager.Format("Collections.Update.CurrentRevision", "Currently installed: {0}",
						FormatRevisionActionLabel(installedAssociation.Association.Revision))
				};
				layout.Controls.Add(current, 0, 0);

				LabelControl help = new LabelControl
				{
					AutoSizeMode = LabelAutoSizeMode.Vertical, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 8),
					Text = L("Collections.Update.KnownRevisionHelp",
						"Choose another revision already retained by NMM. This does not change the installed Collection yet; the next screen builds the exact comparison and lets you review optional selections before approval.")
				};
				layout.Controls.Add(help, 0, 1);

				ListBoxControl revisions = new ListBoxControl { Dock = DockStyle.Fill };
				foreach (CollectionRevision candidate in candidates) revisions.Items.Add(new RetainedRevisionChoice(candidate));
				if (revisions.Items.Count > 0) revisions.SelectedIndex = 0;
				layout.Controls.Add(revisions, 0, 2);

				SimpleButton compare = new SimpleButton
				{
					AutoSize = true, Text = L("Collections.Update.OpenSelectedRevision", "Open selected revision"),
					DialogResult = DialogResult.OK, Enabled = revisions.SelectedItem != null
				};
				SimpleButton cancel = new SimpleButton
				{
					AutoSize = true, Text = L("Common.Action.Cancel", "Cancel"), DialogResult = DialogResult.Cancel
				};
				revisions.SelectedIndexChanged += (sender, args) => compare.Enabled = revisions.SelectedItem != null;
				revisions.DoubleClick += (sender, args) =>
				{
					if (revisions.SelectedItem != null) dialog.DialogResult = DialogResult.OK;
				};
				FlowLayoutPanel buttons = new FlowLayoutPanel
				{
					Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 8, 0, 0)
				};
				buttons.Controls.Add(cancel);
				buttons.Controls.Add(compare);
				layout.Controls.Add(buttons, 0, 3);
				dialog.AcceptButton = compare;
				dialog.CancelButton = cancel;

				ApplyCollectionDialogDisplaySettings(dialog, dialogSettings);
				if (dialog.ShowDialog(this) != DialogResult.OK) return null;
				RetainedRevisionChoice selected = revisions.SelectedItem as RetainedRevisionChoice;
				return selected == null ? null : selected.Revision;
			}
		}

		/// <summary>Shows one locally-retained historical revision as the incoming candidate for the existing review/update path.</summary>
		private void ShowRetainedRevisionCandidate(CollectionRevision revision)
		{
			if (revision == null) throw new ArgumentNullException(nameof(revision));
			NexusCollectionPreviewSnapshot retained = _revisionUpdateWorkflow.LoadRetainedPreview(revision.Identity);
			CancelPreviewWork(false);
			_managedAssociationPresentation = null;
			ResetWorkflowViewState();
			_snapshot = retained;
			_displayContext = CollectionUiContext.Incoming(++_previewGeneration, retained.Revision.Identity, null);
			RenderSnapshot(retained);
			_workflowStatusLabel.Text = LanguageManager.Format("Collections.Update.RetainedRevisionReady",
				"Workflow: {0} is loaded from NMM's retained Collection history. Review optional selections, then choose Review revision change to build the exact comparison.",
				FormatRevisionActionLabel(retained.Revision.Identity));
			RefreshWorkflowActivity();
		}

		/// <summary>Opens the saved candidate as a preview; continuation still requires its primary action.</summary>
		private void ShowInterruptedRevisionUpdate(CollectionRevisionUpdateWorkflowResult result)
		{
			NexusCollectionPreviewSnapshot retained = _revisionUpdateWorkflow.LoadInterruptedPreview(result.Operation.Identity);
			CancelPreviewWork(false);
			_managedAssociationPresentation = null;
			ResetWorkflowViewState();
			_snapshot = retained;
			_displayContext = CollectionUiContext.Incoming(++_previewGeneration, retained.Revision.Identity, result.Operation.Identity);
			_revisionUpdateResult = result;
			_revisionUpdatePreparation = result.Preparation;
			RenderSnapshot(retained);
			RenderRevisionUpdateWorkflowResult(result);
			RefreshWorkflowActivity();
		}

		/// <summary>Restores missing manifest presentation from the approved operation's retained exact source.</summary>
		private void LoadInterruptedManifestForIncoming(CollectionRevisionUpdateWorkflowResult result)
		{
			if (result == null || result.IsCommitted || _snapshot == null || _snapshot.HasManifestPreview ||
				_snapshot.Revision == null || !_snapshot.Revision.Identity.Equals(result.Operation.Revision) ||
				_displayContext == null || _displayContext.Kind != CollectionUiContextKind.IncomingCollection)
				return;
			NexusCollectionPreviewSnapshot retained = _revisionUpdateWorkflow.LoadInterruptedPreview(result.Operation.Identity);
			_snapshot = _snapshot.WithBundleImport(retained.BundleImport);
			RenderSnapshot(_snapshot);
		}

		/// <summary>Distinguishes verified candidate installs from ready files and previous-revision bindings.</summary>
		private void ApplyRevisionUpdateMemberState(CollectionRevisionUpdateWorkflowResult result)
		{
			if (result.Preparation == null || _snapshot == null || _snapshot.Revision == null ||
				!_snapshot.Revision.Identity.Equals(result.Operation.Revision)) return;
			HashSet<CollectionMemberKey> selected = new HashSet<CollectionMemberKey>(result.Preparation.CurrentPlan.NewPlan.SelectedMembers.Select(x => x.MemberKey));
			HashSet<CollectionMemberKey> committed = new HashSet<CollectionMemberKey>(result.Operation.NativeChildren.Where(x =>
				x.Action == CollectionNativeChildAction.ActivateOrReinstall && x.IsReconciled && x.HasVerifiedCommittedNativeState).Select(x => x.Member.MemberKey));
			Dictionary<CollectionMemberKey, CollectionRevisionUpdatePreparationMemberState> preparing = result.Preparation.Members.ToDictionary(x => x.UpdateMember.MemberKey);
			_suppressMemberCheckEvents = true;
			try
			{
				foreach (MemberGridRow row in _memberRows)
				{
					NormalizedCollectionMember member = row.Member;
					if (member == null || !member.IdentityResolution.IsResolved) continue;
					CollectionMemberKey key = member.IdentityResolution.Key;
					row.Selected = selected.Contains(key);
					CollectionRevisionUpdatePreparationMemberState preparation;
					row.ManagedState = !row.Selected ? L("Collections.Member.Unselected", "Not selected")
						: committed.Contains(key) ? L("Collections.Update.MemberCandidateVerified", "Installed for target revision; verified")
						: preparing.TryGetValue(key, out preparation) ? preparation.VerifiedArchive != null
							? L("Collections.Update.MemberFileReadyPending", "File ready; revision change not finished")
							: L("Collections.Update.MemberFileWaiting", "Waiting for revision file")
						: L("Collections.Update.MemberRetainedPending", "Retained member; target revision not yet verified");
				}
				UpdateMemberSelectionText();
			}
			finally { _suppressMemberCheckEvents = false; }
		}

		/// <summary>Gives a reachable next action without implying that repeating Resume resolves a planner failure.</summary>
		private static string GetRevisionUpdateBlockedNextAction()
		{
			return L("Collections.Update.BlockedNextAction",
				"Export Technical Report for this unfinished revision change. These findings need a supported recovery or review step; repeating the check alone cannot resolve them.");
		}

		/// <summary>Retains the specific execution gate findings in the visible issues and exported technical report.</summary>
		private void AppendRevisionUpdateStopDiagnostics(CollectionRevisionUpdateWorkflowResult result)
		{
			if (result == null || (result.Status != CollectionRevisionUpdateWorkflowStatus.ExplicitReviewRequired &&
				result.Status != CollectionRevisionUpdateWorkflowStatus.RecoveryRequired)) return;
			string nextAction = result.Status == CollectionRevisionUpdateWorkflowStatus.RecoveryRequired &&
				CanRecheckReportedSuccessfulRevisionChild(result.Operation)
				? GetRevisionUpdateInstructions(result.Status, result.Operation.Revision, result.Operation)
				: GetRevisionUpdateBlockedNextAction();
			AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic,
				L("Collections.Status.ActionRequired", "Action required"), "revision-update.continuation-stopped",
				L("Collections.Update.RevisionChangeSubject", "Revision change"), CollectionUserMessagePresenter.FromRaw(result.Message, nextAction));
			CollectionRevisionUpdateCandidateExecutionPlanning planning = result.ExecutionPlanning;
			if (planning != null)
			{
				foreach (CollectionMemberMatchResult match in planning.Matches.Members.Where(x => x.IsBlocked || x.Disposition == CollectionMemberMatchDisposition.AcquisitionRequired))
				{
					string detail = match.Reason == CollectionMemberMatchReason.ConflictingVerifiedRecipe
						? L("Collections.Update.MatchProtected", "This mod would reuse an installation protected for independent use or another Collection.")
						: match.Reason == CollectionMemberMatchReason.MissingBoundNativeMod
							? L("Collections.Update.MatchMissing", "The mod installation recorded by this member's binding is missing.")
							: match.Reason == CollectionMemberMatchReason.AmbiguousInstalledCandidates
								? L("Collections.Update.MatchAmbiguous", "Several installed mods match this member; NMM cannot choose one safely.")
								: match.Reason == CollectionMemberMatchReason.NoReusableInput
									? L("Collections.Update.MatchInputMissing", "The exact archive or prepared installation recipe required for this member is unavailable.")
									: L("Collections.Update.MatchUnavailable", "NMM cannot establish a safe installation match for this member.");
					AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic,
						L("Collections.Status.ActionRequired", "Action required"), "revision-update.execution.match." + match.Reason,
						FormatMemberSubject(match.Member.MemberKey), CollectionUserMessagePresenter.FromRaw(detail, nextAction), match.Member.MemberKey,
						"Match reason: " + match.Reason + "; native candidates: " + String.Join(", ", match.NativeCandidates.Select(x => x.Identity.NativeModKey)));
				}
				foreach (CollectionDependencyPhaseIssue issue in planning.DependencyPlan.Issues.Where(x => x.Kind != CollectionDependencyPhaseIssueKind.BlockedMemberMatch))
					AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic,
						L("Collections.Status.ActionRequired", "Action required"), "revision-update.execution.dependency." + issue.Kind,
						issue.MemberKey == null ? GetCurrentCollectionSubject() : FormatMemberSubject(issue.MemberKey),
						CollectionUserMessagePresenter.FromRaw(issue.Reason, nextAction), issue.MemberKey,
						"Dependency issue: " + issue.Kind + "; related member: " + issue.RelatedMemberKey);
				foreach (CollectionConflictImpactIssue issue in planning.ImpactPlan.Issues.Where(x => x.Kind != CollectionConflictImpactIssueKind.UpstreamPlanBlocked))
				{
					string subject = issue.MemberKey == null ? GetCurrentCollectionSubject() : FormatMemberSubject(issue.MemberKey);
					if (!String.IsNullOrWhiteSpace(issue.SubjectKey)) subject += " / " + issue.SubjectKey;
					CollectionFileImpact file = planning.ImpactPlan.FileImpacts.FirstOrDefault(x => x.Target.ToString() == issue.SubjectKey);
					AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic,
						L("Collections.Status.ActionRequired", "Action required"), "revision-update.execution.impact." + issue.Kind, subject,
						CollectionUserMessagePresenter.FromRaw(issue.Message, nextAction), issue.MemberKey,
						"Impact issue: " + issue.Kind + "; resource: " + issue.SubjectKey + "; current owner: " + (file == null ? String.Empty : file.CurrentOwnerKey));
				}
				if (planning.IsReady && CollectionRevisionUpdateCandidateExecutionCoordinator.RequiresDurableFileWinnerReconciliation(planning.ImpactPlan))
					foreach (CollectionFileImpact file in planning.ImpactPlan.FileImpacts.Where(x => x.PreserveCurrentOwner || x.Writers.Count > 1))
						AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic,
							L("Collections.Status.ActionRequired", "Action required"), "revision-update.execution.file-provider-review", file.Target.ToString(),
							CollectionUserMessagePresenter.FromRaw(L("Collections.Update.FileProviderUnsupported", "This file needs provider reconciliation that the approved revision change cannot apply safely."), nextAction), file.PlannedWinner,
							"Current owner: " + file.CurrentOwnerKey + "; writers: " + String.Join(", ", file.Writers));
			}
			_showErrorIssuesCheckBox.Checked = true;
			UpdateIssuesHeader();
		}

		private CollectionRevisionUpdateWorkflowResult FindMatchingInterruptedRevisionUpdate()
		{
			if (_snapshot == null || _snapshot.Revision == null) return null;
			return _revisionUpdateRecoveryResults.FirstOrDefault(x => x != null && !x.IsCommitted && x.Operation != null &&
				x.Operation.Revision != null && x.Operation.Revision.Equals(_snapshot.Revision.Identity));
		}

		private void DetachManagedAssociation(CollectionManagementAssociation selected)
		{
			CollectionDetachResult result = _managementWorkflow.Detach(selected.AssociationId);
			RefreshManagedAssociations();
			_workflowStatusLabel.Text = LanguageManager.Format("Collections.Management.Detached",
				"Workflow: NMM stopped tracking '{0}'; installed content was preserved.", selected.DisplayName);
			XtraMessageBox.Show(this, LanguageManager.Format("Collections.Management.DetachedMessage",
				"'{0}' is no longer tracked as an installed Collection. {1} installed mod instance(s) remain available for standalone use.",
				selected.DisplayName, result.StandaloneProvenance.Count),
				L("Collections.Management.DetachedTitle", "Collection detached"), MessageBoxButtons.OK, MessageBoxIcon.Information);
		}

		private async void ManageAssociationRemovalButton_Click(object sender, EventArgs e)
		{
			CollectionManagementAssociation selected = _managedAssociationCombo.SelectedItem as CollectionManagementAssociation;
			CollectionUiContext context = _managedAssociationActionContext;
			if (_managementWorkflow == null || selected == null || _workflowBusy || context == null ||
				context.AssociationId != selected.AssociationId)
				return;

			string choicePrompt = LanguageManager.Format("Collections.Management.RemoveOrDetachPrompt",
				"Choose what to do with '{0}'.\r\n\r\nYes = choose which Collection mods to uninstall. You can keep individual mods in the next screen. An unfinished installation will not block removal.\r\n\r\nNo = stop tracking only and keep all installed mods, files, plugins and configuration exactly as they are.\r\n\r\nCancel = make no changes.",
				selected.DisplayName);
			DialogResult choice = XtraMessageBox.Show(this, choicePrompt,
				L("Collections.Actions.ManageRemoval", "Remove / stop tracking..."), MessageBoxButtons.YesNoCancel,
				MessageBoxIcon.Question, MessageBoxDefaultButton.Button3);
			if (choice == DialogResult.Cancel || !IsActionContextCurrent(context))
				return;

			if (choice == DialogResult.No)
			{
				BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Managing,
					L("Collections.Management.Detaching", "Stopping Collection tracking without changing installed content..."));
				try
				{
					DetachManagedAssociation(selected);
				}
				catch (Exception ex)
				{
					Trace.TraceError("Collection detach failed: " + ex);
					RememberTechnicalFailure("association.detach-failed", ex, context);
					CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForFailure("association.detach-failed", ex.Message);
					XtraMessageBox.Show(this, BuildUserDialogMessage(userMessage),
						L("Collections.Management.DetachFailed", "Collection tracking change failed"),
						MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
				finally
				{
					EndWorkflowWork(context);
				}
				return;
			}

			bool removalApproved = false;
			CancellationToken token = BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Managing, L("Collections.Management.ReviewingRemoval",
				"Reviewing which Collection effects can be removed safely..."));
			try
			{
				SetWorkflowActivity(CollectionWorkflowActivityBuilder.Foreground(CollectionWorkflowActivityPhase.Managing,
					L("Collections.Management.ReviewingRemoval", "Reviewing which Collection effects can be removed safely...")));
				CollectionUninstallEffectsPlan plan = await _managementWorkflow.PreviewCollectionModRemovalAsync(selected.AssociationId, token);
				if (!IsWorkflowContextCurrent(context, token))
					return;

				if (plan.HasBlockedImpacts)
				{
					string blocked = String.Join(Environment.NewLine, plan.Impacts.Where(x => x.BlocksExecution)
						.Select(x => "- " + FormatUninstallImpactSubject(x) + ": " + CollectionUserMessagePresenter.SanitizeInternalTerminology(x.Reason)));
					XtraMessageBox.Show(this, LanguageManager.Format("Collections.Management.RemovalBlockedMessageWithDetach",
						"Safe automatic removal is blocked by the current managed setup. Nothing was changed.\r\n\r\n{0}\r\n\r\nYou can run Remove / stop tracking again and choose No to stop Collection tracking while preserving all installed content.", blocked),
						L("Collections.Management.RemovalBlockedTitle", "Collection effect removal blocked"),
						MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}

				CollectionManagementAssociationPresentation removalPresentation = _managedAssociationPresentation;
				if (removalPresentation == null || removalPresentation.Association.AssociationId != selected.AssociationId)
					removalPresentation = await Task.Run(() => _managementWorkflow.GetAssociationPresentation(selected.AssociationId), token);
				if (!IsWorkflowContextCurrent(context, token)) return;
				Dictionary<NativeModInstanceIdentity, string> removalSubjects = plan.Impacts.ToDictionary(x => x.NativeMod,
					x => FormatUninstallImpactSubject(x, removalPresentation == null ? null : removalPresentation.RetainedManifest));
				using (var review = new CollectionUninstallReviewDialog(selected.DisplayName, plan,
					x => removalSubjects[x.NativeMod], FormatUninstallPreservationReason))
				{
					if (review.ShowDialog(this) != DialogResult.OK || !IsWorkflowContextCurrent(context, token))
						return;
					plan = review.ReviewedPlan;
				}

				removalApproved = true;
				SetWorkflowActivity(CollectionWorkflowActivityBuilder.Foreground(CollectionWorkflowActivityPhase.Managing,
					L("Collections.Management.UninstallingReviewedMods", "Uninstalling the reviewed Collection mods and checking the result...")));
				CollectionUninstallEffectsResult result = await _managementWorkflow.RemoveEffectsAsync(plan, token,
					_managedAssociationPresentation == null ? _snapshot?.Revision?.Identity.Collection : null);
				if (!IsWorkflowContextCurrent(context, token))
					return;
				_recoveryResults = _recoveryResults.Where(x => !x.Operation.Collection.Equals(selected.Association.Revision.Collection)).ToList();
				_localRestoreRecoveryResults = _localRestoreRecoveryResults.Where(x => !x.Operation.Collection.Equals(selected.Association.Revision.Collection)).ToList();
				_localRestoreRecoverySources = _localRestoreRecoverySources.Where(x => !x.Operation.Collection.Equals(selected.Association.Revision.Collection)).ToList();
				_revisionUpdateRecoveryResults = _revisionUpdateRecoveryResults.Where(x => !x.Operation.Collection.Equals(selected.Association.Revision.Collection)).ToList();
				_verifyRepairRecoveryResults = _verifyRepairRecoveryResults.Where(x =>
					!plan.SupersededOperations.Any(operation => operation.Identity.Equals(x.OperationIdentity))).ToList();
				if (_revisionUpdateResult != null && _revisionUpdateResult.Operation.Collection.Equals(selected.Association.Revision.Collection))
					_revisionUpdateResult = null;
				if (result.IsSuccessful)
					RememberCompletedEffectRemoval(result, selected, removalSubjects);
				RefreshManagedAssociations();
				if (result.IsSuccessful)
				{
					_workflowStatusLabel.Text = _completedRemovalReport.Progress.WorkflowStatus;
					XtraMessageBox.Show(this, LanguageManager.Format("Collections.Management.RemovalCompleteSummary",
						"Uninstall finished for '{0}'.\r\n\r\n{1} mods uninstalled. {2} mods kept.\r\n\r\nThe list below shows what was kept and why. You can still export a Technical Report.",
						selected.DisplayName, result.Impacts.Count(x => x.RequiresNativeRemoval),
						result.Impacts.Count(x => !x.RequiresNativeRemoval && x.Disposition != CollectionUninstallNativeDisposition.AlreadyAbsent)),
						L("Collections.Management.RemovalCompleteTitle", "Collection uninstalled"),
						MessageBoxButtons.OK, MessageBoxIcon.Information);
				}
				else
				{
					_operationSnapshot = result.Operation;
					_workflowStatusLabel.Text = LanguageManager.Format("Collections.Management.Removal.PartialCounts",
						"Workflow: {0} mods uninstalled. {1} mods could not be removed. See the list below; Uninstall Collection can retry the remaining mods.",
						result.RemovedCount, result.FailedRemovals.Count());
					foreach (CollectionUninstallNativeImpact failed in result.FailedRemovals)
						AddReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic,
							L("Collections.Status.NotRemoved", "Not removed"), "association.remove.native-failed", removalSubjects[failed.NativeMod],
							L("Collections.Management.Removal.NativeFailed", "NMM's mod uninstaller did not finish removing this mod. Other listed mods were still attempted."),
							failed.MemberKeys.FirstOrDefault(), L("Collections.Management.Removal.Retry", "Choose Uninstall Collection to retry the remaining mods."));
					ShowFirstBlockingIssue();
				}
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
					AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic,
						L("Collections.Status.ActionRequired", "Action required"), "association.remove-failed", selected.DisplayName, userMessage);
					XtraMessageBox.Show(this, BuildUserDialogMessage(userMessage), removalApproved
						? L("Collections.Management.RemovalFailed", "Collection effect removal stopped")
						: L("Collections.Management.RemovalBlockedTitle", "Collection effect removal blocked"),
						MessageBoxButtons.OK, removalApproved ? MessageBoxIcon.Error : MessageBoxIcon.Warning);
				}
			}
			finally
			{
				EndWorkflowWork(context);
			}
		}

		/// <summary>Reviews and deletes only the exact selected saved backup, preserving installed mods and shared recovery content.</summary>
		private async void DeleteLocalCaptureButton_Click(object sender, EventArgs e)
		{
			CollectionManagementLocalCapture selected = _localCaptureCombo.SelectedItem as CollectionManagementLocalCapture;
			CollectionUiContext context = _localCaptureActionContext;
			CollectionManagementApplicationService workflow = _managementWorkflow;
			if (workflow == null || selected == null || _workflowBusy || context == null ||
				context.LocalCapture == null || !context.LocalCapture.Equals(selected.CaptureIdentity)) return;
			CancellationToken token = BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Managing,
				L("Collections.LocalDelete.Checking", "Checking whether the saved backup can be deleted..."));
			_deleteLocalCaptureButton.Text = L("Collections.LocalDelete.CheckingButton", "Checking backup...");
			try
			{
				CollectionsLocalCaptureDeletionStatus status = await Task.Run(() =>
					workflow.GetLocalCaptureDeletionStatus(selected.CaptureIdentity, selected.Capture.SourceTarget), token);
				if (!IsWorkflowContextCurrent(context, token)) return;
				if (status != CollectionsLocalCaptureDeletionStatus.Ready)
				{
					ShowLocalCaptureDeletionBlocked(status);
					RefreshLocalCaptures();
					return;
				}
				using (DevExpressDisplaySettings settings = DevExpressDisplaySettings.CreateFromSettings(Properties.Settings.Default))
				using (var dialog = new CollectionLocalCaptureDeleteDialog(selected, settings))
				{
					if (dialog.ShowDialog(this) != DialogResult.OK)
					{
						_workflowStatusLabel.Text = L("Collections.LocalDelete.Cancelled", "Backup deletion cancelled.");
						return;
					}
				}
				if (!IsWorkflowContextCurrent(context, token)) return;
				_deleteLocalCaptureButton.Text = L("Collections.LocalDelete.DeletingButton", "Deleting backup...");
				_workflowStatusLabel.Text = L("Collections.LocalDelete.Deleting", "Deleting the selected saved backup...");
				SetWorkflowActivity(CollectionWorkflowActivityBuilder.Foreground(CollectionWorkflowActivityPhase.Managing, _workflowStatusLabel.Text));
				status = await Task.Run(() => workflow.DeleteLocalCapture(selected.CaptureIdentity, selected.Capture.SourceTarget, token), token);
				if (!IsWorkflowContextCurrent(context, token)) return;
				RefreshLocalCaptures();
				if (status != CollectionsLocalCaptureDeletionStatus.Deleted)
				{
					ShowLocalCaptureDeletionBlocked(status);
					return;
				}
				_workflowStatusLabel.Text = LanguageManager.Format("Collections.LocalDelete.Completed",
					"Saved Local Collection '{0}' deleted. Your installed mods are unchanged.", selected.DisplayName);
			}
			catch (OperationCanceledException)
			{
				if (IsWorkflowOwnerCurrent(context))
					_workflowStatusLabel.Text = L("Collections.LocalDelete.Cancelled", "Backup deletion cancelled.");
			}
			catch (Exception ex)
			{
				Trace.TraceError("Local Collection backup deletion failed: " + ex);
				RememberTechnicalFailure("local-capture-delete.failed", ex, context);
				if (IsWorkflowContextCurrent(context, token))
				{
					_workflowStatusLabel.Text = L("Collections.LocalDelete.Failed", "The saved backup could not be deleted. See the error for details.");
					XtraMessageBox.Show(this, ex.Message, L("Collections.LocalDelete.FailedTitle", "Backup deletion failed"),
						MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
			finally
			{
				EndWorkflowWork(context);
			}
		}

		/// <summary>Explains the saved-backup dependency that must be resolved before deletion can be reviewed again.</summary>
		private void ShowLocalCaptureDeletionBlocked(CollectionsLocalCaptureDeletionStatus status)
		{
			string message;
			switch (status)
			{
				case CollectionsLocalCaptureDeletionStatus.NotFound:
					message = L("Collections.LocalDelete.Missing", "This saved backup is no longer available. Select another saved Local Collection.");
					break;
				case CollectionsLocalCaptureDeletionStatus.TargetMismatch:
					message = L("Collections.LocalDelete.WrongTarget", "The active game setup has changed. Return to the setup where this backup was saved and select it again.");
					break;
				case CollectionsLocalCaptureDeletionStatus.InstalledCollection:
					message = L("Collections.LocalDelete.Installed", "An installed Collection still needs this saved backup. Uninstall that Collection or stop tracking it before deleting the backup.");
					break;
				case CollectionsLocalCaptureDeletionStatus.ReplacementInProgress:
					message = L("Collections.LocalDelete.Replacement", "A setup replacement is unfinished. Complete its recovery before deleting saved backups for this setup.");
					break;
				default:
					message = L("Collections.LocalDelete.Recovery", "An unfinished Collection operation still needs this backup. Finish or safely stop that operation before deleting the backup.");
					break;
			}
			_workflowStatusLabel.Text = message;
			XtraMessageBox.Show(this, message, L("Collections.LocalDelete.BlockedTitle", "Saved backup cannot be deleted"),
				MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}

		private async void RestoreLocalCaptureButton_Click(object sender, EventArgs e)
		{
			if (_localRestoreRecoverySources.Count > 0)
			{
				await ReviewInterruptedLocalRestoreAsync();
				return;
			}
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
				if (!context.LocalCapture.Equals(preview.Plan.CaptureIdentity) ||
					!context.LocalCapture.Equals(preview.SealedCapture.Capture.Identity) ||
					!context.Revision.Equals(preview.SealedCapture.Capture.Revision))
					throw new InvalidDataException("The restore review source differs from the selected Local Collection.");
				if (!preview.IsReadyForRestore)
				{
					string reasons = preview.Plan.Issues.Count == 0
						? L("Collections.LocalRestore.BlockedUnknown", "The saved capture cannot be restored automatically from the current state.")
						: String.Join(Environment.NewLine, preview.Plan.Issues.Select(x => "- " + CollectionUserMessagePresenter.SanitizeInternalTerminology(x.Message)));
					_workflowStatusLabel.Text = L("Collections.LocalRestore.Blocked", "Workflow: Local Collection restore requires action before it can run.");
					XtraMessageBox.Show(this, reasons, L("Collections.LocalRestore.BlockedTitle", "Local Collection restore blocked"),
						MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}

				string review = LanguageManager.Format("Collections.LocalRestore.ReviewExactSourcePrompt",
					"Restore the saved Local Collection '{0}'?\r\nCapture ID: {4}\r\n\r\nReviewed managed members: {1}\r\nCurrent managed registrations to remove: {2}\r\nManaged file targets to restore: {3}\r\n\r\nThis restores the NMM-managed state recorded by the saved Local Collection. The current NMM profile is preserved before changes begin, and previous Collection tracking is reconciled with the restored setup. Unknown or unmanaged files are not blanket-deleted.\r\n\r\nProceed with these reviewed changes?",
					selected.DisplayName + " - " + selected.RevisionLabel, preview.Plan.Members.Count, preview.Plan.CurrentNativeKeysToRemove.Count,
					preview.Plan.DeploymentTargets.Count, preview.Plan.CaptureIdentity);
				if (XtraMessageBox.Show(this, review, L("Collections.Actions.RestoreLocal", "Restore Local Collection..."),
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
				_localRestoreRecoveryResults = new[] { result };
				RefreshLocalCaptures();
				RefreshManagedAssociations();
				if (result.IsSuccessful)
				{
					_workflowStatusLabel.Text = LanguageManager.Format("Collections.LocalRestore.Completed",
						"Workflow: Local Collection '{0}' restored and fully verified.", selected.DisplayName);
					XtraMessageBox.Show(this, LanguageManager.Format("Collections.LocalRestore.CompletedMessage",
						"'{0}' was restored. Members, owner stacks/payloads, replay artifacts, supported plugin/configuration state, logical metadata and the profile/association boundary all passed final verification.", selected.DisplayName),
						L("Collections.LocalRestore.CompletedTitle", "Local Collection restored"), MessageBoxButtons.OK, MessageBoxIcon.Information);
				}
				else
				{
					CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForLocalRestore(result.Status, result.Message);
					SetWorkflowPresentation(userMessage);
					XtraMessageBox.Show(this, BuildUserDialogMessage(userMessage), L("Collections.LocalRestore.RecoveryTitle", "Local Collection restore requires recovery"),
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
					XtraMessageBox.Show(this, BuildUserDialogMessage(userMessage), L("Collections.LocalRestore.FailedTitle", "Local Collection restore stopped"),
						MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
			finally
			{
				RefreshLocalRestoreRecoverySources();
				EndWorkflowWork(context);
			}
		}

		/// <summary>Requires an explicit choice about the durable restore source before resuming or safely retiring it.</summary>
		private async Task ReviewInterruptedLocalRestoreAsync()
		{
			if (_managementWorkflow == null || _workflowBusy || _localRestoreRecoverySources.Count != 1) return;
			CollectionManagementLocalRestoreRecoverySource source = _localRestoreRecoverySources[0];
			if (!source.HasResolvedCapture || !String.IsNullOrEmpty(source.Diagnostic)) return;
			CollectionUiContext context = CollectionUiContext.CurrentSetup(_previewGeneration);
			string prompt = LanguageManager.Format("Collections.LocalRestore.InterruptedSourcePrompt",
				"Interrupted Local Collection restore:\r\n{0}\r\nCapture ID: {1}\r\nOperation ID: {2}\r\n\r\nYes = resume this exact saved source.\r\n\r\nNo = stop this restore after checking its native recovery boundary. Keep partial installed state, preserved profiles, saved collections and recovery evidence. The restore will be recorded as stopped, and previous Collection tracking as incomplete. You can then review Uninstall Collection or start a fresh restore.\r\n\r\nCancel = leave the operation paused.\r\n\r\nIf this is the wrong source, choose No. Stopping does not roll back changes or claim the setup is healthy.",
				source.SourceDisplayName, source.CaptureIdentity, source.OperationIdentity);
			DialogResult choice = XtraMessageBox.Show(this, prompt, L("Collections.LocalRestore.ReviewInterrupted", "Review interrupted restore..."),
				MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button3);
			if (choice == DialogResult.Cancel || !IsActionContextCurrent(context)) return;
			CancellationToken token = BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Recovering,
				choice == DialogResult.Yes ? L("Collections.LocalRestore.ResumingReviewed", "Resuming the reviewed Local Collection restore...")
				: L("Collections.LocalRestore.StoppingReviewed", "Checking recovery before stopping the Local Collection restore..."));
			try
			{
				CollectionLocalRestoreWorkflowResult result = null;
				if (choice == DialogResult.Yes)
					result = await Task.Run(() => _managementWorkflow.ResumeLocalRestoreAsync(source, token), token);
				else
				{
					CollectionOperation stopped = await Task.Run(() => _managementWorkflow.StopLocalRestoreAsync(source, token), token);
					_stoppedLocalRestoreSource = source;
					result = new CollectionLocalRestoreWorkflowResult(CollectionLocalRestoreWorkflowStatus.StoppedPartial, stopped,
						L("Collections.LocalRestore.StoppedPartialReport", "The reviewed Local restore was safely stopped with partial changes preserved. Recovery inputs and the journal were retained; outgoing Collection tracking is incomplete."));
				}
				if (!IsWorkflowContextCurrent(context, token)) return;
				_localRestoreRecoveryResults = new[] { result };
				RefreshLocalRestoreRecoverySources();
				RefreshLocalCaptures();
				RefreshManagedAssociations();
				string message = choice == DialogResult.No
					? L("Collections.LocalRestore.StoppedPartial", "The restore was stopped after safe-boundary checks. Partial changes remain and previous Collection tracking is incomplete. Select the intended saved Local Collection for a fresh restore, or use Uninstall Collection to review removal of exclusive Collection effects.")
					: result.Message;
				_workflowStatusLabel.Text = message;
				XtraMessageBox.Show(this, message, L("Collections.LocalRestore.ReviewInterrupted", "Review interrupted restore..."),
					MessageBoxButtons.OK, choice == DialogResult.Yes && !result.IsSuccessful ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
			}
			catch (OperationCanceledException)
			{
				if (IsWorkflowOwnerCurrent(context))
					_workflowStatusLabel.Text = L("Collections.LocalRestore.InterruptedPaused", "The interrupted Local restore remains paused; review its recovery state before continuing.");
			}
			catch (Exception ex)
			{
				Trace.TraceError("Interrupted Local restore review failed: " + ex);
				RememberTechnicalFailure("local-restore.review-interrupted-failed", ex, context);
				if (IsWorkflowContextCurrent(context, token))
					XtraMessageBox.Show(this, CollectionUserMessagePresenter.SanitizeInternalTerminology(ex.Message),
						L("Collections.LocalRestore.InterruptedBlocked", "Interrupted restore action blocked"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
			finally
			{
				RefreshLocalRestoreRecoverySources();
				EndWorkflowWork(context);
			}
		}

		/// <summary>Refreshes durable recovery identity after an apply/resume/stop, including failures after operation creation.</summary>
		private void RefreshLocalRestoreRecoverySources()
		{
			try
			{
				_localRestoreRecoverySources = _managementWorkflow == null ? new CollectionManagementLocalRestoreRecoverySource[0]
					: _managementWorkflow.GetInterruptedLocalRestoreSources();
				List<CollectionLocalRestoreWorkflowResult> results = _localRestoreRecoveryResults.Where(x => x.Operation.IsTerminal).ToList();
				foreach (CollectionManagementLocalRestoreRecoverySource source in _localRestoreRecoverySources)
				{
					CollectionLocalRestoreWorkflowResult previous = _localRestoreRecoveryResults.FirstOrDefault(x => x.Operation.Identity.Equals(source.OperationIdentity));
					results.Add(new CollectionLocalRestoreWorkflowResult(previous == null ? CollectionLocalRestoreWorkflowStatus.RecoveryRequired : previous.Status,
						source.Operation, previous == null ? L("Collections.LocalRestore.PendingReview", "The interrupted Local restore is paused until its exact saved source is reviewed.") : previous.Message));
				}
				_localRestoreRecoveryResults = results;
			}
			catch (Exception ex)
			{
				Trace.TraceError("Local restore recovery source refresh failed: " + ex);
			}
		}

		private async void SaveCurrentSetupButton_Click(object sender, EventArgs e)
		{
			if (_captureWorkflow == null || _workflowBusy)
				return;

			PromptDialog nameDialog = PromptDialog.ShowDialog(PromptDialogMode.TextOnly, null, this,
				L("Collections.Capture.NamePrompt", "Name for the Local Collection:"),
				L("Collections.Actions.SaveCurrentSetup", "Save current setup as Local Collection"),
				L("Collections.Capture.DefaultName", "Current setup"), null, null);
			if (nameDialog == null || String.IsNullOrWhiteSpace(nameDialog.EnteredText))
				return;

			LocalCaptureCapability? capabilityChoice = ChooseLocalCaptureCapability();
			if (!capabilityChoice.HasValue)
				return;

			LocalCaptureCapability capability = capabilityChoice.Value;
			_currentSetupActionContext = CollectionUiContext.CurrentSetup(_previewGeneration);
			CollectionUiContext context = _currentSetupActionContext;
			CancellationToken token = BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Capturing, L("Collections.Capture.Saving",
				"Capturing, verifying and retaining the current setup..."));
			try
			{
				var request = new CollectionSaveCurrentSetupRequest(nameDialog.EnteredText, capability);
				var progress = new Progress<CollectionLocalCaptureProgress>(sample =>
				{
					if (!IsWorkflowContextCurrent(context, token))
						return;
					string status = FormatCaptureProgress(sample);
					_workflowStatusLabel.Text = status;
					SetWorkflowActivity(sample.HasDeterminateProgress
						? CollectionWorkflowActivityBuilder.ForegroundProgress(CollectionWorkflowActivityPhase.Capturing,
							status, sample.Current, sample.Total, "local-capture-" + sample.Phase.ToString())
						: CollectionWorkflowActivityBuilder.Foreground(CollectionWorkflowActivityPhase.Capturing, status));
				});
				CollectionSaveCurrentSetupResult result = await _captureWorkflow.SaveCurrentSetupAsync(request, progress, token);
				if (!IsWorkflowContextCurrent(context, token))
					return;

				if (result.IsSaved)
				{
					RefreshLocalCaptures();
					string capabilityLabel = FormatCaptureCapability(result.Capture.Capability);
					_workflowStatusLabel.Text = LanguageManager.Format("Collections.Capture.SavedStatus",
						"Workflow: Local Collection saved - {0}.", capabilityLabel);
					XtraMessageBox.Show(this, LanguageManager.Format("Collections.Capture.SavedMessage",
						"The current setup was saved as '{0}'.\r\n\r\nCapability: {1}",
						result.Definition.DisplayName, capabilityLabel),
						L("Collections.Capture.SavedTitle", "Local Collection saved"), MessageBoxButtons.OK, MessageBoxIcon.Information);
					return;
				}

				string reasons = result.Issues.Count == 0
					? L("Collections.Capture.NotSealedUnknown", "The capture could not be sealed.")
					: String.Join(Environment.NewLine, result.Issues.Select(x => "- " + CollectionUserMessagePresenter.SanitizeInternalTerminology(x.Message)));
				_workflowStatusLabel.Text = L("Collections.Capture.NotSaved", "Workflow: Local Collection was not saved; requested capability could not be sealed.");
				XtraMessageBox.Show(this, LanguageManager.Format("Collections.Capture.NotSavedMessage",
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
					XtraMessageBox.Show(this, BuildUserDialogMessage(userMessage), L("Collections.Capture.FailedTitle", "Local Collection capture failed"),
						MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
			finally
			{
				EndWorkflowWork(context);
			}
		}

		private LocalCaptureCapability? ChooseLocalCaptureCapability()
		{
			using (DevExpressDisplaySettings dialogSettings = DevExpressDisplaySettings.CreateFromSettings(Properties.Settings.Default))
			using (var dialog = new ManagedFontXtraForm())
			using (var fullBackup = new CheckEdit())
			using (var recipeOnly = new CheckEdit())
			using (var save = new SimpleButton())
			using (var cancel = new SimpleButton())
			{
				dialog.Text = L("Collections.Capture.CapabilityTitle", "How should this Local Collection be saved?");
				dialog.StartPosition = FormStartPosition.CenterParent;
				dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
				dialog.MinimizeBox = false;
				dialog.MaximizeBox = false;
				dialog.ShowInTaskbar = false;
				dialog.ClientSize = new Size(610, 315);

				var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(14) };
				layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
				layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				var intro = new LabelControl
				{
					AutoSizeMode = LabelAutoSizeMode.Vertical, MaximumSize = new Size(575, 0),
					Text = L("Collections.Capture.CapabilityIntro", "Choose whether NMM should keep a complete local backup or only the setup recipe.")
				};
				fullBackup.Properties.CheckStyle = CheckStyles.Radio;
				fullBackup.Properties.RadioGroupIndex = 1;
				fullBackup.AutoSize = true;
				fullBackup.Checked = true;
				fullBackup.Text = L("Collections.Capture.FullBackup", "Full local backup (recommended for reliable restore)");
				fullBackup.Margin = new Padding(0, 14, 0, 2);
				var fullDescription = new LabelControl
				{
					AutoSizeMode = LabelAutoSizeMode.Vertical, MaximumSize = new Size(555, 0), Margin = new Padding(22, 0, 0, 8),
					Text = L("Collections.Capture.FullBackupDescription",
						"Copies and verifies the mod archives and managed file data needed to restore this setup without redownloading. Large setups can take several minutes and may use several GB of disk space.")
				};
				recipeOnly.Properties.CheckStyle = CheckStyles.Radio;
				recipeOnly.Properties.RadioGroupIndex = 1;
				recipeOnly.AutoSize = true;
				recipeOnly.Text = L("Collections.Capture.RecipeOnlyChoice", "Setup recipe only (smaller and faster)");
				recipeOnly.Margin = new Padding(0, 8, 0, 2);
				var recipeDescription = new LabelControl
				{
					AutoSizeMode = LabelAutoSizeMode.Vertical, MaximumSize = new Size(555, 0), Margin = new Padding(22, 0, 0, 8),
					Text = L("Collections.Capture.RecipeOnlyDescription",
						"Saves the setup definition and provenance without keeping every required byte. Restoring it may require downloads or manual steps.")
				};
				var note = new LabelControl
				{
					AutoSizeMode = LabelAutoSizeMode.Vertical, MaximumSize = new Size(575, 0),
					Text = L("Collections.Capture.NoMutationNote", "Saving a Local Collection does not change your currently installed mods."),
					Margin = new Padding(0, 8, 0, 0)
				};
				save.Text = L("Collections.Capture.Save", "Save");
				save.AutoSize = true;
				save.DialogResult = DialogResult.OK;
				cancel.Text = L("Common.Cancel", "Cancel");
				cancel.AutoSize = true;
				cancel.DialogResult = DialogResult.Cancel;
				var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
				buttons.Controls.Add(cancel);
				buttons.Controls.Add(save);

				layout.Controls.Add(intro, 0, 0);
				layout.Controls.Add(fullBackup, 0, 1);
				layout.Controls.Add(fullDescription, 0, 2);
				layout.Controls.Add(recipeOnly, 0, 3);
				layout.Controls.Add(recipeDescription, 0, 4);
				layout.Controls.Add(note, 0, 5);
				layout.Controls.Add(buttons, 0, 6);
				dialog.Controls.Add(layout);
				dialog.AcceptButton = save;
				dialog.CancelButton = cancel;

				ApplyCollectionDialogDisplaySettings(dialog, dialogSettings);
				if (dialog.ShowDialog(this) != DialogResult.OK)
					return null;
				return fullBackup.Checked
					? LocalCaptureCapability.LocallyRestorableWithinScope
					: LocalCaptureCapability.RecipeOnly;
			}
		}

		private static string FormatCaptureProgress(CollectionLocalCaptureProgress progress)
		{
			if (progress == null)
				return L("Collections.Capture.Saving", "Capturing the current setup...");
			switch (progress.Phase)
			{
				case CollectionLocalCaptureProgressPhase.ReadingCurrentSetup:
					return L("Collections.Capture.Progress.Reading", "Reading the current NMM setup...");
				case CollectionLocalCaptureProgressPhase.RetainingManagedPayloads:
					return L("Collections.Capture.Progress.ManagedFiles", "Classifying and saving managed file data...");
				case CollectionLocalCaptureProgressPhase.RetainingInstallerState:
					return L("Collections.Capture.Progress.Installer", "Saving installer replay data...");
				case CollectionLocalCaptureProgressPhase.CapturingConfiguration:
					return L("Collections.Capture.Progress.Configuration", "Saving plugin, configuration and user metadata...");
				case CollectionLocalCaptureProgressPhase.RetainingModArchives:
					return L("Collections.Capture.Progress.Archives", "Saving mod archives...");
				case CollectionLocalCaptureProgressPhase.VerifyingRetainedContent:
					return L("Collections.Capture.Progress.Verifying", "Verifying saved backup data...");
				case CollectionLocalCaptureProgressPhase.PublishingCapture:
					return L("Collections.Capture.Progress.Publishing", "Finalizing the Local Collection...");
				default:
					return L("Collections.Capture.Saving", "Capturing the current setup...");
			}
		}

		private static string FormatCaptureCapability(LocalCaptureCapability capability)
		{
			return capability == LocalCaptureCapability.LocallyRestorableWithinScope
				? L("Collections.Capture.Capability.Restorable", "Full local backup")
				: L("Collections.Capture.Capability.RecipeOnly", "Setup recipe only");
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

			using (var dialog = new XtraOpenFileDialog())
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
			if (_workflowBusy || _workflow == null || _snapshot == null || !_snapshot.HasConcreteRevision || context == null)
				return;
			if (FindRevisionUpdateSourceAssociation() != null || FindMatchingInterruptedRevisionUpdate() != null)
			{
				CompareUpdateButton_Click(sender, e);
				return;
			}

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
					!CanResolveNexusSourcePolicyDuringPreparation(selection.CapabilityReport))
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

		private void ResolveFileConflictsButton_Click(object sender, EventArgs e)
		{
			CollectionUiContext context = _incomingActionContext;
			if (_workflow == null || _preparation == null || _preparation.ImpactPlan == null || context == null || _workflowBusy)
				return;

			List<CollectionConflictImpactIssue> issues = _preparation.ImpactPlan.Issues
				.Where(x => x.Kind == CollectionConflictImpactIssueKind.ExistingFileWinnerDecisionRequired)
				.ToList();
			if (issues.Count == 0)
				return;

			int saved = 0;
			foreach (CollectionConflictImpactIssue issue in issues)
			{
				if (!IsActionContextCurrent(context)) return;
				CollectionFileImpact impact = _preparation.ImpactPlan.FileImpacts.SingleOrDefault(x =>
					StringComparer.Ordinal.Equals(x.Target.ToString(), issue.SubjectKey) &&
					x.PlannedWinner != null && Equals(x.PlannedWinner, issue.MemberKey));
				if (impact == null) continue;

				string memberName = FormatMemberSubject(impact.PlannedWinner);
				bool singleWriter = impact.Writers.Count == 1;
				bool canKeepExisting = _workflow.CanKeepExistingManagedFileWinner(_preparation, impact.Target);
				string prompt = canKeepExisting
					? LanguageManager.Format(singleWriter
						? "Collections.ConflictResolution.IncomingWinnerPrompt"
						: "Collections.ConflictResolution.IncomingWinnerMultiWriterPrompt",
						singleWriter
						? "The Collection plans to make {0} the final provider of:\r\n\r\n{1}\r\n\r\nThis file is currently provided by another NMM-managed mod.\r\n\r\nYes = let the Collection member become the final provider.\r\nNo = keep the current managed mod as the final provider while still installing the Collection member underneath it.\r\nCancel = decide later.\r\n\r\nThe decision is stored only for this exact Collection revision, target and current owner. If ownership changes, NMM will require review again."
						: "The Collection plans to make {0} the final Collection provider of:\r\n\r\n{1}\r\n\r\nThis file is currently provided by another NMM-managed mod and several Collection members also write it.\r\n\r\nYes = let the reviewed Collection winner replace the current managed provider.\r\nNo = keep the current managed mod as the final provider and keep the reviewed Collection winner directly underneath it as the managed fallback.\r\nCancel = decide later.\r\n\r\nThe decision is stored only for this exact Collection revision, target and current owner. If ownership changes, NMM will require review again.",
						memberName, impact.Target.ToString())
					: LanguageManager.Format("Collections.ConflictResolution.IncomingWinnerUnsupportedExistingPrompt",
						"The Collection plans to make {0} the final provider of:\r\n\r\n{1}\r\n\r\nThe current provider is not an active NMM mod owner that C9 can safely re-select (for example, it may be an original/unresolved fallback).\r\n\r\nYes = let the Collection member become the final provider.\r\nNo = leave this conflict unresolved.\r\nCancel = stop reviewing conflicts for now.",
						memberName, impact.Target.ToString());
				DialogResult choice = XtraMessageBox.Show(this, prompt, L("Collections.Actions.ResolveFileConflicts", "Resolve file conflicts..."),
					MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
				if (choice == DialogResult.Cancel) break;

				try
				{
					if (choice == DialogResult.Yes)
					{
						_workflow.AuthorizeIncomingFileWinner(_preparation, impact.Target, "Incoming Collection winner authorized from the Collections file-conflict review UI.");
						saved++;
					}
					else if (canKeepExisting)
					{
						_workflow.KeepExistingManagedFileWinner(_preparation, impact.Target, "Existing managed winner preserved from the Collections file-conflict review UI.");
						saved++;
					}
				}
				catch (Exception ex)
				{
					Trace.TraceError("Collection file-conflict decision failed: " + ex);
					RememberTechnicalFailure("conflict-resolution.file-winner", ex, context);
					CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForFailure("conflict-resolution.file-winner", ex.Message);
					SetWorkflowPresentation(userMessage);
					AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic,
						L("Collections.Status.ActionRequired", "Action required"), "conflict-resolution.file-winner",
						impact.Target.ToString(), userMessage, impact.PlannedWinner);
					break;
				}
			}

			if (saved > 0)
			{
				_workflowStatusLabel.Text = LanguageManager.Format("Collections.ConflictResolution.DecisionsSaved",
					"Workflow: saved {0} file-conflict decision(s). Choose Download / Prepare again to rebuild the exact review.", saved);
				AddReviewItem(CollectionReviewSeverity.Info, CollectionReviewItemKind.Progress,
					L("Collections.Status.Supported", "Saved"), "conflict-resolution.saved", GetCurrentCollectionSubject(),
					LanguageManager.Format("Collections.ConflictResolution.SavedExplanation",
						"{0} durable file-conflict decision(s) were saved. Rebuild preparation to apply them to a fresh native-state observation.", saved));
				_resolveFileConflictsButton.Enabled = false;
				_downloadPrepareButton.Enabled = true;
				UpdateIssuesHeader();
			}
		}

		private async void ResumeButton_Click(object sender, EventArgs e)
		{
			CollectionUiContext context = _incomingActionContext;
			if (_workflowBusy || _workflow == null || !CanResumePreparation(_acquisitionBatch) || context == null)
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
			if (!CanResumePreparation(batch) || batch.IsReady || HasManualAcquisitionAction(batch))
				return;

			if (GetQueuedAcquisitionStates(batch).Count > 0)
				_acquisitionRefreshTimer.Start();
		}

		private async void AcquisitionRefreshTimer_Tick(object sender, EventArgs e)
		{
			RefreshWorkflowActivity();
			if (_workflowBusy || _workflow == null || !CanResumePreparation(_acquisitionBatch))
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
				 x.Disposition == CollectionMemberAcquisitionDisposition.BundledQueued ||
				 x.Disposition == CollectionMemberAcquisitionDisposition.DirectQueued) &&
				x.QueueCorrelation != null && x.QueueCorrelation.Task != null).ToList();
		}

		private async void OpenPendingButton_Click(object sender, EventArgs e)
		{
			CollectionUiContext context = _incomingActionContext;
			CollectionManualAcquisitionPendingAction pending = GetSelectedOrFirstPendingAction();
			if (_workflowBusy || context == null || !IsActionContextCurrent(context) || pending == null)
				return;

			string nexusDomain;
			long nexusModId;
			long nexusFileId;
			bool isNexusPending = NexusCollectionModFileArtifactIdentity.TryParse(pending.Request.SelectedArtifact,
				out nexusDomain, out nexusModId, out nexusFileId);
			try
			{
				if (pending.Supports(CollectionManualAcquisitionActionKind.Browser) && pending.BrowserUri != null)
					Process.Start(pending.BrowserUri.ToString());
			}
			catch (Exception ex)
			{
				RememberTechnicalFailure("acquisition.open-page-failed", ex, context);
				CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForFailure("acquisition.open-page-failed", ex.Message);
				AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.ManualAction, L("Collections.Status.ActionRequired", "Action required"),
					"acquisition.open-page-failed", FormatMemberSubject(pending.Request.MemberKey), userMessage, pending.Request.MemberKey,
					"Member: " + pending.Request.MemberKey);
				return;
			}

			// Preserve the existing Nexus/NXM interaction: opening the provider page remains the complete button action.
			// Characterized manual/browse artifacts continue to the local picker because their bytes must be supplied explicitly.
			if (isNexusPending || !pending.Supports(CollectionManualAcquisitionActionKind.LocalFile))
				return;

			if (!String.IsNullOrWhiteSpace(pending.Instructions))
			{
				XtraMessageBox.Show(this, pending.Instructions,
					L("Collections.Acquisition.DownloadInstructionsTitle", "Collection download instructions"),
					MessageBoxButtons.OK, MessageBoxIcon.Information);
				if (!IsActionContextCurrent(context))
					return;
			}

			using (var dialog = new XtraOpenFileDialog())
			{
				dialog.Title = L("Collections.Acquisition.SelectArchiveTitle", "Select the exact downloaded mod archive");
				dialog.CheckFileExists = true;
				dialog.CheckPathExists = true;
				dialog.Multiselect = false;
				dialog.Filter = L("Collections.Acquisition.SelectArchiveFilter", "Mod archives|*.zip;*.7z;*.rar|All files|*.*");
				if (dialog.ShowDialog(this) != DialogResult.OK || !IsActionContextCurrent(context))
					return;

				CancellationToken token = BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Preparing,
					L("Collections.Workflow.VerifyingSelectedArchive", "Verifying the selected archive and resuming Collection preparation..."));
				try
				{
					CollectionVerifiedArchive verified;
					bool revisionUpdatePending = _revisionUpdatePreparation != null && _revisionUpdatePreparation.Members.Any(x =>
						x.PendingAction != null && x.PendingAction.ActionId == pending.ActionId);
					if (revisionUpdatePending)
					{
						if (_revisionUpdateWorkflow == null || _revisionUpdateReview == null)
							throw new InvalidOperationException("The pending revision-update acquisition no longer has its approved review context.");
						verified = _revisionUpdateWorkflow.VerifyLocalFile(_revisionUpdatePreparation, pending, dialog.FileName, token);
						if (verified == null)
						{
							ShowLocalArchiveMismatch(pending);
							return;
						}
						CollectionRevisionUpdateWorkflowResult updateResult = await _revisionUpdateWorkflow.ProbePreparationAndContinueAsync(
							_revisionUpdatePreparation, _revisionUpdateReview, token);
						if (!IsWorkflowContextCurrent(context, token)) return;
						_revisionUpdateResult = updateResult;
						_revisionUpdatePreparation = updateResult.Preparation;
						RenderRevisionUpdateWorkflowResult(updateResult);
					}
					else
					{
						if (_workflow == null || _acquisitionBatch == null)
							throw new InvalidOperationException("The pending Collection acquisition no longer has its preparation context.");
						verified = _workflow.VerifyLocalFile(_acquisitionBatch, pending, dialog.FileName, token);
						if (verified == null)
						{
							ShowLocalArchiveMismatch(pending);
							return;
						}
						CollectionAdditiveWorkflowPreparationResult result = await _workflow.ResumePreparationAsync(_acquisitionBatch, token);
						if (!IsWorkflowContextCurrent(context, token)) return;
						RenderPreparationResult(result);
					}
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception ex)
				{
					Trace.TraceError("Collection local archive verification failed: " + ex);
					RememberTechnicalFailure("acquisition.local-file-failed", ex, context);
					if (IsWorkflowContextCurrent(context, token))
					{
						CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForFailure("acquisition.local-file-failed", ex.Message);
						AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.ManualAction,
							L("Collections.Status.ActionRequired", "Action required"), "acquisition.local-file-failed",
							FormatMemberSubject(pending.Request.MemberKey), userMessage, pending.Request.MemberKey);
					}
				}
				finally
				{
					EndWorkflowWork(context);
				}
			}
		}

		private void ShowLocalArchiveMismatch(CollectionManualAcquisitionPendingAction pending)
		{
			string explanation = L("Collections.Acquisition.LocalArchiveMismatch",
				"The selected archive does not match the exact file identity declared by this Collection member. No installation work was started.");
			AddReviewItem(CollectionReviewSeverity.Warning, CollectionReviewItemKind.ManualAction,
				L("Collections.Status.ActionRequired", "Action required"), "acquisition.local-file-mismatch",
				FormatMemberSubject(pending.Request.MemberKey), explanation, pending.Request.MemberKey,
				L("Collections.Acquisition.SelectCorrectArchive", "Select the exact archive required by the Collection and try again."));
			_workflowStatusLabel.Text = LanguageManager.Format("Collections.Workflow.UserStatus", "Workflow: {0}", explanation);
			UpdateIssuesHeader();
		}

		private async void ReplaceButton_Click(object sender, EventArgs e)
		{
			if (_workflowBusy || _replacementWorkflow == null)
				return;
			if (_replacementReview != null)
			{
				await ContinueReplacementAsync();
				return;
			}
			if (_workflow == null || _preparation == null ||
				!_preparation.IsReadyForReview || _preparation.Runtime == null || _selectionDirty)
				return;

			CollectionUiContext context = _incomingActionContext;
			if (context == null || !IsActionContextCurrent(context))
				return;

			CollectionReviewedWorkflowRuntime acquiredRuntime = _preparation.Runtime;
			CollectionReplacementBackupChoice backupChoice = _replacementBackupCheckBox.Checked
				? CollectionReplacementBackupChoice.CreateLocalCollection
				: CollectionReplacementBackupChoice.ContinueWithoutLocalCollection;

			string preflight = L("Collections.Replace.PreflightWarning",
				"Replace current managed setup is destructive. NMM will build a separate exact replacement review before removing anything. Unknown/unmanaged content is preserved within the observed scope. Continue to build the replacement review?");
			if (XtraMessageBox.Show(this, preflight, L("Collections.Actions.ReplaceCurrent", "Replace current managed setup..."),
				MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
				return;

			CancellationToken token = BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Reviewing,
				L("Collections.Replace.PreparingReview", "Building exact replacement review..."));
			try
			{
				// Download / Prepare owns an additive review journal. Replacement is a distinct operation, so retire that
				// pre-mutation journal only after preserving its immutable runtime/archive evidence in memory.
				if (_operationIdentity != null && _operationSnapshot != null &&
					_operationSnapshot.Kind == CollectionOperationKind.ApplyResolvedPlan && !_operationSnapshot.HasCrossedNativeBoundary && !_operationSnapshot.IsTerminal)
					_workflow.CancelBeforeApply(_operationIdentity);

				CollectionReplacementWorkflowReview review = await Task.Run(() =>
					_replacementWorkflow.PrepareReviewAsync(acquiredRuntime, backupChoice, token), token);
				if (!IsWorkflowContextCurrent(context, token))
					return;

				_replacementReview = review;
				_operationIdentity = review.Operation.Identity;
				_operationSnapshot = review.Operation;
				_reviewedPlanIdentity = review.Plan.Identity;
				BindIncomingDisplayOperation(review.Operation.Identity);
				RenderReplacementReview(review);

				string confirmation = BuildReplacementApprovalConfirmation(review);
				if (XtraMessageBox.Show(this, confirmation, L("Collections.Actions.ReplaceCurrent", "Replace current managed setup..."),
					MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
				{
					_operationSnapshot = _replacementWorkflow.CancelBeforeApply(review);
					_replacementReview = null;
					_operationIdentity = null;
					_reviewedPlanIdentity = null;
					ClearIncomingDisplayOperation();
					_workflowStatusLabel.Text = L("Collections.Replace.Cancelled", "Workflow: replacement review cancelled before native mutation. Download / Prepare can be run again to build another review.");
					return;
				}

				SetWorkflowActivity(CollectionWorkflowActivityBuilder.Foreground(CollectionWorkflowActivityPhase.Applying,
					L("Collections.Replace.Applying", "Applying the approved replacement transition...")));
				CollectionReplacementWorkflowApplyResult result = await Task.Run(() =>
					_replacementWorkflow.ApproveAndApplyAsync(review, token), token);
				if (!IsWorkflowContextCurrent(context, token))
					return;
				_operationSnapshot = result.Operation;
				RenderReplacementApplyResult(result);
				if (result.IsCommitted)
				{
					_preparation = null;
					_acquisitionBatch = null;
					_replacementReview = null;
					RefreshManagedAssociations(true);
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection replacement failed: " + ex);
				RememberTechnicalFailure("replacement.failed", ex, context);
				if (IsWorkflowContextCurrent(context, token))
				{
					CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForFailure("replacement.failed", ex.Message);
					SetWorkflowPresentation(userMessage);
					AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic,
						L("Collections.Status.ActionRequired", "Action required"), "replacement.failed", GetCurrentCollectionSubject(), userMessage);
				}
			}
			finally
			{
				EndWorkflowWork(context);
			}
		}

		private async Task ContinueReplacementAsync()
		{
			CollectionReplacementWorkflowReview review = _replacementReview;
			CollectionUiContext context = _incomingActionContext;
			if (review == null || context == null || !IsActionContextCurrent(context))
				return;
			CollectionOperation operation = _operationSnapshot ?? review.Operation;
			if (operation.Phase == CollectionOperationPhase.AwaitingReplacementPhaseAmendment ||
				operation.Phase == CollectionOperationPhase.ReplacementBarrierRevalidationRequired)
			{
				XtraMessageBox.Show(this,
					L("Collections.Replace.AmendmentRequired", "The live state changed after destructive replacement work began. The durable replacement journal requires a new exact phase review before further incoming mutation. No broader consent will be inferred automatically."),
					L("Collections.Status.ActionRequired", "Action required"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			CancellationToken token = BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Recovering,
				L("Collections.Replace.Continuing", "Checking replacement recovery and verified phase boundaries..."));
			try
			{
				if (operation.RequiresRecovery || operation.ResultState == CollectionOperationResultState.RecoveryRequired ||
					operation.Phase == CollectionOperationPhase.Recovering || operation.Phase == CollectionOperationPhase.RecoveryRequired)
				{
					CollectionReplacementRecoveryResult recovery = await Task.Run(() => _replacementWorkflow.RecoverAsync(review, token), token);
					if (!IsWorkflowContextCurrent(context, token)) return;
					_operationSnapshot = recovery.Operation;
					if (recovery.IsRolledBack)
					{
						_workflowStatusLabel.Text = L("Collections.Replace.RolledBack", "Workflow: replacement recovery verified the protected original managed setup.");
						_replacementReview = null;
						_operationIdentity = null;
						_reviewedPlanIdentity = null;
						ClearIncomingDisplayOperation();
						RefreshManagedAssociations(true);
						return;
					}
					if (recovery.RequiresRecovery)
					{
						_workflowStatusLabel.Text = L("Collections.Replace.Recovery", "Workflow: replacement still requires explicit recovery before further managed mutation.");
						return;
					}
				}

				CollectionReplacementWorkflowApplyResult result = await Task.Run(() => _replacementWorkflow.ResumeAsync(review, token), token);
				if (!IsWorkflowContextCurrent(context, token)) return;
				_operationSnapshot = result.Operation;
				RenderReplacementApplyResult(result);
				if (result.IsCommitted)
				{
					_replacementReview = null;
					_preparation = null;
					_acquisitionBatch = null;
					RefreshManagedAssociations(true);
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection replacement continuation failed: " + ex);
				RememberTechnicalFailure("replacement.continue-failed", ex, context);
				if (IsWorkflowContextCurrent(context, token))
					SetWorkflowPresentation(CollectionUserMessagePresenter.ForFailure("replacement.continue-failed", ex.Message));
			}
			finally
			{
				EndWorkflowWork(context);
			}
		}

		private void RenderReplacementReview(CollectionReplacementWorkflowReview review)
		{
			ClearReviewItems();
			int removals = review.Diff.NativeMods.Count(x => x.RemovalDecision == CollectionReplacementRemovalDecision.EligibleForReviewedRemoval ||
				x.RemovalDecision == CollectionReplacementRemovalDecision.RequiresExplicitReview);
			int protectedMods = review.Diff.NativeMods.Count(x => x.RemovalDecision == CollectionReplacementRemovalDecision.Protected);
			int incomingChanges = review.Diff.IncomingMembers.Count(x => x.Disposition == CollectionReplacementDiffDisposition.IncomingOnly ||
				x.Disposition == CollectionReplacementDiffDisposition.ReinstallOrChange);
			int outgoingAssociations = review.Diff.Associations.Count(x => x.Disposition != CollectionReplacementAssociationDisposition.SurvivesCompatible);
			AddReviewItem(CollectionReviewSeverity.Warning, CollectionReviewItemKind.Diagnostic,
				L("Collections.Replace.DestructiveReview", "Destructive replacement"), "replacement.review.removals", GetCurrentCollectionSubject(),
				LanguageManager.Format("Collections.Replace.RemovalCount", "{0} managed mod instance(s) are approved for outgoing removal.", removals));
			AddReviewItem(CollectionReviewSeverity.Info, CollectionReviewItemKind.Diagnostic,
				L("Collections.Replace.Protected", "Protected / retained"), "replacement.review.protected", GetCurrentCollectionSubject(),
				LanguageManager.Format("Collections.Replace.ProtectedCount", "{0} managed mod instance(s) are retained or protected; {1} incoming member(s) require install/reinstall.", protectedMods, incomingChanges));
			AddReviewItem(CollectionReviewSeverity.Info, CollectionReviewItemKind.Diagnostic,
				L("Collections.Replace.Associations", "Association transitions"), "replacement.review.associations", GetCurrentCollectionSubject(),
				LanguageManager.Format("Collections.Replace.AssociationCount", "{0} outgoing Collection association(s) will transition out after aggregate verification succeeds.", outgoingAssociations));
			if (review.Plan.Policy.ReplacementBackupChoice == CollectionReplacementBackupChoice.CreateLocalCollection)
				AddReviewItem(CollectionReviewSeverity.Info, CollectionReviewItemKind.Diagnostic,
					L("Collections.Replace.Backup", "Local Collection backup"), "replacement.review.backup", GetCurrentCollectionSubject(),
					L("Collections.Replace.BackupRequested", "A persistent Local Collection backup will be sealed before outgoing managed effects are removed. Mandatory operation recovery is prepared separately."));
			else
				AddReviewItem(CollectionReviewSeverity.Warning, CollectionReviewItemKind.Diagnostic,
					L("Collections.Replace.NoBackup", "No persistent Local Collection backup"), "replacement.review.no-backup", GetCurrentCollectionSubject(),
					L("Collections.Replace.NoBackupDetail", "No saved Local Collection was requested. NMM will still prepare mandatory operation-owned recovery data before removal."));
			AppendFolderCorrectionReview(review.PreparedRecipes);
			_workflowStatusLabel.Text = L("Collections.Replace.ReviewReady", "Workflow: exact replacement review is ready. Nothing has been removed yet.");
		}

		private string BuildReplacementApprovalConfirmation(CollectionReplacementWorkflowReview review)
		{
			int removals = review.Diff.NativeMods.Count(x => x.RemovalDecision == CollectionReplacementRemovalDecision.EligibleForReviewedRemoval ||
				x.RemovalDecision == CollectionReplacementRemovalDecision.RequiresExplicitReview);
			int installs = review.Diff.IncomingMembers.Count(x => x.Disposition == CollectionReplacementDiffDisposition.IncomingOnly ||
				x.Disposition == CollectionReplacementDiffDisposition.ReinstallOrChange);
			var text = new StringBuilder();
			text.AppendLine(L("Collections.Replace.ConfirmationIntro", "Approve this exact replacement transition?"));
			text.AppendLine();
			text.AppendLine(LanguageManager.Format("Collections.Replace.ConfirmationRemove", "Outgoing managed mod instances to remove: {0}", removals));
			text.AppendLine(LanguageManager.Format("Collections.Replace.ConfirmationIncoming", "Incoming installs/reinstalls: {0}", installs));
			text.AppendLine(review.Plan.Policy.ReplacementBackupChoice == CollectionReplacementBackupChoice.CreateLocalCollection
				? L("Collections.Replace.ConfirmationBackup", "Persistent Local Collection backup: Yes")
				: L("Collections.Replace.ConfirmationNoBackup", "Persistent Local Collection backup: No (mandatory operation recovery still required)"));
			text.AppendLine();
			text.AppendLine(L("Collections.Replace.ConfirmationFinal", "After approval, NMM may remove reviewed managed effects. If the live state differs from the approved projection, replacement stops for review or recovery instead of silently widening consent."));
			return text.ToString();
		}

		private void RenderReplacementApplyResult(CollectionReplacementWorkflowApplyResult result)
		{
			if (result == null)
				return;
			switch (result.Status)
			{
				case CollectionReplacementWorkflowApplyStatus.Committed:
					_workflowStatusLabel.Text = L("Collections.Replace.Completed", "Workflow: replacement completed and aggregate native verification passed.");
					_appliedValue.Text = L("Collections.Status.Applied.Applied", "Applied");
					break;
				case CollectionReplacementWorkflowApplyStatus.ExplicitReviewRequired:
					_workflowStatusLabel.Text = L("Collections.Replace.ReviewAgain", "Workflow: replacement paused because the verified post-mutation state requires a new explicit review.");
					_appliedValue.Text = L("Collections.Status.ActionRequired", "Action required");
					break;
				case CollectionReplacementWorkflowApplyStatus.PausedAtSafeBoundary:
					_workflowStatusLabel.Text = L("Collections.Replace.Paused", "Workflow: replacement paused at a verified safe boundary and can be recovered/resumed.");
					_appliedValue.Text = L("Collections.Status.Pending", "Pending");
				break;
				default:
					_workflowStatusLabel.Text = L("Collections.Replace.Recovery", "Workflow: replacement requires recovery before further managed mutation.");
					_appliedValue.Text = L("Collections.Status.Applied.RecoveryRequired", "Recovery required before further mutation");
				break;
			}
			if (!String.IsNullOrWhiteSpace(result.Message))
				_summaryBox.Text = result.Message;
		}

		private async void InstallButton_Click(object sender, EventArgs e)
		{
			CollectionUiContext context = GetInstallActionContext();
			if (_workflowBusy || context == null)
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
				_operationSnapshot = review.Operation;
				if (review.Rehydration.Status == CollectionReviewedWorkflowRehydrationStatus.RecoveryRequired)
				{
					_workflowStatusLabel.Text = FormatActiveTargetRecoveryActivity();
					SetWorkflowActivity(CollectionWorkflowActivityBuilder.Foreground(CollectionWorkflowActivityPhase.Recovering,
						_workflowStatusLabel.Text));
					IReadOnlyList<CollectionAdditiveWorkflowRecoveryResult> recovery = await Task.Run(() =>
						_workflow.ReconcileIncompleteTargetAsync(reviewToken), reviewToken);
					if (!IsWorkflowContextCurrent(context, reviewToken))
						return;
					_recoveryResults = recovery ?? new CollectionAdditiveWorkflowRecoveryResult[0];
					CollectionAdditiveWorkflowRecoveryResult matching = _recoveryResults.FirstOrDefault(x => x.Operation.Identity.Equals(reviewedOperationIdentity));
					if (matching != null)
						_operationSnapshot = matching.Operation;
					if (matching != null && matching.Operation.IsTerminal)
					{
						CacheRecoveryResult(matching);
						RefreshManagedAssociations(true);
						ShowSelectedManagedAssociation();
						SetWorkflowPresentation(CollectionUserMessagePresenter.ForRecovery(matching.Status, matching.Message));
						return;
					}
					review = await Task.Run(() => _workflow.GetReview(reviewedOperationIdentity), reviewToken);
					if (!IsWorkflowContextCurrent(context, reviewToken))
						return;
				}
			}
			catch (OperationCanceledException)
			{
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
				_operationSnapshot = review.Operation;
				bool requiresRecovery = review.Rehydration.Status == CollectionReviewedWorkflowRehydrationStatus.RecoveryRequired;
				CollectionAdditiveWorkflowRecoveryStatus status = requiresRecovery
					? CollectionAdditiveWorkflowRecoveryStatus.RecoveryRequired : CollectionAdditiveWorkflowRecoveryStatus.RepreparationRequired;
				CollectionAdditiveWorkflowRecoveryResult recovery = _recoveryResults.FirstOrDefault(x =>
					x.Operation.Identity.Equals(review.Operation.Identity) && x.Status == status &&
					x.Operation.CheckpointSequence == review.Operation.CheckpointSequence);
				string reviewMessage = recovery == null ? review.Rehydration.Message : recovery.Message;
				CacheRecoveryResult(new CollectionAdditiveWorkflowRecoveryResult(status, review.Operation, review.Rehydration, reviewMessage));
				_reviewedPlanIdentity = requiresRecovery ? review.Operation.PlanIdentity : null;
				if (_snapshot != null)
					RenderIssues(_snapshot, false, true);
				CollectionUserMessagePresentation userMessage = requiresRecovery
					? CollectionUserMessagePresenter.ForRecovery(status, reviewMessage)
					: CollectionUserMessagePresenter.FromRaw(reviewMessage,
						ResolveNextAction(CollectionUserMessagePresenter.ForRecovery(status, reviewMessage)));
				SetWorkflowPresentation(userMessage);
				AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic,
					L("Collections.Status.ActionRequired", "Action required"), "workflow.review-invalid", GetCurrentCollectionSubject(), userMessage);
				_appliedValue.Text = requiresRecovery
					? L("Collections.Status.Applied.RecoveryRequired", "Recovery required before further mutation")
					: L("Collections.Status.Applied.Reprepare", "Not applied - preparation must be rebuilt");
				if (!requiresRecovery && !review.Operation.HasCrossedNativeBoundary)
				{
					_preparation = null;
					_acquisitionBatch = null;
				}
				RefreshWorkflowActivity();
				UpdateActionButtons();
				return;
			}

			_operationSnapshot = review.Operation;
			CacheRecoveryResult(new CollectionAdditiveWorkflowRecoveryResult(CollectionAdditiveWorkflowRecoveryStatus.ReadyToResume,
				review.Operation, review.Rehydration, review.Rehydration.Message));
			RenderExactReview(review);
			string confirmation = BuildApprovalConfirmation(review);
			if (XtraMessageBox.Show(this, confirmation, L("Collections.Actions.InstallCurrent", "Review and install..."),
				MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
				return;
			if (!IsActionContextCurrent(context))
			{
				XtraMessageBox.Show(this,
					L("Collections.Messages.Apply.ContextChanged", "The displayed Collection changed while the approval dialog was open. No new installation was submitted. Reopen the intended revision and prepare its review again."),
					L("Collections.Status.ActionRequired", "Action required"), MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			CancellationToken token = BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Applying, L("Collections.Workflow.Applying", "Applying the approved Collection plan..."));
			try
			{
				CollectionAdditiveWorkflowApplyResult result = await Task.Run(() =>
					_workflow.ApproveAndApplyAsync(reviewedOperationIdentity, reviewedPlanIdentity, token), token);
				if (!IsWorkflowContextCurrent(context, token))
				{
					Trace.TraceWarning("Collection apply returned after the visible preview changed; native result must be inspected from the current target state.");
					if (!IsDisposed && !Disposing)
						XtraMessageBox.Show(this,
							L("Collections.Messages.Apply.ViewChanged", "The Collection view changed while installation was running. NMM has not assumed that installation failed or succeeded. Check the installed Collection and its Technical Report before trying again."),
							L("Collections.Status.ActionRequired", "Action required"), MessageBoxButtons.OK, MessageBoxIcon.Information);
					return;
				}
				RenderApplyResult(result);
				if (result.IsCommitted)
					_managedMemberExpansionAssociationId = null;
				RefreshManagedAssociations(true);
				ShowAdditiveApplyFeedback(result);
			}
			catch (OperationCanceledException)
			{
				if (IsWorkflowOwnerCurrent(context))
				{
					_workflowStatusLabel.Text = L("Collections.Workflow.CancelRequested", "Workflow: cancellation requested; NMM will reconcile the installed state before any further work.");
					XtraMessageBox.Show(this, _workflowStatusLabel.Text,
						L("Collections.Messages.Apply.PausedTitle", "Collection installation paused"), MessageBoxButtons.OK, MessageBoxIcon.Information);
				}
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
					string reason = CollectionTechnicalReportSanitizer.SanitizeText(ex.Message);
					XtraMessageBox.Show(this, BuildUserDialogMessage(userMessage) +
						(String.IsNullOrWhiteSpace(reason) ? String.Empty : Environment.NewLine + Environment.NewLine + reason),
						L("Collections.Messages.Apply.StoppedTitle", "Collection installation stopped"), MessageBoxButtons.OK, MessageBoxIcon.Error);
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
				RefreshInstalledState();
				report = BuildTechnicalReportSnapshot();
			}
			catch (Exception ex)
			{
				Trace.TraceError("Collection technical report snapshot failed: " + ex);
				XtraMessageBox.Show(this,
					L("Collections.TechnicalReport.BuildFailed", "NMM could not build the technical report for the current Collection state."),
					L("Collections.TechnicalReport.FailedTitle", "Technical report failed"),
					MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			using (var dialog = new XtraSaveFileDialog
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
					XtraMessageBox.Show(this,
						L("Collections.TechnicalReport.SaveFailed", "NMM could not save the technical report to the selected location."),
						L("Collections.TechnicalReport.FailedTitle", "Technical report failed"),
						MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
		}

		private CollectionTechnicalReportSnapshot BuildTechnicalReportSnapshot()
		{
			if (_snapshot == null && _managedAssociationPresentation == null && _completedRemovalReport != null)
				return _completedRemovalReport;
			string version = typeof(CollectionsPreviewControl).Assembly.GetName().Version == null
				? String.Empty
				: typeof(CollectionsPreviewControl).Assembly.GetName().Version.ToString();
			var report = new CollectionTechnicalReportSnapshot(version);
			report.Performance = CollectionPerformanceMetrics.Capture();
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
			if (_workflow != null || _managementWorkflow != null)
			{
				try { targetPaths = _workflow != null ? _workflow.GetTargetPaths() : _managementWorkflow.GetTargetPaths(); }
				catch (Exception ex) { report.UnavailableData.Add("target-context: " + CollectionTechnicalReportSanitizer.SanitizeText(ex.Message)); }
			}
			string targetFingerprint = _operationSnapshot == null ? null : _operationSnapshot.Target.ToString();
			if (targetFingerprint == null && _managedAssociationPresentation != null)
				targetFingerprint = _managedAssociationPresentation.Association.Association.Target.ToString();
			if (targetFingerprint == null && _recoveryResults.Count > 0)
				targetFingerprint = _recoveryResults[0].Operation.Target.ToString();
			if (targetFingerprint == null && _localRestoreRecoveryResults.Count > 0)
				targetFingerprint = _localRestoreRecoveryResults[0].Operation.Target.ToString();
			if (targetFingerprint == null && _managementWorkflow != null)
			{
				try { targetFingerprint = _managementWorkflow.GetReportTargetIdentity().Fingerprint; }
				catch (Exception ex) { report.UnavailableData.Add("target-fingerprint: " + CollectionTechnicalReportSanitizer.SanitizeText(ex.Message)); }
			}
			string fallbackGameId = _snapshot != null && _snapshot.Link != null ? _snapshot.Link.GameDomain :
				(_managedAssociationPresentation == null ? null : _managedAssociationPresentation.NexusGameDomain);
			report.Target = new CollectionTechnicalReportTarget
			{
				GameId = CollectionTechnicalReportSanitizer.SanitizeText(targetPaths == null ? fallbackGameId : targetPaths.GameId),
				GameName = targetPaths == null ? null : CollectionTechnicalReportSanitizer.SanitizeText(targetPaths.GameName),
				TargetFingerprint = targetFingerprint
			};
			if (targetPaths == null)
				report.UnavailableData.Add(_workflow == null && _managementWorkflow == null
					? "target-context: unavailable because no Collection application service is connected"
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

			foreach (MemberGridRow row in _memberRows)
			{
				NormalizedCollectionMember member = row.Member;
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
					Selected = member.IsRequired || row.Selected,
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
			if (operation.Kind == CollectionOperationKind.UpdateRevision && _revisionUpdateWorkflow != null)
			{
				try
				{
					CollectionOperation current = _revisionUpdateWorkflow.ReadCurrentOperation(operation.Identity);
					if (!current.Target.Equals(operation.Target) || !Equals(current.Revision, operation.Revision) ||
						!Equals(current.PlanIdentity, operation.PlanIdentity))
						throw new InvalidOperationException("The current revision-update journal no longer matches the displayed operation scope.");
					operation = current;
				}
				catch (Exception ex)
				{
					report.UnavailableData.Add("revision-update-journal: " + CollectionTechnicalReportSanitizer.SanitizeText(ex.Message));
				}
			}
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
			{
				CollectionManagementLocalRestoreRecoverySource source = FindLocalRestoreRecoverySource(result.Operation.Identity);
				report.Recovery.Add(new CollectionTechnicalReportRecoveryItem
				{
					Scope = "local-restore",
					Status = result.Status.ToString(),
					OperationIdentity = result.Operation.Identity.ToString(),
					Phase = result.Operation.Phase.ToString(),
					ResultState = result.Operation.ResultState.ToString(),
					CaptureIdentity = source == null || source.CaptureIdentity == null ? null : source.CaptureIdentity.ToString(),
					CaptureDisplayName = source == null ? null : CollectionTechnicalReportSanitizer.SanitizeText(source.DisplayName),
					CaptureRevisionLabel = source == null ? null : CollectionTechnicalReportSanitizer.SanitizeText(source.RevisionLabel),
					SourceDiagnostic = source == null ? null : CollectionTechnicalReportSanitizer.SanitizeText(source.Diagnostic),
					Message = CollectionTechnicalReportSanitizer.SanitizeText(result.Message)
				});
			}
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
			return _workflow != null || _managementWorkflow != null || _snapshot != null || _managedAssociationPresentation != null ||
				_operationSnapshot != null || _completedRemovalReport != null ||
				_reviewItems.Count > 0 || _recoveryResults.Count > 0 || _localRestoreRecoveryResults.Count > 0 ||
				_revisionUpdateRecoveryResults.Count > 0 || _verifyRepairRecoveryResults.Count > 0;
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

		/// <summary>Names the Collection whose interrupted work is being checked without exposing filesystem paths.</summary>
		private string FormatActiveTargetRecoveryActivity()
		{
			string collectionName = GetCurrentCollectionSubject();
			if (String.IsNullOrWhiteSpace(collectionName) || StringComparer.CurrentCulture.Equals(collectionName, L("Collections.Review.CurrentCollection", "Current Collection")))
				return L("Collections.Workflow.Recovering", "Checking incomplete Collection operations...");
			return LanguageManager.Format("Collections.Workflow.RecoveringCollection",
				"Checking incomplete Collection operations...\r\nCollection: {0}", collectionName);
		}

		private async void BeginRecoveryReconciliation()
		{
			if ((_workflow == null && _managementWorkflow == null) || _workflowBusy || IsDisposed || Disposing)
				return;
			long performanceStarted = CollectionPerformanceMetrics.StartTiming();
			long observedRecoveryOperations = 0;
			CollectionUiContext context = CollectionUiContext.CurrentSetup(_previewGeneration);
			_localRestoreRecoverySources = _managementWorkflow == null
				? new CollectionManagementLocalRestoreRecoverySource[0] : _managementWorkflow.GetInterruptedLocalRestoreSources();
			string recoveryStatus = FormatLocalRestoreRecoveryActivity(_localRestoreRecoverySources) ??
				FormatActiveTargetRecoveryActivity();
			CancellationToken token = BeginWorkflowWork(context, CollectionWorkflowActivityPhase.Recovering, recoveryStatus);
			try
			{
				IReadOnlyList<CollectionAdditiveWorkflowRecoveryResult> results = _workflow == null
					? new CollectionAdditiveWorkflowRecoveryResult[0]
					: await _workflow.ReconcileIncompleteTargetAsync(token);
				IReadOnlyList<CollectionLocalRestoreWorkflowResult> localRestoreResults = _managementWorkflow == null
					? new CollectionLocalRestoreWorkflowResult[0]
					: await Task.Run(() => _managementWorkflow.ReconcileInterruptedLocalRestoresAsync(token), token);
				IReadOnlyList<CollectionRevisionUpdateWorkflowResult> revisionUpdateResults = _managementWorkflow == null
					? new CollectionRevisionUpdateWorkflowResult[0]
					: await _managementWorkflow.ReconcileInterruptedRevisionUpdatesAsync(token);
				IReadOnlyList<CollectionVerifyRepairRecoveryResult> verifyRepairResults = _managementWorkflow == null
					? new CollectionVerifyRepairRecoveryResult[0]
					: await _managementWorkflow.ReconcileInterruptedVerifyRepairsAsync(token);
				IReadOnlyList<CollectionReplacementStartupInspection> replacementInspections = _managementWorkflow == null
					? new CollectionReplacementStartupInspection[0]
					: await Task.Run(() => _managementWorkflow.InspectInterruptedReplacements(), token);
				_hasInterruptedReplacement = replacementInspections.Count > 0;
				IReadOnlyList<CollectionInstalledMemberRemovalResult> memberRemovalResults = new CollectionInstalledMemberRemovalResult[0];
				IReadOnlyList<CollectionUninstallEffectsResult> effectRemovalResults = new CollectionUninstallEffectsResult[0];
				bool c10RecoveryClear = revisionUpdateResults.All(x => x.IsCommitted) && verifyRepairResults.All(x => x.IsCommitted);
				if (_managementWorkflow != null && localRestoreResults.All(x => x.IsSuccessful) && c10RecoveryClear)
					memberRemovalResults = await _managementWorkflow.ReconcileInterruptedMemberRemovalsAsync(token);
				if (_managementWorkflow != null && localRestoreResults.All(x => x.IsSuccessful) && c10RecoveryClear && memberRemovalResults.All(x => x.IsSuccessful))
					effectRemovalResults = await _managementWorkflow.ReconcileInterruptedEffectRemovalAsync(token);
				observedRecoveryOperations = (results == null ? 0 : results.Count) + (localRestoreResults == null ? 0 : localRestoreResults.Count) +
					(revisionUpdateResults == null ? 0 : revisionUpdateResults.Count) + (verifyRepairResults == null ? 0 : verifyRepairResults.Count) +
					(replacementInspections == null ? 0 : replacementInspections.Count) + (memberRemovalResults == null ? 0 : memberRemovalResults.Count) +
					(effectRemovalResults == null ? 0 : effectRemovalResults.Count);
				if (_managementWorkflow != null)
				{
					string cleanupStatus = L("Collections.Workflow.Cleanup", "Cleaning stale retained Collection data...");
					_workflowStatusLabel.Text = cleanupStatus;
					SetWorkflowActivity(CollectionWorkflowActivityBuilder.Foreground(CollectionWorkflowActivityPhase.Recovering, cleanupStatus));
					await _managementWorkflow.CleanupRetainedContentAsync(token,
						_managedAssociationPresentation == null ? _snapshot?.Revision?.Identity.Collection : null, 32);
				}

				_recoveryResults = results ?? new CollectionAdditiveWorkflowRecoveryResult[0];
				_localRestoreRecoveryResults = localRestoreResults ?? new CollectionLocalRestoreWorkflowResult[0];
				_localRestoreRecoverySources = _managementWorkflow == null
					? new CollectionManagementLocalRestoreRecoverySource[0] : _managementWorkflow.GetInterruptedLocalRestoreSources();
				_revisionUpdateRecoveryResults = revisionUpdateResults ?? new CollectionRevisionUpdateWorkflowResult[0];
				_verifyRepairRecoveryResults = verifyRepairResults ?? new CollectionVerifyRepairRecoveryResult[0];
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
					if (replacementInspections.Count > 0)
						AddReviewItem(CollectionReviewSeverity.Warning, CollectionReviewItemKind.Diagnostic,
							L("Collections.Status.ActionRequired", "Action required"), "replacement.interrupted", GetCurrentCollectionSubject(),
							LanguageManager.Format("Collections.Replace.InterruptedCount", "{0} interrupted replacement operation(s) exist for this target. Replacement recovery must be resolved before new managed mutation.", replacementInspections.Count));
					CollectionRevisionUpdateWorkflowResult pendingUpdate = _managedAssociationPresentation == null
						? FindMatchingInterruptedRevisionUpdate() : FindInterruptedRevisionUpdateForAssociation(_managedAssociationPresentation.Association);
					if (pendingUpdate != null)
					{
						_operationSnapshot = pendingUpdate.Operation;
						AppendRevisionUpdateStopDiagnostics(pendingUpdate);
					}
					foreach (CollectionVerifyRepairRecoveryResult repair in _verifyRepairRecoveryResults.Where(x => !x.IsCommitted))
						AddReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic,
							L("Collections.Status.ActionRequired", "Action required"), "verify-repair.interrupted." + repair.OperationIdentity.OperationId.ToString("N"),
							GetCurrentCollectionSubject(), repair.Detail);
				}
				else
				{
					int unresolvedCount = _recoveryResults.Count + _localRestoreRecoveryResults.Count(x => !x.IsSuccessful) +
						_revisionUpdateRecoveryResults.Count(x => !x.IsCommitted) + _verifyRepairRecoveryResults.Count(x => !x.IsCommitted) +
						memberRemovalResults.Count(x => !x.IsSuccessful) + replacementInspections.Count;
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
				CollectionPerformanceMetrics.RecordStartupRecovery(performanceStarted, observedRecoveryOperations);
				EndWorkflowWork(context);
			}
		}

		private void MembersGrid_MouseDown(object sender, MouseEventArgs e)
		{
			if (e.Button != MouseButtons.Right)
				return;
			var hit = _membersView.CalcHitInfo(e.Location);
			if (!hit.InRow && !hit.InRowCell)
				return;
			_membersView.FocusedRowHandle = hit.RowHandle;
			UpdateMemberActionMenuItems();
			_memberActionsMenu.ShowPopup(_membersGrid.PointToScreen(e.Location));
		}

		private void PrimaryIncomingActionButton_Click(object sender, EventArgs e)
		{
			if (_workflowBusy) return;
			if (_primaryIncomingReviewsRevisionOwnership)
			{
				ReviewRevisionUpdateOwnership_Click(sender, e);
				return;
			}
			if (_primaryIncomingReviewsBlockedIssues)
			{
				ShowFirstBlockingIssue();
				return;
			}
			SimpleButton source = _primaryIncomingActionSource;
			if (source == null || !source.Enabled)
				return;
			if (ReferenceEquals(source, _compareUpdateButton)) CompareUpdateButton_Click(source, EventArgs.Empty);
			else if (ReferenceEquals(source, _resolveFileConflictsButton)) ResolveFileConflictsButton_Click(source, EventArgs.Empty);
			else if (ReferenceEquals(source, _openPendingButton)) OpenPendingButton_Click(source, EventArgs.Empty);
			else if (ReferenceEquals(source, _resumeButton)) ResumeButton_Click(source, EventArgs.Empty);
			else if (ReferenceEquals(source, _installButton)) InstallButton_Click(source, EventArgs.Empty);
			else if (ReferenceEquals(source, _downloadPrepareButton)) DownloadPrepareButton_Click(source, EventArgs.Empty);
		}

		private void ShowFirstBlockingIssue()
		{
			_showErrorIssuesCheckBox.Checked = true;
			_showWarningIssuesCheckBox.Checked = false;
			_showInfoIssuesCheckBox.Checked = false;
			RefreshFilteredReviewIssues();
			_reviewTabs.SelectedTabPage = _issuesPage;
			_issuesGrid.Focus();
			int index = _issueRows.ToList().FindIndex(row => row.Item != null && row.Item.Severity == CollectionReviewSeverity.Error);
			if (index < 0) return;
			int handle = _issuesView.GetRowHandle(index);
			_issuesView.FocusedRowHandle = handle;
			_issuesView.MakeRowVisible(handle);
		}

		private void AddMemberActionMenuItem(PopupMenu menu, SimpleButton sourceButton, EventHandler handler, bool beginGroup)
		{
			BarButtonItem item = new BarButtonItem(_memberMenuManager, sourceButton.Text) { Tag = sourceButton };
			item.ItemClick += (sender, args) => handler(sender, EventArgs.Empty);
			BarItemLink link = menu.AddItem(item);
			link.BeginGroup = beginGroup;
		}

		private void UpdateMemberActionMenuItems()
		{
			foreach (BarItemLink link in _memberActionsMenu.ItemLinks)
			{
				BarButtonItem item = link.Item as BarButtonItem;
				SimpleButton sourceButton = item == null ? null : item.Tag as SimpleButton;
				if (sourceButton == null) continue;
				item.Caption = sourceButton.Text;
				item.Enabled = sourceButton.Enabled;
			}
		}

		private void MembersView_FocusedRowChanged(object sender, FocusedRowChangedEventArgs e)
		{
			UpdatePendingDownloadActionLabel(GetSelectedOrFirstPendingAction());
			UpdateActionButtons();
		}

		/// <summary>Toggles an optional checkbox on the first click, even on a row that is not yet focused.</summary>
		private void MembersView_MouseDown(object sender, MouseEventArgs e)
		{
			if (e.Button != MouseButtons.Left) return;
			var hit = _membersView.CalcHitInfo(e.Location);
			if (!hit.InRowCell || hit.Column == null || hit.Column.FieldName != nameof(MemberGridRow.Selected)) return;
			DXMouseEventArgs mouse = e as DXMouseEventArgs;
			if (mouse != null) mouse.Handled = true;
			_membersView.FocusedRowHandle = hit.RowHandle;
			_membersView.FocusedColumn = hit.Column;
			ToggleMemberSelection(hit.RowHandle);
		}

		/// <summary>Supports Space on a focused optional checkbox without opening an editor.</summary>
		private void MembersGrid_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.KeyCode != Keys.Space || e.Modifiers != Keys.None || _membersView.FocusedColumn == null ||
				_membersView.FocusedColumn.FieldName != nameof(MemberGridRow.Selected)) return;
			e.Handled = true;
			e.SuppressKeyPress = true;
			ToggleMemberSelection(_membersView.FocusedRowHandle);
		}

		/// <summary>Preserves required-member, reviewed-operation and installed-view guards for every selection input.</summary>
		private bool CanEditMemberSelection(MemberGridRow row)
		{
			NormalizedCollectionMember member = row == null ? null : row.Member;
			return !_suppressMemberCheckEvents && !_workflowBusy && _managedAssociationPresentation == null &&
				(_operationSnapshot == null || (!_operationSnapshot.HasCrossedNativeBoundary && !_operationSnapshot.IsSuccessful)) &&
				member != null && !member.IsRequired && member.IdentityResolution.IsResolved && _incomingActionContext != null;
		}

		/// <summary>Uses the existing cell-change path to save explicit intent and invalidate the previous review.</summary>
		private void ToggleMemberSelection(int rowHandle)
		{
			MemberGridRow row = _membersView.GetRow(rowHandle) as MemberGridRow;
			if (CanEditMemberSelection(row))
				_membersView.SetRowCellValue(rowHandle, nameof(MemberGridRow.Selected), !row.Selected);
		}

		private void MembersView_CellValueChanged(object sender, CellValueChangedEventArgs e)
		{
			if (_suppressMemberCheckEvents || e.Column == null || e.Column.FieldName != nameof(MemberGridRow.Selected))
				return;
			MemberGridRow row = _membersView.GetRow(e.RowHandle) as MemberGridRow;
			NormalizedCollectionMember member = row == null ? null : row.Member;
			if (!CanEditMemberSelection(row))
			{
				// A review/operation boundary may change while an editor is open.
				if (row != null)
				{
					row.Selected = !row.Selected;
					_membersView.RefreshRow(e.RowHandle);
				}
				return;
			}
			CollectionUiContext selectionContext = _incomingActionContext;
			_optionalMemberSelectionState[member.IdentityResolution.Key] = row.Selected
				? CollectionMemberSelection.Selected : CollectionMemberSelection.Unselected;
			BeginInvoke((Action)(() =>
			{
				if (!IsActionContextCurrent(selectionContext)) return;
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
					_replacementReview = null;
					_acquisitionBatch = null;
				}
				_installButton.Enabled = false;
				if (GetCurrentMemberFilter() != CollectionMemberListFilterKind.All || !String.IsNullOrWhiteSpace(_memberSearchTextBox.Text))
					RenderMembers(_snapshot, GetSelectedMemberToken(), _membersView.TopRowIndex);
				else
					UpdateMemberSelectionText();
				_workflowStatusLabel.Text = L("Collections.Workflow.SelectionChanged", "Workflow: optional selection changed; prepare a new review before installation.");
				RefreshWorkflowActivity();
				UpdateActionButtons();
			}));
		}

		/// <summary>Keeps displayed revision defaults consistent with review inheritance without making them explicit user decisions.</summary>
		private void RefreshInheritedRevisionOptionalSelections(NexusCollectionPreviewSnapshot snapshot)
		{
			if (_revisionUpdateWorkflow == null || !snapshot.HasManifestPreview || _managedAssociationPresentation != null ||
				_displayContext == null || _displayContext.Kind != CollectionUiContextKind.IncomingCollection ||
				_revisionUpdateReview != null || _operationIdentity != null)
				return;
			CollectionManagementAssociation source = FindRevisionUpdateSourceAssociation();
			if (source == null)
			{
				_inheritedRevisionOptionalSelections.Clear();
				_inheritedRevisionPreview = null;
				return;
			}
			if (ReferenceEquals(_inheritedRevisionPreview, snapshot) && _inheritedRevisionAssociationId == source.AssociationId &&
				source.Association.Revision.Equals(_inheritedInstalledRevision))
				return;
			CollectionEffectiveSelection selection = _revisionUpdateWorkflow.BuildCandidatePreviewSelection(source.AssociationId, snapshot.CapabilityReport);
			_inheritedRevisionOptionalSelections.Clear();
			foreach (NormalizedCollectionMember member in selection.Manifest.Members.Where(x => !x.IsRequired && x.IdentityResolution.IsResolved))
				_inheritedRevisionOptionalSelections.Add(member.IdentityResolution.Key, member.Selection);
			_inheritedRevisionPreview = snapshot;
			_inheritedRevisionAssociationId = source.AssociationId;
			_inheritedInstalledRevision = source.Association.Revision;
		}

		private void RenderSnapshot(NexusCollectionPreviewSnapshot snapshot)
		{
			RefreshInheritedRevisionOptionalSelections(snapshot);
			string selectedMember = GetSelectedMemberToken();
			int previousTopIndex = _membersView.TopRowIndex;
			CollectionDefinition definition = snapshot.Definition;
			CollectionRevision revision = snapshot.Revision;
			NexusCollectionRevisionMetadata providerRevision = snapshot.RevisionLookup?.Revision;
			NexusCollectionSummaryMetadata providerSummary = snapshot.SummaryLookup?.Collection;

			_collectionValue.Text = FirstNonEmpty(definition?.DisplayName, providerSummary?.Name, snapshot.Link?.CollectionSlug, L("Collections.Value.Unknown", "Unknown"));
			_curatorValue.Text = FirstNonEmpty(definition?.AuthorDisplayName, providerSummary?.AuthorDisplayName, L("Collections.Value.Unknown", "Unknown"));
			_locatorValue.Text = snapshot.Link != null
				? snapshot.Link.GameDomain + " / " + snapshot.Link.CollectionSlug
				: (_managedAssociationPresentation != null ? FormatManagedAssociationLocator(_managedAssociationPresentation)
					: revision != null ? "collection " + revision.Collection.StableId : L("Collections.Value.Unknown", "Unknown"));
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
			_membersView.BeginDataUpdate();
			_suppressMemberCheckEvents = true;
			int focusedIndex = -1;
			try
			{
				_memberRows.Clear();
				if (!snapshot.HasManifestPreview)
				{
					_membersHeader.Text = L("Collections.Preview.Members", "Members");
					return;
				}

				CollectionCapabilityReport report = snapshot.CapabilityReport;
				Dictionary<CollectionMemberKey, CollectionManagementMemberPresentation> managedMembers =
					_managedAssociationPresentation == null
						? new Dictionary<CollectionMemberKey, CollectionManagementMemberPresentation>()
						: _managedAssociationPresentation.Members.ToDictionary(x => x.MemberKey, x => x);
				CollectionMemberListFilterKind filter = GetCurrentMemberFilter();
				string searchText = _memberSearchTextBox.Text;
				int visibleCount = 0;
				foreach (CollectionMemberCapabilityReport memberReport in report.MemberReports)
				{
					NormalizedCollectionMember member = memberReport.Member;
					string token = MemberToken(member);
					string displayName = string.IsNullOrWhiteSpace(member.DisplayName) ? token : member.DisplayName;
					string artifactText = member.Artifact == null ? L("Collections.Value.Unresolved", "Unresolved") : member.Artifact.ToString();

					CollectionManagementMemberPresentation managedMember = null;
					if (member.IdentityResolution.IsResolved)
						managedMembers.TryGetValue(member.IdentityResolution.Key, out managedMember);
					bool selected = _managedAssociationPresentation != null
						? managedMember != null
						: IsMemberEffectivelySelected(member);
					string managedStateText = managedMember == null
						? (_managedAssociationPresentation == null
							? L("Collections.Member.ManagedState.NotApplied", "Not applied")
							: L("Collections.Member.ManagedState.NotBound", "Not part of installed recipe"))
						: FormatManagedMemberState(managedMember);

					if (!CollectionMemberListPresentationFilter.Matches(filter, searchText, displayName, token, artifactText,
						managedMember == null ? null : managedMember.NativeMod.NativeModKey, managedStateText, member.IsRequired, selected,
						memberReport.Status, managedMember != null && managedMember.HasDetectedDrift,
						managedMember != null && managedMember.HasExplicitLocalDecision))
						continue;

					_memberRows.Add(new MemberGridRow(member, displayName, selected,
						member.IsRequired ? L("Collections.Member.Required", "Required") : L("Collections.Member.Optional", "Optional"),
						managedMember != null && _managedAssociationPresentation.Association.State == CollectionAssociationState.Applied
							? (managedMember.HasDetectedDrift ? L("Collections.Status.ActionRequired", "Action required") : L("Collections.Status.Supported", "Supported"))
							: FormatCompatibility(memberReport.Status), artifactText, managedStateText));
					if (StringComparer.Ordinal.Equals(token, selectedToken))
						focusedIndex = _memberRows.Count - 1;
					visibleCount++;
				}

				UpdateMembersHeader(report.Manifest.Members.Count, visibleCount);
			}
			finally
			{
				_membersView.EndDataUpdate();
				if (focusedIndex >= 0 && focusedIndex < _memberRows.Count)
					_membersView.FocusedRowHandle = _membersView.GetRowHandle(focusedIndex);
				if (previousTopIndex >= 0 && _memberRows.Count > 0)
					_membersView.TopRowIndex = Math.Min(previousTopIndex, _memberRows.Count - 1);
				_suppressMemberCheckEvents = false;
			}
		}

		private CollectionMemberListFilterKind GetCurrentMemberFilter()
		{
			MemberFilterChoice choice = _memberFilterCombo.SelectedItem as MemberFilterChoice;
			return choice == null ? CollectionMemberListFilterKind.All : choice.Kind;
		}

		private bool IsMemberEffectivelySelected(NormalizedCollectionMember member)
		{
			if (member == null)
				return false;
			if (member.IsRequired)
				return true;
			if (!member.IdentityResolution.IsResolved)
				return member.IsSelected;
			CollectionMemberSelection selection;
			if (_optionalMemberSelectionState.TryGetValue(member.IdentityResolution.Key, out selection))
				return selection == CollectionMemberSelection.Selected;
			if (_displayContext != null && _displayContext.Kind == CollectionUiContextKind.IncomingCollection &&
				_inheritedRevisionOptionalSelections.TryGetValue(member.IdentityResolution.Key, out selection))
				return selection == CollectionMemberSelection.Selected;
			return member.IsSelected;
		}

		private void UpdateMembersHeader(int totalCount, int visibleCount)
		{
			bool filtered = GetCurrentMemberFilter() != CollectionMemberListFilterKind.All ||
				!String.IsNullOrWhiteSpace(_memberSearchTextBox.Text);
			_membersHeader.Text = filtered
				? LanguageManager.Format("Collections.Preview.MemberFilteredCount", "Members ({0} of {1})", visibleCount, totalCount)
				: LanguageManager.Format("Collections.Preview.MemberCount", "Members ({0})", totalCount);
		}

		private void MemberListFilter_Changed(object sender, EventArgs e)
		{
			if (_snapshot == null || !_snapshot.HasManifestPreview || _workflowBusy)
				return;
			string selectedToken = GetSelectedMemberToken();
			int previousTopIndex = _membersView.TopRowIndex;
			RenderMembers(_snapshot, selectedToken, previousTopIndex);
			UpdateActionButtons();
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
			_issuesView.BeginDataUpdate();
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
				if (snapshot.HasManifestPreview)
				{
					AppendSetupGuidance(snapshot.CapabilityReport.Manifest.SetupGuidance);
					AppendCollectionTools(snapshot.CapabilityReport.Manifest.LaunchTools);
				}
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
				_issuesView.EndDataUpdate();
			}
		}

		private void AppendSetupGuidance(CollectionSetupGuidance guidance)
		{
			if (guidance == null || !guidance.HasGuidance)
				return;

			if (guidance.RecommendNewProfile)
			{
				AddReviewItem(CollectionReviewSeverity.Warning, CollectionReviewItemKind.ManualAction,
					L("Collections.Status.Recommendation", "Recommendation"), "manifest.guidance.recommend-new-profile",
					L("Collections.Guidance.ProfileSubject", "Curator setup recommendation"),
					L("Collections.Guidance.RecommendNewProfile", "The curator recommends using a new Vortex profile for this Collection. NMM does not automatically create, switch or replace profiles; review whether the current managed setup is appropriate before applying the Collection."));
			}

			if (!String.IsNullOrWhiteSpace(guidance.InstallInstructions))
			{
				AddReviewItem(CollectionReviewSeverity.Info, CollectionReviewItemKind.ManualAction,
					L("Collections.Status.Information", "Information"), "manifest.guidance.install-instructions",
					L("Collections.Guidance.InstructionsSubject", "Curator installation instructions"), guidance.InstallInstructions);
			}

			if (guidance.GameVersions.Count > 0)
			{
				AddReviewItem(CollectionReviewSeverity.Info, CollectionReviewItemKind.ManualAction,
					L("Collections.Status.Information", "Information"), "manifest.guidance.game-versions",
					L("Collections.Guidance.GameVersionsSubject", "Curator game-version reference"),
					LanguageManager.Format("Collections.Guidance.GameVersions",
						"The Collection was authored for these game version value(s): {0}. NMM preserves this guidance but does not currently treat it as an automatically verified compatibility gate.",
						String.Join(", ", guidance.GameVersions)));
			}
		}

		private void AppendCollectionTools(IEnumerable<CollectionLaunchTool> tools)
		{
			foreach (CollectionLaunchTool tool in tools ?? Enumerable.Empty<CollectionLaunchTool>())
			{
				string arguments = tool.Arguments.Count == 0 ? L("Collections.Tools.NoArguments", "(none)") : String.Join(" ", tool.Arguments);
				string workingDirectory = String.IsNullOrEmpty(tool.RelativeWorkingDirectory)
					? L("Collections.Tools.ExecutableDirectory", "Executable directory")
					: tool.RelativeWorkingDirectory;
				string environment = tool.Environment.Count == 0
					? L("Collections.Tools.NoEnvironment", "(none)")
					: String.Join(", ", tool.Environment.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => x.Key + "=" + x.Value));
				AddReviewItem(CollectionReviewSeverity.Info, CollectionReviewItemKind.ManualAction,
					L("Collections.Status.Information", "Information"), "manifest.tool", tool.Name,
					LanguageManager.Format("Collections.Tools.Review",
						"This Collection defines an optional user-launched tool. NMM will never run it automatically. Executable: {0}; arguments: {1}; working directory: {2}; environment: {3}.",
						tool.RelativeExecutablePath, arguments, workingDirectory, environment));
			}
		}

		private static bool CanResolveNexusSourcePolicyDuringPreparation(CollectionCapabilityReport report)
		{
			if (report == null || report.Status != CollectionCompatibilityStatus.ActionRequired)
				return false;
			if (report.ManifestIssues.Count > 0)
				return false;

			bool sawResolvablePolicy = false;
			foreach (CollectionMemberCapabilityReport memberReport in report.MemberReports.Where(x => x.Member.IsSelected))
			{
				foreach (CollectionCapabilityIssue issue in memberReport.Issues)
				{
					if (issue.Status == CollectionCompatibilityStatus.Unsupported ||
						!CollectionNexusSourcePolicyResolver.IsResolvableIssue(issue))
						return false;
					sawResolvablePolicy = true;
				}
			}
			return sawResolvablePolicy;
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
			_replacementReview = null;
			_revisionUpdateReview = null;
			_revisionUpdatePreparation = null;
			_revisionUpdateResult = null;
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
			CollectionConflictImpactIssue blockingImpact = result.Status == CollectionAdditiveWorkflowPreparationStatus.ActionRequired && result.ImpactPlan != null
				? result.ImpactPlan.Issues.FirstOrDefault(x => x.Status == CollectionConflictImpactStatus.ActionRequired) : null;
			if (blockingImpact != null)
				userMessage = GetImpactIssuePresentation(result.ImpactPlan, blockingImpact);
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
					_contentValue.Text = ResolveUserMessage(userMessage);
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
			if (result.Status == CollectionAdditiveWorkflowPreparationStatus.ActionRequired ||
				result.Status == CollectionAdditiveWorkflowPreparationStatus.Blocked)
				ShowFirstBlockingIssue();
		}

		/// <summary>Report the actual durable apply result at the approval boundary, not only in a pane
		/// that may be below the visible part of the Collections tab. This is presentation only.</summary>
		private void ShowAdditiveApplyFeedback(CollectionAdditiveWorkflowApplyResult result)
		{
			if (result == null)
				return;
			CollectionUserMessagePresentation presentation = CollectionUserMessagePresenter.ForApply(result.Status, result.Message);
			string message = BuildUserDialogMessage(presentation);
			if (!result.IsCommitted)
				message += Environment.NewLine + Environment.NewLine + L("Collections.Messages.Apply.DetailsInReview",
					"The failed mod and details are shown under Review / issues.");
			string title = result.IsCommitted
				? L("Collections.Messages.Apply.SuccessTitle", "Collection installed and verified")
				: result.Status == CollectionAdditiveWorkflowApplyStatus.PausedAtSafeBoundary
					? L("Collections.Messages.Apply.PausedTitle", "Collection installation paused")
					: L("Collections.Messages.Apply.StoppedTitle", "Collection installation stopped");
			MessageBoxIcon icon = result.IsCommitted || result.Status == CollectionAdditiveWorkflowApplyStatus.PausedAtSafeBoundary
				? MessageBoxIcon.Information : MessageBoxIcon.Warning;
			XtraMessageBox.Show(this, message, title, MessageBoxButtons.OK, icon);
		}

		private void RenderApplyResult(CollectionAdditiveWorkflowApplyResult result)
		{
			if (result == null)
				return;
			_operationIdentity = result.Operation.Identity;
			_operationSnapshot = result.Operation;
			BindIncomingDisplayOperation(_operationIdentity);
			CollectionAdditiveWorkflowRecoveryStatus recoveryStatus = result.Status == CollectionAdditiveWorkflowApplyStatus.PausedAtSafeBoundary
				? CollectionAdditiveWorkflowRecoveryStatus.ReadyToResume
				: result.Status == CollectionAdditiveWorkflowApplyStatus.RecoveryRequired ? CollectionAdditiveWorkflowRecoveryStatus.RecoveryRequired
				: result.Status == CollectionAdditiveWorkflowApplyStatus.RepreparationRequired ? CollectionAdditiveWorkflowRecoveryStatus.RepreparationRequired
				: CollectionAdditiveWorkflowRecoveryStatus.StoppedPartial;
			CacheRecoveryResult(new CollectionAdditiveWorkflowRecoveryResult(recoveryStatus, result.Operation, null, result.Message));
			if (_snapshot != null)
				RenderIssues(_snapshot, false, false);
			CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForApply(result.Status, result.Message);
			SetWorkflowPresentation(userMessage);
			AddPresentedReviewItem(CollectionReviewPresentationClassifier.ForApply(result.Status),
				result.IsCommitted ? CollectionReviewItemKind.Progress : CollectionReviewItemKind.Diagnostic,
				result.IsCommitted ? L("Collections.Status.Supported", "Ready") : L("Collections.Status.ActionRequired", "Action required"),
				"workflow.apply-result", GetCurrentCollectionSubject(), userMessage, null,
				CombineTechnicalDetail("Apply status: " + result.Status, "Operation: " + result.Operation.Identity));
			if (result.Status == CollectionAdditiveWorkflowApplyStatus.RecoveryRequired)
			{
				CollectionNativeChildOperation child = result.Operation.NativeChildren.LastOrDefault(x => !x.IsReconciled &&
					x.NativeResult != null && x.NativeResult.Durability == ModOperationDurability.Unknown);
				if (child != null && !String.IsNullOrWhiteSpace(child.NativeResult.Message))
					AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic,
						L("Collections.Status.ActionRequired", "Action required"), "workflow.native-verification",
						FormatMemberSubject(child.Member.MemberKey),
						CollectionUserMessagePresenter.FromRaw(child.NativeResult.Message, ResolveNextAction(userMessage)), child.Member.MemberKey);
			}
			if (result.Status == CollectionAdditiveWorkflowApplyStatus.StoppedPartial)
			{
				CollectionNativeChildOperation failed = result.Operation.NativeChildren.LastOrDefault(x => x.NativeResult != null &&
					x.NativeResult.ReportedStatus != ModOperationReportedStatus.Succeeded);
				if (failed != null)
					AddReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic,
						L("Collections.Status.ActionRequired", "Action required"), "workflow.native-rolled-back",
						FormatMemberSubject(failed.Member.MemberKey),
						L("Collections.Messages.Apply.ModRolledBack", "This mod was not installed. NMM checked that its previous files and settings were preserved."),
						failed.Member.MemberKey, ResolveNextAction(userMessage), CombineTechnicalDetail(failed.NativeResult.Message, result.Message));
			}
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
				string technical = "Disposition: " + state.Disposition + "; match disposition: " + state.Match.Disposition + "; match reason: " + state.Match.Reason;
				if (state.PendingAction != null)
					technical += "; allowed manual actions: " + state.PendingAction.AllowedActions;
				if (state.QueueCorrelation != null)
					technical += "; native task status: " + state.QueueCorrelation.Task.Status;
				bool producerEndedWithoutArchive = state.Disposition == CollectionMemberAcquisitionDisposition.RestartActionRequired &&
					state.QueueCorrelation != null && CollectionAcquisitionConsumerTask.IsTerminal(state.QueueCorrelation.Task.Status);
				CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForAcquisition(state.Disposition, technical, producerEndedWithoutArchive);
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

		/// <summary>Names the affected file and packages or plugin change while retaining ownership details in the report.</summary>
		private CollectionUserMessagePresentation GetImpactIssuePresentation(CollectionConflictImpactPlan impactPlan, CollectionConflictImpactIssue issue)
		{
			if (issue.Kind == CollectionConflictImpactIssueKind.FileWinnerDecisionRequired)
			{
				CollectionFileImpact file = impactPlan.FileImpacts.FirstOrDefault(x => StringComparer.Ordinal.Equals(x.Target.ToString(), issue.SubjectKey));
				if (file != null)
					return CollectionUserMessagePresenter.ForFileWinner(issue, file.Target.RelativePath,
						String.Join(", ", file.Writers.Select(FormatMemberSubject)));
			}
			CollectionPluginImpact plugin = impactPlan.PluginImpacts.FirstOrDefault(x => Equals(x.MemberKey, issue.MemberKey) &&
				StringComparer.Ordinal.Equals(String.Join("|", x.Effect.PluginPaths), issue.SubjectKey));
			return CollectionUserMessagePresenter.ForImpact(issue, plugin == null ? null : plugin.Effect);
		}

		/// <summary>Shows the folder move and existing-file choices together with the reviewed mod.</summary>
		private void AppendFolderCorrectionReview(IEnumerable<PreparedCollectionNativeRecipe> recipes)
		{
			foreach (PreparedCollectionNativeRecipe recipe in recipes.Where(x => x.EffectPreview.InstallRootCorrection != null))
			{
				CollectionInstallRootCorrection correction = recipe.EffectPreview.InstallRootCorrection;
				AddReviewItem(CollectionReviewSeverity.Info, CollectionReviewItemKind.PlannedEffect,
					L("Collections.FolderCorrection.Status", "Move to game folder"), "install-folder.move", recipe.Member.DisplayName,
					L("Collections.FolderCorrection.Detail", "Move this mod from Data to the game folder. Its old Data files will be removed, and any files it replaced will be restored."), recipe.Member.MemberKey,
					L("Collections.Messages.Next.ReviewPlannedChange", "Review this planned change before installing."),
					"Install method: " + correction.InstallMethod + "; old root: Data; new root: GameRoot; old targets: " +
					String.Join(", ", correction.Files.Select(x => x.Before.Target + " -> " + (x.After.Existed ? "restore previous file" : "remove"))) +
					"; new targets: " + String.Join(", ", recipe.EffectPreview.Files.Select(x => x.Target.ToString())));
				foreach (CollectionInstallRootDestination destination in correction.Destinations.Where(x => x.Before.Existed))
					AddReviewItem(CollectionReviewSeverity.Info, CollectionReviewItemKind.PlannedEffect,
						L("Collections.FolderCorrection.ExistingFile", "Existing file"), "install-folder.existing-file", destination.Before.Target.RelativePath,
						destination.PreserveWinner ? L("Collections.FolderCorrection.KeepExisting", "Keep the file supplied by the other Collection mod.") :
						L("Collections.FolderCorrection.ReplaceExisting", "Replace this game-folder file. NMM will keep its previous version for restoration when the mod is uninstalled."), recipe.Member.MemberKey, null,
						"Previous owner: " + (destination.CurrentOwnerKey ?? "unmanaged") + "; sha256: " + destination.Before.ContentHash.Value);
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
				CollectionReviewItemKind issueKind = issue.Kind == CollectionConflictImpactIssueKind.ExistingFileWinnerDecisionRequired
					? CollectionReviewItemKind.ManualAction : CollectionReviewItemKind.Diagnostic;
				AddPresentedReviewItem(CollectionReviewPresentationClassifier.ForImpact(issue.Status), issueKind,
					FormatImpactStatus(issue.Status), "impact." + issue.Kind.ToString().ToLowerInvariant(), subject,
					GetImpactIssuePresentation(impactPlan, issue), issue.MemberKey,
					CombineTechnicalDetail("Impact kind: " + issue.Kind, issue.SubjectKey, issue.MemberKey == null ? null : "Member: " + issue.MemberKey));
			}

			foreach (CollectionFileImpact impact in impactPlan.FileImpacts)
			{
				string winnerName = impact.PlannedWinner == null ? L("Collections.Review.NoPlannedWinner", "No Collection member") : FormatMemberSubject(impact.PlannedWinner);
				string explanation = impact.PreserveCurrentOwner
					? L("Collections.Review.FileKeepExistingWinner", "The current NMM-managed provider will remain the final provider for this file; Collection writers will remain underneath it.")
					: impact.PlannedWinner == null
						? L("Collections.Review.FileNoWinner", "No Collection member is selected as the final provider for this file.")
						: LanguageManager.Format("Collections.Review.FileWinner", "After installation, {0} is planned to provide this file.", winnerName);
				string technical = CombineTechnicalDetail(
					"Planned Collection winner: " + (impact.PlannedWinner == null ? "none" : impact.PlannedWinner.ToString()),
					"Preserve current owner: " + impact.PreserveCurrentOwner,
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
				if (!recovery.Operation.IsTerminal)
				{
					CollectionManagementLocalRestoreRecoverySource source = FindLocalRestoreRecoverySource(recovery.Operation.Identity);
					CollectionUserMessagePresentation userMessage = CollectionUserMessagePresenter.ForLocalRestore(recovery.Status, recovery.Message);
					string subject = source != null && source.HasResolvedCapture
						? source.SourceDisplayName : (recovery.Operation.Revision == null ? L("Collections.Review.LocalCollectionRestore", "Local Collection restore") : recovery.Operation.Revision.ToString());
					string sourceDetail = source == null ? null : "Capture: " + (source.CaptureIdentity == null ? "<unresolved>" : source.CaptureIdentity.ToString()) +
						(String.IsNullOrWhiteSpace(source.Diagnostic) ? String.Empty : "; source diagnostic: " + source.Diagnostic);
					AddPresentedReviewItem(CollectionReviewSeverity.Error, CollectionReviewItemKind.Diagnostic, L("Collections.Status.ActionRequired", "Action required"),
						"workflow.local-restore-recovery", subject, userMessage, null,
						CombineTechnicalDetail(CombineTechnicalDetail("Restore status: " + recovery.Status, "Operation: " + recovery.Operation.Identity), sourceDetail));
				}
			}
		}

		/// <summary>Replaces stale startup recovery data with the latest operation result without retaining completed work.</summary>
		private void CacheRecoveryResult(CollectionAdditiveWorkflowRecoveryResult result)
		{
			List<CollectionAdditiveWorkflowRecoveryResult> results = _recoveryResults.Where(x => !x.Operation.Identity.Equals(result.Operation.Identity)).ToList();
			if (!result.Operation.IsTerminal)
				results.Add(result);
			_recoveryResults = results;
		}

		private void BindMatchingRecovery()
		{
			if (_snapshot == null || _snapshot.Revision == null || _recoveryResults == null)
				return;
			CollectionAdditiveWorkflowRecoveryResult matching = _recoveryResults.FirstOrDefault(x =>
				x != null && x.Operation != null && x.Operation.Revision != null && x.Operation.Revision.Equals(_snapshot.Revision.Identity) &&
				(x.Status == CollectionAdditiveWorkflowRecoveryStatus.ReviewRequired || x.Status == CollectionAdditiveWorkflowRecoveryStatus.ReadyToResume ||
				 x.Status == CollectionAdditiveWorkflowRecoveryStatus.RecoveryRequired));
			if (matching == null || matching.Operation.PlanIdentity == null || matching.Operation.IsTerminal ||
				(_operationSnapshot != null && _operationSnapshot.Identity.Equals(matching.Operation.Identity) &&
				 _operationSnapshot.CheckpointSequence > matching.Operation.CheckpointSequence))
				return;
			_operationIdentity = matching.Operation.Identity;
			_operationSnapshot = matching.Operation;
			BindIncomingDisplayOperation(_operationIdentity);
			_reviewedPlanIdentity = matching.Operation.PlanIdentity;
			_selectionDirty = false;
			SetWorkflowPresentation(CollectionUserMessagePresenter.ForRecovery(matching.Status, matching.Message));
			_appliedValue.Text = matching.Status == CollectionAdditiveWorkflowRecoveryStatus.ReadyToResume
				? L("Collections.Status.Applied.ResumeReady", "Incomplete apply reconciled - explicit resume available")
				: matching.Status == CollectionAdditiveWorkflowRecoveryStatus.RecoveryRequired
				? L("Collections.Status.Applied.RecoveryRequired", "Recovery required before further mutation")
				: LanguageManager.Get("Collections.Status.Applied.RecoveredReviewReady", "Recovered review awaits explicit approval");
			UpdateActionButtons();
		}

		internal static IReadOnlyList<CollectionOptionalMemberSelection> BuildManagedMemberExpansionSelections(
			NormalizedCollectionManifest manifest, IEnumerable<CollectionMemberKey> boundMemberKeys, CollectionMemberKey requestedMemberKey)
		{
			if (manifest == null)
				throw new ArgumentNullException(nameof(manifest));
			if (boundMemberKeys == null)
				throw new ArgumentNullException(nameof(boundMemberKeys));
			if (requestedMemberKey == null)
				throw new ArgumentNullException(nameof(requestedMemberKey));

			var resolvedMembers = manifest.Members.Where(x => x != null && x.IdentityResolution.IsResolved)
				.ToDictionary(x => x.IdentityResolution.Key);
			NormalizedCollectionMember requested;
			if (!resolvedMembers.TryGetValue(requestedMemberKey, out requested))
				throw new InvalidOperationException("The requested installed-Collection expansion member is not present in the exact retained manifest.");
			if (requested.Requirement != CollectionMemberRequirement.Optional)
				throw new InvalidOperationException("Only an optional Collection member can be added through installed-member expansion.");

			var bound = new HashSet<CollectionMemberKey>(boundMemberKeys);
			if (bound.Contains(requestedMemberKey))
				throw new InvalidOperationException("The requested optional Collection member is already part of the installed association.");
			foreach (CollectionMemberKey key in bound)
				if (key == null || !resolvedMembers.ContainsKey(key))
					throw new InvalidOperationException("The installed Collection association contains a member that is not present in the exact retained manifest.");
			foreach (NormalizedCollectionMember required in manifest.Members.Where(x => x != null && x.Requirement == CollectionMemberRequirement.Required))
			{
				if (!required.IdentityResolution.IsResolved || !bound.Contains(required.IdentityResolution.Key))
					throw new InvalidOperationException("The installed Collection association is missing a required retained-manifest member and cannot be expanded additively.");
			}

			var result = new List<CollectionOptionalMemberSelection>();
			foreach (NormalizedCollectionMember candidate in manifest.Members)
			{
				if (candidate == null || candidate.Requirement != CollectionMemberRequirement.Optional ||
					!candidate.IdentityResolution.IsResolved)
					continue;
				CollectionMemberKey key = candidate.IdentityResolution.Key;
				result.Add(new CollectionOptionalMemberSelection(key, bound.Contains(key) || key.Equals(requestedMemberKey)
					? CollectionMemberSelection.Selected
					: CollectionMemberSelection.Unselected));
			}
			return result.AsReadOnly();
		}

		private IEnumerable<CollectionOptionalMemberSelection> BuildOptionalSelections()
		{
			var result = new List<CollectionOptionalMemberSelection>();
			if (_snapshot == null || !_snapshot.HasManifestPreview)
				return result;
			foreach (NormalizedCollectionMember member in _snapshot.CapabilityReport.Manifest.Members)
			{
				if (member == null || member.Requirement != CollectionMemberRequirement.Optional || !member.IdentityResolution.IsResolved)
					continue;
				result.Add(new CollectionOptionalMemberSelection(member.IdentityResolution.Key,
					IsMemberEffectivelySelected(member) ? CollectionMemberSelection.Selected : CollectionMemberSelection.Unselected));
			}
			return result;
		}

		/// <summary>
		/// Revision update defaults come from the installed association, not from the candidate manifest. Only checkbox
		/// choices the user actually changed in this candidate preview are sent as overrides of that preserved baseline.
		/// </summary>
		private IEnumerable<CollectionOptionalMemberSelection> BuildExplicitRevisionOptionalSelections()
		{
			var result = new List<CollectionOptionalMemberSelection>();
			if (_snapshot == null || !_snapshot.HasManifestPreview || _optionalMemberSelectionState.Count == 0)
				return result;
			var candidateOptional = new HashSet<CollectionMemberKey>(_snapshot.CapabilityReport.Manifest.Members
				.Where(x => x != null && x.Requirement == CollectionMemberRequirement.Optional && x.IdentityResolution.IsResolved)
				.Select(x => x.IdentityResolution.Key));
			foreach (KeyValuePair<CollectionMemberKey, CollectionMemberSelection> decision in _optionalMemberSelectionState)
			{
				if (candidateOptional.Contains(decision.Key))
					result.Add(new CollectionOptionalMemberSelection(decision.Key, decision.Value));
			}
			return result;
		}

		private NormalizedCollectionMember GetSelectedNormalizedMember()
		{
			MemberGridRow row = _membersView.GetFocusedRow() as MemberGridRow;
			return row == null ? null : row.Member;
		}

		private CollectionManagementMemberPresentation GetSelectedManagedMemberPresentation()
		{
			if (_managedAssociationPresentation == null || GetSelectedNormalizedMember() == null)
				return null;
			NormalizedCollectionMember selected = GetSelectedNormalizedMember();
			if (selected == null || !selected.IdentityResolution.IsResolved)
				return null;
			return _managedAssociationPresentation.Members.FirstOrDefault(x => x.MemberKey.Equals(selected.IdentityResolution.Key));
		}

		private T ChooseManagedCustomization<T>(string title, string prompt, IReadOnlyList<T> values, Func<T, string> formatter) where T : class
		{
			if (values == null || values.Count == 0)
				return null;
			if (values.Count == 1)
				return values[0];

			using (DevExpressDisplaySettings dialogSettings = DevExpressDisplaySettings.CreateFromSettings(Properties.Settings.Default))
			using (var dialog = new ManagedFontXtraForm())
			using (var list = new ListBoxControl())
			using (var ok = new SimpleButton())
			using (var cancel = new SimpleButton())
			{
				dialog.Text = title;
				dialog.StartPosition = FormStartPosition.CenterParent;
				dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
				dialog.MinimizeBox = false;
				dialog.MaximizeBox = false;
				dialog.ShowInTaskbar = false;
				dialog.ClientSize = new Size(560, 260);

				var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
				layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
				layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				var label = new LabelControl { AutoSizeMode = LabelAutoSizeMode.Vertical, MaximumSize = new Size(530, 0), Text = prompt, Margin = new Padding(0, 0, 0, 8) };
				list.Dock = DockStyle.Fill;
				foreach (T value in values)
					list.Items.Add(new ManagedCustomizationChoice<T>(value, formatter(value)));
				list.SelectedIndex = 0;

				ok.Text = L("Common.OK", "OK");
				ok.AutoSize = true;
				ok.DialogResult = DialogResult.OK;
				cancel.Text = L("Common.Cancel", "Cancel");
				cancel.AutoSize = true;
				cancel.DialogResult = DialogResult.Cancel;
				var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
				buttons.Controls.Add(cancel);
				buttons.Controls.Add(ok);

				layout.Controls.Add(label, 0, 0);
				layout.Controls.Add(list, 0, 1);
				layout.Controls.Add(buttons, 0, 2);
				dialog.Controls.Add(layout);
				dialog.AcceptButton = ok;
				dialog.CancelButton = cancel;
				ApplyCollectionDialogDisplaySettings(dialog, dialogSettings);
				if (dialog.ShowDialog(this) != DialogResult.OK)
					return null;
				ManagedCustomizationChoice<T> selected = list.SelectedItem as ManagedCustomizationChoice<T>;
				return selected == null ? null : selected.Value;
			}
		}

		private static string FormatDriftChoice(CollectionDriftObservation drift)
		{
			string text = FormatRequirementAspect(drift.Requirement.Aspect);
			if (!String.IsNullOrWhiteSpace(drift.Requirement.SubjectKey))
				text += " - " + drift.Requirement.SubjectKey;
			if (!String.IsNullOrWhiteSpace(drift.Detail))
				text += ": " + CollectionUserMessagePresenter.SanitizeInternalTerminology(drift.Detail);
			return text;
		}

		private static string FormatOverrideChoice(UserOverride userOverride)
		{
			string text = FormatRequirementAspect(userOverride.Requirement.Aspect);
			if (!String.IsNullOrWhiteSpace(userOverride.Requirement.SubjectKey))
				text += " - " + userOverride.Requirement.SubjectKey;
			if (!String.IsNullOrWhiteSpace(userOverride.Note))
				text += ": " + userOverride.Note;
			return text;
		}

		private static string FormatRequirementAspect(CollectionRequirementAspect aspect)
		{
			switch (aspect)
			{
				case CollectionRequirementAspect.MemberParticipation:
					return L("Collections.Requirement.MemberParticipation", "member participation");
				case CollectionRequirementAspect.MemberEnabledState:
					return L("Collections.Requirement.MemberEnabledState", "enabled/disabled");
				case CollectionRequirementAspect.ArtifactSelection:
					return L("Collections.Requirement.ArtifactSelection", "selected file/version");
				case CollectionRequirementAspect.InstallerRecipe:
					return L("Collections.Requirement.InstallerRecipe", "installer choices");
				case CollectionRequirementAspect.FileWinner:
					return L("Collections.Requirement.FileWinner", "file winner");
				case CollectionRequirementAspect.PluginState:
					return L("Collections.Requirement.PluginState", "plugin state");
				case CollectionRequirementAspect.ConfigurationState:
					return L("Collections.Requirement.ConfigurationState", "configuration state");
				case CollectionRequirementAspect.AdditionalManagedContent:
					return L("Collections.Requirement.AdditionalManagedContent", "additional managed content");
				default:
					return L("Collections.Requirement.Unknown", "Collection requirement");
			}
		}

		private sealed class ManagedCustomizationChoice<T> where T : class
		{
			public ManagedCustomizationChoice(T value, string text)
			{
				Value = value ?? throw new ArgumentNullException(nameof(value));
				Text = text ?? String.Empty;
			}

			public T Value { get; }
			public string Text { get; }

			public override string ToString()
			{
				return Text;
			}
		}

		private CollectionManualAcquisitionPendingAction GetSelectedOrFirstPendingAction()
		{
			NormalizedCollectionMember selected = GetSelectedNormalizedMember();
			if (_revisionUpdatePreparation != null)
			{
				if (selected != null && selected.IdentityResolution.IsResolved)
				{
					CollectionRevisionUpdatePreparationMemberState selectedUpdate = _revisionUpdatePreparation.Members.FirstOrDefault(x =>
						x.UpdateMember.MemberKey.Equals(selected.IdentityResolution.Key));
					if (selectedUpdate != null && selectedUpdate.PendingAction != null)
						return selectedUpdate.PendingAction;
				}
				CollectionManualAcquisitionPendingAction updatePending = _revisionUpdatePreparation.Members
					.Where(x => x.PendingAction != null).Select(x => x.PendingAction).FirstOrDefault();
				if (updatePending != null) return updatePending;
			}

			if (_acquisitionBatch == null)
				return null;
			if (selected != null && selected.IdentityResolution.IsResolved)
			{
				CollectionMemberAcquisitionState selectedState = _acquisitionBatch.Members.FirstOrDefault(x => x.Match.Member.MemberKey.Equals(selected.IdentityResolution.Key));
				if (selectedState != null && selectedState.PendingAction != null)
					return selectedState.PendingAction;
			}
			return _acquisitionBatch.Members.Where(x => x.PendingAction != null).Select(x => x.PendingAction).FirstOrDefault();
		}

		private void UpdatePendingDownloadActionLabel(CollectionManualAcquisitionPendingAction pending)
		{
			if (pending == null)
			{
				_openPendingButton.Text = L("Collections.Actions.OpenDownloadPage", "Download selected missing mod");
				return;
			}

			string displayName = GetMemberDisplayName(pending.Request.MemberKey);
			bool canBrowse = pending.Supports(CollectionManualAcquisitionActionKind.Browser) && pending.BrowserUri != null;
			bool canSelect = pending.Supports(CollectionManualAcquisitionActionKind.LocalFile);
			string nexusDomain; long nexusModId; long nexusFileId;
			bool isNexus = NexusCollectionModFileArtifactIdentity.TryParse(pending.Request.SelectedArtifact,
				out nexusDomain, out nexusModId, out nexusFileId);
			if (isNexus)
			{
				_openPendingButton.Text = String.IsNullOrWhiteSpace(displayName)
					? L("Collections.Actions.OpenDownloadPage", "Download selected missing mod")
					: LanguageManager.Format("Collections.Actions.OpenDownloadPageNamed", "Download missing mod: {0}", displayName);
			}
			else if (canBrowse && canSelect)
			{
				_openPendingButton.Text = String.IsNullOrWhiteSpace(displayName)
					? L("Collections.Actions.DownloadOrSelectArchive", "Download / select archive...")
					: LanguageManager.Format("Collections.Actions.DownloadOrSelectArchiveNamed", "Download / select archive: {0}", displayName);
			}
			else if (canSelect)
			{
				_openPendingButton.Text = String.IsNullOrWhiteSpace(displayName)
					? L("Collections.Actions.SelectArchive", "Select archive...")
					: LanguageManager.Format("Collections.Actions.SelectArchiveNamed", "Select archive: {0}", displayName);
			}
			else
			{
				_openPendingButton.Text = String.IsNullOrWhiteSpace(displayName)
					? L("Collections.Actions.OpenDownloadPage", "Download selected missing mod")
					: LanguageManager.Format("Collections.Actions.OpenDownloadPageNamed", "Download missing mod: {0}", displayName);
			}
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

		/// <summary>Explains why a reviewed native installation must remain after this Collection is removed.</summary>
		private static string FormatUninstallPreservationReason(CollectionUninstallNativeImpact impact)
		{
			switch (impact.Disposition)
			{
				case CollectionUninstallNativeDisposition.RemoveNativeMod:
					if (impact.SurvivingAssociationIds.Count > 0)
						return L("Collections.Management.Removal.SharedSelected", "Another Collection also uses this mod. Uncheck it to keep that Collection working.");
					if (impact.StandaloneUse == StandaloneModUse.ExplicitStandaloneUse)
						return L("Collections.Management.Removal.IndependentSelected", "You also use this mod separately. Uncheck it to keep it installed.");
					return L("Collections.Management.Removal.ListedSelected", "Listed in this Collection. Uncheck it to keep it installed.");
				case CollectionUninstallNativeDisposition.AlreadyAbsent:
					return L("Collections.Management.Removal.Absent", "Already uninstalled.");
				case CollectionUninstallNativeDisposition.PreserveSharedCollection:
					return L("Collections.Management.Removal.Shared", "Another installed Collection still needs this mod.");
				case CollectionUninstallNativeDisposition.PreserveStandalone:
					return L("Collections.Management.Removal.Independent", "This mod is marked to be kept independently of the Collection.");
				case CollectionUninstallNativeDisposition.PreserveCustomized:
					return L("Collections.Management.Removal.Customized", "This mod has changes you chose to keep.");
				case CollectionUninstallNativeDisposition.PreserveUnknownProvenance:
					return L("Collections.Management.Removal.UnknownOwnership", "NMM cannot confirm whether this mod was already installed before the Collection.");
				default:
					return CollectionUserMessagePresenter.SanitizeInternalTerminology(impact.Reason);
			}
		}

		/// <summary>Keeps the verified uninstall outcome exportable after its installed association is removed.</summary>
		private void RememberCompletedEffectRemoval(CollectionUninstallEffectsResult result,
			CollectionManagementAssociation selected, IDictionary<NativeModInstanceIdentity, string> subjects)
		{
			_completedRemovalItems = result.Impacts.Select(x => new CollectionReviewItem(CollectionReviewSeverity.Info,
				CollectionReviewItemKind.PlannedEffect, x.RequiresNativeRemoval ? L("Collections.Status.Removed", "Removed") :
					x.Disposition == CollectionUninstallNativeDisposition.AlreadyAbsent ? L("Collections.Status.AlreadyAbsent", "Already absent") : L("Collections.Status.Kept", "Kept"),
				"association.remove." + x.Disposition, subjects[x.NativeMod], x.RequiresNativeRemoval
					? L("Collections.Management.Removal.ModUninstalled", "NMM uninstalled this mod.") : FormatUninstallPreservationReason(x),
				x.MemberKeys.FirstOrDefault(), null, x.Reason)).ToArray();
			string status = LanguageManager.Format("Collections.Management.RemovalCompleteCounts",
				"Workflow: Collection uninstalled. {0} mods removed; {1} mods kept. See the results below.",
				result.Impacts.Count(x => x.RequiresNativeRemoval),
				result.Impacts.Count(x => !x.RequiresNativeRemoval && x.Disposition != CollectionUninstallNativeDisposition.AlreadyAbsent));
			Version version = typeof(CollectionsPreviewControl).Assembly.GetName().Version;
			var report = new CollectionTechnicalReportSnapshot(version == null ? String.Empty : version.ToString())
			{
				Performance = CollectionPerformanceMetrics.Capture(),
				Context = new CollectionTechnicalReportContext
				{
					Kind = "CompletedCollectionRemoval", Generation = _previewGeneration,
					RevisionIdentity = result.Operation.Revision.ToString(), OperationIdentity = result.Operation.Identity.ToString(),
					CompatibilityStatus = L("Collections.Management.Removal.NotTracked", "Collection no longer tracked"),
					ContentStatus = L("Collections.Management.Removal.Finished", "Uninstall finished"),
					AppliedStatus = L("Collections.Management.Removal.Uninstalled", "Collection uninstalled")
				},
				Collection = new CollectionTechnicalReportCollection
				{
					Identity = result.Operation.Collection.ToString(), DisplayName = selected.DisplayName,
					RevisionIdentity = result.Operation.Revision.ToString(), RevisionLabel = selected.RevisionLabel
				},
				Target = new CollectionTechnicalReportTarget { TargetFingerprint = result.Operation.Target.Fingerprint },
				Progress = new CollectionTechnicalReportProgress
				{
					WorkflowStatus = status, OperationPhase = result.Operation.Phase.ToString(),
					ActivityState = CollectionWorkflowActivityState.Completed.ToString(), ActivityPhase = CollectionWorkflowActivityPhase.Managing.ToString(),
					CommandsLocked = false, WorkActive = false, EtaBasis = "completed"
				}
			};
			if (_workflow != null)
			{
				try
				{
					Nexus.Client.GameStorage.GameStoragePathSet paths = _workflow.GetTargetPaths();
					report.Target.GameId = paths.GameId;
					report.Target.GameName = paths.GameName;
				}
				catch (Exception ex) { report.UnavailableData.Add("target-context: " + CollectionTechnicalReportSanitizer.SanitizeText(ex.Message)); }
			}
			_operationSnapshot = result.Operation;
			PopulateTechnicalReportOperation(report);
			ClearReviewItems();
			foreach (CollectionReviewItem item in _completedRemovalItems) AddReviewItem(item);
			PopulateTechnicalReportReview(report);
			_completedRemovalReport = report;
		}

		private string FormatUninstallImpactSubject(CollectionUninstallNativeImpact impact, NexusCollectionBundleImportResult manifest = null)
		{
			if (impact == null)
				return L("Collections.Review.ManagedMod", "Managed mod");

			if (!String.IsNullOrWhiteSpace(impact.NativeDisplayName)) return impact.NativeDisplayName.Trim();

			string[] names = impact.MemberKeys.Select(key =>
			{
				NormalizedCollectionMember member = manifest == null || manifest.Manifest == null ? null : manifest.Manifest.Members.FirstOrDefault(x =>
					x.IdentityResolution.IsResolved && x.IdentityResolution.Key.Equals(key));
				return member == null || String.IsNullOrWhiteSpace(member.DisplayName) ? FormatMemberSubject(key) : member.DisplayName;
			}).Where(x => !String.IsNullOrWhiteSpace(x))
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
					_acquisitionRefreshTimer.Stop();
					_recoveryResults = _recoveryResults.Where(x => !x.Operation.Identity.Equals(cancelled.Identity)).ToList();
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
			if (_operationIdentity == null)
				return;
			try
			{
				if (_replacementReview != null && _replacementWorkflow != null && _operationSnapshot != null &&
					_operationSnapshot.Kind == CollectionOperationKind.ReplaceCurrentManagedSetup && !_operationSnapshot.HasCrossedNativeBoundary)
					_replacementWorkflow.CancelBeforeApply(_replacementReview);
				else if (_workflow != null)
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
			AppendFolderCorrectionReview(review.Runtime.PreparedRecipes);
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
			text.AppendLine(L("Collections.Review.ConfirmDetailTabs", "The complete changes are listed in the Changes to review tab. NMM will install only the changes you reviewed; if the current setup changes, a new review is required."));
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

				DialogResult choice = XtraMessageBox.Show(this,
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

			if (_blockedRevisionUpdatePlan != null)
			{
				SetWorkflowActivity(CollectionWorkflowActivityBuilder.Waiting(CollectionWorkflowActivityPhase.Reviewing, _workflowStatusLabel.Text, true));
				SetWorkflowEta(CollectionWorkflowEtaSnapshot.Unavailable("revision-review-blocked"));
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
			if (_workflowBusy && _workflowActivity.IsWorkActive)
				_workflowStatusLabel.Text = FormatWorkflowActivityText(_workflowActivity);
			RenderWorkflowActivity();
			RenderWorkflowEta();
			if (_workflowBusy)
			{
				_primaryIncomingActionButton.Text = GetBusyIncomingActionText();
				_compareUpdateButton.Text = GetBusyIncomingActionText();
			}
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
			if (_workflowStatusLabel == null || _membersLoadingOverlay == null || _membersLoadingLabel == null ||
				_membersLoadingProgress == null || _membersLoadingCancelButton == null || _workflowActivityAnimationTimer == null)
				return;

			CollectionWorkflowActivityPresentation presentation = CollectionWorkflowActivityPresentationBuilder.Build(_workflowActivity);
			_workflowStatusLabel.AccessibleDescription = FormatWorkflowActivityText(_workflowActivity);
			_membersLoadingOverlay.Appearance.BackColor = _membersGrid.BackColor;
			_membersLoadingOverlay.Appearance.Options.UseBackColor = true;
			if (presentation.AnimateIcon)
			{
				if (!_workflowActivityAnimationTimer.Enabled)
				{
					_workflowActivityAnimationFrame = 0;
					_workflowActivityAnimationTimer.Start();
				}
			}
			else
				_workflowActivityAnimationTimer.Stop();
			RenderWorkflowActivityIcon();

			if (presentation.ShowMemberLoadingOverlay && !_wasShowingMemberLoadingOverlay)
			{
				_reviewNavigationPending = true;
				_reviewTabs.SelectedTabPage = _membersPage;
			}
			_wasShowingMemberLoadingOverlay = presentation.ShowMemberLoadingOverlay;
			_membersLoadingOverlay.Visible = presentation.ShowMemberLoadingOverlay;
			if (presentation.ShowMemberLoadingOverlay)
			{
				_membersLoadingLabel.Text = FormatWorkflowActivityText(_workflowActivity);
				ConfigureMemberLoadingProgress(_workflowActivity);
				_membersLoadingCancelButton.Visible = _workflowActivity != null &&
					_workflowActivity.Phase == CollectionWorkflowActivityPhase.Capturing;
				_membersLoadingCancelButton.Enabled = _workflowCancellation != null && !_workflowCancellation.IsCancellationRequested;
				_membersLoadingOverlay.BringToFront();
			}
			else
			{
				_membersLoadingProgress.Visible = false;
				_membersLoadingMarquee.Visible = false;
				_membersLoadingCancelButton.Visible = false;
			}
		}

		private void MembersLoadingCancelButton_Click(object sender, EventArgs e)
		{
			if (_workflowActivity == null || _workflowActivity.Phase != CollectionWorkflowActivityPhase.Capturing ||
				_workflowCancellation == null || _workflowCancellation.IsCancellationRequested)
				return;
			_membersLoadingCancelButton.Enabled = false;
			_workflowStatusLabel.Text = L("Collections.Capture.Cancelling", "Cancelling Local Collection capture at the next safe file boundary...");
			_workflowCancellation.Cancel();
		}

		private void ConfigureMemberLoadingProgress(CollectionWorkflowActivitySnapshot snapshot)
		{
			if (snapshot != null && !snapshot.IsIndeterminate && snapshot.Current.HasValue && snapshot.Total.HasValue &&
				snapshot.Total.Value > 0 && snapshot.Current.Value >= 0)
			{
				_membersLoadingMarquee.Visible = false;
				_membersLoadingProgress.Visible = true;
				double fraction = Math.Min(1D, (double)snapshot.Current.Value / snapshot.Total.Value);
				_membersLoadingProgress.Position = Math.Max(0, Math.Min(1000, (int)Math.Round(fraction * 1000)));
				return;
			}

			_membersLoadingProgress.Visible = false;
			_membersLoadingMarquee.Visible = true;
		}

		private void WorkflowActivityAnimationTimer_Tick(object sender, EventArgs e)
		{
			if (_workflowActivity == null || !_workflowActivity.IsWorkActive || _workflowActivity.State != CollectionWorkflowActivityState.Working)
			{
				_workflowActivityAnimationTimer.Stop();
				RenderWorkflowActivity();
				return;
			}

			_workflowActivityAnimationFrame = (_workflowActivityAnimationFrame + 1) % 8;
			RenderWorkflowActivityIcon();
		}

		/// <summary>
		/// The label renders NMM's actual semantic SVG icons rather than font glyphs; both
		/// the style and accessible color palette come from the user's Display Options.
		/// </summary>
		private void RenderWorkflowActivityIcon()
		{
			if (_workflowStatusLabel == null || _workflowStatusLabel.IsDisposed)
				return;

			NmmIconAction? action = null;
			if (_workflowActivity != null)
			{
				switch (_workflowActivity.State)
				{
					case CollectionWorkflowActivityState.Working: action = NmmIconAction.Refresh; break;
					case CollectionWorkflowActivityState.Queued: action = NmmIconAction.Sync; break;
					case CollectionWorkflowActivityState.WaitingForUser: action = NmmIconAction.Help; break;
					case CollectionWorkflowActivityState.Paused: action = NmmIconAction.Pause; break;
					case CollectionWorkflowActivityState.Completed: action = NmmIconAction.ModActive; break;
					case CollectionWorkflowActivityState.Failed: action = NmmIconAction.Warning; break;
				}
			}

			Bitmap newImage = null;
			if (action.HasValue)
			{
				int size = NmmIconProvider.CurrentIconSize;
				Image source = NmmIconProvider.GetBitmap(action.Value, size, false);
				if (source != null)
				{
					newImage = new Bitmap(size, size);
					using (Graphics graphics = Graphics.FromImage(newImage))
					{
						graphics.SmoothingMode = SmoothingMode.AntiAlias;
						graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
						graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
						graphics.TranslateTransform(size / 2F, size / 2F);
						if (_workflowActivity.State == CollectionWorkflowActivityState.Working && _workflowActivity.IsWorkActive)
							graphics.RotateTransform(-_workflowActivityAnimationFrame * 45F);
						graphics.DrawImage(source, new RectangleF(-size / 2F, -size / 2F, size, size));
					}
				}
			}

			Bitmap oldImage = _workflowActivityIconBitmap;
			_workflowActivityIconBitmap = newImage;
			_workflowStatusLabel.ImageOptions.Image = newImage;
			oldImage?.Dispose();
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
				case CollectionOperationPhase.RemovingObsoleteRevisionEffects:
				case CollectionOperationPhase.ObsoleteRevisionEffectsVerified:
				case CollectionOperationPhase.InstallingCandidateRevisionChildren:
				case CollectionOperationPhase.CandidateRevisionChildrenVerified:
				case CollectionOperationPhase.ReapplyingQualifiedRevisionOverrides:
				case CollectionOperationPhase.QualifiedRevisionOverridesVerified:
				case CollectionOperationPhase.RepairingQualifiedEffects:
					return CollectionWorkflowActivityPhase.Applying;
				case CollectionOperationPhase.Verifying:
				case CollectionOperationPhase.VerifyingCandidateRevisionAggregate:
				case CollectionOperationPhase.CandidateRevisionAggregateVerified:
				case CollectionOperationPhase.QualifiedEffectsVerified:
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

		/// <summary>
		/// Resolves the exact reviewed operation owned by the displayed incoming or installed Collection.
		/// </summary>
		private CollectionUiContext GetInstallActionContext()
		{
			CollectionUiContext context = _displayContext;
			if (_workflow == null || _operationIdentity == null || _reviewedPlanIdentity == null || _selectionDirty ||
				!IsActionContextCurrent(context) || _operationSnapshot == null || _operationSnapshot.IsTerminal ||
				!_operationSnapshot.Identity.Equals(_operationIdentity) || _operationSnapshot.PlanIdentity == null ||
				!_operationSnapshot.PlanIdentity.Equals(_reviewedPlanIdentity) || context.Revision == null ||
				_operationSnapshot.Revision == null || !context.Revision.Equals(_operationSnapshot.Revision))
				return null;

			if (context.Kind == CollectionUiContextKind.InstalledCollection)
			{
				if (_managedAssociationPresentation == null ||
					context.AssociationId != _managedAssociationPresentation.Association.AssociationId ||
					!context.Revision.Equals(_managedAssociationPresentation.Association.Association.Revision))
					return null;
			}
			else if (context.Kind != CollectionUiContextKind.IncomingCollection)
				return null;

			return context.WithOperation(_operationIdentity);
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
			_blockedRevisionUpdatePlan = null;
			_acquisitionRefreshTimer.Stop();
			_autoResumedQueueOperations.Clear();
			_preparation = null;
			_replacementReview = null;
			_revisionUpdateReview = null;
			_revisionUpdatePreparation = null;
			_revisionUpdateResult = null;
			_acquisitionBatch = null;
			_operationIdentity = null;
			_operationSnapshot = null;
			ClearIncomingDisplayOperation();
			_reviewedPlanIdentity = null;
			_selectionDirty = false;
			_selectionCapabilityBlocked = false;
			_optionalMemberSelectionState.Clear();
			_inheritedRevisionOptionalSelections.Clear();
			_inheritedRevisionPreview = null;
			_inheritedRevisionAssociationId = null;
			_inheritedInstalledRevision = null;
			_managedMemberExpansionAssociationId = null;
			_hasInterruptedReplacement = false;
			_workflowStatusLabel.Text = _workflow == null
				? L("Collections.Workflow.PreviewOnly", "Workflow: preview only - Collection installation is unavailable in this session.")
				: L("Collections.Workflow.Idle", "Workflow: idle");
			SetWorkflowActivity(CollectionWorkflowActivityBuilder.Idle(_workflowStatusLabel.Text));
		}

		private CollectionManagementLocalRestoreRecoverySource FindLocalRestoreRecoverySource(CollectionOperationIdentity operationIdentity)
		{
			if (operationIdentity == null || _localRestoreRecoverySources == null) return null;
			return _localRestoreRecoverySources.FirstOrDefault(x => x != null && x.OperationIdentity.Equals(operationIdentity)) ??
				(_stoppedLocalRestoreSource != null && _stoppedLocalRestoreSource.OperationIdentity.Equals(operationIdentity) ? _stoppedLocalRestoreSource : null);
		}

		private static string FormatLocalRestoreRecoveryActivity(IReadOnlyList<CollectionManagementLocalRestoreRecoverySource> sources)
		{
			if (sources == null || sources.Count == 0) return null;
			CollectionManagementLocalRestoreRecoverySource source = sources[0];
			string capture = source.CaptureIdentity == null ? "unresolved capture" : source.CaptureIdentity.ToString();
			return LanguageManager.Format("Collections.LocalRestore.InterruptedSourceActivity",
				"Interrupted Local Collection restore: {0} [{1}]. Review the source before resuming or stopping.", source.SourceDisplayName, capture);
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
			_deleteLocalCaptureButton.Enabled = !_workflowBusy && hasLocalCapture;
			if (!_workflowBusy)
				_deleteLocalCaptureButton.Text = L("Collections.Actions.DeleteLocal", "Delete Local Collection...");
			bool hasInterruptedLocalRestore = _localRestoreRecoverySources != null && _localRestoreRecoverySources.Count > 0;
			_localCaptureCombo.Enabled = !_workflowBusy && !hasInterruptedLocalRestore && _managementWorkflow != null && _localCaptureCombo.Properties.Items.Count > 0;
			_restoreLocalCaptureButton.Text = hasInterruptedLocalRestore
				? L("Collections.LocalRestore.ReviewInterrupted", "Review interrupted restore...")
				: L("Collections.Actions.RestoreLocal", "Restore Local Collection...");
			_restoreLocalCaptureButton.Enabled = !_workflowBusy && (hasInterruptedLocalRestore
				? _localRestoreRecoverySources.Count == 1 && _localRestoreRecoverySources[0].HasResolvedCapture &&
					String.IsNullOrEmpty(_localRestoreRecoverySources[0].Diagnostic)
				: hasLocalCapture && selectedCapture.Capability == LocalCaptureCapability.LocallyRestorableWithinScope);
			CollectionManagementLocalWorkingCopy selectedWorkingCopy = _localWorkingCopyCombo.SelectedItem as CollectionManagementLocalWorkingCopy;
			_localWorkingCopyCombo.Enabled = !_workflowBusy && _managementWorkflow != null && _localWorkingCopyCombo.Properties.Items.Count > 0;
			_editLocalWorkingCopyButton.Enabled = !_workflowBusy && _managementWorkflow != null && selectedWorkingCopy != null;
			_saveLocalWorkingCopyRevisionButton.Enabled = !_workflowBusy && _managementWorkflow != null && selectedWorkingCopy != null;

			CollectionManagementAssociation selectedAssociation = _managedAssociationCombo.SelectedItem as CollectionManagementAssociation;
			bool hasManagedAssociation = _managementWorkflow != null && selectedAssociation != null;
			_managedAssociationActionContext = hasManagedAssociation
				? CollectionUiContext.Installed(_previewGeneration, selectedAssociation.Association.Revision, selectedAssociation.AssociationId)
				: null;
			_managedAssociationCombo.Enabled = !_workflowBusy && _managementWorkflow != null && _managedAssociationCombo.Properties.Items.Count > 0;
			bool revisionChangeOwnsAssociation = hasManagedAssociation && selectedAssociation.PendingRevision != null;
			// A durable revision transition is the dominant workflow for this association. Hide unrelated setup/local
			// management groups instead of making the user scan a wall of disabled or irrelevant controls. Support
			// remains visible, and the Installed Collection selector remains available for context/navigation.
			_currentSetupGroup.Visible = !revisionChangeOwnsAssociation;
			_savedLocalGroup.Visible = !revisionChangeOwnsAssociation;
			_localWorkingCopyGroup.Visible = !revisionChangeOwnsAssociation;
			_installedGroup.Visible = true;
			_localCollectionsPage.PageVisible = !revisionChangeOwnsAssociation;
			_manageAssociationRemovalButton.Enabled = !_workflowBusy && hasManagedAssociation;
			_verifyRepairButton.Enabled = !_workflowBusy && hasManagedAssociation && !revisionChangeOwnsAssociation && _managedAssociationPresentation != null;
			_cloneManagedAssociationButton.Enabled = !_workflowBusy && hasManagedAssociation && !revisionChangeOwnsAssociation &&
				_managedAssociationPresentation != null && _managedAssociationPresentation.HasRetainedManifest;
			// Uninstall remains available to stop an interrupted transition. Other maintenance commands
			// reappear when the transition reaches a terminal state.
			_manageAssociationRemovalButton.Visible = true;
			_verifyRepairButton.Visible = !revisionChangeOwnsAssociation;
			_cloneManagedAssociationButton.Visible = !revisionChangeOwnsAssociation;
			CollectionManagementMemberPresentation selectedManagedMember = GetSelectedManagedMemberPresentation();
			bool canChangeMemberIntent = !_workflowBusy && hasManagedAssociation && !revisionChangeOwnsAssociation && selectedManagedMember != null &&
				selectedAssociation.State != CollectionAssociationState.Recovering;
			_showManagedMemberButton.Enabled = !_workflowBusy && hasManagedAssociation && selectedManagedMember != null;
			_showManagedMemberImpactButton.Enabled = !_workflowBusy && hasManagedAssociation && selectedManagedMember != null;
			NormalizedCollectionMember selectedNormalizedMember = GetSelectedNormalizedMember();
			_addManagedOptionalMemberButton.Enabled = !_workflowBusy && _workflow != null && hasManagedAssociation &&
				selectedAssociation.State == CollectionAssociationState.Applied && _managedAssociationPresentation != null &&
				_managedAssociationPresentation.HasRetainedManifest && selectedNormalizedMember != null &&
				selectedNormalizedMember.Requirement == CollectionMemberRequirement.Optional && selectedNormalizedMember.IdentityResolution.IsResolved &&
				!_managedAssociationPresentation.BoundMemberKeys.Contains(selectedNormalizedMember.IdentityResolution.Key);
			_removeManagedOptionalMemberButton.Enabled = !_workflowBusy && _managementWorkflow != null && hasManagedAssociation &&
				selectedAssociation.State != CollectionAssociationState.Recovering && selectedAssociation.State != CollectionAssociationState.Incomplete &&
				_managedAssociationPresentation != null && _managedAssociationPresentation.HasRetainedManifest &&
				selectedNormalizedMember != null && selectedNormalizedMember.Requirement == CollectionMemberRequirement.Optional &&
				selectedNormalizedMember.IdentityResolution.IsResolved && selectedManagedMember != null &&
				selectedManagedMember.MemberKey.Equals(selectedNormalizedMember.IdentityResolution.Key);
			_ignoreMemberDifferenceButton.Enabled = canChangeMemberIntent && selectedManagedMember.DriftObservations.Any(x =>
				CollectionMemberRequirementStates.IsIgnorableMemberDifference(x.Requirement.Aspect));
			_stopIgnoringMemberDifferenceButton.Enabled = canChangeMemberIntent && selectedManagedMember.UserOverrides.Any(x =>
				CollectionMemberRequirementStates.IsIgnorableMemberDifference(x.Requirement.Aspect));
			_acceptMemberDriftButton.Enabled = canChangeMemberIntent && selectedManagedMember.DriftObservations.Any(x =>
				!CollectionMemberRequirementStates.IsIgnorableMemberDifference(x.Requirement.Aspect));
			_clearMemberOverrideButton.Enabled = canChangeMemberIntent && selectedManagedMember.UserOverrides.Any(x =>
				!CollectionMemberRequirementStates.IsIgnorableMemberDifference(x.Requirement.Aspect));

			bool hasMemberList = _snapshot != null && _snapshot.HasManifestPreview;
			_memberSearchTextBox.Enabled = !_workflowBusy && hasMemberList;
			_memberFilterCombo.Enabled = !_workflowBusy && hasMemberList;

			bool hasConcreteRevision = _snapshot != null && _snapshot.HasConcreteRevision;
			_incomingActionContext = _displayContext != null && _displayContext.Kind == CollectionUiContextKind.IncomingCollection &&
				_displayContext.IsCurrentGeneration(_previewGeneration) ? _displayContext : null;
			if (_incomingActionContext != null && _operationIdentity != null)
				_incomingActionContext = _incomingActionContext.WithOperation(_operationIdentity);
			CollectionManagementAssociation matchingAssociation = FindMatchingManagedAssociation();
			bool expandingManagedAssociation = matchingAssociation != null && _managedMemberExpansionAssociationId.HasValue &&
				matchingAssociation.AssociationId == _managedMemberExpansionAssociationId.Value;
			bool sameRevisionAlreadyApplied = matchingAssociation != null && matchingAssociation.State == CollectionAssociationState.Applied &&
				!expandingManagedAssociation && FindInterruptedInstallation(matchingAssociation.Association.Revision) == null;
			bool installedAssociationView = _managedAssociationPresentation != null;
			CollectionRevisionUpdateWorkflowResult installedPending = installedAssociationView
				? FindInterruptedRevisionUpdateForAssociation(_managedAssociationPresentation.Association) : null;
			_previewContextLabel.Text = installedPending != null
				? GetInstalledRevisionUpdateContextLabel(installedPending)
				: installedAssociationView
					? L("Collections.Context.InstalledDetails", "Installed Collection details")
					: _displayContext != null && _displayContext.Kind == CollectionUiContextKind.SavedLocalCollection
						? L("Collections.Context.SavedLocalDetails", "Saved Local Collection details")
						: _snapshot == null && _completedRemovalReport != null
							? L("Collections.Context.RemovalResult", "Last Collection uninstall")
							: L("Collections.Context.Incoming", "Incoming Collection");
			string displayedRevision = _snapshot?.Revision == null ? null : FormatRevisionActionLabel(_snapshot.Revision.Identity);
			_previewSection.Text = installedPending != null
				? LanguageManager.Format("Collections.Context.SectionRevisionTransition", "{0} - {1} -> {2}", _previewContextLabel.Text,
					FormatRevisionActionLabel(_managedAssociationPresentation.Association.Association.Revision), FormatRevisionActionLabel(installedPending.Operation.Revision))
				: String.IsNullOrEmpty(displayedRevision) ? _previewContextLabel.Text
					: LanguageManager.Format("Collections.Context.SectionRevision", "{0} - {1}", _previewContextLabel.Text, displayedRevision);
			// The Collection and revision already appear in the incoming section caption. Duplicating that
			// long caption here clips the details heading under larger accessibility fonts.
			_detailsSection.Text = L("Collections.Context.MembersReview", "Collection mods and review");
			CollectionOperation installedPendingApply = installedAssociationView
				? FindInterruptedInstallation(_managedAssociationPresentation.Association.Association.Revision) : null;
			// Keep one stable action row below the status; the report remains available in every Collection context.
			_incomingActionsPanel.Visible = true;
			_advancedIncomingGroup.Visible = _incomingActionContext != null && installedPending == null;
			_advancedOptionsPage.PageVisible = _incomingActionContext != null && installedPending == null;
			CollectionManagementAssociation revisionUpdateSource = FindRevisionUpdateSourceAssociation();
			CollectionRevisionUpdateWorkflowResult interruptedRevisionUpdate = FindMatchingInterruptedRevisionUpdate();
			if (_incomingActionContext != null && _workflowBusy &&
				(revisionUpdateSource != null || _revisionUpdateReview != null || (_revisionUpdateResult ?? interruptedRevisionUpdate) != null))
			{
				_instructionLabel.Text = LanguageManager.Format("Collections.Update.WorkingTargetInstructions",
					"NMM is working on the change to {0}. Wait for the current step to finish. Any required input or stop reason will appear here.",
					FormatRevisionActionLabel(_snapshot.Revision.Identity));
			}
			else if (_incomingActionContext != null && _blockedRevisionUpdatePlan != null)
				_instructionLabel.Text = _blockedRevisionUpdatePlan.Members.Any(x => x.RequiresStandaloneUseConfirmation)
					? L("Collections.Update.BlockedOwnershipInstructions", "The revision comparison found mods whose ownership needs review. Choose Review Collection ownership... to decide separately which installations to keep independently. The installed revision has not changed.")
					: L("Collections.Update.BlockedReviewInstructions", "Resolve the named errors below before running the revision comparison again. The installed revision has not changed.");
			else if (_incomingActionContext != null && (_revisionUpdateResult ?? interruptedRevisionUpdate) != null &&
				!(_revisionUpdateResult ?? interruptedRevisionUpdate).IsCommitted)
			{
				CollectionRevisionUpdateWorkflowResult pending = _revisionUpdateResult ?? interruptedRevisionUpdate;
				_instructionLabel.Text = _workflowBusy
					? LanguageManager.Format("Collections.Update.WorkingTargetInstructions",
						"NMM is working on the change to {0}. Wait for the current step to finish. Any required input or stop reason will appear here.",
						FormatRevisionActionLabel(pending.Operation.Revision))
					: GetRevisionUpdateInstructions(pending.Status, pending.Operation.Revision, pending.Operation);
				_contentValue.Text = _workflowBusy
					? L("Collections.Update.CheckingChangeContent", "Checking revision change...")
					: GetRevisionUpdateContentText(pending.Status);
			}
			else if (_incomingActionContext != null && revisionUpdateSource != null)
				_instructionLabel.Text = L("Collections.Update.IncomingRevisionInstructions",
					"The incoming revision is shown above; the installed revision is shown in Installed Collection below. Choose Review revision change to compare them and review the changes before approving them.");
			CollectionRevisionUpdateWorkflowResult displayedRevisionUpdate = _revisionUpdateResult ?? interruptedRevisionUpdate;
			bool canStartRevisionUpdate = !_workflowBusy && _revisionUpdateWorkflow != null && _workflow != null && !installedAssociationView &&
				_incomingActionContext != null && hasConcreteRevision &&
				((displayedRevisionUpdate != null && !displayedRevisionUpdate.IsCommitted) ||
				 (revisionUpdateSource != null && CanSupersedePreparationForRevisionChange(_operationSnapshot)));
			bool canChooseRetainedRevision = !_workflowBusy && installedAssociationView && installedPending == null && installedPendingApply == null &&
				_revisionUpdateWorkflow != null && _workflow != null && hasManagedAssociation && !revisionChangeOwnsAssociation &&
				selectedAssociation.Association.Revision.Collection.Origin == CollectionOrigin.NexusMods &&
				selectedAssociation.State != CollectionAssociationState.Recovering && selectedAssociation.State != CollectionAssociationState.Incomplete;
			_compareUpdateButton.Enabled = canStartRevisionUpdate || canChooseRetainedRevision ||
				(!_workflowBusy && installedPending != null && _revisionUpdateWorkflow != null);
			// During an installed pending revision change the same action is promoted to the bold contextual button above.
			// Keep the ordinary toolbar Compare / Update command out of the way instead of presenting two competing actions.
			_compareUpdateButton.Visible = _incomingActionContext == null && installedPending == null;
			if (displayedRevisionUpdate != null && !displayedRevisionUpdate.IsCommitted)
				_compareUpdateButton.Text = GetRevisionUpdateActionText(displayedRevisionUpdate.Status, displayedRevisionUpdate.Operation.Revision, displayedRevisionUpdate.Operation);
			else
				_compareUpdateButton.Text = installedPending != null
					? GetRevisionUpdateActionText(installedPending.Status, installedPending.Operation.Revision, installedPending.Operation)
					: L("Collections.Actions.CompareUpdate", "Compare / Update...");
			if (_workflowBusy && _incomingActionContext != null &&
				(revisionUpdateSource != null || displayedRevisionUpdate != null))
				_compareUpdateButton.Text = GetBusyIncomingActionText();
			_importButton.Enabled = !_workflowBusy && hasConcreteRevision && !installedAssociationView && !sameRevisionAlreadyApplied;
			_downloadPrepareButton.Enabled = !_workflowBusy && _workflow != null && hasConcreteRevision && !installedAssociationView && !sameRevisionAlreadyApplied && !_selectionCapabilityBlocked &&
				revisionUpdateSource == null && interruptedRevisionUpdate == null &&
				(_acquisitionBatch == null || _selectionDirty || _acquisitionBatch.Members.Any(x => x.Disposition == CollectionMemberAcquisitionDisposition.RestartActionRequired) ||
				 (_preparation != null &&
				 (_preparation.Status == CollectionAdditiveWorkflowPreparationStatus.PreparationRequired ||
				  _preparation.Status == CollectionAdditiveWorkflowPreparationStatus.ActionRequired ||
				  _preparation.Status == CollectionAdditiveWorkflowPreparationStatus.Blocked)));
			bool hasResolvableFileConflict = _preparation != null && _preparation.ImpactPlan != null &&
				_preparation.ImpactPlan.Issues.Any(x => x.Kind == CollectionConflictImpactIssueKind.ExistingFileWinnerDecisionRequired);
			_resolveFileConflictsButton.Visible = false;
			_resolveFileConflictsButton.Enabled = !_workflowBusy && hasResolvableFileConflict && !installedAssociationView && !sameRevisionAlreadyApplied;
			_autoOverwriteArchivesCheckBox.Enabled = _downloadPrepareButton.Enabled || _compareUpdateButton.Enabled;
			bool resumeRequired = CanResumePreparation(_acquisitionBatch);
			_resumeButton.Visible = false;
			_resumeButton.Enabled = !_workflowBusy && resumeRequired;
			CollectionManualAcquisitionPendingAction pendingDownload = GetSelectedOrFirstPendingAction();
			_openPendingButton.Visible = false;
			_openPendingButton.Enabled = !_workflowBusy && pendingDownload != null &&
				(pendingDownload.Supports(CollectionManualAcquisitionActionKind.Browser) ||
				 pendingDownload.Supports(CollectionManualAcquisitionActionKind.LocalFile));
			UpdatePendingDownloadActionLabel(pendingDownload);
			bool exactReview = GetInstallActionContext() != null && _operationSnapshot != null &&
				_operationSnapshot.Kind == CollectionOperationKind.ApplyResolvedPlan;
			_installButton.Enabled = !_workflowBusy && exactReview && !sameRevisionAlreadyApplied;
			bool replacementReady = !_workflowBusy && _replacementWorkflow != null && _workflow != null && !installedAssociationView &&
				!_hasInterruptedReplacement && !sameRevisionAlreadyApplied && !_selectionDirty && _replacementReview == null && _preparation != null &&
				_preparation.IsReadyForReview && _preparation.Runtime != null;
			bool replacementNeedsNewReview = _replacementReview != null && _operationSnapshot != null &&
				(_operationSnapshot.Phase == CollectionOperationPhase.AwaitingReplacementPhaseAmendment ||
				 _operationSnapshot.Phase == CollectionOperationPhase.ReplacementBarrierRevalidationRequired);
			bool replacementCanContinue = !_workflowBusy && _replacementWorkflow != null && _replacementReview != null && _operationSnapshot != null &&
				!_operationSnapshot.IsTerminal && !replacementNeedsNewReview;
			_replaceButton.Enabled = replacementReady || replacementCanContinue || (!_workflowBusy && replacementNeedsNewReview);
			_replacementBackupCheckBox.Enabled = replacementReady;
			if (replacementNeedsNewReview)
				_replaceButton.Text = L("Collections.Replace.ActionRequiredButton", "Replacement review required");
			else if (replacementCanContinue && (_operationSnapshot.RequiresRecovery || _operationSnapshot.ResultState == CollectionOperationResultState.RecoveryRequired))
				_replaceButton.Text = L("Collections.Replace.RecoverButton", "Check replacement recovery...");
			else if (replacementCanContinue)
				_replaceButton.Text = L("Collections.Replace.ResumeButton", "Resume replacement...");
			else
				_replaceButton.Text = L("Collections.Actions.ReplaceCurrent", "Replace current managed setup...");
			if (sameRevisionAlreadyApplied)
				_installButton.Text = L("Collections.Actions.AlreadyApplied", "Already applied");
			else if (expandingManagedAssociation)
				_installButton.Text = L("Collections.Actions.AddOptionalMemberReview", "Review and add member...");
			else if (exactReview && (_operationSnapshot.RequiresRecovery || _operationSnapshot.HasUnreconciledNativeChild))
				_installButton.Text = L("Collections.Actions.CheckRecoveryContinue", "Check recovery and continue...");
			else if (exactReview && (_preparation == null || _operationSnapshot.HasCrossedNativeBoundary))
				_installButton.Text = L("Collections.Actions.ResumeApply", "Review and continue...");
			else
				_installButton.Text = L("Collections.Actions.InstallCurrent", "Review and install...");
			_clearButton.Enabled = !_workflowBusy && !installedAssociationView && (_snapshot != null || _operationIdentity != null || _preparation != null ||
				_replacementReview != null || _revisionUpdateReview != null || _revisionUpdatePreparation != null || _revisionUpdateResult != null || _acquisitionBatch != null);
			_exportTechnicalReportButton.Enabled = HasTechnicalReportContent();

			// Keep rare/destructive controls out of the way until the current incoming workflow can actually use them.
			_importButton.Visible = !installedAssociationView && hasConcreteRevision;
			_autoOverwriteArchivesCheckBox.Visible = !installedAssociationView && (_downloadPrepareButton.Enabled || _compareUpdateButton.Enabled || _acquisitionBatch != null);
			_replacementBackupCheckBox.Visible = replacementReady;
			_replaceButton.Visible = replacementReady || replacementCanContinue || replacementNeedsNewReview;
			_clearButton.Visible = _clearButton.Enabled && !installedAssociationView;
			UpdatePrimaryIncomingAction(sameRevisionAlreadyApplied, hasConcreteRevision, installedAssociationView);
			UpdateMemberActionMenuItems();

			// Keep the member list navigable after apply/recovery. ItemCheck separately enforces read-only selection once
			// native mutation has crossed the boundary, so disabling the entire grid only breaks scrolling and inspection.
			_membersGrid.Enabled = !_workflowBusy;
			bool canChooseMods = _incomingActionContext != null && _managedAssociationPresentation == null &&
				(_operationSnapshot == null || (!_operationSnapshot.HasCrossedNativeBoundary && !_operationSnapshot.IsSuccessful));
			_membersPage.Text = installedAssociationView ? L("Collections.Tabs.InstalledMods", "Installed mods")
				: canChooseMods ? L("Collections.Tabs.Mods", "Choose mods") : L("Collections.Tabs.CollectionMods", "Collection mods");
			_memberSelectionHint.Text = canChooseMods
				? L("Collections.Members.OptionalHint", "Required mods are included. Tick optional mods you want to use.")
				: L("Collections.Members.InspectHint", "Right-click a mod for available actions.");
			if (!_workflowBusy)
			{
				bool readyForReview = _reviewActionCount > 0 && _reviewErrorCount == 0 &&
					(ReferenceEquals(_primaryIncomingActionSource, _installButton) ||
					 (ReferenceEquals(_primaryIncomingActionSource, _compareUpdateButton) && _revisionUpdateReview != null &&
					  _revisionUpdateReview.Operation.Phase == CollectionOperationPhase.ReadyForReview));
				if (_reviewErrorCount > 0 && (_lastPresentedReviewErrorCount == 0 || _reviewNavigationPending))
					_reviewTabs.SelectedTabPage = _issuesPage;
				else if (readyForReview && (!_wasReadyForReview || _reviewNavigationPending))
					_reviewTabs.SelectedTabPage = _reviewActionsPage;
				_lastPresentedReviewErrorCount = _reviewErrorCount;
				_wasReadyForReview = readyForReview;
				_reviewNavigationPending = false;
			}
		}

		private string GetInstalledRevisionUpdateContextLabel(CollectionRevisionUpdateWorkflowResult pending)
		{
			if (pending == null) return L("Collections.Context.RevisionChangeInProgress", "Revision change in progress");
			switch (pending.Status)
			{
				case CollectionRevisionUpdateWorkflowStatus.ReadyForReview:
					return L("Collections.Context.RevisionChangeReady", "Revision change ready for review");
				case CollectionRevisionUpdateWorkflowStatus.AwaitingInput:
					return L("Collections.Context.RevisionChangeWaiting", "Revision change waiting for files");
				case CollectionRevisionUpdateWorkflowStatus.ExplicitReviewRequired:
					return L("Collections.Context.RevisionChangeBlocked", "Revision change blocked");
				case CollectionRevisionUpdateWorkflowStatus.RecoveryRequired:
					return L("Collections.Context.RevisionChangeRecovery", "Revision change requires recovery");
				case CollectionRevisionUpdateWorkflowStatus.PausedAtSafeBoundary:
					return L("Collections.Context.RevisionChangePaused", "Revision change paused safely");
				default:
					return L("Collections.Context.RevisionChangeInProgress", "Revision change in progress");
			}
		}

		private void UpdatePrimaryIncomingAction(bool sameRevisionAlreadyApplied, bool hasConcreteRevision, bool installedAssociationView)
		{
			_primaryIncomingActionSource = null;
			_primaryIncomingReviewsBlockedIssues = false;
			_primaryIncomingReviewsRevisionOwnership = false;
			SimpleButton source = null;
			CollectionRevisionUpdateWorkflowResult installedPending = installedAssociationView && _managedAssociationPresentation != null
				? FindInterruptedRevisionUpdateForAssociation(_managedAssociationPresentation.Association) : null;
			if (installedPending != null)
			{
				_primaryIncomingActionButton.Visible = true;
				if (_workflowBusy)
				{
					_primaryIncomingActionButton.Enabled = false;
					_primaryIncomingActionButton.Text = LanguageManager.Format("Collections.Update.CheckingTargetRevisionButton",
						"Checking interrupted change to {0}...", FormatRevisionActionLabel(installedPending.Operation.Revision));
					return;
				}
				if (installedPending.Status == CollectionRevisionUpdateWorkflowStatus.RecoveryRequired &&
					!CanRecheckReportedSuccessfulRevisionChild(installedPending.Operation))
				{
					_primaryIncomingReviewsBlockedIssues = true;
					_primaryIncomingActionButton.Enabled = !_workflowBusy && _reviewErrorCount > 0;
					_primaryIncomingActionButton.Text = L("Collections.Actions.ReviewRecoveryIssue", "Review recovery issue");
					return;
				}

				_primaryIncomingActionSource = _compareUpdateButton;
				_primaryIncomingActionButton.Enabled = !_workflowBusy && _compareUpdateButton.Enabled;
				_primaryIncomingActionButton.Text = GetRevisionUpdateActionText(installedPending.Status, installedPending.Operation.Revision, installedPending.Operation);
				return;
			}
			if (installedAssociationView && _managedAssociationPresentation != null &&
				FindInterruptedInstallation(_managedAssociationPresentation.Association.Association.Revision) != null)
			{
				_primaryIncomingActionSource = _installButton;
				_primaryIncomingActionButton.Visible = true;
				_primaryIncomingActionButton.Enabled = !_workflowBusy && _installButton.Enabled;
				_primaryIncomingActionButton.Text = _workflowBusy ? GetBusyIncomingActionText() : _installButton.Text;
				return;
			}
			if (_workflowBusy)
			{
				_primaryIncomingActionButton.Visible = !installedAssociationView && hasConcreteRevision;
				_primaryIncomingActionButton.Enabled = false;
				_primaryIncomingActionButton.Text = GetBusyIncomingActionText();
				return;
			}
			CollectionRevisionUpdateWorkflowResult displayedRevisionUpdate = _revisionUpdateResult ?? FindMatchingInterruptedRevisionUpdate();
			if (!_workflowBusy && _blockedRevisionUpdatePlan != null)
			{
				_primaryIncomingReviewsRevisionOwnership = _blockedRevisionUpdatePlan.Members.Any(x => x.RequiresStandaloneUseConfirmation);
				_primaryIncomingReviewsBlockedIssues = !_primaryIncomingReviewsRevisionOwnership;
			}
			else if (!_workflowBusy && displayedRevisionUpdate != null &&
				displayedRevisionUpdate.Status == CollectionRevisionUpdateWorkflowStatus.RecoveryRequired &&
				!CanRecheckReportedSuccessfulRevisionChild(displayedRevisionUpdate.Operation))
				_primaryIncomingReviewsBlockedIssues = true;
			else if (_compareUpdateButton.Enabled)
				source = _compareUpdateButton;
			else if (_resolveFileConflictsButton.Enabled)
				source = _resolveFileConflictsButton;
			else if (!_workflowBusy && !_selectionDirty &&
				((_preparation != null && _preparation.Status == CollectionAdditiveWorkflowPreparationStatus.Blocked) ||
				 (_acquisitionBatch != null && _acquisitionBatch.HasBlockedMembers)))
				_primaryIncomingReviewsBlockedIssues = true;
			else if (_openPendingButton.Enabled)
				source = _openPendingButton;
			else if (_selectionDirty && _downloadPrepareButton.Enabled)
				source = _downloadPrepareButton;
			else if (_resumeButton.Enabled)
				source = _resumeButton;
			else if (_installButton.Enabled)
				source = _installButton;
			else if (_downloadPrepareButton.Enabled)
				source = _downloadPrepareButton;

			_primaryIncomingActionSource = source;
			_primaryIncomingActionButton.Visible = !installedAssociationView && hasConcreteRevision;
			_primaryIncomingActionButton.Enabled = _primaryIncomingReviewsRevisionOwnership || _primaryIncomingReviewsBlockedIssues || (source != null && source.Enabled);
			if (_primaryIncomingReviewsRevisionOwnership)
			{
				_primaryIncomingActionButton.Text = L("Collections.Update.ReviewOwnership", "Review Collection ownership...");
				return;
			}
			if (_primaryIncomingReviewsBlockedIssues)
			{
				_primaryIncomingActionButton.Text = displayedRevisionUpdate != null &&
					displayedRevisionUpdate.Status == CollectionRevisionUpdateWorkflowStatus.RecoveryRequired
					? GetRevisionUpdateActionText(displayedRevisionUpdate.Status, displayedRevisionUpdate.Operation.Revision, displayedRevisionUpdate.Operation)
					: L("Collections.Actions.ReviewBlockingIssues", "Review blocking issues");
				return;
			}
			if (source == null)
			{
				_primaryIncomingActionButton.Text = sameRevisionAlreadyApplied
					? L("Collections.Actions.AlreadyApplied", "Already applied")
					: _downloadPrepareButton.Text;
				return;
			}

			if (ReferenceEquals(source, _compareUpdateButton))
			{
				_primaryIncomingActionButton.Text = (_revisionUpdateResult ?? FindMatchingInterruptedRevisionUpdate()) != null
					? source.Text
					: L("Collections.Update.ReviewRevisionChangeButton", "Review revision change...");
				return;
			}
			_primaryIncomingActionButton.Text = ReferenceEquals(source, _downloadPrepareButton) && _selectionDirty
				? L("Collections.Actions.PrepareUpdatedSelection", "Prepare updated selection")
				: source.Text;
		}

		/// <summary>Names the current foreground phase while the incoming action is disabled.</summary>
		private string GetBusyIncomingActionText()
		{
			CollectionWorkflowActivityPhase phase = _workflowActivity == null ? CollectionWorkflowActivityPhase.None : _workflowActivity.Phase;
			switch (phase)
			{
				case CollectionWorkflowActivityPhase.Importing: return L("Collections.Busy.Importing", "Importing Collection...");
				case CollectionWorkflowActivityPhase.Preparing: return L("Collections.Busy.Preparing", "Preparing mod files...");
				case CollectionWorkflowActivityPhase.Acquiring: return L("Collections.Busy.Downloading", "Downloading mod files...");
				case CollectionWorkflowActivityPhase.Reviewing: return L("Collections.Busy.Reviewing", "Reviewing changes...");
				case CollectionWorkflowActivityPhase.Applying: return L("Collections.Busy.Applying", "Applying approved changes...");
				case CollectionWorkflowActivityPhase.Verifying: return L("Collections.Busy.Verifying", "Verifying Collection...");
				case CollectionWorkflowActivityPhase.Recovering: return L("Collections.Busy.Continuing", "Checking and continuing...");
				case CollectionWorkflowActivityPhase.Capturing: return L("Collections.Busy.Capturing", "Saving Local Collection...");
				case CollectionWorkflowActivityPhase.Restoring: return L("Collections.Busy.Restoring", "Restoring Local Collection...");
				case CollectionWorkflowActivityPhase.Managing: return L("Collections.Busy.Managing", "Saving choices...");
				default: return L("Collections.Busy.Working", "Working...");
			}
		}

		private CollectionManagementAssociation FindMatchingManagedAssociation()
		{
			if (_managedAssociationPresentation != null)
				return _managedAssociationPresentation.Association;
			if (_snapshot == null || _snapshot.Revision == null)
				return null;
			foreach (object item in _managedAssociationCombo.Properties.Items)
			{
				CollectionManagementAssociation association = item as CollectionManagementAssociation;
				if (association != null && association.Association.Revision.Equals(_snapshot.Revision.Identity))
					return association;
			}
			return null;
		}

		/// <summary>Restores the stopped workflow's content status after its temporary busy caption.</summary>
		private static string GetRevisionUpdateContentText(CollectionRevisionUpdateWorkflowStatus status)
		{
			return status == CollectionRevisionUpdateWorkflowStatus.AwaitingInput
				? L("Collections.Update.WaitingForFiles", "Waiting for revision files")
				: status == CollectionRevisionUpdateWorkflowStatus.RecoveryRequired
					? L("Collections.Update.NeedsRecovery", "Revision change needs recovery")
					: L("Collections.Update.ChangeNotFinished", "Revision change not finished");
		}

		/// <summary>Offers a verification recheck only for a reported-successful child with unresolved durability.</summary>
		private static bool CanRecheckReportedSuccessfulRevisionChild(CollectionOperation operation)
		{
			if (operation == null || operation.IsTerminal || operation.Kind != CollectionOperationKind.UpdateRevision)
				return false;
			CollectionNativeChildOperation child = operation.NativeChildren.LastOrDefault(x => !x.IsReconciled);
			return child != null && child.Action == CollectionNativeChildAction.ActivateOrReinstall &&
				child.Checkpoint == CollectionNativeChildCheckpoint.NativeTerminalObserved && child.NativeResult != null &&
				child.NativeResult.ReportedStatus == ModOperationReportedStatus.Succeeded &&
				child.NativeResult.Durability == ModOperationDurability.Unknown;
		}

		/// <summary>Uses the same pending-revision action caption in buttons and their instructions.</summary>
		private static string GetRevisionUpdateActionText(CollectionRevisionUpdateWorkflowStatus status, CollectionRevisionIdentity revision,
			CollectionOperation operation = null)
		{
			string target = FormatRevisionActionLabel(revision);
			if (status == CollectionRevisionUpdateWorkflowStatus.RecoveryRequired)
				return CanRecheckReportedSuccessfulRevisionChild(operation)
					? L("Collections.Update.CheckInstalledFilesAndContinue", "Check installed files and continue...")
					: L("Collections.Actions.ReviewRecoveryIssue", "Review recovery issue");
			if (status == CollectionRevisionUpdateWorkflowStatus.ReadyForReview)
				return LanguageManager.Format("Collections.Update.ReviewApproveTargetRevisionButton", "Review and approve change to {0}...", target);
			if (status == CollectionRevisionUpdateWorkflowStatus.ExplicitReviewRequired)
				return LanguageManager.Format("Collections.Update.RecheckTargetRevisionButton", "Recheck and continue change to {0}...", target);
			return LanguageManager.Format("Collections.Update.ContinueTargetRevisionButton", "Continue change to {0}...", target);
		}

		/// <summary>Explains the next action using the exact caption of the pending-revision button.</summary>
		private static string GetRevisionUpdateInstructions(CollectionRevisionUpdateWorkflowStatus status, CollectionRevisionIdentity revision,
			CollectionOperation operation = null)
		{
			string action = GetRevisionUpdateActionText(status, revision, operation);
			if (status == CollectionRevisionUpdateWorkflowStatus.AwaitingInput)
				return LanguageManager.Format("Collections.Update.DownloadTargetInstructions",
					"This revision change has been approved. Once the required files are ready, choose '{0}' to verify them and install the approved changes.", action);
			if (status == CollectionRevisionUpdateWorkflowStatus.ReadyForReview)
				return LanguageManager.Format("Collections.Update.ApprovalTargetInstructions",
					"Choose '{0}' to review and approve the remaining changes.", action);
			if (status == CollectionRevisionUpdateWorkflowStatus.ExplicitReviewRequired)
				return LanguageManager.Format("Collections.Update.BlockedTargetInstructions",
					"Review the named mods and files under Review / issues. After resolving those issues, choose '{0}' to repeat the checks. If no supported action is available, export a Technical Report.", action);
			if (status == CollectionRevisionUpdateWorkflowStatus.RecoveryRequired && CanRecheckReportedSuccessfulRevisionChild(operation))
				return LanguageManager.Format("Collections.Update.RecheckSuccessfulChildInstructions",
					"NMM could not confirm an installed mod. Choose '{0}' to check its files and try to finish this change. Completed steps are retained. See Review / issues if it still cannot continue.", action);
			if (status == CollectionRevisionUpdateWorkflowStatus.RecoveryRequired)
				return LanguageManager.Format("Collections.Update.RecoveryTargetInstructions",
					"This revision change needs recovery. Choose '{0}' to inspect the reason and available action. Completed steps are retained.", action);
			return LanguageManager.Format("Collections.Update.ContinueTargetInstructions",
				"This revision change is incomplete. Choose '{0}' to check completed steps and continue. Any stop reason appears under Review / issues. The installed selector keeps the previous revision until the change finishes.", action);
		}

		private static string FormatRevisionActionLabel(CollectionRevisionIdentity revision)
		{
			if (revision == null) return "revision";
			return revision.NexusRevisionNumber.HasValue
				? "Revision #" + revision.NexusRevisionNumber.Value.ToString(CultureInfo.InvariantCulture)
				: "Revision " + revision.StableRevisionId;
		}

		private void ApplyManagedAssociationPresentation()
		{
			CollectionManagementAssociation association = FindMatchingManagedAssociation();
			if (association == null)
				return;

			if (association.PendingRevision != null)
			{
				_revisionValue.Text = LanguageManager.Format("Collections.Update.RevisionTransitionValue", "{0} -> {1} (change incomplete)",
					FormatRevisionActionLabel(association.Association.Revision), FormatRevisionActionLabel(association.PendingRevision));
				_appliedValue.Text = L("Collections.Update.PreviousRevisionPending", "Previous revision recorded; revision change incomplete");
				_contentValue.Text = L("Collections.Update.InstalledContentPending", "Revision change unfinished; current setup not verified");
				_compatibilityValue.Text = L("Collections.Update.InstalledCompatibilityPending", "Current setup verification deferred until revision change completes");
				return;
			}

			CollectionOperation pendingApply = FindInterruptedInstallation(association.Association.Revision);
			if (pendingApply != null)
			{
				_appliedValue.Text = L("Collections.Status.Applied.InterruptedInstallation", "Installation interrupted - completed mods are kept");
				_contentValue.Text = L("Collections.Status.Content.InterruptedInstallation", "Check the interrupted installation before continuing");
				_compatibilityValue.Text = L("Collections.VerifyRepair.IncompleteCompatibility", "Current setup not yet verified");
				string nextAction = pendingApply.RequiresRecovery || pendingApply.HasUnreconciledNativeChild
					? L("Collections.Actions.CheckRecoveryContinue", "Check recovery and continue...")
					: L("Collections.Actions.ResumeApply", "Review and continue...");
				_instructionLabel.Text = LanguageManager.Format("Collections.Management.InterruptedInstallationActionInstructions",
					"Choose '{0}' to check the unfinished installation. To undo it, choose Uninstall Collection; NMM will check the interrupted step before showing which mods it can remove.", nextAction);
				return;
			}
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
					_appliedValue.Text = L("Collections.Status.Applied.IncompleteVerify", "Incomplete - use Verify / Repair");
					_contentValue.Text = L("Collections.VerifyRepair.IncompleteContent", "Current setup incomplete - verification required");
					_compatibilityValue.Text = L("Collections.VerifyRepair.IncompleteCompatibility", "Current setup not yet verified");
					break;
				case CollectionAssociationState.Recovering:
					_appliedValue.Text = L("Collections.Status.Applied.RecoveryRequired", "Recovery required before further mutation");
					break;
			}
		}

		private void UpdateMemberSelectionText()
		{
			_membersView.RefreshData();
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
			_memberRows.Clear();
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
			_memberRows.Clear();
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
			_memberRows.Clear();
			ClearReviewItems();
			_membersHeader.Text = L("Collections.Preview.Members", "Members");
			_issuesHeader.Text = L("Collections.Preview.Issues", "Review / issues");
			if (_completedRemovalReport != null)
			{
				_collectionValue.Text = _completedRemovalReport.Collection.DisplayName;
				_revisionValue.Text = _completedRemovalReport.Collection.RevisionLabel;
				_compatibilityValue.Text = _completedRemovalReport.Context.CompatibilityStatus;
				_contentValue.Text = _completedRemovalReport.Context.ContentStatus;
				_appliedValue.Text = _completedRemovalReport.Context.AppliedStatus;
				_summaryBox.Text = L("Collections.Management.RemovalResultHelp", "Uninstall finished. The list below shows which mods were removed or kept, and why. Export Technical Report remains available for this result.");
				_workflowStatusLabel.Text = _completedRemovalReport.Progress.WorkflowStatus;
				foreach (CollectionReviewItem item in _completedRemovalItems) AddReviewItem(item);
			}
		}

		private void SetDefaultInstruction()
		{
			_instructionLabel.Text = L("Collections.Preview.Instructions",
				"Open a Nexus Collection NXM link to load it, choose optional mods, download and prepare, review the changes, then install. Select a saved Local Collection to review and restore it.");
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
			string message = ResolveUserMessage(presentation);
			string nextAction = ResolveNextAction(presentation);
			_workflowStatusLabel.Text = String.IsNullOrWhiteSpace(nextAction)
				? LanguageManager.Format("Collections.Workflow.UserStatus", "Workflow: {0}", message)
				: LanguageManager.Format("Collections.Workflow.UserStatusWithNextAction", "Workflow: {0} {1}", message, nextAction);
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
				_reviewRows.Add(CreateReviewGridRow(reviewItem));
			}
			else if (CollectionReviewPresentationFilter.IsSeverityFiltered(reviewItem) && ReviewSeverityIsVisible(reviewItem))
			{
				RemoveFilteredEmptyMessage();
				int priority = CollectionReviewPresentationFilter.GetIssuePriority(reviewItem);
				int index = _issueRows.ToList().FindIndex(row => row.Item != null &&
					CollectionReviewPresentationFilter.GetIssuePriority(row.Item) > priority);
				if (index < 0) _issueRows.Add(CreateReviewGridRow(reviewItem));
				else _issueRows.Insert(index, CreateReviewGridRow(reviewItem));
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
			_issueRows.Clear();
			_reviewRows.Clear();
			UpdateReviewFilterLabels();
			UpdateReviewActionsVisibility();
		}

		private void ReviewSeverityFilter_CheckedChanged(object sender, EventArgs e)
		{
			RefreshFilteredReviewIssues();
		}

		private void RefreshFilteredReviewIssues()
		{
			_issuesView.BeginDataUpdate();
			try
			{
				_issueRows.Clear();
				foreach (CollectionReviewItem reviewItem in _reviewItems.OrderBy(CollectionReviewPresentationFilter.GetIssuePriority))
				{
					if (CollectionReviewPresentationFilter.IsSeverityFiltered(reviewItem) && ReviewSeverityIsVisible(reviewItem))
						_issueRows.Add(CreateReviewGridRow(reviewItem));
				}
			}
			finally
			{
				_issuesView.EndDataUpdate();
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
			if (_issueRows.Count > 0)
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

			_issueRows.Add(new ReviewGridRow(null, message, String.Empty, String.Empty, String.Empty, null));
		}

		private void RemoveFilteredEmptyMessage()
		{
			if (_issueRows.Count == 1 && _issueRows[0].Item == null)
				_issueRows.Clear();
		}

		private void UpdateIssuesHeader()
		{
			int issueCount = _reviewErrorCount + _reviewWarningCount + _reviewInfoCount;
			_issuesHeader.Text = LanguageManager.Format("Collections.Preview.IssueCount", "Review / issues ({0})", issueCount);
			_issuesPage.Text = _issuesHeader.Text;
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
			EnsureCheckCaptionFits(_showErrorIssuesCheckBox);
			EnsureCheckCaptionFits(_showWarningIssuesCheckBox);
			EnsureCheckCaptionFits(_showInfoIssuesCheckBox);
		}

		private void UpdateReviewActionsVisibility()
		{
			bool visible = _reviewActionCount > 0;
			_reviewActionsHeader.Text = _snapshot == null && _managedAssociationPresentation == null && _completedRemovalReport != null
				? LanguageManager.Format("Collections.Management.Removal.ResultsCount", "Uninstall results ({0})", _reviewActionCount)
				: LanguageManager.Format("Collections.Review.ActionsCount", "Required actions / planned changes ({0})", _reviewActionCount);
			_reviewActionsHeader.Visible = visible;
			_reviewActionsGrid.Visible = visible;

			_reviewActionsPage.Text = _snapshot == null && _managedAssociationPresentation == null && _completedRemovalReport != null
				? LanguageManager.Format("Collections.Tabs.UninstallCount", "Uninstall results ({0})", _reviewActionCount)
				: LanguageManager.Format("Collections.Tabs.ChangesCount", "Changes to review ({0})", _reviewActionCount);
			_reviewActionsPage.PageVisible = visible;
			if (visible && _snapshot == null && _managedAssociationPresentation == null && _completedRemovalReport != null)
				_reviewTabs.SelectedTabPage = _reviewActionsPage;
		}

		private static ReviewGridRow CreateReviewGridRow(CollectionReviewItem reviewItem)
		{
			string subject = FormatCuratorMarkdown(reviewItem.Subject, false);
			string explanation = FormatCuratorMarkdown(reviewItem.Explanation, false);
			string nextAction = FormatCuratorMarkdown(reviewItem.NextAction, false);
			var tooltip = new StringBuilder();
			tooltip.AppendLine(reviewItem.StatusText);
			if (!String.IsNullOrWhiteSpace(reviewItem.Subject)) tooltip.AppendLine(FormatCuratorMarkdown(reviewItem.Subject, true));
			if (!String.IsNullOrWhiteSpace(reviewItem.Explanation)) tooltip.AppendLine(FormatCuratorMarkdown(reviewItem.Explanation, true));
			if (!String.IsNullOrWhiteSpace(reviewItem.NextAction)) tooltip.Append(FormatCuratorMarkdown(reviewItem.NextAction, true));
			return new ReviewGridRow(reviewItem, reviewItem.StatusText, subject, explanation, nextAction, tooltip.ToString().TrimEnd());
		}

		/// <summary>
		/// Presents the safe Markdown subset used by Nexus/Vortex Collection guidance without exposing raw markup in WinForms.
		/// Images are never fetched by this control. Links keep their label in the grid and expose the URL only in the tooltip.
		/// </summary>
		private static string FormatCuratorMarkdown(string value, bool includeLinkTargets)
		{
			if (String.IsNullOrWhiteSpace(value))
				return String.Empty;

			string formatted = Regex.Replace(value, @"!\[([^\]]*)\]\((https?://[^)\s]+)\)", match =>
			{
				if (!includeLinkTargets)
					return String.Empty;
				string alt = match.Groups[1].Value.Trim();
				return String.IsNullOrWhiteSpace(alt)
					? L("Collections.Guidance.RemoteImage", "Remote image") + " - " + match.Groups[2].Value
					: alt + " - " + match.Groups[2].Value;
			}, RegexOptions.IgnoreCase);
			formatted = Regex.Replace(formatted, @"\[([^\]]+)\]\((https?://[^)\s]+)\)", match =>
				includeLinkTargets ? match.Groups[1].Value + " - " + match.Groups[2].Value : match.Groups[1].Value, RegexOptions.IgnoreCase);
			formatted = formatted.Replace("**", String.Empty).Replace("__", String.Empty).Replace("`", String.Empty);
			formatted = Regex.Replace(formatted, @"<[^>]+>", String.Empty);
			if (includeLinkTargets)
			{
				formatted = Regex.Replace(formatted, @"[ \t]+", " ");
				formatted = Regex.Replace(formatted, @"[ \t]*\r?\n[ \t]*", Environment.NewLine);
			}
			else
			{
				formatted = Regex.Replace(formatted, @"\s+", " ");
			}
			return formatted.Trim();
		}

		private string GetSelectedMemberToken()
		{
			NormalizedCollectionMember member = GetSelectedNormalizedMember();
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

		/// <summary>Shows a short, explicit revision approval with a scrollable list of independently preserved mods.</summary>
		private bool ConfirmRevisionUpdate(CollectionRevisionUpdatePlan plan, int added, int removed, int changed,
			int overrideCount, IEnumerable<CollectionRevisionUpdateMemberPlan> preservedStandalone)
		{
			List<string> preservedNames = preservedStandalone.Select(FormatRevisionUpdateMemberSubject).ToList();
			using (DevExpressDisplaySettings settings = DevExpressDisplaySettings.CreateFromSettings(Properties.Settings.Default))
			using (ManagedFontXtraForm dialog = new ManagedFontXtraForm())
			{
				dialog.Text = L("Collections.Update.ConfirmTitle", "Apply revision change");
				dialog.Font = settings.Font;
				dialog.AutoScaleMode = AutoScaleMode.Font;
				dialog.StartPosition = FormStartPosition.CenterParent;
				dialog.MinimizeBox = false;
				dialog.MaximizeBox = false;
				dialog.ShowInTaskbar = false;
				dialog.MinimumSize = new Size(560, 280);
				dialog.ClientSize = new Size(650, preservedNames.Count > 0 ? 370 : 250);
				DevExpress.XtraEditors.PanelControl body = new DevExpress.XtraEditors.PanelControl
				{
					Dock = DockStyle.Fill, Padding = new Padding(12), BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder
				};
				dialog.Controls.Add(body);
				if (preservedNames.Count > 0)
				{
					DevExpress.XtraEditors.GroupControl preserved = new DevExpress.XtraEditors.GroupControl
					{
						Dock = DockStyle.Fill, Text = L("Collections.Update.KeepInstalled", "These mods stay installed")
					};
					DevExpress.XtraEditors.MemoEdit names = new DevExpress.XtraEditors.MemoEdit { Dock = DockStyle.Fill };
					names.Properties.ReadOnly = true;
					names.Properties.WordWrap = true;
					names.Properties.ScrollBars = ScrollBars.Vertical;
					names.Text = String.Join(Environment.NewLine, preservedNames);
					preserved.Controls.Add(names);
					body.Controls.Add(preserved);
				}
				string summary = LanguageManager.Format("Collections.Update.ConfirmCounts",
					"Collection changes: {0} added, {1} removed, {2} changed.", added, removed, changed);
				if (overrideCount > 0)
					summary += Environment.NewLine + L("Collections.Update.KeepCustomizations", "Your saved customizations will be kept.");
				summary += "\r\n\r\n" + L("Collections.Update.ConfirmScope", "Only the reviewed Collection changes will be applied.");
				DevExpress.XtraEditors.LabelControl details = CreateApprovalLabel(summary);
				body.Controls.Add(details);
				DevExpress.XtraEditors.LabelControl heading = CreateApprovalLabel(LanguageManager.Format("Collections.Update.ConfirmTransition",
					"{0}\r\n{1} -> {2}\r\n\r\nApply this revision change?",
					_snapshot?.Definition?.DisplayName ?? L("Collections.Context.Collection", "Collection"),
					FormatRevisionActionLabel(plan.OldPlan.Revision), FormatRevisionActionLabel(plan.NewPlan.Revision)));
				body.Controls.Add(heading);
				DevExpress.XtraEditors.PanelControl buttons = new DevExpress.XtraEditors.PanelControl
				{
					Dock = DockStyle.Bottom, Height = settings.Font.Height * 3, Padding = new Padding(12, 6, 12, 6),
					BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder
				};
				DevExpress.XtraEditors.SimpleButton cancel = new DevExpress.XtraEditors.SimpleButton
				{
					Dock = DockStyle.Right, Width = 100, Text = L("Common.Action.Cancel", "Cancel"), DialogResult = DialogResult.Cancel
				};
				DevExpress.XtraEditors.SimpleButton apply = new DevExpress.XtraEditors.SimpleButton
				{
					Dock = DockStyle.Right, Width = 200, Text = L("Collections.Update.ApplyRevisionChange", "Apply revision change"), DialogResult = DialogResult.OK
				};
				NmmIconProvider.BindDialogButton(apply, NmmIconAction.Apply);
				NmmIconProvider.BindDialogButton(cancel, NmmIconAction.Cancel);
				buttons.Controls.Add(apply);
				buttons.Controls.Add(cancel);
				dialog.Controls.Add(buttons);
				dialog.AcceptButton = apply;
				dialog.CancelButton = cancel;
				DevExpressDisplaySettingsApplier.ApplyToControlTree(dialog, settings);
				dialog.Shown += (sender, args) => cancel.Focus();
				return dialog.ShowDialog(this) == DialogResult.OK;
			}
		}

		/// <summary>Creates a wrapping, skin-aware label for the revision approval dialog.</summary>
		private static DevExpress.XtraEditors.LabelControl CreateApprovalLabel(string text)
		{
			DevExpress.XtraEditors.LabelControl label = new DevExpress.XtraEditors.LabelControl
			{
				Dock = DockStyle.Top, AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.Vertical,
				UseMnemonic = false, Padding = new Padding(0, 0, 0, 12), Text = text
			};
			label.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
			label.Appearance.Options.UseTextOptions = true;
			return label;
		}

		/// <summary>Frames an existing responsive layout with a DevExpress skin caption and border.</summary>
		private sealed class CollectionSectionGroup : DevExpress.XtraEditors.GroupControl
		{
			private readonly Control _content;

			/// <summary>Creates a section whose height follows its existing wrapping content.</summary>
			internal CollectionSectionGroup(string caption, Control content)
			{
				_content = content;
				Text = caption;
				Dock = DockStyle.Fill;
				AutoSize = true;
				Padding = new Padding(6);
				Margin = new Padding(0, 0, 0, 6);
				content.Dock = DockStyle.Fill;
				Controls.Add(content);
			}

			/// <summary>Includes the current skin's caption and border when measuring wrapped content.</summary>
			public override Size GetPreferredSize(Size proposedSize)
			{
				if (_content == null) return base.GetPreferredSize(proposedSize);
				int frameWidth = Math.Max(0, Width - DisplayRectangle.Width);
				int frameHeight = Math.Max(Font.Height, Height - DisplayRectangle.Height);
				Size contentSize = _content.GetPreferredSize(new Size(Math.Max(0, proposedSize.Width - frameWidth), 0));
				return new Size(contentSize.Width + frameWidth, contentSize.Height + frameHeight);
			}
		}

		/// <summary>DevExpress CheckEdit AutoSize may retain a short editor width after NMM changes its UI font.
		/// Reserve the full caption, checkbox glyph and skin spacing so the action panel wraps instead of clipping.</summary>
		private static void EnsureCheckCaptionFits(CheckEdit check)
		{
			if (check == null || check.IsDisposed)
				return;
			Font font = check.Properties.Appearance.Options.UseFont && check.Properties.Appearance.Font != null
				? check.Properties.Appearance.Font : check.Font;
			if (font == null)
				font = SystemFonts.DefaultFont;
			int captionWidth = TextRenderer.MeasureText(check.Text ?? String.Empty, font).Width;
			int reservedWidth = Math.Max(36, font.Height + 18) + check.Padding.Horizontal + 12;
			check.MinimumSize = new Size(captionWidth + reservedWidth, 0);
		}

		private void EnsureActionCheckCaptionsFit()
		{
			EnsureCheckCaptionFits(_autoOverwriteArchivesCheckBox);
			EnsureCheckCaptionFits(_replacementBackupCheckBox);
			EnsureCheckCaptionFits(_showErrorIssuesCheckBox);
			EnsureCheckCaptionFits(_showWarningIssuesCheckBox);
			EnsureCheckCaptionFits(_showInfoIssuesCheckBox);
			_incomingActionsPanel?.PerformLayout();
		}

		/// <summary>Creates an action group with the existing Collections heading and wrapping layout.</summary>
		private static Control CreateActionGroup(string caption, params Control[] actions)
		{
			LabelControl heading;
			CollectionActionPanel actionPanel;
			return CreateActionGroup(caption, out heading, out actionPanel, actions);
		}

		/// <summary>Creates an action group whose heading and actions can follow the displayed context.</summary>
		private static Control CreateActionGroup(string caption, out LabelControl heading, out CollectionActionPanel actionPanel, params Control[] actions)
		{
			TablePanel group = CreateCollectionTable(2);
			group.AutoSize = true;
			group.Margin = new Padding(0, 0, 0, 4);
			heading = new LabelControl
			{
				AutoSizeMode = LabelAutoSizeMode.Horizontal, Text = caption ?? String.Empty,
				Margin = new Padding(3, 0, 3, 2)
			};
			actionPanel = new CollectionActionPanel();
			foreach (Control action in actions ?? new Control[0])
				if (action != null) actionPanel.Controls.Add(action);
			heading.Appearance.FontStyleDelta = FontStyle.Bold;
			AddCollectionCell(group, heading, 0);
			AddCollectionCell(group, actionPanel, 1);
			return group;
		}

		/// <summary>Creates a DevExpress table where only explicitly relative rows consume unused height.</summary>
		private static TablePanel CreateCollectionTable(int rows, int columns = 1)
		{
			TablePanel table = new TablePanel
			{
				Dock = DockStyle.Fill, UseSkinIndents = false, Padding = Padding.Empty,
				Margin = Padding.Empty, AutoSizeDefaultTableElementLength = 0
			};
			for (int column = 0; column < columns; column++)
				table.Columns.Add(new TablePanelColumn(TablePanelEntityStyle.Relative, 1F));
			for (int row = 0; row < rows; row++)
				table.Rows.Add(new TablePanelRow(TablePanelEntityStyle.AutoSize, 1F));
			return table;
		}

		/// <summary>Moves an existing control into a DevExpress table without replacing its workflow handlers.</summary>
		private static void AddCollectionCell(TablePanel table, Control control, int row, int column = 0)
		{
			table.Controls.Add(control);
			table.SetCell(control, row, column);
		}

		/// <summary>Gives each review list the full working area instead of splitting three grids into small panes.</summary>
		private static XtraTabPage CreateCollectionPage(XtraTabControl tabs, string caption)
		{
			XtraTabPage page = new XtraTabPage { Text = caption, Padding = new Padding(6) };
			tabs.TabPages.Add(page);
			return page;
		}

		/// <summary>Wraps actions in a DevExpress surface using current font sizes and visible controls only.</summary>
		private sealed class CollectionActionPanel : PanelControl
		{
			/// <summary>Creates a compact wrapping action row.</summary>
			internal CollectionActionPanel()
			{
				BorderStyle = BorderStyles.NoBorder;
				Dock = DockStyle.Fill;
				AutoSize = true;
				Margin = Padding.Empty;
				Padding = Padding.Empty;
			}

			/// <summary>Measures the height of the wrapped actions at the proposed width.</summary>
			public override Size GetPreferredSize(Size proposedSize)
			{
				return ArrangeActions(proposedSize.Width > 0 ? proposedSize.Width : Math.Max(1, Width), false);
			}

			/// <summary>Reflows actions after a size or global font change.</summary>
			protected override void OnLayout(LayoutEventArgs e)
			{
				base.OnLayout(e);
				ArrangeActions(Math.Max(1, ClientSize.Width), true);
			}

			/// <summary>Uses identical measurements for preferred height and actual layout, avoiding empty toolbar space.</summary>
			private Size ArrangeActions(int width, bool apply)
			{
				int x = Padding.Left;
				int y = Padding.Top;
				int rowHeight = 0;
				int usedWidth = 0;
				int available = Math.Max(1, width - Padding.Horizontal);
				foreach (Control child in Controls)
				{
					if (!child.Visible) continue;
					Size size = child.AutoSize ? child.GetPreferredSize(new Size(available, 0)) : child.Size;
					size.Width = Math.Min(available, Math.Max(child.MinimumSize.Width, size.Width));
					size.Height = Math.Max(child.MinimumSize.Height, size.Height);
					int occupiedWidth = size.Width + child.Margin.Horizontal;
					if (x > Padding.Left && x + occupiedWidth > width - Padding.Right)
					{
						x = Padding.Left;
						y += rowHeight;
						rowHeight = 0;
					}
					if (apply) child.Bounds = new Rectangle(x + child.Margin.Left, y + child.Margin.Top, size.Width, size.Height);
					x += occupiedWidth;
					rowHeight = Math.Max(rowHeight, size.Height + child.Margin.Vertical);
					usedWidth = Math.Max(usedWidth, x);
				}
				return new Size(usedWidth + Padding.Right, y + rowHeight + Padding.Bottom);
			}
		}

		/// <summary>Creates a themed grid with stable row order and one focused member/review item.</summary>
		private static GridView CreateCollectionGridView(GridControl grid)
		{
			GridView view = new GridView(grid);
			grid.MainView = view;
			grid.ViewCollection.Add(view);
			view.OptionsView.ShowGroupPanel = false;
			view.OptionsView.ShowIndicator = false;
			view.OptionsView.ColumnAutoWidth = false;
			view.OptionsSelection.MultiSelect = false;
			view.OptionsCustomization.AllowColumnMoving = false;
			view.OptionsCustomization.AllowSort = false;
			view.OptionsCustomization.AllowFilter = false;
			return view;
		}

		private static GridColumn AddCollectionGridColumn(GridView view, string property, string caption, int width, bool editable = false)
		{
			GridColumn column = view.Columns.AddVisible(property, caption);
			column.Width = width;
			column.OptionsColumn.AllowEdit = editable;
			column.OptionsColumn.ReadOnly = !editable;
			return column;
		}

		private GridControl CreateReviewGrid(BindingList<ReviewGridRow> rows, out GridView view)
		{
			GridControl grid = new GridControl { Dock = DockStyle.Fill };
			view = CreateCollectionGridView(grid);
			view.OptionsBehavior.Editable = false;
			view.OptionsView.ColumnAutoWidth = true;
			view.OptionsView.RowAutoHeight = true;
			RepositoryItemMemoEdit wrappedText = new RepositoryItemMemoEdit { ReadOnly = true };
			grid.RepositoryItems.Add(wrappedText);
			AddCollectionGridColumn(view, nameof(ReviewGridRow.Status), L("Collections.Columns.Status", "Status"), 90);
			AddCollectionGridColumn(view, nameof(ReviewGridRow.Subject), L("Collections.Columns.Field", "Subject"), 170);
			AddCollectionGridColumn(view, nameof(ReviewGridRow.Reason), L("Collections.Columns.Reason", "What happened / planned change"), 315).ColumnEdit = wrappedText;
			AddCollectionGridColumn(view, nameof(ReviewGridRow.NextAction), L("Collections.Columns.NextAction", "What you can do"), 285).ColumnEdit = wrappedText;
			grid.DataSource = rows;
			GridView reviewView = view;
			grid.ToolTipController = _toolTip;
			_toolTip.GetActiveObjectInfo += (sender, args) =>
			{
				if (args.SelectedControl != grid || args.Info != null)
					return;
				var hit = reviewView.CalcHitInfo(args.ControlMousePosition);
				ReviewGridRow row = hit.InRowCell || hit.InRow ? reviewView.GetRow(hit.RowHandle) as ReviewGridRow : null;
				if (row != null && !String.IsNullOrWhiteSpace(row.Tooltip))
					args.Info = new ToolTipControlInfo(row, row.Tooltip);
			};
			return grid;
		}

		/// <summary>Layout panels arrange controls only; their transparent background lets the current DX skin show through.</summary>
		private static void MakeLayoutSkinTransparent(Control control)
		{
			if (control is TableLayoutPanel || control is FlowLayoutPanel)
				control.BackColor = Color.Transparent;
			foreach (Control child in control.Controls)
				MakeLayoutSkinTransparent(child);
		}

		/// <summary>Applies the same persistent Aa font and DevExpress appearance as the main Collections surface.</summary>
		private static void ApplyCollectionDialogDisplaySettings(ManagedFontXtraForm dialog, DevExpressDisplaySettings settings)
		{
			dialog.AutoScaleMode = AutoScaleMode.Font;
			dialog.Font = settings.Font;
			MakeLayoutSkinTransparent(dialog);
			DevExpressDisplaySettingsApplier.ApplyToControlTree(dialog, settings);
		}

		private static LabelControl AddHeaderRow(TablePanel table, int row, int column, string caption)
		{
			var name = new LabelControl
			{
				AutoSizeMode = LabelAutoSizeMode.Horizontal,
				Text = caption,

				Margin = new Padding(0, 3, 8, 3)
			};
			var value = new LabelControl
			{
				AutoSizeMode = LabelAutoSizeMode.Vertical,
				Dock = DockStyle.Fill,
				Text = "-",
				Margin = new Padding(0, 3, 18, 3)
			};
			name.Appearance.FontStyleDelta = FontStyle.Bold;
			AddCollectionCell(table, name, row, column);
			AddCollectionCell(table, value, row, column + 1);
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
				case CollectionMemberAcquisitionDisposition.DirectQueued:
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
