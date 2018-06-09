namespace Nexus.Client.Properties
{
    using Nexus.Client.Settings;

    /// <summary>
    /// This class adds the <see cref="ISettings"/> to the project's <see cref="Properties.Settings"/>
    /// class.
    /// </summary>
    /// <remarks>
    /// This file should not contain any memebers or properties.
    /// </remarks>
    internal sealed partial class Settings : ISettings
	{
        private static object _settingsFileLock = new object();

		/// <summary>
		/// Gets the full name of the mod manager.
		/// </summary>
		/// <value>The full name of the mod manager.</value>
		public string ModManagerName
		{
			get
			{
				return ProgrammeMetadata.ModManagerName;
			}
		}

        /// <summary>
        /// A thread-safe call to save the current settings to file.
        /// </summary>
        public override void Save()
        {
            lock(_settingsFileLock)
            {
                base.Save();
            }
        }
	}
}
