# NMM Community Edition release notes

## Version 0.93.3

- Bugfix: Fixed issue preventing W3R mods containing XML files from being installed through normal install methods (it worked through collections).
- Bugfix: Fixed mod installs failing with an unrelated Collections message.

## Version 0.93.2

- New Feature: Added Nexus Collections support.
- New Feature: Overhauled Nexus Mods API integration with dedicated REST and GraphQL clients, improved authentication, quota tracking, and error handling.
- New Feature: Added “Full” update checks to find mod updates beyond the monthly window (warning it may use a lot of hourly requests based on your mod list).
- New Feature: Added the Direct Install method, restoring the classic vanilla mod installation workflow. Direct and Virtual Install methods can coexist within the same NMM installation, with no migration required for existing mods.
- New Feature: Completely overhauled the Scripted Installer system.
- New Feature: Added a new Sort column, allowing you to assign custom numeric values to mods and sort them in any order you prefer.
- New Feature: Upgraded NMM and its installer requirements to .NET Framework 4.8.
- New Feature: Added Local Collections to save and restore managed setups, including retained archives and supported file, plugin, and configuration state.
- New Feature: Moved installation-log persistence to SQLite while retaining legacy migration, backup, and profile compatibility.
- New Feature: Added persistent Uninstall Date column to the Mods views.
- New Feature: Added progress counter to the File Manager loading screen.
- New Feature: Added Witcher 3 Remastered support.
- New Feature: Added new Game Discovery window embedded in the main UI.
- Bugfix: Removed unused dll game modes.
- Bugfix: Improved support for long paths to the File Manager.
- Bugfix: Reduced API requests usage.
- Bugfix: Optimized virtual-install persistence with incremental database updates and fewer full-store rewrites.
- Bugfix: Improved archive extraction and large-file streaming, including solid archives with deeply nested paths.
- Bugfix: Reduced metadata-refresh overhead by avoiding unnecessary screenshot loading and archive reads.
- Bugfix: Improved download throughput and reduced disk overhead when saving resumable download progress.
- Bugfix: Improved download retry handling, cancellation, pausing, and download-list cleanup.
- Bugfix: Improved Steam game detection across multiple libraries, renamed installation folders, and stale registry entries.
- Bugfix: Improved game-detection and download-monitor responsiveness during cancellation, shutdown, and game switching.
- Bugfix: Improved reinstall, upgrade, and uninstall handling for obsolete files, broken links, locked files, and original-file restoration.
- Bugfix: Improved transaction rollback so one restoration failure does not prevent other files from being recovered.
- Bugfix: Improved Game Storage validation and recovery when adopting new folder locations.
- Bugfix: Added “Remove all untracked files” to File Manager for supported games, with confirmation and ownership rechecks.
- Bugfix: Added clearer endorsement icons and fixed its management.
- Bugfix: Improved mod-deletion confirmation with explicit actions and themed warning graphics.
- Bugfix: Improved ENB archive-root detection and added automatic Fallout 4 xSE PluginPreloader package recognition.
- Bugfix: Updated Skyrim SE executable detection and bundled Skyrim/Fallout 4 official-content definitions.
- Bugfix: Improved metadata persistence, including endorsement states, and recognition of newer Nexus archive filenames.
- Bugfix: Updated English UI coverage and synchronized French, German, Spanish, Turkish, and Simplified Chinese translations.
- Bugfix: Fixed plugin activation for mods containing multiple plugins with interdependent master/dependency relationships.

## Version 0.92.14

- Bugfix: Fixed Category View jumping all over the place when selecting a mod.
- Bugfix: Fixed “Focus top row on Install Date changes” to work only when the view is sorted by “Install Date”.

## Version 0.92.13

- Bugfix: Fixed mods occasionally being incorrectly assigned to Unassigned instead of their Nexus category.
- Bugfix: Fixed the Mods view jumping to the activated mod’s new position when sorting by Install Date with automatic focus/jump disabled.

## Version 0.92.12

- Bugfix: Restored mod management of installed mods that have no mod archive in the Mods folder.
- Bugfix: Fixed NMM getting stuck when deleting mods.

## Version 0.92.11

- Bugfix: Fixed Nexus default categories being recreated on startup after users intentionally removed them.
- Bugfix: Fixed an intermittent Category View crash caused by an invalid top-visible TreeList index after mods or categories changed.
- Bugfix: Changed program update checks to default to once per day to better reflect the new release schedules.
- Bugfix: Fixed startup crashes caused by stale or duplicated script-type assemblies when upgrading over an existing NMM installation.

## Version 0.92.10

- New Feature: Added Turkish UI translation.
- Bugfix: Reworked FOMOD metadata caching to use the SQLite cache reliably, avoiding stale loose-cache data and unnecessary archive access.
- Bugfix: Fixed FOMOD cache validation so replaced or updated mod archives correctly invalidate outdated metadata.
- Bugfix: Improved FOMOD startup performance by using cached archive fingerprints for fast format detection.
- Bugfix: Removed legacy loose-cache reads/writes from normal FOMOD operation; installer files are now read directly from the original mod archive.
- Bugfix: Fixed FOMOD cache repair so it no longer creates incomplete SQLite metadata entries.
- Bugfix: Fixed scripted installers showing NotUsable warnings for mandatory SelectAll options.
- Bugfix: Fixed Script Extender detection for Skyrim SE, Skyrim GOG, Skyrim VR, Enderal SE, and Oblivion Remastered.
- Bugfix: Fixed Category View occasionally jumping focus to a different mod when selecting or double-clicking an entry.

## Version 0.92.9

- New Feature: Added a category selector to Add Mod From File, allowing imported mods to be assigned directly to a chosen category.
- Bugfix: Fixed the Reinstall context menu command not working as intended.

## Version 0.92.8

- Bugfix: Fixed Game Storage recovery so validation warnings can no longer lock users out of a Game Mode, especially after reinstalling NMM or reusing existing storage folders.
- Bugfix: Various tweaks and fixes to the Game Storage management.
- Bugfix: Fixed Add Mod from File leaving the selected folder handle open until another mod was added or NMM was closed.
- Bugfix: Fixed Don’t extract ReadMe files failing to skip root-level readme.txt files in some mod archives.
- Bugfix: Updated Hide FE Plugin Indexes to also hide the corresponding Rel. Position values for FE plugins.

## Version 0.92.7

- New Feature: Added new Categories option to show folder icon + mod count next to each category name.
- New Feature: Added new Categories option to hide Nexus default categories.
- Bugfix: Fixed Category View incorrectly expanding an extra category when restoring the previous session.

## Version 0.92.6 (hotfix)

- HOTFIX: Fixed category assignment issues that could move multiple mods at once when changing categories.
- HOTFIX: Fixed legacy category migration issues that could incorrectly place existing mods under Unassigned.
- HOTFIX: Added recovery for repository categories affected by previous category-ID migrations.
- HOTFIX: Fixed TXT file conflicts sometimes being silently overwritten without prompting after mod deactivation/reactivation.
- Bugfix: Fixed Game Storage Manager preventing users from setting up previously used paths.
- Bugfix: Fixed .txt file install.

## Version 0.92.4

- New Feature: Category View overhaul.
- New Feature: Added new option to the Plugin tab allowing you to hide the FE plugin indexes from the LO Index column.
- Bugfix: Fixed last issue causing NMM to always open the File Manager on startup
- Bugfix: Fixed issue causing the grid to automatically reset to the top when performing some operations in Category view
- Bugfix: Removed "Category" in front of each category when in Category view

## Version 0.92.3

- New Feature: Added new Grid Option to have the Latest column link to the main mod page instead of the specific file tab.
- Bugfix: Fixed rare mod installation failures being incorrectly reported as successful.
- Bugfix: Improved and fixed program updater.

## Version 0.92.2 (hotfix)

- New Feature: Language Manager, added support for user-made localization files, making it possible to create and use custom UI translations.
- New Feature: New UI Options: Added several new options to customize the NMM interface:
  - Choose between Text Only, Icons Only, or Text + Icons for buttons.
  - Change icon size.
  - Change icon style.
  - Select different color profiles, including profiles designed to improve usability for various types of color vision deficiency.
- New Feature: Added built-in translations for:
  - German
  - French
  - Spanish
  - Simplified Chinese
- These translations were machine-generated using Google Translate, so corrections and improvements are welcome. Feel free to suggest fixes or contribute updated localization files directly
- New Feature: Added a new set of icons with support for configurable colors, sizes, and styles, integrating with the new UI customization options.
- Bugfix: Fixed NMM occasionally switching to the File Manager tab during cold startup instead of restoring the last selected tab.
- HOTFIX: Fixed Mods toolbar sticking to icon+text buttons.
- HOTFIX: Fixed unreadable overwrite prompt buttons.
- HOTFIX: Fixed Storage Manager preventing users to set a new path after a fresh install.
- HOTFIX: Fixed a Settings menu crash.

## Version 0.91.18

- Bugfix: Comfortable density being overwritten by serialized Spacious row metrics.
- Bugfix: High-DPI column widths being scaled again during startup.
- Bugfix: Mods column movement and final widths not being flushed reliably.
- Bugfix: Fixed an issue causing the XCom 2 game mode to crash.
- Bugfix: User-defined main-tab order reverting to Plugins, Mods, Categories, File Manager.
- Bugfix: Fixed a crash issue when changing skins.
- Bugfix: Fixed main tabs visibly cycling during startup before restoring the last selected tab.
- Bugfix: Fixed stale Mod Activation Queue counts persisting after clearing the queue or restarting NMM.

## Version 0.91.17 (hotfix)

- Bugfix: Fixed various UI edits persistence fixes.
- Bugfix: Fixed bottom status bar.
- Bugfix: Fixed UI always starting on the File Manager tab.
- Bugfix: Fixed Install and Download Date column size constraints.
- Bugfix: Fixed control docking.

## Version 0.91.16

- New Feature: UI Overhaul (almost) complete, all UI controls are now affected by the selected skin and font.
- Bugfix: Fixed issue with the XCOM 2 Game mode.
- Bugfix: Fixed plugin policy enforcement preventing users from fixing their load order.
- Bugfix: Fixed NMM crashing on startup without a tracelog file.
- Bugfix: Fixed text colours when using dark skins.
- Bugfix: Fixed startup crash.

## Version 0.91.13

- Bugfix: Fixed the Get Mod Info’s Description field not properly parsing the input text.
- Bugfix: Fixed Get Mod Info clicking on an item in the list not populating the info panels.
- Bugfix: Fixed the Get Mod Info UI not fully implementing the selected skin.
- Bugfix: Fixed the Get Mod Info UI not using the font settings from the Aa Display menu.
- Bugfix: Fixed Skyrim SE and other Gamebryo game modes crash after launch because of stale plugin file watchers.
- Bugfix: Fixed a crash when switching profiles on game modes using plugin management.
- Bugfix: Performance tweaks to the file manager loading times (up to 20 times faster).
- Bugfix: Fixed the mod rename button not firing a rename event.

## Version 0.91.12

- New Feature: Mod Tagger (Get Mod Info) overhaul.
- Bugfix: Mod files are now considered as such so you should no longer receive false upgrade prompts and borked latest version numbers.
- Bugfix: Fixed AddShellExtension crash when opening the Settings menu.
- Bugfix: Fixed program getting stuck on loading when a malformed mod archive is present (eg: Crown Reach Manor for Skyrim SE).

## Version 0.91.11

- New Feature: Added a new button to fix category issues in your current setup: Mods panel -> Categories -> Update Nexus and Custom categories.
- Bugfix: Fixed issue causing custom categories being assigned IDs in the same range of Nexus categories.
- Bugfix: Fixed selected mod not being highlighted in the Mods grid.

## Version 0.91.10

- New Feature: Added new Option to toggle Plugin Restrictions.
- New Feature: Added drag&drop scrolling to the new plugin manager.
- Bugfix: Fixed Starfield official plugin list.
- Bugfix: Tons of performance tweaks to plugin management and mod uninstall.
- Bugfix: Double clicking on a Mod Name cell no longer triggers a fake mod rename.
- Bugfix: Fixed column width not persisting through sessions.
- Bugfix: Fixed text filters persisting through sessions.
- Bugfix: Fixed meta.ini constantly prompting for file overwrites.
- Bugfix: Fixed CSharpScripts mod installers (mainly for Fallout NV).

## Version 0.91.9 (hotfix)

