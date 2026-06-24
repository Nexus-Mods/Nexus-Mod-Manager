namespace Nexus.Client.ModManagement.UI
{
    using System;
    using System.Collections.Generic;
    using Nexus.Client.Mods;
    using Nexus.Client.UI.Controls;
    using Nexus.Client.Util.Collections;

    /// <summary>
    /// The surface that both the legacy <see cref="ModManagerControl"/> and the new
    /// DevExpress-based replacement must expose so that <see cref="MainForm"/>,
    /// <see cref="MainFormVM"/>, and <see cref="ModMigrationTask"/> can use either
    /// implementation without referencing the concrete type.
    /// </summary>
    public interface IModManagerView
    {
        // ── ViewModel ────────────────────────────────────────────────────────

        /// <summary>Gets or sets the view model bound to this view.</summary>
        ModManagerVM ViewModel { get; set; }

        // ── Mod operations ───────────────────────────────────────────────────

        /// <summary>Deactivates (uninstalls) all currently active mods.</summary>
        void DeactivateAllMods(bool forceUninstall, bool silent);

        /// <summary>Deactivates a specific list of mods.</summary>
        void DeactivateAllMods(IList<IMod> mods, bool forceUninstall, bool silent, bool filesOnly);

        /// <summary>Disables all currently active mods (without uninstalling).</summary>
        void DisableAllMods(bool silent);

        // ── UI helpers ───────────────────────────────────────────────────────

        /// <summary>Forces the mod list to repaint/refresh.</summary>
        void ForceListRefresh();

        /// <summary>Resets column widths to their defaults.</summary>
        void ResetColumns();

        /// <summary>Updates the enabled/disabled state of toolbar commands based on the current selection.</summary>
        void SetCommandExecutableStatus();

        /// <summary>Temporarily suppresses the summary panel update (used during background tasks).</summary>
        void ToggleDisabledSummary(bool disabled);

        /// <summary>Applies a text filter to the mod list (called from the main-form search box).</summary>
        void FindItemWithText(string filter);

        /// <summary>Refreshes the Skyrim SE download-mode button text/image.</summary>
        void SetSkyrimDownloadModeFeedback();

        // ── Events raised for MainForm ────────────────────────────────────────

        /// <summary>Raised to request that keyboard focus moves to the search text box.</summary>
        event EventHandler SetTextBoxFocus;

        /// <summary>Raised to request that the search text box is cleared.</summary>
        event EventHandler ResetSearchBox;

        /// <summary>Raised when the total mod count changes.</summary>
        event EventHandler UpdateModsCount;

        /// <summary>Raised when one or more mods should be removed from all profiles.</summary>
        event EventHandler<ModEventArgs> UninstallModFromProfiles;

        /// <summary>Raised after all mods have been uninstalled.</summary>
        event EventHandler UninstalledAllMods;
    }
}
