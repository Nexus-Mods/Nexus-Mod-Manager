namespace Nexus.Client.ModRepositories
{
	public static class NexusLinks
	{
		#region Properties

		public static string FAQs => @"https://forums.nexusmods.com/index.php?/topic/721054-read-here-first-nexus-mod-manager-frequent-issues/";

        public static string Issues => @"https://github.com/Nexus-Mods/Nexus-Mod-Manager/issues";

        public static string NexusMods => @"https://www.nexusmods.com";

        public static string Premium => @"https://www.nexusmods.com/register/premium";

        public static string LatestVersion => @"https://api.github.com/repos/Nexus-Mods/Nexus-Mod-Manager/releases/latest";

        /// <summary>
        /// URL to retrieve JSON data for all available releases on GitHub.
        /// </summary>
        public static string ReleasesJson => @"https://api.github.com/repos/Nexus-Mods/Nexus-Mod-Manager/releases";

        public static string Releases => @"https://github.com/Nexus-Mods/Nexus-Mod-Manager/releases";

        #endregion
	}
}
