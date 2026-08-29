namespace Nexus.Client.ModManagement.UI
{
	using System;
	using System.Collections.Generic;
	using System.Windows.Forms;

	using Nexus.Client.Mods;

	/// <summary>
	/// Common frontend contract for a Mods list surface. The Mod Manager owns commands,
	/// dialogs and ViewModel subscriptions; concrete surfaces only expose presentation-
	/// level selection, filtering and refresh operations.
	/// </summary>
	internal interface IModListSurface
	{
		/// <summary>Gets the WinForms control hosted by the Mod Manager.</summary>
		Control ViewControl { get; }

		/// <summary>Gets the currently focused mod, or <c>null</c>.</summary>
		IMod FocusedMod { get; }

		/// <summary>Gets the currently selected mods.</summary>
		IList<IMod> SelectedMods { get; }

		/// <summary>Raised when the focused or selected mods change.</summary>
		event EventHandler SelectionChanged;

		/// <summary>Rebuilds the surface from the supplied Mods collection.</summary>
		void SetMods(IEnumerable<IMod> mods);

		/// <summary>Adds mods without requiring a full surface rebuild.</summary>
		void AddMods(IEnumerable<IMod> mods);

		/// <summary>Removes mods without requiring a full surface rebuild.</summary>
		void RemoveMods(IEnumerable<IMod> mods);

		/// <summary>Refreshes one mod after a property/state change.</summary>
		void RefreshMod(IMod mod, string propertyName);

		/// <summary>Applies the shared Mod Manager text search to the surface.</summary>
		void ApplyTextFilter(string filter);

		/// <summary>Focuses and selects a mod when it is present in the surface.</summary>
		void FocusMod(IMod mod);

		/// <summary>Rebinds the surface to changes in the backing mod collection.</summary>
		void RefreshDataSource();

		/// <summary>Refreshes displayed values without changing collection membership.</summary>
		void RefreshData();

		/// <summary>Invalidates all rendered mod rows.</summary>
		void InvalidateRows();

		/// <summary>Invalidates the complete surface.</summary>
		void InvalidateView();

		/// <summary>Invalidates the rendered row/node for a specific mod.</summary>
		void InvalidateMod(IMod mod);
	}

	/// <summary>
	/// Optional capabilities exposed by the hierarchical Category View surface.
	/// </summary>
	internal interface IModCategorySurface : IModListSurface
	{
		/// <summary>
		/// Collapses every category node in the hierarchical view.
		/// </summary>
		void CollapseAllCategories();
		/// <summary>
		/// Expands every category node in the hierarchical view.
		/// </summary>
		void ExpandAllCategories();
		/// <summary>
		/// Gets the category names whose nodes are currently collapsed.
		/// </summary>
		IList<string> GetCollapsedCategoryNames();
		/// <summary>
		/// Restores category expansion state from a persisted set of collapsed names.
		/// </summary>
		void RestoreCollapsedCategories(IEnumerable<string> categoryNames);
	}
}
