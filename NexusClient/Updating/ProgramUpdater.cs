namespace Nexus.Client.Updating
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Windows.Forms;

    using Newtonsoft.Json.Linq;

    using Nexus.Client.ModRepositories;
    using Nexus.Client.Util;
using Nexus.Client.Util.Localization;
    using Nexus.UI.Controls;

    /// <summary>
    /// Updates the program.
    /// </summary>
    public class ProgramUpdater : UpdaterBase
    {
        private readonly bool _isAutomaticCheck;
        private readonly UpdateManager _updateManager;

        private JArray _releases;
        
        /// <summary>
        /// Gets the updater name.
        /// </summary>
        /// <value>The updater name.</value>
        public override string Name => $"{CommonData.ModManagerName} Updater";

        /// <summary>
        /// Gets Releases information from GitHub.
        /// </summary>
        private JArray Releases
        {
            get
            {
                if (_releases == null)
                {
                    var releasesApi = Links.Instance.ReleasesApi;
                    _releases = DownloadReleaseList(releasesApi);

                    // The repository redirect service is optional. If it returns a valid but
                    // unavailable repository, fall back to the canonical NMM repository.
                    if ((_releases == null || _releases.Count == 0) &&
                        !string.Equals(releasesApi, Links.DefaultReleasesApi, StringComparison.OrdinalIgnoreCase))
                    {
                        Trace.TraceWarning(
                            "Could not retrieve usable release information from {0}; retrying the canonical repository.",
                            releasesApi);

                        _releases = DownloadReleaseList(Links.DefaultReleasesApi);
                    }
                }

                return _releases;
            }
        }

        /// <summary>
        /// Downloads and parses a GitHub releases response.
        /// </summary>
        private static JArray DownloadReleaseList(string releasesApi)
        {
            // .NET Framework 4.6.2 can still inherit a legacy TLS protocol set from the
            // machine configuration. GitHub requires TLS 1.2 or newer. Preserve any
            // explicitly enabled protocols and add TLS 1.2 when necessary.
            var securityProtocols = ServicePointManager.SecurityProtocol;
            if (securityProtocols != (SecurityProtocolType)0 &&
                (securityProtocols & SecurityProtocolType.Tls12) == 0)
            {
                ServicePointManager.SecurityProtocol = securityProtocols | SecurityProtocolType.Tls12;
            }

            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = ApiCallManager.UserAgent;
                wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                wc.Headers["X-GitHub-Api-Version"] = "2022-11-28";

                try
                {
                    var releasesRawData = wc.DownloadString(releasesApi);
                    var releases = JArray.Parse(releasesRawData);

                    if (releases.Count == 0)
                    {
                        Trace.TraceWarning("GitHub returned an empty release list from {0}.", releasesApi);
                    }

                    return releases;
                }
                catch (WebException ex)
                {
                    TraceGitHubWebException(ex, releasesApi);
                }
                catch (Exception ex)
                {
                    Trace.TraceError("Could not parse release information returned by {0}.", releasesApi);
                    TraceUtil.TraceException(ex);
                }
            }

            return null;
        }

        /// <summary>
        /// Logs enough information to diagnose updater failures without exposing response content.
        /// </summary>
        private static void TraceGitHubWebException(WebException exception, string releasesApi)
        {
            var response = exception.Response as HttpWebResponse;

            if (response != null)
            {
                Trace.TraceError(
                    "Could not download release information from {0}. HTTP {1} ({2}).",
                    releasesApi,
                    (int)response.StatusCode,
                    response.StatusCode);

                var rateLimitRemaining = response.Headers["X-RateLimit-Remaining"];
                var rateLimitLimit = response.Headers["X-RateLimit-Limit"];
                var rateLimitReset = response.Headers["X-RateLimit-Reset"];

                if (!string.IsNullOrEmpty(rateLimitRemaining) ||
                    !string.IsNullOrEmpty(rateLimitLimit) ||
                    !string.IsNullOrEmpty(rateLimitReset))
                {
                    Trace.TraceWarning(
                        "GitHub rate limit: remaining={0}, limit={1}, reset={2}.",
                        rateLimitRemaining ?? "unknown",
                        rateLimitLimit ?? "unknown",
                        rateLimitReset ?? "unknown");
                }
            }
            else
            {
                Trace.TraceError(
                    "Could not download release information from {0}. WebExceptionStatus: {1}.",
                    releasesApi,
                    exception.Status);
            }

            TraceUtil.TraceException(exception);
        }

        /// <summary>
        /// A simple constructor that initializes the object with the given values.
        /// </summary>
        /// <param name="updateManager">The Update Manager.</param>
        /// <param name="environmentInfo">The applications environment info.</param>
        /// <param name="isAutomaticCheck">Whether the check is automatic or user requested.</param>
        public ProgramUpdater(UpdateManager updateManager, IEnvironmentInfo environmentInfo, bool isAutomaticCheck)
                    : base(environmentInfo)
        {
            _isAutomaticCheck = isAutomaticCheck;
            SetRequiresRestart(true);
            _updateManager = updateManager;

            _releases = null;
        }

        /// <summary>
        /// Performs the update.
        /// </summary>
        /// <returns><c>true</c> if the update completed successfully;
        /// <c>false</c> otherwise.</returns>
        public override bool Update()
        {
            Trace.TraceInformation("Checking for new client version...");
            Trace.Indent();

            SetProgressMaximum(2);
            SetMessage(LanguageManager.Format("Updater.Progress.Checking", "Checking for new {0} version...", CommonData.ModManagerName));
            
            var currentVersion = new Version(CommonData.VersionString);
            var releaseInformation = GetReleaseInformation();

            var newVersion = releaseInformation.Item1;
            var downloadUrl = releaseInformation.Item2;

            SetProgress(1);

            if (CancelRequested)
            {
                Trace.Unindent();
                return CancelUpdate();
            }

            if (newVersion == null || string.IsNullOrEmpty(downloadUrl))
            {
                SetMessage(LanguageManager.Get("Updater.Progress.VersionInfoFailed", "Could not get version information from the update server."));
                return false;
            }

            var dialogResult = DialogResult.No;

            if (newVersion > currentVersion)
            {
                string releaseNotes;
                var checkDownloadedInstaller = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", Path.GetFileName(downloadUrl));

                var promptMessage = LanguageManager.Format(
                    "Updater.NewVersion.Message",
                    "A new version of {0} is available ({1})." + Environment.NewLine +
                    "Would you like to download and install it?" + Environment.NewLine + Environment.NewLine +
                    "Below you can find the change log for the new release:",
                    CommonData.ModManagerName,
                    newVersion);

                try
                {
                    releaseNotes = ConstructChangeLog(currentVersion, newVersion);
                }
                catch
                {
                    releaseNotes = LanguageManager.Get("Updater.NewVersion.ChangeLogUnavailable", "Unable to retrieve change log.");
                }

                DisplayDialog(() => dialogResult = ExtendedMessageBox.Show(null, promptMessage, LanguageManager.Get("Updater.NewVersion.Title", "New version available"), releaseNotes, 700, 450, ExtendedMessageBoxButtons.Backup, MessageBoxIcon.Question));

                switch (dialogResult)
                {
                    case DialogResult.Cancel:
                        Trace.Unindent();
                        return CancelUpdate();
                    case DialogResult.Yes:
                        _updateManager.CreateBackup();
                        break;
                }

                if (File.Exists(checkDownloadedInstaller))
                {
                    SetMessage(LanguageManager.Get("Updater.Progress.LaunchInstaller", "Launching installer..."));
                    var processStartInfo = new ProcessStartInfo(checkDownloadedInstaller);
                    Process.Start(processStartInfo);
                    Trace.Unindent();
                    
                    return true;
                }

                SetMessage(LanguageManager.Format("Updater.Progress.Downloading", "Downloading new {0} version...", CommonData.ModManagerName));

                string newInstaller;

                try
                {
                    newInstaller = DownloadFile(new Uri(string.Format(downloadUrl)));
                }
                catch (FileNotFoundException)
                {
                    var avMessage = LanguageManager.Format(
                        "Updater.Error.InstallerNotFound",
                        "Unable to find the installer to download:" + Environment.NewLine +
                        "This could be caused by a network issue or by your Firewall." + Environment.NewLine + Environment.NewLine +
                        "As a result you won't be able to automatically update the program." + Environment.NewLine + Environment.NewLine +
                        "You can download the update manually from:" + Environment.NewLine + "{0}",
                        Links.Instance.Releases);

                    DisplayDialog(() => dialogResult = ExtendedMessageBox.Show(null, avMessage, LanguageManager.Get("Updater.Error.Title", "Unable to update"), MessageBoxButtons.OK, MessageBoxIcon.Information));

                    Trace.Unindent();
                    return CancelUpdate();
                }

                SetProgress(2);

                if (CancelRequested)
                {
                    Trace.Unindent();
                    return CancelUpdate();
                }

                if (!string.IsNullOrEmpty(newInstaller))
                {
                    var oldPath = newInstaller;
                    newInstaller = Path.Combine(Path.GetTempPath(), Path.GetFileName(newInstaller));
                    FileUtil.ForceDelete(newInstaller);

                    try
                    {
                        File.Move(oldPath, newInstaller);
                    }
                    catch (FileNotFoundException)
                    {
                        var avMessage = LanguageManager.Format(
                            "Updater.Error.DownloadedUpdateMissing",
                            "Unable to find the downloaded update:" + Environment.NewLine +
                            "This could be caused by a network issue or by your anti-virus software deleting it falsely flagging the installer as a virus." + Environment.NewLine +
                            "As a result you won't be able to automatically update the program." + Environment.NewLine + Environment.NewLine +
                            "To fix this issue you need to add {0}'s executable and all its folders to your" + Environment.NewLine +
                            "anti-virus exception list. You can also download the update manually from:" + Environment.NewLine + "{1}",
                            CommonData.ModManagerName,
                            Links.Instance.Releases);

                        DisplayDialog(() => dialogResult = ExtendedMessageBox.Show(null, avMessage, LanguageManager.Get("Updater.Error.Title", "Unable to update"), MessageBoxButtons.OK, MessageBoxIcon.Information));
                        
                        Trace.Unindent();
                        return CancelUpdate();
                    }

                    SetMessage(LanguageManager.Get("Updater.Progress.LaunchInstaller", "Launching installer..."));
                    var psiInfo = new ProcessStartInfo(newInstaller);
                    Process.Start(psiInfo);
                    Trace.Unindent();

                    return true;
                }
            }
            else if (!_isAutomaticCheck)
            {
                var promptMessage = LanguageManager.Format(
                    "Updater.UpToDate.Message",
                    "{0} is already up to date." + Environment.NewLine + Environment.NewLine +
                    "NOTE: You can find the release notes and past versions here:" + Environment.NewLine + "{1}",
                    CommonData.ModManagerName,
                    Links.Instance.Releases);

                DisplayDialog(() => ExtendedMessageBox.Show(null, promptMessage, LanguageManager.Get("Updater.UpToDate.Title", "Up to date"), MessageBoxButtons.OK, MessageBoxIcon.Information));
            }

            SetMessage(LanguageManager.Format("Updater.Progress.UpToDate", "{0} is already up to date.", CommonData.ModManagerName));
            SetProgress(2);
            Trace.Unindent();

            return true;
        }

        private static void DisplayDialog(ThreadStart showMessage)
        {
            try
            {
                // The extended message box contains an ActiveX control which must be run in an STA thread,
                // we can't control what thread this gets called on, so create one if we need to.

                var apartmentState = ApartmentState.Unknown;
                Thread.CurrentThread.TrySetApartmentState(apartmentState);
                
                if (apartmentState == ApartmentState.STA)
                {
                    showMessage();
                }
                else
                {
                    var messageThread = new Thread(showMessage);
                    messageThread.SetApartmentState(ApartmentState.STA);
                    messageThread.Start();
                    messageThread.Join();
                }
            }
            catch {}
        }

        /// <summary>
        /// Cancels the update.
        /// </summary>
        /// <remarks>
        /// This is a convenience method that allows the setting of the message and
        /// the determination of the return value in one call.
        /// </remarks>
        /// <returns>Always <c>true</c>.</returns>
        private bool CancelUpdate()
        {
            SetMessage(LanguageManager.Format("Updater.Progress.Cancelled", "Cancelled {0} update.", CommonData.ModManagerName));
            SetProgress(2);

            return true;
        }

        /// <summary>
        /// Constructs a changelog for all releases between <see cref="currentVersion"/> and <see cref="newVersion"/>.
        /// </summary>
        /// <param name="currentVersion">The currently running version of NMM.</param>
        /// <param name="newVersion">The new version of NMM available for download.</param>
        /// <returns></returns>
        private string ConstructChangeLog(Version currentVersion, Version newVersion)
        {
            if (Releases == null)
            {
                throw new Exception("Could not get Releases info from GitHub.");
            }
            
            var newerVersions = new List<JToken>();
            newerVersions.AddRange(from release in Releases let version = new Version(release["tag_name"].Value<string>()) where version > currentVersion select release);
            
            var changeLog = new StringBuilder($"<html><body><h1>{LanguageManager.Format("Updater.Changelog.ChangesBetween", "Changes between {0} and {1}:", currentVersion, newVersion)}</h1>");

            foreach (var version in newerVersions)
            {
                var body = version["body"].Value<string>().Replace("\r\n", Environment.NewLine);

                var paragraph = new StringBuilder($"<h2>{version["tag_name"].Value<string>()}</h2>");
                
                var newFeaturesRaw = Regex.Match(body, @"\*\*[nN]ew [fF]eatures\*\*(.+)\*\*[bB]", RegexOptions.Singleline).Groups[1].Value;
                var newFeatures = newFeaturesRaw.TrimEnd(' ', '-').Trim(' ', '\r', '\n').Split('\n');

                paragraph.AppendLine($"<h3>{LanguageManager.Get("Updater.Changelog.NewFeatures", "New Features")}</h3><ul>");

                foreach (var feature in newFeatures)
                {
                    paragraph.AppendLine($"<li>{feature.Trim().TrimStart(' ', '-')}</li>");
                }

                var bugFixesRaw = Regex.Match(body, @"\*\*[bB]ugfixes\*\*(.+)", RegexOptions.Singleline).Groups[1].Value;
                var bugFixes = bugFixesRaw.Trim(' ', '\r', '\n').Split('\n');

                paragraph.AppendLine($"</ul><h3>{LanguageManager.Get("Updater.Changelog.BugFixes", "Bug fixes")}</h3><ul>");

                foreach (var bugFix in bugFixes)
                {
                    paragraph.AppendLine($"<li>{bugFix.Trim().TrimStart(' ', '-')}</li>");
                }

                paragraph.AppendLine("</ul>");

                changeLog.AppendLine($"<p>{paragraph}</p>");
                changeLog.AppendLine("<hr />");
            }

            changeLog.Remove(changeLog.Length - 6, 6); // Remove the last <hr />
            changeLog.AppendLine("</body></html>");

            return changeLog.ToString();
        }

        /// <summary>
        /// Get release information.
        /// </summary>
        /// <returns>Version of latest release, and download URL for it.</returns>
        private Tuple<Version, string> GetReleaseInformation()
        {
            if (Releases == null || Releases.Count == 0)
            {
                Trace.TraceError("Could not get version information from the update server: no releases were returned.");
                return new Tuple<Version, string>(null, null);
            }

            Version latestVersion = null;
            string downloadUrl = null;

            foreach (var release in Releases)
            {
                Version releaseVersion;
                string releaseDownloadUrl;

                if (!TryGetReleaseInformation(release, out releaseVersion, out releaseDownloadUrl))
                {
                    continue;
                }

                if (latestVersion == null || releaseVersion > latestVersion)
                {
                    latestVersion = releaseVersion;
                    downloadUrl = releaseDownloadUrl;
                }
            }

            if (latestVersion == null || string.IsNullOrEmpty(downloadUrl))
            {
                Trace.TraceError("Could not get version information from the update server: no valid release with a downloadable asset was found.");
                return new Tuple<Version, string>(null, null);
            }

            Trace.TraceInformation("Latest valid update release is {0} ({1}).", latestVersion, downloadUrl);
            return new Tuple<Version, string>(latestVersion, downloadUrl);
        }

        /// <summary>
        /// Extracts the version and installer URL from a GitHub release.
        /// </summary>
        private static bool TryGetReleaseInformation(JToken release, out Version version, out string downloadUrl)
        {
            version = null;
            downloadUrl = null;

            var releaseObject = release as JObject;
            if (releaseObject == null)
            {
                return false;
            }

            var draft = releaseObject["draft"];
            if (draft != null && draft.Type != JTokenType.Null && draft.Value<bool>())
            {
                return false;
            }

            var tagNameToken = releaseObject["tag_name"];
            var tagName = tagNameToken == null || tagNameToken.Type == JTokenType.Null
                ? null
                : tagNameToken.Value<string>();
            var normalizedTag = string.IsNullOrWhiteSpace(tagName) ? null : tagName.Trim();

            if (!string.IsNullOrEmpty(normalizedTag) &&
                normalizedTag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                normalizedTag = normalizedTag.Substring(1);
            }

            if (string.IsNullOrEmpty(normalizedTag) || !Version.TryParse(normalizedTag, out version))
            {
                Trace.TraceWarning("Skipping GitHub release with invalid tag '{0}'.", tagName ?? "<null>");
                version = null;
                return false;
            }

            var assets = releaseObject["assets"] as JArray;
            if (assets == null || assets.Count == 0)
            {
                Trace.TraceWarning("Skipping GitHub release {0}: it has no downloadable assets.", tagName);
                version = null;
                return false;
            }

            // Prefer an actual Windows installer rather than relying on GitHub asset order.
            downloadUrl = FindDownloadUrl(assets, true) ?? FindDownloadUrl(assets, false);

            if (string.IsNullOrEmpty(downloadUrl))
            {
                Trace.TraceWarning("Skipping GitHub release {0}: no asset has a browser download URL.", tagName);
                version = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Finds a downloadable asset, optionally restricting the result to a Windows installer.
        /// </summary>
        private static string FindDownloadUrl(IEnumerable<JToken> assets, bool installerOnly)
        {
            foreach (var asset in assets)
            {
                var assetObject = asset as JObject;
                if (assetObject == null)
                {
                    continue;
                }

                var urlToken = assetObject["browser_download_url"];
                var url = urlToken == null || urlToken.Type == JTokenType.Null
                    ? null
                    : urlToken.Value<string>();

                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                if (!installerOnly)
                {
                    return url;
                }

                var nameToken = assetObject["name"];
                var assetName = nameToken == null || nameToken.Type == JTokenType.Null
                    ? string.Empty
                    : nameToken.Value<string>() ?? string.Empty;
                if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                    assetName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                {
                    return url;
                }
            }

            return null;
        }
    }
}