- New Feature: Added “Repair FOMOD Info cache” to the Tools menu, it will restore the old info cache for mods that have “Data” in the mod archive. This caused mods to be missing their info, their categories and custom categories. This only works on mods that are currently in the unassigned category.
- Bugfix: Fixed cache migration failing for mods with “Data” in their archive path.

## Version 0.91.8

- New Feature: Added a new column to the File Manager showing the File Link type.
- Bugfix: Changed a few Plugin Manager column names to avoid confusion.
- Bugfix: Fixed multiple plugin toggle by hitting the spacebar.
- Bugfix: Restored Archive Invalidation for Oblivion, Fallout 3 and NV.
- Bugfix: Tweaks to “Get Mod info” it should be quicker and more reliable.

## Version 0.91.7 (hotfix)

- Bugfix: Fixed NMM Installer not running on some OS.
- Bugfix: Fixed Fallout 4 Categories file.
- Bugfix: Fixed reset to NexusMods categories showing an incorrect error message.

## Version 0.91.6

- New Feature: Plugin Manager overhaul.
- New Feature: File Manager.
- New Feature: Json Game Modes, you can add new game modes on your own.
- New Feature: New Storage Manager.
- New Feature: UI Skin support.
- New Feature: Install to game root context menu command (eg. SKSE).
- New Feature: Category View: added option to show only categories with new mods.
- New Feature: Font settings (Aa Display) now apply to all new controls.
- Bugfix: Edits you make to the new UIs should now persist between sessions.
- Bugfix: Fixes and tweaks to the rename function.
- Bugfix: The overwrite dialog should no longer spawn behind NMM.
- Bugfix: Tweaks and optimization to install/uninstall process.
- Bugfix: Updated 7zip libs to 26.02.

## Version 0.90.12

- New Feature: Added support for the new NexusMods file naming conventions.
- New Feature: Added new Display Option to have the grid stick to the top when uninstalling mods and the grid is sorted by Install Date.
- Bugfix: Fixed the uninstall and Delete action not triggering the mod deactivation.

## Version 0.90.11 (hotfix)

- Bugfix: Fixed Uninstall mod from current profile not really uninstalling the mod.
- Bugfix: Fixed Uninstall mod from all profiles not disabling the mod.
- Bugfix: Fixed Reinstall mod not updating the UI after the reinstall process completed.
- Bugfix: Single-mod uninstall operations are now correctly shown and tracked in the Mod Activation Queue.
- Bugfix: The mod archive overwrite prompt now appears only when a file would actually be overwritten, and both overwrite and rename options work correctly.
- Bugfix: Prevented duplicate mod-add tasks from being created when the same archive is added multiple times in quick succession.

## Version 0.90.9

- New Feature: Double-clicking a column header border now resizes that column to fit its content.
- Bugfix: Fixed inconsistent column resizing behaviour in the mod grid.
- Bugfix: Fixed the mod grid scrolling back to the top after installing a mod.

## Version 0.90.8

- New Feature: Letter-key navigation is back: pressing a letter jumps to the first visible mod starting with that letter.
- New Feature: Home and End now move the grid focus to the top or bottom of the mod list.
- New Feature: Added a new Display Option, enabled by default, to focus the top row after changing grid sorting instead of following the previously selected mod.
- New Feature: Added a toolbar position toggle, allowing the mod grid toolbar to be switched between the top and left side of the grid.
- New Feature: Added a new Categories tab with a first, simple implementation of the Category Manager.

## Version 0.90.7

- New Feature: Added a “Show active mods in Bold” option in the Display Option.
- New Feature: The top bar and the context menu now also use the font settings from the Aa Display menu.
- New Feature: Added a new menu in the top bar with the Disabled, Uninstall and Delete actions.
- New Feature: Added the Mod Count to the category labels when switching to the Category View.
- New Feature: You can now click F2 to edit a mode name when hovering it.
- Bugfix: Added new Status icons that are more visible.
- Bugfix: Tag Mod is now “Get Mod Info” again.

## Version 0.90.6 (hotfix)

- New Feature: New Mod Manager control: fast, with built-in filters, text search, sorting, grouping, and more.
- New Feature: New top action bar (temporary, icons will be replaced by text, also a toggle will switch from the new top bar to the old left bar)
- New Feature: New cache manager based on SQLite. Startup times (after the first run where the system has to setup the new DB) decreased by 80-90% on Skyrim SE with 1500 mods.
- New Feature: Added support for MD5 mod search on NM.
- New Feature: More intuitive Mod Name edit.
- New Feature: Changed the mod toolbar to a more intuitive icon + text format.
- New Feature: Added a font selector to the Mod grid (you can change font, size, spacing).
- New Feature: Mods with missing archives are now flagged in the UI and protected from uninstalling, with a warning that they cannot be reinstalled unless the archive is restored.
- Bugfix: Auto Filter Row uses “Contains” as default search method.
- Bugfix: Fixed grid layout and sorting not being saved.
- Bugfix: Fixed Reset UI not restoring the new grid’s defaults.
- Bugfix: Restored “DEL” key to delete mods.
- Bugfix: Fixed Endorsement status management.
- Bugfix: Fixed FOMOD installer issue causing NMM to install unselected files.
- Bugfix: Fixed FOMOD install failing to initialize when a borked logo image file is inside the archive.
- Bugfix: Fixed FOMOD installer unusable warning on usable options.
- Bugfix: Fixed a plugin manager crash when uninstalling mods.
- Bugfix: Fixed NMM hanging when uninstalling a mod with tons of plugins (eg. Khajiit Speak Redux).
- Bugfix: Fixed NMM not removing plugin files when uninstalling some mods.
- Bugfix: Fixed crash issue when writing the plugin order during the uninstall or reinstall process.
- Bugfix: Fixed crash when NMM can’t retrieve the icon from a supported app.
- Bugfix: Fixed No Man’s Sky mod installation.
- Bugfix: Fixed some issues preventing NMM from running (emulated) on Linux or Mac (Crossover).
- Bugfix: Fixed legacy behaviours where NMM automatically uninstalled mods.
- Bugfix: Fixed performance issues when loading some of the new columns.
- Bugfix: Fixed column size, category view and other UI settings not being saved/restored.
- Bugfix: Fixed collapse/expand all categories not working.
- Bugfix: Fixed date sorting with some formats like German’s dot separated dates.

## Version 0.88.5

- Bugfix: Fix NullReferenceException in SetupCommands icon extraction and plugin store null guard.
- Bugfix: Add NexusClientTests: SafeExtractIcon and PluginOrderLog null guard regression tests.
- Bugfix: Fix O(n²) install/uninstall performance hang with 1000+ mods.

## Version 0.88.4

- Bugfix: Fixed official Starfield plugin sorting.

## Version 0.88.3

- Bugfix: Fixed issue causing Oblivion Remastered management of official plugins.
- Bugfix: Fixed issue causing NMM to ask you to rewrite readme files.
- Bugfix: Updated 7z libraries to 26.1.1.

## Version 0.88.2

- Bugfix: Fixed plugin management issue in Skyrim SE and Fallout 4.

## Version 0.88.1

- Bugfix: Fixed issue causing Oblivion Remastered plugins to randomly disable.
- Bugfix: Added new Oblivion Remastered official plugins to the managed list.

## Version 0.88.0

- New Feature: Added Oblivion Remastered Support.

## Version 0.87.6

- New Feature: Added Pandora to the Supported Tools list for Skyrim Special Edition.

## Version 0.87.5

- Bugfix: Fixed issue preventing mod addition with some mod archives.

## Version 0.87.4

- Bugfix: Fixed infinite loading loop when a malformed mod archive is present in the mod directory.
- Bugfix: Fixed mod download issue when the mod archive is malformed.
- Bugfix: Fixed mod install issue when the mod archive is malformed.

## Version 0.87.3

- Bugfix: Fixed mod update check issue (caused by the outdated Nexus API library).

## Version 0.87.2

- Bugfix: Updated various external libraries (mainly the 7z one which had a security issues).

## Version 0.87.1

- New Feature: Added support for new Starfield plugins.
- Bugfix: Fixed Cyberpunk 2077 game mode crash.

## Version 0.87.0

- New Feature: Added support for Starfield version 1.12.30.0+.

## Version 0.86.1

- Bugfix: Fixed Fallout New Vegas mod installation for mods with txt script files.
- Bugfix: Fixed Cyberpunk mod installation for mods going into the x64/mods folder (you need to delete and re-download any mod already present to fix the corrupt cache).

## Version 0.86.0

- Bugfix: Fixed major issue preventing proper installation of some mods (eg. Complete Crafting Overhaul Remastered for Skyrim).
- Bugfix: Fixed mod cache handling, NMM will automatically fix a borked cache folder but you should re-tag mods that are missing some info (using the Tag button on the left bar).
- Bugfix: Fixed mod tagger not updating the mod id for mods that were missing it.

## Version 0.85.2

- Bugfix: Fixed minor issue with the Starfield game mode.
- Bugfix: Fixed Baldur’s Gate 3 mod install for mods installing into the game folder (eg. Dll mods).
- Bugfix: Tweaked scripted installers for compatibility to default to MO2 install options instead of Vortex’s.

## Version 0.85.0

- New Feature: Added support for Starfield.
- Bugfix: Fixed Steam game discovery.
- Bugfix: Updated all the external libraries.

## Version 0.84.9

- Bugfix: Fixed final Baldur’s Gate 3 release support.
- Bugfix: Fixed mod installation for some FO4 mods (like Heather Casdin 2.6).

## Version 0.84.8

- Bugfix: Automatically fixed the Skyrim download mode if the one selected is unavailable.

## Version 0.84.7

- Bugfix: Fixed a bug that forced the Skyrim SE download mode to GOG.
- Bugfix: Fixed an issue causing the Skyrim download mode to toggle each time NMM started.

## Version 0.84.6

- Bugfix: Fixed issue preventing the button to switch between Skyrim SE download modes (Steam or GOG) from properly working.

## Version 0.84.5

- Bugfix: Fixed a bug that was causing NMM to install in the root folder files with a plugin extension inside their name (but not as the actual extension). eg. Better Locational Damage - RobCo Patch.

## Version 0.84.4

- New Feature: Added support for Hogwarts Legacy.
- Bugfix: Fixed mod endorsement.

## Version 0.84.3

- Bugfix: Finally squished the Settings menu crash bug.

## Version 0.84.0

- New Feature: Added Skyrim AE GOG game mode. You can set the same MODS folder as the one used in the Skyrim SE mode to avoid redownloading the same mod archives, but you MUST set different Install Info and Virtual Install folder or your mod install will get corrupt.
- Bugfix: Removed visual download speed limit (the non premium user was reporting the wrong limit anyway).

## Version 0.83.9

- Bugfix: Fixed installation of FOMODs using WebP images.

## Version 0.83.8

- Bugfix: New Cyberpunk 2077 hotfix for mods going into the "mods" folder.

## Version 0.83.7

- Bugfix: Hotfixed Cyberpunk 2077 installation of some mods like RED4ext.

## Version 0.83.6

- Bugfix: Fixed scripted installers crash when an image file is missing from the archive.
- Bugfix: Fixed xml scripted installers when the mod author sets "." as a destination path (eg. Nocturnal Moths mod).
- Bugfix: Fixed rare crash when rescanning game modes.

## Version 0.83.5

- Bugfix: Fixed BG3 Patch 7 support .

## Version 0.83.4

- Bugfix: Fixed .dll files (and other file types) not re-activating when using multi-hd mode.
- Bugfix: Fixed installation issue for some BG3 mods.
- Bugfix: Fixed plugin activation issue in Enderal SE game mode.

## Version 0.83.3

- New Feature: Added new button to the left bar to filter for mods requiring an update (this just works in the plain mod view).
- Bugfix: Fixed a crash when adding local mods while being offline.
- Bugfix: Fixed columns not resizing when using "Reset UI".
- Bugfix: Fixed a rare crash when installing/uninstalling mods.

## Version 0.83.2

- Bugfix: Fixed an issue breaking the periodic Mod Update check.
- Bugfix: Tweaked the missing download Id search.

## Version 0.83.1

- Bugfix: Fixed another issue that could cause crashing when opening the Settings menu.

## Version 0.83.0

- New Feature: Overhauled the mod update check system.
- Bugfix: Fixed a rare crash when openin the Settings menu.

## Version 0.82.1

- Bugfix: Fixed a possible crash when deleting mods.
- Bugfix: Fixed Cyberpunk 2077 mod installation for mods like redscript, CET and Appearance Menu Mod.

