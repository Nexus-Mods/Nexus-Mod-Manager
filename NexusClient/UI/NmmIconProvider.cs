namespace Nexus.Client.UI
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.Drawing;
	using System.IO;
	using System.Linq;
	using System.Reflection;

	using DevExpress.LookAndFeel;
	using DevExpress.Skins;
	using DevExpress.Utils;
	using DevExpress.Utils.Drawing;
	using DevExpress.Utils.Svg;
	using DevExpress.XtraBars;
	using DevExpress.XtraEditors;

	internal enum NmmIconAction
	{
		Add,
		AddFile,
		AddUrl,
		InstallEnable,
		InstallRoot,
		Disable,
		Delete,
		Refresh,
		GetModInfo,
		Launch,
		Profiles,
		Help,
		ChangeGame,
		Tools,
		OpenFolder,
		Settings,
		SupportedTools,
		CheckUpdates,
		UpdateApplication,
		Save,
		MoveUp,
		MoveDown,
		Sort,
		EnableAll,
		DisableAll,
		Endorse,
		Categories,
		Collapse,
		Expand,
		Sync,
		UpdateResetCategories,
		Reset,
		ResetUnassigned,
		ResetAll,
		SwitchView,
		Export,
		Import,
		ImportFromClipboard,
		ImportFromFile,
		Copy,
		Filter,
		DownloadMode,
		DisplayOptions,
		Layout,
		Pause,
		Resume,
		Cancel,
		Remove,
		RemoveAll,
		Purge,
		Rename,
		Backup,
		Restore,
		Apply,
		Authorize,
		ExternalLink,
		Screenshot,
		Clear,
		Browse,
		Repair,
		Warning,
		Uninstall,
		Reinstall,
		Restrictions
	}

	internal enum NmmIconStyle
	{
		Minimal,
		Classic
	}

	internal enum NmmIconColorProfile
	{
		Base
	}

	internal enum NmmButtonPresentation
	{
		TextOnly,
		IconsOnly,
		TextAndIcons
	}

	/// <summary>
	/// Central source for NMM semantic SVG actions. Assets are loaded once, while
	/// style, size and semantic colors are applied independently at runtime.
	/// </summary>
	internal static class NmmIconProvider
	{
		private const string ResourceMarker = ".Resources.Icons.";
		private const int DefaultIconSize = 20;
		private static readonly object SyncRoot = new object();
		private static readonly Dictionary<string, string> ResourceIndex = BuildResourceIndex();
		private static readonly Dictionary<string, SvgImage> SvgCache = new Dictionary<string, SvgImage>(StringComparer.OrdinalIgnoreCase);
		private static readonly Dictionary<string, Image> BitmapCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
		private static readonly HashSet<string> MissingResourceWarnings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private static readonly List<IconBinding> Bindings = new List<IconBinding>();
		private static readonly List<WeakReference> PresentationOnlyBarItems = new List<WeakReference>();
		private static readonly List<BarPresentationSurface> BarSurfaces = new List<BarPresentationSurface>();

		private static NmmIconStyle _style = NmmIconStyle.Minimal;
		private static int _iconSize = DefaultIconSize;
		private static NmmIconColorProfile _colorProfile = NmmIconColorProfile.Base;
		private static NmmButtonPresentation _buttonPresentation = NmmButtonPresentation.TextAndIcons;
		private static bool _darkSurface;
		private static string _paletteStamp;

		internal static NmmIconStyle CurrentStyle { get { return _style; } }
		internal static int CurrentIconSize { get { return _iconSize; } }
		internal static NmmIconColorProfile CurrentColorProfile { get { return _colorProfile; } }
		internal static NmmButtonPresentation CurrentButtonPresentation { get { return _buttonPresentation; } }

		/// <summary>
		/// Applies the current global icon choices and refreshes every registered UI action.
		/// </summary>
		internal static void ApplySettings(NmmIconStyle style, int iconSize, NmmIconColorProfile colorProfile, NmmButtonPresentation buttonPresentation)
		{
			lock (SyncRoot)
			{
				int resolvedIconSize = NormalizeIconSize(iconSize);
				bool settingsChanged = _style != style ||
					_iconSize != resolvedIconSize ||
					_colorProfile != colorProfile ||
					_buttonPresentation != buttonPresentation;
				_style = style;
				_iconSize = resolvedIconSize;
				_colorProfile = colorProfile;
				_buttonPresentation = buttonPresentation;

				if (settingsChanged)
					ClearBitmapCacheCore();

				ApplyPaletteCore(false);
				RefreshBindingsCore();
				RefreshBarSurfacesCore();
			}
		}

		/// <summary>
		/// Re-resolves semantic colors after a skin/palette change without reloading SVG assets.
		/// </summary>
		internal static void RefreshForCurrentSkin()
		{
			lock (SyncRoot)
			{
				ApplyPaletteCore(true);
				ClearBitmapCacheCore();
				RefreshBindingsCore();
				RefreshBarSurfacesCore();
			}
		}

		internal static void Bind(BarItem item, NmmIconAction action)
		{
			Bind(item, action, null);
		}

		internal static void Bind(BarItem item, NmmIconAction action, Image preferredImage)
		{
			if (item == null)
				return;

			lock (SyncRoot)
			{
				ApplyPaletteCore(false);
				RegisterBindingCore(item, action, preferredImage);
				EnsureBarItemToolTip(item);
				ApplyBindingCore(item, action, preferredImage, null);
				RefreshBarSurfacesCore();
			}
		}

		/// <summary>
		/// Registers a bar item whose native/brand image must be preserved while still
		/// participating in Text/Icon presentation changes.
		/// </summary>
		internal static void BindPresentationOnly(BarItem item)
		{
			if (item == null)
				return;

			lock (SyncRoot)
			{
				RegisterPresentationOnlyBarItemCore(item);
				EnsureBarItemToolTip(item);
				RefreshBarSurfacesCore();
			}
		}

		internal static void Bind(SimpleButton button, NmmIconAction action)
		{
			if (button == null || button.IsDisposed)
				return;

			lock (SyncRoot)
			{
				ApplyPaletteCore(false);
				IconBinding binding = RegisterBindingCore(button, action);
				EnsureSimpleButtonTextTrackingCore(button, binding);
				EnsureSimpleButtonToolTip(button);
				ApplyBindingCore(button, action, null, binding);
			}
		}

		/// <summary>
		/// Registers a toolbar surface for global button presentation. Individual menu
		/// links are deliberately left untouched so menu captions remain visible.
		/// </summary>
		internal static void BindBar(Bar bar, bool vertical)
		{
			if (bar == null)
				return;

			lock (SyncRoot)
			{
				RegisterBarSurfaceCore(bar, vertical);
				ApplyBarPresentationCore(bar, vertical);
			}
		}

		/// <summary>
		/// Compatibility path for controls that cannot consume SvgImage directly.
		/// Normal DevExpress controls should use Bind instead.
		/// </summary>
		internal static Image GetBitmap(NmmIconAction action, int size, bool disabled)
		{
			lock (SyncRoot)
			{
				ApplyPaletteCore(false);
				int resolvedSize = NormalizeBitmapSize(size);
				string cacheKey = String.Format(
					"{0}|{1}|{2}|{3}|{4}|{5}",
					_style,
					action,
					resolvedSize,
					_colorProfile,
					_darkSurface,
					disabled);

				Image cached;
				if (BitmapCache.TryGetValue(cacheKey, out cached))
					return cached;

				SvgImage svgImage = GetSvgImageCore(action);
				if (svgImage == null)
					return null;

				SvgBitmap svgBitmap = SvgBitmap.Create(svgImage);
				Image image = svgBitmap.Render(
					new Size(resolvedSize, resolvedSize),
					CreateSvgPalette(disabled),
					DefaultBoolean.False,
					DefaultBoolean.False);
				BitmapCache[cacheKey] = image;
				return image;
			}
		}

		private static IconBinding RegisterBindingCore(object target, NmmIconAction action, Image preferredImage = null)
		{
			for (int i = Bindings.Count - 1; i >= 0; i--)
			{
				object existing = Bindings[i].Target.Target;
				if (existing == null)
				{
					Bindings.RemoveAt(i);
					continue;
				}

				if (Object.ReferenceEquals(existing, target))
				{
					Bindings[i].Action = action;
					Bindings[i].PreferredImage = preferredImage;
					return Bindings[i];
				}
			}

			IconBinding binding = new IconBinding(target, action, preferredImage);
			Bindings.Add(binding);
			return binding;
		}

		private static void EnsureSimpleButtonTextTrackingCore(SimpleButton button, IconBinding binding)
		{
			if (button == null || binding == null || binding.TextTrackingAttached)
				return;

			binding.OriginalText = button.Text ?? String.Empty;
			binding.AutoToolTip = String.IsNullOrWhiteSpace(button.ToolTip) ||
				String.Equals(button.ToolTip, binding.OriginalText, StringComparison.Ordinal);
			button.TextChanged += SimpleButton_TextChanged;
			binding.TextTrackingAttached = true;
		}

		private static void SimpleButton_TextChanged(object sender, EventArgs e)
		{
			SimpleButton button = sender as SimpleButton;
			if (button == null || button.IsDisposed)
				return;

			lock (SyncRoot)
			{
				IconBinding binding = FindBindingCore(button);
				if (binding == null || binding.UpdatingText)
					return;

				string currentText = button.Text ?? String.Empty;
				if (_buttonPresentation == NmmButtonPresentation.IconsOnly)
				{
					if (!String.IsNullOrEmpty(currentText))
					{
						binding.OriginalText = currentText;
						if (binding.AutoToolTip)
							button.ToolTip = currentText;
						SetSimpleButtonTextCore(button, binding, String.Empty);
					}
					return;
				}

				binding.OriginalText = currentText;
				if (binding.AutoToolTip && !String.IsNullOrWhiteSpace(currentText))
					button.ToolTip = currentText;
			}
		}

		private static void SetSimpleButtonTextCore(SimpleButton button, IconBinding binding, string text)
		{
			if (button == null || binding == null || String.Equals(button.Text, text, StringComparison.Ordinal))
				return;

			binding.UpdatingText = true;
			try
			{
				button.Text = text;
			}
			finally
			{
				binding.UpdatingText = false;
			}
		}

		private static void RegisterPresentationOnlyBarItemCore(BarItem item)
		{
			for (int i = PresentationOnlyBarItems.Count - 1; i >= 0; i--)
			{
				object existing = PresentationOnlyBarItems[i].Target;
				if (existing == null)
				{
					PresentationOnlyBarItems.RemoveAt(i);
					continue;
				}

				if (Object.ReferenceEquals(existing, item))
					return;
			}

			PresentationOnlyBarItems.Add(new WeakReference(item));
		}

		private static bool IsPresentationOnlyBarItemCore(BarItem item)
		{
			for (int i = PresentationOnlyBarItems.Count - 1; i >= 0; i--)
			{
				object existing = PresentationOnlyBarItems[i].Target;
				if (existing == null)
				{
					PresentationOnlyBarItems.RemoveAt(i);
					continue;
				}

				if (Object.ReferenceEquals(existing, item))
					return true;
			}

			return false;
		}

		private static void RegisterBarSurfaceCore(Bar bar, bool vertical)
		{
			for (int i = BarSurfaces.Count - 1; i >= 0; i--)
			{
				Bar existing = BarSurfaces[i].Target.Target as Bar;
				if (existing == null)
				{
					BarSurfaces.RemoveAt(i);
					continue;
				}

				if (Object.ReferenceEquals(existing, bar))
				{
					BarSurfaces[i].Vertical = vertical;
					return;
				}
			}

			BarSurfaces.Add(new BarPresentationSurface(bar, vertical));
		}

		private static void RefreshBarSurfacesCore()
		{
			for (int i = BarSurfaces.Count - 1; i >= 0; i--)
			{
				Bar bar = BarSurfaces[i].Target.Target as Bar;
				if (bar == null || bar.Manager == null)
				{
					BarSurfaces.RemoveAt(i);
					continue;
				}

				try
				{
					ApplyBarPresentationCore(bar, BarSurfaces[i].Vertical);
				}
				catch (InvalidOperationException)
				{
					BarSurfaces.RemoveAt(i);
				}
				catch (Exception ex)
				{
					Trace.TraceWarning("Unable to refresh NMM toolbar presentation: {0}", ex.Message);
				}
			}
		}

		private static void ApplyBarPresentationCore(Bar bar, bool vertical)
		{
			if (bar == null)
				return;

			foreach (BarItemLink link in bar.ItemLinks)
			{
				if (link == null || link.Item == null)
					continue;

				IconBinding binding = FindBindingCore(link.Item);
				bool presentationOnly = IsPresentationOnlyBarItemCore(link.Item);
				if (binding == null && !presentationOnly)
					continue;

				link.UserDefine |= BarLinkUserDefines.PaintStyle;
				link.UserPaintStyle = ResolveToolbarPaintStyle(link.Item);

				if (vertical && _buttonPresentation != NmmButtonPresentation.IconsOnly && binding != null)
				{
					link.UserDefine |= BarLinkUserDefines.Caption;
					link.UserCaption = GetCompactCaption(binding.Action, link.Item.Caption);
				}
				else
				{
					link.UserDefine &= ~BarLinkUserDefines.Caption;
				}
			}
		}

		private static IconBinding FindBindingCore(object target)
		{
			for (int i = Bindings.Count - 1; i >= 0; i--)
			{
				object existing = Bindings[i].Target.Target;
				if (existing == null)
				{
					Bindings.RemoveAt(i);
					continue;
				}

				if (Object.ReferenceEquals(existing, target))
					return Bindings[i];
			}

			return null;
		}

		private static BarItemPaintStyle ResolveToolbarPaintStyle(BarItem item)
		{
			switch (_buttonPresentation)
			{
				case NmmButtonPresentation.TextOnly:
					return BarItemPaintStyle.Caption;
				case NmmButtonPresentation.IconsOnly:
					return item is BarSubItem ? BarItemPaintStyle.CaptionInMenu : BarItemPaintStyle.Standard;
				default:
					return BarItemPaintStyle.CaptionGlyph;
			}
		}

		private static string GetCompactCaption(NmmIconAction action, string fallbackCaption)
		{
			switch (action)
			{
				case NmmIconAction.Add:
				case NmmIconAction.AddFile:
				case NmmIconAction.AddUrl:
					return "Add";
					case NmmIconAction.Rename:
						return "Rename";
				case NmmIconAction.InstallEnable:
				case NmmIconAction.InstallRoot:
					return "Enable";
					case NmmIconAction.EnableAll:
						return "Enable All";
				case NmmIconAction.Disable:
					return "Disable";
					case NmmIconAction.DisableAll:
						return "Disable All";
				case NmmIconAction.Delete:
				case NmmIconAction.Remove:
				case NmmIconAction.Purge:
					return "Remove";
				case NmmIconAction.RemoveAll:
					return "Remove All";
				case NmmIconAction.Refresh:
					return "Refresh";
				case NmmIconAction.GetModInfo:
					return "Info";
				case NmmIconAction.CheckUpdates:
					return "Updates";
					case NmmIconAction.UpdateApplication:
						return "Update NMM";
				case NmmIconAction.Endorse:
					return "Endorse";
				case NmmIconAction.Categories:
					return "Categories";
					case NmmIconAction.Sync:
						return "Update";
					case NmmIconAction.UpdateResetCategories:
						return "Reset Nexus";
					case NmmIconAction.Reset:
						return "Reset";
					case NmmIconAction.ResetUnassigned:
						return "Reset Unassigned";
					case NmmIconAction.ResetAll:
						return "Unassign All";
				case NmmIconAction.SwitchView:
					return "View";
				case NmmIconAction.Filter:
					return "Updates Only";
				case NmmIconAction.Export:
					return "Export";
					case NmmIconAction.Import:
						return "Import";
					case NmmIconAction.ImportFromClipboard:
						return "Clipboard";
					case NmmIconAction.ImportFromFile:
						return "File";
				case NmmIconAction.DownloadMode:
					return "Download";
				case NmmIconAction.DisplayOptions:
					return "Display Option";
				case NmmIconAction.Layout:
					return "Layout";
				case NmmIconAction.Settings:
					return "Display";
				default:
					return fallbackCaption ?? String.Empty;
			}
		}

		private static void RefreshBindingsCore()
		{
			for (int i = Bindings.Count - 1; i >= 0; i--)
			{
				object target = Bindings[i].Target.Target;
				if (target == null)
				{
					Bindings.RemoveAt(i);
					continue;
				}

				BaseButton button = target as BaseButton;
				if (button != null && button.IsDisposed)
				{
					Bindings.RemoveAt(i);
					continue;
				}

				try
				{
					ApplyBindingCore(target, Bindings[i].Action, Bindings[i].PreferredImage, Bindings[i]);
				}
				catch (ObjectDisposedException)
				{
					Bindings.RemoveAt(i);
				}
				catch (InvalidOperationException)
				{
					Bindings.RemoveAt(i);
				}
				catch (Exception ex)
				{
					Trace.TraceWarning("Unable to refresh NMM SVG icon binding: {0}", ex.Message);
				}
			}
		}

		private static void ApplyBindingCore(object target, NmmIconAction action, Image preferredImage, IconBinding binding)
		{
			Size imageSize = new Size(_iconSize, _iconSize);

			BarItem barItem = target as BarItem;
			if (barItem != null)
			{
				if (preferredImage != null)
				{
					barItem.ImageOptions.SvgImage = null;
					barItem.ImageOptions.Image = DevExpressDisplaySettingsApplier.ResizeBarItemImage(preferredImage, imageSize);
					barItem.PaintStyle = BarItemPaintStyle.CaptionGlyph;
					return;
				}

				SvgImage barImage = GetSvgImageCore(action);
				if (barImage == null)
					return;

				barItem.ImageOptions.Image = null;
				barItem.ImageOptions.SvgImage = null;
				barItem.ImageOptions.SvgImageColorizationMode = SvgImageColorizationMode.CommonPalette;
				barItem.ImageOptions.SvgImageSize = imageSize;
				barItem.ImageOptions.SvgImage = barImage;
				// Keep the item itself menu-safe. Toolbar-only presentation is applied
				// per BarItemLink by registered toolbar surfaces.
				barItem.PaintStyle = BarItemPaintStyle.CaptionGlyph;
				return;
			}

			SimpleButton button = target as SimpleButton;
			if (button != null)
			{
				if (binding == null)
					binding = FindBindingCore(button);
				if (binding != null)
				{
					if (_buttonPresentation == NmmButtonPresentation.IconsOnly)
					{
						if (!String.IsNullOrEmpty(button.Text))
							binding.OriginalText = button.Text;
						if (binding.AutoToolTip && !String.IsNullOrWhiteSpace(binding.OriginalText))
							button.ToolTip = binding.OriginalText;
						SetSimpleButtonTextCore(button, binding, String.Empty);
					}
					else if (String.IsNullOrEmpty(button.Text) && !String.IsNullOrEmpty(binding.OriginalText))
					{
						SetSimpleButtonTextCore(button, binding, binding.OriginalText);
					}
				}

				button.ImageOptions.Image = null;
				button.ImageOptions.SvgImage = null;

				if (_buttonPresentation == NmmButtonPresentation.TextOnly)
				{
					button.ImageOptions.Location = ImageLocation.Default;
					button.Invalidate();
					return;
				}

				SvgImage buttonImage = GetSvgImageCore(action);
				if (buttonImage == null)
					return;

				button.ImageOptions.SvgImageColorizationMode = SvgImageColorizationMode.CommonPalette;
				button.ImageOptions.SvgImageSize = imageSize;
				button.ImageOptions.SvgImage = buttonImage;
				button.ImageOptions.Location = _buttonPresentation == NmmButtonPresentation.IconsOnly
					? ImageLocation.MiddleCenter
					: ImageLocation.Default;
				button.Invalidate();
			}
		}

		private static void EnsureBarItemToolTip(BarItem item)
		{
			if (item == null)
				return;

			if (String.IsNullOrWhiteSpace(item.Hint) && !String.IsNullOrWhiteSpace(item.Caption))
				item.Hint = item.Caption;
			item.ShowToolTip = true;
		}

		private static void EnsureSimpleButtonToolTip(SimpleButton button)
		{
			if (button == null)
				return;

			if (String.IsNullOrWhiteSpace(button.ToolTip) && !String.IsNullOrWhiteSpace(button.Text))
				button.ToolTip = button.Text;
			button.ShowToolTips = true;
		}

		private static SvgImage GetSvgImageCore(NmmIconAction action)
		{
			string cacheKey = _style + "|" + action;
			SvgImage image;
			if (SvgCache.TryGetValue(cacheKey, out image))
				return image;

			image = LoadSvgImageCore(_style, action);
			if (image == null && _style != NmmIconStyle.Minimal)
				image = LoadSvgImageCore(NmmIconStyle.Minimal, action);

			if (image != null)
				SvgCache[cacheKey] = image;

			return image;
		}

		private static SvgImage LoadSvgImageCore(NmmIconStyle style, NmmIconAction action)
		{
			string resourceKey = style + "." + action + ".svg";
			string fullResourceName;
			if (!ResourceIndex.TryGetValue(resourceKey, out fullResourceName))
			{
				WarnMissingResource(resourceKey);
				return null;
			}

			try
			{
				Assembly assembly = typeof(NmmIconProvider).Assembly;
				using (Stream stream = assembly.GetManifestResourceStream(fullResourceName))
				{
					return stream == null ? null : SvgImage.FromStream(stream);
				}
			}
			catch (Exception ex)
			{
				Trace.TraceWarning("Unable to load NMM SVG icon '{0}': {1}", resourceKey, ex.Message);
				return null;
			}
		}

		private static Dictionary<string, string> BuildResourceIndex()
		{
			Dictionary<string, string> index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			Assembly assembly = typeof(NmmIconProvider).Assembly;
			foreach (string fullName in assembly.GetManifestResourceNames())
			{
				int markerIndex = fullName.IndexOf(ResourceMarker, StringComparison.OrdinalIgnoreCase);
				if (markerIndex < 0)
					continue;

				string key = fullName.Substring(markerIndex + ResourceMarker.Length);
				if (!index.ContainsKey(key))
					index.Add(key, fullName);
			}
			return index;
		}

		private static void WarnMissingResource(string resourceKey)
		{
			if (!MissingResourceWarnings.Add(resourceKey))
				return;

			Trace.TraceWarning("NMM SVG icon resource was not found: {0}", resourceKey);
		}

		private static void ApplyPaletteCore(bool force)
		{
			_darkSurface = DevExpressDisplaySettingsApplier.IsDarkSkinSurface();
			string paletteStamp = String.Format(
				"{0}|{1}|{2}|{3}",
				UserLookAndFeel.Default.SkinName,
				UserLookAndFeel.Default.ActiveSvgPaletteName,
				_colorProfile,
				_darkSurface);

			if (!force && String.Equals(_paletteStamp, paletteStamp, StringComparison.Ordinal))
				return;

			try
			{
				Skin commonSkin = CommonSkins.GetSkin(UserLookAndFeel.Default);
				if (commonSkin == null)
					return;

				NmmIconPalette palette = NmmIconPalette.Resolve(_colorProfile, _darkSurface);
				ApplyPaletteColors(commonSkin.SvgPalettes[ObjectState.Normal], palette.NormalColors);
				ApplyPaletteColors(commonSkin.SvgPalettes[ObjectState.Disabled], palette.DisabledColors);
				_paletteStamp = paletteStamp;
			}
			catch (Exception ex)
			{
				Trace.TraceWarning("Unable to apply NMM SVG icon palette: {0}", ex.Message);
			}
		}

		private static void ApplyPaletteColors(SvgPalette svgPalette, IDictionary<string, Color> colors)
		{
			if (svgPalette == null || colors == null)
				return;

			foreach (KeyValuePair<string, Color> pair in colors)
			{
				SvgColor existing = svgPalette.Colors.FirstOrDefault(
					color => String.Equals(color.Name, pair.Key, StringComparison.OrdinalIgnoreCase));
				if (existing != null)
					svgPalette.Colors.Remove(existing);
				svgPalette.Colors.Add(new SvgColor(pair.Key, pair.Value));
			}
		}

		private static SvgPalette CreateSvgPalette(bool disabled)
		{
			NmmIconPalette palette = NmmIconPalette.Resolve(_colorProfile, _darkSurface);
			IDictionary<string, Color> source = disabled ? palette.DisabledColors : palette.NormalColors;
			SvgPalette svgPalette = new SvgPalette();
			foreach (KeyValuePair<string, Color> pair in source)
				svgPalette.Colors.Add(new SvgColor(pair.Key, pair.Value));
			return svgPalette;
		}

		private static int NormalizeIconSize(int iconSize)
		{
			switch (iconSize)
			{
				case 16:
				case 20:
				case 24:
				case 32:
					return iconSize;
				default:
					return DefaultIconSize;
			}
		}

		private static int NormalizeBitmapSize(int size)
		{
			return size > 0 && size <= 256 ? size : DefaultIconSize;
		}

		private static void ClearBitmapCacheCore()
		{
			foreach (Image image in BitmapCache.Values)
				image.Dispose();
			BitmapCache.Clear();
		}

		private sealed class IconBinding
		{
			internal IconBinding(object target, NmmIconAction action, Image preferredImage)
			{
				Target = new WeakReference(target);
				Action = action;
				PreferredImage = preferredImage;
			}

			internal WeakReference Target { get; private set; }
			internal NmmIconAction Action { get; set; }
			internal Image PreferredImage { get; set; }
			internal string OriginalText { get; set; }
			internal bool AutoToolTip { get; set; }
			internal bool TextTrackingAttached { get; set; }
			internal bool UpdatingText { get; set; }
		}

		private sealed class BarPresentationSurface
		{
			internal BarPresentationSurface(Bar bar, bool vertical)
			{
				Target = new WeakReference(bar);
				Vertical = vertical;
			}

			internal WeakReference Target { get; private set; }
			internal bool Vertical { get; set; }
		}
	}

	/// <summary>
	/// Semantic icon colors for one accessibility profile and one surface brightness.
	/// SVG files reference these names and never own the final user-facing colors.
	/// </summary>
	internal sealed class NmmIconPalette
	{
		private NmmIconPalette(IDictionary<string, Color> normalColors, IDictionary<string, Color> disabledColors)
		{
			NormalColors = normalColors;
			DisabledColors = disabledColors;
		}

		internal IDictionary<string, Color> NormalColors { get; private set; }
		internal IDictionary<string, Color> DisabledColors { get; private set; }

		internal static NmmIconPalette Resolve(NmmIconColorProfile profile, bool darkSurface)
		{
			// Base is deliberately the only 0.91 profile. New accessibility profiles
			// only need to provide another semantic palette here; assets and bindings stay unchanged.
			Dictionary<string, Color> normal = CreateBaseColors(darkSurface);
			Color background = DevExpressDisplaySettingsApplier.GetSkinColor("Control", SystemColors.Control);
			Color disabledText = DevExpressDisplaySettingsApplier.GetSkinColor("DisabledText", SystemColors.GrayText);
			Dictionary<string, Color> disabled = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

			foreach (KeyValuePair<string, Color> pair in normal)
			{
				if (String.Equals(pair.Key, "NmmOnSemantic", StringComparison.OrdinalIgnoreCase) ||
					String.Equals(pair.Key, "NmmNeutral", StringComparison.OrdinalIgnoreCase))
				{
					disabled[pair.Key] = disabledText;
				}
				else
				{
					disabled[pair.Key] = Blend(background, pair.Value, 0.45d);
				}
			}

			return new NmmIconPalette(normal, disabled);
		}

		private static Dictionary<string, Color> CreateBaseColors(bool darkSurface)
		{
			Color neutral = DevExpressDisplaySettingsApplier.GetSkinColor("ControlText", SystemColors.ControlText);
			Dictionary<string, Color> colors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
			if (darkSurface)
			{
				colors["NmmPositive"] = Color.FromArgb(104, 198, 109);
				colors["NmmNegative"] = Color.FromArgb(255, 104, 104);
				colors["NmmWarning"] = Color.FromArgb(255, 170, 74);
				colors["NmmInformation"] = Color.FromArgb(105, 169, 255);
				colors["NmmAccent"] = Color.FromArgb(180, 135, 230);
			}
			else
			{
				colors["NmmPositive"] = Color.FromArgb(54, 155, 70);
				colors["NmmNegative"] = Color.FromArgb(216, 68, 68);
				colors["NmmWarning"] = Color.FromArgb(224, 122, 24);
				colors["NmmInformation"] = Color.FromArgb(47, 126, 219);
				colors["NmmAccent"] = Color.FromArgb(138, 84, 198);
			}

			colors["NmmNeutral"] = neutral;
			colors["NmmOnSemantic"] = Color.FromArgb(250, 250, 250);
			colors["NmmHighlight"] = Color.White;
			colors["NmmShadow"] = Color.Black;
			return colors;
		}

		private static Color Blend(Color background, Color foreground, double foregroundWeight)
		{
			return Color.FromArgb(
				255,
				BlendComponent(background.R, foreground.R, foregroundWeight),
				BlendComponent(background.G, foreground.G, foregroundWeight),
				BlendComponent(background.B, foreground.B, foregroundWeight));
		}

		private static int BlendComponent(byte background, byte foreground, double foregroundWeight)
		{
			double value = background * (1d - foregroundWeight) + foreground * foregroundWeight;
			return Math.Max(0, Math.Min(255, (int)Math.Round(value)));
		}
	}
}
