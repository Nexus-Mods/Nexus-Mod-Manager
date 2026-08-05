namespace Nexus.Client.ModManagement.UI
{
	using System;
	using System.ComponentModel;
	using System.Diagnostics;
	using System.IO;
	using System.Windows.Forms;
	using DevExpress.XtraEditors;
	using DevExpress.XtraEditors.DXErrorProvider;
	using DevExpress.XtraGrid.Views.Base;
	using DevExpress.XtraGrid.Views.Grid;
	using Nexus.Client.ModRepositories;
	using Nexus.Client.Mods;
	using Nexus.Client.UI;
	using Nexus.Client.Util;

	/// <summary>
	/// Displays Nexus match candidates and a DevExpress-based mod metadata editor.
	/// </summary>
	public partial class ModTaggerForm : XtraForm
	{
		private const string WindowSettingsKey = "GetModInfoForm";
		private const string SplitterSettingsKey = "getModInfo.SplitterPosition";
		private const string DescriptionPreviewStyles =
			".description { padding: 6px; } " +
			".quote { margin: 6px 0; padding: 6px 8px; border-left: 3px solid @DisabledText; background-color: @Control; } " +
			".code { margin: 6px 0; padding: 6px; font-family: Consolas; background-color: @Control; } " +
			".empty { padding: 6px; font-style: italic; color: @DisabledText; }";
		private ModTaggerVM m_vmlViewModel;
		private ExtendedImage m_eimScreenshot;
		private DevExpressDisplaySettings m_dxdDisplaySettings;
		private bool m_booLoadingEditor;

		/// <summary>
		/// Gets or sets the view model that supplies candidates and saves edited metadata.
		/// </summary>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public ModTaggerVM ViewModel
		{
			get { return m_vmlViewModel; }
			set
			{
				m_vmlViewModel = value;
				if (m_vmlViewModel == null)
				{
					m_dxdDisplaySettings?.Dispose();
					m_dxdDisplaySettings = null;
					return;
				}

				Icon = m_vmlViewModel.CurrentTheme.Icon;
				grdCandidates.DataSource = m_vmlViewModel.TagCandidates;
				grvCandidates.ClearSelection();
				grvCandidates.FocusedRowHandle = DevExpress.XtraGrid.GridControl.InvalidRowHandle;
				m_vmlViewModel.LoadCurrentModInfo();
				LoadEditorValues();
				UpdateCandidateHint();
				ApplyDisplaySettings();
			}
		}

		/// <summary>
		/// Initializes the Get Mod Info dialog.
		/// </summary>
		/// <param name="p_mtgTaggerVM">The view model that retrieves and saves mod metadata.</param>
		public ModTaggerForm(ModTaggerVM p_mtgTaggerVM)
		{
			InitializeComponent();
			ViewModel = p_mtgTaggerVM;
			buttonPanel_Resize(this, EventArgs.Empty);
		}

		/// <summary>
		/// Applies the font, size, and density selected through the Aa Display options to the complete dialog.
		/// </summary>
		private void ApplyDisplaySettings()
		{
			if (ViewModel == null)
				return;

			m_dxdDisplaySettings?.Dispose();
			m_dxdDisplaySettings = DevExpressDisplaySettings.CreateFromSettings(ViewModel.Settings);
			DevExpressDisplaySettingsApplier.ApplyToControlTree(this, m_dxdDisplaySettings);
		}

		/// <summary>
		/// Releases the display-font resources owned by the dialog.
		/// </summary>
		/// <param name="e">The form-closed event arguments.</param>
		protected override void OnFormClosed(FormClosedEventArgs e)
		{
			m_dxdDisplaySettings?.Dispose();
			m_dxdDisplaySettings = null;
			base.OnFormClosed(e);
		}

		/// <summary>
		/// Restores the saved window position and candidate/editor split.
		/// </summary>
		/// <param name="e">The event arguments.</param>
		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			if (DesignMode || ViewModel == null)
				return;

			ViewModel.Settings.WindowPositions.GetWindowPosition(WindowSettingsKey, this);
			Int32 splitterPosition;
			if (Int32.TryParse(ViewModel.Settings.DockPanelLayouts[SplitterSettingsKey], out splitterPosition))
				splitMain.SplitterPosition = Math.Max(240, Math.Min(splitterPosition, Math.Max(240, splitMain.Width - 420)));
		}

		/// <summary>
		/// Persists the window position and candidate/editor split.
		/// </summary>
		/// <param name="e">The closing event arguments.</param>
		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			if (!DesignMode && ViewModel != null)
			{
				ViewModel.Settings.WindowPositions.SetWindowPosition(WindowSettingsKey, this);
				ViewModel.Settings.DockPanelLayouts[SplitterSettingsKey] = splitMain.SplitterPosition.ToString();
				ViewModel.Settings.Save();
			}
			base.OnFormClosing(e);
		}

		/// <summary>
		/// Loads the Nexus candidate focused through keyboard navigation into the editor.
		/// </summary>
		/// <param name="sender">The candidate grid view.</param>
		/// <param name="e">The focused-row event arguments.</param>
		private void grvCandidates_FocusedRowChanged(object sender, FocusedRowChangedEventArgs e)
		{
			LoadCandidateAtRow(e.FocusedRowHandle);
		}

		/// <summary>
		/// Loads the clicked candidate even when the row was already focused and no focus-change event is raised.
		/// </summary>
		/// <param name="sender">The candidate grid view.</param>
		/// <param name="e">The row-click event arguments.</param>
		private void grvCandidates_RowClick(object sender, RowClickEventArgs e)
		{
			LoadCandidateAtRow(e.RowHandle);
		}

		/// <summary>
		/// Loads the candidate represented by the specified grid row into the metadata editor.
		/// </summary>
		/// <param name="rowHandle">The candidate row handle.</param>
		private void LoadCandidateAtRow(Int32 rowHandle)
		{
			if (ViewModel == null || rowHandle < 0)
				return;

			IModInfo candidate = grvCandidates.GetRow(rowHandle) as IModInfo;
			if (candidate == null)
				return;

			ViewModel.LoadTagOption(candidate);
			LoadEditorValues();
		}

		/// <summary>
		/// Copies the current editable metadata from the view model into the DevExpress editors.
		/// </summary>
		private void LoadEditorValues()
		{
			if (ViewModel == null)
				return;

			m_booLoadingEditor = true;
			try
			{
				var edited = ViewModel.EditedModInfo;
				txtName.Text = edited.ModName ?? String.Empty;
				txtVersion.Text = edited.HumanReadableVersion ?? String.Empty;
				txtAuthor.Text = edited.Author ?? String.Empty;
				txtWebsite.Text = edited.Website ?? String.Empty;
				txtModId.Text = edited.ModId ?? String.Empty;
				txtFileId.Text = edited.DownloadId ?? String.Empty;
				txtDescription.Text = edited.Description ?? String.Empty;
				btnEditDescription.Checked = false;
				SetDescriptionEditMode(false);
				m_eimScreenshot = edited.Screenshot;
				picScreenshot.Image = m_eimScreenshot;
				errorProvider.ClearErrors();
			}
			finally
			{
				m_booLoadingEditor = false;
			}
		}

		/// <summary>
		/// Switches between the sanitized description preview and the original editable source.
		/// </summary>
		/// <param name="sender">The source-edit toggle.</param>
		/// <param name="e">The event arguments.</param>
		private void btnEditDescription_CheckedChanged(object sender, EventArgs e)
		{
			if (m_booLoadingEditor)
				return;

			SetDescriptionEditMode(btnEditDescription.Checked);
		}

		/// <summary>
		/// Applies the requested description mode and refreshes the formatted preview when necessary.
		/// </summary>
		/// <param name="editSource">Whether the original description markup should be editable.</param>
		private void SetDescriptionEditMode(bool editSource)
		{
			btnEditDescription.Text = editSource ? "Preview" : "Edit source";
			txtDescription.Visible = editSource;
			htmlDescription.Visible = !editSource;

			if (editSource)
			{
				txtDescription.BringToFront();
				txtDescription.Focus();
				return;
			}

			UpdateDescriptionPreview();
			htmlDescription.BringToFront();
		}

		/// <summary>
		/// Renders the current raw description as sanitized HTML without modifying the persisted source.
		/// </summary>
		private void UpdateDescriptionPreview()
		{
			htmlDescription.HtmlTemplate.Set(NexusDescriptionFormatter.ToSafeHtml(txtDescription.Text), DescriptionPreviewStyles);
			htmlDescription.Refresh();
		}

		/// <summary>
		/// Updates the candidate-panel guidance according to the number of repository matches.
		/// </summary>
		private void UpdateCandidateHint()
		{
			if (ViewModel == null || ViewModel.TagCandidates.Count == 0)
			{
				lblCandidateHint.Text = "No automatic Nexus match was found. Enter a Nexus link or the numeric IDs manually.";
				return;
			}

			lblCandidateHint.Text = ViewModel.TagCandidates.Count == 1
				? "One Nexus match was found. Verify it, then save or edit the metadata manually."
				: "Select the exact Nexus file. Different files belonging to the same mod remain separate entries.";
		}

		/// <summary>
		/// Normalizes a manually entered website and extracts Nexus mod and file identifiers.
		/// </summary>
		/// <param name="sender">The website editor.</param>
		/// <param name="e">The event arguments.</param>
		private void txtWebsite_Validated(object sender, EventArgs e)
		{
			if (m_booLoadingEditor)
				return;

			if (String.IsNullOrWhiteSpace(txtWebsite.Text))
			{
				errorProvider.SetError(txtWebsite, String.Empty);
				return;
			}

			Uri website;
			NexusModLink link;
			NexusModLinkParser.TryParse(txtWebsite.Text, out link);
			if (link != null && String.Equals(link.SourceUri.Scheme, "nxm", StringComparison.OrdinalIgnoreCase))
			{
				website = NexusModLinkParser.CreateModUri(link.GameDomain, link.ModId, link.FileId);
			}
			else if (!NexusModLinkParser.TryNormalizeWebsite(txtWebsite.Text, out website))
			{
				errorProvider.SetError(txtWebsite, "Enter a valid HTTP, HTTPS, or NXM Nexus Mods address.", ErrorType.Critical);
				return;
			}

			txtWebsite.Text = website.ToString();
			errorProvider.SetError(txtWebsite, String.Empty);

			if (link == null && !NexusModLinkParser.TryParse(website.ToString(), out link))
				return;

			string previousModId = txtModId.Text.Trim();
			txtModId.Text = link.ModId;
			if (!String.IsNullOrEmpty(link.FileId))
				txtFileId.Text = link.FileId;
			else if (!String.IsNullOrEmpty(previousModId) && !String.Equals(previousModId, link.ModId, StringComparison.OrdinalIgnoreCase))
				txtFileId.Text = String.Empty;
		}

		/// <summary>
		/// Opens the best available mod or file page represented by the current editor values.
		/// </summary>
		/// <param name="sender">The open-page button.</param>
		/// <param name="e">The event arguments.</param>
		private void btnOpenWebsite_Click(object sender, EventArgs e)
		{
			Uri website = ResolveEditorNavigationUri();
			if (website == null)
			{
				errorProvider.SetError(txtWebsite, "Enter a Nexus link or valid Nexus mod ID first.", ErrorType.Information);
				return;
			}

			try
			{
				Process.Start(website.ToString());
			}
			catch (Exception ex)
			{
				errorProvider.SetError(txtWebsite, "Unable to open the web address: " + ex.Message, ErrorType.Warning);
			}
		}

		/// <summary>
		/// Resolves a navigation URI from the explicitly entered website and Nexus identifiers.
		/// </summary>
		/// <returns>The best available navigation URI, or <c>null</c>.</returns>
		private Uri ResolveEditorNavigationUri()
		{
			Uri storedWebsite = null;
			if (!String.IsNullOrWhiteSpace(txtWebsite.Text))
				NexusModLinkParser.TryNormalizeWebsite(txtWebsite.Text, out storedWebsite);

			return NexusModLinkParser.ResolveNavigationUri(
				storedWebsite,
				ViewModel == null ? null : ViewModel.GameDomainName,
				txtModId.Text.Trim(),
				txtFileId.Text.Trim());
		}

		/// <summary>
		/// Selects and loads a screenshot through the DevExpress file dialog.
		/// </summary>
		/// <param name="sender">The set-screenshot button.</param>
		/// <param name="e">The event arguments.</param>
		private void btnSetScreenshot_Click(object sender, EventArgs e)
		{
			using (XtraOpenFileDialog dialog = new XtraOpenFileDialog())
			{
				dialog.Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*";
				dialog.Title = "Select mod screenshot";
				if (dialog.ShowDialog(this) != DialogResult.OK)
					return;

				try
				{
					m_eimScreenshot = new ExtendedImage(File.ReadAllBytes(dialog.FileName));
					picScreenshot.Image = m_eimScreenshot;
					errorProvider.SetError(picScreenshot, String.Empty);
				}
				catch (Exception ex)
				{
					errorProvider.SetError(picScreenshot, "Unable to load the selected image: " + ex.Message, ErrorType.Warning);
				}
			}
		}

		/// <summary>
		/// Removes the screenshot from the current editor values.
		/// </summary>
		/// <param name="sender">The clear-screenshot button.</param>
		/// <param name="e">The event arguments.</param>
		private void btnClearScreenshot_Click(object sender, EventArgs e)
		{
			m_eimScreenshot = null;
			picScreenshot.Image = null;
			errorProvider.SetError(picScreenshot, String.Empty);
		}

		/// <summary>
		/// Restores the metadata currently persisted on the local archive.
		/// </summary>
		/// <param name="sender">The restore button.</param>
		/// <param name="e">The event arguments.</param>
		private void btnRestoreCurrent_Click(object sender, EventArgs e)
		{
			if (ViewModel == null)
				return;

			grvCandidates.ClearSelection();
			grvCandidates.FocusedRowHandle = DevExpress.XtraGrid.GridControl.InvalidRowHandle;
			ViewModel.LoadCurrentModInfo();
			LoadEditorValues();
		}

		/// <summary>
		/// Validates and persists the edited mod metadata.
		/// </summary>
		/// <param name="sender">The save button.</param>
		/// <param name="e">The event arguments.</param>
		private void btnOK_Click(object sender, EventArgs e)
		{
			if (ViewModel == null)
				return;

			errorProvider.ClearErrors();
			string error;
			if (!ViewModel.TrySaveTags(
				txtName.Text,
				txtVersion.Text,
				txtAuthor.Text,
				txtWebsite.Text,
				txtModId.Text,
				txtFileId.Text,
				txtDescription.Text,
				m_eimScreenshot,
				out error))
			{
				ApplySaveError(error);
				return;
			}

			DialogResult = DialogResult.OK;
			Close();
		}

		/// <summary>
		/// Displays a save validation error beside the most relevant editor.
		/// </summary>
		/// <param name="error">The validation error to display.</param>
		private void ApplySaveError(string error)
		{
			if (String.IsNullOrEmpty(error))
				error = "The mod information could not be saved.";

			if (error.IndexOf("name", StringComparison.OrdinalIgnoreCase) >= 0)
				errorProvider.SetError(txtName, error, ErrorType.Critical);
			else if (error.IndexOf("file ID", StringComparison.OrdinalIgnoreCase) >= 0)
				errorProvider.SetError(txtFileId, error, ErrorType.Critical);
			else if (error.IndexOf("mod ID", StringComparison.OrdinalIgnoreCase) >= 0)
				errorProvider.SetError(txtModId, error, ErrorType.Critical);
			else
				errorProvider.SetError(txtWebsite, error, ErrorType.Critical);
		}

		/// <summary>
		/// Keeps the save and cancel actions aligned when the dialog is resized.
		/// </summary>
		/// <param name="sender">The action panel.</param>
		/// <param name="e">The event arguments.</param>
		private void buttonPanel_Resize(object sender, EventArgs e)
		{
			const Int32 margin = 12;
			const Int32 gap = 8;
			btnCancel.Left = buttonPanel.ClientSize.Width - margin - btnCancel.Width;
			btnOK.Left = btnCancel.Left - gap - btnOK.Width;
			btnRestoreCurrent.Left = margin;
		}
	}
}
