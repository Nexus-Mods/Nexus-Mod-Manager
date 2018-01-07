using Nexus.Client.Games.Fallout3;

namespace Nexus.Client.Games.Fallout4VR
{
	/// <summary>
	/// The paths of the settings files used by Fallout4.
	/// </summary>
	public class Fallout4VRSettingsFiles : FalloutSettingsFiles
	{
		#region Properties

		/// <summary>
		/// Gets or sets the path to the Fallout4_default.ini file.
		/// </summary>
		/// <value>The path to the Fallout4_default.ini file.</value>
		public string Fallout4DefaultIniPath
		{
			get
			{
				return this["Fallout4DefaultIniPath"];
			}
			set
			{
				this["Fallout4DefaultIniPath"] = value;
			}
		}

		#endregion
	}
}
