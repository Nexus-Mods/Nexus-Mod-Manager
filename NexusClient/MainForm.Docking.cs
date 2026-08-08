namespace Nexus.Client
{
	using System;
	using System.Drawing;
	using System.IO;
	using System.Windows.Forms;

	using DevExpress.XtraBars.Docking;
	using DevExpress.XtraBars.Docking2010;
	using DevExpress.XtraBars.Docking2010.Views;
	using DevExpress.XtraBars.Docking2010.Views.Tabbed;

	/// <summary>
	/// Contains the DevExpress document and docking infrastructure used by the main window.
	/// </summary>
	public partial class MainForm
	{
		private const string DevExpressDockLayoutSettingsKey = "mainForm.DevExpressDockLayout";
		private const string DevExpressDocumentLayoutSettingsKey = "mainForm.DevExpressDocumentLayout";
		private const string DevExpressActiveDocumentSettingsKey = "mainForm.DevExpressActiveDocument";
		private const int DefaultMonitorPanelHeight = 185;

		private DockManager _mainDockManager;
		private DockPanel _downloadMonitorDockPanel;
		private DockPanel _modActivationMonitorDockPanel;
		private DocumentManager _mainDocumentManager;
		private TabbedView _mainTabbedView;
		private bool _applyDefaultMonitorSizeOnShown;

		/// <summary>
		/// Creates the DevExpress document manager and dock manager that replace the
		/// legacy DockPanelSuite container on the main window.
		/// </summary>
		private void InitializeMainDockingInfrastructure()
		{
			_mainDockManager = new DockManager(components)
			{
				Form = this
			};

			// All permanent NMM pages are Form/DockContent descendants, so use the
			// DocumentManager's native MDI mode instead of trying to host top-level
			// Forms as ordinary controls. TabbedView then presents those MDI children
			// as the Mods / Plugins / Categories / File Manager tab strip.
			IsMdiContainer = true;
			_mainTabbedView = new TabbedView();
			_mainDocumentManager = new DocumentManager(components)
			{
				MdiParent = this,
				View = _mainTabbedView
			};

			// Main NMM surfaces are permanent application pages. Keep them as tabs and
			// prevent users from accidentally closing/floating them out of the main UI.
			_mainTabbedView.DocumentProperties.AllowClose = false;
			_mainTabbedView.DocumentProperties.AllowFloat = false;
			_mainTabbedView.DocumentProperties.AllowDock = false;
			_mainTabbedView.DocumentSelected += MainTabbedView_DocumentSelected;

			CreateMonitorDockPanels();

			// DockManager adds its own edge controls after the standalone main bars.
			// Keep NMM's toolbar/status hosts outermost so document and monitor areas
			// are always laid out between them instead of over them.
			barMainToolbarHost?.BringToFront();
			barStatusHost?.BringToFront();
		}

		/// <summary>
		/// Creates the Download Manager and Mod Activation Queue as native DevExpress
		/// dock panels and embeds the existing monitor forms inside their containers.
		/// </summary>
		private void CreateMonitorDockPanels()
		{
			_downloadMonitorDockPanel = _mainDockManager.AddPanel(DockingStyle.Bottom);
			_downloadMonitorDockPanel.Name = "downloadMonitorDockPanel";
			_downloadMonitorDockPanel.Text = String.IsNullOrEmpty(_downloadMonitorControl.Text)
				? "Download Manager"
				: _downloadMonitorControl.Text;
			_downloadMonitorDockPanel.Options.ShowAutoHideButton = true;
			_downloadMonitorDockPanel.Options.ShowCloseButton = false;
			EmbedDockContent(_downloadMonitorControl, _downloadMonitorDockPanel);

			_modActivationMonitorDockPanel = _mainDockManager.AddPanel(DockingStyle.Bottom);
			_modActivationMonitorDockPanel.Name = "modActivationMonitorDockPanel";
			_modActivationMonitorDockPanel.Text = String.IsNullOrEmpty(_modActivationMonitorControl.Text)
				? "Mod Activation Queue"
				: _modActivationMonitorControl.Text;
			_modActivationMonitorDockPanel.Options.ShowAutoHideButton = true;
			_modActivationMonitorDockPanel.Options.ShowCloseButton = false;
			EmbedDockContent(_modActivationMonitorControl, _modActivationMonitorDockPanel);

			_downloadMonitorControl.TextChanged += (sender, args) =>
			{
				if (_downloadMonitorDockPanel != null)
					_downloadMonitorDockPanel.Text = _downloadMonitorControl.Text;
			};
			_modActivationMonitorControl.TextChanged += (sender, args) =>
			{
				if (_modActivationMonitorDockPanel != null)
					_modActivationMonitorDockPanel.Text = _modActivationMonitorControl.Text;
			};
		}

		/// <summary>
		/// Embeds a legacy DockContent-derived form in a DevExpress dock panel without
		/// using DockPanelSuite for layout or auto-hide behavior.
		/// </summary>
		/// <param name="content">The existing monitor form.</param>
		/// <param name="panel">The DevExpress dock panel that owns the form.</param>
		private static void EmbedDockContent(Form content, DockPanel panel)
		{
			if (content == null || panel?.ControlContainer == null)
				return;

			content.TopLevel = false;
			content.FormBorderStyle = FormBorderStyle.None;
			content.Dock = DockStyle.Fill;
			panel.ControlContainer.Controls.Add(content);
		}

		/// <summary>
		/// Shows the embedded monitor forms only after their view models have been assigned,
		/// so their Load handlers can restore persisted grid state correctly.
		/// </summary>
		private void ShowEmbeddedDockContents()
		{
			if (_downloadMonitorControl != null && !_downloadMonitorControl.Visible)
				_downloadMonitorControl.Show();

			if (_modActivationMonitorControl != null && !_modActivationMonitorControl.Visible)
				_modActivationMonitorControl.Show();
		}

		/// <summary>
		/// Registers the permanent NMM pages as MDI children so DevExpress TabbedView
		/// supplies the main Mods / Plugins / Categories / File Manager tab strip.
		/// </summary>
		private void EnsureMainDocuments()
		{
			EnsureMdiDocument((Form)_modManagerControl, "ModManagerDocument", "Mods");

			if (ViewModel.UsesPlugins)
				EnsureMdiDocument(_pluginManagerControl, "PluginManagerDocument", "Plugins");
			else if (_pluginManagerControl.Visible)
				_pluginManagerControl.Hide();

			EnsureMdiDocument(_categoryManagerControl, "CategoryManagerDocument", "Categories");

			if (IsFileManagerAvailable())
				EnsureMdiDocument(_fileManagerControl, "FileManagerDocument", "File Manager");
		}

		/// <summary>
		/// Registers one permanent NMM page as an MDI child. DevExpress TabbedView
		/// automatically wraps the child Form in a document and supplies its tab.
		/// </summary>
		private void EnsureMdiDocument(Form form, string fallbackName, string caption)
		{
			if (form == null)
				return;

			if (String.IsNullOrWhiteSpace(form.Name))
				form.Name = fallbackName;

			form.Text = caption;
			if (!Object.ReferenceEquals(form.MdiParent, this))
				form.MdiParent = this;

			if (!form.Visible)
				form.Show();
		}

		/// <summary>
		/// Applies NMM's default main-window layout using DevExpress docking primitives.
		/// </summary>
		private void ApplyDefaultMainDockingLayout()
		{
			ApplyDefaultMonitorDockingLayout();
			_mainTabbedView.ActivateDocument((Control)_modManagerControl);
		}

		/// <summary>
		/// Restores the default split arrangement for the two bottom monitor panels.
		/// </summary>
		private void ApplyDefaultMonitorDockingLayout()
		{
			_mainDockManager.BeginUpdate();
			try
			{
				_downloadMonitorDockPanel.Visibility = DockVisibility.Visible;
				_modActivationMonitorDockPanel.Visibility = DockVisibility.Visible;

				_downloadMonitorDockPanel.DockTo(DockingStyle.Bottom);
				_modActivationMonitorDockPanel.DockTo(_downloadMonitorDockPanel, DockingStyle.Right);
				_applyDefaultMonitorSizeOnShown = true;
			}
			finally
			{
				_mainDockManager.EndUpdate();
			}

			// DevExpress explicitly requires DockPanel.Size changes after panels have
			// been fully initialized. Reset UI can run after MainForm is already shown.
			if (Visible && IsHandleCreated)
				ApplyDefaultMonitorPanelSizes();
		}

		/// <summary>
		/// Applies the initial bottom-panel height and equal split after DockManager has
		/// completed control initialization.
		/// </summary>
		private void ApplyDefaultMonitorPanelSizes()
		{
			if (!_applyDefaultMonitorSizeOnShown || _downloadMonitorDockPanel == null ||
				_modActivationMonitorDockPanel == null)
				return;

			DockPanel monitorContainer = _downloadMonitorDockPanel.ParentPanel;
			int availableWidth = monitorContainer?.ClientSize.Width ?? ClientSize.Width;
			int halfWidth = Math.Max(200, availableWidth / 2);

			if (monitorContainer != null)
				monitorContainer.Size = new Size(Math.Max(400, availableWidth), DefaultMonitorPanelHeight);
			else
				_downloadMonitorDockPanel.Size = new Size(Math.Max(400, ClientSize.Width), DefaultMonitorPanelHeight);

			_downloadMonitorDockPanel.Size = new Size(halfWidth, DefaultMonitorPanelHeight);
			_modActivationMonitorDockPanel.Size = new Size(halfWidth, DefaultMonitorPanelHeight);
			_applyDefaultMonitorSizeOnShown = false;
		}

		/// <summary>
		/// Restores the persisted DevExpress docking and document layouts, if present.
		/// Legacy DockPanelSuite XML stored under "mainForm" is intentionally ignored.
		/// </summary>
		/// <returns>True when at least one DevExpress layout was restored.</returns>
		private bool RestoreMainDockingLayout()
		{
			bool dockRestored = false;
			bool documentRestored = false;
			string dockLayout = GetLayoutSetting(DevExpressDockLayoutSettingsKey);
			string documentLayout = GetLayoutSetting(DevExpressDocumentLayoutSettingsKey);

			if (!String.IsNullOrEmpty(dockLayout))
			{
				try
				{
					_mainDockManager.ForceInitialize();
					using (MemoryStream stream = DecodeLayout(dockLayout))
						_mainDockManager.RestoreLayoutFromStream(stream);
					dockRestored = true;
					_applyDefaultMonitorSizeOnShown = false;
				}
				catch
				{
					// Invalid/obsolete layout: fall back to the deterministic default below.
				}
			}

			if (!dockRestored)
				ApplyDefaultMonitorDockingLayout();

			if (!String.IsNullOrEmpty(documentLayout))
			{
				try
				{
					using (MemoryStream stream = DecodeLayout(documentLayout))
						_mainTabbedView.RestoreLayoutFromStream(stream, true);
					documentRestored = true;
				}
				catch
				{
					// An invalid document layout must not prevent NMM from opening.
				}
			}

			RestoreActiveMainDocument();
			return dockRestored || documentRestored;
		}

		/// <summary>
		/// Saves the DevExpress docking layout and selected main document to the existing
		/// NMM settings store.
		/// </summary>
		private void SaveMainDockingLayout()
		{
			if (ViewModel?.EnvironmentInfo?.Settings?.DockPanelLayouts == null ||
				_mainDockManager == null || _mainTabbedView == null)
				return;

			using (MemoryStream dockStream = new MemoryStream())
			{
				_mainDockManager.SaveLayoutToStream(dockStream);
				ViewModel.EnvironmentInfo.Settings.DockPanelLayouts[DevExpressDockLayoutSettingsKey] =
					Convert.ToBase64String(dockStream.ToArray());
			}

			using (MemoryStream documentStream = new MemoryStream())
			{
				_mainTabbedView.SaveLayoutToStream(documentStream);
				ViewModel.EnvironmentInfo.Settings.DockPanelLayouts[DevExpressDocumentLayoutSettingsKey] =
					Convert.ToBase64String(documentStream.ToArray());
			}

			BaseDocument activeDocument = _mainTabbedView.ActiveDocument;
			if (activeDocument?.Control != null)
				ViewModel.EnvironmentInfo.Settings.DockPanelLayouts[DevExpressActiveDocumentSettingsKey] = activeDocument.Control.Name;
		}

		/// <summary>
		/// Clears the persisted DevExpress main-window layouts and immediately restores
		/// the deterministic default docking arrangement.
		/// </summary>
		private void ResetMainDockingLayout()
		{
			if (ViewModel?.EnvironmentInfo?.Settings?.DockPanelLayouts != null)
			{
				ViewModel.EnvironmentInfo.Settings.DockPanelLayouts.Remove("mainForm");
				ViewModel.EnvironmentInfo.Settings.DockPanelLayouts.Remove(DevExpressDockLayoutSettingsKey);
				ViewModel.EnvironmentInfo.Settings.DockPanelLayouts.Remove(DevExpressDocumentLayoutSettingsKey);
				ViewModel.EnvironmentInfo.Settings.DockPanelLayouts.Remove(DevExpressActiveDocumentSettingsKey);
			}

			EnsureMainDocuments();
			ApplyDefaultMainDockingLayout();
		}

		/// <summary>
		/// Restores the last selected permanent main document, falling back to Mods.
		/// </summary>
		private void RestoreActiveMainDocument()
		{
			string activeControlName = GetLayoutSetting(DevExpressActiveDocumentSettingsKey);
			if (!String.IsNullOrEmpty(activeControlName))
			{
				foreach (BaseDocument document in _mainTabbedView.Documents)
				{
					if (document.Control != null && String.Equals(document.Control.Name, activeControlName, StringComparison.Ordinal))
					{
						_mainTabbedView.ActivateDocument(document.Control);
						return;
					}
				}
			}

			_mainTabbedView.ActivateDocument((Control)_modManagerControl);
		}

		/// <summary>
		/// Handles main-document selection changes and performs lazy initialization for
		/// document content that is intentionally loaded only when first opened.
		/// </summary>
		private async void MainTabbedView_DocumentSelected(object sender, DocumentEventArgs e)
		{
			SetBarItemVisible(toolStripTextBoxFind, false);
			toolStripTextBoxFind.Enabled = false;

			if (Visible && e?.Document?.Control != null && Object.ReferenceEquals(e.Document.Control, _fileManagerControl))
				await _fileManagerControl.EnsureInitialLoadAsync().ConfigureAwait(true);
		}

		/// <summary>
		/// Returns whether the requested control is the active main document.
		/// </summary>
		private bool IsMainDocumentActive(Control control)
		{
			return control != null && _mainTabbedView?.ActiveDocument?.Control != null &&
				Object.ReferenceEquals(_mainTabbedView.ActiveDocument.Control, control);
		}

		/// <summary>
		/// Activates the Download Manager without pinning an auto-hidden panel.
		/// </summary>
		private void ShowDownloadMonitorPanel()
		{
			if (_downloadMonitorDockPanel == null || _mainDockManager == null)
				return;

			if (_downloadMonitorDockPanel.Visibility == DockVisibility.Hidden)
				_downloadMonitorDockPanel.Visibility = DockVisibility.Visible;

			_mainDockManager.ActivePanel = _downloadMonitorDockPanel;
		}

		/// <summary>
		/// Reads a layout value from the existing settings dictionary.
		/// </summary>
		private string GetLayoutSetting(string key)
		{
			if (ViewModel?.EnvironmentInfo?.Settings?.DockPanelLayouts == null ||
				!ViewModel.EnvironmentInfo.Settings.DockPanelLayouts.ContainsKey(key))
				return null;

			return ViewModel.EnvironmentInfo.Settings.DockPanelLayouts[key];
		}

		/// <summary>
		/// Decodes a base64-encoded DevExpress layout into a readable memory stream.
		/// </summary>
		private static MemoryStream DecodeLayout(string encodedLayout)
		{
			byte[] bytes = Convert.FromBase64String(encodedLayout);
			return new MemoryStream(bytes, false);
		}
	}
}
