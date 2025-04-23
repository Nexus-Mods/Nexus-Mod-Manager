using Nexus.Client.Games.Fallout3;

namespace Nexus.Client.Games.OblivionRemastered
{
	/// <summary>
	/// The paths of the settings files used by OblivionRemastered.
	/// </summary>
	public class OblivionRemasteredSettingsFiles : FalloutSettingsFiles
	{
		#region Properties

		/// <summary>
		/// Gets or sets the path to the Skyrim_default.ini file.
		/// </summary>
		/// <value>The path to the Skyrim_default.ini file.</value>
		public string OblivionRemasteredDefaultIniPath
		{
			get
			{
				return this["OblivionRemasteredDefaultIniPath"];
			}
			set
			{
				this["OblivionRemasteredDefaultIniPath"] = value;
			}
		}

		#endregion
	}
}
