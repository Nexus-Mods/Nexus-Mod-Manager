using System.Windows.Forms;
using Nexus.Client.Util;

namespace Nexus.Client.Settings.UI
{
	/// <summary>
	/// A view allowing the editing of mod options.
	/// </summary>
	public partial class ModOptionsPage : UserControl, ISettingsGroupView
	{
		#region Constructors

		/// <summary>
		/// A sinmple consturctor that initializes teh object with the given values.
		/// </summary>
		/// <param name="p_mosSettings">The settings group whose settings will be editable with this view.</param>
		public ModOptionsPage(ModOptionsSettingsGroup p_mosSettings)
		{
			SettingsGroup = p_mosSettings;
			InitializeComponent();

			cbxModCompression.DataSource = p_mosSettings.ModCompressionLevels;
			cbxModFormat.DataSource = p_mosSettings.ModCompressionFormats;
			BindingHelper.CreateFullBinding(cbxModCompression, () => cbxModCompression.SelectedItem, p_mosSettings, () => p_mosSettings.ModCompressionLevel);
			BindingHelper.CreateFullBinding(cbxModFormat, () => cbxModFormat.SelectedItem, p_mosSettings, () => p_mosSettings.ModCompressionFormat);
		}

		#endregion

		#region ISettingsGroupView Members

		/// <summary>
		/// Gets the <see cref="SettingsGroup"/> whose settings will be editable with this view.
		/// </summary>
		/// <value>The <see cref="SettingsGroup"/> whose settings will be editable with this view.</value>
		public SettingsGroup SettingsGroup { get; private set; }

		#endregion
	}
}
