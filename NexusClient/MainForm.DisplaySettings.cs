namespace Nexus.Client
{
	using System;
	using System.Collections.Generic;
	using System.Drawing;
	using System.Windows.Forms;

	using Nexus.Client.ModManagement.UI;
	using Nexus.Client.UI;

	public partial class MainForm
	{
		private const string DevExpressDisplayFontSettingsKey =
			"mainForm.DevExpressDisplay.Font";
		private const string DevExpressDisplayFontSizeSettingsKey =
			"mainForm.DevExpressDisplay.FontSize";
		private const string DevExpressDisplayDensitySettingsKey =
			"mainForm.DevExpressDisplay.Density";

		private const string LegacyDevExpressDisplayFontSettingsKey =
			"modManagerDXGrid.Font";
		private const string LegacyDevExpressDisplayFontSizeSettingsKey =
			"modManagerDXGrid.FontSize";
		private const string LegacyDevExpressDisplayDensitySettingsKey =
			"modManagerDXGrid.Density";

		private ToolStripDropDownButton _devExpressDisplayButton;
		private ComboBox _devExpressDisplayFontCombo;
		private ComboBox _devExpressDisplayFontSizeCombo;
		private ComboBox _devExpressDisplayDensityCombo;
		private bool _updatingDevExpressDisplaySelector;
		private DevExpressDisplaySettings _devExpressDisplaySettings;

		private void InitializeDevExpressDisplaySelector(MainFormVM viewModel)
		{
			string fontName = DevExpressDisplaySettings.ResolveFontFamily(
				ReadDevExpressDisplaySetting(
					viewModel,
					DevExpressDisplayFontSettingsKey,
					LegacyDevExpressDisplayFontSettingsKey,
					DevExpressDisplaySettings.DefaultFontFamily));
			float fontSize = DevExpressDisplaySettings.ParseFontSize(
				ReadDevExpressDisplaySetting(
					viewModel,
					DevExpressDisplayFontSizeSettingsKey,
					LegacyDevExpressDisplayFontSizeSettingsKey,
					DevExpressDisplaySettings.FormatFontSize(
						DevExpressDisplaySettings.DefaultFontSizePt)));
			string density = DevExpressDisplaySettings.ResolveDensity(
				ReadDevExpressDisplaySetting(
					viewModel,
					DevExpressDisplayDensitySettingsKey,
					LegacyDevExpressDisplayDensitySettingsKey,
					DevExpressDisplaySettings.DefaultDensity));

			_devExpressDisplaySettings = new DevExpressDisplaySettings(
				fontName,
				fontSize,
				density);

			_devExpressDisplayButton = new ToolStripDropDownButton
			{
				Name = "toolStripDropDownButtonDevExpressDisplay",
				DisplayStyle = ToolStripItemDisplayStyle.Text,
				Text = "Aa Display",
				ToolTipText = "Font, size and density for the manager UI"
			};

			TableLayoutPanel panel = new TableLayoutPanel
			{
				AutoSize = false,
				ColumnCount = 2,
				RowCount = 4,
				Padding = new Padding(8),
				Size = new Size(260, 126)
			};
			panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58f));
			panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
			panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
			panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
			panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));

			_devExpressDisplayFontCombo = CreateDevExpressDisplayCombo(
				DevExpressDisplaySettings.FontChoices,
				162);
			_devExpressDisplayFontSizeCombo = CreateDevExpressDisplayCombo(
				DevExpressDisplaySettings.FontSizeChoices,
				84);
			_devExpressDisplayDensityCombo = CreateDevExpressDisplayCombo(
				DevExpressDisplaySettings.DensityChoices,
				132);

			AddDevExpressDisplayRow(
				panel,
				0,
				"Font:",
				_devExpressDisplayFontCombo);
			AddDevExpressDisplayRow(
				panel,
				1,
				"Size:",
				_devExpressDisplayFontSizeCombo);
			AddDevExpressDisplayRow(
				panel,
				2,
				"Density:",
				_devExpressDisplayDensityCombo);

			Button resetButton = new Button
			{
				Text = "Reset",
				AutoSize = false,
				Height = 24,
				Dock = DockStyle.Left,
				Width = 76
			};
			resetButton.Click += (sender, args) => ResetDevExpressDisplaySettings();
			panel.Controls.Add(resetButton, 1, 3);

			_devExpressDisplayButton.DropDownItems.Add(
				new ToolStripControlHost(panel)
				{
					AutoSize = false,
					Size = panel.Size,
					Margin = Padding.Empty,
					Padding = Padding.Empty
				});

			UpdateDevExpressDisplaySelector();

			_devExpressDisplayFontCombo.SelectedIndexChanged +=
				DevExpressDisplayControl_SelectedIndexChanged;
			_devExpressDisplayFontSizeCombo.SelectedIndexChanged +=
				DevExpressDisplayControl_SelectedIndexChanged;
			_devExpressDisplayDensityCombo.SelectedIndexChanged +=
				DevExpressDisplayControl_SelectedIndexChanged;

			int insertionIndex =
				toolStrip1.Items.IndexOf(_devExpressSkinComboBox) + 1;
			toolStrip1.Items.Insert(insertionIndex, _devExpressDisplayButton);

			Disposed += MainFormDisplaySettings_Disposed;
		}

		private static string ReadDevExpressDisplaySetting(
			MainFormVM viewModel,
			string key,
			string legacyKey,
			string defaultValue)
		{
			if (viewModel?.EnvironmentInfo?.Settings?.DockPanelLayouts == null)
				return defaultValue;

			if (viewModel.EnvironmentInfo.Settings.DockPanelLayouts.ContainsKey(key))
			{
				string value =
					viewModel.EnvironmentInfo.Settings.DockPanelLayouts[key];
				if (!string.IsNullOrWhiteSpace(value))
					return value;
			}

			if (viewModel.EnvironmentInfo.Settings.DockPanelLayouts.ContainsKey(
					legacyKey))
			{
				string legacyValue =
					viewModel.EnvironmentInfo.Settings.DockPanelLayouts[legacyKey];
				if (!string.IsNullOrWhiteSpace(legacyValue))
					return legacyValue;
			}

			return defaultValue;
		}

		private static ComboBox CreateDevExpressDisplayCombo(
			IEnumerable<string> items,
			int width)
		{
			ComboBox combo = new ComboBox
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				Width = width,
				Anchor = AnchorStyles.Left | AnchorStyles.Right
			};

			foreach (string item in items)
				combo.Items.Add(item);

			return combo;
		}

		private static void AddDevExpressDisplayRow(
			TableLayoutPanel panel,
			int row,
			string labelText,
			Control control)
		{
			Label label = new Label
			{
				Text = labelText,
				AutoSize = false,
				TextAlign = ContentAlignment.MiddleLeft,
				Dock = DockStyle.Fill
			};

			control.Dock = DockStyle.Fill;
			panel.Controls.Add(label, 0, row);
			panel.Controls.Add(control, 1, row);
		}

		private void DevExpressDisplayControl_SelectedIndexChanged(
			object sender,
			EventArgs e)
		{
			if (_updatingDevExpressDisplaySelector)
				return;

			SetDevExpressDisplaySettings(
				_devExpressDisplayFontCombo.SelectedItem as string,
				DevExpressDisplaySettings.ParseFontSize(
					_devExpressDisplayFontSizeCombo.SelectedItem as string),
				_devExpressDisplayDensityCombo.SelectedItem as string,
				true);
		}

		private void ResetDevExpressDisplaySettings()
		{
			SetDevExpressDisplaySettings(
				DevExpressDisplaySettings.DefaultFontFamily,
				DevExpressDisplaySettings.DefaultFontSizePt,
				DevExpressDisplaySettings.DefaultDensity,
				true);
		}

		private void SetDevExpressDisplaySettings(
			string fontName,
			float fontSize,
			string density,
			bool save)
		{
			DevExpressDisplaySettings previousSettings =
				_devExpressDisplaySettings;
			DevExpressDisplaySettings newSettings =
				new DevExpressDisplaySettings(fontName, fontSize, density);

			_devExpressDisplaySettings = newSettings;
			UpdateDevExpressDisplaySelector();
			ApplyDevExpressDisplaySettingsToSurfaces();

			if (save &&
				ViewModel?.EnvironmentInfo?.Settings?.DockPanelLayouts != null)
			{
				SaveDevExpressDisplaySetting(
					DevExpressDisplayFontSettingsKey,
					LegacyDevExpressDisplayFontSettingsKey,
					newSettings.FontFamilyName);
				SaveDevExpressDisplaySetting(
					DevExpressDisplayFontSizeSettingsKey,
					LegacyDevExpressDisplayFontSizeSettingsKey,
					DevExpressDisplaySettings.FormatFontSize(
						newSettings.FontSizePt));
				SaveDevExpressDisplaySetting(
					DevExpressDisplayDensitySettingsKey,
					LegacyDevExpressDisplayDensitySettingsKey,
					newSettings.Density);
				ViewModel.EnvironmentInfo.Settings.Save();
			}

			previousSettings?.Dispose();
		}

		private void SaveDevExpressDisplaySetting(
			string key,
			string legacyKey,
			string value)
		{
			ViewModel.EnvironmentInfo.Settings.DockPanelLayouts[key] = value;
			ViewModel.EnvironmentInfo.Settings.DockPanelLayouts[legacyKey] = value;
		}

		private void UpdateDevExpressDisplaySelector()
		{
			if (_devExpressDisplaySettings == null ||
				_devExpressDisplayFontCombo == null ||
				_devExpressDisplayFontSizeCombo == null ||
				_devExpressDisplayDensityCombo == null)
			{
				return;
			}

			_updatingDevExpressDisplaySelector = true;
			try
			{
				if (!_devExpressDisplayFontCombo.Items.Contains(
						_devExpressDisplaySettings.FontFamilyName))
				{
					_devExpressDisplayFontCombo.Items.Add(
						_devExpressDisplaySettings.FontFamilyName);
				}

				_devExpressDisplayFontCombo.SelectedItem =
					_devExpressDisplaySettings.FontFamilyName;
				_devExpressDisplayFontSizeCombo.SelectedItem =
					DevExpressDisplaySettings.FormatFontSize(
						_devExpressDisplaySettings.FontSizePt);
				_devExpressDisplayDensityCombo.SelectedItem =
					_devExpressDisplaySettings.Density;
			}
			finally
			{
				_updatingDevExpressDisplaySelector = false;
			}

			_devExpressDisplayButton.ToolTipText = string.Format(
				"{0}, {1}, {2}",
				_devExpressDisplaySettings.FontFamilyName,
				DevExpressDisplaySettings.FormatFontSize(
					_devExpressDisplaySettings.FontSizePt),
				_devExpressDisplaySettings.Density);
		}

		private void ApplyDevExpressDisplaySettingsToSurfaces()
		{
			if (_devExpressDisplaySettings == null)
				return;

			ModManagerDXControl modManagerDX =
				_modManagerControl as ModManagerDXControl;
			modManagerDX?.ApplyDisplaySettings(_devExpressDisplaySettings);
			_pluginManagerControl?.ApplyDisplaySettings(
				_devExpressDisplaySettings);
			_categoryManagerControl?.ApplyDisplaySettings(
				_devExpressDisplaySettings);
			_fileManagerControl?.ApplyDisplaySettings(
				_devExpressDisplaySettings);
		}

		private void MainFormDisplaySettings_Disposed(
			object sender,
			EventArgs e)
		{
			_devExpressDisplaySettings?.Dispose();
			_devExpressDisplaySettings = null;
		}
	}
}
