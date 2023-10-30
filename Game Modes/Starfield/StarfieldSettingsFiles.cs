using Nexus.Client.Games.Starfield;

namespace Nexus.Client.Games.Starfield
{
	/// <summary>
	/// The paths of the settings files used by Starfield.
	/// </summary>
	public class StarfieldSettingsFiles : Nexus.Client.Games.Fallout3.FalloutSettingsFiles
	{
		#region Properties

		/// <summary>
		/// Gets or sets the path to the Starfield_default.ini file.
		/// </summary>
		/// <value>The path to the Starfield_default.ini file.</value>
		public string StarfieldDefaultIniPath
		{
			get
			{
				return this["StarfieldDefaultIniPath"];
			}
			set
			{
				this["StarfieldDefaultIniPath"] = value;
			}
		}

		#endregion
	}
}
