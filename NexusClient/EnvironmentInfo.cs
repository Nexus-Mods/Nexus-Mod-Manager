using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;
using Nexus.Client.Settings;

namespace Nexus.Client
{
	/// <summary>
	/// Provides information about the current programme environment.
	/// </summary>
	public class EnvironmentInfo : IEnvironmentInfo
	{
		private string m_strApplicationPersonalDataFolderPath = null;
		private string m_strPersonalDataFolderPath = null;
		private string m_strTempPath = null;

		#region Properties

		/// <summary>
		/// Gets the path to the user's personal data folder.
		/// </summary>
		/// <value>The path to the user's personal data folder.</value>
		public string PersonalDataFolderPath
		{
			get
			{
				return m_strPersonalDataFolderPath;
			}
		}

		/// <summary>
		/// Gets the path to the mod manager's folder in the user's personal data folder.
		/// </summary>
		/// <value>The path to the mod manager's folder in the user's personal data folder.</value>
		public string ApplicationPersonalDataFolderPath
		{
			get
			{
				return m_strApplicationPersonalDataFolderPath;
			}
		}

		/// <summary>
		/// Gets whether the programme is running under the Mono framework.
		/// </summary>
		/// <value>Whether the programme is running under the Mono framework.</value>
		public bool IsMonoMode
		{
			get
			{
				return Type.GetType("Mono.Runtime") != null;
			}
		}

		/// <summary>
		/// Gets the temporary path used by the application.
		/// </summary>
		/// <value>The temporary path used by the application.</value>
		public string TemporaryPath
		{
			get
			{
				return m_strTempPath;
			}
		}

		/// <summary>
		/// Gets the path to the directory where programme data is stored.
		/// </summary>
		/// <value>The path to the directory where programme data is stored.</value>
		public string ProgrammeInfoDirectory
		{
			get
			{
				return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "data");
			}
		}

		/// <summary>
		/// Gets whether the current process is 64bit.
		/// </summary>
		/// <value>Whether the current process is 64bit.</value>
		public bool Is64BitProcess
		{
			get
			{
				return (IntPtr.Size == 8);
			}
		}

		/// <summary>
		/// Gets the application and user settings.
		/// </summary>
		/// <value>The application and user settings.</value>
		public ISettings Settings { get; private set; }

		/// <summary>
		/// Gets the version of the running application.
		/// </summary>
		/// <value>The version of the running application.</value>
		public Version ApplicationVersion
		{
			get
			{
				return new Version(ProgrammeMetadata.VersionString);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_setSettings">The application and user settings.</param>
		public EnvironmentInfo(ISettings p_setSettings)
		{
			Settings = p_setSettings;
			m_strPersonalDataFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
			if (String.IsNullOrEmpty(m_strPersonalDataFolderPath))
				m_strPersonalDataFolderPath = Registry.GetValue(@"HKEY_CURRENT_USER\software\microsoft\windows\currentversion\explorer\user shell folders", "Personal", null).ToString();

            if (String.IsNullOrEmpty(Settings.TempPathFolder))
            {
                m_strTempPath = Path.Combine(Path.GetTempPath(), Application.ProductName);
                if (!Directory.Exists(m_strTempPath))
                    Directory.CreateDirectory(m_strTempPath);
            }
            else
                m_strTempPath = Settings.TempPathFolder;

			m_strApplicationPersonalDataFolderPath = Path.Combine(m_strPersonalDataFolderPath, p_setSettings.ModManagerName);
		}

		#endregion
	}
}
