namespace Nexus.Client
{
	using System;
	using System.Collections.Generic;

	using DevExpress.XtraBars;

	using Nexus.Client.ModManagement.UI;
	using Nexus.Client.UI;

	public partial class MainForm
	{
		private const string DevExpressDisplayFontSettingsKey = "mainForm.DevExpressDisplay.Font";
		private const string DevExpressDisplayFontSizeSettingsKey = "mainForm.DevExpressDisplay.FontSize";
		private const string DevExpressDisplayDensitySettingsKey = "mainForm.DevExpressDisplay.Density";
		private const string LegacyDevExpressDisplayFontSettingsKey = "modManagerDXGrid.Font";
		private const string LegacyDevExpressDisplayFontSizeSettingsKey = "modManagerDXGrid.FontSize";
		private const string LegacyDevExpressDisplayDensitySettingsKey = "modManagerDXGrid.Density";

		private BarSubItem _devExpressDisplayButton;
		private BarSubItem _devExpressDisplayFontMenu;
		private BarSubItem _devExpressDisplayFontSizeMenu;
		private BarSubItem _devExpressDisplayDensityMenu;
		private readonly List<BarButtonItem> _devExpressDisplayFontItems = new List<BarButtonItem>();
		private readonly List<BarButtonItem> _devExpressDisplayFontSizeItems = new List<BarButtonItem>();
		private readonly List<BarButtonItem> _devExpressDisplayDensityItems = new List<BarButtonItem>();
		private readonly List<DevExpressDisplaySettings> _retiredDevExpressDisplaySettings = new List<DevExpressDisplaySettings>();
		private bool _updatingDevExpressDisplaySelector;
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

			_devExpressDisplaySettings = new DevExpressDisplaySettings(fontName, fontSize, density);
			_devExpressDisplayButton = new BarSubItem(barManagerMain, "Aa Display")
			{
				Hint = "Font, size and density for the manager UI"
			};
			_devExpressDisplayFontMenu = new BarSubItem(barManagerMain, "Font");
			_devExpressDisplayFontSizeMenu = new BarSubItem(barManagerMain, "Size");
			_devExpressDisplayDensityMenu = new BarSubItem(barManagerMain, "Density");

			CreateDevExpressDisplayChoiceItems(_devExpressDisplayFontMenu, _devExpressDisplayFontItems, DevExpressDisplaySettings.FontChoices, DevExpressDisplayFont_ItemClick);
			CreateDevExpressDisplayChoiceItems(_devExpressDisplayFontSizeMenu, _devExpressDisplayFontSizeItems, DevExpressDisplaySettings.FontSizeChoices, DevExpressDisplayFontSize_ItemClick);
			CreateDevExpressDisplayChoiceItems(_devExpressDisplayDensityMenu, _devExpressDisplayDensityItems, DevExpressDisplaySettings.DensityChoices, DevExpressDisplayDensity_ItemClick);

			_devExpressDisplayButton.AddItem(_devExpressDisplayFontMenu);
			_devExpressDisplayButton.AddItem(_devExpressDisplayFontSizeMenu);
			_devExpressDisplayButton.AddItem(_devExpressDisplayDensityMenu);

			BarButtonItem resetButton = new BarButtonItem(barManagerMain, "Reset");
			resetButton.ItemClick += (sender, args) => ResetDevExpressDisplaySettings();
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

			if (viewModel.EnvironmentInfo.Settings.DockPanelLayouts.ContainsKey(legacyKey))
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
		private void CreateDevExpressDisplayChoiceItems(BarSubItem menu, ICollection<BarButtonItem> target, IEnumerable<string> choices, ItemClickEventHandler handler)
		{
			foreach (string choice in choices)
			{
				BarButtonItem item = new BarButtonItem(barManagerMain, choice)
				{
					ButtonStyle = BarButtonStyle.Check,
					Tag = choice
				};
				item.ItemClick += handler;
				menu.AddItem(item);
				target.Add(item);
			}
		}

		/// <summary>
		/// Applies a selected font-family choice.
		/// </summary>
		private void DevExpressDisplayFont_ItemClick(object sender, ItemClickEventArgs e)
		{
			if (_updatingDevExpressDisplaySelector)
				return;

			SetDevExpressDisplaySettings(Convert.ToString(e.Item.Tag), _devExpressDisplaySettings.FontSizePt, _devExpressDisplaySettings.Density, true);
		}

