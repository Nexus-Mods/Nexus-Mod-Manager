namespace Nexus.Client.Updating
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Runtime.Serialization;
    using System.Text;
    using System.Threading;
    using System.Windows.Forms;

    using Nexus.Client.ModRepositories;
    using Nexus.Client.Util;
    using Nexus.UI.Controls;

    /// <summary>
    /// Updates the program.
    /// </summary>
    public class ProgramUpdater : UpdaterBase
    {
        #region  GitHubReleaseJsonContract
        /* These classes (contracts) should be based on the format of the json from:
            https://api.github.com/repos/Nexus-Mods/Nexus-Mod-Manager/releases
        */

        [DataContract]
        private class GitHubReleaseJsonContract
        {
            [DataMember(Name = "url")]
            public string url { get; set; }
            [DataMember(Name = "assets_url")]
            public string assets_url { get; set; }
            [DataMember(Name = "upload_url")]
            public string upload_url { get; set; }
            [DataMember(Name = "html_url")]
            public string html_url { get; set; }
            [DataMember(Name = "id")]
            public int id { get; set; }
            [DataMember(Name = "node_id")]
            public string node_id { get; set; }
            [DataMember(Name = "tag_name")]
            public string tag_name { get; set; }
            [DataMember(Name = "target_commitish")]
            public string target_commitish { get; set; }
            [DataMember(Name = "name")]
            public string name { get; set; }
            [DataMember(Name = "draft")]
            public bool draft { get; set; }

            [DataMember(Name = "author")]
            public author_information author_info { get; set; }
            
            [DataMember(Name = "prerelease")]
            public bool prerelease { get; set; }
            [DataMember(Name = "created_at")]
            public string created_at { get; set; }
            [DataMember(Name = "published_at")]
            public string published_at { get; set; }
            
            [DataMember(Name = "assets")]
            public assets_information[] assets_info { get; set; }
            
            [DataMember(Name = "tarball_url")]
            public string tarball_url { get; set; }
            [DataMember(Name = "zipball_url")]
            public string zipball_url { get; set; }
            [DataMember(Name = "body")]
            public string body { get; set; }
            
            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"[tag_name='{this.tag_name}']");
                sb.AppendLine($"[name='{this.name}']");

                var index = 0;
                sb.AppendLine(
                    $"[assets count='{(this.assets_info == null ? "0" : this.assets_info.Length.ToString())}']");
                foreach (var k in this.assets_info)
                {
                    //k.name
                    sb.AppendLine($"[asset index='{index}']");
                    sb.AppendLine($"[name='{k.name}']");
                    sb.AppendLine($"[name='{k.browser_download_url}']");
                    index++;
                }  
                sb.AppendLine($"[tarball_url='{this.tarball_url}']");
                sb.AppendLine($"[zipball_url='{this.zipball_url}']");
                
                
                
                return sb.ToString();
            }

        }
        [DataContract]
        private class author_information
        {
            [DataMember(Name = "login")]
            public string login { get; set; }
            [DataMember(Name = "id")]
            public int id { get; set; }
            [DataMember(Name = "node_id")]
            public string node_id { get; set; }
            [DataMember(Name = "avatar_url")]
            public string avatar_url { get; set; }
            [DataMember(Name = "gravatar_id")]
            public string gravatar_id { get; set; }
            [DataMember(Name = "url")]
            public string url { get; set; }
            [DataMember(Name = "html_url")]
            public string html_url { get; set; }
            [DataMember(Name = "followers_url")]
            public string followers_url { get; set; }
            [DataMember(Name = "following_url")]
            public string following_url { get; set; }
            [DataMember(Name = "gists_url")]
            public string gists_url { get; set; }
            [DataMember(Name = "starred_url")]
            public string starred_url { get; set; }
            [DataMember(Name = "subscriptions_url")]
            public string subscriptions_url { get; set; }
            [DataMember(Name = "organizations_url")]
            public string organizations_url { get; set; }
            [DataMember(Name = "repos_url")]
            public string repos_url { get; set; }
            [DataMember(Name = "events_url")]
            public string events_url { get; set; }
            [DataMember(Name = "received_events_url")]
            public string received_events_url { get; set; }
            [DataMember(Name = "type")]
            public string type { get; set; }
            [DataMember(Name = "site_admin")]
            public bool site_admin { get; set; }
        }
        [DataContract]
        private class assets_information
        {
            [DataMember(Name = "url")]
            public string url { get; set; }
            [DataMember(Name = "id")]
            public int id { get; set; }
            [DataMember(Name = "node_id")]
            public string node_id { get; set; }
            [DataMember(Name = "name")]
            public string name { get; set; }
            [DataMember(Name = "label")]
            public string label { get; set; }
            
            [DataMember(Name = "uploader")]
            public author_information uploader_info { get; set; }
            
            [DataMember(Name = "content_type")]
            public string content_type { get; set; }
            [DataMember(Name = "state")]
            public string state { get; set; }
            [DataMember(Name = "download_count")]
            public int download_count { get; set; }
            [DataMember(Name = "created_at")]
            public string created_at { get; set; }
            [DataMember(Name = "published_at")]
            public string published_at { get; set; }
            [DataMember(Name = "browser_download_url")]
            public string browser_download_url { get; set; }
        }
        #endregion

        #region  GithubReleaseParser

        private class GithubReleaseParser
        {
            public GithubReleaseParser()
            {
                LatestVersion = "";
                LatestVersionUrl = "";             
            }

            public bool GetLatestVersion()
            {
                if (GetReleaseInformation(out var data))
                {
                    if (!ParseReleaseInformation(data))
                    {
                        Trace.TraceWarning("Failed to parse github release information.");
                        return false;
                    }
                }
                else
                {
                    Trace.TraceWarning("Failed to get github release information.");
                    return false;
                }
                
                return true;
            }

            private bool GetReleaseInformation(out string data)
            {
                data = "";
                
                bool result;
                
                using (var wclNewVersion = new ExtendedWebClient(15000))
                {
                    try
                    {
                        data = wclNewVersion.DownloadString(NexusLinks.LatestVersion);
                        result = true;
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine("GithubReleaseParser::GetReleaseInformation:: error - {0}", e.Message);
                        Console.Error.WriteLine(e.ToString());
                        
                        data = "";
                        result = false;
                    }
                }

                return result;
            }

            private bool ParseReleaseInformation(string data)
            {
                var result = false;
                
                try
                {
                    var ghrjc = JSONSerializer.Deserialize<GitHubReleaseJsonContract>(data);
                    
                    if (ghrjc != null)
                    {
                        if (!string.IsNullOrEmpty(ghrjc.tag_name))
                        {
                            LatestVersion = ghrjc.tag_name;
                        }

                        if (ghrjc.assets_info?[0] != null)
                        {
                            if (!string.IsNullOrEmpty(ghrjc.assets_info[0].browser_download_url))
                            {
                                LatestVersionUrl = ghrjc.assets_info[0].browser_download_url;
                            }
                        }
                        
                        result = !string.IsNullOrEmpty(LatestVersion) && !string.IsNullOrEmpty(LatestVersionUrl);
                    }
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine("GithubReleaseParser::GetReleaseInformation:: error - {0}", e.Message);
                    Console.Error.WriteLine(e.ToString());
                    result = false;
                }

                return result;
            }

            public string LatestVersion { get; private set; }

            public string LatestVersionUrl { get; private set; }
        }    
        
        #endregion
        
        #region ExtendedWebClient
        
        private class ExtendedWebClient : WebClient
        {
            private int CustomTimeout { get; }

            public ExtendedWebClient() : this(30000) { }

            public ExtendedWebClient(int timeout)
            {
                CustomTimeout = timeout;
                Headers["User-Agent"] = ApiCallManager.UserAgent;                
                Headers["Accept"] = "application/json";
            }

            protected override WebRequest GetWebRequest(Uri uri)
            {
                var webRequest = base.GetWebRequest(uri);

                if (webRequest == null)
                {
                    Trace.TraceError("ExtendedWebClient: Could not set custom timeout on WebRequest.");
                }
                else
                {
                    webRequest.Timeout = CustomTimeout;
                }

                return webRequest;
            }
        }

        #endregion

        protected const string ReleaseNotesUri = "https://staticdelivery.nexusmods.com/NMM/releasenotes.html";
        private readonly bool _isAutomaticCheck;
        private readonly UpdateManager _updateManager;

        #region Properties

        /// <summary>
        /// Gets the updater name.
        /// </summary>
        /// <value>The updater name.</value>
        public override string Name => $"{EnvironmentInfo.Settings.ModManagerName} Updater";

        #endregion

        #region Constructors

        /// <summary>
        /// A simple constructor that initializes the object with the given values.
        /// </summary>
        /// <param name="updateManager"></param>
        /// <param name="environmentInfo">The application's environment info.</param>
        /// <param name="isAutomaticCheck">Whether the check is automatic or user requested.</param>
        public ProgramUpdater(UpdateManager updateManager, IEnvironmentInfo environmentInfo, bool isAutomaticCheck)
                    : base(environmentInfo)
        {
            _isAutomaticCheck = isAutomaticCheck;
            SetRequiresRestart(true);
            _updateManager = updateManager;
        }

        #endregion

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
            SetMessage($"Checking for new {EnvironmentInfo.Settings.ModManagerName} version...");
            var currentVersion = new Version(CommonData.VersionString);
            var newVersion = GetNewProgramVersion(out var downloadUri);
            SetProgress(1);

            if (CancelRequested)
            {
                Trace.Unindent();
                return CancelUpdate();
            }

            if (newVersion == null)
            {
                SetMessage("Could not get version information from the update server.");
                return false;
            }

            var promptMessage = new StringBuilder();
            var dialogResult = DialogResult.No;

            var releaseNotes = string.Empty;

            if (newVersion > currentVersion && !string.IsNullOrEmpty(downloadUri))
            {
                var checkDownloadedInstaller = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", Path.GetFileName(downloadUri));
                
                promptMessage.AppendFormat("A new version of {0} is available ({1}).{2}Would you like to download and install it?", EnvironmentInfo.Settings.ModManagerName, newVersion, Environment.NewLine).AppendLine();
                promptMessage.AppendLine();
                promptMessage.AppendLine();
                promptMessage.AppendLine("Below you can find the change log for the new release:");

                try
                {
                    releaseNotes = ConstructChangeLog(currentVersion, newVersion);
                }
                 catch
                {
                    releaseNotes = "Unable to retrieve the Release Notes.";
                }

                try
                {
                    // The extended message box contains an ActiveX control which must be run in an STA thread,
                    // we can't control what thread this gets called on, so create one if we need to.
                    ThreadStart showMessage = () => dialogResult = ExtendedMessageBox.Show(null, promptMessage.ToString(), "New version available", releaseNotes, 700, 450, ExtendedMessageBoxButtons.Backup, MessageBoxIcon.Question);
                    var apartmentState = ApartmentState.Unknown;
                    Thread.CurrentThread.TrySetApartmentState(apartmentState);
                    
                    if (apartmentState == ApartmentState.STA)
                    {
                        showMessage();
                    }
                    else
                    {
                        var message = new Thread(showMessage);
                        message.SetApartmentState(ApartmentState.STA);
                        message.Start();
                        message.Join();
                    }

                }
                catch
                {
                }

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
                    SetMessage("Launching installer...");
                    var psiInfo = new ProcessStartInfo(checkDownloadedInstaller);
                    Process.Start(psiInfo);
                    Trace.Unindent();
                    
                    return true;
                }

                SetMessage($"Downloading new {EnvironmentInfo.Settings.ModManagerName} version...");

                string newInstaller;

                try
                {
                    newInstaller = DownloadFile(new Uri(string.Format(downloadUri)));
                }
                catch (FileNotFoundException)
                {
                    var stbAVMessage = new StringBuilder();
                    stbAVMessage.AppendLine("Unable to find the installer to download:");
                    stbAVMessage.AppendLine("this could be caused by a network issue or by your Firewall.");
                    stbAVMessage.AppendLine("As a result you won't be able to automatically update the program.");
                    stbAVMessage.AppendLine();
                    stbAVMessage.AppendFormat("You can download the update manually from:");
                    stbAVMessage.AppendLine(NexusLinks.Releases);
                    
                    try
                    {
                        // The extended message box contains an ActiveX control which must be run in an STA thread,
                        // we can't control what thread this gets called on, so create one if we need to.
                        ThreadStart showMessage = () => dialogResult = ExtendedMessageBox.Show(null, stbAVMessage.ToString(), "Unable to update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        var astState = ApartmentState.Unknown;
                        Thread.CurrentThread.TrySetApartmentState(astState);
                        
                        if (astState == ApartmentState.STA)
                        {
                            showMessage();
                        }
                        else
                        {
                            var message = new Thread(showMessage);
                            message.SetApartmentState(ApartmentState.STA);
                            message.Start();
                            message.Join();
                        }

                    }
                    catch
                    {
                    }

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
                        var stbAVMessage = new StringBuilder();
                        stbAVMessage.AppendLine("Unable to find the downloaded update:");
                        stbAVMessage.AppendLine("this could be caused by a network issue or by your anti-virus software deleting it falsely flagging the installer as a virus.");
                        stbAVMessage.AppendLine("As a result you won't be able to automatically update the program.");
                        stbAVMessage.AppendLine();
                        stbAVMessage.AppendFormat("To fix this issue you need to add {0}'s executable and all its folders to your", EnvironmentInfo.Settings.ModManagerName).AppendLine();
                        stbAVMessage.AppendLine("anti-virus exception list. You can also download the update manually from:");
                        stbAVMessage.AppendLine(NexusLinks.Releases);

                        try
                        {
                            // The extended message box contains an ActiveX control which must be run in an STA thread,
                            // we can't control what thread this gets called on, so create one if we need to.
                            ThreadStart actShowMessage = () => dialogResult = ExtendedMessageBox.Show(null, stbAVMessage.ToString(), "Unable to update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            var astState = ApartmentState.Unknown;
                            Thread.CurrentThread.TrySetApartmentState(astState);
                            if (astState == ApartmentState.STA)
                                actShowMessage();
                            else
                            {
                                var thdMessage = new Thread(actShowMessage);
                                thdMessage.SetApartmentState(ApartmentState.STA);
                                thdMessage.Start();
                                thdMessage.Join();
                            }

                        }
                        catch
                        {
                        }

                        Trace.Unindent();
                        return CancelUpdate();
                    }

                    SetMessage("Launching installer...");
                    var psiInfo = new ProcessStartInfo(newInstaller);
                    Process.Start(psiInfo);
                    Trace.Unindent();
                    return true;
                }
            }
            else if (!_isAutomaticCheck)
            {
                promptMessage.AppendFormat("{0} is already up to date.", EnvironmentInfo.Settings.ModManagerName).AppendLine();
                promptMessage.AppendLine();
                promptMessage.AppendLine();
                promptMessage.AppendLine("NOTE: You can find the release notes and past versions here:");
                promptMessage.AppendLine(NexusLinks.Releases);

                try
                {
                    // The extended message box contains an ActiveX control which must be run in an STA thread,
                    // we can't control what thread this gets called on, so create one if we need to.
                    ThreadStart showMessage = () => dialogResult = ExtendedMessageBox.Show(null, promptMessage.ToString(), "Up to date", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    var astState = ApartmentState.Unknown;
                    Thread.CurrentThread.TrySetApartmentState(astState);
                    
                    if (astState == ApartmentState.STA)
                    {
                        showMessage();
                    }
                    else
                    {
                        var thdMessage = new Thread(showMessage);
                        thdMessage.SetApartmentState(ApartmentState.STA);
                        thdMessage.Start();
                        thdMessage.Join();
                    }

                }
                catch
                {
                }
            }

            SetMessage($"{EnvironmentInfo.Settings.ModManagerName} is already up to date.");
            SetProgress(2);
            Trace.Unindent();

            return true;
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
            SetMessage($"Cancelled {EnvironmentInfo.Settings.ModManagerName} update.");
            SetProgress(2);

            return true;
        }

        /// <summary>
        /// Constructs a changelog for all releases between <see cref="currentVersion"/> and <see cref="newVersion"/>.
        /// </summary>
        /// <param name="currentVersion">The currently running version of NMM.</param>
        /// <param name="newVersion">The new version of NMM available for download.</param>
        /// <returns></returns>
        private static string ConstructChangeLog(Version currentVersion, Version newVersion)
        {
            var newerVersions = new List<GitHubReleaseJsonContract>();

            using (var wc = new ExtendedWebClient(15000))
            {
                try
                {
                    var releasesRawData = wc.DownloadString(NexusLinks.ReleasesJson);
                    var releases = JSONSerializer.Deserialize<List<GitHubReleaseJsonContract>>(releasesRawData);

                    newerVersions.AddRange(from release in releases let version = new Version(release.tag_name) where version > currentVersion select release);
                }
                catch (Exception ex)
                {
                    Trace.TraceError("Could not download release information from GitHub.");
                    TraceUtil.TraceException(ex);
                    return null;
                }
            }

            var changeLog = new StringBuilder($"<html><body><h1>Changes included in update to {newVersion}:</h1>");

            foreach (var version in newerVersions)
            {
                var body = version.body;

                try
                {
                    body = body.Substring(body.IndexOf("###"));
                }
                catch {}

                if (body.StartsWith("###"))
                {
                    body = body.Substring(4);
                    body = body.Insert(0, "<h2>");
                    
                    body = body.Insert(body.IndexOf('\r'), "</h2>");
                }

                body = body.Replace("\r\n", "");
                body = body.Replace("- **Bugfixes**", "<h3>Bugfixes</h3>");
                body = body.Replace("- **New features**", "<h3>New features</h3>");

                // TODO: Make the list of items prettier, this is adding extra line breaks after the H3 elements.
                body = body.Replace("- ", "<br />* ");

                changeLog.AppendLine($"<p>{body.Trim()}</p>");
            }

            changeLog.AppendLine("</body></html>");

            return changeLog.ToString();
        }

        /// <summary>
        /// Gets the newest available program version.
        /// </summary>
        /// <returns>The newest available program version,
        /// or null if no information could be retrieved.</returns>
        private static Version GetNewProgramVersion(out string downloadUri)
        {
            Version newVersion = null;
            downloadUri = string.Empty;
            
            try
            {
                var release = new GithubReleaseParser();

                if (release.GetLatestVersion())
                {
                    newVersion = new Version(release.LatestVersion);
                    downloadUri = release.LatestVersionUrl;
                    Console.Error.WriteLine("latest version = {0}", newVersion.ToString());
                    Console.Error.WriteLine("latest version url = {0}", downloadUri);
                }
            }
            catch (Exception e)
            {

                Console.Error.WriteLine("ProgramUpdater::GetNewProgramVersion:: error - {0}", e.Message);
                Console.Error.WriteLine(e.ToString());
                
            }

            return newVersion;
        }
    }
}
