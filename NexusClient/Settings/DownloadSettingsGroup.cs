using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Nexus.Client.Games;
using Nexus.Client.ModRepositories;
using Nexus.Client.Util;

namespace Nexus.Client.Settings
{
	/// <summary>
	/// The group of download settings.
	/// </summary>
	public class DownloadSettingsGroup : SettingsGroup
	{

		private bool m_booPremiumOnly = false;
		private string m_strUserLocation = "";
		private Int32 m_intConnections = 1;

		#region Properties

		/// <summary>
		/// Gets the title of the settings group.
		/// </summary>
		/// <value>The title of the settings group.</value>
		public override string Title
		{
			get
			{
				return "Download Options";
			}
		}

		/// <summary>
		/// Gets or sets whether the user wants to use only Premium Server.
		/// </summary>
		/// <value>Whether the user wants to use only Premium Server.</value>
		public bool PremiumOnly
		{
			get
			{
				return m_booPremiumOnly;
			}
			set
			{
				SetPropertyIfChanged(ref m_booPremiumOnly, value, () => PremiumOnly);
			}
		}

		/// <summary>
		/// Gets or sets the user favourite download location.
		/// </summary>
		/// <value>The user favourite download location.</value>
		public string UserLocation
		{
			get
			{
				return m_strUserLocation;
			}
			set
			{
				SetPropertyIfChanged(ref m_strUserLocation, value, () => UserLocation);
			}
		}

		/// <summary>
		/// Gets or sets the number of connections per file.
		/// </summary>
		/// <value>The number of connections per file.</value>
		public Int32 NumberOfConnections
		{
			get
			{
				return m_intConnections;
			}
			set
			{
				SetPropertyIfChanged(ref m_intConnections, value, () => NumberOfConnections);
			}
		}

		/// <summary>
		/// Gets the repository's file server zones.
		/// </summary>
		/// <value>the repository's file server zones.</value>
		public IList<FileServerZone> FileServerZones { get; private set; }

		/// <summary>
		/// Gets the number allowed connections.
		/// </summary>
		/// <value>The number allowed connections.</value>
		public Int32[] AllowedConnections { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public DownloadSettingsGroup(IEnvironmentInfo p_eifEnvironmentInfo, IList<FileServerZone> p_fszFileServerZones, Int32[] p_intAllowedConnections)
			: base(p_eifEnvironmentInfo)
		{
			FileServerZones = p_fszFileServerZones;
			AllowedConnections = p_intAllowedConnections;
		}

		#endregion

		/// <summary>
		/// Loads the grouped setting values from the persistent store.
		/// </summary>
		public override void Load()
		{
			PremiumOnly = EnvironmentInfo.Settings.PremiumOnly;
			UserLocation = EnvironmentInfo.Settings.UserLocation;
			NumberOfConnections = EnvironmentInfo.Settings.NumberOfConnections;
		}

		/// <summary>
		/// Persists the grouped setting values to the persistent store.
		/// </summary>
		/// <returns><c>true</c> if the settings were persisted;
		/// <c>false</c> otherwise.</returns>
		public override bool Save()
		{
			EnvironmentInfo.Settings.PremiumOnly = PremiumOnly;
			EnvironmentInfo.Settings.UserLocation = UserLocation;
			EnvironmentInfo.Settings.NumberOfConnections = NumberOfConnections;
			EnvironmentInfo.Settings.Save();
			return true;
		}
	}
}
