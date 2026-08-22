using System;
using System.Collections.Generic;
using System.Diagnostics;

using DevExpress.XtraDialogs;
using DevExpress.Dialogs.Core.Localization;
using DevExpress.Utils.Filtering.Internal;
using DevExpress.XtraBars.Localization;
using DevExpress.XtraGrid.Localization;
using DevExpress.XtraLayout.Localization;

using Nexus.Client.Util.Localization;

using EditorsLocalizer = DevExpress.XtraEditors.Controls.Localizer;
using EditorsStringId = DevExpress.XtraEditors.Controls.StringId;

namespace Nexus.Client.UI
{
	/// <summary>
	/// Bridges NMM language packs to the DevExpress runtime-localization API.
	/// Localized values are resolved once during startup and cached by enum value;
	/// no LanguageManager lookup is performed by DevExpress while the UI is running.
	/// </summary>
	internal static class DevExpressLocalization
	{
		private const string GridPrefix = "DevExpress.Grid.";
		private const string EditorsPrefix = "DevExpress.Editors.";
		private const string BarsPrefix = "DevExpress.Bars.";
		private const string LayoutPrefix = "DevExpress.Layout.";
		private const string DialogsPrefix = "DevExpress.Dialogs.";
		private const string FilteringPrefix = "DevExpress.Filtering.";

		private static bool _initialized;

		/// <summary>
		/// Installs only the DevExpress localizers for which the selected NMM language
		/// pack actually contains translations. Built-in English keeps the stock
		/// DevExpress localizers and therefore has zero additional runtime overhead.
		/// </summary>
		public static void Initialize()
		{
			if (_initialized)
				return;

			_initialized = true;

			if (!LanguageManager.IsInitialized || LanguageManager.CurrentLanguage.IsBuiltIn)
				return;

			bool grid = LanguageManager.HasTranslationsWithPrefix(GridPrefix);
			bool editors = LanguageManager.HasTranslationsWithPrefix(EditorsPrefix);
			bool bars = LanguageManager.HasTranslationsWithPrefix(BarsPrefix);
			bool layout = LanguageManager.HasTranslationsWithPrefix(LayoutPrefix);
			bool dialogs = LanguageManager.HasTranslationsWithPrefix(DialogsPrefix);
			bool filtering = LanguageManager.HasTranslationsWithPrefix(FilteringPrefix);

			if (grid)
				GridLocalizer.Active = new NmmGridLocalizer();

			if (editors)
				EditorsLocalizer.Active = new NmmEditorsLocalizer();

			if (bars)
				BarLocalizer.Active = new NmmBarLocalizer();

			if (layout)
				LayoutLocalizer.Active = new NmmLayoutLocalizer();

			if (dialogs)
				DialogsLocalizer.Active = new NmmDialogsLocalizer();

			if (filtering)
				FilterUIElementResXLocalizer.Active = new NmmFilteringLocalizer();

			Trace.TraceInformation(
				"DevExpress UI localization initialized: grid={0}, editors={1}, bars={2}, layout={3}, dialogs={4}, filtering={5}",
				grid,
				editors,
				bars,
				layout,
				dialogs,
				filtering);
		}

		private static Dictionary<T, string> BuildCache<T>(string prefix, Func<T, string> getEnglish)
			where T : struct
		{
			Array values = Enum.GetValues(typeof(T));
			Dictionary<T, string> cache = new Dictionary<T, string>(values.Length);

			foreach (T id in values)
			{
				string english = getEnglish(id) ?? String.Empty;
				string key = prefix + id.ToString();
				string localized = ContainsCompositeFormatItem(english)
					? LanguageManager.GetFormat(key, english)
					: LanguageManager.Get(key, english);

				cache[id] = localized;
			}

			return cache;
		}

		private static bool ContainsCompositeFormatItem(string value)
		{
			if (String.IsNullOrEmpty(value))
				return false;

			for (int i = 0; i < value.Length - 1; i++)
			{
				if (value[i] != '{')
					continue;

				if (i > 0 && value[i - 1] == '{')
					continue;

				if (Char.IsDigit(value[i + 1]))
					return true;
			}

			return false;
		}

