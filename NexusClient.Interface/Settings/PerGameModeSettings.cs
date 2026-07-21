
namespace Nexus.Client.Settings
{
	/// <summary>
	/// A dictionary that stores per game mode settings.
	/// </summary>
	public class PerGameModeSettings<T> : KeyedSettings<T>
	{
		#region Properties

		/// <summary>
		/// Gets the name of the key name to use when serializing the dictionary.
		/// </summary>
		/// <value>The name of the key name to use when serializing the dictionary.</value>
		protected override string XmlKeyName
		{
			get
			{
				return "modeId";
			}
		}

		#endregion
	}
}

