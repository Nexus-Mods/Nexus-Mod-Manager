using System.Windows.Forms;
using Nexus.Client.UI;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.Games.DataDriven
{
    /// <summary>One compiled release decision shared by startup and direct factory construction.</summary>
    internal static class Witcher3RemasteredReleasePolicy
    {
        /// <summary>Remains closed until the bounded native workflow has real-installation runtime evidence.</summary>
        internal static bool NativeActivationApproved => false;

        /// <summary>Explains the outstanding release evidence without implying optional features must be implemented first.</summary>
        internal static ViewMessage CreateActivationBlockMessage()
        {
            return new ViewMessage(
                LanguageManager.Get("Witcher3.Release.ActivationPending.Message",
                    "The Witcher 3 Remastered mode is discoverable, but activation is disabled until its basic install, upgrade, removal and recovery workflows have been validated with a real game installation. This build cannot manage Remastered mods yet. Remastered load-order editing remains disabled separately."),
                null,
                LanguageManager.Get("Witcher3.Release.ActivationPending.Title", "Witcher 3 Remastered - runtime validation pending"),
                MessageBoxIcon.Information);
        }
    }
}
