using Nexus.Client.Games.Fallout3;

namespace Nexus.Client.Games.SkyrimSE
{
	/// <summary>
	/// The paths of the settings files used by SkyrimSE.
	/// </summary>
	public class SkyrimSESettingsFiles : FalloutSettingsFiles
	{
		#region Properties

		/// <summary>
		/// Gets or sets the path to the SkyrimSE_default.ini file.
		/// </summary>
		/// <value>The path to the SkyrimSE_default.ini file.</value>
		public string SkyrimSEDefaultIniPath
		{
			get
			{
				return this["SkyrimSEDefaultIniPath"];
			}
			set
			{
				this["SkyrimSEDefaultIniPath"] = value;
			}
		}

		#endregion
	}
}
