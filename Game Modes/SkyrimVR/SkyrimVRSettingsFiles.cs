namespace Nexus.Client.Games.SkyrimVR
{
    using Nexus.Client.Games.Fallout3;

    /// <summary>
    /// The paths of the settings files used by SkyrimVR.
    /// </summary>
    public class SkyrimVRSettingsFiles : FalloutSettingsFiles
	{
		#region Properties

		/// <summary>
		/// Gets or sets the path to the Skyrim_default.ini file.
		/// </summary>
		/// <value>The path to the Skyrim_default.ini file.</value>
		public string SkyrimVRDefaultIniPath
		{
			get => this["SkyrimVRDefaultIniPath"];
		    set => this["SkyrimVRDefaultIniPath"] = value;
		}

		#endregion
	}
}
