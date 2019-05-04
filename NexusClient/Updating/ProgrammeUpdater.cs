namespace Nexus.Client.Updating
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Net;
    using System.Runtime.Serialization;
    using System.Text;
    using System.Threading;
    using System.Windows.Forms;

    using Nexus.Client.ModRepositories;
    using Nexus.Client.Util;
    using Nexus.UI.Controls;

    /// <summary>
    /// Updates the programme.
    /// </summary>
    public class ProgrammeUpdater : UpdaterBase
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
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(string.Format("[tag_name='{0}']", this.tag_name));
                sb.AppendLine(string.Format("[name='{0}']", this.name));

                int index = 0;
                sb.AppendLine(string.Format("[assets count='{0}']", this.assets_info == null ? "0" : this.assets_info.Length.ToString()));
                foreach (var k in this.assets_info)
                {
                    //k.name
                    sb.AppendLine(string.Format("[asset index='{0}']",index));
                    sb.AppendLine(string.Format("[name='{0}']",k.name));
                    sb.AppendLine(string.Format("[name='{0}']",k.browser_download_url));
                    index++;
                }  
                sb.AppendLine(string.Format("[tarball_url='{0}']", this.tarball_url));
                sb.AppendLine(string.Format("[zipball_url='{0}']", this.zipball_url));
                
                
                
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
                this.LatestVersion = "";
                this.LatestVersionUrl = "";             
            }

            public bool GetLatestVersion()
            {
                string data = "";
                if (GetReleaseInformation(out data))
                {
                    if (!ParseReleaseInformation(data))
                    {
                        Console.Error.WriteLine("failed to parse github release information");
                        return false;
                    }
                }
                else
                {
                    Console.Error.WriteLine("failed to get github release information");
                    return false;
                }
                return true;
            }

            private bool GetReleaseInformation(out string data)
            {
                data = "";
                bool ret = false;
                using (ExtendedWebClient wclNewVersion = new ExtendedWebClient(15000))
                {
                    try
                    {
                        data = wclNewVersion.DownloadString(NexusLinks.LatestVersion);
                        ret = true;
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine("GithubReleaseParser::GetReleaseInformation:: error - {0}", e.Message);
                        Console.Error.WriteLine(e.ToString());
                        data = "";
                        ret = false;
                    }
                }

                return ret;
            }
            private bool ParseReleaseInformation(string data)
            {
                bool ret = false;
                try
                {
                    GitHubReleaseJsonContract ghrjc = JSONSerializer.Deserialize<GitHubReleaseJsonContract>(data);
                    if (ghrjc != null)
                    {
                        if (!string.IsNullOrEmpty(ghrjc.tag_name))
                            {
                                this.LatestVersion = ghrjc.tag_name;
                            }
                            if (ghrjc.assets_info != null)
                            {
                                if (ghrjc.assets_info[0] != null)
                                {
                                    if (!string.IsNullOrEmpty(ghrjc.assets_info[0].browser_download_url))
                                    {
                                        this.LatestVersionUrl = ghrjc.assets_info[0].browser_download_url;
                                    }
                                }
                            }
                            ret = ( (this.LatestVersion != "") && (this.LatestVersionUrl != "") );
                    }
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine("GithubReleaseParser::GetReleaseInformation:: error - {0}", e.Message);
                    Console.Error.WriteLine(e.ToString());
                    data = "";
                    ret = false;
                }

                return ret;
            }

            public string LatestVersion { get; private set; }
            public string LatestVersionUrl { get; private set; }
        }        
#endregion
        
#region ExtendedWebClient
        private class ExtendedWebClient : WebClient
        {
            private int m_intDefaultTimeout = 30000;

            public Int32 CustomTimeout
            {
                get
                {
                    return m_intDefaultTimeout;
                }
                set
                {
                    m_intDefaultTimeout = value;
                }
            }

            public ExtendedWebClient() : this(30000) { }

            public ExtendedWebClient(int p_intTimeout)
            {
                this.m_intDefaultTimeout = p_intTimeout;
                this.Headers["User-Agent"] = string.Format("Nexus Client v{0}", CommonData.VersionString);                
                this.Headers["Accept"] = "application/json";
                
            }

            protected override WebRequest GetWebRequest(Uri uri)
            {
                WebRequest w = base.GetWebRequest(uri);
                w.Timeout = m_intDefaultTimeout;
                return w;
            }
        }
#endregion

        protected const string m_strURI = "https://staticdelivery.nexusmods.com/NMM/releasenotes.html";
        private bool m_booIsAutoCheck = false;
        
#region Properties

        /// <summary>
        /// Gets the updater's name.
        /// </summary>
        /// <value>The updater's name.</value>
        public override string Name
        {
            get
            {
                return String.Format("{0} Updater", EnvironmentInfo.Settings.ModManagerName);
            }
        }

        private UpdateManager UpdateManager = null;

#endregion

