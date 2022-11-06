using Nexus.Client.Games.Fallout3;

namespace Nexus.Client.Games.SkyrimGOG
{
	/// <summary>
	/// The paths of the settings files used by SkyrimGOG.
	/// </summary>
	public class SkyrimGOGSettingsFiles : FalloutSettingsFiles
	{
		#region Properties

		/// <summary>
		/// Gets or sets the path to the Skyrim_default.ini file.
		/// </summary>
		/// <value>The path to the Skyrim_default.ini file.</value>
		public string SkyrimGOGDefaultIniPath
		{
			get
			{
				return this["SkyrimGOGDefaultIniPath"];
			}
			set
			{
				this["SkyrimGOGDefaultIniPath"] = value;
			}
		}

		#endregion
	}
}
