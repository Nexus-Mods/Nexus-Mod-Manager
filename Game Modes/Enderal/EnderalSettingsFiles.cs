using Nexus.Client.Games.Fallout3;

namespace Nexus.Client.Games.Enderal
{
	/// <summary>
	/// The paths of the settings files used by Enderal.
	/// </summary>
	public class EnderalSettingsFiles : FalloutSettingsFiles
	{
		#region Properties

		/// <summary>
		/// Gets or sets the path to the Enderal_default.ini file.
		/// </summary>
		/// <value>The path to the Enderal_default.ini file.</value>
		public string EnderalDefaultIniPath
		{
			get
			{
				return this["EnderalDefaultIniPath"];
			}
			set
			{
				this["EnderalDefaultIniPath"] = value;
			}
		}

		#endregion
	}
}
