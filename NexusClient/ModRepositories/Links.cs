namespace Nexus.Client.ModRepositories
{
    /// <summary>
    /// Links used throughout the application.
    /// </summary>
	public class Links
    {
        private const string DefaultRepository = "Nexus-Mods/Nexus-Mod-Manager";

        private static Links _instance;
        private readonly string _repo;

        private Links()
        {
            _repo = DefaultRepository;

            using (var wc = new System.Net.WebClient())
            {
                try
                {
                    wc.Headers[System.Net.HttpRequestHeader.UserAgent] = ApiCallManager.UserAgent;

                    var redirectedRepository = wc.DownloadString("https://nmm.ahlgren.io/repo").Trim();
                    if (IsValidRepository(redirectedRepository))
                    {
                        _repo = redirectedRepository;
                    }
                    else
                    {
                        System.Diagnostics.Trace.TraceWarning(
                            "Ignoring invalid repository returned by the NMM repository redirect service: '{0}'.",
                            redirectedRepository);
                    }
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Trace.TraceInformation(
                        "Could not query the NMM repository redirect service; using {0}. {1}: {2}",
                        DefaultRepository,
                        ex.GetType().Name,
                        ex.Message);
                }
            }
        }

        private static bool IsValidRepository(string repository)
        {
            return !string.IsNullOrWhiteSpace(repository) &&
                   System.Text.RegularExpressions.Regex.IsMatch(repository, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$");
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
        /// URL to retrieve JSON data for releases from the canonical NMM repository.
        /// </summary>
        public static string DefaultReleasesApi => $"https://api.github.com/repos/{DefaultRepository}/releases";

        /// <summary>
        /// Link to find releases of the application on GitHub.
        /// </summary>
        public string Releases => $"https://github.com/{_repo}/releases";
    }
}
