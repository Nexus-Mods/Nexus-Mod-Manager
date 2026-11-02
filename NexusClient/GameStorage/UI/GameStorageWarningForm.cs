using System;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using Nexus.Client.UI;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.GameStorage.UI
{
    /// <summary>Gives every game the same explicit Continue or Cancel choice for advisory Game Storage findings.</summary>
    internal sealed class GameStorageWarningForm : ManagedFontXtraForm, IView
    {
        /// <summary>Shows the reviewed paths and warnings without modifying saved preferences or existing files.</summary>
        internal GameStorageWarningForm(string message)
        {
            Text = LanguageManager.Get("GameStorage.Warning.Title", "Game Storage warning");
            Width = 790;
            Height = 510;
            MinimumSize = new System.Drawing.Size(600, 350);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            Padding = new Padding(12);
            Controls.Add(new MemoEdit { Dock = DockStyle.Fill, ReadOnly = true, Text = message });
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
            var cancel = new SimpleButton { Text = LanguageManager.Get("Common.Button.Cancel", "Cancel"), DialogResult = DialogResult.Cancel, Width = 110, Height = 30 };
            var proceed = new SimpleButton { Text = LanguageManager.Get("GameStorage.Warning.Continue", "Continue"), DialogResult = DialogResult.OK, Width = 110, Height = 30 };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(proceed);
            Controls.Add(buttons);
            AcceptButton = proceed;
            CancelButton = cancel;
            ActiveControl = cancel;
        }

        /// <summary>Explains the selected game paths and that accepting warnings preserves existing storage metadata.</summary>
        internal static string CreateMessage(GameStoragePathSet paths, GameStorageHealthCheck health)
        {
            return (paths.GameName ?? paths.GameId) + Environment.NewLine +
                LanguageManager.Get("GameDiscovery.InstallationHeading", "INSTALLATION PATH") + ": " + paths.GameInstallPath + Environment.NewLine +
                GameStorageLocalization.GetFolderRoleName(GameStorageFolderRole.InstallInfo) + ": " + paths.InstallInfoPath + Environment.NewLine +
                GameStorageLocalization.GetFolderRoleName(GameStorageFolderRole.Mods) + ": " + paths.ModsPath + Environment.NewLine +
                GameStorageLocalization.GetFolderRoleName(GameStorageFolderRole.VirtualInstall) + ": " + paths.VirtualInstallPath + Environment.NewLine +
                GameStorageLocalization.GetFolderRoleName(GameStorageFolderRole.LinkFolder) + ": " + paths.LinkFolderPath + Environment.NewLine + Environment.NewLine +
                health.ToUserMessage() + Environment.NewLine +
                LanguageManager.Get("GameStorage.Warning.ContinueExplanation", "Game Storage is advisory. Choose Continue to accept these folders, or Cancel to review them. Continue keeps existing mod files, install records and manifests in place. Separate setups managing the same files can conflict during mod installation or removal.");
        }
    }
}