		private static string ActiveLanguage
		{
			get
			{
				LanguagePackInfo language = LanguageManager.CurrentLanguage;
				if (!String.IsNullOrWhiteSpace(language.Culture))
					return language.Culture;

				return language.Id;
			}
		}

		private sealed class NmmGridLocalizer : GridLocalizer
		{
			private readonly Dictionary<GridStringId, string> _cache;

			public NmmGridLocalizer()
			{
				_cache = BuildCache<GridStringId>(GridPrefix, GetEnglish);
			}

			public override string Language
			{
				get { return ActiveLanguage; }
			}

			public override string GetLocalizedString(GridStringId id)
			{
				string value;
				return _cache.TryGetValue(id, out value) ? value : base.GetLocalizedString(id);
			}

			private string GetEnglish(GridStringId id)
			{
				return base.GetLocalizedString(id);
			}
		}

		private sealed class NmmEditorsLocalizer : EditorsLocalizer
		{
			private readonly Dictionary<EditorsStringId, string> _cache;

			public NmmEditorsLocalizer()
			{
				_cache = BuildCache<EditorsStringId>(EditorsPrefix, GetEnglish);
			}

			public override string Language
			{
				get { return ActiveLanguage; }
			}

			public override string GetLocalizedString(EditorsStringId id)
			{
				string value;
				return _cache.TryGetValue(id, out value) ? value : base.GetLocalizedString(id);
			}

			private string GetEnglish(EditorsStringId id)
			{
				return base.GetLocalizedString(id);
			}
		}

		private sealed class NmmBarLocalizer : BarLocalizer
		{
			private readonly Dictionary<BarString, string> _cache;

			public NmmBarLocalizer()
			{
				_cache = BuildCache<BarString>(BarsPrefix, GetEnglish);
			}

			public override string Language
			{
				get { return ActiveLanguage; }
			}

			public override string GetLocalizedString(BarString id)
			{
				string value;
				return _cache.TryGetValue(id, out value) ? value : base.GetLocalizedString(id);
			}

			private string GetEnglish(BarString id)
			{
				return base.GetLocalizedString(id);
			}
		}


		private sealed class NmmDialogsLocalizer : DialogsLocalizer
		{
			private readonly Dictionary<DialogsStringId, string> _cache;

			public NmmDialogsLocalizer()
			{
				_cache = BuildCache<DialogsStringId>(DialogsPrefix, GetEnglish);
			}

			public override string Language
			{
				get { return ActiveLanguage; }
			}

			public override string GetLocalizedString(DialogsStringId id)
			{
				string value;
				return _cache.TryGetValue(id, out value) ? value : base.GetLocalizedString(id);
			}

			private string GetEnglish(DialogsStringId id)
			{
				return base.GetLocalizedString(id);
			}
		}

		private sealed class NmmFilteringLocalizer : FilterUIElementResXLocalizer
		{
			private readonly Dictionary<FilterUIElementLocalizerStringId, string> _cache;

			public NmmFilteringLocalizer()
			{
				_cache = BuildCache<FilterUIElementLocalizerStringId>(FilteringPrefix, GetEnglish);
			}

			public override string Language
			{
				get { return ActiveLanguage; }
			}

			public override string GetLocalizedString(FilterUIElementLocalizerStringId id)
			{
				string value;
				return _cache.TryGetValue(id, out value) ? value : base.GetLocalizedString(id);
			}

			private string GetEnglish(FilterUIElementLocalizerStringId id)
			{
				return base.GetLocalizedString(id);
			}
		}

		private sealed class NmmLayoutLocalizer : LayoutLocalizer
		{
			private readonly Dictionary<LayoutStringId, string> _cache;

			public NmmLayoutLocalizer()
			{
				_cache = BuildCache<LayoutStringId>(LayoutPrefix, GetEnglish);
			}

			public override string Language
			{
				get { return ActiveLanguage; }
			}

			public override string GetLocalizedString(LayoutStringId id)
			{
				string value;
				return _cache.TryGetValue(id, out value) ? value : base.GetLocalizedString(id);
			}

			private string GetEnglish(LayoutStringId id)
			{
				return base.GetLocalizedString(id);
			}
		}
	}
}
