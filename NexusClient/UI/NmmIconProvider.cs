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
		ModActive,
		ModInstalled,
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
		Base,
		Deuteranopia,
		Protanopia,
		Tritanopia,
		HighContrast
	}

	internal enum NmmButtonPresentation
	{
		TextOnly,
		IconsOnly,
		TextAndIcons
	}

	internal enum NmmButtonPresentationScope
	{
		MainBar,
		Plugins,
		Mods,
		Categories,
		FileManager,
		DownloadManager,
		ModActivationQueue
	}

	/// <summary>
	/// Button-presentation settings for the explicitly configurable toolbar surfaces.
	/// A non-custom profile is a global override; a custom profile uses the per-surface values.
	/// </summary>
	internal sealed class NmmButtonPresentationProfile
	{
		private readonly Dictionary<NmmButtonPresentationScope, NmmButtonPresentation> _values;

		private NmmButtonPresentationProfile(bool custom, NmmButtonPresentation globalPresentation, IDictionary<NmmButtonPresentationScope, NmmButtonPresentation> values)
		{
			IsCustom = custom;
			GlobalPresentation = globalPresentation;
			_values = new Dictionary<NmmButtonPresentationScope, NmmButtonPresentation>(values);
		}

		internal bool IsCustom { get; private set; }
		internal NmmButtonPresentation GlobalPresentation { get; private set; }

		internal static NmmButtonPresentationProfile CreateGlobal(NmmButtonPresentation presentation)
		{
			Dictionary<NmmButtonPresentationScope, NmmButtonPresentation> values = new Dictionary<NmmButtonPresentationScope, NmmButtonPresentation>();
			foreach (NmmButtonPresentationScope scope in Enum.GetValues(typeof(NmmButtonPresentationScope)))
				values[scope] = presentation;

			return new NmmButtonPresentationProfile(false, presentation, values);
		}

		internal static NmmButtonPresentationProfile CreateDefault()
		{
			Dictionary<NmmButtonPresentationScope, NmmButtonPresentation> values = new Dictionary<NmmButtonPresentationScope, NmmButtonPresentation>();
			foreach (NmmButtonPresentationScope scope in Enum.GetValues(typeof(NmmButtonPresentationScope)))
				values[scope] = NmmButtonPresentation.TextAndIcons;

			values[NmmButtonPresentationScope.DownloadManager] = NmmButtonPresentation.IconsOnly;
			values[NmmButtonPresentationScope.ModActivationQueue] = NmmButtonPresentation.IconsOnly;
			return new NmmButtonPresentationProfile(true, NmmButtonPresentation.TextAndIcons, values);
		}

		internal static NmmButtonPresentationProfile CreateCustom(IDictionary<NmmButtonPresentationScope, NmmButtonPresentation> values)
		{
			Dictionary<NmmButtonPresentationScope, NmmButtonPresentation> resolved = new Dictionary<NmmButtonPresentationScope, NmmButtonPresentation>();
			foreach (NmmButtonPresentationScope scope in Enum.GetValues(typeof(NmmButtonPresentationScope)))
			{
				NmmButtonPresentation presentation;
				resolved[scope] = values != null && values.TryGetValue(scope, out presentation)
					? presentation
					: NmmButtonPresentation.TextAndIcons;
			}

			return new NmmButtonPresentationProfile(true, NmmButtonPresentation.TextAndIcons, resolved);
		}

		internal NmmButtonPresentation Get(NmmButtonPresentationScope scope)
		{
			if (!IsCustom)
				return GlobalPresentation;

			NmmButtonPresentation presentation;
			return _values.TryGetValue(scope, out presentation) ? presentation : NmmButtonPresentation.TextAndIcons;
		}

		internal NmmButtonPresentationProfile WithGlobal(NmmButtonPresentation presentation)
		{
			return CreateGlobal(presentation);
		}

		internal NmmButtonPresentationProfile WithScope(NmmButtonPresentationScope scope, NmmButtonPresentation presentation)
		{
			Dictionary<NmmButtonPresentationScope, NmmButtonPresentation> values = new Dictionary<NmmButtonPresentationScope, NmmButtonPresentation>();
			foreach (NmmButtonPresentationScope currentScope in Enum.GetValues(typeof(NmmButtonPresentationScope)))
				values[currentScope] = Get(currentScope);

			values[scope] = presentation;
			return CreateCustom(values);
		}

		public override bool Equals(object obj)
		{
			NmmButtonPresentationProfile other = obj as NmmButtonPresentationProfile;
			if (other == null || IsCustom != other.IsCustom || GlobalPresentation != other.GlobalPresentation)
				return false;

			foreach (NmmButtonPresentationScope scope in Enum.GetValues(typeof(NmmButtonPresentationScope)))
			{
				if (Get(scope) != other.Get(scope))
					return false;
			}

			return true;
		}

		public override int GetHashCode()
		{
			unchecked
			{
				int hash = IsCustom ? 17 : 31;
				hash = hash * 397 ^ GlobalPresentation.GetHashCode();
				foreach (NmmButtonPresentationScope scope in Enum.GetValues(typeof(NmmButtonPresentationScope)))
					hash = hash * 397 ^ Get(scope).GetHashCode();
				return hash;
			}
		}
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
		private static NmmButtonPresentationProfile _buttonPresentationProfile = NmmButtonPresentationProfile.CreateDefault();
		private static bool _darkSurface;
		private static string _paletteStamp;

		internal static NmmIconStyle CurrentStyle { get { return _style; } }
		internal static int CurrentIconSize { get { return _iconSize; } }
		internal static NmmIconColorProfile CurrentColorProfile { get { return _colorProfile; } }
		internal static NmmButtonPresentation GetButtonPresentation(NmmButtonPresentationScope scope) { return _buttonPresentationProfile.Get(scope); }

		/// <summary>
		/// Applies the current global icon choices and refreshes every registered UI action.
		/// </summary>
		internal static void ApplySettings(NmmIconStyle style, int iconSize, NmmIconColorProfile colorProfile, NmmButtonPresentationProfile buttonPresentationProfile)
		{
			lock (SyncRoot)
			{
				int resolvedIconSize = NormalizeIconSize(iconSize);
				NmmButtonPresentationProfile resolvedPresentationProfile = buttonPresentationProfile ?? NmmButtonPresentationProfile.CreateDefault();
				bool settingsChanged = _style != style ||
					_iconSize != resolvedIconSize ||
					_colorProfile != colorProfile ||
					!_buttonPresentationProfile.Equals(resolvedPresentationProfile);
				_style = style;
				_iconSize = resolvedIconSize;
				_colorProfile = colorProfile;
				_buttonPresentationProfile = resolvedPresentationProfile;

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
			Bind(button, action, null);
		}

		internal static void Bind(SimpleButton button, NmmIconAction action, NmmButtonPresentationScope? presentationScope)
		{
			if (button == null || button.IsDisposed)
				return;

			lock (SyncRoot)
			{
				ApplyPaletteCore(false);
				IconBinding binding = RegisterBindingCore(button, action, null, presentationScope);
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
				RegisterBarSurfaceCore(bar, null, vertical);
				ApplyBarPresentationCore(bar, vertical, NmmButtonPresentation.TextAndIcons);
			}
		}

		/// <summary>
		/// Registers one of the explicitly configurable toolbar surfaces.
		/// </summary>
		internal static void BindBar(Bar bar, NmmButtonPresentationScope presentationScope, bool vertical)
		{
			if (bar == null)
				return;

			lock (SyncRoot)
			{
				RegisterBarSurfaceCore(bar, presentationScope, vertical);
				ApplyBarPresentationCore(bar, vertical, _buttonPresentationProfile.Get(presentationScope));
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

		private static IconBinding RegisterBindingCore(object target, NmmIconAction action, Image preferredImage = null, NmmButtonPresentationScope? presentationScope = null)
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
					Bindings[i].PresentationScope = presentationScope;
					return Bindings[i];
				}
			}

			IconBinding binding = new IconBinding(target, action, preferredImage, presentationScope);
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
				NmmButtonPresentation presentation = GetButtonPresentationCore(binding.PresentationScope);
				if (presentation == NmmButtonPresentation.IconsOnly)
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

		private static void RegisterBarSurfaceCore(Bar bar, NmmButtonPresentationScope? presentationScope, bool vertical)
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
					BarSurfaces[i].PresentationScope = presentationScope;
					return;
				}
			}

			BarSurfaces.Add(new BarPresentationSurface(bar, presentationScope, vertical));
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
					NmmButtonPresentation presentation = GetButtonPresentationCore(BarSurfaces[i].PresentationScope);
					ApplyBarPresentationCore(bar, BarSurfaces[i].Vertical, presentation);
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

		private static void ApplyBarPresentationCore(Bar bar, bool vertical, NmmButtonPresentation presentation)
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
				link.UserPaintStyle = ResolveToolbarPaintStyle(link.Item, presentation);

				if (vertical && presentation != NmmButtonPresentation.IconsOnly && binding != null)
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

		private static BarItemPaintStyle ResolveToolbarPaintStyle(BarItem item, NmmButtonPresentation presentation)
		{
			switch (presentation)
			{
				case NmmButtonPresentation.TextOnly:
					return BarItemPaintStyle.Caption;
				case NmmButtonPresentation.IconsOnly:
					return item is BarSubItem ? BarItemPaintStyle.CaptionInMenu : BarItemPaintStyle.Standard;
				default:
					return BarItemPaintStyle.CaptionGlyph;
			}
		}

		private static NmmButtonPresentation GetButtonPresentationCore(NmmButtonPresentationScope? presentationScope)
		{
			return presentationScope.HasValue
				? _buttonPresentationProfile.Get(presentationScope.Value)
				: NmmButtonPresentation.TextAndIcons;
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
				NmmButtonPresentation presentation = GetButtonPresentationCore(binding == null ? (NmmButtonPresentationScope?)null : binding.PresentationScope);
				if (binding != null)
				{
					if (presentation == NmmButtonPresentation.IconsOnly)
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

				if (presentation == NmmButtonPresentation.TextOnly)
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
				button.ImageOptions.Location = presentation == NmmButtonPresentation.IconsOnly
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
			internal IconBinding(object target, NmmIconAction action, Image preferredImage, NmmButtonPresentationScope? presentationScope)
			{
				Target = new WeakReference(target);
				Action = action;
				PreferredImage = preferredImage;
				PresentationScope = presentationScope;
			}

			internal WeakReference Target { get; private set; }
			internal NmmIconAction Action { get; set; }
			internal Image PreferredImage { get; set; }
			internal NmmButtonPresentationScope? PresentationScope { get; set; }
			internal string OriginalText { get; set; }
			internal bool AutoToolTip { get; set; }
			internal bool TextTrackingAttached { get; set; }
			internal bool UpdatingText { get; set; }
		}

		private sealed class BarPresentationSurface
		{
			internal BarPresentationSurface(Bar bar, NmmButtonPresentationScope? presentationScope, bool vertical)
			{
				Target = new WeakReference(bar);
				PresentationScope = presentationScope;
				Vertical = vertical;
			}

			internal WeakReference Target { get; private set; }
			internal NmmButtonPresentationScope? PresentationScope { get; set; }
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
			NmmColorPalette semantic = NmmColorPalette.Resolve(profile, darkSurface);
			Dictionary<string, Color> normal = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
			{
				["NmmPositive"] = semantic.Positive,
				["NmmNegative"] = semantic.Negative,
				["NmmWarning"] = semantic.Warning,
				["NmmInformation"] = semantic.Information,
				["NmmAccent"] = semantic.Accent,
				["NmmNeutral"] = semantic.Neutral,
				["NmmOnSemantic"] = semantic.OnSemantic,
				["NmmHighlight"] = Color.White,
				["NmmShadow"] = Color.Black
			};
			Color background = DevExpressDisplaySettingsApplier.GetSkinColor("Control", SystemColors.Control);
			Color disabledText = DevExpressDisplaySettingsApplier.GetSkinColor("DisabledText", SystemColors.GrayText);
			Dictionary<string, Color> disabled = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

			foreach (KeyValuePair<string, Color> pair in normal)
			{
				if (String.Equals(pair.Key, "NmmOnSemantic", StringComparison.OrdinalIgnoreCase) ||
					String.Equals(pair.Key, "NmmNeutral", StringComparison.OrdinalIgnoreCase))
					disabled[pair.Key] = disabledText;
				else
					disabled[pair.Key] = Blend(background, pair.Value, 0.45d);
			}

			return new NmmIconPalette(normal, disabled);
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

	/// <summary>
	/// Shared semantic colors for icons and accessibility-aware grid states.
	/// </summary>
	internal sealed class NmmColorPalette
	{
		private NmmColorPalette() { }

		internal Color Positive { get; private set; }
		internal Color Negative { get; private set; }
		internal Color Warning { get; private set; }
		internal Color Information { get; private set; }
		internal Color Accent { get; private set; }
		internal Color Neutral { get; private set; }
		internal Color OnSemantic { get; private set; }
		internal Color ModActiveRowBackColor { get; private set; }
		internal Color ModActiveSelectedRowBackColor { get; private set; }
		internal Color ModInstalledRowBackColor { get; private set; }
		internal Color ModInstalledSelectedRowBackColor { get; private set; }
		internal Color ModRowForeColor { get; private set; }
		internal Color ModNewRowBackColor { get; private set; }
		internal Color ModNewRowForeColor { get; private set; }
		internal Color ModNewGroupBackColor { get; private set; }
		internal Color ModNewGroupForeColor { get; private set; }
		internal Color ModSortHeaderBackColor { get; private set; }
		internal Color ModSortHeaderForeColor { get; private set; }
		internal Color ModFilterMatchColor { get; private set; }
		internal Color ModLatestVersionForeColor { get; private set; }
		internal Color ModOutdatedVersionForeColor { get; private set; }
		internal Color[] CategoryColors { get; private set; }
		internal Color CategoryNeutralColor { get; private set; }
		internal Color FileSourceBaseGameColor { get; private set; }
		internal Color FileSourceInstalledColor { get; private set; }
		internal Color FileSourceCreationsColor { get; private set; }
		internal Color FileSourceExternalColor { get; private set; }
		internal Color FileSourceUntrackedColor { get; private set; }
		internal Color PluginErrorColor { get; private set; }
		internal Color PluginWarningColor { get; private set; }

		internal static NmmColorPalette Resolve(NmmIconColorProfile profile, bool darkSurface)
		{
			Color positive, negative, warning, information, accent;
			Color[] categories;
			ResolveProfileColors(profile, darkSurface, out positive, out negative, out warning, out information, out accent, out categories);
			Color background = DevExpressDisplaySettingsApplier.GetSkinColor("Control", SystemColors.Control);
			Color text = DevExpressDisplaySettingsApplier.GetSkinColor("ControlText", SystemColors.ControlText);
			double subtle = darkSurface ? 0.20d : 0.10d;
			double selected = darkSurface ? 0.34d : 0.20d;
			double group = darkSurface ? 0.34d : 0.22d;
			Color groupBack = Blend(background, warning, group);
			Color[] sources = profile == NmmIconColorProfile.Base
				? (darkSurface ? Colors(Color.LightSkyBlue, Color.LightGreen, Color.Violet, Color.Turquoise, Color.LightSalmon) : Colors(Color.RoyalBlue, Color.ForestGreen, Color.DarkViolet, Color.DarkCyan, Color.OrangeRed))
				: Colors(categories[0], categories[3], categories[2], categories[5], categories[1]);

			NmmColorPalette palette = new NmmColorPalette
			{
				Positive = positive, Negative = negative, Warning = warning, Information = information, Accent = accent,
				Neutral = text, OnSemantic = Color.FromArgb(250, 250, 250),
				ModActiveRowBackColor = Blend(background, positive, subtle),
				ModActiveSelectedRowBackColor = Blend(background, positive, selected),
				ModInstalledRowBackColor = Blend(background, warning, subtle),
				ModInstalledSelectedRowBackColor = Blend(background, warning, selected),
				ModRowForeColor = text,
				ModNewRowBackColor = Blend(background, warning, darkSurface ? 0.24d : 0.13d),
				ModNewRowForeColor = warning,
				ModNewGroupBackColor = groupBack,
				ModNewGroupForeColor = GetContrastingTextColor(groupBack),
				ModSortHeaderBackColor = Blend(background, information, darkSurface ? 0.28d : 0.15d),
				ModSortHeaderForeColor = information,
				ModFilterMatchColor = Color.FromArgb(darkSurface ? 105 : 120, warning.R, warning.G, warning.B),
				ModLatestVersionForeColor = information,
				ModOutdatedVersionForeColor = negative,
				CategoryColors = categories,
				CategoryNeutralColor = darkSurface ? Color.FromArgb(156, 163, 175) : Color.FromArgb(107, 114, 128),
				FileSourceBaseGameColor = sources[0], FileSourceInstalledColor = sources[1], FileSourceCreationsColor = sources[2], FileSourceExternalColor = sources[3], FileSourceUntrackedColor = sources[4],
				PluginErrorColor = negative,
				PluginWarningColor = warning
			};

			if (profile == NmmIconColorProfile.Base)
			{
				palette.ModLatestVersionForeColor = darkSurface ? Color.LightSkyBlue : C(37, 99, 235);
				palette.ModOutdatedVersionForeColor = darkSurface ? Color.LightCoral : C(200, 40, 40);
				palette.PluginErrorColor = darkSurface ? Color.LightCoral : Color.DarkRed;
				palette.PluginWarningColor = darkSurface ? Color.Orange : Color.DarkOrange;
				palette.ModFilterMatchColor = Color.FromArgb(120, 255, 230, 120);
				if (darkSurface)
				{
					palette.ModNewRowBackColor = C(78, 65, 24);
					palette.ModNewRowForeColor = C(255, 235, 153);
					palette.ModNewGroupBackColor = C(98, 74, 8);
					palette.ModNewGroupForeColor = C(255, 239, 170);
				}
				else
				{
					palette.ModActiveRowBackColor = C(249, 254, 249);
					palette.ModActiveSelectedRowBackColor = C(218, 240, 218);
					palette.ModInstalledRowBackColor = C(255, 251, 244);
					palette.ModInstalledSelectedRowBackColor = C(250, 230, 200);
					palette.ModRowForeColor = Color.Black;
					palette.ModNewRowBackColor = C(255, 248, 214);
					palette.ModNewRowForeColor = C(82, 62, 0);
					palette.ModNewGroupBackColor = C(255, 226, 128);
					palette.ModNewGroupForeColor = C(68, 49, 0);
					palette.ModSortHeaderBackColor = C(219, 234, 254);
					palette.ModSortHeaderForeColor = C(37, 99, 235);
				}
			}

			return palette;
		}

		internal static Color GetContrastingTextColor(Color background)
		{
			double luminance = (0.2126d * background.R + 0.7152d * background.G + 0.0722d * background.B) / 255d;
			return luminance > 0.58d ? Color.Black : Color.White;
		}

		private static void ResolveProfileColors(NmmIconColorProfile profile, bool dark, out Color positive, out Color negative, out Color warning, out Color information, out Color accent, out Color[] categories)
		{
			switch (profile)
			{
				case NmmIconColorProfile.Deuteranopia:
					positive = dark ? C(86,180,233) : C(0,114,178); negative = dark ? C(255,142,91) : C(213,94,0); warning = dark ? C(255,203,84) : C(180,118,0); information = dark ? C(87,211,176) : C(0,126,98); accent = dark ? C(230,160,207) : C(170,78,132);
					categories = dark ? Colors(C(86,180,233),C(255,142,91),C(230,160,207),C(87,211,176),C(255,203,84),C(164,133,255),C(109,220,221),C(255,170,120),C(120,170,255),C(214,214,214),C(196,150,225),C(120,205,160)) : Colors(C(0,114,178),C(213,94,0),C(170,78,132),C(0,126,98),C(180,118,0),C(94,72,168),C(0,125,135),C(178,80,20),C(45,95,170),C(90,90,90),C(123,71,146),C(36,125,82)); break;
				case NmmIconColorProfile.Protanopia:
					positive = dark ? C(87,211,176) : C(0,126,98); negative = dark ? C(230,160,207) : C(170,78,132); warning = dark ? C(255,203,84) : C(180,118,0); information = dark ? C(86,180,233) : C(0,114,178); accent = dark ? C(176,156,255) : C(94,72,168);
					categories = dark ? Colors(C(87,211,176),C(86,180,233),C(230,160,207),C(255,203,84),C(176,156,255),C(109,220,221),C(255,166,115),C(196,196,196),C(117,157,255),C(123,219,156),C(223,141,218),C(255,211,122)) : Colors(C(0,126,98),C(0,114,178),C(170,78,132),C(180,118,0),C(94,72,168),C(0,125,135),C(178,80,20),C(85,85,85),C(50,78,160),C(24,116,73),C(144,65,137),C(150,103,0)); break;
				case NmmIconColorProfile.Tritanopia:
					positive = dark ? C(105,214,143) : C(20,128,67); negative = dark ? C(255,112,139) : C(190,24,60); warning = dark ? C(255,156,92) : C(184,78,16); information = dark ? C(202,145,255) : C(111,52,165); accent = dark ? C(255,126,214) : C(174,35,128);
					categories = dark ? Colors(C(105,214,143),C(255,112,139),C(202,145,255),C(255,156,92),C(255,126,214),C(125,225,181),C(244,183,111),C(223,125,170),C(158,220,128),C(213,213,213),C(177,133,231),C(255,138,119)) : Colors(C(20,128,67),C(190,24,60),C(111,52,165),C(184,78,16),C(174,35,128),C(15,116,89),C(154,91,12),C(163,41,96),C(69,120,30),C(82,82,82),C(96,58,150),C(174,55,35)); break;
				case NmmIconColorProfile.HighContrast:
					positive = dark ? C(124,252,0) : C(0,100,0); negative = dark ? C(255,82,82) : C(176,0,32); warning = dark ? C(255,215,64) : C(122,78,0); information = dark ? C(64,196,255) : C(0,71,171); accent = dark ? C(234,128,252) : C(91,33,182);
					categories = dark ? Colors(C(64,196,255),C(255,82,82),C(234,128,252),C(124,252,0),C(255,215,64),C(0,255,213),C(255,145,0),C(194,158,255),C(255,93,174),C(230,230,230),C(145,255,109),C(255,187,102)) : Colors(C(0,71,171),C(176,0,32),C(91,33,182),C(0,100,0),C(122,78,0),C(0,96,96),C(170,70,0),C(73,45,156),C(154,0,86),C(55,55,55),C(39,112,0),C(150,70,0)); break;
				default:
					if (dark) { positive=C(104,198,109); negative=C(255,104,104); warning=C(255,170,74); information=C(105,169,255); accent=C(180,135,230); categories=Colors(C(180,135,230),C(105,169,255),C(230,116,180),C(79,196,183),C(255,181,72),C(104,198,109),C(255,139,72),C(142,145,241),C(255,104,104),C(85,190,235),C(199,135,247),C(78,184,169)); }
					else { positive=C(54,155,70); negative=C(216,68,68); warning=C(224,122,24); information=C(47,126,219); accent=C(138,84,198); categories=Colors(C(139,92,246),C(59,130,246),C(236,72,153),C(20,184,166),C(245,158,11),C(34,197,94),C(249,115,22),C(99,102,241),C(220,38,38),C(14,165,233),C(168,85,247),C(13,148,136)); } break;
			}
		}

		private static Color[] Colors(params Color[] colors) { return colors; }
		private static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }
		private static Color Blend(Color background, Color foreground, double weight)
		{
			return Color.FromArgb(255, BlendComponent(background.R, foreground.R, weight), BlendComponent(background.G, foreground.G, weight), BlendComponent(background.B, foreground.B, weight));
		}
		private static int BlendComponent(byte background, byte foreground, double weight)
		{
			double value = background * (1d - weight) + foreground * weight;
			return Math.Max(0, Math.Min(255, (int)Math.Round(value)));
		}
	}

}
