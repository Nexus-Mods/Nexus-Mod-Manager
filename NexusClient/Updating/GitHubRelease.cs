namespace Nexus.Client.Updating
{
    using System;
    using System.Diagnostics;
    using System.Net;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Represents information about a GitHub release.
    /// </summary>
    public class GitHubRelease
    {
        /// <summary>
        /// Creates a GitHubRelease object with information from given API call.
        /// </summary>
        /// <param name="apiCall">API call to make for this GitHub release.</param>
        public GitHubRelease(string apiCall)
        {
            try
            {
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "N/A");
                    var content = wc.DownloadString(apiCall);

                    Version = ParseVersion(content);
                    DownloadLink = ParseDownloadLink(content);
                }
            }
            catch (WebException e)
            {
                Trace.TraceError("Could not get version information: {0}", e.Message);
                Version = new Version(69, 69, 69, 69);
                DownloadLink = string.Empty;
            }
        }

        #region Properties

        /// <summary>
        /// Link to information about the latest release on GitHub.
        /// </summary>
        public static string LatestRelease
        {
            get
            {
                return "https://api.github.com/repos/Nexus-Mods/Nexus-Mod-Manager/releases/latest";
            }
        }

        /// <summary>
        /// Version of the latest release.
        /// </summary>
        public Version Version { get; private set; }

        /// <summary>
        /// URL to the installer file of the latest release.
        /// </summary>
        public string DownloadLink { get; private set; }

        #endregion
        
        private static Version ParseVersion(string content)
        {
            var releaseVersion = Regex.Match(content, "tag_name\":\"(\\d+\\.\\d+\\.\\d)\"", RegexOptions.IgnoreCase).Groups[1];
            var tmp = releaseVersion.Value.Split('.');

            return new Version(Convert.ToInt32(tmp[0]), Convert.ToInt32(tmp[1]), Convert.ToInt32(tmp[2]));
        }

        private static string ParseDownloadLink(string content)
        {
            return Regex.Match(content, "browser_download_url\":\"(.*\\.exe)\"", RegexOptions.IgnoreCase).Groups[1].Value;
        }
    }
}
