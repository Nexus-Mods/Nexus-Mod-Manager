using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Nexus.Client.CollectionManagement;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using Nexus.Client.OnlineServices.NexusMods.GraphQl;
using Nexus.Client.UI;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.CollectionManagement.UI
{
	/// <summary>
	/// Read-only C2 Collections surface for incoming Nexus revision metadata and normalized local bundle previews.
	/// </summary>
	/// <remarks>
	/// The control intentionally contains no install, replace, detach or remove command. Those commands remain gated by
	/// the later durable journal/planner/native execution phases.
	/// </remarks>
	public sealed class CollectionsPreviewControl : ManagedFontDockContent
	{
		private readonly Button _importButton;
		private readonly Button _clearButton;
		private readonly Label _instructionLabel;
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
		private NexusCollectionPreviewSnapshot _snapshot;
		private CancellationTokenSource _previewCancellation;
		private int _previewGeneration;
		private bool _initialized;

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
				RowCount = 5,
				Padding = new Padding(8)
			};
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			Controls.Add(root);

			var toolbar = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill,
				AutoSize = true,
				WrapContents = false,
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
			_clearButton = new Button
			{
				AutoSize = true,
				Text = L("Collections.Actions.ClearPreview", "Clear preview")
			};
			_clearButton.Click += ClearButton_Click;
			_instructionLabel = new Label
			{
				AutoSize = true,
				MaximumSize = new Size(760, 0),
				Padding = new Padding(12, 6, 0, 0),
				Text = L("Collections.Preview.Instructions", "Open a Nexus Collection NXM link to resolve a concrete revision. You can then import its downloaded bundle or collection.json for a read-only capability preview.")
			};
			toolbar.Controls.Add(_importButton);
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

			var splitHeaders = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				AutoSize = true,
				ColumnCount = 2
			};
			splitHeaders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
			splitHeaders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
			_membersHeader = new Label { AutoSize = true, Text = L("Collections.Preview.Members", "Members") };
			_issuesHeader = new Label { AutoSize = true, Text = L("Collections.Preview.Issues", "Capability / provider issues") };
			splitHeaders.Controls.Add(_membersHeader, 0, 0);
			splitHeaders.Controls.Add(_issuesHeader, 1, 0);
			root.Controls.Add(splitHeaders, 0, 3);

			var contentGrid = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 2,
				RowCount = 1
			};
			contentGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
			contentGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));

			_membersView = CreateListView();
			_membersView.Columns.Add(L("Collections.Columns.Member", "Member"), 210);
			_membersView.Columns.Add(L("Collections.Columns.Requirement", "Requirement"), 88);
			_membersView.Columns.Add(L("Collections.Columns.Selection", "Selection"), 82);
			_membersView.Columns.Add(L("Collections.Columns.Compatibility", "Compatibility"), 112);
			_membersView.Columns.Add(L("Collections.Columns.Artifact", "Artifact"), 240);
			contentGrid.Controls.Add(_membersView, 0, 0);

			_issuesView = CreateListView();
			_issuesView.Columns.Add(L("Collections.Columns.Status", "Status"), 105);
			_issuesView.Columns.Add(L("Collections.Columns.Code", "Code"), 180);
			_issuesView.Columns.Add(L("Collections.Columns.Field", "Field"), 130);
			_issuesView.Columns.Add(L("Collections.Columns.Reason", "Reason"), 360);
			contentGrid.Controls.Add(_issuesView, 1, 0);
			root.Controls.Add(contentGrid, 0, 4);

			RenderEmptyState();
		}

		/// <summary>
		/// Connects the surface to the shared incoming Collection NXM dispatcher.
		/// </summary>
		public void Initialize(NexusCollectionNxmDispatcher dispatcher)
		{
			if (ReferenceEquals(_dispatcher, dispatcher) && _initialized)
				return;

			DetachDispatcher();
			++_previewGeneration;
			_previewCancellation?.Cancel();
			_previewCancellation?.Dispose();
			_previewCancellation = null;
			_snapshot = null;
			RenderEmptyState();

			_dispatcher = dispatcher;
			_initialized = true;
			_controller = dispatcher == null ? null : new NexusCollectionPreviewController(dispatcher.Provider);

			if (_dispatcher != null)
				_dispatcher.DispatchCompleted += Dispatcher_DispatchCompleted;

			_importButton.Enabled = false;
			if (IsHandleCreated)
				BeginInvoke((Action)DrainIncomingQueue);
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			if (_initialized)
				BeginInvoke((Action)DrainIncomingQueue);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				DetachDispatcher();
				_previewCancellation?.Cancel();
				_previewCancellation?.Dispose();
				_previewCancellation = null;
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
			_previewCancellation?.Cancel();
			_previewCancellation?.Dispose();
			_previewCancellation = new CancellationTokenSource();
			CancellationToken token = _previewCancellation.Token;

			_snapshot = null;
			_importButton.Enabled = false;
			ShowLoading(dispatch.Link);
			PreviewActivated(this, EventArgs.Empty);

			try
			{
				NexusCollectionPreviewSnapshot snapshot = await _controller
					.CreateFromDispatchAsync(dispatch, token);
				if (token.IsCancellationRequested || generation != _previewGeneration || IsDisposed)
					return;

				_snapshot = snapshot;
				RenderSnapshot(snapshot);
			}
			catch (OperationCanceledException)
			{
				// A newer Collection selector superseded this read-only metadata request.
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
			if (_controller == null || sourceSnapshot == null || !sourceSnapshot.HasConcreteRevision)
			{
				_summaryBox.Text = L("Collections.Preview.ImportNeedsRevision", "Resolve a concrete Nexus Collection revision before importing its bundle or collection.json.");
				return;
			}

			using (var dialog = new OpenFileDialog())
			{
				dialog.Title = L("Collections.Preview.ImportTitle", "Import Nexus Collection bundle for preview");
				dialog.CheckFileExists = true;
				dialog.CheckPathExists = true;
				dialog.Multiselect = false;
				dialog.Filter = L("Collections.Preview.ImportFilter", "Collection bundle or manifest|*.zip;*.7z;*.rar;collection.json|All files|*.*");
				if (dialog.ShowDialog(this) != DialogResult.OK)
					return;

				int generation = _previewGeneration;
				_importButton.Enabled = false;
				_contentValue.Text = L("Collections.Status.Content.Inspecting", "Inspecting bundle...");
				try
				{
					NexusCollectionPreviewSnapshot imported = await Task.Run(() => _controller.ImportFile(sourceSnapshot, dialog.FileName));
					if (generation != _previewGeneration || !ReferenceEquals(sourceSnapshot, _snapshot) || IsDisposed)
						return;

					_snapshot = imported;
					RenderSnapshot(imported);
				}
				catch (Exception ex)
				{
					if (generation != _previewGeneration || IsDisposed)
						return;
					Trace.TraceError("Collection bundle preview import failed: " + ex);
					_contentValue.Text = L("Collections.Status.Content.ImportFailed", "Bundle import failed");
					AddIssueRow(L("Collections.Status.ActionRequired", "Action required"), "bundle.import-failed", string.Empty, ex.Message);
					_issuesHeader.Text = LanguageManager.Format("Collections.Preview.IssueCount", "Capability / provider issues ({0})", _issuesView.Items.Count);
				}
				finally
				{
					if (generation == _previewGeneration && !IsDisposed)
						_importButton.Enabled = _snapshot != null && _snapshot.HasConcreteRevision;
				}
			}
		}

		private void ClearButton_Click(object sender, EventArgs e)
		{
			++_previewGeneration;
			_previewCancellation?.Cancel();
			_previewCancellation?.Dispose();
			_previewCancellation = null;
			_snapshot = null;
			_importButton.Enabled = false;
			RenderEmptyState();
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
			_locatorValue.Text = snapshot.Link == null
				? L("Collections.Value.Unknown", "Unknown")
				: snapshot.Link.GameDomain + " / " + snapshot.Link.CollectionSlug;

			if (revision != null)
			{
				_revisionValue.Text = LanguageManager.Format(
					"Collections.Preview.ConcreteRevision",
					"#{0} (collection {1}, revision {2})",
					revision.Identity.NexusRevisionNumber,
					revision.Collection.StableId,
					revision.Identity.StableRevisionId);
			}
			else if (providerRevision != null && providerRevision.RevisionNumber.HasValue)
			{
				_revisionValue.Text = LanguageManager.Format("Collections.Preview.PartialRevision", "#{0} (identity incomplete)", providerRevision.RevisionNumber.Value);
			}
			else
			{
				_revisionValue.Text = snapshot.Link != null && snapshot.Link.RevisionRequest.IsLatest
					? L("Collections.Preview.LatestUnresolved", "latest (not resolved)")
					: L("Collections.Value.Unknown", "Unknown");
			}

			if (snapshot.HasManifestPreview)
			{
				_compatibilityValue.Text = FormatCompatibility(snapshot.CapabilityReport.Status);
				_contentValue.Text = L("Collections.Status.Content.ManifestImported", "Manifest imported; member archives not prepared");
			}
			else
			{
				_compatibilityValue.Text = L("Collections.Status.Compatibility.NotEvaluated", "Not evaluated - manifest not imported");
				_contentValue.Text = snapshot.HasConcreteRevision
					? L("Collections.Status.Content.BundleNotImported", "Bundle not imported / prepared")
					: L("Collections.Status.Content.WaitingRevision", "Waiting for concrete revision");
			}
			_appliedValue.Text = L("Collections.Status.Applied.NotApplied", "Not applied (read-only preview)");
			_importButton.Enabled = snapshot.HasConcreteRevision;

			string summary = definition?.Summary ?? providerSummary?.Summary;
			_summaryBox.Text = string.IsNullOrWhiteSpace(summary)
				? L("Collections.Preview.NoSummary", "No collection summary was returned. Decorative metadata is optional and does not establish identity or readiness.")
				: summary;

			RenderMembers(snapshot, selectedMember, previousTopIndex);
			RenderIssues(snapshot);
		}

		private void RenderMembers(NexusCollectionPreviewSnapshot snapshot, string selectedToken, int previousTopIndex)
		{
			_membersView.BeginUpdate();
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
					string token = member.IdentityResolution.IsResolved
						? member.IdentityResolution.Key.ToString()
						: "ordinal:" + member.SourceOrdinal.ToString(CultureInfo.InvariantCulture);
					string displayName = string.IsNullOrWhiteSpace(member.DisplayName) ? token : member.DisplayName;
					var item = new ListViewItem(displayName) { Tag = token };
					item.SubItems.Add(member.IsRequired ? L("Collections.Member.Required", "Required") : L("Collections.Member.Optional", "Optional"));
					item.SubItems.Add(member.IsSelected ? L("Collections.Member.Selected", "Selected") : L("Collections.Member.Unselected", "Not selected"));
					item.SubItems.Add(FormatCompatibility(memberReport.Status));
					item.SubItems.Add(member.Artifact == null ? L("Collections.Value.Unresolved", "Unresolved") : member.Artifact.ToString());
					_membersView.Items.Add(item);
				}

				if (!string.IsNullOrEmpty(selectedToken))
				{
					foreach (ListViewItem item in _membersView.Items)
					{
						if (StringComparer.Ordinal.Equals(Convert.ToString(item.Tag), selectedToken))
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
					{
						AddIssueRow(
							FormatCompatibility(issue.Status),
							issue.Code,
							issue.FieldPath ?? string.Empty,
							issue.Reason);
					}
				}

				if (!snapshot.HasConcreteRevision && snapshot.RevisionError == null)
					AddIssueRow(L("Collections.Status.ActionRequired", "Action required"), "provider.identity-incomplete", string.Empty, L("Collections.Preview.IdentityIncomplete", "The provider response did not contain the stable collection and revision identity required for a trusted manifest preview."));

				_issuesHeader.Text = LanguageManager.Format("Collections.Preview.IssueCount", "Capability / provider issues ({0})", _issuesView.Items.Count);
			}
			finally
			{
				_issuesView.EndUpdate();
			}
		}

		private void AddGraphQlErrors(string codePrefix, System.Collections.Generic.IReadOnlyList<NexusGraphQlError> errors)
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

		private void ShowLoading(NexusCollectionNxmLink link)
		{
			_collectionValue.Text = link?.CollectionSlug ?? L("Collections.Value.Unknown", "Unknown");
			_curatorValue.Text = L("Collections.Value.Loading", "Loading...");
			_locatorValue.Text = link == null ? L("Collections.Value.Unknown", "Unknown") : link.GameDomain + " / " + link.CollectionSlug;
			_revisionValue.Text = link == null
				? L("Collections.Value.Unknown", "Unknown")
				: link.RevisionRequest.IsLatest
					? L("Collections.Preview.ResolvingLatest", "Resolving latest...")
					: "#" + link.RevisionRequest.RevisionNumber.Value.ToString(CultureInfo.InvariantCulture);
			_compatibilityValue.Text = L("Collections.Value.Loading", "Loading...");
			_contentValue.Text = L("Collections.Status.Content.WaitingRevision", "Waiting for concrete revision");
			_appliedValue.Text = L("Collections.Status.Applied.NotApplied", "Not applied (read-only preview)");
			_summaryBox.Text = L("Collections.Preview.Loading", "Resolving Collection metadata. No game files or native mod state are being changed.");
			_membersView.Items.Clear();
			_issuesView.Items.Clear();
			_membersHeader.Text = L("Collections.Preview.Members", "Members");
			_issuesHeader.Text = L("Collections.Preview.Issues", "Capability / provider issues");
		}

		private void RenderUnexpectedFailure(NexusCollectionNxmLink link, Exception exception)
		{
			_collectionValue.Text = link?.CollectionSlug ?? L("Collections.Value.Unknown", "Unknown");
			_curatorValue.Text = L("Collections.Value.Unknown", "Unknown");
			_locatorValue.Text = link == null ? L("Collections.Value.Unknown", "Unknown") : link.GameDomain + " / " + link.CollectionSlug;
			_revisionValue.Text = L("Collections.Value.Unresolved", "Unresolved");
			_compatibilityValue.Text = L("Collections.Status.ActionRequired", "Action required");
			_contentValue.Text = L("Collections.Status.Content.WaitingRevision", "Waiting for concrete revision");
			_appliedValue.Text = L("Collections.Status.Applied.NotApplied", "Not applied (read-only preview)");
			_summaryBox.Text = exception.Message;
			_membersView.Items.Clear();
			_issuesView.Items.Clear();
			AddIssueRow(L("Collections.Status.ActionRequired", "Action required"), "preview.unexpected-failure", string.Empty, exception.Message);
			_issuesHeader.Text = LanguageManager.Format("Collections.Preview.IssueCount", "Capability / provider issues ({0})", _issuesView.Items.Count);
		}

		private void RenderEmptyState()
		{
			_collectionValue.Text = "-";
			_curatorValue.Text = "-";
			_locatorValue.Text = "-";
			_revisionValue.Text = "-";
			_compatibilityValue.Text = L("Collections.Status.Compatibility.NotEvaluated", "Not evaluated - manifest not imported");
			_contentValue.Text = L("Collections.Status.Content.NoSelection", "No revision selected");
			_appliedValue.Text = L("Collections.Status.Applied.NotApplied", "Not applied (read-only preview)");
			_summaryBox.Text = L("Collections.Preview.EmptySummary", "No Collection preview is loaded.");
			_membersView.Items.Clear();
			_issuesView.Items.Clear();
			_membersHeader.Text = L("Collections.Preview.Members", "Members");
			_issuesHeader.Text = L("Collections.Preview.Issues", "Capability / provider issues");
		}

		private string GetSelectedMemberToken()
		{
			if (_membersView.SelectedItems.Count == 0)
				return null;
			return Convert.ToString(_membersView.SelectedItems[0].Tag);
		}

		private void DetachDispatcher()
		{
			if (_dispatcher != null)
				_dispatcher.DispatchCompleted -= Dispatcher_DispatchCompleted;
			_dispatcher = null;
			_controller = null;
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
				case CollectionCompatibilityStatus.Supported:
					return L("Collections.Status.Supported", "Supported");
				case CollectionCompatibilityStatus.ActionRequired:
					return L("Collections.Status.ActionRequired", "Action required");
				case CollectionCompatibilityStatus.Unsupported:
					return L("Collections.Status.Unsupported", "Unsupported");
				default:
					return L("Collections.Value.Unknown", "Unknown");
			}
		}

		private static string FirstNonEmpty(params string[] values)
		{
			foreach (string value in values)
			{
				if (!string.IsNullOrWhiteSpace(value))
					return value;
			}
			return string.Empty;
		}

		private static string L(string key, string fallback)
		{
			return LanguageManager.Get(key, fallback);
		}
	}
}
