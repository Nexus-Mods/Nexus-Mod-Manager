namespace Nexus.Client
{
	using System;
	using System.Collections.Generic;
	using System.Drawing;
	using System.IO;
	using System.Linq;
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
		private const string DevExpressDocumentOrderSettingsKey = "mainForm.DevExpressDocumentOrder.v1";
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
		private bool _mainDocumentStartupUpdatePending;

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

			// Mods, Plugins, Collections, Categories and File Manager are Form/DockContent
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
			_mainTabbedView.DocumentActivated += MainTabbedView_DocumentActivated;
			_mainTabbedView.BeginUpdate();
			_mainDocumentStartupUpdatePending = true;

			if (_downloadMonitorControl != null && _modActivationMonitorControl != null)
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
			_downloadMonitorDockPanel.Text = String.IsNullOrEmpty(_downloadMonitorControl.Text) ? L("MainForm.Dock.DownloadManager", "Download Manager") : _downloadMonitorControl.Text;
			_downloadMonitorDockPanel.Options.ShowAutoHideButton = true;
			_downloadMonitorDockPanel.Options.ShowCloseButton = false;
			_downloadMonitorDockPanel.Options.AllowDockAsTabbedDocument = false;
			EnsureMonitorDockContent(_downloadMonitorDockPanel, _downloadMonitorControl);

			_modActivationMonitorDockPanel = _mainDockManager.AddPanel(DockingStyle.Bottom);
			_modActivationMonitorDockPanel.Name = "modActivationMonitorDockPanel";
			_modActivationMonitorDockPanel.ID = ModActivationMonitorDockPanelId;
			_modActivationMonitorDockPanel.Text = String.IsNullOrEmpty(_modActivationMonitorControl.Text) ? L("MainForm.Dock.ModActivationQueue", "Mod Activation Queue") : _modActivationMonitorControl.Text;
			_modActivationMonitorDockPanel.Options.ShowAutoHideButton = true;
			_modActivationMonitorDockPanel.Options.ShowCloseButton = false;
			_modActivationMonitorDockPanel.Options.AllowDockAsTabbedDocument = false;
			EnsureMonitorDockContent(_modActivationMonitorDockPanel, _modActivationMonitorControl);

			_downloadMonitorControl.TextChanged += DownloadMonitor_TextChanged;
			_modActivationMonitorControl.TextChanged += ModActivationMonitor_TextChanged;
		}

		private void DownloadMonitor_TextChanged(object sender, EventArgs args)
		{
			if (_downloadMonitorDockPanel != null && Object.ReferenceEquals(sender, _downloadMonitorControl))
				_downloadMonitorDockPanel.Text = _downloadMonitorControl.Text;
		}

		private void ModActivationMonitor_TextChanged(object sender, EventArgs args)
		{
			if (_modActivationMonitorDockPanel != null && Object.ReferenceEquals(sender, _modActivationMonitorControl))
				_modActivationMonitorDockPanel.Text = _modActivationMonitorControl.Text;
		}

		/// <summary>
		/// Ensures the monitor controls remain visible after their view models have been assigned.
		/// </summary>
		private void ShowEmbeddedDockContents()
		{
			RefreshMonitorDockPanelReferences();
			EnsureMonitorDockContent(_downloadMonitorDockPanel, _downloadMonitorControl);
			EnsureMonitorDockContent(_modActivationMonitorDockPanel, _modActivationMonitorControl);

			// DockManager persists panel captions as part of its layout. Replace any
			// serialized task counts with the controls' current runtime captions.
			if (_downloadMonitorDockPanel != null)
				_downloadMonitorDockPanel.Text = String.IsNullOrEmpty(_downloadMonitorControl.Text) ? L("MainForm.Dock.DownloadManager", "Download Manager") : _downloadMonitorControl.Text;
			if (_modActivationMonitorDockPanel != null)
				_modActivationMonitorDockPanel.Text = String.IsNullOrEmpty(_modActivationMonitorControl.Text) ? L("MainForm.Dock.ModActivationQueue", "Mod Activation Queue") : _modActivationMonitorControl.Text;
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
		/// Registers the permanent NMM pages before their saved visual order is applied.
		/// </summary>
		private void EnsureMainDocuments()
		{
			if (ViewModel == null)
			{
				EnsureMdiDocument(_emptyModsDocument, "ModManagerDocument", L("MainForm.Tabs.Mods", "Mods"));
				EnsureMdiDocument(_emptyCategoriesDocument, "CategoryManagerDocument", L("MainForm.Tabs.Categories", "Categories"));
				EnsureMdiDocument(_emptyCollectionsDocument, "CollectionsDocument", L("MainForm.Tabs.Collections", "Collections"));
				EnsureMdiDocument(_gameDiscoveryDocument, "GameDiscoveryDocument", L("GameDiscovery.TabTitle", "Games"));
				return;
			}

			// Form.Show() activates MDI children. Register every non-preferred page first so
			// Mods is the last activation DevExpress can defer. Visual order is applied
			// after the startup update has ended and the document group is available.
			if (ViewModel.UsesPlugins)
			{
				EnsureMdiDocument(_pluginManagerControl, "PluginManagerDocument", L("MainForm.Tabs.Plugins", "Plugins"));
			}
			else if (_pluginManagerControl.Visible)
			{
				_pluginManagerControl.Hide();
			}

			if (ViewModel.Witcher3ModSettingsLifecycle != null)
			{
				EnsureMdiDocument(_witcher3LoadOrderControl, "Witcher3LoadOrderDocument",
					L("MainForm.Tabs.Witcher3LoadOrder", "Witcher Load Order"));
			}
			else if (_witcher3LoadOrderControl.Visible)
			{
				_witcher3LoadOrderControl.Hide();
			}

			if (ViewModel.GameMode is Games.ManagedContent.IManagedContentComponentProvider)
				EnsureMdiDocument(_managedContentComponentsControl, "ManagedContentComponentsDocument",
					L("MainForm.Tabs.Components", "Components"));
			else if (_managedContentComponentsControl.Visible)
				_managedContentComponentsControl.Hide();

			if (ViewModel.NativeLoadOrder != null)
				EnsureMdiDocument(_loadOrderControl, "LoadOrderDocument", L("LoadOrder.UI.Title", "Load Order") + (_loadOrderControl.HasUnsavedChanges ? " *" : String.Empty));
			else if (_loadOrderControl?.Visible == true)
				_loadOrderControl.Hide();

			EnsureMdiDocument(_categoryManagerControl, "CategoryManagerDocument", L("MainForm.Tabs.Categories", "Categories"));

			if (IsFileManagerAvailable())
			{
				EnsureMdiDocument(_fileManagerControl, "FileManagerDocument", L("MainForm.Tabs.FileManager", "File Manager"));
			}
			else if (_fileManagerControl.Visible)
			{
				_fileManagerControl.Hide();
			}

			EnsureMdiDocument(_collectionsPreviewControl, "CollectionsDocument", L("MainForm.Tabs.Collections", "Collections"));
			if (_wabbajackGalleryControl.IsAvailable)
				EnsureMdiDocument(_wabbajackGalleryControl, "WabbajackGalleryDocument", "Wabbajack");
			else if (_wabbajackGalleryControl.Visible)
				_wabbajackGalleryControl.Hide();
			EnsureMdiDocument(_gameDiscoveryDocument, "GameDiscoveryDocument", L("GameDiscovery.TabTitle", "Games"));
			EnsureMdiDocument((Form)_modManagerControl, "ModManagerDocument", L("MainForm.Tabs.Mods", "Mods"));
		}

		/// <summary>
		/// Returns all permanent controls in their canonical visual order, including currently unavailable pages.
		/// </summary>
		private Control[] GetDefaultMainDocumentOrder()
		{
			if (ViewModel == null)
				return new Control[] { _gameDiscoveryDocument, _emptyModsDocument, _emptyCategoriesDocument, _emptyCollectionsDocument };
			return new Control[] { _gameDiscoveryDocument, _pluginManagerControl, _witcher3LoadOrderControl, _loadOrderControl, (Control)_modManagerControl, _categoryManagerControl,
				_fileManagerControl, _collectionsPreviewControl, _wabbajackGalleryControl, _managedContentComponentsControl };
		}

		/// <summary>
		/// Decodes an existing saved order, ignoring obsolete and duplicate names and filling missing pages deterministically.
		/// </summary>
		private static List<string> ReadMainDocumentOrder(Control[] defaultOrder, string savedOrder)
		{
			string[] defaults = defaultOrder.Where(x => x != null && !String.IsNullOrWhiteSpace(x.Name)).Select(x => x.Name).ToArray();
			var names = new List<string>();
			foreach (string encodedName in (savedOrder ?? String.Empty).Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
			{
				string documentName;
				try
				{
					documentName = Uri.UnescapeDataString(encodedName);
				}
				catch (UriFormatException)
				{
					continue;
				}

				if (defaults.Contains(documentName, StringComparer.Ordinal) && !names.Contains(documentName))
					names.Add(documentName);
			}
			for (int index = 0; index < defaults.Length; index++)
			{
				if (names.Contains(defaults[index])) continue;
				int nextIndex = index + 1;
				while (nextIndex < defaults.Length && !names.Contains(defaults[nextIndex])) nextIndex++;
				names.Insert(nextIndex < defaults.Length ? names.IndexOf(defaults[nextIndex]) : names.Count, defaults[index]);
			}
			// Keep Wabbajack beside Collections even when restoring an earlier saved tab order.
			if (names.Contains("CollectionsDocument") && names.Remove("WabbajackGalleryDocument"))
				names.Insert(names.IndexOf("CollectionsDocument") + 1, "WabbajackGalleryDocument");
			return names;
		}

		/// <summary>Applies a saved or default visual order only after the real MDI documents have been created.</summary>
		internal static void ApplyMainDocumentOrder(TabbedView view, Control[] defaultOrder, string savedOrder)
		{
			int targetIndex = 0;
			foreach (string name in ReadMainDocumentOrder(defaultOrder, savedOrder))
			{
				Document document = view.Documents.OfType<Document>().FirstOrDefault(x => String.Equals(x.Control?.Name, name, StringComparison.Ordinal));
				if (document != null)
					view.Controller.Move(document, targetIndex++);
			}
		}

		/// <summary>Captures the visible tab order without discarding saved positions for pages unavailable in this game.</summary>
		internal static string CaptureMainDocumentOrder(TabbedView view, Control[] defaultOrder, string previousOrder)
		{
			if (view.DocumentGroups.Count != 1) return previousOrder;
			List<string> names = ReadMainDocumentOrder(defaultOrder, previousOrder);
			List<string> visibleNames = view.DocumentGroups[0].Items.OfType<BaseDocument>()
				.Select(x => x.Control?.Name).Where(x => names.Contains(x)).ToList();
			if (visibleNames.Count == 0 || visibleNames.Distinct(StringComparer.Ordinal).Count() != visibleNames.Count)
				return previousOrder;
			int visibleIndex = 0;
			for (int index = 0; index < names.Count; index++)
				if (visibleNames.Contains(names[index])) names[index] = visibleNames[visibleIndex++];
			return String.Join("|", names.Select(Uri.EscapeDataString));
		}

		/// <summary>
		/// Saves the current visual order of the permanent main tabs.
		/// </summary>
		private void SaveMainDocumentOrder()
		{
			if (ViewModel?.EnvironmentInfo?.Settings?.DockPanelLayouts == null ||
				!_mainDocumentPersistenceEnabled || _mainDocumentStartupUpdatePending || _mainTabbedView == null)
			{
				return;
			}

			string order = CaptureMainDocumentOrder(_mainTabbedView, GetDefaultMainDocumentOrder(), GetLayoutSetting(DevExpressDocumentOrderSettingsKey));
			if (!String.IsNullOrEmpty(order))
				ViewModel.EnvironmentInfo.Settings.DockPanelLayouts[DevExpressDocumentOrderSettingsKey] = order;
		}

		/// <summary>
		/// Applies the canonical Plugins, Mods, Categories, File Manager and Collections tab order.
		/// </summary>
		private void ApplyDefaultMainDocumentOrder()
		{
			ApplyMainDocumentOrder(_mainTabbedView, GetDefaultMainDocumentOrder(), null);
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
		/// tabs are rebuilt independently and their simple visual order is restored separately.
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

			return dockRestored;
		}

		/// <summary>
		/// Saves the DevExpress monitor docking layout to the existing NMM settings store.
		/// Permanent document tabs use a separate simple order value instead of layout serialization.
		/// </summary>
		private void SaveMainDockingLayout()
		{
			CompleteMainDocumentStartupUpdate();

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
				ViewModel.EnvironmentInfo.Settings.DockPanelLayouts.Remove(DevExpressDocumentOrderSettingsKey);
				ViewModel.EnvironmentInfo.Settings.DockPanelLayouts.Remove(LegacyDevExpressDockLayoutV3SettingsKey);
				ViewModel.EnvironmentInfo.Settings.DockPanelLayouts.Remove(LegacyDevExpressDockLayoutV2SettingsKey);
				RemoveLegacyMainLayoutSettings();
			}

			EnsureMainDocuments();
			ApplyDefaultMainDocumentOrder();
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
		/// Activates the dedicated read-only Collections document for an incoming NXM preview.
		/// </summary>
		private void ActivateCollectionsDocument()
		{
			if (_mainTabbedView != null && FindMainDocument(_collectionsPreviewControl) != null)
				_mainTabbedView.ActivateDocument(_collectionsPreviewControl);
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
			if (e?.Document?.Control == _gameDiscoveryDocument)
			{
				_collectionStatisticsDirty = true;
				CaptureGameStatistics();
			}
			if (_mainDocumentStartupUpdatePending || ViewModel == null)
				return;
			SetBarItemVisible(toolStripTextBoxFind, false);
			toolStripTextBoxFind.Enabled = false;

			if (_mainDocumentPersistenceEnabled && Visible && e?.Document?.Control != null && Object.ReferenceEquals(e.Document.Control, _fileManagerControl))
				await _fileManagerControl.EnsureInitialLoadAsync().ConfigureAwait(true);

			if (_mainDocumentPersistenceEnabled && Visible && e?.Document?.Control != null && Object.ReferenceEquals(e.Document.Control, _collectionsPreviewControl))
				_collectionsPreviewControl.RefreshInstalledState();

			if (_mainDocumentPersistenceEnabled && Visible && Object.ReferenceEquals(e?.Document?.Control, _wabbajackGalleryControl))
				await _wabbajackGalleryControl.EnsureLoadedAsync().ConfigureAwait(true);

			if (_mainDocumentPersistenceEnabled && Visible && e?.Document?.Control != null &&
				Object.ReferenceEquals(e.Document.Control, _witcher3LoadOrderControl) &&
				ViewModel.Witcher3ModSettingsLifecycle != null)
				await _witcher3LoadOrderControl.RefreshOnActivateAsync().ConfigureAwait(true);

			if (_mainDocumentPersistenceEnabled && Visible && e?.Document?.Control != null &&
				Object.ReferenceEquals(e.Document.Control, _managedContentComponentsControl) &&
				ViewModel.GameMode is Games.ManagedContent.IManagedContentComponentProvider)
				await _managedContentComponentsControl.RefreshOnActivateAsync().ConfigureAwait(true);
		}

		/// <summary>Loads the native order after ActiveDocument changes; selection precedes activation in DevExpress.</summary>
		private async void MainTabbedView_DocumentActivated(object sender, DocumentEventArgs e)
		{
			if (_mainDocumentStartupUpdatePending || !_mainDocumentPersistenceEnabled || !Visible || ViewModel?.NativeLoadOrder == null)
				return;
			if (Object.ReferenceEquals(e?.Document?.Control, _loadOrderControl))
				await _loadOrderControl.RefreshOnActivateAsync().ConfigureAwait(true);
		}

		/// <summary>
		/// Finishes document creation and selection before the main window can paint its first document.
		/// </summary>
		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			CompleteMainDocumentStartupUpdate();
		}

		/// <summary>Releases the startup update and applies visual order before selecting Mods.</summary>
		internal static void CompleteMainDocumentStartup(TabbedView view, Control[] defaultOrder, string savedOrder, Control mods)
		{
			view.EndUpdate();
			ApplyMainDocumentOrder(view, defaultOrder, savedOrder);
			view.ActivateDocument(mods);
		}

		/// <summary>
		/// Releases the startup document update lock after all permanent MDI children have been registered.
		/// </summary>
		private void CompleteMainDocumentStartupUpdate()
		{
			if (!_mainDocumentStartupUpdatePending || _mainTabbedView == null)
				return;

			CompleteMainDocumentStartup(_mainTabbedView, GetDefaultMainDocumentOrder(),
				ViewModel == null ? null : GetLayoutSetting(DevExpressDocumentOrderSettingsKey),
				ViewModel == null ? (Control)_gameDiscoveryDocument : (Control)_modManagerControl);
			_mainDocumentStartupUpdatePending = false;
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
