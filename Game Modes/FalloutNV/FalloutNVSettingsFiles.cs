using Nexus.Client.Games.Fallout3;

namespace Nexus.Client.Games.FalloutNV
{
	/// <summary>
	/// The paths of the settings files used by Fallout: New Vegas.
	/// </summary>
	public class FalloutNVSettingsFiles : FalloutSettingsFiles
	{
		#region Properties

		/// <summary>
		/// Gets or sets the path to the fallout_default.ini file.
		/// </summary>
		/// <value>The path to the fallout_default.ini file.</value>
		public string FODefaultIniPath
		{
			get
			{
				return this["FODefaultIniPath"];
			}
			set
			{
				this["FODefaultIniPath"] = value;
			}
		}

		#endregion
	}
}
