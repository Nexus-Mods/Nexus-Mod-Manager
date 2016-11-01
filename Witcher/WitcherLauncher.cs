using Nexus.Client.Commands;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nexus.Client.Games.Witcher
{
    public class WitcherLauncher : GameLauncherBase
    {
        #region Constructors

        /// <summary>
        /// A simple constructor that initializes the object with the given dependencies.
        /// </summary>
        /// <param name="p_gmdGameMode">>The game mode currently being managed.</param>
        /// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
        public WitcherLauncher(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo) 
            : base(p_gmdGameMode, p_eifEnvironmentInfo)
        {
        }
        #endregion

        /// <summary>
        /// Initializes the game launch commands.
        /// </summary>
        protected override void SetupCommands()
        {
            Trace.TraceInformation("Launch Commands:");
            Trace.Indent();

            ClearLaunchCommands();

            string strCommand = GetPlainLaunchCommand();
            string strCMIcon = Path.Combine(GameMode.ExecutablePath, "System", "Witcher.exe");
            Trace.TraceInformation("Plain Command: {0} (IsNull={1})", strCommand, (strCommand == null));
            Image imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
            AddLaunchCommand(new Command("PlainLaunch", "Launch The Witcher", "Launches The Witcher.", imgIcon, LaunchGame, true));

            strCommand = GetLauncherLaunchCommand();
            strCMIcon = Path.Combine(GameMode.ExecutablePath, "launcher.exe");
            Trace.TraceInformation("Plain Command: {0} (IsNull={1})", strCommand, (strCommand == null));
            imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
            AddLaunchCommand(new Command("LauncherLaunch", "Launch The Witcher (Launcher)", "Launches The Witcher using the official Launcher.", imgIcon, LaunchLauncher, true));


            DefaultLaunchCommand = new Command("Launch The Witcher", "Launches The Witcher.", LaunchGame);

            Trace.Unindent();
        }

        #region Launch Commands

        #region Vanilla Launch

        /// <summary>
        /// Launches the game.
        /// </summary>
        private void LaunchWitcherPlain()
        {
            Trace.TraceInformation("Launching The Witcher...");
            Trace.Indent();
            string strCommand = GetPlainLaunchCommand();
            Trace.TraceInformation("Command: " + strCommand);
            Launch(strCommand, null);
        }

        /// <summary>
        /// Gets the plain launch command.
        /// </summary>
        /// <returns>The plain launch command.</returns>
        private string GetPlainLaunchCommand()
        {
            string strCommand = Path.Combine(GameMode.ExecutablePath, "System", "Witcher.exe");
            return strCommand;
        }

        /// <summary>
        /// Gets the plain launch command.
        /// </summary>
        /// <returns>The plain launch command.</returns>
        private string GetLauncherLaunchCommand()
        {
            string strCommand = Path.Combine(GameMode.ExecutablePath, "launcher.exe");
            return strCommand;
        }

        #endregion

        /// <summary>
        /// Launches the game, using FOSE if present.
        /// </summary>
        private void LaunchGame()
        {
            //if (!String.IsNullOrEmpty(EnvironmentInfo.Settings.CustomLaunchCommands[GameMode.ModeId]))
            //    LaunchWitcher2Custom();
            //else
                LaunchWitcherPlain();
        }

        private void LaunchLauncher()
        {
            Trace.TraceInformation("Launching The Witcher (launcher)...");
            Trace.Indent();
            string strCommand = GetLauncherLaunchCommand();
            Trace.TraceInformation("Command: " + strCommand);
            Launch(strCommand, null);
        }

        #endregion
    }
}
