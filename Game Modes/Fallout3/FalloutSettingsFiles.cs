using Nexus.Client.Games.Gamebryo;

namespace Nexus.Client.Games.Fallout3
{
	/// <summary>
	/// The paths of the settings files used by Fallout.
	/// </summary>
	public class FalloutSettingsFiles : GamebryoSettingsFiles
	{
		/// <summary>
		/// Gets or sets the path of the FalloutPrefs.ini file.
		/// </summary>
		/// <value>The path of the FalloutPrefs.ini file.</value>
		public string FOPrefsIniPath
		{
			get
			{
				return this["FOPrefsIniPath"];
			}
			set
			{
				this["FOPrefsIniPath"] = value;
			}
		}

		/// <summary>
		/// Gets or sets the path of the GECKCustom.ini file.
		/// </summary>
		/// <value>The path of the GECKCustom.ini file.</value>
		public string GeckIniPath
		{
			get
			{
				return this["GeckIniPath"];
			}
			set
			{
				this["GeckIniPath"] = value;
			}
		}

		/// <summary>
		/// Gets or sets the path of the GECKPrefs.ini file.
		/// </summary>
		/// <value>The path of the GECKPrefs.ini file.</value>
		public string GeckPrefsIniPath
		{
			get
			{
				return this["GeckPrefsIniPath"];
			}
			set
			{
				this["GeckPrefsIniPath"] = value;
			}
		}
	}
}