#region Constructors

        /// <summary>
        /// A simple constructor that initializes the object with the given values.
        /// </summary>
        /// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
        /// <param name="p_booIsAutoCheck">Whether the check is automatic or user requested.</param>
        public ProgrammeUpdater(UpdateManager p_umUpdateManager, IEnvironmentInfo p_eifEnvironmentInfo, bool p_booIsAutoCheck)
            : base(p_eifEnvironmentInfo)
        {
            m_booIsAutoCheck = p_booIsAutoCheck;
            SetRequiresRestart(true);
            UpdateManager = p_umUpdateManager;
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
            SetMessage(String.Format("Checking for new {0} version...", EnvironmentInfo.Settings.ModManagerName));
            string strDownloadUri = String.Empty;
            Version verNew = GetNewProgrammeVersion(out strDownloadUri);
            SetProgress(1);

            if (CancelRequested)
            {
                Trace.Unindent();
                return CancelUpdate();
            }

            if (verNew == new Version("69.69.69.69"))
            {
                SetMessage("Could not get version information from the update server.");
                return false;
            }

            StringBuilder stbPromptMessage = new StringBuilder();
            DialogResult drResult = DialogResult.No;

            string strReleaseNotes = String.Empty;

            if ((verNew > new Version(CommonData.VersionString)) && !String.IsNullOrEmpty(strDownloadUri))
            {
                string strCheckDownloadedInstaller = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", Path.GetFileName(strDownloadUri));
                
                stbPromptMessage.AppendFormat("A new version of {0} is available ({1}).{2}Would you like to download and install it?", EnvironmentInfo.Settings.ModManagerName, verNew, Environment.NewLine).AppendLine();
                stbPromptMessage.AppendLine();
                stbPromptMessage.AppendLine();
                stbPromptMessage.AppendLine("Below you can find the change log for the new release:");

                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(m_strURI);
                    HttpWebResponse response = (HttpWebResponse)request.GetResponse();

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        Stream receiveStream = response.GetResponseStream();
                        StreamReader readStream = null;

                        if (response.CharacterSet == null)
                        {
                            readStream = new StreamReader(receiveStream);
                        }
                        else
                        {
                            readStream = new StreamReader(receiveStream, Encoding.GetEncoding(response.CharacterSet));
                        }

                        strReleaseNotes = readStream.ReadToEnd();

                        response.Close();
                        readStream.Close();
                    }

                }
                catch
                {
                    strReleaseNotes = "Unable to retrieve the Release Notes.";
                }

                try
                {
                    //the extended message box contains an activex control wich must be run in an STA thread,
                    // we can't control what thread this gets called on, so create one if we need to
                    ThreadStart actShowMessage = () => drResult = ExtendedMessageBox.Show(null, stbPromptMessage.ToString(), "New version available", strReleaseNotes, 700, 450, ExtendedMessageBoxButtons.Backup, MessageBoxIcon.Question);
                    ApartmentState astState = ApartmentState.Unknown;
                    Thread.CurrentThread.TrySetApartmentState(astState);
                    if (astState == ApartmentState.STA)
                        actShowMessage();
                    else
                    {
                        Thread thdMessage = new Thread(actShowMessage);
                        thdMessage.SetApartmentState(ApartmentState.STA);
                        thdMessage.Start();
                        thdMessage.Join();
                    }

                }
                catch
                {
                }

                if (drResult == DialogResult.Cancel)
                {
                    Trace.Unindent();
                    return CancelUpdate();
                }

                if (drResult == DialogResult.Yes)
                {
                    UpdateManager.CreateBackup();
                }

                if (File.Exists(strCheckDownloadedInstaller))
                {
                    SetMessage("Launching installer...");
                    ProcessStartInfo psiInfo = new ProcessStartInfo(strCheckDownloadedInstaller);
                    Process.Start(psiInfo);
                    Trace.Unindent();
                    return true;
                }

                SetMessage(String.Format("Downloading new {0} version...", EnvironmentInfo.Settings.ModManagerName));

                string strNewInstaller = string.Empty;
                try
                {
                    strNewInstaller = DownloadFile(new Uri(String.Format(strDownloadUri)));
                }
                catch (FileNotFoundException)
                {
                    StringBuilder stbAVMessage = new StringBuilder();
                    stbAVMessage.AppendLine("Unable to find the installer to download:");
                    stbAVMessage.AppendLine("this could be caused by a network issue or by your Firewall.");
                    stbAVMessage.AppendLine("As a result you won't be able to automatically update the program.");
                    stbAVMessage.AppendLine();
                    stbAVMessage.AppendFormat("You can download the update manually from:");
                    stbAVMessage.AppendLine(NexusLinks.Releases);
                    try
                    {
                        //the extended message box contains an activex control wich must be run in an STA thread,
                        // we can't control what thread this gets called on, so create one if we need to
                        ThreadStart actShowMessage = () => drResult = ExtendedMessageBox.Show(null, stbAVMessage.ToString(), "Unable to update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        ApartmentState astState = ApartmentState.Unknown;
                        Thread.CurrentThread.TrySetApartmentState(astState);
                        if (astState == ApartmentState.STA)
                            actShowMessage();
                        else
                        {
                            Thread thdMessage = new Thread(actShowMessage);
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

                SetProgress(2);

                if (CancelRequested)
                {
                    Trace.Unindent();
                    return CancelUpdate();
                }

                if (!String.IsNullOrEmpty(strNewInstaller))
                {
                    string strOldPath = strNewInstaller;
                    strNewInstaller = Path.Combine(Path.GetTempPath(), Path.GetFileName(strNewInstaller));
                    FileUtil.ForceDelete(strNewInstaller);

                    try
                    {
                        File.Move(strOldPath, strNewInstaller);
                    }
                    catch (FileNotFoundException)
                    {
                        StringBuilder stbAVMessage = new StringBuilder();
                        stbAVMessage.AppendLine("Unable to find the downloaded update:");
                        stbAVMessage.AppendLine("this could be caused by a network issue or by your anti-virus software deleting it falsely flagging the installer as a virus.");
                        stbAVMessage.AppendLine("As a result you won't be able to automatically update the program.");
                        stbAVMessage.AppendLine();
                        stbAVMessage.AppendFormat("To fix this issue you need to add {0}'s executable and all its folders to your", EnvironmentInfo.Settings.ModManagerName).AppendLine();
                        stbAVMessage.AppendLine("anti-virus exception list. You can also download the update manually from:");
                        stbAVMessage.AppendLine(NexusLinks.Releases);

                        try
                        {
                            //the extended message box contains an activex control wich must be run in an STA thread,
                            // we can't control what thread this gets called on, so create one if we need to
                            ThreadStart actShowMessage = () => drResult = ExtendedMessageBox.Show(null, stbAVMessage.ToString(), "Unable to update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            ApartmentState astState = ApartmentState.Unknown;
                            Thread.CurrentThread.TrySetApartmentState(astState);
                            if (astState == ApartmentState.STA)
                                actShowMessage();
                            else
                            {
                                Thread thdMessage = new Thread(actShowMessage);
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
                    ProcessStartInfo psiInfo = new ProcessStartInfo(strNewInstaller);
                    Process.Start(psiInfo);
                    Trace.Unindent();
                    return true;
                }
            }
            else if (!m_booIsAutoCheck)
            {
                stbPromptMessage.AppendFormat("{0} is already up to date.", EnvironmentInfo.Settings.ModManagerName).AppendLine();
                stbPromptMessage.AppendLine();
                stbPromptMessage.AppendLine();
                stbPromptMessage.AppendLine("NOTE: You can find the release notes and past versions here:");
                stbPromptMessage.AppendLine(NexusLinks.Releases);

                try
                {
                    //the extended message box contains an activex control wich must be run in an STA thread,
                    // we can't control what thread this gets called on, so create one if we need to
                    ThreadStart actShowMessage = () => drResult = ExtendedMessageBox.Show(null, stbPromptMessage.ToString(), "Up to date", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    ApartmentState astState = ApartmentState.Unknown;
                    Thread.CurrentThread.TrySetApartmentState(astState);
                    if (astState == ApartmentState.STA)
                        actShowMessage();
                    else
                    {
                        Thread thdMessage = new Thread(actShowMessage);
                        thdMessage.SetApartmentState(ApartmentState.STA);
                        thdMessage.Start();
                        thdMessage.Join();
                    }

                }
                catch
                {
                }
            }

            SetMessage(String.Format("{0} is already up to date.", EnvironmentInfo.Settings.ModManagerName));
            SetProgress(2);
            Trace.Unindent();
            return true;
        }

        /// <summary>
        /// Cancels the update.
        /// </summary>
        /// <remarks>
        /// This is a convience method that allows the setting of the message and
        /// the determination of the return value in one call.
        /// </remarks>
        /// <returns>Always <c>true</c>.</returns>
        private bool CancelUpdate()
        {
            SetMessage(String.Format("Cancelled {0} update.", EnvironmentInfo.Settings.ModManagerName));
            SetProgress(2);
            return true;
        }

        /// <summary>
        /// Gets the newest available programme version.
        /// </summary>
        /// <returns>The newest available programme version,
        /// or 69.69.69.69 if now information could be retrieved.</returns>
        private Version GetNewProgrammeVersion(out string p_strDownloadUri)
        {
            ExtendedWebClient wclNewVersion = new ExtendedWebClient(15000);
            Version verNew = new Version("69.69.69.69");
            p_strDownloadUri = String.Empty;
            try
            {
                GithubReleaseParser release = new GithubReleaseParser();

                if (release.GetLatestVersion())
                {
                    verNew = new Version(release.LatestVersion);
                    p_strDownloadUri = release.LatestVersionUrl;
                    Console.Error.WriteLine("latest version = {0}", verNew.ToString());
                    Console.Error.WriteLine("latest version url = {0}", p_strDownloadUri);
                }
            }
            catch (Exception e)
            {

                Console.Error.WriteLine("ProgrammeUpdater::GetNewProgrammeVersion:: error - {0}", e.Message);
                Console.Error.WriteLine(e.ToString());
                
            }

            return verNew;
        }
    }
}
