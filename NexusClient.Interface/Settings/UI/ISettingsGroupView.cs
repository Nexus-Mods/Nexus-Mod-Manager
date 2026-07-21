
namespace Nexus.Client.Settings.UI
{
	/// <summary>
	/// Describes the methods and properties of a view allowing the editing of the settings
	/// in a <see cref="SettingsGroup"/>.
	/// </summary>
	public interface ISettingsGroupView
	{
		/// <summary>
		/// Gets the <see cref="SettingsGroup"/> whose settings will be editable with this view.
		/// </summary>
		/// <value>The <see cref="SettingsGroup"/> whose settings will be editable with this view.</value>
		SettingsGroup SettingsGroup { get; }
	}
}
