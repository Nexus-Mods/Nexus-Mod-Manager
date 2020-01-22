namespace Nexus.Client.ModRepositories
{
    /// <summary>
    /// Links used throughout the application.
    /// </summary>
	public class Links
    {
        private static Links _instance;
        private readonly string _repo;

        private Links()
        {
            _repo = "Nexus-Mods/Nexus-Mod-Manager";

            using (var wc = new System.Net.WebClient())
            {
                try
                {
                    _repo = wc.DownloadString("https://nmm.ahlgren.io/repo").Trim();
                }
                catch {}
            }
        }

        /// <summary>
        /// Gets a singleton instance of <see cref="Links"/>.
        /// </summary>
        public static Links Instance => _instance ?? (_instance = new Links());

        /// <summary>
        /// Link to Frequently Asked Questions on the Nexus Forums.
        /// </summary>
        public static string FAQs => "https://forums.nexusmods.com/index.php?/topic/721054-read-here-first-nexus-mod-manager-frequent-issues/";

        /// <summary>
        /// Link to GitHub repository 
        /// </summary>
        public string Issues => $"https://github.com/{_repo}/issues";

        /// <summary>
        /// Link to the Nexus Mods website.
        /// </summary>
        public static string NexusMods => "https://www.nexusmods.com";

        /// <summary>
        /// Link to register for a Premium account at Nexus Mods.
        /// </summary>
        public static string Premium => "https://www.nexusmods.com/register/premium";

        /// <summary>
        /// URL to retrieve JSON data for all available releases on GitHub.
        /// </summary>
        public string ReleasesApi => $"https://api.github.com/repos/{_repo}/releases";

        /// <summary>
        /// Link to find releases of the application on GitHub.
        /// </summary>
        public string Releases => $"https://github.com/{_repo}/releases";
    }
}