## Version 0.82.0

- New Feature: Added Nemesis to the supported tools list for SkyrimLE.
- New Feature: Reinstall Mod now supports multiple mod selection.
- New Feature: Updated external libraries and .Net Framework .
- Bugfix: Fixed Cyberpunk 2077 mod install for mods like "redscript".
- Bugfix: Fixed addition of read-only mod archives.
- Bugfix: Fixed mod selection after a multiple mod deletion.

## Version 0.81.1

- Bugfix: Fixed overwrite prompt not showing up.

## Version 0.81.0

- New Feature: Added Baldur's Gate 3 game support.
- New Feature: Added Ko-fi link to support the NMM development.
- Bugfix: Fixed Wrye Flash shortcut for FNV.
- Bugfix: Fixed issue that could prevent some file links from being uninstalled.
- Bugfix: Fixed issue that could cause the file overwrite dialog to show up when not required.

## Version 0.80.14

- New Feature: Added Wrye Bash to the Fallout NV supported tools menu.
- Bugfix: Fixed another issue causing the plugin list to get stuck.
- Bugfix: Fixed SSEdit QuickAutoClean shortcut.

## Version 0.80.13

- Bugfix: Fixed issue causing the Plugin list to get stuck and being unable to move plugins in the load order.
- Bugfix: Fixed the installation for the Monster Hunter World mod loader.
- Bugfix: Fixed issue preventing mods with custom categories from being reassigned.

## Version 0.80.12

- New Feature: Added Subnautica game support.
- New Feature: Added Subnautica: Below Zero game support.
- New Feature: Added Mount&Blade II: Bannerlord game support.
- New Feature: Added Nemesis to the supported tools list for Skyrim SE.
- Bugfix: Tweaked the ominous "too many active plugins" for Bethesda games.
- Bugfix: Fixed the Fallout 4 ini warning appearing when no ini edit is required.

## Version 0.80.11

- New Feature: Added Stardew Valley game support.
- Bugfix: Fixed backup manager failing when using a custom temp folder.
- Bugfix: Fixed the reported game name for the "Reload" button in the game switch sub menu.

## Version 0.80.10 Hotfix

- Bugfix: Fixed installation of scripted installers where the mod author didn't set a destination for .esp files (this was causing NMM to not be able to install plugin files for these mods).

## Version 0.80.9

- Bugfix: Fixed random crash happening when installing new mods or sorting plugins using LOOT, caused by profile files getting locked.
- Bugfix: Fixed installation path retrieval for Skyrim SE and Enderal.
- Bugfix: Corrected Enderal SE launchers.
- Bugfix: Fixed installation of mods with borked scripted installers (eg. Finding Susanna Alive for Skyrim SE).
- Bugfix: Fixed a crash that could happen when trying to enable a plugin file that failed to link.
- Bugfix: Fixed Sims 4 for localized versions of the game.

## Version 0.80.7 Hotfix

- New Feature: Added Enderal Special Edition support.
- Bugfix: [0.80.7] Fixed some issues with the Enderal SE game mode.
- Bugfix: Fixed mod addition seemingly getting stuck when downloading mods containing a lot of txt file (eg. CBBE 3BA).
- Bugfix: [0.80.6] Fixed an occurrence of NMM crashing when opening the Settings menu (for everyone this time).
- Bugfix: Changed enable/disable icons for consistency with the old ones.
- Bugfix: Added prompt to the Patreon/Discord buttons to make sure users know what's going on if the didn't look at the tooltip.
- Bugfix: Profile switch optimization.

## Version 0.80.4 Hotfix

