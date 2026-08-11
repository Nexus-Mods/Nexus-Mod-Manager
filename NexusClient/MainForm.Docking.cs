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
		private const string DevExpressDockLayoutSettingsKey = "mainForm.DevExpressDockLayout.v4";
		private const string DevExpressActiveDocumentSettingsKey = "mainForm.DevExpressActiveDocument.v3";
		private const string LegacyDevExpressActiveDocumentV2SettingsKey = "mainForm.DevExpressActiveDocument.v2";
		private const string LegacyDevExpressDockLayoutV3SettingsKey = "mainForm.DevExpressDockLayout.v3";
		private const string LegacyDevExpressDockLayoutV2SettingsKey = "mainForm.DevExpressDockLayout.v2";
		private const string LegacyDevExpressDockLayoutSettingsKey = "mainForm.DevExpressDockLayout";
		private const string LegacyDevExpressDocumentLayoutSettingsKey = "mainForm.DevExpressDocumentLayout";
		private const string LegacyDevExpressActiveDocumentSettingsKey = "mainForm.DevExpressActiveDocument";
		private const int DefaultMonitorPanelHeight = 185;
		private static readonly Guid DownloadMonitorDockPanelId = new Guid("{8C4AC717-2912-4E84-9059-2B5658763381}");
		private static readonly Guid ModActivationMonitorDockPanelId = new Guid("{7BDB051E-BC92-4354-9861-9E909B369D83}");

		private DockManager _mainDockManager;
		private DockPanel _downloadMonitorDockPanel;
		private DockPanel _modActivationMonitorDockPanel;
		private DocumentManager _mainDocumentManager;
		private TabbedView _mainTabbedView;
		private bool _applyDefaultMonitorSizeOnShown;
		private bool _mainDocumentPersistenceEnabled;

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

			// Mods, Plugins, Categories and File Manager are still Form/DockContent
			// descendants. Use DocumentManager's native MDI mode so TabbedView can
			// wrap those forms as permanent documents without a second docking system.
			IsMdiContainer = true;
			_mainTabbedView = new TabbedView();
			_mainDocumentManager = new DocumentManager(components)
			{
				MdiParent = this,
				View = _mainTabbedView
			};

			_mainTabbedView.DocumentProperties.AllowClose = false;
			_mainTabbedView.DocumentProperties.AllowFloat = false;
			_mainTabbedView.DocumentProperties.AllowDock = false;
			_mainTabbedView.DocumentSelected += MainTabbedView_DocumentSelected;

			CreateMonitorDockPanels();

			barMainToolbarHost?.BringToFront();
			barStatusHost?.BringToFront();
		}

		/// <summary>
		/// Creates the Download Manager and Mod Activation Queue as native DevExpress
		/// dock panels using real controls as their content.
		/// </summary>
		private void CreateMonitorDockPanels()
		{
			_downloadMonitorDockPanel = _mainDockManager.AddPanel(DockingStyle.Bottom);
			_downloadMonitorDockPanel.Name = "downloadMonitorDockPanel";
			_downloadMonitorDockPanel.ID = DownloadMonitorDockPanelId;
			_downloadMonitorDockPanel.Text = String.IsNullOrEmpty(_downloadMonitorControl.Text) ? "Download Manager" : _downloadMonitorControl.Text;
			_downloadMonitorDockPanel.Options.ShowAutoHideButton = true;
			_downloadMonitorDockPanel.Options.ShowCloseButton = false;
			_downloadMonitorDockPanel.Options.AllowDockAsTabbedDocument = false;
			EnsureMonitorDockContent(_downloadMonitorDockPanel, _downloadMonitorControl);

			_modActivationMonitorDockPanel = _mainDockManager.AddPanel(DockingStyle.Bottom);
			_modActivationMonitorDockPanel.Name = "modActivationMonitorDockPanel";
			_modActivationMonitorDockPanel.ID = ModActivationMonitorDockPanelId;
			_modActivationMonitorDockPanel.Text = String.IsNullOrEmpty(_modActivationMonitorControl.Text) ? "Mod Activation Queue" : _modActivationMonitorControl.Text;
			_modActivationMonitorDockPanel.Options.ShowAutoHideButton = true;
			_modActivationMonitorDockPanel.Options.ShowCloseButton = false;
			_modActivationMonitorDockPanel.Options.AllowDockAsTabbedDocument = false;
			EnsureMonitorDockContent(_modActivationMonitorDockPanel, _modActivationMonitorControl);

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
		/// Ensures the monitor controls remain visible after their view models have been assigned.
		/// </summary>
		private void ShowEmbeddedDockContents()
		{
			RefreshMonitorDockPanelReferences();
			EnsureMonitorDockContent(_downloadMonitorDockPanel, _downloadMonitorControl);
			EnsureMonitorDockContent(_modActivationMonitorDockPanel, _modActivationMonitorControl);
		}

		/// <summary>
		/// Refreshes the monitor panel references after a DevExpress layout restore.
		/// DockManager may replace runtime panel instances while restoring a serialized layout.
		/// </summary>
		private void RefreshMonitorDockPanelReferences()
		{
			if (_mainDockManager == null)
				return;

			DockPanel downloadPanel = _mainDockManager["downloadMonitorDockPanel"];
			if (downloadPanel != null)
			{
				_downloadMonitorDockPanel = downloadPanel;
				_downloadMonitorDockPanel.Options.AllowDockAsTabbedDocument = false;
			}

			DockPanel activationPanel = _mainDockManager["modActivationMonitorDockPanel"];
			if (activationPanel != null)
			{
				_modActivationMonitorDockPanel = activationPanel;
				_modActivationMonitorDockPanel.Options.AllowDockAsTabbedDocument = false;
			}
		}

		/// <summary>
		/// Reattaches a monitor control to the current DevExpress dock-panel container.
		/// Layout restoration may recreate the panel's control container, so merely setting
		/// the child control visible is not sufficient after a manager restart.
		/// </summary>
		/// <param name="panel">The DevExpress panel that owns the monitor.</param>
		/// <param name="content">The monitor control to host.</param>
		private static void EnsureMonitorDockContent(DockPanel panel, Control content)
		{
			if (panel == null || content == null || panel.ControlContainer == null)
				return;

			Control container = panel.ControlContainer;
			if (!Object.ReferenceEquals(content.Parent, container))
			{
				content.Parent?.Controls.Remove(content);
				container.Controls.Add(content);
			}

			content.Dock = DockStyle.Fill;
			content.Visible = true;
			content.BringToFront();
			content.PerformLayout();
			container.PerformLayout();
		}

		/// <summary>
		/// Registers the permanent NMM pages in their fixed tab order.
		/// </summary>
		private void EnsureMainDocuments()
		{
			if (ViewModel.UsesPlugins)
				EnsureMdiDocument(_pluginManagerControl, "PluginManagerDocument", "Plugins");
			else if (_pluginManagerControl.Visible)
				_pluginManagerControl.Hide();

			EnsureMdiDocument((Form)_modManagerControl, "ModManagerDocument", "Mods");
			EnsureMdiDocument(_categoryManagerControl, "CategoryManagerDocument", "Categories");

			if (IsFileManagerAvailable())
				EnsureMdiDocument(_fileManagerControl, "FileManagerDocument", "File Manager");
			else if (_fileManagerControl.Visible)
				_fileManagerControl.Hide();
		}

		/// <summary>
		/// Registers one permanent NMM page as an MDI child. DevExpress TabbedView
		/// automatically creates the corresponding document and tab.
		/// </summary>
		/// <param name="form">The page form to register.</param>
		/// <param name="fallbackName">The stable form name used when none has been assigned.</param>
		/// <param name="caption">The visible document caption.</param>
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
		/// Finds the document that currently owns the specified control.
		/// </summary>
		/// <param name="control">The control to locate.</param>
		/// <returns>The matching document, or null when the control is not registered.</returns>
		private BaseDocument FindMainDocument(Control control)
		{
			if (control == null || _mainTabbedView == null)
				return null;

			foreach (BaseDocument document in _mainTabbedView.Documents)
			{
				if (Object.ReferenceEquals(document.Control, control))
					return document;
			}

			return null;
		}

		/// <summary>
		/// Applies NMM's deterministic main-window layout and selects Mods.
		/// </summary>
		private void ApplyDefaultMainDockingLayout()
		{
			ApplyDefaultMonitorDockingLayout();
			ActivateModsDocument();
		}

		/// <summary>
		/// Restores the default split arrangement for the two bottom monitor panels.
		/// </summary>
		private void ApplyDefaultMonitorDockingLayout()
		{
			_mainDockManager.BeginUpdate();
			try
			{
				_downloadMonitorDockPanel.DockedAsTabbedDocument = false;
				_modActivationMonitorDockPanel.DockedAsTabbedDocument = false;
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
			int halfWidth = Math.Max(240, availableWidth / 2);

			if (monitorContainer != null)
				monitorContainer.Size = new Size(Math.Max(480, availableWidth), DefaultMonitorPanelHeight);
			else
				_downloadMonitorDockPanel.Size = new Size(Math.Max(480, ClientSize.Width), DefaultMonitorPanelHeight);

			_downloadMonitorDockPanel.Size = new Size(halfWidth, DefaultMonitorPanelHeight);
			_modActivationMonitorDockPanel.Size = new Size(halfWidth, DefaultMonitorPanelHeight);
			_downloadMonitorControl.PerformLayout();
			_modActivationMonitorControl.PerformLayout();
			RestoreMonitorColumnWidths();
			_applyDefaultMonitorSizeOnShown = false;
		}

		/// <summary>
		/// Restores only the versioned DevExpress monitor docking layout. Permanent document
		/// tabs are rebuilt every startup so obsolete layouts cannot hide or reorder them.
		/// </summary>
		/// <returns>True when a compatible dock layout was restored.</returns>
		private bool RestoreMainDockingLayout()
		{
			bool dockRestored = false;
			string dockLayout = GetLayoutSetting(DevExpressDockLayoutSettingsKey);

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
					// Invalid/obsolete layout: use the deterministic default below.
				}
			}

			if (!dockRestored)
				ApplyDefaultMonitorDockingLayout();

			ShowEmbeddedDockContents();
			if (_downloadMonitorDockPanel.DockedAsTabbedDocument || _modActivationMonitorDockPanel.DockedAsTabbedDocument)
			{
				ApplyDefaultMonitorDockingLayout();
				dockRestored = false;
			}

			RestoreActiveMainDocument();
			return dockRestored;
		}

		/// <summary>
		/// Saves the DevExpress monitor docking layout to the existing NMM settings store.
		/// Permanent document tabs intentionally are not serialized.
		/// </summary>
		private void SaveMainDockingLayout()
		{
			if (ViewModel?.EnvironmentInfo?.Settings?.DockPanelLayouts == null || _mainDockManager == null)
				return;

			ShowEmbeddedDockContents();
			using (MemoryStream dockStream = new MemoryStream())
			{
				_mainDockManager.SaveLayoutToStream(dockStream);
				ViewModel.EnvironmentInfo.Settings.DockPanelLayouts[DevExpressDockLayoutSettingsKey] =
					Convert.ToBase64String(dockStream.ToArray());
			}

			RemoveLegacyMainLayoutSettings();
		}

		/// <summary>
		/// Clears persisted main-window layouts and immediately restores the deterministic defaults.
		/// </summary>
		private void ResetMainDockingLayout()
		{
			if (ViewModel?.EnvironmentInfo?.Settings?.DockPanelLayouts != null)
			{
				ViewModel.EnvironmentInfo.Settings.DockPanelLayouts.Remove("mainForm");
				ViewModel.EnvironmentInfo.Settings.DockPanelLayouts.Remove(DevExpressDockLayoutSettingsKey);
				ViewModel.EnvironmentInfo.Settings.DockPanelLayouts.Remove(DevExpressActiveDocumentSettingsKey);
				ViewModel.EnvironmentInfo.Settings.DockPanelLayouts.Remove(LegacyDevExpressDockLayoutV3SettingsKey);
				ViewModel.EnvironmentInfo.Settings.DockPanelLayouts.Remove(LegacyDevExpressDockLayoutV2SettingsKey);
				RemoveLegacyMainLayoutSettings();
			}

			EnsureMainDocuments();
			ApplyDefaultMainDockingLayout();
		}

		/// <summary>
		/// Removes layout keys written by the broken intermediate document-host implementation.
		/// </summary>
		private void RemoveLegacyMainLayoutSettings()
		{
			if (ViewModel?.EnvironmentInfo?.Settings?.DockPanelLayouts == null)
				return;

			ViewModel.EnvironmentInfo.Settings.DockPanelLayouts.Remove(LegacyDevExpressDockLayoutV3SettingsKey);
			ViewModel.EnvironmentInfo.Settings.DockPanelLayouts.Remove(LegacyDevExpressDockLayoutV2SettingsKey);
			ViewModel.EnvironmentInfo.Settings.DockPanelLayouts.Remove(LegacyDevExpressDockLayoutSettingsKey);
			ViewModel.EnvironmentInfo.Settings.DockPanelLayouts.Remove(LegacyDevExpressDocumentLayoutSettingsKey);
			ViewModel.EnvironmentInfo.Settings.DockPanelLayouts.Remove(LegacyDevExpressActiveDocumentV2SettingsKey);
			ViewModel.EnvironmentInfo.Settings.DockPanelLayouts.Remove(LegacyDevExpressActiveDocumentSettingsKey);
		}

		/// <summary>
		/// Activates the Mods page, which is always the default main document.
		/// </summary>
		private void ActivateModsDocument()
		{
			if (_mainTabbedView != null && FindMainDocument((Control)_modManagerControl) != null)
				_mainTabbedView.ActivateDocument((Control)_modManagerControl);
		}

		/// <summary>
		/// Restores the last selected permanent main document, falling back to Mods when
		/// the saved page is not available in the current game mode.
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

			ActivateModsDocument();
		}

		/// <summary>
		/// Captures the selected permanent main document while the MDI children are still active.
		/// </summary>
		/// <param name="selectedControl">The selected document control, or null to use the active document.</param>
		private void SaveActiveMainDocument(Control selectedControl = null)
		{
			if (ViewModel?.EnvironmentInfo?.Settings?.DockPanelLayouts == null)
				return;

			Control activeControl = selectedControl ?? _mainTabbedView?.ActiveDocument?.Control;
			if (activeControl != null)
				ViewModel.EnvironmentInfo.Settings.DockPanelLayouts[DevExpressActiveDocumentSettingsKey] = activeControl.Name;
		}

		/// <summary>
		/// Reapplies monitor column widths after the final dock and window dimensions are known.
		/// </summary>
		private void RestoreMonitorColumnWidths()
		{
			_downloadMonitorControl?.RestorePersistedColumnWidths();
			_modActivationMonitorControl?.RestorePersistedColumnWidths();
		}

		/// <summary>
		/// Handles main-document selection changes and performs lazy initialization for
		/// document content that is intentionally loaded only when first opened.
		/// </summary>
		private async void MainTabbedView_DocumentSelected(object sender, DocumentEventArgs e)
		{
			SetBarItemVisible(toolStripTextBoxFind, false);
			toolStripTextBoxFind.Enabled = false;

			if (_mainDocumentPersistenceEnabled && Visible && e?.Document?.Control != null)
			{
				SaveActiveMainDocument(e.Document.Control);
				ViewModel.EnvironmentInfo.Settings.Save();
			}

			if (_mainDocumentPersistenceEnabled && Visible && e?.Document?.Control != null && Object.ReferenceEquals(e.Document.Control, _fileManagerControl))
				await _fileManagerControl.EnsureInitialLoadAsync().ConfigureAwait(true);
		}

		/// <summary>
		/// Restores the persisted main document after DevExpress has completed its startup
		/// MDI activation sequence, then enables persistence for genuine user selections.
		/// </summary>
		/// <param name="sender">The application that raised the idle event.</param>
		/// <param name="e">The event arguments.</param>
		private async void RestoreActiveMainDocumentOnIdle(object sender, EventArgs e)
		{
			Application.Idle -= RestoreActiveMainDocumentOnIdle;
			RestoreActiveMainDocument();
			_mainDocumentPersistenceEnabled = true;

			if (IsMainDocumentActive(_fileManagerControl))
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
		/// Activates the Download Manager without changing its pin/auto-hide state.
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