		/// <summary>
		/// Applies a selected font-size choice.
		/// </summary>
		private void DevExpressDisplayFontSize_ItemClick(object sender, ItemClickEventArgs e)
		{
			if (_updatingDevExpressDisplaySelector)
				return;

			SetDevExpressDisplaySettings(_devExpressDisplaySettings.FontFamilyName, DevExpressDisplaySettings.ParseFontSize(Convert.ToString(e.Item.Tag)), _devExpressDisplaySettings.Density, true);
		}

		/// <summary>
		/// Applies a selected display-density choice.
		/// </summary>
		private void DevExpressDisplayDensity_ItemClick(object sender, ItemClickEventArgs e)
		{
			if (_updatingDevExpressDisplaySelector)
				return;

			SetDevExpressDisplaySettings(_devExpressDisplaySettings.FontFamilyName, _devExpressDisplaySettings.FontSizePt, Convert.ToString(e.Item.Tag), true);
		}

		/// <summary>
		/// Restores the global DevExpress display defaults.
		/// </summary>
		private void ResetDevExpressDisplaySettings()
		{
			SetDevExpressDisplaySettings(DevExpressDisplaySettings.DefaultFontFamily, DevExpressDisplaySettings.DefaultFontSizePt, DevExpressDisplaySettings.DefaultDensity, true);
		}

		/// <summary>
		/// Replaces the current display settings, applies them to active surfaces and optionally persists them.
		/// </summary>
		private void SetDevExpressDisplaySettings(string fontName, float fontSize, string density, bool save)
		{
			DevExpressDisplaySettings previousSettings = _devExpressDisplaySettings;
			DevExpressDisplaySettings newSettings = new DevExpressDisplaySettings(fontName, fontSize, density);

			_devExpressDisplaySettings = newSettings;
			UpdateDevExpressDisplaySelector();
			ApplyDevExpressDisplaySettingsToSurfaces();

			if (save && ViewModel?.EnvironmentInfo?.Settings?.DockPanelLayouts != null)
			{
				SaveDevExpressDisplaySetting(DevExpressDisplayFontSettingsKey, LegacyDevExpressDisplayFontSettingsKey, newSettings.FontFamilyName);
				SaveDevExpressDisplaySetting(DevExpressDisplayFontSizeSettingsKey, LegacyDevExpressDisplayFontSizeSettingsKey, DevExpressDisplaySettings.FormatFontSize(newSettings.FontSizePt));
				SaveDevExpressDisplaySetting(DevExpressDisplayDensitySettingsKey, LegacyDevExpressDisplayDensitySettingsKey, newSettings.Density);
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
			}
			finally
			{
				_updatingDevExpressDisplaySelector = false;
			}

			_devExpressDisplayButton.Hint = String.Format("{0}, {1}, {2}", _devExpressDisplaySettings.FontFamilyName, DevExpressDisplaySettings.FormatFontSize(_devExpressDisplaySettings.FontSizePt), _devExpressDisplaySettings.Density);
		}

		/// <summary>
		/// Updates the checked state of a display-selector choice group.
		/// </summary>
		private static void UpdateDevExpressDisplayChoiceChecks(IEnumerable<BarButtonItem> items, string selectedValue)
		{
			foreach (BarButtonItem item in items)
				item.Down = String.Equals(Convert.ToString(item.Tag), selectedValue, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Applies the current display settings to all active DevExpress surfaces already created by the main form.
		/// </summary>
		private void ApplyDevExpressDisplaySettingsToSurfaces()
		{
			if (_devExpressDisplaySettings == null)
				return;

			DevExpressDisplaySettingsApplier.ApplyToControlTree(this, _devExpressDisplaySettings);
			DevExpressDisplaySettingsApplier.ApplyToBarManager(barManagerMain, _devExpressDisplaySettings);

			ModManagerDXControl modManagerDX = _modManagerControl as ModManagerDXControl;
			modManagerDX?.ApplyDisplaySettings(_devExpressDisplaySettings);
			_pluginManagerControl?.ApplyDisplaySettings(_devExpressDisplaySettings);
			_categoryManagerControl?.ApplyDisplaySettings(_devExpressDisplaySettings);
			_fileManagerControl?.ApplyDisplaySettings(_devExpressDisplaySettings);
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
