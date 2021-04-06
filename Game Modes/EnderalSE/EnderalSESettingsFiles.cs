using Nexus.Client.Games.Fallout3;

namespace Nexus.Client.Games.EnderalSE
{
	/// <summary>
	/// The paths of the settings files used by EnderalSE.
	/// </summary>
	public class EnderalSESettingsFiles : FalloutSettingsFiles
	{
		#region Properties

		/// <summary>
		/// Gets or sets the path to the EnderalSE_default.ini file.
		/// </summary>
		/// <value>The path to the EnderalSE_default.ini file.</value>
		public string EnderalSEDefaultIniPath
		{
			get
			{
				return this["EnderalSEDefaultIniPath"];
			}
			set
			{
				this["EnderalSEDefaultIniPath"] = value;
			}
		}

		#endregion
	}
}
