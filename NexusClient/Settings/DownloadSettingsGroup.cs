using System;
using System.Collections.Generic;
using System.Linq;
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

		private bool m_booUseMultithreadedDownloads = false;
		private bool m_booPremiumEnabled = false;
		private string m_strUserLocation = "default";
		private IModRepository m_mmrModRepository = null;

		#region Custom Events

		public event EventHandler UpdatedSettings;

		#endregion

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
		/// Gets or sets whether to use multithreaded downloads.
		/// </summary>
		/// <value>Whether to use multithreaded downloads.</value>
		public bool UseMultithreadedDownloads
		{
			get
			{
				return m_booUseMultithreadedDownloads;
			}
			set
			{
				SetPropertyIfChanged(ref m_booUseMultithreadedDownloads, value, () => UseMultithreadedDownloads);
			}
		}

		/// <summary>
		/// Gets or sets whether the user wants to use only Premium Server.
		/// </summary>
		/// <value>Whether the user wants to use only Premium Server.</value>
		public bool PremiumEnabled
		{
			get
			{
				return m_booPremiumEnabled;
			}
			private set
			{
				SetPropertyIfChanged(ref m_booPremiumEnabled, value, () => PremiumEnabled);
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
		/// Gets the repository's file server zones.
		/// </summary>
		/// <value>the repository's file server zones.</value>
		public List<FileServerZone> FileServerZones { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public DownloadSettingsGroup(IEnvironmentInfo p_eifEnvironmentInfo, IModRepository p_mmrModRepository)
			: base(p_eifEnvironmentInfo)
		{
			m_mmrModRepository = p_mmrModRepository;
			m_mmrModRepository.UserStatusUpdate += new EventHandler(m_mmrModRepository_UserStatusUpdate);

			if (LoadRepositorySettings())
			{
				Load();
				Save();
			}
		}

		#endregion

		private bool LoadRepositorySettings()
		{
			bool MemberCheck  = false;
			Int32 UserStatus = 3;

			if (FileServerZones != null)
				FileServerZones.Clear();

			if (m_mmrModRepository.UserStatus != null)
				UserStatus = Convert.ToInt32(m_mmrModRepository.UserStatus[1]);

			if (((UserStatus != 4) && (UserStatus != 6) && (UserStatus != 13) && (UserStatus != 27) && (UserStatus != 31) && (UserStatus != 32)) && !m_mmrModRepository.IsOffline)
			{
				PremiumEnabled = false;
				FileServerZones = m_mmrModRepository.FileServerZones.Where(x => x.IsPremium == false).ToList();
			}
			else
			{
				PremiumEnabled = true;
				FileServerZones = m_mmrModRepository.FileServerZones.ToList();
			}

			FileServerZone fszUser = FileServerZones.Find(x => x.FileServerID == EnvironmentInfo.Settings.UserLocation);

			if (!PremiumEnabled)
			{
				if (fszUser != null)
				{
					if (fszUser.IsPremium)
					{
						EnvironmentInfo.Settings.UserLocation = "default";
						MemberCheck = true;
					}
				}
				else
					EnvironmentInfo.Settings.UserLocation = "default";
			}

			return MemberCheck;
		}

		/// <summary>
		/// Loads the grouped setting values from the persistent store.
		/// </summary>
		public override void Load()
		{
			UseMultithreadedDownloads = EnvironmentInfo.Settings.UseMultithreadedDownloads;
			FileServerZone fszUser = FileServerZones.Find(x => x.FileServerID == EnvironmentInfo.Settings.UserLocation);
			if (fszUser != null)
				UserLocation = fszUser.FileServerID;
			else
				UserLocation = "default";
		}

		/// <summary>
		/// Persists the grouped setting values to the persistent store.
		/// </summary>
		/// <returns><c>true</c> if the settings were persisted;
		/// <c>false</c> otherwise.</returns>
		public override bool Save()
		{
			EnvironmentInfo.Settings.UseMultithreadedDownloads = UseMultithreadedDownloads;
			EnvironmentInfo.Settings.UserLocation = UserLocation;
			lock (EnvironmentInfo.Settings)
				EnvironmentInfo.Settings.Save();
			return true;
		}

		/// <summary>
		/// Handles the <see cref="m_mmrModRepository.UserStatusUpdate"/> event of the tasks list.
		/// </summary>
		/// <remarks>
		/// Updates the UI elements.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void m_mmrModRepository_UserStatusUpdate(object sender, EventArgs e)
		{
			LoadRepositorySettings();
			if (this.UpdatedSettings != null)
				this.UpdatedSettings(this, new EventArgs());
		}
	}
}
