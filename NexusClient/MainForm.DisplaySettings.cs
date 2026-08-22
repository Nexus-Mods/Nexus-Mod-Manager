namespace Nexus.Client
{
	using System;
	using System.Collections.Generic;

	using DevExpress.XtraBars;
	using DevExpress.XtraEditors;

	using Nexus.Client.ModManagement.UI;
	using Nexus.Client.UI;
	using Nexus.Client.Util.Localization;

	public partial class MainForm
	{
		private const string DevExpressDisplayFontSettingsKey = "mainForm.DevExpressDisplay.Font";
		private const string DevExpressDisplayFontSizeSettingsKey = "mainForm.DevExpressDisplay.FontSize";
		private const string DevExpressDisplayDensitySettingsKey = "mainForm.DevExpressDisplay.Density";
		private const string DevExpressDisplayIconStyleSettingsKey = "mainForm.DevExpressDisplay.IconStyle";
		private const string DevExpressDisplayIconSizeSettingsKey = "mainForm.DevExpressDisplay.IconSize";
		private const string DevExpressDisplayIconColorProfileSettingsKey = "mainForm.DevExpressDisplay.IconColorProfile";
		private const string DevExpressDisplayButtonPresentationSettingsKey = "mainForm.DevExpressDisplay.ButtonPresentation";
		private const string LegacyDevExpressDisplayFontSettingsKey = "modManagerDXGrid.Font";
		private const string LegacyDevExpressDisplayFontSizeSettingsKey = "modManagerDXGrid.FontSize";
		private const string LegacyDevExpressDisplayDensitySettingsKey = "modManagerDXGrid.Density";

		private BarSubItem _devExpressDisplayButton;
		private BarSubItem _devExpressDisplayFontMenu;
		private BarSubItem _devExpressDisplayFontSizeMenu;
		private BarSubItem _devExpressDisplayDensityMenu;
		private BarSubItem _devExpressDisplayIconStyleMenu;
		private BarSubItem _devExpressDisplayIconSizeMenu;
		private BarSubItem _devExpressDisplayIconColorProfileMenu;
		private BarSubItem _devExpressDisplayButtonPresentationMenu;
		private BarSubItem _uiLanguageMenu;
		private readonly List<BarButtonItem> _devExpressDisplayFontItems = new List<BarButtonItem>();
		private readonly List<BarButtonItem> _devExpressDisplayFontSizeItems = new List<BarButtonItem>();
		private readonly List<BarButtonItem> _devExpressDisplayDensityItems = new List<BarButtonItem>();
		private readonly List<BarButtonItem> _devExpressDisplayIconStyleItems = new List<BarButtonItem>();
		private readonly List<BarButtonItem> _devExpressDisplayIconSizeItems = new List<BarButtonItem>();
		private readonly List<BarButtonItem> _devExpressDisplayIconColorProfileItems = new List<BarButtonItem>();
		private readonly List<BarButtonItem> _devExpressDisplayButtonPresentationGlobalItems = new List<BarButtonItem>();
		private readonly List<BarButtonItem> _uiLanguageItems = new List<BarButtonItem>();
		private readonly Dictionary<NmmButtonPresentationScope, List<BarButtonItem>> _devExpressDisplayButtonPresentationScopeItems = new Dictionary<NmmButtonPresentationScope, List<BarButtonItem>>();
		private readonly List<DevExpressDisplaySettings> _retiredDevExpressDisplaySettings = new List<DevExpressDisplaySettings>();
		private bool _updatingDevExpressDisplaySelector;
		private string _selectedUiLanguageId;
		private DevExpressDisplaySettings _devExpressDisplaySettings;

		/// <summary>
		/// Creates the DevExpress display selector and restores its persisted values.
		/// </summary>
		/// <param name="viewModel">The main-form view model containing persisted UI settings.</param>
		private void InitializeDevExpressDisplaySelector(MainFormVM viewModel)
		{
			string fontName = DevExpressDisplaySettings.ResolveFontFamily(ReadDevExpressDisplaySetting(viewModel, DevExpressDisplayFontSettingsKey, LegacyDevExpressDisplayFontSettingsKey, DevExpressDisplaySettings.DefaultFontFamily));
			float fontSize = DevExpressDisplaySettings.ParseFontSize(ReadDevExpressDisplaySetting(viewModel, DevExpressDisplayFontSizeSettingsKey, LegacyDevExpressDisplayFontSizeSettingsKey, DevExpressDisplaySettings.FormatFontSize(DevExpressDisplaySettings.DefaultFontSizePt)));
			string density = DevExpressDisplaySettings.ResolveDensity(ReadDevExpressDisplaySetting(viewModel, DevExpressDisplayDensitySettingsKey, LegacyDevExpressDisplayDensitySettingsKey, DevExpressDisplaySettings.DefaultDensity));
			NmmIconStyle iconStyle = DevExpressDisplaySettings.ResolveIconStyle(ReadDevExpressDisplaySetting(viewModel, DevExpressDisplayIconStyleSettingsKey, null, DevExpressDisplaySettings.DefaultIconStyle.ToString()));
			int iconSize = DevExpressDisplaySettings.ParseIconSize(ReadDevExpressDisplaySetting(viewModel, DevExpressDisplayIconSizeSettingsKey, null, DevExpressDisplaySettings.FormatIconSize(DevExpressDisplaySettings.DefaultIconSize)));
			NmmIconColorProfile iconColorProfile = DevExpressDisplaySettings.ResolveIconColorProfile(ReadDevExpressDisplaySetting(viewModel, DevExpressDisplayIconColorProfileSettingsKey, null, DevExpressDisplaySettings.DefaultIconColorProfile.ToString()));
			NmmButtonPresentationProfile buttonPresentationProfile = DevExpressDisplaySettings.ResolveButtonPresentationProfile(
				ReadDevExpressDisplaySetting(viewModel, DevExpressDisplayButtonPresentationSettingsKey, null, DevExpressDisplaySettings.DefaultButtonPresentation.ToString()),
				scope => ReadDevExpressDisplaySetting(viewModel, DevExpressDisplaySettings.GetButtonPresentationSettingsKey(scope), null, DevExpressDisplaySettings.DefaultButtonPresentation.ToString()));

			_devExpressDisplaySettings = new DevExpressDisplaySettings(fontName, fontSize, density, iconStyle, iconSize, iconColorProfile, buttonPresentationProfile);
			NmmIconProvider.ApplySettings(iconStyle, iconSize, iconColorProfile, buttonPresentationProfile);
			_devExpressDisplayButton = new BarSubItem(barManagerMain, L("MainForm.DisplayOptions.Caption", "Display Option"))
			{
				Hint = L("MainForm.DisplayOptions.Hint", "Font, density and icon presentation for the manager UI")
			};
			_devExpressDisplayFontMenu = new BarSubItem(barManagerMain, L("MainForm.DisplayOptions.Font", "Font"));
			_devExpressDisplayFontSizeMenu = new BarSubItem(barManagerMain, L("MainForm.DisplayOptions.Size", "Size"));
			_devExpressDisplayDensityMenu = new BarSubItem(barManagerMain, L("MainForm.DisplayOptions.Density", "Density"));
			_devExpressDisplayIconStyleMenu = new BarSubItem(barManagerMain, L("MainForm.DisplayOptions.IconStyle", "Icon Style"));
			_devExpressDisplayIconSizeMenu = new BarSubItem(barManagerMain, L("MainForm.DisplayOptions.IconSize", "Icon Size"));
			_devExpressDisplayIconColorProfileMenu = new BarSubItem(barManagerMain, L("MainForm.DisplayOptions.ColorProfile", "Color Profile"));
			_devExpressDisplayButtonPresentationMenu = new BarSubItem(barManagerMain, L("MainForm.DisplayOptions.ButtonPresentation", "Button Presentation"));
			_uiLanguageMenu = new BarSubItem(barManagerMain, L("MainForm.DisplayOptions.UILanguage", "UI Language"));

			CreateDevExpressDisplayChoiceItems(_devExpressDisplayFontMenu, _devExpressDisplayFontItems, DevExpressDisplaySettings.FontChoices, DevExpressDisplayFont_ItemClick);
			CreateDevExpressDisplayChoiceItems(_devExpressDisplayFontSizeMenu, _devExpressDisplayFontSizeItems, DevExpressDisplaySettings.FontSizeChoices, DevExpressDisplayFontSize_ItemClick);
			CreateDevExpressDisplayChoiceItems(_devExpressDisplayDensityMenu, _devExpressDisplayDensityItems, DevExpressDisplaySettings.DensityChoices, DevExpressDisplayDensity_ItemClick, GetDensityCaption);
			CreateDevExpressDisplayChoiceItems(_devExpressDisplayIconStyleMenu, _devExpressDisplayIconStyleItems, DevExpressDisplaySettings.IconStyleChoices, DevExpressDisplayIconStyle_ItemClick, GetIconStyleCaption);
			CreateDevExpressDisplayChoiceItems(_devExpressDisplayIconSizeMenu, _devExpressDisplayIconSizeItems, DevExpressDisplaySettings.IconSizeChoices, DevExpressDisplayIconSize_ItemClick);
			CreateDevExpressDisplayChoiceItems(_devExpressDisplayIconColorProfileMenu, _devExpressDisplayIconColorProfileItems, DevExpressDisplaySettings.IconColorProfileChoices, DevExpressDisplayIconColorProfile_ItemClick, GetColorProfileCaption);
			CreateDevExpressButtonPresentationMenus();
			CreateUiLanguageMenu();

			_devExpressDisplayButton.AddItem(_devExpressDisplayFontMenu);
			_devExpressDisplayButton.AddItem(_devExpressDisplayFontSizeMenu);
			_devExpressDisplayButton.AddItem(_devExpressDisplayDensityMenu);
			_devExpressDisplayButton.AddItem(_devExpressDisplayIconStyleMenu).BeginGroup = true;
			_devExpressDisplayButton.AddItem(_devExpressDisplayIconSizeMenu);
			_devExpressDisplayButton.AddItem(_devExpressDisplayButtonPresentationMenu);
			_devExpressDisplayButton.AddItem(_devExpressDisplayIconColorProfileMenu);
			_devExpressDisplayButton.AddItem(_uiLanguageMenu).BeginGroup = true;

			BarButtonItem resetButton = new BarButtonItem(barManagerMain, L("Common.Action.Reset", "Reset"));
			resetButton.ItemClick += (sender, args) => ResetDevExpressDisplaySettings();
			NmmIconProvider.Bind(_devExpressDisplayButton, NmmIconAction.DisplayOptions);
			NmmIconProvider.Bind(resetButton, NmmIconAction.Reset);
			_devExpressDisplayButton.AddItem(resetButton).BeginGroup = true;

			UpdateDevExpressDisplaySelector();
			Disposed += MainFormDisplaySettings_Disposed;
		}

		/// <summary>
		/// Reads a current or legacy display setting and falls back when neither contains a value.
		/// </summary>
		/// <param name="viewModel">The main-form view model.</param>
		/// <param name="key">The current settings key.</param>
		/// <param name="legacyKey">The legacy settings key.</param>
		/// <param name="defaultValue">The fallback value.</param>
		/// <returns>The persisted or fallback value.</returns>
		private static string ReadDevExpressDisplaySetting(MainFormVM viewModel, string key, string legacyKey, string defaultValue)
		{
			if (viewModel?.EnvironmentInfo?.Settings?.DockPanelLayouts == null)
				return defaultValue;

			if (viewModel.EnvironmentInfo.Settings.DockPanelLayouts.ContainsKey(key))
			{
				string value = viewModel.EnvironmentInfo.Settings.DockPanelLayouts[key];
				if (!String.IsNullOrWhiteSpace(value))
					return value;
			}

			if (!String.IsNullOrWhiteSpace(legacyKey) && viewModel.EnvironmentInfo.Settings.DockPanelLayouts.ContainsKey(legacyKey))
			{
				string legacyValue = viewModel.EnvironmentInfo.Settings.DockPanelLayouts[legacyKey];
				if (!String.IsNullOrWhiteSpace(legacyValue))
					return legacyValue;
			}

			return defaultValue;
		}

		/// <summary>
		/// Populates one DevExpress display submenu with checkable choices.
		/// </summary>
		/// <param name="menu">The submenu receiving the choices.</param>
		/// <param name="target">The list used to update checked state later.</param>
		/// <param name="choices">The available display choices.</param>
		/// <param name="handler">The choice click handler.</param>
		private void CreateDevExpressDisplayChoiceItems(BarSubItem menu, ICollection<BarButtonItem> target, IEnumerable<string> choices, ItemClickEventHandler handler, Func<string, string> captionResolver = null)
		{
			foreach (string choice in choices)
			{
				string caption = captionResolver == null ? choice : captionResolver(choice);
				BarButtonItem item = new BarButtonItem(barManagerMain, caption)
				{
					ButtonStyle = BarButtonStyle.Check,
					Tag = choice
				};
				item.ItemClick += handler;
				menu.AddItem(item);
				target.Add(item);
			}
		}

		private static string GetDensityCaption(string choice)
		{
			switch (choice)
			{
				case "Compact": return L("MainForm.DisplayOptions.Density.Compact", "Compact");
				case "Comfortable": return L("MainForm.DisplayOptions.Density.Comfortable", "Comfortable");
				case "Spacious": return L("MainForm.DisplayOptions.Density.Spacious", "Spacious");
				default: return choice;
			}
		}

		private static string GetIconStyleCaption(string choice)
		{
			switch (choice)
			{
				case "Minimal": return L("MainForm.DisplayOptions.IconStyle.Minimal", "Minimal");
				case "Classic": return L("MainForm.DisplayOptions.IconStyle.Classic", "Classic");
				default: return choice;
			}
		}

		private static string GetColorProfileCaption(string choice)
		{
			switch (choice)
			{
				case "Base": return L("MainForm.DisplayOptions.ColorProfile.Base", "Base");
				case "Deuteranopia": return L("MainForm.DisplayOptions.ColorProfile.Deuteranopia", "Deuteranopia");
				case "Protanopia": return L("MainForm.DisplayOptions.ColorProfile.Protanopia", "Protanopia");
				case "Tritanopia": return L("MainForm.DisplayOptions.ColorProfile.Tritanopia", "Tritanopia");
				case "High Contrast": return L("MainForm.DisplayOptions.ColorProfile.HighContrast", "High Contrast");
				default: return choice;
			}
		}

		private static string GetButtonPresentationCaption(string choice)
		{
			switch (choice)
			{
				case "Text only": return L("MainForm.DisplayOptions.ButtonPresentation.TextOnly", "Text only");
				case "Icons only": return L("MainForm.DisplayOptions.ButtonPresentation.IconsOnly", "Icons only");
				case "Text + Icons": return L("MainForm.DisplayOptions.ButtonPresentation.TextAndIcons", "Text + Icons");
				case "Custom": return L("MainForm.DisplayOptions.ButtonPresentation.Custom", "Custom");
				default: return choice;
			}
		}

		/// <summary>
		/// Populates the UI Language submenu from the packs discovered at startup. No
		/// filesystem access is performed here; LanguageManager keeps the discovery
		/// result in memory for the lifetime of the process.
		/// </summary>
		private void CreateUiLanguageMenu()
		{
			_selectedUiLanguageId = LanguageManager.CurrentLanguage.Id;
			List<LanguagePackInfo> languages = new List<LanguagePackInfo>(LanguageManager.AvailableLanguages);
			languages.Sort(CompareUiLanguages);

			foreach (LanguagePackInfo language in languages)
			{
				BarButtonItem item = new BarButtonItem(barManagerMain, language.Name)
				{
					ButtonStyle = BarButtonStyle.Check,
					Tag = language.Id
				};
				item.ItemClick += UiLanguage_ItemClick;
				_uiLanguageMenu.AddItem(item);
				_uiLanguageItems.Add(item);
			}

			UpdateUiLanguageChoiceChecks();
		}

		private static int CompareUiLanguages(LanguagePackInfo left, LanguagePackInfo right)
		{
			if (left.IsBuiltIn != right.IsBuiltIn)
				return left.IsBuiltIn ? -1 : 1;

			return StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name);
		}

		private void UiLanguage_ItemClick(object sender, ItemClickEventArgs e)
		{
			string languageId = Convert.ToString(e.Item.Tag);
			if (String.IsNullOrWhiteSpace(languageId))
			{
				UpdateUiLanguageChoiceChecks();
				return;
			}

			string previousLanguageId = Properties.Settings.Default.UILanguage;
			if (String.Equals(_selectedUiLanguageId, languageId, StringComparison.OrdinalIgnoreCase) &&
				String.Equals(previousLanguageId, languageId, StringComparison.OrdinalIgnoreCase))
			{
				UpdateUiLanguageChoiceChecks();
				return;
			}

			try
			{
				Properties.Settings.Default.UILanguage = languageId;
				Properties.Settings.Default.Save();
			}
			catch (Exception ex)
			{
				Properties.Settings.Default.UILanguage = previousLanguageId;
				System.Diagnostics.Trace.TraceWarning("Unable to save UI language '{0}'. {1}", languageId, ex.Message);
				UpdateUiLanguageChoiceChecks();
				XtraMessageBox.Show(
					this,
					LanguageManager.Get("MainForm.DisplayOptions.UILanguage.SaveFailed", "Nexus Mod Manager could not save the selected UI language."),
					LanguageManager.Get("MainForm.DisplayOptions.UILanguage.Title", "UI Language"),
					System.Windows.Forms.MessageBoxButtons.OK,
					System.Windows.Forms.MessageBoxIcon.Warning);
				return;
			}

			_selectedUiLanguageId = languageId;
			UpdateUiLanguageChoiceChecks();

			if (!String.Equals(languageId, LanguageManager.CurrentLanguage.Id, StringComparison.OrdinalIgnoreCase))
			{
				XtraMessageBox.Show(
					this,
					LanguageManager.Get("MainForm.DisplayOptions.UILanguage.RestartRequired", "The selected UI language will be applied the next time Nexus Mod Manager starts."),
					LanguageManager.Get("MainForm.DisplayOptions.UILanguage.Title", "UI Language"),
					System.Windows.Forms.MessageBoxButtons.OK,
					System.Windows.Forms.MessageBoxIcon.Information);
			}
		}

		private void UpdateUiLanguageChoiceChecks()
		{
			foreach (BarButtonItem item in _uiLanguageItems)
				item.Down = String.Equals(Convert.ToString(item.Tag), _selectedUiLanguageId, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Creates the scoped Button Presentation selector. Only the explicitly listed
		/// toolbar surfaces participate; dialogs, popups and ordinary controls do not.
		/// </summary>
		private void CreateDevExpressButtonPresentationMenus()
		{
			BarSubItem globalMenu = new BarSubItem(barManagerMain, L("MainForm.DisplayOptions.ButtonPresentation.Global", "Global"));
			CreateButtonPresentationGlobalItems(globalMenu);
			_devExpressDisplayButtonPresentationMenu.AddItem(globalMenu);

			AddButtonPresentationScopeMenu(L("MainForm.DisplayOptions.ButtonPresentation.MainBar", "Main Bar"), NmmButtonPresentationScope.MainBar);
			AddButtonPresentationScopeMenu(L("MainForm.Tabs.Plugins", "Plugins"), NmmButtonPresentationScope.Plugins);
			AddButtonPresentationScopeMenu(L("MainForm.Tabs.Mods", "Mods"), NmmButtonPresentationScope.Mods);
			AddButtonPresentationScopeMenu(L("MainForm.Tabs.Categories", "Categories"), NmmButtonPresentationScope.Categories);
			AddButtonPresentationScopeMenu(L("MainForm.Tabs.FileManager", "File Manager"), NmmButtonPresentationScope.FileManager);
			AddButtonPresentationScopeMenu(L("MainForm.Dock.DownloadManager", "Download Manager"), NmmButtonPresentationScope.DownloadManager);
			AddButtonPresentationScopeMenu(L("MainForm.Dock.ModActivationQueue", "Mod Activation Queue"), NmmButtonPresentationScope.ModActivationQueue);
		}

		private void CreateButtonPresentationGlobalItems(BarSubItem menu)
		{
			foreach (string choice in DevExpressDisplaySettings.ButtonPresentationChoices)
			{
				BarButtonItem item = new BarButtonItem(barManagerMain, GetButtonPresentationCaption(choice))
				{
					ButtonStyle = BarButtonStyle.Check,
					Tag = choice
				};
				item.ItemClick += DevExpressDisplayButtonPresentationGlobal_ItemClick;
				menu.AddItem(item);
				_devExpressDisplayButtonPresentationGlobalItems.Add(item);
			}

			BarButtonItem customItem = new BarButtonItem(barManagerMain, L("MainForm.DisplayOptions.ButtonPresentation.Custom", "Custom"))
			{
				ButtonStyle = BarButtonStyle.Check,
				Tag = "Custom",
				Enabled = false,
				Hint = L("MainForm.DisplayOptions.ButtonPresentation.Custom.Hint", "Uses the individual toolbar settings below.")
			};
			menu.AddItem(customItem).BeginGroup = true;
			_devExpressDisplayButtonPresentationGlobalItems.Add(customItem);
		}

		private void AddButtonPresentationScopeMenu(string caption, NmmButtonPresentationScope scope)
		{
			BarSubItem menu = new BarSubItem(barManagerMain, caption);
			List<BarButtonItem> items = new List<BarButtonItem>();
			foreach (string choice in DevExpressDisplaySettings.ButtonPresentationChoices)
			{
				BarButtonItem item = new BarButtonItem(barManagerMain, GetButtonPresentationCaption(choice))
				{
					ButtonStyle = BarButtonStyle.Check,
					Tag = new ButtonPresentationScopeChoice(scope, DevExpressDisplaySettings.ResolveButtonPresentation(choice))
				};
				item.ItemClick += DevExpressDisplayButtonPresentationScope_ItemClick;
				menu.AddItem(item);
				items.Add(item);
			}

			_devExpressDisplayButtonPresentationScopeItems[scope] = items;
			_devExpressDisplayButtonPresentationMenu.AddItem(menu);
		}

		/// <summary>
		/// Applies a selected font-family choice.
		/// </summary>
		private void DevExpressDisplayFont_ItemClick(object sender, ItemClickEventArgs e)
		{
			if (_updatingDevExpressDisplaySelector)
				return;

			SetDevExpressDisplaySettings(Convert.ToString(e.Item.Tag), _devExpressDisplaySettings.FontSizePt, _devExpressDisplaySettings.Density, _devExpressDisplaySettings.IconStyle, _devExpressDisplaySettings.IconSize, _devExpressDisplaySettings.IconColorProfile, _devExpressDisplaySettings.ButtonPresentationProfile, true);
		}

		/// <summary>
		/// Applies a selected font-size choice.
		/// </summary>
		private void DevExpressDisplayFontSize_ItemClick(object sender, ItemClickEventArgs e)
		{
			if (_updatingDevExpressDisplaySelector)
				return;

			SetDevExpressDisplaySettings(_devExpressDisplaySettings.FontFamilyName, DevExpressDisplaySettings.ParseFontSize(Convert.ToString(e.Item.Tag)), _devExpressDisplaySettings.Density, _devExpressDisplaySettings.IconStyle, _devExpressDisplaySettings.IconSize, _devExpressDisplaySettings.IconColorProfile, _devExpressDisplaySettings.ButtonPresentationProfile, true);
		}

		/// <summary>
		/// Applies a selected display-density choice.
		/// </summary>
		private void DevExpressDisplayDensity_ItemClick(object sender, ItemClickEventArgs e)
		{
			if (_updatingDevExpressDisplaySelector)
				return;

			SetDevExpressDisplaySettings(_devExpressDisplaySettings.FontFamilyName, _devExpressDisplaySettings.FontSizePt, Convert.ToString(e.Item.Tag), _devExpressDisplaySettings.IconStyle, _devExpressDisplaySettings.IconSize, _devExpressDisplaySettings.IconColorProfile, _devExpressDisplaySettings.ButtonPresentationProfile, true);
		}

		private void DevExpressDisplayIconStyle_ItemClick(object sender, ItemClickEventArgs e)
		{
			if (_updatingDevExpressDisplaySelector)
				return;

			SetDevExpressDisplaySettings(_devExpressDisplaySettings.FontFamilyName, _devExpressDisplaySettings.FontSizePt, _devExpressDisplaySettings.Density, DevExpressDisplaySettings.ResolveIconStyle(Convert.ToString(e.Item.Tag)), _devExpressDisplaySettings.IconSize, _devExpressDisplaySettings.IconColorProfile, _devExpressDisplaySettings.ButtonPresentationProfile, true);
		}

		private void DevExpressDisplayIconSize_ItemClick(object sender, ItemClickEventArgs e)
		{
			if (_updatingDevExpressDisplaySelector)
				return;

			SetDevExpressDisplaySettings(_devExpressDisplaySettings.FontFamilyName, _devExpressDisplaySettings.FontSizePt, _devExpressDisplaySettings.Density, _devExpressDisplaySettings.IconStyle, DevExpressDisplaySettings.ParseIconSize(Convert.ToString(e.Item.Tag)), _devExpressDisplaySettings.IconColorProfile, _devExpressDisplaySettings.ButtonPresentationProfile, true);
		}

		private void DevExpressDisplayIconColorProfile_ItemClick(object sender, ItemClickEventArgs e)
		{
			if (_updatingDevExpressDisplaySelector)
				return;

			SetDevExpressDisplaySettings(_devExpressDisplaySettings.FontFamilyName, _devExpressDisplaySettings.FontSizePt, _devExpressDisplaySettings.Density, _devExpressDisplaySettings.IconStyle, _devExpressDisplaySettings.IconSize, DevExpressDisplaySettings.ResolveIconColorProfile(Convert.ToString(e.Item.Tag)), _devExpressDisplaySettings.ButtonPresentationProfile, true);
		}

		private void DevExpressDisplayButtonPresentationGlobal_ItemClick(object sender, ItemClickEventArgs e)
		{
			if (_updatingDevExpressDisplaySelector)
				return;

			string choice = Convert.ToString(e.Item.Tag);
			if (String.Equals(choice, "Custom", StringComparison.OrdinalIgnoreCase))
				return;

			NmmButtonPresentationProfile profile = _devExpressDisplaySettings.ButtonPresentationProfile.WithGlobal(DevExpressDisplaySettings.ResolveButtonPresentation(choice));
			SetDevExpressDisplaySettings(_devExpressDisplaySettings.FontFamilyName, _devExpressDisplaySettings.FontSizePt, _devExpressDisplaySettings.Density, _devExpressDisplaySettings.IconStyle, _devExpressDisplaySettings.IconSize, _devExpressDisplaySettings.IconColorProfile, profile, true);
		}

		private void DevExpressDisplayButtonPresentationScope_ItemClick(object sender, ItemClickEventArgs e)
		{
			if (_updatingDevExpressDisplaySelector)
				return;

			ButtonPresentationScopeChoice choice = e.Item.Tag as ButtonPresentationScopeChoice;
			if (choice == null)
				return;

			NmmButtonPresentationProfile profile = _devExpressDisplaySettings.ButtonPresentationProfile.WithScope(choice.Scope, choice.Presentation);
			SetDevExpressDisplaySettings(_devExpressDisplaySettings.FontFamilyName, _devExpressDisplaySettings.FontSizePt, _devExpressDisplaySettings.Density, _devExpressDisplaySettings.IconStyle, _devExpressDisplaySettings.IconSize, _devExpressDisplaySettings.IconColorProfile, profile, true);
		}

		/// <summary>
		/// Restores the global DevExpress display defaults.
		/// </summary>
		private void ResetDevExpressDisplaySettings()
		{
			SetDevExpressDisplaySettings(DevExpressDisplaySettings.DefaultFontFamily, DevExpressDisplaySettings.DefaultFontSizePt, DevExpressDisplaySettings.DefaultDensity, DevExpressDisplaySettings.DefaultIconStyle, DevExpressDisplaySettings.DefaultIconSize, DevExpressDisplaySettings.DefaultIconColorProfile, DevExpressDisplaySettings.DefaultButtonPresentationProfile, true);
		}

		/// <summary>
		/// Replaces the current display settings, applies them to active surfaces and optionally persists them.
		/// </summary>
		private void SetDevExpressDisplaySettings(string fontName, float fontSize, string density, NmmIconStyle iconStyle, int iconSize, NmmIconColorProfile iconColorProfile, NmmButtonPresentationProfile buttonPresentationProfile, bool save)
		{
			DevExpressDisplaySettings previousSettings = _devExpressDisplaySettings;
			DevExpressDisplaySettings newSettings = new DevExpressDisplaySettings(fontName, fontSize, density, iconStyle, iconSize, iconColorProfile, buttonPresentationProfile);

			_devExpressDisplaySettings = newSettings;
			UpdateDevExpressDisplaySelector();
			ApplyDevExpressDisplaySettingsToSurfaces();

			if (save && ViewModel?.EnvironmentInfo?.Settings?.DockPanelLayouts != null)
			{
				SaveDevExpressDisplaySetting(DevExpressDisplayFontSettingsKey, LegacyDevExpressDisplayFontSettingsKey, newSettings.FontFamilyName);
				SaveDevExpressDisplaySetting(DevExpressDisplayFontSizeSettingsKey, LegacyDevExpressDisplayFontSizeSettingsKey, DevExpressDisplaySettings.FormatFontSize(newSettings.FontSizePt));
				SaveDevExpressDisplaySetting(DevExpressDisplayDensitySettingsKey, LegacyDevExpressDisplayDensitySettingsKey, newSettings.Density);
				ViewModel.EnvironmentInfo.Settings.DockPanelLayouts[DevExpressDisplayIconStyleSettingsKey] = newSettings.IconStyle.ToString();
				ViewModel.EnvironmentInfo.Settings.DockPanelLayouts[DevExpressDisplayIconSizeSettingsKey] = DevExpressDisplaySettings.FormatIconSize(newSettings.IconSize);
				ViewModel.EnvironmentInfo.Settings.DockPanelLayouts[DevExpressDisplayIconColorProfileSettingsKey] = newSettings.IconColorProfile.ToString();
				ViewModel.EnvironmentInfo.Settings.DockPanelLayouts[DevExpressDisplayButtonPresentationSettingsKey] = DevExpressDisplaySettings.FormatButtonPresentationGlobal(newSettings.ButtonPresentationProfile);
				foreach (NmmButtonPresentationScope scope in Enum.GetValues(typeof(NmmButtonPresentationScope)))
					ViewModel.EnvironmentInfo.Settings.DockPanelLayouts[DevExpressDisplaySettings.GetButtonPresentationSettingsKey(scope)] = newSettings.ButtonPresentationProfile.Get(scope).ToString();
				ViewModel.EnvironmentInfo.Settings.Save();
			}

			// DevExpress appearance objects retain the assigned Font instance and can
			// use it again during a later skin change. Keep replaced settings alive
			// until the form and its child controls have finished disposing.
			if (previousSettings != null)
				_retiredDevExpressDisplaySettings.Add(previousSettings);
		}

		/// <summary>
		/// Saves both the current and legacy display-setting keys for backwards compatibility.
		/// </summary>
		private void SaveDevExpressDisplaySetting(string key, string legacyKey, string value)
		{
			ViewModel.EnvironmentInfo.Settings.DockPanelLayouts[key] = value;
			ViewModel.EnvironmentInfo.Settings.DockPanelLayouts[legacyKey] = value;
		}

		/// <summary>
		/// Synchronizes the checked menu choices and selector hint with the active settings.
		/// </summary>
		private void UpdateDevExpressDisplaySelector()
		{
			if (_devExpressDisplaySettings == null || _devExpressDisplayButton == null)
				return;

			_updatingDevExpressDisplaySelector = true;
			try
			{
				UpdateDevExpressDisplayChoiceChecks(_devExpressDisplayFontItems, _devExpressDisplaySettings.FontFamilyName);
				UpdateDevExpressDisplayChoiceChecks(_devExpressDisplayFontSizeItems, DevExpressDisplaySettings.FormatFontSize(_devExpressDisplaySettings.FontSizePt));
				UpdateDevExpressDisplayChoiceChecks(_devExpressDisplayDensityItems, _devExpressDisplaySettings.Density);
				UpdateDevExpressDisplayChoiceChecks(_devExpressDisplayIconStyleItems, _devExpressDisplaySettings.IconStyle.ToString());
				UpdateDevExpressDisplayChoiceChecks(_devExpressDisplayIconSizeItems, DevExpressDisplaySettings.FormatIconSize(_devExpressDisplaySettings.IconSize));
				UpdateDevExpressDisplayChoiceChecks(_devExpressDisplayIconColorProfileItems, DevExpressDisplaySettings.FormatIconColorProfile(_devExpressDisplaySettings.IconColorProfile));
				UpdateDevExpressDisplayChoiceChecks(_devExpressDisplayButtonPresentationGlobalItems, DevExpressDisplaySettings.FormatButtonPresentationGlobal(_devExpressDisplaySettings.ButtonPresentationProfile));
				foreach (KeyValuePair<NmmButtonPresentationScope, List<BarButtonItem>> pair in _devExpressDisplayButtonPresentationScopeItems)
					UpdateButtonPresentationScopeChecks(pair.Value, _devExpressDisplaySettings.ButtonPresentationProfile.Get(pair.Key));
			}
			finally
			{
				_updatingDevExpressDisplaySelector = false;
			}

			_devExpressDisplayButton.Hint = LanguageManager.Format("MainForm.DisplayOptions.Summary", "{0}, {1}, {2} | {3} icons, {4}, {5}, {6} profile", _devExpressDisplaySettings.FontFamilyName, DevExpressDisplaySettings.FormatFontSize(_devExpressDisplaySettings.FontSizePt), GetDensityCaption(_devExpressDisplaySettings.Density), GetIconStyleCaption(_devExpressDisplaySettings.IconStyle.ToString()), DevExpressDisplaySettings.FormatIconSize(_devExpressDisplaySettings.IconSize), GetButtonPresentationCaption(DevExpressDisplaySettings.FormatButtonPresentationGlobal(_devExpressDisplaySettings.ButtonPresentationProfile)), GetColorProfileCaption(DevExpressDisplaySettings.FormatIconColorProfile(_devExpressDisplaySettings.IconColorProfile)));
		}

		/// <summary>
		/// Updates the checked state of a display-selector choice group.
		/// </summary>
		private static void UpdateDevExpressDisplayChoiceChecks(IEnumerable<BarButtonItem> items, string selectedValue)
		{
			foreach (BarButtonItem item in items)
				item.Down = String.Equals(Convert.ToString(item.Tag), selectedValue, StringComparison.OrdinalIgnoreCase);
		}

		private static void UpdateButtonPresentationScopeChecks(IEnumerable<BarButtonItem> items, NmmButtonPresentation selectedValue)
		{
			foreach (BarButtonItem item in items)
			{
				ButtonPresentationScopeChoice choice = item.Tag as ButtonPresentationScopeChoice;
				item.Down = choice != null && choice.Presentation == selectedValue;
			}
		}

		/// <summary>
		/// Applies the current display settings to all active DevExpress surfaces already created by the main form.
		/// </summary>
		private void ApplyDevExpressDisplaySettingsToSurfaces()
		{
			if (_devExpressDisplaySettings == null)
				return;

			NmmIconProvider.ApplySettings(_devExpressDisplaySettings.IconStyle, _devExpressDisplaySettings.IconSize, _devExpressDisplaySettings.IconColorProfile, _devExpressDisplaySettings.ButtonPresentationProfile);
			DevExpressDisplaySettingsApplier.ApplyToControlTree(this, _devExpressDisplaySettings);
			DevExpressDisplaySettingsApplier.ApplyToBarManager(barManagerMain, _devExpressDisplaySettings);

			ModManagerDXControl modManagerDX = _modManagerControl as ModManagerDXControl;
			modManagerDX?.ApplyDisplaySettings(_devExpressDisplaySettings);
			_pluginManagerControl?.ApplyDisplaySettings(_devExpressDisplaySettings);
			_categoryManagerControl?.ApplyDisplaySettings(_devExpressDisplaySettings);
			_fileManagerControl?.ApplyDisplaySettings(_devExpressDisplaySettings);
		}

		private sealed class ButtonPresentationScopeChoice
		{
			internal ButtonPresentationScopeChoice(NmmButtonPresentationScope scope, NmmButtonPresentation presentation)
			{
				Scope = scope;
				Presentation = presentation;
			}

			internal NmmButtonPresentationScope Scope { get; private set; }
			internal NmmButtonPresentation Presentation { get; private set; }
		}

		/// <summary>
		/// Releases the font resources owned by the current display settings.
		/// </summary>
		private void MainFormDisplaySettings_Disposed(object sender, EventArgs e)
		{
			_devExpressDisplaySettings?.Dispose();
			_devExpressDisplaySettings = null;

			foreach (DevExpressDisplaySettings retiredSettings in _retiredDevExpressDisplaySettings)
				retiredSettings.Dispose();

			_retiredDevExpressDisplaySettings.Clear();
		}
	}
}
