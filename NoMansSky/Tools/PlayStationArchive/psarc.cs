using Nexus.Client.Commands;
using Nexus.Client.Games.Tools;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Nexus.Client.Games.NoMansSky.Tools.PlayStationArchive
{
    /// <summary>
    /// Contains methods for extraction of No Man's Sky PSARC files
    /// </summary>
    public class PSARC : ITool
    {
        #region Events

        public event EventHandler<DisplayToolViewEventArgs> DisplayToolView = delegate { };

        public event EventHandler<DisplayToolViewEventArgs> CloseToolView = delegate { };

        #endregion

        private IToolView m_tvwToolView = null;

        #region Properties

        public Command LaunchCommand { get; private set; }

        protected NoMansSkyGameMode GameMode { get; private set; }

        #endregion

        #region Constructor

        public PSARC(NoMansSkyGameMode p_gmdGameMode)
        {
            GameMode = p_gmdGameMode;
            LaunchCommand = new Command("Extract all pak files", "Cleans the GAMEDATA directory and extracts all pak files", UnpackGameData);
        }

        #endregion

        /// <summary>
        /// Sets the view to use for this tool.
        /// </summary>
        /// <param name="p_tvwToolView">The view to use for this tool.</param>
        public void SetToolView(IToolView p_tvwToolView) => m_tvwToolView = p_tvwToolView;

        public void UnpackGameData()
        {
            String[] staPakFiles = null;
            String strPakFilePath = Path.Combine(GameMode.InstallationPath, "PCBANKS_BAK");

            foreach (string strDirectory in Directory.GetDirectories(GameMode.InstallationPath).Except(new[] { Path.Combine(GameMode.InstallationPath, "PCBANKS"), Path.Combine(GameMode.InstallationPath, "PCBANKS_BAK") }))
                Directory.Delete(strDirectory, true);

            foreach (string strFile in Directory.GetFiles(GameMode.InstallationPath))
                File.Delete(strFile);

            if (!Directory.Exists(strPakFilePath))
            {
                FolderBrowserDialog fbdBankSelectDialog = new FolderBrowserDialog();
                fbdBankSelectDialog.Description = "Select the location of your PCBANKS folder";
                fbdBankSelectDialog.ShowNewFolderButton = false;
                DialogResult drResult = fbdBankSelectDialog.ShowDialog();

                if(drResult == DialogResult.OK)
                {
                    strPakFilePath = fbdBankSelectDialog.SelectedPath;
                }
                else
                {
                    MessageBox.Show("Couldn't find any pak files to extract.");
                    return;
                }
            }

            staPakFiles = Directory.GetFiles(strPakFilePath);

            if (!staPakFiles.Any(s => s.Contains("NMSARC")))
            {
                MessageBox.Show("Couldn't find any No Man's Sky pak files to extract.");
                return;
            }
            

            foreach(String strPakFile in staPakFiles)
            {
                Process prsExtractProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = Path.Combine(Environment.CurrentDirectory, "data", "psarc.exe"),
                        Arguments = "extract --to=\"" + GameMode.InstallationPath + "\" \"" + strPakFile + "\"",
                        UseShellExecute = true,
                        RedirectStandardOutput = false,
                        CreateNoWindow = false
                    }
                };
                prsExtractProcess.Start();
                prsExtractProcess.WaitForExit();
            }

            if(Directory.Exists(Path.Combine(GameMode.InstallationPath, "PCBANKS")))
                Directory.Move(Path.Combine(GameMode.InstallationPath, "PCBANKS"), Path.Combine(GameMode.InstallationPath, "PCBANKS_BAK"));
        }
    }
}
