using Nexus.Client.Games.Fallout3;

namespace Nexus.Client.Games.Skyrim
{
	/// <summary>
	/// The paths of the settings files used by Skyrim.
	/// </summary>
	public class SkyrimSettingsFiles : FalloutSettingsFiles
	{
		#region Properties

		/// <summary>
		/// Gets or sets the path to the skyrim_default.ini file.
		/// </summary>
		/// <value>The path to the skyrim_default.ini file.</value>
		public string SkyrimDefaultIniPath
		{
			get
			{
				return this["SkyrimDefaultIniPath"];
			}
			set
			{
				this["SkyrimDefaultIniPath"] = value;
			}
		}

		#endregion
	}
}
