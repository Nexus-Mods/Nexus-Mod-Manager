namespace Nexus.Client
{
	using System;
	using System.Collections.Generic;
	using System.ComponentModel;
	using System.Drawing;
	using System.Windows.Forms;

	using DevExpress.XtraBars;
	using DevExpress.XtraEditors.Repository;

	using Nexus.Client.Commands;
	using Nexus.Client.Games.Tools;
	using Nexus.Client.UI;

	public partial class MainForm
	{
		private BarManager barManagerMain;
		private Bar barMainToolbar;
		private Bar barStatus;
		private StandaloneBarDockControl barMainToolbarHost;
		private StandaloneBarDockControl barStatusHost;

		private const int MainToolbarHeight = 43;
		private const int MainToolbarImageSize = 36;
		private const int StatusBarHeight = 36;
		private const int StatusBarImageSize = 16;

		private BarButtonItem spbLaunch;
		private BarButtonItem spbProfiles;
		private BarButtonItem spbHelp;
		private BarButtonItem spbChangeMode;
		private BarButtonItem toolStripSplitButtonTools;
		private BarButtonItem spbFolders;
		private BarButtonItem tsbSettings;
		private BarButtonItem spbSupportedTools;
		private BarEditItem toolStripTextBoxFind;
		private RepositoryItemTextEdit repositoryFind;
		private BarButtonItem tsbUpdate;
		private BarButtonItem tsbYouTube;
		private BarButtonItem tsbDiscord;
		private BarButtonItem spbSupportNMM;
		private BarButtonItem tsbiPatreon;
		private BarButtonItem tsbiKofi;

		private PopupMenu popupLaunch;
		private PopupMenu popupProfiles;
		private PopupMenu popupHelp;
		private PopupMenu popupChangeMode;
		private PopupMenu popupTools;
		private PopupMenu popupFolders;
		private PopupMenu popupSupportedTools;
		private PopupMenu popupSupportNMM;

		private BarButtonItem toolStripButtonOnlineStatus;
		private BarStaticItem toolStripLabelDownloads;
		private DownloadProgressBarItem toolStripProgressBarDownloadSpeed;
		private BarButtonItem toolStripButtonGoPremium;
		private BarStaticItem toolStripLabelLoginMessage;
		private BarButtonItem toolStripButtonRateLimit;
		private BarStaticItem toolStripLabelBottomBarFeedback;
		private BarStaticItem toolStripLabelBottomBarFeedbackCounter;
		private BarButtonItem toolStripButtonLoader;
		private BarStaticItem toolStripLabelPluginsCounter;
		private BarStaticItem toolStripLabelActivePluginsCounter;
		private BarStaticItem tlbModsCounter;
		private BarStaticItem tsbSkyrimDownloads;

		private BarButtonItem _launchDefaultItem;
		private BarItem _profileDefaultItem;
		private Dictionary<BarItem, DevExpressBarItemCommandBinding> _mainBarCommandBindings;
		private List<Command> _changeModeCommandsWithExecutedHandler;

		/// <summary>
		/// Creates the DevExpress main toolbar and status bar items.
		/// </summary>
		private void InitializeMainBars()
		{
			barManagerMain = new BarManager
			{
				Form = this,
				AllowCustomization = false
			};
			components.Add(barManagerMain);
			_mainBarCommandBindings = new Dictionary<BarItem, DevExpressBarItemCommandBinding>();
			_changeModeCommandsWithExecutedHandler = new List<Command>();

			InitializeBarHosts();

			barMainToolbar = new Bar(barManagerMain, "Main Toolbar")
			{
				DockStyle = BarDockStyle.Standalone,
				StandaloneBarDockControl = barMainToolbarHost
			};
			barMainToolbar.OptionsBar.AllowQuickCustomization = false;
			barMainToolbar.OptionsBar.DisableClose = true;
			barMainToolbar.OptionsBar.DisableCustomization = true;
			barMainToolbar.OptionsBar.DrawDragBorder = false;
			barMainToolbar.OptionsBar.UseWholeRow = true;

			barStatus = new Bar(barManagerMain, "Status Bar")
			{
				DockStyle = BarDockStyle.Standalone,
				StandaloneBarDockControl = barStatusHost
			};
			barStatus.OptionsBar.AllowQuickCustomization = false;
			barStatus.OptionsBar.DisableClose = true;
			barStatus.OptionsBar.DisableCustomization = true;
			barStatus.OptionsBar.DrawDragBorder = false;
			barStatus.OptionsBar.UseWholeRow = true;
			barManagerMain.StatusBar = barStatus;

			InitializeMainToolbarItems();
			InitializeStatusBarItems();
		}

		/// <summary>
		/// Adds the toolbar links after the skin and display selectors have been created.
		/// </summary>
		private void BuildMainToolbarLinks()
		{
			barMainToolbar.ClearLinks();
			AddMainToolbarItem(spbLaunch);
			AddMainToolbarItem(spbProfiles);
			AddMainToolbarItem(toolStripSplitButtonTools, true);
			AddMainToolbarItem(spbFolders);
			AddMainToolbarItem(tsbSettings);
			AddMainToolbarItem(_devExpressSkinLabel, true);
			AddMainToolbarItem(_devExpressSkinComboBox);
			AddMainToolbarItem(_devExpressDisplayButton);
			AddMainToolbarItem(spbSupportedTools, true);
			AddMainToolbarItem(toolStripTextBoxFind);

			AddMainToolbarItem(spbSupportNMM, true);
			AddMainToolbarItem(tsbYouTube);
			AddMainToolbarItem(tsbDiscord);
			AddMainToolbarItem(tsbUpdate);
			AddMainToolbarItem(spbChangeMode);
			AddMainToolbarItem(spbHelp);
		}

		/// <summary>
		/// Adds a single item to the main toolbar.
		/// </summary>
		/// <param name="item">The item to add.</param>
		/// <param name="beginGroup">Whether a separator should precede the item.</param>
		private void AddMainToolbarItem(BarItem item, bool beginGroup = false)
		{
			if (item == null)
				return;

			BarItemLink link = barMainToolbar.AddItem(item);
			link.BeginGroup = beginGroup;
		}

		/// <summary>
		/// Creates fixed-height standalone hosts for the main toolbar and status bar.
		/// </summary>
		private void InitializeBarHosts()
		{
			barMainToolbarHost = new StandaloneBarDockControl
			{
				CausesValidation = false,
				Dock = DockStyle.Top,
				Height = MainToolbarHeight,
				MinimumSize = new Size(0, MainToolbarHeight),
				MaximumSize = new Size(0, MainToolbarHeight)
			};

			barStatusHost = new StandaloneBarDockControl
			{
				CausesValidation = false,
				Dock = DockStyle.Bottom,
				Height = StatusBarHeight,
				MinimumSize = new Size(0, StatusBarHeight),
				MaximumSize = new Size(0, StatusBarHeight)
			};

			Controls.Add(barStatusHost);
			Controls.Add(barMainToolbarHost);
			barManagerMain.DockControls.Add(barMainToolbarHost);
			barManagerMain.DockControls.Add(barStatusHost);
			barMainToolbarHost.BringToFront();
			barStatusHost.BringToFront();
		}

		/// <summary>
		/// Creates the fixed main-toolbar actions and their popup menus.
		/// </summary>
		private void InitializeMainToolbarItems()
		{
			ComponentResourceManager resources = new ComponentResourceManager(typeof(MainForm));

			popupLaunch = new PopupMenu(barManagerMain);
			popupProfiles = new PopupMenu(barManagerMain);
			popupHelp = new PopupMenu(barManagerMain);
			popupChangeMode = new PopupMenu(barManagerMain);
			popupTools = new PopupMenu(barManagerMain);
			popupFolders = new PopupMenu(barManagerMain);
			popupSupportedTools = new PopupMenu(barManagerMain);
			popupSupportNMM = new PopupMenu(barManagerMain);

			spbLaunch = CreateDropDownButton("Launch Game", popupLaunch, false, BarItemPaintStyle.CaptionGlyph);
			spbLaunch.ImageOptions.Image = ScaleBarImage(resources.GetObject("spbLaunch.Image") as Image, MainToolbarImageSize);
			spbLaunch.ItemClick += SpbLaunch_ItemClick;

			spbProfiles = CreateDropDownButton("Profiles", popupProfiles, false, BarItemPaintStyle.CaptionGlyph);
			spbProfiles.ItemClick += SpbProfiles_ItemClick;

			spbHelp = CreateDropDownButton("Help", popupHelp, true, BarItemPaintStyle.Standard);
			spbHelp.Alignment = BarItemLinkAlignment.Right;
			spbHelp.ImageOptions.Image = ScaleBarImage(Properties.Resources.help_flat, MainToolbarImageSize);

			spbChangeMode = CreateDropDownButton("Change Game Mode", popupChangeMode, true, BarItemPaintStyle.Standard);
			spbChangeMode.Alignment = BarItemLinkAlignment.Right;
			spbChangeMode.ImageOptions.Image = ScaleBarImage(Properties.Resources.switch_game_flat, MainToolbarImageSize);

			toolStripSplitButtonTools = CreateDropDownButton("Tools", popupTools, true, BarItemPaintStyle.Standard);
			toolStripSplitButtonTools.ImageOptions.Image = ScaleBarImage(Properties.Resources.program_tools_flat, MainToolbarImageSize);

			spbFolders = CreateDropDownButton("Open folders", popupFolders, true, BarItemPaintStyle.Standard);
			spbFolders.ImageOptions.Image = ScaleBarImage(Properties.Resources.folder_link_flat, MainToolbarImageSize);

			tsbSettings = new BarButtonItem(barManagerMain, "Settings")
			{
				PaintStyle = BarItemPaintStyle.Standard
			};
			tsbSettings.ImageOptions.Image = ScaleBarImage(Properties.Resources.settings_flat, MainToolbarImageSize);
			tsbSettings.ItemClick += (sender, args) => tsbSettings_Click(sender, EventArgs.Empty);

			spbSupportedTools = CreateDropDownButton("Supported Tools", popupSupportedTools, true, BarItemPaintStyle.Standard);
			spbSupportedTools.ImageOptions.Image = ScaleBarImage(Properties.Resources.supported_tools_flat, MainToolbarImageSize);

			repositoryFind = new RepositoryItemTextEdit();
			repositoryFind.KeyUp += tstFind_KeyUp;
			barManagerMain.RepositoryItems.Add(repositoryFind);
			toolStripTextBoxFind = new BarEditItem(barManagerMain, repositoryFind)
			{
				EditWidth = 120,
				EditValue = String.Empty,
				Visibility = BarItemVisibility.Never
			};

			tsbUpdate = new BarButtonItem(barManagerMain, "Check for Updates")
			{
				Alignment = BarItemLinkAlignment.Right,
				PaintStyle = BarItemPaintStyle.Standard
			};
			tsbUpdate.ImageOptions.Image = ScaleBarImage(Properties.Resources.update_check_flat, MainToolbarImageSize);

			tsbDiscord = new BarButtonItem(barManagerMain, "Discord")
			{
				Alignment = BarItemLinkAlignment.Right,
				Hint = "Join the Official NMM Community Discord",
				PaintStyle = BarItemPaintStyle.Standard
			};
			tsbDiscord.ImageOptions.Image = ScaleBarImage(Properties.Resources.discord_logo_512, MainToolbarImageSize);
			tsbDiscord.ItemClick += (sender, args) => tsbDiscord_Click(sender, EventArgs.Empty);

			tsbYouTube = new BarButtonItem(barManagerMain, "YouTube")
			{
				Alignment = BarItemLinkAlignment.Right,
				Hint = "Watch the official NMM Community Edition YouTube channel",
				PaintStyle = BarItemPaintStyle.Standard
			};
			tsbYouTube.ImageOptions.Image = ScaleBarImage(Properties.Resources.youtube_logo_512, MainToolbarImageSize);
			tsbYouTube.ItemClick += (sender, args) => tsbYouTube_Click(sender, EventArgs.Empty);

			spbSupportNMM = CreateDropDownButton("Support the NMM development", popupSupportNMM, false, BarItemPaintStyle.Standard);
			spbSupportNMM.Alignment = BarItemLinkAlignment.Right;
			spbSupportNMM.ImageOptions.Image = ScaleBarImage(Properties.Resources.kofi_button, MainToolbarImageSize);
			spbSupportNMM.ItemClick += (sender, args) => spbSupportNMM_ButtonClick(sender, EventArgs.Empty);

			tsbiPatreon = new BarButtonItem(barManagerMain, "Donate on Patreon");
			tsbiPatreon.ImageOptions.Image = ScaleBarImage(Properties.Resources.Digital_Patreon_Logo_FieryCoral, StatusBarImageSize);
			tsbiPatreon.ItemClick += (sender, args) => tsbiPatreon_Click(sender, EventArgs.Empty);
			popupSupportNMM.AddItem(tsbiPatreon);

			tsbiKofi = new BarButtonItem(barManagerMain, "Donate on Ko-fi");
			tsbiKofi.ImageOptions.Image = ScaleBarImage(Properties.Resources.kofi_button, StatusBarImageSize);
			tsbiKofi.ItemClick += (sender, args) => tsbiKofi_Click(sender, EventArgs.Empty);
			popupSupportNMM.AddItem(tsbiKofi);
		}

		/// <summary>
		/// Creates a DevExpress toolbar button with an associated popup menu.
		/// </summary>
		/// <param name="caption">The button caption.</param>
		/// <param name="popupMenu">The menu displayed by the drop-down portion.</param>
		/// <param name="actAsDropDown">Whether the whole button should open the menu.</param>
		/// <param name="paintStyle">The toolbar paint style.</param>
		/// <returns>The configured bar button item.</returns>
		private BarButtonItem CreateDropDownButton(string caption, PopupMenu popupMenu, bool actAsDropDown, BarItemPaintStyle paintStyle)
		{
			return new BarButtonItem(barManagerMain, caption)
			{
				ButtonStyle = BarButtonStyle.DropDown,
				DropDownControl = popupMenu,
				ActAsDropDown = actAsDropDown,
				PaintStyle = paintStyle
			};
		}

		/// <summary>
		/// Creates the DevExpress status-bar items.
		/// </summary>
		private void InitializeStatusBarItems()
		{
			toolStripButtonOnlineStatus = new BarButtonItem(barManagerMain, "Login")
			{
				PaintStyle = BarItemPaintStyle.Standard
			};
			toolStripButtonOnlineStatus.ImageOptions.Image = ScaleBarImage(Properties.Resources.loggedout_flat, 32);

			toolStripLabelDownloads = new BarStaticItem
			{
				Manager = barManagerMain,
				Caption = String.Empty
			};

			toolStripProgressBarDownloadSpeed = new DownloadProgressBarItem(barManagerMain)
			{
				Maximum = 100,
				Value = 0,
				OptionalValue = 0,
				ShowOptionalProgress = true,
				ColorFillMode = DownloadProgressBarItem.FillType.Fixed
			};

			toolStripButtonGoPremium = new BarButtonItem(barManagerMain, String.Empty);
			toolStripLabelLoginMessage = new BarStaticItem { Manager = barManagerMain };
			toolStripButtonRateLimit = new BarButtonItem(barManagerMain, "Rate Limit")
			{
				PaintStyle = BarItemPaintStyle.Standard
			};
			toolStripButtonRateLimit.ImageOptions.Image = ScaleBarImage(Properties.Resources.token_info, StatusBarImageSize);
			toolStripButtonRateLimit.ItemClick += (sender, args) => ToolStripButtonRateLimitOnClick(sender, EventArgs.Empty);

			toolStripLabelBottomBarFeedback = new BarStaticItem { Manager = barManagerMain };
			toolStripLabelBottomBarFeedbackCounter = new BarStaticItem { Manager = barManagerMain };
			toolStripButtonLoader = new BarButtonItem(barManagerMain, "Activity")
			{
				PaintStyle = BarItemPaintStyle.Standard,
				Visibility = BarItemVisibility.Never
			};
			toolStripButtonLoader.ImageOptions.Image = ScaleBarImage(Properties.Resources.round_loading, StatusBarImageSize);

			toolStripLabelPluginsCounter = new BarStaticItem
			{
				Manager = barManagerMain,
				Caption = "Total plugins / Active plugins"
			};
			toolStripLabelActivePluginsCounter = new BarStaticItem { Manager = barManagerMain };
			tlbModsCounter = new BarStaticItem
			{
				Manager = barManagerMain,
				Caption = "Total mods / Active mods"
			};
			tsbSkyrimDownloads = new BarStaticItem
			{
				Manager = barManagerMain,
				Caption = "Skyrim Downloads",
				Visibility = BarItemVisibility.Never
			};

			barStatus.AddItem(toolStripButtonOnlineStatus);
			barStatus.AddItem(toolStripLabelDownloads);
			barStatus.AddItem(toolStripProgressBarDownloadSpeed);
			barStatus.AddItem(toolStripButtonGoPremium);
			barStatus.AddItem(toolStripLabelLoginMessage);
			barStatus.AddItem(toolStripButtonRateLimit);
			barStatus.AddItem(toolStripLabelBottomBarFeedback).BeginGroup = true;
			barStatus.AddItem(toolStripLabelBottomBarFeedbackCounter);
			barStatus.AddItem(toolStripButtonLoader);

			tlbModsCounter.Alignment = BarItemLinkAlignment.Right;
			toolStripLabelPluginsCounter.Alignment = BarItemLinkAlignment.Right;
			toolStripLabelActivePluginsCounter.Alignment = BarItemLinkAlignment.Right;
			tsbSkyrimDownloads.Alignment = BarItemLinkAlignment.Right;

			barStatus.AddItem(tlbModsCounter);
			barStatus.AddItem(toolStripLabelActivePluginsCounter);
			barStatus.AddItem(toolStripLabelPluginsCounter);
			barStatus.AddItem(tsbSkyrimDownloads);
		}

		/// <summary>
		/// Creates a bounded bitmap for a DevExpress bar item so large source resources cannot resize the host bar.
		/// </summary>
		/// <param name="image">The source image.</param>
		/// <param name="size">The square target size in pixels.</param>
		/// <returns>A scaled bitmap, or <c>null</c> when no source image is supplied.</returns>
		private static Image ScaleBarImage(Image image, int size)
		{
			if (image == null)
				return null;

			return new Bitmap(image, new Size(size, size));
		}

		/// <summary>
		/// Executes the currently selected default launch command.
		/// </summary>
		private void SpbLaunch_ItemClick(object sender, ItemClickEventArgs e)
		{
			Command command = _launchDefaultItem?.Tag as Command;
			command?.Execute();
		}

		/// <summary>
		/// Executes the currently selected default profile entry when one is available.
		/// </summary>
		private void SpbProfiles_ItemClick(object sender, ItemClickEventArgs e)
		{
			if (_profileDefaultItem == null || !_profileDefaultItem.Enabled)
				return;

			HandleProfileItemClick(_profileDefaultItem);
		}

		/// <summary>
		/// Focuses the DevExpress find editor in the main toolbar.
		/// </summary>
		private void FocusMainFindEditor()
		{
			foreach (BarItemLink link in toolStripTextBoxFind.Links)
			{
				BarEditItemLink editLink = link as BarEditItemLink;
				if (editLink == null || !editLink.Visible)
					continue;

				editLink.Focus();
				return;
			}
		}

		/// <summary>
		/// Binds an existing DevExpress bar item to a command, replacing any previous binding.
		/// </summary>
		/// <param name="item">The existing bar item.</param>
		/// <param name="command">The command to bind.</param>
		private void BindExistingBarItem(BarItem item, Command command)
		{
			if (item == null)
				throw new ArgumentNullException(nameof(item));
			if (command == null)
				throw new ArgumentNullException(nameof(command));

			DevExpressBarItemCommandBinding existingBinding;
			if (_mainBarCommandBindings.TryGetValue(item, out existingBinding))
				existingBinding.Unbind();

			_mainBarCommandBindings[item] = new DevExpressBarItemCommandBinding(item, command);
		}

		/// <summary>
		/// Creates a DevExpress button bound to an NMM command.
		/// </summary>
		/// <param name="command">The command represented by the button.</param>
		/// <param name="image">An optional image that overrides a missing command image.</param>
		/// <returns>The bound DevExpress button.</returns>
		private BarButtonItem CreateCommandBarButton(Command command, Image image = null)
		{
			if (command == null)
				throw new ArgumentNullException(nameof(command));

			BarButtonItem item = new BarButtonItem(barManagerMain, command.Name)
			{
				Tag = command
			};

			BindExistingBarItem(item, command);

			if (item.ImageOptions.Image == null && image != null)
				item.ImageOptions.Image = ScaleBarImage(image, StatusBarImageSize);

			return item;
		}

		/// <summary>
		/// Clears and disposes transient items in a popup menu, including command bindings and nested popups.
		/// </summary>
		/// <param name="popupMenu">The popup menu whose transient items should be released.</param>
		private void ClearTransientPopupItems(PopupMenu popupMenu)
		{
			if (popupMenu == null)
				return;

			List<BarItem> items = new List<BarItem>();
			foreach (BarItemLink link in popupMenu.ItemLinks)
			{
				if (link.Item != null && !items.Contains(link.Item))
					items.Add(link.Item);
			}

			popupMenu.ClearLinks();
			foreach (BarItem item in items)
				DisposeTransientBarItem(item);
		}

		/// <summary>
		/// Releases one transient bar item and any nested popup items it owns.
		/// </summary>
		/// <param name="item">The transient item to release.</param>
		private void DisposeTransientBarItem(BarItem item)
		{
			if (item == null)
				return;

			BarSubItem subItem = item as BarSubItem;
			if (subItem != null)
			{
				List<BarItem> childItems = new List<BarItem>();
				foreach (BarItemLink childLink in subItem.ItemLinks)
				{
					if (childLink.Item != null && !childItems.Contains(childLink.Item))
						childItems.Add(childLink.Item);
				}

				subItem.ClearLinks();
				foreach (BarItem childItem in childItems)
					DisposeTransientBarItem(childItem);
			}

			BarButtonItem buttonItem = item as BarButtonItem;
			PopupMenu nestedPopup = buttonItem?.DropDownControl as PopupMenu;
			if (nestedPopup != null)
			{
				ClearTransientPopupItems(nestedPopup);
				nestedPopup.Dispose();
			}

			DevExpressBarItemCommandBinding binding;
			if (_mainBarCommandBindings != null && _mainBarCommandBindings.TryGetValue(item, out binding))
			{
				binding.Unbind();
				_mainBarCommandBindings.Remove(item);
			}

			item.Dispose();
		}

		/// <summary>
		/// Releases command and tool event subscriptions owned by the main DevExpress bars.
		/// </summary>
		private void DisposeMainBarResources()
		{
			if (_changeModeCommandsWithExecutedHandler != null)
			{
				foreach (Command command in _changeModeCommandsWithExecutedHandler)
					command.Executed -= ChangeGameModeCommand_Executed;
				_changeModeCommandsWithExecutedHandler.Clear();
			}

			foreach (ITool tool in _boundGameTools)
			{
				tool.DisplayToolView -= Tool_DisplayToolView;
				tool.CloseToolView -= Tool_CloseToolView;
			}
			_boundGameTools.Clear();

			if (_mainBarCommandBindings != null)
			{
				foreach (DevExpressBarItemCommandBinding binding in _mainBarCommandBindings.Values)
					binding.Unbind();
				_mainBarCommandBindings.Clear();
			}
		}

		/// <summary>
		/// Sets a bar item's visibility using the DevExpress visibility model.
		/// </summary>
		private static void SetBarItemVisible(BarItem item, bool visible)
		{
			if (item != null)
				item.Visibility = visible ? BarItemVisibility.Always : BarItemVisibility.Never;
		}

		/// <summary>
		/// Gets the effective font used by a DevExpress bar item.
		/// </summary>
		private Font GetBarItemFont(BarItem item)
		{
			return item?.ItemAppearance?.Normal?.Font ?? Font;
		}

		/// <summary>
		/// Applies a font style while preserving the effective bar-item font family and size.
		/// </summary>
		private void SetBarItemFontStyle(BarItem item, FontStyle style)
		{
			if (item == null)
				return;

			Font currentFont = GetBarItemFont(item);
			item.ItemAppearance.Normal.Font = new Font(currentFont, style);
			item.ItemAppearance.Normal.Options.UseFont = true;
		}

		/// <summary>
		/// Applies or clears a foreground-color override for a DevExpress bar item.
		/// </summary>
		private static void SetBarItemForeColor(BarItem item, Color color)
		{
			if (item == null)
				return;

			item.ItemAppearance.Normal.ForeColor = color;
			item.ItemAppearance.Normal.Options.UseForeColor = !color.IsEmpty;
		}

		/// <summary>
		/// Gets the current text from the DevExpress find editor.
		/// </summary>
		private string MainFindText => Convert.ToString(toolStripTextBoxFind.EditValue) ?? String.Empty;
	}
}