- New Feature: Added support for Cyberpunk version 1.2 (or later), NMM will still be able to work with older versions.
- New Feature: Added support for Cyberpunk mods that require to be installed in the bin/x64 folder (they need to be archived with a bin/x64 or x64 folder to be properly recognized).
- New Feature: Added folder shortcut to NMM's config file.
- New Feature: Added TES4Edit to the Oblivion supported tools list.
- Bugfix: Profile switch overhaul (lots of tweaks and fixes, still this won't fix already borked profiles!!!).
- Bugfix: Improved compatibility with Oblivion modscripts (still far from being 100% compatible).

## Version 0.80.2

- New Feature: Added SSEQuickAutoClean to the supported tools list.
- Bugfix: Added new debug info to the profile switching progress bar.
- Bugfix: Fixed a null reference crash when adding mods.
- Bugfix: Replaced old icons still sticking around in the download and mod activation monitors.
- Bugfix: Removed outdated Nexus references.
- Bugfix: Fixed mod info retrival from info.xml files when manually adding mods.
- Bugfix: Fixed automatic path retrieval for Fallout 3 GOTY.

## Version 0.80.1

- Bugfix: Fixed crash during the Restore Backup process.
- Bugfix: Fixed install issue with Morrowind MWSE mods.
- Bugfix: Fixed NMM hanging when pasing malformed mod descriptions.
- Bugfix: Managed crashes during the Registered Mods scan.
- Bugfix: LinkActivationTasks no longer causing the program to crash when failing to link mod files.
- Bugfix: Better management of NexusModsAPIRepository errors that could cause the program to crash.

## Version 0.80.0

- New Feature: Cyberpunk 2077 game support.
- New Feature: New flat icons for the NMM UI, this is not an UI overhaul, I just couldn't stand the sight of those old school icons.
- New Feature: NMM official Discord channel link on the NMM UI.
- New Feature: NMM official development Patreon link on the NMM UI.
- Bugfix: Fixed reported remaining download time (hours were missing).
- Bugfix: Fixed Steam path detection for some games.
- Bugfix: Fixed "scripts" folder management for Gamebryo game mods.
- Bugfix: Removed NexusMods icons to avoid confusion: support and requests are no longer handled by the NexusMods team, please use the official Discord (there's really not a great consistency with the new game icons, but they're good enough for the time being).
- Bugfix: Changed the color of the highlighted item in the game selection, the old one was making the game name unreadable.
- Bugfix: Further tweaking to the mod uninstall process that could sometimes get stuck (usually it just acted like it was stuck).

## Version 0.72.3

- Bugfix: Fixed the installation of some mods file with CalienteTools or SKSE paths not in the archive root (the fix is not retroactive, you need to redownload or re-add the mod for the changes to take effect).
- Bugfix: Increased mod uninstall/reinstall performances (more tweaks will come in the near future, the "Reinstall mod" command is sometimes unreliable and you should avoid using it for the time being).
- Bugfix: Fixed program hanging when uninstalling mods.
- Bugfix: Mod update checks will no longer rename your mods.

## Version 0.72.2

- Bugfix: Fixed crash when adding mods from a local drive.

## Version 0.72.1

- Bugfix: Downloaded mod files are now shown in the list as "Main Mod name" - "Specific mod file name".

## Version 0.72.0

- New Feature: Improvements to the mod download management, API requests down to 3, used to be 9 using old versions.
- Bugfix: Fixed SkyrimVR mod download issue.

## Version 0.71.4

- Bugfix: Fixed crash when adding a mod (on AddModTask.BuildDescriptor).
- Bugfix: Fixed game detector unable to retrieve Steam game paths.
- Bugfix: Fixed some typo in the chinese installer.

## Version 0.71.3

- New Feature: Added support for Chinese ini files.
- Bugfix: NMM now support new links for Skyrim SE, Fallout NV and TESO.

## Version 0.71.2

- Bugfix: NMM is again able to properly retrieve patch notes on the program update window (starting from the next update, of course).
- Bugfix: Updated 7z dll's to the 19.x release.
- Bugfix: Fixed real-time plugin sorting after using LOOT while NMM was open.

## Version 0.71.1

- Bugfix: Made several fixes to reduce NullReferenceExceptions and other crashes (most now log errors and display error messages instead).
- Bugfix: Updated to latest Squid-Box.SevenZipSharp version, which includes some better error descriptions and performance improvements on extracting archives.

## Version 0.71.0

- New Feature: Added Monster Hunter: World game support.
- New Feature: Added command to export the current mod list. (thanks to user iswenzz on Github)

## Version 0.70.11

- Bugfix: Fixed load order import failing to handle .esl files. (thanks to user jbostrus on Github)

## Version 0.70.10

- Bugfix: Fixed redundant code slowing down plugin sorting. (thanks to user jbostrus on Github)
- Bugfix: Fixed enabled official unmanaged plugins showing as disabled. (thanks to user jbostrus on Github)

## Version 0.70.9

- Bugfix: Fix for category view filter resetting wrong. (thanks to user jbostrus on Github)
- Bugfix: Fixed NMM's setup installing .pdb files. (thanks to user jbostrus on Github)
- Bugfix: Removed unused tool from the Enderal game mode. (thanks to user jbostrus on Github)
- Bugfix: Fix for trailing (and leading) spaces on archive files. (thanks to user jbostrus on Github)
- Bugfix: Added clearer logging/info when FOMOD info.xml is malformed.

## Version 0.70.8

- Bugfix: NMM now properly associates to file extensions even if no other program has.
- Bugfix: Fixed a crash when adding mods without being authenticated.
- Bugfix: Added manual fallback method for authenticating.
- Bugfix: Fixed a few icons (thanks to user Nubbie on Github)

## Version 0.70.7

- Bugfix: SSO improvements and fixed a missing DLL related crash.
- Bugfix: Fixed crash when mod could not be found.

## Version 0.70.6

- Bugfix: Fixed potential crash in GetFileListInfo and added useful info to the tracelog.
- Bugfix: Fixed missing mod links when looking for updates.
- Bugfix: Improvements to the mod info search.

## Version 0.70.5

- Bugfix: Fixed an issue with Nexus API implementation throwing an unexpected exception related to empty API keys.

## Version 0.70.4

- New Feature: Added support for Enderal standalone (thanks to user jbostrus on Github)
- Bugfix: Fixed current game missing from the change game menu.
- Bugfix: Fixed Steam path retrieval for some games (thanks to user jbostrus on Github)

## Version 0.70.3

- Bugfix: Fixed issue preventing downloadIDs from being properly retrieved.

## Version 0.70.2

- Bugfix: Fixed crash when adding mods from the local drive.
- Bugfix: Fixed another modid parsing related crash.
- Bugfix: Fixed missing/misbehaving launch commands.
- Bugfix: Fixed missing Game Tools.
- Bugfix: Fixed some issues preventing NMM from running on Linux (thanks to user caseif on Github).

## Version 0.70.1

- Bugfix: Fixed NMM being unable to download SkyrimSE and FalloutNV mods.
- Bugfix: Fixed crash when parsing malformed modId's.

## Version 0.70.0

- New Feature: Added new Nexus API and SSO support.
- New Feature: Brand new logo (that's it for now, but we're also planning on updating the UI)

## Version 0.65.11

- New Feature: Added Appveyor support.
- Bugfix: Fix .esl not showing up/removing in plugin list when activating/deactivating mods. with .esl files. (thanks to user jbostrus on Github)

## Version 0.65.10

- New Feature: Added a new OS Settings tab in the Settings menu where file associations and shell extensions are managed. (thanks to user squid-box on Github)
- Bugfix: Fixed startup crash when checking for filesystem compatibility with symlinks. (thanks to user squid-box on Github)
- Bugfix: Fixed registry related crash when opening the settings menu. (thanks to user squid-box on Github)
- Bugfix: Various improvements related to the SevenZipSharp library. (thanks to user squid-box on Github)
- Bugfix: Fixed crash issue during mod installation. (thanks to user squid-box on Github)
- Bugfix: Fixed crash issue with corrupted skyrim.ccc files. (thanks to user squid-box on Github)
- Bugfix: Added some unit tests to the source code. (thanks to user squid-box on Github)

## Version 0.65.9

- Bugfix: The Settings menu will now properly report whether NXM URLs and mod file types are associated with NMM. (thanks to user squid-box on Github)
- Bugfix: Fixed login form accepting email addresses. (thanks to user squid-box on Github)
- Bugfix: Fixed security exception while running C# install scripts. (thanks to user ousnius on Github)
- Bugfix: Further source code tweaks and cleanup. (thanks to user squid-box on Github)
- Bugfix: Restored some lost fixes for LOOT and plugin sorting. (thanks to user squid-box on Github)

## Version 0.65.8

- Bugfix: Source code cleanup and update. (thanks to user squid-box and dyslexicanaboko on Github)
- Bugfix: Fixed crash when downloading the program's update. (thanks to user dyslexicanaboko on Github)
- Bugfix: Fixed crash related to the config file being locked. (thanks to user dyslexicanaboko on Github)
- Bugfix: Fixed a crash when reading the MultiHdInstall setting from the config file. (thanks to user dyslexicanaboko on Github)

## Version 0.65.7

- New Feature: Added "rudimentary" support for Skyrim VR (you need to set it as the default game mode for NMM). (thanks to user squid-box on Github)
- New Feature: Added "rudimentary" support for Fallout 4 VR (you need to set it as the default game mode for NMM). (thanks to user squid-box on Github)
- Bugfix: Improved shell extension settings error handling. (thanks to user squid-box on Github)
- Bugfix: Fixed crash issue when the Fallout 4 executable is missing. (thanks to user squid-box on Github)
- Bugfix: Added FNVEdit support to the FNV game mode. (thanks to user squid-box on Github)
- Bugfix: Fixed registry paths related crash issues. (thanks to user dyslexicanaboko on Github)
- Bugfix: Fixed crash issue when Script Types or Game Mode folders are missing, added better feedback. (thanks to user dyslexicanaboko on Github)

## Version 0.65.6

- New Feature: New NMM releases are now being pulled from GitHub. (thanks to user neoh4x0r on Github)
- Bugfix: Fixed and tweaked code to improve compatibility with Linux and Wine. (thanks to user neoh4x0r on Github)
- Bugfix: Fixed issue when launching LOOT for SkyrimSE from whitin NMM. (thanks to user squid-box on Github)
- Bugfix: Fixed crash issue when generating the tracelog file and .NET Framework 4.5 isn't present. (thanks to user squid-box on Github)
- Bugfix: Fixed crash issue when trying to save a config file that is already open. (thanks to user squid-box on Github)

## Version 0.65.5

- New Feature: Added launch parameters to LOOT for supported games. (thanks to user squid-box on Github)
- Bugfix: Fixed issue with illegal characters in file paths. (thanks to user kkriisgaard on Github)
- Bugfix: Updated 7z dlls. (thanks to user squid-box on Github)
- Bugfix: Fixed issue with unsupported file systems during game detection. (thanks to user squid-box on Github)
- Bugfix: Additional tracing for plugins.txt related issues. (thanks to user squid-box on Github)
- Bugfix: Additional tracing for Fallout 4 version retrieval issues. (thanks to user squid-box on Github)
- Bugfix: Added prompt when the program's config file is corrupt. (thanks to user squid-box on Github)

## Version 0.65.4

- Bugfix: Fixed install issue with some XML script mods.

## Version 0.65.3

- Bugfix: Fixed plugin sorting issue when ESL plugins are present in the list.

## Version 0.65.2

- Bugfix: Fixed user/pass authentication issue.

## Version 0.65.1

- Bugfix: Fixed installation issue on XML installers.

## Version 0.65.0

- New Feature: The program will now communicate with the server using the https protocol.
- New Feature: Optimized the size of the program’s icons. (thanks to user SuperSandro2000 on Github)

## Version 0.64.3

- Bugfix: Fixed download IO performances. (thanks to user mgziminsky on Github)
- Bugfix: Tweaked game search to only look on fixed drives. (thanks to user mgziminsky on Github)

## Version 0.64.2

- Bugfix: Fixed mod installation issue. (thanks to user squid-box on Github)

## Version 0.64.1

- Bugfix: Fixed issue causing official plugins to be disabled when using the "Disable all plugins" button on the plugin tab. (thanks to user squid-box on Github)
- Bugfix: The program will now try to read the unamanaged plugins list from the game's .ccc file. (thanks to user squid-box on Github)
- Bugfix: Fixed archive invalidation for FO4 and Skyrim SE. (thanks to user ousnius on Github)

## Version 0.64.0

- New Feature: NMM is now able to handle large mod files (2GB+)  thanks to the new mod archive management using streams instead of loading the whole file into memory. (thanks to user squid-box on Github)
- Bugfix: Fixed issue preventing users from closing the manager when there was an ongoing mod installation. (thanks to user Arefu on Github)

## Version 0.63.20

- Bugfix: Fixed long-standing issue that caused NMM to be unable to start with a corrupt config file.
- Bugfix: Fixed an issue that could cause the Virtual config setup to be reset when using a fresh NMM install on an already configured virtual folder.
- Bugfix: Fixed inconsistencies with the Update checks/warnings management.

## Version 0.63.19

- New Feature: In the right-click context menu you can now turn off the update check (and automatic rename) for selected mods.
- New Feature: NMM will now try to automatically setup Fallout 4's ini for modding. (thanks to user smaitlx1 on Github)
- New Feature: Users can now disable multiple mods at once. (thanks to user Arefu on Github)
- Bugfix: Remove mod manager column size timer which sometimes results in columns being resized too narrow and not allowing user to expand them. (thanks to user smaitlx1 on Github)
- Bugfix: Fixed issue where the UI could become unresponsive while parsing the mod's download date. (thanks to user Xebeth on Github)
- Bugfix: Fixed incorrect trimming of UNC paths. (thanks to user Xebeth on Github)
- Bugfix: Fixed an infinite loop during the startup UAC checks. (thanks to user Xebeth on Github)
- Bugfix: Fixed issue causing the state of the Update Warning and Update Checks toggles to be lost or incorrectly set on newly downloaded mods.
- Bugfix: Removed the codesigned uninstaller since the signature was no longer valid.

## Version 0.63.17

- New Feature: Added ESL plugins support.

## Version 0.63.16

- Bugfix: Fixed SKSE support for Skyrim SE, the program will now use the correct executable.

## Version 0.63.15

- Bugfix: The Fallout 4 game mode will now use the latest plugin management algorithm when it can't retrieve the proper file version from the game's exe.

## Version 0.63.14 - Community Fixes

- New Feature: Users will now be able to add multiple files at once when clicking on the "Add mod from file" button. (Thanks to Arefu on Github)
- Bugfix: The "Purge Files" prompt will now better explain what the manager is going to purge.

## Version 0.63.13 - Community Fixes

- Bugfix: Fixed an issue preventing the installation of some big mod archives. (Thanks to WolfMad on Github)
- Bugfix: Fixed links pointing to the old issue tracker on the Nexus forums, users are now properly sent to the Github issue tracker. (Thanks to Lehmax on Github)

## Version 0.63.12 - Community Fixes

- Bugfix: Fixed issue causing profile switching to randomly overwrite the previous profile with the one NMM was switching to. (Thanks to Freakness109 on Github)
- Bugfix: Fixed rare issue causing NMM to crash when showing extended message boxes. (Thanks to CrayonOfDoom on Github)

## Version 0.63.11

- Bugfix: Hotfixed an issue with the Witcher 3 game launcher.

## Version 0.63.10

- New Feature: Added new No Man's Sky patch support (the program will now only work with the current patch).
- New Feature: Added Breaking Wheel support.
- Bugfix: Users will now be able to select the new fileservers in the download settings.

## Version 0.63.9

- Bugfix: Fixed an issue with the "Purge loose files" functionality.
- Bugfix: Fixed an issue causing the program to crash when checking for mod updates.
- Bugfix: Fixed a long-standing bug while parsing filenames preventing proper recognition of specific mod files (the ones with long version numbers).
- Bugfix: Fixed a profile update crash.
- Bugfix: Tweaked the mod search functionality both on client and server side to prevent false positives when trying to recognize Skyrim mods when in Skyrim SE game mode. When installing a Skyrim mod into Skyrim SE then the mod will be left as the original filename or as the mod name hardcoded by the author in the info.xml file and there will be no possibility of an automatic upgrade. Please note, this is not backwards compatible with old versions of NMM and it's not retroactive.

## Version 0.63.8

- Bugfix: Fixed Fallout4 DLC plugins being written in the plugins.txt file.
- Bugfix: Added DLCNukaWorld to the official plugin list for FO4.
- Bugfix: Fixed a typo in the download manager.
- Bugfix: Fixed issue that could cause the mod lists in the virtualinstall folder and the one in modprofiles to desync.
- Bugfix: Fixed an issue that could cause the xml config files to become corrupt and unreadable.
- Bugfix: Added a function to automatically fix a corrupt profile modlist.xml .
- Bugfix: Fixed issue that could cause the uninstall function to save the modlist file more than once slowing down the whole process.
- Bugfix: Fixed a BackupManager crash when the function was unable to find a file flagged for backup.
- Bugfix: Updated the 7z 32bit dll.

## Version 0.63.7

- Bugfix: Fixed a rare crash when multiple downloads fails at the same time.
- Bugfix: Updated mod list library to address some random crashes and performance issues.
- Bugfix: Fixed issue causing No Man's Sky specific mod list commands to show on other game modes.
- Bugfix: Fixed issue causing NMM to create an useless folder in the Local folder.
- Bugfix: Tentative fix for a XML startup crash.
- Bugfix: Mod upgrades should now work as intended.
- Bugfix: Fixed rare registry related startup crash.
- Bugfix: Fixed issues causing the Backup procedure to ignore the check for free space on the selected backup drive.
- Bugfix: Fixed an ini related Fallout 4 game mode crash.

## Version 0.63.6

- Bugfix: Fixed issue that could cause Mods to be missing from the list while in category view.
- Bugfix: Fixed issue with "Categories: Update and Reset to Nexus Defaults" that could fail to properly update the category list.
- Bugfix: "Categories: Update and Reset to Nexus Defaults" will now properly reset the base category list when unable to update the online list.
- Bugfix: Updated base category list for Skyrim SE.
- Bugfix: Merged the "Fix DownloadIDs" and "Check for mod updates" functionality, it should now also properly work for everyone.

## Version 0.63.5

- Bugfix: Fixed a startup crash issue when launching the Skyrim SE mode and there was a plugin requesting an unofficial plugin as master.

## Version 0.63.4

- Bugfix: Skyrim SE mode will now properly look for SSEEdit instead of TES5Edit.
- Bugfix: The plugins tab will now make it more obvious that NMM is not managing official plugins (and they will also show in the proper order).
- Bugfix: Updated the list of forced hardlinks with some sound files that dindn't work when symlinked.
- Bugfix: Fixed a Fallout 4 startup crash when the Fallout4.ini was missing or broken.
- Bugfix: The program will no longer crash when adding mod archives directly from the Mods folder.

## Version 0.63.3

- Bugfix: Fixed crash issue when launching the Skyrim SE game mode without first running the game once.

## Version 0.63.2

- New Feature: Added Skyrim Special Edition support.
- Bugfix: Fixed issue where .wav and .bgsm files were not always hardlinked.

## Version 0.63.1

- Bugfix: Fixed issue that could cause NMM to crash during mod upgrades or file overwrites.

## Version 0.63.0

- New Feature: Added No Man's Sky mod installation and sorting support.

## Version 0.62.2

- Bugfix: Fixed a crash issue when unable to retrieve the Fallout 4 executable version.
- Bugfix: Fixed a crash when parsing a null or corrupt path during the mod activation.
- Bugfix: Fixed an issue that could cause a crash when retrieving alternate mod versions and the downloadId was null.

## Version 0.62.1

- Bugfix: Fixed issue that could prevent users from opening the restore backup ui.

## Version 0.62.0

- New Feature: Added the Backup Manager:
- users will now be able to backup and restore their mod installation, base game files and the NMM Mods folder (new submenu under Tools).
- a backup button has also been added to the program update prompt.
- New Feature: Added new setting to the General settings tab where the user can set whether NMM should manage the mod name or leave it as the user set it.
- New Feature: NMM now supports the multi mod selection when using the mod delete function.
- New Feature: NMM will now be able to retrieve mod categories from the web server (a new button has been added to the Categories submenu).
- Bugfix: Fixed a bug causing virtual install and profile config files to become bloated, an automatic file fixing procedure will take care analyze your config files and fix them when you start NMM (This can be a lenghty but necessary process).
- Bugfix: Fixed several crashes when trying to save the profile config.
- Bugfix: Fixed confirmation prompt when exiting the program.
- Bugfix: Fixed crash caused by some special characters in the profile name.
- Bugfix: Various fixes and tweaks to the "Check for mod updates" and "Get missing download ID" functions.
- Bugfix: Fixed mod installation when the mod's downloadID was invalid.
- Bugfix: Fixed issue where NMM was unable to retrieve the mod version when loading the profile's mod list (also randomly causing a crash).
- Bugfix: Added more feedback to the profile switching process when disabling links from the previous profile, previously NMM seemed to hang.
- Bugfix: Fixed long profile names breaking the UI, NMM will now also prevent users to set names longer than 64 chars.
- Bugfix: Fixed crash when NMM tries to read the plugins.txt or loadorder.txt files that are locked for access by another program.
- Bugfix: Fixed issue where right-clicking on a profile in the profile tab didn't select the profile.
- Bugfix: Fixed renaming issue with some special characters causing empty profile names.
- Bugfix: Fixed the progress bar popup randomly starting to blink after changing the loadorder.
- Bugfix: Fixed profile switch related crash when removing old plugins.
- Bugfix: Fixed crash when switching game modes.
- Bugfix: Fixed crash issue when saving or loading a profile.
- Bugfix: Fixed mod migration crash.
- Bugfix: Updated 7zip libraries to address security issues.
- Bugfix: Updated Fallout 4 categories.
- Bugfix: Fixed issue where NMM was unable to retrieve the mod ID from some filenames.
- Bugfix: Fixed startup crash issue when Windows 8 users were missing the latest .Net Framework.
- Bugfix: Fixed stuck queue issue with invalid mod or download IDs.
- Bugfix: Fixed crash issues when updating a mod IDs in the current profile.
- Bugfix: Tweaked connection issues feedback.
- Bugfix: Fixed issue preventing the mod migration procedure from properly importing the backup file.
- Bugfix: Fixed issue causing mod reinstalls to fail while using the multi-HD mode.
- Bugfix: Fixed issue where dash characters were added to a downloaded mod name.
- Bugfix: Fixed Supported Tools config window popping under its dropdown menu.
- Bugfix: Tweaked and re-enabled the mod upgrade functionality, if it doesn't work for a mod file it means the mod author didn't properly set it as a new version of the previous file.
- Bugfix: Fixed crash issues when saving or updating the profile's modlist xml file.
- Bugfix: NMM will now provide feedback when the 'Check for mod updates' or 'DownloadID' related functions fails.
- Bugfix: Fixed issue where a downloaded mod may not show up in the Mods list when using custom categories.
- Bugfix: Fixed wrong popup menu location when clicking the 'Check for mod updates' button.
- Bugfix: NMM will now properly recognize newer FO4 DLCs as such.
- Bugfix: Fixed another issue that could cause broken records to be added to the profile NMM is switching to and preventing NMM to remove linked files.
- Bugfix: Fixed crash issue when the profile id is null.
- Bugfix: Fixed issue where activating a new mod wasn't saving the mod entries in the virtual install config files, thus showing the mod as disabled when restarting NMM.

## Version 0.61.23

- Bugfix: Tweaked and fixed the "Check for mod updates" function.
- Bugfix: Fixed issue with the "Get Missing Download ID" function preventing NMM from retrieving the correct info for some mods.

## Version 0.61.22

- Bugfix: Fixed mod update check and get missing downloadid functions.
- Bugfix: Fixed a few loadorder and plugins file interaction related crashes.
- Bugfix: NMM will now add Fallout 4 DLCs to the Plugins.txt file.

## Version 0.61.21

- Bugfix: Fixed Fallout 4 1.5 issue that could cause plugins to become disabled when changing a plugin load order.

## Version 0.61.20

- Bugfix: Again, added support for the latest Fallout 4 beta version.

## Version 0.61.19

- Bugfix: Fixed support for the latest Fallout 4 beta version.
- Bugfix: Fixed issue preventing the Get Mod Updates and Get Missing Download IDs from properly working.

## Version 0.61.18

- Bugfix: Fixed NMM installer not prompting for the install folder when the program was already present on the system.
- Bugfix: Fixed crash when intercepting external changes made to the loadorder file.
- Bugfix: Fixed a forced random crash issue with the progress bar.

## Version 0.61.7

- New Feature: Added support for Fallout 4 version 1.5 beta or higher.
- New Feature: Added new "Import profile's Load Order' button to the profile's submenu.
- Bugfix: NMM will now properly recognize the official Fallout 4 DLCs as such.
- Bugfix: Fixed a few crash issues where NMM was unable to link a mod file.
- Bugfix: Fixed crash when purging mod links.
- Bugfix: The login form will now always show on top of the main NMM window.
- Bugfix: Fixed crash when the system prevents NMM from interacting with the plugins and loadorder files.
- Bugfix: Fixed issue where switching to a profile didn't load the saved scripted installer options.
- Bugfix: Fixed issue where NMM could randomly add files to the profile it was switching from.
- Bugfix: Fixed issue where NMM was assigning the same DownloadID to mod files sharing the same ModID.
- Bugfix: Supported tool Bodyslide will now be referred as such (instead of "Bodyslide [version]).

## Version 0.61.16

- Bugfix: NMM will now properly install mod files where the root folder is "interface".
- Bugfix: Fixed a crash when trying to uninstall a plugin that's no longer present.
- Bugfix: NMM will now provide better feedback when a "Delete Mod" fails.
- Bugfix: Fallout4 game mode will now interact with the Fallout4Custom.ini instead of the default one.
- Bugfix: The version column will now properly show both the local and the online version, separated by a "/".

## Version 0.61.15

- Bugfix: Fixed and tweaked the Supported Tools dropdown menu.
- Bugfix: Fixed issue preventing users from re-configuring a supported tool's path, users can now right-click on the supported tool in the menu to set a new path for it.
- Bugfix: Fixed scripting issue for Oblivion and Morrowind caused by missing dll files.
- Bugfix: Fixed issue preventing NMM from properly installing the BOSS dll for Morrowind.
- Bugfix: Fixed crash issue when setting the Temp path to a root folder.
- Bugfix: Fixed issue allowing users to enforce an invalid and unsafe path for the Temp folder.
- Bugfix: Fixed random "Value of 'xxx' is not valid for 'Value'." exception preventing some functions using the modal progress bar to complete.

## Version 0.61.14

- New Feature: Overhauled and fixed the Supported Tools setup and functionality, there's no longer a settings tab, to set the tool's path just go in the Tools submenu and click on the desired tool.
- New Feature: Added FO4Edit to Fallout 4 supported tools.
- Bugfix: Removed unused dll files causing false alerts in the tracelog file.

## Version 0.61.13 Hotfix

- Bugfix: Fixed issue preventing NMM from restoring overridden links when uninstalling a mod.

## Version 0.61.12

- New Feature: Added a "Reinstall Mod" option to the right-click context menu.
- Bugfix: Fixed issue preventing mod files from being removed from the game folder when uninstalling a mod that was previously overwritten.
- Bugfix: Fixed mod deletion prompt not showing up when using the DEL key.
- Bugfix: NMM will now properly run the 64bit version of Bodyslide2 when available.

## Version 0.61.11

- New Feature: Added XCOM 2 modding support.
- Bugfix: Updated Dragon's Dogma categories (you need to click on the "categories"->"reset to nexus defaults" button to update your local list).
- Bugfix: Fixed a few issues with automatic game scan for Dragon's Dogma and Witcher 3.

## Version 0.61.10

- Bugfix: Fixed null reference crash when disabling mods in Witcher3 mode.
- Bugfix: Fixed crash when UAC is preventing NMM from interacting with its VirtualInstall folder.
- Bugfix: Fixed another "failed rollback" crash when interacting with the plugin load order or activation state.
- Bugfix: Fixed crash when purging file links and the user had manually removed mod files.

## Version 0.61.9

- Bugfix: Fixed mod deletion prompt appearing when clicking on "Uninstall from all profiles".
- Bugfix: Fixed mod delete removing the mod archive when clicking NO at the prompt.

## Version 0.61.8

- Bugfix: Fixed issue preventing Witcher 3 cache from properly updating (causing missing mod entries in the list).
- Bugfix: Fixed issue where NMM was reporting a "Failed rollback" crash when trying to write on a read-only plugins.txt file.
- Bugfix: Removed misleading "Delete Mod" button from the left bar, all the uninstall and delete commands are in the right-click context menu.
- Bugfix: Tweaked and fixed mod delete functionality.

## Version 0.61.7

- Bugfix: Fixed issue causing the plugins.txt file to be updated very slowly when enabling/disabling multiple plugins.
- Bugfix: NMM will now properly install Witcher 3 mods meant to be  installed in the DLC folder.
- Bugfix: Fixed Witcher 3 mods installation/uninstallation issues (installing in the wrong folder or leaving files behind).
- Bugfix: Fixed issue causing NMM to rename a Witcher 3 mod folder's name.
- Bugfix: Fixed unsupported mod structure warning to always reference Dragon's Dogma.

## Version 0.61.6

- New Feature: Added Dragon's Dogma modding support.
- Bugfix: Fixed issue causing loose mod files or game files overwritten by mods to be lost while switching profiles.
- Bugfix: Fixed crash when clicking on empty elements of the Profiles menu.
- Bugfix: Fixed crash when adding elements to the mod install queue.
- Bugfix: Fixed Fallout 4 displayed name.
- Bugfix: Fixed issue preventing some mods from properly installing on Witcher 3 and Starbound.
- Bugfix: Fixed issue causing some properly packaged Witcher 3 mods to be flagged as not supported.
- Bugfix: Added new check to try and quickly retrieve game paths from Steam.

## Version 0.61.5

- Bugfix: Fixed issue with Fallout 4 categories.
- Bugfix: Reworked contextual menu for the Mods control.
- Bugfix: Tweaked source code and updated to .Net Framework 4.6.

## Version 0.61.4

- Bugfix: Added F4SE launcher.
- Bugfix: Tweaked F4 ini editing warning.
- Bugfix: Fixed issue causing NMM to clean the plugins.txt readonly flag at startup.
- Bugfix: Fixed Fallout 4 .mp3 and .bgsm file installation while using MultiHD mode.
- Bugfix: Fixed crash issue when using the "Uninstall from all profiles" function.

## Version 0.61.3

- Bugfix: NMM will now prevent the Fallout 4 launcher from resetting the plugins.txt file.
- Bugfix: Fixed Fallout 4 categories.
- Bugfix: Fixed issue preventing NMM from removing scripted installer config files when using "Uninstall all active mods".
- Bugfix: Fixed rare crash when switching mod install mode in the Tools menu.
- Bugfix: Fixed rare issue where NMM suggests a non-existent drive as Virtual folder.
- Bugfix: Fixed a rare crash in the file downloader.

## Version 0.61.2

- Bugfix: NMM will now provide better feedback when unable to check for Fallout4 ini edits, it also no longer prevents mod installation.
- Bugfix: Fixed issue preventing NMM from properly installing .bsa file for Skyrim when using multiHD mode.
- Bugfix: Fixed issue preventing NMM from properly installing .mp3 files.
- Bugfix: Fixed an issue to preserve file links when disabling a mod that shares a file with another still enabled mod.
- Bugfix: Fixed profile switch crash issue when checking for a plugin’s masters activation state.

## Version 0.61.1

- Bugfix: Fixed issue preventing NMM from restoring overwritten loose files.
- Bugfix: Fixed issue with some scripted installers being unable to check for dependencies and being unable to install.

## Version 0.61.0

- New Feature: Added Fallout 4 modding support.
- New Feature: Plugins that are missing their masters will show as red in the Plugins tab. Plugins whose masters are disabled will be shown as orange (the plugin description will also provide feedback about these issues).
- New Feature: Release notes will be shown in the update popup.
- Bugfix: Fixed crash issue and profile switch issues caused when enabling scripted mods during a profile switch.
- Bugfix: NMM will now prompt the user whether he really wants to enable or disable all the plugins when clicking the buttons in the Plugins tab.

## Version 0.60.16 hotfix

- New Feature: Added context menu to each profile to rename/delete it (you’re no longer limited to interact with the current profile).
- Hotfix: Fixed issue crashing on Witcher3 game mode.
- Bugfix: Fixed issue preventing NMM from saving the load order when moving plugins in the Plugins tab.
- Bugfix: NMM no longer loads non-existent paths into the Supported Tools settings menu.
- Bugfix: Fixed issue preventing NMM from removing cache files when a mod archive is deleted.
- Bugfix: Fixed issue causing readme files to trigger the “Missing files” warning during a profile switch.
- Bugfix: Fixed rare crash when something outside NMM changes the load order.
- Bugfix: Removed old debug related messages from the tracelog.

## Version 0.60.14

- New Feature: Changed the way NMM handles the mod cache, NMM will now load 4x faster than before (It needs to migrate your cache files, so add a few seconds the first time you run this new version).
- New Feature: Merged the mod Version and Latest Version columns to reduce UI cluttering (you will still be able to click on the version number to reach the mod page, if you have update warnings enabled hovering the mouse on the warning icon will show you the latest version available online).
- Bugfix: Fixed issue preventing NMM from properly enabling disabled mods while using the MultiHD mode.
- Bugfix: Fixed issue preventing NMM from saving the activation state of plugins.
- Bugfix: Fixed “Could not find part of the path” crash issue when migrating or switching installation mode.
- Bugfix: Fixed crash issue when loading broken or null paths from the Supported Tools settings.
- Bugfix: Fixed issue causing NMM to ask users to configure their install settings when they were already set.
- Bugfix: Fixed NMM being unable to parse XML scripts with fake choices (like S.W.I.F.T for Skyrim).
- Bugfix: Fixed issues with the cache manager.
- Bugfix: Fixed issues where NMM wasn’t prompting to install the new version when the installer was already present in the temp folder.
- Bugfix: Fixed NMM being randomly unable to import the load order.

## Version 0.60.13

- Bugfix: Disabled the masterlist.yaml update check until we can properly fix it (it seems to get stuck for some users).

## Version 0.60.12

- Bugfix: Updated LOOT library to version 0.8.
- Bugfix: Fixed issue preventing users from completing the Dark Souls 2 setup.
- Bugfix: Fixed mod discoverer looking for mods in the profiles folder.
- Bugfix: Fixed crash issue while sorting the plugins.

## Version 0.60.11

- Bugfix: Fixed crash issue when switching to MultiHD mode.
- Bugfix: Fixed crash issue when deleting the current profile and then switching to another one.
- Bugfix: Fixed issue that could cause NMM to remove backup files when changing install mode.
- Bugfix: Fixed issue in MultiHD mode where NMM wasn’t reinstalling missing mods required by the profile.
- Bugfix: Fixed NMM saving registry values in the wrong place.
- Bugfix: Fixed misleading folder suggestion on the setup screen.
- Bugfix: Fixed NMM crashing when setting invalid paths for Supported Tools.
- Bugfix: Fixed Supported Tools settings tab not showing error warnings.
- Bugfix: Minor tweaks to the Supported Tools settings tab.

## Version 0.60.10

- New Feature: Added a "Mod" field in the plugin description that will tell you what mod that plugin belongs to.
- New Feature: Right-clicking on a mod name will now also show the mod’s filename.
- Bugfix: Fixed linked .bsa files not working with Fallout/Oblivion.
- Bugfix: Small tweak to improve startup loading with lots of mods (more improvements will come).
- Bugfix: Fixed issue causing a crash for some mods when forcing a file copy in multiHD mode.
- Bugfix: Fixed issue causing Delete mod to be enabled when clicking on categories.
- Bugfix: Fixed crash issue with the Delete mod function.

## Version 0.60.9

- New Feature: NMM will now be able to keep track of the real ID for mod files.
- New Feature: Added “Dual Sheat Redux” and “Patchus Maximus” to the supported tools (those are just shortcuts, you need to install them).
- New Feature: Added “Installed Mods” counter to the bottom bar.
- Bugfix: Fixed issue where a profile was unable to restore scripted installers choices.
- Bugfix: Fixed issue where scripted installers couldn’t reinstall their files (if a file was present twice).
- Bugfix: Fixed several issues with the Change Virtual Folder functionality and the multiHD mode setup.
- Bugfix: Fixed crash when searching for program updates while the new installer is already installing.
- Bugfix: The program will now check whether there’s actually an update to download before starting the masterlist update.
- Bugfix: Fixed crash issue when retrieving the plugin directory.
- Bugfix: Fixed issue preventing NMM to store “Dual Sheat Redux Patch.esp” and “PatchusMaximus.esp” in a profile.
- Bugfix: Fixed issue where NMM wasn’t updating supported patches files (DSR, PM, Bashed). Make sure to click on Save profile or disable/enable the plugin if you edit them when NMM is not running.
- Bugfix: Fixed LOOT settings missing for New Vegas.

## Version 0.60.8

- Bugfix: Tentative fix for the uninstall progress getting stuck during the mod migration.
- Bugfix: Fixed issue causing NMM to crash while loading the path configuration form (also preventing the Dark Souls 2 game mode from being initialized).
- Bugfix: Fixed random crash while trying to change the read-only flag of a plugin file.
- Bugfix: Fixed crash when the config file references to an unplugged drive.
- Bugfix: Fixed random crash while deleting a newly created category.

## Version 0.60.7

- Bugfix: Fixed bug preventing NMM from properly working with some games (Dragon Age, State of Decay, etc.)
- Bugfix: Tentative fix for the bug preventing .xml files from being removed from the “Scripted” folder.
- Bugfix: Implemented additional log entries to help with a rare bug crashing NMM while sorting the plugins.
- Bugfix: Fixed issue preventing plugin files from being installed on ReFS drives.

## Version 0.60.6

- Bugfix: Fixed many issues with the MultiHD install mode (missing files, mods unable to install, etc..).
- Bugfix: Fixed crash when the user is using a broken/incomplete .Net Framework.
- Bugfix: Fixed rare crash while right-clicking on a mod in the mods list.
- Bugfix: When the MultiHD mode is active NMM will prevent the user from installing/activating mods or switching profiles if the program is not run as Administrator (and will warn the user about it).
- Bugfix: Fixed an issue preventing some fomods from installing (like FCO for New Vegas).
- Bugfix: Fixed issue preventing some mods from installing txt files.

## Version 0.60.5

- Bugfix: Fixed issue causing some plugins to randomly show as corrupt.
- Bugfix: Fixed issue causing .jar and .exe files from properly working (FNIS, BodySlide2, DualSheat’s .jar, etc.).
- Bugfix: Fixed issue causing the “Uninstall from all profiles” command to crash under some specific circumstances.

## Version 0.60.4

- Bugfix: Fixed issue causing the migration process to be unable to reinstall some mods.
- Bugfix: Fixed issue preventing NMM from properly handling the load order for Fallout and Oblivion.
- Bugfix: Fixed issue causing the Morrowind game mode to hang while performing the mod migration.
- Bugfix: Fixed issue causing the Morrowind game mode to install files in the wrong folder.
- Bugfix: Fixed issue preventing NMM from automatically retrieve the Morrowind game path during the startup scan.
- Bugfix: Fixed issue preventing the ReadMe Manager from properly catching readme files.
- Bugfix: Fixed issue with the Backup file management.

## Version 0.60.3

- New Feature: Added new “Restore Backup Profile” functionality to the Tools menu.
- Bugfix: Fixed issue preventing the folder selection dialog to appear in the setup menu.
- Bugfix: Tweaked the startup disclaimer.

## Version 0.60.2

- Bugfix: Fixed issue causing scripted installers to fail when there was no active mod profile.

## Version 0.60.1

- Bugfix: Fixed issue that could cause NMM to crash when starting the migration process.
- Bugfix: Fixed issue that could hang the program while switching profile.

## Version 0.60.0

- New Feature: Profile Manager.
- New Feature: New mod installation method.
- New Feature: Added a few new icons for a clearer interface.
- New Feature: Added a new button to launch external tools.
- Bugfix: Fixed crash caused by custom fonts.
- Bugfix: Fixed automatic plugin sorting being enabled for unsupported games.
- Bugfix: Fixed a crash issue caused by a broken config file.
- Bugfix: Fixed crash while refreshing plugin indices in the plugin’s tab.
- Bugfix: Fixed crash while trying to change a plugin timestamp and the file is locked by another process.
- Bugfix: Fixed crash when the program could not find the new installer file on the server.
- Bugfix: Fixed local mod addition when the source folder contains illegal characters.
- Bugfix: Fixed automatic plugin sorting being enabled and crashing on Morrowind.
- Bugfix: Fixed rare occurrence of files being linked in the wrong folder.

## Version 0.56.1 hotfix

- Bugfix: Fixed Witcher 3 automatic detection when installed through Steam.

## Version 0.56.0

- New Feature: Added Witcher 3 support (only for mods made through the official Modding Toolkit).
- Bugfix: Fixed issue with the install queue feedback on the bottom bar.
- Bugfix: Fixed issue causing many Oblivion omod to fail to install or to crash the program.
- Bugfix: The installer should no longer ask Windows 10 FINAL users to install .Net Framework 4.5.2.

## Version 0.55.8

- Bugfix: Added forced timeout to the program version check (15 seconds).
- Bugfix: Tweaked feedback when a mod archive is being locally moved to the program’s Mods folder.
- Bugfix: Updated LOOT API to version 0.7.1.
- Bugfix: Fixed load order not always synching with games using file timestamps to manage it.

## Version 0.55.7

- Bugfix: Fixed crash issue with invalid plugin file edit events.
- Bugfix: Fixed crash issue while uninstalling a mod and the game’s ini file is no longer present at the expected location.
- Bugfix: Tweaked failed login messages to be more informative.
- Bugfix: Fixed crash issue when in category view and moving a mod to another category when a search filter is active.
- Bugfix: Fixed crash issue when in category view and renaming a mod while a search filter is active.
- Bugfix: Fixed crash during the program update check when the server is returning an unexpected value.
- Bugfix: Fixed DownloadDate column showing 0s while in category view.

## Version 0.55.6

- New Feature: The manager no longer prevents you from activating more than 255 plugins for Gamebryo games, but it will warn you on the status bar when the limit is exceeded and inform you that the game won’t be able to start.
- Bugfix: Fixed startup crash when the manager was unable to retrieve the plugin list activation state.
- Bugfix: Fixed crash issue when retrieving a plugin from his file path.
- Bugfix: Fixed issue causing a plugin to disappear from the list when the program used to edit it will recreate it from scratch (eg. Construction Kit).

## Version 0.55.5

- Bugfix: Fixed issue causing an infinite loop in the file watcher while monitoring the plugins/loadorder files.

## Version 0.55.4

- Bugfix: Various tweaks to the plugin load order and activation management.
- Bugfix: Fixed NMM trying to react to external events before a file was fully created/written to.
- Bugfix: Fixed issue preventing NMM from saving loadorder.txt and plugins.txt files if they already existed but 0 bytes in size.
- Bugfix: Fixed NMM crashing while trying to uninstall a file being used by another process.

## Version 0.55.3

- Bugfix: NMM will more consistently create the loadorder when the loadorder.txt file is missing or unreadable.
- Bugfix: Fixed an issue causing missing/deactived plugins in both Fallout games and Oblivion.
- Bugfix: Fixed “HearthFires.esp” and added hd texture packs entries to the base plugins list (this will fix them missing when NMM creates a new loadorder.txt file).
- Bugfix: Fixed a few “index out of range” crashes when interacting with the plugin list.

## Version 0.55.2

- Bugfix: Fixed NMM crashing when uninstalling a mod and the mod archive is no longer present in the Mods folder.
- Bugfix: Fixed crash during mod deactivation for plugin-less game modes.

## Version 0.55.1

- Bugfix: Additional checks to prevent the program from crashing when something is locking a file.
- Bugfix: Fixed crash when the program is unable to perform a token login.
- Bugfix: Fixed threading related null object reference crash.
- Bugfix: Fixed issue preventing NMM to manage single character name plugins.
- Bugfix: Fixed issue causing missing plugins in the NMM Plugins tab.
- Bugfix: Fixed issue causing the Automatic Sorting functionality to delete the loadorder file.
- Bugfix: Fixed crash when the Automatic Sorting fails, NMM will now warn the user with a message box.

## Version 0.55.0

- New Feature: New plugin loadorder/activation management.
- New Feature: NMM will now synch in realtime with external plugin loadorder/activation changes.
- New Feature: Added the plugin count to the status bar (present/active).
- New Feature: Added new “Download Date” column to the Mods tab.
- New Feature: Added new FNV4GB launcher option for New Vegas (you still need to manually install it).
- Bugfix: Fixed many issues when there were too many active plugins.
- Bugfix: Fixed crash issue with broken mod packages.
- Bugfix: Fixed broken column auto-resize on the Download Monitor.
- Bugfix: Fixed rare crash when resetting the search filter.
- Bugfix: Fixed Mod Installation Queue crash issue when many different operations were queued.
- Bugfix: NMM will properly show “Idle” when the mod install queue is empty.
- Bugfix: Fixed crash when parsing the endorsement state from the cached file.
- Bugfix: Fixed startup issue where NMM was asking to upgrade a mod when the local version couldn’t be retrieved.
- Bugfix: Fixed mod addition status showing as “Downloading” when locally adding a file.
- Bugfix: Fixed Morrowind plugin sorting/activation management.
- Bugfix: Fixed issue in the Mod Activation Queue Monitor causing increasing lag after each completed task.
- Bugfix: Enable and Disable all plugins buttons will now properly show a progress bar.
- Bugfix: Fixed NMM suggesting paths on drives that are no longer present on the system.

## Version 0.54.10 hotfix

- Bugfix: Fixed libloadorder issue causing blank plugins.txt files (broken plugins will be ignored).
- Bugfix: Fixed loadorder/plugins backup procedure being unable to retrieve the AppData path on some systems.
- Bugfix: Fixed issue caused by invalid userlist.yaml files.

## Version 0.54.9 hotfix

- New Feature: You will now be able to provide custom user lists to NMM for the automatic sorting (name your file as userlist.yaml and put it in the Documents/Nexus Mod Manager/loot/{GameName} folder).
- New Feature: NMM will now make a safety backup of your plugins.txt and loadorder.txt files.
- Bugfix: Fixed an “index out of range” crash.
- Bugfix: Fixed some forced plugins.txt deletions in the libloadorder library.

## Version 0.54.8 hotfix

- Bugfix: Fixed a startup crash when a plugin loaded before its master and that master plugin where the last 2 elements of the plugin list.
- Bugfix: The “Uninstall all active mods” function will now properly make use of the activation queue.

## Version 0.54.7 hotfix

- New Feature: NMM will now prevent you from moving a plugin above one of its master files.
- Bugfix: Fixed load order issue causing NMM to be unable to properly save the plugins.txt (causing blank plugins.txt files).
- Bugfix: Fixed issue causing NMM to crash when there were invalid entries in the loadorder file.
- Bugfix: Fixed issue preventing some plugins to activate after being installed.

## Version 0.54.6 hotfix

- Bugfix: Fixed another issue causing disabled plugins.

## Version 0.54.5 hotfix

- Bugfix: Fixed threading issue causing all sorts of bugs with the plugins.txt file interaction (eg. Disabled plugins).

## Version 0.54.4 hotfix

- Bugfix: Fixed issue preventing the plugin list from properly updating after the Automatic Sorting.
- Bugfix: Fixed issue causing NMM to crash when there were duplicate entries in the loadorder file.
- Bugfix: Fixed another rare instance of plugin entry duplication in the loadorder file.

## Version 0.54.3 hotfix

- Bugfix: Fixed issue where NMM is unable to validate new directories for the Install Info and Mods.

## Version 0.54.2 hotfix

- Bugfix: Fixed NMM crashing because of faulty loadorder/plugins file.

## Version 0.54.0

- New Feature: Added support for libloadorder and LOOT APIs (replacing the BOSS API).
- New Feature: Added new “Automatic Plugin Sorting” functionality to the Tools menu (for supported games).
- New Feature: Added new info column to the “Mod Activation Queue” monitor.
- Bugfix: Fixed long-standing issue causing NMM to rarely mishandle critical plugin entries in the loadorder.
- Bugfix: Fixed NMM crashing when an invalid loadorder.txt file is present in the game’s AppData folder.
- Bugfix: Fixed a crash loop issue caused by setting NMM paths to hard-drives no longer present on the system.

## Version 0.53.7

- Bugfix: Fixed issue causing the program to crash while editing a mod name.
- Bugfix: Fixed a long-standing issue preventing some XML scripted installers (like the one for WARZONES 2015) from properly installing.

## Version 0.53.6

- Bugfix: Fixed NMM crashing when an invalid image parameter was present in a plugin file.
- Bugfix: Fixed local mod addition queue getting stuck on incomplete mod additions.
- Bugfix: Fixed a tracelog UI issue where the details panel made it impossible to click the buttons.

## Version 0.53.5 hotfix

- Bugfix: This should fix an issue causing scripted installers to fail after choosing the desired options.
- Bugfix: Fixed an issue causing the mod activation state to not update while in category view.

## Version 0.53.4 hotfix

- Bugfix: Fixed NMM randomly crashing when installing/uninstalling mods.

## Version 0.53.3

- New Feature: NMM will keep track of options selected in scripted installers (this info will be used in the profile build).
- New Feature: Added Mod Activation Queue feedback to the bottom bar.
- New Feature: The Mod Activation Queue will now automatically focus on the currently active element.
- Bugfix: Fixed crash when trying to filter the Install Date column in category view.
- Bugfix: Fixed IllegalFilePath exception crash during a mod uninstall.
- Bugfix: Fixed UnauthorizedAccess exception crash during the game scan.
- Bugfix: Fixed startup crash when NMM was unable to retrieve critical paths in the config file.
- Bugfix: Failed logins will now provide more useful feedback.
- Bugfix: Overwrite prompts will now show which mod is trying to overwrite a file (users were left clueless if many mods were in the queue and the monitor was minimized).

## Version 0.53.2 hotfix

- New Feature: Mod counter (present/active) added to the bottom bar.
- Bugfix: Fixed missing progress column on the mod activation monitor.
- Bugfix: Fixed cancelled installs reporting as 100%.
- Bugfix: Fixed CTRL+C not copying all fields on the mod activation monitor.
- Bugfix: Fixed many instances of UI lag and/or getting stuck.

## Version 0.53.1 hotfix

- Bugfix: Fixed mod upgrades getting stuck and blocking the activation queue.

## Version 0.53.0

- New Feature: Mod Activation Queue added.
- New Feature: Backup restore functionality added.
- Bugfix: Fixed many instances of NMM crashing when a file is locked by another process.
- Bugfix: Fixed a progress bar “ArgumentOutOfRange” crash.
- Bugfix: Fixed shift+click multiselection on the mods control.

## Version 0.52.3

- Bugfix: Fixed NMM saving html encoded mod names.

## Version 0.52.2

- Bugfix: Removed unnecessary entries in the server selection list.
- Bugfix: Fixed an issue preventing the CDN to be chosen if the selected Premium server is unavailable.
- Bugfix: Fixed an issue causing NMM to save mod files with url encoded names.

## Version 0.52.1

- Bugfix: NMM will ignore the fileserver selection when CDN downloads are provided.

## Version 0.52.0

- New Feature: Full support for the new CDN download system.
- Bugfix: Fixed a tips related startup crash.

## Version 0.51.0

- New Feature: Added the Tips Manager, it will provide feedback for new version’s fixes and tweaks.
- New Feature: NMM will warn the user if there’s something wrong with the selected InstallInfo/Mods folders.
- New Feature: NMM will prompt the user to go back to the folder setup instead of just automatically removing installed mods if they're missing from the proper mod folder.
- New Feature: Added support for the new endorse/abstain management (to update the local status you’re required to perform a “mod update check”, no icon in the endorsement column means you never endorsed the mod).
- New Feature: Added new “Stop Searching” button to the game scan UI: clicking this will stop the search process for all game modes.
- New Feature: Added new “Quick Startup” button to the game scan UI: clicking this (after confirming at least one game path) will immediately switch to the game selection with just the games you’ve already verified.
- Bugfix: Fixed an issue with the XML script compiler preventing scripts to properly compile for plugin-less games.

## Version 0.50.3

- New Feature: Added mod script support for State of Decay installers.
- Bugfix: The program installer will now force uninstall only for versions older than 0.50.0.
- Bugfix: Fixed the Dark Souls 2 support with new versions of the GeDoSaTo Tool.

## Version 0.50.2

- Bugfix: Fixed an issue preventing UI elements to be reorganized by the user.
- Bugfix: The Reset UI button in the Tools menu will now properly reset the column size in the mod control.
- Bugfix: The ‘Check for mod update’ and ‘Endorsement toggle’ requests will now properly resume after a successful login (pending requests will be discarded after 15 seconds).

## Version 0.50.1

- Bugfix: Fixed an issue with 32bit systems and the new SevenSharpZip library.
- Bugfix: Fixed an issue while installing scripted New Vegas mods.

## Version 0.50.0

- New Feature: Source code upgraded to the .Net Framework 4.5.
- New Feature: Added a new button in the Download settings menu that will fix the Chrome/Windows 8.x download bug.
- New Feature: The seamless login functionality will try to restore the task that requested the login.
- New Feature: Updated the library handling mod archives (this should also fix some long-standing bugs).
- Bugfix: Fixed a download startup bug while being offline.

## Version 0.49.7

- New Feature: New seamless login functionality.
- Bugfix: Fixed a “value cannot be null" startup crash.
- Bugfix: Fixed a startup crash while trying to create environment folders.

## Version 0.49.6

- Bugfix: Fixed startup crash in Dark Souls 2 mode.

## Version 0.49.5

- New Feature: Added support for Dark Souls 2.
- New Feature: Added support for War Thunder.
- Bugfix: Fixed an infinite loop during the startup phase.
- Bugfix: Fixed a startup crash caused by missing or corrupt Mods/InstallInfo folder settings (the program will send the user to the required folders setup screen).
- Bugfix: Fixed a crash during the mod uninstallation process caused by broken install paths.
- Bugfix: Fixed checkboxes randomly disappearing from the plugin list.

## Version 0.49.4

- Bugfix: Fixed some instances of the “file is being used by another process” crash (mainly on local mod addition via “Add mod from file” button or drag&drop).
- Bugfix: Program update check failing if the user is not logged in.
- Bugfix: Crash when the category file is corrupt and the user selects No/Cancel at the category setup prompt.
- Bugfix: Crash while trying to change file associations and the antivirus prevents NMM from interacting with the Windows’ registry.
- Bugfix: Crash during the initial UAC check when the Install Info folder is not set.

## Version 0.49.3

- Bugfix: Program failing to perform a mod “upgrade install” for game modes not using plugins.
- Bugfix: Game scan crashing because of unexpected values for TESO install path in the registry.
- Bugfix: Crash during the UAC check caused by uninitialized folders.

## Version 0.49.2

- Bugfix: 32bit variables used to work with file/download size resulting in corrupt archives for huge mod files.
- Bugfix: Max download chunk size was too small (this *could* also improve the download speed on bigger files).
- Bugfix: Tweaked Min download chunk size for small files and threaded downloads.
- Bugfix: IO issues causing crashes on multiple local mod additions.
- Bugfix: Local mod addition messing up the download queue.

## Version 0.49.1

- Bugfix: NMM crashing while using the Reset UI button.
- Bugfix: Crash when reading a corrupt UI section from the config file.
- Bugfix: Resized UI elements on the Game Selection screen.

## Version 0.49.0

- New Feature: Added support for State of Decay.
- New Feature: Added support for Starbound.
- New Feature: Improved the “The Elder Scrolls Online” game detection.
- Bugfix: Fixes the installation of ESO addons on the EU version of the game.
- Bugfix: Some instances of visual category duplication while in category view.
- Bugfix: Clicking on add new category while in plain view was adding the category to the mod list.

## Version 0.48.2

- New Feature: Mod categories for TESO (if you were using one of the previous 0.48 versions you’ll need to click the small arrow besides the Category button on the left bar and select “Reset to Nexus defaults”)
- New Feature: The program will now get mod page links from the repository instead of guessing them.
- Bugfix: Offline warning message showing when there are no mods to check for updates.
- Bugfix: Category duplication while adding a mod in category view.
- Bugfix: Some mod entries duplicating when the search filter was active and any mod property was changed.
- Bugfix: Categories automatically un-hiding when a download completed.
- Bugfix: Added missing category to the Skyrim game mode (you’ll need to click the small arrow besides the Category button on the left bar and select “Reset to Nexus defaults” to locally update the category list).

## Version 0.48.1

- New Feature: Search box for the Mods panel! (CTRL+F will set focus to the search box)
- New Feature: The Elder Scrolls Online game support.
- New Feature: Witcher 2 mode will also handle mods created without the REDkit.
- New Feature: Game scan greatly improved for many games.
- Bugfix: HTML detection for the mod description mishandling some tags.
- Bugfix: Fixed some mod install issues for non-gamebryo games.
- Bugfix: The login screen will provide better feedback for login issues caused by the server.
- Bugfix: RAZUMOVSKY EGOR (sadgb) on Sourceforge fixed a crash on the filesearcher when encountering Linux paths.
- Bugfix: Fixed some cases of failed mod update checks.
- Bugfix: Added tracelog feedback for failed mod update checks (requires NMM to run in trace mode).

## Version 0.47.3

- Bugfix: Add mod from file asking the user to login (this time, for real).
- Bugfix: Mod update check cancel button not working during the startup phase.
- Bugfix: Unused options in the settings menu.
- Bugfix: Old tesnexus domain calls in some functions.

## Version 0.47.2

- New Feature: Better feedback on login issues.
- Bugfix: Add mod from file asking the user to login.
- Bugfix: Download speed messing up after a resume.
- Bugfix: Download from URL button disabled while offline.
- Bugfix: Cancelling a paused download after a program restart didn’t remove partial files.

## Version 0.47.1 hotfix

- Bugfix: Latest mod version not updating if there’s no new version.
- Bugfix: Mod category reset failing to check the login status.

## Version 0.47.0

- New Feature: Download queue system.
- New Feature: Login-less Mod manager startup.
- New Feature: New download options.
- New Feature: You will now be able to copy the messages in the download monitor (CTRL+C or right-click menu).
- New Feature: Button to seamlessly login and logout.
- New Feature: Button to purge all completed/failed downloads.
- New Feature: Button to resume all paused downloads.
- Bugfix: “Get missing mod info” panel messing endorsement and category info.
- Bugfix: Issues while moving multiple plugins on the loadorder.
- Bugfix: Loading logo form disappearing from the taskbar.
- Bugfix: Download retries.
- Bugfix: Multiple occurrences of the same file download crashing the manager.
- Bugfix: Download failing on “File not found”, NMM will now try other servers.

## Version 0.46.0

- New Feature: New web service support.
- New Feature: X: Rebirth support.
- Bugfix: Fixed NMM crashing when pausing corrupt downloads.
- Bugfix: NMM getting stuck for a few seconds while resuming a download.

## Version 0.45.7

- New Feature: The tracelog message will now link to the folder containing tracelog files (by clicking YES).
- New Feature: Added support for the new web calls.
- Bugfix: Incompatibility with some Dragon Age versions.
- Bugfix: Incompatibility with some Dragon Age 2 versions.
- Bugfix: Fixed a rare crash when confirming the paths on the game detection form.
- Bugfix: Fixed an issue causing Omods to fail to run their scripted installer.
- Bugfix: Fixed an issue causing the Readme Manager to intercept some script files.
- Bugfix: Readme Manager messing up the current folder during mod installations.
- Bugfix: Crash in both Dragon Age game modes while trying to uninstall a mod with a chargenmorphcfg file.
- Bugfix: Rare crash while downloading very small files.
- Bugfix: Fixed a crash when the Archive Invalidation failed to work (security, permissions, etc).
- Bugfix: Fixed an issue on the Game Detection form causing the OK button to become enabled when it shouldn’t.

## Version 0.45.6

- Bugfix: Game scan crashing while searching in invalid paths.
- Bugfix: Long timeout on some web requests causing the program to hang.
- Bugfix: Crash with scripted installers with invalid image paths.
- Bugfix: Skyrim default categories updated (you’ll need to use the “Categories: reset to Nexus site defaults” button to update your local categories).

## Version 0.45.5

- Bugfix: Crash related to checkboxes.
- Bugfix: Crash while retrieving the Mods/Install Info folder path from the registry.
- Bugfix: Crash when the user tries to cancel a stuck download.
- Bugfix: Rare crash while uninstalling a mod.
- Bugfix: Crash when resuming a download.

## Version 0.45.4

- Bugfix: Mod installation issue with nested paths in mod archives.

## Version 0.45.3

- Bugfix: Long startup times when huge mod archives are present. *will take effect the second time you run NMM after applying this update
- Bugfix: Dragon Age chargenmorphcfg merge failing with commented lines.
- Bugfix: Dragon Age chargenmorphcfg merge failing while uninstalling mods.

## Version 0.45.2

- Bugfix: Several issues while installing/uninstalling “Dragon Age Origins” mods.

## Version 0.45.1

- New Feature: “Dragon Age Origins” support, both loose files and dazip (NMM will automatically merge installed chargenmorphcfg.xml files, you’ll need to remove any previously manually installed chargenmorphcfg.xml file for this to properly work).
- New Feature: “Dragon Age 2” support (NMM will automatically merge installed chargenmorphcfg.xml files, you’ll need to remove any previously manually installed chargenmorphcfg.xml file for this to properly work).
- Bugfix: Crash when antivirus programs prevents NMM from uninstalling files.
- Bugfix: Crash when retrieving user credentials.
- Bugfix: Startup crash when blocked by some security suites.

## Version 0.44.16

- Bugfix: NMM unable to automatically download program updates.
- Bugfix: NMM crashing with manual login while there are connectivity issues.
- Bugfix: NMM Mods tab buttons being active when they shouldn’t.

## Version 0.44.15

- Bugfix: NMM crashing with auto login while there’s no connection available.

## Version 0.44.14

- New Feature: NMM will now properly handle web-side forced downtimes, warning the user about it.
- Bugfix: NMM misplacing Mods/InstallInfo folders while changing program settings.
- Bugfix: Crash when loading incorrect UI settings.
- Bugfix: Crash when trying to move plugins in the plugin list.
- Bugfix: Crash when trying to remove a non existant plugin.
- Bugfix: Crash while retrieving mod updates and received no answer from the web service.

## Version 0.44.13

- Bugfix: Fixed Witcher2 implementation.
- Bugfix: NMM slow loading times when big mod archives were present in the Mods directory (this is the first of two planned loading times fixes).

## Version 0.44.12

- New Feature: Added Witcher 2 REDKit mods support.
- Bugfix: NMM prompt windows popping in the background and requiring ALT+TAB to be reached.
- Bugfix: Clicking the Enable/Disable All plugin button caused the manager window to lose focus and go behind any currently open window.
- Bugfix: Fixed another antivirus crash, NMM will warn the user about the antivirus messing with it.
- Bugfix: InvalidCastException crash.
- Bugfix: Rare crash while renaming a category.
- Bugfix: Crash while trying to resume a file in this 2 cases: file became unavailable on the Nexus or the user starts NMM in online mode but then closes the connection.
- Bugfix: NMM will now force "Temp" as name for the custom temp folder to prevent users from selecting system/important folders with unexpected results.

## Version 0.44.11

- Bugfix: ZoneAlarm crashing NMM (the user will now be warned that ZoneAlarm is blocking the program execution).
- Bugfix: Multi uninstaller progress window closing way before the uninstallers where complete.
- Bugfix: When adding a new version of a mod NMM didn’t properly import the previous version’s settings (endorsement, category, show update warning).

## Version 0.44.10

- New Feature: The user will now be able to set a custom Temp folder (General settings menu).
- Bugfix: NMM unable to complete the mod download/add process.

## Version 0.44.9

- New Feature: The fileserver selection will now show country flags and european countries will be grouped under "European Union".
- New Feature: The user will now be able to set the Tracelog files folder (General settings tab).
- Bugfix: Drag&Drop not working on collapsed categories.
- Bugfix: Memory leak in mod file installation.
- Bugfix: New Czech server causing NMM to report “file does not exist” if the user set any fileserver zone different from default.
- Bugfix: NMM crashing while adding a mod and the Mods folder is on a network drive.

## Version 0.44.8

- Bugfix: Crash caused by duplicate entries in the Install Log file. (the dreaded "an item with the same key has already been added" crash).
- Bugfix: Uninstalling a plugin caused the program to remove it from the plugin list even if a previous version was restored.
- Bugfix: A readonly check preventing the program from restoring a previous file version if the file to restore was set to readonly.
- Bugfix: The mod manager will now automatically uninstall a mod whose mod archive is missing, instead of crashing the program.
- Bugfix: Deleting a mod while having multiple mods selected also removed all the selected mods from the list even though the mod deletion method only works on a single mods.
- Bugfix: Rare crash with the saved column sorting.
- Bugfix: Bug that prevented the program from obtaining updated mod info from a new version of the mod archive.

## Version 0.44.7

- Bugfix: Some Omod files failing to install.
- Bugfix: Rare crash while renaming mods.
- Bugfix: Messagebox for the automatic program update check popping on the background and blocking the program execution.

## Version 0.44.6

- New Feature: New options in the Mod Update Warning context menu, you will now be able to enable and disable the warning for all the mods with a single click. In the settings menu you will also be able to choose whether to show the greyed out icon or no icon at all.
- New Feature: The program update prompt will now link the user to the latest version change log.
- Bugfix: Crash while trying to uninstall mods that installed bloated paths.
- Bugfix: NMM showing a duplicate mod entry when overwriting an already present mod file.
- Bugfix: NMM unable to load custom mod screenshots for some archives.
- Bugfix: import/export load order functionality didn't properly handle the plugin’s activation state.

## Version 0.44.5

- Bugfix: Another bug causing NMM to show duplicate mods in the list. (if the program should warn you about a local mod version mismatch, answer NO for every mod, and it won’t bother you anymore)
- Bugfix: Mod update warning resetting after a “mod category reset” or “get missing mod info”.
- Bugfix: “unactive” typo.

## Version 0.44.4

- New Feature: The ReadMeManager now supports PDF files.
- Bugfix: ReadMe Manager preventing .txt files from being installed in mod folders. (This fixes the installation of mods like Fores New Idles)
- Bugfix: Rare crash while deleting a mod in CategoryView.
- Bugfix: Omod script interpreter.
- Bugfix: Users were able to set Mods and Install Info as the same folder.
- Bugfix: NMM crashing with a corrupt ReadMeManager.xml file.
- Bugfix: NMM using the wrong game path after a game rescan.
- Bugfix: LoadOrder export using the wrong plugin list.

## Version 0.44.3

- New Feature: NMM will now prevent ReadMe files from being extracted in the mod folder (thus only installing them in the ReadMe Manager), you can set this behaviour on/off in the Settings menu (Default on).
- New Feature: The user can now select the game mode by doubleclicking on it.
- Bugfix: Mods failing to deactivate on upgrade.
- Bugfix: Category view failing to update after mod rename.
- Bugfix: Mod list not re-sorting after mod/category name change.
- Bugfix: NMM mods startup scan searching in the readme folder (causing some duplicate entries in the mod list).
- Bugfix: NMM crashing when another instance of the program is already running.
- Bugfix: "Yes to All" not working as intended.
- Bugfix: Some OMOD crashes (comment lines and "misplaced" curly brackets).

## Version 0.44.2

- New Feature: New “Uninstall all active mods” button in the Tools menu.
- New Feature: The readme scan is now optional and you can manually perform it by right-clicking on selected mods/categories and choosing the option from the context menu.
- Bugfix: Category view: category and mod properties failing to properly update on the list.
- Bugfix: Category view: NMM freezing when trying to delete a mod from the list.
- Bugfix: WoT manager crashing when the game was using a malformed version number.
- Bugfix: NMM crashing when checking for mod updates and the server was unreachable.
- Bugfix: NMM reporting a mod as installed/uninstalled even though the user aborted the scripted install/uninstall or there was an error performing it.
- Bugfix: NMM failing to properly load saved column sizes.
- Bugfix: Readme Manager creating readme archive files into the wrong folder for newly downloaded mods.
- Bugfix: Readme Manager setup adding random mod files in readme archives.

## Version 0.44.1 - Hotfix

- Suggestion: We advise you to perform a clean install of this new version to prevent any leftover bug in config files to further mess with your mods (Uninstall NMM removing config files when asked, and then make sure the program folder has been removed, this won’t remove your mods or mod install info).
- Bugfix: Non-categorized view unable to update.
- Bugfix: An inadvertently commented line caused NMM to duplicate some mods.

## Version 0.44.0

- New Feature: ReadMe Manager, quick access to mods readme files (right click on the mod and select the read me to open from the list).
- New Feature: Enabled the toggle update warning on categories.
- New Feature: NMM will now remember the last sorting (column and order) used while in category view.
- New Feature: Adds a new icon for mods with the update warning disabled that could be outdated.
- New Feature: The mod list view is now loaded into the new control.
- Bugfix: NMM crashing while the UAC check was unable to parse an illegal folder path.
- Bugfix: Readonly check preventing NMM to restore overwritten files on mod uninstall.
- Bugfix: Mod install failing because of readonly files.
- Bugfix: Mod update check resetting the update warning icon to false.
- Bugfix: Rare crash while removing temporary mod installation files.
- Bugfix: Sorting any column in descending order in category view reversed the category list order.
- Bugfix: Ugly ToggleSidePanel button removed from the UI (there's now a setting in the General settings menu).
- Bugfix: Some buttons will be set as invisible when disabled to reduce UI clutter.
