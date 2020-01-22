namespace Nexus.Client.Updating
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Threading;
    using System.Windows.Forms;

    using Newtonsoft.Json.Linq;

    using Nexus.Client.ModRepositories;
    using Nexus.Client.Util;
    using Nexus.UI.Controls;

    /// <summary>
    /// Updates the program.
    /// </summary>
    public class ProgramUpdater : UpdaterBase
    {
        private readonly bool _isAutomaticCheck;
        private readonly UpdateManager _updateManager;
        
        /// <summary>
        /// Gets the updater name.
        /// </summary>
        /// <value>The updater name.</value>
        public override string Name => $"{EnvironmentInfo.Settings.ModManagerName} Updater";

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
            var (newVersion, downloadUrl) = GetReleaseInformation();

            SetProgress(1);

            if (CancelRequested)
            {
                Trace.Unindent();
                return CancelUpdate();
            }

            if (newVersion == null || string.IsNullOrEmpty(downloadUrl))
            {
                SetMessage("Could not get version information from the update server.");
                return false;
            }

            var promptMessage = new StringBuilder();
            var dialogResult = DialogResult.No;

            if (newVersion > currentVersion)
            {
                string releaseNotes;
                var checkDownloadedInstaller = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", Path.GetFileName(downloadUrl));
                
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
                    releaseNotes = "Unable to retrieve change log.";
                }

                DisplayDialog(() => dialogResult = ExtendedMessageBox.Show(null, promptMessage.ToString(), "New version available", releaseNotes, 700, 450, ExtendedMessageBoxButtons.Backup, MessageBoxIcon.Question));

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
                    newInstaller = DownloadFile(new Uri(string.Format(downloadUrl)));
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
                    
                    DisplayDialog(() => dialogResult = ExtendedMessageBox.Show(null, stbAVMessage.ToString(), "Unable to update", MessageBoxButtons.OK, MessageBoxIcon.Information));

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

                        DisplayDialog(() => dialogResult = ExtendedMessageBox.Show(null, stbAVMessage.ToString(), "Unable to update", MessageBoxButtons.OK, MessageBoxIcon.Information));
                        
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

                DisplayDialog(() => ExtendedMessageBox.Show(null, promptMessage.ToString(), "Up to date", MessageBoxButtons.OK, MessageBoxIcon.Information));
            }

            SetMessage($"{EnvironmentInfo.Settings.ModManagerName} is already up to date.");
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
            var newerVersions = new List<JToken>();

            using (var wc = new WebClient())
            {
                wc.Headers["User-Agent"] = ApiCallManager.UserAgent;
                wc.Headers["Accept"] = "application/json";

                try
                {
                    var releasesRawData = wc.DownloadString(NexusLinks.ReleasesJson);
                    var releases = JArray.Parse(releasesRawData);

                    // Select releases that have a version above the current one.
                    newerVersions.AddRange(from release in releases let version = new Version(release["tag_name"].Value<string>()) where version > currentVersion select release);
                }
                catch (Exception ex)
                {
                    Trace.TraceError("Could not download release information from GitHub.");
                    TraceUtil.TraceException(ex);
                    return null;
                }
            }

            // Construct changelog from filtered releases.

            var changeLog = new StringBuilder($"<html><body><h1>Changes between {currentVersion} and {newVersion}:</h1>");

            foreach (var version in newerVersions)
            {
                var body = version["body"].Value<string>();

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
        /// Get release information.
        /// </summary>
        /// <returns>Version of latest release, and download URL for it.</returns>
        private static (Version LatestVersion, string DownloadUrl) GetReleaseInformation()
        {
            Version latestVersion = null;
            string downloadUrl = null;

            using (var wc = new WebClient())
            {
                wc.Headers["User-Agent"] = ApiCallManager.UserAgent;
                wc.Headers["Accept"] = "application/json";

                try
                {
                    var data = wc.DownloadString(NexusLinks.LatestVersion);

                    var latestReleaseInfo = JToken.Parse(data);

                    if (latestReleaseInfo != null)
                    {
                        latestVersion = new Version(latestReleaseInfo["tag_name"].Value<string>());
                        downloadUrl = latestReleaseInfo["assets"][0]["browser_download_url"].Value<string>();
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceError("Failed to retrieve latest release version info.");
                    TraceUtil.TraceException(ex);
                }
            }

            return (latestVersion, downloadUrl);
        }
    }
}
