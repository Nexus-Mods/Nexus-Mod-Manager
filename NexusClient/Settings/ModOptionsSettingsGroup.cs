using System;
using Nexus.Client.Games;
using SevenZip;

namespace Nexus.Client.Settings
{
	/// <summary>
	/// The group of mod option settings.
	/// </summary>
	public class ModOptionsSettingsGroup : SettingsGroup
	{
		private CompressionLevel m_cplCompressionLevel = CompressionLevel.Ultra;
		private OutArchiveFormat m_oafModArchiveFormat = OutArchiveFormat.SevenZip;

		#region Properties

		/// <summary>
		/// Gets the title of the settings group.
		/// </summary>
		/// <value>The title of the settings group.</value>
		public override string Title
		{
			get
			{
				return "Mod Options";
			}
		}

		/// <summary>
		/// Gets the list of available compression levels.
		/// </summary>
		/// <value>The list of available compression levels.</value>
		public CompressionLevel[] ModCompressionLevels { get; private set; }

		/// <summary>
		/// Gets or sets the preferred compression level to use for mods.
		/// </summary>
		/// <remarks>
		/// Note that not all mod formats support confirgurable compression levels.
		/// </remarks>
		/// <value>The preferred compression level to use for mods.</value>
		public CompressionLevel ModCompressionLevel
		{
			get
			{
				return m_cplCompressionLevel;
			}
			set
			{
				SetPropertyIfChanged(ref m_cplCompressionLevel, value, () => ModCompressionLevel);
			}
		}

		/// <summary>
		/// Gets the list of available compression formats.
		/// </summary>
		/// <value>The list of available compression formats.</value>
		public OutArchiveFormat[] ModCompressionFormats { get; private set; }

		/// <summary>
		/// Gets or sets the preferred compression format to use for mods.
		/// </summary>
		/// <remarks>
		/// Note that not all mod formats support confirgurable compression formats.
		/// </remarks>
		/// <value>The preferred compression format to use for mods.</value>
		public OutArchiveFormat ModCompressionFormat
		{
			get
			{
				return m_oafModArchiveFormat;
			}
			set
			{
				SetPropertyIfChanged(ref m_oafModArchiveFormat, value, () => ModCompressionFormat);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public ModOptionsSettingsGroup(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
			ModCompressionLevels = (CompressionLevel[])Enum.GetValues(typeof(CompressionLevel));
			ModCompressionFormats = (OutArchiveFormat[])Enum.GetValues(typeof(OutArchiveFormat));
		}

		#endregion

		/// <summary>
		/// Loads the grouped setting values from the persistent store.
		/// </summary>
		public override void Load()
		{
			ModCompressionLevel = EnvironmentInfo.Settings.ModCompressionLevel;
			ModCompressionFormat = EnvironmentInfo.Settings.ModCompressionFormat;
		}

		/// <summary>
		/// Persists the grouped setting values to the persistent store.
		/// </summary>
		/// <returns><c>true</c> if the settings were persisted;
		/// <c>false</c> otherwise.</returns>
		public override bool Save()
		{
			EnvironmentInfo.Settings.ModCompressionLevel = ModCompressionLevel;
			EnvironmentInfo.Settings.ModCompressionFormat = ModCompressionFormat;
			EnvironmentInfo.Settings.Save();
			return true;
		}
	}
}
