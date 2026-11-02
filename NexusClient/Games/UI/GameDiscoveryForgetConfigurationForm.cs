using System;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using Nexus.Client.UI;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.Games.UI
{
    /// <summary>Separates forgetting saved paths from optional, potentially slow inspection of empty setup files.</summary>
    internal sealed class GameDiscoveryForgetConfigurationForm : ManagedFontXtraForm, IView
    {
        /// <summary>Shows an immediate choice without reading storage or changing the active game.</summary>
        internal GameDiscoveryForgetConfigurationForm(string gameName)
        {
            Text = LanguageManager.Get("GameDiscovery.ForgetConfiguration", "Forget configuration...");
            Width = 740;
            Height = 310;
            MinimumSize = new System.Drawing.Size(680, 280);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            Padding = new Padding(12);
            Controls.Add(new MemoEdit { Dock = DockStyle.Fill, ReadOnly = true, Text = gameName + Environment.NewLine + Environment.NewLine +
                LanguageManager.Get("GameDiscovery.ForgetConfirm", "Forget this game's saved setup? Game and storage paths, deferred setup changes and startup selection will be cleared. Mod archives, installed files, profiles and install records on disk will be preserved.") +
                Environment.NewLine + Environment.NewLine + LanguageManager.Get("GameDiscovery.ForgetChoiceHint", "Forget configuration clears the saved setup immediately without scanning storage. Choose Review empty setup cleanup to inspect removable empty setup data first.") });
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
            var cancel = new SimpleButton { Text = LanguageManager.Get("Common.Button.Cancel", "Cancel"), DialogResult = DialogResult.Cancel, Width = 100, Height = 32 };
            var inspect = new SimpleButton { Text = LanguageManager.Get("GameDiscovery.ReviewEmptyCleanup", "Review empty setup cleanup..."), DialogResult = DialogResult.No, Width = 240, Height = 32 };
            var forget = new SimpleButton { Text = LanguageManager.Get("GameDiscovery.ForgetOnly", "Forget configuration"), DialogResult = DialogResult.Yes, Width = 170, Height = 32 };
            actions.Controls.Add(cancel);
            actions.Controls.Add(inspect);
            actions.Controls.Add(forget);
            Controls.Add(actions);
            AcceptButton = forget;
            CancelButton = cancel;
            ActiveControl = cancel;
        }
    }
}
